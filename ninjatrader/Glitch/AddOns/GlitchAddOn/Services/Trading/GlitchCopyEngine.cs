//
// Master execution fan-out. This engine owns routes, ratios, follower entries
// and follower protection, independent of whichever producer trades the master.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NinjaTrader.Cbi;

namespace Glitch.Services
{
    public sealed class GlitchCopyExecutionContext
    {
        public string ExecutionId { get; set; }
        public DateTime ExecutionTimeUtc { get; set; }
        public Instrument Instrument { get; set; }
        public OrderAction Action { get; set; }
        public OrderType OrderType { get; set; }
        public int Quantity { get; set; }
        public int EntryOrderFilledQuantity { get; set; }
        public int MasterNetAfterExecution { get; set; }
        public string OrderSignalName { get; set; }
        public string Oco { get; set; }
    }

    public sealed class GlitchCopyFollowerRoute
    {
        public string MasterAccount { get; set; }
        public Account MasterAccountInstance { get; set; }
        public Account FollowerAccount { get; set; }
        public double Ratio { get; set; }
        public int MaxContracts { get; set; }
        public int MaxMicroContracts { get; set; }
        public string MicroContractRootRegex { get; set; }
    }

    public sealed class GlitchCopyEngine
    {
        public const string CopySignalName = "GLT-COPY";
        public const string CatchUpSignalName = "GLT-CATCHUP";
        private static readonly TimeSpan PendingMasterCopyTtl = TimeSpan.FromSeconds(45);
        private static int _ocoNonce;

        private readonly object _gate = new object();
        private readonly LinkedList<string> _seenExecutionIds = new LinkedList<string>();
        private readonly HashSet<string> _seenExecutionIdSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<GlitchCopyFollowerRoute>> _routesByMaster =
            new Dictionary<string, List<GlitchCopyFollowerRoute>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PendingMasterCopy> _pendingMasterCopies =
            new Dictionary<string, PendingMasterCopy>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FollowerEntryLifecycle> _entriesBySignal =
            new Dictionary<string, FollowerEntryLifecycle>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _flattenSubmitted =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _suppressedFollowerRoots =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _enabled;

        public Action<string, string> Journal { get; set; }
        public Action<string, string, string> RaiseCritical { get; set; }

        public bool IsEnabled
        {
            get { lock (_gate) return _enabled; }
        }

        public int ActiveRouteCount
        {
            get { lock (_gate) return _routesByMaster.Values.Sum(routes => routes.Count); }
        }

        public int GetActiveRouteCount(string masterAccount)
        {
            lock (_gate)
            {
                return !string.IsNullOrWhiteSpace(masterAccount)
                    && _routesByMaster.TryGetValue(masterAccount.Trim(), out List<GlitchCopyFollowerRoute> routes)
                    ? routes.Count
                    : 0;
            }
        }

        public string GetEntryDenialReason(
            Account masterAccount,
            Instrument instrument,
            OrderAction action,
            int masterQuantity)
        {
            if (masterAccount == null || instrument == null || masterQuantity <= 0 || !IsOpeningAction(action))
                return "invalid_master_entry";
            if (!TryGetRoutes(masterAccount.Name, out List<GlitchCopyFollowerRoute> routes))
                return null;

            string root = GlitchReplicationEngine.GetInstrumentRoot(instrument);
            int direction = action == OrderAction.Buy ? 1 : -1;
            if (!GlitchReplicationEngine.TryGetNetQuantityForInstrumentRoot(masterAccount, root, out int masterNet))
                return "master_positions_unavailable|account=" + CleanToken(masterAccount.Name);
            if (masterNet != 0 && Math.Sign(masterNet) != direction)
                return "master_direction_conflict|account=" + CleanToken(masterAccount.Name);
            int projectedMaster = masterNet + (direction * masterQuantity);
            foreach (GlitchCopyFollowerRoute route in routes)
            {
                Account follower = route.FollowerAccount;
                if (IsFollowerRootSuppressed(follower, root))
                    return "follower_requires_explicit_resync|account=" + CleanToken(follower.Name);
                if (!GlitchReplicationEngine.TryGetNetQuantityForInstrumentRoot(follower, root, out int actual))
                    return "follower_positions_unavailable|account=" + CleanToken(follower.Name);
                if (actual != 0 && Math.Sign(actual) != direction)
                    return "follower_direction_conflict|account=" + CleanToken(follower.Name);

                int expected = (int)Math.Round(
                    Math.Abs(projectedMaster) * route.Ratio,
                    MidpointRounding.AwayFromZero);
                if (expected <= 0)
                    return "follower_quantity_rounds_to_zero|account=" + CleanToken(follower.Name);

                if (!TryGetInFlightOpeningQuantity(follower, root, direction, out int inFlight)
                    || !TryGetTotalInFlightOpeningQuantity(follower, out int totalInFlight)
                    || !TryGetTotalOpenContracts(follower, out int totalOpen))
                    return "follower_state_unavailable|account=" + CleanToken(follower.Name);
                int expectedCurrent = masterNet == 0
                    ? 0
                    : (int)Math.Round(Math.Abs(masterNet) * route.Ratio, MidpointRounding.AwayFromZero);
                if (Math.Abs(actual) + inFlight != expectedCurrent)
                    return "follower_requires_explicit_resync|account=" + CleanToken(follower.Name)
                        + "|actual=" + Math.Abs(actual).ToString(CultureInfo.InvariantCulture)
                        + "|inflight=" + inFlight.ToString(CultureInfo.InvariantCulture)
                        + "|expected=" + expectedCurrent.ToString(CultureInfo.InvariantCulture);
                int needed = Math.Max(0, expected - Math.Abs(actual) - inFlight);
                if (!TryResolveRouteContractCap(route, root, out int cap))
                    return "follower_contract_cap_unavailable|account=" + CleanToken(follower.Name);
                int projected = totalOpen + totalInFlight + needed;
                if (projected > cap)
                    return "follower_max_contracts|account=" + CleanToken(follower.Name)
                        + "|projected=" + projected.ToString(CultureInfo.InvariantCulture)
                        + "|cap=" + cap.ToString(CultureInfo.InvariantCulture);
            }

            return null;
        }

