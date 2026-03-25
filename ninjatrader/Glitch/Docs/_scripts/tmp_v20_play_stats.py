#!/usr/bin/env python3
"""v20 trades: PnL, win rate, exit route (TP1/TP2/stop) by play type."""
from __future__ import annotations

import re
from pathlib import Path

import numpy as np
import pandas as pd

TRADES = Path(r"C:\Users\alan\OneDrive\Desktop\Glitch 247 Trades Adaptive v20.csv")
OUT = Path(__file__).resolve().parent.parent / "glitch-247-v20-play-stats-autogen.md"


def money(s) -> float:
    """Parse NT export profit (e.g. '-$ 44,20' or '$ 22,90')."""
    if s is None or (isinstance(s, float) and np.isnan(s)):
        return float("nan")
    raw = str(s).strip().replace("−", "-")
    neg = "-" in raw[:4]  # leading minus / '-$'
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
    tr = pd.read_csv(TRADES, sep=";", engine="python")
    tr["profit_num"] = tr["Profit"].map(money)
    tr["play"] = tr["Entry name"].map(play_from_signal)
    tr["exit_route"] = tr["Exit name"].map(exit_route)

    lines: list[str] = []
    lines.append("# Glitch 247 v20 — per-play PnL, win rate, exits\n")
    lines.append(f"Source: `{TRADES}`\n")
    lines.append(
        "`Exit name` mapping: `Target` → TP1, `Target2` → TP2, `Stop` → stop "
        "(matches your strategy order names).\n"
    )

    lines.append("\n## Net PnL & win rate by play type\n")
    lines.append("| play | n | net PnL | win rate | mean $/trade |\n")
    lines.append("|------|---|---------|----------|-------------|\n")
    for p in ["LL", "LS", "HL", "HS"]:
        sub = tr[tr["play"] == p]
        if len(sub) == 0:
            continue
        w = (sub["profit_num"] > 0).sum()
        lines.append(
            f"| **{p}** | {len(sub)} | {sub['profit_num'].sum():,.2f} | "
            f"{100 * w / len(sub):.2f}% | {sub['profit_num'].mean():.2f} |\n"
        )

    def section(title: str, mask: pd.Series) -> None:
        lines.append(f"\n## {title}\n")
        sub = tr[mask]
        vc = sub["exit_route"].value_counts()
        total = len(sub)
        lines.append(f"Rows: **{total}**\n")
        lines.append("| exit_route | count | % of rows |\n")
        lines.append("|------------|-------|----------|\n")
        for route, c in vc.items():
            lines.append(f"| {route} | {c} | {100 * c / total:.2f}% |\n")

    section("Longs — all (LL + HL)", tr["play"].isin(["LL", "HL"]))
    section("Longs — LL only", tr["play"].eq("LL"))
    section("Longs — HL only", tr["play"].eq("HL"))

    lines.append("\n### Longs (HL) — TP2 vs not TP2 (row counts)\n")
    hl = tr[tr["play"] == "HL"]
    n_hl = len(hl)
    n_tp2 = (hl["exit_route"] == "tp2").sum()
    lines.append(f"- **TP2 exit** (`Exit name` = Target2): **{n_tp2}** ({100 * n_tp2 / n_hl:.2f}% of HL rows)\n")
    lines.append(f"- **Not TP2** (stop, TP1, time, etc.): **{n_hl - n_tp2}** ({100 * (n_hl - n_tp2) / n_hl:.2f}%)\n")
    n_tp1 = (hl["exit_route"] == "tp1").sum()
    lines.append(f"- **TP1 exit** (`Target`): **{n_tp1}** ({100 * n_tp1 / n_hl:.2f}% of HL rows)\n")

    lines.append("\n### Longs (LL) — TP1 vs stop (LL has no TP2 in strategy)\n")
    ll = tr[tr["play"] == "LL"]
    n_ll = len(ll)
    lines.append(f"- **TP1** (`Target`): **{(ll['exit_route'] == 'tp1').sum()}** / {n_ll}\n")
    lines.append(f"- **Stop**: **{(ll['exit_route'] == 'stop').sum()}** / {n_ll}\n")

    section("Shorts — all (LS + HS)", tr["play"].isin(["LS", "HS"]))
    section("Shorts — LS only", tr["play"].eq("LS"))
    section("Shorts — HS only", tr["play"].eq("HS"))

    lines.append("\n### Shorts (HS) — TP2 vs not TP2\n")
    hs = tr[tr["play"] == "HS"]
    n_hs = len(hs)
    n_tp2_hs = (hs["exit_route"] == "tp2").sum()
    lines.append(f"- **TP2** (`Target2`): **{n_tp2_hs}** ({100 * n_tp2_hs / n_hs:.2f}% of HS rows)\n")
    lines.append(f"- **Not TP2**: **{n_hs - n_tp2_hs}** ({100 * (n_hs - n_tp2_hs) / n_hs:.2f}%)\n")
    lines.append(
        f"- **TP1** (`Target`): **{(hs['exit_route'] == 'tp1').sum()}** "
        f"({100 * (hs['exit_route'] == 'tp1').sum() / n_hs:.2f}% of HS rows)\n"
    )

    lines.append("\n## v21 note\n")
    lines.append(
        "Code defaults are already **HL = 35, 2, 2, 3** (ADX, SL, TP1, TP2). "
        "Surrogate v21 was a *small* tweak suggestion (e.g. ADX/TP2), not “change everything.” "
        "If you want **no change**, keep **35 / 2 / 2 / 3** until your own read of the table above says otherwise.\n"
    )

    OUT.write_text("".join(lines), encoding="utf-8")
    print(f"Wrote {OUT}")


if __name__ == "__main__":
    main()
