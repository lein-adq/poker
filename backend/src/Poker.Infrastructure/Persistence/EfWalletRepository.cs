using Microsoft.EntityFrameworkCore;
using Poker.Application.Wallet;
using StackExchange.Redis;

namespace Poker.Infrastructure.Persistence;

public sealed class EfWalletRepository(PokerDbContext db, IConnectionMultiplexer redis) : IWalletRepository
{
    private static readonly LedgerEntryType[] RealBagTypes =
    [
        LedgerEntryType.SignupGrant, LedgerEntryType.WelcomeGiftClaim, LedgerEntryType.DailyGift,
        LedgerEntryType.BuyIn, LedgerEntryType.CashOut, LedgerEntryType.HandWin
    ];

    public async Task AddEntryAsync(LedgerEntry entry)
    {
        db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = entry.Id,
            UserId = entry.UserId,
            Type = entry.Type,
            Amount = entry.Amount,
            TableId = entry.TableId,
            CreatedAtUtc = entry.CreatedAtUtc
        });
        await db.SaveChangesAsync();
    }

    public Task<int> GetBalanceAsync(string userId) =>
        db.LedgerEntries
            .Where(e => e.UserId == userId && RealBagTypes.Contains(e.Type))
            .SumAsync(e => (int?)e.Amount)
            .ContinueWith(t => t.Result ?? 0);

    public Task<int> GetPlayChipBalanceAsync(string userId) =>
        db.LedgerEntries
            .Where(e => e.UserId == userId && !RealBagTypes.Contains(e.Type))
            .SumAsync(e => (int?)e.Amount)
            .ContinueWith(t => t.Result ?? 0);

    public Task<bool> HasClaimedWelcomeGiftAsync(string userId) =>
        db.LedgerEntries.AnyAsync(e => e.UserId == userId && e.Type == LedgerEntryType.WelcomeGiftClaim);

    /// <summary>Atomic claim via a Redis SETNX dedupe key, per the design in docs/PRD-derived plan.</summary>
    public Task<bool> TryMarkDailyGiftGrantedAsync(string userId, DateOnly localDate)
    {
        string key = $"dailygift:{userId}:{localDate:yyyy-MM-dd}";
        return redis.GetDatabase().StringSetAsync(key, "1", TimeSpan.FromDays(2), When.NotExists);
    }
}
