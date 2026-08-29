using System.Security.Claims;
using Poker.Api.Iam;
using Poker.Application.Abstractions;
using Poker.Application.Tables;
using Poker.Application.Wallet;

namespace Poker.Api.Endpoints;

public sealed record ValidateEmailRequest(string Email);
public sealed record RegisteredWebhookRequest(string UserId, string Email, string? TimeZoneId);
public sealed record CreateTableRequest(
    string Name, int MinBuyIn, int MaxBuyIn, int SmallBlind, int BigBlind,
    bool IsPrivate, bool UseRealBankroll, int? MaxSeats, int? MinPlayersToStart);
public sealed record RebuyRequest(int AdditionalChips);

public static class EndpointMappings
{
    public static void MapPokerEndpoints(this WebApplication app)
    {
        // --- Ory Kratos webhooks (internal network only, not public) ---
        var iam = app.MapGroup("/internal/iam");

        iam.MapPost("/validate-email", (ValidateEmailRequest req, EmailDomainAllowList allowList) =>
        {
            if (!string.IsNullOrWhiteSpace(req.Email) && allowList.IsAllowed(req.Email))
            {
                return Results.Ok();
            }

            // Shape required by Kratos's can_interrupt webhook contract: it parses `messages` into a
            // per-field validation error attached to the traits.email node in the registration UI.
            return Results.BadRequest(new
            {
                messages = new[]
                {
                    new
                    {
                        instance_ptr = "#/traits/email",
                        messages = new[]
                        {
                            new { id = 4000002, text = "This email domain isn't allowed. Please use a known provider.", type = "error" }
                        }
                    }
                }
            });
        });

        iam.MapPost("/on-registered", async (RegisteredWebhookRequest req, IUserRepository users, WalletService wallet) =>
        {
            await users.UpsertAsync(new UserSummary(req.UserId, req.Email, req.TimeZoneId ?? "UTC"));
            await wallet.GrantSignupBonusAsync(req.UserId);
            return Results.Ok();
        });

        // --- Authenticated wallet API ---
        var walletApi = app.MapGroup("/api/wallet").RequireAuthorization();

        walletApi.MapGet("/", async (ClaimsPrincipal user, WalletService wallet) =>
        {
            string userId = RequireUserId(user);
            return Results.Ok(new
            {
                balance = await wallet.GetBalanceAsync(userId),
                playChips = await wallet.GetPlayChipBalanceAsync(userId)
            });
        });

        walletApi.MapPost("/welcome-gift/claim", async (ClaimsPrincipal user, WalletService wallet) =>
        {
            string userId = RequireUserId(user);
            try
            {
                int granted = await wallet.ClaimWelcomeGiftAsync(userId);
                return Results.Ok(new { granted });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // --- Authenticated table API ---
        var tables = app.MapGroup("/api/tables").RequireAuthorization();

        tables.MapGet("/", async (TableService svc) =>
            Results.Ok((await svc.ListTablesAsync()).Select(TableSummary.From)));

        tables.MapPost("/", async (CreateTableRequest req, ClaimsPrincipal user, TableService svc) =>
        {
            string userId = RequireUserId(user);
            try
            {
                var config = new TableConfig(
                    Guid.NewGuid(), req.Name, userId, req.MinBuyIn, req.MaxBuyIn,
                    req.SmallBlind, req.BigBlind, req.IsPrivate,
                    req.IsPrivate ? req.UseRealBankroll : true,
                    req.MaxSeats ?? 9, req.MinPlayersToStart ?? 2);
                var table = await svc.CreateTableAsync(config);
                return Results.Ok(TableSummary.From(table));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        tables.MapPost("/{tableId:guid}/rebuy", async (Guid tableId, RebuyRequest req, ClaimsPrincipal user, TableService svc) =>
        {
            string userId = RequireUserId(user);
            try
            {
                await svc.RequestRebuyAsync(tableId, userId, req.AdditionalChips);
                return Results.Ok();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static string RequireUserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Missing user id claim.");
}
