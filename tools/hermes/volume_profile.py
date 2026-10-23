"""Volume-profile facts for the Hermes packet.

Code measures distances, nodes, and probes. It does not choose a stop or an entry.
Missing inputs are omitted with a reason. A full-map zero from NinjaTrader is kept.
"""

from __future__ import annotations

import json
import math
from typing import Any

FACTS_SCHEMA = "glitch.volume_profile_facts.v1"
FACTS_BUDGET_BYTES = 4096
VALUE_AREA_FRACTION = 0.7

_LEVEL_NAMES = (
    "session_high",
    "session_low",
    "previous_session_high",
    "previous_session_low",
    "session_open",
    "open_high",
)


def rows_are_sorted(profile: dict[str, Any]) -> bool:
    """True when every row array is strictly increasing by p."""
    if not _rows_sorted(profile.get("session_rows")):
        return False
    bars = profile.get("bars_1m")
    if bars is None:
        return True
    if not isinstance(bars, list):
        return False
    return all(_rows_sorted(bar.get("rows") if isinstance(bar, dict) else None) for bar in bars)


def snapshot_profile_delta(snapshot: dict[str, Any]) -> dict[str, int]:
    """UTF-8 bytes added by volume_profile on one snapshot and one corpus line."""
    encoded = _dumps(snapshot)
    stripped = json.loads(encoded)
    for instrument in stripped.get("instruments") or []:
        if isinstance(instrument, dict):
            instrument.pop("volume_profile", None)
    base = _dumps(stripped)
    added = len(encoded) - len(base)
    return {
        "snapshot_bytes": len(encoded),
        "snapshot_without_profile_bytes": len(base),
        "snapshot_delta_bytes": added,
        "corpus_line_delta_bytes": added,
    }


def build_facts(
    instrument: dict[str, Any],
    profile: dict[str, Any],
    ohlc_bars: list[dict[str, Any]] | None = None,
    swings: list[dict[str, Any]] | None = None,
    ohlc_source: str = "packet_frames",
) -> dict[str, Any]:
    facts: dict[str, Any] = {"schema_version": FACTS_SCHEMA}
    omitted: list[dict[str, str]] = []
    if not isinstance(profile, dict) or profile.get("status") != "ready":
        reason = "profile_missing"
        if isinstance(profile, dict) and profile.get("reason"):
            reason = str(profile["reason"])
        elif isinstance(profile, dict) and profile.get("status"):
            reason = str(profile["status"])
        omitted.append({"field": "volume_profile", "reason": reason})
        facts["omitted"] = omitted
        return facts

    tick_size = _positive(profile.get("tick_size")) or _positive(
        (instrument.get("instrument_economics") or {}).get("tick_size")
    )
    if tick_size is None:
        facts["omitted"] = [{"field": "volume_profile", "reason": "tick_size_missing"}]
        return facts

    current = _positive(instrument.get("current_price"))
    rows = profile.get("session_rows") if isinstance(profile.get("session_rows"), list) else []
    grid = _grid(rows, tick_size)
    truncated = bool(profile.get("session_truncated"))
    facts["nodes_scope"] = "truncated_window" if truncated else "session"
    full_map = profile.get("full_map") if isinstance(profile.get("full_map"), dict) else None
    if full_map and _positive(full_map.get("poc")):
        facts["poc"] = float(full_map["poc"])
        facts["vah"] = float(full_map["vah"])
        facts["val"] = float(full_map["val"])
        facts["value_area_fraction"] = VALUE_AREA_FRACTION
    elif truncated:
        omitted.append({"field": "poc", "reason": "profile_truncated"})
        omitted.append({"field": "vah", "reason": "profile_truncated"})
        omitted.append({"field": "val", "reason": "profile_truncated"})
    elif grid:
        poc, val, vah = _value_area(grid, tick_size, current)
        facts["poc"] = poc
        facts["vah"] = vah
        facts["val"] = val
        facts["value_area_fraction"] = VALUE_AREA_FRACTION

    atr = _atr(instrument)
    if atr is None:
        omitted.append({"field": "distance_atr_1m", "reason": "atr_unavailable"})
        omitted.append({"field": "probe_tolerance_atr", "reason": "atr_unavailable"})
        window = 40
        tolerance = 2 * tick_size
    else:
        window = max(40, math.ceil((2 * atr) / tick_size))
        tolerance = max(2 * tick_size, 0.1 * atr)
    facts["probe_tolerance_ticks"] = tolerance / tick_size

    if current is None:
        omitted.append({"field": "nodes", "reason": "current_price_missing"})
        hvn: list[dict[str, Any]] = []
        vacuums: list[dict[str, Any]] = []
    else:
        hvn = _hvn(grid, tick_size, current, atr, window)
        vacuums = _vacuums(grid, tick_size, current, atr, window)
    facts["hvn"] = hvn[:3]
    facts["lvn"] = vacuums[:3]
    if truncated:
        omitted.append({"field": "nodes_outside_window", "reason": "profile_truncated"})

    point_value = _positive((instrument.get("instrument_economics") or {}).get("point_value_usd"))
    if point_value is None:
        omitted.append({"field": "distance_usd_1ct", "reason": "economics_unavailable"})

    levels = _named_levels(instrument, full_map)
    if swings is None:
        omitted.append({"field": "swing", "reason": "swings_unavailable"})
    else:
        nearest = _nearest_swings(swings, current, 3) if current is not None else []
        if not nearest:
            omitted.append({"field": "swing", "reason": "swings_unavailable"})
        for swing in nearest:
            levels.append({"name": "swing", "label": swing.get("label"), "price": float(swing["price"])})

    bars = [_normalize_bar(bar) for bar in (ohlc_bars or [])]
    bars = [bar for bar in bars if bar is not None]
    facts["ohlc_source"] = ohlc_source if len(bars) >= 2 else "packet_frames"
    level_facts = []
    for level in levels:
        level_facts.append(_level_facts(
            level, grid, tick_size, current, atr, tolerance, truncated, full_map,
            bars, vacuums, point_value, omitted,
        ))
    facts["levels"] = level_facts
    facts["candle_facts"] = _candles(bars, omitted)
    if omitted:
        facts["omitted"] = omitted
    return _fit(facts)


