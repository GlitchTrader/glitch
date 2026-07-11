using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Glitch.Services
{
    internal static class GlitchAiIntentJournalWriter
    {
        private static readonly object SyncRoot = new object();
        private static HashSet<string> _knownIntentIds;
        private static bool _idsLoaded;

        public static string GetReceivedJsonlPath()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("intents", "received.jsonl"));
        }

        public static int CountReceived()
        {
            lock (SyncRoot)
            {
                EnsureIdsLoaded();
                return _knownIntentIds == null ? 0 : _knownIntentIds.Count;
            }
        }

        public static bool HasIntentId(string intentId)
        {
            if (string.IsNullOrWhiteSpace(intentId))
                return false;

            lock (SyncRoot)
            {
                EnsureIdsLoaded();
                return _knownIntentIds.Contains(intentId);
            }
        }

        public static void RegisterIntentId(string intentId)
        {
            if (string.IsNullOrWhiteSpace(intentId))
                return;

            lock (SyncRoot)
            {
                EnsureIdsLoaded();
                if (_knownIntentIds.Contains(intentId))
                    return;

                _knownIntentIds.Add(intentId);
                AppendIntentId(intentId);
            }
        }

        public static void AppendAcceptedMirror(string intentId, string rawJson, DateTime receivedUtc)
        {
            if (string.IsNullOrWhiteSpace(intentId) || string.IsNullOrWhiteSpace(rawJson))
                return;

            string path = GetReceivedJsonlPath();
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string line = "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.intent.journal.v1") + ","
                + "\"received_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(receivedUtc)) + ","
                + "\"status\":" + GlitchSnapshotJson.String("accepted") + ","
                + "\"mode\":" + GlitchSnapshotJson.String("paper") + ","
                + "\"intent_id\":" + GlitchSnapshotJson.String(intentId) + ","
                + "\"intent\":" + rawJson.Trim()
                + "}";

            File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
        }

        private static void EnsureIdsLoaded()
        {
            if (_idsLoaded)
                return;

            _knownIntentIds = new HashSet<string>(StringComparer.Ordinal);
            string idsPath = GlitchStateStore.GetDefaultPath(Path.Combine("intents", "intent_ids.txt"));
            if (File.Exists(idsPath))
            {
                foreach (string line in File.ReadAllLines(idsPath))
                {
                    string id = line == null ? null : line.Trim();
                    if (!string.IsNullOrWhiteSpace(id))
                        _knownIntentIds.Add(id);
                }
            }

            _idsLoaded = true;
        }

        private static void AppendIntentId(string intentId)
        {
            string idsPath = GlitchStateStore.GetDefaultPath(Path.Combine("intents", "intent_ids.txt"));
            string directory = Path.GetDirectoryName(idsPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.AppendAllText(idsPath, intentId + Environment.NewLine, new UTF8Encoding(false));
        }
    }
}
