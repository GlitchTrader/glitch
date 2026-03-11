// MysticPulseCHE_Strategy_MVP.cs
// Multi-timeframe CHE: run indicator on higher TF, execute on lower TF
// 3 contracts: TP1/TP2 partial exits, trailing stop protects TP3 runner
// Features: Flip confirmation, profit protection, time filter
// MNQ: 1 point = 4 ticks

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class MysticPulseCHE_Strategy_MVP : Strategy
    {
        // CHE state (computed on higher TF)
        private double smoothedTR, smoothedDMPlus, smoothedDMMinus;
        private double prevDIPlus, prevDIMinus;
        private int posCount, negCount;
        private bool? priorBullish;
        private bool bullish, isFlip;

        // Trade state
        private double entryPrice;
        private string tradeDirection;
        private Order stopOrder;
        private Order tp1Order, tp2Order, tp3Order;
        private bool tp1Filled, tp2Filled;

        // Managed protective stop state (one-way, never loosens)
        private double extremePriceSinceTP1;
        private double currentStopPrice;

        // Flip confirmation state
        private int barsSinceFlipAgainst;
        private bool flipAgainstPending;

        // Profit protection state
        private double maxFavorableExcursion;
        private bool movedToBreakeven;

        // Trailing entry state (wait for price to confirm direction)
        private bool pendingEntry;
        private string pendingDirection;
        private double flipPrice;         // Price at flip
        private int barsSinceFlip;

        // Daily P&L tracking
        private double dailyPnL;
        private DateTime currentTradingDay;
        private bool dailyLimitReached;


        // Indicators on higher TF
        private SMA smaHighHTF, smaLowHTF, smaCloseHTF;

        private static readonly Brush Green = Brushes.LimeGreen;
        private static readonly Brush Red = Brushes.Red;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "MysticPulseCHE_MVP";
                Calculate = Calculate.OnEachTick;  // Required for intrabar stop management
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                StartBehavior = StartBehavior.WaitUntilFlat;
                IsOverlay = true;

                // CHE parameters
                Length = 30;
                SmoothingFactor = 15;
                HigherTFMinutes = 5;

                // Risk (in points; 1 pt = 4 ticks for MNQ)
                // 3 contracts: TP1 quick lock, TP2 mid, TP3 runner
                TP1Points = 25;
                TP2Points = 45;
                TP3Points = 400;
                StopPoints = 38;
                TrailPoints = 38;
                EnableTrailingStop = true;

                // Flip confirmation (wait X bars before exiting on flip)
                FlipConfirmBars = 1;

                // Profit protection (move to breakeven after X pts profit)
                BreakevenTrigger = 38;
                EnableBreakeven = true;

                // Time filter (avoid choppy hours) - Exchange time (CT for CME)
                EnableTimeFilter = true;
                TradingStartHour = 6;   // 6 AM CT (= 7 AM ET)
                TradingEndHour = 15;    // 3 PM CT (= 4 PM ET)
                AvoidLunchStart = 5;
                AvoidLunchEnd = 5;

                // Trailing entry (require price confirmation before entering)
                EnableTrailingEntry = true;
                EntryConfirmPoints = 5;  // Price must move 5 pts in signal direction
                MaxEntryBars = 9;        // Cancel pending entry after 9 bars

                // Daily P&L limits
                EnableDailyLimits = true;
                DailyProfitTarget = 120;  // Lock in profits at $120/day
                DailyLossLimit = 240;     // Stop trading at -$240/day
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, HigherTFMinutes);
            }
            else if (State == State.DataLoaded)
            {
                smaHighHTF = SMA(Highs[1], SmoothingFactor);
                smaLowHTF = SMA(Lows[1], SmoothingFactor);
                smaCloseHTF = SMA(Closes[1], SmoothingFactor);

                smoothedTR = smoothedDMPlus = smoothedDMMinus = double.NaN;
                
                // Initialize daily tracking
                dailyPnL = 0;
                currentTradingDay = DateTime.MinValue;
                dailyLimitReached = false;
                
                ResetTradeState();
            }
        }

        private void ResetTradeState()
        {
            entryPrice = double.NaN;
            tradeDirection = "";
            stopOrder = null;
            tp1Order = tp2Order = tp3Order = null;
            tp1Filled = tp2Filled = false;
            extremePriceSinceTP1 = double.NaN;
            currentStopPrice = double.NaN;
            barsSinceFlipAgainst = 0;
            flipAgainstPending = false;
            maxFavorableExcursion = 0;
            movedToBreakeven = false;
            // Trailing entry state
            pendingEntry = false;
            pendingDirection = "";
            flipPrice = double.NaN;
            barsSinceFlip = 0;
        }

        protected override void OnBarUpdate()
        {
            // Higher TF: compute CHE (only on bar close of HTF)
            if (BarsInProgress == 1)
            {
                if (CurrentBars[1] < 10) return;
                ComputeCHE();
                return;
            }

            // Primary TF: trade logic
            if (BarsInProgress == 0)
            {
                if (CurrentBars[0] < 10 || CurrentBars[1] < 10) return;

                // Check for new trading day - reset daily P&L
                DateTime barDate = Time[0].Date;
                if (barDate != currentTradingDay)
                {
                    currentTradingDay = barDate;
                    dailyPnL = 0;
                    dailyLimitReached = false;
                }

                // Color bars (only on bar close to avoid flickering)
                if (IsFirstTickOfBar)
                {
                    BarBrushes[0] = bullish ? Green : Red;
                    CandleOutlineBrushes[0] = BarBrushes[0];
                }

                // === IN POSITION ===
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    // One managed stop engine:
                    // - initial fixed stop
                    // - move to breakeven once trigger is hit
                    // - then trail with fixed gap, never backward
                    UpdateManagedProtectiveStop();

                    // Check for flip against us
                    bool isFlipAgainst = false;
                    if (tradeDirection == "LONG" && !bullish && isFlip)
                        isFlipAgainst = true;
                    else if (tradeDirection == "SHORT" && bullish && isFlip)
                        isFlipAgainst = true;

                    // Flip confirmation logic
                    if (isFlipAgainst)
                    {
                        if (!flipAgainstPending)
                        {
                            flipAgainstPending = true;
                            barsSinceFlipAgainst = 0;
                        }
                    }

                    // Count bars since flip started (on first tick of each bar)
                    if (flipAgainstPending && IsFirstTickOfBar)
                    {
                        barsSinceFlipAgainst++;
                    }

                    // Check if flip is confirmed OR price flipped back (cancel pending exit)
                    if (flipAgainstPending)
                    {
                        // If color flipped back in our favor, cancel pending exit
                        bool backInFavor = (tradeDirection == "LONG" && bullish) || 
                                           (tradeDirection == "SHORT" && !bullish);
                        if (backInFavor)
                        {
                            flipAgainstPending = false;
                            barsSinceFlipAgainst = 0;
                        }
                        // If flip confirmed (held for FlipConfirmBars), exit
                        else if (barsSinceFlipAgainst >= FlipConfirmBars)
                        {
                            CancelAllOrders();
                            if (Position.MarketPosition == MarketPosition.Long)
                                ExitLong("FLIP", "");
                            else
                                ExitShort("FLIP", "");
                            return;
                        }
                    }

                }
                // === FLAT - LOOK FOR ENTRY ===
                else
                {
                    // Reset flip pending state when flat
                    flipAgainstPending = false;
                    barsSinceFlipAgainst = 0;

                    // Daily P&L limits check
                    if (EnableDailyLimits && dailyLimitReached)
                    {
                        pendingEntry = false;  // Cancel any pending entry
                        return;
                    }

                    // Handle trailing entry confirmation (if enabled and pending)
                    if (EnableTrailingEntry && pendingEntry)
                    {
                        // Count bars since flip started (on first tick of new bar)
                        if (IsFirstTickOfBar)
                            barsSinceFlip++;

                        // Cancel if waited too long
                        if (barsSinceFlip > MaxEntryBars)
                        {
                            pendingEntry = false;
                            pendingDirection = "";
                            return;
                        }

                        // Check if price has moved enough in our direction to confirm
                        double currentPrice = Close[0];
                        bool confirmed = false;
                        
                        if (pendingDirection == "LONG" && currentPrice >= flipPrice + EntryConfirmPoints)
                            confirmed = true;
                        else if (pendingDirection == "SHORT" && currentPrice <= flipPrice - EntryConfirmPoints)
                            confirmed = true;

                        if (confirmed)
                        {
                            // Save direction before ResetTradeState clears it
                            string entryDir = pendingDirection;
                            ResetTradeState();
                            tradeDirection = entryDir;

                            if (tradeDirection == "LONG")
                                EnterLong(3, "L");
                            else
                                EnterShort(3, "S");
                        }
                        return;  // Don't process new flips while waiting
                    }

                    // Only enter on first tick of bar to avoid multiple entries
                    if (isFlip && IsFirstTickOfBar)
                    {
                        // Time filter check
                        if (EnableTimeFilter && !IsWithinTradingHours())
                            return;

                        // Trailing entry: set up pending entry instead of entering immediately
                        if (EnableTrailingEntry)
                        {
                            pendingEntry = true;
                            pendingDirection = bullish ? "LONG" : "SHORT";
                            flipPrice = Close[0];
                            barsSinceFlip = 0;
                            return;
                        }

                        // Direct entry (trailing entry disabled)
                        ResetTradeState();

                        if (bullish)
                        {
                            tradeDirection = "LONG";
                            EnterLong(3, "L");  // 3 contracts
                        }
                        else
                        {
                            tradeDirection = "SHORT";
                            EnterShort(3, "S");  // 3 contracts
                        }
                    }
                }
            }
        }

        private bool IsWithinTradingHours()
        {
            // Time[0] is in exchange time (CT for CME/MNQ)
            int hour = Time[0].Hour;
            
            // Outside main trading hours
            if (hour < TradingStartHour || hour >= TradingEndHour)
                return false;
            
            // During lunch avoidance period
            if (hour >= AvoidLunchStart && hour < AvoidLunchEnd)
                return false;
            
            return true;
        }

        private void UpdateManagedProtectiveStop()
        {
            MarketPosition direction = GetTradeMarketPosition();
            if (direction == MarketPosition.Flat || double.IsNaN(entryPrice))
                return;

            int ticksPerPt = 4;
            int qty = Math.Max(1, Position.Quantity);
            double pointValueInPrice = TickSize * ticksPerPt;
            double initialStopDistance = StopPoints * pointValueInPrice;
            double trailDistance = TrailPoints * pointValueInPrice;

            if (double.IsNaN(extremePriceSinceTP1))
                extremePriceSinceTP1 = entryPrice;

            // Track favorable extreme from live/historical-safe prices.
            double currentHigh = GetCurrentAskSafe();
            double currentLow = GetCurrentBidSafe();

            if (direction == MarketPosition.Long)
                extremePriceSinceTP1 = Math.Max(extremePriceSinceTP1, Math.Max(High[0], currentHigh));
            else
                extremePriceSinceTP1 = Math.Min(extremePriceSinceTP1, Math.Min(Low[0], currentLow));

            double favorablePts = direction == MarketPosition.Long
                ? (extremePriceSinceTP1 - entryPrice) / pointValueInPrice
                : (entryPrice - extremePriceSinceTP1) / pointValueInPrice;

            if (favorablePts > maxFavorableExcursion)
                maxFavorableExcursion = favorablePts;

            bool breakevenArmed = EnableBreakeven && favorablePts >= BreakevenTrigger;
            if (breakevenArmed)
                movedToBreakeven = true;

            double desiredStop = direction == MarketPosition.Long
                ? entryPrice - initialStopDistance
                : entryPrice + initialStopDistance;

            // After BE trigger: stop is at least breakeven, then trails by fixed gap.
            if (breakevenArmed)
            {
                if (EnableTrailingStop)
                {
                    desiredStop = direction == MarketPosition.Long
                        ? extremePriceSinceTP1 - trailDistance
                        : extremePriceSinceTP1 + trailDistance;
                }
                else
                {
                    desiredStop = entryPrice;
                }

                if (direction == MarketPosition.Long && desiredStop < entryPrice)
                    desiredStop = entryPrice;
                if (direction == MarketPosition.Short && desiredStop > entryPrice)
                    desiredStop = entryPrice;
            }

            // Monotonic stop: never move backward.
            if (!double.IsNaN(currentStopPrice))
            {
                if (direction == MarketPosition.Long)
                    desiredStop = Math.Max(desiredStop, currentStopPrice);
                else
                    desiredStop = Math.Min(desiredStop, currentStopPrice);
            }

            bool missingStop = !IsOrderActive(stopOrder);
            bool qtyMismatch = stopOrder != null && IsOrderActive(stopOrder) && stopOrder.Quantity != qty;
            bool betterPrice = double.IsNaN(currentStopPrice)
                || (direction == MarketPosition.Long && desiredStop > currentStopPrice + TickSize)
                || (direction == MarketPosition.Short && desiredStop < currentStopPrice - TickSize);

            if (missingStop || qtyMismatch || betterPrice)
                UpsertProtectiveStop(direction, qty, desiredStop);
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null) return;
            string name = execution.Order.Name ?? "";

            int ticksPerPt = 4;

            // Entry fill - place brackets: stop for 3, TP1/TP2/TP3 for 1 each
            if (name == "L" && execution.Order.OrderState == OrderState.Filled)
            {
                entryPrice = execution.Order.AverageFillPrice;
                tradeDirection = "LONG";
                double stopPrice = entryPrice - (StopPoints * TickSize * ticksPerPt);
                double tp1Price = entryPrice + (TP1Points * TickSize * ticksPerPt);
                double tp2Price = entryPrice + (TP2Points * TickSize * ticksPerPt);
                double tp3Price = entryPrice + (TP3Points * TickSize * ticksPerPt);

                stopOrder = ExitLongStopMarket(0, true, 3, stopPrice, "STOP", "L");
                currentStopPrice = stopPrice;
                extremePriceSinceTP1 = entryPrice;
                maxFavorableExcursion = 0;
                movedToBreakeven = false;
                tp1Filled = false;
                tp2Filled = false;
                tp1Order = ExitLongLimit(0, true, 1, tp1Price, "TP1", "L");
                tp2Order = ExitLongLimit(0, true, 1, tp2Price, "TP2", "L");
                tp3Order = ExitLongLimit(0, true, 1, tp3Price, "TP3", "L");
            }
            else if (name == "S" && execution.Order.OrderState == OrderState.Filled)
            {
                entryPrice = execution.Order.AverageFillPrice;
                tradeDirection = "SHORT";
                double stopPrice = entryPrice + (StopPoints * TickSize * ticksPerPt);
                double tp1Price = entryPrice - (TP1Points * TickSize * ticksPerPt);
                double tp2Price = entryPrice - (TP2Points * TickSize * ticksPerPt);
                double tp3Price = entryPrice - (TP3Points * TickSize * ticksPerPt);

                stopOrder = ExitShortStopMarket(0, true, 3, stopPrice, "STOP", "S");
                currentStopPrice = stopPrice;
                extremePriceSinceTP1 = entryPrice;
                maxFavorableExcursion = 0;
                movedToBreakeven = false;
                tp1Filled = false;
                tp2Filled = false;
                tp1Order = ExitShortLimit(0, true, 1, tp1Price, "TP1", "S");
                tp2Order = ExitShortLimit(0, true, 1, tp2Price, "TP2", "S");
                tp3Order = ExitShortLimit(0, true, 1, tp3Price, "TP3", "S");
            }
            // TP1 fill - keep stop synced for remaining quantity
            else if (name == "TP1" && execution.Order.OrderState == OrderState.Filled && !tp1Filled)
            {
                tp1Filled = true;
                UpdateManagedProtectiveStop();
            }
            // TP2 fill - keep stop synced for remaining quantity
            else if (name == "TP2" && execution.Order.OrderState == OrderState.Filled && !tp2Filled)
            {
                tp2Filled = true;
                UpdateManagedProtectiveStop();
            }

            // Track daily P&L on exit fills
            if (EnableDailyLimits && execution.Order.OrderState == OrderState.Filled)
            {
                bool isExit = name == "TP1" || name == "TP2" || name == "TP3" || name == "STOP" || name == "BE" || name == "TRAIL" || name == "FLIP" 
                              || name.Contains("close") || name.Contains("Close");
                
                if (isExit && !double.IsNaN(entryPrice))
                {
                    // Calculate profit: (exit - entry) * quantity * point_value
                    // MNQ: $2 per point (4 ticks * $0.50)
                    double pointValue = 2.0;
                    double pnlPoints = 0;
                    
                    if (tradeDirection == "LONG")
                        pnlPoints = price - entryPrice;
                    else if (tradeDirection == "SHORT")
                        pnlPoints = entryPrice - price;
                    
                    double tradePnL = pnlPoints * quantity * pointValue;
                    dailyPnL += tradePnL;
                    
                    // Check limits
                    if (dailyPnL >= DailyProfitTarget || dailyPnL <= -DailyLossLimit)
                    {
                        dailyLimitReached = true;
                    }
                }
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity,
            int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            // Track our order references
            if (order.Name == "STOP" || order.Name == "BE" || order.Name == "TRAIL")
                stopOrder = order;
            else if (order.Name == "TP1")
                tp1Order = order;
            else if (order.Name == "TP2")
                tp2Order = order;
            else if (order.Name == "TP3")
                tp3Order = order;
        }

        private void CancelAllOrders()
        {
            if (IsOrderActive(stopOrder))
                CancelOrder(stopOrder);
            if (IsOrderActive(tp1Order))
                CancelOrder(tp1Order);
            if (IsOrderActive(tp2Order))
                CancelOrder(tp2Order);
            if (IsOrderActive(tp3Order))
                CancelOrder(tp3Order);
        }

        private bool IsOrderActive(Order order)
        {
            if (order == null) return false;
            return order.OrderState != OrderState.Cancelled
                && order.OrderState != OrderState.Rejected
                && order.OrderState != OrderState.Filled;
        }

        private MarketPosition GetTradeMarketPosition()
        {
            if (Position.MarketPosition != MarketPosition.Flat)
                return Position.MarketPosition;

            if (tradeDirection == "LONG")
                return MarketPosition.Long;
            if (tradeDirection == "SHORT")
                return MarketPosition.Short;

            return MarketPosition.Flat;
        }

        private void UpsertProtectiveStop(MarketPosition direction, int quantity, double stopPrice)
        {
            if (direction == MarketPosition.Flat || quantity <= 0 || double.IsNaN(stopPrice))
                return;

            bool canChangeStop = stopOrder != null &&
                (stopOrder.OrderState == OrderState.Working
                || stopOrder.OrderState == OrderState.Accepted
                || stopOrder.OrderState == OrderState.Submitted);
            bool updated = false;

            if (canChangeStop)
            {
                ChangeOrder(stopOrder, quantity, 0, stopPrice);
                updated = true;
            }
            else if (!IsOrderActive(stopOrder))
            {
                if (direction == MarketPosition.Long)
                    stopOrder = ExitLongStopMarket(0, true, quantity, stopPrice, "STOP", "L");
                else
                    stopOrder = ExitShortStopMarket(0, true, quantity, stopPrice, "STOP", "S");
                updated = true;
            }

            if (updated)
                currentStopPrice = stopPrice;
        }

        private double GetCurrentAskSafe()
        {
            double ask = GetCurrentAsk(0);
            return ask > 0 ? ask : Close[0];
        }

        private double GetCurrentBidSafe()
        {
            double bid = GetCurrentBid(0);
            return bid > 0 ? bid : Close[0];
        }

        private void ComputeCHE()
        {
            if (CurrentBars[1] < 2) { isFlip = false; return; }

            double h = smaHighHTF[0], l = smaLowHTF[0];
            double hp = smaHighHTF[1], lp = smaLowHTF[1], cp = smaCloseHTF[1];

            double tr = Math.Max(h - l, Math.Max(Math.Abs(h - cp), Math.Abs(l - cp)));
            double up = h - hp, dn = lp - l;
            double dmp = (up > dn && up > 0) ? up : 0;
            double dmm = (dn > up && dn > 0) ? dn : 0;

            if (double.IsNaN(smoothedTR))
            {
                smoothedTR = tr; smoothedDMPlus = dmp; smoothedDMMinus = dmm;
            }
            else
            {
                smoothedTR = smoothedTR - smoothedTR / Length + tr;
                smoothedDMPlus = smoothedDMPlus - smoothedDMPlus / Length + dmp;
                smoothedDMMinus = smoothedDMMinus - smoothedDMMinus / Length + dmm;
            }

            double dip = smoothedTR > 0 ? smoothedDMPlus / smoothedTR * 100 : 0;
            double dim = smoothedTR > 0 ? smoothedDMMinus / smoothedTR * 100 : 0;

            if (dip > prevDIPlus && dip > dim) { posCount++; negCount = 0; }
            if (dim > prevDIMinus && dim > dip) { negCount++; posCount = 0; }

            prevDIPlus = dip; prevDIMinus = dim;
            bullish = posCount >= negCount;
            isFlip = priorBullish.HasValue && priorBullish.Value != bullish;
            priorBullish = bullish;
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Length", Order = 0, GroupName = "1. CHE")]
        public int Length { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "SmoothingFactor", Order = 1, GroupName = "1. CHE")]
        public int SmoothingFactor { get; set; }

        [NinjaScriptProperty]
        [Range(1, 120)]
        [Display(Name = "Higher TF (minutes)", Order = 2, GroupName = "1. CHE")]
        public int HigherTFMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(1, 400)]
        [Display(Name = "TP1 (points)", Order = 0, GroupName = "2. Risk")]
        public int TP1Points { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "TP2 (points)", Order = 1, GroupName = "2. Risk")]
        public int TP2Points { get; set; }

        [NinjaScriptProperty]
        [Range(1, 800)]
        [Display(Name = "TP3 Runner (points)", Order = 2, GroupName = "2. Risk")]
        public int TP3Points { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Stop (points)", Order = 3, GroupName = "2. Risk")]
        public int StopPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Trail (points)", Order = 4, GroupName = "2. Risk")]
        public int TrailPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Trailing Stop after Breakeven", Order = 5, GroupName = "2. Risk")]
        public bool EnableTrailingStop { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Flip Confirm Bars", Order = 0, GroupName = "3. Filters")]
        public int FlipConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Breakeven Trigger (points)", Order = 1, GroupName = "3. Filters")]
        public int BreakevenTrigger { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven Protection", Order = 2, GroupName = "3. Filters")]
        public bool EnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", Order = 3, GroupName = "3. Filters")]
        public bool EnableTimeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Trading Start Hour (CT)", Order = 4, GroupName = "3. Filters")]
        public int TradingStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Trading End Hour (CT)", Order = 5, GroupName = "3. Filters")]
        public int TradingEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Avoid Lunch Start (CT)", Order = 6, GroupName = "3. Filters")]
        public int AvoidLunchStart { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Avoid Lunch End (CT)", Order = 7, GroupName = "3. Filters")]
        public int AvoidLunchEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Trailing Entry", Order = 8, GroupName = "3. Filters")]
        public bool EnableTrailingEntry { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Entry Confirm Points", Description = "Points price must move in signal direction to confirm entry", Order = 9, GroupName = "3. Filters")]
        public double EntryConfirmPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Entry Wait Bars", Description = "Cancel pending entry if not triggered within this many bars", Order = 10, GroupName = "3. Filters")]
        public int MaxEntryBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Daily Limits", Order = 11, GroupName = "3. Filters")]
        public bool EnableDailyLimits { get; set; }

        [NinjaScriptProperty]
        [Range(50, 2000)]
        [Display(Name = "Daily Profit Target ($)", Description = "Stop trading after reaching this profit", Order = 12, GroupName = "3. Filters")]
        public double DailyProfitTarget { get; set; }

        [NinjaScriptProperty]
        [Range(50, 2000)]
        [Display(Name = "Daily Loss Limit ($)", Description = "Stop trading after losing this amount", Order = 13, GroupName = "3. Filters")]
        public double DailyLossLimit { get; set; }
        #endregion
    }
}
