# 12 — Hermes Cognitive Operating Map

**Status:** canonical map for the persistent `glitch` Hermes profile.
**Scope:** cognition, model routing, skills, memory, ledger upkeep, self-healing, and staged activation.
**Authority:** contract 10 owns safety and execution boundaries; contract 11 owns data and cadence; this document owns how Hermes thinks and learns inside those boundaries.

## 1. Design principle

```text
Protect the machine. Enable the brain.
```

Glitch enforces hard execution facts: identity, account/group scope, prop-firm limits, current positions and orders, intent schema, idempotency, bracket protection, kill switches, and order execution.

Hermes remains free to interpret context probabilistically: direction, regime, opportunity, trade frequency, risk posture inside the allowed envelope, target selection, portfolio allocation, novel patterns, and whether prior lessons still apply.

Archetypes, mined patterns, adversarial bull/bear cases, and historical statistics are evidence. They are not entry gates, whitelists, or substitutes for current judgment. `no_archetype_match` is never by itself a reason to return `NOTHING`.

## 2. One persistent agent, four cognitive loops

All loops belong to the same `glitch` profile and share native Hermes memory, bounded Glitch evidence, and the Hermes-owned knowledge base. Every LLM-driven scheduled call uses an isolated session tagged for its trading lane and receives explicit bounded continuity. They are different jobs, not different agents or personalities.

| Loop | Cadence | Model baseline | Primary question | Output authority |
|---|---:|---|---|---|
| Core decision | 5m while flat; 1m while a scoped master is positioned; one-cycle directives | `gpt-5.6-luna`, medium | What should each configured group do now? | Strict intent batch; Glitch may reject or execute |
| Portfolio supervision | hourly | `gpt-5.6-sol`, high | Is exposure, performance, risk posture, or system health drifting? | Review, bounded self-heal, and plan recommendations; no order |
| Trade debrief | every 15 minutes when new outcomes exist | `gpt-5.6-sol`, high | Why did each master trade enter and exit; what did geometry, quantity, and management teach? | Append-only master episode; no order authority |
| Portfolio planning | every 300 minutes with new reviews | `gpt-5.6-sol`, high | Given today’s progress and prop rules, what hypotheses, sizing, geometry, and management posture should guide the next block? | Active Hermes plan inside Glitch’s hard limits; no Glitch-policy mutation |
| Daily learning | catch up each completed Apex session containing unjournaled episodes | `gpt-5.6-sol`, high | What was learned, what should change tomorrow, and which hypotheses deserve testing? | Journal, memory updates, hypotheses, and tomorrow plan; no live-policy promotion |

Model IDs are explicit defaults, not permanent doctrine. Each job records provider, model, reasoning effort, prompt version, skill versions, token use, and latency so model routing can later be optimized from evidence. The core loop must never silently downgrade to a weaker fallback model. A failed model call produces no new intent. Supervisory loops may defer until their assigned model is available.

No-model work remains script-only: packet completeness, freshness, duplicate detection, delivery retry with the same intent IDs, stale-lock cleanup, schema checks, and derived-index rebuilding.

## 3. Loop contracts

### Core decision loop

Input:

- one immutable five-frame Glitch packet;
- latest portfolio, positions, working orders, group ratios, and hard risk state;
- current prop-firm rules and account phase;
- compact master-only position-building state: account size/equity, liquidation buffer and drawdown headroom, current quantity/average price, contract capacity and valid quantities, complete native protection, and MNQ point/tick value;
- the active Hermes portfolio plan when that deferred loop exists;
- only recent relevant Glitch outcomes and lessons;
- native session and episodic memory.

Reasoning freedom:

- independently evaluate bullish, bearish, stay-flat, stay-positioned, and exit cases;
- use or reject known patterns;
- create a falsifiable discretionary hypothesis;
- choose risk, quantity, native target legs, reserved capacity, later independently protected additions, and management geometry inside Glitch limits;
- vary posture by regime and by configured group.

