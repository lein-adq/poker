import type { EquityDto } from "./types";

/** Shown during an all-in reveal (before the river) and at showdown, per the PRD. */
export default function EquityBar({ equity, playerId }: { equity: EquityDto[]; playerId: string }) {
  const mine = equity.find((e) => e.playerId === playerId);
  if (!mine) return null;

  return (
    <div className="equity-bar" title="Chance of winning this hand">
      <div className="equity-fill" style={{ width: `${mine.winPercent}%` }} />
      <span className="equity-label">
        {mine.winPercent.toFixed(1)}% win{mine.tiePercent > 0.5 ? ` · ${mine.tiePercent.toFixed(1)}% tie` : ""}
      </span>
    </div>
  );
}
