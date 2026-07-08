using System;

namespace Glitch.Services
{
    /// <summary>
    /// Pure risk trigger evaluation for UI and enforcement. Side effects live in GlitchMainWindow.ApplyEnabledRiskActions.
    /// </summary>
    public static class GlitchRiskMitigationEngine
    {
        public sealed class RiskMitigationTriggers
        {
            public bool EnforceBufferFreeze { get; set; }
            public bool EnforceOneContract { get; set; }
            public bool EnforceUnrealizedFlatten { get; set; }
            public bool EnforceEvalLock { get; set; }
            public bool CriticalBufferBreach { get; set; }
            public bool EvalProfitTargetReached { get; set; }
            public bool OneContractBreach { get; set; }
            public bool OneContractRecovery { get; set; }
            public bool UnrealizedLossBreach { get; set; }
            public double BufferFreezeThreshold { get; set; }
            public double OneContractOnThreshold { get; set; }
            public double OneContractOffThreshold { get; set; }
            public double UnrealizedFlattenThreshold { get; set; }
        }

        public static RiskMitigationTriggers ComputeTriggers(GlitchRuntimePolicySettings settings, AccountRiskRowSnapshot row)
        {
            if (settings == null || row == null)
                return new RiskMitigationTriggers();

            var triggers = new RiskMitigationTriggers
            {
                EnforceBufferFreeze = settings.IsBufferFreezeEnabledFor(row.AccountStatus),
                EnforceOneContract = settings.IsBufferOneContractEnabledFor(row.AccountStatus),
                EnforceUnrealizedFlatten = settings.IsUnrealizedFlattenEnabledFor(row.AccountStatus),
                EnforceEvalLock = settings.IsEvalProfitTargetLockEnabledFor(row.AccountStatus),
                BufferFreezeThreshold = settings.BufferFreezeThresholdRatio,
                OneContractOnThreshold = settings.BufferOneContractOnThresholdRatio,
                OneContractOffThreshold = settings.BufferOneContractOffThresholdRatio,
                UnrealizedFlattenThreshold = settings.UnrealizedFlattenThresholdRatio
            };

            triggers.CriticalBufferBreach = IsBufferCriticalRiskTriggered(row, triggers.BufferFreezeThreshold);
            triggers.EvalProfitTargetReached = triggers.EnforceEvalLock && IsEvalProfitTargetLockTriggered(row);
            triggers.OneContractBreach = IsBufferOneContractRiskTriggered(row, triggers.OneContractOnThreshold);
            triggers.OneContractRecovery = IsBufferOneContractRecoveryTriggered(row, triggers.OneContractOffThreshold);
            triggers.UnrealizedLossBreach = IsUnrealizedLossRiskTriggered(row, triggers.UnrealizedFlattenThreshold);
            return triggers;
        }

        public static string ResolveScopeSettingKey(string ruleKeyPrefix, string accountStatus)
        {
            string normalized = NormalizeComplianceAccountStatus(accountStatus);
            if (string.Equals(normalized, "Sim", StringComparison.OrdinalIgnoreCase))
                return ruleKeyPrefix + "_SIM";
            if (string.Equals(normalized, "Eval", StringComparison.OrdinalIgnoreCase))
                return ruleKeyPrefix + "_EVAL";
            if (string.Equals(normalized, "AP", StringComparison.OrdinalIgnoreCase))
                return ruleKeyPrefix + "_AP";
            return ruleKeyPrefix;
        }

        public static string BuildRuleJournalEvent(
            string ruleId,
            string action,
            double observedValue,
            double thresholdValue,
            string authorizingSetting,
            string detail = null)
        {
            string line = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "rule={0}|action={1}|observed={2:F2}|threshold={3:F2}|setting={4}",
                ruleId,
                action,
                observedValue,
                thresholdValue,
                authorizingSetting ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(detail))
                line += "|detail=" + detail;

            return line;
        }

