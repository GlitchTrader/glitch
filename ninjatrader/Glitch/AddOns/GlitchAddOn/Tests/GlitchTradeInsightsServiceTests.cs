#if GLITCH_ADDON_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Glitch.Services.Tests
{
    [TestFixture]
    internal sealed class GlitchSyncLifecycleStateTests
    {
        [TestCase(-1)]
        [TestCase(4)]
        public void OppositeOrOverexposedFollowerFlattensThenCopiesPositiveTwoExactlyOnce(int followerActual)
        {
            Assert.That(
                GlitchSyncLifecycleState.DecideInitial(2, followerActual),
                Is.EqualTo(GlitchSyncInitialAction.SubmitFlatten));
            var state = new GlitchSyncLifecycleState(followerActual);

            Assert.That(state.TryBeginFlatten(), Is.True);
            Assert.That(state.TryBeginFlatten(), Is.False);
            state.MarkFlattenSubmitted(true);
            Assert.That(
                state.ObserveFlatten(0, Math.Abs(followerActual)),
                Is.EqualTo(GlitchSyncObservation.ContinueTail));
            Assert.That(state.TryBeginTail(0, 2), Is.True);
            Assert.That(state.TryBeginTail(0, 2), Is.False);
            state.MarkTailSubmitted(true);
            Assert.That(state.ObserveTail(2, 2), Is.EqualTo(GlitchSyncObservation.Completed));
            Assert.That(state.ObserveTail(2, 2), Is.EqualTo(GlitchSyncObservation.None));
            Assert.That(state.IsTerminal, Is.True);
        }

        [Test]
        public void PartialFlattenAndTailCallbacksWaitWithoutResubmission()
        {
            var state = new GlitchSyncLifecycleState(4);

            Assert.That(state.TryBeginFlatten(), Is.True);
            state.MarkFlattenSubmitted(true);
            Assert.That(state.ObserveFlatten(3, 1), Is.EqualTo(GlitchSyncObservation.None));
            Assert.That(state.ObserveFlatten(3, 1), Is.EqualTo(GlitchSyncObservation.None));
            Assert.That(state.ObserveFlatten(0, 4), Is.EqualTo(GlitchSyncObservation.ContinueTail));
            Assert.That(state.TryBeginTail(0, 2), Is.True);
            state.MarkTailSubmitted(true);
            Assert.That(state.ObserveTail(1, 1), Is.EqualTo(GlitchSyncObservation.None));
            Assert.That(state.ObserveTail(1, 1), Is.EqualTo(GlitchSyncObservation.None));
            Assert.That(state.ObserveTail(2, 2), Is.EqualTo(GlitchSyncObservation.Completed));
        }

        [Test]
        public void FailedFlattenTerminatesWithoutTail()
        {
            var state = new GlitchSyncLifecycleState(-1);

            Assert.That(state.TryBeginFlatten(), Is.True);
            state.MarkFlattenSubmitted(false);
            Assert.That(state.IsTerminal, Is.True);
            Assert.That(state.ObserveFlatten(0, 0), Is.EqualTo(GlitchSyncObservation.None));
            Assert.That(state.TryBeginTail(0, 2), Is.False);
        }

        [Test]
        public void HumanOverrideSupersedesPendingSync()
        {
            var flatten = new GlitchSyncLifecycleState(-1);
            Assert.That(flatten.TryBeginFlatten(), Is.True);
            flatten.MarkFlattenSubmitted(true);
            Assert.That(flatten.ObserveFlatten(0, 0), Is.EqualTo(GlitchSyncObservation.ManualOverride));
            Assert.That(flatten.IsTerminal, Is.True);

            var tail = new GlitchSyncLifecycleState(0);
            Assert.That(tail.TryBeginTail(0, 2), Is.True);
            tail.MarkTailSubmitted(true);
            Assert.That(tail.ObserveTail(1, 0), Is.EqualTo(GlitchSyncObservation.ManualOverride));
            Assert.That(tail.IsTerminal, Is.True);
        }

        [Test]
        public void HumanPartialCloseCannotMasqueradeAsOwnedFlattenProgress()
        {
            var state = new GlitchSyncLifecycleState(4);

            Assert.That(state.TryBeginFlatten(), Is.True);
            state.MarkFlattenSubmitted(true);
            Assert.That(state.ObserveFlatten(3, 0), Is.EqualTo(GlitchSyncObservation.ManualOverride));
            Assert.That(state.IsTerminal, Is.True);
        }
    }

    [TestFixture]
    internal sealed class GlitchReplicationProtectionAllocationTests
    {
        [Test]
        public void TwoPartialFillsReceiveDistinctProtectionLegs()
        {
            GlitchReplicationProtectionPlan plan = Plan(1, 1);

            Assert.That(
                GlitchReplicationProtection.TryScalePlanSlice(plan, 1.0, 0, 1, out List<GlitchScaledProtectionLeg> first),
                Is.True);
            Assert.That(
                GlitchReplicationProtection.TryScalePlanSlice(plan, 1.0, 1, 1, out List<GlitchScaledProtectionLeg> second),
                Is.True);
            Assert.That(first.Select(leg => leg.SourceToken), Is.EqualTo(new[] { "leg1" }));
            Assert.That(second.Select(leg => leg.SourceToken), Is.EqualTo(new[] { "leg2" }));
        }

        [Test]
        public void FractionalRatioUsesCumulativeAwayFromZeroAllocation()
        {
            GlitchReplicationProtectionPlan plan = Plan(1, 1);

            Assert.That(GlitchReplicationProtection.ScaleFollowerQuantity(1, 0.5), Is.EqualTo(1));
            Assert.That(GlitchReplicationProtection.ScaleFollowerQuantity(2, 0.5), Is.EqualTo(1));
            Assert.That(
                GlitchReplicationProtection.TryScalePlanSlice(plan, 0.5, 0, 1, out List<GlitchScaledProtectionLeg> scaled),
                Is.True);
            Assert.That(scaled.Select(leg => leg.SourceToken), Is.EqualTo(new[] { "leg1" }));
            Assert.That(
                GlitchReplicationProtection.TryScalePlanSlice(plan, 0.5, 1, 1, out _),
                Is.False);
        }

        [Test]
        public void ThreeLegPlanSlicesAcrossOrderedLegBoundaries()
        {
            GlitchReplicationProtectionPlan plan = Plan(1, 1, 1);

            Assert.That(
                GlitchReplicationProtection.TryScalePlanSlice(plan, 1.0, 1, 2, out List<GlitchScaledProtectionLeg> scaled),
                Is.True);
            Assert.That(scaled.Select(leg => leg.SourceToken), Is.EqualTo(new[] { "leg2", "leg3" }));
            Assert.That(scaled.Sum(leg => leg.Quantity), Is.EqualTo(2));
        }

        [Test]
        public void SyncExistingQuantitySelectsOnlyMissingTail()
        {
            GlitchReplicationProtectionPlan plan = Plan(1, 1, 1);

            Assert.That(
                GlitchReplicationProtection.TryScalePlanSlice(plan, 1.0, 2, 1, out List<GlitchScaledProtectionLeg> scaled),
                Is.True);
            Assert.That(scaled.Select(leg => leg.SourceToken), Is.EqualTo(new[] { "leg3" }));
        }

        [Test]
        public void DuplicateAllocationCallbackIsDeterministicAndDoesNotMutatePlan()
        {
            GlitchReplicationProtectionPlan plan = Plan(1, 1);

            Assert.That(
                GlitchReplicationProtection.TryScalePlanSlice(plan, 1.0, 1, 1, out List<GlitchScaledProtectionLeg> first),
                Is.True);
            Assert.That(
                GlitchReplicationProtection.TryScalePlanSlice(plan, 1.0, 1, 1, out List<GlitchScaledProtectionLeg> duplicate),
                Is.True);
            Assert.That(duplicate.Select(leg => leg.SourceToken), Is.EqualTo(first.Select(leg => leg.SourceToken)));
            Assert.That(plan.Legs.Select(leg => leg.MasterQuantity), Is.EqualTo(new[] { 1, 1 }));
        }

        [Test]
        public void RecoveryMetadataRetainsRatioTwoAndSecondTrancheOffset()
        {
            const string signal =
                "GLT-COPY-E-acct01-entry001-R4000000000000000-O00000002";

            Assert.That(
                GlitchCopyEngine.TryReadFollowerAllocationMetadata(signal, out double ratio, out int offset),
                Is.True);
            Assert.That(ratio, Is.EqualTo(2.0));
            Assert.That(offset, Is.EqualTo(2));

            GlitchReplicationProtectionPlan plan = Plan(1, 1);
            Assert.That(
                GlitchReplicationProtection.TryScalePlanSlice(plan, ratio, offset, 2, out List<GlitchScaledProtectionLeg> scaled),
                Is.True);
            Assert.That(scaled.Select(leg => leg.SourceToken), Is.EqualTo(new[] { "leg2" }));
            Assert.That(scaled.Sum(leg => leg.Quantity), Is.EqualTo(2));
        }

        [Test]
        public void RecoveryMetadataDuplicateIsDeterministicAndLegacySignalIsAmbiguous()
        {
            const string signal =
                "GLT-COPY-E-acct01-entry001-R4000000000000000-O00000002";

            Assert.That(
                GlitchCopyEngine.TryReadFollowerAllocationMetadata(signal, out double firstRatio, out int firstOffset),
                Is.True);
            Assert.That(
                GlitchCopyEngine.TryReadFollowerAllocationMetadata(signal, out double duplicateRatio, out int duplicateOffset),
                Is.True);
            Assert.That(duplicateRatio, Is.EqualTo(firstRatio));
            Assert.That(duplicateOffset, Is.EqualTo(firstOffset));
            Assert.That(
                GlitchCopyEngine.TryReadFollowerAllocationMetadata(
                    "GLT-COPY-E-acct01-entry001",
                    out _,
                    out _),
                Is.False);
        }

        private static GlitchReplicationProtectionPlan Plan(params int[] quantities)
        {
            var plan = new GlitchReplicationProtectionPlan
            {
                MasterQuantity = quantities.Sum(),
                IsLong = true,
                TickSize = 0.25
            };
            for (int i = 0; i < quantities.Length; i++)
            {
                plan.Legs.Add(new GlitchReplicationProtectionLeg
                {
                    MasterQuantity = quantities[i],
                    StopPrice = 100 - i,
                    TargetPrice = 101 + i,
                    SourceToken = "leg" + (i + 1)
                });
            }
            return plan;
        }
    }

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

        [Test]
        public void IncrementalPartialFillsAggregateExactlyOnceWithVwapAndPnl()
        {
            DateTime start = new DateTime(2026, 8, 3, 11, 0, 0, DateTimeKind.Utc);
            var accumulator = new GlitchTradeInsightsService.ExecutionAccumulator();
            var entries = new List<GlitchTradeInsightsService.TradeJournalEvent>
            {
                Execution(start, "Buy", 3, 100.00, "GLT-COPY-E-trade-1", "SYNC", "entry-1", 3.00, "Replication"),
                Execution(start.AddMilliseconds(20), "Buy", 2, 102.00, "GLT-COPY-E-trade-1", "SYNC", "entry-2", 2.00, "Replication")
            };

            Assert.That(accumulator.Process(entries, entries), Is.Empty);

            var allExits = new List<GlitchTradeInsightsService.TradeJournalEvent>();
            GlitchTradeInsightsService.TradeRoundTrip completed = null;
            for (int index = 0; index < 5; index++)
            {
                GlitchTradeInsightsService.TradeJournalEvent exit = Execution(
                    start.AddMinutes(1).AddMilliseconds(index * 20),
                    "Sell",
                    1,
                    108.00 + index,
                    "GLT-COPY-S-trade-1-" + (index + 1),
                    "SL",
                    "exit-" + (index + 1),
                    1.00,
                    "Replication");
                allExits.Add(exit);
                IReadOnlyList<GlitchTradeInsightsService.TradeRoundTrip> closed =
                    accumulator.Process(new[] { exit }, entries.Concat(allExits).ToList());
                if (index < 4)
                    Assert.That(closed, Is.Empty);
                else
                {
                    Assert.That(closed, Has.Count.EqualTo(1));
                    completed = closed[0];
                }
            }

            Assert.That(completed, Is.Not.Null);
            Assert.That(completed.Contracts, Is.EqualTo(5).Within(0.000001));
            Assert.That(completed.EntryPrice, Is.EqualTo(100.8).Within(0.000001));
            Assert.That(completed.ExitPrice, Is.EqualTo(110.0).Within(0.000001));
            Assert.That(completed.PnlPoints, Is.EqualTo(46.0).Within(0.000001));
            Assert.That(completed.CommissionTotal, Is.EqualTo(10.0).Within(0.000001));
            Assert.That(completed.TradeSource, Is.EqualTo("Replication"));
            Assert.That(completed.EntrySignal, Is.EqualTo("GLT-COPY-E-trade-1"));

            Assert.That(
                accumulator.Process(new[] { allExits[4] }, entries.Concat(allExits).ToList()),
                Is.Empty,
                "A repeated native execution id must not emit a duplicate completion row.");
        }

        [Test]
        public void ScaleOutThenScaleInIncludesEveryEntryAndExitFragment()
        {
            DateTime start = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
            var events = new List<GlitchTradeInsightsService.TradeJournalEvent>
            {
                Execution(start, "Buy", 2, 100.00, "Entry", null, "entry-1", 0, "Manual"),
                Execution(start.AddMinutes(1), "Sell", 1, 110.00, "Close", "EXIT", "exit-1", 0, "Manual"),
                Execution(start.AddMinutes(2), "Buy", 1, 120.00, "Entry", null, "entry-2", 0, "Manual"),
                Execution(start.AddMinutes(3), "Sell", 2, 130.00, "Close", "EXIT", "exit-2", 0, "Manual")
            };

            GlitchTradeInsightsService.TradeInsightsSnapshot snapshot =
                new GlitchTradeInsightsService().BuildSnapshot(
                    events,
                    new List<GlitchTradeInsightsService.TradeWarningEvent>(),
                    start.AddMinutes(4));

            Assert.That(snapshot.ClosedTrades, Has.Count.EqualTo(1));
            Assert.That(snapshot.ClosedTrades[0].Contracts, Is.EqualTo(3).Within(0.000001));
            Assert.That(snapshot.ClosedTrades[0].EntryPrice, Is.EqualTo(106.6666666667).Within(0.000001));
            Assert.That(snapshot.ClosedTrades[0].ExitPrice, Is.EqualTo(123.3333333333).Within(0.000001));
            Assert.That(snapshot.ClosedTrades[0].PnlPoints, Is.EqualTo(50).Within(0.000001));
            Assert.That(snapshot.ClosedTrades[0].TradeSource, Is.EqualTo("Manual"));
        }

        [Test]
        public void ManualThenAiAdditionClosesAsDistinctFifoLots()
        {
            DateTime start = new DateTime(2026, 8, 3, 14, 0, 0, DateTimeKind.Utc);
            GlitchTradeInsightsService.TradeInsightsSnapshot snapshot = Snapshot(start,
                Execution(start, "Buy", 1, 100, "Entry", null, "m-1", 1, "Manual", "manual-order"),
                Execution(start.AddSeconds(1), "Buy", 2, 110, "GLT-AI-E-intent-1", "ENTRY", "a-1", 2, "Strategy", "ai-order"),
                Execution(start.AddMinutes(1), "Sell", 3, 120, "Close", "EXIT", "x-1", 3, "Manual"));

            Assert.That(snapshot.ClosedTrades, Has.Count.EqualTo(2));
            GlitchTradeInsightsService.TradeRoundTrip manual = snapshot.ClosedTrades.Single(trade => trade.EntryOrderIdentity == "manual-order");
            GlitchTradeInsightsService.TradeRoundTrip ai = snapshot.ClosedTrades.Single(trade => trade.EntryOrderIdentity == "ai-order");
            Assert.That(manual.TradeSource, Is.EqualTo("Manual"));
            Assert.That(manual.PnlPoints, Is.EqualTo(20).Within(0.000001));
            Assert.That(manual.CommissionTotal, Is.EqualTo(2).Within(0.000001));
            Assert.That(ai.TradeSource, Is.EqualTo("Strategy"));
            Assert.That(ai.PnlPoints, Is.EqualTo(20).Within(0.000001));
            Assert.That(ai.CommissionTotal, Is.EqualTo(4).Within(0.000001));
        }

        [Test]
        public void AiThenManualAdditionClosesAsDistinctFifoLots()
        {
            DateTime start = new DateTime(2026, 8, 3, 14, 30, 0, DateTimeKind.Utc);
            GlitchTradeInsightsService.TradeInsightsSnapshot snapshot = Snapshot(start,
                Execution(start, "Buy", 1, 100, "GLT-AI-E-intent-2", "ENTRY", "a-2", 0, "Strategy", "ai-order-2"),
                Execution(start.AddSeconds(1), "Buy", 1, 110, "Entry", null, "m-2", 0, "Manual", "manual-order-2"),
                Execution(start.AddMinutes(1), "Sell", 2, 120, "Close", "EXIT", "x-2", 0, "Manual"));

            Assert.That(snapshot.ClosedTrades, Has.Count.EqualTo(2));
            Assert.That(snapshot.ClosedTrades.Single(trade => trade.EntryOrderIdentity == "ai-order-2").PnlPoints, Is.EqualTo(20).Within(0.000001));
            Assert.That(snapshot.ClosedTrades.Single(trade => trade.EntryOrderIdentity == "manual-order-2").PnlPoints, Is.EqualTo(10).Within(0.000001));
        }

        [Test]
        public void DistinctAiIntentsWithoutOrderIdentityRemainDistinct()
        {
            DateTime start = new DateTime(2026, 8, 3, 15, 0, 0, DateTimeKind.Utc);
            GlitchTradeInsightsService.TradeInsightsSnapshot snapshot = Snapshot(start,
                Execution(start, "Buy", 1, 100, "GLT-AI-E-intent-A", "ENTRY", "a-3", 0, "Strategy"),
                Execution(start.AddSeconds(1), "Buy", 1, 105, "GLT-AI-E-intent-B", "ENTRY", "a-4", 0, "Strategy"),
                Execution(start.AddMinutes(1), "Sell", 2, 110, "GLT-AI-X", "EXIT", "x-3", 0, "Strategy"));

            Assert.That(snapshot.ClosedTrades, Has.Count.EqualTo(2));
            Assert.That(snapshot.ClosedTrades.Select(trade => trade.EntrySignal), Is.EquivalentTo(new[]
            {
                "GLT-AI-E-intent-A",
                "GLT-AI-E-intent-B"
            }));
        }

        [Test]
        public void SameNativeOrderPartialFillsAggregateIntoOneEpisode()
        {
            DateTime start = new DateTime(2026, 8, 3, 15, 30, 0, DateTimeKind.Utc);
            GlitchTradeInsightsService.TradeInsightsSnapshot snapshot = Snapshot(start,
                Execution(start, "Buy", 1, 100, "Entry", null, "p-1", 1, "Manual", "partial-order"),
                Execution(start.AddMilliseconds(20), "Buy", 1, 102, "Entry", null, "p-2", 1, "Manual", "partial-order"),
                Execution(start.AddMinutes(1), "Sell", 2, 110, "Close", "EXIT", "p-x", 2, "Manual"));

            Assert.That(snapshot.ClosedTrades, Has.Count.EqualTo(1));
            Assert.That(snapshot.ClosedTrades[0].Contracts, Is.EqualTo(2).Within(0.000001));
            Assert.That(snapshot.ClosedTrades[0].EntryPrice, Is.EqualTo(101).Within(0.000001));
            Assert.That(snapshot.ClosedTrades[0].EntryOrderIdentity, Is.EqualTo("partial-order"));
        }

        [Test]
        public void TradeIdIsStableAcrossAggregateCorrections()
        {
            DateTime entry = new DateTime(2026, 8, 3, 16, 0, 0, DateTimeKind.Utc);
            var partial = new GlitchTradeInsightsService.TradeRoundTrip
            {
                AccountName = "Sim101",
                Instrument = "MNQ",
                EntryUtc = entry,
                ExitUtc = entry.AddMinutes(1),
                IsLong = true,
                Contracts = 1,
                EntryPrice = 100,
                ExitPrice = 110,
                TradeSource = "Manual",
                EntrySignal = "Entry",
                EntryOrderIdentity = "stable-order"
            };
            var corrected = new GlitchTradeInsightsService.TradeRoundTrip
            {
                AccountName = "Sim101",
                Instrument = "MNQ",
                EntryUtc = entry.AddMilliseconds(20),
                ExitUtc = entry.AddMinutes(2),
                IsLong = true,
                Contracts = 3,
                EntryPrice = 102,
                ExitPrice = 108,
                TradeSource = "Manual",
                EntrySignal = "Entry",
                EntryOrderIdentity = "stable-order"
            };

            Assert.That(GlitchTradeInsightsService.BuildTradeId(corrected), Is.EqualTo(GlitchTradeInsightsService.BuildTradeId(partial)));
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
            double commission = 0,
            string source = "Strategy",
            string orderIdentity = "")
        {
            string tagToken = string.IsNullOrWhiteSpace(tag) ? string.Empty : " [TAG:" + tag + "]";
            string commissionToken = commission == 0
                ? string.Empty
                : " [COMM:" + commission.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture) + "]";
            string orderToken = string.IsNullOrWhiteSpace(orderIdentity) ? string.Empty : " [OID:" + orderIdentity + "]";
            return new GlitchTradeInsightsService.TradeJournalEvent
            {
                UtcTime = utc,
                AccountName = "Sim101",
                Category = "Execution",
                Message = "Exec " + action + " " + quantity + " MNQ @ " +
                          price.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture) +
                          " (" + signal + ") [SRC:" + source + "]" + tagToken + commissionToken + " [EID:" + executionId + "]" + orderToken
            };
        }
    }
}
#endif
