//
// GlitchCopyEngine — master fill fan-out to followers at configured ratio.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NinjaTrader.Cbi;

namespace Glitch.Services
{
    public sealed class GlitchCopyExecutionContext
    {
        public string ExecutionId { get; set; }
        public Instrument Instrument { get; set; }
        public OrderAction Action { get; set; }
        public int Quantity { get; set; }
        public string OrderSignalName { get; set; }
    }

    public sealed class GlitchCopyFollowerRoute
    {
        public string MasterAccount { get; set; }
        public Account MasterAccountInstance { get; set; }
        public Account FollowerAccount { get; set; }
        public double Ratio { get; set; }
    }

    public sealed class GlitchCopyEngine
    {
        public const string CopySignalName = "GLT-COPY";
        public const string CatchUpSignalName = "GLT-CATCHUP";
        private const int MaxCopySubmitAttempts = 3;
        private static readonly TimeSpan PendingMasterCopyTtl = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan FollowerLifecycleTransitionTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan FollowerProtectionVisibilityGrace = TimeSpan.FromSeconds(3);

        private readonly object _gate = new object();
        private readonly LinkedList<string> _seenExecutionIds = new LinkedList<string>();
        private readonly HashSet<string> _seenExecutionIdSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<GlitchCopyFollowerRoute>> _routesByMaster =
            new Dictionary<string, List<GlitchCopyFollowerRoute>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CopyRetryTicket> _pendingCopyRetries =
            new Dictionary<string, CopyRetryTicket>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FollowerProtectionTicket> _followerProtectionByKey =
            new Dictionary<string, FollowerProtectionTicket>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ActiveFollowerProtection> _activeFollowerProtectionByKey =
            new Dictionary<string, ActiveFollowerProtection>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GlitchReplicationProtectionPlan> _protectionPlansByEntrySignal =
            new Dictionary<string, GlitchReplicationProtectionPlan>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PendingMasterCopy> _pendingMasterCopies =
            new Dictionary<string, PendingMasterCopy>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FollowerEntryLifecycle> _entryLifecyclesBySignal =
            new Dictionary<string, FollowerEntryLifecycle>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _flattenInFlight =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PendingFollowerFlatten> _pendingFollowerFlattens =
            new Dictionary<string, PendingFollowerFlatten>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan FollowerFlattenConfirmationTimeout = TimeSpan.FromSeconds(5);
        private bool _enabled;

        public Action<string, string> Journal { get; set; }
        public Action<string, string, string> RaiseCritical { get; set; }

        public bool IsEnabled
        {
            get
            {
                lock (_gate)
                    return _enabled;
            }
        }

        public void Configure(bool enabled, IReadOnlyList<GlitchCopyFollowerRoute> routes)
        {
            lock (_gate)
            {
                _enabled = enabled;
                _routesByMaster.Clear();
                if (!enabled || routes == null)
                    return;

                foreach (GlitchCopyFollowerRoute route in routes)
                {
                    if (route == null ||
                        string.IsNullOrWhiteSpace(route.MasterAccount) ||
                        route.FollowerAccount == null ||
                        double.IsNaN(route.Ratio) ||
                        double.IsInfinity(route.Ratio) ||
                        route.Ratio <= 0)
                    {
                        continue;
                    }

                    string masterKey = route.MasterAccount.Trim();
                    if (!_routesByMaster.TryGetValue(masterKey, out List<GlitchCopyFollowerRoute> bucket))
                    {
                        bucket = new List<GlitchCopyFollowerRoute>();
                        _routesByMaster[masterKey] = bucket;
                    }

                    bucket.Add(route);
                }
            }
        }

