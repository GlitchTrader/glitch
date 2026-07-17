"""Durable source boundaries shared by main and AI rails."""

import json
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ADDON = ROOT / "ninjatrader/Glitch/AddOns/GlitchAddOn"
INDICATORS = ROOT / "ninjatrader/Glitch/Indicators/glitch"

COPY_ENGINE = ADDON / "Services/Trading/GlitchCopyEngine.cs"
PROTECTION = ADDON / "Services/Trading/GlitchReplicationProtection.cs"
MAIN_WINDOW = ADDON / "UI/MainWindow/GlitchMainWindow.cs"
REPLICATION_UI = ADDON / "UI/MainWindow/GlitchMainWindow.Replication.partial.cs"
POLICY_STORE = ADDON / "Services/Persistence/GlitchRuntimePolicyStore.cs"
TRADE_INSIGHTS = ADDON / "Services/Insights/GlitchTradeInsightsService.cs"
SUMMARY_TAB = ADDON / "UI/MainWindow/GlitchMainWindow.SummaryTab.partial.cs"
METADATA = ADDON / "Services/Trading/GlitchInstrumentMetadataService.cs"
FEED_BUS = ADDON / "UI/Analytics/GlitchAnalyticsFeedBus.cs"
ANALYTICS_BRIDGE = INDICATORS / "GlitchAnalyticsBridge.cs"
PROP_RULES = ADDON / "Resources/PropFirmRules.json"


