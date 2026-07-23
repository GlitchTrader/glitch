"""AI-only ownership boundaries; shared core contracts live under tools/tests."""

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
ADDON = ROOT / "ninjatrader/Glitch/AddOns/GlitchAddOn"
EXECUTOR = ADDON / "Services/Ai/GlitchAiOrderExecutor.cs"
FIREWALL = ADDON / "Services/Ai/GlitchAiRiskFirewall.cs"
POLICY = ADDON / "Services/Ai/GlitchAiRailPolicyStore.cs"
PORTFOLIO_READER = ADDON / "Services/Ai/GlitchAiPortfolioSnapshotReader.cs"
TELEMETRY_UI = ADDON / "UI/MainWindow/GlitchMainWindow.Telemetry.partial.cs"
INTENT_VALIDATOR = ADDON / "Services/Ai/GlitchAiIntentValidator.cs"
JSON_FIELDS = ADDON / "Services/Ai/GlitchAiJsonFields.cs"
TRADING_WINDOW = ADDON / "Services/Ai/GlitchAiTradingWindow.cs"
TEMPORAL_CLOSE = ADDON / "UI/MainWindow/GlitchMainWindow.AiTemporalCompliance.partial.cs"
SETTINGS_TAB = ADDON / "UI/MainWindow/GlitchMainWindow.SettingsTab.partial.cs"
RUNTIME_POLICY = ADDON / "Services/Persistence/GlitchRuntimePolicyStore.cs"
LOCALIZATION = ADDON / "Resources/Localization.tsv"
PORTFOLIO_WRITER = ADDON / "Services/Persistence/GlitchPortfolioSnapshotWriter.cs"
EXCHANGE_WRITER = ADDON / "Services/Persistence/GlitchHermesExchangeWriter.cs"
HEALTH = ADDON / "Services/Persistence/GlitchAiHealthEvaluator.cs"
STATE_STORE = ADDON / "Services/Ai/GlitchAiIntentStateStore.cs"
INTENT_SERVER = ADDON / "Services/Ai/GlitchAiIntentServer.cs"
MAIN_WINDOW = ADDON / "UI/MainWindow/GlitchMainWindow.cs"
ADDON_SHELL = ADDON / "GlitchAddOn.cs"


