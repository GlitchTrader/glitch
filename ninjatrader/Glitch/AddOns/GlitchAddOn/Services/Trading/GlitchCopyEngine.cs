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
        public int EntryOrderQuantity { get; set; }
        public int? PostExecutionNetQuantity { get; set; }
        public bool IsRuntimeEventSnapshot { get; set; }
        public string ExecutionOperation { get; set; }
        public bool IsSodExecution { get; set; }
        public Order EntryOrder { get; set; }
        public string OrderIdentity { get; set; }
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
            if (actual == 0)
                return GlitchSyncObservation.ContinueTail;
            int expectedActual = InitialActual
                - (Math.Sign(InitialActual) * Math.Max(0, ownedFilled));
            if (actual != expectedActual)
            {
                _phase = Phase.Terminal;
                return GlitchSyncObservation.ManualOverride;
            }
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
            if (actual == TailExpected)
            {
                _phase = Phase.Terminal;
                return GlitchSyncObservation.Completed;
            }
            int requestedDelta = TailExpected - TailStart;
            int expectedActual = TailStart
                + (Math.Sign(requestedDelta) * Math.Max(0, ownedFilled));
            if (actual != expectedActual)
            {
                _phase = Phase.Terminal;
                return GlitchSyncObservation.ManualOverride;
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
        private const int MaxNativeProtectionBatchQuantity = 10;
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
        private readonly Dictionary<string, EntryOrderAllocationState> _entryOrderAllocations =
            new Dictionary<string, EntryOrderAllocationState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _allocationRouteSignatures =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _reportedProtectionAmbiguities =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ProtectionRepairAttempt> _protectionRepairAttempts =
            new Dictionary<string, ProtectionRepairAttempt>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PendingMasterClose> _pendingMasterCloses =
            new Dictionary<string, PendingMasterClose>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PendingProtectionMirror> _pendingProtectionMirrors =
            new Dictionary<string, PendingProtectionMirror>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<DeferredFollowerOpen>> _deferredFollowerOpens =
            new Dictionary<string, List<DeferredFollowerOpen>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FollowerProtectionExitBlock> _followerProtectionExitBlocks =
            new Dictionary<string, FollowerProtectionExitBlock>(StringComparer.OrdinalIgnoreCase);
        private long _routeRevision;

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
            bool routeChanged;
            long routeRevision;
            bool configuredEnabled;
            int configuredRouteCount;
            lock (_gate)
            {
                _routesByMaster.Clear();
                var nextRouteSignatures =
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                    {
                        bucket.Add(route);
                        nextRouteSignatures[BuildAllocationRouteKey(route)] =
                            BuildAllocationRouteSignature(route);
                    }
                }

                bool nextEnabled = enabled && _routesByMaster.Values.Any(bucket => bucket.Count > 0);
                routeChanged = ReconcileAllocationEpochs(nextEnabled, nextRouteSignatures);
                _enabled = nextEnabled;
                routeRevision = _routeRevision;
                configuredEnabled = nextEnabled;
                configuredRouteCount = nextRouteSignatures.Count;
            }
            if (routeChanged)
            {
                Journal?.Invoke(
                    "System",
                    "replication_route_revision|revision="
                    + routeRevision.ToString(CultureInfo.InvariantCulture)
                    + "|enabled=" + (configuredEnabled ? "1" : "0")
                    + "|routes=" + configuredRouteCount.ToString(CultureInfo.InvariantCulture));
            }
        }

        // Flatten All is a terminal lifecycle boundary. Do not carry native
        // entry/close/sync ownership into the next flat session.
        public void ResetAfterFlattenAll()
        {
            lock (_gate)
            {
                _entriesBySignal.Clear();
                _closesBySignal.Clear();
                _syncByFollowerInstrument.Clear();
                _entryOrderAllocations.Clear();
                _allocationRouteSignatures.Clear();
                _reportedProtectionAmbiguities.Clear();
                _protectionRepairAttempts.Clear();
                _pendingMasterCloses.Clear();
                _pendingProtectionMirrors.Clear();
                _deferredFollowerOpens.Clear();
                _followerProtectionExitBlocks.Clear();
                _seenExecutionIds.Clear();
                _seenExecutionIdSet.Clear();
            }
        }

        public void ProcessMasterExecution(Account masterAccount, GlitchCopyExecutionContext context)
        {
            if (masterAccount == null || context?.Instrument == null || context.Quantity <= 0)
                return;
            if (context.IsRuntimeEventSnapshot
                && (!string.Equals(context.ExecutionOperation, "Add", StringComparison.OrdinalIgnoreCase)
                    || context.IsSodExecution
                    || !context.PostExecutionNetQuantity.HasValue))
                return;
            if (ParseFollowerSignalKind(context.OrderSignalName) != FollowerSignalKind.None)
                return;
            if (!TryGetRouteSnapshot(masterAccount.Name, true, out List<GlitchCopyFollowerRoute> routes))
                return;
            ClearProtectionExitBlocksAtMasterBoundary(masterAccount, context.Instrument, routes);

            if (!TryResolveExecutionTransition(masterAccount, context, out ExecutionTransition transition))
            {
                string signal = context.OrderSignalName?.Trim() ?? string.Empty;
                bool hasExplicitIntent = context.Action == OrderAction.SellShort
                    || context.Action == OrderAction.BuyToCover
                    || IsEntrySignal(signal)
                    || IsExitSignal(signal);
                if (!hasExplicitIntent)
                {
                    foreach (GlitchCopyFollowerRoute route in routes)
                        JournalCopy(route, context, 0, "copy_skip|master_transition_unavailable");
                    RaiseCritical?.Invoke(
                        masterAccount.Name,
                        "Master execution direction could not be resolved from native position truth; no follower order was submitted.",
                        "MasterExecutionTransitionUnavailable|"
                            + CleanToken(context.Instrument?.FullName));
                    return;
                }
                transition = IsOpeningAction(masterAccount, context)
                    ? ExecutionTransition.OpenOnly(context.Quantity, ResolveEntryAction(masterAccount, context))
                    : ExecutionTransition.CloseOnly(context.Quantity, ResolveCloseAction(masterAccount, context));
            }

            if (transition.CloseQuantity > 0)
            {
                GlitchCopyExecutionContext closeContext = CloneExecutionContext(
                    context,
                    transition.CloseQuantity,
                    transition.CloseAction,
                    "close");
                string closeKey = BuildExecutionDedupKey(masterAccount.Name, closeContext);
                if (TryRememberExecutionId(closeKey))
                    FanOutCompleteClose(masterAccount, closeContext, routes, closeKey);
            }

            if (transition.OpenQuantity <= 0)
                return;

            GlitchCopyExecutionContext openContext = CloneExecutionContext(
                context,
                transition.OpenQuantity,
                transition.OpenAction,
                "open");
            int masterEntryQuantity = ResolveContextMasterQuantity(openContext);
            string currentEntryToken = GlitchReplicationProtection.StableToken(
                BuildExecutionDedupKey(masterAccount.Name, openContext),
                16);
            string currentMasterOrderIdentity = ResolveMasterOrderIdentity(openContext);
            HashSet<string> claimedSources = GetClaimedMasterSourceTokens(
                masterAccount,
                openContext.Instrument,
                openContext.Action == OrderAction.Buy,
                currentMasterOrderIdentity);
            GlitchReplicationProtection.TryResolveMasterPlan(
                masterAccount,
                openContext.Instrument,
                openContext.OrderSignalName,
                masterEntryQuantity,
                openContext.Action == OrderAction.Buy,
                claimedSources,
                openContext.ExecutionTimeUtc,
                out GlitchReplicationProtectionPlan plan);

            FanOutOpening(
                masterAccount,
                openContext,
                routes,
                plan,
                masterEntryQuantity,
                transition.CloseQuantity > 0);
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
            FollowerSignalKind signalKind = ParseFollowerSignalKind(signal);
            ProcessPendingMasterClose(followerAccount, order.Instrument, false);
            ProcessDeferredFollowerOpen(followerAccount, order.Instrument);
            ProcessSyncFollowerOrderUpdate(followerAccount, order, signal);
            if (signalKind == FollowerSignalKind.Close)
            {
                TrackCloseOrder(followerAccount, order, signal);
                return;
            }
            if (signalKind == FollowerSignalKind.Protection)
            {
                ProcessFollowerProtectionOrderUpdate(followerAccount, order, signal);
                TryApplyPendingProtectionMirrorForOrder(followerAccount, order, signal);
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

            if (!lifecycle.ProtectionAvailable
                && lifecycle.MasterAccountInstance != null
                && lifecycle.MasterEntryOrder != null)
                TryAttachLateFollowerProtection(lifecycle.MasterAccountInstance, lifecycle.MasterEntryOrder);

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
            }

            if (TryHandleRepairProtectionOrderUpdate(followerAccount, order, signal))
                return;

            if (order.OrderState == OrderState.Filled && lifecycle?.MasterAccountInstance != null)
            {
                int masterNet = 0;
                GlitchReplicationEngine.TryGetNetQuantityForInstrument(
                    lifecycle.MasterAccountInstance,
                    order.Instrument,
                    out masterNet);
                string blockKey = BuildFollowerInstrumentKey(followerAccount, order.Instrument);
                lock (_gate)
                {
                    _followerProtectionExitBlocks[blockKey] = new FollowerProtectionExitBlock
                    {
                        Key = blockKey,
                        FollowerAccount = followerAccount,
                        MasterAccount = lifecycle.MasterAccountInstance,
                        Instrument = order.Instrument,
                        MasterDirection = Math.Sign(masterNet),
                        RecordedUtc = DateTime.UtcNow
                    };
                }
                Journal?.Invoke(
                    followerAccount.Name,
                    "follower_protection_exit|instrument=" + CleanToken(root)
                    + "|master_net=" + masterNet.ToString(CultureInfo.InvariantCulture)
                    + "|sync_reentry=blocked_until_master_flat_or_reverses");
            }

            if (order.OrderState != OrderState.Rejected)
                return;
            lock (_gate)
            {
                if (lifecycle != null && lifecycle.ProtectionFailed)
                    return;
            }

            if (lifecycle != null)
            {
                lock (_gate)
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

        private bool TryHandleRepairProtectionOrderUpdate(
            Account followerAccount,
            Order order,
            string signal)
        {
            string entryToken = ExtractFollowerProtectionEntryToken(signal);
            ProtectionRepairAttempt attempt;
            lock (_gate)
            {
                attempt = _protectionRepairAttempts.Values.FirstOrDefault(item =>
                    item != null
                    && string.Equals(item.EntryToken, entryToken, StringComparison.OrdinalIgnoreCase));
            }
            if (attempt == null)
                return false;
            if (order.OrderState != OrderState.Rejected)
                return true;

            bool requestSiblingCancel;
            lock (_gate)
            {
                attempt.InFlight = false;
                attempt.NextAttemptUtc = DateTime.UtcNow.AddSeconds(
                    Math.Min(4, Math.Max(1, attempt.AttemptCount)));
                requestSiblingCancel = !attempt.SiblingCancelRequested;
                attempt.SiblingCancelRequested = true;
            }
            if (requestSiblingCancel
                && !string.IsNullOrWhiteSpace(order.Oco)
                && TrySnapshotOrders(followerAccount, out Order[] orders))
            {
                Order sibling = orders.FirstOrDefault(item =>
                    item != null
                    && !ReferenceEquals(item, order)
                    && string.Equals(item.Oco, order.Oco, StringComparison.OrdinalIgnoreCase)
                    && GlitchReplicationEngine.CanCancelOrder(item));
                if (sibling != null)
                {
                    try
                    {
                        followerAccount.Cancel(new[] { sibling });
                    }
                    catch (Exception ex)
                    {
                        RaiseCritical?.Invoke(
                            followerAccount.Name,
                            "Rejected repair OCO sibling could not be cancelled: " + ex.GetType().Name,
                            "FollowerProtectionRepairSiblingCancelFailed|"
                                + CleanToken(order.Instrument?.FullName));
                    }
                }
            }
            Journal?.Invoke(
                followerAccount.Name,
                "follower_protection_repair|instrument="
                    + CleanToken(order.Instrument?.FullName)
                    + "|result=rejected_backoff|attempt="
                    + attempt.AttemptCount.ToString(CultureInfo.InvariantCulture));
            RaiseCritical?.Invoke(
                followerAccount.Name,
                "A bounded follower-protection repair was rejected; its sibling was cancelled and retry is delayed.",
                "FollowerProtectionRepairRejected|" + CleanToken(order.Instrument?.FullName));
            return true;
        }

        public void ProcessAccountStateUpdate(Account account)
        {
            if (account == null)
                return;
            List<Instrument> pendingCloseInstruments;
            lock (_gate)
            {
                pendingCloseInstruments = _pendingMasterCloses.Values
                    .Where(item => item?.Account != null
                        && item.Instrument != null
                        && string.Equals(item.Account.Name, account.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Instrument)
                    .ToList();
            }
            foreach (Instrument pendingInstrument in pendingCloseInstruments)
                ProcessPendingMasterClose(account, pendingInstrument, true);
            List<Instrument> deferredOpenInstruments;
            lock (_gate)
            {
                deferredOpenInstruments = _deferredFollowerOpens.Values
                    .SelectMany(items => items ?? new List<DeferredFollowerOpen>())
                    .Where(item => item?.Route?.FollowerAccount != null
                        && item.Instrument != null
                        && string.Equals(item.Route.FollowerAccount.Name, account.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Instrument)
                    .GroupBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
            }
            foreach (Instrument deferredInstrument in deferredOpenInstruments)
                ProcessDeferredFollowerOpen(account, deferredInstrument);
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
                if (IsSyncReentryBlocked(
                        followerAccount,
                        instrument,
                        masterAccount,
                        masterNet,
                        actual,
                        expected))
                {
                    JournalSync(
                        followerAccount,
                        root,
                        "validation",
                        "blocked_recent_follower_protection_exit",
                        actual,
                        expected,
                        null);
                    continue;
                }

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
                        root
                            + "|" + followerAccount.Name
                            + "|" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)
                            + "|" + Interlocked.Increment(ref _syncNonce).ToString(CultureInfo.InvariantCulture)
                            + "|" + Guid.NewGuid().ToString("N"),
                        16)
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
            string queueResult = QueueFollowerCloseAfterProtectionCancel(
                sync.FollowerAccount,
                sync.Instrument,
                actual > 0 ? OrderAction.Sell : OrderAction.BuyToCover,
                Math.Abs(actual),
                sync.IdentitySource + "|flatten",
                0,
                CatchUpSignalName,
                sync,
                "flatten");
            bool accepted = IsPendingCloseAccepted(queueResult);
            lock (_gate)
            {
                if (!IsCurrentSyncLifecycle(sync))
                    return;
                sync.State.MarkFlattenSubmitted(accepted);
                if (!accepted)
                    _syncByFollowerInstrument.Remove(sync.Key);
            }

            JournalSync(
                sync.FollowerAccount,
                sync.Root,
                "flatten_submission",
                accepted ? CleanToken(queueResult) : "failed_" + CleanToken(queueResult),
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
            lock (_gate)
            {
                if (!IsCurrentSyncLifecycle(sync))
                    return;
                sync.ReduceTargetExpected = expected;
            }
            string queueResult = QueueFollowerCloseAfterProtectionCancel(
                sync.FollowerAccount,
                sync.Instrument,
                action,
                quantity,
                sync.IdentitySource + "|reduce",
                expected,
                CatchUpSignalName,
                sync,
                "reduce");
            bool accepted = IsPendingCloseAccepted(queueResult);
            lock (_gate)
            {
                if (!IsCurrentSyncLifecycle(sync))
                    return;
                if (!accepted)
                    _syncByFollowerInstrument.Remove(sync.Key);
            }

            JournalSync(
                sync.FollowerAccount,
                sync.Root,
                "reduce_submission",
                accepted ? CleanToken(queueResult) : "failed_" + CleanToken(queueResult),
                actual,
                expected,
                "qty=" + quantity.ToString(CultureInfo.InvariantCulture));
            if (accepted)
                ProcessSyncLifecycle(sync);
        }

        private static bool IsPendingCloseAccepted(string result)
        {
            return string.Equals(result, "submitted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result, "already_converged", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(result)
                    && result.StartsWith("awaiting_", StringComparison.OrdinalIgnoreCase));
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
                Math.Abs(expected),
                plan,
                CatchUpSignalName,
                sync.IdentitySource,
                route.MasterAccountInstance,
                null,
                Math.Abs(masterNet),
                sync.IdentitySource,
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
            lock (_gate)
            {
                if (isFlattenOrder)
                    sync.FlattenOrder = order;
                else if (isReduceOrder)
                    sync.ReduceOrder = order;
                else
                    sync.TailOrder = order;
            }
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
                if (_pendingMasterCloses.TryGetValue(sync.Key, out PendingMasterClose pending)
                    && ReferenceEquals(pending?.SyncOwner, sync))
                    _pendingMasterCloses.Remove(sync.Key);
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

        private bool ReconcileAllocationEpochs(
            bool nextEnabled,
            IReadOnlyDictionary<string, string> nextRouteSignatures)
        {
            bool routeChanged = nextEnabled != _enabled
                || _allocationRouteSignatures.Count != (nextRouteSignatures?.Count ?? 0);
            if (!routeChanged)
            {
                foreach (KeyValuePair<string, string> next in nextRouteSignatures
                    ?? new Dictionary<string, string>())
                {
                    if (!_allocationRouteSignatures.TryGetValue(next.Key, out string existing)
                        || !string.Equals(existing, next.Value, StringComparison.Ordinal))
                    {
                        routeChanged = true;
                        break;
                    }
                }
            }

            if (!nextEnabled)
            {
                // Disabling replication ends the allocation epoch. Do not carry
                // cumulative quantities into a later enable and manufacture a
                // stale ratio delta or duplicate follower entry.
                _entryOrderAllocations.Clear();
                _protectionRepairAttempts.Clear();
            }
            if (routeChanged)
            {
                _pendingProtectionMirrors.Clear();
                _deferredFollowerOpens.Clear();
                _routeRevision++;
            }

            _allocationRouteSignatures.Clear();
            foreach (KeyValuePair<string, string> signature in nextRouteSignatures
                ?? new Dictionary<string, string>())
                _allocationRouteSignatures[signature.Key] = signature.Value;
            return routeChanged;
        }

        private ExecutionAllocation AllocateExecutionDelta(
            GlitchCopyFollowerRoute route,
            GlitchCopyExecutionContext context,
            bool includeEntryOrderPlan)
        {
            var result = new ExecutionAllocation();
            if (route == null
                || context?.Instrument == null
                || context.Quantity <= 0
                || route.Ratio <= 0
                || double.IsNaN(route.Ratio)
                || double.IsInfinity(route.Ratio))
                return result;

            string routeKey = BuildAllocationRouteKey(route);
            string orderKey = routeKey
                + "|"
                + (context.Instrument.FullName?.Trim() ?? string.Empty)
                + "|"
                + context.Action
                + "|"
                + ResolveMasterOrderIdentity(context);
            lock (_gate)
            {
                if (!_entryOrderAllocations.TryGetValue(
                        orderKey,
                        out EntryOrderAllocationState state))
                {
                    state = new EntryOrderAllocationState
                    {
                        RouteKey = routeKey,
                        Ratio = route.Ratio,
                        PlannedMasterQuantity = includeEntryOrderPlan
                            ? ResolveEntryOrderQuantity(context)
                            : 0
                    };
                    _entryOrderAllocations[orderKey] = state;
                }
                if (includeEntryOrderPlan)
                    state.PlannedMasterQuantity = Math.Max(
                        state.PlannedMasterQuantity,
                        ResolveEntryOrderQuantity(context));

                result.FollowerOrderOffset = state.FollowerQuantity;
                state.MasterQuantity += context.Quantity;
                int targetFollowerQuantity =
                    GlitchReplicationProtection.ScaleFollowerQuantity(state.MasterQuantity, state.Ratio);
                result.Quantity = Math.Max(0, targetFollowerQuantity - state.FollowerQuantity);
                state.FollowerQuantity = targetFollowerQuantity;
                result.MasterCumulative = state.MasterQuantity;
                result.FollowerCumulative = state.FollowerQuantity;
                result.Ratio = state.Ratio;
                result.FollowerOrderPlanQuantity = includeEntryOrderPlan
                    ? Math.Max(
                        state.FollowerQuantity,
                        GlitchReplicationProtection.ScaleFollowerQuantity(
                            state.PlannedMasterQuantity,
                            state.Ratio))
                    : state.FollowerQuantity;

                // The native Order instance is mutable and several execution
                // callbacks can be queued before any of them are processed.
                // Keep the per-order accumulator for the session instead of
                // observing a later OrderState and resetting cumulative ratio
                // allocation between partial fills. It is cleared only at a
                // real lifecycle boundary (disable or Flatten All).
            }
            return result;
        }

        private static string BuildAllocationRouteKey(GlitchCopyFollowerRoute route)
        {
            return (route?.MasterAccount?.Trim() ?? string.Empty)
                + "|"
                + (route?.FollowerAccount?.Name?.Trim() ?? string.Empty);
        }

        private static string BuildAllocationRouteSignature(GlitchCopyFollowerRoute route)
        {
            return BuildAllocationRouteKey(route)
                + "|R"
                + BitConverter.DoubleToInt64Bits(route?.Ratio ?? 0)
                    .ToString("x16", CultureInfo.InvariantCulture)
                + "|M"
                + (route?.MasterAccount?.Trim() ?? string.Empty)
                + "|F"
                + (route?.FollowerAccount?.Name?.Trim() ?? string.Empty);
        }

        private bool IsSyncReentryBlocked(
            Account followerAccount,
            Instrument instrument,
            Account masterAccount,
            int masterNet,
            int followerNet,
            int expectedFollowerNet)
        {
            if (followerAccount == null
                || instrument == null
                || masterAccount == null
                || masterNet == 0)
                return false;
            bool wouldReenterStoppedExposure = followerNet == 0
                ? expectedFollowerNet != 0
                : Math.Sign(followerNet) == Math.Sign(expectedFollowerNet)
                    && Math.Abs(followerNet) < Math.Abs(expectedFollowerNet);
            if (!wouldReenterStoppedExposure)
                return false;
            string key = BuildFollowerInstrumentKey(followerAccount, instrument);
            lock (_gate)
            {
                if (!_followerProtectionExitBlocks.TryGetValue(key, out FollowerProtectionExitBlock block)
                    || block == null)
                    return false;
                if (!string.Equals(block.MasterAccount?.Name, masterAccount.Name, StringComparison.OrdinalIgnoreCase)
                    || block.MasterDirection == 0
                    || block.MasterDirection != Math.Sign(masterNet))
                {
                    _followerProtectionExitBlocks.Remove(key);
                    return false;
                }
                return true;
            }
        }

        private void ClearProtectionExitBlocksAtMasterBoundary(
            Account masterAccount,
            Instrument instrument,
            IReadOnlyList<GlitchCopyFollowerRoute> routes)
        {
            if (masterAccount == null
                || instrument == null
                || !GlitchReplicationEngine.TryGetNetQuantityForInstrument(masterAccount, instrument, out int masterNet))
                return;
            lock (_gate)
            {
                foreach (GlitchCopyFollowerRoute route in routes ?? Array.Empty<GlitchCopyFollowerRoute>())
                {
                    string key = BuildFollowerInstrumentKey(route?.FollowerAccount, instrument);
                    if (!_followerProtectionExitBlocks.TryGetValue(key, out FollowerProtectionExitBlock block)
                        || block == null)
                        continue;
                    if (masterNet == 0 || block.MasterDirection != Math.Sign(masterNet))
                        _followerProtectionExitBlocks.Remove(key);
                }
            }
        }

        private static string ResolveMasterOrderIdentity(GlitchCopyExecutionContext context)
        {
            if (!string.IsNullOrWhiteSpace(context?.OrderIdentity))
                return context.OrderIdentity.Trim();
            Order order = context?.EntryOrder;
            return (context?.OrderSignalName?.Trim() ?? string.Empty)
                + "|"
                + (order?.Time ?? DateTime.MinValue).Ticks.ToString(CultureInfo.InvariantCulture)
                + "|"
                + ResolveEntryOrderQuantity(context).ToString(CultureInfo.InvariantCulture)
                + "|"
                + (order?.GetHashCode() ?? 0).ToString(CultureInfo.InvariantCulture);
        }

        private static int ResolveEntryOrderQuantity(GlitchCopyExecutionContext context)
        {
            if (context == null)
                return 0;
            return Math.Max(
                Math.Max(0, context.Quantity),
                Math.Max(
                    Math.Max(0, context.EntryOrderQuantity),
                    Math.Max(0, context.EntryOrderFilledQuantity)));
        }

        private static string AllocationJournalSuffix(ExecutionAllocation allocation)
        {
            return "|allocation_basis=native_master_order"
                + "|allocation_master=" + (allocation?.MasterCumulative ?? 0)
                    .ToString(CultureInfo.InvariantCulture)
                + "|allocation_follower=" + (allocation?.FollowerCumulative ?? 0)
                    .ToString(CultureInfo.InvariantCulture)
                + "|allocation_ratio=" + (allocation?.Ratio ?? 0)
                    .ToString("0.####", CultureInfo.InvariantCulture);
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
            int masterEntryQuantity,
            bool deferUntilFollowerFlat = false)
        {
            string dedupKey = BuildExecutionDedupKey(masterAccount.Name, context);
            if (!TryRememberExecutionId(dedupKey))
                return;

            foreach (GlitchCopyFollowerRoute route in routes)
            {
                ExecutionAllocation allocation = AllocateExecutionDelta(route, context, true);
                if (allocation.Quantity <= 0)
                {
                    JournalCopy(route, context, 0, "copy_skip|ratio_rounds_to_zero"
                        + AllocationJournalSuffix(allocation));
                    continue;
                }

                var effectiveRoute = new GlitchCopyFollowerRoute
                {
                    MasterAccount = route.MasterAccount,
                    MasterAccountInstance = route.MasterAccountInstance,
                    FollowerAccount = route.FollowerAccount,
                    Ratio = allocation.Ratio
                };
                if (deferUntilFollowerFlat || plan == null)
                {
                    QueueDeferredFollowerOpen(new DeferredFollowerOpen
                    {
                        Route = effectiveRoute,
                        Instrument = context.Instrument,
                        Action = ResolveEntryAction(masterAccount, context),
                        Quantity = allocation.Quantity,
                        FollowerAllocationOffset = allocation.FollowerOrderOffset,
                        FollowerPlanQuantity = allocation.FollowerOrderPlanQuantity,
                        Plan = plan,
                        SignalPrefix = CopySignalName,
                        IdentitySource = dedupKey,
                        MasterAccount = masterAccount,
                        MasterEntrySignal = context.OrderSignalName,
                        MasterEntryQuantity = masterEntryQuantity,
                        MasterOrderIdentity = ResolveMasterOrderIdentity(context),
                        MasterEntryOrder = context.EntryOrder,
                        RequiresFollowerFlat = deferUntilFollowerFlat
                    });
                }
                else
                {
                    SubmitFollowerEntry(
                        effectiveRoute,
                        context.Instrument,
                        ResolveEntryAction(masterAccount, context),
                        allocation.Quantity,
                        allocation.FollowerOrderOffset,
                        allocation.FollowerOrderPlanQuantity,
                        plan,
                        CopySignalName,
                        dedupKey,
                        masterAccount,
                        context.OrderSignalName,
                        masterEntryQuantity,
                        ResolveMasterOrderIdentity(context),
                        context.EntryOrder);
                }
            }
        }

        private void QueueDeferredFollowerOpen(DeferredFollowerOpen deferred)
        {
            if (deferred?.Route?.FollowerAccount == null || deferred.Instrument == null)
                return;
            string key = BuildFollowerInstrumentKey(deferred.Route.FollowerAccount, deferred.Instrument);
            lock (_gate)
            {
                deferred.RouteRevision = _routeRevision;
                deferred.RouteSignature = BuildAllocationRouteSignature(deferred.Route);
                if (!_deferredFollowerOpens.TryGetValue(key, out List<DeferredFollowerOpen> queue))
                {
                    queue = new List<DeferredFollowerOpen>();
                    _deferredFollowerOpens[key] = queue;
                }
                DeferredFollowerOpen existing = queue.FirstOrDefault(item =>
                    item != null
                    && item.RequiresFollowerFlat == deferred.RequiresFollowerFlat
                    && item.Action == deferred.Action
                    && string.Equals(
                        item.MasterOrderIdentity,
                        deferred.MasterOrderIdentity,
                        StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    queue.Add(deferred);
                }
                else
                {
                    existing.Quantity += deferred.Quantity;
                    existing.FollowerAllocationOffset = Math.Min(
                        existing.FollowerAllocationOffset,
                        deferred.FollowerAllocationOffset);
                    existing.FollowerPlanQuantity = Math.Max(
                        existing.FollowerPlanQuantity,
                        deferred.FollowerPlanQuantity);
                    existing.MasterEntryQuantity = Math.Max(
                        existing.MasterEntryQuantity,
                        deferred.MasterEntryQuantity);
                    existing.MasterEntryOrder = deferred.MasterEntryOrder ?? existing.MasterEntryOrder;
                }
            }
            Journal?.Invoke(
                deferred.Route.FollowerAccount.Name,
                "copy_reversal|instrument=" + CleanToken(deferred.Instrument.FullName)
                + "|phase=deferred_until_flat|qty="
                + deferred.Quantity.ToString(CultureInfo.InvariantCulture));
            // If an earlier follower OCO already flattened this account there
            // may be no later follower callback to drain the queue.
            ProcessDeferredFollowerOpen(
                deferred.Route.FollowerAccount,
                deferred.Instrument);
        }

        private void ProcessDeferredFollowerOpen(Account account, Instrument instrument)
        {
            if (account == null || instrument == null)
                return;
            string key = BuildFollowerInstrumentKey(account, instrument);
            List<DeferredFollowerOpen> queue;
            lock (_gate)
            {
                if (!_enabled
                    || _pendingMasterCloses.ContainsKey(key)
                    || !_deferredFollowerOpens.TryGetValue(key, out queue)
                    || queue == null
                    || queue.Count == 0)
                    return;
            }
            if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(account, instrument, out int followerNet)
                || followerNet != 0
                || !TrySnapshotOrders(account, out Order[] orders)
                || orders.Any(order => order?.Instrument != null
                    && string.Equals(order.Instrument.FullName, instrument.FullName, StringComparison.OrdinalIgnoreCase)
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                    && (ParseFollowerSignalKind(order.Name) == FollowerSignalKind.Close
                        || ParseFollowerSignalKind(order.Name) == FollowerSignalKind.Protection)))
                return;

            DeferredFollowerOpen deferred = queue.LastOrDefault();
            if (deferred == null)
                return;
            GlitchCopyFollowerRoute currentRoute = FindConfiguredRoute(
                deferred.MasterAccount,
                account);
            bool routeCurrent;
            lock (_gate)
            {
                routeCurrent = _enabled
                    && deferred.RouteRevision == _routeRevision
                    && currentRoute != null
                    && string.Equals(
                        deferred.RouteSignature,
                        BuildAllocationRouteSignature(currentRoute),
                        StringComparison.Ordinal);
                _deferredFollowerOpens.Remove(key);
            }
            if (!routeCurrent
                || !GlitchReplicationEngine.TryGetNetQuantityForInstrument(
                    deferred.MasterAccount,
                    deferred.Instrument,
                    out int masterNet)
                || masterNet == 0
                || (masterNet > 0) != (deferred.Action == OrderAction.Buy))
            {
                Journal?.Invoke(
                    account.Name,
                    "copy_reversal|instrument=" + CleanToken(instrument.FullName)
                    + "|phase=open|result=superseded_route_or_master_truth_changed");
                return;
            }

            int authoritativeQuantity = Math.Abs(ScaleSignedQuantity(masterNet, currentRoute.Ratio));
            if (authoritativeQuantity <= 0)
                return;
            GlitchReplicationProtection.TryResolveMasterPlan(
                deferred.MasterAccount,
                deferred.Instrument,
                deferred.MasterEntrySignal,
                Math.Abs(masterNet),
                masterNet > 0,
                out GlitchReplicationProtectionPlan currentPlan);
            SubmitFollowerEntry(
                currentRoute,
                deferred.Instrument,
                deferred.Action,
                authoritativeQuantity,
                0,
                authoritativeQuantity,
                currentPlan,
                deferred.SignalPrefix,
                deferred.IdentitySource,
                deferred.MasterAccount,
                deferred.MasterEntrySignal,
                Math.Abs(masterNet),
                deferred.MasterOrderIdentity,
                deferred.MasterEntryOrder);
        }

        private void FanOutCompleteClose(
            Account masterAccount,
            GlitchCopyExecutionContext context,
            IReadOnlyList<GlitchCopyFollowerRoute> routes,
            string executionKey)
        {
            string root = GlitchReplicationEngine.GetInstrumentRoot(context.Instrument);
            OrderAction closeAction = ResolveCloseAction(masterAccount, context);
            int authoritativeMasterNet;
            if (context.PostExecutionNetQuantity.HasValue)
                authoritativeMasterNet = context.PostExecutionNetQuantity.Value;
            else if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(
                         masterAccount,
                         context.Instrument,
                         out authoritativeMasterNet))
            {
                foreach (GlitchCopyFollowerRoute route in routes)
                {
                    JournalCopy(route, context, 0, "copy_close_skip|master_native_state_unavailable");
                    RaiseCritical?.Invoke(
                        route.FollowerAccount.Name,
                        "Master position state is unavailable; no follower close order was submitted.",
                        "MasterCloseStateUnavailable|" + CleanToken(context.Instrument?.FullName ?? root));
                }
                return;
            }
            foreach (GlitchCopyFollowerRoute route in routes)
            {
                ExecutionAllocation allocation = AllocateExecutionDelta(route, context, false);
                if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(route.FollowerAccount, context.Instrument, out int followerNet))
                {
                    JournalCopy(route, context, 0, "copy_close_skip|native_state_unavailable"
                        + AllocationJournalSuffix(allocation));
                    RaiseCritical?.Invoke(route.FollowerAccount.Name,
                        "Follower position state is unavailable; no close order was submitted.",
                        "FollowerCloseStateUnavailable|" + CleanToken(context.Instrument?.FullName ?? root));
                    continue;
                }
                int closable = closeAction == OrderAction.Sell
                    ? Math.Max(0, followerNet)
                    : closeAction == OrderAction.BuyToCover
                        ? Math.Max(0, -followerNet)
                        : 0;
                int requested = allocation.Quantity;
                if (closable <= 0)
                {
                    JournalCopy(route, context, 0, "copy_skip|follower_has_no_closable_exposure"
                        + AllocationJournalSuffix(allocation));
                    continue;
                }
                int quantity = Math.Min(requested, closable);
                if (quantity <= 0)
                {
                    JournalCopy(route, context, 0, "copy_skip|ratio_rounds_to_zero"
                        + AllocationJournalSuffix(allocation));
                    continue;
                }

                int authoritativeFollowerTarget = 0;
                if (authoritativeMasterNet != 0
                    && Math.Sign(authoritativeMasterNet) == Math.Sign(followerNet))
                {
                    authoritativeFollowerTarget = Math.Sign(authoritativeMasterNet)
                        * GlitchReplicationProtection.ScaleFollowerQuantity(
                            Math.Abs(authoritativeMasterNet),
                            route.Ratio);
                }
                string result = QueueFollowerCloseAfterProtectionCancel(
                    route.FollowerAccount,
                    context.Instrument,
                    closeAction,
                    quantity,
                    executionKey,
                    authoritativeFollowerTarget);
                JournalCopy(route, context, quantity, "copy_close|result=" + CleanToken(result)
                    + "|exec=" + CleanToken(executionKey)
                    + "|master_post_net=" + authoritativeMasterNet.ToString(CultureInfo.InvariantCulture)
                    + "|follower_target=" + authoritativeFollowerTarget.ToString(CultureInfo.InvariantCulture)
                    + AllocationJournalSuffix(allocation));
            }
        }

        private string QueueFollowerCloseAfterProtectionCancel(
            Account account,
            Instrument instrument,
            OrderAction action,
            int quantity,
            string identity,
            int authoritativeTargetNet,
            string signalPrefix = CopySignalName,
            FollowerSyncLifecycle syncOwner = null,
            string syncPhase = null,
            FollowerEntryLifecycle recoveryOwner = null)
        {
            if (account == null || instrument == null || quantity <= 0)
                return "invalid_request";
            if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(account, instrument, out int actual))
                return "native_state_unavailable";

            bool isLongExposure = action == OrderAction.Sell;
            int closable = isLongExposure ? Math.Max(0, actual) : Math.Max(0, -actual);
            if (closable <= 0)
                return "already_converged";

            string key = BuildFollowerInstrumentKey(account, instrument);
            PendingMasterClose pending;
            lock (_gate)
            {
                if (!_pendingMasterCloses.TryGetValue(key, out pending))
                {
                    pending = new PendingMasterClose
                    {
                        Key = key,
                        Account = account,
                        Instrument = instrument,
                        IsLongExposure = isLongExposure,
                        InitialFollowerNet = actual,
                        AuthoritativeTargetNet = authoritativeTargetNet,
                        TargetInitialized = true,
                        Identity = identity,
                        SignalPrefix = signalPrefix,
                        SyncOwner = syncOwner,
                        SyncPhase = syncPhase,
                        RecoveryOwner = recoveryOwner
                    };
                    _pendingMasterCloses[key] = pending;
                }
                if (pending.IsLongExposure != isLongExposure)
                    return "conflicting_pending_direction";
                if (!ReferenceEquals(pending.SyncOwner, syncOwner)
                    && (pending.SyncOwner != null || syncOwner != null))
                    return "conflicting_pending_owner";
                pending.RequestedQuantity += Math.Min(quantity, closable);
                if (!pending.TargetInitialized)
                {
                    pending.AuthoritativeTargetNet = authoritativeTargetNet;
                    pending.TargetInitialized = true;
                }
                else
                {
                    // Close targets only move toward flat. This also makes an
                    // out-of-order native callback unable to grow follower risk.
                    pending.AuthoritativeTargetNet = isLongExposure
                        ? Math.Min(pending.AuthoritativeTargetNet, Math.Max(0, authoritativeTargetNet))
                        : Math.Max(pending.AuthoritativeTargetNet, Math.Min(0, authoritativeTargetNet));
                }
                pending.Identity = identity;
            }

            return ProcessPendingMasterClose(account, instrument, false);
        }

        private string ProcessPendingMasterClose(
            Account account,
            Instrument instrument,
            bool positionUpdateObserved)
        {
            if (account == null || instrument == null)
                return "invalid_request";
            string key = BuildFollowerInstrumentKey(account, instrument);
            PendingMasterClose pending;
            lock (_gate)
            {
                if (!_pendingMasterCloses.TryGetValue(key, out pending) || pending == null)
                    return "none";
            }

            if (!TrySnapshotOrders(account, out Order[] orders))
                return "native_order_state_unavailable";
            if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(account, instrument, out int actual))
                return "native_state_unavailable";
            int blindTarget = pending.IsLongExposure
                ? Math.Max(0, pending.InitialFollowerNet - pending.RequestedQuantity)
                : Math.Min(0, pending.InitialFollowerNet + pending.RequestedQuantity);
            int desiredTarget = pending.IsLongExposure
                ? Math.Max(blindTarget, Math.Max(0, pending.AuthoritativeTargetNet))
                : Math.Min(blindTarget, Math.Min(0, pending.AuthoritativeTargetNet));
            int closableToTarget = pending.IsLongExposure
                ? Math.Max(0, actual - desiredTarget)
                : Math.Max(0, desiredTarget - actual);
            if (closableToTarget <= 0)
            {
                lock (_gate)
                    _pendingMasterCloses.Remove(key);
                Journal?.Invoke(
                    account.Name,
                    "master_exit_convergence|instrument=" + CleanToken(instrument.FullName)
                    + "|native=" + actual.ToString(CultureInfo.InvariantCulture)
                    + "|target=" + desiredTarget.ToString(CultureInfo.InvariantCulture)
                    + "|result=already_converged");
                return "already_converged";
            }

            if (pending.CloseSubmitted)
                return "awaiting_close_position_confirmation";

            OrderAction exitAction = pending.IsLongExposure ? OrderAction.Sell : OrderAction.BuyToCover;
            List<Order> activeProtection = orders
                .Where(order => order?.Instrument != null
                    && string.Equals(order.Instrument.FullName, instrument.FullName, StringComparison.OrdinalIgnoreCase)
                    && order.OrderAction == exitAction
                    && ParseFollowerSignalKind(order.Name) == FollowerSignalKind.Protection
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                    && !string.IsNullOrWhiteSpace(order.Oco))
                .ToList();
            var protectionUnits = new List<FollowerProtectionUnit>();
            foreach (IGrouping<string, Order> group in activeProtection.GroupBy(
                order => order.Oco.Trim(),
                StringComparer.OrdinalIgnoreCase))
            {
                if (!TryBuildFollowerProtectionUnit(
                        group.Key,
                        group.ToList(),
                        exitAction,
                        pending.IsLongExposure,
                        out FollowerProtectionUnit unit))
                {
                    if (pending.RecoveryOwner != null)
                    {
                        bool requestCancellation;
                        lock (_gate)
                        {
                            requestCancellation = pending.ProtectionMutationRequestedOcos.Add(group.Key);
                            if (requestCancellation)
                            {
                                pending.RequiresPositionBarrier = true;
                                pending.ProtectionMutationAcknowledged = false;
                            }
                        }
                        if (!requestCancellation)
                            return "awaiting_protection_mutation";
                        Order survivingSibling = group
                            .Where(GlitchReplicationEngine.CanCancelOrder)
                            .OrderBy(order => GlitchReplicationEngine.IsStopLikeOrder(order) ? 0 : 1)
                            .FirstOrDefault();
                        if (survivingSibling == null)
                            return "awaiting_protection_terminal";
                        try
                        {
                            account.Cancel(new[] { survivingSibling });
                            return "awaiting_protection_mutation";
                        }
                        catch (Exception ex)
                        {
                            lock (_gate)
                                pending.ProtectionMutationRequestedOcos.Remove(group.Key);
                            RaiseCritical?.Invoke(
                                account.Name,
                                "The surviving OCO sibling could not be cancelled before attributed recovery: " + ex.GetType().Name,
                                "FollowerRecoverySiblingCancelFailed|" + CleanToken(instrument.FullName));
                            return "protection_cancel_failed_" + ex.GetType().Name;
                        }
                    }
                    bool mutationInFlight;
                    lock (_gate)
                        mutationInFlight = pending.ProtectionMutationRequestedOcos.Contains(group.Key);
                    if (mutationInFlight)
                        return "awaiting_protection_mutation";
                    RaiseCritical?.Invoke(
                        account.Name,
                        "Follower protection is incomplete or malformed; close convergence was stopped before another exit order could be submitted.",
                        "FollowerMasterExitProtectionAmbiguous|" + CleanToken(instrument.FullName));
                    return "protection_ambiguous";
                }
                protectionUnits.Add(unit);
            }

            protectionUnits = protectionUnits
                .OrderBy(unit => unit.Oco, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int desiredProtectedQuantity = Math.Abs(desiredTarget);
            int protectedQuantity = protectionUnits.Sum(unit => unit.Quantity);
            if (protectedQuantity > desiredProtectedQuantity)
            {
                int keepRemaining = desiredProtectedQuantity;
                var changes = new List<Order>();
                var cancellations = new List<Order>();
                var originalQuantities = new Dictionary<Order, int>();
                var changedOcos = new List<string>();
                var cancelledOcos = new List<string>();
                foreach (FollowerProtectionUnit unit in protectionUnits)
                {
                    int keep = Math.Min(unit.Quantity, Math.Max(0, keepRemaining));
                    keepRemaining -= keep;
                    if (keep == unit.Quantity)
                        continue;
                    bool alreadyRequested;
                    lock (_gate)
                        alreadyRequested = pending.ProtectionMutationRequestedOcos.Contains(unit.Oco);
                    if (alreadyRequested)
                        continue;
                    if (keep == 0)
                    {
                        Order cancellation = unit.Orders
                            .Where(GlitchReplicationEngine.CanCancelOrder)
                            .OrderBy(order => GlitchReplicationEngine.IsStopLikeOrder(order) ? 0 : 1)
                            .FirstOrDefault();
                        if (cancellation == null)
                            return "awaiting_protection_cancellable";
                        cancellations.Add(cancellation);
                        cancelledOcos.Add(unit.Oco);
                    }
                    else
                    {
                        foreach (Order order in unit.Orders)
                        {
                            int desiredTotal = order.Filled + keep;
                            if (desiredTotal == order.Quantity || desiredTotal == order.QuantityChanged)
                                continue;
                            originalQuantities[order] = order.QuantityChanged;
                            order.QuantityChanged = desiredTotal;
                            changes.Add(order);
                        }
                        changedOcos.Add(unit.Oco);
                    }
                }

                if (changes.Count == 0 && cancellations.Count == 0)
                    return "awaiting_protection_mutation";
                lock (_gate)
                {
                    foreach (string oco in changedOcos.Concat(cancelledOcos))
                        pending.ProtectionMutationRequestedOcos.Add(oco);
                    pending.RequiresPositionBarrier = true;
                    pending.ProtectionMutationAcknowledged = false;
                }
                if (changes.Count > 0)
                {
                    try
                    {
                        account.Change(changes.ToArray());
                    }
                    catch (Exception ex)
                    {
                        foreach (KeyValuePair<Order, int> original in originalQuantities)
                            original.Key.QuantityChanged = original.Value;
                        lock (_gate)
                        {
                            foreach (string oco in changedOcos)
                                pending.ProtectionMutationRequestedOcos.Remove(oco);
                        }
                        RaiseCritical?.Invoke(
                            account.Name,
                            "Follower protection could not be resized before close convergence: " + ex.GetType().Name,
                            "FollowerMasterExitProtectionResizeFailed|" + CleanToken(instrument.FullName));
                        return "protection_resize_failed_" + ex.GetType().Name;
                    }
                }
                if (cancellations.Count > 0)
                {
                    try
                    {
                        account.Cancel(cancellations.ToArray());
                    }
                    catch (Exception ex)
                    {
                        lock (_gate)
                        {
                            foreach (string oco in cancelledOcos)
                                pending.ProtectionMutationRequestedOcos.Remove(oco);
                        }
                        RaiseCritical?.Invoke(
                            account.Name,
                            "Follower protection could not be cancelled before close convergence: " + ex.GetType().Name,
                            "FollowerMasterExitProtectionCancelFailed|" + CleanToken(instrument.FullName));
                        return "protection_cancel_failed_" + ex.GetType().Name;
                    }
                }
                Journal?.Invoke(
                    account.Name,
                    "master_exit_convergence|instrument=" + CleanToken(instrument.FullName)
                    + "|phase=reserve_protection|desired="
                    + desiredProtectedQuantity.ToString(CultureInfo.InvariantCulture)
                    + "|changed=" + changes.Count.ToString(CultureInfo.InvariantCulture)
                    + "|cancelled_oco_groups=" + cancellations.Count.ToString(CultureInfo.InvariantCulture));
                return "awaiting_protection_mutation";
            }
            if (protectedQuantity < desiredProtectedQuantity)
                return "awaiting_protection_repair";

            if (pending.RequiresPositionBarrier)
            {
                lock (_gate)
                {
                    if (!positionUpdateObserved)
                    {
                        pending.ProtectionMutationAcknowledged = true;
                        pending.ProtectionMutationAcknowledgedUtc = DateTime.UtcNow;
                        return "awaiting_position_barrier";
                    }
                    if (!pending.ProtectionMutationAcknowledged
                        || DateTime.UtcNow - pending.ProtectionMutationAcknowledgedUtc
                            < TimeSpan.FromMilliseconds(50))
                        return "awaiting_protection_mutation";
                    pending.RequiresPositionBarrier = false;
                    pending.ProtectionMutationAcknowledged = false;
                }
            }

            int workingOwnedCloseQuantity = orders
                .Where(order => order?.Instrument != null
                    && string.Equals(order.Instrument.FullName, instrument.FullName, StringComparison.OrdinalIgnoreCase)
                    && order.OrderAction == exitAction
                    && ParseFollowerSignalKind(order.Name) == FollowerSignalKind.Close
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                .Sum(RemainingQuantity);
            int availableClosable = Math.Max(0, closableToTarget - workingOwnedCloseQuantity);
            int unreservedExposure = Math.Max(0, Math.Abs(actual) - protectedQuantity);
            int closeQuantity = Math.Min(availableClosable, unreservedExposure);
            if (closeQuantity <= 0)
                return workingOwnedCloseQuantity > 0
                    ? "awaiting_owned_close"
                    : "awaiting_reserved_exposure";
            FollowerOrderSubmission submission = SubmitFollowerClose(
                account,
                instrument,
                pending.IsLongExposure ? OrderAction.Sell : OrderAction.BuyToCover,
                closeQuantity,
                pending.Identity,
                string.IsNullOrWhiteSpace(pending.SignalPrefix)
                    ? CopySignalName
                    : pending.SignalPrefix,
                pending.RecoveryOwner);
            lock (_gate)
            {
                if (_pendingMasterCloses.TryGetValue(key, out PendingMasterClose current)
                    && ReferenceEquals(current, pending))
                {
                    pending.CloseSubmitted = string.Equals(
                        submission.Result,
                        "submitted",
                        StringComparison.OrdinalIgnoreCase);
                    pending.CloseSignal = submission.Signal;
                    pending.CloseOrder = submission.Order;
                    if (pending.SyncOwner != null && IsCurrentSyncLifecycle(pending.SyncOwner))
                    {
                        if (string.Equals(pending.SyncPhase, "flatten", StringComparison.OrdinalIgnoreCase))
                        {
                            pending.SyncOwner.FlattenOrderSignal = submission.Signal;
                            pending.SyncOwner.FlattenOrder = submission.Order;
                        }
                        else if (string.Equals(pending.SyncPhase, "reduce", StringComparison.OrdinalIgnoreCase))
                        {
                            pending.SyncOwner.ReduceOrderSignal = submission.Signal;
                            pending.SyncOwner.ReduceOrder = submission.Order;
                        }
                    }
                    if (!pending.CloseSubmitted)
                        _pendingMasterCloses.Remove(key);
                }
            }
            Journal?.Invoke(
                account.Name,
                "master_exit_convergence|instrument=" + CleanToken(instrument.FullName)
                + "|requested=" + pending.RequestedQuantity.ToString(CultureInfo.InvariantCulture)
                + "|native=" + actual.ToString(CultureInfo.InvariantCulture)
                + "|target=" + desiredTarget.ToString(CultureInfo.InvariantCulture)
                + "|native_closable=" + closableToTarget.ToString(CultureInfo.InvariantCulture)
                + "|working_owned_close=" + workingOwnedCloseQuantity.ToString(CultureInfo.InvariantCulture)
                + "|reserved_protection=" + protectedQuantity.ToString(CultureInfo.InvariantCulture)
                + "|submitted=" + closeQuantity.ToString(CultureInfo.InvariantCulture)
                + "|result=" + CleanToken(submission.Result));
            return submission.Result;
        }

        private FollowerOrderSubmission SubmitFollowerClose(
            Account account,
            Instrument instrument,
            OrderAction action,
            int quantity,
            string identity,
            string signalPrefix,
            FollowerEntryLifecycle recoveryOwner = null)
        {
            string accountToken = GlitchReplicationProtection.StableToken(account?.Name, 6);
            string closeToken = GlitchReplicationProtection.StableToken(identity, 16);
            string signal = signalPrefix + "-X-" + accountToken + "-" + closeToken;
            Order order = null;
            string result;
            bool submitAttempted = false;
            if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(account, instrument, out int initialNet))
                return new FollowerOrderSubmission { Signal = signal, Result = "native_state_unavailable" };
            int closable = action == OrderAction.Sell
                ? Math.Max(0, initialNet)
                : action == OrderAction.BuyToCover
                    ? Math.Max(0, -initialNet)
                    : 0;
            quantity = Math.Min(quantity, closable);
            if (quantity <= 0)
                return new FollowerOrderSubmission { Signal = signal, Result = "already_converged" };
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
                        RecoveryOwner = recoveryOwner,
                        InitialNet = initialNet,
                        TargetNet = initialNet
                            + (GlitchReplicationEngine.GetOrderActionSign(action) * quantity)
                    };
                }
                submitAttempted = true;
                account.Submit(new[] { order });
                if (order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled)
                    throw new InvalidOperationException("close_rejected");
                result = "submitted";
            }
            catch (Exception ex)
            {
                bool nativeOrderVisible = order != null
                    && (GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                        || order.OrderState == OrderState.Filled
                        || order.Filled >= quantity);
                if (!nativeOrderVisible
                    && TrySnapshotOrders(account, out Order[] visibleOrders))
                {
                    Order visibleOrder = visibleOrders.FirstOrDefault(item =>
                        item?.Instrument != null
                        && string.Equals(item.Instrument.FullName, instrument.FullName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(item.Name, signal, StringComparison.OrdinalIgnoreCase)
                        && (GlitchReplicationEngine.IsWorkingOrderState(item.OrderState)
                            || item.OrderState == OrderState.Filled
                            || item.Filled >= quantity));
                    if (visibleOrder != null)
                    {
                        order = visibleOrder;
                        nativeOrderVisible = true;
                        lock (_gate)
                        {
                            if (_closesBySignal.TryGetValue(signal, out CloseState visibleLifecycle))
                                visibleLifecycle.Order = visibleOrder;
                        }
                    }
                }
                if (nativeOrderVisible)
                {
                    // Submit can throw after the adapter has accepted the order.
                    // Native visibility is authoritative and suppresses a retry.
                    result = "submitted";
                    Journal?.Invoke(
                        account?.Name ?? "Unknown",
                        "follower_close_submit|signal=" + CleanToken(signal)
                        + "|result=accepted_despite_" + CleanToken(ex.GetType().Name));
                }
                else
                {
                    lock (_gate)
                        _closesBySignal.Remove(signal);
                    result = (submitAttempted ? "state_unknown_" : "failed_pre_submit_")
                        + ex.GetType().Name;
                    RaiseCritical?.Invoke(
                        account?.Name ?? "Unknown",
                        (submitAttempted
                            ? "Follower close submission state is unknown"
                            : "Follower close could not be constructed")
                            + " and will not be retried automatically: " + ex.GetType().Name,
                        "FollowerCloseFailed|" + GlitchReplicationEngine.GetInstrumentRoot(instrument));
                }
            }
            return new FollowerOrderSubmission { Signal = signal, Order = order, Result = result };
        }

        private void TrackCloseOrder(Account account, Order order, string signal)
        {
            CloseState lifecycle;
            bool terminalFailure = false;
            int unfilledQuantity = 0;
            lock (_gate)
            {
                if (!_closesBySignal.TryGetValue(signal, out lifecycle)
                    || lifecycle == null
                    || !string.Equals(lifecycle.Account?.Name, account?.Name, StringComparison.OrdinalIgnoreCase))
                    return;
                lifecycle.Order = order;
                if (!GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                {
                    unfilledQuantity = RemainingQuantity(order);
                    terminalFailure = unfilledQuantity > 0
                        && (order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled);
                    if (terminalFailure && lifecycle.RecoveryOwner != null)
                        lifecycle.RecoveryOwner.RecoveryCloseSubmitted = false;
                    _closesBySignal.Remove(signal);
                }
            }
            if (!terminalFailure)
                return;

            Journal?.Invoke(
                account.Name,
                "follower_close|signal=" + CleanToken(signal)
                + "|state=" + CleanToken(order.OrderState.ToString())
                + "|unfilled_qty=" + unfilledQuantity.ToString(CultureInfo.InvariantCulture)
                + "|result=terminal_unresolved");
            RaiseCritical?.Invoke(
                account.Name,
                "A Glitch-owned follower close ended before its full quantity executed.",
                "FollowerCloseTerminalUnresolved|"
                    + CleanToken(order.Instrument?.FullName)
                    + "|" + CleanToken(signal));
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
            lock (_gate)
            {
                if (lifecycle.RecoveryCloseSubmitted)
                    return;
                lifecycle.RecoveryCloseSubmitted = true;
            }
            OrderAction recoveryAction = lifecycle.IsLong
                ? OrderAction.Sell
                : OrderAction.BuyToCover;
            int recoveryTarget = followerNet
                + (GlitchReplicationEngine.GetOrderActionSign(recoveryAction) * quantity);
            string queueResult = QueueFollowerCloseAfterProtectionCancel(
                lifecycle.Account,
                lifecycle.Instrument,
                recoveryAction,
                quantity,
                lifecycle.EntrySignal + "|" + reason,
                recoveryTarget,
                CopySignalName,
                null,
                null,
                lifecycle);
            if (!IsPendingCloseAccepted(queueResult))
            {
                lock (_gate)
                    lifecycle.RecoveryCloseSubmitted = false;
            }
            Journal?.Invoke(
                lifecycle.Account.Name,
                "follower_recovery|instrument=" + CleanToken(root)
                + "|reason=" + CleanToken(reason)
                + "|attributable_qty=" + attributableQuantity.ToString(CultureInfo.InvariantCulture)
                + "|native_same_side_qty=" + Math.Abs(followerNet).ToString(CultureInfo.InvariantCulture)
                + "|submitted_qty=" + quantity.ToString(CultureInfo.InvariantCulture)
                + "|result=" + CleanToken(queueResult));
        }

        private FollowerOrderSubmission SubmitFollowerEntry(
            GlitchCopyFollowerRoute route,
            Instrument instrument,
            OrderAction action,
            int quantity,
            int followerAllocationOffset,
            int followerPlanQuantity,
            GlitchReplicationProtectionPlan plan,
            string signalPrefix,
            string identitySource,
            Account masterAccount,
            string masterEntrySignal,
            int masterEntryQuantity,
            string masterOrderIdentity,
            Order masterEntryOrder)
        {
            if (route?.FollowerAccount == null || instrument == null || quantity <= 0)
                return new FollowerOrderSubmission { Result = "invalid_request" };
            List<GlitchScaledProtectionLeg> scaled = null;
            bool protectionAvailable = plan != null
                && GlitchReplicationProtection.TryScalePlanSlice(
                    plan,
                    followerPlanQuantity,
                    followerAllocationOffset,
                    quantity,
                    out scaled);

            string accountToken = GlitchReplicationProtection.StableToken(route.FollowerAccount.Name, 6);
            string entryToken = GlitchReplicationProtection.StableToken(identitySource, 16);
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
                MasterPlanSourceTokens = plan?.Legs == null
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(
                        plan.Legs.Select(leg => leg.SourceToken),
                        StringComparer.OrdinalIgnoreCase),
                ProtectionAvailable = protectionAvailable,
                MasterAccountInstance = masterAccount,
                MasterAccountName = masterAccount?.Name?.Trim(),
                MasterEntrySignal = masterEntrySignal?.Trim(),
                MasterEntryQuantity = Math.Max(0, masterEntryQuantity),
                MasterOrderIdentity = masterOrderIdentity?.Trim(),
                MasterEntryOrder = masterEntryOrder,
                RouteRatio = route.Ratio,
                FollowerAllocationOffset = Math.Max(0, followerAllocationOffset),
                FollowerPlanQuantity = Math.Max(quantity, followerPlanQuantity),
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
            var batches = new List<ProtectionBatch>();
            for (int unitIndex = fromQuantity; unitIndex < toQuantity; unitIndex++)
            {
                GlitchScaledProtectionLeg leg = ResolveUnitLeg(lifecycle.ScaledLegs, unitIndex);
                if (leg == null)
                {
                    failure = "unit_plan_missing";
                    return false;
                }

                string sourceToken = string.IsNullOrWhiteSpace(leg.SourceToken) ? "source" : leg.SourceToken;
                ProtectionBatch batch = batches.FirstOrDefault(item =>
                    string.Equals(item.SourceToken, sourceToken, StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(item.StopPrice - leg.StopPrice) <= 0.0000001d
                    && Math.Abs(item.TargetPrice - leg.TargetPrice) <= 0.0000001d
                    && item.Quantity < MaxNativeProtectionBatchQuantity);
                if (batch == null)
                {
                    batch = new ProtectionBatch
                    {
                        SourceToken = sourceToken,
                        StopPrice = leg.StopPrice,
                        TargetPrice = leg.TargetPrice,
                        FirstUnitIndex = unitIndex
                    };
                    batches.Add(batch);
                }
                batch.Quantity++;
            }

            var orders = new List<Order>();
            foreach (ProtectionBatch batch in batches)
            {
                string sourceToken = batch.SourceToken;
                string unitToken = (batch.FirstUnitIndex + 1).ToString("00", CultureInfo.InvariantCulture);
                string nonce = (Interlocked.Increment(ref _ocoNonce) & 0xffff).ToString("x4", CultureInfo.InvariantCulture);
                string oco = "GLTCP" + sourceToken + lifecycle.EntryToken.Substring(0, Math.Min(6, lifecycle.EntryToken.Length)) + unitToken + nonce;
                string signalTail = sourceToken + "-" + lifecycle.EntryToken + "-" + unitToken;
                OrderAction exitAction = lifecycle.IsLong ? OrderAction.Sell : OrderAction.BuyToCover;
                Order stop = lifecycle.Account.CreateOrder(
                    lifecycle.Instrument, exitAction, OrderType.StopMarket, OrderEntry.Automated, TimeInForce.Gtc,
                    batch.Quantity, 0, batch.StopPrice, oco, CopySignalName + "-S-" + signalTail, DateTime.MaxValue, null);
                Order target = lifecycle.Account.CreateOrder(
                    lifecycle.Instrument, exitAction, OrderType.Limit, OrderEntry.Automated, TimeInForce.Gtc,
                    batch.Quantity, batch.TargetPrice, 0, oco, CopySignalName + "-T-" + signalTail, DateTime.MaxValue, null);
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
                    0,
                    0);
                return true;
            }
            int followerPlanQuantity = followerAllocationOffset + requestedQuantity;
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
                    followerAllocationOffset,
                    followerPlanQuantity);
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
                    followerAllocationOffset,
                    followerPlanQuantity);
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
                    followerAllocationOffset,
                    followerPlanQuantity);
                return true;
            }

            if (!GlitchReplicationProtection.TryScalePlanSlice(
                    plan,
                    followerPlanQuantity,
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
                    followerAllocationOffset,
                    followerPlanQuantity);
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
                MasterAccountInstance = masterAccount,
                RouteRatio = recoveredRatio,
                FollowerAllocationOffset = followerAllocationOffset,
                FollowerPlanQuantity = followerPlanQuantity,
                SubmittedQuantity = requestedQuantity,
                EntryOrder = entryOrder,
                ScaledLegs = scaled,
                MasterPlanSourceTokens = new HashSet<string>(
                    plan.Legs.Select(leg => leg.SourceToken),
                    StringComparer.OrdinalIgnoreCase),
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
            int followerAllocationOffset,
            int followerPlanQuantity)
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
                FollowerPlanQuantity = Math.Max(0, followerPlanQuantity),
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
            string instrumentName = masterOrder.Instrument.FullName?.Trim() ?? string.Empty;
            List<FollowerEntryLifecycle> candidates;
            lock (_gate)
            {
                candidates = _entriesBySignal.Values
                    .Where(lifecycle => lifecycle != null
                        && !lifecycle.ProtectionAvailable
                        && lifecycle.SubmittedQuantity > 0
                        && lifecycle.RouteRatio > 0
                        && lifecycle.FollowerPlanQuantity > 0
                        && lifecycle.MasterEntryQuantity > 0
                        && string.Equals(lifecycle.MasterAccountName, masterName, StringComparison.OrdinalIgnoreCase)
                        && lifecycle.Instrument != null
                        && string.Equals(
                            lifecycle.Instrument.FullName,
                            instrumentName,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(lifecycle => lifecycle.MasterEntryOrder?.Time ?? DateTime.MinValue)
                    .ThenBy(lifecycle => lifecycle.EntrySignal, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            foreach (FollowerEntryLifecycle lifecycle in candidates)
            {
                int requiredMasterQuantity = lifecycle.MasterEntryQuantity;
                HashSet<string> claimedSources = GetClaimedMasterSourceTokens(
                    masterAccount,
                    lifecycle.Instrument,
                    lifecycle.IsLong,
                    lifecycle.MasterOrderIdentity);
                DateTime preferredNotBeforeUtc = lifecycle.MasterEntryOrder?.Time == null
                    || lifecycle.MasterEntryOrder.Time == DateTime.MinValue
                    ? DateTime.MinValue
                    : lifecycle.MasterEntryOrder.Time.Kind == DateTimeKind.Utc
                        ? lifecycle.MasterEntryOrder.Time
                        : lifecycle.MasterEntryOrder.Time.ToUniversalTime();
                if (!GlitchReplicationProtection.TryResolveMasterPlan(
                        masterAccount,
                        lifecycle.Instrument,
                        lifecycle.MasterEntrySignal,
                        requiredMasterQuantity,
                        lifecycle.IsLong,
                        claimedSources,
                        preferredNotBeforeUtc,
                        out GlitchReplicationProtectionPlan plan))
                {
                    LogPlanWait(lifecycle);
                    continue;
                }
                if (!GlitchReplicationProtection.TryScalePlanSlice(
                        plan,
                        lifecycle.FollowerPlanQuantity,
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
                    lifecycle.MasterPlanSourceTokens = new HashSet<string>(
                        plan.Legs.Select(leg => leg.SourceToken),
                        StringComparer.OrdinalIgnoreCase);
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

        private void LogPlanWait(FollowerEntryLifecycle lifecycle)
        {
            lock (_gate)
            {
                if (lifecycle.LatePlanWaitLogged)
                    return;
                lifecycle.LatePlanWaitLogged = true;
            }
            Journal?.Invoke(lifecycle.Account?.Name ?? "Unknown",
                "follower_protection|entry=" + CleanToken(lifecycle.EntrySignal)
                + "|result=waiting_for_complete_master_plan");
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
            double masterPrice = isStop ? masterOrder.StopPrice : masterOrder.LimitPrice;
            foreach (GlitchCopyFollowerRoute route in routes)
            {
                string key = BuildProtectionMirrorKey(
                    route.FollowerAccount,
                    masterOrder.Instrument,
                    sourceToken,
                    isStop);
                lock (_gate)
                {
                    if (_pendingProtectionMirrors.TryGetValue(key, out PendingProtectionMirror existing)
                        && existing != null)
                    {
                        existing.DesiredPrice = masterPrice;
                    }
                    else
                    {
                        _pendingProtectionMirrors[key] = new PendingProtectionMirror
                        {
                            Key = key,
                            Account = route.FollowerAccount,
                            Instrument = masterOrder.Instrument,
                            SourceToken = sourceToken,
                            IsStop = isStop,
                            DesiredPrice = masterPrice
                        };
                    }
                }
                TryApplyPendingProtectionMirror(key, protectionKind);
            }
        }

        private void TryApplyPendingProtectionMirrorForOrder(
            Account followerAccount,
            Order order,
            string signal)
        {
            string sourceToken = ExtractFollowerProtectionSourceToken(signal);
            if (followerAccount == null
                || order?.Instrument == null
                || string.IsNullOrWhiteSpace(sourceToken))
                return;
            bool isStop = GlitchReplicationEngine.IsStopLikeOrder(order);
            string key = BuildProtectionMirrorKey(
                followerAccount,
                order.Instrument,
                sourceToken,
                isStop);
            TryApplyPendingProtectionMirror(key, isStop ? "stop" : "target");
        }

        private void TryApplyPendingProtectionMirror(string key, string protectionKind)
        {
            PendingProtectionMirror pending;
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(key)
                    || !_pendingProtectionMirrors.TryGetValue(key, out pending)
                    || pending == null)
                    return;
                if (_pendingMasterCloses.ContainsKey(
                    BuildFollowerInstrumentKey(pending.Account, pending.Instrument)))
                    return;
            }
            if (!TrySnapshotOrders(pending.Account, out Order[] orders))
                return;

            string prefix = CopySignalName
                + (pending.IsStop ? "-S-" : "-T-")
                + pending.SourceToken
                + "-";
            List<Order> matching = orders
                .Where(order => order?.Instrument != null
                    && string.Equals(order.Instrument.FullName, pending.Instrument.FullName, StringComparison.OrdinalIgnoreCase)
                    && (order.Name ?? string.Empty).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                .ToList();
            if (matching.Count == 0)
                return;
            if (matching.Any(order => !CanChangeOrder(order)))
                return;

            double desiredPrice;
            lock (_gate)
            {
                if (!_pendingProtectionMirrors.TryGetValue(key, out PendingProtectionMirror current)
                    || !ReferenceEquals(current, pending))
                    return;
                if (pending.ChangeInFlight)
                {
                    bool acknowledged = matching.All(order => Math.Abs(
                        (pending.IsStop ? order.StopPrice : order.LimitPrice)
                        - pending.SubmittedPrice) <= 0.0000001d);
                    if (!acknowledged)
                        return;
                    pending.ChangeInFlight = false;
                }
                desiredPrice = pending.DesiredPrice;
            }
            List<Order> changes = matching
                .Where(order => Math.Abs(
                    (pending.IsStop ? order.StopPrice : order.LimitPrice)
                    - desiredPrice) > 0.0000001d)
                .ToList();
            if (changes.Count == 0)
            {
                lock (_gate)
                    _pendingProtectionMirrors.Remove(key);
                return;
            }

            lock (_gate)
            {
                if (!_pendingProtectionMirrors.TryGetValue(key, out PendingProtectionMirror current)
                    || !ReferenceEquals(current, pending)
                    || pending.ChangeInFlight)
                    return;
                pending.ChangeInFlight = true;
                pending.SubmittedPrice = desiredPrice;
            }

            try
            {
                foreach (Order followerOrder in changes)
                {
                    if (pending.IsStop)
                        followerOrder.StopPriceChanged = desiredPrice;
                    else
                        followerOrder.LimitPriceChanged = desiredPrice;
                }
                pending.Account.Change(changes.ToArray());
                Journal?.Invoke(
                    pending.Account.Name,
                    "follower_protection_mirror|instrument=" + CleanToken(pending.Instrument.FullName)
                    + "|kind=" + CleanToken(protectionKind)
                    + "|source=" + CleanToken(pending.SourceToken)
                    + "|orders=" + changes.Count.ToString(CultureInfo.InvariantCulture)
                    + "|result=change_submitted");
            }
            catch (Exception ex)
            {
                bool nativeChangeVisible = false;
                if (TrySnapshotOrders(pending.Account, out Order[] visibleOrders))
                {
                    List<Order> visibleMatching = visibleOrders
                        .Where(order => order?.Instrument != null
                            && string.Equals(order.Instrument.FullName, pending.Instrument.FullName, StringComparison.OrdinalIgnoreCase)
                            && (order.Name ?? string.Empty).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                        .ToList();
                    nativeChangeVisible = changes.All(changed => visibleMatching.Any(visible =>
                        string.Equals(visible.Name, changed.Name, StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(
                            (pending.IsStop ? visible.StopPrice : visible.LimitPrice)
                            - desiredPrice) <= 0.0000001d));
                }
                if (nativeChangeVisible)
                {
                    lock (_gate)
                    {
                        if (_pendingProtectionMirrors.TryGetValue(key, out PendingProtectionMirror current)
                            && ReferenceEquals(current, pending))
                            _pendingProtectionMirrors.Remove(key);
                    }
                    Journal?.Invoke(
                        pending.Account.Name,
                        "follower_protection_mirror|instrument=" + CleanToken(pending.Instrument.FullName)
                        + "|result=accepted_despite_" + CleanToken(ex.GetType().Name));
                    return;
                }
                lock (_gate)
                {
                    if (_pendingProtectionMirrors.TryGetValue(key, out PendingProtectionMirror current)
                        && ReferenceEquals(current, pending)
                        && Math.Abs(pending.SubmittedPrice - desiredPrice) <= 0.0000001d)
                        pending.ChangeInFlight = false;
                }
                foreach (Order followerOrder in changes)
                {
                    if (pending.IsStop)
                        followerOrder.StopPriceChanged = followerOrder.StopPrice;
                    else
                        followerOrder.LimitPriceChanged = followerOrder.LimitPrice;
                }
                RaiseCritical?.Invoke(
                    pending.Account.Name,
                    "Follower " + protectionKind + " could not mirror the master: " + ex.GetType().Name,
                    "FollowerProtectionMirrorFailed|"
                        + CleanToken(pending.Instrument.FullName)
                        + "|" + protectionKind);
            }
        }

        private static bool CanChangeOrder(Order order)
        {
            return order != null
                && (order.OrderState == OrderState.Working
                    || order.OrderState == OrderState.PartFilled);
        }

        private static string BuildProtectionMirrorKey(
            Account account,
            Instrument instrument,
            string sourceToken,
            bool isStop)
        {
            return BuildFollowerInstrumentKey(account, instrument)
                + "|" + (sourceToken ?? string.Empty).Trim()
                + "|" + (isStop ? "S" : "T");
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

            return false;
        }

        private void CancelOwnedOrdersAtFlat(Account account, Instrument instrument, Order[] orders)
        {
            ClearProtectionAmbiguity(account, instrument);
            List<Order> candidates = orders
                .Where(order => order?.Instrument != null
                    && string.Equals(order.Instrument.FullName, instrument.FullName, StringComparison.OrdinalIgnoreCase)
                    && GlitchReplicationEngine.CanCancelOrder(order)
                    && (ParseFollowerSignalKind(order.Name) == FollowerSignalKind.Protection
                        || ParseFollowerSignalKind(order.Name) == FollowerSignalKind.Close))
                .ToList();
            List<Order> cancellations = candidates
                .Where(order => ParseFollowerSignalKind(order.Name) == FollowerSignalKind.Close)
                .ToList();
            cancellations.AddRange(candidates
                .Where(order => ParseFollowerSignalKind(order.Name) == FollowerSignalKind.Protection)
                .GroupBy(
                    order => string.IsNullOrWhiteSpace(order.Oco) ? order.Name : order.Oco,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderBy(order => GlitchReplicationEngine.IsStopLikeOrder(order) ? 0 : 1)
                    .First()));
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
            string mutationKey = BuildFollowerInstrumentKey(account, instrument);
            lock (_gate)
            {
                if (_pendingMasterCloses.ContainsKey(mutationKey))
                    return;
            }
            var protectionOrders = orders
                .Where(order => order?.Instrument != null
                    && string.Equals(order.Instrument.FullName, instrument.FullName, StringComparison.OrdinalIgnoreCase)
                    && ParseFollowerSignalKind(order.Name) == FollowerSignalKind.Protection
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                    && !string.IsNullOrWhiteSpace(order.Oco))
                .ToList();
            if (protectionOrders.Count == 0)
            {
                ReportProtectionDeficit(account, instrument, Math.Abs(netQuantity), 0);
                TryRepairProtectionDeficit(
                    account,
                    instrument,
                    orders,
                    netQuantity,
                    new List<FollowerProtectionUnit>(),
                    0);
                return;
            }

            OrderAction expectedExitAction = netQuantity > 0
                ? OrderAction.Sell
                : OrderAction.BuyToCover;
            var units = new List<FollowerProtectionUnit>();
            foreach (IGrouping<string, Order> group in protectionOrders.GroupBy(
                order => order.Oco.Trim(),
                StringComparer.OrdinalIgnoreCase))
            {
                if (!TryBuildFollowerProtectionUnit(
                        group.Key,
                        group.ToList(),
                        expectedExitAction,
                        netQuantity > 0,
                        out FollowerProtectionUnit unit))
                {
                    ReportProtectionAmbiguity(account, instrument, "incomplete_or_malformed_follower_oco");
                    return;
                }
                units.Add(unit);
            }
            units = units
                .OrderBy(unit => unit.Oco, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int coveredQuantity = units.Sum(unit => unit.Quantity);
            int excess = coveredQuantity - Math.Abs(netQuantity);
            if (excess < 0)
            {
                ReportProtectionDeficit(
                    account,
                    instrument,
                    Math.Abs(netQuantity),
                    coveredQuantity);
                TryRepairProtectionDeficit(
                    account,
                    instrument,
                    orders,
                    netQuantity,
                    units,
                    coveredQuantity);
                return;
            }
            if (excess == 0)
            {
                ClearProtectionAmbiguity(account, instrument);
                return;
            }

            Account protectionMaster = ResolveProtectionMasterAccount(account, instrument, units);
            if (protectionMaster == null)
            {
                ReportProtectionAmbiguity(account, instrument, "unique_master_route_unavailable");
                return;
            }
            if (!GlitchReplicationEngine.TryGetNetQuantityForInstrument(
                    protectionMaster,
                    instrument,
                    out int masterNet)
                || masterNet == 0
                || (masterNet > 0) != (netQuantity > 0))
            {
                ReportProtectionAmbiguity(account, instrument, "authoritative_master_geometry_unavailable");
                return;
            }
            GlitchReplicationProtectionPlan masterPlan;
            bool hasMasterGeometry = GlitchReplicationProtection.TryResolveMasterPlan(
                    protectionMaster,
                    instrument,
                    null,
                    Math.Abs(masterNet),
                    masterNet > 0,
                    out masterPlan);
            if (!hasMasterGeometry)
            {
                hasMasterGeometry = GlitchReplicationProtection.TryResolveSingleOvercoveredMasterGeometry(
                    protectionMaster,
                    instrument,
                    Math.Abs(masterNet),
                    masterNet > 0,
                    out masterPlan);
            }
            if (!hasMasterGeometry
                || !GlitchReplicationProtection.TryScalePlan(
                    masterPlan,
                    Math.Abs(netQuantity),
                    out List<GlitchScaledProtectionLeg> desiredPlan))
            {
                ReportProtectionAmbiguity(account, instrument, "authoritative_master_geometry_unavailable");
                return;
            }

            var desiredGeometry = new List<ProtectionGeometry>();
            foreach (GlitchScaledProtectionLeg leg in desiredPlan)
            {
                for (int i = 0; i < Math.Max(0, leg.Quantity); i++)
                {
                    desiredGeometry.Add(new ProtectionGeometry
                    {
                        SourceToken = leg.SourceToken,
                        StopPrice = leg.StopPrice,
                        TargetPrice = leg.TargetPrice
                    });
                }
            }
            if (desiredGeometry.Count != Math.Abs(netQuantity))
            {
                ReportProtectionAmbiguity(account, instrument, "scaled_master_geometry_incomplete");
                return;
            }

            var keepByUnit = units.ToDictionary(unit => unit, unit => 0);
            foreach (ProtectionGeometry desired in desiredGeometry)
            {
                FollowerProtectionUnit match = units.FirstOrDefault(unit =>
                    keepByUnit[unit] < unit.Quantity
                    && ProtectionGeometryMatches(unit, desired));
                if (match == null)
                {
                    ReportProtectionAmbiguity(account, instrument, "follower_geometry_does_not_match_master");
                    return;
                }
                keepByUnit[match]++;
            }

            var cancellations = new List<Order>();
            var changes = new List<Order>();
            var originalQuantityChanged = new Dictionary<Order, int>();
            foreach (FollowerProtectionUnit unit in units)
            {
                int desiredRemaining = keepByUnit[unit];
                if (desiredRemaining == unit.Quantity)
                    continue;
                if (desiredRemaining == 0)
                {
                    Order cancelOrder = unit.Orders
                        .Where(GlitchReplicationEngine.CanCancelOrder)
                        .OrderBy(order => GlitchReplicationEngine.IsStopLikeOrder(order) ? 0 : 1)
                        .FirstOrDefault();
                    if (cancelOrder == null)
                    {
                        ReportProtectionAmbiguity(account, instrument, "matched_trim_not_cancellable");
                        return;
                    }
                    // One cancellation per native OCO is sufficient; its mate
                    // transitions through OCO without doubling request volume.
                    cancellations.Add(cancelOrder);
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
                        originalQuantityChanged[order] = order.QuantityChanged;
                        order.QuantityChanged = desiredTotal;
                        changes.Add(order);
                    }
                }
            }

            if (cancellations.Count == 0 && changes.Count == 0)
            {
                ClearProtectionAmbiguity(account, instrument);
                return;
            }
            bool nativeMutationFailed = false;
            if (changes.Count > 0)
            {
                try
                {
                    account.Change(changes.ToArray());
                    Journal?.Invoke(
                        account.Name,
                        "excess_protection_resize|basis=master_geometry|instrument=" + CleanToken(instrument.FullName)
                        + "|changed=" + changes.Count.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    nativeMutationFailed = true;
                    foreach (KeyValuePair<Order, int> original in originalQuantityChanged)
                        original.Key.QuantityChanged = original.Value;
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
                        "excess_protection_cancel|basis=master_geometry|instrument=" + CleanToken(instrument.FullName)
                        + "|orders=" + cancellations.Count.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    nativeMutationFailed = true;
                    RaiseCritical?.Invoke(
                        account.Name,
                        "Excess follower protection could not be cancelled: " + ex.GetType().Name,
                        "FollowerProtectionTrimFailed|" + CleanToken(instrument.FullName));
                }
            }
            if (!nativeMutationFailed)
                ClearProtectionAmbiguity(account, instrument);
        }

        private bool TryRepairProtectionDeficit(
            Account account,
            Instrument instrument,
            Order[] orders,
            int netQuantity,
            IReadOnlyList<FollowerProtectionUnit> existingUnits,
            int coveredQuantity)
        {
            if (account == null || instrument == null || netQuantity == 0)
                return false;
            if ((orders ?? Array.Empty<Order>()).Any(order =>
                    order?.Instrument != null
                    && string.Equals(order.Instrument.FullName, instrument.FullName, StringComparison.OrdinalIgnoreCase)
                    && ParseFollowerSignalKind(order.Name) == FollowerSignalKind.Close
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)))
            {
                Journal?.Invoke(
                    account.Name,
                    "follower_protection_repair|instrument=" + CleanToken(instrument.FullName)
                    + "|result=deferred_while_owned_close_working");
                return false;
            }

            GlitchCopyFollowerRoute route = FindUniqueConfiguredRouteForFollower(account);
            Account masterAccount = route?.MasterAccountInstance;
            if (masterAccount == null
                || !GlitchReplicationEngine.TryGetNetQuantityForInstrument(masterAccount, instrument, out int masterNet)
                || masterNet == 0
                || Math.Sign(masterNet) != Math.Sign(netQuantity))
            {
                ReportProtectionAmbiguity(account, instrument, "repair_master_route_or_position_unavailable");
                return false;
            }

            if (!GlitchReplicationProtection.TryResolveMasterPlan(
                    masterAccount,
                    instrument,
                    null,
                    Math.Abs(masterNet),
                    masterNet > 0,
                    out GlitchReplicationProtectionPlan masterPlan)
                || !GlitchReplicationProtection.TryScalePlan(
                    masterPlan,
                    Math.Abs(netQuantity),
                    out List<GlitchScaledProtectionLeg> desiredPlan))
            {
                ReportProtectionAmbiguity(account, instrument, "repair_master_geometry_unavailable");
                return false;
            }

            var missingGeometry = new List<ProtectionGeometry>();
            foreach (GlitchScaledProtectionLeg leg in desiredPlan)
            {
                for (int index = 0; index < Math.Max(0, leg.Quantity); index++)
                {
                    missingGeometry.Add(new ProtectionGeometry
                    {
                        SourceToken = leg.SourceToken,
                        StopPrice = leg.StopPrice,
                        TargetPrice = leg.TargetPrice
                    });
                }
            }
            foreach (FollowerProtectionUnit unit in existingUnits ?? Array.Empty<FollowerProtectionUnit>())
            {
                for (int index = 0; index < Math.Max(0, unit.Quantity); index++)
                {
                    int matchIndex = missingGeometry.FindIndex(desired =>
                        string.Equals(desired.SourceToken, unit.SourceToken, StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(desired.StopPrice - unit.StopPrice) <= 0.0000001d
                        && Math.Abs(desired.TargetPrice - unit.TargetPrice) <= 0.0000001d);
                    if (matchIndex < 0)
                    {
                        ReportProtectionAmbiguity(account, instrument, "repair_existing_geometry_mismatch");
                        return false;
                    }
                    missingGeometry.RemoveAt(matchIndex);
                }
            }

            int expectedMissing = Math.Max(0, Math.Abs(netQuantity) - coveredQuantity);
            if (missingGeometry.Count != expectedMissing || expectedMissing <= 0)
            {
                ReportProtectionAmbiguity(account, instrument, "repair_missing_geometry_mismatch");
                return false;
            }

            string attemptIdentity = BuildFollowerInstrumentKey(account, instrument)
                + "|N" + netQuantity.ToString(CultureInfo.InvariantCulture)
                + "|P" + coveredQuantity.ToString(CultureInfo.InvariantCulture)
                + "|R" + _routeRevision.ToString(CultureInfo.InvariantCulture);
            string entryToken = GlitchReplicationProtection.StableToken(
                "repair|" + attemptIdentity,
                16);
            var batches = new List<ProtectionBatch>();
            foreach (ProtectionGeometry geometry in missingGeometry)
            {
                ProtectionBatch batch = batches.FirstOrDefault(item =>
                    string.Equals(item.SourceToken, geometry.SourceToken, StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(item.StopPrice - geometry.StopPrice) <= 0.0000001d
                    && Math.Abs(item.TargetPrice - geometry.TargetPrice) <= 0.0000001d
                    && item.Quantity < MaxNativeProtectionBatchQuantity);
                if (batch == null)
                {
                    batch = new ProtectionBatch
                    {
                        SourceToken = geometry.SourceToken,
                        StopPrice = geometry.StopPrice,
                        TargetPrice = geometry.TargetPrice,
                        FirstUnitIndex = batches.Sum(item => item.Quantity)
                    };
                    batches.Add(batch);
                }
                batch.Quantity++;
            }

            var repairOrders = new List<Order>();
            int batchIndex = 0;
            foreach (ProtectionBatch batch in batches)
            {
                string sourceToken = string.IsNullOrWhiteSpace(batch.SourceToken)
                    ? "source"
                    : batch.SourceToken;
                string unitToken = (++batchIndex).ToString("000", CultureInfo.InvariantCulture);
                string nonce = (Interlocked.Increment(ref _ocoNonce) & 0xffff)
                    .ToString("x4", CultureInfo.InvariantCulture);
                string oco = "GLTRP"
                    + sourceToken
                    + entryToken.Substring(0, Math.Min(6, entryToken.Length))
                    + unitToken
                    + nonce;
                string signalTail = sourceToken + "-" + entryToken + "-" + unitToken;
                OrderAction exitAction = netQuantity > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
                Order stop = account.CreateOrder(
                    instrument,
                    exitAction,
                    OrderType.StopMarket,
                    OrderEntry.Automated,
                    TimeInForce.Gtc,
                    batch.Quantity,
                    0,
                    batch.StopPrice,
                    oco,
                    CopySignalName + "-S-" + signalTail,
                    DateTime.MaxValue,
                    null);
                Order target = account.CreateOrder(
                    instrument,
                    exitAction,
                    OrderType.Limit,
                    OrderEntry.Automated,
                    TimeInForce.Gtc,
                    batch.Quantity,
                    batch.TargetPrice,
                    0,
                    oco,
                    CopySignalName + "-T-" + signalTail,
                    DateTime.MaxValue,
                    null);
                if (stop == null || target == null)
                {
                    RaiseCritical?.Invoke(
                        account.Name,
                        "Missing follower protection could not be constructed; no repair orders were submitted.",
                        "FollowerProtectionRepairCreateFailed|" + CleanToken(instrument.FullName));
                    return false;
                }
                repairOrders.Add(stop);
                repairOrders.Add(target);
            }

            ProtectionRepairAttempt repairAttempt;
            lock (_gate)
            {
                if (!_protectionRepairAttempts.TryGetValue(attemptIdentity, out repairAttempt))
                {
                    repairAttempt = new ProtectionRepairAttempt
                    {
                        Identity = attemptIdentity,
                        EntryToken = entryToken
                    };
                    _protectionRepairAttempts[attemptIdentity] = repairAttempt;
                }
                if (repairAttempt.InFlight
                    || repairAttempt.AttemptCount >= 3
                    || DateTime.UtcNow < repairAttempt.NextAttemptUtc)
                    return false;
                repairAttempt.InFlight = true;
                repairAttempt.AttemptCount++;
                repairAttempt.SiblingCancelRequested = false;
            }

            try
            {
                account.Submit(repairOrders.ToArray());
                if (repairOrders.Any(order =>
                        order.OrderState == OrderState.Rejected
                        || order.OrderState == OrderState.Cancelled))
                    throw new InvalidOperationException("repair_bracket_rejected");
                Journal?.Invoke(
                    account.Name,
                    "follower_protection_repair|instrument=" + CleanToken(instrument.FullName)
                    + "|missing_qty=" + expectedMissing.ToString(CultureInfo.InvariantCulture)
                    + "|native_orders=" + repairOrders.Count.ToString(CultureInfo.InvariantCulture)
                    + "|result=submitted");
                return true;
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    repairAttempt.InFlight = false;
                    repairAttempt.NextAttemptUtc = DateTime.UtcNow.AddSeconds(
                        Math.Min(4, repairAttempt.AttemptCount));
                }
                RaiseCritical?.Invoke(
                    account.Name,
                    "Missing follower protection repair failed; bounded retry is delayed to prevent request storms: " + ex.GetType().Name,
                    "FollowerProtectionRepairFailed|" + CleanToken(instrument.FullName));
                return false;
            }
        }

        private static bool TryBuildFollowerProtectionUnit(
            string oco,
            List<Order> orders,
            OrderAction expectedExitAction,
            bool isLong,
            out FollowerProtectionUnit unit)
        {
            unit = null;
            List<Order> stops = orders?.Where(GlitchReplicationEngine.IsStopLikeOrder).ToList()
                ?? new List<Order>();
            List<Order> targets = orders?.Where(order => order?.OrderType == OrderType.Limit).ToList()
                ?? new List<Order>();
            if (orders == null
                || stops.Count != 1
                || targets.Count != 1
                || orders.Count != 2
                || orders.Any(order => order.OrderAction != expectedExitAction))
                return false;

            Order stop = stops[0];
            Order target = targets[0];
            int stopQuantity = RemainingQuantity(stop);
            int targetQuantity = RemainingQuantity(target);
            string stopSource = ExtractFollowerProtectionSourceToken(stop.Name);
            string targetSource = ExtractFollowerProtectionSourceToken(target.Name);
            string stopEntry = ExtractFollowerProtectionEntryToken(stop.Name);
            string targetEntry = ExtractFollowerProtectionEntryToken(target.Name);
            if (stopQuantity <= 0
                || stopQuantity != targetQuantity
                || stop.StopPrice <= 0
                || target.LimitPrice <= 0
                || string.IsNullOrWhiteSpace(stopSource)
                || !string.Equals(stopSource, targetSource, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(stopEntry)
                || !string.Equals(stopEntry, targetEntry, StringComparison.OrdinalIgnoreCase)
                || (isLong && stop.StopPrice >= target.LimitPrice)
                || (!isLong && stop.StopPrice <= target.LimitPrice))
                return false;

            unit = new FollowerProtectionUnit
            {
                Oco = oco,
                Orders = orders,
                Quantity = stopQuantity,
                SourceToken = stopSource,
                EntryToken = stopEntry,
                StopPrice = stop.StopPrice,
                TargetPrice = target.LimitPrice
            };
            return true;
        }

        private static string ExtractFollowerProtectionSourceToken(string signal)
        {
            if (string.IsNullOrWhiteSpace(signal))
                return null;
            string value = signal.Trim();
            foreach (string marker in new[]
            {
                CopySignalName + "-S-",
                CopySignalName + "-T-"
            })
            {
                if (!value.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                    continue;
                string tail = value.Substring(marker.Length);
                int separator = tail.IndexOf('-');
                return separator <= 0 ? null : tail.Substring(0, separator);
            }
            return null;
        }

        private static string ExtractFollowerProtectionEntryToken(string signal)
        {
            if (string.IsNullOrWhiteSpace(signal))
                return null;
            string value = signal.Trim();
            foreach (string marker in new[]
            {
                CopySignalName + "-S-",
                CopySignalName + "-T-"
            })
            {
                if (!value.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                    continue;
                string[] tail = value.Substring(marker.Length)
                    .Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
                return tail.Length < 2 ? null : tail[1];
            }
            return null;
        }

        private Account ResolveProtectionMasterAccount(
            Account followerAccount,
            Instrument instrument,
            IReadOnlyList<FollowerProtectionUnit> units)
        {
            List<Account> lifecycleMasters;
            lock (_gate)
            {
                var entryTokens = new HashSet<string>(
                    (units ?? Array.Empty<FollowerProtectionUnit>())
                        .Where(unit => !string.IsNullOrWhiteSpace(unit?.EntryToken))
                        .Select(unit => unit.EntryToken),
                    StringComparer.OrdinalIgnoreCase);
                lifecycleMasters = _entriesBySignal.Values
                    .Where(lifecycle =>
                        lifecycle?.MasterAccountInstance != null
                        && lifecycle.Account != null
                        && string.Equals(
                            lifecycle.Account.Name,
                            followerAccount?.Name,
                            StringComparison.OrdinalIgnoreCase)
                        && lifecycle.Instrument != null
                        && string.Equals(
                            lifecycle.Instrument.FullName,
                            instrument?.FullName,
                            StringComparison.OrdinalIgnoreCase)
                        && entryTokens.Contains(lifecycle.EntryToken))
                    .Select(lifecycle => lifecycle.MasterAccountInstance)
                    .GroupBy(master => master.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
            }
            if (lifecycleMasters.Count == 1)
                return lifecycleMasters[0];
            if (lifecycleMasters.Count > 1)
                return null;
            return FindUniqueConfiguredRouteForFollower(followerAccount)?.MasterAccountInstance;
        }

        private static bool ProtectionGeometryMatches(
            FollowerProtectionUnit follower,
            ProtectionGeometry master)
        {
            // SourceToken derives from the exact native master OCO identity.
            // Prices may be one callback behind while MirrorMasterProtection applies
            // an accepted amendment, so lineage decides survival; price convergence
            // remains separately owned by the mirror path.
            return follower != null
                && master != null
                && string.Equals(
                    follower.SourceToken,
                    master.SourceToken,
                    StringComparison.OrdinalIgnoreCase);
        }

        private void ReportProtectionAmbiguity(
            Account account,
            Instrument instrument,
            string reason)
        {
            string identity = (account?.Name?.Trim() ?? "Unknown")
                + "|"
                + (instrument?.FullName?.Trim() ?? "-")
                + "|"
                + CleanToken(reason);
            lock (_gate)
            {
                if (!_reportedProtectionAmbiguities.Add(identity))
                    return;
            }
            Journal?.Invoke(
                account?.Name ?? "Unknown",
                "follower_protection_reconcile|instrument=" + CleanToken(instrument?.FullName)
                + "|result=ambiguous_unchanged|reason=" + CleanToken(reason));
            RaiseCritical?.Invoke(
                account?.Name ?? "Unknown",
                "Follower protection exceeded native exposure, but the surviving master bracket geometry was ambiguous. Glitch-owned orders were left unchanged.",
                "FollowerProtectionReconcileAmbiguous|"
                    + CleanToken(instrument?.FullName)
                    + "|"
                    + CleanToken(reason));
        }

        private void ClearProtectionAmbiguity(Account account, Instrument instrument)
        {
            string prefix = (account?.Name?.Trim() ?? "Unknown")
                + "|"
                + (instrument?.FullName?.Trim() ?? "-")
                + "|";
            lock (_gate)
            {
                foreach (string identity in _reportedProtectionAmbiguities
                    .Where(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList())
                    _reportedProtectionAmbiguities.Remove(identity);
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

        private static bool IsOpeningAction(Account masterAccount, GlitchCopyExecutionContext context)
        {
            if (context == null)
                return false;

            if (context.Action == OrderAction.SellShort)
                return true;
            if (context.Action == OrderAction.BuyToCover)
                return false;

            // NinjaTrader uses Sell for both a long exit and a manual short
            // entry, and Buy for both a long entry and a short exit. The
            // action alone is therefore not enough to classify the fill.
            // Prefer the explicit signal when one exists. This preserves
            // manual/AI intent; native OCO fills use pre/post position truth.
            string signal = context.OrderSignalName?.Trim() ?? string.Empty;
            if (IsExitSignal(signal))
                return false;
            if (IsEntrySignal(signal))
                return true;

            if (TryGetMasterNet(masterAccount, context, out int masterNet))
            {
                if (context.Action == OrderAction.Sell)
                    return masterNet < 0;
                if (context.Action == OrderAction.Buy)
                    return masterNet > 0;
            }

            // If native position truth is unavailable, retain the historical
            // conservative fallback: unnamed Buy opens and unnamed Sell
            // closes. The engine must never infer a short entry from absence.
            return context.Action == OrderAction.Buy;
        }

        private void ReportProtectionDeficit(
            Account account,
            Instrument instrument,
            int exposureQuantity,
            int protectedQuantity)
        {
            string reason = "underprotected_"
                + protectedQuantity.ToString(CultureInfo.InvariantCulture)
                + "_of_"
                + exposureQuantity.ToString(CultureInfo.InvariantCulture);
            string identity = (account?.Name?.Trim() ?? "Unknown")
                + "|"
                + (instrument?.FullName?.Trim() ?? "-")
                + "|"
                + reason;
            lock (_gate)
            {
                if (!_reportedProtectionAmbiguities.Add(identity))
                    return;
            }
            Journal?.Invoke(
                account?.Name ?? "Unknown",
                "follower_protection_reconcile|instrument=" + CleanToken(instrument?.FullName)
                + "|result=underprotected|protected="
                + protectedQuantity.ToString(CultureInfo.InvariantCulture)
                + "|exposure=" + exposureQuantity.ToString(CultureInfo.InvariantCulture));
            RaiseCritical?.Invoke(
                account?.Name ?? "Unknown",
                "Follower protection covers only "
                    + protectedQuantity.ToString(CultureInfo.InvariantCulture)
                    + " of "
                    + exposureQuantity.ToString(CultureInfo.InvariantCulture)
                    + " open contracts.",
                "FollowerProtectionDeficit|"
                    + CleanToken(instrument?.FullName)
                    + "|" + protectedQuantity.ToString(CultureInfo.InvariantCulture)
                    + "|" + exposureQuantity.ToString(CultureInfo.InvariantCulture));
        }

        private static bool TryResolveExecutionTransition(
            Account masterAccount,
            GlitchCopyExecutionContext context,
            out ExecutionTransition transition)
        {
            transition = null;
            if (context == null
                || context.Quantity <= 0
                || !TryGetMasterNet(masterAccount, context, out int postExecutionNet))
                return false;

            int actionSign = GlitchReplicationEngine.GetOrderActionSign(context.Action);
            if (actionSign == 0)
                return false;
            int preExecutionNet = postExecutionNet - (actionSign * context.Quantity);
            int closeQuantity = preExecutionNet != 0 && Math.Sign(preExecutionNet) != actionSign
                ? Math.Min(Math.Abs(preExecutionNet), context.Quantity)
                : 0;
            int openQuantity = Math.Max(0, context.Quantity - closeQuantity);
            transition = new ExecutionTransition
            {
                CloseQuantity = closeQuantity,
                CloseAction = preExecutionNet > 0 ? OrderAction.Sell : OrderAction.BuyToCover,
                OpenQuantity = openQuantity,
                OpenAction = actionSign > 0 ? OrderAction.Buy : OrderAction.SellShort
            };
            return true;
        }

        private static GlitchCopyExecutionContext CloneExecutionContext(
            GlitchCopyExecutionContext source,
            int quantity,
            OrderAction action,
            string phase)
        {
            bool wholeExecution = source != null && quantity == source.Quantity;
            string suffix = "|" + (phase ?? "phase");
            return new GlitchCopyExecutionContext
            {
                ExecutionId = string.IsNullOrWhiteSpace(source?.ExecutionId)
                    ? null
                    : source.ExecutionId.Trim() + suffix,
                ExecutionTimeUtc = source?.ExecutionTimeUtc ?? DateTime.UtcNow,
                Instrument = source?.Instrument,
                Action = action,
                OrderType = source?.OrderType ?? OrderType.Market,
                Quantity = quantity,
                EntryOrderFilledQuantity = wholeExecution
                    ? Math.Max(quantity, source?.EntryOrderFilledQuantity ?? 0)
                    : quantity,
                EntryOrderQuantity = wholeExecution
                    ? Math.Max(quantity, source?.EntryOrderQuantity ?? 0)
                    : quantity,
                PostExecutionNetQuantity = source?.PostExecutionNetQuantity,
                IsRuntimeEventSnapshot = source?.IsRuntimeEventSnapshot ?? false,
                ExecutionOperation = source?.ExecutionOperation,
                IsSodExecution = source?.IsSodExecution ?? false,
                EntryOrder = source?.EntryOrder,
                OrderIdentity = (source?.OrderIdentity ?? string.Empty) + suffix,
                OrderSignalName = source?.OrderSignalName,
                Oco = source?.Oco
            };
        }

        private HashSet<string> GetClaimedMasterSourceTokens(
            Account masterAccount,
            Instrument instrument,
            bool isLong,
            string currentMasterOrderIdentity)
        {
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (masterAccount == null || instrument == null)
                return claimed;
            lock (_gate)
            {
                foreach (FollowerEntryLifecycle lifecycle in _entriesBySignal.Values)
                {
                    if (lifecycle == null
                        || lifecycle.IsLong != isLong
                        || (!string.IsNullOrWhiteSpace(currentMasterOrderIdentity)
                            && string.Equals(
                                lifecycle.MasterOrderIdentity,
                                currentMasterOrderIdentity,
                                StringComparison.OrdinalIgnoreCase))
                        || !string.Equals(lifecycle.MasterAccountName, masterAccount.Name, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(lifecycle.Instrument?.FullName, instrument.FullName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    foreach (string source in lifecycle.MasterPlanSourceTokens
                        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase))
                        claimed.Add(source);
                }
            }
            return claimed;
        }

        private static OrderAction ResolveEntryAction(
            Account masterAccount,
            GlitchCopyExecutionContext context)
        {
            if (context == null)
                return OrderAction.Buy;
            if (context.Action == OrderAction.Sell
                && (IsEntrySignal(context.OrderSignalName)
                    || (TryGetMasterNet(masterAccount, context, out int shortNet)
                        && shortNet < 0)))
                return OrderAction.SellShort;
            return context.Action;
        }

        private static OrderAction ResolveCloseAction(
            Account masterAccount,
            GlitchCopyExecutionContext context)
        {
            if (context == null)
                return OrderAction.BuyToCover;
            if (context.Action == OrderAction.Buy
                && (IsExitSignal(context.OrderSignalName)
                    || (TryGetMasterNet(masterAccount, context, out int shortNet)
                        && shortNet < 0)))
                return OrderAction.BuyToCover;
            if (context.Action == OrderAction.Sell
                && TryGetMasterNet(masterAccount, context, out int longNet)
                && longNet > 0)
                return OrderAction.Sell;
            return context.Action;
        }

        private static bool TryGetMasterNet(
            Account masterAccount,
            GlitchCopyExecutionContext context,
            out int masterNet)
        {
            masterNet = 0;
            if (context?.PostExecutionNetQuantity != null)
            {
                masterNet = context.PostExecutionNetQuantity.Value;
                return true;
            }
            return masterAccount != null
                && context?.Instrument != null
                && GlitchReplicationEngine.TryGetNetQuantityForInstrument(
                    masterAccount,
                    context.Instrument,
                    out masterNet);
        }

        private static bool IsEntrySignal(string signal)
        {
            return SignalContainsToken(signal, "entry")
                || SignalContainsToken(signal, "e");
        }

        private static bool IsExitSignal(string signal)
        {
            return SignalContainsToken(signal, "exit")
                || SignalContainsToken(signal, "close")
                || SignalContainsToken(signal, "flatten")
                || SignalContainsToken(signal, "x");
        }

        private static bool SignalContainsToken(string signal, string token)
        {
            if (string.IsNullOrWhiteSpace(signal) || string.IsNullOrWhiteSpace(token))
                return false;
            return signal.Split(new[] { '-', '_', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(part => string.Equals(part, token, StringComparison.OrdinalIgnoreCase));
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
                    + "|" + (context?.OrderIdentity ?? string.Empty)
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
            public FollowerEntryLifecycle RecoveryOwner { get; set; }
            public int InitialNet { get; set; }
            public int TargetNet { get; set; }
            public bool CancelRequested { get; set; }
        }

        private sealed class EntryOrderAllocationState
        {
            public string RouteKey { get; set; }
            public double Ratio { get; set; }
            public int MasterQuantity { get; set; }
            public int FollowerQuantity { get; set; }
            public int PlannedMasterQuantity { get; set; }
        }

        private sealed class ExecutionAllocation
        {
            public int Quantity { get; set; }
            public int MasterCumulative { get; set; }
            public int FollowerCumulative { get; set; }
            public int FollowerOrderOffset { get; set; }
            public int FollowerOrderPlanQuantity { get; set; }
            public double Ratio { get; set; }
        }

        private sealed class ExecutionTransition
        {
            public int CloseQuantity { get; set; }
            public OrderAction CloseAction { get; set; }
            public int OpenQuantity { get; set; }
            public OrderAction OpenAction { get; set; }

            public static ExecutionTransition OpenOnly(int quantity, OrderAction action)
            {
                return new ExecutionTransition { OpenQuantity = quantity, OpenAction = action };
            }

            public static ExecutionTransition CloseOnly(int quantity, OrderAction action)
            {
                return new ExecutionTransition { CloseQuantity = quantity, CloseAction = action };
            }
        }

        private sealed class PendingMasterClose
        {
            public string Key { get; set; }
            public Account Account { get; set; }
            public Instrument Instrument { get; set; }
            public bool IsLongExposure { get; set; }
            public int InitialFollowerNet { get; set; }
            public int AuthoritativeTargetNet { get; set; }
            public bool TargetInitialized { get; set; }
            public int RequestedQuantity { get; set; }
            public string Identity { get; set; }
            public HashSet<string> ProtectionMutationRequestedOcos { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public bool CloseSubmitted { get; set; }
            public string CloseSignal { get; set; }
            public Order CloseOrder { get; set; }
            public bool RequiresPositionBarrier { get; set; }
            public bool ProtectionMutationAcknowledged { get; set; }
            public DateTime ProtectionMutationAcknowledgedUtc { get; set; }
            public string SignalPrefix { get; set; }
            public FollowerSyncLifecycle SyncOwner { get; set; }
            public string SyncPhase { get; set; }
            public FollowerEntryLifecycle RecoveryOwner { get; set; }
        }

        private sealed class PendingProtectionMirror
        {
            public string Key { get; set; }
            public Account Account { get; set; }
            public Instrument Instrument { get; set; }
            public string SourceToken { get; set; }
            public bool IsStop { get; set; }
            public double DesiredPrice { get; set; }
            public bool ChangeInFlight { get; set; }
            public double SubmittedPrice { get; set; }
        }

        private sealed class DeferredFollowerOpen
        {
            public GlitchCopyFollowerRoute Route { get; set; }
            public Instrument Instrument { get; set; }
            public OrderAction Action { get; set; }
            public int Quantity { get; set; }
            public int FollowerAllocationOffset { get; set; }
            public int FollowerPlanQuantity { get; set; }
            public GlitchReplicationProtectionPlan Plan { get; set; }
            public string SignalPrefix { get; set; }
            public string IdentitySource { get; set; }
            public Account MasterAccount { get; set; }
            public string MasterEntrySignal { get; set; }
            public int MasterEntryQuantity { get; set; }
            public string MasterOrderIdentity { get; set; }
            public Order MasterEntryOrder { get; set; }
            public long RouteRevision { get; set; }
            public string RouteSignature { get; set; }
            public bool RequiresFollowerFlat { get; set; }
        }

        private sealed class FollowerProtectionExitBlock
        {
            public string Key { get; set; }
            public Account FollowerAccount { get; set; }
            public Account MasterAccount { get; set; }
            public Instrument Instrument { get; set; }
            public int MasterDirection { get; set; }
            public DateTime RecordedUtc { get; set; }
        }

        private sealed class FollowerProtectionUnit
        {
            public string Oco { get; set; }
            public List<Order> Orders { get; set; }
            public int Quantity { get; set; }
            public string SourceToken { get; set; }
            public string EntryToken { get; set; }
            public double StopPrice { get; set; }
            public double TargetPrice { get; set; }
        }

        private sealed class ProtectionBatch
        {
            public string SourceToken { get; set; }
            public int FirstUnitIndex { get; set; }
            public int Quantity { get; set; }
            public double StopPrice { get; set; }
            public double TargetPrice { get; set; }
        }

        private sealed class ProtectionGeometry
        {
            public string SourceToken { get; set; }
            public double StopPrice { get; set; }
            public double TargetPrice { get; set; }
        }

        private sealed class ProtectionRepairAttempt
        {
            public string Identity { get; set; }
            public string EntryToken { get; set; }
            public int AttemptCount { get; set; }
            public bool InFlight { get; set; }
            public DateTime NextAttemptUtc { get; set; }
            public bool SiblingCancelRequested { get; set; }
        }

        private sealed class FollowerEntryLifecycle
        {
            public string EntrySignal { get; set; }
            public string EntryToken { get; set; }
            public Account Account { get; set; }
            public Instrument Instrument { get; set; }
            public bool IsLong { get; set; }
            public Account MasterAccountInstance { get; set; }
            public string MasterAccountName { get; set; }
            public string MasterEntrySignal { get; set; }
            public int MasterEntryQuantity { get; set; }
            public string MasterOrderIdentity { get; set; }
            public Order MasterEntryOrder { get; set; }
            public double RouteRatio { get; set; }
            public int FollowerAllocationOffset { get; set; }
            public int FollowerPlanQuantity { get; set; }
            public int SubmittedQuantity { get; set; }
            public Order EntryOrder { get; set; }
            public int ProtectedQuantity { get; set; }
            public bool ProtectionSubmissionInProgress { get; set; }
            public bool ProtectionFailed { get; set; }
            public bool ProtectionAvailable { get; set; }
            public bool RecoveryCloseSubmitted { get; set; }
            public List<GlitchScaledProtectionLeg> ScaledLegs { get; set; }
            public HashSet<string> MasterPlanSourceTokens { get; set; }
            public bool LatePlanWaitLogged { get; set; }
        }
    }
}
