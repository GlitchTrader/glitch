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
        private static readonly TimeSpan WriteThrottle = TimeSpan.FromMinutes(1);
        private static DateTime _lastWriteUtc = DateTime.MinValue;
        private static string _lastSnapshotHash;

        public static string SchemaVersion => GlitchMarketSnapshotRawJson.SchemaVersion;

        public static bool TryWriteLatestIfDue(DateTime nowUtc, string snapshotId = null)
        {
            if (_lastWriteUtc != DateTime.MinValue && (nowUtc - _lastWriteUtc) < WriteThrottle)
                return false;

            return TryWriteLatest(nowUtc, snapshotId);
        }

        public static bool TryWriteLatest(DateTime nowUtc, string snapshotId = null)
        {
            try
            {
                GlitchAnalyticsFeedBus.EnsurePersistenceLoaded();

                string json = BuildSnapshotJson(nowUtc, snapshotId);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                string hash = GlitchSnapshotJson.ComputeStableHash(json);
                json = GlitchMarketSnapshotJson.InjectSnapshotHash(json, hash);
                if (string.Equals(hash, _lastSnapshotHash, StringComparison.Ordinal))
                {
                    _lastWriteUtc = nowUtc;
                    return true;
                }

                string path = GetLatestSnapshotPath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tempPath, path);

                GlitchHistoricalSnapshotExporter.TryArchiveMarketSnapshot(json, nowUtc);

                _lastSnapshotHash = hash;
                _lastWriteUtc = nowUtc;
                return true;
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

            var instruments = new List<GlitchMarketSnapshotRawJson.RawInstrumentPayload>();

            for (int i = 0; i < roots.Count; i++)
            {
                string root = roots[i];
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                GlitchIndicatorInstrumentSnapshot snapshot;
                if (!GlitchAnalyticsFeedBus.TryGetSnapshot(root, out snapshot) || snapshot == null)
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
                UpdatedUtc = snapshot.UpdatedUtc,
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
                    OrderFlowCumulativeDelta = reading.OrderFlowCumulativeDelta,
                    OrderFlowDeltaChange = reading.OrderFlowDeltaChange,
                    OrderFlowVwap = reading.OrderFlowVwap,
                    OrderFlowVwapDeviation = reading.OrderFlowVwapDeviation
                }
            };
        }
    }
}
