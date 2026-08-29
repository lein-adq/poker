using Microsoft.AspNetCore.SignalR;
using Poker.Application.Tables;

namespace Poker.Api.Hubs;

/// <summary>
/// Sends every viewer of a table their own personalized <see cref="TableStateDto"/>.
///
/// Never a single shared broadcast to the table group: hole cards are private, so what each viewer is
/// allowed to see differs. Shared by <see cref="TableHub"/> and the server-side ticker so that state
/// reaching clients from a player's action and state reaching them from the action clock are identical.
/// </summary>
public sealed class TableBroadcaster(IHubContext<TableHub> hub, TableService tableService)
{
    public async Task BroadcastAsync(Guid tableId)
    {
        var table = await tableService.GetTableAsync(tableId);
        if (table is null)
        {
            return;
        }

        var viewers = table.Seats.Select(s => s.PlayerId).Where(id => id is not null)
            .Concat(table.Spectators)
            .Distinct()
            .ToList();

        foreach (var viewerId in viewers)
        {
            await hub.Clients.User(viewerId!).SendAsync("TableState", TableStateDto.For(table, viewerId));
        }
    }
}
