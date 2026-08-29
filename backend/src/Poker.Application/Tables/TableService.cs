using Poker.Application.Abstractions;
using Poker.Application.Wallet;
using Poker.Domain.Betting;
using Poker.GameEngine.Hands;

namespace Poker.Application.Tables;

/// <summary>
/// Use-case orchestrator for table lifecycle: creation, spectating, seating, waitlisting,
/// queued rebuys, leaving, and driving hands forward as players act. Every mutation acquires
/// a per-table lock, loads the latest state, mutates it, then saves — safe for a
/// Redis-backed <see cref="ITableRepository"/> shared across API instances.
/// </summary>
public sealed class TableService(
    ITableRepository repo,
    IActiveTableTracker activeTables,
    IDistributedLock distributedLock,
    WalletService wallet)
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    public async Task<TableState> CreateTableAsync(TableConfig config)
    {
        var table = new TableState(config);
        await repo.SaveAsync(table);
        return table;
    }

    public Task<IReadOnlyList<TableState>> ListTablesAsync() => repo.ListAsync();

    public Task<TableState?> GetTableAsync(Guid tableId) => repo.GetAsync(tableId);

    public async Task JoinAsSpectatorAsync(Guid tableId, string playerId)
    {
        await MutateAsync(tableId, async table =>
        {
            if (table.FindSeat(playerId) is not null)
            {
                return; // already seated, nothing to do
            }

            if (!await activeTables.TrySetActiveTableAsync(playerId, tableId))
            {
                throw new InvalidOperationException("This account is already active at another table.");
            }

            table.Spectators.Add(playerId);
        });
    }

    public async Task SitAsync(Guid tableId, string playerId, int buyInChips)
    {
        await MutateAsync(tableId, async table =>
        {
            if (buyInChips < table.Config.MinBuyIn || buyInChips > table.Config.MaxBuyIn)
            {
                throw new InvalidOperationException(
                    $"Buy-in must be between {table.Config.MinBuyIn} and {table.Config.MaxBuyIn}.");
            }

            if (table.FindSeat(playerId) is not null)
            {
                throw new InvalidOperationException("Already seated at this table.");
            }

            if (!await activeTables.TrySetActiveTableAsync(playerId, tableId))
            {
                throw new InvalidOperationException("This account is already active at another table.");
            }

            table.Spectators.Remove(playerId);

            var seat = table.FirstOpenSeat();
            if (seat is null)
            {
                if (table.Waitlist.All(w => w.PlayerId != playerId))
                {
                    table.Waitlist.Add(new WaitlistEntry(playerId, buyInChips));
                }
                return;
            }

            await DebitBuyInAsync(table, playerId, buyInChips);
            seat.PlayerId = playerId;
            seat.Stack = buyInChips;
        });
    }

    public async Task RequestRebuyAsync(Guid tableId, string playerId, int additionalChips)
    {
        if (additionalChips <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(additionalChips));
        }

        await MutateAsync(tableId, async table =>
        {
            var seat = table.FindSeat(playerId) ?? throw new InvalidOperationException("Not seated at this table.");

            if (seat.Stack + seat.PendingRebuyChips + additionalChips > table.Config.MaxBuyIn)
            {
                throw new InvalidOperationException("This rebuy would exceed the table's max buy-in.");
            }

            await DebitBuyInAsync(table, playerId, additionalChips);
            seat.PendingRebuyChips += additionalChips;

            // Only effective immediately if no round is in progress; otherwise applied at the next hand boundary.
            if (table.CurrentHand is null)
            {
                seat.Stack += seat.PendingRebuyChips;
                seat.PendingRebuyChips = 0;
            }
        });
    }

    public async Task LeaveAsync(Guid tableId, string playerId)
    {
        await MutateAsync(tableId, async table =>
        {
            var seat = table.FindSeat(playerId);
            if (seat is not null)
            {
                bool inHand = table.CurrentHand is { Result: null } hand &&
                              hand.Players.Any(p => p.PlayerId == playerId && !p.IsFolded);
                if (inHand)
                {
                    throw new InvalidOperationException("Fold before leaving a hand you're still in.");
                }

                int cashOut = seat.Stack;
                seat.PlayerId = null;
                seat.Stack = 0;
                seat.PendingRebuyChips = 0;

                if (table.Config.IsPrivate && !table.Config.UseRealBankroll)
                {
                    await wallet.CreditPrivateTableCashOutAsync(playerId, cashOut, tableId);
                }
                else
                {
                    await wallet.CreditCashOutAsync(playerId, cashOut, tableId);
                }

                await PromoteFromWaitlistAsync(table);
            }
            else
            {
                table.Spectators.Remove(playerId);
                table.Waitlist.RemoveAll(w => w.PlayerId == playerId);
            }

            await activeTables.ClearActiveTableAsync(playerId);
        });
    }

    /// <summary>Starts a new hand if enough seated players have chips and no hand is currently running.</summary>
    public async Task<bool> TryStartHandAsync(Guid tableId)
    {
        bool started = false;
        await MutateAsync(tableId, table =>
        {
            started = TryStartHand(table);
            return Task.CompletedTask;
        });
        return started;
    }

    public async Task ApplyPlayerActionAsync(Guid tableId, string playerId, BettingActionType action, int amount = 0)
    {
        await MutateAsync(tableId, table =>
        {
            var hand = table.CurrentHand ?? throw new InvalidOperationException("No hand is in progress.");
            hand.Act(playerId, action, amount);
            hand.TryAdvance();

            if (hand.Result is not null)
            {
                foreach (var p in hand.Players)
                {
                    var seat = table.FindSeat(p.PlayerId);
                    if (seat is not null)
                    {
                        seat.Stack = p.Stack;
                    }
                }
                // Deliberately leave the finished HandEngine (with its populated Result) in place
                // rather than clearing it here, so the showdown/pot result is still visible to a
                // broadcast taken right after this call. TryStartHand replaces it once the next
                // hand actually begins.
                table.Status = TableStatus.WaitingForPlayers;

                // Queued rebuys are only effective once the round they were requested during has ended.
                foreach (var seat in table.Seats.Where(s => !s.IsEmpty && s.PendingRebuyChips > 0))
                {
                    seat.Stack += seat.PendingRebuyChips;
                    seat.PendingRebuyChips = 0;
                }
            }

            return Task.CompletedTask;
        });
    }

    private static bool TryStartHand(TableState table)
    {
        if (!table.CanStartHand)
        {
            return false;
        }

        foreach (var seat in table.Seats.Where(s => !s.IsEmpty))
        {
            seat.Stack += seat.PendingRebuyChips;
            seat.PendingRebuyChips = 0;
        }

        table.ButtonSeatIndex = NextOccupiedSeatIndex(table, table.ButtonSeatIndex);
        var order = table.ActiveSeatsFromButton(table.ButtonSeatIndex);
        if (order.Count < table.Config.MinPlayersToStart)
        {
            return false;
        }

        var players = order.Select(s => new PlayerBetState { PlayerId = s.PlayerId!, Stack = s.Stack }).ToList();
        table.CurrentHand = new HandEngine(players, table.Config.SmallBlind, table.Config.BigBlind);
        table.Status = TableStatus.Playing;
        return true;
    }

    private static int NextOccupiedSeatIndex(TableState table, int fromIndex)
    {
        for (int step = 1; step <= table.Config.MaxSeats; step++)
        {
            int idx = ((fromIndex + step) % table.Config.MaxSeats + table.Config.MaxSeats) % table.Config.MaxSeats;
            if (!table.Seats[idx].IsEmpty)
            {
                return idx;
            }
        }
        return fromIndex;
    }

    private async Task PromoteFromWaitlistAsync(TableState table)
    {
        while (table.Waitlist.Count > 0 && table.FirstOpenSeat() is { } seat)
        {
            var entry = table.Waitlist[0];
            table.Waitlist.RemoveAt(0);

            try
            {
                await DebitBuyInAsync(table, entry.PlayerId, entry.RequestedBuyIn);
                seat.PlayerId = entry.PlayerId;
                seat.Stack = entry.RequestedBuyIn;
            }
            catch (InvalidOperationException)
            {
                // No longer able to afford the buy-in (real-bankroll balance dropped) — skip to the next in line.
            }
        }
    }

    private Task DebitBuyInAsync(TableState table, string playerId, int amount) =>
        table.Config.IsPrivate && !table.Config.UseRealBankroll
            ? wallet.DebitPrivateTableBuyInAsync(playerId, amount, table.Config.Id)
            : wallet.DebitForBuyInAsync(playerId, amount, table.Config.Id);

    private async Task MutateAsync(Guid tableId, Func<TableState, Task> mutate)
    {
        await using var _ = await distributedLock.AcquireAsync($"table:{tableId}", LockTimeout);
        var table = await repo.GetAsync(tableId) ?? throw new InvalidOperationException("Table not found.");
        await mutate(table);
        await repo.SaveAsync(table);
    }
}