For increasing exposure, Hermes compares a single protected tranche, TP1/TP2/TP3 native entry legs, reserving capacity, a later same-direction addition, and unchanged exposure. An addition may be at a favorable or adverse price when evidence supports the thesis, but price movement alone never creates a grid, martingale, or loss-recovery rule. Current acceptance, rejection, structure, and excursion override stale forecasts. Glitch independently rejects an Apex Legacy evaluation entry when complete protected downside is ambiguous or reaches the authoritative liquidation buffer; this is an account-survival boundary, not a preferred quantity, percentage budget, or strategy.

Output: exactly one `glitch.intent.batch.v1`, containing one ordered `glitch.intent.v3` decision per route-bound group. Scheduled output is JSON only. Entries carry independent native protection; management names selected stable Glitch leg IDs. Glitch remains the only executor.

### Hourly portfolio supervision loop

Input: the last hour of packets, decisions, rejections, executions, open risk, realized/unrealized performance, delivery health, model usage, and the active plan.

Output: `glitch.hermes.hourly_review.v1` containing:

- portfolio and per-group state;
- what is working, failing, or statistically unknowable;
- exposure and drawdown posture;
- anomalies such as churn, repeated rejection, stale data, delivery failure, or bracket inconsistency;
- automatic Tier-0 repairs performed;
- proposed changes to the current Hermes plan;
- unresolved items requiring operator attention.

It may reduce Hermes’s intended exposure or pause proposals. It may not raise Glitch caps, alter account groups, enable execution, or place an order.

### 300-minute portfolio planning loop

Input: current prop-firm phase and rules, daily and trailing PnL, drawdown buffer, remaining session, market regime, group-level performance, hourly reviews, and recent lessons.

Output: `glitch.hermes.portfolio_plan.v1` containing:

- objectives for the next 300 minutes and the trading day;
- per-group purpose and risk allocation;
- preferred participation posture by market regime;
- profit preservation, loss containment, and stop-trading conditions;
- evaluation/PA progress targets where relevant;
- hypotheses the core loop should actively test on Sim;
- evidence that should trigger plan revision.

The newest valid plan becomes active Hermes context automatically. It may narrow risk or redirect attention inside Glitch’s current envelope. It cannot widen hard limits or rewrite prop-firm rules.

### Daily learning and tomorrow loop

Input: the complete session ledger and immutable pre-decision packets, selected and available quantities, pre-entry exposure and protection, entry/add classification, every target leg and stop, planned downside by leg and in total, closed outcomes, per-contract and planned-risk-normalized MAE/MFE/PnL, management decisions, actual exits, rejected intents, missed or avoided opportunities when measurable, plan changes, market regimes, and system health.

Output: `glitch.hermes.daily_journal.v1` containing:

- concise trader journal;
- performance and process attribution by group and regime;
- lessons with evidence links and confidence;
- mistakes, good decisions, and unresolved ambiguity;
- candidate knowledge updates;
- tomorrow’s targets, risk posture, experiments, and invalidation conditions.

Verified observations and episodic lessons may enter Hermes memory automatically with provenance. Hermes must assess whether quantity was evidence-based or habitual and whether native legs, reserved capacity, or a later addition were plausible, while preserving uncertainty instead of inventing hindsight rules. New strategy hypotheses may be tested on Sim. Hard Glitch policy, prop rules, account mappings, and any live/eval promotion remain outside automatic learning authority.

## 4. Skill architecture

The Glitch skills are an overlay on Hermes’s native skills, memory, sessions, search, file, terminal, planning, and upkeep capabilities. Installation must not prune or replace native Hermes capabilities.

Existing Glitch overlay:

| Skill | Purpose |
|---|---|
| `glitch-observe-market` | Read packet and market structure without forcing a named setup |
| `glitch-assess-risk` | Understand portfolio and trade risk inside Glitch limits |
| `glitch-form-thesis` | Form falsifiable discretionary or pattern-supported hypotheses |
| `glitch-build-intent` | Convert a chosen decision into the strict protected intent contract |
| `glitch-submit-intent` | Keep interactive chat on directives; only the worker may validate and deliver intents |
| `glitch-review-outcomes` | Attribute Glitch-recorded outcomes and generate lessons |
| `glitch-self-learning` | Promote attributable outcomes into append-only episodic and durable lessons without turning memory into truth |
| `glitch-self-heal` | Reconcile Hermes-owned state to Glitch truth, append the correction, and resume safe operation |
| `glitch-supervisor-ledger` | Maintain append-only observations, guidance, lessons, and build requests |
| `glitch-learning-loop` | Debrief master trades, supervise hourly, plan every 300 minutes, update daily memory, and evaluate one reversible cognitive overlay |
| `glitch-escalate-to-codex` | Propose bounded source work without scheduling or operating Codex |

