# R06 — Pattern Mining: Method, Metrics, and Hermes Seeding

**Audience:** private maintainers and agents only.
**Author:** Fable (architect) · 2026-07-11 · **Status:** ACTIVE — mining in progress, findings appended at bottom.
**Rail:** R06 / GL-029 (parallel lane) · feeds R11 (Hermes paper loop), R13 (replay proof), R18 (confidence gating), R23 (self-learn).

```text
Hermes proposes. Glitch validates, executes, journals, and protects.
Mining discovers. Validation deflates. Only survivors become archetypes.
```

---

## 1. The corpus (audited 2026-07-11)

| Property | Value |
|---|---|
| Location | `GlitchData/export/corpus/MNQ/` (runtime store — read-only for mining) |
| Files | **705,697** single-snapshot JSONs, one per minute |
| Coverage | 2024-01-03T07:00Z → 2025-12-31T21:59Z (2 full years) |
| Schema | `glitch.market.snapshot.v2` (raw-only; v1 is legacy, do not mine it) |
| Timeframes | 1m, 5m, 15m, 60m per snapshot |
| Indicators per TF | ATR, ADX, RSI, StochK, z-score, average_price, DI+, DI−, CCI, MACD-histogram |
| Session block | name (Asia/Europe/US), session high/low, previous session high/low |
| Order flow | **all null** — layer was off during export. Excluded from mining. Re-export later if wanted. |

Mining must not write into `GlitchData/`. Working set and outputs live in
`Glitch-Collab/Research/r06-mining/` (scripts + reports committed; bulk parquet gitignored).

## 2. Principles (what keeps this honest)

1. **Net-of-cost from the first query.** MNQ point value $2.00, tick $0.50. Friction model:
   commission ≈ $1.30 round trip + slippage 1 tick/side ≈ $1.00 → **$2.30 RT ≈ 1.15 points**.
   Any pattern whose gross edge per trade is not a multiple of 1.15 pts does not exist.
2. **Labels are trade geometry, not returns.** Triple-barrier labeling (SL barrier, TP barrier,
   time barrier) matched to the Intent v2 bracket mandate. A label IS a bracketed trade outcome.
3. **Condition before you mine.** Edges on MNQ 1-min are regime-local (proved by the prior
   6-year study). All expectancy is computed inside regime cells (vol × trend × session),
   never pooled first.
4. **Legible output only.** The deliverable is rules a language model can read, apply, and
   explain in a journal: archetypes with explicit preconditions, triggers, geometry, and stats.
   Opaque model files are not deliverables; ML is used only to find interactions, which are
   then re-expressed as rules.
5. **Multiple-testing control is the game.** Scanning thousands of condition combos guarantees
   lucky ghosts. Every candidate passes: train/validation/holdout split by time, purge gap at
   boundaries, and a required out-of-sample repeat of the in-sample sign and magnitude.
   The 2025-Q4 slice is a **locked holdout** — untouched until final archetype selection.
6. **Sample floor.** No archetype ships with < 300 triggered instances in-sample and < 100
   out-of-sample. Below that, variance owns the result.
7. **Mining proposes, replay proves.** A mined archetype is a *candidate* until it beats the
   NOTHING baseline on the R13 replay harness on data it never trained on.

## 3. Metrics (ranked — what "good" means)

| Metric | Definition | Bar |
|---|---|---|
| **Net expectancy / trade** | mean(net PnL points) × $2, after 1.15 pts friction | > $1.50/trade at 1 contract |
| **Hit rate vs breakeven** | win% vs breakeven win% for the geometry (incl. friction) | ≥ +4 pts above breakeven |
| **Profit factor (net)** | gross wins / gross losses | ≥ 1.15 in-sample, ≥ 1.05 OOS |
| **Trigger frequency** | instances per week | ≥ 3/week (else operationally irrelevant) |
| **OOS retention** | OOS expectancy / IS expectancy | ≥ 0.5 (decay > 50% = ghost) |
| **Max adverse run** | worst losing streak × per-trade risk | must fit $300/day + prop drawdown |
| **Regime stability** | expectancy sign consistent across 3+ regime-adjacent cells | required |

