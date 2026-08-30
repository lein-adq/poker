using Microsoft.EntityFrameworkCore;
using Poker.Application.Wallet;

namespace Poker.Infrastructure.Persistence;

public sealed class UserEntity
{
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public string DisplayName { get; set; } = string.Empty;
    public required string TimeZoneId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class LedgerEntryEntity
{
    public Guid Id { get; init; }
    public required string UserId { get; init; }
    public LedgerEntryType Type { get; init; }
    public int Amount { get; init; }
    public Guid? TableId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class PokerDbContext(DbContextOptions<PokerDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<LedgerEntryEntity> LedgerEntries => Set<LedgerEntryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(e =>
        {
            e.HasKey(u => u.UserId);
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<LedgerEntryEntity>(e =>
        {
            e.HasKey(l => l.Id);
            e.HasIndex(l => new { l.UserId, l.Type });
        });
    }
}
