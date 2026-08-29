namespace Poker.Application.Wallet;

public enum LedgerEntryType
{
    SignupGrant,
    WelcomeGiftClaim,
    DailyGift,
    BuyIn,
    CashOut,
    HandWin,
    PrivateTablePlayChipsGrant,
    PrivateTableBuyIn,
    PrivateTableCashOut
}

/// <summary>An append-only chip movement. Wallet balance is a projection over these; never mutated in place.</summary>
public sealed record LedgerEntry(
    Guid Id,
    string UserId,
    LedgerEntryType Type,
    int Amount,
    Guid? TableId,
    DateTimeOffset CreatedAtUtc);
