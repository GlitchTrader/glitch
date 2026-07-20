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
APEX_DIRECTION_GUARD = ADDON / "Services/Risk/GlitchApexDirectionGuard.cs"
INTENT_VALIDATOR = ADDON / "Services/Ai/GlitchAiIntentValidator.cs"
JSON_FIELDS = ADDON / "Services/Ai/GlitchAiJsonFields.cs"
TRADING_WINDOW = ADDON / "Services/Ai/GlitchAiTradingWindow.cs"
TEMPORAL_CLOSE = ADDON / "UI/MainWindow/GlitchMainWindow.AiTemporalCompliance.partial.cs"
PORTFOLIO_WRITER = ADDON / "Services/Persistence/GlitchPortfolioSnapshotWriter.cs"


def source(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def method_body(text: str, signature: str, next_signature: str) -> str:
    return text.split(signature, 1)[1].split(next_signature, 1)[0]


class AiSourceArchitectureContractTests(unittest.TestCase):
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

    def test_ai_checks_replication_admission_before_master_submit(self):
        executor = source(EXECUTOR)
        entry = method_body(
            executor,
            "private static GlitchAiExecutionResult TryExecuteGroupEnter",
            "private static bool TryGetEntryAccountIndex",
        )
        self.assertIn("GetReplicationEntryDenialReason", entry)
        self.assertLess(entry.index("GetReplicationEntryDenialReason"), entry.index("masterMember.Account.Submit"))
        self.assertIn("GlitchAiOrderExecutor.GetReplicationEntryDenialReason", source(TELEMETRY_UI))

    def test_ai_refuses_firm_direction_conflicts(self):
        telemetry = source(TELEMETRY_UI)
        guard = source(APEX_DIRECTION_GUARD)
        executor = source(EXECUTOR)
        self.assertIn("GetAiEntryDenialReason", telemetry)
        self.assertIn("GlitchApexDirectionGuard.TryApproveEntry", executor)
        self.assertIn("apex_cross_direction_conflict", guard)

    def test_ai_refuses_to_trade_a_group_when_replication_routes_are_incomplete(self):
        telemetry = source(TELEMETRY_UI)
        self.assertIn("expectedFollowerCount", telemetry)
        self.assertIn("GetActiveRouteCount", telemetry)
        self.assertIn("replication_routes_incomplete", telemetry)

    def test_invalid_policy_cannot_arm_or_pass_the_firewall(self):
        executor = source(EXECUTOR)
        firewall = source(FIREWALL)
        policy = source(POLICY)
        self.assertIn("public bool IsValid", policy)
        self.assertIn("policy_load_failed_", policy)
        self.assertIn("policy_schema_invalid", policy)
        self.assertIn("policy_key_duplicated_", policy)
        self.assertNotIn("public bool AiEnabled", policy)
        self.assertNotIn("public bool AiKillSwitch", policy)
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
        self.assertIn("accountChanges.Key.Change", move_stop)
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

        self.assertIn("take_profit_1", move_target)
        self.assertIn("LimitPriceChanged = targetPrice", move_target)
        self.assertIn("StopPriceChanged = stopPrice", move_target)
        self.assertEqual(move_target.count("masterAccount.Change"), 1)
        self.assertIn('isStop ? "-S-" : "-T-"', mirror)
        self.assertIn("followerOrder.LimitPriceChanged = masterPrice", mirror)
        self.assertIn("followerOrder.StopPriceChanged = masterPrice", mirror)
        self.assertIn('"MOVE_TP"', validator)
        self.assertIn("move_tp_requires_take_profit_1", validator)
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
        self.assertIn("master_protection_reconcile_timeout", account_reconcile)
        self.assertIn("RecoverGroup", account_reconcile)
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
        self.assertIn("GlitchAiOrderExecutor.GetReplicationEntryDenialReason", telemetry)

    def test_ai_preserves_absolute_structural_prices(self):
        executor = source(EXECUTOR)
        self.assertIn("public double StopPrice", executor)
        self.assertIn("public double TargetPrice", executor)
        self.assertIn("structural_prices=preserved", executor)
        self.assertIn("IsExecutableBracketPrice", executor)
        self.assertNotIn("public double StopDistance", executor)
        self.assertNotIn("public double TargetDistance", executor)
        self.assertNotIn("re-anchor", executor)

    def test_ai_uses_authoritative_account_and_replication_capacity(self):
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
        self.assertIn('TryExtractNumber(portfolioAccountJson, "max_contracts"', executor)
        self.assertIn('TryExtractNumber(portfolioAccountJson, "max_contracts"', firewall)
        self.assertIn("GetReplicationEntryDenialReason", executor)
        self.assertIn("TryGetTotalOpenContracts(masterAccount", executor)
        self.assertIn("lock (account.Positions)", executor)
        self.assertIn("TryGetTotalOpenContractsFromAccountBlock", firewall)
        self.assertIn('ExtractString(positionJson, "market_position")', portfolio_reader)

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
        self.assertIn("tp3_not_beyond_tp2", firewall)

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

    def test_entry_session_window_is_authoritative_at_firewall_and_submit_boundary(self):
        firewall = source(FIREWALL)
        executor = source(EXECUTOR)
        window = source(TRADING_WINDOW)
        writer = source(PORTFOLIO_WRITER)
        self.assertIn("trading_window_closed", firewall)
        self.assertIn("trading_window_closed_at_execution", executor)
        self.assertLess(
            executor.index("trading_window_closed_at_execution"),
            executor.index("masterMember.Account.Submit"),
        )
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
        self.assertIn('member.get("native_state_available") is not True', direct_cycle)
        self.assertIn("DayOfWeek.Saturday", window)
        self.assertIn("DayOfWeek.Sunday", window)
        self.assertIn("DayOfWeek.Friday", window)
        self.assertIn("Eastern Standard Time", window)

    def test_daily_close_is_scoped_risk_reduction_and_ai_off_does_not_act(self):
        temporal = source(TEMPORAL_CLOSE)
        self.assertIn("GlitchHermesControlStateStore.Load().TradingPaused", temporal)
        self.assertIn("var requiredNames", temporal)
        self.assertIn("group.MasterAccount", temporal)
        self.assertIn("member.FollowerAccount", temporal)
        self.assertIn("requiredNames.Contains(candidate.Name.Trim())", temporal)
        self.assertIn("GlitchReplicationEngine.TryFlattenAccount", temporal)
        self.assertIn("WaitForAllAccountsFlatAsync", temporal)
        self.assertIn("_isFlattenAllInProgress = true", temporal)
        self.assertIn("AiDailyCloseAccountUnavailable", temporal)
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
