using Poker.Application.Tables;
using Poker.Application.Wallet;
using Poker.Domain.Betting;
using Xunit;

namespace Poker.Application.Tests;

public class TableServiceTests
{
    private static (TableService svc, InMemoryWalletRepository walletRepo, InMemoryActiveTableTracker tracker, FixedClock clock) Build()
    {
        var walletRepo = new InMemoryWalletRepository();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var wallet = new WalletService(walletRepo, clock);
        var tracker = new InMemoryActiveTableTracker();
        var svc = new TableService(new InMemoryTableRepository(), tracker, new InMemoryDistributedLock(), wallet, clock);
        return (svc, walletRepo, tracker, clock);
    }

    private static async Task GrantChips(InMemoryWalletRepository repo, string userId, int amount) =>
        await repo.AddEntryAsync(new LedgerEntry(Guid.NewGuid(), userId, LedgerEntryType.SignupGrant, amount, null, DateTimeOffset.UtcNow));

    [Fact]
    public async Task Sit_DebitsWallet_AndOccupiesSeat()
    {
        var (svc, walletRepo, _, _) = Build();
        await GrantChips(walletRepo, "alice", 1000);
        var config = TestTable.PublicConfig();
        var table = await svc.CreateTableAsync(config);

        await svc.SitAsync(config.Id, "alice", 300);

        var reloaded = (await svc.ListTablesAsync()).Single();
        Assert.Equal(1, reloaded.SeatedPlayerCount);
        Assert.Equal(700, await walletRepo.GetBalanceAsync("alice"));
    }

