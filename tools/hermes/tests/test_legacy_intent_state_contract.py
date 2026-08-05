"""Hermes intent identity is recovered from the one operation journal."""

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
ADDON = ROOT / "ninjatrader/Glitch/AddOns/GlitchAddOn"


def source(relative: str) -> str:
    return (ADDON / relative).read_text(encoding="utf-8-sig")


class JournalIntentIdentityContractTests(unittest.TestCase):
    def test_sidecar_intent_state_authority_is_absent(self) -> None:
        self.assertFalse(
            (ADDON / "Services/Ai/GlitchAiIntentStateStore.cs").exists()
        )
        self.assertNotIn(
            "GlitchAiIntentStateStore",
            source("Services/Ai/GlitchAiIntentServer.cs"),
        )

    def test_content_bound_receipts_are_serialized_and_recovered(self) -> None:
        contracts = source("Core/GlitchContracts.cs")
        journal = source("Infrastructure/GlitchOperationJournal.cs")
        host = source("Infrastructure/GlitchRuntimeHost.cs")

        self.assertIn("interface IGlitchHermesIntent", contracts)
        self.assertIn("class HermesNoActionRequested", contracts)
        for field in (
            "content_fingerprint",
            "receipt_status",
            "receipt_code",
            "receipt_message",
        ):
            self.assertIn(field, journal)
        self.assertIn("RememberRecoveredHermesIntent(record.Input)", host)
        self.assertIn("Hermes intent identity has conflicting durable content", host)

    def test_identical_replays_have_stable_response_content(self) -> None:
        contract = source("Services/Ai/GlitchAiIntentResultContract.cs")
        self.assertIn("string intentCreatedUtc", contract)
        self.assertIn("GlitchSnapshotJson.String(intentCreatedUtc", contract)
        self.assertNotIn("DateTime.UtcNow", contract)


if __name__ == "__main__":
    unittest.main()
