# Poker

Multiplayer Texas Hold'em: .NET/SignalR backend, Redis, the Ory stack (Kratos + Oathkeeper) for IAM, React frontend. See `docs/PRD.md` for the product spec and `.claude/plans/gentle-hatching-bentley.md`-derived architecture notes in commit history for the design rationale.

## Run it

```
cd infra
docker compose up -d --build
```

This brings up: Postgres (app data), a second Postgres for Kratos, Redis, Kratos (identity), Oathkeeper (auth-proxy edge), Mailslurper (catches dev emails), the API, and the web app.

- App: http://localhost:5173
- Mailslurper (view registration/login codes): http://localhost:4436
- API (direct, behind Oathkeeper normally): http://localhost:4455
- Kratos public API: http://localhost:4433

Stop with `docker compose down` (add `-v` to also wipe the Postgres/Kratos volumes).

## Local backend dev (outside Docker)

```
cd backend
dotnet test Poker.slnx        # 611 tests: hand evaluator, side pots, betting, equity, tables, wallet,
                              # randomised chip-conservation property tests over whole sessions, and
                              # SignalR hub-boundary tests (per-viewer privacy, disconnects, the ticker)
dotnet run --project src/Poker.Api
```

Needs Postgres and Redis reachable at the connection strings in `src/Poker.Api/appsettings.json` (defaults assume `localhost`).

## Local frontend dev (outside Docker)

```
cd frontend
npm install
npm run dev
```

## End-to-end test

Needs the Docker stack running (see above).

```
cd e2e
npm install
npx playwright install chromium
npm test
```

Two real browsers register through Kratos (the emailed code is read out of Mailslurper's API, so there
is no test-only auth bypass in the app), sit at a table, play a hand, and watch the server deal the next
one. One path on purpose — it exists to catch what the fast suites cannot see: auth, routing, CORS,
static-file serving and serialization. It found three genuine breakages on its first runs (SPA deep
links 404ing, duplicated CORS headers blocking every API call, and the API port being published to the
host — an authentication bypass).

## CI

`.github/workflows/ci.yml` runs on every push to `master`/`main` and every pull request, in two parallel
jobs. Nothing here needs Postgres or Redis — the backend suite runs against in-memory fakes.

| Job | Checks |
| --- | --- |
| Backend | `dotnet build -c Release`, `dotnet test -c Release` (611 tests), plus a non-gating `dotnet list package --vulnerable` audit |
| Frontend | `npm run lint` (oxlint), `npm run build` (`tsc -b` typecheck + vite bundle) |

`.github/workflows/e2e.yml` runs the Playwright test against a full compose stack **nightly and on
demand**, not per pull request — standing the stack up costs minutes and would put browser flake on the
critical path to merging.

The dependency audit reports rather than fails, so a newly published advisory against an existing
package cannot break unrelated pull requests on the day it lands. It currently reports one finding:
`Microsoft.OpenApi` 2.0.0 (high, [GHSA-v5pm-xwqc-g5wc](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc)),
pulled in transitively by `Microsoft.AspNetCore.OpenApi` 10.0.10.

## What's implemented

- **Game engine** (`Poker.Domain`, `Poker.GameEngine`): 7-card hand evaluator, betting-round state machine with side pots, full hand lifecycle (blinds → streets → showdown, auto-runout on all-in), Monte Carlo equity calculator.
- **Table orchestration** (`Poker.Application`): seating, buy-in bounds, waitlist promotion, queued rebuys (applied at the hand boundary), one-active-table-per-account, private play-money tables isolated from the real wallet.
- **Wallet**: append-only ledger, signup grant, claimable welcome gift, daily timezone-aware gift (`Poker.Infrastructure/DailyGift`).
- **IAM**: Kratos passwordless email (code) auth, an email-domain allow-list enforced via a Kratos registration webhook, Oathkeeper injecting identity headers the API trusts.
- **Real-time**: SignalR `LobbyHub`/`TableHub`, Redis backplane, per-viewer hole-card privacy (opponents' cards are hidden until showdown or an all-in reveal).
- **Frontend**: auth, lobby, table felt UI, chat with spectator tagging, live equity bar, showdown hand-name badges, hand-ranking cheatsheet.

## Known scope limits (see plan for rationale)

- Live per-hand table state (including deck order) lives in-process, not replicated through Redis — single API instance. Redis *is* used for the one-active-table lock, the distributed table lock, the daily-gift dedupe key, and the SignalR backplane.
- No Ory Keto — authorization is plain domain logic (table creator checks, seat ownership), no relationship-graph permission engine was needed.
- Odd chips in a split pot go to the first eligible winner rather than the seat left of the button.
