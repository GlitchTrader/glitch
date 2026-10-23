# Volume-at-price contract (Track 0)

Status: accepted, with the operator amendments below. Tracks A, B, and C follow this file and `glitch_hermes_docs/schemas/volume-at-price.v1.schema.json`. Amend this file before code diverges. Track D is out of scope.

This is the contract those tracks implement. It is not a claim that a running NinjaTrader instance already emits the object.

## Placement

- Snapshot schema stays `glitch.market.snapshot.v2`. A corpus line stays one v2 snapshot document (`glitch.market.corpus.v1` wrapper unchanged).
- Optional `volume_profile` is added on the instrument object from `GlitchMarketSnapshotRawJson.BuildInstrumentJson`. Absence means it was not serialized. Consumers must not treat absence as zero volume.
- `packet_for_model` removes raw `volume_profile` from every instrument in every frame. The model sees `volume_profile_facts` only.
- Facts are computed per instrument, each object at most 4096 UTF-8 bytes. The direct-cycle market filter still leaves only MNQ inside `instruments` (existing compaction; `test_model_packet_contains_only_configured_sim_group_and_mnq`). Facts for every other instrument that had a profile are kept on the latest frame as `volume_profile_facts_by_instrument`. They are not dropped with the instrument. A kept instrument with no profile records `volume_profile_facts_omitted: "profile_missing"` and no fabricated numbers.
- Facts attach on the latest frame only. Earlier frames keep neither the raw profile nor a second copy of the facts.
- The market-structure ledger that exists today is MNQ bars only. Swing levels from that ledger are attached only for MNQ. Other instruments omit swing levels with reason `swings_unavailable`.

## Session key and previous session

`SessionBlock.Key` is `Name|yyyyMMddHH`, and the hour is the session start, not the bar hour. `SessionBlock.Resolve` returns NYC at 08:00, London at 03:00, or Asia at 16:00 local. Inside a session the key does not change when the clock hour changes. Asia after midnight still uses the previous calendar day's 16:00 start, so midnight does not start a new key.

`previous_session_high` and `previous_session_low` come from `SessionTracker.Update`. When `SessionKey` changes, the tracker's `CurrentHigh` and `CurrentLow` are copied to `PreviousHigh` and `PreviousLow`, then published as `session.previous_high` and `session.previous_low`. They are the high and low of the previous Glitch block (Asia, London, or NYC) on that timeframe, not the previous exchange trading day. The volume profile uses the 1-minute tracker.

The profile map resets only when that 1-minute key changes.

## Raw profile

`schema_version` is `glitch.volume_at_price.v1`.

`status` is `ready`, `warming`, or `unavailable`.

- `ready`: at least one session price with volume.
- `warming`: the map exists and no trade has been classified yet. `reason` is `no_trades`.
- `unavailable`: tick size is missing or the accumulator was not armed. `reason` is required unless `status` is `ready`.

`classification` is `bid_ask_at_quote_else_tick_rule` when any trade in the session was classified with bid/ask quotes, otherwise `tick_rule_only`. Historical and replay ticks that never saw quotes stay `tick_rule_only`.

Each row is `{ "p", "b", "a" }`. `p` is the trade price rounded to the nearest tick, midpoint away from zero. Ask `a` is buyer-aggressor size. Bid `b` is seller-aggressor size. The only classifier is the one already in `OnMarketData`: at-quote, then tick rule, then a 50/50 split of that single trade. Row total is `b + a` and is not stored.

Volume format: the 50/50 split is the only fractional source, so each stored size is a multiple of 0.5. Serialize with invariant culture and no grouping. If the value is a whole number after rounding half away from zero to one decimal, emit an integer (`2`). Otherwise emit one decimal (`1.5`, `0.5`). Do not use general or scientific format.

`session_rows` and every candle's `rows` are strictly increasing by `p`. The schema records `x-glitch-row-order: p-ascending-strict`. The serializer sorts. Tests reject unsorted rows. JSON Schema has no standard monotonic-array keyword; that annotation plus the serializer and tests are the requirement.

