using System.Security.Cryptography;
using Poker.Domain.Cards;

namespace Poker.GameEngine.Equity;

public readonly record struct EquityResult(double WinPercent, double TiePercent);

/// <summary>
/// Monte Carlo equity calculator for revealed hands (e.g. an all-in showdown before the river).
/// All participants' hole cards must be known; this does not model hidden opponent ranges.
/// </summary>
public static class EquityCalculator
{
    public static Dictionary<string, EquityResult> Calculate(
        IReadOnlyDictionary<string, IReadOnlyList<Card>> playerHoleCards,
        IReadOnlyList<Card> board,
        int trials = 5000)
    {
        if (playerHoleCards.Count < 2)
        {
            return playerHoleCards.Keys.ToDictionary(k => k, _ => new EquityResult(100.0, 0.0));
        }

        var known = new HashSet<Card>(playerHoleCards.Values.SelectMany(c => c).Concat(board));
        var remainingDeck = Enum.GetValues<Suit>()
            .SelectMany(s => Enum.GetValues<Rank>().Select(r => new Card(r, s)))
            .Where(c => !known.Contains(c))
            .ToList();

        int neededBoardCards = 5 - board.Count;
        int effectiveTrials = neededBoardCards == 0 ? 1 : trials;

        var wins = playerHoleCards.Keys.ToDictionary(k => k, _ => 0);
        var ties = playerHoleCards.Keys.ToDictionary(k => k, _ => 0);

        for (int t = 0; t < effectiveTrials; t++)
        {
            var completedBoard = neededBoardCards == 0
                ? board
                : board.Concat(SampleWithoutReplacement(remainingDeck, neededBoardCards)).ToList();

            HandValue? best = null;
            var bestPlayers = new List<string>(playerHoleCards.Count);
            foreach (var (playerId, hole) in playerHoleCards)
            {
                var value = HandEvaluator.EvaluateBest(hole.Concat(completedBoard).ToList());
                if (best is null || value > best.Value)
                {
                    best = value;
                    bestPlayers.Clear();
                    bestPlayers.Add(playerId);
                }
                else if (value == best.Value)
                {
                    bestPlayers.Add(playerId);
                }
            }

            if (bestPlayers.Count == 1)
            {
                wins[bestPlayers[0]]++;
            }
            else
            {
                foreach (var p in bestPlayers)
                {
                    ties[p]++;
                }
            }
        }

        return playerHoleCards.Keys.ToDictionary(
            k => k,
            k => new EquityResult(100.0 * wins[k] / effectiveTrials, 100.0 * ties[k] / effectiveTrials));
    }

    private static List<Card> SampleWithoutReplacement(List<Card> pool, int count)
    {
        var indices = Enumerable.Range(0, pool.Count).ToArray();
        var result = new List<Card>(count);
        for (int i = 0; i < count; i++)
        {
            int j = RandomNumberGenerator.GetInt32(i, indices.Length);
            (indices[i], indices[j]) = (indices[j], indices[i]);
            result.Add(pool[indices[i]]);
        }
        return result;
    }
}
