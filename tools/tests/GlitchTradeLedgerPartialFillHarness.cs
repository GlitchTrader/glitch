using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            CorrectedAggregateReplacesOnlyTheSameLifecycle();
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
            EntrySignal = "GLT-COPY-E-reused"
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
            EntrySignal = "GLT-COPY-E-reused"
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
        Require(!GlitchTradeLedgerService.AreSameTradeLifecycle(corrected, laterTrade), "reused signal merged a separate later trade");
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

    private static GlitchTradeInsightsService.TradeJournalEvent Execution(
        DateTime utc,
        string action,
        int quantity,
        double price,
        string signal,
        string tag,
        string executionId,
        double commission,
        string source)
    {
        string tagToken = string.IsNullOrWhiteSpace(tag) ? "" : " [TAG:" + tag + "]";
        string commissionToken = Math.Abs(commission) < 0.0000001
            ? ""
            : " [COMM:" + commission.ToString("0.########", CultureInfo.InvariantCulture) + "]";
        return new GlitchTradeInsightsService.TradeJournalEvent
        {
            UtcTime = utc,
            AccountName = "Sim101",
            Category = "Execution",
            Message = "Exec " + action + " " + quantity + " MNQ @ " +
                      price.ToString("0.########", CultureInfo.InvariantCulture) +
                      " (" + signal + ") [SRC:" + source + "]" + tagToken + commissionToken +
                      " [EID:" + executionId + "]"
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
        }
    }
}