`session_rows` emitted in the snapshot are at most 800 prices, the ones nearest the last trade, still sorted by `p`. When the cap drops prices, `session_truncated` is true and `session_omitted_bins` is the dropped count. `session_bid_total`, `session_ask_total`, and `session_volume_total` always come from the uncapped map when `status` is `ready`.

`bars_1m` is the last 5 closed 1-minute candles, oldest first. The forming minute is not included. Each candle has at most 120 rows, nearest that candle's last trade. `bar_truncated` and `bar_omitted_bins` are set when a candle is cut. A corpus export seals the completed minute before serialization so that closed bar is the newest entry.

## Full map, computed before the cut

NinjaTrader computes these from the uncapped session map and writes them on `full_map` even when `session_rows` is truncated. A trend day must not lose them.

- `poc`, `vah`, `val`, `value_area_fraction` (`0.7`).
- For each named level that has a price: `volume_above` and `volume_below` (strictly above or below the level tick).

POC: maximum `b + a`. Tie: smaller distance to the last trade, then lower price.

Value area: start at the POC tick. Walk one-tick neighbors across the full span, treating a missing tick as volume 0. Add the neighbor with more volume. On a tie, add both. Stop when included volume is at least 70% of `session_volume_total`. VAH is the highest included price, VAL the lowest.

A computed zero from that full map is a real measurement and is emitted. A level with no price is omitted, not written as zero.

Named levels on `full_map`: `session_high`, `session_low`, `previous_session_high`, `previous_session_low`, `session_open`, `open_high`. `session_open` is `SessionTracker.SessionOpen` on the 1-minute series. `open_high` is the high of the first closed 1-minute bar of the current key. If that bar has not closed, omit `open_high`.

## Hot path

Live trades update the map inside the existing `OnMarketData` last-trade path, after the current buy/sell split, and only when that split accepted the trade. Realtime accumulation does not require a chart (`ChartControl`); requiring one previously froze the map while snapshots still published `status=ready`. Realtime does not also accumulate the tick series. Historical and replay accumulate the existing 1-tick series in `OnBarUpdate` with the tick rule when order flow or historical export enabled the tick BIP.

If no trade has mutated the map for more than three minutes, publish `status=stale` with `reason=no_recent_trades` and `last_trade_utc` instead of `ready`. Downstream fact builders (`volume_profile.py`, market-perception overlay) already require `status=ready`, so stale profiles are omitted as neutral rather than used for vacuum-stop evidence.

One `VolumeBin` is allocated the first time a price appears in the session map or the open minute. Later trades at that price only mutate `Bid` and `Ask`. The session key string is cached until the local hour changes, so a tick does not allocate a new key. Snapshot JSON is built on the existing publish or export cadence, not per tick.

## Derived facts

`tools/hermes/volume_profile.py` builds `volume_profile_facts` (`glitch.volume_profile_facts.v1`) from the raw profile plus supplied OHLC and swings. `run-direct-glitch-cycle.py` attaches the result. Failure or a missing profile does not raise and does not block or force a decision.

When `full_map` is present, POC, VAH, VAL, and per-level volume above/below are copied from it. They are not recomputed from a truncated window. If `full_map` is missing and `session_truncated` is not true, those fields are computed from `session_rows` with the same rules. If `full_map` is missing and the profile is truncated, those fields are omitted with reason `profile_truncated`.

Other facts come from the serialized rows and supplied bars:

