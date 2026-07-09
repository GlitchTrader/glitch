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

Mode: Hermes LLM cron.

Cadence: every 5 minutes, aligned to the closed decision window.

Purpose:

```text
read latest valid snapshot
read recent Glitch-journaled outcomes
read active rules/archetypes/lessons
emit one strict JSON intent per instrument or NOTHING
```

Allowed current actions:

```text
ENTER_LONG
ENTER_SHORT
HOLD
EXIT
NOTHING
```

Output stays small. The cost risk is input/context bloat, not the intent JSON.

## Layer 3 — hourly portfolio and risk review

Mode: Hermes cron, preferably script-assisted with bounded LLM reasoning only when needed.

Cadence: every 1 hour.

Purpose:

```text
review portfolio exposure
review account/risk budget
review drawdown and lock state
review instrument concentration/correlation
recommend risk posture changes
```

This layer does not place trades. It produces reviewable recommendations or policy candidates.

## Layer 4 — six-hour learning pass

Mode: Hermes cron, LLM-driven or script-assisted.

Cadence: every 6 hours during active experimentation.

Purpose:

```text
read accepted/rejected intents
read journaled outcomes
compare expected vs actual behavior
rank mistakes and useful setups
produce candidate lesson/archetype changes
```

Candidate lessons are not active policy until promoted through a versioned review step.

## Layer 5 — daily trader journal

Mode: Hermes cron.

Cadence: daily after the trading session.

Purpose:

```text
summarize the day
identify repeated mistakes
summarize wins/losses by regime and setup
produce a concise trader-style journal
prepare candidate lessons for review
```

The daily journal is how Hermes learns like a trader without rewriting live policy silently.

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

Use wiki-style memory and skills for durable operator knowledge:

```text
wiki memory      stable project doctrine, model/cadence policy, glossary
skills           reusable procedures and checks
journal          trader-style reflection and outcomes
archetypes       versioned setup definitions
policy           promoted rules only
```

Do not store private/raw dumps or unreviewed lessons as active policy.

## Cadence map

```text
1 minute   Glitch snapshot export + script-only snapshot_sanity
5 minutes  Hermes suggest_trade LLM cron
1 hour     Hermes portfolio/risk review
6 hours    Hermes learning pass
Daily      Hermes trader journal / summary
```

## Ponytail constraints

Do first:

```text
one snapshot schema
one live exporter
one historical exporter using the same schema
one 5-minute operator job
one script-only watchdog
one daily journal
```

Do not start with:

```text
always-on AI daemon
custom scheduler
streaming LLM loop
big feature store
unreviewed auto-policy updates
separate AI trading platform
```

Add those only when the simple cron/exporter path fails a measured need.
