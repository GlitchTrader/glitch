//
//
//   /$$$$$$  /$$ /$$   /$$               /$$      
//  /$$__  $$| $$|__/  | $$              | $$      
// | $$  \__/| $$ /$$ /$$$$$$    /$$$$$$$| $$$$$$$ 
// | $$ /$$$$| $$| $$|_  $$_/   /$$_____/| $$__  $$
// | $$|_  $$| $$| $$  | $$    | $$      | $$  \ $$
// | $$  \ $$| $$| $$  | $$ /$$| $$      | $$  | $$
// |  $$$$$$/| $$| $$  |  $$$$/|  $$$$$$$| $$  | $$
//  \______/ |__/|__/   \___/   \_______/|__/  |__/
//                                                                                                
//
// __________________________________________________
// __________________________________________________
//
//
// Glitch AddOn
// by GlitchTrader.com
//
// __________________________________________________
// __________________________________________________
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Glitch.Services;

namespace Glitch.UI
{
    public static class GlitchAnalyticsFeedBus
    {
        private static readonly object SyncRoot = new object();
        private static readonly TimeSpan MaintenanceRetentionAge = TimeSpan.FromDays(7);
        private static int _publishCounter;
        private static bool _persistenceLoaded;
        private static DateTime _lastPersistUtc = DateTime.MinValue;
        private static readonly TimeSpan PersistThrottle = TimeSpan.FromSeconds(5);
        private static readonly Dictionary<string, InstrumentFeedState> StateByInstrument =
            new Dictionary<string, InstrumentFeedState>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, BridgePresenceState> BridgeStateByInstrument =
            new Dictionary<string, BridgePresenceState>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<Action>> BridgeBootstrapPublishersByInstrument =
            new Dictionary<string, List<Action>>(StringComparer.OrdinalIgnoreCase);
        private static DateTime _lastLegacyImportUtc = DateTime.MinValue;
        private static readonly TimeSpan LegacyImportThrottle = TimeSpan.FromMilliseconds(500);

        public static void Publish(GlitchIndicatorReading reading)
        {
            if (!TryNormalizeIncomingReading(reading, out GlitchIndicatorReading normalizedReading))
                return;

            string normalizedRoot = NormalizeInstrumentRoot(normalizedReading.InstrumentRoot);
            if (string.IsNullOrWhiteSpace(normalizedRoot) || normalizedReading.Minutes <= 0)
                return;

            DateTime heartbeatUtc = DateTime.UtcNow;

            lock (SyncRoot)
            {
                if (RequireBridgeRegistrationForPublish())
                {
                    BridgePresenceState bridgeState;
                    if (!BridgeStateByInstrument.TryGetValue(normalizedRoot, out bridgeState) ||
                        bridgeState == null ||
                        bridgeState.ActiveInstanceCount <= 0)
                    {
                        return;
                    }
                }

                _publishCounter++;
                if ((_publishCounter % 128) == 0 || StateByInstrument.Count > 32)
                    RunMaintenancePrune(heartbeatUtc);

                InstrumentFeedState state;
                if (!StateByInstrument.TryGetValue(normalizedRoot, out state) || state == null)
                {
                    state = new InstrumentFeedState(normalizedRoot);
                    StateByInstrument[normalizedRoot] = state;
                }

                state.LastUpdatedUtc = heartbeatUtc;
                if (normalizedReading.CurrentPrice.HasValue && normalizedReading.CurrentPrice.Value > 0)
                    state.CurrentPrice = normalizedReading.CurrentPrice;

                if (!string.IsNullOrWhiteSpace(normalizedReading.SessionName))
                    state.SessionName = normalizedReading.SessionName;
                if (normalizedReading.SessionHigh.HasValue)
                    state.SessionHigh = normalizedReading.SessionHigh;
                if (normalizedReading.SessionLow.HasValue)
                    state.SessionLow = normalizedReading.SessionLow;
                if (normalizedReading.PreviousSessionHigh.HasValue)
                    state.PreviousSessionHigh = normalizedReading.PreviousSessionHigh;
                if (normalizedReading.PreviousSessionLow.HasValue)
                    state.PreviousSessionLow = normalizedReading.PreviousSessionLow;

                GlitchIndicatorReading snapshotReading = normalizedReading.Clone();
                snapshotReading.InstrumentRoot = normalizedRoot;
                snapshotReading.UtcTime = heartbeatUtc;
                state.TimeframeReadings[normalizedReading.Minutes] = snapshotReading;
            }

            MaybePersistToDisk();
        }

        public static void EnsurePersistenceLoaded()
        {
            lock (SyncRoot)
            {
                if (_persistenceLoaded)
                    return;

                _persistenceLoaded = true;
            }

            ImportPersistedInstrumentState();
        }

        public static void BootstrapAllRegisteredBridges()
        {
            EnsurePersistenceLoaded();
            DateTime nowUtc = DateTime.UtcNow;
            ImportLegacyBusStateIfNeeded(nowUtc);

            List<string> roots;
            lock (SyncRoot)
            {
                roots = GetKnownInstrumentRootsUnsafe();
            }

            for (int i = 0; i < roots.Count; i++)
                RequestBridgeBootstrapPublish(roots[i]);
        }

        public static IReadOnlyList<string> GetKnownInstrumentRoots()
        {
            EnsurePersistenceLoaded();
            lock (SyncRoot)
                return GetKnownInstrumentRootsUnsafe();
        }

