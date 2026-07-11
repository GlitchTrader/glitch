using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Glitch.Services
{
    internal static class GlitchAiReplayHarnessWriter
    {
        public const string SchemaVersion = "glitch.replay.harness.v1";
        private static readonly TimeSpan WriteThrottle = TimeSpan.FromMinutes(15);
        private static DateTime _lastWriteUtc = DateTime.MinValue;

        public static string GetLatestPath()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("replay", "harness", "latest.json"));
        }

        public static bool TryWriteIfDue(DateTime nowUtc)
        {
            if (_lastWriteUtc != DateTime.MinValue && (nowUtc - _lastWriteUtc) < WriteThrottle)
                return false;

            return TryWrite(nowUtc);
        }

        public static bool TryWrite(DateTime nowUtc)
        {
            try
            {
                string json = BuildJson(nowUtc);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                string path = GetLatestPath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tempPath, path);

                _lastWriteUtc = nowUtc;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildJson(DateTime nowUtc)
        {
            int indexPairs = CountIndexPairs();
            int replayPairs = ReadReplayPairCount();
            var labels = TallyMnqSignalLabels(40);

            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"schema_version\":").Append(GlitchSnapshotJson.String(SchemaVersion)).Append(',');
            sb.Append("\"created_utc\":").Append(GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(nowUtc))).Append(',');
            sb.Append("\"historical_index_pair_count\":").Append(indexPairs.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"replay_bundle_pair_count\":").Append(replayPairs.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"mnq_5m_signal_tally\":").Append(WriteTally(labels)).Append(',');
            sb.Append("\"status\":").Append(GlitchSnapshotJson.String(indexPairs > 0 ? "ready" : "waiting_for_history"));
            sb.Append('}');
            return sb.ToString();
        }

        private static int CountIndexPairs()
        {
            string indexPath = Path.Combine(GlitchHistoricalSnapshotExporter.GetHistoricalRootPath(), "index.jsonl");
            if (!File.Exists(indexPath))
                return 0;

            return File.ReadLines(indexPath).Count(line => !string.IsNullOrWhiteSpace(line));
        }

        private static int ReadReplayPairCount()
        {
            string replayPath = GlitchHistoricalSnapshotExporter.GetReplayLatestPath();
            if (!File.Exists(replayPath))
                return 0;

            try
            {
                string head = ReadHead(replayPath, 1024);
                double count;
                return GlitchAiJsonFields.TryExtractNumber(head, "pair_count", out count) ? (int)count : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static Dictionary<string, int> TallyMnqSignalLabels(int maxFiles)
        {
            var tally = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string marketDir = Path.Combine(GlitchHistoricalSnapshotExporter.GetHistoricalRootPath(), "market");
            if (!Directory.Exists(marketDir))
                return tally;

            string[] files = Directory.GetFiles(marketDir, "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            int start = Math.Max(0, files.Length - maxFiles);
            for (int i = start; i < files.Length; i++)
            {
                string label = ExtractMnq5mSignalLabel(files[i]);
                if (string.IsNullOrWhiteSpace(label))
                    continue;

                int count;
                tally[label] = tally.TryGetValue(label, out count) ? count + 1 : 1;
            }

            return tally;
        }

        private static string ExtractMnq5mSignalLabel(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                string marker = "\"instrument\":" + GlitchSnapshotJson.String("MNQ");
                int start = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    return null;

                string slice = json.Substring(start, Math.Min(12000, json.Length - start));
                Match match = Regex.Match(
                    slice,
                    "\"minutes\"\\s*:\\s*5[\\s\\S]{0,800}?\"signal_label\"\\s*:\\s*\"([^\"]+)\"",
                    RegexOptions.CultureInvariant);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        private static string WriteTally(Dictionary<string, int> tally)
        {
            if (tally == null || tally.Count == 0)
                return "{}";

            var parts = new List<string>();
            foreach (KeyValuePair<string, int> pair in tally.OrderByDescending(p => p.Value))
            {
                parts.Add(GlitchSnapshotJson.String(pair.Key) + ":" + pair.Value.ToString(CultureInfo.InvariantCulture));
            }

            return "{" + string.Join(",", parts) + "}";
        }

        private static string ReadHead(string path, int maxChars)
        {
            using (var reader = new StreamReader(path))
            {
                char[] buffer = new char[maxChars];
                int read = reader.ReadBlock(buffer, 0, buffer.Length);
                return new string(buffer, 0, read);
            }
        }
    }
}
