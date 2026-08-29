namespace Poker.Domain.Cards;

/// <summary>
/// The ranking of a specific 5-card hand: a category plus tiebreaker ranks in
/// descending significance (e.g. full house => [tripsRank, pairRank]).
/// Comparable so two hands can be ranked directly against each other.
/// </summary>
public readonly struct HandValue : IComparable<HandValue>
{
    public HandCategory Category { get; }
    public IReadOnlyList<int> Tiebreakers { get; }

    public HandValue(HandCategory category, IReadOnlyList<int> tiebreakers)
    {
        Category = category;
        Tiebreakers = tiebreakers;
    }

    public int CompareTo(HandValue other)
    {
        if (Category != other.Category)
        {
            return Category.CompareTo(other.Category);
        }

        int len = Math.Min(Tiebreakers.Count, other.Tiebreakers.Count);
        for (int i = 0; i < len; i++)
        {
            int c = Tiebreakers[i].CompareTo(other.Tiebreakers[i]);
            if (c != 0)
            {
                return c;
            }
        }
        return 0;
    }

    public static bool operator >(HandValue a, HandValue b) => a.CompareTo(b) > 0;
    public static bool operator <(HandValue a, HandValue b) => a.CompareTo(b) < 0;
    public static bool operator >=(HandValue a, HandValue b) => a.CompareTo(b) >= 0;
    public static bool operator <=(HandValue a, HandValue b) => a.CompareTo(b) <= 0;
    public static bool operator ==(HandValue a, HandValue b) => a.CompareTo(b) == 0;
    public static bool operator !=(HandValue a, HandValue b) => a.CompareTo(b) != 0;

    public override bool Equals(object? obj) => obj is HandValue other && CompareTo(other) == 0;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Category);
        foreach (var t in Tiebreakers)
        {
            hash.Add(t);
        }
        return hash.ToHashCode();
    }

    public string Describe() => Category switch
    {
        HandCategory.StraightFlush => Tiebreakers[0] == (int)Rank.Ace ? "Royal Flush" : "Straight Flush",
        HandCategory.FourOfAKind => "Four of a Kind",
        HandCategory.FullHouse => "Full House",
        HandCategory.Flush => "Flush",
        HandCategory.Straight => "Straight",
        HandCategory.ThreeOfAKind => "Three of a Kind",
        HandCategory.TwoPair => "Two Pair",
        HandCategory.OnePair => "Pair",
        _ => "High Card"
    };
}
