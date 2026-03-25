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
// by GlitchTrader.com
//
// __________________________________________________
// __________________________________________________
//

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NinjaTrader.Cbi;

namespace Glitch.Services
{
    public static class GlitchComplianceEngine
    {
        public static double ResolveMaxContractsLimit(double maxContracts, double maxMicros)
        {
            return ResolveMaxContractsLimit(maxContracts, maxMicros, 10.0);
        }

        public static double ResolveMaxContractsLimit(double maxContracts, double maxMicros, double microMultiplier)
        {
            if (maxContracts > 0)
                return Math.Round(maxContracts, MidpointRounding.AwayFromZero);

            // Backward compatibility for older rules that only provide micro-equivalent limits.
            if (maxMicros > 0)
            {
                double ratio = microMultiplier > 0 ? microMultiplier : 10.0;
                return Math.Round(maxMicros / ratio, MidpointRounding.AwayFromZero);
            }

            return 0;
        }

        public static double ResolveMaxMicrosLimit(double maxMicros, double maxContracts)
        {
            return ResolveMaxMicrosLimit(maxMicros, maxContracts, 10.0);
        }

        public static double ResolveMaxMicrosLimit(double maxMicros, double maxContracts, double microMultiplier)
        {
            // Legacy helper retained for compatibility with older callsites.
            if (maxMicros > 0)
                return Math.Round(maxMicros, MidpointRounding.AwayFromZero);

            if (maxContracts > 0)
            {
                double ratio = microMultiplier > 0 ? microMultiplier : 10.0;
                return Math.Round(maxContracts * ratio, MidpointRounding.AwayFromZero);
            }

            return 0;
        }

        public static string NormalizeAccountStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "Eval";

            string value = status.Trim();
            if (value.Equals("Sim", StringComparison.OrdinalIgnoreCase))
                return "Sim";
            if (value.Equals("AP", StringComparison.OrdinalIgnoreCase) || value.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                return "AP";
            return "Eval";
        }

        public static string InferPropFirmId(Account account, out string confidence)
        {
            confidence = "Low";
            if (account == null)
                return "None";

            string accountName = account.Name ?? string.Empty;
            string connectionName = TryGetNestedPropertyValueAsString(account, "Connection.Name", "Connection.Options.Name", "Connection.Options.Provider", "Connection.Options.BrandName");

            string upperName = accountName.ToUpperInvariant();
            string upperConnection = (connectionName ?? string.Empty).ToUpperInvariant();
            bool hasWealthCharts = upperConnection.Contains("WEALTHCHARTS") || upperName.Contains("WEALTHCHARTS");
            bool hasApex = upperConnection.Contains("APEX") || upperName.Contains("APEX") || hasWealthCharts;
            bool hasIntraday = upperName.Contains("INTRADAY") || upperConnection.Contains("INTRADAY");
            bool hasEod = upperName.Contains(" EOD") || upperName.StartsWith("EOD", StringComparison.OrdinalIgnoreCase) ||
                          upperName.Contains("-EOD") || upperConnection.Contains("EOD") ||
                          upperName.Contains("END OF DAY") || upperConnection.Contains("END OF DAY");

            if (hasWealthCharts)
            {
                confidence = upperConnection.Contains("WEALTHCHARTS") ? "High" : "Medium";
                return "WealthCharts";
            }

            if (hasApex)
            {
                confidence = upperConnection.Contains("APEX") ? "High" : "Medium";
                if (hasIntraday)
                    return "ApexIntraday";
                if (hasEod)
                    return "ApexEod";
                return "ApexTraderFunding";
            }

            if (upperConnection.Contains("TAKEPROFIT") || upperConnection.Contains("TPT") ||
                upperName.Contains("TAKEPROFIT") || upperName.Contains("TPT"))
            {
                confidence = upperConnection.Contains("TAKEPROFIT") || upperConnection.Contains("TPT") ? "High" : "Medium";
                return "TakeProfitTrader";
            }

            if (upperConnection.Contains("TRADEDAY") || upperName.Contains("TRADEDAY"))
            {
                confidence = upperConnection.Contains("TRADEDAY") ? "High" : "Medium";
                return "TradeDay";
            }

            return "None";
        }

