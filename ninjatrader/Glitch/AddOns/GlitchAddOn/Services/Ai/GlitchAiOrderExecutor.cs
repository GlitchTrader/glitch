using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NinjaTrader.Cbi;

namespace Glitch.Services
{
    internal static class GlitchAiOrderExecutor
    {
        public const string SignalEntry = "GLT-AI-E";
        public const string SignalStop = "GLT-AI-S";
        public const string SignalTarget = "GLT-AI-T";
        public const string SignalExit = "GLT-AI-X";
        private static readonly TimeSpan ExecutionPriceMaxAge = TimeSpan.FromSeconds(5);

        private static readonly object GroupSync = new object();
        private static readonly object PendingAmendmentSync = new object();
        private static readonly Dictionary<string, string> PendingAmendmentBodiesByIntentId =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ExecutionGroupContext> GroupsBySignal =
            new Dictionary<string, ExecutionGroupContext>(StringComparer.OrdinalIgnoreCase);

        public static Func<Func<GlitchAiExecutionResult>, GlitchAiExecutionResult> UiInvoke;
        public static Action<string, string, string> RaiseCritical;

        public static GlitchAiExecutionResult TryExecuteApprovedIntent(string rawJson, DateTime nowUtc)
        {
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            if (policy == null || !policy.IsValid)
            {
                return GlitchAiExecutionResult.Failed(
                    "policy_invalid",
                    policy?.ValidationError ?? "policy_unavailable");
            }
            if (!IsExecutionEnabled(policy))
            {
                return GlitchAiExecutionResult.Skipped(
                    "trading_off",
                    "AI Auto is off");
            }

            string action = GlitchAiJsonFields.ExtractString(rawJson, "action");
            if (IsNoOpAction(action))
                return GlitchAiExecutionResult.Skipped("no_op_action");

            if (UiInvoke == null)
                return GlitchAiExecutionResult.Failed("ui_dispatcher_missing");

            try
            {
                return UiInvoke(() => ExecuteOnUiThread(rawJson, policy, action));
            }
            catch (Exception ex)
            {
                return GlitchAiExecutionResult.Failed("executor_exception", ex.Message);
            }
        }

        public static void ProcessOrderUpdate(Account account, Order order)
        {
            if (account == null || order == null || string.IsNullOrWhiteSpace(order.Name))
                return;

            ExecutionGroupContext group;
            lock (GroupSync)
            {
                GroupsBySignal.TryGetValue(order.Name.Trim(), out group);
                if (group == null)
                {
                    List<ExecutionGroupContext> recovering = GroupsBySignal.Values
                        .Distinct()
                        .Where(item => item != null
                            && item.RecoveryStarted
                            && item.Accounts.Contains(account)
                            && SameInstrument(item.Instrument, order.Instrument))
                        .ToList();
                    foreach (ExecutionGroupContext item in recovering)
                        TryCompleteGroup(item);
                    TryFinalizePendingAmendments(account, order);
                    return;
                }
            }

            if (order.OrderState == OrderState.Rejected)
            {
                RecoverGroup(group, "order_update_" + order.OrderState + "_" + account.Name);
                return;
            }

            if (group.RecoveryStarted)
            {
                int recoveryAccountIndex;
                if (TryGetEntryAccountIndex(group, order, out recoveryAccountIndex)
                    && order.Filled > 0)
                    TryRecoverLateFill(group, recoveryAccountIndex, "order_update_" + order.OrderState + "_" + account.Name);
                TryCompleteGroup(group);
                return;
            }

            int entryAccountIndex;
            if (TryGetEntryAccountIndex(group, order, out entryAccountIndex))
                ReconcileEntryProtection(group, entryAccountIndex, order, "order_update");

            TryCompleteGroup(group);
            TryFinalizePendingAmendments(account, order);
        }

        public static GlitchAiExecutionResult TryReconcileStartedIntent(string rawJson, DateTime nowUtc)
        {
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            if (policy == null || !policy.IsValid)
                return GlitchAiExecutionResult.Failed("reconcile_policy_invalid", policy?.ValidationError);
            if (UiInvoke == null)
                return GlitchAiExecutionResult.Failed("reconcile_ui_dispatcher_missing");
            try
            {
                return UiInvoke(() => ReconcileStartedIntentOnUiThread(rawJson, policy));
            }
            catch (Exception ex)
            {
                return GlitchAiExecutionResult.Failed("reconcile_exception", ex.Message);
            }
        }

        private static GlitchAiExecutionResult ReconcileStartedIntentOnUiThread(string rawJson, GlitchAiRailPolicy policy)
        {
            string accountName = GlitchAiJsonFields.ExtractString(rawJson, "account");
            string operatorProfile = GlitchAiJsonFields.ExtractString(rawJson, "operator_profile");
            if (!policy.TryResolveProfileAccount(operatorProfile, out string boundAccount)
                || !string.Equals(accountName, boundAccount, StringComparison.OrdinalIgnoreCase))
                return GlitchAiExecutionResult.Failed("reconcile_account_binding_invalid");
            if (!TryResolveExecutionGroup(policy, boundAccount, out List<ExecutionGroupMember> members, out _, out string groupFailure))
                return GlitchAiExecutionResult.Failed("reconcile_execution_group_invalid", groupFailure);

            string instrumentRoot = GlitchAiJsonFields.ExtractString(rawJson, "instrument");
            string snapshotHash = GlitchAiJsonFields.ExtractString(rawJson, "snapshot_hash");
            if (!GlitchInstrumentMetadataService.TryResolveTradeInstrument(instrumentRoot, out Instrument instrument)
                && GlitchAiSnapshotRegistry.TryGetInstrumentFullName(snapshotHash, instrumentRoot, out string snapshotFullName))
                GlitchInstrumentMetadataService.RegisterTradeInstrument(snapshotFullName);
            if (!GlitchInstrumentMetadataService.TryResolveTradeInstrument(instrumentRoot, out instrument))
                return GlitchAiExecutionResult.Failed("reconcile_instrument_unresolved", instrumentRoot);

            Account master = members[0].Account;
            string action = GlitchAiJsonFields.ExtractString(rawJson, "action");
            if (string.Equals(action, "EXIT", StringComparison.Ordinal))
                return TryReconcileOwnedExit(master, instrument, GlitchAiJsonFields.ExtractString(rawJson, "intent_id"), 0);

            if (string.Equals(action, "ENTER_LONG", StringComparison.Ordinal)
                || string.Equals(action, "ENTER_SHORT", StringComparison.Ordinal))
            {
                string correlation = BuildIntentCorrelation(GlitchAiJsonFields.ExtractString(rawJson, "intent_id"));
                string intentId = GlitchAiJsonFields.ExtractString(rawJson, "intent_id");
                if (!TryFindNamedOrders(
                    master,
                    BuildSignalName(SignalEntry, correlation, 0),
                    instrument,
                    out List<Order> matchingEntries))
                    return GlitchAiExecutionResult.Pending("reconcile_entry_visibility_unavailable");
                if (matchingEntries.Count == 0)
                {
                    // Take a second native snapshot before declaring a crash-before-
                    // submit signal absent. This is bounded observation only; it does
                    // not submit, cancel, or use elapsed time as authority.
                    if (!IsNativeOrderVisibilityReady(master))
                        return GlitchAiExecutionResult.Pending("reconcile_entry_visibility_unavailable", "account_connection_not_connected");
                    if (!TryFindNamedOrders(
                        master,
                        BuildSignalName(SignalEntry, correlation, 0),
                        instrument,
                        out List<Order> confirmation))
                        return GlitchAiExecutionResult.Pending("reconcile_entry_visibility_unavailable");
                    if (confirmation.Count != 0)
                        return GlitchAiExecutionResult.Pending("reconcile_entry_visibility_changing");
                    return GlitchAiExecutionResult.Pending("reconcile_entry_not_found", "native_entry_absent");
                }
                if (matchingEntries.Count != 1)
                    return GlitchAiExecutionResult.Failed("reconcile_entry_native_identity_ambiguous");
                if (!GlitchAiEntryBaselinePlanStore.TryLoad(intentId, 0, out GlitchAiEntryBaselinePlan entryPlan))
                    return GlitchAiExecutionResult.Failed("reconcile_entry_baseline_plan_unavailable");
                Order entry = matchingEntries[0];
                if (!TryGetNetPosition(master, instrument, out int currentNet))
                    return GlitchAiExecutionResult.Failed("reconcile_entry_position_unknown");
                int entryDirection = entry.OrderAction == OrderAction.Buy ? 1
                    : entry.OrderAction == OrderAction.SellShort ? -1 : 0;
                if (entryDirection == 0 || !EntryPlanMatchesNativeOrder(entryPlan, master, instrument, entry, entryDirection))
                    return GlitchAiExecutionResult.Failed("reconcile_entry_baseline_plan_identity_ambiguous");
                if (entry.Filled <= 0
                    && (entry.OrderState == OrderState.Rejected || entry.OrderState == OrderState.Cancelled))
                    return GlitchAiExecutionResult.Failed("reconciled_entry_terminal_" + entry.OrderState + "_zero_fill");
                if (entry.Filled <= 0 || !IsTerminalTrackedOrder(entry))
                    return GlitchAiExecutionResult.Pending("reconcile_entry_pending", entry.OrderState.ToString());
                int expectedNet = entryPlan.BaselineNet + (entryDirection * entry.Filled);
                if (currentNet != expectedNet
                    || !HasExactBaselineProtection(master, instrument, entryPlan))
                    return GlitchAiExecutionResult.Failed("reconcile_entry_superseded_manual_or_concurrent_intent");
                if (HasExactCorrelationOwnedProtection(master, instrument, correlation, entry.Filled, entryDirection))
                    return GlitchAiExecutionResult.Succeeded("reconciled_entry_native_protected");
                return TryRecoverReconciledEntryProtection(rawJson, master, instrument, entry, correlation);
            }

            if (!HasCompleteAiProtection(master, instrument, out List<Order> stops, out List<Order> targets))
                return GlitchAiExecutionResult.Failed("reconcile_protection_incomplete");
            if (!TryParseProtectionAmendments(
                rawJson,
                string.Equals(action, "MOVE_TP", StringComparison.Ordinal),
                out List<ProtectionAmendment> amendments,
                out string parseFailure))
                return GlitchAiExecutionResult.Failed("reconcile_updates_invalid", parseFailure);

            if (string.Equals(action, "MOVE_STOP", StringComparison.Ordinal))
            {
                if (!IsIntentV3(rawJson))
                {
                    if (stops.All(stop => PricesEqual(stop.StopPrice, amendments[0].StopPrice.Value)))
                        return GlitchAiExecutionResult.Succeeded("reconciled_move_stop_native_state");
                    if (stops.Any(stop => GlitchReplicationEngine.IsWorkingOrderState(stop.OrderState)))
                        return GlitchAiExecutionResult.Pending("reconcile_move_stop_amendment_pending");
                    return GlitchAiExecutionResult.Failed("reconcile_move_stop_outcome_ambiguous");
                }
                if (!TryIndexProtectionOrders(stops, SignalStop, out Dictionary<string, Order> stopsByLeg, out string stopFailure))
                    return GlitchAiExecutionResult.Failed("reconcile_move_stop_leg_state_invalid", stopFailure);
                bool stopsMatch = amendments.All(amendment => stopsByLeg.TryGetValue(amendment.LegId, out Order stop)
                    && PricesEqual(stop.StopPrice, amendment.StopPrice.Value));
                if (stopsMatch)
                    return GlitchAiExecutionResult.Succeeded("reconciled_move_stop_native_state");
                if (stops.Any(stop => GlitchReplicationEngine.IsWorkingOrderState(stop.OrderState)))
                    return GlitchAiExecutionResult.Pending("reconcile_move_stop_amendment_pending");
                return GlitchAiExecutionResult.Failed("reconcile_move_stop_outcome_ambiguous");
            }

            if (string.Equals(action, "MOVE_TP", StringComparison.Ordinal))
            {
                if (!IsIntentV3(rawJson))
                {
                    bool targetsMatch = targets.Count == 1
                        && PricesEqual(targets[0].LimitPrice, amendments[0].TargetPrice.Value);
                    if (amendments[0].StopPrice.HasValue)
                    {
                        Order matchingStop = stops.SingleOrDefault(stop => string.Equals(stop.Oco, targets[0].Oco, StringComparison.Ordinal));
                        targetsMatch = targetsMatch
                            && matchingStop != null
                            && PricesEqual(matchingStop.StopPrice, amendments[0].StopPrice.Value);
                    }
                    if (targetsMatch)
                        return GlitchAiExecutionResult.Succeeded("reconciled_move_tp_native_state");
                    if (targets.Any(target => GlitchReplicationEngine.IsWorkingOrderState(target.OrderState))
                        || stops.Any(stop => GlitchReplicationEngine.IsWorkingOrderState(stop.OrderState)))
                        return GlitchAiExecutionResult.Pending("reconcile_move_tp_amendment_pending");
                    return GlitchAiExecutionResult.Failed("reconcile_move_tp_outcome_ambiguous");
                }
                if (!TryIndexProtectionOrders(targets, SignalTarget, out Dictionary<string, Order> targetsByLeg, out string targetFailure))
                    return GlitchAiExecutionResult.Failed("reconcile_move_tp_leg_state_invalid", targetFailure);
                if (!TryIndexProtectionOrders(stops, SignalStop, out Dictionary<string, Order> stopsByTargetLeg, out string stopsFailure))
                    return GlitchAiExecutionResult.Failed("reconcile_move_tp_leg_state_invalid", stopsFailure);
                bool targetsMatch = amendments.All(amendment => targetsByLeg.TryGetValue(amendment.LegId, out Order target)
                    && PricesEqual(target.LimitPrice, amendment.TargetPrice.Value)
                    && (!amendment.StopPrice.HasValue
                        || (stopsByTargetLeg.TryGetValue(amendment.LegId, out Order stop)
                            && PricesEqual(stop.StopPrice, amendment.StopPrice.Value))));
                if (targetsMatch)
                    return GlitchAiExecutionResult.Succeeded("reconciled_move_tp_native_state");
                if (targets.Any(target => GlitchReplicationEngine.IsWorkingOrderState(target.OrderState))
                    || stops.Any(stop => GlitchReplicationEngine.IsWorkingOrderState(stop.OrderState)))
                    return GlitchAiExecutionResult.Pending("reconcile_move_tp_amendment_pending");
                return GlitchAiExecutionResult.Failed("reconcile_move_tp_outcome_ambiguous");
            }

            return GlitchAiExecutionResult.Failed("reconcile_action_unsupported", action);
        }

