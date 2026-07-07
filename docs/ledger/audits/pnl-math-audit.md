# PnL / Analytics Math Audit (GL-005)

**Lane:** LANE-2 · math-audit (Opus)
**Date:** 2026-07-07
**Scope:** GL-005 "math must be infallible" — the journal/summary analytics pipeline.
**Rule:** read-only; evidence before inference; every finding tagged CONFIRMED or PLAUSIBLE.

Files audited (all under `ninjatrader/Glitch/AddOns/GlitchAddOn/`):
- `Services/Insights/GlitchTradeInsightsService.cs` — trade construction + all stat formulas
- `Services/Insights/GlitchTradeLedgerService.cs` — TSV persistence of round-trips
- `Services/Insights/GlitchRiskLockLedgerService.cs` — lock-event ledger
- `UI/MainWindow/GlitchMainWindow.SummaryTab.partial.cs` — points→USD, fleet aggregation, card/grid binding, formatting
- `UI/MainWindow/GlitchMainWindow.JournalTab.partial.cs` — Journal tab layout (shares the same value fields)
- `UI/MainWindow/GlitchMainWindow.cs` — execution-message construction (`TryBuildExecutionJournalMessage`, ~L5991)
- `Services/Risk/GlitchComplianceEngine.cs` — drawdown/liquidation-threshold math (compliance, adjacent to GL-005)

---

## 1. Pipeline map (how a number reaches the screen)

```
NT ExecutionUpdate
  → TryBuildExecutionJournalMessage (GlitchMainWindow.cs:5991)
       builds string "Exec BUY 1 MNQ @ 20000.25 (name) [SRC:..][TAG:..][EID:..]"
       — price/qty are raw NT .ToString(); NO commission field
  → AppendJournal(account,"Execution",msg)  → _journalEntries (in-memory) + audit-feed TSV
  → RefreshSummaryInsightsIfNeeded (SummaryTab.partial.cs:357)
       (a) BuildSnapshot(journalEvents)              -> ClosedTrades in POINTS·contracts
           - regex parse (InsightsService:36) -> ExecutionEvent
           - de-dup by [EID] identity / no-id 500ms signature (:63-84)
           - ApplyExecution net-position accounting (:147) -> TradeRoundTrip.PnlPoints
       (b) MergeAndGetAll -> TradeLedgerService (persist TSV, invariant culture)
       (c) NormalizeTradesToUsd (:861) -> PnlPoints *= ResolveInstrumentPointValue(instrument)
       (d) BuildFleetTradeAggregates (:998) -> group trades across accounts into "fleet trades"
       (e) BuildStats over the FLEET trades (InsightsService:773) -> All/Long/Short TradeStats
       (f) cards + performance grid bound to that snapshot (:428-459)
```

**Two crucial facts that shape every verdict below:**

1. **`PnlPoints` is points × contracts** (realized ticks summed across partial exits, InsightsService:197), and the **only** money conversion is `PnlPoints * pointValue` in `NormalizeTradesToUsd`. After step (c) the field literally holds **US dollars**, gross of fees. All dashboard tiles/grids show these dollars.
2. **The displayed All/Long/Short come from the FLEET-aggregated snapshot (step e), not per-account trades.** Fleet aggregation groups every raw round-trip sharing `(instrument, side, entry÷5s bucket, exit÷5s bucket)` into one synthetic trade whose PnL = **sum** of the group. So "94 trades" is a count of *fleet* trades, and every win-rate / avg / PF is computed at the fleet-net level.

---

## 2. Reconciliation of the production screenshot

Given: 94 trades, net −2430.25, WR 59.6%, AvgW +250.50, AvgL −433.11, PF 0.85, Long −3196.00, Short +765.75.

- Long + Short = −3196.00 + 765.75 = **−2430.25** — exact. Side split is a clean partition of the fleet trades (each fleet trade is one side), so it must sum to net. ✓ Internally consistent.
- WR 59.6% × 94 = 56.02 ⇒ **56 winning** fleet trades, **38 losing**, **0 breakeven** in this dataset.
- Implied gross: `56 × 250.50 = 14028.00`; `38 × 433.11 = 16458.18`.
  - PF check: `14028.00 / 16458.18 = 0.8524` → **0.85** ✓
  - Net check: `14028.00 − 16458.18 = −2430.18` vs displayed **−2430.25**.
