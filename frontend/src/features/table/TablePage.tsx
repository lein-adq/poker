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
  const [showRaiseControls, setShowRaiseControls] = useState(false);
  const [showSitModal, setShowSitModal] = useState(false);
  const [sitSeatIndex, setSitSeatIndex] = useState<number | null>(null);
  const [showRebuyModal, setShowRebuyModal] = useState(false);
  const [showMenu, setShowMenu] = useState(false);
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  const isMyTurn = table?.hand?.currentActorPlayerId === myId;

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Ignore if user is typing in an input
      if (document.activeElement?.tagName === "INPUT" || document.activeElement?.tagName === "TEXTAREA") return;

      if (!isMyTurn || !tableId) return;

      const legal = table?.hand?.currentLegalActions;

      if (e.key === "Escape" && showRaiseControls) {
        setShowRaiseControls(false);
        return;
      }

      if (e.key === "Enter" && showRaiseControls) {
        invoke("Act", tableId, BettingActionType.Raise, raiseTo);
        setShowRaiseControls(false);
        return;
      }

      if (showRaiseControls) return; // if betting controls open, block other hotkeys except Enter/Esc

      switch (e.key.toLowerCase()) {
        case 'f':
          invoke("Act", tableId, BettingActionType.Fold, 0);
          break;
        case 'k':
          if (legal?.canCheck) invoke("Act", tableId, BettingActionType.Check, 0);
          break;
        case 'c':
          if (legal?.canCall) invoke("Act", tableId, BettingActionType.Call, 0);
          break;
        case 'r':
        case 'b':
          if (!legal || !mySeat || mySeat.stack <= legal.callAmount) break;
          setShowRaiseControls(true);
          setRaiseTo(0);
          break;
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isMyTurn, tableId, raiseTo, showRaiseControls, table]);

  useEffect(() => {
    if (!tableId) return;
    const connection = createHubConnection("/hubs/table");
    connectionRef.current = connection;

    connection.on("TableState", (dto: TableStateDto) => setTable(dto));
    connection.on("ChatMessage", (msg: ChatMessageDto) => setMessages((m) => [...m, msg].slice(-50)));

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
      connection.invoke("Leave", tableId).catch(() => { });
      connection.stop();
    };
  }, [tableId]);

  // Server-authoritative: this only renders the deadline the server already set. A client with a
  // skewed clock sees a slightly wrong number, never a different outcome.
  const secondsToAct = useSecondsRemaining(table?.hand?.result ? null : table?.hand?.actionDeadlineUtc);
  const nextHandSeconds = useSecondsRemaining(table?.nextHandStartUtc);

  if (!tableId) return null;

  const invoke = (method: string, ...args: unknown[]) =>
    connectionRef.current?.invoke(method, ...args).catch((e) => setError(e.message));

  const mySeat = table?.seats.find((s) => s.playerId === myId) ?? null;
  const isSeated = !!mySeat;

  return (
    <div className="table-page">
      <header className="table-header">
        <div className="hamburger-menu-container" onBlur={() => setTimeout(() => setShowMenu(false), 200)} tabIndex={-1}>
          <button className="hamburger-btn" onClick={() => setShowMenu(!showMenu)}>
            ☰
          </button>
          {showMenu && (
            <div className="hamburger-dropdown">
              {isSeated && (
                <button onMouseDown={() => {
                  invoke("Leave", tableId);
                  setShowMenu(false);
                }}>👀 Quedarse como espectador</button>
              )}
              {isSeated && (
                <button onMouseDown={() => {
                  setShowMenu(false);
                  setShowRebuyModal(true);
                }}>💰 Agregar chips</button>
              )}
              <button onMouseDown={() => {
                if (isSeated) invoke("Leave", tableId);
                navigate("/");
              }}>🚪 Salirse de la mesa</button>
            </div>
          )}
        </div>
        <h1>{table?.name ?? "Loading…"}</h1>
        <Cheatsheet />
      </header>
      {error && <p className="error">{error}</p>}

      <div className="table-content-area">
        <div className="table-main-col">
          <div className="felt-container">
            <div className="felt">
              {!table?.hand && nextHandSeconds !== null && nextHandSeconds > 0 && (
                <div className="game-starting-overlay">
                  <h2>Game starts in</h2>
                  <div className="countdown">{nextHandSeconds}</div>
                </div>
              )}
        <div className="board">
          {table?.hand?.board.map((c, i) => <Card key={i} card={c} />)}
        </div>
        {table?.hand && (
          <div className="board-info">
            <div className="street-label">{table.hand.street}</div>
            <div className="total-pot">
              <span className="chip-icon">🪙</span> Pot: {table.hand.totalPot}
            </div>
          </div>
        )}

        <div className="seats">
          {table?.seats.map((seat) => (
            <div key={seat.index}>
              <SeatView
                seat={seat}
                isMe={seat.playerId === myId}
                isActor={table.hand?.currentActorPlayerId === seat.playerId}
                secondsToAct={secondsToAct}
                maxSeats={table.seats.length}
                onSitClick={isSeated ? undefined : () => {
                  setSitSeatIndex(seat.index);
                  setShowSitModal(true);
                }}
              />
              <SeatBetAnimation seat={seat} maxSeats={table.seats.length} />
            </div>
          ))}
        </div>

        {table?.equity && myId && <EquityBar equity={table.equity} playerId={myId} />}

        {table?.hand?.result && table.hand.result.map((pot, potIndex) =>
          pot.winnerPlayerIds.map((winnerId, winnerIndex) => {
            const seat = table.seats.find(s => s.playerId === winnerId);
            if (!seat) return null;
            const angle = (seat.index / table.seats.length) * 2 * Math.PI - Math.PI / 2;
            const left = 50 + 48 * Math.cos(angle);
            const top = 50 + 48 * Math.sin(angle);
            return (
              <div
                key={`${potIndex}-${winnerIndex}`}
                className="flying-chips"
                style={{ '--target-left': `${left}%`, '--target-top': `${top}%` } as React.CSSProperties}
              >
                🪙
              </div>
            );
          })
        )}

        {table?.hand?.result && (
          <div className="hand-results">
            {table.hand.result.map((pot, i) => (
              <div key={i} className="pot-result">
                Pot {i + 1}: {pot.amount} chips → {pot.winnerPlayerIds.map(id => table.seats.find(s => s.playerId === id)?.playerName || id).join(", ")}
              </div>
            ))}
          </div>
        )}
        </div>
      </div>

      <div className="table-controls">
        {showSitModal && table && (
          <SitModal
            minBuyIn={table.minBuyIn}
            maxBuyIn={table.maxBuyIn}
            onClose={() => {
              setShowSitModal(false);
              setSitSeatIndex(null);
            }}
            onSit={(amount) => {
              invoke("Sit", tableId, amount, sitSeatIndex);
              setShowSitModal(false);
              setSitSeatIndex(null);
            }}
          />
        )}

        {showRebuyModal && table && (
          <SitModal
            title="Agregar Chips (Rebuy)"
            buttonText="AGREGAR CHIPS"
            minBuyIn={1}
            maxBuyIn={table.maxBuyIn}
            onClose={() => setShowRebuyModal(false)}
            onSit={(amount) => {
              invoke("RequestRebuy", tableId, amount);
              setShowRebuyModal(false);
            }}
          />
        )}

        {isSeated && (
          <div className="action-controls">
            {mySeat && mySeat.pendingRebuyChips > 0 && <span>Rebuy of {mySeat.pendingRebuyChips} chips queued for next hand</span>}

            {isMyTurn && table && (
              <div className="poker-action-bar">
                {!showRaiseControls ? (
                  <>
                    <button className="poker-btn fold" onClick={() => invoke("Act", tableId, BettingActionType.Fold, 0)}>
                      <span className="hotkey">F</span> FOLD
                    </button>
                    <button
                      className="poker-btn check"
                      onClick={() => invoke("Act", tableId, BettingActionType.Check, 0)}
                      disabled={!table.hand?.currentLegalActions?.canCheck}
                    >
                      <span className="hotkey">K</span> CHECK
                    </button>
                    <button
                      className="poker-btn call"
                      onClick={() => invoke("Act", tableId, BettingActionType.Call, 0)}
                      disabled={!table.hand?.currentLegalActions?.canCall}
                    >
                      <span className="hotkey">C</span> CALL {table.hand?.currentLegalActions?.canCall ? table.hand.currentLegalActions.callAmount : ''}
                    </button>
                    <button
                      className="poker-btn raise-outline"
                      onClick={() => {
                        setShowRaiseControls(true);
                        setRaiseTo(0);
                      }}
                      disabled={!table.hand?.currentLegalActions || !mySeat || mySeat.stack <= table.hand.currentLegalActions.callAmount}
                    >
                      <span className="hotkey">R</span> RAISE
                    </button>
                  </>
                ) : (() => {
                  const legal = table.hand?.currentLegalActions;
                  const maxRaiseTo = legal?.maxRaiseTo || mySeat?.stack || 0;
                  const minRaiseTo = Math.min(legal?.minRaiseTo || table.bigBlind, maxRaiseTo);

                  return (
                    <div className="bet-controls">
                      <div className="bet-amount-box">
                        <span className="label">Your bet</span>
                        <input
                          type="number"
                          min={minRaiseTo}
                          max={maxRaiseTo}
                          value={raiseTo}
                          onChange={(e) => setRaiseTo(+e.target.value)}
                          autoFocus
                        />
                      </div>
                      <div className="bet-slider-container">
                        <div className="quick-bets">
                          <button onClick={() => setRaiseTo(minRaiseTo)}>MIN</button>
                          <button onClick={() => setRaiseTo(Math.max(minRaiseTo, Math.floor((table.hand?.totalPot || 0) / 2)))}>1/2 POT</button>
                          <button onClick={() => setRaiseTo(Math.max(minRaiseTo, Math.floor((table.hand?.totalPot || 0) * 0.75)))}>3/4 POT</button>
                          <button onClick={() => setRaiseTo(Math.max(minRaiseTo, Math.floor(table.hand?.totalPot || 0)))}>POT</button>
                          <button onClick={() => setRaiseTo(maxRaiseTo)}>ALL IN</button>
                        </div>
                        <div className="slider-row">
                          <span className="slider-btn" onClick={() => setRaiseTo(r => Math.max(minRaiseTo, r - table.bigBlind))}>-</span>
                          <input type="range" min={minRaiseTo} max={maxRaiseTo} value={raiseTo < minRaiseTo ? minRaiseTo : raiseTo} onChange={(e) => setRaiseTo(Number(e.target.value))} />
                          <span className="slider-btn" onClick={() => setRaiseTo(r => Math.min(maxRaiseTo, Math.max(minRaiseTo, r) + table.bigBlind))}>+</span>
                        </div>
                      </div>
                      <div className="action-submit-container">
                        <button className="back-btn" onClick={() => setShowRaiseControls(false)}>
                          <span className="esc-label">ESC</span>
                          BACK
                        </button>
                        <button
                          className="action-submit"
                          disabled={Number(raiseTo) < Number(minRaiseTo)}
                          onClick={() => {
                            if (Number(raiseTo) >= Number(minRaiseTo)) {
                              invoke("Act", tableId, BettingActionType.Raise, Number(raiseTo));
                              setShowRaiseControls(false);
                            }
                          }}
                        >
                          RAISE
                        </button>
                      </div>
                    </div>
                  );
                })()}
              </div>
            )}
          </div>
        )}
      </div>
    </div>

        <div className="table-sidebar">
          <ChatPanel messages={messages} onSend={(message) => invoke("SendChatMessage", tableId, message)} />
        </div>
      </div>
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

function SeatBetAnimation({ seat, maxSeats }: { seat: SeatDto; maxSeats: number }) {
  const [bets, setBets] = useState<{ id: number }[]>([]);
  const prevStackRef = useRef(seat.stack);

  useEffect(() => {
    if (seat.stack < prevStackRef.current) {
      const id = Date.now();
      setBets(b => [...b, { id }]);
      setTimeout(() => {
        setBets(b => b.filter(x => x.id !== id));
      }, 500);
    }
    prevStackRef.current = seat.stack;
  }, [seat.stack]);

  if (bets.length === 0) return null;

  const angle = (seat.index / maxSeats) * 2 * Math.PI - Math.PI / 2;
  const left = 50 + 48 * Math.cos(angle);
  const top = 50 + 48 * Math.sin(angle);

  return (
    <>
      {bets.map(b => (
        <div key={b.id} className="flying-bet" style={{ '--start-left': `${left}%`, '--start-top': `${top}%` } as React.CSSProperties}>🪙</div>
      ))}
    </>
  );
}

function SeatView({
  seat,
  isMe,
  isActor,
  secondsToAct,
  maxSeats,
  onSitClick,
}: {
  seat: SeatDto;
  isMe: boolean;
  isActor: boolean;
  secondsToAct: number | null;
  maxSeats: number;
  onSitClick?: () => void;
}) {
  const angle = (seat.index / maxSeats) * 2 * Math.PI - Math.PI / 2;
  const a = 48;
  const b = 48;
  const left = 50 + a * Math.cos(angle);
  const top = 50 + b * Math.sin(angle);
  const style = { left: `${left}%`, top: `${top}%` };

  if (!seat.playerId) {
    return (
      <div className="seat empty" style={style} onClick={onSitClick}>
        <div className="sit-text">S I T</div>
      </div>
    );
  }

  return (
    <div className={`seat ${isActor ? "acting" : ""} ${seat.isFolded ? "folded" : ""} ${seat.isSittingOut ? "away" : ""}`} style={style}>
      <div className="seat-player">
        {seat.playerName} {isMe && "(you)"}
      </div>
      <div className="seat-stack">{seat.stack} chips</div>
      {seat.currentBet > 0 && (
        <div className="seat-current-bet">
          <span className="chip-icon">🪙</span> {seat.currentBet}
        </div>
      )}
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

function SitModal({ title = "Intended Stack", buttonText = "REQUEST THE SEAT", minBuyIn, maxBuyIn, onClose, onSit }: { title?: string; buttonText?: string; minBuyIn: number; maxBuyIn: number; onClose: () => void; onSit: (amount: number) => void }) {
  const [amount, setAmount] = useState(maxBuyIn);
  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <h2 style={{ margin: "0 0 0.5rem 0", fontSize: "1.2rem" }}>{title}</h2>
        <input type="number" value={amount} onChange={(e) => setAmount(+e.target.value)} min={minBuyIn} max={maxBuyIn} style={{ fontSize: "1.2rem", padding: "0.8rem" }} />
        <div style={{ color: "#8b949e", fontSize: "0.85rem", marginBottom: "1rem" }}>
          minimum: {minBuyIn} / maximum: {maxBuyIn}
        </div>
        <button className="request-seat-btn" onClick={() => onSit(amount)}>{buttonText}</button>
      </div>
    </div>
  );
}
