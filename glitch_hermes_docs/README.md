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

- **2026-07-13 — Direct control and sessions:** one `glitch` profile uses named `chat` and `trading` sessions. The core worker resumes only `trading`; deterministic slash commands call Glitch's authenticated control endpoint and are reflected in the Glitch UI. Native Kanban is deferred to learning/review and is never an execution rail.

- **2026-07-08 — Intent contract v2 (bracket mandate):** `docs/09_intent_contract_v2_brackets.md` + `schemas/intent.v2.schema.json`. Every entry intent carries mandatory absolute SL + TP1 prices with optional second/third protected legs. NT holds each OCO bracket; AI may tighten protection or exit but never widen a stop. v2 supersedes v1 (v1 was never implemented). Version ladder + firewall chain: `Glitch-Platform/docs/ai-program/roadmap.md`.
- **2026-07-09 — Hermes operator contract:** `docs/10_hermes_operator_contract.md` defines the future 5-minute Hermes cron/operator loop, required input bundle, strict JSON intent output, builder reading order, and missing bridge contracts. Runtime decision: Hermes native cron first; no always-on daemon unless cron fails a measured need. It does not make Hermes the operator today; it defines how the operator must operate later.
- **2026-07-09 — Snapshot/learning pipeline:** `docs/11_snapshot_ingestion_learning_pipeline.md` defines the minute snapshot shape, 5-minute operator loop, hourly portfolio/risk review, 6-hour learning pass, daily trader journal, multi-instrument support, and historical exporter/replay corpus using the same schema as live.
- **2026-07-13 — Cognitive operating map:** `docs/12_hermes_trading_skills_and_knowledge.md` is the canon for one persistent Hermes mind, native-capability preservation, dynamic Glitch groups, model routing, four separated cognitive loops, ledger ownership, memory layers, bounded self-heal, and staged activation. Archetypes and named routes are evidence/identity, never mandatory trading personalities or deterministic opportunity gates. Initial activation is interactive orientation plus the 5-minute paper core only; hourly, six-hour, and daily jobs remain deferred until core evidence is trustworthy.
