# 10 — Hermes Operator Contract

## Status

Design doctrine / operator contract. This document defines how the future Hermes-side Glitch operator must behave before any `addon-ai-bridge` or live intent path is implemented.

## Where this belongs

Canonical repo location:

```text
Glitch-Platform/glitch_hermes_docs/docs/10_hermes_operator_contract.md
```

Builders should find it through:

```text
Glitch-Platform/docs/ai-program/roadmap.md
Glitch-Platform/glitch_hermes_docs/README.md
Glitch-Platform/glitch_hermes_docs/wiki_memory/07_LLM_WIKI_INDEX.md
ABKB: projects/glitch/project_profile.md and projects/glitch/sources.md
```

When the bridge is implemented, this contract should be mirrored into the eventual AddOn-side AI bridge docs, likely near:

```text
ninjatrader/Glitch/Docs/addon-ai-bridge.md
```

Do not treat this as a request to make Hermes the operator today. It is the doctrine for the operator runtime that will later be implemented.

## Prime responsibility split

```text
Glitch = deterministic machine.
Hermes = probabilistic operator.
```

Glitch owns:

- NinjaTrader process access;
- market/account/position source-of-truth snapshots;
- prop-firm rule enforcement;
- compliance and risk validation;
- order submission and bracket/OCO management;
- journaling of validation, execution, fills, rejects, risk locks, and account outcomes;
- kill switches and operator override.

Hermes owns:

- scheduled analysis;
- probabilistic pattern interpretation;
- trade archetype selection;
- lesson synthesis from past trades;
- candidate intent generation;
- daily/weekly learning memos;
- policy/prompt/archetype versioning;
- never order execution.

Invariant:

```text
Hermes proposes. Glitch validates, executes, journals, and protects.
```

Hermes must never receive NinjaTrader credentials, account credentials, broker credentials, or any direct order API that bypasses Glitch.

## Target 5-minute loop

Cadence:

```text
Every 5 minutes, aligned to the closed decision window.
```

Nominal loop:

```text
1. Glitch snapshots deterministic state.
2. Hermes cron loads the operator brief and current snapshot.
3. Hermes reads recent outcomes, lessons, archetypes, and risk posture.
4. Hermes emits exactly one machine-readable intent per instrument per cycle.
5. Glitch validates the intent through deterministic checks.
6. Glitch either rejects with reason codes or executes with NT-held protective brackets.
7. Glitch journals the result.
8. Hermes learns only from Glitch-journaled outcomes.
```

No Hermes output is actionable until Glitch accepts it.

## Required Glitch-to-Hermes input bundle

The Hermes operator prompt/job must receive a bounded, deterministic input bundle, not ad-hoc chat context.

Minimum bundle:

```text
operator_brief:
  role: Glitch operator
  allowed authority: propose-only
  forbidden authority: execute, bypass firewall, widen stop, alter prop-firm rules

market_snapshot:
  schema_version
  snapshot_id
  snapshot_hash
  created_utc
  instrument
  current_price
  session context
  normalized timeframe readings
  order-flow context when available
  stale-data flags

portfolio_snapshot:
  account
  account status
  equity/balance
  daily PnL
  open PnL
  position state
  working-order state
  drawdown/buffer remaining
  lock state

prop_firm_rules:
  firm
  tier/account size
  max contracts
  daily loss
  max drawdown
  trading-day/session restrictions
  consistency/trading-day constraints when applicable

recent_trade_memory:
  recent accepted intents
  recent rejected intents and reason codes
  fills/exits
  PnL/MAE/MFE/duration
  current streak/churn/session quality

lessons_and_archetypes:
  allowed trade archetypes
  suppressed conditions
  regime-specific notes
  prompt/policy version
  archetype library version
```

All fields must be versioned or explicitly optional. Missing critical state should force `NOTHING`, not inference.

## Required Hermes-to-Glitch output

Hermes must output strict machine-readable intent only. Free-form prose is non-authoritative.

Allowed M0-style actions:

```text
ENTER_LONG   // Buy
ENTER_SHORT  // Sell
HOLD         // keep existing position, no change
EXIT         // request flat/reduce risk now
NOTHING      // stay flat / no action
```

Every entry intent must include:

```text
schema_version
intent_id
created_utc
snapshot_hash
instrument
account
operator_version
model_version
prompt_version
policy_version
archetype_id or null
action
quantity
order_type
stop_loss
take_profit_1
optional take_profit_2
optional stop_loss_2
confidence
bounded_reason_codes
```

Bracket mandate:

- `ENTER_LONG` / `ENTER_SHORT` require `stop_loss` and `take_profit_1`.
- TP2/SL2 are optional and only valid when quantity supports a runner.
- A naked entry must be impossible.
- NT/Glitch hold the protective bracket.
- Hermes may not manage a loss mid-flight and may never widen a stop.

`HOLD`, `EXIT`, and `NOTHING` must still include the snapshot hash and reason codes so Glitch can journal why the cycle did or did not trade.

## Deterministic Glitch firewall requirements

Glitch must reject before order creation if any check fails. Minimum check chain:

```text
1. operator/AI feature enabled?
2. local bridge authenticated?
3. schema valid and version allowed?
4. snapshot fresh and hash matches?
5. intent_id unseen or idempotent retry?
6. instrument allowlisted?
7. account allowlisted?
8. prop-firm rules loaded and fresh?
9. current account/position state unambiguous?
10. no forbidden position conflict?
11. cooldown and daily trade caps pass?
12. risk per trade computable from entry/SL?
13. risk within per-trade and daily budget?
14. bracket sane and tick-rounded?
15. session/news/lockout clear?
16. existing compliance engine passes?
17. kill switch still off at final submit boundary?
```

