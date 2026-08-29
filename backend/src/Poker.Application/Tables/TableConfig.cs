namespace Poker.Application.Tables;

public sealed record TableConfig(
    Guid Id,
    string Name,
    string CreatorUserId,
    int MinBuyIn,
    int MaxBuyIn,
    int SmallBlind,
    int BigBlind,
    bool IsPrivate,
    bool UseRealBankroll,
    int MaxSeats = 9,
    int MinPlayersToStart = 2)
{
    public void Validate()
    {
        if (MaxSeats is < 2 or > 9)
        {
            throw new ArgumentException("A table supports between 2 and 9 seats.");
        }
        if (MinPlayersToStart < 2)
        {
            throw new ArgumentException("A hand needs at least 2 players to start.");
        }
        if (MinBuyIn <= 0 || MaxBuyIn < MinBuyIn)
        {
            throw new ArgumentException("Buy-in range is invalid.");
        }
        if (IsPrivate == false && UseRealBankroll == false)
        {
            throw new ArgumentException("Public tables always use the real bankroll.");
        }
    }
}
