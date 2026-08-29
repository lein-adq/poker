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
dotnet test Poker.slnx        # 602 tests: hand evaluator, side pots, betting, equity, tables, wallet,
                              # plus randomised chip-conservation property tests over whole sessions
dotnet run --project src/Poker.Api
```

Needs Postgres and Redis reachable at the connection strings in `src/Poker.Api/appsettings.json` (defaults assume `localhost`).

## Local frontend dev (outside Docker)

```
cd frontend
npm install
npm run dev
```

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
