using System;

namespace Glitch.Core
{
    internal static class GlitchNativeIdentity
    {
        internal const string Prefix = "GL1-";
        internal const int MaximumSignalLength = 50;

        internal static string Build(string commandId, string role, string legId = null)
        {
            if (!IsSignalToken(commandId)
                || !IsSignalToken(role)
                || (!string.IsNullOrWhiteSpace(legId) && !IsSignalToken(legId)))
            {
                throw new InvalidOperationException("Native command identity is not signal-safe.");
            }

            string signal = Prefix + commandId + "-" + role
                + (string.IsNullOrWhiteSpace(legId) ? string.Empty : "-" + legId);
            if (signal.Length > MaximumSignalLength)
                throw new InvalidOperationException("Native command identity exceeds NinjaTrader's signal limit.");
            return signal;
        }

        internal static bool TryParse(
            string signal,
            out string commandId,
            out string role)
        {
            string legId;
            return TryParse(signal, out commandId, out role, out legId);
        }

        internal static bool TryParse(
            string signal,
            out string commandId,
            out string role,
            out string legId)
        {
            commandId = null;
            role = null;
            legId = null;
            if (string.IsNullOrWhiteSpace(signal)
                || signal.Length > MaximumSignalLength
                || !signal.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int roleSeparator = signal.IndexOf('-', Prefix.Length);
            if (roleSeparator <= Prefix.Length || roleSeparator + 1 >= signal.Length)
                return false;

            commandId = signal.Substring(Prefix.Length, roleSeparator - Prefix.Length);
            string suffix = signal.Substring(roleSeparator + 1);
            int legSeparator = suffix.IndexOf('-');
            role = legSeparator < 0 ? suffix : suffix.Substring(0, legSeparator);
            legId = legSeparator < 0 ? string.Empty : suffix.Substring(legSeparator + 1);

            if (!IsSignalToken(commandId) || !IsSignalToken(role))
                return false;
            if (legSeparator >= 0 && !IsSignalToken(legId))
                return false;
            return true;
        }

        internal static bool IsGlitchSignal(string signal)
        {
            string commandId;
            string role;
            return TryParse(signal, out commandId, out role);
        }

        internal static bool TryGetRole(string signal, out string role)
        {
            string commandId;
            return TryParse(signal, out commandId, out role);
        }

        internal static bool TryGetProtectionLegId(string signal, out string legId)
        {
            string commandId;
            string role;
            return TryParse(signal, out commandId, out role, out legId)
                && !string.IsNullOrWhiteSpace(legId)
                && IsProtectionRole(role);
        }

        internal static bool IsProtectionRole(string role)
        {
            return IsStopRole(role) || IsTargetRole(role);
        }

        internal static bool IsStopRole(string role)
        {
            return StartsWithRole(role, "HS") || StartsWithRole(role, "PS");
        }

        internal static bool IsTargetRole(string role)
        {
            return StartsWithRole(role, "HT") || StartsWithRole(role, "PT");
        }

        internal static bool IsMasterProtectionRole(string role)
        {
            return StartsWithRole(role, "HS") || StartsWithRole(role, "HT");
        }

        internal static bool IsFollowerProtectionRole(string role)
        {
            return StartsWithRole(role, "PS") || StartsWithRole(role, "PT");
        }

        private static bool StartsWithRole(string role, string prefix)
        {
            return !string.IsNullOrWhiteSpace(role)
                && role.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSignalToken(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf('-') < 0;
        }
    }
}
