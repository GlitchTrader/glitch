# Now — clean non-AI candidate

**Updated:** 2026-07-18

**Branch:** `cleanup/main-core`
**Role:** non-AI beta replacement candidate; no Hermes or AI runtime is included.

## Candidate

- One producer-neutral CopyEngine owns configured master-to-follower replication,
  ratios, explicit resync, follower-native OCO protection, and complete close
  propagation.
- Startup/recompile is observe-only. Replicate OFF preserves existing protection.
  A manual follower change is preserved and suppresses automatic re-entry until an
  explicit resync. Human NinjaTrader controls and Glitch Flatten All remain usable.
- Native account/order/position state and account contract ceilings fail closed.
  Copy closes cannot cross zero; ambiguous submissions are never blindly retried.
- Follower protection is registered before submit. A synchronous or asynchronous
  protection failure issues one native flatten and no retry.
- Replicate state reflects an effective engine with at least one connected route.
  Flatten All reports disconnected configured accounts as incomplete.
- Journal reconstruction ignores orphan exits, allocates reversal commission once,
  uses canonical instrument values, and labels Master/Group/Fleet scope.
- Analytics preserves contract identity and timeframe freshness. FRED calendar rows
  remain context and cannot create a false live news alert.
- Apex metadata carries the current 30% Legacy consistency value. The direction
  guard applies only to Glitch-generated Apex entries; automation eligibility is
  not an execution gate.

## Verification

- Shared source contracts: **32/32**.
- Five production web builds and five lint runs: pass.
- Python compilation, tracked PowerShell/JSON parsing, diff and secret checks: pass.
- The identical shared C# core is included in the deployed AI superset, which
  matched all 87 candidate files and compiled green in NinjaTrader.

## Honest release gate

The exact non-AI package has not replaced the currently deployed AI superset, so it
still needs one clean-target F5 compile and one bounded market-open Sim lifecycle
before promotion to `main` or distribution: entry, ratio followers, native brackets,
native close, all accounts flat/order-free, and Journal reconciliation.

See `docs/ledger/audits/2026-07-18-weekend-clean-candidate-audit.md`.