def apply_to_model_packet(
    packet: dict[str, Any],
    *,
    ledger_bars: list[dict[str, Any]] | None = None,
    swings: list[dict[str, Any]] | None = None,
) -> None:
    """Strip raw profiles. Attach per-instrument facts on the latest frame only."""
    frames = packet.get("frames")
    if not isinstance(frames, list) or not frames:
        return
    ohlc_by_root = _ohlc_from_frames(frames)
    last = len(frames) - 1
    for index, frame in enumerate(frames):
        if not isinstance(frame, dict):
            continue
        market = frame.get("market_snapshot")
        if not isinstance(market, dict):
            continue
        instruments = market.get("instruments")
        if not isinstance(instruments, list):
            continue
        side: list[dict[str, Any]] = []
        kept: list[dict[str, Any]] = []
        for instrument in instruments:
            if not isinstance(instrument, dict):
                continue
            profile = instrument.pop("volume_profile", None)
            root = str(instrument.get("instrument") or instrument.get("instrument_root") or "")
            facts = None
            if index == last and isinstance(profile, dict):
                use_ledger = root == "MNQ" and isinstance(ledger_bars, list) and len(ledger_bars) >= 2
                bars = ledger_bars if use_ledger else ohlc_by_root.get(root) or []
                try:
                    facts = build_facts(
                        instrument,
                        profile,
                        bars,
                        swings if root == "MNQ" else None,
                        "market_structure_ledger" if use_ledger else "packet_frames",
                    )
                except Exception:
                    facts = {
                        "schema_version": FACTS_SCHEMA,
                        "omitted": [{"field": "volume_profile", "reason": "facts_failed"}],
                    }
                instrument["volume_profile_facts"] = facts
            elif index == last:
                instrument["volume_profile_facts_omitted"] = "profile_missing"
            if root == "MNQ":
                kept.append(instrument)
            elif facts is not None:
                side.append({"instrument": root, "volume_profile_facts": facts})
        if any(str(item.get("instrument") or item.get("instrument_root")) == "MNQ" for item in instruments if isinstance(item, dict)):
            market["instruments"] = kept
            market["instrument_count"] = len(kept)
        if index == last and side:
            market["volume_profile_facts_by_instrument"] = side


