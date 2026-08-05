using System;
using System.IO;
using System.Linq;
using Glitch.Core;
using Glitch.Infrastructure;

namespace NinjaTrader.Core
{
    public static class Globals
    {
        public static string UserDataDir { get; set; }
    }
}

internal static class GlitchJournalHarness
{
    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static int Main()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "GlitchJournalHarness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        NinjaTrader.Core.Globals.UserDataDir = root;
        try
        {
            var journal = new GlitchOperationJournal();
            var position = new PositionObserved("Master", "MNQ 09-26", 0);
            var input = new HermesEntryRequested(
                "intent-1", "Master", "MNQ 09-26", 2, 20000m, 19990m,
                new[]
                {
                    new HermesTarget(1, 19989m, 20010m),
                    new HermesTarget(1, 19988m, 20020m)
                },
                "RAW-CONTENT-FINGERPRINT",
                "pending",
                "intent_dispatched",
                "ENTER_LONG");
            var noAction = new HermesNoActionRequested(
                "intent-2",
                "Master",
                "MNQ",
                "NOTHING",
                "RAW-NO-ACTION-FINGERPRINT",
                "executed",
                "no_native_action_requested",
                "NOTHING");
            Assert(journal.TryAppendInput(position, "test", out string positionError), positionError);
            Assert(journal.TryAppendInput(input, "test", out string inputError), inputError);
            Assert(journal.TryAppendInput(
                noAction, "test", out string noActionError), noActionError);
            var lifecycle = new ExecutionLifecycleObserved(
                GlitchNativeOperation.Update,
                "execution-1",
                "Master",
                "MNQ 09-26",
                "native-order-1",
                2,
                20001m,
                true,
                string.Empty,
                "command-1");
            Assert(journal.TryAppendInput(
                lifecycle, "test", out string lifecycleError), lifecycleError);
            var routeConfiguration = new RouteConfigurationChanged(
                true,
                new[]
                {
                    new RouteConfigurationItem(
                        "route-1", "Master", "Follower", 0.5m, true)
                });
            Assert(journal.TryAppendInput(
                routeConfiguration, "test", out string routeError), routeError);

            var originalEngine = new GlitchEngine();
            originalEngine.Handle(position);
            SubmitMarketCommand marketCommand = originalEngine.Handle(input)
                .OfType<SubmitMarketCommand>().Single();
            Assert(journal.TryAppend(
                marketCommand, "accepted", "test", out string acceptedError), acceptedError);
            Assert(journal.TryAppend(
                marketCommand, "native_request_started", "test", out string startedError), startedError);

            var command = new SubmitProtectionCommand(
                "G12345678901234567890",
                "Master",
                "MNQ 09-26",
                2,
                19991m,
                new[]
                {
                    new ProtectionTarget("L111111111111111", 1, 19991m, 20011m),
                    new ProtectionTarget("L222222222222222", 1, 19992m, 20022m)
                },
                "parent",
                true,
                20001m,
                "route-1",
                "exposure-1");

            Assert(journal.TryLoad(out var records, out string loadError), loadError);
            HermesEntryRequested loadedInput = records.Select(value => value.Input)
                .OfType<HermesEntryRequested>().Single();
            HermesNoActionRequested loadedNoAction = records.Select(value => value.Input)
                .OfType<HermesNoActionRequested>().Single();
            RouteConfigurationChanged loadedRoutes = records.Select(value => value.Input)
                .OfType<RouteConfigurationChanged>().Single();
            ExecutionLifecycleObserved loadedLifecycle = records.Select(value => value.Input)
                .OfType<ExecutionLifecycleObserved>().Single();
            Assert(loadedInput.Targets.Count == 2
                && loadedInput.Targets[1].StopPrice == 19988m
                && loadedInput.ContentFingerprint == "RAW-CONTENT-FINGERPRINT"
                && loadedInput.ReceiptCode == "intent_dispatched",
                "Hermes input did not round-trip exactly");
            Assert(loadedNoAction.ContentFingerprint == "RAW-NO-ACTION-FINGERPRINT"
                && loadedNoAction.ReceiptStatus == "executed"
                && loadedNoAction.ReceiptCode == "no_native_action_requested",
                "Hermes no-action receipt did not round-trip exactly");
            Assert(loadedRoutes.ReplicationEnabled
                && loadedRoutes.Routes.Single().Ratio == 0.5m
                && loadedRoutes.SynchronizeRouteIds.Count == 0,
                "atomic route configuration did not round-trip exactly");
            Assert(loadedLifecycle.Operation == GlitchNativeOperation.Update
                && loadedLifecycle.NativeOrderKey == "native-order-1"
                && loadedLifecycle.SignedQuantity == 2,
                "execution lifecycle evidence did not round-trip exactly");
            SubmitMarketCommand loadedMarket = records.Select(value => value.Command)
                .OfType<SubmitMarketCommand>().Last();
            Assert(GlitchOperationJournal.Fingerprint(loadedMarket)
                    == GlitchOperationJournal.Fingerprint(marketCommand),
                "market command did not round-trip exactly");
            Assert(records.Last().Phase == "native_request_started",
                "latest durable request boundary was not preserved");

            var replayEngine = new GlitchEngine();
            SubmitMarketCommand replayed = records
                .Where(value => value.Input != null)
                .SelectMany(value => replayEngine.Handle(value.Input))
                .OfType<SubmitMarketCommand>()
                .Single();
            Assert(GlitchOperationJournal.Fingerprint(replayed)
                    == GlitchOperationJournal.Fingerprint(marketCommand),
                "accepted inputs did not replay to the same native command");

            var changed = new SubmitProtectionCommand(
                command.CommandId,
                command.AccountName,
                command.InstrumentName,
                command.SignedEntryQuantity,
                command.StopPrice,
                new[]
                {
                    command.Targets[0],
                    new ProtectionTarget("L222222222222222", 1, 19992m, 20023m)
                },
                command.ParentCorrelationId,
                command.PropagatesAsMasterExecution,
                command.EntryPrice,
                command.RouteId,
                command.ExposureId);
            Assert(GlitchOperationJournal.Fingerprint(command)
                != GlitchOperationJournal.Fingerprint(changed),
                "command fingerprint ignored nested protection geometry");

            string journalPath = Path.Combine(
                root, "glitch", "runtime", "operations.v3.jsonl");
            File.AppendAllText(journalPath, "not-json" + Environment.NewLine);
            Assert(!journal.TryLoad(out _, out string corruptError)
                    && !string.IsNullOrWhiteSpace(corruptError),
                "a corrupt journal was accepted as recoverable state");

            Console.WriteLine("Glitch journal harness passed.");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
