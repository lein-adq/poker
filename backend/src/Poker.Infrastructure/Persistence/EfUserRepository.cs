using Microsoft.EntityFrameworkCore;
using Poker.Application.Abstractions;

namespace Poker.Infrastructure.Persistence;

public sealed class EfUserRepository(PokerDbContext db) : IUserRepository
{
    public async Task<UserSummary?> GetAsync(string userId)
    {
        var entity = await db.Users.FindAsync(userId);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<UserSummary>> ListAllAsync() =>
        (await db.Users.ToListAsync()).Select(Map).ToList();

    public async Task UpsertAsync(UserSummary user)
    {
        var existing = await db.Users.FindAsync(user.UserId);
        if (existing is null)
        {
            db.Users.Add(new UserEntity
            {
                UserId = user.UserId,
                Email = user.Email,
                TimeZoneId = user.TimeZoneId,
                DisplayName = user.DisplayName,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.TimeZoneId = user.TimeZoneId;
            existing.DisplayName = user.DisplayName;
        }
        await db.SaveChangesAsync();
    }

    private static UserSummary Map(UserEntity e) => new(e.UserId, e.Email, e.TimeZoneId, e.DisplayName);
}
