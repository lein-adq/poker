using Poker.Domain.Cards;
using Xunit;

namespace Poker.GameEngine.Tests;

public class HandEvaluatorTests
{
    private static Card C(string s)
    {
        var rank = s[0] switch
        {
            'T' => Rank.Ten,
            'J' => Rank.Jack,
            'Q' => Rank.Queen,
            'K' => Rank.King,
            'A' => Rank.Ace,
            _ => (Rank)(s[0] - '0')
        };
        var suit = s[1] switch
        {
            'c' => Suit.Clubs,
            'd' => Suit.Diamonds,
            'h' => Suit.Hearts,
            's' => Suit.Spades,
            _ => throw new ArgumentException(s)
        };
        return new Card(rank, suit);
    }

    private static List<Card> Cards(params string[] s) => s.Select(C).ToList();

    [Fact]
    public void RoyalFlush_IsHighestCategory()
    {
        var hand = HandEvaluator.Evaluate5(Cards("Ts", "Js", "Qs", "Ks", "As"));
        Assert.Equal(HandCategory.StraightFlush, hand.Category);
        Assert.Equal("Royal Flush", hand.Describe());
    }

    [Fact]
    public void WheelStraight_AceCountsLow()
    {
        var hand = HandEvaluator.Evaluate5(Cards("Ac", "2d", "3h", "4s", "5c"));
        Assert.Equal(HandCategory.Straight, hand.Category);
        Assert.Equal(5, hand.Tiebreakers[0]);
    }

    [Fact]
    public void FourOfAKind_BeatsFullHouse()
    {
        var quads = HandEvaluator.Evaluate5(Cards("9c", "9d", "9h", "9s", "2c"));
        var boat = HandEvaluator.Evaluate5(Cards("Kc", "Kd", "Kh", "2s", "2c"));
        Assert.True(quads > boat);
    }

    [Fact]
    public void FullHouse_BeatsFlush()
    {
        var boat = HandEvaluator.Evaluate5(Cards("Kc", "Kd", "Kh", "2s", "2c"));
        var flush = HandEvaluator.Evaluate5(Cards("2c", "5c", "8c", "Jc", "Ac"));
        Assert.True(boat > flush);
    }

    [Fact]
    public void TwoPair_HigherTopPairWins()
    {
        var a = HandEvaluator.Evaluate5(Cards("Ac", "Ad", "2h", "2s", "9c"));
        var b = HandEvaluator.Evaluate5(Cards("Kc", "Kd", "Qh", "Qs", "9d"));
        Assert.True(a > b);
    }

    [Fact]
    public void OnePair_KickerBreaksTie()
    {
        var a = HandEvaluator.Evaluate5(Cards("Ac", "Ad", "Kh", "Qs", "2c"));
        var b = HandEvaluator.Evaluate5(Cards("Ah", "As", "Kd", "Jc", "2d"));
        Assert.True(a > b);
    }

    [Fact]
    public void EvaluateBest_PicksBestOfSevenCards()
    {
        // Hole: As Ks. Board: Ah Ad 2c 2d Qc -> best 5 is full house AAA22.
        var seven = Cards("As", "Ks", "Ah", "Ad", "2c", "2d", "Qc");
        var best = HandEvaluator.EvaluateBest(seven);
        Assert.Equal(HandCategory.FullHouse, best.Category);
        Assert.Equal((int)Rank.Ace, best.Tiebreakers[0]);
        Assert.Equal(2, best.Tiebreakers[1]);
    }

    [Fact]
    public void HighCard_ComparesAllFiveRanks()
    {
        var a = HandEvaluator.Evaluate5(Cards("Ac", "Kd", "9h", "5s", "2c"));
        var b = HandEvaluator.Evaluate5(Cards("Ac", "Kd", "9h", "5s", "3c"));
        Assert.True(b > a);
    }
}
