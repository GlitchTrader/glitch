from __future__ import annotations

import re
from pathlib import Path

import numpy as np
import pandas as pd

TRADES = Path(r"C:\Users\alan\OneDrive\Desktop\Glitch 247 Trades Adaptive v12.csv")
EVENTS = Path(r"C:\Users\alan\Documents\NinjaTrader 8\GlitchData\Telemetry\Glitch247TradeEvents12.csv")
TELEM10 = Path(r"C:\Users\alan\Documents\NinjaTrader 8\GlitchData\Telemetry\Glitch247Telemetry10.csv")
OUT = Path(r"d:\click-blue\trading\glitch-platform\ninjatrader\Glitch\Docs\glitch-247-v13-scenario-recommendations.md")

MNQ_DOLLARS_PER_TICK = 0.5


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


def load_joined_trades_events() -> pd.DataFrame:
    tr = pd.read_csv(TRADES, sep=";", encoding="utf-8")
    tr["pnl"] = tr["Profit"].map(money)
    tr["mae_d"] = tr["MAE"].map(money).abs()
    tr["mfe_d"] = tr["MFE"].map(money).abs()
    tr["qty"] = pd.to_numeric(tr["Qty"], errors="coerce").fillna(1)
    tr["entry_time"] = pd.to_datetime(tr["Entry time"], dayfirst=True, errors="coerce")
    tr["play"] = parse_play(tr["Entry name"])

    ev = pd.read_csv(EVENTS, low_memory=False)
    ev = ev[ev["event_type"] == "ENTRY"].copy()
    ev["entry_time_raw"] = pd.to_datetime(ev["timestamp"], errors="coerce").dt.tz_localize(None)
    ev = ev.rename(columns={"order_name": "Entry name", "play_type": "play_ev"})

    # Find best offset (v12 historically +2h)
    candidate_offsets = [pd.Timedelta(hours=h) for h in [-4, -3, -2, -1, 0, 1, 2, 3, 4]]
    best_off = pd.Timedelta(0)
    best_cov = -1.0
    sample = tr[["Entry name", "entry_time"]].dropna().copy()
    sample = sample.sample(min(len(sample), 700), random_state=7) if len(sample) > 700 else sample
    for off in candidate_offsets:
        ev["entry_time"] = ev["entry_time_raw"] + off
        hits, total = 0, 0
        for name, grp in sample.groupby("Entry name", sort=False):
            e = ev[ev["Entry name"] == name][["entry_time"]].rename(columns={"entry_time": "event_time"}).sort_values("event_time")
            g = grp.sort_values("entry_time")
            if e.empty:
                continue
            j = pd.merge_asof(
                g,
                e,
                left_on="entry_time",
                right_on="event_time",
                direction="nearest",
                tolerance=pd.Timedelta("3min"),
            )
            hits += int(j["event_time"].notna().sum())
            total += len(j)
        cov = (hits / total) if total else 0.0
        if cov > best_cov:
            best_cov = cov
            best_off = off

    ev["entry_time"] = ev["entry_time_raw"] + best_off
    ev = ev.sort_values("entry_time")
    keep = ["entry_time", "Entry name", "adx_now", "atr_ticks_entry", "selected_stop_ticks", "selected_tp1_ticks", "selected_tp2_ticks"]
    ev = ev[keep]

    tr = tr.sort_values("entry_time")
    out = []
    for name, grp in tr.groupby("Entry name", sort=False):
        e = ev[ev["Entry name"] == name].sort_values("entry_time")
        g = grp.sort_values("entry_time")
        if e.empty:
            for c in ["adx_now", "atr_ticks_entry", "selected_stop_ticks", "selected_tp1_ticks", "selected_tp2_ticks"]:
                g[c] = np.nan
            out.append(g)
            continue
        j = pd.merge_asof(
            g,
            e.drop(columns=["Entry name"]),
            on="entry_time",
            direction="nearest",
            tolerance=pd.Timedelta("3min"),
        )
        out.append(j)
    j = pd.concat(out, ignore_index=True)

    # Convert MAE/MFE dollars to ticks then ATR units
    j["mae_ticks"] = j["mae_d"] / (MNQ_DOLLARS_PER_TICK * j["qty"])
    j["mfe_ticks"] = j["mfe_d"] / (MNQ_DOLLARS_PER_TICK * j["qty"])
    j["mae_atr"] = j["mae_ticks"] / j["atr_ticks_entry"]
    j["mfe_atr"] = j["mfe_ticks"] / j["atr_ticks_entry"]
    j["atr_join_ok"] = j["atr_ticks_entry"].notna() & (j["atr_ticks_entry"] > 0)
    j.attrs["offset"] = best_off
    j.attrs["coverage"] = float(j["atr_join_ok"].mean())
    return j


