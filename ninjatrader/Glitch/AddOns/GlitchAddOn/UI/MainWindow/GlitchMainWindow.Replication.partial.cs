//
//
//   /$$$$$$  /$$ /$$   /$$               /$$      
//  /$$__  $$| $$|__/  | $$              | $$      
// | $$  \__/| $$ /$$ /$$$$$$    /$$$$$$$| $$$$$$$ 
// | $$ /$$$$| $$| $$|_  $$_/   /$$_____/| $$__  $$
// | $$|_  $$| $$| $$  | $$    | $$      | $$  \ $$
// | $$  \ $$| $$| $$  | $$ /$$| $$      | $$  | $$
// |  $$$$$$/| $$| $$  |  $$$$/|  $$$$$$$| $$  | $$
//  \______/ |__/|__/   \___/   \_______/|__/  |__/
//                                                                                                
//
// __________________________________________________
// __________________________________________________
//
//
// Glitch AddOn
//
// v.0.1.0.
// March 03, 2026
// by GlitchTrader.com
//
// __________________________________________________
// __________________________________________________
//

using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Glitch.Services;
using NinjaTrader.Cbi;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private enum DeltaSubmitResult
        {
            Failed = 0,
            Rejected = 1,
            Accepted = 2
        }

        private void ExecuteReplicationCycle(IReadOnlyList<Account> activeAccounts)
        {
            if (!UseLegacyReplicationEngine())
                return;

            if (!_isReplicatingUi || _isFlattenAllInProgress)
                return;
            if (_accountGroups == null || _accountGroups.Count == 0)
                return;

            var accountsByName = (activeAccounts ?? Array.Empty<Account>())
                .Where(account => account != null && !string.IsNullOrWhiteSpace(account.Name))
                .GroupBy(account => account.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
            if (accountsByName.Count == 0)
                return;

            var rowsByAccount = (_accountRows ?? new ObservableCollection<AccountGridRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.DisplayName))
                .GroupBy(row => row.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            DateTime nowUtc = DateTime.UtcNow;
            PruneReplicationSubmitCooldowns(nowUtc);
            PruneReplicationPendingSubmits(nowUtc);
            PruneProtectiveSyncCooldowns(nowUtc);
            if (nowUtc < _replicationWarmupUntilUtc)
                return;

            var microRootCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var intentsByKey = new Dictionary<string, ReplicationIntent>(StringComparer.OrdinalIgnoreCase);
            var conflictingIntentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AccountGroupDefinition group in _accountGroups)
            {
                if (group == null || string.IsNullOrWhiteSpace(group.MasterAccount) || group.Members == null || group.Members.Count == 0)
                    continue;
                if (!accountsByName.TryGetValue(group.MasterAccount.Trim(), out Account masterAccount) || masterAccount == null)
                    continue;
                string masterAccountName = masterAccount.Name?.Trim();

                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member == null || !member.IsEnabled || string.IsNullOrWhiteSpace(member.FollowerAccount))
                        continue;
                    if (double.IsNaN(member.Ratio) || double.IsInfinity(member.Ratio) || member.Ratio <= 0)
                        continue;
                    if (!accountsByName.TryGetValue(member.FollowerAccount.Trim(), out Account followerAccount) || followerAccount == null)
                        continue;
                    string followerAccountName = followerAccount.Name?.Trim();
                    if (string.Equals(masterAccount.Name, followerAccount.Name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    rowsByAccount.TryGetValue(followerAccountName ?? string.Empty, out AccountGridRow followerAccountRow);

                    foreach (string instrumentRoot in GetSyncInstrumentRoots(masterAccount, followerAccount))
                    {
                        if (string.IsNullOrWhiteSpace(instrumentRoot))
                            continue;
                        bool enforceStrategyCompliance = IsStrategyDrivenMasterInstrument(masterAccount, instrumentRoot, nowUtc);
                        if (IsReplicationFrozen(followerAccountName))
                        {
                            AppendReplicationStructuredJournal(
                                followerAccountName,
                                "sync_veto",
                                ReplicationVetoReason.LocalComplianceBreach.ToString(),
                                $"instrument={CleanJournalToken(instrumentRoot)}|reason=frozen_until_manual_ack");
                            continue;
                        }

                        if (enforceStrategyCompliance && IsTradingLocked(masterAccountName))
                        {
                            AppendReplicationStructuredJournal(
                                followerAccountName,
                                "sync_veto",
                                ReplicationVetoReason.TradingLocked.ToString(),
                                $"instrument={CleanJournalToken(instrumentRoot)}|locked_account={CleanJournalToken(masterAccountName)}");
                            continue;
                        }
                        if (enforceStrategyCompliance && IsTradingLocked(followerAccountName))
                        {
                            AppendReplicationStructuredJournal(
                                followerAccountName,
                                "sync_veto",
                                ReplicationVetoReason.LocalComplianceBreach.ToString(),
                                $"instrument={CleanJournalToken(instrumentRoot)}|locked_account={CleanJournalToken(followerAccountName)}");
                            continue;
                        }

                        int masterNetQty = GetNetQuantityForInstrumentRoot(masterAccount, instrumentRoot);
                        rowsByAccount.TryGetValue(masterAccountName ?? string.Empty, out AccountGridRow masterAccountRow);
                        int declaredMasterCap = ResolveDeclaredContractCap(masterAccountRow, instrumentRoot, microRootCache);
                        if (declaredMasterCap > 0 && Math.Abs(masterNetQty) > declaredMasterCap)
                        {
                            string capDetail = $"instrument={CleanJournalToken(instrumentRoot)}|masterNetQty={masterNetQty}|declaredCap={declaredMasterCap}";
                            AppendReplicationStructuredJournal(
                                followerAccountName,
                                "sync_veto",
                                ReplicationVetoReason.MasterCapExceeded.ToString(),
                                capDetail);
                            FreezeReplicationForAccount(
                                followerAccountName,
                                ReplicationVetoReason.MasterCapExceeded,
                                instrumentRoot,
                                $"Master net qty {masterNetQty} exceeds declared cap {declaredMasterCap}. Manual ack required.");
                            continue;
                        }

                        int targetAbsQty = masterNetQty == 0
                            ? 0
                            : RoundConservativeContracts(Math.Abs(masterNetQty) * member.Ratio);
                        int followerCapForInstrument = ResolveFollowerInstrumentContractCap(followerAccountRow, instrumentRoot, microRootCache);
                        if (followerCapForInstrument > 0 && targetAbsQty > followerCapForInstrument)
                        {
                            AppendReplicationStructuredJournal(
                                followerAccountName,
                                "sync_intent",
                                ReplicationVetoReason.FollowerCapExceeded.ToString(),
                                $"instrument={CleanJournalToken(instrumentRoot)}|targetAbsQty={targetAbsQty}|cap={followerCapForInstrument}|action=clamp");
                            targetAbsQty = followerCapForInstrument;
                        }
                        if (enforceStrategyCompliance &&
                            targetAbsQty > 1 &&
                            !string.IsNullOrWhiteSpace(followerAccountName) &&
                            _riskOneContractAccounts.Contains(followerAccountName))
                        {
                            // One-contract mode is pre-trade preventive, not corrective:
                            // do not force-reduce already-open larger exposure every heartbeat.
                            int followerCurrentNetQty = GetNetQuantityForInstrumentRoot(followerAccount, instrumentRoot);
                            int followerInFlightNetQty = GetInFlightReplicationEntryDeltaForInstrumentRoot(followerAccount, instrumentRoot);
                            int followerEffectiveAbsQty = Math.Abs(followerCurrentNetQty + followerInFlightNetQty);
                            if (followerEffectiveAbsQty <= 1)
                                targetAbsQty = 1;
                            else
                                targetAbsQty = Math.Min(targetAbsQty, followerEffectiveAbsQty);
                        }
                        int targetNetQty = masterNetQty == 0 ? 0 : targetAbsQty * Math.Sign(masterNetQty);
                        string followerInstrumentKey = $"{followerAccount.Name}|{instrumentRoot}";
                        Instrument tradeInstrument =
                            FindInstrumentForInstrumentRoot(masterAccount, instrumentRoot) ??
                            FindInstrumentForInstrumentRoot(followerAccount, instrumentRoot);

                        var nextIntent = new ReplicationIntent
                        {
                            Key = followerInstrumentKey,
                            MasterAccount = masterAccount,
                            FollowerAccount = followerAccount,
                            InstrumentRoot = instrumentRoot,
                            TradeInstrument = tradeInstrument,
                            TargetNetQty = targetNetQty,
                            EnforceStrategyCompliance = enforceStrategyCompliance
                        };

                        if (intentsByKey.TryGetValue(followerInstrumentKey, out ReplicationIntent existingIntent))
                        {
                            bool sameTarget = existingIntent.TargetNetQty == nextIntent.TargetNetQty;
                            bool sameMaster = string.Equals(existingIntent.MasterAccount?.Name, nextIntent.MasterAccount?.Name, StringComparison.OrdinalIgnoreCase);
                            bool sameCompliance = existingIntent.EnforceStrategyCompliance == nextIntent.EnforceStrategyCompliance;
                            if (!sameTarget || !sameMaster || !sameCompliance)
                                conflictingIntentKeys.Add(followerInstrumentKey);

                            continue;
                        }

                        intentsByKey[followerInstrumentKey] = nextIntent;
                    }
                }
            }

            ApplyAggregateContractCap(intentsByKey, conflictingIntentKeys, rowsByAccount, microRootCache);

            foreach (var kvp in intentsByKey)
            {
                string followerInstrumentKey = kvp.Key;
                if (conflictingIntentKeys.Contains(followerInstrumentKey))
                {
                    if (TryMarkReplicationConflictNotification(followerInstrumentKey, nowUtc))
                    {
                        ReplicationIntent conflictingIntent = kvp.Value;
                        string followerName = conflictingIntent?.FollowerAccount?.Name ?? "Unknown";
                        string instrumentRoot = conflictingIntent?.InstrumentRoot ?? followerInstrumentKey;
                        AppendJournal(
                            followerName,
                            "Replication",
                            $"Conflict on {instrumentRoot}: multiple replication intents detected. Skipping this cycle.");
                        RaiseCriticalWarning(
                            followerName,
                            $"Replication conflict on {instrumentRoot}. Multiple intents were generated; sync skipped until configuration converges.",
                            $"ReplicationConflict|{instrumentRoot}",
                            unlocksTrading: false);
                    }

                    continue;
                }

                ReplicationIntent intent = kvp.Value;
                if (intent?.FollowerAccount == null || string.IsNullOrWhiteSpace(intent.InstrumentRoot))
                    continue;
                if (IsReplicationFrozen(intent.FollowerAccount.Name))
                {
                    AppendReplicationStructuredJournal(
                        intent.FollowerAccount.Name,
                        "sync_veto",
                        ReplicationVetoReason.LocalComplianceBreach.ToString(),
                        $"instrument={CleanJournalToken(intent.InstrumentRoot)}|reason=frozen_until_manual_ack");
                    continue;
                }
                if (intent.EnforceStrategyCompliance &&
                    (IsTradingLocked(intent.MasterAccount?.Name) || IsTradingLocked(intent.FollowerAccount?.Name)))
                {
                    AppendReplicationStructuredJournal(
                        intent.FollowerAccount.Name,
                        "sync_veto",
                        ReplicationVetoReason.LocalComplianceBreach.ToString(),
                        $"instrument={CleanJournalToken(intent.InstrumentRoot)}|reason=trading_locked");
                    continue;
                }

                int followerNetQty = GetNetQuantityForInstrumentRoot(intent.FollowerAccount, intent.InstrumentRoot);
                int inFlightReplicationDelta = GetInFlightReplicationEntryDeltaForInstrumentRoot(intent.FollowerAccount, intent.InstrumentRoot);
                int effectiveFollowerNetQty = followerNetQty + inFlightReplicationDelta;
                if (DetectReplicationBurst(followerInstrumentKey, effectiveFollowerNetQty, nowUtc, out string burstReason))
                {
                    AppendReplicationStructuredJournal(
                        intent.FollowerAccount.Name,
                        "burst_notice",
                        string.IsNullOrWhiteSpace(burstReason) ? "detected" : burstReason,
                        $"instrument={CleanJournalToken(intent.InstrumentRoot)}|followerQty={effectiveFollowerNetQty}");
                }

                ApplyLiveFollowerAggregatePosition(intent.FollowerAccount);
                int deltaNetQty = intent.TargetNetQty - effectiveFollowerNetQty;
                AppendReplicationStructuredJournal(
                    intent.FollowerAccount.Name,
                    "sync_intent",
                    "intent_evaluated",
                    $"instrument={CleanJournalToken(intent.InstrumentRoot)}|masterNetQty={GetNetQuantityForInstrumentRoot(intent.MasterAccount, intent.InstrumentRoot)}|followerCurrentQty={followerNetQty}|targetFollowerQty={intent.TargetNetQty}|deltaQty={deltaNetQty}");
                if (deltaNetQty != 0)
                {
                    if (inFlightReplicationDelta != 0)
                        continue;
                    if (intent.TradeInstrument == null)
                        continue;
                    if (IsReplicationSubmissionCoolingDown(followerInstrumentKey, nowUtc))
                        continue;

                    int originalTarget = intent.TargetNetQty;
                    int clampedDelta = ClampReplicationDelta(deltaNetQty, out bool deltaClamped);
                    if (deltaClamped)
                    {
                        intent.TargetNetQty = effectiveFollowerNetQty + clampedDelta;
                        AppendReplicationStructuredJournal(
                            intent.FollowerAccount.Name,
                            "sync_intent",
                            "delta_clamped",
                            $"instrument={CleanJournalToken(intent.InstrumentRoot)}|requestedDelta={deltaNetQty}|clampedDelta={clampedDelta}|targetAfterClamp={intent.TargetNetQty}");
                    }

                    if (HasBlockingWorkingOrdersForInstrumentRoot(intent.FollowerAccount, intent.InstrumentRoot))
                    {
                        bool requiresHardResync =
                            intent.TargetNetQty == 0 ||
                            followerNetQty == 0 ||
                            Math.Sign(intent.TargetNetQty) != Math.Sign(followerNetQty) ||
                            Math.Abs(intent.TargetNetQty) < Math.Abs(followerNetQty);

                        if (!requiresHardResync)
                            continue;

                        bool cancelled = CancelAllWorkingOrdersForInstrumentRoot(intent.FollowerAccount, intent.InstrumentRoot);
                        if (!cancelled)
                        {
                            RaiseCriticalWarning(
                                intent.FollowerAccount.Name,
                                $"Replication blocked: could not cancel working orders for {intent.InstrumentRoot} during hard resync.",
                                $"ReplicationBlock|{intent.InstrumentRoot}",
                                unlocksTrading: false);
                            continue;
                        }

                        AppendJournal(
                            intent.FollowerAccount.Name,
                            "Replication",
                            $"Cleared working orders on {intent.InstrumentRoot} for hard resync.");
                    }

                    if (ShouldSuppressDuplicateReplicationSubmit(
                        followerInstrumentKey,
                        intent.TargetNetQty,
                        followerNetQty,
                        nowUtc))
                    {
                        intent.TargetNetQty = originalTarget;
                        continue;
                    }

                    string submitFailureReason;
                    if (TrySubmitDeltaOrderWithRetry(intent, effectiveFollowerNetQty, out submitFailureReason))
                    {
                        MarkReplicationPendingSubmit(
                            followerInstrumentKey,
                            intent.TargetNetQty,
                            followerNetQty,
                            nowUtc);
                        MarkReplicationSubmission(followerInstrumentKey, nowUtc);
                        AppendJournal(
                            intent.FollowerAccount.Name,
                            "Replication",
                            $"Synced {intent.InstrumentRoot} to target {intent.TargetNetQty} contracts.");
                        double masterAvgPrice = TryGetOpenPositionAveragePrice(intent.MasterAccount, intent.InstrumentRoot);
                        double followerAvgPrice = TryGetOpenPositionAveragePrice(intent.FollowerAccount, intent.InstrumentRoot);
                        AppendReplicationStructuredJournal(
                            intent.FollowerAccount.Name,
                            "sync_submit",
                            "accepted",
                            $"instrument={CleanJournalToken(intent.InstrumentRoot)}|targetFollowerQty={intent.TargetNetQty}|deltaQty={(intent.TargetNetQty - effectiveFollowerNetQty)}|masterAvg={masterAvgPrice.ToString(CultureInfo.InvariantCulture)}|followerAvg={followerAvgPrice.ToString(CultureInfo.InvariantCulture)}");
                    }
                    else
                    {
                        string reasonText = string.IsNullOrWhiteSpace(submitFailureReason)
                            ? "No broker acknowledgement."
                            : submitFailureReason;
                        RaiseCriticalWarning(
                            intent.FollowerAccount.Name,
                            $"Replication submit failed for {intent.InstrumentRoot}. Target={intent.TargetNetQty}, current={followerNetQty}. {reasonText}",
                            $"ReplicationSubmit|{intent.InstrumentRoot}",
                            unlocksTrading: false);
                        AppendJournal(
                            intent.FollowerAccount.Name,
                            "Replication",
                            $"Submit failed for {intent.InstrumentRoot}; target {intent.TargetNetQty}, current {followerNetQty}. {reasonText}");
                        AppendReplicationStructuredJournal(
                            intent.FollowerAccount.Name,
                            "sync_submit",
                            "rejected",
                            $"instrument={CleanJournalToken(intent.InstrumentRoot)}|targetFollowerQty={intent.TargetNetQty}|followerCurrentQty={followerNetQty}|detail={CleanJournalToken(reasonText)}");
                    }

                    intent.TargetNetQty = originalTarget;

                    continue;
                }

                SyncFollowerProtectiveOrders(intent, nowUtc);
            }
        }

        private bool TrySubmitDeltaOrderWithRetry(ReplicationIntent intent, int initialFollowerNetQty, out string failureReason)
        {
            failureReason = null;
            if (intent == null || intent.FollowerAccount == null || intent.TradeInstrument == null)
            {
                failureReason = "Invalid replication intent.";
                return false;
            }

            int previousDistance = Math.Abs(intent.TargetNetQty - initialFollowerNetQty);
            int maxAttempts = _replicationSubmitMaxAttempts > 0 ? _replicationSubmitMaxAttempts : 1;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                int currentFollowerNetQty = GetNetQuantityForInstrumentRoot(intent.FollowerAccount, intent.InstrumentRoot);
                int currentDelta = intent.TargetNetQty - currentFollowerNetQty;
                if (currentDelta == 0)
                    return true;

                DeltaSubmitResult submitResult = SubmitDeltaOrder(
                    intent.FollowerAccount,
                    intent.TradeInstrument,
                    currentDelta,
                    out string submitError);
                if (submitResult == DeltaSubmitResult.Accepted)
                {
                    bool acknowledged = HasReplicationSubmitEvidence(
                        intent.FollowerAccount,
                        intent.InstrumentRoot,
                        intent.TargetNetQty,
                        currentDelta,
                        previousDistance);
                    if (acknowledged)
                        return true;

                    // Broker acknowledgment can be delayed relative to this sync heartbeat.
                    // Treat accepted submits as success and let the next cycle verify position convergence.
                    failureReason = null;
                    return true;
                }

                failureReason = string.IsNullOrWhiteSpace(submitError)
                    ? "Submit failed."
                    : submitError;

                if (submitResult == DeltaSubmitResult.Accepted || attempt >= maxAttempts)
                    break;

                int refreshedFollowerNetQty = GetNetQuantityForInstrumentRoot(intent.FollowerAccount, intent.InstrumentRoot);
                previousDistance = Math.Abs(intent.TargetNetQty - refreshedFollowerNetQty);
            }

            return false;
        }

        private DeltaSubmitResult SubmitDeltaOrder(Account followerAccount, Instrument instrument, int deltaNetQty, out string failureReason)
        {
            failureReason = null;
            if (followerAccount == null || instrument == null || deltaNetQty == 0)
            {
                failureReason = "Invalid delta submission input.";
                return DeltaSubmitResult.Failed;
            }

            int quantity = Math.Abs(deltaNetQty);
            if (quantity <= 0)
            {
                failureReason = "Invalid order quantity.";
                return DeltaSubmitResult.Failed;
            }

            try
            {
                string instrumentRoot = GetInstrumentRoot(instrument);
                if (string.IsNullOrWhiteSpace(instrumentRoot))
                {
                    failureReason = "Unable to resolve instrument root.";
                    return DeltaSubmitResult.Failed;
                }
                int followerNetQty = GetNetQuantityForInstrumentRoot(followerAccount, instrumentRoot);
                if (deltaNetQty > 0)
                {
                    // Move follower net towards long: cover shorts first, then buy to expand long.
                    if (followerNetQty < 0)
                    {
                        int coverQty = Math.Min(Math.Abs(followerNetQty), quantity);
                        if (coverQty > 0)
                        {
                            DeltaSubmitResult coverResult = SubmitMarketOrder(
                                followerAccount,
                                instrument,
                                OrderAction.BuyToCover,
                                coverQty,
                                out failureReason);
                            if (coverResult != DeltaSubmitResult.Accepted)
                                return coverResult;
                        }

                        int remainingQty = quantity - coverQty;
                        if (remainingQty > 0)
                        {
                            DeltaSubmitResult buyResult = SubmitMarketOrder(
                                followerAccount,
                                instrument,
                                OrderAction.Buy,
                                remainingQty,
                                out failureReason);
                            if (buyResult != DeltaSubmitResult.Accepted)
                                return buyResult;
                        }

                        return DeltaSubmitResult.Accepted;
                    }

                    return SubmitMarketOrder(followerAccount, instrument, OrderAction.Buy, quantity, out failureReason);
                }

                // Move follower net towards short: sell longs first, then sell short to expand short.
                if (followerNetQty > 0)
                {
                    int sellQty = Math.Min(followerNetQty, quantity);
                    if (sellQty > 0)
                    {
                        DeltaSubmitResult sellResult = SubmitMarketOrder(
                            followerAccount,
                            instrument,
                            OrderAction.Sell,
                            sellQty,
                            out failureReason);
                        if (sellResult != DeltaSubmitResult.Accepted)
                            return sellResult;
                    }

                    int remainingQty = quantity - sellQty;
                    if (remainingQty > 0)
                    {
                        DeltaSubmitResult shortResult = SubmitMarketOrder(
                            followerAccount,
                            instrument,
                            OrderAction.SellShort,
                            remainingQty,
                            out failureReason);
                        if (shortResult != DeltaSubmitResult.Accepted)
                            return shortResult;
                    }

                    return DeltaSubmitResult.Accepted;
                }

                return SubmitMarketOrder(followerAccount, instrument, OrderAction.SellShort, quantity, out failureReason);
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return DeltaSubmitResult.Failed;
            }
        }

        private DeltaSubmitResult SubmitMarketOrder(Account account, Instrument instrument, OrderAction action, int quantity, out string failureReason)
        {
            failureReason = null;
            if (account == null || instrument == null || quantity <= 0)
            {
                failureReason = "Invalid market order input.";
                return DeltaSubmitResult.Failed;
            }

            try
            {
                Order order = account.CreateOrder(
                    instrument,
                    action,
                    OrderType.Market,
                    GetPreferredFollowerOrderEntry(),
                    TimeInForce.Day,
                    quantity,
                    0.0,
                    0.0,
                    string.Empty,
                    ReplicationSignalName,
                    DateTime.MaxValue,
                    null);
                if (order == null)
                {
                    failureReason = "CreateOrder returned null.";
                    return DeltaSubmitResult.Failed;
                }

                account.Submit(new[] { order });
                if (order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled)
                {
                    failureReason = $"Order {order.OrderState} by broker.";
                    return DeltaSubmitResult.Rejected;
                }

                return DeltaSubmitResult.Accepted;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return DeltaSubmitResult.Failed;
            }
        }

        private bool HasReplicationSubmitEvidence(
            Account account,
            string instrumentRoot,
            int targetNetQty,
            int deltaNetQty,
            int previousDistanceToTarget)
        {
            if (account == null || string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            int currentNetQty = GetNetQuantityForInstrumentRoot(account, instrumentRoot);
            int currentDistance = Math.Abs(targetNetQty - currentNetQty);
            if (currentDistance < previousDistanceToTarget)
                return true;

            try
            {
                foreach (Order order in account.Orders)
                {
                    if (order == null || order.Instrument == null)
                        continue;
                    if (!string.Equals(GetInstrumentRoot(order.Instrument), instrumentRoot, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string signalName = order.Name ?? string.Empty;
                    if (!signalName.StartsWith(ReplicationSignalName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int actionSign = GetOrderActionSign(order.OrderAction);
                    if (actionSign != 0 && Math.Sign(deltaNetQty) != 0 && actionSign != Math.Sign(deltaNetQty))
                        continue;

                    if (order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled)
                        continue;

                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static OrderEntry GetPreferredFollowerOrderEntry()
        {
            try
            {
                if (Enum.TryParse("Automated", true, out OrderEntry automatedEntry))
                    return automatedEntry;
            }
            catch
            {
            }

            try
            {
                if (Enum.TryParse("Manual", true, out OrderEntry manualEntry))
                    return manualEntry;
            }
            catch
            {
            }

            return default(OrderEntry);
        }

        private bool HasBlockingWorkingOrdersForInstrumentRoot(Account account, string instrumentRoot)
        {
            if (account == null || string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            try
            {
                return GetWorkingOrdersForInstrumentRoot(account, instrumentRoot)
                    .Any(order => !IsReplicatedProtectiveOrder(order) && !IsReplicatedEntryOrder(order));
            }
            catch
            {
                return false;
            }
        }

        private static bool IsReplicatedEntryOrder(Order order)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.Name))
                return false;

            return order.Name.StartsWith(ReplicationSignalName, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetInFlightReplicationEntryDeltaForInstrumentRoot(Account account, string instrumentRoot)
        {
            if (account == null || string.IsNullOrWhiteSpace(instrumentRoot))
                return 0;

            int netDelta = 0;
            try
            {
                foreach (Order order in GetWorkingOrdersForInstrumentRoot(account, instrumentRoot))
                {
                    if (!IsReplicatedEntryOrder(order))
                        continue;

                    int actionSign = GetOrderActionSign(order.OrderAction);
                    if (actionSign == 0)
                        continue;

                    int remainingQty = GetRemainingOrderQuantity(order);
                    if (remainingQty <= 0)
                        continue;

                    netDelta += actionSign * remainingQty;
                }
            }
            catch
            {
            }

            return netDelta;
        }

        private static int GetTotalInFlightReplicationEntryDelta(Account account)
        {
            if (account == null)
                return 0;

            int netDelta = 0;
            try
            {
                foreach (Order order in account.Orders)
                {
                    if (order == null || !GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                        continue;
                    if (!IsReplicatedEntryOrder(order))
                        continue;

                    int actionSign = GetOrderActionSign(order.OrderAction);
                    if (actionSign == 0)
                        continue;

                    int remainingQty = GetRemainingOrderQuantity(order);
                    if (remainingQty <= 0)
                        continue;

                    netDelta += actionSign * remainingQty;
                }
            }
            catch
            {
            }

            return netDelta;
        }

        private static int GetRemainingOrderQuantity(Order order)
        {
            if (order == null)
                return 0;

            int totalQty = Math.Abs(order.Quantity);
            if (totalQty <= 0)
                return 0;

            double filledRaw = TryGetNestedPropertyValueAsDouble(order, "Filled", "FilledQuantity", "QuantityFilled");
            int filledQty = filledRaw > 0
                ? Math.Max(0, (int)Math.Round(filledRaw, MidpointRounding.AwayFromZero))
                : 0;
            if (filledQty >= totalQty)
                return 0;

            return totalQty - filledQty;
        }

        private void ApplyLiveFollowerAggregatePosition(Account followerAccount)
        {
            if (followerAccount == null || string.IsNullOrWhiteSpace(followerAccount.Name))
                return;

            string trimmedAccountName = followerAccount.Name.Trim();
            string display = GetAccountEffectivePositionDisplay(followerAccount);

            AccountGridRow accountRow = (_accountRows ?? new ObservableCollection<AccountGridRow>())
                .LastOrDefault(row =>
                    row != null &&
                    !string.IsNullOrWhiteSpace(row.DisplayName) &&
                    string.Equals(row.DisplayName.Trim(), trimmedAccountName, StringComparison.OrdinalIgnoreCase));
            if (accountRow != null)
                accountRow.Position = display;

            foreach (AccountGroupDefinition group in _accountGroups ?? new ObservableCollection<AccountGroupDefinition>())
            {
                if (group?.Members == null)
                    continue;

                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member == null || string.IsNullOrWhiteSpace(member.FollowerAccount))
                        continue;
                    if (!string.Equals(member.FollowerAccount.Trim(), trimmedAccountName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    member.Position = display;
                }
            }
        }

        private static bool CancelAllWorkingOrdersForInstrumentRoot(Account account, string instrumentRoot)
        {
            if (account == null || string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            try
            {
                List<Order> workingOrders = GetWorkingOrdersForInstrumentRoot(account, instrumentRoot);
                if (workingOrders == null || workingOrders.Count == 0)
                    return true;

                return CancelOrders(account, workingOrders);
            }
            catch
            {
                return false;
            }
        }

        private bool IsReplicationSubmissionCoolingDown(string cooldownKey, DateTime nowUtc)
        {
            lock (_replicationOrderLock)
            {
                return _replicationSubmitCooldownByKey.TryGetValue(cooldownKey, out DateTime cooldownUntilUtc) &&
                       cooldownUntilUtc > nowUtc;
            }
        }

        private void MarkReplicationSubmission(string cooldownKey, DateTime nowUtc)
        {
            lock (_replicationOrderLock)
                _replicationSubmitCooldownByKey[cooldownKey] = nowUtc.AddMilliseconds(_replicationSubmitCooldownMs);
        }

        private void PruneReplicationSubmitCooldowns(DateTime nowUtc)
        {
            lock (_replicationOrderLock)
            {
                foreach (string expiredKey in _replicationSubmitCooldownByKey
                    .Where(kvp => kvp.Value <= nowUtc)
                    .Select(kvp => kvp.Key)
                    .ToList())
                {
                    _replicationSubmitCooldownByKey.Remove(expiredKey);
                }
            }
        }

        private void PruneReplicationPendingSubmits(DateTime nowUtc)
        {
            lock (_replicationOrderLock)
            {
                foreach (string expiredKey in _replicationPendingSubmitByKey
                    .Where(kvp => kvp.Value == null || kvp.Value.ExpiresUtc <= nowUtc)
                    .Select(kvp => kvp.Key)
                    .ToList())
                {
                    _replicationPendingSubmitByKey.Remove(expiredKey);
                }
            }
        }

        private bool ShouldSuppressDuplicateReplicationSubmit(
            string submitKey,
            int targetNetQty,
            int followerNetQty,
            DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(submitKey))
                return false;

            lock (_replicationOrderLock)
            {
                if (!_replicationPendingSubmitByKey.TryGetValue(submitKey, out ReplicationPendingSubmitState pending) || pending == null)
                    return false;
                if (pending.ExpiresUtc <= nowUtc)
                {
                    _replicationPendingSubmitByKey.Remove(submitKey);
                    return false;
                }

                if (pending.TargetNetQty != targetNetQty || pending.FollowerNetQtyAtSubmit != followerNetQty)
                {
                    _replicationPendingSubmitByKey.Remove(submitKey);
                    return false;
                }

                return true;
            }
        }

        private void MarkReplicationPendingSubmit(
            string submitKey,
            int targetNetQty,
            int followerNetQty,
            DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(submitKey))
                return;

            int holdMs = Math.Max(_replicationSubmitCooldownMs * 4, 1200);
            lock (_replicationOrderLock)
            {
                _replicationPendingSubmitByKey[submitKey] = new ReplicationPendingSubmitState
                {
                    TargetNetQty = targetNetQty,
                    FollowerNetQtyAtSubmit = followerNetQty,
                    ExpiresUtc = nowUtc.AddMilliseconds(holdMs)
                };
            }
        }

        private void ClearReplicationSubmitCooldowns()
        {
            lock (_replicationOrderLock)
            {
                _replicationSubmitCooldownByKey.Clear();
                _replicationPendingSubmitByKey.Clear();
            }
        }

        private bool IsProtectiveSyncCoolingDown(string cooldownKey, DateTime nowUtc)
        {
            lock (_protectiveOrderLock)
            {
                return _protectiveSyncCooldownByKey.TryGetValue(cooldownKey, out DateTime cooldownUntilUtc) &&
                       cooldownUntilUtc > nowUtc;
            }
        }

        private void MarkProtectiveSync(string cooldownKey, DateTime nowUtc)
        {
            lock (_protectiveOrderLock)
                _protectiveSyncCooldownByKey[cooldownKey] = nowUtc.AddMilliseconds(_protectiveSyncCooldownMs);
        }

        private void PruneProtectiveSyncCooldowns(DateTime nowUtc)
        {
            lock (_protectiveOrderLock)
            {
                foreach (string expiredKey in _protectiveSyncCooldownByKey
                    .Where(kvp => kvp.Value <= nowUtc)
                    .Select(kvp => kvp.Key)
                    .ToList())
                {
                    _protectiveSyncCooldownByKey.Remove(expiredKey);
                }
            }
        }

        private void ClearProtectiveSyncCooldowns()
        {
            lock (_protectiveOrderLock)
                _protectiveSyncCooldownByKey.Clear();
        }

        private void SyncFollowerProtectiveOrders(ReplicationIntent intent, DateTime nowUtc)
        {
            if (intent?.FollowerAccount == null || string.IsNullOrWhiteSpace(intent.InstrumentRoot))
                return;

            string cooldownKey = $"{intent.Key}|PROTECTIVE";
            if (IsProtectiveSyncCoolingDown(cooldownKey, nowUtc))
                return;

            bool changed = false;
            if (intent.TargetNetQty == 0)
            {
                changed = CancelReplicatedProtectiveOrders(intent.FollowerAccount, intent.InstrumentRoot);
                if (changed)
                {
                    AppendReplicationStructuredJournal(
                        intent.FollowerAccount.Name,
                        "protective_cancel",
                        "target_qty_zero",
                        $"instrument={CleanJournalToken(intent.InstrumentRoot)}|reason=position_sync_flat");
                }
                if (changed)
                    MarkProtectiveSync(cooldownKey, nowUtc);
                return;
            }

            if (intent.TradeInstrument == null)
            {
                intent.TradeInstrument =
                    FindInstrumentForInstrumentRoot(intent.FollowerAccount, intent.InstrumentRoot) ??
                    FindInstrumentForInstrumentRoot(intent.MasterAccount, intent.InstrumentRoot);
                if (intent.TradeInstrument == null)
                    return;
            }

            int followerNetQty = GetNetQuantityForInstrumentRoot(intent.FollowerAccount, intent.InstrumentRoot);
            if (followerNetQty != intent.TargetNetQty)
                return;

            if (!TryBuildMasterProtectiveTemplate(intent.MasterAccount, intent.InstrumentRoot, intent.TargetNetQty, out ProtectiveTemplate template))
            {
                AppendReplicationStructuredJournal(
                    intent.FollowerAccount.Name,
                    "protective_template",
                    "absent",
                    $"instrument={CleanJournalToken(intent.InstrumentRoot)}|followerQty={followerNetQty}");

                if (followerNetQty != 0)
                {
                    EnsureFollowerEmergencyStop(intent, followerNetQty);
                    FreezeReplicationForAccount(
                        intent.FollowerAccount.Name,
                        ReplicationVetoReason.MissingMasterProtective,
                        intent.InstrumentRoot,
                        "Master protective template missing while follower has open position. Emergency stop retained and replication frozen.");
                    MarkProtectiveSync(cooldownKey, nowUtc);
                    return;
                }

                changed = CancelReplicatedProtectiveOrders(intent.FollowerAccount, intent.InstrumentRoot);
                if (changed)
                {
                    AppendReplicationStructuredJournal(
                        intent.FollowerAccount.Name,
                        "protective_cancel",
                        "template_absent_no_position",
                        $"instrument={CleanJournalToken(intent.InstrumentRoot)}|reason=cleanup_only");
                }
                if (changed)
                    MarkProtectiveSync(cooldownKey, nowUtc);
                return;
            }

            AppendReplicationStructuredJournal(
                intent.FollowerAccount.Name,
                "protective_template",
                "present",
                $"instrument={CleanJournalToken(intent.InstrumentRoot)}|followerQty={followerNetQty}|targetQty={intent.TargetNetQty}");

            ProtectiveTemplate followerTemplate = AdjustProtectiveTemplateForFollower(intent, template);
            changed = EnsureFollowerProtectiveOrders(intent, followerTemplate);
            if (changed)
                MarkProtectiveSync(cooldownKey, nowUtc);
        }

        private bool EnsureFollowerProtectiveOrders(ReplicationIntent intent, ProtectiveTemplate template)
        {
            if (intent?.FollowerAccount == null || intent.TradeInstrument == null || template == null)
                return false;

            int quantity = Math.Abs(intent.TargetNetQty);
            if (quantity <= 0)
                return false;

            OrderAction exitAction = intent.TargetNetQty > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
            List<Order> allProtectiveOrders = GetWorkingOrdersForInstrumentRoot(intent.FollowerAccount, intent.InstrumentRoot)
                .Where(IsReplicatedProtectiveOrder)
                .ToList();
            bool hasTransitionalProtectiveOrders = allProtectiveOrders.Any(IsProtectiveOrderInFlightTransitionState);
            List<Order> protectiveOrders = allProtectiveOrders
                .Where(order => !IsProtectiveOrderInFlightTransitionState(order))
                .ToList();
            bool changed = false;
            Order existingStop = protectiveOrders.FirstOrDefault(IsStopLikeOrder);
            Order existingTarget = protectiveOrders.FirstOrDefault(IsLimitLikeOrder);

            bool needsStop = template.HasStop;
            bool needsTarget = template.HasTarget;
            if (needsStop && needsTarget)
            {
                bool hasStop = existingStop != null;
                bool hasTarget = existingTarget != null;
                bool hasSplitPair = hasStop ^ hasTarget;
                bool hasMismatchedOco = false;
                if (hasStop && hasTarget)
                {
                    string stopOco = TryGetNestedPropertyValueAsString(existingStop, "Oco");
                    string targetOco = TryGetNestedPropertyValueAsString(existingTarget, "Oco");
                    bool hasAsymmetricMissingOco = string.IsNullOrWhiteSpace(stopOco) != string.IsNullOrWhiteSpace(targetOco);
                    hasMismatchedOco =
                        hasAsymmetricMissingOco ||
                        (!string.IsNullOrWhiteSpace(stopOco) &&
                         !string.IsNullOrWhiteSpace(targetOco) &&
                         !string.Equals(stopOco, targetOco, StringComparison.OrdinalIgnoreCase));
                }

                if (hasSplitPair || hasMismatchedOco)
                {
                    if (hasTransitionalProtectiveOrders)
                        return false;

                    var resetPair = new List<Order>();
                    if (existingStop != null)
                        resetPair.Add(existingStop);
                    if (existingTarget != null)
                        resetPair.Add(existingTarget);
                    if (resetPair.Count > 0 && CancelOrders(intent.FollowerAccount, resetPair))
                        changed = true;

                    existingStop = null;
                    existingTarget = null;
                    allProtectiveOrders = GetWorkingOrdersForInstrumentRoot(intent.FollowerAccount, intent.InstrumentRoot)
                        .Where(IsReplicatedProtectiveOrder)
                        .ToList();
                    hasTransitionalProtectiveOrders = allProtectiveOrders.Any(IsProtectiveOrderInFlightTransitionState);
                    protectiveOrders = allProtectiveOrders
                        .Where(order => !IsProtectiveOrderInFlightTransitionState(order))
                        .ToList();
                }
            }

            if (hasTransitionalProtectiveOrders &&
                ((needsStop && existingStop == null) || (needsTarget && existingTarget == null)))
            {
                // Broker is still transitioning prior protective orders; wait for a stable snapshot before re-submitting.
                return false;
            }

            string ocoId = BuildProtectiveOcoId(intent.FollowerAccount.Name, intent.InstrumentRoot);

            changed |= EnsureProtectiveOrder(
                intent.FollowerAccount,
                intent.TradeInstrument,
                existingStop,
                template.HasStop,
                exitAction,
                quantity,
                OrderType.StopMarket,
                0.0,
                template.StopPrice,
                ocoId,
                ProtectiveStopSignalName);

            changed |= EnsureProtectiveOrder(
                intent.FollowerAccount,
                intent.TradeInstrument,
                existingTarget,
                template.HasTarget,
                exitAction,
                quantity,
                OrderType.Limit,
                template.TargetPrice,
                0.0,
                ocoId,
                ProtectiveTargetSignalName);

            var consumedOrders = new HashSet<Order>();
            if (existingStop != null)
                consumedOrders.Add(existingStop);
            if (existingTarget != null)
                consumedOrders.Add(existingTarget);

            var extraOrders = protectiveOrders
                .Where(order => order != null && !consumedOrders.Contains(order))
                .ToArray();
            if (extraOrders.Length > 0)
                changed |= CancelOrders(intent.FollowerAccount, extraOrders);

            return changed;
        }

        private bool EnsureProtectiveOrder(
            Account account,
            Instrument instrument,
            Order existingOrder,
            bool shouldExist,
            OrderAction action,
            int quantity,
            OrderType orderType,
            double limitPrice,
            double stopPrice,
            string ocoId,
            string signalName)
        {
            if (account == null)
                return false;

            if (!shouldExist)
            {
                if (existingOrder == null)
                    return false;

                if (string.Equals(signalName, ProtectiveStopSignalName, StringComparison.OrdinalIgnoreCase))
                    return false;

                return CancelOrders(account, new[] { existingOrder });
            }

            if (existingOrder == null)
            {
                if (!TryValidateProtectiveSubmitPrices(
                        account,
                        instrument,
                        orderType,
                        limitPrice,
                        stopPrice,
                        out string validationReason))
                {
                    AppendReplicationStructuredJournal(
                        account.Name,
                        "protective_skip",
                        "invalid_price",
                        $"instrument={CleanJournalToken(GetInstrumentRoot(instrument))}|signal={CleanJournalToken(signalName)}|detail={CleanJournalToken(validationReason)}");
                    return false;
                }

                return SubmitProtectiveOrder(
                    account,
                    instrument,
                    action,
                    quantity,
                    orderType,
                    limitPrice,
                    stopPrice,
                    ocoId,
                    signalName);
            }

            bool sameShape =
                existingOrder.OrderAction == action &&
                existingOrder.OrderType == orderType &&
                IsReplicatedProtectiveOrder(existingOrder);

            if (!sameShape)
            {
                if (string.Equals(signalName, ProtectiveStopSignalName, StringComparison.OrdinalIgnoreCase))
                    return false;

                bool cancelled = CancelOrders(account, new[] { existingOrder });
                if (!cancelled)
                    return false;

                return SubmitProtectiveOrder(
                    account,
                    instrument,
                    action,
                    quantity,
                    orderType,
                    limitPrice,
                    stopPrice,
                    ocoId,
                    signalName);
            }

            if (!ProtectiveOrderNeedsChange(existingOrder, quantity, limitPrice, stopPrice))
                return false;

            try
            {
                existingOrder.QuantityChanged = quantity;
                existingOrder.LimitPriceChanged = limitPrice;
                existingOrder.StopPriceChanged = stopPrice;
                account.Change(new[] { existingOrder });
                return true;
            }
            catch
            {
                if (string.Equals(signalName, ProtectiveStopSignalName, StringComparison.OrdinalIgnoreCase))
                    return false;

                bool cancelled = CancelOrders(account, new[] { existingOrder });
                if (!cancelled)
                    return false;

                return SubmitProtectiveOrder(
                    account,
                    instrument,
                    action,
                    quantity,
                    orderType,
                    limitPrice,
                    stopPrice,
                    ocoId,
                    signalName);
            }
        }

        private static bool ProtectiveOrderNeedsChange(Order order, int quantity, double limitPrice, double stopPrice)
        {
            if (order == null)
                return true;

            if (Math.Abs(order.Quantity) != Math.Abs(quantity))
                return true;

            double tickSize = 0.0000001;
            try
            {
                if (order.Instrument?.MasterInstrument != null && order.Instrument.MasterInstrument.TickSize > 0)
                    tickSize = order.Instrument.MasterInstrument.TickSize / 4.0;
            }
            catch
            {
            }

            if (limitPrice > 0 && Math.Abs(order.LimitPrice - limitPrice) > tickSize)
                return true;
            if (stopPrice > 0 && Math.Abs(order.StopPrice - stopPrice) > tickSize)
                return true;

            return false;
        }

        private static bool IsProtectiveOrderInFlightTransitionState(Order order)
        {
            if (order == null)
                return false;

            string stateText = order.OrderState.ToString();
            if (string.IsNullOrWhiteSpace(stateText))
                return false;

            return stateText.IndexOf("Pending", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   stateText.IndexOf("Submitted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   stateText.IndexOf("Cancel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   stateText.IndexOf("Change", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static double TryGetOpenPositionAveragePrice(Account account, string instrumentRoot)
        {
            Position position = FindOpenPositionForInstrumentRoot(account, instrumentRoot);
            if (position == null)
                return 0;

            double averagePrice = position.AveragePrice;
            if (averagePrice <= 0 || double.IsNaN(averagePrice) || double.IsInfinity(averagePrice))
                return 0;

            return averagePrice;
        }

        private ProtectiveTemplate AdjustProtectiveTemplateForFollower(ReplicationIntent intent, ProtectiveTemplate masterTemplate)
        {
            if (intent == null || masterTemplate == null)
                return masterTemplate;

            var adjusted = new ProtectiveTemplate
            {
                HasStop = masterTemplate.HasStop,
                StopPrice = masterTemplate.StopPrice,
                HasTarget = masterTemplate.HasTarget,
                TargetPrice = masterTemplate.TargetPrice
            };

            Instrument instrument = intent.TradeInstrument;
            if (instrument?.MasterInstrument == null)
                return adjusted;

            double tickSize = instrument.MasterInstrument.TickSize;
            if (tickSize <= 0 || double.IsNaN(tickSize) || double.IsInfinity(tickSize))
                return adjusted;

            int followerNetQty = GetNetQuantityForInstrumentRoot(intent.FollowerAccount, intent.InstrumentRoot);
            if (followerNetQty == 0)
                return adjusted;

            double masterAvg = TryGetOpenPositionAveragePrice(intent.MasterAccount, intent.InstrumentRoot);
            double followerAvg = TryGetOpenPositionAveragePrice(intent.FollowerAccount, intent.InstrumentRoot);
            if (masterAvg <= 0 || followerAvg <= 0)
                return adjusted;

            if (adjusted.HasStop && masterTemplate.StopPrice > 0)
            {
                double stopOffset = masterTemplate.StopPrice - masterAvg;
                double followerStop = instrument.MasterInstrument.RoundToTickSize(followerAvg + stopOffset);
                if (IsProtectiveStopPriceValid(followerNetQty, followerStop, followerAvg, tickSize))
                    adjusted.StopPrice = followerStop;
            }

            if (adjusted.HasTarget && masterTemplate.TargetPrice > 0)
            {
                double targetOffset = masterTemplate.TargetPrice - masterAvg;
                double followerTarget = instrument.MasterInstrument.RoundToTickSize(followerAvg + targetOffset);
                if (IsProtectiveLimitPriceValid(followerNetQty, followerTarget, followerAvg, tickSize))
                    adjusted.TargetPrice = followerTarget;
            }

            return adjusted;
        }

        private static bool IsProtectiveStopPriceValid(int netQty, double stopPrice, double averagePrice, double tickSize)
        {
            if (stopPrice <= 0 || averagePrice <= 0 || tickSize <= 0)
                return false;

            double minSeparation = tickSize * 0.5;
            if (netQty > 0)
                return stopPrice < averagePrice - minSeparation;
            if (netQty < 0)
                return stopPrice > averagePrice + minSeparation;

            return false;
        }

        private static bool IsProtectiveLimitPriceValid(int netQty, double limitPrice, double averagePrice, double tickSize)
        {
            if (limitPrice <= 0 || averagePrice <= 0 || tickSize <= 0)
                return false;

            double minSeparation = tickSize * 0.5;
            if (netQty > 0)
                return limitPrice > averagePrice + minSeparation;
            if (netQty < 0)
                return limitPrice < averagePrice - minSeparation;

            return false;
        }

        private bool TryValidateProtectiveSubmitPrices(
            Account account,
            Instrument instrument,
            OrderType orderType,
            double limitPrice,
            double stopPrice,
            out string validationReason)
        {
            validationReason = null;
            if (account == null || instrument == null)
            {
                validationReason = "missing_account_or_instrument";
                return false;
            }

            string instrumentRoot = GetInstrumentRoot(instrument);
            int netQty = GetNetQuantityForInstrumentRoot(account, instrumentRoot);
            if (netQty == 0)
            {
                validationReason = "flat_position";
                return false;
            }

            double averagePrice = TryGetOpenPositionAveragePrice(account, instrumentRoot);
            if (averagePrice <= 0)
            {
                validationReason = "missing_average_price";
                return false;
            }

            double tickSize = instrument.MasterInstrument != null && instrument.MasterInstrument.TickSize > 0
                ? instrument.MasterInstrument.TickSize
                : 0;
            if (tickSize <= 0)
            {
                validationReason = "missing_tick_size";
                return false;
            }

            string typeText = orderType.ToString();
            if (typeText.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0 && stopPrice > 0)
            {
                if (!IsProtectiveStopPriceValid(netQty, stopPrice, averagePrice, tickSize))
                {
                    validationReason = "stop_on_wrong_side_of_average";
                    return false;
                }
            }

            if (typeText.IndexOf("Limit", StringComparison.OrdinalIgnoreCase) >= 0 && limitPrice > 0)
            {
                if (!IsProtectiveLimitPriceValid(netQty, limitPrice, averagePrice, tickSize))
                {
                    validationReason = "limit_on_wrong_side_of_average";
                    return false;
                }
            }

            return true;
        }

        private static bool TryBuildMasterProtectiveTemplate(Account masterAccount, string instrumentRoot, int masterNetQty, out ProtectiveTemplate template)
        {
            template = null;
            if (masterAccount == null || string.IsNullOrWhiteSpace(instrumentRoot) || masterNetQty == 0)
                return false;

            Order stopCandidate = null;
            Order targetCandidate = null;
            foreach (Order order in GetWorkingOrdersForInstrumentRoot(masterAccount, instrumentRoot))
            {
                if (order == null || !IsExitOrderForNet(order, masterNetQty))
                    continue;

                if (IsStopLikeOrder(order))
                {
                    if (stopCandidate == null || Math.Abs(order.Quantity) > Math.Abs(stopCandidate.Quantity))
                        stopCandidate = order;
                    continue;
                }

                if (IsLimitLikeOrder(order))
                {
                    if (targetCandidate == null || Math.Abs(order.Quantity) > Math.Abs(targetCandidate.Quantity))
                        targetCandidate = order;
                }
            }

            double stopPrice = ExtractOrderPrice(stopCandidate, preferStopPrice: true);
            double targetPrice = ExtractOrderPrice(targetCandidate, preferStopPrice: false);
            bool hasStop = stopPrice > 0;
            bool hasTarget = targetPrice > 0;
            if (!hasStop && !hasTarget)
                return false;

            template = new ProtectiveTemplate
            {
                HasStop = hasStop,
                StopPrice = stopPrice,
                HasTarget = hasTarget,
                TargetPrice = targetPrice
            };
            return true;
        }

        private bool SubmitProtectiveOrder(
            Account account,
            Instrument instrument,
            OrderAction action,
            int quantity,
            OrderType orderType,
            double limitPrice,
            double stopPrice,
            string ocoId,
            string signalName)
        {
            if (account == null || instrument == null || quantity <= 0)
                return false;

            string primaryOcoId = ocoId ?? string.Empty;
            Exception submissionError;
            if (TrySubmitProtectiveOrderInternal(
                account,
                instrument,
                action,
                quantity,
                orderType,
                limitPrice,
                stopPrice,
                primaryOcoId,
                signalName,
                out submissionError))
            {
                return true;
            }

            if (IsOcoReuseRejection(submissionError))
            {
                string retryOcoId = BuildProtectiveOcoId(account.Name, GetInstrumentRoot(instrument));
                if (!string.Equals(retryOcoId, primaryOcoId, StringComparison.OrdinalIgnoreCase))
                {
                    Exception retryError;
                    if (TrySubmitProtectiveOrderInternal(
                        account,
                        instrument,
                        action,
                        quantity,
                        orderType,
                        limitPrice,
                        stopPrice,
                        retryOcoId,
                        signalName,
                        out retryError))
                    {
                        return true;
                    }

                    submissionError = retryError ?? submissionError;
                }
            }

            if (submissionError != null)
            {
                AppendJournal(
                    account.Name,
                    "Replication",
                    $"Protective submit failed on {CleanJournalToken(GetInstrumentRoot(instrument))}: {CleanJournalToken(submissionError.Message)}");
            }

            return false;
        }

        private static bool TrySubmitProtectiveOrderInternal(
            Account account,
            Instrument instrument,
            OrderAction action,
            int quantity,
            OrderType orderType,
            double limitPrice,
            double stopPrice,
            string ocoId,
            string signalName,
            out Exception error)
        {
            error = null;
            try
            {
                Order order = account.CreateOrder(
                    instrument,
                    action,
                    orderType,
                    GetPreferredFollowerOrderEntry(),
                    TimeInForce.Day,
                    quantity,
                    limitPrice,
                    stopPrice,
                    ocoId ?? string.Empty,
                    signalName ?? string.Empty,
                    DateTime.MaxValue,
                    null);
                if (order == null)
                {
                    error = new InvalidOperationException("CreateOrder returned null.");
                    return false;
                }

                account.Submit(new[] { order });
                if (order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled)
                {
                    error = new InvalidOperationException("Order was rejected by broker on submit.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        private static bool IsOcoReuseRejection(Exception ex)
        {
            string message = ex == null ? string.Empty : ex.Message ?? string.Empty;
            if (string.IsNullOrWhiteSpace(message))
                return false;

            bool mentionsOco = message.IndexOf("OCO", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!mentionsOco)
                return false;

            return message.IndexOf("reuse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("reused", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("already used", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool CancelReplicatedProtectiveOrders(Account account, string instrumentRoot)
        {
            if (account == null || string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            List<Order> protectiveOrders = GetWorkingOrdersForInstrumentRoot(account, instrumentRoot)
                .Where(IsReplicatedProtectiveOrder)
                .ToList();
            if (protectiveOrders.Count == 0)
                return false;

            return CancelOrders(account, protectiveOrders);
        }

        private static bool CancelOrders(Account account, IReadOnlyCollection<Order> orders)
        {
            if (account == null || orders == null || orders.Count == 0)
                return false;

            Order[] orderArray = orders
                .Where(order =>
                    order != null &&
                    IsWorkingOrderState(order.OrderState) &&
                    !IsOrderCancellationInFlight(order))
                .Distinct()
                .ToArray();
            if (orderArray.Length == 0)
                return false;

            try
            {
                account.Cancel(orderArray);
                return true;
            }
            catch
            {
            }

            return false;
        }

        private bool TryMarkReplicationConflictNotification(string followerInstrumentKey, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(followerInstrumentKey))
                return false;

            string cooldownKey = "CONFLICT|" + followerInstrumentKey.Trim();
            lock (_replicationOrderLock)
            {
                if (_replicationSubmitCooldownByKey.TryGetValue(cooldownKey, out DateTime cooldownUntilUtc) &&
                    cooldownUntilUtc > nowUtc)
                {
                    return false;
                }

                _replicationSubmitCooldownByKey[cooldownKey] = nowUtc.AddSeconds(30);
                return true;
            }
        }

        private static bool IsOrderCancellationInFlight(Order order)
        {
            if (order == null)
                return false;

            string stateText = order.OrderState.ToString();
            if (string.IsNullOrWhiteSpace(stateText))
                return false;

            return stateText.IndexOf("Cancel", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<Order> GetWorkingOrdersForInstrumentRoot(Account account, string instrumentRoot)
        {
            return GlitchReplicationEngine.GetWorkingOrdersForInstrumentRoot(account, instrumentRoot);
        }

        private static bool IsWorkingOrderState(OrderState state)
        {
            return GlitchReplicationEngine.IsWorkingOrderState(state);
        }

        private static bool IsReplicatedProtectiveOrder(Order order)
        {
            return GlitchReplicationEngine.IsReplicatedProtectiveOrder(order, ProtectiveStopSignalName, ProtectiveTargetSignalName);
        }

        private static bool IsStopLikeOrder(Order order)
        {
            return GlitchReplicationEngine.IsStopLikeOrder(order);
        }

        private static bool IsLimitLikeOrder(Order order)
        {
            return GlitchReplicationEngine.IsLimitLikeOrder(order);
        }

        private static bool IsExitOrderForNet(Order order, int netQty)
        {
            return GlitchReplicationEngine.IsExitOrderForNet(order, netQty);
        }

        private static int GetOrderActionSign(OrderAction action)
        {
            return GlitchReplicationEngine.GetOrderActionSign(action);
        }

        private static double ExtractOrderPrice(Order order, bool preferStopPrice)
        {
            return GlitchReplicationEngine.ExtractOrderPrice(order, preferStopPrice);
        }

        private static string BuildProtectiveOcoId(string accountName, string instrumentRoot)
        {
            return GlitchReplicationEngine.BuildProtectiveOcoId(accountName, instrumentRoot);
        }

        private static int ComputeStablePositiveHash(string value)
        {
            return GlitchReplicationEngine.ComputeStablePositiveHash(value);
        }

        private static int RoundConservativeContracts(double rawQuantity)
        {
            return GlitchReplicationEngine.RoundConservativeContracts(rawQuantity);
        }

        private static int ResolveFollowerInstrumentContractCap(AccountGridRow followerRow, string instrumentRoot, IDictionary<string, bool> microRootCache = null)
        {
            if (followerRow == null)
                return 0;

            int maxContracts = followerRow.MaxContractsRaw > 0
                ? (int)Math.Round(followerRow.MaxContractsRaw, MidpointRounding.AwayFromZero)
                : 0;
            int maxMicros = followerRow.MaxMicrosRaw > 0
                ? (int)Math.Round(followerRow.MaxMicrosRaw, MidpointRounding.AwayFromZero)
                : 0;
            double microMultiplier = followerRow.MicroContractMultiplier > 0
                ? followerRow.MicroContractMultiplier
                : DefaultMicroContractMultiplier;

            if (IsMicroContractRoot(instrumentRoot, followerRow.MicroContractRootRegex, microRootCache))
            {
                if (maxMicros > 0)
                    return maxMicros;

                if (maxContracts > 0)
                    return Math.Max(1, (int)Math.Round(maxContracts * microMultiplier, MidpointRounding.AwayFromZero));

                return 0;
            }

            if (maxContracts > 0)
                return maxContracts;

            if (maxMicros > 0)
                return Math.Max(1, (int)Math.Round(maxMicros / microMultiplier, MidpointRounding.AwayFromZero));

            return 0;
        }

        private static bool IsMicroContractRoot(string instrumentRoot, string pattern, IDictionary<string, bool> cache = null)
        {
            if (string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            string root = instrumentRoot.Trim().ToUpperInvariant();
            string regexPattern = string.IsNullOrWhiteSpace(pattern) ? DefaultMicroContractRootRegex : pattern.Trim();
            if (regexPattern.Length > MaxMicroContractRegexLength)
                regexPattern = DefaultMicroContractRootRegex;
            string cacheKey = regexPattern + "|" + root;
            if (cache != null && cache.TryGetValue(cacheKey, out bool cached))
                return cached;

            bool isMatch;
            try
            {
                isMatch = Regex.IsMatch(
                    root,
                    regexPattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    MicroContractRegexTimeout);
            }
            catch (RegexMatchTimeoutException)
            {
                isMatch = Regex.IsMatch(
                    root,
                    DefaultMicroContractRootRegex,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    MicroContractRegexTimeout);
            }
            catch
            {
                isMatch = false;
            }

            if (cache != null)
                cache[cacheKey] = isMatch;

            return isMatch;
        }

        private static double ResolveContractUnitWeight(AccountGridRow followerRow, string instrumentRoot, IDictionary<string, bool> microRootCache = null)
        {
            if (followerRow == null)
                return 1.0;

            if (!IsMicroContractRoot(instrumentRoot, followerRow.MicroContractRootRegex, microRootCache))
                return 1.0;

            double multiplier = followerRow.MicroContractMultiplier > 0
                ? followerRow.MicroContractMultiplier
                : DefaultMicroContractMultiplier;
            if (multiplier <= 0)
                return 1.0;

            return 1.0 / multiplier;
        }

        private void ApplyAggregateContractCap(
            Dictionary<string, ReplicationIntent> intentsByKey,
            ISet<string> conflictingIntentKeys,
            Dictionary<string, AccountGridRow> rowsByAccount,
            IDictionary<string, bool> microRootCache = null)
        {
            if (intentsByKey == null || intentsByKey.Count == 0 || rowsByAccount == null || rowsByAccount.Count == 0)
                return;

            foreach (var group in intentsByKey.Values
                .Where(intent =>
                    intent != null &&
                    intent.FollowerAccount != null &&
                    !string.IsNullOrWhiteSpace(intent.InstrumentRoot) &&
                    (conflictingIntentKeys == null || string.IsNullOrWhiteSpace(intent.Key) || !conflictingIntentKeys.Contains(intent.Key)))
                .GroupBy(intent => intent.FollowerAccount.Name?.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                string followerName = group.Key;
                if (string.IsNullOrWhiteSpace(followerName))
                    continue;
                if (!rowsByAccount.TryGetValue(followerName, out AccountGridRow followerRow) || followerRow == null)
                    continue;

                double cap = followerRow.MaxContractsRaw;
                if (double.IsNaN(cap) || double.IsInfinity(cap) || cap <= 0)
                    continue;

                List<ReplicationIntent> followerIntents = group.ToList();
                var unitWeightByIntent = new Dictionary<ReplicationIntent, double>();
                foreach (ReplicationIntent intent in followerIntents)
                    unitWeightByIntent[intent] = ResolveContractUnitWeight(followerRow, intent.InstrumentRoot, microRootCache);

                double totalUnits = followerIntents.Sum(intent => Math.Abs(intent.TargetNetQty) * unitWeightByIntent[intent]);
                if (totalUnits <= cap + 1e-8)
                    continue;

                double scale = cap / totalUnits;
                if (scale <= 0)
                    scale = 0;

                foreach (ReplicationIntent intent in followerIntents)
                {
                    int absQty = Math.Abs(intent.TargetNetQty);
                    int scaledQty = RoundConservativeContracts(absQty * scale);
                    if (scaledQty > absQty)
                        scaledQty = absQty;
                    intent.TargetNetQty = intent.TargetNetQty < 0 ? -scaledQty : scaledQty;
                }

                double adjustedUnits = followerIntents.Sum(intent => Math.Abs(intent.TargetNetQty) * unitWeightByIntent[intent]);
                while (adjustedUnits > cap + 1e-8)
                {
                    ReplicationIntent reducer = null;
                    double maxContribution = double.MinValue;
                    foreach (ReplicationIntent intent in followerIntents)
                    {
                        int absQty = Math.Abs(intent.TargetNetQty);
                        if (absQty <= 0)
                            continue;

                        double contribution = absQty * unitWeightByIntent[intent];
                        if (contribution > maxContribution)
                        {
                            maxContribution = contribution;
                            reducer = intent;
                        }
                    }
                    if (reducer == null)
                        break;

                    double reducerWeight = unitWeightByIntent[reducer];
                    reducer.TargetNetQty = reducer.TargetNetQty > 0 ? reducer.TargetNetQty - 1 : reducer.TargetNetQty + 1;
                    adjustedUnits -= reducerWeight;
                }
            }
        }

        private void AppendReplicationStructuredJournal(
            string accountName,
            string eventType,
            string reasonCode,
            string details)
        {
            string evt = string.IsNullOrWhiteSpace(eventType) ? "unknown" : CleanJournalToken(eventType);
            string reason = string.IsNullOrWhiteSpace(reasonCode) ? "none" : CleanJournalToken(reasonCode);
            string payload = string.IsNullOrWhiteSpace(details) ? string.Empty : details.Trim();
            string message = string.IsNullOrWhiteSpace(payload)
                ? $"SYNC|event={evt}|reason={reason}"
                : $"SYNC|event={evt}|reason={reason}|{payload}";
            AppendJournal(string.IsNullOrWhiteSpace(accountName) ? "System" : accountName, "Replication", message);
        }

        private int ResolveDeclaredContractCap(AccountGridRow row, string instrumentRoot, IDictionary<string, bool> microRootCache = null)
        {
            int capFromRow = ResolveFollowerInstrumentContractCap(row, instrumentRoot, microRootCache);
            int capFromPolicy = _runtimePolicySettings != null ? Math.Max(0, _runtimePolicySettings.ReplicationDeclaredCapContracts) : 0;
            if (capFromRow > 0 && capFromPolicy > 0)
                return Math.Min(capFromRow, capFromPolicy);
            if (capFromRow > 0)
                return capFromRow;
            return capFromPolicy;
        }

        private bool IsReplicationFrozen(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return false;
            return _replicationFrozenKeys.Contains(accountName.Trim());
        }

        private void FreezeReplicationForAccount(string accountName, ReplicationVetoReason reason, string instrumentRoot, string details)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return;

            string normalized = accountName.Trim();
            if (_replicationFrozenKeys.Add(normalized))
            {
                string reasonCode = reason.ToString();
                string instrumentToken = string.IsNullOrWhiteSpace(instrumentRoot) ? "-" : CleanJournalToken(instrumentRoot);
                string detailText = string.IsNullOrWhiteSpace(details) ? string.Empty : details.Trim();
                AppendReplicationStructuredJournal(
                    normalized,
                    "replication_frozen",
                    reasonCode,
                    $"instrument={instrumentToken}|detail={CleanJournalToken(detailText)}");
            }

            string message = $"Replication frozen for {normalized} on {CleanJournalToken(instrumentRoot)}. {details}";
            RaiseCriticalWarning(
                normalized,
                message,
                "ReplicationFreeze",
                unlocksTrading: _runtimePolicySettings == null || _runtimePolicySettings.FreezeRequiresManualAcknowledge);
        }

        private int ClampReplicationDelta(int deltaNetQty, out bool clamped)
        {
            clamped = false;
            return deltaNetQty;
        }

        private bool DetectReplicationBurst(string key, int observedQty, DateTime nowUtc, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            int windowMs = _runtimePolicySettings != null
                ? Math.Max(250, _runtimePolicySettings.ReplicationBurstWindowMs)
                : 1000;
            int fillThreshold = _runtimePolicySettings != null
                ? Math.Max(2, _runtimePolicySettings.ReplicationBurstFillCountThreshold)
                : 4;
            int qtyJumpThreshold = _runtimePolicySettings != null
                ? Math.Max(2, _runtimePolicySettings.ReplicationBurstQtyJumpThreshold)
                : 6;

            if (!_replicationBurstStateByKey.TryGetValue(key, out ReplicationBurstState state) || state == null)
            {
                _replicationBurstStateByKey[key] = new ReplicationBurstState
                {
                    WindowStartUtc = nowUtc,
                    LastObservedQty = observedQty,
                    QtyChangeCount = 0
                };
                return false;
            }

            if ((nowUtc - state.WindowStartUtc).TotalMilliseconds > windowMs)
            {
                state.WindowStartUtc = nowUtc;
                state.LastObservedQty = observedQty;
                state.QtyChangeCount = 0;
                return false;
            }

            int delta = Math.Abs(observedQty - state.LastObservedQty);
            if (delta > 0)
            {
                state.QtyChangeCount++;
                state.LastObservedQty = observedQty;
            }

            if (delta >= qtyJumpThreshold)
            {
                reason = $"qty_jump_{delta}";
                return true;
            }

            if (state.QtyChangeCount >= fillThreshold)
            {
                reason = $"qty_changes_{state.QtyChangeCount}";
                return true;
            }

            return false;
        }

        private static Position FindOpenPositionForInstrumentRoot(Account account, string instrumentRoot)
        {
            if (account == null || string.IsNullOrWhiteSpace(instrumentRoot))
                return null;

            try
            {
                return account.Positions.FirstOrDefault(position =>
                    position != null &&
                    position.Instrument != null &&
                    position.MarketPosition != MarketPosition.Flat &&
                    string.Equals(GetInstrumentRoot(position.Instrument), instrumentRoot, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private bool EnsureFollowerEmergencyStop(ReplicationIntent intent, int followerNetQty)
        {
            if (intent?.FollowerAccount == null || string.IsNullOrWhiteSpace(intent.InstrumentRoot) || followerNetQty == 0)
                return false;

            Position position = FindOpenPositionForInstrumentRoot(intent.FollowerAccount, intent.InstrumentRoot);
            if (position == null || position.Instrument == null)
                return false;

            Instrument instrument = position.Instrument;
            double tickSize = instrument.MasterInstrument != null && instrument.MasterInstrument.TickSize > 0
                ? instrument.MasterInstrument.TickSize
                : 0;
            if (tickSize <= 0)
                return false;

            int stopTicks = _runtimePolicySettings != null
                ? Math.Max(2, _runtimePolicySettings.FollowerEmergencyStopTicks)
                : 20;
            double averagePrice = position.AveragePrice;
            if (averagePrice <= 0 || double.IsNaN(averagePrice) || double.IsInfinity(averagePrice))
                return false;

            OrderAction exitAction = followerNetQty > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
            int quantity = Math.Abs(followerNetQty);
            double stopPrice = followerNetQty > 0
                ? averagePrice - (stopTicks * tickSize)
                : averagePrice + (stopTicks * tickSize);
            stopPrice = instrument.MasterInstrument.RoundToTickSize(stopPrice);

            List<Order> protectiveOrders = GetWorkingOrdersForInstrumentRoot(intent.FollowerAccount, intent.InstrumentRoot)
                .Where(IsReplicatedProtectiveOrder)
                .ToList();
            Order existingStop = protectiveOrders.FirstOrDefault(IsStopLikeOrder);
            string ocoId = BuildProtectiveOcoId(intent.FollowerAccount.Name, intent.InstrumentRoot);
            bool changed = EnsureProtectiveOrder(
                intent.FollowerAccount,
                instrument,
                existingStop,
                shouldExist: true,
                action: exitAction,
                quantity: quantity,
                orderType: OrderType.StopMarket,
                limitPrice: 0.0,
                stopPrice: stopPrice,
                ocoId: ocoId,
                signalName: ProtectiveStopSignalName);

            if (changed)
            {
                AppendReplicationStructuredJournal(
                    intent.FollowerAccount.Name,
                    "emergency_stop",
                    "master_template_missing",
                    $"instrument={CleanJournalToken(intent.InstrumentRoot)}|qty={quantity}|stop={stopPrice.ToString(CultureInfo.InvariantCulture)}");
            }

            return changed;
        }

        private static List<string> GetSyncInstrumentRoots(Account masterAccount, Account followerAccount)
        {
            return GlitchReplicationEngine.GetSyncInstrumentRoots(masterAccount, followerAccount);
        }

        private static string GetInstrumentRoot(Instrument instrument)
        {
            return GlitchReplicationEngine.GetInstrumentRoot(instrument);
        }

        private static int GetNetQuantityForInstrumentRoot(Account account, string instrumentRoot)
        {
            return GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(account, instrumentRoot);
        }

        private static Instrument FindInstrumentForInstrumentRoot(Account account, string instrumentRoot)
        {
            return GlitchReplicationEngine.FindInstrumentForInstrumentRoot(account, instrumentRoot);
        }

        private static List<Instrument> GetOpenPositionInstruments(Account account)
        {
            return GlitchReplicationEngine.GetOpenPositionInstruments(account);
        }

        private static bool IsAccountFlat(Account account)
        {
            return GlitchReplicationEngine.IsAccountFlat(account);
        }

        private static bool HasAnyWorkingOrders(Account account)
        {
            return GlitchReplicationEngine.HasAnyWorkingOrders(account);
        }

        private static async Task<bool> WaitForAllAccountsFlatAsync(IReadOnlyList<Account> accounts, TimeSpan timeout)
        {
            return await GlitchReplicationEngine.WaitForAllAccountsFlatAsync(accounts, timeout);
        }

        private bool UseLegacyReplicationEngine()
        {
            return _runtimePolicySettings != null && _runtimePolicySettings.UseLegacyReplicationEngine;
        }

        private void RefreshCopyEngineConfiguration(IReadOnlyList<Account> activeAccounts)
        {
            if (_copyEngine == null)
                return;

            if (!_isReplicatingUi || UseLegacyReplicationEngine())
            {
                _copyEngine.Configure(false, null);
                return;
            }

            var accountsByName = (activeAccounts ?? Array.Empty<Account>())
                .Where(account => account != null && !string.IsNullOrWhiteSpace(account.Name))
                .GroupBy(account => account.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            var routes = new List<GlitchCopyFollowerRoute>();
            foreach (AccountGroupDefinition group in _accountGroups ?? new ObservableCollection<AccountGroupDefinition>())
            {
                if (group == null || string.IsNullOrWhiteSpace(group.MasterAccount) || group.Members == null)
                    continue;

                string masterName = group.MasterAccount.Trim();
                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member == null || !member.IsEnabled || string.IsNullOrWhiteSpace(member.FollowerAccount))
                        continue;
                    if (double.IsNaN(member.Ratio) || double.IsInfinity(member.Ratio) || member.Ratio <= 0)
                        continue;
                    if (!accountsByName.TryGetValue(member.FollowerAccount.Trim(), out Account followerAccount) || followerAccount == null)
                        continue;

                    routes.Add(new GlitchCopyFollowerRoute
                    {
                        MasterAccount = masterName,
                        FollowerAccount = followerAccount,
                        Ratio = member.Ratio
                    });
                }
            }

            _copyEngine.Configure(true, routes);
        }

        private void TryProcessCopyExecutionFromRuntimeEvent(string eventName, Account account, object eventArgs)
        {
            if (account == null || eventArgs == null || _copyEngine == null)
                return;
            if (!_isReplicatingUi || UseLegacyReplicationEngine())
                return;
            if (!string.Equals(eventName, "ExecutionUpdate", StringComparison.OrdinalIgnoreCase))
                return;
            if (!TryBuildCopyExecutionContext(eventArgs, out GlitchCopyExecutionContext context))
                return;

            _copyEngine.ProcessMasterExecution(account, context);
        }

        private bool TryBuildCopyExecutionContext(object eventArgs, out GlitchCopyExecutionContext context)
        {
            context = null;
            object executionObject = TryGetNestedPropertyValue(eventArgs, "Execution") ?? eventArgs;
            if (executionObject == null)
                return false;

            string executionId = TryGetNestedPropertyValueAsString(executionObject, "ExecutionId", "Id");
            string quantityText = TryGetNestedPropertyValueAsString(executionObject, "Quantity");
            if (!int.TryParse(quantityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity) || quantity <= 0)
                return false;

            Instrument instrument = TryGetNestedPropertyValue(executionObject, "Instrument") as Instrument;
            if (instrument == null)
                return false;

            Order order = TryGetNestedPropertyValue(executionObject, "Order") as Order;
            string signalName = order?.Name;
            if (IsReplicationInternalSignal(signalName) ||
                (!string.IsNullOrWhiteSpace(signalName) &&
                 signalName.StartsWith(GlitchCopyEngine.CopySignalName, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            OrderAction action;
            if (order != null)
            {
                action = order.OrderAction;
            }
            else if (!TryParseOrderActionToken(
                         TryGetNestedPropertyValueAsString(executionObject, "Order.OrderAction", "OrderAction", "MarketPosition"),
                         out action))
            {
                return false;
            }

            context = new GlitchCopyExecutionContext
            {
                ExecutionId = executionId,
                Instrument = instrument,
                Action = action,
                Quantity = quantity,
                OrderSignalName = signalName
            };
            return true;
        }

        private static bool TryParseOrderActionToken(string actionToken, out OrderAction action)
        {
            action = OrderAction.Buy;
            if (string.IsNullOrWhiteSpace(actionToken))
                return false;

            string normalized = actionToken.Trim();
            if (normalized.Equals("Buy", StringComparison.OrdinalIgnoreCase))
            {
                action = OrderAction.Buy;
                return true;
            }

            if (normalized.Equals("Sell", StringComparison.OrdinalIgnoreCase))
            {
                action = OrderAction.Sell;
                return true;
            }

            if (normalized.Equals("SellShort", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Sell Short", StringComparison.OrdinalIgnoreCase))
            {
                action = OrderAction.SellShort;
                return true;
            }

            if (normalized.Equals("BuyToCover", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Buy To Cover", StringComparison.OrdinalIgnoreCase))
            {
                action = OrderAction.BuyToCover;
                return true;
            }

            return Enum.TryParse(normalized, true, out action);
        }

        private void CancelGlitchWorkingOrdersOnFollowers(IReadOnlyList<Account> activeAccounts)
        {
            foreach (Account account in activeAccounts ?? Array.Empty<Account>())
            {
                if (account == null)
                    continue;

                try
                {
                    foreach (Order order in account.Orders.ToArray())
                    {
                        if (order == null || string.IsNullOrWhiteSpace(order.Name))
                            continue;
                        if (!IsGlitchOwnedWorkingOrder(order))
                            continue;
                        if (!GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                            continue;

                        account.Cancel(new[] { order });
                    }
                }
                catch
                {
                }
            }
        }

        private static bool IsGlitchOwnedWorkingOrder(Order order)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.Name))
                return false;

            string name = order.Name.Trim();
            return name.StartsWith(GlitchCopyEngine.CopySignalName, StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("GLT-SYNC", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("GLT-PROT-", StringComparison.OrdinalIgnoreCase);
        }

        private void EvaluateReplicationDrift(IReadOnlyList<Account> activeAccounts)
        {
            _replicationDriftNotice = null;
            if (!_isReplicatingUi || UseLegacyReplicationEngine() || _accountGroups == null || _accountGroups.Count == 0)
            {
                UpdateReplicationDriftBanner();
                return;
            }

            var accountsByName = (activeAccounts ?? Array.Empty<Account>())
                .Where(account => account != null && !string.IsNullOrWhiteSpace(account.Name))
                .GroupBy(account => account.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            foreach (AccountGroupDefinition group in _accountGroups)
            {
                if (group == null || string.IsNullOrWhiteSpace(group.MasterAccount) || group.Members == null)
                    continue;
                if (!accountsByName.TryGetValue(group.MasterAccount.Trim(), out Account masterAccount) || masterAccount == null)
                    continue;

                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member == null || !member.IsEnabled || string.IsNullOrWhiteSpace(member.FollowerAccount))
                        continue;
                    if (double.IsNaN(member.Ratio) || double.IsInfinity(member.Ratio) || member.Ratio <= 0)
                        continue;
                    if (!accountsByName.TryGetValue(member.FollowerAccount.Trim(), out Account followerAccount) || followerAccount == null)
                        continue;

                    foreach (string instrumentRoot in GetSyncInstrumentRoots(masterAccount, followerAccount))
                    {
                        if (string.IsNullOrWhiteSpace(instrumentRoot))
                            continue;

                        int masterNetQty = GetNetQuantityForInstrumentRoot(masterAccount, instrumentRoot);
                        int expectedQty = (int)Math.Round(
                            masterNetQty * member.Ratio,
                            MidpointRounding.AwayFromZero);
                        int actualQty = GetNetQuantityForInstrumentRoot(followerAccount, instrumentRoot);
                        if (actualQty == expectedQty)
                            continue;

                        _replicationDriftNotice = new ReplicationDriftNotice
                        {
                            FollowerAccount = followerAccount.Name?.Trim(),
                            MasterAccount = masterAccount,
                            FollowerAccountRef = followerAccount,
                            InstrumentRoot = instrumentRoot,
                            ActualQty = actualQty,
                            ExpectedQty = expectedQty,
                            Ratio = member.Ratio
                        };
                        UpdateReplicationDriftBanner();
                        return;
                    }
                }
            }

            UpdateReplicationDriftBanner();
        }

        private void OnReplicationDriftSyncButtonClick(object sender, RoutedEventArgs e)
        {
            if (_replicationDriftNotice == null)
                return;

            ReplicationDriftNotice notice = _replicationDriftNotice;
            Instrument instrument = FindInstrumentForInstrumentRoot(notice.FollowerAccountRef, notice.InstrumentRoot);
            if (instrument == null)
                return;

            int deltaQty = notice.ExpectedQty - notice.ActualQty;
            if (deltaQty == 0)
                return;

            DeltaSubmitResult result = SubmitDeltaOrder(notice.FollowerAccountRef, instrument, deltaQty, out string failureReason);
            AppendJournal(
                notice.FollowerAccount,
                "Replication",
                $"user_sync|origin=user_sync_button|instrument={CleanJournalToken(notice.InstrumentRoot)}|actual={notice.ActualQty}|expected={notice.ExpectedQty}|delta={deltaQty}|result={result}");
            if (result != DeltaSubmitResult.Accepted)
            {
                RaiseCriticalWarning(
                    notice.FollowerAccount,
                    $"Manual sync failed on {notice.InstrumentRoot}: {failureReason ?? result.ToString()}",
                    $"UserSyncFailed|{CleanJournalToken(notice.InstrumentRoot)}",
                    unlocksTrading: false);
            }
        }

        private void UpdateReplicationDriftBanner()
        {
            if (_headerReplicationDriftBanner == null || _headerReplicationDriftText == null)
                return;

            if (_replicationDriftNotice == null)
            {
                _headerReplicationDriftBanner.Visibility = Visibility.Collapsed;
                _headerReplicationDriftText.Text = string.Empty;
                return;
            }

            _headerReplicationDriftText.Text =
                $"Follower {_replicationDriftNotice.FollowerAccount} differs from ratio target (has {_replicationDriftNotice.ActualQty}, ratio implies {_replicationDriftNotice.ExpectedQty}) on {CleanJournalToken(_replicationDriftNotice.InstrumentRoot)}.";
            _headerReplicationDriftBanner.Visibility = Visibility.Visible;
        }
    }
}

