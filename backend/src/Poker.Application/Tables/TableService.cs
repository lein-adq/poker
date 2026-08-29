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
    WalletService wallet,
    IClock clock)
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long a player has to act before the table acts for them.</summary>
    public static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Pause between a hand finishing and the next being dealt, so clients can show the showdown.</summary>
    public static readonly TimeSpan NextHandDelay = TimeSpan.FromSeconds(3);

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
            if (table.FindSeat(playerId) is { } seatedAlready)
            {
                // A seated player rejoining is a reconnect: clear the sit-out their dropped connection set.
                seatedAlready.IsSittingOut = false;
                seatedAlready.DisconnectedAtUtc = null;
                return;
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

                // Pending rebuy chips were already debited from the wallet when the top-up was
                // requested, so they must be cashed out too rather than silently zeroed.
                int cashOut = seat.Stack + seat.PendingRebuyChips;
                seat.PlayerId = null;
                seat.Stack = 0;
                seat.PendingRebuyChips = 0;

                // Drop them from the live hand's seat map: they may buy back in during this same hand
                // and land in the very seat they just vacated, and their now-stale in-hand stack must
                // not be written over that fresh buy-in.
                table.HandSeatIndexByPlayerId.Remove(playerId);

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
            AfterAction(table, hand);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// The shared tail of every action, whether a player took it or their clock ran out and the table took
    /// it for them: advance the hand, push stacks back to the seats, and arm the next deadline.
    /// </summary>
    private void AfterAction(TableState table, HandEngine hand)
    {
        hand.TryAdvance();

        // After *every* action, not just at the end of the hand: LeaveAsync cashes out Seat.Stack
        // (a folded player may leave mid-hand) and RequestRebuyAsync caps top-ups against it, so a
        // seat stack left frozen at its pre-hand value hands out chips that are still in the pot.
        table.SyncSeatStacksFromHand();

        if (hand.Result is not null)
        {
            // Deliberately leave the finished HandEngine (with its populated Result) in place
            // rather than clearing it here, so the showdown/pot result is still visible to a
            // broadcast taken right after this call. TryStartHand replaces it once the next
            // hand actually begins.
            table.Status = TableStatus.WaitingForPlayers;
            table.ActionDeadlineUtc = null;
            table.NextHandStartUtc = clock.UtcNow + NextHandDelay;

            // Queued rebuys are only effective once the round they were requested during has ended.
            foreach (var seat in table.Seats.Where(s => !s.IsEmpty && s.PendingRebuyChips > 0))
            {
                seat.Stack += seat.PendingRebuyChips;
                seat.PendingRebuyChips = 0;
            }
        }
        else
        {
            table.ActionDeadlineUtc = clock.UtcNow + ActionTimeout;
        }
    }

    /// <summary>
    /// The server-side heartbeat for one table: acts for a player whose clock has run out, and deals the
    /// next hand once the post-showdown pause is over. Returns true if anything changed and viewers need
    /// a fresh broadcast. Driven by the API's table ticker, never by a client.
    /// </summary>
    public async Task<bool> TickAsync(Guid tableId)
    {
        // Unlocked pre-check first: the ticker sweeps every table on every tick and almost none of them
        // have anything due, so don't pay for a distributed lock just to find that out.
        var snapshot = await repo.GetAsync(tableId);
        if (snapshot is null || !IsWorkDue(snapshot, clock.UtcNow))
        {
            return false;
        }

        bool changed = false;
        await MutateAsync(tableId, table =>
        {
            // Re-checked under the lock: the player may have acted between the pre-check and here.
            int guard = 0;
            while (IsWorkDue(table, clock.UtcNow) && guard++ <= table.Config.MaxSeats)
            {
                if (table.CurrentHand is { Result: null } hand)
                {
                    if (hand.CurrentActorId is not { } actorId)
                    {
                        break;
                    }

                    // Check when it is free, otherwise fold. Never commit chips on somebody's behalf.
                    var legal = hand.GetLegalActions(actorId);
                    hand.Act(actorId, legal.CanCheck ? BettingActionType.Check : BettingActionType.Fold);
                    AfterAction(table, hand);
                }
                else
                {
                    // Cleared before the attempt: if a hand cannot start (everyone left or is sitting
                    // out) the ticker must go quiet rather than retry every tick forever. Seating a
                    // player re-arms it via TryStartHandAsync.
                    table.NextHandStartUtc = null;
                    TryStartHand(table);
                }

                changed = true;
            }

            return Task.CompletedTask;
        });

        return changed;
    }

    private static bool IsWorkDue(TableState table, DateTimeOffset now) =>
        table.CurrentHand is { Result: null }
            ? table.ActionDeadlineUtc is { } deadline && now >= deadline
            : table.NextHandStartUtc is { } startAt && now >= startAt;

    /// <summary>
    /// Called when a player's last connection to the table drops. A seated player keeps their seat and
    /// their chips and keeps the full clock on any decision already in front of them, but is skipped when
    /// the next hand is dealt until they come back. A spectator is dropped outright, which also releases
    /// the one-active-table slot they were holding.
    /// </summary>
    public async Task MarkDisconnectedAsync(Guid tableId, string playerId)
    {
        bool releasedSlot = false;

        await MutateAsync(tableId, table =>
        {
            var seat = table.FindSeat(playerId);
            if (seat is not null)
            {
                seat.IsSittingOut = true;
                seat.DisconnectedAtUtc = clock.UtcNow;
            }
            else
            {
                table.Spectators.Remove(playerId);
                table.Waitlist.RemoveAll(w => w.PlayerId == playerId);
                releasedSlot = true;
            }

            return Task.CompletedTask;
        });

        if (releasedSlot)
        {
            await activeTables.ClearActiveTableAsync(playerId);
        }
    }

    private bool TryStartHand(TableState table)
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

        table.HandSeatIndexByPlayerId.Clear();
        foreach (var seat in order)
        {
            table.HandSeatIndexByPlayerId[seat.PlayerId!] = seat.Index;
        }

        // The blinds are posted inside the HandEngine constructor, so the seats are already behind.
        table.SyncSeatStacksFromHand();

        table.NextHandStartUtc = null;
        table.ActionDeadlineUtc = clock.UtcNow + ActionTimeout;
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
