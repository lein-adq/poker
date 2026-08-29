import { expect, test, type Browser, type APIRequestContext, type Page } from "@playwright/test";
import { registerNewUser, uniqueEmail } from "../helpers/auth";

/**
 * The one thing unit and hub-boundary tests cannot prove: that the whole stack is actually wired
 * together. Two real browsers, real Kratos registration with an emailed code, real Oathkeeper header
 * injection, real SignalR — playing a real hand.
 *
 * Kept to a single path on purpose. Broad coverage belongs in the fast suites; this exists to catch
 * the integration failures they are blind to by construction.
 */
test("two players register, sit down, and play a hand to completion", async ({ browser, request }) => {
  const alice = await openPlayer(browser, request, "alice");
  const bob = await openPlayer(browser, request, "bob");

  try {
    const tableUrl = await createTable(alice.page);
    await sitDown(alice.page);

    await bob.page.goto(tableUrl);
    await sitDown(bob.page);

    // Two seated players is enough to deal, so the hand starts on its own.
    await expect(alice.page.locator(".street-label")).toBeVisible();
    await expect(bob.page.locator(".street-label")).toBeVisible();

    await expectHoleCardsArePrivate(alice.page);
    await expectHoleCardsArePrivate(bob.page);

    // Whoever the button put on the clock folds, which ends the hand immediately.
    const actor = await playerToAct([alice.page, bob.page]);
    await actor.getByRole("button", { name: "Fold" }).click();

    // Both browsers see the same result, which means the per-viewer broadcast reached both.
    await expect(alice.page.locator(".pot-result")).toBeVisible();
    await expect(bob.page.locator(".pot-result")).toBeVisible();

    // Nobody clicks anything from here. The result clearing means the server's ticker dealt the next
    // hand on its own clock — the behaviour that used to depend on a client staying connected.
    await expect(alice.page.locator(".pot-result")).toBeHidden({ timeout: 30_000 });
    await expect(alice.page.locator(".street-label")).toBeVisible();
    await expect(bob.page.locator(".street-label")).toBeVisible();
  } finally {
    await alice.close();
    await bob.close();
  }
});

async function openPlayer(browser: Browser, request: APIRequestContext, name: string) {
  // A separate context per player: separate cookie jar, so these are genuinely two different sessions.
  const context = await browser.newContext();
  const page = await context.newPage();
  await registerNewUser(page, request, uniqueEmail(name));
  return { page, close: () => context.close() };
}

async function createTable(page: Page): Promise<string> {
  await page.getByRole("button", { name: /create table/i }).click();
  await page.getByLabel("Name").fill(`E2E ${Date.now()}`);
  await page.getByRole("button", { name: "Create", exact: true }).click();

  await expect(page).toHaveURL(/\/table\/[0-9a-f-]+$/i);
  return page.url();
}

async function sitDown(page: Page): Promise<void> {
  await page.getByRole("button", { name: /^Sit \(/ }).click();
  await expect(page.locator(".seat", { hasText: "(you)" })).toBeVisible();
}

/**
 * The privacy rule at its only truly authoritative level: what is in the other player's browser. Their
 * own two cards are face up; the opponent's are backs, with no card face anywhere in that seat.
 */
async function expectHoleCardsArePrivate(page: Page): Promise<void> {
  const mySeat = page.locator(".seat", { hasText: "(you)" });
  await expect(mySeat.locator(".hole-cards .card:not(.card-back)")).toHaveCount(2);

  const opponentSeat = page.locator(".seat").filter({ hasNot: page.locator("text=(you)") })
    .filter({ has: page.locator(".hole-cards") });
  await expect(opponentSeat.locator(".hole-cards .card-back")).toHaveCount(2);
  await expect(opponentSeat.locator(".hole-cards .card:not(.card-back)")).toHaveCount(0);
}

/** Returns whichever page is currently showing betting controls. */
async function playerToAct(pages: Page[]): Promise<Page> {
  for (const page of pages) {
    if (await page.locator(".betting-actions").isVisible().catch(() => false)) {
      return page;
    }
  }

  // The state broadcast may not have landed yet; wait for one of them to get the action.
  await expect(pages[0].locator(".betting-actions").or(pages[1].locator(".betting-actions"))).toBeVisible();
  for (const page of pages) {
    if (await page.locator(".betting-actions").isVisible()) {
      return page;
    }
  }
  throw new Error("Neither player was given the action.");
}
