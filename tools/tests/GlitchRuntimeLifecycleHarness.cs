using System;
using System.Collections.Generic;
using System.Threading;
using Glitch.Core;
using Glitch.Infrastructure;

internal static class GlitchRuntimeLifecycleHarness
{
    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static int Main()
    {
        GlitchRuntimeOwnershipLease.ResetForTests();
        int firstShutdowns = 0;
        int secondShutdowns = 0;
        var firstOwner = new GlitchRuntimeOwnershipLease(() => firstShutdowns++);
        var secondOwner = new GlitchRuntimeOwnershipLease(() => secondShutdowns++);
        firstOwner.Acquire();
        Assert(firstOwner.IsOwner, "the first AppDomain runtime owner was not published");
        secondOwner.Acquire();
        Assert(firstShutdowns == 1, "replacement did not synchronously stop the prior owner exactly once");
        Assert(secondOwner.IsOwner, "the replacement AppDomain runtime owner was not published");
        firstOwner.Dispose();
        Assert(secondOwner.IsOwner, "stale termination cleared the replacement runtime owner");
        secondOwner.Dispose();
        Assert(!secondOwner.IsOwner, "runtime ownership remained published after termination");

        var failingOwner = new GlitchRuntimeOwnershipLease(
            () => { throw new InvalidOperationException("expected handoff failure"); });
        var blockedReplacement = new GlitchRuntimeOwnershipLease(() => { });
        failingOwner.Acquire();
        bool handoffFailed = false;
        try
        {
            blockedReplacement.Acquire();
        }
        catch (InvalidOperationException error)
        {
            handoffFailed = error.Message == "expected handoff failure";
        }
        Assert(handoffFailed, "a failed prior shutdown did not block replacement ownership");
        Assert(failingOwner.IsOwner, "a failed shutdown discarded the prior runtime owner");
        failingOwner.Dispose();
        blockedReplacement.Dispose();
        GlitchRuntimeOwnershipLease.ResetForTests();

        var observed = new List<long>();
        var observedSignal = new ManualResetEventSlim(false);
        var runtime = new GlitchRuntime(
            evt =>
            {
                lock (observed)
                    observed.Add(evt.Sequence);
                observedSignal.Set();
            });

        Assert(!runtime.TryPost(0, new GlitchRuntimeEvent(0, "before-start")),
            "an inactive runtime accepted an event");

        long firstGeneration = runtime.Start();
        Assert(firstGeneration == 1, "the first runtime generation was not one");
        Assert(runtime.Start() == firstGeneration,
            "starting an active runtime created a second generation");
        Assert(runtime.TryPost(firstGeneration, new GlitchRuntimeEvent(1, "active")),
            "the active runtime rejected an event");
        Assert(observedSignal.Wait(TimeSpan.FromSeconds(2)),
            "the serialized runtime did not consume the event");

        runtime.Stop();
        Assert(!runtime.IsRunning, "the runtime remained active after Stop");
        Assert(!runtime.TryPost(firstGeneration, new GlitchRuntimeEvent(2, "after-stop")),
            "a retired generation accepted an event");

        observedSignal.Reset();
        long secondGeneration = runtime.Start();
        Assert(secondGeneration == 2, "restart did not create a new generation");
        Assert(!runtime.TryPost(firstGeneration, new GlitchRuntimeEvent(2, "stale-callback")),
            "a callback from the retired generation entered the replacement runtime");
        Assert(runtime.TryPost(secondGeneration, new GlitchRuntimeEvent(3, "restarted")),
            "the restarted runtime rejected an event");
        Assert(observedSignal.Wait(TimeSpan.FromSeconds(2)),
            "the restarted runtime did not consume the event");
        runtime.Stop();

        lock (observed)
        {
            Assert(observed.Count == 2, "an event was lost or consumed after retirement");
            Assert(observed[0] == 1 && observed[1] == 3,
                "events were not consumed in the expected generations");
        }

        runtime.Dispose();

        int reportedErrors = 0;
        var afterFault = new ManualResetEventSlim(false);
        var faultRuntime = new GlitchRuntime(
            evt =>
            {
                if (evt.Sequence == 4)
                    throw new InvalidOperationException("expected test fault");
                afterFault.Set();
            },
            error => Interlocked.Increment(ref reportedErrors));
        long faultGeneration = faultRuntime.Start();
        Assert(faultRuntime.TryPost(faultGeneration, new GlitchRuntimeEvent(4, "fault")),
            "the fault probe was rejected");
        Assert(faultRuntime.TryPost(faultGeneration, new GlitchRuntimeEvent(5, "after-fault")),
            "the post-fault probe was rejected");
        Assert(afterFault.Wait(TimeSpan.FromSeconds(2)),
            "one bad event killed the serialized runtime");
        faultRuntime.Dispose();
        Assert(reportedErrors == 1, "the runtime did not report exactly one consumer fault");

        int drained = 0;
        var firstEntered = new ManualResetEventSlim(false);
        var releaseFirst = new ManualResetEventSlim(false);
        var stopCompleted = new ManualResetEventSlim(false);
        var drainingRuntime = new GlitchRuntime(evt =>
        {
            if (evt.Sequence == 6)
            {
                firstEntered.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(2));
            }
            Interlocked.Increment(ref drained);
        });
        long drainingGeneration = drainingRuntime.Start();
        Assert(drainingRuntime.TryPost(
            drainingGeneration, new GlitchRuntimeEvent(6, "drain-first")),
            "the first drain probe was rejected");
        Assert(firstEntered.Wait(TimeSpan.FromSeconds(2)),
            "the drain probe did not enter the consumer");
        Assert(drainingRuntime.TryPost(
            drainingGeneration, new GlitchRuntimeEvent(7, "drain-second")),
            "the queued drain probe was rejected");
        var stopThread = new Thread(() =>
        {
            drainingRuntime.Stop();
            stopCompleted.Set();
        });
        stopThread.Start();
        Assert(!stopCompleted.Wait(TimeSpan.FromMilliseconds(100)),
            "Stop returned before the accepted queue could drain");
        releaseFirst.Set();
        Assert(stopCompleted.Wait(TimeSpan.FromSeconds(2)),
            "Stop did not finish after the queue drained");
        stopThread.Join();
        Assert(drained == 2, "Stop discarded an accepted runtime event");
        drainingRuntime.Dispose();

        Console.WriteLine("Glitch runtime lifecycle harness passed.");
        return 0;
    }
}
