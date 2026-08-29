using System.Text.Json.Serialization;

namespace Poker.Application.Tables;

public sealed record WaitlistEntry(string PlayerId, int RequestedBuyIn);

public sealed record TableSummary(
    Guid Id,
    string Name,
    int SeatedPlayerCount,
    int MaxSeats,
    int MinBuyIn,
    int MaxBuyIn,
    bool IsPrivate,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] TableStatus Status,
    int WaitlistCount)
{
    public static TableSummary From(TableState table) => new(
        table.Config.Id,
        table.Config.Name,
        table.SeatedPlayerCount,
        table.Config.MaxSeats,
        table.Config.MinBuyIn,
        table.Config.MaxBuyIn,
        table.Config.IsPrivate,
        table.Status,
        table.Waitlist.Count);
}
