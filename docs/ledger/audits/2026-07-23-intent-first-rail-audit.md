# Intent-first replication and AI rail audit — 2026-07-23

## Authority and scope

This audit follows the canonical Glitch rail in `docs/ledger/ledger.json`.
Findings were issued as Spec Kit work items before implementation:
`GL-REP-01`, `GL-AI-06`, `GL-AI-07`, `GL-AI-08`, `GL-COMP-01`,
`GL-PERF-01`, `GL-DOC-01`, and `GL-ARCH-01`.

The governing hierarchy is:

```text
explicit human intent > Hermes intent > deterministic inference
native NinjaTrader facts remain execution truth
```

Standard source is `standard/20`; experimental AI source is `ai/22`.
The public Hermes profile candidate remains a separate source lane and is not
installed or published merely because its local source changed.

## Architect findings and disposition

### Replication

- Native master execution, not bracket discovery, is the replication event.
- The configured group, enabled route, and ratio govern future execution
  deltas. Configuration changes never align existing positions.
- Explicit **Sync** is the only catch-up owner. It closes an exact attributable
  delta first, waits for native flat truth when direction must change, then
  recomputes one opening tail.
- A manual master partial close is a native master execution and is replicated.
  A manual follower change remains the user's state; later master deltas still
  replicate and do not silently erase it.
- Follower protection recovery persists exact ratio bits and the cumulative
  allocation offset. Legacy, changed, duplicate, or malformed identity is
  observational and causes no guessed 1×/leg-zero recovery mutation.
- Native follower cleanup and Sync lifecycle advancement run from
  `PositionUpdate`, after position truth changes. Master stop and target changes
  mirror through the same shared path in both editions.
- Replication, Sync, and recovery no longer use broad whole-instrument
  `Account.Flatten`. Only exact deltas and Glitch-owned protection are mutable.
- Bracket readiness, pending-copy TTL, inferred follower capacity, and implicit
  alignment gates were removed from the replication path.

### AI intent lifecycle

- Intent UUID plus body hash has one atomic durable execution owner. Same UUID
  with different content is a conflict; duplicates replay immutable or
  in-progress state.
- Submit return is pending, not proof of execution.
- `execution_started` crash recovery requires connected native account order
  visibility, two dispatcher observations, and a 30-second stable absence
  interval before one possible crash-before-submit resume. `pending` never
  resubmits.
- ENTRY success requires exact correlation-owned native exposure and working
  correlation-owned protection. An independent AI addition is evaluated as its
  own correlation; a human same-side add, partial close, full close, opposite
  trade, or unrelated order supersedes ambiguous recovery.
- If a new AI correlation cannot establish exact complete protection, Glitch
  preserves the durable baseline correlations and unwinds only the attributable
  new fill through a persisted UUID-named recovery close. That close uses the
  same connected/two-snapshot/30-second at-most-one-resume boundary and cannot
  complete until native net returns exactly to the durable baseline.
- EXIT writes a durable per-account plan before Submit with the exact account,
  instrument, direction, quantity, and protected correlations. Protection
  remains live until the UUID-named native exit is actionable. Reconciliation
  validates plan, native exit identity, remaining quantity, direction, and
  plan-only protection before canceling only those correlations. A concurrent
  AI addition remains protected.

### Human and Hermes authority

- A due complete packet invokes Luna even when operational facts make an entry
  currently unattractive or factually unavailable. Those facts remain visible
  to cognition and may later produce a truthful native/executor rejection.
- Inferred prop-firm size, Apex buffer, time-window, session, or stop-widening
  policy does not suppress human or Hermes intent.
- Deterministic compliance order actions require a visible, persisted,
  default-off Settings opt-in. The daily close control states the exact account
  allowlist and its broad 16:59 Eastern flatten behavior in all six authored
  locales. AI enablement, allowlisting, or Hermes pause is not consent.

### Performance and minimality

- The minute publisher owns one background preflight before any native capture
  after restart. Existing complete frame/packet artifacts avoid native capture.
- Filesystem reads, hashes, serialization, and atomic writes run off the
  dispatcher and outside the publisher lock.
- A failed dispatcher capture or queue handoff releases its ownership lease.
- Dispatcher, background, native Positions/Orders, and analytics-lock timing
  counters expose max, p95, and p99 rather than hiding work.
- Shared CopyEngine cleanup consolidated route lookup, follower signal parsing,
  submission results, and observational recovery construction. The resulting
  delta is +701 lines per edition; retained state distinguishes native callback,
  protection, Sync, manual-override, and restart/idempotency phases.