        private static GlitchAiExecutionResult TryRecoverReconciledEntryProtection(
            string rawJson,
            Account account,
            Instrument instrument,
            Order entry,
            string correlation)
        {
            string intentId = GlitchAiJsonFields.ExtractString(rawJson, "intent_id");
            if (!GlitchAiEntryBaselinePlanStore.TryLoad(intentId, 0, out GlitchAiEntryBaselinePlan baselinePlan))
                return GlitchAiExecutionResult.Failed("reconcile_entry_baseline_plan_unavailable");
            if (HasOwnedCorrelationProtection(account, instrument, correlation))
            {
                if (!IsNativeOrderVisibilityReady(account))
                    return GlitchAiExecutionResult.Pending("reconcile_entry_visibility_unavailable", "account_connection_not_connected");
                if (!TryCancelActiveOwnedProtection(account, instrument, new HashSet<string>(new[] { correlation }, StringComparer.OrdinalIgnoreCase)))
                    return GlitchAiExecutionResult.Pending("reconcile_entry_protection_cancel_request_failed");
                if (!ArePlannedProtectionOrdersTerminalOrAbsent(account, instrument, new HashSet<string>(new[] { correlation }, StringComparer.OrdinalIgnoreCase)))
                    return GlitchAiExecutionResult.Pending("reconcile_entry_protection_cancel_pending");
            }
            return TryCloseReconciledEntryDelta(account, instrument, entry, correlation, baselinePlan);
        }

        private static GlitchAiExecutionResult TryCloseReconciledEntryDelta(
            Account account, Instrument instrument, Order entry, string correlation, GlitchAiEntryBaselinePlan plan)
        {
            if (!IsNativeOrderVisibilityReady(account))
                return GlitchAiExecutionResult.Pending("reconcile_entry_visibility_unavailable", "account_connection_not_connected");
            string closeSignal = BuildSignalName(SignalExit, correlation, 0);
            if (!TryFindNamedOrders(account, closeSignal, instrument, out List<Order> closes))
                return GlitchAiExecutionResult.Pending("reconcile_entry_visibility_unavailable");
            if (closes.Count > 1)
                return GlitchAiExecutionResult.Failed("reconcile_entry_recovery_close_identity_ambiguous");
            if (closes.Count == 1)
            {
                Order close = closes[0];
                if (!TryGetNetPosition(account, instrument, out int net))
                    return GlitchAiExecutionResult.Failed("reconcile_entry_position_unknown");
                if (close.OrderState == OrderState.Filled)
                {
                    GlitchAiExecutionResult terminal = close.Quantity == entry.Filled
                        && net == plan.BaselineNet && HasExactBaselineProtection(account, instrument, plan)
                        && ArePlannedProtectionOrdersTerminalOrAbsent(account, instrument,
                            new HashSet<string>(new[] { correlation }, StringComparer.OrdinalIgnoreCase))
                        ? GlitchAiExecutionResult.Failed("reconciled_entry_protection_failed_delta_closed")
                        : GlitchAiExecutionResult.Failed("reconcile_entry_superseded_manual_or_concurrent_intent");
                    GlitchAiIntentStateStore.TryFinalizeNonterminal(plan.IntentId, terminal, out _);
                    return terminal;
                }
                if (close.OrderState == OrderState.Rejected || close.OrderState == OrderState.Cancelled)
                {
                    GlitchAiExecutionResult terminal = GlitchAiExecutionResult.Failed("reconcile_entry_recovery_close_terminal_" + close.OrderState);
                    GlitchAiIntentStateStore.TryFinalizeNonterminal(plan.IntentId, terminal, out _);
                    return terminal;
                }
                return GlitchAiExecutionResult.Pending("reconcile_entry_recovery_close_pending", close.OrderState.ToString());
            }
            if (!TryGetNetPosition(account, instrument, out int expectedNet)
                || expectedNet != plan.BaselineNet + (plan.EntryDirection * entry.Filled)
                || !HasExactBaselineProtection(account, instrument, plan))
                return GlitchAiExecutionResult.Failed("reconcile_entry_superseded_manual_or_concurrent_intent");
            if (!string.IsNullOrWhiteSpace(plan.RecoveryCloseSignal))
            {
                if (!string.Equals(plan.RecoveryCloseSignal, closeSignal, StringComparison.OrdinalIgnoreCase)
                    || plan.RecoveryCloseQuantity != entry.Filled)
                    return GlitchAiExecutionResult.Failed("reconcile_entry_recovery_close_identity_ambiguous");
                if (!TryFindNamedOrders(account, closeSignal, instrument, out List<Order> confirmation))
                    return GlitchAiExecutionResult.Pending("reconcile_entry_visibility_unavailable");
                if (confirmation.Count != 0)
                    return confirmation.Count == 1
                        ? GlitchAiExecutionResult.Pending("reconcile_entry_recovery_close_visibility_changing")
                        : GlitchAiExecutionResult.Failed("reconcile_entry_recovery_close_identity_ambiguous");
                return GlitchAiExecutionResult.Pending("reconcile_entry_recovery_close_visibility_unresolved");
            }
            else if (!GlitchAiEntryBaselinePlanStore.TryBeginRecoveryClose(
                plan.IntentId, plan.AccountIndex, closeSignal, entry.Filled, DateTime.UtcNow, out plan))
                return GlitchAiExecutionResult.Pending("reconcile_entry_recovery_close_plan_pending");
            OrderAction action = plan.EntryDirection > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
            try
            {
                Order close = CreateExitOrder(account, instrument, action, OrderType.Market, entry.Filled, 0, 0, string.Empty, closeSignal);
                if (close == null)
                    return GlitchAiExecutionResult.Failed("reconcile_entry_recovery_close_create_failed");
                account.Submit(new[] { close });
                return GlitchAiExecutionResult.Pending("reconcile_entry_recovery_close_submitted");
            }
            catch (Exception ex)
            {
                return GlitchAiExecutionResult.Pending("reconcile_entry_recovery_close_ambiguous", ex.GetType().Name);
            }
        }

        private static bool HasOwnedCorrelationProtection(Account account, Instrument instrument, string correlation)
        {
            if (!TrySnapshotOrders(account, out Order[] orders))
                return true;
            string stopPrefix = BuildSignalName(SignalStop, correlation, 0);
            string targetPrefix = BuildSignalName(SignalTarget, correlation, 0);
            return orders.Any(order => order != null && SameInstrument(order.Instrument, instrument)
                && (string.Equals(order.Name, stopPrefix, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(order.Name, targetPrefix, StringComparison.OrdinalIgnoreCase)
                    || order.Name.StartsWith(stopPrefix + "-", StringComparison.OrdinalIgnoreCase)
                    || order.Name.StartsWith(targetPrefix + "-", StringComparison.OrdinalIgnoreCase)));
        }

        public static void ProcessAccountStateUpdate(Account account)
        {
            if (account == null)
                return;

            ReconcileAiOwnedProtectionFromPosition(account);

            List<ExecutionGroupContext> groups;
            lock (GroupSync)
            {
                groups = GroupsBySignal.Values
                    .Distinct()
                    .Where(item => item != null
                        && FindGroupAccountIndex(item, account) >= 0)
                    .ToList();
            }

            foreach (ExecutionGroupContext group in groups)
            {
                int accountIndex = FindGroupAccountIndex(group, account);
                if (accountIndex < 0)
                    continue;

                if (group.RecoveryStarted)
                {
                    TryRecoverLateFill(group, accountIndex, "account_state_update_" + account.Name);
                    TryCompleteGroup(group);
                    continue;
                }

                Order entry = FindNamedOrder(
                    account,
                    BuildSignalName(SignalEntry, group.Correlation, accountIndex),
                    group.Instrument);
                if (entry == null)
                {
                    int offset = accountIndex * GetOrderStride(group);
                    if (offset >= 0 && offset < group.Orders.Count)
                        entry = group.Orders[offset];
                }

                if (entry != null)
                    ReconcileEntryProtection(group, accountIndex, entry, "account_state_update");

                TryCompleteGroup(group);
            }

            TryFinalizePendingAmendments(account, null);
        }

        private static void TrackPendingAmendment(string intentId, string rawJson)
        {
            if (string.IsNullOrWhiteSpace(intentId) || string.IsNullOrWhiteSpace(rawJson))
                return;
            lock (PendingAmendmentSync)
                PendingAmendmentBodiesByIntentId[intentId.Trim()] = rawJson;
        }

        private static void TryFinalizePendingAmendments(Account account, Order order)
        {
            if (account == null)
                return;
            if (order != null
                && !IsOwnedStopSignal(order.Name)
                && !IsOwnedTargetSignal(order.Name))
                return;

            List<string> intentIds;
            lock (PendingAmendmentSync)
                intentIds = PendingAmendmentBodiesByIntentId.Keys.ToList();
            if (intentIds.Count == 0)
                return;

            foreach (string intentId in intentIds)
            {
                string rawJson;
                lock (PendingAmendmentSync)
                {
                    if (!PendingAmendmentBodiesByIntentId.TryGetValue(intentId, out rawJson))
                        continue;
                }

                string action = GlitchAiJsonFields.ExtractString(rawJson, "action");
                if (!string.Equals(action, "MOVE_STOP", StringComparison.Ordinal)
                    && !string.Equals(action, "MOVE_TP", StringComparison.Ordinal))
                    continue;

                string accountName = GlitchAiJsonFields.ExtractString(rawJson, "account");
                if (!string.Equals(accountName, account.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                GlitchAiExecutionResult reconciled = TryReconcileStartedIntent(rawJson, DateTime.UtcNow);
                if (string.Equals(reconciled.Status, "pending", StringComparison.Ordinal))
                    continue;

                bool terminalMove = string.Equals(reconciled.Status, "executed", StringComparison.Ordinal)
                    && reconciled.Code != null
                    && reconciled.Code.StartsWith("reconciled_move_", StringComparison.Ordinal);
                if (!terminalMove && !string.Equals(reconciled.Status, "failed", StringComparison.Ordinal))
                    continue;

                GlitchAiExecutionJournalWriter.TryAppend(intentId, reconciled, DateTime.UtcNow);
                GlitchAiIntentStateStore.TryFinalizeNonterminal(intentId, reconciled, out _);
                lock (PendingAmendmentSync)
                    PendingAmendmentBodiesByIntentId.Remove(intentId);
            }
        }

        private static void ReconcileAiOwnedProtectionFromPosition(Account account)
        {
            if (account == null || !TrySnapshotOrders(account, out Order[] orders))
                return;

            HashSet<string> instrumentFullNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Order order in orders)
            {
                if (order?.Instrument == null
                    || string.IsNullOrWhiteSpace(order.Instrument.FullName)
                    || (!IsOwnedStopSignal(order.Name) && !IsOwnedTargetSignal(order.Name)))
                    continue;
                instrumentFullNames.Add(order.Instrument.FullName);
            }

            foreach (string instrumentFullName in instrumentFullNames)
            {
                Instrument instrument = orders
                    .FirstOrDefault(order => order?.Instrument != null
                        && string.Equals(order.Instrument.FullName, instrumentFullName, StringComparison.OrdinalIgnoreCase))
                    ?.Instrument;
                if (instrument == null
                    || !TryGetNetPosition(account, instrument, out int netQuantity))
                    continue;

                if (netQuantity == 0)
                    CancelAiProtection(account, instrument, orders);
                else
                    ResizeAiProtection(account, instrument, orders, Math.Abs(netQuantity));
            }
        }

        private static void CancelAiProtection(Account account, Instrument instrument, Order[] orders)
        {
            List<Order> cancellations = orders
                .Where(order => order?.Instrument != null
                    && SameInstrument(order.Instrument, instrument)
                    && GlitchReplicationEngine.CanCancelOrder(order)
                    && (IsOwnedStopSignal(order.Name) || IsOwnedTargetSignal(order.Name)))
                .ToList();
            if (cancellations.Count == 0)
                return;
            try
            {
                account.Cancel(cancellations.ToArray());
            }
            catch (Exception ex)
            {
                RaiseCritical?.Invoke(
                    account.Name,
                    "AI protection could not be cancelled after the native position became flat: " + ex.GetType().Name,
                    "AiProtectionFlatCancelFailed|" + instrument.FullName);
            }
        }

        private static void ResizeAiProtection(
            Account account,
            Instrument instrument,
            Order[] orders,
            int requiredUnits)
        {
            var protectionOrders = orders
                .Where(order => order?.Instrument != null
                    && SameInstrument(order.Instrument, instrument)
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                    && !string.IsNullOrWhiteSpace(order.Oco)
                    && (IsOwnedStopSignal(order.Name) || IsOwnedTargetSignal(order.Name)))
                .ToList();
            if (protectionOrders.Count == 0)
                return;

            var units = protectionOrders
                .GroupBy(order => order.Oco.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    Orders = group.ToList(),
                    Quantity = group.Max(order => Math.Max(0, order.Quantity - order.Filled))
                })
                .Where(unit => unit.Quantity > 0)
                .OrderBy(unit => unit.Orders.Count)
                .ThenByDescending(unit => unit.Orders[0].Oco, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int excess = units.Sum(unit => unit.Quantity) - requiredUnits;
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
                        int currentRemaining = Math.Max(0, order.Quantity - order.Filled);
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
                }
                catch (Exception ex)
                {
                    RaiseCritical?.Invoke(
                        account.Name,
                        "AI protection could not be resized to native position truth: " + ex.GetType().Name,
                        "AiProtectionResizeFailed|" + instrument.FullName);
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
                    RaiseCritical?.Invoke(
                        account.Name,
                        "Excess AI protection could not be cancelled: " + ex.GetType().Name,
                        "AiProtectionTrimCancelFailed|" + instrument.FullName);
                }
            }
        }

        public static bool IsExecutionEnabled(GlitchAiRailPolicy policy)
        {
            return policy != null
                && policy.IsValid
                && !GlitchHermesControlStateStore.Load().TradingPaused;
        }

        private static bool IsNoOpAction(string action)
        {
            return string.Equals(action, "NOTHING", StringComparison.Ordinal)
                || string.Equals(action, "HOLD", StringComparison.Ordinal);
        }

        private static GlitchAiExecutionResult ExecuteOnUiThread(string rawJson, GlitchAiRailPolicy policy, string action)
        {
            string accountName = GlitchAiJsonFields.ExtractString(rawJson, "account");
            string operatorProfile = GlitchAiJsonFields.ExtractString(rawJson, "operator_profile");
            string boundAccount;
            if (!policy.TryResolveProfileAccount(operatorProfile, out boundAccount))
                return GlitchAiExecutionResult.Failed("operator_profile_not_bound", operatorProfile);
            if (!string.Equals(accountName, boundAccount, StringComparison.OrdinalIgnoreCase))
                return GlitchAiExecutionResult.Failed("profile_account_mismatch", boundAccount);

            List<ExecutionGroupMember> members;
            string groupId;
            string groupFailure;
            if (!TryResolveExecutionGroup(policy, boundAccount, out members, out groupId, out groupFailure))
                return GlitchAiExecutionResult.Failed("execution_group_invalid", groupFailure);

            List<Account> accounts = new List<Account> { members[0].Account };

            string instrumentRoot = GlitchAiJsonFields.ExtractString(rawJson, "instrument");
            string snapshotHash = GlitchAiJsonFields.ExtractString(rawJson, "snapshot_hash");
            Instrument instrument;
            if (!GlitchInstrumentMetadataService.TryResolveTradeInstrument(instrumentRoot, out instrument)
                && GlitchAiSnapshotRegistry.TryGetInstrumentFullName(snapshotHash, instrumentRoot, out string snapshotInstrumentFullName))
            {
                GlitchInstrumentMetadataService.RegisterTradeInstrument(snapshotInstrumentFullName);
            }
            if (!GlitchInstrumentMetadataService.TryResolveTradeInstrument(instrumentRoot, out instrument))
                return GlitchAiExecutionResult.Failed("instrument_not_resolved", instrumentRoot);

            string intentId = GlitchAiJsonFields.ExtractString(rawJson, "intent_id");
            if (string.Equals(action, "EXIT", StringComparison.Ordinal))
                return TryExecuteGroupExit(accounts, instrument, groupId, intentId);

            if (string.Equals(action, "MOVE_STOP", StringComparison.Ordinal))
                return TryExecuteGroupMoveStop(members, instrument, rawJson, groupId, intentId);

            if (string.Equals(action, "MOVE_TP", StringComparison.Ordinal))
                return TryExecuteGroupMoveTarget(members, instrument, rawJson, groupId, intentId);

            if (string.Equals(action, "ENTER_LONG", StringComparison.Ordinal))
                return TryExecuteGroupEnter(members, instrument, rawJson, true, groupId, intentId);

            if (string.Equals(action, "ENTER_SHORT", StringComparison.Ordinal))
                return TryExecuteGroupEnter(members, instrument, rawJson, false, groupId, intentId);

            return GlitchAiExecutionResult.Skipped("unsupported_action", action);
        }