Win rate alone is banned as a headline metric (a 21% win-rate 3R short beats a 55% 0.5R long).

## 4. Labeling spec (triple-barrier)

For each minute t where a candidate condition fires:

```text
entry     = next 1m open after t (no lookahead; Hermes decides on closed bars)
SL        = entry ∓ k_sl × ATR1m(t)      (k_sl ∈ {1.5, 2.5, 4.0})
TP        = entry ± k_tp × ATR1m(t)      (k_tp = k_sl × RR, RR ∈ {1.0, 1.5, 2.0})
time cap  = 60 minutes (vertical barrier → exit at close of bar t+60)
label     = which barrier is hit first, walked forward on 1m highs/lows
net PnL   = barrier PnL − 1.15 pts friction
ambiguity = if a single 1m bar spans both barriers → count as LOSS (pessimistic tie-break)
```

ATR-relative geometry keeps SL/TP meaningful across the 2024 (low vol) → 2025 (higher vol)
range and maps directly onto Intent v2 `stop_loss` / `take_profit_1`.

## 5. Regime frame (conditioning axes)

Computed per minute from the snapshot itself — all available to Hermes live:

```text
vol_regime    = ATR60 / close, tertiles fit on TRAIN ONLY → low | med | high
trend_regime  = ADX60 ≥ 25 → trending (DI+>DI− up / down) · ADX60 < 25 → range
session       = from session.name + UTC hour → Asia | Europe | US_open (13:30–15:30Z)
                | US_mid | US_close (19:00–21:00Z) | post
day_of_week   = secondary check only (no DOW-only archetypes — too prone to ghosts)
```

Target: ~45 populated regime cells (3 vol × 3 trend × 5 session).

## 6. Mining method (layered)

```text
L0  ETL: 705k JSON → monthly parquet (flat row = all 4 TFs' indicators + OHLCV + session)
L1  Quality audit: gaps, weekend/holiday shape, indicator NaN warmups, dedupe
L2  Feature grid: per indicator × TF → quintile buckets (fit on train only)
    + derived: RSI divergence 1m vs 15m, z-score extremes, DI spread, session-range position,
    prev-session interaction, ATR expansion ratio (1m ATR vs its 60-bar mean)
L3  Conditional expectancy scan: for each regime cell × feature bucket (single + pairs)
    × side (long/short) × geometry (9 combos) → net expectancy table with counts
L4  Candidate filter: metrics bars from §3 on TRAIN (2024-01 → 2025-06)
L5  Validation: VALID slice (2025-07 → 2025-09) — sign + ≥50% retention required
L6  Interaction probe: gradient-boosted trees + SHAP on surviving cells only,
    to catch 3-way interactions the grid missed → re-expressed as explicit rules
L7  Archetype writing: survivors → archetype JSON + playbook prose
L8  Holdout: 2025-Q4, touched exactly once, after archetypes are frozen
```

Purge gap: 1 trading day dropped at every slice boundary (labels look ≤ 60 min ahead;
one day is generous).

## 7. Archetype contract (the deliverable)

One JSON per archetype (versioned, append-only), mirrored in prose:

```json
{
  "archetype_id": "MNQ-<name>-v1",
  "instrument": "MNQ",
  "side": "short",
  "regime_preconditions": {"vol": ["med","high"], "trend": "range", "session": ["US_open"]},
  "trigger": [{"feature": "rsi_1m", "op": "<=", "value": 25},
               {"feature": "z_score_15m", "op": "<=", "value": -2.0}],
  "geometry": {"sl_atr1m_mult": 2.5, "rr": 1.5, "time_cap_min": 60},
  "sizing": {"contracts_base": 1, "risk_usd_per_trade": "sl_points * 2.00 * contracts"},
  "stats": {"train": {"n": 0, "net_exp_usd": 0, "pf": 0, "win_rate": 0},
             "valid": {"n": 0, "net_exp_usd": 0, "pf": 0, "win_rate": 0},
             "holdout": null},
  "failure_modes": ["..."],
  "status": "candidate | validated | promoted | retired",
  "provenance": {"corpus": "MNQ 2024-01→2025-12 v2", "mined_utc": "", "miner": ""}
}
```

