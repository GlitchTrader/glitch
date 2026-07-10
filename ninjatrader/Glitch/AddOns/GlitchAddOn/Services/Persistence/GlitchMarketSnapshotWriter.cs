using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Glitch.UI;

namespace Glitch.Services
{
    internal static class GlitchMarketSnapshotWriter
    {
        public const string SchemaVersion = "glitch.market.snapshot.v1";
        private static readonly int[] RequiredTimeframesMinutes = { 1, 5, 15, 60 };
        private static readonly TimeSpan FeedFreshnessWindow = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan WriteThrottle = TimeSpan.FromMinutes(1);
        private static DateTime _lastWriteUtc = DateTime.MinValue;
        private static string _lastSnapshotHash;

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

                string hash = ComputeStableHash(json);
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

        private static string BuildSnapshotJson(DateTime nowUtc, string snapshotId)
        {
            IReadOnlyList<string> roots = GlitchAnalyticsFeedBus.GetKnownInstrumentRoots();
            if (roots == null || roots.Count == 0)
                return null;

            var instruments = new List<string>();
            var coverageRows = new List<string>();
            int freshInstrumentCount = 0;

            for (int i = 0; i < roots.Count; i++)
            {
                string root = roots[i];
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                GlitchIndicatorInstrumentSnapshot snapshot;
                if (!GlitchAnalyticsFeedBus.TryGetSnapshot(root, out snapshot) || snapshot == null)
                    continue;

                bool isFresh = GlitchAnalyticsFeedBus.IsSnapshotFresh(snapshot, nowUtc, FeedFreshnessWindow);
                if (isFresh)
                    freshInstrumentCount++;

                var presentMinutes = new HashSet<int>();
                if (snapshot.TimeframeReadings != null)
                {
                    foreach (int minutes in snapshot.TimeframeReadings.Keys)
                        presentMinutes.Add(minutes);
                }

                var missingMinutes = new List<int>();
                for (int m = 0; m < RequiredTimeframesMinutes.Length; m++)
                {
                    int required = RequiredTimeframesMinutes[m];
                    if (!presentMinutes.Contains(required))
                        missingMinutes.Add(required);
                }

                instruments.Add(BuildInstrumentJson(snapshot, isFresh, missingMinutes));
                coverageRows.Add(BuildCoverageJson(root, isFresh, presentMinutes, missingMinutes));
            }

            if (instruments.Count == 0)
                return null;

            if (string.IsNullOrWhiteSpace(snapshotId))
                snapshotId = nowUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var sb = new StringBuilder(4096);
            sb.Append('{');
            sb.Append("\"schema_version\":").Append(JsonString(SchemaVersion)).Append(',');
            sb.Append("\"created_utc\":").Append(JsonString(FormatUtc(nowUtc))).Append(',');
            sb.Append("\"snapshot_id\":").Append(JsonString(snapshotId)).Append(',');
            sb.Append("\"source_mode\":\"live\",");
            sb.Append("\"required_timeframes_minutes\":").Append(JsonIntArray(RequiredTimeframesMinutes)).Append(',');
            sb.Append("\"fresh_instrument_count\":").Append(freshInstrumentCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"instrument_count\":").Append(instruments.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"coverage\":[").Append(string.Join(",", coverageRows)).Append("],");
            sb.Append("\"instruments\":[").Append(string.Join(",", instruments)).Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        private static string BuildCoverageJson(
            string root,
            bool isFresh,
            HashSet<int> presentMinutes,
            List<int> missingMinutes)
        {
            var present = presentMinutes.OrderBy(x => x).ToArray();
            return "{"
                + "\"instrument_root\":" + JsonString(root) + ","
                + "\"is_fresh\":" + (isFresh ? "true" : "false") + ","
                + "\"present_timeframes_minutes\":" + JsonIntArray(present) + ","
                + "\"missing_timeframes_minutes\":" + JsonIntArray(missingMinutes.ToArray())
                + "}";
        }

        private static string BuildInstrumentJson(
            GlitchIndicatorInstrumentSnapshot snapshot,
            bool isFresh,
            List<int> missingMinutes)
        {
            var readings = new List<string>();
            if (snapshot.TimeframeReadings != null)
            {
                foreach (KeyValuePair<int, GlitchIndicatorReading> entry in snapshot.TimeframeReadings.OrderBy(x => x.Key))
                {
                    GlitchIndicatorReading reading = entry.Value;
                    if (reading != null)
                        readings.Add(BuildReadingJson(reading));
                }
            }

            return "{"
                + "\"instrument\":" + JsonString(snapshot.InstrumentRoot) + ","
                + "\"timestamp_utc\":" + JsonString(FormatUtc(snapshot.UpdatedUtc)) + ","
                + "\"is_fresh\":" + (isFresh ? "true" : "false") + ","
                + "\"current_price\":" + JsonNullableNumber(snapshot.CurrentPrice) + ","
                + "\"session\":{"
                + "\"name\":" + JsonString(snapshot.SessionName) + ","
                + "\"high\":" + JsonNullableNumber(snapshot.SessionHigh) + ","
                + "\"low\":" + JsonNullableNumber(snapshot.SessionLow) + ","
                + "\"previous_high\":" + JsonNullableNumber(snapshot.PreviousSessionHigh) + ","
                + "\"previous_low\":" + JsonNullableNumber(snapshot.PreviousSessionLow)
                + "},"
                + "\"missing_timeframes_minutes\":" + JsonIntArray(missingMinutes.ToArray()) + ","
                + "\"timeframe_readings\":[" + string.Join(",", readings) + "]"
                + "}";
        }

        private static string BuildReadingJson(GlitchIndicatorReading reading)
        {
            return "{"
                + "\"minutes\":" + reading.Minutes.ToString(CultureInfo.InvariantCulture) + ","
                + "\"utc_time\":" + JsonString(FormatUtc(reading.UtcTime)) + ","
                + "\"current_price\":" + JsonNullableNumber(reading.CurrentPrice) + ","
                + "\"average_price\":" + JsonNullableNumber(reading.AveragePrice) + ","
                + "\"atr\":" + JsonNullableNumber(reading.Atr) + ","
                + "\"adx\":" + JsonNullableNumber(reading.Adx) + ","
                + "\"score\":" + JsonNumber(reading.Score) + ","
                + "\"raw_score\":" + JsonNullableNumber(reading.RawScore) + ","
                + "\"directional_score\":" + JsonNullableNumber(reading.DirectionalScore) + ","
                + "\"tradeability_score\":" + JsonNullableNumber(reading.TradeabilityScore) + ","
                + "\"signal_label\":" + JsonString(reading.SignalLabel) + ","
                + "\"volatility_hint\":" + JsonString(reading.VolatilityHint) + ","
                + "\"trend_hint\":" + JsonString(reading.TrendHint) + ","
                + "\"regime_label\":" + JsonString(reading.RegimeLabel) + ","
                + "\"no_trade_reasons\":" + JsonString(reading.NoTradeReasons) + ","
                + "\"rsi\":" + JsonNullableNumber(reading.Rsi) + ","
                + "\"stoch_k\":" + JsonNullableNumber(reading.StochK) + ","
                + "\"z_score\":" + JsonNullableNumber(reading.ZScore) + ","
                + "\"ema_alignment\":" + JsonNullableNumber(reading.EmaAlignment) + ","
                + "\"regime_weight\":" + JsonNullableNumber(reading.RegimeWeight) + ","
                + "\"oscillator_composite_score\":" + JsonNullableNumber(reading.OscillatorCompositeScore) + ","
                + "\"ma_composite_score\":" + JsonNullableNumber(reading.MaCompositeScore) + ","
                + "\"order_flow_score\":" + JsonNullableNumber(reading.OrderFlowScore) + ","
                + "\"order_flow_confidence\":" + JsonNullableNumber(reading.OrderFlowConfidence) + ","
                + "\"order_flow_reliability\":" + JsonNullableNumber(reading.OrderFlowReliability) + ","
                + "\"order_flow_cumulative_delta\":" + JsonNullableNumber(reading.OrderFlowCumulativeDelta) + ","
                + "\"order_flow_delta_change\":" + JsonNullableNumber(reading.OrderFlowDeltaChange) + ","
                + "\"order_flow_vwap\":" + JsonNullableNumber(reading.OrderFlowVwap) + ","
                + "\"order_flow_vwap_deviation\":" + JsonNullableNumber(reading.OrderFlowVwapDeviation) + ","
                + "\"order_flow_aggression_balance\":" + JsonNullableNumber(reading.OrderFlowAggressionBalance) + ","
                + "\"order_flow_depth_imbalance\":" + JsonNullableNumber(reading.OrderFlowDepthImbalance) + ","
                + "\"order_flow_hint\":" + JsonString(reading.OrderFlowHint)
                + "}";
        }

        private static string FormatUtc(DateTime value)
        {
            if (value == DateTime.MinValue)
                return string.Empty;

            return value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture)
                : value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        private static string JsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                switch (ch)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (ch < 32)
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(ch);
                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        private static string JsonNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "null";
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string JsonNullableNumber(double? value)
        {
            if (!value.HasValue)
                return "null";
            return JsonNumber(value.Value);
        }

        private static string JsonIntArray(IReadOnlyList<int> values)
        {
            if (values == null || values.Count == 0)
                return "[]";

            var parts = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
                parts[i] = values[i].ToString(CultureInfo.InvariantCulture);
            return "[" + string.Join(",", parts) + "]";
        }

        private static string ComputeStableHash(string json)
        {
            if (string.IsNullOrEmpty(json))
                return string.Empty;

            unchecked
            {
                int hash = 17;
                for (int i = 0; i < json.Length; i++)
                    hash = (hash * 31) + json[i];
                return hash.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