        public void Configure(bool enabled, IReadOnlyList<GlitchCopyFollowerRoute> routes)
        {
            lock (_gate)
            {
                _routesByMaster.Clear();
                foreach (GlitchCopyFollowerRoute route in routes ?? Array.Empty<GlitchCopyFollowerRoute>())
                {
                    if (!IsValidRoute(route))
                        continue;
                    string masterName = route.MasterAccount.Trim();
                    string followerName = route.FollowerAccount.Name?.Trim();
                    if (string.Equals(masterName, followerName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!_routesByMaster.TryGetValue(masterName, out List<GlitchCopyFollowerRoute> bucket))
                    {
                        bucket = new List<GlitchCopyFollowerRoute>();
                        _routesByMaster[masterName] = bucket;
                    }
                    if (!bucket.Any(item => string.Equals(
                        item.FollowerAccount?.Name,
                        route.FollowerAccount.Name,
                        StringComparison.OrdinalIgnoreCase)))
                        bucket.Add(route);
                }

                _enabled = enabled && _routesByMaster.Values.Any(bucket => bucket.Count > 0);
                var activeFollowerRoots = new HashSet<string>(
                    _routesByMaster.Values
                        .SelectMany(bucket => bucket)
                        .Select(route => (route.FollowerAccount?.Name?.Trim() ?? string.Empty) + "|"),
                    StringComparer.OrdinalIgnoreCase);
                foreach (string key in _suppressedFollowerRoots
                    .Where(key => !activeFollowerRoots.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    .ToList())
                    _suppressedFollowerRoots.Remove(key);
                if (!_enabled)
                    _pendingMasterCopies.Clear();
            }
        }

        public void ProcessMasterExecution(Account masterAccount, GlitchCopyExecutionContext context)
        {
            if (masterAccount == null || context?.Instrument == null || context.Quantity <= 0)
                return;
            if (IsFollowerOwnedSignal(context.OrderSignalName))
                return;
            if (!TryGetRoutes(masterAccount.Name, out List<GlitchCopyFollowerRoute> routes))
                return;

            if (GlitchReplicationProtection.IsMasterProtectionExecution(context))
            {
                Journal?.Invoke(masterAccount.Name, "copy_skip|reason=master_native_bracket_owns_exit");
                return;
            }

            if (!IsOpeningAction(context.Action))
            {
                string closeKey = BuildExecutionDedupKey(masterAccount.Name, context);
                if (TryRememberExecutionId(closeKey))
                    FanOutCompleteClose(masterAccount, context, routes, closeKey);
                return;
            }

            bool isLong = context.Action == OrderAction.Buy;
            if (!GlitchApexDirectionGuard.TryApproveEntry(
                masterAccount,
                context.Instrument,
                isLong ? 1 : -1,
                out string directionFailure))
            {
                string directionRoot = GlitchReplicationEngine.GetInstrumentRoot(context.Instrument);
                Journal?.Invoke(masterAccount.Name,
                    "master_entry_not_replicated|reason=apex_direction_compliance|instrument="
                    + CleanToken(directionRoot) + "|detail=" + CleanToken(directionFailure));
                RaiseCritical?.Invoke(
                    masterAccount.Name,
                    "A manual/native master entry conflicts with Apex portfolio direction. Glitch preserved the human position but refused to replicate it.",
                    "ApexCrossDirectionDetected|" + directionRoot);
                return;
            }
            int copyMasterQuantity = ResolveContextMasterQuantity(context);
            string root = GlitchReplicationEngine.GetInstrumentRoot(context.Instrument);
            if (!GlitchReplicationEngine.TryGetNetQuantityForInstrumentRoot(masterAccount, root, out int currentMasterNet))
            {
                RaiseCritical?.Invoke(masterAccount.Name,
                    "Master position state is unavailable; followers were left unchanged.",
                    "MasterPositionsUnavailable|" + root);
                return;
            }
            context.MasterNetAfterExecution = currentMasterNet;
            if (Math.Abs(currentMasterNet) < copyMasterQuantity
                || !GlitchReplicationProtection.TryResolveMasterPlan(
                masterAccount,
                context.Instrument,
                context.OrderSignalName,
                copyMasterQuantity,
                isLong,
                out GlitchReplicationProtectionPlan plan))
            {
                string pendingKey = BuildPendingMasterCopyKey(masterAccount, context);
                lock (_gate)
                {
                    _pendingMasterCopies[pendingKey] = new PendingMasterCopy
                    {
                        MasterAccount = masterAccount,
                        Context = CloneContext(context),
                        CreatedUtc = DateTime.UtcNow
                    };
                }
                Journal?.Invoke(masterAccount.Name,
                    "copy_wait|reason=master_bracket_not_working|instrument="
                    + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(context.Instrument)));
                return;
            }

            FanOutOpening(masterAccount, context, routes, plan);
        }

        public void ProcessMasterOrderUpdate(Account masterAccount, Order order)
        {
            if (masterAccount == null || order == null)
                return;
            TryReleasePendingMasterCopies(masterAccount);
            MirrorMasterStop(masterAccount, order);
        }

        public void ProcessFollowerOrderUpdate(Account followerAccount, Order order)
        {
            if (followerAccount == null || order?.Instrument == null || string.IsNullOrWhiteSpace(order.Name))
                return;
            string signal = order.Name.Trim();
            if (IsFollowerProtectionSignal(signal))
            {
                ProcessFollowerProtectionOrderUpdate(followerAccount, order, signal);
                return;
            }
            if (!IsFollowerEntrySignal(signal))
                return;

            FollowerEntryLifecycle lifecycle;
            lock (_gate)
                _entriesBySignal.TryGetValue(signal, out lifecycle);

            if (lifecycle == null)
            {
                if (order.Filled <= 0)
                    return;

                if (!IsRecentOrder(order, TimeSpan.FromMinutes(2))
                    || !TryRecoverRecentFollowerLifecycle(followerAccount, order, signal, out lifecycle))
                {
                    RaiseCritical?.Invoke(
                        followerAccount.Name,
                        "A Glitch-owned follower entry has no recoverable native protection. Existing orders were not changed.",
                        "FollowerProtectionRecoveryRequired|" + GlitchReplicationEngine.GetInstrumentRoot(order.Instrument));
                    return;
                }

                lock (_gate)
                    _entriesBySignal[signal] = lifecycle;
            }

            int protectFrom;
            int protectTo;
            lock (_gate)
            {
                if (lifecycle.ProtectionSubmissionInProgress || lifecycle.ProtectionFailed)
                    return;
                protectFrom = lifecycle.ProtectedQuantity;
                protectTo = Math.Max(0, order.Filled);
                if (protectTo > protectFrom)
                    lifecycle.ProtectionSubmissionInProgress = true;
            }

            if (protectTo > protectFrom)
            {
                if (!SubmitProtectionUnits(lifecycle, protectFrom, protectTo, out string failure))
                {
                    bool firstFailure;
                    lock (_gate)
                    {
                        firstFailure = !lifecycle.ProtectionFailed;
                        lifecycle.ProtectionSubmissionInProgress = false;
                        lifecycle.ProtectionFailed = true;
                    }
                    if (!firstFailure)
                        return;
                    RequestFollowerFlattenOnce(followerAccount, order.Instrument, "protection_submit_failed");
                    RaiseCritical?.Invoke(
                        followerAccount.Name,
                        "Follower entry filled but native protection failed: " + failure,
                        "FollowerProtectionFailed|" + GlitchReplicationEngine.GetInstrumentRoot(order.Instrument));
                    return;
                }

                lock (_gate)
                {
                    lifecycle.ProtectedQuantity = protectTo;
                    lifecycle.ProtectionSubmissionInProgress = false;
                }

                Journal?.Invoke(followerAccount.Name,
                    "follower_protection|entry=" + CleanToken(signal)
                    + "|protected_qty=" + protectTo.ToString(CultureInfo.InvariantCulture)
                    + "|result=submitted");

                // NT can deliver later partial fills while the first protection
                // submission is in progress. The Order instance carries the latest
                // aggregate fill, so drain that delta immediately instead of waiting
                // for an event that may already have been coalesced.
                if (Math.Max(0, order.Filled) > protectTo)
                    ProcessFollowerOrderUpdate(followerAccount, order);
            }

            if ((order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled)
                && order.Filled <= 0)
            {
                lock (_gate)
                    _entriesBySignal.Remove(signal);
                RaiseCritical?.Invoke(
                    followerAccount.Name,
                    "Follower entry was rejected or cancelled. Glitch did not retry an ambiguous order.",
                    "FollowerEntryRejected|" + GlitchReplicationEngine.GetInstrumentRoot(order.Instrument));
            }
        }

        private void ProcessFollowerProtectionOrderUpdate(Account followerAccount, Order order, string signal)
        {
            if (order.OrderState != OrderState.Rejected)
                return;

            string root = GlitchReplicationEngine.GetInstrumentRoot(order.Instrument);
            FollowerEntryLifecycle lifecycle;
            lock (_gate)
            {
                lifecycle = _entriesBySignal.Values.FirstOrDefault(item =>
                    item?.Account != null
                    && string.Equals(item.Account.Name, followerAccount.Name, StringComparison.OrdinalIgnoreCase)
                    && item.Instrument != null
                    && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(item.Instrument), root, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.EntryToken)
                    && signal.IndexOf("-" + item.EntryToken + "-", StringComparison.OrdinalIgnoreCase) >= 0);
                if (lifecycle != null)
                {
                    if (lifecycle.ProtectionFailed)
                        return;
                    lifecycle.ProtectionSubmissionInProgress = false;
                    lifecycle.ProtectionFailed = true;
                }
            }

            // Rejection is broker/NT evidence that a Glitch-owned protective leg
            // is not live. Cancellation is not equivalent: it can be a normal OCO
            // transition or an explicit human action and is deliberately preserved.
            if (GlitchReplicationEngine.TryGetNetQuantityForInstrumentRoot(followerAccount, root, out int followerNet)
                && followerNet == 0)
                return;

            RequestFollowerFlattenOnce(followerAccount, order.Instrument, "protection_order_rejected");
            RaiseCritical?.Invoke(
                followerAccount.Name,
                "A Glitch-owned follower stop or target was rejected; one native flatten was submitted and no order was retried.",
                "FollowerProtectionRejected|" + root + "|" + CleanToken(lifecycle?.EntrySignal ?? signal));
        }

