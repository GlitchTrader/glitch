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

- The live product is the NinjaTrader AddOn plus `GlitchAnalyticsBridge` indicator.
- Analytics move from chart indicator to AddOn feed bus, then into UI snapshots.
- The AddOn persists runtime state under `GlitchData/`; analytics cache now includes `AnalyticsBridgeCache.json`.
- Copy/replication is being simplified around an event-driven `GlitchCopyEngine` rather than a polling sync loop.
- Hermes/AI order authority is not live. The accepted invariant remains: Hermes proposes; Glitch validates, executes, journals, and protects.
- Hermes runtime starts as native cron jobs: script-only snapshot sanity, 5-minute `suggest_trade`, hourly portfolio/risk review, 6-hour learning pass, and daily trader journal. No always-on daemon until cron fails a measured need.
- The AI data contract is snapshot-first: live snapshots and historical replay/export must use the same schema so pattern mining can transfer into operator archetypes.

## Ponytail rule for docs

Delete duplication before adding pages. If a fact belongs in code-derived public docs, put it in `ninjatrader/Glitch/Docs/`. If it is roadmap, risk gates, or agent coordination, keep it private under `docs/ledger/`, `docs/ai-program/`, `glitch_hermes_docs/`, or ABKB.
