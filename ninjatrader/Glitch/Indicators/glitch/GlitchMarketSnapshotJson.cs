#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Legacy glitch.market.snapshot.v1 (opinionated). v2 raw export uses GlitchMarketSnapshotRawJson.
    /// </summary>
    internal static class GlitchMarketSnapshotJson
    {
        public const string SchemaVersion = "glitch.market.snapshot.v1";
        internal static readonly int[] RequiredTimeframesMinutes = { 1, 5, 15, 60 };

        internal sealed class InstrumentPayload
        {
            public string InstrumentRoot { get; set; }
            public DateTime UpdatedUtc { get; set; }
            public bool IsFresh { get; set; }
            public double? CurrentPrice { get; set; }
            public string SessionName { get; set; }
            public double? SessionHigh { get; set; }
            public double? SessionLow { get; set; }
            public double? PreviousSessionHigh { get; set; }
            public double? PreviousSessionLow { get; set; }
            public List<GlitchBridgeBusCompat.BridgeReading> TimeframeReadings { get; set; }
        }

        internal static string BuildSnapshotJson(
            string sourceMode,
            DateTime createdUtc,
            string snapshotId,
            IReadOnlyList<InstrumentPayload> instruments)
        {
            if (instruments == null || instruments.Count == 0)
                return null;

            var instrumentJson = new List<string>(instruments.Count);
            var coverageRows = new List<string>(instruments.Count);
            int freshInstrumentCount = 0;

            for (int i = 0; i < instruments.Count; i++)
            {
                InstrumentPayload instrument = instruments[i];
                if (instrument == null || string.IsNullOrWhiteSpace(instrument.InstrumentRoot))
                    continue;

                if (instrument.IsFresh)
                    freshInstrumentCount++;

                var presentMinutes = new HashSet<int>();
                if (instrument.TimeframeReadings != null)
                {
                    for (int r = 0; r < instrument.TimeframeReadings.Count; r++)
                    {
                        GlitchBridgeBusCompat.BridgeReading reading = instrument.TimeframeReadings[r];
                        if (reading != null)
                            presentMinutes.Add(reading.Minutes);
                    }
                }

                var missingMinutes = new List<int>();
                for (int m = 0; m < RequiredTimeframesMinutes.Length; m++)
                {
                    int required = RequiredTimeframesMinutes[m];
                    if (!presentMinutes.Contains(required))
                        missingMinutes.Add(required);
                }

                instrumentJson.Add(BuildInstrumentJson(instrument, missingMinutes));
                coverageRows.Add(BuildCoverageJson(instrument.InstrumentRoot, instrument.IsFresh, presentMinutes, missingMinutes));
            }

            if (instrumentJson.Count == 0)
                return null;

            if (string.IsNullOrWhiteSpace(snapshotId))
                snapshotId = createdUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

            if (string.IsNullOrWhiteSpace(sourceMode))
                sourceMode = "live";

            var sb = new StringBuilder(4096);
            sb.Append('{');
            sb.Append("\"schema_version\":").Append(JsonString(SchemaVersion)).Append(',');
            sb.Append("\"created_utc\":").Append(JsonString(FormatUtc(createdUtc))).Append(',');
            sb.Append("\"snapshot_id\":").Append(JsonString(snapshotId)).Append(',');
            sb.Append("\"source_mode\":").Append(JsonString(sourceMode)).Append(',');
            sb.Append("\"required_timeframes_minutes\":").Append(JsonIntArray(RequiredTimeframesMinutes)).Append(',');
            sb.Append("\"fresh_instrument_count\":").Append(freshInstrumentCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"instrument_count\":").Append(instrumentJson.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"coverage\":[").Append(string.Join(",", coverageRows)).Append("],");
            sb.Append("\"instruments\":[").Append(string.Join(",", instrumentJson)).Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        internal static string InjectSnapshotHash(string json, string hash)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(hash))
                return json;

            const string marker = "\"snapshot_id\":";
            int index = json.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
                return json;

            int valueStart = index + marker.Length;
            int valueEnd = json.IndexOf(',', valueStart);
            if (valueEnd < 0)
                return json;

            string insert = ",\"snapshot_hash\":" + JsonString(hash);
            return json.Substring(0, valueEnd) + insert + json.Substring(valueEnd);
        }

        internal static string ComputeStableHash(string json)
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

        internal static GlitchBridgeBusCompat.BridgeReading ToBridgeReading(GlitchIndicatorReadingAdapter reading)
        {
            if (reading == null)
                return null;

            return new GlitchBridgeBusCompat.BridgeReading
            {
                InstrumentRoot = reading.InstrumentRoot,
                Minutes = reading.Minutes,
                UtcTime = reading.UtcTime,
                CurrentPrice = reading.CurrentPrice,
                AveragePrice = reading.AveragePrice,
                Atr = reading.Atr,
                Adx = reading.Adx,
                Score = reading.Score,
                RawScore = reading.RawScore,
                DirectionalScore = reading.DirectionalScore,
                TradeabilityScore = reading.TradeabilityScore,
                SignalLabel = reading.SignalLabel,
                VolatilityHint = reading.VolatilityHint,
                TrendHint = reading.TrendHint,
                RegimeLabel = reading.RegimeLabel,
                NoTradeReasons = reading.NoTradeReasons,
                Rsi = reading.Rsi,
                StochK = reading.StochK,
                ZScore = reading.ZScore,
                EmaAlignment = reading.EmaAlignment,
                RegimeWeight = reading.RegimeWeight,
                OscillatorCompositeScore = reading.OscillatorCompositeScore,
                MaCompositeScore = reading.MaCompositeScore,
                OrderFlowScore = reading.OrderFlowScore,
                OrderFlowConfidence = reading.OrderFlowConfidence,
                OrderFlowReliability = reading.OrderFlowReliability,
                OrderFlowCumulativeDelta = reading.OrderFlowCumulativeDelta,
                OrderFlowDeltaChange = reading.OrderFlowDeltaChange,
                OrderFlowVwap = reading.OrderFlowVwap,
                OrderFlowVwapDeviation = reading.OrderFlowVwapDeviation,
                OrderFlowAggressionBalance = reading.OrderFlowAggressionBalance,
                OrderFlowDepthImbalance = reading.OrderFlowDepthImbalance,
                OrderFlowHint = reading.OrderFlowHint,
                SessionName = reading.SessionName,
                SessionHigh = reading.SessionHigh,
                SessionLow = reading.SessionLow,
                PreviousSessionHigh = reading.PreviousSessionHigh,
                PreviousSessionLow = reading.PreviousSessionLow
            };
        }

        private static string BuildCoverageJson(
            string root,
            bool isFresh,
            HashSet<int> presentMinutes,
            List<int> missingMinutes)
        {
            int[] present = presentMinutes == null
                ? Array.Empty<int>()
                : presentMinutes.OrderBy(x => x).ToArray();

            return "{"
                + "\"instrument_root\":" + JsonString(root) + ","
                + "\"is_fresh\":" + (isFresh ? "true" : "false") + ","
                + "\"present_timeframes_minutes\":" + JsonIntArray(present) + ","
                + "\"missing_timeframes_minutes\":" + JsonIntArray(missingMinutes == null ? Array.Empty<int>() : missingMinutes.ToArray())
                + "}";
        }

        private static string BuildInstrumentJson(InstrumentPayload snapshot, List<int> missingMinutes)
        {
            var readings = new List<string>();
            if (snapshot.TimeframeReadings != null)
            {
                IEnumerable<GlitchBridgeBusCompat.BridgeReading> ordered = snapshot.TimeframeReadings
                    .Where(x => x != null)
                    .OrderBy(x => x.Minutes);

                foreach (GlitchBridgeBusCompat.BridgeReading reading in ordered)
                    readings.Add(BuildReadingJson(reading));
            }

            return "{"
                + "\"instrument\":" + JsonString(snapshot.InstrumentRoot) + ","
                + "\"timestamp_utc\":" + JsonString(FormatUtc(snapshot.UpdatedUtc)) + ","
                + "\"is_fresh\":" + (snapshot.IsFresh ? "true" : "false") + ","
                + "\"current_price\":" + JsonNullableNumber(snapshot.CurrentPrice) + ","
                + "\"session\":{"
                + "\"name\":" + JsonString(snapshot.SessionName) + ","
                + "\"high\":" + JsonNullableNumber(snapshot.SessionHigh) + ","
                + "\"low\":" + JsonNullableNumber(snapshot.SessionLow) + ","
                + "\"previous_high\":" + JsonNullableNumber(snapshot.PreviousSessionHigh) + ","
                + "\"previous_low\":" + JsonNullableNumber(snapshot.PreviousSessionLow)
                + "},"
                + "\"missing_timeframes_minutes\":" + JsonIntArray(missingMinutes == null ? Array.Empty<int>() : missingMinutes.ToArray()) + ","
                + "\"timeframe_readings\":[" + string.Join(",", readings) + "]"
                + "}";
        }

        internal static string BuildReadingJson(GlitchBridgeBusCompat.BridgeReading reading)
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

        public static string FormatUtc(DateTime value)
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
    }

    /// <summary>
    /// Field-compatible adapter so AddOn GlitchIndicatorReading can feed the shared snapshot JSON builder.
    /// </summary>
    internal sealed class GlitchIndicatorReadingAdapter
    {
        public string InstrumentRoot { get; set; }
        public int Minutes { get; set; }
        public DateTime UtcTime { get; set; }
        public double? CurrentPrice { get; set; }
        public double? AveragePrice { get; set; }
        public double? Atr { get; set; }
        public double? Adx { get; set; }
        public double Score { get; set; }
        public double? RawScore { get; set; }
        public double? DirectionalScore { get; set; }
        public double? TradeabilityScore { get; set; }
        public string SignalLabel { get; set; }
        public string VolatilityHint { get; set; }
        public string TrendHint { get; set; }
        public string RegimeLabel { get; set; }
        public string NoTradeReasons { get; set; }
        public double? Rsi { get; set; }
        public double? StochK { get; set; }
        public double? ZScore { get; set; }
        public double? EmaAlignment { get; set; }
        public double? RegimeWeight { get; set; }
        public double? OscillatorCompositeScore { get; set; }
        public double? MaCompositeScore { get; set; }
        public double? OrderFlowScore { get; set; }
        public double? OrderFlowConfidence { get; set; }
        public double? OrderFlowReliability { get; set; }
        public double? OrderFlowCumulativeDelta { get; set; }
        public double? OrderFlowDeltaChange { get; set; }
        public double? OrderFlowVwap { get; set; }
        public double? OrderFlowVwapDeviation { get; set; }
        public double? OrderFlowAggressionBalance { get; set; }
        public double? OrderFlowDepthImbalance { get; set; }
        public string OrderFlowHint { get; set; }
        public string SessionName { get; set; }
        public double? SessionHigh { get; set; }
        public double? SessionLow { get; set; }
        public double? PreviousSessionHigh { get; set; }
        public double? PreviousSessionLow { get; set; }
    }
}
