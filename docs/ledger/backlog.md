# Glitch Backlog

**Active work:** `docs/ai-program/operating-system-rail.md` (R01–R23) on branch **`glitch/ai-rail`**.  
**User releases:** `main` only — v0.0.1.x (`docs/ledger/branching.md`).
**Status values:** `todo | in_progress | partial | done | deferred`. Flip only with evidence.

**v0.0.1.9 baseline (2026-07-09):** Trust + stable + **non-AI operator**. Shipped `Glitch_v0.0.1.9.zip`. Wave 6 + Honest Copy + session hardening closed below. New work = rail steps only.

Key paths: `ninjatrader/Glitch/AddOns/GlitchAddOn/` · `Indicators/glitch/GlitchAnalyticsBridge.cs`

---

## Closed — v0.0.1.9 "Trust" (non-AI operator)

| ID | Title | Status | Evidence |
|----|-------|--------|----------|
| GL-020 | RP-1: dispatcher catch on account refresh | done | `RefreshPipeline.partial.cs`; shipped v0.0.1.9 |
| GL-021 | RP-3: stale header/shell on disconnect | done | same; shipped v0.0.1.9 |
| GL-022 | SHA-256 release checksums | done | `apps/download` checksums manifest; v0.0.1.9 in manifest |
| GL-023 | Repo hygiene (export artifacts gitignore) | done | `.gitignore`; shipped v0.0.1.9 |
| GL-024 | F1 commission truth (journal net PnL) | done | ledger + reconcile notice; shipped v0.0.1.9 |
| GL-036 | Honest Copy P0: Flatten via `account.Flatten`, replication safe defaults | done | `GlitchReplicationEngine.cs`; commit `160c296` + v0.0.1.9 |
| GL-037 | Event-driven `GlitchCopyEngine` + drift banner | done | `GlitchCopyEngine.cs`; v0.0.1.9 |
| GL-038 | Risk consent matrix (display-only default) | done | v0.0.1.9 operator path; extend on rail if needed |
| GL-040 | Deletion pass / legacy replication behind kill switch | done | Honest Copy merges; v0.0.1.9 |
| GL-010 | Scrollable panels | done | Accordion scroll host; v0.0.1.9 session |
| GL-011 | Followers block star layout | done | prior UI calm wave + v0.0.1.9 |
| GL-012 | Calm-by-default warnings | done | taxonomy landed; v0.0.1.9 |
| GL-013 | Journal performance first | done | v0.0.1.9 |
| GL-014 | Settings granularity | partial → done | sufficient for v19; deepen under R20 fleet if needed |
| GL-015 | Editable cell affordances | done | v0.0.1.9 |
| — | Analytics P0–P2 scoring (bridge DM, Williams %R, per-TF freshness) | done | v0.0.1.9 session |
| — | Daily PnL header + tab scroll UX | done | v0.0.1.9 session |
| GL-001 | Replication drift / unsolicited orders | done | Honest Copy event engine; operator acceptance v0.0.1.9 |
| GL-004 | No loops / dup orders | done | Copy engine idempotency; v0.0.1.9 |
| GL-005 | PnL / analytics accuracy (F1) | done | F1 via GL-024; F2 deferred → R01 |
| GL-008 | Multi-asset bridge (umbrella) | superseded | → R01–R02 (rail) |
| GL-009 | Hermes decision layer (umbrella) | superseded | → R03–R23 (rail) |

---

## Carryover (not v19 blockers — on rail or deferred)

| ID | Title | Rail / note |
|----|-------|-------------|
| GL-039 | PnL scope selector (Master/Group/Fleet) + basis labels | **R04** portfolio snapshot accuracy; polish when fleet UI needed |
| GL-041 | Live verification protocol (§7) | **R14** gate before live **AI**; manual non-AI operator OK on v19 |
| GL-005 | F2–F10 math polish (point value, PF=0, session tags…) | F2 → **R01**; rest **deferred** until reproved |
| GL-002 | Full codebase audit | **deferred** — Honest Copy + v19 ship supersede as gate |
| GL-003 | Compliance opt-in completeness | **deferred** — v19 usable; harden under R20 |
| GL-017 | Remove FundingTicks | **deferred** — before external replication marketing |
| GL-018 | Rebuild Lucid rules | **deferred** |
| GL-019 | Per-firm copy-trading policy | **deferred** — operator ticket for TPT/TradeDay |
| GL-006 | Less noise (umbrella) | **done** via GL-010–015 |
| GL-007 | Distribution loop to test users | **deferred** — v0.0.1.9 published; operator-driven |
| GL-016 | NT8 + prop-firm research | done (research) |

