namespace Poker.Application.Tables;

public interface ITableRepository
{
    Task<TableState?> GetAsync(Guid tableId);
    Task SaveAsync(TableState table);
    Task<IReadOnlyList<TableState>> ListAsync();
    Task RemoveAsync(Guid tableId);
}

public interface IActiveTableTracker
{
    /// <summary>Atomically claims this table as the user's one active table. False if they're already active elsewhere.</summary>
    Task<bool> TrySetActiveTableAsync(string userId, Guid tableId);

    Task ClearActiveTableAsync(string userId);

    Task<Guid?> GetActiveTableAsync(string userId);
}
