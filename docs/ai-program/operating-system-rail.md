# Glitch Operating System — First Principles & Rail

**Audience:** private maintainers and agents only.  
**Author:** operator + Cursor · 2026-07-09  
**Doctrine:** ABKB evidence ladder · Ponytail · north-star · prime invariant.

```text
Hermes proposes. Glitch validates, executes, journals, and protects.
```

**Version shorthand (labels only, not schedule):** v19 = v0.0.1.9 (baseline) · v20 = v0.0.2.0 · v30 = v0.0.3.0.

**Baseline:** v0.0.1.9 shipped — Trust + stable + non-AI operator. Ledger pruned; all new work is R01+.

**Branching:** active clean candidates are `cleanup/main-core` and
`cleanup/ai-core`; `main` remains public release authority. The former
`glitch/ai-rail` is historical implementation provenance. See
`docs/ledger/branching.md`.

Duration is **open**. Advance the rail on **evidence**, not calendar. Parallelize compute wherever steps are read-only.

## Product architecture decision (2026-07-14)

The distributable product has **one centralized Hermes brain**, hosted as a persistent supervised service on a VPS. Customers do not install Hermes and do not receive a Glitch chat surface.

```text
central market ingestion -> canonical five-minute packet -> Hermes recommendation
  -> authenticated recommendation API -> Glitch clients poll every five minutes
  -> each Glitch validates against local state -> executes/manages locally
  -> local replication, native brackets, compliance, journal, and Feed
  -> bounded outcomes return to the central learning rail
```

The recommendation is advice, not an order. It carries stable identity, instrument, desired action/position, confidence, thesis, expiry, structural stop/targets, and risk metadata. Each client remains authoritative for account/group eligibility, quantity, prop rules, replication, orders, brackets, and emergency controls.

The current local `glitch` Hermes profile and direct filesystem exchange are an **internal validation harness only**. They prove packet, decision, execution, replication, outcome, cycle-continuity, and learning contracts before the network boundary is introduced. They are not the customer deployment topology.

Stabilization precedes expansion. The gate is: supervised gateway continuity; isolated `trading`-tagged decision sessions with bounded explicit history, next-packet failure recovery, and durable native memory; truthful portfolio snapshots; every completed group trade reconciled into learning outcomes; reproducible paper execution/replication/brackets; and an observable cycle trail. Only after that gate may central ingestion, recommendation APIs, Feed UI, dynamic quantity, multi-group, or multi-instrument work begin.

---

## 1. What we are building (first principles)

We already have ~95% of the substrate:

```text
Glitch ingests markets · risk-assesses · replicates · journals
```

The operating system **uses these systems to build the system that runs Glitch**:

```text
┌─────────────────────────────────────────────────────────────────┐
│ NinjaTrader 8 + GlitchAnalyticsBridge + GlitchAddOn             │
│   deterministic: compliance · replication · ledger · firewall   │
└───────────────────────────┬─────────────────────────────────────┘
                            │ writes
                            ▼
              ┌─────────────────────────────┐
              │  MARKET snapshot (file)      │  instruments, prices,
              │  PORTFOLIO snapshot (file)   │  groups, accounts, risk
              └─────────────┬───────────────┘
                            │ same schema for historical export
                            ▼
              ┌─────────────────────────────┐
              │  Hermes operator bundle        │
              │   · snapshots                  │
              │   · prop firm rules / account    │
              │   · memory + mined patterns      │
              │   · skills (full kit)            │
              │   · instructions                 │
              │   · output format (Intent v2)    │
              └─────────────┬───────────────┘
                            │ POST intent (when armed)
                            ▼
              ┌─────────────────────────────┐
              │  Glitch firewall → execute     │
              │  journal → feedback loop       │
              └─────────────────────────────┘
```

**That is the whole system.** No second trading platform. No LLM with NT credentials.

### 1.1 Market snapshot (what Hermes sees about the tape)

Per instrument (multi-asset), per export tick:

```text
instrument root · timestamp · price / OHLC / volume
timeframes: 1m · 5m · 15m · 60m · 4h · 12h · 24h (extend bridge only as needed)
session high/low · previous session high/low
raw indicators · normalized indicators · Glitch score / components
regime · trend · volatility · order flow (when enabled)
no_trade_reasons
schema_version · snapshot_id · snapshot_hash · provenance
```

