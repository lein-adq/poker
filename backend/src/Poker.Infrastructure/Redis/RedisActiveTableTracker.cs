using Poker.Application.Tables;
using StackExchange.Redis;

namespace Poker.Infrastructure.Redis;

/// <summary>Enforces one active table per account via a `active_table:{userId}` key holding the table id.</summary>
public sealed class RedisActiveTableTracker(IConnectionMultiplexer redis) : IActiveTableTracker
{
    private static string Key(string userId) => $"active_table:{userId}";

    public async Task<bool> TrySetActiveTableAsync(string userId, Guid tableId)
    {
        var db = redis.GetDatabase();
        string key = Key(userId);
        string value = tableId.ToString();

        if (await db.StringSetAsync(key, value, when: When.NotExists))
        {
            return true;
        }

        var existing = await db.StringGetAsync(key);
        return existing.HasValue && existing == value;
    }

    public Task ClearActiveTableAsync(string userId) => redis.GetDatabase().KeyDeleteAsync(Key(userId));

    public async Task<Guid?> GetActiveTableAsync(string userId)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(userId));
        return value.HasValue && Guid.TryParse((string?)value, out var id) ? id : null;
    }
}
