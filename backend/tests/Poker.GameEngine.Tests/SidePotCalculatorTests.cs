using Poker.Domain.Betting;
using Xunit;

namespace Poker.GameEngine.Tests;

public class SidePotCalculatorTests
{
    [Fact]
    public void NoFolds_NoSidePots_SinglePotEveryoneEligible()
    {
        var pots = SidePotCalculator.Calculate([
            new PlayerContribution("A", 300, false),
            new PlayerContribution("B", 300, false),
        ]);

        var pot = Assert.Single(pots);
        Assert.Equal(600, pot.Amount);
        Assert.Equal(["A", "B"], pot.EligiblePlayerIds.OrderBy(x => x));
    }

    [Fact]
    public void FoldedPlayerChipsStayInPot_ButTheyAreNotEligible()
    {
        var pots = SidePotCalculator.Calculate([
            new PlayerContribution("A", 300, false),
            new PlayerContribution("B", 300, false),
            new PlayerContribution("C", 100, true),
        ]);

        var pot = Assert.Single(pots);
        Assert.Equal(700, pot.Amount);
        Assert.Equal(["A", "B"], pot.EligiblePlayerIds.OrderBy(x => x));
    }

    [Fact]
    public void ShortStackAllIn_CreatesMainPotAndSidePot()
    {
        // A is all-in for 100. B and C put in 300 each.
        var pots = SidePotCalculator.Calculate([
            new PlayerContribution("A", 100, false),
            new PlayerContribution("B", 300, false),
            new PlayerContribution("C", 300, false),
        ]);

        Assert.Equal(2, pots.Count);

        var main = pots[0];
        Assert.Equal(300, main.Amount); // 100 * 3 contributors
        Assert.Equal(["A", "B", "C"], main.EligiblePlayerIds.OrderBy(x => x));

        var side = pots[1];
        Assert.Equal(400, side.Amount); // 200 * 2 contributors
        Assert.Equal(["B", "C"], side.EligiblePlayerIds.OrderBy(x => x));

        Assert.Equal(700, pots.Sum(p => p.Amount));
    }

    [Fact]
    public void MultipleShortStacks_ProduceLayeredSidePots()
    {
        var pots = SidePotCalculator.Calculate([
            new PlayerContribution("A", 50, false),
            new PlayerContribution("B", 150, false),
            new PlayerContribution("C", 400, false),
            new PlayerContribution("D", 400, false),
        ]);

        Assert.Equal(1000, pots.Sum(p => p.Amount));
        Assert.Equal(3, pots.Count);
        Assert.Equal(200, pots[0].Amount); // 50 * 4
        Assert.Equal(300, pots[1].Amount); // 100 * 3
        Assert.Equal(500, pots[2].Amount); // 250 * 2
        Assert.Equal(["C", "D"], pots[2].EligiblePlayerIds.OrderBy(x => x));
    }
}
