# 09 — Intent Contract v2: The Bracket Mandate

**Status:** normative amendment to `02_contracts.md` · **Origin:** operator dictation 2026-07-08 · **Schema:** `schemas/intent.v2.schema.json`

> Compatibility note (v0.0.2.2): new cognition emits `glitch.intent.v3`.
> v2 remains accepted for entries, no-ops, holds, exits, and global
> `MOVE_STOP`. v2 `MOVE_TP` is accepted only when exactly one native target
> remains; multi-target scope is rejected rather than collapsed. See
> `14_intent_v3_reliability.md` for the current contract.

## Why v2

v1 allowed `stop_loss` and `take_profit_1` to be null. The operator has closed that door:

```text
Every order MUST HAVE SL and TP. NT handles execution.
AI is not required for protection to work — NT/Glitch hold the native brackets.
```

Rationale: if the connection to Hermes dies, if Hermes hangs, or if Glitch's UI thread stalls, protective orders must already exist inside NinjaTrader (and at the broker where supported). Later AI cycles may hold, tighten, add a protected same-direction tranche, or exit; protection never depends on those later calls.

## Cadence

Hermes reviews every five-minute boundary while flat and each minute while a scoped master is positioned. A failed inference or strict-contract attempt makes the next newer minute packet eligible immediately, without repeating the failed packet. Each invoked cycle uses an isolated session tagged `trading`, receives bounded explicit decision/outcome continuity, and emits one intent per configured route-bound group. Timeframe rows are live in-progress observations unless explicitly marked closed.

## Action set

| Action | Operator alias | Meaning | Bracket fields |
|--------|---------------|---------|----------------|
| `ENTER_LONG` | BUY | open long | **required** |
| `ENTER_SHORT` | SELL | open short | **required** |
| `HOLD` | HOLD | keep existing position unchanged | ignored |
| `MOVE_STOP` | — | tighten every active Glitch-owned master stop | `stop_loss` only |
| `MOVE_TP` | — | move every remaining Glitch-owned master target; optionally tighten stops in the same change | `take_profit_1`, optional `stop_loss` |
| `EXIT` | — | close existing position now (risk-reducing, always allowed) | ignored |
| `NOTHING` | DO-NOTHING | flat and stay flat | ignored |

`ADJUST_STOP` remains an unsupported legacy alias; use `MOVE_STOP`. Partial exits happen through independently protected target legs rather than an unprotected free-form command.

## Bracket fields (ENTER_* intents)

| Field | Required | Semantics |
|-------|----------|-----------|
| `stop_loss` | **yes** | protective stop for TP1 quantity (or the full position when TP2 is absent). Loss side of entry. Defines the maximum per-contract trade risk. |
| `take_profit_1` | **yes** | first target. Profit side of entry. |
| `take_profit_2` | no | second target for the runner. Must be beyond TP1. Requires `quantity ≥ 2` and `quantity_tp1` split. |
| `stop_loss_2` | no | optional initial stop for the TP2 runner quantity. It must remain on the loss side of entry and be tighter than `stop_loss`. When omitted, the runner starts with `stop_loss`. Requires `take_profit_2`. |
| `quantity_tp1` | when TP2 present | contracts closed at TP1; remainder (`quantity − quantity_tp1 ≥ 1`) runs to TP2. |
| `take_profit_3` | no | third target beyond TP2. Requires TP2, `quantity ≥ 3`, and `quantity_tp2`. |
| `stop_loss_3` | no | optional initial stop for the TP3 leg; loss-side and no looser than the preceding stop. |
| `quantity_tp2` | when TP3 present | contracts closed at TP2; the positive remainder runs to TP3. |

All prices must be tick-rounded for the instrument (Glitch validates against the instrument metadata registry; a non-aligned price is a reject, not a silent round).

## Execution semantics (Glitch-side, deterministic)

1. Firewall passes (see `03_risk_firewall.md` + roadmap check chain) → executor submits the market entry using signal `GLT-AI-E-*`.
2. On full entry fill, Glitch immediately submits one account-local OCO stop/target pair per leg using `GLT-AI-S-*` / `GLT-AI-T-*`. A partial entry fill fails closed into cancel/flatten recovery; protection construction or submission failure does the same.
3. TP2/TP3 present → position is bracketed as two or three independent OCO pairs. A target fill cancels only its paired stop; every remaining leg stays protected.
4. A later `MOVE_STOP` intent may change named remaining Glitch-owned stops in either direction while each stop remains on the protective market side. Capacity and liquidation-buffer evidence inform Hermes; they are not hidden amendment vetoes.
5. A later `MOVE_TP` intent atomically moves every remaining Glitch-owned target to one absolute profit-side price and may tighten every remaining stop in the same change. CopyEngine mirrors both master changes to matching follower protection orders.
6. `EXIT` → flatten the AI position via market order and cancel the bracket. Always allowed (risk-reducing), still journaled.
7. Sizing: Hermes chooses master quantity from current evidence. Glitch verifies only structural native executability before submission; supplied capacity remains packet evidence.

There is no AI-only one-contract cap. Current account/group state and prop-firm ceilings remain visible evidence; they do not deterministically choose or veto Hermes quantity.

## Versioning

`schema_version: "glitch.intent.v2"`. v2 was the first implemented contract and remains the bounded compatibility contract described above. Current cognition emits v3. Schema changes require a new version const and a ledger entry.
