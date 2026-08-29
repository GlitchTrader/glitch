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
            string tempPath = null;
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
                    if (!HasNonWhitespaceContent(entry.MarketPath)
                        || !HasNonWhitespaceContent(entry.PortfolioPath))
                    {
                        continue;
                    }

                    pairs.Add(new ReplayPair
                    {
                        SnapshotId = entry.SnapshotId,
                        CreatedUtc = entry.CreatedUtc,
                        MarketPath = entry.MarketPath,
                        PortfolioPath = entry.PortfolioPath
                    });
                }

                if (pairs.Count == 0)
                    return false;

                pairs.Sort((a, b) => a.CreatedUtc.CompareTo(b.CreatedUtc));
                if (pairs.Count > maxPairs)
                    pairs = pairs.GetRange(pairs.Count - maxPairs, maxPairs);

                string replayPath = GetReplayLatestPath();
                string directory = Path.GetDirectoryName(replayPath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                tempPath = replayPath + ".tmp";
                using (var writer = new StreamWriter(tempPath, false, new UTF8Encoding(false)))
                    WriteReplayJson(writer, nowUtc, sinceUtc, pairs);
                if (File.Exists(replayPath))
                    File.Delete(replayPath);
                File.Move(tempPath, replayPath);
                return true;
            }
            catch
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }
                return false;
            }
        }

        public static bool TryWriteReplayBundleIfDue(DateTime nowUtc, TimeSpan interval, TimeSpan lookback)
        {
            if (_lastReplayAttemptUtc != DateTime.MinValue && (nowUtc - _lastReplayAttemptUtc) < interval)
                return false;
            _lastReplayAttemptUtc = nowUtc;
            bool wrote = TryWriteReplayBundle(nowUtc - lookback, nowUtc);
            if (wrote)
                _lastReplayWriteUtc = nowUtc;
            return wrote;
        }

        private static DateTime _lastReplayWriteUtc = DateTime.MinValue;
        private static DateTime _lastReplayAttemptUtc = DateTime.MinValue;
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

        private static bool HasNonWhitespaceContent(string path)
        {
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                int value;
                while ((value = reader.Read()) >= 0)
                {
                    if (!char.IsWhiteSpace((char)value))
                        return true;
                }
            }

            return false;
        }

        private static void WriteReplayJson(
            TextWriter writer,
            DateTime nowUtc,
            DateTime sinceUtc,
            List<ReplayPair> pairs)
        {
            string snapshotId = nowUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            writer.Write('{');
            writer.Write("\"schema_version\":");
            writer.Write(GlitchSnapshotJson.String(ReplaySchemaVersion));
            writer.Write(",\"created_utc\":");
            writer.Write(GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(nowUtc)));
            writer.Write(",\"snapshot_id\":");
            writer.Write(GlitchSnapshotJson.String(snapshotId));
            writer.Write(",\"source_mode\":\"historical_replay\",");
            writer.Write("\"range_start_utc\":");
            writer.Write(GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(sinceUtc)));
            writer.Write(",\"range_end_utc\":");
            writer.Write(GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(nowUtc)));
            writer.Write(",\"pair_count\":");
            writer.Write(pairs.Count.ToString(CultureInfo.InvariantCulture));
            writer.Write(",\"pairs\":[");
            for (int i = 0; i < pairs.Count; i++)
            {
                if (i > 0)
                    writer.Write(',');
                ReplayPair pair = pairs[i];
                writer.Write('{');
                writer.Write("\"snapshot_id\":");
                writer.Write(GlitchSnapshotJson.String(pair.SnapshotId));
                writer.Write(",\"created_utc\":");
                writer.Write(GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(pair.CreatedUtc)));
                writer.Write('}');
            }
            writer.Write("],\"market_snapshots\":");
            WriteReplaySnapshotArray(writer, pairs, useMarketPath: true);
            writer.Write(",\"portfolio_snapshots\":");
            WriteReplaySnapshotArray(writer, pairs, useMarketPath: false);
            writer.Write('}');
        }

        private static void WriteReplaySnapshotArray(
            TextWriter writer,
            List<ReplayPair> pairs,
            bool useMarketPath)
        {
            writer.Write('[');
            for (int i = 0; i < pairs.Count; i++)
            {
                string path = useMarketPath ? pairs[i].MarketPath : pairs[i].PortfolioPath;
                string json = RewriteSourceMode(File.ReadAllText(path), "historical_replay");
                if (string.IsNullOrWhiteSpace(json))
                    throw new InvalidDataException("Historical replay snapshot is empty: " + path);
                if (i > 0)
                    writer.Write(',');
                writer.Write(json);
            }
            writer.Write(']');
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
            public string MarketPath { get; set; }
            public string PortfolioPath { get; set; }
        }
    }
}
