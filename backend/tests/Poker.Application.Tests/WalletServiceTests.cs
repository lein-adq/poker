using Poker.Application.Wallet;
using Xunit;

namespace Poker.Application.Tests;

public class WalletServiceTests
{
    private static (WalletService svc, InMemoryWalletRepository repo) Build()
    {
        var repo = new InMemoryWalletRepository();
        return (new WalletService(repo, new FixedClock(DateTimeOffset.UtcNow)), repo);
    }

    [Fact]
    public async Task SignupGrant_Credits300Chips()
    {
        var (svc, repo) = Build();
        await svc.GrantSignupBonusAsync("alice");
        Assert.Equal(300, await repo.GetBalanceAsync("alice"));
    }

    [Fact]
    public async Task WelcomeGift_CanOnlyBeClaimedOnce()
    {
        var (svc, repo) = Build();
        await svc.GrantSignupBonusAsync("alice");

        int granted = await svc.ClaimWelcomeGiftAsync("alice");

        Assert.Equal(300, granted);
        Assert.Equal(600, await repo.GetBalanceAsync("alice"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ClaimWelcomeGiftAsync("alice"));
    }

    [Fact]
    public async Task DailyGift_IsIdempotentPerLocalDate()
    {
        var (svc, _) = Build();
        var today = new DateOnly(2026, 8, 28);

        bool first = await svc.TryGrantDailyGiftAsync("alice", today);
        bool second = await svc.TryGrantDailyGiftAsync("alice", today);
        bool nextDay = await svc.TryGrantDailyGiftAsync("alice", today.AddDays(1));

        Assert.True(first);
        Assert.False(second);
        Assert.True(nextDay);
    }

    [Fact]
    public async Task DebitForBuyIn_InsufficientBalance_Throws()
    {
        var (svc, _) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DebitForBuyInAsync("alice", 300, Guid.NewGuid()));
    }

    [Fact]
    public async Task PrivateTablePlayChips_AreIsolatedFromRealBalance()
    {
        var (svc, repo) = Build();
        await svc.GrantSignupBonusAsync("alice");

        await svc.DebitPrivateTableBuyInAsync("alice", 5000, Guid.NewGuid());

        Assert.Equal(300, await repo.GetBalanceAsync("alice")); // untouched
        Assert.Equal(-5000, await repo.GetPlayChipBalanceAsync("alice"));
    }
}
