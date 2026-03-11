// GlitchDirectionalFlipStrategyV2.cs
// Versão 2: TP3 (runner) usa a mesma lógica de saída do Teste21c (stop fixo em ticks ou Swing+ATR).
// Multi-timeframe CHE: run indicator on higher TF, execute on lower TF
// 3 contracts: TP1/TP2 partial exits; TP3 runner sai por stop fixo ou Swing+ATR (estilo Teste21c)
// MNQ: 1 point = 4 ticks

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class GlitchDirectionalFlipStrategyV2 : Strategy
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
        private Order tp1Order, tp2Order;
        private bool tp1Filled, tp2Filled;

        // Managed protective stop state (one-way, never loosens)
        private double extremePriceSinceTP1;
        private double currentStopPrice;

        // TP3 runner: saída estilo Teste21c (apenas quando qty==1 após TP1+TP2)
        private bool _tp3Teste21cFixedPlaced;

        // Flip confirmation state
        private int barsSinceFlipAgainst;
        private bool flipAgainstPending;

        // Profit protection state
        private double maxFavorableExcursion;
        private bool movedToBreakeven;
        private bool _loggedBreakeven;
        private bool _loggedDailyLimit;
        private bool _loggedWaitingBars;
        private int _flatLogBarCount;
        // Flip detectado no HTF (BIP=1); processado no LTF (BIP=0) para evitar perder o flip pela ordem de execução
        private bool _flipJustOccurred;
        private bool _flipDirectionLong;
        // Trailing entry state
        private bool pendingEntry;
        private string pendingDirection;
        private double flipPrice;
        private int barsSinceFlip;

        // Daily P&L tracking
        private double dailyPnL;
        private DateTime currentTradingDay;
        private bool dailyLimitReached;

        // Indicators on higher TF
        private SMA smaHighHTF, smaLowHTF, smaCloseHTF;
        // TP3 exit (estilo Teste21c) - série primária
        private Swing _swingTP3;
        private ATR _atrTP3;

        private static readonly Brush Green = Brushes.LimeGreen;
        private static readonly Brush Red = Brushes.Red;
        private static readonly Brush PendingLong = Brushes.DodgerBlue;
        private static readonly Brush PendingShort = Brushes.OrangeRed;
        private static readonly Brush EntryLong = Brushes.DeepSkyBlue;
        private static readonly Brush EntryShort = Brushes.OrangeRed;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "GlitchDirectionalFlipStrategyV2";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                StartBehavior = StartBehavior.WaitUntilFlat;
                IsOverlay = true;
				IncludeCommission = true;

                Length = 29;
                SmoothingFactor = 18;

                TP1Points = 45;
                TP2Points = 50;
                StopPoints = 45;
                TrailPoints = 35;
                EnableTrailingStop = true;

                FlipConfirmBars = 2;
                BreakevenTrigger = 35;
                EnableBreakeven = true;

                EnableTimeFilter = true;
                TradingStartHour = 5;
                TradingEndHour = 14;
                AvoidLunchStart = 5;
                AvoidLunchEnd = 5;

                EnableTrailingEntry = false;
                EntryConfirmPoints = 1;
                MaxEntryBars = 9;
                CancelPendingOnOppositeSignal = false;

                EnableStateBarColoring = true;
                HighlightPendingEntry = true;
                ShowFlipMarkers = true;
                ShowActualEntryMarkers = true;

                EnableDailyLimits = true;
                DailyProfitTarget = 280;
                DailyLossLimit = 580;

                // TP3 exit - mesma lógica do Teste21c
                UseSwingStopTP3 = true;
                SwingStrengthTP3 = 15;
                SwingAtrOffsetMultiplierTP3 = 0;
                FallbackAtrMultiplierTP3 = 0;
                MinStopImproveTicksTP3 = 10;
                StopLossTicksTP3 = 100;
                MaxStopLossTicksTP3 = 200;
                AtrPeriodTP3 = 14;
                MaxTP3StopDistanceFromClosePoints = 50;  // 0 = sem limite; >0 = stop não pode ficar a mais de X pontos do Close

                EnableLogs = false;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 5);
            }
            else if (State == State.DataLoaded)
            {
                smaHighHTF = SMA(Highs[1], SmoothingFactor);
                smaLowHTF = SMA(Lows[1], SmoothingFactor);
                smaCloseHTF = SMA(Closes[1], SmoothingFactor);

                _swingTP3 = Swing(Math.Max(1, SwingStrengthTP3));
                _atrTP3 = ATR(AtrPeriodTP3);

                smoothedTR = smoothedDMPlus = smoothedDMMinus = double.NaN;
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
            tp1Order = tp2Order = null;
            tp1Filled = tp2Filled = false;
            extremePriceSinceTP1 = double.NaN;
            currentStopPrice = double.NaN;
            _tp3Teste21cFixedPlaced = false;
            barsSinceFlipAgainst = 0;
            flipAgainstPending = false;
            maxFavorableExcursion = 0;
            movedToBreakeven = false;
            pendingEntry = false;
            pendingDirection = "";
            flipPrice = double.NaN;
            barsSinceFlip = 0;
            _loggedBreakeven = false;
            _loggedDailyLimit = false;
            _flipJustOccurred = false;
        }

        /// <summary>Escreve na Output window quando EnableLogs = true.</summary>
        private void Log(string message)
        {
            if (!EnableLogs) return;
            string timestamp = (Bars != null && CurrentBar >= 0 && Time != null && CurrentBar < Time.Count)
                ? Time[0].ToString("HH:mm:ss")
                : DateTime.Now.ToString("HH:mm:ss");
            Print(string.Format("[{0}] GlitchV2 | {1}", timestamp, message));
        }

        /// <summary>Log do "pensamento" da estratégia: razões e estado.</summary>
        private void LogThink(string message)
        {
            if (!EnableLogs) return;
            string timestamp = (Bars != null && CurrentBar >= 0 && Time != null && CurrentBar < Time.Count)
                ? Time[0].ToString("HH:mm:ss")
                : DateTime.Now.ToString("HH:mm:ss");
            Print(string.Format("[{0}] GlitchV2 |   → {1}", timestamp, message));
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 1)
            {
                if (CurrentBars[1] < 10) return;
                ComputeCHE();
                return;
            }

            if (BarsInProgress == 0)
            {
                if (CurrentBars[0] < 10 || CurrentBars[1] < 10)
                {
                    if (EnableLogs && !_loggedWaitingBars)
                    {
                        _loggedWaitingBars = true;
                        LogThink(string.Format("Aguardando barras: primary={0}, secondary={1} (mínimo 10 em cada). CHE só começa após ter 10 barras em ambos os timeframes.", CurrentBars[0], CurrentBars[1]));
                    }
                    return;
                }
                _loggedWaitingBars = false;

                DateTime barDate = Time[0].Date;
                if (barDate != currentTradingDay)
                {
                    currentTradingDay = barDate;
                    dailyPnL = 0;
                    dailyLimitReached = false;
                    _loggedDailyLimit = false;
                    _flatLogBarCount = 0;
                    if (EnableLogs) LogThink("Novo dia de trading. P&L diário zerado.");
                }

                if (IsFirstTickOfBar && EnableStateBarColoring)
                {
                    Brush barBrush = bullish ? Green : Red;
                    if (HighlightPendingEntry && Position.MarketPosition == MarketPosition.Flat && pendingEntry)
                        barBrush = pendingDirection == "LONG" ? PendingLong : PendingShort;
                    BarBrushes[0] = barBrush;
                    CandleOutlineBrushes[0] = barBrush;
                }

                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    int qty = Math.Max(1, Position.Quantity);
                    if (IsFirstTickOfBar)
                    {
                        string fase = !tp1Filled ? "inicial (aguardando TP1)" : (!tp2Filled ? "após TP1 (aguardando TP2)" : "runner TP3");
                        double pts = double.IsNaN(entryPrice) ? 0 : (tradeDirection == "LONG"
                            ? (extremePriceSinceTP1 - entryPrice) / (TickSize * 4)
                            : (entryPrice - extremePriceSinceTP1) / (TickSize * 4));
                        LogThink(string.Format("Em posição {0} | qty={1} | {2} | stop={3:F2} | favorável={4:F1} pts | CHE={5}",
                            tradeDirection, qty, fase, currentStopPrice, pts, bullish ? "bullish" : "bearish"));
                    }

                    UpdateManagedProtectiveStop();

                    bool isFlipAgainst = false;
                    if (tradeDirection == "LONG" && !bullish && isFlip)
                        isFlipAgainst = true;
                    else if (tradeDirection == "SHORT" && bullish && isFlip)
                        isFlipAgainst = true;

                    if (isFlipAgainst)
                    {
                        if (!flipAgainstPending)
                        {
                            flipAgainstPending = true;
                            barsSinceFlipAgainst = 0;
                            LogThink(string.Format("CHE deu flip contra a posição (agora {0}). Contando barras de confirmação (preciso {1} para sair).",
                                bullish ? "bullish" : "bearish", FlipConfirmBars));
                        }
                    }

                    if (flipAgainstPending && IsFirstTickOfBar)
                        barsSinceFlipAgainst++;

                    if (flipAgainstPending)
                    {
                        bool backInFavor = (tradeDirection == "LONG" && bullish) ||
                                           (tradeDirection == "SHORT" && !bullish);
                        if (backInFavor)
                        {
                            LogThink("CHE voltou a favor da posição. Mantenho posição, cancelo alerta de saída por flip.");
                            flipAgainstPending = false;
                            barsSinceFlipAgainst = 0;
                        }
                        else if (barsSinceFlipAgainst >= FlipConfirmBars)
                        {
                            LogThink(string.Format("Decisão: CHE contra posição há {0} barras (>= {1}) → saindo por FLIP.", barsSinceFlipAgainst, FlipConfirmBars));
                            Log(string.Format("Flip contra posição: saindo {0} após {1} barras", tradeDirection, barsSinceFlipAgainst));
                            CancelAllOrders();
                            if (Position.MarketPosition == MarketPosition.Long)
                                ExitLong("FLIP", "");
                            else
                                ExitShort("FLIP", "");
                            return;
                        }
                    }
                }
                else
                {
                    flipAgainstPending = false;
                    barsSinceFlipAgainst = 0;

                    // Log periódico quando flat: mostra que a estratégia está viva e qual a direção do CHE (flips podem ser raros)
                    if (EnableLogs && IsFirstTickOfBar)
                    {
                        _flatLogBarCount++;
                        if (_flatLogBarCount % 20 == 1)
                            LogThink(string.Format("Flat. CHE direção atual: {0}. Aguardando flip para entrar (isFlip={1}).", bullish ? "LONG" : "SHORT", isFlip));
                    }

                    if (EnableDailyLimits && dailyLimitReached)
                    {
                        if (!_loggedDailyLimit)
                        {
                            _loggedDailyLimit = true;
                            LogThink(string.Format("Limite diário atingido (P&L={0:F2}). Não entro em novas operações até o próximo dia.", dailyPnL));
                            Log(string.Format("Limite diário atingido. P&L diário={0:F2}", dailyPnL));
                        }
                        pendingEntry = false;
                        return;
                    }

                    if (EnableTrailingEntry && pendingEntry)
                    {
                        if (CancelPendingOnOppositeSignal)
                        {
                            bool pendingInvalidated = (pendingDirection == "LONG" && !bullish) ||
                                                     (pendingDirection == "SHORT" && bullish);
                            if (pendingInvalidated)
                            {
                                LogThink(string.Format("CHE virou {0}. Cancelando pending {1} (sinal oposto).", bullish ? "bullish" : "bearish", pendingDirection));
                                Log(string.Format("Pending {0} cancelado (sinal oposto)", pendingDirection));
                                pendingEntry = false;
                                pendingDirection = "";
                                barsSinceFlip = 0;
                            }
                        }

                        if (!pendingEntry) { }
                        else
                        {
                            if (IsFirstTickOfBar)
                                barsSinceFlip++;
                            if (barsSinceFlip > MaxEntryBars)
                            {
                                LogThink(string.Format("Pending expirado: {0} barras sem confirmação (máx {1}). Cancelando.", barsSinceFlip, MaxEntryBars));
                                pendingEntry = false;
                                pendingDirection = "";
                                return;
                            }
                            double currentPrice = Close[0];
                            bool confirmed = false;
                            if (pendingDirection == "LONG" && currentPrice >= flipPrice + EntryConfirmPoints)
                                confirmed = true;
                            else if (pendingDirection == "SHORT" && currentPrice <= flipPrice - EntryConfirmPoints)
                                confirmed = true;
                            if (IsFirstTickOfBar && pendingEntry)
                            {
                                double need = pendingDirection == "LONG" ? flipPrice + EntryConfirmPoints : flipPrice - EntryConfirmPoints;
                                LogThink(string.Format("Pending {0}: barra {1}/{2}. Preço precisa {3} {4:F2} (Close={5:F2}).",
                                    pendingDirection, barsSinceFlip, MaxEntryBars, pendingDirection == "LONG" ? ">=" : "<=", need, currentPrice));
                            }
                            if (confirmed)
                            {
                                string entryDir = pendingDirection;
                                ResetTradeState();
                                tradeDirection = entryDir;
                                LogThink(string.Format("Preço confirmou {0} (Close={1:F2} {2} flipPrice±conf). Entrando.", entryDir, currentPrice, pendingDirection == "LONG" ? ">=" : "<="));
                                Log(string.Format("Entrada (trailing): {0} confirmada @ Close={1:F2}", entryDir, currentPrice));
                                if (tradeDirection == "LONG")
                                {
                                    if (ShowActualEntryMarkers) DrawEntryMarker(true);
                                    EnterLong(3, "L");
                                }
                                else
                                {
                                    if (ShowActualEntryMarkers) DrawEntryMarker(false);
                                    EnterShort(3, "S");
                                }
                            }
                            return;
                        }
                    }

                    // Processar flip: isFlip é setado no BIP=1 (5 min); pode não estar visível aqui na mesma "barra" por causa da ordem de execução. Por isso usamos _flipJustOccurred quando o flip foi detectado no HTF.
                    bool flipToProcess = (isFlip || _flipJustOccurred) && IsFirstTickOfBar;
                    bool entryLong = _flipJustOccurred ? _flipDirectionLong : bullish;
                    if (_flipJustOccurred && flipToProcess)
                        _flipJustOccurred = false;

                    if (flipToProcess)
                    {
                        if (ShowFlipMarkers) DrawFlipMarker();
                        Log(string.Format("CHE Flip: {0} (Close={1:F2})", entryLong ? "LONG" : "SHORT", Close[0]));
                        LogThink(string.Format("Avaliando entrada: direção={0}, horário={1}, modo={2}.",
                            entryLong ? "LONG" : "SHORT", EnableTimeFilter ? (IsWithinTradingHours() ? "OK" : "fora") : "desligado", EnableTrailingEntry ? "trailing" : "imediata"));

                        if (EnableTimeFilter && !IsWithinTradingHours())
                        {
                            LogThink(string.Format("Ignorando: fora do horário (Hora={0}, permitido {1}-{2}, almoço {3}-{4}).", Time[0].Hour, TradingStartHour, TradingEndHour, AvoidLunchStart, AvoidLunchEnd));
                            return;
                        }
                        if (EnableTrailingEntry)
                        {
                            pendingEntry = true;
                            pendingDirection = entryLong ? "LONG" : "SHORT";
                            flipPrice = Close[0];
                            barsSinceFlip = 0;
                            LogThink(string.Format("Modo trailing: aguardando preço confirmar {0} (preço precisa ir {1} de {2:F2}, confirmação={3} pts).", pendingDirection, pendingDirection == "LONG" ? "acima" : "abaixo", flipPrice, EntryConfirmPoints));
                            Log(string.Format("Pending entry: {0} @ flipPrice={1:F2}", pendingDirection, flipPrice));
                            return;
                        }
                        ResetTradeState();
                        LogThink("Entrada imediata: enviando ordem de mercado.");
                        if (entryLong)
                        {
                            tradeDirection = "LONG";
                            Log(string.Format("Sinal LONG (entrada imediata)"));
                            if (ShowActualEntryMarkers) DrawEntryMarker(true);
                            EnterLong(3, "L");
                        }
                        else
                        {
                            tradeDirection = "SHORT";
                            Log(string.Format("Sinal SHORT (entrada imediata)"));
                            if (ShowActualEntryMarkers) DrawEntryMarker(false);
                            EnterShort(3, "S");
                        }
                    }
                }
            }
        }

        private bool IsWithinTradingHours()
        {
            int hour = Time[0].Hour;
            if (hour < TradingStartHour || hour >= TradingEndHour)
                return false;
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

            double desiredStop;

            // TP3 runner (1 contrato após TP1+TP2): saída estilo Teste21c
            if (tp1Filled && tp2Filled && qty == 1)
            {
                desiredStop = ComputeTP3DesiredStopLikeTeste21c(direction);
                // Limite: stop do TP3 não pode ficar a mais de X pontos do Close
                if (MaxTP3StopDistanceFromClosePoints > 0)
                {
                    double maxDist = MaxTP3StopDistanceFromClosePoints * pointValueInPrice;
                    double closeRef = Close[0];
                    if (direction == MarketPosition.Long)
                    {
                        double floorStop = closeRef - maxDist;
                        if (desiredStop < floorStop)
                            desiredStop = Instrument.MasterInstrument.RoundToTickSize(floorStop);
                    }
                    else
                    {
                        double ceilingStop = closeRef + maxDist;
                        if (desiredStop > ceilingStop)
                            desiredStop = Instrument.MasterInstrument.RoundToTickSize(ceilingStop);
                    }
                }
                // Monotonic: só melhorar
                if (!double.IsNaN(currentStopPrice))
                {
                    if (direction == MarketPosition.Long)
                        desiredStop = Math.Max(desiredStop, currentStopPrice);
                    else
                        desiredStop = Math.Min(desiredStop, currentStopPrice);
                }
            }
            else
            {
                desiredStop = direction == MarketPosition.Long
                    ? entryPrice - initialStopDistance
                    : entryPrice + initialStopDistance;

                if (breakevenArmed)
                {
                    if (EnableTrailingStop)
                    {
                        desiredStop = direction == MarketPosition.Long
                            ? extremePriceSinceTP1 - trailDistance
                            : extremePriceSinceTP1 + trailDistance;
                    }
                    else
                        desiredStop = entryPrice;
                    if (direction == MarketPosition.Long && desiredStop < entryPrice)
                        desiredStop = entryPrice;
                    if (direction == MarketPosition.Short && desiredStop > entryPrice)
                        desiredStop = entryPrice;
                }

                if (!double.IsNaN(currentStopPrice))
                {
                    if (direction == MarketPosition.Long)
                        desiredStop = Math.Max(desiredStop, currentStopPrice);
                    else
                        desiredStop = Math.Min(desiredStop, currentStopPrice);
                }
            }

            bool missingStop = !IsOrderActive(stopOrder);
            bool qtyMismatch = stopOrder != null && IsOrderActive(stopOrder) && stopOrder.Quantity != qty;
            double minImprove = TickSize;
            if (tp1Filled && tp2Filled && qty == 1 && UseSwingStopTP3)
                minImprove = Math.Max(TickSize, MinStopImproveTicksTP3 * TickSize);
            bool betterPrice = double.IsNaN(currentStopPrice)
                || (direction == MarketPosition.Long && desiredStop > currentStopPrice + minImprove)
                || (direction == MarketPosition.Short && desiredStop < currentStopPrice - minImprove);

            if (missingStop || qtyMismatch || betterPrice)
                UpsertProtectiveStop(direction, qty, desiredStop);
        }

        /// <summary>
        /// Calcula o stop desejado para o TP3 (runner) com a mesma lógica do Teste21c:
        /// stop fixo em ticks ou Swing+ATR (atualiza quando melhora).
        /// </summary>
        private double ComputeTP3DesiredStopLikeTeste21c(MarketPosition direction)
        {
            double referencePrice = Close[0];

            if (!UseSwingStopTP3)
            {
                if (_tp3Teste21cFixedPlaced && !double.IsNaN(currentStopPrice))
                    return currentStopPrice;
                int effectiveTicks = Math.Max(1, StopLossTicksTP3);
                if (MaxStopLossTicksTP3 > 0)
                    effectiveTicks = Math.Min(effectiveTicks, MaxStopLossTicksTP3);
                double stopPrice = direction == MarketPosition.Long
                    ? entryPrice - effectiveTicks * TickSize
                    : entryPrice + effectiveTicks * TickSize;
                _tp3Teste21cFixedPlaced = true;
                return Instrument.MasterInstrument.RoundToTickSize(stopPrice);
            }

            // Swing+ATR (igual Teste21c)
            double atrVal = SafeAtrTP3();
            double offset = Math.Max(0, SwingAtrOffsetMultiplierTP3) * atrVal;
            double fallbackDist = Math.Max(0.5, FallbackAtrMultiplierTP3) * atrVal;

            double swingLow = _swingTP3 != null ? _swingTP3.SwingLow[0] : 0.0;
            double swingHigh = _swingTP3 != null ? _swingTP3.SwingHigh[0] : 0.0;

            if (direction == MarketPosition.Long)
            {
                double candidate = swingLow - offset;
                bool valid = swingLow > 0 && candidate < Close[0];
                if (valid)
                    return Instrument.MasterInstrument.RoundToTickSize(candidate);
                if (!double.IsNaN(currentStopPrice) && currentStopPrice != 0)
                    return Instrument.MasterInstrument.RoundToTickSize(currentStopPrice);
                return Instrument.MasterInstrument.RoundToTickSize(referencePrice - fallbackDist);
            }

            if (direction == MarketPosition.Short)
            {
                double candidate = swingHigh + offset;
                bool valid = swingHigh > 0 && candidate > Close[0];
                if (valid)
                    return Instrument.MasterInstrument.RoundToTickSize(candidate);
                if (!double.IsNaN(currentStopPrice) && currentStopPrice != 0)
                    return Instrument.MasterInstrument.RoundToTickSize(currentStopPrice);
                return Instrument.MasterInstrument.RoundToTickSize(referencePrice + fallbackDist);
            }

            return referencePrice;
        }

        private double SafeAtrTP3()
        {
            double atrVal = _atrTP3 != null ? _atrTP3[0] : (TickSize * 10);
            if (double.IsNaN(atrVal) || double.IsInfinity(atrVal) || atrVal <= 0)
                atrVal = TickSize * 10;
            return Math.Max(TickSize, atrVal);
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null) return;
            string name = execution.Order.Name ?? "";

            int ticksPerPt = 4;

            if (name == "L" && execution.Order.OrderState == OrderState.Filled)
            {
                entryPrice = execution.Order.AverageFillPrice;
                tradeDirection = "LONG";
                double stopPrice = entryPrice - (StopPoints * TickSize * ticksPerPt);
                double tp1Price = entryPrice + (TP1Points * TickSize * ticksPerPt);
                double tp2Price = entryPrice + (TP2Points * TickSize * ticksPerPt);

                Log(string.Format("ENTRADA LONG preenchida @ {0:F2} | Stop={1:F2} TP1={2:F2} TP2={3:F2}", entryPrice, stopPrice, tp1Price, tp2Price));
                LogThink(string.Format("Plano: stop inicial {0:F2}, TP1 em {1:F2}, TP2 em {2:F2}. Após TP1+TP2 o runner (TP3) sai só por stop (breakeven aos {3} pts, depois trailing ou Swing+ATR).", stopPrice, tp1Price, tp2Price, BreakevenTrigger));
                stopOrder = ExitLongStopMarket(0, true, 3, stopPrice, "STOP", "L");
                currentStopPrice = stopPrice;
                extremePriceSinceTP1 = entryPrice;
                maxFavorableExcursion = 0;
                movedToBreakeven = false;
                tp1Filled = false;
                tp2Filled = false;
                _tp3Teste21cFixedPlaced = false;
                tp1Order = ExitLongLimit(0, true, 1, tp1Price, "TP1", "L");
                tp2Order = ExitLongLimit(0, true, 1, tp2Price, "TP2", "L");
                // TP3: sem limit; saída apenas pelo stop (lógica Teste21c no UpdateManagedProtectiveStop)
            }
            else if (name == "S" && execution.Order.OrderState == OrderState.Filled)
            {
                entryPrice = execution.Order.AverageFillPrice;
                tradeDirection = "SHORT";
                double stopPrice = entryPrice + (StopPoints * TickSize * ticksPerPt);
                double tp1Price = entryPrice - (TP1Points * TickSize * ticksPerPt);
                double tp2Price = entryPrice - (TP2Points * TickSize * ticksPerPt);

                Log(string.Format("ENTRADA SHORT preenchida @ {0:F2} | Stop={1:F2} TP1={2:F2} TP2={3:F2}", entryPrice, stopPrice, tp1Price, tp2Price));
                LogThink(string.Format("Plano: stop inicial {0:F2}, TP1 em {1:F2}, TP2 em {2:F2}. Após TP1+TP2 o runner (TP3) sai só por stop (breakeven aos {3} pts, depois trailing ou Swing+ATR).", stopPrice, tp1Price, tp2Price, BreakevenTrigger));
                stopOrder = ExitShortStopMarket(0, true, 3, stopPrice, "STOP", "S");
                currentStopPrice = stopPrice;
                extremePriceSinceTP1 = entryPrice;
                maxFavorableExcursion = 0;
                movedToBreakeven = false;
                tp1Filled = false;
                tp2Filled = false;
                _tp3Teste21cFixedPlaced = false;
                tp1Order = ExitShortLimit(0, true, 1, tp1Price, "TP1", "S");
                tp2Order = ExitShortLimit(0, true, 1, tp2Price, "TP2", "S");
                // TP3: sem limit; saída pelo stop (lógica Teste21c)
            }
            else if (name == "TP1" && execution.Order.OrderState == OrderState.Filled && !tp1Filled)
            {
                tp1Filled = true;
                Log(string.Format("TP1 preenchido @ {0:F2} | Posição: {1}", price, tradeDirection));
                LogThink("1º alvo atingido. Restam 2 contratos; stop segue na lógica (breakeven/trailing).");
                UpdateManagedProtectiveStop();
            }
            else if (name == "TP2" && execution.Order.OrderState == OrderState.Filled && !tp2Filled)
            {
                tp2Filled = true;
                Log(string.Format("TP2 preenchido @ {0:F2} | Runner (TP3) ativo", price));
                LogThink(string.Format("Runner TP3 ativo: 1 contrato. Saída só por stop ({0}).", UseSwingStopTP3 ? "Swing+ATR, melhora quando possível" : "fixo em ticks"));
                UpdateManagedProtectiveStop();
            }

            if ((name == "STOP" || name == "FLIP") && execution.Order.OrderState == OrderState.Filled)
            {
                LogThink(string.Format("Posição encerrada por {0}. Fico flat, aguardando próximo flip do CHE.", name));
                Log(string.Format("Saída por {0} @ {1:F2} | {2} qty={3}", name, price, tradeDirection, quantity));
            }

            if (EnableDailyLimits && execution.Order.OrderState == OrderState.Filled)
            {
                bool isExit = name == "TP1" || name == "TP2" || name == "TP3" || name == "STOP" || name == "BE" || name == "TRAIL" || name == "FLIP"
                              || name.Contains("close") || name.Contains("Close");
                if (isExit && !double.IsNaN(entryPrice))
                {
                    double pointValue = 2.0;
                    double pnlPoints = 0;
                    if (tradeDirection == "LONG")
                        pnlPoints = price - entryPrice;
                    else if (tradeDirection == "SHORT")
                        pnlPoints = entryPrice - price;
                    double tradePnL = pnlPoints * quantity * pointValue;
                    dailyPnL += tradePnL;
                    if (dailyPnL >= DailyProfitTarget || dailyPnL <= -DailyLossLimit)
                        dailyLimitReached = true;
                }
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity,
            int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            if (order.Name == "STOP" || order.Name == "BE" || order.Name == "TRAIL")
                stopOrder = order;
            else if (order.Name == "TP1")
                tp1Order = order;
            else if (order.Name == "TP2")
                tp2Order = order;
        }

        private void CancelAllOrders()
        {
            if (IsOrderActive(stopOrder))
                CancelOrder(stopOrder);
            if (IsOrderActive(tp1Order))
                CancelOrder(tp1Order);
            if (IsOrderActive(tp2Order))
                CancelOrder(tp2Order);
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
                if (movedToBreakeven && !_loggedBreakeven && !double.IsNaN(entryPrice) && Math.Abs(stopPrice - entryPrice) < TickSize * 2)
                {
                    _loggedBreakeven = true;
                    LogThink(string.Format("Preço favorável atingiu trigger de breakeven ({0} pts). Movendo stop para entrada {1:F2} para travar risco zero.", BreakevenTrigger, stopPrice));
                    Log(string.Format("Stop movido para breakeven @ {0:F2}", stopPrice));
                }
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
            if (isFlip)
            {
                _flipJustOccurred = true;
                _flipDirectionLong = bullish;
            }
            priorBullish = bullish;
        }

        private void DrawFlipMarker()
        {
            string tag = "CHE_FLIP_" + CurrentBar;
            if (bullish)
            {
                double y = Low[0] - (2 * TickSize);
                Draw.TriangleUp(this, tag, false, 0, y, Green);
            }
            else
            {
                double y = High[0] + (2 * TickSize);
                Draw.TriangleDown(this, tag, false, 0, y, Red);
            }
        }

        private void DrawEntryMarker(bool isLong)
        {
            string tag = "CHE_ENTRY_" + CurrentBar + "_" + Time[0].Ticks;
            if (isLong)
            {
                double y = Low[0] - (4 * TickSize);
                Draw.ArrowUp(this, tag, false, 0, y, EntryLong);
            }
            else
            {
                double y = High[0] + (4 * TickSize);
                Draw.ArrowDown(this, tag, false, 0, y, EntryShort);
            }
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
        [Range(1, 400)]
        [Display(Name = "TP1 (points)", Order = 0, GroupName = "2. Risk")]
        public int TP1Points { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "TP2 (points)", Order = 1, GroupName = "2. Risk")]
        public int TP2Points { get; set; }

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
        [Display(Name = "Entry Confirm Points", Order = 9, GroupName = "3. Filters")]
        public double EntryConfirmPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Entry Wait Bars", Order = 10, GroupName = "3. Filters")]
        public int MaxEntryBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cancel Pending on Opposite Signal", Order = 11, GroupName = "3. Filters")]
        public bool CancelPendingOnOppositeSignal { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Daily Limits", Order = 12, GroupName = "3. Filters")]
        public bool EnableDailyLimits { get; set; }

        [NinjaScriptProperty]
        [Range(50, 2000)]
        [Display(Name = "Daily Profit Target ($)", Order = 13, GroupName = "3. Filters")]
        public double DailyProfitTarget { get; set; }

        [NinjaScriptProperty]
        [Range(50, 2000)]
        [Display(Name = "Daily Loss Limit ($)", Order = 14, GroupName = "3. Filters")]
        public double DailyLossLimit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable State Bar Coloring", Order = 0, GroupName = "4. Visual")]
        public bool EnableStateBarColoring { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Highlight Pending Entry", Order = 1, GroupName = "4. Visual")]
        public bool HighlightPendingEntry { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Flip Markers", Order = 2, GroupName = "4. Visual")]
        public bool ShowFlipMarkers { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Actual Entry Markers", Order = 3, GroupName = "4. Visual")]
        public bool ShowActualEntryMarkers { get; set; }

        // --- TP3 exit (estilo Teste21c) ---
        [NinjaScriptProperty]
        [Display(Name = "Use Swing Stop for TP3", Description = "TP3 runner: true = Swing+ATR, false = stop fixo em ticks", Order = 0, GroupName = "5. TP3 Exit (Teste21c)")]
        public bool UseSwingStopTP3 { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Swing Strength (TP3)", Order = 1, GroupName = "5. TP3 Exit (Teste21c)")]
        public int SwingStrengthTP3 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Swing ATR Offset Multiplier (TP3)", Order = 2, GroupName = "5. TP3 Exit (Teste21c)")]
        public double SwingAtrOffsetMultiplierTP3 { get; set; }

        [NinjaScriptProperty]
        [Range(-10.1, 10.0)]
        [Display(Name = "Fallback ATR Multiplier (TP3)", Order = 3, GroupName = "5. TP3 Exit (Teste21c)")]
        public double FallbackAtrMultiplierTP3 { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Min Stop Improve Ticks (TP3)", Order = 4, GroupName = "5. TP3 Exit (Teste21c)")]
        public int MinStopImproveTicksTP3 { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Stop Loss Ticks (TP3 fixo)", Order = 5, GroupName = "5. TP3 Exit (Teste21c)")]
        public int StopLossTicksTP3 { get; set; }

        [NinjaScriptProperty]
        [Range(0, 50000)]
        [Display(Name = "Max Stop Loss Ticks (TP3)", Order = 6, GroupName = "5. TP3 Exit (Teste21c)")]
        public int MaxStopLossTicksTP3 { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ATR Period (TP3)", Order = 7, GroupName = "5. TP3 Exit (Teste21c)")]
        public int AtrPeriodTP3 { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max TP3 Stop Distance from Close (points)", Description = "0 = sem limite. Stop do TP3 não pode ficar a mais do que X pontos do Close.", Order = 8, GroupName = "5. TP3 Exit (Teste21c)")]
        public int MaxTP3StopDistanceFromClosePoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Logs (Output window)", Description = "Ativa mensagens de log na janela Output para acompanhar o funcionamento da estratégia.", Order = 0, GroupName = "6. Debug")]
        public bool EnableLogs { get; set; }
        #endregion
    }
}
