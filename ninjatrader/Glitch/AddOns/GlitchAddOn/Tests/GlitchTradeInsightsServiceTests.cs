#if GLITCH_ADDON_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Glitch.Services.Tests
{
    [TestFixture]
    internal sealed class GlitchTradeInsightsServiceTests
    {
        [Test]
        public void OrphanBuyToCoverDoesNotCreatePhantomLong()
        {
            DateTime start = new DateTime(2026, 7, 15, 14, 13, 54, DateTimeKind.Utc);
            var events = new List<GlitchTradeInsightsService.TradeJournalEvent>
            {
                Execution(start, "BuyToCover", 1, 29832.25, "GLT-AI-T-old", "EXIT", "orphan-exit"),
                Execution(start.AddMinutes(8), "Buy", 1, 29755.75, "Entry", null, "long-entry"),
                Execution(start.AddMinutes(9), "Sell", 1, 29740.00, "Stop", "EXIT", "long-exit"),
                Execution(start.AddMinutes(17), "SellShort", 1, 29729.00, "Entry", null, "short-entry")
            };

            GlitchTradeInsightsService.TradeInsightsSnapshot snapshot =
                new GlitchTradeInsightsService().BuildSnapshot(
                    events,
                    new List<GlitchTradeInsightsService.TradeWarningEvent>(),
                    start.AddMinutes(18));

            Assert.That(snapshot.ClosedTrades, Has.Count.EqualTo(1));
            Assert.That(snapshot.ClosedTrades[0].IsLong, Is.True);
            Assert.That(snapshot.ClosedTrades[0].Contracts, Is.EqualTo(1));
            Assert.That(snapshot.ClosedTrades[0].EntryPrice, Is.EqualTo(29755.75).Within(0.000001));
            Assert.That(snapshot.ClosedTrades[0].ExitPrice, Is.EqualTo(29740.00).Within(0.000001));
            Assert.That(snapshot.ClosedTrades[0].PnlPoints, Is.EqualTo(-15.75).Within(0.000001));
        }

        [Test]
        public void OrphanSellDoesNotCreatePhantomShort()
        {
            DateTime start = new DateTime(2026, 7, 15, 15, 0, 0, DateTimeKind.Utc);
            var events = new List<GlitchTradeInsightsService.TradeJournalEvent>
            {
                Execution(start, "Sell", 1, 30000.00, "Close", "EXIT", "orphan-exit"),
                Execution(start.AddMinutes(1), "SellShort", 1, 29990.00, "Entry", null, "short-entry"),
                Execution(start.AddMinutes(2), "BuyToCover", 1, 29980.00, "Target", "EXIT", "short-exit")
            };

            GlitchTradeInsightsService.TradeInsightsSnapshot snapshot =
                new GlitchTradeInsightsService().BuildSnapshot(
                    events,
                    new List<GlitchTradeInsightsService.TradeWarningEvent>(),
                    start.AddMinutes(3));

            Assert.That(snapshot.ClosedTrades, Has.Count.EqualTo(1));
            Assert.That(snapshot.ClosedTrades[0].IsLong, Is.False);
            Assert.That(snapshot.ClosedTrades[0].EntryPrice, Is.EqualTo(29990.00).Within(0.000001));
            Assert.That(snapshot.ClosedTrades[0].ExitPrice, Is.EqualTo(29980.00).Within(0.000001));
            Assert.That(snapshot.ClosedTrades[0].PnlPoints, Is.EqualTo(10.00).Within(0.000001));
        }

        [Test]
        public void ReversalCommissionIsSplitAcrossClosingAndOpeningQuantities()
        {
            DateTime start = new DateTime(2026, 7, 15, 16, 0, 0, DateTimeKind.Utc);
            var events = new List<GlitchTradeInsightsService.TradeJournalEvent>
            {
                Execution(start, "Buy", 1, 30000.00, "Entry", null, "long-entry", 1.00),
                Execution(start.AddMinutes(1), "Sell", 2, 29990.00, "Reverse", null, "reverse", 2.00),
                Execution(start.AddMinutes(2), "BuyToCover", 1, 29980.00, "Target", "EXIT", "short-exit", 1.00)
            };

            GlitchTradeInsightsService.TradeInsightsSnapshot snapshot =
                new GlitchTradeInsightsService().BuildSnapshot(
                    events,
                    new List<GlitchTradeInsightsService.TradeWarningEvent>(),
                    start.AddMinutes(3));

            Assert.That(snapshot.ClosedTrades, Has.Count.EqualTo(2));
            Assert.That(snapshot.ClosedTrades[0].CommissionTotal, Is.EqualTo(2.00).Within(0.000001));
            Assert.That(snapshot.ClosedTrades[1].CommissionTotal, Is.EqualTo(2.00).Within(0.000001));
        }

        private static GlitchTradeInsightsService.TradeJournalEvent Execution(
            DateTime utc,
            string action,
            int quantity,
            double price,
            string signal,
            string tag,
            string executionId,
            double commission = 0)
        {
            string tagToken = string.IsNullOrWhiteSpace(tag) ? string.Empty : " [TAG:" + tag + "]";
            string commissionToken = commission == 0
                ? string.Empty
                : " [COMM:" + commission.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture) + "]";
            return new GlitchTradeInsightsService.TradeJournalEvent
            {
                UtcTime = utc,
                AccountName = "Sim101",
                Category = "Execution",
                Message = "Exec " + action + " " + quantity + " MNQ @ " +
                          price.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture) +
                          " (" + signal + ") [SRC:Strategy]" + tagToken + commissionToken + " [EID:" + executionId + "]"
            };
        }
    }
}
#endif

