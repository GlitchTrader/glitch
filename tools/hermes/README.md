# Hermes / Glitch direct bridge

The canonical runtime is direct: Glitch publishes paired immutable one-minute frames, builds each packet from the five latest observed frames, and reports missing minutes as packet evidence. Hermes native cron wakes the persistent `glitch` profile for one decision, and Hermes posts the validated result to Glitch's existing localhost intent firewall. Codex is a builder and is not part of the trading runtime.

The role split and builder handoff are defined in `glitch_hermes_docs/docs/13_three_layer_handoff.md`. Hermes chat supervises Hermes trading through append-only supervisor records and may propose source work. Codex runs only when explicitly invoked for approved builder work; it never polls market data or submits intents.

The cognitive, model, skill, memory, ledger, and self-improvement canon is `glitch_hermes_docs/docs/12_hermes_trading_skills_and_knowledge.md`. The active runtime uses the fast operator plus one evidence-gated learning worker: completed-trade debrief, hourly supervision, 300-minute planning, daily native-memory upkeep, and one reversible cognitive overlay.

Glitch is the only writer below `GlitchData/hermes/exchange/glitch/`; Hermes is the only writer below `GlitchData/hermes/exchange/hermes/`. Stable packet, intent, route, account, and snapshot IDs join those physical streams into one logical ledger.

Installation and activation are separate:

```powershell
.\tools\hermes\install-direct-hermes-bridge.ps1
.\tools\hermes\enable-direct-hermes-cron.ps1
.\tools\hermes\enable-hermes-learning-cron.ps1
```

The installer changes only the host `glitch` profile to a local backend, pins the fast operator, clears silent fallbacks, enables native memory/session persistence, reconciles the source-controlled skills/plugin exactly, installs both workers, and preserves named sessions. It creates no cron job. The two enable scripts reconcile one native one-minute core worker and one native 15-minute learning launcher. The launcher exits immediately after starting a separately locked learning process, so a slow Sol review cannot occupy Hermes native cron's serialized lane or delay the next Luna decision. Every model call uses a fresh session tagged `trading`; durable packets, episodes, plans, guidance, native memory, and a versioned cognitive overlay provide continuity. Neither touches the Workframe dogfood Docker stack.

`run-direct-glitch-cycle.py` spends zero model calls until a new complete packet exists. It invokes Luna in an isolated `trading` session with current market/portfolio truth, master-only valid quantities, stable Glitch leg IDs and native working-order geometry, recent outcomes, the active plan/guidance/cognitive overlay, native memory, and a literal valid JSON template. Flat books use elapsed five-minute cadence; positioned books use every new packet. Any model, validation, delivery, firewall, or executor failure makes the next packet immediately eligible. A bounded rollover check prevents the cron from selecting the prior packet when publication is milliseconds behind the wake-up; the chosen immutable packet remains the decision identity and Glitch revalidates current execution truth. Transport-uncertain delivery reuses the same durable outbox and intent UUIDs. There is no in-process polling retry.

`launch-hermes-learning-cycle.py` is the fast cron boundary. `run-hermes-learning-cycle.py` is the detached, single-instance worker and invokes Sol only when evidence makes a loop due. It writes append-only master trade episodes, hourly observations/guidance, 300-minute plans, and daily journals under the Hermes supervisor exchange. Daily learning may update compact native memory and activate one versioned prompt/SOUL/skill cognitive overlay; later episodes must promote, revise, or roll it back. `learning-worker-status.json` and `learning-worker.log` expose the real worker result independently of the launcher. Neither process has intent, order, policy, group, ratio, or execution authority.

Start the human-facing session with:

```powershell
.\tools\hermes\start-glitch-chat.ps1
```

Deterministic slash commands are handled directly by the plugin, without an LLM turn. Hyphen and underscore spellings are both registered:

