#!/usr/bin/env python3
from __future__ import annotations

import argparse
import itertools
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Tuple

import numpy as np
import pandas as pd


@dataclass(frozen=True)
class SimParams:
    enable_low_conf: bool
    atr_period: int
    stop_hard_cap_ticks: int
    adx_ll: int
    adx_ls: int
    adx_hl: int
    adx_hs: int
    sl_ll: float
    tp1_ll: float
    sl_ls: float
    tp1_ls: float
    sl_hl: float
    tp1_hl: float
    tp2_hl: float
    sl_hs: float
    tp1_hs: float
    tp2_hs: float
    max_minutes_in_trade: int
    daily_profit_limit: float
    daily_loss_limit: float
    lock_trigger_1: float
    lock_giveback_1: float
    lock_trigger_2: float
    lock_giveback_2: float
    enable_recovery: bool
    recovery_trigger_losses: int
    recovery_wins_to_exit: int


def _play_type(direction: int, high_conf: bool) -> str:
    if direction == 0:
        return "NA"
    if high_conf:
        return "HL" if direction > 0 else "HS"
    return "LL" if direction > 0 else "LS"


def _safe_ticks(v: float) -> int:
    if not math.isfinite(v) or v <= 0:
        return 0
    return int(round(v))


def _compute_risk_ticks(play: str, atr_ticks: float, p: SimParams) -> Tuple[int, int, int]:
    atr = atr_ticks if math.isfinite(atr_ticks) and atr_ticks > 0 else 1.0
    if play == "LL":
        stop = _safe_ticks(p.sl_ll * atr)
        tp1 = _safe_ticks(p.tp1_ll * atr)
        tp2 = 0
    elif play == "LS":
        stop = _safe_ticks(p.sl_ls * atr)
        tp1 = _safe_ticks(p.tp1_ls * atr)
        tp2 = 0
    elif play == "HL":
        stop = _safe_ticks(p.sl_hl * atr)
        tp1 = _safe_ticks(p.tp1_hl * atr)
        tp2 = _safe_ticks(p.tp2_hl * atr)
    elif play == "HS":
        stop = _safe_ticks(p.sl_hs * atr)
        tp1 = _safe_ticks(p.tp1_hs * atr)
        tp2 = _safe_ticks(p.tp2_hs * atr)
    else:
        return 0, 0, 0
    stop = max(1, min(p.stop_hard_cap_ticks, stop))
    tp1 = max(1, tp1)
    tp2 = max(0, tp2)
    return stop, tp1, tp2


def _adx_min_for_play(play: str, p: SimParams) -> int:
    return {
        "LL": p.adx_ll,
        "LS": p.adx_ls,
        "HL": p.adx_hl,
        "HS": p.adx_hs,
    }.get(play, 10_000)


def _entry_features(row: pd.Series, p: SimParams) -> Tuple[int, bool, str]:
    primary_direction = int(row["primary_direction"])
    fallback_direction = int(row["fallback_direction"])
    primary_conf = int(row["primary_confidence"])

    direction = primary_direction if primary_direction != 0 else fallback_direction
    conf = primary_conf if primary_direction != 0 else 1
    high_conf = conf >= 2

    if (not p.enable_low_conf) and (not high_conf):
        direction = 0
        high_conf = False

    play = _play_type(direction, high_conf)
    if play == "NA":
        return 0, False, play

    adx = float(row["adx"])
    adx_ok = math.isfinite(adx) and adx >= _adx_min_for_play(play, p)
    return direction, adx_ok, play


