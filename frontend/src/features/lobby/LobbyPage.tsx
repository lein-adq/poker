import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api, type TableSummary } from "../../lib/api";
import WalletBadge from "../wallet/WalletBadge";

export default function LobbyPage() {
  const [tables, setTables] = useState<TableSummary[]>([]);
  const [showCreate, setShowCreate] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();

  const load = () => api.listTables().then(setTables).catch((e) => setError(e.message));

  useEffect(() => {
    load();
    const interval = setInterval(load, 5000);
    return () => clearInterval(interval);
  }, []);

  return (
    <div className="lobby-page">
      <header className="lobby-header">
        <h1>Tables</h1>
        <WalletBadge />
        <button onClick={() => setShowCreate(true)}>Create table</button>
      </header>
      {error && <p className="error">{error}</p>}
      <table className="table-list">
        <thead>
          <tr>
            <th>Name</th>
            <th>Players</th>
            <th>Buy-in</th>
            <th>Status</th>
            <th>Waitlist</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {tables.map((t) => (
            <tr key={t.id}>
              <td>
                {t.name} {t.isPrivate && <span className="badge">Private</span>}
              </td>
              <td>
                {t.seatedPlayerCount}/{t.maxSeats}
              </td>
              <td>
                {t.minBuyIn}–{t.maxBuyIn}
              </td>
              <td>{t.status}</td>
              <td>{t.waitlistCount}</td>
              <td>
                <button onClick={() => navigate(`/table/${t.id}`)}>Join</button>
              </td>
            </tr>
          ))}
          {tables.length === 0 && (
            <tr>
              <td colSpan={6}>No tables yet — create one to get started.</td>
            </tr>
          )}
        </tbody>
      </table>

      {showCreate && (
        <CreateTableModal
          onClose={() => setShowCreate(false)}
          onCreated={(table) => {
            setShowCreate(false);
            navigate(`/table/${table.id}`);
          }}
        />
      )}
    </div>
  );
}

function CreateTableModal({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (table: TableSummary) => void;
}) {
  const [name, setName] = useState("My Table");
  const [minBuyIn, setMinBuyIn] = useState(100);
  const [maxBuyIn, setMaxBuyIn] = useState(1000);
  const [smallBlind, setSmallBlind] = useState(5);
  const [bigBlind, setBigBlind] = useState(10);
  const [isPrivate, setIsPrivate] = useState(false);
  const [useRealBankroll, setUseRealBankroll] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const table = await api.createTable({
        name,
        minBuyIn,
        maxBuyIn,
        smallBlind,
        bigBlind,
        isPrivate,
        useRealBankroll: isPrivate ? useRealBankroll : true,
      });
      onCreated(table);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <form className="modal" onClick={(e) => e.stopPropagation()} onSubmit={submit}>
        <h2>Create a table</h2>
        {error && <p className="error">{error}</p>}
        <label>
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} required />
        </label>
        <label>
          Min buy-in
          <input type="number" value={minBuyIn} onChange={(e) => setMinBuyIn(+e.target.value)} min={1} />
        </label>
        <label>
          Max buy-in
          <input type="number" value={maxBuyIn} onChange={(e) => setMaxBuyIn(+e.target.value)} min={minBuyIn} />
        </label>
        <label>
          Small blind
          <input type="number" value={smallBlind} onChange={(e) => setSmallBlind(+e.target.value)} min={1} />
        </label>
        <label>
          Big blind
          <input type="number" value={bigBlind} onChange={(e) => setBigBlind(+e.target.value)} min={smallBlind + 1} />
        </label>
        <label>
          <input type="checkbox" checked={isPrivate} onChange={(e) => setIsPrivate(e.target.checked)} />
          Private table
        </label>
        {isPrivate && (
          <label>
            <input
              type="checkbox"
              checked={useRealBankroll}
              onChange={(e) => setUseRealBankroll(e.target.checked)}
            />
            Use real chip balance (unchecked = unlimited play chips, isolated from your real bag)
          </label>
        )}
        <div className="modal-actions">
          <button type="button" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" disabled={busy}>
            {busy ? "Creating…" : "Create"}
          </button>
        </div>
      </form>
    </div>
  );
}
