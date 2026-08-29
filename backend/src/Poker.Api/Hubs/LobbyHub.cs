using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Poker.Application.Tables;

namespace Poker.Api.Hubs;

[Authorize]
public sealed class LobbyHub(TableService tableService) : Hub
{
    public const string GroupName = "lobby";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName);
        await SendTableList();
    }

    public Task RefreshTables() => SendTableList();

    private async Task SendTableList()
    {
        var tables = await tableService.ListTablesAsync();
        await Clients.Caller.SendAsync("TableList", tables.Select(TableSummary.From));
    }
}
