using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Glitch.Services
{
    internal static class GlitchAiJournalBridge
    {
        private static readonly object SyncRoot = new object();
        private static readonly HashSet<string> AcceptedIntentIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _acceptedIntentIndexLoaded;

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

                EnsureAcceptedIntentIndexLoaded(path);
                if (AcceptedIntentIds.Contains(intentId))
                    return true;

                string line = "{"
                    + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.intent.accepted.v1") + ","
                    + "\"recorded_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(recordedUtc)) + ","
                    + "\"status\":" + GlitchSnapshotJson.String("accepted") + ","
                    + "\"intent_id\":" + GlitchSnapshotJson.String(intentId) + ","
                    + "\"accepted_facts\":[\"contract_valid\",\"intent_identity_claimed\"],"
                    + "\"intent\":" + rawJson.Trim()
                    + "}";

                using (var stream = new FileStream(
                    path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    writer.WriteLine(line);
                AcceptedIntentIds.Add(intentId);
                GlitchAiIntentJournalWriter.AppendAcceptedMirror(intentId, rawJson, recordedUtc);

                return true;
            }
        }

        private static void EnsureAcceptedIntentIndexLoaded(string path)
        {
            if (_acceptedIntentIndexLoaded)
                return;
            if (File.Exists(path))
            {
                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string existingIntentId = GlitchAiJsonFields.ExtractString(line, "intent_id");
                        if (!string.IsNullOrWhiteSpace(existingIntentId))
                            AcceptedIntentIds.Add(existingIntentId);
                    }
                }
            }
            _acceptedIntentIndexLoaded = true;
        }
    }
}
