using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Poker.Api.Auth;

/// <summary>Lets hubs target a specific user via Clients.User(userId) — required so hole cards are only ever sent to their owner.</summary>
public sealed class NameIdentifierUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirstValue(ClaimTypes.NameIdentifier);
}
