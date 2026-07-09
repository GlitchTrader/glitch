//
//
//   /$$$$$$  /$$ /$$   /$$               /$$      
//  /$$__  $$| $$|__/  | $$              | $$      
// | $$  \__/| $$ /$$ /$$$$$$    /$$$$$$$| $$$$$$$ 
// | $$ /$$$$| $$| $$|_  $$_/   /$$_____/| $$__  $$
// | $$|_  $$| $$| $$  | $$    | $$      | $$  \ $$
// | $$  \ $$| $$| $$  | $$ /$$| $$      | $$  | $$
// |  $$$$$$/| $$| $$  |  $$$$/|  $$$$$$$| $$  | $$
//  \______/ |__/|__/   \___/   \_______/|__/  |__/
//                                                                                                
//
// __________________________________________________
// __________________________________________________
//
//
// Glitch AddOn
// by GlitchTrader.com
//
// __________________________________________________
// __________________________________________________
//

using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace Glitch.UI
{
    internal sealed class GlitchAnalyticsEngine
    {
        private static readonly int[] DefaultTimeframes = { 60, 15, 5, 1 };
        private static readonly TimeSpan MaxFeedAge = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan MaxBridgePresenceAge = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan SnapshotRetentionAge = TimeSpan.FromDays(7);

        internal static double ResolveCanonicalTimeframeScore(GlitchTimeframeReading reading)
        {
            return ResolveCompositeInputScore(reading);
        }

        internal static double ComputeCompositeScore(IEnumerable<GlitchTimeframeReading> readings)
        {
            return ComputeWeightedCompositeScore(readings);
        }

        public IReadOnlyList<string> BuildInstrumentOptions(IEnumerable<Account> accounts, string selectedInstrument)
        {
            DateTime nowUtc = DateTime.UtcNow;
            var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string instrument in GlitchAnalyticsFeedBus.GetActiveInstrumentRoots(nowUtc, MaxFeedAge))
                options.Add(instrument);

            foreach (string instrument in GlitchAnalyticsFeedBus.GetBridgeInstrumentRoots(nowUtc, MaxBridgePresenceAge))
                options.Add(instrument);

            foreach (string instrument in GlitchAnalyticsFeedBus.GetKnownInstrumentRoots())
                options.Add(instrument);

            string normalizedSelected = NormalizeInstrumentRoot(selectedInstrument);
            if (!string.IsNullOrWhiteSpace(normalizedSelected))
                options.Add(normalizedSelected);

            return options
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public GlitchAnalyticsSnapshot BuildSnapshot(string instrumentRoot, IEnumerable<Account> accounts, DateTime nowUtc)
        {
            string normalizedInstrument = NormalizeInstrumentRoot(instrumentRoot);
            SessionWindow sessionWindow = SessionWindow.Resolve(nowUtc.ToLocalTime());
            if (string.IsNullOrWhiteSpace(normalizedInstrument))
                return BuildEmptySnapshot(null, sessionWindow, nowUtc);

            GlitchIndicatorInstrumentSnapshot sourceSnapshot;
            if (!GlitchAnalyticsFeedBus.TryGetSnapshot(normalizedInstrument, out sourceSnapshot) || sourceSnapshot == null)
            {
                GlitchBridgeStatus bridgeStatus;
                if (GlitchAnalyticsFeedBus.TryGetBridgeStatus(
                    normalizedInstrument,
                    nowUtc,
                    MaxBridgePresenceAge,
                    out bridgeStatus) &&
                    bridgeStatus != null)
                {
                    bool requestedBootstrap = GlitchAnalyticsFeedBus.RequestBridgeBootstrapPublish(normalizedInstrument);
                    if (requestedBootstrap &&
                        GlitchAnalyticsFeedBus.TryGetSnapshot(normalizedInstrument, nowUtc, MaxFeedAge, out sourceSnapshot) &&
                        sourceSnapshot != null)
                        return BuildSnapshotFromSource(normalizedInstrument, sessionWindow, nowUtc, sourceSnapshot);

                    string bridgeStatusText = BuildBridgeStatusText(bridgeStatus);
                    if (!requestedBootstrap)
                        bridgeStatusText += " | No live bridge callback detected yet.";
                    return BuildBridgeDetectedSnapshot(normalizedInstrument, sessionWindow, nowUtc, bridgeStatusText);
                }

                return BuildEmptySnapshot(normalizedInstrument, sessionWindow, nowUtc);
            }

            bool isLiveFeed = GlitchAnalyticsFeedBus.IsSnapshotFresh(sourceSnapshot, nowUtc, MaxFeedAge);
            if (!isLiveFeed && !GlitchAnalyticsFeedBus.HasReadingWithinAge(sourceSnapshot, nowUtc, SnapshotRetentionAge))
                return BuildEmptySnapshot(normalizedInstrument, sessionWindow, nowUtc);

            GlitchAnalyticsSnapshot built = BuildSnapshotFromSource(
                normalizedInstrument,
                sessionWindow,
                nowUtc,
                sourceSnapshot);
            if (!isLiveFeed)
            {
                string ageText = FormatAge(nowUtc, sourceSnapshot.UpdatedUtc);
                built.CompositeSignal = "Retained · " + built.CompositeSignal;
                foreach (GlitchTimeframeReading reading in built.TimeframeReadings ?? new List<GlitchTimeframeReading>())
                {
                    if (reading == null || string.Equals(reading.SignalLabel, "Awaiting feed", StringComparison.OrdinalIgnoreCase))
                        continue;

                    reading.TrendHint = string.IsNullOrWhiteSpace(reading.TrendHint)
                        ? "Last chart feed " + ageText
                        : reading.TrendHint + " | Last chart feed " + ageText;
                }
            }

            return built;
        }

        private GlitchAnalyticsSnapshot BuildSnapshotFromSource(
            string normalizedInstrument,
            SessionWindow sessionWindow,
            DateTime nowUtc,
            GlitchIndicatorInstrumentSnapshot sourceSnapshot)
        {
            var timeframeReadings = new List<GlitchTimeframeReading>(DefaultTimeframes.Length);
            var activeReadings = new List<GlitchTimeframeReading>(DefaultTimeframes.Length);
            foreach (int timeframe in DefaultTimeframes)
            {
                GlitchIndicatorReading sourceReading;
                if (sourceSnapshot.TimeframeReadings != null &&
                    sourceSnapshot.TimeframeReadings.TryGetValue(timeframe, out sourceReading) &&
                    sourceReading != null)
                {
                    GlitchTimeframeReading reading = ToTimeframeReading(timeframe, sourceReading);
                    timeframeReadings.Add(reading);
                    if (IsReadingFresh(sourceReading, nowUtc, MaxFeedAge))
                        activeReadings.Add(reading);
                    continue;
                }

                timeframeReadings.Add(BuildAwaitingReading(timeframe));
            }

            double? liveCurrentPrice = ResolveCurrentPrice(sourceSnapshot);
            string liveSessionName = ResolveSessionName(sourceSnapshot);
            double? liveSessionHigh = ResolveSessionHigh(sourceSnapshot);
            double? liveSessionLow = ResolveSessionLow(sourceSnapshot);
            double? livePreviousSessionHigh = ResolvePreviousSessionHigh(sourceSnapshot);
            double? livePreviousSessionLow = ResolvePreviousSessionLow(sourceSnapshot);

            double compositeScore = ComputeWeightedCompositeScore(activeReadings);
            return new GlitchAnalyticsSnapshot
            {
                InstrumentRoot = normalizedInstrument,
                CurrentPrice = liveCurrentPrice,
                SessionName = string.IsNullOrWhiteSpace(liveSessionName)
                    ? sessionWindow.Name
                    : liveSessionName,
                SessionHigh = liveSessionHigh,
                SessionLow = liveSessionLow,
                PreviousSessionHigh = livePreviousSessionHigh,
                PreviousSessionLow = livePreviousSessionLow,
                CompositeScore = compositeScore,
                CompositeSignal = GlitchSignalScale.ToLabel(compositeScore),
                TimeframeReadings = timeframeReadings,
                ScoreSectionTitle = "Instrument Overview",
                NewsSentiment = null,
                EarningsAnalysis = null,
                OfficialNews = null,
                IsNewsEventLockoutActive = false,
                NewsEventLockoutText = null,
                UpdatedUtc = sourceSnapshot.UpdatedUtc
            };
        }

        private static double ComputeWeightedCompositeScore(IEnumerable<GlitchTimeframeReading> readings)
        {
            if (readings == null)
                return 0;

            double weighted = 0;
            double total = 0;
            foreach (GlitchTimeframeReading reading in readings)
            {
                if (reading == null)
                    continue;

                double weight = ResolveCompositeWeight(reading.Minutes);
                if (weight <= 0)
                    continue;

                weighted += ResolveCompositeInputScore(reading) * weight;
                total += weight;
            }

            if (total <= 1e-8)
                return 0;

            return weighted / total;
        }

        private static double ResolveCompositeInputScore(GlitchTimeframeReading reading)
        {
            if (reading == null)
                return 0;

            // ponytail: bridge Score is already the blended publish truth — do not re-mix components here
            return NormalizeCompositeSignal(reading.Score);
        }

        private static bool IsReadingFresh(GlitchIndicatorReading reading, DateTime nowUtc, TimeSpan maxAge)
        {
            if (reading == null || maxAge <= TimeSpan.Zero)
                return false;

            if (reading.UtcTime == default)
                return false;

            return (nowUtc - reading.UtcTime) <= maxAge;
        }

        private static double NormalizeCompositeSignal(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0;
            if (value < -1.0)
                return -1.0;
            if (value > 1.0)
                return 1.0;
            return value;
        }

        private static double? ResolveCurrentPrice(GlitchIndicatorInstrumentSnapshot snapshot)
        {
            GlitchIndicatorReading reading = FindFreshestReading(snapshot, x => x.CurrentPrice.HasValue && x.CurrentPrice.Value > 0);
            if (reading != null && reading.CurrentPrice.HasValue && reading.CurrentPrice.Value > 0)
                return reading.CurrentPrice;

            return snapshot == null || !snapshot.CurrentPrice.HasValue || snapshot.CurrentPrice.Value <= 0
                ? (double?)null
                : snapshot.CurrentPrice;
        }

        private static string ResolveSessionName(GlitchIndicatorInstrumentSnapshot snapshot)
        {
            GlitchIndicatorReading reading = FindFreshestReading(snapshot, x => !string.IsNullOrWhiteSpace(x.SessionName));
            if (reading != null && !string.IsNullOrWhiteSpace(reading.SessionName))
                return reading.SessionName;

            return snapshot == null ? null : snapshot.SessionName;
        }

        private static double? ResolveSessionHigh(GlitchIndicatorInstrumentSnapshot snapshot)
        {
            GlitchIndicatorReading reading = FindFreshestReading(snapshot, x => x.SessionHigh.HasValue);
            return reading != null && reading.SessionHigh.HasValue
                ? reading.SessionHigh
                : snapshot == null ? (double?)null : snapshot.SessionHigh;
        }

        private static double? ResolveSessionLow(GlitchIndicatorInstrumentSnapshot snapshot)
        {
            GlitchIndicatorReading reading = FindFreshestReading(snapshot, x => x.SessionLow.HasValue);
            return reading != null && reading.SessionLow.HasValue
                ? reading.SessionLow
                : snapshot == null ? (double?)null : snapshot.SessionLow;
        }

        private static double? ResolvePreviousSessionHigh(GlitchIndicatorInstrumentSnapshot snapshot)
        {
            GlitchIndicatorReading reading = FindFreshestReading(snapshot, x => x.PreviousSessionHigh.HasValue);
            return reading != null && reading.PreviousSessionHigh.HasValue
                ? reading.PreviousSessionHigh
                : snapshot == null ? (double?)null : snapshot.PreviousSessionHigh;
        }

        private static double? ResolvePreviousSessionLow(GlitchIndicatorInstrumentSnapshot snapshot)
        {
            GlitchIndicatorReading reading = FindFreshestReading(snapshot, x => x.PreviousSessionLow.HasValue);
            return reading != null && reading.PreviousSessionLow.HasValue
                ? reading.PreviousSessionLow
                : snapshot == null ? (double?)null : snapshot.PreviousSessionLow;
        }

        private static GlitchIndicatorReading FindFreshestReading(
            GlitchIndicatorInstrumentSnapshot snapshot,
            Func<GlitchIndicatorReading, bool> predicate)
        {
            if (snapshot == null || snapshot.TimeframeReadings == null || snapshot.TimeframeReadings.Count == 0)
                return null;

            return snapshot.TimeframeReadings.Values
                .Where(x => x != null && (predicate == null || predicate(x)))
                .OrderByDescending(x => x.UtcTime)
                .ThenBy(x => x.Minutes)
                .FirstOrDefault();
        }

        private static double ResolveCompositeWeight(int minutes)
        {
            if (minutes <= 1)
                return 0.45;
            if (minutes <= 5)
                return 0.30;
            if (minutes <= 15)
                return 0.17;
            return 0.08;
        }

        private static GlitchTimeframeReading ToTimeframeReading(int timeframeMinutes, GlitchIndicatorReading source)
        {
            if (source == null)
                return BuildAwaitingReading(timeframeMinutes);

            return new GlitchTimeframeReading
            {
                Minutes = timeframeMinutes,
                AveragePrice = source.AveragePrice,
                AtrProxy = source.Atr,
                AdxProxy = source.Adx,
                Score = source.Score,
                RawScore = source.RawScore ?? source.Score,
                DirectionalScore = source.DirectionalScore ?? source.Score,
                TradeabilityScore = source.TradeabilityScore,
                SignalLabel = string.IsNullOrWhiteSpace(source.SignalLabel)
                    ? GlitchSignalScale.ToLabel(source.Score)
                    : source.SignalLabel,
                VolatilityHint = source.VolatilityHint,
                TrendHint = source.TrendHint,
                RegimeLabel = source.RegimeLabel,
                NoTradeReasons = source.NoTradeReasons,
                Rsi = source.Rsi,
                StochK = source.StochK,
                ZScore = source.ZScore,
                EmaAlignment = source.EmaAlignment,
                RegimeWeight = source.RegimeWeight,
                OscillatorCompositeScore = source.OscillatorCompositeScore,
                MaCompositeScore = source.MaCompositeScore,
                OrderFlowScore = source.OrderFlowScore,
                OrderFlowConfidence = source.OrderFlowConfidence,
                OrderFlowReliability = source.OrderFlowReliability,
                OrderFlowCumulativeDelta = source.OrderFlowCumulativeDelta,
                OrderFlowDeltaChange = source.OrderFlowDeltaChange,
                OrderFlowVwap = source.OrderFlowVwap,
                OrderFlowVwapDeviation = source.OrderFlowVwapDeviation,
                OrderFlowAggressionBalance = source.OrderFlowAggressionBalance,
                OrderFlowDepthImbalance = source.OrderFlowDepthImbalance,
                OrderFlowHint = source.OrderFlowHint
            };
        }

        private static GlitchAnalyticsSnapshot BuildEmptySnapshot(string instrumentRoot, SessionWindow sessionWindow, DateTime nowUtc)
        {
            var timeframeReadings = new List<GlitchTimeframeReading>(DefaultTimeframes.Length);
            foreach (int timeframe in DefaultTimeframes)
                timeframeReadings.Add(BuildAwaitingReading(timeframe));

            return new GlitchAnalyticsSnapshot
            {
                InstrumentRoot = instrumentRoot,
                CurrentPrice = null,
                SessionName = sessionWindow.Name,
                SessionHigh = null,
                SessionLow = null,
                PreviousSessionHigh = null,
                PreviousSessionLow = null,
                CompositeScore = 0,
                CompositeSignal = GlitchSignalScale.ToLabel(0),
                TimeframeReadings = timeframeReadings,
                ScoreSectionTitle = "Instrument Overview",
                NewsSentiment = null,
                EarningsAnalysis = null,
                OfficialNews = null,
                IsNewsEventLockoutActive = false,
                NewsEventLockoutText = null,
                UpdatedUtc = nowUtc
            };
        }

        private static GlitchAnalyticsSnapshot BuildStaleSnapshot(
            string instrumentRoot,
            SessionWindow sessionWindow,
            DateTime nowUtc,
            DateTime lastUpdateUtc)
        {
            string staleText = "Feed stale. Last update " + FormatAge(nowUtc, lastUpdateUtc) +
                               ". Re-attach/enable GlitchAnalyticsBridge on a live chart.";

            var timeframeReadings = new List<GlitchTimeframeReading>(DefaultTimeframes.Length);
            foreach (int timeframe in DefaultTimeframes)
                timeframeReadings.Add(BuildAwaitingReading(timeframe, staleText, "No live chart feed detected."));

            return new GlitchAnalyticsSnapshot
            {
                InstrumentRoot = instrumentRoot,
                CurrentPrice = null,
                SessionName = sessionWindow.Name,
                SessionHigh = null,
                SessionLow = null,
                PreviousSessionHigh = null,
                PreviousSessionLow = null,
                CompositeScore = 0,
                CompositeSignal = "Stale",
                TimeframeReadings = timeframeReadings,
                ScoreSectionTitle = "Instrument Overview",
                NewsSentiment = null,
                EarningsAnalysis = null,
                OfficialNews = null,
                IsNewsEventLockoutActive = false,
                NewsEventLockoutText = null,
                UpdatedUtc = nowUtc
            };
        }

        private static GlitchAnalyticsSnapshot BuildBridgeDetectedSnapshot(
            string instrumentRoot,
            SessionWindow sessionWindow,
            DateTime nowUtc,
            string bridgeStatusText)
        {
            string trendHint = string.IsNullOrWhiteSpace(bridgeStatusText)
                ? "Bridge detected. Waiting for first publish."
                : bridgeStatusText;

            var timeframeReadings = new List<GlitchTimeframeReading>(DefaultTimeframes.Length);
            foreach (int timeframe in DefaultTimeframes)
            {
                timeframeReadings.Add(BuildAwaitingReading(
                    timeframe,
                    "Bridge live. Waiting for valid samples.",
                    trendHint));
            }

            return new GlitchAnalyticsSnapshot
            {
                InstrumentRoot = instrumentRoot,
                CurrentPrice = null,
                SessionName = sessionWindow.Name,
                SessionHigh = null,
                SessionLow = null,
                PreviousSessionHigh = null,
                PreviousSessionLow = null,
                CompositeScore = 0,
                CompositeSignal = GlitchSignalScale.ToLabel(0),
                TimeframeReadings = timeframeReadings,
                ScoreSectionTitle = "Instrument Overview",
                NewsSentiment = null,
                EarningsAnalysis = null,
                OfficialNews = null,
                IsNewsEventLockoutActive = false,
                NewsEventLockoutText = null,
                UpdatedUtc = nowUtc
            };
        }

        private static string BuildBridgeStatusText(GlitchBridgeStatus status)
        {
            if (status == null)
                return "Bridge detected. Waiting for first publish.";

            if (!status.PublishToGlitchUi)
                return "Bridge is attached but 'Publish To Glitch UI' is disabled.";

            if (!status.IsTrackedPrimaryTimeframe)
                return "Bridge is attached on an unsupported timeframe. Use 1/5/15/60 minute chart.";

            return "Bridge live. Waiting for first valid signal publish (needs warm-up bars).";
        }

        private static GlitchTimeframeReading BuildAwaitingReading(int timeframeMinutes)
        {
            return BuildAwaitingReading(
                timeframeMinutes,
                "Attach GlitchAnalyticsBridge to a chart for this instrument.",
                "No live chart feed detected.");
        }

        private static GlitchTimeframeReading BuildAwaitingReading(
            int timeframeMinutes,
            string volatilityHint,
            string trendHint)
        {
            return new GlitchTimeframeReading
            {
                Minutes = timeframeMinutes,
                AveragePrice = null,
                AtrProxy = null,
                AdxProxy = null,
                Score = 0,
                SignalLabel = "Awaiting feed",
                VolatilityHint = volatilityHint,
                TrendHint = trendHint
            };
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

        private static string FormatAge(DateTime nowUtc, DateTime lastUpdateUtc)
        {
            if (lastUpdateUtc <= DateTime.MinValue)
                return "unknown";

            TimeSpan age = nowUtc - lastUpdateUtc;
            if (age.TotalSeconds < 0)
                age = TimeSpan.Zero;

            if (age.TotalMinutes < 1)
                return Math.Round(age.TotalSeconds).ToString("N0") + "s ago";
            if (age.TotalHours < 1)
                return Math.Round(age.TotalMinutes).ToString("N0") + "m ago";
            if (age.TotalDays < 1)
                return Math.Round(age.TotalHours, 1).ToString("N1") + "h ago";
            return Math.Round(age.TotalDays, 1).ToString("N1") + "d ago";
        }

        private struct SessionWindow
        {
            public string Name { get; }

            private SessionWindow(string name)
            {
                Name = name;
            }

            public static SessionWindow Resolve(DateTime nowLocal)
            {
                int hour = nowLocal.Hour;
                if (hour >= 8 && hour < 16)
                    return new SessionWindow("NYC");
                if (hour >= 3 && hour < 8)
                    return new SessionWindow("London");
                return new SessionWindow("Asia");
            }
        }
    }

    internal sealed class GlitchAnalyticsSnapshot
    {
        public string InstrumentRoot { get; set; }
        public double? CurrentPrice { get; set; }
        public string SessionName { get; set; }
        public double? SessionHigh { get; set; }
        public double? SessionLow { get; set; }
        public double? PreviousSessionHigh { get; set; }
        public double? PreviousSessionLow { get; set; }
        public double CompositeScore { get; set; }
        public string CompositeSignal { get; set; }
        public IReadOnlyList<GlitchTimeframeReading> TimeframeReadings { get; set; }
        public string NewsSentiment { get; set; }
        public string EarningsAnalysis { get; set; }
        public string OfficialNews { get; set; }
        public string ScoreSectionTitle { get; set; }
        public bool IsNewsEventLockoutActive { get; set; }
        public string NewsEventLockoutText { get; set; }
        public IReadOnlyList<string> Mag7ScoreLines { get; set; }
        public IReadOnlyList<string> LatestHeadlineLines { get; set; }
        public IReadOnlyList<string> OfficialNewsLines { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    internal sealed class GlitchTimeframeReading
    {
        public int Minutes { get; set; }
        public double? AveragePrice { get; set; }
        public double? AtrProxy { get; set; }
        public double? AdxProxy { get; set; }
        public double Score { get; set; }
        public double RawScore { get; set; }
        public double DirectionalScore { get; set; }
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
    }

    internal static class GlitchSignalScale
    {
        public static string ToLabel(double score)
        {
            if (score <= -0.75)
                return "Strong Sell";
            if (score <= -0.35)
                return "Sell";
            if (score <= -0.10)
                return "Weak Sell";
            if (score < 0.10)
                return "Neutral";
            if (score < 0.35)
                return "Weak Buy";
            if (score < 0.75)
                return "Buy";
            return "Strong Buy";
        }
    }

    internal struct PriceRange
    {
        public static PriceRange Empty => new PriceRange(null, null);

        public PriceRange(double? high, double? low)
        {
            High = high;
            Low = low;
        }

        public double? High { get; }
        public double? Low { get; }
    }
}
