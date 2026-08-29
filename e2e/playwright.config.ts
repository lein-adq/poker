import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./tests",

  // The stack under test is shared mutable state — a single API instance owns every table in-process,
  // and an account may only be at one table at a time — so these must not race each other.
  workers: 1,
  fullyParallel: false,

  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,

  // Registration goes through Kratos and an emailed code, so even the happy path is genuinely slow.
  timeout: 120_000,
  expect: { timeout: 20_000 },

  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : [["list"]],

  use: {
    baseURL: process.env.E2E_APP_URL ?? "http://localhost:5173",
    trace: "retain-on-failure",
    video: "retain-on-failure",
    screenshot: "only-on-failure",
  },

  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