`sl_points × $2 × contracts ≤ per-trade cap` is how the firewall check 10 stays satisfiable.

## 8. Seeding Hermes and the ongoing loop

```text
Seed (now):     Glitch-Collab/Research/r06-mining/out/archetypes/*.json  (all candidates)
                → promoted subset copied to glitch_hermes_docs/memory/archetypes.v1.json
                + glitch_hermes_docs/memory/mnq-playbook.md   (prose: how to read the tape,
                  regime recognition recipe, SL/sizing doctrine, no-trade conditions)
Use (R11+):     suggest_trade prompt includes: latest snapshot + active archetype file +
                lessons file. Hermes maps snapshot → regime cell → matching archetypes →
                emits intent with the archetype's geometry, or NOTHING when no archetype
                matches (NOTHING is the default, not the exception).
Learn (R17+):   6-hour learning pass correlates journal outcomes per archetype_id
                (intent.reason carries archetype_id) → per-archetype live stats accumulate.
Re-mine (loop): monthly, expanding window; live corpus keeps growing via R05 archiver.
                New candidates → same validation spine → replay harness → promotion.
Retire:         archetype whose live net expectancy goes negative over ≥ 50 trades, or
                whose live/backtest retention < 0.3, is auto-flagged for retirement review.
Never:          Hermes edits its own archetype file. Promotion and retirement are
                versioned human-gated changes (R23 promotion gate).
```

## 9. Ledger integration

Backlog sub-items added under R06 (see `docs/ledger/backlog.md`):

```text
R06a  ETL corpus → parquet + quality audit          (this session)
R06b  Labeling + regime frame + expectancy scan     (this session)
R06c  Candidate archetypes + validation slice        (this session)
R06d  Archetype JSON + MNQ playbook + Hermes memory seed
R06e  Holdout pass + replay-harness proof (R13 tie-in)
R06f  Ongoing loop: monthly re-mine + live-stat reconciliation (R17/R23 tie-in)
```

---

## 10. Findings log (append-only, newest first)

*(appended as mining progresses — survives session cuts)*

### 2026-07-13 — R06g probes: data patterns + first-principles skill grading

Four probes run over the full 2022–2026 corpus (`mine_06_probes.py`, results in
`out/expanded/probe_results.md`):

- **P1 regime forecastability — NULL.** Next-hour path efficiency (trend vs chop) is
  unpredictable from current ADX/ATR state (persistence corr −0.006; ADX quintile spread
  0.133→0.129). Do not build confidence gating on trendiness forecasts from these features.
- **P2 skill grade: "fade 2σ from 60m mean, target 50% reversion" — FAILS.** Negative in
  every regime segment (range long: −2.9 pts PF 0.87; short of +2σ strength worst at −4.9 pts
  PF 0.72; n=1.4–1.5k dedup each side). 50% reversion of a ~150-pt deviation within 60 min
  almost never happens (TP rate 9–14%). Generic unconditioned deviation-fading is now an
  evidence-graded DO-NOT-ARM at this timescale.
- **P3 exhaustion (DI-spread collapse in strong trend) — WEAK.** Continuation drift drops
  +0.57 → +0.37 pts but never flips negative; P(continue) ~0.51 both. Mild de-rating input
  only; not an EXIT signal.
