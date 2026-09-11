using System;
using System.Collections.Generic;
using System.IO;
using Glitch.Services;

internal static class GlitchAiHealthHarness
{
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    public static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "GlitchHealth-" + Guid.NewGuid().ToString("N"));
        try
        {
            GlitchHermesExchangeWriter.Root = root;
            Directory.CreateDirectory(Path.Combine(root, "glitch"));
            Directory.CreateDirectory(Path.Combine(root, "hermes", "model-attempts"));
            Directory.CreateDirectory(Path.Combine(root, "hermes", "events"));
            Directory.CreateDirectory(Path.Combine(root, "hermes", "supervisor"));
            var now = new DateTime(2026, 9, 11, 22, 0, 0, DateTimeKind.Utc);
            File.WriteAllText(Path.Combine(root, "glitch", "latest-decision-packet.json"),
                "{\"window_close_utc\":\"2026-09-11T22:00:00Z\",\"is_contiguous\":true,\"observed_span_minutes\":5}");
            File.WriteAllText(Path.Combine(root, "latest.json"), "{\"created_utc\":\"2026-09-11T22:00:00Z\"}");
            string attempt = Path.Combine(root, "hermes", "model-attempts", "20260911T2157Z.json");
            File.WriteAllText(attempt, "{\"status\":\"completed\",\"started_utc\":\"2026-09-11T21:57:00Z\"}");
            File.WriteAllText(Path.Combine(root, "hermes", "supervisor", "learning-worker-status.json"),
                "{\"status\":\"deferred\",\"recorded_utc\":\"2026-09-11T22:00:00Z\"}");
            string events = Path.Combine(root, "hermes", "events", "cycles.jsonl");
            foreach (string symbol in new[] { "MES", "M2K", "MNQ" })
            {
                GlitchAiPortfolioSnapshotReader.Instrument = symbol;
                Require(GlitchAiHealthEvaluator.Evaluate(now).ReasonCodes.Contains("decision_worker_overdue"),
                    symbol + " position did not use management cadence");
            }
            GlitchAiPortfolioSnapshotReader.PositionAccount = "SecondMaster";
            Require(GlitchAiHealthEvaluator.Evaluate(now).ReasonCodes.Contains("decision_worker_overdue"),
                "second selected master was treated as flat");
            GlitchAiPortfolioSnapshotReader.PositionAccount = "";
            foreach (string reason in new[] { "weekend", "maintenance_window", "market_session_closed", "native_daily_capture_locked_and_group_flat" })
            {
                File.WriteAllText(events, Event("llm_skipped", reason, "2026-09-11T22:00:00Z", "20260911T2200Z"));
                Require(GlitchAiHealthEvaluator.RecentHealthyDeferral(events, now, now, false) == reason,
                    "flat native admission deferral was not recognized");
                GlitchAiPortfolioSnapshotReader.PositionAccount = "Master";
                if (reason != "native_daily_capture_locked_and_group_flat")
                    Require(!GlitchAiHealthEvaluator.Evaluate(now).ReasonCodes.Contains("decision_worker_overdue"),
                        "healthy deferral was treated as a missing worker: " + reason);
                GlitchAiPortfolioSnapshotReader.PositionAccount = "";
            }
            GlitchAiPortfolioSnapshotReader.PositionAccount = "Master";
            foreach (string reason in new[] { "stale_market_package", "position_state_packet_lagging_native_transition", "unknown" })
            {
                File.WriteAllText(events, Event("llm_skipped", reason, "2026-09-11T22:00:00Z", "20260911T2200Z"));
                Require(GlitchAiHealthEvaluator.Evaluate(now).ReasonCodes.Contains("decision_worker_overdue"), "unsafe deferral hid missing decisions");
            }
            File.WriteAllText(events, Event("llm_skipped", "weekend", "2026-09-11T21:50:00Z", "20260911T2150Z"));
            Require(GlitchAiHealthEvaluator.Evaluate(now).ReasonCodes.Contains("decision_worker_overdue"), "stale weekend state hid stalled worker");
            foreach (string invalid in new[] {
                "{\"event\":\"llm_skipped\",\"reason\":\"weekend\"}",
                Event("llm_skipped", "weekend", "2026-09-11T22:10:00Z", "20260911T2200Z"),
                Event("llm_skipped", "weekend", "2026-09-11T22:00:00Z", "20260911T2150Z"),
                Event("failed", "weekend", "2026-09-11T22:00:00Z", "20260911T2200Z") })
            {
                File.WriteAllText(events, invalid);
                Require(GlitchAiHealthEvaluator.RecentHealthyDeferral(events, now, now, false) == null,
                    "invalid or unrelated skip was accepted");
            }
            File.WriteAllText(events, new string('x', 20000) + "\n" + Event("llm_skipped", "weekend", "2026-09-11T22:00:00Z", "20260911T2200Z"));
            Require(GlitchAiHealthEvaluator.RecentHealthyDeferral(events, now, now, false) == "weekend", "bounded tail lost final record");
            File.WriteAllText(events, Event("llm_skipped", "weekend", "2026-09-11T22:00:00Z", "20260911T2200Z"));
            File.WriteAllText(attempt, "{\"status\":\"failed\",\"started_utc\":\"2026-09-11T21:57:00Z\"}");
            Require(GlitchAiHealthEvaluator.Evaluate(now).ReasonCodes.Contains("decision_worker_failed"), "deferral hid model failure");
            File.WriteAllText(attempt, "{\"status\":\"started\",\"started_utc\":\"2026-09-11T21:50:00Z\"}");
            Require(GlitchAiHealthEvaluator.Evaluate(now).ReasonCodes.Contains("decision_worker_stalled"), "deferral hid active stall");
            Console.WriteLine("Glitch health harness passed (all instruments, masters, deferrals, failures, stalls).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static string Event(string type, string reason, string utc, string cycle)
    { return "{\"schema_version\":\"glitch.hermes.cycle_event.v1\",\"event\":\"" + type + "\",\"reason\":\"" + reason + "\",\"recorded_utc\":\"" + utc + "\",\"cycle_id\":\"" + cycle + "\"}\n"; }
}

// Boundary doubles only. Evaluate and JSON parsing below use production source.
namespace Glitch.Services
{
    internal sealed class GlitchHermesControlState { public bool TradingPaused; }
    internal static class GlitchHermesControlStateStore { public static GlitchHermesControlState Load() => new GlitchHermesControlState(); }
    internal sealed class GlitchAiRailPolicy
    {
        public bool IsValid = true; public int SnapshotMaxAgeSeconds = 300; public string ValidationError;
        public string ExecutorAccount = "Master";
        public Dictionary<string,string> ProfileAccountBindings = new Dictionary<string,string> { {"glitch", "Master"}, {"glitch-2", "SecondMaster"} };
    }
    internal static class GlitchAiRailPolicyStore { public static GlitchAiRailPolicy Load() => new GlitchAiRailPolicy(); }
    internal static class GlitchAiAutoRuntimeController { public static bool IsTradingJobEnabled() => true; }
    internal static class GlitchExternalTelemetryServer { public static bool IsRunning => true; }
    internal static class GlitchAiIntentServer { public static bool IsRunning => true; }
    internal static class GlitchHermesControlServer { public static bool IsRunning => true; }
    internal static class GlitchHermesExchangeWriter { public static string Root; public static string GetExchangeRoot() => Root; }
    internal static class GlitchMarketSnapshotWriter
    {
        public static string TryGetLatestSnapshotHash() => "test";
        public static string GetLatestSnapshotPath() => Path.Combine(GlitchHermesExchangeWriter.Root, "latest.json");
    }
    internal static class GlitchSnapshotJson { public static string String(string s) => "\"" + s + "\""; public static string Bool(bool b) => b ? "true" : "false"; }
    internal static class GlitchAiPortfolioSnapshotReader
    {
        public static string Instrument = "MES", PositionAccount = "Master";
        public static bool TryGetFreshRiskState(string account, DateTime now, int age, out bool locked, out bool eval, out double pnl, out string json, out string failure)
        { locked = eval = false; pnl = 0; json = account == PositionAccount ? Instrument : ""; failure = ""; return true; }
        public static bool TryGetOpenPositionQuantityFromAccountBlock(string json, string instrument, out int quantity)
        { quantity = json == instrument ? 1 : 0; return true; }
        public static bool TryGetTotalOpenContractsFromAccountBlock(string json, out int quantity)
        { quantity = json.Length > 0 ? 1 : 0; return true; }
    }
}
