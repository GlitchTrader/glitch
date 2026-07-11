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

            string positionsArray = ExtractArray(accountJson, "positions");
            if (string.IsNullOrWhiteSpace(positionsArray) || positionsArray == "[]")
                return 0;

            string root = instrumentRoot.Trim().ToUpperInvariant();
            int total = 0;
            int index = 0;
            while (index < positionsArray.Length)
            {
                int objectStart = positionsArray.IndexOf('{', index);
                if (objectStart < 0)
                    break;

                int objectEnd = FindMatchingBrace(positionsArray, objectStart);
                if (objectEnd < 0)
                    break;

                string positionJson = positionsArray.Substring(objectStart, objectEnd - objectStart + 1);
                string positionInstrument = GlitchAiJsonFields.ExtractString(positionJson, "instrument_root");
                if (string.Equals(positionInstrument, root, StringComparison.OrdinalIgnoreCase))
                {
                    double qty;
                    if (GlitchAiJsonFields.TryExtractNumber(positionJson, "quantity", out qty))
                        total += (int)Math.Round(qty, MidpointRounding.AwayFromZero);
                }

                index = objectEnd + 1;
            }

            return total;
        }

        public static double GetRealizedPnlToday(string accountName)
        {
            string accountJson;
            if (!TryGetAccountBlock(accountName, out accountJson))
                return 0;

            double pnl;
            return GlitchAiJsonFields.TryExtractNumber(accountJson, "realized_pnl", out pnl) ? pnl : 0;
        }

        private static string ExtractArray(string json, string key)
        {
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[";
            Match match = Regex.Match(json, pattern, RegexOptions.CultureInvariant);
            if (!match.Success)
                return "[]";

            int start = match.Index + match.Length - 1;
            int end = FindMatchingBracket(json, start);
            if (end < 0)
                return "[]";

            return json.Substring(start, end - start + 1);
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