def _fit(facts: dict[str, Any]) -> dict[str, Any]:
    if _size(facts) <= FACTS_BUDGET_BYTES:
        return facts
    facts.pop("candle_facts", None)
    if _size(facts) <= FACTS_BUDGET_BYTES:
        return facts
    for level in facts.get("levels") or []:
        if isinstance(level, dict):
            level.pop("outcomes", None)
    if _size(facts) <= FACTS_BUDGET_BYTES:
        return facts
    if isinstance(facts.get("hvn"), list) and len(facts["hvn"]) > 1:
        facts["hvn"] = facts["hvn"][:1]
    if isinstance(facts.get("lvn"), list) and len(facts["lvn"]) > 1:
        facts["lvn"] = facts["lvn"][:1]
    return facts


def _level_facts(
    level: dict[str, Any],
    grid: dict[int, tuple[float, float]],
    tick_size: float,
    current: float | None,
    atr: float | None,
    tolerance: float,
    truncated: bool,
    full_map: dict[str, Any] | None,
    bars: list[dict[str, float | str]],
    vacuums: list[dict[str, Any]],
    point_value: float | None,
    omitted: list[dict[str, str]],
) -> dict[str, Any]:
    price = float(level["price"])
    out: dict[str, Any] = {"name": level["name"], "price": price}
    if level.get("label"):
        out["label"] = level["label"]
    level_tick = _to_tick(price, tick_size)
    ticks = list(grid)
    outside = bool(ticks) and (level_tick < min(ticks) or level_tick > max(ticks))
    if truncated and outside:
        omitted.append({"field": f"{level['name']}.volume_at", "reason": "profile_truncated"})
    elif grid:
        volume_at = _sum_total(grid, level_tick, level_tick)
        volume_near = _sum_total(grid, level_tick - 4, level_tick + 4)
        bid_near, ask_near = _sum_split(grid, level_tick - 4, level_tick + 4)
        out["volume_at"] = volume_at
        out["volume_near"] = volume_near
        out["bid_near"] = bid_near
        out["ask_near"] = ask_near
    mapped = _full_map_level(full_map, level["name"], price)
    if mapped is not None:
        out["volume_above"] = mapped["volume_above"]
        out["volume_below"] = mapped["volume_below"]
    elif truncated:
        omitted.append({"field": f"{level['name']}.volume_above", "reason": "profile_truncated"})
    elif grid:
        out["volume_above"] = _sum_total_above(grid, level_tick)
        out["volume_below"] = _sum_total_below(grid, level_tick)
    if len(bars) < 2:
        omitted.append({"field": f"{level['name']}.area_tests", "reason": "bars_insufficient"})
    else:
        probes = _probes(bars, price, tolerance)
        out["touch_count"] = len(probes)
        out["outcomes"] = probes[-8:]
    beyond = _beyond(vacuums, price, current, tick_size, atr, point_value)
    out.update(beyond)
    return out


def _probes(bars: list[dict[str, Any]], level: float, tolerance: float) -> list[dict[str, str]]:
    outcomes = []
    previous_close = None
    for bar in bars:
        high = float(bar["h"])
        low = float(bar["l"])
        close = float(bar["c"])
        open_ = float(bar["o"])
        if high >= level - tolerance and low <= level + tolerance:
            anchor = open_
            if abs(anchor - level) <= tolerance and previous_close is not None:
                anchor = previous_close
            if anchor < level:
                if close > level + tolerance:
                    kind = "acceptance"
                elif close < level - tolerance:
                    kind = "rejection"
                else:
                    kind = "unresolved"
            else:
                if close < level - tolerance:
                    kind = "acceptance"
                elif close > level + tolerance:
                    kind = "rejection"
                else:
                    kind = "unresolved"
            outcomes.append({"bar_utc": str(bar["utc"]), "classification": kind})
        previous_close = close
    return outcomes


