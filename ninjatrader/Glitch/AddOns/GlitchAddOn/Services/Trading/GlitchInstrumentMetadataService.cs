//
// GlitchInstrumentMetadataService — point value, tick size, session from NT MasterInstrument (R01 / GL-025).
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NinjaTrader.Cbi;

namespace Glitch.Services
{
    public sealed class GlitchInstrumentMetadata
    {
        private GlitchInstrumentMetadata(string instrumentRoot, bool isResolved, double pointValue, double tickSize, string sessionTemplateName)
        {
            InstrumentRoot = instrumentRoot ?? string.Empty;
            IsResolved = isResolved;
            PointValue = pointValue;
            TickSize = tickSize;
            SessionTemplateName = sessionTemplateName ?? string.Empty;
        }

        public string InstrumentRoot { get; }
        public bool IsResolved { get; }
        public double PointValue { get; }
        public double TickSize { get; }
        public string SessionTemplateName { get; }

        public static GlitchInstrumentMetadata Resolved(
            string instrumentRoot,
            double pointValue,
            double tickSize,
            string sessionTemplateName)
        {
            return new GlitchInstrumentMetadata(instrumentRoot, true, pointValue, tickSize, sessionTemplateName);
        }

        public static GlitchInstrumentMetadata Unknown(string instrumentRoot)
        {
            return new GlitchInstrumentMetadata(instrumentRoot, false, 0, 0, string.Empty);
        }
    }

    public static class GlitchInstrumentMetadataService
    {
        private static readonly Dictionary<string, GlitchInstrumentMetadata> Cache =
            new Dictionary<string, GlitchInstrumentMetadata>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Instrument> TradeInstruments =
            new Dictionary<string, Instrument>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Sync = new object();

        public static string NormalizeInstrumentRoot(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string normalized = value.Trim();
            int spaceIndex = normalized.IndexOf(' ');
            if (spaceIndex > 0)
                normalized = normalized.Substring(0, spaceIndex);

            int dotIndex = normalized.IndexOf('.');
            if (dotIndex > 0)
                normalized = normalized.Substring(0, dotIndex);

            normalized = normalized.Trim();
            return normalized.Length == 0 ? null : normalized.ToUpperInvariant();
        }

        public static string GetInstrumentRoot(Instrument instrument)
        {
            if (instrument == null)
                return string.Empty;

            return NormalizeInstrumentRoot(instrument.MasterInstrument?.Name ?? instrument.FullName) ?? string.Empty;
        }

        public static bool TryResolve(string instrumentName, out GlitchInstrumentMetadata metadata)
        {
            metadata = null;
            foreach (string candidate in BuildLookupCandidates(instrumentName))
            {
                if (TryResolveCandidate(candidate, out metadata))
                    return true;
            }

            string root = NormalizeInstrumentRoot(instrumentName);
            metadata = GlitchInstrumentMetadata.Unknown(root ?? "UNKNOWN");
            CacheUnknown(metadata);
            return false;
        }

        public static bool TryResolve(Instrument instrument, out GlitchInstrumentMetadata metadata)
        {
            metadata = null;
            if (instrument == null)
                return false;

            string root = GetInstrumentRoot(instrument);
            if (string.IsNullOrWhiteSpace(root))
                return false;

            RegisterTradeInstrument(instrument);

            lock (Sync)
            {
                if (Cache.TryGetValue(root, out metadata))
                    return metadata.IsResolved;
            }

            metadata = BuildFromInstrument(root, instrument);
            CacheMetadata(metadata);
            return metadata.IsResolved;
        }

