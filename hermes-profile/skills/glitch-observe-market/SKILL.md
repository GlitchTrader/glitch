---
name: glitch-observe-market
description: Read one completed Glitch MNQ market snapshot and bounded historical context, verify data quality, and classify the observable regime without issuing a trade.
---

# Observe Market

Use only files supplied in `/opt/glitch-data`.

1. For a supplied `glitch.hermes.portfolio_cycle.v1`, treat that cycle's market envelope and `local_safety_attestation` as the only current-cycle authority. Require its snapshot hash, MNQ, completed bars, and machine features. Do not inspect or use `current/policy.json`, `current/portfolio.latest.json`, or old journal records to re-decide current account eligibility; Glitch already performed that private local check. For other cycle types, require the explicitly supplied current market and policy inputs. Mark the cycle invalid only when a required supplied item is absent or stale.
2. Describe only observable state: session, price location, trend/alignment across 1m/5m/15m/60m, volatility, momentum, liquidity/order flow when present, and conflicts.
3. Use supplied `market.machine_features` and `market.archetype_evaluation` as the exact local measurement layer. Compare with active `knowledge/archetypes.v2.json` and retrieved examples under `history/market/`. V2 statuses and regime preconditions are authoritative evidence; archetypes remain priors rather than a whitelist.
4. Never claim or tag an archetype match unless its supplied `exact_match` is true. If no exact match exists, a falsifiable novel thesis remains allowed but must be tagged `discretionary_candidate:<short_name>` and must name the mismatch with the closest archetype.
5. Do not recompute or override supplied machine features, infer indicators that are missing, or use future bars.
6. Pass a compact observation to the risk and thesis steps: `data_valid`, `regime`, `supporting_signals`, `conflicting_signals`, `matched_archetypes`, and `novel_pattern_notes`.

This skill never emits or submits an intent.
