import { useEffect, useState } from "react";
import { api, type WalletBalance } from "../../lib/api";

export default function WalletBadge() {
  const [wallet, setWallet] = useState<WalletBalance | null>(null);
  const [claiming, setClaiming] = useState(false);
  const [claimError, setClaimError] = useState<string | null>(null);
  const [claimed, setClaimed] = useState(false);

  const load = () => api.getWallet().then(setWallet).catch(() => {});

  useEffect(() => {
    load();
  }, []);

  async function claimGift() {
    setClaiming(true);
    setClaimError(null);
    try {
      await api.claimWelcomeGift();
      setClaimed(true);
      await load();
    } catch (e) {
      setClaimError((e as Error).message);
      setClaimed(true); // most likely "already claimed" — hide the button either way
    } finally {
      setClaiming(false);
    }
  }

  if (!wallet) return null;

  return (
    <div className="wallet-badge">
      <span title="Real chip balance">{wallet.balance.toLocaleString()} chips</span>
      {!claimed && (
        <button onClick={claimGift} disabled={claiming}>
          {claiming ? "Claiming…" : "Claim welcome gift (+300)"}
        </button>
      )}
      {claimError && <span className="error">{claimError}</span>}
    </div>
  );
}
