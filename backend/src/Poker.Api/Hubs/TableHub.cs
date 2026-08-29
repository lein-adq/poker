using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Poker.Application.Tables;
using Poker.Domain.Betting;

namespace Poker.Api.Hubs;

[Authorize]
public sealed class TableHub(
    TableService tableService,
    TableBroadcaster broadcaster,
    TableConnectionRegistry connections,
    IHubContext<LobbyHub> lobbyHub) : Hub
{
    private const string TableIdItemKey = "tableId";

    private string UserId => Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private static string GroupName(Guid tableId) => $"table:{tableId}";

    public async Task JoinAsSpectator(Guid tableId)
    {
        // Also the reconnect path: a seated player whose connection dropped rejoins through here, which
        // clears the sit-out their disconnect set. Clients must re-invoke this after an automatic
        // reconnect, since group membership belongs to the connection that went away.
        await tableService.JoinAsSpectatorAsync(tableId, UserId);
        await TrackConnectionAsync(tableId);
        await broadcaster.BroadcastAsync(tableId);
    }

    public async Task Sit(Guid tableId, int buyInChips)
    {
        await tableService.SitAsync(tableId, UserId, buyInChips);
        await TrackConnectionAsync(tableId);
        await tableService.TryStartHandAsync(tableId);
        await broadcaster.BroadcastAsync(tableId);
        await NotifyLobby();
    }

    public async Task RequestRebuy(Guid tableId, int additionalChips)
    {
        await tableService.RequestRebuyAsync(tableId, UserId, additionalChips);
        await broadcaster.BroadcastAsync(tableId);
    }

    public async Task Leave(Guid tableId)
    {
        await tableService.LeaveAsync(tableId, UserId);
        connections.Remove(UserId, tableId, Context.ConnectionId);
        Context.Items.Remove(TableIdItemKey);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(tableId));
        await broadcaster.BroadcastAsync(tableId);
        await NotifyLobby();
    }

    public async Task Act(Guid tableId, BettingActionType action, int amount)
    {
        await tableService.ApplyPlayerActionAsync(tableId, UserId, action, amount);
        await broadcaster.BroadcastAsync(tableId);

        // The next hand is dealt by TableTickerService once the post-showdown pause elapses. This method
        // used to sleep for that pause and then start the hand itself, which made the game clock depend
        // on the acting client staying connected, and blocked that client's single concurrent
        // invocation slot (so they could not even chat) for the whole three seconds.
    }

    public async Task SendChatMessage(Guid tableId, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var table = await tableService.GetTableAsync(tableId);
        bool isSpectator = table?.FindSeat(UserId) is null;

        await Clients.Group(GroupName(tableId)).SendAsync("ChatMessage", new
        {
            userId = UserId,
            message = message.Length > 500 ? message[..500] : message,
            isSpectator,
            sentAtUtc = DateTimeOffset.UtcNow
        });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Only react once the user's *last* connection for this table has gone: closing one of two tabs,
        // or a reconnect that supersedes an old connection id, is not a player leaving.
        if (Context.Items.TryGetValue(TableIdItemKey, out var value) && value is Guid tableId &&
            connections.Remove(UserId, tableId, Context.ConnectionId))
        {
            await tableService.MarkDisconnectedAsync(tableId, UserId);
            await broadcaster.BroadcastAsync(tableId);
            await NotifyLobby();
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task TrackConnectionAsync(Guid tableId)
    {
        Context.Items[TableIdItemKey] = tableId;
        connections.Add(UserId, tableId, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tableId));
    }

    private Task NotifyLobby() =>
        lobbyHub.Clients.Group(LobbyHub.GroupName).SendAsync("TablesChanged");
}
