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
        self.assertIn('CreateAccordionExpander(root, "AI Trading Scope")', source)
        self.assertIn("scopeExpander.IsExpanded = false", source)
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

    def test_ai_switch_reports_the_actual_control_and_job_state_without_a_false_stale_mode(self):
        main = (UI / "GlitchMainWindow.cs").read_text(encoding="utf-8")
        refresh = (UI / "GlitchMainWindow.RefreshPipeline.partial.cs").read_text(encoding="utf-8")
        self.assertNotIn('Value = "Stale"', main)
        self.assertNotIn('"AI Auto Stale"', main)
        self.assertIn("_aiTradingButton.Tag = !paused && tradingJobEnabled", main)
        self.assertIn('GlitchAiAutoRuntimeController.IsTradingJobEnabled()', main)
        self.assertIn("UpdateHermesModeUi", refresh)

    def test_ai_feed_separates_current_collection_from_completed_decisions(self):
        source = (UI / "GlitchMainWindow.AiTab.partial.cs").read_text(encoding="utf-8")
        self.assertIn('"Current Window"', source)
        self.assertIn('"Latest AI Decision"', source)
        self.assertIn('"Latest snapshot " + snapshotAge + "  |  Latest decision "', source)
        self.assertNotIn('"Last cycle "', source)
        self.assertIn("AiDecisionHistoryLimit = 20", source)
        self.assertIn("CreateDisclosureRowExpander(_aiFeedHost, headerText)", source)
        self.assertNotIn("var header = new Grid", source)
        self.assertIn('"SUPPORTING SNAPSHOTS"', source)
        self.assertIn('GetAiJsonString(value, "instrument"), "MNQ"', source)

    def test_shared_ui_hierarchy_uses_boxed_sections_and_compact_disclosure_rows(self):
        accordion = (UI / "GlitchMainWindow.AccordionLayout.partial.cs").read_text(encoding="utf-8")
        settings = (UI / "GlitchMainWindow.SettingsTab.partial.cs").read_text(encoding="utf-8")

        self.assertIn('"BackgroundTableHeader", "BackgroundTextInput", "BackgroundMainWindow"', accordion)
        self.assertIn("Control.BorderThicknessProperty, new Thickness(1)", accordion)
        self.assertIn("CreateDisclosureRowExpander", accordion)
        self.assertIn("WrapDisclosureRowContent", accordion)
        self.assertIn('CreateAccordionExpander(root, "settings.risk.title", "Risk Management Rules")', settings)
        self.assertIn('CreateAccordionExpander(root, "settings.license.title", "License & Updates")', settings)
        self.assertIn("CreateDisclosureRowExpander(GetSettingsStyleContext(), titleKey, descriptionFallback)", settings)
        self.assertNotIn("var expander = new Expander", settings)

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
