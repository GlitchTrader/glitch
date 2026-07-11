using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Glitch.Services
{
    internal static class GlitchAiExecutionJournalWriter
    {
        private static readonly object SyncRoot = new object();

        public static string GetExecutionsJsonlPath()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("intents", "executions.jsonl"));
        }

        public static void TryAppend(string intentId, GlitchAiExecutionResult result, DateTime recordedUtc)
        {
            if (string.IsNullOrWhiteSpace(intentId) || result == null)
                return;

            lock (SyncRoot)
            {
                string path = GetExecutionsJsonlPath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string line = "{"
                    + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.intent.execution.v1") + ","
                    + "\"recorded_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(recordedUtc)) + ","
                    + "\"intent_id\":" + GlitchSnapshotJson.String(intentId) + ","
                    + "\"status\":" + GlitchSnapshotJson.String(result.Status ?? string.Empty) + ","
                    + "\"code\":" + GlitchSnapshotJson.String(result.Code ?? string.Empty) + ","
                    + "\"message\":" + GlitchSnapshotJson.String(result.Message ?? string.Empty)
                    + "}";

                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }
    }
}
