# Glitch Backlog

Seeded 2026-07-07 from operator dictation (user-reported bugs + expansion plan); expanded same day with screenshot-grounded user complaints (GL-010…GL-016). Statuses: `todo | in_progress | partial | done | deferred`. Flip status only with evidence (test, repro, user confirmation).

Key files: `ninjatrader/Glitch/AddOns/GlitchAddOn/` — `UI/MainWindow/GlitchMainWindow.cs` (7.9k lines, monolith), `GlitchMainWindow.Replication.partial.cs` (2.2k), `Services/Trading/GlitchReplicationEngine.cs`, `Services/Risk/GlitchComplianceEngine.cs`, `Services/Insights/GlitchTradeLedgerService.cs` + `GlitchTradeInsightsService.cs` (journal math), `UI/MainWindow/GlitchMainWindow.SettingsTab.partial.cs`, `GlitchMainWindow.JournalTab.partial.cs`.

## Wave 1 — Audit (gate for everything else)

| ID | Title | Status | Notes |
|----|-------|--------|-------|
| GL-002 | First-principles audit of entire Glitch codebase | todo | Consistency check before any bug fix or feature. Scope: replication engine, compliance engine, PnL/analytics math, feed bus normalization, persistence. Output: audit findings doc per service group with evidence. |

## Wave 2 — Bugs (user-reported)

| ID | Title | Status | Notes |
|----|-------|--------|-------|
| GL-001 | Replication drift/delays master↔followers; Glitch PnL ≠ NinjaTrader PnL | todo | "Needs to be fully wired." Two symptoms, possibly one root cause (event ordering / reconciliation). Acceptance: side-by-side NT vs Glitch PnL equality on live sim session; follower order latency measured and bounded. |
| GL-004 | No loops, fake orders, or duplicated orders in replication | todo | Acceptance: adversarial test session (rapid entries/exits, partial fills, disconnect/reconnect) produces zero phantom/dup orders in journal. |
| GL-005 | Dashboard PnL calculations and analytics accuracy | partial | **Audit complete** (`audits/pnl-math-audit.md`, 2026-07-07, LANE-2/Opus, 10 ranked findings; F2 spot-verified by lead). Arithmetic itself sound — screenshot reconciles to the cent. **F1 fix landed GL-024 (2026-07-08):** execution `[COMM:]` token → `CommissionTotal` on ledger rows → net USD display + `journal_reconcile_divergence` notice vs NT realized. Remaining: **F2** unknown instruments silently fall back to pointValue 1.0 (`SummaryTab.partial.cs:940` — gates GL-008); **F3** fleet-aggregated stats + 5s bucket straddle; then F4 (PF=0 on all-wins), F6 (session tags use machine-local not exchange time), F10 (500ms de-dup may drop fast fills — coordinate w/ LANE-1). Testability plan: extract `Glitch.Insights.Math` (NT-independent) + ~30 unit tests. |
| GL-003 | Prop-firm compliance fully wired, configurable, opt-in per feature on Security tab | todo | No sudden behaviors the user cannot individually control. Each compliance feature: visible toggle + clear description + default documented. |

## Wave 3 — Improve (UI/UX — user complaints, screenshot-grounded 2026-07-07)

| ID | Title | Status | Notes |
|----|-------|--------|-------|
| GL-006 | Less warnings, less noise; simplify screens | todo | Umbrella item; concretized by GL-010…GL-015 below. |
| GL-010 | Scrollable panels for long lists | partial (awaiting NT8 compile) | Dashboard groups must handle 20 accounts × 20 followers: ScrollViewer with fixed headers on Connected Accounts and follower tables; window must never clip rows. Files: `GlitchMainWindow.SummaryTab.partial.cs`, `.Replication.partial.cs`. Acceptance: 20+20 rows fully reachable at 1280×900. |
| GL-011 | Dashboard: followers block is the star | partial (awaiting NT8 compile) | Connected Accounts must not steal height from replication groups (no fixed bottom tier with 20 accounts). Followers own the star row; Connected Accounts in collapsed expander with internal scroll cap. Acceptance: follower groups visible without scrolling on open; accounts reachable via expander + grid scroll. |
| GL-012 | Warnings doctrine: calm by default | partial (awaiting NT8 compile) | Journal "Critical Warnings" panel produces mostly false positives; red/orange flashes stress traders. Implement warning taxonomy: `critical` (imminent breach/lock — red, persistent, rare) vs `notice` (quiet log, no color, no flash). Kill false-positive sources; move non-critical to a quiet history view. Acceptance: a normal profitable/losing sim session produces ZERO red elements. See north-star "Calm by default" invariant. |
| GL-013 | Journal: performance first, live feed demoted | partial (awaiting NT8 compile) | Trader Performance table is the value; Live Feed is secondary. Reorder/resize; consider Live Feed collapsed by default. File: `GlitchMainWindow.JournalTab.partial.cs`. |
| GL-014 | Settings granularity | partial (awaiting NT8 compile) | Settings tab has only 4 coarse risk checkboxes. Expand into per-feature, per-account-type (Sim/Eval/PA) controls with numeric thresholds visible/editable — this is the UI face of GL-003 (opt-in compliance on the Security tab). Files: `GlitchMainWindow.SettingsTab.partial.cs`, `GlitchRuntimePolicyStore.cs`. |
| GL-015 | Editable cells must look editable; ratio/size clarity | partial (awaiting NT8 compile) | Follower table: users can't tell which cells (Ratio, Max DD, Max L, Max C, Size) are editable — add chevron/underline/hover affordance; show resolved ratio math (master size × ratio = follower contracts) inline or on hover. File: `GlitchMainWindow.Replication.partial.cs`. |

