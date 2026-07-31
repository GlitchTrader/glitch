# Glitch Branching and Release Doctrine

**Effective:** 2026-07-22

| Branch | Purpose | Public artifact |
|---|---|---|
| `main` | Production web apps, explicit catalog, inspected artifacts | Standard `/latest`; AI `/latest/ai` |
| `standard/20` | Standard source and no-AI maintenance | `Glitch_v0.0.2.5.zip` |
| `fix/runtime-health-ai` | Experimental AI v0.0.2.6 source; promoted to `main` | `Glitch_AI_v0.0.2.6.zip` |

The old `cleanup/main-core`, `cleanup/ai-core`, and `glitch/ai-rail` names are historical.

## Coordination authority

- `docs/ledger/ledger.json` on `main` is the one work ledger for Standard, AI, web, and release work.
- This branch does not maintain a local queue, now file, or status ledger.
- Read current work with `git show origin/main:docs/ledger/ledger.json`; ask the Glitch PM to persist durable lifecycle changes on the default branch.

- `/latest` and `/api/releases/latest` default to Standard.
- `/latest/ai` and `?edition=ai` select Experimental AI.
- Unregistered ZIPs are ignored; exact artifacts remain immutable and checksummed.
- Shared C# changes are verified in both lanes. Never copy live NinjaTrader files back into source.
- The public Hermes profile is a separate repository and release.

The current temporary main-product promotion is AI v0.0.2.6 with Hermes profile v0.0.2.17, recorded at promotion commit `e35f93a`. Historical branch and release identities remain provenance only.

No branch name grants runtime authority. Promotion requires exact-source tests, F5 compile, bounded native lifecycle evidence, inspected artifact, checksum, catalog record, and explicit operator approval.
