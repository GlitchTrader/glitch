# 11 — Snapshot, Ingestion, And Learning Pipeline

## Status

Design doctrine / operator contract extension. This is not live trading authority and not a request to add an always-on AI daemon.

Canonical invariant:

```text
Hermes proposes. Glitch validates, executes, journals, and protects.
```

## Why this exists

The AI program needs one shared data shape for three uses:

1. live operator decisions;
2. historical replay/export;
3. pattern mining and learning.

The simple rule is:

```text
The historical exporter must emit the same snapshot shape the live Hermes operator reads.
```

If live and historical data use different shapes, learning will not transfer cleanly.

## Layer 1 — minute snapshot

Mode: deterministic Glitch-side export. No LLM.

Cadence: every 1 minute, or the fastest cadence Glitch can produce without UI/thread risk.

Purpose:

```text
capture fresh market/account/risk state
write raw snapshot
write normalized snapshot when available
version the schema
make live and historical replay comparable
```

Recommended shape:

```text
snapshot:
  schema_version
  snapshot_id
  snapshot_hash
  created_utc
  source_mode: live | historical_replay
  instruments[]:
    instrument
    timestamp_utc
    price / OHLC / volume when available
    timeframe_readings[]
    indicators[]
    regime_features
    trend_features
    momentum_features
    volatility_features
    order_flow_features when available
    no_trade_reasons
  portfolio:
    accounts[]
    positions[]
    working_orders[]
    realized/unrealized PnL
    drawdown/buffer state
    lock state
  policy:
    prop firm rules version
    account allowlist status
    instrument allowlist status
  provenance:
    code_version
    data_source
    normalization_version
```

Keep raw and normalized data separate:

```text
raw_snapshot        source-close export, minimal interpretation
normalized_snapshot derived features, indicator normalization, regime labels
```

Do not let Hermes infer missing critical fields. Missing critical state forces `NOTHING`.

## Layer 2 — five-minute operator

Mode: one centralized Hermes LLM cycle. The local cron is the temporary validation harness.

Cadence: every five-minute boundary while flat and each minute while a scoped master is positioned. Any failed inference or output-contract attempt retries on the next newer minute packet. Timeframe rows are live in-progress observations unless explicitly marked closed.

Purpose:

```text
read latest valid snapshot
read recent Glitch-journaled outcomes
read active rules plus relevant hypotheses, evidence, and lessons
emit one strict JSON intent per configured route-bound group
```

Allowed current actions:

```text
ENTER_LONG
ENTER_SHORT
HOLD
MOVE_STOP
MOVE_TP
EXIT
NOTHING
```

Output stays small. The cost risk is input/context bloat, not the intent JSON.

### Current stabilization transport

Glitch writes a minute frame only after market and portfolio snapshots with the same `snapshot_id` are both present. Once five consecutive frames exist, it atomically publishes one immutable rolling five-frame packet per minute under `GlitchData/hermes/exchange/glitch`. The lightweight worker wakes on Hermes's native one-minute tick, but invokes Luna only on five-minute boundaries while flat, every minute while a scoped master is positioned, once for an explicit directive, or on the next newer packet after any failed model/contract attempt.

Hermes native cron owns the wake-up under a supervised gateway. Its worker performs a zero-model check for a new packet, creates an isolated `trading`-tagged session for every eligible model call, resends bounded Glitch decision/execution/outcome tails, supplies a literal valid-output template, and submits strict intents to Glitch's authenticated receiver. A failed packet is not repeated; its next newer packet becomes immediately eligible and cannot inherit the failed transcript. It does not classify opportunities or impose trading archetypes before inference. Contract/scope validation cannot replace Hermes's probabilistic decision; Glitch's firewall remains the execution authority.

Codex is not present in snapshot publication, scheduling, inference, delivery, execution, journaling, or learning.

### Target central ingestion and delivery

The central ingest service produces the same versioned minute snapshot and five-frame decision-packet formats as the client harness. Shared fixtures and hashes enforce schema parity; central code must not invent a second feature vocabulary.

One canonical packet causes one Hermes recommendation. The stored recommendation includes a stable ID, packet/snapshot hashes, instrument, desired action/position, confidence, thesis, expiry, structural stop/targets, and risk metadata. Entitled Glitch clients poll for that record every five minutes. Clients do not trigger model calls.

Each client combines the recommendation with current local portfolio and group state, validates it through Glitch, executes only the group master, and lets the existing replication engine own followers. The client posts bounded receipts/outcomes keyed by recommendation and local execution IDs. Raw credentials and direct order authority never leave the client.

Learning distinguishes shared market evidence from client execution evidence. Central outcomes may aggregate slippage, rejection, bracket, and PnL statistics without treating one customer's account state as universal policy. Only completed, ledger-correlated outcomes enter the learning corpus.

## Layer 3 — completed-trade debrief and hourly review

Mode: Hermes cron, preferably script-assisted with bounded LLM reasoning only when needed.

Cadence: debrief new completed master outcomes every 15 minutes; review accumulated episodes every 1 hour.

Each completed entry is joined by `cycle_id` to its immutable pre-decision packet. The debrief carries the selected and available quantities, pre-entry position and complete native protection, initial-entry or addition classification, every target leg and stop, planned downside by leg and in total, realized PnL, per-contract and planned-risk-normalized MAE/MFE, all management decisions, and the actual exit reason. Missing attribution remains unresolved evidence rather than becoming a lesson.

Purpose:

```text
review portfolio exposure
review account/risk budget
review drawdown and lock state
review instrument concentration/correlation
recommend risk posture changes
```

