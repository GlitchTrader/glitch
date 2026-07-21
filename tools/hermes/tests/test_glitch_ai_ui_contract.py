import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[3]
UI = ROOT / "ninjatrader" / "Glitch" / "AddOns" / "GlitchAddOn" / "UI" / "MainWindow"
POLICY = ROOT / "ninjatrader" / "Glitch" / "AddOns" / "GlitchAddOn" / "Services" / "Ai" / "GlitchAiRailPolicyStore.cs"


class GlitchAiUiContractTests(unittest.TestCase):
    def test_ai_tab_reuses_groups_and_durable_artifacts(self):
        source = (UI / "GlitchMainWindow.AiTab.partial.cs").read_text(encoding="utf-8")
        self.assertIn("AI Trading Scope", source)
        self.assertIn("_accountGroups", source)
        self.assertIn('Path.Combine("intents", "decisions.jsonl")', source)
        self.assertIn('Path.Combine("intents", "executions.jsonl")', source)
        self.assertNotIn("DispatcherTimer", source)
        self.assertNotIn("HttpClient", source)

    def test_header_has_one_ai_switch_and_no_runtime_brand_or_mode(self):
        header = (UI / "GlitchMainWindow.Header.partial.cs").read_text(encoding="utf-8")
        main = (UI / "GlitchMainWindow.cs").read_text(encoding="utf-8")
        self.assertIn("_aiTradingButton", header)
        self.assertIn('"Glitch AI", "AI Auto On"', main)
        self.assertNotIn('"Hermes"', header)
        self.assertNotIn("ON / Paper", main)

    def test_ai_switch_distinguishes_stopped_running_and_stale(self):
        main = (UI / "GlitchMainWindow.cs").read_text(encoding="utf-8")
        refresh = (UI / "GlitchMainWindow.RefreshPipeline.partial.cs").read_text(encoding="utf-8")
        self.assertIn('Value = "Stale"', main)
        self.assertIn('"AI Auto Stale"', main)
        self.assertIn("IsAiDecisionLoopHealthy() ? \"Running\" : \"Stale\"", main)
        self.assertIn('Path.Combine("hermes", "exchange", "hermes", "events", "cycles.jsonl")', main)
        self.assertIn("TimeSpan.FromMinutes(12)", main)
        self.assertIn("GlitchAiOrderExecutor.IsExecutionEnabled(policy)", main)
        self.assertIn("UpdateHermesModeUi", refresh)

    def test_ai_feed_uses_account_scope_not_paper_live_label(self):
        source = (UI / "GlitchMainWindow.AiTab.partial.cs").read_text(encoding="utf-8")
        self.assertIn("AI scope ", source)
        self.assertIn("AI config invalid", source)
        self.assertNotIn("effectivePolicy.Mode", source)

    def test_scope_is_policy_binding_not_a_second_group_model(self):
        source = POLICY.read_text(encoding="utf-8")
        self.assertIn("TrySaveTradingScope", source)
        self.assertIn('ReplaceStringArray(json, "profile_account_bindings"', source)
        self.assertIn('ReplaceStringArray(json, "account_allowlist"', source)
        self.assertNotIn("AiAccountGroup", source)


if __name__ == "__main__":
    unittest.main()
