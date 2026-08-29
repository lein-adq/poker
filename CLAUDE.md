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
per source project pairing (`Poker.GameEngine.Tests`, `Poker.Application.Tests` — no dedicated
`Poker.Domain.Tests`; domain logic is exercised through the GameEngine tests).

### Frontend (outside Docker)

```
cd frontend
npm install
npm run dev        # vite dev server
npm run build       # tsc -b && vite build
npm run lint         # oxlint
```

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

Kratos calls back into the API for two things: `/internal/iam/validate-email` (registration webhook,
enforces the email domain allow-list) and `/internal/iam/on-registered` (grants the signup wallet
bonus). Both routes are unauthenticated by design — reachable only from Kratos on the internal network.

### Real-time model

`TableHub` never does a single shared broadcast to a table group — `BroadcastTableState` builds a
**per-viewer** `TableStateDto` (`Hubs/TableStateDto.cs`) so hole cards stay private: a seated player
only sees their own cards; everyone's cards become visible once revealed (showdown, or earlier if all
remaining players are all-in, matching PRD's early-reveal rule). Any change to what a `TableStateDto`
exposes needs to be re-checked against this privacy rule, not just "does the UI need this field."

After a hand result is produced, `TableService.ApplyPlayerActionAsync` deliberately leaves the finished
`HandEngine` in place (with `Result` populated) rather than clearing it, so the showdown/pot breakdown
is still visible in the state broadcast right after the action that ended the hand; `TableHub.Act` then
sleeps 3s before calling `TryStartHandAsync` for the next hand, so clients have time to render it.

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
