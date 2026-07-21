"""Durable source boundaries shared by main and AI rails."""

import base64
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
APEX_DIRECTION_GUARD = ADDON / "Services/Risk/GlitchApexDirectionGuard.cs"
PROP_RULE_BUNDLE = ADDON / "UI/MainWindow/GlitchMainWindow.PropFirmRulesBundle.generated.cs"
PROP_RULE_GENERATOR = ROOT / "scripts/generate_bundled_prop_rules.ps1"
FUNDAMENTAL_ANALYSIS = ADDON / "Services/FundamentalAnalysis/GlitchFundamentalAnalysisService.cs"


def source(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def method_body(text: str, signature: str, next_signature: str) -> str:
    return text.split(signature, 1)[1].split(next_signature, 1)[0]


class SharedSourceArchitectureContractTests(unittest.TestCase):
    def test_follower_recovery_never_accepts_unscoped_instrument_protection(self):
        copy_engine = source(COPY_ENGINE)
        self.assertNotIn("HasCompleteFollowerProtectionForCurrentPosition", copy_engine)
        self.assertNotIn("restored_native_orders_observed", copy_engine)
        self.assertIn("TryRecoverRecentFollowerLifecycle", copy_engine)
        self.assertIn("TryCountCompleteFollowerProtection(followerAccount, entryOrder.Instrument, entryToken", copy_engine)

    def test_automation_eligibility_is_not_an_execution_gate(self):
        text = "\n".join(source(path) for path in ADDON.rglob("*.cs")) + source(PROP_RULES)
        self.assertNotIn("automatedTradingAllowed", text)
        self.assertNotIn("firm_automation_prohibited", text)

    def test_fred_release_rows_are_context_not_live_compliance_alerts(self):
        text = source(FUNDAMENTAL_ANALYSIS)
        lockout = method_body(text, "private NewsLockoutState BuildLockoutState", "private List<string> BuildOfficialNewsLines")
        self.assertIn('!string.Equals(x.Source, "FRED", StringComparison.OrdinalIgnoreCase)', lockout)
        self.assertIn("sourceEvents", text)

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
        self.assertIn("!TrySnapshotPositions", flat)
        self.assertIn("return false;", flat)
        self.assertIn("!TrySnapshotOrders", working)
        self.assertIn("return true;", working)
        self.assertIn("TryGetNetQuantityForInstrumentRoot", text)
        self.assertIn("TryGetOpenPositionInstruments", text)

    def test_follower_submission_fails_closed_on_unknown_native_state_or_capacity(self):
        copy = source(COPY_ENGINE)
        fanout = method_body(copy, "private void FanOutOpening", "private void FanOutCompleteClose")
        submit = method_body(copy, "private void SubmitProtectedFollowerEntry", "private bool SubmitProtectionUnits")
        for token in (
            "copy_reject|native_state_unavailable",
            "copy_reject|contract_cap_unavailable",
            "copy_reject|max_contracts",
        ):
            self.assertIn(token, fanout)
        self.assertIn("FollowerFinalAdmissionUnavailable", submit)
        self.assertIn("TryGetTotalOpenContracts", copy)
        self.assertIn("TryGetInFlightOpeningQuantity", copy)
        self.assertIn("TryResolveRouteContractCap", copy)
        self.assertNotIn("GetEntryDenialReason", copy)
        self.assertNotIn("private static int GetTotalOpenContracts", copy)
        self.assertNotIn("private static Order[] SnapshotOrders", copy)

    def test_replication_suppresses_only_the_divergent_follower(self):
        copy = source(COPY_ENGINE)
        fanout = method_body(copy, "private void FanOutOpening", "private void FanOutCompleteClose")
        self.assertIn("expectedBeforeFill", fanout)
        self.assertIn("Math.Abs(actual) + inFlight != expectedBeforeFill", fanout)
        self.assertIn("SuppressFollowerRoot", fanout)
        self.assertIn("continue;", fanout)

    def test_explicit_resync_and_final_submit_use_the_configured_route_cap(self):
        copy = source(COPY_ENGINE)
        align = method_body(copy, "public void AlignFollowerToMaster", "private void FanOutOpening")
        submit = method_body(copy, "private void SubmitProtectedFollowerEntry", "private bool SubmitProtectionUnits")
        self.assertIn("FindConfiguredRoute(masterAccount, followerAccount)", align)
        self.assertIn("SubmitProtectedFollowerEntry(\n                    configuredRoute", align)
        self.assertIn("TryResolveRouteContractCap(route, instrumentRoot", submit)
        self.assertIn("totalOpen + totalInFlight + quantity", submit)
        self.assertIn("FollowerFinalMaxContracts", submit)

    def test_replication_requires_a_connected_master_and_truthful_active_route(self):
        copy = source(COPY_ENGINE)
        route_validation = method_body(copy, "private static bool IsValidRoute", "private static bool IsOpeningAction")
        self.assertIn("route.MasterAccountInstance != null", route_validation)
        replication = source(REPLICATION_UI)
        self.assertIn("MasterAccountInstance = masterAccount", replication)
        self.assertIn("_copyEngine.Configure(_isReplicatingUi, routes)", replication)

    def test_replication_route_uses_current_rule_projection_when_grid_cap_is_absent(self):
        replication = source(REPLICATION_UI)
        resolver = method_body(
            replication,
            "private int ResolveFollowerRouteContractCap",
            "private void AlignAllEnabledFollowersToMaster",
        )
        self.assertIn("BuildPortfolioSnapshotAccountRecord(row, account).MaxContracts", resolver)
        self.assertIn("double.IsNaN(contractCap)", resolver)
        self.assertIn("double.IsInfinity(contractCap)", resolver)
        self.assertIn("MaxContracts = followerMaxContracts", replication)
        self.assertNotIn("MaxContracts = followerRow?.MaxContractsRaw > 0", replication)

    def test_manual_follower_divergence_suppresses_only_automatic_reentry_until_resync(self):
        copy = source(COPY_ENGINE)
        opening = method_body(copy, "private void FanOutOpening", "private void FanOutCompleteClose")
        align = method_body(copy, "public void AlignFollowerToMaster", "private void FanOutOpening")
        self.assertIn("manual_or_external_divergence", opening)
        self.assertIn("SuppressFollowerRoot", opening)
        self.assertIn("explicit_resync_required", opening)
        self.assertIn("ClearFollowerRootSuppression", align)
        self.assertNotRegex(copy, r"(?i)quarantin")

    def test_follower_flatten_latch_releases_per_instrument_root(self):
        copy = source(COPY_ENGINE)
        cleanup = method_body(copy, "private void CleanupFlatFollowerOrders", "private void RequestFollowerFlattenOnce")
        self.assertIn("submittedRoots", cleanup)
        self.assertIn("_flattenSubmitted.Remove(accountPrefix + root)", cleanup)
        self.assertNotIn("if (GlitchReplicationEngine.IsAccountFlat(account))", cleanup)

    def test_copy_entries_require_complete_native_master_brackets(self):
        text = source(COPY_ENGINE)
        self.assertIn("TryResolveMasterPlan", text)
        self.assertIn("PendingMasterCopyTtl", text)
        self.assertIn("SubmitProtectionUnits", text)
        self.assertIn("OrderType.StopMarket", text)
        self.assertIn("OrderType.Limit", text)
        self.assertIn("OrderEntry.Automated", text)

    def test_partial_master_fills_wait_for_cumulative_position_and_are_not_order_id_deduped(self):
        copy = source(COPY_ENGINE)
        replication = source(REPLICATION_UI)
        self.assertIn("EntryOrderFilledQuantity", copy)
        self.assertIn("ResolveContextMasterQuantity(context)", copy)
        self.assertIn("Math.Abs(currentMasterNet) < copyMasterQuantity", copy)
        self.assertIn("Math.Abs(masterNet) < copyMasterQuantity", copy)
        self.assertIn('TryGetNestedPropertyValueAsString(executionObject, "ExecutionId")', replication)
        self.assertNotIn('TryGetNestedPropertyValueAsString(executionObject, "ExecutionId", "Id")', replication)
        self.assertIn("Math.Max(quantity, Math.Max(0, order.Filled))", replication)

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
        self.assertNotIn("HasCompleteFollowerProtectionForCurrentPosition", body)
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

    def test_copy_cleanup_waits_for_native_position_truth(self):
        body = method_body(
            source(REPLICATION_UI),
            "private void TryProcessReplicationOrderStateFromRuntimeEvent",
            "private List<Account> ResolveFlattenAllAccounts",
        )
        self.assertIn(
            'if (string.Equals(eventName, "PositionUpdate", StringComparison.OrdinalIgnoreCase))\n'
            "                _copyEngine.ProcessAccountStateUpdate(account);",
            body,
        )

    def test_glitch_trade_lifecycle_keeps_earliest_terminal_exit(self):
        ledger = source(ADDON / "Services/Insights/GlitchTradeLedgerService.cs")
        body = method_body(
            ledger,
            "private void NormalizeDuplicateTradesUnsafe",
            "private static string BuildExactDuplicateSignature",
        )
        self.assertIn("seenGlitchEntries", body)
        self.assertIn('entrySignal.StartsWith("GLT-"', body)
        self.assertIn(".OrderBy(pair => pair.Value?.ExitUtc", body)

    def test_follower_failure_evidence_is_trade_scoped_and_unambiguous(self):
        copy_engine = source(COPY_ENGINE)
        self.assertIn(
            '"FollowerProtectionRejected|" + root + "|" + CleanToken(lifecycle?.EntrySignal ?? signal)',
            copy_engine,
        )
        self.assertIn("|result=flatten_requested", copy_engine)
        self.assertNotIn("submitted_pending_confirmation", copy_engine)

    def test_replication_state_is_truthful_and_reload_is_observe_only(self):
        window = source(MAIN_WINDOW)
        performance = source(ADDON / "UI/MainWindow/GlitchMainWindow.Performance.partial.cs")
        chart_trader = source(ADDON / "GlitchAddOn.ChartTrader.partial.cs")
        self.assertIn("_isReplicatingUi && _copyEngine?.IsEnabled == true", window)
        self.assertIn("SetReplicationFromExternalSurface(!IsReplicationEnabledFromExternalSurface()", window)
        self.assertIn("IsReplicating = IsReplicationEnabledFromExternalSurface()", performance)
        self.assertIn("GlitchShellBridge.ToggleReplication()", chart_trader)
        self.assertNotIn("UseLegacyReplicationEngine", window + source(POLICY_STORE))
        self.assertNotIn('AlignAllEnabledFollowersToMaster("startup")', window)
        self.assertIn("replication_restored|origin=startup|catchup=skipped", window)

    def test_addon_and_chart_trader_flatten_all_share_one_fleet_command(self):
        chart_trader = source(ADDON / "GlitchAddOn.ChartTrader.partial.cs")
        shell = source(ADDON / "Services/GlitchShellBridge.cs")
        self.assertIn("GlitchShellBridge.FlattenAll()", chart_trader)
        self.assertIn("GlitchAddOn.RequestFlattenAll()", shell)
        self.assertIn("RunFlattenAllAsync(showHeaderButtonFeedback: true)", source(MAIN_WINDOW))

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

    def test_prop_rule_generator_is_workspace_relative_and_bundle_matches_json(self):
        generator = source(PROP_RULE_GENERATOR)
        self.assertIn("$PSScriptRoot", generator)
        self.assertNotRegex(generator, r"(?i)[a-z]:\\")
        bundle = source(PROP_RULE_BUNDLE)
        encoded_block = bundle.split("const string base64 =", 1)[1].split(";", 1)[0]
        encoded = "".join(re.findall(r'\"([A-Za-z0-9+/=]+)\"', encoded_block))
        self.assertEqual(base64.b64decode(encoded), PROP_RULES.read_bytes())

    def test_all_apex_programs_surface_current_copy_policy_information(self):
        rules = {firm["firmId"]: firm for firm in json.loads(source(PROP_RULES))["firms"]}
        for firm_id in ("ApexTraderFunding", "ApexEod", "ApexIntraday", "WealthCharts"):
            policy = rules[firm_id]["copyTradingPolicy"]
            self.assertEqual(policy["allowed"], "conditional")
            self.assertTrue(policy["sameOwnerOnly"])
            self.assertEqual(
                policy["sourceUrl"],
                "https://dashboard.apextraderfunding.com/agreement/user-agreement",
            )
            self.assertIn("autonomous AI/automation is prohibited", policy["notes"])

    def test_glitch_generated_apex_entries_fail_closed_on_cross_direction_state(self):
        guard = source(APEX_DIRECTION_GUARD)
        copy = source(COPY_ENGINE)
        self.assertIn("TrySnapshotPositions", guard)
        self.assertIn("TrySnapshotOrders", guard)
        self.assertIn("apex_cross_direction_conflict", guard)
        self.assertIn("GlitchApexDirectionGuard.TryApproveEntry", copy)
        self.assertIn("Glitch preserved the human position", copy)

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
