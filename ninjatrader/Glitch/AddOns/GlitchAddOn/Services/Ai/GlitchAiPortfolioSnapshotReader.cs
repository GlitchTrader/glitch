using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Glitch.Services
{
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
                bool nativeStateAvailable;
                string positionsArray;
                if (!GlitchAiJsonFields.TryExtractBool(accountJson, "is_risk_locked", out riskLocked)
                    || !GlitchAiJsonFields.TryExtractBool(accountJson, "is_eval_target_locked", out evalTargetLocked)
                    || !GlitchAiJsonFields.TryExtractBool(accountJson, "native_state_available", out nativeStateAvailable)
                    || !GlitchAiJsonFields.TryExtractNumber(accountJson, "realized_pnl", out realizedPnl)
                    || !GlitchAiJsonFields.TryExtractNumber(accountJson, "working_orders", out workingOrders)
                    || !TryExtractArray(accountJson, "positions", out positionsArray))
                {
                    failure = "portfolio_account_fields_incomplete_" + accountName;
                    return false;
                }
                if (!nativeStateAvailable)
                {
                    failure = "portfolio_native_state_unavailable_" + accountName;
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

        public static bool TryGetOpenPositionQuantityFromAccountBlock(
            string accountJson,
            string instrumentRoot,
            out int quantity)
        {
            quantity = 0;
            if (string.IsNullOrWhiteSpace(accountJson) || string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            List<string> positions;
            if (!TryGetPositionBlocks(accountJson, out positions))
                return false;

            string root = instrumentRoot.Trim().ToUpperInvariant();
            int total = 0;
            foreach (string positionJson in positions)
            {
                string positionInstrument = GlitchAiJsonFields.ExtractString(positionJson, "instrument_root");
                string marketPosition = GlitchAiJsonFields.ExtractString(positionJson, "market_position");
                double qty;
                if (string.IsNullOrWhiteSpace(positionInstrument)
                    || string.IsNullOrWhiteSpace(marketPosition)
                    || !GlitchAiJsonFields.TryExtractNumber(positionJson, "quantity", out qty))
                    return false;
                if (string.Equals(positionInstrument, root, StringComparison.OrdinalIgnoreCase))
                {
                    int roundedQuantity = (int)Math.Round(Math.Abs(qty), MidpointRounding.AwayFromZero);
                    if (string.Equals(marketPosition, "Long", StringComparison.OrdinalIgnoreCase))
                        total += roundedQuantity;
                    else if (string.Equals(marketPosition, "Short", StringComparison.OrdinalIgnoreCase))
                        total -= roundedQuantity;
                    else if (!string.Equals(marketPosition, "Flat", StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }

            quantity = total;
            return true;
        }

        public static bool TryGetTotalOpenContractsFromAccountBlock(string accountJson, out int totalContracts)
        {
            totalContracts = 0;
            List<string> positions;
            if (!TryGetPositionBlocks(accountJson, out positions))
                return false;

            foreach (string positionJson in positions)
            {
                string marketPosition = GlitchAiJsonFields.ExtractString(positionJson, "market_position");
                double quantity;
                if (string.IsNullOrWhiteSpace(marketPosition)
                    || !GlitchAiJsonFields.TryExtractNumber(positionJson, "quantity", out quantity))
                    return false;
                if (string.Equals(marketPosition, "Flat", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(marketPosition, "Long", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(marketPosition, "Short", StringComparison.OrdinalIgnoreCase))
                    return false;

                totalContracts += (int)Math.Round(Math.Abs(quantity), MidpointRounding.AwayFromZero);
            }

            return true;
        }

        private static bool TryGetAccountBlockFromJson(string json, string accountName, out string accountJson)
        {
            accountJson = null;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(accountName))
                return false;

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

        private static bool TryGetPositionBlocks(string accountJson, out List<string> positions)
        {
            positions = new List<string>();
            string positionsArray;
            if (!TryExtractArray(accountJson, "positions", out positionsArray))
                return false;

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
                positions.Add(positionsArray.Substring(index, objectEnd - index + 1));

                index = objectEnd + 1;
                while (index < limit && char.IsWhiteSpace(positionsArray[index]))
                    index++;
                if (index >= limit)
                    break;
                if (positionsArray[index] != ',')
                    return false;
                index++;
            }

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
