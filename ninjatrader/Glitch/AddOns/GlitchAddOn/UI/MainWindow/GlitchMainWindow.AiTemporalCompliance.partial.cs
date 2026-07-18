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

        private void MaybeEnforceAiDailyClose(IReadOnlyList<Account> activeAccounts)
        {
            if (GlitchHermesControlStateStore.Load().TradingPaused
                || _isFlattenAllInProgress
                || (_aiDailyCloseInFlight != null && !_aiDailyCloseInFlight.IsCompleted))
                return;

            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            if (policy == null || !policy.IsValid || policy.AccountAllowlist == null)
                return;

            GlitchAiTradingWindowStatus window = GlitchAiTradingWindow.Evaluate(
                DateTime.UtcNow,
                "18:00:00",
                "16:59:00");
            if (!window.IsValid || window.IsEntryAllowed)
                return;

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
            foreach (AccountGroupDefinition group in _accountGroups ?? new System.Collections.ObjectModel.ObservableCollection<AccountGroupDefinition>())
            {
                if (group == null
                    || string.IsNullOrWhiteSpace(group.MasterAccount)
                    || !requiredNames.Contains(group.MasterAccount.Trim())
                    || group.Members == null)
                    continue;

                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member == null || member.IsMasterRow || !member.IsEnabled
                        || string.IsNullOrWhiteSpace(member.FollowerAccount))
                        continue;
                    requiredNames.Add(member.FollowerAccount.Trim());
                }
            }

            foreach (string selectedName in requiredNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!connectedNames.Contains(selectedName))
                {
                    RaiseCriticalWarning(
                        selectedName,
                        "AI daily-close compliance cannot verify this selected account because it is disconnected or unavailable.",
                        "AiDailyCloseAccountUnavailable",
                        unlocksTrading: false);
                }
            }

            List<Account> exposedAccounts = connectedAccounts.Where(candidate => candidate != null
                && !string.IsNullOrWhiteSpace(candidate.Name)
                && requiredNames.Contains(candidate.Name.Trim())
                && IsApexOrSimAccount(candidate)
                && (!GlitchReplicationEngine.IsAccountFlat(candidate)
                    || GlitchReplicationEngine.HasAnyWorkingOrders(candidate)))
                .ToList();
            if (exposedAccounts.Count == 0)
                return;

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
                    RaiseCriticalWarning(
                        "System",
                        "AI daily-close compliance could not confirm every selected account flat and order-free.",
                        "AiDailyCloseIncomplete",
                        unlocksTrading: false);
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

        private static bool IsApexOrSimAccount(Account account)
        {
            if (account == null)
                return false;
            if ((account.Name ?? string.Empty).StartsWith("Sim", StringComparison.OrdinalIgnoreCase))
                return true;

            string firm = GlitchComplianceEngine.InferPropFirmId(account, out _);
            return !string.IsNullOrWhiteSpace(firm)
                && firm.StartsWith("Apex", StringComparison.OrdinalIgnoreCase);
        }
    }
}
