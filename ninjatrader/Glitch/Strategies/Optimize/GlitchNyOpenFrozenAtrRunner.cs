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
    public class GlitchNyOpenFrozenAtrRunner : Strategy
    {
        private const string LongTp1Signal = "NYO_L_TP1";
        private const string LongTp2Signal = "NYO_L_TP2";
        private const string LongRunnerSignal = "NYO_L_RUN";
        private const string ShortTp1Signal = "NYO_S_TP1";
        private const string ShortTp2Signal = "NYO_S_TP2";
        private const string ShortRunnerSignal = "NYO_S_RUN";

        private ATR _atr;
        private EMA _emaFast;
        private EMA _emaSlow;
        private ADX _adx;
        private SMA _volumeSma;

        private double _tickSize;
        private int _openingRangeEndTime;
        private bool _openingRangeLocked;
        private double _openingRangeHigh;
        private double _openingRangeLow;

        private int _sessionTrades;
        private bool _entrySubmitted;
        private bool _countedCurrentTrade;

        private bool _trailInitialized;
        private bool _activeLong;
        private double _frozenAtrPoints;
        private double _frozenStopPoints;
        private double _frozenTrailPoints;
        private double _frozenTp1Points;
        private double _frozenTp2Points;
        private double _entryPrice;
        private double _activeStopPrice;
        private double _extremePriceSinceEntry;
        private string _pendingEntryReason;

        private TimeZoneInfo _appTz;
        private TimeZoneInfo _tplTz;
        private DateTime _currentTplDate;

        private StringBuilder _gateAuditBuffer;
        private int _gateAuditBufferedRows;
        private string _gateAuditFilePathResolved;
        private bool _gateAuditHeaderWritten;

        private StringBuilder _tradeTapeBuffer;
        private int _tradeTapeBufferedRows;
        private string _tradeTapeFilePathResolved;
        private bool _tradeTapeHeaderWritten;
        private int _lastExportedTradeIndex;
        private int _lastEvaluatedSignalBar;
        private bool _forceFlatSubmitted;

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Entry Start Time", Description = "HHmmss in exchange/session template timezone.", Order = 1, GroupName = "Window")]
        public int EntryStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Entry End Time", Description = "HHmmss in exchange/session template timezone.", Order = 2, GroupName = "Window")]
        public int EntryEndTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, 120)]
        [Display(Name = "Opening Range Minutes", Order = 3, GroupName = "Window")]
        public int OpeningRangeMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Max Trades Per Day", Order = 4, GroupName = "Window")]
        public int MaxTradesPerSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Strict Submit Time Gate", Description = "Require current submit time to also be within entry window.", Order = 5, GroupName = "Window")]
        public bool StrictSubmitTimeGate { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Force Flat At Window End", Description = "Exit open position when exchange time passes Entry End Time.", Order = 6, GroupName = "Window")]
        public bool ForceFlatAtWindowEnd { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "ATR Period", Order = 1, GroupName = "Signal")]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "Fast EMA Period", Order = 2, GroupName = "Signal")]
        public int FastEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(3, 300)]
        [Display(Name = "Slow EMA Period", Order = 3, GroupName = "Signal")]
        public int SlowEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "ADX Period", Order = 4, GroupName = "Signal")]
        public int AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Min ADX", Order = 5, GroupName = "Signal")]
        public double MinAdx { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "Volume SMA Period", Order = 6, GroupName = "Signal")]
        public int VolumeSmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Volume Filter", Order = 7, GroupName = "Signal")]
        public bool UseVolumeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Min Volume Factor", Description = "Bar volume must be >= factor * Volume SMA.", Order = 8, GroupName = "Signal")]
        public double MinVolumeFactor { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 2.0)]
        [Display(Name = "Breakout Buffer ATR Mult", Order = 9, GroupName = "Signal")]
        public double BreakoutBufferAtrMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 2.0)]
        [Display(Name = "Min Body ATR Mult", Order = 10, GroupName = "Signal")]
        public double MinBodyAtrMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Max Counter Wick Fraction", Description = "Max opposite wick / total bar range.", Order = 11, GroupName = "Signal")]
        public double MaxCounterWickFraction { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Min OR Width ATR Mult", Description = "Skip if opening range is too narrow.", Order = 12, GroupName = "Signal")]
        public double MinOrWidthAtrMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20.0)]
        [Display(Name = "Max OR Width ATR Mult", Description = "Skip if opening range is too wide.", Order = 13, GroupName = "Signal")]
        public double MaxOrWidthAtrMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Stop ATR Mult", Order = 1, GroupName = "Risk")]
        public double StopAtrMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Trail ATR Mult", Order = 2, GroupName = "Risk")]
        public double TrailAtrMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "TP1 ATR Mult", Order = 3, GroupName = "Risk")]
        public double Tp1AtrMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 20.0)]
        [Display(Name = "TP2 ATR Mult", Order = 4, GroupName = "Risk")]
        public double Tp2AtrMult { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use TP2 As Runner", Order = 5, GroupName = "Risk")]
        public bool UseTp2AsRunner { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 2000.0)]
        [Display(Name = "TP2 Runner Cap Points", Description = "0 = no TP cap, runner exits only by trailing stop.", Order = 6, GroupName = "Risk")]
        public double Tp2RunnerCapPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Stop Points", Order = 7, GroupName = "Risk")]
        public double MinStopPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Trail Points", Order = 8, GroupName = "Risk")]
        public double MinTrailPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "TP1 Quantity", Order = 9, GroupName = "Risk")]
        public int Tp1Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "TP2 Quantity", Order = 10, GroupName = "Risk")]
        public int Tp2Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Runner Quantity", Order = 11, GroupName = "Risk")]
        public int RunnerQuantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Gate Audit Export", Order = 1, GroupName = "Diagnostics")]
        public bool EnableGateAuditExport { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Gate Audit Export Relative Path", Order = 2, GroupName = "Diagnostics")]
        public string GateAuditExportRelativePath { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Gate Audit Flush Every N Rows", Order = 3, GroupName = "Diagnostics")]
        public int GateAuditFlushEveryNRows { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Audit Only Window Bars", Order = 4, GroupName = "Diagnostics")]
        public bool AuditOnlyWindowBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Trade Tape Export", Order = 5, GroupName = "Diagnostics")]
        public bool EnableTradeTapeExport { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Tape Export Relative Path", Order = 6, GroupName = "Diagnostics")]
        public string TradeTapeExportRelativePath { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Trade Tape Flush Every N Rows", Order = 7, GroupName = "Diagnostics")]
        public int TradeTapeFlushEveryNRows { get; set; }

        protected override void OnStateChange()
        {
            try
            {
                if (State == State.SetDefaults)
                {
                    Name = "GlitchNyOpenFrozenAtrRunner";
                    Description = "NY-open breakout model with frozen ATR geometry, split exits, ratcheting trailing stop, and exchange-time gate audit.";
                    Calculate = Calculate.OnEachTick;
                    EntriesPerDirection = 3;
                    EntryHandling = EntryHandling.UniqueEntries;
                    IsExitOnSessionCloseStrategy = true;
                    ExitOnSessionCloseSeconds = 30;
                    IsFillLimitOnTouch = false;
                    MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                    OrderFillResolution = OrderFillResolution.High;
                    Slippage = 0;
                    StartBehavior = StartBehavior.WaitUntilFlat;
                    TimeInForce = TimeInForce.Gtc;
                    TraceOrders = false;
                    RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                    StopTargetHandling = StopTargetHandling.PerEntryExecution;
                    BarsRequiredToTrade = 30;
                    IsInstantiatedOnEachOptimizationIteration = false;

                    EntryStartTime = 83000;
                    EntryEndTime = 110000;
                    OpeningRangeMinutes = 15;
                    MaxTradesPerSession = 1;
                    StrictSubmitTimeGate = true;
                    ForceFlatAtWindowEnd = true;

                    AtrPeriod = 14;
                    FastEmaPeriod = 21;
                    SlowEmaPeriod = 50;
                    AdxPeriod = 14;
                    MinAdx = 20.0;
                    VolumeSmaPeriod = 20;
                    UseVolumeFilter = true;
                    MinVolumeFactor = 1.0;
                    BreakoutBufferAtrMult = 0.10;
                    MinBodyAtrMult = 0.20;
                    MaxCounterWickFraction = 0.40;
                    MinOrWidthAtrMult = 0.50;
                    MaxOrWidthAtrMult = 4.00;

                    StopAtrMult = 1.50;
                    TrailAtrMult = 1.50;
                    Tp1AtrMult = 0.80;
                    Tp2AtrMult = 1.50;
                    UseTp2AsRunner = true;
                    Tp2RunnerCapPoints = 400.0;
                    MinStopPoints = 4.0;
                    MinTrailPoints = 4.0;
                    Tp1Quantity = 1;
                    Tp2Quantity = 1;
                    RunnerQuantity = 0;

                    EnableGateAuditExport = true;
                    GateAuditExportRelativePath = @"Glitch\glitch-nyopen-gate-audit-v2.csv";
                    GateAuditFlushEveryNRows = 200;
                    AuditOnlyWindowBars = false;
                    EnableTradeTapeExport = true;
                    TradeTapeExportRelativePath = @"Glitch\glitch-nyopen-trades-v2.csv";
                    TradeTapeFlushEveryNRows = 20;
                }
                else if (State == State.DataLoaded)
                {
                    _atr = ATR(AtrPeriod);
                    _emaFast = EMA(FastEmaPeriod);
                    _emaSlow = EMA(SlowEmaPeriod);
                    _adx = ADX(AdxPeriod);
                    _volumeSma = SMA(Volume, VolumeSmaPeriod);

                    _tickSize = Instrument != null && Instrument.MasterInstrument != null && Instrument.MasterInstrument.TickSize > 0
                        ? Instrument.MasterInstrument.TickSize
                        : 0.25;

                    _appTz = TimeZoneInfo.Local;
                    _tplTz = (Bars != null && Bars.TradingHours != null && Bars.TradingHours.TimeZoneInfo != null)
                        ? Bars.TradingHours.TimeZoneInfo
                        : _appTz;
                    _currentTplDate = DateTime.MinValue;

                    _gateAuditFilePathResolved = ResolveExportPath(GateAuditExportRelativePath, "glitch-nyopen-gate-audit-v2.csv");
                    _tradeTapeFilePathResolved = ResolveExportPath(TradeTapeExportRelativePath, "glitch-nyopen-trades-v2.csv");
                    _gateAuditBuffer = new StringBuilder(4096);
                    _tradeTapeBuffer = new StringBuilder(4096);
                    _lastExportedTradeIndex = SystemPerformance != null && SystemPerformance.AllTrades != null
                        ? SystemPerformance.AllTrades.Count
                        : 0;
                    _lastEvaluatedSignalBar = -1;

                    ResetSessionState();
                    ResetTradeState();
                }
                else if (State == State.Terminated)
                {
                    FlushGateAuditBuffer();
                    FlushTradeTapeBuffer();
                }
            }
            catch (Exception ex)
            {
                Log("GlitchNyOpenFrozenAtrRunner OnStateChange error: " + ex, LogLevel.Error);
                throw;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            DateTime nowTpl = ConvertAppToTpl(Time[0]);
            int nowTime = ToTimeHHmmss(nowTpl);
            ResetDayIfNeeded(nowTpl.Date);

            if (CurrentBar < BarsRequiredToTrade + 2)
                return;

            ExportNewClosedTrades();

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (_trailInitialized)
                    ResetTradeState();
            }
            else
            {
                UpdateRatchetingStop();

                if (ForceFlatAtWindowEnd && IsAfterEntryWindowEnd(nowTime) && !_forceFlatSubmitted)
                    SubmitForceFlatExits();
            }

            // Evaluate signal logic once per bar across historical/playback/live.
            if (CurrentBar == _lastEvaluatedSignalBar)
                return;
            if (State != State.Historical && !IsFirstTickOfBar)
                return;
            _lastEvaluatedSignalBar = CurrentBar;

            DateTime signalBarTimeApp = Time[1];
            DateTime signalBarTimeTpl = ConvertAppToTpl(signalBarTimeApp);
            int signalTime = ToTimeHHmmss(signalBarTimeTpl);
            UpdateOpeningRange(High[1], Low[1], signalTime);
            EvaluateAndMaybeEnterFromClosedBar(signalBarTimeApp, signalBarTimeTpl, signalTime, nowTime);
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
            if (execution == null || execution.Order == null)
                return;

            if (execution.Order.OrderState != OrderState.Filled)
                return;

            string fromSignal = execution.Order.Name ?? string.Empty;
            if (!IsEntrySignal(fromSignal))
                return;

            if (!_countedCurrentTrade)
            {
                _sessionTrades++;
                _countedCurrentTrade = true;
            }

            if (Position.MarketPosition == MarketPosition.Flat)
                return;

            if (!_trailInitialized)
            {
                _entryPrice = Position.AveragePrice > 0 ? Position.AveragePrice : price;
                _activeLong = Position.MarketPosition == MarketPosition.Long;
                _extremePriceSinceEntry = _entryPrice;
                _activeStopPrice = _activeLong
                    ? RoundToTick(_entryPrice - _frozenStopPoints)
                    : RoundToTick(_entryPrice + _frozenStopPoints);

                _trailInitialized = true;
                ApplyActiveStop();
                return;
            }

            _entryPrice = Position.AveragePrice > 0 ? Position.AveragePrice : _entryPrice;
        }

        protected override void OnOrderUpdate(
            Order order,
            double limitPrice,
            double stopPrice,
            int quantity,
            int filled,
            double averageFillPrice,
            OrderState orderState,
            DateTime time,
            ErrorCode error,
            string comment)
        {
            if (order == null)
                return;

            if (!IsEntrySignal(order.Name ?? string.Empty))
                return;

            if (orderState == OrderState.Rejected || orderState == OrderState.Cancelled)
            {
                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    _entrySubmitted = false;
                    _pendingEntryReason = string.Empty;
                }
            }
        }

        private void EvaluateAndMaybeEnterFromClosedBar(DateTime signalBarTimeApp, DateTime signalBarTimeTpl, int signalTime, int submitTime)
        {
            double atr = Math.Max(_tickSize, _atr[1]);
            bool inSignalWindow = IsWithinEntryWindow(signalTime);
            bool inSubmitWindow = IsWithinEntryWindow(submitTime);
            bool inWindow = StrictSubmitTimeGate ? (inSignalWindow && inSubmitWindow) : inSignalWindow;
            if (AuditOnlyWindowBars && !inSignalWindow && !inSubmitWindow)
                return;

            bool positionFlat = Position.MarketPosition == MarketPosition.Flat;
            int maxTrades = Math.Max(1, MaxTradesPerSession);
            bool passTradeCap = _sessionTrades < maxTrades;
            bool passEntryPending = !_entrySubmitted;
            bool passOrLocked = _openingRangeLocked;

            double rangeWidth = passOrLocked ? Math.Max(0.0, _openingRangeHigh - _openingRangeLow) : 0.0;
            double minRange = atr * Math.Max(0.0, MinOrWidthAtrMult);
            double maxRange = atr * Math.Max(MinOrWidthAtrMult + 0.1, MaxOrWidthAtrMult);
            bool passOrWidth = passOrLocked && rangeWidth >= minRange && rangeWidth <= maxRange && rangeWidth > _tickSize;

            double adxValue = _adx[1];
            bool adxPass = adxValue >= MinAdx;

            double volumeValue = Volume[1];
            double volumeSma = _volumeSma[1];
            double requiredVol = Math.Max(1.0, volumeSma * Math.Max(0.1, MinVolumeFactor));
            bool volumePass = !UseVolumeFilter || volumeValue >= requiredVol;

            bool trendUp = _emaFast[1] > _emaSlow[1] && _emaFast[1] >= _emaFast[2];
            bool trendDown = _emaFast[1] < _emaSlow[1] && _emaFast[1] <= _emaFast[2];
            bool trendPass = trendUp || trendDown;

            double barRange = Math.Max(_tickSize, High[1] - Low[1]);
            double body = Math.Abs(Close[1] - Open[1]);
            double bodyMin = atr * Math.Max(0.0, MinBodyAtrMult);
            bool bodyPass = body >= bodyMin;

            double upperWick = Math.Max(0.0, High[1] - Math.Max(Open[1], Close[1]));
            double lowerWick = Math.Max(0.0, Math.Min(Open[1], Close[1]) - Low[1]);
            double counterWickLong = upperWick / barRange;
            double counterWickShort = lowerWick / barRange;

            double breakoutBuffer = atr * Math.Max(0.0, BreakoutBufferAtrMult);
            bool longBreak = trendUp && bodyPass &&
                             counterWickLong <= MaxCounterWickFraction &&
                             Close[1] > (_openingRangeHigh + breakoutBuffer) &&
                             High[1] > _openingRangeHigh;

            bool shortBreak = trendDown && bodyPass &&
                              counterWickShort <= MaxCounterWickFraction &&
                              Close[1] < (_openingRangeLow - breakoutBuffer) &&
                              Low[1] < _openingRangeLow;

            bool uniqueDirection = longBreak ^ shortBreak;

            string blockReason = string.Empty;
            if (!inSignalWindow)
                blockReason = "OutsideSignalWindow";
            else if (StrictSubmitTimeGate && !inSubmitWindow)
                blockReason = "OutsideSubmitWindow";
            else if (!positionFlat)
                blockReason = "InPosition";
            else if (!passTradeCap)
                blockReason = "DailyTradeCap";
            else if (!passEntryPending)
                blockReason = "EntryPending";
            else if (!passOrLocked)
                blockReason = "OpeningRangeNotLocked";
            else if (!passOrWidth)
                blockReason = "OpeningRangeWidth";
            else if (!adxPass)
                blockReason = "ADX";
            else if (!volumePass)
                blockReason = "Volume";
            else if (!trendPass)
                blockReason = "Trend";
            else if (!bodyPass)
                blockReason = "Body";
            else if (!uniqueDirection)
                blockReason = "NoUnambiguousBreak";

            string decision = "BLOCK";
            bool didSubmit = false;
            if (string.IsNullOrEmpty(blockReason))
            {
                bool isLong = longBreak;
                _pendingEntryReason = isLong ? "ORB_LONG_BREAK" : "ORB_SHORT_BREAK";
                SubmitSplitEntry(isLong, atr);
                decision = isLong ? "ENTER_LONG" : "ENTER_SHORT";
                didSubmit = true;
                blockReason = "PASS";
            }
            else
            {
                _pendingEntryReason = string.Empty;
            }

            ExportGateAuditRow(
                signalBarTimeApp,
                signalBarTimeTpl,
                signalTime,
                submitTime,
                atr,
                adxValue,
                volumeValue,
                volumeSma,
                requiredVol,
                rangeWidth,
                minRange,
                maxRange,
                trendUp,
                trendDown,
                trendPass,
                body,
                bodyMin,
                bodyPass,
                counterWickLong,
                counterWickShort,
                longBreak,
                shortBreak,
                uniqueDirection,
                inSignalWindow,
                inSubmitWindow,
                inWindow,
                positionFlat,
                passTradeCap,
                passEntryPending,
                passOrLocked,
                passOrWidth,
                adxPass,
                volumePass,
                decision,
                blockReason,
                didSubmit);
        }

        private void SubmitSplitEntry(bool isLong, double frozenAtr)
        {
            int tp1Qty = Math.Max(0, Tp1Quantity);
            int tp2Qty = Math.Max(0, Tp2Quantity);
            int runQty = Math.Max(0, RunnerQuantity);
            if (tp1Qty + tp2Qty + runQty <= 0)
                return;

            _frozenAtrPoints = Math.Max(_tickSize, frozenAtr);
            _frozenStopPoints = Math.Max(Math.Max(_tickSize, MinStopPoints), _frozenAtrPoints * Math.Max(0.1, StopAtrMult));
            _frozenTrailPoints = Math.Max(Math.Max(_tickSize, MinTrailPoints), _frozenAtrPoints * Math.Max(0.1, TrailAtrMult));
            _frozenTp1Points = _frozenAtrPoints * Math.Max(0.0, Tp1AtrMult);
            _frozenTp2Points = _frozenAtrPoints * Math.Max(0.0, Tp2AtrMult);
            int tp1Ticks = Tp1AtrMult > 0 ? PointsToTicks(_frozenTp1Points) : 0;
            int tp2Ticks = Tp2AtrMult > 0 ? PointsToTicks(_frozenTp2Points) : 0;
            int tp2RunnerCapTicks = UseTp2AsRunner && Tp2RunnerCapPoints > 0
                ? PointsToTicks(Tp2RunnerCapPoints)
                : 0;

            _entryPrice = Close[0];
            _activeLong = isLong;
            _activeStopPrice = isLong
                ? RoundToTick(_entryPrice - _frozenStopPoints)
                : RoundToTick(_entryPrice + _frozenStopPoints);
            _extremePriceSinceEntry = _entryPrice;
            _trailInitialized = false;
            _entrySubmitted = true;
            _forceFlatSubmitted = false;

            if (isLong)
            {
                if (tp1Qty > 0)
                {
                    SetStopLoss(LongTp1Signal, CalculationMode.Price, _activeStopPrice, false);
                    if (tp1Ticks > 0)
                        SetProfitTarget(LongTp1Signal, CalculationMode.Ticks, tp1Ticks);
                    EnterLong(tp1Qty, LongTp1Signal);
                }

                if (tp2Qty > 0)
                {
                    SetStopLoss(LongTp2Signal, CalculationMode.Price, _activeStopPrice, false);
                    if (UseTp2AsRunner)
                    {
                        if (tp2RunnerCapTicks > 0)
                            SetProfitTarget(LongTp2Signal, CalculationMode.Ticks, tp2RunnerCapTicks);
                    }
                    else if (tp2Ticks > 0)
                    {
                        SetProfitTarget(LongTp2Signal, CalculationMode.Ticks, tp2Ticks);
                    }
                    EnterLong(tp2Qty, LongTp2Signal);
                }

                if (runQty > 0)
                {
                    SetStopLoss(LongRunnerSignal, CalculationMode.Price, _activeStopPrice, false);
                    EnterLong(runQty, LongRunnerSignal);
                }

                return;
            }

            if (tp1Qty > 0)
            {
                SetStopLoss(ShortTp1Signal, CalculationMode.Price, _activeStopPrice, false);
                if (tp1Ticks > 0)
                    SetProfitTarget(ShortTp1Signal, CalculationMode.Ticks, tp1Ticks);
                EnterShort(tp1Qty, ShortTp1Signal);
            }

            if (tp2Qty > 0)
            {
                SetStopLoss(ShortTp2Signal, CalculationMode.Price, _activeStopPrice, false);
                if (UseTp2AsRunner)
                {
                    if (tp2RunnerCapTicks > 0)
                        SetProfitTarget(ShortTp2Signal, CalculationMode.Ticks, tp2RunnerCapTicks);
                }
                else if (tp2Ticks > 0)
                {
                    SetProfitTarget(ShortTp2Signal, CalculationMode.Ticks, tp2Ticks);
                }
                EnterShort(tp2Qty, ShortTp2Signal);
            }

            if (runQty > 0)
            {
                SetStopLoss(ShortRunnerSignal, CalculationMode.Price, _activeStopPrice, false);
                EnterShort(runQty, ShortRunnerSignal);
            }
        }

        private void UpdateRatchetingStop()
        {
            if (!_trailInitialized)
            {
                if (Position.MarketPosition == MarketPosition.Flat)
                    return;

                _activeLong = Position.MarketPosition == MarketPosition.Long;
                _entryPrice = Position.AveragePrice > 0 ? Position.AveragePrice : _entryPrice;
                _extremePriceSinceEntry = _entryPrice;
                _activeStopPrice = _activeLong
                    ? RoundToTick(_entryPrice - _frozenStopPoints)
                    : RoundToTick(_entryPrice + _frozenStopPoints);
                _trailInitialized = true;
                ApplyActiveStop();
                return;
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ResetTradeState();
                return;
            }

            if (_activeLong && Position.MarketPosition != MarketPosition.Long)
                return;
            if (!_activeLong && Position.MarketPosition != MarketPosition.Short)
                return;

            if (_activeLong)
            {
                _extremePriceSinceEntry = Math.Max(_extremePriceSinceEntry, High[0]);
                double candidateStop = RoundToTick(_extremePriceSinceEntry - _frozenTrailPoints);
                if (candidateStop > (_activeStopPrice + (_tickSize * 0.25)))
                {
                    _activeStopPrice = candidateStop;
                    ApplyActiveStop();
                }

                return;
            }

            _extremePriceSinceEntry = Math.Min(_extremePriceSinceEntry, Low[0]);
            double shortCandidate = RoundToTick(_extremePriceSinceEntry + _frozenTrailPoints);
            if (shortCandidate < (_activeStopPrice - (_tickSize * 0.25)))
            {
                _activeStopPrice = shortCandidate;
                ApplyActiveStop();
            }
        }

        private void ApplyActiveStop()
        {
            if (_activeLong)
            {
                SetStopLoss(LongTp1Signal, CalculationMode.Price, _activeStopPrice, false);
                SetStopLoss(LongTp2Signal, CalculationMode.Price, _activeStopPrice, false);
                SetStopLoss(LongRunnerSignal, CalculationMode.Price, _activeStopPrice, false);
                return;
            }

            SetStopLoss(ShortTp1Signal, CalculationMode.Price, _activeStopPrice, false);
            SetStopLoss(ShortTp2Signal, CalculationMode.Price, _activeStopPrice, false);
            SetStopLoss(ShortRunnerSignal, CalculationMode.Price, _activeStopPrice, false);
        }

        private void UpdateOpeningRange(double barHigh, double barLow, int barTime)
        {
            if (_openingRangeLocked)
                return;

            if (barTime < EntryStartTime)
                return;

            if (barTime < _openingRangeEndTime)
            {
                _openingRangeHigh = Math.Max(_openingRangeHigh, barHigh);
                _openingRangeLow = Math.Min(_openingRangeLow, barLow);
                return;
            }

            if (_openingRangeHigh > _openingRangeLow)
                _openingRangeLocked = true;
        }

        private bool IsWithinEntryWindow(int hhmmss)
        {
            if (EntryStartTime <= EntryEndTime)
                return hhmmss >= EntryStartTime && hhmmss <= EntryEndTime;
            return hhmmss >= EntryStartTime || hhmmss <= EntryEndTime;
        }

        private bool IsAfterEntryWindowEnd(int hhmmss)
        {
            if (EntryStartTime <= EntryEndTime)
                return hhmmss > EntryEndTime;
            return hhmmss > EntryEndTime && hhmmss < EntryStartTime;
        }

        private void SubmitForceFlatExits()
        {
            if (Position.MarketPosition == MarketPosition.Long)
            {
                ExitLong("TIME_FLAT_TP1", LongTp1Signal);
                ExitLong("TIME_FLAT_TP2", LongTp2Signal);
                ExitLong("TIME_FLAT_RUN", LongRunnerSignal);
                _forceFlatSubmitted = true;
                return;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                ExitShort("TIME_FLAT_TP1", ShortTp1Signal);
                ExitShort("TIME_FLAT_TP2", ShortTp2Signal);
                ExitShort("TIME_FLAT_RUN", ShortRunnerSignal);
                _forceFlatSubmitted = true;
            }
        }

        private void ResetSessionState()
        {
            _openingRangeEndTime = AddMinutesToHhmmss(EntryStartTime, Math.Max(1, OpeningRangeMinutes));
            _openingRangeLocked = false;
            _openingRangeHigh = double.MinValue;
            _openingRangeLow = double.MaxValue;
            _sessionTrades = 0;
            _entrySubmitted = false;
            _countedCurrentTrade = false;
            _pendingEntryReason = string.Empty;
            ResetTradeState();
        }

        private void ResetTradeState()
        {
            _trailInitialized = false;
            _activeLong = false;
            _frozenAtrPoints = 0;
            _frozenStopPoints = 0;
            _frozenTrailPoints = 0;
            _frozenTp1Points = 0;
            _frozenTp2Points = 0;
            _entryPrice = 0;
            _activeStopPrice = 0;
            _extremePriceSinceEntry = 0;
            _entrySubmitted = false;
            _countedCurrentTrade = false;
            _pendingEntryReason = string.Empty;
            _forceFlatSubmitted = false;
        }

        private void ResetDayIfNeeded(DateTime tplDate)
        {
            if (_currentTplDate == tplDate)
                return;

            _currentTplDate = tplDate;
            ResetSessionState();
        }

        private bool IsEntrySignal(string signalName)
        {
            return string.Equals(signalName, LongTp1Signal, StringComparison.Ordinal) ||
                   string.Equals(signalName, LongTp2Signal, StringComparison.Ordinal) ||
                   string.Equals(signalName, LongRunnerSignal, StringComparison.Ordinal) ||
                   string.Equals(signalName, ShortTp1Signal, StringComparison.Ordinal) ||
                   string.Equals(signalName, ShortTp2Signal, StringComparison.Ordinal) ||
                   string.Equals(signalName, ShortRunnerSignal, StringComparison.Ordinal);
        }

        private int AddMinutesToHhmmss(int hhmmss, int minutes)
        {
            int hh = Math.Max(0, Math.Min(23, hhmmss / 10000));
            int mm = Math.Max(0, Math.Min(59, (hhmmss / 100) % 100));
            int ss = Math.Max(0, Math.Min(59, hhmmss % 100));

            int totalMinutes = (hh * 60) + mm + Math.Max(0, minutes);
            int wrappedMinutes = ((totalMinutes % (24 * 60)) + (24 * 60)) % (24 * 60);

            int outH = wrappedMinutes / 60;
            int outM = wrappedMinutes % 60;
            return (outH * 10000) + (outM * 100) + ss;
        }

        private DateTime ConvertAppToTpl(DateTime tApp)
        {
            try
            {
                DateTime t = DateTime.SpecifyKind(tApp, DateTimeKind.Unspecified);
                return TimeZoneInfo.ConvertTime(t, _appTz, _tplTz);
            }
            catch
            {
                return tApp;
            }
        }

        private int ToTimeHHmmss(DateTime t)
        {
            return (t.Hour * 10000) + (t.Minute * 100) + t.Second;
        }

        private string ResolveExportPath(string relativePath, string fallbackFileName)
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string userPath = relativePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userPath))
                userPath = fallbackFileName;
            if (Path.IsPathRooted(userPath))
                return userPath;
            return Path.Combine(docs, "NinjaTrader 8", userPath);
        }

        private void ExportNewClosedTrades()
        {
            if (!EnableTradeTapeExport || string.IsNullOrEmpty(_tradeTapeFilePathResolved))
                return;

            if (SystemPerformance == null || SystemPerformance.AllTrades == null)
                return;

            int total = SystemPerformance.AllTrades.Count;
            if (total <= _lastExportedTradeIndex)
                return;

            if (_tradeTapeBuffer == null)
                _tradeTapeBuffer = new StringBuilder(4096);

            if (!_tradeTapeHeaderWritten)
            {
                _tradeTapeBuffer.AppendLine("entry_time_app,entry_time_tpl,exit_time_app,exit_time_tpl,entry_name,direction,entry_price,exit_price,quantity,pnl_currency");
                _tradeTapeHeaderWritten = true;
            }

            for (int i = _lastExportedTradeIndex; i < total; i++)
            {
                Trade t = SystemPerformance.AllTrades[i];
                if (t == null || t.Entry == null || t.Exit == null)
                    continue;

                DateTime entryApp = t.Entry.Time;
                DateTime exitApp = t.Exit.Time;
                DateTime entryTpl = ConvertAppToTpl(entryApp);
                DateTime exitTpl = ConvertAppToTpl(exitApp);
                string direction = t.Entry.MarketPosition == MarketPosition.Short ? "SHORT" : "LONG";

                StringBuilder row = new StringBuilder(256);
                AppendCsv(row, entryApp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                AppendCsv(row, entryTpl.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                AppendCsv(row, exitApp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                AppendCsv(row, exitTpl.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                AppendCsv(row, t.Entry.Name ?? string.Empty);
                AppendCsv(row, direction);
                AppendCsv(row, t.Entry.Price.ToString("F2", CultureInfo.InvariantCulture));
                AppendCsv(row, t.Exit.Price.ToString("F2", CultureInfo.InvariantCulture));
                AppendCsv(row, t.Quantity.ToString(CultureInfo.InvariantCulture));
                AppendCsv(row, t.ProfitCurrency.ToString("F2", CultureInfo.InvariantCulture));
                _tradeTapeBuffer.AppendLine(row.ToString());
                _tradeTapeBufferedRows++;
            }

            _lastExportedTradeIndex = total;
            if (_tradeTapeBufferedRows >= Math.Max(1, TradeTapeFlushEveryNRows))
                FlushTradeTapeBuffer();
        }

        private void ExportGateAuditRow(
            DateTime signalBarTimeApp,
            DateTime signalBarTimeTpl,
            int signalTime,
            int submitTime,
            double atr,
            double adxValue,
            double volumeValue,
            double volumeSma,
            double requiredVol,
            double rangeWidth,
            double minRange,
            double maxRange,
            bool trendUp,
            bool trendDown,
            bool trendPass,
            double body,
            double bodyMin,
            bool bodyPass,
            double counterWickLong,
            double counterWickShort,
            bool longBreak,
            bool shortBreak,
            bool uniqueDirection,
            bool inSignalWindow,
            bool inSubmitWindow,
            bool inWindow,
            bool positionFlat,
            bool passTradeCap,
            bool passEntryPending,
            bool passOrLocked,
            bool passOrWidth,
            bool adxPass,
            bool volumePass,
            string decision,
            string blockReason,
            bool didSubmit)
        {
            if (!EnableGateAuditExport || string.IsNullOrEmpty(_gateAuditFilePathResolved))
                return;

            if (_gateAuditBuffer == null)
                _gateAuditBuffer = new StringBuilder(4096);

            if (!_gateAuditHeaderWritten)
            {
                _gateAuditBuffer.AppendLine("bar_time_app,bar_time_tpl,bar_index,signal_time_tpl,submit_time_tpl,signal_in_window,submit_in_window,pass_window,position,position_flat,trades_today,max_trades,pass_trade_cap,entry_submitted,pass_entry_pending,or_locked,or_high,or_low,or_width,or_min,or_max,pass_or_width,atr,adx,pass_adx,volume,volume_sma,volume_required,pass_volume,trend_up,trend_down,pass_trend,body,body_min,pass_body,counter_wick_long,counter_wick_short,max_counter_wick,long_break,short_break,unique_direction,decision,block_reason,did_submit,entry_reason,frozen_atr,frozen_stop,frozen_trail,tp1,tp2,strict_submit_gate,force_flat_window_end");
                _gateAuditHeaderWritten = true;
            }

            StringBuilder row = new StringBuilder(768);
            AppendCsv(row, signalBarTimeApp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            AppendCsv(row, signalBarTimeTpl.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            AppendCsv(row, CurrentBar.ToString(CultureInfo.InvariantCulture));
            AppendCsv(row, signalTime.ToString(CultureInfo.InvariantCulture));
            AppendCsv(row, submitTime.ToString(CultureInfo.InvariantCulture));
            AppendCsv(row, inSignalWindow ? "1" : "0");
            AppendCsv(row, inSubmitWindow ? "1" : "0");
            AppendCsv(row, inWindow ? "1" : "0");
            AppendCsv(row, Position.MarketPosition.ToString());
            AppendCsv(row, positionFlat ? "1" : "0");
            AppendCsv(row, _sessionTrades.ToString(CultureInfo.InvariantCulture));
            AppendCsv(row, Math.Max(1, MaxTradesPerSession).ToString(CultureInfo.InvariantCulture));
            AppendCsv(row, passTradeCap ? "1" : "0");
            AppendCsv(row, _entrySubmitted ? "1" : "0");
            AppendCsv(row, passEntryPending ? "1" : "0");
            AppendCsv(row, passOrLocked ? "1" : "0");
            AppendCsv(row, passOrLocked ? _openingRangeHigh.ToString("F2", CultureInfo.InvariantCulture) : string.Empty);
            AppendCsv(row, passOrLocked ? _openingRangeLow.ToString("F2", CultureInfo.InvariantCulture) : string.Empty);
            AppendCsv(row, passOrLocked ? rangeWidth.ToString("F2", CultureInfo.InvariantCulture) : string.Empty);
            AppendCsv(row, minRange.ToString("F2", CultureInfo.InvariantCulture));
            AppendCsv(row, maxRange.ToString("F2", CultureInfo.InvariantCulture));
            AppendCsv(row, passOrWidth ? "1" : "0");
            AppendCsv(row, atr.ToString("F4", CultureInfo.InvariantCulture));
            AppendCsv(row, adxValue.ToString("F2", CultureInfo.InvariantCulture));
            AppendCsv(row, adxPass ? "1" : "0");
            AppendCsv(row, volumeValue.ToString("F0", CultureInfo.InvariantCulture));
            AppendCsv(row, volumeSma.ToString("F0", CultureInfo.InvariantCulture));
            AppendCsv(row, requiredVol.ToString("F0", CultureInfo.InvariantCulture));
            AppendCsv(row, volumePass ? "1" : "0");
            AppendCsv(row, trendUp ? "1" : "0");
            AppendCsv(row, trendDown ? "1" : "0");
            AppendCsv(row, trendPass ? "1" : "0");
            AppendCsv(row, body.ToString("F4", CultureInfo.InvariantCulture));
            AppendCsv(row, bodyMin.ToString("F4", CultureInfo.InvariantCulture));
            AppendCsv(row, bodyPass ? "1" : "0");
            AppendCsv(row, counterWickLong.ToString("F4", CultureInfo.InvariantCulture));
            AppendCsv(row, counterWickShort.ToString("F4", CultureInfo.InvariantCulture));
            AppendCsv(row, MaxCounterWickFraction.ToString("F4", CultureInfo.InvariantCulture));
            AppendCsv(row, longBreak ? "1" : "0");
            AppendCsv(row, shortBreak ? "1" : "0");
            AppendCsv(row, uniqueDirection ? "1" : "0");
            AppendCsv(row, decision ?? string.Empty);
            AppendCsv(row, blockReason ?? string.Empty);
            AppendCsv(row, didSubmit ? "1" : "0");
            AppendCsv(row, _pendingEntryReason ?? string.Empty);
            AppendCsv(row, _frozenAtrPoints.ToString("F4", CultureInfo.InvariantCulture));
            AppendCsv(row, _frozenStopPoints.ToString("F4", CultureInfo.InvariantCulture));
            AppendCsv(row, _frozenTrailPoints.ToString("F4", CultureInfo.InvariantCulture));
            AppendCsv(row, _frozenTp1Points.ToString("F4", CultureInfo.InvariantCulture));
            AppendCsv(row, _frozenTp2Points.ToString("F4", CultureInfo.InvariantCulture));
            AppendCsv(row, StrictSubmitTimeGate ? "1" : "0");
            AppendCsv(row, ForceFlatAtWindowEnd ? "1" : "0");

            _gateAuditBuffer.AppendLine(row.ToString());
            _gateAuditBufferedRows++;
            if (_gateAuditBufferedRows >= Math.Max(1, GateAuditFlushEveryNRows))
                FlushGateAuditBuffer();
        }

        private void FlushGateAuditBuffer()
        {
            if (_gateAuditBuffer == null || _gateAuditBufferedRows <= 0 || string.IsNullOrEmpty(_gateAuditFilePathResolved))
                return;

            try
            {
                string dir = Path.GetDirectoryName(_gateAuditFilePathResolved);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(_gateAuditFilePathResolved, _gateAuditBuffer.ToString());
                _gateAuditBuffer.Clear();
                _gateAuditBufferedRows = 0;
            }
            catch
            {
            }
        }

        private void FlushTradeTapeBuffer()
        {
            if (_tradeTapeBuffer == null || _tradeTapeBufferedRows <= 0 || string.IsNullOrEmpty(_tradeTapeFilePathResolved))
                return;

            try
            {
                string dir = Path.GetDirectoryName(_tradeTapeFilePathResolved);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(_tradeTapeFilePathResolved, _tradeTapeBuffer.ToString());
                _tradeTapeBuffer.Clear();
                _tradeTapeBufferedRows = 0;
            }
            catch
            {
            }
        }

        private static void AppendCsv(StringBuilder sb, string value)
        {
            if (sb.Length > 0)
                sb.Append(',');

            string v = value ?? string.Empty;
            bool needsQuotes = v.IndexOf(',') >= 0 || v.IndexOf('"') >= 0 || v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0;
            if (!needsQuotes)
            {
                sb.Append(v);
                return;
            }

            sb.Append('"');
            for (int i = 0; i < v.Length; i++)
            {
                char c = v[i];
                if (c == '"')
                    sb.Append("\"\"");
                else
                    sb.Append(c);
            }
            sb.Append('"');
        }

        private double RoundToTick(double price)
        {
            if (Instrument != null && Instrument.MasterInstrument != null)
                return Instrument.MasterInstrument.RoundToTickSize(price);

            return Math.Round(price / _tickSize, MidpointRounding.AwayFromZero) * _tickSize;
        }

        private int PointsToTicks(double points)
        {
            return Math.Max(1, (int)Math.Round(Math.Max(_tickSize, points) / _tickSize, MidpointRounding.AwayFromZero));
        }
    }
}
