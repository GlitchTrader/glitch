# Glitch Ledger Log

Append-only operator log. Newest first.

## 2026-07-07 — Cursor wave 1

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