`glitch-learning-loop` is the intentionally small supervisory overlay. It reuses
the existing outcome, self-learning, self-heal, and supervisor-ledger skills
instead of splitting read, write, planning, and upkeep into six thin wrappers.

Skills teach procedures and expose resources. They do not encode a deterministic trading strategy, require an archetype match, force a trade quota, or silently become policy.

## 5. Ledger and knowledge topology

Source order:

```text
live Glitch state and Glitch-owned events
→ operator-confirmed facts
→ Hermes plans and reviews
→ structured Hermes knowledge
→ native session memory
→ inference
```

Glitch owns immutable trading truth:

- market and portfolio snapshots;
- account groups and ratios;
- prop-firm rules and hard policy;
- received intents and decisions;
- executions, fills, brackets, rejections, and outcomes;
- trade ledger and operator journal.

Hermes owns interpretation and learning:

```text
GlitchData/hermes/exchange/hermes/
  outbox/                 strict decisions awaiting delivery
  outbox-context/         packet-bound operator-directive identity for crash-safe consumption
  model-attempts/          one durable inference attempt per packet, including terminal failure evidence
  receipts/               delivery evidence
  events/cycles.jsonl     core-loop events
  reviews/hourly/         hourly supervision outputs (planned)
  plans/current.json      active cognitive plan (planned)
  plans/history/          immutable prior plans (planned)
  journal/daily/          daily journals (planned)
  knowledge/observations/ evidence-linked facts (planned)
  knowledge/hypotheses/   testable strategy/risk ideas (planned)
  knowledge/lessons/      durable, provenance-linked lessons (planned)
  health/                 self-heal and runtime-health evidence (planned)
```

One writer owns every physical stream. Records join through `packet_id`, `snapshot_hash`, `intent_id`, `trade_id`, `route_id`, `account`, model version, prompt version, and skill versions. Hermes never edits Glitch-owned records; corrections are new linked events.

## 6. Memory lifecycle

| Layer | Contents | Update rule |
|---|---|---|
| Working | current packet, open positions, current plan | replaced as state changes |
| Episodic | decisions, trades, rejects, outcomes, operator conversations | append with Glitch evidence links |
| Semantic | regimes, prop-rule interpretations, recurring market behavior | update with provenance and confidence |
| Procedural | skills and output contracts | versioned source change |
| Strategic | active targets, risk posture, experiment priorities | replaced by 300-minute/daily plan; history retained |
| Reflective | lessons, contradictions, model/process failures | append, merge duplicates, never erase contrary evidence |

Memory is context, not execution truth. Hermes may revise beliefs freely as evidence changes. It must preserve the provenance and uncertainty of those revisions.

## 7. Self-heal authority

Tier 0 — automatic and evidence-recorded:

- retry failed delivery using the same intent IDs;
- clear a demonstrably stale Hermes-owned lock;
- rebuild derived indexes from immutable ledgers;
- quarantine malformed Hermes output;
- compact or deduplicate Hermes memory without deleting source events;
- isolate new entries only for an affected group when safety cannot be proven;
- append the discrepancy and correction, verify current state, and resume the
  affected capability automatically while unaffected groups continue.

Self-heal uses NinjaTrader/Glitch positions, orders, fills, balances, PnL,
brackets, receipts, and immutable events as authoritative. It repairs only
Hermes-owned derived state or uses a supported Glitch reconciliation surface.
It never resets accounts or baselines, rewrites journals, deletes losses,
fabricates missing evidence, or marks recovery complete without current proof.
Source defects may be recorded for later building, but runtime recovery does
not wait for Codex or a human.

Tier 1 — Hermes may propose and test on Sim:

