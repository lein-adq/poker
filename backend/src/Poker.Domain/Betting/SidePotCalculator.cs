namespace Poker.Domain.Betting;

public sealed record Pot(int Amount, IReadOnlyList<string> EligiblePlayerIds);

public sealed record PlayerContribution(string PlayerId, int CommittedTotal, bool IsFolded);

/// <summary>
/// Splits the total chips committed across a hand into a main pot and any side pots,
/// based on how much each player committed and whether they folded before showdown.
/// </summary>
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
                pots.Add(new Pot(amount, eligible));
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
            if (merged.Count > 0 && merged[^1].EligiblePlayerIds.SequenceEqual(pot.EligiblePlayerIds))
            {
                var last = merged[^1];
                merged[^1] = last with { Amount = last.Amount + pot.Amount };
            }
            else
            {
                merged.Add(pot);
            }
        }
        return merged;
    }
}
