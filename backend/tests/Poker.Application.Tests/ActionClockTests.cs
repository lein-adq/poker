using Poker.Application.Tables;
using Poker.Application.Wallet;
using Poker.Domain.Betting;
using Poker.GameEngine.Hands;
using Xunit;

namespace Poker.Application.Tests;

/// <summary>
/// The table has to advance on the server's clock rather than only when a client sends something.
/// Without that, a player who closes their laptop while holding the action freezes the table for
/// everyone else, and the pause between hands depends on the previous actor staying connected.
/// </summary>
public class ActionClockTests
{
    private static (TableService Svc, InMemoryWalletRepository WalletRepo, InMemoryActiveTableTracker Tracker, FixedClock Clock) Build()
    {
        var walletRepo = new InMemoryWalletRepository();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var wallet = new WalletService(walletRepo, clock);
        var tracker = new InMemoryActiveTableTracker();
        var svc = new TableService(new InMemoryTableRepository(), tracker, new InMemoryDistributedLock(), wallet, clock);
        return (svc, walletRepo, tracker, clock);
    }

    /// <summary>Seats alice, bob and carol with 300 each and deals a hand. Alice is on the button and
    /// acts first preflop; bob posts the small blind and carol the big blind.</summary>
    private static async Task<(TableService Svc, TableState Table, TableConfig Config, InMemoryWalletRepository WalletRepo, InMemoryActiveTableTracker Tracker, FixedClock Clock)>
        SeatedTable(int playerCount = 3, bool deal = true)
    {
        var (svc, walletRepo, tracker, clock) = Build();
        var config = TestTable.PublicConfig();
        await svc.CreateTableAsync(config);

        foreach (var p in new[] { "alice", "bob", "carol" }.Take(playerCount))
        {
            await walletRepo.AddEntryAsync(
                new LedgerEntry(Guid.NewGuid(), p, LedgerEntryType.SignupGrant, 1000, null, clock.UtcNow));
            await svc.SitAsync(config.Id, p, 300);
        }

        if (deal)
        {
            Assert.True(await svc.TryStartHandAsync(config.Id));
        }

        var table = (await svc.ListTablesAsync()).Single();
        return (svc, table, config, walletRepo, tracker, clock);
    }

    [Fact]
    public async Task Tick_DoesNothing_WhileThePlayerStillHasTimeToAct()
    {
        var (svc, table, config, _, _, clock) = await SeatedTable();
        var deadline = table.ActionDeadlineUtc;
        Assert.NotNull(deadline);

        clock.UtcNow += TableService.ActionTimeout - TimeSpan.FromSeconds(1);

        Assert.False(await svc.TickAsync(config.Id));
        Assert.Equal("alice", table.CurrentHand!.CurrentActorId);
        Assert.Equal(deadline, table.ActionDeadlineUtc);
    }

    [Fact]
    public async Task ExpiredClock_ChecksWhenCheckingIsFree()
    {
        var (svc, table, config, _, _, clock) = await SeatedTable();

        // Everyone calls round to the big blind, who can check their option for free.
        await svc.ApplyPlayerActionAsync(config.Id, "alice", BettingActionType.Call);
        await svc.ApplyPlayerActionAsync(config.Id, "bob", BettingActionType.Call);
        Assert.Equal("carol", table.CurrentHand!.CurrentActorId);

        clock.UtcNow += TableService.ActionTimeout;
        Assert.True(await svc.TickAsync(config.Id));

        // Checked, not folded: the clock must never throw away a hand that costs nothing to keep.
        Assert.False(table.CurrentHand!.Players.Single(p => p.PlayerId == "carol").IsFolded);
        Assert.Equal(Street.Flop, table.CurrentHand.CurrentStreet);
    }

    [Fact]
    public async Task ExpiredClock_FoldsWhenFacingABet()
    {
        var (svc, table, config, _, _, clock) = await SeatedTable();

        await svc.ApplyPlayerActionAsync(config.Id, "alice", BettingActionType.Raise, 60);
        Assert.Equal("bob", table.CurrentHand!.CurrentActorId);
        int bobStackBeforeTimeout = table.FindSeat("bob")!.Stack;

        clock.UtcNow += TableService.ActionTimeout;
        Assert.True(await svc.TickAsync(config.Id));

        Assert.True(table.CurrentHand!.Players.Single(p => p.PlayerId == "bob").IsFolded);
        // Folding is free: the clock never commits chips on somebody's behalf.
        Assert.Equal(bobStackBeforeTimeout, table.FindSeat("bob")!.Stack);
        Assert.Equal("carol", table.CurrentHand.CurrentActorId);
    }

    [Fact]
    public async Task ExpiredClock_ResetsTheDeadlineForTheNextPlayer()
    {
        var (svc, table, config, _, _, clock) = await SeatedTable();

        clock.UtcNow += TableService.ActionTimeout;
        Assert.True(await svc.TickAsync(config.Id));

        // One player timing out must not cascade into everybody behind them being timed out at once.
        Assert.Equal(clock.UtcNow + TableService.ActionTimeout, table.ActionDeadlineUtc);
        Assert.False(await svc.TickAsync(config.Id));
    }

