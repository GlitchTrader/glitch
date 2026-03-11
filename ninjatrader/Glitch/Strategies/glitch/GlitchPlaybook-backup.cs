#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class GlitchPlaybook : Strategy
    {
        private static readonly int[] TargetMinutes = { 1, 5, 15, 60 };
        private const int MinBarsForSignal = 30;
        private const int ZLookback = 30;
        private const string StrategyProfileVersion = "GlitchPlaybook_v1";
        private const double AnalyticsUnifiedNeutralThreshold = 0.10;
        private const int OrderFlowDepthLevels = 6;
        private const int TrendBreakoutLookbackBars = 12;
        private const double TrendBreakoutAtrBufferMultiplier = 0.10;
        private const double MinDirectionalEnergyForSetup = 0.18;
        private const int ReversalPivotStrength = 2;
        private const int ReversalPivotLookbackBars = 120;
        private const double ReversalStructureImpulseAtr = 0.08;
        private static readonly TimeSpan OrderFlowTapeWindow = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan OrderFlowDepthFreshness = TimeSpan.FromSeconds(20);

        private EMA[] _emaFastByBip;
        private EMA[] _emaMedByBip;
        private EMA[] _emaSlowByBip;
        private ATR[] _atrByBip;
        private ADX[] _adxByBip;
        private RSI[] _rsiByBip;
        private Stochastics[] _stochByBip;
        private SMA[] _smaByBip;

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

        private int[] _minutesByBip;
        private bool[] _isTrackedByBip;
        private readonly Dictionary<int, int> _bipByMinutes = new Dictionary<int, int>();

        private Series<double> _triggerScoreSeries;
        private double _tickSize;

        private int _lastEntryBar = -1;
        private int _entriesToday;
        private DateTime _currentTradeDate = DateTime.MinValue;
        private double _dailyStartCumProfit;
        private bool _dailyStateInitialized;
        private bool _dailyTradingLocked;
        private string _dailyLockReason = string.Empty;
        private bool _dailyFlattenSubmitted;
        private double _lastKnownCumProfit;

        private StreamWriter _telemetryWriter;
        private string _telemetryFilePath;
        private bool _telemetryHeaderWritten;
        private long _telemetryRowCount;
        private long _telemetryBarsSeen;
        private bool _hasLoggedTelemetryWriteError;
        private bool _telemetryRuntimeEnabled;
        private double _lastScore1;
        private double _lastScore5;
        private double _lastScore15;
        private double _lastScore60;
        private bool _skipOrderFlowRuntime;
        private bool _trailInitialized;
        private MarketPosition _trailDirection = MarketPosition.Flat;
        private double _trailEntryPrice;
        private double _trailExtremePrice;
        private double _trailActiveStopPrice;
        private double _trailDistancePoints;
        private double _trailTp1TargetPrice;
        private bool _trailTp1Touched;
        private string _lastArchetype = "none";
        private string _activeLongCoreSignalName = LongCoreSignalName;
        private string _activeLongRunnerSignalName = LongRunnerSignalName;
        private string _activeShortCoreSignalName = ShortCoreSignalName;
        private string _activeShortRunnerSignalName = ShortRunnerSignalName;

        private const string LongCoreSignalName = "L-Core";
        private const string LongRunnerSignalName = "L-Runner";
        private const string ShortCoreSignalName = "S-Core";
        private const string ShortRunnerSignalName = "S-Runner";

        [NinjaScriptProperty]
        [Range(1, 60)]
        [Display(Name = "Execution Timeframe (min)", Order = 1, GroupName = "Playbooks")]
        public int ExecutionTimeframeMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Confidence Gate %", Order = 2, GroupName = "Playbooks")]
        public int ConfidenceGateThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Trend Playbook", Order = 3, GroupName = "Playbooks")]
        public bool EnableTrendPlaybook { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Pullback Playbook", Order = 4, GroupName = "Playbooks")]
        public bool EnablePullbackPlaybook { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Reversal Playbook", Order = 5, GroupName = "Playbooks")]
        public bool EnableReversalPlaybook { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Trend Min Confidence %", Order = 6, GroupName = "Playbooks")]
        public int TrendMinConfidence { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Pullback Min Confidence %", Order = 7, GroupName = "Playbooks")]
        public int PullbackMinConfidence { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Reversal Min Confidence %", Order = 8, GroupName = "Playbooks")]
        public int ReversalMinConfidence { get; set; }

        [NinjaScriptProperty]
        [Range(0.00, 1.00)]
        [Display(Name = "Reversal Divergence Min", Order = 9, GroupName = "Playbooks")]
        public double ReversalDivergenceThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Fallback Threshold Entries", Order = 10, GroupName = "Playbooks")]
        public bool EnableFallbackThresholdEntry { get; set; }

        [NinjaScriptProperty]
        [Range(0.10, 1.00)]
        [Display(Name = "Long Entry Threshold", Order = 1, GroupName = "Signals")]
        public double LongEntryThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(-1.00, -0.10)]
        [Display(Name = "Short Entry Threshold", Order = 2, GroupName = "Signals")]
        public double ShortEntryThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(-0.50, 0.50)]
        [Display(Name = "Long Exit Threshold", Order = 3, GroupName = "Signals")]
        public double LongExitThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(-0.50, 0.50)]
        [Display(Name = "Short Exit Threshold", Order = 4, GroupName = "Signals")]
        public double ShortExitThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Immediate Reversal", Order = 5, GroupName = "Signals")]
        public bool AllowImmediateReversal { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 6, GroupName = "Signals")]
        public bool EnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(5, 70)]
        [Display(Name = "Min ADX For Entries", Order = 7, GroupName = "Signals")]
        public double MinAdxForEntries { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Neutral Zone Filter", Order = 1, GroupName = "Trade Quality")]
        public bool EnableNeutralZoneFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0.00, 0.50)]
        [Display(Name = "Neutral Zone Half Width", Order = 2, GroupName = "Trade Quality")]
        public double NeutralZoneHalfWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Score Slope Filter", Order = 3, GroupName = "Trade Quality")]
        public bool EnableScoreSlopeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0.00, 0.50)]
        [Display(Name = "Min Score Delta For Entry", Order = 4, GroupName = "Trade Quality")]
        public double MinScoreDeltaForEntry { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Base Score Alignment", Order = 5, GroupName = "Trade Quality")]
        public bool RequireBaseScoreAlignment { get; set; }

        [NinjaScriptProperty]
        [Range(0, 300)]
        [Display(Name = "Min Bars Between Entries", Order = 6, GroupName = "Trade Quality")]
        public int MinBarsBetweenEntries { get; set; }

        [NinjaScriptProperty]
        [Range(0, 300)]
        [Display(Name = "Min Bars In Position", Order = 7, GroupName = "Trade Quality")]
        public int MinBarsInPosition { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Choppiness Filter", Order = 8, GroupName = "Trade Quality")]
        public bool EnableChoppinessFilter { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Choppiness Period", Order = 9, GroupName = "Trade Quality")]
        public int ChoppinessPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(20.0, 90.0)]
        [Display(Name = "Choppiness Threshold", Order = 10, GroupName = "Trade Quality")]
        public double ChoppinessThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Stop Loss", Order = 1, GroupName = "Risk")]
        public bool UseStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.25, 10.0)]
        [Display(Name = "Stop ATR Multiplier", Order = 2, GroupName = "Risk")]
        public double StopAtrMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Profit Target", Order = 3, GroupName = "Risk")]
        public bool UseProfitTarget { get; set; }

        [NinjaScriptProperty]
        [Range(0.25, 20.0)]
        [Display(Name = "Target ATR Multiplier", Order = 4, GroupName = "Risk")]
        public double TargetAtrMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable FailFast Exit", Order = 5, GroupName = "Risk")]
        public bool EnableFailFast { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "FailFast Max Bars", Order = 6, GroupName = "Risk")]
        public int FailFastBars { get; set; }

        [NinjaScriptProperty]
        [Range(-1.00, 1.00)]
        [Display(Name = "FailFast Long Score", Order = 7, GroupName = "Risk")]
        public double FailFastLongScore { get; set; }

        [NinjaScriptProperty]
        [Range(-1.00, 1.00)]
        [Display(Name = "FailFast Short Score", Order = 8, GroupName = "Risk")]
        public double FailFastShortScore { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Two Leg Position", Order = 9, GroupName = "Risk")]
        public bool EnableTwoLegPosition { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Core Quantity", Order = 10, GroupName = "Risk")]
        public int CoreQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Runner Quantity", Order = 11, GroupName = "Risk")]
        public int RunnerQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(0.25, 10.0)]
        [Display(Name = "Core Target ATR Mult", Order = 12, GroupName = "Risk")]
        public double CoreTargetAtrMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.25, 25.0)]
        [Display(Name = "Runner Target ATR Mult", Order = 13, GroupName = "Risk")]
        public double RunnerTargetAtrMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Runner ATR Trail", Order = 14, GroupName = "Risk")]
        public bool EnableRunnerAtrTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0.10, 20.0)]
        [Display(Name = "Runner Trail ATR Mult", Order = 15, GroupName = "Risk")]
        public double RunnerTrailAtrMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Min Runner Trail Ticks", Order = 16, GroupName = "Risk")]
        public int MinRunnerTrailTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Start Trail After TP1", Order = 17, GroupName = "Risk")]
        public bool DelayRunnerTrailUntilTp1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Order Flow Layer", Order = 1, GroupName = "Order Flow")]
        public bool EnableOrderFlowLayer { get; set; }

        [NinjaScriptProperty]
        [Range(0.00, 1.00)]
        [Display(Name = "Order Flow Blend", Order = 2, GroupName = "Order Flow")]
        public double OrderFlowBlend { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Order Flow Confirmation", Order = 3, GroupName = "Order Flow")]
        public bool RequireOrderFlowConfirmation { get; set; }

        [NinjaScriptProperty]
        [Range(-1.00, 1.00)]
        [Display(Name = "Min OF Score For Long", Order = 4, GroupName = "Order Flow")]
        public double MinOrderFlowScoreForLong { get; set; }

        [NinjaScriptProperty]
        [Range(-1.00, 1.00)]
        [Display(Name = "Max OF Score For Short", Order = 5, GroupName = "Order Flow")]
        public double MaxOrderFlowScoreForShort { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Session Time Filter", Order = 1, GroupName = "Session")]
        public bool UseSessionTimeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Session Start (HHmmss)", Order = 2, GroupName = "Session")]
        public int SessionStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Session End (HHmmss)", Order = 3, GroupName = "Session")]
        public int SessionEndTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 300)]
        [Display(Name = "Max Entries Per Day", Order = 1, GroupName = "Daily Controls")]
        public int MaxEntriesPerDay { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Daily PnL Guard", Order = 2, GroupName = "Daily Controls")]
        public bool UseDailyPnlGuard { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "Daily Profit Target $", Order = 3, GroupName = "Daily Controls")]
        public double DailyProfitTargetCurrency { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "Daily Loss Limit $", Order = 4, GroupName = "Daily Controls")]
        public double DailyLossLimitCurrency { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flatten On Daily Guard", Order = 5, GroupName = "Daily Controls")]
        public bool FlattenOnDailyGuardHit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Fast Backtest Mode", Order = 1, GroupName = "Performance")]
        public bool FastBacktestMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip OF In Fast Backtest", Order = 2, GroupName = "Performance")]
        public bool SkipOrderFlowInFastBacktest { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Telemetry Export", Order = 1, GroupName = "Telemetry")]
        public bool EnableTelemetryExport { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Export Historical Telemetry", Order = 2, GroupName = "Telemetry")]
        public bool ExportHistoricalTelemetry { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Telemetry File Prefix", Order = 3, GroupName = "Telemetry")]
        public string TelemetryFilePrefix { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Telemetry Auto Flush", Order = 4, GroupName = "Telemetry")]
        public bool TelemetryAutoFlush { get; set; }

        [NinjaScriptProperty]
        [Range(1, 2000)]
        [Display(Name = "Flush Interval Bars", Order = 5, GroupName = "Telemetry")]
        public int TelemetryFlushIntervalBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Execution Rows", Order = 6, GroupName = "Telemetry")]
        public bool EnableExecutionTelemetryRows { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Glitch multi-timeframe playbook strategy with indicator-parity score model.";
                Name = "GlitchPlaybook";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 4;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = MinBarsForSignal;
                IsInstantiatedOnEachOptimizationIteration = false;

                ExecutionTimeframeMinutes = 5;
                ConfidenceGateThreshold = 60;
                EnableTrendPlaybook = true;
                EnablePullbackPlaybook = true;
                EnableReversalPlaybook = true;
                TrendMinConfidence = 60;
                PullbackMinConfidence = 55;
                ReversalMinConfidence = 65;
                ReversalDivergenceThreshold = 0.25;
                EnableFallbackThresholdEntry = false;

                LongEntryThreshold = 0.55;
                ShortEntryThreshold = -0.55;
                LongExitThreshold = 0.05;
                ShortExitThreshold = -0.05;
                AllowImmediateReversal = false;
                EnableAdxFilter = true;
                MinAdxForEntries = 28.0;

                EnableNeutralZoneFilter = true;
                NeutralZoneHalfWidth = 0.12;
                EnableScoreSlopeFilter = true;
                MinScoreDeltaForEntry = 0.02;
                RequireBaseScoreAlignment = true;
                MinBarsBetweenEntries = 12;
                MinBarsInPosition = 2;
                EnableChoppinessFilter = true;
                ChoppinessPeriod = 14;
                ChoppinessThreshold = 61.8;

                UseStopLoss = true;
                StopAtrMultiplier = 1.50;
                UseProfitTarget = true;
                TargetAtrMultiplier = 2.00;
                EnableFailFast = true;
                FailFastBars = 3;
                FailFastLongScore = -0.05;
                FailFastShortScore = 0.05;
                EnableTwoLegPosition = true;
                CoreQuantity = 1;
                RunnerQuantity = 1;
                CoreTargetAtrMultiplier = 0.9;
                RunnerTargetAtrMultiplier = 5.0;
                EnableRunnerAtrTrail = true;
                RunnerTrailAtrMultiplier = 1.5;
                MinRunnerTrailTicks = 8;
                DelayRunnerTrailUntilTp1 = true;

                EnableOrderFlowLayer = true;
                OrderFlowBlend = 0.80;
                RequireOrderFlowConfirmation = false;
                MinOrderFlowScoreForLong = 0.05;
                MaxOrderFlowScoreForShort = -0.05;

                UseSessionTimeFilter = true;
                SessionStartTime = 93000;
                SessionEndTime = 155500;

                MaxEntriesPerDay = 12;
                UseDailyPnlGuard = true;
                DailyProfitTargetCurrency = 250;
                DailyLossLimitCurrency = 250;
                FlattenOnDailyGuardHit = true;

                FastBacktestMode = true;
                SkipOrderFlowInFastBacktest = true;
                EnableTelemetryExport = false;
                ExportHistoricalTelemetry = false;
                TelemetryFilePrefix = "GlitchPlaybook";
                TelemetryAutoFlush = false;
                TelemetryFlushIntervalBars = 250;
                EnableExecutionTelemetryRows = false;
            }
            else if (State == State.Configure)
            {
                AddMissingTimeframeSeries();
                _skipOrderFlowRuntime = EnableOrderFlowLayer && FastBacktestMode && SkipOrderFlowInFastBacktest;
                if (EnableOrderFlowLayer && !_skipOrderFlowRuntime)
                    AddOrderFlowTickSeries();
            }
            else if (State == State.DataLoaded)
            {
                _tickSize = (Instrument != null && Instrument.MasterInstrument != null && Instrument.MasterInstrument.TickSize > 0)
                    ? Instrument.MasterInstrument.TickSize
                    : 0.25;
                _triggerScoreSeries = new Series<double>(this);

                InitializeSeriesMetadata();
                InitializeIndicators();
                _orderFlowTickBip = _skipOrderFlowRuntime ? -1 : ResolveOrderFlowTickBip();
                if (_skipOrderFlowRuntime)
                {
                    _orderFlowDeltaByBip = null;
                    _orderFlowVwapByBip = null;
                    _isOrderFlowRuntimeAvailable = false;
                    _hasLoggedOrderFlowUnavailable = false;
                }
                else
                {
                    InitializeOrderFlowLayer();
                }

                _lastEntryBar = -1;
                _entriesToday = 0;
                _currentTradeDate = DateTime.MinValue;
                _dailyStartCumProfit = 0;
                _dailyStateInitialized = false;
                _dailyTradingLocked = false;
                _dailyLockReason = string.Empty;
                _dailyFlattenSubmitted = false;
                _lastKnownCumProfit = 0;
                ResetRunnerTrailState();

                _telemetryBarsSeen = 0;
                _telemetryRowCount = 0;
                _telemetryHeaderWritten = false;
                _hasLoggedTelemetryWriteError = false;
                _telemetryRuntimeEnabled = EnableTelemetryExport && !FastBacktestMode;
                if (_telemetryRuntimeEnabled)
                    InitializeTelemetryWriter();
                _telemetryRuntimeEnabled = _telemetryWriter != null;
            }
            else if (State == State.Terminated)
            {
                CloseTelemetryWriter();
            }
        }

        protected override void OnBarUpdate()
        {
            if (_isOrderFlowRuntimeAvailable && _orderFlowTickBip > 0 && BarsInProgress == _orderFlowTickBip)
            {
                RefreshOrderFlowFromTickSeries();
                return;
            }

            if (BarsInProgress != 0)
                return;

            if (_telemetryRuntimeEnabled)
                _telemetryBarsSeen++;

            if (Position.MarketPosition == MarketPosition.Flat)
                _dailyFlattenSubmitted = false;

            Dictionary<int, SignalSnapshot> signals = BuildSignalSet();
            PlaybookDecision decision = BuildPlaybookDecision(signals, ResolveExecutionTimeframeMinutes(ExecutionTimeframeMinutes));
            _triggerScoreSeries[0] = decision.TriggerScore;
            decision.PrevTriggerScore = CurrentBar > 0 ? _triggerScoreSeries[1] : decision.TriggerScore;
            decision.ScoreDelta = decision.TriggerScore - decision.PrevTriggerScore;

            SignalSnapshot executionSignal = GetExecutionSignal(signals, decision.ExecutionMinutes);
            EvaluateTradingSignals(executionSignal, ref decision, Times[0][0]);

            WriteTelemetry(executionSignal, decision, signals, "bar", decision.Action, decision.Reason, string.Empty, null, null, null, string.Empty, Times[0][0]);
        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (!_isOrderFlowRuntimeAvailable || marketDataUpdate == null)
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

            double tradeVolume = marketDataUpdate.Volume > 0 ? marketDataUpdate.Volume : 0;
            if (tradeVolume <= 0)
                return;

            double previousTradePrice = _lastTradePrice;
            _lastTradePrice = tradePrice;

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

            double volume = Math.Max(0, marketDepthUpdate.Volume);
            if (marketDepthUpdate.MarketDataType == MarketDataType.Ask)
                _depthAskByLevel[level] = volume;
            else if (marketDepthUpdate.MarketDataType == MarketDataType.Bid)
                _depthBidByLevel[level] = volume;
            else
                return;

            _lastDepthUpdateUtc = DateTime.UtcNow;
        }

        protected override void OnExecutionUpdate(
            Execution execution,
            string executionId,
            double price,
            int quantity,
            MarketPosition marketPosition,
            string orderId,
            DateTime time)
        {
            if (!_telemetryRuntimeEnabled || !EnableExecutionTelemetryRows)
                return;
            if (_telemetryWriter == null)
                return;
            if (State == State.Historical && !ExportHistoricalTelemetry)
                return;
            if (execution == null)
                return;

            var signals = new Dictionary<int, SignalSnapshot>(4)
            {
                { 1, new SignalSnapshot { Score = _lastScore1 } },
                { 5, new SignalSnapshot { Score = _lastScore5 } },
                { 15, new SignalSnapshot { Score = _lastScore15 } },
                { 60, new SignalSnapshot { Score = _lastScore60 } }
            };
            SignalSnapshot signal = new SignalSnapshot
            {
                Close = price,
                Score = ResolveExecutionScore(signals, ResolveExecutionTimeframeMinutes(ExecutionTimeframeMinutes)),
                SignalLabel = string.Empty
            };
            PlaybookDecision decision = BuildWarmupPlaybookDecision();
            WriteTelemetry(
                signal,
                decision,
                signals,
                "execution",
                "execution_fill",
                execution.Order == null ? "execution_fill" : execution.Order.Name,
                execution.Order == null ? string.Empty : execution.Order.OrderAction.ToString(),
                price,
                quantity,
                marketPosition,
                orderId ?? executionId,
                time);
        }

        private void EvaluateTradingSignals(SignalSnapshot signal, ref PlaybookDecision decision, DateTime barTime)
        {
            double cumProfit = ResolveCumProfitSnapshot();
            RollDailyState(barTime, cumProfit);
            double dailyRealized = _dailyStateInitialized ? cumProfit - _dailyStartCumProfit : 0;
            bool lockTriggered = UpdateDailyLockState(dailyRealized);

            decision.DailyRealizedPnl = dailyRealized;
            decision.EntriesToday = _entriesToday;
            decision.BarsSinceLastEntry = _lastEntryBar >= 0 ? CurrentBar - _lastEntryBar : int.MaxValue;
            decision.BarsSincePositionEntry =
                Position.MarketPosition != MarketPosition.Flat && _lastEntryBar >= 0
                    ? CurrentBar - _lastEntryBar
                    : int.MaxValue;
            decision.OrderFlowAvailable = signal.OrderFlowScore.HasValue;
            decision.DailyLockActive = _dailyTradingLocked;
            decision.DailyLockReason = _dailyLockReason;

            decision.SessionPass = !UseSessionTimeFilter || IsInsideSessionWindow(barTime);
            decision.DailyPass = !decision.DailyLockActive;
            decision.MaxEntriesPass = MaxEntriesPerDay <= 0 || _entriesToday < MaxEntriesPerDay;
            decision.CooldownPass = MinBarsBetweenEntries <= 0 || decision.BarsSinceLastEntry >= MinBarsBetweenEntries;
            decision.NeutralPass = !EnableNeutralZoneFilter || Math.Abs(decision.TriggerScore) >= NeutralZoneHalfWidth;
            decision.AdxPass = !EnableAdxFilter || decision.AdxProxy >= MinAdxForEntries;
            decision.ChoppinessPass = !EnableChoppinessFilter || !IsChoppyRegime(signal.Bips);
            decision.SlopeLongPass = !EnableScoreSlopeFilter || decision.ScoreDelta >= MinScoreDeltaForEntry;
            decision.SlopeShortPass = !EnableScoreSlopeFilter || decision.ScoreDelta <= -MinScoreDeltaForEntry;
            decision.BaseLongPass = !RequireBaseScoreAlignment || decision.ExecutionBaseScore >= 0.05;
            decision.BaseShortPass = !RequireBaseScoreAlignment || decision.ExecutionBaseScore <= -0.05;
            decision.OrderFlowLongPass =
                !RequireOrderFlowConfirmation ||
                !signal.OrderFlowScore.HasValue ||
                signal.OrderFlowScore.Value >= MinOrderFlowScoreForLong;
            decision.OrderFlowShortPass =
                !RequireOrderFlowConfirmation ||
                !signal.OrderFlowScore.HasValue ||
                signal.OrderFlowScore.Value <= MaxOrderFlowScoreForShort;
            decision.MinHoldPass =
                MinBarsInPosition <= 0 ||
                Position.MarketPosition == MarketPosition.Flat ||
                decision.BarsSincePositionEntry >= MinBarsInPosition;
            decision.EntryGatePass =
                decision.SessionPass &&
                decision.DailyPass &&
                decision.MaxEntriesPass &&
                decision.CooldownPass &&
                decision.NeutralPass &&
                decision.AdxPass &&
                decision.ChoppinessPass;

            ResolveEntrySetups(signal, decision, out bool longSetup, out bool shortSetup, out string archetype);
            decision.LongSetup = longSetup;
            decision.ShortSetup = shortSetup;
            decision.EntryArchetype = archetype;

            double triggerAbs = Math.Abs(decision.TriggerScore);
            double thresholdAbs = Math.Max(Math.Abs(LongEntryThreshold), Math.Abs(ShortEntryThreshold));
            bool strongDirectionalImpulse =
                decision.SetupKind == 1 &&
                decision.BiasSign != 0 &&
                triggerAbs >= thresholdAbs &&
                decision.AdxProxy >= Math.Max(22.0, MinAdxForEntries);
            bool playbookReady = decision.PlaybookActionable || strongDirectionalImpulse || EnableFallbackThresholdEntry;
            if (strongDirectionalImpulse)
                decision.PlaybookActionable = true;

            bool isReversalStructureEntry =
                (decision.LongSetup || decision.ShortSetup) &&
                string.Equals(decision.EntryArchetype, "reversal_structure_g", StringComparison.Ordinal);
            bool coreRiskPass =
                decision.SessionPass &&
                decision.DailyPass &&
                decision.MaxEntriesPass &&
                decision.CooldownPass;

            decision.LongAllowed =
                coreRiskPass &&
                decision.OrderFlowLongPass &&
                (isReversalStructureEntry
                    ? decision.LongSetup
                    : (decision.EntryGatePass &&
                       decision.SlopeLongPass &&
                       decision.BaseLongPass &&
                       playbookReady));
            decision.ShortAllowed =
                coreRiskPass &&
                decision.OrderFlowShortPass &&
                (isReversalStructureEntry
                    ? decision.ShortSetup
                    : (decision.EntryGatePass &&
                       decision.SlopeShortPass &&
                       decision.BaseShortPass &&
                       playbookReady));

            if (lockTriggered &&
                FlattenOnDailyGuardHit &&
                Position.MarketPosition != MarketPosition.Flat &&
                !_dailyFlattenSubmitted)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    ExitLong("L-DailyGuard-Core", _activeLongCoreSignalName);
                    ExitLong("L-DailyGuard-Runner", _activeLongRunnerSignalName);
                }
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    ExitShort("S-DailyGuard-Core", _activeShortCoreSignalName);
                    ExitShort("S-DailyGuard-Runner", _activeShortRunnerSignalName);
                }

                _dailyFlattenSubmitted = true;
                return;
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ResetRunnerTrailState();
                if (decision.LongSetup && decision.LongAllowed)
                {
                    SubmitPlaybookEntry(true, signal, decision.EntryArchetype);
                    MarkEntry(barTime);
                    decision.Action = "enter_long";
                    decision.Reason = "playbook_long_" + (decision.EntryArchetype ?? "unknown");
                    return;
                }

                if (decision.ShortSetup && decision.ShortAllowed)
                {
                    SubmitPlaybookEntry(false, signal, decision.EntryArchetype);
                    MarkEntry(barTime);
                    decision.Action = "enter_short";
                    decision.Reason = "playbook_short_" + (decision.EntryArchetype ?? "unknown");
                    return;
                }

                if (decision.LongSetup)
                    decision.GateReason = BuildEntryBlockReason("long", decision);
                else if (decision.ShortSetup)
                    decision.GateReason = BuildEntryBlockReason("short", decision);
                return;
            }

            if (Position.MarketPosition == MarketPosition.Long)
            {
                UpdateRunnerTrail(MarketPosition.Long, signal);
                bool exitForSignal = decision.TriggerScore <= LongExitThreshold && decision.MinHoldPass;
                bool exitFailFast =
                    EnableFailFast &&
                    decision.BarsSincePositionEntry <= FailFastBars &&
                    decision.TriggerScore <= FailFastLongScore;
                bool reverseSignal = AllowImmediateReversal && decision.ShortSetup && decision.ShortAllowed && decision.MinHoldPass;

                if (exitForSignal || exitFailFast || reverseSignal)
                {
                    ExitLong("L-ExitSignal-Core", _activeLongCoreSignalName);
                    ExitLong("L-ExitSignal-Runner", _activeLongRunnerSignalName);
                    if (reverseSignal)
                    {
                        SubmitPlaybookEntry(false, signal, "signal_flip");
                        MarkEntry(barTime);
                    }
                }
                return;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                UpdateRunnerTrail(MarketPosition.Short, signal);
                bool exitForSignal = decision.TriggerScore >= ShortExitThreshold && decision.MinHoldPass;
                bool exitFailFast =
                    EnableFailFast &&
                    decision.BarsSincePositionEntry <= FailFastBars &&
                    decision.TriggerScore >= FailFastShortScore;
                bool reverseSignal = AllowImmediateReversal && decision.LongSetup && decision.LongAllowed && decision.MinHoldPass;

                if (exitForSignal || exitFailFast || reverseSignal)
                {
                    ExitShort("S-ExitSignal-Core", _activeShortCoreSignalName);
                    ExitShort("S-ExitSignal-Runner", _activeShortRunnerSignalName);
                    if (reverseSignal)
                    {
                        SubmitPlaybookEntry(true, signal, "signal_flip");
                        MarkEntry(barTime);
                    }
                }
            }
        }

        private void ResolveEntrySetups(SignalSnapshot signal, PlaybookDecision decision, out bool longSetup, out bool shortSetup, out string archetype)
        {
            longSetup = false;
            shortSetup = false;
            archetype = "none";

            bool crossLong = decision.TriggerScore >= LongEntryThreshold && decision.PrevTriggerScore < LongEntryThreshold;
            bool crossShort = decision.TriggerScore <= ShortEntryThreshold && decision.PrevTriggerScore > ShortEntryThreshold;
            bool hasDirectionalEnergy =
                Math.Abs(decision.ContextScore) >= MinDirectionalEnergyForSetup ||
                Math.Abs(decision.TriggerScore) >= MinDirectionalEnergyForSetup;
            bool breakoutLong = false;
            bool breakoutShort = false;

            if (hasDirectionalEnergy &&
                signal.Bips >= 0 &&
                TryGetBreakoutLevels(signal.Bips, TrendBreakoutLookbackBars, out double priorHigh, out double priorLow))
            {
                double buffer = Math.Max(_tickSize, Math.Max(_tickSize, signal.Atr) * TrendBreakoutAtrBufferMultiplier);
                breakoutLong =
                    decision.BiasSign > 0 &&
                    decision.TriggerScore >= LongEntryThreshold &&
                    signal.Close >= (priorHigh - buffer);
                breakoutShort =
                    decision.BiasSign < 0 &&
                    decision.TriggerScore <= ShortEntryThreshold &&
                    signal.Close <= (priorLow + buffer);
            }

            if (decision.Playbook == PlaybookType.TrendContinuation)
            {
                longSetup = decision.BiasSign > 0 && hasDirectionalEnergy && (crossLong || breakoutLong);
                shortSetup = decision.BiasSign < 0 && hasDirectionalEnergy && (crossShort || breakoutShort);
                if (longSetup || shortSetup)
                    archetype = (crossLong || crossShort) ? "trend_continuation_cross" : "trend_breakout_follow";
                else
                    archetype = "trend_continuation_wait";
                return;
            }

            if (decision.Playbook == PlaybookType.Pullback)
            {
                longSetup =
                    decision.BiasSign > 0 &&
                    hasDirectionalEnergy &&
                    decision.ContextScore >= Math.Max(0.20, LongEntryThreshold * 0.55) &&
                    decision.TriggerScore > 0 &&
                    decision.PrevTriggerScore <= 0;
                shortSetup =
                    decision.BiasSign < 0 &&
                    hasDirectionalEnergy &&
                    decision.ContextScore <= -Math.Max(0.20, Math.Abs(ShortEntryThreshold) * 0.55) &&
                    decision.TriggerScore < 0 &&
                    decision.PrevTriggerScore >= 0;
                archetype = (longSetup || shortSetup) ? "pullback_resume" : "pullback_wait";
                return;
            }

            if (decision.Playbook == PlaybookType.ReversalWatch)
            {
                bool longPivotStructure = TryDetectReversalStructure(signal, true);
                bool shortPivotStructure = TryDetectReversalStructure(signal, false);

                bool longPivotFlip =
                    decision.ContextSign < 0 &&
                    hasDirectionalEnergy &&
                    decision.TriggerSign > 0 &&
                    decision.Divergence >= ReversalDivergenceThreshold &&
                    decision.TriggerScore >= (LongEntryThreshold * 0.80) &&
                    decision.PrevTriggerScore < (LongEntryThreshold * 0.80);
                bool shortPivotFlip =
                    decision.ContextSign > 0 &&
                    hasDirectionalEnergy &&
                    decision.TriggerSign < 0 &&
                    decision.Divergence >= ReversalDivergenceThreshold &&
                    decision.TriggerScore <= (ShortEntryThreshold * 0.80) &&
                    decision.PrevTriggerScore > (ShortEntryThreshold * 0.80);

                longSetup = longPivotStructure || longPivotFlip;
                shortSetup = shortPivotStructure || shortPivotFlip;

                if (longPivotStructure || shortPivotStructure)
                    archetype = "reversal_structure_g";
                else if (longPivotFlip || shortPivotFlip)
                    archetype = "reversal_pivot_flip";
                else
                    archetype = "reversal_wait";
                return;
            }

            if (EnableFallbackThresholdEntry)
            {
                longSetup = decision.BiasSign >= 0 && hasDirectionalEnergy && (crossLong || breakoutLong);
                shortSetup = decision.BiasSign <= 0 && hasDirectionalEnergy && (crossShort || breakoutShort);
                archetype = (longSetup || shortSetup) ? "fallback_threshold" : "fallback_wait";
            }
        }

        private string BuildEntryBlockReason(string side, PlaybookDecision decision)
        {
            if (!decision.SessionPass)
                return "blocked_session";
            if (!decision.DailyPass)
                return "blocked_daily_guard";
            if (!decision.MaxEntriesPass)
                return "blocked_max_entries";
            if (!decision.CooldownPass)
                return "blocked_cooldown";
            if (!decision.NeutralPass)
                return "blocked_neutral_zone";
            if (!decision.AdxPass)
                return "blocked_adx";
            if (!decision.ChoppinessPass)
                return "blocked_choppiness";
            if (!decision.PlaybookActionable && !EnableFallbackThresholdEntry)
                return "blocked_playbook_actionability";

            bool isLong = string.Equals(side, "long", StringComparison.OrdinalIgnoreCase);
            if (isLong)
            {
                if (!decision.SlopeLongPass)
                    return "blocked_slope_long";
                if (!decision.BaseLongPass)
                    return "blocked_base_long";
                if (!decision.OrderFlowLongPass)
                    return "blocked_orderflow_long";
            }
            else
            {
                if (!decision.SlopeShortPass)
                    return "blocked_slope_short";
                if (!decision.BaseShortPass)
                    return "blocked_base_short";
                if (!decision.OrderFlowShortPass)
                    return "blocked_orderflow_short";
            }

            return "blocked_other";
        }

        private void MarkEntry(DateTime barTime)
        {
            _lastEntryBar = CurrentBar;
            if (!_dailyStateInitialized || _currentTradeDate != barTime.Date)
                RollDailyState(barTime, ResolveCumProfitSnapshot());
            _entriesToday++;
        }

        private void RollDailyState(DateTime barTime, double cumProfit)
        {
            DateTime barDate = barTime.Date;
            if (_dailyStateInitialized && barDate == _currentTradeDate)
                return;

            _currentTradeDate = barDate;
            _dailyStartCumProfit = cumProfit;
            _entriesToday = 0;
            _dailyTradingLocked = false;
            _dailyLockReason = string.Empty;
            _dailyStateInitialized = true;
            _dailyFlattenSubmitted = false;
        }

        private bool UpdateDailyLockState(double dailyRealizedPnl)
        {
            if (!UseDailyPnlGuard)
            {
                _dailyTradingLocked = false;
                _dailyLockReason = string.Empty;
                return false;
            }

            if (_dailyTradingLocked)
                return false;

            double lossLimit = Math.Abs(DailyLossLimitCurrency);
            if (lossLimit > 0 && dailyRealizedPnl <= -lossLimit)
            {
                _dailyTradingLocked = true;
                _dailyLockReason = "daily_loss_limit_hit";
                return true;
            }

            double profitLimit = Math.Abs(DailyProfitTargetCurrency);
            if (profitLimit > 0 && dailyRealizedPnl >= profitLimit)
            {
                _dailyTradingLocked = true;
                _dailyLockReason = "daily_profit_target_hit";
                return true;
            }

            return false;
        }

        private bool IsInsideSessionWindow(DateTime barTime)
        {
            int start = NormalizeTimeToken(SessionStartTime);
            int end = NormalizeTimeToken(SessionEndTime);
            if (start == end)
                return true;

            int token = (barTime.Hour * 10000) + (barTime.Minute * 100) + barTime.Second;
            if (start < end)
                return token >= start && token <= end;

            return token >= start || token <= end;
        }

        private static int NormalizeTimeToken(int raw)
        {
            if (raw < 0)
                return 0;
            if (raw > 235959)
                return 235959;
            return raw;
        }

        private double ResolveCumProfitSnapshot()
        {
            double current = GetRealizedPnlSafe();
            if (double.IsNaN(current) || double.IsInfinity(current))
                return _lastKnownCumProfit;

            _lastKnownCumProfit = current;
            return current;
        }

        private void ConfigureRiskForEntry(string entrySignalName, double atr)
        {
            double distanceForStop = Math.Max(_tickSize, atr * StopAtrMultiplier);
            double distanceForTarget = Math.Max(_tickSize, atr * TargetAtrMultiplier);

            int stopTicks = ToTicks(distanceForStop);
            int targetTicks = ToTicks(distanceForTarget);

            if (UseStopLoss)
                SetStopLoss(entrySignalName, CalculationMode.Ticks, stopTicks, false);
            if (UseProfitTarget)
                SetProfitTarget(entrySignalName, CalculationMode.Ticks, targetTicks);
        }

        private int ToTicks(double priceDistance)
        {
            double tick = Math.Max(_tickSize, TickSize > 0 ? TickSize : _tickSize);
            int ticks = (int)Math.Round(priceDistance / tick, MidpointRounding.AwayFromZero);
            return Math.Max(1, ticks);
        }

        private void SubmitPlaybookEntry(bool isLong, SignalSnapshot signal, string archetype)
        {
            _lastArchetype = string.IsNullOrWhiteSpace(archetype) ? "unknown" : archetype;
            ResolveEntrySignalNames(isLong, _lastArchetype, out string coreSignalName, out string runnerSignalName);
            int coreQty = Math.Max(1, CoreQuantity);
            int runnerQty = EnableTwoLegPosition ? Math.Max(0, RunnerQuantity) : 0;
            double atr = signal.Atr > 0 ? signal.Atr : Math.Max(_tickSize, TickSize);
            int stopTicks = ToTicks(Math.Max(_tickSize, atr * StopAtrMultiplier));
            int coreTargetTicks = ToTicks(Math.Max(_tickSize, atr * CoreTargetAtrMultiplier));
            int runnerTargetTicks = ToTicks(Math.Max(_tickSize, atr * RunnerTargetAtrMultiplier));
            int fallbackTargetTicks = ToTicks(Math.Max(_tickSize, atr * TargetAtrMultiplier));

            if (isLong)
            {
                _activeLongCoreSignalName = coreSignalName;
                _activeLongRunnerSignalName = runnerSignalName;
                if (UseStopLoss)
                {
                    SetStopLoss(coreSignalName, CalculationMode.Ticks, stopTicks, false);
                    if (runnerQty > 0)
                        SetStopLoss(runnerSignalName, CalculationMode.Ticks, stopTicks, false);
                }

                if (UseProfitTarget)
                {
                    SetProfitTarget(coreSignalName, CalculationMode.Ticks, coreTargetTicks);
                    if (runnerQty > 0)
                        SetProfitTarget(runnerSignalName, CalculationMode.Ticks, runnerTargetTicks);
                }
                else if (runnerQty == 0)
                {
                    SetProfitTarget(coreSignalName, CalculationMode.Ticks, fallbackTargetTicks);
                }

                EnterLong(coreQty, coreSignalName);
                if (runnerQty > 0)
                    EnterLong(runnerQty, runnerSignalName);

                InitializeRunnerTrail(MarketPosition.Long, atr, stopTicks, coreTargetTicks);
                return;
            }

            _activeShortCoreSignalName = coreSignalName;
            _activeShortRunnerSignalName = runnerSignalName;
            if (UseStopLoss)
            {
                SetStopLoss(coreSignalName, CalculationMode.Ticks, stopTicks, false);
                if (runnerQty > 0)
                    SetStopLoss(runnerSignalName, CalculationMode.Ticks, stopTicks, false);
            }

            if (UseProfitTarget)
            {
                SetProfitTarget(coreSignalName, CalculationMode.Ticks, coreTargetTicks);
                if (runnerQty > 0)
                    SetProfitTarget(runnerSignalName, CalculationMode.Ticks, runnerTargetTicks);
            }
            else if (runnerQty == 0)
            {
                SetProfitTarget(coreSignalName, CalculationMode.Ticks, fallbackTargetTicks);
            }

            EnterShort(coreQty, coreSignalName);
            if (runnerQty > 0)
                EnterShort(runnerQty, runnerSignalName);

            InitializeRunnerTrail(MarketPosition.Short, atr, stopTicks, coreTargetTicks);
        }

        private void ResolveEntrySignalNames(bool isLong, string archetype, out string coreSignalName, out string runnerSignalName)
        {
            string suffix = ResolveArchetypeCode(archetype);
            string side = isLong ? "L" : "S";
            coreSignalName = string.Format(CultureInfo.InvariantCulture, "{0}-Core-{1}", side, suffix);
            runnerSignalName = string.Format(CultureInfo.InvariantCulture, "{0}-Runner-{1}", side, suffix);
        }

        private static string ResolveArchetypeCode(string archetype)
        {
            string key = string.IsNullOrWhiteSpace(archetype)
                ? string.Empty
                : archetype.Trim().ToLowerInvariant();

            if (key.Contains("trend_breakout"))
                return "TB";
            if (key.Contains("trend_continuation"))
                return "TC";
            if (key.Contains("pullback"))
                return "PB";
            if (key.Contains("reversal"))
                return "RV";
            if (key.Contains("fallback"))
                return "FB";
            if (key.Contains("signal_flip"))
                return "SF";
            return "NA";
        }

        private void InitializeRunnerTrail(MarketPosition direction, double atr, int stopTicks, int coreTargetTicks)
        {
            if (!EnableRunnerAtrTrail || !EnableTwoLegPosition || RunnerQuantity <= 0)
            {
                ResetRunnerTrailState();
                return;
            }

            double entryPrice = Close != null && CurrentBar >= 0 ? Close[0] : 0;
            if (entryPrice <= 0)
            {
                ResetRunnerTrailState();
                return;
            }

            _trailInitialized = true;
            _trailDirection = direction;
            _trailEntryPrice = entryPrice;
            _trailExtremePrice = entryPrice;
            _trailTp1Touched = !DelayRunnerTrailUntilTp1;
            double trailDistance = Math.Max(ToTicks(Math.Max(_tickSize, atr * RunnerTrailAtrMultiplier)), MinRunnerTrailTicks) * _tickSize;
            _trailActiveStopPrice = direction == MarketPosition.Long
                ? entryPrice - (Math.Max(1, stopTicks) * _tickSize)
                : entryPrice + (Math.Max(1, stopTicks) * _tickSize);

            _trailEntryPrice = entryPrice;
            // store core target in points via synthetic field to reuse in activation check
            _trailTp1TargetPrice = direction == MarketPosition.Long
                ? entryPrice + (coreTargetTicks * _tickSize)
                : entryPrice - (coreTargetTicks * _tickSize);
            _trailDistancePoints = Math.Max(_tickSize, trailDistance);
        }

        private void UpdateRunnerTrail(MarketPosition direction, SignalSnapshot signal)
        {
            if (!EnableRunnerAtrTrail || !EnableTwoLegPosition || RunnerQuantity <= 0)
                return;
            if (!_trailInitialized || _trailDirection != direction)
                return;

            double atr = signal.Atr > 0 ? signal.Atr : Math.Max(_tickSize, TickSize);
            int trailTicks = Math.Max(MinRunnerTrailTicks, ToTicks(Math.Max(_tickSize, atr * RunnerTrailAtrMultiplier)));
            _trailDistancePoints = Math.Max(_tickSize, trailTicks * _tickSize);

            if (direction == MarketPosition.Long)
            {
                _trailExtremePrice = Math.Max(_trailExtremePrice, High[0]);
                if (DelayRunnerTrailUntilTp1 && !_trailTp1Touched && High[0] >= _trailTp1TargetPrice)
                    _trailTp1Touched = true;
                if (!_trailTp1Touched)
                    return;

                double candidate = Instrument.MasterInstrument.RoundToTickSize(_trailExtremePrice - _trailDistancePoints);
                if (candidate > (_trailActiveStopPrice + (_tickSize * 0.25)))
                {
                    _trailActiveStopPrice = candidate;
                    SetStopLoss(_activeLongRunnerSignalName, CalculationMode.Price, _trailActiveStopPrice, false);
                }
                return;
            }

            _trailExtremePrice = Math.Min(_trailExtremePrice, Low[0]);
            if (DelayRunnerTrailUntilTp1 && !_trailTp1Touched && Low[0] <= _trailTp1TargetPrice)
                _trailTp1Touched = true;
            if (!_trailTp1Touched)
                return;

            double shortCandidate = Instrument.MasterInstrument.RoundToTickSize(_trailExtremePrice + _trailDistancePoints);
            if (shortCandidate < (_trailActiveStopPrice - (_tickSize * 0.25)))
            {
                _trailActiveStopPrice = shortCandidate;
                SetStopLoss(_activeShortRunnerSignalName, CalculationMode.Price, _trailActiveStopPrice, false);
            }
        }

        private void ResetRunnerTrailState()
        {
            _trailInitialized = false;
            _trailDirection = MarketPosition.Flat;
            _trailEntryPrice = 0;
            _trailExtremePrice = 0;
            _trailActiveStopPrice = 0;
            _trailTp1Touched = false;
            _trailDistancePoints = 0;
            _trailTp1TargetPrice = 0;
            _activeLongCoreSignalName = LongCoreSignalName;
            _activeLongRunnerSignalName = LongRunnerSignalName;
            _activeShortCoreSignalName = ShortCoreSignalName;
            _activeShortRunnerSignalName = ShortRunnerSignalName;
        }

        private bool IsChoppyRegime(int bip)
        {
            if (!EnableChoppinessFilter)
                return false;
            if (bip < 0 || _atrByBip == null || bip >= _atrByBip.Length || _atrByBip[bip] == null)
                return false;

            double chop = ComputeChoppinessIndex(bip, ChoppinessPeriod);
            if (double.IsNaN(chop) || double.IsInfinity(chop))
                return false;
            return chop > ChoppinessThreshold;
        }

        private double ComputeChoppinessIndex(int bip, int period)
        {
            if (period < 2)
                return double.NaN;
            if (CurrentBars == null || bip < 0 || bip >= CurrentBars.Length || CurrentBars[bip] < period)
                return double.NaN;

            double sumTrueRange = 0;
            double maxHigh = double.MinValue;
            double minLow = double.MaxValue;
            for (int i = 0; i < period; i++)
            {
                double h = Highs[bip][i];
                double l = Lows[bip][i];
                double prevClose = Closes[bip][Math.Min(i + 1, CurrentBars[bip])];
                double tr1 = h - l;
                double tr2 = Math.Abs(h - prevClose);
                double tr3 = Math.Abs(l - prevClose);
                double trueRange = Math.Max(tr1, Math.Max(tr2, tr3));
                if (double.IsNaN(trueRange) || double.IsInfinity(trueRange) || trueRange < 0)
                    return double.NaN;
                sumTrueRange += trueRange;

                if (h > maxHigh)
                    maxHigh = h;
                if (l < minLow)
                    minLow = l;
            }

            double range = maxHigh - minLow;
            if (range <= 1e-8 || sumTrueRange <= 1e-8)
                return double.NaN;

            double ratio = sumTrueRange / range;
            if (ratio <= 0)
                return double.NaN;

            double denom = Math.Log10(period);
            if (Math.Abs(denom) <= 1e-8)
                return double.NaN;

            double chop = 100.0 * Math.Log10(ratio) / denom;
            return Math.Max(0.0, Math.Min(100.0, chop));
        }

        private bool TryGetBreakoutLevels(int bip, int lookback, out double priorHigh, out double priorLow)
        {
            priorHigh = double.NaN;
            priorLow = double.NaN;
            if (lookback < 2)
                return false;
            if (CurrentBars == null || bip < 0 || bip >= CurrentBars.Length)
                return false;
            if (CurrentBars[bip] < lookback + 1)
                return false;

            double highest = double.MinValue;
            double lowest = double.MaxValue;
            for (int i = 1; i <= lookback; i++)
            {
                double h = Highs[bip][i];
                double l = Lows[bip][i];
                if (double.IsNaN(h) || double.IsInfinity(h) || double.IsNaN(l) || double.IsInfinity(l))
                    return false;

                if (h > highest)
                    highest = h;
                if (l < lowest)
                    lowest = l;
            }

            if (highest <= double.MinValue || lowest >= double.MaxValue)
                return false;

            priorHigh = highest;
            priorLow = lowest;
            return true;
        }

        private bool TryDetectReversalStructure(SignalSnapshot signal, bool bullish)
        {
            if (signal.Bips < 0)
                return false;

            int bip = signal.Bips;
            if (CurrentBars == null || bip >= CurrentBars.Length || CurrentBars[bip] < (ReversalPivotLookbackBars / 2))
                return false;

            double atr = Math.Max(_tickSize, signal.Atr > 0 ? signal.Atr : _tickSize);
            List<PivotPoint> pivots = CollectPivotPoints(bip, ReversalPivotStrength, ReversalPivotLookbackBars);
            if (pivots.Count < 5)
                return false;

            // Evaluate newest alternating 5-point structures.
            for (int i = pivots.Count - 1; i >= 4; i--)
            {
                PivotPoint p0 = pivots[i - 4];
                PivotPoint p1 = pivots[i - 3];
                PivotPoint p2 = pivots[i - 2];
                PivotPoint p3 = pivots[i - 1];
                PivotPoint p4 = pivots[i];

                if (bullish)
                {
                    // low-high-low-high-low   with: E < C, F > D, G > E
                    if (p0.IsHigh || !p1.IsHigh || p2.IsHigh || !p3.IsHigh || p4.IsHigh)
                        continue;
                    if (p2.Price > (p0.Price - (atr * ReversalStructureImpulseAtr)))
                        continue;
                    if (p3.Price < (p1.Price + (atr * ReversalStructureImpulseAtr)))
                        continue;
                    if (p4.Price < (p2.Price + (atr * ReversalStructureImpulseAtr)))
                        continue;
                    if (Close[0] < (p4.Price + (atr * 0.05)))
                        continue;

                    return true;
                }

                // bearish mirror: high-low-high-low-high with: E > C, F < D, G < E
                if (!p0.IsHigh || p1.IsHigh || !p2.IsHigh || p3.IsHigh || !p4.IsHigh)
                    continue;
                if (p2.Price < (p0.Price + (atr * ReversalStructureImpulseAtr)))
                    continue;
                if (p3.Price > (p1.Price - (atr * ReversalStructureImpulseAtr)))
                    continue;
                if (p4.Price > (p2.Price - (atr * ReversalStructureImpulseAtr)))
                    continue;
                if (Close[0] > (p4.Price - (atr * 0.05)))
                    continue;

                return true;
            }

            return false;
        }

        private List<PivotPoint> CollectPivotPoints(int bip, int strength, int lookback)
        {
            var pivots = new List<PivotPoint>(64);
            if (strength < 1)
                return pivots;
            if (CurrentBars == null || bip < 0 || bip >= CurrentBars.Length)
                return pivots;

            int maxBarsAgo = Math.Min(lookback, Math.Max(0, CurrentBars[bip] - strength - 1));
            for (int barsAgo = maxBarsAgo; barsAgo >= strength; barsAgo--)
            {
                bool swingHigh = IsSwingHigh(bip, barsAgo, strength);
                bool swingLow = IsSwingLow(bip, barsAgo, strength);
                if (!swingHigh && !swingLow)
                    continue;

                if (swingHigh)
                {
                    pivots.Add(new PivotPoint
                    {
                        IsHigh = true,
                        BarsAgo = barsAgo,
                        Price = Highs[bip][barsAgo]
                    });
                }

                if (swingLow)
                {
                    pivots.Add(new PivotPoint
                    {
                        IsHigh = false,
                        BarsAgo = barsAgo,
                        Price = Lows[bip][barsAgo]
                    });
                }
            }

            return pivots;
        }

        private bool IsSwingHigh(int bip, int barsAgo, int strength)
        {
            if (barsAgo - strength < 0)
                return false;
            if (barsAgo + strength > CurrentBars[bip])
                return false;

            double value = Highs[bip][barsAgo];
            for (int i = 1; i <= strength; i++)
            {
                if (Highs[bip][barsAgo - i] >= value)
                    return false;
                if (Highs[bip][barsAgo + i] > value)
                    return false;
            }

            return true;
        }

        private bool IsSwingLow(int bip, int barsAgo, int strength)
        {
            if (barsAgo - strength < 0)
                return false;
            if (barsAgo + strength > CurrentBars[bip])
                return false;

            double value = Lows[bip][barsAgo];
            for (int i = 1; i <= strength; i++)
            {
                if (Lows[bip][barsAgo - i] <= value)
                    return false;
                if (Lows[bip][barsAgo + i] < value)
                    return false;
            }

            return true;
        }

        private void AddMissingTimeframeSeries()
        {
            var existingMinutes = new HashSet<int>();
            if (BarsPeriod != null && BarsPeriod.BarsPeriodType == BarsPeriodType.Minute)
                existingMinutes.Add(BarsPeriod.Value);

            foreach (int minute in TargetMinutes)
            {
                if (existingMinutes.Contains(minute))
                    continue;

                AddDataSeries(BarsPeriodType.Minute, minute);
                existingMinutes.Add(minute);
            }
        }

        private void AddOrderFlowTickSeries()
        {
            bool isPrimaryOneTick =
                BarsPeriod != null &&
                BarsPeriod.BarsPeriodType == BarsPeriodType.Tick &&
                BarsPeriod.Value == 1;
            if (!isPrimaryOneTick)
                AddDataSeries(BarsPeriodType.Tick, 1);
        }

        private void InitializeSeriesMetadata()
        {
            _bipByMinutes.Clear();
            int seriesCount = BarsArray == null ? 0 : BarsArray.Length;
            _minutesByBip = new int[seriesCount];
            _isTrackedByBip = new bool[seriesCount];

            for (int bip = 0; bip < seriesCount; bip++)
            {
                Bars bars = BarsArray[bip];
                if (bars == null || bars.BarsPeriod == null)
                    continue;

                if (bars.BarsPeriod.BarsPeriodType != BarsPeriodType.Minute)
                    continue;

                int minutes = bars.BarsPeriod.Value;
                _minutesByBip[bip] = minutes;

                bool tracked = false;
                for (int i = 0; i < TargetMinutes.Length; i++)
                {
                    if (TargetMinutes[i] == minutes)
                    {
                        tracked = true;
                        break;
                    }
                }
                if (!tracked)
                    continue;

                _isTrackedByBip[bip] = true;
                if (!_bipByMinutes.ContainsKey(minutes))
                    _bipByMinutes.Add(minutes, bip);
            }
        }

        private void InitializeIndicators()
        {
            int seriesCount = BarsArray == null ? 0 : BarsArray.Length;
            _emaFastByBip = new EMA[seriesCount];
            _emaMedByBip = new EMA[seriesCount];
            _emaSlowByBip = new EMA[seriesCount];
            _atrByBip = new ATR[seriesCount];
            _adxByBip = new ADX[seriesCount];
            _rsiByBip = new RSI[seriesCount];
            _stochByBip = new Stochastics[seriesCount];
            _smaByBip = new SMA[seriesCount];

            for (int bip = 0; bip < seriesCount; bip++)
            {
                if (_isTrackedByBip == null || bip >= _isTrackedByBip.Length || !_isTrackedByBip[bip])
                    continue;

                _emaFastByBip[bip] = EMA(BarsArray[bip], 12);
                _emaMedByBip[bip] = EMA(BarsArray[bip], 26);
                _emaSlowByBip[bip] = EMA(BarsArray[bip], 55);
                _atrByBip[bip] = ATR(BarsArray[bip], 14);
                _adxByBip[bip] = ADX(BarsArray[bip], 14);
                _rsiByBip[bip] = RSI(BarsArray[bip], 14, 3);
                _stochByBip[bip] = Stochastics(BarsArray[bip], 14, 3, 3);
                _smaByBip[bip] = SMA(BarsArray[bip], 20);
            }
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

        private void InitializeOrderFlowLayer()
        {
            _orderFlowDeltaByBip = null;
            _orderFlowVwapByBip = null;
            _isOrderFlowRuntimeAvailable = false;
            _hasLoggedOrderFlowUnavailable = false;
            _orderFlowTape.Clear();
            _orderFlowTapeBuyVolume = 0;
            _orderFlowTapeSellVolume = 0;
            _lastOrderFlowTapeUtc = DateTime.MinValue;
            _lastDepthUpdateUtc = DateTime.MinValue;
            _lastBidPrice = 0;
            _lastAskPrice = 0;
            _lastTradePrice = 0;
            for (int i = 0; i < OrderFlowDepthLevels; i++)
            {
                _depthBidByLevel[i] = 0;
                _depthAskByLevel[i] = 0;
            }

            if (!EnableOrderFlowLayer || BarsArray == null || BarsArray.Length == 0)
                return;

            if (_orderFlowTickBip < 0)
            {
                LogOrderFlowUnavailableOnce("GlitchPlaybook order flow unavailable: missing 1-tick data series.");
                return;
            }

            try
            {
                int seriesCount = BarsArray.Length;
                var deltaByBip = new OrderFlowCumulativeDelta[seriesCount];
                var vwapByBip = new OrderFlowVWAP[seriesCount];
                int initialized = 0;

                for (int bip = 0; bip < seriesCount; bip++)
                {
                    if (_isTrackedByBip == null || bip >= _isTrackedByBip.Length || !_isTrackedByBip[bip])
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

                    if (deltaByBip[bip] != null)
                        initialized++;
                }

                _orderFlowDeltaByBip = deltaByBip;
                _orderFlowVwapByBip = vwapByBip;
                _isOrderFlowRuntimeAvailable = initialized > 0;
                if (!_isOrderFlowRuntimeAvailable)
                    LogOrderFlowUnavailableOnce("GlitchPlaybook order flow unavailable: no tracked timeframe instances initialized.");
            }
            catch (Exception ex)
            {
                _orderFlowDeltaByBip = null;
                _orderFlowVwapByBip = null;
                _isOrderFlowRuntimeAvailable = false;
                LogOrderFlowUnavailableOnce("GlitchPlaybook order flow unavailable: " + ex.Message);
            }
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
                    LogOrderFlowUnavailableOnce("GlitchPlaybook order flow tick refresh disabled: " + ex.Message);
                    return;
                }
            }
        }

        private void LogOrderFlowUnavailableOnce(string message)
        {
            if (_hasLoggedOrderFlowUnavailable)
                return;

            _hasLoggedOrderFlowUnavailable = true;
            Print(string.IsNullOrWhiteSpace(message)
                ? "GlitchPlaybook order flow unavailable."
                : message);
        }

        private Dictionary<int, SignalSnapshot> BuildSignalSet()
        {
            var signals = new Dictionary<int, SignalSnapshot>(TargetMinutes.Length);
            DateTime nowUtc = (_isOrderFlowRuntimeAvailable && EnableOrderFlowLayer) ? DateTime.UtcNow : DateTime.MinValue;
            for (int i = 0; i < TargetMinutes.Length; i++)
            {
                int minutes = TargetMinutes[i];
                SignalSnapshot signal;
                if (_bipByMinutes.TryGetValue(minutes, out int bip) && TryBuildSignal(bip, minutes, nowUtc, out signal))
                {
                    signals[minutes] = signal;
                }
                else
                {
                    signals[minutes] = BuildWarmupSignal(minutes);
                }
            }

            UpdateLastScores(signals);
            return signals;
        }

        private bool TryBuildSignal(int bip, int minutes, DateTime nowUtc, out SignalSnapshot signal)
        {
            signal = default(SignalSnapshot);
            if (bip < 0 || BarsArray == null || bip >= BarsArray.Length)
                return false;
            if (CurrentBars == null || bip >= CurrentBars.Length || CurrentBars[bip] < MinBarsForSignal)
                return false;
            if (_emaFastByBip == null || bip >= _emaFastByBip.Length || _emaFastByBip[bip] == null)
                return false;
            if (_emaMedByBip == null || bip >= _emaMedByBip.Length || _emaMedByBip[bip] == null)
                return false;
            if (_emaSlowByBip == null || bip >= _emaSlowByBip.Length || _emaSlowByBip[bip] == null)
                return false;
            if (_atrByBip == null || bip >= _atrByBip.Length || _atrByBip[bip] == null)
                return false;
            if (_adxByBip == null || bip >= _adxByBip.Length || _adxByBip[bip] == null)
                return false;
            if (_rsiByBip == null || bip >= _rsiByBip.Length || _rsiByBip[bip] == null)
                return false;
            if (_stochByBip == null || bip >= _stochByBip.Length || _stochByBip[bip] == null)
                return false;
            if (_smaByBip == null || bip >= _smaByBip.Length || _smaByBip[bip] == null)
                return false;

            double close = Closes[bip][0];
            if (close <= 0 || double.IsNaN(close) || double.IsInfinity(close))
                return false;

            double open = Opens[bip][0];
            double high = Highs[bip][0];
            double low = Lows[bip][0];
            double volume = Math.Max(0, Volumes[bip][0]);
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
            double regimeWeight = BuildRegimeWeight(adx);

            double directionalCore =
                (emaSlopeSignal * 0.42) +
                (emaAlignment * 0.23) +
                (rsiSignal * 0.14) +
                (stochSignal * 0.11) +
                (zSignal * 0.10);

            double baseScore = Clamp(directionalCore * regimeWeight, -1, 1);
            double? orderFlowScore;
            double? orderFlowConfidence;
            double? orderFlowDelta;
            double? orderFlowDeltaChange;
            double? orderFlowVwap;
            double? orderFlowVwapDeviation;
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
                finalScore = Clamp((baseScore * (1.0 - blend)) + (orderFlowScore.Value * blend), -1, 1);
            }

            signal = new SignalSnapshot
            {
                Minutes = minutes,
                Bips = bip,
                IsLive = true,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume,
                AveragePrice = averagePrice,
                Atr = atr,
                Adx = adx,
                Rsi = rsi,
                StochK = stochK,
                ZScore = zScore,
                EmaSlopeSignal = emaSlopeSignal,
                EmaAlignment = emaAlignment,
                RsiSignal = rsiSignal,
                StochSignal = stochSignal,
                ZSignal = zSignal,
                RegimeWeight = regimeWeight,
                BaseScore = baseScore,
                Score = finalScore,
                SignalLabel = ToSignalLabel(finalScore),
                OrderFlowScore = orderFlowScore,
                OrderFlowConfidence = orderFlowConfidence,
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

        private SignalSnapshot BuildWarmupSignal(int minutes)
        {
            double score = 0;
            if (minutes == 1)
                score = _lastScore1;
            else if (minutes == 5)
                score = _lastScore5;
            else if (minutes == 15)
                score = _lastScore15;
            else if (minutes == 60)
                score = _lastScore60;

            double close = Close != null && CurrentBar >= 0 ? Close[0] : 0;
            return new SignalSnapshot
            {
                Minutes = minutes,
                Bips = _bipByMinutes.TryGetValue(minutes, out int bip) ? bip : -1,
                IsLive = false,
                Open = close,
                High = close,
                Low = close,
                Close = close,
                Volume = 0,
                AveragePrice = close,
                Atr = 0,
                Adx = 0,
                Rsi = 50,
                StochK = 50,
                ZScore = 0,
                EmaSlopeSignal = 0,
                EmaAlignment = 0,
                RsiSignal = 0,
                StochSignal = 0,
                ZSignal = 0,
                RegimeWeight = 1,
                BaseScore = score,
                Score = score,
                SignalLabel = "Neutral"
            };
        }

        private void UpdateLastScores(IDictionary<int, SignalSnapshot> signals)
        {
            if (signals == null)
                return;

            if (signals.TryGetValue(1, out SignalSnapshot s1))
                _lastScore1 = s1.Score;
            if (signals.TryGetValue(5, out SignalSnapshot s5))
                _lastScore5 = s5.Score;
            if (signals.TryGetValue(15, out SignalSnapshot s15))
                _lastScore15 = s15.Score;
            if (signals.TryGetValue(60, out SignalSnapshot s60))
                _lastScore60 = s60.Score;
        }

        private PlaybookDecision BuildPlaybookDecision(IDictionary<int, SignalSnapshot> signals, int executionMinutes)
        {
            var decision = PlaybookDecision.CreateDefault();
            decision.ExecutionMinutes = executionMinutes;

            SignalSnapshot oneMinute = signals != null && signals.TryGetValue(1, out SignalSnapshot s1) ? s1 : BuildWarmupSignal(1);
            SignalSnapshot fiveMinute = signals != null && signals.TryGetValue(5, out SignalSnapshot s5) ? s5 : BuildWarmupSignal(5);
            SignalSnapshot fifteenMinute = signals != null && signals.TryGetValue(15, out SignalSnapshot s15) ? s15 : BuildWarmupSignal(15);
            SignalSnapshot sixtyMinute = signals != null && signals.TryGetValue(60, out SignalSnapshot s60) ? s60 : BuildWarmupSignal(60);
            bool has1 = oneMinute.IsLive;
            bool has5 = fiveMinute.IsLive;
            bool has15 = fifteenMinute.IsLive;
            bool has60 = sixtyMinute.IsLive;
            bool hasLive = has1 || has5 || has15 || has60;

            SignalSnapshot executionSignal = GetExecutionSignal(signals, executionMinutes);
            decision.ExecutionBaseScore = executionSignal.BaseScore;
            decision.ExecutionScore = executionSignal.Score;

            if (!hasLive)
            {
                decision.Reason = "awaiting_feed";
                return decision;
            }

            double contextScore;
            double triggerScore;
            double microScore;
            double? adxProxy;
            if (executionMinutes <= 1)
            {
                contextScore = ComputeDecisionWeightedScore(
                    has60 ? sixtyMinute.Score : 0, has60 ? 0.50 : 0,
                    has15 ? fifteenMinute.Score : 0, has15 ? 0.30 : 0,
                    has5 ? fiveMinute.Score : 0, has5 ? 0.20 : 0);
                if (!has60 && !has15 && has5)
                    contextScore = fiveMinute.Score;

                triggerScore = ComputeDecisionWeightedScore(
                    has1 ? oneMinute.Score : 0, has1 ? 0.75 : 0,
                    has5 ? fiveMinute.Score : 0, has5 ? 0.25 : 0,
                    0, 0);
                if (!has1 && has5)
                    triggerScore = fiveMinute.Score;

                microScore = has1 ? oneMinute.Score : triggerScore;
                adxProxy = has5
                    ? fiveMinute.Adx
                    : has15
                        ? fifteenMinute.Adx
                        : has60
                            ? sixtyMinute.Adx
                            : (double?)null;
            }
            else if (executionMinutes <= 5)
            {
                contextScore = ComputeDecisionWeightedScore(
                    has60 ? sixtyMinute.Score : 0, has60 ? 0.60 : 0,
                    has15 ? fifteenMinute.Score : 0, has15 ? 0.40 : 0,
                    0, 0);
                if (!has60 && has15)
                    contextScore = fifteenMinute.Score;

                triggerScore = ComputeDecisionWeightedScore(
                    has5 ? fiveMinute.Score : 0, has5 ? 0.75 : 0,
                    has1 ? oneMinute.Score : 0, has1 ? 0.25 : 0,
                    0, 0);
                if (!has5 && has1)
                    triggerScore = oneMinute.Score;

                microScore = has1 ? oneMinute.Score : triggerScore;
                adxProxy = has5
                    ? fiveMinute.Adx
                    : has15
                        ? fifteenMinute.Adx
                        : has60
                            ? sixtyMinute.Adx
                            : (double?)null;
            }
            else if (executionMinutes <= 15)
            {
                contextScore = has60 ? sixtyMinute.Score : (has15 ? fifteenMinute.Score : 0);
                triggerScore = ComputeDecisionWeightedScore(
                    has15 ? fifteenMinute.Score : 0, has15 ? 0.80 : 0,
                    has5 ? fiveMinute.Score : 0, has5 ? 0.20 : 0,
                    0, 0);
                if (!has15 && has5)
                    triggerScore = fiveMinute.Score;

                microScore = has5 ? fiveMinute.Score : triggerScore;
                adxProxy = has15
                    ? fifteenMinute.Adx
                    : has60
                        ? sixtyMinute.Adx
                        : has5
                            ? fiveMinute.Adx
                            : (double?)null;
            }
            else
            {
                contextScore = has60 ? sixtyMinute.Score : 0;
                triggerScore = ComputeDecisionWeightedScore(
                    has60 ? sixtyMinute.Score : 0, has60 ? 0.85 : 0,
                    has15 ? fifteenMinute.Score : 0, has15 ? 0.15 : 0,
                    0, 0);
                if (!has60 && has15)
                    triggerScore = fifteenMinute.Score;

                microScore = has15 ? fifteenMinute.Score : triggerScore;
                adxProxy = has60
                    ? sixtyMinute.Adx
                    : has15
                        ? fifteenMinute.Adx
                        : (double?)null;
            }

            int contextSign = ResolveDecisionScoreSign(contextScore);
            int triggerSign = ResolveDecisionScoreSign(triggerScore);
            int microSign = ResolveDecisionScoreSign(microScore);
            int biasSign = contextSign != 0 ? contextSign : triggerSign;

            int setupKind;
            if (contextSign == 0 && triggerSign == 0)
                setupKind = 0;
            else if (contextSign != 0 && triggerSign == contextSign)
                setupKind = 1;
            else if (contextSign != 0 && triggerSign == 0)
                setupKind = 2;
            else
                setupKind = 3;

            double contextStrength = Math.Min(1.0, Math.Abs(contextScore));
            double triggerStrength = Math.Min(1.0, Math.Abs(triggerScore));
            double microStrength = Math.Min(1.0, Math.Abs(microScore));
            double divergence = Math.Min(1.0, Math.Abs(triggerScore - contextScore));
            double adxNorm = 0;
            if (adxProxy.HasValue && !double.IsNaN(adxProxy.Value) && !double.IsInfinity(adxProxy.Value))
            {
                adxNorm = (adxProxy.Value - 15.0) / 25.0;
                if (adxNorm < 0)
                    adxNorm = 0;
                if (adxNorm > 1)
                    adxNorm = 1;
            }

            double confidenceRaw;
            if (setupKind == 1)
            {
                confidenceRaw = 0.45 + (contextStrength * 0.25) + (triggerStrength * 0.20) + (microStrength * 0.05) + (adxNorm * 0.05);
            }
            else if (setupKind == 2)
            {
                confidenceRaw = 0.32 + (contextStrength * 0.25) + ((1.0 - triggerStrength) * 0.18) + (adxNorm * 0.10);
            }
            else if (setupKind == 3)
            {
                confidenceRaw = 0.30 + (triggerStrength * 0.22) + (divergence * 0.28) + (adxNorm * 0.10);
            }
            else
            {
                confidenceRaw = 0.12 + (adxNorm * 0.10);
            }

            if (confidenceRaw < 0)
                confidenceRaw = 0;
            if (confidenceRaw > 0.99)
                confidenceRaw = 0.99;

            int confidence = (int)Math.Round(confidenceRaw * 100.0, MidpointRounding.AwayFromZero);
            bool triggerAligned = triggerSign != 0 && triggerSign == biasSign;
            bool microAligned = microSign == 0 || microSign == biasSign;
            bool uiActionable = setupKind == 1 &&
                                biasSign != 0 &&
                                triggerAligned &&
                                microAligned &&
                                confidence >= ConfidenceGateThreshold;
            PlaybookType playbook = ResolvePlaybookType(setupKind, confidence, divergence, contextSign, triggerSign, biasSign, triggerAligned, microAligned);
            bool playbookActionable = playbook != PlaybookType.NoTrade && confidence >= ConfidenceGateThreshold;

            decision.ContextScore = contextScore;
            decision.TriggerScore = triggerScore;
            decision.MicroScore = microScore;
            decision.AdxProxy = adxProxy ?? 0;
            decision.ContextSign = contextSign;
            decision.TriggerSign = triggerSign;
            decision.MicroSign = microSign;
            decision.BiasSign = biasSign;
            decision.Divergence = divergence;
            decision.SetupKind = setupKind;
            decision.Confidence = confidence;
            decision.Playbook = playbook;
            decision.Actionable = uiActionable;
            decision.PlaybookActionable = playbookActionable;
            decision.DirectionalScore = biasSign == 0 ? triggerScore : contextScore;
            decision.BiasLabel = ResolveDecisionBiasLabel(biasSign);
            decision.SetupLabel = ResolveDecisionSetupLabel(setupKind);
            decision.ActionabilityLabel = uiActionable ? "Actionable" : "Wait";
            decision.Reason = playbookActionable ? "playbook_actionable" : "playbook_wait";
            return decision;
        }

        private PlaybookType ResolvePlaybookType(
            int setupKind,
            int confidence,
            double divergence,
            int contextSign,
            int triggerSign,
            int biasSign,
            bool triggerAligned,
            bool microAligned)
        {
            if (setupKind == 1 &&
                EnableTrendPlaybook &&
                biasSign != 0 &&
                triggerAligned &&
                microAligned &&
                confidence >= TrendMinConfidence)
            {
                return PlaybookType.TrendContinuation;
            }

            if (setupKind == 2 &&
                EnablePullbackPlaybook &&
                biasSign != 0 &&
                contextSign == biasSign &&
                confidence >= PullbackMinConfidence)
            {
                return PlaybookType.Pullback;
            }

            if (setupKind == 3 &&
                EnableReversalPlaybook &&
                contextSign != 0 &&
                triggerSign != 0 &&
                contextSign != triggerSign &&
                divergence >= ReversalDivergenceThreshold &&
                confidence >= ReversalMinConfidence)
            {
                return PlaybookType.ReversalWatch;
            }

            return PlaybookType.NoTrade;
        }

        private static int ResolveDecisionScoreSign(double score)
        {
            if (score >= AnalyticsUnifiedNeutralThreshold)
                return 1;
            if (score <= -AnalyticsUnifiedNeutralThreshold)
                return -1;
            return 0;
        }

        private static double ComputeDecisionWeightedScore(
            double valueA,
            double weightA,
            double valueB,
            double weightB,
            double valueC,
            double weightC)
        {
            double sumWeights = 0;
            double sum = 0;

            if (weightA > 0)
            {
                sum += valueA * weightA;
                sumWeights += weightA;
            }
            if (weightB > 0)
            {
                sum += valueB * weightB;
                sumWeights += weightB;
            }
            if (weightC > 0)
            {
                sum += valueC * weightC;
                sumWeights += weightC;
            }

            if (sumWeights <= 1e-8)
                return 0;
            return sum / sumWeights;
        }

        private static string ResolveDecisionBiasLabel(int sign)
        {
            if (sign > 0)
                return "Long";
            if (sign < 0)
                return "Short";
            return "Neutral";
        }

        private static string ResolveDecisionSetupLabel(int setupKind)
        {
            if (setupKind == 1)
                return "Trend Continuation";
            if (setupKind == 2)
                return "Pullback";
            if (setupKind == 3)
                return "Reversal Watch";
            return "No-Trade";
        }

        private static int ResolveExecutionTimeframeMinutes(int requestedMinutes)
        {
            if (requestedMinutes <= 1)
                return 1;
            if (requestedMinutes <= 5)
                return 5;
            if (requestedMinutes <= 15)
                return 15;
            return 60;
        }

        private SignalSnapshot GetExecutionSignal(IDictionary<int, SignalSnapshot> signals, int executionMinutes)
        {
            if (signals != null && signals.TryGetValue(executionMinutes, out SignalSnapshot selected))
                return selected;

            int[] fallback = { 5, 15, 1, 60 };
            for (int i = 0; i < fallback.Length; i++)
            {
                if (signals != null && signals.TryGetValue(fallback[i], out SignalSnapshot candidate))
                    return candidate;
            }

            return BuildWarmupSignal(executionMinutes);
        }

        private double ResolveExecutionScore(IDictionary<int, SignalSnapshot> signals, int executionMinutes)
        {
            return GetExecutionSignal(signals, executionMinutes).Score;
        }

        private PlaybookDecision BuildWarmupPlaybookDecision()
        {
            var decision = PlaybookDecision.CreateDefault();
            decision.BiasLabel = "Neutral";
            decision.SetupLabel = "No-Trade";
            decision.ActionabilityLabel = "Wait";
            return decision;
        }

        private void TryBuildOrderFlowSnapshot(
            int bip,
            double close,
            double atr,
            DateTime nowUtc,
            bool includeHint,
            out double? score,
            out double? confidence,
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

            if (nowUtc != DateTime.MinValue)
            {
                if (TryGetOrderFlowTapeBalance(nowUtc, out double tapeBalance))
                    aggressionBalance = tapeBalance;
                if (TryGetDepthImbalance(nowUtc, out double depthValue))
                    depthImbalance = depthValue;
            }

            double ofScore =
                (deltaPressure * 0.55) +
                ((aggressionBalance ?? 0) * 0.20) +
                ((depthImbalance ?? 0) * 0.15) +
                (vwapBias * 0.10);

            if (CurrentBars[bip] > 0)
            {
                double priceVelocity = Clamp(
                    (Closes[bip][0] - Opens[bip][0]) / Math.Max(Math.Max(atr, _tickSize), 1e-8),
                    -1,
                    1);
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

            if (includeHint)
            {
                hint = BuildOrderFlowHint(
                    ofScore,
                    confidence.Value,
                    cumulativeDelta,
                    deltaChange,
                    aggressionBalance,
                    depthImbalance,
                    vwapDeviation);
            }
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

            double bid = 0;
            double ask = 0;
            for (int i = 0; i < OrderFlowDepthLevels; i++)
            {
                bid += Math.Max(0, _depthBidByLevel[i]);
                ask += Math.Max(0, _depthAskByLevel[i]);
            }

            double total = bid + ask;
            if (total <= 1e-8)
                return false;

            imbalance = Clamp((bid - ask) / total, -1, 1);
            return true;
        }

        private static string BuildOrderFlowHint(
            double score,
            double confidence,
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

            var sb = new StringBuilder(160);
            sb.Append(polarity);
            sb.Append(" | Conf ");
            sb.Append((confidence * 100.0).ToString("0", CultureInfo.InvariantCulture));
            sb.Append('%');

            if (deltaChange.HasValue)
            {
                sb.Append(" | dDelta ");
                sb.Append(deltaChange.Value.ToString("+0.##;-0.##;0.00", CultureInfo.InvariantCulture));
            }
            if (aggressionBalance.HasValue)
            {
                sb.Append(" | Tape ");
                sb.Append(aggressionBalance.Value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture));
            }
            if (depthImbalance.HasValue)
            {
                sb.Append(" | Depth ");
                sb.Append(depthImbalance.Value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture));
            }
            if (vwapDeviation.HasValue)
            {
                sb.Append(" | VWAP ");
                sb.Append(vwapDeviation.Value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture));
                sb.Append(" sigma");
            }
            if (cumulativeDelta.HasValue)
            {
                sb.Append(" | Delta ");
                sb.Append(cumulativeDelta.Value.ToString("0.##", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private void InitializeTelemetryWriter()
        {
            if (!EnableTelemetryExport || FastBacktestMode)
                return;

            try
            {
                string baseDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "GlitchTelemetry");
                Directory.CreateDirectory(baseDir);

                string prefix = SanitizeFileToken(string.IsNullOrWhiteSpace(TelemetryFilePrefix) ? "GlitchPlaybook" : TelemetryFilePrefix);
                string instrumentToken = SanitizeFileToken(Instrument == null ? "Unknown" : Instrument.FullName);
                string barsToken = SanitizeFileToken(BarsPeriod.BarsPeriodType + "_" + BarsPeriod.Value);
                string runToken = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                _telemetryFilePath = Path.Combine(baseDir, prefix + "_" + instrumentToken + "_" + barsToken + "_" + runToken + ".csv");
                _telemetryWriter = new StreamWriter(new FileStream(_telemetryFilePath, FileMode.Create, FileAccess.Write, FileShare.Read));
                _telemetryWriter.AutoFlush = TelemetryAutoFlush;
                _telemetryHeaderWritten = false;
                _telemetryRowCount = 0;
                EnsureTelemetryHeader();
                Print("GlitchPlaybook telemetry: " + _telemetryFilePath);
            }
            catch (Exception ex)
            {
                _telemetryWriter = null;
                _telemetryFilePath = null;
                Print("GlitchPlaybook telemetry init failed: " + ex.Message);
            }
        }

        private void CloseTelemetryWriter()
        {
            if (_telemetryWriter == null)
                return;

            try
            {
                _telemetryWriter.Flush();
                _telemetryWriter.Dispose();
            }
            catch
            {
            }

            _telemetryWriter = null;
            if (!string.IsNullOrWhiteSpace(_telemetryFilePath))
            {
                Print(
                    "GlitchPlaybook telemetry summary | bars seen: " +
                    _telemetryBarsSeen.ToString(CultureInfo.InvariantCulture) +
                    " | rows written: " +
                    _telemetryRowCount.ToString(CultureInfo.InvariantCulture) +
                    " | file: " +
                    _telemetryFilePath);
            }
        }

        private void EnsureTelemetryHeader()
        {
            if (_telemetryWriter == null || _telemetryHeaderWritten)
                return;

            var sb = new StringBuilder(2048);
            AppendCsv(sb, "utc_time");
            AppendCsv(sb, "local_time");
            AppendCsv(sb, "instrument");
            AppendCsv(sb, "bar_index");
            AppendCsv(sb, "row_type");
            AppendCsv(sb, "event_name");
            AppendCsv(sb, "event_action");
            AppendCsv(sb, "event_price");
            AppendCsv(sb, "event_quantity");
            AppendCsv(sb, "event_market_position");
            AppendCsv(sb, "event_order_id");
            AppendCsv(sb, "profile_version");
            AppendCsv(sb, "execution_tf_min");
            AppendCsv(sb, "score_1m");
            AppendCsv(sb, "score_5m");
            AppendCsv(sb, "score_15m");
            AppendCsv(sb, "score_60m");
            AppendCsv(sb, "context_score");
            AppendCsv(sb, "trigger_score");
            AppendCsv(sb, "micro_score");
            AppendCsv(sb, "directional_score");
            AppendCsv(sb, "base_score_execution");
            AppendCsv(sb, "final_score_execution");
            AppendCsv(sb, "playbook");
            AppendCsv(sb, "bias");
            AppendCsv(sb, "setup");
            AppendCsv(sb, "confidence");
            AppendCsv(sb, "actionability");
            AppendCsv(sb, "playbook_actionable");
            AppendCsv(sb, "entry_archetype");
            AppendCsv(sb, "close");
            AppendCsv(sb, "atr");
            AppendCsv(sb, "adx");
            AppendCsv(sb, "rsi");
            AppendCsv(sb, "stoch_k");
            AppendCsv(sb, "z_score");
            AppendCsv(sb, "order_flow_score");
            AppendCsv(sb, "order_flow_confidence");
            AppendCsv(sb, "order_flow_delta");
            AppendCsv(sb, "order_flow_delta_change");
            AppendCsv(sb, "order_flow_vwap");
            AppendCsv(sb, "order_flow_vwap_deviation");
            AppendCsv(sb, "order_flow_tape_balance");
            AppendCsv(sb, "order_flow_depth_imbalance");
            AppendCsv(sb, "signal_label");
            AppendCsv(sb, "position");
            AppendCsv(sb, "quantity");
            AppendCsv(sb, "unrealized_pnl");
            AppendCsv(sb, "realized_pnl");
            AppendCsv(sb, "action");
            AppendCsv(sb, "reason");
            AppendCsv(sb, "gate_reason_detail");
            AppendCsv(sb, "score_delta");
            AppendCsv(sb, "entry_setup_long");
            AppendCsv(sb, "entry_setup_short");
            AppendCsv(sb, "gate_entries_allowed");
            AppendCsv(sb, "gate_long_allowed");
            AppendCsv(sb, "gate_short_allowed");
            AppendCsv(sb, "gate_session_pass");
            AppendCsv(sb, "gate_daily_pass");
            AppendCsv(sb, "gate_max_entries_pass");
            AppendCsv(sb, "gate_cooldown_pass");
            AppendCsv(sb, "gate_neutral_pass");
            AppendCsv(sb, "gate_adx_pass");
            AppendCsv(sb, "gate_choppiness_pass");
            AppendCsv(sb, "gate_slope_long_pass");
            AppendCsv(sb, "gate_slope_short_pass");
            AppendCsv(sb, "gate_base_long_pass");
            AppendCsv(sb, "gate_base_short_pass");
            AppendCsv(sb, "gate_of_long_pass");
            AppendCsv(sb, "gate_of_short_pass");
            AppendCsv(sb, "gate_min_hold_pass");
            AppendCsv(sb, "orderflow_available");
            AppendCsv(sb, "daily_realized_pnl");
            AppendCsv(sb, "entries_today");
            AppendCsv(sb, "bars_since_last_entry");
            AppendCsv(sb, "bars_since_position_entry");
            AppendCsv(sb, "daily_lock_active");
            AppendCsv(sb, "daily_lock_reason");

            _telemetryWriter.WriteLine(sb.ToString());
            _telemetryHeaderWritten = true;
        }

        private void WriteTelemetry(
            SignalSnapshot executionSignal,
            PlaybookDecision decision,
            IDictionary<int, SignalSnapshot> signals,
            string rowType,
            string rowAction,
            string rowReason,
            string eventAction,
            double? eventPrice,
            int? eventQuantity,
            MarketPosition? eventMarketPosition,
            string eventOrderId,
            DateTime rowTime)
        {
            if (!_telemetryRuntimeEnabled)
                return;
            if (_telemetryWriter == null)
                return;
            if (State == State.Historical && !ExportHistoricalTelemetry)
                return;

            try
            {
                EnsureTelemetryHeader();

                SignalSnapshot score1 = signals != null && signals.TryGetValue(1, out SignalSnapshot s1) ? s1 : BuildWarmupSignal(1);
                SignalSnapshot score5 = signals != null && signals.TryGetValue(5, out SignalSnapshot s5) ? s5 : BuildWarmupSignal(5);
                SignalSnapshot score15 = signals != null && signals.TryGetValue(15, out SignalSnapshot s15) ? s15 : BuildWarmupSignal(15);
                SignalSnapshot score60 = signals != null && signals.TryGetValue(60, out SignalSnapshot s60) ? s60 : BuildWarmupSignal(60);

                var sb = new StringBuilder(2200);
                DateTime timestamp = rowTime == DateTime.MinValue ? DateTime.UtcNow : rowTime;
                AppendCsv(sb, timestamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                AppendCsv(sb, timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                AppendCsv(sb, Instrument == null ? string.Empty : Instrument.FullName);
                AppendCsv(sb, string.Equals(rowType, "execution", StringComparison.OrdinalIgnoreCase) ? string.Empty : CurrentBar.ToString(CultureInfo.InvariantCulture));
                AppendCsv(sb, rowType ?? string.Empty);
                AppendCsv(sb, string.Equals(rowType, "execution", StringComparison.OrdinalIgnoreCase) ? "execution" : string.Empty);
                AppendCsv(sb, eventAction ?? string.Empty);
                AppendCsv(sb, ToInvariantNullable(eventPrice));
                AppendCsv(sb, eventQuantity.HasValue ? eventQuantity.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
                AppendCsv(sb, eventMarketPosition.HasValue ? eventMarketPosition.Value.ToString() : string.Empty);
                AppendCsv(sb, eventOrderId ?? string.Empty);
                AppendCsv(sb, StrategyProfileVersion);
                AppendCsv(sb, decision.ExecutionMinutes.ToString(CultureInfo.InvariantCulture));
                AppendCsv(sb, ToInvariant(score1.Score));
                AppendCsv(sb, ToInvariant(score5.Score));
                AppendCsv(sb, ToInvariant(score15.Score));
                AppendCsv(sb, ToInvariant(score60.Score));
                AppendCsv(sb, ToInvariant(decision.ContextScore));
                AppendCsv(sb, ToInvariant(decision.TriggerScore));
                AppendCsv(sb, ToInvariant(decision.MicroScore));
                AppendCsv(sb, ToInvariant(decision.DirectionalScore));
                AppendCsv(sb, ToInvariant(decision.ExecutionBaseScore));
                AppendCsv(sb, ToInvariant(decision.ExecutionScore));
                AppendCsv(sb, decision.Playbook.ToString());
                AppendCsv(sb, decision.BiasLabel ?? string.Empty);
                AppendCsv(sb, decision.SetupLabel ?? string.Empty);
                AppendCsv(sb, decision.Confidence.ToString(CultureInfo.InvariantCulture));
                AppendCsv(sb, decision.ActionabilityLabel ?? string.Empty);
                AppendCsv(sb, ToBooleanToken(decision.PlaybookActionable));
                AppendCsv(sb, decision.EntryArchetype ?? string.Empty);
                AppendCsv(sb, ToInvariant(executionSignal.Close));
                AppendCsv(sb, ToInvariant(executionSignal.Atr));
                AppendCsv(sb, ToInvariant(executionSignal.Adx));
                AppendCsv(sb, ToInvariant(executionSignal.Rsi));
                AppendCsv(sb, ToInvariant(executionSignal.StochK));
                AppendCsv(sb, ToInvariant(executionSignal.ZScore));
                AppendCsv(sb, ToInvariantNullable(executionSignal.OrderFlowScore));
                AppendCsv(sb, ToInvariantNullable(executionSignal.OrderFlowConfidence));
                AppendCsv(sb, ToInvariantNullable(executionSignal.OrderFlowCumulativeDelta));
                AppendCsv(sb, ToInvariantNullable(executionSignal.OrderFlowDeltaChange));
                AppendCsv(sb, ToInvariantNullable(executionSignal.OrderFlowVwap));
                AppendCsv(sb, ToInvariantNullable(executionSignal.OrderFlowVwapDeviation));
                AppendCsv(sb, ToInvariantNullable(executionSignal.OrderFlowAggressionBalance));
                AppendCsv(sb, ToInvariantNullable(executionSignal.OrderFlowDepthImbalance));
                AppendCsv(sb, executionSignal.SignalLabel ?? string.Empty);
                AppendCsv(sb, Position.MarketPosition.ToString());
                AppendCsv(sb, Position.Quantity.ToString(CultureInfo.InvariantCulture));
                AppendCsv(sb, ToInvariantFinite(GetUnrealizedPnlSafe(executionSignal.Close)));
                AppendCsv(sb, ToInvariantFinite(GetRealizedPnlSafe()));
                AppendCsv(sb, rowAction ?? string.Empty);
                AppendCsv(sb, rowReason ?? string.Empty);
                AppendCsv(sb, decision.GateReason ?? string.Empty);
                AppendCsv(sb, ToInvariantFinite(decision.ScoreDelta));
                AppendCsv(sb, ToBooleanToken(decision.LongSetup));
                AppendCsv(sb, ToBooleanToken(decision.ShortSetup));
                AppendCsv(sb, ToBooleanToken(decision.EntryGatePass));
                AppendCsv(sb, ToBooleanToken(decision.LongAllowed));
                AppendCsv(sb, ToBooleanToken(decision.ShortAllowed));
                AppendCsv(sb, ToBooleanToken(decision.SessionPass));
                AppendCsv(sb, ToBooleanToken(decision.DailyPass));
                AppendCsv(sb, ToBooleanToken(decision.MaxEntriesPass));
                AppendCsv(sb, ToBooleanToken(decision.CooldownPass));
                AppendCsv(sb, ToBooleanToken(decision.NeutralPass));
                AppendCsv(sb, ToBooleanToken(decision.AdxPass));
                AppendCsv(sb, ToBooleanToken(decision.ChoppinessPass));
                AppendCsv(sb, ToBooleanToken(decision.SlopeLongPass));
                AppendCsv(sb, ToBooleanToken(decision.SlopeShortPass));
                AppendCsv(sb, ToBooleanToken(decision.BaseLongPass));
                AppendCsv(sb, ToBooleanToken(decision.BaseShortPass));
                AppendCsv(sb, ToBooleanToken(decision.OrderFlowLongPass));
                AppendCsv(sb, ToBooleanToken(decision.OrderFlowShortPass));
                AppendCsv(sb, ToBooleanToken(decision.MinHoldPass));
                AppendCsv(sb, ToBooleanToken(decision.OrderFlowAvailable));
                AppendCsv(sb, ToInvariantFinite(decision.DailyRealizedPnl));
                AppendCsv(sb, decision.EntriesToday >= 0 ? decision.EntriesToday.ToString(CultureInfo.InvariantCulture) : string.Empty);
                AppendCsv(sb, decision.BarsSinceLastEntry == int.MaxValue ? string.Empty : decision.BarsSinceLastEntry.ToString(CultureInfo.InvariantCulture));
                AppendCsv(sb, decision.BarsSincePositionEntry == int.MaxValue ? string.Empty : decision.BarsSincePositionEntry.ToString(CultureInfo.InvariantCulture));
                AppendCsv(sb, ToBooleanToken(decision.DailyLockActive));
                AppendCsv(sb, decision.DailyLockReason ?? string.Empty);

                _telemetryWriter.WriteLine(sb.ToString());
                _telemetryRowCount++;

                if (!TelemetryAutoFlush &&
                    TelemetryFlushIntervalBars > 0 &&
                    (_telemetryRowCount % TelemetryFlushIntervalBars) == 0)
                {
                    _telemetryWriter.Flush();
                }
            }
            catch (Exception ex)
            {
                if (!_hasLoggedTelemetryWriteError)
                {
                    _hasLoggedTelemetryWriteError = true;
                    Print("GlitchPlaybook telemetry write failed: " + ex.Message);
                }
            }
        }

        private static string ToBooleanToken(bool value)
        {
            return value ? "1" : "0";
        }

        private double GetUnrealizedPnlSafe(double close)
        {
            try
            {
                return Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, close);
            }
            catch
            {
                return double.NaN;
            }
        }

        private double GetRealizedPnlSafe()
        {
            try
            {
                if (SystemPerformance == null ||
                    SystemPerformance.AllTrades == null ||
                    SystemPerformance.AllTrades.TradesPerformance == null ||
                    SystemPerformance.AllTrades.TradesPerformance.Currency == null)
                {
                    return double.NaN;
                }

                return SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            }
            catch
            {
                return double.NaN;
            }
        }

        private static string SanitizeFileToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "NA";

            string token = value.Trim();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
                token = token.Replace(invalid[i], '_');
            token = token.Replace(' ', '_');
            return token;
        }

        private static void AppendCsv(StringBuilder sb, string value)
        {
            if (sb.Length > 0)
                sb.Append(',');

            if (string.IsNullOrEmpty(value))
                return;

            bool mustQuote =
                value.IndexOf(',') >= 0 ||
                value.IndexOf('"') >= 0 ||
                value.IndexOf('\r') >= 0 ||
                value.IndexOf('\n') >= 0;

            if (!mustQuote)
            {
                sb.Append(value);
                return;
            }

            sb.Append('"');
            sb.Append(value.Replace("\"", "\"\""));
            sb.Append('"');
        }

        private static string ToInvariant(double value)
        {
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private static string ToInvariantNullable(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.########", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static string ToInvariantFinite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return string.Empty;
            return value.ToString("0.########", CultureInfo.InvariantCulture);
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

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private enum PlaybookType
        {
            NoTrade = 0,
            TrendContinuation = 1,
            Pullback = 2,
            ReversalWatch = 3
        }

        private struct PivotPoint
        {
            public bool IsHigh;
            public int BarsAgo;
            public double Price;
        }

        private struct PlaybookDecision
        {
            public string Action;
            public string Reason;
            public string GateReason;
            public string BiasLabel;
            public string SetupLabel;
            public string ActionabilityLabel;
            public int ExecutionMinutes;
            public int SetupKind;
            public int Confidence;
            public int ContextSign;
            public int TriggerSign;
            public int MicroSign;
            public int BiasSign;
            public double ContextScore;
            public double TriggerScore;
            public double PrevTriggerScore;
            public double MicroScore;
            public double DirectionalScore;
            public double Divergence;
            public double AdxProxy;
            public double ExecutionBaseScore;
            public double ExecutionScore;
            public double ScoreDelta;
            public PlaybookType Playbook;
            public bool Actionable;
            public bool PlaybookActionable;
            public bool LongSetup;
            public bool ShortSetup;
            public bool EntryGatePass;
            public bool LongAllowed;
            public bool ShortAllowed;
            public bool SessionPass;
            public bool DailyPass;
            public bool MaxEntriesPass;
            public bool CooldownPass;
            public bool NeutralPass;
            public bool AdxPass;
            public bool SlopeLongPass;
            public bool SlopeShortPass;
            public bool BaseLongPass;
            public bool BaseShortPass;
            public bool OrderFlowLongPass;
            public bool OrderFlowShortPass;
            public bool ChoppinessPass;
            public bool MinHoldPass;
            public bool OrderFlowAvailable;
            public double DailyRealizedPnl;
            public int EntriesToday;
            public int BarsSinceLastEntry;
            public int BarsSincePositionEntry;
            public bool DailyLockActive;
            public string DailyLockReason;
            public string EntryArchetype;

            public static PlaybookDecision CreateDefault()
            {
                return new PlaybookDecision
                {
                    Action = string.Empty,
                    Reason = string.Empty,
                    GateReason = string.Empty,
                    BiasLabel = "Neutral",
                    SetupLabel = "No-Trade",
                    ActionabilityLabel = "Wait",
                    ExecutionMinutes = 5,
                    SetupKind = 0,
                    Confidence = 0,
                    ContextSign = 0,
                    TriggerSign = 0,
                    MicroSign = 0,
                    BiasSign = 0,
                    ContextScore = 0,
                    TriggerScore = 0,
                    PrevTriggerScore = 0,
                    MicroScore = 0,
                    DirectionalScore = 0,
                    Divergence = 0,
                    AdxProxy = 0,
                    ExecutionBaseScore = 0,
                    ExecutionScore = 0,
                    ScoreDelta = 0,
                    Playbook = PlaybookType.NoTrade,
                    Actionable = false,
                    PlaybookActionable = false,
                    LongSetup = false,
                    ShortSetup = false,
                    EntryGatePass = false,
                    LongAllowed = false,
                    ShortAllowed = false,
                    SessionPass = true,
                    DailyPass = true,
                    MaxEntriesPass = true,
                    CooldownPass = true,
                    NeutralPass = true,
                    AdxPass = true,
                    SlopeLongPass = true,
                    SlopeShortPass = true,
                    BaseLongPass = true,
                    BaseShortPass = true,
                    OrderFlowLongPass = true,
                    OrderFlowShortPass = true,
                    ChoppinessPass = true,
                    MinHoldPass = true,
                    OrderFlowAvailable = false,
                    DailyRealizedPnl = 0,
                    EntriesToday = 0,
                    BarsSinceLastEntry = int.MaxValue,
                    BarsSincePositionEntry = int.MaxValue,
                    DailyLockActive = false,
                    DailyLockReason = string.Empty,
                    EntryArchetype = "none"
                };
            }
        }

        private struct SignalSnapshot
        {
            public int Minutes;
            public int Bips;
            public bool IsLive;
            public double Open;
            public double High;
            public double Low;
            public double Close;
            public double Volume;
            public double AveragePrice;
            public double Atr;
            public double Adx;
            public double Rsi;
            public double StochK;
            public double ZScore;
            public double EmaSlopeSignal;
            public double EmaAlignment;
            public double RsiSignal;
            public double StochSignal;
            public double ZSignal;
            public double RegimeWeight;
            public double BaseScore;
            public double Score;
            public string SignalLabel;
            public double? OrderFlowScore;
            public double? OrderFlowConfidence;
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
    }
}
