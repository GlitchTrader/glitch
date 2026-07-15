# Now — stable local paper proof

**Updated:** 2026-07-15
**Branch:** `glitch/ai-rail`
**Authority:** `backlog.md` and `docs/ai-program/operating-system-rail.md`

## Primary objective

Stabilize the reset local paper harness without Codex operating the market. Preserve master-only intent -> Glitch replication -> account-local native bracket architecture; make Journal truth reconcile to NinjaTrader; prove one- and two-leg native protection, partial-fill recovery, follower ratios, TP1 scale-out, surviving runner protection, terminal close, and outcome learning from authoritative ledger evidence.

The product target remains one centralized Hermes brain on a supervised VPS, one canonical recommendation per five-minute window, authenticated client polling, and local Glitch execution, management, replication, protection, compliance, and journaling. Expansion remains behind stable and profitable single-instrument paper evidence.

## Ordered implementation rail

1. `GL-055` source fix is implemented: deploy/F5, reset already-corrupt Journal data, and reconcile the fresh ledger to matching NinjaTrader account/time scope.
2. `GL-053` local two-leg source slice is implemented: F5, then run one bounded 3-contract Sim lifecycle proving master/follower OCO pairs, TP1-only reduction, runner coverage, TP2/SL terminal close, reconnect behavior, and journal/outcome truth.
3. Close remaining `GL-047`–`GL-049b` stabilization evidence before centralization or multi-instrument work.
4. Apply the mainline handoff in its recorded order, starting with `GL-055`; do not merge the AI rail wholesale.

`R14` remains a separate named-commit/operator gate for any non-simulation AI promotion.

## Operating loop

```text
read fresh market + portfolio + policy state
-> Hermes proposes one bounded intent for a group master
-> schema, freshness, scope, and risk validation
-> Glitch executes the master and owns replication plus native protection
-> NinjaTrader holds independent OCO pairs
-> Glitch journals authoritative account outcomes
-> Hermes learns from reconciled evidence
```

## Stop lines

- Codex builds and performs bounded verification; it does not operate or monitor the market.
- Invalid, stale, duplicate, malformed, unprotected, or out-of-scope output produces no order.
- A partial fill or protection failure enters bounded cancel/flatten recovery.
- A follower asymmetry must end flat or protected and be journaled.
- Paper results do not authorize live promotion. Live requires repository gates plus explicit operator approval.