- **Residual −0.07** is fully explained: AvgW/AvgL are rounded to cents for display (`FormatSignedCurrency`, N2). The exact unrounded win/loss sums produce −2430.25 (which the Long/Short exact split confirms). This is a *display-rounding* residual, **not** a math error. All six numbers are mutually consistent under the fleet-level model.

Conclusion: the arithmetic in the screenshot is self-consistent. The risks below are about **what those numbers mean** and **when they silently go wrong**, not about this particular reconciliation.

---

## 3. Formula inventory (metric → file:line → formula → verdict)

| Metric | File:line | Formula (as coded) | Verdict |
|---|---|---|---|
| Signed qty from action | InsightsService.cs:293 | BUY/COVER/LONG → +\|q\|; SELL/SHORT → −\|q\|; else 0 | **Correct** |
| Same-side add avg price | InsightsService.cs:182 | `(\|prevQ\|·avg + \|q\|·px) / \|newQ\|` (weighted) | **Correct** (average-cost) |
| Realized points on close | InsightsService.cs:196 | `(px − avg)·prevSign · closeQty` accumulated | **Correct**; note = points·contracts |
| Exit price of round-trip | InsightsService.cs:252 | `ClosedNotional / ClosedContracts` (vol-wtd) | **Correct** |
| Contracts of round-trip | InsightsService.cs:269 | `MaxAbsQty` (peak position) | **Ambiguous** (peak, not entry size — see F7) |
| Trade emitted when | InsightsService.cs:205 | only when position returns flat | **Correct** (flat-to-flat, not FIFO lots) |
| Net points | InsightsService.cs:798,832 | `Σ pnl` | **Correct** |
| Gross profit / loss | InsightsService.cs:804,813 | `Σ pnl>0` / `Σ pnl<0` (loss kept negative) | **Correct** |
| Win rate | InsightsService.cs:831 | `wins / totalTrades` (denominator includes breakeven) | **Ambiguous** (see F5) |
| Avg trade | InsightsService.cs:832 | `netPnl / total` | **Correct** |
| Avg win | InsightsService.cs:833 | `Σwin / wins` | **Correct** |
| Avg loss | InsightsService.cs:834 | `Σloss / losses` (negative) | **Correct** |
| Profit factor | InsightsService.cs:835 | `grossProfit / \|grossLoss\|`; **0 if grossLoss==0** | **Wrong on edge** (see F4) |
| Largest win/loss | InsightsService.cs:806,815 | `Max` / `Min` | **Correct** |
| Win/loss streaks | InsightsService.cs:790-827 | breakeven resets both streaks | **Correct** (defensible) |
| Avg duration | InsightsService.cs:836 | `Σduration.Ticks / total` | **Correct** |
| Close-reason win rate | InsightsService.cs:756 | `wins/total` per reason | **Correct** |
| Points → USD | SummaryTab.cs:874 | `PnlPoints · ResolveInstrumentPointValue` | **Correct** *(gross of fees — F1)* |
| Point value resolution | SummaryTab.cs:909-996 | NT `Instrument.MasterInstrument.PointValue` → fuzzy → fallback dict → **1.0** | **Correct primary; unsafe final fallback** (F2) |
| MNQ point value | SummaryTab.cs:44 | fallback `MNQ = 2.0` | **Correct** ($2/pt, $0.50/0.25-tick) |
| Fleet aggregate PnL | SummaryTab.cs:1040 | `Σ group.PnlPoints` | **Correct sum; semantics — F3** |
| Fleet grouping key | SummaryTab.cs:1011-1017 | `instr\|side\|entry÷5s\|exit÷5s` | **Bucket-boundary risk** (F3) |
| Fleet entry/exit price | SummaryTab.cs:1034-1039 | contract-weighted avg | **Correct** |
| Avg contracts/trade | SummaryTab.cs:1149 | `Avg(\|fleet.Contracts\|)` | **Correct** (of fleet peak sizes) |
| Net PnL tile | SummaryTab.cs:432 | `FormatSignedCurrency(All.NetPoints)` (USD) | **Correct** |
| Win-rate tile | SummaryTab.cs:431 | `P1` of `All.WinRate` | **Correct** |
| PF tile | SummaryTab.cs:434 | `PF>0 ? N2 : "-"` | **Correct display** (masks F4) |
| Drawdown liquidation floor | ComplianceEngine.cs:437-440 | `ref = trailingPeak>0?peak:max(eq,size)`; `floor = static? size−dd : ref−dd` | **Correct for trailing/static**; account-sourced, not journal |
| Apex intraday dd default | GlitchMainWindow.cs:6703 | `round(maxDrawdown · 0.30)` | **Plausible**; verify vs current Apex rules (defer to GL-016) |

