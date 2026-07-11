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

            foreach (string candidate in BuildLookupCandidates(instrumentRoot))
            {
                instrument = TryGetInstrument(candidate, "GetInstrument");
                if (instrument == null)
                    instrument = TryGetInstrument(candidate, "GetInstrumentFuzzy");
                if (instrument != null)
                    return true;
            }

            return false;
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

            if (pointValue > 0 && tickSize > 0)
            {
                return GlitchInstrumentMetadata.Resolved(root, pointValue, tickSize, sessionTemplate);
            }

            return GlitchInstrumentMetadata.Unknown(root);
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
