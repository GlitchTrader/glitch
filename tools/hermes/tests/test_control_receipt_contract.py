"""GL-CTRL-02: durable control command ownership and reconciliation contracts."""
from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
CONTROL = ROOT / "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Ai/GlitchHermesControlServer.cs"


def source() -> str:
    return CONTROL.read_text(encoding="utf-8")


def body(text: str, start: str, end: str) -> str:
    return text.split(start, 1)[1].split(end, 1)[0]


class ControlReceiptContractTests(unittest.TestCase):
    def test_receipt_is_bound_to_semantic_command_content(self) -> None:
        text = source()
        self.assertIn("public string BodyHash { get; set; }", text)
        self.assertIn('"glitch.control.receipt.v3"', text)
        self.assertIn('"body_sha256"', text)
        self.assertIn("ComputeBodyHash(commandId, normalized)", text)
        begin = body(text, "public static bool TryBegin(", "public static GlitchHermesControlReceipt Complete")
        self.assertIn("out bool contentConflict", begin)
        self.assertIn("receipt.BodyHash", begin)
        self.assertIn("bodyHash", begin)
        self.assertIn("StringComparison.OrdinalIgnoreCase", begin)
        self.assertIn('Error("command_content_conflict")', text)

    def test_one_in_process_owner_and_restart_reacquisition_are_explicit(self) -> None:
        text = source()
        self.assertIn("private static readonly HashSet<string> ActiveCommandIds", text)
        begin = body(text, "public static bool TryBegin(", "public static GlitchHermesControlReceipt Complete")
        self.assertIn('string.Equals(receipt.Status, "applying", StringComparison.Ordinal)', begin)
        self.assertIn("ActiveCommandIds.Add(commandId)", begin)
        complete = body(text, "public static GlitchHermesControlReceipt Complete(", "public static void ReleaseExecution")
        self.assertIn("ActiveCommandIds.Remove", complete)
        self.assertIn("ReleaseExecution(commandId)", text)
        self.assertIn('? 200\n                        : string.Equals(receipt.Status, "applying", StringComparison.Ordinal) ? 202 : 409', text)

    def test_replayed_state_changes_are_idempotent_and_flatten_is_native_complete(self) -> None:
        text = source()
        execute = body(text, "private static bool Execute(", "private static string StatusJson")
        self.assertIn("state.TradingPaused != desiredPaused", execute)
        self.assertIn("getter != null && getter() == desired", execute)
        self.assertIn("completion.GetAwaiter().GetResult()", execute)
        self.assertIn('failure = "flatten_incomplete"', execute)
        self.assertNotIn("Thread.Sleep", execute)
        self.assertNotIn("DateTime.UtcNow -", execute)

    def test_control_json_escapes_all_json_control_characters(self) -> None:
        text = source()
        json_string = body(text, "internal static string JsonString(", "internal sealed class GlitchHermesControlReceipt")
        for escape in ('case \'"\'', "case '\\\\'", "case '\\b'", "case '\\f'", "case '\\n'", "case '\\r'", "case '\\t'"):
            self.assertIn(escape, json_string)
        self.assertIn("character < 0x20", json_string)
        self.assertIn('builder.Append("\\\\u")', json_string)
        self.assertNotIn(".Replace(\"\\\\\",", text)

    def test_control_path_does_not_add_strategy_or_trade_creation(self) -> None:
        text = source()
        for forbidden in (
            "ENTER_LONG",
            "ENTER_SHORT",
            "SubmitOrder",
            "Account.Submit",
            "risk_percent",
            "daily_target",
            "profit_target",
        ):
            self.assertNotIn(forbidden, text)
        self.assertIn("SetReplication", text)
        self.assertIn("FlattenAllAsync", text)


if __name__ == "__main__":
    unittest.main()