```text
/chat_mode       chat normally; leave the current trading-job state unchanged
/trade           turn the operator and learning loops ON for the Glitch-configured scope
/trade_mode      deprecated paper|live compatibility alias; delegates to /trade
/pause_trading   turn both scheduled loops OFF
/flatten_all     pause trading, then invoke Glitch's existing Flatten All workflow
/bias_long       suggest a long bias for the next cycle; Hermes retains final authority
/bias_short      suggest a short bias for the next cycle; Hermes retains final authority
/bias_neutral    remove directional bias for the next cycle
/long            require one protected long on the next flat configured cycle; Hermes chooses SL/TP
/short           require one protected short on the next flat configured cycle; Hermes chooses SL/TP
/replicate_on    request explicit replication-on state
/replicate_off   request explicit replication-off state
/glitch_status   show Glitch policy, job, gateway, and replication state
```

Commands use the existing bearer token and the localhost Glitch control endpoint on `127.0.0.1:8789`. Command IDs are idempotent. The Glitch header shows the product-facing `AI Auto` state; replication and flatten continue to use the existing Glitch UI/state paths. AI Auto is the only activation switch. Groups, accounts, ratios, limits, and account types come from Glitch packets and native state.

Bias commands write one expiring advisory to the Hermes-owned exchange. The direct trading worker consumes it on the next valid packet, records its identity beside the durable outbox batch, and marks it consumed only after producing that validated batch. Delivery retries reuse that exact batch and its intent IDs without another model call. Biases never bypass Glitch risk, bracket, account, or execution validation; they are not persistent memory and older directives cannot consume or affect later cycles.

`/long` and `/short` are stronger operator-directed experiments. They are accepted only when the configured Glitch scope is flat and order-free and trading and replication are on. They do not distinguish account types. When Glitch is already ON, they reconcile stale paused Hermes jobs so the Glitch ON/OFF control remains authoritative. Hermes must honor the requested direction and select structure-aware bracket geometry; Glitch still owns final risk validation, replication, execution, and native protection.

Entry decisions have a 300-second analytical window matching the canonical five-minute flat-book cycle and still require a separate live execution price no older than five seconds. Absolute structural levels must remain executable at that live price. `glitch.intent.v3` manages selected native legs by stable Glitch `leg_id`; selected stops may tighten or widen while remaining protective, and widening must fit fresh authoritative Apex liquidation capacity. v2 entries/no-ops/exits remain compatible; v2 `MOVE_STOP` remains global and v2 `MOVE_TP` is accepted only when one target remains. `EXIT`, `HOLD`, and `NOTHING` do not use entry-grade snapshot freshness: exits reduce an existing position, while hold/nothing are non-executing journal decisions.

## Clean trading epoch reset

`reset-hermes-trading-epoch.ps1` previews by default. It inventories the exact
Hermes/Glitch trading artifacts that would be archived and cleared, but makes no
changes until `-Apply` is supplied. Apply mode refuses to run while any Glitch
Hermes cron job is enabled.

```powershell
# Preview only
.\tools\hermes\reset-hermes-trading-epoch.ps1

# After /pause_trading and the manual Glitch/NT resets
.\tools\hermes\reset-hermes-trading-epoch.ps1 -Apply
```

The reset archives redacted session transcripts and file evidence, replaces only
the named `trading` session, deletes accidental one-shot trading/review sessions,
clears native `USER.md` memory content while preserving the memory subsystem, and
clears decisions, receipts, outcomes, directives, cron output, trading lessons,
observations/guidance, and the stale synced capsule journal. It preserves
`SOUL.md`, skills, plugins, configuration, the named `chat` session, approved
build-request/Codex evidence, Glitch policy, and account groups.

Glitch `Journal.tsv` and `TradeLedger.tsv` are intentionally not deleted by the
script. Use the existing Glitch **Reset Data** button so NinjaTrader's in-memory
journal and summary state are reset together. Reset each NinjaTrader Sim account
manually; filesystem cleanup is not an account-balance reset.

## Legacy development harnesses

