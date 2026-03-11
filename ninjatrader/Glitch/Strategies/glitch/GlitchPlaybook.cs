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
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    // Phase 1: single-play archetype strategy (BOMS reversal only).
    public class GlitchPlaybook : Strategy
    {
        private const string LongSignalName = "LONG_G_TP1";
        private const string LongRunnerSignalName = "LONG_G_TP2";
        private const string ShortSignalName = "SHORT_G_TP1";
        private const string ShortRunnerSignalName = "SHORT_G_TP2";
        private const int MaxStoredSwingPivots = 140;

        private const int PivotStrength = 2;
        private const double BreakBufferPoints = 0.25;
        private const bool RequirePullbackPivot = true;
        private const int SetupExpiryBars = 12;
        private const bool RequireBreakOfFForEntry = false;
        private const int MinBarsBetweenSignals = 2;
        private const int LabelOffsetTicks = 4;
        private const int AtrPeriod = 14;
        private const int MinStopTicks = 4;
        private const int MinTargetTicks = 4;
        private const int MinTrailTicks = 4;
        private const int PrimaryQuantity = 1;
        private const int RunnerQuantity = 1;
        private const bool DrawAllPivotLetters = false;
        private const double TrailPullbackAtrMultiple = 0.50;
        private const double ProgressiveTrailTightenPerAtr = 0.12;
        private const double ProgressiveTrailMinAtrMultiple = 0.35;
        private const int BreakEvenPlusTicks = 1;

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
        private ATR _atr;

        private Nullable<PivotPoint> _lastPivot;
        private readonly List<PivotPoint> _swingPivots = new List<PivotPoint>();
        private int _pivotSequence;
        private int _lastSignalBarIndex;
        private int _lastArmedLongGBarIndex;
        private int _lastArmedShortGBarIndex;

        private SetupDirection _pendingSetupDirection;
        private PivotPoint _pendingA;
        private PivotPoint _pendingB;
        private PivotPoint _pendingC;
        private PivotPoint _pendingD;
        private PivotPoint _pendingE;
        private PivotPoint _pendingF;
        private PivotPoint _pendingG;
        private int _pendingSetupBar;

        private bool _trailingInitialized;
        private MarketPosition _trailingPosition;
        private double _activeStopPrice;
        private double _extremePriceSinceEntry;
        private double _trailDistancePoints;
        private int _lastSubmittedLongTrailTicks;
        private int _lastSubmittedShortTrailTicks;
        private double _lastLongEntryStopPrice;
        private double _lastShortEntryStopPrice;
        private bool _trailHasMovedLong;
        private bool _trailHasMovedShort;
        private int _executionDrawCounter;
        private int _skipDrawCounter;
        private int _pendingSetupConfidence;
        private double _trailEntryPrice;
        private bool _trailTp1Reached;
        private bool _trailPullbackSeen;
        private bool _trailActivated;
        private double _trailHRefPrice;
        private double _trailIRefPrice;

        [NinjaScriptProperty]
        [Display(Name = "Enable Reversal Setup", Order = 1, GroupName = "Reversal Setup")]
        public bool EnableReversalSetup { get; set; }

        [NinjaScriptProperty]
        [Range(-20.0, 20.0)]
        [Display(Name = "Reversal SL Offset From E (pts)", Order = 2, GroupName = "Reversal Setup")]
        public double ReversalStopOffsetPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.10, 20.0)]
        [Display(Name = "Reversal TP1 ATR Multiple", Order = 3, GroupName = "Reversal Setup")]
        public double ReversalTp1AtrMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0.10, 40.0)]
        [Display(Name = "Reversal TP2 ATR Multiple", Order = 4, GroupName = "Reversal Setup")]
        public double ReversalTp2AtrMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0.10, 20.0)]
        [Display(Name = "Reversal TSL ATR Multiple", Order = 5, GroupName = "Reversal Setup")]
        public double ReversalTslAtrMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Min Reversal Confidence %", Order = 6, GroupName = "Reversal Setup")]
        public int MinReversalConfidence { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 300.0)]
        [Display(Name = "Reversal Max Entry->SL Dist (pts)", Order = 7, GroupName = "Reversal Setup")]
        public double ReversalMaxEntryStopDistancePoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Reversal Min Pivot Leg (pts)", Order = 8, GroupName = "Reversal Setup")]
        public double ReversalMinPivotLegPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Reversal Min E->G Bounce (pts)", Order = 9, GroupName = "Reversal Setup")]
        public double ReversalMinEToGBouncePoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Reversal Choppiness Filter", Order = 10, GroupName = "Reversal Setup")]
        public bool UseReversalChoppinessFilter { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Reversal Choppiness Period", Order = 11, GroupName = "Reversal Setup")]
        public int ReversalChoppinessPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Reversal Max Choppiness", Order = 12, GroupName = "Reversal Setup")]
        public double ReversalMaxChoppiness { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "GlitchPlaybook phase 1: BOMS A-G reversal setup only.";
                Name = "GlitchPlaybook";
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

                EnableReversalSetup = true;
                ReversalStopOffsetPoints = 3.0;
                ReversalTp1AtrMultiple = 1.00;
                ReversalTp2AtrMultiple = 5.00;
                ReversalTslAtrMultiple = 1.00;
                MinReversalConfidence = 55;
                ReversalMaxEntryStopDistancePoints = 50.0;
                ReversalMinPivotLegPoints = 2.0;
                ReversalMinEToGBouncePoints = 1.0;
                UseReversalChoppinessFilter = true;
                ReversalChoppinessPeriod = 14;
                ReversalMaxChoppiness = 61.8;
            }
            else if (State == State.DataLoaded)
            {
                _atr = ATR(AtrPeriod);
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
                _lastArmedLongGBarIndex = -1;
                _lastArmedShortGBarIndex = -1;
                _pendingSetupDirection = SetupDirection.None;
                _pendingSetupBar = -1;
                _pendingA = default(PivotPoint);
                _pendingB = default(PivotPoint);
                _pendingC = default(PivotPoint);
                _pendingD = default(PivotPoint);
                _pendingE = default(PivotPoint);
                _pendingF = default(PivotPoint);
                _pendingG = default(PivotPoint);

                ResetTrailingState();
                _lastLongEntryStopPrice = 0;
                _lastShortEntryStopPrice = 0;
                _trailHasMovedLong = false;
                _trailHasMovedShort = false;
                _executionDrawCounter = 0;
                _skipDrawCounter = 0;
                _pendingSetupConfidence = 0;
                _trailEntryPrice = 0;
                _trailTp1Reached = false;
                _trailPullbackSeen = false;
                _trailActivated = false;
                _trailHRefPrice = 0;
                _trailIRefPrice = 0;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            int indicatorBarsNeeded = AtrPeriod + 2;
            int chopBarsNeeded = UseReversalChoppinessFilter ? ReversalChoppinessPeriod + 2 : 0;
            int minBarsNeeded = Math.Max(BarsRequiredToTrade, Math.Max((PivotStrength * 2) + 2, Math.Max(indicatorBarsNeeded, chopBarsNeeded)));
            if (CurrentBar < minBarsNeeded)
                return;

            PivotPoint pivot;
            bool addedAlternatingPivot = TryRegisterPivotAtOffset(PivotStrength, out pivot);
            if (addedAlternatingPivot && EnableReversalSetup)
                TryArmPatternSetupFromRecentPivots();

            if (EnableReversalSetup)
                ProcessPendingSetupEntry();

            UpdateAtrTrailingStop();
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

            string orderName = execution.Order.Name ?? string.Empty;
            string fromSignal = execution.Order.FromEntrySignal ?? string.Empty;
            OrderAction action = execution.Order.OrderAction;
            string label = string.Empty;
            Brush brush = Brushes.Gainsboro;

            bool isTp1Entry = string.Equals(orderName, LongSignalName, StringComparison.Ordinal) ||
                              string.Equals(orderName, ShortSignalName, StringComparison.Ordinal);
            bool isTp2Entry = string.Equals(orderName, LongRunnerSignalName, StringComparison.Ordinal) ||
                              string.Equals(orderName, ShortRunnerSignalName, StringComparison.Ordinal);
            bool isProfitTarget = orderName.IndexOf("Profit target", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isStopLoss = orderName.IndexOf("Stop loss", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isTp1Entry)
            {
                label = "TP1";
                brush = Brushes.Aqua;
            }
            else if (isTp2Entry)
            {
                label = "TP2";
                brush = Brushes.DeepSkyBlue;
            }
            else if (isProfitTarget)
            {
                bool fromTp1 = string.Equals(fromSignal, LongSignalName, StringComparison.Ordinal) ||
                               string.Equals(fromSignal, ShortSignalName, StringComparison.Ordinal);
                label = fromTp1 ? "TP1" : "TP2";
                brush = Brushes.LimeGreen;
            }
            else if (isStopLoss)
            {
                bool fromLong = string.Equals(fromSignal, LongSignalName, StringComparison.Ordinal) ||
                                string.Equals(fromSignal, LongRunnerSignalName, StringComparison.Ordinal);
                bool trailed = fromLong ? _trailHasMovedLong : _trailHasMovedShort;
                label = trailed ? "TSL" : "SL";
                brush = trailed ? Brushes.Gold : Brushes.OrangeRed;
            }

            if (string.IsNullOrWhiteSpace(label))
                return;

            double tick = ResolveTickSize();
            bool isSellSide = action == OrderAction.Sell || action == OrderAction.SellShort;
            double y = isSellSide ? price + (tick * 3) : price - (tick * 3);
            int barsAgo = CurrentBar >= 0 ? 0 : 0;

            _executionDrawCounter++;
            Draw.Text(
                this,
                "GPB_EXEC_" + _executionDrawCounter.ToString(CultureInfo.InvariantCulture),
                label,
                barsAgo,
                y,
                brush);
        }

        private bool TryRegisterPivotAtOffset(int barsAgo, out PivotPoint pivot)
        {
            if (!TryBuildPivot(barsAgo, out pivot))
                return false;

            if (_lastPivot.HasValue && _lastPivot.Value.BarIndex == pivot.BarIndex)
                return false;

            _pivotSequence++;
            pivot.Sequence = _pivotSequence;
            pivot.Label = string.Empty;

            if (_lastPivot.HasValue)
            {
                PivotPoint previous = _lastPivot.Value;
                int startBarsAgo = CurrentBar - previous.BarIndex;
                int endBarsAgo = CurrentBar - pivot.BarIndex;
                Draw.Line(
                    this,
                    "GPB_LINE_" + pivot.BarIndex.ToString(CultureInfo.InvariantCulture),
                    false,
                    startBarsAgo,
                    previous.Price,
                    endBarsAgo,
                    pivot.Price,
                    Brushes.DimGray,
                    NinjaTrader.Gui.DashStyleHelper.Solid,
                    1);
            }

            if (DrawAllPivotLetters)
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

            if (lastIndex >= 0 && ReversalMinPivotLegPoints > 0)
            {
                PivotPoint previousOpposite = _swingPivots[lastIndex];
                double legPoints = Math.Abs(pivot.Price - previousOpposite.Price);
                if (legPoints < ReversalMinPivotLegPoints)
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

                double choppinessLong;
                if (!PassesChoppinessFilter(out choppinessLong))
                {
                    DrawSkipMarkerAtPivot(g, true, "CHOP " + choppinessLong.ToString("0.0", CultureInfo.InvariantCulture));
                    return;
                }

                int confidence = ComputeReversalConfidence(a, b, c, d, e, f, g, true);
                if (confidence < MinReversalConfidence)
                    return;
                _lastArmedLongGBarIndex = g.BarIndex;
                ArmPendingSetup(SetupDirection.Long, a, b, c, d, e, f, g, confidence);
                return;
            }

            if (MatchesShortReversalPattern(a, b, c, d, e, f, g))
            {
                if (g.BarIndex == _lastArmedShortGBarIndex)
                    return;

                double choppinessShort;
                if (!PassesChoppinessFilter(out choppinessShort))
                {
                    DrawSkipMarkerAtPivot(g, false, "CHOP " + choppinessShort.ToString("0.0", CultureInfo.InvariantCulture));
                    return;
                }

                int confidence = ComputeReversalConfidence(a, b, c, d, e, f, g, false);
                if (confidence < MinReversalConfidence)
                    return;
                _lastArmedShortGBarIndex = g.BarIndex;
                ArmPendingSetup(SetupDirection.Short, a, b, c, d, e, f, g, confidence);
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
            double eToGBounce = g.Price - e.Price;

            if (!cLowerThanA || !dLowerThanB || !eLowerThanC || !fLowerThanD || !gHigherThanE)
                return false;

            if (ReversalMinEToGBouncePoints > 0 && eToGBounce < ReversalMinEToGBouncePoints)
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
            double eToGBounce = e.Price - g.Price;

            if (!cHigherThanA || !dHigherThanB || !eHigherThanC || !fHigherThanD || !gLowerThanE)
                return false;

            if (ReversalMinEToGBouncePoints > 0 && eToGBounce < ReversalMinEToGBouncePoints)
                return false;

            return !RequirePullbackPivot || gAboveF;
        }

        private int ComputeReversalConfidence(
            PivotPoint a,
            PivotPoint b,
            PivotPoint c,
            PivotPoint d,
            PivotPoint e,
            PivotPoint f,
            PivotPoint g,
            bool isLong)
        {
            double atr = ResolveAtrPoints();
            double tick = ResolveTickSize();
            double safeAtr = Math.Max(tick, atr);

            double impulse = isLong ? (f.Price - e.Price) : (e.Price - f.Price);
            double pullback = isLong ? (f.Price - g.Price) : (g.Price - f.Price);
            double reclaim = isLong ? (g.Price - e.Price) : (e.Price - g.Price);
            double trend = isLong ? (a.Price - e.Price) : (e.Price - a.Price);

            if (impulse <= tick || reclaim <= 0 || trend <= 0)
                return 0;

            double impulseScore = Clamp((impulse / safeAtr) / 1.6, 0, 1);
            double trendScore = Clamp((trend / safeAtr) / 2.5, 0, 1);
            double reclaimRatio = Clamp(reclaim / Math.Max(impulse, tick), 0, 1);

            double pullbackRatio = Clamp(pullback / Math.Max(impulse, tick), 0, 1.5);
            // Ideal pullback around 45% of impulse before continuation.
            double pullbackScore = 1.0 - Clamp(Math.Abs(pullbackRatio - 0.45) / 0.45, 0, 1);

            double structureMonotonic = 1.0;
            if (isLong)
            {
                if (!(c.Price < a.Price && e.Price < c.Price && d.Price < b.Price && f.Price < d.Price))
                    structureMonotonic = 0.0;
            }
            else
            {
                if (!(c.Price > a.Price && e.Price > c.Price && d.Price > b.Price && f.Price > d.Price))
                    structureMonotonic = 0.0;
            }

            double confidence01 =
                (impulseScore * 0.32) +
                (pullbackScore * 0.28) +
                (reclaimRatio * 0.22) +
                (trendScore * 0.12) +
                (structureMonotonic * 0.06);

            int confidence = (int)Math.Round(Clamp(confidence01, 0, 1) * 100.0, MidpointRounding.AwayFromZero);
            return Math.Max(0, Math.Min(100, confidence));
        }

        private void ArmPendingSetup(
            SetupDirection direction,
            PivotPoint a,
            PivotPoint b,
            PivotPoint c,
            PivotPoint d,
            PivotPoint e,
            PivotPoint f,
            PivotPoint g,
            int confidence)
        {
            _pendingSetupDirection = direction;
            _pendingA = a;
            _pendingB = b;
            _pendingC = c;
            _pendingD = d;
            _pendingE = e;
            _pendingF = f;
            _pendingG = g;
            _pendingSetupBar = CurrentBar;
            _pendingSetupConfidence = confidence;

            DrawSetupMarker(direction, a, b, c, d, e, f, g, confidence);
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

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (_pendingSetupDirection == SetupDirection.Long)
            {
                double invalidationPrice = _pendingE.Price - ReversalStopOffsetPoints;
                if (Close[0] <= (invalidationPrice - (ResolveTickSize() * 0.25)))
                {
                    ClearPendingSetup();
                    return;
                }

                bool tinyFlip = Close[0] > Close[1];
                bool breakF = Close[0] > (_pendingF.Price + BreakBufferPoints);
                if (tinyFlip && (!RequireBreakOfFForEntry || breakF))
                    SubmitPatternEntry(true);

                return;
            }

            double shortInvalidationPrice = _pendingE.Price + ReversalStopOffsetPoints;
            if (Close[0] >= (shortInvalidationPrice + (ResolveTickSize() * 0.25)))
            {
                ClearPendingSetup();
                return;
            }

            bool shortFlip = Close[0] < Close[1];
            bool shortBreakF = Close[0] < (_pendingF.Price - BreakBufferPoints);
            if (shortFlip && (!RequireBreakOfFForEntry || shortBreakF))
                SubmitPatternEntry(false);
        }

        private void SubmitPatternEntry(bool isLong)
        {
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            double choppiness;
            if (!PassesChoppinessFilter(out choppiness))
            {
                DrawSkipMarker(isLong, "CHOP " + choppiness.ToString("0.0", CultureInfo.InvariantCulture));
                ClearPendingSetup();
                return;
            }

            double entryReferencePrice = Close[0];
            int tp1Ticks = ResolveAtrRiskTicks(ReversalTp1AtrMultiple, MinTargetTicks);
            int tp2Ticks = ResolveAtrRiskTicks(ReversalTp2AtrMultiple, MinTargetTicks);
            int trailTicks = ResolveAtrRiskTicks(ReversalTslAtrMultiple, MinTrailTicks);

            double structureStopPrice = isLong
                ? _pendingE.Price - ReversalStopOffsetPoints
                : _pendingE.Price + ReversalStopOffsetPoints;
            double selectedStopPrice = structureStopPrice;

            selectedStopPrice = RoundToTick(selectedStopPrice);
            double stopDistancePoints = Math.Abs(entryReferencePrice - selectedStopPrice);
            if (ReversalMaxEntryStopDistancePoints > 0 && stopDistancePoints > ReversalMaxEntryStopDistancePoints)
            {
                Draw.Text(
                    this,
                    "GPB_SKIP_STOP_" + CurrentBar.ToString(CultureInfo.InvariantCulture),
                    "SKIP: SL > " + ReversalMaxEntryStopDistancePoints.ToString("0.##", CultureInfo.InvariantCulture) + " pts",
                    0,
                    isLong ? Low[0] - (ResolveTickSize() * 5) : High[0] + (ResolveTickSize() * 5),
                    Brushes.DarkOrange);
                ClearPendingSetup();
                return;
            }

            if (isLong)
            {
                _lastSubmittedLongTrailTicks = trailTicks;
                _lastLongEntryStopPrice = selectedStopPrice;
                _trailHasMovedLong = false;

                SetStopLoss(LongSignalName, CalculationMode.Price, selectedStopPrice, false);
                SetProfitTarget(LongSignalName, CalculationMode.Ticks, tp1Ticks);
                EnterLong(PrimaryQuantity, LongSignalName);

                SetStopLoss(LongRunnerSignalName, CalculationMode.Price, selectedStopPrice, false);
                SetProfitTarget(LongRunnerSignalName, CalculationMode.Ticks, tp2Ticks);
                EnterLong(RunnerQuantity, LongRunnerSignalName);
            }
            else
            {
                _lastSubmittedShortTrailTicks = trailTicks;
                _lastShortEntryStopPrice = selectedStopPrice;
                _trailHasMovedShort = false;

                SetStopLoss(ShortSignalName, CalculationMode.Price, selectedStopPrice, false);
                SetProfitTarget(ShortSignalName, CalculationMode.Ticks, tp1Ticks);
                EnterShort(PrimaryQuantity, ShortSignalName);

                SetStopLoss(ShortRunnerSignalName, CalculationMode.Price, selectedStopPrice, false);
                SetProfitTarget(ShortRunnerSignalName, CalculationMode.Ticks, tp2Ticks);
                EnterShort(RunnerQuantity, ShortRunnerSignalName);
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
            _pendingC = default(PivotPoint);
            _pendingD = default(PivotPoint);
            _pendingE = default(PivotPoint);
            _pendingF = default(PivotPoint);
            _pendingG = default(PivotPoint);
            _pendingSetupBar = -1;
            _pendingSetupConfidence = 0;
        }

        private void DrawSetupMarker(
            SetupDirection direction,
            PivotPoint a,
            PivotPoint b,
            PivotPoint c,
            PivotPoint d,
            PivotPoint e,
            PivotPoint f,
            PivotPoint g,
            int confidence)
        {
            int barsAgo = CurrentBar - g.BarIndex;
            if (barsAgo < 0)
                return;

            string tagBase = "GPB_SETUP_" + g.BarIndex.ToString(CultureInfo.InvariantCulture) + "_" + direction.ToString();
            Brush setupBrush = direction == SetupDirection.Long ? Brushes.LimeGreen : Brushes.OrangeRed;
            double y = direction == SetupDirection.Long
                ? g.Price - (ResolveTickSize() * LabelOffsetTicks)
                : g.Price + (ResolveTickSize() * LabelOffsetTicks);

            Draw.Text(
                this,
                tagBase + "_txt",
                (direction == SetupDirection.Long ? "LONG G SETUP" : "SHORT G SETUP") +
                " | C" + confidence.ToString(CultureInfo.InvariantCulture) + "%",
                barsAgo,
                y,
                setupBrush);

            DrawSetupLetter(tagBase, "A", a);
            DrawSetupLetter(tagBase, "B", b);
            DrawSetupLetter(tagBase, "C", c);
            DrawSetupLetter(tagBase, "D", d);
            DrawSetupLetter(tagBase, "E", e);
            DrawSetupLetter(tagBase, "F", f);
            DrawSetupLetter(tagBase, "G", g);
        }

        private void DrawSetupLetter(string tagBase, string letter, PivotPoint pivot)
        {
            int barsAgo = CurrentBar - pivot.BarIndex;
            if (barsAgo < 0)
                return;

            double y = pivot.Type == PivotType.High
                ? pivot.Price + (_tickSize * LabelOffsetTicks)
                : pivot.Price - (_tickSize * LabelOffsetTicks);
            Brush brush = pivot.Type == PivotType.High ? Brushes.Gold : Brushes.DeepSkyBlue;
            Draw.Text(
                this,
                tagBase + "_L_" + letter + "_" + pivot.BarIndex.ToString(CultureInfo.InvariantCulture),
                letter,
                barsAgo,
                y,
                brush);
        }

        private void DrawEntryMarker(bool isLong, double stopPrice)
        {
            string tagBase = "GPB_ENTRY_" + CurrentBar.ToString(CultureInfo.InvariantCulture);
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
                (isLong ? "Long G Entry (TP1+TP2)" : "Short G Entry (TP1+TP2)") + " | stop " + stopPrice.ToString("0.##", CultureInfo.InvariantCulture),
                0,
                isLong ? y - (ResolveTickSize() * 2) : y + (ResolveTickSize() * 2),
                brush);
        }

        private bool PassesChoppinessFilter(out double choppiness)
        {
            choppiness = ComputeChoppiness(ReversalChoppinessPeriod);
            if (!UseReversalChoppinessFilter)
                return true;

            return choppiness <= ReversalMaxChoppiness;
        }

        private double ComputeChoppiness(int period)
        {
            int p = Math.Max(2, period);
            if (CurrentBar < p + 1)
                return 50.0;

            double sumTrueRange = 0.0;
            double highest = double.MinValue;
            double lowest = double.MaxValue;

            for (int i = 0; i < p; i++)
            {
                double high = High[i];
                double low = Low[i];
                double prevClose = Close[i + 1];

                double tr1 = high - low;
                double tr2 = Math.Abs(high - prevClose);
                double tr3 = Math.Abs(low - prevClose);
                double trueRange = Math.Max(tr1, Math.Max(tr2, tr3));

                sumTrueRange += trueRange;
                if (high > highest)
                    highest = high;
                if (low < lowest)
                    lowest = low;
            }

            double range = highest - lowest;
            double tick = ResolveTickSize();
            if (range <= tick || sumTrueRange <= tick)
                return 100.0;

            double raw = 100.0 * (Math.Log10(sumTrueRange / range) / Math.Log10(p));
            return Clamp(raw, 0.0, 100.0);
        }

        private void DrawSkipMarker(bool isLong, string reason)
        {
            _skipDrawCounter++;
            double tick = ResolveTickSize();
            double y = isLong ? Low[0] - (tick * 6) : High[0] + (tick * 6);
            Draw.Text(
                this,
                "GPB_SKIP_" + _skipDrawCounter.ToString(CultureInfo.InvariantCulture),
                "SKIP: " + reason,
                0,
                y,
                Brushes.DarkOrange);
        }

        private void DrawSkipMarkerAtPivot(PivotPoint pivot, bool isLong, string reason)
        {
            int barsAgo = CurrentBar - pivot.BarIndex;
            if (barsAgo < 0)
                barsAgo = 0;

            _skipDrawCounter++;
            double tick = ResolveTickSize();
            double y = isLong ? pivot.Price - (tick * 8) : pivot.Price + (tick * 8);
            Draw.Text(
                this,
                "GPB_SKIPP_" + _skipDrawCounter.ToString(CultureInfo.InvariantCulture),
                "SKIP: " + reason,
                barsAgo,
                y,
                Brushes.DarkOrange);
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
            string text = pivot.Label;
            Draw.Text(
                this,
                "GPB_PIVOT_" + pivot.BarIndex.ToString(CultureInfo.InvariantCulture),
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

        private double ResolveAtrPoints()
        {
            if (_atr == null)
                return ResolveTickSize();

            double atrPoints = _atr[0];
            if (double.IsNaN(atrPoints) || double.IsInfinity(atrPoints) || atrPoints <= 0)
                return ResolveTickSize();

            return atrPoints;
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

        private int PointsToTicks(double points)
        {
            double tick = ResolveTickSize();
            int ticks = (int)Math.Round(points / tick, MidpointRounding.AwayFromZero);
            return Math.Max(1, ticks);
        }

        private void UpdateAtrTrailingStop()
        {
            if (!EnableReversalSetup)
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
                int tp1Ticks = ResolveAtrRiskTicks(ReversalTp1AtrMultiple, MinTargetTicks);
                double tp1Level = _trailEntryPrice + (tp1Ticks * tick);
                if (!_trailTp1Reached)
                {
                    if (High[0] >= tp1Level)
                    {
                        _trailTp1Reached = true;
                        _trailHRefPrice = High[0];

                        double bePrice = RoundToTick(_trailEntryPrice + (BreakEvenPlusTicks * tick));
                        if (bePrice > (_activeStopPrice + (tick * 0.25)))
                        {
                            _activeStopPrice = bePrice;
                            _trailHasMovedLong = true;
                            ApplyStopPriceForDirection(MarketPosition.Long, _activeStopPrice);
                        }
                    }
                    return;
                }

                if (!_trailPullbackSeen)
                {
                    _trailHRefPrice = Math.Max(_trailHRefPrice, High[0]);
                    double pullbackPoints = Math.Max(tick, ResolveAtrPoints() * TrailPullbackAtrMultiple);
                    if (Low[0] <= (_trailHRefPrice - pullbackPoints))
                    {
                        _trailPullbackSeen = true;
                        _trailIRefPrice = Low[0];
                    }
                    return;
                }

                if (!_trailActivated)
                {
                    _trailIRefPrice = Math.Min(_trailIRefPrice, Low[0]);
                    bool reversingFromI = Close[0] > Close[1] && Close[0] > (_trailIRefPrice + (tick * 0.25));
                    if (!reversingFromI)
                        return;

                    _trailActivated = true;
                }

                _extremePriceSinceEntry = Math.Max(_extremePriceSinceEntry, High[0]);
                _trailDistancePoints = ResolveDynamicTrailDistancePoints(MarketPosition.Long, _lastSubmittedLongTrailTicks, tick);
                double candidate = RoundToTick(_extremePriceSinceEntry - _trailDistancePoints);
                double nextStop = Math.Max(_activeStopPrice, candidate);
                if (nextStop > (_activeStopPrice + (tick * 0.25)))
                {
                    _activeStopPrice = nextStop;
                    _trailHasMovedLong = true;
                    ApplyStopPriceForDirection(MarketPosition.Long, _activeStopPrice);
                }
                return;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                int tp1Ticks = ResolveAtrRiskTicks(ReversalTp1AtrMultiple, MinTargetTicks);
                double tp1Level = _trailEntryPrice - (tp1Ticks * tick);
                if (!_trailTp1Reached)
                {
                    if (Low[0] <= tp1Level)
                    {
                        _trailTp1Reached = true;
                        _trailHRefPrice = Low[0];

                        double bePrice = RoundToTick(_trailEntryPrice - (BreakEvenPlusTicks * tick));
                        if (bePrice < (_activeStopPrice - (tick * 0.25)))
                        {
                            _activeStopPrice = bePrice;
                            _trailHasMovedShort = true;
                            ApplyStopPriceForDirection(MarketPosition.Short, _activeStopPrice);
                        }
                    }
                    return;
                }

                if (!_trailPullbackSeen)
                {
                    _trailHRefPrice = Math.Min(_trailHRefPrice, Low[0]);
                    double pullbackPoints = Math.Max(tick, ResolveAtrPoints() * TrailPullbackAtrMultiple);
                    if (High[0] >= (_trailHRefPrice + pullbackPoints))
                    {
                        _trailPullbackSeen = true;
                        _trailIRefPrice = High[0];
                    }
                    return;
                }

                if (!_trailActivated)
                {
                    _trailIRefPrice = Math.Max(_trailIRefPrice, High[0]);
                    bool reversingFromI = Close[0] < Close[1] && Close[0] < (_trailIRefPrice - (tick * 0.25));
                    if (!reversingFromI)
                        return;

                    _trailActivated = true;
                }

                _extremePriceSinceEntry = Math.Min(_extremePriceSinceEntry, Low[0]);
                _trailDistancePoints = ResolveDynamicTrailDistancePoints(MarketPosition.Short, _lastSubmittedShortTrailTicks, tick);
                double candidate = RoundToTick(_extremePriceSinceEntry + _trailDistancePoints);
                double nextStop = Math.Min(_activeStopPrice, candidate);
                if (nextStop < (_activeStopPrice - (tick * 0.25)))
                {
                    _activeStopPrice = nextStop;
                    _trailHasMovedShort = true;
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
                _trailDistancePoints = ResolveDynamicTrailDistancePoints(MarketPosition.Long, _lastSubmittedLongTrailTicks, tick);
                _activeStopPrice = _lastLongEntryStopPrice > 0 ? _lastLongEntryStopPrice : RoundToTick(entryPrice - (ResolveAtrRiskTicks(ReversalTslAtrMultiple, MinStopTicks) * tick));
                _extremePriceSinceEntry = Math.Max(entryPrice, High[0]);
                _trailHasMovedLong = false;
                _trailEntryPrice = entryPrice;
                _trailTp1Reached = false;
                _trailPullbackSeen = false;
                _trailActivated = false;
                _trailHRefPrice = entryPrice;
                _trailIRefPrice = entryPrice;
                ApplyStopPriceForDirection(MarketPosition.Long, _activeStopPrice);
            }
            else if (current == MarketPosition.Short)
            {
                _trailDistancePoints = ResolveDynamicTrailDistancePoints(MarketPosition.Short, _lastSubmittedShortTrailTicks, tick);
                _activeStopPrice = _lastShortEntryStopPrice > 0 ? _lastShortEntryStopPrice : RoundToTick(entryPrice + (ResolveAtrRiskTicks(ReversalTslAtrMultiple, MinStopTicks) * tick));
                _extremePriceSinceEntry = Math.Min(entryPrice, Low[0]);
                _trailHasMovedShort = false;
                _trailEntryPrice = entryPrice;
                _trailTp1Reached = false;
                _trailPullbackSeen = false;
                _trailActivated = false;
                _trailHRefPrice = entryPrice;
                _trailIRefPrice = entryPrice;
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
                SetStopLoss(LongRunnerSignalName, CalculationMode.Price, price, false);
                return;
            }

            if (direction == MarketPosition.Short)
            {
                SetStopLoss(ShortSignalName, CalculationMode.Price, price, false);
                SetStopLoss(ShortRunnerSignalName, CalculationMode.Price, price, false);
            }
        }

        private double ResolveDynamicTrailDistancePoints(MarketPosition direction, int submittedTrailTicks, double tick)
        {
            double atrPoints = ResolveAtrPoints();
            double favorablePoints =
                direction == MarketPosition.Long
                    ? Math.Max(0, _extremePriceSinceEntry - _trailEntryPrice)
                    : Math.Max(0, _trailEntryPrice - _extremePriceSinceEntry);
            double favorableAtr = atrPoints > 1e-8 ? favorablePoints / atrPoints : 0;

            double effectiveMult = Math.Max(
                ProgressiveTrailMinAtrMultiple,
                Math.Max(0.05, ReversalTslAtrMultiple - (favorableAtr * ProgressiveTrailTightenPerAtr)));

            int trailTicks = Math.Max(1, submittedTrailTicks > 0
                ? submittedTrailTicks
                : ResolveAtrRiskTicks(effectiveMult, MinTrailTicks));
            double minimumPoints = tick * Math.Max(1, MinTrailTicks);
            return Math.Max(minimumPoints, Math.Max(trailTicks * tick, atrPoints * effectiveMult));
        }

        private void ResetTrailingState()
        {
            _trailingInitialized = false;
            _trailingPosition = MarketPosition.Flat;
            _activeStopPrice = 0;
            _extremePriceSinceEntry = 0;
            _trailDistancePoints = 0;
            _lastSubmittedLongTrailTicks = 0;
            _lastSubmittedShortTrailTicks = 0;
            _lastLongEntryStopPrice = 0;
            _lastShortEntryStopPrice = 0;
            _trailHasMovedLong = false;
            _trailHasMovedShort = false;
            _trailEntryPrice = 0;
            _trailTp1Reached = false;
            _trailPullbackSeen = false;
            _trailActivated = false;
            _trailHRefPrice = 0;
            _trailIRefPrice = 0;
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

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
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
