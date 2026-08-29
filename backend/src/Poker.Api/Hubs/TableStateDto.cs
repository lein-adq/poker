using Poker.Application.Tables;
using Poker.Domain.Cards;
using Poker.GameEngine.Equity;
using Poker.GameEngine.Hands;

namespace Poker.Api.Hubs;

public sealed record CardDto(string Rank, string Suit)
{
    public static CardDto From(Card c) => new(c.Rank.ToString(), c.Suit.ToString());
}

public sealed record SeatDto(
    int Index,
    string? PlayerId,
    int Stack,
    int PendingRebuyChips,
    bool IsAllIn,
    bool IsFolded,
    IReadOnlyList<CardDto>? HoleCards,
    string? RevealedHandName);

public sealed record PotDto(int Amount, IReadOnlyList<string> WinnerPlayerIds, IReadOnlyList<string> EligiblePlayerIds);

public sealed record HandDto(
    string Street,
    IReadOnlyList<CardDto> Board,
    string? CurrentActorPlayerId,
    IReadOnlyList<PotDto>? Result);

public sealed record EquityDto(string PlayerId, double WinPercent, double TiePercent);

public sealed record TableStateDto(
    Guid TableId,
    string Name,
    string Status,
    int MinBuyIn,
    int MaxBuyIn,
    IReadOnlyList<SeatDto> Seats,
    IReadOnlyList<string> Spectators,
    int WaitlistCount,
    HandDto? Hand,
    IReadOnlyList<EquityDto>? Equity)
{
    /// <summary>
    /// Builds the state as <paramref name="viewerPlayerId"/> should see it: a seated player always sees
    /// their own hole cards; everyone's hole cards become visible once the hand reaches showdown, or
    /// earlier if all remaining players are all-in (the PRD's early-reveal case) — never otherwise.
    /// </summary>
    public static TableStateDto For(TableState table, string? viewerPlayerId)
    {
        var hand = table.CurrentHand;
        bool revealAll = hand is not null && ShouldRevealAllHoleCards(hand);

        var seats = table.Seats.Select(seat =>
        {
            IReadOnlyList<CardDto>? holeCards = null;
            string? revealedHandName = null;

            if (hand is not null && seat.PlayerId is not null && hand.HoleCards.TryGetValue(seat.PlayerId, out var cards))
            {
                bool isViewer = seat.PlayerId == viewerPlayerId;
                bool folded = hand.Players.First(p => p.PlayerId == seat.PlayerId).IsFolded;

                if (isViewer || (revealAll && !folded))
                {
                    holeCards = cards.Select(CardDto.From).ToList();
                }

                if (revealAll && !folded && hand.Board.Count == 5)
                {
                    revealedHandName = HandEvaluator.EvaluateBest(cards.Concat(hand.Board).ToList()).Describe();
                }
            }

            var playerBetState = hand?.Players.FirstOrDefault(p => p.PlayerId == seat.PlayerId);
            return new SeatDto(
                seat.Index,
                seat.PlayerId,
                seat.Stack,
                seat.PendingRebuyChips,
                playerBetState?.IsAllIn ?? false,
                playerBetState?.IsFolded ?? false,
                holeCards,
                revealedHandName);
        }).ToList();

        HandDto? handDto = hand is null ? null : new HandDto(
            hand.CurrentStreet.ToString(),
            hand.Board.Select(CardDto.From).ToList(),
            hand.CurrentActorId,
            hand.Result?.Pots.Select(p => new PotDto(p.Amount, p.WinnerPlayerIds, p.EligiblePlayerIds)).ToList());

        IReadOnlyList<EquityDto>? equity = null;
        if (hand is not null && hand.Result is null && revealAll)
        {
            equity = hand.ComputeLiveEquity()
                .Select(kv => new EquityDto(kv.Key, kv.Value.WinPercent, kv.Value.TiePercent))
                .ToList();
        }

        return new TableStateDto(
            table.Config.Id,
            table.Config.Name,
            table.Status.ToString(),
            table.Config.MinBuyIn,
            table.Config.MaxBuyIn,
            seats,
            table.Spectators.ToList(),
            table.Waitlist.Count,
            handDto,
            equity);
    }

    /// <summary>The PRD's "ALL IN before the river" case: everyone still in the hand has no more chips to bet.</summary>
    private static bool ShouldRevealAllHoleCards(HandEngine hand)
    {
        if (hand.Result is not null)
        {
            return true;
        }

        var active = hand.Players.Where(p => !p.IsFolded).ToList();
        return active.Count > 1 && active.All(p => p.IsAllIn);
    }
}
