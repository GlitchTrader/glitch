import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[3]
UI = ROOT / "ninjatrader" / "Glitch" / "AddOns" / "GlitchAddOn" / "UI" / "MainWindow"
POLICY = ROOT / "ninjatrader" / "Glitch" / "AddOns" / "GlitchAddOn" / "Services" / "Ai" / "GlitchAiRailPolicyStore.cs"
AI_AUTO = ROOT / "ninjatrader" / "Glitch" / "AddOns" / "GlitchAddOn" / "Services" / "Ai" / "GlitchAiAutoRuntimeController.cs"
CONTROL_PLUGIN = ROOT / "hermes-profile" / "plugins" / "glitch-control" / "__init__.py"


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

    def test_ai_on_requires_recent_native_cycle_health(self):
        main = (UI / "GlitchMainWindow.cs").read_text(encoding="utf-8")
        refresh = (UI / "GlitchMainWindow.RefreshPipeline.partial.cs").read_text(encoding="utf-8")
        self.assertIn('Value = "Stale"', main)
        self.assertIn('"AI Auto Stale"', main)
        self.assertIn("!paused && tradingJobEnabled && IsAiDecisionLoopHealthy()", main)
        self.assertIn('GlitchAiAutoRuntimeController.IsTradingJobEnabled()', main)
        self.assertIn('Path.Combine("hermes", "exchange", "hermes", "events", "cycles.jsonl")', main)
        self.assertIn("TimeSpan.FromMinutes(12)", main)
        self.assertIn("UpdateHermesModeUi", refresh)

    def test_scope_is_policy_binding_not_a_second_group_model(self):
        source = POLICY.read_text(encoding="utf-8")
        self.assertIn("TrySaveTradingScope", source)
        self.assertIn('ReplaceStringArray(json, "profile_account_bindings"', source)
        self.assertIn('ReplaceStringArray(json, "account_allowlist"', source)
        self.assertNotIn("AiAccountGroup", source)

    def test_ai_switch_owns_the_native_job_without_running_a_model(self):
        main = (UI / "GlitchMainWindow.cs").read_text(encoding="utf-8")
        bridge = AI_AUTO.read_text(encoding="utf-8")
        plugin = CONTROL_PLUGIN.read_text(encoding="utf-8")

        self.assertIn("GlitchAiAutoRuntimeController.SetEnabledAsync(targetEnabled)", main)
        self.assertIn("state.TradingPaused = true;", main)
        self.assertLess(
            main.index("state.TradingPaused = true;", main.index("private async void OnAiTradingButtonClick")),
            main.index("SetEnabledAsync(targetEnabled)", main.index("private async void OnAiTradingButtonClick")),
        )
        self.assertIn('Arguments = QuoteArgument(controlPluginPath) + " ai-auto "', bridge)
        self.assertIn("CreateNoWindow = true", bridge)
        self.assertIn("It never runs a model itself", bridge)
        self.assertIn('arguments[0] != "ai-auto"', plugin)
        self.assertIn('_trade_mode("") if arguments[1] == "on" else _pause_trading("")', plugin)


if __name__ == "__main__":
    unittest.main()