---

## Active — Operating system rail

Canonical spec: `docs/ai-program/operating-system-rail.md`  
Version map: `docs/ai-program/roadmap.md`

| Rail | GL | Version | Title | Status |
|------|-----|---------|-------|--------|
| R01 | GL-025 | v20 | Instrument metadata registry | done |
| R02 | GL-026 | v20 | Multi-asset ingest (`GlitchAiMarketIngest`) | done (bridge rolled back to single-instrument UI) |
| R03 | — | v21 | Market snapshot writer (file) | done |
| R04 | — | v21 | Portfolio snapshot writer (file) | done |
| R05 | — | v21 | Historical exporter (same schema) | done (live archiver + bulk corpus strategy) |
| R06 | GL-029 | H-1 | Pattern mining / backtest (parallel) | **done (v1)** — 4 validated + 1 candidate + 2 retired archetypes; see `docs/ai-program/r06-pattern-mining.md` §10 |
| R06a | GL-029 | H-1 | · ETL corpus → parquet + quality audit | done (705,697 snapshots 2024-01→2025-12, 0 bad; `Glitch-Collab/Research/r06-mining/`) |
| R06b | GL-029 | H-1 | · Triple-barrier labels + regime frame + expectancy scan | done (6,596 tests → 23 OOS survivors, 5 families) |
| R06c | GL-029 | H-1 | · Candidate archetypes + validation slice (2025-Q3) | done (7 frozen) |
| R06d | GL-029 | H-1 | · Archetype JSON + MNQ playbook + Hermes memory seed | done (`glitch_hermes_docs/memory/` + doc 12 skills) |
| R06e | GL-029 | H-1 | · Holdout pass (2025-Q4, locked) + R13 replay proof | partial — holdout done (2 longs retired); R13 replay proof pending |
| R06f | GL-029 | H-1 | · Ongoing loop: monthly re-mine + live-stat reconciliation | **first pass done 2026-07-13** on GL-046 expanded corpus (2022→2026): v1 set demoted after era re-test; v2 set (5 validated incl. HV-LULL workhorse) holdout-proven on 2026-Q1 and seeded to `glitch_hermes_docs/memory/archetypes.v2.json`; next: fill 2023-Q4 corpus hole, DSR/PBO, order-flow re-export, R13 replay on v2 |
| R06g | GL-029 | H-1 | · Management-layer mining + skill grading (exits, time-stops, scale-out) | **probes done 2026-07-13** (`out/expanded/probe_results.md`): trendiness forecast NULL; generic 2σ-fade skill FAILS grading (do not arm); DI-collapse exhaustion weak; **time-stop signal strong** (underwater@20min → −12/−14 pts vs in-profit@20min → +19/+33 pts on v2 flagships). Next: quantify per-archetype exit thresholds, mine breakeven/scale-out legs, XGBoost+SHAP take/skip meta-labeler, then fold into playbook + R13 replay |
| R07 | GL-027 | v21 | External telemetry server (localhost GET) | done |
| R08 | GL-030 | v22 | Intent endpoint (paper only) | done |
| R09 | GL-030 | v22 | AI risk firewall | done |
| R10 | GL-031 | v22 | AI journal bridge | done |
| R11 | GL-035 | v22 | Hermes suggest_trade → paper | partial — strict ingress, bounded profile, failure journal, timeout, and paper `NOTHING` proof exist; GL-043 still needs fresh-feed POST/idempotency evidence |
| R12 | GL-032 | v23 | Sim101 bracket executor | partial — group-safe Sim101→102/103 implementation is deployed and compiled; GL-045 owns the remaining runtime acceptance fixtures |
| R13 | GL-029 | v23 | Replay harness / archetype proof | ready |
| R14 | GL-041 | — | Honest Copy live verify (pre-AI) | todo |
| R15 | GL-033 | v24 | Eval allowlist + Eval Sprint profile | todo |
| R16 | GL-033, GL-035 | v24 | Live Hermes loop | todo |
| R17 | — | — | Fail-fast lesson loop | ongoing |
| R18 | — | v25 | Confidence gating | todo |
| R19 | — | v26 | Lifecycle (ADJUST_STOP, partial) | todo |
| R20 | — | v27 | Fleet / portfolio risk | todo |
| R21 | — | v28 | Central Hermes VPS + ingestion + recommendation API | specified in GL-051–052; blocked on stabilization |
| R22 | — | v29 | Self-heal tier 1 | todo |
| R23 | — | v30 | Self-learn + promotion | todo |
| — | GL-028 | H-0 | Hermes ingest scaffold | todo |
| — | GL-034 | gates | Security audit (2-stage) | todo |