    [Fact]
    public async Task NextHand_IsDealtByTheClock_WithoutAnyClientInvolvement()
    {
        var (svc, table, config, _, _, clock) = await SeatedTable(playerCount: 2);

        // Heads-up: the button/small blind acts first preflop. Folding ends the hand immediately.
        await svc.ApplyPlayerActionAsync(config.Id, table.CurrentHand!.CurrentActorId!, BettingActionType.Fold);
        var finishedHand = table.CurrentHand;
        Assert.NotNull(finishedHand!.Result);
        Assert.Null(table.ActionDeadlineUtc);
        Assert.Equal(clock.UtcNow + TableService.NextHandDelay, table.NextHandStartUtc);

        // The finished hand stays visible for the showdown pause so clients can render the result.
        Assert.False(await svc.TickAsync(config.Id));
        Assert.Same(finishedHand, table.CurrentHand);

        clock.UtcNow += TableService.NextHandDelay;
        Assert.True(await svc.TickAsync(config.Id));

        Assert.NotSame(finishedHand, table.CurrentHand);
        Assert.Null(table.CurrentHand!.Result);
        Assert.Null(table.NextHandStartUtc);
        Assert.Equal(TableStatus.Playing, table.Status);
    }

    [Fact]
    public async Task Disconnect_KicksThePlayerAtTheEndOfTheHand_ButLeavesTheirLiveHandAlone()
    {
        var (svc, table, config, _, _, clock) = await SeatedTable();

        await svc.MarkDisconnectedAsync(config.Id, "bob");

        // Still holding their seat, chips and live hand: a brief blip must not cost them the pot.
        var bobSeat = table.FindSeat("bob")!;
        Assert.True(bobSeat.IsSittingOut);
        Assert.Equal(clock.UtcNow, bobSeat.DisconnectedAtUtc);
        Assert.Equal(290, bobSeat.Stack);
        Assert.False(table.CurrentHand!.Players.Single(p => p.PlayerId == "bob").IsFolded);

        // Play the hand out: alice folds, bob's clock runs down, carol takes it.
        await svc.ApplyPlayerActionAsync(config.Id, "alice", BettingActionType.Fold);
        clock.UtcNow += TableService.ActionTimeout;
        Assert.True(await svc.TickAsync(config.Id));
        Assert.NotNull(table.CurrentHand!.Result);

        clock.UtcNow += TableService.NextHandDelay;
        Assert.True(await svc.TickAsync(config.Id));

        // Kicked from the table entirely before the next hand starts.
        Assert.Null(table.FindSeat("bob"));
        Assert.DoesNotContain("bob", table.Spectators);
    }

    [Fact]
    public async Task Reconnecting_ClearsTheSitOut_AndDealsThemBackIn()
    {
        var (svc, table, config, _, _, clock) = await SeatedTable();
        await svc.MarkDisconnectedAsync(config.Id, "bob");
        Assert.True(table.FindSeat("bob")!.IsSittingOut);

        // Rejoining the table is the reconnect: a seated player comes back through the spectator path.
        await svc.JoinAsSpectatorAsync(config.Id, "bob");

        var bobSeat = table.FindSeat("bob")!;
        Assert.False(bobSeat.IsSittingOut);
        Assert.Null(bobSeat.DisconnectedAtUtc);
        Assert.DoesNotContain("bob", table.Spectators); // still a player, not demoted to spectator

        await svc.ApplyPlayerActionAsync(config.Id, "alice", BettingActionType.Fold);
        await svc.ApplyPlayerActionAsync(config.Id, "bob", BettingActionType.Fold);
        clock.UtcNow += TableService.NextHandDelay;
        Assert.True(await svc.TickAsync(config.Id));

        Assert.Contains(table.CurrentHand!.Players, p => p.PlayerId == "bob");
    }

    [Fact]
    public async Task DisconnectedSpectator_ReleasesTheOneActiveTableSlot()
    {
        var (svc, _, config, _, tracker, _) = await SeatedTable(playerCount: 2, deal: false);
        await svc.JoinAsSpectatorAsync(config.Id, "dave");
        Assert.Equal(config.Id, await tracker.GetActiveTableAsync("dave"));

        await svc.MarkDisconnectedAsync(config.Id, "dave");

        // Otherwise closing the tab locks the account out of every table until it calls Leave, which a
        // dropped connection never gets to do.
        Assert.Null(await tracker.GetActiveTableAsync("dave"));
    }

    [Fact]
    public async Task TableWhereEveryoneDisconnected_GoesQuiet_RatherThanRetryingForever()
    {
        var (svc, table, config, _, _, clock) = await SeatedTable(playerCount: 2);

        await svc.ApplyPlayerActionAsync(config.Id, table.CurrentHand!.CurrentActorId!, BettingActionType.Fold);
        await svc.MarkDisconnectedAsync(config.Id, "alice");
        await svc.MarkDisconnectedAsync(config.Id, "bob");

        clock.UtcNow += TableService.NextHandDelay;

        // One tick consumes the pending start and finds nobody to deal to; every tick after that is a
        // no-op, so an abandoned table costs nothing and never spins.
        await svc.TickAsync(config.Id);
        Assert.Null(table.NextHandStartUtc);
        Assert.False(await svc.TickAsync(config.Id));

        clock.UtcNow += TimeSpan.FromMinutes(10);
        Assert.False(await svc.TickAsync(config.Id));
    }
}