        public void ProcessAccountStateUpdate(Account account)
        {
            if (account == null)
                return;
            TryReleasePendingMasterCopies(account);
            CleanupFlatFollowerOrders(account);
        }

        public void AlignFollowerToMaster(Account masterAccount, Account followerAccount, double ratio, string origin)
        {
            if (!IsEnabled || masterAccount == null || followerAccount == null || ratio <= 0
                || double.IsNaN(ratio) || double.IsInfinity(ratio))
                return;

            GlitchCopyFollowerRoute configuredRoute = FindConfiguredRoute(masterAccount, followerAccount);
            if (configuredRoute == null)
            {
                RaiseCritical?.Invoke(
                    followerAccount.Name,
                    "Explicit resync has no active configured route; no order was submitted.",
                    "CatchUpRouteUnavailable");
                return;
            }
            ratio = configuredRoute.Ratio;
            ClearFollowerSuppressions(followerAccount);
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            GlitchReplicationEngine.CollectPositionInstrumentRoots(masterAccount, roots);
            GlitchReplicationEngine.CollectPositionInstrumentRoots(followerAccount, roots);
            foreach (string root in roots)
            {
                if (!GlitchReplicationEngine.TryGetNetQuantityForInstrumentRoot(masterAccount, root, out int masterNet)
                    || !GlitchReplicationEngine.TryGetNetQuantityForInstrumentRoot(followerAccount, root, out int actual))
                {
                    RaiseCritical?.Invoke(
                        followerAccount.Name,
                        "Explicit resync could not verify native position state; no order was submitted.",
                        "CatchUpStateUnavailable|" + root);
                    continue;
                }

                ClearFollowerRootSuppression(followerAccount, root);
                int expected = (int)Math.Round(masterNet * ratio, MidpointRounding.AwayFromZero);
                if (expected == actual)
                    continue;

                Instrument instrument = GlitchReplicationEngine.FindInstrumentForInstrumentRoot(masterAccount, root)
                    ?? GlitchReplicationEngine.FindInstrumentForInstrumentRoot(followerAccount, root);
                if (instrument == null)
                    continue;

                if (expected == 0 || (actual != 0 && Math.Sign(actual) != Math.Sign(expected))
                    || Math.Abs(actual) > Math.Abs(expected))
                {
                    RequestFollowerFlattenOnce(followerAccount, instrument, "explicit_resync");
                    Journal?.Invoke(followerAccount.Name,
                        "catchup|origin=" + CleanToken(origin)
                        + "|result=follower_flatten_submitted|actual=" + actual
                        + "|expected=" + expected);
                    continue;
                }

                int quantity = Math.Abs(expected) - Math.Abs(actual);
                if (quantity <= 0)
                    continue;
                bool isLong = expected > 0;
                if (!GlitchReplicationProtection.TryResolveMasterPlan(
                    masterAccount,
                    instrument,
                    null,
                    Math.Abs(masterNet),
                    isLong,
                    out GlitchReplicationProtectionPlan plan))
                {
                    RaiseCritical?.Invoke(
                        followerAccount.Name,
                        "Follower could not catch up because the master has no complete native bracket.",
                        "CatchUpProtectionMissing|" + root);
                    continue;
                }

                SubmitProtectedFollowerEntry(
                    configuredRoute,
                    instrument,
                    isLong ? OrderAction.Buy : OrderAction.SellShort,
                    quantity,
                    plan,
                    CatchUpSignalName,
                    "sync" + GlitchReplicationProtection.StableToken(root + origin, 8));
            }
        }

