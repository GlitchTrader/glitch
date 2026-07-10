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
        private void MaybeWritePortfolioSnapshot(DateTime nowUtc, string snapshotId = null)
        {
            GlitchPortfolioSnapshotCapture capture = BuildPortfolioSnapshotCapture();
            if (capture == null)
                return;

            GlitchPortfolioSnapshotWriter.TryWriteLatestIfDue(nowUtc, capture, snapshotId);
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
                IsReplicating = _isReplicatingUi,
                PropFirmRulesSchemaVersion = rulesSchema,
                PropFirmRulesUpdatedAtUtc = rulesUpdatedAt,
                Accounts = records
            };
        }

        private GlitchPortfolioSnapshotAccountRecord BuildPortfolioSnapshotAccountRecord(AccountGridRow row, Account account)
        {
            double headroomRatio = row.MaxDrawdownRaw > 0 && !double.IsNaN(row.BufferMarginRaw)
                ? row.BufferMarginRaw / row.MaxDrawdownRaw
                : double.NaN;

            return new GlitchPortfolioSnapshotAccountRecord
            {
                AccountName = row.DisplayName,
                AccountStatus = row.AccountStatus,
                PropFirmId = row.PropFirmId,
                AccountSize = row.AccountSizeRaw,
                ProfitTarget = row.ProfitTargetRaw,
                MaxDrawdown = row.MaxDrawdownRaw,
                DailyLossLimit = row.DailyLossLimitRaw,
                Equity = row.EquityRaw,
                LiquidationThreshold = row.NetLiqRaw,
                BufferMargin = row.BufferMarginRaw,
                HeadroomRatio = headroomRatio,
                RealizedPnl = row.RealizedPnlRaw,
                UnrealizedPnl = row.UnrealizedPnlRaw,
                TotalPnl = row.TotalPnlRaw,
                PositionDisplay = row.Position,
                MaxContracts = row.MaxContractsRaw,
                IsRiskLocked = _riskLockedAccounts.Contains(row.DisplayName),
                IsEvalTargetLocked = _evalTargetLockedAccounts.Contains(row.DisplayName),
                Positions = BuildPortfolioSnapshotPositions(account)
            };
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
