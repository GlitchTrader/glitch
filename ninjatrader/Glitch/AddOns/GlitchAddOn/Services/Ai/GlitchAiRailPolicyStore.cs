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
        public bool AiEnabled { get; set; } = true;
        public bool AiKillSwitch { get; set; } = false;
        public bool RequireValidLicense { get; set; } = false;
        public string Mode { get; set; } = "paper";
        public int SnapshotMaxAgeSeconds { get; set; } = 90;
        public int MaxContracts { get; set; } = 1;
        public double MaxLossPerTradeUsd { get; set; } = 100;
        public double MaxDailyLossUsd { get; set; } = 300;
        public int MaxTradesPerDay { get; set; } = 5;
        public int CooldownAfterLossMinutes { get; set; } = 10;
        public bool NewsLockout { get; set; } = false;
        public bool ExecutorEnabled { get; set; } = false;
        public string ExecutorAccount { get; set; } = "Sim101";
        public HashSet<string> InstrumentAllowlist { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MNQ", "MES", "M2K" };
        public HashSet<string> AccountAllowlist { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sim101", "Sim102", "Sim103" };
        public HashSet<string> BlockedSessions { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    internal static class GlitchAiRailPolicyStore
    {
        public static string GetPolicyPath()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("ai", "policy.json"));
        }

        public static GlitchAiRailPolicy Load()
        {
            EnsureDefaultExists();
            try
            {
                string json = File.ReadAllText(GetPolicyPath());
                return Parse(json);
            }
            catch
            {
                return new GlitchAiRailPolicy();
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

        private static void TryUpgradeLegacyPolicy(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return;

                bool changed = false;
                if (json.IndexOf("executor_enabled", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    json = InsertAfterBool(json, "news_lockout", ",\"executor_enabled\":false");
                    changed = true;
                }

                if (json.IndexOf("executor_account", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    json = InsertAfterBool(json, "executor_enabled", ",\"executor_account\":\"Sim101\"");
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

        private static string BuildDefaultTemplate()
        {
            return "{"
                + "\"schema_version\":\"glitch.ai.policy.v1\","
                + "\"ai_enabled\":true,"
                + "\"ai_kill_switch\":false,"
                + "\"require_valid_license\":false,"
                + "\"mode\":\"paper\","
                + "\"snapshot_max_age_seconds\":90,"
                + "\"max_contracts\":1,"
                + "\"max_loss_per_trade_usd\":100,"
                + "\"max_daily_loss_usd\":300,"
                + "\"max_trades_per_day\":5,"
                + "\"cooldown_after_loss_minutes\":10,"
                + "\"news_lockout\":false,"
                + "\"executor_enabled\":false,"
                + "\"executor_account\":\"Sim101\","
                + "\"instrument_allowlist\":[\"MNQ\",\"MES\",\"M2K\"],"
                + "\"account_allowlist\":[\"Sim101\",\"Sim102\",\"Sim103\"],"
                + "\"blocked_sessions\":[]"
                + "}";
        }

        private static GlitchAiRailPolicy Parse(string json)
        {
            var policy = new GlitchAiRailPolicy();
            if (string.IsNullOrWhiteSpace(json))
                return policy;

            if (GlitchAiJsonFields.TryExtractBool(json, "ai_enabled", out bool aiEnabled))
                policy.AiEnabled = aiEnabled;
            if (GlitchAiJsonFields.TryExtractBool(json, "ai_kill_switch", out bool killSwitch))
                policy.AiKillSwitch = killSwitch;
            if (GlitchAiJsonFields.TryExtractBool(json, "require_valid_license", out bool requireLicense))
                policy.RequireValidLicense = requireLicense;
            if (GlitchAiJsonFields.TryExtractBool(json, "news_lockout", out bool newsLockout))
                policy.NewsLockout = newsLockout;
            if (GlitchAiJsonFields.TryExtractBool(json, "executor_enabled", out bool executorEnabled))
                policy.ExecutorEnabled = executorEnabled;

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
            if (GlitchAiJsonFields.TryExtractNumber(json, "max_loss_per_trade_usd", out double maxLossTrade))
                policy.MaxLossPerTradeUsd = maxLossTrade;
            if (GlitchAiJsonFields.TryExtractNumber(json, "max_daily_loss_usd", out double maxDailyLoss))
                policy.MaxDailyLossUsd = maxDailyLoss;
            if (GlitchAiJsonFields.TryExtractNumber(json, "max_trades_per_day", out double maxTrades))
                policy.MaxTradesPerDay = Math.Max(1, (int)maxTrades);
            if (GlitchAiJsonFields.TryExtractNumber(json, "cooldown_after_loss_minutes", out double cooldown))
                policy.CooldownAfterLossMinutes = Math.Max(0, (int)cooldown);

            policy.InstrumentAllowlist = ParseStringArray(json, "instrument_allowlist", policy.InstrumentAllowlist);
            policy.AccountAllowlist = ParseStringArray(json, "account_allowlist", policy.AccountAllowlist);
            policy.BlockedSessions = ParseStringArray(json, "blocked_sessions", policy.BlockedSessions);
            return policy;
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

            return values.Count == 0 ? fallback : values;
        }
    }
}
