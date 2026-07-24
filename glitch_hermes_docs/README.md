# Glitch + Hermes Trading Contract

**Audience:** private maintainers and agents. This is not public documentation or trading authority.

## Authority order

1. Current Glitch/NinjaTrader source and emitted runtime evidence.
2. `docs/14_intent_v3_reliability.md` and `schemas/intent.v3.schema.json`.
3. `docs/10_hermes_operator_contract.md`, `docs/11_snapshot_ingestion_learning_pipeline.md`, and `docs/12_hermes_trading_skills_and_knowledge.md`.
4. Installed public-profile SOUL, skills, plugin, and workers.
5. Older documents, wiki memory, archetypes, and playbooks as historical evidence only.

Documents `00` through `08`, `09_intent_contract_v2_brackets.md`, `99_original_extended_contract.md`, and `wiki_memory/` preserve provenance. They do not override v3, do not become runtime instructions, and must not revive fixed quantity, fixed-dollar, trade-count, cooldown, archetype, tighten-only, or centralized-VPS rules.

## Current shipped boundary

- AI AddOn v0.0.2.3 and public Hermes profile v0.0.2.11.
- Customer-installable local profile from `GlitchTrader/glitch-hermes-profile`; no required centralized recommendation service.
- Exactly two profile jobs: minute direct operator and 30-minute learning supervisor.
- Every LLM trading call uses an isolated session tagged `trading` with bounded continuity.
- `/trade` activates the existing Glitch-selected scope; `/trade_mode` is only a deprecated compatibility alias.
- Hermes decides thesis, direction, master quantity, geometry, timing, scaling, and management.
- Glitch validates factual executability, account survival, native protection, execution, replication, attribution, reconciliation, and journals.
- CopyEngine alone owns followers and user ratios. Codex is not in the runtime loop.

## Current behavior

- Intent v3 manages named stable Glitch legs independently.
- Stops may tighten or safely fall back while remaining on the protective side of live price; Apex capacity remains observational packet evidence rather than an amendment veto.
- One publisher creates gap-aware five-frame packets from paired complete minutes.
- Flat cadence is five elapsed minutes; positioned cadence is every new complete packet; recognized failures retry on the next packet.
- Atomic intent state and native reconciliation prevent blind duplicate entry after crashes.
- Closing the window hides a retained runtime rather than stopping its safety and exchange services.
- One learning supervisor batches outcomes, NOTHING, rejections, and forward evidence into hourly, 300-minute, and completed-session review.
- MARKET is the only current entry type. LIMIT is deferred until the full pending-order lifecycle exists.

The compact release handoff is `../docs/ledger/now.md`; current stop lines are in `../docs/ledger/backlog.md`.