Prefer a **dedicated ingest chart** with `GlitchAiMarketIngest` for multi-instrument Hermes feed. Keep `GlitchAnalyticsBridge` on the trade chart for the visual assistant (single instrument, full MTF + bar coloring).

```text
Trade chart     → GlitchAnalyticsBridge  → AddOn analytics UI (one instrument)
Ingest chart    → GlitchAiMarketIngest   → feed bus → R03 market snapshot JSON
```

Multiple charts is the intended split when NT cannot cheaply multiplex instruments through the UI bridge.

### 1.2 Portfolio snapshot (what Hermes sees about the book)

```text
groups · accounts · connection state
positions · working orders · sizes
realized / unrealized PnL (commission-true)
drawdown · buffer remaining · lock state
prop firm id · tier · encoded rules version
risk levels · compliance flags
```

### 1.3 Hermes operator input bundle

Each decision cycle, Hermes receives:

| Input | Source |
|-------|--------|
| Latest market + portfolio snapshots | Glitch file export and/or localhost telemetry |
| Prop firm rules | Per account or group, from `PropFirmRules.json` + ComplianceEngine |
| Memory + patterns | Hermes store + mined archetypes from historical/replay corpus |
| Skills | Full Hermes kit: portfolio mgmt, sentiment, calculus, scripts, web search, trading skills |
| Instructions | `10_hermes_operator_contract.md` + versioned prompt/policy |
| Output format | Intent v2 JSON — mandatory SL + TP1, optional TP2/SL2 |

### 1.4 Learning loops (three compounding sources)

```text
1. Market data + patterns     → archetypes, regime filters, setup ranking
2. Trades + lessons           → journal round-trips, intent→outcome correlation
3. Portfolio + risk over time → drawdown behavior, sizing, fleet coordination
```

Historical export **must use the same schema** as live snapshots so mining transfers to the operator.

---

## 2. Operator economics (anti–fail-slow)

```text
Fail fast many times     = good   (~$17 eval + tokens + lessons)
Pay tokens to build/test/mine = good
Stall on dev / test / trade   = bad

Fail slow into $600 renewal while halfway = worst outcome
→ prefer cancel · pay $17 · fresh eval · apply lessons
```

Sunk-cost logic is **inverted**: paying to keep a stalled eval is worse than blowing it cheap and restarting with a better system.

**Preferred trajectory (compute-limited, not calendar-limited):**

```text
~3 days build rail through snapshots + exporter
~7 days train/mine/replay/paper in parallel (agent sessions)
~7–15 days live eval sprint when evidence green
```

No self-sabotage: do not cap the live eval at paper-survival dollars/day when the goal is **$15k pass on $250k eval**. Use **prop-encoded limits** tightened by Glitch, paced toward target — not arbitrary timidity.

---

## 3. Roles & parallel lanes

| Lane | Owner | Rail steps |
|------|-------|------------|
| **Glitch build** | Cursor | R01–R05, R08–R16, R22 |
| **Hermes ingest/mine** | Hermes + parallel agent sessions | R06, R07, R11+ (can start at R05) |
| **Live verify** | Operator | F5, GL-041, account enable |
| **Architecture** | Fable | contract changes only |

**Parallel now:** R06 pattern mining on `Glitch-Collab` + historical exports does not wait for R16 live trading.

---

## 4. The rail (one step after another)

Advance when the step’s **acceptance** is met. Skip nothing in §4.5 safety. Labels map to version bands for packaging only.

### Phase A — Observe (read-only)