        private static bool TryResolveExecutionGroup(
            GlitchAiRailPolicy policy,
            string masterAccount,
            out List<ExecutionGroupMember> members,
            out string groupId,
            out string failure)
        {
            members = new List<ExecutionGroupMember>();
            groupId = null;
            failure = null;

            if (policy == null || string.IsNullOrWhiteSpace(masterAccount))
            {
                failure = "executor_account_missing";
                return false;
            }

            string groupPath = GlitchStateStore.GetDefaultPath("AccountGroups.tsv");
            List<GlitchStateStore.AccountGroupRecord> matching = GlitchStateStore.LoadAccountGroups(groupPath)
                .Where(group => group != null
                    && string.Equals(group.MasterAccount, masterAccount, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matching.Count != 1)
            {
                failure = matching.Count == 0 ? "executor_group_missing" : "executor_group_ambiguous";
                return false;
            }

            GlitchStateStore.AccountGroupRecord selected = matching[0];
            string name = selected.MasterAccount.Trim();
            if (!policy.AccountAllowlist.Contains(name))
            {
                failure = "unallowlisted_master_" + name;
                return false;
            }

            Account account = FindAccount(name);
            if (account == null)
            {
                failure = "master_account_not_found_" + name;
                return false;
            }

            // AI owns one account: the configured group master. The replication
            // engine independently owns follower discovery, ratios and protection.
            members.Add(new ExecutionGroupMember { Account = account });

            groupId = selected.GroupId;
            return true;
        }

        private static GlitchAiExecutionResult TryExecuteGroupEnter(
            IReadOnlyList<ExecutionGroupMember> members,
            Instrument instrument,
            string rawJson,
            bool isLong,
            string groupId,
            string intentId)
        {
            double quantityValue;
            double stopLoss;
            double takeProfit1;
            if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity", out quantityValue) || quantityValue < 1
                || !GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss", out stopLoss)
                || !GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_1", out takeProfit1))
            {
                return GlitchAiExecutionResult.Failed("enter_fields_missing");
            }

            bool hasSecondTarget = GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_2", out double takeProfit2);
            bool hasSecondStop = GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss_2", out double stopLoss2);
            bool hasThirdTarget = GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_3", out double takeProfit3);
            bool hasThirdStop = GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss_3", out double stopLoss3);
            double quantityTp1Value = 0;
            double quantityTp2Value = 0;
            if (hasSecondTarget
                && (!GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity_tp1", out quantityTp1Value)
                    || quantityTp1Value < 1))
                return GlitchAiExecutionResult.Failed("enter_quantity_split_missing");
            if (hasSecondStop && !hasSecondTarget)
                return GlitchAiExecutionResult.Failed("enter_stop_loss_2_requires_tp2");
            if (hasThirdTarget
                && (!hasSecondTarget
                    || !GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity_tp2", out quantityTp2Value)
                    || quantityTp2Value < 1))
                return GlitchAiExecutionResult.Failed("enter_third_leg_split_missing");
            if (hasThirdStop && !hasThirdTarget)
                return GlitchAiExecutionResult.Failed("enter_stop_loss_3_requires_tp3");

            string instrumentRoot = GlitchAiJsonFields.ExtractString(rawJson, "instrument");
            string action = GlitchAiJsonFields.ExtractString(rawJson, "action");
            string snapshotHash = GlitchAiJsonFields.ExtractString(rawJson, "snapshot_hash");
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            if (policy == null || !policy.IsValid)
                return GlitchAiExecutionResult.Failed("policy_invalid", policy?.ValidationError);
            double snapshotMarketPrice;
            string snapshotFailure;
            if (!GlitchAiSnapshotRegistry.TryGetFreshInstrumentPrice(
                snapshotHash,
                instrumentRoot,
                DateTime.UtcNow,
                policy.SnapshotMaxAgeSeconds,
                out snapshotMarketPrice,
                out snapshotFailure))
            {
                return GlitchAiExecutionResult.Failed("group_snapshot_invalid", snapshotFailure);
            }

            GlitchInstrumentMetadata metadata;
            if (!GlitchInstrumentMetadataService.TryResolve(instrument, out metadata)
                || metadata == null
                || !metadata.IsResolved
                || metadata.TickSize <= 0
                || metadata.PointValue <= 0)
                return GlitchAiExecutionResult.Failed("group_execution_metadata_unavailable");

            double liveExecutionPrice;
            string livePriceFailure;
            if (!TryGetFreshExecutionPrice(instrument, DateTime.UtcNow, out liveExecutionPrice, out livePriceFailure))
                return GlitchAiExecutionResult.Failed("group_live_price_invalid", livePriceFailure);

            // Market intents necessarily permit movement between observation and
            // submission. Hermes owns whether the stated absolute geometry expresses
            // its thesis; Glitch only verifies that the live bracket remains
            // executable and inside authoritative account-survival boundaries.
            if (!IsExecutableBracketPrice(isLong, liveExecutionPrice, stopLoss, takeProfit1))
                return GlitchAiExecutionResult.Failed("group_structural_prices_crossed_before_entry", "leg=1");
            if (hasSecondTarget
                && !IsExecutableBracketPrice(isLong, liveExecutionPrice, hasSecondStop ? stopLoss2 : stopLoss, takeProfit2))
                return GlitchAiExecutionResult.Failed("group_structural_prices_crossed_before_entry", "leg=2");
            if (hasThirdTarget
                && !IsExecutableBracketPrice(
                    isLong,
                    liveExecutionPrice,
                    hasThirdStop ? stopLoss3 : hasSecondStop ? stopLoss2 : stopLoss,
                    takeProfit3))
                return GlitchAiExecutionResult.Failed("group_structural_prices_crossed_before_entry", "leg=3");

            int masterQuantity = (int)Math.Round(quantityValue, MidpointRounding.AwayFromZero);
            if (Math.Abs(quantityValue - masterQuantity) > 0.0000001d)
                return GlitchAiExecutionResult.Failed("master_quantity_must_be_integer");
            int quantityTp1 = masterQuantity;
            int quantityTp2 = 0;
            int quantityTp3 = 0;
            if (hasSecondTarget)
            {
                quantityTp1 = (int)Math.Round(quantityTp1Value, MidpointRounding.AwayFromZero);
                if (Math.Abs(quantityTp1Value - quantityTp1) > 0.0000001d
                    || quantityTp1 < 1 || quantityTp1 >= masterQuantity)
                    return GlitchAiExecutionResult.Failed("master_quantity_split_invalid");
                quantityTp2 = masterQuantity - quantityTp1;
            }
            if (hasThirdTarget)
            {
                quantityTp2 = (int)Math.Round(quantityTp2Value, MidpointRounding.AwayFromZero);
                quantityTp3 = masterQuantity - quantityTp1 - quantityTp2;
                if (Math.Abs(quantityTp2Value - quantityTp2) > 0.0000001d
                    || quantityTp2 < 1 || quantityTp3 < 1)
                    return GlitchAiExecutionResult.Failed("master_three_leg_quantity_split_invalid");
            }

            members[0].Quantity = masterQuantity;

            if (!TryGetNetPosition(members[0].Account, instrument, out int masterCurrentNet))
                return GlitchAiExecutionResult.Failed("master_positions_unavailable", members[0].Account.Name);
            int requestedDirection = isLong ? 1 : -1;
            if (masterCurrentNet != 0 && Math.Sign(masterCurrentNet) != requestedDirection)
                return GlitchAiExecutionResult.Failed("opposite_position_exists", members[0].Account.Name);
            Account masterAccount = members[0].Account;
            if (masterCurrentNet != 0 && !HasCompleteAiProtection(masterAccount, instrument, out _))
                return GlitchAiExecutionResult.Failed("existing_master_position_not_fully_ai_protected", masterAccount.Name);
            if (!TryHasWorkingNonProtectionOrder(masterAccount, instrument, out bool hasWorkingNonProtection))
                return GlitchAiExecutionResult.Failed("master_orders_unavailable", masterAccount.Name);
            if (hasWorkingNonProtection)
                return GlitchAiExecutionResult.Failed("master_order_in_flight_or_not_ai_owned", masterAccount.Name);

            bool riskLocked;
            bool evalTargetLocked;
            string portfolioFailure;
            if (!GlitchAiPortfolioSnapshotReader.TryGetFreshRiskState(
                masterAccount.Name,
                DateTime.UtcNow,
                policy.SnapshotMaxAgeSeconds,
                out riskLocked,
                out evalTargetLocked,
                out _,
                out _,
                out portfolioFailure))
                return GlitchAiExecutionResult.Failed("master_portfolio_snapshot_invalid", portfolioFailure);
            if (riskLocked)
                return GlitchAiExecutionResult.Failed("master_account_risk_locked", masterAccount.Name);
            if (evalTargetLocked)
                return GlitchAiExecutionResult.Failed("master_account_eval_target_locked", masterAccount.Name);

            OrderAction entryAction = isLong ? OrderAction.Buy : OrderAction.SellShort;
            string correlation = BuildIntentCorrelation(intentId);
            ExecutionGroupMember masterMember = members[0];
            var protectionLegs = new List<StructuralProtectionLeg>
            {
                new StructuralProtectionLeg
                {
                    Quantity = quantityTp1,
                    StopPrice = stopLoss,
                    TargetPrice = takeProfit1
                }
            };
            if (hasSecondTarget)
            {
                protectionLegs.Add(new StructuralProtectionLeg
                {
                    Quantity = hasThirdTarget ? quantityTp2 : masterQuantity - quantityTp1,
                    StopPrice = hasSecondStop ? stopLoss2 : stopLoss,
                    TargetPrice = takeProfit2
                });
            }
            if (hasThirdTarget)
            {
                protectionLegs.Add(new StructuralProtectionLeg
                {
                    Quantity = quantityTp3,
                    StopPrice = hasThirdStop ? stopLoss3 : hasSecondStop ? stopLoss2 : stopLoss,
                    TargetPrice = takeProfit3
                });
            }
            var group = new ExecutionGroupContext
            {
                Correlation = correlation,
                GroupId = groupId,
                IntentId = intentId,
                Instrument = instrument,
                Accounts = new List<Account> { masterMember.Account },
                Orders = new List<Order>(),
                IsLong = isLong,
                ProtectionLegs = protectionLegs,
                EntrySubmissionStarted = new bool[1],
                ProtectionSubmitted = new bool[1],
                EntrySubmittedUtc = DateTime.UtcNow
            };

            string entrySignal = BuildSignalName(SignalEntry, correlation, 0);
            if (!TryPrepareEntryBaselinePlan(
                intentId,
                0,
                masterMember.Account,
                instrument,
                entrySignal,
                requestedDirection,
                masterCurrentNet,
                out string baselineFailure))
                return GlitchAiExecutionResult.Failed("master_entry_baseline_plan_unavailable", baselineFailure);
            Order entryOrder = CreateEntryOrder(masterMember.Account, instrument, entryAction, masterMember.Quantity, entrySignal);
            if (entryOrder == null)
                return GlitchAiExecutionResult.Failed("master_entry_create_failed", masterMember.Account.Name);

            group.Orders.Add(entryOrder);
            foreach (StructuralProtectionLeg unused in protectionLegs)
            {
                group.Orders.Add(null);
                group.Orders.Add(null);
            }

            RegisterGroup(group);
            try
            {
                lock (GroupSync)
                    group.EntrySubmissionStarted[0] = true;
                masterMember.Account.Submit(new[] { entryOrder });
                ReconcileEntryProtection(group, 0, entryOrder, "submit_return");
                bool recoveryStarted;
                lock (GroupSync)
                    recoveryStarted = group.RecoveryStarted;
                if (recoveryStarted)
                    return GlitchAiExecutionResult.Pending("master_submit_recovery_pending", masterMember.Account.Name);
                if (IsRejected(entryOrder))
                {
                    RecoverGroup(group, "immediate_submit_reject_" + masterMember.Account.Name);
                    return GlitchAiExecutionResult.Failed("master_submit_rejected", masterMember.Account.Name);
                }
            }
            catch (Exception ex)
            {
                RecoverGroup(group, "submit_exception_" + ex.GetType().Name);
                return GlitchAiExecutionResult.Failed("group_submit_exception", ex.Message);
            }

            return GlitchAiExecutionResult.Pending(
                "master_entry_submitted",
                "group=" + CleanToken(groupId)
                    + "|correlation=" + correlation
                    + "|contract=" + CleanToken(instrument.FullName)
                    + "|master=" + CleanToken(masterMember.Account.Name)
                    + "|master_quantity=" + masterMember.Quantity.ToString(CultureInfo.InvariantCulture)
                    + "|followers=replication_engine"
                    + "|replication_owner=GlitchCopyEngine"
                    + "|snapshot_price=" + snapshotMarketPrice.ToString(CultureInfo.InvariantCulture)
                    + "|live_price=" + liveExecutionPrice.ToString(CultureInfo.InvariantCulture)
                    + "|stop_price=" + stopLoss.ToString(CultureInfo.InvariantCulture)
                    + "|target_price=" + takeProfit1.ToString(CultureInfo.InvariantCulture)
                    + "|protection_legs=" + protectionLegs.Count.ToString(CultureInfo.InvariantCulture)
                    + "|quantity_tp1=" + quantityTp1.ToString(CultureInfo.InvariantCulture)
                    + "|structural_prices=preserved");
        }

        private static bool TryGetFreshExecutionPrice(
            Instrument instrument,
            DateTime nowUtc,
            out double price,
            out string failure)
        {
            price = 0;
            failure = null;
            if (instrument == null || instrument.MarketData == null || instrument.MarketData.Last == null)
            {
                failure = "live_last_missing";
                return false;
            }

            NinjaTrader.Data.MarketDataEventArgs last = instrument.MarketData.Last;
            price = last.Price;
            if (price <= 0 || double.IsNaN(price) || double.IsInfinity(price))
            {
                failure = "live_last_price_invalid";
                return false;
            }

            DateTime eventUtc = last.Time.ToUniversalTime();
            if (eventUtc == DateTime.MinValue
                || eventUtc > nowUtc.AddSeconds(2)
                || (nowUtc - eventUtc) > ExecutionPriceMaxAge)
            {
                failure = "live_last_stale";
                return false;
            }

            return true;
        }

        private static bool IsExecutableBracketPrice(bool isLong, double referencePrice, double stopPrice, double targetPrice)
        {
            return referencePrice > 0 && stopPrice > 0 && targetPrice > 0
                && (isLong
                    ? stopPrice < referencePrice && targetPrice > referencePrice
                    : stopPrice > referencePrice && targetPrice < referencePrice);
        }

        private static bool TryGetEntryAccountIndex(ExecutionGroupContext group, Order order, out int accountIndex)
        {
            accountIndex = -1;
            if (group == null || order == null || group.Orders == null)
                return false;

            int stride = GetOrderStride(group);
            for (int i = 0; i < group.Accounts.Count; i++)
            {
                int offset = i * stride;
                Account expectedAccount = group.Accounts[i];
                string expectedSignal = BuildSignalName(SignalEntry, group.Correlation, i);
                if (offset < group.Orders.Count
                    && expectedAccount != null
                    && order.Account != null
                    && string.Equals(order.Name, expectedSignal, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(order.Account.Name, expectedAccount.Name, StringComparison.OrdinalIgnoreCase)
                    && SameInstrument(order.Instrument, group.Instrument))
                {
                    lock (GroupSync)
                        group.Orders[offset] = order;
                    accountIndex = i;
                    return true;
                }
            }

            return false;
        }

        private static int FindGroupAccountIndex(ExecutionGroupContext group, Account account)
        {
            if (group?.Accounts == null || account == null || string.IsNullOrWhiteSpace(account.Name))
                return -1;
            for (int i = 0; i < group.Accounts.Count; i++)
            {
                Account candidate = group.Accounts[i];
                if (candidate != null
                    && string.Equals(candidate.Name, account.Name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static void ReconcileEntryProtection(
            ExecutionGroupContext group,
            int accountIndex,
            Order entry,
            string origin)
        {
            if (group == null || entry == null || group.RecoveryStarted
                || accountIndex < 0 || accountIndex >= group.Accounts.Count)
                return;

            int offset = accountIndex * GetOrderStride(group);
            if (offset < 0 || offset >= group.Orders.Count)
                return;
            lock (GroupSync)
                group.Orders[offset] = entry;

            if (entry.OrderState == OrderState.Rejected)
            {
                RecoverGroup(group, CleanToken(origin) + "_entry_rejected_" + group.Accounts[accountIndex].Name);
                return;
            }
            if (entry.Filled <= 0)
                return;
            if (entry.OrderState != OrderState.Filled)
            {
                // A multi-contract market entry can fill in several native
                // executions. Aggregate while it remains working. A terminal
                // incomplete entry cannot use the requested structural split.
                if (GlitchReplicationEngine.IsWorkingOrderState(entry.OrderState))
                    return;

                GlitchAiExecutionJournalWriter.TryAppend(
                    group.IntentId,
                    GlitchAiExecutionResult.Failed(
                        "group_terminal_partial_entry_fill_recovery",
                        "account=" + CleanToken(group.Accounts[accountIndex].Name)
                            + "|filled=" + entry.Filled.ToString(CultureInfo.InvariantCulture)
                            + "|quantity=" + entry.Quantity.ToString(CultureInfo.InvariantCulture)),
                    DateTime.UtcNow);
                RecoverGroup(group, "terminal_partial_entry_fill_" + group.Accounts[accountIndex].Name);
                return;
            }

            GlitchAiExecutionResult protection = TrySubmitStructuralProtection(group, accountIndex);
            if (!string.Equals(protection.Code, "group_structural_brackets_submitted", StringComparison.Ordinal)
                && !string.Equals(protection.Code, "group_structural_brackets_already_submitted", StringComparison.Ordinal))
            {
                GlitchAiExecutionJournalWriter.TryAppend(group.IntentId, protection, DateTime.UtcNow);
                RecoverGroup(
                    group,
                    "fill_protection_" + protection.Code + "_" + group.Accounts[accountIndex].Name);
                return;
            }

            if (string.Equals(protection.Code, "group_structural_brackets_submitted", StringComparison.Ordinal))
                GlitchAiExecutionJournalWriter.TryAppend(group.IntentId, protection, DateTime.UtcNow);
        }

        private static GlitchAiExecutionResult TrySubmitStructuralProtection(
            ExecutionGroupContext group,
            int accountIndex)
        {
            if (group == null || accountIndex < 0 || accountIndex >= group.Accounts.Count)
                return GlitchAiExecutionResult.Failed("group_protection_account_invalid");

            int offset = accountIndex * GetOrderStride(group);
            if (group.ProtectionLegs == null || group.ProtectionLegs.Count == 0
                || offset + (group.ProtectionLegs.Count * 2) >= group.Orders.Count)
                return GlitchAiExecutionResult.Failed("group_protection_slots_missing");

            Account account = group.Accounts[accountIndex];
            Order entry = group.Orders[offset];
            if (account == null || entry == null
                || entry.Filled <= 0
                || entry.AverageFillPrice <= 0)
                return GlitchAiExecutionResult.Failed("group_entry_fill_incomplete");

            if (group.ProtectionLegs.Sum(leg => leg.Quantity) != entry.Filled)
                return GlitchAiExecutionResult.Failed("group_structural_quantity_split_invalid");

            double fillPrice = entry.AverageFillPrice;
            OrderAction exitAction = group.IsLong ? OrderAction.Sell : OrderAction.BuyToCover;
            foreach (StructuralProtectionLeg leg in group.ProtectionLegs)
            {
                if (!IsExecutableBracketPrice(group.IsLong, fillPrice, leg.StopPrice, leg.TargetPrice))
                    return GlitchAiExecutionResult.Failed("group_structural_geometry_invalid_at_fill");
            }

            // Account.CreateOrder can synchronously raise account/order callbacks.
            // Claim this account's one protection submission before creating the
            // first native order so callback re-entry cannot build another bracket.
            lock (GroupSync)
            {
                if (group.ProtectionSubmitted == null
                    || accountIndex >= group.ProtectionSubmitted.Length)
                    return GlitchAiExecutionResult.Failed("group_protection_state_missing");
                if (group.ProtectionSubmitted[accountIndex])
                    return GlitchAiExecutionResult.Succeeded("group_structural_brackets_already_submitted");
                group.ProtectionSubmitted[accountIndex] = true;
            }

            var createdOrders = new List<Order>();
            var evidence = new List<string>();
            string protectionSubmissionNonce = Guid.NewGuid().ToString("N").Substring(0, 6);
            try
            {
                for (int legIndex = 0; legIndex < group.ProtectionLegs.Count; legIndex++)
                {
                    StructuralProtectionLeg leg = group.ProtectionLegs[legIndex];
                    double stopPrice = leg.StopPrice;
                    double targetPrice = leg.TargetPrice;
                    string legToken = (legIndex + 1).ToString(CultureInfo.InvariantCulture);
                    string oco = "GLTAI" + group.Correlation
                        + accountIndex.ToString(CultureInfo.InvariantCulture)
                        + legToken + protectionSubmissionNonce;
                    string stopSignal = BuildSignalName(SignalStop, group.Correlation, accountIndex, legIndex);
                    string targetSignal = BuildSignalName(SignalTarget, group.Correlation, accountIndex, legIndex);
                    Order stop = CreateExitOrder(account, group.Instrument, exitAction, OrderType.StopMarket, leg.Quantity, 0, stopPrice, oco, stopSignal);
                    Order target = CreateExitOrder(account, group.Instrument, exitAction, OrderType.Limit, leg.Quantity, targetPrice, 0, oco, targetSignal);
                    if (stop != null)
                        createdOrders.Add(stop);
                    if (target != null)
                        createdOrders.Add(target);
                    if (stop == null || target == null)
                    {
                        ReleaseProtectionSubmission(group, accountIndex, createdOrders);
                        return GlitchAiExecutionResult.Failed("group_structural_bracket_create_failed", "leg=" + legToken);
                    }
                    lock (GroupSync)
                    {
                        group.Orders[offset + 1 + (legIndex * 2)] = stop;
                        group.Orders[offset + 2 + (legIndex * 2)] = target;
                    }
                    evidence.Add("leg" + legToken + "_qty=" + leg.Quantity.ToString(CultureInfo.InvariantCulture));
                    evidence.Add("sl" + legToken + "=" + stopPrice.ToString(CultureInfo.InvariantCulture));
                    evidence.Add("tp" + legToken + "=" + targetPrice.ToString(CultureInfo.InvariantCulture));
                }

                lock (GroupSync)
                {
                    foreach (Order protectionOrder in createdOrders)
                        GroupsBySignal[protectionOrder.Name.Trim()] = group;
                }
                account.Submit(createdOrders.ToArray());
                if (createdOrders.Any(IsRejected))
                {
                    ReleaseProtectionSubmission(group, accountIndex, createdOrders);
                    return GlitchAiExecutionResult.Failed("group_structural_bracket_rejected", account.Name);
                }
            }
            catch (Exception ex)
            {
                ReleaseProtectionSubmission(group, accountIndex, createdOrders);
                return GlitchAiExecutionResult.Failed("group_structural_bracket_submit_exception", ex.GetType().Name);
            }

            return GlitchAiExecutionResult.Succeeded(
                "group_structural_brackets_submitted",
                BuildGroupEvidenceMessage(group)
                    + "|account=" + CleanToken(account.Name)
                    + "|fill=" + fillPrice.ToString(CultureInfo.InvariantCulture)
                    + "|" + string.Join("|", evidence));
        }

        private static void ReleaseProtectionSubmission(
            ExecutionGroupContext group,
            int accountIndex,
            IEnumerable<Order> createdOrders)
        {
            lock (GroupSync)
            {
                foreach (Order protectionOrder in createdOrders ?? Enumerable.Empty<Order>())
                {
                    if (protectionOrder != null && !string.IsNullOrWhiteSpace(protectionOrder.Name))
                        GroupsBySignal.Remove(protectionOrder.Name.Trim());
                }
                if (group?.ProtectionSubmitted != null
                    && accountIndex >= 0
                    && accountIndex < group.ProtectionSubmitted.Length)
                    group.ProtectionSubmitted[accountIndex] = false;
            }
        }

        private static double RoundToTick(double price, double tickSize)
        {
            if (tickSize <= 0)
                return price;
            return Math.Round(price / tickSize, MidpointRounding.AwayFromZero) * tickSize;
        }

        private static GlitchAiExecutionResult TryExecuteGroupExit(
            IReadOnlyList<Account> accounts,
            Instrument instrument,
            string groupId,
            string intentId)
        {
            string exitCorrelation = BuildIntentCorrelation(intentId);
            var plans = new List<OwnedExitPlan>();
            for (int accountIndex = 0; accountIndex < accounts.Count; accountIndex++)
            {
                Account account = accounts[accountIndex];
                string exitSignal = BuildSignalName(SignalExit, exitCorrelation, accountIndex);
                if (!TryFindNamedOrders(account, exitSignal, instrument, out List<Order> existingExits))
                    return GlitchAiExecutionResult.Pending("group_exit_visibility_unavailable", account.Name);
                if (existingExits.Count > 0)
                    return TryReconcileOwnedExit(account, instrument, intentId, accountIndex);
                if (!TryGetNetPosition(account, instrument, out int net))
                    return GlitchAiExecutionResult.Failed("group_exit_position_state_unavailable", account.Name);
                if (net == 0)
                    return GlitchAiExecutionResult.Failed("group_exit_human_override_flat", account.Name);
                if (!TryCollectActiveOwnedExposure(account, instrument, out ActiveOwnedExposure owned, out string ownershipFailure)
                    || owned.Direction != Math.Sign(net)
                    || owned.Quantity != Math.Abs(net))
                    return GlitchAiExecutionResult.Failed(
                        "group_exit_superseded_manual_override",
                        "account=" + CleanToken(account.Name) + "|reason=" + CleanToken(ownershipFailure));
                OrderAction closeAction = owned.Direction > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
                Order close = CreateExitOrder(
                    account,
                    instrument,
                    closeAction,
                    OrderType.Market,
                    owned.Quantity,
                    0,
                    0,
                    string.Empty,
                    exitSignal);
                if (close == null)
                    return GlitchAiExecutionResult.Failed("group_exit_owned_close_create_failed", account.Name);
                plans.Add(new OwnedExitPlan
                {
                    Account = account,
                    Order = close,
                    Correlations = owned.Correlations,
                    Direction = owned.Direction,
                    AccountIndex = accountIndex
                });
            }

            // Commit the exact correlations before native Submit. A restart can
            // then distinguish this EXIT's protection from a later AI addition.
            foreach (OwnedExitPlan plan in plans)
            {
                if (!GlitchAiExitOwnershipPlanStore.TryPersist(new GlitchAiExitOwnershipPlan
                {
                    IntentId = intentId,
                    AccountIndex = plan.AccountIndex,
                    AccountName = plan.Account.Name,
                    InstrumentName = instrument.FullName,
                    ExitSignal = plan.Order.Name,
                    Quantity = plan.Order.Quantity,
                    Direction = plan.Direction,
                    Correlations = new HashSet<string>(plan.Correlations, StringComparer.OrdinalIgnoreCase)
                }, out string planFailure))
                    return GlitchAiExecutionResult.Failed("group_exit_ownership_plan_unavailable", planFailure);
            }

            // Submit the deterministic exit while native protection remains live.
            // A Submit return is not enough to remove OCO protection: NinjaTrader
            // must first expose this UUID-named exit in an actionable native state.
            // A thrown, initialized, or otherwise unobservable order leaves every
            // protection order intact for same-UUID reconciliation rather than
            // turning broker visibility ambiguity into an unprotected position.
            try
            {
                foreach (OwnedExitPlan plan in plans)
                {
                    plan.Account.Submit(new[] { plan.Order });
                    if (!TryFindNamedOrders(plan.Account, plan.Order.Name, instrument, out List<Order> observedExits)
                        || observedExits.Count != 1
                        || !IsNativeExitActionable(observedExits[0]))
                        return GlitchAiExecutionResult.Pending("group_exit_native_visibility_pending", plan.Account.Name);
                    if (!IsNativeOrderVisibilityReady(plan.Account))
                        return GlitchAiExecutionResult.Pending("group_exit_visibility_unavailable", "account_connection_not_connected");
                    if (!TryCancelActiveOwnedProtection(plan.Account, instrument, plan.Correlations))
                        return GlitchAiExecutionResult.Pending("group_exit_protection_cancel_request_failed", plan.Account.Name);
                    if (!ArePlannedProtectionOrdersTerminalOrAbsent(plan.Account, instrument, plan.Correlations))
                        return GlitchAiExecutionResult.Pending("group_exit_protection_cancel_pending", plan.Account.Name);
                }
            }
            catch (Exception ex)
            {
                return GlitchAiExecutionResult.Pending("group_exit_submit_ambiguous", ex.GetType().Name);
            }

            return GlitchAiExecutionResult.Pending(
                "group_exit_owned_close_submitted",
                "group=" + CleanToken(groupId)
                    + "|accounts=" + string.Join(",", accounts.Select(account => account.Name)));
        }

        private static GlitchAiExecutionResult TryExecuteGroupMoveStop(
            IReadOnlyList<ExecutionGroupMember> members,
            Instrument instrument,
            string rawJson,
            string groupId,
            string intentId)
        {
            Account masterAccount = members[0].Account;
            if (!HasCompleteAiProtection(masterAccount, instrument, out List<Order> masterStops))
                return GlitchAiExecutionResult.Failed("move_stop_protection_incomplete", masterAccount.Name);
            if (!TryGetNetPosition(masterAccount, instrument, out int masterNet))
                return GlitchAiExecutionResult.Failed("move_stop_position_state_unavailable", masterAccount.Name);
            if (masterNet == 0)
                return GlitchAiExecutionResult.Failed("move_stop_position_flat");
            bool isLong = masterNet > 0;
            if (!TryGetFreshExecutionPrice(instrument, DateTime.UtcNow, out double livePrice, out string liveFailure))
                return GlitchAiExecutionResult.Failed("move_stop_live_price_invalid", liveFailure);
            if (!TryParseProtectionAmendments(rawJson, false, out List<ProtectionAmendment> amendments, out string parseFailure))
                return GlitchAiExecutionResult.Failed("move_stop_updates_invalid", parseFailure);

            var proposedStops = new Dictionary<Order, double>();
            bool isV3 = IsIntentV3(rawJson);
            if (isV3)
            {
                if (!TryIndexProtectionOrders(masterStops, SignalStop, out Dictionary<string, Order> stopsByLeg, out string indexFailure))
                    return GlitchAiExecutionResult.Failed("move_stop_leg_state_invalid", indexFailure);
                foreach (ProtectionAmendment amendment in amendments)
                {
                    if (!stopsByLeg.TryGetValue(amendment.LegId, out Order stop))
                        return GlitchAiExecutionResult.Failed("move_stop_leg_not_found", amendment.LegId);
                    proposedStops[stop] = amendment.StopPrice.Value;
                }
            }
            else
            {
                double stopPrice = amendments[0].StopPrice.Value;
                foreach (Order stop in masterStops)
                    proposedStops[stop] = stopPrice;
            }

            foreach (double stopPrice in proposedStops.Values)
            {
                if ((isLong && stopPrice >= livePrice) || (!isLong && stopPrice <= livePrice))
                    return GlitchAiExecutionResult.Failed("move_stop_market_side_invalid");
            }
            List<Order> changes = proposedStops
                .Where(item => !PricesEqual(item.Key.StopPrice, item.Value))
                .Select(item => item.Key)
                .ToList();
            if (changes.Count > 0)
            {
                try
                {
                    foreach (Order stop in changes)
                        stop.StopPriceChanged = proposedStops[stop];
                    masterAccount.Change(changes.ToArray());
                }
                catch (Exception ex)
                {
                    return GlitchAiExecutionResult.Failed("move_stop_change_failed", masterAccount.Name + ":" + ex.GetType().Name);
                }
            }
            if (changes.Count == 0)
            {
                return GlitchAiExecutionResult.Succeeded(
                    "move_stop_already_set",
                    "group=" + CleanToken(groupId)
                        + "|requested_legs=" + amendments.Count.ToString(CultureInfo.InvariantCulture)
                        + "|followers=replication_engine");
            }

            TrackPendingAmendment(intentId, rawJson);
            return GlitchAiExecutionResult.Pending(
                "move_stop_amendment_pending",
                "group=" + CleanToken(groupId)
                    + "|master_orders=" + changes.Count.ToString(CultureInfo.InvariantCulture)
                    + "|requested_legs=" + amendments.Count.ToString(CultureInfo.InvariantCulture)
                    + "|followers=replication_engine");
        }

        private static GlitchAiExecutionResult TryExecuteGroupMoveTarget(
            IReadOnlyList<ExecutionGroupMember> members,
            Instrument instrument,
            string rawJson,
            string groupId,
            string intentId)
        {
            if (!TryGetFreshExecutionPrice(instrument, DateTime.UtcNow, out double livePrice, out string liveFailure))
                return GlitchAiExecutionResult.Failed("move_tp_live_price_invalid", liveFailure);

            Account masterAccount = members[0].Account;
            if (!TryGetNetPosition(masterAccount, instrument, out int masterNet))
                return GlitchAiExecutionResult.Failed("move_tp_position_state_unavailable", masterAccount.Name);
            if (masterNet == 0)
                return GlitchAiExecutionResult.Failed("move_tp_position_flat");
            bool isLong = masterNet > 0;
            if (!HasCompleteAiProtection(masterAccount, instrument, out List<Order> masterStops, out List<Order> masterTargets))
                return GlitchAiExecutionResult.Failed("move_tp_protection_incomplete", masterAccount.Name);
            if (!TryParseProtectionAmendments(rawJson, true, out List<ProtectionAmendment> amendments, out string parseFailure))
                return GlitchAiExecutionResult.Failed("move_tp_updates_invalid", parseFailure);

            var proposedTargets = new Dictionary<Order, double>();
            var proposedStops = new Dictionary<Order, double>();
            if (IsIntentV3(rawJson))
            {
                if (!TryIndexProtectionOrders(masterTargets, SignalTarget, out Dictionary<string, Order> targetsByLeg, out string targetIndexFailure))
                    return GlitchAiExecutionResult.Failed("move_tp_leg_state_invalid", targetIndexFailure);
                if (!TryIndexProtectionOrders(masterStops, SignalStop, out Dictionary<string, Order> stopsByLeg, out string stopIndexFailure))
                    return GlitchAiExecutionResult.Failed("move_tp_leg_state_invalid", stopIndexFailure);
                foreach (ProtectionAmendment amendment in amendments)
                {
                    if (!targetsByLeg.TryGetValue(amendment.LegId, out Order target))
                        return GlitchAiExecutionResult.Failed("move_tp_leg_not_found", amendment.LegId);
                    proposedTargets[target] = amendment.TargetPrice.Value;
                    if (amendment.StopPrice.HasValue)
                    {
                        if (!stopsByLeg.TryGetValue(amendment.LegId, out Order stop))
                            return GlitchAiExecutionResult.Failed("move_tp_stop_leg_not_found", amendment.LegId);
                        proposedStops[stop] = amendment.StopPrice.Value;
                    }
                }
            }
            else
            {
                if (masterTargets.Count != 1)
                    return GlitchAiExecutionResult.Failed("legacy_move_tp_scope_ambiguous", "remaining_targets=" + masterTargets.Count.ToString(CultureInfo.InvariantCulture));
                ProtectionAmendment amendment = amendments[0];
                proposedTargets[masterTargets[0]] = amendment.TargetPrice.Value;
                if (amendment.StopPrice.HasValue)
                {
                    Order matchingStop = masterStops.SingleOrDefault(stop => string.Equals(stop.Oco, masterTargets[0].Oco, StringComparison.Ordinal));
                    if (matchingStop == null)
                        return GlitchAiExecutionResult.Failed("legacy_move_tp_stop_scope_unavailable");
                    proposedStops[matchingStop] = amendment.StopPrice.Value;
                }
            }

            foreach (double targetPrice in proposedTargets.Values)
            {
                if ((isLong && targetPrice <= livePrice) || (!isLong && targetPrice >= livePrice))
                    return GlitchAiExecutionResult.Failed("move_tp_market_side_invalid");
            }
            foreach (double stopPrice in proposedStops.Values)
            {
                if ((isLong && stopPrice >= livePrice) || (!isLong && stopPrice <= livePrice))
                    return GlitchAiExecutionResult.Failed("move_tp_stop_market_side_invalid");
            }
            List<Order> targetChanges = proposedTargets
                .Where(item => !PricesEqual(item.Key.LimitPrice, item.Value))
                .Select(item => item.Key)
                .ToList();
            List<Order> stopChanges = proposedStops
                .Where(item => !PricesEqual(item.Key.StopPrice, item.Value))
                .Select(item => item.Key)
                .ToList();
            List<Order> changes = targetChanges.Concat(stopChanges).ToList();
            if (changes.Count == 0)
            {
                return GlitchAiExecutionResult.Succeeded(
                    "move_tp_already_set",
                    "group=" + CleanToken(groupId)
                        + "|requested_legs=" + amendments.Count.ToString(CultureInfo.InvariantCulture)
                        + "|followers=replication_engine");
            }

            try
            {
                foreach (Order target in targetChanges)
                    target.LimitPriceChanged = proposedTargets[target];
                foreach (Order stop in stopChanges)
                    stop.StopPriceChanged = proposedStops[stop];
                masterAccount.Change(changes.ToArray());
            }
            catch (Exception ex)
            {
                return GlitchAiExecutionResult.Failed("move_tp_change_failed", masterAccount.Name + ":" + ex.GetType().Name);
            }

            TrackPendingAmendment(intentId, rawJson);
            return GlitchAiExecutionResult.Pending(
                stopChanges.Count > 0 ? "move_tp_and_stop_amendment_pending" : "move_tp_amendment_pending",
                "group=" + CleanToken(groupId)
                    + "|master_target_orders=" + targetChanges.Count.ToString(CultureInfo.InvariantCulture)
                    + "|master_stop_orders=" + stopChanges.Count.ToString(CultureInfo.InvariantCulture)
                    + "|requested_legs=" + amendments.Count.ToString(CultureInfo.InvariantCulture)
                    + "|followers=replication_engine");
        }

        private static bool IsIntentV3(string rawJson)
        {
            return string.Equals(
                GlitchAiJsonFields.ExtractString(rawJson, "schema_version"),
                "glitch.intent.v3",
                StringComparison.Ordinal);
        }

        private static bool TryParseProtectionAmendments(
            string rawJson,
            bool requireTarget,
            out List<ProtectionAmendment> amendments,
            out string failure)
        {
            amendments = new List<ProtectionAmendment>();
            failure = null;
            if (!IsIntentV3(rawJson))
            {
                var legacy = new ProtectionAmendment();
                if (requireTarget)
                {
                    if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_1", out double target))
                    {
                        failure = "take_profit_1_missing";
                        return false;
                    }
                    legacy.TargetPrice = target;
                    if (GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss", out double targetStop))
                        legacy.StopPrice = targetStop;
                }
                else
                {
                    if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss", out double stop))
                    {
                        failure = "stop_loss_missing";
                        return false;
                    }
                    legacy.StopPrice = stop;
                }
                amendments.Add(legacy);
                return true;
            }

            if (!GlitchAiJsonFields.TryParseObject(rawJson, out IDictionary parsed)
                || !(parsed["protection_updates"] is IList updates)
                || updates.Count == 0)
            {
                failure = "protection_updates_missing";
                return false;
            }
            var legIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < updates.Count; i++)
            {
                IDictionary update = updates[i] as IDictionary;
                string legId = update != null && update.Contains("leg_id") ? update["leg_id"] as string : null;
                if (string.IsNullOrWhiteSpace(legId) || !legIds.Add(legId.Trim()))
                {
                    failure = "leg_id_missing_or_duplicate";
                    return false;
                }
                var amendment = new ProtectionAmendment { LegId = legId.Trim() };
                if (requireTarget)
                {
                    if (!TryGetParsedNumber(update, "take_profit", out double target))
                    {
                        failure = "take_profit_missing";
                        return false;
                    }
                    amendment.TargetPrice = target;
                    if (update.Contains("stop_loss"))
                    {
                        if (!TryGetParsedNumber(update, "stop_loss", out double targetStop))
                        {
                            failure = "stop_loss_invalid";
                            return false;
                        }
                        amendment.StopPrice = targetStop;
                    }
                }
                else
                {
                    if (!TryGetParsedNumber(update, "stop_loss", out double stop))
                    {
                        failure = "stop_loss_missing";
                        return false;
                    }
                    amendment.StopPrice = stop;
                }
                amendments.Add(amendment);
            }
            return true;
        }

        private static bool TryGetParsedNumber(IDictionary parsed, string key, out double value)
        {
            value = 0;
            if (parsed == null || !parsed.Contains(key) || parsed[key] == null
                || parsed[key] is bool || parsed[key] is string)
                return false;
            try
            {
                value = Convert.ToDouble(parsed[key], CultureInfo.InvariantCulture);
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch
            {
                value = 0;
                return false;
            }
        }

        private static bool TryIndexProtectionOrders(
            IEnumerable<Order> orders,
            string signalPrefix,
            out Dictionary<string, Order> byLeg,
            out string failure)
        {
            byLeg = new Dictionary<string, Order>(StringComparer.Ordinal);
            failure = null;
            foreach (Order order in orders)
            {
                if (!TryGetLegId(order?.Name, signalPrefix, out string legId))
                {
                    failure = "native_leg_id_unavailable";
                    return false;
                }
                if (byLeg.ContainsKey(legId))
                {
                    failure = "native_leg_id_duplicate_" + legId;
                    return false;
                }
                byLeg[legId] = order;
            }
            return true;
        }

        internal static bool TryGetLegId(string signalName, string signalPrefix, out string legId)
        {
            legId = null;
            string prefix = signalPrefix + "-";
            if (string.IsNullOrWhiteSpace(signalName)
                || !signalName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            string[] parts = signalName.Substring(prefix.Length).Split('-');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0])
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return false;
            int legIndex = 0;
            if (parts.Length > 2
                && !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out legIndex))
                return false;
            legId = parts[0] + ":" + legIndex.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private static bool PricesEqual(double left, double right)
        {
            return Math.Abs(left - right) <= 0.0000001d;
        }

        private sealed class ProtectionAmendment
        {
            public string LegId { get; set; }
            public double? StopPrice { get; set; }
            public double? TargetPrice { get; set; }
        }

        private static Order FindNamedOrder(Account account, string name, Instrument instrument)
        {
            List<Order> matches = FindNamedOrders(account, name, instrument);
            return matches.Count == 1 ? matches[0] : null;
        }

        private static List<Order> FindNamedOrders(Account account, string name, Instrument instrument)
        {
            return TryFindNamedOrders(account, name, instrument, out List<Order> matches)
                ? matches
                : new List<Order>();
        }

        private static bool TryFindNamedOrders(
            Account account,
            string name,
            Instrument instrument,
            out List<Order> matches)
        {
            matches = new List<Order>();
            if (string.IsNullOrWhiteSpace(name)
                || !TrySnapshotOrders(account, out Order[] orders))
                return false;

            matches = orders.Where(order => order != null
                && string.Equals(order.Name, name, StringComparison.OrdinalIgnoreCase)
                && order.Account != null
                && string.Equals(order.Account.Name, account.Name, StringComparison.OrdinalIgnoreCase)
                && SameInstrument(order.Instrument, instrument)).ToList();
            return true;
        }

        private static GlitchAiExecutionResult TryReconcileOwnedExit(
            Account account,
            Instrument instrument,
            string intentId,
            int accountIndex)
        {
            string exitSignal = BuildSignalName(SignalExit, BuildIntentCorrelation(intentId), accountIndex);
            if (!TryFindNamedOrders(account, exitSignal, instrument, out List<Order> matches))
                return GlitchAiExecutionResult.Pending("reconcile_exit_visibility_unavailable");
            if (matches.Count != 1)
            {
                if (!TryGetNetPosition(account, instrument, out int missingNet))
                    return GlitchAiExecutionResult.Failed("reconcile_exit_position_unknown");
                if (matches.Count == 0 && missingNet == 0)
                {
                    if (!GlitchAiExitOwnershipPlanStore.TryLoad(intentId, accountIndex, out GlitchAiExitOwnershipPlan flatPlan))
                        return ReconcileSecondAbsentExitSnapshot(account, instrument, exitSignal);
                    if (!IsNativeOrderVisibilityReady(account))
                        return GlitchAiExecutionResult.Pending("reconcile_exit_visibility_unavailable", "account_connection_not_connected");
                    return ReconcilePlannedExitProtection(account, instrument, flatPlan, 0, true);
                }
                if (matches.Count == 0 && !IsNativeOrderVisibilityReady(account))
                    return GlitchAiExecutionResult.Pending("reconcile_exit_visibility_unavailable", "account_connection_not_connected");
                return matches.Count == 0
                    ? ReconcileSecondAbsentExitSnapshot(account, instrument, exitSignal)
                    : GlitchAiExecutionResult.Failed("reconcile_exit_native_identity_ambiguous");
            }

            Order exit = matches[0];
            if (!GlitchAiExitOwnershipPlanStore.TryLoad(intentId, accountIndex, out GlitchAiExitOwnershipPlan plan))
                return GlitchAiExecutionResult.Failed("reconcile_exit_ownership_plan_identity_ambiguous");
            if (!ExitPlanMatchesNativeOrder(plan, account, instrument, exitSignal, exit))
                return GlitchAiExecutionResult.Failed("reconcile_exit_ownership_plan_identity_ambiguous");
            // A named UUID exit must be actionable before its redundant OCO
            // protection is removed. This also covers a crash after native submit:
            // a later Filled observation still clears the owned pairs when flat.
            if (exit.OrderState == OrderState.Rejected || exit.OrderState == OrderState.Cancelled)
            {
                if (!TryGetNetPosition(account, instrument, out int terminalNet))
                    return GlitchAiExecutionResult.Failed("reconcile_exit_position_unknown");
                if (terminalNet != 0)
                    return GlitchAiExecutionResult.Failed("reconciled_exit_terminal_" + exit.OrderState + "_residual");
                return ReconcilePlannedExitProtection(account, instrument, plan, 0, true);
            }
            if (!IsNativeExitActionable(exit))
                return GlitchAiExecutionResult.Pending("reconcile_exit_native_visibility_pending", exit.OrderState.ToString());
            if (!TryGetNetPosition(account, instrument, out int net))
                return GlitchAiExecutionResult.Failed("reconcile_exit_position_unknown");
            int remainingExit = Math.Max(0, exit.Quantity - exit.Filled);
            if (net != plan.Direction * remainingExit)
                return GlitchAiExecutionResult.Failed(
                    "reconcile_exit_superseded_manual_or_concurrent_intent",
                    "net=" + net.ToString(CultureInfo.InvariantCulture)
                        + "|expected=" + (plan.Direction * remainingExit).ToString(CultureInfo.InvariantCulture));
            return ReconcilePlannedExitProtection(account, instrument, plan, net, exit.OrderState == OrderState.Filled);
        }

        private static GlitchAiExecutionResult ReconcilePlannedExitProtection(
            Account account,
            Instrument instrument,
            GlitchAiExitOwnershipPlan plan,
            int net,
            bool exitFilledOrAbsent)
        {
            // Account.Orders can be stale while disconnected. Neither a cancel
            // request nor a terminal/absent proof is safe until the native feed
            // is Connected again.
            if (!IsNativeOrderVisibilityReady(account))
                return GlitchAiExecutionResult.Pending("reconcile_exit_visibility_unavailable", "account_connection_not_connected");
            if (ArePlannedProtectionOrdersTerminalOrAbsent(account, instrument, plan.Correlations))
                return net == 0 && exitFilledOrAbsent
                    ? GlitchAiExecutionResult.Succeeded("reconciled_exit_flat")
                    : GlitchAiExecutionResult.Pending("reconcile_exit_protection_cancelled_pending");
            if (TryCollectActiveOwnedExposureForCorrelations(account, instrument, plan.Correlations, out ActiveOwnedExposure plannedExposure, out string ownershipFailure)
                && (plannedExposure.Direction != plan.Direction || plannedExposure.Quantity != plan.Quantity))
                return GlitchAiExecutionResult.Failed(
                    "reconcile_exit_superseded_manual_or_concurrent_intent",
                    "planned_qty=" + plannedExposure.Quantity.ToString(CultureInfo.InvariantCulture)
                        + "|exit_qty=" + plan.Quantity.ToString(CultureInfo.InvariantCulture));
            if (!TryCancelActiveOwnedProtection(account, instrument, plan.Correlations))
                return GlitchAiExecutionResult.Pending("reconcile_exit_protection_cancel_request_failed");
            // One OCO sibling may already be terminal while the other is still
            // Working/CancelPending. That is normal asynchronous cancellation,
            // not a reason to touch a different correlation or report success.
            if (!ArePlannedProtectionOrdersTerminalOrAbsent(account, instrument, plan.Correlations))
                return GlitchAiExecutionResult.Pending("reconcile_exit_protection_cancel_pending");
            return net == 0 && exitFilledOrAbsent
                ? GlitchAiExecutionResult.Succeeded("reconciled_exit_flat")
                : GlitchAiExecutionResult.Pending("reconcile_exit_pending");
        }

        private static bool IsNativeExitActionable(Order exit)
        {
            if (exit == null)
                return false;
            return exit.OrderState == OrderState.Submitted
                || exit.OrderState == OrderState.Accepted
                || exit.OrderState == OrderState.Working
                || exit.OrderState == OrderState.PartFilled
                || exit.OrderState == OrderState.Filled;
        }

        private static GlitchAiExecutionResult ReconcileSecondAbsentExitSnapshot(
            Account account,
            Instrument instrument,
            string exitSignal)
        {
            if (!TryFindNamedOrders(account, exitSignal, instrument, out List<Order> confirmation))
                return GlitchAiExecutionResult.Pending("reconcile_exit_visibility_unavailable");
            if (confirmation.Count == 0)
                return GlitchAiExecutionResult.Pending("reconcile_exit_not_found", "native_exit_absent_stable");
            return confirmation.Count == 1
                ? GlitchAiExecutionResult.Pending("reconcile_exit_visibility_changing")
                : GlitchAiExecutionResult.Failed("reconcile_exit_native_identity_ambiguous");
        }

        private static bool TryCollectActiveOwnedExposure(
            Account account,
            Instrument instrument,
            out ActiveOwnedExposure owned,
            out string failure)
        {
            owned = new ActiveOwnedExposure();
            failure = null;
            if (!TrySnapshotOrders(account, out Order[] orders))
            {
                failure = "native_orders_unavailable";
                return false;
            }
            var coverageByCorrelation = new Dictionary<string, OwnedProtectionCoverage>(StringComparer.OrdinalIgnoreCase);
            foreach (Order order in orders)
            {
                if (order == null || !SameInstrument(order.Instrument, instrument)
                    || !GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                    continue;
                bool isStop = TryGetProtectionCorrelation(order.Name, SignalStop, out string correlation);
                bool isTarget = !isStop && TryGetProtectionCorrelation(order.Name, SignalTarget, out correlation);
                if (!isStop && !isTarget)
                    continue;
                int remaining = Math.Max(0, order.Quantity - order.Filled);
                int direction = order.OrderAction == OrderAction.Sell ? 1
                    : order.OrderAction == OrderAction.BuyToCover ? -1 : 0;
                if (remaining <= 0 || direction == 0)
                {
                    failure = "owned_protection_shape_invalid";
                    return false;
                }
                if (!coverageByCorrelation.TryGetValue(correlation, out OwnedProtectionCoverage coverage))
                {
                    coverage = new OwnedProtectionCoverage();
                    coverageByCorrelation[correlation] = coverage;
                }
                if (isStop)
                {
                    coverage.StopCoverage += remaining;
                    coverage.StopDirection = coverage.StopDirection == 0 ? direction : coverage.StopDirection == direction ? direction : int.MinValue;
                }
                else
                {
                    coverage.TargetCoverage += remaining;
                    coverage.TargetDirection = coverage.TargetDirection == 0 ? direction : coverage.TargetDirection == direction ? direction : int.MinValue;
                }
            }
            if (coverageByCorrelation.Count == 0)
            {
                failure = "owned_protection_missing";
                return false;
            }
            foreach (KeyValuePair<string, OwnedProtectionCoverage> item in coverageByCorrelation)
            {
                OwnedProtectionCoverage coverage = item.Value;
                if (coverage.StopCoverage <= 0 || coverage.StopCoverage != coverage.TargetCoverage
                    || coverage.StopDirection == 0 || coverage.StopDirection != coverage.TargetDirection
                    || coverage.StopDirection == int.MinValue)
                {
                    failure = "owned_protection_incomplete";
                    return false;
                }
                if (owned.Direction == 0)
                    owned.Direction = coverage.StopDirection;
                else if (owned.Direction != coverage.StopDirection)
                {
                    failure = "owned_protection_direction_mixed";
                    return false;
                }
                owned.Quantity += coverage.StopCoverage;
                owned.Correlations.Add(item.Key);
            }
            return owned.Direction != 0 && owned.Quantity > 0;
        }

        private static bool TryCollectActiveOwnedExposureForCorrelations(
            Account account,
            Instrument instrument,
            ISet<string> correlations,
            out ActiveOwnedExposure owned,
            out string failure)
        {
            owned = new ActiveOwnedExposure();
            failure = null;
            if (correlations == null || correlations.Count == 0 || !TrySnapshotOrders(account, out Order[] orders))
            {
                failure = "native_orders_unavailable";
                return false;
            }
            var coverageByCorrelation = new Dictionary<string, OwnedProtectionCoverage>(StringComparer.OrdinalIgnoreCase);
            foreach (Order order in orders)
            {
                if (order == null || !SameInstrument(order.Instrument, instrument)
                    || !GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                    continue;
                bool isStop = TryGetProtectionCorrelation(order.Name, SignalStop, out string correlation);
                bool isTarget = !isStop && TryGetProtectionCorrelation(order.Name, SignalTarget, out correlation);
                if ((!isStop && !isTarget) || !correlations.Contains(correlation))
                    continue;
                int remaining = Math.Max(0, order.Quantity - order.Filled);
                int direction = order.OrderAction == OrderAction.Sell ? 1
                    : order.OrderAction == OrderAction.BuyToCover ? -1 : 0;
                if (remaining <= 0 || direction == 0)
                {
                    failure = "owned_protection_shape_invalid";
                    return false;
                }
                if (!coverageByCorrelation.TryGetValue(correlation, out OwnedProtectionCoverage coverage))
                {
                    coverage = new OwnedProtectionCoverage();
                    coverageByCorrelation[correlation] = coverage;
                }
                if (isStop)
                {
                    coverage.StopCoverage += remaining;
                    coverage.StopDirection = coverage.StopDirection == 0 ? direction : coverage.StopDirection == direction ? direction : int.MinValue;
                }
                else
                {
                    coverage.TargetCoverage += remaining;
                    coverage.TargetDirection = coverage.TargetDirection == 0 ? direction : coverage.TargetDirection == direction ? direction : int.MinValue;
                }
            }
            if (coverageByCorrelation.Count != correlations.Count)
            {
                failure = coverageByCorrelation.Count == 0 ? "owned_protection_missing" : "owned_protection_correlation_missing";
                return false;
            }
            foreach (KeyValuePair<string, OwnedProtectionCoverage> item in coverageByCorrelation)
            {
                OwnedProtectionCoverage coverage = item.Value;
                if (coverage.StopCoverage <= 0 || coverage.StopCoverage != coverage.TargetCoverage
                    || coverage.StopDirection == 0 || coverage.StopDirection != coverage.TargetDirection
                    || coverage.StopDirection == int.MinValue)
                {
                    failure = "owned_protection_incomplete";
                    return false;
                }
                if (owned.Direction == 0)
                    owned.Direction = coverage.StopDirection;
                else if (owned.Direction != coverage.StopDirection)
                {
                    failure = "owned_protection_direction_mixed";
                    return false;
                }
                owned.Quantity += coverage.StopCoverage;
                owned.Correlations.Add(item.Key);
            }
            return owned.Direction != 0 && owned.Quantity > 0;
        }

        private static bool ExitPlanMatchesNativeOrder(
            GlitchAiExitOwnershipPlan plan,
            Account account,
            Instrument instrument,
            string exitSignal,
            Order exit)
        {
            if (plan == null || exit == null || plan.Quantity != exit.Quantity
                || !string.Equals(plan.AccountName, account.Name, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(plan.InstrumentName, instrument.FullName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(plan.ExitSignal, exitSignal, StringComparison.OrdinalIgnoreCase))
                return false;
            return plan.Direction == 1 ? exit.OrderAction == OrderAction.Sell
                : plan.Direction == -1 && exit.OrderAction == OrderAction.BuyToCover;
        }

        private static bool TryGetProtectionCorrelation(string signalName, string prefix, out string correlation)
        {
            correlation = null;
            string marker = prefix + "-";
            if (string.IsNullOrWhiteSpace(signalName)
                || !signalName.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                return false;
            string[] parts = signalName.Substring(marker.Length).Split('-');
            if (parts.Length < 2 || parts.Length > 3
                || string.IsNullOrWhiteSpace(parts[0])
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                || (parts.Length == 3 && !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                return false;
            correlation = parts[0];
            return true;
        }

        private static bool TryCancelActiveOwnedProtection(Account account, Instrument instrument, ISet<string> correlations)
        {
            if (correlations == null || correlations.Count == 0 || !TrySnapshotOrders(account, out Order[] orders))
                return false;
            foreach (Order order in orders.Where(order => order != null
                && SameInstrument(order.Instrument, instrument)
                && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                && ((TryGetProtectionCorrelation(order.Name, SignalStop, out string stopCorrelation) && correlations.Contains(stopCorrelation))
                    || (TryGetProtectionCorrelation(order.Name, SignalTarget, out string targetCorrelation) && correlations.Contains(targetCorrelation)))))
            {
                try
                {
                    account.Cancel(new[] { order });
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ArePlannedProtectionOrdersTerminalOrAbsent(Account account, Instrument instrument, ISet<string> correlations)
        {
            if (correlations == null || correlations.Count == 0 || !TrySnapshotOrders(account, out Order[] orders))
                return false;
            return orders.Where(order => order != null
                    && SameInstrument(order.Instrument, instrument)
                    && ((TryGetProtectionCorrelation(order.Name, SignalStop, out string stopCorrelation) && correlations.Contains(stopCorrelation))
                        || (TryGetProtectionCorrelation(order.Name, SignalTarget, out string targetCorrelation) && correlations.Contains(targetCorrelation))))
                .All(IsTerminalTrackedOrder);
        }

        private static void RegisterGroup(ExecutionGroupContext group)
        {
            lock (GroupSync)
            {
                foreach (Order order in group.Orders)
                {
                    if (order != null && !string.IsNullOrWhiteSpace(order.Name))
                        GroupsBySignal[order.Name.Trim()] = group;
                }
            }
        }

        private static void RecoverGroup(ExecutionGroupContext group, string trigger)
        {
            if (group == null)
                return;

            bool firstRecovery;
            lock (GroupSync)
            {
                firstRecovery = !group.RecoveryStarted;
                group.RecoveryStarted = true;
            }

            // NinjaTrader reports several updates for every rejected sibling order.
            // Only the callback that first enters recovery may cancel/flatten the group.
            if (!firstRecovery)
            {
                TryCompleteGroup(group);
                return;
            }

            for (int i = 0; i < group.Orders.Count; i++)
            {
                Order order = group.Orders[i];
                if (order == null)
                    continue;
                Account account = group.Accounts[Math.Min(i / GetOrderStride(group), group.Accounts.Count - 1)];
                TryCancel(account, order);
            }

            foreach (Account account in group.Accounts)
            {
                if (account == null)
                    continue;

                int accountIndex = group.Accounts.IndexOf(account);
                string result = TryCloseAttributableEntryDelta(group, accountIndex, trigger);
                if (firstRecovery || !string.Equals(result, "flat_no_exposure", StringComparison.Ordinal))
                {
                    GlitchAiExecutionJournalWriter.TryAppend(
                        group.IntentId,
                        GlitchAiExecutionResult.Failed(
                            "group_recovery_account",
                            "group=" + CleanToken(group.GroupId)
                                + "|correlation=" + group.Correlation
                                + "|trigger=" + CleanToken(trigger)
                                + "|account=" + CleanToken(account.Name)
                                + "|result=" + result
                                + "|scope=uuid_owned_entry_delta"),
                        DateTime.UtcNow);
                }
            }

            TryCompleteGroup(group);
        }

        private static void TryRecoverLateFill(ExecutionGroupContext group, int accountIndex, string trigger)
        {
            if (group == null || !group.RecoveryStarted
                || accountIndex < 0 || accountIndex >= group.Accounts.Count
                || !HasFilledEntry(group, accountIndex))
                return;

            string result = TryCloseAttributableEntryDelta(group, accountIndex, trigger);
            GlitchAiExecutionJournalWriter.TryAppend(
                group.IntentId,
                GlitchAiExecutionResult.Failed(
                    "group_recovery_late_fill_reconciled",
                    "group=" + CleanToken(group.GroupId)
                        + "|correlation=" + group.Correlation
                        + "|trigger=" + CleanToken(trigger)
                        + "|account=" + CleanToken(group.Accounts[accountIndex].Name)
                        + "|result=" + result),
                DateTime.UtcNow);
        }

        private static string TryCloseAttributableEntryDelta(ExecutionGroupContext group, int accountIndex, string trigger)
        {
            if (group == null || accountIndex < 0 || accountIndex >= group.Accounts.Count
                || !HasFilledEntry(group, accountIndex))
                return "flat_no_exposure";
            Account account = group.Accounts[accountIndex];
            int offset = accountIndex * GetOrderStride(group);
            Order entry = group.Orders[offset];
            if (!GlitchAiEntryBaselinePlanStore.TryLoad(group.IntentId, accountIndex, out GlitchAiEntryBaselinePlan plan))
                return "entry_baseline_plan_unavailable";
            // Live callbacks use the same durable plan and UUID close identity as
            // restart reconciliation, including baseline+fill ownership proof.
            if (HasOwnedCorrelationProtection(account, group.Instrument, group.Correlation))
            {
                var correlationSet = new HashSet<string>(new[] { group.Correlation }, StringComparer.OrdinalIgnoreCase);
                if (!IsNativeOrderVisibilityReady(account))
                    return "entry_recovery_visibility_unavailable";
                if (!TryCancelActiveOwnedProtection(account, group.Instrument, correlationSet))
                    return "entry_recovery_protection_cancel_request_failed";
                if (!ArePlannedProtectionOrdersTerminalOrAbsent(account, group.Instrument, correlationSet))
                    return "entry_recovery_protection_cancel_pending";
            }
            return TryCloseReconciledEntryDelta(account, group.Instrument, entry, group.Correlation, plan).Code;
        }

        private static void TryCompleteGroup(ExecutionGroupContext group)
        {
            if (group == null)
                return;

            TryRecordFilledAndProtected(group);

            bool terminal;
            if (group.RecoveryStarted)
            {
                terminal = true;
                for (int i = 0; i < group.Accounts.Count; i++)
                {
                    if (!IsAccountRecoveryTerminal(group, i))
                    {
                        terminal = false;
                        break;
                    }
                }

                if (!terminal)
                {
                    bool recordUnresolved;
                    lock (GroupSync)
                    {
                        recordUnresolved = !group.RecoveryUnresolvedRecorded;
                        group.RecoveryUnresolvedRecorded = true;
                    }
                    if (recordUnresolved)
                    {
                        string message = "AI group recovery unresolved; inspect and flatten/protect all members. correlation=" + group.Correlation;
                        RaiseCritical?.Invoke("System", message, "AiGroupRecovery|" + group.Correlation);
                        GlitchAiExecutionJournalWriter.TryAppend(
                            group.IntentId,
                            GlitchAiExecutionResult.Failed("group_recovery_unresolved", message),
                            DateTime.UtcNow);
                    }
                    return;
                }

                bool recordTerminal;
                lock (GroupSync)
                {
                    recordTerminal = !group.RecoveryTerminalRecorded;
                    group.RecoveryTerminalRecorded = true;
                }
                if (recordTerminal)
                {
                    GlitchAiExecutionResult recoveryTerminal = GlitchAiExecutionResult.Failed(
                            "group_recovery_terminal",
                            "group=" + CleanToken(group.GroupId) + "|correlation=" + group.Correlation + "|state=flat_and_orders_terminal");
                    GlitchAiExecutionJournalWriter.TryAppend(group.IntentId, recoveryTerminal, DateTime.UtcNow);
                    GlitchAiIntentStateStore.TryFinalizeNonterminal(group.IntentId, recoveryTerminal, out _);
                }
            }
            else
            {
                terminal = group.Orders.All(IsTerminalTrackedOrder);
                if (!terminal)
                    return;

                bool recordTradeClosed;
                lock (GroupSync)
                {
                    recordTradeClosed = group.EntryFilledRecorded && !group.TradeClosedRecorded;
                    group.TradeClosedRecorded |= recordTradeClosed;
                }
                if (recordTradeClosed)
                {
                    GlitchAiExecutionJournalWriter.TryAppend(
                        group.IntentId,
                        GlitchAiExecutionResult.Succeeded(
                            "group_trade_closed",
                            BuildGroupEvidenceMessage(group) + "|state=flat_and_orders_terminal"),
                        DateTime.UtcNow);
                }
            }

            lock (GroupSync)
            {
                foreach (Order order in group.Orders)
                {
                    if (order != null && !string.IsNullOrWhiteSpace(order.Name))
                        GroupsBySignal.Remove(order.Name.Trim());
                }
            }
        }

        private static void TryRecordFilledAndProtected(ExecutionGroupContext group)
        {
            if (group == null)
                return;

            bool masterEntryFilled = false;
            bool allEntriesFilled = true;
            bool allProtected = !group.RecoveryStarted;
            for (int accountIndex = 0; accountIndex < group.Accounts.Count; accountIndex++)
            {
                int offset = accountIndex * GetOrderStride(group);
                if (offset + (group.ProtectionLegs.Count * 2) >= group.Orders.Count)
                    return;

                Order entry = group.Orders[offset];
                bool entryFilled = entry != null
                    && entry.OrderState == OrderState.Filled
                    && entry.Quantity > 0
                    && entry.Filled == entry.Quantity;
                if (accountIndex == 0)
                    masterEntryFilled = entryFilled;
                allEntriesFilled &= entryFilled;
                bool everyBracketWorking = true;
                for (int legIndex = 0; legIndex < group.ProtectionLegs.Count; legIndex++)
                {
                    Order stop = group.Orders[offset + 1 + (legIndex * 2)];
                    Order target = group.Orders[offset + 2 + (legIndex * 2)];
                    everyBracketWorking &= stop != null && target != null
                        && GlitchReplicationEngine.IsWorkingOrderState(stop.OrderState)
                        && GlitchReplicationEngine.IsWorkingOrderState(target.OrderState);
                }
                allProtected &= entryFilled
                    && HasExactOwnedExposure(group, accountIndex)
                    && everyBracketWorking;
            }

            bool recordEntryFilled;
            lock (GroupSync)
            {
                recordEntryFilled = masterEntryFilled && !group.EntryFilledRecorded;
                group.EntryFilledRecorded |= recordEntryFilled;
            }
            if (recordEntryFilled)
            {
                GlitchAiExecutionJournalWriter.TryAppend(
                    group.IntentId,
                    GlitchAiExecutionResult.Succeeded(
                        "group_entry_filled",
                        BuildGroupEvidenceMessage(group) + "|state=master_entry_filled"),
                    DateTime.UtcNow);
            }

            bool recordOpenProtected;
            lock (GroupSync)
            {
                recordOpenProtected = allEntriesFilled && allProtected && !group.OpenProtectedRecorded;
                group.OpenProtectedRecorded |= recordOpenProtected;
            }
            if (recordOpenProtected)
            {
                GlitchAiExecutionResult openProtected = GlitchAiExecutionResult.Succeeded(
                        "group_entry_open_protected",
                        BuildGroupEvidenceMessage(group) + "|state=positions_exact_and_brackets_working");
                GlitchAiExecutionJournalWriter.TryAppend(group.IntentId, openProtected, DateTime.UtcNow);
                GlitchAiIntentStateStore.TryFinalizeNonterminal(group.IntentId, openProtected, out _);
            }
        }

        private static string BuildGroupEvidenceMessage(ExecutionGroupContext group)
        {
            return "group=" + CleanToken(group.GroupId)
                + "|correlation=" + group.Correlation
                + "|master=" + CleanToken(group.Accounts == null || group.Accounts.Count == 0 || group.Accounts[0] == null
                    ? string.Empty
                    : group.Accounts[0].Name)
                + "|instrument=" + CleanToken(group.Instrument == null || group.Instrument.MasterInstrument == null
                    ? string.Empty
                    : group.Instrument.MasterInstrument.Name)
                + "|contract=" + CleanToken(group.Instrument == null ? string.Empty : group.Instrument.FullName)
                + "|accounts=" + string.Join(",", group.Accounts.Select(account => CleanToken(account == null ? string.Empty : account.Name)));
        }

        private static bool IsAccountRecoveryTerminal(ExecutionGroupContext group, int accountIndex)
        {
            Account account = group.Accounts[accountIndex];
            if (account == null)
                return true;
            int orderOffset = accountIndex * GetOrderStride(group);
            if (orderOffset + (group.ProtectionLegs.Count * 2) >= group.Orders.Count)
                return false;
            Order entry = group.Orders[orderOffset];
            // Protection slots are intentionally null until the entry fills. An entry
            // reject or a protection-construction failure must still be able to reach a
            // terminal recovery state after the account is flat.
            bool entrySubmissionStarted = group.EntrySubmissionStarted != null
                && accountIndex < group.EntrySubmissionStarted.Length
                && group.EntrySubmissionStarted[accountIndex];
            bool entryHasFill = entry != null && entry.Filled > 0;
            bool allOrdersTerminal = entry != null
                && (!entrySubmissionStarted || IsTerminalTrackedOrder(entry))
                && group.Orders.Skip(orderOffset + 1).Take(group.ProtectionLegs.Count * 2)
                    .All(order => order == null || IsTerminalTrackedOrder(order))
                && (!entryHasFill || IsDurableRecoveryCloseTerminal(group, accountIndex, entry));
            return GlitchReplicationEngine.IsAccountFlat(account) && allOrdersTerminal;
        }

        private static bool IsDurableRecoveryCloseTerminal(ExecutionGroupContext group, int accountIndex, Order entry)
        {
            if (group == null || entry == null || !GlitchAiEntryBaselinePlanStore.TryLoad(group.IntentId, accountIndex, out GlitchAiEntryBaselinePlan plan))
                return false;
            string closeSignal = BuildSignalName(SignalExit, group.Correlation, accountIndex);
            Account account = group.Accounts[accountIndex];
            if (!TryFindNamedOrders(account, closeSignal, group.Instrument, out List<Order> closes) || closes.Count != 1)
                return false;
            if (closes[0].OrderState != OrderState.Filled || !TryGetNetPosition(account, group.Instrument, out int net))
                return false;
            return net == plan.BaselineNet
                && HasExactBaselineProtection(account, group.Instrument, plan)
                && ArePlannedProtectionOrdersTerminalOrAbsent(account, group.Instrument,
                    new HashSet<string>(new[] { group.Correlation }, StringComparer.OrdinalIgnoreCase));
        }

        private static bool HasExactEntryExposure(ExecutionGroupContext group, int accountIndex)
        {
            if (!HasFilledEntry(group, accountIndex))
                return false;

            int offset = accountIndex * GetOrderStride(group);
            Account account = group.Accounts[accountIndex];
            Order entry = group.Orders[offset];
            int entryDirection = entry.OrderAction == OrderAction.Buy ? 1
                : entry.OrderAction == OrderAction.SellShort ? -1 : 0;
            if (!TryGetNetPosition(account, group.Instrument, out int actualNet))
                return false;
            if (!GlitchAiEntryBaselinePlanStore.TryLoad(group.IntentId, accountIndex, out GlitchAiEntryBaselinePlan plan)
                || !EntryPlanMatchesNativeOrder(plan, account, group.Instrument, entry, entryDirection))
                return false;
            return entryDirection != 0
                && ReferenceEquals(entry.Account, account)
                && SameInstrument(entry.Instrument, group.Instrument)
                && entry.Name == BuildSignalName(SignalEntry, group.Correlation, accountIndex)
                && actualNet == plan.BaselineNet + (entryDirection * entry.Filled)
                && HasExactBaselineProtection(account, group.Instrument, plan)
                && HasExactCorrelationOwnedProtection(
                    account,
                    group.Instrument,
                    group.Correlation,
                    entry.Filled,
                    entryDirection);
        }

        private static bool TryPrepareEntryBaselinePlan(
            string intentId,
            int accountIndex,
            Account account,
            Instrument instrument,
            string entrySignal,
            int entryDirection,
            int observedNet,
            out string failure)
        {
            failure = null;
            if (GlitchAiEntryBaselinePlanStore.TryLoad(intentId, accountIndex, out GlitchAiEntryBaselinePlan existing))
            {
                if (!EntryPlanMatchesIdentity(existing, account, instrument, entrySignal, entryDirection)
                    || observedNet != existing.BaselineNet
                    || !HasExactBaselineProtection(account, instrument, existing))
                {
                    failure = "entry_baseline_superseded_manual_or_concurrent_intent";
                    return false;
                }
                return true;
            }

            var correlations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int protectionQuantity = 0;
            if (observedNet != 0)
            {
                if (!TryCollectActiveOwnedExposure(account, instrument, out ActiveOwnedExposure baseline, out string baselineFailure)
                    || baseline.Direction != entryDirection || baseline.Quantity != Math.Abs(observedNet))
                {
                    failure = "entry_baseline_" + CleanToken(baselineFailure);
                    return false;
                }
                correlations = new HashSet<string>(baseline.Correlations, StringComparer.OrdinalIgnoreCase);
                protectionQuantity = baseline.Quantity;
            }
            var plan = new GlitchAiEntryBaselinePlan
            {
                IntentId = intentId,
                AccountIndex = accountIndex,
                AccountName = account.Name,
                InstrumentName = instrument.FullName,
                EntrySignal = entrySignal,
                EntryDirection = entryDirection,
                BaselineNet = observedNet,
                BaselineProtectionQuantity = protectionQuantity,
                BaselineCorrelations = correlations
            };
            if (!GlitchAiEntryBaselinePlanStore.TryPersist(plan, out failure))
                return false;
            return true;
        }

        private static bool EntryPlanMatchesNativeOrder(
            GlitchAiEntryBaselinePlan plan,
            Account account,
            Instrument instrument,
            Order entry,
            int entryDirection)
        {
            return entry != null
                && EntryPlanMatchesIdentity(plan, account, instrument, entry.Name, entryDirection)
                && string.Equals(plan.EntrySignal, entry.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static bool EntryPlanMatchesIdentity(
            GlitchAiEntryBaselinePlan plan,
            Account account,
            Instrument instrument,
            string entrySignal,
            int entryDirection)
        {
            return plan != null && account != null && instrument != null
                && string.Equals(plan.AccountName, account.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(plan.InstrumentName, instrument.FullName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(plan.EntrySignal, entrySignal, StringComparison.OrdinalIgnoreCase)
                && plan.EntryDirection == entryDirection;
        }

        private static bool HasExactBaselineProtection(Account account, Instrument instrument, GlitchAiEntryBaselinePlan plan)
        {
            if (plan == null)
                return false;
            if (plan.BaselineProtectionQuantity == 0)
                return plan.BaselineNet == 0 && plan.BaselineCorrelations.Count == 0;
            return TryCollectActiveOwnedExposureForCorrelations(account, instrument, plan.BaselineCorrelations, out ActiveOwnedExposure exposure, out _)
                && exposure.Direction == plan.EntryDirection
                && exposure.Quantity == plan.BaselineProtectionQuantity;
        }

        private static bool HasExactCorrelationOwnedProtection(
            Account account,
            Instrument instrument,
            string correlation,
            int expectedQuantity,
            int expectedDirection)
        {
            if (expectedQuantity <= 0 || expectedDirection == 0
                || !TrySnapshotOrders(account, out Order[] orders))
                return false;
            int stopCoverage = 0;
            int targetCoverage = 0;
            foreach (Order order in orders)
            {
                if (order == null || !SameInstrument(order.Instrument, instrument)
                    || !GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                    continue;
                bool isStop = TryGetProtectionCorrelation(order.Name, SignalStop, out string orderCorrelation);
                bool isTarget = !isStop && TryGetProtectionCorrelation(order.Name, SignalTarget, out orderCorrelation);
                if ((!isStop && !isTarget)
                    || !string.Equals(orderCorrelation, correlation, StringComparison.OrdinalIgnoreCase))
                    continue;
                int direction = order.OrderAction == OrderAction.Sell ? 1
                    : order.OrderAction == OrderAction.BuyToCover ? -1 : 0;
                if (direction != expectedDirection)
                    return false;
                if (isStop)
                    stopCoverage += Math.Max(0, order.Quantity - order.Filled);
                else
                    targetCoverage += Math.Max(0, order.Quantity - order.Filled);
            }
            return stopCoverage == expectedQuantity && targetCoverage == expectedQuantity;
        }

        private static bool HasFilledEntry(ExecutionGroupContext group, int accountIndex)
        {
            if (group == null || accountIndex < 0 || accountIndex >= group.Accounts.Count)
                return false;
            int offset = accountIndex * GetOrderStride(group);
            if (offset < 0 || offset >= group.Orders.Count)
                return false;
            Order entry = group.Orders[offset];
            Account account = group.Accounts[accountIndex];
            return account != null
                && entry != null
                && entry.Filled > 0
                && entry.Account != null
                && string.Equals(entry.Account.Name, account.Name, StringComparison.OrdinalIgnoreCase)
                && SameInstrument(entry.Instrument, group.Instrument)
                && entry.Name == BuildSignalName(SignalEntry, group.Correlation, accountIndex);
        }

        private static bool HasExactOwnedExposure(ExecutionGroupContext group, int accountIndex)
        {
            if (group == null || accountIndex < 0 || accountIndex >= group.Accounts.Count)
                return false;
            int offset = accountIndex * GetOrderStride(group);
            if (offset + (group.ProtectionLegs.Count * 2) >= group.Orders.Count)
                return false;
            Account account = group.Accounts[accountIndex];
            Order entry = group.Orders[offset];
            if (account == null || entry == null
                || !HasExactEntryExposure(group, accountIndex))
                return false;
            int stopCoverage = 0;
            int targetCoverage = 0;
            for (int legIndex = 0; legIndex < group.ProtectionLegs.Count; legIndex++)
            {
                Order stop = group.Orders[offset + 1 + (legIndex * 2)];
                Order target = group.Orders[offset + 2 + (legIndex * 2)];
                if (stop == null || target == null
                    || !ReferenceEquals(stop.Account, account)
                    || !ReferenceEquals(target.Account, account)
                    || !SameInstrument(stop.Instrument, group.Instrument)
                    || !SameInstrument(target.Instrument, group.Instrument)
                    || string.IsNullOrWhiteSpace(stop.Oco)
                    || !string.Equals(stop.Oco, target.Oco, StringComparison.Ordinal)
                    || stop.Name != BuildSignalName(SignalStop, group.Correlation, accountIndex, legIndex)
                    || target.Name != BuildSignalName(SignalTarget, group.Correlation, accountIndex, legIndex)
                    || (entry.OrderAction == OrderAction.Buy
                        && (stop.OrderAction != OrderAction.Sell || target.OrderAction != OrderAction.Sell))
                    || (entry.OrderAction == OrderAction.SellShort
                        && (stop.OrderAction != OrderAction.BuyToCover || target.OrderAction != OrderAction.BuyToCover)))
                    return false;
                stopCoverage += stop.Quantity;
                targetCoverage += target.Quantity;
            }

            return ReferenceEquals(entry.Account, account)
                && SameInstrument(entry.Instrument, group.Instrument)
                && stopCoverage == entry.Filled
                && targetCoverage == entry.Filled;
        }

        private static bool TryGetNetPosition(Account account, Instrument instrument, out int net)
        {
            net = 0;
            if (account == null || instrument == null || account.Positions == null)
                return false;
            try
            {
                lock (account.Positions)
                {
                    foreach (Position position in account.Positions)
                    {
                        if (position == null || position.Instrument == null || !SameInstrument(position.Instrument, instrument))
                            continue;
                        if (position.MarketPosition == MarketPosition.Long)
                            net += position.Quantity;
                        else if (position.MarketPosition == MarketPosition.Short)
                            net -= position.Quantity;
                    }
                }
                return true;
            }
            catch
            {
                net = 0;
                return false;
            }
        }

        private static bool TryGetTotalOpenContracts(Account account, out int total)
        {
            total = 0;
            if (account == null || account.Positions == null)
                return false;

            try
            {
                lock (account.Positions)
                {
                    foreach (Position position in account.Positions)
                    {
                        if (position == null || position.MarketPosition == MarketPosition.Flat)
                            continue;
                        total += Math.Abs(position.Quantity);
                    }
                }
                return true;
            }
            catch
            {
                total = 0;
                return false;
            }
        }

        private static bool HasCompleteAiProtection(Account account, Instrument instrument, out List<Order> stops)
        {
            return HasCompleteAiProtection(account, instrument, out stops, out _);
        }

        private static bool HasCompleteAiProtection(
            Account account,
            Instrument instrument,
            out List<Order> stops,
            out List<Order> targets)
        {
            stops = new List<Order>();
            targets = new List<Order>();
            if (account == null || instrument == null
                || !TryGetNetPosition(account, instrument, out int net)
                || !TrySnapshotOrders(account, out Order[] orders))
                return false;
            if (net == 0)
                return false;
            int stopCoverage = 0;
            int targetCoverage = 0;
            foreach (Order order in orders)
            {
                if (order == null || order.Instrument == null || !SameInstrument(order.Instrument, instrument)
                    || !GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                    continue;
                if (IsOwnedStopSignal(order.Name))
                {
                    stops.Add(order);
                    stopCoverage += Math.Max(0, order.Quantity - order.Filled);
                }
                else if (IsOwnedTargetSignal(order.Name))
                {
                    targets.Add(order);
                    targetCoverage += Math.Max(0, order.Quantity - order.Filled);
                }
            }
            return stopCoverage == Math.Abs(net) && targetCoverage == Math.Abs(net);
        }

        private static bool TryHasWorkingNonProtectionOrder(
            Account account,
            Instrument instrument,
            out bool hasWorking)
        {
            hasWorking = false;
            if (!TrySnapshotOrders(account, out Order[] orders))
                return false;
            hasWorking = orders.Any(order => order != null
                && order.Instrument != null
                && SameInstrument(order.Instrument, instrument)
                && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                && !IsOwnedStopSignal(order.Name)
                && !IsOwnedTargetSignal(order.Name));
            return true;
        }

        private static bool TrySnapshotOrders(Account account, out Order[] orders)
        {
            orders = Array.Empty<Order>();
            if (account?.Orders == null)
                return false;
            try
            {
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

        private static bool IsNativeOrderVisibilityReady(Account account)
        {
            try
            {
                // NinjaTrader exposes the order-feed connection state, but no
                // cross-process order-sync-complete token. The server pairs this
                // factual Connected check with two dispatcher snapshots and its
                // durable 30-second delivery settle interval before one resume.
                return account != null
                    && account.Connection != null
                    && account.Connection.Status == ConnectionStatus.Connected;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsOwnedStopSignal(string name)
        {
            return !string.IsNullOrWhiteSpace(name)
                && name.StartsWith(SignalStop + "-", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOwnedTargetSignal(string name)
        {
            return !string.IsNullOrWhiteSpace(name)
                && name.StartsWith(SignalTarget + "-", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTerminalTrackedOrder(Order order)
        {
            if (order == null)
                return false;
            return order.OrderState == OrderState.Filled
                || order.OrderState == OrderState.Cancelled
                || order.OrderState == OrderState.Rejected;
        }

        private static bool SameInstrument(Instrument left, Instrument right)
        {
            return left != null && right != null
                && string.Equals(
                    left.FullName,
                    right.FullName,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildSignalName(string prefix, string correlation, int accountIndex)
        {
            return prefix + "-" + correlation + "-" + accountIndex.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildIntentCorrelation(string intentId)
        {
            if (!Guid.TryParse(intentId, out Guid parsed))
                return "invalidintent";
            return parsed.ToString("N", CultureInfo.InvariantCulture).Substring(0, 16);
        }

        private static string BuildSignalName(string prefix, string correlation, int accountIndex, int legIndex)
        {
            string baseName = BuildSignalName(prefix, correlation, accountIndex);
            return legIndex <= 0
                ? baseName
                : baseName + "-" + legIndex.ToString(CultureInfo.InvariantCulture);
        }

        private static int GetOrderStride(ExecutionGroupContext group)
        {
            int legCount = group == null || group.ProtectionLegs == null
                ? 0
                : group.ProtectionLegs.Count;
            return 1 + (Math.Max(1, legCount) * 2);
        }

        private static Order CreateEntryOrder(Account account, Instrument instrument, OrderAction action, int quantity, string signalName)
        {
            return account.CreateOrder(
                instrument,
                action,
                OrderType.Market,
                OrderEntry.Automated,
                TimeInForce.Day,
                quantity,
                0,
                0,
                string.Empty,
                signalName,
                DateTime.MaxValue,
                null);
        }

        private static Order CreateExitOrder(
            Account account,
            Instrument instrument,
            OrderAction action,
            OrderType orderType,
            int quantity,
            double limitPrice,
            double stopPrice,
            string oco,
            string signalName)
        {
            return account.CreateOrder(
                instrument,
                action,
                orderType,
                OrderEntry.Automated,
                TimeInForce.Gtc,
                quantity,
                limitPrice,
                stopPrice,
                oco,
                signalName,
                DateTime.MaxValue,
                null);
        }

        private static bool IsRejected(Order order)
        {
            return order != null
                && (order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled);
        }

        private static void TryCancel(Account account, Order order)
        {
            if (account == null || order == null)
                return;

            try
            {
                if (GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                    account.Cancel(new[] { order });
            }
            catch
            {
            }
        }

        private static Account FindAccount(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName) || Account.All == null)
                return null;

            lock (Account.All)
            {
                foreach (Account account in Account.All)
                {
                    if (account != null && string.Equals(account.Name, accountName.Trim(), StringComparison.OrdinalIgnoreCase))
                        return account;
                }
            }

            return null;
        }

        private static string CleanToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "none";
            return value.Replace("|", "_").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private sealed class ExecutionGroupContext
        {
            public string Correlation { get; set; }
            public string GroupId { get; set; }
            public string IntentId { get; set; }
            public Instrument Instrument { get; set; }
            public List<Account> Accounts { get; set; }
            public List<Order> Orders { get; set; }
            public bool IsLong { get; set; }
            public List<StructuralProtectionLeg> ProtectionLegs { get; set; }
            public bool[] EntrySubmissionStarted { get; set; }
            public bool[] ProtectionSubmitted { get; set; }
            public bool RecoveryStarted { get; set; }
            public bool RecoveryUnresolvedRecorded { get; set; }
            public bool RecoveryTerminalRecorded { get; set; }
            public bool EntryFilledRecorded { get; set; }
            public bool OpenProtectedRecorded { get; set; }
            public bool TradeClosedRecorded { get; set; }
            public DateTime EntrySubmittedUtc { get; set; }
        }

        private sealed class OwnedExitPlan
        {
            public Account Account { get; set; }
            public Order Order { get; set; }
            public ISet<string> Correlations { get; set; }
            public int Direction { get; set; }
            public int AccountIndex { get; set; }
        }

        private sealed class ActiveOwnedExposure
        {
            public int Direction { get; set; }
            public int Quantity { get; set; }
            public ISet<string> Correlations { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class OwnedProtectionCoverage
        {
            public int StopCoverage { get; set; }
            public int TargetCoverage { get; set; }
            public int StopDirection { get; set; }
            public int TargetDirection { get; set; }
        }

        private sealed class StructuralProtectionLeg
        {
            public int Quantity { get; set; }
            public double StopPrice { get; set; }
            public double TargetPrice { get; set; }
        }

        private sealed class ExecutionGroupMember
        {
            public Account Account { get; set; }
            public int Quantity { get; set; }
        }
    }
}
