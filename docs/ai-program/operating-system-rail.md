# Glitch Operating System — First Principles & Rail

**Audience:** private maintainers and agents only.  
**Author:** operator + Cursor · 2026-07-09  
**Doctrine:** ABKB evidence ladder · Ponytail · north-star · prime invariant.

```text
Hermes proposes. Glitch validates, executes, journals, and protects.
```

**Version shorthand (labels only, not schedule):** v19 = v0.0.1.9 (baseline) · v20 = v0.0.2.0 · v30 = v0.0.3.0.

**Baseline:** v0.0.1.9 shipped — Trust + stable + non-AI operator. Ledger pruned; all new work is R01+.

**Branching:** R01+ **product code** on `glitch/ai-rail`; `main` = v0.0.1.x patches + user downloads only. See `docs/ledger/branching.md`.

Duration is **open**. Advance the rail on **evidence**, not calendar. Parallelize compute wherever steps are read-only.

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

Prefer **one chart / one bridge instance** feeding multiple instruments via `BarsInProgress` / `AddData` before asking the operator to open many charts. Multiple charts is acceptable when NT requires it.

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
| **R05** | v21 | Historical exporter — **same schema** as R03/R04 | Replay file validates; Hermes ingests without transform |
| **R06** | H-1 | **Parallel:** pattern mining, backtest harness, archetype docs | Ranked setups with evidence; seed Hermes memory |
| **R07** | v21 | `GlitchExternalTelemetryServer` (localhost GET) — optional if files suffice | Schema-valid GET `/snapshot`; bearer auth; off UI thread |

### Phase B — Propose (paper only)

| Step | Label | Work | Acceptance |
|------|-------|------|------------|
| **R08** | v22 | `GlitchAiIntentServer` POST `/intent` — **no executor registered** | 100% intents journaled; zero `CreateOrder` in path |
| **R09** | v22 | `GlitchAiRiskFirewall` — 15-step chain | Adversarial intents rejected with per-check journal |
| **R10** | v22 | `GlitchAiJournalBridge` — intent ↔ snapshot_hash ↔ verdict | Round-trip reconstructable from journal alone |
| **R11** | v22 | Hermes `suggest_trade` → paper endpoint | End-to-end propose without execution |

**Gate:** GL-024 commission-true journal before Hermes learns from outcomes.

### Phase C — Execute sim

| Step | Label | Work | Acceptance |
|------|-------|------|------------|
| **R12** | v23 | `GlitchAiOrderExecutor` Sim101 — bracket-mandatory, `GlitchAI*` signals | Zero naked entries; attach failure ⇒ entry cancelled |
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
| **R21** | v28 | Shadow model + VPS 24/7 runbook | Shadow journaled; soak without stuck handoffs |
| **R22** | v29 | Self-heal: sanity fail → disable AI; schema drift → paper; fault → degrade | Injected faults recover without code deploy |
| **R23** | v30 | Self-learn: promotion gate; versioned policy candidates | Promotion beats baseline on replay; audit trail |

**Milestone names (M0–M3)** remain in `glitch_hermes_docs/docs/05_milestones_m0_m3.md`; this rail is the **implementation order**.

---

## 4.5 Safety invariants (never “move fast” past these)

- Hermes has no order API, no NT credentials.
- Every ENTER: SL + TP1; NT OCO; AI never manages loss mid-flight.
- Glitch firewall runs before any order; failure ⇒ no order.
- GL-041 before live eval AI.
- Replication path and AI path stay separate.

---

## 5. Hermes cron (when armed)

| Job | Cadence | LLM |
|-----|---------|-----|
| market + portfolio snapshot flush | 1 min | no |
| snapshot_sanity | 5 min | no |
| suggest_trade | 5 min | yes |
| portfolio_risk_review | 1 hr | optional |
| learning_pass | 6 hr | yes |
| daily_learning | post-session | yes |

Cron first. Daemon only if cron fails a measured need.

---

## 6. Ponytail — do not build

- Public telemetry/intent endpoints.
- AI inside replication engine.
- Separate live vs historical snapshot shapes.
- Martingale / buffer-chase logic.
- Calendar gates (“wait 2 weeks”) — use evidence gates.
- Extra indicators before bridge truth is exported.
- Always-on Hermes microservice platform.

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

**Next step:** R03 — market snapshot writer (file).

**Parallel:** R06 — mine `Glitch-Collab` / historical exports in separate Hermes sessions now.

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
