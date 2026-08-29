using Poker.Domain.Betting;
using Poker.Domain.Cards;
using Poker.GameEngine.Equity;

namespace Poker.GameEngine.Hands;

public enum Street
{
    Preflop,
    Flop,
    Turn,
    River,
    Showdown
}

public sealed record PotResult(int Amount, IReadOnlyList<string> WinnerPlayerIds, IReadOnlyList<string> EligiblePlayerIds);

public sealed record HandResult(IReadOnlyList<PotResult> Pots);

/// <summary>
/// Orchestrates one hand of Texas Hold'em from deal to showdown: posts blinds, sequences betting
/// rounds across streets via <see cref="BettingRound"/>, auto-runs the board when all remaining
/// players are all-in, and settles pots via <see cref="SidePotCalculator"/> at the end.
/// </summary>
public sealed class HandEngine
{
    private readonly Deck _deck = new();
    private readonly List<PlayerBetState> _players;
    private readonly Dictionary<string, List<Card>> _holeCards = new();
    private readonly List<Card> _board = new();
    private readonly int _bigBlind;

    public Street CurrentStreet { get; private set; } = Street.Preflop;
    public BettingRound CurrentBettingRound { get; private set; }
    public HandResult? Result { get; private set; }

    public IReadOnlyList<Card> Board => _board;
    public IReadOnlyDictionary<string, List<Card>> HoleCards => _holeCards;
    public IReadOnlyList<PlayerBetState> Players => _players;

    /// <param name="seatOrderPlayers">
    /// All players still in the hand, in seat order starting with the small blind (index 0) then
    /// the big blind (index 1). Stacks must already reflect chips brought to the table.
    /// </param>
    public HandEngine(List<PlayerBetState> seatOrderPlayers, int smallBlind, int bigBlind)
    {
        if (seatOrderPlayers.Count < 2)
        {
            throw new ArgumentException("A hand needs at least 2 players.", nameof(seatOrderPlayers));
        }

        _players = seatOrderPlayers;
        _bigBlind = bigBlind;

        foreach (var p in _players)
        {
            _holeCards[p.PlayerId] = _deck.Draw(2).ToList();
        }

        PostBlind(_players[0], smallBlind);
        PostBlind(_players[1], bigBlind);

        CurrentBettingRound = new BettingRound(BuildActionOrder(Street.Preflop), bigBlind, startingBet: _players[1].CommittedThisRound);
    }

    public string? CurrentActorId => CurrentBettingRound.CurrentActor?.PlayerId;

    public LegalActions GetLegalActions(string playerId) => CurrentBettingRound.GetLegalActions(playerId);

    public void Act(string playerId, BettingActionType action, int amount = 0) =>
        CurrentBettingRound.Apply(playerId, action, amount);

    /// <summary>
    /// Call after every <see cref="Act"/> once <see cref="BettingRound.IsComplete"/> is true for the
    /// current street. Deals the next street (or runs the board out automatically when the remaining
    /// players are all all-in) and settles the hand at showdown. Returns true if the hand state changed.
    /// </summary>
    public bool TryAdvance()
    {
        if (Result is not null || !CurrentBettingRound.IsComplete)
        {
            return false;
        }

        while (true)
        {
            var contenders = _players.Where(p => !p.IsFolded).ToList();
            if (contenders.Count <= 1)
            {
                Showdown();
                return true;
            }

            if (CurrentStreet == Street.River)
            {
                Showdown();
                return true;
            }

            DealNextStreetCards();
            foreach (var p in _players)
            {
                p.CommittedThisRound = 0;
            }

            bool anyoneCanStillBet = contenders.Count(p => p.Stack > 0) > 1;
            if (anyoneCanStillBet)
            {
                CurrentBettingRound = new BettingRound(BuildActionOrder(CurrentStreet), _bigBlind);
                return true;
            }
            // Everyone (or all but one) is all-in: no more betting is possible, so keep dealing
            // streets automatically until the river, then go to showdown.
        }
    }

    /// <summary>Live win/tie percentages for players still in the hand, for display during play or an all-in reveal.</summary>
    public Dictionary<string, EquityResult> ComputeLiveEquity(int trials = 5000)
    {
        var active = _holeCards
            .Where(kv => !_players.Single(p => p.PlayerId == kv.Key).IsFolded)
            .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Card>)kv.Value);
        return EquityCalculator.Calculate(active, _board, trials);
    }

    private List<PlayerBetState> BuildActionOrder(Street street)
    {
        if (_players.Count == 2)
        {
            // Heads-up: the button/small blind acts first preflop; the big blind acts first postflop.
            return street == Street.Preflop
                ? [_players[0], _players[1]]
                : [_players[1], _players[0]];
        }

        if (street == Street.Preflop)
        {
            // Under the gun (index 2) acts first, wrapping around to the small/big blind last.
            return Enumerable.Range(0, _players.Count).Select(i => _players[(2 + i) % _players.Count]).ToList();
        }

        // Postflop: first active player left of the button (the small blind seat) acts first.
        return new List<PlayerBetState>(_players);
    }

    private void DealNextStreetCards()
    {
        switch (CurrentStreet)
        {
            case Street.Preflop:
                _board.AddRange(_deck.Draw(3));
                CurrentStreet = Street.Flop;
                break;
            case Street.Flop:
                _board.AddRange(_deck.Draw(1));
                CurrentStreet = Street.Turn;
                break;
            case Street.Turn:
                _board.AddRange(_deck.Draw(1));
                CurrentStreet = Street.River;
                break;
        }
    }

    private void Showdown()
    {
        var contributions = _players
            .Select(p => new PlayerContribution(p.PlayerId, p.CommittedTotal, p.IsFolded))
            .ToList();
        var pots = SidePotCalculator.Calculate(contributions);

        var potResults = new List<PotResult>();
        foreach (var pot in pots)
        {
            if (pot.EligiblePlayerIds.Count == 1)
            {
                CreditPlayer(pot.EligiblePlayerIds[0], pot.Amount);
                potResults.Add(new PotResult(pot.Amount, pot.EligiblePlayerIds, pot.EligiblePlayerIds));
                continue;
            }

            while (_board.Count < 5)
            {
                _board.AddRange(_deck.Draw(1));
            }

            HandValue? best = null;
            var winners = new List<string>();
            foreach (var playerId in pot.EligiblePlayerIds)
            {
                var value = HandEvaluator.EvaluateBest(_holeCards[playerId].Concat(_board).ToList());
                if (best is null || value > best.Value)
                {
                    best = value;
                    winners.Clear();
                    winners.Add(playerId);
                }
                else if (value == best.Value)
                {
                    winners.Add(playerId);
                }
            }

            int share = pot.Amount / winners.Count;
            int remainder = pot.Amount % winners.Count;
            foreach (var w in winners)
            {
                CreditPlayer(w, share);
            }
            if (remainder > 0)
            {
                // Odd-chip simplification: awarded to the first eligible winner rather than the
                // seat closest left of the button. Acceptable for an MVP.
                CreditPlayer(winners[0], remainder);
            }

            potResults.Add(new PotResult(pot.Amount, winners, pot.EligiblePlayerIds));
        }

        Result = new HandResult(potResults);
        CurrentStreet = Street.Showdown;
    }

    private void CreditPlayer(string playerId, int amount) =>
        _players.Single(p => p.PlayerId == playerId).Stack += amount;

    private static void PostBlind(PlayerBetState p, int amount)
    {
        int posted = Math.Min(amount, p.Stack);
        p.Stack -= posted;
        p.CommittedThisRound += posted;
        p.CommittedTotal += posted;
    }
}