        private static List<string> GetKnownInstrumentRootsUnsafe()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in StateByInstrument.Keys)
                roots.Add(key);
            foreach (string key in BridgeStateByInstrument.Keys)
                roots.Add(key);
            return roots.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void ImportPersistedInstrumentState()
        {
            List<GlitchAnalyticsBridgeCacheStore.PersistedInstrumentFeed> persisted = GlitchAnalyticsBridgeCacheStore.Load();
            if (persisted == null || persisted.Count == 0)
                return;

            lock (SyncRoot)
            {
                for (int i = 0; i < persisted.Count; i++)
                {
                    GlitchAnalyticsBridgeCacheStore.PersistedInstrumentFeed feed = persisted[i];
                    if (feed == null || string.IsNullOrWhiteSpace(feed.InstrumentRoot))
                        continue;

                    string normalizedRoot = NormalizeInstrumentRoot(feed.InstrumentRoot);
                    if (string.IsNullOrWhiteSpace(normalizedRoot))
                        continue;

                    InstrumentFeedState state;
                    if (!StateByInstrument.TryGetValue(normalizedRoot, out state) || state == null)
                    {
                        state = new InstrumentFeedState(normalizedRoot);
                        StateByInstrument[normalizedRoot] = state;
                    }

                    if (feed.LastUpdatedUtc != DateTime.MinValue && feed.LastUpdatedUtc >= state.LastUpdatedUtc)
                        state.LastUpdatedUtc = feed.LastUpdatedUtc;

                    if (HasPositiveValue(feed.CurrentPrice))
                        state.CurrentPrice = feed.CurrentPrice;
                    if (!string.IsNullOrWhiteSpace(feed.SessionName))
                        state.SessionName = feed.SessionName;
                    if (feed.SessionHigh.HasValue)
                        state.SessionHigh = feed.SessionHigh;
                    if (feed.SessionLow.HasValue)
                        state.SessionLow = feed.SessionLow;
                    if (feed.PreviousSessionHigh.HasValue)
                        state.PreviousSessionHigh = feed.PreviousSessionHigh;
                    if (feed.PreviousSessionLow.HasValue)
                        state.PreviousSessionLow = feed.PreviousSessionLow;

                    if (feed.Readings == null)
                        continue;

                    for (int r = 0; r < feed.Readings.Count; r++)
                    {
                        GlitchIndicatorReading reading = feed.Readings[r];
                        if (reading == null || reading.Minutes <= 0)
                            continue;
                        if (!TryNormalizeIncomingReading(reading, out GlitchIndicatorReading normalized))
                            continue;

                        GlitchIndicatorReading existing;
                        if (!state.TimeframeReadings.TryGetValue(normalized.Minutes, out existing) ||
                            existing == null ||
                            normalized.UtcTime >= existing.UtcTime)
                        {
                            state.TimeframeReadings[normalized.Minutes] = normalized;
                        }
                    }
                }
            }
        }

        private static void MaybePersistToDisk()
        {
            DateTime nowUtc = DateTime.UtcNow;
            lock (SyncRoot)
            {
                if (_lastPersistUtc != DateTime.MinValue && (nowUtc - _lastPersistUtc) < PersistThrottle)
                    return;

                _lastPersistUtc = nowUtc;
            }

            try
            {
                List<GlitchAnalyticsBridgeCacheStore.PersistedInstrumentFeed> feeds;
                lock (SyncRoot)
                    feeds = ExportPersistedFeedsUnsafe();

                GlitchAnalyticsBridgeCacheStore.Save(feeds);
            }
            catch
            {
            }
        }

        private static List<GlitchAnalyticsBridgeCacheStore.PersistedInstrumentFeed> ExportPersistedFeedsUnsafe()
        {
            var feeds = new List<GlitchAnalyticsBridgeCacheStore.PersistedInstrumentFeed>();
            foreach (KeyValuePair<string, InstrumentFeedState> entry in StateByInstrument)
            {
                InstrumentFeedState state = entry.Value;
                if (state == null || state.TimeframeReadings == null || state.TimeframeReadings.Count == 0)
                    continue;

                var readings = new List<GlitchIndicatorReading>();
                foreach (GlitchIndicatorReading reading in state.TimeframeReadings.Values)
                {
                    if (reading != null)
                        readings.Add(reading.Clone());
                }

                if (readings.Count == 0)
                    continue;

                feeds.Add(new GlitchAnalyticsBridgeCacheStore.PersistedInstrumentFeed
                {
                    InstrumentRoot = state.InstrumentRoot,
                    LastUpdatedUtc = state.LastUpdatedUtc,
                    CurrentPrice = state.CurrentPrice,
                    SessionName = state.SessionName,
                    SessionHigh = state.SessionHigh,
                    SessionLow = state.SessionLow,
                    PreviousSessionHigh = state.PreviousSessionHigh,
                    PreviousSessionLow = state.PreviousSessionLow,
                    Readings = readings
                });
            }

            return feeds;
        }

        public static void FlushPersistence()
        {
            EnsurePersistenceLoaded();
            lock (SyncRoot)
                _lastPersistUtc = DateTime.MinValue;

            MaybePersistToDisk();
        }

        public static void RegisterBridge(string instrumentRoot, bool publishToGlitchUi)
        {
            string normalizedRoot = NormalizeInstrumentRoot(instrumentRoot);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
                return;

            DateTime nowUtc = DateTime.UtcNow;
            lock (SyncRoot)
            {
                BridgePresenceState state;
                if (!BridgeStateByInstrument.TryGetValue(normalizedRoot, out state) || state == null)
                {
                    state = new BridgePresenceState(normalizedRoot);
                    BridgeStateByInstrument[normalizedRoot] = state;
                    state.ActiveInstanceCount = 1;
                }
                else
                {
                    state.ActiveInstanceCount++;
                }

                state.PublishToGlitchUi = publishToGlitchUi;
                state.LastHeartbeatUtc = nowUtc;
            }
        }

        public static void TouchBridge(
            string instrumentRoot,
            bool publishToGlitchUi,
            bool isTrackedPrimaryTimeframe)
        {
            string normalizedRoot = NormalizeInstrumentRoot(instrumentRoot);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
                return;

            DateTime nowUtc = DateTime.UtcNow;
            lock (SyncRoot)
            {
                BridgePresenceState state;
                if (!BridgeStateByInstrument.TryGetValue(normalizedRoot, out state) || state == null)
                {
                    state = new BridgePresenceState(normalizedRoot)
                    {
                        ActiveInstanceCount = 1
                    };
                    BridgeStateByInstrument[normalizedRoot] = state;
                }

                state.PublishToGlitchUi = publishToGlitchUi;
                state.IsTrackedPrimaryTimeframe = isTrackedPrimaryTimeframe;
                state.LastHeartbeatUtc = nowUtc;
            }
        }

        public static void UnregisterBridge(string instrumentRoot)
        {
            string normalizedRoot = NormalizeInstrumentRoot(instrumentRoot);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
                return;

            lock (SyncRoot)
            {
                BridgePresenceState state;
                if (!BridgeStateByInstrument.TryGetValue(normalizedRoot, out state) || state == null)
                    return;

                state.ActiveInstanceCount--;
                if (state.ActiveInstanceCount <= 0)
                {
                    BridgeStateByInstrument.Remove(normalizedRoot);
                    BridgeBootstrapPublishersByInstrument.Remove(normalizedRoot);
                }
            }
        }

