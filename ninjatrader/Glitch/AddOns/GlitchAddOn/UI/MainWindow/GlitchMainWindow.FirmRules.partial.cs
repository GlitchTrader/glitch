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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private static Dictionary<string, FirmRuleMetadata> LoadFirmRuleMetadata()
        {
            string jsonContent = TryReadPropFirmRulesJson();
            var parsed = ParseFirmRulesFromJson(jsonContent);
            if (parsed.Count > 0)
                return parsed;

            if (ShouldAllowDeveloperRulePathFallback())
                return BuildFallbackFirmRules();

            return new Dictionary<string, FirmRuleMetadata>(StringComparer.OrdinalIgnoreCase);
        }

        private static string TryReadPropFirmRulesJson()
        {
            foreach (string path in GetPropFirmRulesPathCandidates())
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        return File.ReadAllText(path);
                }
                catch
                {
                }
            }

            return null;
        }

        private static IEnumerable<string> GetPropFirmRulesPathCandidates()
        {
            var candidates = new List<string>();

            try
            {
                string userDir = NinjaTrader.Core.Globals.UserDataDir;
                if (!string.IsNullOrWhiteSpace(userDir))
                {
                    candidates.Add(Path.Combine(userDir, "bin", "Custom", "AddOns", "GlitchAddOn", "Resources", "PropFirmRules.json"));
                    candidates.Add(Path.Combine(userDir, "bin", "Custom", "Resources", "PropFirmRules.json"));
                    candidates.Add(Path.Combine(userDir, "bin", "Custom", "PropFirmRules.json"));
                }
            }
            catch
            {
            }

            try
            {
                string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrWhiteSpace(assemblyDir))
                {
                    candidates.Add(Path.Combine(assemblyDir, "Resources", "PropFirmRules.json"));
                    candidates.Add(Path.Combine(assemblyDir, "AddOns", "GlitchAddOn", "Resources", "PropFirmRules.json"));
                }
            }
            catch
            {
            }

            if (ShouldAllowDeveloperRulePathFallback())
            {
                try
                {
                    string cwd = Environment.CurrentDirectory;
                    if (!string.IsNullOrWhiteSpace(cwd))
                    {
                        candidates.Add(Path.Combine(cwd, "PropRules", "PropFirmRules.json"));
                        candidates.Add(Path.Combine(cwd, "Workspaces", "Alan", "Glitch", "AddOns", "GlitchAddOn", "Resources", "PropFirmRules.json"));
                    }
                }
                catch
                {
                }
            }

            return candidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool ShouldAllowDeveloperRulePathFallback()
        {
            string value = Environment.GetEnvironmentVariable("GLITCH_ALLOW_DEV_RULES_FALLBACK");
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim();
            return normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static int ResolveRuntimeIntSetting(string envName, int defaultValue, int minValue, int maxValue)
        {
            int fallback = ClampInt(defaultValue, minValue, maxValue);
            if (string.IsNullOrWhiteSpace(envName))
                return fallback;

            string raw = Environment.GetEnvironmentVariable(envName);
            if (string.IsNullOrWhiteSpace(raw))
                return fallback;

            string value = raw.Trim();
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ||
                int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
            {
                return ClampInt(parsed, minValue, maxValue);
            }

            return fallback;
        }

        private static int ClampInt(int value, int minValue, int maxValue)
        {
            if (minValue > maxValue)
                return value;
            if (value < minValue)
                return minValue;
            if (value > maxValue)
                return maxValue;
            return value;
        }

        private static Dictionary<string, FirmRuleMetadata> ParseFirmRulesFromJson(string jsonContent)
        {
            var firms = new Dictionary<string, FirmRuleMetadata>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(jsonContent))
                return firms;

            Dictionary<string, FirmRuleMetadata> structured = ParseFirmRulesFromJsonObject(jsonContent);
            if (structured.Count > 0)
                return structured;

            string globalDefaultsJson = ExtractObjectJson(jsonContent, "globalInstrumentSizingDefaults");
            string defaultMicroRegex = ExtractStringValue(globalDefaultsJson, "microContractRootRegex");
            if (string.IsNullOrWhiteSpace(defaultMicroRegex))
                defaultMicroRegex = DefaultMicroContractRootRegex;
            double defaultMicroMultiplier = ExtractNumberValueNullable(globalDefaultsJson, "microContractMultiplier") ?? DefaultMicroContractMultiplier;
            if (double.IsNaN(defaultMicroMultiplier) || double.IsInfinity(defaultMicroMultiplier) || defaultMicroMultiplier <= 0)
                defaultMicroMultiplier = DefaultMicroContractMultiplier;

            int firmsKey = jsonContent.IndexOf("\"firms\"", StringComparison.OrdinalIgnoreCase);
            if (firmsKey < 0)
                return firms;

            int arrayStart = jsonContent.IndexOf('[', firmsKey);
            if (arrayStart < 0)
                return firms;

            int arrayEnd = FindMatchingBracket(jsonContent, arrayStart);
            if (arrayEnd <= arrayStart)
                return firms;

            int pos = arrayStart + 1;
            while (pos < arrayEnd)
            {
                if (jsonContent[pos] == '{')
                {
                    int firmEnd = FindMatchingBrace(jsonContent, pos);
                    if (firmEnd > pos && firmEnd <= arrayEnd)
                    {
                        string firmJson = jsonContent.Substring(pos, firmEnd - pos + 1);
                        FirmRuleMetadata metadata = ParseFirmMetadata(firmJson, defaultMicroRegex, defaultMicroMultiplier);
                        if (metadata != null && !string.IsNullOrWhiteSpace(metadata.FirmId))
                            firms[metadata.FirmId] = metadata;

                        pos = firmEnd + 1;
                        continue;
                    }
                }

                pos++;
            }

            return firms;
        }

        private static Dictionary<string, FirmRuleMetadata> ParseFirmRulesFromJsonObject(string jsonContent)
        {
            var firms = new Dictionary<string, FirmRuleMetadata>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(jsonContent))
                return firms;

            IDictionary root = DeserializeJsonObject(jsonContent) as IDictionary;
            if (root == null)
                return firms;

            IDictionary globalDefaults = TryReadJsonDictionary(root, "globalInstrumentSizingDefaults");
            string defaultMicroRegex = TryReadJsonString(globalDefaults, "microContractRootRegex");
            if (string.IsNullOrWhiteSpace(defaultMicroRegex))
                defaultMicroRegex = DefaultMicroContractRootRegex;

            double defaultMicroMultiplier = TryReadJsonDouble(globalDefaults, "microContractMultiplier") ?? DefaultMicroContractMultiplier;
            if (double.IsNaN(defaultMicroMultiplier) || double.IsInfinity(defaultMicroMultiplier) || defaultMicroMultiplier <= 0)
                defaultMicroMultiplier = DefaultMicroContractMultiplier;

            IList rows = TryReadJsonList(root, "firms");
            if (rows == null || rows.Count == 0)
                return firms;

            foreach (object row in rows)
            {
                IDictionary firmRow = row as IDictionary;
                if (firmRow == null)
                    continue;

                FirmRuleMetadata metadata = ParseFirmMetadataFromJsonDictionary(
                    firmRow,
                    defaultMicroRegex,
                    defaultMicroMultiplier);
                if (metadata == null || string.IsNullOrWhiteSpace(metadata.FirmId))
                    continue;

                firms[metadata.FirmId] = metadata;
            }

            return firms;
        }

        private static FirmRuleMetadata ParseFirmMetadataFromJsonDictionary(
            IDictionary firmRow,
            string defaultMicroRegex,
            double defaultMicroMultiplier)
        {
            if (firmRow == null)
                return null;

            string firmId = TryReadJsonString(firmRow, "firmId");
            if (string.IsNullOrWhiteSpace(firmId))
                return null;

            IDictionary semantics = TryReadJsonDictionary(firmRow, "enforcementSemantics");
            string status = TryReadJsonString(firmRow, "status");
            if (string.IsNullOrWhiteSpace(status))
                status = "Supported";

            var metadata = new FirmRuleMetadata
            {
                FirmId = firmId,
                UiDisplayName = TryReadJsonString(firmRow, "uiDisplayName"),
                Status = status,
                DrawdownType = TryReadJsonString(semantics, "drawdownType") ?? TryReadJsonString(firmRow, "drawdownType"),
                MaxLossTracking = TryReadJsonString(semantics, "maxLossTracking"),
                MaxLossThresholdUpdate = TryReadJsonString(semantics, "maxLossThresholdUpdate"),
                MaxLossFloorCapMode = TryReadJsonString(semantics, "maxLossFloorCapMode"),
                MaxLossFloorCapOffset = TryReadJsonDouble(semantics, "maxLossFloorCapOffset") ?? 0,
                MaxLossFloorCapTrigger = TryReadJsonString(semantics, "maxLossFloorCapTrigger"),
                MaxLossFloorCapStatuses = TryReadJsonString(semantics, "maxLossFloorCapStatuses"),
                HasDailyLossLimit = TryReadJsonBoolean(semantics, "hasDailyLossLimit") ?? false,
                AllowSundayTrading = TryReadJsonBoolean(semantics, "allowSundayTrading") ?? true,
                MinimumTradingDays = (int)Math.Round(TryReadJsonDouble(semantics, "minimumTradingDays") ?? 0, MidpointRounding.AwayFromZero),
                ConsistencyRulePercent = TryReadJsonDouble(semantics, "consistencyRulePercent") ?? 0,
                TradingStartTime = TryReadJsonString(semantics, "tradingStartTime"),
                TradingEndTime = TryReadJsonString(semantics, "tradingEndTime"),
                EvalRithmicThresholdStopsAtProfitTarget =
                    TryReadJsonBoolean(semantics, "evalRithmicThresholdStopsAtProfitTarget") ?? false,
                EvalTradovateThresholdStopsAtProfitTarget =
                    TryReadJsonBoolean(semantics, "evalTradovateThresholdStopsAtProfitTarget") ?? false,
                EvalWealthChartsThresholdStopsAtProfitTarget =
                    TryReadJsonBoolean(semantics, "evalWealthChartsThresholdStopsAtProfitTarget") ?? false,
                EvalRithmicThresholdStopOffset =
                    TryReadJsonDouble(semantics, "evalRithmicThresholdStopOffset") ?? 0,
                EvalTradovateThresholdStopOffset =
                    TryReadJsonDouble(semantics, "evalTradovateThresholdStopOffset") ?? 0,
                EvalWealthChartsThresholdStopOffset =
                    TryReadJsonDouble(semantics, "evalWealthChartsThresholdStopOffset") ?? 0,
                EvalProfitTargetLockBuffer =
                    TryReadJsonDouble(semantics, "evalProfitTargetLockBuffer") ?? 20.0,
                UseNativeLiquidationThresholdWhenAvailable =
                    TryReadJsonBoolean(semantics, "useNativeLiquidationThresholdWhenAvailable") ?? true,
                MicroContractRootRegex = TryReadJsonString(semantics, "microContractRootRegex") ?? defaultMicroRegex,
                MicroContractMultiplier = TryReadJsonDouble(semantics, "microContractMultiplier") ?? defaultMicroMultiplier,
                ProviderRules = ParseFirmProviderRulesFromJsonDictionary(firmRow),
                Tiers = ParseFirmTiersFromJsonDictionary(firmRow)
            };

            if (string.IsNullOrWhiteSpace(metadata.MicroContractRootRegex))
                metadata.MicroContractRootRegex = DefaultMicroContractRootRegex;

            if (double.IsNaN(metadata.MicroContractMultiplier) ||
                double.IsInfinity(metadata.MicroContractMultiplier) ||
                metadata.MicroContractMultiplier <= 0)
            {
                metadata.MicroContractMultiplier = defaultMicroMultiplier > 0
                    ? defaultMicroMultiplier
                    : DefaultMicroContractMultiplier;
            }

            return metadata;
        }

        private static List<FirmTierRule> ParseFirmTiersFromJsonDictionary(IDictionary firmRow)
        {
            var tiers = new List<FirmTierRule>();
            IList rows = TryReadJsonList(firmRow, "tiers");
            if (rows == null || rows.Count == 0)
                return tiers;

            foreach (object row in rows)
            {
                IDictionary tierRow = row as IDictionary;
                if (tierRow == null)
                    continue;

                double accountSize = TryReadJsonDouble(tierRow, "accountSize") ?? 0;
                if (accountSize <= 0)
                    continue;

                tiers.Add(new FirmTierRule
                {
                    AccountSize = accountSize,
                    MaxContracts = TryReadJsonDouble(tierRow, "maxContracts") ?? 0,
                    MaxMicros = TryReadJsonDouble(tierRow, "maxMicros") ?? 0,
                    MaxDrawdown = TryReadJsonDouble(tierRow, "maxDrawdown") ?? 0,
                    IntratradeDrawdown = TryReadJsonDouble(tierRow, "intratradeDrawdown") ?? 0,
                    ProfitTarget = TryReadJsonDouble(tierRow, "profitTarget") ?? 0,
                    DailyLossLimit = TryReadJsonDouble(tierRow, "dailyLossLimit") ?? 0,
                    StatusFilter = TryReadJsonString(tierRow, "statusFilter") ?? TryReadJsonString(tierRow, "statuses"),
                    ProviderFilter = TryReadJsonString(tierRow, "providerFilter") ?? TryReadJsonString(tierRow, "providerHints"),
                    MinProfit = TryReadJsonDouble(tierRow, "minProfit") ?? 0,
                    MaxProfit = TryReadJsonDouble(tierRow, "maxProfit") ?? 0
                });
            }

            return tiers
                .OrderBy(tier => tier.AccountSize)
                .ThenBy(tier => tier.MinProfit)
                .ToList();
        }

        private static List<FirmProviderRule> ParseFirmProviderRulesFromJsonDictionary(IDictionary firmRow)
        {
            var providerRules = new List<FirmProviderRule>();
            IList rows = TryReadJsonList(firmRow, "providerRules");
            if (rows == null || rows.Count == 0)
                return providerRules;

            foreach (object row in rows)
            {
                IDictionary providerRow = row as IDictionary;
                if (providerRow == null)
                    continue;

                providerRules.Add(new FirmProviderRule
                {
                    ProviderFilter = TryReadJsonString(providerRow, "providerFilter") ?? TryReadJsonString(providerRow, "providerHints"),
                    EvalThresholdStopsAtProfitTarget = TryReadJsonBoolean(providerRow, "evalThresholdStopsAtProfitTarget"),
                    EvalThresholdStopOffset = TryReadJsonDouble(providerRow, "evalThresholdStopOffset") ?? 0
                });
            }

            return providerRules;
        }

        private static object TryReadJsonValue(IDictionary source, string key)
        {
            if (source == null || string.IsNullOrWhiteSpace(key))
                return null;

            foreach (DictionaryEntry entry in source)
            {
                string currentKey = entry.Key == null ? null : entry.Key.ToString();
                if (string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }

            return null;
        }

        private static IDictionary TryReadJsonDictionary(IDictionary source, string key)
        {
            return TryReadJsonValue(source, key) as IDictionary;
        }

        private static IList TryReadJsonList(IDictionary source, string key)
        {
            return TryReadJsonValue(source, key) as IList;
        }

        private static string TryReadJsonString(IDictionary source, string key)
        {
            object value = TryReadJsonValue(source, key);
            if (value == null)
                return null;

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static double? TryReadJsonDouble(IDictionary source, string key)
        {
            object value = TryReadJsonValue(source, key);
            if (value == null)
                return null;

            if (value is double doubleValue)
                return doubleValue;
            if (value is float floatValue)
                return floatValue;
            if (value is decimal decimalValue)
                return (double)decimalValue;
            if (value is int intValue)
                return intValue;
            if (value is long longValue)
                return longValue;

            if (double.TryParse(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double parsed))
                return parsed;

            return null;
        }

        private static bool? TryReadJsonBoolean(IDictionary source, string key)
        {
            object value = TryReadJsonValue(source, key);
            if (value == null)
                return null;

            if (value is bool boolValue)
                return boolValue;

            if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out bool parsed))
                return parsed;

            return null;
        }

        private static object DeserializeJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            Type serializerType = ResolveJsonSerializerType();
            if (serializerType == null)
                return null;

            object serializer;
            try
            {
                serializer = Activator.CreateInstance(serializerType);
            }
            catch
            {
                return null;
            }

            MethodInfo deserializeMethod = serializerType.GetMethod("DeserializeObject", new[] { typeof(string) });
            if (deserializeMethod == null)
                return null;

            try
            {
                return deserializeMethod.Invoke(serializer, new object[] { json });
            }
            catch
            {
                return null;
            }
        }

        private static Type ResolveJsonSerializerType()
        {
            Type serializerType = Type.GetType(
                "System.Web.Script.Serialization.JavaScriptSerializer, System.Web.Extensions, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35",
                false);
            if (serializerType != null)
                return serializerType;

            serializerType = Type.GetType(
                "System.Web.Script.Serialization.JavaScriptSerializer, System.Web.Extensions",
                false);
            if (serializerType != null)
                return serializerType;

            try
            {
                Assembly assembly = Assembly.Load("System.Web.Extensions");
                if (assembly != null)
                    return assembly.GetType("System.Web.Script.Serialization.JavaScriptSerializer", false, false);
            }
            catch
            {
            }

            return null;
        }

        private static FirmRuleMetadata ParseFirmMetadata(string firmJson, string defaultMicroRegex, double defaultMicroMultiplier)
        {
            if (string.IsNullOrWhiteSpace(firmJson))
                return null;

            string firmId = ExtractStringValue(firmJson, "firmId");
            if (string.IsNullOrWhiteSpace(firmId))
                return null;

            string semanticsJson = ExtractObjectJson(firmJson, "enforcementSemantics");
            string status = ExtractStringValue(firmJson, "status");
            if (string.IsNullOrWhiteSpace(status))
                status = "Supported";

            var metadata = new FirmRuleMetadata
            {
                FirmId = firmId,
                UiDisplayName = ExtractStringValue(firmJson, "uiDisplayName"),
                Status = status,
                DrawdownType = ExtractStringValue(semanticsJson, "drawdownType") ?? ExtractStringValue(firmJson, "drawdownType"),
                MaxLossTracking = ExtractStringValue(semanticsJson, "maxLossTracking"),
                MaxLossThresholdUpdate = ExtractStringValue(semanticsJson, "maxLossThresholdUpdate"),
                MaxLossFloorCapMode = ExtractStringValue(semanticsJson, "maxLossFloorCapMode"),
                MaxLossFloorCapOffset = ExtractNumberValueNullable(semanticsJson, "maxLossFloorCapOffset") ?? 0,
                MaxLossFloorCapTrigger = ExtractStringValue(semanticsJson, "maxLossFloorCapTrigger"),
                MaxLossFloorCapStatuses = ExtractStringValue(semanticsJson, "maxLossFloorCapStatuses"),
                HasDailyLossLimit = ExtractBooleanValueNullable(semanticsJson, "hasDailyLossLimit") ?? false,
                AllowSundayTrading = ExtractBooleanValueNullable(semanticsJson, "allowSundayTrading") ?? true,
                MinimumTradingDays = (int)Math.Round(ExtractNumberValueNullable(semanticsJson, "minimumTradingDays") ?? 0, MidpointRounding.AwayFromZero),
                ConsistencyRulePercent = ExtractNumberValueNullable(semanticsJson, "consistencyRulePercent") ?? 0,
                TradingStartTime = ExtractStringValue(semanticsJson, "tradingStartTime"),
                TradingEndTime = ExtractStringValue(semanticsJson, "tradingEndTime"),
                EvalRithmicThresholdStopsAtProfitTarget = ExtractBooleanValueNullable(semanticsJson, "evalRithmicThresholdStopsAtProfitTarget") ?? false,
                EvalTradovateThresholdStopsAtProfitTarget = ExtractBooleanValueNullable(semanticsJson, "evalTradovateThresholdStopsAtProfitTarget") ?? false,
                EvalWealthChartsThresholdStopsAtProfitTarget = ExtractBooleanValueNullable(semanticsJson, "evalWealthChartsThresholdStopsAtProfitTarget") ?? false,
                EvalRithmicThresholdStopOffset = ExtractNumberValueNullable(semanticsJson, "evalRithmicThresholdStopOffset") ?? 0,
                EvalTradovateThresholdStopOffset = ExtractNumberValueNullable(semanticsJson, "evalTradovateThresholdStopOffset") ?? 0,
                EvalWealthChartsThresholdStopOffset = ExtractNumberValueNullable(semanticsJson, "evalWealthChartsThresholdStopOffset") ?? 0,
                EvalProfitTargetLockBuffer = ExtractNumberValueNullable(semanticsJson, "evalProfitTargetLockBuffer") ?? 20.0,
                UseNativeLiquidationThresholdWhenAvailable = ExtractBooleanValueNullable(semanticsJson, "useNativeLiquidationThresholdWhenAvailable") ?? true,
                MicroContractRootRegex = ExtractStringValue(semanticsJson, "microContractRootRegex") ?? defaultMicroRegex,
                MicroContractMultiplier = ExtractNumberValueNullable(semanticsJson, "microContractMultiplier") ?? defaultMicroMultiplier,
                ProviderRules = ParseFirmProviderRules(firmJson),
                Tiers = ParseFirmTiers(firmJson)
            };

            if (string.IsNullOrWhiteSpace(metadata.MicroContractRootRegex))
                metadata.MicroContractRootRegex = DefaultMicroContractRootRegex;
            if (double.IsNaN(metadata.MicroContractMultiplier) ||
                double.IsInfinity(metadata.MicroContractMultiplier) ||
                metadata.MicroContractMultiplier <= 0)
            {
                metadata.MicroContractMultiplier = defaultMicroMultiplier > 0 ? defaultMicroMultiplier : DefaultMicroContractMultiplier;
            }

            return metadata;
        }

        private static List<FirmTierRule> ParseFirmTiers(string firmJson)
        {
            var tiers = new List<FirmTierRule>();
            int tiersKey = firmJson.IndexOf("\"tiers\"", StringComparison.OrdinalIgnoreCase);
            if (tiersKey < 0)
                return tiers;

            int arrayStart = firmJson.IndexOf('[', tiersKey);
            if (arrayStart < 0)
                return tiers;

            int arrayEnd = FindMatchingBracket(firmJson, arrayStart);
            if (arrayEnd <= arrayStart)
                return tiers;

            int pos = arrayStart + 1;
            while (pos < arrayEnd)
            {
                if (firmJson[pos] == '{')
                {
                    int tierEnd = FindMatchingBrace(firmJson, pos);
                    if (tierEnd > pos && tierEnd <= arrayEnd)
                    {
                        string tierJson = firmJson.Substring(pos, tierEnd - pos + 1);
                        double accountSize = ExtractNumberValue(tierJson, "accountSize");
                        if (accountSize > 0)
                        {
                            tiers.Add(new FirmTierRule
                            {
                                AccountSize = accountSize,
                                MaxContracts = ExtractNumberValueNullable(tierJson, "maxContracts") ?? 0,
                                MaxMicros = ExtractNumberValueNullable(tierJson, "maxMicros") ?? 0,
                                MaxDrawdown = ExtractNumberValue(tierJson, "maxDrawdown"),
                                IntratradeDrawdown = ExtractNumberValueNullable(tierJson, "intratradeDrawdown") ?? 0,
                                ProfitTarget = ExtractNumberValueNullable(tierJson, "profitTarget") ?? 0,
                                DailyLossLimit = ExtractNumberValueNullable(tierJson, "dailyLossLimit") ?? 0,
                                StatusFilter = ExtractStringValue(tierJson, "statusFilter") ?? ExtractStringValue(tierJson, "statuses"),
                                ProviderFilter = ExtractStringValue(tierJson, "providerFilter") ?? ExtractStringValue(tierJson, "providerHints"),
                                MinProfit = ExtractNumberValueNullable(tierJson, "minProfit") ?? 0,
                                MaxProfit = ExtractNumberValueNullable(tierJson, "maxProfit") ?? 0
                            });
                        }

                        pos = tierEnd + 1;
                        continue;
                    }
                }

                pos++;
            }

            return tiers
                .OrderBy(t => t.AccountSize)
                .ThenBy(t => t.MinProfit)
                .ToList();
        }

        private static List<FirmProviderRule> ParseFirmProviderRules(string firmJson)
        {
            var providerRules = new List<FirmProviderRule>();
            int providerRulesKey = firmJson.IndexOf("\"providerRules\"", StringComparison.OrdinalIgnoreCase);
            if (providerRulesKey < 0)
                return providerRules;

            int arrayStart = firmJson.IndexOf('[', providerRulesKey);
            if (arrayStart < 0)
                return providerRules;

            int arrayEnd = FindMatchingBracket(firmJson, arrayStart);
            if (arrayEnd <= arrayStart)
                return providerRules;

            int pos = arrayStart + 1;
            while (pos < arrayEnd)
            {
                if (firmJson[pos] == '{')
                {
                    int ruleEnd = FindMatchingBrace(firmJson, pos);
                    if (ruleEnd > pos && ruleEnd <= arrayEnd)
                    {
                        string ruleJson = firmJson.Substring(pos, ruleEnd - pos + 1);
                        providerRules.Add(new FirmProviderRule
                        {
                            ProviderFilter = ExtractStringValue(ruleJson, "providerFilter") ?? ExtractStringValue(ruleJson, "providerHints"),
                            EvalThresholdStopsAtProfitTarget = ExtractBooleanValueNullable(ruleJson, "evalThresholdStopsAtProfitTarget"),
                            EvalThresholdStopOffset = ExtractNumberValueNullable(ruleJson, "evalThresholdStopOffset") ?? 0
                        });

                        pos = ruleEnd + 1;
                        continue;
                    }
                }

                pos++;
            }

            return providerRules;
        }

        private static int FindMatchingBrace(string text, int openIndex)
        {
            int depth = 1;
            bool inString = false;
            bool escaped = false;

            for (int i = openIndex + 1; i < text.Length; i++)
            {
                char c = text[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                    continue;

                if (c == '{')
                    depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        private static int FindMatchingBracket(string text, int openIndex)
        {
            int depth = 1;
            bool inString = false;
            bool escaped = false;

            for (int i = openIndex + 1; i < text.Length; i++)
            {
                char c = text[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                    continue;

                if (c == '[')
                    depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        private static string ExtractStringValue(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return null;

            int keyPos = json.IndexOf($"\"{key}\"", StringComparison.OrdinalIgnoreCase);
            if (keyPos < 0)
                return null;

            int colonPos = json.IndexOf(':', keyPos);
            if (colonPos < 0)
                return null;

            int quoteStart = json.IndexOf('"', colonPos + 1);
            if (quoteStart < 0)
                return null;

            int quoteEnd = quoteStart + 1;
            bool escaped = false;
            while (quoteEnd < json.Length)
            {
                char c = json[quoteEnd];
                if (escaped)
                {
                    escaped = false;
                    quoteEnd++;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    quoteEnd++;
                    continue;
                }

                if (c == '"')
                    break;

                quoteEnd++;
            }

            if (quoteEnd >= json.Length)
                return null;

            return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        private static string ExtractObjectJson(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return null;

            int keyPos = json.IndexOf($"\"{key}\"", StringComparison.OrdinalIgnoreCase);
            if (keyPos < 0)
                return null;

            int colonPos = json.IndexOf(':', keyPos);
            if (colonPos < 0)
                return null;

            int objectStart = colonPos + 1;
            while (objectStart < json.Length && char.IsWhiteSpace(json[objectStart]))
                objectStart++;

            if (objectStart >= json.Length || json[objectStart] != '{')
                return null;

            int objectEnd = FindMatchingBrace(json, objectStart);
            if (objectEnd <= objectStart)
                return null;

            return json.Substring(objectStart, objectEnd - objectStart + 1);
        }

        private static double ExtractNumberValue(string json, string key)
        {
            return ExtractNumberValueNullable(json, key) ?? 0;
        }

        private static double? ExtractNumberValueNullable(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return null;

            int keyPos = json.IndexOf($"\"{key}\"", StringComparison.OrdinalIgnoreCase);
            if (keyPos < 0)
                return null;

            int colonPos = json.IndexOf(':', keyPos);
            if (colonPos < 0)
                return null;

            int start = colonPos + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start]))
                start++;

            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-' || json[end] == '+'))
                end++;

            if (end <= start)
                return null;

            string number = json.Substring(start, end - start);
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return value;

            return null;
        }

        private static bool? ExtractBooleanValueNullable(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return null;

            int keyPos = json.IndexOf($"\"{key}\"", StringComparison.OrdinalIgnoreCase);
            if (keyPos < 0)
                return null;

            int colonPos = json.IndexOf(':', keyPos);
            if (colonPos < 0)
                return null;

            int start = colonPos + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start]))
                start++;

            if (start >= json.Length)
                return null;

            string remaining = json.Substring(start);
            if (remaining.StartsWith("true", StringComparison.OrdinalIgnoreCase))
                return true;

            if (remaining.StartsWith("false", StringComparison.OrdinalIgnoreCase))
                return false;

            return null;
        }

        private static Dictionary<string, FirmRuleMetadata> BuildFallbackFirmRules()
        {
            var rules = new Dictionary<string, FirmRuleMetadata>(StringComparer.OrdinalIgnoreCase)
            {
                ["ApexTraderFunding"] = new FirmRuleMetadata
                {
                    FirmId = "ApexTraderFunding",
                    UiDisplayName = "Apex",
                    Status = "Supported",
                    DrawdownType = "Trailing",
                    MaxLossTracking = "TrailingUnrealized",
                    MaxLossThresholdUpdate = "Intraday",
                    MaxLossFloorCapMode = "AtInitialPlusOffset",
                    MaxLossFloorCapOffset = 100,
                    MaxLossFloorCapTrigger = "WhenReferenceReachesInitialPlusDrawdownPlusOffset",
                    MaxLossFloorCapStatuses = "AP",
                    EvalRithmicThresholdStopsAtProfitTarget = true,
                    EvalTradovateThresholdStopsAtProfitTarget = false,
                    UseNativeLiquidationThresholdWhenAvailable = true,
                    Tiers = new List<FirmTierRule>
                    {
                        new FirmTierRule { AccountSize = 25000, MaxContracts = 4, MaxDrawdown = 1500, IntratradeDrawdown = 0, ProfitTarget = 1500 },
                        new FirmTierRule { AccountSize = 50000, MaxContracts = 10, MaxDrawdown = 2500, IntratradeDrawdown = 0, ProfitTarget = 3000 },
                        new FirmTierRule { AccountSize = 75000, MaxContracts = 12, MaxDrawdown = 2750, IntratradeDrawdown = 0, ProfitTarget = 4500 },
                        new FirmTierRule { AccountSize = 100000, MaxContracts = 14, MaxDrawdown = 3000, IntratradeDrawdown = 0, ProfitTarget = 6000 },
                        new FirmTierRule { AccountSize = 150000, MaxContracts = 17, MaxDrawdown = 5000, IntratradeDrawdown = 0, ProfitTarget = 9000 }
                    }
                },
                ["TakeProfitTrader"] = new FirmRuleMetadata
                {
                    FirmId = "TakeProfitTrader",
                    UiDisplayName = "TPT",
                    Status = "Supported",
                    DrawdownType = "EOD",
                    MaxLossTracking = "TrailingEod",
                    MaxLossThresholdUpdate = "EndOfDay",
                    MaxLossFloorCapMode = "AtInitialBalance",
                    MaxLossFloorCapOffset = 0,
                    MaxLossFloorCapTrigger = "WhenThresholdReachesCap",
                    MaxLossFloorCapStatuses = "Eval,AP",
                    EvalRithmicThresholdStopsAtProfitTarget = false,
                    EvalTradovateThresholdStopsAtProfitTarget = false,
                    UseNativeLiquidationThresholdWhenAvailable = true,
                    Tiers = new List<FirmTierRule>
                    {
                        new FirmTierRule { AccountSize = 25000, MaxContracts = 3, MaxMicros = 30, MaxDrawdown = 1500, IntratradeDrawdown = 0, ProfitTarget = 1500 },
                        new FirmTierRule { AccountSize = 50000, MaxContracts = 6, MaxMicros = 60, MaxDrawdown = 2000, IntratradeDrawdown = 0, ProfitTarget = 3000 },
                        new FirmTierRule { AccountSize = 75000, MaxContracts = 9, MaxMicros = 90, MaxDrawdown = 2500, IntratradeDrawdown = 0, ProfitTarget = 4500 },
                        new FirmTierRule { AccountSize = 100000, MaxContracts = 12, MaxMicros = 120, MaxDrawdown = 3000, IntratradeDrawdown = 0, ProfitTarget = 6000 },
                        new FirmTierRule { AccountSize = 150000, MaxContracts = 15, MaxMicros = 150, MaxDrawdown = 4500, IntratradeDrawdown = 0, ProfitTarget = 9000 }
                    }
                },
                ["TradeDay"] = new FirmRuleMetadata
                {
                    FirmId = "TradeDay",
                    UiDisplayName = "TradeDay",
                    Status = "Supported",
                    DrawdownType = "EOD",
                    MaxLossTracking = "TrailingEod",
                    MaxLossThresholdUpdate = "EndOfDay",
                    MaxLossFloorCapMode = "AtInitialBalance",
                    MaxLossFloorCapOffset = 0,
                    MaxLossFloorCapTrigger = "WhenThresholdReachesCap",
                    MaxLossFloorCapStatuses = "Eval,AP",
                    EvalRithmicThresholdStopsAtProfitTarget = false,
                    EvalTradovateThresholdStopsAtProfitTarget = false,
                    UseNativeLiquidationThresholdWhenAvailable = true,
                    Tiers = new List<FirmTierRule>
                    {
                        new FirmTierRule { AccountSize = 50000, MaxContracts = 5, MaxDrawdown = 2000, IntratradeDrawdown = 0, ProfitTarget = 3000 },
                        new FirmTierRule { AccountSize = 100000, MaxContracts = 10, MaxDrawdown = 3000, IntratradeDrawdown = 0, ProfitTarget = 6000 },
                        new FirmTierRule { AccountSize = 150000, MaxContracts = 15, MaxDrawdown = 4000, IntratradeDrawdown = 0, ProfitTarget = 9000 }
                    }
                }
            };

            return rules;
        }

        private static Dictionary<string, string> BuildFirmIdToDisplayMap(Dictionary<string, FirmRuleMetadata> firmRules)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["None"] = "None"
            };

            if (firmRules == null)
                return map;

            foreach (var kvp in firmRules)
            {
                string firmId = kvp.Key;
                FirmRuleMetadata metadata = kvp.Value;
                if (string.IsNullOrWhiteSpace(firmId))
                    continue;

                string display = metadata?.UiDisplayName;
                if (string.IsNullOrWhiteSpace(display))
                    display = firmId;

                if (string.Equals(firmId, "ApexTraderFunding", StringComparison.OrdinalIgnoreCase))
                    display = "Apex";
                else if (string.Equals(firmId, "TakeProfitTrader", StringComparison.OrdinalIgnoreCase))
                    display = "TakeProfit";

                map[firmId] = display;
            }

            return map;
        }

        private static Dictionary<string, string> BuildFirmDisplayToIdMap(Dictionary<string, string> firmIdToDisplay)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (firmIdToDisplay == null)
                return map;

            foreach (var kvp in firmIdToDisplay)
            {
                if (string.IsNullOrWhiteSpace(kvp.Value))
                    continue;

                if (!map.ContainsKey(kvp.Value))
                    map[kvp.Value] = kvp.Key;
            }

            return map;
        }

        private static List<string> BuildPropFirmDisplayOptions(Dictionary<string, FirmRuleMetadata> firmRules, Dictionary<string, string> firmIdToDisplay)
        {
            var options = new List<string> { "None" };
            if (firmRules == null || firmIdToDisplay == null)
                return options;

            foreach (var kvp in firmRules)
            {
                if (kvp.Value == null)
                    continue;
                if (!IsFirmStatusSupported(kvp.Value.Status))
                    continue;

                if (!firmIdToDisplay.TryGetValue(kvp.Key, out string display))
                    continue;
                if (string.IsNullOrWhiteSpace(display))
                    continue;
                if (string.Equals(display, "None", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!options.Contains(display, StringComparer.OrdinalIgnoreCase))
                    options.Add(display);
            }

            return options
                .OrderBy(x => x.Equals("None", StringComparison.OrdinalIgnoreCase) ? string.Empty : x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsFirmStatusSupported(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return false;

            string normalized = status.Trim();
            return normalized.StartsWith("Supported", StringComparison.OrdinalIgnoreCase);
        }

        private static List<double> BuildGlobalAccountSizeOptions(Dictionary<string, FirmRuleMetadata> firmRules)
        {
            var sizes = new SortedSet<double>();
            if (firmRules != null)
            {
                foreach (var metadata in firmRules.Values)
                {
                    if (metadata?.Tiers == null)
                        continue;

                    foreach (var tier in metadata.Tiers)
                    {
                        if (tier.AccountSize > 0)
                            sizes.Add(tier.AccountSize);
                    }
                }
            }

            if (sizes.Count == 0)
            {
                sizes.Add(25000);
                sizes.Add(50000);
                sizes.Add(75000);
                sizes.Add(100000);
                sizes.Add(150000);
            }

            return sizes.ToList();
        }

    }
}
