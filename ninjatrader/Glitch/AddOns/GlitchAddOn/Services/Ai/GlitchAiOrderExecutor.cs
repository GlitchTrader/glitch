using System;
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
        private static readonly TimeSpan ProtectionReconcileGrace = TimeSpan.FromSeconds(2);

        private static readonly object GroupSync = new object();
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
        }

        public static void ProcessAccountStateUpdate(Account account)
        {
            if (account == null)
                return;

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

                bool protectionSubmitted;
                lock (GroupSync)
                    protectionSubmitted = group.ProtectionSubmitted != null
                        && accountIndex < group.ProtectionSubmitted.Length
                        && group.ProtectionSubmitted[accountIndex];
                if (!group.RecoveryStarted
                    && !protectionSubmitted
                    && DateTime.UtcNow - group.EntrySubmittedUtc >= ProtectionReconcileGrace)
                {
                    if (!TryGetNetPosition(account, group.Instrument, out int netPosition))
                    {
                        GlitchAiExecutionJournalWriter.TryAppend(
                            group.IntentId,
                            GlitchAiExecutionResult.Failed(
                                "master_position_state_unavailable",
                                "account=" + CleanToken(account.Name)
                                    + "|correlation=" + group.Correlation),
                            DateTime.UtcNow);
                        RecoverGroup(group, "position_state_unavailable_" + account.Name);
                        TryCompleteGroup(group);
                        continue;
                    }
                    if (netPosition == 0)
                    {
                        TryCompleteGroup(group);
                        continue;
                    }
                    GlitchAiExecutionJournalWriter.TryAppend(
                        group.IntentId,
                        GlitchAiExecutionResult.Failed(
                            "master_protection_reconcile_timeout",
                            "account=" + CleanToken(account.Name)
                                + "|correlation=" + group.Correlation),
                        DateTime.UtcNow);
                    RecoverGroup(group, "protection_reconcile_timeout_" + account.Name);
                }
                TryCompleteGroup(group);
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

            if (!GlitchAiRiskFirewall.TryComputeTradeRiskUsd(rawJson, instrumentRoot, action, snapshotMarketPrice, out _))
                return GlitchAiExecutionResult.Failed("group_risk_not_computable");

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

            // Hermes assessed absolute geometry at snapshotMarketPrice. Glitch
            // may execute equal or better geometry, but never make the stop
            // farther and the target nearer without a fresh Hermes decision.
            bool liveGeometryWorsened = isLong
                ? liveExecutionPrice > snapshotMarketPrice
                : liveExecutionPrice < snapshotMarketPrice;
            if (liveGeometryWorsened)
            {
                return GlitchAiExecutionResult.Failed(
                    "group_entry_geometry_changed_reassess",
                    "snapshot_price=" + snapshotMarketPrice.ToString(CultureInfo.InvariantCulture)
                        + "|live_price=" + liveExecutionPrice.ToString(CultureInfo.InvariantCulture));
            }

            // Intent v2 prices remain absolute structural levels when live
            // geometry is equal or better and all bracket legs remain executable.
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
            string portfolioAccountJson;
            string portfolioFailure;
            if (!GlitchAiPortfolioSnapshotReader.TryGetFreshRiskState(
                masterAccount.Name,
                DateTime.UtcNow,
                policy.SnapshotMaxAgeSeconds,
                out riskLocked,
                out evalTargetLocked,
                out _,
                out portfolioAccountJson,
                out portfolioFailure))
                return GlitchAiExecutionResult.Failed("master_portfolio_snapshot_invalid", portfolioFailure);
            if (riskLocked)
                return GlitchAiExecutionResult.Failed("master_account_risk_locked", masterAccount.Name);
            if (evalTargetLocked)
                return GlitchAiExecutionResult.Failed("master_account_eval_target_locked", masterAccount.Name);
            if (!GlitchAiJsonFields.TryExtractNumber(portfolioAccountJson, "max_contracts", out double maxContracts)
                || maxContracts < 1)
                return GlitchAiExecutionResult.Failed("master_contract_ceiling_missing", masterAccount.Name);
            int masterContractCeiling = (int)Math.Floor(maxContracts);
            if (!TryGetTotalOpenContracts(masterAccount, out int masterTotalOpenContracts))
                return GlitchAiExecutionResult.Failed("master_positions_unavailable", masterAccount.Name);
            if (masterTotalOpenContracts + masterQuantity > masterContractCeiling)
                return GlitchAiExecutionResult.Failed(
                    "max_contracts_exceeded_at_execution",
                    (masterTotalOpenContracts + masterQuantity).ToString(CultureInfo.InvariantCulture));

            OrderAction entryAction = isLong ? OrderAction.Buy : OrderAction.SellShort;
            if (!GlitchApexDirectionGuard.TryApproveEntry(
                masterAccount,
                instrument,
                isLong ? 1 : -1,
                out string apexDirectionFailure))
            {
                return GlitchAiExecutionResult.Failed(
                    "apex_direction_compliance_rejected",
                    apexDirectionFailure);
            }
            GlitchAiTradingWindowStatus finalTradingWindow = GlitchAiTradingWindow.Evaluate(
                DateTime.UtcNow,
                GlitchAiJsonFields.ExtractString(portfolioAccountJson, "trading_start_time_et"),
                GlitchAiJsonFields.ExtractString(portfolioAccountJson, "trading_end_time_et"));
            if (!finalTradingWindow.IsValid)
                return GlitchAiExecutionResult.Failed("trading_window_unavailable_at_execution", finalTradingWindow.Failure);
            if (!finalTradingWindow.IsEntryAllowed)
                return GlitchAiExecutionResult.Failed("trading_window_closed_at_execution");

            string correlation = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture).Substring(0, 10);
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
                RecoveryFlattenSubmitted = new bool[1],
                EntrySubmittedUtc = DateTime.UtcNow
            };

            string entrySignal = BuildSignalName(SignalEntry, correlation, 0);
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
                    return GlitchAiExecutionResult.Failed("master_submit_recovery_started", masterMember.Account.Name);
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

            return GlitchAiExecutionResult.Succeeded(
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
            if (!string.Equals(protection.Status, "submitted", StringComparison.Ordinal))
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
                || entry.OrderState != OrderState.Filled
                || entry.Filled != entry.Quantity
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
            var failures = new List<string>();
            foreach (Account account in accounts)
            {
                if (!TryGetNetPosition(account, instrument, out int net))
                    return GlitchAiExecutionResult.Failed("group_exit_position_state_unavailable", account.Name);
                if (net == 0)
                    continue;
                if (!HasCompleteAiProtection(account, instrument, out _))
                    return GlitchAiExecutionResult.Failed("group_exit_not_fully_ai_owned", account.Name);
            }

            foreach (Account account in accounts)
            {
                try
                {
                    account.Flatten(new[] { instrument });
                }
                catch (Exception ex)
                {
                    failures.Add(account.Name + ":" + ex.GetType().Name);
                    GlitchAiExecutionJournalWriter.TryAppend(
                        intentId,
                        GlitchAiExecutionResult.Failed("group_exit_account_failed", account.Name + "|" + ex.Message),
                        DateTime.UtcNow);
                }
            }

            if (failures.Count > 0)
                return GlitchAiExecutionResult.Failed("group_exit_partial_failure", string.Join(",", failures));

            return GlitchAiExecutionResult.Succeeded(
                "group_exit_submitted",
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
            if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss", out double requestedStop))
                return GlitchAiExecutionResult.Failed("move_stop_price_missing");
            GlitchInstrumentMetadata metadata;
            if (!GlitchInstrumentMetadataService.TryResolve(instrument, out metadata)
                || metadata == null || !metadata.IsResolved || metadata.TickSize <= 0)
                return GlitchAiExecutionResult.Failed("move_stop_metadata_unavailable");
            double stopPrice = RoundToTick(requestedStop, metadata.TickSize);
            double livePrice;
            string liveFailure;
            if (!TryGetFreshExecutionPrice(instrument, DateTime.UtcNow, out livePrice, out liveFailure))
                return GlitchAiExecutionResult.Failed("move_stop_live_price_invalid", liveFailure);

            if (!TryGetNetPosition(members[0].Account, instrument, out int masterNet))
                return GlitchAiExecutionResult.Failed("move_stop_position_state_unavailable", members[0].Account.Name);
            if (masterNet == 0)
                return GlitchAiExecutionResult.Failed("move_stop_position_flat");
            bool isLong = masterNet > 0;
            if ((isLong && stopPrice >= livePrice) || (!isLong && stopPrice <= livePrice))
                return GlitchAiExecutionResult.Failed("move_stop_market_side_invalid");

            Account masterAccount = members[0].Account;
            if (!HasCompleteAiProtection(masterAccount, instrument, out List<Order> masterStops))
                return GlitchAiExecutionResult.Failed("move_stop_protection_incomplete", masterAccount.Name);
            var changes = masterStops
                .Where(stop => isLong ? stopPrice > stop.StopPrice : stopPrice < stop.StopPrice)
                .Select(stop => Tuple.Create(masterAccount, stop))
                .ToList();

            var failures = new List<string>();
            foreach (IGrouping<Account, Tuple<Account, Order>> accountChanges in changes.GroupBy(item => item.Item1))
            {
                try
                {
                    List<Order> orders = accountChanges.Select(item => item.Item2).ToList();
                    foreach (Order order in orders)
                        order.StopPriceChanged = stopPrice;
                    accountChanges.Key.Change(orders.ToArray());
                }
                catch (Exception ex)
                {
                    failures.Add(accountChanges.Key.Name + ":" + ex.GetType().Name);
                }
            }
            if (failures.Count > 0)
                return GlitchAiExecutionResult.Failed("move_stop_partial_failure", string.Join(",", failures));
            return GlitchAiExecutionResult.Succeeded(
                changes.Count == 0 ? "move_stop_already_tighter" : "move_stop_submitted",
                "group=" + CleanToken(groupId)
                    + "|stop_price=" + stopPrice.ToString(CultureInfo.InvariantCulture)
                    + "|master_orders=" + changes.Count.ToString(CultureInfo.InvariantCulture)
                    + "|followers=replication_engine");
        }

        private static GlitchAiExecutionResult TryExecuteGroupMoveTarget(
            IReadOnlyList<ExecutionGroupMember> members,
            Instrument instrument,
            string rawJson,
            string groupId,
            string intentId)
        {
            if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_1", out double requestedTarget))
                return GlitchAiExecutionResult.Failed("move_tp_price_missing");
            bool hasRequestedStop = GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss", out double requestedStop);
            if (!GlitchInstrumentMetadataService.TryResolve(instrument, out GlitchInstrumentMetadata metadata)
                || metadata == null || !metadata.IsResolved || metadata.TickSize <= 0)
                return GlitchAiExecutionResult.Failed("move_tp_metadata_unavailable");
            double targetPrice = RoundToTick(requestedTarget, metadata.TickSize);
            double stopPrice = hasRequestedStop ? RoundToTick(requestedStop, metadata.TickSize) : 0;
            if (!TryGetFreshExecutionPrice(instrument, DateTime.UtcNow, out double livePrice, out string liveFailure))
                return GlitchAiExecutionResult.Failed("move_tp_live_price_invalid", liveFailure);

            Account masterAccount = members[0].Account;
            if (!TryGetNetPosition(masterAccount, instrument, out int masterNet))
                return GlitchAiExecutionResult.Failed("move_tp_position_state_unavailable", masterAccount.Name);
            if (masterNet == 0)
                return GlitchAiExecutionResult.Failed("move_tp_position_flat");
            bool isLong = masterNet > 0;
            if ((isLong && targetPrice <= livePrice) || (!isLong && targetPrice >= livePrice))
                return GlitchAiExecutionResult.Failed("move_tp_market_side_invalid");
            if (hasRequestedStop && ((isLong && stopPrice >= livePrice) || (!isLong && stopPrice <= livePrice)))
                return GlitchAiExecutionResult.Failed("move_tp_stop_market_side_invalid");
            if (!HasCompleteAiProtection(masterAccount, instrument, out List<Order> masterStops, out List<Order> masterTargets))
                return GlitchAiExecutionResult.Failed("move_tp_protection_incomplete", masterAccount.Name);

            List<Order> targetChanges = masterTargets
                .Where(target => Math.Abs(target.LimitPrice - targetPrice) > 0.0000001d)
                .ToList();
            List<Order> stopChanges = hasRequestedStop
                ? masterStops.Where(stop => isLong ? stopPrice > stop.StopPrice : stopPrice < stop.StopPrice).ToList()
                : new List<Order>();
            List<Order> changes = targetChanges.Concat(stopChanges).ToList();
            if (changes.Count == 0)
            {
                return GlitchAiExecutionResult.Succeeded(
                    "move_tp_already_set",
                    "group=" + CleanToken(groupId)
                        + "|target_price=" + targetPrice.ToString(CultureInfo.InvariantCulture)
                        + "|followers=replication_engine");
            }

            try
            {
                foreach (Order target in targetChanges)
                    target.LimitPriceChanged = targetPrice;
                foreach (Order stop in stopChanges)
                    stop.StopPriceChanged = stopPrice;
                masterAccount.Change(changes.ToArray());
            }
            catch (Exception ex)
            {
                return GlitchAiExecutionResult.Failed("move_tp_change_failed", masterAccount.Name + ":" + ex.GetType().Name);
            }

            return GlitchAiExecutionResult.Succeeded(
                stopChanges.Count > 0 ? "move_tp_and_stop_submitted" : "move_tp_submitted",
                "group=" + CleanToken(groupId)
                    + "|target_price=" + targetPrice.ToString(CultureInfo.InvariantCulture)
                    + "|stop_price=" + (stopChanges.Count > 0 ? stopPrice.ToString(CultureInfo.InvariantCulture) : "unchanged")
                    + "|master_target_orders=" + targetChanges.Count.ToString(CultureInfo.InvariantCulture)
                    + "|master_stop_orders=" + stopChanges.Count.ToString(CultureInfo.InvariantCulture)
                    + "|followers=replication_engine");
        }

        private static Order FindNamedOrder(Account account, string name, Instrument instrument)
        {
            if (string.IsNullOrWhiteSpace(name)
                || !TrySnapshotOrders(account, out Order[] orders))
                return null;

            List<Order> matches = orders.Where(order => order != null
                && string.Equals(order.Name, name, StringComparison.OrdinalIgnoreCase)
                && order.Account != null
                && string.Equals(order.Account.Name, account.Name, StringComparison.OrdinalIgnoreCase)
                && SameInstrument(order.Instrument, instrument)).ToList();
            return matches.Count == 1 ? matches[0] : null;
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

                int flattened = 0;
                string result = "flat_no_exposure";
                try
                {
                    if (HasFilledEntry(group, group.Accounts.IndexOf(account)))
                    {
                        int accountIndex = group.Accounts.IndexOf(account);
                        lock (GroupSync)
                            group.RecoveryFlattenSubmitted[accountIndex] = true;
                        account.Flatten(new[] { group.Instrument });
                        flattened = 1;
                        result = "instrument_flatten_issued";
                    }
                }
                catch (Exception ex)
                {
                    lock (GroupSync)
                        group.RecoveryFlattenSubmitted[group.Accounts.IndexOf(account)] = false;
                    result = "flatten_failed_" + ex.GetType().Name;
                }

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
                                + "|instruments=" + flattened.ToString(CultureInfo.InvariantCulture)),
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

            lock (GroupSync)
            {
                if (group.RecoveryFlattenSubmitted != null
                    && accountIndex < group.RecoveryFlattenSubmitted.Length
                    && group.RecoveryFlattenSubmitted[accountIndex])
                    return;
                group.RecoveryFlattenSubmitted[accountIndex] = true;
            }

            Account account = group.Accounts[accountIndex];
            try
            {
                account.Flatten(new[] { group.Instrument });
                GlitchAiExecutionJournalWriter.TryAppend(
                    group.IntentId,
                    GlitchAiExecutionResult.Failed(
                        "group_recovery_late_fill_flattened",
                        "group=" + CleanToken(group.GroupId)
                            + "|correlation=" + group.Correlation
                            + "|trigger=" + CleanToken(trigger)
                            + "|account=" + CleanToken(account.Name)),
                    DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                lock (GroupSync)
                    group.RecoveryFlattenSubmitted[accountIndex] = false;
                GlitchAiExecutionJournalWriter.TryAppend(
                    group.IntentId,
                    GlitchAiExecutionResult.Failed(
                        "group_recovery_late_fill_flatten_failed",
                        CleanToken(account.Name) + "|" + ex.GetType().Name),
                    DateTime.UtcNow);
            }
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
                    GlitchAiExecutionJournalWriter.TryAppend(
                        group.IntentId,
                        GlitchAiExecutionResult.Succeeded(
                            "group_recovery_terminal",
                            "group=" + CleanToken(group.GroupId) + "|correlation=" + group.Correlation + "|state=flat_and_orders_terminal"),
                        DateTime.UtcNow);
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
                GlitchAiExecutionJournalWriter.TryAppend(
                    group.IntentId,
                    GlitchAiExecutionResult.Succeeded(
                        "group_entry_open_protected",
                        BuildGroupEvidenceMessage(group) + "|state=positions_exact_and_brackets_working"),
                    DateTime.UtcNow);
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
            bool recoveryFlattenSubmitted = group.RecoveryFlattenSubmitted != null
                && accountIndex < group.RecoveryFlattenSubmitted.Length
                && group.RecoveryFlattenSubmitted[accountIndex];
            bool allOrdersTerminal = entry != null
                && (!entrySubmissionStarted || IsTerminalTrackedOrder(entry))
                && group.Orders.Skip(orderOffset + 1).Take(group.ProtectionLegs.Count * 2)
                    .All(order => order == null || IsTerminalTrackedOrder(order))
                && (!entryHasFill || recoveryFlattenSubmitted);
            return GlitchReplicationEngine.IsAccountFlat(account) && allOrdersTerminal;
        }

        private static bool HasExactEntryExposure(ExecutionGroupContext group, int accountIndex)
        {
            if (!HasFilledEntry(group, accountIndex))
                return false;

            int offset = accountIndex * GetOrderStride(group);
            Account account = group.Accounts[accountIndex];
            Order entry = group.Orders[offset];
            int expectedNet = entry.OrderAction == OrderAction.Buy
                ? entry.Filled
                : entry.OrderAction == OrderAction.SellShort
                    ? -entry.Filled
                    : 0;
            if (!TryGetNetPosition(account, group.Instrument, out int actualNet))
                return false;
            return expectedNet != 0
                && ReferenceEquals(entry.Account, account)
                && SameInstrument(entry.Instrument, group.Instrument)
                && entry.Name == BuildSignalName(SignalEntry, group.Correlation, accountIndex)
                && Math.Sign(actualNet) == Math.Sign(expectedNet)
                && Math.Abs(actualNet) >= Math.Abs(expectedNet);
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
                    stopCoverage += order.Quantity;
                }
                else if (IsOwnedTargetSignal(order.Name))
                {
                    targets.Add(order);
                    targetCoverage += order.Quantity;
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
            public bool[] RecoveryFlattenSubmitted { get; set; }
            public bool RecoveryStarted { get; set; }
            public bool RecoveryUnresolvedRecorded { get; set; }
            public bool RecoveryTerminalRecorded { get; set; }
            public bool EntryFilledRecorded { get; set; }
            public bool OpenProtectedRecorded { get; set; }
            public bool TradeClosedRecorded { get; set; }
            public DateTime EntrySubmittedUtc { get; set; }
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
