"""Small source-level guardrail set for the active Glitch architecture.

These tests intentionally protect only high-value ownership and lifecycle
boundaries. They are not a substitute for NinjaTrader compilation or Sim
acceptance; see README.md.
"""

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
ADDON = ROOT / "ninjatrader/Glitch/AddOns/GlitchAddOn"
COPY_ENGINE = ADDON / "Services/Trading/GlitchCopyEngine.cs"
EXECUTOR = ADDON / "Services/Ai/GlitchAiOrderExecutor.cs"
POLICY = ADDON / "Services/Ai/GlitchAiRailPolicyStore.cs"
VALIDATOR = ADDON / "Services/Ai/GlitchAiIntentValidator.cs"
PORTFOLIO = ADDON / "Services/Ai/GlitchAiPortfolioSnapshotReader.cs"
APEX_GUARD = ADDON / "Services/Risk/GlitchApexDirectionGuard.cs"
MAIN_WINDOW = ADDON / "UI/MainWindow/GlitchMainWindow.cs"
REPLICATION = ADDON / "UI/MainWindow/GlitchMainWindow.Replication.partial.cs"
INSIGHTS = ADDON / "Services/Insights/GlitchTradeInsightsService.cs"
DIRECT_CYCLE = ROOT / "tools/hermes/run-direct-glitch-cycle.py"
DIRECT_INSTALLER = ROOT / "tools/hermes/install-direct-hermes-bridge.ps1"
DIRECT_CRON = ROOT / "tools/hermes/enable-direct-hermes-cron.ps1"
RESET_EPOCH = ROOT / "tools/hermes/reset-hermes-trading-epoch.ps1"
CONTROL_PLUGIN = ROOT / "hermes-profile/plugins/glitch-control/__init__.py"
SIM_SUBMIT = ROOT / "tools/hermes/submit-validated-sim-intent.ps1"
SIM_EXIT = ROOT / "tools/hermes/submit-validated-sim-exit.ps1"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def section(text: str, start: str, end: str) -> str:
    return text.split(start, 1)[1].split(end, 1)[0]


