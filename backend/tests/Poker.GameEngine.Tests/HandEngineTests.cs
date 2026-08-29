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
        Assert.Equal(0, engine.Board.Count);
        var pot = Assert.Single(engine.Result!.Pots);
        Assert.Equal("B", Assert.Single(pot.WinnerPlayerIds));
        Assert.Equal(2000, a.Stack + b.Stack);
    }
}
