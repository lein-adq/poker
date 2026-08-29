using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Poker.Api.Auth;

/// <summary>
/// Trusts identity headers injected by Ory Oathkeeper after it validates the caller's Kratos session
/// cookie. This API must only ever be reachable through Oathkeeper (enforced at the network/ingress
/// level, e.g. the docker-compose network) — these headers are never treated as untrusted input from
/// a public client, only from the trusted edge proxy sitting in front of this service.
/// </summary>
public sealed class OathkeeperAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Oathkeeper";
    public const string UserIdHeader = "X-User-Id";
    public const string UserEmailHeader = "X-User-Email";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserIdHeader, out var userId) || string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (Request.Headers.TryGetValue(UserEmailHeader, out var email) && !string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new Claim(ClaimTypes.Email, email.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
