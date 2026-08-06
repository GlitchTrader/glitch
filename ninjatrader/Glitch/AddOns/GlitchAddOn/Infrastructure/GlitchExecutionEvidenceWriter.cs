using System;
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
