# Glitch North Star

## Product doctrine

```text
Trust before intelligence.
Native NinjaTrader state before cached or inferred state.
Hermes decides; Glitch validates, executes, protects, replicates, reconciles, and journals.
```

## Current product lines

- Standard v0.0.2.0 is the official default download.
- Experimental AI v0.0.2.2 is separate, uses public Hermes profile v0.0.2.4, and carries no profitability, unattended-operation, PA, or live-readiness claim.
- `main` owns the explicit release catalog. A ZIP is not a release until the catalog and checksum register it.

## Invariants

- Native NinjaTrader account, position, order, execution, and PnL truth outranks local state.
- One producer-neutral CopyEngine copies each master execution delta at the configured ratio, owns follower-native OCO protection and close propagation, preserves manual follower changes without blocking later copies, and performs catch-up only on explicit user resync.
- Hermes owns thesis, direction, master quantity, geometry, timing, scaling, management, and self-review.
- Inferred policy does not override Hermes intent or an already accepted native master execution.
- Glitch may reject only factual invalidity, ambiguous native state, ownership, incomplete protection, explicit human-enabled compliance locks, and structurally unprovable native mutations. Prop-firm capacity, liquidation buffers, sessions, and time windows remain packet evidence unless a visible default-off Settings compliance action is enabled.
- Code never chooses a strategy, quantity schedule, stop formula, risk percentage, target formula, quota, grid, or martingale behavior.
- Decisions, receipts, outcomes, journals, episodes, memory, and supervisory review remain attributable through stable IDs.
- Codex builds and verifies code; it is not a runtime trader.

Tests and compiles prove software shape, not profitability or account authorization. Historical plans remain provenance and never override current source, `ledger.json` on `main`, the release catalog, or current operator direction.
