//
// Teste21b - Mesma lógica de entrada do Teste21a, saída igual ao StopLoss (Swing+ATR ou stop fixo em ticks).
//
#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	/// <summary>
	/// Estratégia automatizada que combina múltiplos indicadores (igual Teste21a) para entrada.
	/// Saída igual ao StopLoss: stop market com Swing+ATR ou stop fixo em ticks (sem profit target, sem ATM).
	/// </summary>
	public class Teste21b : Strategy
	{
		#region Indicadores - Trend
		private EMA emaFast;
		private EMA emaSlow;
		private ADX adx;
		#endregion

		#region Indicadores - Momentum
		private RSI rsi;
		private MACD macd;
		private StochasticsFast stochastics;
		#endregion

		#region Indicadores - Volatilidade
		private ATR atr;
		private Bollinger bollinger;
		#endregion

		#region Indicadores - Volume
		private OBV obv;
		private MFI mfi;
		private SMA volumeSMA;
		#endregion

		#region GlitchIndicator (opcional)
		private GlitchIndicator glitchIndicator;
		private bool useGlitchIndicator;
		#endregion

		#region Series e Estado
		private Series<double> longScoreSeries;
		private Series<double> shortScoreSeries;
		private Series<double> trendScoreSeries;
		private Series<double> momentumScoreSeries;
		private Series<double> volatilityScoreSeries;
		private Series<double> volumeScoreSeries;
		private Series<double> confirmationScoreSeries;
		#endregion

		#region Variáveis de Estado
		private double lastLongScore = 0;
		private double lastShortScore = 0;
		private int lastEntryBar = -1;
		private double entryPrice = 0;
		#endregion

		#region Variáveis de Estado - Saída (estilo StopLoss)
		private Swing _swing;
		private Order _stopOrder;
		private double _currentStopPrice;
		private bool _hasStopPrice;
		private bool _fixedStopPlaced;
		private string currentSignalName = ""; // Nome do sinal de entrada para vincular a saída
		private const string StopSignalName = "StopLoss";
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Teste21a com saída igual ao StopLoss (Swing+ATR ou stop fixo em ticks).";
				Name = "Teste21b";
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
				TimeInForce = Cbi.TimeInForce.Gtc;
				TraceOrders = false;
				RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
				BarsRequiredToTrade = 50;
				IsInstantiatedOnEachOptimizationIteration = true;
				IncludeCommission 							= true;


				// Parâmetros - Trend
				EmaFastPeriod = 7;
				EmaSlowPeriod = 23;
				AdxPeriod = 14;
				AdxTrendThreshold = 43.0;

				// Parâmetros - Momentum
				RsiPeriod = 14;
				RsiOversoldLong = 30;
				RsiOverboughtLong = 70;
				RsiOversoldShort = 10;
				RsiOverboughtShort = 30;
				RsiExitOverboughtLong = 30;
				RsiExitOversoldShort = 70;
				MacdFast = 23;
				MacdSlow = 27;
				MacdSmooth = 8;
				StochKPeriod = 14;
				StochDPeriod = 5;
				StochOversoldLong = 60;
				StochOverboughtLong = 65;
				StochOversoldShort = 65;
				StochOverboughtShort = 0;

				// Parâmetros - Volatilidade
				AtrPeriod = 14;
				AtrMultiplierStopLong = 5.0;
				AtrMultiplierTargetLong = 5.0;
				AtrMultiplierStopShort = 2.0;
				AtrMultiplierTargetShort = 10.0;
				BollingerPeriod = 13;
				BollingerStdDev = 1.0;

				// Parâmetros - Volume
				MfiPeriod = 14;
				MfiOversoldLong = 33;
				MfiOverboughtLong = 70;
				MfiOversoldShort = 10;
				MfiOverboughtShort = 55;
				MinVolumeMultiplier = 1.0;

				// Parâmetros - Scoring e Filtros
				ScoreThresholdLong = 65.0;
				ScoreThresholdShort = 80.0;
				MinConfirmationSignalsLong = 3;
				MinConfirmationSignalsShort = 5;
				UseGlitchIndicator = true;
				GlitchMinScore = 85.0;

				// Parâmetros - Risco
				UseAtrBasedStops = true;
				FixedStopTicksLong = 35;
				FixedTargetTicksLong = 80;
				FixedStopTicksShort = 90;
				FixedTargetTicksShort = 20;
				MaxDailyLoss = 200.0;
				MaxPositionSize = 1;

				// Parâmetros - Filtros de Tempo
				EnableTimeFilter = false;
				StartTime = 93000;  // 9:30
				EndTime = 160000;   // 16:00

				// Parâmetros - Cooldown
				EnableCooldown = true;
				CooldownBarsLong = 6;
				CooldownBarsShort = 6;

				// Parâmetros - Saída (igual StopLoss)
				EnableDecisionLog = false;
				UseSwingStop = true;
				SwingStrength = 5;
				SwingAtrOffsetMultiplier = 0;
				FallbackAtrMultiplier = 0;
				MinStopImproveTicks = 1;
				StopLossTicks = 50;
				MaxStopLossTicks = 750;  // 0 = sem limite; >0 limita a distância máxima do stop em ticks (otimizável)

				// Cores
				AddPlot(Brushes.LimeGreen, "Long Score");
				AddPlot(Brushes.OrangeRed, "Short Score");
				Plots[0].PlotStyle = PlotStyle.Bar;
				Plots[1].PlotStyle = PlotStyle.Bar;
				Plots[0].Width = 2;
				Plots[1].Width = 2;
			}
			else if (State == State.Configure)
			{
				// Stops/targets são configurados dinamicamente após entrada
			}
			else if (State == State.DataLoaded)
			{
				// Inicializar indicadores - Trend
				emaFast = EMA(EmaFastPeriod);
				emaSlow = EMA(EmaSlowPeriod);
				adx = ADX(AdxPeriod);

				// Inicializar indicadores - Momentum
				rsi = RSI(RsiPeriod, 1);
				macd = MACD(MacdFast, MacdSlow, MacdSmooth);
				stochastics = StochasticsFast(StochDPeriod, StochKPeriod);

				// Inicializar indicadores - Volatilidade
				atr = ATR(AtrPeriod);
				bollinger = Bollinger(BollingerStdDev, BollingerPeriod);

				// Inicializar indicadores - Volume
				obv = OBV();
				mfi = MFI(MfiPeriod);
				volumeSMA = SMA(Volume, 20);

				// Inicializar Swing para saída estilo StopLoss
				_swing = Swing(Math.Max(1, SwingStrength));
				ResetState();

				// Tentar inicializar GlitchIndicator (opcional)
				if (UseGlitchIndicator)
				{
					try
					{
						glitchIndicator = GlitchIndicator(true, 2, true, true, 25, 25, 10, false, 0, false, 25, false, 15, 12, 67, 0.20, 40, true, true, 0, 0.13, 1.10, 4.0, 6.0, 0.28, 3, 0.10, 0.12, 23.0, 15.0, 20, 1.0, 1.0, 0.5, 1.5, 8, 21, false, "");
						useGlitchIndicator = glitchIndicator != null;
					}
					catch
					{
						useGlitchIndicator = false;
						Print("GlitchIndicator não disponível, continuando sem ele");
					}
				}
				else
				{
					useGlitchIndicator = false;
				}

				// Inicializar series
				longScoreSeries = new Series<double>(this);
				shortScoreSeries = new Series<double>(this);
				trendScoreSeries = new Series<double>(this);
				momentumScoreSeries = new Series<double>(this);
				volatilityScoreSeries = new Series<double>(this);
				volumeScoreSeries = new Series<double>(this);
				confirmationScoreSeries = new Series<double>(this);

				// Adicionar indicadores ao gráfico
				AddChartIndicator(emaFast);
				AddChartIndicator(emaSlow);
				AddChartIndicator(adx);
				AddChartIndicator(rsi);
				AddChartIndicator(macd);
				AddChartIndicator(bollinger);

				// Cores
				emaFast.Plots[0].Brush = Brushes.Gold;
				emaSlow.Plots[0].Brush = Brushes.DodgerBlue;
				adx.Plots[0].Brush = Brushes.Purple;
				rsi.Plots[0].Brush = Brushes.Yellow;
			}
			else if (State == State.Terminated)
			{
				_stopOrder = null;
			}
		}

		protected override void OnBarUpdate()
		{
			// Verificar se há barras suficientes
			if (CurrentBar < BarsRequiredToTrade)
				return;

			int currentTime = ToTime(Time[0]);

			// Sair de todas as trades às 16:50
			if (currentTime >= 165000) // 16:50:00
			{
				if (Position.MarketPosition == Cbi.MarketPosition.Long)
					ExitLong("End of day 16:50");
				else if (Position.MarketPosition == Cbi.MarketPosition.Short)
					ExitShort("End of day 16:50");
			}

			// Verificar filtro de tempo
			if (EnableTimeFilter)
			{
				if (currentTime < StartTime || currentTime > EndTime)
				{
					if (Position.MarketPosition == Cbi.MarketPosition.Long)
						ExitLong("Time filter");
					else if (Position.MarketPosition == Cbi.MarketPosition.Short)
						ExitShort("Time filter");
					return;
				}
			}

			// Verificar perda diária máxima
			if (CheckMaxDailyLoss())
			{
				if (Position.MarketPosition == Cbi.MarketPosition.Long)
					ExitLong("Max daily loss");
				else if (Position.MarketPosition == Cbi.MarketPosition.Short)
					ExitShort("Max daily loss");
				return;
			}

			// Calcular scores
			CalculateScores();

			// Atualizar plots
			Values[0][0] = longScoreSeries[0];
			Values[1][0] = shortScoreSeries[0];

			// Lógica de entrada/saída
			if (Position.MarketPosition == Cbi.MarketPosition.Flat)
			{
				ResetState();
				currentSignalName = "";
				CheckEntryConditions();
			}
			else
			{
				ManageStopLossStyle();
				if (EnableDecisionLog)
				{
					bool hasStop = HasActiveStopLoss();
					string posSide = Position.MarketPosition == Cbi.MarketPosition.Long ? "Long" : "Short";
					if (hasStop)
						Print(string.Format("[Teste21b] {0} | Stop ativo em {1}.", posSide, _currentStopPrice));
					else
						Print(string.Format("[Teste21b] {0} | Sem stop ativo.", posSide));
				}
			}

			lastLongScore = longScoreSeries[0];
			lastShortScore = shortScoreSeries[0];
		}

		/// <summary>
		/// Calcula os scores para Long e Short baseado em múltiplos indicadores
		/// </summary>
		private void CalculateScores()
		{
			// Inicializar scores
			double trendScoreLong = 0, trendScoreShort = 0;
			double momentumScoreLong = 0, momentumScoreShort = 0;
			double volatilityScoreLong = 0, volatilityScoreShort = 0;
			double volumeScoreLong = 0, volumeScoreShort = 0;
			double confirmationScoreLong = 0, confirmationScoreShort = 0;

			// === TREND SCORE (30%) ===
			bool emaBullish = emaFast[0] > emaSlow[0];
			bool emaBearish = emaFast[0] < emaSlow[0];
			double adxValue = adx[0];
			bool strongTrend = adxValue > AdxTrendThreshold;

			if (emaBullish && strongTrend)
				trendScoreLong = 100.0;
			else if (emaBullish)
				trendScoreLong = 60.0;
			else if (emaFast[0] > emaSlow[1] && emaFast[1] <= emaSlow[1]) // Crossover recente
				trendScoreLong = 70.0;

			if (emaBearish && strongTrend)
				trendScoreShort = 100.0;
			else if (emaBearish)
				trendScoreShort = 60.0;
			else if (emaFast[0] < emaSlow[1] && emaFast[1] >= emaSlow[1]) // Crossover recente
				trendScoreShort = 70.0;

			trendScoreSeries[0] = emaBullish ? trendScoreLong : (emaBearish ? -trendScoreShort : 0);

			// === MOMENTUM SCORE (25%) ===
			double rsiValue = rsi[0];
			double macdMain = macd[0];
			double macdSignal = macd.Avg[0];
			double macdHistogram = macdMain - macdSignal;
			double stochK = stochastics.K[0];
			double stochD = stochastics.D[0];

			// RSI
			if (rsiValue > 50 && rsiValue < RsiOverboughtLong)
				momentumScoreLong += 30.0;
			else if (rsiValue < 50 && rsiValue > RsiOversoldShort)
				momentumScoreShort += 30.0;

			if (rsiValue < RsiOversoldLong && rsiValue > rsi[1]) // RSI subindo de oversold
				momentumScoreLong += 20.0;
			else if (rsiValue > RsiOverboughtShort && rsiValue < rsi[1]) // RSI caindo de overbought
				momentumScoreShort += 20.0;

			// MACD
			if (macdHistogram > 0 && macdMain > macdSignal)
				momentumScoreLong += 25.0;
			else if (macdHistogram < 0 && macdMain < macdSignal)
				momentumScoreShort += 25.0;

			if (macdHistogram > macd[1] - macd.Avg[1]) // Histogram crescendo
				momentumScoreLong += 15.0;
			else if (macdHistogram < macd[1] - macd.Avg[1]) // Histogram diminuindo
				momentumScoreShort += 15.0;

			// Stochastic
			if (stochK > stochD && stochK < StochOverboughtLong)
				momentumScoreLong += 20.0;
			else if (stochK < stochD && stochK > StochOversoldShort)
				momentumScoreShort += 20.0;

			if (stochK < StochOversoldLong && stochK > stochastics.K[1]) // Stoch subindo de oversold
				momentumScoreLong += 10.0;
			else if (stochK > StochOverboughtShort && stochK < stochastics.K[1]) // Stoch caindo de overbought
				momentumScoreShort += 10.0;

			momentumScoreLong = Math.Min(100.0, momentumScoreLong);
			momentumScoreShort = Math.Min(100.0, momentumScoreShort);
			momentumScoreSeries[0] = momentumScoreLong > momentumScoreShort ? momentumScoreLong : -momentumScoreShort;

			// === VOLATILIDADE SCORE (20%) ===
			double atrValue = atr[0];
			double bbUpper = bollinger.Upper[0];
			double bbLower = bollinger.Lower[0];
			double bbMiddle = bollinger.Middle[0];
			double pricePos = (Close[0] - bbLower) / (bbUpper - bbLower);

			// Preço próximo ao topo da banda (potencial reversão short ou continuação long)
			if (Close[0] > bbUpper * 0.98)
			{
				// Se em tendência de alta forte, pode ser continuação
				if (trendScoreLong > 60)
					volatilityScoreLong += 30.0;
				else
					volatilityScoreShort += 40.0; // Overextended, potencial reversão
			}
			// Preço próximo à base da banda (potencial reversão long ou continuação short)
			else if (Close[0] < bbLower * 1.02)
			{
				if (trendScoreShort > 60)
					volatilityScoreShort += 30.0;
				else
					volatilityScoreLong += 40.0; // Overextended, potencial reversão
			}
			else if (pricePos > 0.7)
				volatilityScoreLong += 20.0;
			else if (pricePos < 0.3)
				volatilityScoreShort += 20.0;

			// Bandas expandindo (volatilidade aumentando) - bom para tendências
			if (CurrentBar > 0)
			{
				double bbWidth = bbUpper - bbLower;
				double prevBBWidth = bollinger.Upper[1] - bollinger.Lower[1];
				if (bbWidth > prevBBWidth)
				{
					if (trendScoreLong > trendScoreShort)
						volatilityScoreLong += 10.0;
					else
						volatilityScoreShort += 10.0;
				}
			}

			volatilityScoreSeries[0] = volatilityScoreLong > volatilityScoreShort ? volatilityScoreLong : -volatilityScoreShort;

			// === VOLUME SCORE (15%) ===
			double currentVolume = Volume[0];
			double avgVolume = volumeSMA[0];
			double mfiValue = mfi[0];

			// Volume acima da média
			if (currentVolume > avgVolume * MinVolumeMultiplier)
			{
				if (Close[0] > Close[1]) // Preço subindo com volume
					volumeScoreLong += 40.0;
				else if (Close[0] < Close[1]) // Preço caindo com volume
					volumeScoreShort += 40.0;
			}

			// MFI
			if (mfiValue < MfiOversoldLong && mfiValue > mfi[1])
				volumeScoreLong += 30.0; // Compra com volume crescente
			else if (mfiValue > MfiOverboughtShort && mfiValue < mfi[1])
				volumeScoreShort += 30.0; // Venda com volume crescente

			// OBV trending
			if (CurrentBar > 0)
			{
				if (obv[0] > obv[1] && Close[0] > Close[1])
					volumeScoreLong += 30.0;
				else if (obv[0] < obv[1] && Close[0] < Close[1])
					volumeScoreShort += 30.0;
			}

			volumeScoreLong = Math.Min(100.0, volumeScoreLong);
			volumeScoreShort = Math.Min(100.0, volumeScoreShort);
			volumeScoreSeries[0] = volumeScoreLong > volumeScoreShort ? volumeScoreLong : -volumeScoreShort;

			// === CONFIRMAÇÃO SCORE (10%) - Price Action ===
			// Padrões de candlestick simples
			bool bullishCandle = Close[0] > Open[0];
			bool bearishCandle = Close[0] < Open[0];
			double bodySize = Math.Abs(Close[0] - Open[0]);
			double candleRange = High[0] - Low[0];
			double bodyRatio = candleRange > 0 ? bodySize / candleRange : 0;

			// Candle forte (corpo grande)
			if (bullishCandle && bodyRatio > 0.7)
				confirmationScoreLong += 40.0;
			else if (bearishCandle && bodyRatio > 0.7)
				confirmationScoreShort += 40.0;

			// Preço acima/abaixo de médias móveis
			if (Close[0] > emaFast[0] && Close[0] > emaSlow[0])
				confirmationScoreLong += 30.0;
			else if (Close[0] < emaFast[0] && Close[0] < emaSlow[0])
				confirmationScoreShort += 30.0;

			// Momentum de preço (3 barras)
			if (CurrentBar >= 2)
			{
				if (Close[0] > Close[1] && Close[1] > Close[2])
					confirmationScoreLong += 30.0;
				else if (Close[0] < Close[1] && Close[1] < Close[2])
					confirmationScoreShort += 30.0;
			}

			confirmationScoreSeries[0] = confirmationScoreLong > confirmationScoreShort ? confirmationScoreLong : -confirmationScoreShort;

			// === SCORE FINAL PONDERADO ===
			double finalLongScore = (trendScoreLong * 0.30) +
									(momentumScoreLong * 0.25) +
									(volatilityScoreLong * 0.20) +
									(volumeScoreLong * 0.15) +
									(confirmationScoreLong * 0.10);

			double finalShortScore = (trendScoreShort * 0.30) +
									 (momentumScoreShort * 0.25) +
									 (volatilityScoreShort * 0.20) +
									 (volumeScoreShort * 0.15) +
									 (confirmationScoreShort * 0.10);

			// Boost do GlitchIndicator se disponível
			if (useGlitchIndicator && glitchIndicator != null)
			{
				try
				{
					double glitchLongScore = glitchIndicator.LongScore[0];
					double glitchShortScore = glitchIndicator.ShortScore[0];

					// Adiciona 10% do score do Glitch se ele for alto
					if (glitchLongScore >= GlitchMinScore)
						finalLongScore = Math.Min(100.0, finalLongScore + (glitchLongScore * 0.10));
					if (glitchShortScore >= GlitchMinScore)
						finalShortScore = Math.Min(100.0, finalShortScore + (glitchShortScore * 0.10));
				}
				catch { }
			}

			// Verificar número mínimo de sinais de confirmação
			int confirmationCountLong = 0;
			int confirmationCountShort = 0;

			if (trendScoreLong > 50) confirmationCountLong++;
			if (momentumScoreLong > 50) confirmationCountLong++;
			if (volatilityScoreLong > 30) confirmationCountLong++;
			if (volumeScoreLong > 40) confirmationCountLong++;
			if (confirmationScoreLong > 50) confirmationCountLong++;

			if (trendScoreShort > 50) confirmationCountShort++;
			if (momentumScoreShort > 50) confirmationCountShort++;
			if (volatilityScoreShort > 30) confirmationCountShort++;
			if (volumeScoreShort > 40) confirmationCountShort++;
			if (confirmationScoreShort > 50) confirmationCountShort++;

			// Aplicar penalidade se não houver confirmação suficiente
			if (confirmationCountLong < MinConfirmationSignalsLong)
				finalLongScore *= 0.7; // Reduz 30%
			if (confirmationCountShort < MinConfirmationSignalsShort)
				finalShortScore *= 0.7;

			longScoreSeries[0] = Math.Max(0, Math.Min(100, finalLongScore));
			shortScoreSeries[0] = Math.Max(0, Math.Min(100, finalShortScore));
		}

		/// <summary>
		/// Verifica condições de entrada
		/// </summary>
		private void CheckEntryConditions()
		{
			// Não entrar entre 16:30 e 18:00
			int currentTime = ToTime(Time[0]);
			if (currentTime >= 163000 && currentTime < 180000) // 16:30 - 18:00
				return;

			double longScore = longScoreSeries[0];
			double shortScore = shortScoreSeries[0];

			// Verificar cooldown para Long
			bool canEnterLong = !EnableCooldown || lastEntryBar < 0 || (CurrentBar - lastEntryBar) >= CooldownBarsLong;

			// Entrar Long
			if (canEnterLong && longScore >= ScoreThresholdLong && longScore > shortScore && longScore > lastLongScore)
			{
				int quantity = Math.Min(MaxPositionSize, 1);
				string signalName = "LongScore_" + (int)longScore;
				currentSignalName = signalName;
				EnterLong(quantity, signalName);
				entryPrice = Close[0];
				lastEntryBar = CurrentBar;
			}
			// Verificar cooldown para Short
			bool canEnterShort = !EnableCooldown || lastEntryBar < 0 || (CurrentBar - lastEntryBar) >= CooldownBarsShort;

			// Entrar Short
			if (canEnterShort && shortScore >= ScoreThresholdShort && shortScore > longScore && shortScore > lastShortScore)
			{
				int quantity = Math.Min(MaxPositionSize, 1);
				string signalName = "ShortScore_" + (int)shortScore;
				currentSignalName = signalName;
				EnterShort(quantity, signalName);
				entryPrice = Close[0];
				lastEntryBar = CurrentBar;
			}
		}

		/// <summary>
		/// Gerencia a saída no estilo StopLoss: stop fixo em ticks ou Swing+ATR (atualiza quando melhora).
		/// </summary>
		private void ManageStopLossStyle()
		{
			if (Position.MarketPosition == Cbi.MarketPosition.Flat)
				return;

			int qty = Math.Abs(Position.Quantity);
			if (qty <= 0)
				return;

			string fromEntry = string.IsNullOrEmpty(currentSignalName) ? "" : currentSignalName;

			// Stop fixo em ticks: coloca uma vez e não mexe
			if (!UseSwingStop)
			{
				if (!_fixedStopPlaced)
				{
					double entryRef = Position.AveragePrice;
					int effectiveTicks = Math.Max(1, StopLossTicks);
					if (MaxStopLossTicks > 0)
						effectiveTicks = Math.Min(effectiveTicks, MaxStopLossTicks);
					double stopPrice = Position.MarketPosition == Cbi.MarketPosition.Long
						? entryRef - effectiveTicks * TickSize
						: entryRef + effectiveTicks * TickSize;
					stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
					PlaceStopOrder(qty, stopPrice, fromEntry);
					_fixedStopPlaced = true;
					if (EnableDecisionLog)
						Print(string.Format("[Teste21b] Stop fixo colocado em {0} ({1} ticks).", stopPrice, effectiveTicks));
				}
				return;
			}

			// Stop Swing+ATR: calcular preço desejado e colocar ou atualizar
			double referencePrice = Close[0];
			double desiredStop = ComputeDesiredSwingStop(Position.MarketPosition, referencePrice, !_hasStopPrice);
			double entryForMaxStop = Position.AveragePrice;
			// Aplicar limite máximo de stop (em ticks) quando MaxStopLossTicks > 0
			if (MaxStopLossTicks > 0)
			{
				double maxDist = MaxStopLossTicks * TickSize;
				if (Position.MarketPosition == Cbi.MarketPosition.Long)
				{
					double floorStop = entryForMaxStop - maxDist;
					if (desiredStop < floorStop)
						desiredStop = Instrument.MasterInstrument.RoundToTickSize(floorStop);
				}
				else if (Position.MarketPosition == Cbi.MarketPosition.Short)
				{
					double ceilingStop = entryForMaxStop + maxDist;
					if (desiredStop > ceilingStop)
						desiredStop = Instrument.MasterInstrument.RoundToTickSize(ceilingStop);
				}
			}

			if (!_hasStopPrice)
			{
				_currentStopPrice = desiredStop;
				_hasStopPrice = true;
				PlaceStopOrder(qty, _currentStopPrice, fromEntry);
				if (EnableDecisionLog)
					Print(string.Format("[Teste21b] Stop Swing inicial em {0}.", _currentStopPrice));
				return;
			}

			double minImprove = Math.Max(1, MinStopImproveTicks) * TickSize;
			bool improveLong = Position.MarketPosition == Cbi.MarketPosition.Long && desiredStop > (_currentStopPrice + minImprove);
			bool improveShort = Position.MarketPosition == Cbi.MarketPosition.Short && desiredStop < (_currentStopPrice - minImprove);

			if (improveLong || improveShort)
			{
				_currentStopPrice = desiredStop;
				if (_stopOrder != null && (_stopOrder.OrderState == OrderState.Working || _stopOrder.OrderState == OrderState.Accepted))
					CancelOrder(_stopOrder);
				_stopOrder = null;
				PlaceStopOrder(qty, _currentStopPrice, fromEntry);
				if (EnableDecisionLog)
					Print(string.Format("[Teste21b] Stop Swing atualizado -> {0}.", _currentStopPrice));
			}
		}

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
		{
			if (order == null || order != _stopOrder)
				return;

			if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
				_stopOrder = null;

			if (orderState == OrderState.Rejected && Position.MarketPosition != Cbi.MarketPosition.Flat)
			{
				double recovered = ComputeDesiredSwingStop(Position.MarketPosition, Close[0], true);
				_currentStopPrice = recovered;
				_hasStopPrice = true;
				int qty = Math.Abs(Position.Quantity);
				PlaceStopOrder(qty, _currentStopPrice, string.IsNullOrEmpty(currentSignalName) ? "" : currentSignalName);
				if (EnableDecisionLog)
					Print(string.Format("[Teste21b] Stop rejeitado (erro={0}). Reenviado em {1}.", error, _currentStopPrice));
			}
		}

		private void PlaceStopOrder(int quantity, double stopPrice, string fromEntrySignal)
		{
			double rounded = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
			if (Position.MarketPosition == Cbi.MarketPosition.Long)
				_stopOrder = ExitLongStopMarket(0, true, quantity, rounded, StopSignalName, fromEntrySignal);
			else if (Position.MarketPosition == Cbi.MarketPosition.Short)
				_stopOrder = ExitShortStopMarket(0, true, quantity, rounded, StopSignalName, fromEntrySignal);
		}

		/// <summary>
		/// Verifica se a posição atual tem um stop loss ativo (ordem enviada e em Working/Accepted).
		/// </summary>
		private bool HasActiveStopLoss()
		{
			if (Position.MarketPosition == Cbi.MarketPosition.Flat)
				return false;
			if (_stopOrder == null)
				return false;
			return _stopOrder.OrderState == OrderState.Working || _stopOrder.OrderState == OrderState.Accepted;
		}

		private double ComputeDesiredSwingStop(Cbi.MarketPosition direction, double referencePrice, bool allowFallback)
		{
			double atrVal = SafeAtr();
			double offset = Math.Max(0, SwingAtrOffsetMultiplier) * atrVal;
			double fallbackDist = Math.Max(0.5, FallbackAtrMultiplier) * atrVal;

			double swingLow = _swing != null ? _swing.SwingLow[0] : 0.0;
			double swingHigh = _swing != null ? _swing.SwingHigh[0] : 0.0;

			if (direction == Cbi.MarketPosition.Long)
			{
				double candidate = swingLow - offset;
				bool valid = swingLow > 0 && candidate < Close[0];
				if (valid)
					return Instrument.MasterInstrument.RoundToTickSize(candidate);
				if (allowFallback)
					return Instrument.MasterInstrument.RoundToTickSize(referencePrice - fallbackDist);
				return Instrument.MasterInstrument.RoundToTickSize(_currentStopPrice);
			}

			if (direction == Cbi.MarketPosition.Short)
			{
				double candidate = swingHigh + offset;
				bool valid = swingHigh > 0 && candidate > Close[0];
				if (valid)
					return Instrument.MasterInstrument.RoundToTickSize(candidate);
				if (allowFallback)
					return Instrument.MasterInstrument.RoundToTickSize(referencePrice + fallbackDist);
				return Instrument.MasterInstrument.RoundToTickSize(_currentStopPrice);
			}

			return referencePrice;
		}

		private double SafeAtr()
		{
			double atrVal = atr != null ? atr[0] : (TickSize * 10);
			if (double.IsNaN(atrVal) || double.IsInfinity(atrVal) || atrVal <= 0)
				atrVal = TickSize * 10;
			return Math.Max(TickSize, atrVal);
		}

		private void ResetState()
		{
			_currentStopPrice = 0;
			_hasStopPrice = false;
			_fixedStopPlaced = false;
			_stopOrder = null;
		}

		/// <summary>
		/// Verifica se a perda diária máxima foi atingida
		/// </summary>
		private bool CheckMaxDailyLoss()
		{
			if (MaxDailyLoss <= 0)
				return false;

			try
			{
				double dailyPnL = 0;
				
				// Calcular PnL realizado do dia atual
				if (SystemPerformance?.AllTrades != null)
				{
					foreach (var trade in SystemPerformance.AllTrades)
					{
						if (trade?.Exit != null && trade.Exit.Time.Date == Time[0].Date)
						{
							dailyPnL += trade.ProfitCurrency;
						}
					}
				}

				// Adicionar PnL não realizado se houver posição
				if (Position.MarketPosition != Cbi.MarketPosition.Flat)
				{
					double unrealizedPnL = Position.GetUnrealizedProfitLoss(Cbi.PerformanceUnit.Currency, Close[0]);
					if (!double.IsNaN(unrealizedPnL))
						dailyPnL += unrealizedPnL;
				}

				return dailyPnL <= -MaxDailyLoss;
			}
			catch
			{
				return false;
			}
		}

		#region Properties - Trend
		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "EMA Fast Period", GroupName = "01. Trend", Order = 1)]
		public int EmaFastPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "EMA Slow Period", GroupName = "01. Trend", Order = 2)]
		public int EmaSlowPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "ADX Period", GroupName = "01. Trend", Order = 3)]
		public int AdxPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "ADX Trend Threshold", GroupName = "01. Trend", Order = 4)]
		public double AdxTrendThreshold { get; set; }
		#endregion

		#region Properties - Momentum
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "RSI Period", GroupName = "02. Momentum", Order = 1)]
		public int RsiPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0, 50)]
		[Display(Name = "RSI Oversold Long", GroupName = "02. Momentum", Order = 2)]
		public int RsiOversoldLong { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "RSI Overbought Long", GroupName = "02. Momentum", Order = 3)]
		public int RsiOverboughtLong { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "RSI Oversold Short", GroupName = "02. Momentum", Order = 4)]
		public int RsiOversoldShort { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "RSI Overbought Short", GroupName = "02. Momentum", Order = 5)]
		public int RsiOverboughtShort { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "RSI Exit Overbought Long", GroupName = "02. Momentum", Order = 6)]
		public int RsiExitOverboughtLong { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "RSI Exit Oversold Short", GroupName = "02. Momentum", Order = 7)]
		public int RsiExitOversoldShort { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "MACD Fast", GroupName = "02. Momentum", Order = 8)]
		public int MacdFast { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "MACD Slow", GroupName = "02. Momentum", Order = 9)]
		public int MacdSlow { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "MACD Smooth", GroupName = "02. Momentum", Order = 10)]
		public int MacdSmooth { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Stochastic K Period", GroupName = "02. Momentum", Order = 11)]
		public int StochKPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Stochastic D Period", GroupName = "02. Momentum", Order = 12)]
		public int StochDPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Stochastic Oversold Long", GroupName = "02. Momentum", Order = 13)]
		public int StochOversoldLong { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Stochastic Overbought Long", GroupName = "02. Momentum", Order = 14)]
		public int StochOverboughtLong { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Stochastic Oversold Short", GroupName = "02. Momentum", Order = 15)]
		public int StochOversoldShort { get; set; }

		[NinjaScriptProperty]
		[Range(00, 100)]
		[Display(Name = "Stochastic Overbought Short", GroupName = "02. Momentum", Order = 16)]
		public int StochOverboughtShort { get; set; }
		#endregion

		#region Properties - Volatilidade
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "ATR Period", GroupName = "03. Volatilidade", Order = 1)]
		public int AtrPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.5, 10.0)]
		[Display(Name = "ATR Multiplier Stop Long", GroupName = "03. Volatilidade", Order = 2)]
		public double AtrMultiplierStopLong { get; set; }

		[NinjaScriptProperty]
		[Range(0.5, 20.0)]
		[Display(Name = "ATR Multiplier Target Long", GroupName = "03. Volatilidade", Order = 3)]
		public double AtrMultiplierTargetLong { get; set; }

		[NinjaScriptProperty]
		[Range(0.5, 20.0)]
		[Display(Name = "ATR Multiplier Stop Short", GroupName = "03. Volatilidade", Order = 4)]
		public double AtrMultiplierStopShort { get; set; }

		[NinjaScriptProperty]
		[Range(0.5, 20.0)]
		[Display(Name = "ATR Multiplier Target Short", GroupName = "03. Volatilidade", Order = 5)]
		public double AtrMultiplierTargetShort { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Bollinger Period", GroupName = "03. Volatilidade", Order = 6)]
		public int BollingerPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.5, 5.0)]
		[Display(Name = "Bollinger Std Dev", GroupName = "03. Volatilidade", Order = 7)]
		public double BollingerStdDev { get; set; }
		#endregion

		#region Properties - Volume
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "MFI Period", GroupName = "04. Volume", Order = 1)]
		public int MfiPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0, 50)]
		[Display(Name = "MFI Oversold Long", GroupName = "04. Volume", Order = 2)]
		public int MfiOversoldLong { get; set; }

		[NinjaScriptProperty]
		[Range(50, 100)]
		[Display(Name = "MFI Overbought Long", GroupName = "04. Volume", Order = 3)]
		public int MfiOverboughtLong { get; set; }

		[NinjaScriptProperty]
		[Range(0, 50)]
		[Display(Name = "MFI Oversold Short", GroupName = "04. Volume", Order = 4)]
		public int MfiOversoldShort { get; set; }

		[NinjaScriptProperty]
		[Range(50, 100)]
		[Display(Name = "MFI Overbought Short", GroupName = "04. Volume", Order = 5)]
		public int MfiOverboughtShort { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, 5.0)]
		[Display(Name = "Min Volume Multiplier", GroupName = "04. Volume", Order = 4)]
		public double MinVolumeMultiplier { get; set; }
		#endregion

		#region Properties - Scoring
		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Score Threshold Long", GroupName = "05. Scoring", Order = 1)]
		public double ScoreThresholdLong { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Score Threshold Short", GroupName = "05. Scoring", Order = 2)]
		public double ScoreThresholdShort { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Min Confirmation Signals Long", GroupName = "05. Scoring", Order = 3)]
		public int MinConfirmationSignalsLong { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Min Confirmation Signals Short", GroupName = "05. Scoring", Order = 4)]
		public int MinConfirmationSignalsShort { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Glitch Indicator", GroupName = "05. Scoring", Order = 5)]
		public bool UseGlitchIndicator { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Glitch Min Score", GroupName = "05. Scoring", Order = 6)]
		public double GlitchMinScore { get; set; }
		#endregion

		#region Properties - Risco
		[NinjaScriptProperty]
		[Display(Name = "Use ATR Based Stops", GroupName = "06. Risco", Order = 1)]
		public bool UseAtrBasedStops { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Fixed Stop Long (Ticks)", GroupName = "06. Risco", Order = 2)]
		public int FixedStopTicksLong { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "Fixed Target Long (Ticks)", GroupName = "06. Risco", Order = 3)]
		public int FixedTargetTicksLong { get; set; }

		[NinjaScriptProperty]
		[Range(1, 1000)]
		[Display(Name = "Fixed Stop Short (Ticks)", GroupName = "06. Risco", Order = 4)]
		public int FixedStopTicksShort { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "Fixed Target Short (Ticks)", GroupName = "06. Risco", Order = 5)]
		public int FixedTargetTicksShort { get; set; }

		[NinjaScriptProperty]
		[Range(0, 10000)]
		[Display(Name = "Max Daily Loss", GroupName = "06. Risco", Order = 4)]
		public double MaxDailyLoss { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Max Position Size", GroupName = "06. Risco", Order = 5)]
		public int MaxPositionSize { get; set; }
		#endregion

		#region Properties - Filtros
		[NinjaScriptProperty]
		[Display(Name = "Enable Time Filter", GroupName = "07. Filtros", Order = 1)]
		public bool EnableTimeFilter { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "Start Time", GroupName = "07. Filtros", Order = 2)]
		public int StartTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "End Time", GroupName = "07. Filtros", Order = 3)]
		public int EndTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Cooldown", GroupName = "07. Filtros", Order = 4)]
		public bool EnableCooldown { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Cooldown Bars Long", GroupName = "07. Filtros", Order = 5)]
		public int CooldownBarsLong { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Cooldown Bars Short", GroupName = "07. Filtros", Order = 6)]
		public int CooldownBarsShort { get; set; }
		#endregion

		#region Properties - Saída (StopLoss)
		[NinjaScriptProperty]
		[Display(Name = "EnableDecisionLog", GroupName = "08. Saída", Order = 0)]
		public bool EnableDecisionLog { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UseSwingStop", GroupName = "08. Saída", Order = 1)]
		public bool UseSwingStop { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "SwingStrength", GroupName = "08. Saída", Order = 2)]
		public int SwingStrength { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, 10.0)]
		[Display(Name = "SwingAtrOffsetMultiplier", GroupName = "08. Saída", Order = 3)]
		public double SwingAtrOffsetMultiplier { get; set; }

		[NinjaScriptProperty]
		[Range(-10.1, 10.0)]
		[Display(Name = "FallbackAtrMultiplier", GroupName = "08. Saída", Order = 4)]
		public double FallbackAtrMultiplier { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "MinStopImproveTicks", GroupName = "08. Saída", Order = 5)]
		public int MinStopImproveTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 5000)]
		[Display(Name = "StopLossTicks", GroupName = "08. Saída", Order = 6)]
		public int StopLossTicks { get; set; }

		/// <summary>
		/// Distância máxima do stop em ticks (a partir do preço de entrada). 0 = sem limite. Otimizável.
		/// </summary>
		[NinjaScriptProperty]
		[Range(0, 50000)]
		[Display(Name = "Max Stop Loss (Ticks)", GroupName = "08. Saída", Order = 7)]
		public int MaxStopLossTicks { get; set; }
		#endregion
	}
}