- **P4 management mining — THE JUICE.** On both flagship v2 archetypes, PnL sign at minute
  20 nearly separates the final population: QO-BREAKDOWN underwater@20min → E[final] −14.3 pts
  vs in-profit@20min → **+32.5 pts**; HV-LULL −12.5 vs +19.4. The underwater cohort
  deteriorates monotonically (10→20→30 min), which is also direct empirical proof that
  averaging down is wrong on this data. Winners' MAE median 1.5–1.6×ATR (p90 3.6–4.4×) vs
  mined SLs 2.5–4.0×ATR → breakeven-move and scale-out legs are mineable.
  **Candidate skill: "in profit by minute 20 or get out" — quantify exact per-archetype
  thresholds and add as R06g deliverable before R13 replay.**

### 2026-07-13 — R06f COMPLETE: v1 set invalidated by era re-test; v2 set mined, holdout-proven, seeded

**Phase 1 — frozen v1 re-test on unseen eras (the humbling):** every v1 archetype failed
generalization. DTC shorts: +6.3/+9.2 pts in 2022 bear but **−4.5/−2.9 in 2023 recovery**;
BOUNCE negative in both; retired longs confirmed (CLOSE-DIP −6.5 pts × n=167 in 2022).
**Lesson: 2 years of corpus cannot separate edge from era.** All v1 archetypes demoted
(retired; OPEN-FADE → candidate). Full table: `Research/r06-mining/out/expanded/v1_retest.csv`.

**Phase 2 — v2 mining (train 2022-01→2025-06 spanning bear+recovery+bull · valid 2025-H2
· locked holdout 2026-01→03-12 touched once):** 8,966 tests → 304 train pass → 13 OOS
survivors + pairs → 8 frozen → holdout verdicts:

| v2 Archetype | Train | Valid | Holdout 2026 | Status |
|---|---|---|---|---|
| QO-BREAKDOWN-SHORT (quiet open, below prev low + cci15↓, 4.0/1.5) | +11.0, PF 1.65, n=178 | +17.8, PF 1.82, n=40 | **+11.2, PF 1.34, n=10** | validated |
| QO-MOMDIV-SHORT (macd5↓↓ + cci15↓, 2.5/1.5) | +8.5, n=154 | +10.1, n=40 | +20.0, PF 1.73, n=9 | validated |
| QO-WEAK-SHORT (z15 ≤ −1.79, 4.0/2.0) | +6.8, n=174 | +16.0, n=40 | +44.4, n=7 | validated |
| **HV-LULL-SHORT (vol_hi downtrend, adx1 ≤ 13.8, 2.5/1.5)** | +2.2, **n=1065** | +4.6, n=118 | **+4.1, PF 1.20, n=107** | **validated — workhorse** |
| HV-EUR-BOUNCE-SHORT | +4.6, n=419 | +3.3, n=47 | −8.2, PF 0.69, n=47 | RETIRED |
| DC-DOWNDAY-LONG (prev day down, vol_md range) | +4.5, n=281 | +3.4, n=43 | −10.4, PF 0.52, n=30 | RETIRED |
| DC-DIP-LONG (1m-vs-15m RSI dip, quiet midday) | +4.5, n=279 | +6.6, n=115 | +26.5, n=5 | validated (thin — review at 30 trades) |
| DT-MID-SHORT (v1-family successor) | +9.4, n=155 | +7.4, n=26 | −16.4, n=8 | candidate (bear-era; paper-only) |

**Program conclusions:** (1) era-robustness is the primary selection criterion from now on —
multi-era train + fresh forward holdout is the minimum bar; (2) HV-LULL-SHORT is the
highest-confidence, highest-frequency finding of the program (positive across 1,290 dedup
trades spanning 4 eras); (3) the book is short-heavy — durably bullish tape ⇒ re-mine, not
improvised longs; (4) v2 seeded to `glitch_hermes_docs/memory/archetypes.v2.json` +
playbook v2 rewritten.

