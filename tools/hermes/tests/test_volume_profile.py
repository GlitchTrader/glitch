import importlib.util
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys_path = ROOT / "tools" / "hermes"
import sys
sys.path.insert(0, str(sys_path))

import volume_profile
from profile_root import PROFILE_ROOT

FIXTURE = Path(__file__).resolve().parent / "fixtures" / "volume_at_price_snapshot.v1.json"
SCRIPT = ROOT / "tools" / "hermes" / "run-direct-glitch-cycle.py"
SPEC = importlib.util.spec_from_file_location("direct_glitch_cycle_volume", SCRIPT)
CYCLE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(CYCLE)


def load_fixture():
    return json.loads(FIXTURE.read_text(encoding="utf-8"))


class VolumeProfileFactsTests(unittest.TestCase):
    def test_rows_are_strictly_sorted_and_halves_are_explicit(self):
        profile = load_fixture()["snapshot"]["instruments"][0]["volume_profile"]
        self.assertTrue(volume_profile.rows_are_sorted(profile))
        self.assertEqual(profile["session_rows"][6]["b"], 0.5)
        self.assertEqual(profile["session_rows"][6]["a"], 0.5)
        broken = json.loads(json.dumps(profile))
        broken["session_rows"][0], broken["session_rows"][1] = (
            broken["session_rows"][1],
            broken["session_rows"][0],
        )
        self.assertFalse(volume_profile.rows_are_sorted(broken))

    def test_full_map_facts_survive_and_name_a_vacuum(self):
        payload = load_fixture()
        instrument = payload["snapshot"]["instruments"][0]
        facts = volume_profile.build_facts(
            instrument,
            instrument["volume_profile"],
            payload["ohlc_1m"],
            payload["swings"],
            "fixture",
        )
        self.assertEqual(facts["poc"], 100.5)
        self.assertEqual(facts["vah"], 100.75)
        self.assertEqual(facts["val"], 100)
        self.assertEqual(facts["probe_tolerance_ticks"], 2)
        high = next(level for level in facts["levels"] if level["name"] == "session_high")
        self.assertEqual(high["volume_above"], 24)
        self.assertEqual(high["volume_below"], 148)
        self.assertGreaterEqual(high["touch_count"], 1)
        self.assertTrue(facts["lvn"])
        self.assertGreaterEqual(facts["lvn"][0]["span_ticks"], 4)
        swing_names = [level["name"] for level in facts["levels"] if level["name"] == "swing"]
        self.assertEqual(len(swing_names), 3)
        self.assertLessEqual(volume_profile._size(facts), volume_profile.FACTS_BUDGET_BYTES)
        self.assertNotIn("entry", json.dumps(facts))

    def test_truncated_window_keeps_full_map_poc(self):
        instrument = {
            "instrument": "MES",
            "current_price": 200,
            "instrument_economics": {"point_value_usd": 5, "tick_size": 0.25},
            "session": {"high": 210, "low": 199},
        }
        profile = {
            "schema_version": "glitch.volume_at_price.v1",
            "status": "ready",
            "tick_size": 0.25,
            "session_truncated": True,
            "session_rows": [
                {"p": 199.75, "b": 1, "a": 1},
                {"p": 200, "b": 1, "a": 1},
            ],
            "full_map": {
                "poc": 190,
                "vah": 191,
                "val": 189,
                "value_area_fraction": 0.7,
                "levels": [
                    {"name": "session_high", "price": 210, "volume_above": 0, "volume_below": 500}
                ],
            },
        }
        facts = volume_profile.build_facts(instrument, profile, ohlc_bars=None, swings=None)
        self.assertEqual(facts["poc"], 190)
        self.assertEqual(facts["nodes_scope"], "truncated_window")
        high = next(level for level in facts["levels"] if level["name"] == "session_high")
        self.assertEqual(high["volume_below"], 500)
        self.assertNotIn("volume_at", high)
        reasons = {item["reason"] for item in facts["omitted"]}
        self.assertIn("profile_truncated", reasons)
        self.assertIn("swings_unavailable", reasons)

    def test_missing_profile_is_not_zero(self):
        facts = volume_profile.build_facts({"instrument": "MNQ"}, {"status": "warming", "reason": "no_trades"})
        self.assertNotIn("poc", facts)
        self.assertEqual(facts["omitted"][0]["reason"], "no_trades")

    def test_model_packet_strips_raw_profile_and_keeps_other_instruments(self):
        payload = load_fixture()
        packet = {
            "frames": [
                {"minute_id": "20260930T1404Z", "market_snapshot": {"instruments": [
                    {"instrument": "MNQ", "current_price": 100.5},
                    {"instrument": "MES", "current_price": 7000, "volume_profile": {
                        "schema_version": "glitch.volume_at_price.v1",
                        "status": "warming",
                        "reason": "no_trades",
                        "tick_size": 0.25,
                        "classification": "tick_rule_only",
                    }},
                ]}},
                {"minute_id": "20260930T1405Z", "market_snapshot": {"instruments": [
                    payload["snapshot"]["instruments"][0],
                    {"instrument": "MES", "current_price": 7000, "instrument_economics": {
                        "point_value_usd": 5, "tick_size": 0.25
                    }, "volume_profile": payload["snapshot"]["instruments"][0]["volume_profile"]},
                ], "coverage": [{"instrument_root": "MNQ"}, {"instrument_root": "MES"}]}},
            ],
            "policy": {},
        }
        before = json.dumps(packet).encode("utf-8")
        model = CYCLE.packet_for_model(packet, {"books": []})
        latest = model["frames"][-1]["market_snapshot"]
        self.assertEqual([item["instrument"] for item in latest["instruments"]], ["MNQ"])
        self.assertNotIn("volume_profile", latest["instruments"][0])
        self.assertEqual(latest["instruments"][0]["volume_profile_facts"]["poc"], 100.5)
        side = latest["volume_profile_facts_by_instrument"]
        self.assertEqual(side[0]["instrument"], "MES")
        self.assertNotIn('"volume_profile":', json.dumps(side))
        self.assertNotIn('"volume_profile":', json.dumps(model["frames"][0]))
        after = json.dumps(model).encode("utf-8")
        self.assertLess(len(after), len(before) + 20000)
        delta = volume_profile.snapshot_profile_delta(payload["snapshot"])
        self.assertGreater(delta["snapshot_delta_bytes"], 0)
        self.assertEqual(delta["snapshot_delta_bytes"], delta["corpus_line_delta_bytes"])
        print("VOLUME_PROFILE_DELTA", json.dumps(delta), "model_packet_bytes", len(after))

    def test_skill_stays_inside_the_byte_cap(self):
        skill = (PROFILE_ROOT / "skills" / "glitch-volume-profile" / "SKILL.md").read_bytes()
        self.assertLessEqual(len(skill), 2560)
        self.assertNotIn(b"Gunther", skill)


if __name__ == "__main__":
    unittest.main()
