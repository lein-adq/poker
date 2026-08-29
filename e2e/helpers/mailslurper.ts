import type { APIRequestContext } from "@playwright/test";

const MAILSLURPER_URL = process.env.E2E_MAILSLURPER_URL ?? "http://localhost:4437";

interface MailItem {
  id?: string;
  dateSent?: string;
  toAddresses?: string[];
  subject?: string;
  body?: string;
  htmlBody?: string;
  textBody?: string;
}

/**
 * Reads the login/registration code Kratos emailed, out of the dev mail catcher the compose stack
 * already runs. Deliberately not a test-only bypass in the API: an auth shortcut that exists in
 * production code is a liability regardless of how it is gated, and this proves the real flow anyway.
 */
export async function waitForLoginCode(
  request: APIRequestContext,
  email: string,
  timeoutMs = 45_000,
): Promise<string> {
  const deadline = Date.now() + timeoutMs;
  let lastError = "no email arrived";

  while (Date.now() < deadline) {
    try {
      const response = await request.get(`${MAILSLURPER_URL}/mail`, { timeout: 10_000 });
      if (response.ok()) {
        const code = extractCode(await response.json(), email);
        if (code) {
          return code;
        }
        lastError = `no code in any mail addressed to ${email}`;
      } else {
        lastError = `mailslurper returned ${response.status()}`;
      }
    } catch (error) {
      lastError = (error as Error).message;
    }

    await new Promise((resolve) => setTimeout(resolve, 500));
  }

  throw new Error(`Timed out waiting for a login code for ${email}: ${lastError}`);
}

function extractCode(payload: unknown, email: string): string | null {
  const items = ((payload as { mailItems?: MailItem[] })?.mailItems ?? [])
    .filter((item) => addressedTo(item, email))
    // Newest first: a re-registration would otherwise pick up a stale code.
    .sort((a, b) => (b.dateSent ?? "").localeCompare(a.dateSent ?? ""));

  for (const item of items) {
    const text = stripHtml([item.body, item.htmlBody, item.textBody].filter(Boolean).join("\n"));
    const match = /\b(\d{6})\b/.exec(text);
    if (match) {
      return match[1];
    }
  }
  return null;
}

function addressedTo(item: MailItem, email: string): boolean {
  const needle = email.toLowerCase();
  return (item.toAddresses ?? []).some((address) => address.toLowerCase().includes(needle));
}

function stripHtml(value: string): string {
  return value
    .replace(/<[^>]+>/g, " ")
    .replace(/&nbsp;/g, " ")
    .replace(/&#(\d+);/g, (_, code: string) => String.fromCharCode(Number(code)));
}
