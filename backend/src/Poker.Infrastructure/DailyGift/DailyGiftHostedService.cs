using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Poker.Application.Abstractions;
using Poker.Application.Wallet;

namespace Poker.Infrastructure.DailyGift;

/// <summary>
/// Every minute, grants the 300-chip daily gift to any user whose local time (per their stored IANA
/// timezone) has just reached 06:00. Idempotent via <see cref="WalletService.TryGrantDailyGiftAsync"/>'s
/// Redis-backed per-local-date dedupe key, so re-checking the same user within the same local day is safe.
/// </summary>
public sealed class DailyGiftHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<DailyGiftHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private const int GiftHourLocal = 6;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Daily gift sweep failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var wallet = scope.ServiceProvider.GetRequiredService<WalletService>();

        var utcNow = DateTimeOffset.UtcNow;
        foreach (var user in await users.ListAllAsync())
        {
            ct.ThrowIfCancellationRequested();

            TimeZoneInfo tz;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(user.TimeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                continue;
            }

            var local = TimeZoneInfo.ConvertTime(utcNow, tz);
            if (local.Hour != GiftHourLocal)
            {
                continue;
            }

            bool granted = await wallet.TryGrantDailyGiftAsync(user.UserId, DateOnly.FromDateTime(local.Date));
            if (granted)
            {
                logger.LogInformation("Granted daily gift to {UserId} at local time {Local}", user.UserId, local);
            }
        }
    }
}