**Dependencies (evidence, not calendar):**

```text
R01–R02 → R03–R05 (snapshots need metadata)
R05 → R06 parallel OK immediately on Collab/historical
R03–R05 → R07 optional
R08–R11 clean → R12–R13
R12–R13 evidence → R14 → R15–R16
GL-034 stage 1 → R07 ship · stage 2 → R15–R16
R16+ → R18…R23
```

### R11–R12 Sim group implementation tickets (Spec Kit task form)

**Feature boundary:** Hermes receives only bounded, read-only data; it may propose exactly one
`glitch.intent.v2` market intent for `MNQ` / `Sim101`. Glitch resolves the enabled Sim101
group, validates once, submits the account-local brackets, journals every state change, and
remains the only order-capable component. No eval or external account is in scope.

| ID | Story / dependency | Exact implementation surface | Verification and acceptance |
|----|--------------------|------------------------------|-----------------------------|
| GL-042 | **R12a — group-safe executor (deployed and F5-compiled 2026-07-12).** Depends on R08–R10 and current event-copy wiring. | Canonical source requires the configured Sim executor, resolves the enabled `AccountGroups.tsv` group, rejects non-Sim/unallowlisted members, creates account-local OCO brackets with `GLT-AI-*` signals, fails closed on fresh/complete group risk state, tracks all recovery orders to explicit terminal state, and permits flatten only for exact account/contract/net-quantity ownership. | Independent safety rereads closed all identified P0/P1 findings; 19 automatic validator/source-safety checks pass and diff check is clean. The complete 78-file AddOn was deployed via the approved helper and F5 returned without a compiler-error surface; refreshed runtime snapshots pass every group/schema/risk check. Remaining group-runtime acceptance is tracked in GL-045. No arm/order/live promotion. |
| GL-043 | **R11a — strict Hermes ingress (partial 2026-07-12).** Depends on GL-042 policy surface and R07 telemetry. | `tools/hermes/` adapter and tests; `glitch_hermes_docs/schemas/intent.v2.schema.json`; `GlitchAiIntentValidator.cs` only if a schema/runtime mismatch is exposed. Read latest market/portfolio snapshots and policy; call Hermes under a hard timeout; extract exactly one JSON object; schema-validate locally; post only valid output. A model error, prose, timeout, invalid JSON, or snapshot rotation is a journaled/refused cycle with no order. | Passed: isolated model fixtures prove LONG, SHORT, HOLD, EXIT, NOTHING; validator rejects prose, wrong account, LIMIT, naked entry, and stale hash; entries are one-contract `MARKET`, omit `limit_price`, and pass tick/risk geometry. Production adapter journals staged success/failure, captures model output, hard-times out, and cleans active state; forced exit/timeout fixtures fail closed. Glitch now binds hash/freshness/market price to one read and revalidates it before group execution. The `NOTHING`-only idempotency harness refuses armed execution and verifies first accept, duplicate `409`, flat/no-order postconditions, and skipped execution evidence; 19/19 automatic checks pass. Remaining: run that proof under a fresh market feed. |
| GL-044 | **H-2 profile — minimal isolated Glitch Hermes operator (done 2026-07-12).** Depends on GL-043 contract, not on live execution. | Canonical skills live under `hermes-profile/skills`: observe market, assess risk, form thesis, build intent, and separately review outcomes. `tools/hermes/build-data-capsule.ps1` copies only allowlisted current snapshots/policy, selected historical examples, patterns, contracts, and provenance-separated journals into a hash-manifested capsule. Profile `glitch` uses `gpt-5.6-luna`/medium, no memory/curator/delegation, terminal-only tools, and an offline read-only Docker mount; canonical `GlitchData`, repo, credentials, and AB/ABKB are not mounted. Hermes-attributable trade history starts at zero; optional Sim101 history is `legacy_sim101` context only. | Five skills validate; profile reports the target model and exactly five local skills; Docker proof enumerates only `/opt/glitch-data`, rejects writes, and cannot see the repo. The bounded three-day capsule contains 4,141 timestamp-sorted snapshots with a SHA-256 manifest, and the isolated profile produced a schema-valid `NOTHING` intent that traversed the paper firewall and journaled a skipped execution while unarmed. No cron or order authority granted. |
| GL-045 | **Sim group acceptance harness.** Depends on GL-042–043. | Focused NT-independent tests where possible plus `tools/hermes/` fixtures; operator F5 checklist in the existing ledger evidence path. | Run the adversarial matrix: stale snapshot, wrong account, wrong instrument, duplicate ID, bad tick, invalid/wide bracket, missing follower, follower rejection, stop exit, target exit, and kill switch. Every case has a deterministic journaled verdict; no unprotected follower position is tolerated. |
| GL-046 | **R06g — expanded corpus re-mine (audit active; export not frozen).** Depends on the operator's new snapshot export completing. | `Glitch-Collab/Research/r06-mining/` scripts and committed reports only; `docs/ai-program/r06-pattern-mining.md` findings append; `glitch_hermes_docs/memory/` changes only after promotion. Freeze v1 provenance, audit schema/time coverage, then use new chronological purged train/validation/locked-holdout splits. | Latest live audit at 2026-07-12T22:46Z: 1,394,655 indexed v2 rows, 2022-01-04→2026-03-12, zero malformed/duplicate IDs and 141/141 sampled payloads clean; the append stream is filling 2023-10. Index has one append-block chronology inversion and grew during the audit, so it is not frozen. The audit distinguishes set integrity from append order; corpus freeze requires full inventory and exact re-hash; split design binds timestamp-sorted slices and keeps 2026 unopened; candidate freeze hashes exact individual archetype bytes and geometry and refuses an opened holdout. Twelve focused gate tests pass. After export stops: full audit → corpus freeze → split contract → candidate freeze → one-touch 2026 evaluation, then bias/regime/outcome comparisons. No archetype/policy promotion without R13 replay evidence. |

