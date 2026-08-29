import { useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import * as signalR from "@microsoft/signalr";
import { createHubConnection } from "../../lib/signalr";
import { useAuth } from "../../context/AuthContext";
import Cheatsheet from "./Cheatsheet";
import EquityBar from "./EquityBar";
import { Card, CardBack } from "./Card";
import ChatPanel from "../chat/ChatPanel";
import { BettingActionType, type ChatMessageDto, type SeatDto, type TableStateDto } from "./types";

export default function TablePage() {
  const { tableId } = useParams<{ tableId: string }>();
  const { session } = useAuth();
  const navigate = useNavigate();
  const myId = session?.identity.id;

  const [table, setTable] = useState<TableStateDto | null>(null);
  const [messages, setMessages] = useState<ChatMessageDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [buyIn, setBuyIn] = useState(0);
  const [raiseTo, setRaiseTo] = useState(0);
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    if (!tableId) return;
    const connection = createHubConnection("/hubs/table");
    connectionRef.current = connection;

    connection.on("TableState", (dto: TableStateDto) => setTable(dto));
    connection.on("ChatMessage", (msg: ChatMessageDto) => setMessages((m) => [...m, msg]));

    // Group membership and the server's connection tracking both belong to the connection that went
    // away, and automatic reconnect hands out a fresh connection id — so rejoining is not optional.
    // This is also what clears the sit-out the server applied when the old connection dropped.
    connection.onreconnected(() => {
      connection.invoke("JoinAsSpectator", tableId).catch((e) => setError(e.message));
    });

    connection
      .start()
      .then(() => connection.invoke("JoinAsSpectator", tableId))
      .catch((e) => setError(e.message));

    return () => {
      connection.invoke("Leave", tableId).catch(() => {});
      connection.stop();
    };
  }, [tableId]);

  // Server-authoritative: this only renders the deadline the server already set. A client with a
  // skewed clock sees a slightly wrong number, never a different outcome.
  const secondsToAct = useSecondsRemaining(table?.hand?.result ? null : table?.hand?.actionDeadlineUtc);

  if (!tableId) return null;

  const invoke = (method: string, ...args: unknown[]) =>
    connectionRef.current?.invoke(method, ...args).catch((e) => setError(e.message));

  const mySeat = table?.seats.find((s) => s.playerId === myId) ?? null;
  const isSeated = !!mySeat;
  const isMyTurn = !!table?.hand && table.hand.currentActorPlayerId === myId;

  return (
    <div className="table-page">
      <header className="table-header">
        <button onClick={() => navigate("/")}>← Lobby</button>
        <h1>{table?.name ?? "Loading…"}</h1>
        <Cheatsheet />
      </header>
      {error && <p className="error">{error}</p>}

      <div className="felt">
        <div className="board">
          {table?.hand?.board.map((c, i) => <Card key={i} card={c} />)}
          {table?.hand && <div className="street-label">{table.hand.street}</div>}
        </div>

        <div className="seats">
          {table?.seats.map((seat) => (
            <SeatView
              key={seat.index}
              seat={seat}
              isMe={seat.playerId === myId}
              isActor={table.hand?.currentActorPlayerId === seat.playerId}
              secondsToAct={secondsToAct}
            />
          ))}
        </div>

        {table?.equity && myId && <EquityBar equity={table.equity} playerId={myId} />}

        {table?.hand?.result && (
          <div className="pot-result">
            {table.hand.result.map((pot, i) => (
              <div key={i}>
                Pot {i + 1}: {pot.amount} chips → {pot.winnerPlayerIds.join(", ")}
              </div>
            ))}
          </div>
        )}
      </div>

      <div className="table-controls">
        {!isSeated && table && (
          <div className="sit-controls">
            <input
              type="number"
              min={table.minBuyIn}
              max={table.maxBuyIn}
              value={buyIn || table.minBuyIn}
              onChange={(e) => setBuyIn(+e.target.value)}
            />
            <button onClick={() => invoke("Sit", tableId, buyIn || table.minBuyIn)}>
              Sit ({table.minBuyIn}–{table.maxBuyIn})
            </button>
          </div>
        )}

        {isSeated && (
          <div className="action-controls">
            {mySeat && mySeat.pendingRebuyChips === 0 && (
              <RebuyControl tableId={tableId} onRebuy={(amount) => invoke("RequestRebuy", tableId, amount)} />
            )}
            {mySeat && mySeat.pendingRebuyChips > 0 && <span>Rebuy of {mySeat.pendingRebuyChips} queued for next hand</span>}

            {isMyTurn && (
              <div className="betting-actions">
                {secondsToAct !== null && (
                  <span className={`action-clock ${secondsToAct <= 5 ? "urgent" : ""}`}>{secondsToAct}s</span>
                )}
                <button onClick={() => invoke("Act", tableId, BettingActionType.Fold, 0)}>Fold</button>
                <button onClick={() => invoke("Act", tableId, BettingActionType.Check, 0)}>Check</button>
                <button onClick={() => invoke("Act", tableId, BettingActionType.Call, 0)}>Call</button>
                <input type="number" value={raiseTo} onChange={(e) => setRaiseTo(+e.target.value)} placeholder="Raise to…" />
                <button onClick={() => invoke("Act", tableId, BettingActionType.Raise, raiseTo)}>Raise</button>
              </div>
            )}

            <button onClick={() => invoke("Leave", tableId)}>Leave table</button>
          </div>
        )}
      </div>

      <ChatPanel messages={messages} onSend={(message) => invoke("SendChatMessage", tableId, message)} />
    </div>
  );
}

