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
      
      // Kratos returns an updated flow in both success (asking for the code) and failure (validation errors).
      // We must check if the new flow contains any error messages before moving to the code step.
      const hasError = 
        result.flow.ui.messages?.some((m: any) => m.type === "error") ||
        result.flow.ui.nodes.some((n: any) => n.messages?.some((m: any) => m.type === "error"));

      if (hasError) {
        setFlow(result.flow);
        
        // Extract the first error message to show to the user
        const uiMessage = result.flow.ui.messages?.find((m: any) => m.type === "error")?.text;
        const nodeMessage = result.flow.ui.nodes.find((n: any) => n.messages?.some((m: any) => m.type === "error"))
          ?.messages?.find((m: any) => m.type === "error")?.text;
          
        setError(uiMessage || nodeMessage || "Registration failed. Please check your details.");
      } else {
        setFlow(result.flow);
        setStep("code");
      }
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
      <div className="auth-logo">Pokercito.lol</div>
      <h1>Create your account</h1>
      <p className="auth-hint">
        We only accept email addresses from known providers (Gmail, Outlook, iCloud, etc.) to keep
        disposable accounts out.
      </p>
      {error && <p className="error">{error}</p>}
      {step === "email" ? (
        <form onSubmit={handleRequestCode}>
          <label>Email</label>
          <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} placeholder="your@email.com" />
          <button type="submit" disabled={busy || !flow}>
            {busy ? "Sending code…" : "Send me a code"}
          </button>
        </form>
      ) : (
        <form onSubmit={handleSubmitCode}>
          <p>We sent a code to {email}. Enter it below.</p>
          <label>Code</label>
          <input required value={code} onChange={(e) => setCode(e.target.value)} placeholder="000000" />
          <button type="submit" disabled={busy}>
            {busy ? "Verifying…" : "Verify & create account"}
          </button>
        </form>
      )}
      <span className="auth-link">
        Already have an account? <Link to="/auth/login">Log in</Link>
      </span>
    </div>
  );
}
