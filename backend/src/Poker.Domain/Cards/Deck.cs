using System.Security.Cryptography;

namespace Poker.Domain.Cards;

/// <summary>A shuffled 52-card deck. Uses a CSPRNG so shuffles can't be predicted/replayed.</summary>
public sealed class Deck
{
    private readonly List<Card> _cards;
    private int _next;

    public Deck()
    {
        _cards = new List<Card>(52);
        foreach (Suit suit in Enum.GetValues<Suit>())
        {
            foreach (Rank rank in Enum.GetValues<Rank>())
            {
                _cards.Add(new Card(rank, suit));
            }
        }
        Shuffle();
    }

    public int RemainingCount => _cards.Count - _next;

    public void Shuffle()
    {
        for (int i = _cards.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
        }
        _next = 0;
    }

    public Card Draw()
    {
        if (_next >= _cards.Count)
        {
            throw new InvalidOperationException("Deck is empty.");
        }
        return _cards[_next++];
    }

    public IReadOnlyList<Card> Draw(int count)
    {
        var result = new List<Card>(count);
        for (int i = 0; i < count; i++)
        {
            result.Add(Draw());
        }
        return result;
    }

    /// <summary>Remaining, undealt cards — used by the equity calculator to sample opponent/board outcomes.</summary>
    public IReadOnlyList<Card> RemainingCards() => _cards.Skip(_next).ToList();
}
