"""GL-AI-09: legacy execution journals must preserve nonterminal truth."""

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
AI = ROOT / "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Ai"
STATE_STORE = AI / "GlitchAiIntentStateStore.cs"
RESULT_CONTRACT = AI / "GlitchAiIntentResultContract.cs"


def source(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def method_body(text: str, signature: str, next_signature: str) -> str:
    return text.split(signature, 1)[1].split(next_signature, 1)[0]


class LegacyIntentStateContractTests(unittest.TestCase):
    def test_legacy_reconstruction_uses_the_canonical_phase_contract(self) -> None:
        store = source(STATE_STORE)
        reconstruct = method_body(
            store,
            "private static bool TryReconstructLegacyState",
            "internal static GlitchAiExecutionResult BuildLegacyExecutionResult",
        )
        self.assertIn("BuildLegacyExecutionResult", reconstruct)
        self.assertIn("GlitchAiIntentResultContract.GetPhase(legacyResult)", reconstruct)
        self.assertIn("GlitchAiIntentResultContract.BuildAcceptedJson(intentId, legacyResult)", reconstruct)
        self.assertNotIn(
            'string.Equals(executionStatus, "failed", StringComparison.Ordinal) ? "failed" : "executed"',
            reconstruct,
        )

    def test_pending_failed_executed_and_unknown_statuses_are_explicit(self) -> None:
        store = source(STATE_STORE)
        mapping = method_body(
            store,
            "internal static GlitchAiExecutionResult BuildLegacyExecutionResult",
            "private static string FindLastLine",
        )
        self.assertIn('status == "pending"', mapping)
        self.assertIn("GlitchAiExecutionResult.Pending(code)", mapping)
        self.assertIn('status == "executed"', mapping)
        self.assertIn("GlitchAiExecutionResult.Succeeded(code)", mapping)
        self.assertIn('status == "failed"', mapping)
        self.assertIn("GlitchAiExecutionResult.Failed(code)", mapping)
        self.assertIn('status == "skipped"', mapping)
        self.assertIn("GlitchAiExecutionResult.Skipped(code)", mapping)
        self.assertIn('"legacy_execution_status_unknown"', mapping)

    def test_pending_remains_nonterminal_in_the_canonical_contract(self) -> None:
        contract = source(RESULT_CONTRACT)
        self.assertIn(
            'string.Equals(result?.Status, "pending", StringComparison.Ordinal) ? "pending" : "executed"',
            contract,
        )


if __name__ == "__main__":
    unittest.main()