        public static void RegisterBridgeBootstrapPublisher(string instrumentRoot, Action publisher)
        {
            if (publisher == null)
                return;

            string normalizedRoot = NormalizeInstrumentRoot(instrumentRoot);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
                return;

            lock (SyncRoot)
            {
                List<Action> publishers;
                if (!BridgeBootstrapPublishersByInstrument.TryGetValue(normalizedRoot, out publishers) || publishers == null)
                {
                    publishers = new List<Action>();
                    BridgeBootstrapPublishersByInstrument[normalizedRoot] = publishers;
                }

                if (!publishers.Contains(publisher))
                    publishers.Add(publisher);
            }
        }

        public static void UnregisterBridgeBootstrapPublisher(string instrumentRoot, Action publisher)
        {
            string normalizedRoot = NormalizeInstrumentRoot(instrumentRoot);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
                return;

            lock (SyncRoot)
            {
                List<Action> publishers;
                if (!BridgeBootstrapPublishersByInstrument.TryGetValue(normalizedRoot, out publishers) || publishers == null)
                    return;

                if (publisher == null)
                {
                    BridgeBootstrapPublishersByInstrument.Remove(normalizedRoot);
                    return;
                }

                publishers.RemoveAll(p => p == null || p == publisher);
                if (publishers.Count == 0)
                    BridgeBootstrapPublishersByInstrument.Remove(normalizedRoot);
            }
        }

        public static bool RequestBridgeBootstrapPublish(string instrumentRoot)
        {
            string normalizedRoot = NormalizeInstrumentRoot(instrumentRoot);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
                return false;

            List<Action> publishers;
            lock (SyncRoot)
            {
                if (!BridgeBootstrapPublishersByInstrument.TryGetValue(normalizedRoot, out publishers) ||
                    publishers == null ||
                    publishers.Count == 0)
                    return false;

                publishers = publishers
                    .Where(p => p != null)
                    .ToList();
            }

            bool invoked = false;
            foreach (Action publisher in publishers)
            {
                try
                {
                    publisher();
                    invoked = true;
                }
                catch
                {
                }
            }
            return invoked;
        }

