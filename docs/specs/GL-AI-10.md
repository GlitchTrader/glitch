# GL-AI-10 — Normalize native outcomes for Hermes learning and calibration

Issue: #15
Priority: P1
Status: Normalization landed; native acceptance pending

## Intent

Give the paired Hermes profile an append-only, native-grounded outcome record that can support attributable learning and later calibration without giving Hermes execution authority.

## Current gap

The profile requires completed, attributable master outcomes and excludes infrastructure failures from learning. The NT runtime already owns the relevant native facts, but the learning exchange needs one normalized join across intent, cycle, decision packet, snapshot, route, account, trade, and native terminal evidence.

## Proposed change

Define a normalized outcome record containing decision and fill anchors; native terminal state; quantity and protection facts; PnL/R; MAE/MFE; drift and management facts; first-touch facts; attribution status; and explicit unresolved joins. Keep the record append-only and preserve the distinction between incomplete, unresolved, and attributable outcomes.

## Landed canonical layers

`reconcile-hermes-outcomes.py` now adds additive canonical layers:

- `decision_geometry` — Hermes intent geometry and the decision-price anchor.
- `native_geometry` — actual fill/exit, native economics, and initial
  protection legs.
- `execution_diagnostics` — the GL-AI-11 intent-fidelity projection.
- `normalized_outcome` — realized R plus sampled MFE/MAE R, each with source
  quality; sampled excursions remain explicitly non-exact.
- `forecast_outcome` — optional Hermes-only
  `STOP_BEFORE_PRIMARY_TARGET` probability joined to native exit evidence with
  a per-outcome Brier score. It is never an execution gate.
- `attribution` — origin, normalization status, learning eligibility, and
  excursion source quality.

First-touch states are restricted to `STOP_FIRST`, `PRIMARY_TARGET_FIRST`,
`NEITHER`, and `UNRESOLVED`. The reconciler does not claim ambiguous same-bar
ordering from a terminal ledger row.

Calibration or lesson promotion may consume only complete attributable records. No metric becomes a deterministic trading gate.

## Boundaries

- No strategy, fixed RR/ATR formula, probability threshold, or automatic promotion rule.
- No learning from native/infrastructure failure as if it were a trading outcome.
- No mutation of native state.

## Acceptance

- The record joins the required IDs and preserves unresolved joins.
- Delayed, partial, rejected, manually altered, reconnected, restarted, and completed outcomes are represented distinctly.
- Tests prove calibration excludes incomplete/unresolved records.