- Near-price window: `max(40 ticks, ceil(2 * atr_1m / tick_size))`. If ATR is missing, the window is 40 ticks and `distance_atr_1m` is omitted with reason `atr_unavailable`.
- HVN: a row greater than both adjacent ticks (missing neighbor is 0) and at least 1.5 times the median of positive row totals, inside the window. At most 3, nearest first.
- LVN / vacuum: a contiguous run of at least 4 ticks, including empty ticks between the first and last serialized price, where every tick total is `<= max(1, 0.20 * median positive total)`, with its nearer edge inside the window. At most 3, nearest first. One thin bin is not a vacuum.
- If `session_truncated` is true, nodes and vacuums describe the serialized window. Set `nodes_scope` to `truncated_window`. Do not pretend the window is the whole session.
- `volume_at` and `volume_near` (±4 ticks, inclusive), plus `bid_near` and `ask_near`, come from serialized rows. If the level lies outside a truncated window, omit them with reason `profile_truncated`. Volume above/below still come from `full_map` when present.
- Probe tolerance is `max(2 * tick_size, 0.1 * atr_1m)`. If ATR is missing, use 2 ticks and record `probe_tolerance_atr` omitted with reason `atr_unavailable`. A probe is a 1-minute bar whose range reaches that tolerance around the level. `touch_count` is the probe count. Classification uses the approach side (open, else the prior close): `acceptance` when the close finishes beyond the level by more than the tolerance, `rejection` when it finishes back beyond the tolerance, otherwise `unresolved`. Keep at most the latest 8 outcomes, each with `bar_utc`. If fewer than 2 OHLC bars exist, omit area tests with reason `bars_insufficient`.
- OHLC preference: the market-structure ledger when it has at least 2 bars for that instrument, otherwise the packet's 1-minute frames. `ohlc_source` records which one was used.
- Tested levels are the named session levels that have a price, plus the 2 or 3 swings nearest the current price from the ledger. A swing's `name` is `swing` and it keeps the ledger `label`.
- Nearest vacuum beyond a level: if the level is more than one tick above price, the nearest vacuum whose span is entirely above the level; if more than one tick below, the nearest vacuum entirely below. A level within one tick of price reports `beyond_up` and `beyond_down` independently when each exists. Distance is from the level to the nearer edge, in ticks. `distance_usd_1ct = distance_ticks * tick_size * point_value_usd`. Omit dollars with reason `economics_unavailable` when point value is missing. This is a reference, not a stop instruction.
- Candle facts, only when the required bars exist, each with boolean `value` and `bar_utc` of the last bar used: `engulfing_bull`, `engulfing_bear` (real bodies; the current body covers the prior body and the prior body is the opposite color), `close_above_prior_close`, `close_below_prior_close`, `n_candle_up`, `n_candle_down` (`N = 3`, strictly rising or falling closes). Omit a fact whose bars are missing. Do not emit `false` for missing bars.

UTF-8 size of one instrument's facts object must stay ≤ 4096 bytes (`json` separators `,` and `:`). Shed in this order: candle facts, area-test outcome lists (keep `touch_count`), then nodes beyond the nearest HVN and nearest LVN. Do not drop `omitted`.

## Track C binding

- Skill `skills/glitch-volume-profile/SKILL.md` in the profile repo, at most 2560 bytes. If a draft exceeds that, merge the text into `glitch-order-flow` instead of adding the file.
- Direct-cycle skill list is `glitch-trade-mnq,glitch-volume-profile,glitch-build-intent`.
- One sentence in `SOUL.md`, in the paragraph that begins "Choose one coherent wager": a stop may use a volume vacuum beyond the level as supporting evidence for where the chosen wager is actually wrong.
- No playbook, no example trades, no new decision gate.

## Fixture and measurement

`tools/hermes/tests/fixtures/volume_at_price_snapshot.v1.json` holds a hand-written v2 snapshot (`snapshot`) plus OHLC and swings used only by tests. The snapshot includes `full_map` and does not invent a second POC outside that object.

Report, from the serializer sample and from this fixture:

- UTF-8 bytes added to one snapshot document by `volume_profile`
- the same delta for one corpus line (the snapshot document plus a trailing newline)
- UTF-8 bytes added to the model packet by facts, after the raw profile has been removed
