using System;
using System.IO;
using Glitch.Services;

internal static class GlitchEvalTargetLockHarness
{
    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "GlitchEvalTargetLock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "EvalTargetLocks.tsv");
        try
        {
            DateTime beforeOpen = new DateTime(2026, 8, 18, 21, 50, 0, DateTimeKind.Utc);
            DateTime afterOpen = new DateTime(2026, 8, 18, 22, 0, 0, DateTimeKind.Utc);
            Require(GlitchEvalTargetLockStore.ResolveSessionId(beforeOpen) == "20260818", "pre-open session identity changed");
            Require(GlitchEvalTargetLockStore.ResolveSessionId(afterOpen) == "20260819", "post-open session identity changed");

            bool first = GlitchEvalTargetLockStore.RecordDetected(
                path, "Eval101", beforeOpen, 26520, 26500, "NetLiquidation", "connected", out GlitchEvalTargetLockState created);
            Require(first && created != null, "first target detection was not persisted");
            bool duplicate = GlitchEvalTargetLockStore.RecordDetected(
                path, "Eval101", beforeOpen.AddMinutes(1), 26400, 26500, "CashValue", "connected", out _);
            Require(!duplicate, "later lower equity replaced the monotonic session latch");
            Require(GlitchEvalTargetLockStore.TryGetActive(path, "Eval101", beforeOpen.AddMinutes(2), out GlitchEvalTargetLockState active), "persisted lock did not reload");
            Require(Math.Abs(active.DetectedEquity - 26520) < 0.001, "detected equity was not monotonic");
            Require(active.EquitySource == "NetLiquidation", "equity source was not preserved");

            GlitchEvalTargetLockStore.RecordAttempt(path, "Eval101", beforeOpen.AddMinutes(2), "flatten_not_accepted", false);
            Require(GlitchEvalTargetLockStore.TryGetActive(path, "Eval101", beforeOpen.AddMinutes(3), out active)
                && active.Status == "pending" && active.LastResult == "flatten_not_accepted", "pending retry state was not durable");
            GlitchEvalTargetLockStore.RecordAttempt(path, "Eval101", beforeOpen.AddMinutes(4), "flat_order_free", true);
            Require(GlitchEvalTargetLockStore.TryGetActive(path, "Eval101", beforeOpen.AddMinutes(5), out active)
                && active.Status == "satisfied", "satisfied state was not durable");
            Require(!GlitchEvalTargetLockStore.TryGetActive(path, "Eval101", afterOpen, out _), "prior-session lock leaked into the next session");

            Console.WriteLine("Eval target lock harness passed.");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