---

## 4. Ranked findings

### F1 — Analytics PnL is GROSS of commissions; cannot equal NT net PnL — CONFIRMED — **HIGH**
- **Evidence:** `TryBuildExecutionJournalMessage` (GlitchMainWindow.cs:6009) emits only `action/qty/instrument/price`; no commission/fee token. The ledger schema (`GlitchTradeLedgerService.cs:44`) has no commission column. `ApplyExecution` computes PnL purely from price deltas (InsightsService.cs:196). There is no fee term anywhere in the pipeline.
- **Failure scenario:** North-star invariant says "what Glitch displays must equal what NinjaTrader reports." The Dashboard/account PnL is read from NT `Account.Get(...)` (per LANE-1), which is **net of commissions**. The Journal/Summary tiles are **gross**. On MNQ at ~$0.35–$1.24 round-turn per contract, a 94-fleet-trade session diverges by tens to well over a hundred dollars — a permanent, silent gap between the two surfaces and against the broker. GL-005 acceptance ("recomputed-from-journal == displayed") can pass while the NT cross-check fails.
- **Minimal fix:** carry a commission field on the execution message (NT `Execution.Commission`), persist it on the round-trip, subtract `Σcommission` from realized USD in `NormalizeTradesToUsd`. If per-fill commission is unavailable at message time, expose analytics explicitly as "Gross PnL" so it is not mistaken for NT net.

### F2 — Unknown-instrument point value silently defaults to 1.0 — CONFIRMED — **HIGH** (gates GL-008)
- **Evidence:** `ResolveInstrumentPointValue` (SummaryTab.cs:940) returns `1.0` when NT lookup, fuzzy lookup, and the fallback dict all miss.
- **Failure scenario:** For any instrument not in NT's cache and not in the 12-symbol fallback table (a new micro, an equity CFD, a renamed root, or a fuzzy-match failure under a non-US locale), `pnlUSD = points × 1.0`. For ES that under-reports by 50×; for MNQ by 2×. No error, no flag — the tile just shows a wrong number. This is the single biggest correctness landmine for the GL-008 multi-asset goal.
- **Minimal fix:** treat unresolved point value as a hard "unknown" — exclude the trade from money aggregates and surface a one-line "instrument X unpriced" notice, rather than coercing to 1.0. Keep the NT-first resolution (it is correct and already the right design for GL-008).

### F3 — Fleet aggregation redefines win rate / averages and is bucket-boundary fragile — CONFIRMED (math) / PLAUSIBLE (distortion magnitude) — **HIGH**
- **Evidence:** `BuildFleetTradeAggregates` (SummaryTab.cs:998) groups by `entry÷5s` and `exit÷5s` integer buckets and sums PnL; the displayed stats are computed over these fleet trades (`snapshot` at SummaryTab.cs:415-419), which feed every tile and the Trader Performance grid.
- **Failure scenarios:**
  1. *Semantic:* win rate is the fraction of *fleet-net-positive* groups, not of individual account trades. If a master wins +200 but two followers lose −150 each, the fleet trade is a −100 "loss" — the win the trader actually saw disappears from WR and from Avg Win. Avg Win/Avg Loss also mix 1-account and N-account magnitudes into one mean, so +250.50 may be one account or a 3-account stack. Defensible as a "per-signal" view but it is **not** the per-trade stat a trader expects, and nothing on screen says so.
  2. *Boundary:* two fills of the same logical trade landing 4 s apart can straddle a 5-second boundary (e.g. `…:04.9` vs `…:05.1`) → different buckets → the trade splits into two fleet rows, inflating trade count and skewing WR/PF. Conversely, two unrelated trades on two accounts within the same 5 s window on the same instrument/side get merged.
