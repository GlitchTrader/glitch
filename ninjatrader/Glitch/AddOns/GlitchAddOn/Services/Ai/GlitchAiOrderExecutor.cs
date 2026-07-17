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

        private static readonly object GroupSync = new object();
        private static readonly Dictionary<string, ExecutionGroupContext> GroupsBySignal =
            new Dictionary<string, ExecutionGroupContext>(StringComparer.OrdinalIgnoreCase);

        public static Func<Func<GlitchAiExecutionResult>, GlitchAiExecutionResult> UiInvoke;
        public static Action<string, string, string> RaiseCritical;
        public static Func<Account, Instrument, OrderAction, int, string> GetReplicationEntryDenialReason;

        public static GlitchAiExecutionResult TryExecuteApprovedIntent(string rawJson, DateTime nowUtc)
        {
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            if (!IsExecutionEnabled(policy))
            {
                return GlitchAiExecutionResult.Skipped(
                    "executor_mode_" + (policy.Mode ?? "paper"),
                    "trading is off or the execution mode is unsupported");
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
            if (TryGetEntryAccountIndex(group, order, out entryAccountIndex)
                && order.Filled > 0
                && order.OrderState != OrderState.Filled)
            {
                // A multi-contract market entry can fill in several native
                // executions. Aggregate while the entry remains working and build
                // the structural bracket once the terminal Filled state arrives.
                // A terminal incomplete entry cannot use the full requested plan
                // and therefore takes the existing one-shot recovery path.
                if (GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                    return;

                GlitchAiExecutionJournalWriter.TryAppend(
                    group.IntentId,
                    GlitchAiExecutionResult.Failed(
                        "group_terminal_partial_entry_fill_recovery",
                        "account=" + CleanToken(account.Name)
                            + "|filled=" + order.Filled.ToString(CultureInfo.InvariantCulture)
                            + "|quantity=" + order.Quantity.ToString(CultureInfo.InvariantCulture)),
                    DateTime.UtcNow);
                RecoverGroup(group, "terminal_partial_entry_fill_" + account.Name);
                return;
            }

            if (TryGetEntryAccountIndex(group, order, out entryAccountIndex)
                && order.OrderState == OrderState.Filled)
            {
                GlitchAiExecutionResult protection = TrySubmitStructuralProtection(group, entryAccountIndex);
                if (!string.Equals(protection.Status, "submitted", StringComparison.Ordinal))
                {
                    GlitchAiExecutionJournalWriter.TryAppend(group.IntentId, protection, DateTime.UtcNow);
                    RecoverGroup(group, "fill_protection_" + protection.Code + "_" + account.Name);
                    return;
                }

                if (string.Equals(protection.Code, "group_structural_brackets_submitted", StringComparison.Ordinal))
                    GlitchAiExecutionJournalWriter.TryAppend(group.IntentId, protection, DateTime.UtcNow);
            }

            TryCompleteGroup(group);
        }

        public static void ProcessAccountStateUpdate(Account account)
        {
            if (account == null)
                return;

            List<ExecutionGroupContext> recovering;
            lock (GroupSync)
            {
                recovering = GroupsBySignal.Values
                    .Distinct()
                    .Where(item => item != null
                        && item.RecoveryStarted
                        && item.Accounts.Contains(account))
                    .ToList();
            }

            foreach (ExecutionGroupContext group in recovering)
            {
                int accountIndex = group.Accounts.IndexOf(account);
                if (accountIndex >= 0)
                    TryRecoverLateFill(group, accountIndex, "account_state_update_" + account.Name);
                TryCompleteGroup(group);
            }
        }

        public static bool IsExecutionEnabled(GlitchAiRailPolicy policy)
        {
            return policy != null
                && (string.Equals(policy.Mode, "paper", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(policy.Mode, "live", StringComparison.OrdinalIgnoreCase))
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
                return GlitchAiExecutionResult.Failed("sim_group_invalid", groupFailure);

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
            double quantityTp1Value = 0;
            if (hasSecondTarget
                && (!GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity_tp1", out quantityTp1Value)
                    || quantityTp1Value < 1))
                return GlitchAiExecutionResult.Failed("enter_quantity_split_missing");
            if (hasSecondStop && !hasSecondTarget)
                return GlitchAiExecutionResult.Failed("enter_stop_loss_2_requires_tp2");

            string instrumentRoot = GlitchAiJsonFields.ExtractString(rawJson, "instrument");
            string action = GlitchAiJsonFields.ExtractString(rawJson, "action");
            string snapshotHash = GlitchAiJsonFields.ExtractString(rawJson, "snapshot_hash");
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
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

            double masterRisk;
            if (!GlitchAiRiskFirewall.TryComputeTradeRiskUsd(rawJson, instrumentRoot, action, snapshotMarketPrice, out masterRisk))
                return GlitchAiExecutionResult.Failed("group_risk_not_computable");

            GlitchInstrumentMetadata metadata;
            if (!GlitchInstrumentMetadataService.TryResolve(instrument, out metadata)
                || metadata == null
                || !metadata.IsResolved
                || metadata.TickSize <= 0
                || metadata.PointValue <= 0)
                return GlitchAiExecutionResult.Failed("group_execution_metadata_unavailable");

            double stopDistance = isLong
                ? snapshotMarketPrice - stopLoss
                : stopLoss - snapshotMarketPrice;
            double targetDistance = isLong
                ? takeProfit1 - snapshotMarketPrice
                : snapshotMarketPrice - takeProfit1;
            if (stopDistance < metadata.TickSize || targetDistance < metadata.TickSize)
                return GlitchAiExecutionResult.Failed("group_bracket_distance_invalid");

            double secondStopDistance = stopDistance;
            double secondTargetDistance = 0;
            if (hasSecondTarget)
            {
                secondTargetDistance = isLong
                    ? takeProfit2 - snapshotMarketPrice
                    : snapshotMarketPrice - takeProfit2;
                if (secondTargetDistance < metadata.TickSize)
                    return GlitchAiExecutionResult.Failed("group_second_target_distance_invalid");
                if (hasSecondStop)
                {
                    secondStopDistance = isLong
                        ? snapshotMarketPrice - stopLoss2
                        : stopLoss2 - snapshotMarketPrice;
                    if (secondStopDistance < metadata.TickSize)
                        return GlitchAiExecutionResult.Failed("group_second_stop_distance_invalid");
                }
            }

            double liveExecutionPrice;
            string livePriceFailure;
            if (!TryGetFreshExecutionPrice(instrument, DateTime.UtcNow, out liveExecutionPrice, out livePriceFailure))
                return GlitchAiExecutionResult.Failed("group_live_price_invalid", livePriceFailure);

            // A MARKET intent expresses bracket geometry at decision time. MNQ
            // can move materially before submission, so preserve those distances
            // and re-anchor them to the executable price instead of rejecting a
            // valid trade because its absolute snapshot levels became stale.
            stopLoss = RoundToTick(
                isLong ? liveExecutionPrice - stopDistance : liveExecutionPrice + stopDistance,
                metadata.TickSize);
            takeProfit1 = RoundToTick(
                isLong ? liveExecutionPrice + targetDistance : liveExecutionPrice - targetDistance,
                metadata.TickSize);
            double liveRewardRisk = targetDistance / stopDistance;

            int masterQuantity = (int)Math.Round(quantityValue, MidpointRounding.AwayFromZero);
            if (Math.Abs(quantityValue - masterQuantity) > 0.0000001d)
                return GlitchAiExecutionResult.Failed("master_quantity_must_be_integer");
            int quantityTp1 = masterQuantity;
            if (hasSecondTarget)
            {
                quantityTp1 = (int)Math.Round(quantityTp1Value, MidpointRounding.AwayFromZero);
                if (Math.Abs(quantityTp1Value - quantityTp1) > 0.0000001d
                    || quantityTp1 < 1 || quantityTp1 >= masterQuantity)
                    return GlitchAiExecutionResult.Failed("master_quantity_split_invalid");
            }

            members[0].Quantity = masterQuantity;

            int masterCurrentNet = GetNetPosition(members[0].Account, instrument);
            int requestedDirection = isLong ? 1 : -1;
            if (masterCurrentNet != 0 && Math.Sign(masterCurrentNet) != requestedDirection)
                return GlitchAiExecutionResult.Failed("opposite_position_exists", members[0].Account.Name);
            if (Math.Abs(masterCurrentNet) + masterQuantity > policy.MaxContracts)
                return GlitchAiExecutionResult.Failed(
                    "max_contracts_exceeded_at_execution",
                    (Math.Abs(masterCurrentNet) + masterQuantity).ToString(CultureInfo.InvariantCulture));

            double perContractRisk = stopDistance * metadata.PointValue;
            if (perContractRisk > policy.MaxRiskPerContractUsd)
            {
                return GlitchAiExecutionResult.Failed(
                    "max_risk_per_contract_exceeded_at_execution",
                    perContractRisk.ToString("F2", CultureInfo.InvariantCulture));
            }
            double masterLiveRisk = perContractRisk * masterQuantity;
            if (masterLiveRisk > policy.MaxLossPerTradeUsd)
            {
                return GlitchAiExecutionResult.Failed(
                    "max_loss_per_trade_exceeded_at_execution",
                    masterLiveRisk.ToString("F2", CultureInfo.InvariantCulture));
            }
            Account masterAccount = members[0].Account;
            if (masterCurrentNet != 0 && !HasCompleteAiProtection(masterAccount, instrument, out _))
                return GlitchAiExecutionResult.Failed("existing_master_position_not_fully_ai_protected", masterAccount.Name);
            if (HasWorkingNonProtectionOrder(masterAccount, instrument))
                return GlitchAiExecutionResult.Failed("master_order_in_flight_or_not_ai_owned", masterAccount.Name);

            bool riskLocked;
            bool evalTargetLocked;
            double realizedToday;
            string portfolioFailure;
            if (!GlitchAiPortfolioSnapshotReader.TryGetFreshRiskState(
                masterAccount.Name,
                DateTime.UtcNow,
                policy.SnapshotMaxAgeSeconds,
                out riskLocked,
                out evalTargetLocked,
                out realizedToday,
                out portfolioFailure))
                return GlitchAiExecutionResult.Failed("master_portfolio_snapshot_invalid", portfolioFailure);
            if (riskLocked)
                return GlitchAiExecutionResult.Failed("master_account_risk_locked", masterAccount.Name);
            if (evalTargetLocked)
                return GlitchAiExecutionResult.Failed("master_account_eval_target_locked", masterAccount.Name);

            double lossToday = realizedToday < 0 ? -realizedToday : 0;
            if (policy.MaxDailyLossUsd > 0 && lossToday + masterLiveRisk > policy.MaxDailyLossUsd)
                return GlitchAiExecutionResult.Failed("master_account_daily_loss_exceeded", masterAccount.Name);

            OrderAction entryAction = isLong ? OrderAction.Buy : OrderAction.SellShort;
            string replicationDenial = GetReplicationEntryDenialReason?.Invoke(
                masterAccount,
                instrument,
                entryAction,
                masterQuantity);
            if (!string.IsNullOrWhiteSpace(replicationDenial))
                return GlitchAiExecutionResult.Failed("replication_entry_blocked", replicationDenial);

            string correlation = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture).Substring(0, 10);
            ExecutionGroupMember masterMember = members[0];
            var protectionLegs = new List<StructuralProtectionLeg>
            {
                new StructuralProtectionLeg
                {
                    Quantity = quantityTp1,
                    StopDistance = stopDistance,
                    TargetDistance = targetDistance
                }
            };
            if (hasSecondTarget)
            {
                protectionLegs.Add(new StructuralProtectionLeg
                {
                    Quantity = masterQuantity - quantityTp1,
                    StopDistance = secondStopDistance,
                    TargetDistance = secondTargetDistance
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
                TickSize = metadata.TickSize,
                PointValue = metadata.PointValue,
                MaxLossPerMasterContractUsd = Math.Min(
                    policy.MaxRiskPerContractUsd,
                    policy.MaxLossPerTradeUsd / masterQuantity),
                EntrySubmissionStarted = new bool[1],
                ProtectionSubmitted = new bool[1],
                RecoveryFlattenSubmitted = new bool[1]
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
                    + "|master_risk=" + masterLiveRisk.ToString("F2", CultureInfo.InvariantCulture)
                    + "|snapshot_price=" + snapshotMarketPrice.ToString(CultureInfo.InvariantCulture)
                    + "|live_price=" + liveExecutionPrice.ToString(CultureInfo.InvariantCulture)
                    + "|stop_price=" + stopLoss.ToString(CultureInfo.InvariantCulture)
                    + "|target_price=" + takeProfit1.ToString(CultureInfo.InvariantCulture)
                    + "|protection_legs=" + protectionLegs.Count.ToString(CultureInfo.InvariantCulture)
                    + "|quantity_tp1=" + quantityTp1.ToString(CultureInfo.InvariantCulture)
                    + "|live_risk_per_contract=" + perContractRisk.ToString("F2", CultureInfo.InvariantCulture)
                    + "|live_reward_risk=" + liveRewardRisk.ToString("F2", CultureInfo.InvariantCulture));
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

        private static bool TryGetEntryAccountIndex(ExecutionGroupContext group, Order order, out int accountIndex)
        {
            accountIndex = -1;
            if (group == null || order == null || group.Orders == null)
                return false;

            int stride = GetOrderStride(group);
            for (int i = 0; i < group.Accounts.Count; i++)
            {
                int offset = i * stride;
                if (offset < group.Orders.Count && ReferenceEquals(group.Orders[offset], order))
                {
                    accountIndex = i;
                    return true;
                }
            }

            return false;
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

            lock (GroupSync)
            {
                if (group.ProtectionSubmitted != null
                    && accountIndex < group.ProtectionSubmitted.Length
                    && group.ProtectionSubmitted[accountIndex])
                    return GlitchAiExecutionResult.Succeeded("group_structural_brackets_already_submitted");
            }

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
            var createdOrders = new List<Order>();
            var evidence = new List<string>();
            for (int legIndex = 0; legIndex < group.ProtectionLegs.Count; legIndex++)
            {
                StructuralProtectionLeg leg = group.ProtectionLegs[legIndex];
                double stopPrice = RoundToTick(
                    group.IsLong ? fillPrice - leg.StopDistance : fillPrice + leg.StopDistance,
                    group.TickSize);
                double targetPrice = RoundToTick(
                    group.IsLong ? fillPrice + leg.TargetDistance : fillPrice - leg.TargetDistance,
                    group.TickSize);
                bool geometryValid = group.IsLong
                    ? stopPrice < fillPrice && targetPrice > fillPrice
                    : stopPrice > fillPrice && targetPrice < fillPrice;
                if (!geometryValid)
                    return GlitchAiExecutionResult.Failed("group_structural_geometry_invalid_at_fill", "leg=" + (legIndex + 1));
                double fillStopDistance = group.IsLong ? fillPrice - stopPrice : stopPrice - fillPrice;
                double fillRiskPerContract = fillStopDistance * group.PointValue;
                if (fillRiskPerContract > group.MaxLossPerMasterContractUsd)
                    return GlitchAiExecutionResult.Failed("group_structural_risk_exceeded_at_fill", "leg=" + (legIndex + 1));

                string legToken = (legIndex + 1).ToString(CultureInfo.InvariantCulture);
                string oco = "GLTAI" + group.Correlation + accountIndex.ToString(CultureInfo.InvariantCulture) + legToken;
                string stopSignal = BuildSignalName(SignalStop, group.Correlation, accountIndex, legIndex);
                string targetSignal = BuildSignalName(SignalTarget, group.Correlation, accountIndex, legIndex);
                Order stop = CreateExitOrder(account, group.Instrument, exitAction, OrderType.StopMarket, leg.Quantity, 0, stopPrice, oco, stopSignal);
                Order target = CreateExitOrder(account, group.Instrument, exitAction, OrderType.Limit, leg.Quantity, targetPrice, 0, oco, targetSignal);
                if (stop == null || target == null)
                    return GlitchAiExecutionResult.Failed("group_structural_bracket_create_failed", "leg=" + legToken);
                group.Orders[offset + 1 + (legIndex * 2)] = stop;
                group.Orders[offset + 2 + (legIndex * 2)] = target;
                createdOrders.Add(stop);
                createdOrders.Add(target);
                evidence.Add("leg" + legToken + "_qty=" + leg.Quantity.ToString(CultureInfo.InvariantCulture));
                evidence.Add("sl" + legToken + "=" + stopPrice.ToString(CultureInfo.InvariantCulture));
                evidence.Add("tp" + legToken + "=" + targetPrice.ToString(CultureInfo.InvariantCulture));
            }

            lock (GroupSync)
            {
                if (group.ProtectionSubmitted != null && accountIndex < group.ProtectionSubmitted.Length)
                    group.ProtectionSubmitted[accountIndex] = true;
                foreach (Order protectionOrder in createdOrders)
                    GroupsBySignal[protectionOrder.Name.Trim()] = group;
            }

            try
            {
                account.Submit(createdOrders.ToArray());
                if (createdOrders.Any(IsRejected))
                    return GlitchAiExecutionResult.Failed("group_structural_bracket_rejected", account.Name);
            }
            catch (Exception ex)
            {
                return GlitchAiExecutionResult.Failed("group_structural_bracket_submit_exception", ex.GetType().Name);
            }

            return GlitchAiExecutionResult.Succeeded(
                "group_structural_brackets_submitted",
                BuildGroupEvidenceMessage(group)
                    + "|account=" + CleanToken(account.Name)
                    + "|fill=" + fillPrice.ToString(CultureInfo.InvariantCulture)
                    + "|" + string.Join("|", evidence));
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
                int net = GetNetPosition(account, instrument);
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

            int masterNet = GetNetPosition(members[0].Account, instrument);
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

        private static List<ExecutionGroupContext> RecoverOwnedGroupsFromLiveOrders(
            IReadOnlyList<Account> accounts,
            Instrument instrument,
            string groupId,
            string intentId)
        {
            var recovered = new List<ExecutionGroupContext>();
            if (accounts == null || accounts.Count == 0 || instrument == null)
                return recovered;

            var correlations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Account account in accounts)
            {
                if (account == null || account.Orders == null)
                    return recovered;

                foreach (Order order in account.Orders)
                {
                    string correlation;
                    int accountIndex;
                    if (TryParseAiSignalName(order == null ? null : order.Name, SignalEntry, out correlation, out accountIndex)
                        || TryParseAiSignalName(order == null ? null : order.Name, SignalStop, out correlation, out accountIndex)
                        || TryParseAiSignalName(order == null ? null : order.Name, SignalTarget, out correlation, out accountIndex))
                        correlations.Add(correlation);
                }
            }

            foreach (string correlation in correlations)
            {
                var group = new ExecutionGroupContext
                {
                    Correlation = correlation,
                    GroupId = groupId,
                    IntentId = intentId,
                    Instrument = instrument,
                    Accounts = accounts.ToList(),
                    Orders = new List<Order>(),
                    ProtectionLegs = new List<StructuralProtectionLeg>
                    {
                        new StructuralProtectionLeg { Quantity = 1 }
                    },
                    EntrySubmissionStarted = new bool[accounts.Count],
                    ProtectionSubmitted = new bool[accounts.Count],
                    RecoveryFlattenSubmitted = new bool[accounts.Count]
                };

                Order recoveredStop1 = FindNamedOrder(accounts[0], BuildSignalName(SignalStop, correlation, 0, 0), instrument);
                Order recoveredTarget1 = FindNamedOrder(accounts[0], BuildSignalName(SignalTarget, correlation, 0, 0), instrument);
                if (recoveredStop1 != null && recoveredTarget1 != null)
                    group.ProtectionLegs[0].Quantity = recoveredStop1.Quantity;
                Order recoveredStop2 = FindNamedOrder(accounts[0], BuildSignalName(SignalStop, correlation, 0, 1), instrument);
                Order recoveredTarget2 = FindNamedOrder(accounts[0], BuildSignalName(SignalTarget, correlation, 0, 1), instrument);
                if (recoveredStop2 != null && recoveredTarget2 != null)
                    group.ProtectionLegs.Add(new StructuralProtectionLeg { Quantity = recoveredStop2.Quantity });

                bool complete = true;
                for (int i = 0; i < accounts.Count; i++)
                {
                    Account account = accounts[i];
                    Order entry = FindNamedOrder(account, BuildSignalName(SignalEntry, correlation, i), instrument);
                    if (entry == null)
                    {
                        complete = false;
                        break;
                    }

                    group.Orders.Add(entry);
                    for (int legIndex = 0; legIndex < group.ProtectionLegs.Count; legIndex++)
                    {
                        Order stop = FindNamedOrder(account, BuildSignalName(SignalStop, correlation, i, legIndex), instrument);
                        Order target = FindNamedOrder(account, BuildSignalName(SignalTarget, correlation, i, legIndex), instrument);
                        if (stop == null || target == null)
                        {
                            complete = false;
                            break;
                        }
                        group.Orders.Add(stop);
                        group.Orders.Add(target);
                    }
                    if (!complete)
                        break;
                }

                if (!complete)
                    continue;

                bool exact = true;
                for (int i = 0; i < accounts.Count; i++)
                {
                    if (!HasExactOwnedExposure(group, i))
                    {
                        exact = false;
                        break;
                    }
                }

                if (!exact)
                    continue;

                recovered.Add(group);
            }

            if (recovered.Count == 1)
                RegisterGroup(recovered[0]);
            return recovered;
        }

        private static Order FindNamedOrder(Account account, string name, Instrument instrument)
        {
            if (account == null || account.Orders == null || string.IsNullOrWhiteSpace(name))
                return null;

            List<Order> matches = account.Orders.Where(order => order != null
                && string.Equals(order.Name, name, StringComparison.Ordinal)
                && ReferenceEquals(order.Account, account)
                && SameInstrument(order.Instrument, instrument)).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        internal static bool TryParseAiSignalName(
            string name,
            string prefix,
            out string correlation,
            out int accountIndex)
        {
            correlation = null;
            accountIndex = -1;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(prefix))
                return false;

            string marker = prefix + "-";
            if (!name.StartsWith(marker, StringComparison.Ordinal))
                return false;

            string[] parts = name.Substring(marker.Length).Split('-');
            int ignoredLegIndex;
            if ((parts.Length != 2 && parts.Length != 3) || parts[0].Length != 10
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out accountIndex)
                || accountIndex < 0
                || (parts.Length == 3
                    && (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out ignoredLegIndex)
                        || ignoredLegIndex < 0)))
            {
                accountIndex = -1;
                return false;
            }

            for (int i = 0; i < parts[0].Length; i++)
            {
                char c = parts[0][i];
                if (!((c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F')))
                    return false;
            }

            correlation = parts[0];
            return true;
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
                    if (HasExactEntryExposure(group, group.Accounts.IndexOf(account)))
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
                || !HasExactEntryExposure(group, accountIndex))
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
                bool allTerminal = group.Orders.All(IsTerminalTrackedOrder);
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
            if (group == null || accountIndex < 0 || accountIndex >= group.Accounts.Count)
                return false;
            int offset = accountIndex * GetOrderStride(group);
            if (offset >= group.Orders.Count)
                return false;

            Account account = group.Accounts[accountIndex];
            Order entry = group.Orders[offset];
            if (account == null || entry == null || entry.Filled <= 0)
                return false;

            int expectedNet = entry.OrderAction == OrderAction.Buy
                ? entry.Filled
                : entry.OrderAction == OrderAction.SellShort
                    ? -entry.Filled
                    : 0;
            int actualNet = GetNetPosition(account, group.Instrument);
            return expectedNet != 0
                && ReferenceEquals(entry.Account, account)
                && SameInstrument(entry.Instrument, group.Instrument)
                && entry.Name == BuildSignalName(SignalEntry, group.Correlation, accountIndex)
                && Math.Sign(actualNet) == Math.Sign(expectedNet)
                && Math.Abs(actualNet) >= Math.Abs(expectedNet);
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

        private static bool HasExactNetPosition(Account account, Instrument instrument, int expectedNet)
        {
            if (account == null || instrument == null || expectedNet == 0 || account.Positions == null)
                return false;

            try
            {
                int actualNet = 0;
                foreach (Position position in account.Positions)
                {
                    if (position == null || position.Instrument == null
                        || !SameInstrument(position.Instrument, instrument))
                        continue;

                    if (position.MarketPosition == MarketPosition.Long)
                        actualNet += position.Quantity;
                    else if (position.MarketPosition == MarketPosition.Short)
                        actualNet -= position.Quantity;
                }

                return actualNet == expectedNet;
            }
            catch
            {
                return false;
            }
        }

        private static int GetNetPosition(Account account, Instrument instrument)
        {
            if (account == null || instrument == null || account.Positions == null)
                return 0;
            int net = 0;
            foreach (Position position in account.Positions)
            {
                if (position == null || position.Instrument == null || !SameInstrument(position.Instrument, instrument))
                    continue;
                if (position.MarketPosition == MarketPosition.Long)
                    net += position.Quantity;
                else if (position.MarketPosition == MarketPosition.Short)
                    net -= position.Quantity;
            }
            return net;
        }

        private static bool HasCompleteAiProtection(Account account, Instrument instrument, out List<Order> stops)
        {
            stops = new List<Order>();
            if (account == null || instrument == null || account.Orders == null)
                return false;
            int net = GetNetPosition(account, instrument);
            if (net == 0)
                return false;
            int stopCoverage = 0;
            int targetCoverage = 0;
            foreach (Order order in account.Orders)
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
                    targetCoverage += order.Quantity;
            }
            return stopCoverage == Math.Abs(net) && targetCoverage == Math.Abs(net);
        }

        private static bool HasWorkingNonProtectionOrder(Account account, Instrument instrument)
        {
            if (account == null || account.Orders == null)
                return false;
            return account.Orders.Any(order => order != null
                && order.Instrument != null
                && SameInstrument(order.Instrument, instrument)
                && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                && !IsOwnedStopSignal(order.Name)
                && !IsOwnedTargetSignal(order.Name));
        }

        private static bool IsOwnedStopSignal(string name)
        {
            return !string.IsNullOrWhiteSpace(name)
                && (name.StartsWith(SignalStop + "-", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(GlitchCopyEngine.CopySignalName + "-S-", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsOwnedTargetSignal(string name)
        {
            return !string.IsNullOrWhiteSpace(name)
                && (name.StartsWith(SignalTarget + "-", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(GlitchCopyEngine.CopySignalName + "-T-", StringComparison.OrdinalIgnoreCase));
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
            public double TickSize { get; set; }
            public double PointValue { get; set; }
            public double MaxLossPerMasterContractUsd { get; set; }
            public bool[] EntrySubmissionStarted { get; set; }
            public bool[] ProtectionSubmitted { get; set; }
            public bool[] RecoveryFlattenSubmitted { get; set; }
            public bool RecoveryStarted { get; set; }
            public bool RecoveryUnresolvedRecorded { get; set; }
            public bool RecoveryTerminalRecorded { get; set; }
            public bool EntryFilledRecorded { get; set; }
            public bool OpenProtectedRecorded { get; set; }
            public bool TradeClosedRecorded { get; set; }
        }

        private sealed class StructuralProtectionLeg
        {
            public int Quantity { get; set; }
            public double StopDistance { get; set; }
            public double TargetDistance { get; set; }
        }

        private sealed class ExecutionGroupMember
        {
            public Account Account { get; set; }
            public int Quantity { get; set; }
        }
    }
}
