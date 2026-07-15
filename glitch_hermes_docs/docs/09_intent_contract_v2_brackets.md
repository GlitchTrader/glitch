# 09 — Intent Contract v2: The Bracket Mandate

**Status:** normative amendment to `02_contracts.md` · **Origin:** operator dictation 2026-07-08 · **Schema:** `schemas/intent.v2.schema.json`

## Why v2

v1 allowed `stop_loss` and `take_profit_1` to be null. The operator has closed that door:

```text
Every order MUST HAVE SL and TP. NT handles execution.
AI is not responsible for stopping a loss mid-flight — NT/Glitch are.
```

Rationale: if the connection to Hermes dies, if Hermes hangs, if Glitch's UI thread stalls — the protective orders must already exist inside NinjaTrader (and at the broker where supported). The AI's job ends the moment the intent is accepted.

## Cadence

Hermes analyzes the tape every **5 minutes** (candle close) and emits **at most one intent per instrument per cycle**. Glitch acts on it or rejects it. There is no streaming decision channel.

## Action set

| Action | Operator alias | Meaning | Bracket fields |
|--------|---------------|---------|----------------|
| `ENTER_LONG` | BUY | open long | **required** |
| `ENTER_SHORT` | SELL | open short | **required** |
| `HOLD` | HOLD | keep existing position unchanged | ignored |
| `EXIT` | — | close existing position now (risk-reducing, always allowed) | ignored |
| `NOTHING` | DO-NOTHING | flat and stay flat | ignored |

`ADJUST_STOP` / `PARTIAL_EXIT` remain reserved for M1 and are rejected in M0. When enabled, `ADJUST_STOP` may only *tighten* (reduce risk) — a widening adjustment is rejected at the firewall.

## Bracket fields (ENTER_* intents)

| Field | Required | Semantics |
|-------|----------|-----------|
| `stop_loss` | **yes** | protective stop for TP1 quantity (or the full position when TP2 is absent). Loss side of entry. Defines the maximum per-contract trade risk. |
| `take_profit_1` | **yes** | first target. Profit side of entry. |
| `take_profit_2` | no | second target for the runner. Must be beyond TP1. Requires `quantity ≥ 2` and `quantity_tp1` split. |
| `stop_loss_2` | no | optional initial stop for the TP2 runner quantity. It must remain on the loss side of entry and be tighter than `stop_loss`. When omitted, the runner starts with `stop_loss`. Requires `take_profit_2`. |
| `quantity_tp1` | when TP2 present | contracts closed at TP1; remainder (`quantity − quantity_tp1 ≥ 1`) runs to TP2. |

All prices must be tick-rounded for the instrument (Glitch validates against the instrument metadata registry; a non-aligned price is a reject, not a silent round).

## Execution semantics (Glitch-side, deterministic)

1. Firewall passes (see `03_risk_firewall.md` + roadmap check chain) → executor submits the market entry using signal `GLT-AI-E-*`.
2. On full entry fill, Glitch immediately submits one account-local OCO stop/target pair per leg using `GLT-AI-S-*` / `GLT-AI-T-*`. A partial entry fill fails closed into cancel/flatten recovery; protection construction or submission failure does the same.
3. TP2 present → position is bracketed as two independent OCO pairs: TP1/SL for `quantity_tp1`, and TP2/SL2 (or TP2/SL when SL2 is omitted) for the remainder. A TP1 fill cancels only its paired stop; the runner remains protected.
4. A later `MOVE_STOP` intent may tighten all remaining Glitch-owned stops. It cannot widen risk.
5. `EXIT` → flatten the AI position via market order and cancel the bracket. Always allowed (risk-reducing), still journaled.
6. Sizing: Hermes proposes `quantity`; the firewall caps it. Risk per trade = `|entry − stop_loss| × pointValue × quantity` — computable **before** any order exists because SL is mandatory.

## M0 note

M0 caps quantity at 1 contract, so TP2/SL2 cannot activate (firewall rejects TP2 with quantity < 2). The contract still carries the fields from day one so schema and journal shape never change when M1 raises the cap.

## Versioning

`schema_version: "glitch.intent.v2"`. Glitch rejects any other version. v1 was never implemented in code; v2 is the first implemented contract. Schema changes require a new version const and a ledger entry.
