# Glitch Backlog

**Active work:** `docs/ai-program/operating-system-rail.md` (R01–R23) on branch **`glitch/ai-rail`**.  
**User releases:** `main` only — v0.0.1.x (`docs/ledger/branching.md`).
**Status values:** `todo | in_progress | partial | done | deferred`. Flip only with evidence.

**v0.0.1.9 baseline (2026-07-09):** Trust + stable + **non-AI operator**. Shipped `Glitch_v0.0.1.9.zip`. Wave 6 + Honest Copy + session hardening closed below. New work = rail steps only.

Key paths: `ninjatrader/Glitch/AddOns/GlitchAddOn/` · `Indicators/glitch/GlitchAnalyticsBridge.cs`

## 2026-07-17 clean shared-core candidate

Branch `cleanup/main-core` implements the non-AI shared-core work without importing
Hermes, AI services, intent endpoints, prompts, sessions, or the AI Feed:

- producer-neutral master-to-follower routes, ratios, native follower OCO protection,
  exact execution deduplication, explicit resync, and cross-zero refusal;
- one truthful Replicate state, Replicate OFF preserving existing protection, observe-only
  startup, and one native Flatten All path that reports unresolved accounts as incomplete;
- Journal orphan-exit/reversal-commission repair, canonical instrument metadata, richer
  Analytics identity/freshness fields, and explicit Master/Group/Fleet Journal scope;
- corrected Apex Legacy 30% consistency metadata and directional-only metadata. Automation
  eligibility is not a Glitch execution gate.

Twenty focused source checks pass, including the absence of an automation-eligibility gate,
async protection rejection, independent
multi-leg stop identity, and live metadata replacing a stale negative cache. NinjaTrader F5 and bounded Sim fixtures remain mandatory before merge,
deployment, packaging, or any claim that the runtime is fixed. The tables below describe the
shipped v0.0.1.9 baseline and historical rail; this section is the current candidate status.

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
| R01 | GL-025 | v20 | Instrument metadata registry | todo |
| R02 | GL-026 | v20 | Multi-asset bridge normalization | todo |
| R03 | — | v21 | Market snapshot writer (file) | todo |
| R04 | — | v21 | Portfolio snapshot writer (file) | todo |
| R05 | — | v21 | Historical exporter (same schema) | todo |
| R06 | GL-029 | H-1 | Pattern mining / backtest (parallel) | todo |
| R07 | GL-027 | v21 | Telemetry server (localhost GET) | todo |
| R08 | GL-030 | v22 | Intent endpoint (paper only) | todo |
| R09 | GL-030 | v22 | AI risk firewall | todo |
| R10 | GL-031 | v22 | AI journal bridge | todo |
| R11 | GL-035 | v22 | Hermes suggest_trade → paper | todo |
| R12 | GL-032 | v23 | Sim101 bracket executor | todo |
| R13 | GL-029 | v23 | Replay harness / archetype proof | todo |
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

---

## Archive

Pre-v19 waves, handoffs, and audits: `docs/ledger/audits/` · `docs/ledger/handoffs/` · `docs/ledger/research/`

Delegation map retired 2026-07-09; use rail + parallel Hermes sessions.
