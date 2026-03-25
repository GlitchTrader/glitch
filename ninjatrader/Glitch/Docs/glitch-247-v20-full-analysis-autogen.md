# Glitch 247 v20 — full analysis & what-ifs

**Money parsing:** European `-$ …` / `$ …` format handled correctly (not 100% win rate bug).

- Trades: `C:\Users\alan\OneDrive\Desktop\Glitch 247 Trades Adaptive v20.csv`
- Events: `C:\Users\alan\Documents\NinjaTrader 8\GlitchData\Telemetry\Glitch247TradeEvents20.csv`
- Telemetry: `C:\Users\alan\Documents\NinjaTrader 8\GlitchData\Telemetry\Glitch247Telemetry20.csv` (chunked aggregates)

## 1) Trades — net PnL, win rate, mean $/trade

| play | n | net PnL | win rate | mean | median profit |
|------|---|---------|----------|------|----------------|
| LL | 467 | -827.70 | 43.90% | -1.77 | -14.10 |
| LS | 339 | 1,318.10 | 49.26% | 3.89 | -1.10 |
| HL | 1325 | 2,154.90 | 57.36% | 1.63 | 17.90 |
| HS | 1833 | 7,505.00 | 57.45% | 4.09 | 18.90 |

**Total net (all rows):** 10,150.30

## 2) Trades — MAE / MFE (dollars per row, Strategy Analyzer)

| play | median MAE | median MFE | median MFE/MAE | median Bars |
|------|------------|------------|----------------|-------------|
| LL | 15.50 | 18.00 | 1.16 | 8 |
| LS | 15.00 | 20.50 | 1.37 | 6 |
| HL | 18.50 | 28.00 | 1.51 | 7 |
| HS | 19.50 | 33.00 | 1.69 | 5 |

### Winners vs losers — mean MAE/MFE by play

**LL** — winners n=205, losers n=262
- Winners: mean MAE 9.07, mean MFE 34.20
- Losers:  mean MAE 27.02, mean MFE 13.87

**LS** — winners n=167, losers n=172
- Winners: mean MAE 8.59, mean MFE 37.89
- Losers:  mean MAE 26.44, mean MFE 14.80

**HL** — winners n=760, losers n=565
- Winners: mean MAE 12.29, mean MFE 42.80
- Losers:  mean MAE 45.14, mean MFE 21.96

**HS** — winners n=1053, losers n=780
- Winners: mean MAE 12.94, mean MFE 51.08
- Losers:  mean MAE 49.55, mean MFE 25.72

## 3) Exit route × play (row counts)

### LL

| exit | count | sum net |
|------|-------|--------|
| daily_limit | 7 | -88.20 |
| hard_stop_cap | 6 | -348.60 |
| session | 2 | -22.20 |
| stop | 247 | -6,760.20 |
| tp1 | 205 | 6,391.50 |

### LS

| exit | count | sum net |
|------|-------|--------|
| daily_limit | 6 | -25.10 |
| hard_stop_cap | 4 | -235.90 |
| session | 1 | -1.10 |
| stop | 162 | -4,378.70 |
| tp1 | 166 | 5,958.90 |

### HL

| exit | count | sum net |
|------|-------|--------|
| daily_limit | 43 | -1,078.90 |
| hard_stop_cap | 23 | -1,648.30 |
| session | 7 | 79.30 |
| stop | 500 | -23,093.50 |
| time | 2 | 125.80 |
| tp1 | 719 | 26,370.10 |
| tp2 | 31 | 1,400.40 |

### HS

| exit | count | sum net |
|------|-------|--------|
| daily_limit | 86 | -1,678.00 |
| hard_stop_cap | 49 | -3,246.50 |
| session | 8 | -82.00 |
| stop | 654 | -33,391.40 |
| time | 2 | 29.80 |
| tp1 | 971 | 42,220.40 |
| tp2 | 63 | 3,652.70 |

## 4) Events (ENTRY) ↔ trades (join)

- ENTRY events: **3481**
- Join: **inner** on `Entry name` = `entry_signal`, keep **nearest `ts_adj`** within **2h** of `Entry time` (one row per trade).
- Trades with a matched ENTRY: **3964** / 3964
- Matched with ADX: **3964**
- **Sanity (HL):** full trades net **2,154.90** vs matched rows net **2,154.90** (should be close)

