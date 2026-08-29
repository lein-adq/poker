using Poker.Api.Hubs;
using Poker.Application.Tables;
using Poker.Domain.Betting;
using Xunit;

namespace Poker.Api.Tests;

/// <summary>
/// Behaviour at the hub boundary: what each connection is actually sent, and what a dropped connection
/// actually does to a table. These paths are invisible to the service-level tests.
/// </summary>
public class TableHubTests
{
    [Fact]
    public async Task EveryViewerIsSentTheirOwnCards_AndNobodyElsesUntilShowdown()
    {
        var h = await TableHubHarness.CreateAsync("alice", "bob");
        await h.Hub("carol").JoinAsSpectator(h.TableId);
        h.Clients.Clear();

        await h.Hub(h.CurrentActor).Act(h.TableId, BettingActionType.Call, 0);
        Assert.Null(h.Table.CurrentHand!.Result); // hand still live, so nothing is revealed yet

        var toAlice = h.LastStateSentTo("alice");
        Assert.NotNull(TableHubHarness.SeatOf(toAlice, "alice").HoleCards);
        Assert.Null(TableHubHarness.SeatOf(toAlice, "bob").HoleCards);

        var toBob = h.LastStateSentTo("bob");
        Assert.NotNull(TableHubHarness.SeatOf(toBob, "bob").HoleCards);
        Assert.Null(TableHubHarness.SeatOf(toBob, "alice").HoleCards);

        // A spectator sees nobody's cards — not even of the player whose action triggered the broadcast.
        var toCarol = h.LastStateSentTo("carol");
        Assert.Null(TableHubHarness.SeatOf(toCarol, "alice").HoleCards);
        Assert.Null(TableHubHarness.SeatOf(toCarol, "bob").HoleCards);
    }

    [Fact]
    public async Task ActThatEndsAHand_LeavesTheShowdownVisible_AndDoesNotDealTheNextHandItself()
    {
        var h = await TableHubHarness.CreateAsync("alice", "bob");
        var firstHand = h.Table.CurrentHand;

        await h.Hub(h.CurrentActor).Act(h.TableId, BettingActionType.Fold, 0);

        // Act used to sleep three seconds and then start the next hand, which tied the table's clock to
        // the acting client staying connected. Dealing the next hand belongs to the ticker now.
        Assert.Same(firstHand, h.Table.CurrentHand);
        Assert.NotNull(h.Table.CurrentHand!.Result);
        Assert.NotNull(h.LastStateSentTo("bob").Hand!.Result);
    }

    [Fact]
    public async Task TheTicker_DealsTheNextHandAndBroadcastsIt_WithNoClientInvolved()
    {
        var h = await TableHubHarness.CreateAsync("alice", "bob");
        await h.Hub(h.CurrentActor).Act(h.TableId, BettingActionType.Fold, 0);
        var finishedHand = h.Table.CurrentHand;
        h.Clients.Clear();

        h.Clock.UtcNow += TableService.NextHandDelay;
        Assert.True(await h.TickAsync());

        Assert.NotSame(finishedHand, h.Table.CurrentHand);
        Assert.Null(h.Table.CurrentHand!.Result);

        // Both players learn about it without either having sent anything.
        Assert.NotEmpty(h.Clients.To("alice"));
        Assert.NotEmpty(h.Clients.To("bob"));
        Assert.Null(h.LastStateSentTo("alice").Hand!.Result);
    }

    [Fact]
    public async Task ClosingOneOfTwoTabs_DoesNotSitThePlayerOut()
    {
        var h = await TableHubHarness.CreateAsync("alice", "bob");

        // Bob opens the table in a second tab. SignalR also hands out a fresh connection id on every
        // automatic reconnect, so "a connection dropped" must not mean "the player left".
        var secondTab = h.Hub("bob", "conn-bob-second-tab");
        await secondTab.JoinAsSpectator(h.TableId);

        await h.Hub("bob").OnDisconnectedAsync(null);
        Assert.False(h.Table.FindSeat("bob")!.IsSittingOut);

        await secondTab.OnDisconnectedAsync(null);
        Assert.True(h.Table.FindSeat("bob")!.IsSittingOut);
    }

    [Fact]
    public async Task LosingTheLastConnection_SitsThePlayerOut_AndTellsEveryoneStillWatching()
    {
        var h = await TableHubHarness.CreateAsync("alice", "bob");
        h.Clients.Clear();

        await h.Hub("bob").OnDisconnectedAsync(null);

        Assert.True(TableHubHarness.SeatOf(h.LastStateSentTo("alice"), "bob").IsSittingOut);
    }

    [Fact]
    public async Task DisconnectingAConnectionThatNeverJoined_IsIgnored()
    {
        var h = await TableHubHarness.CreateAsync("alice", "bob");
        h.Clients.Clear();

        // No table on this connection's Items bag: nothing to react to, and nothing to blow up on.
        await h.Hub("dave", "conn-dave-never-joined").OnDisconnectedAsync(null);

        Assert.Empty(h.Clients.Sent);
        Assert.False(h.Table.FindSeat("alice")!.IsSittingOut);
    }

    [Fact]
    public async Task LeavingStopsTheirBroadcasts()
    {
        var h = await TableHubHarness.CreateAsync("alice", "bob");
        var carol = h.Hub("carol");
        await carol.JoinAsSpectator(h.TableId);
        await carol.Leave(h.TableId);

        h.Clients.Clear();
        await h.Hub(h.CurrentActor).Act(h.TableId, BettingActionType.Call, 0);

        Assert.Empty(h.Clients.To("carol"));
        Assert.NotEmpty(h.Clients.To("alice"));
        Assert.DoesNotContain(
            (TableHubHarness.ConnectionOf("carol"), $"table:{h.TableId}"), h.GroupManager.Groups);
    }

    [Fact]
    public async Task Chat_GoesToTheTableGroup_TagsSpectators_AndIsTruncated()
    {
        var h = await TableHubHarness.CreateAsync("alice", "bob");
        await h.Hub("carol").JoinAsSpectator(h.TableId);
        h.Clients.Clear();

        await h.Hub("alice").SendChatMessage(h.TableId, "nice hand");
        await h.Hub("carol").SendChatMessage(h.TableId, new string('x', 600));
        await h.Hub("bob").SendChatMessage(h.TableId, "   ");

        var chats = h.Clients.Sent.Where(m => m.Method == "ChatMessage").ToList();
        Assert.Equal(2, chats.Count); // the whitespace-only message is dropped
        Assert.All(chats, m => Assert.Equal($"group:table:{h.TableId}", m.Target));

        Assert.False((bool)Prop(chats[0].Args[0]!, "isSpectator")!);
        Assert.Equal("nice hand", Prop(chats[0].Args[0]!, "message"));

        Assert.True((bool)Prop(chats[1].Args[0]!, "isSpectator")!);
        Assert.Equal(500, ((string)Prop(chats[1].Args[0]!, "message")!).Length);
    }

    private static object? Prop(object target, string name) =>
        target.GetType().GetProperty(name)!.GetValue(target);
}
