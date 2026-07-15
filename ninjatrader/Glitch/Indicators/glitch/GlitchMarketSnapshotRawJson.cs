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
    /// glitch.market.snapshot.v2 — observations plus numeric derived analytics. No labels or trade opinions.
    /// </summary>
    internal static class GlitchMarketSnapshotRawJson
    {
        public const string SchemaVersion = "glitch.market.snapshot.v2";
        internal static readonly int[] RequiredTimeframesMinutes = { 1, 5, 15, 60 };

        internal sealed class RawIndicatorsPayload
        {
            public double? Atr { get; set; }
            public double? Adx { get; set; }
            public double? Rsi { get; set; }
            public double? StochK { get; set; }
            public double? ZScore { get; set; }
            public double? AveragePrice { get; set; }
            public double? DiPlus { get; set; }
            public double? DiMinus { get; set; }
            public double? Cci { get; set; }
            public double? MacdHistogram { get; set; }
            public double? OrderFlowCumulativeDelta { get; set; }
            public double? OrderFlowDeltaChange { get; set; }
            public double? OrderFlowVwap { get; set; }
            public double? OrderFlowVwapDeviation { get; set; }
        }

        internal sealed class DerivedAnalyticsPayload
        {
            public double? RawScore { get; set; }
            public double? DirectionalScore { get; set; }
            public double? TradeabilityScore { get; set; }
            public double? EmaAlignment { get; set; }
            public double? RegimeWeight { get; set; }
            public double? OscillatorCompositeScore { get; set; }
            public double? MaCompositeScore { get; set; }
            public double? OrderFlowScore { get; set; }
            public double? OrderFlowConfidence { get; set; }
            public double? OrderFlowReliability { get; set; }
        }

        internal sealed class RawTimeframeBarPayload
        {
            public int Minutes { get; set; }
            public DateTime UtcTime { get; set; }
            public double? Open { get; set; }
            public double? High { get; set; }
            public double? Low { get; set; }
            public double? Close { get; set; }
            public double? Volume { get; set; }
            public RawIndicatorsPayload Indicators { get; set; }
            public DerivedAnalyticsPayload DerivedAnalytics { get; set; }
        }

        internal sealed class RawInstrumentPayload
        {
            public string InstrumentRoot { get; set; }
            public string InstrumentFullName { get; set; }
            public DateTime UpdatedUtc { get; set; }
            public double? CurrentPrice { get; set; }
            public string SessionName { get; set; }
            public double? SessionHigh { get; set; }
            public double? SessionLow { get; set; }
            public double? PreviousSessionHigh { get; set; }
            public double? PreviousSessionLow { get; set; }
            public List<RawTimeframeBarPayload> TimeframeBars { get; set; }
        }

        internal static string BuildSnapshotJson(
            string sourceMode,
            DateTime createdUtc,
            string snapshotId,
            IReadOnlyList<RawInstrumentPayload> instruments)
        {
            if (instruments == null || instruments.Count == 0)
                return null;

            var instrumentJson = new List<string>(instruments.Count);
            var coverageRows = new List<string>(instruments.Count);

            for (int i = 0; i < instruments.Count; i++)
            {
                RawInstrumentPayload instrument = instruments[i];
                if (instrument == null || string.IsNullOrWhiteSpace(instrument.InstrumentRoot))
                    continue;

                var presentMinutes = new HashSet<int>();
                if (instrument.TimeframeBars != null)
                {
                    for (int b = 0; b < instrument.TimeframeBars.Count; b++)
                    {
                        RawTimeframeBarPayload bar = instrument.TimeframeBars[b];
                        if (bar != null)
                            presentMinutes.Add(bar.Minutes);
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
                coverageRows.Add(BuildCoverageJson(instrument.InstrumentRoot, presentMinutes, missingMinutes));
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
            sb.Append("\"created_utc\":").Append(JsonString(GlitchMarketSnapshotJson.FormatUtc(createdUtc))).Append(',');
            sb.Append("\"snapshot_id\":").Append(JsonString(snapshotId)).Append(',');
            sb.Append("\"source_mode\":").Append(JsonString(sourceMode)).Append(',');
            sb.Append("\"required_timeframes_minutes\":").Append(JsonIntArray(RequiredTimeframesMinutes)).Append(',');
            sb.Append("\"instrument_count\":").Append(instrumentJson.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"coverage\":[").Append(string.Join(",", coverageRows)).Append("],");
            sb.Append("\"instruments\":[").Append(string.Join(",", instrumentJson)).Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        private static string BuildCoverageJson(
            string root,
            HashSet<int> presentMinutes,
            List<int> missingMinutes)
        {
            int[] present = presentMinutes == null
                ? Array.Empty<int>()
                : presentMinutes.OrderBy(x => x).ToArray();

            return "{"
                + "\"instrument_root\":" + JsonString(root) + ","
                + "\"present_timeframes_minutes\":" + JsonIntArray(present) + ","
                + "\"missing_timeframes_minutes\":" + JsonIntArray(missingMinutes == null ? Array.Empty<int>() : missingMinutes.ToArray())
                + "}";
        }

        private static string BuildInstrumentJson(RawInstrumentPayload snapshot, List<int> missingMinutes)
        {
            var bars = new List<string>();
            if (snapshot.TimeframeBars != null)
            {
                foreach (RawTimeframeBarPayload bar in snapshot.TimeframeBars.Where(x => x != null).OrderBy(x => x.Minutes))
                    bars.Add(BuildTimeframeBarJson(bar));
            }

            return "{"
                + "\"instrument\":" + JsonString(snapshot.InstrumentRoot) + ","
                + "\"instrument_full_name\":" + JsonString(snapshot.InstrumentFullName) + ","
                + "\"timestamp_utc\":" + JsonString(GlitchMarketSnapshotJson.FormatUtc(snapshot.UpdatedUtc)) + ","
                + "\"current_price\":" + JsonNullableNumber(snapshot.CurrentPrice) + ","
                + "\"session\":{"
                + "\"name\":" + JsonString(snapshot.SessionName) + ","
                + "\"high\":" + JsonNullableNumber(snapshot.SessionHigh) + ","
                + "\"low\":" + JsonNullableNumber(snapshot.SessionLow) + ","
                + "\"previous_high\":" + JsonNullableNumber(snapshot.PreviousSessionHigh) + ","
                + "\"previous_low\":" + JsonNullableNumber(snapshot.PreviousSessionLow)
                + "},"
                + "\"missing_timeframes_minutes\":" + JsonIntArray(missingMinutes == null ? Array.Empty<int>() : missingMinutes.ToArray()) + ","
                + "\"timeframe_bars\":[" + string.Join(",", bars) + "]"
                + "}";
        }

        private static string BuildTimeframeBarJson(RawTimeframeBarPayload bar)
        {
            RawIndicatorsPayload ind = bar.Indicators ?? new RawIndicatorsPayload();
            DerivedAnalyticsPayload derived = bar.DerivedAnalytics ?? new DerivedAnalyticsPayload();
            return "{"
                + "\"minutes\":" + bar.Minutes.ToString(CultureInfo.InvariantCulture) + ","
                + "\"utc_time\":" + JsonString(GlitchMarketSnapshotJson.FormatUtc(bar.UtcTime)) + ","
                + "\"open\":" + JsonNullableNumber(bar.Open) + ","
                + "\"high\":" + JsonNullableNumber(bar.High) + ","
                + "\"low\":" + JsonNullableNumber(bar.Low) + ","
                + "\"close\":" + JsonNullableNumber(bar.Close) + ","
                + "\"volume\":" + JsonNullableNumber(bar.Volume) + ","
                + "\"indicators\":{"
                + "\"atr\":" + JsonNullableNumber(ind.Atr) + ","
                + "\"adx\":" + JsonNullableNumber(ind.Adx) + ","
                + "\"rsi\":" + JsonNullableNumber(ind.Rsi) + ","
                + "\"stoch_k\":" + JsonNullableNumber(ind.StochK) + ","
                + "\"z_score\":" + JsonNullableNumber(ind.ZScore) + ","
                + "\"average_price\":" + JsonNullableNumber(ind.AveragePrice) + ","
                + "\"di_plus\":" + JsonNullableNumber(ind.DiPlus) + ","
                + "\"di_minus\":" + JsonNullableNumber(ind.DiMinus) + ","
                + "\"cci\":" + JsonNullableNumber(ind.Cci) + ","
                + "\"macd_histogram\":" + JsonNullableNumber(ind.MacdHistogram) + ","
                + "\"order_flow_cumulative_delta\":" + JsonNullableNumber(ind.OrderFlowCumulativeDelta) + ","
                + "\"order_flow_delta_change\":" + JsonNullableNumber(ind.OrderFlowDeltaChange) + ","
                + "\"order_flow_vwap\":" + JsonNullableNumber(ind.OrderFlowVwap) + ","
                + "\"order_flow_vwap_deviation\":" + JsonNullableNumber(ind.OrderFlowVwapDeviation)
                + "},"
                + "\"derived_analytics\":{"
                + "\"raw_score\":" + JsonNullableNumber(derived.RawScore) + ","
                + "\"directional_score\":" + JsonNullableNumber(derived.DirectionalScore) + ","
                + "\"tradeability_score\":" + JsonNullableNumber(derived.TradeabilityScore) + ","
                + "\"ema_alignment\":" + JsonNullableNumber(derived.EmaAlignment) + ","
                + "\"regime_weight\":" + JsonNullableNumber(derived.RegimeWeight) + ","
                + "\"oscillator_composite_score\":" + JsonNullableNumber(derived.OscillatorCompositeScore) + ","
                + "\"ma_composite_score\":" + JsonNullableNumber(derived.MaCompositeScore) + ","
                + "\"order_flow_score\":" + JsonNullableNumber(derived.OrderFlowScore) + ","
                + "\"order_flow_confidence\":" + JsonNullableNumber(derived.OrderFlowConfidence) + ","
                + "\"order_flow_reliability\":" + JsonNullableNumber(derived.OrderFlowReliability)
                + "}"
                + "}";
        }

        private static string JsonString(string value)
        {
            return GlitchMarketSnapshotJsonInject.String(value);
        }

        private static string JsonNullableNumber(double? value)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
                return "null";
            return value.Value.ToString("R", CultureInfo.InvariantCulture);
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

    // ponytail: reuse v1 json escaper without making GlitchMarketSnapshotJson public helpers wider.
    internal static class GlitchMarketSnapshotJsonInject
    {
        internal static string String(string value)
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
    }
}
