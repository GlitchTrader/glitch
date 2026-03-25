# Glitch 247 — v4 vs v7, telemetry, and how to use the four knob sets

This doc ties together **trade exports** (`Glitch 247 Trades Adaptive v4.csv` vs `v7.csv`), **bar telemetry** (`Glitch247Telemetry.csv`), and **code evolution** from our working sessions.

## 1. Headline numbers (same backtest universe: MNQ, same export format)

| Metric | **v4** | **v7** | Δ (v7 − v4) |
|--------|--------|--------|-------------|
| **Net (all)** | **+$10,657** | **−$4,425** | **−$15,083** |
| **Net long** | −$588 | +$592 | +$1,180 |
| **Net short** | **+$11,245** | **−$5,017** | **−$16,262** |
| **# longs** | 700 | 255 | −445 |
| **# shorts** | 2813 | 3181 | **+368** |
| **Approx DD (long-only cum PnL path)** | −$1,985 | −$993 | (not comparable goal) |
| **Approx DD (short-only cum PnL path)** | **−$2,320** | **−$7,921** | **Worse** |

**Takeaway:** v7 did **not** “fix longs at the expense of a little short edge.” It **collapsed the short book** (−$16k vs v4) while **also** taking **more** short trades (+368). Long PnL improved slightly but **long count collapsed** (−445), so the system is **not** in a comparable regime to v4.

**Your target (stated):** restore **~+$10k short, ~−$2k short DD** first; then push **longs** toward **+$10k** using **HL-only** knobs; **drawdown last**.

---

## 2. What changed in *code* since the profitable v4 era (chat + workspace)

Below is **behavioral** surface area — not every line of diff.

| Area | What we added / changed | Why it matters |
|------|---------------------------|----------------|
| **Per-play ATR gates** | `MinAtrTicksLL/LS/HL/HS` + **adaptive** median × scale | Extra **entry** filter beyond ADX + trend. When **on**, it removes bars with “too small” ATR vs rolling median. **Scales are per play** (`AdaptiveAtrScaleHS` only affects HS when gate is on). |
| **Adaptive ATR gate toggle** | `EnableAdaptiveAtrGate` | **v4 profitable runs used this ON** (regime filter). **v7 `SetDefaults` flipped it OFF** (“baseline restore”) which **changes which bars trade** — especially shorts — even when min tick inputs are 0. |
| **ADX symmetry mistake (v5)** | Short ADX 35 → 40 with long 40 | **Reverted:** killed shorts (many winners were ADX 35–40). **Do not** set long/short ADX the same “to be fair” without data. |
| **Trailing stops (removed)** | Global + per-play trail UI and `GetDynamicStopPrice` logic were **removed** from `Glitch247Strategy` | Stops are **fixed** at entry ± ATR-derived tick risk (`ComputeActiveRisk` + hard cap). No trailing. |
| **Low confidence toggle** | `EnableLowConfidenceEntries` default **false** | v4 comparison runs were effectively **HL/HS only** — aligns with exports showing `_LH_` / `_SH_` names. |
| **Recovery mode** | Consecutive-loss / daily-limit interaction | Can block or shrink entries; should stay **on** while debugging, but note it’s **global** state, not per play. |
| **Daily lock ladder** | Optional giveback after daily peak | Defaults often **0**; if enabled, interacts with **exit mix** (e.g. `DailyPnLLimit` rows in CSV). |
| **Trade telemetry file** | `Glitch247TradeEvents*.csv` | **Does not** change fills; safe to toggle for research. |
| **ATR period** | Strategy default **10** | `247TelemetryExporter` defaults **ATR 5** unless you aligned it — **telemetry vs strategy** feature scales won’t match if period differs. |

---

## 3. How this maps to the **four independent knob sets**

In `Glitch247Strategy`, **`ComputeActiveRisk`** chooses SL/TP from **direction × confidence** only:

