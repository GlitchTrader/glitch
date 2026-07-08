# Glitch Ledger Log

Append-only operator log. Newest first.

## 2026-07-08 — Cursor honest-copy Phase 0

- **P0-1 (compile pending):** `TryExecuteFlattenAllAsync` uses `account.Flatten(instruments)` per account only; journals `flatten_all|origin=user_button|result=...`; incomplete flatten raises Critical `FlattenAllIncomplete` without resubmit.
- **P0-2 (compile pending):** `EnforceStrategyComplianceActions` setting (default false, persisted); max-contracts and no-protection strategy-path flattens/freeze only when enabled.
- **P0-3 (compile pending):** Replication starts OFF each session (`_isReplicatingUi` forced false on load); user click journals `replication_enabled|origin=user_click`.

## 2026-07-08 — INCIDENT + deep audit → "Honest Copy" rewrite ordered (architect: Fable)
- **Incident (sim, journal-proven):** Flatten All left the user's ATM Stop1 working → filled → Sim101 long 2 unsolicited → 500ms absolute sync bought 4+6 on followers + planted emergency stops → header showed unlabeled fleet PnL (+$72 unrealized → +$2 realized). Full reconstruction: `audits/fable-deep-audit.md` §1.
- Census: 257 fallback occurrences, 80 empty catch blocks, ~30 distinct order-path compensating mechanisms, 2,367-line replication partial on an 8,133-line god window. Verified Cursor's `cursor-deep-audit.md` and corrected it (missed: flatten-leaves-brackets root cause, frozen-account live stops, confirmed_Working bug, runtime/source drift).
- Operator decree recorded: **no Glitch-initiated action without user initiation or explicit granular opt-in; compliance = display math by default; every automatic action journaled with its authorizing setting.**
- Decision: stop patching guards; rewrite order path event-driven (GlitchCopyEngine), one flatten primitive (`account.Flatten`), drift reported never auto-corrected. Backlog Wave 8 (GL-036…041); **Wave 7 AI program frozen until GL-041 verification gate passes.** Handoff: `handoffs/2026-07-08-cursor-honest-copy.md` (Phase 0 same-day).
- Process finding D-10: running NT binary ≠ workspace HEAD (emergency_stop fired live but has no callers in source). All verification must state the binary's commit.

## 2026-07-08 — Replication storm RCA + hotfix (Sim101/102/103)

- **Incident:** Manual 2 MNQ on Sim101 + replicate (2×/3×) + Flatten All produced ~5 follower SL round-trips and ~$520 loss vs expected ~$39–52 scaled loss. Journal + TradeLedger prove buy → missing-master-protective emergency stop → freeze cleared on compliance refresh → rebuy loop.
- **RCA:** `docs/ledger/audits/replication-storm-rca-2026-07-08.md`. Root: `_replicationFrozenKeys` cleared by `ClearComplianceEnforcementRuntimeState` when compliance off; emergency stop on manual copy without master bracket; RP-2 double `ExecuteReplicationCycle`.
- **Fix (deployed to bin\\Custom source):** split `_replicationEngineFrozenKeys`; skip protective breach on manual copy; replication on light ticks only; F2/F4; **group Size/Master live-sync**; burst/protective/in-flight fixes; indicator publish throttle. **Operator:** recompile AddOn + Indicator; run Scenario A–D in `ponytail-audit-2026-07-08.md`.

## 2026-07-08 — Tradovate/Apex instrument universe (operator)

- Captured 148 Tradovate/Apex symbols (108 futures, 40 spreads) for future bridge/normalize/ingest/mining work. Operator intent: export ~2y normalized history via bridge (same units as realtime), mine, then connect live ingest. Catalog: `docs/ai-program/tradovate-apex-instrument-universe.md`.

## 2026-07-08 — Cursor trust-v0019

