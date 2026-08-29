using Poker.Application.Tables;
using Poker.Application.Wallet;
using Poker.Domain.Betting;
using Poker.GameEngine.Hands;
using Xunit;

namespace Poker.Application.Tests;

/// <summary>
/// Chips must never be created or destroyed by table operations. The only two ledger entries that move
/// chips in or out of a table are the buy-in debit and the cash-out credit, so at every point in a
/// session this must hold:
///
///     sum(wallet balances) + sum(seat stacks + queued rebuys) + chips committed to the live pot == constant
///
/// The mid-hand paths are what make this non-trivial: while a hand runs the <see cref="HandEngine"/> owns
/// the authoritative stack, and a folded player is allowed to cash out and walk away before it ends.
/// </summary>
public class ChipConservationTests
{
    private static (TableService Svc, InMemoryWalletRepository WalletRepo, FixedClock Clock) Build()
    {
        var walletRepo = new InMemoryWalletRepository();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var wallet = new WalletService(walletRepo, clock);
        var svc = new TableService(
            new InMemoryTableRepository(), new InMemoryActiveTableTracker(), new InMemoryDistributedLock(), wallet, clock);
        return (svc, walletRepo, clock);
    }

    private static Task GrantChips(InMemoryWalletRepository repo, string userId, int amount) =>
        repo.AddEntryAsync(new LedgerEntry(Guid.NewGuid(), userId, LedgerEntryType.SignupGrant, amount, null, DateTimeOffset.UtcNow));

    private static int ChipsOnTable(TableState table)
    {
        int inSeats = table.Seats.Where(s => !s.IsEmpty).Sum(s => s.Stack + s.PendingRebuyChips);

        // While a hand is live, committed chips sit in the pot rather than in anybody's stack. Once it
        // has a Result those chips have been paid back out into stacks, so counting them again would
        // double up. Players who left mid-hand still have chips in the pot and are counted here.
        int inPot = table.CurrentHand is { Result: null } hand ? hand.Players.Sum(p => p.CommittedTotal) : 0;

        return inSeats + inPot;
    }

    private static async Task AssertConserved(
        InMemoryWalletRepository repo, IEnumerable<string> players, TableState table, int expected, string context)
    {
        int wallets = 0;
        foreach (var p in players)
        {
            wallets += await repo.GetBalanceAsync(p);
        }

        int actual = wallets + ChipsOnTable(table);
        int drift = actual - expected;
        Assert.True(
            drift == 0,
            $"Chip conservation broken {context}: expected {expected} chips in the system, found {actual} " +
            $"({(drift > 0 ? $"{drift} created from nothing" : $"{-drift} destroyed")}).");
    }

    [Fact]
    public async Task FoldedPlayerLeavingMidHand_CannotWalkOffWithChipsAlreadyInThePot()
    {
        var (svc, walletRepo, _) = Build();
        var config = TestTable.PublicConfig();
        await svc.CreateTableAsync(config);

        string[] players = ["alice", "bob", "carol"];
        foreach (var p in players)
        {
            await GrantChips(walletRepo, p, 1000);
        }

        foreach (var p in players)
        {
            await svc.SitAsync(config.Id, p, 300);
        }
        Assert.True(await svc.TryStartHandAsync(config.Id));

        var table = (await svc.ListTablesAsync()).Single();
        await AssertConserved(walletRepo, players, table, 3000, "after the hand was dealt");

        // Three-handed: alice holds the button and acts first preflop, bob posts the small blind (10).
        await svc.ApplyPlayerActionAsync(config.Id, "alice", BettingActionType.Call);
        await svc.ApplyPlayerActionAsync(config.Id, "bob", BettingActionType.Fold);

        // Bob's 10 small-blind chips are in the pot and stay there — his seat is worth 290, not 300.
        Assert.Equal(290, table.FindSeat("bob")!.Stack);
        Assert.Null(table.CurrentHand!.Result); // alice and carol are still contesting the pot

        await svc.LeaveAsync(config.Id, "bob");

        Assert.Equal(990, await walletRepo.GetBalanceAsync("bob")); // 1000 - 300 buy-in + 290 cash-out
        await AssertConserved(walletRepo, players, table, 3000, "after a folded player cashed out mid-hand");
    }

