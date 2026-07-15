import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]


class ThreeLayerHandoffTests(unittest.TestCase):
    def test_build_request_schema_is_approval_gated(self):
        schema = json.loads((ROOT / "glitch_hermes_docs/schemas/supervisor-build-request.v1.schema.json").read_text())
        self.assertEqual(schema["$id"], "glitch.supervisor.build_request.v1")
        self.assertIn("approved", schema["properties"]["status"]["enum"])
        self.assertEqual(schema["properties"]["requested_by"]["const"], "hermes-chat")

    def test_soul_keeps_codex_out_of_trading_runtime(self):
        soul = (ROOT / "hermes-profile/profiles/glitch/SOUL.md").read_text()
        self.assertIn("Codex is a separate bounded builder", soul)
        self.assertIn("never part of your market-data or execution loop", soul)

    def test_handoff_doc_preserves_glitch_truth_authority(self):
        doc = (ROOT / "glitch_hermes_docs/docs/13_three_layer_handoff.md").read_text()
        self.assertIn("Glitch             execution, replication, compliance, brackets, trading truth", doc)
        self.assertIn("appends a `proposed` build request", doc)
        self.assertIn("never runs a Hermes trading cycle", doc)


if __name__ == "__main__":
    unittest.main()