- **NT8 compile: PASS** (Alan, 2026-07-08) on branch `glitch/trust-v0019` after GL-014 CS1628/CS0019 fix (`6d2d716`). Runtime acceptance per `audits/trust-v0019-changes.md` still open.
- GL-020: `MarshalAccountRefreshResult` dispatcher catch → `RecordSubsystemFault("account_refresh", ex)`; finally/coalesce unchanged.
- GL-021: empty-account header zeroed; final shell publish on no accounts.
- GL-023: gitignore export artifacts.
- GL-024: F1 commission truth (net journal PnL + reconcile notice).
- GL-022: SHA-256 checksum manifest + `npm run checksums`.
- GL-014: per-account-type compliance settings granularity.

## 2026-07-08 — AI-program architecture pass (architect: Fable → implementer: Cursor)

- Operator decree: Fable architects/documents, Cursor implements exactly what is planned. Goal: improve v0.0.1.9, then v0.0.2.0+ with AI progressively integrated — more assets, better bridge/normalized analytics, then ingest → mine → backtest → learn → 5-min BUY/SELL/HOLD/NOTHING intents with **mandatory SL+TP1 (optional TP2/SL2), NT-held OCO brackets**, Glitch deterministic firewall before any order.
- Wrote `docs/ai-program/roadmap.md` — version ladder v0.0.1.9 "Trust" → v0.0.2.0 "Eyes" → v0.0.2.1 "Voice" → v0.0.2.2 "Ears" (paper) → v0.0.2.3 "Hands-sim" → v0.0.2.4 "Hands-eval", plus Hermes H-0/H-1/H-2 and the 15-step firewall chain.
- Wrote intent contract v2 (bracket mandate): `glitch_hermes_docs/docs/09_intent_contract_v2_brackets.md` + `schemas/intent.v2.schema.json`. AI never manages a loss mid-flight; naked positions impossible by construction.
- Backlog: Wave 6 (GL-020…GL-024, v0.0.1.9 hardening incl. RP-1 catch + F1 commission truth) and Wave 7 (GL-025…GL-035, AI program) seeded with gates; GL-008/GL-009 become umbrellas.
- Handoff for Cursor: `handoffs/2026-07-08-cursor-trust-v0019.md` (Wave A fully specified WO-A1…A6; Wave B preview only). Branch: `glitch/trust-v0019`.
- ABKB glitch project profile updated with AI-program section + pointers.

## 2026-07-08 — v0.0.1.8 post-publish review (lead: Fable)

- Full release review: `audits/v0.0.1.8-release-review.md`. Verdict: sound; one fix recommended.
- **RP-1 (P1):** wave-2 marshaled apply (`RefreshPipeline.partial.cs`) has `try/finally` without catch — exceptions in apply path (incl. `ExecuteReplicationCycle`) escape to WPF dispatcher; old timer-tick catch no longer covers it. Fix: catch → `RecordSubsystemFault("account_refresh", …)`.
- **RP-2:** replication cycle can double-fire (<500ms) and heavy-tick replication timing now rides thread-pool/dispatcher scheduling — LANE-1 must verify idempotency under the new pipeline.
- Security: shipped zip clean (compiled export only); license/HTTP surfaces pass; recommend SHA-256 next to release zips + scoped security audit before wider distribution and before Hermes servers land.
- Hermes (GL-009): read-only prep may start now per phase ladder step 2 (telemetry server + mktintel-style ingestion runtime); intent/execution path stays gated on Waves 1–2, LANE-1, and GL-005 F1 (journal must be commission-true before Hermes learns from it).

## 2026-07-08 — Performance wave 2 (refresh pipeline)

- **Light replication tick:** `heavyTabWork: false` skips `BuildAccountRow` loop — only `ExecuteReplicationCycle` + coalesced shell publish (500ms cadence no longer rebuilds all rows).
- **Background row build:** full refresh snapshots selection overrides, builds rows on thread pool, marshals apply/risk/replication/header to UI at `Background` priority; coalesces overlapping ticks; synchronous path for startup, flatten, grid edit, subsystem degrade.
- **Lock hardening:** `_peakStatesByAccount` and trade-source snapshots → `ConcurrentDictionary`; removed `_peakStateLock` / `_tradeSourceLock`.
- New: `GlitchMainWindow.RefreshPipeline.partial.cs`.
- Deployed to NT bin for F5.

