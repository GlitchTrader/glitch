import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
CATALOG = ROOT / "apps" / "download" / "src" / "lib" / "release-catalog.json"
LEDGER = ROOT / "docs" / "ledger" / "ledger.json"


class ProfilePairContractTests(unittest.TestCase):
    def test_current_ai_release_names_the_intelligence_first_profile(self) -> None:
        catalog = json.loads(CATALOG.read_text(encoding="utf-8-sig"))
        current = [
            row
            for row in catalog
            if row.get("edition") == "ai" and row.get("version") == "0.0.2.7"
        ]
        self.assertEqual(len(current), 1)
        self.assertEqual(current[0].get("status"), "experimental")
        self.assertEqual(current[0].get("hermesProfileVersion"), "0.0.2.20")

    def test_release_ledger_records_the_exact_profile_pair(self) -> None:
        ledger = json.loads(LEDGER.read_text(encoding="utf-8"))
        release = next(item for item in ledger["items"] if item["id"] == "GL-REL-01")
        evidence = "\n".join(release.get("evidence") or [])
        self.assertIn("Glitch AI 0.0.2.7", evidence)
        self.assertIn("Hermes profile 0.0.2.20", evidence)
        self.assertIn("0ae40d977931a0ee34191da175510a86a92cb10f", evidence)


if __name__ == "__main__":
    unittest.main()
