using Poker.Application.Abstractions;

namespace Poker.Application.Wallet;

public sealed class WalletService(IWalletRepository repo, IClock clock)
{
    public const int SignupGrantAmount = 300;
    public const int WelcomeGiftAmount = 300;
    public const int DailyGiftAmount = 300;

    public Task GrantSignupBonusAsync(string userId) =>
        repo.AddEntryAsync(new LedgerEntry(Guid.NewGuid(), userId, LedgerEntryType.SignupGrant, SignupGrantAmount, null, clock.UtcNow));

    public async Task<int> ClaimWelcomeGiftAsync(string userId)
    {
        if (await repo.HasClaimedWelcomeGiftAsync(userId))
        {
            throw new InvalidOperationException("The welcome gift has already been claimed.");
        }

        await repo.AddEntryAsync(new LedgerEntry(Guid.NewGuid(), userId, LedgerEntryType.WelcomeGiftClaim, WelcomeGiftAmount, null, clock.UtcNow));
        return WelcomeGiftAmount;
    }

    /// <summary>Idempotent: returns false (no-op) if this user already received today's local-date gift.</summary>
    public async Task<bool> TryGrantDailyGiftAsync(string userId, DateOnly localDate)
    {
        if (!await repo.TryMarkDailyGiftGrantedAsync(userId, localDate))
        {
            return false;
        }

        await repo.AddEntryAsync(new LedgerEntry(Guid.NewGuid(), userId, LedgerEntryType.DailyGift, DailyGiftAmount, null, clock.UtcNow));
        return true;
    }

    public Task<int> GetBalanceAsync(string userId) => repo.GetBalanceAsync(userId);

    public Task<int> GetPlayChipBalanceAsync(string userId) => repo.GetPlayChipBalanceAsync(userId);

    public async Task DebitForBuyInAsync(string userId, int amount, Guid tableId)
    {
        int balance = await repo.GetBalanceAsync(userId);
        if (balance < amount)
        {
            throw new InvalidOperationException("Insufficient chips for this buy-in.");
        }

        await repo.AddEntryAsync(new LedgerEntry(Guid.NewGuid(), userId, LedgerEntryType.BuyIn, -amount, tableId, clock.UtcNow));
    }

    public Task CreditCashOutAsync(string userId, int amount, Guid tableId)
    {
        if (amount <= 0)
        {
            return Task.CompletedTask;
        }
        return repo.AddEntryAsync(new LedgerEntry(Guid.NewGuid(), userId, LedgerEntryType.CashOut, amount, tableId, clock.UtcNow));
    }

    public Task CreditHandWinAsync(string userId, int amount, Guid tableId)
    {
        if (amount <= 0)
        {
            return Task.CompletedTask;
        }
        return repo.AddEntryAsync(new LedgerEntry(Guid.NewGuid(), userId, LedgerEntryType.HandWin, amount, tableId, clock.UtcNow));
    }

    /// <summary>Private-table play chips are unlimited and isolated from the real bag: minted on demand.</summary>
    public Task DebitPrivateTableBuyInAsync(string userId, int amount, Guid tableId) =>
        repo.AddEntryAsync(new LedgerEntry(Guid.NewGuid(), userId, LedgerEntryType.PrivateTableBuyIn, -amount, tableId, clock.UtcNow));

    public Task CreditPrivateTableCashOutAsync(string userId, int amount, Guid tableId)
    {
        if (amount <= 0)
        {
            return Task.CompletedTask;
        }
        return repo.AddEntryAsync(new LedgerEntry(Guid.NewGuid(), userId, LedgerEntryType.PrivateTableCashOut, amount, tableId, clock.UtcNow));
    }
}