/**
 * Seconds left on the current action clock, or null when nobody is on the clock. Recomputed locally
 * between broadcasts so the number ticks down smoothly instead of jumping on each server message.
 */
function useSecondsRemaining(deadlineUtc: string | null | undefined): number | null {
  const [remaining, setRemaining] = useState<number | null>(null);

  // oxlint react(set-state-in-effect): the wall clock is exactly the "external system" an effect is
  // for. Deriving this during render instead would mean calling Date.now() there, which is impure.
  useEffect(() => {
    if (!deadlineUtc) {
      setRemaining(null);
      return;
    }

    const deadline = new Date(deadlineUtc).getTime();
    const update = () => setRemaining(Math.max(0, Math.ceil((deadline - Date.now()) / 1000)));
    update();

    const id = setInterval(update, 250);
    return () => clearInterval(id);
  }, [deadlineUtc]);

  return remaining;
}

function SeatView({
  seat,
  isMe,
  isActor,
  secondsToAct,
}: {
  seat: SeatDto;
  isMe: boolean;
  isActor: boolean;
  secondsToAct: number | null;
}) {
  if (!seat.playerId) {
    return <div className="seat empty">Seat {seat.index + 1} open</div>;
  }

  return (
    <div className={`seat ${isActor ? "acting" : ""} ${seat.isFolded ? "folded" : ""} ${seat.isSittingOut ? "away" : ""}`}>
      <div className="seat-player">
        {seat.playerId.slice(0, 8)} {isMe && "(you)"}
      </div>
      <div className="seat-stack">{seat.stack} chips</div>
      {isActor && secondsToAct !== null && (
        <div className={`seat-clock ${secondsToAct <= 5 ? "urgent" : ""}`}>{secondsToAct}s</div>
      )}
      {seat.isSittingOut && <span className="badge away">AWAY</span>}
      {seat.isAllIn && <span className="badge">ALL IN</span>}
      <div className="hole-cards">
        {seat.holeCards ? seat.holeCards.map((c, i) => <Card key={i} card={c} />) : isMe ? null : <><CardBack /><CardBack /></>}
      </div>
      {seat.revealedHandName && <div className="hand-name">{seat.revealedHandName}</div>}
    </div>
  );
}

function RebuyControl({ onRebuy }: { tableId: string; onRebuy: (amount: number) => void }) {
  const [amount, setAmount] = useState(100);
  return (
    <span className="rebuy-control">
      <input type="number" value={amount} min={1} onChange={(e) => setAmount(+e.target.value)} />
      <button onClick={() => onRebuy(amount)}>Add chips</button>
    </span>
  );
}
