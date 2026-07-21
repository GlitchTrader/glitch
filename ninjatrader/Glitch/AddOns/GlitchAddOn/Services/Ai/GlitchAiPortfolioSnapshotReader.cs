using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Glitch.Services
{
    internal sealed class GlitchPortfolioAccountClassification
    {
        public string AccountName { get; set; }
        public string AccountStatus { get; set; }
        public string PropFirmId { get; set; }
    }

    internal static class GlitchAiPortfolioSnapshotReader
    {
        public static bool TryGetFreshRiskState(
            string accountName,
            DateTime nowUtc,
            int maxAgeSeconds,
            out bool riskLocked,
            out bool evalTargetLocked,
            out double realizedPnl,
            out string failure)
        {
            string accountJson;
            return TryGetFreshRiskState(
                accountName,
                nowUtc,
                maxAgeSeconds,
                out riskLocked,
                out evalTargetLocked,
                out realizedPnl,
                out accountJson,
                out failure);
        }

        public static bool TryGetFreshRiskState(
            string accountName,
            DateTime nowUtc,
            int maxAgeSeconds,
            out bool riskLocked,
            out bool evalTargetLocked,
            out double realizedPnl,
            out string accountJson,
            out string failure)
        {
            riskLocked = false;
            evalTargetLocked = false;
            realizedPnl = 0;
            accountJson = null;
            failure = null;

            string path = GlitchPortfolioSnapshotWriter.GetLatestSnapshotPath();
            if (!File.Exists(path))
            {
                failure = "portfolio_snapshot_missing";
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                DateTime? createdUtc = GlitchAiJsonFields.TryExtractUtc(json, "created_utc");
                if (!createdUtc.HasValue)
                {
                    failure = "portfolio_snapshot_created_utc_missing";
                    return false;
                }

                double ageSeconds = (nowUtc - createdUtc.Value).TotalSeconds;
                if (ageSeconds < -5 || ageSeconds > maxAgeSeconds)
                {
                    failure = "portfolio_snapshot_stale";
                    return false;
                }

                if (!TryGetAccountBlockFromJson(json, accountName, out accountJson))
                {
                    failure = "portfolio_account_missing_" + accountName;
                    return false;
                }

                double workingOrders;
                string positionsArray;
                if (!GlitchAiJsonFields.TryExtractBool(accountJson, "is_risk_locked", out riskLocked)
                    || !GlitchAiJsonFields.TryExtractBool(accountJson, "is_eval_target_locked", out evalTargetLocked)
                    || !GlitchAiJsonFields.TryExtractNumber(accountJson, "realized_pnl", out realizedPnl)
                    || !GlitchAiJsonFields.TryExtractNumber(accountJson, "working_orders", out workingOrders)
                    || !TryExtractArray(accountJson, "positions", out positionsArray))
                {
                    failure = "portfolio_account_fields_incomplete_" + accountName;
                    return false;
                }

                return true;
            }
            catch
            {
                failure = "portfolio_snapshot_unreadable";
                return false;
            }
        }

        public static bool TryGetAccountBlock(string accountName, out string accountJson)
        {
            accountJson = null;
            if (string.IsNullOrWhiteSpace(accountName))
                return false;

            string path = GlitchPortfolioSnapshotWriter.GetLatestSnapshotPath();
            if (!File.Exists(path))
                return false;

            try
            {
                string json = File.ReadAllText(path);
                return TryGetAccountBlockFromJson(json, accountName, out accountJson);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsAccountRiskLocked(string accountName)
        {
            string accountJson;
            if (!TryGetAccountBlock(accountName, out accountJson))
                return false;

            bool locked;
            return GlitchAiJsonFields.TryExtractBool(accountJson, "is_risk_locked", out locked) && locked;
        }

        public static bool IsEvalTargetLocked(string accountName)
        {
            string accountJson;
            if (!TryGetAccountBlock(accountName, out accountJson))
                return false;

            bool locked;
            return GlitchAiJsonFields.TryExtractBool(accountJson, "is_eval_target_locked", out locked) && locked;
        }

        public static int GetOpenPositionQuantity(string accountName, string instrumentRoot)
        {
            string accountJson;
            if (!TryGetAccountBlock(accountName, out accountJson) || string.IsNullOrWhiteSpace(instrumentRoot))
                return 0;

            return GetOpenPositionQuantityFromAccountBlock(accountJson, instrumentRoot);
        }

        public static int GetOpenPositionQuantityFromAccountBlock(string accountJson, string instrumentRoot)
        {
            int quantity;
            return TryGetOpenPositionQuantityFromAccountBlock(accountJson, instrumentRoot, out quantity)
                ? quantity
                : 0;
        }

        public static bool TryGetOpenPositionQuantityFromAccountBlock(
            string accountJson,
            string instrumentRoot,
            out int quantity)
        {
            quantity = 0;
            if (string.IsNullOrWhiteSpace(accountJson) || string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            string positionsArray;
            if (!TryExtractArray(accountJson, "positions", out positionsArray))
                return false;
            if (positionsArray == "[]")
                return true;

            string root = instrumentRoot.Trim().ToUpperInvariant();
            int total = 0;
            int index = 1;
            int limit = positionsArray.Length - 1;
            while (index < limit)
            {
                while (index < limit && char.IsWhiteSpace(positionsArray[index]))
                    index++;
                if (index >= limit)
                    break;
                if (positionsArray[index] != '{')
                    return false;

                int objectEnd = FindMatchingBrace(positionsArray, index);
                if (objectEnd < 0)
                    return false;

                string positionJson = positionsArray.Substring(index, objectEnd - index + 1);
                string positionInstrument = GlitchAiJsonFields.ExtractString(positionJson, "instrument_root");
                string marketPosition = GlitchAiJsonFields.ExtractString(positionJson, "market_position");
                double qty;
                if (string.IsNullOrWhiteSpace(positionInstrument)
                    || string.IsNullOrWhiteSpace(marketPosition)
                    || !GlitchAiJsonFields.TryExtractNumber(positionJson, "quantity", out qty))
                    return false;
                if (string.Equals(positionInstrument, root, StringComparison.OrdinalIgnoreCase))
                {
                    int signedQuantity = (int)Math.Round(Math.Abs(qty), MidpointRounding.AwayFromZero);
                    if (string.Equals(marketPosition, "Short", StringComparison.OrdinalIgnoreCase))
                        signedQuantity = -signedQuantity;
                    else if (!string.Equals(marketPosition, "Long", StringComparison.OrdinalIgnoreCase))
                        return false;
                    total += signedQuantity;
                }

                index = objectEnd + 1;
                while (index < limit && char.IsWhiteSpace(positionsArray[index]))
                    index++;
                if (index >= limit)
                    break;
                if (positionsArray[index] != ',')
                    return false;
                index++;
                while (index < limit && char.IsWhiteSpace(positionsArray[index]))
                    index++;
                if (index >= limit)
                    return false;
            }

            quantity = total;
            return true;
        }

        public static bool TryGetFreshAccountClassifications(
            DateTime nowUtc,
            int maxAgeSeconds,
            out IReadOnlyList<GlitchPortfolioAccountClassification> classifications,
            out string failure)
        {
            classifications = Array.Empty<GlitchPortfolioAccountClassification>();
            failure = null;
            string path = GlitchPortfolioSnapshotWriter.GetLatestSnapshotPath();
            if (!File.Exists(path))
            {
                failure = "portfolio_snapshot_missing";
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                DateTime? createdUtc = GlitchAiJsonFields.TryExtractUtc(json, "created_utc");
                if (!createdUtc.HasValue)
                {
                    failure = "portfolio_snapshot_created_utc_missing";
                    return false;
                }
                double ageSeconds = (nowUtc - createdUtc.Value).TotalSeconds;
                if (ageSeconds < -5 || ageSeconds > maxAgeSeconds)
                {
                    failure = "portfolio_snapshot_stale";
                    return false;
                }
                if (!TryExtractArray(json, "accounts", out string accountsArray))
                {
                    failure = "portfolio_accounts_missing";
                    return false;
                }

                var result = new List<GlitchPortfolioAccountClassification>();
                int index = 1;
                int limit = accountsArray.Length - 1;
                while (index < limit)
                {
                    while (index < limit && char.IsWhiteSpace(accountsArray[index]))
                        index++;
                    if (index >= limit)
                        break;
                    if (accountsArray[index] != '{')
                    {
                        failure = "portfolio_accounts_malformed";
                        return false;
                    }
                    int objectEnd = FindMatchingBrace(accountsArray, index);
                    if (objectEnd < 0)
                    {
                        failure = "portfolio_accounts_malformed";
                        return false;
                    }
                    string accountJson = accountsArray.Substring(index, objectEnd - index + 1);
                    string accountName = GlitchAiJsonFields.ExtractString(accountJson, "account");
                    string accountStatus = GlitchAiJsonFields.ExtractString(accountJson, "account_status");
                    string propFirmId = GlitchAiJsonFields.ExtractString(accountJson, "prop_firm_id");
                    if (string.IsNullOrWhiteSpace(accountName)
                        || string.IsNullOrWhiteSpace(accountStatus)
                        || string.IsNullOrWhiteSpace(propFirmId))
                    {
                        failure = "portfolio_account_classification_incomplete";
                        return false;
                    }
                    result.Add(new GlitchPortfolioAccountClassification
                    {
                        AccountName = accountName.Trim(),
                        AccountStatus = accountStatus.Trim(),
                        PropFirmId = propFirmId.Trim()
                    });

                    index = objectEnd + 1;
                    while (index < limit && char.IsWhiteSpace(accountsArray[index]))
                        index++;
                    if (index >= limit)
                        break;
                    if (accountsArray[index] != ',')
                    {
                        failure = "portfolio_accounts_malformed";
                        return false;
                    }
                    index++;
                }

                classifications = result;
                return true;
            }
            catch
            {
                failure = "portfolio_snapshot_unreadable";
                return false;
            }
        }

        public static double GetRealizedPnlToday(string accountName)
        {
            string accountJson;
            if (!TryGetAccountBlock(accountName, out accountJson))
                return 0;

            double pnl;
            return GlitchAiJsonFields.TryExtractNumber(accountJson, "realized_pnl", out pnl) ? pnl : 0;
        }

        private static bool TryGetAccountBlockFromJson(string json, string accountName, out string accountJson)
        {
            accountJson = null;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(accountName))
                return false;

            // Writer contract: account is serialized as the first field in each
            // account object, so LastIndexOf('{') resolves the correct boundary.
            string marker = "\"account\":" + GlitchSnapshotJson.String(accountName.Trim());
            int start = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return false;
            int objectStart = json.LastIndexOf('{', start);
            if (objectStart < 0)
                return false;
            int end = FindMatchingBrace(json, objectStart);
            if (end < 0)
                return false;
            accountJson = json.Substring(objectStart, end - objectStart + 1);
            return true;
        }

        private static bool TryExtractArray(string json, string key, out string value)
        {
            value = null;
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[";
            Match match = Regex.Match(json, pattern, RegexOptions.CultureInvariant);
            if (!match.Success)
                return false;

            int start = match.Index + match.Length - 1;
            int end = FindMatchingBracket(json, start);
            if (end < 0)
                return false;

            value = json.Substring(start, end - start + 1);
            return true;
        }

        private static int FindMatchingBrace(string json, int start)
        {
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '{')
                    depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        private static int FindMatchingBracket(string json, int start)
        {
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '[')
                    depth++;
                else if (ch == ']')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }
    }
}
