using Poker.Application.Abstractions;
using StackExchange.Redis;

namespace Poker.Infrastructure.Redis;

/// <summary>Simple Redis lock: SET NX PX to acquire, a token-checked Lua script to release safely.</summary>
public sealed class RedisDistributedLock(IConnectionMultiplexer redis) : IDistributedLock
{
    private const string ReleaseScript = """
        if redis.call("get", KEYS[1]) == ARGV[1] then
            return redis.call("del", KEYS[1])
        else
            return 0
        end
        """;

    public async Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan timeout)
    {
        var db = redis.GetDatabase();
        string lockKey = $"lock:{key}";
        string token = Guid.NewGuid().ToString("N");
        var deadline = DateTime.UtcNow + timeout;
        var delay = TimeSpan.FromMilliseconds(25);

        while (!await db.StringSetAsync(lockKey, token, timeout, When.NotExists))
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Could not acquire lock '{key}' within {timeout}.");
            }
            await Task.Delay(delay);
        }

        return new Releaser(db, lockKey, token);
    }

    private sealed class Releaser(IDatabase db, string lockKey, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await db.ScriptEvaluateAsync(ReleaseScript, [lockKey], [token]);
    }
}
