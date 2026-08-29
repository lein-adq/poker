namespace Poker.Domain.Cards;

/// <summary>Ordered so that a higher numeric value always beats a lower one.</summary>
public enum HandCategory
{
    HighCard = 0,
    OnePair = 1,
    TwoPair = 2,
    ThreeOfAKind = 3,
    Straight = 4,
    Flush = 5,
    FullHouse = 6,
    FourOfAKind = 7,
    StraightFlush = 8
}
