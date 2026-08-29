import { useEffect, useState } from "react";
import { useNavigate, Link } from "react-router-dom";
import {
  startRegistration,
  requestRegistrationCode,
  submitRegistrationCode,
  type KratosFlow,
} from "../../lib/kratos";
import { useAuth } from "../../context/AuthContext";

export default function RegisterPage() {
  const [flow, setFlow] = useState<KratosFlow | null>(null);
  const [email, setEmail] = useState("");
  const [code, setCode] = useState("");
  const [step, setStep] = useState<"email" | "code">("email");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const { refresh } = useAuth();
  const navigate = useNavigate();
  const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone;

  useEffect(() => {
    startRegistration().then(setFlow).catch((e) => setError(e.message));
  }, []);

  async function handleRequestCode(e: React.FormEvent) {
    e.preventDefault();
    if (!flow) return;
    setBusy(true);
    setError(null);
    try {
      const result = await requestRegistrationCode(flow, email, timezone);
      setFlow(result.flow);
      setStep("code");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function handleSubmitCode(e: React.FormEvent) {
    e.preventDefault();
    if (!flow) return;
    setBusy(true);
    setError(null);
    try {
      const result = await submitRegistrationCode(flow, email, timezone, code);
      if (result.ok) {
        await refresh();
        navigate("/");
      } else {
        setFlow(result.flow);
        setError("That code didn't work — check your inbox and try again.");
      }
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="auth-page">
      <h1>Create your account</h1>
      <p className="auth-hint">
        We only accept email addresses from known providers (Gmail, Outlook, iCloud, etc.) to keep
        disposable accounts out.
      </p>
      {error && <p className="error">{error}</p>}
      {step === "email" ? (
        <form onSubmit={handleRequestCode}>
          <label>
            Email
            <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          </label>
          <button type="submit" disabled={busy || !flow}>
            {busy ? "Sending code…" : "Send me a code"}
          </button>
        </form>
      ) : (
        <form onSubmit={handleSubmitCode}>
          <p>We sent a code to {email}. Enter it below.</p>
          <label>
            Code
            <input required value={code} onChange={(e) => setCode(e.target.value)} />
          </label>
          <button type="submit" disabled={busy}>
            {busy ? "Verifying…" : "Verify & create account"}
          </button>
        </form>
      )}
      <p>
        Already have an account? <Link to="/auth/login">Log in</Link>
      </p>
    </div>
  );
}