The scripts below remain offline/debugging fixtures; they are not the production scheduler. **Glitch window must be open for runtime fixtures.**
Their historical one-contract/four-book/opportunity-gate assumptions are not active Glitch policy and do not define current correctness. The active direct worker, schema, executor, and focused tests use dynamic Glitch-supplied capacity and probabilistic decisions.

| Script | What it does |
|--------|--------------|
| `snapshot-sanity.ps1` | Reads `GlitchData/selfcheck/*.json`; exits 1 if degraded |
| `preflight-open.ps1` | Fail-closed feed, account, server, and policy readiness check |
| `invoke-hermes-cycle.ps1` | Current bundle → Hermes → strict validation; optional paper POST |
| `invoke-hermes-portfolio-cycle.ps1` | One shared snapshot/journal → one Hermes call → four independently validated book decisions; never submits |
| `sync-nt-journal.ps1` | Copies authoritative NinjaTrader/Glitch journals verbatim into the Hermes data capsule |
| `run-contract-scenarios.ps1` | Disposable LONG/SHORT/NOTHING/HOLD/EXIT model matrix |
| `run-paper-trading-cycle.ps1` | Guarded recurring Sim cycle: observe flat state, then one-shot submit validated entries |
| `run-hermes-portfolio-cycle.ps1` | Single-operator runner; `-SubmitSim` sequentially routes validated entries through the one-shot gate |
| `install-operator-profile.ps1` | Refresh the single `glitch` operator and four-book mandate; never enables cron |
| `gl045-prearm.ps1` | Automatic GL-045 validator/group/readiness matrix before operator F5 cases |
| `verify-nothing-idempotency.ps1` | Fresh-feed `NOTHING` first-accept/duplicate-reject proof; refuses armed execution |
| `suggest-trade.ps1` | Legacy GET market → POST `NOTHING` stub |

```powershell
.\tools\hermes\snapshot-sanity.ps1
.\tools\hermes\preflight-open.ps1 -Target paper
.\tools\hermes\invoke-hermes-cycle.ps1
.\tools\hermes\invoke-hermes-cycle.ps1 -PrepareOnly
.\tools\hermes\invoke-hermes-cycle.ps1 -PostPaper
.\tools\hermes\run-paper-trading-cycle.ps1
.\tools\hermes\invoke-hermes-portfolio-cycle.ps1 -PrepareOnly
.\tools\hermes\run-hermes-portfolio-cycle.ps1
.\tools\hermes\preflight-open.ps1 -Target paper -Profile glitch-conservative -MasterAccount Sim301
.\tools\hermes\install-operator-profile.ps1
.\tools\hermes\run-contract-scenarios.ps1 -Scenario stale_snapshot_nothing
.\tools\hermes\gl045-prearm.ps1 -Target paper
```

`invoke-hermes-cycle.ps1` never enables the executor. External Hermes receives only a privacy-redacted
flat-group MNQ entry-decision cycle; portfolio/account financials and private policy remain local, and
any open position or working order refuses inference. `-PostPaper` refuses to run unless policy
is `mode=paper` with `executor_enabled=false`. Sim arming and NinjaTrader deployment remain
separate operator actions after GL-042/GL-045 evidence.

The current design uses one persistent `glitch` Hermes profile and packet-defined execution groups. Legacy route IDs
(`glitch`, `glitch-aggressive`, `glitch-conservative`, `glitch-stay-revert`) remain compatibility labels in older harnesses;
they are not separate Hermes agents or fixed cognitive personalities. The legacy harness journal is `hermes-portfolio-cycles.jsonl`.
Before every inference, NinjaTrader's `received.jsonl`, `decisions.jsonl`, `executions.jsonl`,
`tradeledger.tsv`, `Journal.tsv`, and `hermes-cycles*.jsonl` are copied without transformation to
`Glitch-Hermes-Data\journal\nt`. The single operator may learn across books but must attribute positions,
decisions, and performance by route ID and master account. NinjaTrader/Glitch remains the journal source of truth.
Disposable model evidence: `tools\hermes\tests\out\` (ignored by Git).
