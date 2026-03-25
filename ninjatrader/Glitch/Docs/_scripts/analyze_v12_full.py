from __future__ import annotations

import re
from pathlib import Path

import numpy as np
import pandas as pd

TRADES = Path(r"C:\Users\alan\OneDrive\Desktop\Glitch 247 Trades Adaptive v12.csv")
EVENTS = Path(r"C:\Users\alan\Documents\NinjaTrader 8\GlitchData\Telemetry\Glitch247TradeEvents12.csv")
OUT = Path(r"d:\click-blue\trading\glitch-platform\ninjatrader\Glitch\Docs\glitch-247-v12-analysis-autogen.md")


def money(x: object) -> float:
    s = str(x).strip()
    if not s:
        return np.nan
    neg = "-" in s or "−" in s or "(" in s
    s = re.sub(r"[^0-9,\.]", "", s)
    if not s:
        return np.nan
    if "," in s and "." in s:
        s = s.replace(".", "").replace(",", ".")
    elif "," in s:
        s = s.replace(",", ".")
    v = float(s)
    return -v if neg else v


def parse_play(entry_name: pd.Series) -> pd.Series:
    m = entry_name.astype(str).str.extract(r"_([LS])([HL])_")
    return np.where(
        m[0].eq("L") & m[1].eq("L"),
        "LL",
        np.where(
            m[0].eq("S") & m[1].eq("L"),
            "LS",
            np.where(
                m[0].eq("L") & m[1].eq("H"),
                "HL",
                np.where(m[0].eq("S") & m[1].eq("H"), "HS", "NA"),
            ),
        ),
    )


