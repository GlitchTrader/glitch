using System;
using System.Collections.Generic;
using Glitch.Services;

internal static class GlitchReplicationMathHarness
{
    private sealed class FakeAccount
    {
        public int SubmitCalls { get; private set; }
        public int ChangeCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public int SubmittedOrders { get; private set; }

        public void Submit(int orderCount)
        {
            SubmitCalls++;
            SubmittedOrders += orderCount;
        }

        public void Change()
        {
            ChangeCalls++;
        }

        public void Cancel()
        {
            CancelCalls++;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static int Main()
    {
        Assert(GlitchReplicationMath.ScaleQuantity(4, 2) == 8, "ratio 2 failed");
        Assert(GlitchReplicationMath.ScaleQuantity(16, 3) == 48, "ratio 3 failed");

        GlitchExecutionSplit sellReversal = GlitchReplicationMath.SplitExecution(-1, -2);
        Assert(sellReversal.PreExecutionNet == 1
            && sellReversal.CloseQuantity == 1
            && sellReversal.OpenQuantity == 1
            && sellReversal.ExecutionSign == -1,
            "long-to-short reversal was not split into close one/open one");
        GlitchExecutionSplit buyReversal = GlitchReplicationMath.SplitExecution(1, 2);
        Assert(buyReversal.PreExecutionNet == -1
            && buyReversal.CloseQuantity == 1
            && buyReversal.OpenQuantity == 1
            && buyReversal.ExecutionSign == 1,
            "short-to-long reversal was not split into close one/open one");
        GlitchExecutionSplit ordinaryClose = GlitchReplicationMath.SplitExecution(0, -1);
        Assert(ordinaryClose.CloseQuantity == 1 && ordinaryClose.OpenQuantity == 0,
            "ordinary manual close was classified as an opening");
        GlitchExecutionSplit ordinaryOpen = GlitchReplicationMath.SplitExecution(1, 1);
        Assert(ordinaryOpen.CloseQuantity == 0 && ordinaryOpen.OpenQuantity == 1,
            "ordinary manual entry was classified as a close");

        var allocation = new GlitchCumulativeAllocationBook();
        var halfRatio = new Dictionary<string, string> { ["M|F"] = "M|F|Rhalf" };
        allocation.Configure(true, halfRatio);
        GlitchExecutionAllocation firstOrder = allocation.Allocate(
            "M|F", "MNQ 09-26", "open_long", 1, 0.5, "order-a", 1, true);
        GlitchExecutionAllocation secondOrder = allocation.Allocate(
            "M|F", "MNQ 09-26", "open_long", 1, 0.5, "order-b", 1, true);
        Assert(firstOrder.Quantity == 1 && secondOrder.Quantity == 0,
            "separate executions did not share the exact-contract ratio basis");
        allocation.Configure(true, halfRatio);
        GlitchExecutionAllocation unchangedEpoch = allocation.Allocate(
            "M|F", "MNQ 09-26", "open_long", 1, 0.5, "order-c", 1, true);
        Assert(unchangedEpoch.Quantity == 1,
            "an unchanged refresh reset the cumulative allocation epoch");
        var oneRatio = new Dictionary<string, string> { ["M|F"] = "M|F|Rone" };
        allocation.Configure(true, oneRatio);
        GlitchExecutionAllocation futureAfterRatioChange = allocation.Allocate(
            "M|F", "MNQ 09-26", "open_long", 1, 1, "order-d", 2, true);
        Assert(futureAfterRatioChange.Quantity == 1,
            "a ratio change did not start a future-only allocation epoch");
        allocation.Configure(true, halfRatio);
        GlitchExecutionAllocation downwardRatioChange = allocation.Allocate(
            "M|F", "MNQ 09-26", "open_long", 1, 0.5, "order-d", 2, true);
        Assert(downwardRatioChange.Quantity == 1,
            "a downward ratio change retroactively suppressed a future fill");

        List<int> firstSameSignalClaim = GlitchReplicationMath.SelectExactPrefix(
            new[] { 1, 1 }, 1, quantity => quantity);
        List<int> secondSameSignalClaim = GlitchReplicationMath.SelectExactPrefix(
            new[] { 1 }, 1, quantity => quantity);
        Assert(firstSameSignalClaim.Count == 1 && secondSameSignalClaim.Count == 1,
            "two visible same-signal OCO tranches could not be claimed independently");
        Assert(GlitchReplicationMath.SelectExactPrefix(
                new[] { 2 }, 1, quantity => quantity).Count == 0,
            "an overcovered OCO was guessed as exact ownership");

        var fake = new FakeAccount();
        fake.Submit(1);   // one follower market entry request
        fake.Submit(120); // 60 exact-contract stop/target pairs in one submission
        Assert(fake.SubmitCalls == 2 && fake.SubmittedOrders == 121,
            "one master execution was split into repeated submit calls");

        var nativeRequests = new GlitchNativeMaintenanceGate();
        DateTime requestStart = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 300; i++)
        {
            if (nativeRequests.TryAcquire("FollowerA", requestStart.AddMilliseconds(i)))
                fake.Change();
        }
        Assert(fake.ChangeCalls == 2,
            "300 callbacks produced an unbounded native change burst");
        Assert(nativeRequests.TryAcquire("FollowerB", requestStart),
            "one account's maintenance gate blocked a different account");

        Assert(nativeRequests.TryAcquireCancel(
                "FollowerA", "FollowerA|close-1", requestStart.AddMilliseconds(500)),
            "first close cancellation was not admitted");
        fake.Cancel();
        Assert(!nativeRequests.TryAcquireCancel(
                "FollowerA", "FollowerA|close-1", requestStart.AddMilliseconds(750)),
            "the same close order received a duplicate cancel request");
        Assert(nativeRequests.ObserveCancel(
                "FollowerA|close-1", requestStart.AddMilliseconds(800), true, true),
            "CancelPending incorrectly released cancel ownership");
        Assert(!nativeRequests.ObserveCancel(
                "FollowerA|close-1", requestStart.AddMilliseconds(1600), false, true),
            "a native return to Working did not release a stale cancel request");
        Assert(nativeRequests.TryAcquireCancel(
                "FollowerA", "FollowerA|close-1", requestStart.AddMilliseconds(1600)),
            "a confirmed failed cancellation could never be retried");
        fake.Cancel();
        Assert(fake.CancelCalls == 2,
            "cancel request accounting did not match the native acknowledgements");

        Assert(GlitchReplicationMath.BuildCloseTarget(15, 1) == 14,
            "manual follower overexposure was erased by a copied close");
        Assert(GlitchReplicationMath.BuildCloseTarget(5, 1) == 4,
            "manual follower underexposure suppressed a copied close");
        Assert(GlitchReplicationMath.RemainingCloseQuantity(9, 8) == 1,
            "an OCO partial fill did not reduce the remaining copied close");
        Assert(GlitchReplicationMath.RemainingCloseQuantity(8, 8) == 0,
            "an OCO fill satisfying the target still requested a close");
        Assert(GlitchReplicationMath.RemainingCloseQuantity(0, 8) == 0,
            "a flat follower still requested a close");
        Assert(GlitchReplicationMath.RemainingAttributedCloseQuantity(10, 15, 1) == 1,
            "a delayed copied close erased a later manual follower entry");
        Assert(GlitchReplicationMath.BuildAttributedCloseTarget(10, 15, 1) == 14,
            "a later manual follower entry was not preserved in the close target");
        Assert(GlitchReplicationMath.RemainingAttributedCloseQuantity(10, 9, 1) == 0,
            "a native follower reduction satisfying the copied delta still requested a close");
        Assert(GlitchReplicationMath.RemainingAttributedCloseQuantity(-10, -15, 1) == 1,
            "a delayed short close erased a later manual follower short entry");
        Assert(GlitchReplicationMath.BuildAttributedCloseTarget(-10, -15, 1) == -14,
            "a later manual follower short entry was not preserved in the close target");
        Assert(GlitchReplicationMath.RemainingAttributedCloseQuantity(10, 11, 1, 1) == 0,
            "an owned close fill after a manual add was submitted twice");
        Assert(GlitchReplicationMath.BuildAttributedCloseTarget(10, 11, 2, 1) == 10,
            "a second copied close did not preserve a later manual add");

        var amendment = new GlitchProtectionAmendmentGate();
        int submittedChanges = 0;
        amendment.SetDesired(20001);
        if (amendment.TryBegin(true, false, out double submitted))
            submittedChanges++;
        for (int i = 2; i <= 100; i++)
        {
            amendment.SetDesired(20000 + i);
            if (amendment.TryBegin(true, false, out submitted))
                submittedChanges++;
        }
        Assert(submittedChanges == 1,
            "100 callbacks submitted more than one change before native acknowledgement");
        Assert(amendment.Acknowledge(true), "native acknowledgement was not accepted");
        if (amendment.TryBegin(true, false, out submitted))
            submittedChanges++;
        Assert(submittedChanges == 2 && Math.Abs(submitted - 20100) < 0.0000001d,
            "the post-ack change did not coalesce to the latest master price");

        Console.WriteLine("replication math harness: PASS");
        return 0;
    }
}