class CurrentSourceContractTests(unittest.TestCase):
    def test_direct_bridge_is_the_installed_decision_runtime(self):
        installer = read(DIRECT_INSTALLER)
        cron = read(DIRECT_CRON)
        self.assertIn("run-direct-glitch-cycle.py", installer)
        self.assertIn("run-direct-glitch-cycle.py", cron)
        self.assertNotIn("run-hermes-portfolio-cycle.ps1", installer)
        self.assertNotIn("run-hermes-portfolio-cycle.ps1", cron)

    def test_installer_and_plugin_expose_owned_trading_epoch_reset(self):
        installer = read(DIRECT_INSTALLER)
        plugin = read(CONTROL_PLUGIN)
        reset = read(RESET_EPOCH)
        self.assertIn("reset-hermes-trading-epoch.ps1", installer)
        self.assertIn("reset-named-hermes-session.py", installer)
        self.assertIn('"reset-trading"', plugin)
        self.assertIn('"reset-memory"', plugin)
        self.assertNotIn('"reset": (_reset_trading', plugin)
        self.assertIn('action="TRADING_OFF"', plugin)
        self.assertIn("_assert_sim_reset_safe()", plugin)
        self.assertIn('"SOUL.md", "skills", "plugins", "config"', reset.replace("'", '"'))
        self.assertIn("'Journal.tsv', 'TradeLedger.tsv'", reset)

    def test_direct_cycle_allows_dynamic_quantity_and_native_multi_leg_protection(self):
        cycle = read(DIRECT_CYCLE)
        self.assertNotIn('quantity must equal 1', cycle)
        self.assertIn('"take_profit_2"', cycle)
        self.assertIn('"take_profit_3"', cycle)
        self.assertIn('"quantity_tp1"', cycle)
        self.assertIn('"quantity_tp2"', cycle)

    def test_visible_ai_switch_is_the_only_operational_gate_and_invalid_policy_fails_closed(self):
        executor = read(EXECUTOR)
        policy = read(POLICY)
        switch = section(executor, "public static bool IsExecutionEnabled", "private static bool IsNoOpAction")
        self.assertIn("policy.IsValid", switch)
        self.assertIn("TradingPaused", switch)
        self.assertNotIn("policy.AiEnabled", switch)
        self.assertNotIn("policy.AiKillSwitch", switch)
        self.assertNotIn("policy.Mode", switch)
        self.assertIn("return InvalidPolicy()", policy)
        self.assertIn("AccountAllowlist.Clear()", policy)

    def test_acceptance_submitters_use_visible_ai_switch_without_policy_arm_ritual(self):
        for source in (read(SIM_SUBMIT), read(SIM_EXIT)):
            self.assertIn("executor_enabled", source)
            self.assertIn("[DateTimeOffset]::Parse", source)
            self.assertNotIn("$policy.mode = 'sim'", source)
            self.assertNotIn("$policyBefore.mode = 'sim'", source)
            self.assertNotIn("executor_enabled = $true", source)

    def test_short_positions_recover_a_negative_signed_quantity(self):
        portfolio = read(PORTFOLIO)
        parser = section(portfolio, "string marketPosition", "index = objectEnd + 1")
        self.assertIn('string.Equals(marketPosition, "Short"', parser)
        self.assertIn("signedQuantity = -signedQuantity", parser)
        self.assertIn('string.Equals(marketPosition, "Long"', parser)

    def test_follower_entry_lifecycle_is_registered_before_submit(self):
        copy_engine = read(COPY_ENGINE)
        submit = section(copy_engine, "private string TrySubmitProtectedFollowerEntry", "public void ProcessAccountStateUpdate")
        self.assertLess(submit.index("RegisterFollowerProtectionUnsafe"), submit.index("account.Submit"))
        self.assertIn("RemoveFollowerProtectionTicketUnsafe", submit)

    def test_follower_submit_never_blindly_retries_an_unknown_entry(self):
        copy_engine = read(COPY_ENGINE)
        submit = section(copy_engine, "private void SubmitCopyWithRetry", "private static bool IsFollowerOwnedSignal")
        self.assertEqual(submit.count("TrySubmitProtectedFollowerEntry("), 1)
        self.assertIn("terminalRejectionProved", submit)
        self.assertIn("CopySubmitStateUnknown", submit)

    def test_terminal_partial_follower_entry_is_protected_and_transitions_expire(self):
        copy_engine = read(COPY_ENGINE)
        update = section(copy_engine, "public void ProcessFollowerOrderUpdate", "public void AlignFollowerToMaster")
        reconcile = section(copy_engine, "private void ReconcileFollowerRoot", "private static bool HasFollowerOrderTransition")
        self.assertIn("terminalPositiveFill", update)
        self.assertIn("order.Filled > 0", update)
        self.assertIn("FollowerLifecycleTransitionTimeout", reconcile)
        self.assertIn("transition_timeout_", reconcile)

    def test_protection_coverage_uses_remaining_oco_pairs(self):
        copy_engine = read(COPY_ENGINE)
        executor = read(EXECUTOR)
        follower = section(copy_engine, "private static bool HasCompleteFollowerProtection", "private static int RemainingQuantity")
        master = section(executor, "private static bool HasCompleteAiProtection", "private static int RemainingQuantity")
        for value in (follower, master):
            self.assertIn("RemainingQuantity", value)
            self.assertIn("OrderType.StopMarket", value)
            self.assertIn("OrderType.Limit", value)
        self.assertNotIn("protection.Any(order => order.OrderState == OrderState.PartFilled)", follower)

    def test_follower_partial_protection_fill_resizes_oco_without_flattening(self):
        copy_engine = read(COPY_ENGINE)
        update = section(copy_engine, "private void HandleFollowerProtectionOrderUpdate", "private void RecordFollowerProtectionClose")
        resize = section(copy_engine, "private bool TryResizeFollowerOcoAfterPartialFill", "private void RecordFollowerProtectionClose")
        self.assertIn("TryResizeFollowerOcoAfterPartialFill", update)
        self.assertNotIn('RequestFollowerFlattenOnce(account, order.Instrument, "partial_protection_fill")', update)
        self.assertIn("sibling.QuantityChanged = sibling.Filled + remaining", resize)
        self.assertIn("account.Change(new[] { sibling })", resize)
        self.assertNotIn("account.Flatten", resize)

    def test_master_brackets_are_registered_before_submit(self):
        executor = read(EXECUTOR)
        submit = section(executor, "private static GlitchAiExecutionResult TrySubmitStructuralProtection", "private static bool HasWorkingStructuralProtection")
        self.assertLess(submit.index("GroupsBySignal[protectionOrder.Name.Trim()] = group"), submit.index("account.Submit"))
        self.assertIn("GroupsBySignal.Remove", submit)

    def test_follower_entry_identity_contains_account_and_execution_tokens(self):
        copy_engine = read(COPY_ENGINE)
        builder = section(copy_engine, "private static string BuildFollowerEntrySignal", "private static bool IsSameTrackedOrder")
        self.assertIn("SanitizeIdentityToken(account?.Name", builder)
        self.assertIn("SanitizeIdentityToken(executionToken", builder)
        self.assertNotIn("GetHashCode", builder)

    def test_pending_master_copy_has_ttl_enablement_and_delta_reconciliation(self):
        copy_engine = read(COPY_ENGINE)
        release = section(copy_engine, "private void TryReleasePendingMasterCopies", "private void AuditFollowerProtection")
        self.assertIn("if (!_enabled)", release)
        self.assertIn("PendingMasterCopyTtl", release)
        self.assertIn("AlignFollowerToMaster", release)

    def test_startup_restore_reactivates_runtime_without_forcing_alignment(self):
        main = read(MAIN_WINDOW)
        loaded = section(main, "private void OnWindowLoaded", "private void BootstrapAnalyticsBridgeOnStartup")
        self.assertIn("RefreshCopyEngineConfiguration(GetActiveAccountsSnapshot())", loaded)
        self.assertIn("mode=runtime_active", loaded)
        self.assertNotIn('AlignAllEnabledFollowersToMaster("startup")', loaded)

    def test_visible_replication_state_is_backed_by_copy_engine_runtime(self):
        main = read(MAIN_WINDOW)
        runtime = section(main, "private bool IsReplicationRuntimeActive", "internal bool IsReplicationEnabledFromExternalSurface")
        setter = section(main, "internal bool SetReplicationFromExternalSurface", "internal void ToggleReplicationFromExternalSurface")
        button = section(main, "private void UpdateReplicateButtonState", "private void UpdateRefreshTimerCadence")
        self.assertIn("_copyEngine.IsEnabled", runtime)
        self.assertIn("IsReplicationRuntimeActive() == enabled", setter)
        self.assertIn("IsReplicationRuntimeActive() ? \"Running\" : \"Stopped\"", button)

    def test_external_follower_activity_is_observed_without_persistent_route_state(self):
        copy_engine = read(COPY_ENGINE)
        manual = section(copy_engine, "public bool ProcessExternalFollowerExecution", "public void ProcessFollowerOrderUpdate")
        self.assertIn("external_follower_execution", manual)
        self.assertIn("action=observed", manual)
        self.assertNotIn("_quarantinedFollowerRoots", copy_engine)
        self.assertNotIn('StartsWith("GLT-"', manual)
        self.assertNotIn("account.Cancel", manual)

    def test_glitch_flatten_transition_is_registered_before_nt_can_emit_synchronous_events(self):
        copy_engine = read(COPY_ENGINE)
        flatten = section(copy_engine, "private string TryFlattenFollowerInstrument", "private static bool IsSubmissionStarted")
        self.assertLess(flatten.index("_pendingFollowerFlattens[key]"), flatten.index("account.Flatten"))
        self.assertLess(flatten.index("FollowerLifecycleState.Closing"), flatten.index("account.Flatten"))
        self.assertIn("_pendingFollowerFlattens.Remove(key)", flatten)
        self.assertIn("_flattenInFlight.Remove(key)", flatten)

    def test_follower_flatten_deduplication_has_one_owner(self):
        copy_engine = read(COPY_ENGINE)
        flatten = section(copy_engine, "private string TryFlattenFollowerInstrument", "private static bool IsSubmissionStarted")
        request = section(copy_engine, "private void RequestFollowerFlattenOnce", "private static bool HasCompleteFollowerProtection")
        self.assertIn('return "already_pending"', flatten)
        self.assertNotIn("_flattenInFlight.Add", request)
        self.assertIn("TryFlattenFollowerInstrument", request)

    def test_master_fill_conflict_preserves_existing_state_and_unwinds_glitch_master(self):
        copy_engine = read(COPY_ENGINE)
        master = section(copy_engine, "public void ProcessMasterExecution", "public bool ProcessExternalFollowerExecution")
        self.assertIn("flatten_glitch_master", master)
        self.assertIn("preserve_account_state", master)
        self.assertIn("existing account state was preserved", master)
        self.assertNotIn("flatten_offending_follower", master)

    def test_native_and_flatten_all_controls_have_no_hidden_follower_state_gate(self):
        replication = read(REPLICATION)
        resolve = section(replication, "private List<Account> ResolveFlattenAllAccounts", "private int IssueFlattenOrdersForAccounts")
        issue = section(replication, "private int IssueFlattenOrdersForAccounts", "private static Account TryFindConnectedAccountByName")
        self.assertNotIn("_quarantinedFollowerRoots", resolve)
        self.assertNotIn("_quarantinedFollowerRoots", issue)
        self.assertIn("GlitchReplicationEngine.TryFlattenAccount", issue)

    def test_cross_direction_alignment_is_state_derived_and_submits_nothing(self):
        copy_engine = read(COPY_ENGINE)
        align = section(copy_engine, "private void AlignFollowerToMaster", "private void SubmitCopyWithRetry")
        cross = section(align, "if (actual != 0 && expected != 0 && Math.Sign(actual) != Math.Sign(expected))", "if (delta == 0)")
        self.assertNotIn("TryFlattenFollowerInstrument", cross)
        self.assertIn("action=no_submission", cross)
        self.assertIn("cross_direction_blocked", cross)
        self.assertNotIn("TrySubmitProtectedFollowerEntry", cross)

    def test_apex_direction_guard_covers_positions_and_working_entries(self):
        guard = read(APEX_GUARD)
        self.assertIn("TrySnapshotPositions", guard)
        self.assertIn("TrySnapshotOrders", guard)
        self.assertIn("apex_position_state_unavailable", guard)
        self.assertIn("apex_order_state_unavailable", guard)
        self.assertIn("GlitchReplicationEngine.IsWorkingOrderState", guard)
        self.assertIn("OrderAction.SellShort", guard)
        self.assertIn("cross_direction", guard.lower())

    def test_catchup_refuses_non_atomic_same_direction_reduction(self):
        copy_engine = read(COPY_ENGINE)
        align = section(copy_engine, "private void AlignFollowerToMaster", "private void SubmitCopyWithRetry")
        self.assertIn("sameDirectionReduction", align)
        self.assertIn("partial_reduction_requires_bracket_resize", align)
        self.assertLess(align.index("sameDirectionReduction"), align.index("TryResolveCatchUpOrder"))

    def test_reconciler_defers_only_declared_or_native_transitions(self):
        copy_engine = read(COPY_ENGINE)
        reconcile = section(copy_engine, "private void ReconcileFollowerRoot", "private static bool HasFollowerOrderTransition")
        self.assertIn("FollowerLifecycleState.EntryPending", reconcile)
        self.assertIn("FollowerLifecycleState.ProtectionSubmitting", reconcile)
        self.assertIn("FollowerLifecycleState.OcoTransitioning", reconcile)
        self.assertIn("HasFollowerOrderTransition(orders, root)", reconcile)
        self.assertNotIn("CoverageTransitionGrace", copy_engine)

    def test_flat_follower_cancels_orphan_glitch_protection(self):
        copy_engine = read(COPY_ENGINE)
        reconcile = section(copy_engine, "private void ReconcileFollowerRoot", "private static bool HasFollowerOrderTransition")
        flat = section(reconcile, "if (net == 0)", "if (HasCompleteFollowerProtection")
        self.assertIn("account.Cancel(workingProtection.ToArray())", flat)
        self.assertIn("orphan_protection_cancel", flat)

    def test_replication_off_preserves_existing_native_protection(self):
        main = read(MAIN_WINDOW)
        replication = read(REPLICATION)
        self.assertNotIn("CancelGlitchWorkingOrdersOnFollowers", main)
        self.assertNotIn("CancelGlitchWorkingOrdersOnFollowers", replication)

    def test_public_alignment_api_does_not_expose_internal_plan_type(self):
        copy_engine = read(COPY_ENGINE)
        public_api = section(copy_engine, "public void AlignFollowerToMaster(", "private void AlignFollowerToMaster(")
        self.assertNotIn("GlitchReplicationProtectionPlan", public_api)
        self.assertIn("Account masterAccount", public_api)
        self.assertIn("Account followerAccount", public_api)

    def test_three_leg_bracket_contract_is_consistent_across_validator_and_executor(self):
        validator = read(VALIDATOR)
        executor = read(EXECUTOR)
        copy_engine = read(COPY_ENGINE)
        self.assertIn('"take_profit_3"', validator)
        self.assertIn("stop_loss_3_requires_take_profit_3", validator)
        self.assertIn('"quantity_tp2"', executor)
        self.assertIn("quantityTp3", executor)
        self.assertIn("plan.Legs.Count > maxProtectionLegs", copy_engine)

    def test_master_lifecycle_is_not_mislabeled_as_group_lifecycle(self):
        executor = read(EXECUTOR)
        lifecycle = section(
            executor,
            "private static void TryCompleteGroup",
            "private static bool IsAccountRecoveryTerminal",
        )
        self.assertIn('"master_entry_filled"', lifecycle)
        self.assertIn('"master_entry_open_protected"', lifecycle)
        self.assertIn('"master_trade_closed"', lifecycle)
        self.assertIn('"master_structural_brackets_submitted"', executor)
        self.assertNotIn('"group_entry_open_protected"', lifecycle)
        self.assertNotIn('"group_trade_closed"', lifecycle)

        copy_engine = read(COPY_ENGINE)
        self.assertIn('"follower_structural_brackets_submitted"', copy_engine)
        self.assertIn('"follower_trade_closed"', copy_engine)

    def test_exit_is_idempotent_when_flat_and_reports_master_submission(self):
        executor = read(EXECUTOR)
        exit_path = section(
            executor,
            "private static GlitchAiExecutionResult TryExecuteGroupExit",
            "private static GlitchAiExecutionResult TryExecuteGroupMoveStop",
        )
        self.assertIn('"exit_already_flat"', exit_path)
        self.assertIn('"master_exit_submitted"', exit_path)
        self.assertNotIn('"group_exit_submitted"', exit_path)
        self.assertLess(exit_path.index("positionedAccounts.Count == 0"), exit_path.index("account.Flatten"))

    def test_journal_reversal_allocates_commission_once(self):
        insights = read(INSIGHTS)
        self.assertIn("closeQty / executionQuantity", insights)
        self.assertIn("remainder / executionQuantity", insights)
        self.assertIn("Math.Abs(commission) * Math.Min(1d, fraction)", insights)


if __name__ == "__main__":
    unittest.main()
