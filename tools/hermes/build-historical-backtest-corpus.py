#!/usr/bin/env python3
"""Validate and join Glitch MES/MNQ/M2K historical feature exports."""

from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import re
import subprocess
import sys
from collections import OrderedDict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable
import xml.etree.ElementTree as ET


CORPUS_SCHEMA = "glitch.market.corpus.v1"
DATASET_SCHEMA = "glitch.market.backtest-dataset.v1"
MARKET_SCHEMA = "glitch.market.snapshot.v2"
REQUIRED_INSTRUMENTS = ("MES", "MNQ", "M2K")
REQUIRED_TIMEFRAMES = (1, 5, 15, 60)
SOURCE_FILES = (
    "GlitchAnalyticsBridge.cs",
    "GlitchMarketSnapshotJson.cs",
    "GlitchMarketSnapshotRawJson.cs",
    "GlitchHistoricalCorpusWriter.cs",
)
EXPORT_DRIVER = "GlitchHistoricalCorpusExportStrategy.cs"
BUILDER_PATH = "tools/hermes/build-historical-backtest-corpus.py"
SCOPED_SOURCE_PATHS = tuple(
    f"ninjatrader/Glitch/Indicators/glitch/{name}" for name in SOURCE_FILES
) + (
    f"ninjatrader/Glitch/Strategies/{EXPORT_DRIVER}",
    BUILDER_PATH,
)


def parse_args() -> argparse.Namespace:
    root = Path(__file__).resolve().parents[2]
    documents = Path.home() / "Documents" / "NinjaTrader 8"
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus-root", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--source-root", type=Path, default=root)
    parser.add_argument(
        "--live-indicator-root",
        type=Path,
        default=documents / "bin" / "Custom" / "Indicators" / "glitch",
    )
    parser.add_argument(
        "--ninjatrader-config",
        type=Path,
        default=documents / "config.xml",
    )
    parser.add_argument(
        "--live-export-driver",
        type=Path,
        default=documents / "bin" / "Custom" / "Strategies" / "glitch" / EXPORT_DRIVER,
    )
    parser.add_argument(
        "--instrument-order",
        nargs="+",
        default=None,
        help="Prompt order. Defaults to the current installed live market snapshot order.",
    )
    parser.add_argument(
        "--live-market-snapshot",
        type=Path,
        default=documents / "GlitchData" / "snapshots" / "market" / "latest.json",
    )
    return parser.parse_args()


def fail(message: str) -> None:
    raise ValueError(message)


def normalized_source(path: Path) -> bytes:
    text = (
        path.read_text(encoding="utf-8-sig")
        .replace("\r\n", "\n")
        .replace("\r", "\n")
    )
    marker = "#region NinjaScript generated code"
    if marker in text:
        text = text.split(marker, 1)[0]
    return (text.rstrip() + "\n").encode("utf-8")


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def verify_source_live_parity(
    source_root: Path,
    live_root: Path,
    live_export_driver: Path,
) -> dict[str, Any]:
    source_dir = source_root / "ninjatrader" / "Glitch" / "Indicators" / "glitch"
    rows: dict[str, Any] = OrderedDict()
    for name in SOURCE_FILES:
        source_path = source_dir / name
        live_path = live_root / name
        if not source_path.is_file():
            fail(f"missing canonical source file: {source_path}")
        if not live_path.is_file():
            fail(f"missing installed indicator file: {live_path}")
        source_bytes = normalized_source(source_path)
        live_bytes = normalized_source(live_path)
        source_hash = sha256_bytes(source_bytes)
        live_hash = sha256_bytes(live_bytes)
        if source_hash != live_hash:
            fail(f"installed/source mismatch for {name}: {live_hash} != {source_hash}")
        rows[name] = {
            "sha256": source_hash,
            "source_path": str(source_path.resolve()),
            "installed_path": str(live_path.resolve()),
        }

    driver_source = (
        source_root / "ninjatrader" / "Glitch" / "Strategies" / EXPORT_DRIVER
    )
    if not driver_source.is_file():
        fail(f"missing canonical export driver: {driver_source}")
    if not live_export_driver.is_file():
        fail(f"missing installed export driver: {live_export_driver}")
    source_bytes = normalized_source(driver_source)
    live_bytes = normalized_source(live_export_driver)
    source_hash = sha256_bytes(source_bytes)
    live_hash = sha256_bytes(live_bytes)
    if source_hash != live_hash:
        fail(f"installed/source mismatch for {EXPORT_DRIVER}: {live_hash} != {source_hash}")
    rows[EXPORT_DRIVER] = {
        "sha256": source_hash,
        "source_path": str(driver_source.resolve()),
        "installed_path": str(live_export_driver.resolve()),
    }
    return rows


