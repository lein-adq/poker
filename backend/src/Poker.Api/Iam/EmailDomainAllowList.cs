namespace Poker.Api.Iam;

/// <summary>
/// Allow-list (not a disposable-domain blacklist, per the PRD) of email domains permitted to register.
/// Seeded with the most common real providers; extend via configuration ("Iam:AllowedEmailDomains")
/// to add company/custom domains without touching code.
/// </summary>
public sealed class EmailDomainAllowList
{
    private readonly HashSet<string> _domains;

    public EmailDomainAllowList(IConfiguration config)
    {
        var configured = config.GetSection("Iam:AllowedEmailDomains").Get<string[]>() ?? [];
        _domains = new HashSet<string>(DefaultDomains.Concat(configured), StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string[] DefaultDomains =
    [
        "gmail.com", "googlemail.com",
        "outlook.com", "hotmail.com", "live.com", "msn.com",
        "yahoo.com", "ymail.com",
        "icloud.com", "me.com", "mac.com",
        "proton.me", "protonmail.com",
        "aol.com"
    ];

    public bool IsAllowed(string email)
    {
        int at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1)
        {
            return false;
        }
        return _domains.Contains(email[(at + 1)..]);
    }
}
