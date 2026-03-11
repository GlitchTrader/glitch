#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class GlitchBomsStrategy : Strategy
    {
        private const string LongSignalName = "BOMS-L1";
        private const string LongRunnerSignalName = "BOMS-L2";
        private const string ShortSignalName = "BOMS-S1";
        private const string ShortRunnerSignalName = "BOMS-S2";
        private const int MaxStoredSwingPivots = 120;

        private enum PivotType
        {
            High,
            Low
        }

        private enum SetupDirection
        {
            None,
            Long,
            Short
        }

        private struct PivotPoint
        {
            public int Sequence;
            public int BarIndex;
            public double Price;
            public PivotType Type;
            public string Label;
        }

        private double _tickSize;
        private Nullable<PivotPoint> _lastPivot;
        private readonly List<PivotPoint> _swingPivots = new List<PivotPoint>();
        private int _pivotSequence;
        private int _lastSignalBarIndex;
        private ATR _atr;
        private ADX _adx;
        private SetupDirection _pendingSetupDirection;
        private PivotPoint _pendingA;
        private PivotPoint _pendingB;
        private PivotPoint _pendingE;
        private PivotPoint _pendingF;
        private PivotPoint _pendingG;
        private int _pendingSetupBar;
        private int _lastArmedLongGBarIndex;
        private int _lastArmedShortGBarIndex;
        private int _lastSubmittedLongStopTicks;
        private int _lastSubmittedShortStopTicks;
        private int _lastSubmittedLongTarget1Ticks;
        private int _lastSubmittedShortTarget1Ticks;
        private int _lastSubmittedLongTrailTicks;
        private int _lastSubmittedShortTrailTicks;
        private bool _trailingInitialized;
        private MarketPosition _trailingPosition;
        private double _activeStopPrice;
        private double _extremePriceSinceEntry;
        private double _trailDistancePoints;
        private double _trailEntryPrice;
        private bool _trailActivationReady;
        private bool _trailTp1Touched;
        private bool _trailPullbackSeen;
        private double _trailHReferencePrice;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Pivot Strength", Order = 1, GroupName = "Structure")]
        public int PivotStrength { get; set; }

        [NinjaScriptProperty]
        [Range(0.00, 10.00)]
        [Display(Name = "Break Buffer Points", Order = 2, GroupName = "Structure")]
        public double BreakBufferPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Pullback Pivot", Order = 3, GroupName = "Structure")]
        public bool RequirePullbackPivot { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Draw Pivot Labels", Order = 4, GroupName = "Structure")]
        public bool DrawPivotLabels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Draw Structure Lines", Order = 5, GroupName = "Structure")]
        public bool DrawStructureLines { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Label Offset Ticks", Order = 6, GroupName = "Structure")]
        public int LabelOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Setup Expiry Bars", Order = 7, GroupName = "Structure")]
        public int SetupExpiryBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Break Of F", Order = 8, GroupName = "Structure")]
        public bool RequireBreakOfFForEntry { get; set; }

        [NinjaScriptProperty]
        [Range(0.00, 25.00)]
        [Display(Name = "Stop Padding Behind E", Order = 9, GroupName = "Structure")]
        public double StopBehindEPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Primary Quantity", Order = 1, GroupName = "Execution")]
        public int PrimaryQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Runner Quantity", Order = 2, GroupName = "Execution")]
        public int RunnerQuantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Signal Flip", Order = 3, GroupName = "Execution")]
        public bool AllowSignalFlip { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Min Bars Between Signals", Order = 4, GroupName = "Execution")]
        public int MinBarsBetweenSignals { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "ATR Period", Order = 1, GroupName = "Risk")]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.10, 20.0)]
        [Display(Name = "Stop ATR Multiple", Order = 2, GroupName = "Risk")]
        public double StopAtrMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0.10, 20.0)]
        [Display(Name = "TP1 ATR Multiple", Order = 3, GroupName = "Risk")]
        public double TargetAtrMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0.10, 30.0)]
        [Display(Name = "TP2 ATR Multiple", Order = 4, GroupName = "Risk")]
        public double RunnerTargetAtrMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Min Stop Ticks", Order = 5, GroupName = "Risk")]
        public int MinStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Min Target Ticks", Order = 6, GroupName = "Risk")]
        public int MinTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ATR Trailing Stop", Order = 7, GroupName = "Risk")]
        public bool EnableAtrTrailingStop { get; set; }

        [NinjaScriptProperty]
        [Range(0.10, 20.0)]
        [Display(Name = "Trailing ATR Multiple", Order = 8, GroupName = "Risk")]
        public double TrailingAtrMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Min Trail Ticks", Order = 9, GroupName = "Risk")]
        public int MinTrailTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Delay Trail Until H Retest", Order = 10, GroupName = "Risk")]
        public bool DelayTrailUntilHRetestBreak { get; set; }

        [NinjaScriptProperty]
        [Range(0.10, 10.0)]
        [Display(Name = "I Pullback ATR Multiple", Order = 11, GroupName = "Risk")]
        public double TrailRetestPullbackAtrMultiple { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Progressive Trail", Order = 12, GroupName = "Risk")]
        public bool EnableProgressiveTrailTightening { get; set; }

        [NinjaScriptProperty]
        [Range(0.00, 2.0)]
        [Display(Name = "Trail Tighten Per ATR", Order = 13, GroupName = "Risk")]
        public double ProgressiveTrailTightenPerAtr { get; set; }

        [NinjaScriptProperty]
        [Range(0.10, 20.0)]
        [Display(Name = "Min Trail ATR Multiple", Order = 14, GroupName = "Risk")]
        public double ProgressiveTrailMinAtrMultiple { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 1, GroupName = "Filters")]
        public bool EnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "ADX Period", Order = 2, GroupName = "Filters")]
        public int AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(5.0, 80.0)]
        [Display(Name = "Min ADX For Entries", Order = 3, GroupName = "Filters")]
        public double MinAdxForEntries { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "A-G structural reversal strategy with stop behind E, ATR TP1/TP2, and ATR trailing stop.";
                Name = "GlitchBomsStrategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 2;
                EntryHandling = EntryHandling.UniqueEntries;
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
                BarsRequiredToTrade = 10;
                IsInstantiatedOnEachOptimizationIteration = false;

                PivotStrength = 2;
                BreakBufferPoints = 0.25;
                RequirePullbackPivot = true;
                DrawPivotLabels = true;
                DrawStructureLines = true;
                LabelOffsetTicks = 4;
                SetupExpiryBars = 12;
                RequireBreakOfFForEntry = false;
                StopBehindEPaddingPoints = 0.50;

                PrimaryQuantity = 1;
                RunnerQuantity = 1;
                AllowSignalFlip = false;
                MinBarsBetweenSignals = 2;

                AtrPeriod = 14;
                StopAtrMultiple = 1.00;
                TargetAtrMultiple = 1.00;
                RunnerTargetAtrMultiple = 5.00;
                MinStopTicks = 4;
                MinTargetTicks = 4;
                EnableAtrTrailingStop = true;
                TrailingAtrMultiple = 1.00;
                MinTrailTicks = 4;
                DelayTrailUntilHRetestBreak = true;
                TrailRetestPullbackAtrMultiple = 0.50;
                EnableProgressiveTrailTightening = true;
                ProgressiveTrailTightenPerAtr = 0.15;
                ProgressiveTrailMinAtrMultiple = 0.40;

                EnableAdxFilter = true;
                AdxPeriod = 14;
                MinAdxForEntries = 25.0;
            }
            else if (State == State.DataLoaded)
            {
                _atr = ATR(AtrPeriod);
                _adx = ADX(AdxPeriod);

                _tickSize =
                    Instrument != null &&
                    Instrument.MasterInstrument != null &&
                    Instrument.MasterInstrument.TickSize > 0
                        ? Instrument.MasterInstrument.TickSize
                        : 0.25;
                _lastPivot = null;
                _swingPivots.Clear();
                _pivotSequence = 0;
                _lastSignalBarIndex = -1;
                _pendingSetupDirection = SetupDirection.None;
                _pendingSetupBar = -1;
                _lastArmedLongGBarIndex = -1;
                _lastArmedShortGBarIndex = -1;
                _pendingA = default(PivotPoint);
                _pendingB = default(PivotPoint);
                _pendingE = default(PivotPoint);
                _pendingF = default(PivotPoint);
                _pendingG = default(PivotPoint);
                _lastSubmittedLongStopTicks = 0;
                _lastSubmittedShortStopTicks = 0;
                _lastSubmittedLongTarget1Ticks = 0;
                _lastSubmittedShortTarget1Ticks = 0;
                _lastSubmittedLongTrailTicks = 0;
                _lastSubmittedShortTrailTicks = 0;
                _trailingInitialized = false;
                _trailingPosition = MarketPosition.Flat;
                _activeStopPrice = 0;
                _extremePriceSinceEntry = 0;
                _trailDistancePoints = 0;
                _trailEntryPrice = 0;
                _trailActivationReady = !DelayTrailUntilHRetestBreak;
                _trailTp1Touched = false;
                _trailPullbackSeen = false;
                _trailHReferencePrice = 0;

                Print(
                    "GlitchBomsStrategy A-G rules | pivot strength: " +
                    PivotStrength.ToString(CultureInfo.InvariantCulture) +
                    " | stop ATRx: " +
                    StopAtrMultiple.ToString("0.##", CultureInfo.InvariantCulture) +
                    " | TP1 ATRx: " +
                    TargetAtrMultiple.ToString("0.##", CultureInfo.InvariantCulture) +
                    " | TP2 ATRx: " +
                    RunnerTargetAtrMultiple.ToString("0.##", CultureInfo.InvariantCulture) +
                    " | trail ATRx: " +
                    (EnableAtrTrailingStop
                        ? TrailingAtrMultiple.ToString("0.##", CultureInfo.InvariantCulture)
                        : "off") +
                    " | break F required: " +
                    RequireBreakOfFForEntry.ToString(CultureInfo.InvariantCulture) +
                    " | delayed trail: " +
                    DelayTrailUntilHRetestBreak.ToString(CultureInfo.InvariantCulture) +
                    " | progressive trail: " +
                    EnableProgressiveTrailTightening.ToString(CultureInfo.InvariantCulture) +
                    " | ADX filter: " +
                    (EnableAdxFilter ? ("on >= " + MinAdxForEntries.ToString("0.##", CultureInfo.InvariantCulture)) : "off"));
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            int indicatorBarsNeeded = Math.Max(AtrPeriod, EnableAdxFilter ? AdxPeriod : 0) + 2;
            int minBarsNeeded = Math.Max(BarsRequiredToTrade, Math.Max((PivotStrength * 2) + 2, indicatorBarsNeeded));
            if (CurrentBar < minBarsNeeded)
                return;

            PivotPoint pivot;
            bool addedAlternatingPivot = TryRegisterPivotAtOffset(PivotStrength, out pivot);
            if (addedAlternatingPivot)
                TryArmPatternSetupFromRecentPivots();

            ProcessPendingSetupEntry();
            UpdateAtrTrailingStop();
        }

        private bool TryRegisterPivotAtOffset(int barsAgo, out PivotPoint pivot)
        {
            if (!TryBuildPivot(barsAgo, out pivot))
                return false;

            if (_lastPivot.HasValue && _lastPivot.Value.BarIndex == pivot.BarIndex)
                return false;

            _pivotSequence++;
            pivot.Sequence = _pivotSequence;
            pivot.Label = ToAlphabetLabel(_pivotSequence);

            if (DrawStructureLines && _lastPivot.HasValue)
            {
                PivotPoint previous = _lastPivot.Value;
                int startBarsAgo = CurrentBar - previous.BarIndex;
                int endBarsAgo = CurrentBar - pivot.BarIndex;
                Draw.Line(
                    this,
                    "BOMS_LINE_" + pivot.BarIndex.ToString(CultureInfo.InvariantCulture),
                    false,
                    startBarsAgo,
                    previous.Price,
                    endBarsAgo,
                    pivot.Price,
                    Brushes.DimGray,
                    NinjaTrader.Gui.DashStyleHelper.Solid,
                    1);
            }

            if (DrawPivotLabels)
                DrawPivotLabel(pivot);

            _lastPivot = pivot;
            return AddOrReplaceSwingPivot(pivot);
        }

        private bool AddOrReplaceSwingPivot(PivotPoint pivot)
        {
            int lastIndex = _swingPivots.Count - 1;
            if (lastIndex >= 0 && _swingPivots[lastIndex].Type == pivot.Type)
            {
                PivotPoint previous = _swingPivots[lastIndex];
                bool replace =
                    (pivot.Type == PivotType.High && pivot.Price > previous.Price) ||
                    (pivot.Type == PivotType.Low && pivot.Price < previous.Price);

                if (replace)
                    _swingPivots[lastIndex] = pivot;

                return false;
            }

            _swingPivots.Add(pivot);
            if (_swingPivots.Count > MaxStoredSwingPivots)
                _swingPivots.RemoveAt(0);

            return true;
        }

        private void TryArmPatternSetupFromRecentPivots()
        {
            if (_swingPivots.Count < 7)
                return;

            int n = _swingPivots.Count;
            PivotPoint a = _swingPivots[n - 7];
            PivotPoint b = _swingPivots[n - 6];
            PivotPoint c = _swingPivots[n - 5];
            PivotPoint d = _swingPivots[n - 4];
            PivotPoint e = _swingPivots[n - 3];
            PivotPoint f = _swingPivots[n - 2];
            PivotPoint g = _swingPivots[n - 1];

            if (MatchesLongReversalPattern(a, b, c, d, e, f, g))
            {
                if (g.BarIndex == _lastArmedLongGBarIndex)
                    return;
                _lastArmedLongGBarIndex = g.BarIndex;
                ArmPendingSetup(SetupDirection.Long, a, b, e, f, g);
                return;
            }

            if (MatchesShortReversalPattern(a, b, c, d, e, f, g))
            {
                if (g.BarIndex == _lastArmedShortGBarIndex)
                    return;
                _lastArmedShortGBarIndex = g.BarIndex;
                ArmPendingSetup(SetupDirection.Short, a, b, e, f, g);
            }
        }

        private bool MatchesLongReversalPattern(
            PivotPoint a,
            PivotPoint b,
            PivotPoint c,
            PivotPoint d,
            PivotPoint e,
            PivotPoint f,
            PivotPoint g)
        {
            if (a.Type != PivotType.Low || b.Type != PivotType.High || c.Type != PivotType.Low || d.Type != PivotType.High || e.Type != PivotType.Low || f.Type != PivotType.High || g.Type != PivotType.Low)
                return false;

            double eps = Math.Max(0, BreakBufferPoints * 0.25);
            bool cLowerThanA = c.Price < (a.Price - eps);
            bool dLowerThanB = d.Price < (b.Price - eps);
            bool eLowerThanC = e.Price < (c.Price - eps);
            bool fLowerThanD = f.Price < (d.Price - eps);
            bool gHigherThanE = g.Price > (e.Price + eps);
            bool gBelowF = g.Price < (f.Price - eps);

            if (!cLowerThanA || !dLowerThanB || !eLowerThanC || !fLowerThanD || !gHigherThanE)
                return false;

            return !RequirePullbackPivot || gBelowF;
        }

        private bool MatchesShortReversalPattern(
            PivotPoint a,
            PivotPoint b,
            PivotPoint c,
            PivotPoint d,
            PivotPoint e,
            PivotPoint f,
            PivotPoint g)
        {
            if (a.Type != PivotType.High || b.Type != PivotType.Low || c.Type != PivotType.High || d.Type != PivotType.Low || e.Type != PivotType.High || f.Type != PivotType.Low || g.Type != PivotType.High)
                return false;

            double eps = Math.Max(0, BreakBufferPoints * 0.25);
            bool cHigherThanA = c.Price > (a.Price + eps);
            bool dHigherThanB = d.Price > (b.Price + eps);
            bool eHigherThanC = e.Price > (c.Price + eps);
            bool fHigherThanD = f.Price > (d.Price + eps);
            bool gLowerThanE = g.Price < (e.Price - eps);
            bool gAboveF = g.Price > (f.Price + eps);

            if (!cHigherThanA || !dHigherThanB || !eHigherThanC || !fHigherThanD || !gLowerThanE)
                return false;

            return !RequirePullbackPivot || gAboveF;
        }

        private void ArmPendingSetup(
            SetupDirection direction,
            PivotPoint a,
            PivotPoint b,
            PivotPoint e,
            PivotPoint f,
            PivotPoint g)
        {
            _pendingSetupDirection = direction;
            _pendingA = a;
            _pendingB = b;
            _pendingE = e;
            _pendingF = f;
            _pendingG = g;
            _pendingSetupBar = CurrentBar;

            DrawSetupMarker(direction, a, b, e, f, g);
        }

        private void ProcessPendingSetupEntry()
        {
            if (_pendingSetupDirection == SetupDirection.None)
                return;

            if (_lastSignalBarIndex >= 0 && (CurrentBar - _lastSignalBarIndex) < MinBarsBetweenSignals)
                return;

            if (CurrentBar - _pendingSetupBar > SetupExpiryBars)
            {
                ClearPendingSetup();
                return;
            }

            if (EnableAdxFilter && (_adx == null || _adx[0] < MinAdxForEntries))
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (!AllowSignalFlip)
                    return;

                if (_pendingSetupDirection == SetupDirection.Long && Position.MarketPosition == MarketPosition.Short)
                {
                    ExitShort("BOMS-FlipExit-S1", ShortSignalName);
                    if (RunnerQuantity > 0)
                        ExitShort("BOMS-FlipExit-S2", ShortRunnerSignalName);
                }
                else if (_pendingSetupDirection == SetupDirection.Short && Position.MarketPosition == MarketPosition.Long)
                {
                    ExitLong("BOMS-FlipExit-L1", LongSignalName);
                    if (RunnerQuantity > 0)
                        ExitLong("BOMS-FlipExit-L2", LongRunnerSignalName);
                }

                return;
            }

            if (_pendingSetupDirection == SetupDirection.Long)
            {
                double invalidationPrice = _pendingE.Price - StopBehindEPaddingPoints;
                if (Low[0] <= invalidationPrice)
                {
                    ClearPendingSetup();
                    return;
                }

                bool tinyFlip = Close[0] > Open[0] && Close[0] > Close[1];
                bool breakF = Close[0] > (_pendingF.Price + BreakBufferPoints);
                if (tinyFlip && (!RequireBreakOfFForEntry || breakF))
                    SubmitPatternEntry(true);

                return;
            }

            double shortInvalidationPrice = _pendingE.Price + StopBehindEPaddingPoints;
            if (High[0] >= shortInvalidationPrice)
            {
                ClearPendingSetup();
                return;
            }

            bool shortFlip = Close[0] < Open[0] && Close[0] < Close[1];
            bool shortBreakF = Close[0] < (_pendingF.Price - BreakBufferPoints);
            if (shortFlip && (!RequireBreakOfFForEntry || shortBreakF))
                SubmitPatternEntry(false);
        }

        private void SubmitPatternEntry(bool isLong)
        {
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            double tick = ResolveTickSize();
            double entryReferencePrice = Close[0];

            int stopTicksAtr = ResolveAtrRiskTicks(StopAtrMultiple, MinStopTicks);
            int tp1Ticks = ResolveAtrRiskTicks(TargetAtrMultiple, MinTargetTicks);
            int tp2Ticks = ResolveAtrRiskTicks(RunnerTargetAtrMultiple, MinTargetTicks);
            int trailTicks = ResolveAtrRiskTicks(TrailingAtrMultiple, MinTrailTicks);

            double atrStopPrice = isLong
                ? entryReferencePrice - (stopTicksAtr * tick)
                : entryReferencePrice + (stopTicksAtr * tick);
            double structureStopPrice = isLong
                ? _pendingE.Price - StopBehindEPaddingPoints
                : _pendingE.Price + StopBehindEPaddingPoints;
            double selectedStopPrice = isLong
                ? Math.Min(atrStopPrice, structureStopPrice)
                : Math.Max(atrStopPrice, structureStopPrice);

            selectedStopPrice = RoundToTick(selectedStopPrice);
            int finalStopTicks = Math.Max(1, PointsToTicks(Math.Abs(entryReferencePrice - selectedStopPrice)));

            if (isLong)
            {
                _lastSubmittedLongStopTicks = finalStopTicks;
                _lastSubmittedLongTarget1Ticks = tp1Ticks;
                _lastSubmittedLongTrailTicks = trailTicks;

                SetStopLoss(LongSignalName, CalculationMode.Price, selectedStopPrice, false);
                SetProfitTarget(LongSignalName, CalculationMode.Ticks, tp1Ticks);
                EnterLong(Math.Max(1, PrimaryQuantity), LongSignalName);

                if (RunnerQuantity > 0)
                {
                    SetStopLoss(LongRunnerSignalName, CalculationMode.Price, selectedStopPrice, false);
                    SetProfitTarget(LongRunnerSignalName, CalculationMode.Ticks, tp2Ticks);
                    EnterLong(RunnerQuantity, LongRunnerSignalName);
                }
            }
            else
            {
                _lastSubmittedShortStopTicks = finalStopTicks;
                _lastSubmittedShortTarget1Ticks = tp1Ticks;
                _lastSubmittedShortTrailTicks = trailTicks;

                SetStopLoss(ShortSignalName, CalculationMode.Price, selectedStopPrice, false);
                SetProfitTarget(ShortSignalName, CalculationMode.Ticks, tp1Ticks);
                EnterShort(Math.Max(1, PrimaryQuantity), ShortSignalName);

                if (RunnerQuantity > 0)
                {
                    SetStopLoss(ShortRunnerSignalName, CalculationMode.Price, selectedStopPrice, false);
                    SetProfitTarget(ShortRunnerSignalName, CalculationMode.Ticks, tp2Ticks);
                    EnterShort(RunnerQuantity, ShortRunnerSignalName);
                }
            }

            DrawEntryMarker(isLong, selectedStopPrice);
            _lastSignalBarIndex = CurrentBar;
            ClearPendingSetup();
        }

        private void ClearPendingSetup()
        {
            _pendingSetupDirection = SetupDirection.None;
            _pendingA = default(PivotPoint);
            _pendingB = default(PivotPoint);
            _pendingE = default(PivotPoint);
            _pendingF = default(PivotPoint);
            _pendingG = default(PivotPoint);
            _pendingSetupBar = -1;
        }

        private void DrawSetupMarker(
            SetupDirection direction,
            PivotPoint a,
            PivotPoint b,
            PivotPoint e,
            PivotPoint f,
            PivotPoint g)
        {
            int barsAgo = CurrentBar - g.BarIndex;
            if (barsAgo < 0)
                return;

            string tagBase = "BOMS_SETUP_" + g.BarIndex.ToString(CultureInfo.InvariantCulture) + "_" + direction.ToString();
            Brush setupBrush = direction == SetupDirection.Long ? Brushes.LimeGreen : Brushes.OrangeRed;
            double y = direction == SetupDirection.Long
                ? g.Price - (ResolveTickSize() * LabelOffsetTicks)
                : g.Price + (ResolveTickSize() * LabelOffsetTicks);

            Draw.Text(
                this,
                tagBase + "_txt",
                direction.ToString().ToUpperInvariant() + " G setup | A:" + a.Label + " B:" + b.Label + " E:" + e.Label + " F:" + f.Label + " G:" + g.Label,
                barsAgo,
                y,
                setupBrush);

            if (DrawStructureLines)
            {
                Draw.HorizontalLine(this, tagBase + "_SR_A", a.Price, Brushes.DimGray);
                Draw.HorizontalLine(this, tagBase + "_SR_B", b.Price, Brushes.SlateGray);
            }
        }

        private void DrawEntryMarker(bool isLong, double stopPrice)
        {
            string tagBase = "BOMS_ENTRY_" + CurrentBar.ToString(CultureInfo.InvariantCulture);
            double y = isLong
                ? Low[0] - (ResolveTickSize() * 2)
                : High[0] + (ResolveTickSize() * 2);
            Brush brush = isLong ? Brushes.LimeGreen : Brushes.OrangeRed;

            if (isLong)
                Draw.ArrowUp(this, tagBase + "_arr", false, 0, y, brush);
            else
                Draw.ArrowDown(this, tagBase + "_arr", false, 0, y, brush);

            Draw.Text(
                this,
                tagBase + "_txt",
                (isLong ? "Long G" : "Short G") + " | stop " + stopPrice.ToString("0.##", CultureInfo.InvariantCulture),
                0,
                isLong ? y - (ResolveTickSize() * 2) : y + (ResolveTickSize() * 2),
                brush);
        }

        private void DrawPivotLabel(PivotPoint pivot)
        {
            int barsAgo = CurrentBar - pivot.BarIndex;
            if (barsAgo < 0)
                return;

            double y =
                pivot.Type == PivotType.High
                    ? pivot.Price + (_tickSize * LabelOffsetTicks)
                    : pivot.Price - (_tickSize * LabelOffsetTicks);
            Brush brush = pivot.Type == PivotType.High ? Brushes.Gold : Brushes.DeepSkyBlue;
            string text =
                pivot.Label +
                " " +
                (pivot.Type == PivotType.High ? "H " : "L ") +
                pivot.Price.ToString("0.##", CultureInfo.InvariantCulture);
            Draw.Text(
                this,
                "BOMS_PIVOT_" + pivot.BarIndex.ToString(CultureInfo.InvariantCulture),
                text,
                barsAgo,
                y,
                brush);
        }

        private bool TryBuildPivot(int barsAgo, out PivotPoint pivot)
        {
            pivot = default(PivotPoint);
            if (barsAgo < PivotStrength)
                return false;
            if (CurrentBar < (PivotStrength * 2) + 1)
                return false;

            bool isHigh = IsPivotHigh(barsAgo);
            bool isLow = IsPivotLow(barsAgo);
            if (isHigh == isLow)
                return false;

            pivot.Sequence = _pivotSequence;
            pivot.BarIndex = CurrentBar - barsAgo;
            pivot.Type = isHigh ? PivotType.High : PivotType.Low;
            pivot.Price = isHigh ? High[barsAgo] : Low[barsAgo];
            pivot.Label = string.Empty;
            return true;
        }

        private bool IsPivotHigh(int barsAgo)
        {
            double candidate = High[barsAgo];
            for (int i = 1; i <= PivotStrength; i++)
            {
                if (High[barsAgo + i] >= candidate)
                    return false;
                if (High[barsAgo - i] > candidate)
                    return false;
            }
            return true;
        }

        private bool IsPivotLow(int barsAgo)
        {
            double candidate = Low[barsAgo];
            for (int i = 1; i <= PivotStrength; i++)
            {
                if (Low[barsAgo + i] <= candidate)
                    return false;
                if (Low[barsAgo - i] < candidate)
                    return false;
            }
            return true;
        }

        private int PointsToTicks(double points)
        {
            double tick = _tickSize;
            if (tick <= 0 && TickSize > 0)
                tick = TickSize;
            if (tick <= 0)
                tick = 0.25;

            int ticks = (int)Math.Round(points / tick, MidpointRounding.AwayFromZero);
            return Math.Max(1, ticks);
        }

        private int ResolveAtrRiskTicks(double atrMultiple, int minTicks)
        {
            int minimum = Math.Max(1, minTicks);
            if (_atr == null)
                return minimum;

            double atrPoints = _atr[0];
            if (double.IsNaN(atrPoints) || double.IsInfinity(atrPoints) || atrPoints <= 0)
                return minimum;

            int atrTicks = PointsToTicks(atrPoints * atrMultiple);
            return Math.Max(minimum, atrTicks);
        }

        private void UpdateAtrTrailingStop()
        {
            if (!EnableAtrTrailingStop)
            {
                if (Position.MarketPosition == MarketPosition.Flat)
                    ResetTrailingState();
                return;
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ResetTrailingState();
                return;
            }

            EnsureTrailingState();
            if (!_trailingInitialized)
                return;

            double tick = ResolveTickSize();
            if (Position.MarketPosition == MarketPosition.Long)
            {
                _extremePriceSinceEntry = Math.Max(_extremePriceSinceEntry, High[0]);
                _trailDistancePoints = ResolveDynamicTrailDistancePoints(MarketPosition.Long, tick);
                if (DelayTrailUntilHRetestBreak)
                {
                    UpdateDelayedTrailActivationState(MarketPosition.Long, tick);
                    if (!_trailActivationReady)
                        return;
                }

                double candidate = RoundToTick(_extremePriceSinceEntry - _trailDistancePoints);
                double nextStop = Math.Max(_activeStopPrice, candidate);
                if (nextStop > (_activeStopPrice + (tick * 0.25)))
                {
                    _activeStopPrice = nextStop;
                    ApplyStopPriceForDirection(MarketPosition.Long, _activeStopPrice);
                }
                return;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                _extremePriceSinceEntry = Math.Min(_extremePriceSinceEntry, Low[0]);
                _trailDistancePoints = ResolveDynamicTrailDistancePoints(MarketPosition.Short, tick);
                if (DelayTrailUntilHRetestBreak)
                {
                    UpdateDelayedTrailActivationState(MarketPosition.Short, tick);
                    if (!_trailActivationReady)
                        return;
                }

                double candidate = RoundToTick(_extremePriceSinceEntry + _trailDistancePoints);
                double nextStop = Math.Min(_activeStopPrice, candidate);
                if (nextStop < (_activeStopPrice - (tick * 0.25)))
                {
                    _activeStopPrice = nextStop;
                    ApplyStopPriceForDirection(MarketPosition.Short, _activeStopPrice);
                }
            }
        }

        private void EnsureTrailingState()
        {
            MarketPosition current = Position.MarketPosition;
            if (_trailingInitialized && _trailingPosition == current)
                return;

            double tick = ResolveTickSize();
            double entryPrice = Position.AveragePrice;
            if (entryPrice <= 0)
                return;

            if (current == MarketPosition.Long)
            {
                int stopTicks = Math.Max(1, _lastSubmittedLongStopTicks > 0 ? _lastSubmittedLongStopTicks : ResolveAtrRiskTicks(StopAtrMultiple, MinStopTicks));
                int trailTicks = Math.Max(1, _lastSubmittedLongTrailTicks > 0 ? _lastSubmittedLongTrailTicks : ResolveAtrRiskTicks(TrailingAtrMultiple, MinTrailTicks));
                _trailDistancePoints = trailTicks * tick;
                _activeStopPrice = RoundToTick(entryPrice - (stopTicks * tick));
                _extremePriceSinceEntry = Math.Max(entryPrice, High[0]);
                _trailEntryPrice = entryPrice;
                _trailActivationReady = !DelayTrailUntilHRetestBreak;
                _trailTp1Touched = false;
                _trailPullbackSeen = false;
                _trailHReferencePrice = entryPrice;
                ApplyStopPriceForDirection(MarketPosition.Long, _activeStopPrice);
            }
            else if (current == MarketPosition.Short)
            {
                int stopTicks = Math.Max(1, _lastSubmittedShortStopTicks > 0 ? _lastSubmittedShortStopTicks : ResolveAtrRiskTicks(StopAtrMultiple, MinStopTicks));
                int trailTicks = Math.Max(1, _lastSubmittedShortTrailTicks > 0 ? _lastSubmittedShortTrailTicks : ResolveAtrRiskTicks(TrailingAtrMultiple, MinTrailTicks));
                _trailDistancePoints = trailTicks * tick;
                _activeStopPrice = RoundToTick(entryPrice + (stopTicks * tick));
                _extremePriceSinceEntry = Math.Min(entryPrice, Low[0]);
                _trailEntryPrice = entryPrice;
                _trailActivationReady = !DelayTrailUntilHRetestBreak;
                _trailTp1Touched = false;
                _trailPullbackSeen = false;
                _trailHReferencePrice = entryPrice;
                ApplyStopPriceForDirection(MarketPosition.Short, _activeStopPrice);
            }
            else
            {
                return;
            }

            _trailingPosition = current;
            _trailingInitialized = true;
        }

        private void ApplyStopPriceForDirection(MarketPosition direction, double price)
        {
            if (direction == MarketPosition.Long)
            {
                SetStopLoss(LongSignalName, CalculationMode.Price, price, false);
                if (RunnerQuantity > 0)
                    SetStopLoss(LongRunnerSignalName, CalculationMode.Price, price, false);
                return;
            }

            if (direction == MarketPosition.Short)
            {
                SetStopLoss(ShortSignalName, CalculationMode.Price, price, false);
                if (RunnerQuantity > 0)
                    SetStopLoss(ShortRunnerSignalName, CalculationMode.Price, price, false);
            }
        }

        private double ResolveDynamicTrailDistancePoints(MarketPosition direction, double tick)
        {
            double atrPoints = (_atr != null && !double.IsNaN(_atr[0]) && !double.IsInfinity(_atr[0]) && _atr[0] > 0)
                ? _atr[0]
                : (tick * Math.Max(1, MinTrailTicks));

            double effectiveMultiple = TrailingAtrMultiple;
            if (EnableProgressiveTrailTightening)
            {
                double favorablePoints =
                    direction == MarketPosition.Long
                        ? Math.Max(0, _extremePriceSinceEntry - _trailEntryPrice)
                        : Math.Max(0, _trailEntryPrice - _extremePriceSinceEntry);
                double favorableAtr = atrPoints > 1e-8 ? favorablePoints / atrPoints : 0;
                effectiveMultiple = Math.Max(
                    ProgressiveTrailMinAtrMultiple,
                    TrailingAtrMultiple - (favorableAtr * ProgressiveTrailTightenPerAtr));
            }

            double minimumPoints = tick * Math.Max(1, MinTrailTicks);
            return Math.Max(minimumPoints, atrPoints * Math.Max(0.05, effectiveMultiple));
        }

        private void UpdateDelayedTrailActivationState(MarketPosition direction, double tick)
        {
            if (_trailActivationReady || !DelayTrailUntilHRetestBreak)
                return;

            double atrPoints = (_atr != null && !double.IsNaN(_atr[0]) && !double.IsInfinity(_atr[0]) && _atr[0] > 0)
                ? _atr[0]
                : tick;
            double pullbackPoints = Math.Max(tick, atrPoints * TrailRetestPullbackAtrMultiple);
            int tp1Ticks = direction == MarketPosition.Long ? _lastSubmittedLongTarget1Ticks : _lastSubmittedShortTarget1Ticks;
            tp1Ticks = Math.Max(1, tp1Ticks);

            double tp1Level = direction == MarketPosition.Long
                ? _trailEntryPrice + (tp1Ticks * tick)
                : _trailEntryPrice - (tp1Ticks * tick);

            if (!_trailTp1Touched)
            {
                bool hitTp1 = direction == MarketPosition.Long
                    ? High[0] >= tp1Level
                    : Low[0] <= tp1Level;

                if (hitTp1)
                {
                    _trailTp1Touched = true;
                    _trailHReferencePrice = direction == MarketPosition.Long ? High[0] : Low[0];
                }
                return;
            }

            if (!_trailPullbackSeen)
            {
                if (direction == MarketPosition.Long)
                {
                    _trailHReferencePrice = Math.Max(_trailHReferencePrice, High[0]);
                    if (Low[0] <= (_trailHReferencePrice - pullbackPoints))
                        _trailPullbackSeen = true;
                }
                else
                {
                    _trailHReferencePrice = Math.Min(_trailHReferencePrice, Low[0]);
                    if (High[0] >= (_trailHReferencePrice + pullbackPoints))
                        _trailPullbackSeen = true;
                }
                return;
            }

            bool brokeHAgain = direction == MarketPosition.Long
                ? High[0] > (_trailHReferencePrice + (tick * 0.25))
                : Low[0] < (_trailHReferencePrice - (tick * 0.25));

            if (brokeHAgain)
                _trailActivationReady = true;
        }

        private void ResetTrailingState()
        {
            _trailingInitialized = false;
            _trailingPosition = MarketPosition.Flat;
            _activeStopPrice = 0;
            _extremePriceSinceEntry = 0;
            _trailDistancePoints = 0;
            _trailEntryPrice = 0;
            _trailActivationReady = !DelayTrailUntilHRetestBreak;
            _trailTp1Touched = false;
            _trailPullbackSeen = false;
            _trailHReferencePrice = 0;
            _lastSubmittedLongStopTicks = 0;
            _lastSubmittedShortStopTicks = 0;
            _lastSubmittedLongTarget1Ticks = 0;
            _lastSubmittedShortTarget1Ticks = 0;
            _lastSubmittedLongTrailTicks = 0;
            _lastSubmittedShortTrailTicks = 0;
        }

        private double ResolveTickSize()
        {
            if (_tickSize > 0)
                return _tickSize;
            if (TickSize > 0)
                return TickSize;
            return 0.25;
        }

        private double RoundToTick(double price)
        {
            if (Instrument != null && Instrument.MasterInstrument != null)
                return Instrument.MasterInstrument.RoundToTickSize(price);

            double tick = ResolveTickSize();
            return Math.Round(price / tick, MidpointRounding.AwayFromZero) * tick;
        }

        private static string ToAlphabetLabel(int sequence)
        {
            if (sequence <= 0)
                return "A";

            int value = sequence - 1;
            StringBuilder label = new StringBuilder();
            while (value >= 0)
            {
                int remainder = value % 26;
                label.Insert(0, (char)('A' + remainder));
                value = (value / 26) - 1;
            }

            return label.ToString();
        }
    }
}