**Current closeout order:** GL-043 → GL-045. GL-042 implementation and GL-044 isolation/capsule proof are complete. GL-046 runs in parallel after
the new export completes. R12 is not armed until GL-045 is green. R14 remains the separate gate
for any non-simulation AI promotion.

---

### Central-brain decision and stabilization tickets (Spec Kit task form)

**Architecture decision:** the product runtime is one central Hermes brain on a supervised VPS. Entitled Glitch clients poll one stored recommendation per five-minute window; Glitch remains the local execution, management, replication, bracket, compliance, and journal authority. Customer UI adds Feed, not Chat. The present local Hermes profile is a contract-validation harness, not a distributable dependency.

| ID | Phase / dependency | Scope | Acceptance and evidence |
|----|--------------------|-------|-------------------------|
| GL-049b | **S0 — follower exit/bracket and fleet-flatten terminality; source/deploy/compile complete, runtime proof open.** Blocks Feed acceptance. | Delegate complete copied/catch-up follower exits to NT AddOn `Account.Flatten` so NT cancels account-local working orders before closing the remaining position. Refuse unsupported partial follower exits without removing protection. Flatten All must retain configured account names as intended scope and treat unresolved/disconnected accounts as incomplete, never flat. | Source contracts pass; full AddOn was deployed with zero workspace/live hash drift and the operator reported a green final F5 compile. Remaining: one protected entry/master EXIT or native SL/TP fixture plus one disconnected-account/Flatten-All fixture must prove no stale follower order can reverse a flat account and no missing configured account can produce a false fleet-flat result. |
| GL-047 | **S0 — gateway and session continuity; implementation present, runtime proof open.** Blocks all expansion. | Hermes-native supervised hidden service, exactly one `glitch` profile and named `trading` session, zero Codex/operator polling in runtime, and bounded session/context rollover that preserves durable learning without replaying stale decisions as current limits. | Source installer/session tests are green. Remaining: prove terminal/Codex exit does not stop scheduled work, gateway status agrees across service and Hermes surfaces, restart-on-failure works, consecutive decisions retain one logical session until an explicit bounded rollover, and missed windows are journaled. |
| GL-048 | **S0 — authoritative outcome learning; implementation present, runtime proof open.** Depends on completed Glitch journal + TradeLedger rows. | Correlate one group lifecycle event with the master and every enabled follower's authoritative `TradeLedger.tsv` round trip. Native Hermes memory/skills remain available, but only evidence-linked outcomes may update lessons; self-heal may append corrections and never rewrite or conceal trading history. | Focused reconciliation and skill/source contracts pass. A missing account round trip emits no outcome. Once all expected accounts close, exactly one idempotent outcome records per-account trade IDs, fills, exits, PnL, close kind, MFE/MAE, and group PnL. Remaining: fresh post-reset runtime proof that the journal is read, an outcome is synced, a lesson/memory record is produced, and a later cycle can retrieve it. |
| GL-049 | **S0 — portfolio snapshot truth; source fix present, runtime proof open.** Blocks learning and Feed. | Build top-level `position_display`, unrealized PnL, and total PnL from the same live NT position collection serialized in nested `positions[]`, not a lagging UI row. | Source contracts pass. Remaining: while open, signed position and unrealized totals reconcile exactly with nested positions for every account and portfolio totals; while flat, all agree on zero. One open/flat NT fixture is required. |
| GL-050 | **S1 — observable Feed; local UI slice implemented.** Depends on GL-047–049b green for acceptance and on GL-052 for central-client transport. | The local read-only `Glitch AI` Feed shows five stages: snapshots, sealed packet, AI decision, execution check, and outcome, plus decision/audit and validation details. No Chat or free-form control surface. Adapt this existing view to central recommendation receipts later; do not create a second Feed implementation. | Source/UI contracts pass and the tab compiled in the deployed AddOn. Remaining: prove durable rebuild/freshness/error rendering across restart and preserve zero extra model calls; later bind the same view to central API receipts. |
| GL-051 | **S2 — central ingestion + VPS brain.** Depends on S0 and stable paper evidence. | Build central market ingestion that emits the same snapshot and five-frame packet schemas/feature versions as the harness. Run one persistent supervised Hermes profile/session on VPS with durable recommendation and learning stores. | Shared fixture/hash parity; one packet causes at most one model call and one stored recommendation; restart preserves session/memory; stale/incomplete packets publish no actionable recommendation. |
| GL-052 | **S2 — recommendation API and client polling.** Depends on GL-051. | Authenticated entitlement-scoped API returns versioned, identified, expiring, idempotent recommendations. Glitch polls once per window and applies local portfolio/group/risk/firewall truth before acting. | Many clients consume one recommendation without multiplying model calls; replay/expiry/schema mismatch fail closed; API cannot submit orders; localhost execution surface remains private; receipts join recommendation to local execution/outcome IDs. |
| GL-053 | **S3 — quantity, legs, and group control. Two-leg local slice implemented, deployed, and compiled on `ai-rail`; central recommendation portion still depends on GL-052.** | Quantity 1–5 derives from Glitch policy. `take_profit_2` + `quantity_tp1` creates two independent native OCO legs on the master; Glitch replication scales each leg by the configured follower ratio. Same-direction later entries remain independently protected tranches. Do not add a separate averaging strategy or have Hermes trade followers. Arbitrary three-plus-leg arrays remain deferred until two-leg runtime proof justifies them. | Source contract/normalizer/firewall/executor/copy-engine tests pass; non-integral follower leg ratios reject before entry; partial entry fills fail closed into cancel/flatten recovery; operator reported green F5 after deployment. Remaining gate: one 3-contract Sim proof covering master/followers, TP1-only reduction, surviving runner protection, TP2/SL close, reconnect, and journal reconciliation. |
| GL-054 | **S4 — multi-instrument portfolio.** Depends on GL-053 and central ingest coverage. | Rank and recommend across multiple instruments while local Glitch enforces account/group allocation, concentration, contract resolution, and portfolio risk. | Per-instrument freshness/contract tests, cross-instrument exposure evidence, no duplicate use of one risk budget, and paper profitability evidence before promotion. |
| GL-063 | **S0 — temporal and prop-rule compliance truth.** Blocks any PA/live AI promotion; may be proven on Sim first. | Add one authoritative time-policy decision used by UI, snapshot, AI firewall, and order executor. Model per-firm/program/account-state rules for scheduled news, maintenance breaks, weekends/holidays, entry cutoff, and whether an already-open position must be flattened before a prohibited window. Exits and risk-reducing actions remain available during a lockout. Replace the current broad FRED release-family heuristic with a verified event-time source; never turn `realtime_start`/`realtime_end` metadata into fake daily event times. Record source URL, retrieved/version time, timezone, event start/end, pre/post buffer, affected accounts, and exact rejection reason. Current finding: the analytics banner computes a 5-minute pre/post window around inferred event duration, but the AI firewall only reads static `policy.news_lockout`; a live paper intent was approved while the banner showed an active FOMC event, and the banner's displayed end omitted the post-buffer. | Table-driven timezone/DST fixtures plus one Sim proof each for: before/during/after a real event, false/duplicated event rejection, maintenance break, weekend/holiday, entry immediately before a must-flat boundary, protected position approaching a boundary, and allowed emergency exit while locked. UI and firewall must show the same effective window. Reverify current Apex rules before encoding: normal directional news trading may be allowed, while prohibited automation/AI on PA/live can be the controlling rule. Do not infer Eval/PA legality from anecdotes; require official current rule evidence or written firm approval. |
| GL-064 | **S0 — paper performance and regime calibration.** Depends on GL-055 journal truth and GL-048 outcome truth; runs before centralization/live promotion. | Freeze prompt/skill/code versions for bounded paper evaluation windows and analyze authoritative NT account exports by direction, volatility/regime, session, hold time, MAE/MFE, stop/target geometry, quantity, and costs. Preserve Hermes cognition: findings may tune prompts, skills, and candidate policy, but must not become hidden deterministic entry gates. The first observed session showed useful directional behavior (roughly `-$200` to `+$520` during a trend) followed by giveback in chop. NT's full-day screenshot showed `+$401.50` across 71 trades, while a later 08:00-scoped report showed `+$291.50` across 66 trades; the corrupted Glitch Journal showed 44 trades and `-$1,374.50`. Treat all of this as a diagnostic sample with explicit scope differences, not profitability proof. | Each evaluation binds exact commit, prompt/skill hashes, account/time scope, NT export, and reconciliation status. Report expectancy after costs, drawdown, turnover, long/short and regime slices, replication parity, and counterfactual prompt change. Promote a change only when a later untouched window improves the named metric without breaking safety; otherwise retain/revert it. No arbitrary trade quota, forced archetype, or daily-profit promise becomes an execution rule. |

