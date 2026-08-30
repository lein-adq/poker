using Microsoft.AspNetCore.SignalR;
using Poker.Api.Hubs;
using Poker.Application.Tables;
using Poker.Application.Wallet;
using Poker.Application.Tests;
using Xunit;

namespace Poker.Api.Tests;

/// <summary>
/// A table wired up the way the API wires one: the real <see cref="TableService"/>, the real production
/// <see cref="Poker.Infrastructure.Tables.InMemoryTableRepository"/>, the real
/// <see cref="TableBroadcaster"/> and <see cref="TableConnectionRegistry"/> — with only the SignalR
/// transport and the Redis/EF-backed edges swapped for recording stand-ins.
///
/// The point is to assert on what each individual connection actually receives, which is where the
/// per-viewer hole-card privacy rule and the disconnect refcounting either hold or do not.
/// </summary>
public sealed class TableHubHarness
{
    public required TableService TableService { get; init; }
    public required TableBroadcaster Broadcaster { get; init; }
    public required TableConnectionRegistry Connections { get; init; }
    public required RecordingClients Clients { get; init; }
    public required RecordingGroupManager GroupManager { get; init; }
    public required InMemoryWalletRepository WalletRepo { get; init; }
    public required FixedClock Clock { get; init; }
    public required TableConfig Config { get; init; }

    public Guid TableId => Config.Id;

    /// <summary>
    /// SignalR builds a fresh Hub per invocation but keeps one HubCallerContext per connection, and the
    /// Items bag on it is where TableHub remembers which table this connection joined. Caching contexts
    /// here reproduces that; a fresh context per call would silently give every connection an empty bag.
    /// </summary>
    private readonly Dictionary<(string UserId, string ConnectionId), FakeHubCallerContext> _contexts = [];

    /// <summary>Whoever is actually on the clock. Which seat that is depends on the button, so tests
    /// must not assume it.</summary>
    public string CurrentActor => Table.CurrentHand!.CurrentActorId!;

    public TableState Table => TableService.ListTablesAsync().GetAwaiter().GetResult().Single();

    public static string ConnectionOf(string userId) => $"conn-{userId}";

    public static async Task<TableHubHarness> CreateAsync(params string[] seatedPlayers)
    {
        var walletRepo = new InMemoryWalletRepository();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var wallet = new WalletService(walletRepo, clock);
        var tableService = new TableService(
            new Poker.Infrastructure.Tables.InMemoryTableRepository(),
            new InMemoryActiveTableTracker(),
            new InMemoryDistributedLock(),
            wallet,
            clock);

        var config = TestTable.PublicConfig();
        await tableService.CreateTableAsync(config);

        var clients = new RecordingClients();
        var dummyUsers = new DummyUserRepository();
        var harness = new TableHubHarness
        {
            TableService = tableService,
            Broadcaster = new TableBroadcaster(new RecordingHubContext<TableHub>(clients), tableService, dummyUsers),
            Connections = new TableConnectionRegistry(),
            Clients = clients,
            GroupManager = new RecordingGroupManager(),
            WalletRepo = walletRepo,
            Clock = clock,
            Config = config,
        };

        foreach (var player in seatedPlayers)
        {
            await walletRepo.AddEntryAsync(
                new LedgerEntry(Guid.NewGuid(), player, LedgerEntryType.SignupGrant, 1000, null, clock.UtcNow));
            await harness.Hub(player).Sit(config.Id, 300);
        }

        return harness;
    }

    /// <summary>One connection for a user. Defaults to that user's "main" connection id.</summary>
    public TableHub Hub(string userId, string? connectionId = null)
    {
        var key = (userId, connectionId ?? ConnectionOf(userId));
        if (!_contexts.TryGetValue(key, out var context))
        {
            context = new FakeHubCallerContext(key.Item1, key.Item2);
            _contexts[key] = context;
        }

        return new TableHub(TableService, Broadcaster, Connections, new RecordingHubContext<LobbyHub>(Clients))
        {
            Context = context,
            Clients = Clients,
            Groups = GroupManager,
        };
    }

    /// <summary>What the API's ticker does for one table on each tick.</summary>
    public async Task<bool> TickAsync()
    {
        if (!await TableService.TickAsync(TableId))
        {
            return false;
        }

        await Broadcaster.BroadcastAsync(TableId);
        return true;
    }

    public TableStateDto LastStateSentTo(string userId) =>
        Clients.To(userId).Last(m => m.Method == "TableState").Payload<TableStateDto>();

    public static SeatDto SeatOf(TableStateDto state, string playerId) =>
        state.Seats.Single(s => s.PlayerId == playerId);

    private sealed class DummyUserRepository : Poker.Application.Abstractions.IUserRepository
    {
        public Task<Poker.Application.Abstractions.UserSummary?> GetAsync(string userId) => Task.FromResult<Poker.Application.Abstractions.UserSummary?>(new(userId, "test@test.com", "UTC", userId));
        public Task<IReadOnlyList<Poker.Application.Abstractions.UserSummary>> ListAllAsync() => Task.FromResult<IReadOnlyList<Poker.Application.Abstractions.UserSummary>>([]);
        public Task UpsertAsync(Poker.Application.Abstractions.UserSummary user) => Task.CompletedTask;
    }
}
