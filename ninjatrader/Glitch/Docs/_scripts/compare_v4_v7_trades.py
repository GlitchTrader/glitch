"""
Compare Glitch 247 trade exports (v4 vs v7) + sample telemetry.
Run from repo: python ninjatrader/Glitch/Docs/_scripts/compare_v4_v7_trades.py
"""
from __future__ import annotations

import re
from pathlib import Path

import numpy as np
import pandas as pd

V4 = Path(r"C:\Users\alan\OneDrive\Desktop\Glitch 247 Trades Adaptive v4.csv")
V7 = Path(r"C:\Users\alan\OneDrive\Desktop\Glitch 247 Trades Adaptive v7.csv")
TELEMETRY = Path(r"C:\Users\alan\Documents\NinjaTrader 8\GlitchData\Telemetry\Glitch247Telemetry.csv")


def money(x) -> float:
    s = str(x).strip()
    if not s or s.lower() == "nan":
        return np.nan
    neg = bool(re.search(r"^\s*[-–−\u2212]", s)) or s.strip().startswith("(")
    s = re.sub(r"[^\d,.\-]", "", s.replace("-", "").replace("−", "").replace("–", ""))
    if not s:
        return np.nan
    if "," in s and "." in s:
        s = s.replace(".", "").replace(",", ".")
    elif "," in s:
        s = s.replace(",", ".")
    v = float(s)
    return -abs(v) if neg else v


def load_trades(path: Path) -> pd.DataFrame:
    df = pd.read_csv(path, sep=";", encoding="utf-8")
    df["pnl"] = df["Profit"].apply(money)
    df["is_long"] = df["Market pos."].astype(str).str.contains("Long", case=False)
    df["play"] = df["Entry name"].astype(str).str.extract(r"_(L|S)(H|L)_", expand=True)[1]
    df["entry_ts"] = pd.to_datetime(df["Entry time"], dayfirst=True, errors="coerce")
    return df


def max_dd_from_pnl_series(pnl: np.ndarray) -> float:
    if len(pnl) == 0:
        return 0.0
    cum = np.cumsum(pnl)
    peak = np.maximum.accumulate(cum)
    dd = cum - peak
    return float(np.min(dd)) if len(dd) else 0.0


def side_equity_dd(df: pd.DataFrame, is_long: bool) -> tuple[float, float, float]:
    """Chronological cumulative PnL for one side only; return net, max_dd, n."""
    sub = df[df["is_long"] == is_long].sort_values("entry_ts")
    pnl = sub["pnl"].values
    net = float(np.nansum(pnl))
    dd = max_dd_from_pnl_series(pnl)
    return net, dd, len(sub)


def report(name: str, df: pd.DataFrame) -> dict:
    net = df["pnl"].sum()
    long_net, long_dd, long_n = side_equity_dd(df, True)
    short_net, short_dd, short_n = side_equity_dd(df, False)
    wins = (df["pnl"] > 0).sum()
    out = {
        "name": name,
        "rows": len(df),
        "net_all": net,
        "n_long": long_n,
        "n_short": short_n,
        "net_long": long_net,
        "net_short": short_net,
        "approx_dd_long": long_dd,
        "approx_dd_short": short_dd,
        "win_rate": wins / len(df) if len(df) else 0,
        "avg": df["pnl"].mean(),
        "largest_win": df["pnl"].max(),
        "largest_loss": df["pnl"].min(),
    }
    return out


def exit_mix(df: pd.DataFrame) -> pd.DataFrame:
    return (
        df.groupby(["Market pos.", "Exit name"])["pnl"]
        .agg(["sum", "count", "mean"])
        .round(2)
    )


