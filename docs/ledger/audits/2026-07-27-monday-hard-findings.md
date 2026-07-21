# Monday morning hard findings — 2026-07-27

**Verified:** 2026-07-21  
**Source:** `cleanup/ai-core` at `9ec42b32de51ad694d16c144ad65c2684b2ec499`  
**Role:** Sim/paper candidate only

This note consolidates hard release blockers already supported by candidate evidence. It does not create a second queue; `docs/ledger/backlog.md` remains authoritative.

## GL-AUD-001 — holiday and special-close authority is incomplete

**Severity:** P0 for unattended PA/live promotion

The candidate implements ordinary Eastern-time daily, weekly, weekend, and maintenance boundaries. The candidate ledger explicitly records that exchange holidays and special closes still lack an authoritative session source.

A broker rejection is not equivalent to publishing correct pre-trade eligibility. Without authoritative holiday/special-session truth, an unattended live system cannot prove that entry eligibility is correct for exceptional sessions.

### Monday instruction

- Keep AI Auto in Sim/paper mode.
- Do not promote to PA/live or unattended operation.
- Close GL-063 with an authoritative exchange/session source and runtime fixtures for special closes and holidays.
- Exits and risk reduction must remain available when entry is blocked.

## GL-AUD-002 — profitability is not established by the existing samples

**Severity:** P0 for commercial or live-trading claims

The recorded evidence contains materially different scopes and one historically corrupted Journal:

- NinjaTrader full-day view: `+$401.50`, 71 trades;
- later 08:00-scoped report: `+$291.50`, 66 trades;
- previously corrupted Glitch Journal: 44 trades and `-$1,374.50`.

The candidate ledger correctly treats these as diagnostic samples, not profitability proof. The current code and prompt/skill set must remain frozen while a new authoritative paper sample is collected and reconciled against NinjaTrader exports.

### Monday instruction

- Make no profitability, expectancy, or readiness claim from the historical numbers.
- Bind every evaluation to the exact commit, prompt/skill hashes, account, instrument, date/time scope, costs, and NinjaTrader export.
- Reconcile Journal and TradeLedger to native NinjaTrader truth before allowing learning or calibration evidence to promote a change.
- Continue GL-064 only on a versioned untouched window.

## GL-AUD-003 — gateway recovery and idempotent replay are not fully runtime-proven

**Severity:** P1 — continuity and duplicate-cost/order risk

The latest commit proves that detached learning no longer blocks the minute trading operator. Broader GL-047 recovery evidence remains open:

- terminal/Codex exit must not stop scheduled operation;
- gateway status must agree across the service and Hermes surfaces;
- restart-on-failure must be demonstrated;
- a killed worker must reuse the same outbox and intent IDs without a second model call;
- missed windows must be journaled.

These are unclosed runtime acceptance requirements, not claims that the current implementation has already failed them.

### Monday instruction

Do not begin central VPS/API expansion or any live promotion until the exact recovery matrix is evidenced on the frozen candidate.

## GL-AUD-004 — two moderate production dependency findings remain open

**Severity:** P2 — dependency maintenance

The candidate ledger records that `npm audit --omit=dev` reports two moderate findings in Next's bundled PostCSS dependency. The current applications are already on the accepted Next 16.2.10 line, and npm's proposed downgrade to Next 9 is invalid.

### Monday instruction

- Do not apply the breaking audit suggestion.
- Preserve the audit output as evidence.
- Track the upstream Next/PostCSS fix and update only through a supported Next release with all five production builds and lint gates rerun.

## Stop line

The correct next state is unchanged: freeze the corrected candidate, collect truthful paper evidence, and close runtime/session authority. No new AI architecture is justified by these findings.