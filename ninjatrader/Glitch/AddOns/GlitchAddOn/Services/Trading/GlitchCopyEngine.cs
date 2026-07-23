//
// Master execution fan-out. This engine owns routes, ratios, follower entries
// and follower protection, independent of whichever producer trades the master.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        public Order EntryOrder { get; set; }
        public string OrderSignalName { get; set; }
        public string Oco { get; set; }
    }

    public sealed class GlitchCopyFollowerRoute
    {
        public string MasterAccount { get; set; }
        public Account MasterAccountInstance { get; set; }
        public Account FollowerAccount { get; set; }
        public double Ratio { get; set; }
    }

    internal enum GlitchSyncInitialAction
    {
        AlreadySynced,
        SubmitFlatten,
        SubmitReduce,
        SubmitTail
    }

    internal enum GlitchSyncObservation
    {
        None,
        ContinueTail,
        Completed,
        ManualOverride
    }

    internal sealed class GlitchSyncLifecycleState
    {
        private enum Phase
        {
            Validating,
            FlattenSubmitting,
            AwaitingFlat,
            TailSubmitting,
            AwaitingTail,
            Terminal
        }

        private Phase _phase = Phase.Validating;

        public GlitchSyncLifecycleState(int initialActual)
        {
            InitialActual = initialActual;
        }

        public int InitialActual { get; }
        public int TailStart { get; private set; }
        public int TailExpected { get; private set; }
        public bool IsTerminal => _phase == Phase.Terminal;
        public bool IsAwaitingFlat => _phase == Phase.AwaitingFlat;
        public bool IsAwaitingTail => _phase == Phase.AwaitingTail;

        public static GlitchSyncInitialAction DecideInitial(int expected, int actual)
        {
            if (expected == actual)
                return GlitchSyncInitialAction.AlreadySynced;
            if (expected == 0
                || (actual != 0 && Math.Sign(actual) != Math.Sign(expected)))
                return GlitchSyncInitialAction.SubmitFlatten;
            if (actual != 0
                && Math.Sign(actual) == Math.Sign(expected)
                && Math.Abs(actual) > Math.Abs(expected))
                return GlitchSyncInitialAction.SubmitReduce;
            return GlitchSyncInitialAction.SubmitTail;
        }

        public bool TryBeginFlatten()
        {
            if (_phase != Phase.Validating)
                return false;
            _phase = Phase.FlattenSubmitting;
            return true;
        }

        public void MarkFlattenSubmitted(bool submitted)
        {
            if (_phase != Phase.FlattenSubmitting)
                return;
            _phase = submitted ? Phase.AwaitingFlat : Phase.Terminal;
        }

        public GlitchSyncObservation ObserveFlatten(int actual, int ownedFilled)
        {
            if (_phase != Phase.AwaitingFlat)
                return GlitchSyncObservation.None;
            int expectedActual = InitialActual
                - (Math.Sign(InitialActual) * Math.Max(0, ownedFilled));
            if (actual != expectedActual)
            {
                _phase = Phase.Terminal;
                return GlitchSyncObservation.ManualOverride;
            }
            if (actual == 0)
                return GlitchSyncObservation.ContinueTail;
            return GlitchSyncObservation.None;
        }

        public bool TryBeginTail(int actual, int expected)
        {
            if ((_phase != Phase.Validating && _phase != Phase.AwaitingFlat)
                || actual == expected)
                return false;
            TailStart = actual;
            TailExpected = expected;
            _phase = Phase.TailSubmitting;
            return true;
        }

        public void MarkTailSubmitted(bool submitted)
        {
            if (_phase != Phase.TailSubmitting)
                return;
            _phase = submitted ? Phase.AwaitingTail : Phase.Terminal;
        }

        public GlitchSyncObservation ObserveTail(int actual, int ownedFilled)
        {
            if (_phase != Phase.AwaitingTail)
                return GlitchSyncObservation.None;
            int requestedDelta = TailExpected - TailStart;
            int expectedActual = TailStart
                + (Math.Sign(requestedDelta) * Math.Max(0, ownedFilled));
            if (actual != expectedActual)
            {
                _phase = Phase.Terminal;
                return GlitchSyncObservation.ManualOverride;
            }
            if (actual == TailExpected)
            {
                _phase = Phase.Terminal;
                return GlitchSyncObservation.Completed;
            }
            return GlitchSyncObservation.None;
        }

        public void Supersede()
        {
            _phase = Phase.Terminal;
        }
    }

    public sealed class GlitchCopyEngine
    {
        public const string CopySignalName = "GLT-COPY";
        public const string CatchUpSignalName = "GLT-CATCHUP";
        private static int _ocoNonce;
        private static int _syncNonce;

        private readonly object _gate = new object();
        private readonly LinkedList<string> _seenExecutionIds = new LinkedList<string>();
        private readonly HashSet<string> _seenExecutionIdSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<GlitchCopyFollowerRoute>> _routesByMaster =
            new Dictionary<string, List<GlitchCopyFollowerRoute>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FollowerEntryLifecycle> _entriesBySignal =
            new Dictionary<string, FollowerEntryLifecycle>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CloseState> _closesBySignal =
            new Dictionary<string, CloseState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FollowerSyncLifecycle> _syncByFollowerInstrument =
            new Dictionary<string, FollowerSyncLifecycle>(StringComparer.OrdinalIgnoreCase);

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
            }
        }

        public void ProcessMasterExecution(Account masterAccount, GlitchCopyExecutionContext context)
        {
            if (masterAccount == null || context?.Instrument == null || context.Quantity <= 0)
                return;
            if (ParseFollowerSignalKind(context.OrderSignalName) != FollowerSignalKind.None)
                return;
            if (!TryGetRouteSnapshot(masterAccount.Name, true, out List<GlitchCopyFollowerRoute> routes))
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

            GlitchReplicationProtectionPlan plan = null;
            int masterEntryQuantity = ResolveContextMasterQuantity(context);
            GlitchReplicationProtection.TryResolveMasterPlan(
                masterAccount,
                context.Instrument,
                context.OrderSignalName,
                masterEntryQuantity,
                context.Action == OrderAction.Buy,
                out plan);

            FanOutOpening(masterAccount, context, routes, plan, masterEntryQuantity);
        }

        public void ProcessMasterOrderUpdate(Account masterAccount, Order order)
        {
            if (masterAccount == null || order == null)
                return;
            TryAttachLateFollowerProtection(masterAccount, order);
            MirrorMasterProtection(masterAccount, order);
        }

        public void ProcessFollowerOrderUpdate(Account followerAccount, Order order)
        {
            if (followerAccount == null || order?.Instrument == null || string.IsNullOrWhiteSpace(order.Name))
                return;
            string signal = order.Name.Trim();
            ProcessSyncFollowerOrderUpdate(followerAccount, order, signal);
            FollowerSignalKind signalKind = ParseFollowerSignalKind(signal);
            if (signalKind == FollowerSignalKind.Close)
            {
                TrackCloseOrder(followerAccount, order, signal);
                return;
            }
            if (signalKind == FollowerSignalKind.Protection)
            {
                ProcessFollowerProtectionOrderUpdate(followerAccount, order, signal);
                return;
            }
            if (signalKind != FollowerSignalKind.Entry)
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

            lock (_gate)
                lifecycle.EntryOrder = order;

            if (lifecycle.ProtectionAvailable)
            {
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
                        TrySubmitAttributedRecoveryClose(
                            lifecycle,
                            Math.Max(0, protectTo - protectFrom),
                            "protection_submit_failed");
                        RaiseCritical?.Invoke(
                            followerAccount.Name,
                            "Follower entry protection failed; only attributable copied exposure was considered for recovery: " + failure,
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
                    && string.Equals(item.Instrument.FullName, order.Instrument.FullName, StringComparison.OrdinalIgnoreCase)
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
            if (GlitchReplicationEngine.TryGetNetQuantityForInstrument(followerAccount, order.Instrument, out int followerNet)
                && followerNet == 0)
                return;

            if (lifecycle == null)
            {
                Journal?.Invoke(
                    followerAccount.Name,
                    "follower_recovery|instrument=" + CleanToken(root)
                    + "|reason=protection_order_rejected|result=manual_override_unattributed");
                return;
            }
            int attributableQuantity = Math.Min(
                lifecycle.SubmittedQuantity,
                Math.Max(0, lifecycle.EntryOrder?.Filled ?? 0));
            TrySubmitAttributedRecoveryClose(
                lifecycle,
                attributableQuantity,
                "protection_order_rejected");
            RaiseCritical?.Invoke(
                followerAccount.Name,
                "A Glitch-owned follower stop or target was rejected; only attributable copied exposure was considered for recovery.",
                "FollowerProtectionRejected|" + root + "|" + CleanToken(lifecycle?.EntrySignal ?? signal));
        }

        public void ProcessAccountStateUpdate(Account account)
        {
            if (account == null)
                return;
            ReconcileCloses(account);
            ReconcileFollowerProtection(account);
            CleanupFlatFollowerOrders(account);
            ProcessSyncAccountStateUpdate(account);
        }

        public void ProcessFollowerExecution(Account account)
        {
            // ponytail: authoritative follower protection convergence is owned by PositionUpdate.
        }

        public void SyncFollower(Account masterAccount, Account followerAccount, double ratio)
        {
            if (followerAccount == null)
                return;
            if (!IsEnabled || masterAccount == null || ratio <= 0
                || double.IsNaN(ratio) || double.IsInfinity(ratio))
            {
                JournalSync(followerAccount, "-", "validation", "invalid_request", 0, 0, null);
                return;
            }

            GlitchCopyFollowerRoute configuredRoute = FindConfiguredRoute(masterAccount, followerAccount);
            if (configuredRoute == null)
            {
                JournalSync(followerAccount, "-", "validation", "route_unavailable", 0, 0, null);
                RaiseCritical?.Invoke(
                    followerAccount.Name,
                    "Sync has no active configured route; no order was submitted.",
                    "SyncRouteUnavailable");
                return;
            }
            ratio = configuredRoute.Ratio;
            var instruments = new Dictionary<string, Instrument>(StringComparer.OrdinalIgnoreCase);
            GlitchReplicationEngine.CollectPositionInstruments(masterAccount, instruments);
            GlitchReplicationEngine.CollectPositionInstruments(followerAccount, instruments);
            if (instruments.Count == 0)
            {
                JournalSync(followerAccount, "-", "validation", "already_flat", 0, 0, null);
                return;
            }

            foreach (Instrument instrument in instruments.Values)
            {
                string root = GlitchReplicationEngine.GetInstrumentRoot(instrument);
                string syncKey = BuildFollowerInstrumentKey(followerAccount, instrument);
                lock (_gate)
                {
                    if (_syncByFollowerInstrument.ContainsKey(syncKey))
                    {
                        JournalSync(followerAccount, root, "validation", "already_in_progress", 0, 0, null);
                        continue;
                    }
                }

                if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(masterAccount, instrument, out int masterNet)
                    || !GlitchReplicationEngine.TryGetNetQuantityForInstrument(followerAccount, instrument, out int actual))
                {
                    JournalSync(followerAccount, root, "validation", "state_unavailable", 0, 0, null);
                    RaiseCritical?.Invoke(
                        followerAccount.Name,
                        "Sync could not verify native position state; no order was submitted.",
                        "SyncStateUnavailable|" + root);
                    continue;
                }
                int expected = ScaleSignedQuantity(masterNet, ratio);

                GlitchSyncInitialAction initialAction =
                    GlitchSyncLifecycleState.DecideInitial(expected, actual);
                if (initialAction == GlitchSyncInitialAction.AlreadySynced)
                {
                    JournalSync(followerAccount, root, "validation", "already_synced", actual, expected, null);
                    continue;
                }

                var sync = new FollowerSyncLifecycle
                {
                    Key = syncKey,
                    Root = root,
                    MasterAccount = masterAccount,
                    FollowerAccount = followerAccount,
                    Instrument = instrument,
                    Ratio = ratio,
                    State = new GlitchSyncLifecycleState(actual),
                    IdentitySource = "sync" + GlitchReplicationProtection.StableToken(
                        root + "|" + followerAccount.Name + "|" + Interlocked.Increment(ref _syncNonce),
                        8)
                };
                lock (_gate)
                {
                    if (_syncByFollowerInstrument.ContainsKey(syncKey))
                    {
                        JournalSync(followerAccount, root, "validation", "already_in_progress", actual, expected, null);
                        continue;
                    }
                    _syncByFollowerInstrument[syncKey] = sync;
                }

                if (initialAction == GlitchSyncInitialAction.SubmitFlatten)
                    BeginSyncFlatten(sync, actual, expected);
                else if (initialAction == GlitchSyncInitialAction.SubmitReduce)
                    BeginSyncReduce(sync, actual, expected);
                else
                    BeginSyncTail(sync, configuredRoute, masterNet, actual, expected);
            }
        }

        private void BeginSyncFlatten(FollowerSyncLifecycle sync, int actual, int expected)
        {
            if (sync == null)
                return;
            lock (_gate)
            {
                if (!IsCurrentSyncLifecycle(sync) || !sync.State.TryBeginFlatten())
                    return;
            }

            JournalSync(sync.FollowerAccount, sync.Root, "validation", "flatten_required", actual, expected, null);
            FollowerOrderSubmission submission = SubmitFollowerClose(
                sync.FollowerAccount,
                sync.Instrument,
                actual > 0 ? OrderAction.Sell : OrderAction.BuyToCover,
                Math.Abs(actual),
                sync.IdentitySource + "|flatten",
                CatchUpSignalName);
            bool accepted = string.Equals(submission.Result, "submitted", StringComparison.OrdinalIgnoreCase);
            lock (_gate)
            {
                if (!IsCurrentSyncLifecycle(sync))
                    return;
                sync.FlattenOrderSignal = submission.Signal;
                sync.FlattenOrder = submission.Order;
                sync.State.MarkFlattenSubmitted(accepted);
                if (!accepted)
                    _syncByFollowerInstrument.Remove(sync.Key);
            }

            JournalSync(
                sync.FollowerAccount,
                sync.Root,
                "flatten_submission",
                accepted ? "submitted" : "failed_" + CleanToken(submission.Result),
                actual,
                expected,
                "qty=" + Math.Abs(actual).ToString(CultureInfo.InvariantCulture));
            if (accepted)
                ProcessSyncLifecycle(sync);
        }

        private void BeginSyncReduce(FollowerSyncLifecycle sync, int actual, int expected)
        {
            if (sync == null)
                return;
            int quantity = Math.Abs(actual) - Math.Abs(expected);
            if (quantity <= 0)
            {
                SupersedeSync(sync, "validation", "reduce_not_required", actual, expected);
                return;
            }

            OrderAction action = actual > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
            JournalSync(sync.FollowerAccount, sync.Root, "validation", "reduce_required", actual, expected, null);
            FollowerOrderSubmission submission = SubmitFollowerClose(
                sync.FollowerAccount,
                sync.Instrument,
                action,
                quantity,
                sync.IdentitySource + "|reduce",
                CatchUpSignalName);
            bool accepted = string.Equals(submission.Result, "submitted", StringComparison.OrdinalIgnoreCase);
            lock (_gate)
            {
                if (!IsCurrentSyncLifecycle(sync))
                    return;
                sync.ReduceOrderSignal = submission.Signal;
                sync.ReduceOrder = submission.Order;
                sync.ReduceTargetExpected = expected;
                if (!accepted)
                    _syncByFollowerInstrument.Remove(sync.Key);
            }

            JournalSync(
                sync.FollowerAccount,
                sync.Root,
                "reduce_submission",
                accepted ? "submitted" : "failed_" + CleanToken(submission.Result),
                actual,
                expected,
                "qty=" + quantity.ToString(CultureInfo.InvariantCulture));
            if (accepted)
                ProcessSyncLifecycle(sync);
        }

        private void BeginSyncTail(
            FollowerSyncLifecycle sync,
            GlitchCopyFollowerRoute route,
            int observedMasterNet,
            int observedActual,
            int observedExpected)
        {
            if (sync == null || route == null)
                return;
            if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(
                    route.MasterAccountInstance,
                    sync.Instrument,
                    out int masterNet)
                || !GlitchReplicationEngine.TryGetNetQuantityForInstrument(
                    sync.FollowerAccount,
                    sync.Instrument,
                    out int actual))
            {
                SupersedeSync(sync, "tail_validation", "state_unavailable", observedActual, observedExpected);
                return;
            }

            int expected = ScaleSignedQuantity(masterNet, route.Ratio);
            if (actual != observedActual)
            {
                SupersedeSync(sync, "tail_validation", "manual_override", actual, expected);
                return;
            }
            if (masterNet != observedMasterNet || expected != observedExpected)
            {
                observedMasterNet = masterNet;
                observedExpected = expected;
            }
            if (expected == actual)
            {
                SupersedeSync(sync, "tail_validation", "already_synced", actual, expected);
                return;
            }
            if (GlitchSyncLifecycleState.DecideInitial(expected, actual)
                != GlitchSyncInitialAction.SubmitTail)
            {
                SupersedeSync(sync, "tail_validation", "truth_changed", actual, expected);
                return;
            }

            int quantity = Math.Abs(expected) - Math.Abs(actual);
            lock (_gate)
            {
                if (!IsCurrentSyncLifecycle(sync) || !sync.State.TryBeginTail(actual, expected))
                    return;
            }

            bool isLong = expected > 0;
            GlitchReplicationProtectionPlan plan = null;
            GlitchReplicationProtection.TryResolveMasterPlan(
                route.MasterAccountInstance,
                sync.Instrument,
                null,
                Math.Abs(observedMasterNet),
                isLong,
                out plan);

            JournalSync(sync.FollowerAccount, sync.Root, "tail_validation", "tail_required", actual, expected,
                "qty=" + quantity.ToString(CultureInfo.InvariantCulture)
                + "|offset=" + Math.Abs(actual).ToString(CultureInfo.InvariantCulture));
            FollowerOrderSubmission submission = SubmitFollowerEntry(
                route,
                sync.Instrument,
                isLong ? OrderAction.Buy : OrderAction.SellShort,
                quantity,
                Math.Abs(actual),
                plan,
                CatchUpSignalName,
                sync.IdentitySource,
                null,
                null,
                0,
                null);

            bool submitted = string.Equals(submission.Result, "submitted", StringComparison.OrdinalIgnoreCase);
            lock (_gate)
            {
                if (!IsCurrentSyncLifecycle(sync))
                    return;
                sync.TailEntrySignal = submission.Signal;
                sync.TailOrder = submission.Order;
                sync.State.MarkTailSubmitted(submitted);
                if (!submitted)
                    _syncByFollowerInstrument.Remove(sync.Key);
            }
            JournalSync(
                sync.FollowerAccount,
                sync.Root,
                "tail_submission",
                submitted ? "submitted" : "failed_" + CleanToken(submission.Result),
                actual,
                expected,
                "qty=" + quantity.ToString(CultureInfo.InvariantCulture)
                + "|offset=" + Math.Abs(actual).ToString(CultureInfo.InvariantCulture)
                + "|protection=" + (submission.ProtectionAvailable ? "mirrored" : "not_available"));
            if (submitted)
                ProcessSyncLifecycle(sync);
        }

        private void ProcessSyncAccountStateUpdate(Account account)
        {
            List<FollowerSyncLifecycle> active;
            lock (_gate)
            {
                active = _syncByFollowerInstrument.Values
                    .Where(sync => sync?.FollowerAccount != null
                        && string.Equals(sync.FollowerAccount.Name, account.Name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            foreach (FollowerSyncLifecycle sync in active)
                ProcessSyncLifecycle(sync);
        }

        private void ProcessSyncLifecycle(FollowerSyncLifecycle sync)
        {
            if (sync == null
                || !GlitchReplicationEngine.TryGetNetQuantityForInstrument(
                    sync.FollowerAccount,
                    sync.Instrument,
                    out int actual))
            {
                if (sync != null)
                    SupersedeSync(sync, "confirmation", "state_unavailable", 0, 0);
                return;
            }

            if (sync.ReduceTargetExpected.HasValue)
            {
                int actionSign = GlitchReplicationEngine.GetOrderActionSign(sync.ReduceOrder?.OrderAction ?? OrderAction.Sell);
                int expectedFromOwnedFills = sync.State.InitialActual
                    + (actionSign * Math.Max(0, sync.ReduceOrder?.Filled ?? 0));
                if (actual == sync.ReduceTargetExpected.Value)
                {
                    CancelSyncOwnedRemainder(sync, sync.ReduceOrder);
                    RemoveSyncLifecycle(sync);
                    JournalSync(
                        sync.FollowerAccount,
                        sync.Root,
                        "reduce_confirmation",
                        "confirmed",
                        actual,
                        sync.ReduceTargetExpected.Value,
                        null);
                    return;
                }
                if (actual != expectedFromOwnedFills)
                {
                    CancelSyncOwnedRemainder(sync, sync.ReduceOrder);
                    RemoveSyncLifecycle(sync);
                    JournalSync(
                        sync.FollowerAccount,
                        sync.Root,
                        "reduce_confirmation",
                        "manual_or_native_override",
                        actual,
                        sync.ReduceTargetExpected.Value,
                        null);
                    return;
                }

                return;
            }

            GlitchSyncObservation observation;
            bool awaitingFlat;
            bool awaitingTail;
            lock (_gate)
            {
                if (!IsCurrentSyncLifecycle(sync))
                    return;
                awaitingFlat = sync.State.IsAwaitingFlat;
                awaitingTail = sync.State.IsAwaitingTail;
                observation = awaitingFlat
                    ? sync.State.ObserveFlatten(actual, Math.Max(0, sync.FlattenOrder?.Filled ?? 0))
                    : awaitingTail
                        ? sync.State.ObserveTail(actual, Math.Max(0, sync.TailOrder?.Filled ?? 0))
                        : GlitchSyncObservation.None;
            }

            if (observation == GlitchSyncObservation.None)
                return;
            if (observation == GlitchSyncObservation.ManualOverride)
            {
                RemoveSyncLifecycle(sync);
                CancelSyncOwnedRemainder(sync, awaitingFlat ? sync.FlattenOrder : sync.TailOrder);
                JournalSync(
                    sync.FollowerAccount,
                    sync.Root,
                    awaitingFlat ? "flatten_confirmation" : "tail_confirmation",
                    "manual_override",
                    actual,
                    awaitingTail ? sync.State.TailExpected : 0,
                    null);
                return;
            }
            if (observation == GlitchSyncObservation.Completed)
            {
                RemoveSyncLifecycle(sync);
                JournalSync(
                    sync.FollowerAccount,
                    sync.Root,
                    "tail_confirmation",
                    "confirmed",
                    actual,
                    sync.State.TailExpected,
                    null);
                return;
            }

            GlitchCopyFollowerRoute route =
                FindConfiguredRoute(sync.MasterAccount, sync.FollowerAccount);
            if (route == null
                || !string.Equals(route.MasterAccount, sync.MasterAccount.Name, StringComparison.OrdinalIgnoreCase)
                || Math.Abs(route.Ratio - sync.Ratio) > 0.0000001d)
            {
                SupersedeSync(sync, "flatten_confirmation", "superseded_route_changed", actual, 0);
                return;
            }
            if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(
                    route.MasterAccountInstance,
                    sync.Instrument,
                    out int masterNet))
            {
                SupersedeSync(sync, "flatten_confirmation", "master_state_unavailable", actual, 0);
                return;
            }

            int expected = ScaleSignedQuantity(masterNet, route.Ratio);
            JournalSync(sync.FollowerAccount, sync.Root, "flatten_confirmation", "confirmed_flat", actual, expected, null);
            if (expected == 0)
            {
                SupersedeSync(sync, "completion", "confirmed_flat", actual, expected);
                return;
            }
            BeginSyncTail(sync, route, masterNet, actual, expected);
        }

        private void ProcessSyncFollowerOrderUpdate(Account followerAccount, Order order, string signal)
        {
            if (followerAccount == null || order?.Instrument == null || string.IsNullOrWhiteSpace(signal))
                return;
            string root = GlitchReplicationEngine.GetInstrumentRoot(order.Instrument);
            string key = BuildFollowerInstrumentKey(followerAccount, order.Instrument);
            FollowerSyncLifecycle sync;
            lock (_gate)
            {
                if (!_syncByFollowerInstrument.TryGetValue(key, out sync)
                    || sync == null)
                    return;
            }

            bool isFlattenOrder =
                string.Equals(sync.FlattenOrderSignal, signal, StringComparison.OrdinalIgnoreCase);
            bool isTailOrder =
                string.Equals(sync.TailEntrySignal, signal, StringComparison.OrdinalIgnoreCase);
            bool isReduceOrder =
                string.Equals(sync.ReduceOrderSignal, signal, StringComparison.OrdinalIgnoreCase);
            if (!isFlattenOrder && !isTailOrder && !isReduceOrder)
                return;
            if (order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled)
            {
                int actual = 0;
                GlitchReplicationEngine.TryGetNetQuantityForInstrument(followerAccount, order.Instrument, out actual);
                SupersedeSync(
                    sync,
                    isFlattenOrder ? "flatten_confirmation" : isReduceOrder ? "reduce_confirmation" : "tail_confirmation",
                    order.Filled > 0 ? "failed_partial_cancel" : "failed_rejected",
                    actual,
                    isFlattenOrder ? 0 : isReduceOrder ? sync.ReduceTargetExpected ?? 0 : sync.State.TailExpected);
                return;
            }
            ProcessSyncLifecycle(sync);
        }

        private void CancelSyncOwnedRemainder(FollowerSyncLifecycle sync, Order order)
        {
            if (sync?.FollowerAccount == null
                || order == null
                || !GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                 || (!string.Equals(order.Name, sync.FlattenOrderSignal, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(order.Name, sync.TailEntrySignal, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(order.Name, sync.ReduceOrderSignal, StringComparison.OrdinalIgnoreCase)))
                return;
            try
            {
                sync.FollowerAccount.Cancel(new[] { order });
                JournalSync(
                    sync.FollowerAccount,
                    sync.Root,
                    "manual_override",
                    "sync_order_cancel_submitted",
                    0,
                    0,
                    "signal=" + CleanToken(order.Name));
            }
            catch (Exception ex)
            {
                JournalSync(
                    sync.FollowerAccount,
                    sync.Root,
                    "manual_override",
                    "sync_order_cancel_failed_" + ex.GetType().Name,
                    0,
                    0,
                    "signal=" + CleanToken(order.Name));
            }
        }

        private void SupersedeSync(
            FollowerSyncLifecycle sync,
            string phase,
            string result,
            int actual,
            int expected)
        {
            if (sync == null)
                return;
            lock (_gate)
            {
                if (!IsCurrentSyncLifecycle(sync))
                    return;
                sync.State.Supersede();
                _syncByFollowerInstrument.Remove(sync.Key);
            }
            JournalSync(sync.FollowerAccount, sync.Root, phase, result, actual, expected, null);
        }

        private void RemoveSyncLifecycle(FollowerSyncLifecycle sync)
        {
            lock (_gate)
            {
                if (IsCurrentSyncLifecycle(sync))
                    _syncByFollowerInstrument.Remove(sync.Key);
            }
        }

        private bool IsCurrentSyncLifecycle(FollowerSyncLifecycle sync)
        {
            return sync != null
                && _syncByFollowerInstrument.TryGetValue(sync.Key, out FollowerSyncLifecycle current)
                && ReferenceEquals(current, sync);
        }

        private void JournalSync(
            Account followerAccount,
            string root,
            string phase,
            string result,
            int actual,
            int expected,
            string extra)
        {
            Journal?.Invoke(
                followerAccount?.Name ?? "Unknown",
                "replication_sync|origin=user_sync"
                + "|follower=" + CleanToken(followerAccount?.Name)
                + "|instrument=" + CleanToken(root)
                + "|phase=" + CleanToken(phase)
                + "|result=" + CleanToken(result)
                + "|actual=" + actual.ToString(CultureInfo.InvariantCulture)
                + "|expected=" + expected.ToString(CultureInfo.InvariantCulture)
                + (string.IsNullOrWhiteSpace(extra) ? string.Empty : "|" + extra));
        }

        private static string BuildFollowerInstrumentKey(Account followerAccount, Instrument instrument)
        {
            return (followerAccount?.Name?.Trim() ?? string.Empty)
                + "|"
                + (instrument?.FullName?.Trim() ?? string.Empty);
        }

        private static int ScaleSignedQuantity(int masterNet, double ratio)
        {
            return Math.Sign(masterNet)
                * GlitchReplicationProtection.ScaleFollowerQuantity(Math.Abs(masterNet), ratio);
        }

        private void FanOutOpening(
            Account masterAccount,
            GlitchCopyExecutionContext context,
            IReadOnlyList<GlitchCopyFollowerRoute> routes,
            GlitchReplicationProtectionPlan plan,
            int masterEntryQuantity)
        {
            string dedupKey = BuildExecutionDedupKey(masterAccount.Name, context);
            if (!TryRememberExecutionId(dedupKey))
                return;

            foreach (GlitchCopyFollowerRoute route in routes)
            {
                int quantity = ScaleExecution(context, route.Ratio);
                int followerAllocationOffset = ResolveFollowerAllocationOffset(context, route.Ratio);
                if (quantity <= 0)
                {
                    JournalCopy(route, context, 0, "copy_skip|ratio_rounds_to_zero");
                    continue;
                }

                SubmitFollowerEntry(
                    route,
                    context.Instrument,
                    context.Action,
                    quantity,
                    followerAllocationOffset,
                    plan,
                    CopySignalName,
                    dedupKey,
                    masterAccount,
                    context.OrderSignalName,
                    masterEntryQuantity,
                    context.EntryOrder);
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
                if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(route.FollowerAccount, context.Instrument, out int followerNet))
                {
                    JournalCopy(route, context, 0, "copy_close_skip|native_state_unavailable");
                    RaiseCritical?.Invoke(route.FollowerAccount.Name,
                        "Follower position state is unavailable; no close order was submitted.",
                        "FollowerCloseStateUnavailable|" + CleanToken(context.Instrument?.FullName ?? root));
                    continue;
                }
                int closable = context.Action == OrderAction.Sell
                    ? Math.Max(0, followerNet)
                    : context.Action == OrderAction.BuyToCover
                        ? Math.Max(0, -followerNet)
                        : 0;
                int requested = ScaleExecution(context, route.Ratio);
                if (closable <= 0)
                {
                    JournalCopy(route, context, 0, "copy_skip|follower_has_no_closable_exposure");
                    continue;
                }
                int quantity = Math.Min(requested, closable);
                if (quantity <= 0)
                {
                    JournalCopy(route, context, 0, "copy_skip|ratio_rounds_to_zero");
                    continue;
                }

                FollowerOrderSubmission submission = SubmitFollowerClose(
                    route.FollowerAccount,
                    context.Instrument,
                    context.Action,
                    quantity,
                    executionKey,
                    CopySignalName);
                JournalCopy(route, context, quantity, "copy_close|result=" + CleanToken(submission.Result)
                    + "|exec=" + CleanToken(executionKey));
            }
        }

        private FollowerOrderSubmission SubmitFollowerClose(
            Account account,
            Instrument instrument,
            OrderAction action,
            int quantity,
            string identity,
            string signalPrefix)
        {
            string accountToken = GlitchReplicationProtection.StableToken(account?.Name, 6);
            string closeToken = GlitchReplicationProtection.StableToken(identity, 8);
            string signal = signalPrefix + "-X-" + accountToken + "-" + closeToken;
            Order order = null;
            string result;
            if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(account, instrument, out int initialNet))
                return new FollowerOrderSubmission { Signal = signal, Result = "native_state_unavailable" };
            try
            {
                order = account?.CreateOrder(
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
                lock (_gate)
                {
                    _closesBySignal[signal] = new CloseState
                    {
                        Signal = signal,
                        Account = account,
                        Instrument = instrument,
                        Order = order,
                        InitialNet = initialNet,
                        TargetNet = initialNet
                            + (GlitchReplicationEngine.GetOrderActionSign(action) * quantity)
                    };
                }
                account.Submit(new[] { order });
                if (order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled)
                    throw new InvalidOperationException("close_rejected");
                result = "submitted";
            }
            catch (Exception ex)
            {
                lock (_gate)
                    _closesBySignal.Remove(signal);
                result = "failed_" + ex.GetType().Name;
                RaiseCritical?.Invoke(
                    account?.Name ?? "Unknown",
                    "Follower close submission failed: " + ex.GetType().Name,
                    "FollowerCloseFailed|" + GlitchReplicationEngine.GetInstrumentRoot(instrument));
            }
            return new FollowerOrderSubmission { Signal = signal, Order = order, Result = result };
        }

        private void TrackCloseOrder(Account account, Order order, string signal)
        {
            CloseState lifecycle;
            lock (_gate)
            {
                if (!_closesBySignal.TryGetValue(signal, out lifecycle)
                    || lifecycle == null
                    || !string.Equals(lifecycle.Account?.Name, account?.Name, StringComparison.OrdinalIgnoreCase))
                    return;
                lifecycle.Order = order;
                if (!GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                    _closesBySignal.Remove(signal);
            }
        }

        private void TrySubmitAttributedRecoveryClose(
            FollowerEntryLifecycle lifecycle,
            int attributableQuantity,
            string reason)
        {
            if (lifecycle?.Account == null || lifecycle.Instrument == null)
                return;
            if (attributableQuantity <= 0)
            {
                Journal?.Invoke(
                    lifecycle.Account.Name,
                    "follower_recovery|instrument="
                    + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(lifecycle.Instrument))
                    + "|reason=" + CleanToken(reason)
                    + "|result=manual_override_unattributed");
                return;
            }
            lock (_gate)
            {
                if (lifecycle.RecoveryCloseSubmitted)
                    return;
                lifecycle.RecoveryCloseSubmitted = true;
            }

            string root = GlitchReplicationEngine.GetInstrumentRoot(lifecycle.Instrument);
            if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(
                    lifecycle.Account,
                    lifecycle.Instrument,
                    out int followerNet)
                || followerNet == 0
                || (followerNet > 0) != lifecycle.IsLong)
            {
                Journal?.Invoke(
                    lifecycle.Account.Name,
                    "follower_recovery|instrument=" + CleanToken(root)
                    + "|reason=" + CleanToken(reason)
                    + "|result=manual_override");
                return;
            }

            int quantity = Math.Min(attributableQuantity, Math.Abs(followerNet));
            FollowerOrderSubmission submission = SubmitFollowerClose(
                lifecycle.Account,
                lifecycle.Instrument,
                lifecycle.IsLong ? OrderAction.Sell : OrderAction.BuyToCover,
                quantity,
                lifecycle.EntrySignal + "|" + reason,
                CopySignalName);
            Journal?.Invoke(
                lifecycle.Account.Name,
                "follower_recovery|instrument=" + CleanToken(root)
                + "|reason=" + CleanToken(reason)
                + "|attributable_qty=" + attributableQuantity.ToString(CultureInfo.InvariantCulture)
                + "|native_same_side_qty=" + Math.Abs(followerNet).ToString(CultureInfo.InvariantCulture)
                + "|submitted_qty=" + quantity.ToString(CultureInfo.InvariantCulture)
                + "|result=" + CleanToken(submission.Result));
        }

        private FollowerOrderSubmission SubmitFollowerEntry(
            GlitchCopyFollowerRoute route,
            Instrument instrument,
            OrderAction action,
            int quantity,
            int followerAllocationOffset,
            GlitchReplicationProtectionPlan plan,
            string signalPrefix,
            string identitySource,
            Account masterAccount,
            string masterEntrySignal,
            int masterEntryQuantity,
            Order masterEntryOrder)
        {
            if (route?.FollowerAccount == null || instrument == null || quantity <= 0)
                return new FollowerOrderSubmission { Result = "invalid_request" };
            List<GlitchScaledProtectionLeg> scaled = null;
            bool protectionAvailable = plan != null
                && GlitchReplicationProtection.TryScalePlanSlice(
                    plan,
                    route.Ratio,
                    followerAllocationOffset,
                    quantity,
                    out scaled);

            string accountToken = GlitchReplicationProtection.StableToken(route.FollowerAccount.Name, 6);
            string entryToken = GlitchReplicationProtection.StableToken(identitySource, 8);
            string signal = BuildFollowerEntrySignal(
                signalPrefix,
                accountToken,
                entryToken,
                route.Ratio,
                followerAllocationOffset);
            var lifecycle = new FollowerEntryLifecycle
            {
                EntrySignal = signal,
                EntryToken = entryToken,
                Account = route.FollowerAccount,
                Instrument = instrument,
                IsLong = action == OrderAction.Buy,
                ScaledLegs = scaled,
                ProtectionAvailable = protectionAvailable,
                MasterAccountName = masterAccount?.Name?.Trim(),
                MasterEntrySignal = masterEntrySignal?.Trim(),
                MasterEntryQuantity = Math.Max(0, masterEntryQuantity),
                MasterEntryOrder = masterEntryOrder,
                RouteRatio = route.Ratio,
                FollowerAllocationOffset = Math.Max(0, followerAllocationOffset),
                SubmittedQuantity = quantity
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

            lock (_gate)
                lifecycle.EntryOrder = order;

            Journal?.Invoke(route.FollowerAccount.Name,
                "copy_entry|master=" + CleanToken(route.MasterAccount)
                + "|follower=" + CleanToken(route.FollowerAccount.Name)
                + "|instrument=" + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(instrument))
                + "|ratio=" + route.Ratio.ToString("0.####", CultureInfo.InvariantCulture)
                + "|qty=" + quantity.ToString(CultureInfo.InvariantCulture)
                + "|protection=" + (protectionAvailable ? "mirrored" : "not_available")
                + "|result=" + CleanToken(result));

            if (order != null && order.Filled > 0)
                ProcessFollowerOrderUpdate(route.FollowerAccount, order);
            if (!string.Equals(result, "submitted", StringComparison.OrdinalIgnoreCase))
                RaiseCritical?.Invoke(
                    route.FollowerAccount.Name,
                    "Follower entry was not confirmed submitted; Glitch will not retry it automatically.",
                    "FollowerEntrySubmitUnknown|" + GlitchReplicationEngine.GetInstrumentRoot(instrument));
            return new FollowerOrderSubmission
            {
                Signal = signal,
                Order = order,
                Result = result,
                ProtectionAvailable = protectionAvailable
            };
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

            bool isLong;
            if (entryOrder.OrderAction == OrderAction.Buy)
                isLong = true;
            else if (entryOrder.OrderAction == OrderAction.SellShort)
                isLong = false;
            else
                return false;

            int requestedQuantity = Math.Max(0, entryOrder.Filled);
            string entryToken = ExtractFollowerEntryToken(entrySignal);
            if (string.IsNullOrWhiteSpace(entryToken))
                return false;
            if (!TryReadFollowerAllocationMetadata(
                    entrySignal,
                    out double recoveredRatio,
                    out int followerAllocationOffset))
            {
                lifecycle = CreateObservationalRecoveredLifecycle(
                    followerAccount,
                    entryOrder,
                    entrySignal,
                    entryToken,
                    isLong,
                    "ambiguous_allocation_metadata_recovered",
                    0,
                    0);
                return true;
            }
            GlitchCopyFollowerRoute route = FindUniqueConfiguredRouteForFollower(followerAccount);
            Account masterAccount = route?.MasterAccountInstance;
            if (masterAccount == null)
            {
                lifecycle = CreateObservationalRecoveredLifecycle(
                    followerAccount,
                    entryOrder,
                    entrySignal,
                    entryToken,
                    isLong,
                    "ambiguous_route_recovered",
                    recoveredRatio,
                    followerAllocationOffset);
                return true;
            }
            if (BitConverter.DoubleToInt64Bits(recoveredRatio)
                != BitConverter.DoubleToInt64Bits(route.Ratio))
            {
                lifecycle = CreateObservationalRecoveredLifecycle(
                    followerAccount,
                    entryOrder,
                    entrySignal,
                    entryToken,
                    isLong,
                    "ambiguous_route_ratio_changed_recovered",
                    recoveredRatio,
                    followerAllocationOffset);
                return true;
            }

            string root = GlitchReplicationEngine.GetInstrumentRoot(entryOrder.Instrument);
            if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(masterAccount, entryOrder.Instrument, out int masterNet))
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
            {
                lifecycle = CreateObservationalRecoveredLifecycle(
                    followerAccount,
                    entryOrder,
                    entrySignal,
                    entryToken,
                    isLong,
                    "not_available_recovered",
                    recoveredRatio,
                    followerAllocationOffset);
                return true;
            }

            if (!GlitchReplicationProtection.TryScalePlanSlice(
                    plan,
                    recoveredRatio,
                    followerAllocationOffset,
                    requestedQuantity,
                    out List<GlitchScaledProtectionLeg> scaled))
            {
                lifecycle = CreateObservationalRecoveredLifecycle(
                    followerAccount,
                    entryOrder,
                    entrySignal,
                    entryToken,
                    isLong,
                    "ambiguous_allocation_slice_recovered",
                    recoveredRatio,
                    followerAllocationOffset);
                return true;
            }

            if (!TryCountCompleteFollowerProtection(followerAccount, entryOrder.Instrument, entryToken, isLong, out int protectedQuantity)
                || protectedQuantity > requestedQuantity)
                return false;

            lifecycle = new FollowerEntryLifecycle
            {
                EntrySignal = entrySignal,
                EntryToken = entryToken,
                Account = followerAccount,
                Instrument = entryOrder.Instrument,
                IsLong = isLong,
                RouteRatio = recoveredRatio,
                FollowerAllocationOffset = followerAllocationOffset,
                SubmittedQuantity = requestedQuantity,
                EntryOrder = entryOrder,
                ScaledLegs = scaled,
                ProtectionAvailable = true,
                ProtectedQuantity = protectedQuantity
            };
            Journal?.Invoke(followerAccount.Name,
                "follower_protection|entry=" + CleanToken(entrySignal)
                + "|result=recent_lifecycle_recovered|protected_qty="
                + protectedQuantity.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        private FollowerEntryLifecycle CreateObservationalRecoveredLifecycle(
            Account followerAccount,
            Order entryOrder,
            string entrySignal,
            string entryToken,
            bool isLong,
            string result,
            double routeRatio,
            int followerAllocationOffset)
        {
            var lifecycle = new FollowerEntryLifecycle
            {
                EntrySignal = entrySignal,
                EntryToken = entryToken,
                Account = followerAccount,
                Instrument = entryOrder.Instrument,
                IsLong = isLong,
                RouteRatio = routeRatio,
                FollowerAllocationOffset = Math.Max(0, followerAllocationOffset),
                SubmittedQuantity = Math.Max(0, entryOrder.Filled),
                EntryOrder = entryOrder,
                ProtectionAvailable = false
            };
            Journal?.Invoke(
                followerAccount.Name,
                "follower_protection|entry=" + CleanToken(entrySignal)
                + "|result=" + CleanToken(result));
            return lifecycle;
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
            return TryGetRouteSnapshot(masterAccount.Name, false, out List<GlitchCopyFollowerRoute> routes)
                ? routes.FirstOrDefault(route => route?.FollowerAccount != null
                    && string.Equals(route.FollowerAccount.Name, followerAccount.Name, StringComparison.OrdinalIgnoreCase))
                : null;
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
                    && ParseFollowerSignalKind(order.Name) == FollowerSignalKind.Protection
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

        private static string BuildFollowerEntrySignal(
            string signalPrefix,
            string accountToken,
            string entryToken,
            double ratio,
            int followerAllocationOffset)
        {
            string ratioBits = BitConverter.DoubleToInt64Bits(ratio)
                .ToString("x16", CultureInfo.InvariantCulture);
            return signalPrefix
                + "-E-" + accountToken
                + "-" + entryToken
                + "-R" + ratioBits
                + "-O" + Math.Max(0, followerAllocationOffset).ToString("x8", CultureInfo.InvariantCulture);
        }

        internal static bool TryReadFollowerAllocationMetadata(
            string signal,
            out double ratio,
            out int followerAllocationOffset)
        {
            ratio = 0;
            followerAllocationOffset = 0;
            if (string.IsNullOrWhiteSpace(signal))
                return false;
            string[] segments = signal.Trim().Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            int entryIndex = Array.FindIndex(
                segments,
                segment => string.Equals(segment, "E", StringComparison.OrdinalIgnoreCase));
            if (entryIndex < 0 || entryIndex + 4 >= segments.Length)
                return false;

            string ratioToken = segments[entryIndex + 3];
            string offsetToken = segments[entryIndex + 4];
            if (ratioToken.Length != 17
                || !ratioToken.StartsWith("R", StringComparison.OrdinalIgnoreCase)
                || offsetToken.Length != 9
                || !offsetToken.StartsWith("O", StringComparison.OrdinalIgnoreCase)
                || !long.TryParse(
                    ratioToken.Substring(1),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out long ratioBits)
                || !int.TryParse(
                    offsetToken.Substring(1),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out followerAllocationOffset))
                return false;

            ratio = BitConverter.Int64BitsToDouble(ratioBits);
            return ratio > 0
                && !double.IsNaN(ratio)
                && !double.IsInfinity(ratio)
                && followerAllocationOffset >= 0;
        }

        private static string ExtractFollowerEntryToken(string signal)
        {
            if (string.IsNullOrWhiteSpace(signal))
                return null;
            string[] segments = signal.Trim().Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            int entryIndex = Array.FindIndex(
                segments,
                segment => string.Equals(segment, "E", StringComparison.OrdinalIgnoreCase));
            return entryIndex < 0 || entryIndex + 2 >= segments.Length
                ? null
                : segments[entryIndex + 2].Trim();
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

        private void TryAttachLateFollowerProtection(Account masterAccount, Order masterOrder)
        {
            if (masterAccount == null || masterOrder?.Instrument == null)
                return;

            string masterName = masterAccount.Name?.Trim() ?? string.Empty;
            string root = GlitchReplicationEngine.GetInstrumentRoot(masterOrder.Instrument);
            List<FollowerEntryLifecycle> candidates;
            lock (_gate)
            {
                candidates = _entriesBySignal.Values
                    .Where(lifecycle => lifecycle != null
                        && !lifecycle.ProtectionAvailable
                        && lifecycle.SubmittedQuantity > 0
                        && lifecycle.RouteRatio > 0
                        && lifecycle.MasterEntryQuantity > 0
                        && !string.IsNullOrWhiteSpace(lifecycle.MasterEntrySignal)
                        && string.Equals(lifecycle.MasterAccountName, masterName, StringComparison.OrdinalIgnoreCase)
                        && lifecycle.Instrument != null
                        && string.Equals(
                            GlitchReplicationEngine.GetInstrumentRoot(lifecycle.Instrument),
                            root,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            foreach (FollowerEntryLifecycle lifecycle in candidates)
            {
                int requiredMasterQuantity = Math.Max(
                    lifecycle.MasterEntryQuantity,
                    Math.Max(0, lifecycle.MasterEntryOrder?.Filled ?? 0));
                if (!GlitchReplicationProtection.TryResolveMasterPlan(
                        masterAccount,
                        lifecycle.Instrument,
                        lifecycle.MasterEntrySignal,
                        requiredMasterQuantity,
                        lifecycle.IsLong,
                        out GlitchReplicationProtectionPlan plan)
                    || !GlitchReplicationProtection.TryScalePlanSlice(
                        plan,
                        lifecycle.RouteRatio,
                        lifecycle.FollowerAllocationOffset,
                        lifecycle.SubmittedQuantity,
                        out List<GlitchScaledProtectionLeg> scaled))
                    continue;
                if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(lifecycle.Account, lifecycle.Instrument, out int followerNet)
                    || followerNet == 0
                    || (followerNet > 0) != lifecycle.IsLong)
                    continue;

                Order entryOrder;
                lock (_gate)
                {
                    if (lifecycle.ProtectionAvailable)
                        continue;
                    lifecycle.ScaledLegs = scaled;
                    lifecycle.ProtectionAvailable = true;
                    entryOrder = lifecycle.EntryOrder;
                }

                Journal?.Invoke(lifecycle.Account?.Name ?? "Unknown",
                    "follower_protection|entry=" + CleanToken(lifecycle.EntrySignal)
                    + "|result=late_plan_attached");
                if (entryOrder != null)
                {
                    ProcessFollowerOrderUpdate(lifecycle.Account, entryOrder);
                    ReconcileFollowerProtection(lifecycle.Account);
                }
            }
        }

        private void MirrorMasterProtection(Account masterAccount, Order masterOrder)
        {
            bool isStop = GlitchReplicationEngine.IsStopLikeOrder(masterOrder);
            bool isTarget = masterOrder.OrderType == OrderType.Limit;
            if (masterOrder.Instrument == null
                || (!isStop && !isTarget)
                || !GlitchReplicationEngine.IsWorkingOrderState(masterOrder.OrderState)
                || (isStop ? masterOrder.StopPrice : masterOrder.LimitPrice) <= 0)
                return;
            if (!TryGetRouteSnapshot(masterAccount.Name, false, out List<GlitchCopyFollowerRoute> routes))
                return;

            string sourceToken = GlitchReplicationProtection.BuildSourceToken(masterOrder.Name, masterOrder.Oco);
            string protectionKind = isStop ? "stop" : "target";
            string prefix = CopySignalName + (isStop ? "-S-" : "-T-") + sourceToken + "-";
            double masterPrice = isStop ? masterOrder.StopPrice : masterOrder.LimitPrice;
            string root = GlitchReplicationEngine.GetInstrumentRoot(masterOrder.Instrument);
            foreach (GlitchCopyFollowerRoute route in routes)
            {
                if (!TrySnapshotOrders(route.FollowerAccount, out Order[] orders))
                {
                    RaiseCritical?.Invoke(route.FollowerAccount.Name,
                        "Follower order state is unavailable; the master " + protectionKind + " was not mirrored.",
                        "FollowerProtectionMirrorStateUnavailable|" + root + "|" + protectionKind);
                    continue;
                }
                List<Order> changes = orders
                    .Where(order => order?.Instrument != null
                        && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                        && (order.Name ?? string.Empty).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(order.Instrument), root, StringComparison.OrdinalIgnoreCase)
                        && Math.Abs((isStop ? order.StopPrice : order.LimitPrice) - masterPrice) > 0.0000001d)
                    .ToList();
                if (changes.Count == 0)
                    continue;
                try
                {
                    foreach (Order followerOrder in changes)
                    {
                        if (isStop)
                            followerOrder.StopPriceChanged = masterPrice;
                        else
                            followerOrder.LimitPriceChanged = masterPrice;
                    }
                    route.FollowerAccount.Change(changes.ToArray());
                }
                catch (Exception ex)
                {
                    RaiseCritical?.Invoke(route.FollowerAccount.Name,
                        "Follower " + protectionKind + " could not mirror the master: " + ex.GetType().Name,
                        "FollowerProtectionMirrorFailed|" + root + "|" + protectionKind);
                }
            }
        }

        private void ReconcileFollowerProtection(Account account)
        {
            if (account == null || !TrySnapshotOrders(account, out Order[] orders))
                return;
            if (!AccountOwnsGlitchReplicationState(account, orders))
                return;

            HashSet<string> instrumentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Order order in orders)
            {
                if (order?.Instrument == null
                    || string.IsNullOrWhiteSpace(order.Instrument.FullName)
                    || ParseFollowerSignalKind(order.Name) == FollowerSignalKind.None)
                    continue;
                instrumentNames.Add(order.Instrument.FullName);
            }

            lock (_gate)
            {
                foreach (FollowerEntryLifecycle lifecycle in _entriesBySignal.Values)
                {
                    if (lifecycle?.Account == null
                        || lifecycle.Instrument == null
                        || string.IsNullOrWhiteSpace(lifecycle.Instrument.FullName)
                        || !string.Equals(lifecycle.Account.Name, account.Name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    instrumentNames.Add(lifecycle.Instrument.FullName);
                }
            }

            foreach (string instrumentName in instrumentNames)
            {
                Instrument instrument = orders
                    .FirstOrDefault(order => order?.Instrument != null
                        && string.Equals(order.Instrument.FullName, instrumentName, StringComparison.OrdinalIgnoreCase))
                    ?.Instrument;
                if (instrument == null)
                {
                    lock (_gate)
                    {
                        instrument = _entriesBySignal.Values
                            .FirstOrDefault(lifecycle => lifecycle?.Instrument != null
                                && string.Equals(lifecycle.Instrument.FullName, instrumentName, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(lifecycle.Account?.Name, account.Name, StringComparison.OrdinalIgnoreCase))
                            ?.Instrument;
                    }
                }
                if (instrument == null
                    || !GlitchReplicationEngine.TryGetNetQuantityForInstrument(account, instrument, out int netQuantity))
                    continue;

                if (netQuantity == 0)
                    CancelOwnedOrdersAtFlat(account, instrument, orders);
                else
                {
                    ResizeProtection(account, instrument, orders, netQuantity);
                    CancelUnsafeCloseRemainders(account, instrument, orders, netQuantity);
                }
            }
        }

        private void ReconcileCloses(Account account)
        {
            if (account == null)
                return;
            List<CloseState> lifecycles;
            lock (_gate)
            {
                lifecycles = _closesBySignal.Values
                    .Where(item => item?.Account != null
                        && string.Equals(item.Account.Name, account.Name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            var cancellations = new List<Order>();
            foreach (CloseState lifecycle in lifecycles)
            {
                Order order = lifecycle.Order;
                if (order == null || lifecycle.Instrument == null)
                    continue;
                if (!GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                    || RemainingQuantity(order) <= 0)
                {
                    lock (_gate)
                        _closesBySignal.Remove(lifecycle.Signal);
                    continue;
                }
                if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(
                        account,
                        lifecycle.Instrument,
                        out int actual))
                    continue;
                if (actual == 0)
                    continue;
                int expectedFromOwnedFills = lifecycle.InitialNet
                    + (GlitchReplicationEngine.GetOrderActionSign(order.OrderAction)
                        * Math.Max(0, order.Filled));
                if (actual == expectedFromOwnedFills && actual != lifecycle.TargetNet)
                    continue;
                lock (_gate)
                {
                    if (lifecycle.CancelRequested)
                        continue;
                    lifecycle.CancelRequested = true;
                }
                cancellations.Add(order);
            }
            if (cancellations.Count == 0)
                return;
            try
            {
                account.Cancel(cancellations.ToArray());
                Journal?.Invoke(
                    account.Name,
                    "follower_close_reconcile|result=cancel_owned_remainder|orders="
                    + cancellations.Count.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    foreach (CloseState lifecycle in lifecycles.Where(item => cancellations.Contains(item.Order)))
                        lifecycle.CancelRequested = false;
                }
                RaiseCritical?.Invoke(
                    account.Name,
                    "Follower close remainder could not be cancelled after native position changed: " + ex.GetType().Name,
                    "FollowerCloseRemainderCancelFailed");
            }
        }

        private void CancelUnsafeCloseRemainders(
            Account account,
            Instrument instrument,
            Order[] orders,
            int netQuantity)
        {
            if (account == null || instrument == null || netQuantity == 0)
                return;

            int closable = Math.Abs(netQuantity);
            List<Order> closeOrders = orders
                .Where(order => order?.Instrument != null
                    && string.Equals(order.Instrument.FullName, instrument.FullName, StringComparison.OrdinalIgnoreCase)
                    && ParseFollowerSignalKind(order.Name) == FollowerSignalKind.Close
                    && GlitchReplicationEngine.CanCancelOrder(order))
                .ToList();
            int totalRemaining = closeOrders.Sum(RemainingQuantity);
            int excess = totalRemaining - closable;
            if (excess <= 0)
                return;

            var cancellations = new List<Order>();
            foreach (Order order in closeOrders
                .OrderBy(item => RemainingQuantity(item))
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (excess <= 0)
                    break;
                int remaining = RemainingQuantity(order);
                if (remaining <= 0)
                    continue;
                cancellations.Add(order);
                excess -= remaining;
            }

            if (cancellations.Count == 0)
                return;
            try
            {
                account.Cancel(cancellations.ToArray());
                Journal?.Invoke(
                    account.Name,
                    "excess_close_remainder_cancel|instrument=" + CleanToken(instrument.FullName)
                    + "|orders=" + cancellations.Count.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                RaiseCritical?.Invoke(
                    account.Name,
                    "Excess follower close remainder could not be cancelled: " + ex.GetType().Name,
                    "FollowerCloseRemainderCancelFailed|" + CleanToken(instrument.FullName));
            }
        }

        private bool AccountOwnsGlitchReplicationState(Account account, Order[] orders)
        {
            if (account == null)
                return false;
            if (orders != null && orders.Any(order =>
                    order != null
                    && !string.IsNullOrWhiteSpace(order.Name)
                    && ParseFollowerSignalKind(order.Name) != FollowerSignalKind.None))
                return true;

            lock (_gate)
            {
                if (_entriesBySignal.Values.Any(lifecycle =>
                        lifecycle?.Account != null
                        && string.Equals(lifecycle.Account.Name, account.Name, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            return IsConfiguredFollower(account);
        }

        private void CancelOwnedOrdersAtFlat(Account account, Instrument instrument, Order[] orders)
        {
            List<Order> cancellations = orders
                .Where(order => order?.Instrument != null
                    && string.Equals(order.Instrument.FullName, instrument.FullName, StringComparison.OrdinalIgnoreCase)
                    && GlitchReplicationEngine.CanCancelOrder(order)
                    && (ParseFollowerSignalKind(order.Name) == FollowerSignalKind.Protection
                        || ParseFollowerSignalKind(order.Name) == FollowerSignalKind.Close))
                .ToList();
            if (cancellations.Count == 0)
                return;
            try
            {
                account.Cancel(cancellations.ToArray());
                Journal?.Invoke(
                    account.Name,
                    "follower_protection_reconcile|instrument=" + CleanToken(instrument.FullName)
                    + "|result=flat_cancel|orders=" + cancellations.Count.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                RaiseCritical?.Invoke(
                    account.Name,
                    "Glitch-owned follower orders could not be cancelled at flat: " + ex.GetType().Name,
                    "FollowerFlatCancelFailed|" + CleanToken(instrument.FullName));
            }
        }

        private void ResizeProtection(
            Account account,
            Instrument instrument,
            Order[] orders,
            int netQuantity)
        {
            var protectionOrders = orders
                .Where(order => order?.Instrument != null
                    && string.Equals(order.Instrument.FullName, instrument.FullName, StringComparison.OrdinalIgnoreCase)
                    && ParseFollowerSignalKind(order.Name) == FollowerSignalKind.Protection
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                    && !string.IsNullOrWhiteSpace(order.Oco))
                .ToList();
            if (protectionOrders.Count == 0)
                return;

            var units = protectionOrders
                .GroupBy(order => order.Oco.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    Orders = group.ToList(),
                    Quantity = group.Max(RemainingQuantity)
                })
                .Where(unit => unit.Quantity > 0)
                .OrderBy(unit => unit.Orders.Count)
                .ThenByDescending(unit => unit.Orders[0].Oco, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int excess = units.Sum(unit => unit.Quantity) - Math.Abs(netQuantity);
            if (excess <= 0)
                return;

            var cancellations = new List<Order>();
            var changes = new List<Order>();
            foreach (var unit in units)
            {
                if (excess <= 0)
                    break;
                int reduction = Math.Min(excess, unit.Quantity);
                int desiredRemaining = unit.Quantity - reduction;
                if (desiredRemaining == 0)
                {
                    cancellations.AddRange(unit.Orders.Where(GlitchReplicationEngine.CanCancelOrder));
                }
                else
                {
                    foreach (Order order in unit.Orders)
                    {
                        int currentRemaining = RemainingQuantity(order);
                        int desiredOrderRemaining = Math.Min(currentRemaining, desiredRemaining);
                        int desiredTotal = order.Filled + desiredOrderRemaining;
                        if (desiredTotal == order.Quantity || desiredTotal == order.QuantityChanged)
                            continue;
                        order.QuantityChanged = desiredTotal;
                        changes.Add(order);
                    }
                }
                excess -= reduction;
            }

            if (cancellations.Count == 0 && changes.Count == 0)
                return;
            if (changes.Count > 0)
            {
                try
                {
                    account.Change(changes.ToArray());
                    Journal?.Invoke(
                        account.Name,
                        "excess_protection_resize|instrument=" + CleanToken(instrument.FullName)
                        + "|changed=" + changes.Count.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    RaiseCritical?.Invoke(
                        account.Name,
                        "Excess follower protection could not be resized: " + ex.GetType().Name,
                        "FollowerProtectionResizeFailed|" + CleanToken(instrument.FullName));
                }
            }
            if (cancellations.Count > 0)
            {
                try
                {
                    account.Cancel(cancellations.ToArray());
                    Journal?.Invoke(
                        account.Name,
                        "excess_protection_cancel|instrument=" + CleanToken(instrument.FullName)
                        + "|orders=" + cancellations.Count.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    RaiseCritical?.Invoke(
                        account.Name,
                        "Excess follower protection could not be cancelled: " + ex.GetType().Name,
                        "FollowerProtectionTrimFailed|" + CleanToken(instrument.FullName));
                }
            }
        }

        private void CleanupFlatFollowerOrders(Account account)
        {
            if (!TrySnapshotOrders(account, out Order[] orders))
                return;
            if (!AccountOwnsGlitchReplicationState(account, orders))
                return;

            List<Instrument> lifecycleInstruments;
            lock (_gate)
                lifecycleInstruments = _entriesBySignal.Values
                    .Where(lifecycle => lifecycle?.Instrument != null
                        && string.Equals(lifecycle.Account?.Name, account.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(lifecycle => lifecycle.Instrument)
                    .GroupBy(instrument => instrument.FullName, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();

            foreach (Instrument instrument in lifecycleInstruments)
            {
                bool hasWorkingInstrumentOrder = orders.Any(order => order?.Instrument != null
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                    && ParseFollowerSignalKind(order.Name) != FollowerSignalKind.None
                    && string.Equals(order.Instrument.FullName, instrument.FullName, StringComparison.OrdinalIgnoreCase));
                if (hasWorkingInstrumentOrder
                    || !GlitchReplicationEngine.TryGetNetQuantityForInstrument(account, instrument, out int netQuantity)
                    || netQuantity != 0)
                    continue;

                lock (_gate)
                {
                    foreach (string signal in _entriesBySignal
                        .Where(item => string.Equals(item.Value?.Account?.Name, account.Name, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(
                                item.Value?.Instrument?.FullName,
                                instrument.FullName,
                                StringComparison.OrdinalIgnoreCase))
                        .Select(item => item.Key)
                        .ToList())
                        _entriesBySignal.Remove(signal);
                }
            }
        }

        private bool TryGetRouteSnapshot(
            string masterName,
            bool requireEnabled,
            out List<GlitchCopyFollowerRoute> routes)
        {
            routes = null;
            lock (_gate)
            {
                if ((requireEnabled && !_enabled)
                    || string.IsNullOrWhiteSpace(masterName)
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

        private static FollowerSignalKind ParseFollowerSignalKind(string signal)
        {
            if (string.IsNullOrWhiteSpace(signal))
                return FollowerSignalKind.None;
            string value = signal.Trim();
            bool isCopy = value.StartsWith(CopySignalName + "-", StringComparison.OrdinalIgnoreCase);
            bool isCatchUp = value.StartsWith(CatchUpSignalName + "-", StringComparison.OrdinalIgnoreCase);
            if (!isCopy && !isCatchUp)
                return FollowerSignalKind.None;

            string suffix = value.Substring(isCopy ? CopySignalName.Length : CatchUpSignalName.Length);
            if (suffix.StartsWith("-E-", StringComparison.OrdinalIgnoreCase))
                return FollowerSignalKind.Entry;
            if (suffix.StartsWith("-X-", StringComparison.OrdinalIgnoreCase))
                return FollowerSignalKind.Close;
            if (isCopy
                && (suffix.StartsWith("-S-", StringComparison.OrdinalIgnoreCase)
                    || suffix.StartsWith("-T-", StringComparison.OrdinalIgnoreCase)))
                return FollowerSignalKind.Protection;
            return FollowerSignalKind.None;
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
                Math.Max(
                    Math.Max(0, context.EntryOrderFilledQuantity),
                    Math.Max(0, context.EntryOrder?.Filled ?? 0)));
        }

        private static int ScaleExecution(GlitchCopyExecutionContext context, double ratio)
        {
            if (context == null || context.Quantity <= 0 || ratio <= 0)
                return 0;
            int filled = Math.Max(context.Quantity, context.EntryOrderFilledQuantity);
            int before = Math.Max(0, filled - context.Quantity);
            return GlitchReplicationProtection.ScaleFollowerQuantity(filled, ratio)
                - GlitchReplicationProtection.ScaleFollowerQuantity(before, ratio);
        }

        private static int ResolveFollowerAllocationOffset(GlitchCopyExecutionContext context, double ratio)
        {
            if (context == null || context.Quantity <= 0 || ratio <= 0)
                return 0;
            int filled = Math.Max(context.Quantity, context.EntryOrderFilledQuantity);
            int before = Math.Max(0, filled - context.Quantity);
            return GlitchReplicationProtection.ScaleFollowerQuantity(before, ratio);
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

        private enum FollowerSignalKind
        {
            None,
            Entry,
            Protection,
            Close
        }

        private sealed class FollowerOrderSubmission
        {
            public string Signal { get; set; }
            public Order Order { get; set; }
            public string Result { get; set; }
            public bool ProtectionAvailable { get; set; }
        }

        private sealed class FollowerSyncLifecycle
        {
            public string Key { get; set; }
            public string Root { get; set; }
            public Account MasterAccount { get; set; }
            public Account FollowerAccount { get; set; }
            public Instrument Instrument { get; set; }
            public double Ratio { get; set; }
            public string IdentitySource { get; set; }
            public string FlattenOrderSignal { get; set; }
            public Order FlattenOrder { get; set; }
            public string ReduceOrderSignal { get; set; }
            public Order ReduceOrder { get; set; }
            public int? ReduceTargetExpected { get; set; }
            public string TailEntrySignal { get; set; }
            public Order TailOrder { get; set; }
            public GlitchSyncLifecycleState State { get; set; }
        }

        private sealed class CloseState
        {
            public string Signal { get; set; }
            public Account Account { get; set; }
            public Instrument Instrument { get; set; }
            public Order Order { get; set; }
            public int InitialNet { get; set; }
            public int TargetNet { get; set; }
            public bool CancelRequested { get; set; }
        }

        private sealed class FollowerEntryLifecycle
        {
            public string EntrySignal { get; set; }
            public string EntryToken { get; set; }
            public Account Account { get; set; }
            public Instrument Instrument { get; set; }
            public bool IsLong { get; set; }
            public string MasterAccountName { get; set; }
            public string MasterEntrySignal { get; set; }
            public int MasterEntryQuantity { get; set; }
            public Order MasterEntryOrder { get; set; }
            public double RouteRatio { get; set; }
            public int FollowerAllocationOffset { get; set; }
            public int SubmittedQuantity { get; set; }
            public Order EntryOrder { get; set; }
            public int ProtectedQuantity { get; set; }
            public bool ProtectionSubmissionInProgress { get; set; }
            public bool ProtectionFailed { get; set; }
            public bool ProtectionAvailable { get; set; }
            public bool RecoveryCloseSubmitted { get; set; }
            public List<GlitchScaledProtectionLeg> ScaledLegs { get; set; }
        }
    }
}
