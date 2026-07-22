# Glitch Backlog

**Authority:** current source + `now.md` + explicit release catalog. Historical GL/R items remain in `log.md` and `audits/`.

## Active

| ID | Work | Status | Acceptance |
|---|---|---|---|
| GL-LOC-01 | Correct six public Standard reference docs | done | Every claim maps to Standard v0.0.2.0 source; no private formulas or stale services |
| GL-LOC-02 | Localize six reference pages and Docs shell | done | EN/PT/ES/ZH/FR/RU routes build; switcher preserves page; hreflang/sitemap complete |
| GL-LOC-03 | Localize Download | done | Six-language page/header/footer; release APIs and `/latest` contracts unchanged |
| GL-DOC-04 | Reconcile source ledger, rail, and ABKB | done | versions, branches, profile, cadence, authority, tests, and limitations agree |

## Experimental AI follow-up

| ID | Work | Status | Stop line |
|---|---|---|---|
| GL-AI-01 | Runtime-proof intent v3 per-leg TP/SL amendments and independent additions | todo | Bounded Sim; siblings unchanged; followers mirror correctly |
| GL-AI-02 | Runtime-proof safe widening and unsafe widening rejection | todo | Authoritative Apex state; unsafe request causes zero order mutation |
| GL-AI-03 | Hidden-window continuity and restart recovery | todo | Packets, servers, safety, idempotent intent state, and health remain truthful |
| GL-AI-04 | Learning continuity from NOTHING/rejections/outcomes | partial | Atomic deduped episodes reach hourly/300m/daily loops; infrastructure faults never become strategy memory |
| GL-AI-05 | Frozen performance sample | todo | Reconciled NinjaTrader/Glitch executions and attributable packet/decision/outcome data |

## External limitations

| ID | Limitation | Consequence |
|---|---|---|
| GL-063 | Authoritative holiday and special-close source is incomplete | Blocks unattended PA/live-readiness claim |
| GL-DEP-01 | Platform/provider dependency and restart recovery need wider evidence | Blocks unattended-operation claim |
| GL-PERF-01 | Profitability lacks a meaningful frozen sample | Blocks profitability claim |
| GL-LIMIT-01 | Pending limit-entry lifecycle is not implemented | Keep AI entries market-only until place/cancel/replace, partial-fill protection, expiry, replication, and restart recovery exist |

## Closed release state

- Standard v0.0.2.0 published on `/latest`.
- Experimental AI v0.0.2.2 published on `/latest/ai`.
- Hermes profile v0.0.2.4 published and updateable.
- Explicit catalog prevents filename-driven latest selection.
- Maintained branches renamed to `standard/20` and `ai/22`.

Never convert these items into a fixed strategy, quantity schedule, stop formula, trade quota, grid, or martingale rule.
