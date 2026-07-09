# Glitch AI Program — Architecture & Version Roadmap

**Audience:** private maintainers and agents only. Do not publish this file or copy its gates/versions into public docs until a release owner promotes a sanitized summary.

**Author:** Fable (architect) · 2026-07-08 · **Implementer:** Cursor (coder)
**Sources of truth:** this doc (roadmap) · `glitch_hermes_docs/` (AI contract) · `docs/ledger/backlog.md` (work items) · `docs/ledger/north-star.md` (invariants)

## Mission (operator dictation, 2026-07-08)

Improve v0.0.1.9 and prepare v0.0.2.0 onwards with AI progressively integrated into trading decision-making:

1. Ingest more assets; improve analytics, indicators, bridge, and normalized indicators on the Analytics panel.
2. Ingest data → mine patterns → backtest strategies → learn.
3. Eventually Hermes issues BUY / SELL / HOLD / DO-NOTHING recommendations on a 5-minute tape cadence.
4. Glitch receives each recommendation, runs deterministic compliance + risk checks, and places orders only if all checks pass.
5. **Every entry intent MUST carry SL and TP (TP1, optional TP2; SL, optional SL2). NT/Glitch own loss-stopping — never the AI.** Brackets are NT-held OCO orders so TP/SL execute even on connection loss.

## Prime invariant (unchanged)

```text
Hermes proposes. Glitch validates, executes, journals, and protects the account.
```

Glitch is the only component that can touch an order. Hermes has no order API, no account credentials, no NT access — only the read-only telemetry API and the intent endpoint.

---

## Version ladder

| Version | Codename | Contents | Gate to enter |
|---------|----------|----------|---------------|
| **v0.0.1.9** | Trust | RP-1/RP-3, F1 commission truth, SHA-256, Honest Copy, analytics scoring, scroll UX — **non-AI operator baseline** | **shipped 2026-07-09** |
| **v0.0.2.0** | Eyes | Instrument metadata registry + multi-asset bridge normalization (GL-025), normalized Analytics panel (GL-026) | v0.0.1.9 shipped |
| **v0.0.2.1** | Voice | `GlitchExternalTelemetryServer` — read-only localhost API + schemas (GL-027) | GL-034 security design review of the server spec |
| *(Hermes H-0)* | — | Hermes runtime scaffold + `ingest_snapshot` (GL-028), separate repo, mktintel-style | v0.0.2.1 running |
| *(Hermes H-1)* | — | Pattern mining + backtest harness over accumulated corpus + Glitch-Collab research data (GL-029) | exporter + corpus sufficient for ranked archetypes (no calendar gate) |
| **v0.0.2.2** | Ears | `POST /intent` paper mode: intent models v2, AI risk firewall, AI journal bridge (GL-030, GL-031) | Waves 1–2 complete (GL-002 LANE-1, GL-001, GL-004) + F1 landed |
| **v0.0.2.3** | Hands-sim | `GlitchAiOrderExecutor` on Sim101, bracket-mandatory (GL-032) | paper path clean: zero firewall bypasses, zero schema drift rejects |
| **v0.0.2.4** | Hands-eval | Eval allowlist, Eval Sprint profile, kill switch (GL-033) | GL-041 + sim/replay evidence + operator enable |
| v0.0.2.5–v0.0.2.9 | Filter → Shadow | M1–M2: confidence gating, lifecycle, fleet, shadow VPS | live eval rail advancing on evidence |
| **v0.0.3.0** | Learn | M3: self-heal + self-learn with promotion gates | R22–R23 acceptance |
| v0.0.3.x | — | M1–M3 detail (`glitch_hermes_docs/docs/05_milestones_m0_m3.md`) | each per its milestone gate |

**Canonical rail (first principles, R01–R23):** `operating-system-rail.md`  
**Branching:** `docs/ledger/branching.md` — `main` = v0.0.1.x user line; `glitch/ai-rail` = v0.0.2.x+ implementation

Doctrine check: north-star says no AI before audit + fixes. The ladder respects it — v0.0.2.0/2.1 and H-0/H-1 are read-only data work (operator phase-ladder step 2, explicitly pre-approved); nothing that can create an order ships before Waves 1–2 close.

**Instrument universe (Tradovate/Apex via NT):** `tradovate-apex-instrument-universe.md` — 148 symbols captured 2026-07-08 for GL-025 registry seed + bridge export/mining scope.

---

## Component map

```text
NinjaTrader 8 (Windows)
  GlitchAnalyticsBridge (indicator, per chart)
      └─ publishes normalized readings → GlitchAnalyticsFeedBus
  Glitch AddOn
      ├─ GlitchInstrumentMetadataService   NEW  v0.0.2.0  (point value, tick size, session template — from NT MasterInstrument; kills F2 fallback)
      ├─ GlitchExternalTelemetryServer     NEW  v0.0.2.1  (HttpListener, 127.0.0.1:8787, bearer token, GET-only)
      ├─ GlitchAiIntentServer              NEW  v0.0.2.2  (POST /intent — paper first)
      ├─ GlitchAiRiskFirewall              NEW  v0.0.2.2  (deterministic check chain, below)
      ├─ GlitchAiJournalBridge             NEW  v0.0.2.2  (intent → validation → orders → fills → round-trip, one correlated record)
      ├─ GlitchAiOrderExecutor             NEW  v0.0.2.3  (entry + NT-held OCO bracket, atomic; signal names GlitchAI*)
      └─ existing: ComplianceEngine · ReplicationEngine · TradeLedger · RiskLockLedger · ShellBridge (UNTOUCHED by AI path)

Hermes runtime — native cron first, no always-on daemon until proven necessary
      ├─ snapshot_sanity        (H-0)   script-only/no-LLM freshness + schema + stuck-handoff check
      ├─ suggest_trade          (H-2)   5-minute LLM cron → one intent per instrument per cycle or NOTHING
      ├─ portfolio_risk_review  (H-2)   hourly exposure/drawdown/concentration review
      ├─ learning_pass          (H-2)   6-hour candidate lesson/archetype review
      ├─ daily_learning         (H-2)   post-session trader journal from Glitch outcomes
      └─ deferred               Docker/API/daemon/queue only if cron fails a measured requirement
```

