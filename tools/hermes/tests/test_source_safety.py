"""AI-only ownership boundaries; shared core contracts live under tools/tests."""

import unittest
from pathlib import Path

from tools.tests.test_shared_source_contracts import SharedSourceArchitectureContractTests


ROOT = Path(__file__).resolve().parents[3]
ADDON = ROOT / "ninjatrader/Glitch/AddOns/GlitchAddOn"
EXECUTOR = ADDON / "Services/Ai/GlitchAiOrderExecutor.cs"
TELEMETRY_UI = ADDON / "UI/MainWindow/GlitchMainWindow.Telemetry.partial.cs"


def source(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def method_body(text: str, signature: str, next_signature: str) -> str:
    return text.split(signature, 1)[1].split(next_signature, 1)[0]


class AiSourceArchitectureContractTests(unittest.TestCase):
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
        self.assertIn("GetAiEntryDenialReason", telemetry)
        self.assertIn("firm_direction_conflict", telemetry)

    def test_ai_move_stop_changes_master_only(self):
        executor = source(EXECUTOR)
        move_stop = method_body(
            executor,
            "private static GlitchAiExecutionResult TryExecuteGroupMoveStop",
            "private static List<ExecutionGroupContext> RecoverOwnedGroupsFromLiveOrders",
        )
        self.assertIn("Account masterAccount = members[0].Account", move_stop)
        self.assertIn("accountChanges.Key.Change", move_stop)
        self.assertNotIn("Follower", move_stop)

    def test_working_partial_master_fill_aggregates_before_protection(self):
        executor = source(EXECUTOR)
        partial = executor.split("int entryAccountIndex;", 1)[1].split(
            "&& order.OrderState == OrderState.Filled)",
            1,
        )[0]
        self.assertIn("GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)", partial)
        self.assertIn("group_terminal_partial_entry_fill_recovery", partial)


if __name__ == "__main__":
    unittest.main()
