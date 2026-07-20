# Glitch + Hermes Autonomous Trading Contract

**Audience:** private maintainers and agents. This is not public documentation and not live trading authority.

Current authority order:

1. Glitch/NinjaTrader source and emitted runtime artifacts;
2. `docs/09_intent_contract_v2_brackets.md` through `docs/13_three_layer_handoff.md`;
3. the installed `hermes-profile` SOUL, skills, and direct worker.

`docs/00` through `docs/08`, `wiki_memory/`, and mined archetype/playbook files are historical design evidence. They are not installed into the Hermes profile and must not be used as runtime instructions. In particular, their retired M0 one-contract, fixed-dollar, trade-count, cooldown, and archetype rules are not current gates.

This package contains two documentation styles:

- `docs/` — conventional architecture, runtime, risk, deployment, and implementation documents.
- `wiki_memory/` — LLM-wiki / Claude-memory / Hermes-style memory files intended for agent ingestion.

Scope: Glitch NinjaTrader AddOn + GlitchAnalyticsBridge + Hermes agent runtime.

The active code-grounded validation state is summarized in
`../docs/ledger/now.md`. The local `glitch` profile is an internal Sim/paper
contract harness; the product target remains one centralized supervised brain
with client-side Glitch execution and a customer Feed rather than Chat.

Grounding: this plan is based on the current Glitch repo, especially:

- `GlitchAnalyticsBridge.cs`
- `GlitchAddOn/UI/Analytics/GlitchAnalyticsFeedBus.cs`
- `GlitchAddOn/UI/Analytics/GlitchAnalyticsLogic.cs`
- `GlitchAddOn/Services/Risk/GlitchComplianceEngine.cs`
- `GlitchAddOn/Services/Trading/GlitchCopyEngine.cs`
- `GlitchAddOn/Services/Trading/GlitchReplicationEngine.cs`
- `GlitchAddOn/Services/Insights/GlitchTradeInsightsService.cs`
- `GlitchAddOn/Services/Insights/GlitchTradeLedgerService.cs`
- `GlitchAddOn/Services/Insights/GlitchRiskLockLedgerService.cs`
- `GlitchAddOn/Services/GlitchShellBridge.cs`
- `GlitchAddOn/Services/Persistence/GlitchAnalyticsBridgeCacheStore.cs`

Core invariant:

```text
Hermes proposes. Glitch validates, executes, journals, and protects the account.
```

## Amendments

- **2026-07-13, amended 2026-07-20 — Direct control and sessions:** one `glitch` profile owns durable trading memory plus the named maintainer `chat` session. Every scheduled decision uses an isolated session tagged `trading` and receives bounded decision/outcome continuity; deterministic slash commands use `chat`, call Glitch's authenticated control endpoint, and are reflected in the Glitch UI. Native Kanban is deferred to learning/review and is never an execution rail.

- **2026-07-19 — Truthful Glitch AI surface:** AI Auto is the single effective
  ON/OFF control for the local core job and Glitch execution gate. AI Trading
  Scope selects existing group masters; AI operates only the master and the
  producer-neutral CopyEngine owns followers and ratios. The read-only Feed shows
  current snapshot collection, latest snapshot/decision ages, the latest five
  pipeline stages, and 20 expandable decisions with execution and packet evidence.
  All authored UI copy is localized across `en-US`, `pt-BR`, `es-ES`, `zh-CN`,
  `fr-FR`, and `ru-RU`; model-authored reasoning remains verbatim.

- **2026-07-08, amended 2026-07-20 — Intent contract v2 (bracket mandate):** `docs/09_intent_contract_v2_brackets.md` + `schemas/intent.v2.schema.json`. Every entry intent carries mandatory absolute SL + TP1 prices with optional second/third protected legs. NT holds each OCO bracket; AI may tighten stops, move every remaining target (optionally with a tighter stop), or exit, but never widen a stop. v2 supersedes v1 (v1 was never implemented). Version ladder + firewall chain: `Glitch-Platform/docs/ai-program/roadmap.md`.
- **2026-07-09, reconciled 2026-07-20 — Hermes operator contract:** `docs/10_hermes_operator_contract.md` defines the active local five-minute Sim/paper validation loop, required input bundle, strict JSON output template, next-newer-packet error retry, learning boundary, and target centralized transport. The worker runs under a supervised gateway and creates one isolated `trading`-tagged session per model call; Codex is not a scheduler or relay.
- **2026-07-09 — Snapshot/learning pipeline:** `docs/11_snapshot_ingestion_learning_pipeline.md` defines the minute snapshot shape, 5-minute operator loop, hourly portfolio/risk review, 6-hour learning pass, daily trader journal, multi-instrument support, and historical exporter/replay corpus using the same schema as live.
- **2026-07-13 — Cognitive operating map:** `docs/12_hermes_trading_skills_and_knowledge.md` is the canon for one persistent Hermes mind, native-capability preservation, dynamic Glitch groups, model routing, four separated cognitive loops, ledger ownership, memory layers, bounded self-heal, and staged activation. Archetypes and named routes are evidence/identity, never mandatory trading personalities or deterministic opportunity gates. Initial activation is interactive orientation plus the 5-minute paper core only; hourly, six-hour, and daily jobs remain deferred until core evidence is trustworthy.
