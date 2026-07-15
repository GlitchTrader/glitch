import json
import sys
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker


def fail(message: str) -> None:
    print(message, file=sys.stderr)
    raise SystemExit(1)


scenario_path, output_path, intent_schema_path, batch_schema_path = map(Path, sys.argv[1:5])
scenario = json.loads(scenario_path.read_text(encoding="utf-8"))
try:
    batch = json.loads(output_path.read_text(encoding="utf-8").strip())
except json.JSONDecodeError as exc:
    fail(f"not_json: {exc}")

batch_schema = json.loads(batch_schema_path.read_text(encoding="utf-8"))
batch_errors = sorted(
    Draft202012Validator(batch_schema).iter_errors(batch), key=lambda error: list(error.path)
)
if batch_errors:
    fail("batch_schema_invalid: " + "; ".join(error.message for error in batch_errors))
if batch["cycle_id"] != scenario["cycle_id"]:
    fail("cycle_id_mismatch")

intent_schema = json.loads(intent_schema_path.read_text(encoding="utf-8"))
intent_validator = Draft202012Validator(intent_schema, format_checker=FormatChecker())
books = scenario["books"]
decisions = batch["decisions"]
if len(decisions) != len(books):
    fail("decision_count_mismatch")

seen_routes = set()
price = float(scenario["market"]["current_price"])
snapshot_hash = scenario["market"]["snapshot_hash"]
market_features = scenario.get("market", {})


def word_count(value: str) -> int:
    return len(str(value).split())


for index, (book, intent) in enumerate(zip(books, decisions)):
    errors = sorted(intent_validator.iter_errors(intent), key=lambda error: list(error.path))
    if errors:
        fail(f"intent_schema_invalid[{index}]: " + "; ".join(error.message for error in errors))
    route = intent["operator_profile"]
    if route in seen_routes:
        fail("duplicate_route")
    seen_routes.add(route)
    if route != book["route_id"] or intent["account"] != book["master_account"]:
        fail(f"book_scope_violation[{index}]")
    if intent["instrument"] != "MNQ" or intent["snapshot_hash"] != snapshot_hash:
        fail(f"market_scope_violation[{index}]")
    if intent["decision_audit"]["final_choice"] != intent["action"]:
        fail(f"decision_audit_choice_mismatch[{index}]")
    if word_count(intent["reason"]) > 20:
        fail(f"reason_too_verbose[{index}]")
    for key, value in intent["decision_audit"].items():
        if key != "final_choice" and word_count(value) > 14:
            fail(f"decision_audit_too_verbose[{index}]:{key}")
    allowed_actions = set(book.get("allowed_actions", []))
    if not allowed_actions:
        allowed_actions = {"ENTER_LONG", "ENTER_SHORT", "NOTHING"} if book["eligible_for_new_entry"] else {"NOTHING"}
    if intent["action"] not in allowed_actions:
        fail(f"book_action_not_allowed[{index}]:{intent['action']}")
    if book["eligible_for_new_entry"] and intent["reason"].startswith("cycle_invalid:"):
        fail(f"eligible_book_false_cycle_invalid[{index}]")
    if intent["action"] in {"ENTER_LONG", "ENTER_SHORT"}:
        if intent.get("order_type") != "MARKET" or "limit_price" in intent:
            fail(f"entry_not_market_only[{index}]")
        if intent.get("quantity") != 1:
            fail(f"entry_quantity_not_one[{index}]")
        stop = float(intent["stop_loss"])
        target = float(intent["take_profit_1"])
        if any(round(value * 4) != value * 4 for value in (stop, target)):
            fail(f"price_not_mnq_tick_aligned[{index}]")
        if intent["action"] == "ENTER_LONG" and not (stop < price < target):
            fail(f"invalid_long_geometry[{index}]")
        if intent["action"] == "ENTER_SHORT" and not (target < price < stop):
            fail(f"invalid_short_geometry[{index}]")
        estimated_risk = abs(price - stop) * 2
        stop_distance_points = abs(price - stop)
        noise_floor_points = float(book.get("noise_floor_stop_points", 0) or 0)
        if stop_distance_points + 0.000000001 < noise_floor_points:
            fail(f"stop_inside_mnq_noise_floor[{index}]")
        if intent["action"] == "ENTER_SHORT":
            sess_range_zone = str(market_features.get("sess_range_zone", ""))
            lower_breakdown_acceptance = float(market_features.get("lower_breakdown_acceptance_1m", 0) or 0)
            points_from_session_low = float(market_features.get("points_from_session_low", 999999) or 999999)
            if (
                sess_range_zone == "lower_extreme"
                and lower_breakdown_acceptance < 1.0
                and points_from_session_low < 30.0
            ):
                fail(f"short_lower_extreme_without_acceptance_or_headroom[{index}]")
        if intent["action"] == "ENTER_LONG":
            sess_range_zone = str(market_features.get("sess_range_zone", ""))
            support_reclaim = float(market_features.get("support_reclaim_after_flush", 0) or 0)
            near_support_reclaim = float(market_features.get("near_support_reclaim_after_flush", 0) or 0)
            points_above_prev_low = float(market_features.get("points_above_prev_low", 0) or 0)
            if (
                sess_range_zone == "lower_extreme"
                and support_reclaim < 1.0
                and near_support_reclaim < 1.0
                and points_above_prev_low < 0.0
            ):
                fail(f"long_lower_extreme_without_support_reclaim[{index}]")
        if estimated_risk < float(book["min_loss_per_master_contract_usd"]):
            fail(f"estimated_risk_below_book_minimum[{index}]")
        if estimated_risk > float(book["max_loss_per_master_contract_usd"]):
            fail(f"estimated_risk_over_book_limit[{index}]")
        estimated_reward = abs(target - price) * 2
        reward_risk = estimated_reward / estimated_risk
        if reward_risk + 0.000000001 < float(book["minimum_reward_risk"]):
            fail(f"reward_risk_below_book_minimum[{index}]")
        planned_minimum = float(book.get("minimum_planned_reward_risk", max(1.75, float(book["minimum_reward_risk"]))))
        if reward_risk + 0.000000001 < planned_minimum:
            fail(f"reward_risk_below_planned_headroom[{index}]")
    elif any(key in intent for key in ("quantity", "order_type", "stop_loss", "take_profit_1")):
        fail(f"non_entry_contains_entry_fields[{index}]")

print(json.dumps({"cycle_id": batch["cycle_id"], "decisions": len(decisions), "valid": True}))
