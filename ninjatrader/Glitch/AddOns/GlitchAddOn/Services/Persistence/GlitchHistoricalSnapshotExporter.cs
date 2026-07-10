using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Glitch.Services
{
    internal static class GlitchHistoricalSnapshotExporter
    {
        public const string ReplaySchemaVersion = "glitch.historical.replay.v1";
        private const string IndexFileName = "index.jsonl";
        private static readonly Regex SnapshotIdRegex = new Regex(
            "\"snapshot_id\"\\s*:\\s*\"([^\"]+)\"",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool TryArchiveMarketSnapshot(string snapshotJson, DateTime nowUtc)
        {
            return TryArchiveSnapshot(
                snapshotJson,
                nowUtc,
                "market",
                GlitchMarketSnapshotWriter.SchemaVersion);
        }

        public static bool TryArchivePortfolioSnapshot(string snapshotJson, DateTime nowUtc)
        {
            return TryArchiveSnapshot(
                snapshotJson,
                nowUtc,
                "portfolio",
                GlitchPortfolioSnapshotWriter.SchemaVersion);
        }

        public static string GetHistoricalRootPath()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("snapshots", "historical"));
        }

        public static string GetReplayLatestPath()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("snapshots", "historical", "replay", "latest.json"));
        }

        public static bool TryWriteReplayBundle(DateTime sinceUtc, DateTime nowUtc, int maxPairs = 1440)
        {
            try
            {
                string indexPath = Path.Combine(GetHistoricalRootPath(), IndexFileName);
                if (!File.Exists(indexPath))
                    return false;

                var pairs = new List<ReplayPair>();
                foreach (string line in File.ReadLines(indexPath))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    ReplayIndexEntry entry = ParseIndexEntry(line);
                    if (entry == null || string.IsNullOrWhiteSpace(entry.SnapshotId))
                        continue;

                    if (entry.CreatedUtc < sinceUtc || entry.CreatedUtc > nowUtc)
                        continue;

                    if (string.IsNullOrWhiteSpace(entry.MarketPath) || string.IsNullOrWhiteSpace(entry.PortfolioPath))
                        continue;

                    if (!File.Exists(entry.MarketPath) || !File.Exists(entry.PortfolioPath))
                        continue;

                    string marketJson = RewriteSourceMode(File.ReadAllText(entry.MarketPath), "historical_replay");
                    string portfolioJson = RewriteSourceMode(File.ReadAllText(entry.PortfolioPath), "historical_replay");
                    if (string.IsNullOrWhiteSpace(marketJson) || string.IsNullOrWhiteSpace(portfolioJson))
                        continue;

                    pairs.Add(new ReplayPair
                    {
                        SnapshotId = entry.SnapshotId,
                        CreatedUtc = entry.CreatedUtc,
                        MarketJson = marketJson,
                        PortfolioJson = portfolioJson
                    });
                }

                if (pairs.Count == 0)
                    return false;

                pairs.Sort((a, b) => a.CreatedUtc.CompareTo(b.CreatedUtc));
                if (pairs.Count > maxPairs)
                    pairs = pairs.GetRange(pairs.Count - maxPairs, maxPairs);

                string replayJson = BuildReplayJson(nowUtc, sinceUtc, pairs);
                if (string.IsNullOrWhiteSpace(replayJson))
                    return false;

                string replayPath = GetReplayLatestPath();
                string directory = Path.GetDirectoryName(replayPath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string tempPath = replayPath + ".tmp";
                File.WriteAllText(tempPath, replayJson, new UTF8Encoding(false));
                if (File.Exists(replayPath))
                    File.Delete(replayPath);
                File.Move(tempPath, replayPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryWriteReplayBundleIfDue(DateTime nowUtc, TimeSpan interval, TimeSpan lookback)
        {
            if (_lastReplayWriteUtc != DateTime.MinValue && (nowUtc - _lastReplayWriteUtc) < interval)
                return false;

            bool wrote = TryWriteReplayBundle(nowUtc - lookback, nowUtc);
            if (wrote)
                _lastReplayWriteUtc = nowUtc;
            return wrote;
        }

        private static DateTime _lastReplayWriteUtc = DateTime.MinValue;
        private static readonly Dictionary<string, string> _lastArchivedHashByKind =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _pendingMarketArchiveBySnapshotId =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static bool TryArchiveSnapshot(string snapshotJson, DateTime nowUtc, string kind, string expectedSchema)
        {
            if (string.IsNullOrWhiteSpace(snapshotJson))
                return false;

            if (snapshotJson.IndexOf(expectedSchema, StringComparison.Ordinal) < 0)
                return false;

            string hash = GlitchSnapshotJson.ComputeStableHash(snapshotJson);
            string lastHash;
            if (_lastArchivedHashByKind.TryGetValue(kind, out lastHash) &&
                string.Equals(lastHash, hash, StringComparison.Ordinal))
            {
                return true;
            }

            string snapshotId = ExtractSnapshotId(snapshotJson);
            if (string.IsNullOrWhiteSpace(snapshotId))
                snapshotId = nowUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

            try
            {
                string archiveDir = Path.Combine(GetHistoricalRootPath(), kind);
                if (!Directory.Exists(archiveDir))
                    Directory.CreateDirectory(archiveDir);

                string archivePath = Path.Combine(archiveDir, snapshotId + ".json");
                string tempPath = archivePath + ".tmp";
                File.WriteAllText(tempPath, snapshotJson, new UTF8Encoding(false));
                if (File.Exists(archivePath))
                    File.Delete(archivePath);
                File.Move(tempPath, archivePath);

                _lastArchivedHashByKind[kind] = hash;

                if (string.Equals(kind, "market", StringComparison.OrdinalIgnoreCase))
                {
                    _pendingMarketArchiveBySnapshotId[snapshotId] = archivePath;
                }
                else if (string.Equals(kind, "portfolio", StringComparison.OrdinalIgnoreCase))
                {
                    string marketPath;
                    if (_pendingMarketArchiveBySnapshotId.TryGetValue(snapshotId, out marketPath))
                    {
                        AppendIndexEntry(snapshotId, nowUtc, marketPath, archivePath);
                        _pendingMarketArchiveBySnapshotId.Remove(snapshotId);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void AppendIndexEntry(string snapshotId, DateTime createdUtc, string marketPath, string portfolioPath)
        {
            string indexPath = Path.Combine(GetHistoricalRootPath(), IndexFileName);
            string directory = Path.GetDirectoryName(indexPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string line = "{"
                + "\"snapshot_id\":" + GlitchSnapshotJson.String(snapshotId) + ","
                + "\"created_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(createdUtc)) + ","
                + "\"market_path\":" + GlitchSnapshotJson.String(marketPath) + ","
                + "\"portfolio_path\":" + GlitchSnapshotJson.String(portfolioPath)
                + "}";

            File.AppendAllText(indexPath, line + Environment.NewLine, new UTF8Encoding(false));
        }

        private static string ExtractSnapshotId(string json)
        {
            Match match = SnapshotIdRegex.Match(json ?? string.Empty);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string RewriteSourceMode(string json, string sourceMode)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            if (json.IndexOf("\"source_mode\"", StringComparison.Ordinal) < 0)
                return json;

            return Regex.Replace(
                json,
                "\"source_mode\"\\s*:\\s*\"[^\"]*\"",
                "\"source_mode\":\"" + sourceMode + "\"",
                RegexOptions.CultureInvariant);
        }

        private static string BuildReplayJson(DateTime nowUtc, DateTime sinceUtc, List<ReplayPair> pairs)
        {
            var marketSnapshots = new List<string>(pairs.Count);
            var portfolioSnapshots = new List<string>(pairs.Count);
            var pairMeta = new List<string>(pairs.Count);

            for (int i = 0; i < pairs.Count; i++)
            {
                ReplayPair pair = pairs[i];
                marketSnapshots.Add(pair.MarketJson);
                portfolioSnapshots.Add(pair.PortfolioJson);
                pairMeta.Add("{"
                    + "\"snapshot_id\":" + GlitchSnapshotJson.String(pair.SnapshotId) + ","
                    + "\"created_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(pair.CreatedUtc))
                    + "}");
            }

            string snapshotId = nowUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var sb = new StringBuilder(Math.Max(8192, pairs.Count * 512));
            sb.Append('{');
            sb.Append("\"schema_version\":").Append(GlitchSnapshotJson.String(ReplaySchemaVersion)).Append(',');
            sb.Append("\"created_utc\":").Append(GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(nowUtc))).Append(',');
            sb.Append("\"snapshot_id\":").Append(GlitchSnapshotJson.String(snapshotId)).Append(',');
            sb.Append("\"source_mode\":\"historical_replay\",");
            sb.Append("\"range_start_utc\":").Append(GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(sinceUtc))).Append(',');
            sb.Append("\"range_end_utc\":").Append(GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(nowUtc))).Append(',');
            sb.Append("\"pair_count\":").Append(pairs.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"pairs\":[").Append(string.Join(",", pairMeta)).Append("],");
            sb.Append("\"market_snapshots\":[").Append(string.Join(",", marketSnapshots)).Append("],");
            sb.Append("\"portfolio_snapshots\":[").Append(string.Join(",", portfolioSnapshots)).Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        private static ReplayIndexEntry ParseIndexEntry(string line)
        {
            try
            {
                string snapshotId = ExtractJsonString(line, "snapshot_id");
                string createdUtcRaw = ExtractJsonString(line, "created_utc");
                string marketPath = ExtractJsonString(line, "market_path");
                string portfolioPath = ExtractJsonString(line, "portfolio_path");
                if (string.IsNullOrWhiteSpace(snapshotId))
                    return null;

                DateTime createdUtc;
                if (!DateTime.TryParse(createdUtcRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out createdUtc))
                    createdUtc = DateTime.MinValue;

                return new ReplayIndexEntry
                {
                    SnapshotId = snapshotId,
                    CreatedUtc = createdUtc,
                    MarketPath = marketPath,
                    PortfolioPath = portfolioPath
                };
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return null;

            Match match = Regex.Match(
                json,
                "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"",
                RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : null;
        }

        private sealed class ReplayIndexEntry
        {
            public string SnapshotId { get; set; }
            public DateTime CreatedUtc { get; set; }
            public string MarketPath { get; set; }
            public string PortfolioPath { get; set; }
        }

        private sealed class ReplayPair
        {
            public string SnapshotId { get; set; }
            public DateTime CreatedUtc { get; set; }
            public string MarketJson { get; set; }
            public string PortfolioJson { get; set; }
        }
    }
}
