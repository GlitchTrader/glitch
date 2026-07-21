using System;
using System.IO;
using System.Text;

namespace Glitch.Services
{
    internal static class GlitchAiJournalBridge
    {
        private static readonly object SyncRoot = new object();

        public static string GetDecisionsJsonlPath()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("intents", "decisions.jsonl"));
        }

        public static bool TryRecord(
            string intentId,
            string rawJson,
            GlitchAiRiskDecision decision,
            DateTime recordedUtc,
            out bool isDuplicate)
        {
            isDuplicate = false;
            if (string.IsNullOrWhiteSpace(intentId) || string.IsNullOrWhiteSpace(rawJson) || decision == null)
                return false;

            lock (SyncRoot)
            {
                if (GlitchAiIntentJournalWriter.HasIntentId(intentId))
                {
                    isDuplicate = true;
                    return false;
                }

                string path = GetDecisionsJsonlPath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string status = decision.IsApproved ? "approved" : "rejected";
                string line = "{"
                    + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.intent.decision.v1") + ","
                    + "\"recorded_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(recordedUtc)) + ","
                    + "\"status\":" + GlitchSnapshotJson.String(status) + ","
                    + "\"intent_id\":" + GlitchSnapshotJson.String(intentId) + ","
                    + "\"failed_check_number\":" + decision.FailedCheckNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                    + "\"failed_check_code\":" + GlitchSnapshotJson.String(decision.FailedCheckCode ?? string.Empty) + ","
                    + "\"failed_check_message\":" + GlitchSnapshotJson.String(decision.FailedCheckMessage ?? string.Empty) + ","
                    + "\"check_trail\":" + BuildTrailJson(decision.CheckTrail) + ","
                    + "\"intent\":" + rawJson.Trim()
                    + "}";

                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                GlitchAiIntentJournalWriter.RegisterIntentId(intentId);

                if (decision.IsApproved)
                    GlitchAiIntentJournalWriter.AppendAcceptedMirror(intentId, rawJson, recordedUtc);

                return true;
            }
        }

        private static string BuildTrailJson(System.Collections.Generic.IReadOnlyList<string> trail)
        {
            if (trail == null || trail.Count == 0)
                return "[]";

            var sb = new StringBuilder(128);
            sb.Append('[');
            for (int i = 0; i < trail.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(GlitchSnapshotJson.String(trail[i]));
            }

            sb.Append(']');
            return sb.ToString();
        }
    }
}