def simulate(df: pd.DataFrame, p: SimParams, tick_value: float, ambiguity_mode: str) -> Dict[str, float]:
    df = df.sort_values("timestamp").reset_index(drop=True)
    timestamps = pd.to_datetime(df["timestamp"], utc=False)
    if len(df) < 5:
        return {"trades": 0.0, "net_pnl": 0.0, "max_dd": 0.0, "pf": 0.0, "win_rate": 0.0}

    ts64 = timestamps.astype("int64").to_numpy()
    bar_seconds = np.diff(ts64) / 1e9
    median_bar_seconds = float(np.nanmedian(bar_seconds)) if len(bar_seconds) else 60.0
    if not math.isfinite(median_bar_seconds) or median_bar_seconds <= 0:
        median_bar_seconds = 60.0
    max_bars_hold = max(1, int(round((p.max_minutes_in_trade * 60.0) / median_bar_seconds))) if p.max_minutes_in_trade > 0 else 10_000

    daily_realized: Dict[int, float] = {}
    daily_peak: Dict[int, float] = {}
    daily_idle: Dict[int, bool] = {}

    trades = []
    gross_win = 0.0
    gross_loss = 0.0
    equity = 0.0
    peak_equity = 0.0
    max_dd = 0.0

    recovery_active = False
    recovery_wins = 0
    consecutive_losses = 0

    pos = None
    i = 0
    while i < len(df) - 1:
        row = df.iloc[i]
        ts = timestamps.iloc[i]
        day_key = int(ts.strftime("%Y%m%d"))
        daily_realized.setdefault(day_key, 0.0)
        daily_peak.setdefault(day_key, 0.0)
        daily_idle.setdefault(day_key, False)

        if pos is None:
            if daily_idle[day_key]:
                i += 1
                continue

            direction, adx_ok, play = _entry_features(row, p)
            if not adx_ok:
                i += 1
                continue

            high_conf = play.startswith("H")
            if recovery_active and not high_conf:
                i += 1
                continue

            qty = 2 if high_conf else 1
            if recovery_active:
                qty = 1

            atr_ticks = float(row["atr_ticks"])
            tick_size = float(row["tick_size"])
            if not math.isfinite(tick_size) or tick_size <= 0:
                i += 1
                continue
            stop_ticks, tp1_ticks, tp2_ticks = _compute_risk_ticks(play, atr_ticks, p)

            entry_idx = i + 1
            entry_price = float(df.iloc[entry_idx]["open"])
            if not math.isfinite(entry_price):
                i += 1
                continue

            pos = {
                "direction": direction,
                "play": play,
                "qty_total": qty,
                "qty_open": qty,
                "entry_idx": entry_idx,
                "entry_price": entry_price,
                "stop_ticks": stop_ticks,
                "tp1_ticks": tp1_ticks,
                "tp2_ticks": tp2_ticks,
                "tick_size": tick_size,
                "tp1_done": False,
                "day_key": day_key,
            }
            i = entry_idx
            continue

        # Manage open position on bar i
        bar = df.iloc[i]
        high = float(bar["high"])
        low = float(bar["low"])
        close = float(bar["close"])

        direction = int(pos["direction"])
        tick_size = float(pos["tick_size"])
        entry_price = float(pos["entry_price"])
        stop_price = entry_price - pos["stop_ticks"] * tick_size if direction > 0 else entry_price + pos["stop_ticks"] * tick_size
        tp1_price = entry_price + pos["tp1_ticks"] * tick_size if direction > 0 else entry_price - pos["tp1_ticks"] * tick_size
        tp2_price = entry_price + pos["tp2_ticks"] * tick_size if direction > 0 else entry_price - pos["tp2_ticks"] * tick_size

        hit_stop = low <= stop_price if direction > 0 else high >= stop_price
        hit_tp1 = high >= tp1_price if direction > 0 else low <= tp1_price
        hit_tp2 = pos["tp2_ticks"] > 0 and (high >= tp2_price if direction > 0 else low <= tp2_price)

        realized = 0.0
        exit_reason = None

        def pnl_for(price: float, qty: int) -> float:
            ticks = (price - entry_price) / tick_size
            if direction < 0:
                ticks = -ticks
            return ticks * tick_value * qty

        bars_held = i - int(pos["entry_idx"])
        time_exit = bars_held >= max_bars_hold

        if hit_stop and (hit_tp1 or hit_tp2):
            stop_first = ambiguity_mode == "stop_first"
            if not stop_first:
                # "target_first" only when there is no conflicting TP2 priority.
                stop_first = False
            if stop_first:
                realized += pnl_for(stop_price, int(pos["qty_open"]))
                pos["qty_open"] = 0
                exit_reason = "AMBIG_STOP"
            else:
                if not pos["tp1_done"] and hit_tp1 and pos["qty_open"] > 1:
                    realized += pnl_for(tp1_price, 1)
                    pos["qty_open"] -= 1
                    pos["tp1_done"] = True
                if hit_tp2 and pos["qty_open"] > 0:
                    realized += pnl_for(tp2_price, int(pos["qty_open"]))
                    pos["qty_open"] = 0
                    exit_reason = "AMBIG_TP"
                elif pos["qty_open"] > 0:
                    realized += pnl_for(stop_price, int(pos["qty_open"]))
                    pos["qty_open"] = 0
                    exit_reason = "AMBIG_MIX"
        elif hit_stop:
            realized += pnl_for(stop_price, int(pos["qty_open"]))
            pos["qty_open"] = 0
            exit_reason = "STOP"
        else:
            if not pos["tp1_done"] and hit_tp1 and pos["qty_open"] > 1:
                realized += pnl_for(tp1_price, 1)
                pos["qty_open"] -= 1
                pos["tp1_done"] = True
            if hit_tp2 and pos["qty_open"] > 0:
                realized += pnl_for(tp2_price, int(pos["qty_open"]))
                pos["qty_open"] = 0
                exit_reason = "TP2"

        if pos["qty_open"] > 0 and time_exit:
            realized += pnl_for(close, int(pos["qty_open"]))
            pos["qty_open"] = 0
            exit_reason = "TIME"

        if pos["qty_open"] == 0:
            day_key = int(pos["day_key"])
            daily_realized[day_key] += realized
            daily_peak[day_key] = max(daily_peak[day_key], daily_realized[day_key])

            if p.daily_profit_limit > 0 and daily_realized[day_key] >= p.daily_profit_limit:
                daily_idle[day_key] = True
            if p.daily_loss_limit > 0 and daily_realized[day_key] <= -p.daily_loss_limit:
                daily_idle[day_key] = True
            if p.lock_trigger_1 > 0 and daily_peak[day_key] >= p.lock_trigger_1:
                if (daily_peak[day_key] - daily_realized[day_key]) >= p.lock_giveback_1:
                    daily_idle[day_key] = True
            if p.lock_trigger_2 > 0 and daily_peak[day_key] >= p.lock_trigger_2:
                if (daily_peak[day_key] - daily_realized[day_key]) >= p.lock_giveback_2:
                    daily_idle[day_key] = True

            if realized < 0:
                consecutive_losses += 1
                recovery_wins = 0
            elif realized > 0:
                if recovery_active:
                    recovery_wins += 1
                consecutive_losses = 0

            if p.enable_recovery and (not recovery_active) and consecutive_losses >= p.recovery_trigger_losses:
                recovery_active = True
                recovery_wins = 0
            if recovery_active and recovery_wins >= p.recovery_wins_to_exit:
                recovery_active = False
                recovery_wins = 0
                consecutive_losses = 0

            trades.append(realized)
            if realized >= 0:
                gross_win += realized
            else:
                gross_loss += abs(realized)
            equity += realized
            peak_equity = max(peak_equity, equity)
            max_dd = max(max_dd, peak_equity - equity)
            pos = None

        i += 1

    trade_count = len(trades)
    wins = sum(1 for t in trades if t > 0)
    pf = (gross_win / gross_loss) if gross_loss > 0 else (999.0 if gross_win > 0 else 0.0)
    return {
        "trades": float(trade_count),
        "net_pnl": float(sum(trades)),
        "max_dd": float(max_dd),
        "pf": float(pf),
        "win_rate": float((wins / trade_count) if trade_count else 0.0),
    }


