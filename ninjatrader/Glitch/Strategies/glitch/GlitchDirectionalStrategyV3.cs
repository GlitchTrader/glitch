#region Using declarations
using System;
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
    public class GlitchDirectionalStrategyV3 : Strategy
    {
        private const int MinBarsForSignal = 30;
        private const int ZLookback = 30;
        private const string StrategyProfileVersion = "v3.0-perf-baseline";

        private EMA _emaFast;
        private EMA _emaMed;
        private EMA _emaSlow;
        private ATR _atr;
        private ADX _adx;
        private RSI _rsi;
        private Stochastics _stoch;
        private SMA _sma;

        private OrderFlowCumulativeDelta _orderFlowDelta;
        private OrderFlowVWAP _orderFlowVwap;
        private bool _isOrderFlowRuntimeAvailable;
        private bool _hasLoggedOrderFlowUnavailable;
        private int _orderFlowTickBip = -1;

        private Series<double> _scoreSeries;
        private double _tickSize;

        private StreamWriter _telemetryWriter;
        private string _telemetryFilePath;
        private bool _telemetryHeaderWritten;
        private long _telemetryRowCount;
        private long _telemetryBarsSeen;
        private bool _hasLoggedTelemetryWriteError;
        private SignalSnapshot _lastSignalSnapshot;
        private bool _hasLastSignalSnapshot;
        private bool _telemetryRuntimeEnabled;

        private int _lastEntryBar = -1;
        private int _entriesToday;
        private DateTime _currentTradeDate = DateTime.MinValue;
        private double _dailyStartCumProfit;
        private bool _dailyStateInitialized;
        private bool _dailyTradingLocked;
        private string _dailyLockReason = string.Empty;
        private bool _dailyFlattenSubmitted;
        private double _lastKnownCumProfit;

        [NinjaScriptProperty]
        [Range(0.10, 1.00)]
        [Display(Name = "Long Entry Threshold", Order = 1, GroupName = "Signals")]
        public double LongEntryThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(-1.00, -0.10)]
        [Display(Name = "Short Entry Threshold", Order = 2, GroupName = "Signals")]
        public double ShortEntryThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(-0.30, 0.30)]
        [Display(Name = "Long Exit Threshold", Order = 3, GroupName = "Signals")]
        public double LongExitThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(-0.30, 0.30)]
        [Display(Name = "Short Exit Threshold", Order = 4, GroupName = "Signals")]
        public double ShortExitThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Immediate Reversal", Order = 5, GroupName = "Signals")]
        public bool AllowImmediateReversal { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 6, GroupName = "Signals")]
        public bool EnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(5, 60)]
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
        [Display(Name = "Enable Order Flow Layer", Order = 1, GroupName = "Order Flow")]
        public bool EnableOrderFlowLayer { get; set; }

        [NinjaScriptProperty]
        [Range(0.00, 0.80)]
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
                Description = "Standalone Glitch directional strategy (v3) tuned for fast backtest throughput.";
                Name = "GlitchDirectionalStrategyV3";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
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

                LongEntryThreshold = 0.35;
                ShortEntryThreshold = -0.35;
                LongExitThreshold = 0.05;
                ShortExitThreshold = -0.05;
                AllowImmediateReversal = false;
                EnableAdxFilter = true;
                MinAdxForEntries = 18.0;

                EnableNeutralZoneFilter = true;
                NeutralZoneHalfWidth = 0.12;
                EnableScoreSlopeFilter = true;
                MinScoreDeltaForEntry = 0.02;
                RequireBaseScoreAlignment = true;
                MinBarsBetweenEntries = 8;
                MinBarsInPosition = 2;

                UseStopLoss = true;
                StopAtrMultiplier = 1.50;
                UseProfitTarget = true;
                TargetAtrMultiplier = 2.25;

                EnableOrderFlowLayer = true;
                OrderFlowBlend = 0.25;
                RequireOrderFlowConfirmation = true;
                MinOrderFlowScoreForLong = 0.05;
                MaxOrderFlowScoreForShort = -0.05;

                UseSessionTimeFilter = true;
                SessionStartTime = 93000;
                SessionEndTime = 155500;

                MaxEntriesPerDay = 8;
                UseDailyPnlGuard = true;
                DailyProfitTargetCurrency = 250;
                DailyLossLimitCurrency = 250;
                FlattenOnDailyGuardHit = true;

                FastBacktestMode = true;
                EnableTelemetryExport = false;
                ExportHistoricalTelemetry = false;
                TelemetryFilePrefix = "GlitchDirectionalV3";
                TelemetryAutoFlush = false;
                TelemetryFlushIntervalBars = 250;
                EnableExecutionTelemetryRows = false;
            }
            else if (State == State.Configure)
            {
                if (EnableOrderFlowLayer)
                    AddOrderFlowTickSeries();
            }
            else if (State == State.DataLoaded)
            {
                _emaFast = EMA(12);
                _emaMed = EMA(26);
                _emaSlow = EMA(55);
                _atr = ATR(14);
                _adx = ADX(14);
                _rsi = RSI(14, 3);
                _stoch = Stochastics(14, 3, 3);
                _sma = SMA(20);

                _scoreSeries = new Series<double>(this);
                _tickSize = (Instrument != null && Instrument.MasterInstrument != null && Instrument.MasterInstrument.TickSize > 0)
                    ? Instrument.MasterInstrument.TickSize
                    : 0.25;
                _orderFlowTickBip = ResolveOrderFlowTickBip();
                _lastEntryBar = -1;
                _entriesToday = 0;
                _currentTradeDate = DateTime.MinValue;
                _dailyStartCumProfit = 0;
                _dailyStateInitialized = false;
                _dailyTradingLocked = false;
                _dailyLockReason = string.Empty;
                _dailyFlattenSubmitted = false;
                _lastKnownCumProfit = 0;
                _hasLastSignalSnapshot = false;
                _hasLoggedTelemetryWriteError = false;
                _telemetryRuntimeEnabled = EnableTelemetryExport && !FastBacktestMode;

                InitializeOrderFlowLayer();
                if (_telemetryRuntimeEnabled)
                    InitializeTelemetryWriter();
                _telemetryRuntimeEnabled = _telemetryWriter != null;
                Print(
                    "GlitchDirectionalStrategyV3 DataLoaded | telemetry enabled: " +
                    _telemetryRuntimeEnabled.ToString(CultureInfo.InvariantCulture) +
                    " | fast backtest mode: " +
                    FastBacktestMode.ToString(CultureInfo.InvariantCulture) +
                    " | export historical: " +
                    ExportHistoricalTelemetry.ToString(CultureInfo.InvariantCulture) +
                    " | file: " +
                    (_telemetryFilePath ?? "(none)"));
            }
            else if (State == State.Terminated)
            {
                if (_telemetryRuntimeEnabled &&
                    (_telemetryBarsSeen > 0 || _telemetryRowCount > 0 || !string.IsNullOrWhiteSpace(_telemetryFilePath)))
                {
                    Print(
                        "GlitchDirectionalStrategyV3 telemetry summary | bars seen: " +
                        _telemetryBarsSeen.ToString(CultureInfo.InvariantCulture) +
                        " | rows written: " +
                        _telemetryRowCount.ToString(CultureInfo.InvariantCulture) +
                        " | file: " +
                        (_telemetryFilePath ?? "(none)"));
                }
                _orderFlowTickBip = -1;
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

            SignalSnapshot signal = CurrentBar >= MinBarsForSignal
                ? BuildSignal()
                : BuildWarmupSignal();
            if (_telemetryRuntimeEnabled && EnableExecutionTelemetryRows)
            {
                _lastSignalSnapshot = signal;
                _hasLastSignalSnapshot = true;
            }

            _scoreSeries[0] = signal.Score;

            DecisionSnapshot decision;
            if (CurrentBar >= MinBarsForSignal && CurrentBar > 0)
                decision = EvaluateTradingSignals(signal, Times[0][0]);
            else
                decision = BuildWarmupDecision(signal, Times[0][0]);

            if (_telemetryRuntimeEnabled)
                WriteTelemetry(signal, decision, "bar", decision.Action, decision.Reason, string.Empty, null, null, null, string.Empty, Times[0][0]);
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

            SignalSnapshot signal = _hasLastSignalSnapshot
                ? _lastSignalSnapshot
                : BuildFallbackSignal(price);
            DecisionSnapshot decision = BuildRuntimeDecisionSnapshot(signal, time);

            string eventSignal = execution.Order != null ? execution.Order.Name : string.Empty;
            string eventAction = execution.Order != null ? execution.Order.OrderAction.ToString() : string.Empty;
            string rowReason = string.IsNullOrWhiteSpace(eventSignal) ? "execution_fill" : eventSignal;

            WriteTelemetry(
                signal,
                decision,
                "execution",
                "execution_fill",
                rowReason,
                eventAction,
                price,
                quantity,
                marketPosition,
                orderId ?? executionId,
                time);
        }

        private SignalSnapshot BuildWarmupSignal()
        {
            var signal = new SignalSnapshot();
            signal.Close = Close[0];
            signal.Open = Open[0];
            signal.High = High[0];
            signal.Low = Low[0];
            signal.Volume = Volume[0];
            signal.AveragePrice = _sma != null ? _sma[0] : Close[0];
            signal.Atr = _atr != null ? _atr[0] : 0;
            signal.Adx = _adx != null ? _adx[0] : 0;
            signal.Rsi = _rsi != null ? _rsi[0] : 50;
            signal.StochK = _stoch != null ? _stoch.K[0] : 50;
            signal.ZScore = 0;
            signal.EmaSlopeSignal = 0;
            signal.EmaAlignment = 0;
            signal.RsiSignal = 0;
            signal.StochSignal = 0;
            signal.ZSignal = 0;
            signal.RegimeWeight = 0;
            signal.BaseScore = 0;
            signal.Score = 0;
            signal.SignalLabel = _telemetryRuntimeEnabled ? ToSignalLabel(0) : string.Empty;
            return signal;
        }

        private SignalSnapshot BuildSignal()
        {
            var signal = new SignalSnapshot();
            signal.Close = Close[0];
            signal.Open = Open[0];
            signal.High = High[0];
            signal.Low = Low[0];
            signal.Volume = Volume[0];
            signal.AveragePrice = _sma[0];
            signal.Atr = _atr[0];
            signal.Adx = _adx[0];
            signal.Rsi = _rsi[0];
            signal.StochK = _stoch.K[0];
            signal.ZScore = ComputeZScore(Close, Math.Min(ZLookback, CurrentBar + 1));

            double atrNormalization = Math.Max(Math.Max(signal.Atr, _tickSize), 1e-8);

            double fastSlopeN = (_emaFast[0] - _emaFast[1]) / atrNormalization;
            double medSlopeN = (_emaMed[0] - _emaMed[1]) / atrNormalization;
            double slowSlopeN = (_emaSlow[0] - _emaSlow[1]) / atrNormalization;

            signal.EmaSlopeSignal = Clamp((fastSlopeN * 0.50) + (medSlopeN * 0.30) + (slowSlopeN * 0.20), -1, 1);
            signal.EmaAlignment = BuildEmaAlignmentSignal(_emaFast[0], _emaMed[0], _emaSlow[0]);
            signal.RsiSignal = Clamp((signal.Rsi - 50.0) / 22.0, -1, 1);
            signal.StochSignal = Clamp((signal.StochK - 50.0) / 28.0, -1, 1);
            signal.ZSignal = Clamp(signal.ZScore / 2.2, -1, 1);
            signal.RegimeWeight = BuildRegimeWeight(signal.Adx);

            double directionalCore =
                (signal.EmaSlopeSignal * 0.42) +
                (signal.EmaAlignment * 0.23) +
                (signal.RsiSignal * 0.14) +
                (signal.StochSignal * 0.11) +
                (signal.ZSignal * 0.10);

            signal.BaseScore = Clamp(directionalCore * signal.RegimeWeight, -1, 1);
            signal.Score = signal.BaseScore;

            double? orderFlowScore;
            double? orderFlowConfidence;
            double? orderFlowDelta;
            double? orderFlowDeltaChange;
            double? orderFlowVwap;
            double? orderFlowVwapDeviation;
            TryBuildOrderFlowSnapshot(
                signal.Close,
                signal.Atr,
                out orderFlowScore,
                out orderFlowConfidence,
                out orderFlowDelta,
                out orderFlowDeltaChange,
                out orderFlowVwap,
                out orderFlowVwapDeviation);

            signal.OrderFlowScore = orderFlowScore;
            signal.OrderFlowConfidence = orderFlowConfidence;
            signal.OrderFlowCumulativeDelta = orderFlowDelta;
            signal.OrderFlowDeltaChange = orderFlowDeltaChange;
            signal.OrderFlowVwap = orderFlowVwap;
            signal.OrderFlowVwapDeviation = orderFlowVwapDeviation;

            if (orderFlowScore.HasValue && OrderFlowBlend > 0)
            {
                double blend = Clamp(OrderFlowBlend, 0, 0.80);
                signal.Score = Clamp(
                    (signal.BaseScore * (1.0 - blend)) + (orderFlowScore.Value * blend),
                    -1,
                    1);
            }

            signal.SignalLabel = _telemetryRuntimeEnabled ? ToSignalLabel(signal.Score) : string.Empty;
            return signal;
        }

        private SignalSnapshot BuildFallbackSignal(double price)
        {
            return new SignalSnapshot
            {
                Open = price,
                High = price,
                Low = price,
                Close = price,
                Volume = 0,
                AveragePrice = price,
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
                RegimeWeight = 0,
                BaseScore = 0,
                Score = 0,
                SignalLabel = string.Empty,
                OrderFlowScore = null,
                OrderFlowConfidence = null,
                OrderFlowCumulativeDelta = null,
                OrderFlowDeltaChange = null,
                OrderFlowVwap = null,
                OrderFlowVwapDeviation = null
            };
        }

        private DecisionSnapshot BuildWarmupDecision(SignalSnapshot signal, DateTime barTime)
        {
            DecisionSnapshot decision = BuildRuntimeDecisionSnapshot(signal, barTime);
            decision.Reason = "warmup";
            decision.GateReason = "warmup";
            return decision;
        }

        private DecisionSnapshot BuildRuntimeDecisionSnapshot(SignalSnapshot signal, DateTime barTime)
        {
            DecisionSnapshot decision = DecisionSnapshot.CreateDefault();
            double cumProfit = ResolveCumProfitSnapshot();
            RollDailyState(barTime, cumProfit);
            double dailyRealized = _dailyStateInitialized ? cumProfit - _dailyStartCumProfit : 0;
            bool lockTriggered = UpdateDailyLockState(dailyRealized);

            decision.ScoreDelta = CurrentBar > 0 ? (_scoreSeries[0] - _scoreSeries[1]) : 0;
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

            if (lockTriggered)
                decision.GateReason = "daily_lock_triggered";

            return decision;
        }

        private DecisionSnapshot EvaluateTradingSignals(SignalSnapshot signal, DateTime barTime)
        {
            DecisionSnapshot decision = BuildRuntimeDecisionSnapshot(signal, barTime);
            bool lockTriggeredNow = string.Equals(decision.GateReason, "daily_lock_triggered", StringComparison.OrdinalIgnoreCase);

            decision.SessionPass = !UseSessionTimeFilter || IsInsideSessionWindow(barTime);
            decision.DailyPass = !decision.DailyLockActive;
            decision.MaxEntriesPass = MaxEntriesPerDay <= 0 || _entriesToday < MaxEntriesPerDay;
            decision.CooldownPass = MinBarsBetweenEntries <= 0 || decision.BarsSinceLastEntry >= MinBarsBetweenEntries;
            decision.NeutralPass = !EnableNeutralZoneFilter || Math.Abs(signal.Score) >= NeutralZoneHalfWidth;
            decision.AdxPass = !EnableAdxFilter || signal.Adx >= MinAdxForEntries;
            decision.SlopeLongPass = !EnableScoreSlopeFilter || decision.ScoreDelta >= MinScoreDeltaForEntry;
            decision.SlopeShortPass = !EnableScoreSlopeFilter || decision.ScoreDelta <= -MinScoreDeltaForEntry;
            decision.BaseLongPass = !RequireBaseScoreAlignment || signal.BaseScore >= 0.05;
            decision.BaseShortPass = !RequireBaseScoreAlignment || signal.BaseScore <= -0.05;
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
                decision.AdxPass;

            decision.LongSetup = _scoreSeries[0] >= LongEntryThreshold && _scoreSeries[1] < LongEntryThreshold;
            decision.ShortSetup = _scoreSeries[0] <= ShortEntryThreshold && _scoreSeries[1] > ShortEntryThreshold;
            decision.LongAllowed =
                decision.EntryGatePass &&
                decision.SlopeLongPass &&
                decision.BaseLongPass &&
                decision.OrderFlowLongPass;
            decision.ShortAllowed =
                decision.EntryGatePass &&
                decision.SlopeShortPass &&
                decision.BaseShortPass &&
                decision.OrderFlowShortPass;

            if (lockTriggeredNow &&
                FlattenOnDailyGuardHit &&
                Position.MarketPosition != MarketPosition.Flat &&
                !_dailyFlattenSubmitted)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong("L-DailyGuard", "L-Entry");
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort("S-DailyGuard", "S-Entry");

                _dailyFlattenSubmitted = true;
                decision.Action = "exit_daily_guard";
                decision.Reason = string.IsNullOrWhiteSpace(decision.DailyLockReason) ? "daily_guard_lock" : decision.DailyLockReason;
                if (string.IsNullOrWhiteSpace(decision.GateReason))
                    decision.GateReason = "daily_guard_flatten";
                return decision;
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (decision.LongSetup && decision.LongAllowed)
                {
                    ConfigureRiskForEntry("L-Entry", signal.Atr);
                    EnterLong("L-Entry");
                    MarkEntry(barTime);
                    decision.Action = "enter_long";
                    decision.Reason = "score_cross_long";
                    return decision;
                }

                if (decision.ShortSetup && decision.ShortAllowed)
                {
                    ConfigureRiskForEntry("S-Entry", signal.Atr);
                    EnterShort("S-Entry");
                    MarkEntry(barTime);
                    decision.Action = "enter_short";
                    decision.Reason = "score_cross_short";
                    return decision;
                }

                if (decision.LongSetup)
                    decision.GateReason = BuildEntryBlockReason("long", decision);
                else if (decision.ShortSetup)
                    decision.GateReason = BuildEntryBlockReason("short", decision);

                return decision;
            }

            if (Position.MarketPosition == MarketPosition.Long)
            {
                bool exitForSignal = _scoreSeries[0] <= LongExitThreshold && decision.MinHoldPass;
                bool reverseSignal = AllowImmediateReversal && decision.ShortSetup && decision.ShortAllowed && decision.MinHoldPass;
                if (exitForSignal || reverseSignal)
                {
                    ExitLong("L-ExitSignal", "L-Entry");
                    decision.Action = reverseSignal ? "exit_long_reverse" : "exit_long_signal";
                    decision.Reason = reverseSignal ? "reverse_short" : "score_exit_long";

                    if (reverseSignal)
                    {
                        ConfigureRiskForEntry("S-Entry", signal.Atr);
                        EnterShort("S-Entry");
                        MarkEntry(barTime);
                        decision.Action = "reverse_to_short";
                    }
                }
                else if (_scoreSeries[0] <= LongExitThreshold && !decision.MinHoldPass)
                {
                    decision.GateReason = "blocked_min_hold_long_exit";
                }

                return decision;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                bool exitForSignal = _scoreSeries[0] >= ShortExitThreshold && decision.MinHoldPass;
                bool reverseSignal = AllowImmediateReversal && decision.LongSetup && decision.LongAllowed && decision.MinHoldPass;
                if (exitForSignal || reverseSignal)
                {
                    ExitShort("S-ExitSignal", "S-Entry");
                    decision.Action = reverseSignal ? "exit_short_reverse" : "exit_short_signal";
                    decision.Reason = reverseSignal ? "reverse_long" : "score_exit_short";

                    if (reverseSignal)
                    {
                        ConfigureRiskForEntry("L-Entry", signal.Atr);
                        EnterLong("L-Entry");
                        MarkEntry(barTime);
                        decision.Action = "reverse_to_long";
                    }
                }
                else if (_scoreSeries[0] >= ShortExitThreshold && !decision.MinHoldPass)
                {
                    decision.GateReason = "blocked_min_hold_short_exit";
                }
            }

            return decision;
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

        private static string BuildEntryBlockReason(string side, DecisionSnapshot decision)
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

        private void InitializeOrderFlowLayer()
        {
            _orderFlowDelta = null;
            _orderFlowVwap = null;
            _isOrderFlowRuntimeAvailable = false;
            _hasLoggedOrderFlowUnavailable = false;

            if (!EnableOrderFlowLayer)
                return;
            if (_orderFlowTickBip < 0)
            {
                LogOrderFlowUnavailableOnce("GlitchDirectionalStrategy order flow unavailable: missing 1-tick data series.");
                return;
            }

            try
            {
                _orderFlowDelta = OrderFlowCumulativeDelta(
                    BarsArray[0],
                    CumulativeDeltaType.BidAsk,
                    CumulativeDeltaPeriod.Session,
                    0);

                _orderFlowVwap = OrderFlowVWAP(
                    BarsArray[0],
                    VWAPResolution.Standard,
                    BarsArray[0].TradingHours,
                    VWAPStandardDeviations.Three,
                    1.0,
                    2.0,
                    3.0);

                _isOrderFlowRuntimeAvailable = _orderFlowDelta != null;
            }
            catch (Exception ex)
            {
                _orderFlowDelta = null;
                _orderFlowVwap = null;
                _isOrderFlowRuntimeAvailable = false;
                LogOrderFlowUnavailableOnce("GlitchDirectionalStrategy order flow unavailable: " + ex.Message);
            }
        }

        private void TryBuildOrderFlowSnapshot(
            double close,
            double atr,
            out double? score,
            out double? confidence,
            out double? cumulativeDelta,
            out double? deltaChange,
            out double? vwap,
            out double? vwapDeviation)
        {
            score = null;
            confidence = null;
            cumulativeDelta = null;
            deltaChange = null;
            vwap = null;
            vwapDeviation = null;

            if (!EnableOrderFlowLayer || !_isOrderFlowRuntimeAvailable || _orderFlowDelta == null)
                return;

            try
            {
                cumulativeDelta = _orderFlowDelta.DeltaClose[0];
                double previousDelta = CurrentBar > 0 ? _orderFlowDelta.DeltaClose[1] : _orderFlowDelta.DeltaClose[0];
                deltaChange = cumulativeDelta.Value - previousDelta;
            }
            catch (Exception ex)
            {
                LogOrderFlowUnavailableOnce("GlitchDirectionalStrategy order flow delta read failed: " + ex.Message);
                return;
            }

            double barVolume = Math.Max(Volume[0], 0);
            double deltaPressure = 0;
            if (deltaChange.HasValue)
            {
                double normalization = Math.Max(barVolume * 0.30, 1.0);
                deltaPressure = Clamp(deltaChange.Value / normalization, -1, 1);
            }

            double vwapBias = 0;
            if (_orderFlowVwap != null)
            {
                try
                {
                    double currentVwap = _orderFlowVwap.VWAP[0];
                    if (currentVwap > 0)
                    {
                        vwap = currentVwap;
                        double normalization = Math.Max(Math.Max(atr, _tickSize), 1e-8);
                        vwapBias = Clamp((close - currentVwap) / normalization, -1, 1);

                        double stdUpper = _orderFlowVwap.StdDev1Upper[0];
                        double stdLower = _orderFlowVwap.StdDev1Lower[0];
                        double stdSpan = Math.Abs(stdUpper - stdLower);
                        if (stdSpan > 1e-8)
                        {
                            double sigma = (close - currentVwap) / (stdSpan * 0.5);
                            vwapDeviation = Clamp(sigma, -5, 5);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogOrderFlowUnavailableOnce("GlitchDirectionalStrategy order flow VWAP read failed: " + ex.Message);
                }
            }

            double ofScore = Clamp((deltaPressure * 0.85) + (vwapBias * 0.15), -1, 1);
            score = ofScore;

            double confidenceValue = Math.Min(0.85, Math.Abs(deltaPressure) * 0.85);
            if (vwap.HasValue)
                confidenceValue += 0.15;
            confidence = Clamp(confidenceValue, 0, 1);
        }

        private void LogOrderFlowUnavailableOnce(string message)
        {
            if (_hasLoggedOrderFlowUnavailable)
                return;

            _hasLoggedOrderFlowUnavailable = true;
            Print(string.IsNullOrWhiteSpace(message)
                ? "GlitchDirectionalStrategy order flow unavailable."
                : message);
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

        private void RefreshOrderFlowFromTickSeries()
        {
            if (!_isOrderFlowRuntimeAvailable || _orderFlowDelta == null)
                return;

            try
            {
                if (_orderFlowDelta.BarsArray == null || _orderFlowDelta.BarsArray.Length < 2 || _orderFlowDelta.BarsArray[1] == null)
                    return;

                int tickCount = _orderFlowDelta.BarsArray[1].Count;
                if (tickCount <= 0)
                    return;

                _orderFlowDelta.Update(tickCount - 1, 1);
            }
            catch (Exception ex)
            {
                _isOrderFlowRuntimeAvailable = false;
                LogOrderFlowUnavailableOnce("GlitchDirectionalStrategy order flow tick refresh disabled: " + ex.Message);
            }
        }

        private void InitializeTelemetryWriter()
        {
            if (!EnableTelemetryExport || FastBacktestMode)
                return;

            try
            {
                string baseDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "GlitchTelemetry");
                Directory.CreateDirectory(baseDir);

                string prefix = SanitizeFileToken(string.IsNullOrWhiteSpace(TelemetryFilePrefix) ? "GlitchDirectionalV3" : TelemetryFilePrefix);
                string instrumentToken = SanitizeFileToken(Instrument == null ? "Unknown" : Instrument.FullName);
                string barsToken = SanitizeFileToken(BarsPeriod.BarsPeriodType + "_" + BarsPeriod.Value);
                string runToken = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);

                _telemetryFilePath = Path.Combine(
                    baseDir,
                    prefix + "_" + instrumentToken + "_" + barsToken + "_" + runToken + ".csv");

                _telemetryWriter = new StreamWriter(new FileStream(_telemetryFilePath, FileMode.Create, FileAccess.Write, FileShare.Read));
                _telemetryWriter.AutoFlush = TelemetryAutoFlush;
                _telemetryHeaderWritten = false;
                _telemetryRowCount = 0;
                EnsureTelemetryHeader();
                Print("GlitchDirectionalStrategyV3 telemetry: " + _telemetryFilePath);
            }
            catch (Exception ex)
            {
                _telemetryWriter = null;
                _telemetryFilePath = null;
                Print("GlitchDirectionalStrategyV3 telemetry init failed: " + ex.Message);
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
        }

        private void EnsureTelemetryHeader()
        {
            if (_telemetryWriter == null || _telemetryHeaderWritten)
                return;

            var sb = new StringBuilder(512);
            AppendCsv(sb, "utc_time");
            AppendCsv(sb, "local_time");
            AppendCsv(sb, "instrument");
            AppendCsv(sb, "bar_index");
            AppendCsv(sb, "open");
            AppendCsv(sb, "high");
            AppendCsv(sb, "low");
            AppendCsv(sb, "close");
            AppendCsv(sb, "volume");
            AppendCsv(sb, "avg_price");
            AppendCsv(sb, "atr");
            AppendCsv(sb, "adx");
            AppendCsv(sb, "rsi");
            AppendCsv(sb, "stoch_k");
            AppendCsv(sb, "z_score");
            AppendCsv(sb, "ema_slope_signal");
            AppendCsv(sb, "ema_alignment");
            AppendCsv(sb, "rsi_signal");
            AppendCsv(sb, "stoch_signal");
            AppendCsv(sb, "z_signal");
            AppendCsv(sb, "regime_weight");
            AppendCsv(sb, "base_score");
            AppendCsv(sb, "order_flow_score");
            AppendCsv(sb, "order_flow_confidence");
            AppendCsv(sb, "order_flow_delta");
            AppendCsv(sb, "order_flow_delta_change");
            AppendCsv(sb, "order_flow_vwap");
            AppendCsv(sb, "order_flow_vwap_deviation");
            AppendCsv(sb, "final_score");
            AppendCsv(sb, "signal_label");
            AppendCsv(sb, "position");
            AppendCsv(sb, "quantity");
            AppendCsv(sb, "unrealized_pnl");
            AppendCsv(sb, "realized_pnl");
            AppendCsv(sb, "action");
            AppendCsv(sb, "reason");
            AppendCsv(sb, "row_type");
            AppendCsv(sb, "event_name");
            AppendCsv(sb, "event_action");
            AppendCsv(sb, "event_price");
            AppendCsv(sb, "event_quantity");
            AppendCsv(sb, "event_market_position");
            AppendCsv(sb, "event_order_id");
            AppendCsv(sb, "profile_version");
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
            AppendCsv(sb, "gate_reason_detail");

            _telemetryWriter.WriteLine(sb.ToString());
            _telemetryHeaderWritten = true;
        }

        private void WriteTelemetry(
            SignalSnapshot signal,
            DecisionSnapshot decision,
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
                var sb = new StringBuilder(1400);
                DateTime timestamp = rowTime == DateTime.MinValue ? DateTime.UtcNow : rowTime;
                AppendCsv(sb, timestamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                AppendCsv(sb, timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                AppendCsv(sb, Instrument == null ? string.Empty : Instrument.FullName);
                AppendCsv(sb, string.Equals(rowType, "execution", StringComparison.OrdinalIgnoreCase) ? string.Empty : CurrentBar.ToString(CultureInfo.InvariantCulture));
                AppendCsv(sb, ToInvariant(signal.Open));
                AppendCsv(sb, ToInvariant(signal.High));
                AppendCsv(sb, ToInvariant(signal.Low));
                AppendCsv(sb, ToInvariant(signal.Close));
                AppendCsv(sb, ToInvariant(signal.Volume));
                AppendCsv(sb, ToInvariant(signal.AveragePrice));
                AppendCsv(sb, ToInvariant(signal.Atr));
                AppendCsv(sb, ToInvariant(signal.Adx));
                AppendCsv(sb, ToInvariant(signal.Rsi));
                AppendCsv(sb, ToInvariant(signal.StochK));
                AppendCsv(sb, ToInvariant(signal.ZScore));
                AppendCsv(sb, ToInvariant(signal.EmaSlopeSignal));
                AppendCsv(sb, ToInvariant(signal.EmaAlignment));
                AppendCsv(sb, ToInvariant(signal.RsiSignal));
                AppendCsv(sb, ToInvariant(signal.StochSignal));
                AppendCsv(sb, ToInvariant(signal.ZSignal));
                AppendCsv(sb, ToInvariant(signal.RegimeWeight));
                AppendCsv(sb, ToInvariant(signal.BaseScore));
                AppendCsv(sb, ToInvariantNullable(signal.OrderFlowScore));
                AppendCsv(sb, ToInvariantNullable(signal.OrderFlowConfidence));
                AppendCsv(sb, ToInvariantNullable(signal.OrderFlowCumulativeDelta));
                AppendCsv(sb, ToInvariantNullable(signal.OrderFlowDeltaChange));
                AppendCsv(sb, ToInvariantNullable(signal.OrderFlowVwap));
                AppendCsv(sb, ToInvariantNullable(signal.OrderFlowVwapDeviation));
                AppendCsv(sb, ToInvariant(signal.Score));
                AppendCsv(sb, signal.SignalLabel);
                AppendCsv(sb, Position.MarketPosition.ToString());
                AppendCsv(sb, Position.Quantity.ToString(CultureInfo.InvariantCulture));
                AppendCsv(sb, ToInvariantFinite(GetUnrealizedPnlSafe(signal.Close)));
                AppendCsv(sb, ToInvariantFinite(GetRealizedPnlSafe()));
                AppendCsv(sb, rowAction ?? string.Empty);
                AppendCsv(sb, rowReason ?? string.Empty);
                AppendCsv(sb, rowType ?? string.Empty);
                AppendCsv(sb, string.Equals(rowType, "execution", StringComparison.OrdinalIgnoreCase) ? "execution" : string.Empty);
                AppendCsv(sb, eventAction ?? string.Empty);
                AppendCsv(sb, ToInvariantNullable(eventPrice));
                AppendCsv(sb, eventQuantity.HasValue ? eventQuantity.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
                AppendCsv(sb, eventMarketPosition.HasValue ? eventMarketPosition.Value.ToString() : string.Empty);
                AppendCsv(sb, eventOrderId ?? string.Empty);
                AppendCsv(sb, StrategyProfileVersion);
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
                AppendCsv(sb, decision.GateReason ?? string.Empty);

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
                    Print("GlitchDirectionalStrategy telemetry write failed: " + ex.Message);
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

        private struct DecisionSnapshot
        {
            public string Action;
            public string Reason;
            public string GateReason;
            public double ScoreDelta;
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
            public bool MinHoldPass;
            public bool OrderFlowAvailable;
            public double DailyRealizedPnl;
            public int EntriesToday;
            public int BarsSinceLastEntry;
            public int BarsSincePositionEntry;
            public bool DailyLockActive;
            public string DailyLockReason;

            public static DecisionSnapshot CreateDefault()
            {
                return new DecisionSnapshot
                {
                    Action = string.Empty,
                    Reason = string.Empty,
                    GateReason = string.Empty,
                    ScoreDelta = 0,
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
                    MinHoldPass = true,
                    OrderFlowAvailable = false,
                    DailyRealizedPnl = 0,
                    EntriesToday = 0,
                    BarsSinceLastEntry = int.MaxValue,
                    BarsSincePositionEntry = int.MaxValue,
                    DailyLockActive = false,
                    DailyLockReason = string.Empty
                };
            }
        }

        private struct SignalSnapshot
        {
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
        }
    }
}