        public static string InferAccountStatus(Account account, string firmId, out string confidence)
        {
            confidence = "Low";
            if (account == null)
                return "Eval";

            string accountName = (account.Name ?? string.Empty).Trim();
            string upperName = accountName.ToUpperInvariant();
            if (upperName.StartsWith("SIM", StringComparison.OrdinalIgnoreCase))
            {
                confidence = "High";
                return "Sim";
            }

            string connectionMode = TryGetNestedPropertyValueAsString(account, "Connection.Options.Mode", "Connection.Mode");
            if (!string.IsNullOrWhiteSpace(connectionMode) &&
                connectionMode.IndexOf("sim", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                confidence = "High";
                return "Sim";
            }

            if (upperName.Contains(" PA") || upperName.StartsWith("PA", StringComparison.OrdinalIgnoreCase) ||
                upperName.Contains("_PA") || upperName.Contains("APPROVED") || upperName.Contains("FUNDED"))
            {
                confidence = "Medium";
                return "AP";
            }

            if (!string.Equals(firmId, "None", StringComparison.OrdinalIgnoreCase))
            {
                confidence = "Medium";
                return "Eval";
            }

            return "Eval";
        }

        public static string GetExecutionProviderHint(Account account)
        {
            if (account == null)
                return string.Empty;

            string provider = TryGetNestedPropertyValueAsString(
                account,
                "Connection.Options.Provider",
                "Connection.Provider",
                "Connection.Options.Name",
                "Connection.Name");
            if (string.IsNullOrWhiteSpace(provider))
                return string.Empty;

            string upper = provider.ToUpperInvariant();
            if (upper.Contains("WEALTHCHARTS"))
                return "WealthCharts";
            if (upper.Contains("RITHMIC"))
                return "Rithmic";
            if (upper.Contains("TRADOVATE"))
                return "Tradovate";

            return provider.Trim();
        }

        public static string NormalizeMaxLossTracking(string maxLossTracking, string drawdownType)
        {
            string normalized = (maxLossTracking ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                if (normalized.IndexOf("eod", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "TrailingEod";
                if (normalized.IndexOf("static", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Static";
                if (normalized.IndexOf("trailing", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "TrailingUnrealized";
            }

            string drawdown = (drawdownType ?? string.Empty).Trim();
            if (drawdown.IndexOf("eod", StringComparison.OrdinalIgnoreCase) >= 0)
                return "TrailingEod";
            if (drawdown.IndexOf("intraday", StringComparison.OrdinalIgnoreCase) >= 0)
                return "TrailingUnrealized";
            if (drawdown.IndexOf("trailing", StringComparison.OrdinalIgnoreCase) >= 0)
                return "TrailingUnrealized";

            return "Static";
        }

        public static string BuildPeakStateKey(string accountName, string maxLossTracking)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return accountName;

            string normalizedTracking = NormalizeMaxLossTracking(maxLossTracking, drawdownType: null);
            if (string.IsNullOrWhiteSpace(normalizedTracking))
                normalizedTracking = "TrailingUnrealized";

            return accountName.Trim() + "|" + normalizedTracking;
        }

        public static double TryGetNativeLiquidationThreshold(Account account)
        {
            if (account == null)
                return 0;

            double fromAccountItem = TryGetAccountItem(
                account,
                "MinimumAccountBalance",
                "MinAccountBalance",
                "AutoLiquidationThreshold",
                "LiquidationThreshold");
            if (fromAccountItem > 0)
                return fromAccountItem;

            return TryGetNestedPropertyValueAsDouble(
                account,
                "MinimumAccountBalance",
                "Risk.MinimumAccountBalance",
                "Risk.LiquidationThreshold",
                "Connection.MinimumAccountBalance");
        }

        public static double? NormalizeNativeThreshold(
            double nativeValue,
            double effectiveBalance,
            double? modeledThreshold,
            double accountSize,
            double maxDrawdown)
        {
            if (nativeValue <= 0 || double.IsNaN(nativeValue) || double.IsInfinity(nativeValue))
                return null;

            if (nativeValue >= accountSize * 0.5)
            {
                if (modeledThreshold.HasValue)
                {
                    double tolerance = Math.Max(500, maxDrawdown * 0.75);
                    if (Math.Abs(nativeValue - modeledThreshold.Value) <= tolerance)
                        return nativeValue;
                }
                else
                {
                    return nativeValue;
                }
            }

            if (effectiveBalance > 0)
            {
                double convertedFloor = effectiveBalance - nativeValue;
                if (convertedFloor > 0)
                {
                    if (modeledThreshold.HasValue)
                    {
                        double tolerance = Math.Max(500, maxDrawdown * 0.75);
                        if (Math.Abs(convertedFloor - modeledThreshold.Value) <= tolerance)
                            return convertedFloor;
                    }
                }
            }

            return null;
        }

        public static bool ShouldStopEvalThresholdAtProfitTarget(
            bool evalRithmicThresholdStopsAtProfitTarget,
            bool evalTradovateThresholdStopsAtProfitTarget,
            string executionProvider)
        {
            if (string.IsNullOrWhiteSpace(executionProvider))
                return false;

            bool isRithmic = executionProvider.IndexOf("rithmic", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isTradovate = executionProvider.IndexOf("tradovate", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isRithmic)
                return evalRithmicThresholdStopsAtProfitTarget;
            if (isTradovate)
                return evalTradovateThresholdStopsAtProfitTarget;

            return false;
        }

        public static bool StatusMatchesFilter(string status, string filterCsv)
        {
            if (string.IsNullOrWhiteSpace(filterCsv))
                return false;

            string normalizedStatus = NormalizeAccountStatus(status);
            var filters = filterCsv
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => NormalizeAccountStatus(token))
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToList();

            if (filters.Count == 0)
                return false;

            return filters.Any(token => string.Equals(token, normalizedStatus, StringComparison.OrdinalIgnoreCase));
        }

        public static string NormalizeFloorCapMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
                return "None";

            string normalized = mode.Trim();
            if (normalized.Equals("AtInitialBalance", StringComparison.OrdinalIgnoreCase))
                return "AtInitialBalance";
            if (normalized.Equals("AtInitialPlusOffset", StringComparison.OrdinalIgnoreCase))
                return "AtInitialPlusOffset";

            return "None";
        }

        public static string NormalizeFloorCapTrigger(string trigger)
        {
            if (string.IsNullOrWhiteSpace(trigger))
                return "None";

            string normalized = trigger.Trim();
            if (normalized.Equals("WhenThresholdReachesCap", StringComparison.OrdinalIgnoreCase))
                return "WhenThresholdReachesCap";
            if (normalized.Equals("WhenReferenceReachesInitialPlusDrawdownPlusOffset", StringComparison.OrdinalIgnoreCase))
                return "WhenReferenceReachesInitialPlusDrawdownPlusOffset";
            if (normalized.Equals("WhenReferenceReachesInitialPlusDrawdown", StringComparison.OrdinalIgnoreCase))
                return "WhenReferenceReachesInitialPlusDrawdown";
            if (normalized.Equals("WhenRealizedProfitReachesDrawdownPlusOffset", StringComparison.OrdinalIgnoreCase))
                return "WhenRealizedProfitReachesDrawdownPlusOffset";
            if (normalized.Equals("Immediate", StringComparison.OrdinalIgnoreCase))
                return "Immediate";

            return "None";
        }

        public static double? CalculateMinMargin(
            string accountStatus,
            string drawdownType,
            string maxLossTracking,
            string maxLossFloorCapMode,
            double maxLossFloorCapOffset,
            string maxLossFloorCapTrigger,
            string maxLossFloorCapStatuses,
            bool evalRithmicThresholdStopsAtProfitTarget,
            bool evalTradovateThresholdStopsAtProfitTarget,
            double accountSize,
            double maxDrawdown,
            double profitTarget,
            double realizedPnl,
            double currentEquity,
            double trailingPeak,
            string executionProvider)
        {
            bool evalThresholdStopsAtProfitTarget = ShouldStopEvalThresholdAtProfitTarget(
                evalRithmicThresholdStopsAtProfitTarget,
                evalTradovateThresholdStopsAtProfitTarget,
                executionProvider);

            return CalculateMinMargin(
                accountStatus,
                drawdownType,
                maxLossTracking,
                maxLossFloorCapMode,
                maxLossFloorCapOffset,
                maxLossFloorCapTrigger,
                maxLossFloorCapStatuses,
                evalThresholdStopsAtProfitTarget,
                0,
                accountSize,
                maxDrawdown,
                profitTarget,
                realizedPnl,
                currentEquity,
                trailingPeak);
        }

        public static double? CalculateMinMargin(
            string accountStatus,
            string drawdownType,
            string maxLossTracking,
            string maxLossFloorCapMode,
            double maxLossFloorCapOffset,
            string maxLossFloorCapTrigger,
            string maxLossFloorCapStatuses,
            bool evalThresholdStopsAtProfitTarget,
            double evalThresholdStopOffset,
            double accountSize,
            double maxDrawdown,
            double profitTarget,
            double realizedPnl,
            double currentEquity,
            double trailingPeak)
        {
            if (accountSize <= 0 || maxDrawdown <= 0)
                return null;

            string status = NormalizeAccountStatus(accountStatus);
            if (status == "Sim")
                return null;

            string normalizedTracking = NormalizeMaxLossTracking(maxLossTracking, drawdownType);

            double reference = trailingPeak > 0 ? trailingPeak : Math.Max(currentEquity, accountSize);
            double liquidationThreshold = string.Equals(normalizedTracking, "Static", StringComparison.OrdinalIgnoreCase)
                ? accountSize - maxDrawdown
                : reference - maxDrawdown;

            if (status == "Eval" &&
                profitTarget > 0 &&
                evalThresholdStopsAtProfitTarget)
            {
                double evalStopThreshold = accountSize + profitTarget + Math.Max(0, evalThresholdStopOffset);
                liquidationThreshold = Math.Min(liquidationThreshold, evalStopThreshold);
            }

            string capMode = NormalizeFloorCapMode(maxLossFloorCapMode);
            string capTrigger = NormalizeFloorCapTrigger(maxLossFloorCapTrigger);
            bool appliesCapToStatus = StatusMatchesFilter(status, maxLossFloorCapStatuses);
            if (!appliesCapToStatus && string.IsNullOrWhiteSpace(maxLossFloorCapStatuses))
                appliesCapToStatus = true;

            if (appliesCapToStatus && !string.Equals(capMode, "None", StringComparison.OrdinalIgnoreCase))
            {
                double capFloor = string.Equals(capMode, "AtInitialPlusOffset", StringComparison.OrdinalIgnoreCase)
                    ? accountSize + maxLossFloorCapOffset
                    : accountSize;

                bool shouldLock = false;
                switch (capTrigger)
                {
                    case "Immediate":
                        shouldLock = true;
                        break;
                    case "WhenThresholdReachesCap":
                        shouldLock = liquidationThreshold >= capFloor;
                        break;
                    case "WhenReferenceReachesInitialPlusDrawdownPlusOffset":
                        shouldLock = reference >= accountSize + maxDrawdown + maxLossFloorCapOffset;
                        break;
                    case "WhenReferenceReachesInitialPlusDrawdown":
                        shouldLock = reference >= accountSize + maxDrawdown;
                        break;
                    case "WhenRealizedProfitReachesDrawdownPlusOffset":
                        shouldLock = realizedPnl >= maxDrawdown + maxLossFloorCapOffset;
                        break;
                }

                if (shouldLock)
                    liquidationThreshold = capFloor;
            }

            return liquidationThreshold;
        }

        private static double TryGetAccountItem(Account account, params string[] itemNames)
        {
            if (account == null || itemNames == null || itemNames.Length == 0)
                return 0;

            foreach (var itemName in itemNames)
            {
                if (string.IsNullOrWhiteSpace(itemName))
                    continue;

                try
                {
                    if (Enum.TryParse(itemName, true, out AccountItem item))
                        return account.Get(item, Currency.UsDollar);
                }
                catch
                {
                }
            }

            return 0;
        }

        private static double TryGetNestedPropertyValueAsDouble(object root, params string[] propertyPaths)
        {
            if (root == null || propertyPaths == null)
                return 0;

            foreach (string path in propertyPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                object current = root;
                string[] segments = path.Split('.');
                bool failed = false;

                foreach (string segment in segments)
                {
                    if (current == null)
                    {
                        failed = true;
                        break;
                    }

                    PropertyInfo property = current.GetType().GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
                    if (property == null)
                    {
                        failed = true;
                        break;
                    }

                    current = property.GetValue(current, null);
                }

                if (failed || current == null)
                    continue;

                if (current is double d && !double.IsNaN(d) && !double.IsInfinity(d))
                    return d;
                if (current is decimal m)
                    return (double)m;
                if (current is float f && !float.IsNaN(f) && !float.IsInfinity(f))
                    return f;
                if (current is int i)
                    return i;
                if (current is long l)
                    return l;

                string text = current.ToString();
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && parsed > 0)
                    return parsed;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed) && parsed > 0)
                    return parsed;
            }

            return 0;
        }

        private static string TryGetNestedPropertyValueAsString(object root, params string[] propertyPaths)
        {
            if (root == null || propertyPaths == null)
                return null;

            foreach (string path in propertyPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                object current = root;
                string[] segments = path.Split('.');
                bool failed = false;

                foreach (string segment in segments)
                {
                    if (current == null)
                    {
                        failed = true;
                        break;
                    }

                    PropertyInfo property = current.GetType().GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
                    if (property == null)
                    {
                        failed = true;
                        break;
                    }

                    current = property.GetValue(current, null);
                }

                if (!failed && current != null)
                {
                    string value = current.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }

            return null;
        }
    }
}
