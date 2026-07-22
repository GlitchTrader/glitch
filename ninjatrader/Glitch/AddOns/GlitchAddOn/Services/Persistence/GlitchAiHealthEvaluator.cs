using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Glitch.Services
{
    internal sealed class GlitchAiHealthSnapshot
    {
        public string OverallStatus { get; set; }
        public List<string> ReasonCodes { get; set; } = new List<string>();
        public bool AiAutoEnabled { get; set; }
        public bool TradingJobEnabled { get; set; }
        public bool Operating { get; set; }
        public string PacketStatus { get; set; }
        public double PacketAgeSeconds { get; set; } = -1;
        public bool PacketContiguous { get; set; }
        public int PacketObservedSpanMinutes { get; set; }
        public string DecisionWorkerStatus { get; set; }
        public double DecisionAttemptAgeSeconds { get; set; } = -1;
        public string LearningWorkerStatus { get; set; }
        public double LearningWorkerAgeSeconds { get; set; } = -1;
        public bool TelemetryServerRunning { get; set; }
        public bool IntentServerRunning { get; set; }
        public bool ControlServerRunning { get; set; }
        public bool PolicyValid { get; set; }
        public string PolicyError { get; set; }
        public string SelectedMaster { get; set; }
        public bool SelectedMasterNativeState { get; set; }
        public string SnapshotHash { get; set; }
        public double FeedAgeSeconds { get; set; } = -1;

        public string ToJson()
        {
            var sb = new StringBuilder(1024);
            sb.Append('{');
            sb.Append("\"schema_version\":").Append(GlitchSnapshotJson.String("glitch.ai.health.v1")).Append(',');
            sb.Append("\"status\":").Append(GlitchSnapshotJson.String(OverallStatus)).Append(',');
            sb.Append("\"reason_codes\":[");
            for (int i = 0; i < ReasonCodes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(GlitchSnapshotJson.String(ReasonCodes[i]));
            }
            sb.Append("],");
            sb.Append("\"ai_auto_enabled\":").Append(GlitchSnapshotJson.Bool(AiAutoEnabled)).Append(',');
            sb.Append("\"operator_job_enabled\":").Append(GlitchSnapshotJson.Bool(TradingJobEnabled)).Append(',');
            sb.Append("\"operating\":").Append(GlitchSnapshotJson.Bool(Operating)).Append(',');
            sb.Append("\"packet\":{");
            sb.Append("\"status\":").Append(GlitchSnapshotJson.String(PacketStatus)).Append(',');
            sb.Append("\"age_seconds\":").Append(FormatNumber(PacketAgeSeconds)).Append(',');
            sb.Append("\"is_contiguous\":").Append(GlitchSnapshotJson.Bool(PacketContiguous)).Append(',');
            sb.Append("\"observed_span_minutes\":").Append(PacketObservedSpanMinutes.ToString(CultureInfo.InvariantCulture));
            sb.Append("},");
            sb.Append("\"decision_worker\":{");
            sb.Append("\"status\":").Append(GlitchSnapshotJson.String(DecisionWorkerStatus)).Append(',');
            sb.Append("\"attempt_age_seconds\":").Append(FormatNumber(DecisionAttemptAgeSeconds));
            sb.Append("},");
            sb.Append("\"learning_worker\":{");
            sb.Append("\"status\":").Append(GlitchSnapshotJson.String(LearningWorkerStatus)).Append(',');
            sb.Append("\"age_seconds\":").Append(FormatNumber(LearningWorkerAgeSeconds));
            sb.Append("},");
            sb.Append("\"servers\":{");
            sb.Append("\"telemetry\":").Append(GlitchSnapshotJson.Bool(TelemetryServerRunning)).Append(',');
            sb.Append("\"intent\":").Append(GlitchSnapshotJson.Bool(IntentServerRunning)).Append(',');
            sb.Append("\"control\":").Append(GlitchSnapshotJson.Bool(ControlServerRunning));
            sb.Append("},");
            sb.Append("\"policy\":{");
            sb.Append("\"valid\":").Append(GlitchSnapshotJson.Bool(PolicyValid)).Append(',');
            sb.Append("\"error\":").Append(GlitchSnapshotJson.String(PolicyError ?? string.Empty));
            sb.Append("},");
            sb.Append("\"selected_master\":{");
            sb.Append("\"account\":").Append(GlitchSnapshotJson.String(SelectedMaster ?? string.Empty)).Append(',');
            sb.Append("\"native_state_available\":").Append(GlitchSnapshotJson.Bool(SelectedMasterNativeState));
            sb.Append("},");
            sb.Append("\"feed\":{");
            sb.Append("\"snapshot_hash\":").Append(GlitchSnapshotJson.String(SnapshotHash ?? string.Empty)).Append(',');
            sb.Append("\"age_seconds\":").Append(FormatNumber(FeedAgeSeconds));
            sb.Append("}");
            sb.Append('}');
            return sb.ToString();
        }

        private static string FormatNumber(double value)
        {
            return value < 0 || double.IsNaN(value) || double.IsInfinity(value)
                ? "null"
                : value.ToString("F1", CultureInfo.InvariantCulture);
        }
    }

    internal static class GlitchAiHealthEvaluator
    {
        public static GlitchAiHealthSnapshot Evaluate(DateTime nowUtc)
        {
            var result = new GlitchAiHealthSnapshot();
            GlitchHermesControlState control = GlitchHermesControlStateStore.Load();
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            result.AiAutoEnabled = control != null && !control.TradingPaused;
            result.TradingJobEnabled = GlitchAiAutoRuntimeController.IsTradingJobEnabled();
            result.Operating = result.AiAutoEnabled && result.TradingJobEnabled;
            result.PolicyValid = policy != null && policy.IsValid;
            result.PolicyError = policy?.ValidationError;
            result.SelectedMaster = policy?.ExecutorAccount;
            result.TelemetryServerRunning = GlitchExternalTelemetryServer.IsRunning;
            result.IntentServerRunning = GlitchAiIntentServer.IsRunning;
            result.ControlServerRunning = GlitchHermesControlServer.IsRunning;
            result.SnapshotHash = GlitchMarketSnapshotWriter.TryGetLatestSnapshotHash();
            result.FeedAgeSeconds = AgeSeconds(GlitchMarketSnapshotWriter.GetLatestSnapshotPath(), "created_utc", nowUtc);

            string exchange = GlitchHermesExchangeWriter.GetExchangeRoot();
            string packetPath = Path.Combine(exchange, "glitch", "latest-decision-packet.json");
            string packetJson = null;
            DateTime packetWindowUtc = DateTime.MinValue;
            if (!File.Exists(packetPath))
                result.PacketStatus = "missing";
            else
            {
                packetJson = SafeRead(packetPath);
                packetWindowUtc = GlitchAiJsonFields.TryExtractUtc(packetJson, "window_close_utc") ?? DateTime.MinValue;
                result.PacketAgeSeconds = AgeSeconds(packetPath, "window_close_utc", nowUtc);
                result.PacketContiguous = TryReadBool(packetJson, "is_contiguous", true);
                result.PacketObservedSpanMinutes = ReadInt(packetJson, "observed_span_minutes");
                int maxPacketAge = policy != null ? Math.Max(60, policy.SnapshotMaxAgeSeconds) : 300;
                result.PacketStatus = result.PacketAgeSeconds < 0 || result.PacketAgeSeconds > maxPacketAge
                    ? "stale"
                    : result.PacketContiguous ? "operating" : "gapped";
            }

            FileInfo latestAttempt = LatestFile(Path.Combine(exchange, "hermes", "model-attempts"));
            if (latestAttempt == null)
                result.DecisionWorkerStatus = result.Operating ? "waiting" : "off";
            else
            {
                string attemptJson = SafeRead(latestAttempt.FullName);
                result.DecisionWorkerStatus = GlitchAiJsonFields.ExtractString(attemptJson, "status") ?? "unknown";
                result.DecisionAttemptAgeSeconds = AgeSeconds(latestAttempt.FullName, "started_utc", nowUtc);
            }

            string learningPath = Path.Combine(exchange, "hermes", "supervisor", "learning-worker-status.json");
            result.LearningWorkerStatus = File.Exists(learningPath)
                ? GlitchAiJsonFields.ExtractString(SafeRead(learningPath), "status") ?? "unknown"
                : "waiting";
            result.LearningWorkerAgeSeconds = AgeSeconds(learningPath, "recorded_utc", nowUtc);

            bool masterPositioned = false;
            if (result.PolicyValid && !string.IsNullOrWhiteSpace(result.SelectedMaster))
            {
                result.SelectedMasterNativeState = GlitchAiPortfolioSnapshotReader.TryGetFreshRiskState(
                    result.SelectedMaster,
                    nowUtc,
                    policy.SnapshotMaxAgeSeconds,
                    out _,
                    out _,
                    out _,
                    out string accountJson,
                    out _);
                if (result.SelectedMasterNativeState
                    && GlitchAiPortfolioSnapshotReader.TryGetOpenPositionQuantityFromAccountBlock(
                        accountJson,
                        "MNQ",
                        out int openQuantity))
                    masterPositioned = openQuantity != 0;
            }

            if (!result.AiAutoEnabled)
                result.ReasonCodes.Add("ai_auto_off");
            else if (!result.TradingJobEnabled)
                result.ReasonCodes.Add("operator_job_disabled");
            if (!result.PolicyValid) result.ReasonCodes.Add("policy_invalid");
            if (!result.TelemetryServerRunning) result.ReasonCodes.Add("telemetry_server_down");
            if (!result.IntentServerRunning) result.ReasonCodes.Add("intent_server_down");
            if (!result.ControlServerRunning) result.ReasonCodes.Add("control_server_down");
            if (!string.Equals(result.PacketStatus, "operating", StringComparison.Ordinal))
                result.ReasonCodes.Add("packet_" + result.PacketStatus);
            if (result.FeedAgeSeconds < 0 || result.FeedAgeSeconds > 180)
                result.ReasonCodes.Add("market_feed_stale");
            if (result.PolicyValid && !result.SelectedMasterNativeState)
                result.ReasonCodes.Add("selected_master_native_state_unavailable");
            if (string.Equals(result.DecisionWorkerStatus, "failed", StringComparison.Ordinal)
                || string.Equals(result.DecisionWorkerStatus, "execution_failed", StringComparison.Ordinal)
                || string.Equals(result.DecisionWorkerStatus, "delivery_incomplete", StringComparison.Ordinal))
                result.ReasonCodes.Add("decision_worker_" + result.DecisionWorkerStatus);
            else if (string.Equals(result.DecisionWorkerStatus, "started", StringComparison.Ordinal)
                && result.DecisionAttemptAgeSeconds > 360)
                result.ReasonCodes.Add("decision_worker_stalled");
            else if (result.Operating && latestAttempt != null && packetWindowUtc != DateTime.MinValue
                && TryParseMinuteId(Path.GetFileNameWithoutExtension(latestAttempt.Name), out DateTime attemptWindowUtc))
            {
                double elapsedMinutes = (packetWindowUtc - attemptWindowUtc).TotalMinutes;
                double overdueAfterMinutes = masterPositioned ? 2 : 6;
                if (elapsedMinutes >= overdueAfterMinutes)
                {
                    result.DecisionWorkerStatus = "overdue";
                    result.ReasonCodes.Add("decision_worker_overdue");
                }
            }
            if (string.Equals(result.LearningWorkerStatus, "failed", StringComparison.Ordinal))
                result.ReasonCodes.Add("learning_worker_failed");
            else if (result.Operating && !File.Exists(learningPath))
                result.ReasonCodes.Add("learning_worker_status_missing");
            else if (result.Operating && result.LearningWorkerAgeSeconds > 1200)
                result.ReasonCodes.Add("learning_worker_stale");

            result.OverallStatus = !result.AiAutoEnabled
                ? "off"
                : result.ReasonCodes.Count == 0 ? "on" : "degraded";
            return result;
        }

        private static FileInfo LatestFile(string directory)
        {
            try
            {
                return Directory.Exists(directory)
                    ? new DirectoryInfo(directory).GetFiles("*.json").OrderByDescending(file => file.Name).FirstOrDefault()
                    : null;
            }
            catch { return null; }
        }

        private static string SafeRead(string path)
        {
            try { return File.ReadAllText(path); }
            catch { return string.Empty; }
        }

        private static double AgeSeconds(string path, string field, DateTime nowUtc)
        {
            DateTime? observed = GlitchAiJsonFields.TryExtractUtc(SafeRead(path), field);
            return observed.HasValue ? (nowUtc - observed.Value).TotalSeconds : -1;
        }

        private static int ReadInt(string json, string field)
        {
            return GlitchAiJsonFields.TryExtractNumber(json, field, out double value) ? (int)value : 0;
        }

        private static bool TryReadBool(string json, string field, bool fallback)
        {
            return GlitchAiJsonFields.TryExtractBool(json, field, out bool value) ? value : fallback;
        }

        private static bool TryParseMinuteId(string value, out DateTime parsed)
        {
            return DateTime.TryParseExact(
                value,
                "yyyyMMdd'T'HHmm'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsed);
        }
    }
}
