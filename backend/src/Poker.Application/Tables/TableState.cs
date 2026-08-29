using Poker.Domain.Betting;
using Poker.GameEngine.Hands;

namespace Poker.Application.Tables;

public enum TableStatus
{
    WaitingForPlayers,
    Playing
}

public sealed class Seat
{
    public required int Index { get; init; }
    public string? PlayerId { get; set; }
    public int Stack { get; set; }

    /// <summary>Chips requested via a top-up while a hand is in progress; applied once the hand ends.</summary>
    public int PendingRebuyChips { get; set; }

    /// <summary>
    /// Set when the player's last connection to the table drops. They keep their seat and their chips and
    /// still get the full action clock for any decision already in front of them — a brief network blip
    /// should not fold a live hand — but they are skipped when the next hand is dealt until they return.
    /// </summary>
    public bool IsSittingOut { get; set; }

    public DateTimeOffset? DisconnectedAtUtc { get; set; }

    public bool IsEmpty => PlayerId is null;
}

/// <summary>
/// The full live state of one table: seats, spectators, waitlist, and the in-progress hand (if any).
/// Held in memory by <see cref="TableService"/> and persisted/locked via <see cref="ITableRepository"/>
/// and <see cref="Poker.Application.Abstractions.IDistributedLock"/> so it's safe under concurrent access.
/// </summary>
public sealed class TableState
{
    public TableConfig Config { get; }
    public Seat[] Seats { get; }
    public HashSet<string> Spectators { get; } = [];
    public List<WaitlistEntry> Waitlist { get; } = [];
    public TableStatus Status { get; set; } = TableStatus.WaitingForPlayers;
    public int ButtonSeatIndex { get; set; } = -1;

    public HandEngine? CurrentHand { get; set; }

    /// <summary>
    /// When whoever is to act must have acted by. Enforced server-side by the table ticker, which acts
    /// for them on expiry: without it, one player closing their laptop mid-hand freezes the table for
    /// everyone else, permanently.
    /// </summary>
    public DateTimeOffset? ActionDeadlineUtc { get; set; }

    /// <summary>
    /// When the next hand is due to be dealt. The pause after a finished hand exists so clients can render
    /// the showdown; owning it here rather than sleeping inside a hub call means the next hand still gets
    /// dealt when the player whose action ended the previous one disconnects during the pause.
    /// </summary>
    public DateTimeOffset? NextHandStartUtc { get; set; }

    /// <summary>
    /// The seat index each player occupied when the current hand was dealt. The hand engine only knows
    /// player ids, so this is what lets <see cref="SyncSeatStacksFromHand"/> refuse to write a stack back
    /// into a seat its player has since left, or that somebody else now occupies.
    /// </summary>
    public Dictionary<string, int> HandSeatIndexByPlayerId { get; } = [];

    public TableState(TableConfig config)
    {
        config.Validate();
        Config = config;
        Seats = Enumerable.Range(0, config.MaxSeats).Select(i => new Seat { Index = i }).ToArray();
    }

    public int SeatedPlayerCount => Seats.Count(s => !s.IsEmpty);
    public bool IsFull => SeatedPlayerCount >= Config.MaxSeats;
    /// <summary>Seated players who could actually be dealt in: they have chips and are not sitting out.</summary>
    public int ActivePlayerCount => Seats.Count(s => !s.IsEmpty && s.Stack > 0 && !s.IsSittingOut);

    public bool CanStartHand =>
        (CurrentHand is null || CurrentHand.Result is not null) && ActivePlayerCount >= Config.MinPlayersToStart;

    public Seat? FindSeat(string playerId) => Seats.FirstOrDefault(s => s.PlayerId == playerId);
    public Seat? FirstOpenSeat() => Seats.FirstOrDefault(s => s.IsEmpty);

    /// <summary>
    /// Copies live stacks out of the in-progress hand back onto the seats. The hand engine owns the
    /// authoritative stack for as long as a hand runs, so this must be called after every mutation of it
    /// — otherwise anything reading <see cref="Seat.Stack"/> mid-hand (cashing out on leave, the rebuy
    /// max-buy-in check, the next hand's seat ordering) sees the stack as it was before the hand started.
    /// </summary>
    public void SyncSeatStacksFromHand()
    {
        if (CurrentHand is null)
        {
            return;
        }

        foreach (var p in CurrentHand.Players)
        {
            if (HandSeatIndexByPlayerId.TryGetValue(p.PlayerId, out int seatIndex) &&
                Seats[seatIndex].PlayerId == p.PlayerId)
            {
                Seats[seatIndex].Stack = p.Stack;
            }
        }
    }

    /// <summary>Seats that get dealt in, in seat order starting just after the given button index.</summary>
    public List<Seat> ActiveSeatsFromButton(int buttonIndex)
    {
        var occupied = Seats.Where(s => !s.IsEmpty && s.Stack > 0 && !s.IsSittingOut).ToList();
        if (occupied.Count == 0)
        {
            return occupied;
        }

        return occupied
            .OrderBy(s => (s.Index - buttonIndex - 1 + Config.MaxSeats * 2) % Config.MaxSeats)
            .ToList();
    }
}