        public static bool TryResolveTradeInstrument(string instrumentRoot, out Instrument instrument)
        {
            instrument = null;
            if (string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            string root = NormalizeInstrumentRoot(instrumentRoot);
            lock (Sync)
            {
                if (!string.IsNullOrWhiteSpace(root)
                    && TradeInstruments.TryGetValue(root, out instrument)
                    && IsConcreteTradeInstrument(instrument, root))
                    return true;
            }

            // Hermes intents carry a root (MNQ), not an expiry. A root-only
            // Instrument object has metadata but no live simulation quote.
            // Execution must wait for a dated chart contract registration.
            if (string.Equals(instrumentRoot.Trim(), root, StringComparison.OrdinalIgnoreCase))
            {
                if (TryResolveKnownMicroIndexFrontContract(root, DateTime.UtcNow, out instrument))
                    return true;
                return false;
            }

            foreach (string candidate in BuildLookupCandidates(instrumentRoot))
            {
                instrument = TryGetInstrument(candidate, "GetInstrument");
                if (instrument == null)
                    instrument = TryGetInstrument(candidate, "GetInstrumentFuzzy");
                if (IsConcreteTradeInstrument(instrument, root))
                    return true;
            }

            return false;
        }

        public static void RegisterTradeInstrument(string instrumentFullName)
        {
            Instrument instrument = TryGetInstrument(instrumentFullName, "GetInstrument");
            if (instrument != null)
                RegisterTradeInstrument(instrument);
        }

        public static void RegisterTradeInstrument(Instrument instrument)
        {
            string root = GetInstrumentRoot(instrument);
            if (!IsConcreteTradeInstrument(instrument, root))
                return;

            lock (Sync)
                TradeInstruments[root] = instrument;
        }

        private static bool IsConcreteTradeInstrument(Instrument instrument, string root)
        {
            if (instrument == null || string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(instrument.FullName))
                return false;

            return !string.Equals(instrument.FullName.Trim(), root.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryResolveKnownMicroIndexFrontContract(string root, DateTime utcNow, out Instrument instrument)
        {
            instrument = null;
            string normalizedRoot = NormalizeInstrumentRoot(root);
            if (string.IsNullOrWhiteSpace(normalizedRoot)
                || (!string.Equals(normalizedRoot, "MNQ", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalizedRoot, "MES", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalizedRoot, "M2K", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            string contractName = normalizedRoot + " " + GetEquityIndexFrontContractSuffix(utcNow);
            instrument = TryGetInstrument(contractName, "GetInstrument");
            if (instrument == null)
                instrument = TryGetInstrument(contractName, "GetInstrumentFuzzy");
            if (!IsConcreteTradeInstrument(instrument, normalizedRoot))
            {
                instrument = null;
                return false;
            }

            RegisterTradeInstrument(instrument);
            return true;
        }

        private static string GetEquityIndexFrontContractSuffix(DateTime utcNow)
        {
            DateTime date = utcNow.Date;
            int year = date.Year;
            int[] months = { 3, 6, 9, 12 };
            for (int i = 0; i < months.Length; i++)
            {
                int month = months[i];
                DateTime rollover = GetThirdFriday(year, month).AddDays(-8);
                if (date < rollover)
                    return FormatContractSuffix(month, year);
            }

            return FormatContractSuffix(3, year + 1);
        }

        private static DateTime GetThirdFriday(int year, int month)
        {
            DateTime cursor = new DateTime(year, month, 1);
            int daysUntilFriday = ((int)DayOfWeek.Friday - (int)cursor.DayOfWeek + 7) % 7;
            return cursor.AddDays(daysUntilFriday + 14);
        }

        private static string FormatContractSuffix(int month, int year)
        {
            return month.ToString("00") + "-" + (year % 100).ToString("00");
        }

        public static bool TryGetPointValue(string instrumentName, out double pointValue)
        {
            pointValue = 0;
            if (TryResolve(instrumentName, out GlitchInstrumentMetadata metadata) && metadata.PointValue > 0)
            {
                pointValue = metadata.PointValue;
                return true;
            }

            return false;
        }

        public static bool IsPointBasedInstrumentRoot(string instrumentRoot)
        {
            if (TryResolve(instrumentRoot, out GlitchInstrumentMetadata metadata) && metadata.IsResolved)
            {
                if (metadata.PointValue > 0 && metadata.TickSize > 0)
                    return true;
            }

            return false;
        }

        public static double NormalizeAtrToTicks(double atr, double tickSize)
        {
            if (tickSize <= 0 || double.IsNaN(atr) || double.IsInfinity(atr))
                return atr;

            return atr / tickSize;
        }

        private static bool TryResolveCandidate(string candidate, out GlitchInstrumentMetadata metadata)
        {
            metadata = null;
            string root = NormalizeInstrumentRoot(candidate);
            if (string.IsNullOrWhiteSpace(root))
                return false;

            lock (Sync)
            {
                if (Cache.TryGetValue(root, out metadata))
                    return metadata.IsResolved;
            }

            Instrument instrument = TryGetInstrument(candidate, "GetInstrument");
            if (instrument == null)
                instrument = TryGetInstrument(candidate, "GetInstrumentFuzzy");
            if (instrument == null)
                return false;

            metadata = BuildFromInstrument(root, instrument);
            CacheMetadata(metadata);
            return metadata.IsResolved;
        }

        private static GlitchInstrumentMetadata BuildFromInstrument(string root, Instrument instrument)
        {
            MasterInstrument master = instrument?.MasterInstrument;
            double pointValue = master?.PointValue ?? 0;
            double tickSize = master?.TickSize ?? 0;
            string sessionTemplate = master?.TradingHours?.Name ?? string.Empty;

            GlitchInstrumentMetadata knownMicroMetadata;
            if (TryResolveKnownMicroIndexMetadata(root, pointValue, tickSize, sessionTemplate, out knownMicroMetadata))
                return knownMicroMetadata;

            if (pointValue > 0 && tickSize > 0)
            {
                return GlitchInstrumentMetadata.Resolved(root, pointValue, tickSize, sessionTemplate);
            }

            return GlitchInstrumentMetadata.Unknown(root);
        }

        private static bool TryResolveKnownMicroIndexMetadata(
            string root,
            double ntPointValue,
            double ntTickSize,
            string sessionTemplate,
            out GlitchInstrumentMetadata metadata)
        {
            metadata = null;
            string normalizedRoot = NormalizeInstrumentRoot(root);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
                return false;

            double expectedPointValue;
            double expectedTickSize;
            if (string.Equals(normalizedRoot, "MNQ", StringComparison.OrdinalIgnoreCase))
            {
                expectedPointValue = 2.0d;
                expectedTickSize = 0.25d;
            }
            else if (string.Equals(normalizedRoot, "MES", StringComparison.OrdinalIgnoreCase))
            {
                expectedPointValue = 5.0d;
                expectedTickSize = 0.25d;
            }
            else if (string.Equals(normalizedRoot, "M2K", StringComparison.OrdinalIgnoreCase))
            {
                expectedPointValue = 5.0d;
                expectedTickSize = 0.1d;
            }
            else
            {
                return false;
            }

            double resolvedTickSize = ntTickSize > 0 ? ntTickSize : expectedTickSize;
            if (Math.Abs(ntPointValue - expectedPointValue) > 0.0000001d || ntTickSize <= 0)
            {
                metadata = GlitchInstrumentMetadata.Resolved(
                    normalizedRoot,
                    expectedPointValue,
                    resolvedTickSize,
                    sessionTemplate);
                return true;
            }

            return false;
        }

        private static void CacheMetadata(GlitchInstrumentMetadata metadata)
        {
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.InstrumentRoot))
                return;

            lock (Sync)
                Cache[metadata.InstrumentRoot] = metadata;
        }

        private static void CacheUnknown(GlitchInstrumentMetadata metadata)
        {
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.InstrumentRoot))
                return;

            lock (Sync)
            {
                if (!Cache.ContainsKey(metadata.InstrumentRoot))
                    Cache[metadata.InstrumentRoot] = metadata;
            }
        }

        private static IEnumerable<string> BuildLookupCandidates(string instrumentName)
        {
            var candidates = new List<string>();
            string token = string.IsNullOrWhiteSpace(instrumentName) ? string.Empty : instrumentName.Trim().ToUpperInvariant();
            if (token.Length == 0)
                return candidates;

            candidates.Add(token);

            string beforeSpace = token.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(beforeSpace))
                candidates.Add(beforeSpace);

            return candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static Instrument TryGetInstrument(string candidate, string methodName)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return null;

            try
            {
                MethodInfo resolver = typeof(Instrument)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method =>
                        string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
                        method.ReturnType == typeof(Instrument) &&
                        method.GetParameters().Length == 1 &&
                        method.GetParameters()[0].ParameterType == typeof(string));
                if (resolver == null)
                    return null;

                return resolver.Invoke(null, new object[] { candidate }) as Instrument;
            }
            catch
            {
                return null;
            }
        }
    }
}
