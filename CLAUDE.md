# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Multiplayer Texas Hold'em: .NET 10/SignalR backend, Redis, the Ory stack (Kratos + Oathkeeper) for IAM,
Postgres, React/TypeScript frontend. See `README.md` for what's implemented and `docs/PRD.md` for the
product spec. `docs/Catches.md` is a standing list of spec gaps/open product decisions (not yet resolved)
— check it before assuming an edge case has a defined behavior.

## Commands

### Run everything (Docker)

```
cd infra
docker compose up -d --build
```

Brings up: Postgres (app data), a second Postgres for Kratos, Redis, Kratos (identity), Oathkeeper
(auth-proxy edge), Mailslurper (dev email capture), the API, and the web app.

- App: http://localhost:5173
- Mailslurper (registration/login codes land here in dev): http://localhost:4436
- API direct (normally only reachable through Oathkeeper): http://localhost:4455
- Kratos public API: http://localhost:4433

`docker compose down -v` also wipes the Postgres/Kratos volumes.

### Backend (outside Docker)

```
cd backend
dotnet test Poker.slnx                                    # all tests
dotnet test Poker.slnx --filter FullyQualifiedName~HandEngineTests   # single test class
dotnet run --project src/Poker.Api
```

Requires Postgres and Redis reachable at the connection strings in `src/Poker.Api/appsettings.json`
(defaults assume `localhost`). xUnit is the test framework; tests live in `backend/tests/*`, one project
per source project pairing (`Poker.GameEngine.Tests`, `Poker.Application.Tests`, `Poker.Api.Tests` — no
dedicated `Poker.Domain.Tests`; domain logic is exercised through the GameEngine tests). None of them
need Postgres or Redis.

`Poker.Api.Tests` covers the SignalR boundary: it drives real `TableHub` instances with recording
stand-ins for the transport (`SignalRFakes.cs`) so a test can assert what each individual connection was
sent. That is the only level at which the per-viewer hole-card privacy rule and the multi-connection
disconnect refcounting are actually observable. Its harness deliberately reuses one `HubCallerContext`
per connection, because SignalR builds a fresh Hub per invocation but keeps the context — and
`TableHub` stores the joined table id in `Context.Items`.

### Frontend (outside Docker)

```
cd frontend
npm install
npm run dev        # vite dev server
npm run build       # tsc -b && vite build
npm run lint         # oxlint
```

### End-to-end (needs the Docker stack)

```
cd infra && docker compose up -d --build   # the whole stack must be running
cd e2e && npm install && npx playwright install chromium
npm test
```

`e2e/` drives two real browsers through registration (reading the emailed code out of Mailslurper's API
at `:4437`, so no test-only auth bypass has to exist in production code), seating, a full hand, and the
ticker dealing the next one. It is deliberately one path: broad coverage belongs in the fast suites, and
this exists only to catch what they are blind to by construction — auth, routing, CORS, static-file
serving, serialization. It has already earned that keep three times over; see git history.

### CI

`.github/workflows/ci.yml` gates pushes to `master`/`main` and all pull requests: backend
`dotnet build`/`dotnet test` in Release, frontend `npm run lint` and `npm run build` (which is the
`tsc -b` typecheck too). Neither job provisions Postgres or Redis — the backend suite runs entirely on
the in-memory fakes in `tests/Poker.Application.Tests/Fakes.cs`. A test that needs real infrastructure
needs service containers added to the workflow, not a skip.

`.github/workflows/e2e.yml` runs the Playwright suite against a full `docker compose` stack, nightly and
on `workflow_dispatch` — not on pull requests, so stack startup and browser flake stay off the path to
merging.

## Architecture

### Backend layering

Strict dependency direction, enforced by project references — lower layers know nothing about upper ones:

```
Poker.Domain  <-  Poker.GameEngine  <-  Poker.Application  <-  Poker.Infrastructure  <-  Poker.Api
```

- **Poker.Domain** (`backend/src/Poker.Domain`): pure card/betting primitives with no game-flow
  knowledge — `Cards/HandEvaluator` (7-card best-hand evaluation), `Cards/Deck`, `Betting/BettingRound`
  (single-street betting state machine: legal actions, calls/raises/all-ins), `Betting/SidePotCalculator`
  (splits contributions into main/side pots by eligibility).
- **Poker.GameEngine** (`backend/src/Poker.GameEngine`): `Hands/HandEngine` drives one full hand
  (blinds → streets → showdown) by sequencing `BettingRound`s and calling `SidePotCalculator` at
  showdown; auto-runs remaining streets when everyone left is all-in. `Equity/EquityCalculator` does
  Monte Carlo win/tie % for the live equity bar.
- **Poker.Application** (`backend/src/Poker.Application`): use-case orchestration, no ASP.NET/EF/Redis
  dependency — only interfaces (`Abstractions/`). `Tables/TableService` is the central orchestrator:
  every mutating method goes through `MutateAsync`, which acquires a per-table distributed lock, loads
  state from `ITableRepository`, mutates, saves. `Wallet/WalletService` is the append-only ledger over
  `IWalletRepository`.
- **Poker.Infrastructure** (`backend/src/Poker.Infrastructure`): EF Core/Postgres (`Persistence/`,
  wallet + user repos, migrations), Redis (`Redis/RedisDistributedLock`, `RedisActiveTableTracker` for
  the one-active-table-per-account rule), `Tables/InMemoryTableRepository` (see below),
  `DailyGift/DailyGiftHostedService`.
