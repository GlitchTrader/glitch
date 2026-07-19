# Glitch docs map

Keep this boring. Public docs explain what exists. Private docs coordinate what we are building.

## Public-safe docs

Publish only these unless a maintainer explicitly promotes another file:

```text
README.md
ninjatrader/Glitch/Docs/README.md
ninjatrader/Glitch/Docs/architecture.md
ninjatrader/Glitch/Docs/addon.md
ninjatrader/Glitch/Docs/indicator.md
ninjatrader/Glitch/Docs/data-flow-and-bridge.md
ninjatrader/Glitch/Docs/persistence.md
ninjatrader/Glitch/Docs/api-reference.md
apps/website/README.md, adapted with placeholders only
apps/api/README.md, adapted with env names only
```

Public docs must not reveal secrets, machine-local paths, proprietary formulas, security internals, unreleased roadmap, eval/live-trading gates, pricing experiments, affiliate economics, or internal operator notes.

## Private docs

These are for maintainers and agents:

```text
docs/ledger/                 active work log, backlog, audits, handoffs
docs/ai-program/             unreleased AI/Hermes roadmap and gates
glitch_hermes_docs/          private Glitch <-> Hermes contracts and agent memory
ninjatrader/Glitch/Docs/*commercial*
ninjatrader/Glitch/Docs/*funnel*
```

## Current code-grounded state

- The public product is the NinjaTrader AddOn plus `GlitchAnalyticsBridge`
  indicator. Internal Glitch AI/Hermes behavior is documented only in the private
  set until a release owner promotes it.
- Analytics move from chart indicator to AddOn feed bus, then into UI snapshots.
- The AddOn persists runtime state under `GlitchData/`; the full localization
  catalog is bundled and the runtime `Localization.tsv` is sparse overrides only.
- Producer-neutral copy/replication is event-driven through `GlitchCopyEngine`.
  It owns followers, ratios, follower-native OCO protection, and explicit resync
  for both manual and AI-produced master activity.
- The clean AI candidate is an internal Sim/paper validation rail. Hermes decides
  for configured masters; Glitch validates, executes, protects, replicates, and
  journals. Codex is not in the runtime loop.
- The customer product direction is one centralized supervised Glitch AI brain
  with a read-only client Feed, not per-client Hermes or Chat. The local profile
  exists to prove the packet/intent/outcome contract before transport changes.
- The AI data contract is snapshot-first: live snapshots, central ingestion, and
  historical replay/export use the same versioned feature vocabulary.
- Current source/compile/deploy evidence and unresolved runtime gates live in the
  checked-out candidate's `docs/ledger/now.md`, never in this routing page.

## Ponytail rule for docs

Delete duplication before adding pages. If a fact belongs in code-derived public docs, put it in `ninjatrader/Glitch/Docs/`. If it is roadmap, risk gates, or agent coordination, keep it private under `docs/ledger/`, `docs/ai-program/`, `glitch_hermes_docs/`, or ABKB.
