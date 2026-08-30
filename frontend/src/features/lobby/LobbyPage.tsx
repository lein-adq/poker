import { useEffect, useState, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { api, type TableSummary } from "../../lib/api";
import { useAuth } from "../../context/AuthContext";
import WalletBadge from "../wallet/WalletBadge";

export default function LobbyPage() {
  const [tables, setTables] = useState<TableSummary[]>([]);
  const [showCreate, setShowCreate] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [username, setUsername] = useState<string>("");
  const [isEditingUsername, setIsEditingUsername] = useState(false);
  const { logout } = useAuth();
  const navigate = useNavigate();

  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      await Promise.all([
        api.listTables().then(setTables),
        new Promise(r => setTimeout(r, 800))
      ]);
    } catch (e: any) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  }, []);

  const fetchProfile = useCallback(async () => {
    try {
      const data = await api.getProfile();
      setUsername(data.displayName || "");
    } catch {}
  }, []);

  useEffect(() => {
    load();
    fetchProfile();
  }, [load, fetchProfile]);

  const updateUsername = async () => {
    try {
      await api.updateProfile(username);
      setIsEditingUsername(false);
    } catch {}
  };

  return (
    <div className="lobby-page">
      <header className="lobby-header" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '1rem' }}>
        <div style={{ display: "flex", flexDirection: "column" }}>
          <h1 style={{ margin: "0 0 0.5rem 0" }}>Lobby</h1>
          <div className="user-profile-display" style={{ display: "flex", alignItems: "center", gap: "0.5rem", marginBottom: "1rem" }}>
            {isEditingUsername ? (
              <>
                <input 
                  type="text" 
                  value={username} 
                  onChange={e => setUsername(e.target.value)} 
                  placeholder="Display Name" 
                  autoFocus
                  onKeyDown={e => { if (e.key === 'Enter') updateUsername(); }}
                  style={{ padding: "0.3rem", borderRadius: "4px", border: "1px solid #4db8ff", background: "#1a1f26", color: "white" }} 
                />
                <button onClick={updateUsername} style={{ padding: "0.3rem 0.6rem" }}>Save</button>
                <button onClick={() => setIsEditingUsername(false)} style={{ padding: "0.3rem 0.6rem", background: "transparent", color: "gray" }}>Cancel</button>
              </>
            ) : (
              <>
                <span style={{ fontSize: "1.1rem", color: "#8b949e" }}>Welcome, <strong style={{ color: "white" }}>{username || "Guest"}</strong></span>
                <button 
                  onClick={() => setIsEditingUsername(true)}
                  style={{ background: "transparent", border: "none", padding: "0", cursor: "pointer", fontSize: "1rem" }}
                  title="Edit username"
                >
                  ✏️
                </button>
                <button 
                  onClick={() => logout()}
                  style={{ background: "#e53e3e", border: "none", color: "white", padding: "0.2rem 0.6rem", borderRadius: "4px", cursor: "pointer", fontSize: "0.85rem", marginLeft: "0.5rem", fontWeight: "bold", textTransform: "uppercase" }}
                  title="Log out"
                >
                  Logout
                </button>
              </>
            )}
          </div>
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: '1rem' }}>
          <WalletBadge />
          <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
            <button onClick={() => setShowCreate(true)}>Create table</button>
            <button 
              onClick={load} 
              className="refresh-btn"
              title="Refresh tables"
              style={{ 
                background: '#2a2f36', 
                color: '#4db8ff', 
                border: 'none', 
                borderRadius: '50%', 
                width: '36px', 
                height: '36px', 
                display: 'flex', 
                alignItems: 'center', 
                justifyContent: 'center', 
                cursor: 'pointer',
                fontSize: '1.2rem',
                transition: 'background 0.2s',
              }}
              onMouseOver={(e) => e.currentTarget.style.background = '#363d46'}
              onMouseOut={(e) => e.currentTarget.style.background = '#2a2f36'}
            >
              <span className={loading ? 'spin' : ''} style={{ display: 'inline-block' }}>
                {loading ? <div className="spinner-icon">↻</div> : '↻'}
              </span>
            </button>
          </div>
        </div>
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