        public void ProcessMasterExecution(Account masterAccount, GlitchCopyExecutionContext context)
        {
            if (masterAccount == null || context == null)
                return;

            List<GlitchCopyFollowerRoute> routes;
            lock (_gate)
            {
                if (!_enabled)
                    return;

                string masterName = masterAccount.Name?.Trim();
                if (string.IsNullOrWhiteSpace(masterName))
                    return;

                if (!_routesByMaster.TryGetValue(masterName, out routes) || routes == null || routes.Count == 0)
                    return;
                routes = routes.ToList();
            }

            if (context.Instrument == null || context.Quantity <= 0)
                return;

            // AI entries are copied, then every account owns its native bracket.
            // Copying the master's stop/target fill races the followers' matching
            // brackets and can reverse a follower after its own exit fills.
            if (IsFollowerOwnedSignal(context.OrderSignalName)
                || IsAiProtectionSignal(context.OrderSignalName))
                return;

            bool isOpeningExecution = context.Action == OrderAction.Buy
                || context.Action == OrderAction.SellShort;
            bool entryIsLong = context.Action == OrderAction.Buy;
            if (isOpeningExecution)
            {
                GlitchAiRailPolicy directionPolicy = GlitchAiRailPolicyStore.Load();
                if (!GlitchApexDirectionGuard.TryApproveEntry(
                    masterAccount,
                    context.Instrument,
                    entryIsLong ? 1 : -1,
                    directionPolicy.SnapshotMaxAgeSeconds,
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
            }
            bool hasProtectionPlan = TryResolveMasterProtectionPlan(
                masterAccount,
                context.Instrument,
                context.OrderSignalName,
                entryIsLong,
                out GlitchReplicationProtectionPlan protectionPlan);
            if (isOpeningExecution && !hasProtectionPlan)
            {
                string missingProtectionInstrument = GlitchReplicationEngine.GetInstrumentRoot(context.Instrument);
                lock (_gate)
                {
                    _pendingMasterCopies[BuildPendingMasterCopyKey(masterAccount, context)] = new PendingMasterCopy
                    {
                        MasterAccount = masterAccount,
                        Context = CloneExecutionContext(context),
                        CreatedUtc = DateTime.UtcNow
                    };
                }
                Journal?.Invoke(
                    masterAccount.Name,
                    "copy_wait|reason=master_bracket_not_working|instrument=" + CleanToken(missingProtectionInstrument));
                return;
            }

            lock (_gate)
                _pendingMasterCopies.Remove(BuildPendingMasterCopyKey(masterAccount, context));

            string dedupKey = BuildExecutionDedupKey(masterAccount.Name, context);
            if (!TryRememberExecutionId(dedupKey))
                return;

            // Entry executions can be split across multiple fills. Reconcile
            // followers once from the live master net after its complete native
            // bracket exists; never fan out each execution fragment separately.
            if (isOpeningExecution)
            {
                foreach (GlitchCopyFollowerRoute route in routes.Where(route => route?.FollowerAccount != null))
                    AlignFollowerToMaster(masterAccount, route.FollowerAccount, route.Ratio, "master_entry_protected", protectionPlan);
                return;
            }

            string instrumentToken = GlitchReplicationEngine.GetInstrumentRoot(context.Instrument);
            string masterNameForJournal = masterAccount.Name?.Trim() ?? "Unknown";
            string execIdToken = string.IsNullOrWhiteSpace(context.ExecutionId) ? dedupKey : context.ExecutionId.Trim();

            bool machineMasterFlattenRequested = false;
            foreach (GlitchCopyFollowerRoute route in routes)
            {
                if (route?.FollowerAccount == null)
                    continue;
                int masterNet = GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(masterAccount, instrumentToken);
                int masterDirection = masterNet != 0
                    ? Math.Sign(masterNet)
                    : context.Action == OrderAction.Buy
                        ? 1
                        : context.Action == OrderAction.SellShort
                            ? -1
                            : 0;
                int followerNetBeforeCopy = GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(
                    route.FollowerAccount,
                    instrumentToken);
                if (masterDirection != 0 && followerNetBeforeCopy != 0
                    && masterDirection != Math.Sign(followerNetBeforeCopy))
                {
                    bool glitchOwnedMaster = IsAiEntrySignal(context.OrderSignalName);
                    string flattenResult = "account_state_preserved";
                    string flattenFailure = null;
                    if (glitchOwnedMaster && !machineMasterFlattenRequested)
                    {
                        flattenResult = TryFlattenFollowerInstrument(
                            masterAccount,
                            context.Instrument,
                            "ai_master_fill_cross_direction",
                            out flattenFailure);
                        machineMasterFlattenRequested = IsSubmissionStarted(flattenResult);
                    }
                    else if (glitchOwnedMaster)
                    {
                        flattenResult = "already_requested";
                    }
                    Journal?.Invoke(route.FollowerAccount.Name,
                        "cross_direction_blocked|source=master_fill|action="
                        + (glitchOwnedMaster ? "flatten_glitch_master" : "preserve_account_state") + "|instrument="
                        + CleanToken(instrumentToken) + "|result=" + CleanToken(flattenResult)
                        + "|failure=" + CleanToken(flattenFailure));
                    RaiseCritical?.Invoke(
                        route.FollowerAccount.Name,
                        glitchOwnedMaster
                            ? "A Glitch master fill raced opposite follower exposure. The Glitch-owned fill was unwound and existing account state was preserved."
                            : "Opposite master/follower exposure was detected. Glitch preserved account state and refused replication.",
                        "CrossDirectionBlocked|" + instrumentToken);
                    continue;
                }

                int followerQty = (int)Math.Round(context.Quantity * route.Ratio, MidpointRounding.AwayFromZero);
                if (followerQty < 1)
                {
                    JournalCopy(
                        route.FollowerAccount.Name,
                        execIdToken,
                        masterNameForJournal,
                        route.FollowerAccount.Name,
                        instrumentToken,
                        context.Quantity,
                        context.Action,
                        route.Ratio,
                        0,
                        "copy_skip|ratio_rounds_to_zero");
                    continue;
                }

                // A close execution may arrive while a follower is already flat
                // (for example after AI-group recovery or an earlier sync). Never
                // let a BuyToCover/Sell close cross zero and create a reverse trade.
                int followerNet = GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(
                    route.FollowerAccount,
                    instrumentToken);
                int closableQuantity = context.Action == OrderAction.BuyToCover
                    ? Math.Max(0, -followerNet)
                    : context.Action == OrderAction.Sell
                        ? Math.Max(0, followerNet)
                        : int.MaxValue;
                if (closableQuantity != int.MaxValue)
                {
                    followerQty = Math.Min(followerQty, closableQuantity);
                    if (followerQty < 1)
                    {
                        JournalCopy(
                            route.FollowerAccount.Name,
                            execIdToken,
                            masterNameForJournal,
                            route.FollowerAccount.Name,
                            instrumentToken,
                            context.Quantity,
                            context.Action,
                            route.Ratio,
                            0,
                            "copy_skip|follower_has_no_closable_exposure");
                        continue;
                    }

                    // The current replication contract supports complete exits,
                    // not partial scale-outs. Keep the follower protected rather
                    // than leaving an oversized bracket behind.
                    if (followerQty != closableQuantity)
                    {
                        JournalCopy(
                            route.FollowerAccount.Name,
                            execIdToken,
                            masterNameForJournal,
                            route.FollowerAccount.Name,
                            instrumentToken,
                            context.Quantity,
                            context.Action,
                            route.Ratio,
                            0,
                            "copy_skip|partial_exit_requires_bracket_resize");
                        RaiseCritical?.Invoke(
                            route.FollowerAccount.Name,
                            "Partial follower exit was not copied because its native bracket cannot yet be resized safely.",
                            "PartialFollowerExitUnsupported|" + instrumentToken);
                        continue;
                    }
                }

                SubmitCopyWithRetry(
                    route.FollowerAccount,
                    context.Instrument,
                    context.Action,
                    followerQty,
                    execIdToken,
                    masterNameForJournal,
                    instrumentToken,
                    context.Quantity,
                    route.Ratio,
                    protectionPlan,
                    closableQuantity != int.MaxValue);
            }
        }

        public bool ProcessExternalFollowerExecution(Account account, GlitchCopyExecutionContext context)
        {
            if (account == null || context?.Instrument == null)
                return false;
            // Exact Glitch signals continue through their normal lifecycle. Any
            // other follower execution is authoritative external account state:
            // observe it, do not copy it as a master fill, and do not create a
            // persistent route state that can outlive the actual discrepancy.
            if (IsFollowerOwnedSignal(context.OrderSignalName)
                || IsAiEntrySignal(context.OrderSignalName)
                || IsAiProtectionSignal(context.OrderSignalName))
                return false;

            bool configuredFollower;
            lock (_gate)
            {
                configuredFollower = _enabled && _routesByMaster.Values
                    .SelectMany(routes => routes ?? new List<GlitchCopyFollowerRoute>())
                    .Any(route => route?.FollowerAccount != null
                        && string.Equals(route.FollowerAccount.Name, account.Name, StringComparison.OrdinalIgnoreCase));
            }
            if (!configuredFollower)
                return false;

            string root = GlitchReplicationEngine.GetInstrumentRoot(context.Instrument);
            Journal?.Invoke(account.Name,
                "external_follower_execution|action=observed|instrument=" + CleanToken(root)
                + "|signal=" + CleanToken(context.OrderSignalName));
            return true;
        }

        public void ProcessFollowerOrderUpdate(Account followerAccount, Order order)
        {
            if (followerAccount == null || order == null || order.Instrument == null)
                return;
            if (!IsFollowerOwnedSignal(order.Name))
                return;

            if (IsFollowerProtectionSignal(order.Name))
            {
                HandleFollowerProtectionOrderUpdate(followerAccount, order);
                return;
            }

            FollowerEntryLifecycle lifecycle = null;
            bool beginProtectionSubmission = false;
            string entrySignal = order.Name?.Trim();
            lock (_gate)
            {
                if (!string.IsNullOrWhiteSpace(entrySignal))
                    _entryLifecyclesBySignal.TryGetValue(entrySignal, out lifecycle);
                if (lifecycle != null)
                {
                    lifecycle.EntryOrder = order;
                    bool terminalPositiveFill = order.Filled > 0
                        && (order.OrderState == OrderState.Filled || IsRejectedOrCancelled(order.OrderState));
                    if (terminalPositiveFill)
                    {
                        if (lifecycle.State == FollowerLifecycleState.EntryPending)
                        {
                            TransitionLifecycleUnsafe(lifecycle, FollowerLifecycleState.ProtectionSubmitting);
                            beginProtectionSubmission = true;
                        }
                        else
                        {
                            // Filled order updates are not unique in NT. Once this
                            // lifecycle has advanced, every duplicate is a no-op.
                            return;
                        }
                    }
                }
            }

            if (beginProtectionSubmission)
            {
                var protectionTicket = new FollowerProtectionTicket
                {
                    Account = lifecycle.Account,
                    Instrument = lifecycle.Instrument,
                    EntryOrder = order,
                    EntrySignalName = lifecycle.EntrySignalName,
                    Plan = lifecycle.Plan
                };
                bool protectedOk = SubmitFollowerProtection(protectionTicket, order.Filled, order.AverageFillPrice, out string protectionFailure);
                lock (_gate)
                {
                    TransitionLifecycleUnsafe(
                        lifecycle,
                        protectedOk ? FollowerLifecycleState.Protected : FollowerLifecycleState.Failed,
                        protectionFailure);
                }
                if (!protectedOk)
                    ReconcileFollowerRoot(followerAccount, order.Instrument, "protection_submit_failed");
                return;
            }

            if (lifecycle != null && order.Filled > 0 && !IsTerminalOrderState(order.OrderState))
            {
                // A market entry can report one or more partial fills before its
                // terminal Filled update. EntryPending is itself the protection
                // audit's transition guard, so keep aggregating on the same order
                // and create one bracket set from the final filled quantity/price.
                // Treating a normal partial fill as Failed races the audit and can
                // flatten part of an otherwise healthy catch-up entry.
                return;
            }

            if (lifecycle == null && order.OrderState == OrderState.Filled
                && IsProtectedFollowerEntrySignal(order.Name))
            {
                // Restart recovery is derived from the live order book. A duplicate
                // filled-entry event for an already protected position is harmless.
                if (HasCompleteFollowerProtection(followerAccount, order.Instrument))
                    return;
                ReconcileFollowerRoot(followerAccount, order.Instrument, "entry_plan_unavailable");
                return;
            }

            if (!IsRejectedOrCancelled(order.OrderState))
                return;

            CopyRetryTicket ticket;
            string retryKey;
            lock (_gate)
            {
                if (!_enabled)
                    return;
                if (!TryFindCopyRetryTicketUnsafe(order, out retryKey, out ticket) || ticket == null)
                    return;
                if (ticket.Attempts >= MaxCopySubmitAttempts)
                {
                    _pendingCopyRetries.Remove(retryKey);
                    return;
                }
            }

            ticket.Attempts++;
            string result = TrySubmitProtectedFollowerEntry(
                followerAccount,
                ticket.Instrument,
                ticket.Action,
                ticket.Quantity,
                ticket.EntrySignalName,
                ticket.ProtectionPlan,
                out string failureReason,
                out Order submittedOrder);
            ticket.SubmittedOrder = submittedOrder;

            JournalCopy(
                followerAccount.Name,
                ticket.ExecutionId,
                ticket.MasterAccount,
                followerAccount.Name,
                ticket.InstrumentToken,
                ticket.MasterFillQty,
                ticket.Action,
                ticket.Ratio,
                ticket.Quantity,
                "copy_retry|" + result);

            if (IsSubmissionStarted(result))
            {
                lock (_gate)
                {
                    _pendingCopyRetries.Remove(retryKey);
                }
                if (submittedOrder != null && submittedOrder.Filled > 0
                    && IsTerminalOrderState(submittedOrder.OrderState))
                    ProcessFollowerOrderUpdate(followerAccount, submittedOrder);
                return;
            }

            bool terminalRejectionProved = submittedOrder != null
                && submittedOrder.Filled == 0
                && IsRejectedOrCancelled(submittedOrder.OrderState);
            if (ticket.Attempts >= MaxCopySubmitAttempts || !terminalRejectionProved)
            {
                lock (_gate)
                    _pendingCopyRetries.Remove(retryKey);

                string followerName = followerAccount.Name?.Trim() ?? "Unknown";
                string message =
                    $"Copy failed on {followerName} for {ticket.InstrumentToken}: {failureReason ?? result}";
                RaiseCritical?.Invoke(followerName, message, $"CopySubmitFailed|{ticket.InstrumentToken}");
            }
            else
            {
                lock (_gate)
                    _pendingCopyRetries[retryKey] = ticket;
                ProcessFollowerOrderUpdate(followerAccount, submittedOrder);
            }
        }

        public void AlignFollowerToMaster(
            Account masterAccount,
            Account followerAccount,
            double ratio,
            string origin)
        {
            AlignFollowerToMaster(masterAccount, followerAccount, ratio, origin, null);
        }

        private void AlignFollowerToMaster(
            Account masterAccount,
            Account followerAccount,
            double ratio,
            string origin,
            GlitchReplicationProtectionPlan preferredEntryPlan)
        {
            lock (_gate)
            {
                if (!_enabled)
                    return;
            }

            if (masterAccount == null || followerAccount == null)
                return;
            if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio <= 0)
                return;

            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            GlitchReplicationEngine.CollectPositionInstrumentRoots(masterAccount, roots);
            GlitchReplicationEngine.CollectPositionInstrumentRoots(followerAccount, roots);

            string masterName = masterAccount.Name?.Trim() ?? "Unknown";
            string followerName = followerAccount.Name?.Trim() ?? "Unknown";
            string originToken = string.IsNullOrWhiteSpace(origin) ? "user" : origin.Trim();

            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                int masterNet = GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(masterAccount, root);
                int expected = (int)Math.Round(masterNet * ratio, MidpointRounding.AwayFromZero);
                int actual = GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(followerAccount, root);
                int delta = expected - actual;

                Instrument instrument = GlitchReplicationEngine.FindInstrumentForInstrumentRoot(masterAccount, root)
                    ?? GlitchReplicationEngine.FindInstrumentForInstrumentRoot(followerAccount, root);
                if (instrument == null)
                    continue;

                bool sameDirectionReduction = actual != 0 && expected != 0
                    && Math.Sign(actual) == Math.Sign(expected)
                    && Math.Abs(expected) < Math.Abs(actual);
                if (sameDirectionReduction)
                {
                    Journal?.Invoke(followerName,
                        "catchup_refused|reason=partial_reduction_requires_bracket_resize|instrument=" + CleanToken(root));
                    RaiseCritical?.Invoke(
                        followerName,
                        "Follower has excess same-direction exposure. Automatic partial reduction was refused because native brackets cannot be resized atomically; flatten the follower, then explicitly resync.",
                        "CatchUpPartialReductionUnsafe|" + root);
                    continue;
                }

                if (actual != 0 && expected != 0 && Math.Sign(actual) != Math.Sign(expected))
                {
                    Journal?.Invoke(followerName,
                        "cross_direction_blocked|action=no_submission|instrument=" + CleanToken(root));
                    RaiseCritical?.Invoke(
                        followerName,
                        "Cross-direction master/follower exposure requires manual resolution; Glitch submitted no order.",
                        "CrossDirectionBlocked|" + root);
                    continue;
                }

                if (delta == 0)
                {
                    if (expected != 0 && !HasCompleteFollowerProtection(followerAccount, instrument))
                    {
                        if (!TryBuildCatchUpProtectionPlan(masterAccount, instrument, expected > 0, out GlitchReplicationProtectionPlan alignedPlan)
                            || !TryGetPositionFill(followerAccount, instrument, out int alignedQuantity, out double alignedAveragePrice))
                        {
                            RequestFollowerFlattenOnce(followerAccount, instrument, "aligned_plan_missing");
                            continue;
                        }

                        bool restored = SubmitFollowerProtection(
                            new FollowerProtectionTicket
                            {
                                Account = followerAccount,
                                Instrument = instrument,
                                Plan = alignedPlan
                            },
                            alignedQuantity,
                            alignedAveragePrice,
                            out string restoreFailure);
                        if (!restored)
                            RequestFollowerFlattenOnce(followerAccount, instrument, "aligned_restore_" + (restoreFailure ?? "failed"));
                    }
                    continue;
                }

                if (!GlitchReplicationEngine.TryResolveCatchUpOrder(actual, delta, out OrderAction action, out int quantity))
                    continue;

                GlitchReplicationProtectionPlan catchUpProtection = null;
                int preferredMasterQuantity = preferredEntryPlan?.Legs == null
                    ? 0
                    : preferredEntryPlan.Legs.Sum(leg => Math.Max(0, leg?.MasterQuantity ?? 0));
                int preferredFollowerQuantity = preferredMasterQuantity <= 0
                    ? 0
                    : (int)Math.Round(preferredMasterQuantity * ratio, MidpointRounding.AwayFromZero);
                if (expected != 0 && preferredEntryPlan != null && Math.Abs(delta) == preferredFollowerQuantity)
                    catchUpProtection = preferredEntryPlan;
                if (expected != 0 && catchUpProtection == null
                    && !TryBuildCatchUpProtectionPlan(masterAccount, instrument, expected > 0, out catchUpProtection))
                {
                    RaiseCritical?.Invoke(
                        followerName,
                        "AI catch-up refused because the master's complete native bracket could not be cloned.",
                        "CatchUpProtectionMissing|" + root);
                    continue;
                }

                string result;
                string failureReason;
                Order submittedOrder = null;
                if (expected == 0)
                {
                    result = TryFlattenFollowerInstrument(
                        followerAccount,
                        instrument,
                        "catchup_expected_flat",
                        out failureReason);
                }
                else
                {
                    string catchUpSignal = BuildFollowerEntrySignal(catchUpProtection, followerAccount, "catchup");
                    result = TrySubmitProtectedFollowerEntry(
                        followerAccount,
                        instrument,
                        action,
                        quantity,
                        catchUpSignal,
                        catchUpProtection,
                        out failureReason,
                        out submittedOrder);
                }

                if (IsSubmissionStarted(result) && submittedOrder != null)
                {
                    if (submittedOrder.Filled > 0 && IsTerminalOrderState(submittedOrder.OrderState))
                        ProcessFollowerOrderUpdate(followerAccount, submittedOrder);
                }

                if (Journal != null)
                {
                    string ratioText = ratio.ToString("0.####", CultureInfo.InvariantCulture);
                    string message =
                        $"catchup|origin={CleanToken(originToken)}|master={CleanToken(masterName)}|follower={CleanToken(followerName)}|instrument={CleanToken(root)}|masterNet={masterNet}|expected={expected}|actual={actual}|delta={delta}|ratio={ratioText}|qty={quantity}|action={action}|result={CleanToken(result)}";
                    Journal(followerName, message);
                }

                if (!IsSubmissionStarted(result) && RaiseCritical != null)
                {
                    string message = $"Catch-up failed on {followerName} for {root}: {failureReason ?? result}";
                    RaiseCritical.Invoke(followerName, message, $"CatchUpFailed|{root}");
                }
            }
        }

