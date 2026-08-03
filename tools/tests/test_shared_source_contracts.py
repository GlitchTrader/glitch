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
TRADE_LEDGER = ADDON / "Services/Insights/GlitchTradeLedgerService.cs"
SUMMARY_TAB = ADDON / "UI/MainWindow/GlitchMainWindow.SummaryTab.partial.cs"
JOURNAL_TAB = ADDON / "UI/MainWindow/GlitchMainWindow.JournalTab.partial.cs"
METADATA = ADDON / "Services/Trading/GlitchInstrumentMetadataService.cs"
FEED_BUS = ADDON / "UI/Analytics/GlitchAnalyticsFeedBus.cs"
ANALYTICS_BRIDGE = INDICATORS / "GlitchAnalyticsBridge.cs"
PROP_RULES = ADDON / "Resources/PropFirmRules.json"
PROP_RULE_BUNDLE = ADDON / "UI/MainWindow/GlitchMainWindow.PropFirmRulesBundle.generated.cs"
PROP_RULE_GENERATOR = ROOT / "scripts/generate_bundled_prop_rules.ps1"
FUNDAMENTAL_ANALYSIS = ADDON / "Services/FundamentalAnalysis/GlitchFundamentalAnalysisService.cs"
ANALYTICS_LOGIC = ADDON / "UI/Analytics/GlitchAnalyticsLogic.cs"
ANALYTICS_TAB = ADDON / "UI/MainWindow/GlitchMainWindow.AnalyticsTab.partial.cs"
DOWNLOAD_APP = ROOT / "apps/download"
RELEASE_CATALOG = DOWNLOAD_APP / "src/lib/release-catalog.json"
RELEASE_CHECKSUMS = DOWNLOAD_APP / "public/files/checksums.json"
RELEASES_LIB = DOWNLOAD_APP / "src/lib/releases.ts"
RELEASE_VALIDATOR = DOWNLOAD_APP / "scripts/validate-releases.mjs"
RELEASE_PUBLISHER = ROOT / "scripts/publish-release.ps1"
ADDON_UPDATE = ROOT / "apps/api/src/lib/addon-update.ts"
HERMES_PORTFOLIO_EVENTS = ADDON / "Services/Persistence/GlitchHermesPortfolioEventWriter.cs"


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
        self.assertIn("$fileName = if ($Edition -eq 'ai')", publisher)
        self.assertIn('"Glitch_AI_v$Version.zip"', publisher)
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
        self.assertRegex(client, r'CurrentClientVersion = "addon(?:-ai)?-0\.0\.2\.(?:0|2)"')

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
        self.assertIn("TryGetNetQuantityForInstrument", text)
        self.assertNotIn("TryGetNetQuantityForInstrumentRoot", text)
        self.assertIn("TryGetOpenPositionInstruments", text)

    def test_live_replication_copies_each_execution_delta_without_position_repair(self):
        copy = source(COPY_ENGINE)
        opening = method_body(copy, "private void FanOutOpening", "private void FanOutCompleteClose")
        scale = method_body(copy, "private ExecutionAllocation AllocateExecutionDelta", "private static string BuildAllocationRouteKey")
        self.assertIn("AllocateExecutionDelta(route, context, true)", opening)
        self.assertIn("state.MasterQuantity += context.Quantity", scale)
        self.assertIn("ScaleFollowerQuantity(state.MasterQuantity, route.Ratio)", scale)
        self.assertIn("targetFollowerQuantity - state.FollowerQuantity", scale)
        self.assertNotIn("ResolveContextMasterQuantity(context)", opening)
        self.assertNotIn("expected", opening)
        self.assertNotIn("actual", opening)
        self.assertNotIn("inFlight", opening)
        self.assertNotIn("GetEntryDenialReason", copy)
        self.assertNotIn("TryGetInFlightOpeningQuantity", copy)

    def test_fractional_allocation_epoch_is_future_only_and_configuration_safe(self):
        copy = source(COPY_ENGINE)
        configure = method_body(copy, "public void Configure", "public void ProcessMasterExecution")
        epochs = method_body(
            copy,
            "private void ReconcileAllocationEpochs",
            "private ExecutionAllocation AllocateExecutionDelta",
        )
        tooltip = method_body(
            source(ADDON / "UI/MainWindow/GlitchMainWindow.cs"),
            "private string BuildFollowerRatioMathTooltip",
            "private static Style CreateEditableRatioTextBoxStyle",
        )
        self.assertIn("ReconcileAllocationEpochs(nextEnabled, nextRouteSignatures)", configure)
        self.assertIn("if (!nextEnabled || !_enabled)", epochs)
        self.assertIn("_allocationByRouteDirection.Clear()", epochs)
        self.assertIn("changedRoutes.Contains(item.Value.RouteKey)", epochs)
        self.assertNotIn("Submit", epochs)
        self.assertIn("dashboard.group.ratio_allocation_policy", tooltip)

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

    def test_sim_and_unknown_accounts_use_apex_declared_size_template(self):
        window = source(ADDON / "UI/MainWindow/GlitchMainWindow.cs")
        rules = source(ADDON / "UI/MainWindow/GlitchMainWindow.FirmRules.partial.cs")
        self.assertIn('string ruleFirmId = selectedFirmId;', window)
        self.assertIn('ruleFirmId = "ApexTraderFunding";', window)
        self.assertIn('GetRuleForFirmAndSize(ruleFirmId, selectedStatus', window)
        self.assertIn('AccountSize = 250000, MaxContracts = 27', rules)
        self.assertIn('AccountSize = 300000, MaxContracts = 35', rules)

    def test_account_size_provenance_is_additive_and_does_not_change_replication_rules(self):
        window = source(ADDON / "UI/MainWindow/GlitchMainWindow.cs")
        row = source(ADDON / "UI/MainWindow/GlitchMainWindow.AccountGridRow.partial.cs")
        models = source(ADDON / "UI/MainWindow/GlitchMainWindow.Models.partial.cs")
        state = source(ADDON / "Services/Persistence/GlitchStateStore.cs")
        self.assertIn("AccountSizeSource", window)
        self.assertIn("AccountSizeSource", row)
        self.assertIn("AccountSizeSource", models)
        refresh = source(ADDON / "UI/MainWindow/GlitchMainWindow.RefreshPipeline.partial.cs")
        self.assertIn("AccountSizeSource = selectionOverride.AccountSizeSource", refresh)
        self.assertIn('"LiveNetLiquidation"', window)
        self.assertIn('"LiveCashValue"', window)
        self.assertIn('"AccountName"', window)
        self.assertIn('"DefaultTier"', window)
        self.assertIn("sizeSource", state)
        self.assertIn('ruleFirmId = "ApexTraderFunding";', window)
        self.assertIn("GetRuleForFirmAndSize(ruleFirmId, selectedStatus", window)
        self.assertIn("public bool IsRiskDataReady", row)
        self.assertIn("IsRiskDataReady = isRiskDataReady", window)
        self.assertIn("if (!row.IsRiskDataReady)", window)
        self.assertNotIn("return GetAccountSizeFromNt(account) > 0;", window)

    def test_account_runtime_events_use_correct_scopes_without_direct_order_refresh(self):
        window = source(ADDON / "UI/MainWindow/GlitchMainWindow.cs")
        refresh = source(ADDON / "UI/MainWindow/GlitchMainWindow.RefreshPipeline.partial.cs")
        self.assertIn('EnsureAccountStatusEventSubscribed();', window)
        self.assertIn('string[] eventNames = { "ExecutionUpdate", "PositionUpdate", "OrderUpdate", "AccountItemUpdate" };', window)
        self.assertIn('"AccountStatusUpdate"', window)
        self.assertIn("BindingFlags.Public | BindingFlags.Static", window)
        self.assertIn('_accountStatusEventSubscription', window)
        callback = method_body(window, "private void OnAccountRuntimeEventBridgeCore", "private static bool IsReplicationInternalSignal")
        self.assertNotIn("RefreshAccountData(", callback)
        self.assertIn("QueueAccountRefreshFromRuntimeEvent(account, eventArgs);", callback)
        self.assertIn("_activeAccountCache.Remove(account.Name.Trim());", callback)
        self.assertIn("QueueBackgroundAccountRefresh(GetActiveAccountsSnapshot(), heavyTabWork: true);", callback)
        self.assertIn("private void QueueAccountRefreshFromRuntimeEvent(Account account, object eventArgs)", refresh)
        self.assertIn("sequence < Interlocked.Read(ref _accountRefreshSequence)", refresh)

    def test_account_event_binding_failures_are_visible_and_narrowly_scoped(self):
        window = source(ADDON / "UI/MainWindow/GlitchMainWindow.cs")
        binding = method_body(
            window,
            "private void EnsureAccountRuntimeEventsSubscribed",
            "private void RecordAccountEventBindingFailure",
        )
        failure = method_body(
            window,
            "private void RecordAccountEventBindingFailure",
            "private void EnsureAccountStatusEventSubscribed",
        )
        status = method_body(
            window,
            "private void EnsureAccountStatusEventSubscribed",
            "private void OnAccountRuntimeEventBridge",
        )

        self.assertIn('RecordAccountEventBindingFailure(accountName, eventName, "event_registration", ex);', binding)
        self.assertIn('"reflection_lookup"', binding)
        self.assertIn('"delegate_construction"', binding)
        self.assertIn('"event_registration"', status)
        self.assertIn('AppendJournal(normalizedAccount, "System", message);', failure)
        self.assertIn("RaiseCriticalWarning(", failure)
        self.assertIn("unlocksTrading: false", failure)
        self.assertIn("subscriptions.Add(new EventBridgeSubscription", binding)
        self.assertNotIn("catch\n                {\n                }", binding)
        self.assertNotIn("_accountEventSubscriptions.Clear()", binding + status)

    def test_inferred_account_identity_is_observational_only(self):
        window = source(MAIN_WINDOW)
        compliance = source(ADDON / "Services/Risk/GlitchComplianceEngine.cs")
        mitigation = source(ADDON / "Services/Risk/GlitchRiskMitigationEngine.cs")
        runtime_policy = source(POLICY_STORE)
        dashboard = source(ADDON / "UI/MainWindow/GlitchMainWindow.DashboardTab.partial.cs")
        actions = method_body(window, "private void ApplyEnabledRiskActions", "private void ClearComplianceEnforcementRuntimeState")
        normalize = method_body(compliance, "public static string NormalizeAccountStatus", "public static string InferPropFirmId")
        infer = method_body(compliance, "public static string InferAccountStatus", "public static string GetExecutionProviderHint")
        self.assertIn('return "Unknown";', normalize)
        self.assertIn('return "Unknown";', infer)
        self.assertIn("if (!row.IsManualSelection)", actions)
        self.assertIn("GlitchComplianceEngine.NormalizeAccountStatus(accountStatus)", mitigation)
        self.assertIn("GlitchComplianceEngine.NormalizeAccountStatus(accountStatus)", runtime_policy)
        self.assertIn('new List<string> { "Unknown", "Sim", "Eval", "AP" }', window)
        self.assertIn("nameof(AccountGridRow.AccountSizeSource)", dashboard)
        self.assertIn("dashboard.column.source", source(LOCALIZATION))

    def test_current_accounts_follow_native_account_connection_status(self):
        window = source(MAIN_WINDOW)
        active = method_body(window, "private static bool IsActiveAccount(Account account)", "private static bool IsFlattenEligibleAccount")
        flatten = method_body(window, "private static bool IsFlattenEligibleAccount(Account account)", "private static bool? TryGetBoolProperty")
        for method in (active, flatten):
            self.assertIn('accountType.GetProperty("ConnectionStatus")', method)
            self.assertIn('accountConnectionStatus.ToString(), "Connected"', method)
            self.assertNotIn("GetAccountSizeFromNt", method)

    def test_group_refresh_is_projection_only_and_persistence_keeps_route_authority(self):
        window = source(ADDON / "UI/MainWindow/GlitchMainWindow.cs")
        projection = method_body(window, "private static void ApplyAccountSnapshotToGroupMemberRow", "private void UpdateGroupMasterSelection")
        refresh = method_body(source(ADDON / "UI/MainWindow/GlitchMainWindow.RefreshPipeline.partial.cs"), "private void ApplyFullAccountRefreshResult", "private void UpdateHeaderMetricsFromRows")
        persistence = method_body(window, "private void SaveAccountGroupsToDisk", "private void RestoreWindowPlacementFromDisk")
        self.assertIn("member.Pnl", projection)
        self.assertIn("member.Position", projection)
        for forbidden in ("member.FollowerSize =", "member.MasterSize =", "member.Ratio =", "member.IsEnabled ="):
            self.assertNotIn(forbidden, projection)
        self.assertNotIn("SaveAccountGroupsToDisk(", refresh)
        self.assertIn("FollowerSize = followerSize", persistence)
        self.assertIn("Ratio = ratio", persistence)
        self.assertIn("MasterSize = masterSize", persistence)
        self.assertIn("IsEnabled = member.IsEnabled", persistence)

    def test_plan_limits_never_mutate_or_persist_user_group_intent(self):
        window = source(MAIN_WINDOW)
        limits = method_body(window, "private void ApplyPlanLimitsToAccountGroups", "private void MaybeRunLicenseHeartbeat")
        self.assertIn("CountConfiguredGroups() > maxGroups", limits)
        self.assertIn("AnyGroupHasEnabledFollowersOverLimit(maxFollowers)", limits)
        self.assertIn("Saved group and follower settings were preserved", limits)
        self.assertNotIn("member.IsEnabled =", limits)
        self.assertNotIn("SaveAccountGroupsToDisk(", limits)
        self.assertNotIn("RebuildAccountGroupsUi(", limits)

    def test_authoritative_writes_are_durable_atomic_and_visible_on_failure(self):
        state = source(ADDON / "Services/Persistence/GlitchStateStore.cs")
        runtime = source(POLICY_STORE)
        analytics_cache = source(ADDON / "Services/Persistence/GlitchAnalyticsBridgeCacheStore.cs")
        fundamentals = source(FUNDAMENTAL_ANALYSIS)
        trade_ledger = source(TRADE_LEDGER)
        risk_ledger = source(ADDON / "Services/Insights/GlitchRiskLockLedgerService.cs")
        window = source(MAIN_WINDOW)
        for token in (
            "FileOptions.WriteThrough",
            "stream.Flush(true)",
            "File.Replace(tempPath, fullPath, backupPath, true)",
            'fullPath + ".tmp." + Guid.NewGuid().ToString("N")',
        ):
            self.assertIn(token, state)
        self.assertIn("GlitchStateStore.WriteAllLinesAtomic", runtime)
        self.assertIn("GlitchStateStore.WriteAllTextAtomic", analytics_cache)
        self.assertIn("GlitchStateStore.WriteAllLinesAtomic", fundamentals)
        self.assertIn("GlitchStateStore.WriteAllLinesAtomic", trade_ledger)
        self.assertIn("GlitchStateStore.WriteAllLinesAtomic", risk_ledger)
        self.assertNotIn("File.Delete(path)", analytics_cache)
        self.assertIn('RecordSubsystemFault("audit_persistence", ex)', window)
        self.assertIn('RecordSubsystemFault("account_group_persistence", ex)', window)
        self.assertIn('RecordSubsystemFault("account_override_persistence", ex)', window)
        self.assertIn("LoadValidatedWithBackup", state)
        self.assertIn("throw new InvalidDataException", state)
        group_load = method_body(
            window,
            "private void LoadAccountGroupsFromDisk",
            "private void SaveAccountGroupsToDisk",
        )
        self.assertLess(group_load.index("LoadAccountGroups("), group_load.index("_accountGroups.Clear()"))
        self.assertIn('"AccountGroupsRecovered"', group_load)
        self.assertIn('"AccountGroupsLoadFailed"', group_load)
        override_load = method_body(
            window,
            "private void LoadSelectionOverridesFromDisk",
            "private void SaveSelectionOverridesToDisk",
        )
        self.assertIn('"AccountOverridesRecovered"', override_load)
        self.assertIn('"AccountOverridesLoadFailed"', override_load)

    def test_max_contracts_risk_read_uses_locked_snapshot_and_fails_closed(self):
        window = source(ADDON / "UI/MainWindow/GlitchMainWindow.cs")
        helper = method_body(window, "private static bool TryGetTotalAbsoluteOpenContracts", "private static bool HasWorkingProtectiveStop")
        self.assertIn("lock (account.Positions)", helper)
        self.assertIn("ToArray()", helper)
        self.assertIn("TryGetTotalAbsoluteOpenContracts(liveAccount, out int currentAbsContracts)", window)

    def test_manual_follower_divergence_never_blocks_later_execution_deltas(self):
        copy = source(COPY_ENGINE)
        opening = method_body(copy, "private void FanOutOpening", "private void FanOutCompleteClose")
        sync = method_body(copy, "public void SyncFollower", "private void FanOutOpening")
        self.assertIn("AllocateExecutionDelta(route, context, true)", opening)
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
        self.assertIn("AllocateExecutionDelta(route, context, false)", close)
        self.assertIn("TryGetNetQuantityForInstrument(route.FollowerAccount, context.Instrument", close)
        self.assertIn("Math.Min(requested, closable)", close)
        self.assertIn("SubmitFollowerClose", close)
        self.assertIn('signalPrefix + "-X-"', submit)
        self.assertIn("_copyEngine.ProcessFollowerExecution(account)", replication)
        self.assertIn("ReconcileFollowerProtection(account)", state)
        self.assertIn("follower_protection_reconcile", copy)
        self.assertNotIn("PartialFollowerExitUnsupported", copy)
        self.assertNotIn("partial_manual_exit", copy)

    def test_close_and_sync_lifecycles_cancel_owned_remainders_from_exact_position_truth(self):
        copy = source(COPY_ENGINE)
        state = method_body(copy, "public void ProcessAccountStateUpdate", "public void ProcessFollowerExecution")
        sync_lifecycle = method_body(copy, "private void ProcessSyncLifecycle", "private void ProcessSyncFollowerOrderUpdate")
        sync_order = method_body(copy, "private void ProcessSyncFollowerOrderUpdate", "private void CancelSyncOwnedRemainder")
        close_reconcile = method_body(copy, "private void ReconcileCloses", "private void CancelUnsafeCloseRemainders")
        self.assertIn("TryGetNetQuantityForInstrument(", sync_lifecycle)
        self.assertNotIn("TryGetNetQuantityForInstrumentRoot(", sync_lifecycle)
        self.assertIn("CancelSyncOwnedRemainder(sync, sync.ReduceOrder)", sync_lifecycle)
        self.assertIn("sync.ReduceOrderSignal", sync_order)
        self.assertIn("ReconcileCloses(account", state)
        self.assertIn("expectedFromOwnedFills", close_reconcile)
        self.assertIn("account.Cancel(cancellations.ToArray())", close_reconcile)

    def test_partial_protection_reconcile_resizes_native_oco_quantity(self):
        copy = source(COPY_ENGINE)
        trim = method_body(
            copy,
            "private void ResizeProtection",
            "private void CleanupFlatFollowerOrders",
        )
        flat = method_body(
            copy,
            "private void CancelOwnedOrdersAtFlat",
            "private void ResizeProtection",
        )
        self.assertIn("QuantityChanged", trim)
        self.assertIn("account.Change(changes.ToArray())", trim)
        self.assertIn("TryResolveMasterPlan(", trim)
        self.assertIn("TryResolveSingleOvercoveredMasterGeometry(", trim)
        self.assertIn("ResolveProtectionMasterAccount(account, instrument, units)", trim)
        self.assertIn("ProtectionGeometryMatches(unit, desired)", trim)
        self.assertIn("ReportProtectionAmbiguity(", trim)
        self.assertNotIn("ThenByDescending(unit => unit.Orders[0].Oco", trim)
        self.assertIn("FollowerSignalKind.Protection", flat)
        self.assertIn("FollowerSignalKind.Close", flat)
        self.assertNotIn("ParseFollowerSignalKind(order.Name) != FollowerSignalKind.None", flat)

    def test_protection_resize_failure_does_not_promote_local_quantity_to_native_truth(self):
        copy = source(COPY_ENGINE)
        trim = method_body(
            copy,
            "private void ResizeProtection",
            "private static bool TryBuildFollowerProtectionUnit",
        )
        self.assertIn("bool nativeMutationFailed = false;", trim)
        self.assertIn("var originalQuantityChanged = new Dictionary<Order, int>();", trim)
        self.assertIn("originalQuantityChanged[order] = order.QuantityChanged;", trim)
        self.assertIn("original.Key.QuantityChanged = original.Value;", trim)
        self.assertIn("nativeMutationFailed = true;", trim)
        self.assertIn("if (!nativeMutationFailed)\n                ClearProtectionAmbiguity(account, instrument);", trim)
        self.assertLess(
            trim.index("account.Change(changes.ToArray())"),
            trim.index("original.Key.QuantityChanged = original.Value;"),
        )

    def test_rejected_follower_protection_leg_is_repaired_before_recovery_close(self):
        # A transient native rejection of one follower protection leg must not
        # close a follower out of a position the master still holds. The
        # response is one bounded resubmission of the exact same leg; a second
        # rejection falls through to attributed recovery.
        text = source(COPY_ENGINE)
        dispatch = method_body(
            text,
            "private void ProcessFollowerProtectionOrderUpdate",
            "public void ProcessAccountStateUpdate",
        )
        self.assertIn("TryRepairRejectedFollowerProtectionLeg", dispatch)
        self.assertLess(
            dispatch.index("TryRepairRejectedFollowerProtectionLeg"),
            dispatch.index("TrySubmitAttributedRecoveryClose"),
        )
        repair = method_body(
            text,
            "private bool TryRepairRejectedFollowerProtectionLeg",
            "private void TrySubmitAttributedRecoveryClose",
        )
        self.assertIn("RepairedProtectionSignals.Add(signal)", repair)
        self.assertIn("rejected.Oco", repair)
        self.assertIn("rejected.StopPrice", repair)
        self.assertIn("rejected.LimitPrice", repair)
        self.assertIn("follower_protection_repair", repair)
        self.assertNotIn("TrySubmitAttributedRecoveryClose", repair)
        self.assertNotIn("Flatten", repair)

    def test_unavailable_risk_state_is_never_displayed_as_realized_loss(self):
        # GL-STAB-01: a disconnected or unread account must render risk as
        # unavailable (dash), never as a computed 100% loss, and the risk
        # mitigation loop must skip rows without ready native data.
        text = source(MAIN_WINDOW)
        self.assertIn("bool isRiskDataReady = nativeNetLiquidation > 0 || cashValue > 0", text)
        self.assertIn(
            "double headroomRatioRaw = isRiskDataReady && maxDrawdown > 0 && bufferMargin.HasValue",
            text,
        )
        mitigation = method_body(
            text,
            "private void ApplyEnabledRiskActions",
            "private void ClearComplianceEnforcementRuntimeState",
        )
        self.assertIn("if (!row.IsRiskDataReady)", mitigation)

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
        self.assertIn("LogPlanWait(lifecycle)", attach)

    def test_unlinked_manual_atm_plan_requires_exact_full_native_position(self):
        protection = source(PROTECTION)
        resolve = method_body(
            protection,
            "public static bool TryResolveMasterPlan",
            "public static bool TryScalePlan",
        )
        fallback = method_body(
            protection,
            "private static bool CanUseFullPositionPlan",
            "private static string TryGetSignalCorrelation",
        )
        self.assertIn("CanUseFullPositionPlan(", resolve)
        self.assertIn("TryGetNetQuantityForInstrument(", fallback)
        self.assertIn("Math.Abs(masterNet) != requiredMasterQuantity", fallback)
        self.assertIn("instrument.FullName", resolve)
        self.assertNotIn("GetInstrumentRoot(order.Instrument)", resolve)

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
            "private void ReconcileFollowerProtection",
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
        self.assertIn("followerPlanQuantity", recovery)
        self.assertIn("ambiguous_allocation_metadata_recovered", recovery)
        self.assertIn("ambiguous_route_recovered", recovery)
        self.assertIn("ambiguous_route_ratio_changed_recovered", recovery)
        self.assertIn("ambiguous_allocation_slice_recovered", recovery)
        self.assertNotIn("TryScalePlan(plan, requestedQuantity", recovery)

    def test_partial_master_fills_copy_execution_deltas_and_are_not_order_id_deduped(self):
        copy = source(COPY_ENGINE)
        replication = source(REPLICATION_UI)
        self.assertIn("EntryOrderFilledQuantity", copy)
        self.assertIn("EntryOrderQuantity", copy)
        self.assertIn("OrderIdentity", copy)
        self.assertIn("context.EntryOrder?.Filled", copy)
        self.assertIn("AllocateExecutionDelta(route, context, true)", copy)
        self.assertIn("orderState.AllocatedFollowerQuantity", copy)
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
        self.assertIn("allocation.FollowerOrderOffset", opening)
        self.assertIn("allocation.FollowerOrderPlanQuantity", opening)
        self.assertIn("Math.Abs(actual)", sync)
        self.assertIn("TryScalePlanSlice(", attach)
        self.assertIn("lifecycle.FollowerAllocationOffset", attach)
        self.assertIn("lifecycle.FollowerPlanQuantity", attach)
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

    def test_ambiguous_manual_and_ai_exit_actions_use_signal_intent(self):
        copy = source(COPY_ENGINE)
        execution = method_body(
            copy,
            "public void ProcessMasterExecution",
            "public void ProcessMasterOrderUpdate",
        )
        classifier = method_body(copy, "private static bool IsOpeningAction", "private static bool SignalContainsToken")
        self.assertIn("IsOpeningAction(masterAccount, context)", execution)
        self.assertIn("IsExitSignal(signal)", classifier)
        self.assertIn("IsEntrySignal(signal)", classifier)
        self.assertIn("TryGetMasterNet(masterAccount, context, out int masterNet)", classifier)
        self.assertIn("ResolveEntryAction(masterAccount, context)", source(COPY_ENGINE))
        self.assertIn("ResolveCloseAction(masterAccount, context)", source(COPY_ENGINE))
        self.assertIn('SignalContainsToken(signal, "entry")', classifier)
        self.assertIn('SignalContainsToken(signal, "close")', classifier)
        self.assertIn('SignalContainsToken(signal, "x")', classifier)
        self.assertIn("context.Action == OrderAction.Buy", classifier)

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

    def test_replication_snapshots_execution_before_dispatch_and_tracks_close_failures(self):
        window = source(MAIN_WINDOW)
        replication = source(REPLICATION_UI)
        copy_engine = source(COPY_ENGINE)
        bridge = method_body(
            window,
            "private void OnAccountRuntimeEventBridge",
            "private void OnAccountRuntimeEventBridgeCore",
        )
        self.assertLess(
            bridge.index("TryBuildCopyExecutionContext(eventArgs, out executionSnapshot)"),
            bridge.index("Dispatcher.BeginInvoke"),
        )
        self.assertIn("GlitchCopyExecutionContext executionSnapshot", replication)
        recovery = method_body(
            copy_engine,
            "private void TrySubmitAttributedRecoveryClose",
            "private FollowerOrderSubmission SubmitFollowerEntry",
        )
        self.assertLess(
            recovery.index("TryGetNetQuantityForInstrument"),
            recovery.index("lifecycle.RecoveryCloseSubmitted = true"),
        )
        self.assertIn('submission.Result, "submitted"', recovery)
        close_tracking = method_body(
            copy_engine,
            "private void TrackCloseOrder",
            "private void TrySubmitAttributedRecoveryClose",
        )
        self.assertIn("FollowerCloseTerminalUnresolved", close_tracking)
        self.assertIn("lifecycle.RecoveryOwner.RecoveryCloseSubmitted = false", close_tracking)

    def test_replication_state_is_truthful_and_reload_is_observe_only(self):
        window = source(MAIN_WINDOW)
        performance = source(ADDON / "UI/MainWindow/GlitchMainWindow.Performance.partial.cs")
        chart_trader = source(ADDON / "GlitchAddOn.ChartTrader.partial.cs")
        self.assertIn("_isReplicatingUi && _copyEngine?.IsEnabled == true", window)
        self.assertIn("SetReplicationFromExternalSurface(!_isReplicatingUi", window)
        self.assertIn("return _isReplicatingUi == enabled;", window)
        self.assertIn("IsReplicating = IsReplicationEnabledFromExternalSurface()", performance)
        self.assertIn("IsReplicationEffective = IsReplicationEffectivelyActiveFromExternalSurface()", performance)
        self.assertIn('"Armed"', chart_trader)
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
        ownership = method_body(
            text,
            "private bool AccountOwnsGlitchReplicationState",
            "private void CancelOwnedOrdersAtFlat",
        )
        self.assertIn("return false;", ownership)
        self.assertNotIn("IsConfiguredFollower", text)

    def test_hermes_portfolio_event_queue_preserves_events_and_surfaces_overflow(self):
        writer = source(HERMES_PORTFOLIO_EVENTS)
        self.assertIn("GlitchAiJsonFields.TryParseObject", writer)
        self.assertIn('parsed["portfolio_events"] is IEnumerable', writer)
        self.assertIn('directive["dropped_event_count"]', writer)
        self.assertIn('Append(sb, "dropped_event_count"', writer)
        self.assertIn("MaxQueuedEvents", writer)

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

    def test_flatten_all_success_ends_copy_lifecycle_before_routes_restore(self):
        copy = source(COPY_ENGINE)
        window = source(MAIN_WINDOW)
        self.assertIn("public void ResetAfterFlattenAll()", copy)
        reset = method_body(copy, "public void ResetAfterFlattenAll", "public void ProcessMasterExecution")
        for lifecycle_map in (
            "_entriesBySignal.Clear()",
            "_closesBySignal.Clear()",
            "_syncByFollowerInstrument.Clear()",
            "_allocationByRouteDirection.Clear()",
            "_entryOrderAllocations.Clear()",
            "_allocationRouteSignatures.Clear()",
        ):
            self.assertIn(lifecycle_map, reset)
        complete = method_body(window, "private async Task<bool> ExecuteFlattenAllCoreAsync", "private void OnCreateGroupClick")
        self.assertIn("if (complete)", complete)
        self.assertIn("_copyEngine?.ResetAfterFlattenAll();", complete)
        self.assertLess(
            complete.index("_copyEngine?.ResetAfterFlattenAll();"),
            complete.index("RefreshAccountData(preferSynchronous: true);"),
        )

    def test_allocation_route_signature_is_explicit_and_not_object_hash_based(self):
        copy = source(COPY_ENGINE)
        signature = method_body(copy, "private static string BuildAllocationRouteSignature", "private static string ResolveMasterOrderIdentity")
        self.assertIn("BuildAllocationRouteKey(route)", signature)
        self.assertIn("BitConverter.DoubleToInt64Bits(route?.Ratio ?? 0)", signature)
        self.assertIn("route?.FollowerAccount?.Name?.Trim()", signature)
        self.assertNotIn("GetHashCode()", signature)

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
        journal = source(JOURNAL_TAB)
        self.assertIn('ItemsSource = new[] { "Master", "Group", "Fleet" }', summary)
        self.assertIn('"Logical Trades"', summary)
        self.assertIn('"Account Trades"', summary)
        self.assertIn("ApplySummaryScope", summary)
        self.assertNotIn("_summaryFleetTradesValueText.Text = FormatSignedCurrency", summary)
        self.assertNotIn("_summaryAccountsValueText.Text = FormatSignedCurrency", summary)
        for field in (
            "_journalTradesValueText",
            "_journalNetPnlValueText",
            "_journalWinRateValueText",
            "_journalAvgWinValueText",
            "_journalAvgLossValueText",
            "_journalProfitFactorValueText",
            "_journalAsOfText",
            "_journalCardsPanel",
        ):
            self.assertIn(field, journal)
        for summary_field in (
            "_summaryTradesValueText",
            "_summaryNetPointsValueText",
            "_summaryWinRateValueText",
            "_summaryFleetTradesValueText",
            "_summaryAccountsValueText",
            "_summaryProfitFactorValueText",
            "_summaryAsOfText",
            "_summaryCardsPanel",
            "_summaryPerformanceGrid",
        ):
            self.assertNotIn(f"out {summary_field}", journal)
            self.assertNotIn(f"{summary_field} =", journal)
        self.assertIn("_journalAvgWinValueText.Text = FormatSignedCurrency(snapshot.All.AvgWinningTradePoints)", summary)
        self.assertIn("_journalAvgLossValueText.Text = FormatSignedCurrency(snapshot.All.AvgLosingTradePoints)", summary)

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

    def test_fundamental_influence_uses_only_fresh_technical_readings(self):
        logic = source(ANALYTICS_LOGIC)
        tab = source(ANALYTICS_TAB)
        self.assertIn("public DateTime UtcTime { get; set; }", logic)
        self.assertIn("UtcTime = source.UtcTime", logic)
        freshness = method_body(
            logic,
            "internal static bool IsReadingFresh(GlitchTimeframeReading reading, DateTime nowUtc)",
            "public IReadOnlyList<string> BuildInstrumentOptions",
        )
        self.assertIn("reading.UtcTime != default", freshness)
        self.assertIn("(nowUtc - reading.UtcTime) <= MaxFeedAge", freshness)
        influence = method_body(tab, "private void ApplyMag7Influence", "private static double ResolveMag7InfluenceWeight")
        self.assertIn("GlitchAnalyticsEngine.IsReadingFresh(x, nowUtc)", influence)
        self.assertNotIn("x.AveragePrice.HasValue || x.AtrProxy.HasValue || x.AdxProxy.HasValue", influence)

    def test_macro_headlines_flow_through_one_immutable_snapshot(self):
        fundamentals = source(FUNDAMENTAL_ANALYSIS)
        snapshot = method_body(fundamentals, "private GlitchFundamentalAnalysisSnapshot BuildSnapshot", "private sealed class SnapshotScratch")
        scratch = method_body(fundamentals, "private SnapshotScratch CaptureSnapshotScratch", "private void CommitSnapshotCarryForward")
        latest = method_body(fundamentals, "private static List<string> BuildLatestHeadlineLines", "private static string NormalizeHeadlineTitle")
        self.assertIn("scratch.MacroHeadlines", snapshot)
        self.assertNotIn("scratch.HeadlinesBySymbol,\n                null,", snapshot)
        self.assertIn("headline.Clone()", scratch)
        self.assertIn("MacroHeadlines = _macroHeadlines", scratch)
        self.assertNotIn("_headlinesBySymbol", latest)
        self.assertNotIn("_macroHeadlines", latest)


if __name__ == "__main__":
    unittest.main()
