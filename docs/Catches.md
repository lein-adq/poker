# Poker Web App — Spec Gaps & Open Decisions

This document takes the current spec and flags every place where a decision is implied but not made. Each item includes the gap, the risk if left unresolved, and a recommended default so this can move into dev without stalling on every point.

---

## 1. Tables & Seating

| # | Gap | Risk if unresolved | Recommendation |
| --- | ----- | --------------------- | ----------------- |
| 1.1 | "Explore multiple tables" has no defined sort/filter model | Lobby becomes unusable past ~20 tables | Filter by stakes, player count, buy-in range; sort by "most active" default |
| 1.2 | Relationship between spectating and the "one table per account" rule is undefined | Ambiguous whether spectating counts as your active slot | Spectating counts as occupying your one slot; you can't spectate Table A and queue Table B simultaneously |
| 1.3 | No handling for balance < table minimum | Player sees tables they can't join, or crashes on buy-in | Grey out / disable join on tables where balance < minimum, with inline explanation |
| 1.4 | No handling for balance > table maximum | Unclear if excess chips are wasted or blocked | Buy-in capped at table max; excess stays in global balance, not forfeited |
| 1.5 | No rule for stack growing past table max during play | Player could sit indefinitely above the table's intended ceiling | Either cap winnings display only (no forced cash-out) or force a top-of-hand payout above max — **needs an explicit decision, this affects game feel** |
| 1.6 | No blind structure defined | "Min/max stack" describes buy-in, not stakes — the table's actual game isn't specified | Fixed blinds per table tier (e.g., 5/10 for low-stakes tables), no escalation unless tournaments are a separate feature |
| 1.7 | No dealer button rotation rule stated | Implementation detail but affects fairness | Standard clockwise rotation each hand, explicit in spec even if "obvious" |

---

## 2. Queue Mechanics

- **Bust vs. rebuy conflict:** rebuys are allowed "any time, effective next round," but busting triggers a queue backfill. If a busted player gets a grace period to rebuy before their seat is released, that contradicts immediate queue promotion. **Needs one explicit rule**, e.g.: busted player has until the *start* of the next hand to rebuy, or their seat opens to the queue.
- **AFK queue promotion:** if a queued player is offline when their seat opens, is there a timeout before skipping to the next in line? Recommend 30–60s auto-skip.
- **Queue visibility & exit:** can a player see their position, and can they leave voluntarily? Recommend yes to both — silent queues create support tickets.
- **Queue cap:** unbounded queues are a UX dead end. Recommend a visible cap (e.g., 10) with "table full" messaging beyond that.
- **Race condition:** two seats opening in the same round with a populated queue needs to be an atomic server-side operation (strict FIFO), not a client-driven race.

---

## 3. Chip Economy

This is the section with the most risk. Play-money chips still create incentives for abuse if there's no anti-fraud layer.

- **300 chips + 300 "welcome gift":** is this 300 total or 600? As written it reads as two separate grants — needs to be stated as one number.
- **Daily 300 chips at local 06:00:** if the timezone is client-reported, it's trivially exploitable by changing device clock to claim multiple times a day. **Must be server-side**: timezone set once at signup (or via IP geolocation), immutable or rarely-changeable, with a hard 24-hour cooldown enforced server-side — not a wall-clock check.
- **No anti-multi-accounting measure anywhere in the current spec.** Free daily chips with no fraud prevention invites bot-farmed accounts that either hoard chips or feed a "main" account via collusive chip-dumping. Even for play money, this kills any future leaderboard or competitive feature. Needs at minimum: device/IP fingerprinting, and per-account rate limits on rebuys and table joins.
- **No balance cap:** without one, chip totals become meaningless over months and any leaderboard is just "who's played longest." Recommend a soft cap or a prestige/reset mechanic.
- **Table-leave behavior undefined:** does a player's stack return to their global balance when they leave a table, or is it stranded if they don't return? Recommend: stack always returns to global balance on leave, no forfeiture.
- **Zero-balance dead end:** if a player busts and it isn't yet their daily-gift time, they're locked out of every table until tomorrow. This is a real UX cliff with no stated mitigation — consider a one-time low-balance top-up or a "wait for gift" countdown UI so it doesn't feel like a wall.

---

## 4. Chat & Moderation

The spec says "spectators marked" and "no NSFW, no assets" but has no enforcement mechanism:

