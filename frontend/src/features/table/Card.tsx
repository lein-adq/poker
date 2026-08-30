import type { CardDto } from "./types";

const RANK_SYMBOLS: Record<string, string> = {
  Two: "2", Three: "3", Four: "4", Five: "5", Six: "6", Seven: "7",
  Eight: "8", Nine: "9", Ten: "10", Jack: "J", Queen: "Q", King: "K", Ace: "A",
};

const SUIT_SYMBOLS: Record<string, string> = { Clubs: "♣", Diamonds: "♦", Hearts: "♥", Spades: "♠" };
const RED_SUITS = new Set(["Diamonds", "Hearts"]);

export function Card({ card }: { card: CardDto }) {
  return (
    <span className={`card ${RED_SUITS.has(card.suit) ? "red" : "black"}`}>
      {RANK_SYMBOLS[card.rank] ?? card.rank}
      {SUIT_SYMBOLS[card.suit] ?? card.suit}
    </span>
  );
}
export function CardText({ card }: { card: CardDto }) {
  const isRed = RED_SUITS.has(card.suit);
  return (
    <span style={{ 
      fontWeight: 'bold', 
      color: isRed ? '#e53e3e' : '#000000',
      backgroundColor: '#ffffff',
      padding: '0.1rem 0.3rem',
      borderRadius: '4px',
      display: 'inline-block',
      fontSize: '0.85rem',
      boxShadow: '0 1px 2px rgba(0,0,0,0.2)'
    }}>
      {RANK_SYMBOLS[card.rank] ?? card.rank}
      {SUIT_SYMBOLS[card.suit] ?? card.suit}
    </span>
  );
}

export function CardBack() {
  return <span className="card card-back" />;
}
