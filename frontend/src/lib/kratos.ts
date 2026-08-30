import { KRATOS_URL } from "./config";

export interface KratosFlow {
  id: string;
  ui: {
    action: string;
    nodes: Array<{ attributes: { name?: string; value?: unknown } }>;
  };
}

export interface KratosSession {
  identity: {
    id: string;
    traits: { email: string; timezone?: string };
  };
}

function csrfToken(flow: KratosFlow): string {
  const node = flow.ui.nodes.find((n) => n.attributes.name === "csrf_token");
  return (node?.attributes.value as string) ?? "";
}

async function fetchFlow(path: string): Promise<KratosFlow> {
  const res = await fetch(`${KRATOS_URL}${path}`, {
    credentials: "include",
    headers: { Accept: "application/json" },
  });
  if (!res.ok) {
    throw new Error(`Could not start the flow (${res.status}).`);
  }
  return res.json();
}

// Kratos's code method returns a non-2xx response with an updated flow after the first
// submission (it just sent the code and is now waiting for it) — that's expected, not a failure.
async function submitFlow(flow: KratosFlow, body: Record<string, unknown>): Promise<{ ok: boolean; flow: KratosFlow; session?: KratosSession }> {
  const res = await fetch(flow.ui.action, {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify({ ...body, csrf_token: csrfToken(flow) }),
  });
  
  const json = await res.json();
  
  if (res.ok) {
    return { ok: true, flow: json, session: json.session as KratosSession };
  }
  if (json.ui) {
    return { ok: false, flow: json as KratosFlow };
  }
  throw new Error(json.error?.message ?? "Something went wrong. Please try again.");
}

export const startRegistration = () => fetchFlow("/self-service/registration/browser");
export const startLogin = () => fetchFlow("/self-service/login/browser");

export const requestRegistrationCode = (flow: KratosFlow, email: string, timezone: string) =>
  submitFlow(flow, { method: "code", "traits.email": email, "traits.timezone": timezone });

export const submitRegistrationCode = (flow: KratosFlow, email: string, timezone: string, code: string) =>
  submitFlow(flow, { method: "code", "traits.email": email, "traits.timezone": timezone, code });

export const requestLoginCode = (flow: KratosFlow, email: string) =>
  submitFlow(flow, { method: "code", identifier: email });

export const submitLoginCode = (flow: KratosFlow, email: string, code: string) =>
  submitFlow(flow, { method: "code", identifier: email, code });

export async function whoAmI(): Promise<KratosSession | null> {
  const res = await fetch(`${KRATOS_URL}/sessions/whoami`, {
    credentials: "include",
    headers: { Accept: "application/json" },
  });
  if (!res.ok) return null;
  return res.json();
}

export async function logout(): Promise<void> {
  const res = await fetch(`${KRATOS_URL}/self-service/logout/browser`, {
    credentials: "include",
    headers: { Accept: "application/json" },
  });
  if (res.ok) {
    const { logout_url } = await res.json();
    window.location.href = logout_url;
  }
}
