# Glitch AI Roadmap

**Private maintainer summary — reconciled 2026-07-22.**

## Authority model

Hermes owns cognition and master trade decisions. Glitch owns factual state, deterministic execution safety, native protection, replication, attribution, and evidence. NinjaTrader owns the native book.

## Delivered sequence

| Release | Delivered |
|---|---|
| Standard v0.0.2.0 | Shared execution-driven replication core, follower-native OCO protection, explicit resync, native metadata, Journal/Analytics corrections, six-language AddOn |
| AI v0.0.2.0 | Separate Experimental AI edition and customer-installable Hermes profile |
| AI v0.0.2.1 | Adaptive position-building context, Apex liquidation-buffer boundary, learning repair/catch-up, staged cognitive overlays |
| AI v0.0.2.2 | Intent v3 per-leg management, safe stop fallback, independent geometry, gap-aware minute publication, next-packet retries, crash-safe intent delivery, hidden-window continuity, atomic reconciliation, decision-episode learning |
| Profile v0.0.2.4 | Matching public SOUL, skills, plugin, workers, setup, and update path |

These version numbers are release identities, not a deterministic maturity ladder. Historical Eyes/Voice/Ears/Hands labels are archival only.

## Current verification lane

1. Runtime-prove the v0.0.2.2 lifecycle in bounded Sim.
2. Confirm per-leg TP/SL changes leave siblings unchanged and mirror to follower-native protection.
3. Confirm safe widening succeeds only inside authoritative Apex capacity; unsafe widening mutates nothing.
4. Confirm hidden/minimized runtime, next-packet retries, stale-lock recovery, and crash reconciliation.
5. Confirm NOTHING/rejection episodes reach slower learning loops once, without duplicates.
6. Freeze an attributable paper sample and evaluate performance without coding the observed strategy.

## Later capabilities

- A complete pending-entry lifecycle may add limit orders only with place/cancel/replace, TIF/expiry, partial-fill protection, replication, identity, and restart recovery.
- Additional instruments require native metadata, session truth, normalized packets, protection math, and independent evidence.
- Unattended account operation requires authoritative holidays/special closes and dependency/recovery evidence.
- Cognitive improvements remain versioned proposals with independent confirmation and rollback.

## Permanent non-goals

- fixed quantity schedules, stop formulas, risk percentages, target formulas, quotas, grid, or martingale logic;
- follower sizing inside Hermes;
- broker credentials or direct NinjaTrader object access in Hermes;
- profitability claims inferred from tests, prompts, or a small discretionary sample;
- a centralized VPS as the required customer distribution model.

See `operating-system-rail.md`, `docs/ledger/now.md`, and `docs/ledger/backlog.md` for current contracts and stop lines.
