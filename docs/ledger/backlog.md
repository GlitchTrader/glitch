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
| R21 | — | v28 | Shadow + VPS 24/7 | todo |
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

## Archive

Pre-v19 waves, handoffs, and audits: `docs/ledger/audits/` · `docs/ledger/handoffs/` · `docs/ledger/research/`

Delegation map retired 2026-07-09; use rail + parallel Hermes sessions.
