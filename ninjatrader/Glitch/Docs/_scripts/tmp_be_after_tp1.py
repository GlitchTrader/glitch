#!/usr/bin/env python3
"""
What-if: move SL to breakeven after TP1 fills on HL/HS (2-leg trades).

Uses Strategy Analyzer export: pair rows with same Entry name + Entry time.
"""
from __future__ import annotations

import re
from pathlib import Path

import numpy as np
import pandas as pd

TRADES = Path(r"C:\Users\alan\OneDrive\Desktop\Glitch 247 Trades Adaptive v20.csv")
OUT = Path(__file__).resolve().parent.parent / "glitch-247-be-after-tp1-whatif-autogen.md"


def money(s) -> float:
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


def play(name: str) -> str:
    m = re.search(r"Glitch247Strategy_([LS])([LH])_", str(name))
    if not m:
        return "UNK"
    a, b = m.group(1), m.group(2)
    if a == "L" and b == "H":
        return "HL"
    if a == "S" and b == "H":
        return "HS"
    return "XX"


def exit_norm(x: str) -> str:
    s = str(x).strip()
    if s == "Target2":
        return "tp2"
    if s == "Target":
        return "tp1"
    if s == "Stop":
        return "stop"
    return "other"


def main() -> None:
    tr = pd.read_csv(TRADES, sep=";", engine="python")
    tr["profit_num"] = tr["Profit"].map(money)
    tr["play"] = tr["Entry name"].map(play)
    tr["ex"] = tr["Exit name"].map(exit_norm)
    tr["entry_ts"] = pd.to_datetime(tr["Entry time"], dayfirst=True, errors="coerce")
    tr["exit_ts"] = pd.to_datetime(tr["Exit time"], dayfirst=True, errors="coerce")

    hc = tr[tr["play"].isin(["HL", "HS"])].copy()
    hc["gkey"] = hc["Entry name"].astype(str) + "|" + hc["entry_ts"].astype(str)

    lines: list[str] = []
    lines.append("# HL/HS — breakeven after TP1 (what-if)\n\n")
    lines.append(
        "**Method:** Group rows by `Entry name` + `Entry time`. "
        "Sort legs by `Exit time` (then Trade number). "
        "If leg1 is **TP1** (`Target`) and leg2 is **Stop**, replace leg2 PnL with **0** (breakeven; ignores minor commission asymmetry).\n\n"
    )

    delta_total = 0.0
    n_pairs_tp1_stop = 0
    n_pairs_tp1_tp2 = 0
    by_play = {"HL": 0.0, "HS": 0.0}
    n_by = {"HL": 0, "HS": 0}

    # Single-row groups (no split)
    singles = hc.groupby("gkey").filter(lambda x: len(x) == 1)
    pairs = hc.groupby("gkey").filter(lambda x: len(x) == 2)
    multi = hc.groupby("gkey").filter(lambda x: len(x) > 2)

    lines.append(f"- HL+HS rows: **{len(hc)}**\n")
    lines.append(f"- Groups with **2 rows** (typical 2-leg): **{pairs.groupby('gkey').ngroups}** groups\n")
    lines.append(f"- Groups with **1 row**: **{singles.groupby('gkey').ngroups}**\n")
    lines.append(f"- Groups with **>2 rows**: **{multi.groupby('gkey').ngroups}** (excluded from pairing logic)\n\n")

    for gkey, g in pairs.groupby("gkey"):
        g = g.sort_values(["exit_ts", "Trade number"])
        row1, row2 = g.iloc[0], g.iloc[1]
        pl = row1["play"]
        e1, e2 = row1["ex"], row2["ex"]
        if e1 == "tp1" and e2 == "stop":
            # Second leg would have been stopped; at BE -> 0 instead of loss
            old_p2 = row2["profit_num"]
            new_p2 = 0.0
            d = new_p2 - old_p2
            delta_total += d
            n_pairs_tp1_stop += 1
            by_play[pl] += d
            n_by[pl] += 1
        elif e1 == "tp1" and e2 == "tp2":
            n_pairs_tp1_tp2 += 1

    lines.append("## Pairs: TP1 then Stop (BE helps second leg)\n\n")
    lines.append(f"- Count: **{n_pairs_tp1_stop}**\n")
    lines.append(f"- **Δ PnL** (replace leg2 with 0): **{delta_total:,.2f}**\n")
    lines.append(f"  - HL: **{by_play['HL']:,.2f}** ({n_by['HL']} pairs)\n")
    lines.append(f"  - HS: **{by_play['HS']:,.2f}** ({n_by['HS']} pairs)\n\n")

    lines.append("## Pairs: TP1 then TP2 (unchanged under BE rule)\n\n")
    lines.append(f"- Count: **{n_pairs_tp1_tp2}**\n\n")

    # Baseline HL+HS net from file
    base_net = hc["profit_num"].sum()
    lines.append("## Portfolio impact (HL+HS rows only)\n\n")
    lines.append(f"- **HL+HS net (actual):** {base_net:,.2f}\n")
    lines.append(f"- **HL+HS net (what-if):** {base_net + delta_total:,.2f}\n")
    lines.append(f"- **Δ on HL+HS:** {delta_total:,.2f}\n\n")

    all_net = tr["profit_num"].sum()
    lines.append("## All trades\n\n")
    lines.append(f"- **Full strategy net (actual):** {all_net:,.2f}\n")
    lines.append(f"- **Full strategy net (what-if):** {all_net + delta_total:,.2f}\n")
    lines.append(f"- **Δ total:** {delta_total:,.2f}\n\n")

    # Distribution of leg2 losses we're removing
    leg2_losses = []
    for gkey, g in pairs.groupby("gkey"):
        g = g.sort_values(["exit_ts", "Trade number"])
        row1, row2 = g.iloc[0], g.iloc[1]
        if row1["ex"] == "tp1" and row2["ex"] == "stop":
            leg2_losses.append(row2["profit_num"])
    if leg2_losses:
        arr = np.array(leg2_losses)
        lines.append("### Second-leg PnL (Stop) before BE replacement\n\n")
        lines.append(f"- Mean: **{arr.mean():.2f}**, Median: **{np.median(arr):.2f}**, Min: **{arr.min():.2f}**, Max: **{arr.max():.2f}**\n")

    OUT.write_text("".join(lines), encoding="utf-8")
    print(f"Wrote {OUT}")
    print(f"delta_total={delta_total:.2f} pairs_tp1_stop={n_pairs_tp1_stop}")


if __name__ == "__main__":
    main()
