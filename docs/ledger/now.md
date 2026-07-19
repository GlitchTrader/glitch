# Now — clean AI candidate

**Updated:** 2026-07-19

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
- One named Hermes `trading` session uses `gpt-5.6-luna`, medium reasoning, no
  fallback provider, and a four-turn ceiling. Flat books are considered at one
  five-minute decision boundary; positioned books may be reconsidered each minute.
- Delivery is idempotent and crash-safe through a durable outbox/receipt pair.
  Retry reuses the same intent id and never spends a second model call for the
  same packet.
- AI Auto is one truthful switch for the whole core apparatus. ON means the Glitch
  execution gate is open and the named Hermes core job is enabled; OFF closes the
  execution gate and pauses that job, so it cannot spend five-minute Luna calls.
  The UI reports only On or Off; snapshot and decision ages carry observability
  without inventing a third "Stale" control state.
- The Glitch AI feed separates the current five-snapshot collection window from
  the latest completed AI decision. It shows both ages, timestamps the decision,
  and retains the latest 20 decisions as expandable entries with their matching
  packet, execution result, and supporting MNQ snapshot metrics. Decision rows
  reuse the shared Glitch string-header accordion template so NinjaTrader cannot
  stringify WPF header visuals.
- AI Trading Scope uses the same shared accordion and starts collapsed, keeping
  one-time account configuration available without displacing the live feed.
- Account/group capacity is dynamic. Hermes receives valid master quantities
  constrained by every enabled account's current rule ceiling, open exposure,
  and follower ratio. One-to-three native OCO legs support protected scale-out;
  repeated same-direction entries remain independently protected tranches.
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

## Verification frozen for this candidate

- Shared source contracts: **33/33**.
- AI/Hermes contracts: **80/80**; complete AI suite **113/113**.
- Five production web builds: pass.
- Five web lint runs: pass.
- Python compilation, tracked PowerShell parsing, tracked JSON parsing, secret
  scan, and `git diff --check`: pass.
- Complete 87-file AI AddOn folder deployed from this candidate with **87/87
  files matching and 0 hash mismatches** after stale-target cleanup.
- NinjaTrader F5 compile: green; the custom assembly rebuilt at 15:11 local with
  no populated compile-error row.
- Bounded prior Sim evidence on this clean architecture includes protected
  1:2:3 replication, three independent legs, partial fills, same-direction
  protected tranches, duplicate-intent rejection, and fleet flatten.

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

- The weekend cannot prove exchange fills. Before PA/live consideration, run one
  bounded market-open Sim lifecycle: master entry → ratio followers → native
  brackets → native close → all selected accounts flat and order-free → Journal
  and outcome reconciliation.
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

At market open, perform the single bounded Sim acceptance above. If it passes,
reset the paper epoch once and let the frozen candidate collect a versioned paper
sample. Any PA/live AI promotion remains a separate explicit operator decision.
