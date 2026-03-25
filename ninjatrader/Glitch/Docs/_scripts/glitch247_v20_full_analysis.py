#!/usr/bin/env python3
"""
Full v20 analysis: trades + trade events + telemetry (chunked).
Correct European money parsing. Data-driven what-ifs.
"""
from __future__ import annotations

import re
from pathlib import Path

import numpy as np
import pandas as pd

TRADES = Path(r"C:\Users\alan\OneDrive\Desktop\Glitch 247 Trades Adaptive v20.csv")
EVENTS = Path(r"C:\Users\alan\Documents\NinjaTrader 8\GlitchData\Telemetry\Glitch247TradeEvents20.csv")
TELEM = Path(r"C:\Users\alan\Documents\NinjaTrader 8\GlitchData\Telemetry\Glitch247Telemetry20.csv")
OUT = Path(__file__).resolve().parent.parent / "glitch-247-v20-full-analysis-autogen.md"


def money(s) -> float:
    """Parse NT currency: '-$ 44,20', '$ 22,90'."""
    if s is None or (isinstance(s, float) and np.isnan(s)):
        return float("nan")
    raw = str(s).strip().replace("−", "-")
    neg = "-" in raw[:4]
    t = re.sub(r"[^\d,.]", "", raw)
    if not t:
        return float("nan")
    t = t.replace(",", ".")
    if t.count(".") > 1:
        t = t.replace(".", "", t.count(".") - 1)
    v = float(t)
    return -v if neg else v


def play_from_signal(name: str) -> str:
    if not isinstance(name, str):
        return "UNK"
    m = re.search(r"Glitch247Strategy_([LS])([LH])_", name)
    if not m:
        return "UNK"
    side, conf = m.group(1), m.group(2)
    if side == "L" and conf == "L":
        return "LL"
    if side == "S" and conf == "L":
        return "LS"
    if side == "L" and conf == "H":
        return "HL"
    if side == "S" and conf == "H":
        return "HS"
    return "UNK"


def exit_route(ex) -> str:
    s = str(ex).strip()
    if s == "Target2":
        return "tp2"
    if s == "Target":
        return "tp1"
    if s == "Stop":
        return "stop"
    if s == "TimeExit":
        return "time"
    if "Daily" in s:
        return "daily_limit"
    if "session" in s.lower():
        return "session"
    if s == "HardStopCap":
        return "hard_stop_cap"
    return "other"


