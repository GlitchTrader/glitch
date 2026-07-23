//
// R04 portfolio snapshot capture from live account grid + NT positions.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Glitch.Services;
using NinjaTrader.Cbi;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
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
                IsReplicating = IsReplicationEnabledFromExternalSurface(),
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
            double liquidationThreshold = row.NetLiqRaw;
            double bufferMargin = row.BufferMarginRaw;
            if (simulateApexLegacyEval && ruleFirm != null && maxDrawdown > 0 && row.EquityRaw > 0)
            {
                string maxLossTracking = NormalizeMaxLossTracking(ruleFirm.MaxLossTracking, ruleFirm.DrawdownType);
                double trailingPeak = GetOrUpdateTrailingPeak(
                    BuildPeakStateKey(row.DisplayName, maxLossTracking),
                    row.EquityRaw);
                double? modeledThreshold = CalculateMinMargin(
                    ruleStatus,
                    ruleFirm,
                    row.AccountSizeRaw,
                    maxDrawdown,
                    profitTarget,
                    row.RealizedPnlRaw,
                    row.EquityRaw,
                    trailingPeak,
                    GetExecutionProviderHint(account));
                if (modeledThreshold.HasValue)
                {
                    liquidationThreshold = modeledThreshold.Value;
                    bufferMargin = row.EquityRaw - liquidationThreshold;
                }
            }
            double headroomRatio = maxDrawdown > 0 && !double.IsNaN(bufferMargin)
                ? bufferMargin / maxDrawdown
                : double.NaN;
            bool positionsAvailable = TryBuildPortfolioSnapshotPositions(
                account,
                out List<GlitchPortfolioSnapshotPositionRecord> positions);
            bool ordersAvailable = TryBuildPortfolioSnapshotOrders(
                account,
                out List<GlitchPortfolioSnapshotOrderRecord> workingOrders);
            int workingOrderCount = workingOrders.Count;
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
                LiquidationThreshold = liquidationThreshold,
                BufferMargin = bufferMargin,
                HeadroomRatio = headroomRatio,
                RealizedPnl = row.RealizedPnlRaw,
                UnrealizedPnl = liveUnrealizedPnl,
                TotalPnl = row.RealizedPnlRaw + liveUnrealizedPnl,
                PositionDisplay = livePositionDisplay,
                NativeStateAvailable = positionsAvailable && ordersAvailable,
                WorkingOrderCount = workingOrderCount,
                MaxContracts = ruleMaxContracts > 0 ? ruleMaxContracts : row.MaxContractsRaw,
                IsRiskLocked = _riskLockedAccounts.Contains(row.DisplayName),
                IsEvalTargetLocked = _evalTargetLockedAccounts.Contains(row.DisplayName),
                TradingStartTime = ruleFirm?.TradingStartTime,
                TradingEndTime = ruleFirm?.TradingEndTime,
                Positions = positions,
                WorkingOrderDetails = workingOrders
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

        private static bool TryBuildPortfolioSnapshotPositions(
            Account account,
            out List<GlitchPortfolioSnapshotPositionRecord> records)
        {
            records = new List<GlitchPortfolioSnapshotPositionRecord>();
            if (account?.Positions == null)
                return false;

            Stopwatch collectionLockStopwatch = Stopwatch.StartNew();
            try
            {
                lock (account.Positions)
                {
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
                }
                return true;
            }
            catch
            {
                records.Clear();
                return false;
            }
            finally
            {
                collectionLockStopwatch.Stop();
                GlitchHermesExchangeWriter.RecordNativePositionCollectionLockDuration(collectionLockStopwatch.Elapsed);
            }
        }

        private static bool TryBuildPortfolioSnapshotOrders(
            Account account,
            out List<GlitchPortfolioSnapshotOrderRecord> records)
        {
            records = new List<GlitchPortfolioSnapshotOrderRecord>();
            if (account?.Orders == null)
                return false;
            Stopwatch collectionLockStopwatch = Stopwatch.StartNew();
            try
            {
                lock (account.Orders)
                {
                    foreach (Order order in account.Orders)
                    {
                        if (order == null || !GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                            continue;
                        string instrumentFullName = order.Instrument?.FullName;
                        string instrumentRoot = order.Instrument?.MasterInstrument?.Name;
                        if (string.IsNullOrWhiteSpace(instrumentRoot))
                            instrumentRoot = instrumentFullName;
                        records.Add(new GlitchPortfolioSnapshotOrderRecord
                        {
                            InstrumentFullName = instrumentFullName,
                            InstrumentRoot = instrumentRoot,
                            Name = order.Name,
                            OrderType = order.OrderType.ToString(),
                            OrderState = order.OrderState.ToString(),
                            Oco = order.Oco,
                            Quantity = order.Quantity,
                            Filled = order.Filled,
                            LimitPrice = order.LimitPrice,
                            StopPrice = order.StopPrice
                        });
                    }
                }
                return true;
            }
            catch
            {
                records.Clear();
                return false;
            }
            finally
            {
                collectionLockStopwatch.Stop();
                GlitchHermesExchangeWriter.RecordNativeOrderCollectionLockDuration(collectionLockStopwatch.Elapsed);
            }
        }
    }
}
