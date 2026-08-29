using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace Poker.Api.Tests;

/// <summary>One message a hub sent, and who it was addressed to (e.g. <c>user:alice</c>).</summary>
public sealed record SentMessage(string Target, string Method, object?[] Args)
{
    public T Payload<T>() => (T)Args[0]!;
}

/// <summary>
/// Records everything a hub or hub context sends instead of putting it on a wire. Lets a test assert on
/// what each individual viewer received, which is the only way to check the per-viewer hole-card privacy
/// rule at the boundary that actually matters.
/// </summary>
public sealed class RecordingClients : IHubCallerClients, IHubClients
{
    public List<SentMessage> Sent { get; } = [];

    public IReadOnlyList<SentMessage> To(string userId) =>
        Sent.Where(m => m.Target == $"user:{userId}").ToList();

    public SentMessage LastTo(string userId) => To(userId)[^1];

    public void Clear() => Sent.Clear();

    private RecordingProxy Proxy(string target) => new(target, Sent);

    public IClientProxy All => Proxy("all");
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy("all-except");
    public ISingleClientProxy Client(string connectionId) => Proxy($"connection:{connectionId}");

    // The non-generic IHubCallerClients narrows Caller/Client to ISingleClientProxy while the generic
    // IHubClients<IClientProxy> it inherits still wants IClientProxy, so both shapes must be supplied.
    IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => Proxy($"connection:{connectionId}");
    IClientProxy IHubCallerClients<IClientProxy>.Caller => Proxy("caller");
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy("connections");
    public IClientProxy Group(string groupName) => Proxy($"group:{groupName}");
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
        Proxy($"group-except:{groupName}");
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy("groups");
    public IClientProxy User(string userId) => Proxy($"user:{userId}");
    public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy("users");
    public ISingleClientProxy Caller => Proxy("caller");
    public IClientProxy Others => Proxy("others");
    public IClientProxy OthersInGroup(string groupName) => Proxy($"others-in-group:{groupName}");
}

internal sealed class RecordingProxy(string target, List<SentMessage> sink) : ISingleClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        sink.Add(new SentMessage(target, method, args));
        return Task.CompletedTask;
    }

    public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        sink.Add(new SentMessage(target, method, args));
        return Task.FromResult<T>(default!);
    }
}

public sealed class RecordingHubContext<THub>(RecordingClients clients) : IHubContext<THub> where THub : Hub
{
    public IHubClients Clients { get; } = clients;
    public IGroupManager Groups { get; } = new RecordingGroupManager();
}

/// <summary>Records group membership so tests can assert a connection was actually added or removed.</summary>
public sealed class RecordingGroupManager : IGroupManager
{
    public HashSet<(string ConnectionId, string GroupName)> Groups { get; } = [];

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Groups.Add((connectionId, groupName));
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Groups.Remove((connectionId, groupName));
        return Task.CompletedTask;
    }
}

/// <summary>A single client connection: one user, one connection id, its own <see cref="Items"/> bag.</summary>
public sealed class FakeHubCallerContext(string userId, string connectionId) : HubCallerContext
{
    public override string ConnectionId { get; } = connectionId;
    public override string? UserIdentifier => userId;

    public override ClaimsPrincipal? User { get; } =
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));

    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override CancellationToken ConnectionAborted => CancellationToken.None;

    public override void Abort()
    {
    }
}
