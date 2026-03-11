#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class TrendshiftStrategy : Strategy
    {
        private const int ExecutionBarsInProgress = 1;

        public enum StopBandSideOption
        {
            OppositeBandEdge,
            SameBandEdge
        }

        public enum RiskValueUnit
        {
            Points,
            Ticks,
            Dollars
        }

        private const string GroupMarketStructure = "Market Structure";
        private const string GroupFilters = "Trade Filters";
        private const string GroupLongParameters = "Long Parameters";
        private const string GroupShortParameters = "Short Parameters";
        private const string GroupFramework = "Premium/Discount Framework";
        private const string GroupVisual = "Visuals";
        private const string GroupRisk = "Execution and Risk";
        private const string GroupDailyLimits = "Daily Limits";
        private const string GroupDiagnostics = "Diagnostics";

        private sealed class DiagnosticsSnapshot
        {
            public string EntrySignal;
            public string Direction;
            public DateTime SignalTime;
            public double SignalOpen;
            public double SignalHigh;
            public double SignalLow;
            public double SignalClose;
            public int SignalBar;
            public int SignalHour;
            public string SignalDayOfWeek;
            public double Atr;
            public double Adx;
            public double BreakDistancePoints;
            public double BreakDistanceAtr;
            public double CandleRangePoints;
            public double CandleBodyPoints;
            public double CandleBodyPctRange;
            public double StructLevel;
            public double BandLow;
            public double BandHigh;
            public double BandWidthPoints;
            public double BandWidthAtr;
            public double BandPosition;
            public bool IsValidBand;
            public bool StopEnabled;
            public double StopPrice;
            public double StopDistancePoints;
            public double StopDistanceAtr;
            public bool FlattenOpposite;
            public int PriorLossStreak;
            public int PriorStopStreak;
            public double PreDayRealizedCurrency;
            public int PreDayTradeCount;
            public int PreDayLossCount;
            public int PreDayStopCount;
            public bool PassedAdxFilter;
            public bool PassedTimeFilter;
            public int Regime;
            public int Quantity;
            public DateTime EntryTime;
            public double EntryPrice;
            public DateTime ExitTime;
            public double ExitPrice;
            public string ExitName;
            public string ExitType;
            public double ProfitPoints;
            public double ProfitCurrency;
        }

        private ATR _atr;
        private ADX _adx;
        private Order _activeStopOrder;
        private Order _activeProfitOrder;
        private string _activeEntrySignal = string.Empty;
        private string _activeStopSignal = string.Empty;
        private string _activeProfitSignal = string.Empty;
        private string _queuedEntrySignal = string.Empty;
        private MarketPosition _queuedEntryDirection = MarketPosition.Flat;
        private int _queuedEntryQuantity;
        private bool _queuedFlattenOpposite;
        private bool _queuedFlattenSubmitted;
        private bool _queuedStopEnabled;
        private double _queuedStopPrice;
        private string _submittedEntrySignal = string.Empty;
        private MarketPosition _submittedEntryDirection = MarketPosition.Flat;
        private bool _submittedStopEnabled;
        private double _submittedStopPrice;

        private double _lastSwingHigh;
        private double _lastSwingLow;
        private int _lastSwingHighBar;
        private int _lastSwingLowBar;

        private int _regime;
        private double _bandLow;
        private double _bandHigh;
        private double _structLevel;
        private int _lastShiftBar;
        private bool _isLastShiftBullish;
        private DateTime _currentTradeDate;
        private double _dailyStartCumProfit;
        private bool _dailyStateInitialized;
        private bool _dailyEntryLockLoss;
        private bool _dailyEntryLockProfit;
        private bool _queuedDailyLossFlatten;
        private bool _queuedDailyLossFlattenSubmitted;
        private double _activeStopPrice;
        private double _activeProfitPrice;
        private int _consecutiveLossCount;
        private int _consecutiveStopCount;
        private int _dailyTradeCount;
        private int _dailyLossCount;
        private int _dailyStopCount;
        private DiagnosticsSnapshot _queuedDiagnostics;
        private DiagnosticsSnapshot _submittedDiagnostics;
        private Dictionary<string, DiagnosticsSnapshot> _diagnosticsByEntrySignal;
        private string _diagnosticsRunLabelResolved = string.Empty;
        private bool _diagnosticsWriteFailed;
        private int _lastDiagnosticsExportedTradeIndex;

        private static readonly Brush BullShiftBrush = Brushes.Lime;
        private static readonly Brush BearShiftBrush = Brushes.Red;
        private static readonly Brush ShiftTextBrush = Brushes.White;
        private static readonly Brush DiscountBrush = CreateBrush(Color.FromRgb(50, 205, 50), 36);
        private static readonly Brush PremiumBrush = CreateBrush(Color.FromRgb(220, 20, 60), 36);

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "TrendshiftStrategy";
                Description = "Glitch Trend Shift Strategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                StartBehavior = StartBehavior.WaitUntilFlat;
                IsOverlay = true;
                IncludeCommission = true;
                Slippage = 1;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                BarsRequiredToTrade = 0;

                SwingLength = 5;
                UseAtrFilter = true;
                AtrLength = 45;
                LongBreakAtrMult = 3.0;
                ShortBreakAtrMult = 3.0;
                AdxLength = 10;
                EnableEntryHourFilter = false;
                EntryStartHour = 7;
                EntryEndHour = 21;
                EnableSundayBlock = false;

                EnableLongMinAdxFilter = true;
                LongMinAdx = 30.0;
                EnableLongMaxBreakDistanceAtrFilter = false;
                LongMaxBreakDistanceAtr = 3.55;
                EnableLongMaxCandleBodyPctRangeFilter = false;
                LongMaxCandleBodyPctRange = 0.90;
                EnableShortMinAtrFilter = false;
                ShortMinAtr = 3.90;
                EnableShortMinAdxFilter = true;
                ShortMinAdx = 30.0;
                EnableShortMinBreakDistancePointsFilter = false;
                ShortMinBreakDistancePoints = 13.0;
                EnableShortMinCandleBodyPctRangeFilter = false;
                ShortMinCandleBodyPctRange = 0.30;

                EnableFramework = true;
                PersistBandOnTimeout = true;
                MinBandAtrMult = 3.0;
                RegimeTimeoutBars = 60;
                InvertColors = true;

                ShowZoneTint = false;
                ShowShiftMarkers = false;

                BaseContracts = 1;
                CloseOnOppositeShift = true;
                UseStopAtBand = true;
                StopBandSide = StopBandSideOption.OppositeBandEdge;
                EnableHardStop = true;
                HardStopUnit = RiskValueUnit.Dollars;
                HardStopValue = 120.0;

                EnableDailyLossLimit = true;
                DailyLossLimitUnit = RiskValueUnit.Dollars;
                DailyLossLimitValue = 350.0;
                DailyLossIncludesOpenPnl = true;
                FlattenOnDailyLossLimitHit = true;

                EnableDailyProfitLimit = true;
                DailyProfitLimitUnit = RiskValueUnit.Dollars;
                DailyProfitLimitValue = 250.0;
                EnableHardDailyProfitStop = true;
                HardDailyProfitStopUnit = RiskValueUnit.Dollars;
                HardDailyProfitStopValue = 500.0;

                EnableDiagnosticsExport = false;
                ResetDiagnosticsFileOnStart = true;
                DiagnosticsFilePath = ResolveDefaultDiagnosticsPath();
                DiagnosticsRunLabel = string.Empty;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                _atr = ATR(Math.Max(1, AtrLength));
                _adx = ADX(Math.Max(1, AdxLength));
                ResetState();
                InitializeDiagnosticsExporter();
            }
            else if (State == State.Terminated)
            {
                ExportNewClosedDiagnosticsTrades();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == ExecutionBarsInProgress)
            {
                UpdateDailyRiskState(Times[ExecutionBarsInProgress][0], Closes[ExecutionBarsInProgress][0]);
                ProcessExecutionQueue();
                return;
            }

            if (BarsInProgress != 0)
                return;

            bool needsAdxValue = EnableLongMinAdxFilter || EnableShortMinAdxFilter;

            int minimumBars = Math.Max(BarsRequiredToTrade, Math.Max((SwingLength * 2) + 1, AtrLength + 1));
            if (needsAdxValue)
                minimumBars = Math.Max(minimumBars, AdxLength + 1);

            if (CurrentBars[0] < minimumBars || CurrentBars[ExecutionBarsInProgress] < 1)
            {
                BackBrush = null;
                return;
            }

            UpdateSwingLevels();

            double atrValue = _atr[0];
            bool hasMajorHigh = IsValidNumber(_lastSwingHigh);
            bool hasMajorLow = IsValidNumber(_lastSwingLow);

            bool isBullishBreakBase = hasMajorHigh && Close[0] > _lastSwingHigh;
            bool isBearishBreakBase = hasMajorLow && Close[0] < _lastSwingLow;

            bool isBullishBreak = isBullishBreakBase;
            bool isBearishBreak = isBearishBreakBase;
            double bullishBreakAtrMult = LongBreakAtrMult;
            double bearishBreakAtrMult = ShortBreakAtrMult;
            if (UseAtrFilter && atrValue > 0.0)
            {
                isBullishBreak = isBullishBreakBase && (Close[0] - _lastSwingHigh) >= bullishBreakAtrMult * atrValue;
                isBearishBreak = isBearishBreakBase && (_lastSwingLow - Close[0]) >= bearishBreakAtrMult * atrValue;
            }

            bool isBullishShift = isBullishBreak && hasMajorLow;
            bool isBearishShift = isBearishBreak && hasMajorHigh;

            if (isBullishShift && isBearishShift)
            {
                isBullishShift = Close[0] >= Open[0];
                isBearishShift = !isBullishShift;
            }

            bool isBullishShiftFirst = isBullishShift && (_lastShiftBar < 0 || !_isLastShiftBullish);
            bool isBearishShiftFirst = isBearishShift && (_lastShiftBar < 0 || _isLastShiftBullish);
            double adxValue = needsAdxValue && _adx != null ? _adx[0] : double.NaN;
            bool passesLongAdxFilter = PassesDirectionalAdxFilter(MarketPosition.Long, adxValue);
            bool passesShortAdxFilter = PassesDirectionalAdxFilter(MarketPosition.Short, adxValue);
            bool passesTimeFilter = PassesEntryTimeFilter(Times[0][0]);
            double bullishBreakDistancePoints = hasMajorHigh ? Close[0] - _lastSwingHigh : double.NaN;
            double bearishBreakDistancePoints = hasMajorLow ? _lastSwingLow - Close[0] : double.NaN;
            double candleRangePoints = High[0] - Low[0];
            double candleBodyPctRange = candleRangePoints > 0.0
                ? Math.Abs(Close[0] - Open[0]) / candleRangePoints
                : double.NaN;

            if (isBullishShift)
            {
                _regime = 1;
                _bandLow = _lastSwingLow;
                _bandHigh = High[0];
                _structLevel = _lastSwingHigh;
                _lastShiftBar = CurrentBar;
                _isLastShiftBullish = true;
            }

            if (isBearishShift)
            {
                _regime = -1;
                _bandLow = Low[0];
                _bandHigh = _lastSwingHigh;
                _structLevel = _lastSwingLow;
                _lastShiftBar = CurrentBar;
                _isLastShiftBullish = false;
            }

            if (RegimeTimeoutBars > 0
                && _regime != 0
                && !isBullishShift
                && !isBearishShift
                && _lastShiftBar >= 0
                && CurrentBar - _lastShiftBar > RegimeTimeoutBars)
            {
                _regime = 0;
                if (!PersistBandOnTimeout)
                {
                    _bandLow = double.NaN;
                    _bandHigh = double.NaN;
                    _structLevel = double.NaN;
                }
            }

            bool isBaseValidBand = EnableFramework
                && IsValidNumber(_bandLow)
                && IsValidNumber(_bandHigh)
                && _bandHigh > _bandLow;

            double priceSpan = isBaseValidBand ? _bandHigh - _bandLow : double.NaN;
            bool isBandNotTiny =
                (isBaseValidBand && atrValue > 0.0 && MinBandAtrMult > 0.0 && priceSpan >= MinBandAtrMult * atrValue)
                || (isBaseValidBand && (MinBandAtrMult == 0.0 || atrValue <= 0.0));
            bool isValidBand = isBaseValidBand && isBandNotTiny;

            double discountThreshold = isValidBand ? _bandLow + (0.25 * priceSpan) : double.NaN;
            double premiumThreshold = isValidBand ? _bandLow + (0.75 * priceSpan) : double.NaN;

            bool isInDiscount = isValidBand && Close[0] <= discountThreshold;
            bool isInPremium = isValidBand && Close[0] >= premiumThreshold;

            ApplyBackgroundTint(isInDiscount, isInPremium);

            if (ShowShiftMarkers)
            {
                if (isBullishShiftFirst)
                    DrawBullishShiftMarker();
                if (isBearishShiftFirst)
                    DrawBearishShiftMarker();
            }

            UpdateDailyRiskState(Times[0][0], Closes[0][0]);

            bool allowNewEntries = !_dailyEntryLockLoss
                && !_dailyEntryLockProfit
                && !_queuedDailyLossFlatten
                && passesTimeFilter;

            bool passesLongQualityFilters = PassesQualityFilters(
                MarketPosition.Long,
                atrValue,
                bullishBreakDistancePoints,
                candleBodyPctRange);
            bool passesShortQualityFilters = PassesQualityFilters(
                MarketPosition.Short,
                atrValue,
                bearishBreakDistancePoints,
                candleBodyPctRange);

            int orderQuantity = BaseContracts > 0
                ? Math.Max(1, BaseContracts)
                : 0;

            if (isBullishShiftFirst && orderQuantity > 0 && allowNewEntries && passesLongAdxFilter && passesLongQualityFilters)
                QueueEntry(MarketPosition.Long, orderQuantity, isValidBand, atrValue, adxValue, passesLongAdxFilter, passesTimeFilter);

            if (isBearishShiftFirst && orderQuantity > 0 && allowNewEntries && passesShortAdxFilter && passesShortQualityFilters)
                QueueEntry(MarketPosition.Short, orderQuantity, isValidBand, atrValue, adxValue, passesShortAdxFilter, passesTimeFilter);
        }

        private void UpdateSwingLevels()
        {
            double pivotHighValue;
            if (TryGetPivotHigh(SwingLength, out pivotHighValue))
            {
                _lastSwingHigh = pivotHighValue;
                _lastSwingHighBar = CurrentBar - SwingLength;
            }

            double pivotLowValue;
            if (TryGetPivotLow(SwingLength, out pivotLowValue))
            {
                _lastSwingLow = pivotLowValue;
                _lastSwingLowBar = CurrentBar - SwingLength;
            }
        }

        private bool TryResolveStopPrice(bool isLong, bool isValidBand, out double stopPrice)
        {
            stopPrice = double.NaN;
            if (!UseStopAtBand || !isValidBand)
                return false;

            double rawStop = StopFromBand(isLong, _bandLow, _bandHigh, StopBandSide);
            if (!IsValidNumber(rawStop))
                return false;

            double roundedStop = Instrument.MasterInstrument.RoundToTickSize(rawStop);
            if ((isLong && roundedStop >= Close[0]) || (!isLong && roundedStop <= Close[0]))
                return false;

            stopPrice = roundedStop;
            return true;
        }

        private double StopFromBand(bool isLong, double bandLowValue, double bandHighValue, StopBandSideOption stopSide)
        {
            if (isLong)
                return stopSide == StopBandSideOption.OppositeBandEdge ? bandLowValue : bandHighValue;

            return stopSide == StopBandSideOption.OppositeBandEdge ? bandHighValue : bandLowValue;
        }

        private bool TryGetPivotHigh(int strength, out double pivotValue)
        {
            pivotValue = double.NaN;
            if (strength < 1 || CurrentBar < (strength * 2))
                return false;

            int barsAgo = strength;
            double candidate = High[barsAgo];
            for (int i = 1; i <= strength; i++)
            {
                if (High[barsAgo + i] >= candidate)
                    return false;
                if (High[barsAgo - i] > candidate)
                    return false;
            }

            pivotValue = candidate;
            return true;
        }

        private bool TryGetPivotLow(int strength, out double pivotValue)
        {
            pivotValue = double.NaN;
            if (strength < 1 || CurrentBar < (strength * 2))
                return false;

            int barsAgo = strength;
            double candidate = Low[barsAgo];
            for (int i = 1; i <= strength; i++)
            {
                if (Low[barsAgo + i] <= candidate)
                    return false;
                if (Low[barsAgo - i] < candidate)
                    return false;
            }

            pivotValue = candidate;
            return true;
        }

        private void ApplyBackgroundTint(bool isInDiscount, bool isInPremium)
        {
            if (!ShowZoneTint)
            {
                BackBrush = null;
                return;
            }

            Brush discountBrush = InvertColors ? PremiumBrush : DiscountBrush;
            Brush premiumBrush = InvertColors ? DiscountBrush : PremiumBrush;

            if (isInDiscount)
                BackBrush = discountBrush;
            else if (isInPremium)
                BackBrush = premiumBrush;
            else
                BackBrush = null;
        }

        private void DrawBullishShiftMarker()
        {
            string tagBase = "TS_BULL_" + CurrentBar.ToString(CultureInfo.InvariantCulture);
            double markerY = Low[0] - (2 * TickSize);
            Draw.TriangleUp(this, tagBase, false, 0, markerY, BullShiftBrush);
            Draw.Text(this, tagBase + "_TXT", "Shift Up", 0, markerY - (3 * TickSize), ShiftTextBrush);
        }

        private void DrawBearishShiftMarker()
        {
            string tagBase = "TS_BEAR_" + CurrentBar.ToString(CultureInfo.InvariantCulture);
            double markerY = High[0] + (2 * TickSize);
            Draw.TriangleDown(this, tagBase, false, 0, markerY, BearShiftBrush);
            Draw.Text(this, tagBase + "_TXT", "Shift Down", 0, markerY + (3 * TickSize), ShiftTextBrush);
        }

        private void ResetState()
        {
            _activeEntrySignal = string.Empty;
            _lastSwingHigh = double.NaN;
            _lastSwingLow = double.NaN;
            _lastSwingHighBar = -1;
            _lastSwingLowBar = -1;
            _regime = 0;
            _bandLow = double.NaN;
            _bandHigh = double.NaN;
            _structLevel = double.NaN;
            _lastShiftBar = -1;
            _isLastShiftBullish = false;
            _activeStopOrder = null;
            _activeProfitOrder = null;
            _activeStopSignal = string.Empty;
            _activeProfitSignal = string.Empty;
            _queuedEntrySignal = string.Empty;
            _queuedEntryDirection = MarketPosition.Flat;
            _queuedEntryQuantity = 0;
            _queuedFlattenOpposite = false;
            _queuedFlattenSubmitted = false;
            _queuedStopEnabled = false;
            _queuedStopPrice = double.NaN;
            _submittedEntrySignal = string.Empty;
            _submittedEntryDirection = MarketPosition.Flat;
            _submittedStopEnabled = false;
            _submittedStopPrice = double.NaN;
            _currentTradeDate = DateTime.MinValue;
            _dailyStartCumProfit = 0.0;
            _dailyStateInitialized = false;
            _dailyEntryLockLoss = false;
            _dailyEntryLockProfit = false;
            _queuedDailyLossFlatten = false;
            _queuedDailyLossFlattenSubmitted = false;
            _activeStopPrice = double.NaN;
            _activeProfitPrice = double.NaN;
            _consecutiveLossCount = 0;
            _consecutiveStopCount = 0;
            _dailyTradeCount = 0;
            _dailyLossCount = 0;
            _dailyStopCount = 0;
            _queuedDiagnostics = null;
            _submittedDiagnostics = null;
            _diagnosticsByEntrySignal = EnableDiagnosticsExport
                ? new Dictionary<string, DiagnosticsSnapshot>(StringComparer.Ordinal)
                : null;
            _diagnosticsWriteFailed = false;
            _lastDiagnosticsExportedTradeIndex = 0;
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

            Order order = execution.Order;
            string orderName = order.Name ?? string.Empty;
            bool isFilledExecution = order.OrderState == OrderState.Filled || order.OrderState == OrderState.PartFilled;
            if (!isFilledExecution)
                return;

            if (IsEntrySignal(orderName))
            {
                _activeEntrySignal = orderName;
                _submittedEntrySignal = orderName;
                _submittedEntryDirection = orderName.StartsWith("TS_L_", StringComparison.Ordinal)
                    ? MarketPosition.Long
                    : MarketPosition.Short;
                if (EnableDiagnosticsExport)
                    ActivateSubmittedDiagnostics(orderName, time, order.AverageFillPrice > 0.0 ? order.AverageFillPrice : price);

                if (Position.MarketPosition != MarketPosition.Flat && Position.Quantity > 0)
                {
                    double entryPrice = order.AverageFillPrice > 0.0 ? order.AverageFillPrice : Position.AveragePrice;
                    SubmitOrUpdateProtectiveStop(Position.Quantity, entryPrice, entryPrice);
                    SubmitOrUpdateDailyProfitTarget(Position.Quantity, entryPrice, entryPrice);
                }

                return;
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                _activeEntrySignal = string.Empty;
                ClearActiveStopTracking();
            }
        }

        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            if (position == null || position.Account != Account || position.Instrument != Instrument)
                return;

            if (marketPosition != MarketPosition.Flat)
                return;

            ExportNewClosedDiagnosticsTrades();

            _activeEntrySignal = string.Empty;
            ClearActiveStopTracking();
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

            string orderName = order.Name ?? string.Empty;
            if (!string.IsNullOrEmpty(_activeStopSignal) && orderName == _activeStopSignal)
            {
                _activeStopOrder = order;
                if (!IsOrderActive(order))
                {
                    _activeStopOrder = null;
                    _activeStopSignal = string.Empty;
                    _activeStopPrice = double.NaN;
                }
            }

            if (!string.IsNullOrEmpty(_activeProfitSignal) && orderName == _activeProfitSignal)
            {
                _activeProfitOrder = order;
                if (!IsOrderActive(order))
                {
                    _activeProfitOrder = null;
                    _activeProfitSignal = string.Empty;
                    _activeProfitPrice = double.NaN;
                }
            }

            if (IsEntrySignal(orderName) && (orderState == OrderState.Cancelled || orderState == OrderState.Rejected))
            {
                if (Position.MarketPosition == MarketPosition.Flat && orderName == _submittedEntrySignal)
                    ClearSubmittedEntryTracking();
            }
        }

        private void QueueEntry(
            MarketPosition direction,
            int quantity,
            bool isValidBand,
            double atrValue,
            double adxValue,
            bool passesAdxFilter,
            bool passesTimeFilter)
        {
            if (direction == MarketPosition.Flat || quantity <= 0)
                return;

            double stopPrice;
            bool hasStop = TryResolveStopPrice(direction == MarketPosition.Long, isValidBand, out stopPrice);

            _queuedEntryDirection = direction;
            _queuedEntryQuantity = quantity;
            _queuedEntrySignal = direction == MarketPosition.Long
                ? "TS_L_" + CurrentBar.ToString(CultureInfo.InvariantCulture)
                : "TS_S_" + CurrentBar.ToString(CultureInfo.InvariantCulture);
            _queuedStopEnabled = hasStop;
            _queuedStopPrice = hasStop ? stopPrice : double.NaN;
            _queuedFlattenOpposite = CloseOnOppositeShift
                && Position.MarketPosition != MarketPosition.Flat
                && Position.MarketPosition != direction;
            _queuedFlattenSubmitted = false;
            _queuedDiagnostics = EnableDiagnosticsExport
                ? BuildDiagnosticsSnapshot(
                    direction,
                    quantity,
                    isValidBand,
                    hasStop,
                    stopPrice,
                    atrValue,
                    adxValue,
                    passesAdxFilter,
                    passesTimeFilter)
                : null;
        }

        private void ProcessExecutionQueue()
        {
            if (_queuedDailyLossFlatten)
            {
                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    _queuedDailyLossFlatten = false;
                    _queuedDailyLossFlattenSubmitted = false;
                }
                else
                {
                    if (!_queuedDailyLossFlattenSubmitted)
                    {
                        CancelActiveStopOrder();
                        CancelActiveProfitOrder();

                        if (Position.MarketPosition == MarketPosition.Long)
                            ExitLong("TS_DailyLimitFlat_" + CurrentBar.ToString(CultureInfo.InvariantCulture), _activeEntrySignal);
                        else if (Position.MarketPosition == MarketPosition.Short)
                            ExitShort("TS_DailyLimitFlat_" + CurrentBar.ToString(CultureInfo.InvariantCulture), _activeEntrySignal);

                        _queuedDailyLossFlattenSubmitted = true;
                    }

                    return;
                }
            }

            if (!string.IsNullOrEmpty(_queuedEntrySignal) && _queuedEntryQuantity > 0 && _queuedEntryDirection != MarketPosition.Flat)
            {
                if (Position.MarketPosition == _queuedEntryDirection)
                {
                    ClearQueuedEntry();
                    RefreshProtectiveOrdersFromExecutionSeries();
                    return;
                }

                if (_queuedFlattenOpposite)
                {
                    if (Position.MarketPosition == MarketPosition.Flat)
                    {
                        _queuedFlattenOpposite = false;
                        _queuedFlattenSubmitted = false;
                    }
                    else
                    {
                        if (!_queuedFlattenSubmitted)
                        {
                            CancelActiveStopOrder();
                            CancelActiveProfitOrder();

                            if (Position.MarketPosition == MarketPosition.Long)
                                ExitLong("TS_CloseLong_" + CurrentBar.ToString(CultureInfo.InvariantCulture), _activeEntrySignal);
                            else if (Position.MarketPosition == MarketPosition.Short)
                                ExitShort("TS_CloseShort_" + CurrentBar.ToString(CultureInfo.InvariantCulture), _activeEntrySignal);

                            _queuedFlattenSubmitted = true;
                        }

                        return;
                    }
                }

                if (!PassesEntryTimeFilter(Times[ExecutionBarsInProgress][0]))
                {
                    ClearQueuedEntry();
                    return;
                }

                CancelActiveStopOrder();
                CancelActiveProfitOrder();

                _submittedEntrySignal = _queuedEntrySignal;
                _submittedEntryDirection = _queuedEntryDirection;
                _submittedStopEnabled = _queuedStopEnabled;
                _submittedStopPrice = _queuedStopPrice;
                _submittedDiagnostics = _queuedDiagnostics;

                if (_queuedEntryDirection == MarketPosition.Long)
                    EnterLong(_queuedEntryQuantity, _queuedEntrySignal);
                else
                    EnterShort(_queuedEntryQuantity, _queuedEntrySignal);

                ClearQueuedEntry();
                return;
            }

            RefreshProtectiveOrdersFromExecutionSeries();
        }

        private void SubmitOrUpdateProtectiveStop(int quantity, double entryPrice, double currentPrice)
        {
            if (quantity <= 0 || string.IsNullOrEmpty(_submittedEntrySignal) || !IsValidNumber(entryPrice) || entryPrice <= 0.0)
                return;

            double effectiveStopPrice;
            if (!TryResolveEffectiveStopPrice(_submittedEntryDirection, entryPrice, currentPrice, quantity, out effectiveStopPrice))
                return;

            double roundedStop = Instrument.MasterInstrument.RoundToTickSize(effectiveStopPrice);
            string stopSignal = _submittedEntrySignal + "_SL";

            if (IsValidNumber(_activeStopPrice))
            {
                bool isTighterOrEqual = _submittedEntryDirection == MarketPosition.Long
                    ? roundedStop <= _activeStopPrice
                    : roundedStop >= _activeStopPrice;
                if (isTighterOrEqual)
                    return;
            }

            if (_submittedEntryDirection == MarketPosition.Long)
                _activeStopOrder = ExitLongStopMarket(ExecutionBarsInProgress, true, quantity, roundedStop, stopSignal, _submittedEntrySignal);
            else if (_submittedEntryDirection == MarketPosition.Short)
                _activeStopOrder = ExitShortStopMarket(ExecutionBarsInProgress, true, quantity, roundedStop, stopSignal, _submittedEntrySignal);
            else
                return;

            _activeStopSignal = stopSignal;
            _activeStopPrice = roundedStop;
        }

        private void CancelActiveStopOrder()
        {
            if (IsOrderActive(_activeStopOrder))
                CancelOrder(_activeStopOrder);

            _activeStopOrder = null;
            _activeStopSignal = string.Empty;
            _activeStopPrice = double.NaN;
        }

        private void CancelActiveProfitOrder()
        {
            if (IsOrderActive(_activeProfitOrder))
                CancelOrder(_activeProfitOrder);

            _activeProfitOrder = null;
            _activeProfitSignal = string.Empty;
            _activeProfitPrice = double.NaN;
        }

        private bool TryResolveEffectiveStopPrice(MarketPosition direction, double entryPrice, double currentPrice, int quantity, out double stopPrice)
        {
            stopPrice = double.NaN;
            bool hasStop = false;

            if (_submittedStopEnabled && IsValidNumber(_submittedStopPrice))
            {
                stopPrice = _submittedStopPrice;
                hasStop = true;
            }

            double hardStopPrice;
            if (TryResolveHardStopPrice(direction, entryPrice, quantity, out hardStopPrice))
            {
                if (!hasStop)
                {
                    stopPrice = hardStopPrice;
                    hasStop = true;
                }
                else if (direction == MarketPosition.Long)
                {
                    stopPrice = Math.Max(stopPrice, hardStopPrice);
                }
                else if (direction == MarketPosition.Short)
                {
                    stopPrice = Math.Min(stopPrice, hardStopPrice);
                }
            }

            double dailyLossStopPrice;
            if (TryResolveDailyLossStopPrice(direction, entryPrice, quantity, out dailyLossStopPrice))
            {
                if (!hasStop)
                {
                    stopPrice = dailyLossStopPrice;
                    hasStop = true;
                }
                else if (direction == MarketPosition.Long)
                {
                    stopPrice = Math.Max(stopPrice, dailyLossStopPrice);
                }
                else if (direction == MarketPosition.Short)
                {
                    stopPrice = Math.Min(stopPrice, dailyLossStopPrice);
                }
            }

            if (!hasStop)
                return false;

            double roundedStop = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
            if ((direction == MarketPosition.Long && roundedStop >= currentPrice) || (direction == MarketPosition.Short && roundedStop <= currentPrice))
                return false;

            stopPrice = roundedStop;
            return true;
        }

        private bool TryResolveHardStopPrice(MarketPosition direction, double entryPrice, int quantity, out double stopPrice)
        {
            stopPrice = double.NaN;
            if (!EnableHardStop || HardStopValue <= 0.0 || quantity <= 0)
                return false;

            double priceDistance = ResolveHardStopPriceDistance(HardStopUnit, HardStopValue, quantity);
            if (!IsValidNumber(priceDistance) || priceDistance <= 0.0)
                return false;

            stopPrice = direction == MarketPosition.Long
                ? entryPrice - priceDistance
                : entryPrice + priceDistance;
            return IsValidNumber(stopPrice);
        }

        private bool TryResolveDailyLossStopPrice(MarketPosition direction, double entryPrice, int quantity, out double stopPrice)
        {
            stopPrice = double.NaN;
            if (!EnableDailyLossLimit
                || !FlattenOnDailyLossLimitHit
                || !DailyLossIncludesOpenPnl
                || DailyLossLimitValue <= 0.0
                || quantity <= 0)
                return false;

            double pointValue = ResolvePointValue();
            if (pointValue <= 0.0)
                return false;

            double lossCurrencyLimit = ConvertRiskUnitToCurrency(Math.Abs(DailyLossLimitValue), DailyLossLimitUnit);
            double requiredOpenCurrency = -lossCurrencyLimit - ResolveDailyRealizedCurrency();
            double priceDistance = requiredOpenCurrency / (pointValue * Math.Max(1, quantity));

            stopPrice = direction == MarketPosition.Long
                ? entryPrice + priceDistance
                : entryPrice - priceDistance;
            return IsValidNumber(stopPrice);
        }

        private void SubmitOrUpdateDailyProfitTarget(int quantity, double entryPrice, double currentPrice)
        {
            if (quantity <= 0 || string.IsNullOrEmpty(_submittedEntrySignal) || !IsValidNumber(entryPrice) || entryPrice <= 0.0)
                return;

            double targetPrice;
            if (!TryResolveHardDailyProfitTargetPrice(_submittedEntryDirection, entryPrice, quantity, out targetPrice))
                return;

            double roundedTarget = Instrument.MasterInstrument.RoundToTickSize(targetPrice);
            if ((_submittedEntryDirection == MarketPosition.Long && roundedTarget <= currentPrice)
                || (_submittedEntryDirection == MarketPosition.Short && roundedTarget >= currentPrice))
                return;

            if (IsValidNumber(_activeProfitPrice) && roundedTarget == _activeProfitPrice)
                return;

            string profitSignal = _submittedEntrySignal + "_DL";

            if (_submittedEntryDirection == MarketPosition.Long)
                _activeProfitOrder = ExitLongLimit(ExecutionBarsInProgress, true, quantity, roundedTarget, profitSignal, _submittedEntrySignal);
            else if (_submittedEntryDirection == MarketPosition.Short)
                _activeProfitOrder = ExitShortLimit(ExecutionBarsInProgress, true, quantity, roundedTarget, profitSignal, _submittedEntrySignal);
            else
                return;

            _activeProfitSignal = profitSignal;
            _activeProfitPrice = roundedTarget;
        }

        private bool TryResolveHardDailyProfitTargetPrice(MarketPosition direction, double entryPrice, int quantity, out double targetPrice)
        {
            targetPrice = double.NaN;
            if (!EnableHardDailyProfitStop || HardDailyProfitStopValue <= 0.0 || quantity <= 0)
                return false;

            double pointValue = ResolvePointValue();
            if (pointValue <= 0.0)
                return false;

            double targetCurrency = ConvertRiskUnitToCurrency(Math.Abs(HardDailyProfitStopValue), HardDailyProfitStopUnit);
            double remainingCurrency = targetCurrency - ResolveDailyRealizedCurrency();
            if (remainingCurrency <= 0.0)
                return false;

            double priceDistance = remainingCurrency / (pointValue * Math.Max(1, quantity));
            if (!IsValidNumber(priceDistance) || priceDistance <= 0.0)
                return false;

            targetPrice = direction == MarketPosition.Long
                ? entryPrice + priceDistance
                : entryPrice - priceDistance;
            return IsValidNumber(targetPrice);
        }

        private double ResolveHardStopPriceDistance(RiskValueUnit unit, double value, int quantity)
        {
            if (value <= 0.0)
                return double.NaN;

            switch (unit)
            {
                case RiskValueUnit.Points:
                    return value;
                case RiskValueUnit.Ticks:
                    return value * TickSize;
                case RiskValueUnit.Dollars:
                    double pointValue = ResolvePointValue();
                    return pointValue > 0.0
                        ? value / (pointValue * Math.Max(1, quantity))
                        : double.NaN;
                default:
                    return double.NaN;
            }
        }

        private void RollDailyState(DateTime barTime, double cumProfit)
        {
            DateTime barDate = barTime.Date;
            if (!_dailyStateInitialized || barDate != _currentTradeDate)
            {
                _currentTradeDate = barDate;
                _dailyStartCumProfit = cumProfit;
                _dailyStateInitialized = true;
                _dailyEntryLockLoss = false;
                _dailyEntryLockProfit = false;
                _queuedDailyLossFlatten = false;
                _queuedDailyLossFlattenSubmitted = false;
                _dailyTradeCount = 0;
                _dailyLossCount = 0;
                _dailyStopCount = 0;
            }
        }

        private void UpdateDailyRiskState(DateTime barTime, double markPrice)
        {
            double cumProfit = ResolveCumProfitSnapshot();
            RollDailyState(barTime, cumProfit);

            double dailyRealizedCurrency = _dailyStateInitialized ? cumProfit - _dailyStartCumProfit : 0.0;
            double dailyOpenCurrency = Position.MarketPosition == MarketPosition.Flat
                ? 0.0
                : Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, markPrice);

            UpdateDailyTradeLocks(dailyRealizedCurrency, dailyOpenCurrency);
        }

        private void UpdateDailyTradeLocks(double dailyRealizedCurrency, double dailyOpenCurrency)
        {
            if (EnableDailyProfitLimit && DailyProfitLimitValue > 0.0)
            {
                double dailyProfitMetric = ConvertCurrencyToRiskUnit(dailyRealizedCurrency, DailyProfitLimitUnit);
                if (dailyProfitMetric >= Math.Abs(DailyProfitLimitValue))
                    _dailyEntryLockProfit = true;
            }

            if (EnableHardDailyProfitStop && HardDailyProfitStopValue > 0.0)
            {
                double dailyHardProfitMetric = ConvertCurrencyToRiskUnit(
                    dailyRealizedCurrency + dailyOpenCurrency,
                    HardDailyProfitStopUnit);

                if (dailyHardProfitMetric >= Math.Abs(HardDailyProfitStopValue))
                {
                    _dailyEntryLockProfit = true;
                    ClearQueuedEntry();

                    if (Position.MarketPosition != MarketPosition.Flat && !IsOrderActive(_activeProfitOrder))
                        QueueDailyLimitFlatten();
                }
            }

            if (EnableDailyLossLimit && DailyLossLimitValue > 0.0)
            {
                double dailyLossSourceCurrency = dailyRealizedCurrency + (DailyLossIncludesOpenPnl ? dailyOpenCurrency : 0.0);
                double dailyLossMetric = ConvertCurrencyToRiskUnit(dailyLossSourceCurrency, DailyLossLimitUnit);
                if (dailyLossMetric <= -Math.Abs(DailyLossLimitValue))
                {
                    _dailyEntryLockLoss = true;
                    ClearQueuedEntry();

                    if (FlattenOnDailyLossLimitHit
                        && Position.MarketPosition != MarketPosition.Flat
                        && !IsOrderActive(_activeStopOrder))
                        QueueDailyLimitFlatten();
                }
            }
        }

        private void RefreshProtectiveOrdersFromExecutionSeries()
        {
            if (BarsInProgress != ExecutionBarsInProgress
                || Position.MarketPosition == MarketPosition.Flat
                || Position.Quantity <= 0
                || string.IsNullOrEmpty(_submittedEntrySignal))
                return;

            SubmitOrUpdateProtectiveStop(Position.Quantity, Position.AveragePrice, Close[0]);
            SubmitOrUpdateDailyProfitTarget(Position.Quantity, Position.AveragePrice, Close[0]);
        }

        private void QueueDailyLimitFlatten()
        {
            _queuedDailyLossFlatten = true;
            _queuedDailyLossFlattenSubmitted = false;
        }

        private double ResolveCumProfitSnapshot()
        {
            return SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
        }

        private double ResolveDailyRealizedCurrency()
        {
            double cumProfit = ResolveCumProfitSnapshot();
            return _dailyStateInitialized ? cumProfit - _dailyStartCumProfit : 0.0;
        }

        private double ConvertCurrencyToRiskUnit(double currencyValue, RiskValueUnit unit)
        {
            double pointValue = ResolvePointValue();
            switch (unit)
            {
                case RiskValueUnit.Points:
                    return pointValue > 0.0 ? currencyValue / pointValue : currencyValue;
                case RiskValueUnit.Ticks:
                    return pointValue > 0.0 && TickSize > 0.0
                        ? currencyValue / (pointValue * TickSize)
                        : currencyValue;
                case RiskValueUnit.Dollars:
                default:
                    return currencyValue;
            }
        }

        private double ConvertRiskUnitToCurrency(double value, RiskValueUnit unit)
        {
            double pointValue = ResolvePointValue();
            switch (unit)
            {
                case RiskValueUnit.Points:
                    return value * pointValue;
                case RiskValueUnit.Ticks:
                    return value * pointValue * TickSize;
                case RiskValueUnit.Dollars:
                default:
                    return value;
            }
        }

        private double ResolvePointValue()
        {
            return Instrument != null
                && Instrument.MasterInstrument != null
                && Instrument.MasterInstrument.PointValue > 0.0
                    ? Instrument.MasterInstrument.PointValue
                    : 1.0;
        }

        private DiagnosticsSnapshot BuildDiagnosticsSnapshot(
            MarketPosition direction,
            int quantity,
            bool isValidBand,
            bool stopEnabled,
            double stopPrice,
            double atrValue,
            double adxValue,
            bool passesAdxFilter,
            bool passesTimeFilter)
        {
            double bandWidth = IsValidNumber(_bandLow) && IsValidNumber(_bandHigh) && _bandHigh > _bandLow
                ? _bandHigh - _bandLow
                : double.NaN;
            double breakDistance = IsValidNumber(_structLevel)
                ? (direction == MarketPosition.Long ? Close[0] - _structLevel : _structLevel - Close[0])
                : double.NaN;
            double candleRange = High[0] - Low[0];
            double candleBody = Math.Abs(Close[0] - Open[0]);
            double bandPosition = IsValidNumber(bandWidth) && bandWidth > 0.0
                ? (Close[0] - _bandLow) / bandWidth
                : double.NaN;
            double stopDistance = stopEnabled && IsValidNumber(stopPrice)
                ? Math.Abs(Close[0] - stopPrice)
                : double.NaN;

            return new DiagnosticsSnapshot
            {
                EntrySignal = direction == MarketPosition.Long
                    ? "TS_L_" + CurrentBar.ToString(CultureInfo.InvariantCulture)
                    : "TS_S_" + CurrentBar.ToString(CultureInfo.InvariantCulture),
                Direction = direction == MarketPosition.Long ? "Long" : "Short",
                SignalTime = Times[0][0],
                SignalOpen = Open[0],
                SignalHigh = High[0],
                SignalLow = Low[0],
                SignalClose = Close[0],
                SignalBar = CurrentBar,
                SignalHour = Times[0][0].Hour,
                SignalDayOfWeek = Times[0][0].DayOfWeek.ToString(),
                Atr = atrValue,
                Adx = adxValue,
                BreakDistancePoints = breakDistance,
                BreakDistanceAtr = IsValidNumber(breakDistance) && atrValue > 0.0 ? breakDistance / atrValue : double.NaN,
                CandleRangePoints = candleRange,
                CandleBodyPoints = candleBody,
                CandleBodyPctRange = candleRange > 0.0 ? candleBody / candleRange : double.NaN,
                StructLevel = _structLevel,
                BandLow = _bandLow,
                BandHigh = _bandHigh,
                BandWidthPoints = bandWidth,
                BandWidthAtr = IsValidNumber(bandWidth) && atrValue > 0.0 ? bandWidth / atrValue : double.NaN,
                BandPosition = bandPosition,
                IsValidBand = isValidBand,
                StopEnabled = stopEnabled,
                StopPrice = stopPrice,
                StopDistancePoints = stopDistance,
                StopDistanceAtr = IsValidNumber(stopDistance) && atrValue > 0.0 ? stopDistance / atrValue : double.NaN,
                FlattenOpposite = CloseOnOppositeShift
                    && Position.MarketPosition != MarketPosition.Flat
                    && Position.MarketPosition != direction,
                PriorLossStreak = _consecutiveLossCount,
                PriorStopStreak = _consecutiveStopCount,
                PreDayRealizedCurrency = ResolveDailyRealizedCurrency(),
                PreDayTradeCount = _dailyTradeCount,
                PreDayLossCount = _dailyLossCount,
                PreDayStopCount = _dailyStopCount,
                PassedAdxFilter = passesAdxFilter,
                PassedTimeFilter = passesTimeFilter,
                Regime = _regime,
                Quantity = quantity
            };
        }

        private void ActivateSubmittedDiagnostics(string entrySignal, DateTime entryTime, double entryPrice)
        {
            DiagnosticsSnapshot diagnostics = _submittedDiagnostics;
            if (diagnostics == null || !string.Equals(diagnostics.EntrySignal, entrySignal, StringComparison.Ordinal))
                return;

            diagnostics.EntryTime = entryTime;
            diagnostics.EntryPrice = entryPrice;
            diagnostics.Quantity = Position.Quantity > 0 ? Position.Quantity : Math.Max(1, diagnostics.Quantity);
            if (_diagnosticsByEntrySignal != null && !string.IsNullOrEmpty(entrySignal))
                _diagnosticsByEntrySignal[entrySignal] = diagnostics;
            _submittedDiagnostics = null;
        }

        private void ExportNewClosedDiagnosticsTrades()
        {
            if (SystemPerformance == null || SystemPerformance.AllTrades == null)
                return;

            int total = SystemPerformance.AllTrades.Count;
            if (total <= _lastDiagnosticsExportedTradeIndex)
                return;

            for (int i = _lastDiagnosticsExportedTradeIndex; i < total; i++)
            {
                Trade trade = SystemPerformance.AllTrades[i];
                if (trade == null || trade.Entry == null || trade.Exit == null)
                    continue;

                string entryName = trade.Entry.Name ?? string.Empty;
                DiagnosticsSnapshot diagnostics = null;
                if (_diagnosticsByEntrySignal != null && !string.IsNullOrEmpty(entryName))
                    _diagnosticsByEntrySignal.TryGetValue(entryName, out diagnostics);

                if (diagnostics == null)
                {
                    diagnostics = new DiagnosticsSnapshot
                    {
                        EntrySignal = entryName,
                        Direction = trade.Entry.MarketPosition == MarketPosition.Short ? "Short" : "Long"
                    };
                }

                diagnostics.EntryTime = trade.Entry.Time;
                diagnostics.EntryPrice = trade.Entry.Price;
                diagnostics.ExitTime = trade.Exit.Time;
                diagnostics.ExitPrice = trade.Exit.Price;
                diagnostics.ExitName = trade.Exit.Name ?? string.Empty;
                diagnostics.ExitType = ResolveExitType(diagnostics.ExitName);
                diagnostics.Quantity = trade.Quantity;
                diagnostics.ProfitCurrency = trade.ProfitCurrency;
                diagnostics.ProfitPoints = diagnostics.Direction == "Short"
                    ? trade.Entry.Price - trade.Exit.Price
                    : trade.Exit.Price - trade.Entry.Price;

                RecordCompletedTradeOutcome(diagnostics.ProfitCurrency, diagnostics.ExitType);

                if (EnableDiagnosticsExport)
                    TryWriteDiagnosticsRow(diagnostics);

                if (_diagnosticsByEntrySignal != null && !string.IsNullOrEmpty(entryName))
                    _diagnosticsByEntrySignal.Remove(entryName);
            }

            _lastDiagnosticsExportedTradeIndex = total;
        }

        private void RecordCompletedTradeOutcome(double profitCurrency, string exitType)
        {
            _dailyTradeCount++;
            if (profitCurrency < 0.0)
            {
                _dailyLossCount++;
                _consecutiveLossCount++;
            }
            else
            {
                _consecutiveLossCount = 0;
            }

            if (string.Equals(exitType, "Stop", StringComparison.Ordinal))
            {
                _dailyStopCount++;
                _consecutiveStopCount++;
            }
            else
            {
                _consecutiveStopCount = 0;
            }
        }

        private void InitializeDiagnosticsExporter()
        {
            _diagnosticsRunLabelResolved = ResolveDiagnosticsRunLabel();
            if (!EnableDiagnosticsExport)
                return;

            try
            {
                string filePath = ResolveDiagnosticsFilePath();
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                if (ResetDiagnosticsFileOnStart && File.Exists(filePath))
                    File.Delete(filePath);

                if (!File.Exists(filePath))
                    File.WriteAllText(filePath, BuildDiagnosticsHeader() + Environment.NewLine);
            }
            catch
            {
                _diagnosticsWriteFailed = true;
            }
        }

        private void TryWriteDiagnosticsRow(DiagnosticsSnapshot diagnostics)
        {
            if (diagnostics == null || _diagnosticsWriteFailed)
                return;

            try
            {
                string filePath = ResolveDiagnosticsFilePath();
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                if (!File.Exists(filePath))
                    File.WriteAllText(filePath, BuildDiagnosticsHeader() + Environment.NewLine);

                File.AppendAllText(filePath, BuildDiagnosticsRow(diagnostics) + Environment.NewLine);
            }
            catch
            {
                _diagnosticsWriteFailed = true;
            }
        }

        private static string ResolveDefaultDiagnosticsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Glitch Trendshift Diagnostics.csv");
        }

        private string ResolveDiagnosticsFilePath()
        {
            return string.IsNullOrWhiteSpace(DiagnosticsFilePath)
                ? ResolveDefaultDiagnosticsPath()
                : DiagnosticsFilePath.Trim();
        }

        private string ResolveDiagnosticsRunLabel()
        {
            return string.IsNullOrWhiteSpace(DiagnosticsRunLabel)
                ? DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
                : DiagnosticsRunLabel.Trim();
        }

        private static string ResolveExitType(string orderName)
        {
            if (string.IsNullOrEmpty(orderName))
                return string.Empty;
            if (orderName.EndsWith("_SL", StringComparison.Ordinal))
                return "Stop";
            if (orderName.Contains("Exit on session close"))
                return "SessionClose";
            if (orderName.StartsWith("TS_CloseLong_", StringComparison.Ordinal) || orderName.StartsWith("TS_CloseShort_", StringComparison.Ordinal))
                return "OppositeShift";
            if (orderName.EndsWith("_DL", StringComparison.Ordinal))
                return "DailyProfitTarget";
            if (orderName.StartsWith("TS_DailyLimitFlat_", StringComparison.Ordinal))
                return "DailyLossFlatten";
            return orderName;
        }

        private static string BuildDiagnosticsHeader()
        {
            return "RunLabel,Strategy,Instrument,EntrySignal,Direction,SignalTime,EntryTime,ExitTime,SignalBar,SignalHour,SignalDayOfWeek,Quantity,SignalOpen,SignalHigh,SignalLow,SignalClose,EntryPrice,ExitPrice,ProfitPoints,ProfitCurrency,ExitName,ExitType,Atr,Adx,BreakDistancePoints,BreakDistanceAtr,CandleRangePoints,CandleBodyPoints,CandleBodyPctRange,StructLevel,BandLow,BandHigh,BandWidthPoints,BandWidthAtr,BandPosition,IsValidBand,StopEnabled,StopPrice,StopDistancePoints,StopDistanceAtr,FlattenOpposite,PriorLossStreak,PriorStopStreak,PreDayRealizedCurrency,PreDayTradeCount,PreDayLossCount,PreDayStopCount,PassedAdxFilter,PassedTimeFilter,Regime,SwingLength,LongBreakAtrMult,ShortBreakAtrMult,AdxLength,EnableEntryHourFilter,EntryStartHour,EntryEndHour,EnableSundayBlock,EnableLongMinAdxFilter,LongMinAdx,EnableLongMaxBreakDistanceAtrFilter,LongMaxBreakDistanceAtr,EnableLongMaxCandleBodyPctRangeFilter,LongMaxCandleBodyPctRange,EnableShortMinAdxFilter,ShortMinAdx,EnableShortMinAtrFilter,ShortMinAtr,EnableShortMinBreakDistancePointsFilter,ShortMinBreakDistancePoints,EnableShortMinCandleBodyPctRangeFilter,ShortMinCandleBodyPctRange,EnableFramework,MinBandAtrMult,RegimeTimeoutBars,UseStopAtBand,StopBandSide,EnableHardStop,HardStopUnit,HardStopValue,EnableDailyLossLimit,DailyLossLimitUnit,DailyLossLimitValue,EnableDailyProfitLimit,DailyProfitLimitUnit,DailyProfitLimitValue,EnableHardDailyProfitStop,HardDailyProfitStopUnit,HardDailyProfitStopValue";
        }

        private string BuildDiagnosticsRow(DiagnosticsSnapshot diagnostics)
        {
            return string.Join(",",
                EscapeCsv(_diagnosticsRunLabelResolved),
                EscapeCsv(Name),
                EscapeCsv(Instrument != null ? Instrument.FullName : string.Empty),
                EscapeCsv(diagnostics.EntrySignal),
                EscapeCsv(diagnostics.Direction),
                EscapeCsv(FormatDateTime(diagnostics.SignalTime)),
                EscapeCsv(FormatDateTime(diagnostics.EntryTime)),
                EscapeCsv(FormatDateTime(diagnostics.ExitTime)),
                diagnostics.SignalBar.ToString(CultureInfo.InvariantCulture),
                diagnostics.SignalHour.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(diagnostics.SignalDayOfWeek),
                diagnostics.Quantity.ToString(CultureInfo.InvariantCulture),
                FormatDouble(diagnostics.SignalOpen),
                FormatDouble(diagnostics.SignalHigh),
                FormatDouble(diagnostics.SignalLow),
                FormatDouble(diagnostics.SignalClose),
                FormatDouble(diagnostics.EntryPrice),
                FormatDouble(diagnostics.ExitPrice),
                FormatDouble(diagnostics.ProfitPoints),
                FormatDouble(diagnostics.ProfitCurrency),
                EscapeCsv(diagnostics.ExitName),
                EscapeCsv(diagnostics.ExitType),
                FormatDouble(diagnostics.Atr),
                FormatDouble(diagnostics.Adx),
                FormatDouble(diagnostics.BreakDistancePoints),
                FormatDouble(diagnostics.BreakDistanceAtr),
                FormatDouble(diagnostics.CandleRangePoints),
                FormatDouble(diagnostics.CandleBodyPoints),
                FormatDouble(diagnostics.CandleBodyPctRange),
                FormatDouble(diagnostics.StructLevel),
                FormatDouble(diagnostics.BandLow),
                FormatDouble(diagnostics.BandHigh),
                FormatDouble(diagnostics.BandWidthPoints),
                FormatDouble(diagnostics.BandWidthAtr),
                FormatDouble(diagnostics.BandPosition),
                diagnostics.IsValidBand ? "1" : "0",
                diagnostics.StopEnabled ? "1" : "0",
                FormatDouble(diagnostics.StopPrice),
                FormatDouble(diagnostics.StopDistancePoints),
                FormatDouble(diagnostics.StopDistanceAtr),
                diagnostics.FlattenOpposite ? "1" : "0",
                diagnostics.PriorLossStreak.ToString(CultureInfo.InvariantCulture),
                diagnostics.PriorStopStreak.ToString(CultureInfo.InvariantCulture),
                FormatDouble(diagnostics.PreDayRealizedCurrency),
                diagnostics.PreDayTradeCount.ToString(CultureInfo.InvariantCulture),
                diagnostics.PreDayLossCount.ToString(CultureInfo.InvariantCulture),
                diagnostics.PreDayStopCount.ToString(CultureInfo.InvariantCulture),
                diagnostics.PassedAdxFilter ? "1" : "0",
                diagnostics.PassedTimeFilter ? "1" : "0",
                diagnostics.Regime.ToString(CultureInfo.InvariantCulture),
                SwingLength.ToString(CultureInfo.InvariantCulture),
                FormatDouble(LongBreakAtrMult),
                FormatDouble(ShortBreakAtrMult),
                AdxLength.ToString(CultureInfo.InvariantCulture),
                EnableEntryHourFilter ? "1" : "0",
                EntryStartHour.ToString(CultureInfo.InvariantCulture),
                EntryEndHour.ToString(CultureInfo.InvariantCulture),
                EnableSundayBlock ? "1" : "0",
                EnableLongMinAdxFilter ? "1" : "0",
                FormatDouble(LongMinAdx),
                EnableLongMaxBreakDistanceAtrFilter ? "1" : "0",
                FormatDouble(LongMaxBreakDistanceAtr),
                EnableLongMaxCandleBodyPctRangeFilter ? "1" : "0",
                FormatDouble(LongMaxCandleBodyPctRange),
                EnableShortMinAdxFilter ? "1" : "0",
                FormatDouble(ShortMinAdx),
                EnableShortMinAtrFilter ? "1" : "0",
                FormatDouble(ShortMinAtr),
                EnableShortMinBreakDistancePointsFilter ? "1" : "0",
                FormatDouble(ShortMinBreakDistancePoints),
                EnableShortMinCandleBodyPctRangeFilter ? "1" : "0",
                FormatDouble(ShortMinCandleBodyPctRange),
                EnableFramework ? "1" : "0",
                FormatDouble(MinBandAtrMult),
                RegimeTimeoutBars.ToString(CultureInfo.InvariantCulture),
                UseStopAtBand ? "1" : "0",
                EscapeCsv(StopBandSide.ToString()),
                EnableHardStop ? "1" : "0",
                EscapeCsv(HardStopUnit.ToString()),
                FormatDouble(HardStopValue),
                EnableDailyLossLimit ? "1" : "0",
                EscapeCsv(DailyLossLimitUnit.ToString()),
                FormatDouble(DailyLossLimitValue),
                EnableDailyProfitLimit ? "1" : "0",
                EscapeCsv(DailyProfitLimitUnit.ToString()),
                FormatDouble(DailyProfitLimitValue),
                EnableHardDailyProfitStop ? "1" : "0",
                EscapeCsv(HardDailyProfitStopUnit.ToString()),
                FormatDouble(HardDailyProfitStopValue));
        }

        private static string FormatDateTime(DateTime value)
        {
            return value == DateTime.MinValue
                ? string.Empty
                : value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string FormatDouble(double value)
        {
            return IsValidNumber(value)
                ? value.ToString("0.########", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static string EscapeCsv(string value)
        {
            string text = value ?? string.Empty;
            return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? "\"" + text.Replace("\"", "\"\"") + "\""
                : text;
        }

        private void ClearQueuedEntry()
        {
            _queuedEntrySignal = string.Empty;
            _queuedEntryDirection = MarketPosition.Flat;
            _queuedEntryQuantity = 0;
            _queuedFlattenOpposite = false;
            _queuedFlattenSubmitted = false;
            _queuedStopEnabled = false;
            _queuedStopPrice = double.NaN;
            _queuedDiagnostics = null;
        }

        private void ClearSubmittedEntryTracking()
        {
            _submittedEntrySignal = string.Empty;
            _submittedEntryDirection = MarketPosition.Flat;
            _submittedStopEnabled = false;
            _submittedStopPrice = double.NaN;
            _submittedDiagnostics = null;
        }

        private void ClearActiveStopTracking()
        {
            _activeStopOrder = null;
            _activeProfitOrder = null;
            _activeStopSignal = string.Empty;
            _activeProfitSignal = string.Empty;
            _activeStopPrice = double.NaN;
            _activeProfitPrice = double.NaN;
            ClearSubmittedEntryTracking();
        }

        private static bool IsEntrySignal(string orderName)
        {
            return orderName.StartsWith("TS_L_", StringComparison.Ordinal)
                || orderName.StartsWith("TS_S_", StringComparison.Ordinal);
        }

        private static bool IsOrderActive(Order order)
        {
            if (order == null)
                return false;

            return order.OrderState == OrderState.Accepted
                || order.OrderState == OrderState.Working
                || order.OrderState == OrderState.PartFilled;
        }

        private static bool IsValidNumber(double value)
        {
            return !(double.IsNaN(value) || double.IsInfinity(value));
        }

        private bool PassesEntryTimeFilter(DateTime barTime)
        {
            if (EnableSundayBlock && barTime.DayOfWeek == DayOfWeek.Sunday)
                return false;

            if (!EnableEntryHourFilter)
                return true;

            int startHour = Math.Max(0, Math.Min(23, EntryStartHour));
            int endHour = Math.Max(0, Math.Min(23, EntryEndHour));
            int hour = barTime.Hour;

            if (startHour == endHour)
                return true;

            if (startHour < endHour)
                return hour >= startHour && hour < endHour;

            return hour >= startHour || hour < endHour;
        }

        private bool PassesDirectionalAdxFilter(MarketPosition direction, double adxValue)
        {
            if (direction == MarketPosition.Long)
                return !EnableLongMinAdxFilter || (IsValidNumber(adxValue) && adxValue >= LongMinAdx);

            else if (direction == MarketPosition.Short)
                return !EnableShortMinAdxFilter || (IsValidNumber(adxValue) && adxValue >= ShortMinAdx);

            return true;
        }

        private bool PassesQualityFilters(
            MarketPosition direction,
            double atrValue,
            double breakDistancePoints,
            double candleBodyPctRange)
        {
            if (direction == MarketPosition.Long)
            {
                double breakDistanceAtr = IsValidNumber(breakDistancePoints) && atrValue > 0.0
                    ? breakDistancePoints / atrValue
                    : double.NaN;

                if (EnableLongMaxBreakDistanceAtrFilter
                    && (!IsValidNumber(breakDistanceAtr) || breakDistanceAtr > LongMaxBreakDistanceAtr))
                    return false;

                if (EnableLongMaxCandleBodyPctRangeFilter
                    && (!IsValidNumber(candleBodyPctRange) || candleBodyPctRange > LongMaxCandleBodyPctRange))
                    return false;

                return true;
            }

            if (direction == MarketPosition.Short)
            {
                if (EnableShortMinAtrFilter
                    && (!IsValidNumber(atrValue) || atrValue < ShortMinAtr))
                    return false;

                if (EnableShortMinBreakDistancePointsFilter
                    && (!IsValidNumber(breakDistancePoints) || breakDistancePoints < ShortMinBreakDistancePoints))
                    return false;

                if (EnableShortMinCandleBodyPctRangeFilter
                    && (!IsValidNumber(candleBodyPctRange) || candleBodyPctRange < ShortMinCandleBodyPctRange))
                    return false;

                return true;
            }

            return true;
        }

        private static Brush CreateBrush(Color color, byte alpha)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Swing length", Order = 1, GroupName = GroupMarketStructure)]
        public int SwingLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ATR filter", Order = 2, GroupName = GroupMarketStructure)]
        public bool UseAtrFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ATR length", Order = 3, GroupName = GroupMarketStructure)]
        public int AtrLength { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ADX length", Order = 1, GroupName = GroupFilters)]
        public int AdxLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable entry hour filter", Order = 2, GroupName = GroupFilters)]
        public bool EnableEntryHourFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Entry start hour", Order = 3, GroupName = GroupFilters)]
        public int EntryStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Entry end hour", Order = 4, GroupName = GroupFilters)]
        public int EntryEndHour { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Sunday block", Order = 5, GroupName = GroupFilters)]
        public bool EnableSundayBlock { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable long min ADX", Order = 1, GroupName = GroupLongParameters)]
        public bool EnableLongMinAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Long min ADX", Order = 2, GroupName = GroupLongParameters)]
        public double LongMinAdx { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Long break ATR mult", Order = 3, GroupName = GroupLongParameters)]
        public double LongBreakAtrMult { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable long max break ATR", Order = 4, GroupName = GroupLongParameters)]
        public bool EnableLongMaxBreakDistanceAtrFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 50.0)]
        [Display(Name = "Long max break ATR", Order = 5, GroupName = GroupLongParameters)]
        public double LongMaxBreakDistanceAtr { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable long max body %", Order = 6, GroupName = GroupLongParameters)]
        public bool EnableLongMaxCandleBodyPctRangeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Long max body % of range", Order = 7, GroupName = GroupLongParameters)]
        public double LongMaxCandleBodyPctRange { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable short min ADX", Order = 1, GroupName = GroupShortParameters)]
        public bool EnableShortMinAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Short min ADX", Order = 2, GroupName = GroupShortParameters)]
        public double ShortMinAdx { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Short break ATR mult", Order = 3, GroupName = GroupShortParameters)]
        public double ShortBreakAtrMult { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable short min ATR", Order = 4, GroupName = GroupShortParameters)]
        public bool EnableShortMinAtrFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(Name = "Short min ATR", Order = 5, GroupName = GroupShortParameters)]
        public double ShortMinAtr { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable short min break", Order = 6, GroupName = GroupShortParameters)]
        public bool EnableShortMinBreakDistancePointsFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10000.0)]
        [Display(Name = "Short min break points", Order = 7, GroupName = GroupShortParameters)]
        public double ShortMinBreakDistancePoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable short min body %", Order = 8, GroupName = GroupShortParameters)]
        public bool EnableShortMinCandleBodyPctRangeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Short min body % of range", Order = 9, GroupName = GroupShortParameters)]
        public double ShortMinCandleBodyPctRange { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable framework", Order = 1, GroupName = GroupFramework)]
        public bool EnableFramework { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Persist band on timeout", Order = 2, GroupName = GroupFramework)]
        public bool PersistBandOnTimeout { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Min band ATR mult", Order = 3, GroupName = GroupFramework)]
        public double MinBandAtrMult { get; set; }

        [NinjaScriptProperty]
        [Range(0, 50000)]
        [Display(Name = "Regime timeout bars", Order = 4, GroupName = GroupFramework)]
        public int RegimeTimeoutBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Invert colors", Order = 5, GroupName = GroupFramework)]
        public bool InvertColors { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show zone tint", Order = 1, GroupName = GroupVisual)]
        public bool ShowZoneTint { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show shift markers", Order = 2, GroupName = GroupVisual)]
        public bool ShowShiftMarkers { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Base contracts", Order = 1, GroupName = GroupRisk)]
        public int BaseContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flat on opposite shift", Order = 2, GroupName = GroupRisk)]
        public bool CloseOnOppositeShift { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use stop at band", Order = 3, GroupName = GroupRisk)]
        public bool UseStopAtBand { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop band side", Order = 4, GroupName = GroupRisk)]
        public StopBandSideOption StopBandSide { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable hard stop", Order = 5, GroupName = GroupRisk)]
        public bool EnableHardStop { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hard stop unit", Order = 6, GroupName = GroupRisk)]
        public RiskValueUnit HardStopUnit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1000000.0)]
        [Display(Name = "Hard stop value", Order = 7, GroupName = GroupRisk)]
        public double HardStopValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable daily loss limit", Order = 1, GroupName = GroupDailyLimits)]
        public bool EnableDailyLossLimit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Daily loss unit", Order = 2, GroupName = GroupDailyLimits)]
        public RiskValueUnit DailyLossLimitUnit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1000000.0)]
        [Display(Name = "Daily loss value", Order = 3, GroupName = GroupDailyLimits)]
        public double DailyLossLimitValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Daily loss includes open PnL", Order = 4, GroupName = GroupDailyLimits)]
        public bool DailyLossIncludesOpenPnl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flatten on daily loss hit", Order = 5, GroupName = GroupDailyLimits)]
        public bool FlattenOnDailyLossLimitHit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable daily profit cap", Order = 6, GroupName = GroupDailyLimits)]
        public bool EnableDailyProfitLimit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Daily profit unit", Order = 7, GroupName = GroupDailyLimits)]
        public RiskValueUnit DailyProfitLimitUnit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1000000.0)]
        [Display(Name = "Daily profit value", Order = 8, GroupName = GroupDailyLimits)]
        public double DailyProfitLimitValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable hard daily profit stop", Order = 9, GroupName = GroupDailyLimits)]
        public bool EnableHardDailyProfitStop { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hard daily profit unit", Order = 10, GroupName = GroupDailyLimits)]
        public RiskValueUnit HardDailyProfitStopUnit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1000000.0)]
        [Display(Name = "Hard daily profit value", Order = 11, GroupName = GroupDailyLimits)]
        public double HardDailyProfitStopValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable diagnostics export", Order = 1, GroupName = GroupDiagnostics)]
        public bool EnableDiagnosticsExport { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reset diagnostics file on start", Order = 2, GroupName = GroupDiagnostics)]
        public bool ResetDiagnosticsFileOnStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Diagnostics file path", Order = 3, GroupName = GroupDiagnostics)]
        public string DiagnosticsFilePath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Diagnostics run label", Order = 4, GroupName = GroupDiagnostics)]
        public string DiagnosticsRunLabel { get; set; }
    }
}
