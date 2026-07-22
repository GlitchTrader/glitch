# Glitch Branching and Release Doctrine

**Effective:** 2026-07-22

| Branch | Purpose | Public artifact |
|---|---|---|
| `main` | Production web apps, explicit catalog, inspected artifacts | Standard `/latest`; AI `/latest/ai` |
| `standard/20` | Standard v0.0.2.0 source and no-AI maintenance | `Glitch_v0.0.2.0.zip` |
| `ai/22` | Experimental AI v0.0.2.2 source and reliability follow-up | `Glitch_AI_v0.0.2.2.zip` |

The old `cleanup/main-core`, `cleanup/ai-core`, and `glitch/ai-rail` names are historical.

- `/latest` and `/api/releases/latest` default to Standard.
- `/latest/ai` and `?edition=ai` select Experimental AI.
- Unregistered ZIPs are ignored; exact artifacts remain immutable and checksummed.
- Shared C# changes are verified in both lanes. Never copy live NinjaTrader files back into source.
- The public Hermes profile is a separate repository and release.

No branch name grants runtime authority. Promotion requires exact-source tests, F5 compile, bounded native lifecycle evidence, inspected artifact, checksum, catalog record, and explicit operator approval.
