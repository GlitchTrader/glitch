#!/usr/bin/env python3
"""
Streaming analyzer for Glitch telemetry CSV exports.

Usage:
  python scripts/analyze_glitch_telemetry.py --file "C:\\path\\to\\run.csv"
  python scripts/analyze_glitch_telemetry.py --file "C:\\path\\to\\runA.csv" --compare "C:\\path\\to\\runB.csv"
"""

from __future__ import annotations

import argparse
import csv
import math
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Optional


@dataclass
class NumStats:
    count: int = 0
    total: float = 0.0
    min_value: float = math.inf
    max_value: float = -math.inf

    def add(self, value: Optional[float]) -> None:
        if value is None or math.isnan(value) or math.isinf(value):
            return
        self.count += 1
        self.total += value
        if value < self.min_value:
            self.min_value = value
        if value > self.max_value:
            self.max_value = value

    @property
    def avg(self) -> Optional[float]:
        if self.count <= 0:
            return None
        return self.total / self.count

    def summary(self) -> str:
        if self.count <= 0:
            return "count=0"
        return (
            f"count={self.count:,} "
            f"min={self.min_value:.6f} "
            f"max={self.max_value:.6f} "
            f"avg={self.avg:.6f}"
        )


def parse_float(value: str) -> Optional[float]:
    if value is None:
        return None
    text = value.strip()
    if not text:
        return None
    try:
        return float(text)
    except ValueError:
        return None


def parse_int(value: str) -> Optional[int]:
    if value is None:
        return None
    text = value.strip()
    if not text:
        return None
    try:
        return int(text)
    except ValueError:
        return None


def to_label(value: str) -> str:
    if value is None:
        return "(blank)"
    text = value.strip()
    return text if text else "(blank)"


def is_bar_row(row: Dict[str, str]) -> bool:
    row_type = (row.get("row_type") or "").strip().lower()
    return row_type in ("", "bar")