def git_value(source_root: Path, *args: str) -> str:
    result = subprocess.run(
        ["git", *args],
        cwd=source_root,
        check=True,
        capture_output=True,
        text=True,
    )
    return result.stdout.strip()


def global_merge_policy(config_path: Path) -> str | None:
    if not config_path.is_file():
        return None
    root = ET.parse(config_path).getroot()
    node = root.find(".//GlobalMergePolicy")
    return node.text.strip() if node is not None and node.text else None


def load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        fail(f"expected JSON object: {path}")
    return value


def validate_manifest(
    path: Path,
    instrument: str,
    resolved_global_merge_policy: str | None,
) -> dict[str, Any]:
    manifest = load_json(path)
    if manifest.get("schema_version") != CORPUS_SCHEMA:
        fail(f"wrong corpus schema in {path}")
    if manifest.get("market_snapshot_schema") != MARKET_SCHEMA:
        fail(f"wrong market schema in {path}")
    if manifest.get("minimum_warmup_bars_per_timeframe") != 200:
        fail(f"wrong warmup contract in {path}")

    instrument_row = manifest.get("instrument") or {}
    if str(instrument_row.get("root", "")).upper() != instrument:
        fail(f"instrument manifest mismatch in {path}")

    configured_merge_policy = instrument_row.get("merge_policy")
    effective_merge_policy = (
        resolved_global_merge_policy
        if configured_merge_policy == "UseGlobalSettings"
        else configured_merge_policy
    )
    if effective_merge_policy != "MergeBackAdjusted":
        fail(
            f"{instrument} must use MergeBackAdjusted, got "
            f"configured={configured_merge_policy!r}, effective={effective_merge_policy!r}"
        )
    instrument_row["effective_merge_policy"] = effective_merge_policy

    parameters = manifest.get("bridge_parameters") or {}
    expected = {
        "neutral_band": 0.01,
        "enable_bar_coloring": False,
        "publish_to_glitch_ui": False,
        "publish_interval_ms": 750,
        "intra_bar_coloring": False,
        "predictive_boost": 0.35,
        "flip_hysteresis": 0.03,
        "performance_mode": True,
        "enable_order_flow_layer": False,
        "order_flow_blend": 0.8,
    }
    if parameters != expected:
        fail(f"unexpected GlitchAnalyticsBridge parameters in {path}: {parameters!r}")
    return manifest


def validate_snapshot(snapshot: dict[str, Any], instrument: str, path: Path) -> str:
    if snapshot.get("schema_version") != MARKET_SCHEMA:
        fail(f"wrong snapshot schema in {path}")
    if snapshot.get("source_mode") != "historical_replay":
        fail(f"wrong source mode in {path}")
    if snapshot.get("instrument_count") != 1:
        fail(f"expected one instrument per raw row in {path}")
    instruments = snapshot.get("instruments") or []
    if len(instruments) != 1:
        fail(f"missing raw instrument payload in {path}")
    row = instruments[0]
    if str(row.get("instrument", "")).upper() != instrument:
        fail(f"instrument row mismatch in {path}")
    if row.get("current_price") is None or row.get("is_fresh") is not True:
        fail(f"historical current_price/freshness missing in {path}")

    bars = row.get("timeframe_bars") or []
    by_minutes = {item.get("minutes"): item for item in bars}
    if tuple(sorted(by_minutes)) != REQUIRED_TIMEFRAMES:
        fail(f"timeframe coverage mismatch in {path}")
    for minutes, bar in by_minutes.items():
        indicators = bar.get("indicators") or {}
        derived = bar.get("derived_analytics") or {}
        for key in ("atr", "adx", "rsi", "stoch_k", "z_score", "average_price"):
            if indicators.get(key) is None:
                fail(f"{instrument} {minutes}m missing {key} in {path}")
        for key in (
            "raw_score",
            "directional_score",
            "tradeability_score",
            "ema_alignment",
            "regime_weight",
            "oscillator_composite_score",
            "ma_composite_score",
        ):
            if derived.get(key) is None:
                fail(f"{instrument} {minutes}m missing {key} in {path}")
        if minutes == 1 and not isinstance(bar.get("descriptive_state"), dict):
            fail(f"{instrument} missing 1m descriptive state in {path}")
        if minutes != 1 and bar.get("descriptive_state") is not None:
            fail(f"{instrument} non-1m descriptive state must be null in {path}")

    created_utc = snapshot.get("created_utc")
    if not isinstance(created_utc, str) or not created_utc:
        fail(f"missing created_utc in {path}")
    return created_utc


