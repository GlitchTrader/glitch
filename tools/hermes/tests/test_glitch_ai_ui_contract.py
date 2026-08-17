import pathlib
import unittest
from profile_root import PROFILE_ROOT


ROOT = pathlib.Path(__file__).resolve().parents[3]
UI = ROOT / "ninjatrader" / "Glitch" / "AddOns" / "GlitchAddOn" / "UI" / "MainWindow"
POLICY = ROOT / "ninjatrader" / "Glitch" / "AddOns" / "GlitchAddOn" / "Services" / "Ai" / "GlitchAiRailPolicyStore.cs"
AI_AUTO = ROOT / "ninjatrader" / "Glitch" / "AddOns" / "GlitchAddOn" / "Services" / "Ai" / "GlitchAiAutoRuntimeController.cs"
CONTROL_PLUGIN = PROFILE_ROOT / "plugins" / "glitch-control" / "__init__.py"


class GlitchAiUiContractTests(unittest.TestCase):
    def test_ai_tab_reuses_groups_and_durable_artifacts(self):
        source = (UI / "GlitchMainWindow.AiTab.partial.cs").read_text(encoding="utf-8")
        self.assertIn("AI Trading Scope", source)
        self.assertIn('CreateAccordionExpander(root, "ai.scope.title", "AI Trading Scope")', source)
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
        self.assertIn('L("header.button.ai_auto_off", "Glitch AI")', main)
        self.assertIn('L("header.button.ai_auto_on", "AI Auto On")', main)
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
        self.assertIn('"ai.feed.health_status_format"', source)
        self.assertIn('"AI {0}: {1}  |  Latest snapshot {2}  |  Latest decision {3}"', source)
        self.assertNotIn('"Last cycle "', source)
        self.assertIn("AiDecisionHistoryLimit = 20", source)
        self.assertIn("AiDecisionHistoryScanLimit = 2000", source)
        self.assertIn("CoalesceAiDecisionHistoryLines(", source)
        self.assertIn('return "pending_native_result";', source)
        self.assertIn("AiExecutionEvidencePriority(", source)
        self.assertIn('code.EndsWith("_fill_observed"', source)
        self.assertIn("CreateDisclosureRowExpander(_aiFeedHost, headerText)", source)
        self.assertNotIn("var header = new Grid", source)
        self.assertIn('L("ai.snapshots.supporting", "Supporting Snapshots")', source)
        self.assertIn('GetAiJsonString(value, "instrument"), "MNQ"', source)

    def test_ai_cadence_warning_follows_worker_health_not_decision_age(self):
        source = (UI / "GlitchMainWindow.AiTab.partial.cs").read_text(encoding="utf-8")
        self.assertIn("IsAiDecisionWorkerUnhealthy(health)", source)
        self.assertIn('code.StartsWith("decision_worker_", StringComparison.Ordinal)', source)
        self.assertNotIn(
            "nowUtc - latestDecisionUtc.Value > TimeSpan.FromMinutes(12)",
            source,
        )
        self.assertNotIn("if (age <= TimeSpan.FromMinutes(12))", source)

    def test_ai_feed_loads_off_dispatcher_and_uses_bounded_cycle_lookup(self):
        source = (UI / "GlitchMainWindow.AiTab.partial.cs").read_text(encoding="utf-8")
        self.assertIn("await Task.Run(BuildAiTabRefreshSnapshot)", source)
        self.assertIn("Interlocked.CompareExchange(ref _aiTabRefreshInFlight", source)
        self.assertIn("AiTabRefreshMinInterval = TimeSpan.FromSeconds(2)", source)
        self.assertIn("FindAiDecisionPacket(", source)
        self.assertIn('string outboxRoot = Path.Combine(exchangeRoot, "hermes", "outbox")', source)
        self.assertIn('GlitchAiJsonFields.ExtractString(decision, "snapshot_hash")', source)
        self.assertIn("ReadAiPacketFinalSnapshotHash(packetPath)", source)
        self.assertIn("CoalesceAiDecisionHistoryLines(", source)
        history_loader = source[
            source.index("private List<AiDecisionFeedItem> LoadAiDecisionHistory"):
            source.index("private static string ReadAiPacketFinalSnapshotHash")
        ]
        self.assertNotIn("GetFiles(\"*.json\")", history_loader)
        self.assertNotIn("File.ReadLines", history_loader)
        self.assertIn("FileShare.ReadWrite | FileShare.Delete", source)
        self.assertIn('_aiDecisionHistoryPacketFingerprint ?? "0"', source)

    def test_periodic_maintenance_is_not_run_inline_by_the_dispatcher_tick(self):
        main = (UI / "GlitchMainWindow.cs").read_text(encoding="utf-8")
        performance = (UI / "GlitchMainWindow.Performance.partial.cs").read_text(encoding="utf-8")
        tick = main[
            main.index("private void OnRefreshTimerTickCore"):
            main.index("private void OnAccountsGridBeginningEdit")
        ]
        self.assertLess(
            tick.index("CaptureRailNativeStateIfDue(nowUtc);"),
            tick.index("QueueBackgroundMaintenance(nowUtc);"),
        )
        self.assertIn("GlitchRailSelfCheckWriter.CaptureNativeConnectionState();", performance)
        self.assertIn("QueueBackgroundMaintenance(nowUtc);", tick)
        for direct_call in (
            "GlitchHistoricalSnapshotExporter.TryWriteReplayBundleIfDue",
            "GlitchRailSelfCheckWriter.TryWriteIfDue",
            "GlitchSnapshotSanityWriter.TryWriteIfDue",
            "GlitchAiReplayHarnessWriter.TryWriteIfDue",
        ):
            self.assertNotIn(direct_call, tick)
            self.assertIn(direct_call, performance)
        self.assertIn("Task.Run(() =>", performance)
        self.assertIn("Interlocked.CompareExchange(ref _backgroundMaintenanceInFlight", performance)

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

    def test_journal_refresh_does_not_require_removed_summary_tab_controls(self):
        source = (UI / "GlitchMainWindow.SummaryTab.partial.cs").read_text(encoding="utf-8")
        refresh = source[source.index("private void RefreshSummaryInsightsCore"):]
        for control in (
            "_summaryTradesValueText",
            "_summaryWinRateValueText",
            "_summaryNetPointsValueText",
            "_summaryProfitFactorValueText",
            "_summaryAccountsValueText",
            "_summaryAsOfText",
        ):
            self.assertIn(f"if ({control} != null)", refresh)
        self.assertIn("if (_journalTradesValueText != null)", refresh)
        self.assertIn("_summaryMetricRows.Clear();", refresh)

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
        self.assertIn('_trade("") if arguments[1] == "on" else _pause_trading("")', plugin)
        self.assertIn('"trade": (_trade,', plugin)
        self.assertIn('"trade-mode": (_trade_mode,', plugin)


if __name__ == "__main__":
    unittest.main()