def _candles(bars: list[dict[str, Any]], omitted: list[dict[str, str]]) -> list[dict[str, Any]]:
    if len(bars) < 2:
        omitted.append({"field": "candle_facts", "reason": "bars_insufficient"})
        return []
    current = bars[-1]
    prior = bars[-2]
    utc = str(current["utc"])
    facts = [
        {
            "name": "engulfing_bull",
            "value": _bull_body(current) and _bear_body(prior) and _covers(current, prior),
            "bar_utc": utc,
        },
        {
            "name": "engulfing_bear",
            "value": _bear_body(current) and _bull_body(prior) and _covers(current, prior),
            "bar_utc": utc,
        },
        {
            "name": "close_above_prior_close",
            "value": float(current["c"]) > float(prior["c"]),
            "bar_utc": utc,
        },
        {
            "name": "close_below_prior_close",
            "value": float(current["c"]) < float(prior["c"]),
            "bar_utc": utc,
        },
    ]
    if len(bars) < 3:
        omitted.append({"field": "n_candle_up", "reason": "bars_insufficient"})
        omitted.append({"field": "n_candle_down", "reason": "bars_insufficient"})
        return facts
    closes = [float(bar["c"]) for bar in bars[-3:]]
    facts.append({
        "name": "n_candle_up",
        "value": closes[0] < closes[1] < closes[2],
        "bar_utc": utc,
    })
    facts.append({
        "name": "n_candle_down",
        "value": closes[0] > closes[1] > closes[2],
        "bar_utc": utc,
    })
    return facts


def _beyond(
    vacuums: list[dict[str, Any]],
    level: float,
    current: float | None,
    tick_size: float,
    atr: float | None,
    point_value: float | None,
) -> dict[str, Any]:
    if current is None:
        return {}
    above = level > current + tick_size
    below = level < current - tick_size
    near = not above and not below

    def pack(vacuum: dict[str, Any], edge: float) -> dict[str, Any]:
        distance_ticks = abs(edge - level) / tick_size
        payload = {
            "p_near": edge,
            "p_far": vacuum["p_far"] if edge == vacuum["p_near"] else vacuum["p_near"],
            "span_ticks": vacuum["span_ticks"],
            "distance_ticks": distance_ticks,
        }
        if atr:
            payload["distance_atr_1m"] = abs(edge - level) / atr
        if point_value is not None:
            payload["distance_usd_1ct"] = distance_ticks * tick_size * point_value
        return payload

    def nearest(side: str) -> dict[str, Any] | None:
        best = None
        best_distance = None
        for vacuum in vacuums:
            lo = min(vacuum["p_near"], vacuum["p_far"])
            hi = max(vacuum["p_near"], vacuum["p_far"])
            if side == "up" and lo > level:
                edge = lo
            elif side == "down" and hi < level:
                edge = hi
            else:
                continue
            distance = abs(edge - level)
            if best_distance is None or distance < best_distance:
                best = pack(vacuum, edge)
                best_distance = distance
        return best

    out: dict[str, Any] = {}
    if above or near:
        found = nearest("up")
        if found:
            out["beyond_up" if near else "nearest_vacuum_beyond"] = found
    if below or near:
        found = nearest("down")
        if found:
            out["beyond_down" if near else "nearest_vacuum_beyond"] = found
    return out


def _hvn(grid, tick_size, current, atr, window):
    if not grid:
        return []
    totals = {tick: bid + ask for tick, (bid, ask) in grid.items() if bid + ask > 0}
    if not totals:
        return []
    median = _median(list(totals.values()))
    nodes = []
    for tick, total in totals.items():
        left = grid.get(tick - 1, (0, 0))
        right = grid.get(tick + 1, (0, 0))
        if total <= (left[0] + left[1]) or total <= (right[0] + right[1]):
            continue
        if total < 1.5 * median:
            continue
        price = tick * tick_size
        distance = abs(price - current) / tick_size
        if distance > window:
            continue
        node = {"p": price, "total": total, "distance_ticks": distance}
        if atr:
            node["distance_atr_1m"] = abs(price - current) / atr
        nodes.append(node)
    nodes.sort(key=lambda item: (item["distance_ticks"], item["p"]))
    return nodes


