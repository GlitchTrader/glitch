import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]


class OperatorMapTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.operator = json.loads(
            (ROOT / "hermes-profile" / "operator.json").read_text(encoding="utf-8")
        )
        cls.cognitive_map = (
            ROOT
            / "glitch_hermes_docs"
            / "docs"
            / "12_hermes_trading_skills_and_knowledge.md"
        ).read_text(encoding="utf-8")

    def test_one_native_profile_uses_dynamic_glitch_groups(self):
        self.assertEqual(self.operator["operator_profile"], "glitch")
        self.assertEqual(self.operator["books_source"], "dynamic_from_glitch_packet")
        self.assertNotIn("books", self.operator)
        self.assertNotIn("Sim101", json.dumps(self.operator))
        self.assertTrue(self.operator["skills"]["native_hermes_preserved"])

    def test_public_distribution_is_versioned_portable_and_paused_on_fresh_setup(self):
        profile = ROOT / "hermes-profile"
        distribution = (profile / "distribution.yaml").read_text(encoding="utf-8")
        config = (profile / "config.yaml").read_text(encoding="utf-8")
        setup = (profile / "setup.ps1").read_text(encoding="utf-8")
        builder = (ROOT / "tools/hermes/build-public-profile.ps1").read_text(encoding="utf-8")

        self.assertIn("version: 0.0.2.2", distribution)
        self.assertEqual((profile / ".gitattributes").read_text(encoding="utf-8"), "* -text\n")
        self.assertIn("'.gitattributes'", builder)
        self.assertIn('hermes_requires: \">=0.18.2\"', distribution)
        self.assertNotRegex(config, r"(?i)[a-z]:\\")
        self.assertIn("$preserveEnabled = $false", setup)
        self.assertIn("hermes cron pause $jobId", setup)
        self.assertIn("[bool]$verified.enabled -ne $preserveEnabled", setup)
        self.assertNotIn("hermes chat", setup.lower())
        for worker in (
            "run-direct-glitch-cycle.py",
            "reconcile-hermes-outcomes.py",
            "run-hermes-learning-cycle.py",
            "launch-hermes-learning-cycle.py",
            "ensure-named-sessions.py",
        ):
            self.assertIn(worker, builder)

    def test_trade_command_controls_operator_and_learning_together(self):
        plugin = (ROOT / "hermes-profile/plugins/glitch-control/__init__.py").read_text(encoding="utf-8")
        self.assertIn('JOB_NAMES = ("glitch-direct-operator", "glitch-learning-supervisor")', plugin)
        self.assertIn('"trade": (_trade,', plugin)
        self.assertIn('"trade-mode": (_trade_mode,', plugin)
        self.assertIn("no longer changes account authority", plugin)
        self.assertNotIn('state.get("mode")', plugin)

    def test_loop_model_routing_and_ai_auto_activation(self):
        loops = {item["id"]: item for item in self.operator["loops"]}
        self.assertEqual(loops["core_decision"]["model"], "gpt-5.6-luna")
        for loop_id in loops:
            self.assertEqual(loops[loop_id]["initial_activation"], "managed_by_ai_auto")
        for loop_id in ("trade_debrief", "portfolio_supervision", "portfolio_planning", "daily_learning"):
            self.assertEqual(loops[loop_id]["model"], "gpt-5.6-sol")
        self.assertEqual(set(self.operator["activation"]["enable_now"]), {
            "trade_debrief", "portfolio_supervision", "portfolio_planning", "daily_learning",
        })

    def test_core_model_is_pinned_without_silent_fallback(self):
        installer = (ROOT / "tools/hermes/install-direct-hermes-bridge.ps1").read_text(encoding="utf-8")
        runner = (ROOT / "tools/hermes/run-direct-glitch-cycle.py").read_text(encoding="utf-8")
        self.assertIn("config set model.default gpt-5.6-luna", installer)
        self.assertIn("config set model.provider openai-codex", installer)
        self.assertIn("config set agent.reasoning_effort medium", installer)
        self.assertIn("['gpt-5.6-sol']='high'", installer)
        self.assertIn("_write_chain; c=load_config(); _write_chain(c, [])", installer)
        self.assertIn('CORE_MODEL = "gpt-5.6-luna"', runner)
        self.assertIn('CORE_PROVIDER = "openai-codex"', runner)
        self.assertIn('"--max-turns", "4"', runner)

    def test_patterns_are_evidence_not_deterministic_gates(self):
        lowered = self.cognitive_map.lower()
        self.assertIn("archetypes, mined patterns", lowered)
        self.assertIn("are evidence", lowered)
        self.assertIn("no_archetype_match", lowered)
        self.assertNotIn("no match → nothing", lowered)
        self.assertNotIn("statuses are law", lowered)

    def test_self_learning_and_self_heal_are_installed_capabilities(self):
        installed = self.operator["skills"]["installed_overlay"]
        required = self.operator["skills"]["required_before_supervisory_activation"]
        self.assertIn("glitch-self-learning", installed)
        self.assertIn("glitch-self-heal", installed)
        self.assertIn("glitch-learning-loop", installed)
        self.assertNotIn("glitch-self-heal", required)
        self.assertEqual(required, [])

        heal = (ROOT / "hermes-profile/skills/glitch-self-heal/SKILL.md").read_text(encoding="utf-8")
        learn = (ROOT / "hermes-profile/skills/glitch-self-learning/SKILL.md").read_text(encoding="utf-8")
        self.assertIn("Never reset an account", heal)
        self.assertIn("Never fabricate a fill", heal)
        self.assertIn("self-heal does not wait", heal)
        self.assertIn("Never erase the earlier lesson", learn)
        self.assertIn("memory as interpretations", learn)


if __name__ == "__main__":
    unittest.main()