        public static IReadOnlyList<string> GetBridgeInstrumentRoots(DateTime nowUtc, TimeSpan maxAge)
        {
            EnsurePersistenceLoaded();
            ImportLegacyBusStateIfNeeded(nowUtc);

            lock (SyncRoot)
            {
                return BridgeStateByInstrument
                    .Where(kvp =>
                        kvp.Value != null &&
                        kvp.Value.ActiveInstanceCount > 0 &&
                        (maxAge <= TimeSpan.Zero || IsWithinAge(kvp.Value.LastHeartbeatUtc, nowUtc, maxAge)))
                    .Select(kvp => kvp.Key)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        internal static bool TryGetBridgeStatus(
            string instrumentRoot,
            DateTime nowUtc,
            TimeSpan maxAge,
            out GlitchBridgeStatus status)
        {
            status = null;

            string normalizedRoot = NormalizeInstrumentRoot(instrumentRoot);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
                return false;

            EnsurePersistenceLoaded();
            ImportLegacyBusStateIfNeeded(nowUtc);

            lock (SyncRoot)
            {
                if (!TryCreateBridgeStatusUnsafe(normalizedRoot, out status) || status == null)
                    return false;

                if (maxAge > TimeSpan.Zero && !IsWithinAge(status.LastHeartbeatUtc, nowUtc, maxAge))
                {
                    status = null;
                    return false;
                }

                return true;
            }
        }

        public static IReadOnlyList<string> GetActiveInstrumentRoots(DateTime nowUtc, TimeSpan maxAge)
        {
            EnsurePersistenceLoaded();
            ImportLegacyBusStateIfNeeded(nowUtc);

            lock (SyncRoot)
            {
                return StateByInstrument
                    .Where(kvp => kvp.Value != null && (maxAge <= TimeSpan.Zero || IsWithinAge(kvp.Value.LastUpdatedUtc, nowUtc, maxAge)))
                    .Select(kvp => kvp.Key)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        internal static bool TryGetSnapshot(
            string instrumentRoot,
            out GlitchIndicatorInstrumentSnapshot snapshot)
        {
            snapshot = null;

            string normalizedRoot = NormalizeInstrumentRoot(instrumentRoot);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
                return false;

            EnsurePersistenceLoaded();
            ImportLegacyBusStateIfNeeded(DateTime.UtcNow);

            lock (SyncRoot)
                return TryCreateSnapshotUnsafe(normalizedRoot, out snapshot);
        }

        internal static bool IsSnapshotFresh(GlitchIndicatorInstrumentSnapshot snapshot, DateTime nowUtc, TimeSpan maxAge)
        {
            if (snapshot == null || maxAge <= TimeSpan.Zero)
                return false;

            return HasReadingWithinAge(snapshot, nowUtc, maxAge);
        }

        internal static bool HasReadingWithinAge(GlitchIndicatorInstrumentSnapshot snapshot, DateTime nowUtc, TimeSpan maxAge)
        {
            if (snapshot == null || maxAge <= TimeSpan.Zero)
                return false;

            if (snapshot.TimeframeReadings != null)
            {
                foreach (GlitchIndicatorReading reading in snapshot.TimeframeReadings.Values)
                {
                    if (reading != null && IsWithinAge(reading.UtcTime, nowUtc, maxAge))
                        return true;
                }
            }

            return IsWithinAge(snapshot.UpdatedUtc, nowUtc, maxAge);
        }

        internal static bool TryGetSnapshot(
            string instrumentRoot,
            DateTime nowUtc,
            TimeSpan maxAge,
            out GlitchIndicatorInstrumentSnapshot snapshot)
        {
            if (!TryGetSnapshot(instrumentRoot, out snapshot) || snapshot == null)
                return false;

            if (maxAge > TimeSpan.Zero && !IsSnapshotFresh(snapshot, nowUtc, maxAge))
            {
                snapshot = null;
                return false;
            }

            return true;
        }

        private static bool TryCreateBridgeStatusUnsafe(string normalizedRoot, out GlitchBridgeStatus status)
        {
            status = null;

            BridgePresenceState state;
            if (!BridgeStateByInstrument.TryGetValue(normalizedRoot, out state) || state == null)
                return false;

            if (state.ActiveInstanceCount <= 0)
                return false;

            status = new GlitchBridgeStatus
            {
                InstrumentRoot = normalizedRoot,
                ActiveInstanceCount = state.ActiveInstanceCount,
                PublishToGlitchUi = state.PublishToGlitchUi,
                IsTrackedPrimaryTimeframe = state.IsTrackedPrimaryTimeframe,
                LastHeartbeatUtc = state.LastHeartbeatUtc
            };
            return true;
        }

        private static bool TryCreateSnapshotUnsafe(string normalizedRoot, out GlitchIndicatorInstrumentSnapshot snapshot)
        {
            snapshot = null;

            InstrumentFeedState state;
            if (!StateByInstrument.TryGetValue(normalizedRoot, out state) || state == null)
                return false;

            var readings = new Dictionary<int, GlitchIndicatorReading>();
            foreach (var kvp in state.TimeframeReadings)
            {
                GlitchIndicatorReading reading = kvp.Value;
                if (reading == null)
                    continue;

                readings[kvp.Key] = reading.Clone();
            }

            if (readings.Count == 0)
                return false;

            GlitchIndicatorReading freshestPriceReading = FindFreshestReadingUnsafe(
                state,
                reading => HasPositiveValue(reading.CurrentPrice));
            GlitchIndicatorReading freshestSessionReading = FindFreshestReadingUnsafe(
                state,
                reading =>
                    !string.IsNullOrWhiteSpace(reading.SessionName) ||
                    reading.SessionHigh.HasValue ||
                    reading.SessionLow.HasValue ||
                    reading.PreviousSessionHigh.HasValue ||
                    reading.PreviousSessionLow.HasValue);

            snapshot = new GlitchIndicatorInstrumentSnapshot
            {
                InstrumentRoot = normalizedRoot,
                UpdatedUtc = state.LastUpdatedUtc,
                CurrentPrice = freshestPriceReading != null && HasPositiveValue(freshestPriceReading.CurrentPrice)
                    ? freshestPriceReading.CurrentPrice
                    : state.CurrentPrice,
                SessionName = freshestSessionReading != null && !string.IsNullOrWhiteSpace(freshestSessionReading.SessionName)
                    ? freshestSessionReading.SessionName
                    : state.SessionName,
                SessionHigh = freshestSessionReading != null && freshestSessionReading.SessionHigh.HasValue
                    ? freshestSessionReading.SessionHigh
                    : state.SessionHigh,
                SessionLow = freshestSessionReading != null && freshestSessionReading.SessionLow.HasValue
                    ? freshestSessionReading.SessionLow
                    : state.SessionLow,
                PreviousSessionHigh = freshestSessionReading != null && freshestSessionReading.PreviousSessionHigh.HasValue
                    ? freshestSessionReading.PreviousSessionHigh
                    : state.PreviousSessionHigh,
                PreviousSessionLow = freshestSessionReading != null && freshestSessionReading.PreviousSessionLow.HasValue
                    ? freshestSessionReading.PreviousSessionLow
                    : state.PreviousSessionLow,
                TimeframeReadings = readings
            };
            return true;
        }

        private static void RunMaintenancePrune(DateTime nowUtc)
        {
            PruneStale(nowUtc, MaintenanceRetentionAge);
            PruneBridgeState(nowUtc, MaintenanceRetentionAge);
        }

        private static bool IsWithinAge(DateTime timestampUtc, DateTime nowUtc, TimeSpan maxAge)
        {
            if (timestampUtc == DateTime.MinValue)
                return false;
            if (maxAge <= TimeSpan.Zero)
                return true;

            return (nowUtc - timestampUtc) <= maxAge;
        }

        private static void PruneStale(DateTime nowUtc, TimeSpan maxAge)
        {
            if (maxAge <= TimeSpan.Zero)
                return;

            var staleKeys = StateByInstrument
                .Where(kvp => kvp.Value == null || (nowUtc - kvp.Value.LastUpdatedUtc) > maxAge)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (string staleKey in staleKeys)
                StateByInstrument.Remove(staleKey);
        }

        // Hot-recompile compatibility:
        // when an old indicator instance keeps publishing into a legacy GlitchAnalyticsFeedBus type,
        // ingest that state into the current bus so AddOn can attach without reapplying indicators.
        private static void ImportLegacyBusStateIfNeeded(DateTime nowUtc)
        {
            if (!IsLegacyImportEnabled())
                return;

            if (!ShouldRunLegacyImport(nowUtc))
                return;

            Assembly currentAssembly = typeof(GlitchAnalyticsFeedBus).Assembly;
            string busTypeName = typeof(GlitchAnalyticsFeedBus).FullName;
            if (string.IsNullOrWhiteSpace(busTypeName))
                return;

            Assembly[] assemblies;
            try
            {
                assemblies = AppDomain.CurrentDomain.GetAssemblies();
            }
            catch
            {
                return;
            }

            if (assemblies == null || assemblies.Length == 0)
                return;

            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null || assembly == currentAssembly)
                    continue;

                Type legacyBusType;
                try
                {
                    legacyBusType = assembly.GetType(busTypeName, false);
                }
                catch
                {
                    continue;
                }

                if (legacyBusType == null || legacyBusType == typeof(GlitchAnalyticsFeedBus))
                    continue;

                ImportLegacyInstrumentState(legacyBusType);
                ImportLegacyBridgeState(legacyBusType);
            }
        }

        private static bool ShouldRunLegacyImport(DateTime nowUtc)
        {
            lock (SyncRoot)
            {
                if (_lastLegacyImportUtc != DateTime.MinValue &&
                    (nowUtc - _lastLegacyImportUtc) < LegacyImportThrottle)
                    return false;

                _lastLegacyImportUtc = nowUtc;
                return true;
            }
        }

        private static bool RequireBridgeRegistrationForPublish()
        {
#if DEBUG
            return false;
#else
            try
            {
                string flag = Environment.GetEnvironmentVariable("GLITCH_ALLOW_UNREGISTERED_FEED_PUBLISH");
                return !string.Equals(flag, "1", StringComparison.Ordinal);
            }
            catch
            {
                return true;
            }
#endif
        }

        private static bool IsLegacyImportEnabled()
        {
#if DEBUG
            return true;
#else
            try
            {
                string flag = Environment.GetEnvironmentVariable("GLITCH_ALLOW_LEGACY_FEED_IMPORT");
                if (string.Equals(flag, "0", StringComparison.Ordinal) ||
                    string.Equals(flag, "false", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (string.Equals(flag, "1", StringComparison.Ordinal) ||
                    string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase))
                    return true;

                return true;
            }
            catch
            {
                return true;
            }
#endif
        }

        private static bool TryNormalizeIncomingReading(GlitchIndicatorReading reading, out GlitchIndicatorReading normalized)
        {
            normalized = null;
            if (reading == null)
                return false;

            string normalizedRoot = NormalizeInstrumentRoot(reading.InstrumentRoot);
            if (string.IsNullOrWhiteSpace(normalizedRoot) || normalizedRoot.Length > 24)
                return false;
            if (reading.Minutes <= 0 || reading.Minutes > 240)
                return false;

            GlitchIndicatorReading clone = reading.Clone();
            clone.InstrumentRoot = normalizedRoot;
            DateTime incomingUtc = reading.UtcTime;
            if (incomingUtc == default || incomingUtc > DateTime.UtcNow.AddMinutes(5))
                clone.UtcTime = DateTime.UtcNow;
            else
                clone.UtcTime = incomingUtc;
            clone.SignalLabel = ClampText(clone.SignalLabel, 96);
            clone.VolatilityHint = ClampText(clone.VolatilityHint, 128);
            clone.TrendHint = ClampText(clone.TrendHint, 128);
            clone.RegimeLabel = ClampText(clone.RegimeLabel, 96);
            clone.NoTradeReasons = ClampText(clone.NoTradeReasons, 256);
            clone.OrderFlowHint = ClampText(clone.OrderFlowHint, 128);
            clone.SessionName = ClampText(clone.SessionName, 32);

            clone.CurrentPrice = NormalizePositiveFinite(clone.CurrentPrice);
            clone.AveragePrice = NormalizePositiveFinite(clone.AveragePrice);
            clone.Atr = NormalizePositiveFinite(clone.Atr);
            clone.Adx = NormalizeFinite(clone.Adx);
            clone.RawScore = NormalizeFinite(clone.RawScore);
            clone.DirectionalScore = NormalizeFinite(clone.DirectionalScore);
            clone.TradeabilityScore = NormalizeFinite(clone.TradeabilityScore);
            clone.Rsi = NormalizeFinite(clone.Rsi);
            clone.StochK = NormalizeFinite(clone.StochK);
            clone.ZScore = NormalizeFinite(clone.ZScore);
            clone.EmaAlignment = NormalizeFinite(clone.EmaAlignment);
            clone.RegimeWeight = NormalizeFinite(clone.RegimeWeight);
            clone.OscillatorCompositeScore = NormalizeFinite(clone.OscillatorCompositeScore);
            clone.MaCompositeScore = NormalizeFinite(clone.MaCompositeScore);
            clone.OrderFlowScore = NormalizeFinite(clone.OrderFlowScore);
            clone.OrderFlowConfidence = NormalizeFinite(clone.OrderFlowConfidence);
            clone.OrderFlowReliability = NormalizeFinite(clone.OrderFlowReliability);
            clone.OrderFlowCumulativeDelta = NormalizeFinite(clone.OrderFlowCumulativeDelta);
            clone.OrderFlowDeltaChange = NormalizeFinite(clone.OrderFlowDeltaChange);
            clone.OrderFlowVwap = NormalizePositiveFinite(clone.OrderFlowVwap);
            clone.OrderFlowVwapDeviation = NormalizeFinite(clone.OrderFlowVwapDeviation);
            clone.OrderFlowAggressionBalance = NormalizeFinite(clone.OrderFlowAggressionBalance);
            clone.OrderFlowDepthImbalance = NormalizeFinite(clone.OrderFlowDepthImbalance);
            clone.SessionHigh = NormalizePositiveFinite(clone.SessionHigh);
            clone.SessionLow = NormalizePositiveFinite(clone.SessionLow);
            clone.PreviousSessionHigh = NormalizePositiveFinite(clone.PreviousSessionHigh);
            clone.PreviousSessionLow = NormalizePositiveFinite(clone.PreviousSessionLow);

            if (double.IsNaN(clone.Score) || double.IsInfinity(clone.Score))
                clone.Score = 0d;

            normalized = clone;
            return true;
        }

        private static string ClampText(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string trimmed = value.Trim();
            if (trimmed.Length <= maxLength)
                return trimmed;

            return trimmed.Substring(0, maxLength);
        }

        private static double? NormalizeFinite(double? value)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
                return null;

            if (Math.Abs(value.Value) > 1000000d)
                return null;

            return value.Value;
        }

        private static double? NormalizePositiveFinite(double? value)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
                return null;

            if (value.Value <= 0 || value.Value > 1000000d)
                return null;

            return value.Value;
        }

        private static void ImportLegacyInstrumentState(Type legacyBusType)
        {
            IDictionary legacyStateByInstrument = ReadLegacyStaticDictionary(legacyBusType, "StateByInstrument");
            if (legacyStateByInstrument == null || legacyStateByInstrument.Count == 0)
                return;

            foreach (DictionaryEntry legacyEntry in SnapshotDictionaryEntries(legacyStateByInstrument))
            {
                string normalizedRoot = NormalizeInstrumentRoot(legacyEntry.Key == null ? null : legacyEntry.Key.ToString());
                if (string.IsNullOrWhiteSpace(normalizedRoot))
                    continue;

                object legacyState = legacyEntry.Value;
                if (legacyState == null)
                    continue;

                DateTime legacyUpdatedUtc = ReadLegacyDateTime(legacyState, "LastUpdatedUtc", DateTime.MinValue);
                double? legacyCurrentPrice = ReadLegacyNullableDouble(legacyState, "CurrentPrice");
                string legacySessionName = ReadLegacyString(legacyState, "SessionName");
                double? legacySessionHigh = ReadLegacyNullableDouble(legacyState, "SessionHigh");
                double? legacySessionLow = ReadLegacyNullableDouble(legacyState, "SessionLow");
                double? legacyPreviousSessionHigh = ReadLegacyNullableDouble(legacyState, "PreviousSessionHigh");
                double? legacyPreviousSessionLow = ReadLegacyNullableDouble(legacyState, "PreviousSessionLow");
                IDictionary legacyTimeframeReadings =
                    ReadLegacyMemberValue(legacyState, "TimeframeReadings") as IDictionary;
                var convertedReadings = new List<GlitchIndicatorReading>();
                if (legacyTimeframeReadings != null && legacyTimeframeReadings.Count > 0)
                {
                    foreach (DictionaryEntry timeframeEntry in SnapshotDictionaryEntries(legacyTimeframeReadings))
                    {
                        int minutes = ConvertToInt(timeframeEntry.Key, 0);
                        if (minutes <= 0)
                            continue;

                        GlitchIndicatorReading converted = ConvertLegacyReading(
                            timeframeEntry.Value,
                            normalizedRoot,
                            legacyUpdatedUtc == DateTime.MinValue ? DateTime.UtcNow : legacyUpdatedUtc);
                        if (converted == null)
                            continue;

                        convertedReadings.Add(converted);
                    }
                }

                lock (SyncRoot)
                {
                    InstrumentFeedState lockedState;
                    if (!StateByInstrument.TryGetValue(normalizedRoot, out lockedState) || lockedState == null)
                    {
                        lockedState = new InstrumentFeedState(normalizedRoot);
                        StateByInstrument[normalizedRoot] = lockedState;
                    }

                    bool legacyIsNewerOrEqual =
                        legacyUpdatedUtc != DateTime.MinValue &&
                        legacyUpdatedUtc >= lockedState.LastUpdatedUtc;

                    if (legacyUpdatedUtc != DateTime.MinValue && legacyUpdatedUtc > lockedState.LastUpdatedUtc)
                        lockedState.LastUpdatedUtc = legacyUpdatedUtc;

                    if (HasPositiveValue(legacyCurrentPrice) &&
                        (legacyIsNewerOrEqual || !HasPositiveValue(lockedState.CurrentPrice)))
                        lockedState.CurrentPrice = legacyCurrentPrice;

                    if (!string.IsNullOrWhiteSpace(legacySessionName) &&
                        (legacyIsNewerOrEqual || string.IsNullOrWhiteSpace(lockedState.SessionName)))
                        lockedState.SessionName = legacySessionName;

                    if (legacySessionHigh.HasValue &&
                        (legacyIsNewerOrEqual || !lockedState.SessionHigh.HasValue))
                        lockedState.SessionHigh = legacySessionHigh;
                    if (legacySessionLow.HasValue &&
                        (legacyIsNewerOrEqual || !lockedState.SessionLow.HasValue))
                        lockedState.SessionLow = legacySessionLow;
                    if (legacyPreviousSessionHigh.HasValue &&
                        (legacyIsNewerOrEqual || !lockedState.PreviousSessionHigh.HasValue))
                        lockedState.PreviousSessionHigh = legacyPreviousSessionHigh;
                    if (legacyPreviousSessionLow.HasValue &&
                        (legacyIsNewerOrEqual || !lockedState.PreviousSessionLow.HasValue))
                        lockedState.PreviousSessionLow = legacyPreviousSessionLow;

                    for (int i = 0; i < convertedReadings.Count; i++)
                    {
                        GlitchIndicatorReading converted = convertedReadings[i];
                        if (converted == null || converted.Minutes <= 0)
                            continue;

                        GlitchIndicatorReading existing;
                        if (!lockedState.TimeframeReadings.TryGetValue(converted.Minutes, out existing) ||
                            existing == null ||
                            converted.UtcTime >= existing.UtcTime)
                        {
                            lockedState.TimeframeReadings[converted.Minutes] = converted;
                        }
                    }
                }
            }
        }

        private static void ImportLegacyBridgeState(Type legacyBusType)
        {
            IDictionary legacyBridgeByInstrument = ReadLegacyStaticDictionary(legacyBusType, "BridgeStateByInstrument");
            if (legacyBridgeByInstrument == null || legacyBridgeByInstrument.Count == 0)
                return;

            foreach (DictionaryEntry legacyEntry in SnapshotDictionaryEntries(legacyBridgeByInstrument))
            {
                string normalizedRoot = NormalizeInstrumentRoot(legacyEntry.Key == null ? null : legacyEntry.Key.ToString());
                if (string.IsNullOrWhiteSpace(normalizedRoot))
                    continue;

                object legacyState = legacyEntry.Value;
                if (legacyState == null)
                    continue;

                int legacyActiveInstances = ConvertToInt(ReadLegacyMemberValue(legacyState, "ActiveInstanceCount"), 0);
                bool legacyPublishToGlitchUi = ConvertToBool(ReadLegacyMemberValue(legacyState, "PublishToGlitchUi"), false);
                bool legacyTrackedPrimaryTf = ConvertToBool(ReadLegacyMemberValue(legacyState, "IsTrackedPrimaryTimeframe"), false);
                DateTime legacyHeartbeatUtc = ReadLegacyDateTime(legacyState, "LastHeartbeatUtc", DateTime.MinValue);

                lock (SyncRoot)
                {
                    BridgePresenceState localState;
                    if (!BridgeStateByInstrument.TryGetValue(normalizedRoot, out localState) || localState == null)
                    {
                        localState = new BridgePresenceState(normalizedRoot);
                        BridgeStateByInstrument[normalizedRoot] = localState;
                    }

                    if (legacyActiveInstances > localState.ActiveInstanceCount)
                        localState.ActiveInstanceCount = legacyActiveInstances;

                    if (legacyHeartbeatUtc >= localState.LastHeartbeatUtc)
                    {
                        localState.PublishToGlitchUi = legacyPublishToGlitchUi;
                        localState.IsTrackedPrimaryTimeframe = legacyTrackedPrimaryTf;
                        localState.LastHeartbeatUtc = legacyHeartbeatUtc;
                    }
                }
            }
        }

        private static List<DictionaryEntry> SnapshotDictionaryEntries(IDictionary source)
        {
            var entries = new List<DictionaryEntry>();
            if (source == null || source.Count == 0)
                return entries;

            try
            {
                foreach (DictionaryEntry entry in source)
                    entries.Add(entry);
            }
            catch
            {
            }

            return entries;
        }

        private static IDictionary ReadLegacyStaticDictionary(Type legacyBusType, string fieldName)
        {
            if (legacyBusType == null || string.IsNullOrWhiteSpace(fieldName))
                return null;

            FieldInfo field;
            try
            {
                field = legacyBusType.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
            }
            catch
            {
                return null;
            }

            if (field == null)
                return null;

            try
            {
                return field.GetValue(null) as IDictionary;
            }
            catch
            {
                return null;
            }
        }

        private static GlitchIndicatorReading ConvertLegacyReading(
            object legacyReading,
            string normalizedRoot,
            DateTime fallbackUtc)
        {
            if (legacyReading == null)
                return null;

            var reading = new GlitchIndicatorReading();
            reading.InstrumentRoot = normalizedRoot;
            reading.Minutes = ConvertToInt(ReadLegacyMemberValue(legacyReading, "Minutes"), 0);
            if (reading.Minutes <= 0)
                return null;

            reading.UtcTime = ReadLegacyDateTime(legacyReading, "UtcTime", fallbackUtc);
            reading.CurrentPrice = ReadLegacyNullableDouble(legacyReading, "CurrentPrice");
            reading.AveragePrice = ReadLegacyNullableDouble(legacyReading, "AveragePrice");
            reading.Atr = ReadLegacyNullableDouble(legacyReading, "Atr");
            reading.Adx = ReadLegacyNullableDouble(legacyReading, "Adx");
            reading.Score = ConvertToDouble(ReadLegacyMemberValue(legacyReading, "Score"), 0);
            reading.RawScore = ReadLegacyNullableDouble(legacyReading, "RawScore");
            reading.DirectionalScore = ReadLegacyNullableDouble(legacyReading, "DirectionalScore");
            reading.TradeabilityScore = ReadLegacyNullableDouble(legacyReading, "TradeabilityScore");
            reading.SignalLabel = ReadLegacyString(legacyReading, "SignalLabel");
            reading.VolatilityHint = ReadLegacyString(legacyReading, "VolatilityHint");
            reading.TrendHint = ReadLegacyString(legacyReading, "TrendHint");
            reading.RegimeLabel = ReadLegacyString(legacyReading, "RegimeLabel");
            reading.NoTradeReasons = ReadLegacyString(legacyReading, "NoTradeReasons");
            reading.Rsi = ReadLegacyNullableDouble(legacyReading, "Rsi");
            reading.StochK = ReadLegacyNullableDouble(legacyReading, "StochK");
            reading.ZScore = ReadLegacyNullableDouble(legacyReading, "ZScore");
            reading.EmaAlignment = ReadLegacyNullableDouble(legacyReading, "EmaAlignment");
            reading.RegimeWeight = ReadLegacyNullableDouble(legacyReading, "RegimeWeight");
            reading.OscillatorCompositeScore = ReadLegacyNullableDouble(legacyReading, "OscillatorCompositeScore");
            reading.MaCompositeScore = ReadLegacyNullableDouble(legacyReading, "MaCompositeScore");
            reading.OrderFlowScore = ReadLegacyNullableDouble(legacyReading, "OrderFlowScore");
            reading.OrderFlowConfidence = ReadLegacyNullableDouble(legacyReading, "OrderFlowConfidence");
            reading.OrderFlowReliability = ReadLegacyNullableDouble(legacyReading, "OrderFlowReliability");
            reading.OrderFlowCumulativeDelta = ReadLegacyNullableDouble(legacyReading, "OrderFlowCumulativeDelta");
            reading.OrderFlowDeltaChange = ReadLegacyNullableDouble(legacyReading, "OrderFlowDeltaChange");
            reading.OrderFlowVwap = ReadLegacyNullableDouble(legacyReading, "OrderFlowVwap");
            reading.OrderFlowVwapDeviation = ReadLegacyNullableDouble(legacyReading, "OrderFlowVwapDeviation");
            reading.OrderFlowAggressionBalance = ReadLegacyNullableDouble(legacyReading, "OrderFlowAggressionBalance");
            reading.OrderFlowDepthImbalance = ReadLegacyNullableDouble(legacyReading, "OrderFlowDepthImbalance");
            reading.OrderFlowHint = ReadLegacyString(legacyReading, "OrderFlowHint");
            reading.SessionName = ReadLegacyString(legacyReading, "SessionName");
            reading.SessionHigh = ReadLegacyNullableDouble(legacyReading, "SessionHigh");
            reading.SessionLow = ReadLegacyNullableDouble(legacyReading, "SessionLow");
            reading.PreviousSessionHigh = ReadLegacyNullableDouble(legacyReading, "PreviousSessionHigh");
            reading.PreviousSessionLow = ReadLegacyNullableDouble(legacyReading, "PreviousSessionLow");
            return reading;
        }

        private static object ReadLegacyMemberValue(object source, string memberName)
        {
            if (source == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            Type sourceType = source.GetType();
            try
            {
                PropertyInfo property = sourceType.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanRead)
                    return property.GetValue(source, null);
            }
            catch
            {
            }

            try
            {
                FieldInfo field = sourceType.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                    return field.GetValue(source);
            }
            catch
            {
            }

            return null;
        }

        private static string ReadLegacyString(object source, string memberName)
        {
            object value = ReadLegacyMemberValue(source, memberName);
            return value == null ? null : value.ToString();
        }

        private static DateTime ReadLegacyDateTime(object source, string memberName, DateTime fallback)
        {
            return ConvertToDateTime(ReadLegacyMemberValue(source, memberName), fallback);
        }

        private static double? ReadLegacyNullableDouble(object source, string memberName)
        {
            object value = ReadLegacyMemberValue(source, memberName);
            if (value == null)
                return null;

            double converted;
            if (double.TryParse(value.ToString(), out converted))
                return converted;

            return null;
        }

        private static int ConvertToInt(object value, int fallback)
        {
            if (value == null)
                return fallback;

            int converted;
            if (int.TryParse(value.ToString(), out converted))
                return converted;

            return fallback;
        }

        private static bool ConvertToBool(object value, bool fallback)
        {
            if (value == null)
                return fallback;

            bool converted;
            if (bool.TryParse(value.ToString(), out converted))
                return converted;

            return fallback;
        }

        private static double ConvertToDouble(object value, double fallback)
        {
            if (value == null)
                return fallback;

            double converted;
            if (double.TryParse(value.ToString(), out converted))
                return converted;

            return fallback;
        }

        private static DateTime ConvertToDateTime(object value, DateTime fallback)
        {
            if (value == null)
                return fallback;

            DateTime converted;
            if (DateTime.TryParse(value.ToString(), out converted))
                return converted;

            return fallback;
        }

        private static void PruneBridgeState(DateTime nowUtc, TimeSpan maxAge)
        {
            if (maxAge <= TimeSpan.Zero)
                return;

            var staleKeys = BridgeStateByInstrument
                .Where(kvp =>
                    kvp.Value == null ||
                    kvp.Value.ActiveInstanceCount <= 0 ||
                    (nowUtc - kvp.Value.LastHeartbeatUtc) > maxAge)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (string staleKey in staleKeys)
                BridgeStateByInstrument.Remove(staleKey);
        }

        private static string NormalizeInstrumentRoot(string value)
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

            return normalized.Trim().ToUpperInvariant();
        }