## 2026-07-08 — Performance hardening PA-1…PA-9

- Implemented against current codebase (not blind handoff): HTTP 5s policy, fundamentals snapshot lock shrink, background ledger flush, subsystem fault auto-degrade, flatten submit metric, live-feed virtualization, timer Background priority + reentrancy guard, header metric skip-if-unchanged, collection caps.
- Evidence: `docs/ledger/audits/performance-hardening-pa1-pa9.md`.
- **Alan NT8 compile: PASS** (EarningsEvent type fix on snapshot scratch).
- Operator gates open: PA-5 adversarial replay numbers, PA-9 12h soak, network-pull freeze test, flatten `METRIC|flatten_submit_ms` smoke.

## 2026-07-07 — Runtime performance pass 2

- `AccountGridRow` INotifyPropertyChanged + `ApplyFrom` (stops ObservableCollection row replacement every PnL tick).
- Replication: 500ms light refresh + 3s full; dropped `AccountItemUpdate` subscription; journal batch inserts; analytics 8s throttle; account subscription resync 20s; typed `Position` scan.
- Deployed to NT bin for F5.

## 2026-07-07 — Runtime performance audit + fixes

- Operator: NT slowdown/crash pressure with Glitch open; paused accordion layout iteration.
- Audit: `docs/ledger/audits/runtime-performance-audit.md`.
- Fixes: tab-gated refresh (analytics/journal/settings), hidden-window light tick, 2s replication UI cadence, `AccountItemUpdate` throttle, shell publish coalesce, Chart Trader style once, single page-scroll accordion (removed nested scroll/MaxHeight layout), `CanContentScroll=false` on grids, localization slim-down.
- Deployed to NT bin for F5.

## 2026-07-07 — Accordion page layout (Dashboard + Journal)

- Structural redesign per operator: one **page scroll** per tab, standardized **Expander** sections (primary expanded by default), each section has **inner scroll** with viewport-based `MaxHeight`.
- New `GlitchMainWindow.AccordionLayout.partial.cs`; Dashboard groups + Connected Accounts; Journal Performance + Critical / Notice / Live Feed.
- Removed magic `MaxHeight`/`MinHeight` on accordion-hosted grids; follower per-group cap removed (section scroll owns overflow).
- Localization: `dashboard.replication_groups`.

## 2026-07-07 — Cursor wave 1 (post-compile UX iteration)

- **Alan NT8 compile pass** on `glitch/bulletproof-wave1` after compile-fix `aa251da` (CS0120 static/instance, `GridUnitType`, missing field).
- **Post-compile UX feedback (Alan):** (1) Dashboard two-tier layout still wrong — fixed bottom Connected Accounts steals height from followers when many accounts; wants **one pane**, followers star, accounts **collapse or scroll**, not compete. (2) Journal bottom sections stack tight; Expander headers show **`System.Windows.Controls.TextBlock`** (NT8 Expander renders `Header.ToString()` when Header is a TextBlock — use `BindLocalizedHeader` string headers).
- **Iteration (uncommitted):** Dashboard — single star row; groups fill; Connected Accounts in **collapsed Expander** with capped grid (`DashboardTab.partial.cs`). Journal — `BindLocalizedHeader` on Notice History + Live Feed expanders; bottom `StackPanel` with spacing; critical grid row `Auto` not `Star`; removed forced `MinHeight` on empty critical grid (`JournalTab.partial.cs`).
- **Acceptance still open:** Alan recompile + smoke per `audits/ui-calm-changes.md` before any `done` flip.

## 2026-07-07 — Cursor wave 1