- **LL** — `AdxMinLowConfidenceLong`, `StopLossAtrMultLowLong`, `Target1AtrMultLowLong`, `MinAtrTicksLL`, `AdaptiveAtrScaleLL`, …
- **LS** — short low-conf analogs  
- **HL** — `AdxMinHighConfidenceLong`, high long SL/TP mults, HL gates  
- **HS** — `AdxMinHighConfidenceShort`, high short SL/TP mults, HS gates

**Rule:** To recover **short v4**, change **HS + adaptive gate + HS scales**, not HL targets. To chase **+10k long**, change **HL only** once shorts are restored.

**Global coupling (unavoidable):** `EnableAdaptiveAtrGate` applies **after** play type is known but uses **per-play** scales — so it’s “independent” per play in code, yet **one toggle** turns the whole regime filter on/off.

---

## 4. Telemetry (`Glitch247Telemetry.csv`) — what it says about *structure*

Bar-level aggregates (full file, chunked):

| Play label (direction × conf) | ~Bars | Mean ADX | Mean ATR ticks |
|-------------------------------|------:|---------:|---------------:|
| LL | 983,647 | 24.72 | 24.04 |
| LS | 706,533 | 24.53 | 23.17 |
| HL | 251,395 | 25.52 | 38.75 |
| HS | 249,534 | 27.08 | 42.80 |

Interpretation:

- **High-confidence** rows (HL/HS) live in **higher ATR** than low-conf — your **ATR multiples** for stops/targets will **naturally** be larger in dollars on HL/HS.
- **HS** bars show **slightly higher mean ADX** than **HL** in this dump — supports **asymmetric ADX mins** (e.g. higher bar for long quality) **if** you want that — but **do not** copy v5’s symmetric 40/40.

**Caveat:** If telemetry was exported with **ATR period 5** and the strategy uses **10**, do not over-fit thresholds from telemetry alone — **re-export** with **ATR period 10** for apples-to-apples.

---

## 5. Exit mix — where the money went (v4 vs v7)

From the autogenerated comparison (`glitch-247-v4-v7-analysis-autogen.md`):

- **v4 shorts:** large positive contribution from **Target / Target2**; **Stop** still the main drag (as expected).
- **v7 shorts:** **more stop losses** (1640 vs 1259) and **worse net on stops** — consistent with **worse entry mix** (more trades, worse expectancy), not a small TP tweak.

So: **restore entry quality on shorts first** (HS ADX + adaptive HS scale + gate on), then **TP/SL mults** on HS, **without** touching HL until short book matches v4.

---

## 6. Recommended path to your stated goals

### Phase A — Restore **short v4** (~+$10k, ~−$2k short DD path)

1. **`EnableAdaptiveAtrGate = true`** (match v4 regime filter).  
2. **`AdxMinHighConfidenceShort = 35`** (already in defaults — do not raise “to trim chop” without a new study).  
3. **`AdaptiveAtrScaleHS`** leave at **0.90** unless telemetry re-run says otherwise.  

### Phase B — Push **longs** toward +$10k (HL only)

Tune **only**:

- `AdxMinHighConfidenceLong`, `StopLossAtrMultHighLong`, `Target1AtrMultHighLong`, `Target2AtrMultHighLong`  
- `MinAtrTicksHL` / `AdaptiveAtrScaleHL` (if gate on)  

**Do not** change **HS** or **global** ADX short while validating longs.

### Phase C — Drawdown

After **both** sides hit PnL goals in **forward/back** tests, layer **daily lock ladder**, **recovery**, **session filters**, etc.

---

## 7. Regenerate the numeric comparison

```text
python ninjatrader/Glitch/Docs/_scripts/compare_v4_v7_trades.py
```

Writes: `ninjatrader/Glitch/Docs/glitch-247-v4-v7-analysis-autogen.md`

---

## 8. Honest constraints

- **Trade CSVs** are **outcomes**; **telemetry** is **features**. Joining them requires **aligned timestamps / ATR period / instrument** — use for **hypotheses**, then confirm in Strategy Analyzer.
- **~+$10k long and ~+$10k short** on the **same** period with **no** DD work is a **very** high bar; your ordering (PnL first, DD after) is the right engineering sequence.
