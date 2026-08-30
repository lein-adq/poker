using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Poker.Api.Background;
using Poker.Api.Hubs;
using Poker.Application.Abstractions;
using Poker.Application.Tables;
using Poker.Application.Tests;
using Poker.Application.Wallet;
using Poker.Domain.Betting;
using Poker.GameEngine.Hands;
using InMemoryTableRepository = Poker.Infrastructure.Tables.InMemoryTableRepository;
using Xunit;

namespace Poker.Api.Tests;

/// <summary>
/// Runs the real hosted service against a real container, with scope validation on.
///
/// The service-level tests call <see cref="TableService.TickAsync"/> directly, which proves the rules but
/// not that the background service can actually reach it: it is a singleton resolving a scoped
/// <see cref="TableService"/> and a scoped <see cref="TableBroadcaster"/> through a scope factory, and
/// getting that lifetime relationship wrong fails at runtime, not at compile time.
/// </summary>
public class TableTickerServiceTests
{
    [Fact]
    public async Task TheHostedService_DealsTheNextHandOnItsOwnSchedule_AndBroadcastsIt()
    {
        var clients = new RecordingClients();
        var walletRepo = new InMemoryWalletRepository();
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<ITableRepository, InMemoryTableRepository>();
        services.AddSingleton<IActiveTableTracker, InMemoryActiveTableTracker>();
        services.AddSingleton<IDistributedLock, InMemoryDistributedLock>();
        services.AddSingleton<IWalletRepository>(walletRepo);
        services.AddScoped<WalletService>();
        services.AddScoped<TableService>();
        services.AddSingleton<IHubContext<TableHub>>(new RecordingHubContext<TableHub>(clients));
        services.AddSingleton<IUserRepository>(new DummyUserRepository());
        services.AddScoped<TableBroadcaster>();

        // Mirrors Program.cs's lifetimes. ValidateScopes catches a scoped service captured by a
        // singleton — exactly the mistake a background service that touches request-scoped state invites.
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var config = TestTable.PublicConfig();
        TableState table;
        HandEngine? finishedHand;

        using (var scope = provider.CreateScope())
        {
            var tables = scope.ServiceProvider.GetRequiredService<TableService>();
            await tables.CreateTableAsync(config);

            foreach (var player in new[] { "alice", "bob" })
            {
                await walletRepo.AddEntryAsync(new LedgerEntry(
                    Guid.NewGuid(), player, LedgerEntryType.SignupGrant, 1000, null, clock.UtcNow));
                await tables.SitAsync(config.Id, player, 300);
            }

            Assert.True(await tables.TryStartHandAsync(config.Id));
            table = (await tables.ListTablesAsync()).Single();
            await tables.ApplyPlayerActionAsync(config.Id, table.CurrentHand!.CurrentActorId!, BettingActionType.Fold);
            finishedHand = table.CurrentHand;
        }

        // Advanced before the service starts, so nothing mutates the clock while it is being read.
        clock.UtcNow += TableService.NextHandDelay;
        clients.Clear();

        var ticker = new TableTickerService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<TableTickerService>>());

        await ticker.StartAsync(CancellationToken.None);
        try
        {
            var waited = Stopwatch.StartNew();
            while (ReferenceEquals(table.CurrentHand, finishedHand) && waited.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(25);
            }
        }
        finally
        {
            await ticker.StopAsync(CancellationToken.None);
        }

        Assert.NotSame(finishedHand, table.CurrentHand);
        Assert.Null(table.CurrentHand!.Result);
        Assert.NotEmpty(clients.To("alice"));
        Assert.NotEmpty(clients.To("bob"));
    }

    private sealed class DummyUserRepository : IUserRepository
    {
        public Task<UserSummary?> GetAsync(string userId) => Task.FromResult<UserSummary?>(new UserSummary(userId, "foo@bar", "UTC", userId));
        public Task<IReadOnlyList<UserSummary>> ListAllAsync() => Task.FromResult<IReadOnlyList<UserSummary>>([]);
        public Task UpsertAsync(UserSummary user) => Task.CompletedTask;
    }
}
