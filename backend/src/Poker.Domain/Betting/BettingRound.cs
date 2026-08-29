namespace Poker.Domain.Betting;

public enum BettingActionType
{
    Fold,
    Check,
    Call,
    Bet,
    Raise
}

public sealed class PlayerBetState
{
    public required string PlayerId { get; init; }
    public int Stack { get; set; }
    public int CommittedThisRound { get; set; }
    public int CommittedTotal { get; set; }
    public bool IsFolded { get; set; }
    public bool IsAllIn => Stack == 0 && !IsFolded;
}

public readonly record struct LegalActions(bool CanCheck, bool CanCall, int CallAmount, int MinRaiseTo, int MaxRaiseTo);

/// <summary>
/// Drives a single street of betting (preflop/flop/turn/river) for the players still in the hand.
/// Callers order <paramref name="playersInTurnOrder"/> starting with whoever acts first on this street
/// and pre-commit any forced bets (blinds) into <see cref="PlayerBetState.CommittedThisRound"/> before construction.
/// </summary>
/// <remarks>
/// Simplification: a short (less-than-minimum) all-in raise does not reopen action for players who already
/// called the previous full raise, per standard cardroom rules — this engine currently always reopens action
/// on any bet increase. Acceptable for an MVP; revisit if strict rule accuracy becomes a requirement.
/// </remarks>
public sealed class BettingRound
{
    private readonly List<PlayerBetState> _players;
    private readonly HashSet<string> _actedSinceLastRaise = new();
    private int _currentBet;
    private int _minRaiseIncrement;
    private int _actorIndex;

    public BettingRound(List<PlayerBetState> playersInTurnOrder, int bigBlind, int startingBet = 0)
    {
        if (playersInTurnOrder.Count == 0)
        {
            throw new ArgumentException("At least one player is required.", nameof(playersInTurnOrder));
        }

        _players = playersInTurnOrder;
        _currentBet = startingBet;
        _minRaiseIncrement = bigBlind;
        _actorIndex = FindNextActor(-1);
    }

    public bool IsComplete
    {
        get
        {
            var contenders = _players.Where(p => !p.IsFolded).ToList();
            if (contenders.Count <= 1)
            {
                return true;
            }

            foreach (var p in contenders.Where(p => p.Stack > 0))
            {
                if (p.CommittedThisRound != _currentBet || !_actedSinceLastRaise.Contains(p.PlayerId))
                {
                    return false;
                }
            }
            return true;
        }
    }

    public PlayerBetState? CurrentActor => IsComplete ? null : _players[_actorIndex];

    public LegalActions GetLegalActions(string playerId)
    {
        var p = _players.Single(pl => pl.PlayerId == playerId);
        int callAmount = Math.Min(_currentBet - p.CommittedThisRound, p.Stack);
        bool canCheck = _currentBet == p.CommittedThisRound;
        bool canCall = !canCheck && callAmount > 0;
        int maxRaiseTo = p.CommittedThisRound + p.Stack;
        int minRaiseTo = Math.Min(_currentBet + _minRaiseIncrement, maxRaiseTo);
        return new LegalActions(canCheck, canCall, callAmount, minRaiseTo, maxRaiseTo);
    }

    public void Apply(string playerId, BettingActionType action, int amount = 0)
    {
        var p = _players.Single(pl => pl.PlayerId == playerId);
        if (!ReferenceEquals(p, CurrentActor))
        {
            throw new InvalidOperationException("Not this player's turn.");
        }

        switch (action)
        {
            case BettingActionType.Fold:
                p.IsFolded = true;
                break;

            case BettingActionType.Check:
                if (p.CommittedThisRound != _currentBet)
                {
                    throw new InvalidOperationException("Cannot check while facing a bet.");
                }
                break;

            case BettingActionType.Call:
                Commit(p, Math.Min(_currentBet - p.CommittedThisRound, p.Stack));
                break;

            case BettingActionType.Bet:
            case BettingActionType.Raise:
                ApplyRaise(p, amount);
                break;
        }

        _actedSinceLastRaise.Add(playerId);
        if (!IsComplete)
        {
            _actorIndex = FindNextActor(_actorIndex);
        }
    }

    private void ApplyRaise(PlayerBetState p, int amount)
    {
        var legal = GetLegalActions(p.PlayerId);
        int raiseTo = Math.Min(amount, legal.MaxRaiseTo);
        bool isAllIn = raiseTo == legal.MaxRaiseTo;

        if (raiseTo <= _currentBet)
        {
            throw new InvalidOperationException("Raise must exceed the current bet.");
        }
        if (raiseTo < legal.MinRaiseTo && !isAllIn)
        {
            throw new InvalidOperationException($"Raise must be at least {legal.MinRaiseTo}.");
        }

        _minRaiseIncrement = Math.Max(_minRaiseIncrement, raiseTo - _currentBet);
        _currentBet = raiseTo;
        Commit(p, raiseTo - p.CommittedThisRound);
        _actedSinceLastRaise.Clear();
    }

    private static void Commit(PlayerBetState p, int amount)
    {
        amount = Math.Min(amount, p.Stack);
        p.Stack -= amount;
        p.CommittedThisRound += amount;
        p.CommittedTotal += amount;
    }

    private int FindNextActor(int fromIndex)
    {
        for (int step = 1; step <= _players.Count; step++)
        {
            int idx = (fromIndex + step) % _players.Count;
            var p = _players[idx];
            if (!p.IsFolded && p.Stack > 0)
            {
                return idx;
            }
        }
        return Math.Max(fromIndex, 0);
    }
}
