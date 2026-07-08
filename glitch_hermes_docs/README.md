# Glitch + Hermes Autonomous Trading Contract

This package contains two documentation styles:

- `docs/` — conventional architecture, runtime, risk, deployment, and implementation documents.
- `wiki_memory/` — LLM-wiki / Claude-memory / Hermes-style memory files intended for agent ingestion.

Scope: Glitch NinjaTrader AddOn + GlitchAnalyticsBridge + Hermes agent runtime.

Grounding: this plan is based on the uploaded Glitch codebase, especially:

- `GlitchAnalyticsBridge.cs`
- `GlitchAddOn/UI/Analytics/GlitchAnalyticsFeedBus.cs`
- `GlitchAddOn/UI/Analytics/GlitchAnalyticsLogic.cs`
- `GlitchAddOn/Services/Risk/GlitchComplianceEngine.cs`
- `GlitchAddOn/Services/Trading/GlitchReplicationEngine.cs`
- `GlitchAddOn/Services/Insights/GlitchTradeInsightsService.cs`
- `GlitchAddOn/Services/Insights/GlitchTradeLedgerService.cs`
- `GlitchAddOn/Services/Insights/GlitchRiskLockLedgerService.cs`
- `GlitchAddOn/Services/GlitchShellBridge.cs`
- strategy/exporter files under `GlitchStrats/Glitch/`

Core invariant:

```text
Hermes proposes. Glitch validates, executes, journals, and protects the account.
```

## Amendments

- **2026-07-08 — Intent contract v2 (bracket mandate):** `docs/09_intent_contract_v2_brackets.md` + `schemas/intent.v2.schema.json`. Every entry intent carries mandatory SL + TP1 (optional TP2/SL2); NT holds the OCO bracket; AI never manages a loss mid-flight. v2 supersedes v1 (v1 was never implemented). Version ladder + firewall chain: `Glitch-Platform/docs/ai-program/roadmap.md`.