def source(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def method_body(text: str, signature: str, next_signature: str) -> str:
    return text.split(signature, 1)[1].split(next_signature, 1)[0]


class AiSourceArchitectureContractTests(unittest.TestCase):
    def test_intent_v3_is_exact_leg_and_v2_compatibility_is_bounded(self):
        validator = source(INTENT_VALIDATOR)
        executor = source(EXECUTOR)
        self.assertIn('"glitch.intent.v2"', validator)
        self.assertIn('"glitch.intent.v3"', validator)
        self.assertIn("ValidateProtectionUpdates(parsed, false", validator)
        self.assertIn("ValidateProtectionUpdates(parsed, true", validator)
        self.assertIn("protection_update_duplicate_leg_id_", validator)
        self.assertIn("legacy_move_tp_scope_ambiguous", executor)
        self.assertIn("TryGetLegId", executor)
        self.assertIn('legId = parts[0] + ":" + legIndex', executor)

    def test_intent_state_claim_precedes_firewall_and_execution(self):
        server = source(INTENT_SERVER)
        store = source(STATE_STORE)
        executor = source(EXECUTOR)
        self.assertLess(server.index("GlitchAiIntentStateStore.TryClaim"), server.index("GlitchAiRiskFirewall.Validate"))
        self.assertLess(server.index('"execution_started"'), server.index("TryExecuteApprovedIntent"))
        for phase in ("received", "approved", "rejected", "execution_started", "execution_visibility_pending", "pending", "executed", "failed"):
            self.assertIn('"' + phase + '"', server + store)
        self.assertIn("intent_id_content_conflict", server)
        self.assertIn("TryReconstructLegacyState", store)
        self.assertIn("TryReconcileStartedIntent", server)
        self.assertIn("BuildIntentCorrelation", executor)
        self.assertNotIn("IntentExecutionSync", server)
        self.assertIn("TryPromoteToExecutionStarted", store)
        promote = server.index("TryPromoteToExecutionStarted")
        execute = server.rindex("TryExecuteApprovedIntent")
        self.assertLess(promote, execute)
        self.assertIn("GlitchAiExecutionResult.Pending", executor)
        self.assertIn("HasExactCorrelationOwnedProtection", executor)
        self.assertNotIn("HasExactReconciledEntryExposure", executor)
        self.assertIn("reconcile_entry_recovery_close_submitted", executor)

    def test_intent_body_limit_uses_raw_utf8_bytes(self):
        server = source(INTENT_SERVER)
        body = method_body(server, "private static string ReadRequestBody", "private static string BuildHealthJson")
        self.assertIn("byte[] buffer", body)
        self.assertIn("total > MaxBodyBytes", body)
        self.assertNotIn("StreamReader", body)
        self.assertNotIn("char[] buffer", body)

    def test_restart_reconciles_without_time_created_replay_authority(self):
        server = source(INTENT_SERVER)
        self.assertIn("resumeReceivedClaim", server)
        self.assertIn("continueApprovedClaim", server)
        self.assertIn("reconcileExistingClaim", server)
        self.assertIn('"execution_started"', server)
        self.assertIn('"execution_visibility_pending"', server)
        self.assertIn('"pending"', server)

        reconciliation = method_body(
            server,
            "bool reconcileExistingClaim",
            "bool continueApprovedClaim",
        )
        self.assertIn("GlitchAiOrderExecutor.TryReconcileStartedIntent", reconciliation)
        self.assertIn('intentState.Phase, "execution_started"', reconciliation)
        self.assertIn('reconciled.Code, "reconcile_entry_not_found"', reconciliation)
        self.assertIn('reconciled.Code, "reconcile_exit_not_found"', reconciliation)
        self.assertIn('intentState.Phase, "execution_visibility_pending"', reconciliation)
        self.assertNotIn("TryExecuteApprovedIntent", reconciliation)
        self.assertNotIn("NativeOrderVisibilitySettleInterval", reconciliation)
        self.assertIn("native_visibility_unresolved", reconciliation)
        self.assertIn("ConnectionStatus.Connected", source(EXECUTOR))

        resume = method_body(
            server,
            "bool continueApprovedClaim",
            "Action<string, string, string> handler",
        )
        self.assertIn("if (!continueApprovedClaim)", resume)
        self.assertIn("GlitchAiRiskFirewall.Validate", resume)
        self.assertIn('TrySavePhase(intentState, "approved"', resume)
        self.assertNotIn("TryReconcileStartedIntent", resume)

    def test_ai_protection_reconcile_can_resize_partial_oco_units_and_reports_failure(self):
        executor = source(EXECUTOR)
        trim = method_body(
            executor,
            "private static void ResizeAiProtection",
            "public static bool IsExecutionEnabled",
        )
        self.assertIn("QuantityChanged", trim)
        self.assertIn("account.Change(changes.ToArray())", trim)
        self.assertIn("RaiseCritical", trim)

    def test_one_minute_publisher_is_paired_gap_aware_and_not_modulo_gated(self):
        exchange = source(EXCHANGE_WRITER)
        market = source(ADDON / "Services/Persistence/GlitchMarketSnapshotWriter.cs")
        portfolio = source(PORTFOLIO_WRITER)
        main = source(MAIN_WINDOW)
        self.assertIn("TryPublishMinute", exchange)
        self.assertIn("TryWriteDecisionPacket", exchange)
        self.assertIn('\\"is_contiguous\\"', exchange)
        self.assertIn('\\"observed_span_minutes\\"', exchange)
        self.assertIn('\\"missing_minute_ids\\"', exchange)
        self.assertIn("Take(FramesPerPacket)", exchange)
        self.assertIn("GlitchHermesExchangeWriter.TryBeginMinutePublish", main)
        self.assertIn("GlitchHermesExchangeWriter.QueuePublishMinute", main)
        self.assertNotIn("TryWriteLatestIfDue", market + portfolio)
        self.assertNotIn("Minute %", exchange)

    def test_publisher_skips_native_capture_and_packet_work_after_the_minute_is_complete(self):
        exchange = source(EXCHANGE_WRITER)
        main = source(MAIN_WINDOW)
        preflight = method_body(exchange, "public static bool TryBeginMinutePublish", "public static bool QueuePublishMinute")
        publisher = method_body(exchange, "public static bool TryPublishMinute", "public static string GetExchangeRoot")
        packet_writer = method_body(exchange, "private static bool TryWriteDecisionPacket", "private static string BuildFrameJson")

        self.assertIn("BackgroundPublishInFlight", preflight)
        self.assertIn("preflightOnly = !PreflightComplete", preflight)
        self.assertIn("needsPortfolioCapture = !preflightOnly && NativeCaptureRequired", preflight)
        self.assertNotIn("File.", preflight)
        self.assertIn("TryBeginMinutePublish(", main)
        self.assertIn("out bool preflightOnly", main)
        self.assertIn("RecordDispatcherCaptureDuration", main)
        self.assertIn("TryCaptureSnapshotJson(nowUtc, minuteId, out marketSnapshotJson)", main)
        self.assertIn("QueuePublishMinute(", main)
        self.assertIn("ReleaseMinutePublishOwnership", main)
        self.assertIn("CachedMinuteUtc == minuteUtc && CachedPacketComplete", publisher)
        self.assertNotIn("CachedPacketUnavailable", exchange)
        self.assertLess(
            publisher.index("CachedMinuteUtc == minuteUtc && CachedPacketComplete"),
            publisher.index("string minuteId"),
        )
        self.assertIn("frameComplete = true;", publisher)
        self.assertIn("PreflightComplete = packetComplete;", publisher)
        self.assertIn("TryPreflightMinute", exchange)
        self.assertLess(packet_writer.index("File.Exists(packetPath)"), packet_writer.index("new DirectoryInfo"))
        self.assertLess(packet_writer.index("File.Exists(packetPath)"), packet_writer.index("ComputeStableHash"))
        self.assertIn("ThreadPool.QueueUserWorkItem", exchange)
        self.assertIn("GetPublisherTimingSummary", exchange)
        self.assertIn("if (!ThreadPool.QueueUserWorkItem(publish))", exchange)
        self.assertIn("ClearBackgroundPublishInFlight(minuteUtc)", exchange)
        for metric in (
            "native_position_lock_p95_ms",
            "native_position_lock_p99_ms",
            "native_order_lock_p95_ms",
            "native_order_lock_p99_ms",
            "analytics_bus_lock_p95_ms",
            "analytics_bus_lock_p99_ms",
        ):
            self.assertIn(metric, exchange)
        portfolio_capture = source(ADDON / "UI/MainWindow/GlitchMainWindow.PortfolioSnapshot.partial.cs")
        analytics_bus = source(ADDON / "UI/Analytics/GlitchAnalyticsFeedBus.cs")
        self.assertIn("RecordNativePositionCollectionLockDuration", portfolio_capture)
        self.assertIn("RecordNativeOrderCollectionLockDuration", portfolio_capture)
        self.assertIn("RecordAnalyticsBusCollectionLockDuration", analytics_bus)

    def test_hidden_window_preserves_runtime_and_true_addon_shutdown_stops_it(self):
        main = source(MAIN_WINDOW)
        shell = source(ADDON_SHELL)
        refresh = source(ADDON / "UI/MainWindow/GlitchMainWindow.RefreshPipeline.partial.cs")
        self.assertIn("e.Cancel = true", main)
        self.assertIn("Hide();", main)
        self.assertIn("ShutdownForAddOn", main)
        self.assertIn("RefreshHiddenRuntimeSafetyIfDue", main)
        self.assertIn("MaybeEnforceAiDailyClose", refresh)
        self.assertIn("ProcessAccountStateUpdate", refresh)
        self.assertIn("glitchWindow.ShutdownForAddOn()", shell)
        self.assertIn("StopRailInfrastructure();", main)

    def test_health_reports_components_and_server_restart_is_bounded(self):
        health = source(HEALTH)
        telemetry = source(TELEMETRY_UI)
        for component in (
            "PacketStatus", "DecisionWorkerStatus", "LearningWorkerStatus",
            "TelemetryServerRunning", "IntentServerRunning", "ControlServerRunning",
            "SelectedMasterNativeState", "SnapshotHash", "FeedAgeSeconds",
        ):
            self.assertIn(component, health)
        self.assertIn("decision_worker_overdue", health)
        self.assertIn("operator_job_disabled", health)
        self.assertIn("EnsureRailInfrastructureIfDue", telemetry)
        self.assertIn("TimeSpan.FromSeconds(30)", telemetry)
        self.assertIn("TelemetryServerUnavailable", telemetry)
        self.assertIn("IntentServerUnavailable", telemetry)
        self.assertIn("ControlServerUnavailable", telemetry)

    def test_preflight_uses_single_trading_control_not_legacy_policy_gates(self):
        preflight = source(ROOT / "tools" / "hermes" / "preflight-open.ps1")
        self.assertIn("trading_enabled = -not [bool]$control.trading_paused", preflight)
        self.assertNotIn("policy_ai_enabled", preflight)
        self.assertNotIn("kill_switch_off", preflight)
        self.assertNotIn("executor_enabled", preflight)

        submit = source(ROOT / "tools" / "hermes" / "submit-validated-sim-intent.ps1")
        exit_submit = source(ROOT / "tools" / "hermes" / "submit-validated-sim-exit.ps1")
        self.assertNotIn("mode = 'sim'", submit)
        self.assertNotIn("executor_enabled", submit)
        self.assertNotIn("mode = 'sim'", exit_submit)
        self.assertNotIn("executor_enabled", exit_submit)
        self.assertIn("[IO.FileMode]::Open", submit)
        self.assertIn("[IO.FileMode]::CreateNew", submit)
        self.assertIn("[IO.FileMode]::Open", exit_submit)
        self.assertIn("[IO.FileMode]::CreateNew", exit_submit)

    def test_ai_executor_resolves_and_submits_only_the_master(self):
        executor = source(EXECUTOR)
        resolver = method_body(
            executor,
            "private static bool TryResolveExecutionGroup",
            "private static GlitchAiExecutionResult TryExecuteGroupEnter",
        )
        entry = method_body(
            executor,
            "private static GlitchAiExecutionResult TryExecuteGroupEnter",
            "private static bool TryGetEntryAccountIndex",
        )
        self.assertEqual(resolver.count("members.Add("), 1)
        self.assertIn("selected.MasterAccount", resolver)
        self.assertIn("masterMember.Account.Submit", entry)
        self.assertNotIn("FollowerAccount", resolver)

    def test_follower_admission_never_blocks_master_submit(self):
        executor = source(EXECUTOR)
        entry = method_body(
            executor,
            "private static GlitchAiExecutionResult TryExecuteGroupEnter",
            "private static bool TryGetEntryAccountIndex",
        )
        self.assertNotIn("GetReplicationEntryDenialReason", entry)
        self.assertNotIn("GlitchAiOrderExecutor.GetReplicationEntryDenialReason", source(TELEMETRY_UI))

    def test_ai_does_not_override_hermes_with_inferred_direction_policy(self):
        telemetry = source(TELEMETRY_UI)
        executor = source(EXECUTOR)
        self.assertNotIn("GetAiEntryDenialReason", telemetry)
        self.assertNotIn("GlitchApexDirectionGuard", executor)
        self.assertNotIn("apex_direction_compliance_rejected", executor)

    def test_ai_does_not_treat_replication_routes_as_a_cognitive_gate(self):
        telemetry = source(TELEMETRY_UI)
        self.assertNotIn("expectedFollowerCount", telemetry)
        self.assertNotIn("replication_routes_incomplete", telemetry)

    def test_invalid_policy_cannot_arm_or_pass_the_firewall(self):
        executor = source(EXECUTOR)
        firewall = source(FIREWALL)
        policy = source(POLICY)
        self.assertIn("public bool IsValid", policy)
        self.assertIn("policy_load_failed_", policy)
        self.assertIn("policy_schema_invalid", policy)
        self.assertIn("policy_key_duplicated_", policy)
        self.assertIn('"glitch.ai.policy.v2"', policy)
        self.assertIn("WriteAtomically(path, json)", policy)
        self.assertNotIn("public string Mode", policy)
        self.assertNotIn("public bool AiEnabled", policy)
        self.assertNotIn("public bool AiKillSwitch", policy)
        self.assertNotIn("policy.Mode", executor)
        self.assertIn("&& policy.IsValid", executor)
        self.assertIn('"policy_invalid"', executor)
        self.assertIn('"policy_invalid"', firewall)

    def test_ai_move_stop_changes_master_only(self):
        executor = source(EXECUTOR)
        move_stop = method_body(
            executor,
            "private static GlitchAiExecutionResult TryExecuteGroupMoveStop",
            "private static GlitchAiExecutionResult TryExecuteGroupMoveTarget",
        )
        self.assertIn("Account masterAccount = members[0].Account", move_stop)
        self.assertIn("masterAccount.Change", move_stop)
        self.assertIn("move_stop_amendment_pending", move_stop)
        self.assertIn("TrackPendingAmendment", executor)
        self.assertIn("TryFinalizePendingAmendments", executor)
        self.assertNotIn("ValidateProposedStopState", move_stop)
        self.assertNotIn("Follower", move_stop)

    def test_ai_move_target_changes_master_and_replication_mirrors_target_and_optional_stop(self):
        executor = source(EXECUTOR)
        copy_engine = source(ADDON / "Services/Trading/GlitchCopyEngine.cs")
        validator = source(INTENT_VALIDATOR)
        firewall = source(FIREWALL)
        move_target = method_body(
            executor,
            "private static GlitchAiExecutionResult TryExecuteGroupMoveTarget",
            "private static Order FindNamedOrder",
        )
        mirror = method_body(
            copy_engine,
            "private void MirrorMasterProtection",
            "private void CleanupFlatFollowerOrders",
        )

        self.assertIn("protection_updates", move_target)
        self.assertIn("LimitPriceChanged = proposedTargets[target]", move_target)
        self.assertIn("StopPriceChanged = proposedStops[stop]", move_target)
        self.assertEqual(move_target.count("masterAccount.Change"), 1)
        self.assertIn('isStop ? "-S-" : "-T-"', mirror)
        self.assertIn("followerOrder.LimitPriceChanged = masterPrice", mirror)
        self.assertIn("followerOrder.StopPriceChanged = masterPrice", mirror)
        self.assertIn('"MOVE_TP"', validator)
        self.assertIn("move_tp_requires_protection_updates", validator)
        self.assertIn('string.Equals(action, "MOVE_TP"', firewall)

    def test_working_partial_master_fill_aggregates_before_protection(self):
        executor = source(EXECUTOR)
        partial = executor.split("int entryAccountIndex;", 1)[1].split(
            "&& order.OrderState == OrderState.Filled)",
            1,
        )[0]
        self.assertIn("GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)", partial)
        self.assertIn("group_terminal_partial_entry_fill_recovery", partial)

    def test_master_protection_reconciles_by_stable_identity_and_fails_closed(self):
        executor = source(EXECUTOR)
        telemetry = source(TELEMETRY_UI)
        refresh = source(
            ADDON / "UI/MainWindow/GlitchMainWindow.RefreshPipeline.partial.cs"
        )
        identity = method_body(
            executor,
            "private static bool TryGetEntryAccountIndex",
            "private static int FindGroupAccountIndex",
        )
        account_reconcile = method_body(
            executor,
            "public static void ProcessAccountStateUpdate",
            "public static bool IsExecutionEnabled",
        )
        protection = method_body(
            executor,
            "private static void ReconcileEntryProtection",
            "private static GlitchAiExecutionResult TrySubmitStructuralProtection",
        )

        self.assertNotIn("ReferenceEquals", identity)
        self.assertIn("expectedSignal", identity)
        self.assertIn("order.Account.Name", identity)
        self.assertIn("SameInstrument", identity)
        self.assertIn("FindNamedOrder", account_reconcile)
        self.assertIn("ReconcileAiOwnedProtectionFromPosition(account)", account_reconcile)
        self.assertNotIn("master_protection_reconcile_timeout", account_reconcile)
        self.assertNotIn("ProtectionReconcileGrace", executor)
        self.assertIn("TrySubmitStructuralProtection", protection)
        self.assertIn("RecoverGroup", protection)
        self.assertIn('ReconcileEntryProtection(group, 0, entryOrder, "submit_return")', executor)
        self.assertIn("group.ProtectionSubmitted[accountIndex] = true", executor)
        structural = method_body(
            executor,
            "private static GlitchAiExecutionResult TrySubmitStructuralProtection",
            "private static double RoundToTick",
        )
        self.assertLess(
            structural.index("group.ProtectionSubmitted[accountIndex] = true"),
            structural.index("CreateExitOrder(account"),
        )
        self.assertLess(
            structural.index("group.ProtectionSubmitted[accountIndex] = true"),
            structural.index("account.Submit(createdOrders.ToArray())"),
        )
        self.assertIn("ReleaseProtectionSubmission(group, accountIndex, createdOrders)", structural)
        release = method_body(
            executor,
            "private static void ReleaseProtectionSubmission",
            "private static double RoundToTick",
        )
        self.assertIn("group.ProtectionSubmitted[accountIndex] = false", release)
        self.assertIn("GlitchAiOrderExecutor.ProcessAccountStateUpdate(activeAccount)", refresh)
        self.assertNotIn("GlitchAiOrderExecutor.GetReplicationEntryDenialReason", telemetry)

    def test_intent_recovery_preserves_native_human_overrides(self):
        executor = source(EXECUTOR)
        recovery = method_body(
            executor,
            "private static GlitchAiExecutionResult ReconcileStartedIntentOnUiThread",
            "private static GlitchAiExecutionResult TryRecoverReconciledEntryProtection",
        )
        owned_close = method_body(
            executor,
            "private static string TryCloseAttributableEntryDelta",
            "private static void TryCompleteGroup",
        )
        self.assertIn("reconciled_entry_native_protected", recovery)
        self.assertIn("reconcile_entry_superseded_manual_or_concurrent_intent", recovery)
        self.assertIn("reconcile_entry_native_identity_ambiguous", recovery)
        self.assertIn("reconcile_entry_visibility_changing", recovery)
        self.assertIn("TryRecoverReconciledEntryProtection", recovery)
        self.assertNotIn("Account.Flatten", owned_close)
        self.assertIn("GlitchAiEntryBaselinePlanStore.TryLoad", owned_close)
        self.assertIn("TryCloseReconciledEntryDelta", owned_close)

    def test_ai_exit_never_uses_broad_instrument_flatten(self):
        executor = source(EXECUTOR)
        exit_path = method_body(
            executor,
            "private static GlitchAiExecutionResult TryExecuteGroupExit",
            "private static GlitchAiExecutionResult TryExecuteGroupMoveStop",
        )
        reconciliation = method_body(
            executor,
            "private static GlitchAiExecutionResult ReconcileStartedIntentOnUiThread",
            "private static GlitchAiExecutionResult TryRecoverReconciledEntryProtection",
        )
        self.assertNotIn("Account.Flatten", exit_path)
        self.assertIn("TryCollectActiveOwnedExposure", exit_path)
        self.assertIn("plan.Account.Submit(new[] { plan.Order });", exit_path)
        self.assertIn("TryFindNamedOrders(plan.Account, plan.Order.Name", exit_path)
        self.assertIn("IsNativeExitActionable(observedExits[0])", exit_path)
        self.assertIn("group_exit_native_visibility_pending", exit_path)
        self.assertIn("TryCancelActiveOwnedProtection", exit_path)
        self.assertLess(exit_path.index("plan.Account.Submit"), exit_path.index("TryCancelActiveOwnedProtection"))
        self.assertLess(exit_path.index("IsNativeExitActionable(observedExits[0])"), exit_path.index("TryCancelActiveOwnedProtection"))
        self.assertIn("group_exit_submit_ambiguous", exit_path)
        self.assertIn("BuildSignalName(SignalExit, exitCorrelation, accountIndex)", exit_path)
        self.assertIn("group_exit_superseded_manual_override", exit_path)
        self.assertIn("GlitchAiExitOwnershipPlanStore.TryPersist", exit_path)
        self.assertIn("group_exit_ownership_plan_unavailable", exit_path)
        self.assertIn("owned.Quantity != Math.Abs(net)", exit_path)
        self.assertIn("TryReconcileOwnedExit", reconciliation)
        self.assertIn("reconcile_exit_not_found", executor)
        self.assertIn("reconcile_exit_native_visibility_pending", executor)
        self.assertIn("reconcile_exit_ownership_plan_identity_ambiguous", executor)
        self.assertIn("reconcile_exit_superseded_manual_or_concurrent_intent", executor)
        self.assertIn("TryCollectActiveOwnedExposureForCorrelations", executor)
        self.assertIn("ArePlannedProtectionOrdersTerminalOrAbsent", executor)
        self.assertIn("reconcile_exit_protection_cancel_pending", executor)
        self.assertIn("reconcile_exit_protection_cancel_request_failed", executor)
        exit_reconcile = method_body(executor, "private static GlitchAiExecutionResult TryReconcileOwnedExit", "private static GlitchAiExecutionResult ReconcilePlannedExitProtection")
        self.assertIn("IsNativeOrderVisibilityReady(account)", exit_reconcile)
        self.assertIn("account_connection_not_connected", exit_reconcile)
        self.assertIn("private static bool IsNativeExitActionable", executor)
        self.assertIn("reconcile_exit_superseded_manual_or_concurrent_intent", executor)
        self.assertNotIn("TryCancelAllActiveOwnedProtection", executor)
        self.assertNotIn("TryFindSingleOwnedEntry", executor)

    def test_entry_addition_recovery_uses_durable_exact_baseline(self):
        executor = source(EXECUTOR)
        self.assertIn("GlitchAiEntryBaselinePlanStore.TryPersist", executor)
        self.assertIn("TryPrepareEntryBaselinePlan", executor)
        self.assertIn("entry_baseline_superseded_manual_or_concurrent_intent", executor)
        self.assertIn("reconcile_entry_superseded_manual_or_concurrent_intent", executor)
        self.assertIn("HasExactBaselineProtection", executor)
        self.assertIn("actualNet == plan.BaselineNet + (entryDirection * entry.Filled)", executor)
        self.assertIn("reconcile_entry_protection_cancel_pending", executor)
        self.assertIn("TryCloseReconciledEntryDelta", executor)
        self.assertIn("reconcile_entry_recovery_close_submitted", executor)
        self.assertIn("reconcile_entry_recovery_close_visibility_unresolved", executor)
        self.assertNotIn("TimeSpan.FromSeconds(30)", executor)
        entry_plan = source(ADDON / "Services/Ai/GlitchAiEntryBaselinePlanStore.cs")
        self.assertIn("TryBeginRecoveryClose", entry_plan)
        self.assertNotIn("TryMarkRecoveryCloseResumeUsed", entry_plan)
        self.assertNotIn("RecoveryCloseResumeUsed", entry_plan)
        self.assertIn("RecoveryCloseStartedUtc", entry_plan)
        self.assertNotIn("RecoveryCloseSubmitted", executor)
        self.assertIn("IsDurableRecoveryCloseTerminal", executor)

    def test_intent_ownership_plans_publish_only_complete_atomic_files(self):
        exit_plan = source(ADDON / "Services/Ai/GlitchAiExitOwnershipPlanStore.cs")
        entry_plan = source(ADDON / "Services/Ai/GlitchAiEntryBaselinePlanStore.cs")
        for plan in (exit_plan, entry_plan):
            self.assertIn('".tmp"', plan)
            self.assertIn("FileMode.CreateNew", plan)
            self.assertIn("stream.Flush(true)", plan)
            self.assertIn("File.Move(temporary, path)", plan)
            self.assertIn("File.Delete(temporary)", plan)
            self.assertLess(plan.index("stream.Flush(true)"), plan.index("File.Move(temporary, path)"))

    def test_ai_preserves_absolute_structural_prices_without_arbitrary_slippage_veto(self):
        executor = source(EXECUTOR)
        self.assertIn("public double StopPrice", executor)
        self.assertIn("public double TargetPrice", executor)
        self.assertIn("structural_prices=preserved", executor)
        self.assertIn("IsExecutableBracketPrice", executor)
        self.assertNotIn("group_entry_geometry_changed_reassess", executor)
        self.assertNotIn("liveGeometryWorsened", executor)
        self.assertIn("liveExecutionPrice", executor)
        self.assertNotIn("apex_liquidation_buffer_exceeded", executor)
        self.assertNotIn("ValidateProposedStopState", executor)
        self.assertNotIn("public double StopDistance", executor)
        self.assertNotIn("public double TargetDistance", executor)
        self.assertNotIn("re-anchor", executor)

    def test_ai_uses_authoritative_account_and_native_state_without_inferred_capacity_vetoes(self):
        executor = source(EXECUTOR)
        firewall = source(FIREWALL)
        policy = source(POLICY)
        portfolio_reader = source(PORTFOLIO_READER)
        for retired in (
            "MaxRiskPerContractUsd",
            "MaxLossPerTradeUsd",
            "MaxGroupLossPerTradeUsd",
            "MaxDailyLossUsd",
        ):
            self.assertNotIn(retired, executor + firewall + policy)
        self.assertNotIn('TryExtractNumber(portfolioAccountJson, "max_contracts"', executor)
        self.assertNotIn('TryExtractNumber(portfolioAccountJson, "max_contracts"', firewall)
        self.assertNotIn("GetReplicationEntryDenialReason", executor)
        self.assertIn("TryGetNetPosition(members[0].Account", executor)
        self.assertIn("TryHasWorkingNonProtectionOrder(masterAccount", executor)
        self.assertIn("lock (account.Positions)", executor)
        self.assertIn("TryGetOpenPositionQuantityFromAccountBlock", firewall)
        self.assertIn('ExtractString(positionJson, "market_position")', portfolio_reader)

    def test_prop_firm_capacity_and_buffer_remain_observational_packet_evidence(self):
        firewall = source(FIREWALL)
        executor = source(EXECUTOR)
        reader = source(PORTFOLIO_READER)
        snapshot = source(ROOT / "ninjatrader/Glitch/AddOns/GlitchAddOn/UI/MainWindow/GlitchMainWindow.PortfolioSnapshot.partial.cs")
        self.assertNotIn('"ApexTraderFunding"', firewall + executor)
        self.assertNotIn('"buffer_margin"', firewall + executor)
        self.assertNotIn("apex_liquidation_buffer_exceeded", firewall + executor)
        self.assertNotIn("TryComputeOwnedProtectedDownsideUsdFromAccountBlock", firewall + executor)
        self.assertIn("GlitchAiOrderExecutor.SignalStop", reader)
        self.assertIn("GlitchAiOrderExecutor.SignalTarget", reader)
        self.assertIn("stopCoverage != expectedCoverage || targetCoverage != expectedCoverage", reader)
        self.assertNotIn("riskPercent", firewall)
        self.assertNotIn("RiskPercent", firewall)
        self.assertIn("simulateApexLegacyEval && ruleFirm != null", snapshot)
        self.assertIn("CalculateMinMargin", snapshot)
        self.assertIn("BuildPeakStateKey", snapshot)

    def test_ai_snapshot_expiry_matches_one_flat_decision_window(self):
        policy = source(POLICY)
        self.assertIn("SnapshotMaxAgeSeconds { get; set; } = 300", policy)
        self.assertIn('"snapshot_max_age_seconds\\\":300', policy)

    def test_ai_contract_supports_three_native_protection_legs(self):
        executor = source(EXECUTOR)
        firewall = source(FIREWALL)
        self.assertIn('"take_profit_3"', executor)
        self.assertIn('"quantity_tp2"', executor)
        self.assertIn('"stop_loss_3"', executor)
        self.assertIn("quantityTp3 = masterQuantity - quantityTp1 - quantityTp2", executor)
        self.assertNotIn("tp2_not_beyond_tp1", firewall)
        self.assertNotIn("tp3_not_beyond_tp2", firewall)
        self.assertNotIn("stop_loss_2_not_tighter", firewall)
        self.assertNotIn("stop_loss_3_not_tighter", firewall)

    def test_intent_ingress_is_strict_json_and_market_only(self):
        validator = source(INTENT_VALIDATOR)
        fields = source(JSON_FIELDS)
        self.assertIn("TryParseObject", fields)
        self.assertIn("body_must_be_valid_json_object", validator)
        self.assertIn("AllowedFields", validator)
        self.assertIn("unknown_field_", validator)
        self.assertIn("entry_must_be_market_only", validator)
        self.assertIn("intent_id_must_be_uuid", validator)
        self.assertIn("decision_audit_unknown_", validator)
        for required in ('"prompt_version"', '"reason"', '"decision_audit"'):
            self.assertIn(required, validator)

    def test_entry_session_window_is_packet_evidence_not_an_ai_veto(self):
        firewall = source(FIREWALL)
        executor = source(EXECUTOR)
        window = source(TRADING_WINDOW)
        writer = source(PORTFOLIO_WRITER)
        self.assertNotIn("trading_window_closed", firewall + executor)
        self.assertNotIn("session_lockout", firewall + executor)
        for field in (
            "trading_start_time_et", "trading_end_time_et",
            "entry_window_open", "must_flat_utc", "seconds_until_must_flat",
            "native_state_available",
        ):
            self.assertIn(field, writer)
        self.assertIn("NativeStateAvailable", writer)
        self.assertIn("native_state_available", source(PORTFOLIO_READER))
        self.assertIn("portfolio_native_state_unavailable_", source(PORTFOLIO_READER))
        direct_cycle = source(ROOT / "tools" / "hermes" / "run-direct-glitch-cycle.py")
        self.assertNotIn("any_flat_book_is_entry_eligible", direct_cycle)
        self.assertNotIn('master.get("native_state_available") is not True', direct_cycle)
        self.assertIn("working_order_details", writer)
        self.assertIn("DayOfWeek.Saturday", window)
        self.assertIn("DayOfWeek.Sunday", window)
        self.assertIn("DayOfWeek.Friday", window)
        self.assertIn("Eastern Standard Time", window)

    def test_daily_close_requires_explicit_persisted_consent_and_never_expands_ai_scope(self):
        temporal = source(TEMPORAL_CLOSE)
        settings = source(SETTINGS_TAB)
        policy = source(RUNTIME_POLICY)
        rows = {
            row.split("\t", 1)[0]: row.split("\t")
            for row in source(LOCALIZATION).splitlines()
            if row
        }

        self.assertIn("public bool EnforceAiDailyClose { get; set; } = false;", policy)
        self.assertIn('"ENFORCE_AI_DAILY_CLOSE"', policy)
        self.assertNotIn("EnforceStrategyComplianceActions", policy)
        self.assertLess(temporal.index("!_runtimePolicySettings.EnforceAiDailyClose"), temporal.index("GlitchAiRailPolicyStore.Load"))
        self.assertNotIn("group.MasterAccount", temporal)
        self.assertNotIn("member.FollowerAccount", temporal)
        self.assertIn("var requiredNames", temporal)
        self.assertIn("requiredNames.Contains(candidate.Name.Trim())", temporal)
        self.assertNotIn("TradingPaused", temporal)
        self.assertNotIn("IsApexOrSimAccount", temporal)
        self.assertIn("_aiDailyCloseAttemptedForUtc == mustFlatUtc", temporal)
        self.assertIn("GlitchReplicationEngine.TryFlattenAccount", temporal)
        self.assertIn("WaitForAllAccountsFlatAsync", temporal)
        self.assertIn("_isFlattenAllInProgress = true", temporal)
        self.assertIn("AiDailyCloseAccountUnavailable", temporal)
        self.assertIn("result=unresolved", temporal)
        self.assertIn("result=flat_order_free", temporal)
        self.assertIn("_settingsAiDailyCloseCheckBox", settings)
        self.assertIn("_runtimePolicySettings.EnforceAiDailyClose ? \"enabled\" : \"disabled\"", settings)
        daily_close_ui = method_body(settings, "private Expander BuildAiDailyCloseOptIn", "private Expander BuildComplianceFeatureExpander")
        self.assertIn('BuildScopeCheckBox(\n                "settings.risk.enable"', daily_close_ui)
        self.assertIn("TextBlock", daily_close_ui)
        self.assertIn("BuildAiDailyCloseScopeDescription", daily_close_ui)
        self.assertEqual(len(rows["settings.risk.enforce_ai_daily_close"]), 7)
        self.assertTrue(any("AI" in value or "ИИ" in value for value in rows["settings.risk.enforce_ai_daily_close"]))
        self.assertTrue(any("自动" in value for value in rows["settings.risk.enforce_ai_daily_close"]))
        self.assertTrue(any("ИИ" in value for value in rows["settings.risk.enforce_ai_daily_close"]))
        self.assertNotIn("Submit(", temporal)

    def test_master_protection_coverage_never_counts_follower_copy_orders(self):
        executor = source(EXECUTOR)
        body = method_body(
            executor,
            "private static bool HasCompleteAiProtection",
            "private static bool TryHasWorkingNonProtectionOrder",
        )
        self.assertIn("IsOwnedStopSignal", body)
        self.assertIn("IsOwnedTargetSignal", body)
        self.assertIn('StartsWith(SignalStop + "-"', executor)
        self.assertIn('StartsWith(SignalTarget + "-"', executor)
        self.assertNotIn("GLT-COPY", body)


if __name__ == "__main__":
    unittest.main()