        private void FanOutOpening(
            Account masterAccount,
            GlitchCopyExecutionContext context,
            IReadOnlyList<GlitchCopyFollowerRoute> routes,
            GlitchReplicationProtectionPlan plan)
        {
            string dedupKey = BuildExecutionDedupKey(masterAccount.Name, context);
            if (!TryRememberExecutionId(dedupKey))
                return;

            lock (_gate)
                _pendingMasterCopies.Remove(BuildPendingMasterCopyKey(masterAccount, context));

            string root = GlitchReplicationEngine.GetInstrumentRoot(context.Instrument);
            int direction = context.Action == OrderAction.Buy ? 1 : -1;
            if (!GlitchReplicationEngine.TryGetNetQuantityForInstrumentRoot(masterAccount, root, out int masterNet))
            {
                RaiseCritical?.Invoke(masterAccount.Name,
                    "Master position state is unavailable; followers were left unchanged.",
                    "MasterPositionsUnavailable|" + root);
                return;
            }
            int masterNetAtExecution = context.MasterNetAfterExecution != 0
                && Math.Sign(context.MasterNetAfterExecution) == direction
                ? context.MasterNetAfterExecution
                : masterNet;
            int masterBeforeFill = masterNetAtExecution - (direction * Math.Max(0, context.Quantity));
            foreach (GlitchCopyFollowerRoute route in routes)
            {
                Account follower = route.FollowerAccount;
                if (IsFollowerRootSuppressed(follower, root))
                {
                    JournalCopy(route, context, 0, "copy_skip|explicit_resync_required");
                    continue;
                }
                if (!GlitchReplicationEngine.TryGetNetQuantityForInstrumentRoot(follower, root, out int actual)
                    || !TryGetInFlightOpeningQuantity(follower, root, direction, out int inFlight)
                    || !TryGetTotalInFlightOpeningQuantity(follower, out int totalInFlight)
                    || !TryGetTotalOpenContracts(follower, out int totalOpen))
                {
                    JournalCopy(route, context, 0, "copy_reject|native_state_unavailable");
                    RaiseCritical?.Invoke(
                        follower.Name,
                        "Follower position or order state is unavailable; no copy order was submitted.",
                        "FollowerStateUnavailable|" + root);
                    continue;
                }
                if (actual != 0 && Math.Sign(actual) != direction)
                {
                    RaiseCritical?.Invoke(
                        follower.Name,
                        "Follower is opposite the master. Glitch preserved the account and refused to cross zero.",
                        "FollowerDirectionConflict|" + root);
                    continue;
                }

                int expectedBeforeFill = masterBeforeFill == 0 || Math.Sign(masterBeforeFill) != direction
                    ? 0
                    : (int)Math.Round(Math.Abs(masterBeforeFill) * route.Ratio, MidpointRounding.AwayFromZero);
                if (Math.Abs(actual) + inFlight != expectedBeforeFill)
                {
                    SuppressFollowerRoot(follower, root);
                    JournalCopy(route, context, 0,
                        "copy_skip|manual_or_external_divergence|actual=" + Math.Abs(actual)
                        + "|inflight=" + inFlight
                        + "|expected_before=" + expectedBeforeFill);
                    RaiseCritical?.Invoke(
                        follower.Name,
                        "Follower exposure changed outside replication. Glitch will not reopen it until Replicate is explicitly resynchronized.",
                        "FollowerExplicitResyncRequired|" + root);
                    continue;
                }

                int requested = (int)Math.Round(
                    ResolveContextMasterQuantity(context) * route.Ratio,
                    MidpointRounding.AwayFromZero);
                int expected = masterNet == 0 || Math.Sign(masterNet) != direction
                    ? Math.Abs(actual) + requested
                    : (int)Math.Round(Math.Abs(masterNet) * route.Ratio, MidpointRounding.AwayFromZero);
                int needed = Math.Max(0, expected - Math.Abs(actual) - inFlight);
                int quantity = Math.Min(requested, needed);
                if (quantity <= 0)
                {
                    JournalCopy(route, context, 0, "copy_skip|already_aligned_or_inflight");
                    continue;
                }

                if (!TryResolveRouteContractCap(route, root, out int cap))
                {
                    JournalCopy(route, context, 0, "copy_reject|contract_cap_unavailable");
                    RaiseCritical?.Invoke(
                        follower.Name,
                        "Follower contract ceiling is unavailable; no copy order was submitted.",
                        "FollowerContractCapUnavailable|" + root);
                    continue;
                }
                int projected = totalOpen + totalInFlight + quantity;
                if (projected > cap)
                {
                    JournalCopy(route, context, 0, "copy_reject|max_contracts|projected=" + projected + "|cap=" + cap);
                    RaiseCritical?.Invoke(
                        follower.Name,
                        "Follower copy would exceed the account contract cap.",
                        "FollowerMaxContracts|" + root);
                    continue;
                }

                SubmitProtectedFollowerEntry(
                    route,
                    context.Instrument,
                    context.Action,
                    quantity,
                    plan,
                    CopySignalName,
                    dedupKey);
            }
        }

        private void FanOutCompleteClose(
            Account masterAccount,
            GlitchCopyExecutionContext context,
            IReadOnlyList<GlitchCopyFollowerRoute> routes,
            string executionKey)
        {
            string root = GlitchReplicationEngine.GetInstrumentRoot(context.Instrument);
            foreach (GlitchCopyFollowerRoute route in routes)
            {
                if (!GlitchReplicationEngine.TryGetNetQuantityForInstrumentRoot(route.FollowerAccount, root, out int followerNet))
                {
                    JournalCopy(route, context, 0, "copy_close_skip|native_state_unavailable");
                    RaiseCritical?.Invoke(route.FollowerAccount.Name,
                        "Follower position state is unavailable; no close order was submitted.",
                        "FollowerCloseStateUnavailable|" + root);
                    continue;
                }
                int closable = context.Action == OrderAction.Sell
                    ? Math.Max(0, followerNet)
                    : context.Action == OrderAction.BuyToCover
                        ? Math.Max(0, -followerNet)
                        : 0;
                int requested = (int)Math.Round(context.Quantity * route.Ratio, MidpointRounding.AwayFromZero);
                if (closable <= 0)
                {
                    JournalCopy(route, context, 0, "copy_skip|follower_has_no_closable_exposure");
                    continue;
                }
                if (requested < closable)
                {
                    JournalCopy(route, context, 0, "copy_skip|partial_manual_exit_uses_native_brackets");
                    RaiseCritical?.Invoke(
                        route.FollowerAccount.Name,
                        "Partial manual exits are not copied because that could desynchronize native brackets. Use the bracket scale-out or Flatten.",
                        "PartialFollowerExitUnsupported|" + root);
                    continue;
                }

                RequestFollowerFlattenOnce(route.FollowerAccount, context.Instrument, "master_manual_close");
                JournalCopy(route, context, closable, "copy_close|flatten_submitted|exec=" + CleanToken(executionKey));
            }
        }