- No mute, block, or report functionality specified.
- No rate limiting — spam is trivial without one.
- No moderation/ban pipeline for violations.
- No chat history persistence policy (does chat survive a rejoin/refresh?).
- "No NSFW" needs an actual enforcement method: profanity filter (which language(s)?), and/or human or automated report review queue. A rule with no enforcement isn't a spec, it's a hope.

---

## 5. Game Mechanics — Odds & Showdown

- **Win-probability computation is a real server cost**, not a UI detail. Live equity at flop/turn/river requires exact enumeration or Monte Carlo simulation per update. At scale, many concurrent all-in tables running this simultaneously needs a defined compute budget — this should be scoped as its own technical task, not bundled into "show odds."
- **Folded players' equity:** shown or excluded from the live calculation? Needs a decision.
- **Spectator visibility of hole cards:** standard in most of these apps, but currently undecided. This materially changes the chat/streaming experience and should be explicit.
- **Muck rights:** real poker lets a losing player muck (not reveal) at showdown. The current spec implies every hand is forced face-up. If that's intentional (simplification for a v1), state it explicitly; if not, muck rights need their own flow.
- **Side pot nobody is eligible for — RESOLVED (implemented).** A short stack is all-in for the main pot while two other players build a side pot above them; both of those players then fold (legal: the second is folding when they could have checked for free). Nobody is left with a claim to the side pot. Awarding it to the all-in player is wrong — side pots exist precisely to keep them out of it — so those chips are **returned to the players who contributed them**, split by betting level so the division is exact. Implemented in `HandEngine.Showdown` / `SidePotCalculator`; before this the engine divided by zero and crashed the hand. Revisit only if a stricter cardroom rule is wanted (e.g. coercing a free fold into a check so the state is unreachable).
- **Side pots & kickers:** the "show hand name below cards" spec (e.g., "FLUSH") doesn't address kicker tiebreaks or how multiple side-pot winners are displayed at once. This needs an actual UI spec, not just a label — recommend a pot-by-pot breakdown showing which stack of chips goes to which winner and why.

---

## 6. Private Tables — currently the weakest section

The existing bullet is a question mark with no answer. Needs a full pass:

- Who is the table host/owner, and what powers do they have (kick, custom stakes, invite-only)?
- "Unlimited chips, unless explicitly told" — told by whom, configured how, at creation time?
- Is there any cap on the number of private tables a single account can create? Without one, private tables become a loophole around the entire daily-chip economy.

---

## 7. Auth & Compliance

- Email domain allowlist is reasonable, but no password reset flow, no 2FA, and no session/device limits are specified. Given the chip-abuse risk in Section 3, weak auth compounds it directly.
- No enforcement mechanism specified for the profanity/NSFW chat rule (see Section 4).
- No stated age-gating position. Some jurisdictions treat simulated gambling with virtual currency as still requiring an age check even at zero cash value — worth a stated 18+ self-attestation at signup rather than silence, even if legal risk is low.

---

## 8. Missing Entirely (not just under-specified)

These aren't in the spec at all, and none of them are optional for a real-time poker product:

- **Disconnect handling.** Someone loses connection mid-hand holding the action — auto-fold? Auto-check where legal, else fold? Needs a defined sit-out/reconnect flow.
- **Per-player action timer / timebank.** Without one, a single AFK player freezes the entire table indefinitely.
- **Rake.** Presumably zero for play money — but state it explicitly, since it affects pot math and any future monetization path.
- **Server crash/restart recovery.** If the server dies mid-hand, does table state persist? Are chips restored, or is the hand voided?
- **Hand history / audit log.** Needed both for expected functionality and to resolve any fairness disputes about shuffle integrity.

---

## Summary: Decisions Needed Before Dev Starts

1. Total welcome chips: 300 or 600?
2. Server-side timezone lock mechanism for daily gift claims
3. Anti-multi-accounting strategy (fingerprinting, rate limits)
4. Balance cap: yes/no, and what happens at the ceiling
5. Rebuy-vs-queue-promotion timing rule
6. Blind structure per table tier
7. Muck rights: in or out of v1
8. Spectator hole-card visibility: yes/no
9. Private table ownership/permissions model and creation cap
10. Disconnect and action-timer behavior
11. Age-gating stance

Everything else in this document has a recommended default that can ship as-is unless you want to override it.
