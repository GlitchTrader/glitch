# Glitch North Star

## Product doctrine

```text
Trust before intelligence.
Native NinjaTrader state before cached or inferred state.
Hermes decides; Glitch validates, executes, protects, replicates, reconciles, and journals.
```

## Current product lines

- **Standard v0.0.2.0** is the official default download for manual trading, analytics, account management, risk controls, journal, and replication.
- **Experimental AI v0.0.2.2** is a separate package and update channel. It uses the public Hermes profile **v0.0.2.4** and is not a profitability, unattended-operation, or live-readiness claim.
- `main` owns the explicit release catalog. A ZIP is not a release until the catalog and checksum register it.

## Invariants

- **PnL truth:** display the native NinjaTrader value for the same account and session. Unknown native data stays unknown.
- **Replication integrity:** one producer-neutral `GlitchCopyEngine` owns followers, ratios, follower-native OCO protection, close propagation, and explicit resync.
- **User sovereignty:** the user selects accounts, groups, ratios, and enabled risk actions. Startup and recompile are observe-only.
- **Bounded mutation:** ambiguous order state is never blindly retried. Flatten and protection recovery report unresolved state.
- **Cognitive authority:** Hermes owns thesis, direction, master quantity, geometry, timing, scaling, and management. Code does not hard-code a trading strategy.
- **Survival boundary:** Glitch may reject only factual invalidity, missing native truth, ownership violations, incomplete protection, contract capacity, and authoritative account-survival limits.
- **Learning continuity:** decisions, receipts, outcomes, journals, episodes, memory, and supervisory reviews remain attributable through stable IDs.
- **Builder boundary:** Codex changes and verifies code. It is not a runtime trader or a substitute for Hermes cognition.
- **Localization:** authored product, Docs, Guide, Website, and Download copy supports English, Brazilian Portuguese, Spanish, Simplified Chinese, French, and Russian. Broker/model text remains verbatim.

## Release evidence

Source tests, builds, and a green NinjaTrader compile prove software shape, not profitability or account authorization. Promotion claims require the exact artifact, named source commit, bounded native lifecycle evidence, and explicit operator approval.

Historical plans and audits remain useful provenance but do not override `ledger.json`, the release catalog, current source, or current user direction.