Failure result:

```text
accepted=false
status=REJECTED
reason_code=<stable code>
no order exists
journal record written
```

Success result:

```text
accepted=true
status=ACCEPTED|PAPER_ACCEPTED|SUBMITTED
trade_id or paper_trade_id
journal record written
```

## Learning loop boundaries

Hermes may learn from:

- Glitch snapshots;
- Glitch journal records;
- rejected intent reason codes;
- filled order outcomes;
- MAE/MFE/duration/PnL after commission truth is fixed;
- shadow trades and backtest replays;
- operator-approved lessons.

Hermes must not learn from:

- unverified chat claims as trade truth;
- gross PnL when net/commission-corrected PnL is required;
- partial logs that Glitch marks stale or ambiguous;
- any execution not journaled by Glitch;
- hindsight-only labels without timestamped pre-trade snapshots.

Lesson storage should be structured, versioned, and promotable:

```text
raw event -> classified outcome -> candidate lesson -> reviewed/promotion decision -> active archetype/policy update
```

Do not silently mutate the operator policy from one trade. Policy changes require a versioned candidate and evaluation record.

## Hermes runtime placement

Runtime decision: use Hermes native cron first. Do not build an always-on Hermes daemon until cron fails a measured requirement.

Minimum runtime shape:

```text
snapshot_sanity   script-only cron, every 1-5m, no LLM
suggest_trade     LLM cron, every 5m, one strict intent or NOTHING
daily_learning    post-session cron, candidate lessons only
policy_store      versioned files/db records, no silent mutation
lesson_store      Glitch-journaled outcomes and reviewed lessons
```

`suggest_trade` cron job shape:

```text
schedule: every 5m
mode: LLM-driven Hermes cron
input: latest Glitch snapshot + recent journal + active policy/archetypes
output: one strict JSON intent or NOTHING
transport: local authenticated bridge/file/API consumed by Glitch
```

The LLM is on-demand, not resident. Deterministic scripts may monitor freshness and stuck handoffs without an LLM. A small bridge daemon is deferred until a real need appears, such as sub-minute event handling, file-watch debouncing, queue retries, or persistent local API connectivity.

Hermes cron may be used for analysis and intent generation. It must not be the execution scheduler. Execution timing and final validation belong to Glitch.

## Builder instructions

### Codex / Cursor / Claude starting point

Read in order:

```text
1. AGENTS.md
2. docs/ai-program/roadmap.md
3. glitch_hermes_docs/README.md
4. glitch_hermes_docs/docs/09_intent_contract_v2_brackets.md
5. glitch_hermes_docs/docs/10_hermes_operator_contract.md
6. glitch_hermes_docs/schemas/intent.v2.schema.json
7. relevant AddOn source files
```

Relevant AddOn anchors:

```text
ninjatrader/Glitch/AddOns/GlitchAddOn/Services/GlitchShellBridge.cs
ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Persistence/GlitchStateStore.cs
ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Risk/GlitchComplianceEngine.cs
ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Trading/GlitchReplicationEngine.cs
ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Insights/GlitchTradeLedgerService.cs
ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Insights/GlitchTradeInsightsService.cs
ninjatrader/Glitch/AddOns/GlitchAddOn/UI/Analytics/GlitchAnalyticsFeedBus.cs
ninjatrader/Glitch/AddOns/GlitchAddOn/UI/Analytics/GlitchAnalyticsLogic.cs
ninjatrader/Glitch/Indicators/glitch/GlitchAnalyticsBridge.cs
```

### What builders should implement later

Build in this order:

```text
1. Read-only snapshot export with stable schema.
2. Result/journal export with stable schema.
3. Hermes ingest job that stores snapshots and detects staleness.
4. Hermes suggest_trade cron that can only emit strict JSON / NOTHING.
5. Glitch paper intent endpoint with deterministic rejects.
6. Journal bridge from intent -> validation -> outcome.
7. Sim101 executor with mandatory brackets.
8. Eval-account executor only after paper gates and explicit operator approval.
```

### What builders must not do

Never:

- give Hermes direct broker/NinjaTrader order authority;
- bypass `GlitchAiRiskFirewall` or existing compliance checks;
- make free-form LLM text actionable;
- execute from stale snapshots;
- accept an entry without SL + TP1;
- let AI widen stops or manage losses mid-flight;
- blend the AI path into replication before the single-account path is stable;
- treat Hermes memory as trade truth when Glitch journal/source artifacts disagree.

## Open contracts still missing

Before implementation, define or finalize:

```text
1. snapshot schema v1 final fields and freshness semantics;
2. intent response schema with stable reject reason codes;
3. journal/outcome schema for learning;
4. local transport choice: localhost API vs file drop vs queue;
5. bridge authentication and token storage UX;
6. operator policy/archetype storage format;
7. promotion gate for lessons and policy updates;
8. cron failure behavior: no snapshot, stale snapshot, invalid JSON, model outage;
9. paper-mode acceptance metrics;
10. dashboard/observability for rejects, intents, and learning state.
```

## Completion criterion for this contract

This contract is satisfied only when future builders can answer:

```text
What does Glitch expose?
What does Hermes read?
What exactly may Hermes emit?
What must Glitch reject?
Where are outcomes journaled?
How does Hermes learn without bypassing deterministic safety?
```