def make_grid_long_focus(base: SimParams) -> List[SimParams]:
    """Vary HL only; keep HS/LL/LS identical to baseline (protect short-side profile)."""
    # Keep this grid small — `simulate()` is Python-bar-loop bound.
    adx_hl_vals = [35, 38, 40, 42]
    sl_hl_vals = [1.75, 2.0]
    tp1_hl_vals = [2.0, 2.2]
    tp2_hl_vals = [3.0, 3.5, 4.0]
    out: List[SimParams] = []
    for adx_hl, sl_hl, tp1_hl, tp2_hl in itertools.product(
        adx_hl_vals, sl_hl_vals, tp1_hl_vals, tp2_hl_vals
    ):
        out.append(
            SimParams(
                enable_low_conf=base.enable_low_conf,
                atr_period=base.atr_period,
                stop_hard_cap_ticks=base.stop_hard_cap_ticks,
                adx_ll=base.adx_ll,
                adx_ls=base.adx_ls,
                adx_hl=adx_hl,
                adx_hs=base.adx_hs,
                sl_ll=base.sl_ll,
                tp1_ll=base.tp1_ll,
                sl_ls=base.sl_ls,
                tp1_ls=base.tp1_ls,
                sl_hl=sl_hl,
                tp1_hl=tp1_hl,
                tp2_hl=tp2_hl,
                sl_hs=base.sl_hs,
                tp1_hs=base.tp1_hs,
                tp2_hs=base.tp2_hs,
                max_minutes_in_trade=base.max_minutes_in_trade,
                daily_profit_limit=base.daily_profit_limit,
                daily_loss_limit=base.daily_loss_limit,
                lock_trigger_1=base.lock_trigger_1,
                lock_giveback_1=base.lock_giveback_1,
                lock_trigger_2=base.lock_trigger_2,
                lock_giveback_2=base.lock_giveback_2,
                enable_recovery=base.enable_recovery,
                recovery_trigger_losses=base.recovery_trigger_losses,
                recovery_wins_to_exit=base.recovery_wins_to_exit,
            )
        )
    return out


