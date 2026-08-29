using System.Text;
using Poker.Domain.Betting;
using Poker.GameEngine.Hands;
using Xunit;

namespace Poker.GameEngine.Tests;

/// <summary>
/// Property tests over whole hands: a hand redistributes chips between the players in it and must never
/// mint or burn any, no matter how the betting goes. Randomised play is what reaches the multi-way
/// all-in / side-pot / odd-chip-split corners that hand-written examples miss.
/// </summary>
public class HandEngineConservationTests
{
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 400).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void RandomHand_ConservesChips(int seed)
    {
        var rng = new Random(seed);
        int playerCount = rng.Next(2, 9);

        // Wildly uneven stacks, deliberately including stacks below the big blind, so short all-ins and
        // layered side pots are common rather than rare.
        var players = Enumerable.Range(0, playerCount)
            .Select(i => new PlayerBetState { PlayerId = $"p{i}", Stack = rng.Next(5, 600) })
            .ToList();

        int chipsAtStart = players.Sum(p => p.Stack);
        var engine = new HandEngine(players, smallBlind: 10, bigBlind: 20);

        int guard = 0;
        while (engine.Result is null && engine.CurrentActorId is { } actorId)
        {
            Assert.True(guard++ < 500, $"hand never terminated (seed {seed})");
            var (action, amount) = ChooseAction(engine, actorId, rng);
            engine.Act(actorId, action, amount);
            engine.TryAdvance();

            int inFlight = players.Sum(p => p.Stack) + players.Sum(p => p.CommittedTotal);
            Assert.True(
                inFlight == chipsAtStart || engine.Result is not null,
                $"chips changed mid-hand: {chipsAtStart} -> {inFlight}{Describe(players, engine)}");
        }

        Assert.NotNull(engine.Result);

        int chipsAtEnd = players.Sum(p => p.Stack);
        Assert.True(
            chipsAtEnd == chipsAtStart,
            $"hand created or destroyed chips: started with {chipsAtStart}, ended with {chipsAtEnd}{Describe(players, engine)}");

        // Every chip put in must come back out through exactly one pot.
        int committed = players.Sum(p => p.CommittedTotal);
        int awarded = engine.Result!.Pots.Sum(p => p.Amount);
        Assert.True(
            committed == awarded,
            $"pot total {awarded} does not match the {committed} chips committed{Describe(players, engine)}");
    }

    private static string Describe(IEnumerable<PlayerBetState> players, HandEngine engine)
    {
        var sb = new StringBuilder().AppendLine().AppendLine("  players:");
        foreach (var p in players)
        {
            sb.AppendLine($"    {p.PlayerId}: stack={p.Stack} committed={p.CommittedTotal} folded={p.IsFolded}");
        }
        if (engine.Result is { } result)
        {
            sb.AppendLine("  pots:");
            foreach (var pot in result.Pots)
            {
                sb.AppendLine($"    {pot.Amount} eligible=[{string.Join(",", pot.EligiblePlayerIds)}] " +
                              $"winners=[{string.Join(",", pot.WinnerPlayerIds)}]");
            }
        }
        return sb.ToString();
    }

    private static (BettingActionType Action, int Amount) ChooseAction(HandEngine engine, string actorId, Random rng)
    {
        var legal = engine.GetLegalActions(actorId);
        var actor = engine.Players.Single(p => p.PlayerId == actorId);
        int currentBet = actor.CommittedThisRound + legal.CallAmount;
        bool canRaise = legal.MaxRaiseTo > currentBet;

        int roll = rng.Next(100);
        if (roll < 15)
        {
            return (BettingActionType.Fold, 0);
        }
        if (canRaise && roll < 50)
        {
            int raiseTo = rng.Next(2) == 0 ? legal.MaxRaiseTo : rng.Next(legal.MinRaiseTo, legal.MaxRaiseTo + 1);
            return (legal.CanCheck ? BettingActionType.Bet : BettingActionType.Raise, raiseTo);
        }
        if (legal.CanCall)
        {
            return (BettingActionType.Call, 0);
        }
        return legal.CanCheck ? (BettingActionType.Check, 0) : (BettingActionType.Fold, 0);
    }
}
