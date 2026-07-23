using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Glitch.Services;
using NinjaTrader.Cbi;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private Task _aiDailyCloseInFlight;
        private DateTime _aiDailyCloseAttemptedForUtc = DateTime.MinValue;
        private DateTime _aiDailyCloseUnresolvedForUtc = DateTime.MinValue;

        private void MaybeEnforceAiDailyClose(IReadOnlyList<Account> activeAccounts)
        {
            // A visible, persisted opt-in is required before inspecting AI scope or accounts.
            if (_runtimePolicySettings == null
                || !_runtimePolicySettings.EnforceAiDailyClose
                || _isFlattenAllInProgress
                || (_aiDailyCloseInFlight != null && !_aiDailyCloseInFlight.IsCompleted))
                return;

            GlitchAiTradingWindowStatus window = GlitchAiTradingWindow.Evaluate(
                DateTime.UtcNow,
                "18:00:00",
                "16:59:00");
            if (!window.IsValid || window.IsEntryAllowed)
                return;

            DateTime mustFlatUtc = window.MustFlatUtc ?? DateTime.MinValue;
            if (mustFlatUtc == DateTime.MinValue || _aiDailyCloseAttemptedForUtc == mustFlatUtc)
                return;

            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            if (policy == null || !policy.IsValid || policy.AccountAllowlist == null
                || !policy.AccountAllowlist.Any(name => !string.IsNullOrWhiteSpace(name)))
            {
                if (_aiDailyCloseUnresolvedForUtc != mustFlatUtc)
                {
                    AppendJournal("System", "Risk", "ai_daily_close|origin=ai_auto|result=unresolved|reason=no_scope");
                    RaiseCriticalWarning(
                        "System",
                        "AI daily-close is enabled but its persisted account scope is invalid or empty; no account was changed.",
                        "AiDailyCloseNoScope",
                        unlocksTrading: false);
                    _aiDailyCloseUnresolvedForUtc = mustFlatUtc;
                }
                return;
            }

            IReadOnlyList<Account> connectedAccounts = activeAccounts ?? Array.Empty<Account>();
            var connectedNames = new HashSet<string>(
                connectedAccounts.Where(account => account != null && !string.IsNullOrWhiteSpace(account.Name))
                    .Select(account => account.Name.Trim()),
                StringComparer.OrdinalIgnoreCase);
            var requiredNames = new HashSet<string>(
                policy.AccountAllowlist
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim()),
                StringComparer.OrdinalIgnoreCase);
            foreach (string selectedName in requiredNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!connectedNames.Contains(selectedName))
                {
                    if (_aiDailyCloseUnresolvedForUtc != mustFlatUtc)
                    {
                        AppendJournal(
                            selectedName,
                            "Risk",
                            "ai_daily_close|origin=ai_auto|result=unresolved|reason=account_unavailable");
                    }
                    RaiseCriticalWarning(
                        selectedName,
                        "AI daily-close compliance cannot verify this selected account because it is disconnected or unavailable.",
                        "AiDailyCloseAccountUnavailable",
                        unlocksTrading: false);
                }
            }
            _aiDailyCloseUnresolvedForUtc = mustFlatUtc;

            List<Account> exposedAccounts = connectedAccounts.Where(candidate => candidate != null
                && !string.IsNullOrWhiteSpace(candidate.Name)
                && requiredNames.Contains(candidate.Name.Trim())
                && (!GlitchReplicationEngine.IsAccountFlat(candidate)
                    || GlitchReplicationEngine.HasAnyWorkingOrders(candidate)))
                .ToList();
            if (exposedAccounts.Count == 0)
                return;

            _aiDailyCloseAttemptedForUtc = mustFlatUtc;
            _aiDailyCloseInFlight = ExecuteAiDailyCloseAsync(exposedAccounts);
        }

        private async Task ExecuteAiDailyCloseAsync(IReadOnlyList<Account> accounts)
        {
            _isFlattenAllInProgress = true;
            try
            {
                foreach (Account account in accounts)
                {
                    string result = GlitchReplicationEngine.TryFlattenAccount(account, out int instrumentCount)
                        ? "issued"
                        : "state_unavailable";
                    AppendJournal(
                        account.Name,
                        "Risk",
                        "ai_daily_close|origin=ai_auto|result=" + result
                            + "|instruments=" + instrumentCount);
                }

                bool flat = await WaitForAllAccountsFlatAsync(accounts, TimeSpan.FromSeconds(8));
                if (!flat)
                {
                    AppendJournal(
                        "System",
                        "Risk",
                        "ai_daily_close|origin=ai_auto|result=unresolved|reason=not_flat_or_working");
                    RaiseCriticalWarning(
                        "System",
                        "AI daily-close compliance could not confirm every selected account flat and order-free.",
                        "AiDailyCloseIncomplete",
                        unlocksTrading: false);
                }
                else
                {
                    AppendJournal("System", "Risk", "ai_daily_close|origin=ai_auto|result=flat_order_free");
                }
            }
            catch (Exception ex)
            {
                RecordSubsystemFault("ai_daily_close", ex);
                RaiseCriticalWarning(
                    "System",
                    "AI daily-close compliance failed: " + ex.GetType().Name,
                    "AiDailyCloseFailed",
                    unlocksTrading: false);
            }
            finally
            {
                _isFlattenAllInProgress = false;
            }
        }

    }
}
