//
// Deterministic replication math. This file has no NinjaTrader dependency so
// the exact quantity, close-bound, and request-count contracts can be replayed
// in a real C# harness.
//

using System;
using System.Collections.Generic;
using System.Linq;

namespace Glitch.Services
{
    internal sealed class GlitchExecutionAllocation
    {
        public int Quantity { get; set; }
        public int MasterCumulative { get; set; }
        public int FollowerCumulative { get; set; }
        public int FollowerOrderOffset { get; set; }
        public int FollowerOrderPlanQuantity { get; set; }
    }

    internal sealed class GlitchExecutionSplit
    {
        public int PreExecutionNet { get; set; }
        public int PostExecutionNet { get; set; }
        public int CloseQuantity { get; set; }
        public int OpenQuantity { get; set; }
        public int ExecutionSign { get; set; }
    }

    internal sealed class GlitchCumulativeAllocationBook
    {
        private readonly Dictionary<string, CumulativeState> _states =
            new Dictionary<string, CumulativeState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, EntryOrderState> _orders =
            new Dictionary<string, EntryOrderState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _routeSignatures =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool _enabled;

        public void Configure(
            bool enabled,
            IReadOnlyDictionary<string, string> routeSignatures)
        {
            var next = routeSignatures
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!enabled || !_enabled)
            {
                _states.Clear();
                _orders.Clear();
            }
            else
            {
                var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, string> existing in _routeSignatures)
                {
                    if (!next.TryGetValue(existing.Key, out string signature)
                        || !string.Equals(existing.Value, signature, StringComparison.Ordinal))
                        changed.Add(existing.Key);
                }
                foreach (KeyValuePair<string, string> candidate in next)
                {
                    if (!_routeSignatures.TryGetValue(candidate.Key, out string signature)
                        || !string.Equals(signature, candidate.Value, StringComparison.Ordinal))
                        changed.Add(candidate.Key);
                }
                if (changed.Count > 0)
                {
                    foreach (string key in _states
                        .Where(item => changed.Contains(item.Value.RouteKey))
                        .Select(item => item.Key)
                        .ToList())
                        _states.Remove(key);
                    foreach (string key in _orders
                        .Where(item => changed.Contains(item.Value.RouteKey))
                        .Select(item => item.Key)
                        .ToList())
                        _orders.Remove(key);
                }
            }

