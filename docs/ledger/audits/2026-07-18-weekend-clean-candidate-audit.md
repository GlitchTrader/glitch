# Weekend clean-candidate audit — non-AI mainline

**Date:** 2026-07-18

**Branch:** `cleanup/main-core`
**Verdict:** ACCEPT as a beta replacement candidate after the explicit runtime gate below.

## Scope and non-scope

The candidate starts from clean `main` and selectively carries only shared Glitch
capabilities. It does not contain Hermes, AI services, intent endpoints, prompts,
sessions, model scheduling, AI Feed, or AI policy.

## Fixed shared defects

- self-copy and disconnected-route truth;
- cross-zero and oversized close handling;
- blind retry and pending-route retry hazards;
- startup catch-up and Replicate-OFF protection cancellation;
- manual follower re-entry after a human close;
- follower bracket registration race and naked-entry fallback;
- asynchronous protection rejection and multi-leg stop identity;
- duplicate/false-success Flatten All paths;
- orphan-exit Journal fabrication and reversal commission duplication;
- canonical contract/point-value and Analytics freshness identity;
- false FRED-derived event alerts.

## Verification

- Shared contracts: 32/32.
- Five production web builds and five lint runs passed.
- Python, PowerShell, JSON, diff, and changed-file secret checks passed.
- `npm audit --omit=dev` reports two moderate bundled-PostCSS findings. Stable Next
  is already 16.2.10; npm's proposed Next 9 downgrade is not acceptable.
- The shared C# files are byte-identical to the deployed AI candidate and compiled
  as part of that 87-file NinjaTrader build.

## Runtime gate

The current NinjaTrader target contains the AI superset, and the safe deployment
workflow does not delete extra files. Therefore this audit does not relabel the AI
superset compile as an exact non-AI package compile. Before merging or distributing:

1. build/deploy the non-AI package into a clean target;
2. F5 compile with no error rows;
3. run one bounded Sim entry/ratio/protection/close lifecycle;
4. confirm every configured account flat and order-free;
5. reconcile Journal output to NinjaTrader for the same scope.

No PA/live or profitability claim follows from this audit.
