using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Glitch.UI;

internal static class GlitchFeedAuthorityHarness
{
    private static int checks;
    private static void Assert(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static readonly Type Bus = typeof(GlitchAnalyticsFeedBus);
    private static GlitchIndicatorReading Reading(string contract, string publisher, int minutes = 1)
    {
        return new GlitchIndicatorReading { InstrumentRoot = "M2K", InstrumentFullName = contract,
            Publisher = publisher, Minutes = minutes, UtcTime = DateTime.UtcNow,
            CurrentPrice = contract.Contains("09-") ? 2874.4 : 2896,
            OrderFlowVwap = publisher == "glitch_analytics_bridge" ? (double?)2875 : null,
            DescriptiveStateJson = "{\"source\":\"" + publisher + "\"}", SessionHigh = 2880 };
    }
    private static void Reset()
    {
        ((IDictionary)Bus.GetField("StateByInstrument", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Clear();
        Bus.GetField("_persistenceLoaded", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
        Glitch.Services.GlitchAnalyticsBridgeCacheStore.Feeds.Clear();
        GlitchAnalyticsFeedBus.RegisterBridge("M2K", true);
    }
    private static GlitchIndicatorInstrumentSnapshot Snapshot()
    {
        Assert(GlitchAnalyticsFeedBus.TryGetSnapshot("M2K", out var snapshot), "snapshot unavailable");
        return snapshot;
    }
    private static void FeedCompetition()
    {
        Reset();
        var rich = Reading("M2K 09-26", "glitch_analytics_bridge");
        GlitchAnalyticsFeedBus.Publish(rich);
        GlitchAnalyticsFeedBus.Publish(Reading("M2K 09-26", rich.Publisher, 5));
        for (int i = 0; i < 50; i++)
        {
            GlitchAnalyticsFeedBus.Publish(Reading("M2K 12-26", "glitch_ai_market_ingest"));
            GlitchAnalyticsFeedBus.Publish(Reading("M2K 09-26", "glitch_ai_market_ingest"));
            GlitchAnalyticsFeedBus.Publish(Reading("M2K 12-26", rich.Publisher, 15));
            var snapshot = Snapshot();
            Assert(snapshot.InstrumentFullName == "M2K 09-26" && snapshot.CurrentPrice == 2874.4,
                "competing expiry changed selected chart price");
            Assert(snapshot.TimeframeReadings.Count == 2 && snapshot.TimeframeReadings[1].OrderFlowVwap == 2875,
                "lightweight publisher erased rich analytics or mixed a timeframe");
            Assert(snapshot.DescriptiveStateJson.Contains("glitch_analytics_bridge"), "descriptive source mismatch");
        }
        Assert(rich.Clone().Publisher == rich.Publisher, "clone lost source identity");
        var legacyHigher = Reading("M2K 09-26", "", 15);
        legacyHigher.DescriptiveStateJson = null;
        GlitchAnalyticsFeedBus.Publish(legacyHigher);
        Assert(Snapshot().TimeframeReadings.ContainsKey(15), "legacy chart higher timeframe was lost at hot reload");
    }
    private static void FallbackAndRecovery()
    {
        Reset();
        var stale = Reading("M2K 09-26", "glitch_analytics_bridge");
        stale.UtcTime = DateTime.UtcNow.AddMinutes(-3);
        Bus.GetMethod("StoreReadingUnsafe", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { stale, DateTime.UtcNow });
        GlitchAnalyticsFeedBus.Publish(Reading("M2K 12-26", "glitch_ai_market_ingest"));
        var snapshot = Snapshot();
        Assert(snapshot.InstrumentFullName == "M2K 12-26" && snapshot.TimeframeReadings[1].OrderFlowVwap == null,
            "stale owner prevented fallback or leaked old VWAP");
        GlitchAnalyticsFeedBus.Publish(Reading("M2K 09-26", "glitch_analytics_bridge"));
        Assert(Snapshot().InstrumentFullName == "M2K 09-26", "recovered rich publisher did not regain authority");
    }
    private static void ImportsCannotBypassAuthority()
    {
        Reset();
        var legacyRich = Reading("M2K 09-26", "glitch_analytics_bridge");
        Legacy.StateByInstrument["M2K"] = new LegacyState { LastUpdatedUtc = DateTime.UtcNow,
            TimeframeReadings = new Dictionary<int, GlitchIndicatorReading> { { 1, legacyRich } } };
        Bus.GetMethod("ImportLegacyInstrumentState", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { typeof(Legacy) });
        Assert(Snapshot().InstrumentFullName == "M2K 09-26", "sole legacy chart import lost native expiry");
        foreach (bool richFirst in new[] { true, false })
        {
            Reset();
            var rich = Reading("M2K 09-26", "glitch_analytics_bridge");
            var light = Reading("M2K 12-26", "glitch_ai_market_ingest", 5);
            Glitch.Services.GlitchAnalyticsBridgeCacheStore.Feeds.Add(
                new Glitch.Services.GlitchAnalyticsBridgeCacheStore.PersistedInstrumentFeed {
                    InstrumentRoot = "M2K", InstrumentFullName = "M2K 12-26", CurrentPrice = 2896,
                    LastUpdatedUtc = DateTime.UtcNow,
                    Readings = (richFirst ? new[] { rich, light } : new[] { light, rich }).ToList() });
            var snapshot = Snapshot();
            Assert(snapshot.InstrumentFullName == "M2K 09-26" && snapshot.TimeframeReadings.Count == 1,
                "persisted aggregate bypassed contract/source identity");
            Legacy.StateByInstrument["M2K"] = new LegacyState { LastUpdatedUtc = DateTime.UtcNow,
                TimeframeReadings = new Dictionary<int, GlitchIndicatorReading> { { 1, light } } };
            Bus.GetMethod("ImportLegacyInstrumentState", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { typeof(Legacy) });
            Assert(Snapshot().InstrumentFullName == "M2K 09-26", "legacy import bypassed rich source authority");
        }
    }
    private sealed class LegacyState
    {
        public DateTime LastUpdatedUtc;
        public Dictionary<int, GlitchIndicatorReading> TimeframeReadings;
    }
    private static class Legacy
    { internal static readonly Dictionary<string, LegacyState> StateByInstrument = new Dictionary<string, LegacyState>(); }
    public static void Run()
    {
        FeedCompetition(); FallbackAndRecovery(); ImportsCannotBypassAuthority();
        Console.WriteLine("Glitch feed authority harness passed: " + checks + " checks.");
    }
}

namespace Glitch.Services
{
    internal static class GlitchHermesExchangeWriter
    { public static void RecordAnalyticsBusCollectionLockDuration(TimeSpan elapsed) { } }
    internal static class GlitchInstrumentMetadataService
    {
        public static void RegisterTradeInstrument(string name) { }
        public static void RegisterTradeInstrument(NinjaTrader.Cbi.Instrument instrument) { }
    }
    internal static class GlitchAnalyticsBridgeCacheStore
    {
        internal sealed class PersistedInstrumentFeed
        {
            public string InstrumentRoot, InstrumentFullName, SessionName;
            public DateTime LastUpdatedUtc;
            public double? CurrentPrice, SessionHigh, SessionLow, PreviousSessionHigh, PreviousSessionLow;
            public List<GlitchIndicatorReading> Readings;
        }
        internal static readonly List<PersistedInstrumentFeed> Feeds = new List<PersistedInstrumentFeed>();
        public static List<PersistedInstrumentFeed> Load() => Feeds;
        public static void Save(IEnumerable<PersistedInstrumentFeed> feeds) { }
    }
}
