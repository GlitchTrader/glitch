using System;
using Glitch.Services;

internal static class GlitchAiDailyCaptureProtectionHarness
{
    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static int Main()
    {
        VerifyLongProtection();
        VerifyShortProtection();
        VerifyNoPrematureProtection();
        VerifyNeverLoosens();
        VerifyOnlyLooseLegsMove();
        VerifyRealizedSurplusCanProtectAnOpenLoss();
        VerifyCompleteCoverageRequired();
        VerifyDuplicateLegsAreRejected();
        Console.WriteLine("AI daily capture protection harness passed.");
        return 0;
    }

    private static void VerifyLongProtection()
    {
        bool created = GlitchAiDailyCaptureProtectionPlanner.TryCreatePlan(
            0, 140, 125, 2, 100, 2, 0.25,
            new[]
            {
                new GlitchAiDailyCaptureStopState { LegId = "a", Quantity = 1, StopPrice = 95 },
                new GlitchAiDailyCaptureStopState { LegId = "b", Quantity = 1, StopPrice = 96 }
            },
            out GlitchAiDailyCaptureProtectionPlan plan);
        Require(created, "a protectable long did not create a plan");
        Require(Math.Abs(plan.DesiredStopPrice - 132.25) < 0.0000001,
            "the long stop did not reserve four ticks of fill friction");
        Require(Math.Abs(plan.ExecutionReserveUsd - 4) < 0.0000001,
            "the long execution reserve is incorrect");
        Require(plan.LegIds.Count == 2, "the long plan did not cover both loose legs");
    }

    private static void VerifyShortProtection()
    {
        bool created = GlitchAiDailyCaptureProtectionPlanner.TryCreatePlan(
            50, 90, 125, -1, 200, 5, 0.1,
            new[] { new GlitchAiDailyCaptureStopState { LegId = "short", Quantity = 1, StopPrice = 210 } },
            out GlitchAiDailyCaptureProtectionPlan plan);
        Require(created, "a protectable short did not create a plan");
        Require(Math.Abs(plan.DesiredStopPrice - 184.6) < 0.0000001,
            "the short stop was not rounded in the profit-protecting direction");
    }

    private static void VerifyNoPrematureProtection()
    {
        bool created = GlitchAiDailyCaptureProtectionPlanner.TryCreatePlan(
            0, 129.5, 125, 2, 100, 2, 0.25,
            new[]
            {
                new GlitchAiDailyCaptureStopState { LegId = "a", Quantity = 1, StopPrice = 95 },
                new GlitchAiDailyCaptureStopState { LegId = "b", Quantity = 1, StopPrice = 95 }
            },
            out _);
        Require(!created, "protection was requested without one executable tick beyond the reserve");
    }

    private static void VerifyNeverLoosens()
    {
        bool created = GlitchAiDailyCaptureProtectionPlanner.TryCreatePlan(
            0, 140, 125, 1, 100, 2, 0.25,
            new[] { new GlitchAiDailyCaptureStopState { LegId = "tight", Quantity = 1, StopPrice = 170 } },
            out _);
        Require(!created, "an existing tighter long stop was loosened");
    }

    private static void VerifyOnlyLooseLegsMove()
    {
        bool created = GlitchAiDailyCaptureProtectionPlanner.TryCreatePlan(
            0, 140, 125, 2, 100, 2, 0.25,
            new[]
            {
                new GlitchAiDailyCaptureStopState { LegId = "tight", Quantity = 1, StopPrice = 140 },
                new GlitchAiDailyCaptureStopState { LegId = "loose", Quantity = 1, StopPrice = 95 }
            },
            out GlitchAiDailyCaptureProtectionPlan plan);
        Require(created && plan.LegIds.Count == 1 && plan.LegIds[0] == "loose",
            "the planner changed a leg whose existing stop already protected more");
    }

    private static void VerifyRealizedSurplusCanProtectAnOpenLoss()
    {
        bool created = GlitchAiDailyCaptureProtectionPlanner.TryCreatePlan(
            140, -5, 125, 1, 100, 5, 0.25,
            new[] { new GlitchAiDailyCaptureStopState { LegId = "surplus", Quantity = 1, StopPrice = 90 } },
            out GlitchAiDailyCaptureProtectionPlan plan);
        Require(created, "realized surplus could not protect a bounded open loss");
        Require(Math.Abs(plan.DesiredStopPrice - 98) < 0.0000001,
            "the realized-surplus stop did not preserve target plus reserve");
    }

    private static void VerifyCompleteCoverageRequired()
    {
        bool created = GlitchAiDailyCaptureProtectionPlanner.TryCreatePlan(
            0, 140, 125, 2, 100, 2, 0.25,
            new[] { new GlitchAiDailyCaptureStopState { LegId = "only-one", Quantity = 1, StopPrice = 95 } },
            out _);
        Require(!created, "partial native stop coverage was treated as full capture protection");
    }

    private static void VerifyDuplicateLegsAreRejected()
    {
        bool created = GlitchAiDailyCaptureProtectionPlanner.TryCreatePlan(
            0, 140, 125, 2, 100, 2, 0.25,
            new[]
            {
                new GlitchAiDailyCaptureStopState { LegId = "same", Quantity = 1, StopPrice = 95 },
                new GlitchAiDailyCaptureStopState { LegId = "same", Quantity = 1, StopPrice = 95 }
            },
            out _);
        Require(!created, "ambiguous duplicate native leg identity was accepted");
    }
}
