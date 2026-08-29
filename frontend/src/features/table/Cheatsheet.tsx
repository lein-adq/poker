import { useState } from "react";

const HAND_RANKINGS = [
  { name: "Royal Flush", example: "A♠ K♠ Q♠ J♠ T♠", description: "Ace-high straight flush." },
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

export default function Cheatsheet() {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button className="cheatsheet-button" title="Hand rankings" onClick={() => setOpen(true)}>
        ?
      </button>
      {open && (
        <div className="modal-backdrop" onClick={() => setOpen(false)}>
          <div className="modal cheatsheet" onClick={(e) => e.stopPropagation()}>
            <h2>Hand Rankings (best to worst)</h2>
            <table>
              <tbody>
                {HAND_RANKINGS.map((h, i) => (
                  <tr key={h.name}>
                    <td className="rank-number">{i + 1}</td>
                    <td>
                      <strong>{h.name}</strong>
                      <div className="hand-example">{h.example}</div>
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
