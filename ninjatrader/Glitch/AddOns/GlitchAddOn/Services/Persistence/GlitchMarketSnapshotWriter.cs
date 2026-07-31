using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Glitch.UI;
using NinjaTrader.NinjaScript.Indicators;

namespace Glitch.Services
{
    internal static class GlitchMarketSnapshotWriter
    {
        private static string _lastSnapshotHash;

        public static string SchemaVersion => GlitchMarketSnapshotRawJson.SchemaVersion;

        public static bool TryWriteLatest(DateTime nowUtc, string snapshotId = null)
        {
            try
            {
                GlitchAnalyticsFeedBus.EnsurePersistenceLoaded();

                string json = BuildSnapshotJson(nowUtc, snapshotId);
                if (string.IsNullOrWhiteSpace(json))
                    return false;
                return TryWriteCapturedSnapshot(nowUtc, json);
            }
            catch
            {
                return false;
            }
        }

        public static string GetLatestSnapshotPath()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("snapshots", "market", "latest.json"));
        }

        public static string GetRecentSnapshotPath(string snapshotHash)
        {
            int parsedHash;
            if (string.IsNullOrWhiteSpace(snapshotHash)
                || !int.TryParse(snapshotHash, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedHash))
                return null;
            return GlitchStateStore.GetDefaultPath(Path.Combine(
                "snapshots",
                "market",
                "recent",
                parsedHash.ToString(CultureInfo.InvariantCulture) + ".json"));
        }

        private static void WriteRecentSnapshot(string snapshotHash, string json)
        {
            string recentPath = GetRecentSnapshotPath(snapshotHash);
            if (string.IsNullOrWhiteSpace(recentPath))
                return;

            string recentDirectory = Path.GetDirectoryName(recentPath);
            if (!Directory.Exists(recentDirectory))
                Directory.CreateDirectory(recentDirectory);
            WriteAtomic(recentPath, json);

            FileInfo[] recent = new DirectoryInfo(recentDirectory)
                .GetFiles("*.json")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();
            for (int i = 5; i < recent.Length; i++)
            {
                try { recent[i].Delete(); }
                catch { }
            }
        }

        // The AddOn dispatcher captures the synchronized analytics-bus view into
        // this immutable string. Background publication must never query the bus.
        public static bool TryCaptureSnapshotJson(DateTime nowUtc, string snapshotId, out string json)
        {
            return TryCaptureSnapshotJson(nowUtc, snapshotId, null, out json);
        }

        public static bool TryCaptureSnapshotJson(
            DateTime nowUtc,
            string snapshotId,
            GlitchFundamentalAnalysisSnapshot fundamentals,
            out string json)
        {
            json = null;
            try
            {
                IReadOnlyList<GlitchIndicatorInstrumentSnapshot> snapshots =
                    GlitchAnalyticsFeedBus.CaptureSnapshotsForPublication();
                json = BuildSnapshotJson(nowUtc, snapshotId, snapshots, fundamentals);
                return !string.IsNullOrWhiteSpace(json);
            }
            catch
            {
                json = null;
                return false;
            }
        }

        public static bool TryWriteCapturedSnapshot(DateTime nowUtc, string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                    return false;
                string hash = GlitchSnapshotJson.ComputeStableHash(json);
                json = GlitchMarketSnapshotJson.InjectSnapshotHash(json, hash);
                if (string.Equals(hash, _lastSnapshotHash, StringComparison.Ordinal))
                    return true;

                string path = GetLatestSnapshotPath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                WriteAtomic(path, json);
                WriteRecentSnapshot(hash, json);
                GlitchHistoricalSnapshotExporter.TryArchiveMarketSnapshot(json, nowUtc);
                _lastSnapshotHash = hash;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteAtomic(string path, string json)
        {
            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(path))
                    File.Replace(tempPath, path, null);
                else
                    File.Move(tempPath, path);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        public static string TryGetLatestSnapshotHash()
        {
            string path = GetLatestSnapshotPath();
            if (!File.Exists(path))
                return null;

            try
            {
                string json = File.ReadAllText(path);
                string hash = GlitchAiJsonFields.ExtractString(json, "snapshot_hash");
                return string.IsNullOrWhiteSpace(hash) ? GlitchSnapshotJson.ComputeStableHash(json) : hash;
            }
            catch
            {
                return null;
            }
        }

        private static string BuildSnapshotJson(DateTime nowUtc, string snapshotId)
        {
            IReadOnlyList<string> roots = GlitchAnalyticsFeedBus.GetKnownInstrumentRoots();
            if (roots == null || roots.Count == 0)
                return null;

            var snapshots = new List<GlitchIndicatorInstrumentSnapshot>();

            for (int i = 0; i < roots.Count; i++)
            {
                string root = roots[i];
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                GlitchIndicatorInstrumentSnapshot snapshot;
                if (!GlitchAnalyticsFeedBus.TryGetSnapshot(root, out snapshot) || snapshot == null)
                    continue;

                snapshots.Add(snapshot);
            }
            return BuildSnapshotJson(nowUtc, snapshotId, snapshots);
        }

        private static string BuildSnapshotJson(
            DateTime nowUtc,
            string snapshotId,
            IEnumerable<GlitchIndicatorInstrumentSnapshot> snapshots,
            GlitchFundamentalAnalysisSnapshot fundamentals = null)
        {
            var instruments = new List<GlitchMarketSnapshotRawJson.RawInstrumentPayload>();
            foreach (GlitchIndicatorInstrumentSnapshot snapshot in snapshots ?? Enumerable.Empty<GlitchIndicatorInstrumentSnapshot>())
            {
                if (snapshot == null)
                    continue;

                instruments.Add(ToRawInstrumentPayload(snapshot, nowUtc));
            }

            if (instruments.Count == 0)
                return null;

            if (string.IsNullOrWhiteSpace(snapshotId))
                snapshotId = nowUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

            string json = GlitchMarketSnapshotRawJson.BuildSnapshotJson(
                "live",
                nowUtc,
                snapshotId,
                instruments);
            return InjectFundamentalContext(json, fundamentals, nowUtc);
        }

        private static string InjectFundamentalContext(
            string json,
            GlitchFundamentalAnalysisSnapshot snapshot,
            DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(json) || snapshot == null || json[json.Length - 1] != '}')
                return json;

            string context = "{"
                + "\"recorded_utc\":" + JsonString(GlitchSnapshotJson.FormatUtc(nowUtc)) + ","
                + "\"mag7_influence_score\":" + JsonNumber(snapshot.Mag7InfluenceScore) + ","
                + "\"mag7_score_lines\":" + JsonStringArray(BoundedLines(snapshot.Mag7ScoreLines, 7, 240)) + ","
                + "\"news_sentiment\":" + JsonString(BoundedText(snapshot.NewsSentiment, 600)) + ","
                + "\"is_news_lockout_active\":" + (snapshot.IsNewsLockoutActive ? "true" : "false") + ","
                + "\"news_lockout_text\":" + JsonString(BoundedText(snapshot.NewsLockoutText, 300)) + ","
                + "\"latest_headline_lines\":" + JsonStringArray(BoundedLines(snapshot.LatestHeadlineLines, 5, 300)) + ","
                + "\"official_news_lines\":" + JsonStringArray(BoundedLines(snapshot.OfficialNewsLines, 5, 300))
                + "}";
            return json.Substring(0, json.Length - 1)
                + ",\"fundamental_context\":" + context + "}";
        }

        private static string JsonString(string value)
        {
            return GlitchSnapshotJson.String(value);
        }

        private static string JsonNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "null";
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string JsonStringArray(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
                return "[]";
            return "[" + string.Join(",", values.Select(JsonString)) + "]";
        }

        private static List<string> BoundedLines(
            IReadOnlyList<string> values,
            int maximumCount,
            int maximumLength)
        {
            var bounded = new List<string>();
            if (values == null)
                return bounded;

            for (int i = 0; i < values.Count && bounded.Count < maximumCount; i++)
            {
                string value = BoundedText(values[i], maximumLength);
                if (!string.IsNullOrWhiteSpace(value))
                    bounded.Add(value);
            }
            return bounded;
        }

        private static string BoundedText(string value, int maximumLength)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length <= maximumLength)
                return normalized;
            return normalized.Substring(0, maximumLength);
        }

        private static GlitchMarketSnapshotRawJson.RawInstrumentPayload ToRawInstrumentPayload(
            GlitchIndicatorInstrumentSnapshot snapshot,
            DateTime nowUtc)
        {
            var bars = new List<GlitchMarketSnapshotRawJson.RawTimeframeBarPayload>();
            if (snapshot.TimeframeReadings != null)
            {
                foreach (KeyValuePair<int, GlitchIndicatorReading> entry in snapshot.TimeframeReadings.OrderBy(x => x.Key))
                {
                    GlitchIndicatorReading reading = entry.Value;
                    if (reading == null)
                        continue;

                    bars.Add(ToRawTimeframeBar(reading));
                }
            }

            return new GlitchMarketSnapshotRawJson.RawInstrumentPayload
            {
                InstrumentRoot = snapshot.InstrumentRoot,
                InstrumentFullName = snapshot.InstrumentFullName,
                UpdatedUtc = snapshot.UpdatedUtc,
                IsFresh = GlitchAnalyticsFeedBus.IsSnapshotFresh(snapshot, nowUtc, TimeSpan.FromMinutes(5)),
                CurrentPrice = snapshot.CurrentPrice,
                SessionName = snapshot.SessionName,
                SessionHigh = snapshot.SessionHigh,
                SessionLow = snapshot.SessionLow,
                PreviousSessionHigh = snapshot.PreviousSessionHigh,
                PreviousSessionLow = snapshot.PreviousSessionLow,
                TimeframeBars = bars
            };
        }

        private static GlitchMarketSnapshotRawJson.RawTimeframeBarPayload ToRawTimeframeBar(GlitchIndicatorReading reading)
        {
            return new GlitchMarketSnapshotRawJson.RawTimeframeBarPayload
            {
                Minutes = reading.Minutes,
                UtcTime = reading.UtcTime,
                Open = reading.Open,
                High = reading.High,
                Low = reading.Low,
                Close = reading.CurrentPrice,
                Volume = reading.Volume,
                Indicators = new GlitchMarketSnapshotRawJson.RawIndicatorsPayload
                {
                    Atr = reading.Atr,
                    Adx = reading.Adx,
                    Rsi = reading.Rsi,
                    StochK = reading.StochK,
                    ZScore = reading.ZScore,
                    AveragePrice = reading.AveragePrice,
                    DiPlus = reading.DiPlus,
                    DiMinus = reading.DiMinus,
                    Cci = reading.Cci,
                    MacdHistogram = reading.MacdHistogram,
                    OrderFlowCumulativeDelta = reading.OrderFlowCumulativeDelta,
                    OrderFlowDeltaChange = reading.OrderFlowDeltaChange,
                    OrderFlowVwap = reading.OrderFlowVwap,
                    OrderFlowVwapDeviation = reading.OrderFlowVwapDeviation
                },
                DerivedAnalytics = new GlitchMarketSnapshotRawJson.DerivedAnalyticsPayload
                {
                    RawScore = reading.RawScore,
                    DirectionalScore = reading.DirectionalScore,
                    TradeabilityScore = reading.TradeabilityScore,
                    EmaAlignment = reading.EmaAlignment,
                    RegimeWeight = reading.RegimeWeight,
                    OscillatorCompositeScore = reading.OscillatorCompositeScore,
                    MaCompositeScore = reading.MaCompositeScore,
                    OrderFlowScore = reading.OrderFlowScore,
                    OrderFlowConfidence = reading.OrderFlowConfidence,
                    OrderFlowReliability = reading.OrderFlowReliability
                }
            };
        }
    }
}
