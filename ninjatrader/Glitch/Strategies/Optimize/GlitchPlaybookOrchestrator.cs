// GlitchPlaybookOrchestrator.cs
// Phase 1: Orchestrator scaffold + GlitchDirectionalFlip playbook wired to shared execution/risk engine.

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
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
    public enum PlayBook
    {
        None = 0,
        GlitchDirectionalFlip = 10,
        GlitchCompressionBreakout = 20,
        GlitchSweepBos = 30,
        GlitchSessionStructure = 40,
        GlitchMeanReversion = 50
    }

    public enum PlayType
    {
        TrendContinuation = 0,
        BreakoutExpansion = 1,
        LiquiditySweep = 2,
        SessionStructure = 3,
        MeanReversion = 4
    }

    public enum ExitModel
    {
        Fixed = 0,
        AtrMultiple = 1
    }

    public sealed class PlayScore
    {
        public PlayBook Book { get; set; }
        public PlayType Type { get; set; }
        public double LongScore { get; set; }
        public double ShortScore { get; set; }
        public double Confidence { get; set; }
        public bool IsTradeable { get; set; }
        public string Reason { get; set; }

        public bool IsLongBias { get { return LongScore > ShortScore; } }
        public bool IsShortBias { get { return ShortScore > LongScore; } }
    }

    public enum RouterState
    {
        Neutral = 0,
        Armed = 1,
        InPosition = 2,
        Cooldown = 3
    }

    public class GlitchPlaybookOrchestrator : Strategy
    {
        private sealed class EntryTelemetryContext
        {
            public DateTime EntryTime;
            public string EntryName;
            public double EntryPrice;
            public string PlaybookCode;
            public string PlaytypeCode;
            public string Direction;
            public double StopPoints;
            public double Tp1Points;
            public double Tp2Points;
            public double TrailPoints;
            public double BreakevenPoints;
            public double RrTp2Stop;
            public double EntryAtr;
            public string ExitModel;
            public double EntryConfidence;
            public string EntryReason;
            public bool Valid;
        }

        // -----------------------------
        // Router and lifecycle state
        // -----------------------------
        private RouterState _routerState;
        private PlayBook _selectedBook;
        private PlayBook _activeTradeBook;
        private int _tradeSequence;
        private int _cooldownUntilBar;
        private int _playbookLockUntilBar;

        // -----------------------------
        // Daily risk state
        // -----------------------------
        private double _dailyPnL;
        private DateTime _currentTradingDay;
        private bool _dailyLimitReached;

        // -----------------------------
        // Shared order/execution state
        // -----------------------------
        private string _entrySignalTag;
        private string _exitTagTp1;
        private string _exitTagTp2;
        private string _exitTagStop;
        private string _exitTagFlip;
        private string _exitTagNoProgress;
        private string _exitTagTime;

        private Order _stopOrder;
        private Order _tp1Order;
        private Order _tp2Order;

        private double _entryPrice;
        private string _tradeDirection; // LONG / SHORT
        private bool _tp1Filled;
        private bool _tp2Filled;
        private double _extremePriceSinceEntry;
        private double _currentStopPrice;
        private bool _movedToBreakeven;
        private bool _directionalFlipAgainstPending;
        private int _directionalFlipBarsSinceAgainst;

        // -----------------------------
        // GlitchDirectionalFlip signal state
        // -----------------------------
        private double _smoothedTr;
        private double _smoothedDmPlus;
        private double _smoothedDmMinus;
        private double _prevDiPlus;
        private double _prevDiMinus;
        private int _posCount;
        private int _negCount;
        private bool? _priorBullish;
        private bool _bullish;
        private bool _isFlip;
        private bool _flipJustOccurred;
        private bool _flipDirectionLong;

        private SMA _smaHighHtf;
        private SMA _smaLowHtf;
        private SMA _smaCloseHtf;
        private ADX _adxCompression;
        private RSI _rsiCompression;
        private SMA _mrBasis;
        private StdDev _mrStdDev;
        private RSI _mrRsi;
        private SMA _sessionBiasSma;
        private Series<double> _hlc3Series;
        private Series<double> _closeSeries;
        private EMA _t3E1;
        private EMA _t3E2;
        private EMA _t3E3;
        private EMA _t3E4;
        private EMA _t3E5;
        private EMA _t3E6;
        private EMA _mrT3E1;
        private EMA _mrT3E2;
        private EMA _mrT3E3;
        private EMA _mrT3E4;
        private EMA _mrT3E5;
        private EMA _mrT3E6;
        private ATR _atrDirectionalFlip;
        private ATR _atrCompressionBreakout;
        private ATR _atrSweepBos;
        private ATR _atrSessionStructure;
        private ATR _atrMeanReversion;

        // SweepBos state machine
        private int _sweepPhaseLong;
        private int _sweepPhaseShort;
        private int _sweepTimeoutBarLong;
        private int _sweepTimeoutBarShort;

        // SessionStructure state
        private DateTime _sessionStructureDay;
        private bool _sessionRangeBuilding;
        private bool _sessionRangeFrozen;
        private double _sessionRangeHigh;
        private double _sessionRangeLow;
        private bool _sessionLongFiredToday;
        private bool _sessionShortFiredToday;

        // MeanReversion state
        private bool _mrUpperCrossArmed;
        private bool _mrLowerCrossArmed;
        private bool _mrRsiCrossunderArmed;
        private bool _mrRsiCrossoverArmed;

        // Diagnostics counters
        private int _diagBarsEvaluated;
        private int _diagStrictBlockedCount;
        private int _diagSelectedDf;
        private int _diagSelectedCb;
        private int _diagSelectedSb;
        private int _diagSelectedSs;
        private int _diagSelectedMr;
        private int _diagEntriesDf;
        private int _diagEntriesCb;
        private int _diagEntriesSb;
        private int _diagEntriesSs;
        private int _diagEntriesMr;
        private int _dailyEntriesTotal;
        private int _dailyEntriesDf;
        private int _dailyEntriesCb;
        private int _dailyEntriesSb;
        private int _dailyEntriesSs;
        private int _dailyEntriesMr;
        private double _lastSelectedConfidence;
        private string _lastSelectedReason;

        // Per-trade execution profile
        private int _activeEntryQuantity;
        private int _activeRunnerQuantity;
        private PlayType _activeTradeType;

        // Tape exporter
        private StringBuilder _tapeBuffer;
        private int _tapeBufferedRows;
        private string _tapeFilePathResolved;
        private bool _tapeHeaderWritten;
        private StringBuilder _gateAuditBuffer;
        private int _gateAuditBufferedRows;
        private string _gateAuditFilePathResolved;
        private bool _gateAuditHeaderWritten;
        private DateTime _tradeEntryTime;
        private double _tradeEntryPrice;
        private int _tradeRemainingQty;
        private double _tradeExitWeightedPriceSum;
        private int _tradeExitWeightedQtySum;
        private double _tradeRealizedPnlCurrency;
        private double _tradeEntryStopPoints;
        private double _tradeEntryTp1Points;
        private double _tradeEntryTp2Points;
        private double _tradeEntryTrailPoints;
        private double _tradeEntryBreakevenPoints;
        private double _tradeEntryRrTp2;
        private double _tradeEntryAtr;
        private ExitModel _tradeEntryExitModel;
        private double _tradeEntryConfidence;
        private int _tradeEntryBar;
        private string _tradeEntryReason;
        private int _tradeExitQtyTp1;
        private int _tradeExitQtyTp2;
        private int _tradeExitQtyStop;
        private int _tradeExitQtyFlip;
        private int _tradeExitQtyOther;
        private bool _tradeTapeFinalized;
        private int _lastExportedTradeIndex;
        private List<EntryTelemetryContext> _entryTelemetryContexts;

        // -----------------------------
        // Constructor-like reset helpers
        // -----------------------------
        private void ResetTradeState()
        {
            _entrySignalTag = string.Empty;
            _exitTagTp1 = string.Empty;
            _exitTagTp2 = string.Empty;
            _exitTagStop = string.Empty;
            _exitTagFlip = string.Empty;
            _exitTagNoProgress = string.Empty;
            _exitTagTime = string.Empty;

            _stopOrder = null;
            _tp1Order = null;
            _tp2Order = null;

            _entryPrice = double.NaN;
            _tradeDirection = string.Empty;
            _tp1Filled = false;
            _tp2Filled = false;
            _extremePriceSinceEntry = double.NaN;
            _currentStopPrice = double.NaN;
            _movedToBreakeven = false;
            _directionalFlipAgainstPending = false;
            _directionalFlipBarsSinceAgainst = 0;
            _activeTradeBook = PlayBook.None;
            _activeTradeType = PlayType.TrendContinuation;
            _activeEntryQuantity = 0;
            _activeRunnerQuantity = 0;
            _tradeEntryTime = DateTime.MinValue;
            _tradeEntryPrice = double.NaN;
            _tradeRemainingQty = 0;
            _tradeExitWeightedPriceSum = 0;
            _tradeExitWeightedQtySum = 0;
            _tradeRealizedPnlCurrency = 0;
            _tradeEntryStopPoints = 0;
            _tradeEntryTp1Points = 0;
            _tradeEntryTp2Points = 0;
            _tradeEntryTrailPoints = 0;
            _tradeEntryBreakevenPoints = 0;
            _tradeEntryRrTp2 = 0;
            _tradeEntryAtr = 0;
            _tradeEntryExitModel = ExitModel.Fixed;
            _tradeEntryConfidence = 0;
            _tradeEntryBar = -1;
            _tradeEntryReason = string.Empty;
            _tradeExitQtyTp1 = 0;
            _tradeExitQtyTp2 = 0;
            _tradeExitQtyStop = 0;
            _tradeExitQtyFlip = 0;
            _tradeExitQtyOther = 0;
            _tradeTapeFinalized = false;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "GlitchPlaybookOrchestrator";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                StartBehavior = StartBehavior.WaitUntilFlat;
                IsOverlay = true;
                IncludeCommission = true;

                // 0. Orchestrator
                EnableOrchestrator = true;
                MinPlayScoreToTrade = 30;
                PlaybookLockBars = 1;
                CooldownBarsAfterExit = 0;
                MaxTradesPerDay = 15;
                DirectionalFlipMaxTradesPerDay = 4;
                CompressionBreakoutMaxTradesPerDay = 3;
                SweepBosMaxTradesPerDay = 3;
                SessionStructureMaxTradesPerDay = 2;
                MeanReversionMaxTradesPerDay = 3;

                // 1. Shared Execution
                SharedEntryQuantity = 3;
                SharedEnableBreakeven = true;
                SharedEnableTrailingStop = true;
                SharedTimeFilterEnabled = false;
                SharedTradingStartHour = 5;
                SharedTradingEndHour = 14;
                SharedAvoidWindowStartHour = 5;
                SharedAvoidWindowEndHour = 5;

                // 2. Shared Risk
                SharedEnableDailyLimits = false;
                SharedDailyProfitTarget = 280;
                SharedDailyLossLimit = 580;
                SharedDefaultStopPoints = 45;
                SharedDefaultTp1Points = 45;
                SharedDefaultTp2Points = 50;
                SharedDefaultTrailPoints = 35;
                SharedDefaultBreakevenTriggerPoints = 35;
                SharedEnableNoProgressExit = true;
                SharedNoProgressBars = 5;
                SharedNoProgressMinProgressTp1Fraction = 0.20;
                SharedNoProgressAdverseStopFraction = 0.60;
                SharedEnableMaxBarsExit = true;
                SharedMaxBarsInTrade = 24;

                // 3. PlayType: DirectionalFlip
                EnablePlayTypeDirectionalFlip = true;
                DirectionalFlipEntryQuantity = 3;
                DirectionalFlipEnableRunner = false;
                DirectionalFlipLength = 29;
                DirectionalFlipSmoothingFactor = 18;
                DirectionalFlipHigherTfMinutes = 1;
                DirectionalFlipFlipConfirmBars = 2;
                DirectionalFlipEnableBreakeven = true;
                DirectionalFlipEnableTrailingStop = true;
                DirectionalFlipMinPlayScore = 85;
                DirectionalFlipEnableAtrRegimeFilter = true;
                DirectionalFlipAtrRegimeMin = 3.4;
                DirectionalFlipAtrRegimeMax = 9.5;
                DirectionalFlipEnableR2RegimeFilter = true;
                DirectionalFlipR2RegimeMin = 0.20;
                DirectionalFlipR2RegimeMax = 1;
                DirectionalFlipEnableMinRrGuard = true;
                DirectionalFlipMinRr = 1.60;
                DirectionalFlipExitModel = ExitModel.AtrMultiple;
                DirectionalFlipAtrPeriod = 14;
                DirectionalFlipAtrStopMult = 1.55;
                DirectionalFlipAtrTp1Mult = 1.0;
                DirectionalFlipAtrTp2Mult = 3.0;
                DirectionalFlipAtrTrailMult = 1.0;
                DirectionalFlipAtrBreakevenMult = 0.7;
                DirectionalFlipAtrMinPoints = 6;
                DirectionalFlipAtrMaxPoints = 120;
                DirectionalFlipStopPoints = 45;
                DirectionalFlipTp1Points = 45;
                DirectionalFlipTp2Points = 50;
                DirectionalFlipTrailPoints = 35;
                DirectionalFlipBreakevenTriggerPoints = 35;

                // 4. PlayType: CompressionBreakout
                EnablePlayTypeCompressionBreakout = true;
                CompressionBreakoutEntryQuantity = 2;
                CompressionBreakoutEnableRunner = false;
                CompressionBreakoutAdxMin = 18;
                CompressionBreakoutUseAdxFilter = true;
                CompressionBreakoutAdxPeriod = 14;
                CompressionBreakoutRsiPeriod = 14;
                CompressionBreakoutRsiLongMax = 50;
                CompressionBreakoutRsiShortMin = 50;
                CompressionBreakoutVolumeLookback = 50;
                CompressionBreakoutEnableBreakeven = true;
                CompressionBreakoutEnableTrailingStop = true;
                CompressionBreakoutMinPlayScore = 78;
                CompressionBreakoutEnableAtrRegimeFilter = true;
                CompressionBreakoutAtrRegimeMin = 4.0;
                CompressionBreakoutAtrRegimeMax = 9.9;
                CompressionBreakoutEnableR2RegimeFilter = true;
                CompressionBreakoutR2RegimeMin = 0.15;
                CompressionBreakoutR2RegimeMax = 0.90;
                CompressionBreakoutEnableMinRrGuard = true;
                CompressionBreakoutMinRr = 1.55;
                CompressionBreakoutExitModel = ExitModel.AtrMultiple;
                CompressionBreakoutAtrPeriod = 14;
                CompressionBreakoutAtrStopMult = 1.45;
                CompressionBreakoutAtrTp1Mult = 1.0;
                CompressionBreakoutAtrTp2Mult = 2.6;
                CompressionBreakoutAtrTrailMult = 0.9;
                CompressionBreakoutAtrBreakevenMult = 0.65;
                CompressionBreakoutAtrMinPoints = 5;
                CompressionBreakoutAtrMaxPoints = 120;
                CompressionBreakoutStopPoints = 35;
                CompressionBreakoutTp1Points = 30;
                CompressionBreakoutTp2Points = 60;

                // 5. PlayType: SweepBos
                EnablePlayTypeSweepBos = true;
                SweepBosEntryQuantity = 2;
                SweepBosEnableRunner = false;
                SweepBosR2Min = 0.20;
                SweepBosUseTrendFilter = true;
                SweepBosSwingLookback = 20;
                SweepBosMaxSequenceBars = 8;
                SweepBosRestartTimeoutAfterPhase2 = true;
                SweepBosBosLookback = 5;
                SweepBosUseWickBos = true;
                SweepBosRequireBos = true;
                SweepBosRequireCloseBeyondT3 = true;
                SweepBosEnableBreakeven = true;
                SweepBosEnableTrailingStop = true;
                SweepBosMinPlayScore = 84;
                SweepBosEnableAtrRegimeFilter = true;
                SweepBosAtrRegimeMin = 4.1;
                SweepBosAtrRegimeMax = 10.4;
                SweepBosEnableR2RegimeFilter = true;
                SweepBosR2RegimeMin = 0.18;
                SweepBosR2RegimeMax = 0.85;
                SweepBosEnableMinRrGuard = true;
                SweepBosMinRr = 1.75;
                SweepBosExitModel = ExitModel.AtrMultiple;
                SweepBosAtrPeriod = 14;
                SweepBosAtrStopMult = 1.35;
                SweepBosAtrTp1Mult = 1.0;
                SweepBosAtrTp2Mult = 2.7;
                SweepBosAtrTrailMult = 0.9;
                SweepBosAtrBreakevenMult = 0.65;
                SweepBosAtrMinPoints = 5;
                SweepBosAtrMaxPoints = 120;
                SweepBosStopPoints = 35;
                SweepBosTp1Points = 30;
                SweepBosTp2Points = 70;

                // 6. PlayType: SessionStructure
                EnablePlayTypeSessionStructure = true;
                SessionStructureEntryQuantity = 2;
                SessionStructureEnableRunner = false;
                SessionStructureStartHour = 8;
                SessionStructureStartMinute = 30;
                SessionStructureRangeDurationMinutes = 5;
                SessionStructureTradingCutoffHour = 11;
                SessionStructureTradingCutoffMinute = 30;
                SessionStructureBreakoutBufferTicks = 2;
                SessionStructureUsePremarketBias = true;
                SessionStructurePremarketBiasLookbackBars = 120;
                SessionStructureSingleDirectionPerDay = true;
                SessionStructureEnableBreakeven = true;
                SessionStructureEnableTrailingStop = true;
                SessionStructureMinPlayScore = 72;
                SessionStructureEnableAtrRegimeFilter = true;
                SessionStructureAtrRegimeMin = 3.8;
                SessionStructureAtrRegimeMax = 7.8;
                SessionStructureEnableR2RegimeFilter = true;
                SessionStructureR2RegimeMin = 0.10;
                SessionStructureR2RegimeMax = 0.75;
                SessionStructureEnableMinRrGuard = true;
                SessionStructureMinRr = 1.60;
                SessionStructureExitModel = ExitModel.AtrMultiple;
                SessionStructureAtrPeriod = 14;
                SessionStructureAtrStopMult = 1.15;
                SessionStructureAtrTp1Mult = 0.9;
                SessionStructureAtrTp2Mult = 2.1;
                SessionStructureAtrTrailMult = 0.85;
                SessionStructureAtrBreakevenMult = 0.6;
                SessionStructureAtrMinPoints = 4;
                SessionStructureAtrMaxPoints = 100;
                SessionStructureStopPoints = 30;
                SessionStructureTp1Points = 25;
                SessionStructureTp2Points = 55;

                // 7. PlayType: MeanReversion
                EnablePlayTypeMeanReversion = true;
                MeanReversionEntryQuantity = 1;
                MeanReversionEnableRunner = false;
                MeanReversionUseT3Basis = false;
                MeanReversionT3Length = 5;
                MeanReversionT3VolumeFactor = 0.7;
                MeanReversionBbLength = 20;
                MeanReversionBbMultiplier = 2.0;
                MeanReversionRsiLength = 14;
                MeanReversionRsiUpper = 65;
                MeanReversionRsiLower = 35;
                MeanReversionEnableBreakeven = true;
                MeanReversionEnableTrailingStop = false;
                MeanReversionMinPlayScore = 63;
                MeanReversionEnableAtrRegimeFilter = true;
                MeanReversionAtrRegimeMin = 3.6;
                MeanReversionAtrRegimeMax = 9.0;
                MeanReversionEnableR2RegimeFilter = true;
                MeanReversionR2RegimeMin = 0.00;
                MeanReversionR2RegimeMax = 0.18;
                MeanReversionEnableMinRrGuard = true;
                MeanReversionMinRr = 1.30;
                MeanReversionExitModel = ExitModel.AtrMultiple;
                MeanReversionAtrPeriod = 14;
                MeanReversionAtrStopMult = 1.0;
                MeanReversionAtrTp1Mult = 0.8;
                MeanReversionAtrTp2Mult = 1.4;
                MeanReversionAtrTrailMult = 0.75;
                MeanReversionAtrBreakevenMult = 0.5;
                MeanReversionAtrMinPoints = 4;
                MeanReversionAtrMaxPoints = 80;
                MeanReversionStopPoints = 25;
                MeanReversionTp1Points = 20;
                MeanReversionTp2Points = 35;

                // 8. Diagnostics
                EnableDiagnosticsLogs = false;
                StrictModeRequireIndicatorsReady = false;
                DiagnosticsLogEveryNBars = 0;
                EnableCompactStatusPanel = false;
                EnableTapeExport = true;
                TapeExportRelativePath = @"Glitch\glitch-playbook-tape-authoritative-hybrid-v9.csv";
                TapeFlushEveryNRows = 1;
                EnableTapeDebugLogs = true;
                UseAuthoritativeTradeExport = true;
                EnableGateAuditExport = true;
                GateAuditExportRelativePath = @"Glitch\glitch-playbook-gate-audit-v9.csv";
                GateAuditFlushEveryNRows = 50;

                // 9. Indicator Inputs (shared adapters)
                TrendQualityT3Length = 4;
                TrendQualityT3VolumeFactor = 0.7;
                TrendQualityR2Lookback = 100;
            }
            else if (State == State.Configure)
            {
                // Intentionally single-series for consistent behavior across backtest/playback/live.
            }
            else if (State == State.DataLoaded)
            {
                _smaHighHtf = SMA(Highs[0], DirectionalFlipSmoothingFactor);
                _smaLowHtf = SMA(Lows[0], DirectionalFlipSmoothingFactor);
                _smaCloseHtf = SMA(Closes[0], DirectionalFlipSmoothingFactor);
                _adxCompression = ADX(Math.Max(2, CompressionBreakoutAdxPeriod));
                _rsiCompression = RSI(Math.Max(2, CompressionBreakoutRsiPeriod), 1);
                _mrBasis = SMA(Closes[0], Math.Max(2, MeanReversionBbLength));
                _mrStdDev = StdDev(Closes[0], Math.Max(2, MeanReversionBbLength));
                _mrRsi = RSI(Math.Max(2, MeanReversionRsiLength), 1);
                _sessionBiasSma = SMA(Closes[0], Math.Max(5, SessionStructurePremarketBiasLookbackBars));
                _hlc3Series = new Series<double>(this);
                _closeSeries = new Series<double>(this);
                _t3E1 = EMA(_hlc3Series, Math.Max(1, TrendQualityT3Length));
                _t3E2 = EMA(_t3E1, Math.Max(1, TrendQualityT3Length));
                _t3E3 = EMA(_t3E2, Math.Max(1, TrendQualityT3Length));
                _t3E4 = EMA(_t3E3, Math.Max(1, TrendQualityT3Length));
                _t3E5 = EMA(_t3E4, Math.Max(1, TrendQualityT3Length));
                _t3E6 = EMA(_t3E5, Math.Max(1, TrendQualityT3Length));
                _mrT3E1 = EMA(_closeSeries, Math.Max(1, MeanReversionT3Length));
                _mrT3E2 = EMA(_mrT3E1, Math.Max(1, MeanReversionT3Length));
                _mrT3E3 = EMA(_mrT3E2, Math.Max(1, MeanReversionT3Length));
                _mrT3E4 = EMA(_mrT3E3, Math.Max(1, MeanReversionT3Length));
                _mrT3E5 = EMA(_mrT3E4, Math.Max(1, MeanReversionT3Length));
                _mrT3E6 = EMA(_mrT3E5, Math.Max(1, MeanReversionT3Length));
                _atrDirectionalFlip = ATR(Math.Max(2, DirectionalFlipAtrPeriod));
                _atrCompressionBreakout = ATR(Math.Max(2, CompressionBreakoutAtrPeriod));
                _atrSweepBos = ATR(Math.Max(2, SweepBosAtrPeriod));
                _atrSessionStructure = ATR(Math.Max(2, SessionStructureAtrPeriod));
                _atrMeanReversion = ATR(Math.Max(2, MeanReversionAtrPeriod));

                _smoothedTr = _smoothedDmPlus = _smoothedDmMinus = double.NaN;
                _dailyPnL = 0;
                _currentTradingDay = DateTime.MinValue;
                _dailyLimitReached = false;
                _routerState = RouterState.Neutral;
                _selectedBook = PlayBook.None;
                _activeTradeBook = PlayBook.None;
                _tradeSequence = 0;
                _cooldownUntilBar = -1;
                _playbookLockUntilBar = -1;
                _diagBarsEvaluated = 0;
                _diagStrictBlockedCount = 0;
                _diagSelectedDf = _diagSelectedCb = _diagSelectedSb = _diagSelectedSs = _diagSelectedMr = 0;
                _diagEntriesDf = _diagEntriesCb = _diagEntriesSb = _diagEntriesSs = _diagEntriesMr = 0;
                _lastSelectedConfidence = 0;
                _lastSelectedReason = "n/a";
                _activeEntryQuantity = 0;
                _activeRunnerQuantity = 0;
                _activeTradeType = PlayType.TrendContinuation;
                _tapeBuffer = new StringBuilder(4096);
                _tapeBufferedRows = 0;
                _tapeFilePathResolved = ResolveTapeFilePath();
                _tapeHeaderWritten = TryTapeFileHasData(_tapeFilePathResolved);
                _gateAuditBuffer = new StringBuilder(4096);
                _gateAuditBufferedRows = 0;
                _gateAuditFilePathResolved = ResolveGateAuditFilePath();
                _gateAuditHeaderWritten = TryTapeFileHasData(_gateAuditFilePathResolved);
                _lastExportedTradeIndex = -1;
                _entryTelemetryContexts = new List<EntryTelemetryContext>(4096);
                TapeDebug(string.Format("DataLoaded tape path={0} headerExists={1}", _tapeFilePathResolved, _tapeHeaderWritten));
                TapeDebug(string.Format("DataLoaded gate-audit path={0} headerExists={1}", _gateAuditFilePathResolved, _gateAuditHeaderWritten));
                _tradeEntryTime = DateTime.MinValue;
                _tradeEntryPrice = double.NaN;
                _tradeRemainingQty = 0;
                _tradeExitWeightedPriceSum = 0;
                _tradeExitWeightedQtySum = 0;
                _tradeRealizedPnlCurrency = 0;
                _sweepPhaseLong = 0;
                _sweepPhaseShort = 0;
                _sweepTimeoutBarLong = -1;
                _sweepTimeoutBarShort = -1;
                _sessionStructureDay = DateTime.MinValue;
                _sessionRangeBuilding = false;
                _sessionRangeFrozen = false;
                _sessionRangeHigh = double.NaN;
                _sessionRangeLow = double.NaN;
                _sessionLongFiredToday = false;
                _sessionShortFiredToday = false;
                _mrUpperCrossArmed = false;
                _mrLowerCrossArmed = false;
                _mrRsiCrossunderArmed = false;
                _mrRsiCrossoverArmed = false;
                ResetTradeState();
            }
            else if (State == State.Terminated)
            {
                TapeDebug("Terminated: flushing tape buffer.");
                FlushTapeBuffer();
                TapeDebug("Terminated: flushing gate-audit buffer.");
                FlushGateAuditBuffer();
            }
        }

        protected override void OnBarUpdate()
        {
            if (!EnableOrchestrator)
                return;

            if (BarsInProgress != 0)
                return;

            ExportClosedTradesAuthoritative();

            if (CurrentBars[0] < 10)
                return;

            if (EnablePlayTypeDirectionalFlip && CurrentBar < Math.Max(10, DirectionalFlipLength + 2))
                return;

            if (EnablePlayTypeDirectionalFlip)
                ComputeDirectionalFlipState();

            _hlc3Series[0] = (High[0] + Low[0] + Close[0]) / 3.0;
            _closeSeries[0] = Close[0];
            _diagBarsEvaluated++;
            RefreshTradingDay();
            UpdateSessionStructureContext();
            TryDiagnosticsLog();
            UpdateCompactStatusPanel();

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                _routerState = RouterState.InPosition;
                ManageActivePosition();
                return;
            }

            // Position is flat from here.
            if (_routerState == RouterState.InPosition)
            {
                TryFinalizeTapeOnFlatTransition();
                _routerState = RouterState.Cooldown;
                _cooldownUntilBar = CurrentBar + Math.Max(0, CooldownBarsAfterExit);
                ResetTradeState();
            }

            if (_routerState == RouterState.Cooldown && CurrentBar < _cooldownUntilBar)
                return;

            if (_routerState == RouterState.Cooldown && CurrentBar >= _cooldownUntilBar)
                _routerState = RouterState.Neutral;

            if (!IsGlobalEntryAllowed())
                return;

            List<PlayScore> scores = EvaluatePlayScores();
            ExportGateAuditForBlockedCandidates(scores);
            PlayScore selected = SelectBestPlayScore(scores);
            if (selected == null || !selected.IsTradeable)
                return;

            int dayCountByBook;
            int maxByBook;
            if (!IsPerBookEntryAllowed(selected.Book, out dayCountByBook, out maxByBook))
            {
                _lastSelectedConfidence = selected.Confidence;
                _lastSelectedReason = string.Format(
                    CultureInfo.InvariantCulture,
                    "Blocked {0} Daily Cap {1}/{2}",
                    GetPlaybookCode(selected.Book),
                    dayCountByBook,
                    maxByBook);
                return;
            }

            int requiredMinScore = ResolveMinPlayScoreByBook(selected.Book);
            if (selected.Confidence < requiredMinScore)
            {
                _lastSelectedConfidence = selected.Confidence;
                _lastSelectedReason = string.Format(
                    CultureInfo.InvariantCulture,
                    "Blocked Score {0:F1} < Min {1}",
                    selected.Confidence,
                    requiredMinScore);
                return;
            }

            double rr;
            double minRr;
            if (!PassesMinRrGuard(selected.Book, out rr, out minRr))
            {
                _lastSelectedConfidence = selected.Confidence;
                _lastSelectedReason = string.Format(
                    CultureInfo.InvariantCulture,
                    "Blocked RR {0:F2} < Min {1:F2}",
                    rr,
                    minRr);
                return;
            }

            double atrValue;
            double atrMin;
            double atrMax;
            if (!PassesAtrRegimeGuard(selected.Book, out atrValue, out atrMin, out atrMax))
            {
                _lastSelectedConfidence = selected.Confidence;
                _lastSelectedReason = string.Format(
                    CultureInfo.InvariantCulture,
                    "Blocked ATR {0:F2} not in [{1:F2},{2:F2}]",
                    atrValue,
                    atrMin,
                    atrMax);
                return;
            }

            double r2Value;
            double r2Min;
            double r2Max;
            if (!PassesR2RegimeGuard(selected.Book, out r2Value, out r2Min, out r2Max))
            {
                _lastSelectedConfidence = selected.Confidence;
                _lastSelectedReason = string.Format(
                    CultureInfo.InvariantCulture,
                    "Blocked R2 {0:F3} not in [{1:F3},{2:F3}]",
                    r2Value,
                    r2Min,
                    r2Max);
                return;
            }

            _selectedBook = selected.Book;
            _lastSelectedConfidence = selected.Confidence;
            _lastSelectedReason = selected.Reason;
            IncrementSelectionCounter(_selectedBook);
            _routerState = RouterState.Armed;

            bool goLong = selected.LongScore >= selected.ShortScore;
            SubmitEntry(_selectedBook, selected.Type, goLong);
            _playbookLockUntilBar = CurrentBar + Math.Max(0, PlaybookLockBars);
        }

        private void RefreshTradingDay()
        {
            DateTime barDate = Time[0].Date;
            if (barDate == _currentTradingDay)
                return;

            _currentTradingDay = barDate;
            _dailyPnL = 0;
            _dailyLimitReached = false;
            _dailyEntriesTotal = 0;
            _dailyEntriesDf = 0;
            _dailyEntriesCb = 0;
            _dailyEntriesSb = 0;
            _dailyEntriesSs = 0;
            _dailyEntriesMr = 0;
            _sessionLongFiredToday = false;
            _sessionShortFiredToday = false;
            _sessionStructureDay = barDate;
            _sessionRangeBuilding = false;
            _sessionRangeFrozen = false;
            _sessionRangeHigh = double.NaN;
            _sessionRangeLow = double.NaN;
            _sweepPhaseLong = 0;
            _sweepPhaseShort = 0;
            _sweepTimeoutBarLong = -1;
            _sweepTimeoutBarShort = -1;
            _mrUpperCrossArmed = false;
            _mrLowerCrossArmed = false;
            _mrRsiCrossunderArmed = false;
            _mrRsiCrossoverArmed = false;
        }

        private void UpdateSessionStructureContext()
        {
            if (!EnablePlayTypeSessionStructure)
                return;

            DateTime barDate = Time[0].Date;
            if (barDate != _sessionStructureDay)
            {
                _sessionStructureDay = barDate;
                _sessionRangeBuilding = false;
                _sessionRangeFrozen = false;
                _sessionRangeHigh = double.NaN;
                _sessionRangeLow = double.NaN;
                _sessionLongFiredToday = false;
                _sessionShortFiredToday = false;
            }

            int startMinuteOfDay = SessionStructureStartHour * 60 + SessionStructureStartMinute;
            int endMinuteOfDay = startMinuteOfDay + Math.Max(1, SessionStructureRangeDurationMinutes);
            int currentMinuteOfDay = Time[0].Hour * 60 + Time[0].Minute;

            if (currentMinuteOfDay >= startMinuteOfDay && currentMinuteOfDay < endMinuteOfDay)
            {
                if (!_sessionRangeBuilding)
                {
                    _sessionRangeBuilding = true;
                    _sessionRangeFrozen = false;
                    _sessionRangeHigh = High[0];
                    _sessionRangeLow = Low[0];
                }
                else
                {
                    _sessionRangeHigh = Math.Max(_sessionRangeHigh, High[0]);
                    _sessionRangeLow = Math.Min(_sessionRangeLow, Low[0]);
                }
            }
            else if (_sessionRangeBuilding && currentMinuteOfDay >= endMinuteOfDay)
            {
                _sessionRangeBuilding = false;
                _sessionRangeFrozen = !double.IsNaN(_sessionRangeHigh) && !double.IsNaN(_sessionRangeLow);
            }
        }

        private bool IsGlobalEntryAllowed()
        {
            if (SharedEnableDailyLimits && _dailyLimitReached)
                return false;

            if (SharedTimeFilterEnabled && !IsWithinTradingHours())
                return false;

            if (MaxTradesPerDay > 0 && _dailyEntriesTotal >= MaxTradesPerDay)
            {
                _lastSelectedReason = string.Format(
                    CultureInfo.InvariantCulture,
                    "Blocked Daily Cap {0}/{1}",
                    _dailyEntriesTotal,
                    MaxTradesPerDay);
                return false;
            }

            return true;
        }

        private bool IsPerBookEntryAllowed(PlayBook book, out int dayCount, out int maxAllowed)
        {
            dayCount = ResolveDailyEntryCountByBook(book);
            maxAllowed = ResolveMaxTradesPerDayByBook(book);
            if (maxAllowed <= 0)
                return true;
            return dayCount < maxAllowed;
        }

        private bool IsWithinTradingHours()
        {
            int hour = Time[0].Hour;
            if (hour < SharedTradingStartHour || hour >= SharedTradingEndHour)
                return false;
            if (hour >= SharedAvoidWindowStartHour && hour < SharedAvoidWindowEndHour)
                return false;
            return true;
        }

        private void TryFinalizeTapeOnFlatTransition()
        {
            if (!EnableTapeExport || _tradeTapeFinalized || _tradeEntryTime == DateTime.MinValue || double.IsNaN(_tradeEntryPrice))
                return;

            // If execution callbacks didn't provide exit fills, synthesize one at close.
            if (_tradeExitWeightedQtySum <= 0)
            {
                int qty = Math.Max(1, _activeEntryQuantity);
                _tradeExitWeightedQtySum = qty;
                _tradeExitWeightedPriceSum = Close[0] * qty;
                _tradeExitQtyOther += qty;

                if (_tradeRealizedPnlCurrency == 0)
                {
                    double pointValue = Instrument != null && Instrument.MasterInstrument != null
                        ? Instrument.MasterInstrument.PointValue
                        : 1.0;
                    double pnlPoints = _tradeDirection == "LONG" ? (Close[0] - _tradeEntryPrice) : (_tradeEntryPrice - Close[0]);
                    _tradeRealizedPnlCurrency = pnlPoints * qty * pointValue;
                }

                TapeDebug(string.Format("Flat-transition synthesized exit; qty={0} close={1:F2}", qty, Close[0]));
            }

            _tradeRemainingQty = 0;
            FinalizeTradeTapeRow(Time[0]);
        }

        private void IncrementSelectionCounter(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: _diagSelectedDf++; break;
                case PlayBook.GlitchCompressionBreakout: _diagSelectedCb++; break;
                case PlayBook.GlitchSweepBos: _diagSelectedSb++; break;
                case PlayBook.GlitchSessionStructure: _diagSelectedSs++; break;
                case PlayBook.GlitchMeanReversion: _diagSelectedMr++; break;
            }
        }

        private void IncrementEntryCounter(PlayBook book)
        {
            _dailyEntriesTotal++;
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip:
                    _diagEntriesDf++;
                    _dailyEntriesDf++;
                    break;
                case PlayBook.GlitchCompressionBreakout:
                    _diagEntriesCb++;
                    _dailyEntriesCb++;
                    break;
                case PlayBook.GlitchSweepBos:
                    _diagEntriesSb++;
                    _dailyEntriesSb++;
                    break;
                case PlayBook.GlitchSessionStructure:
                    _diagEntriesSs++;
                    _dailyEntriesSs++;
                    break;
                case PlayBook.GlitchMeanReversion:
                    _diagEntriesMr++;
                    _dailyEntriesMr++;
                    break;
            }
        }

        private void TryDiagnosticsLog()
        {
            if (!EnableDiagnosticsLogs || DiagnosticsLogEveryNBars <= 0 || !IsFirstTickOfBar)
                return;
            if (CurrentBar % DiagnosticsLogEveryNBars != 0)
                return;

            Print(string.Format(
                "[GlitchDiag] bars={0} strictBlocked={1} selected(DF/CB/SB/SS/MR)={2}/{3}/{4}/{5}/{6} entries={7}/{8}/{9}/{10}/{11}",
                _diagBarsEvaluated,
                _diagStrictBlockedCount,
                _diagSelectedDf, _diagSelectedCb, _diagSelectedSb, _diagSelectedSs, _diagSelectedMr,
                _diagEntriesDf, _diagEntriesCb, _diagEntriesSb, _diagEntriesSs, _diagEntriesMr
            ));
        }

        private bool AreIndicatorsReadyForPlayBook(PlayBook book)
        {
            bool atrReady = ResolveExitModelByBook(book) != ExitModel.AtrMultiple || IsAtrReadyForBook(book);
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip:
                    return CurrentBar >= Math.Max(10, DirectionalFlipLength + 2)
                        && !double.IsNaN(_smaHighHtf[0]) && !double.IsNaN(_smaLowHtf[0]) && !double.IsNaN(_smaCloseHtf[0])
                        && atrReady;
                case PlayBook.GlitchCompressionBreakout:
                    return CurrentBar >= Math.Max(CompressionBreakoutVolumeLookback + 2, CompressionBreakoutAdxPeriod + 2)
                        && !double.IsNaN(_adxCompression[0]) && !double.IsNaN(_rsiCompression[0])
                        && atrReady;
                case PlayBook.GlitchSweepBos:
                    return CurrentBar >= Math.Max(Math.Max(SweepBosSwingLookback + 3, SweepBosBosLookback + 3), TrendQualityR2Lookback + 5)
                        && !double.IsNaN(_t3E6[0]) && !double.IsNaN(_t3E6[1])
                        && atrReady;
                case PlayBook.GlitchSessionStructure:
                    return (!SessionStructureUsePremarketBias || !double.IsNaN(_sessionBiasSma[0])) && atrReady;
                case PlayBook.GlitchMeanReversion:
                {
                    bool baseReady = !double.IsNaN(_mrStdDev[0]) && !double.IsNaN(_mrRsi[0]);
                    if (!baseReady) return false;
                    bool basisReady = MeanReversionUseT3Basis ? (!double.IsNaN(_mrT3E6[0]) && !double.IsNaN(_mrT3E6[1])) : !double.IsNaN(_mrBasis[0]);
                    return basisReady && atrReady;
                }
                default:
                    return true;
            }
        }

        private bool IsAtrReadyForBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return _atrDirectionalFlip != null && !double.IsNaN(_atrDirectionalFlip[0]);
                case PlayBook.GlitchCompressionBreakout: return _atrCompressionBreakout != null && !double.IsNaN(_atrCompressionBreakout[0]);
                case PlayBook.GlitchSweepBos: return _atrSweepBos != null && !double.IsNaN(_atrSweepBos[0]);
                case PlayBook.GlitchSessionStructure: return _atrSessionStructure != null && !double.IsNaN(_atrSessionStructure[0]);
                case PlayBook.GlitchMeanReversion: return _atrMeanReversion != null && !double.IsNaN(_atrMeanReversion[0]);
                default: return true;
            }
        }

        private void UpdateCompactStatusPanel()
        {
            const string panelTag = "GlitchCompactStatusPanel";
            if (!EnableCompactStatusPanel)
            {
                RemoveDrawObject(panelTag);
                return;
            }

            string posText = Position.MarketPosition == MarketPosition.Flat
                ? "Flat"
                : string.Format("{0} x{1}", Position.MarketPosition, Position.Quantity);

            string selectedCode = GetPlaybookCode(_selectedBook);
            string activeCode = GetPlaybookCode(_activeTradeBook);
            string line1 = string.Format("GLITCH  |  {0}  |  Sel:{1}", _routerState, selectedCode);
            string line2 = string.Format("Act:{0}  Pos:{1}", activeCode, posText);
            string line3 = string.Format("DayPnL:{0:F2}  StrictBlk:{1}", _dailyPnL, _diagStrictBlockedCount);
            string line4 = string.Format("Sel DF/CB/SB/SS/MR {0}/{1}/{2}/{3}/{4}", _diagSelectedDf, _diagSelectedCb, _diagSelectedSb, _diagSelectedSs, _diagSelectedMr);
            string line5 = string.Format(
                "DayEnt {0}/{1}  DF/CB/SB/SS/MR {2}/{3}/{4}/{5}/{6}",
                _dailyEntriesTotal,
                MaxTradesPerDay > 0 ? MaxTradesPerDay.ToString(CultureInfo.InvariantCulture) : "∞",
                _dailyEntriesDf,
                _dailyEntriesCb,
                _dailyEntriesSb,
                _dailyEntriesSs,
                _dailyEntriesMr);
            string line6 = string.Format("Last C:{0:F1}  {1}", _lastSelectedConfidence, _lastSelectedReason);

            string text = line1 + Environment.NewLine +
                          line2 + Environment.NewLine +
                          line3 + Environment.NewLine +
                          line4 + Environment.NewLine +
                          line5 + Environment.NewLine +
                          line6;

            Draw.TextFixed(
                this,
                panelTag,
                text,
                TextPosition.TopRight,
                Brushes.Gainsboro,
                new SimpleFont("Segoe UI", 11),
                Brushes.Transparent,
                Brushes.Transparent,
                0
            );
        }

        private List<PlayScore> EvaluatePlayScores()
        {
            List<PlayScore> scores = new List<PlayScore>(5);
            scores.Add(EvaluateDirectionalFlipPlayScore());
            scores.Add(EvaluateCompressionBreakoutPlayScore());
            scores.Add(EvaluateSweepBosPlayScore());
            scores.Add(EvaluateSessionStructurePlayScore());
            scores.Add(EvaluateMeanReversionPlayScore());
            return scores;
        }

        private PlayScore SelectBestPlayScore(List<PlayScore> scores)
        {
            PlayScore best = null;
            foreach (PlayScore score in scores)
            {
                if (score == null || !score.IsTradeable)
                    continue;

                if (best == null)
                {
                    best = score;
                    continue;
                }

                if (score.Confidence > best.Confidence)
                    best = score;
                else if (Math.Abs(score.Confidence - best.Confidence) < 0.0001 &&
                         GetPlaybookPriority(score.Book) < GetPlaybookPriority(best.Book))
                    best = score;
            }

            // Router lock: keep current selection for a few bars if lock is active.
            if (CurrentBar <= _playbookLockUntilBar && _selectedBook != PlayBook.None && best != null && best.Book != _selectedBook)
            {
                foreach (PlayScore score in scores)
                {
                    if (score.Book == _selectedBook && score.IsTradeable)
                        return score;
                }
            }

            return best;
        }

        private int GetPlaybookPriority(PlayBook book)
        {
            // Lower number = higher priority.
            switch (book)
            {
                case PlayBook.GlitchSessionStructure: return 1;
                case PlayBook.GlitchSweepBos: return 2;
                case PlayBook.GlitchCompressionBreakout: return 3;
                case PlayBook.GlitchDirectionalFlip: return 4;
                case PlayBook.GlitchMeanReversion: return 5;
                default: return 99;
            }
        }

        private PlayScore EvaluateDirectionalFlipPlayScore()
        {
            PlayScore score = new PlayScore
            {
                Book = PlayBook.GlitchDirectionalFlip,
                Type = PlayType.TrendContinuation,
                LongScore = 0,
                ShortScore = 0,
                Confidence = 0,
                IsTradeable = false,
                Reason = "No directional flip."
            };

            if (!EnablePlayTypeDirectionalFlip)
            {
                score.Reason = "Disabled.";
                return score;
            }

            if (StrictModeRequireIndicatorsReady && !AreIndicatorsReadyForPlayBook(PlayBook.GlitchDirectionalFlip))
            {
                _diagStrictBlockedCount++;
                score.Reason = "Indicators not ready (strict mode).";
                return score;
            }

            if (!_flipJustOccurred)
                return score;

            score.IsTradeable = true;
            score.Confidence = 85;
            if (_flipDirectionLong)
                score.LongScore = 85;
            else
                score.ShortScore = 85;

            score.Reason = _flipDirectionLong ? "HTF directional flip long." : "HTF directional flip short.";
            return score;
        }

        private PlayScore EvaluateCompressionBreakoutPlayScore()
        {
            PlayScore score = new PlayScore
            {
                Book = PlayBook.GlitchCompressionBreakout,
                Type = PlayType.BreakoutExpansion,
                Confidence = 0,
                IsTradeable = false,
                Reason = EnablePlayTypeCompressionBreakout ? "No compression breakout trigger." : "Disabled."
            };

            if (!EnablePlayTypeCompressionBreakout)
                return score;

            if (StrictModeRequireIndicatorsReady && !AreIndicatorsReadyForPlayBook(PlayBook.GlitchCompressionBreakout))
            {
                _diagStrictBlockedCount++;
                score.Reason = "Indicators not ready (strict mode).";
                return score;
            }

            int lb = Math.Max(5, CompressionBreakoutVolumeLookback);
            if (CurrentBar < lb + 2)
            {
                score.Reason = "Insufficient bars.";
                return score;
            }

            double volumeDirection = Close[0] > Close[1] ? Volume[0] : -Volume[0];
            double priorMax = double.MinValue;
            double priorMin = double.MaxValue;
            for (int i = 1; i <= lb; i++)
            {
                double vd = Close[i] > Close[i + 1] ? Volume[i] : -Volume[i];
                if (vd > priorMax) priorMax = vd;
                if (vd < priorMin) priorMin = vd;
            }

            bool longBreak = volumeDirection > 0 &&
                             _rsiCompression[0] <= CompressionBreakoutRsiLongMax &&
                             volumeDirection > priorMax;
            bool shortBreak = volumeDirection < 0 &&
                              _rsiCompression[0] >= CompressionBreakoutRsiShortMin &&
                              volumeDirection < priorMin;

            bool adxPass = !CompressionBreakoutUseAdxFilter || _adxCompression[0] >= CompressionBreakoutAdxMin;
            if (!adxPass || (!longBreak && !shortBreak))
                return score;

            score.IsTradeable = true;
            double adxBonus = CompressionBreakoutUseAdxFilter
                ? Math.Min(15, Math.Max(0, _adxCompression[0] - CompressionBreakoutAdxMin))
                : 8;
            score.Confidence = 65 + adxBonus;

            if (longBreak && !shortBreak)
                score.LongScore = score.Confidence;
            else if (shortBreak && !longBreak)
                score.ShortScore = score.Confidence;
            else
            {
                // If both fire (rare), bias to stronger directional breakout distance.
                double longDelta = Math.Abs(volumeDirection - priorMax);
                double shortDelta = Math.Abs(volumeDirection - priorMin);
                if (longDelta >= shortDelta) score.LongScore = score.Confidence;
                else score.ShortScore = score.Confidence;
            }

            score.Reason = "Directional volume breakout + RSI gate" +
                           (CompressionBreakoutUseAdxFilter ? " + ADX filter." : ".");
            return score;
        }

        private PlayScore EvaluateSweepBosPlayScore()
        {
            PlayScore score = new PlayScore
            {
                Book = PlayBook.GlitchSweepBos,
                Type = PlayType.LiquiditySweep,
                Confidence = 0,
                IsTradeable = false,
                Reason = EnablePlayTypeSweepBos ? "No sweep/BOS sequence completion." : "Disabled."
            };

            if (!EnablePlayTypeSweepBos)
                return score;

            if (StrictModeRequireIndicatorsReady && !AreIndicatorsReadyForPlayBook(PlayBook.GlitchSweepBos))
            {
                _diagStrictBlockedCount++;
                score.Reason = "Indicators not ready (strict mode).";
                return score;
            }

            int swingLook = Math.Max(5, SweepBosSwingLookback);
            int bosLook = Math.Max(2, SweepBosBosLookback);
            int seqBars = Math.Max(1, SweepBosMaxSequenceBars);
            if (CurrentBar < Math.Max(Math.Max(swingLook + 3, bosLook + 3), TrendQualityR2Lookback + 5))
            {
                score.Reason = "Insufficient bars.";
                return score;
            }

            // T3 and trend quality
            double t3 = ComputeT3Value();
            double t3Prev = ComputeT3Value(1);
            bool t3Up = t3 > t3Prev;
            bool t3Down = t3 < t3Prev;
            bool t3UpChain = t3 > t3Prev && t3Prev > ComputeT3Value(2) && ComputeT3Value(2) > ComputeT3Value(3);
            bool t3DownChain = t3 < t3Prev && t3Prev < ComputeT3Value(2) && ComputeT3Value(2) < ComputeT3Value(3);
            double r2 = ComputeCloseIndexR2(Math.Max(20, TrendQualityR2Lookback));
            bool trendUp = Close[0] > t3 && t3UpChain && r2 > SweepBosR2Min;
            bool trendDown = Close[0] < t3 && t3DownChain && r2 > SweepBosR2Min;

            // Sweep detection (current + previous for edge-trigger)
            double priorLowest = MIN(Low, swingLook)[1];
            double priorHighest = MAX(High, swingLook)[1];
            double prevPriorLowest = MIN(Low, swingLook)[2];
            double prevPriorHighest = MAX(High, swingLook)[2];

            bool bullishSweep = Low[0] < priorLowest && Close[0] > priorLowest;
            bool bearishSweep = High[0] > priorHighest && Close[0] < priorHighest;
            bool bullishSweepPrev = Low[1] < prevPriorLowest && Close[1] > prevPriorLowest;
            bool bearishSweepPrev = High[1] > prevPriorHighest && Close[1] < prevPriorHighest;
            bool newBullishSweep = bullishSweep && !bullishSweepPrev;
            bool newBearishSweep = bearishSweep && !bearishSweepPrev;

            // FSM phase 0->1 on sweep
            if (newBullishSweep)
            {
                _sweepPhaseLong = 1;
                _sweepTimeoutBarLong = CurrentBar + seqBars;
            }
            if (newBearishSweep)
            {
                _sweepPhaseShort = 1;
                _sweepTimeoutBarShort = CurrentBar + seqBars;
            }

            // Timeout reset
            if (_sweepPhaseLong > 0 && _sweepTimeoutBarLong >= 0 && CurrentBar > _sweepTimeoutBarLong)
            {
                _sweepPhaseLong = 0;
                _sweepTimeoutBarLong = -1;
            }
            if (_sweepPhaseShort > 0 && _sweepTimeoutBarShort >= 0 && CurrentBar > _sweepTimeoutBarShort)
            {
                _sweepPhaseShort = 0;
                _sweepTimeoutBarShort = -1;
            }

            // Phase 1->2 on T3 direction
            bool sweepSignalLong = _sweepPhaseLong == 1 && t3Up;
            bool sweepSignalShort = _sweepPhaseShort == 1 && t3Down;
            if (sweepSignalLong)
            {
                _sweepPhaseLong = 2;
                if (SweepBosRestartTimeoutAfterPhase2)
                    _sweepTimeoutBarLong = CurrentBar + seqBars;
            }
            if (sweepSignalShort)
            {
                _sweepPhaseShort = 2;
                if (SweepBosRestartTimeoutAfterPhase2)
                    _sweepTimeoutBarShort = CurrentBar + seqBars;
            }

            // Confirmation layer
            double bosLevelHigh = MAX(High, bosLook)[1];
            double bosLevelLow = MIN(Low, bosLook)[1];
            bool bosLong = SweepBosUseWickBos ? High[0] > bosLevelHigh : Close[0] > bosLevelHigh;
            bool bosShort = SweepBosUseWickBos ? Low[0] < bosLevelLow : Close[0] < bosLevelLow;
            bool strongBullClose = Close[0] >= High[0] - (High[0] - Low[0]) * 0.25;
            bool strongBearClose = Close[0] <= Low[0] + (High[0] - Low[0]) * 0.25;
            bool aboveT3 = Close[0] > t3;
            bool belowT3 = Close[0] < t3;

            bool confirmLong = (SweepBosRequireBos ? bosLong : true) &&
                               strongBullClose &&
                               (SweepBosRequireCloseBeyondT3 ? aboveT3 : true);
            bool confirmShort = (SweepBosRequireBos ? bosShort : true) &&
                                strongBearClose &&
                                (SweepBosRequireCloseBeyondT3 ? belowT3 : true);

            bool canBuy = _sweepPhaseLong == 2 && confirmLong && (SweepBosUseTrendFilter ? trendUp : true);
            bool canSell = _sweepPhaseShort == 2 && confirmShort && (SweepBosUseTrendFilter ? trendDown : true);

            if (!canBuy && !canSell)
                return score;

            // Consume sequence when fired
            if (canBuy)
            {
                _sweepPhaseLong = 0;
                _sweepTimeoutBarLong = -1;
            }
            if (canSell)
            {
                _sweepPhaseShort = 0;
                _sweepTimeoutBarShort = -1;
            }

            score.IsTradeable = true;
            double baseConfidence = 70;
            double r2Bonus = Math.Min(15, Math.Max(0, (r2 - SweepBosR2Min) * 100));
            score.Confidence = baseConfidence + r2Bonus;
            if (canBuy && !canSell)
                score.LongScore = score.Confidence;
            else if (canSell && !canBuy)
                score.ShortScore = score.Confidence;
            else
            {
                // Extremely rare tie.
                if (trendUp) score.LongScore = score.Confidence;
                else score.ShortScore = score.Confidence;
            }

            score.Reason = "Sweep -> T3 direction -> BOS confirmation.";
            return score;
        }

        private PlayScore EvaluateSessionStructurePlayScore()
        {
            PlayScore score = new PlayScore
            {
                Book = PlayBook.GlitchSessionStructure,
                Type = PlayType.SessionStructure,
                Confidence = 0,
                IsTradeable = false,
                Reason = EnablePlayTypeSessionStructure ? "No session structure breakout." : "Disabled."
            };

            if (!EnablePlayTypeSessionStructure)
                return score;

            if (StrictModeRequireIndicatorsReady && !AreIndicatorsReadyForPlayBook(PlayBook.GlitchSessionStructure))
            {
                _diagStrictBlockedCount++;
                score.Reason = "Indicators not ready (strict mode).";
                return score;
            }

            if (!_sessionRangeFrozen || double.IsNaN(_sessionRangeHigh) || double.IsNaN(_sessionRangeLow))
            {
                score.Reason = "Session range not ready.";
                return score;
            }

            int currentMinute = Time[0].Hour * 60 + Time[0].Minute;
            int cutoffMinute = SessionStructureTradingCutoffHour * 60 + SessionStructureTradingCutoffMinute;
            int startMinute = SessionStructureStartHour * 60 + SessionStructureStartMinute;
            int rangeEndMinute = startMinute + Math.Max(1, SessionStructureRangeDurationMinutes);
            if (currentMinute < rangeEndMinute || currentMinute > cutoffMinute)
            {
                score.Reason = "Outside session play window.";
                return score;
            }

            double buffer = Math.Max(0, SessionStructureBreakoutBufferTicks) * TickSize;
            bool longBreak = Close[0] > _sessionRangeHigh + buffer;
            bool shortBreak = Close[0] < _sessionRangeLow - buffer;

            if (SessionStructureSingleDirectionPerDay)
            {
                if (_sessionLongFiredToday) longBreak = false;
                if (_sessionShortFiredToday) shortBreak = false;
            }

            if (SessionStructureUsePremarketBias)
            {
                int lb = Math.Max(5, SessionStructurePremarketBiasLookbackBars);
                if (CurrentBar < lb + 1 || _sessionBiasSma == null || double.IsNaN(_sessionBiasSma[0]))
                    return score;
                double biasMid = _sessionBiasSma[0];
                bool biasLong = Close[0] >= biasMid;
                bool biasShort = Close[0] <= biasMid;
                if (!biasLong) longBreak = false;
                if (!biasShort) shortBreak = false;
            }

            if (!longBreak && !shortBreak)
                return score;

            score.IsTradeable = true;
            score.Confidence = 72;
            if (longBreak && !shortBreak) score.LongScore = score.Confidence;
            else if (shortBreak && !longBreak) score.ShortScore = score.Confidence;
            else
            {
                // Rare conflict: choose direction with larger distance beyond range.
                double longDist = Close[0] - _sessionRangeHigh;
                double shortDist = _sessionRangeLow - Close[0];
                if (longDist >= shortDist) score.LongScore = score.Confidence;
                else score.ShortScore = score.Confidence;
            }

            score.Reason = "Opening range breakout with session controls.";
            return score;
        }

        private PlayScore EvaluateMeanReversionPlayScore()
        {
            PlayScore score = new PlayScore
            {
                Book = PlayBook.GlitchMeanReversion,
                Type = PlayType.MeanReversion,
                Confidence = 0,
                IsTradeable = false,
                Reason = EnablePlayTypeMeanReversion ? "No mean reversion setup." : "Disabled."
            };

            if (!EnablePlayTypeMeanReversion)
                return score;

            if (StrictModeRequireIndicatorsReady && !AreIndicatorsReadyForPlayBook(PlayBook.GlitchMeanReversion))
            {
                _diagStrictBlockedCount++;
                score.Reason = "Indicators not ready (strict mode).";
                return score;
            }

            int lb = Math.Max(2, MeanReversionBbLength);
            if (CurrentBar < lb + 3)
            {
                score.Reason = "Insufficient bars.";
                return score;
            }

            double basis = MeanReversionUseT3Basis ? ComputeAdaptiveT3FromClose() : _mrBasis[0];
            double basisPrev = MeanReversionUseT3Basis ? ComputeAdaptiveT3FromClose(1) : _mrBasis[1];
            double dev = _mrStdDev[0] * MeanReversionBbMultiplier;
            double devPrev = _mrStdDev[1] * MeanReversionBbMultiplier;
            double upper = basis + dev;
            double lower = basis - dev;
            double upperPrev = basisPrev + devPrev;
            double lowerPrev = basisPrev - devPrev;

            bool basisUp = basis > basisPrev;
            bool basisUpPrev = basisPrev > (MeanReversionUseT3Basis ? ComputeAdaptiveT3FromClose(2) : _mrBasis[2]);
            bool basisTriggerSell = !basisUp && basisUpPrev;
            bool basisTriggerBuy = basisUp && !basisUpPrev;

            bool upperCrossNow = Close[0] > upper && Close[1] <= upperPrev;
            bool lowerCrossNow = Close[0] < lower && Close[1] >= lowerPrev;
            if (upperCrossNow) _mrUpperCrossArmed = true;
            if (lowerCrossNow) _mrLowerCrossArmed = true;

            bool rsiCrossUnderNow = _mrRsi[0] < MeanReversionRsiUpper && _mrRsi[1] >= MeanReversionRsiUpper;
            bool rsiCrossOverNow = _mrRsi[0] > MeanReversionRsiLower && _mrRsi[1] <= MeanReversionRsiLower;
            if (rsiCrossUnderNow) _mrRsiCrossunderArmed = true;
            if (rsiCrossOverNow) _mrRsiCrossoverArmed = true;

            bool shortSignal = _mrUpperCrossArmed && _mrRsiCrossunderArmed && basisTriggerSell;
            bool longSignal = _mrLowerCrossArmed && _mrRsiCrossoverArmed && basisTriggerBuy;

            if (!shortSignal && !longSignal)
                return score;

            // Consume/rearm exactly like original indicator behavior.
            if (shortSignal)
            {
                _mrUpperCrossArmed = false;
                _mrRsiCrossunderArmed = false;
                _mrLowerCrossArmed = false;
                _mrRsiCrossoverArmed = false;
            }
            if (longSignal)
            {
                _mrLowerCrossArmed = false;
                _mrRsiCrossoverArmed = false;
                _mrUpperCrossArmed = false;
                _mrRsiCrossunderArmed = false;
            }

            score.IsTradeable = true;
            score.Confidence = 63;
            if (longSignal && !shortSignal) score.LongScore = score.Confidence;
            else if (shortSignal && !longSignal) score.ShortScore = score.Confidence;
            else if (Close[0] >= basis) score.ShortScore = score.Confidence;
            else score.LongScore = score.Confidence;

            score.Reason = "BB/RSI/basis-turn mean reversion setup.";
            return score;
        }

        private void SubmitEntry(PlayBook book, PlayType type, bool goLong)
        {
            _tradeSequence++;
            string direction = goLong ? "L" : "S";
            string pbCode = GetPlaybookCode(book);
            string ptCode = GetPlayTypeCode(type);
            // Keep signal names compact to avoid NT signal-name truncation.
            _entrySignalTag = string.Format("E|{0}|{1}|{2}|{3}", pbCode, ptCode, direction, _tradeSequence);
            _exitTagTp1 = string.Format("X|{0}|TP1|{1}", pbCode, _tradeSequence);
            _exitTagTp2 = string.Format("X|{0}|TP2|{1}", pbCode, _tradeSequence);
            _exitTagStop = string.Format("X|{0}|STP|{1}", pbCode, _tradeSequence);
            _exitTagFlip = string.Format("X|{0}|FLP|{1}", pbCode, _tradeSequence);
            _exitTagNoProgress = string.Format("X|{0}|NPG|{1}", pbCode, _tradeSequence);
            _exitTagTime = string.Format("X|{0}|TIM|{1}", pbCode, _tradeSequence);

            _activeTradeBook = book;
            _activeTradeType = type;
            _tradeDirection = goLong ? "LONG" : "SHORT";
            _flipJustOccurred = false;
            IncrementEntryCounter(book);
            _activeEntryQuantity = ResolveEntryQuantityByBook(book);
            bool runnerEnabled = ResolveEnableRunnerByBook(book);
            int tp1Qty;
            int tp2Qty;
            int runnerQty;
            BuildBracketQuantities(_activeEntryQuantity, runnerEnabled, out tp1Qty, out tp2Qty, out runnerQty);
            _activeRunnerQuantity = runnerQty;
            if (book == PlayBook.GlitchSessionStructure)
            {
                if (goLong) _sessionLongFiredToday = true;
                else _sessionShortFiredToday = true;
            }

            if (goLong)
                EnterLong(_activeEntryQuantity, _entrySignalTag);
            else
                EnterShort(_activeEntryQuantity, _entrySignalTag);

            if (EnableDiagnosticsLogs)
                Print(string.Format("[Glitch] Entry submitted: {0}", _entrySignalTag));
        }

        private string GetPlaybookCode(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return "DF";
                case PlayBook.GlitchCompressionBreakout: return "CB";
                case PlayBook.GlitchSweepBos: return "SB";
                case PlayBook.GlitchSessionStructure: return "SS";
                case PlayBook.GlitchMeanReversion: return "MR";
                default: return "NA";
            }
        }

        private string GetPlayTypeCode(PlayType type)
        {
            switch (type)
            {
                case PlayType.TrendContinuation: return "TC";
                case PlayType.BreakoutExpansion: return "BE";
                case PlayType.LiquiditySweep: return "LS";
                case PlayType.SessionStructure: return "SE";
                case PlayType.MeanReversion: return "MR";
                default: return "NA";
            }
        }

        private void ManageActivePosition()
        {
            UpdateManagedProtectiveStop();

            if (TryMaxBarsExit())
                return;

            if (TryNoProgressExit())
                return;

            // DirectionalFlip invalidation: match V2 semantics with confirmation bars.
            if (_activeTradeBook == PlayBook.GlitchDirectionalFlip && EnablePlayTypeDirectionalFlip)
            {
                bool flipAgainstLong = _tradeDirection == "LONG" && _isFlip && !_bullish;
                bool flipAgainstShort = _tradeDirection == "SHORT" && _isFlip && _bullish;

                if (flipAgainstLong || flipAgainstShort)
                {
                    if (!_directionalFlipAgainstPending)
                    {
                        _directionalFlipAgainstPending = true;
                        _directionalFlipBarsSinceAgainst = 0;
                    }
                }

                if (_directionalFlipAgainstPending)
                    _directionalFlipBarsSinceAgainst++;

                if (_directionalFlipAgainstPending)
                {
                    bool backInFavor = (_tradeDirection == "LONG" && _bullish) ||
                                       (_tradeDirection == "SHORT" && !_bullish);
                    if (backInFavor)
                    {
                        _directionalFlipAgainstPending = false;
                        _directionalFlipBarsSinceAgainst = 0;
                    }
                    else if (_directionalFlipBarsSinceAgainst >= Math.Max(1, DirectionalFlipFlipConfirmBars))
                    {
                        CancelAllExitOrders();
                        if (Position.MarketPosition == MarketPosition.Long)
                            ExitLong(_exitTagFlip, _entrySignalTag);
                        else if (Position.MarketPosition == MarketPosition.Short)
                            ExitShort(_exitTagFlip, _entrySignalTag);
                    }
                }
            }
        }

        private void UpdateManagedProtectiveStop()
        {
            MarketPosition direction = Position.MarketPosition;
            if (direction == MarketPosition.Flat || double.IsNaN(_entryPrice))
                return;

            double pointValueInPrice = ResolvePointValueInPrice();
            int qty = Math.Max(1, Position.Quantity);

            double stopPoints = ResolveStopPointsByActiveBook();
            double trailPoints = ResolveTrailPointsByActiveBook();
            double beTrigger = ResolveBreakevenTriggerByActiveBook();

            if (double.IsNaN(_extremePriceSinceEntry))
                _extremePriceSinceEntry = _entryPrice;

            // Use bar highs/lows for deterministic backtest/playback/live behavior.
            double currentHigh = High[0];
            double currentLow = Low[0];

            if (direction == MarketPosition.Long)
                _extremePriceSinceEntry = Math.Max(_extremePriceSinceEntry, Math.Max(High[0], currentHigh));
            else
                _extremePriceSinceEntry = Math.Min(_extremePriceSinceEntry, Math.Min(Low[0], currentLow));

            double favorablePoints = direction == MarketPosition.Long
                ? (_extremePriceSinceEntry - _entryPrice) / pointValueInPrice
                : (_entryPrice - _extremePriceSinceEntry) / pointValueInPrice;

            bool enableBreakeven = ResolveEnableBreakevenByActiveBook();
            bool enableTrailing = ResolveEnableTrailingByActiveBook();
            bool breakevenArmed = enableBreakeven && favorablePoints >= beTrigger;
            if (breakevenArmed)
                _movedToBreakeven = true;

            double initialStopDistance = stopPoints * pointValueInPrice;
            double trailDistance = trailPoints * pointValueInPrice;

            double desiredStop = direction == MarketPosition.Long
                ? _entryPrice - initialStopDistance
                : _entryPrice + initialStopDistance;

            if (breakevenArmed)
            {
                if (enableTrailing)
                {
                    desiredStop = direction == MarketPosition.Long
                        ? _extremePriceSinceEntry - trailDistance
                        : _extremePriceSinceEntry + trailDistance;
                }
                else
                {
                    desiredStop = _entryPrice;
                }

                if (direction == MarketPosition.Long && desiredStop < _entryPrice)
                    desiredStop = _entryPrice;
                if (direction == MarketPosition.Short && desiredStop > _entryPrice)
                    desiredStop = _entryPrice;
            }

            if (!double.IsNaN(_currentStopPrice))
            {
                if (direction == MarketPosition.Long)
                    desiredStop = Math.Max(desiredStop, _currentStopPrice);
                else
                    desiredStop = Math.Min(desiredStop, _currentStopPrice);
            }

            bool missingStop = !IsOrderActive(_stopOrder);
            bool qtyMismatch = _stopOrder != null && IsOrderActive(_stopOrder) && _stopOrder.Quantity != qty;
            bool betterPrice = double.IsNaN(_currentStopPrice)
                || (direction == MarketPosition.Long && desiredStop > _currentStopPrice + TickSize)
                || (direction == MarketPosition.Short && desiredStop < _currentStopPrice - TickSize);

            if (missingStop || qtyMismatch || betterPrice)
                UpsertStop(direction, qty, desiredStop);
        }

        private bool TryNoProgressExit()
        {
            if (!SharedEnableNoProgressExit ||
                Position.MarketPosition == MarketPosition.Flat ||
                _tradeEntryBar < 0 ||
                double.IsNaN(_entryPrice) ||
                _tp1Filled)
                return false;

            int barsInTrade = CurrentBar - _tradeEntryBar;
            if (barsInTrade < Math.Max(1, SharedNoProgressBars))
                return false;

            double pointValueInPrice = ResolvePointValueInPrice();
            if (pointValueInPrice <= 0)
                return false;

            double stopPoints = ResolveStopPointsByActiveBook();
            double tp1Points = ResolveTp1PointsByActiveBook();
            if (stopPoints <= 0 || tp1Points <= 0)
                return false;

            double minFavorablePoints = Math.Max(0, tp1Points * Math.Max(0, SharedNoProgressMinProgressTp1Fraction));
            double adverseThresholdPoints = Math.Max(0.25, stopPoints * Math.Max(0.05, SharedNoProgressAdverseStopFraction));

            double favorablePoints;
            double adversePoints;
            if (Position.MarketPosition == MarketPosition.Long)
            {
                favorablePoints = (High[0] - _entryPrice) / pointValueInPrice;
                adversePoints = (_entryPrice - Low[0]) / pointValueInPrice;
            }
            else
            {
                favorablePoints = (_entryPrice - Low[0]) / pointValueInPrice;
                adversePoints = (High[0] - _entryPrice) / pointValueInPrice;
            }

            if (favorablePoints >= minFavorablePoints || adversePoints < adverseThresholdPoints)
                return false;

            CancelAllExitOrders();
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(_exitTagNoProgress, _entrySignalTag);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(_exitTagNoProgress, _entrySignalTag);

            return true;
        }

        private bool TryMaxBarsExit()
        {
            if (!SharedEnableMaxBarsExit ||
                Position.MarketPosition == MarketPosition.Flat ||
                _tradeEntryBar < 0 ||
                SharedMaxBarsInTrade <= 0)
                return false;

            int barsInTrade = CurrentBar - _tradeEntryBar;
            if (barsInTrade < SharedMaxBarsInTrade)
                return false;

            CancelAllExitOrders();
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(_exitTagTime, _entrySignalTag);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(_exitTagTime, _entrySignalTag);

            return true;
        }

        private void UpsertStop(MarketPosition direction, int quantity, double stopPrice)
        {
            if (quantity <= 0 || double.IsNaN(stopPrice))
                return;

            bool canChange = _stopOrder != null &&
                             (_stopOrder.OrderState == OrderState.Working ||
                              _stopOrder.OrderState == OrderState.Accepted ||
                              _stopOrder.OrderState == OrderState.Submitted);

            if (canChange)
            {
                ChangeOrder(_stopOrder, quantity, 0, stopPrice);
                _currentStopPrice = stopPrice;
                return;
            }

            if (IsOrderActive(_stopOrder))
                return;

            if (direction == MarketPosition.Long)
                _stopOrder = ExitLongStopMarket(0, true, quantity, stopPrice, _exitTagStop, _entrySignalTag);
            else
                _stopOrder = ExitShortStopMarket(0, true, quantity, stopPrice, _exitTagStop, _entrySignalTag);

            _currentStopPrice = stopPrice;
        }

        private void UpsertProfitTargets(MarketPosition direction, int tp1Qty, int tp2Qty, double tp1Price, double tp2Price)
        {
            _tp1Order = UpsertLimitTarget(_tp1Order, direction, tp1Qty, tp1Price, _exitTagTp1);
            _tp2Order = UpsertLimitTarget(_tp2Order, direction, tp2Qty, tp2Price, _exitTagTp2);
        }

        private Order UpsertLimitTarget(Order existingOrder, MarketPosition direction, int quantity, double limitPrice, string signalName)
        {
            if (quantity <= 0 || double.IsNaN(limitPrice))
            {
                if (IsOrderActive(existingOrder))
                    CancelOrder(existingOrder);
                return null;
            }

            bool canChange = existingOrder != null &&
                             (existingOrder.OrderState == OrderState.Working ||
                              existingOrder.OrderState == OrderState.Accepted ||
                              existingOrder.OrderState == OrderState.Submitted);

            if (canChange)
            {
                ChangeOrder(existingOrder, quantity, limitPrice, 0);
                return existingOrder;
            }

            if (IsOrderActive(existingOrder))
                return existingOrder;

            if (direction == MarketPosition.Long)
                return ExitLongLimit(0, true, quantity, limitPrice, signalName, _entrySignalTag);

            return ExitShortLimit(0, true, quantity, limitPrice, signalName, _entrySignalTag);
        }

        protected override void OnExecutionUpdate(
            Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            Order order = execution.Order;
            string orderName = order.Name ?? string.Empty;

            bool isEntryExecution = !string.IsNullOrEmpty(_entrySignalTag) &&
                                    orderName == _entrySignalTag &&
                                    quantity > 0;
            bool isTaggedExit =
                orderName == _exitTagTp1 ||
                orderName == _exitTagTp2 ||
                orderName == _exitTagStop ||
                orderName == _exitTagFlip ||
                orderName == _exitTagNoProgress ||
                orderName == _exitTagTime;
            bool couldBeExitFill = order.OrderState == OrderState.Filled &&
                                   (isTaggedExit || (!string.IsNullOrEmpty(_entrySignalTag) && orderName != _entrySignalTag && !double.IsNaN(_entryPrice)));

            // Entry execution (supports partial fills)
            if (isEntryExecution)
            {
                bool firstEntryExecutionForTrade = _tradeEntryTime == DateTime.MinValue || double.IsNaN(_tradeEntryPrice);
                double resolvedEntryPrice = order.AverageFillPrice;
                if (double.IsNaN(resolvedEntryPrice) || resolvedEntryPrice <= 0)
                {
                    if (!double.IsNaN(Position.AveragePrice) && Position.AveragePrice > 0)
                        resolvedEntryPrice = Position.AveragePrice;
                    else
                        resolvedEntryPrice = price;
                }

                _entryPrice = resolvedEntryPrice;
                _extremePriceSinceEntry = _entryPrice;
                _movedToBreakeven = false;
                int entryQty = Math.Max(1, Math.Max(order.Filled, Math.Max(order.Quantity, Position.Quantity)));
                bool runnerEnabled = ResolveEnableRunnerByBook(_activeTradeBook);
                int tp1Qty;
                int tp2Qty;
                int runnerQty;
                BuildBracketQuantities(entryQty, runnerEnabled, out tp1Qty, out tp2Qty, out runnerQty);
                _activeEntryQuantity = entryQty;
                _activeRunnerQuantity = runnerQty;
                if (firstEntryExecutionForTrade)
                {
                    _routerState = RouterState.InPosition;
                    _tradeEntryTime = time;
                    _tradeExitWeightedPriceSum = 0;
                    _tradeExitWeightedQtySum = 0;
                    _tradeRealizedPnlCurrency = 0;
                    _tradeEntryConfidence = _lastSelectedConfidence;
                    _tradeEntryBar = CurrentBar;
                    _tradeEntryReason = _lastSelectedReason ?? string.Empty;
                    _tradeExitQtyTp1 = 0;
                    _tradeExitQtyTp2 = 0;
                    _tradeExitQtyStop = 0;
                    _tradeExitQtyFlip = 0;
                    _tradeExitQtyOther = 0;
                    _tradeTapeFinalized = false;
                }

                _tp1Filled = tp1Qty == 0 || _tradeExitQtyTp1 > 0;
                _tp2Filled = tp2Qty == 0 || _tradeExitQtyTp2 > 0;

                double pointValueInPrice = ResolvePointValueInPrice();
                double stopPrice;
                double tp1Price;
                double tp2Price;

                double stopPoints = ResolveStopPointsByActiveBook();
                double tp1Points = ResolveTp1PointsByActiveBook();
                double tp2Points = ResolveTp2PointsByActiveBook();

                MarketPosition direction = _tradeDirection == "LONG" ? MarketPosition.Long : MarketPosition.Short;
                if (direction == MarketPosition.Long)
                {
                    stopPrice = _entryPrice - (stopPoints * pointValueInPrice);
                    tp1Price = _entryPrice + (tp1Points * pointValueInPrice);
                    tp2Price = _entryPrice + (tp2Points * pointValueInPrice);
                }
                else
                {
                    stopPrice = _entryPrice + (stopPoints * pointValueInPrice);
                    tp1Price = _entryPrice - (tp1Points * pointValueInPrice);
                    tp2Price = _entryPrice - (tp2Points * pointValueInPrice);
                }

                UpsertStop(direction, entryQty, stopPrice);
                UpsertProfitTargets(direction, tp1Qty, tp2Qty, tp1Price, tp2Price);

                _tradeEntryPrice = _entryPrice;
                _tradeRemainingQty = Math.Max(0, entryQty - _tradeExitWeightedQtySum);
                _tradeEntryStopPoints = stopPoints;
                _tradeEntryTp1Points = tp1Points;
                _tradeEntryTp2Points = tp2Points;
                _tradeEntryTrailPoints = ResolveTrailPointsByActiveBook();
                _tradeEntryBreakevenPoints = ResolveBreakevenTriggerByActiveBook();
                _tradeEntryRrTp2 = stopPoints > 0 ? tp2Points / stopPoints : 0;
                _tradeEntryAtr = ResolveAtrValueByBook(_activeTradeBook);
                _tradeEntryExitModel = ResolveExitModelByBook(_activeTradeBook);

                if (firstEntryExecutionForTrade)
                {
                    if (_entryTelemetryContexts == null)
                        _entryTelemetryContexts = new List<EntryTelemetryContext>(4096);

                    bool ctxValid =
                        _tradeEntryStopPoints > 0 &&
                        _tradeEntryTp1Points > 0 &&
                        _tradeEntryTp2Points > 0 &&
                        _tradeEntryRrTp2 > 0 &&
                        !double.IsNaN(_tradeEntryAtr) &&
                        _tradeEntryAtr > 0;

                    _entryTelemetryContexts.Add(new EntryTelemetryContext
                    {
                        EntryTime = time,
                        EntryName = _entrySignalTag ?? string.Empty,
                        EntryPrice = _entryPrice,
                        PlaybookCode = GetPlaybookCode(_activeTradeBook),
                        PlaytypeCode = GetPlayTypeCode(_activeTradeType),
                        Direction = _tradeDirection == "LONG" ? "L" : "S",
                        StopPoints = _tradeEntryStopPoints,
                        Tp1Points = _tradeEntryTp1Points,
                        Tp2Points = _tradeEntryTp2Points,
                        TrailPoints = _tradeEntryTrailPoints,
                        BreakevenPoints = _tradeEntryBreakevenPoints,
                        RrTp2Stop = _tradeEntryRrTp2,
                        EntryAtr = _tradeEntryAtr,
                        ExitModel = _tradeEntryExitModel.ToString(),
                        EntryConfidence = _tradeEntryConfidence,
                        EntryReason = _tradeEntryReason ?? string.Empty,
                        Valid = ctxValid
                    });
                }

                if (EnableDiagnosticsLogs)
                    Print(string.Format("[Glitch] Entry execution: {0} @ {1:F2} qty={2}", orderName, _entryPrice, entryQty));
            }

            // TP fills
            if (orderName == _exitTagTp1 && order.OrderState == OrderState.Filled && !_tp1Filled)
            {
                _tp1Filled = true;
                UpdateManagedProtectiveStop();
            }
            else if (orderName == _exitTagTp2 && order.OrderState == OrderState.Filled && !_tp2Filled)
            {
                _tp2Filled = true;
                UpdateManagedProtectiveStop();
            }

            if (couldBeExitFill && !double.IsNaN(_entryPrice))
            {
                double pointValue = Instrument != null && Instrument.MasterInstrument != null
                    ? Instrument.MasterInstrument.PointValue
                    : 1.0;
                double pnlPoints = 0;
                if (_tradeDirection == "LONG")
                    pnlPoints = price - _entryPrice;
                else if (_tradeDirection == "SHORT")
                    pnlPoints = _entryPrice - price;

                double tradePnL = pnlPoints * quantity * pointValue;
                _tradeRealizedPnlCurrency += tradePnL;
                if (SharedEnableDailyLimits)
                {
                    _dailyPnL += tradePnL;
                    if (_dailyPnL >= SharedDailyProfitTarget || _dailyPnL <= -SharedDailyLossLimit)
                        _dailyLimitReached = true;
                }
            }

            if (couldBeExitFill)
            {
                bool finalizedTrade = false;

                if (couldBeExitFill && _tradeRemainingQty > 0)
                {
                    int fillQty = Math.Max(1, quantity);
                    if (orderName == _exitTagTp1) _tradeExitQtyTp1 += fillQty;
                    else if (orderName == _exitTagTp2) _tradeExitQtyTp2 += fillQty;
                    else if (orderName == _exitTagStop) _tradeExitQtyStop += fillQty;
                    else if (orderName == _exitTagFlip) _tradeExitQtyFlip += fillQty;
                    else _tradeExitQtyOther += fillQty;
                    _tradeExitWeightedPriceSum += price * fillQty;
                    _tradeExitWeightedQtySum += fillQty;
                    _tradeRemainingQty = Math.Max(0, _tradeRemainingQty - fillQty);
                    if (_tradeRemainingQty == 0)
                    {
                        FinalizeTradeTapeRow(time);
                        finalizedTrade = true;
                    }
                }

                // Backtest fill semantics can sometimes leave qty accounting non-zero.
                // If NT reports resulting position flat, force one final tape row.
                if (couldBeExitFill && !finalizedTrade && marketPosition == MarketPosition.Flat && _tradeExitWeightedQtySum > 0)
                {
                    _tradeRemainingQty = 0;
                    FinalizeTradeTapeRow(time);
                }
            }
        }

        protected override void OnOrderUpdate(
            Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice,
            OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            if (order == null)
                return;

            string name = order.Name ?? string.Empty;
            if (name == _exitTagStop)
                _stopOrder = order;
            else if (name == _exitTagTp1)
                _tp1Order = order;
            else if (name == _exitTagTp2)
                _tp2Order = order;
        }

        private void CancelAllExitOrders()
        {
            if (IsOrderActive(_stopOrder))
                CancelOrder(_stopOrder);
            if (IsOrderActive(_tp1Order))
                CancelOrder(_tp1Order);
            if (IsOrderActive(_tp2Order))
                CancelOrder(_tp2Order);
        }

        private bool IsOrderActive(Order order)
        {
            if (order == null)
                return false;

            return order.OrderState != OrderState.Cancelled &&
                   order.OrderState != OrderState.Rejected &&
                   order.OrderState != OrderState.Filled;
        }

        private void ComputeDirectionalFlipState()
        {
            if (CurrentBar < 2)
            {
                _isFlip = false;
                return;
            }

            double h = _smaHighHtf[0];
            double l = _smaLowHtf[0];
            double hp = _smaHighHtf[1];
            double lp = _smaLowHtf[1];
            double cp = _smaCloseHtf[1];

            double tr = Math.Max(h - l, Math.Max(Math.Abs(h - cp), Math.Abs(l - cp)));
            double up = h - hp;
            double dn = lp - l;
            double dmPlus = (up > dn && up > 0) ? up : 0;
            double dmMinus = (dn > up && dn > 0) ? dn : 0;

            if (double.IsNaN(_smoothedTr))
            {
                _smoothedTr = tr;
                _smoothedDmPlus = dmPlus;
                _smoothedDmMinus = dmMinus;
            }
            else
            {
                _smoothedTr = _smoothedTr - _smoothedTr / DirectionalFlipLength + tr;
                _smoothedDmPlus = _smoothedDmPlus - _smoothedDmPlus / DirectionalFlipLength + dmPlus;
                _smoothedDmMinus = _smoothedDmMinus - _smoothedDmMinus / DirectionalFlipLength + dmMinus;
            }

            double diPlus = _smoothedTr > 0 ? _smoothedDmPlus / _smoothedTr * 100.0 : 0;
            double diMinus = _smoothedTr > 0 ? _smoothedDmMinus / _smoothedTr * 100.0 : 0;

            if (diPlus > _prevDiPlus && diPlus > diMinus)
            {
                _posCount++;
                _negCount = 0;
            }

            if (diMinus > _prevDiMinus && diMinus > diPlus)
            {
                _negCount++;
                _posCount = 0;
            }

            _prevDiPlus = diPlus;
            _prevDiMinus = diMinus;

            _bullish = _posCount >= _negCount;
            _isFlip = _priorBullish.HasValue && _priorBullish.Value != _bullish;

            if (_isFlip)
            {
                _flipJustOccurred = true;
                _flipDirectionLong = _bullish;
            }

            _priorBullish = _bullish;
        }

        private double ComputeT3Value(int barsAgo = 0)
        {
            double e1 = _t3E1[barsAgo];
            double e2 = _t3E2[barsAgo];
            double e3 = _t3E3[barsAgo];
            double e4 = _t3E4[barsAgo];
            double e5 = _t3E5[barsAgo];
            double e6 = _t3E6[barsAgo];

            double vf = TrendQualityT3VolumeFactor;
            double c1 = vf;
            double c2 = vf * 3.0;
            double c3 = vf * 3.0;
            double c4 = vf;
            return e6 * c4 + e5 * -c3 + e4 * c2 + e3 * -c1 + e2;
        }

        private double ComputeAdaptiveT3FromClose(int barsAgo = 0)
        {
            double e2 = _mrT3E2[barsAgo];
            double e3 = _mrT3E3[barsAgo];
            double e4 = _mrT3E4[barsAgo];
            double e5 = _mrT3E5[barsAgo];
            double e6 = _mrT3E6[barsAgo];

            double vf = MeanReversionT3VolumeFactor;
            double c1 = -vf * vf * vf;
            double c2 = 3.0 * vf * vf + 3.0 * vf * vf * vf;
            double c3 = -6.0 * vf * vf - 3.0 * vf - 3.0 * vf * vf * vf;
            double c4 = 1.0 + 3.0 * vf + vf * vf * vf + 3.0 * vf * vf;
            return c1 * e6 + c2 * e5 + c3 * e4 + c4 * e3;
        }

        private double ComputeCloseIndexR2(int lookback)
        {
            int n = Math.Max(5, lookback);
            if (CurrentBar < n + 2)
                return 0;

            double sumX = 0;
            double sumY = 0;
            double sumXX = 0;
            double sumYY = 0;
            double sumXY = 0;
            for (int i = 0; i < n; i++)
            {
                double x = i;
                double y = Close[i];
                sumX += x;
                sumY += y;
                sumXX += x * x;
                sumYY += y * y;
                sumXY += x * y;
            }

            double num = n * sumXY - sumX * sumY;
            double denX = n * sumXX - sumX * sumX;
            double denY = n * sumYY - sumY * sumY;
            if (denX <= 0 || denY <= 0)
                return 0;

            double corr = num / Math.Sqrt(denX * denY);
            double r2 = corr * corr;
            if (double.IsNaN(r2) || double.IsInfinity(r2))
                return 0;
            return Math.Max(0, Math.Min(1, r2));
        }

        private double ResolveStopPointsByActiveBook()
        {
            if (ResolveExitModelByBook(_activeTradeBook) == ExitModel.AtrMultiple)
                return ResolveAtrDerivedPointsByBook(
                    _activeTradeBook,
                    ResolveAtrStopMultByBook(_activeTradeBook),
                    DirectionalFlipStopPoints,
                    CompressionBreakoutStopPoints,
                    SweepBosStopPoints,
                    SessionStructureStopPoints,
                    MeanReversionStopPoints,
                    SharedDefaultStopPoints);

            switch (_activeTradeBook)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipStopPoints;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutStopPoints;
                case PlayBook.GlitchSweepBos: return SweepBosStopPoints;
                case PlayBook.GlitchSessionStructure: return SessionStructureStopPoints;
                case PlayBook.GlitchMeanReversion: return MeanReversionStopPoints;
                default: return SharedDefaultStopPoints;
            }
        }

        private double ResolveTp1PointsByActiveBook()
        {
            if (ResolveExitModelByBook(_activeTradeBook) == ExitModel.AtrMultiple)
                return ResolveAtrDerivedPointsByBook(
                    _activeTradeBook,
                    ResolveAtrTp1MultByBook(_activeTradeBook),
                    DirectionalFlipTp1Points,
                    CompressionBreakoutTp1Points,
                    SweepBosTp1Points,
                    SessionStructureTp1Points,
                    MeanReversionTp1Points,
                    SharedDefaultTp1Points);

            switch (_activeTradeBook)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipTp1Points;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutTp1Points;
                case PlayBook.GlitchSweepBos: return SweepBosTp1Points;
                case PlayBook.GlitchSessionStructure: return SessionStructureTp1Points;
                case PlayBook.GlitchMeanReversion: return MeanReversionTp1Points;
                default: return SharedDefaultTp1Points;
            }
        }

        private double ResolveTp2PointsByActiveBook()
        {
            if (ResolveExitModelByBook(_activeTradeBook) == ExitModel.AtrMultiple)
                return ResolveAtrDerivedPointsByBook(
                    _activeTradeBook,
                    ResolveAtrTp2MultByBook(_activeTradeBook),
                    DirectionalFlipTp2Points,
                    CompressionBreakoutTp2Points,
                    SweepBosTp2Points,
                    SessionStructureTp2Points,
                    MeanReversionTp2Points,
                    SharedDefaultTp2Points);

            switch (_activeTradeBook)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipTp2Points;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutTp2Points;
                case PlayBook.GlitchSweepBos: return SweepBosTp2Points;
                case PlayBook.GlitchSessionStructure: return SessionStructureTp2Points;
                case PlayBook.GlitchMeanReversion: return MeanReversionTp2Points;
                default: return SharedDefaultTp2Points;
            }
        }

        private double ResolveTrailPointsByActiveBook()
        {
            if (ResolveExitModelByBook(_activeTradeBook) == ExitModel.AtrMultiple)
                return ResolveAtrDerivedPointsByBook(
                    _activeTradeBook,
                    ResolveAtrTrailMultByBook(_activeTradeBook),
                    DirectionalFlipTrailPoints,
                    SharedDefaultTrailPoints,
                    SharedDefaultTrailPoints,
                    SharedDefaultTrailPoints,
                    SharedDefaultTrailPoints,
                    SharedDefaultTrailPoints);

            switch (_activeTradeBook)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipTrailPoints;
                default: return SharedDefaultTrailPoints;
            }
        }

        private double ResolveBreakevenTriggerByActiveBook()
        {
            if (ResolveExitModelByBook(_activeTradeBook) == ExitModel.AtrMultiple)
                return ResolveAtrDerivedPointsByBook(
                    _activeTradeBook,
                    ResolveAtrBreakevenMultByBook(_activeTradeBook),
                    DirectionalFlipBreakevenTriggerPoints,
                    SharedDefaultBreakevenTriggerPoints,
                    SharedDefaultBreakevenTriggerPoints,
                    SharedDefaultBreakevenTriggerPoints,
                    SharedDefaultBreakevenTriggerPoints,
                    SharedDefaultBreakevenTriggerPoints);

            switch (_activeTradeBook)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipBreakevenTriggerPoints;
                default: return SharedDefaultBreakevenTriggerPoints;
            }
        }

        private ExitModel ResolveExitModelByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipExitModel;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutExitModel;
                case PlayBook.GlitchSweepBos: return SweepBosExitModel;
                case PlayBook.GlitchSessionStructure: return SessionStructureExitModel;
                case PlayBook.GlitchMeanReversion: return MeanReversionExitModel;
                default: return ExitModel.Fixed;
            }
        }

        private double ResolveAtrDerivedPointsByBook(
            PlayBook book,
            double multiple,
            double dfFallback,
            double cbFallback,
            double sbFallback,
            double ssFallback,
            double mrFallback,
            double defaultFallback)
        {
            double atr = ResolveAtrValueByBook(book);
            if (double.IsNaN(atr) || atr <= 0)
            {
                switch (book)
                {
                    case PlayBook.GlitchDirectionalFlip: return dfFallback;
                    case PlayBook.GlitchCompressionBreakout: return cbFallback;
                    case PlayBook.GlitchSweepBos: return sbFallback;
                    case PlayBook.GlitchSessionStructure: return ssFallback;
                    case PlayBook.GlitchMeanReversion: return mrFallback;
                    default: return defaultFallback;
                }
            }

            double pointsPerUnit = ResolvePointValueInPrice();
            if (pointsPerUnit <= 0)
                return defaultFallback;
            double points = atr / pointsPerUnit;
            double rawPoints = points * Math.Max(0.05, multiple);
            double minPoints = ResolveAtrMinPointsByBook(book);
            double maxPoints = ResolveAtrMaxPointsByBook(book);
            if (maxPoints < minPoints)
                maxPoints = minPoints;

            return Math.Max(minPoints, Math.Min(maxPoints, rawPoints));
        }

        private double ResolvePointValueInPrice()
        {
            if (TickSize <= 0)
                return 1.0;

            double ticksPerPoint = Math.Round(1.0 / TickSize);
            if (ticksPerPoint < 1)
                ticksPerPoint = 1;

            double pointValue = TickSize * ticksPerPoint;
            return pointValue > 0 ? pointValue : 1.0;
        }

        private double ResolveAtrValueByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return _atrDirectionalFlip != null ? _atrDirectionalFlip[0] : double.NaN;
                case PlayBook.GlitchCompressionBreakout: return _atrCompressionBreakout != null ? _atrCompressionBreakout[0] : double.NaN;
                case PlayBook.GlitchSweepBos: return _atrSweepBos != null ? _atrSweepBos[0] : double.NaN;
                case PlayBook.GlitchSessionStructure: return _atrSessionStructure != null ? _atrSessionStructure[0] : double.NaN;
                case PlayBook.GlitchMeanReversion: return _atrMeanReversion != null ? _atrMeanReversion[0] : double.NaN;
                default: return double.NaN;
            }
        }

        private double ResolveAtrStopMultByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipAtrStopMult;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutAtrStopMult;
                case PlayBook.GlitchSweepBos: return SweepBosAtrStopMult;
                case PlayBook.GlitchSessionStructure: return SessionStructureAtrStopMult;
                case PlayBook.GlitchMeanReversion: return MeanReversionAtrStopMult;
                default: return 1.5;
            }
        }

        private double ResolveAtrTp1MultByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipAtrTp1Mult;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutAtrTp1Mult;
                case PlayBook.GlitchSweepBos: return SweepBosAtrTp1Mult;
                case PlayBook.GlitchSessionStructure: return SessionStructureAtrTp1Mult;
                case PlayBook.GlitchMeanReversion: return MeanReversionAtrTp1Mult;
                default: return 1.0;
            }
        }

        private double ResolveAtrTp2MultByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipAtrTp2Mult;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutAtrTp2Mult;
                case PlayBook.GlitchSweepBos: return SweepBosAtrTp2Mult;
                case PlayBook.GlitchSessionStructure: return SessionStructureAtrTp2Mult;
                case PlayBook.GlitchMeanReversion: return MeanReversionAtrTp2Mult;
                default: return 2.0;
            }
        }

        private double ResolveAtrTrailMultByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipAtrTrailMult;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutAtrTrailMult;
                case PlayBook.GlitchSweepBos: return SweepBosAtrTrailMult;
                case PlayBook.GlitchSessionStructure: return SessionStructureAtrTrailMult;
                case PlayBook.GlitchMeanReversion: return MeanReversionAtrTrailMult;
                default: return 1.0;
            }
        }

        private double ResolveAtrBreakevenMultByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipAtrBreakevenMult;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutAtrBreakevenMult;
                case PlayBook.GlitchSweepBos: return SweepBosAtrBreakevenMult;
                case PlayBook.GlitchSessionStructure: return SessionStructureAtrBreakevenMult;
                case PlayBook.GlitchMeanReversion: return MeanReversionAtrBreakevenMult;
                default: return 1.0;
            }
        }

        private double ResolveAtrMinPointsByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipAtrMinPoints;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutAtrMinPoints;
                case PlayBook.GlitchSweepBos: return SweepBosAtrMinPoints;
                case PlayBook.GlitchSessionStructure: return SessionStructureAtrMinPoints;
                case PlayBook.GlitchMeanReversion: return MeanReversionAtrMinPoints;
                default: return 1;
            }
        }

        private double ResolveAtrMaxPointsByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipAtrMaxPoints;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutAtrMaxPoints;
                case PlayBook.GlitchSweepBos: return SweepBosAtrMaxPoints;
                case PlayBook.GlitchSessionStructure: return SessionStructureAtrMaxPoints;
                case PlayBook.GlitchMeanReversion: return MeanReversionAtrMaxPoints;
                default: return 400;
            }
        }

        private double ResolveStopPointsByBook(PlayBook book)
        {
            if (ResolveExitModelByBook(book) == ExitModel.AtrMultiple)
                return ResolveAtrDerivedPointsByBook(
                    book,
                    ResolveAtrStopMultByBook(book),
                    DirectionalFlipStopPoints,
                    CompressionBreakoutStopPoints,
                    SweepBosStopPoints,
                    SessionStructureStopPoints,
                    MeanReversionStopPoints,
                    SharedDefaultStopPoints);

            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipStopPoints;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutStopPoints;
                case PlayBook.GlitchSweepBos: return SweepBosStopPoints;
                case PlayBook.GlitchSessionStructure: return SessionStructureStopPoints;
                case PlayBook.GlitchMeanReversion: return MeanReversionStopPoints;
                default: return SharedDefaultStopPoints;
            }
        }

        private double ResolveTp2PointsByBook(PlayBook book)
        {
            if (ResolveExitModelByBook(book) == ExitModel.AtrMultiple)
                return ResolveAtrDerivedPointsByBook(
                    book,
                    ResolveAtrTp2MultByBook(book),
                    DirectionalFlipTp2Points,
                    CompressionBreakoutTp2Points,
                    SweepBosTp2Points,
                    SessionStructureTp2Points,
                    MeanReversionTp2Points,
                    SharedDefaultTp2Points);

            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipTp2Points;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutTp2Points;
                case PlayBook.GlitchSweepBos: return SweepBosTp2Points;
                case PlayBook.GlitchSessionStructure: return SessionStructureTp2Points;
                case PlayBook.GlitchMeanReversion: return MeanReversionTp2Points;
                default: return SharedDefaultTp2Points;
            }
        }

        private bool PassesMinRrGuard(PlayBook book, out double rr, out double minRr)
        {
            minRr = ResolveMinRrByBook(book);
            if (!ResolveEnableMinRrGuardByBook(book) || minRr <= 0)
            {
                rr = double.NaN;
                return true;
            }

            double stopPoints = ResolveStopPointsByBook(book);
            double tp2Points = ResolveTp2PointsByBook(book);
            if (stopPoints <= 0 || tp2Points <= 0)
            {
                rr = 0;
                return false;
            }

            rr = tp2Points / stopPoints;
            return rr >= minRr;
        }

        private bool ResolveEnableMinRrGuardByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipEnableMinRrGuard;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutEnableMinRrGuard;
                case PlayBook.GlitchSweepBos: return SweepBosEnableMinRrGuard;
                case PlayBook.GlitchSessionStructure: return SessionStructureEnableMinRrGuard;
                case PlayBook.GlitchMeanReversion: return MeanReversionEnableMinRrGuard;
                default: return false;
            }
        }

        private double ResolveMinRrByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipMinRr;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutMinRr;
                case PlayBook.GlitchSweepBos: return SweepBosMinRr;
                case PlayBook.GlitchSessionStructure: return SessionStructureMinRr;
                case PlayBook.GlitchMeanReversion: return MeanReversionMinRr;
                default: return 0;
            }
        }

        private int ResolveMinPlayScoreByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return Math.Max(0, DirectionalFlipMinPlayScore);
                case PlayBook.GlitchCompressionBreakout: return Math.Max(0, CompressionBreakoutMinPlayScore);
                case PlayBook.GlitchSweepBos: return Math.Max(0, SweepBosMinPlayScore);
                case PlayBook.GlitchSessionStructure: return Math.Max(0, SessionStructureMinPlayScore);
                case PlayBook.GlitchMeanReversion: return Math.Max(0, MeanReversionMinPlayScore);
                default: return Math.Max(0, MinPlayScoreToTrade);
            }
        }

        private int ResolveDailyEntryCountByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return _dailyEntriesDf;
                case PlayBook.GlitchCompressionBreakout: return _dailyEntriesCb;
                case PlayBook.GlitchSweepBos: return _dailyEntriesSb;
                case PlayBook.GlitchSessionStructure: return _dailyEntriesSs;
                case PlayBook.GlitchMeanReversion: return _dailyEntriesMr;
                default: return 0;
            }
        }

        private int ResolveMaxTradesPerDayByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return Math.Max(0, DirectionalFlipMaxTradesPerDay);
                case PlayBook.GlitchCompressionBreakout: return Math.Max(0, CompressionBreakoutMaxTradesPerDay);
                case PlayBook.GlitchSweepBos: return Math.Max(0, SweepBosMaxTradesPerDay);
                case PlayBook.GlitchSessionStructure: return Math.Max(0, SessionStructureMaxTradesPerDay);
                case PlayBook.GlitchMeanReversion: return Math.Max(0, MeanReversionMaxTradesPerDay);
                default: return 0;
            }
        }

        private bool PassesAtrRegimeGuard(PlayBook book, out double atrValue, out double atrMin, out double atrMax)
        {
            atrValue = ResolveAtrValueByBook(book);
            atrMin = ResolveAtrRegimeMinByBook(book);
            atrMax = ResolveAtrRegimeMaxByBook(book);

            if (!ResolveEnableAtrRegimeFilterByBook(book))
                return true;

            if (double.IsNaN(atrValue) || atrValue <= 0)
                return false;

            if (atrMax < atrMin)
                atrMax = atrMin;

            return atrValue >= atrMin && atrValue <= atrMax;
        }

        private bool ResolveEnableAtrRegimeFilterByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipEnableAtrRegimeFilter;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutEnableAtrRegimeFilter;
                case PlayBook.GlitchSweepBos: return SweepBosEnableAtrRegimeFilter;
                case PlayBook.GlitchSessionStructure: return SessionStructureEnableAtrRegimeFilter;
                case PlayBook.GlitchMeanReversion: return MeanReversionEnableAtrRegimeFilter;
                default: return false;
            }
        }

        private double ResolveAtrRegimeMinByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipAtrRegimeMin;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutAtrRegimeMin;
                case PlayBook.GlitchSweepBos: return SweepBosAtrRegimeMin;
                case PlayBook.GlitchSessionStructure: return SessionStructureAtrRegimeMin;
                case PlayBook.GlitchMeanReversion: return MeanReversionAtrRegimeMin;
                default: return 0;
            }
        }

        private double ResolveAtrRegimeMaxByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipAtrRegimeMax;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutAtrRegimeMax;
                case PlayBook.GlitchSweepBos: return SweepBosAtrRegimeMax;
                case PlayBook.GlitchSessionStructure: return SessionStructureAtrRegimeMax;
                case PlayBook.GlitchMeanReversion: return MeanReversionAtrRegimeMax;
                default: return 999;
            }
        }

        private bool PassesR2RegimeGuard(PlayBook book, out double r2Value, out double r2Min, out double r2Max)
        {
            r2Value = ComputeCloseIndexR2(Math.Max(20, TrendQualityR2Lookback));
            r2Min = ResolveR2RegimeMinByBook(book);
            r2Max = ResolveR2RegimeMaxByBook(book);

            if (!ResolveEnableR2RegimeFilterByBook(book))
                return true;

            if (double.IsNaN(r2Value) || r2Value < 0)
                return false;

            if (r2Max < r2Min)
                r2Max = r2Min;

            return r2Value >= r2Min && r2Value <= r2Max;
        }

        private bool ResolveEnableR2RegimeFilterByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipEnableR2RegimeFilter;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutEnableR2RegimeFilter;
                case PlayBook.GlitchSweepBos: return SweepBosEnableR2RegimeFilter;
                case PlayBook.GlitchSessionStructure: return SessionStructureEnableR2RegimeFilter;
                case PlayBook.GlitchMeanReversion: return MeanReversionEnableR2RegimeFilter;
                default: return false;
            }
        }

        private double ResolveR2RegimeMinByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipR2RegimeMin;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutR2RegimeMin;
                case PlayBook.GlitchSweepBos: return SweepBosR2RegimeMin;
                case PlayBook.GlitchSessionStructure: return SessionStructureR2RegimeMin;
                case PlayBook.GlitchMeanReversion: return MeanReversionR2RegimeMin;
                default: return 0;
            }
        }

        private double ResolveR2RegimeMaxByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipR2RegimeMax;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutR2RegimeMax;
                case PlayBook.GlitchSweepBos: return SweepBosR2RegimeMax;
                case PlayBook.GlitchSessionStructure: return SessionStructureR2RegimeMax;
                case PlayBook.GlitchMeanReversion: return MeanReversionR2RegimeMax;
                default: return 1;
            }
        }

        private int ResolveEntryQuantityByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return Math.Max(1, DirectionalFlipEntryQuantity);
                case PlayBook.GlitchCompressionBreakout: return Math.Max(1, CompressionBreakoutEntryQuantity);
                case PlayBook.GlitchSweepBos: return Math.Max(1, SweepBosEntryQuantity);
                case PlayBook.GlitchSessionStructure: return Math.Max(1, SessionStructureEntryQuantity);
                case PlayBook.GlitchMeanReversion: return Math.Max(1, MeanReversionEntryQuantity);
                default: return Math.Max(1, SharedEntryQuantity);
            }
        }

        private bool ResolveEnableRunnerByBook(PlayBook book)
        {
            switch (book)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipEnableRunner;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutEnableRunner;
                case PlayBook.GlitchSweepBos: return SweepBosEnableRunner;
                case PlayBook.GlitchSessionStructure: return SessionStructureEnableRunner;
                case PlayBook.GlitchMeanReversion: return MeanReversionEnableRunner;
                default: return true;
            }
        }

        private bool ResolveEnableBreakevenByActiveBook()
        {
            switch (_activeTradeBook)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipEnableBreakeven;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutEnableBreakeven;
                case PlayBook.GlitchSweepBos: return SweepBosEnableBreakeven;
                case PlayBook.GlitchSessionStructure: return SessionStructureEnableBreakeven;
                case PlayBook.GlitchMeanReversion: return MeanReversionEnableBreakeven;
                default: return SharedEnableBreakeven;
            }
        }

        private bool ResolveEnableTrailingByActiveBook()
        {
            switch (_activeTradeBook)
            {
                case PlayBook.GlitchDirectionalFlip: return DirectionalFlipEnableTrailingStop;
                case PlayBook.GlitchCompressionBreakout: return CompressionBreakoutEnableTrailingStop;
                case PlayBook.GlitchSweepBos: return SweepBosEnableTrailingStop;
                case PlayBook.GlitchSessionStructure: return SessionStructureEnableTrailingStop;
                case PlayBook.GlitchMeanReversion: return MeanReversionEnableTrailingStop;
                default: return SharedEnableTrailingStop;
            }
        }

        private static void BuildBracketQuantities(int totalQty, bool runnerEnabled, out int tp1Qty, out int tp2Qty, out int runnerQty)
        {
            int qty = Math.Max(1, totalQty);
            if (qty == 1)
            {
                tp1Qty = 0;
                tp2Qty = 1;
                runnerQty = 0;
                return;
            }

            if (runnerEnabled && qty >= 3)
            {
                tp1Qty = 1;
                tp2Qty = 1;
                runnerQty = qty - 2;
                return;
            }

            tp1Qty = 1;
            tp2Qty = qty - tp1Qty;
            runnerQty = 0;
        }

        private string ResolveTapeFilePath()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string userPath = TapeExportRelativePath ?? string.Empty;
            if (Path.IsPathRooted(userPath))
                return userPath;
            return Path.Combine(docs, "NinjaTrader 8", userPath);
        }

        private string ResolveGateAuditFilePath()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string userPath = GateAuditExportRelativePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userPath))
                userPath = @"Glitch\glitch-playbook-gate-audit.csv";
            if (Path.IsPathRooted(userPath))
                return userPath;
            return Path.Combine(docs, "NinjaTrader 8", userPath);
        }

        private void ExportGateAuditForBlockedCandidates(List<PlayScore> scores)
        {
            if (!EnableGateAuditExport || scores == null || string.IsNullOrEmpty(_gateAuditFilePathResolved))
                return;

            try
            {
                if (_gateAuditBuffer == null)
                    _gateAuditBuffer = new StringBuilder(4096);

                if (!_gateAuditHeaderWritten)
                {
                    _gateAuditBuffer.AppendLine("bar_time,bar_index,playbook,playtype,direction_bias,confidence,long_score,short_score,signal_reason,day_count,max_per_day,pass_day_cap,min_playscore,pass_min_playscore,rr,min_rr,pass_rr,atr,atr_min,atr_max,pass_atr,r2,r2_min,r2_max,pass_r2,blocked_reason,router_state,selected_book,close");
                    _gateAuditHeaderWritten = true;
                }

                string barTime = Time[0].ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                string routerState = _routerState.ToString();
                string selectedBook = GetPlaybookCode(_selectedBook);
                string closeValue = Close[0].ToString("F2", CultureInfo.InvariantCulture);

                foreach (PlayScore score in scores)
                {
                    if (score == null || !score.IsTradeable)
                        continue;

                    int dayCount;
                    int maxByBook;
                    bool passDay = IsPerBookEntryAllowed(score.Book, out dayCount, out maxByBook);
                    int minScore = ResolveMinPlayScoreByBook(score.Book);
                    bool passScore = score.Confidence >= minScore;

                    double rr;
                    double minRr;
                    bool passRr = PassesMinRrGuard(score.Book, out rr, out minRr);

                    double atrValue;
                    double atrMin;
                    double atrMax;
                    bool passAtr = PassesAtrRegimeGuard(score.Book, out atrValue, out atrMin, out atrMax);

                    double r2Value;
                    double r2Min;
                    double r2Max;
                    bool passR2 = PassesR2RegimeGuard(score.Book, out r2Value, out r2Min, out r2Max);

                    string blockedReason = string.Empty;
                    if (!passDay)
                        blockedReason = string.Format(CultureInfo.InvariantCulture, "DailyCap {0}/{1}", dayCount, maxByBook);
                    else if (!passScore)
                        blockedReason = string.Format(CultureInfo.InvariantCulture, "Score {0:F1} < Min {1}", score.Confidence, minScore);
                    else if (!passRr)
                        blockedReason = string.Format(CultureInfo.InvariantCulture, "RR {0:F2} < Min {1:F2}", rr, minRr);
                    else if (!passAtr)
                        blockedReason = string.Format(CultureInfo.InvariantCulture, "ATR {0:F2} not in [{1:F2},{2:F2}]", atrValue, atrMin, atrMax);
                    else if (!passR2)
                        blockedReason = string.Format(CultureInfo.InvariantCulture, "R2 {0:F3} not in [{1:F3},{2:F3}]", r2Value, r2Min, r2Max);

                    if (string.IsNullOrEmpty(blockedReason))
                        continue;

                    string directionBias = score.LongScore > score.ShortScore
                        ? "L"
                        : (score.ShortScore > score.LongScore ? "S" : "NA");

                    StringBuilder row = new StringBuilder(384);
                    AppendCsv(row, barTime);
                    AppendCsv(row, CurrentBar.ToString(CultureInfo.InvariantCulture));
                    AppendCsv(row, GetPlaybookCode(score.Book));
                    AppendCsv(row, GetPlayTypeCode(score.Type));
                    AppendCsv(row, directionBias);
                    AppendCsv(row, score.Confidence.ToString("F2", CultureInfo.InvariantCulture));
                    AppendCsv(row, score.LongScore.ToString("F2", CultureInfo.InvariantCulture));
                    AppendCsv(row, score.ShortScore.ToString("F2", CultureInfo.InvariantCulture));
                    AppendCsv(row, score.Reason ?? string.Empty);
                    AppendCsv(row, dayCount.ToString(CultureInfo.InvariantCulture));
                    AppendCsv(row, maxByBook.ToString(CultureInfo.InvariantCulture));
                    AppendCsv(row, passDay ? "1" : "0");
                    AppendCsv(row, minScore.ToString(CultureInfo.InvariantCulture));
                    AppendCsv(row, passScore ? "1" : "0");
                    AppendCsv(row, double.IsNaN(rr) ? string.Empty : rr.ToString("F4", CultureInfo.InvariantCulture));
                    AppendCsv(row, double.IsNaN(minRr) ? string.Empty : minRr.ToString("F4", CultureInfo.InvariantCulture));
                    AppendCsv(row, passRr ? "1" : "0");
                    AppendCsv(row, double.IsNaN(atrValue) ? string.Empty : atrValue.ToString("F4", CultureInfo.InvariantCulture));
                    AppendCsv(row, atrMin.ToString("F4", CultureInfo.InvariantCulture));
                    AppendCsv(row, atrMax.ToString("F4", CultureInfo.InvariantCulture));
                    AppendCsv(row, passAtr ? "1" : "0");
                    AppendCsv(row, double.IsNaN(r2Value) ? string.Empty : r2Value.ToString("F4", CultureInfo.InvariantCulture));
                    AppendCsv(row, r2Min.ToString("F4", CultureInfo.InvariantCulture));
                    AppendCsv(row, r2Max.ToString("F4", CultureInfo.InvariantCulture));
                    AppendCsv(row, passR2 ? "1" : "0");
                    AppendCsv(row, blockedReason);
                    AppendCsv(row, routerState);
                    AppendCsv(row, selectedBook);
                    AppendCsv(row, closeValue);
                    _gateAuditBuffer.AppendLine(row.ToString());
                    _gateAuditBufferedRows++;
                }

                if (_gateAuditBufferedRows >= Math.Max(1, GateAuditFlushEveryNRows))
                    FlushGateAuditBuffer();
            }
            catch (Exception ex)
            {
                TapeDebug(string.Format("Gate audit export failed: {0}: {1}", ex.GetType().Name, ex.Message));
            }
        }

        private bool TryTapeFileHasData(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;
            try
            {
                return File.Exists(filePath) && new FileInfo(filePath).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            string escaped = value.Replace("\"", "\"\"");
            return "\"" + escaped + "\"";
        }

        private static void AppendCsv(StringBuilder sb, string value)
        {
            if (sb.Length > 0)
                sb.Append(',');
            if (string.IsNullOrEmpty(value))
                return;

            bool mustQuote = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0;
            if (!mustQuote)
            {
                sb.Append(value);
                return;
            }

            sb.Append('"');
            sb.Append(value.Replace("\"", "\"\""));
            sb.Append('"');
        }

        private void TapeDebug(string message)
        {
            if (!EnableTapeDebugLogs)
                return;
            Print(string.Format("[GlitchTape] {0}", message));
        }

        private void ExportClosedTradesAuthoritative()
        {
            if (!EnableTapeExport || !UseAuthoritativeTradeExport || string.IsNullOrEmpty(_tapeFilePathResolved))
                return;

            if (SystemPerformance == null || SystemPerformance.AllTrades == null)
                return;

            int total = SystemPerformance.AllTrades.Count;
            if (total <= 0 || _lastExportedTradeIndex >= total - 1)
                return;

            try
            {
                if (_tapeBuffer == null)
                    _tapeBuffer = new StringBuilder(4096);

                if (!_tapeHeaderWritten)
                {
                    _tapeBuffer.AppendLine("trade_index,entry_time,exit_time,playbook,playtype,direction,entry_price,exit_price,pnl_currency,pnl_points,entry_name,exit_name,ctx_available,ctx_match_quality,ctx_valid,ctx_stop_points,ctx_tp1_points,ctx_tp2_points,ctx_trail_points,ctx_breakeven_points,ctx_rr_tp2_stop,ctx_entry_atr,ctx_exit_model,ctx_entry_confidence,ctx_entry_reason");
                    _tapeHeaderWritten = true;
                }

                for (int i = _lastExportedTradeIndex + 1; i < total; i++)
                {
                    Trade t = SystemPerformance.AllTrades[i];
                    if (t == null || t.Entry == null || t.Exit == null)
                        continue;

                    string entryName = t.Entry.Name ?? string.Empty;
                    string exitName = t.Exit.Name ?? string.Empty;
                    string pb = "NA";
                    string pt = "NA";
                    string dir = "NA";
                    TryParseEntryTag(entryName, out pb, out pt, out dir);
                    string matchQuality;
                    EntryTelemetryContext ctx = FindContextForAuthoritativeTrade(entryName, t.Entry.Time, t.Entry.Price, dir, out matchQuality);
                    if (ctx != null)
                    {
                        if (pb == "NA" && !string.IsNullOrEmpty(ctx.PlaybookCode)) pb = ctx.PlaybookCode;
                        if (pt == "NA" && !string.IsNullOrEmpty(ctx.PlaytypeCode)) pt = ctx.PlaytypeCode;
                        if (dir == "NA" && !string.IsNullOrEmpty(ctx.Direction)) dir = ctx.Direction;
                    }

                    double pnlCurrency = t.ProfitCurrency;
                    double pnlPoints = 0;
                    if (dir == "L")
                        pnlPoints = t.Exit.Price - t.Entry.Price;
                    else if (dir == "S")
                        pnlPoints = t.Entry.Price - t.Exit.Price;

                    StringBuilder row = new StringBuilder(256);
                    AppendCsv(row, i.ToString(CultureInfo.InvariantCulture));
                    AppendCsv(row, t.Entry.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    AppendCsv(row, t.Exit.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    AppendCsv(row, pb);
                    AppendCsv(row, pt);
                    AppendCsv(row, dir);
                    AppendCsv(row, t.Entry.Price.ToString("F2", CultureInfo.InvariantCulture));
                    AppendCsv(row, t.Exit.Price.ToString("F2", CultureInfo.InvariantCulture));
                    AppendCsv(row, pnlCurrency.ToString("F2", CultureInfo.InvariantCulture));
                    AppendCsv(row, pnlPoints.ToString("F2", CultureInfo.InvariantCulture));
                    AppendCsv(row, entryName);
                    AppendCsv(row, exitName);
                    AppendCsv(row, ctx != null ? "1" : "0");
                    AppendCsv(row, matchQuality);
                    AppendCsv(row, ctx != null && ctx.Valid ? "1" : "0");
                    AppendCsv(row, ctx != null && ctx.Valid ? ctx.StopPoints.ToString("F4", CultureInfo.InvariantCulture) : string.Empty);
                    AppendCsv(row, ctx != null && ctx.Valid ? ctx.Tp1Points.ToString("F4", CultureInfo.InvariantCulture) : string.Empty);
                    AppendCsv(row, ctx != null && ctx.Valid ? ctx.Tp2Points.ToString("F4", CultureInfo.InvariantCulture) : string.Empty);
                    AppendCsv(row, ctx != null && ctx.Valid ? ctx.TrailPoints.ToString("F4", CultureInfo.InvariantCulture) : string.Empty);
                    AppendCsv(row, ctx != null && ctx.Valid ? ctx.BreakevenPoints.ToString("F4", CultureInfo.InvariantCulture) : string.Empty);
                    AppendCsv(row, ctx != null && ctx.Valid ? ctx.RrTp2Stop.ToString("F4", CultureInfo.InvariantCulture) : string.Empty);
                    AppendCsv(row, ctx != null && ctx.Valid ? ctx.EntryAtr.ToString("F4", CultureInfo.InvariantCulture) : string.Empty);
                    AppendCsv(row, ctx != null && ctx.Valid ? ctx.ExitModel : string.Empty);
                    AppendCsv(row, ctx != null && ctx.Valid ? ctx.EntryConfidence.ToString("F2", CultureInfo.InvariantCulture) : string.Empty);
                    AppendCsv(row, ctx != null && ctx.Valid ? ctx.EntryReason : string.Empty);
                    _tapeBuffer.AppendLine(row.ToString());
                    _tapeBufferedRows++;
                }

                _lastExportedTradeIndex = total - 1;
                PruneConsumedTelemetryContexts();
                if (_tapeBufferedRows >= Math.Max(1, TapeFlushEveryNRows))
                    FlushTapeBuffer();
            }
            catch (Exception ex)
            {
                TapeDebug(string.Format("Authoritative export failed: {0}: {1}", ex.GetType().Name, ex.Message));
            }
        }

        private static void TryParseEntryTag(string entryTag, out string playbookCode, out string playtypeCode, out string direction)
        {
            playbookCode = "NA";
            playtypeCode = "NA";
            direction = "NA";
            if (string.IsNullOrEmpty(entryTag))
                return;

            string[] parts = entryTag.Split('|');
            if (parts.Length >= 5 && string.Equals(parts[0], "E", StringComparison.OrdinalIgnoreCase))
            {
                playbookCode = parts[1];
                playtypeCode = parts[2];
                direction = parts[3];
            }
        }

        private EntryTelemetryContext FindContextForAuthoritativeTrade(string entryName, DateTime entryTime, double entryPrice, string direction, out string matchQuality)
        {
            matchQuality = "none";
            if (_entryTelemetryContexts == null || _entryTelemetryContexts.Count == 0)
                return null;

            int bestExactIndex = -1;
            double bestExactScore = double.MaxValue;
            int bestFuzzyIndex = -1;
            double bestFuzzyScore = double.MaxValue;

            for (int i = 0; i < _entryTelemetryContexts.Count; i++)
            {
                EntryTelemetryContext ctx = _entryTelemetryContexts[i];
                if (ctx == null || !ctx.Valid)
                    continue;

                bool nameMatch = string.Equals(ctx.EntryName ?? string.Empty, entryName ?? string.Empty, StringComparison.Ordinal);
                bool directionMatch = direction == "NA" || string.IsNullOrEmpty(direction) || string.Equals(ctx.Direction, direction, StringComparison.Ordinal);

                double timeDiffSec = Math.Abs((ctx.EntryTime - entryTime).TotalSeconds);
                double priceDiffTicks = TickSize > 0
                    ? Math.Abs(ctx.EntryPrice - entryPrice) / TickSize
                    : Math.Abs(ctx.EntryPrice - entryPrice);
                double score = timeDiffSec + (priceDiffTicks * 0.5);

                if (nameMatch && directionMatch && timeDiffSec <= 2.0 && priceDiffTicks <= 1.5)
                {
                    if (score < bestExactScore)
                    {
                        bestExactScore = score;
                        bestExactIndex = i;
                    }
                    continue;
                }

                if (nameMatch && directionMatch && timeDiffSec <= 300.0)
                {
                    if (score < bestFuzzyScore)
                    {
                        bestFuzzyScore = score;
                        bestFuzzyIndex = i;
                    }
                }
            }

            if (bestExactIndex >= 0)
            {
                EntryTelemetryContext exact = _entryTelemetryContexts[bestExactIndex];
                matchQuality = "exact";
                return exact;
            }

            if (bestFuzzyIndex >= 0)
            {
                EntryTelemetryContext fuzzy = _entryTelemetryContexts[bestFuzzyIndex];
                matchQuality = "fuzzy";
                return fuzzy;
            }

            return null;
        }

        private void PruneConsumedTelemetryContexts()
        {
            if (_entryTelemetryContexts == null || _entryTelemetryContexts.Count == 0)
                return;

            _entryTelemetryContexts.RemoveAll(c => c == null || !c.Valid);
            const int hardCap = 20000;
            if (_entryTelemetryContexts.Count > hardCap)
                _entryTelemetryContexts.RemoveRange(0, _entryTelemetryContexts.Count - hardCap);
        }

        private void FinalizeTradeTapeRow(DateTime exitTime)
        {
            if (UseAuthoritativeTradeExport)
                return;

            if (!EnableTapeExport || _tradeExitWeightedQtySum <= 0 || string.IsNullOrEmpty(_tapeFilePathResolved))
            {
                TapeDebug(string.Format(
                    "Finalize skipped. enabled={0} qtySum={1} pathEmpty={2}",
                    EnableTapeExport,
                    _tradeExitWeightedQtySum,
                    string.IsNullOrEmpty(_tapeFilePathResolved)));
                return;
            }

            try
            {
                if (_tapeBuffer == null)
                    _tapeBuffer = new StringBuilder(4096);

                double avgExit = _tradeExitWeightedPriceSum / _tradeExitWeightedQtySum;
                string pb = GetPlaybookCode(_activeTradeBook);
                string pt = GetPlayTypeCode(_activeTradeType);
                string dir = _tradeDirection == "LONG" ? "L" : "S";
                double holdMinutes = _tradeEntryTime > DateTime.MinValue ? (exitTime - _tradeEntryTime).TotalMinutes : 0;
                int barsHeld = _tradeEntryBar >= 0 ? Math.Max(0, CurrentBar - _tradeEntryBar) : 0;
                string reason = CsvEscape(_tradeEntryReason);
                string row = string.Join(",",
                    _tradeSequence.ToString(CultureInfo.InvariantCulture),
                    _tradeEntryTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    exitTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    pb,
                    pt,
                    dir,
                    _tradeEntryPrice.ToString("F2", CultureInfo.InvariantCulture),
                    avgExit.ToString("F2", CultureInfo.InvariantCulture),
                    _tradeRealizedPnlCurrency.ToString("F2", CultureInfo.InvariantCulture),
                    _activeEntryQuantity.ToString(CultureInfo.InvariantCulture),
                    holdMinutes.ToString("F2", CultureInfo.InvariantCulture),
                    _tradeEntryStopPoints.ToString("F4", CultureInfo.InvariantCulture),
                    _tradeEntryTp1Points.ToString("F4", CultureInfo.InvariantCulture),
                    _tradeEntryTp2Points.ToString("F4", CultureInfo.InvariantCulture),
                    _tradeEntryTrailPoints.ToString("F4", CultureInfo.InvariantCulture),
                    _tradeEntryBreakevenPoints.ToString("F4", CultureInfo.InvariantCulture),
                    _tradeEntryRrTp2.ToString("F4", CultureInfo.InvariantCulture),
                    _tradeEntryAtr.ToString("F4", CultureInfo.InvariantCulture),
                    _tradeEntryExitModel.ToString(),
                    _tradeEntryConfidence.ToString("F2", CultureInfo.InvariantCulture),
                    _tradeExitQtyTp1.ToString(CultureInfo.InvariantCulture),
                    _tradeExitQtyTp2.ToString(CultureInfo.InvariantCulture),
                    _tradeExitQtyStop.ToString(CultureInfo.InvariantCulture),
                    _tradeExitQtyFlip.ToString(CultureInfo.InvariantCulture),
                    _tradeExitQtyOther.ToString(CultureInfo.InvariantCulture),
                    barsHeld.ToString(CultureInfo.InvariantCulture),
                    _tradeEntryTime.Hour.ToString(CultureInfo.InvariantCulture),
                    ((int)_tradeEntryTime.DayOfWeek).ToString(CultureInfo.InvariantCulture),
                    reason);

                if (!_tapeHeaderWritten)
                {
                    _tapeBuffer.AppendLine("trade_id,entry_time,exit_time,playbook,playtype,direction,entry_price,exit_price,pnl_currency,entry_qty,hold_minutes,stop_points,tp1_points,tp2_points,trail_points,breakeven_points,rr_tp2_stop,entry_atr,exit_model,entry_confidence,exit_qty_tp1,exit_qty_tp2,exit_qty_stop,exit_qty_flip,exit_qty_other,bars_held,entry_hour,entry_dow,entry_reason");
                    _tapeHeaderWritten = true;
                }
                _tapeBuffer.AppendLine(row);
                _tapeBufferedRows++;
                _tradeTapeFinalized = true;
                TapeDebug(string.Format("Buffered trade id={0}; rowsBuffered={1}", _tradeSequence, _tapeBufferedRows));

                if (_tapeBufferedRows >= Math.Max(1, TapeFlushEveryNRows))
                    FlushTapeBuffer();
            }
            catch (Exception ex)
            {
                // Never fail strategy execution due to exporter formatting/runtime issues.
                TapeDebug(string.Format("Finalize failed: {0}: {1}", ex.GetType().Name, ex.Message));
            }
        }

        private void FlushTapeBuffer()
        {
            if (_tapeBuffer == null || _tapeBufferedRows <= 0 || string.IsNullOrEmpty(_tapeFilePathResolved))
                return;

            try
            {
                string dir = Path.GetDirectoryName(_tapeFilePathResolved);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(_tapeFilePathResolved, _tapeBuffer.ToString());
                TapeDebug(string.Format("Flushed {0} rows to {1}", _tapeBufferedRows, _tapeFilePathResolved));
                _tapeBuffer.Clear();
                _tapeBufferedRows = 0;
            }
            catch (Exception ex)
            {
                TapeDebug(string.Format("Primary flush failed: {0}", ex.Message));
                try
                {
                    string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "glitch-export-fallback.csv");
                    File.AppendAllText(fallback, _tapeBuffer.ToString());
                    TapeDebug(string.Format("Fallback flush wrote {0} rows to {1}", _tapeBufferedRows, fallback));
                    _tapeBuffer.Clear();
                    _tapeBufferedRows = 0;
                }
                catch (Exception fallbackEx)
                {
                    TapeDebug(string.Format("Fallback flush failed: {0}", fallbackEx.Message));
                }
            }
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
                TapeDebug(string.Format("Flushed {0} gate-audit rows to {1}", _gateAuditBufferedRows, _gateAuditFilePathResolved));
                _gateAuditBuffer.Clear();
                _gateAuditBufferedRows = 0;
            }
            catch (Exception ex)
            {
                TapeDebug(string.Format("Gate-audit flush failed: {0}", ex.Message));
                try
                {
                    string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "glitch-gate-audit-fallback.csv");
                    File.AppendAllText(fallback, _gateAuditBuffer.ToString());
                    TapeDebug(string.Format("Gate-audit fallback wrote {0} rows to {1}", _gateAuditBufferedRows, fallback));
                    _gateAuditBuffer.Clear();
                    _gateAuditBufferedRows = 0;
                }
                catch (Exception fallbackEx)
                {
                    TapeDebug(string.Format("Gate-audit fallback failed: {0}", fallbackEx.Message));
                }
            }
        }

        #region Properties
        // 0. Orchestrator
        [NinjaScriptProperty]
        [Display(Name = "Enable Orchestrator", Order = 0, GroupName = "0. Orchestrator")]
        public bool EnableOrchestrator { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Minimum PlayScore To Trade", Order = 1, GroupName = "0. Orchestrator")]
        public int MinPlayScoreToTrade { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "PlayBook Lock Bars", Order = 2, GroupName = "0. Orchestrator")]
        public int PlaybookLockBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Cooldown Bars After Exit", Order = 3, GroupName = "0. Orchestrator")]
        public int CooldownBarsAfterExit { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Max Trades Per Day (Global, 0=off)", Order = 4, GroupName = "0. Orchestrator")]
        public int MaxTradesPerDay { get; set; }

        // 1. Shared Execution
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Shared Entry Quantity", Order = 0, GroupName = "1. Shared Execution")]
        public int SharedEntryQuantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Shared Enable Breakeven", Order = 1, GroupName = "1. Shared Execution")]
        public bool SharedEnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Shared Enable Trailing Stop", Order = 2, GroupName = "1. Shared Execution")]
        public bool SharedEnableTrailingStop { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Shared Enable Time Filter", Order = 3, GroupName = "1. Shared Execution")]
        public bool SharedTimeFilterEnabled { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Trading Start Hour (CT)", Order = 4, GroupName = "1. Shared Execution")]
        public int SharedTradingStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Trading End Hour (CT)", Order = 5, GroupName = "1. Shared Execution")]
        public int SharedTradingEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Avoid Window Start Hour (CT)", Order = 6, GroupName = "1. Shared Execution")]
        public int SharedAvoidWindowStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Avoid Window End Hour (CT)", Order = 7, GroupName = "1. Shared Execution")]
        public int SharedAvoidWindowEndHour { get; set; }

        // 2. Shared Risk
        [NinjaScriptProperty]
        [Display(Name = "Shared Enable Daily Limits", Order = 0, GroupName = "2. Shared Risk")]
        public bool SharedEnableDailyLimits { get; set; }

        [NinjaScriptProperty]
        [Range(50, 5000)]
        [Display(Name = "Shared Daily Profit Target ($)", Order = 1, GroupName = "2. Shared Risk")]
        public double SharedDailyProfitTarget { get; set; }

        [NinjaScriptProperty]
        [Range(50, 5000)]
        [Display(Name = "Shared Daily Loss Limit ($)", Order = 2, GroupName = "2. Shared Risk")]
        public double SharedDailyLossLimit { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Shared Default Stop (points)", Order = 3, GroupName = "2. Shared Risk")]
        public int SharedDefaultStopPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Shared Default TP1 (points)", Order = 4, GroupName = "2. Shared Risk")]
        public int SharedDefaultTp1Points { get; set; }

        [NinjaScriptProperty]
        [Range(1, 300)]
        [Display(Name = "Shared Default TP2 (points)", Order = 5, GroupName = "2. Shared Risk")]
        public int SharedDefaultTp2Points { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Shared Default Trail (points)", Order = 6, GroupName = "2. Shared Risk")]
        public int SharedDefaultTrailPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Shared Default Breakeven Trigger (points)", Order = 7, GroupName = "2. Shared Risk")]
        public int SharedDefaultBreakevenTriggerPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Shared Enable No-Progress Exit", Order = 8, GroupName = "2. Shared Risk")]
        public bool SharedEnableNoProgressExit { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Shared No-Progress Bars", Order = 9, GroupName = "2. Shared Risk")]
        public int SharedNoProgressBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "Shared Min Progress (TP1 fraction)", Order = 10, GroupName = "2. Shared Risk")]
        public double SharedNoProgressMinProgressTp1Fraction { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 1.5)]
        [Display(Name = "Shared Adverse Exit (SL fraction)", Order = 11, GroupName = "2. Shared Risk")]
        public double SharedNoProgressAdverseStopFraction { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Shared Enable Max Bars Exit", Order = 12, GroupName = "2. Shared Risk")]
        public bool SharedEnableMaxBarsExit { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Shared Max Bars In Trade", Order = 13, GroupName = "2. Shared Risk")]
        public int SharedMaxBarsInTrade { get; set; }

        // 3. PlayType: DirectionalFlip
        [NinjaScriptProperty]
        [Display(Name = "Enable PlayType DirectionalFlip", Order = 0, GroupName = "3. PlayType DirectionalFlip")]
        public bool EnablePlayTypeDirectionalFlip { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "DirectionalFlip Entry Quantity", Order = 90, GroupName = "3. PlayType DirectionalFlip")]
        public int DirectionalFlipEntryQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "DirectionalFlip Max Trades Per Day (0=off)", Order = 89, GroupName = "3. PlayType DirectionalFlip")]
        public int DirectionalFlipMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DirectionalFlip Enable Runner", Order = 91, GroupName = "3. PlayType DirectionalFlip")]
        public bool DirectionalFlipEnableRunner { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DirectionalFlip Enable Breakeven", Order = 92, GroupName = "3. PlayType DirectionalFlip")]
        public bool DirectionalFlipEnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DirectionalFlip Enable Trailing Stop", Order = 93, GroupName = "3. PlayType DirectionalFlip")]
        public bool DirectionalFlipEnableTrailingStop { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "DirectionalFlip Min PlayScore", Order = 94, GroupName = "3. PlayType DirectionalFlip")]
        public int DirectionalFlipMinPlayScore { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DirectionalFlip Enable ATR Regime Filter", Order = 95, GroupName = "3. PlayType DirectionalFlip")]
        public bool DirectionalFlipEnableAtrRegimeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "DirectionalFlip ATR Regime Min", Order = 96, GroupName = "3. PlayType DirectionalFlip")]
        public double DirectionalFlipAtrRegimeMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "DirectionalFlip ATR Regime Max", Order = 97, GroupName = "3. PlayType DirectionalFlip")]
        public double DirectionalFlipAtrRegimeMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DirectionalFlip Enable R2 Regime Filter", Order = 98, GroupName = "3. PlayType DirectionalFlip")]
        public bool DirectionalFlipEnableR2RegimeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "DirectionalFlip R2 Regime Min", Order = 99, GroupName = "3. PlayType DirectionalFlip")]
        public double DirectionalFlipR2RegimeMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "DirectionalFlip R2 Regime Max", Order = 100, GroupName = "3. PlayType DirectionalFlip")]
        public double DirectionalFlipR2RegimeMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DirectionalFlip Enable Min RR Guard", Order = 103, GroupName = "3. PlayType DirectionalFlip")]
        public bool DirectionalFlipEnableMinRrGuard { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5)]
        [Display(Name = "DirectionalFlip Min RR (TP2/SL)", Order = 104, GroupName = "3. PlayType DirectionalFlip")]
        public double DirectionalFlipMinRr { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DirectionalFlip Exit Model", Order = 94, GroupName = "3. PlayType DirectionalFlip")]
        public ExitModel DirectionalFlipExitModel { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "DirectionalFlip ATR Period", Order = 95, GroupName = "3. PlayType DirectionalFlip")]
        public int DirectionalFlipAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "DirectionalFlip ATR Stop Mult", Order = 96, GroupName = "3. PlayType DirectionalFlip")]
        public double DirectionalFlipAtrStopMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "DirectionalFlip ATR TP1 Mult", Order = 97, GroupName = "3. PlayType DirectionalFlip")]
        public double DirectionalFlipAtrTp1Mult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 20)]
        [Display(Name = "DirectionalFlip ATR TP2 Mult", Order = 98, GroupName = "3. PlayType DirectionalFlip")]
        public double DirectionalFlipAtrTp2Mult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "DirectionalFlip ATR Trail Mult", Order = 99, GroupName = "3. PlayType DirectionalFlip")]
        public double DirectionalFlipAtrTrailMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "DirectionalFlip ATR Breakeven Mult", Order = 100, GroupName = "3. PlayType DirectionalFlip")]
        public double DirectionalFlipAtrBreakevenMult { get; set; }

        [NinjaScriptProperty]
        [Range(1, 400)]
        [Display(Name = "DirectionalFlip ATR Min Points Clamp", Order = 101, GroupName = "3. PlayType DirectionalFlip")]
        public int DirectionalFlipAtrMinPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 600)]
        [Display(Name = "DirectionalFlip ATR Max Points Clamp", Order = 102, GroupName = "3. PlayType DirectionalFlip")]
        public int DirectionalFlipAtrMaxPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "DirectionalFlip Length", Order = 1, GroupName = "3. PlayType DirectionalFlip")]
        public int DirectionalFlipLength { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "DirectionalFlip SmoothingFactor", Order = 2, GroupName = "3. PlayType DirectionalFlip")]
        public int DirectionalFlipSmoothingFactor { get; set; }

        [NinjaScriptProperty]
        [Range(1, 120)]
        [Display(Name = "DirectionalFlip Higher TF Minutes (legacy/no-op)", Order = 3, GroupName = "3. PlayType DirectionalFlip")]
        public int DirectionalFlipHigherTfMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "DirectionalFlip Confirm Bars", Order = 4, GroupName = "3. PlayType DirectionalFlip")]
        public int DirectionalFlipFlipConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "DirectionalFlip Stop (points)", Order = 5, GroupName = "3. PlayType DirectionalFlip")]
        public int DirectionalFlipStopPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "DirectionalFlip TP1 (points)", Order = 6, GroupName = "3. PlayType DirectionalFlip")]
        public int DirectionalFlipTp1Points { get; set; }

        [NinjaScriptProperty]
        [Range(1, 300)]
        [Display(Name = "DirectionalFlip TP2 (points)", Order = 7, GroupName = "3. PlayType DirectionalFlip")]
        public int DirectionalFlipTp2Points { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "DirectionalFlip Trail (points)", Order = 8, GroupName = "3. PlayType DirectionalFlip")]
        public int DirectionalFlipTrailPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "DirectionalFlip Breakeven Trigger (points)", Order = 9, GroupName = "3. PlayType DirectionalFlip")]
        public int DirectionalFlipBreakevenTriggerPoints { get; set; }

        // 4. PlayType: CompressionBreakout
        [NinjaScriptProperty]
        [Display(Name = "Enable PlayType CompressionBreakout", Order = 0, GroupName = "4. PlayType CompressionBreakout")]
        public bool EnablePlayTypeCompressionBreakout { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "CompressionBreakout Entry Quantity", Order = 90, GroupName = "4. PlayType CompressionBreakout")]
        public int CompressionBreakoutEntryQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "CompressionBreakout Max Trades Per Day (0=off)", Order = 89, GroupName = "4. PlayType CompressionBreakout")]
        public int CompressionBreakoutMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "CompressionBreakout Enable Runner", Order = 91, GroupName = "4. PlayType CompressionBreakout")]
        public bool CompressionBreakoutEnableRunner { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "CompressionBreakout Enable Breakeven", Order = 92, GroupName = "4. PlayType CompressionBreakout")]
        public bool CompressionBreakoutEnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "CompressionBreakout Enable Trailing Stop", Order = 93, GroupName = "4. PlayType CompressionBreakout")]
        public bool CompressionBreakoutEnableTrailingStop { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "CompressionBreakout Min PlayScore", Order = 94, GroupName = "4. PlayType CompressionBreakout")]
        public int CompressionBreakoutMinPlayScore { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "CompressionBreakout Enable ATR Regime Filter", Order = 95, GroupName = "4. PlayType CompressionBreakout")]
        public bool CompressionBreakoutEnableAtrRegimeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "CompressionBreakout ATR Regime Min", Order = 96, GroupName = "4. PlayType CompressionBreakout")]
        public double CompressionBreakoutAtrRegimeMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "CompressionBreakout ATR Regime Max", Order = 97, GroupName = "4. PlayType CompressionBreakout")]
        public double CompressionBreakoutAtrRegimeMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "CompressionBreakout Enable R2 Regime Filter", Order = 98, GroupName = "4. PlayType CompressionBreakout")]
        public bool CompressionBreakoutEnableR2RegimeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "CompressionBreakout R2 Regime Min", Order = 99, GroupName = "4. PlayType CompressionBreakout")]
        public double CompressionBreakoutR2RegimeMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "CompressionBreakout R2 Regime Max", Order = 100, GroupName = "4. PlayType CompressionBreakout")]
        public double CompressionBreakoutR2RegimeMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "CompressionBreakout Enable Min RR Guard", Order = 103, GroupName = "4. PlayType CompressionBreakout")]
        public bool CompressionBreakoutEnableMinRrGuard { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5)]
        [Display(Name = "CompressionBreakout Min RR (TP2/SL)", Order = 104, GroupName = "4. PlayType CompressionBreakout")]
        public double CompressionBreakoutMinRr { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "CompressionBreakout Exit Model", Order = 94, GroupName = "4. PlayType CompressionBreakout")]
        public ExitModel CompressionBreakoutExitModel { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "CompressionBreakout ATR Period", Order = 95, GroupName = "4. PlayType CompressionBreakout")]
        public int CompressionBreakoutAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "CompressionBreakout ATR Stop Mult", Order = 96, GroupName = "4. PlayType CompressionBreakout")]
        public double CompressionBreakoutAtrStopMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "CompressionBreakout ATR TP1 Mult", Order = 97, GroupName = "4. PlayType CompressionBreakout")]
        public double CompressionBreakoutAtrTp1Mult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 20)]
        [Display(Name = "CompressionBreakout ATR TP2 Mult", Order = 98, GroupName = "4. PlayType CompressionBreakout")]
        public double CompressionBreakoutAtrTp2Mult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "CompressionBreakout ATR Trail Mult", Order = 99, GroupName = "4. PlayType CompressionBreakout")]
        public double CompressionBreakoutAtrTrailMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "CompressionBreakout ATR Breakeven Mult", Order = 100, GroupName = "4. PlayType CompressionBreakout")]
        public double CompressionBreakoutAtrBreakevenMult { get; set; }

        [NinjaScriptProperty]
        [Range(1, 400)]
        [Display(Name = "CompressionBreakout ATR Min Points Clamp", Order = 101, GroupName = "4. PlayType CompressionBreakout")]
        public int CompressionBreakoutAtrMinPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 600)]
        [Display(Name = "CompressionBreakout ATR Max Points Clamp", Order = 102, GroupName = "4. PlayType CompressionBreakout")]
        public int CompressionBreakoutAtrMaxPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "CompressionBreakout ADX Min", Order = 1, GroupName = "4. PlayType CompressionBreakout")]
        public int CompressionBreakoutAdxMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "CompressionBreakout Use ADX Filter", Order = 2, GroupName = "4. PlayType CompressionBreakout")]
        public bool CompressionBreakoutUseAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "CompressionBreakout ADX Period", Order = 3, GroupName = "4. PlayType CompressionBreakout")]
        public int CompressionBreakoutAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "CompressionBreakout RSI Period", Order = 4, GroupName = "4. PlayType CompressionBreakout")]
        public int CompressionBreakoutRsiPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 99)]
        [Display(Name = "CompressionBreakout RSI Long Max", Order = 5, GroupName = "4. PlayType CompressionBreakout")]
        public int CompressionBreakoutRsiLongMax { get; set; }

        [NinjaScriptProperty]
        [Range(1, 99)]
        [Display(Name = "CompressionBreakout RSI Short Min", Order = 6, GroupName = "4. PlayType CompressionBreakout")]
        public int CompressionBreakoutRsiShortMin { get; set; }

        [NinjaScriptProperty]
        [Range(5, 500)]
        [Display(Name = "CompressionBreakout Volume Lookback", Order = 7, GroupName = "4. PlayType CompressionBreakout")]
        public int CompressionBreakoutVolumeLookback { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "CompressionBreakout Stop (points)", Order = 8, GroupName = "4. PlayType CompressionBreakout")]
        public int CompressionBreakoutStopPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "CompressionBreakout TP1 (points)", Order = 9, GroupName = "4. PlayType CompressionBreakout")]
        public int CompressionBreakoutTp1Points { get; set; }

        [NinjaScriptProperty]
        [Range(1, 300)]
        [Display(Name = "CompressionBreakout TP2 (points)", Order = 10, GroupName = "4. PlayType CompressionBreakout")]
        public int CompressionBreakoutTp2Points { get; set; }

        // 5. PlayType: SweepBos
        [NinjaScriptProperty]
        [Display(Name = "Enable PlayType SweepBos", Order = 0, GroupName = "5. PlayType SweepBos")]
        public bool EnablePlayTypeSweepBos { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "SweepBos Entry Quantity", Order = 90, GroupName = "5. PlayType SweepBos")]
        public int SweepBosEntryQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "SweepBos Max Trades Per Day (0=off)", Order = 89, GroupName = "5. PlayType SweepBos")]
        public int SweepBosMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SweepBos Enable Runner", Order = 91, GroupName = "5. PlayType SweepBos")]
        public bool SweepBosEnableRunner { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SweepBos Enable Breakeven", Order = 92, GroupName = "5. PlayType SweepBos")]
        public bool SweepBosEnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SweepBos Enable Trailing Stop", Order = 93, GroupName = "5. PlayType SweepBos")]
        public bool SweepBosEnableTrailingStop { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "SweepBos Min PlayScore", Order = 94, GroupName = "5. PlayType SweepBos")]
        public int SweepBosMinPlayScore { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SweepBos Enable ATR Regime Filter", Order = 95, GroupName = "5. PlayType SweepBos")]
        public bool SweepBosEnableAtrRegimeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "SweepBos ATR Regime Min", Order = 96, GroupName = "5. PlayType SweepBos")]
        public double SweepBosAtrRegimeMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "SweepBos ATR Regime Max", Order = 97, GroupName = "5. PlayType SweepBos")]
        public double SweepBosAtrRegimeMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SweepBos Enable R2 Regime Filter", Order = 98, GroupName = "5. PlayType SweepBos")]
        public bool SweepBosEnableR2RegimeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "SweepBos R2 Regime Min", Order = 99, GroupName = "5. PlayType SweepBos")]
        public double SweepBosR2RegimeMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "SweepBos R2 Regime Max", Order = 100, GroupName = "5. PlayType SweepBos")]
        public double SweepBosR2RegimeMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SweepBos Enable Min RR Guard", Order = 103, GroupName = "5. PlayType SweepBos")]
        public bool SweepBosEnableMinRrGuard { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5)]
        [Display(Name = "SweepBos Min RR (TP2/SL)", Order = 104, GroupName = "5. PlayType SweepBos")]
        public double SweepBosMinRr { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SweepBos Exit Model", Order = 94, GroupName = "5. PlayType SweepBos")]
        public ExitModel SweepBosExitModel { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "SweepBos ATR Period", Order = 95, GroupName = "5. PlayType SweepBos")]
        public int SweepBosAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "SweepBos ATR Stop Mult", Order = 96, GroupName = "5. PlayType SweepBos")]
        public double SweepBosAtrStopMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "SweepBos ATR TP1 Mult", Order = 97, GroupName = "5. PlayType SweepBos")]
        public double SweepBosAtrTp1Mult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 20)]
        [Display(Name = "SweepBos ATR TP2 Mult", Order = 98, GroupName = "5. PlayType SweepBos")]
        public double SweepBosAtrTp2Mult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "SweepBos ATR Trail Mult", Order = 99, GroupName = "5. PlayType SweepBos")]
        public double SweepBosAtrTrailMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "SweepBos ATR Breakeven Mult", Order = 100, GroupName = "5. PlayType SweepBos")]
        public double SweepBosAtrBreakevenMult { get; set; }

        [NinjaScriptProperty]
        [Range(1, 400)]
        [Display(Name = "SweepBos ATR Min Points Clamp", Order = 101, GroupName = "5. PlayType SweepBos")]
        public int SweepBosAtrMinPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 600)]
        [Display(Name = "SweepBos ATR Max Points Clamp", Order = 102, GroupName = "5. PlayType SweepBos")]
        public int SweepBosAtrMaxPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "SweepBos R2 Min", Order = 1, GroupName = "5. PlayType SweepBos")]
        public double SweepBosR2Min { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SweepBos Use Trend Filter", Order = 2, GroupName = "5. PlayType SweepBos")]
        public bool SweepBosUseTrendFilter { get; set; }

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "SweepBos Swing Lookback", Order = 3, GroupName = "5. PlayType SweepBos")]
        public int SweepBosSwingLookback { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "SweepBos Max Sequence Bars", Order = 4, GroupName = "5. PlayType SweepBos")]
        public int SweepBosMaxSequenceBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SweepBos Restart Timeout After Phase2", Order = 5, GroupName = "5. PlayType SweepBos")]
        public bool SweepBosRestartTimeoutAfterPhase2 { get; set; }

        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name = "SweepBos BOS Lookback", Order = 6, GroupName = "5. PlayType SweepBos")]
        public int SweepBosBosLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SweepBos Use Wick BOS", Order = 7, GroupName = "5. PlayType SweepBos")]
        public bool SweepBosUseWickBos { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SweepBos Require BOS", Order = 8, GroupName = "5. PlayType SweepBos")]
        public bool SweepBosRequireBos { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SweepBos Require Close Beyond T3", Order = 9, GroupName = "5. PlayType SweepBos")]
        public bool SweepBosRequireCloseBeyondT3 { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "SweepBos Stop (points)", Order = 10, GroupName = "5. PlayType SweepBos")]
        public int SweepBosStopPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "SweepBos TP1 (points)", Order = 11, GroupName = "5. PlayType SweepBos")]
        public int SweepBosTp1Points { get; set; }

        [NinjaScriptProperty]
        [Range(1, 300)]
        [Display(Name = "SweepBos TP2 (points)", Order = 12, GroupName = "5. PlayType SweepBos")]
        public int SweepBosTp2Points { get; set; }

        // 6. PlayType: SessionStructure
        [NinjaScriptProperty]
        [Display(Name = "Enable PlayType SessionStructure", Order = 0, GroupName = "6. PlayType SessionStructure")]
        public bool EnablePlayTypeSessionStructure { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "SessionStructure Entry Quantity", Order = 90, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructureEntryQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "SessionStructure Max Trades Per Day (0=off)", Order = 89, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructureMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SessionStructure Enable Runner", Order = 91, GroupName = "6. PlayType SessionStructure")]
        public bool SessionStructureEnableRunner { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SessionStructure Enable Breakeven", Order = 92, GroupName = "6. PlayType SessionStructure")]
        public bool SessionStructureEnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SessionStructure Enable Trailing Stop", Order = 93, GroupName = "6. PlayType SessionStructure")]
        public bool SessionStructureEnableTrailingStop { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "SessionStructure Min PlayScore", Order = 94, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructureMinPlayScore { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SessionStructure Enable ATR Regime Filter", Order = 95, GroupName = "6. PlayType SessionStructure")]
        public bool SessionStructureEnableAtrRegimeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "SessionStructure ATR Regime Min", Order = 96, GroupName = "6. PlayType SessionStructure")]
        public double SessionStructureAtrRegimeMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "SessionStructure ATR Regime Max", Order = 97, GroupName = "6. PlayType SessionStructure")]
        public double SessionStructureAtrRegimeMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SessionStructure Enable R2 Regime Filter", Order = 98, GroupName = "6. PlayType SessionStructure")]
        public bool SessionStructureEnableR2RegimeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "SessionStructure R2 Regime Min", Order = 99, GroupName = "6. PlayType SessionStructure")]
        public double SessionStructureR2RegimeMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "SessionStructure R2 Regime Max", Order = 100, GroupName = "6. PlayType SessionStructure")]
        public double SessionStructureR2RegimeMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SessionStructure Enable Min RR Guard", Order = 103, GroupName = "6. PlayType SessionStructure")]
        public bool SessionStructureEnableMinRrGuard { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5)]
        [Display(Name = "SessionStructure Min RR (TP2/SL)", Order = 104, GroupName = "6. PlayType SessionStructure")]
        public double SessionStructureMinRr { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SessionStructure Exit Model", Order = 94, GroupName = "6. PlayType SessionStructure")]
        public ExitModel SessionStructureExitModel { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "SessionStructure ATR Period", Order = 95, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructureAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "SessionStructure ATR Stop Mult", Order = 96, GroupName = "6. PlayType SessionStructure")]
        public double SessionStructureAtrStopMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "SessionStructure ATR TP1 Mult", Order = 97, GroupName = "6. PlayType SessionStructure")]
        public double SessionStructureAtrTp1Mult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 20)]
        [Display(Name = "SessionStructure ATR TP2 Mult", Order = 98, GroupName = "6. PlayType SessionStructure")]
        public double SessionStructureAtrTp2Mult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "SessionStructure ATR Trail Mult", Order = 99, GroupName = "6. PlayType SessionStructure")]
        public double SessionStructureAtrTrailMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "SessionStructure ATR Breakeven Mult", Order = 100, GroupName = "6. PlayType SessionStructure")]
        public double SessionStructureAtrBreakevenMult { get; set; }

        [NinjaScriptProperty]
        [Range(1, 400)]
        [Display(Name = "SessionStructure ATR Min Points Clamp", Order = 101, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructureAtrMinPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 600)]
        [Display(Name = "SessionStructure ATR Max Points Clamp", Order = 102, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructureAtrMaxPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "SessionStructure Start Hour (CT)", Order = 1, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructureStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "SessionStructure Start Minute (CT)", Order = 2, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructureStartMinute { get; set; }

        [NinjaScriptProperty]
        [Range(1, 120)]
        [Display(Name = "SessionStructure Range Duration (minutes)", Order = 3, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructureRangeDurationMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "SessionStructure Trading Cutoff Hour (CT)", Order = 4, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructureTradingCutoffHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "SessionStructure Trading Cutoff Minute (CT)", Order = 5, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructureTradingCutoffMinute { get; set; }

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "SessionStructure Breakout Buffer (ticks)", Order = 6, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructureBreakoutBufferTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SessionStructure Use Premarket Bias", Order = 7, GroupName = "6. PlayType SessionStructure")]
        public bool SessionStructureUsePremarketBias { get; set; }

        [NinjaScriptProperty]
        [Range(5, 2000)]
        [Display(Name = "SessionStructure Premarket Bias Lookback Bars", Order = 8, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructurePremarketBiasLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SessionStructure Single Direction Per Day", Order = 9, GroupName = "6. PlayType SessionStructure")]
        public bool SessionStructureSingleDirectionPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "SessionStructure Stop (points)", Order = 10, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructureStopPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "SessionStructure TP1 (points)", Order = 11, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructureTp1Points { get; set; }

        [NinjaScriptProperty]
        [Range(1, 300)]
        [Display(Name = "SessionStructure TP2 (points)", Order = 12, GroupName = "6. PlayType SessionStructure")]
        public int SessionStructureTp2Points { get; set; }

        // 7. PlayType: MeanReversion
        [NinjaScriptProperty]
        [Display(Name = "Enable PlayType MeanReversion", Order = 0, GroupName = "7. PlayType MeanReversion")]
        public bool EnablePlayTypeMeanReversion { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MeanReversion Entry Quantity", Order = 90, GroupName = "7. PlayType MeanReversion")]
        public int MeanReversionEntryQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "MeanReversion Max Trades Per Day (0=off)", Order = 89, GroupName = "7. PlayType MeanReversion")]
        public int MeanReversionMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MeanReversion Enable Runner", Order = 91, GroupName = "7. PlayType MeanReversion")]
        public bool MeanReversionEnableRunner { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MeanReversion Enable Breakeven", Order = 92, GroupName = "7. PlayType MeanReversion")]
        public bool MeanReversionEnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MeanReversion Enable Trailing Stop", Order = 93, GroupName = "7. PlayType MeanReversion")]
        public bool MeanReversionEnableTrailingStop { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "MeanReversion Min PlayScore", Order = 94, GroupName = "7. PlayType MeanReversion")]
        public int MeanReversionMinPlayScore { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MeanReversion Enable ATR Regime Filter", Order = 95, GroupName = "7. PlayType MeanReversion")]
        public bool MeanReversionEnableAtrRegimeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "MeanReversion ATR Regime Min", Order = 96, GroupName = "7. PlayType MeanReversion")]
        public double MeanReversionAtrRegimeMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "MeanReversion ATR Regime Max", Order = 97, GroupName = "7. PlayType MeanReversion")]
        public double MeanReversionAtrRegimeMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MeanReversion Enable R2 Regime Filter", Order = 98, GroupName = "7. PlayType MeanReversion")]
        public bool MeanReversionEnableR2RegimeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "MeanReversion R2 Regime Min", Order = 99, GroupName = "7. PlayType MeanReversion")]
        public double MeanReversionR2RegimeMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "MeanReversion R2 Regime Max", Order = 100, GroupName = "7. PlayType MeanReversion")]
        public double MeanReversionR2RegimeMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MeanReversion Enable Min RR Guard", Order = 103, GroupName = "7. PlayType MeanReversion")]
        public bool MeanReversionEnableMinRrGuard { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5)]
        [Display(Name = "MeanReversion Min RR (TP2/SL)", Order = 104, GroupName = "7. PlayType MeanReversion")]
        public double MeanReversionMinRr { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MeanReversion Exit Model", Order = 94, GroupName = "7. PlayType MeanReversion")]
        public ExitModel MeanReversionExitModel { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "MeanReversion ATR Period", Order = 95, GroupName = "7. PlayType MeanReversion")]
        public int MeanReversionAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "MeanReversion ATR Stop Mult", Order = 96, GroupName = "7. PlayType MeanReversion")]
        public double MeanReversionAtrStopMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "MeanReversion ATR TP1 Mult", Order = 97, GroupName = "7. PlayType MeanReversion")]
        public double MeanReversionAtrTp1Mult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 20)]
        [Display(Name = "MeanReversion ATR TP2 Mult", Order = 98, GroupName = "7. PlayType MeanReversion")]
        public double MeanReversionAtrTp2Mult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "MeanReversion ATR Trail Mult", Order = 99, GroupName = "7. PlayType MeanReversion")]
        public double MeanReversionAtrTrailMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10)]
        [Display(Name = "MeanReversion ATR Breakeven Mult", Order = 100, GroupName = "7. PlayType MeanReversion")]
        public double MeanReversionAtrBreakevenMult { get; set; }

        [NinjaScriptProperty]
        [Range(1, 400)]
        [Display(Name = "MeanReversion ATR Min Points Clamp", Order = 101, GroupName = "7. PlayType MeanReversion")]
        public int MeanReversionAtrMinPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 600)]
        [Display(Name = "MeanReversion ATR Max Points Clamp", Order = 102, GroupName = "7. PlayType MeanReversion")]
        public int MeanReversionAtrMaxPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MeanReversion Use T3 Basis", Order = 1, GroupName = "7. PlayType MeanReversion")]
        public bool MeanReversionUseT3Basis { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "MeanReversion T3 Length", Order = 2, GroupName = "7. PlayType MeanReversion")]
        public int MeanReversionT3Length { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "MeanReversion T3 Volume Factor", Order = 3, GroupName = "7. PlayType MeanReversion")]
        public double MeanReversionT3VolumeFactor { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "MeanReversion BB Length", Order = 4, GroupName = "7. PlayType MeanReversion")]
        public int MeanReversionBbLength { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10)]
        [Display(Name = "MeanReversion BB Multiplier", Order = 5, GroupName = "7. PlayType MeanReversion")]
        public double MeanReversionBbMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "MeanReversion RSI Length", Order = 6, GroupName = "7. PlayType MeanReversion")]
        public int MeanReversionRsiLength { get; set; }

        [NinjaScriptProperty]
        [Range(1, 99)]
        [Display(Name = "MeanReversion RSI Upper", Order = 7, GroupName = "7. PlayType MeanReversion")]
        public int MeanReversionRsiUpper { get; set; }

        [NinjaScriptProperty]
        [Range(1, 99)]
        [Display(Name = "MeanReversion RSI Lower", Order = 8, GroupName = "7. PlayType MeanReversion")]
        public int MeanReversionRsiLower { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "MeanReversion Stop (points)", Order = 9, GroupName = "7. PlayType MeanReversion")]
        public int MeanReversionStopPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "MeanReversion TP1 (points)", Order = 10, GroupName = "7. PlayType MeanReversion")]
        public int MeanReversionTp1Points { get; set; }

        [NinjaScriptProperty]
        [Range(1, 300)]
        [Display(Name = "MeanReversion TP2 (points)", Order = 11, GroupName = "7. PlayType MeanReversion")]
        public int MeanReversionTp2Points { get; set; }

        // 8. Diagnostics
        [NinjaScriptProperty]
        [Display(Name = "Enable Diagnostics Logs", Order = 0, GroupName = "8. Diagnostics")]
        public bool EnableDiagnosticsLogs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Strict Mode Require Indicators Ready", Order = 1, GroupName = "8. Diagnostics")]
        public bool StrictModeRequireIndicatorsReady { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Diagnostics Log Every N Bars (0=off)", Order = 2, GroupName = "8. Diagnostics")]
        public int DiagnosticsLogEveryNBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Compact Status Panel", Order = 3, GroupName = "8. Diagnostics")]
        public bool EnableCompactStatusPanel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Tape Export", Order = 4, GroupName = "8. Diagnostics")]
        public bool EnableTapeExport { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Tape Export Relative Path", Order = 5, GroupName = "8. Diagnostics")]
        public string TapeExportRelativePath { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Tape Flush Every N Rows", Order = 6, GroupName = "8. Diagnostics")]
        public int TapeFlushEveryNRows { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Tape Debug Logs", Order = 7, GroupName = "8. Diagnostics")]
        public bool EnableTapeDebugLogs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Authoritative Trade Export", Order = 8, GroupName = "8. Diagnostics")]
        public bool UseAuthoritativeTradeExport { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Gate Audit Export", Order = 9, GroupName = "8. Diagnostics")]
        public bool EnableGateAuditExport { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Gate Audit Export Relative Path", Order = 10, GroupName = "8. Diagnostics")]
        public string GateAuditExportRelativePath { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Gate Audit Flush Every N Rows", Order = 11, GroupName = "8. Diagnostics")]
        public int GateAuditFlushEveryNRows { get; set; }

        // 9.1 Indicator Inputs (Trend Quality)
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "TrendQuality T3 Length", Order = 0, GroupName = "9.1 Indicator Inputs - Trend Quality")]
        public int TrendQualityT3Length { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "TrendQuality T3 Volume Factor", Order = 1, GroupName = "9.1 Indicator Inputs - Trend Quality")]
        public double TrendQualityT3VolumeFactor { get; set; }

        [NinjaScriptProperty]
        [Range(20, 500)]
        [Display(Name = "TrendQuality R2 Lookback", Order = 2, GroupName = "9.1 Indicator Inputs - Trend Quality")]
        public int TrendQualityR2Lookback { get; set; }
        #endregion
    }
}