| Step | Label | Work | Acceptance |
|------|-------|------|------------|
| **R01** | v20 | `GlitchInstrumentMetadataService` — point value, tick size, session from `MasterInstrument`; kill F2 silent fallback | Unknown instrument errors visibly; MNQ/NQ comparable |
| **R02** | v20 | Multi-asset bridge normalization; prefer single-chart `AddData` | ≥2 roots publish normalized readings; Analytics selector works |
| **R03** | v21 | **Market snapshot** writer → `GlitchData/snapshots/market/` | Valid JSON; schema versioned; all required TFs for active set |
| **R04** | v21 | **Portfolio snapshot** writer → `GlitchData/snapshots/portfolio/` | Accounts, positions, PnL net of commission, rules version |
| **R05** | v21 | Historical exporter — **same schema** as R03/R04 | Live archiver + **bulk corpus** via `GlitchMarketSnapshotHistoricalExporter` (Strategy Analyzer, 1m only) (**done**) |
| **R06** | H-1 | **Parallel:** pattern mining, backtest harness, archetype docs | Ranked setups with evidence; seed Hermes memory |
| **R07** | v21 | `GlitchExternalTelemetryServer` (localhost GET) — optional if files suffice | Schema-valid GET `/snapshot`; bearer auth; off UI thread (**done**) |

### Phase B — Propose (paper only)

| Step | Label | Work | Acceptance |
|------|-------|------|------------|
| **R08** | v22 | `GlitchAiIntentServer` POST `/intent` — **no executor registered** | 100% intents journaled; zero `CreateOrder` in path (**done**) |
| **R09** | v22 | `GlitchAiRiskFirewall` — 15-step chain | Adversarial intents rejected with per-check journal (**done**) |
| **R10** | v22 | `GlitchAiJournalBridge` — intent ↔ snapshot_hash ↔ verdict | Round-trip reconstructable from journal alone (**done**) |
| **R11** | v22 | Hermes `suggest_trade` → paper endpoint | GL-043: strict JSON ingress; model/prose failure is a no-trade cycle, never an actionable fallback |

**Gate:** GL-024 commission-true journal before Hermes learns from outcomes.

### Phase C — Execute sim

| Step | Label | Work | Acceptance |
|------|-------|------|------------|
| **R12** | v23 | Master-only `GlitchAiOrderExecutor` plus producer-neutral replication | GL-042: AI submits one protected master order; CopyEngine independently creates ratio-scaled, follower-native OCO protection |
| **R13** | v23 | Replay harness — archetypes vs baseline on Eval risk profile | ≥1 archetype beats baseline on replay evidence |

**Gate:** R08–R11 clean (zero firewall bypasses, zero schema drift rejects) — **session count, not weeks**.

### Phase D — Execute live (fail-fast friendly)

| Step | Label | Work | Acceptance |
|------|-------|------|------------|
| **R14** | — | **GL-041** Honest Copy live verification | Protocol §7 green |
| **R15** | v24 | Eval allowlist + **Eval Sprint profile** (prop-paced) | Rules match firm; firewall stricter than prop |
| **R16** | v24 | Live Hermes loop on **one** eval; kill switch | Brackets NT-held; unauthorized trade impossible |
| **R17** | — | Blow → journal → $17 restart | Lesson doc exists; not a silent bug |

**Eval Sprint profile (live pass pacing, not paper timidity):**

```text
Encode prop rules for ~$250k eval: $15k target · $6.5k max loss
Track buffer remaining; auto-disable AI at operator-set fraction (e.g. 50% buffer consumed)
Per-trade risk from bracket geometry × point value — sized for pass pace, not $100 toy cap
Max concurrent AI positions per account: start 1; scale on evidence
No pyramiding · no averaging down · no stop widening
```

### Phase E — Compound (M1 → M3)

| Step | Label | Work | Acceptance |
|------|-------|------|------------|
| **R18** | v25 | Confidence gating + no-trade reason respect | Churn down; PnL/trade flat or up |
| **R19** | v26 | `ADJUST_STOP` tighten-only · `PARTIAL_EXIT` · optional 1m exit cadence | Widening attempts always rejected |
| **R20** | v27 | Fleet: 2–3 evals; portfolio open-risk; correlation guard | Cap breach blocks intent |
| **R21** | v28 | Central Hermes VPS + canonical ingestion + recommendation service | Persistent supervised brain; schema parity; authenticated, expiring, idempotent recommendations |
| **R22** | v29 | Self-heal: sanity fail → disable AI; schema drift → paper; fault → degrade | Injected faults recover without code deploy |
| **R23** | v30 | Self-learn: promotion gate; versioned policy candidates | Promotion beats baseline on replay; audit trail |

