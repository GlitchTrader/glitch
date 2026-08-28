using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Glitch.Infrastructure
{
    internal static class GlitchExecutionEvidenceWriter
    {
        private static readonly object SyncRoot = new object();

        public static string GetPath()
        {
            return Path.Combine(
                NinjaTrader.Core.Globals.UserDataDir,
                "GlitchData",
                "intents",
                "executions.jsonl");
        }

        public static void TryAppend(
            string intentId,
            string status,
            string code,
            string message,
            DateTime recordedUtc)
        {
            if (string.IsNullOrWhiteSpace(intentId))
                return;
            try
            {
                lock (SyncRoot)
                {
                    string path = GetPath();
                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);
                    string line = "{"
                        + "\"schema_version\":" + JsonString("glitch.intent.execution.v1") + ","
                        + "\"recorded_utc\":" + JsonString(recordedUtc.ToUniversalTime().ToString("O")) + ","
                        + "\"intent_id\":" + JsonString(intentId) + ","
                        + "\"status\":" + JsonString(status) + ","
                        + "\"code\":" + JsonString(code) + ","
                        + "\"message\":" + JsonString(message)
                        + "}";
                    File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
                // Evidence persistence must never interfere with native execution.
            }
        }

        public static void TryRequestEntryRangeReassessment(
            string intentId,
            string action,
            string instrument,
            decimal entryRangeLow,
            decimal entryRangeHigh,
            decimal executablePrice,
            DateTime recordedUtc)
        {
            string temporary = null;
            try
            {
                string glitchData = Path.GetDirectoryName(Path.GetDirectoryName(GetPath()));
                string path = Path.Combine(
                    glitchData,
                    "hermes",
                    "exchange",
                    "hermes",
                    "direct-cycle-request.json");
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                string supersessionDirection =
                    string.Equals(action, "ENTER_LONG", StringComparison.Ordinal)
                        ? (executablePrice < entryRangeLow ? "better_price" : "targetward")
                        : (executablePrice > entryRangeHigh ? "better_price" : "targetward");
                string json = "{"
                    + "\"schema_version\":" + JsonString("glitch.hermes.direct_cycle_request.v1") + ","
                    + "\"requested_utc\":" + JsonString(recordedUtc.ToUniversalTime().ToString("O")) + ","
                    + "\"kind\":" + JsonString("entry_range_supersession") + ","
                    + "\"suppress_supersession_followup\":true,"
                    + "\"reassessment_context\":{"
                    + "\"source_intent_id\":" + JsonString(intentId) + ","
                    + "\"original_action\":" + JsonString(action) + ","
                    + "\"instrument\":" + JsonString(instrument) + ","
                    + "\"entry_range_low\":" + entryRangeLow.ToString(CultureInfo.InvariantCulture) + ","
                    + "\"entry_range_high\":" + entryRangeHigh.ToString(CultureInfo.InvariantCulture) + ","
                    + "\"latest_price\":" + executablePrice.ToString(CultureInfo.InvariantCulture) + ","
                    + "\"supersession_direction\":" + JsonString(supersessionDirection)
                    + "}}";
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
                temporary = null;
            }
            catch
            {
                // Reassessment signaling is best-effort; range expiry still prevents mutation.
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(temporary) && File.Exists(temporary))
                {
                    try { File.Delete(temporary); } catch { }
                }
            }
        }

        private static string JsonString(string value)
        {
            var escaped = new StringBuilder("\"");
            foreach (char character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '\\': escaped.Append("\\\\"); break;
                    case '"': escaped.Append("\\\""); break;
                    case '\r': escaped.Append("\\r"); break;
                    case '\n': escaped.Append("\\n"); break;
                    case '\t': escaped.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                            escaped.Append("\\u").Append(((int)character).ToString("x4"));
                        else
                            escaped.Append(character);
                        break;
                }
            }
            return escaped.Append('"').ToString();
        }
    }
}
