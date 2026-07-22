# Glitch Branching and Release Doctrine

**Effective:** 2026-07-22

## Maintained lanes

| Branch | Purpose | Public artifact |
|---|---|---|
| `main` | Production web apps, explicit release catalog, inspected artifacts | Standard `/latest`; AI `/latest/ai` |
| `standard/20` | Standard v0.0.2.0 source and no-AI maintenance | `Glitch_v0.0.2.0.zip` |
| `ai/22` | Experimental AI v0.0.2.2 source and follow-on reliability | `Glitch_AI_v0.0.2.2.zip` |

The old `cleanup/main-core`, `cleanup/ai-core`, and `glitch/ai-rail` names are historical. Do not recreate them.

## Release authority

- The release catalog records filename, edition, version, release date, status, source commit, and optional Hermes profile version.
- Unregistered ZIPs are ignored. Adding a file cannot silently change latest.
- `/latest` and `/api/releases/latest` default to Standard.
- `/latest/ai` and `/api/releases/latest?edition=ai` select Experimental AI.
- Exact version/slug downloads remain immutable and checksummed.
- Standard and AI artifacts are built, inspected, and exported independently. Neither edition is installed over the other.

## Change routing

- Standard behavior, public Docs, Download, Website, API, and catalog work start from `standard/20` or a clean `main` worktree as appropriate.
- AI execution, packets, Hermes contracts, learning, and AI-only UI start from `ai/22`.
- Shared C# fixes must be verified in both source lines before release. Never copy live NinjaTrader files back into source.
- The public Hermes profile is a separate repository with its own compatible version and tag.

## Coordination authority

- `docs/ledger/ledger.json` on `main` is the one work ledger for Standard, AI, web, and release work.
- `standard/20` and `ai/22` do not maintain branch-local queues, now files, or status ledgers.
- A branch agent reads the default-branch ledger and asks the Glitch PM to persist any durable lifecycle transition there.
- A push to `main` remains a publication action even when the diff is coordination-only.

## Promotion

No branch name grants runtime or live authority. Promotion requires exact-source tests, F5 compile, bounded native lifecycle evidence, inspected artifact, checksum, catalog record, and operator approval. A push to `main` deploys public web surfaces and is therefore a publication action.