### ADX at entry — distribution by play (matched rows)

**LL** (n=467): median ADX 32.9, p25 30.9, p75 38.6

**LS** (n=339): median ADX 33.6, p25 31.3, p75 41.1

**HL** (n=1325): median ADX 38.8, p25 36.0, p75 45.1

**HS** (n=1833): median ADX 39.5, p25 36.3, p75 45.5

### HL — net PnL by ADX bucket at entry (matched)

```
         count     sum      mean
adx_bin                         
35-40      740  1235.7  1.669865
40-45      244  1249.5  5.120902
>45        341  -330.3 -0.968622
```

### HS — net PnL by ADX bucket at entry (matched)

```
         count     sum       mean
adx_bin                          
35-40      974   559.6   0.574538
40-45      363  4974.7  13.704408
>45        496  1970.7   3.973185
```

## 5) What-ifs (counterfactuals on **actual** trade PnL)

### A) Remove all **LL** trades
- **Net without LL:** 10,978.00 (vs full 10,150.30)
- **Delta:** +827.70 (removes LL loss)

### B) HL only if ADX at entry ≥ **38** (among matched HL rows)
- Trades kept: **727** / 1325 | HL net would be **1,615.60** vs **2,154.90** (all HL)
- **Δ vs HL baseline:** -539.30

### B) HL only if ADX at entry ≥ **40** (among matched HL rows)
- Trades kept: **585** / 1325 | HL net would be **919.20** vs **2,154.90** (all HL)
- **Δ vs HL baseline:** -1,235.70

### B) HL only if ADX at entry ≥ **42** (among matched HL rows)
- Trades kept: **480** / 1325 | HL net would be **1,198.70** vs **2,154.90** (all HL)
- **Δ vs HL baseline:** -956.20

### B) HL only if ADX at entry ≥ **45** (among matched HL rows)
- Trades kept: **341** / 1325 | HL net would be **-330.30** vs **2,154.90** (all HL)
- **Δ vs HL baseline:** -2,485.20

### C) LL only if ADX ≥ **32** (matched LL rows)
- Kept: **276** / 467 | net **-278.10** vs **-827.70** | **Δ** 549.60

### C) LL only if ADX ≥ **35** (matched LL rows)
- Kept: **168** / 467 | net **12.20** vs **-827.70** | **Δ** 839.90

### C) LL only if ADX ≥ **38** (matched LL rows)
- Kept: **121** / 467 | net **132.40** vs **-827.70** | **Δ** 960.10

### D) Stop exits as % of play (risk of tuning SL)

- **LL**: 52.9% stop exits
- **LS**: 47.8% stop exits
- **HL**: 37.7% stop exits
- **HS**: 35.7% stop exits

## 6) Telemetry — bar-level aggregates (sampled / chunked)

- First ~1M bars: rows with `entry_signal_pass==1`: **253989** / 1000000
- Mean ADX (when pass): **40.00** vs all bars **25.45**
- Mean ATR ticks (when pass): **27.26** vs all **26.23**

*(Full telemetry is ~2.2M rows; extended regime slicing can be added later.)*

## 7) Data-driven optimization levers (priorities)

1. **LL (AdxMinLowConfidenceLong)** — net negative; **§5C** shows raising min ADX on LL (e.g. **≥35** or **≥38**) is strongly associated with **less negative / positive** subset PnL. This is the clearest **portfolio** lift in the what-ifs.
2. **HL — do not assume “higher ADX = better.”** §4 shows HL **>45** bucket net **negative**; naive HL ADX **floors** in §5B **reduce** total HL PnL here. Any HL ADX change should be a **narrow band** (e.g. test **35–45**), not monotonic “more is better.”
3. **HS** — **40–45** ADX bucket dominates HS dollars (§4). Consider **HS-specific** ADX policy vs HL.
4. **Stops vs targets** — majority of gross comes from **TP1 vs Stop** (§3); **TP2** is a small share of rows. Optimize **TP1/SL** before chasing TP2.
5. **Events join** — use `Entry name` = `entry_signal` + **nearest time** within **2h** (after **+2h** on event ts); `merge_asof` alone is wrong when signal names repeat.