    [Fact]
    public async Task Sit_BuyInOutsideRange_Throws()
    {
        var (svc, walletRepo, _, _) = Build();
        await GrantChips(walletRepo, "alice", 1000);
        var config = TestTable.PublicConfig(minBuyIn: 100, maxBuyIn: 500);
        var table = await svc.CreateTableAsync(config);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SitAsync(config.Id, "alice", 50));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SitAsync(config.Id, "alice", 600));
    }

    [Fact]
    public async Task Sit_InsufficientBalance_Throws()
    {
        var (svc, walletRepo, _, _) = Build();
        await GrantChips(walletRepo, "alice", 50);
        var config = TestTable.PublicConfig();
        await svc.CreateTableAsync(config);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SitAsync(config.Id, "alice", 300));
    }

    [Fact]
    public async Task OneActiveTablePerAccount_IsEnforced()
    {
        var (svc, walletRepo, _, _) = Build();
        await GrantChips(walletRepo, "alice", 2000);
        var tableA = await svc.CreateTableAsync(TestTable.PublicConfig());
        var tableB = await svc.CreateTableAsync(TestTable.PublicConfig());

        await svc.SitAsync(tableA.Config.Id, "alice", 300);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SitAsync(tableB.Config.Id, "alice", 300));
    }

    [Fact]
    public async Task TableFull_ExtraPlayersAreWaitlisted_AndPromotedWhenASeatOpens()
    {
        var (svc, walletRepo, _, _) = Build();
        var config = TestTable.PublicConfig(maxSeats: 2);
        var table = await svc.CreateTableAsync(config);

        await GrantChips(walletRepo, "alice", 1000);
        await GrantChips(walletRepo, "bob", 1000);
        await GrantChips(walletRepo, "carol", 1000);

        await svc.SitAsync(config.Id, "alice", 300);
        await svc.SitAsync(config.Id, "bob", 300);
        await svc.SitAsync(config.Id, "carol", 300); // table full -> waitlisted

        var afterWaitlist = (await svc.ListTablesAsync()).Single(t => t.Config.Id == config.Id);
        Assert.Equal(2, afterWaitlist.SeatedPlayerCount);
        Assert.Single(afterWaitlist.Waitlist);
        Assert.Equal(1000, await walletRepo.GetBalanceAsync("carol")); // not yet debited

        await svc.LeaveAsync(config.Id, "alice");

        var afterPromotion = (await svc.ListTablesAsync()).Single(t => t.Config.Id == config.Id);
        Assert.Equal(2, afterPromotion.SeatedPlayerCount);
        Assert.Empty(afterPromotion.Waitlist);
        Assert.NotNull(afterPromotion.FindSeat("carol"));
        Assert.Equal(700, await walletRepo.GetBalanceAsync("carol")); // debited on promotion
    }

    [Fact]
    public async Task Rebuy_WhileHandInProgress_IsQueuedUntilHandEnds()
    {
        var (svc, walletRepo, _, _) = Build();
        var config = TestTable.PublicConfig();
        await svc.CreateTableAsync(config);

        await GrantChips(walletRepo, "alice", 1000);
        await GrantChips(walletRepo, "bob", 1000);
        await svc.SitAsync(config.Id, "alice", 200);
        await svc.SitAsync(config.Id, "bob", 200);
        await svc.TryStartHandAsync(config.Id);

        await svc.RequestRebuyAsync(config.Id, "alice", 100);

        var midHand = (await svc.ListTablesAsync()).Single();
        var aliceSeat = midHand.FindSeat("alice")!;
        Assert.Equal(100, aliceSeat.PendingRebuyChips);
        Assert.NotEqual(300, aliceSeat.Stack); // not yet applied

        // Fold both actions out to end the hand quickly and check the rebuy lands.
        var actorId = midHand.CurrentHand!.CurrentActorId!;
        await svc.ApplyPlayerActionAsync(config.Id, actorId, BettingActionType.Fold);

        var afterHand = (await svc.ListTablesAsync()).Single();
        var seatAfter = afterHand.FindSeat("alice")!;
        Assert.Equal(0, seatAfter.PendingRebuyChips);
    }

    [Fact]
    public async Task PrivateTable_UsesIsolatedPlayChips_NotTheRealBag()
    {
        var (svc, walletRepo, _, _) = Build();
        // Deliberately no real-bag chips granted to alice.
        var config = TestTable.PrivatePlayMoneyConfig();
        await svc.CreateTableAsync(config);

        await svc.SitAsync(config.Id, "alice", 500);

        Assert.Equal(0, await walletRepo.GetBalanceAsync("alice"));
        Assert.Equal(-500, await walletRepo.GetPlayChipBalanceAsync("alice"));
        var table = (await svc.ListTablesAsync()).Single();
        Assert.Equal(500, table.FindSeat("alice")!.Stack);
    }

    [Fact]
    public async Task TwoSeatedPlayers_HandStartsAndPlaysToCompletion()
    {
        var (svc, walletRepo, _, _) = Build();
        var config = TestTable.PublicConfig();
        await svc.CreateTableAsync(config);
        await GrantChips(walletRepo, "alice", 1000);
        await GrantChips(walletRepo, "bob", 1000);
        await svc.SitAsync(config.Id, "alice", 300);
        await svc.SitAsync(config.Id, "bob", 300);

        Assert.True(await svc.TryStartHandAsync(config.Id));

        var table = (await svc.ListTablesAsync()).Single();
        Assert.NotNull(table.CurrentHand);
        Assert.Equal(TableStatus.Playing, table.Status);

        // Everyone folds to the first action -> hand should resolve.
        var actorId = table.CurrentHand!.CurrentActorId!;
        await svc.ApplyPlayerActionAsync(config.Id, actorId, BettingActionType.Fold);

        var afterFold = (await svc.ListTablesAsync()).Single();
        Assert.Equal(TableStatus.WaitingForPlayers, afterFold.Status);
        Assert.Equal(600, afterFold.FindSeat("alice")!.Stack + afterFold.FindSeat("bob")!.Stack);

        // The finished hand (with its Result) must still be visible for a caller to broadcast the
        // showdown/pot outcome before the next hand starts and replaces it. (The in-memory repository
        // hands back the live TableState, so capture the reference now — it will be reassigned.)
        var finishedHand = afterFold.CurrentHand;
        Assert.NotNull(finishedHand);
        Assert.NotNull(finishedHand!.Result);

        Assert.True(await svc.TryStartHandAsync(config.Id));
        var nextHand = (await svc.ListTablesAsync()).Single();
        Assert.NotSame(finishedHand, nextHand.CurrentHand);
        Assert.Null(nextHand.CurrentHand!.Result);
    }
}
