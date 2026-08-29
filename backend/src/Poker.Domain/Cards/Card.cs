namespace Poker.Domain.Cards;

public enum Suit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades
}

public enum Rank
{
    Two = 2,
    Three = 3,
    Four = 4,
    Five = 5,
    Six = 6,
    Seven = 7,
    Eight = 8,
    Nine = 9,
    Ten = 10,
    Jack = 11,
    Queen = 12,
    King = 13,
    Ace = 14
}

public readonly record struct Card(Rank Rank, Suit Suit)
{
    public override string ToString()
    {
        var rank = Rank switch
        {
            Rank.Ten => "T",
            Rank.Jack => "J",
            Rank.Queen => "Q",
            Rank.King => "K",
            Rank.Ace => "A",
            _ => ((int)Rank).ToString()
        };
        var suit = Suit switch
        {
            Suit.Clubs => "c",
            Suit.Diamonds => "d",
            Suit.Hearts => "h",
            Suit.Spades => "s",
            _ => "?"
        };
        return rank + suit;
    }
}