def source(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def method_body(text: str, signature: str, next_signature: str) -> str:
    return text.split(signature, 1)[1].split(next_signature, 1)[0]


class SharedSourceArchitectureContractTests(unittest.TestCase):
    def test_automation_eligibility_is_not_an_execution_gate(self):
        text = "\n".join(source(path) for path in ADDON.rglob("*.cs")) + source(PROP_RULES)
        self.assertNotIn("automatedTradingAllowed", text)
        self.assertNotIn("firm_automation_prohibited", text)

    def test_replication_core_is_producer_neutral(self):
        text = source(COPY_ENGINE) + source(PROTECTION)
        for forbidden in (
            "Services.Ai",
            "GlitchAiOrderExecutor",
            "GlitchAiRailPolicyStore",
            "GlitchHermes",
            "Hermes",
            "GLT-AI",
        ):
            self.assertNotIn(forbidden, text)

    def test_replication_reads_native_collections_through_locked_snapshots(self):
        text = source(ADDON / "Services/Trading/GlitchReplicationEngine.cs")
        self.assertIn("lock (account.Positions)", text)
        self.assertIn("lock (account.Orders)", text)
        self.assertNotIn("foreach (Position position in account.Positions)", text)
        flat = method_body(text, "public static bool IsAccountFlat", "public static bool HasAnyWorkingOrders")
        working = method_body(text, "public static bool HasAnyWorkingOrders", "public static async Task<bool> WaitForAllAccountsFlatAsync")
        self.assertIn("catch\n            {\n                return false;", flat)
        self.assertIn("catch\n            {\n                return true;", working)

    def test_copy_entries_require_complete_native_master_brackets(self):
        text = source(COPY_ENGINE)
        self.assertIn("TryResolveMasterPlan", text)
        self.assertIn("PendingMasterCopyTtl", text)
        self.assertIn("SubmitProtectionUnits", text)
        self.assertIn("OrderType.StopMarket", text)
        self.assertIn("OrderType.Limit", text)
        self.assertIn("OrderEntry.Automated", text)

    def test_each_follower_unit_has_an_independent_native_oco_pair(self):
        body = method_body(
            source(COPY_ENGINE),
            "private bool SubmitProtectionUnits",
            "private bool TryRecoverRecentFollowerLifecycle",
        )
        self.assertIn("for (int unitIndex = fromQuantity; unitIndex < toQuantity; unitIndex++)", body)
        self.assertIn("string oco =", body)
        self.assertGreaterEqual(body.count("\n                    1,"), 2)

    def test_multi_leg_stop_identity_is_native_oco_not_trade_correlation(self):
        body = method_body(
            source(PROTECTION),
            "public static string BuildSourceToken",
            "public static string StableToken",
        )
        self.assertIn("oco.Trim()", body)
        self.assertNotIn("TryGetSignalCorrelation", body)

    def test_master_bracket_fills_are_not_double_copied(self):
        body = method_body(
            source(COPY_ENGINE),
            "public void ProcessMasterExecution",
            "public void ProcessMasterOrderUpdate",
        )
        self.assertIn("IsMasterProtectionExecution", body)
        self.assertIn("return;", body)

    def test_reload_recovery_is_recent_and_non_mutating_when_old(self):
        body = method_body(
            source(COPY_ENGINE),
            "public void ProcessFollowerOrderUpdate",
            "public void ProcessAccountStateUpdate",
        )
        self.assertIn("HasCompleteFollowerProtectionForCurrentPosition", body)
        self.assertIn("IsRecentOrder(order, TimeSpan.FromMinutes(2))", body)
        self.assertIn("TryRecoverRecentFollowerLifecycle", body)
        self.assertIn("Existing orders were not changed", body)
        self.assertNotIn("RequestFollowerFlattenOnce", body.split("int protectFrom;", 1)[0])
        self.assertIn("order.OrderAction == expectedExitAction", source(COPY_ENGINE))

    def test_ambiguous_submission_is_not_blindly_retried(self):
        text = source(COPY_ENGINE)
        self.assertNotRegex(text, r"(?i)submit\w*withretry|retry\w*submit")
        self.assertIn("will not retry", text.lower())

    def test_follower_fill_is_marked_protected_only_after_submission(self):
        body = method_body(
            source(COPY_ENGINE),
            "public void ProcessFollowerOrderUpdate",
            "public void ProcessAccountStateUpdate",
        )
        submit = body.index("SubmitProtectionUnits")
        committed = body.index("lifecycle.ProtectedQuantity = protectTo")
        self.assertGreater(committed, submit)
        self.assertIn("ProtectionSubmissionInProgress", body)
        self.assertIn("lifecycle.ProtectionFailed = true", body)
        self.assertIn("Math.Max(0, order.Filled) > protectTo", body)
        self.assertIn("ProcessFollowerOrderUpdate(followerAccount, order)", body)

    def test_async_protection_rejection_fails_closed_without_retrying_or_owning_cancellation(self):
        body = method_body(
            source(COPY_ENGINE),
            "private void ProcessFollowerProtectionOrderUpdate",
            "public void ProcessAccountStateUpdate",
        )
        self.assertIn("OrderState.Rejected", body)
        self.assertNotIn("OrderState.Cancelled", body)
        self.assertIn("lifecycle.ProtectionFailed = true", body)
        self.assertIn("RequestFollowerFlattenOnce", body)
        self.assertIn("no order was retried", body)

    def test_replication_off_preserves_existing_protection(self):
        toggle = method_body(
            source(MAIN_WINDOW),
            "internal bool SetReplicationFromExternalSurface",
            "internal void ToggleReplicationFromExternalSurface",
        )
        self.assertNotRegex(toggle, r"Cancel.*(Follower|Protection|Order)")
        replication = source(REPLICATION_UI)
        self.assertIn("ProcessFollowerOrderUpdate", replication)
        self.assertIn("ProcessAccountStateUpdate", replication)

    def test_replication_state_is_truthful_and_reload_is_observe_only(self):
        window = source(MAIN_WINDOW)
        self.assertIn("_isReplicatingUi && _copyEngine?.IsEnabled == true", window)
        self.assertIn("SetReplicationFromExternalSurface(!IsReplicationEnabledFromExternalSurface()", window)
        self.assertNotIn("UseLegacyReplicationEngine", window + source(POLICY_STORE))
        self.assertNotIn('AlignAllEnabledFollowersToMaster("startup")', window)
        self.assertIn("replication_restored|origin=startup|catchup=skipped", window)

    def test_follower_cleanup_is_narrowly_owned(self):
        text = source(COPY_ENGINE)
        self.assertIn('StartsWith(CopySignalName + "-E-"', text)
        self.assertIn('StartsWith(CatchUpSignalName + "-E-"', text)
        self.assertNotRegex(text, r"StartsWith\(\s*\"GLT-\"")
        self.assertNotRegex(text, r"Cancel.*Unknown|Flatten.*Unknown")

    def test_flatten_all_requires_resolved_accounts_and_one_native_submission(self):
        window = source(MAIN_WINDOW)
        replication = source(REPLICATION_UI)
        flatten = method_body(
            window,
            "private async Task<bool> ExecuteFlattenAllCoreAsync",
            "private void OnCreateGroupClick",
        ) + method_body(
            replication,
            "private List<Account> ResolveFlattenAllAccounts",
            "private int IssueFlattenOrdersForAccounts",
        )
        self.assertIn("could not positively resolve any accounts", flatten)
        self.assertIn("unresolvedAccounts.Count == 0", flatten)
        issue = method_body(
            replication,
            "private int IssueFlattenOrdersForAccounts",
            "private bool AreAccountsFlatAndClear",
        )
        self.assertEqual(issue.count("TryFlattenAccount("), 1)

    def test_journal_replay_ignores_orphan_exits_and_splits_reversal_commission(self):
        text = source(TRADE_INSIGHTS)
        self.assertIn("if (!IsOpeningAction(evt.Action))", text)
        self.assertIn("AccumulateExecutionCommission(state, evt, closeQty / executionQuantity)", text)
        self.assertIn("AccumulateExecutionCommission(states[key], evt, remainder / executionQuantity)", text)

    def test_currency_pnl_never_uses_unknown_point_value(self):
        summary = source(SUMMARY_TAB)
        metadata = source(METADATA)
        self.assertIn("TryGetPointValue", summary)
        self.assertIn("omitted from currency PnL", summary)
        self.assertNotRegex(summary, r"pointValue\s*=\s*1(?:\.0)?\s*;")
        self.assertIn("TryResolve", metadata)
        self.assertIn("CacheMetadata(BuildFromInstrument(root, instrument))", metadata)
        self.assertIn("Cache.TryGetValue(root, out metadata) && metadata.IsResolved", metadata)

    def test_verified_apex_rules_are_consistent_across_programs(self):
        rules = {firm["firmId"]: firm for firm in json.loads(source(PROP_RULES))["firms"]}
        for firm_id in ("ApexTraderFunding", "ApexEod", "ApexIntraday"):
            self.assertTrue(rules[firm_id]["enforcementSemantics"]["directionalTradingOnly"])
        self.assertEqual(
            rules["ApexTraderFunding"]["enforcementSemantics"]["consistencyRulePercent"],
            30.0,
        )

    def test_journal_scope_and_card_units_are_explicit(self):
        summary = source(SUMMARY_TAB)
        self.assertIn('ItemsSource = new[] { "Master", "Group", "Fleet" }', summary)
        self.assertIn('"Logical Trades"', summary)
        self.assertIn('"Account Trades"', summary)
        self.assertIn("ApplySummaryScope", summary)
        self.assertNotIn("_summaryFleetTradesValueText.Text = FormatSignedCurrency", summary)
        self.assertNotIn("_summaryAccountsValueText.Text = FormatSignedCurrency", summary)

    def test_analytics_observations_are_live_and_rich(self):
        bridge = source(ANALYTICS_BRIDGE)
        feed = source(FEED_BUS)
        for field in (
            "InstrumentFullName",
            "Open",
            "High",
            "Low",
            "Volume",
            "DiPlus",
            "DiMinus",
            "Cci",
            "MacdHistogram",
        ):
            self.assertIn(field, bridge)
            self.assertIn(field, feed)
        self.assertIn("clone.Open = NormalizePositiveFinite", feed)
        self.assertIn("clone.Volume = NormalizeFinite", feed)
        self.assertRegex(bridge, r"UtcTime\s*=\s*(?:DateTime\.UtcNow|nowUtc|readingUtc)")


if __name__ == "__main__":
    unittest.main()
