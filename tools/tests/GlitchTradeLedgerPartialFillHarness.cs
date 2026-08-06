using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Glitch.Services;

internal static class GlitchTradeLedgerPartialFillHarness
{
    private static int Main()
    {
        try
        {
            PartialFillsAggregateExactlyOnce();
            ScaleOutThenScaleInUsesAllFragments();
            IdenticalNoIdFragmentsAreNotCollapsed();
            ManualAndAiAttributionRemainDistinct();
            NativeExecutionCarriesHermesSignalAttribution();
            ManualThenAiAdditionUsesDistinctFifoLots();
            AiThenManualAdditionUsesDistinctFifoLots();
            DistinctAiIntentsRemainDistinct();
            SameOrderPartialFillsAggregate();
            ReversalRemainderOpensASeparateLot();
            CorrectedAggregateReplacesOnlyTheSameLifecycle();
            FallbackCorrectionRetainsExistingLedgerIdentity();
            ExistingTsvRowsRemainParseable();
            Console.WriteLine("TradeLedger partial-fill harness passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.ToString());
            return 1;
        }
    }

    private static void PartialFillsAggregateExactlyOnce()
    {
        DateTime start = new DateTime(2026, 8, 3, 11, 0, 0, DateTimeKind.Utc);
        var accumulator = new GlitchTradeInsightsService.ExecutionAccumulator();
        var entries = new List<GlitchTradeInsightsService.TradeJournalEvent>
        {
            Execution(start, "Buy", 3, 100, "GLT-COPY-E-trade-1", "SYNC", "entry-1", 3, "Replication"),
            Execution(start.AddMilliseconds(20), "Buy", 2, 102, "GLT-COPY-E-trade-1", "SYNC", "entry-2", 2, "Replication")
        };
        Require(accumulator.Process(entries, entries).Count == 0, "entry fragments closed a trade");

        var context = new List<GlitchTradeInsightsService.TradeJournalEvent>(entries);
        GlitchTradeInsightsService.TradeRoundTrip completed = null;
        GlitchTradeInsightsService.TradeJournalEvent finalExit = null;
        for (int index = 0; index < 5; index++)
        {
            GlitchTradeInsightsService.TradeJournalEvent exitEvent = Execution(
                start.AddMinutes(1).AddMilliseconds(index * 20),
                "Sell",
                1,
                108 + index,
                "GLT-COPY-S-trade-1-" + (index + 1),
                "SL",
                "exit-" + (index + 1),
                1,
                "Replication");
            context.Add(exitEvent);
            IReadOnlyList<GlitchTradeInsightsService.TradeRoundTrip> closed =
                accumulator.Process(new[] { exitEvent }, context);
            Require(closed.Count == (index == 4 ? 1 : 0), "trade completed at the wrong fragment");
            if (closed.Count == 1)
                completed = closed[0];
            finalExit = exitEvent;
        }

        Require(completed != null, "trade did not complete");
        Near(completed.Contracts, 5, "contracts");
        Near(completed.EntryPrice, 100.8, "entry VWAP");
        Near(completed.ExitPrice, 110, "exit VWAP");
        Near(completed.PnlPoints, 46, "P&L");
        Near(completed.CommissionTotal, 10, "commission");
        Require(completed.TradeSource == "Replication", "replication attribution changed");
        Require(completed.EntrySignal == "GLT-COPY-E-trade-1", "entry signal changed");
        Require(accumulator.Process(new[] { finalExit }, context).Count == 0, "duplicate completion emitted");
    }

    private static void ScaleOutThenScaleInUsesAllFragments()
    {
        DateTime start = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        var events = new List<GlitchTradeInsightsService.TradeJournalEvent>
        {
            Execution(start, "Buy", 2, 100, "Entry", "", "manual-entry-1", 0, "Manual"),
            Execution(start.AddMinutes(1), "Sell", 1, 110, "Close", "EXIT", "manual-exit-1", 0, "Manual"),
            Execution(start.AddMinutes(2), "Buy", 1, 120, "Entry", "", "manual-entry-2", 0, "Manual"),
            Execution(start.AddMinutes(3), "Sell", 2, 130, "Close", "EXIT", "manual-exit-2", 0, "Manual")
        };
        GlitchTradeInsightsService.TradeInsightsSnapshot snapshot =
            new GlitchTradeInsightsService().BuildSnapshot(
                events,
                new List<GlitchTradeInsightsService.TradeWarningEvent>(),
                start.AddMinutes(4));

        Require(snapshot.ClosedTrades.Count == 1, "manual fragments produced duplicate trades");
        GlitchTradeInsightsService.TradeRoundTrip trade = snapshot.ClosedTrades[0];
        Near(trade.Contracts, 3, "scale trade contracts");
        Near(trade.EntryPrice, 106.6666666667, "scale trade entry VWAP");
        Near(trade.ExitPrice, 123.3333333333, "scale trade exit VWAP");
        Near(trade.PnlPoints, 50, "scale trade P&L");
        Require(trade.TradeSource == "Manual", "manual attribution changed");
    }

    private static void CorrectedAggregateReplacesOnlyTheSameLifecycle()
    {
        DateTime entry = new DateTime(2026, 8, 3, 13, 0, 0, DateTimeKind.Utc);
        var partial = new GlitchTradeInsightsService.TradeRoundTrip
        {
            AccountName = "Sim102",
            Instrument = "MNQ",
            EntryUtc = entry,
            ExitUtc = entry.AddMinutes(1),
            IsLong = true,
            Contracts = 3,
            EntryPrice = 100,
            ExitPrice = 90,
            TradeSource = "Replication",
            EntrySignal = "GLT-COPY-E-reused",
            EntryOrderIdentity = "native-order-42"
        };
        var corrected = new GlitchTradeInsightsService.TradeRoundTrip
        {
            AccountName = "Sim102",
            Instrument = "MNQ",
            EntryUtc = entry.AddMilliseconds(20),
            ExitUtc = entry.AddMinutes(1).AddMilliseconds(100),
            IsLong = true,
            Contracts = 5,
            EntryPrice = 100.8,
            ExitPrice = 90,
            TradeSource = "Replication",
            EntrySignal = "GLT-COPY-E-reused",
            EntryOrderIdentity = "native-order-42"
        };
        var laterTrade = new GlitchTradeInsightsService.TradeRoundTrip
        {
            AccountName = "Sim102",
            Instrument = "MNQ",
            EntryUtc = entry.AddHours(2),
            ExitUtc = entry.AddHours(2).AddMinutes(1),
            IsLong = true,
            Contracts = 5,
            EntryPrice = 101,
            ExitPrice = 102,
            TradeSource = "Replication",
            EntrySignal = "GLT-COPY-E-reused"
        };

        Require(GlitchTradeLedgerService.AreSameTradeLifecycle(partial, corrected), "corrected aggregate was not correlated");
        Require(GlitchTradeLedgerService.IsPreferredAggregate(partial, corrected), "larger corrected aggregate was not preferred");
        Require(
            GlitchTradeInsightsService.BuildTradeId(partial) == GlitchTradeInsightsService.BuildTradeId(corrected),
            "aggregate correction changed the stable trade id");
        Require(!GlitchTradeLedgerService.AreSameTradeLifecycle(corrected, laterTrade), "reused signal merged a separate later trade");

        string ledgerPath = Path.Combine(Path.GetTempPath(), "glitch-ledger-correction-" + Guid.NewGuid().ToString("N") + ".tsv");
        try
        {
            var ledger = new GlitchTradeLedgerService(ledgerPath);
            IReadOnlyList<GlitchTradeInsightsService.TradeRoundTrip> first =
                ledger.MergeAndGetAll(new[] { partial }, entry.AddMinutes(2));
            string retainedTradeId = first.Single().TradeId;
            IReadOnlyList<GlitchTradeInsightsService.TradeRoundTrip> second =
                ledger.MergeAndGetAll(new[] { corrected }, entry.AddMinutes(3));
            Require(second.Count == 1, "corrected aggregate created a second ledger episode");
            Require(second[0].TradeId == retainedTradeId, "ledger correction replaced the stable trade id");
            Near(second[0].Contracts, 5, "corrected ledger contracts");
            ledger.Flush(entry.AddMinutes(4), true);
        }
        finally
        {
            if (File.Exists(ledgerPath))
                File.Delete(ledgerPath);
        }
    }

    private static void IdenticalNoIdFragmentsAreNotCollapsed()
    {
        DateTime start = new DateTime(2026, 8, 3, 12, 30, 0, DateTimeKind.Utc);
        var events = new List<GlitchTradeInsightsService.TradeJournalEvent>
        {
            Execution(start, "Buy", 1, 100, "Entry", "", "", 0, "Manual"),
            Execution(start.AddMilliseconds(10), "Buy", 1, 100, "Entry", "", "", 0, "Manual"),
            Execution(start.AddMinutes(1), "Sell", 2, 101, "Close", "EXIT", "no-id-exit", 0, "Manual")
        };

        GlitchTradeInsightsService.TradeInsightsSnapshot snapshot =
            new GlitchTradeInsightsService().BuildSnapshot(
                events,
                new List<GlitchTradeInsightsService.TradeWarningEvent>(),
                start.AddMinutes(2));
        Require(snapshot.ClosedTrades.Count == 1, "identical no-id fragments did not produce one trade");
        Near(snapshot.ClosedTrades[0].Contracts, 2, "identical no-id contracts");
        Near(snapshot.ClosedTrades[0].PnlPoints, 2, "identical no-id P&L");
    }

    private static void ManualAndAiAttributionRemainDistinct()
    {
        DateTime start = new DateTime(2026, 8, 3, 12, 45, 0, DateTimeKind.Utc);
        var events = new List<GlitchTradeInsightsService.TradeJournalEvent>
        {
            Execution(start, "Buy", 1, 100, "Entry", "", "manual-entry", 0, "Manual"),
            Execution(start.AddMinutes(1), "Sell", 1, 101, "Close", "EXIT", "manual-exit", 0, "Manual"),
            Execution(start.AddMinutes(2), "Buy", 1, 102, "GLT-AI-E-intent-1", "ENTRY", "ai-entry", 0, "Strategy"),
            Execution(start.AddMinutes(3), "Sell", 1, 100, "GLT-AI-S-intent-1", "SL", "ai-exit", 0, "Strategy")
        };

        GlitchTradeInsightsService.TradeInsightsSnapshot snapshot =
            new GlitchTradeInsightsService().BuildSnapshot(
                events,
                new List<GlitchTradeInsightsService.TradeWarningEvent>(),
                start.AddMinutes(4));
        Require(snapshot.ClosedTrades.Count == 2, "manual and AI trades were merged");
        GlitchTradeInsightsService.TradeRoundTrip manual = snapshot.ClosedTrades.Single(trade => trade.EntrySignal == "Entry");
        GlitchTradeInsightsService.TradeRoundTrip ai = snapshot.ClosedTrades.Single(trade => trade.EntrySignal == "GLT-AI-E-intent-1");
        Require(manual.TradeSource == "Manual", "manual source changed");
        Require(ai.TradeSource == "Strategy", "AI strategy source changed");
        Require(ai.EntryType == "ENTRY", "AI entry attribution changed");
        Require(ai.ExitType == "SL", "AI exit attribution changed");
    }

    private static void ManualThenAiAdditionUsesDistinctFifoLots()
    {
        DateTime start = new DateTime(2026, 8, 3, 14, 0, 0, DateTimeKind.Utc);
        GlitchTradeInsightsService.TradeInsightsSnapshot snapshot = Snapshot(start,
            Execution(start, "Buy", 1, 100, "Entry", "", "m-e1", 1, "Manual", "manual-order-1"),
            Execution(start.AddSeconds(1), "Buy", 2, 110, "GLT-AI-E-intent-1", "ENTRY", "a-e1", 2, "Strategy", "ai-order-1"),
            Execution(start.AddMinutes(1), "Sell", 3, 120, "Close", "EXIT", "x-1", 3, "Manual"));

        Require(snapshot.ClosedTrades.Count == 2, "manual->AI addition did not produce two learning episodes");
        GlitchTradeInsightsService.TradeRoundTrip manual = snapshot.ClosedTrades.Single(trade => trade.EntryOrderIdentity == "manual-order-1");
        GlitchTradeInsightsService.TradeRoundTrip ai = snapshot.ClosedTrades.Single(trade => trade.EntryOrderIdentity == "ai-order-1");
        Near(manual.Contracts, 1, "manual->AI manual contracts");
        Near(manual.PnlPoints, 20, "manual->AI manual P&L");
        Near(manual.CommissionTotal, 2, "manual->AI manual commission");
        Near(ai.Contracts, 2, "manual->AI AI contracts");
        Near(ai.PnlPoints, 20, "manual->AI AI P&L");
        Near(ai.CommissionTotal, 4, "manual->AI AI commission");
        Require(manual.TradeSource == "Manual" && ai.TradeSource == "Strategy", "manual->AI provenance crossed lots");
    }

    private static void NativeExecutionCarriesHermesSignalAttribution()
    {
        DateTime start = new DateTime(2026, 8, 3, 13, 30, 0, DateTimeKind.Utc);
        var accumulator = new GlitchTradeInsightsService.ExecutionAccumulator();
        var events = new List<GlitchTradeInsightsService.TradeJournalEvent>
        {
            NativeExecution(start, "GL1-G1D7A1CB4A410356FFF7-HME", 1, 100, "native-entry"),
            NativeExecution(start.AddMinutes(1), "GL1-G1D7A1CB4A410356FFF7-HT0-LB853106C57B5D7A", -1, 105, "native-exit")
        };

        IReadOnlyList<GlitchTradeInsightsService.TradeRoundTrip> closed = accumulator.Process(events, events);
        Require(closed.Count == 1, "native Hermes execution did not close a trade");
        Require(closed[0].TradeSource == "Strategy", "native Hermes trade source was lost");
        Require(closed[0].OpenReason == "Hermes Entry", "native Hermes open reason was lost");
        Require(closed[0].EntryType == "ENTRY", "native Hermes entry type was lost");
        Require(closed[0].EntrySignal == "GL1-G1D7A1CB4A410356FFF7-HME", "native Hermes entry signal was lost");
    }

    private static void AiThenManualAdditionUsesDistinctFifoLots()
    {
        DateTime start = new DateTime(2026, 8, 3, 14, 30, 0, DateTimeKind.Utc);
        GlitchTradeInsightsService.TradeInsightsSnapshot snapshot = Snapshot(start,
            Execution(start, "Buy", 1, 100, "GLT-AI-E-intent-2", "ENTRY", "a-e2", 0, "Strategy", "ai-order-2"),
            Execution(start.AddSeconds(1), "Buy", 1, 110, "Entry", "", "m-e2", 0, "Manual", "manual-order-2"),
            Execution(start.AddMinutes(1), "Sell", 2, 120, "Close", "EXIT", "x-2", 0, "Manual"));

        Require(snapshot.ClosedTrades.Count == 2, "AI->manual addition did not produce two learning episodes");
        GlitchTradeInsightsService.TradeRoundTrip ai = snapshot.ClosedTrades.Single(trade => trade.EntryOrderIdentity == "ai-order-2");
        GlitchTradeInsightsService.TradeRoundTrip manual = snapshot.ClosedTrades.Single(trade => trade.EntryOrderIdentity == "manual-order-2");
        Near(ai.PnlPoints, 20, "AI->manual AI P&L");
        Near(manual.PnlPoints, 10, "AI->manual manual P&L");
        Require(ai.TradeSource == "Strategy" && manual.TradeSource == "Manual", "AI->manual provenance crossed lots");
    }

    private static void DistinctAiIntentsRemainDistinct()
    {
        DateTime start = new DateTime(2026, 8, 3, 15, 0, 0, DateTimeKind.Utc);
        GlitchTradeInsightsService.TradeInsightsSnapshot snapshot = Snapshot(start,
            Execution(start, "Buy", 1, 100, "GLT-AI-E-intent-A", "ENTRY", "a-e3", 0, "Strategy"),
            Execution(start.AddSeconds(1), "Buy", 1, 105, "GLT-AI-E-intent-B", "ENTRY", "a-e4", 0, "Strategy"),
            Execution(start.AddMinutes(1), "Sell", 2, 110, "GLT-AI-X", "EXIT", "x-3", 0, "Strategy"));

        Require(snapshot.ClosedTrades.Count == 2, "distinct AI intents were merged");
        Require(snapshot.ClosedTrades.Any(trade => trade.EntrySignal == "GLT-AI-E-intent-A"), "first AI intent was lost");
        Require(snapshot.ClosedTrades.Any(trade => trade.EntrySignal == "GLT-AI-E-intent-B"), "second AI intent was lost");
    }

    private static void SameOrderPartialFillsAggregate()
    {
        DateTime start = new DateTime(2026, 8, 3, 15, 30, 0, DateTimeKind.Utc);
        GlitchTradeInsightsService.TradeInsightsSnapshot snapshot = Snapshot(start,
            Execution(start, "Buy", 1, 100, "Entry", "", "p-e1", 1, "Manual", "partial-order-1"),
            Execution(start.AddMilliseconds(20), "Buy", 1, 102, "Entry", "", "p-e2", 1, "Manual", "partial-order-1"),
            Execution(start.AddMinutes(1), "Sell", 2, 110, "Close", "EXIT", "p-x1", 2, "Manual"));

        Require(snapshot.ClosedTrades.Count == 1, "same-order partial fills split into separate episodes");
        GlitchTradeInsightsService.TradeRoundTrip trade = snapshot.ClosedTrades[0];
        Near(trade.Contracts, 2, "same-order contracts");
        Near(trade.EntryPrice, 101, "same-order entry VWAP");
        Near(trade.PnlPoints, 18, "same-order P&L");
        Near(trade.CommissionTotal, 4, "same-order commission");
        Require(trade.EntryOrderIdentity == "partial-order-1", "same-order identity was not retained");
    }

    private static void ReversalRemainderOpensASeparateLot()
    {
        DateTime start = new DateTime(2026, 8, 3, 16, 0, 0, DateTimeKind.Utc);
        GlitchTradeInsightsService.TradeInsightsSnapshot snapshot = Snapshot(start,
            Execution(start, "Buy", 1, 100, "Entry", "", "r-e1", 1, "Manual", "reversal-manual"),
            Execution(start.AddSeconds(1), "Buy", 1, 110, "GLT-AI-E-reversal", "ENTRY", "r-e2", 1, "Strategy", "reversal-ai"),
            Execution(start.AddMinutes(1), "Sell", 3, 90, "Reverse", "", "r-x1", 3, "Manual", "reversal-order"),
            Execution(start.AddMinutes(2), "BuyToCover", 1, 80, "Close", "EXIT", "r-x2", 1, "Manual"));

        Require(snapshot.ClosedTrades.Count == 3, "reversal did not close FIFO lots and open one remainder lot");
        GlitchTradeInsightsService.TradeRoundTrip manualLong = snapshot.ClosedTrades.Single(trade => trade.EntryOrderIdentity == "reversal-manual");
        GlitchTradeInsightsService.TradeRoundTrip aiLong = snapshot.ClosedTrades.Single(trade => trade.EntryOrderIdentity == "reversal-ai");
        GlitchTradeInsightsService.TradeRoundTrip reversalShort = snapshot.ClosedTrades.Single(trade => trade.EntryOrderIdentity == "reversal-order");
        Near(manualLong.PnlPoints, -10, "reversal manual long P&L");
        Near(aiLong.PnlPoints, -20, "reversal AI long P&L");
        Near(reversalShort.PnlPoints, 10, "reversal short P&L");
        Near(manualLong.CommissionTotal, 2, "reversal manual commission");
        Near(aiLong.CommissionTotal, 2, "reversal AI commission");
        Near(reversalShort.CommissionTotal, 2, "reversal remainder commission");
        Require(!reversalShort.IsLong, "reversal remainder opened on the wrong side");
    }

    private static void ExistingTsvRowsRemainParseable()
    {
        const string legacyRow = "legacy-id\t639000000000000000\t639000000600000000\tSim101\tMNQ\tLong\t1\t100\t101\t1\tManual Entry\tManual / Unknown\tUS\tUS\tManual\tMANUAL\tMANUAL\tEntry\tClose\t2";
        MethodInfo parseTrade = typeof(GlitchTradeLedgerService).GetMethod("ParseTrade", BindingFlags.NonPublic | BindingFlags.Static);
        var parsed = (GlitchTradeInsightsService.TradeRoundTrip)parseTrade.Invoke(null, new object[] { legacyRow });
        Require(parsed != null, "legacy 20-column TSV row no longer parses");
        Require(parsed.TradeId == "legacy-id", "legacy trade id changed while parsing");
        Require(string.IsNullOrWhiteSpace(parsed.EntryOrderIdentity), "legacy row invented an entry order identity");
    }

    private static void FallbackCorrectionRetainsExistingLedgerIdentity()
    {
        DateTime entry = new DateTime(2026, 8, 3, 16, 30, 0, DateTimeKind.Utc);
        var partial = new GlitchTradeInsightsService.TradeRoundTrip
        {
            AccountName = "Sim101",
            Instrument = "MNQ",
            EntryUtc = entry,
            ExitUtc = entry.AddMinutes(1),
            IsLong = true,
            Contracts = 1,
            EntryPrice = 100,
            ExitPrice = 90,
            TradeSource = "Manual",
            EntrySignal = "Entry"
        };
        var corrected = new GlitchTradeInsightsService.TradeRoundTrip
        {
            AccountName = "Sim101",
            Instrument = "MNQ",
            EntryUtc = entry.AddMilliseconds(20),
            ExitUtc = entry.AddMinutes(1).AddMilliseconds(20),
            IsLong = true,
            Contracts = 2,
            EntryPrice = 101,
            ExitPrice = 90,
            TradeSource = "Manual",
            EntrySignal = "Entry"
        };

        string ledgerPath = Path.Combine(Path.GetTempPath(), "glitch-ledger-fallback-" + Guid.NewGuid().ToString("N") + ".tsv");
        try
        {
            var ledger = new GlitchTradeLedgerService(ledgerPath);
            string retainedTradeId = ledger.MergeAndGetAll(new[] { partial }, entry.AddMinutes(2)).Single().TradeId;
            IReadOnlyList<GlitchTradeInsightsService.TradeRoundTrip> merged =
                ledger.MergeAndGetAll(new[] { corrected }, entry.AddMinutes(3));
            Require(merged.Count == 1, "fallback correction created a second ledger episode");
            Require(merged[0].TradeId == retainedTradeId, "fallback correction did not retain the existing ledger id");
            Near(merged[0].Contracts, 2, "fallback corrected contracts");
            ledger.Flush(entry.AddMinutes(4), true);
        }
        finally
        {
            if (File.Exists(ledgerPath))
                File.Delete(ledgerPath);
        }
    }

    private static GlitchTradeInsightsService.TradeInsightsSnapshot Snapshot(
        DateTime start,
        params GlitchTradeInsightsService.TradeJournalEvent[] events)
    {
        return new GlitchTradeInsightsService().BuildSnapshot(
            events,
            new List<GlitchTradeInsightsService.TradeWarningEvent>(),
            start.AddHours(1));
    }

    private static GlitchTradeInsightsService.TradeJournalEvent Execution(
        DateTime utc,
        string action,
        int quantity,
        double price,
        string signal,
        string tag,
        string executionId,
        double commission,
        string source,
        string orderIdentity = "")
    {
        string tagToken = string.IsNullOrWhiteSpace(tag) ? "" : " [TAG:" + tag + "]";
        string commissionToken = Math.Abs(commission) < 0.0000001
            ? ""
            : " [COMM:" + commission.ToString("0.########", CultureInfo.InvariantCulture) + "]";
        string orderToken = string.IsNullOrWhiteSpace(orderIdentity) ? "" : " [OID:" + orderIdentity + "]";
        return new GlitchTradeInsightsService.TradeJournalEvent
        {
            UtcTime = utc,
            AccountName = "Sim101",
            Category = "Execution",
            Message = "Exec " + action + " " + quantity + " MNQ @ " +
                      price.ToString("0.########", CultureInfo.InvariantCulture) +
                      " (" + signal + ") [SRC:" + source + "]" + tagToken + commissionToken +
                      " [EID:" + executionId + "]" + orderToken
        };
    }

    private static GlitchTradeInsightsService.TradeJournalEvent NativeExecution(
        DateTime utc,
        string nativeOrder,
        int signedQuantity,
        double price,
        string executionId)
    {
        return new GlitchTradeInsightsService.TradeJournalEvent
        {
            UtcTime = utc,
            AccountName = "Sim101",
            Category = "Execution",
            Message = "native_execution|operation=Add|execution_id=" + executionId
                + "|account=Sim101|instrument=MNQ 09-26|native_order=" + nativeOrder
                + "|signed_quantity=" + signedQuantity.ToString(CultureInfo.InvariantCulture)
                + "|price=" + price.ToString("0.########", CultureInfo.InvariantCulture)
                + "|representable=True"
        };
    }

    private static void Near(double actual, double expected, string label)
    {
        if (Math.Abs(actual - expected) > 0.000001)
            throw new InvalidOperationException(label + " expected " + expected + " but got " + actual);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

namespace Glitch.Services
{
    // The harness exercises ledger correlation without touching persistence.
    // Production supplies the real GlitchStateStore in the AddOn assembly.
    internal static class GlitchStateStore
    {
        internal static IEnumerable<string> WithTsvBanner(IEnumerable<string> lines)
        {
            return lines;
        }

        internal static void WriteAllLinesAtomic(string path, IEnumerable<string> lines)
        {
            File.WriteAllLines(path, lines);
        }
    }
}