def make_grid(base: SimParams, mode: str) -> List[SimParams]:
    if mode == "full":
        adx_vals = [28, 30, 32, 35, 38, 40]
        sl_vals = [1.5, 1.75, 2.0, 2.25]
        tp_vals = [1.8, 2.0, 2.2, 2.5, 3.0]
    else:
        adx_vals = [30, 35, 40]
        sl_vals = [1.75, 2.0]
        tp_vals = [2.0, 2.2, 2.5]

    params: List[SimParams] = []
    for adx_hl, adx_hs, sl_hl, tp1_hl, tp2_hl, sl_hs, tp1_hs, tp2_hs in itertools.product(
        adx_vals, adx_vals, sl_vals, tp_vals, [2.5, 3.0, 3.5], sl_vals, tp_vals, [2.5, 3.0, 3.5]
    ):
        params.append(
            SimParams(
                enable_low_conf=base.enable_low_conf,
                atr_period=base.atr_period,
                stop_hard_cap_ticks=base.stop_hard_cap_ticks,
                adx_ll=base.adx_ll,
                adx_ls=base.adx_ls,
                adx_hl=adx_hl,
                adx_hs=adx_hs,
                sl_ll=base.sl_ll,
                tp1_ll=base.tp1_ll,
                sl_ls=base.sl_ls,
                tp1_ls=base.tp1_ls,
                sl_hl=sl_hl,
                tp1_hl=tp1_hl,
                tp2_hl=tp2_hl,
                sl_hs=sl_hs,
                tp1_hs=tp1_hs,
                tp2_hs=tp2_hs,
                max_minutes_in_trade=base.max_minutes_in_trade,
                daily_profit_limit=base.daily_profit_limit,
                daily_loss_limit=base.daily_loss_limit,
                lock_trigger_1=base.lock_trigger_1,
                lock_giveback_1=base.lock_giveback_1,
                lock_trigger_2=base.lock_trigger_2,
                lock_giveback_2=base.lock_giveback_2,
                enable_recovery=base.enable_recovery,
                recovery_trigger_losses=base.recovery_trigger_losses,
                recovery_wins_to_exit=base.recovery_wins_to_exit,
            )
        )
    return params


