import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
CATALOG = ROOT / "apps" / "download" / "src" / "lib" / "release-catalog.json"
EMBEDDED_PROFILE = ROOT / "hermes-profile" / "distribution.yaml"


class ProfilePairContractTests(unittest.TestCase):
    def test_current_ai_release_names_the_embedded_profile(self) -> None:
        catalog = json.loads(CATALOG.read_text(encoding="utf-8-sig"))
        current = [
            row for row in catalog
            if row.get("edition") == "ai" and row.get("version") == "0.0.2.7"
        ]
        self.assertEqual(len(current), 1)
        self.assertEqual(current[0].get("status"), "experimental")
        self.assertEqual(current[0].get("hermesProfileVersion"), "0.0.2.20")

    def test_embedded_profile_is_the_public_profile_version(self) -> None:
        version = next(
            line.split(":", 1)[1].strip()
            for line in EMBEDDED_PROFILE.read_text(encoding="utf-8").splitlines()
            if line.startswith("version:")
        )
        self.assertEqual(version, "0.0.2.20")


if __name__ == "__main__":
    unittest.main()