**Milestone names (M0–M3)** remain in `glitch_hermes_docs/docs/05_milestones_m0_m3.md`; this rail is the **implementation order**.

---

## 4.5 Safety invariants (never “move fast” past these)

- Hermes has no order API, no NT credentials.
- Every ENTER: SL + TP1; NT OCO; AI never manages loss mid-flight.
- Glitch firewall runs before any order; failure ⇒ no order.
- GL-041 before live eval AI.
- Hermes trades only a configured group master. Glitch alone owns replication, follower protection, and reconciliation.
- One effective temporal/prop-rule decision must be shared by UI, snapshots, firewall, and execution. Entry lockouts never disable exits or risk reduction.

---

## 5. Cognitive cadence

| Job | Cadence | LLM |
|-----|---------|-----|
| central market snapshot/packet build | 1 min / 5 min close | no |
| packet and gateway sanity | 5 min | no |
| central recommendation | 5 min | yes |
| portfolio_risk_review | 1 hr | optional |
| learning_pass | 6 hr | yes |
| daily_learning | post-session | yes |

The central VPS uses a supervised persistent Hermes gateway. Hermes native cron may schedule cognition inside that service. Client polling is transport and must not create one model call per customer.

---

## 6. Ponytail — do not build

- Per-client Hermes installations or per-client model calls for the shared recommendation.
- A Glitch chat tab; the customer observability surface is Feed.
- An API that can bypass local Glitch validation or submit broker orders directly.
- AI inside replication engine.
- Separate live vs historical snapshot shapes.
- Martingale / buffer-chase logic.
- Development calendar gates (“wait 2 weeks”) — use evidence gates. This does not waive real exchange-session, maintenance, weekend/holiday, or firm event rules.
- Extra indicators before bridge truth is exported.
- Separate central and client snapshot schemas.

---

## 7. Version labels (packaging map)

| Versions | Rail steps | Outcome name |
|----------|------------|--------------|
| v0.0.2.0 | R01–R02 | Eyes |
| v0.0.2.1 | R03–R07 | Voice |
| v0.0.2.2 | R08–R11 | Ears |
| v0.0.2.3 | R12–R13 | Hands-sim |
| v0.0.2.4 | R14–R17 | Hands-eval |
| v0.0.2.5–v0.0.2.9 | R18–R21 | Filter → Shadow |
| v0.0.3.0 | R22–R23 | Learn |

---

## 8. Active pointer

**Current stabilization pointer:** `cleanup/main-core` and `cleanup/ai-core` consolidate one producer-neutral replication/protection core. AI submits and manages only a configured master; replication owns followers, ratios, follower-native OCO protection, and explicit resync. Source checks are green; F5 and bounded Sim evidence remain open. Preserve the master-only intent -> Glitch replication -> account-local bracket path.

**Evidence pointer:** GL-064 owns versioned paper performance/regime calibration after Journal and outcome truth are green. A profitable-looking directional slice is evidence to analyze, not a license to encode a deterministic strategy or declare profitability.

**Expansion pointer after stabilization:** finish GL-050 acceptance, then GL-051 through GL-052 central ingestion/VPS/API, and GL-054 multi-instrument work. GL-053's two-leg local slice already exists; do not build arbitrary leg arrays before its 3-contract runtime proof.

**Next bounded builder work:** consume the single backlog in `docs/ledger/backlog.md`. Do not revive the historical GL-042/GL-043 arm ritual or any Codex market-monitoring loop.

**Parallel:** R06 — pattern mining on historical replay exports (Hermes sessions).

**Blocker check:** GL-041 status before R16.

---

## References

- `docs/ledger/north-star.md`
- `docs/ai-program/roadmap.md`
- `docs/ledger/backlog.md`
- `glitch_hermes_docs/docs/09_intent_contract_v2_brackets.md`
- `glitch_hermes_docs/docs/10_hermes_operator_contract.md`
- `glitch_hermes_docs/docs/11_snapshot_ingestion_learning_pipeline.md`
- `docs/ai-program/tradovate-apex-instrument-universe.md`
