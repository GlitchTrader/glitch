//
// Runtime-neutral replication protection.
// Resolves the master's live native OCO orders and scales them for a follower.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NinjaTrader.Cbi;

namespace Glitch.Services
{
    internal sealed class GlitchReplicationProtectionLeg
    {
        public int MasterQuantity { get; set; }
        public double StopPrice { get; set; }
        public double TargetPrice { get; set; }
        public string SourceToken { get; set; }
    }

    internal sealed class GlitchReplicationProtectionPlan
    {
        public bool IsLong { get; set; }
        public int MasterQuantity { get; set; }
        public double TickSize { get; set; }
        public List<GlitchReplicationProtectionLeg> Legs { get; set; } =
            new List<GlitchReplicationProtectionLeg>();
    }

    internal sealed class GlitchScaledProtectionLeg
    {
        public int Quantity { get; set; }
        public double StopPrice { get; set; }
        public double TargetPrice { get; set; }
        public string SourceToken { get; set; }
    }

    internal static class GlitchReplicationProtection
    {
        public static bool TryResolveMasterPlan(
            Account masterAccount,
            Instrument instrument,
            string entrySignal,
            int requiredMasterQuantity,
            bool isLong,
            out GlitchReplicationProtectionPlan plan)
        {
            plan = null;
            if (masterAccount == null || instrument == null || requiredMasterQuantity <= 0)
                return false;

            string instrumentName = instrument.FullName?.Trim() ?? string.Empty;
            OrderAction exitAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
            Order[] orders = SnapshotOrders(masterAccount);
            IEnumerable<Order> candidates = orders.Where(order =>
                order != null
                && order.Instrument != null
                && string.Equals(
                    order.Instrument.FullName,
                    instrumentName,
                    StringComparison.OrdinalIgnoreCase)
                && order.OrderAction == exitAction
                && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                && !string.IsNullOrWhiteSpace(order.Oco));

            string signalCorrelation = TryGetSignalCorrelation(entrySignal, "E");
            if (!string.IsNullOrWhiteSpace(signalCorrelation))
            {
                candidates = candidates.Where(order =>
                    string.Equals(TryGetSignalCorrelation(order.Name, "S"), signalCorrelation, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(TryGetSignalCorrelation(order.Name, "T"), signalCorrelation, StringComparison.OrdinalIgnoreCase));
            }
            else if (!string.IsNullOrWhiteSpace(entrySignal))
            {
                List<Order> linked = candidates
                    .Where(order => string.Equals(
                        TryReadOrderLinkSignal(order),
                        entrySignal.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (linked.Count > 0)
                    candidates = linked;
                else if (!CanUseFullPositionPlan(
                    masterAccount,
                    instrument,
                    requiredMasterQuantity,
                    isLong))
                    return false;
            }

            var legs = new List<GlitchReplicationProtectionLeg>();
            foreach (IGrouping<string, Order> ocoGroup in candidates.GroupBy(
                order => order.Oco.Trim(),
                StringComparer.OrdinalIgnoreCase))
            {
                List<Order> stops = ocoGroup.Where(GlitchReplicationEngine.IsStopLikeOrder).ToList();
                List<Order> targets = ocoGroup.Where(order => order.OrderType == OrderType.Limit).ToList();
                if (stops.Count != 1 || targets.Count != 1)
                    continue;

                Order stop = stops[0];
                Order target = targets[0];
                int quantity = Math.Min(RemainingQuantity(stop), RemainingQuantity(target));
                if (quantity <= 0 || stop.StopPrice <= 0 || target.LimitPrice <= 0)
                    continue;
                if (isLong && (stop.StopPrice >= target.LimitPrice))
                    continue;
                if (!isLong && (stop.StopPrice <= target.LimitPrice))
                    continue;

                legs.Add(new GlitchReplicationProtectionLeg
                {
                    MasterQuantity = quantity,
                    StopPrice = stop.StopPrice,
                    TargetPrice = target.LimitPrice,
                    SourceToken = BuildSourceToken(stop.Name, stop.Oco)
                });
            }

            int totalQuantity = legs.Sum(leg => leg.MasterQuantity);
            if (totalQuantity != requiredMasterQuantity)
                return false;

            legs = isLong
                ? legs.OrderBy(leg => leg.TargetPrice).ToList()
                : legs.OrderByDescending(leg => leg.TargetPrice).ToList();

            plan = new GlitchReplicationProtectionPlan
            {
                IsLong = isLong,
                MasterQuantity = totalQuantity,
                TickSize = instrument.MasterInstrument?.TickSize ?? 0,
                Legs = legs
            };
            return true;
        }

        public static bool TryScalePlan(
            GlitchReplicationProtectionPlan plan,
            int followerQuantity,
            out List<GlitchScaledProtectionLeg> scaled)
        {
            scaled = new List<GlitchScaledProtectionLeg>();
            if (plan == null || plan.MasterQuantity <= 0 || followerQuantity <= 0 || plan.Legs == null || plan.Legs.Count == 0)
                return false;

            var allocations = new List<Allocation>();
            int allocated = 0;
            for (int i = 0; i < plan.Legs.Count; i++)
            {
                GlitchReplicationProtectionLeg leg = plan.Legs[i];
                double exact = followerQuantity * (leg.MasterQuantity / (double)plan.MasterQuantity);
                int whole = Math.Max(0, (int)Math.Floor(exact));
                allocations.Add(new Allocation { Index = i, Quantity = whole, Remainder = exact - whole });
                allocated += whole;
            }

            int remainder = followerQuantity - allocated;
            foreach (Allocation allocation in allocations
                .OrderByDescending(item => item.Remainder)
                .ThenBy(item => item.Index))
            {
                if (remainder <= 0)
                    break;
                allocation.Quantity++;
                remainder--;
            }

            if (remainder != 0)
                return false;

            foreach (Allocation allocation in allocations.OrderBy(item => item.Index))
            {
                if (allocation.Quantity <= 0)
                    continue;
                GlitchReplicationProtectionLeg source = plan.Legs[allocation.Index];
                scaled.Add(new GlitchScaledProtectionLeg
                {
                    Quantity = allocation.Quantity,
                    StopPrice = source.StopPrice,
                    TargetPrice = source.TargetPrice,
                    SourceToken = source.SourceToken
                });
            }

            return scaled.Sum(leg => leg.Quantity) == followerQuantity;
        }

        public static int ScaleFollowerQuantity(int masterQuantity, double ratio)
        {
            if (masterQuantity <= 0 || ratio <= 0 || double.IsNaN(ratio) || double.IsInfinity(ratio))
                return 0;
            return (int)Math.Round(masterQuantity * ratio, MidpointRounding.AwayFromZero);
        }

        public static bool TryScalePlanSlice(
            GlitchReplicationProtectionPlan plan,
            double ratio,
            int followerAllocationOffset,
            int followerQuantity,
            out List<GlitchScaledProtectionLeg> scaled)
        {
            scaled = new List<GlitchScaledProtectionLeg>();
            if (plan == null || followerAllocationOffset < 0 || followerQuantity <= 0)
                return false;

            int aggregateFollowerQuantity = ScaleFollowerQuantity(plan.MasterQuantity, ratio);
            if (aggregateFollowerQuantity <= 0
                || followerAllocationOffset > aggregateFollowerQuantity - followerQuantity
                || !TryScalePlan(plan, aggregateFollowerQuantity, out List<GlitchScaledProtectionLeg> aggregate))
                return false;

            int sliceEnd = followerAllocationOffset + followerQuantity;
            int cursor = 0;
            foreach (GlitchScaledProtectionLeg source in aggregate)
            {
                int sourceStart = cursor;
                int sourceEnd = sourceStart + Math.Max(0, source.Quantity);
                int overlap = Math.Max(
                    0,
                    Math.Min(sliceEnd, sourceEnd) - Math.Max(followerAllocationOffset, sourceStart));
                if (overlap > 0)
                {
                    scaled.Add(new GlitchScaledProtectionLeg
                    {
                        Quantity = overlap,
                        StopPrice = source.StopPrice,
                        TargetPrice = source.TargetPrice,
                        SourceToken = source.SourceToken
                    });
                }
                cursor = sourceEnd;
            }

            return scaled.Sum(leg => leg.Quantity) == followerQuantity;
        }

        public static bool IsMasterProtectionExecution(GlitchCopyExecutionContext context)
        {
            if (context == null)
                return false;
            if (!string.IsNullOrWhiteSpace(TryGetSignalCorrelation(context.OrderSignalName, "S"))
                || !string.IsNullOrWhiteSpace(TryGetSignalCorrelation(context.OrderSignalName, "T")))
                return true;

            return !string.IsNullOrWhiteSpace(context.Oco)
                && (context.OrderType == OrderType.Limit
                    || context.OrderType.ToString().IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static string BuildSourceToken(string orderName, string oco)
        {
            // The OCO identifies one native stop/target pair. A trade correlation
            // can contain several independent legs, so it is too broad for stop
            // mirroring and would make one master-leg change move every follower leg.
            string source = !string.IsNullOrWhiteSpace(oco)
                ? oco.Trim()
                : orderName;
            return StableToken(source, 8);
        }

        public static string StableToken(string value, int length)
        {
            uint hash = 2166136261;
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            foreach (byte item in bytes)
            {
                hash ^= item;
                hash *= 16777619;
            }
            string token = hash.ToString("x8", CultureInfo.InvariantCulture);
            return token.Substring(0, Math.Max(1, Math.Min(token.Length, length)));
        }

        private static bool CanUseFullPositionPlan(
            Account masterAccount,
            Instrument instrument,
            int requiredMasterQuantity,
            bool isLong)
        {
            if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(
                masterAccount,
                instrument,
                out int masterNet))
                return false;
            if (Math.Abs(masterNet) != requiredMasterQuantity)
                return false;
            return masterNet != 0 && (masterNet > 0) == isLong;
        }

        private static string TryGetSignalCorrelation(string signalName, string role)
        {
            if (string.IsNullOrWhiteSpace(signalName) || string.IsNullOrWhiteSpace(role))
                return null;
            string[] segments = signalName.Trim().Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 5
                || !string.Equals(segments[0], "GLT", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(segments[2], role, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(segments[1])
                || string.IsNullOrWhiteSpace(segments[3]))
                return null;
            return segments[1] + "|" + segments[3];
        }

        private static int RemainingQuantity(Order order)
        {
            if (order == null)
                return 0;
            return Math.Max(0, Math.Abs(order.Quantity) - Math.Max(0, order.Filled));
        }

        private static Order[] SnapshotOrders(Account account)
        {
            try
            {
                if (account?.Orders == null)
                    return Array.Empty<Order>();
                lock (account.Orders)
                    return account.Orders.ToArray();
            }
            catch
            {
                return Array.Empty<Order>();
            }
        }

        private static string TryReadOrderLinkSignal(Order order)
        {
            if (order == null)
                return null;
            foreach (string propertyName in new[] { "FromEntrySignal", "EntrySignal", "EntryName" })
            {
                try
                {
                    object value = order.GetType().GetProperty(propertyName)?.GetValue(order, null);
                    if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                        return value.ToString().Trim();
                }
                catch
                {
                }
            }
            return null;
        }

        private sealed class Allocation
        {
            public int Index { get; set; }
            public int Quantity { get; set; }
            public double Remainder { get; set; }
        }
    }
}
