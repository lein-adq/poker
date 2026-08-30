namespace Poker.Application.Abstractions;

public sealed record UserSummary(string UserId, string Email, string TimeZoneId, string DisplayName);

public interface IUserRepository
{
    Task<UserSummary?> GetAsync(string userId);
    Task<IReadOnlyList<UserSummary>> ListAllAsync();
    Task UpsertAsync(UserSummary user);
}
