namespace Poker.Application.Wallet;

public interface IWalletRepository
{
    Task AddEntryAsync(LedgerEntry entry);

    /// <summary>Real-chip balance: sum of all real-bag ledger entries for the user.</summary>
    Task<int> GetBalanceAsync(string userId);

    /// <summary>Isolated play-chip balance used only at private tables that don't touch the real bag.</summary>
    Task<int> GetPlayChipBalanceAsync(string userId);

    Task<bool> HasClaimedWelcomeGiftAsync(string userId);

    /// <summary>Atomically claims the daily gift for this user/local-date. Returns false if already claimed.</summary>
    Task<bool> TryMarkDailyGiftGrantedAsync(string userId, DateOnly localDate);
}
