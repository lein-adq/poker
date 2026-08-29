using Poker.Domain.Betting;
using Poker.GameEngine.Hands;
using Xunit;

namespace Poker.GameEngine.Tests;

public class HandEngineTests
{
    private static PlayerBetState Player(string id, int stack) => new() { PlayerId = id, Stack = stack };

    [Fact]
    public void FullHeadsUpHand_PlaysToShowdown_AndConservesChips()
    {
        var a = Player("A", 1000);
        var b = Player("B", 1000);
        var engine = new HandEngine([a, b], smallBlind: 10, bigBlind: 20);

        // Preflop: A (SB/button) acts first heads-up.
        engine.Act("A", BettingActionType.Call);
        engine.Act("B", BettingActionType.Check);
        Assert.True(engine.TryAdvance());
        Assert.Equal(Street.Flop, engine.CurrentStreet);
        Assert.Equal(3, engine.Board.Count);

        // Postflop: B acts first heads-up.
        engine.Act("B", BettingActionType.Check);
        engine.Act("A", BettingActionType.Check);
        Assert.True(engine.TryAdvance());
        Assert.Equal(Street.Turn, engine.CurrentStreet);

        engine.Act("B", BettingActionType.Check);
        engine.Act("A", BettingActionType.Check);
        Assert.True(engine.TryAdvance());
        Assert.Equal(Street.River, engine.CurrentStreet);

        engine.Act("B", BettingActionType.Check);
        engine.Act("A", BettingActionType.Check);
        Assert.True(engine.TryAdvance());

        Assert.NotNull(engine.Result);
        Assert.Equal(Street.Showdown, engine.CurrentStreet);
        Assert.Equal(5, engine.Board.Count);
        Assert.Equal(2000, a.Stack + b.Stack); // no chips created or destroyed
    }

    [Fact]
    public void AllInPreflop_RunsBoardOutAutomatically_ToShowdown()
    {
        var a = Player("A", 500);
        var b = Player("B", 500);
        var engine = new HandEngine([a, b], smallBlind: 10, bigBlind: 20);

        engine.Act("A", BettingActionType.Raise, 500); // A shoves
        engine.Act("B", BettingActionType.Call); // B calls all-in

        Assert.True(engine.TryAdvance());

        Assert.NotNull(engine.Result);
        Assert.Equal(5, engine.Board.Count);
        Assert.Equal(1000, engine.Result!.Pots.Sum(p => p.Amount));
        Assert.Equal(1000, a.Stack + b.Stack);
    }

    [Fact]
    public void FoldPreflop_AwardsPotWithoutRevealingBoard()
    {
        var a = Player("A", 1000);
        var b = Player("B", 1000);
        var engine = new HandEngine([a, b], smallBlind: 10, bigBlind: 20);

        engine.Act("A", BettingActionType.Fold);
        Assert.True(engine.TryAdvance());

        Assert.NotNull(engine.Result);
        Assert.Empty(engine.Board);
        var pot = Assert.Single(engine.Result!.Pots);
        Assert.Equal("B", Assert.Single(pot.WinnerPlayerIds));
        Assert.Equal(2000, a.Stack + b.Stack);
    }

    [Fact]
    public void SidePotEveryoneFoldsOutOf_IsReturnedToItsContributors_NotGivenToTheAllInPlayer()
    {
        var a = Player("A", 50);   // short stack, will be all-in for 50
        var b = Player("B", 500);
        var c = Player("C", 500);
        var engine = new HandEngine([a, b, c], smallBlind: 10, bigBlind: 20);

        // Preflop: C (button) acts first three-handed.
        engine.Act("C", BettingActionType.Raise, 100);
        engine.Act("A", BettingActionType.Call);  // all-in for 50
        engine.Act("B", BettingActionType.Call);
        Assert.True(engine.TryAdvance());
        Assert.Equal(Street.Flop, engine.CurrentStreet);

        // Both live players give up the side pot rather than play it out. A is all-in and cannot act,
        // so this leaves the 100-chip side pot with nobody eligible to win it.
        engine.Act("B", BettingActionType.Fold);
        engine.Act("C", BettingActionType.Fold);
        Assert.True(engine.TryAdvance());

        Assert.NotNull(engine.Result);
        Assert.Equal(2, engine.Result!.Pots.Count);

        var main = engine.Result.Pots[0];
        Assert.Equal(150, main.Amount);
        Assert.Equal("A", Assert.Single(main.WinnerPlayerIds));

        var abandoned = engine.Result.Pots[1];
        Assert.Equal(100, abandoned.Amount);
        Assert.Empty(abandoned.EligiblePlayerIds);
        Assert.Equal(["B", "C"], abandoned.WinnerPlayerIds.OrderBy(x => x));

        Assert.Equal(150, a.Stack);  // wins only the pot they were actually eligible for
        Assert.Equal(450, b.Stack);  // 500 - 100 committed + 50 returned
        Assert.Equal(450, c.Stack);
        Assert.Equal(1050, a.Stack + b.Stack + c.Stack);
    }
}