def _vacuums(grid, tick_size, current, atr, window):
    if not grid:
        return []
    positive = [bid + ask for bid, ask in grid.values() if bid + ask > 0]
    if not positive:
        return []
    limit = max(1.0, 0.20 * _median(positive))
    lo = min(grid)
    hi = max(grid)
    runs: list[tuple[int, int]] = []
    run_start = None
    for tick in range(lo, hi + 1):
        bid, ask = grid.get(tick, (0.0, 0.0))
        thin = (bid + ask) <= limit
        if thin and run_start is None:
            run_start = tick
        elif not thin and run_start is not None:
            runs.append((run_start, tick - 1))
            run_start = None
    if run_start is not None:
        runs.append((run_start, hi))
    current_tick = _to_tick(current, tick_size)
    found = []
    for start, end in runs:
        span = end - start + 1
        if span < 4:
            continue
        if current_tick < start:
            near, far = start, end
        elif current_tick > end:
            near, far = end, start
        else:
            continue
        distance = abs(near - current_tick)
        if distance > window:
            continue
        vacuum = {
            "p_near": near * tick_size,
            "p_far": far * tick_size,
            "span_ticks": span,
            "distance_ticks": float(distance),
        }
        if atr:
            vacuum["distance_atr_1m"] = abs(near * tick_size - current) / atr
        found.append(vacuum)
    found.sort(key=lambda item: (item["distance_ticks"], item["p_near"]))
    return found


def _value_area(grid, tick_size, current):
    totals = {tick: bid + ask for tick, (bid, ask) in grid.items()}
    session_total = sum(totals.values())
    anchor = _to_tick(current, tick_size) if current is not None else 0

    def rank(tick):
        distance = abs(tick - anchor) if current is not None else 0
        return (-totals[tick], distance, tick)

    poc_tick = min(totals, key=rank)
    lo = hi = poc_tick
    included = totals[poc_tick]
    target = session_total * VALUE_AREA_FRACTION
    min_tick, max_tick = min(totals), max(totals)
    while included + 1e-9 < target and (lo > min_tick or hi < max_tick):
        left = totals.get(lo - 1, 0) if lo > min_tick else -1
        right = totals.get(hi + 1, 0) if hi < max_tick else -1
        if left < 0 and right < 0:
            break
        if left >= 0 and right >= 0 and left == right:
            lo -= 1
            hi += 1
            included += left + right
        elif right < 0 or (left >= 0 and left > right):
            lo -= 1
            included += left
        else:
            hi += 1
            included += right
    return poc_tick * tick_size, lo * tick_size, hi * tick_size


def _named_levels(instrument, full_map):
    session = instrument.get("session") if isinstance(instrument.get("session"), dict) else {}
    prices = {
        "session_high": session.get("high"),
        "session_low": session.get("low"),
        "previous_session_high": session.get("previous_high"),
        "previous_session_low": session.get("previous_low"),
    }
    for item in (full_map or {}).get("levels") or []:
        if isinstance(item, dict) and item.get("name") in _LEVEL_NAMES and _number(item.get("price")):
            prices[item["name"]] = item["price"]
    levels = []
    for name in _LEVEL_NAMES:
        price = prices.get(name)
        if _number(price) and float(price) > 0:
            levels.append({"name": name, "price": float(price)})
    return levels


def _full_map_level(full_map, name, price):
    if name == "swing" or not isinstance(full_map, dict):
        return None
    for item in full_map.get("levels") or []:
        if isinstance(item, dict) and item.get("name") == name and _number(item.get("volume_above")):
            return {
                "volume_above": float(item["volume_above"]),
                "volume_below": float(item["volume_below"]),
            }
    return None


def _nearest_swings(swings, current, limit):
    usable = []
    for swing in swings:
        if isinstance(swing, dict) and _number(swing.get("price")):
            usable.append(swing)
    usable.sort(key=lambda item: (abs(float(item["price"]) - current), float(item["price"])))
    return usable[:limit]