def verify_snapshot_hash(line: str, path: Path, line_number: int) -> None:
    match = re.search(r',"snapshot_hash":"(-?\d+)"', line)
    if match is None:
        fail(f"missing snapshot hash in {path}:{line_number}")
    without_hash = line[: match.start()] + line[match.end() :]
    if stable_hash(without_hash) != match.group(1):
        fail(f"snapshot hash mismatch in {path}:{line_number}")


def resolve_instrument_order(args: argparse.Namespace) -> tuple[tuple[str, ...], str]:
    if args.instrument_order:
        order = tuple(str(item).upper() for item in args.instrument_order)
        source = "command_line"
    else:
        if not args.live_market_snapshot.is_file():
            fail(
                "live market snapshot is required to preserve prompt instrument order: "
                f"{args.live_market_snapshot}"
            )
        snapshot = load_json(args.live_market_snapshot)
        order = tuple(
            str(row.get("instrument", "")).upper()
            for row in snapshot.get("instruments", [])
            if row.get("instrument")
        )
        source = str(args.live_market_snapshot.resolve())
    if len(order) != len(REQUIRED_INSTRUMENTS) or set(order) != set(REQUIRED_INSTRUMENTS):
        fail(f"instrument order must contain exactly {REQUIRED_INSTRUMENTS}, got {order}")
    return order, source


def read_part(path: Path, instrument: str) -> dict[str, dict[str, Any]]:
    metadata_path = Path(str(path) + ".meta.json")
    if not metadata_path.is_file():
        fail(f"missing part metadata: {metadata_path}")
    metadata = load_json(metadata_path)
    day = path.name.removesuffix(".jsonl.gz")
    if metadata.get("schema_version") != CORPUS_SCHEMA:
        fail(f"wrong part metadata schema: {metadata_path}")
    if metadata.get("market_snapshot_schema") != MARKET_SCHEMA:
        fail(f"wrong part market schema: {metadata_path}")
    if metadata.get("day_utc") != day:
        fail(f"part metadata day mismatch: {metadata_path}")
    if metadata.get("sha256") != sha256_file(path):
        fail(f"part hash mismatch: {path}")

    rows: dict[str, dict[str, Any]] = {}
    with gzip.open(path, "rt", encoding="utf-8") as stream:
        for line_number, line in enumerate(stream, 1):
            if not line.strip():
                continue
            verify_snapshot_hash(line.rstrip("\r\n"), path, line_number)
            snapshot = json.loads(line)
            timestamp = validate_snapshot(snapshot, instrument, path)
            if timestamp in rows:
                fail(f"duplicate timestamp {timestamp} in {path}:{line_number}")
            rows[timestamp] = snapshot
    if metadata.get("row_count") != len(rows):
        fail(f"row count mismatch in {path}")
    timestamps = sorted(rows)
    if timestamps:
        if metadata.get("first_bar_close_utc") != timestamps[0]:
            fail(f"part first timestamp mismatch in {path}")
        if metadata.get("last_bar_close_utc") != timestamps[-1]:
            fail(f"part last timestamp mismatch in {path}")
    return rows


def validate_export_source_hashes(
    manifests: dict[str, dict[str, Any]],
    source_files: dict[str, Any],
) -> None:
    expected = {name: row["sha256"] for name, row in source_files.items()}
    for instrument, manifest in manifests.items():
        rows = manifest.get("calculation_sources") or {}
        actual = {
            name: row.get("sha256")
            for name, row in rows.items()
            if isinstance(row, dict)
        }
        if actual != expected:
            fail(
                f"{instrument} export-time calculation source hashes do not match "
                f"the current canonical/installed source"
            )


