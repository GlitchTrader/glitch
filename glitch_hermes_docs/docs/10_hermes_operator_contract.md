# 10 — Hermes Operator Contract

## Status

Operator contract for the direct Glitch-to-Hermes filesystem exchange. This document defines authority and execution boundaries. The cognitive-loop, model-routing, skill, memory, and staged-activation canon is `12_hermes_trading_skills_and_knowledge.md`.

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

The source implementation is not activation authority. Installation, cron creation, paper execution, and live/eval authority remain separate explicit actions.

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
- unconstrained probabilistic synthesis of market evidence;
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

### Dynamic groups and execution routes

One persistent Hermes profile (`glitch`) reasons over the groups present in each Glitch-owned decision packet. Every decision carries the packet's route label in `operator_profile` plus its master `account`; Glitch's policy binds that pair and rejects mismatches before execution. Route labels are execution identities, not separate Hermes agents or fixed trading personalities. Hermes never emits follower intents.

Existing labels such as `glitch-aggressive`, `glitch-conservative`, or `glitch-stay-revert` may remain as compatibility/discovery labels in old fixtures. They must not constrain cognition unless the current Glitch packet explicitly supplies a versioned experiment mandate.

Glitch resolves the enabled account group from `AccountGroups.tsv` and owns all fan-out behavior:

- the route-bound master uses multiplier 1;
- each enabled follower uses its current Glitch-configured ratio;
- the AI executor submits only the route-bound master; the existing Glitch copy engine owns follower entry fan-out;
- after each fill, Glitch creates a native GTC OCO stop/target on the master and independently on every follower at its scaled quantity;
- a master AI exit is copied through the same replication path, and follower protection is cancelled after the copied close so no orphan order can reverse an account;
- per-account and aggregate group risk use the actual scaled quantities;
- unknown routes, mismatched masters, malformed groups, duplicate accounts, unallowlisted members, and non-integral quantity mappings fail closed;
- startup catch-up clones the master's active AI bracket or refuses to create an unprotected follower position;
- completed-outcome learning waits for master plus every enabled follower to close.

Group composition and ratios are dynamic Glitch state. Results must therefore be compared using master-account and per-contract expectancy, MAE/MFE, drawdown, and risk-normalized returns—not raw aggregate group PnL.

## Target 5-minute loop

Cadence:

```text
Glitch seals the x0/x5 closed decision window; Hermes consumes it at x1/x6 so the packet exists before inference starts.
```

Nominal loop:

```text
1. Glitch snapshots deterministic state.
2. Hermes cron loads the operator brief and current snapshot.
3. Hermes reads recent outcomes, lessons, hypotheses, relevant evidence, and risk posture.
4. Hermes emits exactly one machine-readable intent per instrument per cycle.
5. Glitch validates the intent through deterministic checks.
6. Glitch either rejects with reason codes or executes with NT-held protective brackets.
7. Glitch journals the result.
8. Glitch's authoritative journals remain in `GlitchData`; the next Hermes cycle reads their bounded tail directly.
9. Hermes uses only matching `operator_profile` and master-account records as its own outcome history.
```

No Hermes output is actionable until Glitch accepts it.

The broader snapshot, ingestion, historical replay, portfolio/risk, and learning cadence is defined in `11_snapshot_ingestion_learning_pipeline.md`.

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
  relevant archetypes and hypotheses as advisory evidence
  known failure conditions and uncertainty
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
optional quantity_tp1
optional take_profit_3
optional stop_loss_3
optional quantity_tp2
confidence
bounded_reason_codes
```

Bracket mandate:

- `ENTER_LONG` / `ENTER_SHORT` require `stop_loss` and `take_profit_1`.
- TP2/SL2 and TP3/SL3 are optional and only valid when quantity supports positive leg splits.
- Quantity must be one of Glitch's supplied valid master quantities. Glitch derives that list dynamically from every enabled account's current prop-rule ceiling, open MNQ exposure, and configured follower ratio; the maximum-exposure account limits the group.
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
11. risk per trade computable from entry/SL?
12. risk within per-trade and daily budget?
13. bracket sane and tick-rounded?
14. session/news/lockout clear?
15. existing compliance engine passes?
16. trading is ON at the final submit boundary?
```

Trade frequency, cooldown, and minimum reward/risk are Hermes decisions, not deterministic firewall gates. `/trade_mode` is the single activation command for the account scope selected in Glitch AI; no separate mode, kill-switch, or executor-arm ritual exists.

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

Hermes does not duplicate or rewrite Glitch's authoritative execution journal. The direct packet contains bounded Glitch-owned journal tails; stable IDs join those facts to Hermes-owned decisions, receipts, reviews, plans, lessons, and hypotheses. Each physical stream has one writer, and corrections are append-only linked events.

Lesson storage should be structured, versioned, and promotable:

```text
raw event -> classified outcome -> candidate lesson -> reviewed/promotion decision -> active archetype/policy update
```

Do not silently mutate the operator policy from one trade. Policy changes require a versioned candidate and evaluation record.

