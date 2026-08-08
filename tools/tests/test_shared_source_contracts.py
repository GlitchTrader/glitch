"""Source architecture contracts for the first-principles NinjaTrader runtime."""

import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
ADDON = ROOT / "ninjatrader" / "Glitch" / "AddOns" / "GlitchAddOn"


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8-sig")


class SharedSourceArchitectureContractTests(unittest.TestCase):
    def test_risk_catalog_has_explicit_apex_intraday_tiers_and_sim_projection(self):
        rules = json.loads(read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Resources/PropFirmRules.json"
        ))
        apex_intraday = next(
            firm for firm in rules["firms"] if firm["firmId"] == "ApexIntraday"
        )
        eval_tiers = [
            tier for tier in apex_intraday["tiers"]
            if tier.get("statusFilter") == "Eval"
        ]
        self.assertEqual(
            [(tier["accountSize"], tier["maxDrawdown"], tier["maxContracts"])
             for tier in eval_tiers],
            [(25000, 1000, 4), (50000, 2000, 6),
             (100000, 3000, 8), (150000, 4000, 12)],
        )
        self.assertEqual(
            [(tier.get("minProfit", 0), tier.get("maxProfit", 0),
              tier["maxContracts"], tier["dailyLossLimit"])
             for tier in apex_intraday["tiers"]
             if tier.get("statusFilter") == "AP" and tier["accountSize"] == 25000],
            [(0, 1000, 1, 500), (1000, 2000, 2, 500),
             (2000, 0, 2, 1250)],
        )
        compliance = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Risk/GlitchComplianceEngine.cs"
        )
        window = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/UI/MainWindow/GlitchMainWindow.cs"
        )
        self.assertNotIn('if (status == "Sim")\n                return null;', compliance)
        self.assertIn('if (string.Equals(selectedStatus, "Sim", StringComparison.OrdinalIgnoreCase))', window)
        self.assertIn('ruleFirmId = "ApexTraderFunding";', window)
        self.assertIn('"Size required"', window)
        self.assertIn('" (simulated)"', window)

    def test_simulation_account_reset_clears_only_that_account_peak_state(self):
        window = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/UI/MainWindow/GlitchMainWindow.cs"
        )
        self.assertIn("Account.SimulationAccountReset += OnSimulationAccountReset;", window)
        self.assertIn("Account.SimulationAccountReset -= OnSimulationAccountReset;", window)
        self.assertIn("private void OnSimulationAccountReset(object sender, EventArgs e)", window)
        self.assertIn("private void ClearPeakStatesForSimulationReset(string accountName)", window)
        self.assertIn('string keyPrefix = normalizedAccountName + "|";', window)
        self.assertIn("_peakStatesByAccount.TryRemove(stateKey, out ignored)", window)

    def test_peak_state_identity_separates_firm_and_account_size(self):
        compliance = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Risk/GlitchComplianceEngine.cs"
        )
        window = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/UI/MainWindow/GlitchMainWindow.cs"
        )
        self.assertIn("string ruleFirmId,", compliance)
        self.assertIn("double accountSize)", compliance)
        self.assertIn('normalizedFirm + "|" + normalizedSize + "|" + normalizedTracking', compliance)
        self.assertIn("ruleFirmId,\n                selectedAccountSize", window)
        self.assertIn('selectedStatus, "Sim", StringComparison.OrdinalIgnoreCase) ? selectedAccountSize : 0', window)

    def test_one_native_gateway_owns_every_order_mutator(self):
        mutators = (".CreateOrder(", ".Submit(", ".Change(", ".Cancel(", ".Flatten(")
        offenders = []
        for source in ADDON.rglob("*.cs"):
            text = source.read_text(encoding="utf-8-sig")
            if any(token in text for token in mutators):
                relative = source.relative_to(ADDON).as_posix()
                if relative != "Infrastructure/NinjaTraderGateway.cs":
                    offenders.append(relative)
        self.assertEqual(offenders, [])

    def test_one_typed_native_subscription_boundary(self):
        gateway = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/NinjaTraderGateway.cs"
        )
        all_other = "\n".join(
            path.read_text(encoding="utf-8-sig")
            for path in ADDON.rglob("*.cs")
            if path.name != "NinjaTraderGateway.cs"
        )
        for event in ("ExecutionUpdate", "OrderUpdate", "PositionUpdate", "AccountStatusUpdate"):
            self.assertIn(f"{event} +=", gateway)
            self.assertNotIn(f"{event} +=", all_other)
        self.assertNotIn("GetEvent(", gateway)

    def test_addon_lifetime_owns_the_runtime_not_the_window(self):
        addon = read("ninjatrader/Glitch/AddOns/GlitchAddOn/GlitchAddOn.cs")
        ownership = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/GlitchRuntimeOwnershipLease.cs"
        )
        host = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/GlitchRuntimeHost.cs"
        )
        window = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/UI/MainWindow/GlitchMainWindow.cs"
        )
        self.assertIn("State.Active", addon)
        self.assertIn("new GlitchRuntimeHost()", addon)
        self.assertIn("State.Terminated", addon)
        self.assertLess(addon.index("_runtimeOwnership.Acquire();"), addon.index("StartRuntimeHost();"))
        self.assertLess(addon.index("StopPriorAssemblyRuntimes();"), addon.index("StartRuntimeHost();"))
        active = addon.index("else if (State == State.Active)")
        terminated = addon.index("else if (State == State.Terminated)", active)
        self.assertNotIn("RunOnUiThreadSync", addon[active:terminated])
        self.assertIn("AppDomain.CurrentDomain.SetData(OwnerSlot, _shutdown)", ownership)
        self.assertIn("priorOwner();", ownership)
        self.assertIn("public static GlitchRuntimeHost Active", host)
        self.assertNotIn("new GlitchRuntimeHost()", window)

    def test_legacy_mutation_graveyard_is_absent(self):
        for relative in (
            "Services/Trading/GlitchCopyEngine.cs",
            "Services/Trading/GlitchReplicationMath.cs",
            "Services/Trading/GlitchReplicationProtection.cs",
            "Services/Ai/GlitchAiRiskFirewall.cs",
            "Services/Ai/GlitchAiRiskDecision.cs",
        ):
            self.assertFalse((ADDON / relative).exists(), relative)

    def test_follower_manual_execution_is_observation_only(self):
        engine = read("ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchEngine.cs")
        self.assertIn("string.Equals(value.Master, execution.AccountName", engine)
        self.assertIn("execution.Origin != GlitchExecutionOrigin.GlitchSynchronization", engine)
        self.assertIn("execution.Origin != GlitchExecutionOrigin.GlitchFlatten", engine)
        self.assertNotIn("position mismatch", engine.lower())
        self.assertNotIn("disable route", engine.lower())

    def test_route_snapshot_and_requested_sync_are_one_durable_input(self):
        contracts = read("ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchContracts.cs")
        engine = read("ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchEngine.cs")
        host = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/GlitchRuntimeHost.cs"
        )
        ui = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/UI/MainWindow/GlitchMainWindow.Replication.partial.cs"
        )
        self.assertIn("class RouteConfigurationChanged", contracts)
        self.assertIn("ReplaceRoutes(routeConfiguration, commands)", engine)
        self.assertIn("configuration.SynchronizeRouteIds", engine)
        self.assertIn('"route_configuration_changed",', host)
        self.assertIn("new RouteSynchronizationRequested(routeId)", host)
        self.assertIn("SynchronizeRoute(BuildRuntimeRouteId(group, member))", ui)

    def test_manual_master_protection_is_native_and_fill_anchored(self):
        contracts = read("ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchContracts.cs")
        engine = read("ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchEngine.cs")
        gateway = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/NinjaTraderGateway.cs"
        )
        self.assertIn("class MasterProtectionObserved", contracts)
        self.assertIn("ConfigureManualBundle", engine)
        self.assertIn("bundle.EntryPrice + value.StopOffset.Value", engine)
        self.assertIn("PublishExternalProtectionSnapshot", gateway)
        self.assertIn("WasProtectionOcoCompletedByFill", gateway)
        self.assertIn("&& !IsGlitchOrder(order)", gateway)
        self.assertNotIn("SignedExecutionPosition(execution)", gateway)
        self.assertIn("OpeningQuantityFromPrior", engine)
        self.assertNotIn("book.SignedPosition = execution.PostPosition", engine)
        self.assertNotIn("OpeningQuantity", contracts)
        self.assertNotIn("PostPosition", contracts)

    def test_manual_protection_snapshot_ignores_glitch_and_position_callbacks(self):
        gateway = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/NinjaTraderGateway.cs"
        )
        position_callback = gateway[
            gateway.index("private void OnPositionUpdate("):
            gateway.index("private void PublishRecoverySnapshot(")
        ]
        order_callback = gateway[
            gateway.index("private void OnOrderUpdate("):
            gateway.index("private void PublishExecutionFact(")
        ]
        helper = gateway[
            gateway.index("private static bool ShouldPublishExternalProtectionSnapshot("):
            gateway.index("private void PublishExternalProtectionSnapshot(")
        ]
        self.assertNotIn("PublishExternalProtectionSnapshot", position_callback)
        self.assertIn("ShouldPublishExternalProtectionSnapshot(account, e.Order)", order_callback)
        self.assertIn("!IsGlitchOrder(order)", helper)
        self.assertIn("IsExitProtection(", helper)
        self.assertIn("CurrentPosition(account, order.Instrument.FullName)", helper)

    def test_execution_lifecycle_facts_cannot_authorize_replication(self):
        contracts = read("ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchContracts.cs")
        engine = read("ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchEngine.cs")
        gateway = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/NinjaTraderGateway.cs"
        )
        journal = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/GlitchOperationJournal.cs"
        )
        self.assertIn("class ExecutionLifecycleObserved", contracts)
        self.assertIn("nativeOperation != GlitchNativeOperation.Add || !representable", gateway)
        self.assertIn("var executionLifecycle = input as ExecutionLifecycleObserved", engine)
        self.assertIn("ObserveExecutionLifecycle(executionLifecycle)", engine)
        self.assertIn("SerializeInput", journal)
        self.assertIn("TryAppendInput", read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/GlitchRuntimeHost.cs"
        ))
        self.assertIn("Operation.Update", gateway)
        self.assertIn("Operation.Remove", gateway)

    def test_exit_waits_for_native_protection_cancellation_finality(self):
        contracts = read("ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchContracts.cs")
        engine = read("ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchEngine.cs")
        gateway = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/NinjaTraderGateway.cs"
        )
        self.assertIn("ProtectionCancellationCompletedObserved", contracts)
        self.assertIn("GlitchOperationPhase.WaitingForProtectionCancellation", engine)
        self.assertIn("ProtectionCancellationComplete(request)", engine)
        self.assertIn("executed < Math.Max(1, fact.Filled)", engine)
        self.assertIn("ObserveProtectionCancellation", gateway)

    def test_hermes_translator_has_no_cognitive_firewall(self):
        executor = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Ai/GlitchAiOrderExecutor.cs"
        )
        server = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Ai/GlitchAiIntentServer.cs"
        )
        validator = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Ai/GlitchAiIntentValidator.cs"
        )
        self.assertIn("GlitchRuntimeHost host = GlitchRuntimeHost.Active", executor)
        self.assertIn("host.SubmitHermes(request)", executor)
        self.assertIn("TryResolveProfileAccount(profile, out string boundAccount)", executor)
        self.assertIn("account_not_configured_as_master", executor)
        self.assertIn("ExecutionBindingsValid", executor)
        self.assertNotIn("master_not_allowlisted", executor)
        for veto in ("RiskFirewall", "TradingWindow", "Apex", "license_required"):
            self.assertNotIn(veto, executor)
        self.assertNotIn("GlitchAiRiskFirewall", server)
        self.assertIn("schema_version_must_be_glitch.intent.v3", validator)
        selfcheck = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Persistence/GlitchRailSelfCheckWriter.cs"
        )
        self.assertIn('"cognitive_firewall\\\":")', selfcheck.replace("Append(", ""))
        self.assertIn("GlitchSnapshotJson.Bool(false)", selfcheck)
        self.assertNotIn("r09_risk_firewall", selfcheck)

    def test_native_hermes_entry_evidence_reaches_outcome_reconciliation(self):
        gateway = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/NinjaTraderGateway.cs"
        )
        writer = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/GlitchExecutionEvidenceWriter.cs"
        )
        ai_tab = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/UI/MainWindow/GlitchMainWindow.AiTab.partial.cs"
        )
        self.assertIn('"master_entry_submitted"', gateway)
        self.assertIn('"group_structural_brackets_submitted"', gateway)
        self.assertIn('"follower_structural_brackets_submitted"', gateway)
        self.assertIn("HermesIntentIdForExecution", read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchEngine.cs"
        ))
        self.assertIn("instrument.MasterInstrument.PointValue", gateway)
        self.assertIn("instrument.MasterInstrument.TickSize", gateway)
        self.assertIn("public static void TryAppend(", writer)
        self.assertIn("Decision in progress ({0}s)", ai_tab)

    def test_entry_protection_geometry_is_rejected_before_native_submission(self):
        executor = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Ai/GlitchAiOrderExecutor.cs"
        )
        gateway = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/NinjaTraderGateway.cs"
        )
        self.assertIn('failure = "entry_protection_geometry_invalid"', executor)
        self.assertIn("IsEntryProtectionGeometryValid", executor)
        self.assertIn("price < referencePrice", executor)
        self.assertIn("price > referencePrice", executor)
        self.assertIn("ValidateProtectionGeometry(command);", gateway)
        self.assertIn("ValidateStopMarketSide", gateway)
        self.assertIn("protection_market_side_invalid", gateway)
        self.assertIn("instrument.MarketData?.Bid?.Price", gateway)
        self.assertIn("instrument.MarketData?.Ask?.Price", gateway)
        self.assertIn("-OrderSign(stopChange.Key.OrderAction)", gateway)
        self.assertNotIn("protection_change_position_flat", gateway)
        self.assertIn("protection_geometry_invalid", gateway)
        self.assertIn("beforeMutation?.Invoke(command);", gateway)
        self.assertLess(
            gateway.index("ValidateProtectionGeometry(command);"),
            gateway.index("beforeMutation?.Invoke(command);", gateway.index("ValidateProtectionGeometry(command);")),
        )

    def test_native_protection_rejection_and_commission_are_durable_facts(self):
        contracts = read("ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchContracts.cs")
        host = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/GlitchRuntimeHost.cs"
        )
        journal = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/GlitchOperationJournal.cs"
        )
        insights = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Insights/GlitchTradeInsightsService.cs"
        )
        self.assertIn("public decimal Commission", contracts)
        self.assertIn('"commission"', journal)
        self.assertIn("native_protection_change_rejected", host)
        self.assertIn("change.HermesIntentId", host)
        self.assertIn('fields.TryGetValue("commission"', insights)
        selfcheck = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Persistence/GlitchRailSelfCheckWriter.cs"
        )
        self.assertIn("policy?.ValidationError ?? string.Empty", selfcheck)
        self.assertNotIn('policy?.ValidationError ?? "policy_unavailable"', selfcheck)

    def test_snapshot_price_uses_instrument_level_field_not_nested_descriptive_price(self):
        registry = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Ai/GlitchAiSnapshotRegistry.cs"
        )
        self.assertIn("GlitchAiJsonFields.TryParseObject(json, out System.Collections.IDictionary snapshot)", registry)
        self.assertIn('(snapshot["instruments"] is System.Collections.IList instruments)', registry)
        self.assertIn('string root = instrument["instrument"] as string;', registry)
        self.assertIn('object rawPrice = instrument["current_price"];', registry)
        price_method = registry[registry.index("public static bool TryGetInstrumentPriceByHash("):]
        price_method = price_method[:price_method.index("public static bool TryGetInstrumentSession(")]
        self.assertIn("TryGetInstrumentCurrentPrice(json, instrumentRoot, out price)", price_method)

    def test_account_size_edit_commits_manual_configuration_before_refresh(self):
        window = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/UI/MainWindow/GlitchMainWindow.cs"
        )
        dashboard = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/UI/MainWindow/GlitchMainWindow.DashboardTab.partial.cs"
        )
        self.assertIn("nameof(AccountGridRow.AccountSizeSelection)", dashboard)
        self.assertIn("UpdateSourceTrigger.PropertyChanged", window)
        edit = window.index("private void OnAccountsGridCellEditEnding")
        save = window.index("SaveSelectionOverridesToDisk();", edit)
        refresh = window.index("RefreshAccountData(preferSynchronous: true);", edit)
        self.assertLess(save, refresh)
        self.assertIn('AccountSizeSource = "Manual"', window)
        self.assertIn("hasManualOverride", window)
        self.assertIn('accountSizeOptionDisplays.Insert(0, "-")', window)
        self.assertNotIn("InferAccountSizeFromName", window)
        self.assertNotIn("GetAccountSizeFromNt", window)

    def test_canonical_manual_configuration_beats_stale_workspace_cache(self):
        window = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/UI/MainWindow/GlitchMainWindow.cs"
        )
        restore = window[window.index("public void Restore("):window.index("public void Save(")]
        self.assertIn("LoadSelectionOverridesFromDisk(overwriteExisting: true);", restore)
        save = window[window.index("public void Save("):window.index("public WorkspaceOptions")]
        self.assertNotIn("CaptureSelectionOverridesFromRows();", save)
        closed = window[window.index("private void OnWindowClosed("):window.index("private void OnRefreshTimerTick(")]
        self.assertNotIn("CaptureSelectionOverridesFromRows();", closed)

    def test_manual_account_size_cannot_be_erased_by_another_grid_edit(self):
        window = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/UI/MainWindow/GlitchMainWindow.cs"
        )
        store = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Persistence/GlitchStateStore.cs"
        )
        upsert = window[
            window.index("private void UpsertSelectionOverrideFromRow"):
            window.index("private string GetOverridesFilePath")
        ]
        self.assertIn("existingOverride.AccountSize.HasValue", upsert)
        self.assertIn("selectedSize = existingOverride.AccountSize.Value", upsert)
        self.assertIn("if (isManual && !accountSize.HasValue)", store)
        self.assertIn("if (!kvp.Value.AccountSize.HasValue || kvp.Value.AccountSize.Value <= 0)", store)

    def test_compliance_is_off_by_default_and_each_runtime_action_is_explicit(self):
        store = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Persistence/GlitchRuntimePolicyStore.cs"
        )
        settings = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/UI/MainWindow/GlitchMainWindow.SettingsTab.partial.cs"
        )
        engine = read("ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchEngine.cs")
        self.assertIn("ReadBool(rows, \"ENFORCE_BUFFER_FREEZE_15_SIM\", false)", store)
        self.assertIn("ReadBool(rows, \"ENFORCE_BUFFER_ONE_CONTRACT_SIM\", false)", store)
        self.assertIn("Limit each future follower replication order to one contract", settings)
        self.assertIn("_settingsNoProtectionTimeoutMsTextBox", settings)
        self.assertIn("ReplicationQuantityLimitChanged", engine)

    def test_user_flatten_is_one_native_account_flatten_request(self):
        engine = read("ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchEngine.cs")
        observations = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Trading/GlitchReplicationEngine.cs"
        )
        gateway = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/NinjaTraderGateway.cs"
        )
        self.assertIn("new FlattenAccountCommand", engine)
        self.assertIn("account.Flatten(instruments.Values.ToArray())", gateway)
        main_window = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/UI/MainWindow/GlitchMainWindow.cs"
        )
        host = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/GlitchRuntimeHost.cs"
        )
        gate = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/GlitchMutationGate.cs"
        )
        self.assertIn('SetReplicationFromExternalSurface(false, "flatten_all")', main_window)
        self.assertNotIn("restoreCopyEngine", main_window)
        self.assertIn("_mutationGate.Fence(accounts);", host)
        self.assertIn("RequestFlattenBatch", host)
        self.assertIn("account_fenced_by_flatten", host)
        self.assertIn("allowDuringRuntimeFault: true", host)
        self.assertIn("!(command is FlattenAccountCommand)", host)
        self.assertIn("lock (_gate)", gate)
        self.assertIn("allowedWhileFenced", gate)
        self.assertIn("state != OrderState.Cancelled", observations)
        self.assertIn("state != OrderState.Filled", observations)
        self.assertIn("state != OrderState.Rejected", observations)

    def test_replication_delta_is_immutable_and_never_position_reconciled(self):
        engine = read("ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchEngine.cs")
        self.assertNotIn("TargetFlat", engine)
        self.assertNotIn("targetFlat", engine)
        split = engine[
            engine.index("private void EnqueueSplitTrade"):
            engine.index("private void EnqueueTrade", engine.index("private void EnqueueSplitTrade"))
        ]
        self.assertNotIn("CloseToFlat", split)
        self.assertIn("CloseToFlat = closeToFlat", engine)
        self.assertIn("operation.RequestedSignedQuantity", engine)
        self.assertIn("native_execution_exceeded_immutable_trade_delta", engine)
        self.assertIn("operation.RemainingSignedQuantity -= execution.SignedQuantity", engine)
        self.assertIn("operation.Purpose == GlitchCommandPurpose.Replication", engine)
        self.assertIn("operation.Purpose == GlitchCommandPurpose.GroupSynchronization", engine)
        self.assertIn("book.SignedPosition == checked(", engine)

    def test_replication_reversal_is_one_native_market_delta(self):
        gateway = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/NinjaTraderGateway.cs"
        )
        start = gateway.index("private void SubmitMarket(")
        end = gateway.index("private void SubmitProtection(", start)
        submit_market = gateway[start:end]
        self.assertNotIn("cannot cross through flat", submit_market)
        self.assertEqual(submit_market.count("account.CreateOrder("), 1)
        self.assertEqual(submit_market.count("account.Submit("), 1)
        self.assertIn("Math.Abs(command.SignedQuantity)", submit_market)
        self.assertIn("current > 0 ? OrderAction.Sell : OrderAction.SellShort", submit_market)

    def test_command_identity_is_stable_and_native_boundary_is_durable(self):
        engine = read("ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchEngine.cs")
        host = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/GlitchRuntimeHost.cs"
        )
        journal = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/GlitchOperationJournal.cs"
        )
        gateway = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/NinjaTraderGateway.cs"
        )
        self.assertIn("SHA256.Create()", engine)
        self.assertNotIn("_commandEpoch", engine)
        self.assertIn('TryAppend(command, "accepted"', host)
        self.assertIn('"native_request_started"', host)
        self.assertIn("beforeMutation?.Invoke(command)", gateway)
        self.assertIn("operations.v5.jsonl", journal)
        self.assertIn("TryPersistReplicationEnabled(priorReplicationEnabled)", host)
        self.assertIn("if (_runtimeFailed && replicationEnabled)", host)
        self.assertIn("_replicationEnabled = false;", host)
        self.assertIn("_commandFingerprints", host)
        self.assertIn("Canonical(SerializeCommand(command))", journal)
        identity = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchNativeIdentity.cs"
        )
        self.assertIn('internal const string Prefix = "GL1-"', identity)
        self.assertIn("internal static bool TryParse", identity)
        self.assertIn("GlitchNativeIdentity.Build", gateway)

    def test_unwritten_native_fact_cannot_advance_the_reducer(self):
        host = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/GlitchRuntimeHost.cs"
        )
        failed = host.index('"input_unjournaled|type="')
        blocked = host.index('BlockMutations("native_input_unwritten")', failed)
        returned = host.index("return;", blocked)
        reduced = host.index("_engine.Handle(runtimeEvent.Input)", failed)
        self.assertLess(blocked, returned)
        self.assertLess(returned, reduced)

    def test_startup_snapshots_are_accepted_and_shutdown_drains_before_unsubscribe(self):
        host = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/GlitchRuntimeHost.cs"
        )
        start = host.index("public void Start()")
        started = host.index("_started = true;", start)
        gateway_start = host.index("_gateway.Start(PostNative);", start)
        self.assertLess(started, gateway_start)
        dispose = host.index("public void Dispose()")
        runtime_dispose = host.index("_runtime.Dispose();", dispose)
        gateway_dispose = host.index("_gateway.Dispose();", dispose)
        self.assertLess(gateway_dispose, runtime_dispose)
        self.assertIn("Close native ingress first", host[gateway_dispose - 300:runtime_dispose])

    def test_portfolio_exposes_exact_protection_leg_ids(self):
        writer = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Persistence/GlitchPortfolioSnapshotWriter.cs"
        )
        capture = read(
            "ninjatrader/Glitch/AddOns/GlitchAddOn/UI/MainWindow/GlitchMainWindow.PortfolioSnapshot.partial.cs"
        )
        self.assertIn('\\"leg_id\\"', writer)
        self.assertIn("TryGetProtectionLegId(order.Name", capture)


if __name__ == "__main__":
    unittest.main()
