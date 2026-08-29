using Poker.Domain.Cards;
using Poker.GameEngine.Equity;
using Xunit;

namespace Poker.GameEngine.Tests;

public class EquityCalculatorTests
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

    [Fact]
    public void PocketAces_HeavyFavorite_PreflopVsRandomHand()
    {
        var hands = new Dictionary<string, IReadOnlyList<Card>>
        {
            ["aces"] = [C("Ac"), C("Ad")],
            ["random"] = [C("7c"), C("2d")],
        };

        var result = EquityCalculator.Calculate(hands, board: [], trials: 4000);

        // AA vs a random hand preflop is roughly 85% to win; allow a wide band for Monte Carlo noise.
        Assert.True(result["aces"].WinPercent > 70, $"expected aces to dominate, got {result["aces"].WinPercent}%");
    }

    [Fact]
    public void CompletedBoard_IsDeterministic_SingleTrial()
    {
        var hands = new Dictionary<string, IReadOnlyList<Card>>
        {
            ["nuts"] = [C("As"), C("Ks")],
            ["weak"] = [C("2c"), C("7d")],
        };
        var board = new List<Card> { C("Qs"), C("Js"), C("Ts"), C("2h"), C("3h") };

        var result = EquityCalculator.Calculate(hands, board, trials: 1000);

        Assert.Equal(100.0, result["nuts"].WinPercent);
        Assert.Equal(0.0, result["weak"].WinPercent);
    }

    [Fact]
    public void EquitySumsRoughlyTo100PercentAcrossPlayers_NoTies()
    {
        var hands = new Dictionary<string, IReadOnlyList<Card>>
        {
            ["a"] = [C("Ac"), C("Kd")],
            ["b"] = [C("9h"), C("9s")],
        };

        var result = EquityCalculator.Calculate(hands, board: [C("2c"), C("5d"), C("8h")], trials: 4000);
        double total = result.Values.Sum(r => r.WinPercent + r.TiePercent);
        Assert.InRange(total, 99.5, 100.5);
    }
}