        private void SubmitCopyWithRetry(
            Account followerAccount,
            Instrument instrument,
            OrderAction action,
            int followerQty,
            string execIdToken,
            string masterNameForJournal,
            string instrumentToken,
            int masterFillQty,
            double ratio,
            GlitchReplicationProtectionPlan protectionPlan,
            bool isClosingExecution)
        {
            string retryKey = BuildCopyRetryKey(followerAccount.Name, instrument, action, execIdToken);
            string failureReason = null;
            Order submittedOrder = null;
            string result;
            if (isClosingExecution)
            {
                // NinjaTrader's AddOn Flatten operation owns the complete close:
                // it cancels working orders for this account/instrument, waits for
                // cancellation, then closes the remaining position. Do not race a
                // separate market order against the follower's native OCO bracket.
                result = TryFlattenFollowerInstrument(
                    followerAccount,
                    instrument,
                    "copied_master_exit",
                    out failureReason);
            }
            else
            {
                string entrySignal = BuildFollowerEntrySignal(protectionPlan, followerAccount, execIdToken);
                result = TrySubmitProtectedFollowerEntry(
                    followerAccount,
                    instrument,
                    action,
                    followerQty,
                    entrySignal,
                    protectionPlan,
                    out failureReason,
                    out submittedOrder);
            }

            JournalCopy(
                followerAccount.Name,
                execIdToken,
                masterNameForJournal,
                followerAccount.Name,
                instrumentToken,
                masterFillQty,
                action,
                ratio,
                followerQty,
                result);

            if (IsSubmissionStarted(result))
            {
                lock (_gate)
                {
                    _pendingCopyRetries.Remove(retryKey);
                }
                if (submittedOrder != null && submittedOrder.Filled > 0
                    && IsTerminalOrderState(submittedOrder.OrderState))
                    ProcessFollowerOrderUpdate(followerAccount, submittedOrder);
                return;
            }

            string followerName = followerAccount.Name?.Trim() ?? "Unknown";
            string message =
                $"Copy failed on {followerName} for {instrumentToken}: {failureReason ?? result}";
            if (isClosingExecution)
            {
                // Flatten has no single order to retry from an OrderUpdate event.
                // Surface the failure once and leave the existing native bracket
                // intact; a blind second flatten request would add no information.
                RaiseCritical?.Invoke(followerName, message, $"CopySubmitFailed|{instrumentToken}");
                return;
            }

            bool terminalRejectionProved = submittedOrder != null
                && submittedOrder.Filled == 0
                && IsRejectedOrCancelled(submittedOrder.OrderState);
            if (!terminalRejectionProved)
            {
                RaiseCritical?.Invoke(followerName, message, $"CopySubmitStateUnknown|{instrumentToken}");
                return;
            }

            lock (_gate)
            {
                _pendingCopyRetries[retryKey] = new CopyRetryTicket
                {
                    FollowerAccount = followerAccount,
                    Instrument = instrument,
                    Action = action,
                    Quantity = followerQty,
                    ExecutionId = execIdToken,
                    MasterAccount = masterNameForJournal,
                    InstrumentToken = instrumentToken,
                    MasterFillQty = masterFillQty,
                    Ratio = ratio,
                    ProtectionPlan = protectionPlan,
                    EntrySignalName = BuildFollowerEntrySignal(protectionPlan, followerAccount, execIdToken),
                    Attempts = 1,
                    SubmittedOrder = submittedOrder
                };
            }

            // Retry only after the order object itself proves a terminal rejection.
            // A submit exception or unknown state is never permission to send a
            // second entry because the first order may still have reached NT.
            ProcessFollowerOrderUpdate(followerAccount, submittedOrder);
        }