        private void SubmitProtectedFollowerEntry(
            GlitchCopyFollowerRoute route,
            Instrument instrument,
            OrderAction action,
            int quantity,
            GlitchReplicationProtectionPlan plan,
            string signalPrefix,
            string identitySource)
        {
            if (route?.FollowerAccount == null || instrument == null || quantity <= 0 || plan == null)
                return;
            int direction = action == OrderAction.Buy ? 1 : action == OrderAction.SellShort ? -1 : 0;
            if (!GlitchApexDirectionGuard.TryApproveEntry(
                route.FollowerAccount,
                instrument,
                direction,
                out string directionFailure))
            {
                string root = GlitchReplicationEngine.GetInstrumentRoot(instrument);
                Journal?.Invoke(route.FollowerAccount.Name,
                    "entry_rejected|reason=apex_direction_compliance|instrument="
                    + CleanToken(root) + "|detail=" + CleanToken(directionFailure));
                RaiseCritical?.Invoke(
                    route.FollowerAccount.Name,
                    "Permission denied: Apex cross-direction compliance rule.",
                    "ApexCrossDirectionBlocked|" + root);
                return;
            }
            if (!GlitchReplicationProtection.TryScalePlan(plan, quantity, out List<GlitchScaledProtectionLeg> scaled))
            {
                RaiseCritical?.Invoke(route.FollowerAccount.Name,
                    "Follower protection could not be scaled to the configured ratio.",
                    "FollowerProtectionScaleFailed|" + GlitchReplicationEngine.GetInstrumentRoot(instrument));
                return;
            }

            string instrumentRoot = GlitchReplicationEngine.GetInstrumentRoot(instrument);
            if (!TryGetTotalOpenContracts(route.FollowerAccount, out int totalOpen)
                || !TryGetTotalInFlightOpeningQuantity(route.FollowerAccount, out int totalInFlight)
                || !TryResolveRouteContractCap(route, instrumentRoot, out int contractCap))
            {
                RaiseCritical?.Invoke(route.FollowerAccount.Name,
                    "Follower state or contract ceiling is unavailable at submission; no copy order was submitted.",
                    "FollowerFinalAdmissionUnavailable|" + instrumentRoot);
                return;
            }
            int projectedContracts = totalOpen + totalInFlight + quantity;
            if (projectedContracts > contractCap)
            {
                RaiseCritical?.Invoke(route.FollowerAccount.Name,
                    "Follower copy would exceed the account contract cap at submission.",
                    "FollowerFinalMaxContracts|" + instrumentRoot);
                return;
            }

            string accountToken = GlitchReplicationProtection.StableToken(route.FollowerAccount.Name, 6);
            string entryToken = GlitchReplicationProtection.StableToken(identitySource, 8);
            string signal = signalPrefix + "-E-" + accountToken + "-" + entryToken;
            var lifecycle = new FollowerEntryLifecycle
            {
                EntrySignal = signal,
                EntryToken = entryToken,
                Account = route.FollowerAccount,
                Instrument = instrument,
                IsLong = action == OrderAction.Buy,
                ScaledLegs = scaled
            };
            lock (_gate)
                _entriesBySignal[signal] = lifecycle;

            Order order = null;
            string result;
            try
            {
                order = route.FollowerAccount.CreateOrder(
                    instrument,
                    action,
                    OrderType.Market,
                    OrderEntry.Automated,
                    TimeInForce.Day,
                    quantity,
                    0,
                    0,
                    string.Empty,
                    signal,
                    DateTime.MaxValue,
                    null);
                if (order == null)
                    throw new InvalidOperationException("create_order_null");
                route.FollowerAccount.Submit(new[] { order });
                result = order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled
                    ? "rejected"
                    : "submitted";
            }
            catch (Exception ex)
            {
                result = "state_unknown_" + ex.GetType().Name;
            }

            Journal?.Invoke(route.FollowerAccount.Name,
                "copy_entry|master=" + CleanToken(route.MasterAccount)
                + "|follower=" + CleanToken(route.FollowerAccount.Name)
                + "|instrument=" + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(instrument))
                + "|ratio=" + route.Ratio.ToString("0.####", CultureInfo.InvariantCulture)
                + "|qty=" + quantity.ToString(CultureInfo.InvariantCulture)
                + "|result=" + CleanToken(result));

            if (order != null && order.Filled > 0)
                ProcessFollowerOrderUpdate(route.FollowerAccount, order);
            if (!string.Equals(result, "submitted", StringComparison.OrdinalIgnoreCase))
                RaiseCritical?.Invoke(
                    route.FollowerAccount.Name,
                    "Follower entry was not confirmed submitted; Glitch will not retry it automatically.",
                    "FollowerEntrySubmitUnknown|" + GlitchReplicationEngine.GetInstrumentRoot(instrument));
        }

        private bool SubmitProtectionUnits(
            FollowerEntryLifecycle lifecycle,
            int fromQuantity,
            int toQuantity,
            out string failure)
        {
            failure = null;
            var orders = new List<Order>();
            for (int unitIndex = fromQuantity; unitIndex < toQuantity; unitIndex++)
            {
                GlitchScaledProtectionLeg leg = ResolveUnitLeg(lifecycle.ScaledLegs, unitIndex);
                if (leg == null)
                {
                    failure = "unit_plan_missing";
                    return false;
                }

                string sourceToken = string.IsNullOrWhiteSpace(leg.SourceToken) ? "source" : leg.SourceToken;
                string unitToken = (unitIndex + 1).ToString("00", CultureInfo.InvariantCulture);
                string nonce = (Interlocked.Increment(ref _ocoNonce) & 0xffff).ToString("x4", CultureInfo.InvariantCulture);
                string oco = "GLTCP" + sourceToken + lifecycle.EntryToken.Substring(0, Math.Min(6, lifecycle.EntryToken.Length)) + unitToken + nonce;
                string signalTail = sourceToken + "-" + lifecycle.EntryToken + "-" + unitToken;
                OrderAction exitAction = lifecycle.IsLong ? OrderAction.Sell : OrderAction.BuyToCover;
                Order stop = lifecycle.Account.CreateOrder(
                    lifecycle.Instrument, exitAction, OrderType.StopMarket, OrderEntry.Automated, TimeInForce.Gtc,
                    1, 0, leg.StopPrice, oco, CopySignalName + "-S-" + signalTail, DateTime.MaxValue, null);
                Order target = lifecycle.Account.CreateOrder(
                    lifecycle.Instrument, exitAction, OrderType.Limit, OrderEntry.Automated, TimeInForce.Gtc,
                    1, leg.TargetPrice, 0, oco, CopySignalName + "-T-" + signalTail, DateTime.MaxValue, null);
                if (stop == null || target == null)
                {
                    failure = "create_bracket_null";
                    return false;
                }
                orders.Add(stop);
                orders.Add(target);
            }

            try
            {
                lifecycle.Account.Submit(orders.ToArray());
                if (orders.Any(order => order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled))
                {
                    failure = "bracket_rejected";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                bool allVisible = orders.All(order => order != null
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState));
                if (allVisible)
                    return true;
                failure = "submit_exception_" + ex.GetType().Name;
                return false;
            }
        }