- **Poker.Api** (`backend/src/Poker.Api`): minimal-API endpoints (`Endpoints/EndpointMappings.cs`),
  SignalR hubs (`Hubs/LobbyHub`, `Hubs/TableHub`), the Oathkeeper header-trust auth handler
  (`Auth/OathkeeperAuthenticationHandler`), Kratos webhook handlers for the email allow-list
  (`Iam/EmailDomainAllowList`) and post-registration signup bonus.

**Live table/hand state is intentionally in-process, not Redis** (`InMemoryTableRepository`): a live
`HandEngine` (deck order, bet state) isn't trivially serializable, so this build assumes a single API
instance owns all tables. Cross-instance concerns that *do* need to be shared already go through Redis:
one-active-table-per-account (`RedisActiveTableTracker`), the per-table distributed lock
(`RedisDistributedLock`), the daily-gift dedupe key, and the SignalR backplane
(`AddStackExchangeRedis`). Don't assume table state survives an API restart or is visible across
instances — it isn't, by design, for this scope.

### Auth model

Kratos owns identity (passwordless email-code auth); Oathkeeper sits at the edge, validates the Kratos
session cookie, and injects `X-User-Id`/`X-User-Email` headers. `OathkeeperAuthenticationHandler` in
the API trusts those headers unconditionally — **this only works because the API must be unreachable
except through Oathkeeper** (enforced at the docker-compose network level, not in app code). When
touching auth, do not add header-based trust anywhere the API might be hit directly by a public client.

**The API service must never publish a port to the host.** It carries `expose: ["8080"]`, not `ports:`,
and that is load-bearing rather than tidiness: while the port was published, `curl -H 'X-User-Id: anyone'
http://localhost:8080/api/wallet/` returned 200 — a complete authentication bypass for anyone who could
reach the host, because the handler above trusts the header on sight. Oathkeeper reaches the API as
`http://api:8080` over the compose network and needs no published port; use `docker compose exec api`
to debug it directly. Any future service that needs to call the API belongs on that network too.

**CORS is owned by the API alone**, via `Cors:AllowedOrigins`. Oathkeeper's own CORS is switched off,
because with both enabled every proxied response carried `Access-Control-Allow-Origin` twice and browsers
rejected the lot — the entire authenticated API was unreachable from the web app while curl saw nothing
wrong. Preflights are not authenticated, so the `poker-cors-preflight` access rule forwards `OPTIONS`
to the API with `noop` authenticator and mutator; without that rule, no rule matches `OPTIONS` at all
and preflight fails. Turning CORS on in either layer without turning it off in the other breaks the app.

Kratos calls back into the API for two things: `/internal/iam/validate-email` (registration webhook,
enforces the email domain allow-list) and `/internal/iam/on-registered` (grants the signup wallet
bonus). Both routes are unauthenticated by design — reachable only from Kratos on the internal network.

### Real-time model

Nothing ever does a single shared broadcast to a table group — `Hubs/TableBroadcaster` builds a
**per-viewer** `TableStateDto` (`Hubs/TableStateDto.cs`) so hole cards stay private: a seated player
only sees their own cards; everyone's cards become visible once revealed (showdown, or earlier if all
remaining players are all-in, matching PRD's early-reveal rule). Any change to what a `TableStateDto`
exposes needs to be re-checked against this privacy rule, not just "does the UI need this field."

After a hand result is produced, `TableService.ApplyPlayerActionAsync` deliberately leaves the finished
`HandEngine` in place (with `Result` populated) rather than clearing it, so the showdown/pot breakdown
is still visible in the state broadcast right after the action that ended the hand.

**The game advances on a server-side clock, not on client messages.** `Background/TableTickerService`
sweeps every table twice a second and calls `TableService.TickAsync`, which (a) acts for a player whose
`TableState.ActionDeadlineUtc` has passed — checking when it is free, folding when facing a bet — and
(b) deals the next hand once `TableState.NextHandStartUtc` (the post-showdown pause) elapses. Both hub
actions and the ticker fan out through the same `Hubs/TableBroadcaster`, so a client cannot tell which
one moved the game. Do not reintroduce client-driven progression: `TableHub.Act` used to sleep 3s and
start the next hand itself, which made the table's clock depend on the acting client staying connected.

`TableHub.OnDisconnectedAsync` marks a player disconnected only when their *last* connection for that
table drops (`Hubs/TableConnectionRegistry` — multiple tabs, and reconnects issuing fresh connection
ids, both mean "a connection dropped" is not "the player left"). A disconnected player keeps their seat,
chips and the full clock on a decision already in front of them, but is skipped when the next hand is
dealt until they rejoin; the client must re-invoke `JoinAsSpectator` on SignalR reconnect, which is both
the group re-join and the sit-out clear.

### Frontend

Vite + React 19 + react-router, feature-folder layout under `frontend/src/features/{auth,chat,lobby,
table,wallet}`. `lib/api.ts` wraps the REST endpoints, `lib/signalr.ts` builds hub connections,
`lib/kratos.ts` talks to Kratos's browser-flow API directly (registration/login/whoami/logout) — Kratos
flows are hit from the browser, not proxied through the API.

## Known scope limits

See `README.md` and `docs/Catches.md` for the full list. The load-bearing ones for future changes:

- Table/hand state is single-instance in-process (see above) — don't build features that assume it's
  shared or durable across restarts.
- No Ory Keto; authorization is plain domain logic (table creator checks, seat ownership).
- Split-pot odd chips go to the first eligible winner, not the seat left of the button (documented
  simplification in `HandEngine.Showdown`).
