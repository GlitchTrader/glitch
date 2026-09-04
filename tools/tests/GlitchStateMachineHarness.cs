using System;
using System.Collections.Generic;
using System.Linq;
using Glitch.Core;

internal static class GlitchStateMachineHarness
{
    private sealed class ProtectedBook
    {
        public GlitchEngine Engine;
        public SubmitProtectionCommand Protection;
        public string StopKey;
        public string TargetKey;
    }

    private sealed class ProtectedRouteBook
    {
        public GlitchEngine Engine;
        public SubmitProtectionCommand MasterProtection;
        public SubmitProtectionCommand FollowerProtection;
        public string MasterStopKey;
        public string MasterTargetKey;
        public string FollowerStopKey;
        public string FollowerTargetKey;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static NativeOrderObserved Order(
        string account,
        string key,
        string state,
        int quantity,
        int filled,
        string correlation,
        string role,
        string legId = null,
        decimal? stopPrice = null,
        decimal? targetPrice = null)
    {
        return new NativeOrderObserved(
            account,
            "MNQ 09-26",
            key,
            key,
            key,
            state,
            quantity,
            filled,
            role.StartsWith("S") ? stopPrice ?? 19990m : null,
            role.StartsWith("T") ? targetPrice ?? 20020m : null,
            "NoError",
            string.Empty,
            "OCO-" + correlation,
            correlation,
            role,
            legId);
    }

    private static ExecutionObserved Execution(
        string id,
        string account,
        int signedQuantity,
        decimal price,
        int openingQuantity,
        int postPosition,
        string nativeOrderKey,
        string commandId,
        string protectionCommandId,
        GlitchExecutionOrigin origin)
    {
        return new ExecutionObserved(
            id,
            account,
            "MNQ 09-26",
            signedQuantity,
            price,
            origin,
            commandId,
            protectionCommandId,
            nativeOrderKey,
            false);
    }

    private static ExecutionLifecycleObserved ExecutionLifecycle(
        GlitchNativeOperation operation,
        string id,
        string account,
        int signedQuantity,
        decimal price,
        string nativeOrderKey,
        bool representable = true)
    {
        return new ExecutionLifecycleObserved(
            operation,
            id,
            account,
            "MNQ 09-26",
            nativeOrderKey,
            signedQuantity,
            price,
            representable,
            representable ? string.Empty : "execution_removed");
    }

    private static IReadOnlyList<GlitchCommand> ObserveExecution(
        GlitchEngine engine,
        ExecutionObserved execution)
    {
        var commands = new List<GlitchCommand>();
        commands.AddRange(engine.Handle(ExecutionLifecycle(
            GlitchNativeOperation.Add,
            execution.ExecutionId,
            execution.AccountName,
            execution.SignedQuantity,
            execution.Price,
            execution.NativeOrderKey)));
        commands.AddRange(engine.Handle(execution));
        return commands;
    }

    private static IReadOnlyList<GlitchCommand> CompleteTrade(
        GlitchEngine engine,
        SubmitMarketCommand command,
        ref int signedPosition,
        string identity,
        GlitchExecutionOrigin origin)
    {
        Assert(command.ExpectedSignedPosition == signedPosition,
            "native trade plan did not use the latest follower position");
        string orderKey = "order-" + identity;
        var commands = new List<GlitchCommand>();
        commands.AddRange(engine.Handle(Order(
            command.AccountName, orderKey, "Working",
            Math.Abs(command.SignedQuantity), 0, command.CommandId, "M")));
        int closingQuantity = signedPosition != 0
            && Math.Sign(signedPosition) != Math.Sign(command.SignedQuantity)
            ? Math.Min(Math.Abs(signedPosition), Math.Abs(command.SignedQuantity))
            : 0;
        int openingQuantity = Math.Abs(command.SignedQuantity) - closingQuantity;
        int postPosition = signedPosition + command.SignedQuantity;
        commands.AddRange(ObserveExecution(engine, Execution(
            "execution-" + identity,
            command.AccountName,
            command.SignedQuantity,
            20000m,
            openingQuantity,
            postPosition,
            orderKey,
            command.CommandId,
            null,
            origin)));
        commands.AddRange(engine.Handle(new PositionObserved(
            command.AccountName,
            command.InstrumentName,
            postPosition)));
        commands.AddRange(engine.Handle(Order(
            command.AccountName, orderKey, "Filled",
            Math.Abs(command.SignedQuantity), Math.Abs(command.SignedQuantity),
            command.CommandId, "M")));
        signedPosition = postPosition;
        return commands;
    }

