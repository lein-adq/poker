namespace Poker.Api.Hubs;

/// <summary>
/// Which connections each user currently has open against each table.
///
/// "This player is gone" means "their last connection for this table dropped", not "a connection
/// dropped": a user with two tabs open who closes one has not left, and SignalR issues a fresh
/// connection id on every automatic reconnect. Getting this wrong sits people out for switching tabs.
///
/// In-process by design, matching the single-instance ownership of live table state.
/// </summary>
public sealed class TableConnectionRegistry
{
    private readonly Dictionary<(string UserId, Guid TableId), HashSet<string>> _connections = [];
    private readonly Lock _gate = new();

    public void Add(string userId, Guid tableId, string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue((userId, tableId), out var connectionIds))
            {
                connectionIds = [];
                _connections[(userId, tableId)] = connectionIds;
            }
            connectionIds.Add(connectionId);
        }
    }

    /// <summary>
    /// Drops one connection. Returns true only when it was this user's last one on this table — i.e.
    /// when they have actually gone away and the table should react.
    /// </summary>
    public bool Remove(string userId, Guid tableId, string connectionId)
    {
        lock (_gate)
        {
            var key = (userId, tableId);
            if (!_connections.TryGetValue(key, out var connectionIds) || !connectionIds.Remove(connectionId))
            {
                return false;
            }

            if (connectionIds.Count > 0)
            {
                return false;
            }

            _connections.Remove(key);
            return true;
        }
    }
}