        private bool TryRecoverRecentFollowerLifecycle(
            Account followerAccount,
            Order entryOrder,
            string entrySignal,
            out FollowerEntryLifecycle lifecycle)
        {
            lifecycle = null;
            if (followerAccount == null || entryOrder?.Instrument == null || entryOrder.Filled <= 0)
                return false;

            GlitchCopyFollowerRoute route = FindUniqueConfiguredRouteForFollower(followerAccount);
            Account masterAccount = route?.MasterAccountInstance;
            if (masterAccount == null)
                return false;

            bool isLong;
            if (entryOrder.OrderAction == OrderAction.Buy)
                isLong = true;
            else if (entryOrder.OrderAction == OrderAction.SellShort)
                isLong = false;
            else
                return false;

            string root = GlitchReplicationEngine.GetInstrumentRoot(entryOrder.Instrument);
            if (!GlitchReplicationEngine.TryGetNetQuantityForInstrumentRoot(masterAccount, root, out int masterNet))
                return false;
            if (masterNet == 0 || (masterNet > 0) != isLong)
                return false;

            if (!GlitchReplicationProtection.TryResolveMasterPlan(
                    masterAccount,
                    entryOrder.Instrument,
                    null,
                    Math.Abs(masterNet),
                    isLong,
                    out GlitchReplicationProtectionPlan plan))
                return false;

            int requestedQuantity = Math.Max(0, entryOrder.Filled);
            if (!GlitchReplicationProtection.TryScalePlan(plan, requestedQuantity, out List<GlitchScaledProtectionLeg> scaled))
                return false;

            string entryToken = ExtractFollowerEntryToken(entrySignal);
            if (string.IsNullOrWhiteSpace(entryToken)
                || !TryCountCompleteFollowerProtection(followerAccount, entryOrder.Instrument, entryToken, isLong, out int protectedQuantity)
                || protectedQuantity > requestedQuantity)
                return false;

            lifecycle = new FollowerEntryLifecycle
            {
                EntrySignal = entrySignal,
                EntryToken = entryToken,
                Account = followerAccount,
                Instrument = entryOrder.Instrument,
                IsLong = isLong,
                ScaledLegs = scaled,
                ProtectedQuantity = protectedQuantity
            };
            Journal?.Invoke(followerAccount.Name,
                "follower_protection|entry=" + CleanToken(entrySignal)
                + "|result=recent_lifecycle_recovered|protected_qty="
                + protectedQuantity.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        private GlitchCopyFollowerRoute FindUniqueConfiguredRouteForFollower(Account followerAccount)
        {
            if (followerAccount == null)
                return null;
            lock (_gate)
            {
                List<GlitchCopyFollowerRoute> matches = _routesByMaster.Values
                    .SelectMany(routes => routes ?? new List<GlitchCopyFollowerRoute>())
                    .Where(route => route?.FollowerAccount != null
                        && string.Equals(route.FollowerAccount.Name, followerAccount.Name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                return matches.Count == 1 ? matches[0] : null;
            }
        }

        private GlitchCopyFollowerRoute FindConfiguredRoute(Account masterAccount, Account followerAccount)
        {
            if (masterAccount == null || followerAccount == null)
                return null;
            lock (_gate)
            {
                if (!_routesByMaster.TryGetValue(masterAccount.Name?.Trim() ?? string.Empty, out List<GlitchCopyFollowerRoute> routes))
                    return null;
                return routes.FirstOrDefault(route => route?.FollowerAccount != null
                    && string.Equals(route.FollowerAccount.Name, followerAccount.Name, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static bool TryCountCompleteFollowerProtection(
            Account account,
            Instrument instrument,
            string entryToken,
            bool isLong,
            out int protectedQuantity)
        {
            protectedQuantity = 0;
            if (account == null || instrument == null)
                return false;
            if (!TrySnapshotOrders(account, out Order[] orders))
                return false;
            string root = GlitchReplicationEngine.GetInstrumentRoot(instrument);
            string tokenNeedle = string.IsNullOrWhiteSpace(entryToken) ? null : "-" + entryToken + "-";
            OrderAction expectedExitAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
            List<Order> protection = orders
                .Where(order => order?.Instrument != null
                    && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(order.Instrument), root, StringComparison.OrdinalIgnoreCase)
                    && order.OrderAction == expectedExitAction
                    && IsFollowerProtectionSignal(order.Name)
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                    && !string.IsNullOrWhiteSpace(order.Oco)
                    && (tokenNeedle == null || order.Name.IndexOf(tokenNeedle, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
            if (protection.Count == 0)
                return true;

            foreach (IGrouping<string, Order> ocoGroup in protection.GroupBy(order => order.Oco, StringComparer.OrdinalIgnoreCase))
            {
                List<Order> stops = ocoGroup.Where(GlitchReplicationEngine.IsStopLikeOrder).ToList();
                List<Order> targets = ocoGroup.Where(order => order.OrderType == OrderType.Limit).ToList();
                if (stops.Count != 1 || targets.Count != 1)
                    return false;
                int quantity = Math.Min(RemainingQuantity(stops[0]), RemainingQuantity(targets[0]));
                if (quantity <= 0)
                    return false;
                protectedQuantity += quantity;
            }
            return true;
        }

        private static string ExtractFollowerEntryToken(string signal)
        {
            if (string.IsNullOrWhiteSpace(signal))
                return null;
            int separator = signal.LastIndexOf('-');
            return separator < 0 || separator >= signal.Length - 1
                ? null
                : signal.Substring(separator + 1).Trim();
        }

        private static bool IsRecentOrder(Order order, TimeSpan maxAge)
        {
            if (order == null || order.Time == DateTime.MinValue || maxAge <= TimeSpan.Zero)
                return false;
            DateTime orderUtc = order.Time.Kind == DateTimeKind.Utc
                ? order.Time
                : order.Time.ToUniversalTime();
            TimeSpan age = DateTime.UtcNow - orderUtc;
            return age >= TimeSpan.Zero && age <= maxAge;
        }

        private void TryReleasePendingMasterCopies(Account account)
        {
            if (!IsEnabled || account == null)
                return;
            List<KeyValuePair<string, PendingMasterCopy>> pending;
            lock (_gate)
            {
                pending = _pendingMasterCopies
                    .Where(item => string.Equals(item.Value?.MasterAccount?.Name, account.Name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            foreach (KeyValuePair<string, PendingMasterCopy> pair in pending)
            {
                PendingMasterCopy item = pair.Value;
                if (item == null)
                    continue;
                if (DateTime.UtcNow - item.CreatedUtc > PendingMasterCopyTtl)
                {
                    lock (_gate) _pendingMasterCopies.Remove(pair.Key);
                    RaiseCritical?.Invoke(account.Name,
                        "Master bracket did not become ready before the copy timeout; followers were left unchanged.",
                        "MasterProtectionTimeout|" + GlitchReplicationEngine.GetInstrumentRoot(item.Context?.Instrument));
                    continue;
                }

                bool isLong = item.Context.Action == OrderAction.Buy;
                string root = GlitchReplicationEngine.GetInstrumentRoot(item.Context.Instrument);
                if (!GlitchReplicationEngine.TryGetNetQuantityForInstrumentRoot(account, root, out int masterNet))
                    continue;
                if (masterNet == 0 || (masterNet > 0) != isLong)
                {
                    lock (_gate) _pendingMasterCopies.Remove(pair.Key);
                    continue;
                }
                int copyMasterQuantity = ResolveContextMasterQuantity(item.Context);
                if (Math.Abs(masterNet) < copyMasterQuantity)
                    continue;
                if (!GlitchReplicationProtection.TryResolveMasterPlan(
                    account,
                    item.Context.Instrument,
                    item.Context.OrderSignalName,
                    copyMasterQuantity,
                    isLong,
                    out GlitchReplicationProtectionPlan plan))
                    continue;
                if (TryGetRoutes(account.Name, out List<GlitchCopyFollowerRoute> routes))
                    FanOutOpening(account, item.Context, routes, plan);
            }
        }

        private void MirrorMasterStop(Account masterAccount, Order masterOrder)
        {
            if (masterOrder.Instrument == null
                || !GlitchReplicationEngine.IsStopLikeOrder(masterOrder)
                || !GlitchReplicationEngine.IsWorkingOrderState(masterOrder.OrderState)
                || masterOrder.StopPrice <= 0)
                return;
            if (!TryGetConfiguredRoutes(masterAccount.Name, out List<GlitchCopyFollowerRoute> routes))
                return;

            string sourceToken = GlitchReplicationProtection.BuildSourceToken(masterOrder.Name, masterOrder.Oco);
            string prefix = CopySignalName + "-S-" + sourceToken + "-";
            string root = GlitchReplicationEngine.GetInstrumentRoot(masterOrder.Instrument);
            foreach (GlitchCopyFollowerRoute route in routes)
            {
                if (!TrySnapshotOrders(route.FollowerAccount, out Order[] orders))
                {
                    RaiseCritical?.Invoke(route.FollowerAccount.Name,
                        "Follower order state is unavailable; the master stop was not mirrored.",
                        "FollowerStopMirrorStateUnavailable|" + root);
                    continue;
                }
                List<Order> changes = orders
                    .Where(order => order?.Instrument != null
                        && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                        && (order.Name ?? string.Empty).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(order.Instrument), root, StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(order.StopPrice - masterOrder.StopPrice) > 0.0000001d)
                    .ToList();
                if (changes.Count == 0)
                    continue;
                try
                {
                    foreach (Order followerStop in changes)
                        followerStop.StopPriceChanged = masterOrder.StopPrice;
                    route.FollowerAccount.Change(changes.ToArray());
                }
                catch (Exception ex)
                {
                    RaiseCritical?.Invoke(route.FollowerAccount.Name,
                        "Follower stop could not mirror the master: " + ex.GetType().Name,
                        "FollowerStopMirrorFailed|" + root);
                }
            }
        }

        private void CleanupFlatFollowerOrders(Account account)
        {
            if (!IsConfiguredFollower(account))
                return;
            if (!TrySnapshotOrders(account, out Order[] orders))
                return;
            foreach (IGrouping<string, Order> group in orders
                .Where(order => order?.Instrument != null
                    && IsFollowerProtectionSignal(order.Name)
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                .GroupBy(order => GlitchReplicationEngine.GetInstrumentRoot(order.Instrument), StringComparer.OrdinalIgnoreCase))
            {
                string root = group.Key;
                bool entryPending = orders.Any(order => order?.Instrument != null
                    && IsFollowerEntrySignal(order.Name)
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                    && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(order.Instrument), root, StringComparison.OrdinalIgnoreCase));
                if (entryPending
                    || !GlitchReplicationEngine.TryGetNetQuantityForInstrumentRoot(account, root, out int netQuantity)
                    || netQuantity != 0)
                    continue;
                try
                {
                    account.Cancel(group.ToArray());
                    Journal?.Invoke(account.Name, "orphan_protection_cancel|instrument=" + CleanToken(root));
                }
                catch (Exception ex)
                {
                    RaiseCritical?.Invoke(account.Name,
                        "Orphan follower brackets could not be cancelled: " + ex.GetType().Name,
                        "FollowerOrphanCancelFailed|" + root);
                }
            }

            string accountPrefix = (account.Name?.Trim() ?? string.Empty) + "|";
            List<string> submittedRoots;
            lock (_gate)
                submittedRoots = _flattenSubmitted
                    .Where(key => key.StartsWith(accountPrefix, StringComparison.OrdinalIgnoreCase))
                    .Select(key => key.Substring(accountPrefix.Length))
                    .ToList();

            foreach (string root in submittedRoots)
            {
                bool hasWorkingRootOrder = orders.Any(order => order?.Instrument != null
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                    && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(order.Instrument), root, StringComparison.OrdinalIgnoreCase));
                if (hasWorkingRootOrder
                    || !GlitchReplicationEngine.TryGetNetQuantityForInstrumentRoot(account, root, out int netQuantity)
                    || netQuantity != 0)
                    continue;

                lock (_gate)
                {
                    _flattenSubmitted.Remove(accountPrefix + root);
                    foreach (string signal in _entriesBySignal
                        .Where(item => string.Equals(item.Value?.Account?.Name, account.Name, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(
                                GlitchReplicationEngine.GetInstrumentRoot(item.Value?.Instrument),
                                root,
                                StringComparison.OrdinalIgnoreCase))
                        .Select(item => item.Key)
                        .ToList())
                        _entriesBySignal.Remove(signal);
                }
            }
        }

        private void RequestFollowerFlattenOnce(Account account, Instrument instrument, string reason)
        {
            if (account == null || instrument == null)
                return;
            string key = (account.Name?.Trim() ?? string.Empty) + "|" + GlitchReplicationEngine.GetInstrumentRoot(instrument);
            lock (_gate)
            {
                if (!_flattenSubmitted.Add(key))
                    return;
            }
            try
            {
                account.Flatten(new[] { instrument });
                Journal?.Invoke(account.Name,
                    "follower_flatten|instrument=" + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(instrument))
                    + "|reason=" + CleanToken(reason)
                    + "|result=flatten_requested");
            }
            catch (Exception ex)
            {
                lock (_gate) _flattenSubmitted.Remove(key);
                RaiseCritical?.Invoke(account.Name,
                    "Follower flatten submission failed: " + ex.GetType().Name,
                    "FollowerFlattenFailed|" + GlitchReplicationEngine.GetInstrumentRoot(instrument));
            }
        }

        private bool TryGetRoutes(string masterName, out List<GlitchCopyFollowerRoute> routes)
        {
            routes = null;
            lock (_gate)
            {
                if (!_enabled || string.IsNullOrWhiteSpace(masterName)
                    || !_routesByMaster.TryGetValue(masterName.Trim(), out List<GlitchCopyFollowerRoute> configured)
                    || configured.Count == 0)
                    return false;
                routes = configured.ToList();
                return true;
            }
        }

        private bool TryGetConfiguredRoutes(string masterName, out List<GlitchCopyFollowerRoute> routes)
        {
            routes = null;
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(masterName)
                    || !_routesByMaster.TryGetValue(masterName.Trim(), out List<GlitchCopyFollowerRoute> configured)
                    || configured.Count == 0)
                    return false;
                routes = configured.ToList();
                return true;
            }
        }

        private bool IsConfiguredFollower(Account account)
        {
            if (account == null)
                return false;
            lock (_gate)
                return _routesByMaster.Values.SelectMany(items => items).Any(route => string.Equals(
                    route.FollowerAccount?.Name,
                    account.Name,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsValidRoute(GlitchCopyFollowerRoute route)
        {
            return route != null
                && !string.IsNullOrWhiteSpace(route.MasterAccount)
                && route.MasterAccountInstance != null
                && route.FollowerAccount != null
                && route.Ratio > 0
                && !double.IsNaN(route.Ratio)
                && !double.IsInfinity(route.Ratio);
        }

        private static bool IsOpeningAction(OrderAction action)
        {
            return action == OrderAction.Buy || action == OrderAction.SellShort;
        }

        private static bool IsFollowerOwnedSignal(string signal)
        {
            return IsFollowerEntrySignal(signal) || IsFollowerProtectionSignal(signal);
        }

        private static bool IsFollowerEntrySignal(string signal)
        {
            if (string.IsNullOrWhiteSpace(signal))
                return false;
            string value = signal.Trim();
            return value.StartsWith(CopySignalName + "-E-", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith(CatchUpSignalName + "-E-", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFollowerProtectionSignal(string signal)
        {
            if (string.IsNullOrWhiteSpace(signal))
                return false;
            string value = signal.Trim();
            return value.StartsWith(CopySignalName + "-S-", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith(CopySignalName + "-T-", StringComparison.OrdinalIgnoreCase);
        }

        private static GlitchScaledProtectionLeg ResolveUnitLeg(IReadOnlyList<GlitchScaledProtectionLeg> legs, int unitIndex)
        {
            int cursor = 0;
            foreach (GlitchScaledProtectionLeg leg in legs ?? Array.Empty<GlitchScaledProtectionLeg>())
            {
                cursor += Math.Max(0, leg.Quantity);
                if (unitIndex < cursor)
                    return leg;
            }
            return null;
        }

        private static bool TryResolveRouteContractCap(
            GlitchCopyFollowerRoute route,
            string instrumentRoot,
            out int cap)
        {
            cap = 0;
            if (route == null)
                return false;
            if (route.MaxMicroContracts > 0 && !string.IsNullOrWhiteSpace(route.MicroContractRootRegex))
            {
                try
                {
                    if (Regex.IsMatch(instrumentRoot ?? string.Empty, route.MicroContractRootRegex, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50)))
                    {
                        cap = route.MaxMicroContracts;
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }
            cap = route.MaxContracts;
            return cap > 0;
        }

        private static bool TryGetTotalOpenContracts(Account account, out int total)
        {
            total = 0;
            try
            {
                if (account?.Positions == null)
                    return false;
                lock (account.Positions)
                    total = account.Positions.Where(position => position != null && position.MarketPosition != MarketPosition.Flat)
                        .Sum(position => Math.Abs(position.Quantity));
                return true;
            }
            catch
            {
                total = 0;
                return false;
            }
        }

        private static bool TryGetInFlightOpeningQuantity(
            Account account,
            string root,
            int direction,
            out int quantity)
        {
            quantity = 0;
            if (!TrySnapshotOrders(account, out Order[] orders))
                return false;
            quantity = orders
                .Where(order => order?.Instrument != null
                    && IsFollowerEntrySignal(order.Name)
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                    && GlitchReplicationEngine.GetOrderActionSign(order.OrderAction) == direction
                    && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(order.Instrument), root, StringComparison.OrdinalIgnoreCase))
                .Sum(RemainingQuantity);
            return true;
        }

        private static bool TryGetTotalInFlightOpeningQuantity(Account account, out int quantity)
        {
            quantity = 0;
            if (!TrySnapshotOrders(account, out Order[] orders))
                return false;
            quantity = orders
                .Where(order => order != null
                    && IsFollowerEntrySignal(order.Name)
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                .Sum(RemainingQuantity);
            return true;
        }

        private static int RemainingQuantity(Order order)
        {
            return order == null ? 0 : Math.Max(0, Math.Abs(order.Quantity) - Math.Max(0, order.Filled));
        }

        private static int ResolveContextMasterQuantity(GlitchCopyExecutionContext context)
        {
            if (context == null)
                return 0;
            return Math.Max(
                Math.Max(0, context.Quantity),
                Math.Max(0, context.EntryOrderFilledQuantity));
        }

        private static bool TrySnapshotOrders(Account account, out Order[] orders)
        {
            orders = Array.Empty<Order>();
            try
            {
                if (account?.Orders == null)
                    return false;
                lock (account.Orders)
                    orders = account.Orders.ToArray();
                return true;
            }
            catch
            {
                orders = Array.Empty<Order>();
                return false;
            }
        }

        private static string BuildFollowerRootKey(Account account, string root)
        {
            return (account?.Name?.Trim() ?? string.Empty) + "|" + (root?.Trim() ?? string.Empty);
        }

        private bool IsFollowerRootSuppressed(Account account, string root)
        {
            lock (_gate)
                return _suppressedFollowerRoots.Contains(BuildFollowerRootKey(account, root));
        }

        private void SuppressFollowerRoot(Account account, string root)
        {
            lock (_gate)
                _suppressedFollowerRoots.Add(BuildFollowerRootKey(account, root));
        }

        private void ClearFollowerRootSuppression(Account account, string root)
        {
            lock (_gate)
                _suppressedFollowerRoots.Remove(BuildFollowerRootKey(account, root));
        }

        private void ClearFollowerSuppressions(Account account)
        {
            string prefix = (account?.Name?.Trim() ?? string.Empty) + "|";
            lock (_gate)
            {
                foreach (string key in _suppressedFollowerRoots
                    .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList())
                    _suppressedFollowerRoots.Remove(key);
            }
        }

        private bool TryRememberExecutionId(string executionId)
        {
            if (string.IsNullOrWhiteSpace(executionId))
                return true;
            lock (_gate)
            {
                if (_seenExecutionIdSet.Contains(executionId))
                    return false;
                _seenExecutionIdSet.Add(executionId);
                _seenExecutionIds.AddLast(executionId);
                while (_seenExecutionIds.Count > 1024)
                {
                    string oldest = _seenExecutionIds.First.Value;
                    _seenExecutionIds.RemoveFirst();
                    _seenExecutionIdSet.Remove(oldest);
                }
                return true;
            }
        }

        private static string BuildExecutionDedupKey(string masterName, GlitchCopyExecutionContext context)
        {
            string identity = !string.IsNullOrWhiteSpace(context?.ExecutionId)
                ? context.ExecutionId.Trim()
                : (context?.ExecutionTimeUtc ?? DateTime.MinValue).Ticks.ToString(CultureInfo.InvariantCulture)
                    + "|" + (context?.OrderSignalName ?? string.Empty)
                    + "|" + context?.Action
                    + "|" + context?.Quantity;
            return (masterName?.Trim() ?? "unknown") + "|" + identity;
        }

        private static string BuildPendingMasterCopyKey(Account account, GlitchCopyExecutionContext context)
        {
            return BuildExecutionDedupKey(account?.Name, context);
        }

        private static GlitchCopyExecutionContext CloneContext(GlitchCopyExecutionContext source)
        {
            return source == null ? null : new GlitchCopyExecutionContext
            {
                ExecutionId = source.ExecutionId,
                ExecutionTimeUtc = source.ExecutionTimeUtc,
                Instrument = source.Instrument,
                Action = source.Action,
                OrderType = source.OrderType,
                Quantity = source.Quantity,
                EntryOrderFilledQuantity = source.EntryOrderFilledQuantity,
                MasterNetAfterExecution = source.MasterNetAfterExecution,
                OrderSignalName = source.OrderSignalName,
                Oco = source.Oco
            };
        }

        private void JournalCopy(GlitchCopyFollowerRoute route, GlitchCopyExecutionContext context, int quantity, string result)
        {
            Journal?.Invoke(route?.FollowerAccount?.Name ?? "Unknown",
                "copy|master=" + CleanToken(route?.MasterAccount)
                + "|follower=" + CleanToken(route?.FollowerAccount?.Name)
                + "|instrument=" + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(context?.Instrument))
                + "|master_action=" + context?.Action
                + "|ratio=" + (route?.Ratio ?? 0).ToString("0.####", CultureInfo.InvariantCulture)
                + "|qty=" + quantity.ToString(CultureInfo.InvariantCulture)
                + "|result=" + CleanToken(result));
        }

        private static string CleanToken(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "-"
                : value.Trim().Replace('|', '_').Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        private sealed class PendingMasterCopy
        {
            public Account MasterAccount { get; set; }
            public GlitchCopyExecutionContext Context { get; set; }
            public DateTime CreatedUtc { get; set; }
        }

        private sealed class FollowerEntryLifecycle
        {
            public string EntrySignal { get; set; }
            public string EntryToken { get; set; }
            public Account Account { get; set; }
            public Instrument Instrument { get; set; }
            public bool IsLong { get; set; }
            public int ProtectedQuantity { get; set; }
            public bool ProtectionSubmissionInProgress { get; set; }
            public bool ProtectionFailed { get; set; }
            public List<GlitchScaledProtectionLeg> ScaledLegs { get; set; }
        }
    }
}