def validate_cross_instrument_contract(
    manifests: dict[str, dict[str, Any]],
) -> None:
    trading_hours = {
        instrument: (manifest.get("instrument") or {}).get("trading_hours")
        for instrument, manifest in manifests.items()
    }
    if len(set(trading_hours.values())) != 1:
        fail(f"instruments must use one trading-hours template: {trading_hours!r}")


def stable_hash(text: str) -> str:
    value = 17
    utf16 = text.encode("utf-16-le", errors="surrogatepass")
    for offset in range(0, len(utf16), 2):
        code_unit = utf16[offset] | (utf16[offset + 1] << 8)
        value = ((value * 31) + code_unit) & 0xFFFFFFFF
    if value >= 0x80000000:
        value -= 0x100000000
    return str(value)


def compact_json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"))


def combined_snapshot(
    timestamp: str,
    rows: dict[str, dict[str, Any]],
    instrument_order: tuple[str, ...] = REQUIRED_INSTRUMENTS,
) -> str:
    instruments = [rows[name]["instruments"][0] for name in instrument_order]
    coverage = []
    for instrument in instruments:
        present = sorted(bar["minutes"] for bar in instrument.get("timeframe_bars", []))
        missing = [minutes for minutes in REQUIRED_TIMEFRAMES if minutes not in present]
        coverage.append(
            OrderedDict(
                (
                    ("instrument_root", instrument["instrument"]),
                    ("is_fresh", instrument["is_fresh"]),
                    ("present_timeframes_minutes", present),
                    ("missing_timeframes_minutes", missing),
                )
            )
        )

    snapshot_id = datetime.fromisoformat(timestamp.replace("Z", "+00:00")).astimezone(
        timezone.utc
    ).strftime("%Y%m%dT%H%M%SZ")
    base = OrderedDict(
        (
            ("schema_version", MARKET_SCHEMA),
            ("created_utc", timestamp),
            ("snapshot_id", snapshot_id),
            ("source_mode", "historical_replay"),
            ("required_timeframes_minutes", list(REQUIRED_TIMEFRAMES)),
            ("fresh_instrument_count", sum(1 for row in instruments if row["is_fresh"])),
            ("instrument_count", len(instruments)),
            ("coverage", coverage),
            ("instruments", instruments),
        )
    )
    hash_value = stable_hash(compact_json(base))
    result = OrderedDict()
    for key, value in base.items():
        result[key] = value
        if key == "snapshot_id":
            result["snapshot_hash"] = hash_value
    return compact_json(result)


def write_part(path: Path, lines: Iterable[str]) -> tuple[int, str, str]:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = Path(str(path) + ".tmp")
    count = 0
    first = ""
    last = ""
    with gzip.open(temp, "wt", encoding="utf-8", newline="\n") as stream:
        for line in lines:
            snapshot = json.loads(line)
            timestamp = snapshot["created_utc"]
            if not first:
                first = timestamp
            last = timestamp
            stream.write(line)
            stream.write("\n")
            count += 1
    temp.replace(path)
    return count, first, last


