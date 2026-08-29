using Poker.Domain.Betting;
using Xunit;

namespace Poker.GameEngine.Tests;

public class BettingRoundTests
{
    private static PlayerBetState Player(string id, int stack) => new() { PlayerId = id, Stack = stack };

    [Fact]
    public void HeadsUp_CheckCheck_ClosesRound()
    {
        var a = Player("A", 1000);
        var b = Player("B", 1000);
        var round = new BettingRound([a, b], bigBlind: 20);

        Assert.False(round.IsComplete);
        round.Apply("A", BettingActionType.Check);
        round.Apply("B", BettingActionType.Check);

        Assert.True(round.IsComplete);
    }

    [Fact]
    public void BetThenCall_ClosesRound_AndMovesChips()
    {
        var a = Player("A", 1000);
        var b = Player("B", 1000);
        var round = new BettingRound([a, b], bigBlind: 20);

        round.Apply("A", BettingActionType.Bet, 100);
        Assert.False(round.IsComplete);
        round.Apply("B", BettingActionType.Call);

        Assert.True(round.IsComplete);
        Assert.Equal(900, a.Stack);
        Assert.Equal(900, b.Stack);
        Assert.Equal(100, a.CommittedTotal);
        Assert.Equal(100, b.CommittedTotal);
    }

    [Fact]
    public void RaiseBelowMinimum_Throws()
    {
        var a = Player("A", 1000);
        var b = Player("B", 1000);
        var round = new BettingRound([a, b], bigBlind: 20);

        round.Apply("A", BettingActionType.Bet, 100);
        Assert.Throws<InvalidOperationException>(() => round.Apply("B", BettingActionType.Raise, 110));
    }

    [Fact]
    public void ShortStackAllInRaise_BelowMinimum_IsAllowed()
    {
        var a = Player("A", 1000);
        var b = Player("B", 50);
        var round = new BettingRound([a, b], bigBlind: 20);

        round.Apply("A", BettingActionType.Bet, 100);
        // B only has 50 total, less than the 100 call amount, so their only options are fold or all-in call.
        round.Apply("B", BettingActionType.Call);

        Assert.Equal(0, b.Stack);
        Assert.True(b.IsAllIn);
    }

    [Fact]
    public void Fold_LeavesSinglePlayer_RoundCompletesImmediately()
    {
        var a = Player("A", 1000);
        var b = Player("B", 1000);
        var round = new BettingRound([a, b], bigBlind: 20);

        round.Apply("A", BettingActionType.Fold);
        Assert.True(round.IsComplete);
        Assert.Null(round.CurrentActor);
    }

    [Fact]
    public void ThreeWay_RaiseReopensActionForEarlierCallers()
    {
        var a = Player("A", 1000);
        var b = Player("B", 1000);
        var c = Player("C", 1000);
        var round = new BettingRound([a, b, c], bigBlind: 20);

        round.Apply("A", BettingActionType.Bet, 100);
        round.Apply("B", BettingActionType.Call);
        Assert.False(round.IsComplete);
        round.Apply("C", BettingActionType.Raise, 300);
        Assert.False(round.IsComplete); // A and B must act again

        round.Apply("A", BettingActionType.Call);
        Assert.False(round.IsComplete);
        round.Apply("B", BettingActionType.Call);
        Assert.True(round.IsComplete);
    }
}
