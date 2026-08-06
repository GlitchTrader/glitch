# GL-AI-10 — Normalize native outcomes for Hermes learning and calibration

Issue: #15
Priority: P1
Status: Proposed

## Intent

Give the paired Hermes profile an append-only, native-grounded outcome record that can support attributable learning and later calibration without giving Hermes execution authority.

## Current gap

The profile requires completed, attributable master outcomes and excludes infrastructure failures from learning. The NT runtime already owns the relevant native facts, but the learning exchange needs one normalized join across intent, cycle, decision packet, snapshot, route, account, trade, and native terminal evidence.

## Proposed change

Define a normalized outcome record containing decision and fill anchors; native terminal state; quantity and protection facts; PnL/R; MAE/MFE; drift and management facts; first-touch facts; attribution status; and explicit unresolved joins. Keep the record append-only and preserve the distinction between incomplete, unresolved, and attributable outcomes.

Calibration or lesson promotion may consume only complete attributable records. No metric becomes a deterministic trading gate.

## Boundaries

- No strategy, fixed RR/ATR formula, probability threshold, or automatic promotion rule.
- No learning from native/infrastructure failure as if it were a trading outcome.
- No mutation of native state.

## Acceptance

- The record joins the required IDs and preserves unresolved joins.
- Delayed, partial, rejected, manually altered, reconnected, restarted, and completed outcomes are represented distinctly.
- Tests prove calibration excludes incomplete/unresolved records.
