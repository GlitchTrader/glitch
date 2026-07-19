# Glitch AI Program — Architecture & Version Roadmap

**Audience:** private maintainers and agents only. Do not publish this file or copy its gates/versions into public docs until a release owner promotes a sanitized summary.

**Originally drafted:** 2026-07-08 · **Reconciled to source:** 2026-07-19
**Current authority:** `docs/ledger/north-star.md` (invariants) ·
`docs/ledger/now.md` (deployed candidate) · `docs/ledger/backlog.md` (work items) ·
`glitch_hermes_docs/docs/09`–`13` (runtime contracts)

Historical milestone labels below remain planning provenance. Where an older
sentence conflicts with the current authority above, the current authority wins.

## Mission (operator dictation, 2026-07-08)

Improve v0.0.1.9 and prepare v0.0.2.0 onwards with AI progressively integrated into trading decision-making:

1. Ingest more assets; improve analytics, indicators, bridge, and normalized indicators on the Analytics panel.
2. Ingest data → mine patterns → backtest strategies → learn.
3. The local validation harness lets Hermes issue structured decisions on a
   five-minute flat-book cadence, with one-minute reconsideration while positioned.
4. Glitch receives each recommendation, runs deterministic compliance + risk checks, and places orders only if all checks pass.
5. **Every entry intent MUST carry SL and TP (TP1, optional TP2; SL, optional SL2). NT/Glitch own loss-stopping — never the AI.** Brackets are NT-held OCO orders so TP/SL execute even on connection loss.

## Prime invariant (unchanged)

```text
Hermes proposes. Glitch validates, executes, journals, and protects the account.
```

Glitch is the only component that can touch an order. Hermes has no account
credentials or NinjaTrader object access; it reads bounded Glitch packets and
returns a structured intent through the authenticated Glitch boundary.

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
**Branching:** `docs/ledger/branching.md` — `main` is public release authority;
`cleanup/main-core` and `cleanup/ai-core` are the active clean candidates. The
former `glitch/ai-rail` is historical implementation provenance.

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
      ├─ GlitchAiOrderExecutor             master-only execution + native OCO legs
      ├─ GlitchCopyEngine                  producer-neutral followers, ratios, follower OCOs, resync
      └─ ComplianceEngine · TradeLedger · RiskLockLedger · ShellBridge

Hermes runtime — supervised local validation harness; centralized VPS is the product target
      ├─ snapshot_sanity        (H-0)   script-only/no-LLM freshness + schema + stuck-handoff check
      ├─ suggest_trade          (H-2)   5-minute LLM cron → one intent per instrument per cycle or NOTHING
      ├─ portfolio_risk_review  (H-2)   hourly exposure/drawdown/concentration review
      ├─ learning_pass          (H-2)   6-hour candidate lesson/archetype review
      ├─ daily_learning         (H-2)   post-session trader journal from Glitch outcomes
      └─ deferred               Docker/API/daemon/queue only if cron fails a measured requirement
```

**AI and replication have one narrow integration boundary:** the AI executor may
submit and manage only a configured master. The producer-neutral CopyEngine used
by manual/non-AI Glitch observes that master and owns followers, ratios,
follower-native protection, closes, and explicit resync. AI never operates a
follower directly and AI policy never belongs inside the CopyEngine.

---

## Intent contract v2 — the bracket mandate

Full normative text: `glitch_hermes_docs/docs/09_intent_contract_v2_brackets.md` · schema: `glitch_hermes_docs/schemas/intent.v2.schema.json`.

Hermes-side operator doctrine: `glitch_hermes_docs/docs/10_hermes_operator_contract.md`
defines the active local validation contract and the target centralized transport.
`glitch_hermes_docs/docs/11_snapshot_ingestion_learning_pipeline.md` defines the
minute snapshot, five-frame packet, historical exporter/replay corpus, outcome
learning, and slower review loops. The local profile is Sim/paper-only and is not
the distributable customer runtime.

Summary of the operator's decisions (2026-07-08):

- **Cadence:** Hermes analyzes the tape every 5 minutes (candle close) and emits at most one intent per instrument per cycle.
- **Actions:** `ENTER_LONG` (BUY) · `ENTER_SHORT` (SELL) · `HOLD` (keep position, no change) · `EXIT` (close now) · `NOTHING` (flat, stay flat).
- **Every ENTER intent MUST include `stop_loss` and `take_profit_1`.** Up to three
  validated quantity/target legs create independent native OCO exits. Later
  same-direction entries may remain independently protected tranches. Invalid or
  over-capacity structures are rejected before submission.
- **NT holds the bracket.** After each entry leg fills, Glitch submits its native
  OCO stop/target pair. Partial entry fills and any protection failure enter one
  bounded cancel/flatten recovery path. A target fill cancels only its paired stop.
- **No stop widening.** Hermes may actively `EXIT` or `MOVE_STOP`; Glitch permits only risk-reducing stop changes.

## Deterministic firewall — check chain (executed in order, all journaled)

```text
 1. AI Auto ON and the configured master is in AI Trading Scope?
 2. schema/action/instrument/account contract valid?
 3. current packet and native portfolio state available and eligible?
 4. intent id and packet delivery idempotent?
 5. requested master quantity valid for every enabled group member after ratios,
    current account-wide exposure, and prop-firm contract ceilings?
 6. bracket structure tick-valid, on the correct sides, and complete for every leg?
 7. current session and enabled compliance controls permit submission?
 8. final native state still agrees immediately before submit?
```

Any failure → intent rejected, reason journaled, **no new order exists**. Trade
frequency, cooldown, setup, pyramiding/averaging posture, and risk/reward geometry
are Hermes judgments informed by current state; they are not hidden fixed-dollar,
trade-count, or archetype gates.

---

## Current release dependencies

- The clean source candidates and their current verification are recorded in
  `docs/ledger/now.md`; old branch evidence is not silently reused.
- One fresh market-open Sim lifecycle must prove master entry → ratio followers →
  native brackets → native close → all selected accounts flat/order-free →
  Journal/outcome reconciliation.
- GL-063 must provide authoritative holiday/special-close truth before unattended
  PA/live promotion. Profitability requires a frozen, reconciled paper sample;
  it is not inferred from source tests.

## Security posture (applies to every new surface)

- Telemetry + intent servers bind `127.0.0.1` only; bearer token required even on loopback (token generated on first run, stored in GlitchData, shown once in Settings). Remote deployment only via Tailscale/WireGuard — never a public bind.
- GL-034 security review is two-stage: design review before GL-027 ships; full audit (apps/api + download flow + AI servers) before GL-033 (live eval).
- Release zips get SHA-256 checksums from v0.0.1.9 (GL-022).