- **Minimal fix:** (a) label the tiles/grid as fleet/per-signal, or offer a per-account toggle; (b) replace hard `÷bucket` flooring with tolerance-based clustering (group if within N seconds of an existing cluster's entry *and* exit), which removes the straddle artifact.

### F4 — Profit factor returns 0 for an all-wins set — CONFIRMED — **MEDIUM**
- **Evidence:** `profitFactor = |grossLoss|>ε ? gp/|gl| : 0` (InsightsService.cs:835). Zero is the *worst-possible* PF, but it is emitted for the *best-possible* case (no losers).
- **Failure scenario:** a clean winning session stores PF=0. The tile hides it (`PF>0?…:"-"`, SummaryTab.cs:434) and `FormatRatio` shows "-", so the user sees "-" for a flawless session — misleading, and any downstream consumer of the raw `ProfitFactor` field reads 0 = terrible.
- **Minimal fix:** return `double.PositiveInfinity` (or a sentinel) when `grossProfit>0 && grossLoss==0`, and format that as "∞"/"—(no losses)"; keep 0-trade case at 0.

### F5 — Breakeven trades sit in the win-rate denominator — CONFIRMED — **LOW/MEDIUM (ambiguous, by design)**
- **Evidence:** `winRate = wins/total` where `total = trades.Count` includes `even` (pnl==0) trades (InsightsService.cs:783,830-831). PF and Avg Win/Loss correctly exclude them.
- **Failure scenario:** scratch trades (exact-tick round trips, common in sim) drag win rate down without being losses, so WR and PF tell inconsistent stories. Not exercised by the screenshot (0 breakevens) but latent. Whether breakevens belong in the denominator is a genuine product choice — flagging so it is chosen deliberately, not by accident.
- **Minimal fix:** decide and document one convention; if "win rate among decided trades," use `wins/(wins+losses)`. Expose the breakeven count (already tracked as `Even`) so the denominator is legible.

### F6 — Close-reason "Session End" and session tags use machine-local time, not exchange time — CONFIRMED — **MEDIUM**
- **Evidence:** `ResolveCloseReason` hardcodes a **local** 15:55–16:10 window (InsightsService.cs:668-670) and `ResolveSessionName` buckets by **local** hour (InsightsService.cs:892-902).
- **Failure scenario:** production shows pt-BR formatting → the machine is very likely on BRT (ET+1/+2 depending on DST). The 16:00 ET futures close then never falls in the coded local window, so "Session End" is under-detected and NYC/London/Asia session labels shift by 1–2 h. This corrupts the Close-Reasons breakdown and session analytics (not the PnL totals). Also affects any future "Daily PnL reset" boundary if it reuses local midnight.
- **Minimal fix:** convert to `America/New_York` (exchange tz) before session/close-window classification instead of `ToLocalTime()`.

### F7 — "Contracts" = peak position size, not entry size — CONFIRMED — **LOW**
- **Evidence:** `Contracts = state.MaxAbsQty` (InsightsService.cs:269). A 1→3→0 scale-in/out reports 3.
- **Impact:** display + "Avg Contracts/Trade" only; PnL is unaffected (uses realized points directly). Note it so the column meaning is understood.
- **Minimal fix:** none required; document, or expose both entry and peak size.

### F8 — Cross-culture number handling is robust in the ledger but fragile at the parse seam — PLAUSIBLE — **MEDIUM**
- **Evidence (good):** the persistent ledger reads/writes every number and tick with `CultureInfo.InvariantCulture` (LedgerService.cs:268-276, 406-421). This is the source of truth and is culture-safe. ✓
- **Evidence (risk):** the execution **message** price/qty are produced by raw NT `.ToString()` (GlitchMainWindow.cs:6009) — under pt-BR that yields a comma decimal ("20000,25"). `TryParseFlexibleDouble` (InsightsService.cs:448) tries CurrentCulture *first*, then Invariant, then comma↔dot swaps. `NumberStyles.Float` excludes thousands, so most mismatches *fail-over* rather than misparse — but a value carrying a locale thousands grouping, or an audit feed written under one culture and reparsed under another, is genuinely ambiguous (e.g. "1.500" → 1.5 instead of 1500).
- **Failure scenario:** a persisted raw-journal audit feed moved between machines/locales, or a future instrument whose price legitimately exceeds 999 with grouping, can round-trip to a wrong price → wrong points → wrong money. Low probability for MNQ-scale prices; real for GL-008 breadth (indices, crude, gold at 4-5 digits).
- **Minimal fix:** format the execution message price/qty with `InvariantCulture` at the source (GlitchMainWindow.cs:6009) so the parse seam is single-culture end-to-end; keep the flexible parser only as a legacy-data shim.
- **Note (user-editable cells):** the brief's comma-decimal "edited cell" corruption concern lives in the **follower table** (`.Replication.partial.cs`, Ratio/Max DD/Max L/Max C/Size), which is outside this lane's files. None of the Journal/Summary math has user-editable numeric cells. Flagging for LANE-1/LANE-3 to verify those parse sites.

### F9 — Latent card-field aliasing between Summary and Journal tabs — CONFIRMED (latent) — **LOW now / HIGH if Summary tab is wired in**
- **Evidence:** the value TextBlocks are single fields (`_summaryFleetTradesValueText`, `_summaryAccountsValueText`, SummaryTab.cs:70,74) assigned via `out` by **both** `CreateSummaryTabImpl` (as "Fleet Trades" / "Accounts Traded", SummaryTab.cs:133,137) and `CreateJournalTabImpl` (as "Avg Win" / "Avg Loss", JournalTab.cs:110-111). `RefreshSummaryInsightsIfNeeded` writes **Avg Win / Avg Loss dollars** into them (SummaryTab.cs:430,435). Also `distinctAccountsTraded` is computed (SummaryTab.cs:421) and never assigned to any control.
- **Current status:** **not a live bug.** `CreateSummaryTab()` is defined (GlitchMainWindow.cs:505) but **never called** — the live TabControl is Dashboard / Analytics / Journal / Settings (GlitchMainWindow.cs:455-481). So only the Journal tab constructs these fields, where the labels ("Avg Win"/"Avg Loss") match the values. Verified correct on the live surface.
- **Failure scenario (if the dead Summary tab is ever re-enabled):** whichever tab is built last owns the field reference; the "Fleet Trades" tile would display an Avg-Win dollar figure and "Accounts Traded" an Avg-Loss negative dollar figure, and the other tab's tiles would be orphaned at "-". `distinctAccountsTraded` remains dropped.
- **Minimal fix:** give each tab its own value fields (or a small per-tab view-model); wire `distinctAccountsTraded` to the "Accounts Traded" tile before re-enabling the Summary tab.

### F10 — De-duplication heuristics can drop or merge real trades — PLAUSIBLE — **MEDIUM**
- **Evidence:** (a) no-EID executions within 500 ms of an identical `(acct,instr,action,qty,price,signal,src,tag)` signature are discarded (InsightsService.cs:73-80); (b) `NormalizeExactDuplicateTradesUnsafe` (LedgerService.cs:345) drops round-trips with identical full signatures.
- **Failure scenario:** two genuine same-price fills of the same size < 500 ms apart (fast partial fills, or two accounts flattening on one signal without EIDs) collapse into one → under-counted contracts and PnL. Conversely, EID de-dup is safe when NT supplies IDs; risk is concentrated in the no-ID path. Cannot confirm frequency without live capture; the design trades a duplication risk (LANE-1/GL-004) against a drop risk here.
- **Minimal fix:** prefer EID-based identity everywhere; for the no-ID path, incorporate a monotonic fill sequence if NT exposes one, and shrink/parameterize the 500 ms window. Coordinate with LANE-1 (dup-order audit) so the two lanes don't fix in opposite directions.

---

## 5. Testability plan

The stat math is **already 90% NT-independent**: `GlitchTradeInsightsService` only touches `System.*` and its own DTOs (no NinjaTrader types). The point-value resolver and fleet aggregation are the parts entangled with NT/WPF. Recommended extraction:

**Extract into a pure static library `Glitch.Insights.Math` (NT-free):**
1. `TradeMath.BuildStats(IReadOnlyList<TradeRoundTrip>)` — move as-is from `BuildStats`.
2. `TradeMath.ApplyExecutions(IEnumerable<ExecutionEvent>)` → closed round-trips — lift `ApplyExecution`/`OpenPositionState`.
3. `TradeMath.AggregateFleet(trades, entryTol, exitTol)` — lift `BuildFleetTradeAggregates` with tolerance params (fixes F3 boundary).
4. `PricingMath.ToUsd(trade, pointValue, commission)` — lift `NormalizeTradesToUsd`, **adding the commission term (F1)** and taking `pointValue` as an injected parameter so NT's `Instrument` stays in the UI layer (a thin `IPointValueSource` interface resolves it live; unit tests pass a stub).

**Concrete unit-test list (xUnit/NUnit, no NT assembly):**

*Trade construction*
- single long round-trip: +N points × contracts.
- single short round-trip: sign correct.
- scale-in (1@100, +1@102) then exit 2@104 → avg entry 101, points = (104−101)·2.
- scale-out (long 3, sell 1@105, sell 2@107) → weighted exit, points sum; **one** trade emitted only at flat.
- position flip (long 2, sell 5@px) → first trade realized on 2, new short of 3 opens at px.
- interleaved two accounts / two instruments → independent state keys (`BuildStateKey`).
- breakeven (exit == entry) → `Even++`, excluded from wins/losses.

*Stats*
- WR denominator with a breakeven present (locks F5 decision).
- PF with zero losses → asserts F4 fixed value (∞/sentinel), not 0.
- PF/AvgWin/AvgLoss/Net on the screenshot vector (56×250.50 / 38×−433.11) → PF 0.85, net −2430.18 (rounded-input) / exact from raw.
- Long+Short partition sums to All.Net.
- streaks with W/L/BE/W sequence.

*Pricing*
- MNQ points→USD at 2.0; ES at 50.0; unknown instrument → **excluded, flagged** (F2), asserting it is NOT coerced to ×1.0.
- commission subtraction: gross vs net (F1).

*Fleet aggregation*
- two accounts, same signal, entry/exit within tolerance → one fleet trade, PnL summed, `AccountCount==2`, label "2 accounts".
- two fills 4 s apart straddling a 5 s boundary → **still one** cluster under tolerance clustering (regression test for F3).
- two unrelated same-instrument/side trades 4 min apart → two fleet trades.

*Culture (parse seam, F8)*
- parse "20000,25" and "20000.25" both → 20000.25.
- format→parse round-trip under `pt-BR` and `en-US` `CultureInfo` for a 5-digit price → identical value (guards GL-008 instruments); assert invariant-formatted source.

*Persistence*
- ledger write→read round-trip preserves ticks/price/points bit-exact under a non-US current culture (LedgerService already invariant — regression guard).
- exact-duplicate normalization removes a true dup but keeps two trades differing only by EID/price (guards F10 over-merge).

---

## 6. Summary for the lead

- The **arithmetic is sound and self-consistent**; the screenshot reconciles exactly (residual is display rounding). GL-005's *internal* acceptance ("recompute-from-journal == displayed") is achievable.
- The **GL-005 product invariant against NT will still fail**, primarily via **F1 (no commissions)** and secondarily **F2 (1.0 point-value fallback)** — these are the two to fix first.
- **F3** changes what "win rate" means and is the most likely source of "these stats look off" user reports.
- **F6 (timezone)** and **F8 (culture at the parse seam)** are the pt-BR-specific correctness risks; both have small, localized fixes.
- **F9** is currently harmless (Summary tab is dead code) but must be fixed before that tab is ever re-enabled.
- The math is highly testable with a small extraction; no NT harness needed for the core.

*Confidence:* formula reads are CONFIRMED from source. Runtime-frequency claims (F3 boundary straddles, F10 drop rate) are PLAUSIBLE and need a live/replay capture (coordinate with LANE-1's replay harness) to quantify. Daily-loss reset-boundary timezone was not located in the Insights/Risk files audited here; the daily-loss limit is consumed governor-side in `GlitchMainWindow.cs` (~L6688) from NT account values — recommend a targeted follow-up if a per-day reset boundary exists there.
