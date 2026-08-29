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
      <h1>Log in</h1>
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
            {busy ? "Verifying…" : "Log in"}
          </button>
        </form>
      )}
      <p>
        New here? <Link to="/auth/register">Create an account</Link>
      </p>
    </div>
  );
}