**Mandatory order:** close runtime evidence for GL-047–049b + GL-055 -> prove GL-053 and GL-063 on Sim -> collect GL-064 stable one-instrument evidence -> finish GL-050 acceptance -> GL-051–052 centralization -> GL-054 instruments. GL-053's local two-leg slice already exists. No parallel implementation queue and no automatic Codex build loop.

---

### Mainline backport handoff — shared Glitch core from `glitch/ai-rail`

**Recorded:** 2026-07-15

**Target:** `main` / non-AI Glitch

**Comparison baseline:** `main` and `origin/main` at `d216015`; `glitch/ai-rail` source baseline at `d7975fb`. The backport must compare exact commits again immediately before implementation.
**Porting rule:** reimplement or selectively cherry-pick only the shared deterministic Glitch capability. Do not import `Services/Ai`, Hermes controls, the AI tab, intent/policy endpoints, model scheduling, snapshot/corpus exporters, or AI-specific signal coupling into non-AI `main`.

| ID | Priority / dependency | Exact shared-core implementation scope | Verification and acceptance |
|----|-----------------------|----------------------------------------|-----------------------------|
| GL-055 | **P0 — Journal round-trip reconstruction integrity. First mainline ticket.** Present in both `main` and `ai-rail`; this is a newly proven latent defect, not an AI feature. | Fix `GlitchTradeInsightsService.ApplyExecution`: when no open state exists, `Sell` and `BuyToCover` are orphan exits and must never create a synthetic position; only true opening actions (`Buy`, `SellShort`) may initialize state. Use action semantics first and exit signal/tag metadata as corroboration. Preserve valid scale-in, scale-out, full close, and reversal handling when state exists. When one execution reverses through zero, split its commission proportionally between the closing lifecycle and the newly opened remainder; never charge the full execution commission to both trades. Add an explicit reconciliation notice/counter for skipped orphan exits. Because `TradeLedger.tsv` is merge-only, provide a bounded rebuild/reset path after deploying the fix so already-corrupt rows do not survive. Source repro: an orphan `BuyToCover` at 2026-07-15 14:13:54 was treated as a long entry, later combined with a real long, and fabricated a Sim101 `-$238` lifecycle; three accounts became a false `-$713` fleet loss and `-$1,374.50` Journal net. | Fixtures: replay starts mid-long with `Sell`; replay starts mid-short with `BuyToCover`; journal reset while positioned; ordinary long/short; scale-in/out; reversal through zero with nonzero commission. Orphan exits create no trade and no residual state; reversal commissions sum exactly once across the two lifecycles. Sum of reconstructed per-account realized PnL reconciles to NT Trade Performance for the same account/time scope and largest loss cannot exceed the authoritative execution-derived lifecycle. Rebuild removes the known false rows. F5 compile green. |
| GL-056 | **P0 — canonical instrument metadata and PnL units. Depends on GL-055 for trustworthy Journal proof.** Backport the shared portion of rail R01/GL-025. | Port `GlitchInstrumentMetadataService` as the one authority for instrument-root normalization, full-contract registration/resolution, point value, tick size, session template, front-contract suffix, and ATR-to-ticks conversion. Route `GlitchReplicationEngine`, Analytics, and Journal USD normalization through it. Unknown point value must be visible and omitted/fail closed in currency aggregation—never silently treated as `$1/point`. Include `InstrumentFullName` in the persisted analytics cache. Do not port AI snapshot registry consumers. | Table-driven tests for MNQ/NQ/MES/ES/M2K and unknown roots; root/full-contract parity; rollover suffix boundary; correct tick/point values; ATR ticks; Journal dollar conversion. Unknown instruments emit one clear warning and cannot silently alter PnL. F5 compile green. |
| GL-057 | **P1 — Analytics bridge identity, freshness, and richer readings. Depends on GL-056.** | Selectively port the non-AI changes in `GlitchAnalyticsBridge`, `GlitchAnalyticsFeedBus`, `GlitchAnalyticsBridgeCacheStore`, and `GlitchMainWindow.AnalyticsTab`: preserve full contract identity alongside root; normalize UTC/local/unspecified timestamps once; clamp implausible future timestamps; retain per-timeframe freshness; publish/cache OHLCV, DI+/DI-, CCI, and MACD histogram; display ATR in ticks using metadata. Keep the existing user-facing Analytics tab and four-timeframe behavior. Exclude `GlitchAiMarketIngest`, raw AI snapshot JSON, historical corpus export, and model-oriented packet code. | One 1m/5m/15m/60m fixture survives AddOn/indicator reload with correct contract and UTC freshness. A stale or future-dated frame is visibly stale, not silently current. Rich fields are finite or null, never NaN/Infinity. Existing Glitch score and Analytics rendering remain backward-compatible. F5 compile and a four-chart/timeframe runtime check green. |
| GL-058 | **P0 — generic replication route and close safety. Independent of AI.** Extract from the current `ai-rail` worktree; do not cherry-pick AI dependencies. | In replication configuration, exclude `IsMasterRow` and any follower whose account name equals the master. For copied close executions, resolve the follower's actual net exposure, clamp quantity to closable exposure, and skip a close when already flat so `Sell`/`BuyToCover` can never cross zero into a reverse position. Include execution identity in retry keys so separate fills do not collide. Journal one deterministic skip/failure reason per event. Preserve ratios and manual trading behavior. | Tests: master row never self-copies; duplicate master-as-follower is ignored; already-flat follower receives no reverse order; oversized close is clamped; separate execution IDs retry independently; 1:1 and non-1:1 ratios remain correct; manual master entry/exit still replicates. F5 compile plus one Sim group entry/exit proof green. |
| GL-059 | **P0 — fleet Flatten All truth. Can land with GL-058.** | Backport configured-account resolution from the rail worktree: retain every configured account name as intended scope, resolve connected `Account` objects separately, use NT `Account.Flatten` so working orders are cancelled before remaining positions close, and report unresolved/disconnected configured accounts as incomplete—never as flat. Metrics must distinguish resolved and unresolved account counts. No AI executor dependency. | Connected flat accounts pass; protected positions cancel orders then flatten; disconnected/unresolved configured account makes the operation explicitly incomplete; zero resolved accounts cannot report success; no stale bracket can reopen a position after flatten. F5 compile and disconnect fixture green. |
| GL-060 | **P1 — follower-native protection generalized for manual/non-AI replication. Depends on GL-058.** The rail implementation proves one- and two-leg mechanics but is currently coupled to `GlitchAiOrderExecutor` and `GLT-AI-*`; port the design, not that coupling. | Introduce a runtime-neutral protection plan owned by the replication engine. When a complete master bracket set is positively observable, each copied follower entry receives matching account-local OCO pairs with per-leg quantities scaled by the configured ratio. Every leg needs a distinct OCO ID; one target fill must cancel only its sibling stop. Do not also copy master protection fills onto followers with native brackets. Catch-up may clone protection only when the complete master set is known; otherwise remain on existing manual-copy behavior with a visible degraded-protection notice. Unsupported partial fills/exits must fail closed, retain protection, or resize safely—never cancel protection first. | Manual master ATM entries with one and two targets create correctly sized protected followers without any AI service loaded. TP1 reduces only its leg while remaining stop/target coverage equals residual exposure. Stop, TP2, master close, reconnect catch-up, partial fill, fractional/integer ratios, follower rejection, and disconnect fixtures cannot leave a follower open and unprotected or reverse it after exit. Existing manual traders without an observable complete bracket retain current behavior. F5 compile plus protected Sim proof green. |
| GL-061 | **P1 — Journal scope semantics. Depends on GL-055–056.** The rail's fleet aggregation is useful but its current unlabeled `Net PnL` presentation is not mainline-ready. Completes carryover GL-039. | Keep per-account authoritative round trips, then expose an explicit `Master / Group / Fleet` scope and basis label. Group/fleet aggregation may sum account-local USD PnL only after scope selection; never compare a three-account fleet total to a single-account NT report under the same unlabeled heading. Show both logical group trades and account-trade count where relevant. Preserve commissions and instrument point values. | For a 1:1 three-account group, Master equals Sim101, Group equals the exact sum of enabled group members, Fleet equals all selected groups, and changing scope changes both label and metrics. Counts and PnL reconcile to exported ledger rows and matching NT account/time filters. No double counting from five-second bucket collisions. |
| GL-062 | **P1 — prop-rule data correction candidate. Independent.** | The rail updates Apex Legacy consistency from 50% to 30% and adds an official source URL/date. Before porting, reverify the exact program/status applicability against the current official Apex rules, then update `PropFirmRules.json` and regenerate the bundled C# resource from the canonical generator. Do not hand-edit only the generated bundle. | Official source, applicability, and verification date recorded; JSON/generated bundle parity test passes; unrelated firms/rules unchanged; UI renders the corrected rule for the intended Apex status only. |