**Follow-ups queued:** fill 2023-10→12 corpus hole (exporter stopped mid-backfill);
DSR/PBO formal deflation; order-flow re-export; R13 replay proof on v2 set.

### 2026-07-13 — R06f expanded run started (GL-046 corpus, 2022→2026)

- Expanded corpus verified quiescent (index last write 2026-07-12 23:11Z): **1,410,695 files**,
  coverage 2022-01-04 → 2026-03-12.
- **Known hole:** backfill stopped mid-2023-10 (21,531 of ~29k files) and **2023-11 + 2023-12
  are missing entirely**. Tolerated for mining (label span-guard invalidates gap-crossing
  trades); operator should re-run the exporter for 2023-10→12 to fill.
- Design (`mine_04_expanded.py`): Phase 1 re-tests the **frozen v1 archetypes** (original
  thresholds) on three unseen eras — 2022 bear, 2023 recovery, 2026-01→03-12 post-freeze
  forward. Phase 2 mines v2: TRAIN 2022-01→2025-06 · VALID 2025-07→2025-12 ·
  **HOLDOUT 2026-01→03-12 locked** (scored once, by a separate script, after v2 freeze).
  New daily-context features added for the long-side bull-gate hunt
  (ret_1d, ret_5d, close_vs_5d_mean, prev_day_dir); vol_hi now populated by 2022.

### 2026-07-12 — expanded-corpus validation contract (before re-mining)

The incoming v2 export extends the corpus to 2022-01-04→2026-03-12. The original
2025-Q4 holdout was opened once on 2026-07-11 and its verdicts changed the playbook;
therefore it is **known evidence**, not an untouched holdout for any expanded run.
Relabeling it as locked would be leakage.

Expanded-run temporal contract:

```text
2022-01-04→2024-12-31  discovery / parameter fitting
2025-01-01→2025-12-31  known forward evidence (never called locked)
2026-01-01→2026-01-07  purge; excluded from fitting and scoring
2026-01-08→2026-03-12  locked final holdout; open once after candidates freeze
```

The exact end is bounded by the frozen manifest. All rows are sorted by parsed UTC;
raw `index.jsonl` append order is forbidden because the expanded export contains a
known chronology inversion. New candidate definitions and geometry must be frozen
before reading 2026 results. Existing archetypes may be evaluated on 2026, but no
threshold, regime gate, or geometry may be tuned from those results without creating
a new future holdout.

The native cross-check uses NinjaTrader Strategy Analyzer with local data, explicit
commission/slippage assumptions, and Walk Forward Optimization where parameters are
fit on each in-sample segment and evaluated on the following unseen test segment.
Native results complement the snapshot replay; they do not replace R13 parity proof
or authorize policy promotion.

Primary platform references:

- NinjaTrader 8 Help Guide, `Strategy Analyzer > Backtest a Strategy`
- NinjaTrader 8 Help Guide, `Strategy Analyzer > Walk Forward Optimization`

### 2026-07-11 — scan complete, archetypes frozen, holdout passed ONCE

**Scan:** 65 regime cells × 98 conditions × 2 sides on train (2024-01→2025-06) with base
geometry = 6,596 tests → 359 passed train bars → **23 survived 2025-Q3 validation**
(dedup non-overlapping trades throughout). Survivors cluster into 5 coherent families,
not 23 independent ghosts. Stage-3 pairs + stage-4 geometry refinement → 7 frozen
archetypes → single locked 2025-Q4 holdout pass. Full tables: `Research/r06-mining/out/`.

**Post-holdout verdicts (the headline):**