        public static bool IsUnrealizedLossRiskTriggered(AccountRiskRowSnapshot row, double thresholdRatio)
        {
            if (row == null || row.IntratradeDrawdownRaw <= 0 || double.IsNaN(row.IntratradeDrawdownRaw) || double.IsInfinity(row.IntratradeDrawdownRaw))
                return false;
            if (double.IsNaN(row.UnrealizedPnlRaw) || double.IsInfinity(row.UnrealizedPnlRaw))
                return false;

            double unrealizedLoss = Math.Max(0, -row.UnrealizedPnlRaw);
            return unrealizedLoss > (thresholdRatio * row.IntratradeDrawdownRaw);
        }

        public static bool IsEvalProfitTargetLockTriggered(AccountRiskRowSnapshot row)
        {
            if (row == null)
                return false;
            if (!string.Equals(row.AccountStatus, "Eval", StringComparison.OrdinalIgnoreCase))
                return false;
            if (double.IsNaN(row.EvalProfitTargetLockBalanceRaw) || double.IsInfinity(row.EvalProfitTargetLockBalanceRaw))
                return false;
            if (row.EvalProfitTargetLockBalanceRaw <= 0)
                return false;
            if (double.IsNaN(row.EquityRaw) || double.IsInfinity(row.EquityRaw))
                return false;

            return row.EquityRaw >= row.EvalProfitTargetLockBalanceRaw;
        }

        public static bool IsBufferCriticalRiskTriggered(AccountRiskRowSnapshot row, double thresholdRatio)
        {
            if (row == null || row.MaxDrawdownRaw <= 0 || double.IsNaN(row.MaxDrawdownRaw) || double.IsInfinity(row.MaxDrawdownRaw))
                return false;
            if (double.IsNaN(row.BufferMarginRaw) || double.IsInfinity(row.BufferMarginRaw))
                return false;

            return row.BufferMarginRaw < (thresholdRatio * row.MaxDrawdownRaw);
        }

        public static bool IsBufferOneContractRiskTriggered(AccountRiskRowSnapshot row, double thresholdRatio)
        {
            if (row == null || row.MaxDrawdownRaw <= 0 || double.IsNaN(row.MaxDrawdownRaw) || double.IsInfinity(row.MaxDrawdownRaw))
                return false;
            if (double.IsNaN(row.BufferMarginRaw) || double.IsInfinity(row.BufferMarginRaw))
                return false;

            return row.BufferMarginRaw < (thresholdRatio * row.MaxDrawdownRaw);
        }

        public static bool IsBufferOneContractRecoveryTriggered(AccountRiskRowSnapshot row, double releaseThresholdRatio)
        {
            if (row == null || row.MaxDrawdownRaw <= 0 || double.IsNaN(row.MaxDrawdownRaw) || double.IsInfinity(row.MaxDrawdownRaw))
                return false;
            if (double.IsNaN(row.BufferMarginRaw) || double.IsInfinity(row.BufferMarginRaw))
                return false;

            return row.BufferMarginRaw >= (releaseThresholdRatio * row.MaxDrawdownRaw);
        }

        private static string NormalizeComplianceAccountStatus(string accountStatus)
        {
            string token = (accountStatus ?? string.Empty).Trim();
            if (token.Length == 0)
                return "Sim";

            if (token.Equals("PA", StringComparison.OrdinalIgnoreCase))
                return "AP";

            return token;
        }
    }

    public sealed class AccountRiskRowSnapshot
    {
        public string AccountStatus { get; set; }
        public double BufferMarginRaw { get; set; }
        public double MaxDrawdownRaw { get; set; }
        public double IntratradeDrawdownRaw { get; set; }
        public double UnrealizedPnlRaw { get; set; }
        public double EquityRaw { get; set; }
        public double EvalProfitTargetLockBalanceRaw { get; set; }
        public double MaxContractsRaw { get; set; }
    }
}
