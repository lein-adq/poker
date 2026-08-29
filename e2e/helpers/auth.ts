import { expect, type APIRequestContext, type Page } from "@playwright/test";
import { waitForLoginCode } from "./mailslurper";

/** Registration is allow-listed to known providers, so the address has to look like a real one. */
export function uniqueEmail(prefix: string): string {
  const unique = `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 8)}`;
  return `poker-e2e-${prefix}-${unique}@gmail.com`;
}

/**
 * Registers a brand new account through the real UI: Kratos flow, emailed code, signup bonus. Leaves
 * the browser sitting in the lobby, logged in.
 */
export async function registerNewUser(
  page: Page,
  request: APIRequestContext,
  email: string,
): Promise<void> {
  await page.goto("/auth/register");

  await page.getByLabel("Email").fill(email);
  await page.getByRole("button", { name: /send me a code/i }).click();

  await expect(page.getByRole("button", { name: /verify & create account/i })).toBeVisible();

  const code = await waitForLoginCode(request, email);
  await page.getByLabel("Code").fill(code);
  await page.getByRole("button", { name: /verify & create account/i }).click();

  // The lobby is the post-registration landing page; its create button proves the session took.
  await expect(page.getByRole("button", { name: /create table/i })).toBeVisible();
}