        private static bool IsFollowerOwnedSignal(string signalName)
        {
            if (string.IsNullOrWhiteSpace(signalName))
                return false;

            string normalized = signalName.Trim();
            return normalized.Equals(CopySignalName, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(CopySignalName + "-", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals(CatchUpSignalName, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(CatchUpSignalName + "-", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFollowerProtectionSignal(string signalName)
        {
            if (string.IsNullOrWhiteSpace(signalName))
                return false;
            string normalized = signalName.Trim();
            return normalized.StartsWith(CopySignalName + "-S-", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(CopySignalName + "-T-", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProtectedFollowerEntrySignal(string signalName)
        {
            if (string.IsNullOrWhiteSpace(signalName))
                return false;
            string normalized = signalName.Trim();
            return normalized.StartsWith(CopySignalName + "-E-", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(CatchUpSignalName + "-E-", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAiProtectionSignal(string signalName)
        {
            if (string.IsNullOrWhiteSpace(signalName))
                return false;
            string normalized = signalName.Trim();
            return normalized.StartsWith(GlitchAiOrderExecutor.SignalStop, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(GlitchAiOrderExecutor.SignalTarget, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAiEntrySignal(string signalName)
        {
            if (string.IsNullOrWhiteSpace(signalName))
                return false;
            return signalName.Trim().StartsWith(
                GlitchAiOrderExecutor.SignalEntry + "-",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryBuildCatchUpProtectionPlan(
            Account masterAccount,
            Instrument instrument,
            bool isLong,
            out GlitchReplicationProtectionPlan plan)
        {
            plan = null;
            if (masterAccount == null || instrument == null || masterAccount.Orders == null
                || instrument.MasterInstrument == null)
                return false;
            string root = GlitchReplicationEngine.GetInstrumentRoot(instrument);
            Order[] masterOrders = SnapshotOrders(masterAccount);
            var pairs = new List<Tuple<Order, Order>>();
            foreach (Order order in masterOrders)
            {
                if (order == null || order.Instrument == null
                    || !GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                    || !string.Equals(GlitchReplicationEngine.GetInstrumentRoot(order.Instrument), root, StringComparison.OrdinalIgnoreCase))
                    continue;
                string name = order.Name ?? string.Empty;
                if (name.StartsWith(GlitchAiOrderExecutor.SignalStop + "-", StringComparison.OrdinalIgnoreCase))
                {
                    string targetName = GlitchAiOrderExecutor.SignalTarget
                        + name.Substring(GlitchAiOrderExecutor.SignalStop.Length);
                    Order target = masterOrders.FirstOrDefault(candidate => candidate != null
                        && string.Equals(candidate.Name, targetName, StringComparison.Ordinal)
                        && candidate.Instrument != null
                        && GlitchReplicationEngine.IsWorkingOrderState(candidate.OrderState)
                        && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(candidate.Instrument), root, StringComparison.OrdinalIgnoreCase));
                    if (target != null)
                        pairs.Add(Tuple.Create(order, target));
                }
            }
            bool aiNamedPairs = pairs.Count > 0;
            if (!aiNamedPairs)
            {
                OrderAction exitAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
                foreach (Order stop in masterOrders.Where(candidate => candidate != null
                    && candidate.Instrument != null
                    && candidate.OrderAction == exitAction
                    && (candidate.OrderType == OrderType.StopMarket || candidate.OrderType == OrderType.StopLimit)
                    && candidate.StopPrice > 0
                    && !string.IsNullOrWhiteSpace(candidate.Oco)
                    && GlitchReplicationEngine.IsWorkingOrderState(candidate.OrderState)
                    && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(candidate.Instrument), root, StringComparison.OrdinalIgnoreCase)))
                {
                    Order target = masterOrders.FirstOrDefault(candidate => candidate != null
                        && candidate.Instrument != null
                        && candidate.OrderAction == exitAction
                        && candidate.OrderType == OrderType.Limit
                        && candidate.LimitPrice > 0
                        && string.Equals(candidate.Oco, stop.Oco, StringComparison.Ordinal)
                        && GlitchReplicationEngine.IsWorkingOrderState(candidate.OrderState)
                        && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(candidate.Instrument), root, StringComparison.OrdinalIgnoreCase));
                    if (target != null)
                        pairs.Add(Tuple.Create(stop, target));
                }
            }
            int maxProtectionLegs = Math.Max(1, GlitchAiRailPolicyStore.Load().MaxContracts);
            if (pairs.Count == 0 || pairs.Count > maxProtectionLegs
                || pairs.Any(pair => pair.Item1.StopPrice <= 0 || pair.Item2.LimitPrice <= 0
                    || pair.Item1.Quantity <= 0 || pair.Item1.Quantity != pair.Item2.Quantity))
                return false;
            string correlation;
            if (aiNamedPairs)
            {
                var correlations = pairs.Select(pair =>
                {
                    string[] signal = (pair.Item1.Name ?? string.Empty).Split('-');
                    return signal.Length >= 4 ? signal[3] : "catchup";
                }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                correlation = correlations.Count == 1
                    ? correlations[0]
                    : "sync" + SanitizeIdentityToken(string.Join(string.Empty, pairs.Select(pair => pair.Item1.Oco)), 10);
            }
            else
            {
                correlation = "manual" + SanitizeIdentityToken(pairs[0].Item1.Oco, 10);
            }
            plan = new GlitchReplicationProtectionPlan
            {
                Correlation = correlation,
                IsLong = isLong,
                TickSize = instrument.MasterInstrument.TickSize,
                PointValue = instrument.MasterInstrument.PointValue,
                MaxRiskPerContractUsd = GlitchAiRailPolicyStore.Load().MaxRiskPerContractUsd,
                Legs = pairs.Select(pair => new GlitchReplicationProtectionLeg
                {
                    MasterQuantity = pair.Item1.Quantity,
                    UseAbsolutePrices = true,
                    AbsoluteStopPrice = pair.Item1.StopPrice,
                    AbsoluteTargetPrice = pair.Item2.LimitPrice
                }).ToList()
            };
            int masterQuantity = Math.Abs(GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(masterAccount, root));
            return plan.TickSize > 0 && plan.PointValue > 0
                && plan.Legs.Sum(leg => leg.MasterQuantity) == masterQuantity;
        }

        private static bool TryResolveMasterProtectionPlan(
            Account masterAccount,
            Instrument instrument,
            string entrySignalName,
            bool isLong,
            out GlitchReplicationProtectionPlan plan)
        {
            if (GlitchAiOrderExecutor.TryGetReplicationProtection(
                masterAccount,
                instrument,
                entrySignalName,
                out plan))
                return true;
            return TryBuildCatchUpProtectionPlan(masterAccount, instrument, isLong, out plan);
        }

        private static bool IsRejectedOrCancelled(OrderState state)
        {
            return state == OrderState.Rejected || state == OrderState.Cancelled;
        }

        private static string BuildExecutionDedupKey(string masterAccountName, GlitchCopyExecutionContext context)
        {
            if (!string.IsNullOrWhiteSpace(context.ExecutionId))
                return context.ExecutionId.Trim();

            string masterToken = string.IsNullOrWhiteSpace(masterAccountName) ? "Unknown" : masterAccountName.Trim();
            string instrumentToken = GlitchReplicationEngine.GetInstrumentRoot(context.Instrument);
            return masterToken + "|" + instrumentToken + "|" + context.Action + "|" + context.Quantity;
        }

        private static string BuildCopyRetryKey(
            string followerAccountName,
            Instrument instrument,
            OrderAction action,
            string executionId)
        {
            string followerToken = string.IsNullOrWhiteSpace(followerAccountName) ? "Unknown" : followerAccountName.Trim();
            string instrumentToken = instrument == null ? "Unknown" : GlitchReplicationEngine.GetInstrumentRoot(instrument);
            string executionToken = string.IsNullOrWhiteSpace(executionId) ? "Unknown" : executionId.Trim();
            return followerToken + "|" + instrumentToken + "|" + action + "|" + executionToken;
        }

        private bool TryRememberExecutionId(string executionId)
        {
            if (string.IsNullOrWhiteSpace(executionId))
                return true;

            string normalized = executionId.Trim();
            lock (_gate)
            {
                if (_seenExecutionIdSet.Contains(normalized))
                    return false;

                _seenExecutionIdSet.Add(normalized);
                _seenExecutionIds.AddLast(normalized);
                while (_seenExecutionIds.Count > 512)
                {
                    string oldest = _seenExecutionIds.First.Value;
                    _seenExecutionIds.RemoveFirst();
                    _seenExecutionIdSet.Remove(oldest);
                }
            }

            return true;
        }

        private static string TrySubmitGlitchMarketOrder(
            Account account,
            Instrument instrument,
            OrderAction action,
            int quantity,
            string signalName,
            out string failureReason,
            out Order submittedOrder)
        {
            failureReason = null;
            submittedOrder = null;
            if (account == null || instrument == null || quantity <= 0)
            {
                failureReason = "invalid_input";
                return "submit|result=failed";
            }

            if (string.IsNullOrWhiteSpace(signalName))
                signalName = CopySignalName;

            try
            {
                Order order = account.CreateOrder(
                    instrument,
                    action,
                    OrderType.Market,
                    OrderEntry.Automated,
                    TimeInForce.Day,
                    quantity,
                    0.0,
                    0.0,
                    string.Empty,
                    signalName,
                    DateTime.MaxValue,
                    null);
                if (order == null)
                {
                    failureReason = "create_order_null";
                    return "submit|result=failed";
                }

                account.Submit(new[] { order });
                submittedOrder = order;
                if (order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled)
                {
                    failureReason = order.OrderState.ToString();
                    return "submit|result=rejected";
                }

                return "accepted";
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return "submit|result=failed";
            }
        }

        private string TrySubmitProtectedFollowerEntry(
            Account account,
            Instrument instrument,
            OrderAction action,
            int quantity,
            string signalName,
            GlitchReplicationProtectionPlan plan,
            out string failureReason,
            out Order submittedOrder)
        {
            failureReason = null;
            submittedOrder = null;
            if (account == null || instrument == null || quantity <= 0 || string.IsNullOrWhiteSpace(signalName))
            {
                failureReason = "invalid_input";
                return "submit|result=failed";
            }

            int requestedDirection = action == OrderAction.Buy
                ? 1
                : action == OrderAction.SellShort
                    ? -1
                    : 0;
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            if (!GlitchApexDirectionGuard.TryApproveEntry(
                account,
                instrument,
                requestedDirection,
                policy.SnapshotMaxAgeSeconds,
                out string complianceFailure))
            {
                failureReason = complianceFailure;
                Journal?.Invoke(account.Name,
                    "entry_rejected|reason=apex_direction_compliance|instrument="
                    + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(instrument))
                    + "|detail=" + CleanToken(complianceFailure));
                RaiseCritical?.Invoke(
                    account.Name,
                    "Permission denied: Apex cross-direction compliance rule.",
                    "ApexCrossDirectionBlocked|" + GlitchReplicationEngine.GetInstrumentRoot(instrument));
                return "submit|result=compliance_rejected";
            }

            if (plan == null)
                return TrySubmitGlitchMarketOrder(
                    account,
                    instrument,
                    action,
                    quantity,
                    CopySignalName,
                    out failureReason,
                    out submittedOrder);

            Order order = null;
            try
            {
                order = account.CreateOrder(
                    instrument,
                    action,
                    OrderType.Market,
                    OrderEntry.Automated,
                    TimeInForce.Day,
                    quantity,
                    0.0,
                    0.0,
                    string.Empty,
                    signalName,
                    DateTime.MaxValue,
                    null);
                if (order == null)
                {
                    failureReason = "create_order_null";
                    return "submit|result=failed";
                }

                lock (_gate)
                    RegisterFollowerProtectionUnsafe(plan, account, instrument, order, signalName);

                submittedOrder = order;
                account.Submit(new[] { order });
                if (IsRejectedOrCancelled(order.OrderState))
                {
                    if (order.Filled > 0)
                        return "accepted";
                    lock (_gate)
                        RemoveFollowerProtectionTicketUnsafe(order, signalName);
                    failureReason = order.OrderState.ToString();
                    return "submit|result=rejected";
                }

                return "accepted";
            }
            catch (Exception ex)
            {
                submittedOrder = order;
                if (order != null && order.Filled > 0)
                {
                    failureReason = "submit_exception_after_fill_" + ex.GetType().Name;
                    return "accepted";
                }
                if (order != null && !IsTerminalOrderState(order.OrderState))
                {
                    failureReason = "submit_state_unknown_" + ex.GetType().Name;
                    return "submission_unknown";
                }
                lock (_gate)
                    RemoveFollowerProtectionTicketUnsafe(submittedOrder, signalName);
                failureReason = ex.Message;
                return "submit|result=failed";
            }
        }

        public void ProcessAccountStateUpdate(Account account)
        {
            if (account == null)
                return;

            TryReleasePendingMasterCopies(account);
            AuditPendingFollowerFlattens(account);
            AuditFollowerProtection(account);
        }

        public void AuditPendingFollowerFlattens()
        {
            AuditPendingFollowerFlattens(null);
        }

        private void AuditPendingFollowerFlattens(Account accountFilter)
        {
            List<KeyValuePair<string, PendingFollowerFlatten>> pending;
            lock (_gate)
            {
                pending = _pendingFollowerFlattens
                    .Where(item => item.Value?.Account != null
                        && (accountFilter == null
                            || string.Equals(item.Value.Account.Name, accountFilter.Name, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            DateTime nowUtc = DateTime.UtcNow;
            foreach (KeyValuePair<string, PendingFollowerFlatten> item in pending)
            {
                PendingFollowerFlatten request = item.Value;
                string root = GlitchReplicationEngine.GetInstrumentRoot(request.Instrument);
                int net = GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(request.Account, root);
                if (net == 0)
                {
                    lock (_gate)
                    {
                        _pendingFollowerFlattens.Remove(item.Key);
                        _flattenInFlight.Remove(item.Key);
                        RemoveFollowerProtectionForInstrumentUnsafe(request.Account, request.Instrument);
                    }
                    Journal?.Invoke(request.Account.Name,
                        "follower_flatten|result=confirmed|instrument=" + CleanToken(root)
                        + "|reason=" + CleanToken(request.Reason));
                    continue;
                }

                if (nowUtc - request.SubmittedUtc < FollowerFlattenConfirmationTimeout)
                    continue;

                lock (_gate)
                {
                    _pendingFollowerFlattens.Remove(item.Key);
                    _flattenInFlight.Remove(item.Key);
                }
                Journal?.Invoke(request.Account.Name,
                    "follower_flatten|result=unconfirmed_timeout|instrument=" + CleanToken(root)
                    + "|remaining_net=" + net.ToString(CultureInfo.InvariantCulture)
                    + "|reason=" + CleanToken(request.Reason));
                RaiseCritical?.Invoke(
                    request.Account.Name,
                    "Follower flatten was submitted but not confirmed; the provider may be disconnected or may have rejected the order.",
                    "FollowerFlattenUnconfirmed|" + root);
            }
        }

        public void ProcessMasterOrderUpdate(Account account, Order order)
        {
            if (account == null || order == null)
                return;
            TryReleasePendingMasterCopies(account);
        }

        private string TryFlattenFollowerInstrument(
            Account account,
            Instrument instrument,
            string reason,
            out string failureReason)
        {
            failureReason = null;
            if (account == null || instrument == null)
            {
                failureReason = "invalid_input";
                return "submit|result=failed";
            }

            string key = BuildAccountRootKey(account, instrument);
            lock (_gate)
            {
                if (_pendingFollowerFlattens.ContainsKey(key) || _flattenInFlight.Contains(key))
                    return "already_pending";

                DateTime nowUtc = DateTime.UtcNow;
                _flattenInFlight.Add(key);
                _pendingFollowerFlattens[key] = new PendingFollowerFlatten
                {
                    Account = account,
                    Instrument = instrument,
                    Reason = reason,
                    SubmittedUtc = nowUtc
                };
                foreach (FollowerEntryLifecycle lifecycle in _entryLifecyclesBySignal.Values.Where(item => item != null
                    && string.Equals(BuildAccountRootKey(item.Account, item.Instrument), key, StringComparison.OrdinalIgnoreCase)))
                {
                    TransitionLifecycleUnsafe(lifecycle, FollowerLifecycleState.Closing, reason);
                }
            }

            try
            {
                account.Flatten(new[] { instrument });
                return "submitted_pending_confirmation";
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _pendingFollowerFlattens.Remove(key);
                    _flattenInFlight.Remove(key);
                    foreach (FollowerEntryLifecycle lifecycle in _entryLifecyclesBySignal.Values.Where(item => item != null
                        && string.Equals(BuildAccountRootKey(item.Account, item.Instrument), key, StringComparison.OrdinalIgnoreCase)
                        && item.State == FollowerLifecycleState.Closing))
                    {
                        TransitionLifecycleUnsafe(lifecycle, FollowerLifecycleState.Failed, "flatten_submit_failed");
                    }
                }
                failureReason = ex.Message;
                return "submit|result=failed";
            }
        }

        private static bool IsSubmissionStarted(string result)
        {
            return string.Equals(result, "accepted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result, "submission_unknown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result, "submitted_pending_confirmation", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result, "already_pending", StringComparison.OrdinalIgnoreCase);
        }

        private void RegisterFollowerProtectionUnsafe(
            GlitchReplicationProtectionPlan plan,
            Account followerAccount,
            Instrument instrument,
            Order entryOrder,
            string entrySignalName = null)
        {
            if (plan == null || followerAccount == null || instrument == null || entryOrder == null)
                return;
            string signal = string.IsNullOrWhiteSpace(entrySignalName)
                ? entryOrder.Name?.Trim()
                : entrySignalName.Trim();
            _followerProtectionByKey[BuildFollowerProtectionKey(followerAccount, instrument, plan.Correlation)] =
                new FollowerProtectionTicket
                {
                    Account = followerAccount,
                    Instrument = instrument,
                    EntryOrder = entryOrder,
                    EntrySignalName = signal,
                    Plan = plan
                };
            if (!string.IsNullOrWhiteSpace(signal))
            {
                DateTime nowUtc = DateTime.UtcNow;
                _protectionPlansByEntrySignal[signal] = plan;
                _entryLifecyclesBySignal[signal] = new FollowerEntryLifecycle
                {
                    EntrySignalName = signal,
                    Account = followerAccount,
                    Instrument = instrument,
                    EntryOrder = entryOrder,
                    Plan = plan,
                    State = FollowerLifecycleState.EntryPending,
                    CreatedUtc = nowUtc,
                    TransitionUtc = nowUtc
                };
            }
        }

        private void RemoveFollowerProtectionTicketUnsafe(Order order, string signalName)
        {
            string signal = string.IsNullOrWhiteSpace(signalName) ? order?.Name?.Trim() : signalName.Trim();
            foreach (string key in _followerProtectionByKey
                .Where(item => item.Value != null && IsSameTrackedOrder(item.Value.EntryOrder, item.Value.EntrySignalName, order, signal))
                .Select(item => item.Key)
                .ToList())
                _followerProtectionByKey.Remove(key);
            if (!string.IsNullOrWhiteSpace(signal))
            {
                _protectionPlansByEntrySignal.Remove(signal);
                if (_entryLifecyclesBySignal.TryGetValue(signal, out FollowerEntryLifecycle lifecycle)
                    && lifecycle.State == FollowerLifecycleState.EntryPending)
                    _entryLifecyclesBySignal.Remove(signal);
            }
        }

        private bool SubmitFollowerProtection(
            FollowerProtectionTicket ticket,
            int filledQuantity,
            double averageFillPrice,
            out string failureReason)
        {
            failureReason = null;
            if (ticket == null || ticket.Plan == null || ticket.Account == null || ticket.Instrument == null
                || filledQuantity <= 0 || averageFillPrice <= 0)
            {
                failureReason = "invalid_input";
                return false;
            }

            GlitchReplicationProtectionPlan plan = ticket.Plan;
            int maxProtectionLegs = Math.Max(1, GlitchAiRailPolicyStore.Load().MaxContracts);
            if (plan.Legs == null || plan.Legs.Count == 0 || plan.Legs.Count > maxProtectionLegs)
            {
                failureReason = "invalid_plan";
                return false;
            }

            int masterQuantity = plan.Legs.Sum(leg => leg.MasterQuantity);
            if (masterQuantity <= 0)
            {
                failureReason = "invalid_quantity_split";
                return false;
            }
            double ratio = (double)filledQuantity / masterQuantity;
            string accountToken = SanitizeIdentityToken(ticket.Account.Name, 12);
            string submissionNonce = Guid.NewGuid().ToString("N").Substring(0, 6);
            string suffix = plan.Correlation + accountToken + submissionNonce;
            OrderAction exitAction = plan.IsLong ? OrderAction.Sell : OrderAction.BuyToCover;
            var orders = new List<Order>();
            var activeProtections = new List<ActiveFollowerProtection>();
            var evidence = new List<string>();
            for (int legIndex = 0; legIndex < plan.Legs.Count; legIndex++)
            {
                GlitchReplicationProtectionLeg leg = plan.Legs[legIndex];
                double scaledLegQuantity = leg.MasterQuantity * ratio;
                int legQuantity = (int)Math.Round(scaledLegQuantity, MidpointRounding.AwayFromZero);
                double stopPrice = RoundToTick(
                    leg.UseAbsolutePrices
                        ? leg.AbsoluteStopPrice
                        : (plan.IsLong ? averageFillPrice - leg.StopDistance : averageFillPrice + leg.StopDistance),
                    plan.TickSize);
                double targetPrice = RoundToTick(
                    leg.UseAbsolutePrices
                        ? leg.AbsoluteTargetPrice
                        : (plan.IsLong ? averageFillPrice + leg.TargetDistance : averageFillPrice - leg.TargetDistance),
                    plan.TickSize);
                double stopDistance = plan.IsLong
                    ? averageFillPrice - stopPrice
                    : stopPrice - averageFillPrice;
                double targetDistance = plan.IsLong
                    ? targetPrice - averageFillPrice
                    : averageFillPrice - targetPrice;
                double riskPerContract = stopDistance * plan.PointValue;
                if (legQuantity <= 0 || Math.Abs(scaledLegQuantity - legQuantity) > 0.0000001d
                    || stopDistance < plan.TickSize || targetDistance < plan.TickSize
                    || riskPerContract <= 0 || riskPerContract > plan.MaxRiskPerContractUsd)
                {
                    failureReason = "invalid_bracket_risk";
                    return false;
                }

                string legToken = (legIndex + 1).ToString(CultureInfo.InvariantCulture);
                string legSuffix = suffix + legToken;
                string oco = "GLTCP" + legSuffix;
                Order stop = ticket.Account.CreateOrder(
                    ticket.Instrument, exitAction, OrderType.StopMarket, OrderEntry.Automated, TimeInForce.Gtc,
                    legQuantity, 0.0, stopPrice, oco, CopySignalName + "-S-" + legSuffix, DateTime.MaxValue, null);
                Order target = ticket.Account.CreateOrder(
                    ticket.Instrument, exitAction, OrderType.Limit, OrderEntry.Automated, TimeInForce.Gtc,
                    legQuantity, targetPrice, 0.0, oco, CopySignalName + "-T-" + legSuffix, DateTime.MaxValue, null);
                if (stop == null || target == null)
                {
                    failureReason = "bracket_create_failed";
                    return false;
                }
                orders.Add(stop);
                orders.Add(target);
                activeProtections.Add(new ActiveFollowerProtection
                {
                    Account = ticket.Account,
                    Instrument = ticket.Instrument,
                    Plan = plan,
                    EntrySignalName = ticket.EntrySignalName,
                    LegIndex = legIndex,
                    Stop = stop,
                    Target = target
                });
                evidence.Add("leg" + legToken + "_qty=" + legQuantity.ToString(CultureInfo.InvariantCulture));
                evidence.Add("sl" + legToken + "=" + stopPrice.ToString(CultureInfo.InvariantCulture));
                evidence.Add("tp" + legToken + "=" + targetPrice.ToString(CultureInfo.InvariantCulture));
            }

            try
            {
                ticket.Account.Submit(orders.ToArray());
                if (orders.Any(order => IsRejectedOrCancelled(order.OrderState)))
                    throw new InvalidOperationException("follower bracket rejected");
                lock (_gate)
                {
                    foreach (ActiveFollowerProtection active in activeProtections)
                        _activeFollowerProtectionByKey[BuildFollowerProtectionKey(
                            ticket.Account, ticket.Instrument, plan.Correlation, active.LegIndex)] = active;
                }
                Journal?.Invoke(
                    ticket.Account.Name,
                        "follower_protection|instrument=" + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(ticket.Instrument))
                        + "|qty=" + filledQuantity.ToString(CultureInfo.InvariantCulture)
                        + "|legs=" + plan.Legs.Count.ToString(CultureInfo.InvariantCulture)
                        + "|result=accepted");
                GlitchAiExecutionJournalWriter.TryAppend(
                    plan.IntentId,
                    GlitchAiExecutionResult.Succeeded(
                        "follower_structural_brackets_submitted",
                        "group=" + CleanToken(plan.GroupId)
                            + "|correlation=" + CleanToken(plan.Correlation)
                            + "|account=" + CleanToken(ticket.Account.Name)
                            + "|instrument=" + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(ticket.Instrument))
                            + "|contract=" + CleanToken(ticket.Instrument.FullName)
                            + "|quantity=" + filledQuantity.ToString(CultureInfo.InvariantCulture)
                            + "|fill=" + averageFillPrice.ToString(CultureInfo.InvariantCulture)
                            + "|" + string.Join("|", evidence)),
                    DateTime.UtcNow);
                foreach (Order order in orders.Where(order => order.OrderState == OrderState.Filled))
                    RecordFollowerProtectionClose(ticket.Account, order);
                return true;
            }
            catch (Exception ex)
            {
                failureReason = "bracket_submit_failed_" + ex.GetType().Name;
                return false;
            }
        }

        private void HandleFollowerProtectionOrderUpdate(Account account, Order order)
        {
            ActiveFollowerProtection active = null;
            string activeKey = null;
            FollowerEntryLifecycle matchedLifecycle = null;
            lock (_gate)
            {
                KeyValuePair<string, ActiveFollowerProtection> match = _activeFollowerProtectionByKey.FirstOrDefault(item => item.Value != null
                    && (IsSameTrackedOrder(item.Value.Stop, item.Value.Stop?.Name, order)
                        || IsSameTrackedOrder(item.Value.Target, item.Value.Target?.Name, order)));
                activeKey = match.Key;
                active = match.Value;
                if (active != null && !string.IsNullOrWhiteSpace(active.EntrySignalName)
                    && _entryLifecyclesBySignal.TryGetValue(active.EntrySignalName, out FollowerEntryLifecycle lifecycle))
                {
                    matchedLifecycle = lifecycle;
                    if (order.OrderState == OrderState.Filled || order.OrderState == OrderState.PartFilled)
                    {
                        TransitionLifecycleUnsafe(lifecycle, FollowerLifecycleState.OcoTransitioning);
                        lifecycle.ReconciliationHandoffPending = true;
                    }
                    else if (order.OrderState == OrderState.Rejected)
                    {
                        TransitionLifecycleUnsafe(lifecycle, FollowerLifecycleState.Failed, "protection_rejected");
                    }
                }
                if (active != null && active.Stop != null && active.Target != null
                    && IsTerminalOrderState(active.Stop.OrderState)
                    && IsTerminalOrderState(active.Target.OrderState)
                    && !string.IsNullOrWhiteSpace(activeKey))
                {
                    _activeFollowerProtectionByKey.Remove(activeKey);
                }
            }

            if (order.OrderState == OrderState.PartFilled)
            {
                if (!TryResizeFollowerOcoAfterPartialFill(account, order, active, out string resizeFailure))
                {
                    lock (_gate)
                    {
                        if (matchedLifecycle != null)
                        {
                            matchedLifecycle.ReconciliationHandoffPending = false;
                            TransitionLifecycleUnsafe(
                                matchedLifecycle,
                                FollowerLifecycleState.Failed,
                                "partial_fill_resize_failed");
                        }
                    }
                    Journal?.Invoke(account.Name,
                        "follower_partial_fill|instrument="
                        + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(order.Instrument))
                        + "|filled=" + order.Filled.ToString(CultureInfo.InvariantCulture)
                        + "|remaining=" + RemainingQuantity(order).ToString(CultureInfo.InvariantCulture)
                        + "|result=resize_failed|failure=" + CleanToken(resizeFailure));
                    RaiseCritical?.Invoke(
                        account.Name,
                        "Follower OCO protection could not be resized after a partial fill.",
                        "FollowerPartialFillResizeFailed|" + GlitchReplicationEngine.GetInstrumentRoot(order.Instrument));
                }
                return;
            }

            if (order.OrderState == OrderState.Filled)
                RecordFollowerProtectionClose(account, order);

            ReconcileFollowerRoot(account, order.Instrument,
                order.OrderState == OrderState.Rejected ? "protection_rejected" : "protection_update");
        }

        private bool TryResizeFollowerOcoAfterPartialFill(
            Account account,
            Order partialOrder,
            ActiveFollowerProtection active,
            out string failureReason)
        {
            failureReason = null;
            if (account == null || partialOrder?.Instrument == null)
            {
                failureReason = "invalid_input";
                return false;
            }

            int remaining = RemainingQuantity(partialOrder);
            if (remaining <= 0)
            {
                failureReason = "no_remaining_quantity";
                return false;
            }

            Order sibling = null;
            if (active != null)
            {
                sibling = IsSameTrackedOrder(active.Stop, active.Stop?.Name, partialOrder)
                    ? active.Target
                    : active.Stop;
            }
            if (sibling == null && !string.IsNullOrWhiteSpace(partialOrder.Oco))
            {
                sibling = SnapshotOrders(account).FirstOrDefault(candidate => candidate != null
                    && candidate.Instrument != null
                    && !IsSameTrackedOrder(candidate, candidate.Name, partialOrder)
                    && IsFollowerProtectionSignal(candidate.Name)
                    && string.Equals(candidate.Oco, partialOrder.Oco, StringComparison.Ordinal)
                    && string.Equals(
                        GlitchReplicationEngine.GetInstrumentRoot(candidate.Instrument),
                        GlitchReplicationEngine.GetInstrumentRoot(partialOrder.Instrument),
                        StringComparison.OrdinalIgnoreCase));
            }
            if (sibling == null)
            {
                failureReason = "oco_sibling_missing";
                return false;
            }
            if (!GlitchReplicationEngine.IsWorkingOrderState(sibling.OrderState))
            {
                failureReason = "oco_sibling_not_working_" + sibling.OrderState;
                return false;
            }

            int siblingRemaining = RemainingQuantity(sibling);
            if (siblingRemaining == remaining)
            {
                Journal?.Invoke(account.Name,
                    "follower_partial_fill|instrument="
                    + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(partialOrder.Instrument))
                    + "|filled=" + partialOrder.Filled.ToString(CultureInfo.InvariantCulture)
                    + "|remaining=" + remaining.ToString(CultureInfo.InvariantCulture)
                    + "|result=already_balanced");
                return true;
            }
            if (siblingRemaining < remaining)
            {
                failureReason = "oco_sibling_under_covered";
                return false;
            }

            try
            {
                sibling.QuantityChanged = sibling.Filled + remaining;
                account.Change(new[] { sibling });
                if (sibling.OrderState == OrderState.Rejected || sibling.OrderState == OrderState.Cancelled)
                {
                    failureReason = "oco_resize_" + sibling.OrderState;
                    return false;
                }
                Journal?.Invoke(account.Name,
                    "follower_partial_fill|instrument="
                    + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(partialOrder.Instrument))
                    + "|filled=" + partialOrder.Filled.ToString(CultureInfo.InvariantCulture)
                    + "|remaining=" + remaining.ToString(CultureInfo.InvariantCulture)
                    + "|sibling_from=" + siblingRemaining.ToString(CultureInfo.InvariantCulture)
                    + "|sibling_to=" + remaining.ToString(CultureInfo.InvariantCulture)
                    + "|result=resize_submitted");
                return true;
            }
            catch (Exception ex)
            {
                failureReason = "oco_resize_exception_" + ex.GetType().Name;
                return false;
            }
        }

        private void RecordFollowerProtectionClose(Account account, Order filledOrder)
        {
            ActiveFollowerProtection active;
            string protectionKey;
            lock (_gate)
            {
                protectionKey = _activeFollowerProtectionByKey
                    .Where(item => item.Value != null
                        && (ReferenceEquals(item.Value.Stop, filledOrder) || ReferenceEquals(item.Value.Target, filledOrder)))
                    .Select(item => item.Key)
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(protectionKey)
                    || !_activeFollowerProtectionByKey.TryGetValue(protectionKey, out active)
                    || active == null)
                    return;
            }
            AppendFollowerClose(active.Plan, account, filledOrder, "native_bracket");
        }

        private static void AppendFollowerClose(
            GlitchReplicationProtectionPlan plan,
            Account account,
            Order filledOrder,
            string closeOwner)
        {
            if (plan == null || account == null || filledOrder == null)
                return;
            GlitchAiExecutionJournalWriter.TryAppend(
                plan.IntentId,
                GlitchAiExecutionResult.Succeeded(
                    "follower_trade_closed",
                    "group=" + CleanToken(plan.GroupId)
                        + "|correlation=" + CleanToken(plan.Correlation)
                        + "|account=" + CleanToken(account.Name)
                        + "|instrument=" + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(filledOrder.Instrument))
                        + "|contract=" + CleanToken(filledOrder.Instrument.FullName)
                        + "|quantity=" + filledOrder.Filled.ToString(CultureInfo.InvariantCulture)
                        + "|fill=" + filledOrder.AverageFillPrice.ToString(CultureInfo.InvariantCulture)
                        + "|close_owner=" + CleanToken(closeOwner)),
                DateTime.UtcNow);
        }

        private static double RoundToTick(double price, double tickSize)
        {
            return tickSize <= 0 ? price : Math.Round(price / tickSize, MidpointRounding.AwayFromZero) * tickSize;
        }

        private static Order[] SnapshotOrders(Account account)
        {
            if (account?.Orders == null)
                return Array.Empty<Order>();
            try
            {
                lock (account.Orders)
                    return account.Orders.ToArray();
            }
            catch
            {
                try { return account.Orders.ToArray(); } catch { return Array.Empty<Order>(); }
            }
        }

        private static Position[] SnapshotPositions(Account account)
        {
            if (account?.Positions == null)
                return Array.Empty<Position>();
            try
            {
                lock (account.Positions)
                    return account.Positions.ToArray();
            }
            catch
            {
                try { return account.Positions.ToArray(); } catch { return Array.Empty<Position>(); }
            }
        }

        private static string BuildAccountRootKey(Account account, Instrument instrument)
        {
            return (account?.Name?.Trim() ?? string.Empty) + "|" + GlitchReplicationEngine.GetInstrumentRoot(instrument);
        }

        private static string SanitizeIdentityToken(string value, int maxLength)
        {
            string token = new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrWhiteSpace(token))
                token = "unknown";
            return token.Length <= maxLength ? token : token.Substring(token.Length - maxLength, maxLength);
        }

        private static bool IsTerminalOrderState(OrderState state)
        {
            return state == OrderState.Filled || state == OrderState.Cancelled || state == OrderState.Rejected;
        }

        private static string BuildFollowerProtectionKey(Account account, Instrument instrument, string correlation)
        {
            return (account?.Name?.Trim() ?? string.Empty)
                + "|" + GlitchReplicationEngine.GetInstrumentRoot(instrument)
                + "|" + (string.IsNullOrWhiteSpace(correlation) ? "catchup" : correlation.Trim());
        }

        private static string BuildFollowerProtectionKey(
            Account account,
            Instrument instrument,
            string correlation,
            int legIndex)
        {
            return BuildFollowerProtectionKey(account, instrument, correlation)
                + "|" + legIndex.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildFollowerEntrySignal(
            GlitchReplicationProtectionPlan plan,
            Account account,
            string executionToken)
        {
            bool isCatchUp = string.Equals(executionToken, "catchup", StringComparison.OrdinalIgnoreCase)
                || (executionToken ?? string.Empty).StartsWith("catchup-", StringComparison.OrdinalIgnoreCase);
            string prefix = isCatchUp
                ? CatchUpSignalName
                : CopySignalName;
            string correlation = string.IsNullOrWhiteSpace(plan?.Correlation) ? "unknown" : plan.Correlation.Trim();
            string accountToken = SanitizeIdentityToken(account?.Name, 12);
            string executionIdentity = isCatchUp && string.Equals(executionToken, "catchup", StringComparison.OrdinalIgnoreCase)
                ? Guid.NewGuid().ToString("N").Substring(0, 8)
                : SanitizeIdentityToken(executionToken, 10);
            return prefix + "-E-" + correlation + "-" + accountToken + "-" + executionIdentity;
        }

        private static bool IsSameTrackedOrder(
            Order tracked,
            string trackedSignal,
            Order candidate,
            string candidateSignal = null)
        {
            if (tracked != null && candidate != null && ReferenceEquals(tracked, candidate))
                return true;
            string left = string.IsNullOrWhiteSpace(trackedSignal) ? tracked?.Name : trackedSignal;
            string right = string.IsNullOrWhiteSpace(candidateSignal) ? candidate?.Name : candidateSignal;
            return !string.IsNullOrWhiteSpace(left)
                && !string.IsNullOrWhiteSpace(right)
                && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildPendingMasterCopyKey(Account account, GlitchCopyExecutionContext context)
        {
            return (account?.Name?.Trim() ?? string.Empty)
                + "|" + (context?.ExecutionId?.Trim() ?? string.Empty)
                + "|" + (context?.OrderSignalName?.Trim() ?? string.Empty);
        }

        private static GlitchCopyExecutionContext CloneExecutionContext(GlitchCopyExecutionContext context)
        {
            return context == null
                ? null
                : new GlitchCopyExecutionContext
                {
                    ExecutionId = context.ExecutionId,
                    Instrument = context.Instrument,
                    Action = context.Action,
                    Quantity = context.Quantity,
                    OrderSignalName = context.OrderSignalName
                };
        }

        private void TryReleasePendingMasterCopies(Account account)
        {
            List<KeyValuePair<string, PendingMasterCopy>> pending;
            List<GlitchCopyFollowerRoute> routes;
            lock (_gate)
            {
                if (!_enabled)
                    return;
                pending = _pendingMasterCopies
                    .Where(item => item.Value != null && item.Value.MasterAccount != null
                        && string.Equals(item.Value.MasterAccount.Name, account.Name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                _routesByMaster.TryGetValue(account.Name?.Trim() ?? string.Empty, out routes);
                routes = routes == null ? new List<GlitchCopyFollowerRoute>() : routes.ToList();
            }

            foreach (KeyValuePair<string, PendingMasterCopy> pair in pending)
            {
                PendingMasterCopy item = pair.Value;
                if (DateTime.UtcNow - item.CreatedUtc > PendingMasterCopyTtl)
                {
                    lock (_gate) _pendingMasterCopies.Remove(pair.Key);
                    Journal?.Invoke(account.Name, "copy_drop|reason=master_bracket_timeout");
                    continue;
                }
                bool isLong = item.Context.Action == OrderAction.Buy;
                if (!TryResolveMasterProtectionPlan(
                    item.MasterAccount,
                    item.Context.Instrument,
                    item.Context.OrderSignalName,
                    isLong,
                    out GlitchReplicationProtectionPlan resolvedPlan))
                    continue;

                lock (_gate) _pendingMasterCopies.Remove(pair.Key);
                foreach (GlitchCopyFollowerRoute route in routes.Where(route => route?.FollowerAccount != null))
                    AlignFollowerToMaster(item.MasterAccount, route.FollowerAccount, route.Ratio, "pending_master_bracket", resolvedPlan);
            }
        }

        private void AuditFollowerProtection(Account account)
        {
            if (account == null)
                return;

            // Account state notifications are delivered for masters and followers.
            // This reconciler owns follower GLT-COPY/GLT-CATCHUP protection only;
            // auditing a master would misclassify its valid GLT-AI brackets and
            // flatten the master immediately after entry.
            lock (_gate)
            {
                bool configuredFollower = _enabled && _routesByMaster.Values
                    .SelectMany(routes => routes ?? new List<GlitchCopyFollowerRoute>())
                    .Any(route => route?.FollowerAccount != null
                        && string.Equals(route.FollowerAccount.Name, account.Name, StringComparison.OrdinalIgnoreCase));
                if (!configuredFollower)
                    return;
            }

            try
            {
                var instrumentsByRoot = new Dictionary<string, Instrument>(StringComparer.OrdinalIgnoreCase);
                foreach (Position position in SnapshotPositions(account))
                {
                    if (position?.Instrument == null)
                        continue;
                    instrumentsByRoot[GlitchReplicationEngine.GetInstrumentRoot(position.Instrument)] = position.Instrument;
                }
                foreach (Order order in SnapshotOrders(account))
                {
                    if (order?.Instrument == null || !IsFollowerOwnedSignal(order.Name))
                        continue;
                    instrumentsByRoot[GlitchReplicationEngine.GetInstrumentRoot(order.Instrument)] = order.Instrument;
                }
                lock (_gate)
                {
                    foreach (FollowerEntryLifecycle lifecycle in _entryLifecyclesBySignal.Values.Where(item => item != null
                        && string.Equals(item.Account?.Name, account.Name, StringComparison.OrdinalIgnoreCase)
                        && item.Instrument != null))
                        instrumentsByRoot[GlitchReplicationEngine.GetInstrumentRoot(lifecycle.Instrument)] = lifecycle.Instrument;
                }
                foreach (Instrument instrument in instrumentsByRoot.Values)
                    ReconcileFollowerRoot(account, instrument, "account_state");
            }
            catch (Exception ex)
            {
                RaiseCritical?.Invoke(account.Name, "Follower reconciliation failed: " + ex.GetType().Name,
                    "FollowerReconcileException");
            }
        }

        private void ReconcileFollowerRoot(Account account, Instrument instrument, string origin)
        {
            if (account == null || instrument == null)
                return;
            string root = GlitchReplicationEngine.GetInstrumentRoot(instrument);
            string key = BuildAccountRootKey(account, instrument);
            int net = GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(account, root);
            Order[] orders = SnapshotOrders(account);
            List<Order> workingProtection = orders.Where(order => order != null && order.Instrument != null
                && IsFollowerProtectionSignal(order.Name)
                && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(order.Instrument), root, StringComparison.OrdinalIgnoreCase)).ToList();
            List<FollowerEntryLifecycle> lifecycles;
            lock (_gate)
            {
                lifecycles = _entryLifecyclesBySignal.Values.Where(item => item != null
                    && string.Equals(item.Account?.Name, account.Name, StringComparison.OrdinalIgnoreCase)
                    && item.Instrument != null
                    && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(item.Instrument), root, StringComparison.OrdinalIgnoreCase)).ToList();

                DateTime nowUtc = DateTime.UtcNow;
                foreach (FollowerEntryLifecycle item in lifecycles.Where(item => IsTransitionState(item.State)
                    && nowUtc - item.TransitionUtc >= FollowerLifecycleTransitionTimeout))
                {
                    string expiredState = item.State.ToString();
                    TransitionLifecycleUnsafe(item, FollowerLifecycleState.Failed, "transition_timeout_" + expiredState);
                }
            }

            if (net == 0)
            {
                lock (_gate)
                {
                    _flattenInFlight.Remove(key);
                }
                DateTime nowUtc = DateTime.UtcNow;
                bool freshProtectionVisibility = lifecycles.Any(item =>
                    (item.State == FollowerLifecycleState.ProtectionSubmitting
                        || item.State == FollowerLifecycleState.Protected)
                    && nowUtc - item.TransitionUtc < FollowerProtectionVisibilityGrace);
                if (workingProtection.Count > 0 && freshProtectionVisibility)
                    return;

                if (workingProtection.Count > 0
                    && !lifecycles.Any(item => item.State == FollowerLifecycleState.EntryPending
                        || item.State == FollowerLifecycleState.ProtectionSubmitting))
                {
                    try { account.Cancel(workingProtection.ToArray()); } catch { }
                    lock (_gate)
                    {
                        foreach (FollowerEntryLifecycle item in lifecycles)
                            TransitionLifecycleUnsafe(item, FollowerLifecycleState.Closing);
                    }
                    Journal?.Invoke(account.Name, "orphan_protection_cancel|instrument=" + CleanToken(root)
                        + "|orders=" + workingProtection.Count.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    lock (_gate)
                    {
                        foreach (string signal in lifecycles.Where(item => item.State != FollowerLifecycleState.EntryPending
                            && item.State != FollowerLifecycleState.ProtectionSubmitting)
                            .Select(item => item.EntrySignalName).Where(item => !string.IsNullOrWhiteSpace(item)).ToList())
                            _entryLifecyclesBySignal.Remove(signal);
                    }
                }
                return;
            }

            if (HasCompleteFollowerProtection(account, instrument))
            {
                lock (_gate)
                {
                    _flattenInFlight.Remove(key);
                    foreach (FollowerEntryLifecycle item in lifecycles.Where(item => item.State != FollowerLifecycleState.EntryPending
                        && item.State != FollowerLifecycleState.ProtectionSubmitting
                        && item.State != FollowerLifecycleState.Protected))
                        TransitionLifecycleUnsafe(item, FollowerLifecycleState.Protected);
                }
                return;
            }

            bool knownTransition;
            lock (_gate)
            {
                bool entryOrCloseTransition = lifecycles.Any(item => item.State == FollowerLifecycleState.EntryPending
                    || item.State == FollowerLifecycleState.ProtectionSubmitting
                    || item.State == FollowerLifecycleState.Closing);
                bool activeOcoTransition = lifecycles.Any(item => item.State == FollowerLifecycleState.OcoTransitioning
                    && _activeFollowerProtectionByKey.Values.Any(active => active != null
                        && string.Equals(active.EntrySignalName, item.EntrySignalName, StringComparison.OrdinalIgnoreCase)));
                FollowerEntryLifecycle handoff = lifecycles.FirstOrDefault(item => item.State == FollowerLifecycleState.OcoTransitioning
                    && item.ReconciliationHandoffPending);
                if (handoff != null)
                    handoff.ReconciliationHandoffPending = false;
                knownTransition = entryOrCloseTransition || activeOcoTransition || handoff != null;
            }
            knownTransition |= HasFollowerOrderTransition(orders, root);
            if (knownTransition)
                return;

            RequestFollowerFlattenOnce(account, instrument, origin + "_coverage_mismatch");
        }

        private static bool HasFollowerOrderTransition(IEnumerable<Order> orders, string instrumentRoot)
        {
            var owned = (orders ?? Enumerable.Empty<Order>())
                .Where(order => order?.Instrument != null
                    && IsFollowerProtectionSignal(order.Name)
                    && string.Equals(
                        GlitchReplicationEngine.GetInstrumentRoot(order.Instrument),
                        instrumentRoot,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (owned.Any(order => order.OrderState == OrderState.CancelPending
                || order.OrderState == OrderState.CancelSubmitted
                || order.OrderState == OrderState.ChangePending
                || order.OrderState == OrderState.ChangeSubmitted))
                return true;

            // On restart there is no in-memory lifecycle. A filled OCO leg whose
            // sibling is still working is the order book's durable transition
            // marker; defer only that self-evident native OCO handoff.
            return owned.Any(working => GlitchReplicationEngine.IsWorkingOrderState(working.OrderState)
                && !string.IsNullOrWhiteSpace(working.Oco)
                && owned.Any(peer => !ReferenceEquals(peer, working)
                    && string.Equals(peer.Oco, working.Oco, StringComparison.Ordinal)
                    && peer.OrderState == OrderState.Filled));
        }

        private void RequestFollowerFlattenOnce(Account account, Instrument instrument, string reason)
        {
            string result = TryFlattenFollowerInstrument(account, instrument, reason, out string failureReason);
            if (string.Equals(result, "already_pending", StringComparison.OrdinalIgnoreCase))
                return;
            if (!IsSubmissionStarted(result))
            {
                RaiseCritical?.Invoke(account.Name,
                    "Follower flatten could not be submitted: " + (failureReason ?? result),
                    "FollowerFlattenSubmitFailed|" + GlitchReplicationEngine.GetInstrumentRoot(instrument));
                return;
            }
            RaiseCritical?.Invoke(account.Name,
                "Follower position could not prove complete native protection; flatten requested.",
                "FollowerProtectionCoverage|" + GlitchReplicationEngine.GetInstrumentRoot(instrument));
        }

        private static bool HasCompleteFollowerProtection(Account account, Instrument instrument)
        {
            if (account == null || instrument == null || account.Orders == null)
                return false;
            string root = GlitchReplicationEngine.GetInstrumentRoot(instrument);
            int net = GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(account, root);
            if (net == 0)
                return true;
            OrderAction expectedExitAction = net > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
            List<Order> protection = SnapshotOrders(account).Where(order => order != null
                && order.Instrument != null
                && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                && IsFollowerProtectionSignal(order.Name)
                && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(order.Instrument), root, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (protection.Count == 0)
                return false;

            int stopCoverage = 0;
            int targetCoverage = 0;
            foreach (IGrouping<string, Order> pair in protection.GroupBy(order => order.Oco ?? string.Empty, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    return false;
                List<Order> stops = pair.Where(order => (order.Name ?? string.Empty).StartsWith(
                    CopySignalName + "-S-", StringComparison.OrdinalIgnoreCase)).ToList();
                List<Order> targets = pair.Where(order => (order.Name ?? string.Empty).StartsWith(
                    CopySignalName + "-T-", StringComparison.OrdinalIgnoreCase)).ToList();
                if (stops.Count != 1 || targets.Count != 1
                    || stops[0].OrderAction != expectedExitAction || targets[0].OrderAction != expectedExitAction
                    || stops[0].OrderType != OrderType.StopMarket || targets[0].OrderType != OrderType.Limit)
                    return false;
                int stopRemaining = RemainingQuantity(stops[0]);
                int targetRemaining = RemainingQuantity(targets[0]);
                if (stopRemaining <= 0 || stopRemaining != targetRemaining)
                    return false;
                stopCoverage += stopRemaining;
                targetCoverage += targetRemaining;
            }
            return stopCoverage == Math.Abs(net) && targetCoverage == Math.Abs(net);
        }

        private static int RemainingQuantity(Order order)
        {
            return order == null ? 0 : Math.Max(0, order.Quantity - order.Filled);
        }

        private static bool IsTransitionState(FollowerLifecycleState state)
        {
            return state == FollowerLifecycleState.EntryPending
                || state == FollowerLifecycleState.ProtectionSubmitting
                || state == FollowerLifecycleState.OcoTransitioning
                || state == FollowerLifecycleState.Closing;
        }

        private static void TransitionLifecycleUnsafe(
            FollowerEntryLifecycle lifecycle,
            FollowerLifecycleState state,
            string failureReason = null)
        {
            if (lifecycle == null)
                return;
            lifecycle.State = state;
            lifecycle.TransitionUtc = DateTime.UtcNow;
            if (failureReason != null)
                lifecycle.FailureReason = failureReason;
        }

        private static bool TryGetPositionFill(Account account, Instrument instrument, out int quantity, out double averagePrice)
        {
            quantity = 0;
            averagePrice = 0;
            if (account == null || instrument == null)
                return false;
            string root = GlitchReplicationEngine.GetInstrumentRoot(instrument);
            Position position = SnapshotPositions(account).FirstOrDefault(item => item != null && item.Instrument != null
                && item.MarketPosition != MarketPosition.Flat && item.Quantity > 0
                && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(item.Instrument), root, StringComparison.OrdinalIgnoreCase));
            if (position == null)
                return false;
            quantity = position.Quantity;
            averagePrice = position.AveragePrice;
            return quantity > 0 && averagePrice > 0;
        }

        private bool TryFindFollowerProtectionTicketUnsafe(
            Order entryOrder,
            out string key,
            out FollowerProtectionTicket ticket)
        {
            foreach (KeyValuePair<string, FollowerProtectionTicket> item in _followerProtectionByKey)
            {
                if (item.Value != null && IsSameTrackedOrder(item.Value.EntryOrder, item.Value.EntrySignalName, entryOrder))
                {
                    key = item.Key;
                    ticket = item.Value;
                    return true;
                }
            }
            key = null;
            ticket = null;
            return false;
        }

        private bool TryFindCopyRetryTicketUnsafe(Order order, out string key, out CopyRetryTicket ticket)
        {
            foreach (KeyValuePair<string, CopyRetryTicket> item in _pendingCopyRetries)
            {
                if (item.Value != null && IsSameTrackedOrder(item.Value.SubmittedOrder, item.Value.EntrySignalName, order))
                {
                    key = item.Key;
                    ticket = item.Value;
                    return true;
                }
            }
            key = null;
            ticket = null;
            return false;
        }

        private void RemoveFollowerProtectionForInstrumentUnsafe(Account account, Instrument instrument)
        {
            string prefix = (account?.Name?.Trim() ?? string.Empty)
                + "|" + GlitchReplicationEngine.GetInstrumentRoot(instrument) + "|";
            foreach (string key in _followerProtectionByKey.Keys.Where(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                _followerProtectionByKey.Remove(key);
            foreach (string key in _activeFollowerProtectionByKey.Keys.Where(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                _activeFollowerProtectionByKey.Remove(key);
        }

        private void JournalCopy(
            string journalAccount,
            string executionId,
            string masterAccount,
            string followerAccount,
            string instrument,
            int masterFillQty,
            OrderAction masterAction,
            double ratio,
            int followerQty,
            string resultToken)
        {
            if (Journal == null)
                return;

            string ratioText = ratio.ToString("0.####", CultureInfo.InvariantCulture);
            string message =
                $"copy|execId={CleanToken(executionId)}|master={CleanToken(masterAccount)}|follower={CleanToken(followerAccount)}|instrument={CleanToken(instrument)}|masterFillQty={masterFillQty}|masterAction={masterAction}|ratio={ratioText}|qty={followerQty}|result={CleanToken(resultToken)}";
            Journal(journalAccount, message);
        }

        private static string CleanToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            return value.Trim().Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        private sealed class CopyRetryTicket
        {
            public Account FollowerAccount { get; set; }
            public Instrument Instrument { get; set; }
            public OrderAction Action { get; set; }
            public int Quantity { get; set; }
            public string ExecutionId { get; set; }
            public string MasterAccount { get; set; }
            public string InstrumentToken { get; set; }
            public int MasterFillQty { get; set; }
            public double Ratio { get; set; }
            public GlitchReplicationProtectionPlan ProtectionPlan { get; set; }
            public string EntrySignalName { get; set; }
            public int Attempts { get; set; }
            public Order SubmittedOrder { get; set; }
        }

        private sealed class FollowerProtectionTicket
        {
            public Account Account { get; set; }
            public Instrument Instrument { get; set; }
            public Order EntryOrder { get; set; }
            public string EntrySignalName { get; set; }
            public GlitchReplicationProtectionPlan Plan { get; set; }
        }

        private sealed class PendingMasterCopy
        {
            public Account MasterAccount { get; set; }
            public GlitchCopyExecutionContext Context { get; set; }
            public DateTime CreatedUtc { get; set; }
        }

        private enum FollowerLifecycleState
        {
            EntryPending,
            ProtectionSubmitting,
            Protected,
            OcoTransitioning,
            Closing,
            Failed
        }

        private sealed class FollowerEntryLifecycle
        {
            public string EntrySignalName { get; set; }
            public Account Account { get; set; }
            public Instrument Instrument { get; set; }
            public Order EntryOrder { get; set; }
            public GlitchReplicationProtectionPlan Plan { get; set; }
            public FollowerLifecycleState State { get; set; }
            public bool ReconciliationHandoffPending { get; set; }
            public string FailureReason { get; set; }
            public DateTime CreatedUtc { get; set; }
            public DateTime TransitionUtc { get; set; }
        }

        private sealed class ActiveFollowerProtection
        {
            public Account Account { get; set; }
            public Instrument Instrument { get; set; }
            public GlitchReplicationProtectionPlan Plan { get; set; }
            public string EntrySignalName { get; set; }
            public int LegIndex { get; set; }
            public Order Stop { get; set; }
            public Order Target { get; set; }
        }

        private sealed class PendingFollowerFlatten
        {
            public Account Account { get; set; }
            public Instrument Instrument { get; set; }
            public string Reason { get; set; }
            public DateTime SubmittedUtc { get; set; }
        }

    }
}
