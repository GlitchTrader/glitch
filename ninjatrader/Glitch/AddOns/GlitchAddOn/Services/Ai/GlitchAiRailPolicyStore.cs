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
        public string ValidationError { get; set; }
        public bool RequireValidLicense { get; set; } = false;
        public int SnapshotMaxAgeSeconds { get; set; } = 300;
        public string ExecutorAccount { get; set; } = string.Empty;
        public Dictionary<string, string> ProfileAccountBindings { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> InstrumentAllowlist { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MNQ", "MES", "M2K" };
        public HashSet<string> AccountAllowlist { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            catch (Exception ex)
            {
                return Invalid("policy_load_failed_" + ex.GetType().Name);
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
                GlitchAiRailPolicy policy = Load();
                if (!policy.IsValid)
                {
                    error = policy.ValidationError ?? "policy_invalid";
                    return false;
                }
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

                WriteAtomically(path, json);
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
                foreach (string retiredBoolKey in new[] { "ai_enabled", "ai_kill_switch" })
                {
                    string upgraded = RemoveJsonScalarProperty(json, retiredBoolKey, "(?:true|false)");
                    if (!string.Equals(upgraded, json, StringComparison.Ordinal))
                    {
                        json = upgraded;
                        changed = true;
                    }
                }
                string withoutRetiredExecutorGate = RemoveJsonScalarProperty(json, "executor_enabled", "(?:true|false)");
                if (!string.Equals(withoutRetiredExecutorGate, json, StringComparison.Ordinal))
                {
                    json = withoutRetiredExecutorGate;
                    changed = true;
                }
                string withoutMode = RemoveJsonScalarProperty(json, "mode", "\"[^\"]*\"");
                if (!string.Equals(withoutMode, json, StringComparison.Ordinal))
                {
                    json = withoutMode;
                    changed = true;
                }

                string v2Schema = Regex.Replace(
                    json,
                    "\"schema_version\"\\s*:\\s*\"glitch\\.ai\\.policy\\.v1\"",
                    "\"schema_version\":\"glitch.ai.policy.v2\"",
                    RegexOptions.CultureInvariant);
                if (!string.Equals(v2Schema, json, StringComparison.Ordinal))
                {
                    json = v2Schema;
                    changed = true;
                }

                if (json.IndexOf("executor_account", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    json = InsertBeforeClosingBrace(json, "\"executor_account\":\"\"");
                    changed = true;
                }

                if (json.IndexOf("profile_account_bindings", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    json = InsertAfterString(
                        json,
                        "executor_account",
                        ",\"profile_account_bindings\":[]");
                    changed = true;
                }

                string[] retiredRiskKeys =
                {
                    "max_contracts",
                    "max_risk_per_contract_usd",
                    "max_loss_per_trade_usd",
                    "max_group_loss_per_trade_usd",
                    "max_daily_loss_usd",
                    "max_trades_per_day",
                    "cooldown_after_loss_minutes",
                    "paper_daily_profit_objective_usd"
                };
                foreach (string key in retiredRiskKeys)
                {
                    string upgraded = RemoveJsonScalarProperty(json, key, "-?[0-9]+(?:\\.[0-9]+)?");
                    if (!string.Equals(upgraded, json, StringComparison.Ordinal))
                    {
                        json = upgraded;
                        changed = true;
                    }
                }

                // Retired defaults expired a valid five-minute decision before
                // its own window ended. Align legacy values to the canonical
                // five-minute packet horizon; live price and crossed-structure
                // checks still run immediately before entry.
                string upgradedSnapshotAge = Regex.Replace(
                    json,
                    "\"snapshot_max_age_seconds\"\\s*:\\s*(?:120|180)(?=\\s*[,}])",
                    "\"snapshot_max_age_seconds\":300",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                if (!string.Equals(upgradedSnapshotAge, json, StringComparison.Ordinal))
                {
                    json = upgradedSnapshotAge;
                    changed = true;
                }

                if (changed && GlitchAiJsonFields.TryParseObject(json, out _))
                    WriteAtomically(path, json);
            }
            catch
            {
            }
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

        private static string RemoveJsonScalarProperty(string json, string key, string valuePattern)
        {
            string propertyPattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*" + valuePattern;
            string withoutLastProperty = Regex.Replace(
                json,
                ",\\s*" + propertyPattern + "(?=\\s*})",
                string.Empty,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            return Regex.Replace(
                withoutLastProperty,
                propertyPattern + "\\s*,?",
                string.Empty,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }

        private static string InsertBeforeClosingBrace(string json, string property)
        {
            string trimmed = (json ?? string.Empty).TrimEnd();
            if (!trimmed.EndsWith("}", StringComparison.Ordinal))
                return json;

            string prefix = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
            string separator = prefix.EndsWith("{", StringComparison.Ordinal) ? string.Empty : ",";
            return prefix + separator + property + "}";
        }

        private static void WriteAtomically(string path, string json)
        {
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static string BuildDefaultTemplate()
        {
            return "{"
                + "\"schema_version\":\"glitch.ai.policy.v2\","
                + "\"require_valid_license\":false,"
                + "\"snapshot_max_age_seconds\":300,"
                + "\"executor_account\":\"\","
                + "\"profile_account_bindings\":[],"
                + "\"instrument_allowlist\":[\"MNQ\",\"MES\",\"M2K\"],"
                + "\"account_allowlist\":[],"
                + "\"blocked_sessions\":[]"
                + "}";
        }

        private static GlitchAiRailPolicy Parse(string json)
        {
            var policy = new GlitchAiRailPolicy();
            if (string.IsNullOrWhiteSpace(json))
                return Invalid("policy_empty");
            if (!GlitchAiJsonFields.TryParseObject(json, out _))
                return Invalid("policy_json_invalid");

            string schemaVersion = GlitchAiJsonFields.ExtractString(json, "schema_version");
            if (!string.Equals(schemaVersion, "glitch.ai.policy.v2", StringComparison.Ordinal))
                return Invalid("policy_schema_invalid");
            if (CountJsonKey(json, "schema_version") != 1)
                return Invalid("policy_schema_duplicated");

            string[] requiredKeys =
            {
                "snapshot_max_age_seconds",
                "executor_account",
                "profile_account_bindings",
                "instrument_allowlist",
                "account_allowlist",
                "blocked_sessions"
            };
            foreach (string key in requiredKeys)
            {
                int count = CountJsonKey(json, key);
                if (count == 0)
                    return Invalid("policy_key_missing_" + key);
                if (count != 1)
                    return Invalid("policy_key_duplicated_" + key);
            }
            foreach (string key in new[]
            {
                "profile_account_bindings",
                "instrument_allowlist",
                "account_allowlist",
                "blocked_sessions"
            })
            {
                if (!Regex.IsMatch(
                    json,
                    "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
                    return Invalid("policy_array_invalid_" + key);
            }

            if (GlitchAiJsonFields.TryExtractBool(json, "require_valid_license", out bool requireLicense))
                policy.RequireValidLicense = requireLicense;
            string executorAccount = GlitchAiJsonFields.ExtractString(json, "executor_account");
            if (!string.IsNullOrWhiteSpace(executorAccount))
                policy.ExecutorAccount = executorAccount.Trim();

            if (CountJsonKey(json, "mode") != 0)
                return Invalid("policy_key_retired_mode");

            if (!GlitchAiJsonFields.TryExtractNumber(json, "snapshot_max_age_seconds", out double snapshotAge)
                || snapshotAge < 1 || snapshotAge > 900
                || Math.Abs(snapshotAge - Math.Round(snapshotAge)) > 0.0000001d)
                return Invalid("policy_snapshot_age_invalid");
            policy.SnapshotMaxAgeSeconds = (int)snapshotAge;
            policy.InstrumentAllowlist = ParseStringArray(json, "instrument_allowlist", policy.InstrumentAllowlist);
            policy.AccountAllowlist = ParseStringArray(json, "account_allowlist", policy.AccountAllowlist);
            policy.ProfileAccountBindings = ParseProfileAccountBindings(json, policy.ProfileAccountBindings);
            policy.AccountAllowlist.UnionWith(policy.ProfileAccountBindings.Values);
            policy.BlockedSessions = ParseStringArray(json, "blocked_sessions", policy.BlockedSessions);
            if (policy.InstrumentAllowlist.Count == 0)
                return Invalid("policy_instrument_allowlist_empty");
            policy.IsValid = true;
            policy.ValidationError = null;
            return policy;
        }

        private static int CountJsonKey(string json, string key)
        {
            return Regex.Matches(
                json ?? string.Empty,
                "\"" + Regex.Escape(key) + "\"\\s*:",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).Count;
        }

        private static GlitchAiRailPolicy Invalid(string error)
        {
            return new GlitchAiRailPolicy
            {
                IsValid = false,
                ValidationError = string.IsNullOrWhiteSpace(error) ? "policy_invalid" : error
            };
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
