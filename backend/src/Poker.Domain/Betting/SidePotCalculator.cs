namespace Poker.Domain.Betting;

/// <param name="EligiblePlayerIds">Players who can still win this pot (contributors who did not fold).</param>
/// <param name="ContributorPlayerIds">
/// Everyone whose chips are in this pot, folded or not. Needed to return a pot that ended up with no
/// eligible player at all — see <see cref="SidePotCalculator"/>.
/// </param>
public sealed record Pot(
    int Amount,
    IReadOnlyList<string> EligiblePlayerIds,
    IReadOnlyList<string> ContributorPlayerIds);

public sealed record PlayerContribution(string PlayerId, int CommittedTotal, bool IsFolded);

/// <summary>
/// Splits the total chips committed across a hand into a main pot and any side pots,
/// based on how much each player committed and whether they folded before showdown.
/// </summary>
/// <remarks>
/// A side pot can legitimately end up with no eligible players: if a short stack is all-in and every
/// player contesting the side pot above them subsequently folds, nobody is left with a claim to it.
/// Those pots are kept unmerged so each one still covers a single betting level, which lets the caller
/// hand every contributor back exactly what they put in.
/// </remarks>
public static class SidePotCalculator
{
    public static List<Pot> Calculate(IReadOnlyList<PlayerContribution> players)
    {
        var contributors = players.Where(p => p.CommittedTotal > 0).ToList();
        var levels = contributors.Select(p => p.CommittedTotal).Distinct().OrderBy(c => c).ToList();

        var pots = new List<Pot>();
        int previousLevel = 0;
        foreach (var level in levels)
        {
            int layerSize = level - previousLevel;
            var layerContributors = contributors.Where(p => p.CommittedTotal >= level).ToList();
            int amount = layerSize * layerContributors.Count;
            if (amount > 0)
            {
                var eligible = layerContributors.Where(p => !p.IsFolded).Select(p => p.PlayerId).ToList();
                pots.Add(new Pot(amount, eligible, layerContributors.Select(p => p.PlayerId).ToList()));
            }
            previousLevel = level;
        }

        return MergeAdjacentPotsWithSameEligibility(pots);
    }

    private static List<Pot> MergeAdjacentPotsWithSameEligibility(List<Pot> pots)
    {
        var merged = new List<Pot>();
        foreach (var pot in pots)
        {
            // Pots nobody is eligible for are never merged: each must stay at a single betting level so
            // that returning it to its contributors divides exactly.
            if (merged.Count > 0 && pot.EligiblePlayerIds.Count > 0 &&
                merged[^1].EligiblePlayerIds.SequenceEqual(pot.EligiblePlayerIds))
            {
                var last = merged[^1];
                merged[^1] = last with
                {
                    Amount = last.Amount + pot.Amount,
                    ContributorPlayerIds = last.ContributorPlayerIds.Union(pot.ContributorPlayerIds).ToList()
                };
            }
            else
            {
                merged.Add(pot);
            }
        }
        return merged;
    }
}