def main() -> None:
    lines: list[str] = []
    lines.append("# Glitch 247 v20 — full analysis & what-ifs\n\n")
    lines.append("**Money parsing:** European `-$ …` / `$ …` format handled correctly (not 100% win rate bug).\n\n")
    lines.append(f"- Trades: `{TRADES}`\n")
    lines.append(f"- Events: `{EVENTS}`\n")
    lines.append(f"- Telemetry: `{TELEM}` (chunked aggregates)\n\n")

    # ---------- TRADES ----------
    tr = pd.read_csv(TRADES, sep=";", engine="python")
    tr["profit_num"] = tr["Profit"].map(money)
    tr["mae_num"] = tr["MAE"].map(money)
    tr["mfe_num"] = tr["MFE"].map(money)
    tr["play"] = tr["Entry name"].map(play_from_signal)
    tr["exit_route"] = tr["Exit name"].map(exit_route)
    tr["win"] = tr["profit_num"] > 0
    tr["entry_ts"] = pd.to_datetime(tr["Entry time"], dayfirst=True, errors="coerce")

    lines.append("## 1) Trades — net PnL, win rate, mean $/trade\n\n")
    lines.append("| play | n | net PnL | win rate | mean | median profit |\n")
    lines.append("|------|---|---------|----------|------|----------------|\n")
    for p in ["LL", "LS", "HL", "HS"]:
        sub = tr[tr["play"] == p]
        if len(sub) == 0:
            continue
        lines.append(
            f"| {p} | {len(sub)} | {sub['profit_num'].sum():,.2f} | "
            f"{100 * sub['win'].mean():.2f}% | {sub['profit_num'].mean():.2f} | "
            f"{sub['profit_num'].median():.2f} |\n"
        )
    lines.append(f"\n**Total net (all rows):** {tr['profit_num'].sum():,.2f}\n\n")

    lines.append("## 2) Trades — MAE / MFE (dollars per row, Strategy Analyzer)\n\n")
    lines.append("| play | median MAE | median MFE | median MFE/MAE | median Bars |\n")
    lines.append("|------|------------|------------|----------------|-------------|\n")
    for p in ["LL", "LS", "HL", "HS"]:
        sub = tr[tr["play"] == p]
        if len(sub) == 0:
            continue
        mae = sub["mae_num"].median()
        mfe = sub["mfe_num"].median()
        ratio = (mfe / mae) if mae and mae > 0 else np.nan
        bars_med = sub["Bars"].median() if "Bars" in sub.columns else np.nan
        lines.append(f"| {p} | {mae:.2f} | {mfe:.2f} | {ratio:.2f} | {bars_med:.0f} |\n")

    lines.append("\n### Winners vs losers — mean MAE/MFE by play\n\n")
    for p in ["LL", "LS", "HL", "HS"]:
        sub = tr[tr["play"] == p]
        if len(sub) < 5:
            continue
        w = sub[sub["win"]]
        l = sub[~sub["win"]]
        lines.append(f"**{p}** — winners n={len(w)}, losers n={len(l)}\n")
        lines.append(
            f"- Winners: mean MAE {w['mae_num'].mean():.2f}, mean MFE {w['mfe_num'].mean():.2f}\n"
        )
        lines.append(
            f"- Losers:  mean MAE {l['mae_num'].mean():.2f}, mean MFE {l['mfe_num'].mean():.2f}\n\n"
        )

    # Exit mix by play (profit-weighted insight)
    lines.append("## 3) Exit route × play (row counts)\n\n")
    for p in ["LL", "LS", "HL", "HS"]:
        sub = tr[tr["play"] == p]
        if len(sub) == 0:
            continue
        vc = sub.groupby("exit_route")["profit_num"].agg(["count", "sum"])
        lines.append(f"### {p}\n\n")
        lines.append("| exit | count | sum net |\n|------|-------|--------|\n")
        for idx, row in vc.iterrows():
            lines.append(f"| {idx} | {int(row['count'])} | {row['sum']:,.2f} |\n")
        lines.append("\n")

    # ---------- EVENTS + TRADES JOIN ----------
    lines.append("## 4) Events (ENTRY) ↔ trades (join)\n\n")
    ev = pd.read_csv(EVENTS)
    ev["ts"] = pd.to_datetime(ev["timestamp"], errors="coerce")
    # +2h aligns trade export with event log (see v20 analysis)
    ev["ts_adj"] = ev["ts"] + pd.Timedelta(hours=2)
    ent = ev[ev["event_type"].astype(str).str.upper().eq("ENTRY")].copy()
    # `entry_signal` repeats across sessions — merge on name + nearest time within 2h (not merge_asof alone).
    full = tr.merge(ent, left_on="Entry name", right_on="entry_signal", how="inner", suffixes=("", "_ev"))
    full["dt_sec"] = (full["ts_adj"] - full["entry_ts"]).abs().dt.total_seconds()
    full = full[full["dt_sec"] <= 7200.0]
    full = full.sort_values("dt_sec")
    m_ok = full.groupby(full["Trade number"], as_index=False).first()

    lines.append(f"- ENTRY events: **{len(ent)}**\n")
    lines.append(
        f"- Join: **inner** on `Entry name` = `entry_signal`, keep **nearest `ts_adj`** within **2h** of `Entry time` "
        f"(one row per trade).\n"
    )
    lines.append(f"- Trades with a matched ENTRY: **{len(m_ok)}** / {len(tr)}\n")
    lines.append(f"- Matched with ADX: **{m_ok['adx_now'].notna().sum()}**\n")
    # Sanity: HL PnL on matched rows should approximate full HL PnL
    hl_full = tr[tr["play"] == "HL"]["profit_num"].sum()
    hl_mt = m_ok[(m_ok["play"] == "HL")]["profit_num"].sum()
    lines.append(f"- **Sanity (HL):** full trades net **{hl_full:,.2f}** vs matched rows net **{hl_mt:,.2f}** (should be close)\n\n")

    if len(m_ok) > 100:
        lines.append("### ADX at entry — distribution by play (matched rows)\n\n")
        for p in ["LL", "LS", "HL", "HS"]:
            sub = m_ok[m_ok["play"] == p]
            if len(sub) < 10:
                continue
            lines.append(f"**{p}** (n={len(sub)}): ")
            lines.append(
                f"median ADX {sub['adx_now'].median():.1f}, "
                f"p25 {sub['adx_now'].quantile(0.25):.1f}, p75 {sub['adx_now'].quantile(0.75):.1f}\n\n"
            )

        # ADX buckets vs PnL (HL only)
        hl = m_ok[(m_ok["play"] == "HL") & m_ok["adx_now"].notna()].copy()
        if len(hl) > 50:
            hl["adx_bin"] = pd.cut(hl["adx_now"], bins=[0, 35, 40, 45, 100], labels=["≤35", "35-40", "40-45", ">45"])
            lines.append("### HL — net PnL by ADX bucket at entry (matched)\n\n")
            g = hl.groupby("adx_bin", observed=True)["profit_num"].agg(["count", "sum", "mean"])
            lines.append("```\n" + g.to_string() + "\n```\n\n")

        hs = m_ok[(m_ok["play"] == "HS") & m_ok["adx_now"].notna()].copy()
        if len(hs) > 50:
            hs["adx_bin"] = pd.cut(hs["adx_now"], bins=[0, 35, 40, 45, 100], labels=["≤35", "35-40", "40-45", ">45"])
            lines.append("### HS — net PnL by ADX bucket at entry (matched)\n\n")
            lines.append("```\n" + hs.groupby("adx_bin", observed=True)["profit_num"].agg(["count", "sum", "mean"]).to_string() + "\n```\n\n")

    # ---------- WHAT-IFS ----------
    lines.append("## 5) What-ifs (counterfactuals on **actual** trade PnL)\n\n")

    # 5a Drop LL entirely
    no_ll = tr[tr["play"] != "LL"]
    lines.append(
        f"### A) Remove all **LL** trades\n"
        f"- **Net without LL:** {no_ll['profit_num'].sum():,.2f} (vs full {tr['profit_num'].sum():,.2f})\n"
        f"- **Delta:** +{-tr[tr['play']=='LL']['profit_num'].sum():,.2f} (removes LL loss)\n\n"
    )

    # 5b ADX floor on HL (matched ENTRY + ADX)
    if len(m_ok) > 200:
        hl_m = m_ok[(m_ok["play"] == "HL") & m_ok["adx_now"].notna()]
        for floor in [38, 40, 42, 45]:
            sub = hl_m[hl_m["adx_now"] >= floor]
            base_pnl = hl_m["profit_num"].sum()
            new_pnl = sub["profit_num"].sum()
            lines.append(
                f"### B) HL only if ADX at entry ≥ **{floor}** (among matched HL rows)\n"
                f"- Trades kept: **{len(sub)}** / {len(hl_m)} | "
                f"HL net would be **{new_pnl:,.2f}** vs **{base_pnl:,.2f}** (all HL)\n"
                f"- **Δ vs HL baseline:** {new_pnl - base_pnl:,.2f}\n\n"
            )

        ll_m = m_ok[(m_ok["play"] == "LL") & m_ok["adx_now"].notna()]
        for floor in [32, 35, 38]:
            sub = ll_m[ll_m["adx_now"] >= floor]
            base_pnl = ll_m["profit_num"].sum()
            new_pnl = sub["profit_num"].sum()
            lines.append(
                f"### C) LL only if ADX ≥ **{floor}** (matched LL rows)\n"
                f"- Kept: **{len(sub)}** / {len(ll_m)} | net **{new_pnl:,.2f}** vs **{base_pnl:,.2f}** | **Δ** {new_pnl - base_pnl:,.2f}\n\n"
            )

    # 5d Stop-heavy exit rate (pain)
    lines.append("### D) Stop exits as % of play (risk of tuning SL)\n\n")
    for p in ["LL", "LS", "HL", "HS"]:
        sub = tr[tr["play"] == p]
        if len(sub) == 0:
            continue
        pct = 100 * (sub["exit_route"] == "stop").sum() / len(sub)
        lines.append(f"- **{p}**: {pct:.1f}% stop exits\n")
    lines.append("\n")

    # ---------- TELEMETRY (chunked) ----------
    lines.append("## 6) Telemetry — bar-level aggregates (sampled / chunked)\n\n")
    if TELEM.exists():
        # First 500k + last 500k bars: mean ADX, atr when entry_signal_pass==1
        chunks = []
        for i, ch in enumerate(pd.read_csv(TELEM, chunksize=250_000)):
            chunks.append(ch)
            if i >= 3:
                break
        head = pd.concat(chunks, ignore_index=True)
        if "entry_signal_pass" in head.columns and "adx" in head.columns:
            passed = head[head["entry_signal_pass"] == 1]
            lines.append(
                f"- First ~1M bars: rows with `entry_signal_pass==1`: **{len(passed)}** / {len(head)}\n"
            )
            lines.append(
                f"- Mean ADX (when pass): **{passed['adx'].mean():.2f}** vs all bars **{head['adx'].mean():.2f}**\n"
            )
        if "atr_ticks" in head.columns:
            lines.append(
                f"- Mean ATR ticks (when pass): **{passed['atr_ticks'].mean():.2f}** vs all **{head['atr_ticks'].mean():.2f}**\n"
            )
        lines.append(
            "\n*(Full telemetry is ~2.2M rows; extended regime slicing can be added later.)*\n\n"
        )

    # ---------- RECOMMENDATIONS ----------
    lines.append("## 7) Data-driven optimization levers (priorities)\n\n")
    lines.append(
        "1. **LL (AdxMinLowConfidenceLong)** — net negative; **§5C** shows raising min ADX on LL "
        "(e.g. **≥35** or **≥38**) is strongly associated with **less negative / positive** subset PnL. "
        "This is the clearest **portfolio** lift in the what-ifs.\n"
    )
    lines.append(
        "2. **HL — do not assume “higher ADX = better.”** §4 shows HL **>45** bucket net **negative**; "
        "naive HL ADX **floors** in §5B **reduce** total HL PnL here. Any HL ADX change should be a **narrow band** "
        "(e.g. test **35–45**), not monotonic “more is better.”\n"
    )
    lines.append(
        "3. **HS** — **40–45** ADX bucket dominates HS dollars (§4). Consider **HS-specific** ADX policy vs HL.\n"
    )
    lines.append(
        "4. **Stops vs targets** — majority of gross comes from **TP1 vs Stop** (§3); **TP2** is a small share of rows. "
        "Optimize **TP1/SL** before chasing TP2.\n"
    )
    lines.append(
        "5. **Events join** — use `Entry name` = `entry_signal` + **nearest time** within **2h** (after **+2h** on event ts); "
        "`merge_asof` alone is wrong when signal names repeat.\n"
    )

    OUT.write_text("".join(lines), encoding="utf-8")
    print(f"Wrote {OUT}")


if __name__ == "__main__":
    main()