- GL-014 (WO-10): settings granularity design written to `audits/ui-calm-changes.md` (`5ae3c63`); implementation deferred.
- GL-019 (WO-9): `copyTradingPolicy` schema + parser + Settings compliance notice (`d262a31`, JSON/parser in `d83b977`).
- GL-018 (WO-8): Lucid rules rebuilt EOD tiers in `PropFirmRules.json` (`d83b977`).
- GL-017 (WO-7): FundingTicks `Discontinued` + UI suffix (`d83b977`).
- GL-015 (WO-5): ratio `ConverterCulture`, hover affordance, math tooltip (`GlitchMainWindow.cs`, `7ec0ac4`).
- GL-011 (WO-6): followers-first dashboard row swap (`DashboardTab.partial.cs`, `2b5c52b`).
- GL-005/F2 (WO-11): unknown point value quiet notice (`SummaryTab.partial.cs`, `bac3046`).
- **Compile fix (post-Alan F5):** restored `_settingsLicenseKeyUnmaskedValue`; fixed `GridUnitType`→`DataGridLengthUnitType` in notice grid; `L`/`Lf` out of static helpers; `NormalizeTradesToUsd` instance method for F2 notice path.
- GL-012 (WO-4): Critical vs Notice taxonomy; notice history expander; header count critical-only (`GlitchMainWindow.cs`, `Models.partial.cs`, `7ec0ac4`).
- GL-013 (WO-3): Journal Trader Performance primary full-width; Live Feed in collapsed Expander (`JournalTab.partial.cs`, `f61bfd4`).
- GL-010 (WO-2): `MaxHeight = 240` on per-group follower DataGrids (`GlitchMainWindow.cs` `CreateGroupMembersGrid`, `b209ad1`). Pairs with WO-1 cap; groups section ScrollViewer handles many groups.
- GL-010 (WO-1): committed lead-approved `MaxHeight = 240` on connected-accounts DataGrid (`DashboardTab.partial.cs`, `d516250`). Backlog → partial (awaiting NT8 compile).

## 2026-07-07 — pass 7 (lead: Fable) — wave 1 delegated to Cursor/Composer 2.5

- Claude plan hard-rate-limited; operator redirected execution to Cursor ($60 plan, idle). Wrote `handoffs/2026-07-07-cursor-wave1.md`: WO-1…WO-11 covering GL-010/011/012/013/015 (UI calm), GL-017/018/019 (rules truth from LANE-4 findings), GL-014 design, F2 stretch fix. Branch: `glitch/bulletproof-wave1`; main stays clean; done-gate remains Alan's NT8 compile.
- Fable schedulers retooled: lane-relaunch one-shot cancelled; 3-hourly spawner replaced with a 2-hourly monitor-only pass (reviews Cursor's branch commits + ledger entries, writes lead-review-notes.md on violations).
- LANE-1 (replication audit) held for a future Opus window (or Cursor first-pass + Opus verify) — money-path audit deserves the strongest reasoning available.

## 2026-07-07 — pass 6 (lead: Fable) — scheduled pass; LANE-3's surviving edit reviewed

- 13:14 São Paulo, still pre-reset (18:10). Discovered one surviving LANE-3 edit in the working tree: `DashboardTab.partial.cs` — `MaxHeight = 240` on the connected-accounts grid (GL-010). Lead review: APPROVED — premise verified against row layout (Auto row1 grid starving star row2 followers); uses existing `ConfigureDataGridScrolling`; also advances GL-011. **Held uncommitted pending Alan's NT8 compile** per C#-gate. GL-010 → in_progress.
- F1 refined earlier this window (commit `c2d00ff`): dashboard reads NT net via account items; Journal recomputes gross; "commission" absent from entire AddOn — Glitch disagrees with itself; sim masks it, funded exposes it.
- Next event: 18:27 SP one-shot relaunches LANE-1 (opus) + LANE-3 (sonnet), incremental-write instructions.

## 2026-07-07 — pass 5 (lead: Fable) — LANE-2 landed; limit hit again

- **LANE-2 (math-audit, Opus) COMPLETE** → `audits/pnl-math-audit.md` (212 lines, 10 ranked findings). Lead spot-verified F2 (`return 1.0;` pointValue fallback at SummaryTab.partial.cs:940) — grounded. GL-005 → partial (audit done, fixes pending). Headline: arithmetic sound (screenshot reconciles to the cent) but journal PnL is gross of commissions (F1 — why Glitch ≠ NT), unknown-instrument pointValue silently 1.0 (F2 — gates GL-008), fleet-aggregated stats redefine win-rate (F3).
- LANE-1, LANE-3, LANE-4 killed by the next session-limit window (resets 18:10 São Paulo). LANE-1 and LANE-4 died AT report-writing stage; key LANE-1 finding banked: follower-cell WPF bindings lack `ConverterCulture` (en-US parse vs pt-BR display). Progress notes updated in `lane-briefs.md`.
- Relaunch scheduled shortly after 18:10 SP: LANE-1 + LANE-4 first (near-complete, cheap wins), LANE-3 after they land (2+2 stagger per contingency).
- **Correction (same pass): LANE-4's report was found COMPLETE on disk** (`research/nt8-propfirm-refresh-2026-07.md`, 184 lines, fully cited) — the agent died after writing, before reporting. GL-016 → done (research). Red flags spawned **GL-017** (FundingTicks is CLOSED since 2026-01, still shipped as Supported), **GL-018** (Lucid rules block is a byte-identical copy-paste of stale FundingTicks data), **GL-019** (no copy-trading policy encoding; TPT's own policy prohibits cross-account copy services — existential for replication; operator must confirm TPT/TradeDay policy in writing). Relaunch re-scoped: next window spawns LANE-1 + LANE-3 only, writing outputs incrementally.

