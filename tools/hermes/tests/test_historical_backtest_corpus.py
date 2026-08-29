import importlib.util
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
SCRIPT = ROOT / "tools" / "hermes" / "build-historical-backtest-corpus.py"
SPEC = importlib.util.spec_from_file_location("historical_corpus", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


def raw_snapshot(instrument: str, timestamp: str) -> dict:
    bars = []
    for minutes in MODULE.REQUIRED_TIMEFRAMES:
        bars.append(
            {
                "minutes": minutes,
                "utc_time": timestamp,
                "open": 1.0,
                "high": 2.0,
                "low": 0.5,
                "close": 1.5,
                "volume": 10.0,
                "descriptive_state": {} if minutes == 1 else None,
                "indicators": {
                    "atr": 1.0,
                    "adx": 25.0,
                    "rsi": 50.0,
                    "stoch_k": 50.0,
                    "z_score": 0.0,
                    "average_price": 1.0,
                },
                "derived_analytics": {
                    "raw_score": 0.0,
                    "directional_score": 0.0,
                    "tradeability_score": 0.5,
                    "ema_alignment": 0.0,
                    "regime_weight": 0.75,
                    "oscillator_composite_score": 0.0,
                    "ma_composite_score": 0.0,
                },
            }
        )
    return {
        "schema_version": MODULE.MARKET_SCHEMA,
        "created_utc": timestamp,
        "snapshot_id": timestamp,
        "source_mode": "historical_replay",
        "instrument_count": 1,
        "instruments": [
            {
                "instrument": instrument,
                "is_fresh": True,
                "current_price": 1.5,
                "timeframe_bars": bars,
            }
        ],
    }


def test_stable_hash_matches_glitch_int32_algorithm():
    assert MODULE.stable_hash("abc") == "602801"
    assert MODULE.stable_hash("a😀b") == "73549551"


def test_combined_snapshot_has_exact_three_instrument_contract():
    timestamp = "2026-01-02T03:04:00.0000000Z"
    rows = {
        name: raw_snapshot(name, timestamp)
        for name in MODULE.REQUIRED_INSTRUMENTS
    }
    combined = json.loads(MODULE.combined_snapshot(timestamp, rows))
    assert combined["schema_version"] == MODULE.MARKET_SCHEMA
    assert combined["instrument_count"] == 3
    assert [row["instrument"] for row in combined["instruments"]] == [
        "MES",
        "MNQ",
        "M2K",
    ]
    assert combined["fresh_instrument_count"] == 3
    assert combined["snapshot_hash"]


def test_raw_snapshot_validation_requires_current_live_semantic_fields():
    timestamp = "2026-01-02T03:04:00.0000000Z"
    snapshot = raw_snapshot("MES", timestamp)
    part = Path("synthetic-part")
    assert MODULE.validate_snapshot(snapshot, "MES", part) == timestamp

    snapshot["instruments"][0]["current_price"] = None
    try:
        MODULE.validate_snapshot(snapshot, "MES", part)
    except ValueError as error:
        assert "current_price/freshness" in str(error)
    else:
        raise AssertionError("missing historical current_price must fail validation")


def test_export_driver_is_no_order_and_uses_canonical_bridge():
    strategy = (
        ROOT
        / "ninjatrader"
        / "Glitch"
        / "Strategies"
        / "GlitchHistoricalCorpusExportStrategy.cs"
    ).read_text(encoding="utf-8")
    assert "GlitchAnalyticsBridge(" in strategy
    assert "HistoricalExportCount" in strategy
    for forbidden in (
        "EnterLong(",
        "EnterShort(",
        "SubmitOrderUnmanaged(",
        "ExitLong(",
        "ExitShort(",
        "Account.CreateOrder(",
    ):
        assert forbidden not in strategy


def test_bridge_historical_path_serializes_live_equivalent_features():
    bridge = (
        ROOT
        / "ninjatrader"
        / "Glitch"
        / "Indicators"
        / "glitch"
        / "GlitchAnalyticsBridge.cs"
    ).read_text(encoding="utf-8")
    assert "HistoricalExportWarmupBars = 200" in bridge
    assert "IsFresh = true" in bridge
    assert "CurrentPrice = Closes[0][0]" in bridge
    assert "DerivedAnalytics = new GlitchMarketSnapshotRawJson.DerivedAnalyticsPayload" in bridge
    assert "minutes == 1" in bridge
    assert "BuildHistoricalCorpusDescriptor()" in bridge


def test_writer_uses_versioned_daily_gzip_parts_not_minute_files():
    writer = (
        ROOT
        / "ninjatrader"
        / "Glitch"
        / "Indicators"
        / "glitch"
        / "GlitchHistoricalCorpusWriter.cs"
    ).read_text(encoding="utf-8")
    assert 'CorpusSchemaVersion = "glitch.market.corpus.v1"' in writer
    assert '"backtest-corpus-v1"' in writer
    assert '".jsonl.gz"' in writer
    assert "GZipStream" in writer
    assert "calculation_sources" in writer
    assert "out string failureReason" in writer
    assert "snapshotId + \".json\"" not in writer


def test_snapshot_hash_validation_detects_tampering():
    base = '{"snapshot_id":"x","value":1}'
    hashed = '{"snapshot_id":"x","snapshot_hash":"' + MODULE.stable_hash(base) + '","value":1}'
    MODULE.verify_snapshot_hash(hashed, Path("synthetic-part"), 1)

    tampered = hashed.replace('"value":1', '"value":2')
    try:
        MODULE.verify_snapshot_hash(tampered, Path("synthetic-part"), 1)
    except ValueError as error:
        assert "snapshot hash mismatch" in str(error)
    else:
        raise AssertionError("tampered snapshot must fail hash validation")


def test_export_source_hash_validation_requires_export_time_parity():
    manifests = {
        instrument: {"calculation_sources": {"a.cs": {"sha256": "abc"}}}
        for instrument in MODULE.REQUIRED_INSTRUMENTS
    }
    MODULE.validate_export_source_hashes(manifests, {"a.cs": {"sha256": "abc"}})
    manifests["MES"]["calculation_sources"]["a.cs"]["sha256"] = "wrong"
    try:
        MODULE.validate_export_source_hashes(manifests, {"a.cs": {"sha256": "abc"}})
    except ValueError as error:
        assert "export-time calculation source hashes" in str(error)
    else:
        raise AssertionError("stale export source hash must fail validation")
