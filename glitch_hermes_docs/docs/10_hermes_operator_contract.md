# 10 — Hermes Operator Contract

**Reconciled:** 2026-07-22

This contract defines the local Experimental AI v0.0.2.2 operator. It does not authorize unattended, PA, or live trading.

## Authority

```text
Hermes: cognition and master decisions
Glitch: factual state, firewall, native execution/protection, replication, attribution, reconciliation, journals
NinjaTrader: native account/order/execution/position truth
Codex: source changes and verification only
```

One persistent `glitch` profile owns durable memory. Every eligible model call uses an isolated session tagged `trading` and receives the immutable packet plus bounded recent decisions, receipts, outcomes, and relevant memory. Route labels are execution identities, not separate personalities. Hermes never emits follower intents.

## Distribution and activation

The profile is installed and updated locally from `GlitchTrader/glitch-hermes-profile`. Setup creates exactly:

- minute `glitch-direct-operator`;
- 15-minute `glitch-learning-supervisor`.

Fresh setup leaves trading inactive. The user selects the master/group in Glitch and activates through AI Auto or `/trade`. `/trade_mode paper|live` is a deprecated alias that only activates the same selected scope; it does not create a second authority mode. Profile updates preserve authentication, overrides, sessions, memories, ledgers, and enabled/paused job state.

## Decision input

Each packet contains five latest paired complete minute frames, continuity and missing-minute metadata, current selected-master state, native protection, authoritative account capacity/buffer fields, valid master quantities, recent attributable history, and policy identity. Missing or ambiguous critical facts remain explicit.

Hermes treats current acceptance, rejection, structure, excursion, and changed evidence as more important than stale forecasts. It may choose:

```text
ENTER_LONG
ENTER_SHORT
HOLD
MOVE_STOP
MOVE_TP
EXIT
NOTHING
```

Entries are MARKET-only and require complete protected legs. Hermes may choose one leg, several independent legs, reserve capacity, or add a later independently protected same-direction tranche. No numeric sizing schedule, stop formula, risk percentage, target formula, opportunity quota, grid, or martingale rule is encoded.

## Intent v3

Each intent carries stable identity, packet/snapshot identity, route/master identity, action, confidence, reasoning, and the action-specific fields in `schemas/intent.v3.schema.json`.

- Entry leg quantities are positive and sum to the selected master quantity.
- Every entry leg has an absolute protective stop and profit target on the correct side of live price.
- Targets need not be ordered and stops need not become progressively tighter.
- `MOVE_STOP` and `MOVE_TP` use named `protection_updates`; unspecified legs remain unchanged.
- A stop may tighten or move farther away while protective. Widening requires fresh authoritative Apex state, complete Glitch-owned coverage, point value, and total-downside recomputation.
- v2 entry/no-op/hold/exit/global MOVE_STOP remain compatibility inputs. Multi-target v2 MOVE_TP fails safely.

## Firewall

Glitch rejects before mutation when policy/scope, schema, identity, idempotency, native state, ownership, instrument metadata, tick/side validity, complete protection, contract capacity, authoritative session state, or Apex liquidation survival cannot be established. Normal movement from snapshot price to market fill is not a cognitive veto.

Followers and ratios do not constrain Hermes's master quantity. CopyEngine validates and executes each follower route independently. Risk-reducing actions remain available when entry-grade data is absent; unsafe widening causes zero mutation.

## Cadence and delivery

- Flat: first eligible packet at least five elapsed minutes after the last attempt.
- Positioned: every new complete packet.
- Model, schema, validation, transport, firewall, or executor failure: next available packet.

Transport uncertainty reuses the same durable idempotent outbox. Terminal rejection requests new cognition on the next packet. Locks record PID/start time and replace dead owners. Intent state advances atomically from receipt to terminal result. Restart recovery reconciles deterministic native signals and journals and never blindly resubmits an ambiguous entry.

## Learning

The single 15-minute learning supervisor processes completed outcomes, NOTHING, rejected/non-executed actions, and five-frame forward decision episodes. It runs hourly supervision, 300-minute planning, and completed-session journals when due. Evidence joins the immutable packet through `cycle_id`; malformed output receives one bounded repair; unprocessed evidence remains pending.

Guidance follows propose → later independent confirmation/contradiction → activate/revise/rollback. Hermes cannot rewrite installed SOUL, skills, Glitch policy, groups, or execution code.

## Stop lines

- Do not give Hermes direct NinjaTrader/broker authority or follower ownership.
- Do not make free-form text actionable or silently retry ambiguous native state.
- Do not add LIMIT without place/cancel/replace, TIF/expiry, partial-fill protection, replication, and restart recovery.
- Do not infer profitability or broader account authority from tests, compile, profile installation, or a short sample.

Reliability details are canonical in `14_intent_v3_reliability.md`; acceptance state is in `../../docs/ledger/now.md` and `../../docs/ledger/backlog.md`.