        private static bool HasPositiveValue(double? value)
        {
            return value.HasValue && value.Value > 0;
        }

        private static GlitchIndicatorReading FindFreshestReadingUnsafe(
            InstrumentFeedState state,
            Func<GlitchIndicatorReading, bool> predicate)
        {
            if (state == null || state.TimeframeReadings == null || state.TimeframeReadings.Count == 0)
                return null;

            GlitchIndicatorReading freshest = null;
            foreach (GlitchIndicatorReading reading in state.TimeframeReadings.Values)
            {
                if (reading == null)
                    continue;
                if (predicate != null && !predicate(reading))
                    continue;

                if (freshest == null ||
                    reading.UtcTime > freshest.UtcTime ||
                    (reading.UtcTime == freshest.UtcTime && reading.Minutes < freshest.Minutes))
                {
                    freshest = reading;
                }
            }

            return freshest;
        }

        private sealed class InstrumentFeedState
        {
            public InstrumentFeedState(string instrumentRoot)
            {
                InstrumentRoot = instrumentRoot;
                LastUpdatedUtc = DateTime.MinValue;
                TimeframeReadings = new Dictionary<int, GlitchIndicatorReading>();
            }

            public string InstrumentRoot { get; }
            public DateTime LastUpdatedUtc { get; set; }
            public double? CurrentPrice { get; set; }
            public string SessionName { get; set; }
            public double? SessionHigh { get; set; }
            public double? SessionLow { get; set; }
            public double? PreviousSessionHigh { get; set; }
            public double? PreviousSessionLow { get; set; }
            public Dictionary<int, GlitchIndicatorReading> TimeframeReadings { get; }
        }

