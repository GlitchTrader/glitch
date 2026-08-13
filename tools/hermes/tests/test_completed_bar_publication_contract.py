from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
BRIDGE = ROOT / "ninjatrader" / "Glitch" / "Indicators" / "glitch" / "GlitchAnalyticsBridge.cs"


def test_native_snapshot_publishes_prior_completed_bar_separately() -> None:
    source = BRIDGE.read_text(encoding="utf-8")

    assert 'sb.Append("\\\"last_completed_bar\\\":");' in source
    assert "State == State.Realtime && CurrentBars[bip] >= 1" in source
    assert "Opens[bip][1]" in source
    assert "Highs[bip][1]" in source
    assert "Lows[bip][1]" in source
    assert "Closes[bip][1]" in source
    assert "Volumes[bip][1]" in source
    assert r'\"completeness\":\"complete\"' in source
    assert r'\"source\":\"ninjatrader_bars_ago_1\"' in source