def main() -> int:
    args = parse_args()
    instrument_order, instrument_order_source = resolve_instrument_order(args)
    instruments = REQUIRED_INSTRUMENTS
    output_root = args.output_root.resolve()
    corpus_root = args.corpus_root.resolve()
    source_root = args.source_root.resolve()
    if output_root == corpus_root:
        fail("output-root must be separate from the raw corpus root")
    if output_root == source_root or source_root in output_root.parents:
        fail("output-root must be outside the canonical source worktree")
    if args.output_root.exists() and any(args.output_root.iterdir()):
        fail("output-root must be new or empty to prevent stale mixed datasets")

    overall_status = git_value(args.source_root, "status", "--short")
    scoped_status = git_value(
        args.source_root,
        "status",
        "--short",
        "--",
        *SCOPED_SOURCE_PATHS,
    )
    if scoped_status:
        fail(
            "calculation/export source files must match the recorded git commit "
            "before finalizing a corpus"
        )

    source_files = verify_source_live_parity(
        args.source_root,
        args.live_indicator_root,
        args.live_export_driver,
    )
    resolved_global_merge_policy = global_merge_policy(args.ninjatrader_config)
    manifests = {
        instrument: validate_manifest(
            args.corpus_root / instrument / "manifest.json",
            instrument,
            resolved_global_merge_policy,
        )
        for instrument in instruments
    }
    validate_export_source_hashes(manifests, source_files)
    validate_cross_instrument_contract(manifests)

    day_sets = {
        instrument: {
            path.name.removesuffix(".jsonl.gz")
            for path in (args.corpus_root / instrument).glob("*.jsonl.gz")
        }
        for instrument in instruments
    }
    common_days = sorted(set.intersection(*(day_sets[name] for name in instruments)))
    if not common_days:
        fail("no complete MES/MNQ/M2K day parts found")

    args.output_root.mkdir(parents=True, exist_ok=True)
    total_rows = 0
    first_timestamp = ""
    last_timestamp = ""
    dropped_by_instrument = {instrument: 0 for instrument in instruments}
    part_rows = []

    for day in common_days:
        raw = {
            instrument: read_part(
                args.corpus_root / instrument / f"{day}.jsonl.gz", instrument
            )
            for instrument in instruments
        }
        common_timestamps = sorted(set.intersection(*(set(raw[name]) for name in instruments)))
        for instrument in instruments:
            dropped_by_instrument[instrument] += len(raw[instrument]) - len(common_timestamps)
        if not common_timestamps:
            continue

        output_path = args.output_root / f"{day}.jsonl.gz"
        count, first, last = write_part(
            output_path,
            (
                combined_snapshot(
                    timestamp,
                    {instrument: raw[instrument][timestamp] for instrument in instruments},
                    instrument_order,
                )
                for timestamp in common_timestamps
            ),
        )
        part_hash = sha256_file(output_path)
        part_rows.append(
            {
                "day_utc": day,
                "path": str(output_path.resolve()),
                "row_count": count,
                "first_utc": first,
                "last_utc": last,
                "sha256": part_hash,
            }
        )
        total_rows += count
        if not first_timestamp:
            first_timestamp = first
        last_timestamp = last

    if total_rows == 0:
        fail("no synchronized rows were written")

    manifest = {
        "schema_version": DATASET_SCHEMA,
        "market_snapshot_schema": MARKET_SCHEMA,
        "created_utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "source": {
            "git_commit": git_value(args.source_root, "rev-parse", "HEAD"),
            "git_branch": git_value(args.source_root, "rev-parse", "--abbrev-ref", "HEAD"),
            "working_tree_clean": not bool(overall_status),
            "calculation_export_source_clean": True,
            "calculation_files": source_files,
            "dataset_builder": {
                "path": str((args.source_root / BUILDER_PATH).resolve()),
                "sha256": sha256_file(args.source_root / BUILDER_PATH),
            },
            "installed_source_parity": True,
        },
        "instruments": manifests,
        "resolved_global_merge_policy": resolved_global_merge_policy,
        "instrument_prompt_order": {
            "order": list(instrument_order),
            "source": instrument_order_source,
        },
        "time_alignment": "exact_utc_intersection",
        "range": {"first_utc": first_timestamp, "last_utc": last_timestamp},
        "row_count": total_rows,
        "dropped_unpaired_rows": dropped_by_instrument,
        "parts": part_rows,
        "fidelity": {
            "deterministic_calculations": "canonical_source_and_installed_hashes_match",
            "instrument_set": sorted(REQUIRED_INSTRUMENTS),
            "timeframes_minutes": list(REQUIRED_TIMEFRAMES),
            "observation_clock": "completed_one_minute_bars",
            "limitations": [
                "Historical multi-timeframe bars do not reproduce every in-progress tick update seen live.",
                "Historical level-2 depth and live quote-tape classification are unavailable.",
                "Historical fundamental/news context is unavailable.",
                "This dataset contains market observations, not native fill or queue-position replay.",
            ],
        },
    }
    manifest_path = args.output_root / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(
        json.dumps(
            {
                "manifest": str(manifest_path.resolve()),
                "rows": total_rows,
                "first_utc": first_timestamp,
                "last_utc": last_timestamp,
                "dropped_unpaired_rows": dropped_by_instrument,
            }
        )
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, subprocess.CalledProcessError, json.JSONDecodeError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        raise SystemExit(1)