**AI order path is fully separate from replication** (`06_implementation_plan.md` Step 0 stands): new services only, no edits inside `GlitchReplicationEngine` / `GlitchComplianceEngine` beyond calling their existing public checks.

---

## Intent contract v2 — the bracket mandate

Full normative text: `glitch_hermes_docs/docs/09_intent_contract_v2_brackets.md` · schema: `glitch_hermes_docs/schemas/intent.v2.schema.json`.

Hermes-side operator doctrine: `glitch_hermes_docs/docs/10_hermes_operator_contract.md` defines the future 5-minute cron/operator input bundle, strict JSON output, learning boundary, and builder reading order. `glitch_hermes_docs/docs/11_snapshot_ingestion_learning_pipeline.md` defines the minute snapshot, historical exporter/replay corpus, hourly portfolio/risk review, 6-hour learning pass, and daily trader journal. Hermes is not the operator today; these are contracts for the later `addon-ai-bridge` implementation.

Summary of the operator's decisions (2026-07-08):

- **Cadence:** Hermes analyzes the tape every 5 minutes (candle close) and emits at most one intent per instrument per cycle.
- **Actions:** `ENTER_LONG` (BUY) · `ENTER_SHORT` (SELL) · `HOLD` (keep position, no change) · `EXIT` (close now) · `NOTHING` (flat, stay flat).
- **Every ENTER intent MUST include `stop_loss` and `take_profit_1`.** Optional `take_profit_2` (quantity split) and `stop_loss_2` (post-TP1 stop for the runner, must reduce risk — typically breakeven). Firewall rejects TP2/SL2 when quantity < 2. M0 is 1-contract, so M0 runs TP1/SL only; the contract supports the full shape from day one.
- **NT holds the bracket.** Glitch submits entry + OCO stop/target atomically; if the bracket cannot be attached, the entry is cancelled — a naked position must be impossible. On TP1 fill, Glitch (deterministically, not Hermes) moves the remainder's stop to SL2 if provided. Connection loss, Hermes crash, or Glitch crash leaves NT-managed protective orders working.
- **AI never manages a loss mid-flight.** No stop widening, ever. Risk-reducing actions (`EXIT`, tightening via future `ADJUST_STOP` in M1) are the only in-flight changes allowed.

## Deterministic firewall — check chain (executed in order, all journaled)

```text
 1. kill switch off?                      (operator master switch, Settings)
 2. AI feature enabled + licensed?
 3. instrument in allowlist?
 4. account in allowlist?
 5. schema-valid intent? (v2, prices tick-rounded)
 6. snapshot fresh? (intent.snapshot_hash matches a snapshot ≤ 90s old)
 7. intent_id unseen? (idempotency)
 8. cooldown elapsed since last AI order on this instrument?
 9. trades-today < daily cap?
10. risk per trade = |entry−SL| × pointValue × qty ≤ per-trade cap ($100 M0)
11. daily loss budget remaining ≥ this trade's risk ($300/day M0)
12. bracket sane? (SL on loss side, TP1 on profit side, SL2 tighter than SL, TP2 beyond TP1, split valid)
13. no position conflict? (no pyramiding, no averaging down, no opposite add)
14. session/news lockout clear?
15. existing ComplianceEngine pass (prop-firm rules) for the target account
```

Any failure → intent rejected, reason journaled, **no order exists**. The SL requirement is what makes check 10 possible: risk is knowable before any order is created.

---

## What this depends on from the current backlog

- **F1 (GL-024)** before Hermes learns from the journal: journal PnL is currently gross of commissions while the dashboard reads NT net. Learning from wrong PnL teaches wrong lessons. Hard gate for v0.0.2.2.
- **GL-002/LANE-1** replication audit (incl. RP-2 pipeline idempotency) before any AI execution shares a machine with live replication.
- **F2** point-value fallback is subsumed by GL-025's instrument metadata registry.
- **GL-019** copy-trading policy confirmations stay an operator action; unrelated to the AI path but same compliance muscle.

## Security posture (applies to every new surface)

- Telemetry + intent servers bind `127.0.0.1` only; bearer token required even on loopback (token generated on first run, stored in GlitchData, shown once in Settings). Remote deployment only via Tailscale/WireGuard — never a public bind.
- GL-034 security review is two-stage: design review before GL-027 ships; full audit (apps/api + download flow + AI servers) before GL-033 (live eval).
- Release zips get SHA-256 checksums from v0.0.1.9 (GL-022).
