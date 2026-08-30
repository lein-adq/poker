import { useEffect, useState } from "react";
import { useNavigate, Link } from "react-router-dom";
import { startLogin, requestLoginCode, submitLoginCode, type KratosFlow } from "../../lib/kratos";
import { useAuth } from "../../context/AuthContext";

export default function LoginPage() {
  const [flow, setFlow] = useState<KratosFlow | null>(null);
  const [email, setEmail] = useState("");
  const [code, setCode] = useState("");
  const [step, setStep] = useState<"email" | "code">("email");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const { refresh } = useAuth();
  const navigate = useNavigate();

  useEffect(() => {
    startLogin().then(setFlow).catch((e) => setError(e.message));
  }, []);

  async function handleRequestCode(e: React.FormEvent) {
    e.preventDefault();
    if (!flow) return;
    setBusy(true);
    setError(null);
    try {
      const result = await requestLoginCode(flow, email);
      
      const hasError = 
        result.flow.ui.messages?.some((m: any) => m.type === "error") ||
        result.flow.ui.nodes.some((n: any) => n.messages?.some((m: any) => m.type === "error"));

      if (hasError) {
        setFlow(result.flow);
        const uiMessage = result.flow.ui.messages?.find((m: any) => m.type === "error")?.text;
        const nodeMessage = result.flow.ui.nodes.find((n: any) => n.messages?.some((m: any) => m.type === "error"))
          ?.messages?.find((m: any) => m.type === "error")?.text;
        setError(uiMessage || nodeMessage || "Login failed. Please check your details.");
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
      const result = await submitLoginCode(flow, email, code);
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
      <h1>Log in</h1>
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
            {busy ? "Verifying…" : "Log in"}
          </button>
        </form>
      )}
      <span className="auth-link">
        New here? <Link to="/auth/register">Create an account</Link>
      </span>
    </div>
  );
}
