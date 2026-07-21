//
// R04 portfolio snapshot capture from live account grid + NT positions.
//

using System;
using System.Collections.Generic;
using System.Linq;
using Glitch.Services;
using NinjaTrader.Cbi;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private bool MaybeWritePortfolioSnapshot(DateTime nowUtc, string snapshotId = null)
        {
            GlitchPortfolioSnapshotCapture capture = BuildPortfolioSnapshotCapture();
            if (capture == null)
                return false;

            return GlitchPortfolioSnapshotWriter.TryWriteLatestIfDue(nowUtc, capture, snapshotId);
        }

        private GlitchPortfolioSnapshotCapture BuildPortfolioSnapshotCapture()
        {
            if (_accountRows == null || _accountRows.Count == 0)
                return null;

            var accountsByName = GetActiveAccountsSnapshot()
                .Where(account => account != null && !string.IsNullOrWhiteSpace(account.Name))
                .GroupBy(account => account.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var records = new List<GlitchPortfolioSnapshotAccountRecord>();
            for (int i = 0; i < _accountRows.Count; i++)
            {
                AccountGridRow row = _accountRows[i];
                if (row == null || string.IsNullOrWhiteSpace(row.DisplayName))
                    continue;

                accountsByName.TryGetValue(row.DisplayName.Trim(), out Account account);
                records.Add(BuildPortfolioSnapshotAccountRecord(row, account));
            }

            if (records.Count == 0)
                return null;

            GlitchPortfolioSnapshotWriter.TryResolvePropFirmRulesVersion(
                out string rulesSchema,
                out string rulesUpdatedAt);

            return new GlitchPortfolioSnapshotCapture
            {
                IsReplicating = IsReplicationRuntimeActive(),
                PropFirmRulesSchemaVersion = rulesSchema,
                PropFirmRulesUpdatedAtUtc = rulesUpdatedAt,
                Accounts = records
            };
        }

        private GlitchPortfolioSnapshotAccountRecord BuildPortfolioSnapshotAccountRecord(AccountGridRow row, Account account)
        {
            bool simulateApexLegacyEval = string.Equals(row.AccountStatus, "Sim", StringComparison.OrdinalIgnoreCase);
            string ruleFirmId = simulateApexLegacyEval ? "ApexTraderFunding" : row.PropFirmId;
            string ruleStatus = simulateApexLegacyEval ? "Eval" : row.AccountStatus;
            FirmTierRule rule = GetRuleForFirmAndSize(
                ruleFirmId,
                ruleStatus,
                GetExecutionProviderHint(account),
                row.AccountSizeRaw,
                0);
            _firmRules.TryGetValue(ruleFirmId ?? string.Empty, out FirmRuleMetadata ruleFirm);
            double ruleMaxContracts = ResolveMaxContractsLimit(rule, ResolveMicroContractMultiplier(ruleFirm));
            double profitTarget = rule?.ProfitTarget ?? row.ProfitTargetRaw;
            double maxDrawdown = rule?.MaxDrawdown ?? row.MaxDrawdownRaw;
            double dailyLossLimit = rule?.DailyLossLimit ?? row.DailyLossLimitRaw;
            double headroomRatio = maxDrawdown > 0 && !double.IsNaN(row.BufferMarginRaw)
                ? row.BufferMarginRaw / maxDrawdown
                : double.NaN;
            List<GlitchPortfolioSnapshotPositionRecord> positions = BuildPortfolioSnapshotPositions(account);
            double liveUnrealizedPnl = positions.Sum(position => position.UnrealizedPnl);
            string livePositionDisplay = BuildPortfolioPositionDisplay(positions);

            return new GlitchPortfolioSnapshotAccountRecord
            {
                AccountName = row.DisplayName,
                AccountStatus = row.AccountStatus,
                PropFirmId = ruleFirmId,
                RuleStatus = ruleStatus,
                RulesAreSimulated = simulateApexLegacyEval,
                AccountSize = row.AccountSizeRaw,
                ProfitTarget = profitTarget,
                MaxDrawdown = maxDrawdown,
                DailyLossLimit = dailyLossLimit,
                Equity = row.EquityRaw,
                LiquidationThreshold = row.NetLiqRaw,
                BufferMargin = row.BufferMarginRaw,
                HeadroomRatio = headroomRatio,
                RealizedPnl = row.RealizedPnlRaw,
                UnrealizedPnl = liveUnrealizedPnl,
                TotalPnl = row.RealizedPnlRaw + liveUnrealizedPnl,
                PositionDisplay = livePositionDisplay,
                WorkingOrderCount = account?.Orders == null
                    ? 0
                    : account.Orders.Count(order => order != null && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)),
                MaxContracts = ruleMaxContracts > 0 ? ruleMaxContracts : row.MaxContractsRaw,
                IsRiskLocked = _riskLockedAccounts.Contains(row.DisplayName),
                IsEvalTargetLocked = _evalTargetLockedAccounts.Contains(row.DisplayName),
                Positions = positions
            };
        }

        private static string BuildPortfolioPositionDisplay(IEnumerable<GlitchPortfolioSnapshotPositionRecord> positions)
        {
            double signedContracts = 0;
            foreach (GlitchPortfolioSnapshotPositionRecord position in positions ?? Enumerable.Empty<GlitchPortfolioSnapshotPositionRecord>())
            {
                double quantity = Math.Abs(position.Quantity);
                if (string.Equals(position.MarketPosition, MarketPosition.Long.ToString(), StringComparison.OrdinalIgnoreCase))
                    signedContracts += quantity;
                else if (string.Equals(position.MarketPosition, MarketPosition.Short.ToString(), StringComparison.OrdinalIgnoreCase))
                    signedContracts -= quantity;
            }

            return FormatSignedContracts(signedContracts);
        }

        private static List<GlitchPortfolioSnapshotPositionRecord> BuildPortfolioSnapshotPositions(Account account)
        {
            var records = new List<GlitchPortfolioSnapshotPositionRecord>();
            if (account?.Positions == null)
                return records;

            foreach (Position position in account.Positions)
            {
                if (position == null || position.MarketPosition == MarketPosition.Flat)
                    continue;

                double unrealized = 0;
                try
                {
                    unrealized = position.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
                }
                catch
                {
                }

                string instrumentFullName = position.Instrument?.FullName;
                string instrumentRoot = position.Instrument?.MasterInstrument?.Name;
                if (string.IsNullOrWhiteSpace(instrumentRoot))
                    instrumentRoot = instrumentFullName;

                records.Add(new GlitchPortfolioSnapshotPositionRecord
                {
                    InstrumentFullName = instrumentFullName,
                    InstrumentRoot = instrumentRoot,
                    MarketPosition = position.MarketPosition.ToString(),
                    Quantity = Math.Abs(position.Quantity),
                    AveragePrice = position.AveragePrice,
                    UnrealizedPnl = unrealized
                });
            }

            return records;
        }
    }
}
