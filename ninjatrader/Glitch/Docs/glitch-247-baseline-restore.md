# Glitch 247 — matching a prior Strategy Analyzer baseline

Workspace defaults in `Glitch247Strategy.cs` can drift from an older **saved NinjaTrader template** (`.xml`) even when ADX / TP / SL numbers look the same.

See also: **`glitch-247-v4-v7-strategy-evolution.md`** (historical v4 vs v7 trade + telemetry analysis; some items describe **older** strategy behavior).

## Current strategy (entry filters)

`Glitch247Strategy` uses **ADX minima per play type**, **ATR-sized** stop/target ticks (with per-direction **SL/TP ATR multiples** and a **global stop hard cap**), **trend** inputs (KAMA / DM / LinReg), **daily PnL limits**, and optional **recovery mode**. There is **no** adaptive median ATR gate, **no** global “min 1 ATR ticks” filter, and **no** per-play min ATR tick gates — those were removed to simplify the UI and logic.

**Trailing stops** are **not** in the strategy; stops are fixed at entry ± ATR-derived risk.

## What commonly breaks “restore baseline”

1. **Enable Low Confidence Entries** — must match the reference run (`true` vs `false` changes LL/LS volume).
2. **Recovery mode, daily limits, commission template** — must match the reference run (not all are obvious from equity alone).
3. **Instrument, session template, and date range** — must match.

## Recommended workflow

1. Open the **reference** Strategy Analyzer configuration and note: low-conf toggle, ADX/ATR multiples, hard cap, daily limits, recovery.
2. Set the same values in the UI (or save/load that template).
3. Re-run the same instrument, session, and date range.

Telemetry file name defaults may change between passes (`Glitch247TradeEvents*.csv`); that does not affect fills.
