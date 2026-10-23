#region Using declarations
using System;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Additive adaptive-evidence helpers for the market snapshot.
    /// Values are observations for Hermes — never mechanical entry/stop rules.
    /// </summary>
    public partial class GlitchAnalyticsBridge
    {
        private static double BuildHmaTrendAlignment(double hma18, double hma43)
        {
            // Same stacked-alignment pattern as EMA, reduced to the 18/43 pair.
            if (hma18 >= hma43)
                return 1.0;
            if (hma18 <= hma43)
                return -1.0;
            return 0.0;
        }

        private static string BuildPriceVsVwapSide(double? orderFlowVwapDeviation)
        {
            if (!orderFlowVwapDeviation.HasValue)
                return null;

            const double AtEpsilon = 1e-8;
            if (orderFlowVwapDeviation.Value > AtEpsilon)
                return "above";
            if (orderFlowVwapDeviation.Value < -AtEpsilon)
                return "below";
            return "at";
        }

        private static string BuildOrbState(double close, double? orbHigh, double? orbLow, bool orbFrozen)
        {
            if (!orbFrozen || !orbHigh.HasValue || !orbLow.HasValue)
                return null;

            if (close > orbHigh.Value)
                return "broke_above";
            if (close < orbLow.Value)
                return "broke_below";
            return "inside";
        }

        private bool TryDetectReversalCandle(int bip, out string pattern)
        {
            pattern = "none";
            if (bip < 0 || bip >= BarsArray.Length || CurrentBars[bip] < 1)
                return false;

            double o0 = Opens[bip][0];
            double h0 = Highs[bip][0];
            double l0 = Lows[bip][0];
            double c0 = Closes[bip][0];
            double o1 = Opens[bip][1];
            double c1 = Closes[bip][1];

            double body0 = Math.Abs(c0 - o0);
            double range0 = h0 - l0;
            if (range0 <= 1e-12)
            {
                pattern = "none";
                return true;
            }

            bool prevBull = c1 > o1;
            bool prevBear = c1 < o1;
            bool currBull = c0 > o0;
            bool currBear = c0 < o0;
            double prevBodyHigh = Math.Max(o1, c1);
            double prevBodyLow = Math.Min(o1, c1);
            double currBodyHigh = Math.Max(o0, c0);
            double currBodyLow = Math.Min(o0, c0);

            if (currBull && prevBear && currBodyLow <= prevBodyLow && currBodyHigh >= prevBodyHigh)
            {
                pattern = "bullish_engulfing";
                return true;
            }

            if (currBear && prevBull && currBodyLow <= prevBodyLow && currBodyHigh >= prevBodyHigh)
            {
                pattern = "bearish_engulfing";
                return true;
            }

            double upperWick = h0 - Math.Max(o0, c0);
            double lowerWick = Math.Min(o0, c0) - l0;
            double body = Math.Max(body0, 1e-12);

            // Hammer: long lower wick, small upper wick, close near high.
            if (lowerWick >= 2.0 * body && upperWick <= body && (c0 - l0) >= 0.60 * range0)
            {
                pattern = "hammer";
                return true;
            }

            // Shooting star: long upper wick, small lower wick, close near low.
            if (upperWick >= 2.0 * body && lowerWick <= body && (h0 - c0) >= 0.60 * range0)
            {
                pattern = "shooting_star";
                return true;
            }

            pattern = "none";
            return true;
        }

        private bool TryResolvePivotBreakoutState(int bip, double close, out string state)
        {
            state = null;
            if (bip < 0 || bip >= BarsArray.Length || CurrentBars[bip] < 4)
                return false;

            int lookback = Math.Min(CurrentBars[bip], 40);
            double? swingHigh = null;
            double? swingLow = null;

            // 3-bar fractal pivots (barsAgo >= 2 so both neighbors exist).
            for (int barsAgo = 2; barsAgo <= lookback - 1; barsAgo++)
            {
                double high = Highs[bip][barsAgo];
                double low = Lows[bip][barsAgo];
                if (!swingHigh.HasValue &&
                    high > Highs[bip][barsAgo - 1] &&
                    high > Highs[bip][barsAgo + 1])
                    swingHigh = high;

                if (!swingLow.HasValue &&
                    low < Lows[bip][barsAgo - 1] &&
                    low < Lows[bip][barsAgo + 1])
                    swingLow = low;

                if (swingHigh.HasValue && swingLow.HasValue)
                    break;
            }

            if (!swingHigh.HasValue && !swingLow.HasValue)
            {
                state = "none";
                return true;
            }

            if (swingHigh.HasValue && close > swingHigh.Value)
            {
                state = "broke_above";
                return true;
            }

            if (swingLow.HasValue && close < swingLow.Value)
            {
                state = "broke_below";
                return true;
            }

            state = "inside";
            return true;
        }

        private void PopulateAdaptiveEvidence(int bip, ref SignalSnapshot signal, SessionTracker session)
        {
            signal.Hma12 = null;
            signal.Hma18 = null;
            signal.Hma43 = null;
            signal.HmaTrendAlignment = null;
            signal.PriceToHma12Distance = null;
            signal.PriceVsVwapSide = BuildPriceVsVwapSide(signal.OrderFlowVwapDeviation);
            signal.OrbHigh = null;
            signal.OrbLow = null;
            signal.OrbState = null;
            signal.ReversalCandle = null;
            signal.PivotBreakoutState = null;

            if (bip < 0 || CurrentBars == null || bip >= CurrentBars.Length)
                return;

            if (_hma12ByBip != null && bip < _hma12ByBip.Length && _hma12ByBip[bip] != null && CurrentBars[bip] >= 12)
                signal.Hma12 = _hma12ByBip[bip][0];
            if (_hma18ByBip != null && bip < _hma18ByBip.Length && _hma18ByBip[bip] != null && CurrentBars[bip] >= 18)
                signal.Hma18 = _hma18ByBip[bip][0];
            if (_hma43ByBip != null && bip < _hma43ByBip.Length && _hma43ByBip[bip] != null && CurrentBars[bip] >= 43)
                signal.Hma43 = _hma43ByBip[bip][0];

            if (signal.Hma18.HasValue && signal.Hma43.HasValue)
                signal.HmaTrendAlignment = BuildHmaTrendAlignment(signal.Hma18.Value, signal.Hma43.Value);

            if (signal.Hma12.HasValue && signal.Atr > 1e-12)
                signal.PriceToHma12Distance = (signal.Close - signal.Hma12.Value) / signal.Atr;

            // Session ORB is owned by the 1-minute tracker; reuse across all TF rows.
            SessionTracker orbSession = TryGetPrimaryOrbSession() ?? session;
            if (orbSession != null)
            {
                signal.OrbHigh = orbSession.OrbHigh;
                signal.OrbLow = orbSession.OrbLow;
                signal.OrbState = BuildOrbState(signal.Close, orbSession.OrbHigh, orbSession.OrbLow, orbSession.OrbFrozen);
            }

            string candle;
            if (TryDetectReversalCandle(bip, out candle))
                signal.ReversalCandle = candle;

            string pivotState;
            if (TryResolvePivotBreakoutState(bip, signal.Close, out pivotState))
                signal.PivotBreakoutState = pivotState;
        }
    }
}