def telemetry10_atr_distribution() -> pd.DataFrame:
    # Chunked read because file is large
    sums = {p: {"n": 0, "atr_sum": 0.0} for p in ["LL", "LS", "HL", "HS"]}
    qtiles = {p: [] for p in ["LL", "LS", "HL", "HS"]}

    for chunk in pd.read_csv(TELEM10, chunksize=300_000):
        d = chunk["final_direction"].to_numpy()
        hc = chunk["high_confidence"].to_numpy()
        pl = np.full(len(chunk), "NA", dtype=object)
        m = d != 0
        pl[m & (hc != 0) & (d > 0)] = "HL"
        pl[m & (hc != 0) & (d < 0)] = "HS"
        pl[m & (hc == 0) & (d > 0)] = "LL"
        pl[m & (hc == 0) & (d < 0)] = "LS"
        chunk["_pl"] = pl
        for p in ["LL", "LS", "HL", "HS"]:
            s = chunk.loc[chunk["_pl"] == p, "atr_ticks"].dropna()
            if s.empty:
                continue
            sums[p]["n"] += len(s)
            sums[p]["atr_sum"] += float(s.sum())
            # reservoir-ish by small random sample from each chunk
            take = s.sample(min(3000, len(s)), random_state=11).tolist()
            qtiles[p].extend(take)

    rows = []
    for p in ["LL", "LS", "HL", "HS"]:
        arr = np.array(qtiles[p], dtype=float)
        if len(arr) == 0:
            rows.append((p, 0, np.nan, np.nan, np.nan, np.nan))
            continue
        rows.append(
            (
                p,
                sums[p]["n"],
                sums[p]["atr_sum"] / max(1, sums[p]["n"]),
                np.quantile(arr, 0.25),
                np.quantile(arr, 0.5),
                np.quantile(arr, 0.75),
            )
        )
    return pd.DataFrame(rows, columns=["play", "bars", "atr_mean", "atr_q25", "atr_q50", "atr_q75"]).set_index("play")


def scenario_grid(play_df: pd.DataFrame) -> pd.DataFrame:
    # play_df has mae_atr, mfe_atr
    stops = [0.8, 1.0, 1.25, 1.5, 1.75, 2.0]
    ratios = [1.0, 1.25, 1.5, 1.75, 2.0, 2.5]
    rows = []
    for s in stops:
        for r in ratios:
            tp = s * r
            p_tp = float((play_df["mfe_atr"] >= tp).mean())
            p_sl = float((play_df["mae_atr"] >= s).mean())
            # proxy expectancy in R units (not execution-accurate because path ordering ignored)
            exp_r = p_tp * r - p_sl * 1.0
            rows.append((s, r, tp, p_tp, p_sl, exp_r))
    return pd.DataFrame(rows, columns=["stop_atr", "ratio_tp_over_sl", "tp_atr", "p_tp", "p_sl", "exp_r_proxy"])


def recommend_per_play(j: pd.DataFrame, telem_dist: pd.DataFrame) -> tuple[pd.DataFrame, dict]:
    rec_rows = []
    details = {}
    for p in ["LL", "LS", "HL", "HS"]:
        s = j[(j["play"] == p) & j["atr_join_ok"] & j["mae_atr"].notna() & j["mfe_atr"].notna()].copy()
        if len(s) < 60:
            continue
        g = scenario_grid(s)
        # keep reasonable sample-hit region: p_tp >= 0.25 to avoid over-ambitious tps
        g2 = g[g["p_tp"] >= 0.25].copy()
        if g2.empty:
            g2 = g
        best = g2.sort_values(["exp_r_proxy", "p_tp"], ascending=[False, False]).iloc[0]

        # convert stop_atr to expected stop ticks using telemetry median atr ticks by play
        atr_med = float(telem_dist.loc[p, "atr_q50"]) if p in telem_dist.index else np.nan
        stop_ticks_med = best["stop_atr"] * atr_med if pd.notna(atr_med) else np.nan
        tp_ticks_med = best["tp_atr"] * atr_med if pd.notna(atr_med) else np.nan
        rec_rows.append(
            (
                p,
                len(s),
                float(best["stop_atr"]),
                float(best["ratio_tp_over_sl"]),
                float(best["tp_atr"]),
                float(best["p_tp"]),
                float(best["p_sl"]),
                float(best["exp_r_proxy"]),
                atr_med,
                stop_ticks_med,
                tp_ticks_med,
            )
        )
        details[p] = g.sort_values("exp_r_proxy", ascending=False).head(8)

    rec = pd.DataFrame(
        rec_rows,
        columns=[
            "play",
            "n_joined",
            "rec_stop_atr",
            "rec_tp_sl_ratio",
            "rec_tp_atr",
            "p_tp_at_rec",
            "p_sl_at_rec",
            "exp_r_proxy",
            "telemetry_atr_ticks_q50",
            "implied_stop_ticks_q50",
            "implied_tp_ticks_q50",
        ],
    ).set_index("play")
    return rec, details