## Hermes runtime placement

Product decision: one persistent Hermes profile/session runs under a supervised gateway on the central VPS. Glitch clients do not install Hermes. They poll the authenticated recommendation API once per five-minute window, then apply local portfolio, compliance, group, sizing, and execution truth. Codex is never a scheduler, bridge, or operator.

The direct filesystem exchange below remains the internal stabilization harness. It must stay contract-compatible with the future network transport so centralization changes transport, not cognition or execution semantics.

Single-writer exchange:

```text
GlitchData/hermes/exchange/glitch/*  Glitch writes, Hermes reads
GlitchData/hermes/exchange/hermes/* Hermes writes, Glitch/bridge reads
```

In the harness, Glitch writes one immutable minute frame after matching market and portfolio snapshots exist. At `xx:x0` and `xx:x5`, five consecutive frames become one immutable decision packet. Hermes native cron checks for a new packet without an LLM call, resumes only the named `trading` session, and delivers strict intents through Glitch's authenticated localhost receiver. Delivery retries reuse the same intent IDs; completed receipts prevent replay. The gateway must use Hermes native supervision, not an orphan child process.

Full cognitive runtime map (only the core loop is enabled during initial validation):

```text
snapshot_sanity        script-only check, no LLM
suggest_trade          5m, Luna/medium, one strict batch for packet-defined groups
portfolio_supervision  hourly, Sol/high, risk/performance review + bounded self-heal
portfolio_planning     6-hour, Sol/high, targets and risk allocation plan
daily_learning         post-session, Sol/high, lessons + tomorrow targets + memory upkeep
policy_store           versioned files/db records, no silent mutation
lesson_store           Glitch-journaled outcomes and reviewed lessons
```

`suggest_trade` cron job shape:

```text
schedule: every 5m
mode: LLM-driven Hermes cron
input: one Glitch-owned five-frame packet + bounded authoritative journal + native Hermes memory
output: one strict JSON decision per configured route-bound group
transport today: Hermes-owned harness worker -> authenticated Glitch localhost intent receiver
transport target: central recommendation store/API -> authenticated five-minute client poll -> local Glitch firewall
```

The model is invoked once per canonical recommendation cycle, not once per customer. Deterministic services monitor ingestion, freshness, delivery, and stuck handoffs without an LLM.

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
6. glitch_hermes_docs/docs/11_snapshot_ingestion_learning_pipeline.md
7. glitch_hermes_docs/schemas/intent.v2.schema.json
8. relevant AddOn source files
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

### Staged implementation and activation

Build in this order:

```text
1. Validate the direct snapshot/packet and receipt contracts in source.
2. Install and orient one persistent Hermes profile while preserving native skills, memory, and sessions.
3. Enable only the 5-minute paper core and validate packet-to-journal continuity.
4. Add hourly supervision only after core evidence is trustworthy.
5. Add six-hour planning, then daily learning, as separate observable stages.
6. Consider eval/live authority only after paper gates and explicit operator approval.
```

### What builders must not do

Never:

- give Hermes direct broker/NinjaTrader order authority;
- bypass `GlitchAiRiskFirewall` or existing compliance checks;
- make free-form LLM text actionable;
- execute from stale snapshots;
- accept an entry without SL + TP1;
- let AI widen stops or manage losses mid-flight;
- bypass the existing replication engine by having Hermes or the AI executor trade followers directly;
- treat Hermes memory as trade truth when Glitch journal/source artifacts disagree.

## Contracts to finish before broader activation

```text
1. freeze the packet and outcome schema versions after source validation;
2. define durable hourly review, six-hour plan, and daily journal schemas;
3. implement the planned Glitch overlay skills listed in document 12;
4. define model-outage and no-silent-downgrade behavior in runtime tests;
5. define paper-mode learning and performance metrics;
6. add observability for packets, calls, intents, rejects, outcomes, and memory upkeep.
```

## Session layout

One `glitch` profile is one agent identity and one native memory system. It has two named sibling sessions, not two agents:

```text
chat      internal maintainer/supervision session; never exposed in the Glitch client UI
trading   persistent JSON-only decision history; resumed only by the 5-minute job
```

The core worker uses `--resume trading`, never `--continue`. This removes the ambiguous “latest session” dependency while preserving one profile, shared native skills, memory, and filesystem. The chat session may inspect status and accept human slash commands while trading continues independently.

The product UI exposes Feed, not the internal maintainer session. The local chat/slash-command surface remains a harness and maintainer tool, not a customer dependency.

Slash commands are deterministic plugin handlers, not prompts to the model. They call Glitch's authenticated localhost control surface. Glitch persists trading pause state, performs replication/flatten through its existing UI execution paths, reflects state in the header, and rejects new entry intents while paused. `EXIT`, `HOLD`, and `NOTHING` remain admissible so pause does not trap an existing position.

Activation remains two-step: install the profile/session/plugin layer, then separately create the native cron job. Installation never starts trading.

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
