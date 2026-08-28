using System;
using System.Collections.Generic;
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
        try
        {
            return Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static int Run()
    {
        string existingRoot = Environment.GetEnvironmentVariable(
            "GLITCH_JOURNAL_VERIFY_ROOT");
        if (!string.IsNullOrWhiteSpace(existingRoot))
            return VerifyExistingJournal(existingRoot);
        string replayRoot = Environment.GetEnvironmentVariable(
            "GLITCH_JOURNAL_REPLAY_VERIFY_ROOT");
        if (!string.IsNullOrWhiteSpace(replayRoot))
            return VerifyReplay(replayRoot);
        string hostReplayRoot = Environment.GetEnvironmentVariable(
            "GLITCH_JOURNAL_HOST_REPLAY_VERIFY_ROOT");
        if (!string.IsNullOrWhiteSpace(hostReplayRoot))
            return VerifyHostReplay(hostReplayRoot);

        string root = Path.Combine(
            Path.GetTempPath(), "GlitchJournalHarness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        NinjaTrader.Core.Globals.UserDataDir = root;
        try
        {
            string runtimeRoot = Path.Combine(root, "glitch", "runtime");
            Directory.CreateDirectory(runtimeRoot);
            string journalPath = Path.Combine(runtimeRoot, "operations.v5.jsonl");
            string legacyJournalPath = Path.Combine(runtimeRoot, "operations.v4.jsonl");
            File.WriteAllText(legacyJournalPath, "not-json" + Environment.NewLine);

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
                "ENTER_LONG",
                19999m,
                20001m);
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
                "command-1",
                1.25m);
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
            var masterProtection = new MasterProtectionObserved(
                "Master",
                "MNQ 09-26",
                1,
                20000.125m,
                "manual-protection-1",
                new[] { new MasterProtectionLeg("UMANUAL00000001", 1, 19990m, 20020m) },
                0.25m);
            Assert(journal.TryAppendInput(
                masterProtection, "test", out string masterProtectionError),
                masterProtectionError);

            const string legacyProtectionRecord =
                "{\"schema\":\"glitch.operation.v5\",\"created_utc\":\"2026-08-07T00:45:54.5591122Z\",\"phase\":\"accepted\",\"command_id\":\"G3795A7A3BB188E9D9E52\",\"type\":\"SubmitProtectionCommand\",\"fingerprint\":\"4acc26aa34d0136cf463831830948e43ceaebb25b015d4ba52b9ecb6de9cf10a\",\"detail\":\"native\",\"command\":{\"type\":\"SubmitProtectionCommand\",\"command_id\":\"G3795A7A3BB188E9D9E52\",\"purpose\":\"Protection\",\"account\":\"Sim101\",\"instrument\":\"MNQ 09-26\",\"signed_entry\":1,\"entry_price\":29537.25,\"parent\":\"G4CC8863E8A279F23FA50\",\"route\":\"\",\"exposure\":\"HERMES|bb6e7f66-ba55-5bb3-8b98-9fadca213a2c|FILL|061a553845a048e79aa512558004c7f1\",\"propagates\":true,\"targets\":[{\"leg_id\":\"LBB090CDCE7CA8AC\",\"quantity\":1,\"stop\":29485.25,\"target\":29593.00}]}}";
            const string legacyMasterProtectionRecord =
                "{\"schema\":\"glitch.operation.v5\",\"created_utc\":\"2026-08-07T00:45:55.0000000Z\",\"phase\":\"input_accepted\",\"command_id\":\"\",\"type\":\"MasterProtectionObserved\",\"source\":\"legacy\",\"input\":{\"type\":\"MasterProtectionObserved\",\"account\":\"LegacyMaster\",\"instrument\":\"MNQ 09-26\",\"signed_quantity\":1,\"reference_price\":20000,\"revision_id\":\"legacy-manual-protection\",\"legs\":[{\"leg_id\":\"ULEGACY0000001\",\"quantity\":1,\"stop\":19990,\"target\":20020}]}}";
            File.AppendAllText(journalPath, legacyProtectionRecord + Environment.NewLine);
            File.AppendAllText(journalPath, legacyMasterProtectionRecord + Environment.NewLine);

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
            MasterProtectionObserved[] loadedMasterProtection = records
                .Select(value => value.Input)
                .OfType<MasterProtectionObserved>()
                .ToArray();
            Assert(loadedInput.Targets.Count == 2
                && loadedInput.Targets[1].StopPrice == 19988m
                && loadedInput.ContentFingerprint == "RAW-CONTENT-FINGERPRINT"
                && loadedInput.ReceiptCode == "intent_dispatched"
                && loadedInput.EntryRangeLow == 19999m
                && loadedInput.EntryRangeHigh == 20001m,
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
                && loadedLifecycle.SignedQuantity == 2
                && loadedLifecycle.Commission == 1.25m,
                "execution lifecycle evidence did not round-trip exactly");
            Assert(loadedMasterProtection.Single(value => value.AccountName == "Master").TickSize == 0.25m,
                "native tick size did not round-trip with manual protection evidence");
            Assert(loadedMasterProtection.Single(value => value.AccountName == "LegacyMaster").TickSize == 0,
                "legacy manual protection evidence did not default missing tick size compatibly");
            SubmitMarketCommand loadedMarket = records.Select(value => value.Command)
                .OfType<SubmitMarketCommand>().Last();
            SubmitProtectionCommand loadedLegacyProtection = records
                .Select(value => value.Command)
                .OfType<SubmitProtectionCommand>()
                .Single(value => value.CommandId == "G3795A7A3BB188E9D9E52");
            Assert(loadedLegacyProtection.HermesIntentId == string.Empty
                && loadedLegacyProtection.Targets.Single().Price == 29593m,
                "pre-hermes-intent protection record did not replay compatibly");
            Assert(GlitchOperationJournal.Fingerprint(loadedLegacyProtection)
                    == "4acc26aa34d0136cf463831830948e43ceaebb25b015d4ba52b9ecb6de9cf10a",
                "empty Hermes intent changed the legacy semantic fingerprint");
            Assert(GlitchOperationJournal.Fingerprint(loadedMarket)
                    == GlitchOperationJournal.Fingerprint(marketCommand),
                "market command did not round-trip exactly");
            Assert(loadedMarket.EntryRangeLow == 19999m
                    && loadedMarket.EntryRangeHigh == 20001m,
                "Hermes entry range did not reach the native command boundary");
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
            Assert(replayed.EntryRangeLow == 19999m
                    && replayed.EntryRangeHigh == 20001m,
                "Hermes entry range was not preserved by recovery replay");

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

            Assert(File.ReadAllText(legacyJournalPath) == "not-json" + Environment.NewLine,
                "the new journal epoch mutated legacy incident history");
            File.AppendAllText(journalPath, "not-json" + Environment.NewLine);
            Assert(!journal.TryLoad(out _, out string corruptError)
                    && !string.IsNullOrWhiteSpace(corruptError),
                "a corrupt journal was accepted as recoverable state");

            string tamperRoot = Path.Combine(root, "tamper");
            string tamperRuntimeRoot = Path.Combine(tamperRoot, "glitch", "runtime");
            Directory.CreateDirectory(tamperRuntimeRoot);
            File.WriteAllText(
                Path.Combine(tamperRuntimeRoot, "operations.v5.jsonl"),
                legacyProtectionRecord.Replace("\"target\":29593.00", "\"target\":29594.00")
                    + Environment.NewLine);
            NinjaTrader.Core.Globals.UserDataDir = tamperRoot;
            var tamperedJournal = new GlitchOperationJournal();
            Assert(!tamperedJournal.TryLoad(out _, out string tamperError)
                    && tamperError.Contains("journal_command_fingerprint_mismatch"),
                "tampered command payload bypassed journal fingerprint validation");

            Console.WriteLine("Glitch journal harness passed.");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static int VerifyExistingJournal(string root)
    {
        NinjaTrader.Core.Globals.UserDataDir = root;
        var journal = new GlitchOperationJournal();
        Assert(journal.TryLoad(out var records, out string error), error);
        var identities = new Dictionary<string, string>(StringComparer.Ordinal);
        int commandCount = 0;
        foreach (GlitchRecoveryRecord record in records.Where(value => value.Command != null))
        {
            commandCount++;
            string fingerprint = GlitchOperationJournal.Fingerprint(record.Command);
            if (identities.TryGetValue(record.Command.CommandId, out string prior))
                Assert(string.Equals(prior, fingerprint, StringComparison.Ordinal),
                    "journal_command_identity_conflict:" + record.Command.CommandId);
            identities[record.Command.CommandId] = fingerprint;
        }
        Console.WriteLine(
            "Existing Glitch journal passed: records=" + records.Count
            + " commands=" + commandCount
            + " identities=" + identities.Count + ".");
        return 0;
    }

    private static int VerifyReplay(string root)
    {
        NinjaTrader.Core.Globals.UserDataDir = root;
        var journal = new GlitchOperationJournal();
        Assert(journal.TryLoad(out var records, out string error), error);
        var engine = new GlitchEngine();
        var emitted = new Dictionary<string, GlitchCommand>(StringComparer.Ordinal);
        var identities = new Dictionary<string, string>(StringComparer.Ordinal);
        int index = 0;
        foreach (GlitchRecoveryRecord record in records)
        {
            index++;
            if (record.Input != null)
            {
                foreach (GlitchCommand command in engine.Handle(record.Input))
                {
                    if (emitted.TryGetValue(command.CommandId, out GlitchCommand priorEmission))
                        Assert(string.Equals(
                                GlitchOperationJournal.FingerprintForReplay(
                                    priorEmission, record.HermesIntentPresent),
                                GlitchOperationJournal.FingerprintForReplay(
                                    command, record.HermesIntentPresent),
                                StringComparison.Ordinal),
                            "replayed_command_content_conflict:" + command.CommandId
                            + "|record=" + index);
                    emitted[command.CommandId] = command;
                }
            }
            if (record.Command == null)
                continue;
            string commandFingerprint = GlitchOperationJournal.Fingerprint(record.Command);
            if (identities.TryGetValue(record.Command.CommandId, out string prior))
                Assert(string.Equals(prior, commandFingerprint, StringComparison.Ordinal),
                    "journal_command_identity_conflict:" + record.Command.CommandId
                    + "|record=" + index);
            identities[record.Command.CommandId] = commandFingerprint;
            if (emitted.TryGetValue(record.Command.CommandId, out GlitchCommand emittedCommand))
            {
                string emittedReplayFingerprint = GlitchOperationJournal.FingerprintForReplay(
                    emittedCommand, record.HermesIntentPresent);
                string recordedReplayFingerprint = GlitchOperationJournal.FingerprintForReplay(
                    record.Command, record.HermesIntentPresent);
                Assert(string.Equals(
                        emittedReplayFingerprint,
                        recordedReplayFingerprint,
                        StringComparison.Ordinal),
                    "replayed_command_content_conflict:" + record.Command.CommandId
                    + "|record=" + index
                    + "|wire_intent=" + record.HermesIntentPresent
                    + "|emitted_intent=" + (emittedCommand as SubmitProtectionCommand)?.HermesIntentId
                    + "|recorded_intent=" + (record.Command as SubmitProtectionCommand)?.HermesIntentId
                    + "|emitted_fp=" + emittedReplayFingerprint
                    + "|recorded_fp=" + recordedReplayFingerprint);
            }
        }
        Console.WriteLine(
            "Existing Glitch replay passed: records=" + records.Count
            + " emissions=" + emitted.Count
            + " identities=" + identities.Count + ".");
        return 0;
    }

    private static int VerifyHostReplay(string root)
    {
        NinjaTrader.Core.Globals.UserDataDir = root;
        var journal = new GlitchOperationJournal();
        Assert(journal.TryLoad(out var records, out string error), error);
        var engine = new GlitchEngine();
        var emitted = new Dictionary<string, GlitchCommand>(StringComparer.Ordinal);
        var journalCommands = new Dictionary<string, string>(StringComparer.Ordinal);
        var commandIdentities = new Dictionary<string, string>(StringComparer.Ordinal);
        int index = 0;
        foreach (GlitchRecoveryRecord record in records)
        {
            index++;
            if (record.Input != null)
            {
                foreach (GlitchCommand command in engine.Handle(record.Input))
                {
                    string fingerprint = GlitchOperationJournal.Fingerprint(command);
                    if (emitted.TryGetValue(command.CommandId, out GlitchCommand priorEmission))
                    {
                        if (!string.Equals(
                                GlitchOperationJournal.Fingerprint(priorEmission),
                                fingerprint,
                                StringComparison.Ordinal))
                        {
                            if (journalCommands.ContainsKey(command.CommandId))
                                continue;
                            throw new InvalidOperationException(
                                "replayed_command_identity_conflict:" + command.CommandId
                                + "|record=" + index);
                        }
                    }
                    if (journalCommands.TryGetValue(command.CommandId, out string journalFingerprint))
                    {
                        if (!string.Equals(journalFingerprint, fingerprint, StringComparison.Ordinal))
                            continue;
                    }
                    emitted[command.CommandId] = command;
                }
            }
            if (record.Command == null)
                continue;
            string commandFingerprint = GlitchOperationJournal.Fingerprint(record.Command);
            if (commandIdentities.TryGetValue(record.Command.CommandId, out string prior))
                Assert(string.Equals(prior, commandFingerprint, StringComparison.Ordinal),
                    "journal_command_identity_conflict:" + record.Command.CommandId
                    + "|record=" + index);
            commandIdentities[record.Command.CommandId] = commandFingerprint;
            if (emitted.TryGetValue(record.Command.CommandId, out GlitchCommand emittedCommand))
                Assert(string.Equals(
                        GlitchOperationJournal.FingerprintForReplay(
                            emittedCommand, record.HermesIntentPresent),
                        GlitchOperationJournal.FingerprintForReplay(
                            record.Command, record.HermesIntentPresent),
                        StringComparison.Ordinal),
                    "replayed_command_content_conflict:" + record.Command.CommandId
                    + "|record=" + index);
            journalCommands[record.Command.CommandId] = commandFingerprint;
        }
        GlitchCommand[] unjournaledPending = emitted.Values
            .Where(command => !journalCommands.ContainsKey(command.CommandId)
                && engine.IsCommandPending(command.CommandId))
            .ToArray();
        Assert(unjournaledPending.Length == 0,
            "recovery_would_resume_unjournaled_commands:"
            + string.Join(",", unjournaledPending.Select(command =>
                command.GetType().Name + "|" + command.CommandId)));
        Console.WriteLine(
            "Existing Glitch host replay passed: records=" + records.Count
            + " emissions=" + emitted.Count
            + " identities=" + commandIdentities.Count
            + " unjournaled_pending=" + unjournaledPending.Length + ".");
        return 0;
    }

}