    [Fact]
    public async Task LeavingWithAQueuedRebuy_ReturnsThoseChipsToTheWallet()
    {
        var (svc, walletRepo, _) = Build();
        var config = TestTable.PublicConfig();
        await svc.CreateTableAsync(config);

        string[] players = ["alice", "bob", "carol"];
        foreach (var p in players)
        {
            await GrantChips(walletRepo, p, 1000);
        }

        foreach (var p in players)
        {
            await svc.SitAsync(config.Id, p, 300);
        }
        Assert.True(await svc.TryStartHandAsync(config.Id));
        var table = (await svc.ListTablesAsync()).Single();

        // A top-up requested mid-hand is debited immediately but only applied at the hand boundary.
        await svc.RequestRebuyAsync(config.Id, "bob", 100);
        Assert.Equal(600, await walletRepo.GetBalanceAsync("bob"));
        Assert.Equal(100, table.FindSeat("bob")!.PendingRebuyChips);
        await AssertConserved(walletRepo, players, table, 3000, "after a queued rebuy");

        await svc.ApplyPlayerActionAsync(config.Id, "alice", BettingActionType.Call);
        await svc.ApplyPlayerActionAsync(config.Id, "bob", BettingActionType.Fold);
        await svc.LeaveAsync(config.Id, "bob");

        // 290 live chips + the 100 that never made it onto the felt; bob is down only his blind.
        Assert.Equal(990, await walletRepo.GetBalanceAsync("bob"));
        await AssertConserved(walletRepo, players, table, 3000, "after leaving with a queued rebuy");
    }

