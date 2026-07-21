using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Glitch.Services
{
    internal sealed class GlitchAiRailPolicy
    {
        public bool IsValid { get; set; }
        public bool AiEnabled { get; set; } = true;
        public bool AiKillSwitch { get; set; } = false;
        public bool RequireValidLicense { get; set; } = false;
        public string Mode { get; set; } = "paper";
        public int SnapshotMaxAgeSeconds { get; set; } = 180;
        public int MaxContracts { get; set; } = 5;
        public double MaxRiskPerContractUsd { get; set; } = 80;
        public double MaxLossPerTradeUsd { get; set; } = 400;
        public double MaxGroupLossPerTradeUsd { get; set; } = 0;
        public double MaxDailyLossUsd { get; set; } = 0;
        public bool NewsLockout { get; set; } = false;
        public string ExecutorAccount { get; set; } = "Sim101";
        public Dictionary<string, string> ProfileAccountBindings { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "glitch", "Sim101" }
            };
        public HashSet<string> InstrumentAllowlist { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MNQ", "MES", "M2K" };
        public HashSet<string> AccountAllowlist { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sim101", "Sim102", "Sim103" };
        public HashSet<string> BlockedSessions { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool TryResolveProfileAccount(string profile, out string account)
        {
            account = null;
            if (string.IsNullOrWhiteSpace(profile) || ProfileAccountBindings == null)
                return false;

            return ProfileAccountBindings.TryGetValue(profile.Trim(), out account)
                && !string.IsNullOrWhiteSpace(account);
        }
    }

    internal static class GlitchAiRailPolicyStore
    {
        public static string GetPolicyPath()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("ai", "policy.json"));
        }

        public static GlitchAiRailPolicy Load()
        {
            try
            {
                EnsureDefaultExists();
                string json = File.ReadAllText(GetPolicyPath());
                return Parse(json);
            }
            catch
            {
                return InvalidPolicy();
            }
        }

        public static void EnsureDefaultExists()
        {
            string path = GetPolicyPath();
            if (File.Exists(path))
            {
                TryUpgradeLegacyPolicy(path);
                return;
            }

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, BuildDefaultTemplate(), new UTF8Encoding(false));
        }

        public static bool TrySaveTradingScope(
            IReadOnlyList<string> enabledMasterAccounts,
            IReadOnlyCollection<string> allowedAccounts,
            out string error)
        {
            error = string.Empty;
            try
            {
                EnsureDefaultExists();
                string path = GetPolicyPath();
                string json = File.ReadAllText(path);
                var masters = (enabledMasterAccounts ?? new string[0])
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var accounts = (allowedAccounts ?? new string[0])
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var bindings = new List<string>();
                for (int i = 0; i < masters.Count; i++)
                    bindings.Add((i == 0 ? "glitch" : "glitch-" + (i + 1).ToString(CultureInfo.InvariantCulture)) + "=" + masters[i]);

                json = ReplaceStringArray(json, "profile_account_bindings", bindings);
                json = ReplaceStringArray(json, "account_allowlist", accounts);
                json = ReplaceStringValue(json, "executor_account", masters.Count == 0 ? string.Empty : masters[0]);

                string temporaryPath = path + ".tmp";
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string ReplaceStringArray(string json, string key, IEnumerable<string> values)
        {
            string replacement = "\"" + key + "\":[" + string.Join(",", values.Select(Quote)) + "]";
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[[^\\]]*\\]";
            return Regex.IsMatch(json, pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.IgnoreCase)
                ? Regex.Replace(json, pattern, replacement, RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.IgnoreCase)
                : json.TrimEnd().TrimEnd('}') + "," + replacement + "}";
        }

        private static string ReplaceStringValue(string json, string key, string value)
        {
            string replacement = "\"" + key + "\":" + Quote(value);
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"[^\"]*\"";
            return Regex.IsMatch(json, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
                ? Regex.Replace(json, pattern, replacement, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
                : json.TrimEnd().TrimEnd('}') + "," + replacement + "}";
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static void TryUpgradeLegacyPolicy(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return;

                bool changed = false;
                string withoutRetiredExecutorGate = Regex.Replace(
                    json,
                    "\"executor_enabled\"\\s*:\\s*(?:true|false)\\s*,?",
                    string.Empty,
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                if (!string.Equals(withoutRetiredExecutorGate, json, StringComparison.Ordinal))
                {
                    json = withoutRetiredExecutorGate;
                    changed = true;
                }
                if (json.IndexOf("executor_account", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    json = InsertAfterBool(json, "news_lockout", ",\"executor_account\":\"Sim101\"");
                    changed = true;
                }

                if (json.IndexOf("profile_account_bindings", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    json = InsertAfterString(
                        json,
                        "executor_account",
                        ",\"profile_account_bindings\":[\"glitch=Sim101\"]");
                    changed = true;
                }

                if (json.IndexOf("max_group_loss_per_trade_usd", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    json = InsertAfterNumber(json, "max_loss_per_trade_usd", ",\"max_group_loss_per_trade_usd\":300");
                    changed = true;
                }
                if (json.IndexOf("max_risk_per_contract_usd", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    json = InsertAfterNumber(json, "max_contracts", ",\"max_risk_per_contract_usd\":80");
                    changed = true;
                }

                string[] legacyReplacements =
                {
                    "\"max_contracts\"\\s*:\\s*1(?=\\s*[,}])|\"max_contracts\":5",
                    "\"max_loss_per_trade_usd\"\\s*:\\s*100(?=\\s*[,}])|\"max_loss_per_trade_usd\":400",
                    "\"max_group_loss_per_trade_usd\"\\s*:\\s*300(?=\\s*[,}])|\"max_group_loss_per_trade_usd\":0",
                    "\"max_daily_loss_usd\"\\s*:\\s*300(?=\\s*[,}])|\"max_daily_loss_usd\":0"
                };
                foreach (string replacement in legacyReplacements)
                {
                    string[] parts = replacement.Split('|');
                    string upgraded = Regex.Replace(
                        json,
                        parts[0],
                        parts[1],
                        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                    if (!string.Equals(upgraded, json, StringComparison.Ordinal))
                    {
                        json = upgraded;
                        changed = true;
                    }
                }

                // 120 seconds was the original default, but the native five-minute
                // packet -> cron -> model path routinely needs about 130 seconds.
                // Migrate only that legacy default; preserve explicit custom values.
                string upgradedSnapshotAge = Regex.Replace(
                    json,
                    "\"snapshot_max_age_seconds\"\\s*:\\s*120(?=\\s*[,}])",
                    "\"snapshot_max_age_seconds\":180",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                if (!string.Equals(upgradedSnapshotAge, json, StringComparison.Ordinal))
                {
                    json = upgradedSnapshotAge;
                    changed = true;
                }

                if (changed)
                    File.WriteAllText(path, json, new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private static string InsertAfterBool(string json, string key, string insert)
        {
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*(true|false)";
            Match match = Regex.Match(json, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (!match.Success)
                return json;

            int insertAt = match.Index + match.Length;
            return json.Substring(0, insertAt) + insert + json.Substring(insertAt);
        }

        private static string InsertAfterNumber(string json, string key, string insert)
        {
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*-?[0-9]+(?:\\.[0-9]+)?";
            Match match = Regex.Match(json, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (!match.Success)
                return json;

            int insertAt = match.Index + match.Length;
            return json.Substring(0, insertAt) + insert + json.Substring(insertAt);
        }

        private static string InsertAfterString(string json, string key, string insert)
        {
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"[^\"]*\"";
            Match match = Regex.Match(json, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (!match.Success)
                return json;

            int insertAt = match.Index + match.Length;
            return json.Substring(0, insertAt) + insert + json.Substring(insertAt);
        }

        private static string BuildDefaultTemplate()
        {
            return "{"
                + "\"schema_version\":\"glitch.ai.policy.v1\","
                + "\"ai_enabled\":true,"
                + "\"ai_kill_switch\":false,"
                + "\"require_valid_license\":false,"
                + "\"mode\":\"paper\","
                + "\"snapshot_max_age_seconds\":180,"
                + "\"max_contracts\":5,"
                + "\"max_risk_per_contract_usd\":80,"
                + "\"max_loss_per_trade_usd\":400,"
                + "\"max_group_loss_per_trade_usd\":0,"
                + "\"max_daily_loss_usd\":0,"
                + "\"news_lockout\":false,"
                + "\"executor_account\":\"Sim101\","
                + "\"profile_account_bindings\":[\"glitch=Sim101\"],"
                + "\"instrument_allowlist\":[\"MNQ\",\"MES\",\"M2K\"],"
                + "\"account_allowlist\":[\"Sim101\",\"Sim102\",\"Sim103\"],"
                + "\"blocked_sessions\":[]"
                + "}";
        }

        private static GlitchAiRailPolicy Parse(string json)
        {
            var policy = new GlitchAiRailPolicy();
            if (string.IsNullOrWhiteSpace(json)
                || !string.Equals(
                    GlitchAiJsonFields.ExtractString(json, "schema_version"),
                    "glitch.ai.policy.v1",
                    StringComparison.Ordinal))
                return InvalidPolicy();

            policy.IsValid = true;

            if (GlitchAiJsonFields.TryExtractBool(json, "ai_enabled", out bool aiEnabled))
                policy.AiEnabled = aiEnabled;
            if (GlitchAiJsonFields.TryExtractBool(json, "ai_kill_switch", out bool killSwitch))
                policy.AiKillSwitch = killSwitch;
            if (GlitchAiJsonFields.TryExtractBool(json, "require_valid_license", out bool requireLicense))
                policy.RequireValidLicense = requireLicense;
            if (GlitchAiJsonFields.TryExtractBool(json, "news_lockout", out bool newsLockout))
                policy.NewsLockout = newsLockout;
            string executorAccount = GlitchAiJsonFields.ExtractString(json, "executor_account");
            if (!string.IsNullOrWhiteSpace(executorAccount))
                policy.ExecutorAccount = executorAccount.Trim();

            string mode = GlitchAiJsonFields.ExtractString(json, "mode");
            if (!string.IsNullOrWhiteSpace(mode))
                policy.Mode = mode.Trim();

            if (GlitchAiJsonFields.TryExtractNumber(json, "snapshot_max_age_seconds", out double snapshotAge))
                policy.SnapshotMaxAgeSeconds = Math.Max(1, (int)snapshotAge);
            if (GlitchAiJsonFields.TryExtractNumber(json, "max_contracts", out double maxContracts))
                policy.MaxContracts = Math.Max(1, (int)maxContracts);
            if (GlitchAiJsonFields.TryExtractNumber(json, "max_risk_per_contract_usd", out double maxRiskPerContract))
                policy.MaxRiskPerContractUsd = maxRiskPerContract;
            if (GlitchAiJsonFields.TryExtractNumber(json, "max_loss_per_trade_usd", out double maxLossTrade))
                policy.MaxLossPerTradeUsd = maxLossTrade;
            if (GlitchAiJsonFields.TryExtractNumber(json, "max_group_loss_per_trade_usd", out double maxGroupLossTrade))
                policy.MaxGroupLossPerTradeUsd = maxGroupLossTrade;
            if (GlitchAiJsonFields.TryExtractNumber(json, "max_daily_loss_usd", out double maxDailyLoss))
                policy.MaxDailyLossUsd = maxDailyLoss;
            policy.InstrumentAllowlist = ParseStringArray(json, "instrument_allowlist", policy.InstrumentAllowlist);
            policy.AccountAllowlist = ParseStringArray(json, "account_allowlist", policy.AccountAllowlist);
            policy.ProfileAccountBindings = ParseProfileAccountBindings(json, policy.ProfileAccountBindings);
            policy.AccountAllowlist.UnionWith(policy.ProfileAccountBindings.Values);
            policy.BlockedSessions = ParseStringArray(json, "blocked_sessions", policy.BlockedSessions);
            return policy;
        }

        private static GlitchAiRailPolicy InvalidPolicy()
        {
            var policy = new GlitchAiRailPolicy
            {
                IsValid = false,
                AiEnabled = false,
                AiKillSwitch = true
            };
            policy.ProfileAccountBindings.Clear();
            policy.AccountAllowlist.Clear();
            return policy;
        }

        private static Dictionary<string, string> ParseProfileAccountBindings(
            string json,
            Dictionary<string, string> fallback)
        {
            string pattern = "\"profile_account_bindings\"\\s*:\\s*\\[([^\\]]*)\\]";
            Match match = Regex.Match(json, pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline);
            if (!match.Success)
                return fallback;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            MatchCollection itemMatches = Regex.Matches(match.Groups[1].Value, "\"([^\"]+)\"", RegexOptions.CultureInvariant);
            for (int i = 0; i < itemMatches.Count; i++)
            {
                string value = itemMatches[i].Groups[1].Value.Trim();
                int separator = value.IndexOf('=');
                if (separator <= 0 || separator >= value.Length - 1)
                    continue;

                string profile = value.Substring(0, separator).Trim();
                string account = value.Substring(separator + 1).Trim();
                if (!string.IsNullOrWhiteSpace(profile) && !string.IsNullOrWhiteSpace(account))
                    values[profile] = account;
            }

            return values;
        }

        private static HashSet<string> ParseStringArray(string json, string key, HashSet<string> fallback)
        {
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[([^\\]]*)\\]";
            Match match = Regex.Match(json, pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline);
            if (!match.Success)
                return fallback;

            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MatchCollection itemMatches = Regex.Matches(match.Groups[1].Value, "\"([^\"]+)\"", RegexOptions.CultureInvariant);
            for (int i = 0; i < itemMatches.Count; i++)
            {
                Match item = itemMatches[i];
                string value = item.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value.ToUpperInvariant());
            }

            return values;
        }
    }
}
