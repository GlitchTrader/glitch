from pathlib import Path
import json
import subprocess

import pytest


ROOT = Path(__file__).resolve().parents[3]
BRIDGE = ROOT / "ninjatrader" / "Glitch" / "Indicators" / "glitch" / "GlitchAnalyticsBridge.cs"
INGEST = BRIDGE.with_name("GlitchAiMarketIngest.cs")


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


def test_lightweight_publisher_cannot_erase_candle_fields() -> None:
    source = INGEST.read_text(encoding="utf-8")
    reading = source.split("return new GlitchBridgeBusCompat.BridgeReading", 1)[1].split("private string BuildDescriptiveStateJson", 1)[0]
    for field, series in (("Open", "Opens"), ("High", "Highs"), ("Low", "Lows"), ("Volume", "Volumes")):
        assert f"{field} = {series}[bip][0]" in reading
    assert r'"\"last_completed_bar\":" + BuildLastCompletedBarJson(bip)' in source
    assert "Calculate = Calculate.OnBarClose" in source


def test_ingest_completed_json_uses_secondary_series_and_never_live_bar(tmp_path: Path) -> None:
    compiler = Path("C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe")
    if not compiler.exists():
        pytest.skip("Windows .NET compiler unavailable")
    source = INGEST.read_text(encoding="utf-8")
    method = "        private string BuildLastCompletedBarJson" + source.split("        private string BuildLastCompletedBarJson", 1)[1].split("        private static string Number", 1)[0]
    harness = r'''
using System;
using System.Globalization;
using System.Web.Script.Serialization;
class GlitchMarketSnapshotJsonInject {
    public static string String(string s) { return new JavaScriptSerializer().Serialize(s); }
}
class Probe {
    int[] CurrentBars = new int[] { 4, 4 };
    DateTime[][] Times = new DateTime[][] {
        new DateTime[] { DateTime.UtcNow, DateTime.UtcNow.AddMinutes(-1) },
        new DateTime[] { new DateTime(2026,9,10,6,1,0,DateTimeKind.Utc), new DateTime(2026,9,10,6,0,0,DateTimeKind.Utc) }
    };
    double[][] Opens = new double[][] { new double[] { 999,998 }, new double[] { 9999,2910.1 } };
    double[][] Highs = new double[][] { new double[] { 999,998 }, new double[] { 9999,2911.2 } };
    double[][] Lows = new double[][] { new double[] { 999,998 }, new double[] { 9999,2909.3 } };
    double[][] Closes = new double[][] { new double[] { 999,998 }, new double[] { 9999,2910.4 } };
    double[][] Volumes = new double[][] { new double[] { 999,998 }, new double[] { 9999,17 } };
    static string Number(double? n) { return n.HasValue ? n.Value.ToString("R", CultureInfo.InvariantCulture) : "null"; }
METHOD
    static void Main() {
        System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");
        var p = new Probe();
        Console.WriteLine(p.BuildLastCompletedBarJson(1));
        if (p.BuildLastCompletedBarJson(-1) != "null" || p.BuildLastCompletedBarJson(2) != "null") throw new Exception("BIP guard");
        p.CurrentBars[1] = 0;
        if (p.BuildLastCompletedBarJson(1) != "null") throw new Exception("history guard");
        p.CurrentBars = null;
        if (p.BuildLastCompletedBarJson(1) != "null") throw new Exception("initialization guard");
    }
}
'''.replace("METHOD", method)
    cs, exe = tmp_path / "probe.cs", tmp_path / "probe.exe"
    cs.write_text(harness, encoding="utf-8")
    subprocess.run([str(compiler), "/nologo", "/r:System.Web.Extensions.dll", f"/out:{exe}", str(cs)], check=True, capture_output=True, text=True)
    result = json.loads(subprocess.check_output([str(exe)], text=True))
    assert {k: result[k] for k in ("open", "high", "low", "close", "volume")} == {
        "open": 2910.1, "high": 2911.2, "low": 2909.3, "close": 2910.4, "volume": 17,
    }
    assert result["utc_time"].startswith("2026-09-10T06:00:00")
    assert result["closed_utc"].startswith("2026-09-10T06:01:00")
    assert result["completeness"] == "complete"
    assert result["source"] == "ninjatrader_bars_ago_1"
