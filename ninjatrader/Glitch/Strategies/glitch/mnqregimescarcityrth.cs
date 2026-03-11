// MNQ_RegimeScarcityRTH_v01.cs
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class MNQ_RegimeScarcityRTH_v01 : Strategy
	{
		private EMA emaFast;
		private EMA emaMed;
		private EMA emaSlow;
		private ATR atrFast;
		private VWMA vwma;
		private StdDev stdDev;

		private Series<double> vwapSeries;
		private double sessionPvSum;
		private double sessionVolSum;

		private TimeZoneInfo appTz;
		private TimeZoneInfo tplTz;

		private DateTime currentTplDay = Core.Globals.MinDate;
		private double dayStartCumProfit;
		private bool blockedForDay;
		private int filledEntriesToday;
		private int entryBarIndex = -1;
		private bool entryPending;
		private string entryPendingSignal = string.Empty;
		private bool stopLossTriggeredToday;
		private MarketPosition lastPosition = MarketPosition.Flat;
		private int activeRuleIndex = -1;
		private int priorTradeCount;

		private const int RuleCount = 8;
		private Queue<double>[] rulePnLWindow;
		private double[] rulePnLSum;
		private int[] ruleWinCount;
		private int[] ruleUnhealthySignalSkips;

		#region Parameters

		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "EmaFastPeriod", Order = 1, GroupName = "Features")]
		public int EmaFastPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "EmaMedPeriod", Order = 2, GroupName = "Features")]
		public int EmaMedPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "EmaSlowPeriod", Order = 3, GroupName = "Features")]
		public int EmaSlowPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(2, 500)]
		[Display(Name = "VwmaPeriod", Order = 4, GroupName = "Features")]
		public int VwmaPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(2, 500)]
		[Display(Name = "ZScorePeriod", Order = 5, GroupName = "Features")]
		public int ZScorePeriod { get; set; }

		[NinjaScriptProperty]
		[Range(2, 500)]
		[Display(Name = "AtrFastPeriod", Order = 6, GroupName = "Features")]
		public int AtrFastPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(10, 2000)]
		[Display(Name = "AtrPercentileLookback", Order = 7, GroupName = "Features")]
		public int AtrPercentileLookback { get; set; }

		[NinjaScriptProperty]
		[Range(10, 2000)]
		[Display(Name = "RangeBaselineLookback", Order = 8, GroupName = "Features")]
		public int RangeBaselineLookback { get; set; }

		[NinjaScriptProperty]
		[Range(0.25, 10.0)]
		[Display(Name = "RangeExpandMult", Order = 9, GroupName = "Features")]
		public double RangeExpandMult { get; set; }

		[NinjaScriptProperty]
		[Range(0.01, 5.0)]
		[Display(Name = "RangeCompressMult", Order = 10, GroupName = "Features")]
		public double RangeCompressMult { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "MinAtrPct", Order = 11, GroupName = "Regime")]
		public int MinAtrPct { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "RTHStartTime", Description = "HHmmss in template timezone.", Order = 20, GroupName = "Windows")]
		public int RTHStartTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "EntryCutoffTime", Description = "Last time to open a new trade (HHmmss template timezone).", Order = 21, GroupName = "Windows")]
		public int EntryCutoffTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "ForceFlatTime", Description = "Force flat at/after this time (HHmmss template timezone).", Order = 22, GroupName = "Windows")]
		public int ForceFlatTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "SkipFriday", Order = 23, GroupName = "Windows")]
		public bool SkipFriday { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "DisableLongOnWednesday", Order = 24, GroupName = "Windows")]
		public bool DisableLongOnWednesday { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "DisableShortOnMonday", Order = 25, GroupName = "Windows")]
		public bool DisableShortOnMonday { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "DisableShortOnThursday", Order = 26, GroupName = "Windows")]
		public bool DisableShortOnThursday { get; set; }

		[NinjaScriptProperty]
		[Range(0.25, 200.0)]
		[Display(Name = "StopPoints", Order = 30, GroupName = "Risk")]
		public double StopPoints { get; set; }

		[NinjaScriptProperty]
		[Range(0.25, 300.0)]
		[Display(Name = "TargetPoints", Order = 31, GroupName = "Risk")]
		public double TargetPoints { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "MaxBarsInTrade", Order = 32, GroupName = "Risk")]
		public int MaxBarsInTrade { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "MaxEntriesPerDay", Order = 33, GroupName = "Risk")]
		public int MaxEntriesPerDay { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "StopAfterFirstStopLoss", Order = 34, GroupName = "Risk")]
		public bool StopAfterFirstStopLoss { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10000)]
		[Display(Name = "DailyLossLimitUsd", Order = 35, GroupName = "Risk")]
		public double DailyLossLimitUsd { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10000)]
		[Display(Name = "DailyProfitCapUsd", Order = 36, GroupName = "Risk")]
		public double DailyProfitCapUsd { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EnableLongRule1", Order = 40, GroupName = "Rules")]
		public bool EnableLongRule1 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EnableLongRule2", Order = 41, GroupName = "Rules")]
		public bool EnableLongRule2 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EnableLongRule3", Order = 42, GroupName = "Rules")]
		public bool EnableLongRule3 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EnableLongRule4", Order = 43, GroupName = "Rules")]
		public bool EnableLongRule4 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EnableShortRule1", Order = 44, GroupName = "Rules")]
		public bool EnableShortRule1 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EnableShortRule2", Order = 45, GroupName = "Rules")]
		public bool EnableShortRule2 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EnableShortRule3", Order = 46, GroupName = "Rules")]
		public bool EnableShortRule3 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EnableShortRule4", Order = 47, GroupName = "Rules")]
		public bool EnableShortRule4 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UseAdaptiveRuleHealth", Order = 50, GroupName = "Adaptive")]
		public bool UseAdaptiveRuleHealth { get; set; }

		[NinjaScriptProperty]
		[Range(5, 500)]
		[Display(Name = "AdaptiveLookbackTrades", Order = 51, GroupName = "Adaptive")]
		public int AdaptiveLookbackTrades { get; set; }

		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "AdaptiveMinTrades", Order = 52, GroupName = "Adaptive")]
		public int AdaptiveMinTrades { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, 1.0)]
		[Display(Name = "AdaptiveMinWinRate", Order = 53, GroupName = "Adaptive")]
		public double AdaptiveMinWinRate { get; set; }

		[NinjaScriptProperty]
		[Range(-200.0, 200.0)]
		[Display(Name = "AdaptiveMinAvgPnlUsd", Order = 54, GroupName = "Adaptive")]
		public double AdaptiveMinAvgPnlUsd { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "AdaptiveProbeEverySignals", Description = "When a rule is unhealthy, allow one probe signal every N blocked signals.", Order = 55, GroupName = "Adaptive")]
		public int AdaptiveProbeEverySignals { get; set; }

		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "MNQ_RegimeScarcityRTH_v01";
				Description = "RTH-only scarcity strategy mined from consolidated telemetry. Default flat with strict regime filters and hard daily risk gates, using the robust core rule set.";
				Calculate = Calculate.OnBarClose;

				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				StopTargetHandling = StopTargetHandling.PerEntryExecution;
				IsInstantiatedOnEachOptimizationIteration = false;

				EmaFastPeriod = 9;
				EmaMedPeriod = 20;
				EmaSlowPeriod = 50;
				VwmaPeriod = 50;
				ZScorePeriod = 50;
				AtrFastPeriod = 14;
				AtrPercentileLookback = 400;
				RangeBaselineLookback = 50;
				RangeExpandMult = 1.5;
				RangeCompressMult = 0.7;
				MinAtrPct = 61;

				RTHStartTime = 93000;
				EntryCutoffTime = 125900;
				ForceFlatTime = 150000;
				SkipFriday = false;
				DisableLongOnWednesday = false;
				DisableShortOnMonday = false;
				DisableShortOnThursday = false;

				StopPoints = 30.0;
				TargetPoints = 50.0;
				MaxBarsInTrade = 360;
				MaxEntriesPerDay = 2;
				StopAfterFirstStopLoss = true;
				DailyLossLimitUsd = 80.0;
				DailyProfitCapUsd = 300.0;

				EnableLongRule1 = false;
				EnableLongRule2 = false;
				EnableLongRule3 = true;
				EnableLongRule4 = false;
				EnableShortRule1 = false;
				EnableShortRule2 = false;
				EnableShortRule3 = true;
				EnableShortRule4 = false;

				UseAdaptiveRuleHealth = false;
				AdaptiveLookbackTrades = 60;
				AdaptiveMinTrades = 20;
				AdaptiveMinWinRate = 0.39;
				AdaptiveMinAvgPnlUsd = 0.0;
				AdaptiveProbeEverySignals = 12;
			}
			else if (State == State.DataLoaded)
			{
				emaFast = EMA(BarsArray[0], EmaFastPeriod);
				emaMed = EMA(BarsArray[0], EmaMedPeriod);
				emaSlow = EMA(BarsArray[0], EmaSlowPeriod);
				atrFast = ATR(BarsArray[0], AtrFastPeriod);
				vwma = VWMA(BarsArray[0], VwmaPeriod);
				stdDev = StdDev(BarsArray[0], ZScorePeriod);
				vwapSeries = new Series<double>(this, MaximumBarsLookBack.Infinite);

				appTz = Core.Globals.GeneralOptions != null && Core.Globals.GeneralOptions.TimeZoneInfo != null
					? Core.Globals.GeneralOptions.TimeZoneInfo
					: TimeZoneInfo.Local;

				tplTz = (Bars != null && Bars.TradingHours != null && Bars.TradingHours.TimeZoneInfo != null)
					? Bars.TradingHours.TimeZoneInfo
					: appTz;

				rulePnLWindow = new Queue<double>[RuleCount];
				rulePnLSum = new double[RuleCount];
				ruleWinCount = new int[RuleCount];
				ruleUnhealthySignalSkips = new int[RuleCount];
				for (int i = 0; i < RuleCount; i++)
					rulePnLWindow[i] = new Queue<double>();

				priorTradeCount = SystemPerformance.AllTrades.Count;
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0)
				return;

			SyncPositionState();

			if (CurrentBar < ComputeWarmupBars())
				return;

			UpdateSessionVWAP();

			DateTime tsTpl = ConvertAppToTpl(Time[0]);
			ResetDayIfNeeded(tsTpl.Date);

			double realizedToday = GetCumProfit() - dayStartCumProfit;
			if (realizedToday <= -DailyLossLimitUsd || realizedToday >= DailyProfitCapUsd)
				blockedForDay = true;

			if (blockedForDay)
			{
				ForceFlatIfNeeded("DailyGate");
				return;
			}

			int ttTpl = ToTimeHHmmss(tsTpl);
			if (Position.MarketPosition != MarketPosition.Flat)
			{
				if (ttTpl >= ForceFlatTime)
				{
					ForceFlatIfNeeded("TimeGate");
					return;
				}

				if (entryBarIndex >= 0 && CurrentBar - entryBarIndex >= MaxBarsInTrade)
				{
					ForceFlatIfNeeded("BarGate");
					return;
				}

				return;
			}

			if (entryPending)
				return;

			if (filledEntriesToday >= MaxEntriesPerDay)
				return;

			if (SkipFriday && tsTpl.DayOfWeek == DayOfWeek.Friday)
				return;

			if (StopAfterFirstStopLoss && stopLossTriggeredToday)
				return;

			if (ttTpl < RTHStartTime || ttTpl > EntryCutoffTime)
				return;

			double atrValue = atrFast[0];
			int atrPct = AtrPercentileRankNoAlloc(atrValue, AtrPercentileLookback);
			if (atrPct < MinAtrPct)
				return;

			double c = Close[0];
			double vwapVal = vwapSeries[0];
			double distVwap = c - vwapVal;
			bool aboveVwap = distVwap >= 0.0;

			double ef = emaFast[0];
			double em = emaMed[0];
			double es = emaSlow[0];
			int emaAlign = ComputeEmaAlignState(ef, em, es);

			double denom = atrValue > 0.0 ? atrValue : 1.0;
			double efSlopeN = (emaFast[0] - emaFast[1]) / denom;
			double emSlopeN = (emaMed[0] - emaMed[1]) / denom;
			double esSlopeN = (emaSlow[0] - emaSlow[1]) / denom;
			int slopeState = ComputeSlopeState(efSlopeN, emSlopeN, esSlopeN);

			double sd = stdDev[0];
			double distVwma = c - vwma[0];
			double zAbs = sd > 0.0 ? Math.Abs(distVwma / sd) : 0.0;

			int atrBin = GetAtrBin(atrPct);
			int zBin = GetZBin(zAbs);
			int rangeState = ComputeRangeState();

			string longSignal = GetLongSignal(emaAlign, slopeState, atrBin, zBin, aboveVwap, rangeState, tsTpl.DayOfWeek);
			string shortSignal = GetShortSignal(emaAlign, slopeState, atrBin, zBin, aboveVwap, rangeState, tsTpl.DayOfWeek);
			if (!IsRuleAllowed(longSignal))
				longSignal = string.Empty;
			if (!IsRuleAllowed(shortSignal))
				shortSignal = string.Empty;
			bool longMatch = !string.IsNullOrEmpty(longSignal);
			bool shortMatch = !string.IsNullOrEmpty(shortSignal);

			// In ambiguous regimes, stay flat by design.
			if (longMatch == shortMatch)
				return;

			int stopTicks = PointsToTicks(StopPoints);
			int targetTicks = PointsToTicks(TargetPoints);

			if (longMatch)
			{
				SetStopLoss(longSignal, CalculationMode.Ticks, stopTicks, false);
				SetProfitTarget(longSignal, CalculationMode.Ticks, targetTicks);
				EnterLong(DefaultQuantity, longSignal);
				entryPending = true;
				entryPendingSignal = longSignal;
			}
			else
			{
				SetStopLoss(shortSignal, CalculationMode.Ticks, stopTicks, false);
				SetProfitTarget(shortSignal, CalculationMode.Ticks, targetTicks);
				EnterShort(DefaultQuantity, shortSignal);
				entryPending = true;
				entryPendingSignal = shortSignal;
			}
		}

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
			double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
		{
			if (order == null)
				return;

			if (string.IsNullOrEmpty(entryPendingSignal))
				return;

			if (order.Name != entryPendingSignal)
				return;

			if (orderState == OrderState.Rejected || orderState == OrderState.Cancelled || orderState == OrderState.Filled || orderState == OrderState.PartFilled)
			{
				entryPending = false;
				if (orderState == OrderState.Rejected || orderState == OrderState.Cancelled || orderState == OrderState.Filled || orderState == OrderState.PartFilled)
					entryPendingSignal = string.Empty;
			}
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
			MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution == null || execution.Order == null)
				return;

			if (execution.Order.OrderState != OrderState.Filled)
				return;

			string orderName = execution.Order.Name ?? string.Empty;

			if (IsEntrySignalName(orderName))
			{
				filledEntriesToday++;
				entryBarIndex = CurrentBar;
				entryPending = false;
				entryPendingSignal = string.Empty;
				activeRuleIndex = RuleIndexFromSignal(orderName);
			}

			if (orderName.IndexOf("Stop loss", StringComparison.OrdinalIgnoreCase) >= 0)
				stopLossTriggeredToday = true;
		}

		private void SyncPositionState()
		{
			MarketPosition now = Position.MarketPosition;

			if (lastPosition != MarketPosition.Flat && now == MarketPosition.Flat)
			{
				double closedPnl = ComputeRealizedPnLSinceLast();
				if (activeRuleIndex >= 0)
					UpdateRuleHealth(activeRuleIndex, closedPnl);

				activeRuleIndex = -1;
				entryBarIndex = -1;
				entryPending = false;
				entryPendingSignal = string.Empty;
			}

			lastPosition = now;
		}

		private void ResetDayIfNeeded(DateTime tplDay)
		{
			if (currentTplDay == tplDay)
				return;

			currentTplDay = tplDay;
			dayStartCumProfit = GetCumProfit();
			blockedForDay = false;
			filledEntriesToday = 0;
			entryPending = false;
			entryPendingSignal = string.Empty;
			stopLossTriggeredToday = false;
		}

		private void ForceFlatIfNeeded(string signalName)
		{
			if (Position.MarketPosition == MarketPosition.Long)
				ExitLong(signalName);
			else if (Position.MarketPosition == MarketPosition.Short)
				ExitShort(signalName);
		}

		private int ComputeWarmupBars()
		{
			return Math.Max(200,
				Math.Max(AtrPercentileLookback,
				Math.Max(RangeBaselineLookback,
				Math.Max(VwmaPeriod,
				Math.Max(ZScorePeriod, EmaSlowPeriod)))));
		}

		private double GetCumProfit()
		{
			try
			{
				return SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
			}
			catch
			{
				return 0.0;
			}
		}

		private double ComputeRealizedPnLSinceLast()
		{
			try
			{
				int total = SystemPerformance.AllTrades.Count;
				if (priorTradeCount < 0 || priorTradeCount > total)
					priorTradeCount = total;

				double pnl = 0.0;
				for (int i = priorTradeCount; i < total; i++)
					pnl += SystemPerformance.AllTrades[i].ProfitCurrency;

				priorTradeCount = total;
				return pnl;
			}
			catch
			{
				return 0.0;
			}
		}

		private void UpdateRuleHealth(int ruleIndex, double pnlUsd)
		{
			if (ruleIndex < 0 || ruleIndex >= RuleCount || rulePnLWindow == null || rulePnLWindow[ruleIndex] == null)
				return;

			Queue<double> q = rulePnLWindow[ruleIndex];
			q.Enqueue(pnlUsd);
			rulePnLSum[ruleIndex] += pnlUsd;
			if (pnlUsd > 0.0)
				ruleWinCount[ruleIndex]++;

			while (q.Count > AdaptiveLookbackTrades)
			{
				double old = q.Dequeue();
				rulePnLSum[ruleIndex] -= old;
				if (old > 0.0)
					ruleWinCount[ruleIndex]--;
			}
		}

		private bool IsRuleAllowed(string signalName)
		{
			if (string.IsNullOrEmpty(signalName))
				return false;

			if (!UseAdaptiveRuleHealth)
				return true;

			int idx = RuleIndexFromSignal(signalName);
			if (idx < 0 || idx >= RuleCount || rulePnLWindow == null || rulePnLWindow[idx] == null)
				return true;

			int n = rulePnLWindow[idx].Count;
			if (n < AdaptiveMinTrades)
				return true;

			double avg = rulePnLSum[idx] / n;
			double wr = (double)ruleWinCount[idx] / n;
			bool healthy = avg >= AdaptiveMinAvgPnlUsd && wr >= AdaptiveMinWinRate;

			if (healthy)
			{
				ruleUnhealthySignalSkips[idx] = 0;
				return true;
			}

			// Keep a sparse probe cadence so disabled rules can recover from regime shifts.
			ruleUnhealthySignalSkips[idx]++;
			if (ruleUnhealthySignalSkips[idx] >= AdaptiveProbeEverySignals)
			{
				ruleUnhealthySignalSkips[idx] = 0;
				return true;
			}

			return false;
		}

		private int RuleIndexFromSignal(string signalName)
		{
			switch (signalName)
			{
				case "RegimeLong_L1": return 0;
				case "RegimeLong_L2": return 1;
				case "RegimeLong_L3": return 2;
				case "RegimeLong_L4": return 3;
				case "RegimeShort_S1": return 4;
				case "RegimeShort_S2": return 5;
				case "RegimeShort_S3": return 6;
				case "RegimeShort_S4": return 7;
				default: return -1;
			}
		}

		private int PointsToTicks(double points)
		{
			int ticks = (int)Math.Round(points / TickSize, MidpointRounding.AwayFromZero);
			return Math.Max(1, ticks);
		}

		private void UpdateSessionVWAP()
		{
			if (Bars.IsFirstBarOfSession)
			{
				sessionPvSum = 0.0;
				sessionVolSum = 0.0;
			}

			double vol = Volume[0];
			double tp = (High[0] + Low[0] + Close[0]) / 3.0;

			sessionPvSum += tp * vol;
			sessionVolSum += vol;

			double vwapVal = sessionVolSum > 0.0 ? sessionPvSum / sessionVolSum : Close[0];
			vwapSeries[0] = vwapVal;
		}

		private int AtrPercentileRankNoAlloc(double currentAtr, int lookback)
		{
			int n = Math.Min(lookback, CurrentBar);
			if (n < 5)
				return -1;

			int le = 0;
			for (int i = 1; i <= n; i++)
			{
				if (atrFast[i] <= currentAtr)
					le++;
			}

			double pct = (double)le / n;
			return (int)Math.Round(pct * 100.0, MidpointRounding.AwayFromZero);
		}

		private double AvgRangeBaseline(int lookback)
		{
			int n = Math.Min(lookback, CurrentBar);
			if (n < 5)
				return double.NaN;

			double sum = 0.0;
			for (int i = 1; i <= n; i++)
				sum += (High[i] - Low[i]);

			return sum / n;
		}

		// Returns: +1 expand, -1 compress, 0 normal.
		private int ComputeRangeState()
		{
			double baseline = AvgRangeBaseline(RangeBaselineLookback);
			if (double.IsNaN(baseline) || baseline <= 0.0)
				return 0;

			double range = High[0] - Low[0];
			if (range > RangeExpandMult * baseline)
				return 1;
			if (range < RangeCompressMult * baseline)
				return -1;
			return 0;
		}

		// Returns: +1 UP3, -1 DN3, 0 MIX.
		private int ComputeSlopeState(double efSlopeN, double emSlopeN, double esSlopeN)
		{
			if (efSlopeN > 0.0 && emSlopeN > 0.0 && esSlopeN > 0.0)
				return 1;
			if (efSlopeN < 0.0 && emSlopeN < 0.0 && esSlopeN < 0.0)
				return -1;
			return 0;
		}

		private int ComputeEmaAlignState(double ef, double em, double es)
		{
			if (ef > em && em > es) return 2;
			if (ef < em && em < es) return -2;
			if (ef > es) return 1;
			if (ef < es) return -1;
			return 0;
		}

		// 0=A0_20, 1=A21_40, 2=A41_60, 3=A61_80, 4=A81_100
		private int GetAtrBin(int atrPct)
		{
			if (atrPct <= 20) return 0;
			if (atrPct <= 40) return 1;
			if (atrPct <= 60) return 2;
			if (atrPct <= 80) return 3;
			return 4;
		}

		// 0=Z0_0.6, 1=Z0.6_1.2, 2=Z1.2_1.8, 3=Z1.8_2.5, 4=Z2.5+
		private int GetZBin(double zAbs)
		{
			if (zAbs <= 0.6) return 0;
			if (zAbs <= 1.2) return 1;
			if (zAbs <= 1.8) return 2;
			if (zAbs <= 2.5) return 3;
			return 4;
		}

		private string GetLongSignal(int emaAlign, int slopeState, int atrBin, int zBin, bool aboveVwap, int rangeState, DayOfWeek dow)
		{
			if (DisableLongOnWednesday && dow == DayOfWeek.Wednesday)
				return string.Empty;

			// Rule L1: RTH -2/DN3/A61_80/Z1.2_1.8/BELOW/NORMAL -> LONG
			if (EnableLongRule1 && emaAlign == -2 && slopeState == -1 && atrBin == 3 && zBin == 2 && !aboveVwap && rangeState == 0)
				return "RegimeLong_L1";

			// Rule L2: RTH +2/UP3/A81_100/Z1.2_1.8/ABOVE/COMPRESS -> LONG
			if (EnableLongRule2 && emaAlign == 2 && slopeState == 1 && atrBin == 4 && zBin == 2 && aboveVwap && rangeState == -1)
				return "RegimeLong_L2";

			// Rule L3: RTH +2/MIX/A81_100/Z0.6_1.2/ABOVE/NORMAL -> LONG
			if (EnableLongRule3 && emaAlign == 2 && slopeState == 0 && atrBin == 4 && zBin == 1 && aboveVwap && rangeState == 0)
				return "RegimeLong_L3";

			// Rule L4: RTH +2/UP3/A81_100/Z0.6_1.2/ABOVE/COMPRESS -> LONG
			if (EnableLongRule4 && emaAlign == 2 && slopeState == 1 && atrBin == 4 && zBin == 1 && aboveVwap && rangeState == -1)
				return "RegimeLong_L4";

			return string.Empty;
		}

		private string GetShortSignal(int emaAlign, int slopeState, int atrBin, int zBin, bool aboveVwap, int rangeState, DayOfWeek dow)
		{
			if (DisableShortOnMonday && dow == DayOfWeek.Monday)
				return string.Empty;

			if (DisableShortOnThursday && dow == DayOfWeek.Thursday)
				return string.Empty;

			// Rule S1: RTH -2/MIX/A61_80/Z0_0.6/BELOW/NORMAL -> SHORT
			if (EnableShortRule1 && emaAlign == -2 && slopeState == 0 && atrBin == 3 && zBin == 0 && !aboveVwap && rangeState == 0)
				return "RegimeShort_S1";

			// Rule S2: RTH +2/UP3/A61_80/Z1.8_2.5/ABOVE/COMPRESS -> SHORT
			if (EnableShortRule2 && emaAlign == 2 && slopeState == 1 && atrBin == 3 && zBin == 3 && aboveVwap && rangeState == -1)
				return "RegimeShort_S2";

			// Rule S3: RTH -2/DN3/A81_100/Z0.6_1.2/BELOW/EXPAND -> SHORT
			if (EnableShortRule3 && emaAlign == -2 && slopeState == -1 && atrBin == 4 && zBin == 1 && !aboveVwap && rangeState == 1)
				return "RegimeShort_S3";

			// Rule S4: RTH -2/MIX/A81_100/Z0_0.6/BELOW/NORMAL -> SHORT
			if (EnableShortRule4 && emaAlign == -2 && slopeState == 0 && atrBin == 4 && zBin == 0 && !aboveVwap && rangeState == 0)
				return "RegimeShort_S4";

			return string.Empty;
		}

		private DateTime ConvertAppToTpl(DateTime tApp)
		{
			try
			{
				DateTime t = DateTime.SpecifyKind(tApp, DateTimeKind.Unspecified);
				return TimeZoneInfo.ConvertTime(t, appTz, tplTz);
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

		private bool IsEntrySignalName(string orderName)
		{
			if (string.IsNullOrEmpty(orderName))
				return false;

			return orderName.StartsWith("RegimeLong_", StringComparison.Ordinal)
				|| orderName.StartsWith("RegimeShort_", StringComparison.Ordinal);
		}
	}
}
