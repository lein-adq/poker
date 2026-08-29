using Poker.Application.Abstractions;
using Poker.Application.Tables;
using Poker.Application.Wallet;

namespace Poker.Application.Tests;

public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>In-process stand-in for the Redis-backed lock — sufficient since tests run single-threaded per table.</summary>
public sealed class InMemoryDistributedLock : IDistributedLock
{
    private sealed class Handle : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan timeout) =>
        Task.FromResult<IAsyncDisposable>(new Handle());
}

public sealed class InMemoryTableRepository : ITableRepository
{
    private readonly Dictionary<Guid, TableState> _tables = [];

    public Task<TableState?> GetAsync(Guid tableId) =>
        Task.FromResult(_tables.GetValueOrDefault(tableId));

    public Task SaveAsync(TableState table)
    {
        _tables[table.Config.Id] = table;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TableState>> ListAsync() =>
        Task.FromResult<IReadOnlyList<TableState>>(_tables.Values.ToList());

    public Task RemoveAsync(Guid tableId)
    {
        _tables.Remove(tableId);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryActiveTableTracker : IActiveTableTracker
{
    private readonly Dictionary<string, Guid> _active = [];

    public Task<bool> TrySetActiveTableAsync(string userId, Guid tableId)
    {
        if (_active.TryGetValue(userId, out var existing) && existing != tableId)
        {
            return Task.FromResult(false);
        }
        _active[userId] = tableId;
        return Task.FromResult(true);
    }

    public Task ClearActiveTableAsync(string userId)
    {
        _active.Remove(userId);
        return Task.CompletedTask;
    }

    public Task<Guid?> GetActiveTableAsync(string userId) =>
        Task.FromResult(_active.TryGetValue(userId, out var id) ? id : (Guid?)null);
}

public sealed class InMemoryWalletRepository : IWalletRepository
{
    private readonly List<LedgerEntry> _entries = [];
    private readonly HashSet<(string UserId, DateOnly LocalDate)> _dailyGiftClaims = [];

    public Task AddEntryAsync(LedgerEntry entry)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<int> GetBalanceAsync(string userId) => Task.FromResult(
        _entries.Where(e => e.UserId == userId && IsRealBag(e.Type)).Sum(e => e.Amount));

    public Task<int> GetPlayChipBalanceAsync(string userId) => Task.FromResult(
        _entries.Where(e => e.UserId == userId && !IsRealBag(e.Type)).Sum(e => e.Amount));

    public Task<bool> HasClaimedWelcomeGiftAsync(string userId) => Task.FromResult(
        _entries.Any(e => e.UserId == userId && e.Type == LedgerEntryType.WelcomeGiftClaim));

    public Task<bool> TryMarkDailyGiftGrantedAsync(string userId, DateOnly localDate) =>
        Task.FromResult(_dailyGiftClaims.Add((userId, localDate)));

    public IReadOnlyList<LedgerEntry> Entries => _entries;

    private static bool IsRealBag(LedgerEntryType type) => type is
        LedgerEntryType.SignupGrant or LedgerEntryType.WelcomeGiftClaim or LedgerEntryType.DailyGift or
        LedgerEntryType.BuyIn or LedgerEntryType.CashOut or LedgerEntryType.HandWin;
}

public static class TestTable
{
    public static TableConfig PublicConfig(int maxSeats = 9, int minBuyIn = 100, int maxBuyIn = 1000) => new(
        Id: Guid.NewGuid(),
        Name: "Test Table",
        CreatorUserId: "creator",
        MinBuyIn: minBuyIn,
        MaxBuyIn: maxBuyIn,
        SmallBlind: 10,
        BigBlind: 20,
        IsPrivate: false,
        UseRealBankroll: true,
        MaxSeats: maxSeats);

    public static TableConfig PrivatePlayMoneyConfig() => new(
        Id: Guid.NewGuid(),
        Name: "Private Table",
        CreatorUserId: "creator",
        MinBuyIn: 100,
        MaxBuyIn: 1000,
        SmallBlind: 10,
        BigBlind: 20,
        IsPrivate: true,
        UseRealBankroll: false);
}