    [Fact]
    public async Task BuyingBackIntoTheSeatYouJustLeft_DoesNotInheritTheOldStack()
    {
        var (svc, walletRepo, _) = Build();
        var config = TestTable.PublicConfig();
        await svc.CreateTableAsync(config);

        string[] players = ["alice", "bob", "carol"];
        foreach (var p in players)
        {
            await GrantChips(walletRepo, p, 1000);
        }

        foreach (var p in players)
        {
            await svc.SitAsync(config.Id, p, 300);
        }
        Assert.True(await svc.TryStartHandAsync(config.Id));
        var table = (await svc.ListTablesAsync()).Single();

        await svc.ApplyPlayerActionAsync(config.Id, "alice", BettingActionType.Call);
        await svc.ApplyPlayerActionAsync(config.Id, "bob", BettingActionType.Fold);
        await svc.LeaveAsync(config.Id, "bob");

        // Bob re-buys while the hand he folded out of is still running, and lands back in his old seat.
        await svc.SitAsync(config.Id, "bob", 500);
        Assert.Equal(500, table.FindSeat("bob")!.Stack);

        // Carol acting must not cause bob's stale in-hand stack (290) to be stamped over that buy-in.
        await svc.ApplyPlayerActionAsync(config.Id, "carol", BettingActionType.Check);
        Assert.Equal(500, table.FindSeat("bob")!.Stack);

        await AssertConserved(walletRepo, players, table, 3000, "after buying back into a vacated seat");
    }

    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 150).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task RandomisedSession_NeverCreatesOrDestroysChips(int seed)
    {
        var rng = new Random(seed);
        var (svc, walletRepo, clock) = Build();
        var config = TestTable.PublicConfig(maxSeats: 6, minBuyIn: 100, maxBuyIn: 1000);
        await svc.CreateTableAsync(config);

        const int grantPerPlayer = 100_000;
        var players = Enumerable.Range(0, rng.Next(2, 7)).Select(i => $"p{i}").ToList();
        foreach (var p in players)
        {
            await GrantChips(walletRepo, p, grantPerPlayer);
        }
        int expected = grantPerPlayer * players.Count;

        foreach (var p in players)
        {
            await svc.SitAsync(config.Id, p, rng.Next(config.MinBuyIn, config.MaxBuyIn + 1));
        }

        var table = (await svc.ListTablesAsync()).Single();
        await AssertConserved(walletRepo, players, table, expected, "after seating");

        for (int handNumber = 0; handNumber < 8; handNumber++)
        {
            if (!await svc.TryStartHandAsync(config.Id))
            {
                break;
            }
            await AssertConserved(walletRepo, players, table, expected, $"after dealing hand {handNumber}");

            int guard = 0;
            while (table.CurrentHand is { Result: null } hand && hand.CurrentActorId is { } actorId)
            {
                Assert.True(guard++ < 500, $"hand {handNumber} never terminated (seed {seed})");

                if (rng.Next(10) == 0)
                {
                    // Let the action clock run out rather than acting: the table acts for them, and that
                    // path has to conserve chips exactly like a player-supplied action does.
                    clock.UtcNow += TableService.ActionTimeout;
                    await svc.TickAsync(config.Id);
                    await AssertConserved(
                        walletRepo, players, table, expected, $"after {actorId}'s clock expired in hand {handNumber}");
                }
                else
                {
                    var (action, amount) = ChooseAction(hand, actorId, rng);
                    await svc.ApplyPlayerActionAsync(config.Id, actorId, action, amount);
                    await AssertConserved(
                        walletRepo, players, table, expected, $"after {actorId} played {action} {amount} in hand {handNumber}");
                }

                string churn = await ChurnAsync(svc, config, table, players, rng);
                if (churn.Length > 0)
                {
                    await AssertConserved(walletRepo, players, table, expected, $"{churn} in hand {handNumber}");
                }
            }
        }

        // Everyone racks up and goes home: with the table emptied, every chip must be back in a wallet.
        foreach (var p in players)
        {
            await svc.LeaveAsync(config.Id, p);
        }
        Assert.Equal(0, ChipsOnTable(table));
        await AssertConserved(walletRepo, players, table, expected, "after every player cashed out");
    }

    private static (BettingActionType Action, int Amount) ChooseAction(HandEngine hand, string actorId, Random rng)
    {
        var legal = hand.GetLegalActions(actorId);
        var actor = hand.Players.Single(p => p.PlayerId == actorId);

        // CallAmount is capped by the actor's stack, so for a short stack this lands exactly on
        // MaxRaiseTo and correctly reports that no raise is available.
        int currentBet = actor.CommittedThisRound + legal.CallAmount;
        bool canRaise = legal.MaxRaiseTo > currentBet;

        int roll = rng.Next(100);
        if (roll < 12)
        {
            return (BettingActionType.Fold, 0);
        }
        if (canRaise && roll < 45)
        {
            // Biased towards the top of the range so all-ins and side pots come up often.
            int raiseTo = rng.Next(2) == 0
                ? legal.MaxRaiseTo
                : rng.Next(legal.MinRaiseTo, legal.MaxRaiseTo + 1);
            return (legal.CanCheck ? BettingActionType.Bet : BettingActionType.Raise, raiseTo);
        }
        if (legal.CanCall)
        {
            return (BettingActionType.Call, 0);
        }
        return legal.CanCheck ? (BettingActionType.Check, 0) : (BettingActionType.Fold, 0);
    }

    /// <summary>
    /// Interleaves the mutations that read <see cref="Seat.Stack"/> while a hand is in flight — the ones
    /// that turn a stale seat stack into created or destroyed chips. Returns a description of what it
    /// did, or an empty string if it did nothing.
    /// </summary>
    private static async Task<string> ChurnAsync(
        TableService svc, TableConfig config, TableState table, IReadOnlyList<string> players, Random rng)
    {
        switch (rng.Next(8))
        {
            case 0:
            {
                var candidates = table.Seats
                    .Where(s => !s.IsEmpty)
                    .Select(s => s.PlayerId!)
                    .Where(id => CanLeaveNow(table, id))
                    .ToList();
                if (candidates.Count == 0)
                {
                    return "";
                }

                string leaver = candidates[rng.Next(candidates.Count)];
                await svc.LeaveAsync(config.Id, leaver);
                return $"after {leaver} cashed out mid-hand";
            }

            case 1:
            {
                var seated = table.Seats.Where(s => !s.IsEmpty).ToList();
                if (seated.Count == 0)
                {
                    return "";
                }

                var seat = seated[rng.Next(seated.Count)];
                try
                {
                    await svc.RequestRebuyAsync(config.Id, seat.PlayerId!, rng.Next(1, 300));
                }
                catch (InvalidOperationException)
                {
                    return ""; // would exceed the table's max buy-in
                }
                return $"after {seat.PlayerId} queued a rebuy";
            }

            case 2:
            {
                var away = players.Where(p => table.FindSeat(p) is null).ToList();
                if (away.Count == 0)
                {
                    return "";
                }

                string returner = away[rng.Next(away.Count)];
                try
                {
                    await svc.SitAsync(config.Id, returner, rng.Next(config.MinBuyIn, config.MaxBuyIn + 1));
                }
                catch (InvalidOperationException)
                {
                    return "";
                }
                return $"after {returner} bought back in";
            }

            case 3:
            {
                var seated = table.Seats.Where(s => !s.IsEmpty).ToList();
                if (seated.Count == 0)
                {
                    return "";
                }

                var seat = seated[rng.Next(seated.Count)];
                if (seat.IsSittingOut)
                {
                    await svc.JoinAsSpectatorAsync(config.Id, seat.PlayerId!);
                    return $"after {seat.PlayerId} reconnected";
                }

                await svc.MarkDisconnectedAsync(config.Id, seat.PlayerId!);
                return $"after {seat.PlayerId} disconnected";
            }

            default:
                return "";
        }
    }

    /// <summary>Mirrors TableService's rule: you may only leave once you are out of the live hand.</summary>
    private static bool CanLeaveNow(TableState table, string playerId) =>
        table.CurrentHand is not { Result: null } hand ||
        !hand.Players.Any(p => p.PlayerId == playerId && !p.IsFolded);
}