def main() -> None:
    ap = argparse.ArgumentParser(description="Glitch247 offline surrogate optimizer (telemetry + optional event context)")
    ap.add_argument("--telemetry", required=True, help="Path to Glitch247Telemetry*.csv")
    ap.add_argument("--events", default="", help="Optional Glitch247TradeEvents*.csv for reference checks")
    ap.add_argument("--out", default="ninjatrader/Glitch/Docs/glitch-247-surrogate-optimization.md")
    ap.add_argument(
        "--grid",
        choices=["quick", "full", "long_focus"],
        default="quick",
        help="long_focus = sweep HL params only (HS/LL/LS fixed to baseline)",
    )
    ap.add_argument("--max-rows", type=int, default=0, help="0 = use all rows (can be slow on multi-million bar files)")
    ap.add_argument("--top", type=int, default=25)
    ap.add_argument("--tick-value", type=float, default=5.0, help="Dollar value per tick (MNQ=0.5, NQ=5)")
    ap.add_argument("--ambiguity", choices=["stop_first", "target_first"], default="stop_first")
    args = ap.parse_args()

    telem = pd.read_csv(args.telemetry)
    telem["timestamp"] = pd.to_datetime(telem["timestamp"], errors="coerce")
    telem = telem.dropna(subset=["timestamp"]).sort_values("timestamp").reset_index(drop=True)
    if args.max_rows and args.max_rows > 0:
        telem = telem.iloc[: args.max_rows].copy()

    base = SimParams(
        enable_low_conf=True,
        atr_period=10,
        stop_hard_cap_ticks=115,
        adx_ll=30,
        adx_ls=30,
        adx_hl=35,
        adx_hs=35,
        sl_ll=1.75,
        tp1_ll=2.2,
        sl_ls=1.75,
        tp1_ls=2.2,
        sl_hl=2.0,
        tp1_hl=2.0,
        tp2_hl=3.0,
        sl_hs=2.0,
        tp1_hs=2.0,
        tp2_hs=3.0,
        max_minutes_in_trade=60,
        daily_profit_limit=600.0,
        daily_loss_limit=360.0,
        lock_trigger_1=150.0,
        lock_giveback_1=100.0,
        lock_trigger_2=250.0,
        lock_giveback_2=100.0,
        enable_recovery=True,
        recovery_trigger_losses=2,
        recovery_wins_to_exit=2,
    )

    if args.grid == "long_focus":
        candidates = [base] + make_grid_long_focus(base)
    else:
        candidates = [base] + make_grid(base, args.grid)
    rows = []
    for idx, p in enumerate(candidates):
        stats = simulate(telem, p, args.tick_value, args.ambiguity)
        score = stats["net_pnl"] - 0.8 * stats["max_dd"]
        rows.append(
            {
                "rank_key": score,
                "idx": idx,
                **stats,
                "adx_hl": p.adx_hl,
                "adx_hs": p.adx_hs,
                "sl_hl": p.sl_hl,
                "tp1_hl": p.tp1_hl,
                "tp2_hl": p.tp2_hl,
                "sl_hs": p.sl_hs,
                "tp1_hs": p.tp1_hs,
                "tp2_hs": p.tp2_hs,
            }
        )

    res = pd.DataFrame(rows).sort_values("rank_key", ascending=False).reset_index(drop=True)
    top = res.head(max(1, args.top))

    lines = []
    lines.append("# Glitch247 Surrogate Optimization")
    lines.append("")
    lines.append(f"- Telemetry: `{args.telemetry}`")
    lines.append(f"- Rows used: `{len(telem)}` (max_rows={args.max_rows})")
    lines.append(f"- Grid mode: `{args.grid}` ({len(candidates)} candidate sets)")
    lines.append(f"- Ambiguity: `{args.ambiguity}`")
    lines.append(f"- Tick value: `{args.tick_value}`")
    lines.append("")
    lines.append("## Top Candidates")
    lines.append("")
    lines.append(top.to_string(index=False))
    lines.append("")
    lines.append("## Baseline Result")
    lines.append("")
    lines.append(res[res["idx"] == 0].to_string(index=False))

    if args.events:
        evt = pd.read_csv(args.events)
        lines.append("")
        lines.append("## Event File Snapshot")
        lines.append("")
        lines.append(f"- Events rows: {len(evt)}")
        if "event_type" in evt.columns:
            lines.append(evt["event_type"].value_counts().to_string())

    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"Wrote {out_path}")


if __name__ == "__main__":
    main()