## Wave 4 — Ship

| ID | Title | Status | Notes |
|----|-------|--------|-------|
| GL-007 | Exercise distribution loop with waiting test users | todo | Pipeline exists: copy AddOn folder to NT bin → recompile → re-export → push to repo → version live → users update. Acceptance: one full cycle delivered to ≥1 external test user with in-app update. |

## Wave 5 — AI expansion (blocked by Waves 1–2)

| ID | Title | Status | Notes |
|----|-------|--------|-------|
| GL-008 | Multi-asset bridge + analytics | todo (umbrella) | **Concretized 2026-07-08 as GL-025/GL-026 in Wave 7** — see `docs/ai-program/roadmap.md` (v0.0.2.0 "Eyes"). |
| GL-009 | Hermes decision layer (5-min loop: BUY/SELL/HOLD/NOTHING → deterministic Glitch checks) | todo (umbrella) | **Concretized 2026-07-08 as GL-027…GL-035 in Wave 7** with version ladder in `docs/ai-program/roadmap.md`. Intent contract v2 (bracket-mandatory: SL+TP1 required, optional TP2/SL2, NT-held OCO) in `glitch_hermes_docs/docs/09_intent_contract_v2_brackets.md` + `schemas/intent.v2.schema.json`. Paper mode first, always. |

## Wave 6 — v0.0.1.9 "Trust" (post-release hardening — from `audits/v0.0.1.8-release-review.md`)