## 2026-07-07 — pass 4 (lead: Fable) — lane relaunch after limit reset

- ~10:53 São Paulo: session limit confirmed reset. Relaunched all four lanes from `lane-briefs.md`: LANE-1 replication-audit (Opus), LANE-2 math-audit (Opus), LANE-3 ui-calm (Sonnet), LANE-4 external-truth (Sonnet). All running in background; outputs expected in `docs/ledger/audits/` and `docs/ledger/research/`.
- Contingency if the limit trips again mid-flight: partial-progress notes go into lane-briefs.md and lanes restagger 2+2 on the next window.

## 2026-07-07 — pass 3 (lead: Fable) — scheduled pass, honest no-op

- 10:13 São Paulo: still inside the session-limit window (resets 10:40). No lane outputs; no C# changes; nothing to integrate. No new subagents spawned (would fail against the limit and duplicate the scheduled 10:53 relaunch). Next event: one-shot lane relaunch at ~10:53 São Paulo.

## 2026-07-07 — pass 2 (lead: Fable) — session-limit recovery

- All four lanes (2× Opus audit, 2× Sonnet) were killed mid-flight by the subscription session limit (resets 10:40 America/Sao_Paulo). No output files were written; no C# was modified (verified via git status). Partial progress notes captured into `lane-briefs.md`.
- Committed previously-untracked `glitch_hermes_docs/` (AI decision-layer contract referenced by GL-009) — `ba510e7`.
- Wrote `docs/ledger/lane-briefs.md` — relaunch-ready delegation prompts so any lead can respawn lanes without reconstruction.
- Scheduled one-shot relaunch of all four lanes for shortly after the limit reset.
- Backlog status: unchanged (all items todo — honest no-op on findings).
- Blocker: subscription session limit until 10:40 São Paulo.

## 2026-07-07 — pass 1 (lead: Fable)

- Seeded ledger (README, north-star with calm-by-default invariant, backlog GL-001…GL-016 with dependency graph + delegation map) — `e1f0ac7`, pushed.
- Spawned four lanes: replication-audit (Opus), math-audit (Opus), ui-calm (Sonnet), external-truth (Sonnet).
- Created recurring 3-hourly lead operator pass (session-scoped cron).
