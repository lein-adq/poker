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

    public TableState(TableConfig config)
    {
        config.Validate();
        Config = config;
        Seats = Enumerable.Range(0, config.MaxSeats).Select(i => new Seat { Index = i }).ToArray();
    }

    public int SeatedPlayerCount => Seats.Count(s => !s.IsEmpty);
    public bool IsFull => SeatedPlayerCount >= Config.MaxSeats;
    public bool CanStartHand =>
        (CurrentHand is null || CurrentHand.Result is not null) && SeatedPlayerCount >= Config.MinPlayersToStart;

    public Seat? FindSeat(string playerId) => Seats.FirstOrDefault(s => s.PlayerId == playerId);
    public Seat? FirstOpenSeat() => Seats.FirstOrDefault(s => s.IsEmpty);

    /// <summary>Seats with chips, in seat order, starting just after the given button index — the order a hand deals to.</summary>
    public List<Seat> ActiveSeatsFromButton(int buttonIndex)
    {
        var occupied = Seats.Where(s => !s.IsEmpty && s.Stack > 0).ToList();
        if (occupied.Count == 0)
        {
            return occupied;
        }

        return occupied
            .OrderBy(s => (s.Index - buttonIndex - 1 + Config.MaxSeats * 2) % Config.MaxSeats)
            .ToList();
    }
}