| Archetype | Train | Valid (Q3) | Holdout (Q4) | Status |
|---|---|---|---|---|
| DTC-BREAK-SHORT (US_mid, vol_md, ADX15≥35 + DI60≤−14, 2.5/2.0) | +10.6 pts, PF 1.82, n=113 | +8.3, PF 1.53, n=31 | +2.4, PF 1.10, n=9 | **validated** |
| DTC-DI-SHORT (broader DI trigger) | +6.3, PF 1.44, n=205 | +6.3, PF 1.40, n=37 | +2.4, n=9 | **validated** |
| DTC-BOUNCE-SHORT (1m-vs-15m RSI spike fade, 4.0/1.0) | +7.1, PF 1.47, n=139 | +6.6, PF 1.44, n=35 | +7.0, n=3 | **validated** |
| LDN-WEAK-SHORT (Europe low-vol continuation) | +2.5, PF 1.32, n=138 | +8.4, PF 2.20, n=55 | −0.5, n=7 | **candidate (paper-only)** |
| CLOSE-DIP-LONG (US_close stoch dip-buy) | +6.4, PF 1.28, n=212 | +12.6, PF 2.03, n=34 | **−16.2, PF 0.46, n=33** | **RETIRED** |
| OPEN-MOM-LONG (quiet-open momentum) | +5.9, PF 1.39, n=100 | +12.9, PF 2.14, n=33 | −7.4, PF 0.68, n=15 | **RETIRED** |
| OPEN-FADE-SHORT (opening up-thrust exhaustion) | +5.0, PF 1.33, n=138 | +21.9, PF 3.28, n=36 | **+9.4, PF 1.86, n=28** | **validated (promoted)** |

**Meta-findings:**
1. The real, transferable edge in this corpus is **short-side continuation of established
   weakness** (60m DI-pressure) and **fading opening up-thrust exhaustion**.
2. **Long-side dip-buying is bull-era beta, not edge** — 21 months of profits, then Q4
   destroyed it. Long archetypes require an explicit higher-TF bull gate before reactivation.
   This retirement is the single most valuable output of the holdout discipline.
3. vol_hi has zero validated archetypes → hard NOTHING zone for Hermes v1.
4. Median SL sizes ($27–100/contract at mined geometry) fit M0 $100 cap at 1 contract;
   the 4.0×ATR archetypes occasionally exceed it in vol_md → emit NOTHING, never thin the stop.

**Deliverables shipped:** `glitch_hermes_docs/memory/archetypes.v1.json` (machine),
`glitch_hermes_docs/memory/mnq-playbook.md` (reasoning guide),
`glitch_hermes_docs/docs/12_hermes_trading_skills_and_knowledge.md` (profile skills+instructions).

**Next mining targets (R06f):** bull-regime gate for the retired longs; vol_hi coverage;
formal DSR/PBO deflation; order-flow re-export; MES/M2K cross-instrument transfer test.

### 2026-07-11 — ETL + quality audit + labeling done

- ETL: 705,697 JSON → 24 monthly parquet parts (`Glitch-Collab/Research/r06-mining/data/`), **0 bad files**.
- Quality: zero NaN on close/ATR/ADX/RSI/session across all 705k rows; 520 gaps >5min (weekends/holidays, expected); largest gap ~3 days (weekend).
- Exchange session names in corpus: `Asia | London | NYC` (mapped to Asia/Europe + ET-clock US buckets).
- Labels: 18 sets (long/short × k_sl {1.5,2.5,4.0} × RR {1.0,1.5,2.0}), pessimistic tie-break,
  60-min vertical barrier, friction 1.15 pts RT → `out/mnq_features_labels.parquet` (135 cols).
- External skills research: `tradermonty/claude-trading-skills` (MIT) reviewed — discretionary
  equity-investor tooling, **not fit** for autonomous futures intents; only position-sizing math
  concept transfers. Useful architecture references instead: QuantAgent (indicator→structured
  prompt agents), TradingGPT/FinMem (layered memory), ATLAS (dynamic prompt optimization).
  Conclusion: Hermes skills will be custom, mirroring our archetype contract.

### 2026-07-11 — session start

- Corpus audited: 705,697 snapshots, 2024-01-03 → 2025-12-31, v2 schema confirmed, order-flow null.
- Plan doc written (this file). ETL starting.