- Unused AI reconciliation and publisher state was deleted. A cross-platform
  contract now compares generated prop-firm JSON semantically instead of
  failing on CRLF checkout bytes.

## Edge-case coverage

Source contracts and behavioral fixtures cover:

- bracketless master entry and delayed protection discovery;
- multiple native fills and ratio-scaled allocation slices;
- manual master partial/full close;
- manual follower add, partial/full close, opposite position, and divergence;
- explicit Sync with close/open phases and human interleave;
- disconnect, missing native snapshots, duplicate identity, changed route, and
  restart recovery;
- AI concurrent duplicate delivery and UUID/body conflict;
- crash before Submit, crash/ambiguity after Submit, delayed order visibility,
  reject, partial fill, late fill, protection failure, AI addition, and
  concurrent human or AI interference;
- daily-close opt-in off/on, empty/exact scope, restart, and unresolved account;
- publisher restart, rollover, incomplete artifact, cache hit, capture failure,
  and queue failure.

Native callback reentrancy, broker delay, disconnect/reconnect, and UI timing
still require bounded NinjaTrader Sim evidence because Python/source fixtures
cannot prove NinjaTrader runtime ordering.

## Live read-only observation and the displayed ratio

At the read-only observation boundary, the configured group was:

| Role | Account | Ratio | Position | Session realized PnL |
|---|---|---:|---:|---:|
| master | Sim101 | 1 | 0 | $65.90 |
| follower | Sim102 | 2 | 0 | -$14.90 |
| follower | Sim103 | 3 | 0 | -$22.10 |

The native master trade used two contracts. The follower execution quantities
were four and six contracts, proving the 2× and 3× quantity ratios. Ratio never
means “multiply the PnL cell.” Those cells are each account's independent
session total after its own fill prices, slippage, prior executions, and
commissions. The screenshot therefore shows correct ratio-sized quantities and
different account PnL outcomes, not a PnL-ratio calculation.

The Control Center observation showed all positions empty. The visible order
history contained only filled or cancelled orders; no working order was used as
test authority.

## Validation evidence

- Standard source contracts: 61/61 passed.
- AI shared source contracts: 54/54 passed.
- AI Hermes contracts: 186/186 passed.
- Durable-main contracts and localized documentation: 46/46 passed.
- Public Hermes candidate: checksum manifest verified; PowerShell setup/reset
  scripts parsed; direct worker help path passed; normalized worker parity with
  AI source passed.
- Standard, AI, durable-main, and public-profile `git diff --check`: clean
  apart from Git's CRLF conversion warnings.

These results prove source shape, not live trading readiness, profitability,
or native callback timing.

## Model and role rail

- Sol high owned architecture and independent review.
- Terra high owned bounded Standard/AI implementation corrections.
- Terra/root test passes verified the merged workspace state.
- Luna medium remains the product's Hermes runtime cognition model; it was not
  used as a coding substitute.
- No all-Sol-extra-high swarm was used. Failed review returned work to the
  coder and ledger cycle instead of being excused by green source tests.

## Explicit decisions under ambiguity

1. NinjaTrader exposes account connection/order visibility but no authoritative
   cross-process “order synchronization complete” event. The implementation
   uses connected order-feed truth plus two dispatcher snapshots and a
   30-second stable absence interval as a delivery-coordination boundary. This
   is not a trading-policy veto and is documented rather than presented as
   atomic certainty.
2. A legacy copied follower signal cannot prove its original ratio/allocation
   offset. Recovery observes and journals it but submits no mutation.
3. A source-only public Hermes profile candidate is not the current published
   profile. Public version facts remain at the last published v0.0.2.8 until an
   explicitly authorized commit/push/update promotes the prepared v0.0.2.9
   candidate.
4. The available native environment has nine accounts, not the 25-account
   bounded performance fixture required by `GL-PERF-01`. Instrumentation and
   source behavior may pass while that ticket remains runtime-open.

## Remaining native rail

The final tester cycle is:

```text
architect review
  -> source approval
  -> AI off + flat/order-free proof
  -> authorized full-folder deployment
  -> NinjaScript compile
  -> bounded Sim callback/UI scenarios
  -> final flat/order-free proof
  -> ledger evidence
  -> architect closure
```

Experimental AI deployment is coupled to the public Hermes profile. It is not
complete until the authorized public profile branch is pushed, the local
profile update and installed `setup.ps1` succeed, and installed distribution,
skills, worker, plugin, jobs, gateway, and paused/enabled-state parity are
verified.
