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
LOCALIZATION = ADDON / "Resources/Localization.tsv"
POLICY_STORE = ADDON / "Services/Persistence/GlitchRuntimePolicyStore.cs"
TRADE_INSIGHTS = ADDON / "Services/Insights/GlitchTradeInsightsService.cs"
SUMMARY_TAB = ADDON / "UI/MainWindow/GlitchMainWindow.SummaryTab.partial.cs"
METADATA = ADDON / "Services/Trading/GlitchInstrumentMetadataService.cs"
FEED_BUS = ADDON / "UI/Analytics/GlitchAnalyticsFeedBus.cs"
ANALYTICS_BRIDGE = INDICATORS / "GlitchAnalyticsBridge.cs"
PROP_RULES = ADDON / "Resources/PropFirmRules.json"
PROP_RULE_BUNDLE = ADDON / "UI/MainWindow/GlitchMainWindow.PropFirmRulesBundle.generated.cs"
PROP_RULE_GENERATOR = ROOT / "scripts/generate_bundled_prop_rules.ps1"
FUNDAMENTAL_ANALYSIS = ADDON / "Services/FundamentalAnalysis/GlitchFundamentalAnalysisService.cs"
DOWNLOAD_APP = ROOT / "apps/download"
RELEASE_CATALOG = DOWNLOAD_APP / "src/lib/release-catalog.json"
RELEASE_CHECKSUMS = DOWNLOAD_APP / "public/files/checksums.json"
RELEASES_LIB = DOWNLOAD_APP / "src/lib/releases.ts"
RELEASE_VALIDATOR = DOWNLOAD_APP / "scripts/validate-releases.mjs"
RELEASE_PUBLISHER = ROOT / "scripts/publish-release.ps1"
ADDON_UPDATE = ROOT / "apps/api/src/lib/addon-update.ts"


