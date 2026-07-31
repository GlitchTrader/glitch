# Glitch v27 Intent-Execution Audit

**Date:** 2026-07-31
**Scope:** NinjaTrader Standard and Advanced source at `fix/runtime-health-ai` (v26 promotion `9285d02`)
**Posture:** doctrine-first top-down review after the 2026-07-30 Sim101 replication incident
**Doctrine:** ABKB `projects/glitch/project_profile.md` builder/operator fault contract; `docs/ai-program/operating-system-rail.md` prime invariant
**Authority:** `docs/ledger/ledger.json` remains the canonical work ledger

## Doctrine yardstick

Hermes owns intent, like a human. Glitch owns execution within that intent: it
does not mutate intent, does not block intent, does not initiate or close a
trade unless the user or Hermes signaled it, and does not police cognition with
hidden deterministic gates. Replication respects the intent expressed through
UI choices (routes, ratios, enabled compliance). NinjaTrader native state is
final execution truth.

Every finding below is measured against that yardstick.

## Root pattern

The 2026-07-30 incident was not one bug. It was one pattern with two habits:

1. **Believing unconfirmed native state.** Positive claims (open-protected,
   reconciled-protected, risk percentage) were computed from submit-time or
   cancel-side transients, or from unavailable account reads, instead of from
   confirmed native truth.
2. **Answering leg-level faults with position-level responses.** A single
   rejected protection leg — a transient native fault — was answered by
   cancel-everything-and-flatten, converting a broker hiccup into an exit that
   intent never requested.

The long recovery-helper names the operator flagged are the visible residue:
two parallel protection state machines (in-process group context and
restart-reconcile over native snapshots) each accreted their own fork of every
fix. That structural unification is `GL-ARCH-02`.

## What v27 changes

### 1. Confirmed-state protection claims (landed, `GL-AI-07` evidence)

`IsConfirmedProtectiveOrderState` (Accepted, TriggerPending, Working,
ChangePending, ChangeSubmitted, PartFilled) now gates:

- `group_entry_open_protected` recording and intent finalization;
- `reconciled_entry_native_protected` restart claims;
- the open-protected exposure chain (`HasExactCorrelationOwnedProtection`).

Submit transients (Initialized/Submitted) and cancel-side transients
(CancelPending/CancelSubmitted) can no longer latch a protection claim.
Exact-coverage protection still in a transient reports
`reconcile_entry_protection_confirmation_pending` instead of triggering
recovery flatten — nonterminal until native outcome, per GL-AI-07.

### 2. Leg-scoped protection repair (landed, `GL-PROT-01`, Sim fixture pending)

AI lane: a rejected protection leg on a filled entry gets exactly one
resubmission of the same leg — same price, quantity, OCO, signal — via
`TryRepairRejectedProtectionLeg`. Entry rejections, unknown orders, repeated
rejections, and failed repairs keep the full recovery response unchanged.

Follower lane: `TryRepairRejectedFollowerProtectionLeg` applies the same
policy, so a follower is not closed out of a position the master still holds
because one protective leg hit a transient fault.

This is not a retry engine. It executes the original intent once against a
transient native fault. It makes no new trading decision, invents no prices,
and keeps flatten as the terminal unknown-safe fallback.

### 3. Unknown-safe risk display (landed, `GL-STAB-01` slice)

The risk percentage now renders as a dash when native reads are not ready
(`isRiskDataReady`), instead of computing 100% from an unavailable-read-as-zero
equity. The enabled-compliance loop already skipped not-ready rows; the AI
portfolio snapshot already carries `native_state_available`. The full
provenance program (connection-status readiness, per-field provenance, the
audit trace matrix) remains open in `GL-STAB-01`.

## Verified non-findings

Reviewed against doctrine and found sound — no change made:

- **Risk firewall** rejects only factual/identity/schema state: allowlists,
  profile binding, tick rounding, market-side geometry, position conflict, and
  explicit user-enabled locks. Risk-per-trade, daily budget, prop capacity, and
  session windows are observational trail entries, not vetoes.
- **Intent server** enforces raw-byte limits, atomic claim, same-body replay,
  different-body conflict, expected-phase promotion, and derives phase from the
  execution result, so Pending stays nonterminal.
- **CopyEngine** copies accepted native execution deltas at user ratios,
  clamps closes to closable exposure, never retries ambiguous orders, treats
  cancellation as possibly-human and preserves it, treats unattributed exposure
  as manual override, and reports rather than repairs ambiguity.
- **Compliance actions** are specific, opt-in, journaled, scoped by account
  status, and lock release requires manual acknowledgement where configured.
- **EXIT/entry recovery paths** cancel only correlation-owned protection and
  close only attributable deltas, pending on visibility gaps.

## Open findings on the ledger

| Item | Priority | Status | Substance |
|---|---|---|---|
| `GL-PROT-01` | P0 | in_progress | Leg repair landed in source; bounded Sim fixture required to close |
| `GL-STAB-01` | P0 | ready | Full unknown-safe provenance program (display slice landed) |
| `GL-REL-01` | P0 | ready | Release catalog pairs v26 with Hermes profile 0.0.2.17, which has no tag (tags stop at 0.0.2.14); profile duplicated in two roots |
| `GL-DEP-01` | P0 | ready | Dependency/restart recovery proof (pre-existing) |
| `GL-ARCH-02` | P2 | backlog | Unify live-group and restart-reconcile protection lifecycles |

## Ledger repair

Eight items sat `in_progress` without `claim.assignee`/`claimed_at`, which the
rail contract forbids and the catch-up validator flagged. No agent was actively
working them, so their truthful status was restored: unclaimed active items to
`ready`, and dependency-blocked items (`GL-AI-07`, `GL-PERF-01`, `GL-DOC-01`,
`GL-ARCH-01`) to `backlog`. All evidence arrays were preserved. The ledger now
validates.

## Boundaries of this audit

- NinjaScript cannot be compiled in this environment; the operator's F5
  compile and export pipeline gate any package.
- 307 deterministic source-contract and worker tests pass; the two
  public-docs suites (English installation guide staleness, doc-app metadata)
  were failing before this audit and are documentation work, not runtime work.
- Nothing here is a profitability, unattended-operation, PA, or live-readiness
  claim. Repair-path behavior requires the `GL-PROT-01` Sim fixture before the
  v27 release claim is made.
