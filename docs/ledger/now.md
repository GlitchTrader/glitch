# Now — clean AI candidate

**Updated:** 2026-07-20

**Branch:** `cleanup/ai-core`

**Role:** internal Sim/paper candidate. It is not PA/live-authorized.

**Authority:** `docs/ledger/backlog.md`; this file is the compact handoff.

## Boundary

```text
Hermes            decides for configured masters from current evidence
Glitch AddOn      validates, executes, protects, replicates, reconciles, journals
NinjaTrader       owns native account, order, OCO, fill, and position truth
Codex             builds/tests/deploys only when explicitly requested
```

Hermes never submits follower orders. The producer-neutral CopyEngine shared with
non-AI Glitch owns enabled followers and ratios. Human NinjaTrader controls and
Glitch Flatten All remain authoritative.

## Candidate state

- AI execution is strict and fail-closed: validated JSON only, selected master
  only, current native portfolio state required, current account/group capacity
  required, market entry only, and native stop/target protection required.
- Every Hermes model call uses an isolated session tagged `trading`,
  `gpt-5.6-luna`, medium reasoning, no fallback provider, and a four-turn ceiling.
  Continuity is explicit: five MNQ market frames, the latest portfolio, six recent
  decisions/executions/outcomes, and native durable memory. Flat books are
  considered at five-minute boundaries; positioned books each minute. A native
  one-minute zero-model check wakes the worker, and any failed model/contract
  attempt retries on the next newer packet.
- The prompt supplies one literal strict JSON template scoped to the current
  cycle/books and forbids `final_choice` outside `decision_audit`.
- Delivery is idempotent and crash-safe through a durable outbox/receipt pair.
  Retry reuses the same intent id and never spends a second model call for the
  same packet.
- CopyEngine lifecycle cleanup runs only after native position truth changes, so
  re-entrant protection order callbacks cannot misclassify a just-filled follower
  as flat and cancel its new stop/target.
- Glitch entry signals are the ledger lifecycle identity. The earliest terminal
  exit wins, and failed/missing protection produces an auditable `process_error`
  outcome that is excluded from trading memory instead of being silently dropped.
- AI Auto is one truthful switch for the whole core apparatus. ON means the Glitch
  execution gate is open and the named Hermes core job is enabled; OFF closes the
  execution gate and pauses that job, so it cannot spend five-minute Luna calls.
  The UI reports only On or Off; snapshot and decision ages carry observability
  without inventing a third "Stale" control state.
- The Glitch AI feed separates the current five-snapshot collection window from
  the latest completed AI decision. It shows both ages, timestamps the decision,
  and retains the latest 20 decisions as expandable entries with their matching
  packet, execution result, and supporting MNQ snapshot metrics. Decision rows
  use the compact shared disclosure-row template so NinjaTrader cannot stringify
  WPF header visuals.
- Major Dashboard, Journal, Settings, and AI Scope sections now share one boxed,
  NinjaTrader-skin-aware hierarchy: distinct section header, attached body, and
  inset content. AI Trading Scope starts collapsed; decision history and Settings
  rules use the lighter compact disclosure row rather than a full section header.
- Authored Glitch AI copy—scope, feed state, stages, fields, errors, and snapshot
  table—is catalog-driven in all six supported locales. Changing language forces
  the dynamic feed to re-render. Model-authored reason/bull/bear/change text,
  account names, intent codes, and indicator symbols remain verbatim by design.
- Account/group capacity is dynamic. Hermes receives valid master quantities
  constrained by every enabled account's current rule ceiling, open exposure,
  and follower ratio. One-to-three native OCO legs support protected scale-out;
  repeated same-direction entries remain independently protected tranches.
- `MOVE_TP` moves every remaining Glitch-owned master target and may atomically
  tighten every remaining master stop. CopyEngine mirrors both changes to the
  corresponding follower-native protection orders.
- The portfolio packet carries `native_state_available`. Any failed native
  Positions/Orders read makes the packet ineligible and costs zero model calls.
- Eastern-time session policy is applied in the packet, firewall, final submit
  boundary, and the selected-group daily-close monitor. Selected followers are
  expanded from the selected master and flattened directly when required.
