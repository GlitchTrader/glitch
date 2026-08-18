using System;
using System.Collections.Generic;
using System.Linq;

namespace Glitch.Services
{
    internal sealed class GlitchAiDailyCaptureStopState
    {
        public string LegId { get; set; }
        public int Quantity { get; set; }
        public double StopPrice { get; set; }
    }

    internal sealed class GlitchAiDailyCaptureProtectionPlan
    {
        public double DesiredStopPrice { get; set; }
        public double ExecutionReserveUsd { get; set; }
        public double TotalPnl { get; set; }
        public IReadOnlyList<string> LegIds { get; set; }
    }

    internal static class GlitchAiDailyCaptureProtectionPlanner
    {
        // Four native ticks per remaining contract provide a bounded execution-cost
        // and stop-fill reserve without inventing an instrument-independent USD value.
        internal const int ExecutionReserveTicks = 4;

        internal static bool TryCreatePlan(
            double realizedPnl,
            double unrealizedPnl,
            double targetUsd,
            int signedQuantity,
            double averagePrice,
            double pointValue,
            double tickSize,
            IEnumerable<GlitchAiDailyCaptureStopState> workingStops,
            out GlitchAiDailyCaptureProtectionPlan plan)
        {
            plan = null;
            if (!IsFinite(realizedPnl)
                || !IsFinite(unrealizedPnl)
                || !IsFinitePositive(targetUsd)
                || signedQuantity == 0
                || signedQuantity == int.MinValue
                || !IsFinitePositive(averagePrice)
                || !IsFinitePositive(pointValue)
                || !IsFinitePositive(tickSize))
                return false;

            int quantity = Math.Abs(signedQuantity);
            List<GlitchAiDailyCaptureStopState> stops = (workingStops
                    ?? Enumerable.Empty<GlitchAiDailyCaptureStopState>())
                .Where(value => value != null
                    && !string.IsNullOrWhiteSpace(value.LegId)
                    && value.Quantity > 0
                    && IsFinitePositive(value.StopPrice))
                .ToList();
            if (stops.Count == 0
                || stops.Select(value => value.LegId).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    != stops.Count
                || stops.Sum(value => value.Quantity) != quantity)
                return false;

            double tickValue = pointValue * tickSize * quantity;
            double reserveUsd = tickValue * ExecutionReserveTicks;
            double totalPnl = realizedPnl + unrealizedPnl;
            // One additional tick of open cushion keeps the requested stop on the
            // protective market side when the latest native PnL is coherent.
            if (totalPnl + 0.0000001d < targetUsd + reserveUsd + tickValue)
                return false;

            double requiredOpenPnlAtStop = targetUsd + reserveUsd - realizedPnl;
            double rawStop = averagePrice
                + (Math.Sign(signedQuantity) * requiredOpenPnlAtStop / (quantity * pointValue));
            double tickUnits = rawStop / tickSize;
            double desiredStop = signedQuantity > 0
                ? Math.Ceiling(tickUnits - 0.000000001d) * tickSize
                : Math.Floor(tickUnits + 0.000000001d) * tickSize;
            desiredStop = Math.Round(desiredStop, 10, MidpointRounding.AwayFromZero);
            if (!IsFinitePositive(desiredStop))
                return false;

            double comparisonTolerance = tickSize / 1000d;
            string[] legIds = stops
                .Where(value => signedQuantity > 0
                    ? desiredStop > value.StopPrice + comparisonTolerance
                    : desiredStop < value.StopPrice - comparisonTolerance)
                .Select(value => value.LegId.Trim())
                .ToArray();
            if (legIds.Length == 0)
                return false;

            plan = new GlitchAiDailyCaptureProtectionPlan
            {
                DesiredStopPrice = desiredStop,
                ExecutionReserveUsd = reserveUsd,
                TotalPnl = totalPnl,
                LegIds = legIds
            };
            return true;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinitePositive(double value)
        {
            return IsFinite(value) && value > 0;
        }
    }
}
