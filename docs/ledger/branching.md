# Glitch — Branching & Release Doctrine

**Audience:** private maintainers and agents only.  
**Reconciled:** 2026-07-19

## Current branches

| Branch | Purpose | Public release authority |
|--------|---------|--------------------------|
| `main` | Shipped non-AI product and approved user patches | Yes |
| `cleanup/main-core` | Clean non-AI release candidate built from `main` | No; merge only after its own compile/runtime acceptance |
| `cleanup/ai-core` | Clean internal Glitch AI Sim/paper candidate using the same shared core | No; never publish as the non-AI download |

The former long-lived `glitch/ai-rail` branch is implementation history, not the
active source candidate. Historical logs may still name it. Do not edit the dirty
project checkout or relabel old runtime evidence as proof of either clean branch.
Read the checked-out candidate's own `docs/ledger/now.md` before changing code.

## Shared-core boundary

`cleanup/main-core` and `cleanup/ai-core` intentionally share producer-neutral
Glitch behavior: replication, follower-native protection, explicit resync,
Flatten All, Journal reconstruction, instrument metadata, Analytics, compliance,
localization, and native state truth.

AI-only code stays on `cleanup/ai-core` until deliberate promotion:

- `Services/Ai/*`, intent/telemetry endpoints, policy and packet stores;
- Glitch AI Auto, AI Trading Scope, and the Glitch AI Feed;
- Hermes profile/session/scheduler, prompts, skills, memories, and learning rail;
- model-oriented snapshots, packets, corpus writers, and recommendation transport.

Shared fixes move between candidates only as a reviewed minimal patch or
cherry-pick. Never merge a whole dirty rail merely to recover one shared fix.

## Required workflow

```text
identify exact candidate and commit
→ read source and candidate ledger
→ make the smallest source change
→ run candidate-specific source/tests/localization gates
→ verify native accounts are safe for deployment
→ deploy the complete AddOn folder once
→ verify source/target hashes
→ one NinjaTrader F5 compile
→ record evidence in the candidate ledger
→ commit the candidate
```

Public zips, checksums, manifests, and release notes are produced from `main` only
after the non-AI candidate is accepted and merged. AI promotion is a separate
release decision; paper/Sim evidence grants no PA/live or public-download authority.

## Agent discipline

- Confirm `git branch --show-current`, `git rev-parse HEAD`, and `git status` before editing.
- Workspace source is authority; never develop in NinjaTrader `bin/Custom`.
- Keep historical audits immutable. Update `now.md`, backlog state, and canonical
  contracts instead of rewriting old evidence.
- Codex builds/tests/deploys only when requested; it is not the trading runtime.

## References

- `docs/ledger/now.md`
- `docs/ledger/backlog.md`
- `docs/ledger/north-star.md`
- `docs/ai-program/operating-system-rail.md`
