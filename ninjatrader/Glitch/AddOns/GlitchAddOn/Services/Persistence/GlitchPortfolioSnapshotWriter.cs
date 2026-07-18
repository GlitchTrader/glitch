using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Glitch.Services
{
    internal sealed class GlitchPortfolioSnapshotPositionRecord
    {
        public string InstrumentFullName { get; set; }
        public string InstrumentRoot { get; set; }
        public string MarketPosition { get; set; }
        public double Quantity { get; set; }
        public double AveragePrice { get; set; }
        public double UnrealizedPnl { get; set; }
    }

    internal sealed class GlitchPortfolioSnapshotAccountRecord
    {
        public string AccountName { get; set; }
        public string AccountStatus { get; set; }
        public string PropFirmId { get; set; }
        public string RuleStatus { get; set; }
        public bool RulesAreSimulated { get; set; }
        public double AccountSize { get; set; }
        public double ProfitTarget { get; set; }
        public double MaxDrawdown { get; set; }
        public double DailyLossLimit { get; set; }
        public double Equity { get; set; }
        public double LiquidationThreshold { get; set; }
        public double BufferMargin { get; set; }
        public double HeadroomRatio { get; set; }
        public double RealizedPnl { get; set; }
        public double UnrealizedPnl { get; set; }
        public double TotalPnl { get; set; }
        public string PositionDisplay { get; set; }
        public bool NativeStateAvailable { get; set; }
        public int WorkingOrderCount { get; set; }
        public double MaxContracts { get; set; }
        public bool IsRiskLocked { get; set; }
        public bool IsEvalTargetLocked { get; set; }
        public string TradingStartTime { get; set; }
        public string TradingEndTime { get; set; }
        public List<GlitchPortfolioSnapshotPositionRecord> Positions { get; set; }
    }

    internal sealed class GlitchPortfolioSnapshotCapture
    {
        public bool IsReplicating { get; set; }
        public string PropFirmRulesSchemaVersion { get; set; }
        public string PropFirmRulesUpdatedAtUtc { get; set; }
        public List<GlitchPortfolioSnapshotAccountRecord> Accounts { get; set; }
    }

    internal static class GlitchPortfolioSnapshotWriter
    {
        public const string SchemaVersion = "glitch.portfolio.snapshot.v1";
        private static readonly TimeSpan WriteThrottle = TimeSpan.FromMinutes(1);
        private static DateTime _lastWriteUtc = DateTime.MinValue;
        private static string _lastSnapshotHash;

        public static bool TryWriteLatestIfDue(DateTime nowUtc, GlitchPortfolioSnapshotCapture capture, string snapshotId = null)
        {
            if (_lastWriteUtc != DateTime.MinValue && (nowUtc - _lastWriteUtc) < WriteThrottle)
                return false;

            return TryWriteLatest(nowUtc, capture, snapshotId);
        }

        public static bool TryWriteLatest(DateTime nowUtc, GlitchPortfolioSnapshotCapture capture, string snapshotId = null)
        {
            if (capture == null)
                return false;

            try
            {
                string json = BuildSnapshotJson(nowUtc, capture, snapshotId);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                string hash = GlitchSnapshotJson.ComputeStableHash(json);
                if (string.Equals(hash, _lastSnapshotHash, StringComparison.Ordinal))
                {
                    _lastWriteUtc = nowUtc;
                    return true;
                }

                string path = GetLatestSnapshotPath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tempPath, path);

                GlitchHistoricalSnapshotExporter.TryArchivePortfolioSnapshot(json, nowUtc);

                _lastSnapshotHash = hash;
                _lastWriteUtc = nowUtc;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string GetLatestSnapshotPath()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("snapshots", "portfolio", "latest.json"));
        }

        public static void TryResolvePropFirmRulesVersion(out string schemaVersion, out string updatedAtUtc)
        {
            schemaVersion = null;
            updatedAtUtc = null;

            try
            {
                string userDir = NinjaTrader.Core.Globals.UserDataDir;
                if (string.IsNullOrWhiteSpace(userDir))
                    return;

                string path = Path.Combine(
                    userDir,
                    "bin",
                    "Custom",
                    "AddOns",
                    "GlitchAddOn",
                    "Resources",
                    "PropFirmRules.json");
                if (!File.Exists(path))
                    return;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return;

                Match schemaMatch = Regex.Match(json, "\"schemaVersion\"\\s*:\\s*\"([^\"]+)\"");
                if (schemaMatch.Success)
                    schemaVersion = schemaMatch.Groups[1].Value;

                Match updatedMatch = Regex.Match(json, "\"updatedAtUtc\"\\s*:\\s*\"([^\"]+)\"");
                if (updatedMatch.Success)
                    updatedAtUtc = updatedMatch.Groups[1].Value;
            }
            catch
            {
            }
        }

        private static string BuildSnapshotJson(DateTime nowUtc, GlitchPortfolioSnapshotCapture capture, string snapshotId)
        {
            if (capture.Accounts == null || capture.Accounts.Count == 0)
                return null;

            TryResolvePropFirmRulesVersion(out string rulesSchema, out string rulesUpdatedAt);
            if (string.IsNullOrWhiteSpace(capture.PropFirmRulesSchemaVersion))
                capture.PropFirmRulesSchemaVersion = rulesSchema;
            if (string.IsNullOrWhiteSpace(capture.PropFirmRulesUpdatedAtUtc))
                capture.PropFirmRulesUpdatedAtUtc = rulesUpdatedAt;

            var accountJson = new List<string>();
            double totalRealized = 0;
            double totalUnrealized = 0;
            double totalPnl = 0;
            for (int i = 0; i < capture.Accounts.Count; i++)
            {
                GlitchPortfolioSnapshotAccountRecord account = capture.Accounts[i];
                if (account == null || string.IsNullOrWhiteSpace(account.AccountName))
                    continue;

                totalRealized += account.RealizedPnl;
                totalUnrealized += account.UnrealizedPnl;
                totalPnl += account.TotalPnl;
                accountJson.Add(BuildAccountJson(account, nowUtc));
            }

            if (accountJson.Count == 0)
                return null;

            if (string.IsNullOrWhiteSpace(snapshotId))
                snapshotId = nowUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var sb = new StringBuilder(4096);
            sb.Append('{');
            sb.Append("\"schema_version\":").Append(GlitchSnapshotJson.String(SchemaVersion)).Append(',');
            sb.Append("\"created_utc\":").Append(GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(nowUtc))).Append(',');
            sb.Append("\"snapshot_id\":").Append(GlitchSnapshotJson.String(snapshotId)).Append(',');
            sb.Append("\"source_mode\":\"live\",");
            sb.Append("\"pnl_basis\":\"nt_account_items\",");
            sb.Append("\"pnl_commission_note\":\"NT RealizedProfitLoss is treated as net of commissions; trade-level commission detail lives in TradeLedger.\",");
            sb.Append("\"is_replicating\":").Append(GlitchSnapshotJson.Bool(capture.IsReplicating)).Append(',');
            sb.Append("\"policy\":{");
            sb.Append("\"prop_firm_rules_schema_version\":").Append(GlitchSnapshotJson.String(capture.PropFirmRulesSchemaVersion)).Append(',');
            sb.Append("\"prop_firm_rules_updated_at_utc\":").Append(GlitchSnapshotJson.String(capture.PropFirmRulesUpdatedAtUtc));
            sb.Append("},");
            sb.Append("\"totals\":{");
            sb.Append("\"realized_pnl\":").Append(GlitchSnapshotJson.Number(totalRealized)).Append(',');
            sb.Append("\"unrealized_pnl\":").Append(GlitchSnapshotJson.Number(totalUnrealized)).Append(',');
            sb.Append("\"total_pnl\":").Append(GlitchSnapshotJson.Number(totalPnl));
            sb.Append("},");
            sb.Append("\"account_count\":").Append(accountJson.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"accounts\":[").Append(string.Join(",", accountJson)).Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        private static string BuildAccountJson(GlitchPortfolioSnapshotAccountRecord account, DateTime nowUtc)
        {
            var positions = new List<string>();
            if (account.Positions != null)
            {
                for (int i = 0; i < account.Positions.Count; i++)
                {
                    GlitchPortfolioSnapshotPositionRecord position = account.Positions[i];
                    if (position == null)
                        continue;
                    positions.Add(BuildPositionJson(position));
                }
            }

            GlitchAiTradingWindowStatus tradingWindow = GlitchAiTradingWindow.Evaluate(
                nowUtc,
                account.TradingStartTime,
                account.TradingEndTime);

            return "{"
                + "\"account\":" + GlitchSnapshotJson.String(account.AccountName) + ","
                + "\"account_status\":" + GlitchSnapshotJson.String(account.AccountStatus) + ","
                + "\"prop_firm_id\":" + GlitchSnapshotJson.String(account.PropFirmId) + ","
                + "\"rule_status\":" + GlitchSnapshotJson.String(account.RuleStatus) + ","
                + "\"rules_are_simulated\":" + GlitchSnapshotJson.Bool(account.RulesAreSimulated) + ","
                + "\"account_size\":" + GlitchSnapshotJson.Number(account.AccountSize) + ","
                + "\"profit_target\":" + GlitchSnapshotJson.Number(account.ProfitTarget) + ","
                + "\"max_drawdown\":" + GlitchSnapshotJson.Number(account.MaxDrawdown) + ","
                + "\"daily_loss_limit\":" + GlitchSnapshotJson.Number(account.DailyLossLimit) + ","
                + "\"equity\":" + GlitchSnapshotJson.Number(account.Equity) + ","
                + "\"liquidation_threshold\":" + GlitchSnapshotJson.Number(account.LiquidationThreshold) + ","
                + "\"buffer_margin\":" + GlitchSnapshotJson.Number(account.BufferMargin) + ","
                + "\"headroom_ratio\":" + GlitchSnapshotJson.Number(account.HeadroomRatio) + ","
                + "\"realized_pnl\":" + GlitchSnapshotJson.Number(account.RealizedPnl) + ","
                + "\"unrealized_pnl\":" + GlitchSnapshotJson.Number(account.UnrealizedPnl) + ","
                + "\"total_pnl\":" + GlitchSnapshotJson.Number(account.TotalPnl) + ","
                + "\"position_display\":" + GlitchSnapshotJson.String(account.PositionDisplay) + ","
                + "\"native_state_available\":" + GlitchSnapshotJson.Bool(account.NativeStateAvailable) + ","
                + "\"working_orders\":" + account.WorkingOrderCount.ToString(CultureInfo.InvariantCulture) + ","
                + "\"max_contracts\":" + GlitchSnapshotJson.Number(account.MaxContracts) + ","
                + "\"is_risk_locked\":" + GlitchSnapshotJson.Bool(account.IsRiskLocked) + ","
                + "\"is_eval_target_locked\":" + GlitchSnapshotJson.Bool(account.IsEvalTargetLocked) + ","
                + "\"trading_start_time_et\":" + GlitchSnapshotJson.String(account.TradingStartTime) + ","
                + "\"trading_end_time_et\":" + GlitchSnapshotJson.String(account.TradingEndTime) + ","
                + "\"trading_window_valid\":" + GlitchSnapshotJson.Bool(tradingWindow.IsValid) + ","
                + "\"trading_session_open\":" + GlitchSnapshotJson.Bool(tradingWindow.IsSessionOpen) + ","
                + "\"entry_window_open\":" + GlitchSnapshotJson.Bool(tradingWindow.IsEntryAllowed) + ","
                + "\"must_flat_utc\":" + GlitchSnapshotJson.String(
                    tradingWindow.MustFlatUtc.HasValue ? GlitchSnapshotJson.FormatUtc(tradingWindow.MustFlatUtc.Value) : string.Empty) + ","
                + "\"seconds_until_must_flat\":" + GlitchSnapshotJson.Number(tradingWindow.SecondsUntilMustFlat) + ","
                + "\"positions\":[" + string.Join(",", positions) + "]"
                + "}";
        }

        private static string BuildPositionJson(GlitchPortfolioSnapshotPositionRecord position)
        {
            return "{"
                + "\"instrument\":" + GlitchSnapshotJson.String(position.InstrumentFullName) + ","
                + "\"instrument_root\":" + GlitchSnapshotJson.String(position.InstrumentRoot) + ","
                + "\"market_position\":" + GlitchSnapshotJson.String(position.MarketPosition) + ","
                + "\"quantity\":" + GlitchSnapshotJson.Number(position.Quantity) + ","
                + "\"average_price\":" + GlitchSnapshotJson.Number(position.AveragePrice) + ","
                + "\"unrealized_pnl\":" + GlitchSnapshotJson.Number(position.UnrealizedPnl)
                + "}";
        }
    }
}