This layer does not place trades. It produces an hourly review, checks whether observed behavior matches the active plan, and may perform Tier 0 repairs only inside Hermes-owned state (for example rebuilding an index, retrying a receipt, quarantining a malformed memory item, or pausing its own unhealthy job). Risk-cap, account, Glitch-policy, deployment, and execution changes remain proposals requiring the authority defined in document 12.

## Layer 4 — 300-minute portfolio planning

Mode: Hermes cron, LLM-driven or script-assisted.

Cadence: every 300 minutes when new hourly evidence exists.

Purpose:

```text
read current portfolio state and authoritative outcomes
compare progress with today's targets and risk budget
adapt target ranges and risk allocation to regime and evidence
identify what Hermes should test or avoid during the next horizon
write a versioned plan without changing Glitch policy or execution settings
```

The plan guides later cognition; it is not an execution gate. Any authority-changing recommendation remains pending until explicitly approved.

## Layer 5 — daily trader journal

Mode: Hermes cron.

Cadence: catch up every completed Apex session that contains unjournaled episodes. A missed invocation is processed later exactly once rather than depending on an exact wall-clock minute.

Purpose:

```text
summarize the day
identify repeated mistakes
summarize wins/losses by regime and setup
produce a concise trader-style journal
update episodic and semantic knowledge with provenance
set evidence-backed targets and questions for tomorrow
prepare candidate lessons for review
```

The daily journal is how Hermes learns like a trader without rewriting Glitch policy silently. It preserves uncertainty and contradictory evidence instead of forcing every observation into a rule. Malformed or schema-invalid learning output receives one bounded repair attempt using the exact template and validation error; a second failure leaves the evidence unprocessed for a later cycle.

## Multi-instrument support

Snapshots must support multiple instruments from day one, even if v1 runs one instrument.

The operator should reason per instrument. The hourly portfolio layer should reason across instruments.

Required capabilities:

```text
per-instrument features
per-instrument regime/direction state
cross-instrument exposure
correlation/concentration checks
instrument ranking/prioritization
```

## Historical exporter and replay corpus

Build an exporter that emits the exact same schema as the live snapshot.

Target corpus:

```text
2 years of historical market data
same snapshot schema as live
same normalization versions as live when possible
same feature names as live
replay markers that distinguish historical from live
```

Uses:

```text
offline replay
backtesting
pattern mining
feature mining
regime discovery
operator prompt/eval fixtures
model comparison
```

This is where the system discovers which indicator combinations and feature permutations actually matter.

## Pattern mining goals

Mine for:

```text
indicator combinations
feature redundancy
regime labels
trend/range/reversal conditions
volatility regimes
directional bias conditions
setup quality by market state
failure modes by market state
```

The output is not direct live authority. It becomes candidate archetypes, lessons, and policy updates.

## Memory and skill layer

Preserve Hermes's native memory, sessions, planning, and upkeep capabilities. Add Glitch-specific skills for durable operator knowledge:

```text
wiki memory      stable project doctrine, model/cadence policy, glossary
skills           reusable procedures and checks
journal          trader-style reflection and outcomes
hypotheses       versioned setups, patterns, and competing explanations
policy           promoted rules only
```

Do not store private/raw dumps or unreviewed lessons as active policy.

## Cadence map

```text
1 minute   central canonical ingest + packet assembly (client harness mirrors it)
5 minutes  one central Hermes recommendation + client poll
15 minutes Hermes debriefs newly completed master outcomes
1 hour     Hermes portfolio/risk review
300 minutes Hermes portfolio targets and risk planning
Daily      Hermes lessons, memory upkeep, and tomorrow targets
```

## Ponytail constraints

Activate in stages. Do first:

```text
one snapshot schema
one live exporter
one historical exporter using the same schema
one 5-minute operator job
one script-only packet check
one supervised Hermes profile/memory system with isolated `trading` inference sessions and native capabilities intact
the core first, followed by evidence-gated debrief, supervision, planning, and daily learning
```

Do not start with:

```text
per-client AI runtime
custom scheduler
streaming LLM loop
big feature store
unreviewed auto-policy updates
separate central/client schemas
```

Activate the 15-minute debrief, hourly supervision, 300-minute planning, and daily learning only from attributable master outcomes. Plans and advisory guidance become usable immediately. A cognitive overlay is first staged as a proposal with no trading influence; a later independent supervisory decision may activate it only from later comparable evidence after contradiction review, and subsequent evidence must continue, promote, revise, or roll it back. The complete cognitive and authority map is `12_hermes_trading_skills_and_knowledge.md`.

## Optional Kanban learning layer (deferred)

Kanban is useful for slow, inspectable learning work—not for the five-minute execution path and not as a second trade ledger.

```text
Glitch ledger       authoritative positions, orders, intents, rejects, fills, brackets, outcomes
Hermes memory       hypotheses, distilled lessons, operator context
Hermes Kanban       bounded review assignments with evidence links and completion criteria
```

After the direct core is validated, a small projector may create an unassigned informational card for an unusual closed trade or anomaly. Assignment is what authorizes model work, so ordinary positions must not spawn agents or model calls. A practical first assigned card is one daily review that compares closed outcomes, updates a hypothesis with evidence, and records what should be tested next. Hourly or 300-minute cards should exist only when a material exception or decision needs attention.

The board must never control trading mode, replication, flattening, order submission, stops, or targets. It links back to immutable Glitch IDs rather than copying market and order truth into `kanban.db`. Only one Hermes gateway may own the Kanban dispatcher. Model routing, automatic card creation, per-position tickets, and dashboard integration are expansion candidates after the core produces trustworthy data; none are required for initial deployment.