- FRED release-calendar rows remain analytics context and cannot create a live
  news lockout banner. News is not an invented execution veto.
- Startup/recompile is observe-only. Replicate OFF preserves existing native
  protection. Manual follower divergence suppresses automatic re-entry until an
  explicit Replicate/follower/ratio resync. Complete closes and Flatten All use
  NinjaTrader's native flatten path.
- Journal reconstruction ignores orphan exits and allocates reversal commission
  once. Master/Group/Fleet scope is explicit.

## Verification and market-open acceptance

- Shared source contracts: **37/37**.
- AI/Hermes contracts: **90/90**; complete AI suite **127/127**.
- Five production web builds: pass.
- Five web lint runs: pass.
- Python compilation, tracked PowerShell parsing, tracked JSON parsing, secret
  scan, and `git diff --check`: pass.
- Localization audit: **329 catalog keys**, **270 referenced code keys**, zero
  missing keys, zero malformed/empty six-locale rows; UTF-8 CJK/Cyrillic sentinels
  pass. This includes 18 older fallback-only labels closed during the AI UI pass.
- Complete 87-file AI AddOn folder deployed from this candidate with **87/87
  files matching, 0 hash mismatches, and 0 extra target files**.
- NinjaTrader F5 compile: green by operator confirmation after the complete
  87-file deployment on 2026-07-20.
- Installed Hermes worker matches source. Exactly one cron job is enabled:
  `glitch-direct-operator`, stored as `* * * * *`; the supervised gateway is
  running and the first post-install zero-model tick completed `ok`.
- Bounded prior Sim evidence on this clean architecture includes protected
  1:2:3 replication, three independent legs, partial fills, same-direction
  protected tranches, duplicate-intent rejection, and fleet flatten.
- The market-open acceptance exposed synchronous callback re-entry while the AI
  executor was creating the first master bracket. The protection-submission claim
  is now acquired before the first native `CreateOrder` call and released only on
  a real create/submit failure. This prevents duplicate bracket construction.
- Bounded intent `7bd326d8-c952-46b8-8604-a913cab6607b` runtime-proved the fix:
  one Sim101 entry, one master stop, one master target, follower entries at the
  configured 1x/2x ratios, one native OCO pair per follower contract, and a native
  target close across the complete group. Final truth is Sim101/102/103 flat with
  zero working orders and realized PnL of +$8.00 / +$12.50 / +$24.50. The deployed
  AddOn matches the candidate 87/87 with zero hash mismatches.

## Red-team corrections in the final pass

1. The daily-close monitor previously suppressed replication callbacks while
   flattening only selected masters. It now expands selected groups and directly
   flattens every required connected member.
2. Failed native account reads could previously serialize as flat/order-free.
   Native state is now explicit and fail-closed from writer through worker.
3. Dead restart-recovery and fail-open convenience APIs were removed instead of
   being retained as misleading alternate paths.
4. FRED dataset-release metadata was separated from real event-alert state,
   removing the false weekend FOMC banner.

## Honest release gates

- The bounded market-open Sim lifecycle now passes for entry, ratio followers,
  native brackets, native close, and final flat/order-free state. This is software
  acceptance evidence, not profitability or PA/live authorization.
- Exchange holidays and special closes still need an authoritative session source.
  Regular weekly and daily maintenance windows are implemented; this residual is
  tracked under GL-063 and blocks unattended PA/live promotion.
- `npm audit --omit=dev` reports two moderate vulnerabilities in Next's bundled
  PostCSS. All apps are already on stable Next 16.2.10; npm's proposed downgrade
  to Next 9 is invalid. Track upstream; do not force a breaking downgrade.
- Profitability is not a software acceptance claim. Freeze this commit for the
  next paper sample and reconcile against authoritative NinjaTrader exports before
  changing prompts or risk posture.

## Next action

AI Auto is currently operator-enabled in paper mode. Let this corrected candidate
collect a versioned paper sample without changing execution logic. Any PA/live AI
promotion remains a separate explicit operator decision.