def source(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def method_body(text: str, signature: str, next_signature: str) -> str:
    return text.split(signature, 1)[1].split(next_signature, 1)[0]


class SharedSourceArchitectureContractTests(unittest.TestCase):
    def test_downloads_are_owned_by_an_explicit_edition_catalog(self):
        catalog = json.loads(source(RELEASE_CATALOG))
        checksums = json.loads(source(RELEASE_CHECKSUMS))
        zip_names = {path.name for path in (DOWNLOAD_APP / "public/files").glob("*.zip")}
        catalog_names = {entry["fileName"] for entry in catalog}

        self.assertEqual(zip_names, catalog_names)
        self.assertEqual(set(checksums), catalog_names)
        self.assertTrue(all(entry["edition"] in {"standard", "ai"} for entry in catalog))
        self.assertTrue(all(re.fullmatch(r"[0-9a-f]{40}", entry["sourceCommit"]) for entry in catalog))

        releases = source(RELEASES_LIB)
        self.assertIn('releaseCatalog from "./release-catalog.json"', releases)
        self.assertNotIn("deriveVersion", releases)
        self.assertNotIn("zipFiles", releases)
        self.assertIn('edition: ReleaseEdition = "standard"', releases)
        self.assertIn("release.slug === normalizedSlug", releases)
        self.assertIn("release.edition === defaultEdition", releases)

    def test_release_validation_and_publisher_fail_closed(self):
        validator = source(RELEASE_VALIDATOR)
        publisher = source(RELEASE_PUBLISHER)
        self.assertIn("unregistered ZIP files are not publishable", validator)
        self.assertIn("does not match checksums.json", validator)
        self.assertIn("Refusing to overwrite existing release", publisher)
        self.assertIn("Expected exactly three NinjaTrader export entries", publisher)
        self.assertIn("if ($Edition -eq 'ai') { 'Glitch_AI' } else { 'Glitch' }", publisher)
        self.assertIn("Assembly version", publisher)
        self.assertIn("npm.cmd run validate:releases", publisher)
        self.assertNotIn("git commit ", publisher.lower())
        self.assertNotIn("git push ", publisher.lower())

    def test_update_channels_follow_the_client_edition(self):
        update = source(ADDON_UPDATE)
        client = source(MAIN_WINDOW)
        self.assertIn('startsWith("addon-ai-")', update)
        self.assertIn('searchParams.set("edition", "ai")', update)
        self.assertIn("DEFAULT_AI_ADDON_DOWNLOAD_URL", update)
        self.assertRegex(client, r'CurrentClientVersion = "addon(?:-ai)?-0\.0\.2\.0"')

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

    def test_live_replication_copies_each_execution_delta_without_position_repair(self):
        copy = source(COPY_ENGINE)
        opening = method_body(copy, "private void FanOutOpening", "private void FanOutCompleteClose")
        scale = method_body(copy, "private static int ScaleExecution", "private static bool TrySnapshotOrders")
        self.assertIn("ScaleExecution(context, route.Ratio)", opening)
        self.assertIn("ScaleFollowerQuantity(filled, ratio)", scale)
        self.assertIn("ScaleFollowerQuantity(before, ratio)", scale)
        self.assertNotIn("ResolveContextMasterQuantity(context)", opening)
        self.assertNotIn("expected", opening)
        self.assertNotIn("actual", opening)
        self.assertNotIn("inFlight", opening)
        self.assertNotIn("GetEntryDenialReason", copy)
        self.assertNotIn("TryGetInFlightOpeningQuantity", copy)

    def test_user_sync_uses_the_configured_route_without_a_route_cap_admission(self):
        copy = source(COPY_ENGINE)
        sync = method_body(copy, "public void SyncFollower", "private void FanOutOpening")
        submit = method_body(copy, "private FollowerOrderSubmission SubmitFollowerEntry", "private bool SubmitProtectionUnits")
        self.assertIn("FindConfiguredRoute(masterAccount, followerAccount)", sync)
        self.assertIn("BeginSyncTail(sync, configuredRoute", sync)
        self.assertIn("FollowerOrderSubmission submission = SubmitFollowerEntry(", sync)
        self.assertNotIn("TryResolveRouteContractCap", copy)
        self.assertNotIn("TryGetTotalOpenContracts", copy)
        self.assertNotIn("TryGetTotalInFlightOpeningQuantity", copy)
        self.assertNotIn("FollowerFinalMaxContracts", copy)

    def test_replication_requires_a_connected_master_and_truthful_active_route(self):
        copy = source(COPY_ENGINE)
        route_validation = method_body(copy, "private static bool IsValidRoute", "private static bool IsOpeningAction")
        self.assertIn("route.MasterAccountInstance != null", route_validation)
        replication = source(REPLICATION_UI)
        self.assertIn("MasterAccountInstance = masterAccount", replication)
        self.assertIn("_copyEngine.Configure(_isReplicatingUi, routes)", replication)

    def test_manual_follower_divergence_never_blocks_later_execution_deltas(self):
        copy = source(COPY_ENGINE)
        opening = method_body(copy, "private void FanOutOpening", "private void FanOutCompleteClose")
        sync = method_body(copy, "public void SyncFollower", "private void FanOutOpening")
        self.assertIn("ScaleExecution(context, route.Ratio)", opening)
        self.assertIn("int expected =", sync)
        self.assertIn("SubmitFollowerEntry", sync)
        for forbidden in (
            "_suppressedFollowerRoots",
            "SuppressFollowerRoot",
            "automatic_sync_required",
            "manual_or_external_divergence",
        ):
            self.assertNotIn(forbidden, copy)

    def test_partial_master_close_is_copied_with_authoritative_protection_reconcile(self):
        copy = source(COPY_ENGINE)
        replication = source(REPLICATION_UI)
        close = method_body(copy, "private void FanOutCompleteClose", "private FollowerOrderSubmission SubmitFollowerClose")
        submit = method_body(
            copy,
            "private FollowerOrderSubmission SubmitFollowerClose",
            "private void TrySubmitAttributedRecoveryClose",
        )
        state = method_body(copy, "public void ProcessAccountStateUpdate", "public void ProcessFollowerExecution")
        self.assertIn("ScaleExecution(context, route.Ratio)", close)
        self.assertIn("TryGetNetQuantityForInstrument(route.FollowerAccount, context.Instrument", close)
        self.assertIn("Math.Min(requested, closable)", close)
        self.assertIn("SubmitFollowerClose", close)
        self.assertIn('signalPrefix + "-X-"', submit)
        self.assertIn("_copyEngine.ProcessFollowerExecution(account)", replication)
        self.assertIn("ReconcileFollowerProtection(account)", state)
        self.assertIn("follower_protection_reconcile", copy)
        self.assertNotIn("PartialFollowerExitUnsupported", copy)
        self.assertNotIn("partial_manual_exit", copy)

    def test_copy_engine_never_uses_account_flatten_or_human_orders_for_cleanup(self):
        copy = source(COPY_ENGINE)
        cleanup = method_body(copy, "private void CleanupFlatFollowerOrders", "private bool TryGetRouteSnapshot")
        self.assertNotIn("account.Flatten", copy)
        self.assertNotIn("_flattenSubmitted", copy)
        self.assertIn("ParseFollowerSignalKind(order.Name) != FollowerSignalKind.None", cleanup)

    def test_copy_entries_follow_native_master_execution_without_a_pending_bracket_veto(self):
        text = source(COPY_ENGINE)
        opening = method_body(text, "public void ProcessMasterExecution", "public void ProcessMasterOrderUpdate")
        submit = method_body(text, "private FollowerOrderSubmission SubmitFollowerEntry", "private bool SubmitProtectionUnits")
        self.assertIn("FanOutOpening(masterAccount, context, routes, plan, masterEntryQuantity)", opening)
        self.assertIn("TryResolveMasterPlan", opening)
        self.assertNotIn("TryGetNetQuantityForInstrumentRoot", opening)
        self.assertNotIn("PendingMasterCopy", text)
        self.assertNotIn("copy_wait|reason=master_bracket_not_working", text)
        self.assertNotIn("|| plan == null", submit)
        self.assertIn("ProtectionAvailable = protectionAvailable", submit)
        self.assertIn('"|protection=" + (protectionAvailable ? "mirrored" : "not_available")', submit)
        self.assertIn("SubmitProtectionUnits", text)
        self.assertIn("OrderType.StopMarket", text)
        self.assertIn("OrderType.Limit", text)
        self.assertIn("OrderEntry.Automated", text)

    def test_execution_before_bracket_retains_late_protection_identity_without_gating_the_copy(self):
        copy = source(COPY_ENGINE)
        opening = method_body(copy, "private void FanOutOpening", "private void FanOutCompleteClose")
        submit = method_body(copy, "private FollowerOrderSubmission SubmitFollowerEntry", "private bool SubmitProtectionUnits")
        self.assertIn("SubmitFollowerEntry(", opening)
        self.assertIn("context.OrderSignalName", opening)
        self.assertIn("MasterAccountName = masterAccount?.Name?.Trim()", submit)
        self.assertIn("MasterEntrySignal = masterEntrySignal?.Trim()", submit)
        self.assertIn("MasterEntryQuantity = Math.Max(0, masterEntryQuantity)", submit)
        self.assertIn("MasterEntryOrder = masterEntryOrder", submit)
        self.assertNotIn("return;\n            }\n\n            FanOutOpening", opening)

    def test_late_complete_master_plan_attaches_follower_protection_once(self):
        copy = source(COPY_ENGINE)
        master_update = method_body(copy, "public void ProcessMasterOrderUpdate", "public void ProcessFollowerOrderUpdate")
        attach = method_body(copy, "private void TryAttachLateFollowerProtection", "private void MirrorMasterProtection")
        self.assertIn("TryAttachLateFollowerProtection(masterAccount, order)", master_update)
        self.assertIn("lifecycle.MasterEntrySignal", attach)
        self.assertIn("lifecycle.MasterEntryQuantity", attach)
        self.assertIn("lifecycle.MasterEntryOrder?.Filled", attach)
        self.assertIn("!lifecycle.ProtectionAvailable", attach)
        self.assertIn("lifecycle.ProtectionAvailable = true", attach)
        self.assertIn("ProcessFollowerOrderUpdate(lifecycle.Account, entryOrder)", attach)
        self.assertIn("result=late_plan_attached", attach)

    def test_duplicate_master_order_callbacks_do_not_repeat_late_attachment(self):
        attach = method_body(
            source(COPY_ENGINE),
            "private void TryAttachLateFollowerProtection",
            "private void MirrorMasterProtection",
        )
        self.assertIn("!lifecycle.ProtectionAvailable", attach)
        self.assertIn("if (lifecycle.ProtectionAvailable)\n                        continue;", attach)
        self.assertEqual(attach.count("ProcessFollowerOrderUpdate(lifecycle.Account, entryOrder)"), 1)

    def test_truly_unprotected_master_stays_copied_without_a_late_protection_failure(self):
        copy = source(COPY_ENGINE)
        attach = method_body(copy, "private void TryAttachLateFollowerProtection", "private void MirrorMasterProtection")
        submit = method_body(copy, "private FollowerOrderSubmission SubmitFollowerEntry", "private bool SubmitProtectionUnits")
        self.assertNotIn("RaiseCritical", attach)
        self.assertNotIn("RequestFollowerFlattenOnce", attach)
        self.assertIn('"not_available"', submit)

    def test_master_stop_and_target_changes_mirror_to_follower_protection(self):
        mirror = method_body(
            source(COPY_ENGINE),
            "private void MirrorMasterProtection",
            "private void TrimFollowerProtection",
        )
        self.assertIn("GlitchReplicationEngine.IsStopLikeOrder(masterOrder)", mirror)
        self.assertIn("masterOrder.OrderType == OrderType.Limit", mirror)
        self.assertIn('CopySignalName + (isStop ? "-S-" : "-T-")', mirror)
        self.assertIn("followerOrder.StopPriceChanged = masterPrice", mirror)
        self.assertIn("followerOrder.LimitPriceChanged = masterPrice", mirror)
        self.assertIn("route.FollowerAccount.Change(changes.ToArray())", mirror)

    def test_late_protection_never_uses_an_unlinked_master_plan(self):
        protection = source(PROTECTION)
        self.assertIn("candidates = linked;", protection)

    def test_unprotected_recent_copy_recovery_is_observational_not_critical(self):
        copy = source(COPY_ENGINE)
        recovery = method_body(
            copy,
            "private bool TryRecoverRecentFollowerLifecycle",
            "private GlitchCopyFollowerRoute FindUniqueConfiguredRouteForFollower",
        )
        self.assertIn("ProtectionAvailable = false", recovery)
        self.assertIn('"not_available_recovered"', recovery)
        self.assertIn('"|result=" + CleanToken(result)', recovery)
        self.assertIn("return true;", recovery)

    def test_recent_recovery_requires_persisted_ratio_and_allocation_offset(self):
        copy = source(COPY_ENGINE)
        recovery = method_body(
            copy,
            "private bool TryRecoverRecentFollowerLifecycle",
            "private GlitchCopyFollowerRoute FindUniqueConfiguredRouteForFollower",
        )
        submit = method_body(
            copy,
            "private FollowerOrderSubmission SubmitFollowerEntry",
            "private bool SubmitProtectionUnits",
        )
        self.assertIn("BuildFollowerEntrySignal(", submit)
        self.assertIn("TryReadFollowerAllocationMetadata(", recovery)
        self.assertIn("BitConverter.DoubleToInt64Bits(route.Ratio)", recovery)
        self.assertIn("TryScalePlanSlice(", recovery)
        self.assertIn("followerAllocationOffset", recovery)
        self.assertIn("ambiguous_allocation_metadata_recovered", recovery)
        self.assertIn("ambiguous_route_recovered", recovery)
        self.assertIn("ambiguous_route_ratio_changed_recovered", recovery)
        self.assertIn("ambiguous_allocation_slice_recovered", recovery)
        self.assertNotIn("TryScalePlan(plan, requestedQuantity", recovery)

    def test_partial_master_fills_copy_execution_deltas_and_are_not_order_id_deduped(self):
        copy = source(COPY_ENGINE)
        replication = source(REPLICATION_UI)
        self.assertIn("EntryOrderFilledQuantity", copy)
        self.assertIn("context.EntryOrder?.Filled", copy)
        self.assertIn("ScaleFollowerQuantity(filled, ratio)", copy)
        self.assertIn("ResolveFollowerAllocationOffset(context, route.Ratio)", copy)
        self.assertNotIn("Math.Abs(currentMasterNet) < copyMasterQuantity", copy)
        self.assertNotIn("Math.Abs(masterNet) < copyMasterQuantity", copy)
        self.assertIn('TryGetNestedPropertyValueAsString(executionObject, "ExecutionId")', replication)
        self.assertNotIn('TryGetNestedPropertyValueAsString(executionObject, "ExecutionId", "Id")', replication)
        self.assertIn("Math.Max(quantity, Math.Max(0, order.Filled))", replication)
        self.assertIn("EntryOrder = order", replication)

    def test_multi_fill_protection_slices_one_aggregate_follower_plan(self):
        copy = source(COPY_ENGINE)
        protection = source(PROTECTION)
        opening = method_body(copy, "private void FanOutOpening", "private void FanOutCompleteClose")
        sync = method_body(copy, "public void SyncFollower", "private void FanOutOpening")
        attach = method_body(copy, "private void TryAttachLateFollowerProtection", "private void MirrorMaster")
        slicing = method_body(
            protection,
            "public static bool TryScalePlanSlice",
            "public static bool IsMasterProtectionExecution",
        )
        self.assertIn("FollowerAllocationOffset", copy)
        self.assertIn("RouteRatio", copy)
        self.assertIn("ResolveFollowerAllocationOffset(context, route.Ratio)", opening)
        self.assertIn("Math.Abs(actual)", sync)
        self.assertIn("TryScalePlanSlice(", attach)
        self.assertIn("lifecycle.FollowerAllocationOffset", attach)
        self.assertIn("ScaleFollowerQuantity(plan.MasterQuantity, ratio)", slicing)
        self.assertIn("TryScalePlan(plan, aggregateFollowerQuantity", slicing)
        self.assertIn("Math.Min(sliceEnd, sourceEnd)", slicing)

    def test_user_sync_is_two_phase_owned_delta_and_detects_manual_interference(self):
        copy = source(COPY_ENGINE)
        replication = source(REPLICATION_UI)
        sync = method_body(copy, "public void SyncFollower", "private void FanOutOpening")
        state_update = method_body(
            copy,
            "public void ProcessAccountStateUpdate",
            "public void ProcessFollowerExecution",
        )
        group_sync = method_body(
            replication,
            "private void SyncGroupFollowers",
            "private void HandleFollowerEnableUserToggle",
        )
        self.assertIn("BeginSyncFlatten", sync)
        self.assertIn("BeginSyncReduce", sync)
        self.assertIn("ProcessSyncLifecycle(sync)", sync)
        self.assertIn("CatchUpSignalName", sync)
        self.assertIn("sync.FlattenOrder?.Filled", sync)
        self.assertIn("sync.TailOrder?.Filled", sync)
        self.assertIn("CancelSyncOwnedRemainder", sync)
        self.assertIn("manual_override", sync)
        self.assertIn("ProcessSyncAccountStateUpdate(account)", state_update)
        self.assertNotIn("account.Flatten", copy)
        self.assertNotIn("|group=", group_sync)
        self.assertIn("|phase=validation|result=", group_sync)

    def test_protection_failure_recovery_is_lifecycle_attributed_and_same_side_capped(self):
        copy = source(COPY_ENGINE)
        recovery = method_body(
            copy,
            "private void TrySubmitAttributedRecoveryClose",
            "private FollowerOrderSubmission SubmitFollowerEntry",
        )
        self.assertIn("RecoveryCloseSubmitted", recovery)
        self.assertIn("(followerNet > 0) != lifecycle.IsLong", recovery)
        self.assertIn("Math.Min(attributableQuantity, Math.Abs(followerNet))", recovery)
        self.assertIn("manual_override", recovery)
        self.assertIn("SubmitFollowerClose(", recovery)

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
        self.assertIn("TrySubmitAttributedRecoveryClose", body)
        self.assertIn("manual_override_unattributed", body)
        self.assertIn("attributableQuantity", body)

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

    def test_follower_failure_evidence_is_trade_scoped_and_unambiguous(self):
        copy_engine = source(COPY_ENGINE)
        self.assertIn(
            '"FollowerProtectionRejected|" + root + "|" + CleanToken(lifecycle?.EntrySignal ?? signal)',
            copy_engine,
        )
        self.assertIn("|attributable_qty=", copy_engine)
        self.assertIn("|result=manual_override_unattributed", copy_engine)
        self.assertNotIn("account.Flatten", copy_engine)

    def test_replication_state_is_truthful_and_reload_is_observe_only(self):
        window = source(MAIN_WINDOW)
        performance = source(ADDON / "UI/MainWindow/GlitchMainWindow.Performance.partial.cs")
        chart_trader = source(ADDON / "GlitchAddOn.ChartTrader.partial.cs")
        self.assertIn("_isReplicatingUi && _copyEngine?.IsEnabled == true", window)
        self.assertIn("SetReplicationFromExternalSurface(!IsReplicationEnabledFromExternalSurface()", window)
        self.assertIn("IsReplicating = IsReplicationEnabledFromExternalSurface()", performance)
        self.assertIn("GlitchShellBridge.ToggleReplication()", chart_trader)
        self.assertNotIn("UseLegacyReplicationEngine", window + source(POLICY_STORE))
        self.assertNotIn('SyncGroupFollowers("startup")', window)
        self.assertIn("replication_restored|origin=startup|catchup=skipped", window)

    def test_sync_is_only_available_from_the_visible_user_sync_action(self):
        window = source(MAIN_WINDOW)
        replication = source(REPLICATION_UI)
        toggle = method_body(
            window,
            "internal bool SetReplicationFromExternalSurface",
            "internal void ToggleReplicationFromExternalSurface",
        )
        enable = method_body(replication, "private void HandleFollowerEnableUserToggle", "private void HandleFollowerRatioUserChange")
        ratio = method_body(replication, "private void HandleFollowerRatioUserChange", "private void WireReplicationMemberHandlers")
        master = method_body(window, "private void UpdateGroupMasterSelection", "private void AddFollowerToGroup")
        self.assertIn('L("dashboard.group.sync", "Sync")', window)
        self.assertIn("SyncGroupFollowers(group)", window)
        self.assertIn("replication_sync|origin=user_sync", replication)
        self.assertIn("_copyEngine.SyncFollower(masterAccount, followerAccount, member.Ratio)", replication)
        self.assertNotIn("Sync", toggle)
        self.assertNotIn("Sync", enable)
        self.assertNotIn("Sync", ratio)
        self.assertNotIn("Sync", master)

    def test_sync_action_is_localized_for_all_supported_languages(self):
        rows = {
            row.split("\t", 1)[0]: row.split("\t")
            for row in source(LOCALIZATION).splitlines()
            if row
        }
        self.assertEqual(
            rows["dashboard.group.sync"],
            ["dashboard.group.sync", "Sync", "Sincronizar", "Sincronizar", "同步", "Synchroniser", "Синхронизировать"],
        )

    def test_addon_and_chart_trader_flatten_all_share_one_fleet_command(self):
        chart_trader = source(ADDON / "GlitchAddOn.ChartTrader.partial.cs")
        shell = source(ADDON / "Services/GlitchShellBridge.cs")
        self.assertIn("GlitchShellBridge.FlattenAll()", chart_trader)
        self.assertIn("GlitchAddOn.RequestFlattenAll()", shell)
        self.assertIn("RunFlattenAllAsync(showHeaderButtonFeedback: true)", source(MAIN_WINDOW))

    def test_ninjascript_reload_permanently_closes_previous_assembly_window(self):
        shell = source(ADDON / "GlitchAddOn.cs")
        self.assertIn('GetMethod(\n                    "ShutdownForAddOn"', shell)
        self.assertIn("System.Reflection.BindingFlags.NonPublic", shell)
        self.assertIn("shutdown.Invoke(window, null)", shell)
        self.assertIn("internal void ShutdownForAddOn()", source(MAIN_WINDOW))

    def test_follower_cleanup_is_narrowly_owned(self):
        text = source(COPY_ENGINE)
        self.assertIn("ParseFollowerSignalKind", text)
        self.assertIn('suffix.StartsWith("-E-"', text)
        self.assertIn('suffix.StartsWith("-X-"', text)
        self.assertIn("isCopy", text)
        self.assertIn("isCatchUp", text)
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
        self.assertEqual(
            json.loads(base64.b64decode(encoded).decode("utf-8")),
            json.loads(PROP_RULES.read_text(encoding="utf-8")),
        )

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

    def test_replication_does_not_veto_executed_entries_with_apex_inference(self):
        copy = source(COPY_ENGINE)
        self.assertNotIn("GlitchApexDirectionGuard", copy)
        self.assertNotIn("apex_direction_compliance", copy)
        self.assertNotIn("master_entry_not_replicated", copy)

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
