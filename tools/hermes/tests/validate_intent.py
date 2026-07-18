import json
import sys
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker


def fail(message: str) -> None:
    print(message, file=sys.stderr)
    raise SystemExit(1)


scenario_path, output_path, schema_path = map(Path, sys.argv[1:4])
scenario = json.loads(scenario_path.read_text(encoding="utf-8"))
raw = output_path.read_text(encoding="utf-8").strip()

try:
    intent = json.loads(raw)
except json.JSONDecodeError as exc:
    fail(f"not_json: {exc}")

if not isinstance(intent, dict):
    fail("output_not_single_object")

schema = json.loads(schema_path.read_text(encoding="utf-8"))
errors = sorted(
    Draft202012Validator(schema, format_checker=FormatChecker()).iter_errors(intent),
    key=lambda error: list(error.path),
)
if errors:
    fail("schema_invalid: " + "; ".join(error.message for error in errors))

expected = scenario["expected_actions"]
if intent["action"] not in expected:
    fail(f"unexpected_action: {intent['action']} expected={expected}")
operator = scenario.get("operator", {"profile": "glitch", "master_account": "Sim101"})
if (
    intent["instrument"] != "MNQ"
    or intent["account"] != operator["master_account"]
    or intent["operator_profile"] != operator["profile"]
):
    fail("scope_violation")
if intent["snapshot_hash"] != scenario["market"]["snapshot_hash"]:
    fail("snapshot_hash_mismatch")
if intent["decision_audit"]["final_choice"] != intent["action"]:
    fail("decision_audit_choice_mismatch")

if intent["action"] in {"ENTER_LONG", "ENTER_SHORT"}:
    if intent.get("order_type") != "MARKET" or "limit_price" in intent:
        fail("entry_not_market_only")
    quantity = intent.get("quantity")
    if not isinstance(quantity, int) or isinstance(quantity, bool) or quantity < 1:
        fail("entry_quantity_invalid")
    valid_quantities = scenario.get("valid_entry_quantities")
    if isinstance(valid_quantities, list) and quantity not in valid_quantities:
        fail("entry_quantity_not_authorized")
    price = scenario["market"]["current_price"]
    stop = intent["stop_loss"]
    target = intent["take_profit_1"]
    if any(round(value * 4) != value * 4 for value in (stop, target)):
        fail("price_not_mnq_tick_aligned")
    if intent["action"] == "ENTER_LONG" and not (stop < price < target):
        fail("invalid_long_geometry")
    if intent["action"] == "ENTER_SHORT" and not (target < price < stop):
        fail("invalid_short_geometry")

print(json.dumps({"scenario": scenario["name"], "action": intent["action"], "valid": True}))
