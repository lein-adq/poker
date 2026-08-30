import { useEffect, useState } from "react";
import { api, type WalletBalance } from "../../lib/api";

export default function WalletBadge() {
  const [wallet, setWallet] = useState<WalletBalance | null>(null);
  const [claiming, setClaiming] = useState(false);
  const [claimError, setClaimError] = useState<string | null>(null);
  
  const welcomeGiftSeen = localStorage.getItem("welcomeGiftSeen") === "true";
  const [showModal, setShowModal] = useState(!welcomeGiftSeen);

  const load = () => api.getWallet().then((w) => {
    setWallet(w);
    // If they already have chips, we assume they don't need the welcome gift modal
    if (w.balance > 0 && !welcomeGiftSeen) {
      localStorage.setItem("welcomeGiftSeen", "true");
      setShowModal(false);
    }
  }).catch(() => {});

  useEffect(() => {
    load();
  }, []);

  async function claimGift() {
    setClaiming(true);
    setClaimError(null);
    try {
      await api.claimWelcomeGift();
      localStorage.setItem("welcomeGiftSeen", "true");
      setShowModal(false);
      await load();
    } catch (e) {
      setClaimError((e as Error).message);
      localStorage.setItem("welcomeGiftSeen", "true"); // most likely already claimed
      setTimeout(() => setShowModal(false), 2000);
    } finally {
      setClaiming(false);
    }
  }

  function dismissModal() {
    localStorage.setItem("welcomeGiftSeen", "true");
    setShowModal(false);
  }

  if (!wallet) return null;

  return (
    <div className="wallet-badge">
      <span title="Real chip balance">{wallet.balance.toLocaleString()} chips</span>
      
      {showModal && wallet.balance === 0 && (
        <div className="modal-backdrop">
          <div className="modal" style={{ textAlign: "center" }}>
            <h2 style={{ marginBottom: "1rem", color: "gold" }}>🎁 Welcome to the Poker Club!</h2>
            <p style={{ marginBottom: "1.5rem" }}>Claim your welcome gift of 300 free chips to start playing right away.</p>
            <div className="modal-actions" style={{ justifyContent: "center" }}>
              <button onClick={dismissModal} className="poker-btn fold">Not Now</button>
              <button onClick={claimGift} disabled={claiming} className="poker-btn raise" style={{ background: "gold", color: "black", border: "none" }}>
                {claiming ? "Claiming…" : "CLAIM 300 CHIPS"}
              </button>
            </div>
            {claimError && <p className="error" style={{ marginTop: "1rem" }}>{claimError}</p>}
          </div>
        </div>
      )}
    </div>
  );
}
