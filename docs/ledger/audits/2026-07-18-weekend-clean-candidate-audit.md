# Weekend clean-candidate audit — AI rail

**Date:** 2026-07-18

**Branch:** `cleanup/ai-core`
**Verdict:** ACCEPT for bounded Sim/paper acceptance; NOT approved for PA/live.

## Scope

This audit covers the current working candidate derived from the clean AI rail,
including the shared replication core, AI intent/firewall/executor path, direct
Hermes worker/profile, snapshots, temporal policy, Journal bridge, and operator UI.
The historical dirty checkout was not used as a merge source.

## Money-path invariants

- AI submits and manages only an allowlisted configured master.
- CopyEngine alone discovers followers, applies ratios, submits follower entries,
  installs follower-local OCO protection, mirrors master stop changes, and performs
  explicit resync.
- Every Glitch-generated entry requires native protection. Missing, rejected, or
  unconfirmable protection fails closed into one native flatten; ambiguous entries
  are never blindly retried.
- Account/order/position collections and contract ceilings must be readable at
  final submission. Unknown state is not inferred flat or safe.
- Replicate state is effective state, not button color. Startup does not catch up.
  Replicate OFF does not cancel existing protection.
- Human account controls remain effective. Manual follower divergence is preserved
  until explicit resync; Flatten All remains fleet-wide and reports unresolved
  configured accounts as incomplete.
- Apex same-direction checks apply to Glitch-generated Apex entries and never
  override human orders. Automation eligibility is not an execution rule.

## Final adversarial findings and disposition

| Finding | Disposition |
|---|---|
| AI daily close could leave followers open while callbacks were suppressed | Fixed by expanding selected masters to enabled followers and directly flattening each required connected account. |
| Failed native Positions/Orders reads could be serialized as an empty book | Fixed with `native_state_available` from capture through reader and zero-call worker eligibility. |
| Unused recovery/default-return APIs advertised behavior outside the active strict path | Removed. |
| FRED release rows generated a false live event banner on Saturday | Fixed by excluding FRED context rows from active lockout calculation; regression contract added. |

## Verification

- `tools/tests`: 32/32.
- `tools/hermes/tests`: 79/79.
- Five workspace production builds and five lint runs passed.
- Python, PowerShell, JSON, diff, and changed-file secret checks passed.
- `npm audit --omit=dev`: two moderate bundled-PostCSS findings remain; no safe
  stable Next update exists beyond installed 16.2.10.
- Safe full-folder deployment: 87 candidate files, zero candidate/live hash
  mismatches.
- NinjaTrader F5 compile: no populated error row.
- Runtime controls were not armed and no order was placed during this final audit.

## Residual release boundary

Static tests and a weekend compile cannot certify exchange behavior. One bounded
market-open Sim lifecycle remains mandatory before using this candidate for a new
paper epoch. Holiday/special-close authority also remains open and blocks unattended
PA/live AI. These are explicit release gates, not hidden runtime gates.
