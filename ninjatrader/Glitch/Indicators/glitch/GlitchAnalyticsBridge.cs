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
// GlitchAnalyticsBridge
// by GlitchTrader.com
//
// __________________________________________________
// __________________________________________________
//

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class GlitchAnalyticsBridge : Indicator
    {
        private static readonly int[] TargetMinutes = { 1, 5, 15, 60 };
        private const int MinBarsForSignal = 30;
        private const int ZLookback = 30;
        private const int PaletteLevels = 41;
        private const int OrderFlowDepthLevels = 6;
        private static readonly TimeSpan OrderFlowTapeWindow = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan OrderFlowDepthFreshness = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan BootstrapPrimaryMaxAge = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan BootstrapFutureTolerance = TimeSpan.FromMinutes(1);

        private EMA[] _emaFastByBip;
        private EMA[] _emaMedByBip;
        private EMA[] _emaSlowByBip;
        private EMA[] _ema13ByBip;
        private EMA[] _ema10ByBip;
        private EMA[] _ema20ByBip;
        private EMA[] _ema30ByBip;
        private EMA[] _ema50ByBip;
        private EMA[] _ema100ByBip;
        private EMA[] _ema200ByBip;
        private ATR[] _atrByBip;
        private ADX[] _adxByBip;
        private DM[] _dmByBip;
        private RSI[] _rsiByBip;
        private Stochastics[] _stochByBip;
        private StochRSI[] _stochRsiByBip;
        private MACD[] _macdByBip;
        private CCI[] _cciByBip;
        private Momentum[] _momentumByBip;
        private WilliamsR[] _williamsRByBip;
        private UltimateOscillator[] _ultimateOscillatorByBip;
        private HMA[] _hma9ByBip;
        private SMA[] _sma10ByBip;
        private SMA[] _smaByBip;
        private SMA[] _sma30ByBip;
        private SMA[] _sma50ByBip;
        private SMA[] _sma100ByBip;
        private SMA[] _sma200ByBip;

        private Brush _neutralBrush;
        private Brush[] _buyPalette;
        private Brush[] _sellPalette;

        private readonly Dictionary<int, DateTime> _lastPublishUtcByMinutes = new Dictionary<int, DateTime>();
        private readonly Dictionary<int, SessionTracker> _sessionByMinutes = new Dictionary<int, SessionTracker>();
        private string _instrumentRoot;
        private int[] _minutesByBip;
        private bool[] _isTrackedByBip;
        private int _lastPaintedBarIndex = -1;
        private int _lastPaintedColorKey = int.MinValue;
        private int _lastColorRegime;
        private DateTime _lastBridgeUnavailableLogUtc = DateTime.MinValue;
        private DateTime _lastBridgeTouchUtc = DateTime.MinValue;
        private DateTime _lastBootstrapRegisterUtc = DateTime.MinValue;
        private double _tickSize;
        private SignalSnapshot[] _cachedSignalByBip;
        private bool[] _hasCachedSignalByBip;
        private OrderFlowCumulativeDelta[] _orderFlowDeltaByBip;
        private OrderFlowVWAP[] _orderFlowVwapByBip;
        private bool _isOrderFlowRuntimeAvailable;
        private bool _hasLoggedOrderFlowUnavailable;
        private int _orderFlowTickBip = -1;

        private readonly Queue<OrderFlowTapeSample> _orderFlowTape = new Queue<OrderFlowTapeSample>();
        private double _orderFlowTapeBuyVolume;
        private double _orderFlowTapeSellVolume;
        private DateTime _lastOrderFlowTapeUtc = DateTime.MinValue;
        private readonly double[] _depthBidByLevel = new double[OrderFlowDepthLevels];
        private readonly double[] _depthAskByLevel = new double[OrderFlowDepthLevels];
        private DateTime _lastDepthUpdateUtc = DateTime.MinValue;
        private double _lastBidPrice;
        private double _lastAskPrice;
        private double _lastTradePrice;

        [NinjaScriptProperty]
        [Range(0.00, 0.60)]
        [Display(Name = "Neutral Band", Order = 1, GroupName = "Parameters")]
        public double NeutralBand { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Bar Coloring", Order = 2, GroupName = "Parameters")]
        public bool EnableBarColoring { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Publish To Glitch UI", Order = 3, GroupName = "Parameters")]
        public bool PublishToGlitchUi { get; set; }

        [NinjaScriptProperty]
        [Range(50, 2000)]
        [Display(Name = "Publish Interval ms", Order = 4, GroupName = "Parameters")]
        public int PublishIntervalMs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Intra-bar Coloring", Order = 5, GroupName = "Parameters")]
        public bool IntraBarColoring { get; set; }

        [NinjaScriptProperty]
        [Range(0.00, 1.00)]
        [Display(Name = "Predictive Boost", Order = 6, GroupName = "Parameters")]
        public double PredictiveBoost { get; set; }

        [NinjaScriptProperty]
        [Range(0.00, 0.25)]
        [Display(Name = "Flip Hysteresis", Order = 7, GroupName = "Parameters")]
        public double FlipHysteresis { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Performance Mode", Order = 8, GroupName = "Parameters")]
        public bool PerformanceMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Order Flow Layer", Order = 9, GroupName = "Parameters")]
        public bool EnableOrderFlowLayer { get; set; }

        [NinjaScriptProperty]
        [Range(0.00, 0.80)]
        [Display(Name = "Order Flow Blend", Order = 10, GroupName = "Parameters")]
        public double OrderFlowBlend { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Historical Snapshot Export", Order = 1, GroupName = "Historical Export")]
        public bool EnableHistoricalSnapshotExport { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Historical Export Directory", Order = 2, GroupName = "Historical Export")]
        public string HistoricalExportDirectory { get; set; }

        private int _historicalExportCount;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Publishes live multi-timeframe analytics for the Glitch add-on and colors bars by regime.";
                Name = "GlitchAnalyticsBridge";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                // ponytail: do not assign IsSuspendedWhileInactive here — NT throws if reapplied on reload; default keeps bridge active enough for feed bus touch/bootstrap.
                NeutralBand = 0.01;
                EnableBarColoring = true;
                PublishToGlitchUi = true;
                PublishIntervalMs = 750;
                IntraBarColoring = false;
                PredictiveBoost = 0.35;
                FlipHysteresis = 0.03;
                PerformanceMode = true;
                EnableOrderFlowLayer = false;
                OrderFlowBlend = 0.8;
                EnableHistoricalSnapshotExport = false;
                HistoricalExportDirectory = string.Empty;
            }
            else if (State == State.Configure)
            {
                if (PublishToGlitchUi || EnableHistoricalSnapshotExport)
                    AddMissingTimeframeSeries();

                if (EnableOrderFlowLayer)
                    AddOrderFlowTickSeries();
            }
            else if (State == State.DataLoaded)
            {
                _instrumentRoot = NormalizeInstrumentRoot(
                    Instrument == null
                        ? null
                        : (Instrument.MasterInstrument == null ? Instrument.FullName : Instrument.MasterInstrument.Name));

                InitializeSeriesMetadata();
                InitializeIndicators();
                InitializeColorPalettes();
                _lastPublishUtcByMinutes.Clear();
                _sessionByMinutes.Clear();
                _tickSize = (Instrument != null && Instrument.MasterInstrument != null && Instrument.MasterInstrument.TickSize > 0)
                    ? Instrument.MasterInstrument.TickSize
                    : 0.25;
                _cachedSignalByBip = new SignalSnapshot[BarsArray.Length];
                _hasCachedSignalByBip = new bool[BarsArray.Length];
                _lastPaintedBarIndex = -1;
                _lastPaintedColorKey = int.MinValue;
                _lastColorRegime = 0;
                _lastBridgeUnavailableLogUtc = DateTime.MinValue;
                _lastBridgeTouchUtc = DateTime.MinValue;
                _lastBootstrapRegisterUtc = DateTime.MinValue;
                _hasLoggedOrderFlowUnavailable = false;
                _lastOrderFlowTapeUtc = DateTime.MinValue;
                _lastDepthUpdateUtc = DateTime.MinValue;
                _lastBidPrice = 0;
                _lastAskPrice = 0;
                _lastTradePrice = 0;
                _orderFlowTape.Clear();
                _orderFlowTapeBuyVolume = 0;
                _orderFlowTapeSellVolume = 0;
                Array.Clear(_depthBidByLevel, 0, _depthBidByLevel.Length);
                Array.Clear(_depthAskByLevel, 0, _depthAskByLevel.Length);
                _orderFlowTickBip = ResolveOrderFlowTickBip();

                InitializeOrderFlowIndicators();

                GlitchBridgeBusCompat.RegisterBridge(_instrumentRoot, PublishToGlitchUi);
                GlitchBridgeBusCompat.RegisterTradeInstrumentInstance(Instrument);
                GlitchBridgeBusCompat.TouchBridge(
                    _instrumentRoot,
                    PublishToGlitchUi,
                    IsTrackedMinutesForBip(0));
                GlitchBridgeBusCompat.RegisterBridgeBootstrapPublisher(_instrumentRoot, RequestBootstrapFromExternal);
            }
            else if (State == State.Realtime)
            {
                GlitchBridgeBusCompat.RegisterBridge(_instrumentRoot, PublishToGlitchUi);
                GlitchBridgeBusCompat.RegisterTradeInstrumentInstance(Instrument);
                GlitchBridgeBusCompat.TouchBridge(
                    _instrumentRoot,
                    PublishToGlitchUi,
                    IsTrackedMinutesForBip(0));
                GlitchBridgeBusCompat.RegisterBridgeBootstrapPublisher(_instrumentRoot, RequestBootstrapFromExternal);
                PublishBootstrapReadings();
            }
            else if (State == State.Terminated)
            {
                _lastPublishUtcByMinutes.Clear();
                _sessionByMinutes.Clear();
                _minutesByBip = null;
                _isTrackedByBip = null;
                _cachedSignalByBip = null;
                _hasCachedSignalByBip = null;
                _orderFlowDeltaByBip = null;
                _orderFlowVwapByBip = null;
                _ema13ByBip = null;
                _ema10ByBip = null;
                _ema20ByBip = null;
                _ema30ByBip = null;
                _ema50ByBip = null;
                _ema100ByBip = null;
                _ema200ByBip = null;
                _dmByBip = null;
                _stochRsiByBip = null;
                _macdByBip = null;
                _cciByBip = null;
                _momentumByBip = null;
                _williamsRByBip = null;
                _ultimateOscillatorByBip = null;
                _hma9ByBip = null;
                _sma10ByBip = null;
                _sma30ByBip = null;
                _sma50ByBip = null;
                _sma100ByBip = null;
                _sma200ByBip = null;
                _isOrderFlowRuntimeAvailable = false;
                _orderFlowTickBip = -1;
                _orderFlowTape.Clear();
                _orderFlowTapeBuyVolume = 0;
                _orderFlowTapeSellVolume = 0;

                GlitchBridgeBusCompat.UnregisterBridgeBootstrapPublisher(_instrumentRoot, RequestBootstrapFromExternal);
                GlitchBridgeBusCompat.UnregisterBridge(_instrumentRoot);
            }
        }

        protected override void OnBarUpdate()
        {
            if (_isOrderFlowRuntimeAvailable && _orderFlowTickBip > 0 && BarsInProgress == _orderFlowTickBip)
            {
                RefreshOrderFlowFromTickSeries();
                return;
            }

            if (BarsInProgress == 0)
            {
                DateTime nowUtc = DateTime.UtcNow;
                bool isTrackedPrimary = IsTrackedMinutesForBip(0);

                if (_lastBridgeTouchUtc == DateTime.MinValue ||
                    (nowUtc - _lastBridgeTouchUtc) >= TimeSpan.FromSeconds(1))
                {
                    _lastBridgeTouchUtc = nowUtc;
                    GlitchBridgeBusCompat.TouchBridge(
                        _instrumentRoot,
                        PublishToGlitchUi,
                        isTrackedPrimary);
                }

                // Re-register periodically so callback wiring self-heals after script reloads.
                if (_lastBootstrapRegisterUtc == DateTime.MinValue ||
                    (nowUtc - _lastBootstrapRegisterUtc) >= TimeSpan.FromSeconds(5))
                {
                    _lastBootstrapRegisterUtc = nowUtc;
                    // AddOn compilation resets its static dated-contract map while
                    // this chart can remain alive. Refresh both callbacks together
                    // so execution never falls back from MNQ 09-26 to generic MNQ.
                    GlitchBridgeBusCompat.RegisterTradeInstrumentInstance(Instrument);
                    GlitchBridgeBusCompat.RegisterBridgeBootstrapPublisher(_instrumentRoot, RequestBootstrapFromExternal);
                }
            }

            int minutes = ResolveMinutesForBip(BarsInProgress);
            if (!IsTrackedMinutesForBip(BarsInProgress))
                return;

            bool hasEnoughBars = CurrentBars[BarsInProgress] >= MinBarsForSignal;

            bool isPrimarySeries = BarsInProgress == 0;
            bool isHistorical = State == State.Historical;
            bool isBoundaryTick = isHistorical || IsFirstTickOfBar;

            bool shouldColor =
                EnableBarColoring &&
                isPrimarySeries &&
                (isHistorical || IntraBarColoring || IsFirstTickOfBar);

            bool bridgeAvailable = !PublishToGlitchUi || GlitchBridgeBusCompat.IsAvailable();
            if (PublishToGlitchUi && !bridgeAvailable)
            {
                DateTime nowUtc = DateTime.UtcNow;
                if (_lastBridgeUnavailableLogUtc == DateTime.MinValue ||
                    (nowUtc - _lastBridgeUnavailableLogUtc) >= TimeSpan.FromSeconds(30))
                {
                    _lastBridgeUnavailableLogUtc = nowUtc;
                    Log(
                        "GlitchAnalyticsBridge waiting for Glitch AddOn bridge type (Glitch.UI.GlitchAnalyticsFeedBus).",
                        NinjaTrader.Cbi.LogLevel.Information);
                }
            }

            // Historical chart replay and cache hydration are not live market observations.
            // Only realtime bar events may advance the feed timestamp used by the AI rail.
            bool shouldPublish =
                State == State.Realtime &&
                PublishToGlitchUi &&
                bridgeAvailable &&
                ShouldPublish(minutes, BarsInProgress);
            bool isStrategyAnalyzerHost = ChartControl == null;
            bool shouldComputeForExport =
                EnableHistoricalSnapshotExport &&
                (isHistorical || (State == State.Realtime && isStrategyAnalyzerHost)) &&
                isBoundaryTick;
            if (!shouldColor && !shouldPublish && !shouldComputeForExport)
                return;

            SignalSnapshot signal;
            bool includeOrderFlowHint = shouldPublish || shouldComputeForExport;
            if (hasEnoughBars)
            {
                if (!TryResolveSignal(BarsInProgress, isBoundaryTick, includeOrderFlowHint, out signal))
                    return;
            }
            else
            {
                signal = BuildWarmupSignal(BarsInProgress, includeOrderFlowHint);
            }

            if (shouldColor)
            {
                double colorScore = ApplyFlipHysteresis(signal.Score);
                ApplyBarColor(colorScore);
            }

            if (shouldPublish)
            {
                SessionTracker session = UpdateSessionTracker(minutes, BarsInProgress);
                GlitchBridgeBusCompat.Publish(BuildBridgeReading(BarsInProgress, minutes, signal, session));
            }

            if (shouldComputeForExport && BarsInProgress == 0)
                TryExportHistoricalMinuteSnapshot();
        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (marketDataUpdate == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            if (marketDataUpdate.MarketDataType == MarketDataType.Bid)
            {
                if (marketDataUpdate.Price > 0)
                    _lastBidPrice = marketDataUpdate.Price;
                return;
            }

            if (marketDataUpdate.MarketDataType == MarketDataType.Ask)
            {
                if (marketDataUpdate.Price > 0)
                    _lastAskPrice = marketDataUpdate.Price;
                return;
            }

            if (marketDataUpdate.MarketDataType != MarketDataType.Last)
                return;

            double tradePrice = marketDataUpdate.Price;
            if (tradePrice <= 0)
                return;

            double previousTradePrice = _lastTradePrice;
            _lastTradePrice = tradePrice;

            if (!_isOrderFlowRuntimeAvailable)
                return;

            double tradeVolume = marketDataUpdate.Volume > 0 ? marketDataUpdate.Volume : 0;
            if (tradeVolume <= 0)
                return;

            double buyVolume = 0;
            double sellVolume = 0;
            if (_lastAskPrice > 0 && tradePrice >= _lastAskPrice)
            {
                buyVolume = tradeVolume;
            }
            else if (_lastBidPrice > 0 && tradePrice <= _lastBidPrice)
            {
                sellVolume = tradeVolume;
            }
            else
            {
                if (previousTradePrice > 0 && tradePrice > previousTradePrice)
                    buyVolume = tradeVolume;
                else if (previousTradePrice > 0 && tradePrice < previousTradePrice)
                    sellVolume = tradeVolume;
                else
                {
                    buyVolume = tradeVolume * 0.5;
                    sellVolume = tradeVolume * 0.5;
                }
            }

            if (buyVolume <= 0 && sellVolume <= 0)
                return;

            _orderFlowTape.Enqueue(new OrderFlowTapeSample
            {
                UtcTime = nowUtc,
                BuyVolume = buyVolume,
                SellVolume = sellVolume
            });
            _orderFlowTapeBuyVolume += buyVolume;
            _orderFlowTapeSellVolume += sellVolume;
            _lastOrderFlowTapeUtc = nowUtc;
            PruneOrderFlowTape(nowUtc);
        }

        protected override void OnMarketDepth(MarketDepthEventArgs marketDepthUpdate)
        {
            if (!_isOrderFlowRuntimeAvailable || marketDepthUpdate == null)
                return;

            int level = marketDepthUpdate.Position;
            if (level < 0 || level >= OrderFlowDepthLevels)
                return;

            double volume = marketDepthUpdate.Volume > 0 ? marketDepthUpdate.Volume : 0;
            if (marketDepthUpdate.MarketDataType == MarketDataType.Bid)
            {
                _depthBidByLevel[level] = volume;
            }
            else if (marketDepthUpdate.MarketDataType == MarketDataType.Ask)
            {
                _depthAskByLevel[level] = volume;
            }

            _lastDepthUpdateUtc = DateTime.UtcNow;
        }

        private void RequestBootstrapFromExternal()
        {
            try
            {
                TriggerCustomEvent(_ => PublishBootstrapReadings(), null);
                return;
            }
            catch
            {
            }

            try
            {
                PublishBootstrapReadings();
            }
            catch
            {
            }
        }

        private void PublishBootstrapReadings()
        {
            if (!PublishToGlitchUi || BarsArray == null || CurrentBars == null)
                return;

            if (!GlitchBridgeBusCompat.IsAvailable())
                return;

            DateTime nowUtc = DateTime.UtcNow;
            DateTime primaryBarUtc;
            if (!TryGetFreshPrimaryBarUtc(nowUtc, out primaryBarUtc))
            {
                Log(
                    "GlitchAnalyticsBridge bootstrap skipped for " + (_instrumentRoot ?? "(null)") +
                    ": primary 1-minute bar is missing or stale.",
                    NinjaTrader.Cbi.LogLevel.Warning);
                return;
            }

            int seriesCount = BarsArray.Length;
            int publishedCount = 0;
            for (int bip = 0; bip < seriesCount; bip++)
            {
                if (bip < 0 || bip >= CurrentBars.Length || CurrentBars[bip] < 0)
                    continue;

                int minutes = ResolveMinutesForBip(bip);
                if (!IsTrackedMinutesForBip(bip))
                    continue;

                SignalSnapshot signal;
                if (CurrentBars[bip] >= MinBarsForSignal)
                {
                    if (!TryBuildSignal(bip, true, out signal))
                        signal = BuildWarmupSignal(bip, true);
                }
                else
                {
                    signal = BuildWarmupSignal(bip, true);
                }

                SessionTracker session = UpdateSessionTracker(minutes, bip);
                bool published = GlitchBridgeBusCompat.Publish(new GlitchBridgeBusCompat.BridgeReading
                {
                    InstrumentRoot = _instrumentRoot,
                    InstrumentFullName = Instrument == null ? null : Instrument.FullName,
                    Minutes = minutes,
                    // This timestamp means "analytics observed from a verified-live chart".
                    // The 60-minute bar's opening timestamp can naturally be much older;
                    // freshness is anchored by the current primary 1-minute bar above.
                    UtcTime = nowUtc,
                    CurrentPrice = ResolvePublishedCurrentPrice(signal.Close),
                    AveragePrice = signal.AveragePrice,
                    Atr = signal.Atr,
                    Adx = signal.Adx,
                    Score = signal.Score,
                    RawScore = signal.RawScore,
                    DirectionalScore = signal.DirectionalScore,
                    TradeabilityScore = signal.TradeabilityScore,
                    SignalLabel = ToSignalLabel(signal.Score),
                    VolatilityHint = BuildVolatilityHint(signal.Atr, signal.Close),
                    TrendHint = BuildTrendHint(signal.Adx, signal.Rsi, signal.StochK, signal.ZScore),
                    RegimeLabel = signal.RegimeLabel,
                    NoTradeReasons = signal.NoTradeReasons,
                    Rsi = signal.Rsi,
                    StochK = signal.StochK,
                    ZScore = signal.ZScore,
                    EmaAlignment = signal.EmaAlignment,
                    RegimeWeight = signal.RegimeWeight,
                    OscillatorCompositeScore = signal.OscillatorCompositeScore,
                    MaCompositeScore = signal.MaCompositeScore,
                    OrderFlowScore = signal.OrderFlowScore,
                    OrderFlowConfidence = signal.OrderFlowConfidence,
                    OrderFlowReliability = signal.OrderFlowReliability,
                    OrderFlowCumulativeDelta = signal.OrderFlowCumulativeDelta,
                    OrderFlowDeltaChange = signal.OrderFlowDeltaChange,
                    OrderFlowVwap = signal.OrderFlowVwap,
                    OrderFlowVwapDeviation = signal.OrderFlowVwapDeviation,
                    OrderFlowAggressionBalance = signal.OrderFlowAggressionBalance,
                    OrderFlowDepthImbalance = signal.OrderFlowDepthImbalance,
                    OrderFlowHint = signal.OrderFlowHint,
                    SessionName = session.Name,
                    SessionHigh = session.CurrentHigh,
                    SessionLow = session.CurrentLow,
                    PreviousSessionHigh = session.PreviousHigh,
                    PreviousSessionLow = session.PreviousLow
                });
                if (published)
                {
                    _lastPublishUtcByMinutes[minutes] = nowUtc;
                    publishedCount++;
                }
            }

            if (publishedCount > 0)
            {
                Log(
                    "GlitchAnalyticsBridge bootstrap published " + publishedCount + " timeframe(s) for " + (_instrumentRoot ?? "(null)") + ".",
                    NinjaTrader.Cbi.LogLevel.Information);
            }
        }

        private bool TryGetFreshPrimaryBarUtc(DateTime nowUtc, out DateTime primaryBarUtc)
        {
            primaryBarUtc = DateTime.MinValue;
            if (Times == null || Times.Length == 0 || Times[0] == null || Times[0].Count == 0)
                return false;

            DateTime primaryBarTime = Times[0][0];
            if (primaryBarTime == DateTime.MinValue)
                return false;

            primaryBarUtc = primaryBarTime.ToUniversalTime();
            if (primaryBarUtc > nowUtc + BootstrapFutureTolerance)
                return false;

            return (nowUtc - primaryBarUtc) <= BootstrapPrimaryMaxAge;
        }

        private SignalSnapshot BuildWarmupSignal(int bip, bool includeOrderFlowHint)
        {
            var signal = new SignalSnapshot();
            if (bip < 0 || bip >= BarsArray.Length || CurrentBars[bip] < 0)
                return signal;

            double close = Closes[bip][0];
            signal.Close = close;
            signal.AveragePrice = close;

            if (_atrByBip != null && bip < _atrByBip.Length && _atrByBip[bip] != null && CurrentBars[bip] > 0)
                signal.Atr = _atrByBip[bip][0];
            if (_adxByBip != null && bip < _adxByBip.Length && _adxByBip[bip] != null && CurrentBars[bip] > 0)
                signal.Adx = _adxByBip[bip][0];
            if (_rsiByBip != null && bip < _rsiByBip.Length && _rsiByBip[bip] != null && CurrentBars[bip] > 0)
                signal.Rsi = _rsiByBip[bip][0];
            if (_stochByBip != null && bip < _stochByBip.Length && _stochByBip[bip] != null && CurrentBars[bip] > 0)
                signal.StochK = _stochByBip[bip].K[0];
            if (_smaByBip != null && bip < _smaByBip.Length && _smaByBip[bip] != null && CurrentBars[bip] > 0)
                signal.AveragePrice = _smaByBip[bip][0];
            if (_dmByBip != null && bip < _dmByBip.Length && _dmByBip[bip] != null && CurrentBars[bip] >= 14)
            {
                signal.DiPlus = _dmByBip[bip].DiPlus[0];
                signal.DiMinus = _dmByBip[bip].DiMinus[0];
            }
            if (_cciByBip != null && bip < _cciByBip.Length && _cciByBip[bip] != null && CurrentBars[bip] >= 20)
                signal.Cci = _cciByBip[bip][0];
            if (_macdByBip != null && bip < _macdByBip.Length && _macdByBip[bip] != null && CurrentBars[bip] >= 35)
                signal.MacdHistogram = _macdByBip[bip].Default[0] - _macdByBip[bip].Avg[0];

            double atr = signal.Atr > 0 ? signal.Atr : Math.Max(_tickSize, 0.25);
            DateTime nowUtc = (_isOrderFlowRuntimeAvailable && EnableOrderFlowLayer) ? DateTime.UtcNow : DateTime.MinValue;
            TryBuildOrderFlowSnapshot(
                bip,
                close,
                atr,
                nowUtc,
                includeOrderFlowHint,
                out signal.OrderFlowScore,
                out signal.OrderFlowConfidence,
                out signal.OrderFlowReliability,
                out signal.OrderFlowCumulativeDelta,
                out signal.OrderFlowDeltaChange,
                out signal.OrderFlowVwap,
                out signal.OrderFlowVwapDeviation,
                out signal.OrderFlowAggressionBalance,
                out signal.OrderFlowDepthImbalance,
                out signal.OrderFlowHint);

            signal.Score = 0;
            signal.RawScore = 0;
            signal.DirectionalScore = 0;
            signal.TradeabilityScore = 0;
            signal.RegimeLabel = "Awaiting";
            signal.NoTradeReasons = "warmup";
            signal.EmaAlignment = 0;
            signal.RegimeWeight = 0;
            signal.ZScore = 0;
            signal.OscillatorCompositeScore = 0;
            signal.MaCompositeScore = 0;
            return signal;
        }

        private bool TryResolveSignal(
            int bip,
            bool isBoundaryTick,
            bool includeOrderFlowHint,
            out SignalSnapshot signal)
        {
            signal = default(SignalSnapshot);

            // ponytail: predictive fast path is chart-color only; published feed always gets a full rebuild
            bool useFastIntraBarPath = PerformanceMode && !isBoundaryTick && !includeOrderFlowHint;

            if (!useFastIntraBarPath)
            {
                if (!TryBuildSignal(bip, includeOrderFlowHint, out signal))
                    return false;

                CacheSignal(bip, signal);
                return true;
            }

            if (!TryGetCachedSignal(bip, out signal))
            {
                if (!TryBuildSignal(bip, includeOrderFlowHint, out signal))
                    return false;

                CacheSignal(bip, signal);
                return true;
            }

            signal.Close = Closes[bip][0];
            signal.Score = BuildPredictiveScore(signal, bip);
            signal.DirectionalScore = signal.Score;
            return true;
        }

        private void CacheSignal(int bip, SignalSnapshot signal)
        {
            if (_cachedSignalByBip == null || _hasCachedSignalByBip == null)
                return;

            if (bip < 0 || bip >= _cachedSignalByBip.Length)
                return;

            _cachedSignalByBip[bip] = signal;
            _hasCachedSignalByBip[bip] = true;
        }

        private bool TryGetCachedSignal(int bip, out SignalSnapshot signal)
        {
            signal = default(SignalSnapshot);
            if (_cachedSignalByBip == null || _hasCachedSignalByBip == null)
                return false;

            if (bip < 0 || bip >= _cachedSignalByBip.Length)
                return false;

            if (!_hasCachedSignalByBip[bip])
                return false;

            signal = _cachedSignalByBip[bip];
            return true;
        }

        private void AddMissingTimeframeSeries()
        {
            int primaryMinutes = ResolveMinutes(BarsPeriod.BarsPeriodType, BarsPeriod.Value);
            foreach (int minutes in TargetMinutes)
            {
                if (minutes == primaryMinutes)
                    continue;

                AddDataSeries(BarsPeriodType.Minute, minutes);
            }
        }

        private void AddOrderFlowTickSeries()
        {
            bool isPrimaryOneTick =
                BarsPeriod.BarsPeriodType == BarsPeriodType.Tick &&
                BarsPeriod.Value == 1;
            if (!isPrimaryOneTick)
                AddDataSeries(BarsPeriodType.Tick, 1);
        }

        private void InitializeSeriesMetadata()
        {
            if (BarsArray == null)
            {
                _minutesByBip = null;
                _isTrackedByBip = null;
                return;
            }

            int seriesCount = BarsArray.Length;
            _minutesByBip = new int[seriesCount];
            _isTrackedByBip = new bool[seriesCount];

            for (int bip = 0; bip < seriesCount; bip++)
            {
                Bars bars = BarsArray[bip];
                int minutes = -1;
                if (bars != null && bars.BarsPeriod != null)
                    minutes = ResolveMinutes(bars.BarsPeriod.BarsPeriodType, bars.BarsPeriod.Value);

                _minutesByBip[bip] = minutes;
                _isTrackedByBip[bip] = IsTrackedMinutes(minutes);
            }
        }

        private int ResolveMinutesForBip(int bip)
        {
            if (_minutesByBip != null && bip >= 0 && bip < _minutesByBip.Length)
                return _minutesByBip[bip];

            if (BarsArray == null || bip < 0 || bip >= BarsArray.Length || BarsArray[bip] == null || BarsArray[bip].BarsPeriod == null)
                return -1;

            return ResolveMinutes(BarsArray[bip].BarsPeriod.BarsPeriodType, BarsArray[bip].BarsPeriod.Value);
        }

        private bool IsTrackedMinutesForBip(int bip)
        {
            if (_isTrackedByBip != null && bip >= 0 && bip < _isTrackedByBip.Length)
                return _isTrackedByBip[bip];

            return IsTrackedMinutes(ResolveMinutesForBip(bip));
        }

        private int ResolveOrderFlowTickBip()
        {
            if (BarsArray == null)
                return -1;

            for (int bip = 0; bip < BarsArray.Length; bip++)
            {
                Bars bars = BarsArray[bip];
                if (bars == null || bars.BarsPeriod == null)
                    continue;

                if (bars.BarsPeriod.BarsPeriodType == BarsPeriodType.Tick &&
                    bars.BarsPeriod.Value == 1)
                {
                    return bip;
                }
            }

            return -1;
        }

        private void InitializeOrderFlowIndicators()
        {
            _isOrderFlowRuntimeAvailable = false;
            _orderFlowDeltaByBip = null;
            _orderFlowVwapByBip = null;

            if (!EnableOrderFlowLayer || BarsArray == null || BarsArray.Length == 0)
                return;

            if (_orderFlowTickBip < 0)
                return;

            int seriesCount = BarsArray.Length;
            var deltaByBip = new OrderFlowCumulativeDelta[seriesCount];
            var vwapByBip = new OrderFlowVWAP[seriesCount];
            int initialized = 0;

            try
            {
                for (int bip = 0; bip < seriesCount; bip++)
                {
                    if (!IsTrackedMinutesForBip(bip))
                        continue;

                    deltaByBip[bip] = OrderFlowCumulativeDelta(
                        BarsArray[bip],
                        CumulativeDeltaType.BidAsk,
                        CumulativeDeltaPeriod.Session,
                        0);
                    vwapByBip[bip] = OrderFlowVWAP(
                        BarsArray[bip],
                        VWAPResolution.Standard,
                        BarsArray[bip].TradingHours,
                        VWAPStandardDeviations.Three,
                        1.0,
                        2.0,
                        3.0);
                    initialized++;
                }
            }
            catch (Exception ex)
            {
                _orderFlowDeltaByBip = null;
                _orderFlowVwapByBip = null;
                _isOrderFlowRuntimeAvailable = false;
                LogOrderFlowUnavailableOnce("Order flow layer disabled: " + ex.Message);
                return;
            }

            _orderFlowDeltaByBip = deltaByBip;
            _orderFlowVwapByBip = vwapByBip;
            _isOrderFlowRuntimeAvailable = initialized > 0;
            if (!_isOrderFlowRuntimeAvailable)
                LogOrderFlowUnavailableOnce("Order flow layer unavailable: no tracked timeframe instances initialized.");
        }

        private void RefreshOrderFlowFromTickSeries()
        {
            if (!_isOrderFlowRuntimeAvailable || _orderFlowDeltaByBip == null)
                return;

            for (int bip = 0; bip < _orderFlowDeltaByBip.Length; bip++)
            {
                OrderFlowCumulativeDelta delta = _orderFlowDeltaByBip[bip];
                if (delta == null)
                    continue;

                try
                {
                    if (delta.BarsArray == null || delta.BarsArray.Length < 2 || delta.BarsArray[1] == null)
                        continue;

                    int tickCount = delta.BarsArray[1].Count;
                    if (tickCount <= 0)
                        continue;

                    delta.Update(tickCount - 1, 1);
                }
                catch (Exception ex)
                {
                    _isOrderFlowRuntimeAvailable = false;
                    LogOrderFlowUnavailableOnce("Order flow layer disabled during tick refresh: " + ex.Message);
                    return;
                }
            }
        }

        private void LogOrderFlowUnavailableOnce(string message)
        {
            if (_hasLoggedOrderFlowUnavailable)
                return;

            _hasLoggedOrderFlowUnavailable = true;
            if (string.IsNullOrWhiteSpace(message))
                message = "Order flow layer unavailable.";

            Log(message, NinjaTrader.Cbi.LogLevel.Information);
        }

        private void PruneOrderFlowTape(DateTime nowUtc)
        {
            while (_orderFlowTape.Count > 0)
            {
                OrderFlowTapeSample oldest = _orderFlowTape.Peek();
                if ((nowUtc - oldest.UtcTime) <= OrderFlowTapeWindow)
                    break;

                _orderFlowTapeBuyVolume -= oldest.BuyVolume;
                _orderFlowTapeSellVolume -= oldest.SellVolume;
                _orderFlowTape.Dequeue();
            }

            if (_orderFlowTapeBuyVolume < 0)
                _orderFlowTapeBuyVolume = 0;
            if (_orderFlowTapeSellVolume < 0)
                _orderFlowTapeSellVolume = 0;
        }

        private bool TryGetOrderFlowTapeBalance(DateTime nowUtc, out double balance)
        {
            balance = 0;
            if (!_isOrderFlowRuntimeAvailable)
                return false;

            if (_lastOrderFlowTapeUtc == DateTime.MinValue)
                return false;

            if ((nowUtc - _lastOrderFlowTapeUtc) > (OrderFlowTapeWindow + OrderFlowTapeWindow))
                return false;

            PruneOrderFlowTape(nowUtc);
            double total = _orderFlowTapeBuyVolume + _orderFlowTapeSellVolume;
            if (total <= 1e-8)
                return false;

            balance = Clamp((_orderFlowTapeBuyVolume - _orderFlowTapeSellVolume) / total, -1, 1);
            return true;
        }

        private bool TryGetDepthImbalance(DateTime nowUtc, out double imbalance)
        {
            imbalance = 0;
            if (!_isOrderFlowRuntimeAvailable || _lastDepthUpdateUtc == DateTime.MinValue)
                return false;

            if ((nowUtc - _lastDepthUpdateUtc) > OrderFlowDepthFreshness)
                return false;

            double bidDepth = 0;
            double askDepth = 0;
            for (int i = 0; i < OrderFlowDepthLevels; i++)
            {
                bidDepth += Math.Max(_depthBidByLevel[i], 0);
                askDepth += Math.Max(_depthAskByLevel[i], 0);
            }

            double total = bidDepth + askDepth;
            if (total <= 1e-8)
                return false;

            imbalance = Clamp((bidDepth - askDepth) / total, -1, 1);
            return true;
        }

        private void InitializeIndicators()
        {
            int seriesCount = BarsArray.Length;
            _emaFastByBip = new EMA[seriesCount];
            _emaMedByBip = new EMA[seriesCount];
            _emaSlowByBip = new EMA[seriesCount];
            _ema13ByBip = new EMA[seriesCount];
            _ema10ByBip = new EMA[seriesCount];
            _ema20ByBip = new EMA[seriesCount];
            _ema30ByBip = new EMA[seriesCount];
            _ema50ByBip = new EMA[seriesCount];
            _ema100ByBip = new EMA[seriesCount];
            _ema200ByBip = new EMA[seriesCount];
            _atrByBip = new ATR[seriesCount];
            _adxByBip = new ADX[seriesCount];
            _dmByBip = new DM[seriesCount];
            _rsiByBip = new RSI[seriesCount];
            _stochByBip = new Stochastics[seriesCount];
            _stochRsiByBip = new StochRSI[seriesCount];
            _macdByBip = new MACD[seriesCount];
            _cciByBip = new CCI[seriesCount];
            _momentumByBip = new Momentum[seriesCount];
            _williamsRByBip = new WilliamsR[seriesCount];
            _ultimateOscillatorByBip = new UltimateOscillator[seriesCount];
            _hma9ByBip = new HMA[seriesCount];
            _sma10ByBip = new SMA[seriesCount];
            _smaByBip = new SMA[seriesCount];
            _sma30ByBip = new SMA[seriesCount];
            _sma50ByBip = new SMA[seriesCount];
            _sma100ByBip = new SMA[seriesCount];
            _sma200ByBip = new SMA[seriesCount];

            for (int bip = 0; bip < seriesCount; bip++)
            {
                if (!IsTrackedMinutesForBip(bip))
                    continue;

                _emaFastByBip[bip] = EMA(BarsArray[bip], 12);
                _emaMedByBip[bip] = EMA(BarsArray[bip], 26);
                _emaSlowByBip[bip] = EMA(BarsArray[bip], 55);
                _ema13ByBip[bip] = EMA(BarsArray[bip], 13);
                _ema10ByBip[bip] = EMA(BarsArray[bip], 10);
                _ema20ByBip[bip] = EMA(BarsArray[bip], 20);
                _ema30ByBip[bip] = EMA(BarsArray[bip], 30);
                _ema50ByBip[bip] = EMA(BarsArray[bip], 50);
                _ema100ByBip[bip] = EMA(BarsArray[bip], 100);
                _ema200ByBip[bip] = EMA(BarsArray[bip], 200);
                _atrByBip[bip] = ATR(BarsArray[bip], 14);
                _adxByBip[bip] = ADX(BarsArray[bip], 14);
                _dmByBip[bip] = DM(BarsArray[bip], 14);
                _rsiByBip[bip] = RSI(BarsArray[bip], 14, 3);
                _stochByBip[bip] = Stochastics(BarsArray[bip], 14, 3, 3);
                _stochRsiByBip[bip] = StochRSI(BarsArray[bip], 14);
                _macdByBip[bip] = MACD(BarsArray[bip], 12, 26, 9);
                _cciByBip[bip] = CCI(BarsArray[bip], 20);
                _momentumByBip[bip] = Momentum(BarsArray[bip], 10);
                _williamsRByBip[bip] = WilliamsR(BarsArray[bip], 14);
                _ultimateOscillatorByBip[bip] = UltimateOscillator(BarsArray[bip], 7, 14, 28);
                _hma9ByBip[bip] = HMA(BarsArray[bip], 9);
                _sma10ByBip[bip] = SMA(BarsArray[bip], 10);
                _smaByBip[bip] = SMA(BarsArray[bip], 20);
                _sma30ByBip[bip] = SMA(BarsArray[bip], 30);
                _sma50ByBip[bip] = SMA(BarsArray[bip], 50);
                _sma100ByBip[bip] = SMA(BarsArray[bip], 100);
                _sma200ByBip[bip] = SMA(BarsArray[bip], 200);
            }
        }

        private void InitializeColorPalettes()
        {
            Color neutral = Color.FromRgb(118, 122, 126);
            Color buyLow = Color.FromRgb(52, 92, 86);
            Color buyHigh = Color.FromRgb(0, 188, 164);
            Color sellLow = Color.FromRgb(112, 84, 58);
            Color sellHigh = Color.FromRgb(255, 94, 0);

            _neutralBrush = new SolidColorBrush(neutral);
            _neutralBrush.Freeze();
            _buyPalette = BuildPalette(buyLow, buyHigh);
            _sellPalette = BuildPalette(sellLow, sellHigh);
        }

        private bool TryBuildSignal(int bip, bool includeOrderFlowHint, out SignalSnapshot signal)
        {
            signal = default(SignalSnapshot);
            if (bip < 0 || bip >= BarsArray.Length)
                return false;

            if (CurrentBars[bip] < MinBarsForSignal)
                return false;

            double close = Closes[bip][0];
            if (close <= 0)
                return false;

            double atr = _atrByBip[bip][0];
            double adx = _adxByBip[bip][0];
            double rsi = _rsiByBip[bip][0];
            double stochK = _stochByBip[bip].K[0];
            double averagePrice = _smaByBip[bip][0];
            double zScore = ComputeZScore(Closes[bip], Math.Min(ZLookback, CurrentBars[bip] + 1));

            double emaFast = _emaFastByBip[bip][0];
            double emaMed = _emaMedByBip[bip][0];
            double emaSlow = _emaSlowByBip[bip][0];

            double denominator = Math.Max(Math.Max(atr, _tickSize), 1e-8);
            double fastSlopeN = (_emaFastByBip[bip][0] - _emaFastByBip[bip][1]) / denominator;
            double medSlopeN = (_emaMedByBip[bip][0] - _emaMedByBip[bip][1]) / denominator;
            double slowSlopeN = (_emaSlowByBip[bip][0] - _emaSlowByBip[bip][1]) / denominator;

            double emaSlopeSignal = Clamp((fastSlopeN * 0.50) + (medSlopeN * 0.30) + (slowSlopeN * 0.20), -1, 1);
            double emaAlignment = BuildEmaAlignmentSignal(emaFast, emaMed, emaSlow);
            double rsiSignal = Clamp((rsi - 50.0) / 22.0, -1, 1);
            double stochSignal = Clamp((stochK - 50.0) / 28.0, -1, 1);
            double zSignal = Clamp(zScore / 2.2, -1, 1);
            double adxSlopeSignal = CurrentBars[bip] > 0
                ? Clamp((adx - _adxByBip[bip][1]) / 8.0, -1, 1)
                : 0;
            double shortMomentumSignal = CurrentBars[bip] >= 5
                ? Clamp((close - Closes[bip][5]) / (denominator * 2.8), -1, 1)
                : 0;
            bool hasDmiSignal =
                _dmByBip != null &&
                bip < _dmByBip.Length &&
                _dmByBip[bip] != null &&
                CurrentBars[bip] >= 14;
            double diDirection = 0;
            double? diPlus = null;
            double? diMinus = null;
            if (hasDmiSignal)
            {
                diPlus = _dmByBip[bip].DiPlus[0];
                diMinus = _dmByBip[bip].DiMinus[0];
                double diSum = diPlus.Value + diMinus.Value;
                if (diSum > 1e-8)
                    diDirection = Clamp((diPlus.Value - diMinus.Value) / diSum, -1, 1);
            }

            double adxDirectionalSignal = hasDmiSignal
                ? Clamp(diDirection * Clamp((adx - 12.0) / 20.0, 0, 1), -1, 1)
                : 0;
            bool hasCciSignal =
                _cciByBip != null &&
                bip < _cciByBip.Length &&
                _cciByBip[bip] != null &&
                CurrentBars[bip] >= 20;
            bool hasMomentumSignal =
                _momentumByBip != null &&
                bip < _momentumByBip.Length &&
                _momentumByBip[bip] != null &&
                CurrentBars[bip] >= 10;
            bool hasWilliamsRSignal =
                _williamsRByBip != null &&
                bip < _williamsRByBip.Length &&
                _williamsRByBip[bip] != null &&
                CurrentBars[bip] >= 14;
            bool hasUltimateSignal =
                _ultimateOscillatorByBip != null &&
                bip < _ultimateOscillatorByBip.Length &&
                _ultimateOscillatorByBip[bip] != null &&
                CurrentBars[bip] >= 28;
            double? cci = hasCciSignal ? (double?)_cciByBip[bip][0] : null;
            double cciSignal = cci.HasValue
                ? Clamp(cci.Value / 180.0, -1, 1)
                : 0;
            double momentumSignal = hasMomentumSignal
                ? Clamp(_momentumByBip[bip][0] / (denominator * 3.2), -1, 1)
                : 0;
            double williamsRSignal = hasWilliamsRSignal
                ? Clamp((-50.0 - _williamsRByBip[bip][0]) / 50.0, -1, 1)
                : 0;
            double ultimateSignal = hasUltimateSignal
                ? Clamp((_ultimateOscillatorByBip[bip][0] - 50.0) / 23.0, -1, 1)
                : 0;
            bool hasStochRsiSignal =
                _stochRsiByBip != null &&
                bip < _stochRsiByBip.Length &&
                _stochRsiByBip[bip] != null &&
                CurrentBars[bip] >= 14;
            double stochRsiSignal = hasStochRsiSignal
                ? Clamp((_stochRsiByBip[bip][0] - 0.5) / 0.28, -1, 1)
                : 0;
            bool hasMacdSignal =
                _macdByBip != null &&
                bip < _macdByBip.Length &&
                _macdByBip[bip] != null &&
                CurrentBars[bip] >= 35;
            double macdHistogramSignal = 0;
            double macdSlopeSignal = 0;
            double? macdHistogramValue = null;
            if (hasMacdSignal)
            {
                double macdMain = _macdByBip[bip].Default[0];
                double macdSignalLine = _macdByBip[bip].Avg[0];
                double macdMainPrev = _macdByBip[bip].Default[1];
                double macdSignalLinePrev = _macdByBip[bip].Avg[1];

                double macdHistogram = macdMain - macdSignalLine;
                double macdHistogramPrev = macdMainPrev - macdSignalLinePrev;
                macdHistogramValue = macdHistogram;
                macdHistogramSignal = Clamp((macdHistogram / denominator) * 1.25, -1, 1);
                macdSlopeSignal = Clamp(((macdHistogram - macdHistogramPrev) / denominator) * 0.90, -1, 1);
            }
            double aoSignal;
            bool hasAoSignal = TryComputeAwesomeOscillatorSignal(bip, denominator, out aoSignal);
            bool hasBullBearSignal =
                _ema13ByBip != null &&
                bip < _ema13ByBip.Length &&
                _ema13ByBip[bip] != null &&
                CurrentBars[bip] >= 13;
            double bullBearSignal = 0;
            if (hasBullBearSignal)
            {
                double ema13 = _ema13ByBip[bip][0];
                double bullPower = Highs[bip][0] - ema13;
                double bearPower = Lows[bip][0] - ema13;
                bullBearSignal = Clamp(((bullPower + bearPower) * 0.5) / denominator, -1, 1);
            }
            double regimeWeight = BuildRegimeWeight(adx);

            int maBuyVotes = 0;
            int maSellVotes = 0;
            int maNeutralVotes = 0;
            int totalMaVotes = 0;
            double maTolerance = Math.Max(denominator * 0.03, _tickSize * 0.50);

            if (CurrentBars[bip] >= 10)
            {
                AddMaVote(close, _ema10ByBip[bip][0], maTolerance, ref maBuyVotes, ref maSellVotes, ref maNeutralVotes, ref totalMaVotes);
                AddMaVote(close, _sma10ByBip[bip][0], maTolerance, ref maBuyVotes, ref maSellVotes, ref maNeutralVotes, ref totalMaVotes);
            }
            if (CurrentBars[bip] >= 20)
            {
                AddMaVote(close, _ema20ByBip[bip][0], maTolerance, ref maBuyVotes, ref maSellVotes, ref maNeutralVotes, ref totalMaVotes);
                AddMaVote(close, _smaByBip[bip][0], maTolerance, ref maBuyVotes, ref maSellVotes, ref maNeutralVotes, ref totalMaVotes);

                double vwma20;
                if (TryComputeVolumeWeightedAverage(bip, 20, out vwma20))
                    AddMaVote(close, vwma20, maTolerance, ref maBuyVotes, ref maSellVotes, ref maNeutralVotes, ref totalMaVotes);
            }
            if (CurrentBars[bip] >= 30)
            {
                AddMaVote(close, _ema30ByBip[bip][0], maTolerance, ref maBuyVotes, ref maSellVotes, ref maNeutralVotes, ref totalMaVotes);
                AddMaVote(close, _sma30ByBip[bip][0], maTolerance, ref maBuyVotes, ref maSellVotes, ref maNeutralVotes, ref totalMaVotes);
            }
            if (CurrentBars[bip] >= 50)
            {
                AddMaVote(close, _ema50ByBip[bip][0], maTolerance, ref maBuyVotes, ref maSellVotes, ref maNeutralVotes, ref totalMaVotes);
                AddMaVote(close, _sma50ByBip[bip][0], maTolerance, ref maBuyVotes, ref maSellVotes, ref maNeutralVotes, ref totalMaVotes);
            }
            if (CurrentBars[bip] >= 100)
            {
                AddMaVote(close, _ema100ByBip[bip][0], maTolerance, ref maBuyVotes, ref maSellVotes, ref maNeutralVotes, ref totalMaVotes);
                AddMaVote(close, _sma100ByBip[bip][0], maTolerance, ref maBuyVotes, ref maSellVotes, ref maNeutralVotes, ref totalMaVotes);
            }
            if (CurrentBars[bip] >= 200)
            {
                AddMaVote(close, _ema200ByBip[bip][0], maTolerance, ref maBuyVotes, ref maSellVotes, ref maNeutralVotes, ref totalMaVotes);
                AddMaVote(close, _sma200ByBip[bip][0], maTolerance, ref maBuyVotes, ref maSellVotes, ref maNeutralVotes, ref totalMaVotes);
            }
            if (_hma9ByBip != null && bip < _hma9ByBip.Length && _hma9ByBip[bip] != null && CurrentBars[bip] >= 9)
                AddMaVote(close, _hma9ByBip[bip][0], maTolerance, ref maBuyVotes, ref maSellVotes, ref maNeutralVotes, ref totalMaVotes);

            double ichimokuBaseline;
            if (TryComputeHighLowMidpoint(bip, 26, 0, out ichimokuBaseline))
                AddMaVote(close, ichimokuBaseline, maTolerance, ref maBuyVotes, ref maSellVotes, ref maNeutralVotes, ref totalMaVotes);

            double tvMaCompositeSignal = totalMaVotes > 0
                ? Clamp((maBuyVotes - maSellVotes) / (double)totalMaVotes, -1, 1)
                : 0;

            int oscillatorBuyVotes = 0;
            int oscillatorSellVotes = 0;
            int oscillatorNeutralVotes = 0;
            int totalOscillatorVotes = 0;
            AddOscillatorVote(rsiSignal, ref oscillatorBuyVotes, ref oscillatorSellVotes, ref oscillatorNeutralVotes, ref totalOscillatorVotes);
            AddOscillatorVote(stochSignal, ref oscillatorBuyVotes, ref oscillatorSellVotes, ref oscillatorNeutralVotes, ref totalOscillatorVotes);
            if (hasCciSignal)
                AddOscillatorVote(cciSignal, ref oscillatorBuyVotes, ref oscillatorSellVotes, ref oscillatorNeutralVotes, ref totalOscillatorVotes);
            if (hasDmiSignal)
                AddOscillatorVote(adxDirectionalSignal, ref oscillatorBuyVotes, ref oscillatorSellVotes, ref oscillatorNeutralVotes, ref totalOscillatorVotes);
            if (hasAoSignal)
                AddOscillatorVote(aoSignal, ref oscillatorBuyVotes, ref oscillatorSellVotes, ref oscillatorNeutralVotes, ref totalOscillatorVotes);
            if (hasMomentumSignal)
                AddOscillatorVote(momentumSignal, ref oscillatorBuyVotes, ref oscillatorSellVotes, ref oscillatorNeutralVotes, ref totalOscillatorVotes);
            if (hasMacdSignal)
                AddOscillatorVote(macdHistogramSignal, ref oscillatorBuyVotes, ref oscillatorSellVotes, ref oscillatorNeutralVotes, ref totalOscillatorVotes);
            if (hasStochRsiSignal)
                AddOscillatorVote(stochRsiSignal, ref oscillatorBuyVotes, ref oscillatorSellVotes, ref oscillatorNeutralVotes, ref totalOscillatorVotes);
            if (hasWilliamsRSignal)
                AddOscillatorVote(williamsRSignal, ref oscillatorBuyVotes, ref oscillatorSellVotes, ref oscillatorNeutralVotes, ref totalOscillatorVotes);
            if (hasBullBearSignal)
                AddOscillatorVote(bullBearSignal, ref oscillatorBuyVotes, ref oscillatorSellVotes, ref oscillatorNeutralVotes, ref totalOscillatorVotes);
            if (hasUltimateSignal)
                AddOscillatorVote(ultimateSignal, ref oscillatorBuyVotes, ref oscillatorSellVotes, ref oscillatorNeutralVotes, ref totalOscillatorVotes);

            double tvOscillatorCompositeSignal = totalOscillatorVotes > 0
                ? Clamp((oscillatorBuyVotes - oscillatorSellVotes) / (double)totalOscillatorVotes, -1, 1)
                : 0;

            double weightedCore = 0;
            double weightSum = 0;
            AccumulateDirectionalFactor(ref weightedCore, ref weightSum, emaSlopeSignal, 0.16);
            AccumulateDirectionalFactor(ref weightedCore, ref weightSum, emaAlignment, 0.10);
            AccumulateDirectionalFactor(ref weightedCore, ref weightSum, rsiSignal, 0.05);
            AccumulateDirectionalFactor(ref weightedCore, ref weightSum, stochSignal, 0.05);
            AccumulateDirectionalFactor(ref weightedCore, ref weightSum, zSignal, 0.05);
            AccumulateDirectionalFactor(ref weightedCore, ref weightSum, adxSlopeSignal, 0.03);
            AccumulateDirectionalFactor(ref weightedCore, ref weightSum, shortMomentumSignal, 0.04);

            if (hasCciSignal)
                AccumulateDirectionalFactor(ref weightedCore, ref weightSum, cciSignal, 0.04);

            if (hasMomentumSignal)
                AccumulateDirectionalFactor(ref weightedCore, ref weightSum, momentumSignal, 0.05);

            if (hasWilliamsRSignal)
                AccumulateDirectionalFactor(ref weightedCore, ref weightSum, williamsRSignal, 0.03);

            if (hasUltimateSignal)
                AccumulateDirectionalFactor(ref weightedCore, ref weightSum, ultimateSignal, 0.03);

            if (hasMacdSignal)
            {
                AccumulateDirectionalFactor(ref weightedCore, ref weightSum, macdHistogramSignal, 0.06);
                AccumulateDirectionalFactor(ref weightedCore, ref weightSum, macdSlopeSignal, 0.04);
            }
            if (hasDmiSignal)
                AccumulateDirectionalFactor(ref weightedCore, ref weightSum, adxDirectionalSignal, 0.05);
            if (hasStochRsiSignal)
                AccumulateDirectionalFactor(ref weightedCore, ref weightSum, stochRsiSignal, 0.04);
            if (hasAoSignal)
                AccumulateDirectionalFactor(ref weightedCore, ref weightSum, aoSignal, 0.05);
            if (hasBullBearSignal)
                AccumulateDirectionalFactor(ref weightedCore, ref weightSum, bullBearSignal, 0.05);

            double directionalCore = weightSum > 1e-8
                ? Clamp(weightedCore / weightSum, -1, 1)
                : 0;

            double baseScore = Clamp(directionalCore * regimeWeight, -1, 1);
            DateTime nowUtc = (_isOrderFlowRuntimeAvailable && EnableOrderFlowLayer) ? DateTime.UtcNow : DateTime.MinValue;
            double? orderFlowScore = null;
            double? orderFlowConfidence = null;
            double? orderFlowReliability = null;
            double? orderFlowDelta = null;
            double? orderFlowDeltaChange = null;
            double? orderFlowVwap = null;
            double? orderFlowVwapDeviation = null;
            double? orderFlowAggressionBalance = null;
            double? orderFlowDepthImbalance = null;
            string orderFlowHint = null;
            TryBuildOrderFlowSnapshot(
                bip,
                close,
                atr,
                nowUtc,
                includeOrderFlowHint,
                out orderFlowScore,
                out orderFlowConfidence,
                out orderFlowReliability,
                out orderFlowDelta,
                out orderFlowDeltaChange,
                out orderFlowVwap,
                out orderFlowVwapDeviation,
                out orderFlowAggressionBalance,
                out orderFlowDepthImbalance,
                out orderFlowHint);

            double finalScore = baseScore;
            if (orderFlowScore.HasValue && OrderFlowBlend > 0)
            {
                double blend = Clamp(OrderFlowBlend, 0, 0.80);
                finalScore = Clamp(
                    (baseScore * (1.0 - blend)) + (orderFlowScore.Value * blend),
                    -1,
                    1);
            }

            string regimeLabel = BuildRegimeLabel(adx, atr, close);
            double tradeabilityScore = BuildTradeabilityScore(
                finalScore,
                adx,
                regimeWeight,
                orderFlowReliability,
                regimeLabel);
            string noTradeReasons = BuildNoTradeReasons(
                finalScore,
                tradeabilityScore,
                regimeLabel,
                orderFlowScore,
                orderFlowReliability);

            signal = new SignalSnapshot
            {
                Close = close,
                AveragePrice = averagePrice,
                Atr = atr,
                Adx = adx,
                Rsi = rsi,
                StochK = stochK,
                ZScore = zScore,
                DiPlus = diPlus,
                DiMinus = diMinus,
                Cci = cci,
                MacdHistogram = macdHistogramValue,
                EmaAlignment = emaAlignment,
                RegimeWeight = regimeWeight,
                OscillatorCompositeScore = tvOscillatorCompositeSignal,
                MaCompositeScore = tvMaCompositeSignal,
                Score = finalScore,
                RawScore = baseScore,
                DirectionalScore = finalScore,
                TradeabilityScore = tradeabilityScore,
                RegimeLabel = regimeLabel,
                NoTradeReasons = noTradeReasons,
                OrderFlowScore = orderFlowScore,
                OrderFlowConfidence = orderFlowConfidence,
                OrderFlowReliability = orderFlowReliability,
                OrderFlowCumulativeDelta = orderFlowDelta,
                OrderFlowDeltaChange = orderFlowDeltaChange,
                OrderFlowVwap = orderFlowVwap,
                OrderFlowVwapDeviation = orderFlowVwapDeviation,
                OrderFlowAggressionBalance = orderFlowAggressionBalance,
                OrderFlowDepthImbalance = orderFlowDepthImbalance,
                OrderFlowHint = orderFlowHint
            };
            return true;
        }

        private double BuildPredictiveScore(SignalSnapshot cachedSignal, int bip)
        {
            if (CurrentBars[bip] < 2)
                return cachedSignal.Score;

            double atr = _atrByBip[bip][0];
            double scale = Math.Max(Math.Max(atr, _tickSize), 1e-8);
            double bodyVelocity = (Closes[bip][0] - Opens[bip][0]) / (scale * 0.55);
            double tickVelocity = (Closes[bip][0] - Closes[bip][1]) / (scale * 0.30);
            double acceleration = ((Closes[bip][0] - Closes[bip][1]) - (Closes[bip][1] - Closes[bip][2])) / (scale * 0.40);

            double leadImpulse = Clamp(
                (bodyVelocity * 0.50) +
                (tickVelocity * 0.35) +
                (acceleration * 0.15),
                -1,
                1);

            double predicted = cachedSignal.Score + (leadImpulse * PredictiveBoost);
            return Clamp(predicted, -1, 1);
        }

        private void TryBuildOrderFlowSnapshot(
            int bip,
            double close,
            double atr,
            DateTime nowUtc,
            bool includeHint,
            out double? score,
            out double? confidence,
            out double? reliability,
            out double? cumulativeDelta,
            out double? deltaChange,
            out double? vwap,
            out double? vwapDeviation,
            out double? aggressionBalance,
            out double? depthImbalance,
            out string hint)
        {
            score = null;
            confidence = null;
            reliability = null;
            cumulativeDelta = null;
            deltaChange = null;
            vwap = null;
            vwapDeviation = null;
            aggressionBalance = null;
            depthImbalance = null;
            hint = null;

            if (!_isOrderFlowRuntimeAvailable || !EnableOrderFlowLayer)
                return;

            if (_orderFlowDeltaByBip == null || bip < 0 || bip >= _orderFlowDeltaByBip.Length)
                return;

            OrderFlowCumulativeDelta delta = _orderFlowDeltaByBip[bip];
            if (delta == null)
                return;

            try
            {
                cumulativeDelta = delta.DeltaClose[0];
                double previousDelta = CurrentBars[bip] > 0 ? delta.DeltaClose[1] : delta.DeltaClose[0];
                deltaChange = cumulativeDelta.Value - previousDelta;
            }
            catch
            {
                return;
            }

            double barVolume = Volumes != null && bip < Volumes.Length && Volumes[bip] != null
                ? Math.Max(Volumes[bip][0], 0)
                : 0;
            double deltaPressure = 0;
            if (deltaChange.HasValue)
            {
                double normalization = Math.Max(barVolume * 0.30, 1.0);
                deltaPressure = Clamp(deltaChange.Value / normalization, -1, 1);
            }

            double vwapBias = 0;
            if (_orderFlowVwapByBip != null && bip < _orderFlowVwapByBip.Length)
            {
                OrderFlowVWAP vwapIndicator = _orderFlowVwapByBip[bip];
                if (vwapIndicator != null)
                {
                    try
                    {
                        double currentVwap = vwapIndicator.VWAP[0];
                        if (currentVwap > 0)
                        {
                            vwap = currentVwap;
                            double normalization = Math.Max(Math.Max(atr, _tickSize), 1e-8);
                            vwapBias = Clamp((close - currentVwap) / normalization, -1, 1);

                            double stdUpper = vwapIndicator.StdDev1Upper[0];
                            double stdLower = vwapIndicator.StdDev1Lower[0];
                            double stdSpan = Math.Abs(stdUpper - stdLower);
                            if (stdSpan > 1e-8)
                            {
                                double sigma = (close - currentVwap) / (stdSpan * 0.5);
                                vwapDeviation = Clamp(sigma, -5, 5);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            double tapeBalance;
            if (TryGetOrderFlowTapeBalance(nowUtc, out tapeBalance))
            {
                aggressionBalance = tapeBalance;
            }

            double depthValue;
            if (TryGetDepthImbalance(nowUtc, out depthValue))
            {
                depthImbalance = depthValue;
            }

            double ofScore =
                (deltaPressure * 0.55) +
                ((aggressionBalance ?? 0) * 0.20) +
                ((depthImbalance ?? 0) * 0.15) +
                (vwapBias * 0.10);

            double priceVelocity = 0;
            bool hasPriceVelocity = false;
            if (CurrentBars[bip] > 0)
            {
                priceVelocity = Clamp(
                    (Closes[bip][0] - Opens[bip][0]) / Math.Max(Math.Max(atr, _tickSize), 1e-8),
                    -1,
                    1);
                hasPriceVelocity = true;
                if (Math.Abs(priceVelocity) > 0.15 &&
                    Math.Abs(deltaPressure) > 0.15 &&
                    Math.Sign(priceVelocity) != Math.Sign(deltaPressure))
                {
                    ofScore *= 0.75;
                }
            }

            ofScore = Clamp(ofScore, -1, 1);
            score = ofScore;

            double confidenceValue = 0;
            confidenceValue += Math.Min(0.55, Math.Abs(deltaPressure) * 0.55);
            if (aggressionBalance.HasValue)
                confidenceValue += Math.Min(0.20, Math.Abs(aggressionBalance.Value) * 0.20);
            if (depthImbalance.HasValue)
                confidenceValue += Math.Min(0.15, Math.Abs(depthImbalance.Value) * 0.15);
            if (vwap.HasValue)
                confidenceValue += 0.10;
            confidence = Clamp(confidenceValue, 0, 1);

            double reliabilityValue = confidence.Value * 0.75;
            if (aggressionBalance.HasValue)
                reliabilityValue += 0.08;
            if (depthImbalance.HasValue)
                reliabilityValue += 0.08;
            if (vwapDeviation.HasValue)
                reliabilityValue += 0.04;
            if (Math.Abs(ofScore) >= 0.20)
                reliabilityValue += 0.05;
            if (hasPriceVelocity && Math.Abs(priceVelocity) > 0.12)
            {
                if (Math.Sign(priceVelocity) == Math.Sign(ofScore))
                    reliabilityValue += 0.10;
                else
                    reliabilityValue -= 0.15;
            }
            reliability = Clamp(reliabilityValue, 0, 1);

            if (includeHint)
            {
                hint = BuildOrderFlowHint(
                    ofScore,
                    confidence.Value,
                    reliability.Value,
                    cumulativeDelta,
                    deltaChange,
                    aggressionBalance,
                    depthImbalance,
                    vwapDeviation);
            }
        }

        private bool ShouldPublish(int minutes, int bip)
        {
            if (State == State.Historical)
                return CurrentBars[bip] >= BarsArray[bip].Count - 1;

            int intervalMs = PublishIntervalMs;
            if (intervalMs <= 0)
                return IsFirstTickOfBar;

            DateTime nowUtc = DateTime.UtcNow;
            DateTime lastPublishUtc;
            if (_lastPublishUtcByMinutes.TryGetValue(minutes, out lastPublishUtc) &&
                (nowUtc - lastPublishUtc).TotalMilliseconds < intervalMs)
                return false;

            _lastPublishUtcByMinutes[minutes] = nowUtc;
            return true;
        }

        private double ApplyFlipHysteresis(double score)
        {
            double hysteresis = FlipHysteresis;
            if (hysteresis < 0)
                hysteresis = 0;

            double upperEnter = NeutralBand + hysteresis;
            double lowerEnter = -NeutralBand - hysteresis;
            double upperExit = NeutralBand - hysteresis;
            double lowerExit = -NeutralBand + hysteresis;

            int regime = _lastColorRegime;
            if (regime == 1)
            {
                if (score <= lowerEnter)
                    regime = -1;
                else if (score < upperExit)
                    regime = 0;
            }
            else if (regime == -1)
            {
                if (score >= upperEnter)
                    regime = 1;
                else if (score > lowerExit)
                    regime = 0;
            }
            else
            {
                if (score >= upperEnter)
                    regime = 1;
                else if (score <= lowerEnter)
                    regime = -1;
            }

            _lastColorRegime = regime;
            if (regime == 1 && score <= NeutralBand)
                return NeutralBand + 0.0001;
            if (regime == -1 && score >= -NeutralBand)
                return -NeutralBand - 0.0001;
            if (regime == 0)
                return 0;

            return score;
        }

        private void ApplyBarColor(double score)
        {
            int colorKey = ResolveColorKey(score);
            if (CurrentBar == _lastPaintedBarIndex && colorKey == _lastPaintedColorKey)
                return;

            Brush brush = ResolveRegimeBrushFromColorKey(colorKey);
            if (brush == null)
            {
                BarBrush = null;
                CandleOutlineBrush = null;
            }
            else
            {
                BarBrush = brush;
                CandleOutlineBrush = brush;
            }

            _lastPaintedBarIndex = CurrentBar;
            _lastPaintedColorKey = colorKey;
        }

        private int ResolveColorKey(double score)
        {
            double absScore = Math.Abs(score);
            if (absScore <= NeutralBand)
                return 0;

            double span = Math.Max(1.0 - NeutralBand, 1e-8);
            int index = (int)Math.Round(((absScore - NeutralBand) / span) * (PaletteLevels - 1));
            if (index < 0)
                index = 0;
            if (index >= PaletteLevels)
                index = PaletteLevels - 1;

            return score >= 0 ? index + 1 : -(index + 1);
        }

        private Brush ResolveRegimeBrushFromColorKey(int colorKey)
        {
            if (colorKey == 0)
                return _neutralBrush;

            int paletteIndex = Math.Abs(colorKey) - 1;
            if (paletteIndex < 0 || paletteIndex >= PaletteLevels)
                return _neutralBrush;

            return colorKey > 0 ? _buyPalette[paletteIndex] : _sellPalette[paletteIndex];
        }

        private Brush ResolveRegimeBrush(double score)
        {
            double absScore = Math.Abs(score);
            if (absScore <= NeutralBand)
                return _neutralBrush;

            double span = Math.Max(1.0 - NeutralBand, 1e-8);
            int index = (int)Math.Round(((absScore - NeutralBand) / span) * (PaletteLevels - 1));
            if (index < 0)
                index = 0;
            if (index >= PaletteLevels)
                index = PaletteLevels - 1;

            return score >= 0 ? _buyPalette[index] : _sellPalette[index];
        }

        private SessionTracker UpdateSessionTracker(int minutes, int bip)
        {
            SessionTracker tracker;
            if (!_sessionByMinutes.TryGetValue(minutes, out tracker) || tracker == null)
            {
                tracker = new SessionTracker();
                _sessionByMinutes[minutes] = tracker;
            }

            DateTime barLocal = Times[bip][0].ToLocalTime();
            SessionBlock block = SessionBlock.Resolve(barLocal);
            tracker.Update(block.Key, block.Name, Highs[bip][0], Lows[bip][0]);
            return tracker;
        }

        private GlitchBridgeBusCompat.BridgeReading BuildBridgeReading(
            int bip,
            int minutes,
            SignalSnapshot signal,
            SessionTracker session)
        {
            DateTime readingUtc = DateTime.UtcNow;
            return new GlitchBridgeBusCompat.BridgeReading
            {
                InstrumentRoot = _instrumentRoot,
                InstrumentFullName = Instrument == null ? null : Instrument.FullName,
                Minutes = minutes,
                UtcTime = readingUtc,
                Open = Opens[bip][0],
                High = Highs[bip][0],
                Low = Lows[bip][0],
                Volume = Volumes[bip][0],
                CurrentPrice = ResolvePublishedCurrentPrice(signal.Close),
                AveragePrice = signal.AveragePrice,
                Atr = signal.Atr,
                Adx = signal.Adx,
                Score = signal.Score,
                RawScore = signal.RawScore,
                DirectionalScore = signal.DirectionalScore,
                TradeabilityScore = signal.TradeabilityScore,
                SignalLabel = ToSignalLabel(signal.Score),
                VolatilityHint = BuildVolatilityHint(signal.Atr, signal.Close),
                TrendHint = BuildTrendHint(signal.Adx, signal.Rsi, signal.StochK, signal.ZScore),
                RegimeLabel = signal.RegimeLabel,
                NoTradeReasons = signal.NoTradeReasons,
                Rsi = signal.Rsi,
                StochK = signal.StochK,
                ZScore = signal.ZScore,
                DiPlus = signal.DiPlus,
                DiMinus = signal.DiMinus,
                Cci = signal.Cci,
                MacdHistogram = signal.MacdHistogram,
                EmaAlignment = signal.EmaAlignment,
                RegimeWeight = signal.RegimeWeight,
                OscillatorCompositeScore = signal.OscillatorCompositeScore,
                MaCompositeScore = signal.MaCompositeScore,
                OrderFlowScore = signal.OrderFlowScore,
                OrderFlowConfidence = signal.OrderFlowConfidence,
                OrderFlowReliability = signal.OrderFlowReliability,
                OrderFlowCumulativeDelta = signal.OrderFlowCumulativeDelta,
                OrderFlowDeltaChange = signal.OrderFlowDeltaChange,
                OrderFlowVwap = signal.OrderFlowVwap,
                OrderFlowVwapDeviation = signal.OrderFlowVwapDeviation,
                OrderFlowAggressionBalance = signal.OrderFlowAggressionBalance,
                OrderFlowDepthImbalance = signal.OrderFlowDepthImbalance,
                OrderFlowHint = signal.OrderFlowHint,
                SessionName = session == null ? null : session.Name,
                SessionHigh = session == null ? null : session.CurrentHigh,
                SessionLow = session == null ? null : session.CurrentLow,
                PreviousSessionHigh = session == null ? null : session.PreviousHigh,
                PreviousSessionLow = session == null ? null : session.PreviousLow
            };
        }

        private int ResolveBipForMinutes(int minutes)
        {
            if (_minutesByBip == null)
                return -1;

            for (int bip = 0; bip < _minutesByBip.Length; bip++)
            {
                if (_minutesByBip[bip] == minutes)
                    return bip;
            }

            return -1;
        }

        private bool TryBuildRawTimeframeBar(int bip, int minutes, out GlitchMarketSnapshotRawJson.RawTimeframeBarPayload bar)
        {
            bar = null;
            if (bip < 0 || bip >= BarsArray.Length || CurrentBars[bip] < MinBarsForSignal)
                return false;

            double close = Closes[bip][0];
            if (close <= 0)
                return false;

            double? diPlus = null;
            double? diMinus = null;
            if (_dmByBip != null && bip < _dmByBip.Length && _dmByBip[bip] != null && CurrentBars[bip] >= 14)
            {
                diPlus = _dmByBip[bip].DiPlus[0];
                diMinus = _dmByBip[bip].DiMinus[0];
            }

            double? cci = null;
            if (_cciByBip != null && bip < _cciByBip.Length && _cciByBip[bip] != null && CurrentBars[bip] >= 20)
                cci = _cciByBip[bip][0];

            double? macdHistogram = null;
            if (_macdByBip != null && bip < _macdByBip.Length && _macdByBip[bip] != null && CurrentBars[bip] >= 35)
            {
                double macdMain = _macdByBip[bip].Default[0];
                double macdSignalLine = _macdByBip[bip].Avg[0];
                macdHistogram = macdMain - macdSignalLine;
            }

            double? orderFlowDelta = null;
            double? orderFlowDeltaChange = null;
            double? orderFlowVwap = null;
            double? orderFlowVwapDeviation = null;
            if (EnableOrderFlowLayer && _isOrderFlowRuntimeAvailable)
            {
                double atr = _atrByBip[bip][0];
                DateTime nowUtc = DateTime.UtcNow;
                double? orderFlowScore;
                double? orderFlowConfidence;
                double? orderFlowReliability;
                double? orderFlowAggressionBalance;
                double? orderFlowDepthImbalance;
                string orderFlowHint;
                TryBuildOrderFlowSnapshot(
                    bip,
                    close,
                    atr,
                    nowUtc,
                    false,
                    out orderFlowScore,
                    out orderFlowConfidence,
                    out orderFlowReliability,
                    out orderFlowDelta,
                    out orderFlowDeltaChange,
                    out orderFlowVwap,
                    out orderFlowVwapDeviation,
                    out orderFlowAggressionBalance,
                    out orderFlowDepthImbalance,
                    out orderFlowHint);
            }

            bar = new GlitchMarketSnapshotRawJson.RawTimeframeBarPayload
            {
                Minutes = minutes,
                UtcTime = Times[bip][0].ToUniversalTime(),
                Open = Opens[bip][0],
                High = Highs[bip][0],
                Low = Lows[bip][0],
                Close = close,
                Volume = Volumes[bip][0],
                Indicators = new GlitchMarketSnapshotRawJson.RawIndicatorsPayload
                {
                    Atr = _atrByBip[bip][0],
                    Adx = _adxByBip[bip][0],
                    Rsi = _rsiByBip[bip][0],
                    StochK = _stochByBip[bip].K[0],
                    ZScore = ComputeZScore(Closes[bip], Math.Min(ZLookback, CurrentBars[bip] + 1)),
                    AveragePrice = _smaByBip[bip][0],
                    DiPlus = diPlus,
                    DiMinus = diMinus,
                    Cci = cci,
                    MacdHistogram = macdHistogram,
                    OrderFlowCumulativeDelta = orderFlowDelta,
                    OrderFlowDeltaChange = orderFlowDeltaChange,
                    OrderFlowVwap = orderFlowVwap,
                    OrderFlowVwapDeviation = orderFlowVwapDeviation
                }
            };
            return true;
        }

        private void TryExportHistoricalMinuteSnapshot()
        {
            if (!EnableHistoricalSnapshotExport)
                return;

            // Strategy Analyzer: hosted indicators often stay State.Realtime with no chart surface.
            bool isStrategyAnalyzerHost = ChartControl == null;
            if (State != State.Historical && !(State == State.Realtime && isStrategyAnalyzerHost))
                return;

            if (ResolveMinutesForBip(0) != 1)
                return;

            if (CurrentBars == null || CurrentBars.Length == 0 || CurrentBars[0] < MinBarsForSignal)
                return;

            DateTime barCloseUtc = Times[0][0].ToUniversalTime();
            var bars = new List<GlitchMarketSnapshotRawJson.RawTimeframeBarPayload>(TargetMinutes.Length);
            SessionTracker primarySession = null;

            for (int i = 0; i < TargetMinutes.Length; i++)
            {
                int minutes = TargetMinutes[i];
                int bip = ResolveBipForMinutes(minutes);
                if (bip < 0 || bip >= CurrentBars.Length || CurrentBars[bip] < MinBarsForSignal)
                    return;

                GlitchMarketSnapshotRawJson.RawTimeframeBarPayload bar;
                if (!TryBuildRawTimeframeBar(bip, minutes, out bar))
                    return;

                SessionTracker session = UpdateSessionTracker(minutes, bip);
                bars.Add(bar);

                if (minutes == 1)
                    primarySession = session;
            }

            if (bars.Count != TargetMinutes.Length)
                return;

            var payload = new GlitchMarketSnapshotRawJson.RawInstrumentPayload
            {
                InstrumentRoot = _instrumentRoot,
                UpdatedUtc = barCloseUtc,
                SessionName = primarySession == null ? null : primarySession.Name,
                SessionHigh = primarySession == null ? null : primarySession.CurrentHigh,
                SessionLow = primarySession == null ? null : primarySession.CurrentLow,
                PreviousSessionHigh = primarySession == null ? null : primarySession.PreviousHigh,
                PreviousSessionLow = primarySession == null ? null : primarySession.PreviousLow,
                TimeframeBars = bars
            };

            bool wrote = GlitchHistoricalCorpusWriter.TryWriteMinuteSnapshot(
                HistoricalExportDirectory,
                _instrumentRoot,
                barCloseUtc,
                new[] { payload });

            if (wrote)
            {
                _historicalExportCount++;
                if (_historicalExportCount == 1 || (_historicalExportCount % 500) == 0)
                {
                    string directory = GlitchHistoricalCorpusWriter.ResolveExportDirectory(
                        HistoricalExportDirectory,
                        _instrumentRoot);
                    Log(
                        "Glitch historical corpus export count=" + _historicalExportCount.ToString(CultureInfo.InvariantCulture)
                        + " latest=" + barCloseUtc.ToString("o", CultureInfo.InvariantCulture)
                        + " dir=" + directory,
                        NinjaTrader.Cbi.LogLevel.Information);
                }
            }
        }

        private double ResolvePublishedCurrentPrice(double fallbackPrice)
        {
            if (_lastTradePrice > 0)
                return _lastTradePrice;

            return fallbackPrice > 0 ? fallbackPrice : 0;
        }

        private bool TryComputeHighLowMidpoint(int bip, int length, int barsAgo, out double midpoint)
        {
            midpoint = 0;
            if (length <= 0 || barsAgo < 0)
                return false;

            if (CurrentBars == null || bip < 0 || bip >= CurrentBars.Length)
                return false;
            if (Highs == null || Lows == null || bip >= Highs.Length || bip >= Lows.Length || Highs[bip] == null || Lows[bip] == null)
                return false;

            int oldestBarsAgo = barsAgo + length - 1;
            if (CurrentBars[bip] < oldestBarsAgo)
                return false;

            double highest = double.MinValue;
            double lowest = double.MaxValue;
            for (int i = barsAgo; i <= oldestBarsAgo; i++)
            {
                double high = Highs[bip][i];
                double low = Lows[bip][i];
                if (high > highest)
                    highest = high;
                if (low < lowest)
                    lowest = low;
            }

            if (highest <= double.MinValue || lowest >= double.MaxValue)
                return false;

            midpoint = (highest + lowest) * 0.5;
            return true;
        }

        private bool TryComputeMedianPriceAverage(int bip, int length, int barsAgo, out double average)
        {
            average = 0;
            if (length <= 0 || barsAgo < 0)
                return false;

            if (CurrentBars == null || bip < 0 || bip >= CurrentBars.Length)
                return false;
            if (Highs == null || Lows == null || bip >= Highs.Length || bip >= Lows.Length || Highs[bip] == null || Lows[bip] == null)
                return false;

            int oldestBarsAgo = barsAgo + length - 1;
            if (CurrentBars[bip] < oldestBarsAgo)
                return false;

            double sum = 0;
            for (int i = barsAgo; i <= oldestBarsAgo; i++)
                sum += (Highs[bip][i] + Lows[bip][i]) * 0.5;

            average = sum / length;
            return true;
        }

        private bool TryComputeAwesomeOscillatorSignal(int bip, double normalizationScale, out double signal)
        {
            signal = 0;
            if (normalizationScale <= 1e-8)
                return false;

            double fast;
            double slow;
            if (!TryComputeMedianPriceAverage(bip, 5, 0, out fast) ||
                !TryComputeMedianPriceAverage(bip, 34, 0, out slow))
            {
                return false;
            }

            double ao = fast - slow;
            double aoSlope = 0;
            double fastPrev;
            double slowPrev;
            if (TryComputeMedianPriceAverage(bip, 5, 1, out fastPrev) &&
                TryComputeMedianPriceAverage(bip, 34, 1, out slowPrev))
            {
                double aoPrev = fastPrev - slowPrev;
                aoSlope = (ao - aoPrev) / normalizationScale;
            }

            signal = Clamp(((ao / normalizationScale) * 0.85) + (aoSlope * 0.35), -1, 1);
            return true;
        }

        private bool TryComputeVolumeWeightedAverage(int bip, int length, out double vwma)
        {
            vwma = 0;
            if (length <= 0)
                return false;

            if (CurrentBars == null || bip < 0 || bip >= CurrentBars.Length)
                return false;
            if (Volumes == null || Closes == null || bip >= Volumes.Length || bip >= Closes.Length || Volumes[bip] == null || Closes[bip] == null)
                return false;

            if (CurrentBars[bip] < (length - 1))
                return false;

            double sumVol = 0;
            double sumPriceVol = 0;
            for (int i = 0; i < length; i++)
            {
                double volume = Volumes[bip][i];
                if (volume < 0)
                    volume = 0;

                sumVol += volume;
                sumPriceVol += Closes[bip][i] * volume;
            }

            if (sumVol <= 1e-8)
            {
                vwma = Closes[bip][0];
                return true;
            }

            vwma = sumPriceVol / sumVol;
            return true;
        }

        private static void AddMaVote(
            double close,
            double average,
            double tolerance,
            ref int buyVotes,
            ref int sellVotes,
            ref int neutralVotes,
            ref int totalVotes)
        {
            if (double.IsNaN(close) || double.IsNaN(average) || double.IsInfinity(close) || double.IsInfinity(average))
                return;

            totalVotes++;
            if (close > (average + tolerance))
            {
                buyVotes++;
                return;
            }

            if (close < (average - tolerance))
            {
                sellVotes++;
                return;
            }

            neutralVotes++;
        }

        private static void AddOscillatorVote(
            double signal,
            ref int buyVotes,
            ref int sellVotes,
            ref int neutralVotes,
            ref int totalVotes)
        {
            int vote = ResolveDirectionalVote(signal, 0.08);
            totalVotes++;
            if (vote > 0)
            {
                buyVotes++;
                return;
            }

            if (vote < 0)
            {
                sellVotes++;
                return;
            }

            neutralVotes++;
        }

        private static int ResolveDirectionalVote(double signal, double threshold)
        {
            if (signal > threshold)
                return 1;
            if (signal < -threshold)
                return -1;
            return 0;
        }

        private static double BuildRegimeWeight(double adx)
        {
            if (adx >= 45)
                return 1.00;
            if (adx >= 30)
                return 0.90;
            if (adx >= 20)
                return 0.75;
            return 0.55;
        }

        private static double BuildEmaAlignmentSignal(double emaFast, double emaMed, double emaSlow)
        {
            if (emaFast >= emaMed && emaMed >= emaSlow)
                return 1.0;
            if (emaFast <= emaMed && emaMed <= emaSlow)
                return -1.0;
            return Clamp(Math.Sign(emaFast - emaSlow) * 0.35, -1, 1);
        }

        private static string BuildVolatilityHint(double atr, double close)
        {
            double atrPct = close > 0 ? (atr / Math.Abs(close)) * 100.0 : 0;
            if (atrPct >= 0.80)
                return "High volatility regime";
            if (atrPct >= 0.30)
                return "Moderate volatility regime";
            return "Low volatility regime";
        }

        private static string BuildTrendHint(double adx, double rsi, double stochK, double zScore)
        {
            string regime;
            if (adx >= 45)
                regime = "Trending";
            else if (adx >= 25)
                regime = "Transitional";
            else
                regime = "Choppy";

            return string.Format(
                "{0} | ADX {1:N1} | RSI {2:N1} | Stoch {3:N1} | Z {4:+0.00;-0.00;0.00}",
                regime,
                adx,
                rsi,
                stochK,
                zScore);
        }

        private static string BuildOrderFlowHint(
            double score,
            double confidence,
            double reliability,
            double? cumulativeDelta,
            double? deltaChange,
            double? aggressionBalance,
            double? depthImbalance,
            double? vwapDeviation)
        {
            string polarity;
            if (score >= 0.35)
                polarity = "OF Buy Pressure";
            else if (score <= -0.35)
                polarity = "OF Sell Pressure";
            else
                polarity = "OF Balanced";

            string deltaToken = deltaChange.HasValue
                ? "D " + deltaChange.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture)
                : "D n/a";
            string cumulativeToken = cumulativeDelta.HasValue
                ? "Cum " + cumulativeDelta.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture)
                : "Cum n/a";
            string aggressionToken = aggressionBalance.HasValue
                ? "Agg " + aggressionBalance.Value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)
                : "Agg n/a";
            string depthToken = depthImbalance.HasValue
                ? "Depth " + depthImbalance.Value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)
                : "Depth n/a";
            string vwapToken = vwapDeviation.HasValue
                ? "VWAP " + vwapDeviation.Value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "s"
                : "VWAP n/a";

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} | C {1:0.00} | R {2:0.00} | {3} | {4} | {5} | {6} | {7}",
                polarity,
                confidence,
                reliability,
                deltaToken,
                cumulativeToken,
                aggressionToken,
                depthToken,
                vwapToken);
        }

        private string BuildRegimeLabel(double adx, double atr, double close)
        {
            double atrPct = close > 0 ? (atr / Math.Abs(close)) * 100.0 : 0;
            if (adx >= 30.0 && atrPct >= 0.60)
                return "Expansion";
            if (adx >= 25.0)
                return "Trend";
            if (adx < 18.0 && atrPct < 0.35)
                return "Compression";
            return "Chop";
        }

        private double BuildTradeabilityScore(
            double directionalScore,
            double adx,
            double regimeWeight,
            double? orderFlowReliability,
            string regimeLabel)
        {
            double directionalDistance = Clamp(
                (Math.Abs(directionalScore) - NeutralBand) / Math.Max(1.0 - NeutralBand, 1e-8),
                0,
                1);
            double adxQuality = Clamp((adx - 18.0) / 25.0, 0, 1);

            double regimeQuality;
            switch ((regimeLabel ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "EXPANSION":
                    regimeQuality = 0.85;
                    break;
                case "TREND":
                    regimeQuality = 0.78;
                    break;
                case "COMPRESSION":
                    regimeQuality = 0.25;
                    break;
                default:
                    regimeQuality = 0.35;
                    break;
            }

            double ofQuality = orderFlowReliability ?? (EnableOrderFlowLayer ? 0.25 : 0.50);
            double composite =
                (directionalDistance * 0.38) +
                (adxQuality * 0.24) +
                (regimeQuality * 0.23) +
                (ofQuality * 0.15);

            double regimeMultiplier = Clamp(regimeWeight / 0.90, 0.55, 1.10);
            return Clamp(composite * regimeMultiplier, 0, 1);
        }

        private string BuildNoTradeReasons(
            double directionalScore,
            double tradeabilityScore,
            string regimeLabel,
            double? orderFlowScore,
            double? orderFlowReliability)
        {
            var reasons = new List<string>(4);

            if (Math.Abs(directionalScore) < Math.Max(NeutralBand, 0.10))
                AddUniqueReason(reasons, "low directional edge");
            if (tradeabilityScore < 0.60)
                AddUniqueReason(reasons, "low tradeability");

            if (string.Equals(regimeLabel, "Chop", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(regimeLabel, "Compression", StringComparison.OrdinalIgnoreCase))
            {
                AddUniqueReason(reasons, "non-trending regime");
            }

            if (EnableOrderFlowLayer)
            {
                if (!orderFlowReliability.HasValue)
                    AddUniqueReason(reasons, "order flow unavailable");
                else if (orderFlowReliability.Value < 0.45)
                    AddUniqueReason(reasons, "order flow low reliability");

                if (orderFlowScore.HasValue)
                {
                    if (Math.Abs(orderFlowScore.Value) < 0.08)
                        AddUniqueReason(reasons, "order flow indecisive");
                    if (Math.Abs(directionalScore) >= Math.Max(NeutralBand, 0.08) &&
                        Math.Abs(orderFlowScore.Value) >= 0.12 &&
                        Math.Sign(directionalScore) != Math.Sign(orderFlowScore.Value))
                    {
                        AddUniqueReason(reasons, "signal-flow conflict");
                    }
                }
            }

            if (reasons.Count == 0)
                return string.Empty;

            if (reasons.Count > 3)
                reasons.RemoveRange(3, reasons.Count - 3);
            return string.Join(", ", reasons.ToArray());
        }

        private static void AddUniqueReason(List<string> reasons, string value)
        {
            if (reasons == null || string.IsNullOrWhiteSpace(value))
                return;

            for (int i = 0; i < reasons.Count; i++)
            {
                if (string.Equals(reasons[i], value, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            reasons.Add(value);
        }

        private static string ToSignalLabel(double score)
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

        private static Brush[] BuildPalette(Color low, Color high)
        {
            var palette = new Brush[PaletteLevels];
            for (int i = 0; i < PaletteLevels; i++)
            {
                double t = (double)i / (PaletteLevels - 1);
                byte r = LerpByte(low.R, high.R, t);
                byte g = LerpByte(low.G, high.G, t);
                byte b = LerpByte(low.B, high.B, t);
                var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
                brush.Freeze();
                palette[i] = brush;
            }

            return palette;
        }

        private static byte LerpByte(byte start, byte end, double t)
        {
            return (byte)Math.Round(start + ((end - start) * t));
        }

        private static double ComputeZScore(ISeries<double> series, int length)
        {
            if (series == null || length < 2)
                return 0;

            double sum = 0;
            for (int i = 0; i < length; i++)
                sum += series[i];

            double mean = sum / length;
            double variance = 0;
            for (int i = 0; i < length; i++)
            {
                double diff = series[i] - mean;
                variance += diff * diff;
            }

            double stdDev = Math.Sqrt(variance / length);
            if (stdDev <= 1e-8)
                return 0;

            return (series[0] - mean) / stdDev;
        }

        private static int ResolveMinutes(BarsPeriodType periodType, int value)
        {
            if (periodType == BarsPeriodType.Minute && value > 0)
                return value;
            if (periodType == BarsPeriodType.Day && value > 0)
                return value * 1440;
            if (periodType == BarsPeriodType.Week && value > 0)
                return value * 10080;
            return -1;
        }

        private static bool IsTrackedMinutes(int minutes)
        {
            for (int i = 0; i < TargetMinutes.Length; i++)
            {
                if (TargetMinutes[i] == minutes)
                    return true;
            }

            return false;
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

        private static void AccumulateDirectionalFactor(
            ref double weightedCore,
            ref double weightSum,
            double signal,
            double weight)
        {
            if (weight <= 0)
                return;

            weightedCore += Clamp(signal, -1, 1) * weight;
            weightSum += weight;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private struct SignalSnapshot
        {
            public double Close;
            public double AveragePrice;
            public double Atr;
            public double Adx;
            public double Rsi;
            public double StochK;
            public double ZScore;
            public double? DiPlus;
            public double? DiMinus;
            public double? Cci;
            public double? MacdHistogram;
            public double EmaAlignment;
            public double RegimeWeight;
            public double OscillatorCompositeScore;
            public double MaCompositeScore;
            public double Score;
            public double RawScore;
            public double DirectionalScore;
            public double TradeabilityScore;
            public string RegimeLabel;
            public string NoTradeReasons;
            public double? OrderFlowScore;
            public double? OrderFlowConfidence;
            public double? OrderFlowReliability;
            public double? OrderFlowCumulativeDelta;
            public double? OrderFlowDeltaChange;
            public double? OrderFlowVwap;
            public double? OrderFlowVwapDeviation;
            public double? OrderFlowAggressionBalance;
            public double? OrderFlowDepthImbalance;
            public string OrderFlowHint;
        }

        private struct OrderFlowTapeSample
        {
            public DateTime UtcTime;
            public double BuyVolume;
            public double SellVolume;
        }

        private sealed class SessionTracker
        {
            public string SessionKey { get; private set; }
            public string Name { get; private set; }
            public double? CurrentHigh { get; private set; }
            public double? CurrentLow { get; private set; }
            public double? PreviousHigh { get; private set; }
            public double? PreviousLow { get; private set; }

            public void Update(string sessionKey, string name, double high, double low)
            {
                if (string.IsNullOrWhiteSpace(sessionKey))
                    return;

                if (!string.Equals(SessionKey, sessionKey, StringComparison.Ordinal))
                {
                    if (CurrentHigh.HasValue && CurrentLow.HasValue)
                    {
                        PreviousHigh = CurrentHigh;
                        PreviousLow = CurrentLow;
                    }

                    SessionKey = sessionKey;
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
            public SessionBlock(string name, DateTime startLocal)
            {
                Name = name;
                Key = name + "|" + startLocal.ToString("yyyyMMddHH");
            }

            public string Name { get; }
            public string Key { get; }

            public static SessionBlock Resolve(DateTime nowLocal)
            {
                DateTime day = nowLocal.Date;
                int hour = nowLocal.Hour;

                if (hour >= 8 && hour < 16)
                    return new SessionBlock("NYC", day.AddHours(8));
                if (hour >= 3 && hour < 8)
                    return new SessionBlock("London", day.AddHours(3));
                if (hour >= 16)
                    return new SessionBlock("Asia", day.AddHours(16));

                return new SessionBlock("Asia", day.AddDays(-1).AddHours(16));
            }
        }
    }
}

