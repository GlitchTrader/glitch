using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Glitch.Services
{
    internal static class GlitchAiJsonFields
    {
        public static string ExtractString(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return null;

            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"";
            Match match = Regex.Match(json, pattern, RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : null;
        }

        public static bool TryExtractNumber(string json, string key, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return false;

            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?(?:\\d+(?:\\.\\d+)?|\\.\\d+))";
            Match match = Regex.Match(json, pattern, RegexOptions.CultureInvariant);
            if (!match.Success)
                return false;

            return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static bool TryExtractBool(string json, string key, out bool value)
        {
            value = false;
            string raw = ExtractString(json, key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*(true|false)";
                Match match = Regex.Match(json, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                if (!match.Success)
                    return false;
                raw = match.Groups[1].Value;
            }

            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }

            return false;
        }

        public static DateTime? TryExtractUtc(string json, string key)
        {
            string raw = ExtractString(json, key);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            DateTime parsed;
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                return null;

            return parsed.ToUniversalTime();
        }
    }
}
