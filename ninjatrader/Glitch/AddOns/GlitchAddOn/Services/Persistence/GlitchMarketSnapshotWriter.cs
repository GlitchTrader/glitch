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
            json = null;
            try
            {
                IReadOnlyList<GlitchIndicatorInstrumentSnapshot> snapshots =
                    GlitchAnalyticsFeedBus.CaptureSnapshotsForPublication();
                json = BuildSnapshotJson(nowUtc, snapshotId, snapshots);
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
            IEnumerable<GlitchIndicatorInstrumentSnapshot> snapshots)
        {
            var instruments = new List<GlitchMarketSnapshotRawJson.RawInstrumentPayload>();
            foreach (GlitchIndicatorInstrumentSnapshot snapshot in snapshots ?? Enumerable.Empty<GlitchIndicatorInstrumentSnapshot>())
            {
                if (snapshot == null)
                    continue;

                instruments.Add(ToRawInstrumentPayload(snapshot));
            }

            if (instruments.Count == 0)
                return null;

            if (string.IsNullOrWhiteSpace(snapshotId))
                snapshotId = nowUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

            return GlitchMarketSnapshotRawJson.BuildSnapshotJson("live", nowUtc, snapshotId, instruments);
        }

        private static GlitchMarketSnapshotRawJson.RawInstrumentPayload ToRawInstrumentPayload(
            GlitchIndicatorInstrumentSnapshot snapshot)
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
