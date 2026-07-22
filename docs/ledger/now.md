# Now — Glitch v0.0.2.x

**Updated:** 2026-07-22

## Release truth

| Edition | Public version | Source | Channel |
|---|---:|---|---|
| Standard | 0.0.2.0 | `58a266ae5fce87f54ef390ef0431b2532f1f82ff` | `/latest` |
| Experimental AI | 0.0.2.2 | `2975b2e4070af118d7e752ca7566aa2353647ccf` | `/latest/ai` |
| Hermes profile | 0.0.2.4 | `GlitchTrader/glitch-hermes-profile` tag `v0.0.2.4` | `hermes profile update glitch` |

`main` and `standard/20` currently point to release commit `d71c647203220273562e85f08c13ae047c0127cf`. The maintained source lanes are `standard/20` and `ai/22`; former cleanup branch names are retired.

## Standard boundary

- Four tabs: Dashboard, Analytics, Journal, Settings.
- `GlitchAnalyticsBridge` publishes normalized 1m/5m/15m/60m context.
- One execution-driven CopyEngine owns master-to-follower quantity scaling, follower-native OCO protection, native close propagation, and explicit resync.
- Startup/recompile observe only. Replication off stops new copies and preserves existing native protection.
- Native account/order/position truth and contract limits fail closed. Flatten All remains a user control and reports incomplete cleanup.
- Authored NinjaTrader UI is six-language UTF-8.

## Experimental AI boundary

- Hermes receives complete Glitch packets, owns cognition and master decisions, and returns structured intents. It never directly operates followers.
- Glitch AI v0.0.2.2 adds intent v3 per-leg protection management, safe stop fallback within authoritative Apex capacity, independent tranche geometry, minute packet continuity, next-packet retries, crash-safe intent state, stale-lock recovery, hidden-window runtime continuity, truthful health, atomic reconciliation, and learning evidence for NOTHING/rejections.
- Flat cadence is elapsed five minutes; positioned cadence is every complete new packet. Recognized failures retry on the next available packet.
- The public profile creates exactly two jobs: a minute direct operator and a 15-minute learning supervisor. Setup preserves authentication, overrides, sessions, memories, ledgers, and enabled/paused cron state.
- The profile is customer-installable and updateable from GitHub. A centralized VPS is not the current distribution model.

## Verification at the shipped AI source

- Shared source contracts: **41**.
- AI/Hermes contracts: **142**.
- Combined source suite: **183**.
- NinjaTrader F5 compile: operator-reported green before export.
- Release catalog validates 14 explicit artifacts and keeps Standard as the default channel.

These facts do not establish profitability, unattended PA/live readiness, holiday/special-close completeness, or dependency recovery.

## Completed documentation pass

- The six Standard reference docs and the installation guide are current and available in all six product languages.
- Docs and Download preserve locale across their shells and cross-site links without changing `/latest`, `/latest/ai`, metadata API, or exact download contracts.
- Source ledgers, the AI rail, and ABKB agree on release, branch, profile, cadence, authority, test, and limitation facts as of 2026-07-22.

## Next product evidence

1. Keep Standard no-regression evidence tied to the exact artifact.
2. Run bounded AI lifecycle fixtures for per-leg amendments, safe/unsafe widening, hidden-window continuity, and final flat/order-free reconciliation.
3. Accumulate a frozen, attributable paper sample before changing cognition or making performance claims.
4. Resolve authoritative holiday/special-close truth and dependency/recovery limitations before unattended promotion.