def adx_sweeps(j: pd.DataFrame) -> pd.DataFrame:
    out = []
    for p in ["LL", "LS", "HL", "HS"]:
        s = j[(j["play"] == p) & j["adx_now"].notna()].copy()
        if len(s) < 80:
            continue
        rows = []
        for k in range(18, 56, 2):
            t = s[s["adx_now"] >= k]
            if len(t) < 40:
                continue
            net = t["pnl"].sum()
            wr = (t["pnl"] > 0).mean()
            pf = t.loc[t["pnl"] > 0, "pnl"].sum() / abs(t.loc[t["pnl"] < 0, "pnl"].sum()) if (t["pnl"] < 0).any() else np.nan
            rows.append((k, len(t), net, wr, pf))
        if not rows:
            continue
        df = pd.DataFrame(rows, columns=["k", "n", "net", "wr", "pf"])
        best = df.loc[df["net"].idxmax()]
        balanced = df[df["n"] >= 0.6 * len(s)]
        b = balanced.loc[balanced["net"].idxmax()] if len(balanced) else best
        out.append((p, int(best["k"]), int(best["n"]), float(best["net"]), float(best["wr"]), float(best["pf"]), int(b["k"]), int(b["n"]), float(b["net"])))
    return pd.DataFrame(out, columns=["play", "best_k", "best_n", "best_net", "best_wr", "best_pf", "balanced_k", "balanced_n", "balanced_net"]).set_index("play")


def main() -> None:
    j = load_joined_trades_events()
    telem = telemetry10_atr_distribution()
    rec, details = recommend_per_play(j, telem)
    adx = adx_sweeps(j)

    by_play = (
        j.groupby("play")
        .agg(
            trades=("pnl", "size"),
            net=("pnl", "sum"),
            avg=("pnl", "mean"),
            winrate=("pnl", lambda s: (s > 0).mean()),
            pf=("pnl", lambda s: s[s > 0].sum() / abs(s[s < 0].sum()) if (s < 0).any() else np.nan),
            mae=("mae_d", "mean"),
            mfe=("mfe_d", "mean"),
        )
        .sort_index()
    )
    by_exit = (
        j.groupby(["play", "Exit name"])
        .agg(trades=("pnl", "size"), net=("pnl", "sum"), avg=("pnl", "mean"))
        .sort_values(["play", "net"], ascending=[True, False])
    )

    lines = []
    lines.append("# v13 Recommendations from v12 + Telemetry10\n\n")
    lines.append(f"- Trades: `{TRADES}`\n")
    lines.append(f"- Events: `{EVENTS}`\n")
    lines.append(f"- Telemetry: `{TELEM10}`\n")
    lines.append(f"- Join offset used: `{j.attrs['offset']}`\n")
    lines.append(f"- Joined ATR/ADX coverage: `{j.attrs['coverage']:.1%}`\n\n")
    lines.append("## v12 baseline by play\n\n")
    lines.append(by_play.round(3).to_string())
    lines.append("\n\n## v12 by play x exit\n\n")
    lines.append(by_exit.round(2).to_string())
    lines.append("\n\n## Telemetry10 ATR distribution by play (bar-level)\n\n")
    lines.append(telem.round(3).to_string())
    lines.append("\n\n## ADX filter sweeps (from joined v12 entries)\n\n")
    lines.append(adx.round(3).to_string())
    lines.append("\n\n## Stop/Target scenario recommendations (ATR units)\n\n")
    if len(rec):
        lines.append(rec.round(3).to_string())
    else:
        lines.append("No recommendation rows (insufficient joined sample).")

    lines.append("\n\n## Notes on 1.25R ambiguity (1/1.25 vs 2/2.5)\n\n")
    lines.append(
        "- Same R ratio does **not** imply same behavior. In absolute ATR space, larger stop/target pairs change hit probabilities because price noise and session/time exits are finite.\n"
    )
    lines.append(
        "- This report therefore recommends both **ratio** and **absolute ATR stop** per play.\n"
    )

    lines.append("\n## Top scenario candidates per play (proxy ranking)\n\n")
    for p in ["LL", "LS", "HL", "HS"]:
        if p not in details:
            continue
        lines.append(f"### {p}\n")
        lines.append(details[p].round(3).to_string(index=False))
        lines.append("\n\n")

    OUT.write_text("".join(lines), encoding="utf-8")
    txt = "".join(lines).encode("ascii", errors="replace").decode("ascii")
    print(txt)
    print(f"\nWrote {OUT}")


if __name__ == "__main__":
    main()

