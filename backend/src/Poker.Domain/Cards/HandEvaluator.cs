namespace Poker.Domain.Cards;

/// <summary>
/// Evaluates the best 5-card poker hand out of 5-7 cards (hole cards + board).
/// Pure, allocation-light, no infra dependencies so it can be unit tested exhaustively.
/// </summary>
public static class HandEvaluator
{
    public static HandValue EvaluateBest(IReadOnlyList<Card> cards)
    {
        if (cards.Count < 5)
        {
            throw new ArgumentException("Need at least 5 cards to evaluate a hand.", nameof(cards));
        }

        if (cards.Count == 5)
        {
            return Evaluate5(cards);
        }

        HandValue? best = null;
        foreach (var combo in Combinations(cards, 5))
        {
            var value = Evaluate5(combo);
            if (best is null || value > best.Value)
            {
                best = value;
            }
        }
        return best!.Value;
    }

    public static HandValue Evaluate5(IReadOnlyList<Card> five)
    {
        if (five.Count != 5)
        {
            throw new ArgumentException("Exactly 5 cards required.", nameof(five));
        }

        var ranksDesc = five.Select(c => (int)c.Rank).OrderByDescending(r => r).ToList();
        bool isFlush = five.Select(c => c.Suit).Distinct().Count() == 1;

        var groups = ranksDesc
            .GroupBy(r => r)
            .Select(g => (Rank: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.Rank)
            .ToList();

        int straightHigh = GetStraightHigh(ranksDesc);
        bool isStraight = straightHigh != 0;

        if (isStraight && isFlush)
        {
            return new HandValue(HandCategory.StraightFlush, [straightHigh]);
        }

        if (groups[0].Count == 4)
        {
            int kicker = groups[1].Rank;
            return new HandValue(HandCategory.FourOfAKind, [groups[0].Rank, kicker]);
        }

        if (groups[0].Count == 3 && groups[1].Count == 2)
        {
            return new HandValue(HandCategory.FullHouse, [groups[0].Rank, groups[1].Rank]);
        }

        if (isFlush)
        {
            return new HandValue(HandCategory.Flush, ranksDesc);
        }

        if (isStraight)
        {
            return new HandValue(HandCategory.Straight, [straightHigh]);
        }

        if (groups[0].Count == 3)
        {
            var kickers = groups.Skip(1).Select(g => g.Rank).OrderByDescending(r => r).Take(2).ToList();
            return new HandValue(HandCategory.ThreeOfAKind, [groups[0].Rank, .. kickers]);
        }

        if (groups[0].Count == 2 && groups[1].Count == 2)
        {
            int highPair = Math.Max(groups[0].Rank, groups[1].Rank);
            int lowPair = Math.Min(groups[0].Rank, groups[1].Rank);
            int kicker = groups[2].Rank;
            return new HandValue(HandCategory.TwoPair, [highPair, lowPair, kicker]);
        }

        if (groups[0].Count == 2)
        {
            var kickers = groups.Skip(1).Select(g => g.Rank).OrderByDescending(r => r).Take(3).ToList();
            return new HandValue(HandCategory.OnePair, [groups[0].Rank, .. kickers]);
        }

        return new HandValue(HandCategory.HighCard, ranksDesc);
    }

    /// <summary>Returns the high card of the best straight in the 5 distinct-or-not ranks, or 0 if none. Handles the wheel (A-2-3-4-5).</summary>
    private static int GetStraightHigh(List<int> ranksDesc)
    {
        var distinct = ranksDesc.Distinct().OrderByDescending(r => r).ToList();
        if (distinct.Count < 5)
        {
            return 0;
        }

        if (distinct[0] - distinct[4] == 4)
        {
            return distinct[0];
        }

        if (distinct[0] == (int)Rank.Ace &&
            distinct[1] == 5 && distinct[2] == 4 && distinct[3] == 3 && distinct[4] == 2)
        {
            return 5;
        }

        return 0;
    }

    private static IEnumerable<List<Card>> Combinations(IReadOnlyList<Card> cards, int k)
    {
        var buffer = new List<Card>(k);
        return Recurse(0);

        IEnumerable<List<Card>> Recurse(int start)
        {
            if (buffer.Count == k)
            {
                yield return new List<Card>(buffer);
                yield break;
            }

            for (int i = start; i < cards.Count; i++)
            {
                buffer.Add(cards[i]);
                foreach (var combo in Recurse(i + 1))
                {
                    yield return combo;
                }
                buffer.RemoveAt(buffer.Count - 1);
            }
        }
    }
}