- prompt, model, reasoning-effort, context-window, or skill refinements;
- portfolio-plan and target changes inside existing hard limits;
- new discretionary hypotheses and knowledge candidates;
- changes to review frequency when supported by cost/latency evidence.

Tier 2 — explicit operator approval:

- raising risk or contract caps;
- changing Glitch policy, account groups, ratios, prop rules, or execution state;
- enabling eval/live accounts;
- deploying code, restarting NinjaTrader, or changing credentials/providers.

Self-heal fixes Hermes’s ability to observe, reason, remember, and deliver. It never becomes a second execution or compliance engine.

## 8. Input and output optimization

Optimize relevance before shrinking intelligence:

- send the core loop five complete frames, state deltas, active plan, applicable prop rules, and relevant recent outcomes—not the entire historical corpus;
- keep raw evidence addressable by ID so Hermes can retrieve more when needed;
- give supervisory loops aggregates plus links to exceptions;
- give daily learning the full session summary and outcome set, not every repeated snapshot;
- cache stable doctrine, skills, rule versions, and knowledge hashes;
- record token cost, latency, malformed-output rate, rejection rate, and decision usefulness by model/job;
- never remove context merely to make a test pass; remove duplication and irrelevant history.

Core output remains tiny and executable. Supervisory outputs may be richer but must use versioned schemas and write only to Hermes-owned streams.

## 9. Paper activation

Stages A and B established the observable core. Stages C-E now run together in
one evidence-gated learning worker; they do not create additional executors.

### Stage A — interactive orientation

1. Install the source-controlled `glitch` profile overlay without enabling cron.
2. Start Hermes interactively in the Glitch project/profile.
3. Ask it to identify its role, hard boundaries, available packet/ledger/rule resources, memory layers, and current system state.
4. Let it inspect existing snapshots and journal evidence and write one harmless Hermes-owned orientation note.
5. Confirm native skills, memory, and sessions remain available alongside Glitch skills.

### Stage B — core paper loop only

1. Deploy and compile the Glitch packet writer.
2. Confirm five observed paired minute frames and one rolling packet with truthful continuity and in-progress timeframe semantics.
3. Enable only the Hermes core decision job.
4. Validate: zero flat-book model calls before five elapsed minutes, every-packet calls only while positioned or explicitly directed, next-packet recovery after any failure, one batch per invoked group set, no duplicate delivery, and receipts/Glitch decisions joined by ID.
5. Observe paper cycles and journal quality. Validate a complete protected open-to-close trade when Hermes chooses one; do not force an entry merely to satisfy infrastructure testing.

### Active learning stages

- Stage C: debrief completed master outcomes every 15 minutes and supervise new episodes hourly.
- Stage D: replace the active 300-minute plan only when a new hourly review exists.
- Stage E: catch up every completed Apex session with unjournaled episodes, update native memory from repeated attributable evidence, and stage or evaluate one reversible cognitive overlay. A proposal has no trading influence until a later independent supervisory decision activates it from later comparable evidence after contradiction review.

One 15-minute no-agent cron launches a separately locked learning process and
returns immediately, keeping slow Sol work outside the serialized minute-operator
lane. That process hosts all three learning stages and calls Sol only when their
evidence and cadence gates are due. Every nested call is tagged `trading`; the
worker has no execution authority.

## 10. Optimization rule

Every optimization must name:

```text
observed problem
affected loop
proposed change
expected benefit
cost and failure mode
evidence metric
rollback
```

Optimize the loop that owns the problem. Do not add deterministic trading gates to compensate for weak prompts, weak snapshots, missing memory, poor model choice, or bad ledger attribution.

## 11. Supervisor and builder handoff

The chat/supervisor session is an overlay on the same persistent Hermes mind. It
may analyze trading activity, maintain Hermes-owned lessons and health records,
offer advisory guidance to the trading session, and escalate a source change to
Codex. It does not become a second executor.

Codex is a bounded builder above Hermes. It runs only when explicitly invoked
for approved work from the supervisor ledger, works in the registered
workspace, validates, performs one clean deployment when required, and records
the handoff. It never polls snapshots, runs a trading cycle, or changes Glitch
state merely because a request exists. See `13_three_layer_handoff.md`.