def run() -> None:
    tr = pd.read_csv(TRADES, sep=";", encoding="utf-8")
    tr["pnl"] = tr["Profit"].map(money)
    tr["mae"] = tr["MAE"].map(money).abs()
    tr["mfe"] = tr["MFE"].map(money).abs()
    tr["qty"] = pd.to_numeric(tr["Qty"], errors="coerce").fillna(1)
    tr["entry_time"] = pd.to_datetime(tr["Entry time"], dayfirst=True, errors="coerce")
    tr["play"] = parse_play(tr["Entry name"])
    tr["side"] = np.where(tr["Market pos."].str.contains("Long", case=False, na=False), "Long", "Short")

    ev = pd.read_csv(EVENTS, low_memory=False)
    ev = ev[ev["event_type"] == "ENTRY"].copy()
    ev["entry_time_raw"] = pd.to_datetime(ev["timestamp"], errors="coerce").dt.tz_localize(None)
    ev = ev.rename(columns={"order_name": "Entry name", "play_type": "play_ev"})
    ev = ev.sort_values("entry_time_raw")

    # Calibrate timestamp offset between trade export and event telemetry.
    # Prior runs showed ~+2h shifts; detect best offset by maximizing near matches.
    candidate_offsets = [pd.Timedelta(hours=h) for h in [-4, -3, -2, -1, 0, 1, 2, 3, 4]]
    best_off = pd.Timedelta(0)
    best_cov = -1.0
    sample = tr[["Entry name", "entry_time"]].dropna().copy()
    sample = sample.sample(min(len(sample), 800), random_state=42) if len(sample) > 800 else sample
    for off in candidate_offsets:
        ev["entry_time"] = ev["entry_time_raw"] + off
        sample_hits = 0
        sample_total = 0
        for name, grp in sample.groupby("Entry name", sort=False):
            e = ev[ev["Entry name"] == name][["entry_time"]].rename(columns={"entry_time": "event_time"}).sort_values("event_time")
            g = grp.sort_values("entry_time")
            if e.empty:
                continue
            jtmp = pd.merge_asof(
                g,
                e,
                left_on="entry_time",
                right_on="event_time",
                direction="nearest",
                tolerance=pd.Timedelta("3min"),
            )
            sample_hits += int(jtmp["event_time"].notna().sum())
            sample_total += len(jtmp)
        cov = (sample_hits / sample_total) if sample_total else 0.0
        if cov > best_cov:
            best_cov = cov
            best_off = off

    ev["entry_time"] = ev["entry_time_raw"] + best_off

    # Join by entry name + nearest timestamp (these names recycle across the run)
    tr = tr.sort_values("entry_time")
    joined = []
    cols = ["entry_time", "Entry name", "adx_now", "selected_stop_ticks", "selected_tp1_ticks", "selected_tp2_ticks", "atr_ticks_entry", "confidence_score"]
    for name, grp in tr.groupby("Entry name", sort=False):
        e = ev[ev["Entry name"] == name][cols].sort_values("entry_time")
        g = grp.sort_values("entry_time")
        if e.empty:
            for c in ["adx_now", "selected_stop_ticks", "selected_tp1_ticks", "selected_tp2_ticks", "atr_ticks_entry", "confidence_score"]:
                g[c] = np.nan
            joined.append(g)
            continue
        j = pd.merge_asof(
            g,
            e.drop(columns=["Entry name"]),
            on="entry_time",
            direction="nearest",
            tolerance=pd.Timedelta("3min"),
        )
        joined.append(j)
    j = pd.concat(joined, ignore_index=True)

    cov = float(j["adx_now"].notna().mean())
    by_play = (
        j.groupby("play")
        .agg(
            trades=("pnl", "size"),
            net=("pnl", "sum"),
            avg=("pnl", "mean"),
            winrate=("pnl", lambda s: (s > 0).mean()),
            pf=("pnl", lambda s: s[s > 0].sum() / abs(s[s < 0].sum()) if (s < 0).any() else np.nan),
            mae=("mae", "mean"),
            mfe=("mfe", "mean"),
        )
        .sort_index()
    )
    by_exit = (
        j.groupby(["play", "Exit name"])
        .agg(trades=("pnl", "size"), net=("pnl", "sum"), avg=("pnl", "mean"))
        .sort_values(["play", "net"], ascending=[True, False])
    )

    jj = j[j["adx_now"].notna()].copy()
    adx_lines = []
    for p in ["LL", "LS", "HL", "HS"]:
        s = jj[jj["play"] == p]
        if len(s) < 30:
            continue
        wm = s.loc[s["pnl"] > 0, "adx_now"].median()
        lm = s.loc[s["pnl"] <= 0, "adx_now"].median()
        adx_lines.append(f"- `{p}` winners median ADX `{wm:.2f}`, losers `{lm:.2f}`, delta `{wm-lm:.2f}`")

    # ADX threshold what-ifs
    whatif = {}
    for p in ["LL", "LS", "HL", "HS"]:
        s = jj[jj["play"] == p]
        if len(s) < 50:
            continue
        rows = []
        for k in range(18, 56, 2):
            t = s[s["adx_now"] >= k]
            if len(t) < 30:
                continue
            rows.append(
                {
                    "k": k,
                    "n": len(t),
                    "net": t["pnl"].sum(),
                    "avg": t["pnl"].mean(),
                    "wr": (t["pnl"] > 0).mean(),
                    "pf": t.loc[t["pnl"] > 0, "pnl"].sum() / abs(t.loc[t["pnl"] < 0, "pnl"].sum())
                    if (t["pnl"] < 0).any()
                    else np.nan,
                }
            )
        if rows:
            w = pd.DataFrame(rows)
            best = w.loc[w["net"].idxmax()]
            base = len(s)
            c = w[w["n"] >= 0.6 * base]
            balanced = c.loc[c["net"].idxmax()] if len(c) else None
            whatif[p] = (best, balanced)

    # R-multiple style diagnostics based on selected_stop_ticks from events join
    mnq_dpt = 0.5
    j2 = jj.copy()
    j2["risk_dollars"] = pd.to_numeric(j2["selected_stop_ticks"], errors="coerce") * j2["qty"] * mnq_dpt
    j2 = j2[j2["risk_dollars"] > 0]
    j2["r_pnl"] = j2["pnl"] / j2["risk_dollars"]
    j2["r_mae"] = j2["mae"] / j2["risk_dollars"]
    j2["r_mfe"] = j2["mfe"] / j2["risk_dollars"]

    lines = []
    lines.append("# v12 Analysis (trades + trade events)\n\n")
    lines.append(f"- Trades file: `{TRADES}`\n")
    lines.append(f"- Events file: `{EVENTS}`\n")
    lines.append(f"- ADX join coverage: `{cov:.1%}`\n")
    lines.append(f"- Time offset applied (events -> trades): `{best_off}`\n")
    lines.append(
        f"- Net PnL: all `${j['pnl'].sum():,.2f}`, long `${j.loc[j['side']=='Long','pnl'].sum():,.2f}`, short `${j.loc[j['side']=='Short','pnl'].sum():,.2f}`\n\n"
    )
    lines.append("## By play type\n\n")
    lines.append(by_play.round(3).to_string())
    lines.append("\n\n## By play x exit type\n\n")
    lines.append(by_exit.round(2).to_string())
    lines.append("\n\n## ADX winners vs losers\n")
    lines.extend([x + "\n" for x in adx_lines])
    lines.append("\n## ADX what-ifs (entry filter only)\n")
    for p in ["LL", "LS", "HL", "HS"]:
        if p not in whatif:
            continue
        b, bal = whatif[p]
        lines.append(
            f"- `{p}` best-net floor: `ADX >= {int(b['k'])}` -> n `{int(b['n'])}`, net `${b['net']:,.2f}`, avg `${b['avg']:.2f}`, win `{b['wr']:.1%}`, PF `{b['pf']:.3f}`\n"
        )
        if bal is not None:
            lines.append(
                f"  - balanced floor (>=60% sample): `ADX >= {int(bal['k'])}` -> n `{int(bal['n'])}`, net `${bal['net']:,.2f}`, avg `${bal['avg']:.2f}`, win `{bal['wr']:.1%}`, PF `{bal['pf']:.3f}`\n"
            )

    lines.append("\n## Stop/Target what-ifs (using realized MAE/MFE bounds)\n")
    for p in ["LL", "LS", "HL", "HS"]:
        s = j2[j2["play"] == p]
        if len(s) < 30:
            continue
        lines.append(
            f"- `{p}` n `{len(s)}`: mean R pnl `{s['r_pnl'].mean():.3f}`, med R pnl `{s['r_pnl'].median():.3f}`, med R MAE `{s['r_mae'].median():.3f}`, med R MFE `{s['r_mfe'].median():.3f}`\n"
        )
        covs = " ".join([f"{x:.2f}:{(s['r_mfe']>=x).mean():.2f}" for x in [1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0]])
        lines.append(f"  - fraction reaching R targets (MFE>=R): {covs}\n")

    OUT.write_text("".join(lines), encoding="utf-8")
    print("".join(lines).encode("ascii", errors="replace").decode("ascii"))
    print(f"\nWrote {OUT}")


if __name__ == "__main__":
    run()

