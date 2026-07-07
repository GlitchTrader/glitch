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
| GL-005 | Dashboard PnL calculations and analytics accuracy | partial | **Audit complete** (`audits/pnl-math-audit.md`, 2026-07-07, LANE-2/Opus, 10 ranked findings; F2 spot-verified by lead). Arithmetic itself sound — screenshot reconciles to the cent. Fix order: **F1** journal PnL is gross of commissions (can never equal NT net — breaks PnL-truth invariant); **F2** unknown instruments silently fall back to pointValue 1.0 (`SummaryTab.partial.cs:940` — gates GL-008); **F3** fleet-aggregated stats + 5s bucket straddle; then F4 (PF=0 on all-wins), F6 (session tags use machine-local not exchange time), F10 (500ms de-dup may drop fast fills — coordinate w/ LANE-1). Testability plan: extract `Glitch.Insights.Math` (NT-independent) + ~30 unit tests. Fixes pending. |
| GL-003 | Prop-firm compliance fully wired, configurable, opt-in per feature on Security tab | todo | No sudden behaviors the user cannot individually control. Each compliance feature: visible toggle + clear description + default documented. |

## Wave 3 — Improve (UI/UX — user complaints, screenshot-grounded 2026-07-07)

| ID | Title | Status | Notes |
|----|-------|--------|-------|
| GL-006 | Less warnings, less noise; simplify screens | todo | Umbrella item; concretized by GL-010…GL-015 below. |
| GL-010 | Scrollable panels for long lists | todo | Dashboard groups must handle 20 accounts × 20 followers: ScrollViewer with fixed headers on Connected Accounts and follower tables; window must never clip rows. Files: `GlitchMainWindow.SummaryTab.partial.cs`, `.Replication.partial.cs`. Acceptance: 20+20 rows fully reachable at 1280×900. |
| GL-011 | Dashboard: followers block is the star | todo | Connected Accounts block currently sits above and crowds out the replication groups (what users actually watch). Invert order or consolidate into a single unified block (account row expands into its follower group). Acceptance: follower groups visible without scrolling on open. |
| GL-012 | Warnings doctrine: calm by default | todo | Journal "Critical Warnings" panel produces mostly false positives; red/orange flashes stress traders. Implement warning taxonomy: `critical` (imminent breach/lock — red, persistent, rare) vs `notice` (quiet log, no color, no flash). Kill false-positive sources; move non-critical to a quiet history view. Acceptance: a normal profitable/losing sim session produces ZERO red elements. See north-star "Calm by default" invariant. |
| GL-013 | Journal: performance first, live feed demoted | todo | Trader Performance table is the value; Live Feed is secondary. Reorder/resize; consider Live Feed collapsed by default. File: `GlitchMainWindow.JournalTab.partial.cs`. |
| GL-014 | Settings granularity | todo | Settings tab has only 4 coarse risk checkboxes. Expand into per-feature, per-account-type (Sim/Eval/PA) controls with numeric thresholds visible/editable — this is the UI face of GL-003 (opt-in compliance on the Security tab). Files: `GlitchMainWindow.SettingsTab.partial.cs`, `GlitchRuntimePolicyStore.cs`. |
| GL-015 | Editable cells must look editable; ratio/size clarity | todo | Follower table: users can't tell which cells (Ratio, Max DD, Max L, Max C, Size) are editable — add chevron/underline/hover affordance; show resolved ratio math (master size × ratio = follower contracts) inline or on hover. File: `GlitchMainWindow.Replication.partial.cs`. |

## Wave 4 — Ship

| ID | Title | Status | Notes |
|----|-------|--------|-------|
| GL-007 | Exercise distribution loop with waiting test users | todo | Pipeline exists: copy AddOn folder to NT bin → recompile → re-export → push to repo → version live → users update. Acceptance: one full cycle delivered to ≥1 external test user with in-app update. |

## Wave 5 — AI expansion (blocked by Waves 1–2)

