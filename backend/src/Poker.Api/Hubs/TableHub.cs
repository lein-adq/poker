using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Poker.Application.Tables;
using Poker.Domain.Betting;

namespace Poker.Api.Hubs;

[Authorize]
public sealed class TableHub(TableService tableService, IHubContext<LobbyHub> lobbyHub) : Hub
{
    private string UserId => Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private static string GroupName(Guid tableId) => $"table:{tableId}";

    public async Task JoinAsSpectator(Guid tableId)
    {
        await tableService.JoinAsSpectatorAsync(tableId, UserId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tableId));
        await BroadcastTableState(tableId);
    }

    public async Task Sit(Guid tableId, int buyInChips)
    {
        await tableService.SitAsync(tableId, UserId, buyInChips);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tableId));
        await tableService.TryStartHandAsync(tableId);
        await BroadcastTableState(tableId);
        await NotifyLobby();
    }

    public async Task RequestRebuy(Guid tableId, int additionalChips)
    {
        await tableService.RequestRebuyAsync(tableId, UserId, additionalChips);
        await BroadcastTableState(tableId);
    }

    public async Task Leave(Guid tableId)
    {
        await tableService.LeaveAsync(tableId, UserId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(tableId));
        await BroadcastTableState(tableId);
        await NotifyLobby();
    }

    public async Task Act(Guid tableId, BettingActionType action, int amount)
    {
        await tableService.ApplyPlayerActionAsync(tableId, UserId, action, amount);

        // Broadcast first so a just-finished hand's showdown/pot result is actually seen by clients
        // before the next hand overwrites it, then give players a moment to read it.
        await BroadcastTableState(tableId);

        var table = await tableService.GetTableAsync(tableId);
        if (table is not null && table.CurrentHand is null or { Result: not null })
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            await tableService.TryStartHandAsync(tableId);
            await BroadcastTableState(tableId);
        }
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

    /// <summary>
    /// Sends every connected player their own personalized view (their hole cards only, others hidden
    /// unless showdown/all-in-reveal) rather than one shared group broadcast.
    /// </summary>
    private async Task BroadcastTableState(Guid tableId)
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
            var dto = TableStateDto.For(table, viewerId);
            await Clients.User(viewerId!).SendAsync("TableState", dto);
        }
    }

    private Task NotifyLobby() =>
        lobbyHub.Clients.Group(LobbyHub.GroupName).SendAsync("TablesChanged");
}
