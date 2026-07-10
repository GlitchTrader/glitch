using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Glitch.Services
{
    internal static class GlitchSnapshotJson
    {
        public static string FormatUtc(DateTime value)
        {
            if (value == DateTime.MinValue)
                return string.Empty;

            return value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture)
                : value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        public static string String(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                switch (ch)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (ch < 32)
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(ch);
                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        public static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "null";
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        public static string NullableNumber(double? value)
        {
            if (!value.HasValue)
                return "null";
            return Number(value.Value);
        }

        public static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        public static string IntArray(IReadOnlyList<int> values)
        {
            if (values == null || values.Count == 0)
                return "[]";

            var parts = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
                parts[i] = values[i].ToString(CultureInfo.InvariantCulture);
            return "[" + string.Join(",", parts) + "]";
        }

        public static string ComputeStableHash(string json)
        {
            if (string.IsNullOrEmpty(json))
                return string.Empty;

            unchecked
            {
                int hash = 17;
                for (int i = 0; i < json.Length; i++)
                    hash = (hash * 31) + json[i];
                return hash.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