| ID | Title | Status | Notes |
|----|-------|--------|-------|
| GL-020 | RP-1: catch in marshaled account-refresh apply | partial (awaiting NT8 compile) | `RefreshPipeline.partial.cs` → `MarshalAccountRefreshResult`: dispatcher action is `try/finally` with no catch; exceptions in `ApplyFullAccountRefreshResult` (incl. `ExecuteReplicationCycle`, `ApplyRiskMitigations`) escape to the WPF dispatcher → potential NT8 crash. Fix: wrap body in try/catch → `RecordSubsystemFault("account_refresh", ex)`. Restores PA-4 degrade doctrine. Acceptance: injected throw inside apply path degrades subsystem quietly, NT stays up. |
| GL-021 | RP-3: stale header/shell when all accounts disconnect | partial (awaiting NT8 compile) | `UpdateHeaderMetricsFromRows` early-returns on empty rows (header freezes at last PnL); `RefreshAccountDataLight` early-return skips `PublishGlitchShellState` (Chart Trader widgets hold stale replication state). Zero/neutral the header metrics and publish a final shell snapshot when active accounts drop to none. |
| GL-022 | Release integrity: SHA-256 checksums | partial (awaiting NT8 compile) | Generate SHA-256 alongside every zip in `apps/download/public/files/` (build-time script), publish on the download page. Backfill v0.0.1.8. |
| GL-023 | Repo hygiene: stray export artifacts | partial (awaiting NT8 compile) | gitignore `ninjatrader/Glitch/Glitch.zip` and `Glitch Screens *.{jpg,psd}` (or relocate to an untracked assets dir). Release zips live only under `apps/download/public/files/`. |
| GL-024 | F1: commission truth — journal must match NT net PnL | partial (awaiting NT8 compile) | Closes GL-005/F1 per `audits/pnl-math-audit.md`: dashboard tiles read NT account items (net when commission template set) while Journal/Analytics recompute gross from PnlPoints; "commission" appears 0× in the AddOn. Fix at the seam (audit's two options): source Journal/Analytics totals from the same NT account items as the tiles, OR feed commissions into the trade ledger. North-star: "what Glitch displays must equal what NinjaTrader reports." **Hard gate for GL-030 — Hermes must never learn from gross-vs-net-corrupted journal data.** |

## Wave 7 — AI program (v0.0.2.x + Hermes; roadmap: `docs/ai-program/roadmap.md`)

| ID | Title | Status | Target | Notes |
|----|-------|--------|--------|-------|
| GL-025 | Instrument metadata registry + multi-asset bridge normalization | todo | v0.0.2.0 | New `GlitchInstrumentMetadataService`: point value, tick size, session template, currency from NT `MasterInstrument` with cited static fallback table; **kills the F2 pointValue=1.0 silent fallback**. Bridge/FeedBus publish normalized units (ticks, ATR-relative, R-multiples) keyed by instrument root. Child of GL-008. |
| GL-026 | Analytics panel: normalized multi-instrument view | todo | v0.0.2.0 | Analytics tab renders per-instrument normalized snapshot (same indicator set, comparable units across MNQ/NQ/ES/MES…), instrument selector, calm-by-default. Child of GL-008. |
| GL-027 | GlitchExternalTelemetryServer (read-only) | todo | v0.0.2.1 | `HttpListener` on `127.0.0.1:8787`, bearer token (generated first-run, stored GlitchData, shown once in Settings). GET `/health` `/snapshot?instrument=` `/accounts` `/positions` `/risk` `/journal/recent`. Serves from existing snapshot builders off the UI thread. Output validates against `glitch_hermes_docs/schemas/snapshot.schema.json`. **Gate: GL-034 design review first.** |
| GL-028 | Hermes runtime scaffold + ingestion (H-0) | todo | Hermes repo | Separate repo `projects/glitch/Hermes`, mktintel-style living-knowledge layout (observations/knowledge/experiments/operations + phase gates + AGENTS.md) + Docker `hermes-api`/`hermes-worker`/Postgres. Only job: `ingest_snapshot` every 5 min during NT sessions. No decision code. |
| GL-029 | Pattern mining + backtest harness (H-1) | todo | Hermes repo | Mine accumulated corpus + `Glitch-Collab`/`Strategy-Research-Data` historical sets; replay harness scoring candidate rules against M0 risk profile. Output: ranked archetypes + evidence docs, mktintel campaign format. |
| GL-030 | Intent endpoint paper mode: models v2 + AI risk firewall | todo | v0.0.2.2 | `GlitchAiIntentServer` (POST /intent), `GlitchAiIntentModels` (contract v2, bracket-mandatory), `GlitchAiRiskFirewall` (15-step deterministic chain in roadmap). Paper mode: validate + journal + respond; **no order code path exists in this version**. Gates: Waves 1–2 complete + GL-024 landed. |
| GL-031 | GlitchAiJournalBridge | todo | v0.0.2.2 | One correlated record per intent: intent → firewall verdict (per-check) → (later) orders/fills/round-trip PnL net of commissions → snapshot hash. This is Hermes's training data; shape is contract, version it. |
| GL-032 | GlitchAiOrderExecutor (Sim101, bracket-mandatory) | todo | v0.0.2.3 | Entry + NT-held OCO bracket submitted atomically; bracket-attach failure ⇒ entry cancelled (naked position impossible by construction). TP1 fill ⇒ Glitch moves runner stop to SL2 deterministically. Signal names `GlitchAIEntry/Stop/Target`. Sim101 only; separate from replication path entirely. Gate: ≥2 weeks clean paper intents. |
| GL-033 | Live eval enablement (M0) | todo | v0.0.2.4 | One allowlisted eval account. M0 caps: MNQ only, 1 contract, $100/trade, $300/day, 3–5 trades/day, cooldowns, kill switch in Settings. Gates: paper-profitable per M0 criteria + operator approval + GL-034 full audit. |
| GL-034 | Security audit (two-stage) | todo | gates GL-027, GL-033 | Stage 1 (before GL-027 ships): design review of telemetry/intent server spec — bind, auth, allowlists, DoS bounds. Stage 2 (before GL-033): full audit of apps/api license endpoints, download/update flow, both AI servers. |
| GL-035 | Hermes decision routine (H-2, 5-min loop) | todo | Hermes repo | `suggest_trade`: reads latest snapshot, emits ≤1 intent/instrument/cycle per contract v2 with mandatory brackets. `daily_learning` post-session from GL-031 data. Gate: GL-030 live in paper mode. |

## Wave 1b — External truth (parallel with audit)

| ID | Title | Status | Notes |
|----|-------|--------|-------|
| GL-016 | NT8 + prop-firm rules refresh | done (research) | **Report landed:** `research/nt8-propfirm-refresh-2026-07.md` (LANE-4, 2026-07-07, fully cited). Follow-up: re-verify 403-blocked official pages with a real browser session. Spawned GL-017/018/019 below. |

## Wave 2b — Compliance truth (from GL-016 red flags — operator attention required)

| ID | Title | Status | Notes |
|----|-------|--------|-------|
| GL-017 | Remove FundingTicks as a live firm | partial (awaiting NT8 compile) | **Firm CLOSED 2026-01** (wind-down announced 2026-01-18). `PropFirmRules.json` still ships it as `Supported` with lastVerifiedDate 2024-01-15. Remove from selectable firms / mark Discontinued in UI + rules JSON. |
| GL-018 | Rebuild Lucid Trading rules from real data | partial (awaiting NT8 compile) | Encoded Lucid block is a **byte-identical copy-paste of the stale FundingTicks block** — fabricated data shipped under Lucid's name. Real Lucid 2026: EOD trailing drawdown, 4 programs (Flex/Pro/Direct/Maxx), 90/10 split since 2026-03, per-program consistency 0%/40%/20%. Rewrite entry from cited sources + firm confirmation. |
| GL-019 | Encode per-firm copy-trading/automation policy; same-owner replication guard | partial (awaiting NT8 compile) | **Existential:** TPT's own Trade Copier Policy prohibits cross-account copy services ("coordinated trading"); TradeDay sources conflict (UNVERIFIED); Apex allows only same-owner, single-master, same-direction (~20 acct cap); Lucid allows copiers but bans cross-account hedging. Rules schema has NO representation of this. Add policy fields to `PropFirmRules.json`, surface in UI at firm selection, and gate replication defaults per firm. **Operator: confirm TPT + TradeDay policy in writing (support ticket) before any marketing of replication for those firms.** Also: Apex 4.0 (2026-03) tier numbers need reconciliation; Apex metals halted 2026-03-14 (no instrument-exclusion mechanism exists). |

## Dependencies

```text
GL-002 → gates → GL-001, GL-003, GL-004, GL-005
GL-016 → informs → GL-003 (compliance must encode CURRENT firm rules)
GL-001/004/005 → gate → GL-007 (don't ship known-broken replication/PnL to test users)
GL-002 + Wave 2 → gate → GL-008, GL-009
Wave 3 (GL-010…GL-015) runs parallel with Waves 1–2 (UI-only, disjoint files)

Wave 6/7 (2026-07-08, roadmap in docs/ai-program/roadmap.md):
GL-020…GL-024 (v0.0.1.9)      → start now, no gate
GL-025/GL-026 (v0.0.2.0)      → after v0.0.1.9 ships; read-only analytics work, allowed pre-audit
GL-034 stage 1                → gates → GL-027 (telemetry server ship)
GL-027 → gates → GL-028 (H-0 ingestion needs the API)
GL-028 + ≥4 weeks corpus      → gate → GL-029
Waves 1–2 complete + GL-024   → gate → GL-030/GL-031 (paper intents; F1 first — journal must be commission-true before Hermes learns)
GL-030 clean ≥2 weeks         → gate → GL-032 (Sim101 execution)
GL-032 + M0 paper-profitable + GL-034 stage 2 + operator approval → gate → GL-033 (live eval)
GL-030 → gates → GL-035 (Hermes decision routine)
```

## Delegation map (2026-07-07, Fable as lead)

| Lane | Agent | Items | Output |
|------|-------|-------|--------|
| replication-audit | Opus subagent | GL-002 (Trading/Risk slice) → findings for GL-001/GL-004 | `docs/ledger/audits/replication-audit.md` |
| math-audit | Opus subagent | GL-002 (Insights slice) → findings for GL-005 | `docs/ledger/audits/pnl-math-audit.md` |
| ui-calm | Sonnet subagent | GL-010…GL-015 implementation | C# patches + `docs/ledger/audits/ui-calm-changes.md` |
| external-truth | Sonnet subagent | GL-016 | `docs/ledger/research/nt8-propfirm-refresh-2026-07.md` |

Compile verification: NinjaScript compiles inside NT8 (F5 / import). Agents deliver patches + static reasoning; operator (or Fable via distribution script) copies to NT bin and compiles before any status flips to done.
