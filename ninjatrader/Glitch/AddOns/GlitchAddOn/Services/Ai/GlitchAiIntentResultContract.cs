namespace Glitch.Services
{
    internal static class GlitchAiIntentResultContract
    {
        public static string BuildAcceptedJson(
            string intentId,
            string intentCreatedUtc,
            GlitchAiExecutionResult result)
        {
            return "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.intent.response.v1") + ","
                + "\"status\":" + GlitchSnapshotJson.String("accepted") + ","
                + "\"intent_id\":" + GlitchSnapshotJson.String(intentId) + ","
                + "\"executor\":" + GlitchSnapshotJson.String(result?.Status ?? "none") + ","
                + "\"executor_code\":" + GlitchSnapshotJson.String(result?.Code ?? string.Empty) + ","
                + "\"created_utc\":" + GlitchSnapshotJson.String(intentCreatedUtc ?? string.Empty) + "}";
        }
    }
}
