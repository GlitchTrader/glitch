//
// GlitchAiMarketIngest — lightweight multi-instrument feed for Hermes snapshots.
// Keep GlitchAnalyticsBridge on the trade chart (single instrument, full UI bridge).
//

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class GlitchAiMarketIngest : Indicator
    {
        private static readonly int[] PrimaryTargetMinutes = { 1, 5, 15, 60 };
        private const int MinBarsForReading = 30;

        private string[] _rootByBip;
        private int[] _minutesByBip;
        private bool[] _isTrackedByBip;
        private ATR[] _atrByBip;
        private ADX[] _adxByBip;
        private RSI[] _rsiByBip;
        private EMA[] _emaFastByBip;
        private EMA[] _emaMedByBip;
        private SMA[] _smaByBip;

        private readonly Dictionary<string, SessionTracker> _sessionByKey = new Dictionary<string, SessionTracker>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _registeredRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Action> _bootstrapPublisherByRoot = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastTouchUtc = DateTime.MinValue;

        [NinjaScriptProperty]
        [Display(Name = "Add Primary Timeframes", Order = 1, GroupName = "Parameters")]
        public bool AddPrimaryTimeframes { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Publishes lightweight multi-instrument readings for Glitch AI snapshots.";
                Name = "GlitchAiMarketIngest";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                IsSuspendedWhileInactive = true;
                AddPrimaryTimeframes = true;
            }
            else if (State == State.Configure)
            {
                if (AddPrimaryTimeframes)
                    AddMissingPrimaryTimeframeSeries();
            }
            else if (State == State.DataLoaded)
            {
                InitializeSeriesMetadata();
                InitializeIndicators();
                RegisterKnownRoots();
                LogLoadedSeries();
            }
            else if (State == State.Realtime)
            {
                RegisterKnownRoots();
                PublishAllTrackedReadings();
            }
            else if (State == State.Terminated)
            {
                UnregisterKnownRoots();
                _sessionByKey.Clear();
                _rootByBip = null;
                _minutesByBip = null;
                _isTrackedByBip = null;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars == null || BarsInProgress < 0 || BarsInProgress >= CurrentBars.Length)
                return;

            if (_isTrackedByBip == null || BarsInProgress >= _isTrackedByBip.Length || !_isTrackedByBip[BarsInProgress])
                return;

            if (CurrentBars[BarsInProgress] < MinBarsForReading)
                return;

            TouchRegisteredRoots();
            TryPublishReading(BarsInProgress);
        }

        private void AddMissingPrimaryTimeframeSeries()
        {
            int primaryMinutes = ResolveMinutes(BarsPeriod.BarsPeriodType, BarsPeriod.Value);
            for (int i = 0; i < PrimaryTargetMinutes.Length; i++)
            {
                int minutes = PrimaryTargetMinutes[i];
                if (minutes == primaryMinutes)
                    continue;

                AddDataSeries(BarsPeriodType.Minute, minutes);
            }
        }

        private void InitializeSeriesMetadata()
        {
            if (BarsArray == null)
            {
                _rootByBip = null;
                _minutesByBip = null;
                _isTrackedByBip = null;
                return;
            }

            int seriesCount = BarsArray.Length;
            _rootByBip = new string[seriesCount];
            _minutesByBip = new int[seriesCount];
            _isTrackedByBip = new bool[seriesCount];

            for (int bip = 0; bip < seriesCount; bip++)
            {
                Bars bars = BarsArray[bip];
                int minutes = -1;
                if (bars != null && bars.BarsPeriod != null)
                    minutes = ResolveMinutes(bars.BarsPeriod.BarsPeriodType, bars.BarsPeriod.Value);

                _minutesByBip[bip] = minutes;
                _isTrackedByBip[bip] = IsTrackedMinutesForBip(bip, minutes);
                _rootByBip[bip] = ResolveInstrumentRootForBip(bip);
            }
        }

        private void InitializeIndicators()
        {
            if (BarsArray == null)
                return;

            int seriesCount = BarsArray.Length;
            _atrByBip = new ATR[seriesCount];
            _adxByBip = new ADX[seriesCount];
            _rsiByBip = new RSI[seriesCount];
            _emaFastByBip = new EMA[seriesCount];
            _emaMedByBip = new EMA[seriesCount];
            _smaByBip = new SMA[seriesCount];

            for (int bip = 0; bip < seriesCount; bip++)
            {
                if (!_isTrackedByBip[bip])
                    continue;

                _atrByBip[bip] = ATR(BarsArray[bip], 14);
                _adxByBip[bip] = ADX(BarsArray[bip], 14);
                _rsiByBip[bip] = RSI(BarsArray[bip], 14, 3);
                _emaFastByBip[bip] = EMA(BarsArray[bip], 12);
                _emaMedByBip[bip] = EMA(BarsArray[bip], 26);
                _smaByBip[bip] = SMA(BarsArray[bip], 20);
            }
        }

        private bool IsTrackedMinutesForBip(int bip, int minutes)
        {
            if (minutes <= 0)
                return false;

            if (bip == 0)
                return IsPrimaryTargetMinutes(minutes);

            return minutes == 1;
        }

        private static bool IsPrimaryTargetMinutes(int minutes)
        {
            for (int i = 0; i < PrimaryTargetMinutes.Length; i++)
            {
                if (PrimaryTargetMinutes[i] == minutes)
                    return true;
            }

            return false;
        }

        private void RegisterKnownRoots()
        {
            if (_rootByBip == null)
                return;

            for (int bip = 0; bip < _rootByBip.Length; bip++)
            {
                string root = _rootByBip[bip];
                if (string.IsNullOrWhiteSpace(root) || !_registeredRoots.Add(root))
                    continue;

                GlitchBridgeBusCompat.RegisterBridge(root, true);
                Action publisher = RequestBootstrapPublish;
                _bootstrapPublisherByRoot[root] = publisher;
                GlitchBridgeBusCompat.RegisterBridgeBootstrapPublisher(root, publisher);
            }
        }

        private void UnregisterKnownRoots()
        {
            foreach (KeyValuePair<string, Action> entry in _bootstrapPublisherByRoot)
            {
                GlitchBridgeBusCompat.UnregisterBridgeBootstrapPublisher(entry.Key, entry.Value);
                GlitchBridgeBusCompat.UnregisterBridge(entry.Key);
            }

            _bootstrapPublisherByRoot.Clear();
            _registeredRoots.Clear();
        }

        private void RequestBootstrapPublish()
        {
            try
            {
                TriggerCustomEvent(_ => PublishAllTrackedReadings(), null);
            }
            catch
            {
                PublishAllTrackedReadings();
            }
        }

        private void TouchRegisteredRoots()
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (_lastTouchUtc != DateTime.MinValue && (nowUtc - _lastTouchUtc) < TimeSpan.FromSeconds(1))
                return;

            _lastTouchUtc = nowUtc;
            foreach (string root in _registeredRoots)
                GlitchBridgeBusCompat.TouchBridge(root, true, true);
        }

        private void TryPublishReading(int bip)
        {
            if (!GlitchBridgeBusCompat.IsAvailable())
                return;

            int minutes = _minutesByBip[bip];
            DateTime nowUtc = DateTime.UtcNow;
            GlitchBridgeBusCompat.BridgeReading reading = BuildReading(bip, minutes, nowUtc);
            if (reading == null)
                return;

            GlitchBridgeBusCompat.Publish(reading);
        }

        private void PublishAllTrackedReadings()
        {
            if (!GlitchBridgeBusCompat.IsAvailable() || BarsArray == null || CurrentBars == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            for (int bip = 0; bip < BarsArray.Length; bip++)
            {
                if (!_isTrackedByBip[bip] || CurrentBars[bip] < MinBarsForReading)
                    continue;

                GlitchBridgeBusCompat.BridgeReading reading = BuildReading(bip, _minutesByBip[bip], nowUtc);
                if (reading != null)
                    GlitchBridgeBusCompat.Publish(reading);
            }
        }

        private GlitchBridgeBusCompat.BridgeReading BuildReading(int bip, int minutes, DateTime nowUtc)
        {
            string root = _rootByBip[bip];
            if (string.IsNullOrWhiteSpace(root))
                return null;

            double close = Closes[bip][0];
            if (close <= 0)
                return null;

            double atr = _atrByBip[bip][0];
            double adx = _adxByBip[bip][0];
            double rsi = _rsiByBip[bip][0];
            double emaFast = _emaFastByBip[bip][0];
            double emaMed = _emaMedByBip[bip][0];
            double averagePrice = _smaByBip[bip][0];

            double emaAlignment = BuildEmaAlignment(close, emaFast, emaMed);
            double rsiSignal = Clamp((rsi - 50.0) / 22.0, -1, 1);
            double directionalScore = Clamp((emaAlignment * 0.55) + (rsiSignal * 0.45), -1, 1);
            double tradeabilityScore = Clamp((adx - 12.0) / 28.0, 0, 1);
            double score = Clamp(directionalScore * (0.65 + (tradeabilityScore * 0.35)), -1, 1);

            SessionTracker session = UpdateSessionTracker(root, minutes, bip);
            DateTime readingUtc = nowUtc;
            if (Times != null && bip < Times.Length && Times[bip] != null && Times[bip].Count > 0)
            {
                DateTime barTime = Times[bip][0];
                if (barTime != DateTime.MinValue)
                    readingUtc = barTime.ToUniversalTime();
            }

            return new GlitchBridgeBusCompat.BridgeReading
            {
                InstrumentRoot = root,
                Minutes = minutes,
                UtcTime = readingUtc,
                CurrentPrice = close,
                AveragePrice = averagePrice,
                Atr = atr,
                Adx = adx,
                Score = score,
                RawScore = directionalScore,
                DirectionalScore = directionalScore,
                TradeabilityScore = tradeabilityScore,
                SignalLabel = ToSignalLabel(score),
                VolatilityHint = BuildVolatilityHint(atr, close),
                TrendHint = BuildTrendHint(adx, rsi),
                RegimeLabel = BuildRegimeLabel(adx, directionalScore),
                Rsi = rsi,
                EmaAlignment = emaAlignment,
                RegimeWeight = tradeabilityScore,
                SessionName = session.Name,
                SessionHigh = session.CurrentHigh,
                SessionLow = session.CurrentLow,
                PreviousSessionHigh = session.PreviousHigh,
                PreviousSessionLow = session.PreviousLow
            };
        }

        private SessionTracker UpdateSessionTracker(string root, int minutes, int bip)
        {
            string key = root + "|" + minutes.ToString(CultureInfo.InvariantCulture);
            SessionTracker tracker;
            if (!_sessionByKey.TryGetValue(key, out tracker))
            {
                tracker = new SessionTracker();
                _sessionByKey[key] = tracker;
            }

            double high = Highs[bip][0];
            double low = Lows[bip][0];
            SessionBlock block = SessionBlock.Resolve(Times[bip][0]);
            tracker.Update(block.Key, block.Name, high, low);
            return tracker;
        }

        private void LogLoadedSeries()
        {
            if (_rootByBip == null)
                return;

            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int bip = 0; bip < _rootByBip.Length; bip++)
            {
                if (!_isTrackedByBip[bip])
                    continue;

                string root = _rootByBip[bip];
                if (!string.IsNullOrWhiteSpace(root))
                    roots.Add(root);
            }

            string rootList = roots.Count == 0 ? "(none)" : string.Join(", ", roots);
            Print(string.Format(
                CultureInfo.InvariantCulture,
                "GlitchAiMarketIngest data loaded: {0} bar series, {1} instrument root(s): {2}.",
                BarsArray == null ? 0 : BarsArray.Length,
                roots.Count,
                rootList));
        }

        private string ResolveInstrumentRootForBip(int bip)
        {
            if (BarsArray == null || bip < 0 || bip >= BarsArray.Length)
                return null;

            Bars bars = BarsArray[bip];
            if (bars == null)
                return null;

            Instrument instrument = bars.Instrument;
            if (instrument == null)
                return null;

            string raw = instrument.MasterInstrument == null
                ? instrument.FullName
                : instrument.MasterInstrument.Name;

            return NormalizeInstrumentRoot(raw);
        }

        private static string NormalizeInstrumentRoot(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string normalized = value.Trim();
            int spaceIndex = normalized.IndexOf(' ');
            if (spaceIndex > 0)
                normalized = normalized.Substring(0, spaceIndex);

            int dotIndex = normalized.IndexOf('.');
            if (dotIndex > 0)
                normalized = normalized.Substring(0, dotIndex);

            return normalized.Trim().ToUpperInvariant();
        }

        private static int ResolveMinutes(BarsPeriodType periodType, int value)
        {
            if (periodType == BarsPeriodType.Minute && value > 0)
                return value;
            return -1;
        }

        private static double BuildEmaAlignment(double close, double emaFast, double emaMed)
        {
            if (close <= 0)
                return 0;

            double fastBias = (close - emaFast) / Math.Max(Math.Abs(emaFast), 1e-8);
            double medBias = (close - emaMed) / Math.Max(Math.Abs(emaMed), 1e-8);
            return Clamp((fastBias * 0.6) + (medBias * 0.4), -1, 1);
        }

        private static string ToSignalLabel(double score)
        {
            if (score >= 0.35)
                return "Buy";
            if (score <= -0.35)
                return "Sell";
            return "Neutral";
        }

        private static string BuildVolatilityHint(double atr, double close)
        {
            if (close <= 0)
                return "Unknown";

            double ratio = atr / close;
            if (ratio >= 0.0045)
                return "High";
            if (ratio >= 0.0025)
                return "Moderate";
            return "Low";
        }

        private static string BuildTrendHint(double adx, double rsi)
        {
            if (adx >= 25)
                return rsi >= 55 ? "Uptrend" : rsi <= 45 ? "Downtrend" : "Trending";
            return "Range";
        }

        private static string BuildRegimeLabel(double adx, double directionalScore)
        {
            if (adx < 18)
                return "Range";

            return directionalScore >= 0 ? "Bull" : "Bear";
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private sealed class SessionTracker
        {
            public string Name { get; private set; }
            public double? CurrentHigh { get; private set; }
            public double? CurrentLow { get; private set; }
            public double? PreviousHigh { get; private set; }
            public double? PreviousLow { get; private set; }
            private string _sessionKey;

            public void Update(string sessionKey, string name, double high, double low)
            {
                if (string.IsNullOrWhiteSpace(sessionKey))
                    return;

                if (!string.Equals(_sessionKey, sessionKey, StringComparison.Ordinal))
                {
                    if (CurrentHigh.HasValue && CurrentLow.HasValue)
                    {
                        PreviousHigh = CurrentHigh;
                        PreviousLow = CurrentLow;
                    }

                    _sessionKey = sessionKey;
                    Name = name;
                    CurrentHigh = high;
                    CurrentLow = low;
                    return;
                }

                if (!CurrentHigh.HasValue || high > CurrentHigh.Value)
                    CurrentHigh = high;
                if (!CurrentLow.HasValue || low < CurrentLow.Value)
                    CurrentLow = low;
            }
        }

        private struct SessionBlock
        {
            public SessionBlock(string name, string key)
            {
                Name = name;
                Key = key;
            }

            public string Name { get; }
            public string Key { get; }

            public static SessionBlock Resolve(DateTime barTimeLocal)
            {
                DateTime day = barTimeLocal.Date;
                int hour = barTimeLocal.Hour;

                if (hour >= 8 && hour < 16)
                    return new SessionBlock("NYC", "NYC|" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                if (hour >= 3 && hour < 8)
                    return new SessionBlock("London", "London|" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                if (hour >= 16)
                    return new SessionBlock("Asia", "Asia|" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));

                return new SessionBlock("Asia", "Asia|" + day.AddDays(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            }
        }
    }
}
