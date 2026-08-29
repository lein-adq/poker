using System.Collections.Concurrent;
using Poker.Application.Tables;

namespace Poker.Infrastructure.Tables;

/// <summary>
/// Process-wide table state store. A live hand's <c>HandEngine</c> (including deck order) is a mutable
/// object graph that isn't trivially serializable, so live table/hand state is kept in-process rather
/// than in Redis for this build — a single API instance owns all tables. Cross-instance concerns that
/// genuinely need to be shared (one-active-table-per-account, distributed locking, daily-gift dedupe,
/// SignalR fan-out) already go through Redis via <see cref="Redis.RedisActiveTableTracker"/>,
/// <see cref="Redis.RedisDistributedLock"/>, and the SignalR Redis backplane. Scaling table state itself
/// across instances would need a serializable hand representation (e.g. a seeded deck) — out of scope here.
/// </summary>
public sealed class InMemoryTableRepository : ITableRepository
{
    private readonly ConcurrentDictionary<Guid, TableState> _tables = new();

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
        _tables.TryRemove(tableId, out _);
        return Task.CompletedTask;
    }
}