        private sealed class BridgePresenceState
        {
            public BridgePresenceState(string instrumentRoot)
            {
                InstrumentRoot = instrumentRoot;
                LastHeartbeatUtc = DateTime.MinValue;
            }

            public string InstrumentRoot { get; }
            public int ActiveInstanceCount { get; set; }
            public bool PublishToGlitchUi { get; set; }
            public bool IsTrackedPrimaryTimeframe { get; set; }
            public DateTime LastHeartbeatUtc { get; set; }
        }
    }

    internal sealed class GlitchBridgeStatus
    {
        public string InstrumentRoot { get; set; }
        public int ActiveInstanceCount { get; set; }
        public bool PublishToGlitchUi { get; set; }
        public bool IsTrackedPrimaryTimeframe { get; set; }
        public DateTime LastHeartbeatUtc { get; set; }
    }

    internal sealed class GlitchIndicatorInstrumentSnapshot
    {
        public string InstrumentRoot { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public double? CurrentPrice { get; set; }
        public string SessionName { get; set; }
        public double? SessionHigh { get; set; }
        public double? SessionLow { get; set; }
        public double? PreviousSessionHigh { get; set; }
        public double? PreviousSessionLow { get; set; }
        public Dictionary<int, GlitchIndicatorReading> TimeframeReadings { get; set; }
    }

    public sealed class GlitchIndicatorReading
    {
        public string InstrumentRoot { get; set; }
        public int Minutes { get; set; }
        public DateTime UtcTime { get; set; }
        public double? CurrentPrice { get; set; }
        public double? AveragePrice { get; set; }
        public double? Atr { get; set; }
        public double? Adx { get; set; }
        public double Score { get; set; }
        public double? RawScore { get; set; }
        public double? DirectionalScore { get; set; }
        public double? TradeabilityScore { get; set; }
        public string SignalLabel { get; set; }
        public string VolatilityHint { get; set; }
        public string TrendHint { get; set; }
        public string RegimeLabel { get; set; }
        public string NoTradeReasons { get; set; }
        public double? Rsi { get; set; }
        public double? StochK { get; set; }
        public double? ZScore { get; set; }
        public double? EmaAlignment { get; set; }
        public double? RegimeWeight { get; set; }
        public double? OscillatorCompositeScore { get; set; }
        public double? MaCompositeScore { get; set; }
        public double? OrderFlowScore { get; set; }
        public double? OrderFlowConfidence { get; set; }
        public double? OrderFlowReliability { get; set; }
        public double? OrderFlowCumulativeDelta { get; set; }
        public double? OrderFlowDeltaChange { get; set; }
        public double? OrderFlowVwap { get; set; }
        public double? OrderFlowVwapDeviation { get; set; }
        public double? OrderFlowAggressionBalance { get; set; }
        public double? OrderFlowDepthImbalance { get; set; }
        public string OrderFlowHint { get; set; }
        public string SessionName { get; set; }
        public double? SessionHigh { get; set; }
        public double? SessionLow { get; set; }
        public double? PreviousSessionHigh { get; set; }
        public double? PreviousSessionLow { get; set; }

        public GlitchIndicatorReading Clone()
        {
            return new GlitchIndicatorReading
            {
                InstrumentRoot = InstrumentRoot,
                Minutes = Minutes,
                UtcTime = UtcTime,
                CurrentPrice = CurrentPrice,
                AveragePrice = AveragePrice,
                Atr = Atr,
                Adx = Adx,
                Score = Score,
                RawScore = RawScore,
                DirectionalScore = DirectionalScore,
                TradeabilityScore = TradeabilityScore,
                SignalLabel = SignalLabel,
                VolatilityHint = VolatilityHint,
                TrendHint = TrendHint,
                RegimeLabel = RegimeLabel,
                NoTradeReasons = NoTradeReasons,
                Rsi = Rsi,
                StochK = StochK,
                ZScore = ZScore,
                EmaAlignment = EmaAlignment,
                RegimeWeight = RegimeWeight,
                OscillatorCompositeScore = OscillatorCompositeScore,
                MaCompositeScore = MaCompositeScore,
                OrderFlowScore = OrderFlowScore,
                OrderFlowConfidence = OrderFlowConfidence,
                OrderFlowReliability = OrderFlowReliability,
                OrderFlowCumulativeDelta = OrderFlowCumulativeDelta,
                OrderFlowDeltaChange = OrderFlowDeltaChange,
                OrderFlowVwap = OrderFlowVwap,
                OrderFlowVwapDeviation = OrderFlowVwapDeviation,
                OrderFlowAggressionBalance = OrderFlowAggressionBalance,
                OrderFlowDepthImbalance = OrderFlowDepthImbalance,
                OrderFlowHint = OrderFlowHint,
                SessionName = SessionName,
                SessionHigh = SessionHigh,
                SessionLow = SessionLow,
                PreviousSessionHigh = PreviousSessionHigh,
                PreviousSessionLow = PreviousSessionLow
            };
        }
    }
}