def _ohlc_from_frames(frames):
    found: dict[str, list[dict[str, Any]]] = {}
    for frame in frames:
        if not isinstance(frame, dict):
            continue
        market = frame.get("market_snapshot")
        if not isinstance(market, dict):
            continue
        for instrument in market.get("instruments") or []:
            if not isinstance(instrument, dict):
                continue
            root = str(instrument.get("instrument") or instrument.get("instrument_root") or "")
            for bar in instrument.get("timeframe_bars") or []:
                if not isinstance(bar, dict) or bar.get("minutes") not in (1, None):
                    continue
                normalized = _normalize_bar(bar, bar.get("utc_time") or frame.get("minute_id"))
                if normalized is None:
                    continue
                found.setdefault(root, []).append(normalized)
    return found


def _normalize_bar(bar, fallback_utc=None):
    if not isinstance(bar, dict):
        return None
    mapping = (
        ("o", ("o", "open")),
        ("h", ("h", "high")),
        ("l", ("l", "low")),
        ("c", ("c", "close")),
    )
    values = {}
    for key, aliases in mapping:
        raw = next((bar.get(alias) for alias in aliases if bar.get(alias) is not None), None)
        if not _number(raw):
            return None
        values[key] = float(raw)
    utc = bar.get("utc") or bar.get("utc_time") or bar.get("id") or fallback_utc
    if not utc:
        return None
    values["utc"] = str(utc)
    return values


def _grid(rows, tick_size):
    grid: dict[int, tuple[float, float]] = {}
    for row in rows:
        if not isinstance(row, dict) or not _number(row.get("p")):
            continue
        tick = _to_tick(float(row["p"]), tick_size)
        bid = float(row.get("b") or 0)
        ask = float(row.get("a") or 0)
        previous = grid.get(tick, (0.0, 0.0))
        grid[tick] = (previous[0] + bid, previous[1] + ask)
    return grid


def _sum_total(grid, lo, hi):
    return sum(bid + ask for tick, (bid, ask) in grid.items() if lo <= tick <= hi)


def _sum_total_above(grid, level_tick):
    return sum(bid + ask for tick, (bid, ask) in grid.items() if tick > level_tick)


def _sum_total_below(grid, level_tick):
    return sum(bid + ask for tick, (bid, ask) in grid.items() if tick < level_tick)


def _sum_split(grid, lo, hi):
    bid = ask = 0.0
    for tick, (row_bid, row_ask) in grid.items():
        if lo <= tick <= hi:
            bid += row_bid
            ask += row_ask
    return bid, ask


def _atr(instrument):
    for bar in instrument.get("timeframe_bars") or []:
        if isinstance(bar, dict) and bar.get("minutes") == 1:
            atr = (bar.get("indicators") or {}).get("atr")
            if _positive(atr):
                return float(atr)
    return None


def _rows_sorted(rows) -> bool:
    if rows is None:
        return True
    if not isinstance(rows, list):
        return False
    previous = None
    for row in rows:
        if not isinstance(row, dict) or not _number(row.get("p")):
            return False
        price = float(row["p"])
        if previous is not None and price <= previous:
            return False
        previous = price
    return True


def _bull_body(bar):
    return float(bar["c"]) > float(bar["o"])


def _bear_body(bar):
    return float(bar["c"]) < float(bar["o"])


def _covers(current, prior):
    current_low = min(float(current["o"]), float(current["c"]))
    current_high = max(float(current["o"]), float(current["c"]))
    prior_low = min(float(prior["o"]), float(prior["c"]))
    prior_high = max(float(prior["o"]), float(prior["c"]))
    return current_low <= prior_low and current_high >= prior_high


def _median(values: list[float]) -> float:
    ordered = sorted(values)
    mid = len(ordered) // 2
    if len(ordered) % 2:
        return float(ordered[mid])
    return (float(ordered[mid - 1]) + float(ordered[mid])) / 2


def _to_tick(price: float, tick_size: float) -> int:
    scaled = price / tick_size
    if scaled >= 0:
        return int(math.floor(scaled + 0.5))
    return int(math.ceil(scaled - 0.5))


def _positive(value):
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    if not math.isfinite(float(value)) or float(value) <= 0:
        return None
    return float(value)


def _number(value):
    return not isinstance(value, bool) and isinstance(value, (int, float)) and math.isfinite(float(value))


def _dumps(value) -> bytes:
    return json.dumps(value, separators=(",", ":")).encode("utf-8")


def _size(value) -> int:
    return len(_dumps(value))