| ID | Title | Status | Notes |
|----|-------|--------|-------|
| GL-008 | Multi-asset bridge + analytics | todo | Bridge/analytics currently MNQ-centric; normalize per-instrument (point values, sessions, regimes). Prerequisite for GL-009 breadth. |
| GL-009 | Hermes decision layer (5-min loop: BUY/SELL/HOLD/NOTHING → deterministic Glitch checks) | todo | Contract + schemas + risk firewall + M0–M3 milestones fully designed in `glitch_hermes_docs/`. Follow the phase ladder in `north-star.md`. Paper mode first, always. |

## Wave 1b — External truth (parallel with audit)

| ID | Title | Status | Notes |
|----|-------|--------|-------|
| GL-016 | NT8 + prop-firm rules refresh | done (research) | **Report landed:** `research/nt8-propfirm-refresh-2026-07.md` (LANE-4, 2026-07-07, fully cited). Follow-up: re-verify 403-blocked official pages with a real browser session. Spawned GL-017/018/019 below. |

## Wave 2b — Compliance truth (from GL-016 red flags — operator attention required)

| ID | Title | Status | Notes |
|----|-------|--------|-------|
| GL-017 | Remove FundingTicks as a live firm | todo | **Firm CLOSED 2026-01** (wind-down announced 2026-01-18). `PropFirmRules.json` still ships it as `Supported` with lastVerifiedDate 2024-01-15. Remove from selectable firms / mark Discontinued in UI + rules JSON. |
| GL-018 | Rebuild Lucid Trading rules from real data | todo | Encoded Lucid block is a **byte-identical copy-paste of the stale FundingTicks block** — fabricated data shipped under Lucid's name. Real Lucid 2026: EOD trailing drawdown, 4 programs (Flex/Pro/Direct/Maxx), 90/10 split since 2026-03, per-program consistency 0%/40%/20%. Rewrite entry from cited sources + firm confirmation. |
| GL-019 | Encode per-firm copy-trading/automation policy; same-owner replication guard | todo | **Existential:** TPT's own Trade Copier Policy prohibits cross-account copy services ("coordinated trading"); TradeDay sources conflict (UNVERIFIED); Apex allows only same-owner, single-master, same-direction (~20 acct cap); Lucid allows copiers but bans cross-account hedging. Rules schema has NO representation of this. Add policy fields to `PropFirmRules.json`, surface in UI at firm selection, and gate replication defaults per firm. **Operator: confirm TPT + TradeDay policy in writing (support ticket) before any marketing of replication for those firms.** Also: Apex 4.0 (2026-03) tier numbers need reconciliation; Apex metals halted 2026-03-14 (no instrument-exclusion mechanism exists). |

## Dependencies

```text
GL-002 → gates → GL-001, GL-003, GL-004, GL-005
GL-016 → informs → GL-003 (compliance must encode CURRENT firm rules)
GL-001/004/005 → gate → GL-007 (don't ship known-broken replication/PnL to test users)
GL-002 + Wave 2 → gate → GL-008, GL-009
Wave 3 (GL-010…GL-015) runs parallel with Waves 1–2 (UI-only, disjoint files)
```

## Delegation map (2026-07-07, Fable as lead)

| Lane | Agent | Items | Output |
|------|-------|-------|--------|
| replication-audit | Opus subagent | GL-002 (Trading/Risk slice) → findings for GL-001/GL-004 | `docs/ledger/audits/replication-audit.md` |
| math-audit | Opus subagent | GL-002 (Insights slice) → findings for GL-005 | `docs/ledger/audits/pnl-math-audit.md` |
| ui-calm | Sonnet subagent | GL-010…GL-015 implementation | C# patches + `docs/ledger/audits/ui-calm-changes.md` |
| external-truth | Sonnet subagent | GL-016 | `docs/ledger/research/nt8-propfirm-refresh-2026-07.md` |

Compile verification: NinjaScript compiles inside NT8 (F5 / import). Agents deliver patches + static reasoning; operator (or Fable via distribution script) copies to NT bin and compiles before any status flips to done.