            _routeSignatures.Clear();
            foreach (KeyValuePair<string, string> signature in next)
                _routeSignatures[signature.Key] = signature.Value;
            _enabled = enabled;
        }

        public GlitchExecutionAllocation Allocate(
            string routeKey,
            string instrumentKey,
            string directionKey,
            int masterExecutionQuantity,
            double ratio,
            string nativeOrderIdentity,
            int nativeOrderPlannedQuantity,
            bool includeEntryOrderPlan,
            bool nativeOrderComplete = false)
        {
            var result = new GlitchExecutionAllocation();
            if (!_enabled
                || string.IsNullOrWhiteSpace(routeKey)
                || string.IsNullOrWhiteSpace(instrumentKey)
                || string.IsNullOrWhiteSpace(directionKey)
                || masterExecutionQuantity <= 0
                || ratio <= 0
                || double.IsNaN(ratio)
                || double.IsInfinity(ratio))
                return result;

            string stateKey = routeKey.Trim()
                + "|" + instrumentKey.Trim()
                + "|" + directionKey.Trim();
            if (!_states.TryGetValue(stateKey, out CumulativeState state))
            {
                state = new CumulativeState { RouteKey = routeKey.Trim() };
                _states[stateKey] = state;
            }

            EntryOrderState orderState = null;
            string orderKey = null;
            if (includeEntryOrderPlan && !string.IsNullOrWhiteSpace(nativeOrderIdentity))
            {
                orderKey = stateKey + "|" + nativeOrderIdentity.Trim();
                if (!_orders.TryGetValue(orderKey, out orderState))
                {
                    orderState = new EntryOrderState
                    {
                        RouteKey = routeKey.Trim(),
                        MasterBaseline = state.MasterQuantity,
                        FollowerBaseline = state.FollowerQuantity,
                        PlannedMasterQuantity = Math.Max(
                            masterExecutionQuantity,
                            nativeOrderPlannedQuantity)
                    };
                    _orders[orderKey] = orderState;
                }
                else
                {
                    orderState.PlannedMasterQuantity = Math.Max(
                        orderState.PlannedMasterQuantity,
                        nativeOrderPlannedQuantity);
                }
                result.FollowerOrderOffset = orderState.AllocatedFollowerQuantity;
            }

            state.MasterQuantity += masterExecutionQuantity;
            int targetFollower = GlitchReplicationMath.ScaleQuantity(
                state.MasterQuantity,
                ratio);
            result.Quantity = Math.Max(0, targetFollower - state.FollowerQuantity);
            state.FollowerQuantity = targetFollower;
            result.MasterCumulative = state.MasterQuantity;
            result.FollowerCumulative = state.FollowerQuantity;

            if (orderState != null)
            {
                orderState.AllocatedFollowerQuantity += result.Quantity;
                int plannedFollower = GlitchReplicationMath.ScaleQuantity(
                        orderState.MasterBaseline + orderState.PlannedMasterQuantity,
                        ratio)
                    - orderState.FollowerBaseline;
                result.FollowerOrderPlanQuantity = Math.Max(
                    orderState.AllocatedFollowerQuantity,
                    plannedFollower);
                if (nativeOrderComplete && orderKey != null)
                    _orders.Remove(orderKey);
            }
            else if (includeEntryOrderPlan)
            {
                result.FollowerOrderPlanQuantity = Math.Max(
                    result.Quantity,
                    GlitchReplicationMath.ScaleQuantity(
                        Math.Max(masterExecutionQuantity, nativeOrderPlannedQuantity),
                        ratio));
            }
            return result;
        }

        public void Reset()
        {
            _states.Clear();
            _orders.Clear();
            _routeSignatures.Clear();
            _enabled = false;
        }

        private sealed class CumulativeState
        {
            public string RouteKey { get; set; }
            public int MasterQuantity { get; set; }
            public int FollowerQuantity { get; set; }
        }

        private sealed class EntryOrderState
        {
            public string RouteKey { get; set; }
            public int MasterBaseline { get; set; }
            public int FollowerBaseline { get; set; }
            public int PlannedMasterQuantity { get; set; }
            public int AllocatedFollowerQuantity { get; set; }
        }
    }

    internal sealed class GlitchNativeMaintenanceGate
    {
        public const int MinimumIntervalMilliseconds = 250;
        private readonly Dictionary<string, DateTime> _nextAllowedUtc =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _cancelInFlightUtc =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public bool TryAcquire(string accountName, DateTime nowUtc)
        {
            string key = string.IsNullOrWhiteSpace(accountName)
                ? "Unknown"
                : accountName.Trim();
            DateTime normalized = nowUtc.Kind == DateTimeKind.Utc
                ? nowUtc
                : nowUtc.ToUniversalTime();
            if (_nextAllowedUtc.TryGetValue(key, out DateTime next)
                && normalized < next)
                return false;
            _nextAllowedUtc[key] = normalized.AddMilliseconds(
                MinimumIntervalMilliseconds);
            return true;
        }

        public bool TryAcquireCancel(
            string accountName,
            string cancelIdentity,
            DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(cancelIdentity)
                || _cancelInFlightUtc.ContainsKey(cancelIdentity)
                || !TryAcquire(accountName, nowUtc))
                return false;
            _cancelInFlightUtc[cancelIdentity] = nowUtc.Kind == DateTimeKind.Utc
                ? nowUtc
                : nowUtc.ToUniversalTime();
            return true;
        }

        public bool ObserveCancel(
            string cancelIdentity,
            DateTime nowUtc,
            bool nativeCancelPending,
            bool nativeOrderCancellable)
        {
            if (string.IsNullOrWhiteSpace(cancelIdentity)
                || !_cancelInFlightUtc.TryGetValue(cancelIdentity, out DateTime requestedUtc))
                return false;
            if (nativeCancelPending)
                return true;
            DateTime normalized = nowUtc.Kind == DateTimeKind.Utc
                ? nowUtc
                : nowUtc.ToUniversalTime();
            if (nativeOrderCancellable
                && (normalized - requestedUtc).TotalSeconds < 1)
                return true;
            _cancelInFlightUtc.Remove(cancelIdentity);
            return false;
        }

        public bool IsCancelInFlight(string cancelIdentity)
        {
            return !string.IsNullOrWhiteSpace(cancelIdentity)
                && _cancelInFlightUtc.ContainsKey(cancelIdentity);
        }

        public void ReleaseCancel(string cancelIdentity)
        {
            if (!string.IsNullOrWhiteSpace(cancelIdentity))
                _cancelInFlightUtc.Remove(cancelIdentity);
        }

        public void Reset()
        {
            _nextAllowedUtc.Clear();
            _cancelInFlightUtc.Clear();
        }
    }

    internal sealed class GlitchProtectionAmendmentGate
    {
        public double DesiredPrice { get; private set; }
        public double SubmittedPrice { get; private set; }
        public bool ChangeInFlight { get; private set; }

        public void SetDesired(double price)
        {
            DesiredPrice = price;
        }

        public bool Acknowledge(bool allNativeOrdersAtSubmittedPrice)
        {
            if (!ChangeInFlight || !allNativeOrdersAtSubmittedPrice)
                return false;
            ChangeInFlight = false;
            return true;
        }

        public bool TryBegin(
            bool allNativeOrdersChangeable,
            bool allNativeOrdersAtDesiredPrice,
            out double submittedPrice)
        {
            submittedPrice = 0;
            if (ChangeInFlight
                || !allNativeOrdersChangeable
                || allNativeOrdersAtDesiredPrice
                || DesiredPrice <= 0)
                return false;
            SubmittedPrice = DesiredPrice;
            ChangeInFlight = true;
            submittedPrice = SubmittedPrice;
            return true;
        }
    }

    internal static class GlitchReplicationMath
    {
        public static GlitchExecutionSplit SplitExecution(
            int postExecutionNet,
            int signedExecutionQuantity)
        {
            var split = new GlitchExecutionSplit
            {
                PostExecutionNet = postExecutionNet,
                ExecutionSign = Math.Sign(signedExecutionQuantity)
            };
            if (signedExecutionQuantity == 0)
                return split;

            split.PreExecutionNet = postExecutionNet - signedExecutionQuantity;
            int executionQuantity = Math.Abs(signedExecutionQuantity);
            int preSign = Math.Sign(split.PreExecutionNet);
            if (preSign != 0 && preSign != split.ExecutionSign)
            {
                split.CloseQuantity = Math.Min(
                    Math.Abs(split.PreExecutionNet),
                    executionQuantity);
            }
            split.OpenQuantity = Math.Max(0, executionQuantity - split.CloseQuantity);
            return split;
        }

        public static int ScaleQuantity(int masterQuantity, double ratio)
        {
            if (masterQuantity <= 0
                || ratio <= 0
                || double.IsNaN(ratio)
                || double.IsInfinity(ratio))
                return 0;
            return (int)Math.Round(
                masterQuantity * ratio,
                MidpointRounding.AwayFromZero);
        }

        public static List<T> SelectExactPrefix<T>(
            IEnumerable<T> orderedItems,
            int requiredQuantity,
            Func<T, int> quantitySelector)
        {
            var selected = new List<T>();
            if (orderedItems == null
                || requiredQuantity <= 0
                || quantitySelector == null)
                return selected;
            int total = 0;
            foreach (T item in orderedItems)
            {
                int quantity = Math.Max(0, quantitySelector(item));
                if (quantity <= 0 || total + quantity > requiredQuantity)
                    continue;
                selected.Add(item);
                total += quantity;
                if (total == requiredQuantity)
                    return selected;
            }
            selected.Clear();
            return selected;
        }

        public static int ScaleExecutionDelta(
            int nativeOrderFilledAfterExecution,
            int nativeExecutionQuantity,
            double ratio)
        {
            if (nativeOrderFilledAfterExecution <= 0
                || nativeExecutionQuantity <= 0
                || ratio <= 0
                || double.IsNaN(ratio)
                || double.IsInfinity(ratio))
                return 0;
            int filledAfter = Math.Max(
                nativeExecutionQuantity,
                nativeOrderFilledAfterExecution);
            int filledBefore = Math.Max(0, filledAfter - nativeExecutionQuantity);
            return Math.Max(
                0,
                ScaleQuantity(filledAfter, ratio)
                    - ScaleQuantity(filledBefore, ratio));
        }

        public static int BuildCloseTarget(
            int initialFollowerNet,
            int requestedCloseQuantity)
        {
            int bounded = Math.Min(
                Math.Abs(initialFollowerNet),
                Math.Max(0, requestedCloseQuantity));
            if (initialFollowerNet > 0)
                return initialFollowerNet - bounded;
            if (initialFollowerNet < 0)
                return initialFollowerNet + bounded;
            return 0;
        }

        public static int RemainingCloseQuantity(int actualFollowerNet, int targetFollowerNet)
        {
            if (targetFollowerNet >= 0 && actualFollowerNet > targetFollowerNet)
                return actualFollowerNet - targetFollowerNet;
            if (targetFollowerNet <= 0 && actualFollowerNet < targetFollowerNet)
                return targetFollowerNet - actualFollowerNet;
            return 0;
        }

        public static int RemainingAttributedCloseQuantity(
            int initialFollowerNet,
            int actualFollowerNet,
            int requestedCloseQuantity)
        {
            return RemainingAttributedCloseQuantity(
                initialFollowerNet,
                actualFollowerNet,
                requestedCloseQuantity,
                0);
        }

        public static int RemainingAttributedCloseQuantity(
            int initialFollowerNet,
            int actualFollowerNet,
            int requestedCloseQuantity,
            int ownedCloseFilledQuantity)
        {
            if (initialFollowerNet == 0
                || actualFollowerNet == 0
                || (initialFollowerNet > 0) != (actualFollowerNet > 0))
                return 0;

            int boundedRequest = Math.Min(
                Math.Abs(initialFollowerNet),
                Math.Max(0, requestedCloseQuantity));
            int ownedFilled = Math.Min(
                boundedRequest,
                Math.Max(0, ownedCloseFilledQuantity));
            int nativeReductionSinceRequest = Math.Max(
                0,
                Math.Abs(initialFollowerNet)
                    - (Math.Abs(actualFollowerNet) + ownedFilled));
            return Math.Min(
                Math.Abs(actualFollowerNet),
                Math.Max(
                    0,
                    boundedRequest - ownedFilled - nativeReductionSinceRequest));
        }

        public static int BuildAttributedCloseTarget(
            int initialFollowerNet,
            int actualFollowerNet,
            int requestedCloseQuantity)
        {
            return BuildAttributedCloseTarget(
                initialFollowerNet,
                actualFollowerNet,
                requestedCloseQuantity,
                0);
        }

        public static int BuildAttributedCloseTarget(
            int initialFollowerNet,
            int actualFollowerNet,
            int requestedCloseQuantity,
            int ownedCloseFilledQuantity)
        {
            int remaining = RemainingAttributedCloseQuantity(
                initialFollowerNet,
                actualFollowerNet,
                requestedCloseQuantity,
                ownedCloseFilledQuantity);
            if (actualFollowerNet > 0)
                return actualFollowerNet - remaining;
            if (actualFollowerNet < 0)
                return actualFollowerNet + remaining;
            return 0;
        }

    }
}