def analyze(path: Path) -> Dict[str, object]:
    if not path.exists():
        raise FileNotFoundError(f"Telemetry file not found: {path}")

    required_columns = [
        "utc_time",
        "base_score",
        "final_score",
        "adx",
        "order_flow_score",
        "order_flow_confidence",
        "action",
        "reason",
    ]

    rows_total = 0
    rows_bar = 0
    rows_execution = 0
    first_utc = ""
    last_utc = ""
    duplicate_utc = 0
    non_monotonic_utc = 0

    final_stats = NumStats()
    base_stats = NumStats()
    adx_stats = NumStats()
    of_stats = NumStats()
    of_conf_stats = NumStats()

    non_empty_of = 0
    non_empty_of_conf = 0

    action_counts: Counter[str] = Counter()
    reason_counts: Counter[str] = Counter()
    gate_reason_counts: Counter[str] = Counter()
    blocked_long_counts: Counter[str] = Counter()
    blocked_short_counts: Counter[str] = Counter()

    equal_base_final_count = 0
    comparable_base_final_count = 0

    prev_final: Optional[float] = None
    prev_utc = ""

    cross_up = {0.10: 0, 0.35: 0, 0.75: 0}
    cross_down = {-0.10: 0, -0.35: 0, -0.75: 0}

    in_buy_band = 0
    in_sell_band = 0
    in_neutral_band = 0

    entries = 0
    exits = 0
    reversals = 0
    daily_lock_rows = 0
    max_entries_seen = 0
    entries_per_day: Counter[str] = Counter()

    with path.open("r", newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        fieldnames = reader.fieldnames or []
        missing = [c for c in required_columns if c not in fieldnames]
        if missing:
            raise ValueError(f"Missing required columns: {missing}")

        has_row_type = "row_type" in fieldnames
        has_gate_reason = "gate_reason_detail" in fieldnames
        has_entry_setup = "entry_setup_long" in fieldnames and "entry_setup_short" in fieldnames
        has_entries_today = "entries_today" in fieldnames
        has_daily_lock = "daily_lock_active" in fieldnames

        for row in reader:
            rows_total += 1
            row_type = (row.get("row_type") or "").strip().lower()
            if has_row_type:
                if row_type == "execution":
                    rows_execution += 1
                elif row_type in ("", "bar"):
                    rows_bar += 1
            else:
                rows_bar += 1

            utc = (row.get("utc_time") or "").strip()
            if rows_total == 1:
                first_utc = utc
            last_utc = utc

            if prev_utc:
                if utc == prev_utc:
                    duplicate_utc += 1
                elif utc < prev_utc:
                    non_monotonic_utc += 1
            prev_utc = utc

            if not is_bar_row(row):
                continue

            base = parse_float(row.get("base_score", ""))
            final = parse_float(row.get("final_score", ""))
            adx = parse_float(row.get("adx", ""))
            of = parse_float(row.get("order_flow_score", ""))
            of_conf = parse_float(row.get("order_flow_confidence", ""))

            base_stats.add(base)
            final_stats.add(final)
            adx_stats.add(adx)
            of_stats.add(of)
            of_conf_stats.add(of_conf)

            if (row.get("order_flow_score") or "").strip():
                non_empty_of += 1
            if (row.get("order_flow_confidence") or "").strip():
                non_empty_of_conf += 1

            action = to_label(row.get("action", ""))
            reason = to_label(row.get("reason", ""))
            action_counts[action] += 1
            reason_counts[reason] += 1

            if action.startswith("enter_"):
                entries += 1
                local_time = (row.get("local_time") or "").strip()
                day = local_time[:10] if local_time else "(unknown)"
                entries_per_day[day] += 1
            elif action.startswith("exit_"):
                exits += 1
            elif action.startswith("reverse_"):
                reversals += 1

            if has_gate_reason:
                gate_reason = to_label(row.get("gate_reason_detail", ""))
                if gate_reason != "(blank)":
                    gate_reason_counts[gate_reason] += 1

            if has_entry_setup:
                long_setup = (row.get("entry_setup_long") or "").strip() == "1"
                short_setup = (row.get("entry_setup_short") or "").strip() == "1"
                if long_setup and has_gate_reason:
                    gate_reason = to_label(row.get("gate_reason_detail", ""))
                    if gate_reason.startswith("blocked_"):
                        blocked_long_counts[gate_reason] += 1
                if short_setup and has_gate_reason:
                    gate_reason = to_label(row.get("gate_reason_detail", ""))
                    if gate_reason.startswith("blocked_"):
                        blocked_short_counts[gate_reason] += 1

            if has_entries_today:
                entries_today = parse_int(row.get("entries_today", ""))
                if entries_today is not None and entries_today > max_entries_seen:
                    max_entries_seen = entries_today

            if has_daily_lock and (row.get("daily_lock_active") or "").strip() == "1":
                daily_lock_rows += 1

            if base is not None and final is not None:
                comparable_base_final_count += 1
                if abs(base - final) <= 1e-12:
                    equal_base_final_count += 1

            if final is not None:
                if final >= 0.10:
                    in_buy_band += 1
                elif final <= -0.10:
                    in_sell_band += 1
                else:
                    in_neutral_band += 1

            if prev_final is not None and final is not None:
                for threshold in (0.10, 0.35, 0.75):
                    if prev_final < threshold <= final:
                        cross_up[threshold] += 1
                for threshold in (-0.10, -0.35, -0.75):
                    if prev_final > threshold >= final:
                        cross_down[threshold] += 1

            prev_final = final

    avg_entries_day = 0.0
    if entries_per_day:
        avg_entries_day = sum(entries_per_day.values()) / len(entries_per_day)

    return {
        "path": path,
        "rows_total": rows_total,
        "rows_bar": rows_bar,
        "rows_execution": rows_execution,
        "first_utc": first_utc,
        "last_utc": last_utc,
        "duplicate_utc": duplicate_utc,
        "non_monotonic_utc": non_monotonic_utc,
        "final_stats": final_stats,
        "base_stats": base_stats,
        "adx_stats": adx_stats,
        "of_stats": of_stats,
        "of_conf_stats": of_conf_stats,
        "non_empty_of": non_empty_of,
        "non_empty_of_conf": non_empty_of_conf,
        "comparable_base_final_count": comparable_base_final_count,
        "equal_base_final_count": equal_base_final_count,
        "in_buy_band": in_buy_band,
        "in_neutral_band": in_neutral_band,
        "in_sell_band": in_sell_band,
        "cross_up": cross_up,
        "cross_down": cross_down,
        "action_counts": action_counts,
        "reason_counts": reason_counts,
        "gate_reason_counts": gate_reason_counts,
        "blocked_long_counts": blocked_long_counts,
        "blocked_short_counts": blocked_short_counts,
        "entries": entries,
        "exits": exits,
        "reversals": reversals,
        "entries_per_day": entries_per_day,
        "avg_entries_day": avg_entries_day,
        "max_entries_seen": max_entries_seen,
        "daily_lock_rows": daily_lock_rows,
    }


def print_summary(summary: Dict[str, object], title: str) -> None:
    path = summary["path"]
    rows_total = summary["rows_total"]
    rows_bar = summary["rows_bar"]
    rows_execution = summary["rows_execution"]
    comparable = summary["comparable_base_final_count"]
    equal = summary["equal_base_final_count"]

    print(f"\n=== {title} ===")
    print(f"File: {path}")
    print(f"Rows: total={rows_total:,} bar={rows_bar:,} execution={rows_execution:,}")
    print(f"UTC range: {summary['first_utc']} -> {summary['last_utc']}")
    print(f"Duplicate UTC rows: {summary['duplicate_utc']:,}")
    print(f"Non-monotonic UTC rows: {summary['non_monotonic_utc']:,}")
    print("")
    print("Numeric coverage (bar rows):")
    print(f"  final_score: {summary['final_stats'].summary()}")
    print(f"  base_score:  {summary['base_stats'].summary()}")
    print(f"  adx:         {summary['adx_stats'].summary()}")
    print(f"  of_score:    {summary['of_stats'].summary()}")
    print(f"  of_conf:     {summary['of_conf_stats'].summary()}")
    print("")
    print("Completeness:")
    print(f"  order_flow_score non-empty: {summary['non_empty_of']:,}/{rows_bar:,}")
    print(f"  order_flow_confidence non-empty: {summary['non_empty_of_conf']:,}/{rows_bar:,}")
    if comparable > 0:
        pct = (equal / comparable) * 100.0
        print(f"  base_score == final_score: {equal:,}/{comparable:,} ({pct:.2f}%)")
    else:
        print("  base_score == final_score: n/a")
    print("")
    print("Score regime occupancy (final_score, bar rows):")
    print(f"  buy band   (>= +0.10): {summary['in_buy_band']:,}")
    print(f"  neutral    (-0.10..+0.10): {summary['in_neutral_band']:,}")
    print(f"  sell band  (<= -0.10): {summary['in_sell_band']:,}")
    print("")
    print("Threshold cross counts (final_score, bar rows):")
    print("  up crosses:")
    for threshold in (0.10, 0.35, 0.75):
        print(f"    {threshold:+.2f}: {summary['cross_up'][threshold]:,}")
    print("  down crosses:")
    for threshold in (-0.10, -0.35, -0.75):
        print(f"    {threshold:+.2f}: {summary['cross_down'][threshold]:,}")
    print("")
    print("Trade actions:")
    print(f"  entries={summary['entries']:,} exits={summary['exits']:,} reversals={summary['reversals']:,}")
    print(f"  avg entries/day={summary['avg_entries_day']:.2f}")
    print(f"  max entries_today observed={summary['max_entries_seen']:,}")
    print(f"  daily lock bar-rows={summary['daily_lock_rows']:,}")
    print("")
    print("Action counts (top 12):")
    for name, count in summary["action_counts"].most_common(12):
        print(f"  {name}: {count:,}")
    print("")
    print("Reason counts (top 12):")
    for name, count in summary["reason_counts"].most_common(12):
        print(f"  {name}: {count:,}")
    print("")
    if summary["gate_reason_counts"]:
        print("Gate reason counts (top 12):")
        for name, count in summary["gate_reason_counts"].most_common(12):
            print(f"  {name}: {count:,}")
        print("")
    if summary["blocked_long_counts"]:
        print("Blocked long reasons (top 8):")
        for name, count in summary["blocked_long_counts"].most_common(8):
            print(f"  {name}: {count:,}")
        print("")
    if summary["blocked_short_counts"]:
        print("Blocked short reasons (top 8):")
        for name, count in summary["blocked_short_counts"].most_common(8):
            print(f"  {name}: {count:,}")
        print("")


def print_delta(base: Dict[str, object], compare: Dict[str, object]) -> None:
    print("\n=== Delta (compare - base) ===")

    def d(name: str) -> None:
        v0 = base[name]
        v1 = compare[name]
        if isinstance(v0, (int, float)) and isinstance(v1, (int, float)):
            print(f"  {name}: {v1 - v0:+,.6f}" if isinstance(v0, float) else f"  {name}: {v1 - v0:+,}")

    d("rows_total")
    d("rows_bar")
    d("rows_execution")
    d("entries")
    d("exits")
    d("reversals")
    d("avg_entries_day")
    d("max_entries_seen")
    d("daily_lock_rows")

    base_final_avg = base["final_stats"].avg
    compare_final_avg = compare["final_stats"].avg
    if base_final_avg is not None and compare_final_avg is not None:
        print(f"  final_score.avg: {compare_final_avg - base_final_avg:+.6f}")
    base_adx_avg = base["adx_stats"].avg
    compare_adx_avg = compare["adx_stats"].avg
    if base_adx_avg is not None and compare_adx_avg is not None:
        print(f"  adx.avg: {compare_adx_avg - base_adx_avg:+.6f}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Analyze Glitch telemetry CSV export.")
    parser.add_argument("--file", required=True, help="Path to telemetry CSV file.")
    parser.add_argument("--compare", help="Optional second telemetry CSV path to compare against --file.")
    args = parser.parse_args()

    base_summary = analyze(Path(args.file))
    print_summary(base_summary, "BASE")

    if args.compare:
        compare_summary = analyze(Path(args.compare))
        print_summary(compare_summary, "COMPARE")
        print_delta(base_summary, compare_summary)


if __name__ == "__main__":
    main()
