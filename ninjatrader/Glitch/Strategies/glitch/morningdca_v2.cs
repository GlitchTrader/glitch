// MorningDCA_v2.cs — Morning Stochastic Pullback DCA Strategy (5-Min Optimized)
//
// KEY CHANGES FROM v1:
//   1. Designed for 5-MINUTE bars (not 1-min)
//   2. DCA stop is from AVERAGE entry (not original entry) — critical fix
//   3. DCA trigger increased to 20pts (was 15)
//   4. Time stop = 24 bars (2 hours on 5-min)
//   5. Cooldown = 6 bars (30 min on 5-min)
//   6. Added DcaStopPoints parameter (stop from avg entry after DCA)
//
// Python backtest results (6 years, 5-min MNQ, K<40):
//   TP=12, DCA@20, DCA_SL=20: $24.54/day, 86.7% WR, 78% daily WR, max DD $-489
//   TP=12, DCA@20, DCA_SL=25: $24.05/day, 88.2% WR, 81% daily WR, max DD $-649
//   TP=16, DCA@20, DCA_SL=20: $21.19/day, 78.1% WR, 66% daily WR, max DD $-649
//
// SETUP INSTRUCTIONS:
//   1. Place in: Documents\NinjaTrader 8\bin\Custom\Strategies\
//   2. In Strategy Analyzer:
//      - Instrument: MNQ (Micro E-mini Nasdaq)
//      - Data series: 5 Minute bars  <-- CRITICAL: must be 5-min
//      - Start: 01/01/2020, End: today
//      - Order fill: Standard (Fastest)
//      - Commission: NinjaTrader Brokerage Free (or your broker)
//      - Entries per direction: 2
//      - Entry handling: Unique entries
//      - Exit on session close: checked
//      - Set order quantity: Strategy
//
// Place in: Documents\NinjaTrader 8\bin\Custom\Strategies\

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class MorningDCA_v2 : Strategy
    {
        #region Fields

        // Indicators
        private EMA _emaFast;
        private EMA _emaMed;
        private EMA _emaSlow;
        private Stochastics _stoch;

        // Trade state
        private bool   _inTrade;
        private int    _direction;       // 1 = long, -1 = short
        private double _origEntry;       // original entry price (first fill)
        private double _avgEntry;        // average entry after DCA
        private int    _totalQty;        // total contracts in position
        private bool   _dcaDone;         // has DCA been added?
        private bool   _dcaAllowed;      // allow DCA on this trade?
        private int    _entryBar;        // bar index of entry
        private int    _lastExitBar;     // bar index of last exit (for cooldown)
        private bool   _initFilled;      // initial entry has filled
        private bool   _dcaFilled;       // DCA entry has filled
        private double _initFillPrice;   // actual fill price of initial entry
        private double _dcaFillPrice;    // actual fill price of DCA

        // Entry signal names (fixed, to match exits)
        private const string SIG_INIT = "InitEntry";
        private const string SIG_DCA  = "DcaEntry";

        // Daily management
        private double   _dailyPnL;
        private int      _dailyTradeCount;
        private int      _dailyConsecLosses;
        private DateTime _lastTradingDay;
        private int      _priorTradeCount; // SystemPerformance trade count at day start

        #endregion

        #region Lifecycle

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "MorningDCA_v2";
                Description                  = "5-min Morning EMA+Stoch pullback with DCA. DCA stop from avg entry.";
                Calculate                    = Calculate.OnBarClose;
                EntriesPerDirection          = 2;         // need 2 for DCA
                EntryHandling                = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                StartBehavior                = StartBehavior.WaitUntilFlat;
                IsOverlay                    = true;
                BarsRequiredToTrade          = 60;

                // ── 1. EMA alignment ──
                EmaFastPeriod   = 9;
                EmaMedPeriod    = 20;
                EmaSlowPeriod   = 50;

                // ── 2. Stochastic filter ──
                StochPeriodK    = 14;
                StochPeriodD    = 3;
                StochSmooth     = 3;
                StochLongThresh = 40;
                StochShortThresh = 60;

                // ── 3. DCA parameters ──
                InitialQty      = 1;
                DcaQty          = 1;
                DcaTrigger      = 20.0;   // v2: 20pts (was 15 in v1)
                ReduceDcaAfterLoss = false; // v2: disabled by default

                // ── 4. Exits ──
                TpPoints        = 12.0;   // take profit from AVG entry (points)
                HardStopPoints  = 30.0;   // hard stop from ORIG entry BEFORE DCA (points)
                DcaStopPoints   = 20.0;   // NEW: stop from AVG entry AFTER DCA (points)
                TimeStopBars    = 24;     // v2: 24 bars = 2 hours on 5-min

                // ── 5. Time filter ──
                TradingStartHour = 10;
                TradingEndHour   = 13;

                // ── 6. Daily management ──
                DailyProfitTarget = 80.0;
                DailyLossLimit    = -80.0;
                MaxTradesPerDay   = 2;
                MaxConsecLosses   = 3;

                // ── 7. Cooldown ──
                CooldownBars    = 6;        // v2: 6 bars = 30 min on 5-min
            }
            else if (State == State.Configure)
            {
                // No additional data series — use 5-min primary
            }
            else if (State == State.DataLoaded)
            {
                _emaFast = EMA(EmaFastPeriod);
                _emaMed  = EMA(EmaMedPeriod);
                _emaSlow = EMA(EmaSlowPeriod);
                _stoch   = Stochastics(StochPeriodK, StochPeriodD, StochSmooth);

                _lastExitBar = -999;
                _priorTradeCount = 0;
            }
        }

        #endregion

        #region OnBarUpdate

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade)
                return;

            // ── Sync _inTrade with actual position ──
            if (_inTrade && Position.MarketPosition == MarketPosition.Flat)
            {
                _inTrade = false;
                _lastExitBar = CurrentBar;
            }

            // ── Daily reset ──
            DateTime today = Time[0].Date;
            if (today != _lastTradingDay)
            {
                _dailyPnL = 0;
                _dailyTradeCount = 0;
                _dailyConsecLosses = 0;
                _lastTradingDay = today;
            }

            // ── If in a trade, manage it ──
            if (_inTrade)
            {
                ManageTrade();
                return;
            }

            // ── Check for new entry ──
            TryEntry();
        }

        #endregion

        #region Entry Logic

        private void TryEntry()
        {
            // Daily limits
            if (_dailyTradeCount >= MaxTradesPerDay) return;
            if (_dailyPnL >= DailyProfitTarget) return;
            if (_dailyPnL <= DailyLossLimit) return;
            if (_dailyConsecLosses >= MaxConsecLosses) return;

            // Cooldown
            if (CurrentBar - _lastExitBar < CooldownBars) return;

            // Time filter
            int hour = Time[0].Hour;
            if (hour < TradingStartHour || hour >= TradingEndHour) return;

            // EMA alignment
            int emaAlign = ComputeEmaAlignment();
            if (emaAlign == 0) return;

            // Stochastic pullback
            double stochK = _stoch.K[0];

            int direction = 0;
            if (emaAlign >= 1 && stochK < StochLongThresh)
                direction = 1;
            else if (emaAlign <= -1 && stochK > StochShortThresh)
                direction = -1;
            else
                return;

            // Enter the trade
            EnterTrade(direction);
        }

        private int ComputeEmaAlignment()
        {
            double ef = _emaFast[0];
            double em = _emaMed[0];
            double es = _emaSlow[0];

            if (ef > em && em > es) return 2;   // strong bullish
            if (ef < em && em < es) return -2;   // strong bearish
            if (ef > es) return 1;                // mild bullish
            if (ef < es) return -1;               // mild bearish
            return 0;
        }

        private void EnterTrade(int direction)
        {
            _direction = direction;
            _totalQty = 0;
            _dcaDone = false;
            _dcaAllowed = ReduceDcaAfterLoss ? (_dailyConsecLosses == 0) : true;
            _entryBar = CurrentBar;
            _initFilled = false;
            _dcaFilled = false;
            _initFillPrice = 0;
            _dcaFillPrice = 0;

            if (direction == 1)
                EnterLong(InitialQty, SIG_INIT);
            else
                EnterShort(InitialQty, SIG_INIT);
        }

        #endregion

        #region Trade Management

        private void ManageTrade()
        {
            int barsHeld = CurrentBar - _entryBar;
            double price = Close[0];
            double high  = High[0];
            double low   = Low[0];

            // ═══════════════════════════════════════════════════
            // STOP LOGIC — The key v2 change:
            //   Before DCA: hard stop from ORIGINAL entry
            //   After DCA: tighter stop from AVERAGE entry
            // ═══════════════════════════════════════════════════
            
            if (_dcaDone && _dcaFilled)
            {
                // ── Post-DCA stop: from AVERAGE entry ──
                double adverseFromAvg;
                if (_direction == 1)
                    adverseFromAvg = _avgEntry - low;
                else
                    adverseFromAvg = high - _avgEntry;

                if (adverseFromAvg >= DcaStopPoints)
                {
                    ExitTrade("DcaStop");
                    return;
                }
            }
            else
            {
                // ── Pre-DCA stop: from ORIGINAL entry ──
                double adverseFromOrig;
                if (_direction == 1)
                    adverseFromOrig = _origEntry - low;
                else
                    adverseFromOrig = high - _origEntry;

                if (adverseFromOrig >= HardStopPoints)
                {
                    ExitTrade("HardStop");
                    return;
                }

                // ── DCA check ──
                if (!_dcaDone && _dcaAllowed && DcaQty > 0)
                {
                    double adverseForDca;
                    if (_direction == 1)
                        adverseForDca = _origEntry - low;
                    else
                        adverseForDca = high - _origEntry;

                    if (adverseForDca >= DcaTrigger)
                    {
                        AddDcaPosition();
                    }
                }
            }

            // ── Take profit check (from AVERAGE entry) ──
            double favorable;
            if (_direction == 1)
                favorable = high - _avgEntry;
            else
                favorable = _avgEntry - low;

            if (favorable >= TpPoints)
            {
                ExitTrade("TakeProfit");
                return;
            }

            // ── Time stop ──
            if (barsHeld >= TimeStopBars)
            {
                ExitTrade("TimeStop");
                return;
            }
        }

        private void AddDcaPosition()
        {
            _dcaDone = true;

            if (_direction == 1)
                EnterLong(DcaQty, SIG_DCA);
            else
                EnterShort(DcaQty, SIG_DCA);

            // Estimate avg entry for immediate management (actual fill updates in OnExecutionUpdate)
            double dcaPrice = (_direction == 1) ? Low[0] : High[0];
            double totalCost = _avgEntry * _totalQty + dcaPrice * DcaQty;
            _totalQty += DcaQty;
            _avgEntry = totalCost / _totalQty;
        }

        private void ExitTrade(string reason)
        {
            if (_direction == 1)
            {
                ExitLong(reason + "I", SIG_INIT);
                if (_dcaFilled)
                    ExitLong(reason + "D", SIG_DCA);
            }
            else
            {
                ExitShort(reason + "I", SIG_INIT);
                if (_dcaFilled)
                    ExitShort(reason + "D", SIG_DCA);
            }
        }

        #endregion

        #region Execution Tracking

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition,
            string orderId, DateTime time)
        {
            if (execution.Order == null) return;

            string name = execution.Order.Name ?? "";

            // ── Entry fills ──
            if (name == SIG_INIT && execution.Order.OrderState == OrderState.Filled)
            {
                _initFilled = true;
                _initFillPrice = price;
                _origEntry = price;
                _avgEntry = price;
                _totalQty = InitialQty;
                _inTrade = true;
                _entryBar = CurrentBar;
            }
            else if (name == SIG_DCA && execution.Order.OrderState == OrderState.Filled)
            {
                _dcaFilled = true;
                _dcaFillPrice = price;
                // Recalculate true average entry from actual fills
                double totalCost = _initFillPrice * InitialQty + _dcaFillPrice * DcaQty;
                _totalQty = InitialQty + DcaQty;
                _avgEntry = totalCost / _totalQty;
            }

            // ── Position went flat = trade closed ──
            if (Position.MarketPosition == MarketPosition.Flat && _inTrade)
            {
                double pnl = ComputeRealizedPnLSinceLast();
                RecordTradeResult(pnl);

                _inTrade = false;
                _lastExitBar = CurrentBar;
                _initFilled = false;
                _dcaFilled = false;
            }
        }

        #endregion

        #region Daily Management

        private double ComputeRealizedPnLSinceLast()
        {
            double pnl = 0;
            int total = SystemPerformance.AllTrades.Count;
            for (int i = _priorTradeCount; i < total; i++)
            {
                pnl += SystemPerformance.AllTrades[i].ProfitCurrency;
            }
            _priorTradeCount = total;
            return pnl;
        }

        private void RecordTradeResult(double pnlDollars)
        {
            _dailyPnL += pnlDollars;
            _dailyTradeCount++;

            if (pnlDollars > 0)
                _dailyConsecLosses = 0;
            else
                _dailyConsecLosses++;
        }

        #endregion

        #region Properties — EMA

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "EMA Fast Period", Order = 1, GroupName = "1. EMA Alignment")]
        public int EmaFastPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "EMA Med Period", Order = 2, GroupName = "1. EMA Alignment")]
        public int EmaMedPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 400)]
        [Display(Name = "EMA Slow Period", Order = 3, GroupName = "1. EMA Alignment")]
        public int EmaSlowPeriod { get; set; }

        #endregion

        #region Properties — Stochastic

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Stoch Period K", Order = 1, GroupName = "2. Stochastic")]
        public int StochPeriodK { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Stoch Period D", Order = 2, GroupName = "2. Stochastic")]
        public int StochPeriodD { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Stoch Smooth", Order = 3, GroupName = "2. Stochastic")]
        public int StochSmooth { get; set; }

        [NinjaScriptProperty]
        [Range(1, 99)]
        [Display(Name = "Stoch Long Threshold", Description = "Enter long when K < this", Order = 4, GroupName = "2. Stochastic")]
        public int StochLongThresh { get; set; }

        [NinjaScriptProperty]
        [Range(1, 99)]
        [Display(Name = "Stoch Short Threshold", Description = "Enter short when K > this", Order = 5, GroupName = "2. Stochastic")]
        public int StochShortThresh { get; set; }

        #endregion

        #region Properties — DCA

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Initial Qty", Order = 1, GroupName = "3. DCA")]
        public int InitialQty { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "DCA Qty", Description = "Contracts to add on DCA (0 = no DCA)", Order = 2, GroupName = "3. DCA")]
        public int DcaQty { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "DCA Trigger", Description = "Points adverse from entry to trigger DCA", Order = 3, GroupName = "3. DCA")]
        public double DcaTrigger { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reduce DCA After Loss", Description = "Disable DCA after a consecutive loss", Order = 4, GroupName = "3. DCA")]
        public bool ReduceDcaAfterLoss { get; set; }

        #endregion

        #region Properties — Exits

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "TP Points", Description = "Take profit points from AVG entry", Order = 1, GroupName = "4. Exits")]
        public double TpPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Hard Stop Points", Description = "Hard stop from ORIG entry (before DCA)", Order = 2, GroupName = "4. Exits")]
        public double HardStopPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "DCA Stop Points", Description = "Stop from AVG entry (after DCA fills)", Order = 3, GroupName = "4. Exits")]
        public double DcaStopPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Time Stop Bars", Description = "Max bars to hold before time exit", Order = 4, GroupName = "4. Exits")]
        public int TimeStopBars { get; set; }

        #endregion

        #region Properties — Time Filter

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Trading Start Hour", Order = 1, GroupName = "5. Time Filter")]
        public int TradingStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Trading End Hour", Order = 2, GroupName = "5. Time Filter")]
        public int TradingEndHour { get; set; }

        #endregion

        #region Properties — Daily Management

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Daily Profit Target ($)", Order = 1, GroupName = "6. Daily Management")]
        public double DailyProfitTarget { get; set; }

        [NinjaScriptProperty]
        [Range(-10000, 0)]
        [Display(Name = "Daily Loss Limit ($)", Order = 2, GroupName = "6. Daily Management")]
        public double DailyLossLimit { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Max Trades Per Day", Order = 3, GroupName = "6. Daily Management")]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Consec Losses", Description = "Stop after N consecutive losses in a day", Order = 4, GroupName = "6. Daily Management")]
        public int MaxConsecLosses { get; set; }

        #endregion

        #region Properties — Cooldown

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Cooldown Bars", Description = "Min bars between trade exits and new entries", Order = 1, GroupName = "7. Cooldown")]
        public int CooldownBars { get; set; }

        #endregion
    }
}
