using System;

namespace Glitch.Services
{
    internal static class GlitchAiIntentResultContract
    {
        public static string GetPhase(GlitchAiExecutionResult result)
        {
            if (string.Equals(result?.Status, "failed", StringComparison.Ordinal)) return "failed";
            return string.Equals(result?.Status, "pending", StringComparison.Ordinal) ? "pending" : "executed";
        }
        public static string BuildAcceptedJson(string intentId, GlitchAiExecutionResult result)
        {
            return "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.intent.response.v1") + ","
                + "\"status\":" + GlitchSnapshotJson.String("accepted") + ","
                + "\"intent_id\":" + GlitchSnapshotJson.String(intentId) + ","
                + "\"executor\":" + GlitchSnapshotJson.String(result?.Status ?? "none") + ","
                + "\"executor_code\":" + GlitchSnapshotJson.String(result?.Code ?? string.Empty) + ","
                + "\"created_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(DateTime.UtcNow)) + "}";
        }
    }
}