def main():
    v4 = load_trades(V4)
    v7 = load_trades(V7)

    r4 = report("v4", v4)
    r7 = report("v7", v7)

    lines = []
    lines.append("# v4 vs v7 trade export comparison\n")
    for r in (r4, r7):
        lines.append(f"## {r['name']}\n")
        lines.append(f"- **Trades:** {r['rows']} (long {r['n_long']}, short {r['n_short']})\n")
        lines.append(f"- **Net PnL (all):** ${r['net_all']:,.2f}\n")
        lines.append(f"- **Net long:** ${r['net_long']:,.2f} | **Net short:** ${r['net_short']:,.2f}\n")
        lines.append(
            f"- **Approx max DD (chronological, long-only series):** ${r['approx_dd_long']:,.2f}\n"
        )
        lines.append(
            f"- **Approx max DD (chronological, short-only series):** ${r['approx_dd_short']:,.2f}\n"
        )
        lines.append(
            f"- **Win rate:** {r['win_rate']*100:.2f}% | Avg trade: ${r['avg']:.2f}\n"
        )
        lines.append(
            f"- **Largest win / loss:** ${r['largest_win']:.2f} / ${r['largest_loss']:.2f}\n"
        )
        lines.append("\n### Exit mix (sum pnl, count)\n")
        lines.append(exit_mix(v4 if r["name"] == "v4" else v7).to_string())
        lines.append("\n")

    lines.append("## Delta (v7 − v4)\n")
    lines.append(f"- Δ net all: ${r7['net_all'] - r4['net_all']:,.2f}\n")
    lines.append(f"- Δ net long: ${r7['net_long'] - r4['net_long']:,.2f}\n")
    lines.append(f"- Δ net short: ${r7['net_short'] - r4['net_short']:,.2f}\n")
    lines.append(f"- Δ short count: {r7['n_short'] - r4['n_short']}\n")
    lines.append(f"- Δ long count: {r7['n_long'] - r4['n_long']}\n")

    # Telemetry sample (chunked)
    lines.append("\n## Telemetry (Glitch247Telemetry.csv, chunked aggregates)\n")
    if TELEMETRY.exists():
        chunksize = 400_000
        agg_n = {"HL": 0, "HS": 0, "LL": 0, "LS": 0}
        sum_adx = {k: 0.0 for k in agg_n}
        sum_atr = {k: 0.0 for k in agg_n}
        for chunk in pd.read_csv(TELEMETRY, chunksize=chunksize):
            d = chunk["final_direction"].to_numpy()
            hc = chunk["high_confidence"].to_numpy()
            pl = np.full(len(chunk), "NA", dtype=object)
            m = d != 0
            pl[m & (hc != 0) & (d > 0)] = "HL"
            pl[m & (hc != 0) & (d < 0)] = "HS"
            pl[m & (hc == 0) & (d > 0)] = "LL"
            pl[m & (hc == 0) & (d < 0)] = "LS"
            chunk = chunk.assign(_pl=pl)
            for p in ("HL", "HS", "LL", "LS"):
                s = chunk[chunk["_pl"] == p]
                if s.empty:
                    continue
                n = len(s)
                agg_n[p] += n
                sum_adx[p] += s["adx"].fillna(0).sum()
                sum_atr[p] += s["atr_ticks"].fillna(0).sum()
        lines.append("| play (bar label) | bars | mean ADX | mean ATR ticks |\n|---|---:|---:|---:|\n")
        for p in ("LL", "LS", "HL", "HS"):
            n = agg_n[p]
            if n == 0:
                continue
            lines.append(
                f"| {p} | {n} | {sum_adx[p]/n:.2f} | {sum_atr[p]/n:.2f} |\n"
            )
        lines.append(
            "\n*Exporter default ATR period may be 5; strategy uses 10 — compare like-for-like when tuning.*\n"
        )
    else:
        lines.append("_Telemetry file not found at expected path._\n")

    out = Path(__file__).resolve().parents[1] / "glitch-247-v4-v7-analysis-autogen.md"
    text = "".join(lines)
    out.write_text(text, encoding="utf-8")
    # Windows console may not support all Unicode; print ASCII-safe
    safe = text.encode("ascii", errors="replace").decode("ascii")
    print(safe)
    print(f"\nWrote {out}")


if __name__ == "__main__":
    main()
