#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Session and closed-1m volume at price. Tick updates mutate an existing bin.
    /// POC, value area, and volume above/below are computed from the uncapped map.
    /// </summary>
    internal sealed class GlitchVolumeAtPrice
    {
        internal const int SessionRowCap = 800;
        internal const int BarRowCap = 120;
        // Keep enough sealed 1m footprints for the Hermes chart numeric footprint pane.
        internal const int ClosedBarCount = 30;
        // If no trade mutates the map for this long, publish status=stale instead of ready.
        internal static readonly TimeSpan ReadyStaleAfter = TimeSpan.FromMinutes(3);

        private Dictionary<long, VolumeBin> _session = new Dictionary<long, VolumeBin>();
        private Dictionary<long, VolumeBin> _minute = new Dictionary<long, VolumeBin>();
        private readonly ClosedBar[] _closed = new ClosedBar[ClosedBarCount];
        private int _closedStart;
        private int _closedCount;
        private string _sessionKey;
        private DateTime _minuteUtc = DateTime.MinValue;
        private bool _minuteOpen;
        private bool _sawAtQuote;
        private bool _capturedOpenHigh;
        private long _lastTick;
        private bool _hasLastTick;
        private DateTime _lastTradeUtc = DateTime.MinValue;
        private double _lastTradePrice;
        private double _tickSize;

        internal double? OpenHigh { get; private set; }

        internal void Reset()
        {
            _session = new Dictionary<long, VolumeBin>();
            _minute = new Dictionary<long, VolumeBin>();
            Array.Clear(_closed, 0, _closed.Length);
            _closedStart = 0;
            _closedCount = 0;
            _sessionKey = null;
            _minuteUtc = DateTime.MinValue;
            _minuteOpen = false;
            _sawAtQuote = false;
            _capturedOpenHigh = false;
            OpenHigh = null;
            _hasLastTick = false;
            _lastTradeUtc = DateTime.MinValue;
            _lastTradePrice = 0;
            _tickSize = 0;
        }

        internal void AddClassified(
            double price,
            double bidVolume,
            double askVolume,
            double tickSize,
            string sessionKey,
            DateTime barUtc,
            bool atQuote)
        {
            if (tickSize <= 0 || string.IsNullOrEmpty(sessionKey))
                return;
            if (bidVolume <= 0 && askVolume <= 0)
                return;
            if (bidVolume < 0)
                bidVolume = 0;
            if (askVolume < 0)
                askVolume = 0;

            EnsureSession(sessionKey, tickSize);
            if (atQuote)
                _sawAtQuote = true;

            DateTime minute = TruncateToMinute(barUtc);
            if (!_minuteOpen)
            {
                _minuteUtc = minute;
                _minuteOpen = true;
            }
            else if (minute != _minuteUtc)
            {
                SealMinute();
                _minuteUtc = minute;
                _minuteOpen = true;
            }

            long tick = ToTick(price, tickSize);
            AddTo(_session, tick, bidVolume, askVolume);
            AddTo(_minute, tick, bidVolume, askVolume);
            _lastTick = tick;
            _hasLastTick = true;
            _lastTradeUtc = barUtc.Kind == DateTimeKind.Utc ? barUtc : barUtc.ToUniversalTime();
            _lastTradePrice = price;
        }

        internal void AddTickRule(
            double price,
            double volume,
            double tickSize,
            string sessionKey,
            DateTime barUtc)
        {
            if (volume <= 0)
                return;

            double bid = 0;
            double ask = 0;
            if (_lastTradePrice > 0 && price > _lastTradePrice)
                ask = volume;
            else if (_lastTradePrice > 0 && price < _lastTradePrice)
                bid = volume;
            else
            {
                bid = volume * 0.5;
                ask = volume * 0.5;
            }

            AddClassified(price, bid, ask, tickSize, sessionKey, barUtc, false);
        }

        internal void SealOpenMinute(DateTime barUtc)
        {
            if (!_minuteOpen || _minute.Count == 0)
                return;
            if (TruncateToMinute(barUtc) != _minuteUtc)
                return;
            SealMinute();
            _minuteOpen = false;
        }

        internal string BuildUnavailable(string reason)
        {
            return "{"
                + "\"schema_version\":\"glitch.volume_at_price.v1\","
                + "\"status\":\"unavailable\","
                + "\"reason\":" + JsonString(reason) + ","
                + "\"classification\":\"tick_rule_only\""
                + "}";
        }

        internal string BuildJson(
            double tickSize,
            string sessionKey,
            double? sessionHigh,
            double? sessionLow,
            double? previousHigh,
            double? previousLow,
            double? sessionOpen)
        {
            if (tickSize <= 0)
                return BuildUnavailable("tick_size_missing");

            if (_session.Count == 0)
            {
                return "{"
                    + "\"schema_version\":\"glitch.volume_at_price.v1\","
                    + "\"status\":\"warming\","
                    + "\"reason\":\"no_trades\","
                    + "\"session_key\":" + JsonString(sessionKey ?? _sessionKey) + ","
                    + "\"tick_size\":" + FormatPrice(tickSize) + ","
                    + "\"classification\":\"" + Classification() + "\","
                    + "\"bars_1m\":" + BuildBarsJson(tickSize)
                    + "}";
            }

            double size = _tickSize > 0 ? _tickSize : tickSize;
            long anchor = _hasLastTick ? _lastTick : 0;
            int omittedBins;
            long[] sessionTicks = NearestTicks(_session, anchor, SessionRowCap, out omittedBins);

            double bidTotal = 0;
            double askTotal = 0;
            long minTick = long.MaxValue;
            long maxTick = long.MinValue;
            long pocTick = 0;
            double pocVolume = -1;
            long pocDistance = long.MaxValue;
            foreach (KeyValuePair<long, VolumeBin> entry in _session)
            {
                double total = entry.Value.Bid + entry.Value.Ask;
                bidTotal += entry.Value.Bid;
                askTotal += entry.Value.Ask;
                if (entry.Key < minTick)
                    minTick = entry.Key;
                if (entry.Key > maxTick)
                    maxTick = entry.Key;
                long distance = _hasLastTick ? Math.Abs(entry.Key - anchor) : 0;
                if (total > pocVolume
                    || (total == pocVolume && (distance < pocDistance
                        || (distance == pocDistance && entry.Key < pocTick))))
                {
                    pocVolume = total;
                    pocTick = entry.Key;
                    pocDistance = distance;
                }
            }

            long valTick;
            long vahTick;
            ValueArea(minTick, maxTick, pocTick, bidTotal + askTotal, out valTick, out vahTick);

            bool stale = _hasLastTick
                && _lastTradeUtc > DateTime.MinValue
                && (DateTime.UtcNow - _lastTradeUtc) > ReadyStaleAfter;
            var sb = new StringBuilder(2048);
            sb.Append("{\"schema_version\":\"glitch.volume_at_price.v1\",\"status\":\"");
            sb.Append(stale ? "stale" : "ready").Append('"');
            if (stale)
                sb.Append(",\"reason\":\"no_recent_trades\"");
            sb.Append(",\"session_key\":");
            sb.Append(JsonString(string.IsNullOrEmpty(_sessionKey) ? sessionKey : _sessionKey));
            sb.Append(",\"tick_size\":").Append(FormatPrice(size));
            sb.Append(",\"classification\":\"").Append(Classification()).Append('"');
            if (_hasLastTick && _lastTradeUtc > DateTime.MinValue)
            {
                sb.Append(",\"last_trade_utc\":");
                sb.Append(JsonString(_lastTradeUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)));
            }
            sb.Append(",\"session_truncated\":").Append(omittedBins > 0 ? "true" : "false");
            sb.Append(",\"session_omitted_bins\":").Append(omittedBins.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"session_bid_total\":").Append(FormatVolume(bidTotal));
            sb.Append(",\"session_ask_total\":").Append(FormatVolume(askTotal));
            sb.Append(",\"session_volume_total\":").Append(FormatVolume(bidTotal + askTotal));
            sb.Append(",\"session_rows\":").Append(BuildRowsJson(_session, sessionTicks, size));
            sb.Append(",\"full_map\":{\"poc\":").Append(FormatPrice(pocTick * size));
            sb.Append(",\"vah\":").Append(FormatPrice(vahTick * size));
            sb.Append(",\"val\":").Append(FormatPrice(valTick * size));
            sb.Append(",\"value_area_fraction\":0.7,\"levels\":[");
            AppendLevel(sb, "session_high", sessionHigh, size);
            AppendLevel(sb, "session_low", sessionLow, size);
            AppendLevel(sb, "previous_session_high", previousHigh, size);
            AppendLevel(sb, "previous_session_low", previousLow, size);
            AppendLevel(sb, "session_open", sessionOpen, size);
            AppendLevel(sb, "open_high", OpenHigh, size);
            sb.Append("]},\"bars_1m\":").Append(BuildBarsJson(size)).Append('}');
            return sb.ToString();
        }

        internal static void SelfCheck()
        {
            var profile = new GlitchVolumeAtPrice();
            DateTime t0 = new DateTime(2026, 9, 30, 14, 0, 0, DateTimeKind.Utc);
            profile.AddClassified(100.0, 5, 5, 0.25, "NYC|2026093008", t0, true);
            profile.AddClassified(100.25, 2, 2, 0.25, "NYC|2026093008", t0, true);
            profile.AddClassified(100.5, 20, 30, 0.25, "NYC|2026093008", t0, true);
            profile.AddClassified(100.75, 3, 3, 0.25, "NYC|2026093008", t0, true);
            profile.AddClassified(101.0, 1, 1, 0.25, "NYC|2026093008", t0, true);
            profile.SealOpenMinute(t0);
            string json = profile.BuildJson(0.25, "NYC|2026093008", 101, 100, null, null, 100);
            if (json.IndexOf("\"poc\":100.5", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("poc");
            if (json.IndexOf("\"val\":100.5", StringComparison.Ordinal) < 0
                || json.IndexOf("\"vah\":100.75", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("value area");
            if (json.IndexOf("\"bars_1m\":[{", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("closed bar");

            var wide = new GlitchVolumeAtPrice();
            DateTime t1 = new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i <= SessionRowCap; i++)
                wide.AddClassified(i * 0.25, i == 0 ? 100 : 1, 0, 0.25, "NYC|2026093008", t1, false);
            string wideJson = wide.BuildJson(0.25, "NYC|2026093008", null, null, null, null, null);
            if (wideJson.IndexOf("\"session_truncated\":true", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("truncated");
            if (wideJson.IndexOf("\"poc\":0,", StringComparison.Ordinal) < 0
                && wideJson.IndexOf("\"poc\":0}", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("full map poc");
            int rowsAt = wideJson.IndexOf("\"session_rows\":", StringComparison.Ordinal);
            int fullAt = wideJson.IndexOf("\"full_map\":", StringComparison.Ordinal);
            string rows = wideJson.Substring(rowsAt, fullAt - rowsAt);
            if (rows.IndexOf("\"p\":0,\"b\":100", StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("truncated rows kept poc bin");

            var split = new GlitchVolumeAtPrice();
            split.AddTickRule(10, 1, 0.25, "NYC|2026093008", t0);
            string splitJson = split.BuildJson(0.25, "NYC|2026093008", null, null, null, null, null);
            if (splitJson.IndexOf("\"b\":0.5", StringComparison.Ordinal) < 0
                || splitJson.IndexOf("\"a\":0.5", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("half volume");

            var aged = new GlitchVolumeAtPrice();
            aged.AddClassified(50, 1, 1, 0.25, "NYC|2026093008", t0, true);
            aged._lastTradeUtc = DateTime.UtcNow - ReadyStaleAfter - TimeSpan.FromSeconds(1);
            string agedJson = aged.BuildJson(0.25, "NYC|2026093008", null, null, null, null, null);
            if (agedJson.IndexOf("\"status\":\"stale\"", StringComparison.Ordinal) < 0
                || agedJson.IndexOf("\"reason\":\"no_recent_trades\"", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("stale status");
        }

        private void EnsureSession(string sessionKey, double tickSize)
        {
            if (!string.Equals(_sessionKey, sessionKey, StringComparison.Ordinal))
            {
                Reset();
                _sessionKey = sessionKey;
            }
            _tickSize = tickSize;
        }

        private void SealMinute()
        {
            if (_minute.Count == 0)
                return;

            int omitted;
            long anchor = _hasLastTick ? _lastTick : 0;
            long[] ticks = NearestTicks(_minute, anchor, BarRowCap, out omitted);
            var bar = new ClosedBar
            {
                Utc = _minuteUtc,
                Ticks = ticks,
                Bid = new double[ticks.Length],
                Ask = new double[ticks.Length],
                Truncated = omitted > 0,
                Omitted = omitted
            };
            long maxTick = long.MinValue;
            foreach (long tick in _minute.Keys)
            {
                if (tick > maxTick)
                    maxTick = tick;
            }
            for (int i = 0; i < ticks.Length; i++)
            {
                VolumeBin bin = _minute[ticks[i]];
                bar.Bid[i] = bin.Bid;
                bar.Ask[i] = bin.Ask;
            }

            if (!_capturedOpenHigh && ticks.Length > 0)
            {
                OpenHigh = maxTick * (_tickSize > 0 ? _tickSize : 0.25);
                _capturedOpenHigh = true;
            }

            if (_closedCount == ClosedBarCount)
            {
                _closed[_closedStart] = bar;
                _closedStart = (_closedStart + 1) % ClosedBarCount;
            }
            else
            {
                int index = (_closedStart + _closedCount) % ClosedBarCount;
                _closed[index] = bar;
                _closedCount++;
            }

            _minute = new Dictionary<long, VolumeBin>();
        }

        private void ValueArea(long minTick, long maxTick, long pocTick, double sessionTotal, out long valTick, out long vahTick)
        {
            valTick = pocTick;
            vahTick = pocTick;
            double target = sessionTotal * 0.7;
            double included = VolumeAt(pocTick);
            long span = maxTick - minTick;
            if (span < 0 || span > 20000)
                return;

            while (included + 1e-9 < target && (valTick > minTick || vahTick < maxTick))
            {
                double left = valTick > minTick ? VolumeAt(valTick - 1) : -1;
                double right = vahTick < maxTick ? VolumeAt(vahTick + 1) : -1;
                if (left < 0 && right < 0)
                    break;
                if (left >= 0 && right >= 0 && left == right)
                {
                    valTick--;
                    vahTick++;
                    included += left + right;
                }
                else if (right < 0 || (left >= 0 && left > right))
                {
                    valTick--;
                    included += left;
                }
                else
                {
                    vahTick++;
                    included += right;
                }
            }
        }

        private double VolumeAt(long tick)
        {
            VolumeBin bin;
            if (!_session.TryGetValue(tick, out bin) || bin == null)
                return 0;
            return bin.Bid + bin.Ask;
        }

        private void AppendLevel(StringBuilder sb, string name, double? price, double tickSize)
        {
            if (!price.HasValue || price.Value <= 0 || tickSize <= 0)
                return;
            if (sb[sb.Length - 1] != '[')
                sb.Append(',');
            long levelTick = ToTick(price.Value, tickSize);
            double above = 0;
            double below = 0;
            foreach (KeyValuePair<long, VolumeBin> entry in _session)
            {
                double total = entry.Value.Bid + entry.Value.Ask;
                if (entry.Key > levelTick)
                    above += total;
                else if (entry.Key < levelTick)
                    below += total;
            }

            sb.Append("{\"name\":").Append(JsonString(name));
            sb.Append(",\"price\":").Append(FormatPrice(levelTick * tickSize));
            sb.Append(",\"volume_above\":").Append(FormatVolume(above));
            sb.Append(",\"volume_below\":").Append(FormatVolume(below)).Append('}');
        }

        private string BuildBarsJson(double tickSize)
        {
            if (_closedCount == 0)
                return "[]";
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < _closedCount; i++)
            {
                ClosedBar bar = _closed[(_closedStart + i) % ClosedBarCount];
                if (i > 0)
                    sb.Append(',');
                sb.Append("{\"utc_time\":");
                sb.Append(JsonString(bar.Utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)));
                if (bar.Truncated)
                {
                    sb.Append(",\"bar_truncated\":true,\"bar_omitted_bins\":");
                    sb.Append(bar.Omitted.ToString(CultureInfo.InvariantCulture));
                }
                sb.Append(",\"rows\":").Append(BuildStoredRows(bar, tickSize)).Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string BuildRowsJson(Dictionary<long, VolumeBin> map, long[] ticks, double tickSize)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < ticks.Length; i++)
            {
                VolumeBin bin = map[ticks[i]];
                if (i > 0)
                    sb.Append(',');
                sb.Append("{\"p\":").Append(FormatPrice(ticks[i] * tickSize));
                sb.Append(",\"b\":").Append(FormatVolume(bin.Bid));
                sb.Append(",\"a\":").Append(FormatVolume(bin.Ask)).Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string BuildStoredRows(ClosedBar bar, double tickSize)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < bar.Ticks.Length; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append("{\"p\":").Append(FormatPrice(bar.Ticks[i] * tickSize));
                sb.Append(",\"b\":").Append(FormatVolume(bar.Bid[i]));
                sb.Append(",\"a\":").Append(FormatVolume(bar.Ask[i])).Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static long[] NearestTicks(Dictionary<long, VolumeBin> map, long anchor, int cap, out int omitted)
        {
            var ticks = new long[map.Count];
            int count = 0;
            foreach (long tick in map.Keys)
                ticks[count++] = tick;
            if (count <= cap)
            {
                Array.Sort(ticks);
                omitted = 0;
                return ticks;
            }

            Array.Sort(ticks, delegate(long left, long right)
            {
                long leftDistance = Math.Abs(left - anchor);
                long rightDistance = Math.Abs(right - anchor);
                int compared = leftDistance.CompareTo(rightDistance);
                if (compared != 0)
                    return compared;
                return left.CompareTo(right);
            });
            var kept = new long[cap];
            Array.Copy(ticks, kept, cap);
            Array.Sort(kept);
            omitted = count - cap;
            return kept;
        }

        private static void AddTo(Dictionary<long, VolumeBin> map, long tick, double bid, double ask)
        {
            VolumeBin bin;
            if (!map.TryGetValue(tick, out bin))
            {
                bin = new VolumeBin();
                map.Add(tick, bin);
            }
            bin.Bid += bid;
            bin.Ask += ask;
        }

        private string Classification()
        {
            return _sawAtQuote ? "bid_ask_at_quote_else_tick_rule" : "tick_rule_only";
        }

        private static long ToTick(double price, double tickSize)
        {
            return (long)Math.Round(price / tickSize, MidpointRounding.AwayFromZero);
        }

        private static DateTime TruncateToMinute(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
            return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
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
                if (ch == '\\' || ch == '"')
                    sb.Append('\\');
                sb.Append(ch);
            }
            sb.Append('"');
            return sb.ToString();
        }

        internal static string FormatVolume(double value)
        {
            double rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
            double whole = Math.Round(rounded, 0, MidpointRounding.AwayFromZero);
            if (Math.Abs(rounded - whole) < 0.0000001)
                return whole.ToString("0", CultureInfo.InvariantCulture);
            return rounded.ToString("0.0", CultureInfo.InvariantCulture);
        }

        internal static string FormatPrice(double value)
        {
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private sealed class VolumeBin
        {
            public double Bid;
            public double Ask;
        }

        private sealed class ClosedBar
        {
            public DateTime Utc;
            public long[] Ticks;
            public double[] Bid;
            public double[] Ask;
            public bool Truncated;
            public int Omitted;
        }
    }
}