    private static int[] RunAllocation(decimal ratio, params int[] masterFills)
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "allocation", ratio);
        int masterPosition = 0;
        int followerPosition = 0;
        int sequence = 0;
        var allocations = new List<int>();
        foreach (int signedFill in masterFills)
        {
            int closingQuantity = masterPosition != 0
                && Math.Sign(masterPosition) != Math.Sign(signedFill)
                ? Math.Min(Math.Abs(masterPosition), Math.Abs(signedFill))
                : 0;
            int openingQuantity = Math.Abs(signedFill) - closingQuantity;
            masterPosition += signedFill;
            IReadOnlyList<GlitchCommand> emitted = ObserveExecution(engine, Execution(
                "master-allocation-" + (++sequence),
                "Master",
                signedFill,
                20000m + sequence,
                openingQuantity,
                masterPosition,
                "master-order-" + sequence,
                null,
                null,
                GlitchExecutionOrigin.External));
            var pending = new Queue<SubmitMarketCommand>(
                emitted.OfType<SubmitMarketCommand>());
            while (pending.Count > 0)
            {
                SubmitMarketCommand command = pending.Dequeue();
                allocations.Add(command.SignedQuantity);
                foreach (SubmitMarketCommand released in CompleteTrade(
                    engine,
                    command,
                    ref followerPosition,
                    "allocation-" + sequence + "-" + allocations.Count,
                    GlitchExecutionOrigin.GlitchReplication)
                    .OfType<SubmitMarketCommand>())
                    pending.Enqueue(released);
            }
        }
        return allocations.ToArray();
    }

    private static IReadOnlyList<GlitchCommand> ConfigureRoute(
        GlitchEngine engine,
        string routeId,
        decimal ratio,
        bool synchronize = false)
    {
        return engine.Handle(new RouteConfigurationChanged(
            true,
            new[]
            {
                new RouteConfigurationItem(
                    routeId, "Master", "Follower", ratio, true)
            },
            synchronize ? new[] { routeId } : null));
    }

    private static void MarkPositionUnknown(
        GlitchEngine engine,
        string account,
        string instrument,
        string identity)
    {
        engine.Handle(new ExecutionObserved(
            identity,
            account,
            instrument,
            1,
            1m,
            GlitchExecutionOrigin.External,
            null,
            null,
            "baseline-" + identity,
            true));
    }

    private static ProtectedBook CreateProtectedLong()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        SubmitMarketCommand entry = engine.Handle(new HermesEntryRequested(
            "entry-1",
            "Master",
            "MNQ 09-26",
            1,
            20000m,
            19990m,
            new[] { new HermesTarget(1, 20020m) }))
            .OfType<SubmitMarketCommand>()
            .Single();

        const string entryKey = "entry-order";
        engine.Handle(Order("Master", entryKey, "Working", 1, 0, entry.CommandId, "M"));
        SubmitProtectionCommand protection = engine.Handle(Execution(
            "entry-fill",
            "Master",
            1,
            20001m,
            1,
            1,
            entryKey,
            entry.CommandId,
            null,
            GlitchExecutionOrigin.HermesMaster))
            .OfType<SubmitProtectionCommand>()
            .Single();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 1));
        engine.Handle(Order("Master", entryKey, "Filled", 1, 1, entry.CommandId, "M"));

        const string stopKey = "stop-order";
        const string targetKey = "target-order";
        engine.Handle(Order("Master", stopKey, "Working", 1, 0, protection.CommandId, "S0"));
        engine.Handle(Order("Master", targetKey, "Working", 1, 0, protection.CommandId, "T0"));
        Assert(engine.GetOperationPhase("HERMES|entry-1") == GlitchOperationPhase.Completed,
            "protected entry did not reach Completed after exact native evidence");
        return new ProtectedBook
        {
            Engine = engine,
            Protection = protection,
            StopKey = stopKey,
            TargetKey = targetKey
        };
    }

    private static ProtectedRouteBook CreateProtectedRouteLong()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "race-route", 1m);

        SubmitMarketCommand masterEntry = engine.Handle(new HermesEntryRequested(
            "race-entry", "Master", "MNQ 09-26", 1, 20000m, 19990m,
            new[] { new HermesTarget(1, 20020m) }))
            .OfType<SubmitMarketCommand>().Single();
        engine.Handle(Order("Master", "race-master-entry", "Working", 1, 0,
            masterEntry.CommandId, "M"));
        GlitchCommand[] masterFillCommands = ObserveExecution(engine, Execution(
            "race-master-entry-fill", "Master", 1, 20001m, 1, 1,
            "race-master-entry", masterEntry.CommandId, null,
            GlitchExecutionOrigin.HermesMaster)).ToArray();
        SubmitProtectionCommand masterProtection = masterFillCommands
            .OfType<SubmitProtectionCommand>().Single();
        SubmitMarketCommand followerEntry = masterFillCommands
            .OfType<SubmitMarketCommand>().Single();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 1));
        engine.Handle(Order("Master", "race-master-entry", "Filled", 1, 1,
            masterEntry.CommandId, "M"));
        string legId = masterProtection.Targets.Single().LegId;
        const string masterStop = "race-master-stop";
        const string masterTarget = "race-master-target";
        engine.Handle(Order("Master", masterStop, "Working", 1, 0,
            masterProtection.CommandId, "S0", legId, 19991m));
        engine.Handle(Order("Master", masterTarget, "Working", 1, 0,
            masterProtection.CommandId, "T0", legId, null, 20021m));

        engine.Handle(Order("Follower", "race-follower-entry", "Working", 1, 0,
            followerEntry.CommandId, "M"));
        SubmitProtectionCommand followerProtection = ObserveExecution(engine, Execution(
            "race-follower-entry-fill", "Follower", 1, 20003m, 1, 1,
            "race-follower-entry", followerEntry.CommandId, null,
            GlitchExecutionOrigin.GlitchReplication))
            .OfType<SubmitProtectionCommand>().Single();
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 1));
        engine.Handle(Order("Follower", "race-follower-entry", "Filled", 1, 1,
            followerEntry.CommandId, "M"));
        const string followerStop = "race-follower-stop";
        const string followerTarget = "race-follower-target";
        engine.Handle(Order("Follower", followerStop, "Working", 1, 0,
            followerProtection.CommandId, "S0", legId, 19993m));
        engine.Handle(Order("Follower", followerTarget, "Working", 1, 0,
            followerProtection.CommandId, "T0", legId, null, 20023m));
        Assert(engine.GetOperationPhase("HERMES|race-entry")
                == GlitchOperationPhase.Completed,
            "race fixture master entry was not protected");

        return new ProtectedRouteBook
        {
            Engine = engine,
            MasterProtection = masterProtection,
            FollowerProtection = followerProtection,
            MasterStopKey = masterStop,
            MasterTargetKey = masterTarget,
            FollowerStopKey = followerStop,
            FollowerTargetKey = followerTarget
        };
    }

    private static void TestReversalIsSequential()
    {
        ProtectedBook setup = CreateProtectedLong();
        GlitchEngine engine = setup.Engine;
        CancelProtectionCommand cancel = engine.Handle(new HermesEntryRequested(
            "reverse-1",
            "Master",
            "MNQ 09-26",
            -2,
            20000m,
            20010m,
            new[] { new HermesTarget(2, 19980m) }))
            .OfType<CancelProtectionCommand>()
            .Single();
        Assert(engine.Handle(Order(
            "Master", setup.StopKey, "Cancelled", 1, 0,
            setup.Protection.CommandId, "S0")).OfType<SubmitMarketCommand>().Count() == 0,
            "reversal traded before every protection child was terminal");
        SubmitMarketCommand close = engine.Handle(Order(
            "Master", setup.TargetKey, "Cancelled", 1, 0,
            setup.Protection.CommandId, "T0"))
            .OfType<SubmitMarketCommand>()
            .Single();
        Assert(close.SignedQuantity == -1 && close.ExpectedSignedPosition == 1,
            "reversal did not emit only the close-to-flat step");

        int[][] closeFactPermutations =
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
            new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 }
        };
        foreach (int[] permutationOrder in closeFactPermutations)
        {
            ProtectedBook permutation = CreateProtectedLong();
            GlitchEngine candidate = permutation.Engine;
            candidate.Handle(new HermesEntryRequested(
                "reverse-1", "Master", "MNQ 09-26", -2, 20000m, 20010m,
                new[] { new HermesTarget(2, 19980m) }));
            candidate.Handle(Order("Master", permutation.StopKey, "Cancelled", 1, 0,
                permutation.Protection.CommandId, "S0"));
            SubmitMarketCommand candidateClose = candidate.Handle(Order(
                "Master", permutation.TargetKey, "Cancelled", 1, 0,
                permutation.Protection.CommandId, "T0"))
                .OfType<SubmitMarketCommand>()
                .Single();
            GlitchInput orderFact = Order(
                "Master", "close-order", "Filled", 1, 1, candidateClose.CommandId, "M");
            GlitchInput executionFact = Execution(
                "close-fill", "Master", -1, 19999m, 0, 0,
                "close-order", candidateClose.CommandId, null,
                GlitchExecutionOrigin.HermesMaster);
            GlitchInput positionFact = new PositionObserved(
                "Master", "MNQ 09-26", 0);
            GlitchInput[] facts = { orderFact, executionFact, positionFact };
            var emitted = new List<SubmitMarketCommand>();
            for (int index = 0; index < permutationOrder.Length; index++)
            {
                SubmitMarketCommand[] current = candidate.Handle(
                        facts[permutationOrder[index]])
                    .OfType<SubmitMarketCommand>()
                    .ToArray();
                if (index < permutationOrder.Length - 1)
                    Assert(current.Length == 0,
                        "reversal opened before order, execution, and position facts were complete");
                emitted.AddRange(current);
            }
            SubmitMarketCommand open = emitted.Single();
            Assert(open.SignedQuantity == -1 && open.ExpectedSignedPosition == 0,
                "reversal did not submit the opening remainder after close finality");
        }
        Assert(!string.IsNullOrWhiteSpace(cancel.CommandId), "cancel command identity was absent");
    }

    private static void TestProtectionFillCancellationPermutations()
    {
        int[][] permutations =
        {
            new[] { 0, 1, 2, 3 }, new[] { 0, 2, 1, 3 },
            new[] { 1, 0, 3, 2 }, new[] { 1, 3, 0, 2 },
            new[] { 2, 0, 3, 1 }, new[] { 2, 3, 1, 0 },
            new[] { 3, 0, 1, 2 }, new[] { 3, 2, 1, 0 }
        };
        foreach (int[] permutation in permutations)
        {
            ProtectedBook setup = CreateProtectedLong();
            GlitchEngine engine = setup.Engine;
            engine.Handle(new HermesEntryRequested(
                "reverse-race", "Master", "MNQ 09-26", -2, 20000m, 20010m,
                new[] { new HermesTarget(2, 19980m) }));

            Func<IReadOnlyList<GlitchCommand>>[] facts =
            {
                () => engine.Handle(Order("Master", setup.StopKey, "Filled", 1, 1,
                    setup.Protection.CommandId, "S0")),
                () => engine.Handle(Order("Master", setup.TargetKey, "Cancelled", 1, 0,
                    setup.Protection.CommandId, "T0")),
                () => ObserveExecution(engine, Execution(
                    "protective-fill", "Master", -1, 19990m, 0, 0,
                    setup.StopKey, setup.Protection.CommandId,
                    setup.Protection.CommandId,
                    GlitchExecutionOrigin.HermesMasterProtection)),
                () => engine.Handle(new PositionObserved(
                    "Master", "MNQ 09-26", 0))
            };
            var markets = new List<SubmitMarketCommand>();
            for (int i = 0; i < permutation.Length; i++)
            {
                SubmitMarketCommand[] emitted = facts[permutation[i]]()
                    .OfType<SubmitMarketCommand>()
                    .ToArray();
                if (i < permutation.Length - 1)
                    Assert(emitted.Length == 0,
                        "protection race released before its complete fact set");
                markets.AddRange(emitted);
            }
            if (markets.Count == 0)
            {
                markets.AddRange(engine.Handle(new PositionObserved(
                        "Master", "MNQ 09-26", 0))
                    .OfType<SubmitMarketCommand>());
            }
            Assert(markets.Count == 1
                && markets[0].SignedQuantity == -2
                && markets[0].ExpectedSignedPosition == 0,
                "protection race permutation changed the resulting native step");
        }
    }

    private static void TestExecutionLifecycleRevisionIsEvidenceOnly()
    {
        ProtectedBook setup = CreateProtectedLong();
        GlitchEngine engine = setup.Engine;
        Assert(engine.Handle(ExecutionLifecycle(
            GlitchNativeOperation.Add,
            "amended-fill",
            "Master",
            -1,
            19990m,
            setup.StopKey)).Count == 0,
            "execution Add lifecycle created a trade");
        Assert(engine.Handle(ExecutionLifecycle(
            GlitchNativeOperation.Update,
            "amended-fill",
            "Master",
            -2,
            19990m,
            setup.StopKey)).Count == 0,
            "execution Update lifecycle created a trade");
        Assert(engine.Handle(ExecutionLifecycle(
            GlitchNativeOperation.Remove,
            "amended-fill",
            "Master",
            0,
            0m,
            string.Empty,
            false)).Count == 0,
            "execution Remove lifecycle created a trade");

        engine.Handle(new HermesEntryRequested(
            "lifecycle-reversal", "Master", "MNQ 09-26", -2, 20000m, 20010m,
            new[] { new HermesTarget(2, 19980m) }));
        engine.Handle(Order("Master", setup.StopKey, "Filled", 1, 1,
            setup.Protection.CommandId, "S0"));
        Assert(engine.Handle(Order("Master", setup.TargetKey, "Cancelled", 1, 0,
            setup.Protection.CommandId, "T0"))
            .OfType<SubmitMarketCommand>().Count() == 0,
            "removed execution evidence still satisfied protection cancellation");

        Assert(engine.Handle(ExecutionLifecycle(
            GlitchNativeOperation.Add,
            "amended-fill",
            "Master",
            -1,
            19990m,
            setup.StopKey)).Count == 0,
            "lifecycle evidence alone released a native mutation");
        GlitchCommand[] afterExecution = engine.Handle(Execution(
            "amended-fill", "Master", -1, 19990m, 0, 0,
            setup.StopKey, setup.Protection.CommandId,
            setup.Protection.CommandId,
            GlitchExecutionOrigin.HermesMasterProtection)).ToArray();
        Assert(afterExecution.OfType<SubmitMarketCommand>().Count() == 0
            && afterExecution.OfType<RefreshPositionCommand>().Count() == 1,
            "execution callback bypassed authoritative position refresh");
        SubmitMarketCommand resumed = engine.Handle(new PositionObserved(
                "Master", "MNQ 09-26", 0))
            .OfType<SubmitMarketCommand>().Single();
        Assert(resumed.SignedQuantity == -2 && resumed.ExpectedSignedPosition == 0,
            "corrected execution evidence did not resume exactly once");
    }

    private static void TestReplicationMathAndFollowerIndependence()
    {
        var batched = new GlitchEngine();
        batched.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(batched, "r", 0.5m);
        SubmitMarketCommand batch = batched.Handle(Execution(
            "master-batch", "Master", 2, 20000m, 2, 2,
            "manual-master", null, null, GlitchExecutionOrigin.External))
            .OfType<SubmitMarketCommand>()
            .Single();
        Assert(batch.SignedQuantity == 1, "batched half-ratio allocation was wrong");

        var partial = new GlitchEngine();
        partial.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(partial, "r", 0.5m);
        SubmitMarketCommand first = partial.Handle(Execution(
            "master-part-1", "Master", 1, 20000m, 1, 1,
            "manual-master-1", null, null, GlitchExecutionOrigin.External))
            .OfType<SubmitMarketCommand>()
            .Single();
        Assert(first.SignedQuantity == 1, "first cumulative half-ratio allocation was wrong");
        Assert(partial.Handle(Execution(
            "master-part-2", "Master", 1, 20001m, 1, 2,
            "manual-master-2", null, null, GlitchExecutionOrigin.External))
            .OfType<SubmitMarketCommand>().Count() == 0,
            "partial batching changed cumulative allocation");
        Assert(partial.Handle(Execution(
            "master-part-2", "Master", 1, 20001m, 1, 2,
            "manual-master-2", null, null, GlitchExecutionOrigin.External))
            .Count == 0,
            "duplicate master execution produced a second allocation");
        Assert(partial.Handle(Execution(
            "manual-follower", "Follower", -1, 19999m, 0, -1,
            "manual-follower-order", null, null, GlitchExecutionOrigin.External))
            .Count == 0,
            "independent follower execution produced a mutation");

        var zero = new GlitchEngine();
        zero.Handle(new PositionObserved("Master", "MNQ 09-26", 3));
        zero.Handle(new PositionObserved("Follower", "MNQ 09-26", 2));
        ConfigureRoute(zero, "zero", 0m);
        SubmitMarketCommand zeroSync = zero.Handle(new RouteSynchronizationRequested("zero"))
            .OfType<SubmitMarketCommand>()
            .Single();
        Assert(zeroSync.SignedQuantity == -2,
            "ratio zero did not translate explicit Sync to zero exposure");
    }

    private static void TestFollowerReversalUsesOneNativeDeltaAndCurrentPositionTruth()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "atomic-reversal", 5m);

        SubmitMarketCommand open = ObserveExecution(engine, Execution(
            "master-open", "Master", 1, 20000m, 1, 1,
            "master-open-order", null, null, GlitchExecutionOrigin.External))
            .OfType<SubmitMarketCommand>().Single();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 1));
        int followerPosition = 0;
        Assert(CompleteTrade(
                engine, open, ref followerPosition, "atomic-open",
                GlitchExecutionOrigin.GlitchReplication)
                .OfType<SubmitMarketCommand>().Count() == 0,
            "opening copy emitted an extra native request");
        Assert(followerPosition == 5, "reversal setup did not reach follower +5");

        SubmitMarketCommand reverse = ObserveExecution(engine, Execution(
            "master-reverse", "Master", -2, 19999m, 1, -1,
            "master-reverse-order", null, null, GlitchExecutionOrigin.External))
            .OfType<SubmitMarketCommand>().Single();
        Assert(reverse.SignedQuantity == -10 && reverse.ExpectedSignedPosition == 5,
            "follower reversal was split instead of preserving the immutable -10 delta");
        Assert(engine.Handle(new PositionObserved(
                "Master", "MNQ 09-26", -1)).Count == 0,
            "master position confirmation emitted another follower request");

        const string reverseOrder = "follower-reverse-order";
        Assert(engine.Handle(Order(
                "Follower", reverseOrder, "Working", 10, 0,
                reverse.CommandId, "M")).Count == 0,
            "working reversal order emitted another native request");
        Assert(ObserveExecution(engine, Execution(
                "follower-reverse-part-1", "Follower", -2, 19998m, 0, 3,
                reverseOrder, reverse.CommandId, null,
                GlitchExecutionOrigin.GlitchReplication)).Count == 0,
            "partial reversal fill emitted another native request");
        Assert(engine.Handle(new PositionObserved(
                "Follower", "MNQ 09-26", 3)).Count == 0,
            "partial-fill position emitted another native request");
        Assert(engine.Handle(Order(
                "Follower", reverseOrder, "Filled", 10, 10,
                reverse.CommandId, "M")).Count == 0,
            "terminal order fact outran execution evidence");
        Assert(ObserveExecution(engine, Execution(
                "follower-reverse-part-2", "Follower", -8, 19997m, 5, -5,
                reverseOrder, reverse.CommandId, null,
                GlitchExecutionOrigin.GlitchReplication)).Count == 0,
            "final reversal fill reused the stale partial-fill position");
        Assert(engine.Handle(new PositionObserved(
                "Follower", "MNQ 09-26", -5)).Count == 0,
            "final follower position emitted another native request");
        Assert(engine.GetOperationPhase(
                "REPL|atomic-reversal|Master|master-reverse")
                == GlitchOperationPhase.Completed,
            "atomic follower reversal did not complete from native facts");
    }

    private static void TestReplicationAllocationIsBatchingIndependent()
    {
        foreach (decimal ratio in new[] { 0m, 0.5m, 1m, 2m })
        {
            int[] batchedOpen = RunAllocation(ratio, 2);
            int[] partialOpen = RunAllocation(ratio, 1, 1);
            Assert(batchedOpen.Sum() == partialOpen.Sum(),
                "open allocation depended on execution batching at ratio " + ratio);
            Assert(batchedOpen.Sum() == decimal.ToInt32(decimal.Round(
                    2m * ratio, 0, MidpointRounding.AwayFromZero)),
                "open allocation total was incorrect at ratio " + ratio);

            int[] batchedRoundTrip = RunAllocation(ratio, 2, -2);
            int[] partialRoundTrip = RunAllocation(ratio, 1, 1, -1, -1);
            Assert(batchedRoundTrip.Sum() == 0 && partialRoundTrip.Sum() == 0,
                "signed round-trip allocation did not return to zero at ratio " + ratio);
            Assert(batchedRoundTrip.Sum(value => Math.Abs(value))
                    == partialRoundTrip.Sum(value => Math.Abs(value)),
                "close allocation depended on execution batching at ratio " + ratio);
        }
    }

    private static void TestEveryFollowerManualActionRemainsIndependent()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "manual-follower", 1m);
        int followerPosition = 0;
        int masterPosition = 0;
        int sequence = 0;

        Action<int> masterFill = signedQuantity =>
        {
            masterPosition += signedQuantity;
            SubmitMarketCommand command = ObserveExecution(engine, Execution(
                "master-manual-sequence-" + (++sequence),
                "Master",
                signedQuantity,
                20000m + sequence,
                Math.Sign(masterPosition) == Math.Sign(signedQuantity)
                    ? Math.Abs(signedQuantity) : 0,
                masterPosition,
                "master-manual-order-" + sequence,
                null,
                null,
                GlitchExecutionOrigin.External))
                .OfType<SubmitMarketCommand>().Single();
            Assert(engine.Handle(new PositionObserved(
                    "Master", "MNQ 09-26", masterPosition)).Count == 0,
                "master position observation created an extra mutation");
            Assert(command.SignedQuantity == signedQuantity,
                "later master delta was changed by follower-local activity");
            CompleteTrade(
                engine,
                command,
                ref followerPosition,
                "manual-copy-" + sequence,
                GlitchExecutionOrigin.GlitchReplication);
        };

        Action<int, int, string> followerManual = (signedQuantity, postPosition, identity) =>
        {
            Assert(ObserveExecution(engine, Execution(
                "follower-manual-" + identity,
                "Follower",
                signedQuantity,
                19990m,
                Math.Sign(postPosition) == Math.Sign(signedQuantity)
                    ? Math.Min(Math.Abs(signedQuantity), Math.Abs(postPosition)) : 0,
                postPosition,
                "follower-manual-order-" + identity,
                null,
                null,
                GlitchExecutionOrigin.External)).Count == 0,
                "manual follower " + identity + " caused a Glitch mutation");
            Assert(engine.Handle(new PositionObserved(
                    "Follower", "MNQ 09-26", postPosition)).Count == 0,
                "manual follower position observation caused a Glitch mutation");
            followerPosition = postPosition;
        };

        masterFill(1);
        followerManual(2, 3, "add");
        masterFill(1);
        followerManual(-1, 3, "reduce");
        masterFill(-1);
        followerManual(-2, 0, "close");
        masterFill(1);
        followerManual(-3, -2, "reverse");
        masterFill(-1);
        Assert(followerPosition == -3,
            "manual follower history was reconciled instead of copying later deltas");
    }

    private static void TestRouteLifecycleHasOnlyRequestedEffects()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 4));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 1));
        int followerPosition = 1;

        SubmitMarketCommand addSync = ConfigureRoute(engine, "lifecycle", 1m, true)
            .OfType<SubmitMarketCommand>().Single();
        Assert(addSync.SignedQuantity == 3,
            "adding an enabled route did not synchronize current exposure");
        CompleteTrade(engine, addSync, ref followerPosition, "route-add",
            GlitchExecutionOrigin.GlitchSynchronization);

        SubmitMarketCommand ratioSync = ConfigureRoute(engine, "lifecycle", 0.5m, true)
            .OfType<SubmitMarketCommand>().Single();
        Assert(ratioSync.SignedQuantity == -2,
            "ratio edit did not synchronize to the requested ratio");
        CompleteTrade(engine, ratioSync, ref followerPosition, "route-ratio",
            GlitchExecutionOrigin.GlitchSynchronization);

        Assert(engine.Handle(new RouteConfigurationChanged(
            true, Array.Empty<RouteConfigurationItem>())).Count == 0,
            "route removal created an unrequested close");
        Assert(ObserveExecution(engine, Execution(
            "master-after-remove", "Master", 1, 20010m, 1, 5,
            "master-after-remove-order", null, null,
            GlitchExecutionOrigin.External)).Count == 0,
            "removed route copied a future execution");
        Assert(engine.Handle(new PositionObserved(
                "Master", "MNQ 09-26", 5)).Count == 0,
            "removed-route position observation created a mutation");

        Assert(engine.Handle(new RouteConfigurationChanged(
            true,
            new[] { new RouteConfigurationItem(
                "lifecycle", "Master", "Follower", 1m, false) },
            new[] { "lifecycle" })).Count == 0,
            "disabled route synchronized or closed exposure");
        SubmitMarketCommand enableSync = engine.Handle(new RouteConfigurationChanged(
            true,
            new[] { new RouteConfigurationItem(
                "lifecycle", "Master", "Follower", 1m, true) },
            new[] { "lifecycle" }))
            .OfType<SubmitMarketCommand>().Single();
        Assert(enableSync.SignedQuantity == 3,
            "enabling a route did not synchronize from current native facts");
    }

    private static void TestSynchronizationRefreshesAreInstrumentScoped()
    {
        var engine = new GlitchEngine();
        foreach (string instrument in new[] { "M2K 09-26", "MNQ 09-26" })
        {
            engine.Handle(new PositionObserved("Master", instrument, 0));
            engine.Handle(new PositionObserved("Follower", instrument, 0));
            MarkPositionUnknown(engine, "Follower", instrument, "unknown-" + instrument);
        }

        RefreshPositionCommand[] refreshes = ConfigureRoute(engine, "scoped-sync", 1m, true)
            .OfType<RefreshPositionCommand>()
            .ToArray();
        Assert(refreshes.Length == 2,
            "multi-instrument synchronization did not request both follower positions");
        Assert(refreshes.Select(value => value.CommandId)
                .Distinct(System.StringComparer.OrdinalIgnoreCase).Count() == 2,
            "multi-instrument synchronization reused one refresh command identity");
    }

    private static void TestUnknownSynchronizationRefreshIsTerminal()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 1));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "unknown-sync", 1m);
        MarkPositionUnknown(engine, "Follower", "MNQ 09-26", "unknown-sync-follower");

        RefreshPositionCommand refresh = engine.Handle(
                new RouteSynchronizationRequested("unknown-sync"))
            .OfType<RefreshPositionCommand>()
            .Single();
        Assert(engine.IsCommandPending(refresh.CommandId),
            "synchronization refresh was not pending before terminal unknown evidence");
        Assert(engine.Handle(new NativeRequestUnknownObserved(
                refresh.CommandId, "command_identity_conflict")).Count == 0,
            "unknown synchronization refresh emitted a native mutation");
        Assert(!engine.IsCommandPending(refresh.CommandId),
            "unknown synchronization refresh remained pending");
        Assert(engine.Handle(new PositionObserved(
                "Follower", "MNQ 09-26", 0))
            .OfType<SubmitMarketCommand>().Count() == 0,
            "retired synchronization resumed after a later position observation");
    }

    private static void TestSynchronizationConvergesToCapturedTargetAfterReplication()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "sync-race", 1m);
        MarkPositionUnknown(engine, "Follower", "MNQ 09-26", "sync-race-follower");
        engine.Handle(new RouteSynchronizationRequested("sync-race"));

        engine.Handle(Execution(
            "sync-race-master-fill", "Master", 1, 20000m, 1, 1,
            "sync-race-master-order", null, null, GlitchExecutionOrigin.External));
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 1));
        SubmitMarketCommand replication = engine.Handle(new PositionObserved(
                "Follower", "MNQ 09-26", 0))
            .OfType<SubmitMarketCommand>()
            .Single();
        Assert(replication.Purpose == GlitchCommandPurpose.Replication
            && replication.SignedQuantity == 1,
            "replication did not retain FIFO ownership ahead of synchronization");

        int followerPosition = 0;
        IReadOnlyList<GlitchCommand> afterReplication = CompleteTrade(
            engine,
            replication,
            ref followerPosition,
            "sync-race-replication",
            GlitchExecutionOrigin.GlitchReplication);
        Assert(followerPosition == 1,
            "replication did not reach the captured synchronization target");
        Assert(afterReplication.OfType<SubmitMarketCommand>().Count() == 0,
            "synchronization applied a stale delta after replication reached its target");
    }

    private static void TestSynchronizationTargetReplansFromLatestFollowerPosition()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 2));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        SubmitMarketCommand original = ConfigureRoute(engine, "target-replan", 1m, true)
            .OfType<SubmitMarketCommand>()
            .Single();
        Assert(original.SignedQuantity == 2,
            "synchronization setup did not capture the expected target");

        SubmitMarketCommand replanned = engine.Handle(new NativePlanStaleObserved(
                original.CommandId, "Follower", "MNQ 09-26", 1))
            .OfType<SubmitMarketCommand>()
            .Single();
        Assert(replanned.SignedQuantity == 1
            && replanned.ExpectedSignedPosition == 1,
            "synchronization did not replan from latest follower position truth");
    }

    private static void TestSynchronizationTargetPreservesOpeningOrderLimit()
    {
        var engine = new GlitchEngine();
        engine.Handle(new ReplicationQuantityLimitChanged("Follower", 1));
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 3));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        SubmitMarketCommand command = ConfigureRoute(engine, "target-limit", 1m, true)
            .OfType<SubmitMarketCommand>()
            .Single();
        int followerPosition = 0;
        for (int step = 1; step <= 3; step++)
        {
            Assert(command.SignedQuantity == 1,
                "synchronization target exceeded the follower opening order limit");
            SubmitMarketCommand[] next = CompleteTrade(
                    engine,
                    command,
                    ref followerPosition,
                    "target-limit-" + step,
                    GlitchExecutionOrigin.GlitchSynchronization)
                .OfType<SubmitMarketCommand>()
                .ToArray();
            if (step < 3)
            {
                Assert(next.Length == 1,
                    "synchronization target did not continue after a limited opening step");
                command = next[0];
            }
            else
            {
                Assert(next.Length == 0,
                    "synchronization target emitted work after reaching its target");
            }
        }
        Assert(followerPosition == 3,
            "limited synchronization did not converge to the captured target");
    }

    private static void TestProtectionRejectionIsTerminalAndNeverRetried()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        SubmitMarketCommand entry = engine.Handle(new HermesEntryRequested(
            "reject-protection", "Master", "MNQ 09-26", 1, 20000m, 19990m,
            new[] { new HermesTarget(1, 20020m) }))
            .OfType<SubmitMarketCommand>().Single();
        engine.Handle(Order("Master", "reject-entry", "Working", 1, 0,
            entry.CommandId, "M"));
        SubmitProtectionCommand protection = ObserveExecution(engine, Execution(
            "reject-entry-fill", "Master", 1, 20001m, 1, 1,
            "reject-entry", entry.CommandId, null,
            GlitchExecutionOrigin.HermesMaster))
            .OfType<SubmitProtectionCommand>().Single();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 1));
        engine.Handle(Order("Master", "reject-entry", "Filled", 1, 1,
            entry.CommandId, "M"));
        Assert(engine.Handle(new NativeOrderObserved(
            "Master", "MNQ 09-26", "reject-stop", "reject-stop", "reject-stop",
            "Rejected", 1, 0, 19991m, null, "OrderRejected", "invalid stop",
            "OCO-reject", protection.CommandId, "S0"))
            .OfType<SubmitProtectionCommand>().Count() == 0,
            "rejected protection was retried");
        Assert(engine.GetOperationPhase("HERMES|reject-protection")
                == GlitchOperationPhase.Failed,
            "protection rejection was not terminal failure evidence");
        Assert(engine.Handle(Order("Master", "reject-target", "Working", 1, 0,
            protection.CommandId, "T0"))
            .OfType<SubmitProtectionCommand>().Count() == 0,
            "later sibling evidence retried rejected protection");
    }

    private static SubmitMarketCommand StartHermesExit(ProtectedRouteBook setup)
    {
        GlitchEngine engine = setup.Engine;
        CancelProtectionCommand cancel = engine.Handle(new HermesExitRequested(
            "race-exit", "Master", "MNQ 09-26"))
            .OfType<CancelProtectionCommand>().Single();
        Assert(cancel.TargetCommandIds.SequenceEqual(
                new[] { setup.MasterProtection.CommandId }),
            "Hermes EXIT did not cancel only its exact master protection");
        Assert(engine.Handle(Order("Master", setup.MasterStopKey, "Cancelled", 1, 0,
            setup.MasterProtection.CommandId, "S0"))
            .OfType<SubmitMarketCommand>().Count() == 0,
            "Hermes EXIT traded before master protection was terminal");
        Assert(engine.Handle(Order(
            "Master", setup.MasterTargetKey, "Cancelled", 1, 0,
            setup.MasterProtection.CommandId, "T0"))
            .OfType<SubmitMarketCommand>().Count() == 0,
            "Hermes EXIT traded before external protection cancellation completed");
        SubmitMarketCommand[] emitted = engine.Handle(
            new ProtectionCancellationCompletedObserved(cancel.CommandId))
            .OfType<SubmitMarketCommand>().ToArray();
        Assert(emitted.Length == 1,
            "Hermes EXIT cancellation released " + emitted.Length
            + " trades; phase=" + engine.GetOperationPhase("HERMES|race-exit"));
        Assert(emitted[0].Purpose == GlitchCommandPurpose.HermesMasterExit
                && emitted[0].ParentCorrelationId == "race-exit",
            "Hermes EXIT did not preserve intent identity into native execution");
        return emitted[0];
    }

    private static IReadOnlyList<GlitchCommand> FillFollowerStop(ProtectedRouteBook setup)
    {
        GlitchEngine engine = setup.Engine;
        var commands = new List<GlitchCommand>();
        commands.AddRange(engine.Handle(Order(
            "Follower", setup.FollowerStopKey, "Filled", 1, 1,
            setup.FollowerProtection.CommandId, "S0")));
        commands.AddRange(engine.Handle(Order(
            "Follower", setup.FollowerTargetKey, "Cancelled", 1, 0,
            setup.FollowerProtection.CommandId, "T0")));
        commands.AddRange(ObserveExecution(engine, Execution(
            "race-follower-stop-fill", "Follower", -1, 19993m, 0, 0,
            setup.FollowerStopKey,
            setup.FollowerProtection.CommandId,
            setup.FollowerProtection.CommandId,
            GlitchExecutionOrigin.GlitchProtection)));
        commands.AddRange(engine.Handle(new PositionObserved(
            "Follower", "MNQ 09-26", 0)));
        return commands;
    }

    private static void TestMasterExitAndFollowerProtectionFillRace()
    {
        ProtectedRouteBook protectionFirst = CreateProtectedRouteLong();
        Assert(FillFollowerStop(protectionFirst)
            .OfType<SubmitMarketCommand>().Count() == 0,
            "follower protective fill invented a replacement trade");
        SubmitMarketCommand firstMasterExit = StartHermesExit(protectionFirst);
        int firstMasterPosition = 1;
        Assert(CompleteTrade(
            protectionFirst.Engine,
            firstMasterExit,
            ref firstMasterPosition,
            "race-protection-first-master-exit",
            GlitchExecutionOrigin.HermesMaster)
            .OfType<SubmitMarketCommand>().Count() == 0,
            "already-settled follower exposure received a duplicate close");

        ProtectedRouteBook masterFirst = CreateProtectedRouteLong();
        SubmitMarketCommand secondMasterExit = StartHermesExit(masterFirst);
        int secondMasterPosition = 1;
        IReadOnlyList<GlitchCommand> masterExitCommands = CompleteTrade(
            masterFirst.Engine,
            secondMasterExit,
            ref secondMasterPosition,
            "race-master-first-master-exit",
            GlitchExecutionOrigin.HermesMaster);
        CancelProtectionCommand followerCancel = masterExitCommands
            .OfType<CancelProtectionCommand>().Single();
        Assert(followerCancel.AccountName == "Follower"
            && followerCancel.TargetCommandIds.SequenceEqual(
                new[] { masterFirst.FollowerProtection.CommandId }),
            "copied master exit did not wait on exact follower protection");
        IReadOnlyList<GlitchCommand> afterFill = FillFollowerStop(masterFirst);
        Assert(afterFill.OfType<SubmitMarketCommand>().Count() == 0,
            "protection fill during copied-exit cancellation reversed the follower");
        Assert(afterFill.OfType<SubmitProtectionCommand>().Count() == 0,
            "settled follower exposure was re-protected after the race");
        Assert(masterFirst.Engine.GetOperationPhase(
                "REPL|race-route|Master|execution-race-master-first-master-exit")
                == GlitchOperationPhase.Completed,
            "copied exit did not settle deterministically after the protection race");
    }

    private static void TestHermesExitDoesNotReverseAfterProtectionWinsCancellationRace()
    {
        ProtectedRouteBook setup = CreateProtectedRouteLong();
        GlitchEngine engine = setup.Engine;
        CancelProtectionCommand cancel = engine.Handle(new HermesExitRequested(
            "master-protection-race-exit", "Master", "MNQ 09-26"))
            .OfType<CancelProtectionCommand>().Single();

        var commands = new List<GlitchCommand>();
        commands.AddRange(engine.Handle(Order(
            "Master", setup.MasterStopKey, "Filled", 1, 1,
            setup.MasterProtection.CommandId, "S0")));
        commands.AddRange(engine.Handle(Order(
            "Master", setup.MasterTargetKey, "Cancelled", 1, 0,
            setup.MasterProtection.CommandId, "T0")));
        commands.AddRange(ObserveExecution(engine, Execution(
            "master-protection-race-fill", "Master", -1, 19991m, 0, 0,
            setup.MasterStopKey,
            setup.MasterProtection.CommandId,
            setup.MasterProtection.CommandId,
            GlitchExecutionOrigin.HermesMasterProtection)));
        commands.AddRange(engine.Handle(new PositionObserved(
            "Master", "MNQ 09-26", 0)));
        commands.AddRange(engine.Handle(
            new ProtectionCancellationCompletedObserved(cancel.CommandId)));

        Assert(commands.OfType<SubmitMarketCommand>()
                .Count(command => command.AccountName == "Master") == 0,
            "Hermes EXIT reversed the master after protection had already flattened it");
        Assert(engine.GetOperationPhase("HERMES|master-protection-race-exit")
                == GlitchOperationPhase.Completed,
            "Hermes EXIT did not complete from authoritative native flat state");
    }

    private static void TestMasterProtectiveExitCopiesDeltaDespiteManualFollowerFlat()
    {
        ProtectedRouteBook setup = CreateProtectedRouteLong();
        GlitchEngine engine = setup.Engine;
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        var commands = new List<GlitchCommand>();
        commands.AddRange(engine.Handle(Order(
            "Master", setup.MasterStopKey, "Filled", 1, 1,
            setup.MasterProtection.CommandId, "S0")));
        commands.AddRange(engine.Handle(Order(
            "Master", setup.MasterTargetKey, "Cancelled", 1, 0,
            setup.MasterProtection.CommandId, "T0")));
        commands.AddRange(ObserveExecution(engine, Execution(
            "race-master-protective-fill", "Master", -1, 19991m, 0, 0,
            setup.MasterStopKey,
            setup.MasterProtection.CommandId,
            setup.MasterProtection.CommandId,
            GlitchExecutionOrigin.GlitchProtection)));

        CancelProtectionCommand followerCancel = commands
            .OfType<CancelProtectionCommand>()
            .Single(command => command.AccountName == "Follower");
        Assert(followerCancel.TargetCommandIds.SequenceEqual(
                new[] { setup.FollowerProtection.CommandId }),
            "master protective exit did not cancel the exact follower protection");

        commands.AddRange(engine.Handle(Order(
            "Follower", setup.FollowerStopKey, "Cancelled", 1, 0,
            setup.FollowerProtection.CommandId, "S0")));
        commands.AddRange(engine.Handle(Order(
            "Follower", setup.FollowerTargetKey, "Cancelled", 1, 0,
            setup.FollowerProtection.CommandId, "T0")));

        SubmitMarketCommand copiedExit = commands.OfType<SubmitMarketCommand>()
            .Single(command => command.AccountName == "Follower");
        Assert(copiedExit.SignedQuantity == -1
                && copiedExit.ExpectedSignedPosition == 0,
            "manual follower flat state vetoed or resized the later master delta");
        Assert(commands.OfType<SubmitMarketCommand>().Count() == 1,
            "one master execution emitted more than one follower allocation");
    }

    private static void TestMisreportedExecutionSideCannotCreateReplicationLoop()
    {
        ProtectedRouteBook setup = CreateProtectedRouteLong();
        GlitchEngine engine = setup.Engine;
        var commands = new List<GlitchCommand>();
        commands.AddRange(engine.Handle(Order(
            "Master", setup.MasterTargetKey, "Filled", 1, 1,
            setup.MasterProtection.CommandId, "T0")));
        commands.AddRange(engine.Handle(Order(
            "Master", setup.MasterStopKey, "Cancelled", 1, 0,
            setup.MasterProtection.CommandId, "S0")));
        commands.AddRange(ObserveExecution(engine, Execution(
            "incident-master-target-fill", "Master", -1, 20020m,
            1, -1,
            setup.MasterTargetKey,
            setup.MasterProtection.CommandId,
            setup.MasterProtection.CommandId,
            GlitchExecutionOrigin.HermesMasterProtection)));
        commands.AddRange(engine.Handle(new PositionObserved(
            "Master", "MNQ 09-26", 0)));

        CancelProtectionCommand followerCancel = commands
            .OfType<CancelProtectionCommand>()
            .Single(command => command.AccountName == "Follower");
        commands.AddRange(engine.Handle(Order(
            "Follower", setup.FollowerStopKey, "Cancelled", 1, 0,
            setup.FollowerProtection.CommandId, "S0")));
        commands.AddRange(engine.Handle(Order(
            "Follower", setup.FollowerTargetKey, "Cancelled", 1, 0,
            setup.FollowerProtection.CommandId, "T0")));

        SubmitMarketCommand copiedExit = commands.OfType<SubmitMarketCommand>()
            .Single(command => command.AccountName == "Follower");
        string orderKey = "incident-follower-close";
        var afterCopiedExit = new List<GlitchCommand>();
        afterCopiedExit.AddRange(engine.Handle(Order(
            "Follower", orderKey, "Working", 1, 0,
            copiedExit.CommandId, "M")));
        afterCopiedExit.AddRange(ObserveExecution(engine, Execution(
            "incident-follower-close-fill", "Follower", -1, 20019m,
            1, -1,
            orderKey,
            copiedExit.CommandId,
            null,
            GlitchExecutionOrigin.GlitchReplication)));
        afterCopiedExit.AddRange(engine.Handle(new PositionObserved(
            "Follower", "MNQ 09-26", 0)));
        afterCopiedExit.AddRange(engine.Handle(Order(
            "Follower", orderKey, "Filled", 1, 1,
            copiedExit.CommandId, "M")));

        Assert(afterCopiedExit.OfType<SubmitMarketCommand>().Count() == 0,
            "a copied close fill created a corrective replication order");
        Assert(afterCopiedExit.OfType<SubmitProtectionCommand>().Count() == 0,
            "execution-side metadata misclassified a copied close as opening exposure");
        Assert(engine.GetOperationPhase(
                "REPL|race-route|Master|incident-master-target-fill")
            == GlitchOperationPhase.Completed,
            "the immutable copied delta did not complete after its exact one-contract fill");
    }

    private static void TestManualMasterCloseCancelsOnlyOwnedMasterProtection()
    {
        ProtectedRouteBook setup = CreateProtectedRouteLong();
        IReadOnlyList<GlitchCommand> commands = ObserveExecution(
            setup.Engine,
            Execution(
                "manual-hermes-master-close",
                "Master",
                -1,
                20005m,
                0,
                0,
                "manual-master-close-order",
                null,
                null,
                GlitchExecutionOrigin.External));
        setup.Engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));

        Assert(commands.OfType<SubmitMarketCommand>().Count() == 0,
            "copied close traded before owned follower protection was terminal");
        CancelProtectionCommand cleanup = commands.OfType<CancelProtectionCommand>()
            .Single(value => value.AccountName == "Master");
        Assert(cleanup.AccountName == "Master"
            && cleanup.TargetCommandIds.SequenceEqual(
                new[] { setup.MasterProtection.CommandId }),
            "manual master close did not cancel only Glitch-owned master protection");
        CancelProtectionCommand followerCancel = commands.OfType<CancelProtectionCommand>()
            .Single(value => value.AccountName == "Follower");
        Assert(followerCancel.TargetCommandIds.SequenceEqual(
                new[] { setup.FollowerProtection.CommandId }),
            "manual master close did not route the copied close through follower protection finality");

        Assert(setup.Engine.Handle(Order(
            "Follower", setup.FollowerStopKey, "Cancelled", 1, 0,
            setup.FollowerProtection.CommandId, "S0")).Count == 0,
            "copied close released before every follower protection child was terminal");
        SubmitMarketCommand copiedClose = setup.Engine.Handle(Order(
            "Follower", setup.FollowerTargetKey, "Cancelled", 1, 0,
            setup.FollowerProtection.CommandId, "T0"))
            .OfType<SubmitMarketCommand>().Single();
        Assert(copiedClose.AccountName == "Follower" && copiedClose.SignedQuantity == -1,
            "manual master close did not replicate exactly once");

        Assert(setup.Engine.Handle(Order(
            "Master", setup.MasterStopKey, "Cancelled", 1, 0,
            setup.MasterProtection.CommandId, "S0")).Count == 0,
            "master cleanup completed before every owned child was terminal");
        Assert(setup.Engine.Handle(Order(
            "Master", setup.MasterTargetKey, "Cancelled", 1, 0,
            setup.MasterProtection.CommandId, "T0"))
            .OfType<SubmitProtectionCommand>().Count() == 0,
            "flat manual master exposure was re-protected");
        Assert(setup.Engine.GetOperationPhase(
                "MASTER-CLEANUP|Master|manual-hermes-master-close")
                == GlitchOperationPhase.Completed,
            "owned master protection cleanup did not complete from native finality");

        ProtectedRouteBook followerIntervention = CreateProtectedRouteLong();
        Assert(ObserveExecution(
            followerIntervention.Engine,
            Execution(
                "manual-follower-close-with-protection",
                "Follower",
                -1,
                20005m,
                0,
                0,
                "manual-follower-close-order",
                null,
                null,
                GlitchExecutionOrigin.External)).Count == 0,
            "independent follower close changed or cancelled Glitch state");
    }

    private static void TestRouteChangeAndSynchronizationAreOneInput()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 4));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 4));
        Assert(ConfigureRoute(engine, "atomic", 1m, true).Count == 0,
            "unchanged exposure produced a synchronization order");

        SubmitMarketCommand resize = ConfigureRoute(engine, "atomic", 0.5m, true)
            .OfType<SubmitMarketCommand>()
            .Single();
        Assert(resize.SignedQuantity == -2
            && resize.ExpectedSignedPosition == 4
            && resize.Purpose == GlitchCommandPurpose.GroupSynchronization,
            "route change did not synchronize from the new ratio atomically");
    }

    private static void TestRouteSnapshotRemovalStopsOnlyFutureReplication()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "removed", 1m);
        engine.Handle(new RouteConfigurationChanged(
            true, Array.Empty<RouteConfigurationItem>()));

        Assert(engine.Handle(Execution(
            "after-route-removal", "Master", 1, 20000m, 1, 1,
            "manual-master", null, null, GlitchExecutionOrigin.External)).Count == 0,
            "a removed route copied a later master execution");
    }

    private static void TestProtectionChangeIsMasterFirstAndExact()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "r", 1m);

        SubmitMarketCommand masterEntry = engine.Handle(new HermesEntryRequested(
            "managed-entry", "Master", "MNQ 09-26", 1, 20000m, 19990m,
            new[] { new HermesTarget(1, 20020m) }))
            .OfType<SubmitMarketCommand>().Single();
        engine.Handle(Order("Master", "master-entry-order", "Working", 1, 0,
            masterEntry.CommandId, "M"));
        GlitchCommand[] masterFillCommands = engine.Handle(Execution(
            "master-entry-fill", "Master", 1, 20001m, 1, 1,
            "master-entry-order", masterEntry.CommandId, null,
            GlitchExecutionOrigin.HermesMaster)).ToArray();
        SubmitProtectionCommand masterProtection = masterFillCommands
            .OfType<SubmitProtectionCommand>().Single();
        SubmitMarketCommand followerEntry = masterFillCommands
            .OfType<SubmitMarketCommand>().Single();
        string legId = masterProtection.Targets[0].LegId;
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 1));
        engine.Handle(Order("Master", "master-entry-order", "Filled", 1, 1,
            masterEntry.CommandId, "M"));
        engine.Handle(Order("Master", "master-stop", "Working", 1, 0,
            masterProtection.CommandId, "S0", legId, 19991m));
        engine.Handle(Order("Master", "master-target", "Working", 1, 0,
            masterProtection.CommandId, "T0", legId, null, 20021m));

        engine.Handle(Order("Follower", "follower-entry-order", "Working", 1, 0,
            followerEntry.CommandId, "M"));
        SubmitProtectionCommand followerProtection = engine.Handle(Execution(
            "follower-entry-fill", "Follower", 1, 20003m, 1, 1,
            "follower-entry-order", followerEntry.CommandId, null,
            GlitchExecutionOrigin.GlitchReplication))
            .OfType<SubmitProtectionCommand>().Single();
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 1));
        engine.Handle(Order("Follower", "follower-entry-order", "Filled", 1, 1,
            followerEntry.CommandId, "M"));
        engine.Handle(Order("Follower", "follower-stop", "Working", 1, 0,
            followerProtection.CommandId, "S0", legId, 19993m));
        engine.Handle(Order("Follower", "follower-target", "Working", 1, 0,
            followerProtection.CommandId, "T0", legId, null, 20023m));

        ChangeProtectionCommand masterChange = engine.Handle(
            new HermesProtectionChangeRequested(
                "move-1", "Master", "MNQ 09-26",
                new[] { new HermesProtectionUpdate(legId, 19995m, 20030m) }))
            .OfType<ChangeProtectionCommand>().Single();
        Assert(masterChange.AccountName == "Master"
            && masterChange.TargetCommandIds.SequenceEqual(new[] { masterProtection.CommandId }),
            "protection change did not target the exact master protection request");

        Assert(engine.Handle(Order("Master", "master-stop", "Working", 1, 0,
            masterProtection.CommandId, "S0", legId, 19995m))
            .OfType<ChangeProtectionCommand>().Count() == 0,
            "follower protection changed before all master changes were confirmed");
        ChangeProtectionCommand followerChange = engine.Handle(Order(
            "Master", "master-target", "Working", 1, 0,
            masterProtection.CommandId, "T0", legId, null, 20030m))
            .OfType<ChangeProtectionCommand>().Single();
        Assert(followerChange.AccountName == "Follower"
            && followerChange.TargetCommandIds.SequenceEqual(new[] { followerProtection.CommandId }),
            "follower change did not wait for and target exact native evidence");
        HermesProtectionUpdate followerUpdate = followerChange.Updates.Single();
        Assert(followerUpdate.StopPrice == 19997m
            && followerUpdate.TargetPrice == 20032m,
            "follower protection did not preserve master geometry from its own fill");
    }

    private static void TestFlattenSupersedesOnlyPriorGlitchWork()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "r", 1m);
        SubmitMarketCommand prior = engine.Handle(new HermesEntryRequested(
            "entry-before-flatten", "Master", "MNQ 09-26", 1, 20000m, 19990m,
            new[] { new HermesTarget(1, 20020m) }))
            .OfType<SubmitMarketCommand>().Single();
        FlattenAccountCommand flatten = engine.Handle(new FlattenAccountRequested(
            "flatten-1", "Master", "test"))
            .OfType<FlattenAccountCommand>().Single();
        Assert(engine.GetOperationPhase("HERMES|entry-before-flatten")
            == GlitchOperationPhase.Superseded,
            "explicit Flatten did not supersede prior unfinished Glitch work");
        Assert(engine.Handle(Execution(
            "late-prior-fill", "Master", 1, 20001m, 1, 1,
            "late-prior-order", prior.CommandId, null,
            GlitchExecutionOrigin.HermesMaster))
            .OfType<SubmitMarketCommand>().Count() == 0,
            "a superseded pre-Flatten execution was copied as new intent");
        engine.Handle(new FlattenCompletedObserved(flatten.CommandId, "Master"));
        SubmitMarketCommand later = engine.Handle(Execution(
            "new-user-fill", "Master", 1, 20002m, 1, 1,
            "new-user-order", null, null, GlitchExecutionOrigin.External))
            .OfType<SubmitMarketCommand>().Single();
        Assert(later.AccountName == "Follower" && later.SignedQuantity == 1,
            "Flatten disabled replication for a later independent User execution");
    }

    private static void TestFlattenRetiresPendingFollowerProtectionRecovery()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "flatten-protection", 1m);
        SubmitMarketCommand followerEntry = engine.Handle(Execution(
            "flatten-protection-master-fill", "Master", 1, 20001m, 1, 1,
            "flatten-protection-master-order", null, null,
            GlitchExecutionOrigin.External))
            .OfType<SubmitMarketCommand>().Single();
        const string legId = "UFLATTEN0000001";
        engine.Handle(new MasterProtectionObserved(
            "Master", "MNQ 09-26", 1, 20001m, "flatten-protection-revision",
            new[] { new MasterProtectionLeg(legId, 1, 19991m, 20021m) },
            0.25m));
        engine.Handle(Order("Follower", "flatten-protection-entry", "Working", 1, 0,
            followerEntry.CommandId, "M"));
        SubmitProtectionCommand protection = ObserveExecution(engine, Execution(
            "flatten-protection-follower-fill", "Follower", 1, 20003m, 1, 1,
            "flatten-protection-entry", followerEntry.CommandId, null,
            GlitchExecutionOrigin.GlitchReplication))
            .OfType<SubmitProtectionCommand>().Single();
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 1));
        Assert(engine.IsCommandPending(protection.CommandId),
            "new follower protection was not pending before Flatten");

        FlattenAccountCommand flatten = engine.Handle(new FlattenAccountRequested(
            "flatten-protection-follower", "Follower", "user_flatten_all"))
            .OfType<FlattenAccountCommand>().Single();
        Assert(!engine.IsCommandPending(protection.CommandId),
            "Flatten left stale follower protection eligible for recovery replay");
        engine.Handle(new FlattenCompletedObserved(flatten.CommandId, "Follower"));
        Assert(!engine.IsCommandPending(protection.CommandId),
            "completed Flatten revived stale follower protection recovery");
    }

    private static void TestManualMasterProtectionFollowsNativeRevisions()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "r", 1m);
        SubmitMarketCommand followerEntry = engine.Handle(Execution(
            "manual-master-fill", "Master", 1, 20001m, 1, 1,
            "manual-master-order", null, null, GlitchExecutionOrigin.External))
            .OfType<SubmitMarketCommand>().Single();
        const string legId = "U123456789ABCDE";
        Assert(engine.Handle(new MasterProtectionObserved(
            "Master", "MNQ 09-26", 1, 20001m, "revision-1",
            new[] { new MasterProtectionLeg(legId, 1, 19991m, 20021m) }))
            .Count == 0,
            "master protection created follower orders before a follower fill existed");

        engine.Handle(Order("Follower", "manual-follower-entry", "Working", 1, 0,
            followerEntry.CommandId, "M"));
        SubmitProtectionCommand followerProtection = engine.Handle(Execution(
            "manual-follower-fill", "Follower", 1, 20003m, 1, 1,
            "manual-follower-entry", followerEntry.CommandId, null,
            GlitchExecutionOrigin.GlitchReplication))
            .OfType<SubmitProtectionCommand>().Single();
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 1));
        Assert(followerProtection.Targets.Single().StopPrice == 19993m
            && followerProtection.Targets.Single().Price == 20023m,
            "manual master protection was not offset from the actual follower fill");
        engine.Handle(Order("Follower", "manual-follower-entry", "Filled", 1, 1,
            followerEntry.CommandId, "M"));
        engine.Handle(Order("Follower", "manual-follower-stop", "Working", 1, 0,
            followerProtection.CommandId, "S0", legId, 19993m));
        engine.Handle(Order("Follower", "manual-follower-target", "Working", 1, 0,
            followerProtection.CommandId, "T0", legId, null, 20023m));

        ChangeProtectionCommand change = engine.Handle(new MasterProtectionObserved(
            "Master", "MNQ 09-26", 1, 20001m, "revision-2",
            new[] { new MasterProtectionLeg(legId, 1, 19995m, 20030m) }))
            .OfType<ChangeProtectionCommand>().Single();
        HermesProtectionUpdate update = change.Updates.Single();
        Assert(change.TargetCommandIds.SequenceEqual(new[] { followerProtection.CommandId })
            && update.StopPrice == 19997m
            && update.TargetPrice == 20032m,
            "manual protection revision was not an exact fill-anchored native change");
        engine.Handle(Order("Follower", "manual-follower-stop", "Working", 1, 0,
            followerProtection.CommandId, "S0", legId, 19997m));
        engine.Handle(Order("Follower", "manual-follower-target", "Working", 1, 0,
            followerProtection.CommandId, "T0", legId, null, 20032m));

        CancelProtectionCommand remove = engine.Handle(new MasterProtectionObserved(
            "Master", "MNQ 09-26", 1, 20001m, "revision-3",
            Array.Empty<MasterProtectionLeg>()))
            .OfType<CancelProtectionCommand>().Single();
        Assert(remove.TargetCommandIds.SequenceEqual(new[] { followerProtection.CommandId }),
            "manual protection removal did not cancel the exact mirrored request");
    }

    private static void TestManualProtectionTranslationIsTickAlignedAndReferenceAware()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "tick-safe", 1m);
        SubmitMarketCommand followerEntry = engine.Handle(Execution(
            "tick-safe-master-fill", "Master", 3, 29508.75m, 3, 3,
            "tick-safe-master-order", null, null, GlitchExecutionOrigin.External))
            .OfType<SubmitMarketCommand>().Single();
        const string legId = "UTICKSAFE000001";
        engine.Handle(new MasterProtectionObserved(
            "Master", "MNQ 09-26", 3, 29508.875m, "same-native-orders",
            new[] { new MasterProtectionLeg(legId, 3, 29488.75m, 29548.75m) },
            0.25m));

        engine.Handle(Order("Follower", "tick-safe-follower-entry", "Working", 3, 0,
            followerEntry.CommandId, "M"));
        SubmitProtectionCommand protection = ObserveExecution(engine, Execution(
            "tick-safe-follower-fill", "Follower", 3, 29508.75m, 3, 3,
            "tick-safe-follower-entry", followerEntry.CommandId, null,
            GlitchExecutionOrigin.GlitchReplication))
            .OfType<SubmitProtectionCommand>().Single();
        ProtectionTarget target = protection.Targets.Single();
        Assert(target.StopPrice == 29488.75m && target.Price == 29548.75m,
            "fractional master average produced non-native follower protection prices");
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 3));
        engine.Handle(Order("Follower", "tick-safe-follower-entry", "Filled", 3, 3,
            followerEntry.CommandId, "M"));
        engine.Handle(Order("Follower", "tick-safe-stop", "Working", 3, 0,
            protection.CommandId, "S0", legId, target.StopPrice));
        engine.Handle(Order("Follower", "tick-safe-target", "Working", 3, 0,
            protection.CommandId, "T0", legId, null, target.Price));

        ChangeProtectionCommand change = engine.Handle(new MasterProtectionObserved(
            "Master", "MNQ 09-26", 3, 29509.125m, "same-native-orders",
            new[] { new MasterProtectionLeg(legId, 3, 29488.75m, 29548.75m) },
            0.25m))
            .OfType<ChangeProtectionCommand>().Single();
        HermesProtectionUpdate update = change.Updates.Single();
        Assert(update.StopPrice == 29488.5m && update.TargetPrice == 29548.5m,
            "a changed native average with unchanged order ids did not revise follower geometry");
    }

    private static void TestProtectionFailureRetiresStaleBundleAndSettlesSafetyFlatten()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "safety-settlement", 1m);
        SubmitMarketCommand followerEntry = engine.Handle(Execution(
            "safety-master-open", "Master", 1, 20001m, 1, 1,
            "safety-master-open-order", null, null, GlitchExecutionOrigin.External))
            .OfType<SubmitMarketCommand>().Single();
        const string legId = "USAFETY0000001";
        engine.Handle(new MasterProtectionObserved(
            "Master", "MNQ 09-26", 1, 20001m, "safety-revision-1",
            new[] { new MasterProtectionLeg(legId, 1, 19991m, 20021m) },
            0.25m));
        engine.Handle(Order("Follower", "safety-follower-entry", "Working", 1, 0,
            followerEntry.CommandId, "M"));
        SubmitProtectionCommand protection = ObserveExecution(engine, Execution(
            "safety-follower-fill", "Follower", 1, 20003m, 1, 1,
            "safety-follower-entry", followerEntry.CommandId, null,
            GlitchExecutionOrigin.GlitchReplication))
            .OfType<SubmitProtectionCommand>().Single();
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 1));
        engine.Handle(Order("Follower", "safety-follower-entry", "Filled", 1, 1,
            followerEntry.CommandId, "M"));

        FlattenAccountCommand flatten = engine.Handle(new NativeRequestFailedObserved(
            protection.CommandId, "native_protection_not_started"))
            .OfType<FlattenAccountCommand>().Single();
        Assert(ObserveExecution(engine, Execution(
                "safety-flatten-fill", "Follower", -1, 19999m, 0, 0,
                "safety-flatten-order", null, null, GlitchExecutionOrigin.GlitchFlatten))
                .OfType<SubmitMarketCommand>().Count() == 0,
            "a safety flatten execution was treated as a new trade");
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        engine.Handle(new FlattenCompletedObserved(flatten.CommandId, "Follower"));

        Assert(engine.Handle(new MasterProtectionObserved(
                "Master", "MNQ 09-26", 1, 20001m, "safety-revision-2",
                new[] { new MasterProtectionLeg(legId, 1, 19995m, 20030m) },
                0.25m)).Count == 0,
            "a later master revision revived the failed follower bundle");
        Assert(engine.Handle(Execution(
                "safety-master-close", "Master", -1, 19998m, 0, 0,
                "safety-master-close-order", null, null, GlitchExecutionOrigin.External))
                .OfType<SubmitMarketCommand>().Count() == 0,
            "a master close reversed exposure already removed by Glitch safety flatten");
    }

    private static void TestCompletedMasterSafetyFlattenStartsFreshReplicationEpoch()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "safety-epoch", 2m);

        SubmitMarketCommand masterEntry = engine.Handle(new HermesEntryRequested(
            "safety-epoch-entry", "Master", "MNQ 09-26", 1, 20000m, 19990m,
            new[] { new HermesTarget(1, 20020m) }))
            .OfType<SubmitMarketCommand>().Single();
        engine.Handle(Order("Master", "safety-epoch-master-entry", "Working", 1, 0,
            masterEntry.CommandId, "M"));
        GlitchCommand[] masterFillCommands = ObserveExecution(engine, Execution(
            "safety-epoch-master-entry-fill", "Master", 1, 20001m, 1, 1,
            "safety-epoch-master-entry", masterEntry.CommandId, null,
            GlitchExecutionOrigin.HermesMaster)).ToArray();
        SubmitProtectionCommand masterProtection = masterFillCommands
            .OfType<SubmitProtectionCommand>().Single();
        SubmitMarketCommand followerEntry = masterFillCommands
            .OfType<SubmitMarketCommand>().Single();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 1));
        engine.Handle(Order("Master", "safety-epoch-master-entry", "Filled", 1, 1,
            masterEntry.CommandId, "M"));

        int followerPosition = 0;
        SubmitProtectionCommand followerProtection = CompleteTrade(
            engine,
            followerEntry,
            ref followerPosition,
            "safety-epoch-follower-entry",
            GlitchExecutionOrigin.GlitchReplication)
            .OfType<SubmitProtectionCommand>().Single();
        Assert(followerPosition == 2,
            "safety epoch fixture did not create the scaled follower position");

        FlattenAccountCommand masterFlatten = engine.Handle(new NativeRequestFailedObserved(
            masterProtection.CommandId, "native_protection_not_started"))
            .OfType<FlattenAccountCommand>().Single();
        FlattenAccountCommand followerFlatten = engine.Handle(new NativeRequestFailedObserved(
            followerProtection.CommandId, "native_protection_not_started"))
            .OfType<FlattenAccountCommand>().Single();

        Assert(ObserveExecution(engine, Execution(
                "safety-epoch-master-flatten-fill", "Master", -1, 19999m, 0, 0,
                "safety-epoch-master-flatten", null, null,
                GlitchExecutionOrigin.GlitchFlatten))
                .OfType<SubmitMarketCommand>().Count() == 0,
            "a master safety flatten execution was treated as a new trade");
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        engine.Handle(new FlattenCompletedObserved(masterFlatten.CommandId, "Master"));

        Assert(ObserveExecution(engine, Execution(
                "safety-epoch-follower-flatten-fill", "Follower", -2, 19999m, 0, 0,
                "safety-epoch-follower-flatten", null, null,
                GlitchExecutionOrigin.GlitchFlatten))
                .OfType<SubmitMarketCommand>().Count() == 0,
            "a late follower safety flatten execution was treated as a new trade");
        followerPosition = 0;
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        engine.Handle(new FlattenCompletedObserved(followerFlatten.CommandId, "Follower"));

        SubmitMarketCommand copiedOpen = ObserveExecution(engine, Execution(
            "safety-epoch-new-master-open", "Master", -1, 19990m, 1, -1,
            "safety-epoch-new-master-open-order", null, null,
            GlitchExecutionOrigin.External))
            .OfType<SubmitMarketCommand>().Single();
        Assert(copiedOpen.SignedQuantity == -2,
            "the completed safety flatten suppressed the next independent master entry");
        CompleteTrade(engine, copiedOpen, ref followerPosition,
            "safety-epoch-new-follower-open", GlitchExecutionOrigin.GlitchReplication);
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", -1));

        SubmitMarketCommand copiedClose = ObserveExecution(engine, Execution(
            "safety-epoch-new-master-close", "Master", 1, 19995m, 0, 0,
            "safety-epoch-new-master-close-order", null, null,
            GlitchExecutionOrigin.External))
            .OfType<SubmitMarketCommand>().Single();
        Assert(copiedClose.SignedQuantity == 2
                && copiedClose.ExpectedSignedPosition == -2,
            "the next master exit did not close the newly copied follower position exactly");
        CompleteTrade(engine, copiedClose, ref followerPosition,
            "safety-epoch-new-follower-close", GlitchExecutionOrigin.GlitchReplication);
        Assert(followerPosition == 0,
            "the copied follower lifecycle ended with orphan or reversed exposure");
    }

    private static void TestSynchronizationCreatesAndRevisesMirroredProtection()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 2));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "protected-sync", 1m);
        const string legId = "USYNC0000000001";
        engine.Handle(new MasterProtectionObserved(
            "Master", "MNQ 09-26", 2, 20000m, "sync-revision-1",
            new[] { new MasterProtectionLeg(legId, 2, 19990m, 20020m) },
            0.25m));
        SubmitMarketCommand sync = engine.Handle(
                new RouteSynchronizationRequested("protected-sync"))
            .OfType<SubmitMarketCommand>().Single();
        int followerPosition = 0;
        SubmitProtectionCommand protection = CompleteTrade(
                engine, sync, ref followerPosition, "protected-sync",
                GlitchExecutionOrigin.GlitchSynchronization)
            .OfType<SubmitProtectionCommand>().Single();
        Assert(protection.SignedEntryQuantity == 2,
            "synchronization reopened follower exposure without mirrored protection");
        engine.Handle(Order("Follower", "protected-sync-stop", "Working", 2, 0,
            protection.CommandId, "S0", legId, 19990m));
        engine.Handle(Order("Follower", "protected-sync-target", "Working", 2, 0,
            protection.CommandId, "T0", legId, null, 20020m));

        ChangeProtectionCommand change = engine.Handle(new MasterProtectionObserved(
            "Master", "MNQ 09-26", 2, 20000m, "sync-revision-2",
            new[] { new MasterProtectionLeg(legId, 2, 19995m, 20030m) },
            0.25m))
            .OfType<ChangeProtectionCommand>().Single();
        Assert(change.TargetCommandIds.SequenceEqual(new[] { protection.CommandId })
                && change.Updates.Single().StopPrice == 19995m
                && change.Updates.Single().TargetPrice == 20030m,
            "manual master stop/target revision did not reach synchronized followers");
    }

    private static void TestUserFlattenResetsFractionalAllocationWithoutDisablingReplication()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        ConfigureRoute(engine, "flatten-continuity", 0.5m);
        SubmitMarketCommand first = engine.Handle(Execution(
            "flatten-master-first", "Master", 1, 20000m, 1, 1,
            "flatten-master-first-order", null, null, GlitchExecutionOrigin.External))
            .OfType<SubmitMarketCommand>().Single();
        int followerPosition = 0;
        CompleteTrade(engine, first, ref followerPosition, "flatten-first-copy",
            GlitchExecutionOrigin.GlitchReplication);
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 1));

        FlattenAccountCommand masterFlatten = engine.Handle(new FlattenAccountRequested(
            "flatten-continuity-master", "Master", "user_flatten_all"))
            .OfType<FlattenAccountCommand>().Single();
        FlattenAccountCommand followerFlatten = engine.Handle(new FlattenAccountRequested(
            "flatten-continuity-follower", "Follower", "user_flatten_all"))
            .OfType<FlattenAccountCommand>().Single();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        engine.Handle(new PositionObserved("Follower", "MNQ 09-26", 0));
        engine.Handle(new FlattenCompletedObserved(masterFlatten.CommandId, "Master"));
        engine.Handle(new FlattenCompletedObserved(followerFlatten.CommandId, "Follower"));

        SubmitMarketCommand afterFlatten = engine.Handle(Execution(
            "flatten-master-second", "Master", 1, 20010m, 1, 1,
            "flatten-master-second-order", null, null, GlitchExecutionOrigin.External))
            .OfType<SubmitMarketCommand>().Single();
        Assert(afterFlatten.SignedQuantity == 1,
            "Flatten All left fractional allocation state that suppressed the next master fill");
    }

    private static void TestFailedEntryProtectionFlattensExposedInstrument()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        SubmitMarketCommand entry = engine.Handle(new HermesEntryRequested(
            "protection-failure", "Master", "MNQ 09-26", 1, 20000m, 19990m,
            new[] { new HermesTarget(1, 20020m) }))
            .OfType<SubmitMarketCommand>().Single();
        engine.Handle(Order("Master", "protection-failure-entry", "Working", 1, 0,
            entry.CommandId, "M"));
        SubmitProtectionCommand protection = ObserveExecution(engine, Execution(
            "protection-failure-fill", "Master", 1, 20001m, 1, 1,
            "protection-failure-entry", entry.CommandId, null,
            GlitchExecutionOrigin.HermesMaster))
            .OfType<SubmitProtectionCommand>().Single();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 1));

        FlattenAccountCommand flatten = engine.Handle(new NativeRequestFailedObserved(
            protection.CommandId, "native_protection_not_started"))
            .OfType<FlattenAccountCommand>().Single();
        Assert(flatten.AccountName == "Master"
            && flatten.InstrumentNames.SequenceEqual(new[] { "MNQ 09-26" }),
            "failed protection did not issue an instrument-scoped flatten");
    }

    private static void TestUnknownEntryProtectionFlattensExposedInstrument()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        SubmitMarketCommand entry = engine.Handle(new HermesEntryRequested(
            "protection-unknown", "Master", "MNQ 09-26", 1, 20000m, 19990m,
            new[] { new HermesTarget(1, 20020m) }))
            .OfType<SubmitMarketCommand>().Single();
        engine.Handle(Order("Master", "protection-unknown-entry", "Working", 1, 0,
            entry.CommandId, "M"));
        SubmitProtectionCommand protection = ObserveExecution(engine, Execution(
            "protection-unknown-fill", "Master", 1, 20001m, 1, 1,
            "protection-unknown-entry", entry.CommandId, null,
            GlitchExecutionOrigin.HermesMaster))
            .OfType<SubmitProtectionCommand>().Single();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 1));

        FlattenAccountCommand flatten = engine.Handle(new NativeRequestUnknownObserved(
            protection.CommandId, "native_protection_submission_unknown"))
            .OfType<FlattenAccountCommand>().Single();
        Assert(flatten.AccountName == "Master"
            && flatten.InstrumentNames.SequenceEqual(new[] { "MNQ 09-26" }),
            "unknown protection did not issue an instrument-scoped flatten");
    }

    private static void TestUnknownNativeRequestIsABarrierUntilExplicitFlatten()
    {
        var engine = new GlitchEngine();
        engine.Handle(new PositionObserved("Master", "MNQ 09-26", 0));
        SubmitMarketCommand first = engine.Handle(new HermesEntryRequested(
            "unknown-1", "Master", "MNQ 09-26", 1, 20000m, 19990m,
            new[] { new HermesTarget(1, 20020m) }))
            .OfType<SubmitMarketCommand>().Single();
        engine.Handle(new NativeRequestUnknownObserved(
            first.CommandId, "crash_after_native_boundary"));
        Assert(engine.GetOperationPhase("HERMES|unknown-1")
            == GlitchOperationPhase.Unknown,
            "unknown native outcome was not preserved");
        Assert(engine.Handle(new HermesEntryRequested(
            "unknown-2", "Master", "MNQ 09-26", 1, 20000m, 19990m,
            new[] { new HermesTarget(1, 20020m) }))
            .OfType<SubmitMarketCommand>().Count() == 0,
            "later work crossed an unresolved native request");

        FlattenAccountCommand flatten = engine.Handle(new FlattenAccountRequested(
            "resolve-unknown", "Master", "operator_resolution"))
            .OfType<FlattenAccountCommand>().Single();
        engine.Handle(new FlattenCompletedObserved(flatten.CommandId, "Master"));
        SubmitMarketCommand resumed = engine.Handle(new HermesEntryRequested(
            "unknown-3", "Master", "MNQ 09-26", 1, 20000m, 19990m,
            new[] { new HermesTarget(1, 20020m) }))
            .OfType<SubmitMarketCommand>().Single();
        Assert(resumed.SignedQuantity == 1,
            "explicit native Flatten did not resolve the unknown-operation barrier");
    }

    public static int Main()
    {
        TestReversalIsSequential();
        TestProtectionFillCancellationPermutations();
        TestExecutionLifecycleRevisionIsEvidenceOnly();
        TestReplicationMathAndFollowerIndependence();
        TestFollowerReversalUsesOneNativeDeltaAndCurrentPositionTruth();
        TestReplicationAllocationIsBatchingIndependent();
        TestEveryFollowerManualActionRemainsIndependent();
        TestRouteChangeAndSynchronizationAreOneInput();
        TestRouteLifecycleHasOnlyRequestedEffects();
        TestSynchronizationRefreshesAreInstrumentScoped();
        TestUnknownSynchronizationRefreshIsTerminal();
        TestSynchronizationConvergesToCapturedTargetAfterReplication();
        TestSynchronizationTargetReplansFromLatestFollowerPosition();
        TestSynchronizationTargetPreservesOpeningOrderLimit();
        TestRouteSnapshotRemovalStopsOnlyFutureReplication();
        TestProtectionChangeIsMasterFirstAndExact();
        TestFlattenSupersedesOnlyPriorGlitchWork();
        TestFlattenRetiresPendingFollowerProtectionRecovery();
        TestManualMasterProtectionFollowsNativeRevisions();
        TestManualProtectionTranslationIsTickAlignedAndReferenceAware();
        TestProtectionFailureRetiresStaleBundleAndSettlesSafetyFlatten();
        TestCompletedMasterSafetyFlattenStartsFreshReplicationEpoch();
        TestSynchronizationCreatesAndRevisesMirroredProtection();
        TestUserFlattenResetsFractionalAllocationWithoutDisablingReplication();
        TestFailedEntryProtectionFlattensExposedInstrument();
        TestUnknownEntryProtectionFlattensExposedInstrument();
        TestUnknownNativeRequestIsABarrierUntilExplicitFlatten();
        TestProtectionRejectionIsTerminalAndNeverRetried();
        TestMasterExitAndFollowerProtectionFillRace();
        TestHermesExitDoesNotReverseAfterProtectionWinsCancellationRace();
        TestMasterProtectiveExitCopiesDeltaDespiteManualFollowerFlat();
        TestMisreportedExecutionSideCannotCreateReplicationLoop();
        TestManualMasterCloseCancelsOnlyOwnedMasterProtection();
        Console.WriteLine("Glitch state machine harness passed.");
        return 0;
    }
}