**AI-rail reference status:** At `d7975fb`, GL-055's orphan-exit guard, reversal-commission allocation, and focused `BuyToCover`/`Sell` regression fixtures are committed; the complete AddOn was deployed and the operator reported a green NinjaTrader compile after the reset. Fresh post-reset Journal-to-NT reconciliation remains open. GL-053's local two-leg bracket slice is also committed and exposes additional acceptance requirements for GL-060; it is AI-only and is not itself a mainline backport. The 3-contract Sim bracket proof remains open. Source fixes do not silently rewrite runtime history.

**Recommended mainline order:** GL-055 -> GL-056 -> GL-058 + GL-059 -> GL-057 -> GL-060 -> GL-061. GL-062 is an independent verified-data patch. Each ticket should be implemented on a clean branch from `main`; the current `ai-rail` worktree is evidence/reference, not a cherry-pickable release commit.

**Explicit non-backports:** all `Services/Ai/*`; Hermes gateway/profile/session/cron work; AI Auto controls and AI Feed tab; intent schemas and policy stores; telemetry/intent HTTP endpoints; portfolio/market model packets; replay/corpus writers; model prompts, skills, memory, and learning loops. Those remain on the AI program rail.

## Archive

Pre-v19 waves, handoffs, and audits: `docs/ledger/audits/` · `docs/ledger/handoffs/` · `docs/ledger/research/`

Delegation map retired 2026-07-09; use rail + parallel Hermes sessions.
