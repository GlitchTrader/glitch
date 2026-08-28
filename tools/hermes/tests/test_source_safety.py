"""Hermes ingress contracts for the first-principles NinjaTrader runtime."""

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[3]
ADDON = ROOT / "ninjatrader" / "Glitch" / "AddOns" / "GlitchAddOn"


def source(relative: str) -> str:
    return (ADDON / relative).read_text(encoding="utf-8-sig")


class HermesIngressContractTests(unittest.TestCase):
    def test_deleted_cognitive_firewall_cannot_return(self):
        for relative in (
            "Services/Ai/GlitchAiRiskDecision.cs",
            "Services/Ai/GlitchAiRiskFirewall.cs",
        ):
            self.assertFalse((ADDON / relative).exists(), relative)

        combined = source("Services/Ai/GlitchAiIntentServer.cs") + source(
            "Services/Ai/GlitchAiOrderExecutor.cs"
        )
        for forbidden in (
            "GlitchAiRiskFirewall",
            "trading_window_closed",
            "apex_direction_compliance_rejected",
            "license_required",
            "position_conflict",
            "capacity_veto",
        ):
            self.assertNotIn(forbidden, combined)

    def test_ingress_validation_is_structural_and_v3_execution_is_exact(self):
        validator = source("Services/Ai/GlitchAiIntentValidator.cs")
        executor = source("Services/Ai/GlitchAiOrderExecutor.cs")
        server = source("Services/Ai/GlitchAiIntentServer.cs")
        result_contract = source("Services/Ai/GlitchAiIntentResultContract.cs")

        self.assertIn('"glitch.intent.v2"', validator)
        self.assertIn('"glitch.intent.v3"', validator)
        self.assertIn("schema_version_must_be_glitch.intent.v3", validator)
        self.assertIn("enter_requires_quantity_ge_1", validator)
        self.assertIn("ValidateProtectionUpdates(parsed, false", validator)
        self.assertIn("ValidateProtectionUpdates(parsed, true", validator)
        self.assertIn("intent_schema_must_be_v3", executor)
        self.assertIn("ToPositiveInteger(quantityRaw, \"quantity\")", executor)
        self.assertIn('GlitchAiJsonFields.ExtractString(body, "prompt_version")', server)
        self.assertIn('"\\\"prompt_version\\\":"', result_contract)

    def test_hermes_entry_range_reaches_only_the_final_master_entry_boundary(self):
        validator = source("Services/Ai/GlitchAiIntentValidator.cs")
        executor = source("Services/Ai/GlitchAiOrderExecutor.cs")
        contracts = source("Core/GlitchContracts.cs")
        engine = source("Core/GlitchEngine.cs")
        gateway = source("Infrastructure/NinjaTraderGateway.cs")
        evidence = source("Infrastructure/GlitchExecutionEvidenceWriter.cs")

        self.assertIn('"entry_range_low", "entry_range_high"', validator)
        self.assertIn("entry_range_requires_low_and_high", validator)
        self.assertIn('rawJson, "entry_range_low"', executor)
        self.assertIn("public decimal? EntryRangeLow", contracts)
        self.assertIn("entryRangeLow: request.EntryRangeLow", engine)
        self.assertIn("command.Purpose == GlitchCommandPurpose.HermesMasterEntry", gateway)
        self.assertIn("instrument.MarketData?.Ask?.Price", gateway)
        self.assertIn("instrument.MarketData?.Bid?.Price", gateway)
        self.assertIn('"entry_range_superseded"', gateway)
        self.assertLess(
            gateway.index("command.Purpose == GlitchCommandPurpose.HermesMasterEntry"),
            gateway.index("account.Submit(new[] { order })"),
        )
        self.assertIn("TryRequestEntryRangeReassessment", evidence)

    def test_operation_journal_is_the_only_intent_identity_authority(self):
        server = source("Services/Ai/GlitchAiIntentServer.cs")
        executor = source("Services/Ai/GlitchAiOrderExecutor.cs")
        host = source("Infrastructure/GlitchRuntimeHost.cs")
        journal = source("Services/Ai/GlitchAiJournalBridge.cs")

        self.assertFalse((ADDON / "Services/Ai/GlitchAiIntentStateStore.cs").exists())
        self.assertNotIn("GlitchAiIntentStateStore", server)
        self.assertIn("host.FindHermesSubmission", executor)
        self.assertIn("_operationJournal.TryAppendInput", host)
        self.assertIn("RememberRecoveredHermesIntent(record.Input)", host)
        self.assertIn("ContentFingerprint", host)
        self.assertIn("intent_id_content_conflict", server)
        self.assertIn("firstAcceptance", server)
        self.assertIn("_seenRequests", source("Core/GlitchEngine.cs"))
        self.assertIn("_commandFingerprints", source("Infrastructure/GlitchRuntimeHost.cs"))
        self.assertIn("glitch.intent.accepted.v1", journal)
        self.assertNotIn("risk_decision", journal)

    def test_profile_and_account_resolve_to_a_configured_master(self):
        executor = source("Services/Ai/GlitchAiOrderExecutor.cs")

        self.assertIn("policy.TryResolveProfileAccount(profile, out string boundAccount)", executor)
        self.assertIn("profile_account_mismatch", executor)
        self.assertIn("policy.ExecutionBindingsValid", executor)
        self.assertNotIn("master_not_allowlisted", executor)
        self.assertIn("group.MasterAccount", executor)
        self.assertIn("account_not_configured_as_master", executor)

    def test_all_supported_hermes_actions_enter_the_single_runtime(self):
        executor = source("Services/Ai/GlitchAiOrderExecutor.cs")
        host = source("Infrastructure/GlitchRuntimeHost.cs")
        engine = source("Core/GlitchEngine.cs")

        for request_type in (
            "HermesEntryRequested",
            "HermesExitRequested",
            "HermesProtectionChangeRequested",
            "HermesNoActionRequested",
        ):
            self.assertIn(request_type, executor)
            self.assertIn(request_type, engine)
        self.assertIn(
            "public GlitchHermesSubmissionReceipt SubmitHermes(GlitchInput request)",
            host,
        )
        self.assertIn('"durable|hermes_intent"', host)
        self.assertIn("_runtime.TryPost(", host)

    def test_hold_and_nothing_are_authoritative_no_ops(self):
        executor = source("Services/Ai/GlitchAiOrderExecutor.cs")

        self.assertIn('string.Equals(action, "HOLD"', executor)
        self.assertIn('string.Equals(action, "NOTHING"', executor)
        self.assertIn("no_native_action_requested", executor)
        self.assertIn("HermesNoActionRequested", executor)
        self.assertIn("input is HermesNoActionRequested", source("Core/GlitchEngine.cs"))

    def test_user_pause_is_the_only_entry_pause_at_the_translator(self):
        executor = source("Services/Ai/GlitchAiOrderExecutor.cs")

        self.assertIn("GlitchHermesControlStateStore.Load().TradingPaused", executor)
        self.assertIn("trading_paused_by_user", executor)
        self.assertNotIn("DateTime.Now", executor)
        self.assertNotIn("IsLicenseValid", executor)
        self.assertNotIn("MaxContracts", executor)

    def test_body_limit_counts_raw_utf8_bytes(self):
        server = source("Services/Ai/GlitchAiIntentServer.cs")

        self.assertIn("byte[] buffer", server)
        self.assertIn("total > MaxBodyBytes", server)
        self.assertNotIn("StreamReader", server)


if __name__ == "__main__":
    unittest.main()
