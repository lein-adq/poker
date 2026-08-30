import { useState } from "react";
import { Card, CardText } from "./Card";
import type { CardDto } from "./types";

const HAND_RANKINGS = [
  { name: "Royal Flush", example: "A♠ K♠ Q♠ J♠ 10♠", description: "Ace-high straight flush." },
  { name: "Straight Flush", example: "9♥ 8♥ 7♥ 6♥ 5♥", description: "Five sequential cards, same suit." },
  { name: "Four of a Kind", example: "9♣ 9♦ 9♥ 9♠ 2♣", description: "Four cards of the same rank." },
  { name: "Full House", example: "K♣ K♦ K♥ 2♠ 2♣", description: "Three of a kind plus a pair." },
  { name: "Flush", example: "2♣ 5♣ 8♣ J♣ A♣", description: "Five cards of the same suit, any order." },
  { name: "Straight", example: "5♣ 6♦ 7♥ 8♠ 9♣", description: "Five sequential cards, any suits." },
  { name: "Three of a Kind", example: "7♣ 7♦ 7♥ K♠ 2♣", description: "Three cards of the same rank." },
  { name: "Two Pair", example: "A♣ A♦ K♥ K♠ 2♣", description: "Two different pairs." },
  { name: "Pair", example: "9♣ 9♦ A♥ K♠ 2♣", description: "Two cards of the same rank." },
  { name: "High Card", example: "A♣ J♦ 8♥ 5♠ 2♣", description: "No matching cards or sequence — highest card plays." },
];

const charToRank: Record<string, string> = { "A": "Ace", "K": "King", "Q": "Queen", "J": "Jack", "10": "Ten", "9": "Nine", "8": "Eight", "7": "Seven", "6": "Six", "5": "Five", "2": "Two" };
const charToSuit: Record<string, string> = { "♠": "Spades", "♥": "Hearts", "♦": "Diamonds", "♣": "Clubs" };

function parseCards(str: string): CardDto[] {
  return str.split(" ").map(c => {
    const isTen = c.startsWith("10");
    const rankStr = isTen ? "10" : c[0];
    const suitStr = isTen ? c[2] : c[1];
    return {
      rank: charToRank[rankStr] || rankStr,
      suit: charToSuit[suitStr] || suitStr
    };
  });
}

export default function Cheatsheet() {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button className="cheatsheet-button" title="Hand rankings" onClick={() => setOpen(true)}>
        ?
      </button>
      {open && (
        <div className="modal-backdrop" style={{ zIndex: 9999 }} onClick={() => setOpen(false)}>
          <div className="modal cheatsheet" onClick={(e) => e.stopPropagation()}>
            <h2>Hand Rankings (best to worst)</h2>
            <table>
              <tbody>
                {HAND_RANKINGS.map((h, i) => (
                  <tr key={h.name}>
                    <td className="rank-number">{i + 1}</td>
                    <td>
                      <strong>{h.name}</strong>
                      <div className="hand-example-cards" style={{ display: 'flex', gap: '0.4rem', marginTop: '0.2rem' }}>
                        {parseCards(h.example).map((c, idx) => (
                          <div key={idx}>
                            <CardText card={c} />
                          </div>
                        ))}
                      </div>
                    </td>
                    <td>{h.description}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <button onClick={() => setOpen(false)}>Close</button>
          </div>
        </div>
      )}
    </>
  );
}
