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

        public static bool TryRecordAccepted(
            string intentId,
            string rawJson,
            DateTime recordedUtc)
        {
            if (string.IsNullOrWhiteSpace(intentId) || string.IsNullOrWhiteSpace(rawJson))
                return false;

            lock (SyncRoot)
            {
                string path = GetDecisionsJsonlPath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string intentToken = "\"intent_id\":" + GlitchSnapshotJson.String(intentId);
                if (File.Exists(path))
                {
                    foreach (string existingLine in File.ReadLines(path))
                    {
                        if (!string.IsNullOrWhiteSpace(existingLine)
                            && existingLine.IndexOf(intentToken, StringComparison.Ordinal) >= 0)
                            return true;
                    }
                }

                string line = "{"
                    + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.intent.accepted.v1") + ","
                    + "\"recorded_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(recordedUtc)) + ","
                    + "\"status\":" + GlitchSnapshotJson.String("accepted") + ","
                    + "\"intent_id\":" + GlitchSnapshotJson.String(intentId) + ","
                    + "\"accepted_facts\":[\"contract_valid\",\"intent_identity_claimed\"],"
                    + "\"intent\":" + rawJson.Trim()
                    + "}";

                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                GlitchAiIntentJournalWriter.AppendAcceptedMirror(intentId, rawJson, recordedUtc);

                return true;
            }
        }
    }
}
