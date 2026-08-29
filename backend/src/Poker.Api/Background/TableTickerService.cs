using Poker.Api.Hubs;
using Poker.Application.Tables;

namespace Poker.Api.Background;

/// <summary>
/// The server-side clock for every live table.
///
/// Without it the game only advances when a client happens to send something: a player who closes their
/// laptop while holding the action freezes the table for everyone else indefinitely, and the pause
/// between hands depends on the previous actor staying connected long enough to trigger the next deal.
/// Each tick asks every table whether its action clock has expired or its next hand is due, and
/// re-broadcasts only the tables that actually changed.
/// </summary>
public sealed class TableTickerService(
    IServiceScopeFactory scopeFactory,
    ILogger<TableTickerService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAllTablesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad table must not take the clock down for every other table.
                logger.LogError(ex, "Table tick failed.");
            }
        }
    }

    private async Task TickAllTablesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var tables = scope.ServiceProvider.GetRequiredService<TableService>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<TableBroadcaster>();

        foreach (var table in await tables.ListTablesAsync())
        {
            ct.ThrowIfCancellationRequested();

            if (await tables.TickAsync(table.Config.Id))
            {
                await broadcaster.BroadcastAsync(table.Config.Id);
            }
        }
    }
}
