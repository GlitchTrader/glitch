# Handoff → Cursor / Composer 2.5 — Glitch Bulletproof Wave 1

**From:** Fable 5 (lead agent) · 2026-07-07 · **To:** Cursor session (Composer 2.5 + subagents)
**Repo:** `D:\ab\projects\Glitch\Glitch-Platform` · **Branch: create `glitch/bulletproof-wave1` and do ALL work there** (main stays clean until Alan compiles).

## Orientation (read these first, in order — 10 min)

1. `docs/ledger/north-star.md` — program order AUDIT→FIX→IMPROVE→SHIP→AI + product invariants. **"Calm by default" is law: a false-positive warning is itself a bug.**
2. `docs/ledger/backlog.md` — GL items, statuses, acceptance criteria, key file map.
3. `docs/ledger/audits/pnl-math-audit.md` — completed math audit (findings F1–F10).
4. `docs/ledger/research/nt8-propfirm-refresh-2026-07.md` — completed prop-firm/NT8 research (red flags: FundingTicks closed; Lucid rules fabricated; copy-trading policy gaps).
5. `docs/ledger/lane-briefs.md` — LANE-3 brief + progress notes (you are absorbing LANE-3's work).

## Non-negotiable rules

- **You cannot compile.** NinjaScript compiles only inside NT8 (Alan presses F5). Mirror WPF patterns already present in each file; re-read every edit in context; when uncertain about an NT8 API, do what the neighboring code does.
- **NEVER write under `C:\Users\alan\Documents\NinjaTrader 8\`** (managed runtime; read-only evidence at most).
- User-facing strings go through **GlitchLocalizationService / Localization.tsv exactly as neighboring labels do** (UTF-8 — never break encoding; check how the nearest existing label resolves its text).
- **Culture discipline:** every user-input parse and display format must be culture-explicit. Machine is pt-BR (comma decimals); WPF bindings default to en-US. Known defect: follower-table editable-cell bindings lack `ConverterCulture` (see WO-5).
- One GL item per commit, message format: `GL-0XX: <what> (compile pending)`. Surgical edits, no refactors, no drive-by cleanup.
- After each item: update its status in `docs/ledger/backlog.md` (todo → `partial (awaiting NT8 compile)`) and append one line to `docs/ledger/log.md` under a `## 2026-07-07 — Cursor wave 1` heading. **The ledger is how the lead monitors you.**
- Finish with `docs/ledger/audits/ui-calm-changes.md`: per item — files/lines changed, what Alan must verify after compiling, anything you deliberately did NOT do and why.

## Work orders (strict order; stop and note rather than guess)

**WO-1 · Commit the surviving edit.** The working tree already contains one lead-approved edit: `UI/MainWindow/GlitchMainWindow.DashboardTab.partial.cs` — `MaxHeight = 240` + GL-010 comment on the connected-accounts DataGrid. First commit on your branch (`GL-010: cap connected-accounts grid height (lead-approved, compile pending)`). Do not modify it.

**WO-2 · GL-010 remainder — follower tables scroll.** In `GlitchMainWindow.Replication.partial.cs`: ensure each follower-group table handles 20 followers (per-group scrolling or capped heights consistent with WO-1's approach; headers stay visible). Check `DashboardTab.partial.cs` line ~229 — a `scroll` element already wraps the groups section; verify it actually engages with many groups and that per-group grids don't defeat it. Acceptance: 20 accounts × 20 followers fully reachable at 1280×900.

**WO-3 · GL-013 — Journal: performance first.** `GlitchMainWindow.JournalTab.partial.cs`: Trader Performance becomes the primary/upper element; Live Feed demoted — collapsed-by-default Expander (or smaller, lower region). Keep the summary tiles row on top.

**WO-4 · GL-012 — warnings: calm by default.** Find where journal "Critical Warnings" entries are generated (grep Warning across Services/ and UI). Introduce a severity split at the generation source: `Critical` = imminent drawdown breach / account lock / flatten triggered ONLY → red, persistent. Everything else = `Notice` → quiet, colorless, no flash/popup, visible only in a collapsed history list. Acceptance: a normal sim session (wins and losses, no rule proximity) renders ZERO red elements. Document every warning source you found and how you classified it.

**WO-5 · GL-015 — editable cells + culture fix.** Follower table (`Replication.partial.cs`): (a) editable cells (Ratio, Max DD, Max L, Max C) get a visible affordance — hover highlight + small pencil/chevron glyph, consistent with the dark theme; (b) add tooltip showing resolved math: "master N × ratio ⇒ follower M contracts"; (c) **fix the culture defect:** the editable-cell `Binding`s lack `ConverterCulture` — en-US parse vs pt-BR display silently corrupts values. Set explicit culture (`ConverterCulture = CultureInfo.CurrentCulture` on bindings, or the invariant round-trip pattern the persistence layer uses — match whichever the codebase already leans on; state your choice in the change doc).

**WO-6 · GL-011 — followers first (evaluate, then smallest change).** After WO-1's cap, followers may already be visible on open. If more is needed: move the groups section above the Connected Accounts grid (simple row swap in `DashboardTab.partial.cs`). Do NOT build the expandable consolidated view — that's a later iteration.

**WO-7 · GL-017 — FundingTicks is a dead firm.** Per the research report (red flag 1): firm closed 2026-01. In `PropFirmRules.json` (locate via grep) set its status to `Discontinued` (keep the block for historical accounts), and make the firm-selection UI (`GlitchMainWindow.FirmRules.partial.cs` / compliance workspace) hide or disable+label it "(discontinued)". New localized string via the TSV pattern.

**WO-8 · GL-018 — rebuild Lucid rules.** Per research red flag 2: the Lucid block is a copy of stale FundingTicks data. Rewrite it from the research report's cited findings: EOD trailing drawdown, four programs (LucidFlex/LucidPro/LucidDirect/LucidMaxx), 90/10 split, per-program consistency 0%/40%/20% — map onto the existing JSON schema as best it allows; set `lastVerifiedDate: 2026-07-07` and add `"verificationNote": "secondary sources; official pages 403-blocked; confirm with firm"` if the schema tolerates extra fields (check the loader — it was confirmed to consume all fields; add only what it ignores gracefully or extend loader minimally).

**WO-9 · GL-019 — copy-trading policy schema (foundation only).** Add per-firm fields to `PropFirmRules.json` + loader: `copyTradingPolicy` { `allowed`: yes/no/conditional/unverified, `sameOwnerOnly`: bool, `maxAccounts`: int?, `notes`: string, `sourceUrl` }. Populate from the research report (TPT: prohibited-per-own-policy/UNVERIFIED-conflict; Apex: same-owner single-master ~20; TradeDay: UNVERIFIED-conflict; Lucid: allowed-no-hedging; FundingTicks: moot). Surface: when a user selects a firm whose policy is not cleanly allowed, show a **quiet, non-red informational notice** (calm-by-default!) in the firm-selection/compliance workspace: "Check {firm}'s trade-copier policy — see Settings › Compliance." No behavioral blocking yet — that needs Alan's written confirmations from the firms.

**WO-10 · GL-014 — settings granularity (DESIGN, then implement only if everything above is done).** Write the design into `ui-calm-changes.md`: the 4 coarse checkboxes in `SettingsTab.partial.cs` become per-feature, per-account-type (Sim/Eval/PA) controls with visible numeric thresholds, persisted via `GlitchRuntimePolicyStore.cs`. Implement only if WO-1…9 are complete and clean.

**WO-11 (stretch) · F2 fix from math audit.** `SummaryTab.partial.cs:940` area: unknown instruments fall back to pointValue `1.0` silently. Change to: resolve via NT's `Instrument.MasterInstrument.PointValue` when available; if still unknown, keep 1.0 BUT emit a quiet Notice (WO-4 taxonomy) "point value unknown for {symbol}; PnL display may be wrong" and log it. No red.

## Out of scope for this wave (do not touch)

Replication engine internals (`GlitchReplicationEngine.cs` beyond the binding fix), commissions/F1 seam, fleet-aggregation F3, licensing, API/website apps, anything under `glitch_hermes_docs/`. Those wait for the LANE-1 audit (separate track).

## Definition of done (per item)

Code on branch + backlog status `partial (awaiting NT8 compile)` + log line + change-doc entry. Nothing is `done` until Alan compiles in NT8 and verifies the acceptance criterion. Honest gaps > silent guesses — if an item resists a surgical fix, write down why and move on.
