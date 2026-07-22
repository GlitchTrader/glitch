# Glitch Ledger Log

Append-only operator log. Newest first.

## 2026-07-22 - AI ledger and rail reconciliation

- Reconciled the maintained `ai/22` handoff to Standard 0.0.2.0, Experimental AI 0.0.2.2, public profile 0.0.2.4, and the explicit release catalog.
- Replaced obsolete cleanup-branch, centralized-VPS, tighten-only, and old maturity-ladder claims with the shipped intent-v3, safe-fallback, gap-aware, crash-safe, hidden-runtime, and decision-episode contracts.
- No source code, profile, runtime, account, order, or artifact changed.

## 2026-07-21 - arbitrary entry-price veto removed

- Runtime evidence showed that Hermes remained healthy and attempted three longs
  after the last completed trade, but Glitch rejected all three with
  `group_entry_geometry_changed_reassess`. The executor branch introduced by
  `3f0639e` treated any higher live price for a long or lower live price for a
  short—even one tick—as invalid. That deterministic veto contradicted market
  order semantics and the Hermes/Glitch authority boundary.
- Removed the snapshot-versus-live directional comparison completely. Hermes's
  absolute stop/target intent now proceeds through normal market movement when
  every native bracket leg remains executable. At the final submission boundary,
  Glitch recomputes complete protected downside from the live price and rejects
  only missing/invalid protection or authoritative Apex liquidation-buffer breach.
  No slippage amount, reward/risk formula, or strategy threshold was substituted.
- The same investigation proved that the learning worker's model sessions held
  valid `glitch.hermes.learning_output.v1` JSON while the shared transport parser
  rejected it. Structured extraction is now explicitly scoped to the caller's
  expected schema, preserving ambiguity rejection without confusing learning
  envelopes with trading-intent envelopes. The existing single repair remains.
- Verification passes 41 shared and 128 AI/Hermes contracts (169 total), tracked
  Python, PowerShell, and JSON parsing, localization, secret, and diff integrity
  gates. The installed public profile `0.0.2.3` preserves auth, environment,
  memories, sessions, job IDs, schedules, and prior enabled state; all 26
  distribution hashes match.
  The complete 87-file AddOn deployed once with exact source/live parity. The
  compiled `Glitch_AI_v0.0.2.1` export identifies assembly version `0.0.2.1`,
  omits the removed veto code, and retains structural-price and Apex survival
  failures. AI Auto, replication, the supervised gateway, and both preserved jobs
  were restored ON after export.
- Public-origin verification exposed two Windows-specific Hermes distribution
  defects: Git checkout converted text bytes after the manifest was generated,
  and Hermes copied a read-only temporary `.git` pack into the installed profile.
  The profile now ships `* -text`, normalizes generated files before hashing, and
  removes only installer-owned Git metadata during setup. A real
  `hermes profile update glitch` from GitHub then completed at `0.0.2.3`, setup
  passed, `.git` was absent afterward, and AI Auto/jobs/gateway returned ON.

## 2026-07-21 - adaptive position building and staged learning candidate

- Preserved the authority boundary: Hermes owns thesis, master quantity,
  geometry, timing, scaling, and management; Glitch supplies factual capacity
  and independently enforces only the authoritative Apex Legacy account-survival
  boundary. No quantity schedule, risk percentage, stop distance, grid,
  martingale, partial-exit action, or strategy rule was added.
- Each master book now carries compact position-building truth: account/equity,
  liquidation buffer and drawdown headroom, current quantity/average price,
  account-wide contract capacity and valid quantities, complete Glitch-owned
  native protection, and MNQ point/tick value. Hermes compares a single tranche,
  native TP1/TP2/TP3 legs, reserved capacity, a later independently protected
  addition, and unchanged exposure. Favorable or adverse additions remain
  evidence-based choices, never price-triggered recovery behavior.
- Python and the C# firewall independently calculate actual per-leg stated-stop
  downside and add complete current protected exposure. Apex Legacy entries fail
  closed when rule identity, buffer, point value, or native coverage is ambiguous,
  and reject at or beyond the observed liquidation buffer with
  `apex_liquidation_buffer_exceeded`. The Sim packet producer now reuses Glitch's
  existing Apex trailing-threshold model and persisted peak state so simulated
  Apex accounts publish authoritative non-null survival state instead of being
  unusable under the fail-closed rail.
- Every debrief joins the completed entry by `cycle_id` to its immutable packet
  and reconstructs pre-entry capacity/protection, entry versus addition, all
  legs/stops, planned downside, normalized PnL/MAE/MFE, management, and actual
  exit. Learning JSON gets one exact-shape repair attempt; a second failure leaves
  evidence pending. Daily journals catch up completed Apex sessions exactly once.
  A cognitive change is first an inert proposal; only later independent comparable
  evidence with contradiction review can activate it, and later evidence can
  continue, promote, revise, or roll it back.
- Source verification passes 41 shared and 127 AI/Hermes tests (168 total), 30
  Python compilations, 34 PowerShell parses, 34 JSON parses, localization and
  changed-diff secret checks, and diff integrity. The public-profile candidate is
  staged as `0.0.2.1`; all 25 manifest hashes reproduce independently. A temporary
  native install/update preserved its config override, installed zero cron jobs,
  and matched all distribution hashes. The complete 87-file AddOn was deployed
  once with 87/87 hash parity and zero missing, extra, or mismatched files. F5
  rebuilt `NinjaTrader.Custom.dll` at `00:48:33Z`; the next live packet proved the
  new simulated Apex threshold and $6,217.50 buffer. The active Hermes profile is
  `0.0.2.1`; auth, environment, memory, and session digests are unchanged, its two
  existing jobs retained IDs/schedules/enabled state, and all 25 installed
  distribution hashes match. A bounded `NOTHING` returned HTTP 202 and
  `skipped/no_op_action` with all nine accounts flat and zero working orders
  before and after; AI Auto and both jobs were restored ON.

## 2026-07-21 - AI feed packet attribution corrected

- The latest-decision feed falsely showed `0/5` snapshots and `Packet Missing`
  because it guessed the source packet from the decision completion minute. A
  decision completed at `18:26Z` had actually consumed the sealed `18:25Z`
  packet; the fifth frame's `snapshot_hash` proved the exact relationship.
- The feed now joins each decision to the final snapshot hash in its source
  packet and includes packet arrivals in both history and render cache
  invalidation. No cadence, trading, execution, or Hermes behavior changed.
- Verification passed 38/38 shared and 113/113 AI/Hermes contracts. The complete
  87-file AddOn deployed with 87/87 hash parity and no missing, mismatched, or
  extra files; F5 rebuilt `NinjaTrader.Custom.dll` at `18:40:32Z` without an
  error surface. The live feed then showed decision `15:36:01` with `5/5` and
  packet `20260721T1835Z`. AI Auto was restored ON; the executor is armed in
  paper mode and all nine accounts are flat and order-free.

## 2026-07-21 - accountable management and execution geometry

- Trade-by-trade evidence isolated the giveback failure: Hermes was often directionally sound, but repeated `HOLD` after its own change condition arrived; an advisory plan had turned caution into a provisional one-contract baseline; malformed output and rejected amendments could remove a positioned review; and a delayed market entry could preserve stale absolute prices while making risk larger and reward smaller than Hermes assessed.
- Corrected the ownership boundaries without coding strategy. `HOLD` now carries an explicit burden of proof, prior change conditions are accountable, quantity stays adaptive under Hermes, and the 0.4%-2% daily objective is long-run expectancy/sizing feedback rather than a quota or forced per-trade rule. Hourly supervision may propose the existing single reversible cognitive overlay after two comparable episodes instead of waiting for the daily loop. Supervisor plan/guidance advanced to v2, so stale v1 one-contract instructions are excluded from both decision and learning continuity until Hermes regenerates compatible cognition.
- Invalid model content receives one exact-template regeneration for the same packet. Provider, contract, delivery, or executor errors wait for a fresh minute packet and make one bounded new decision attempt. Rejected execution is persisted as failed rather than being mistaken for a completed management action.
- Glitch now rejects an entry when live price has moved adversely from the assessed snapshot, because that would make the structural stop farther and target nearer. Equal or favorable geometry preserves Hermes's absolute stop/target levels. No point, ATR, ratio, strategy, or quantity formula was added.
- Validation/deployment: the complete Hermes/Glitch Python suite passes 112/112; Python compilation and diff checks pass. The canonical 87-file AddOn was deployed once with 87 matches, zero missing, zero mismatches, and zero extras; Alan confirmed NinjaTrader compile green. Installed Hermes profile, skills, plugin, and five worker scripts match source with zero hash mismatches. Live resolution proved the durable v1 plan and guidance are both excluded, and bounded post-install operator ticks completed through `2026-07-21T17:17:14Z`; the book was flat with zero working orders during reload.

## 2026-07-21 - learning removed from the trading critical path

- Reconstructed the recurring ten-minute decision gaps from the native cron
  execution ledger. The minute operator and the 15-minute Sol learner shared one
  serialized script lane; learning runs of three to seven minutes held later
  minute claims even though the gateway itself stayed healthy.
- Replaced the learning job body with a 145 ms launcher that starts one detached,
  lock-protected worker. The worker retains the same `glitch` profile, isolated
  `trading` session source, Sol/high cognition, durable memory, and zero execution
  authority. Its actual result is persisted separately in
  `learning-worker-status.json`; failures return nonzero and are visible in the
  worker log.
- Removed contradictory learning authority from debrief evidence. The master-only
  record now carries `master_learning_eligible`; follower divergence is a separate
  replication diagnostic and cannot veto cognition. Pending outcomes are processed
  newest-first in bounded groups of eight, preventing historical FIFO backfill from
  starving current feedback.
- Fixed the TradeLedger single-flight race at its source. A background writer now
  waits the remaining throttle interval, flushes current state, and requeues a dirty
  merge that arrived before the active writer released ownership. The missing
  Sim103 two-contract `e2cd4674` target round trip was reconstructed from the
  authoritative Journal and is now present in live `TradeLedger.tsv`.
- Verification passed 104 AI/Hermes and 38 shared contracts (142 total), Python
  compilation, 30 PowerShell parses, 34 JSON parses, secret scan, and diff
  integrity. Installed script hashes match source; exactly the direct operator and
  detached learning launcher are enabled. The complete 87-file AddOn matches live
  87/87 with no extras, and F5 rebuilt `NinjaTrader.Custom.dll` at 04:32:50 UTC
  without an error surface. A protected `1/1/2` Sim group retained identical
  positions and native working-order details across the compile.
- The first corrected scheduled learning boundary launched at 04:46:06 UTC and
  released native cron after 198 ms. The detached worker finished `ok` at
  04:52:26, debriefed the newest eight of 37 eligible outcomes, increased durable
  episodes from 24 to 32, and emitted a new hourly review/current guidance.
  Direct cron continued concurrently and completed a Luna decision at 04:48:14
  while the learning lock was active.

## 2026-07-21 - master-owned recursive learning activated

- Preserved the cognitive boundary: Hermes owns thesis, strategy, master
  quantity, management, reflection, and improvement. Glitch owns fact truth,
  policy, execution, protection, and replication. User-owned followers and
  ratios no longer reduce valid master quantities or veto a valid master entry;
  CopyEngine still rejects unsafe follower submissions locally and visibly.
- Added current native working-order details and bounded active-trade continuity
  so every positioned minute carries entry thesis, original geometry, management
  actions, current orders, excursion, and rollback. Master outcome attribution
  now survives a missing, divergent, or still-open follower; follower failures
  remain replication process diagnostics.
- Replaced the missing learning path with one no-agent 15-minute worker. It
  debriefs new master outcomes, supervises episodes hourly, plans after 300
  minutes with new reviews, journals daily, updates native memory only from
  repeated attributable evidence, and may test one paper-only versioned
  prompt/SOUL/skill overlay. Two recorded episodes are required to activate it;
  two distinct later episodes after activation or evaluation are required to
  continue, promote, or roll it back. No deterministic trade geometry was coded.
- Corrected two cadence/format defects before deployment. A bounded packet
  rollover check handles the publisher/cron millisecond race without delaying
  every call or discarding results that cross a minute. A complete decoded JSON
  object may shed redundant trailing closing delimiters—the exact observed Luna
  defect—before the unchanged semantic validator; prose or multiple objects fail
  and retry on the next packet.
- Verification passed 99 AI/Hermes and 37 shared contracts (136 total), Python
  compilation, 30 PowerShell parses, 34 tracked JSON parses, and diff integrity.
  The complete 87-file AddOn was deployed once with 87/87 SHA-256 parity and no
  extras. NinjaTrader F5 completed without an error surface, and the next live
  snapshot proved `working_order_details` active for all nine accounts.
- Installed SOUL/operator/scripts/13 skill files match source. Exactly two
  Hermes-native no-agent jobs are enabled: the minute direct operator and the
  non-executing 15-minute learner. The installed learner's no-model dry run
  found 14 eligible historical master outcomes for bounded backfill; its first
  post-create scheduled boundary is 00:45 local gateway time (03:45 UTC).

## 2026-07-20 - decision continuity and follower protection root causes fixed

- Reconstructed the stopped decision rail trade by trade. The gateway and cron
  kept running, but every scheduled prompt was appended to one unbounded named
  session. A timeout during Hermes compression left the active transcript over
  provider context limits, so every later cycle deterministically resumed the
  same poisoned history and failed.
- Scheduled decisions now open a fresh four-turn context for each complete packet
  and never resume a prior transcript. Native Hermes memory and the Glitch outcome
  ledger remain available; failed turns can no longer contaminate later cycles.
- The first fresh live cycle exposed one remaining generation-contract ambiguity:
  validation correctly required a string, but the prompt did not say that a
  numeric-looking `snapshot_hash` must stay quoted. The prompt now states that
  exact JSON type instead of weakening or coercing the fail-closed validator.
- Reconstructed the Sim102 protection rejection as a synchronous event-ordering
  race. CopyEngine orphan cleanup ran inside the protection `OrderUpdate` callback
  before NinjaTrader's position collection reflected the fill, cancelled the new
  protection, and triggered fail-closed flatten. CopyEngine cleanup now runs only
  on authoritative `PositionUpdate`; AI executor reconciliation keeps its existing
  broader callbacks.
- TradeLedger now treats a non-empty Glitch entry signal as the lifecycle identity
  and keeps its earliest terminal exit. Outcome reconciliation emits completed
  failed-protection groups as `process_error`, excludes them from trading memory,
  and no longer silently drops their realized PnL. Warning identity is scoped to
  the entry lifecycle, and flatten evidence now says `flatten_requested`.
- Verification passed: 37 shared contracts plus 81 AI/Hermes contracts (118 total),
  Python compilation, secret scan, and diff check. The complete 87-file AddOn was
  deployed once with 87/87 matching hashes and F5 rebuilt
  `NinjaTrader.Custom.dll` at 17:55 UTC with no compile-error surface. The worker,
  reconciler, and SOUL installed hashes match this candidate exactly. The final
  installed worker then completed scheduled cycle `20260720T1810Z` in a fresh
  context: `decision_ready`, quoted hash, strict validation, and Glitch HTTP 202;
  Hermes chose `NOTHING`, so execution correctly remained a no-op.

## 2026-07-19 - master protection callback recursion fixed and runtime-proved

- Reconstructed the failure from native NinjaTrader evidence. After one Sim101
  fill, `Account.CreateOrder` synchronously re-entered the AI account callback
  before the old protection flag was set, creating 2,341 duplicate stop orders
  and no target before NinjaTrader became unusable.
- Moved the per-account protection claim ahead of the first native order creation,
  retained pre-claim geometry validation, and added explicit rollback for create,
  reject, and submit-exception paths. The fix is in the master AI executor only;
  CopyEngine remains the sole owner of follower replication and protection.
- Also restored current follower contract-cap projection when the dashboard row
  has no populated raw cap. This removes the stale
  `follower_contract_cap_unavailable` rejection without adding an AI-specific cap.
- Full validation passed: 34 shared contracts plus 81 AI/Hermes contracts, Python
  compilation, diff check, NinjaTrader F5 compile, and 87/87 deployed file hashes.
- Bounded Sim intent `7bd326d8-c952-46b8-8604-a913cab6607b` submitted exactly one
  master entry/stop/target. CopyEngine opened Sim102 x1 and Sim103 x2 and created
  three follower OCO pairs. The common native target closed all accounts; final
  state is flat and order-free with +$8.00 / +$12.50 / +$24.50 realized PnL.
- AI Auto and both Hermes jobs remain off. No model call ran and Apex/live accounts
  were never in scope.

## 2026-07-19 - six-locale AI UI and documentation reconciliation

- Localized every authored Glitch AI control, status, stage, field, error, scope,
  feed, history, and supporting-snapshot label across `en-US`, `pt-BR`, `es-ES`,
  `zh-CN`, `fr-FR`, and `ru-RU`. Model-authored reason/bull/bear/change text,
  account names, market symbols, and machine codes remain verbatim by design.
- Closed 18 older fallback-only labels outside the AI tab, including ChartTrader,
  premium overlay, Settings, and risk-rule copy. Language changes now force the
  dynamic AI feed to re-render, and the localization audit also discovers `Lf(...)`
  format keys.
- Reconciled public NinjaTrader docs, private AI-program/ledger docs, Hermes
  operator docs, branch routing, and ABKB against current source truth. Retired
  one-contract, fixed-dollar, trade-count, cooldown, and archetype gates are no
  longer described as active behavior; the central-brain direction remains a
  future product target while the local Sim/paper harness is the current rail.
- Verification: 33/33 shared contracts and 81/81 AI/Hermes contracts (114 total);
  localization audit reports 329 catalog keys, 270 code-referenced keys, zero
  missing keys, and zero malformed or incomplete six-locale rows. UTF-8
  CJK/Cyrillic sentinels and `git diff --check` pass.
- Deployed the complete 87-file AddOn folder once from `cleanup/ai-core`. Source
  and live target match 87/87 with zero missing, mismatched, or extra files.
  NinjaTrader F5 rebuilt `NinjaTrader.Custom.dll` at 16:18 local with no populated
  compile-error surface. No trading, scheduler, account, or policy state changed.

## 2026-07-18 - weekend clean AI candidate freeze

- Finalized `cleanup/ai-core` as a bounded Sim/paper candidate. The full 87-file
  AddOn folder matches the deployed target byte-for-byte and NinjaTrader F5 compiles
  without a populated error row; AI remained unarmed and no order was placed.
- Final red-team fixes: selected-master daily close now expands and directly flattens
  enabled followers; native Positions/Orders capture failure is explicit and
  fail-closed through the zero-call worker; dead fail-open convenience/recovery APIs
  were removed; FRED dataset releases no longer become false live event alerts.
- Verification: 32/32 shared contracts, 79/79 AI/Hermes contracts, five production
  builds, five lint runs, Python/PowerShell/JSON/diff/secret checks.
- Candidate is accepted for one bounded market-open Sim lifecycle, not PA/live.
  Holiday/special-close authority and a fresh versioned paper sample remain open.
  See `audits/2026-07-18-weekend-clean-candidate-audit.md`.

## 2026-07-17 - partial-fill replication edge fixed and runtime-proved

- A bounded three-contract Sim fixture exposed a real defect that the one-contract acceptance could not reveal. Sim101 filled the entry in two executions (`2 + 1`), but CopyEngine capped follower sizing from the transient `-2` master position and opened only Sim102 `-4` and Sim103 `-6` instead of the configured `-6/-9`. Product Flatten All returned the fleet flat and order-free; Apex/live accounts were untouched.
- Root cause was callback timing plus execution identity. The execution callback could arrive before NinjaTrader's authoritative position reflected the cumulative order fill, while the generic `Id` fallback could identify the parent order rather than the individual execution. The copy context now carries cumulative `order.Filled`, waits until live master position covers that cumulative quantity, sizes followers from the greater cumulative truth, and deduplicates by `ExecutionId` or the existing execution-shaped fallback only.
- Added a shared source contract for cumulative partial-fill handling and removal of order-id dedup. The same producer-neutral correction is present in `cleanup/ai-core` and the undeployed `cleanup/main-core` candidate.
- Deployed the complete 85-file AI AddOn once from the clean worktree. NinjaTrader rebuilt `NinjaTrader.Custom.dll` successfully with no compile-error surface. Exact replay intent `c72bd011-b448-4f4f-b2f7-dfc89b01c3c1` produced Sim101 `-3` with 6 working bracket orders, Sim102 `-6` with 12, and Sim103 `-9` with 18—even though the follower entries also partially filled. Duplicate delivery returned HTTP 409.
- A second bounded fixture proved protected same-direction averaging: two independent one-contract master entries produced positions `-2/-4/-6` and independent working protection `4/8/12`. Product Flatten All then returned every Sim account flat and order-free. Final truth is paper, trading OFF, replication OFF; Hermes schedules remained paused and this pass used zero model calls.
- Still unproved by deliberate runtime fault injection: asynchronous follower-bracket rejection, disconnected/unresolved Flatten All, manual follower close plus explicit resync, and AddOn reload while a protected group is open. Those remain acceptance boundaries rather than inferred passes.

## 2026-07-17 - TradeLedger decoupled from Journal-tab visibility

- Found the remaining learning-path defect in runtime evidence rather than the UI: `Journal.tsv` contained the complete bounded master/follower lifecycle, but live `TradeLedger.tsv` was still header-only because reconstruction ran only from `RefreshSummaryInsightsIfNeeded`, which returned unless the Journal tab had been constructed and selected.
- Moved ledger upkeep onto existing lifecycle events without adding a timer or poller. Every flushed `Execution` journal batch now rebuilds/merges completed round trips, and AddOn startup rebuilds from the authoritative persisted Journal. Journal rendering remains a consumer, not the owner, of learning state.
- Added a shared architecture contract proving the execution-journal trigger and startup rebuild have no `_summaryAsOfText`/tab dependency. Verification is 62/62 AI/Hermes contracts plus 23/23 shared contracts; diff check is clean apart from existing line-ending warnings.
- Deployed the complete 85-file clean AddOn, verified 85/85 live hashes, and observed an automatic green compile at `2026-07-17T13:48:02Z`. A reflection harness loaded that exact compiled DLL and replayed the real `Journal.tsv`: it reconstructed 50 closed account trades and wrote 50 temporary ledger rows. The bounded fixture resolved exactly three rows: Sim101 x1 (`-83.5` MNQ points), Sim102 x2 (`-159.5`), and Sim103 x3 (`-238.25`). No order or model call was used for this proof.
- The canonical runtime then invoked the new rebuild and populated `TradeLedger.tsv` with 51 rows while all accounts remained flat/order-free and AI/replication remained OFF. The exact bounded fixture is present as Sim101 x1 `-83.5` points, Sim102 x2 `-159.5`, and Sim103 x3 `-238.25`.
- A second ownership mismatch surfaced in the file proof: the outcome reconciler required AI execution receipts for follower brackets, but producer-neutral CopyEngine correctly emits follower protection to `Journal.tsv`. The reconciler now joins master bracket receipt + exact CopyEngine `follower_protection|entry=...|result=submitted` rows + account-local TradeLedger rows + a later flat/order-free portfolio snapshot. No replication-to-AI coupling was added.
- Isolated reconciliation against the canonical files emitted one exact outcome for intent `e643d401-4d23-49fb-b8f7-658cd3507447`: quantities `1/2/3`, protection evidence `execution_receipt/copy_engine_journal/copy_engine_journal`, per-account PnL `-$167/-$319/-$476.50`, and group PnL `-$962.50`. Managed EXIT metadata now yields `managed_exit`; the old nearest-bracket-price heuristic no longer falsely labels a discretionary exit as a stop or target.
- Installed the clean profile overlay with `-SkipGatewayInstall`: source/installed reconciler hashes match, SOUL/skills/plugin/scripts are current, named sessions and native memory are preserved, and no gateway/scheduler/trading state was enabled. `hermes -p glitch cron list` reports no scheduled jobs. Canonical `hermes-trade-outcomes.jsonl` was intentionally not polluted with the Codex fixture; the outcome proof used an isolated output file and was deleted afterward.

## 2026-07-17 - clean AI candidate compiled and bounded 1:2:3 lifecycle proved

- Deployed the complete 85-file `cleanup/ai-core` AddOn candidate from the clean worktree and verified zero workspace/live hash mismatches. NinjaTrader F5 produced `NinjaTrader.Custom.dll` at `2026-07-17T13:21:46.8902968Z` with no compile-error surface.
- Restored the explicit Sim rule projection lost during consolidation: Sim identity remains `account_status=Sim`, while `prop_firm_id=ApexTraderFunding`, `rule_status=Eval`, `rules_are_simulated=true`, and the current 250K Legacy ceiling is `max_contracts=27`. Snapshot `is_replicating` now comes from the effective CopyEngine state rather than the UI flag alone.
- Restored a producer-neutral Apex same-direction guard before Glitch-generated AI/master and follower entry submissions. It bypasses Sim, never changes human positions/orders, and does not make automation eligibility an execution gate.
- Verification is 62/62 unique AI/Hermes contracts plus 22/22 shared architecture contracts. The full AddOn compile and live portfolio projection are runtime-proved; `cleanup/main-core` remains undeployed and unproved.
- Ran one bounded Sim-only fixture with AI inference and schedules inactive. Entry intent `e643d401-4d23-49fb-b8f7-658cd3507447` passed every firewall check, submitted one MNQ short on Sim101, and preserved absolute `SL=28760` / `TP=28460`. CopyEngine opened Sim102 x2 and Sim103 x3 and created one independent native OCO pair per follower unit. Portfolio truth was `-1/-2/-3` positions with `2/4/6` working orders.
- Managed EXIT intent `c81b04f8-d672-4ae8-a59a-d24a16301cff` closed the master, replicated follower flattening, canceled all twelve protective follower orders plus the master pair, and ended with Sim101/102/103 flat and order-free. Final control truth is paper mode, trading OFF, replication OFF; Apex/live accounts were never in scope.
- NinjaTrader labels follower `Account.Flatten` executions as `[SRC:Manual] [TAG:EXIT]` and CopyEngine currently journals `reason=master_manual_close`, even when the causal master execution is the accepted AI EXIT and `[SRC:Strategy]`. This is an observability-label caveat, not an ownership or execution failure; causality is reconstructible from intent, execution receipt, master fill, and follower copy lines.

## 2026-07-17 - Hermes epoch cognition and execution-contract correction (builder-only, later deployed in the bounded acceptance above)

- Preserved and audited the complete pre-refactor epoch before any reset. The evidence shows 117 decisions requesting one-minute review while flat, 135 API calls, no native tool calls or durable outcome learning, 32 entry decisions with structurally tight stops, and 18 completed master trades dominated by sub-two-minute stopouts. This epoch is diagnostic evidence, not profitability evidence.
- Corrected the cognition contract without turning Glitch into a deterministic strategy. Flat books are considered once per complete five-minute packet; positioned books may be reconsidered each minute. The model still owns direction, HOLD/EXIT/MOVE_STOP, structural absolute stop/target prices, valid dynamic quantity, and one-to-three protected legs. Frequency is no longer an objective and higher-timeframe live rows are regime context rather than closed-candle entry triggers.
- Removed legacy AI-only quantity and fixed-dollar risk gates. Capacity now derives from current Glitch portfolio facts, account-wide exposure, prop-firm maximum contracts, and live follower ratios. The executor preserves Hermes's absolute protection prices instead of silently re-anchoring them as distances.
- Made inference and delivery crash-safe: the runner pins `gpt-5.6-luna` through `openai-codex`, clears silent fallbacks during installation, bounds the named `trading` session to four turns, records one durable model attempt per packet, reuses the same outbox and intent id after transient delivery failure, and treats duplicate HTTP 409 as terminal delivery evidence.
- Fixed signed short exposure, account-wide capacity checks, three-leg validation/execution/recovery, directive identity, and outcome learning. `TradeLedger.pnl_points` was already quantity-weighted; reconciliation no longer multiplies it by quantity a second time.
- Reconciled profile SOUL, Glitch skills, installer/reset scripts, schemas, operator docs, authoritative rail, and the epoch audit. Historical archetypes and M0 dollar limits remain available only as non-authoritative research and cannot become runtime gates.
- Removed duplicate test discovery that inflated the reported suite. Verification is intentionally separated into 62 unique AI/Hermes contracts and 20 shared non-AI architecture contracts, plus PowerShell/JSON parse and diff cleanliness. NinjaTrader F5 compilation and bounded Sim lifecycle fixtures remain mandatory runtime proof; no deployment, reset, scheduler change, model call, account mutation, intent, or order occurred in this pass.

## 2026-07-17 - main/AI shared-core consolidation (builder-only, not deployed)

- Built from clean worktrees at `main=d216015` and `glitch/ai-rail=f82e1f5`; the historical dirty AI checkout was not edited or treated as a merge source.
- Replaced AI-coupled follower logic with one producer-neutral replication/protection core shared by both branches. AI submits and manages only the configured group master; replication independently owns follower discovery, ratios, entries, native OCO protection, explicit resync, and terminal close handling.
- Removed the legacy replication switch, broad `GLT-*` ownership, blind submission retries, Replicate-OFF order cancellation, startup catch-up, duplicate flatten submission, hidden follower quarantine, and follower mutation from the AI executor.
- Fixed route self-copy, cross-zero close/reconcile, truthful Replicate state, unresolved-account Flatten All truth, follower protection commit ordering, Journal orphan exits/reversal commission, canonical point-value handling, Journal scope labels, and rich Analytics identity/freshness fields.
- Closed three final source defects found during bounded review: asynchronously rejected follower protection now fails closed once without retry; multi-leg stop mirroring uses each native source OCO rather than the whole trade correlation; live contract registration replaces stale negative metadata cache entries.
- Removed four obsolete test modules that asserted the retired one-shot/four-book/deterministic opportunity-gate architecture. Active tests now describe the direct persistent-session rail only.
- Corrected Apex Legacy consistency metadata to 30% and retained the explicit same-direction check. No automation-eligibility execution gate exists in metadata, executor policy, or active tests; historical policy research is non-authoritative.
- Verification at this stage is source-only: 20 focused shared architecture checks and 70 active AI/Hermes checks pass, including a regression check that automation eligibility cannot become an execution gate. NinjaTrader F5 compilation and bounded Sim lifecycle evidence remain mandatory before deployment or merge.

## 2026-07-17 - post-audit Sim acceptance and clean Hermes epoch (builder-only)

- Reconciled cognition and execution sizing to one dynamic capacity truth. Sim portfolio snapshots now use Glitch's existing Apex Legacy Eval rules for their configured account size while remaining explicitly marked simulated; the direct packet publishes each account's ceiling, current MNQ exposure, ratio, valid master quantities, and maximum-exposure account. Removed the separate AI-only contract cap from Hermes input and changed firewall/executor checks to current rule-derived account ceilings.
- Updated Glitch identity and skills to own profitability and regime adaptation, treat 25k/250k dollar ranges as aspirational rather than quotas, use objective multi-timeframe numeric features without UI Buy/Sell labels, and size only from Glitch-supplied valid quantities. Snapshot parity already includes 1m/5m/15m/60m OHLCV, ATR, ADX/DI, RSI, Stochastic, CCI, z-score, MACD, EMA alignment, normalized directional/tradeability/composite scores, and optional order flow; stale or unproven news sentiment remains excluded.
- Verification: 79/79 current direct-rail tests passed; the complete 84-file AddOn deployed while flat and F5 compiled without an error surface. Live Sim101/102/103 snapshots now report Apex Legacy Eval context and max contracts 27. The reviewed profile and runner hashes match their installed copies; exactly one native `glitch-direct-operator` job is enabled. The Hermes epoch reset archived 167 artifacts, replaced only the named trading session, preserved chat/SOUL/skills/plugins/groups/policy/Journal/TradeLedger, and rearmed paper AI plus replication.
- Completed the bounded post-audit acceptance against the compiled AddOn. A three-contract master entry created three independent native SL/TP legs on Sim101 and matching three-leg protection on Sim102/Sim103; native TP/SL transitions closed every leg and all three accounts returned flat/order-free.
- Repeated the lifecycle after the operator changed live Glitch ratios to 1:2:3. One Sim101 contract produced exactly two protected contracts on Sim102 and three on Sim103. A managed master exit replicated cleanly and the final portfolio again proved all three accounts flat with zero working orders. Apex/live accounts were never in scope.
- Corrected master-only execution evidence (`master_entry_*`, `master_structural_brackets_submitted`, `master_trade_closed`, `master_exit_submitted`) so it no longer claims follower/group terminality before CopyEngine evidence exists. An EXIT received after the live master is already flat now returns the idempotent `exit_already_flat` no-op and cannot call `Account.Flatten` or create an order.
- Added deterministic `/reset_trading` and `/reset_memory` commands without overriding Hermes's native conversation `/reset`. The command turns trading off, pauses the job, refuses cleanup while any Sim exposure exists, archives the prior epoch, replaces only the named trading session, clears trading memory/decisions/receipts, and preserves chat, SOUL, skills, plugins, configuration, Glitch groups/policy, Journal, TradeLedger, and NT accounts.
- Verification: 76/76 current direct-rail tests passed; PowerShell parsers and diff check passed; the full 84-file AddOn deployed once while flat; F5 compile produced a new `NinjaTrader.Custom.dll` containing `exit_already_flat` and `master_exit_submitted`; runtime flat-EXIT idempotency returned HTTP 202 with `executor_code=exit_already_flat` and no state change.
- Applied the clean Hermes epoch at `2026-07-17T04:13:17Z`; archived 2,333 artifacts, replaced trading session `8b79506c-442d-469d-8e03-a87702ffd06c` with `9df1045e-e498-4b83-83ee-88e0a01e1a4d`, preserved chat session `88c37271-ecc0-4da1-a8f8-0954c3e9d029`, and left Glitch AI plus both Hermes jobs OFF pending the operator's Glitch Journal and NT Sim-account resets.

## 2026-07-16 - test-suite truth and debt cleanup (builder-only)

- Audited the 144-test claim against the active unified-profile/direct-cycle architecture. Removed 100 obsolete test definitions: the retired one-contract validator, four-profile batch validator, deterministic opportunity gate, undiscovered pytest normalizer, and the 77-test accumulated source-string suite. Git history remains the forensic record; production helper scripts were not removed.
- Replaced the historical source-string accumulation with 19 explicit current-architecture guardrails covering direct-runtime ownership, dynamic quantity and three-leg brackets, signed short positions, pre-submit lifecycle registration, unique execution identity, pending-copy TTL, observe-only startup, human quarantine, Flatten All authority, Apex direction checks, catch-up, transition-aware reconciliation, orphan cancellation, and Journal commission allocation.
- Added one canonical test command. It reports Python success separately from the mandatory NinjaTrader F5 compile and bounded Sim lifecycle acceptance, preventing Python checks from being presented as engine/runtime proof.
- Verification: current suite 64/64 passed; PowerShell test-runner parse passed; `git diff --check` passed apart from existing line-ending notices. No deployment, NinjaTrader compile, trading cycle, account/policy mutation, or order occurred.

## 2026-07-15 - four-day AI-rail session closeout and handoff (builder-only)

- Read the complete Codex transcript for thread `019f5786-3f54-7ab1-b809-1407b9e53136`: 230 user messages across 2026-07-12 through 2026-07-15. Reconciled repeated corrections around role ownership, over-gating, replication/brackets, session learning, observability, and performance into `docs/ledger/now.md` rather than creating a parallel lessons document.
- Reconciled the rail to committed source baseline `d7975fb`. Marked local Feed, gateway/session, outcome-learning, portfolio-truth, Journal, and two-leg slices as implemented where source exists while leaving their explicit runtime proof open. Removed the stale active pointer to the old GL-042/043 arm ritual.
- Added only two uncovered work items to the authoritative backlog: GL-063 for shared temporal/prop-rule compliance truth (news, maintenance, weekends/holidays, must-flat windows, and current official rule evidence), and GL-064 for versioned paper performance/regime calibration without turning findings into a deterministic strategy.
- Reviewed the mainline handoff against exact refs: `main`/`origin/main=d216015`, AI source baseline `d7975fb`. GL-055 remains first; the handoff now records committed Journal/reversal fixes and the operator's green F5/reset state, while preserving the explicit AI non-backport boundary.
- Preserved the three-layer authority document as an authority boundary and pointed continuation to the one backlog plus compact `now.md` handoff. No new queue, scheduler, runtime ledger, or architecture layer was created.
- Verification: 118/118 Hermes/source-contract tests pass. Documentation consistency and Git diff checks are recorded in the closeout commit. Local `__pycache__` and `tmp/session-0655.jsonl` artifacts remain untracked. No deployment, restart, model call, market poll, policy/account mutation, intent, or order occurred.

## 2026-07-15 - two-leg native scale-out and consistency pass (builder-only)

- Completed the existing `glitch.intent.v2` TP2 contract through the active direct runner, firewall, master executor, and follower copy engine. A quantity split now creates two distinct native OCO stop/target pairs; follower leg quantities scale from the Glitch-configured ratio and non-integral splits reject before the master entry.
- Preserved same-direction averaging as repeated independently protected entries rather than adding a deterministic averaging strategy or action. Partial master/follower entry fills fail closed into cancel/flatten recovery instead of remaining unprotected.
- Removed stale two-working-order assumptions from managed-exit/legacy portfolio checks, aligned the canonical intent and roadmap language with initial per-leg protection plus later `MOVE_STOP`, and tightened follower recovery risk to the per-contract policy cap.
- Corrected Journal reversal accounting: a single execution crossing through flat now allocates commission proportionally between the closing lifecycle and the newly opened remainder instead of charging the full commission twice. Added the requirement to mainline P0 `GL-055`.
- Critical-path source checks cover snapshot analytics/freshness, instrument metadata, portfolio truth, journal sync/orphan-exit guards, intent normalization/schema, execution recovery, replication, follower brackets, and UI feed contracts. Python/schema/PowerShell parsing passed; 117/117 automated tests passed. NinjaTrader F5 compile and one 3-contract two-target Sim lifecycle remain operator acceptance. No deployment, order, cycle, account, policy, or scheduler state was changed.
- Updated GL-053 and the existing mainline handoff. The AI executor itself remains an explicit non-backport; GL-060 now requires runtime-neutral multi-leg follower protection semantics on `main`, while GL-055 remains the first P0 journal backport.

## 2026-07-15 - native follower close and truthful fleet flatten (builder-only)

- Closed a follow-on protected-entry regression found in the next paper trade. The master received and filled its native SL, but Sim102/Sim103 had been copied into long positions without follower SL/TP orders. `TryGetReplicationProtection` had searched transient groups using `Account` object reference identity; the copy engine then treated a missing plan as optional and submitted naked AI follower entries. Protection lookup is now direct by the already-registered AI entry signal and validates stable account name/instrument identity. An AI follower entry is never submitted if that plan is unavailable.
- Restored group-exit semantics for native master SL/TP fills. Master `GLT-AI-S` and `GLT-AI-T` executions are no longer filtered as replication-internal; they flow through the same complete-exit path and invoke NT native follower flatten. Account-local follower brackets remain independent fail-safes, while a filled master exit remains authoritative for group synchronization. Follower-owned `GLT-COPY`/`GLT-CATCHUP` events are still filtered to prevent loops.
- Focused source-safety suite: 43/43 passing. The complete 81-file AddOn was deployed once; compile and one protected entry/master-SL fixture remain operator acceptance. Codex placed no orders and ran no Hermes cycle.
- Reconstructed the orphan-bracket loss from NT evidence: copied master exits first flattened Sim102/Sim103, then stale follower targets filled against flat accounts and opened unprotected reverse shorts; a later disconnect caused Flatten All to omit those configured accounts and falsely report the fleet flat.
- Removed Glitch's duplicate close-order/bracket-cancel state machine for complete follower exits. Full copied and catch-up exits now delegate to NinjaTrader AddOn `Account.Flatten(account/instrument)`, whose native close sequence cancels working orders, waits for cancellation confirmation, then closes the remaining position. Partial copied exits are refused with a critical warning until bracket resizing is explicitly supported; the existing protection remains in place.
- Flatten All now builds an intended scope from configured group names plus currently available accounts, journals unresolved/disconnected configured accounts, performs best-effort flattening on resolvable accounts, and reports success only when every intended account is positively resolved, flat, and order-free. Missing accounts are never inferred flat.
- Validation: all 88 Hermes/source-contract tests pass; diff check is clean. The complete 81-file AddOn was deployed once through the approved helper and workspace/live SHA-256 comparison reports zero mismatches. NinjaTrader compile and one protected entry/full-exit fixture remain operator acceptance; no order, cycle, account, policy, or scheduler state was changed by Codex.

## 2026-07-14 - centralized-brain decision and stabilization pass (builder-only)

- Recorded the product topology: one supervised Hermes brain on VPS, one canonical recommendation per five-minute window, authenticated Glitch client polling, local Glitch execution/management/replication/brackets/journal, and Feed without Chat. The local Hermes filesystem bridge is now explicitly an internal validation harness.
- Added ordered Spec Kit tickets GL-047 through GL-054. Expansion remains blocked on gateway/session continuity, authoritative outcome learning, portfolio truth, follower exit/bracket terminality, and stable/profitable single-instrument paper evidence. No automatic Codex build or trading loop is authorized.
- Fixed the outcome-learning design to join decisions and group lifecycle evidence to authoritative per-account `TradeLedger.tsv` round trips. A terminal flat/order-free group snapshot is required before learning. Real read-only reconstruction produced the two valid completed outcomes: -$72 and +$195 group PnL.
- Fixed portfolio capture so top-level signed position, unrealized PnL, and total PnL derive from the same live nested position records.
- Root-caused the third-trade drift: the copied master EXIT flattened Sim102/Sim103, but follower targets remained working because position state lagged the order callback; those targets later filled and opened reverse shorts. Source now marks copied/catch-up close orders before submission and cancels follower protection on the tracked close fill without relying on that lagging position callback.
- Gateway installation now uses Hermes-native hidden Windows supervision and cron enablement refuses an unsupervised/stopped gateway. Validation: 87 Hermes tests pass; Python and PowerShell parse checks pass. Deployment was correctly blocked because Sim102 and Sim103 are currently short one MNQ each with no working protection while Sim101 is flat. No order, flatten, cycle, restart, or deployment was performed.

## 2026-07-13 - Hermes cognitive operating map documented (builder-only)

- Established one persistent `glitch` Hermes mind with four separated cognitive jobs: 5-minute core decisions on `gpt-5.6-luna`/medium, hourly portfolio supervision on `gpt-5.6-sol`/high, six-hour portfolio planning on Sol/high, and daily learning/tomorrow planning on Sol/high. Model/provider/effort are recorded defaults with no silent core-loop downgrade.
- Preserved Hermes native skills, memory, sessions, planning, and upkeep. Glitch-specific skills are overlays. Documented the additional ledger, prop-rules, portfolio, planning, knowledge-upkeep, and self-heal skills required before supervisory jobs may activate; they were not implemented or installed in this pass.
- Removed the conceptual four-personality constraint: groups and ratios are dynamic Glitch packet state, route names are execution identities/legacy compatibility labels, and patterns/archetypes are advisory evidence rather than deterministic gates. `no_archetype_match` alone is never a reason for `NOTHING`.
- Defined single-writer ledger ownership, six memory layers, bounded self-heal tiers, per-loop inputs/outputs, and an evidence/rollback rule for future optimizations.
- Staged activation is explicit: interactive orientation, then the 5-minute paper core only; hourly, six-hour, and daily jobs remain deferred until core packet-to-outcome evidence is trustworthy. No profile install, cron activation, model call, deployment, policy/account mutation, intent submission, or order occurred.
- Added a static regression contract and aligned operator, ingestion, profile, and tool documentation. Verification: 63 Hermes Python/source-contract tests pass.

## 2026-07-13 - direct Glitch/Hermes bridge implemented (builder-only)

- Removed Codex from the designed runtime path. Glitch now owns five consecutive minute-frame publication and x0/x5 decision packets under `GlitchData/hermes/exchange/glitch`; Hermes owns outbox, delivery receipts, and cycle events under the sibling `hermes` branch. Physical streams have one writer each and join through stable IDs.
- Added a Hermes-native worker and separate install/enable scripts. The worker makes zero model calls without a new complete packet, resumes the isolated `glitch` profile session, reads bounded authoritative Glitch journal tails, applies contract/scope checks only, and delivers through the existing authenticated Glitch intent firewall. It contains no directional opportunity gate or deterministic trading strategy.
- Group count, route/master bindings, followers, and ratios come from the packet's live Glitch policy and `AccountGroups.tsv`; the batch contract supports the configured group count instead of hardcoding four books. The profile SOUL no longer hardcodes aggressive/conservative archetypes.
- Workframe dogfood isolation is explicit: only the host `glitch` profile is configured by the installer; no Workframe container, volume, network, port, or profile is touched.
- Verification: 60 offline Python/source-contract tests pass; the direct worker compiles; both PowerShell installers parse; scoped diff check passes. No profile installation, cron activation, NinjaTrader deployment, model call, policy/account mutation, intent submission, or order occurred.

## 2026-07-13 — corrected to one Hermes operator with four strategy books

- Replaced the briefly activated four-profile league with one persistent `glitch` Hermes profile, one shared NT journal/memory context, and four independently routed Sim books: balanced/Sim101 group, aggressive/Sim201, conservative/Sim301, and stay-revert/Sim401.
- Added `glitch.intent.batch.v1`: one model call must return exactly four `glitch.intent.v2` decisions in fixed book order. Local validation enforces unique route/account binding, snapshot identity, one-contract market entries, bracket geometry, per-book risk, and `NOTHING` for ineligible books.
- Added a single portfolio runner that can sequentially pass eligible entries through the existing one-shot Sim submission gate and proves paper/unarmed postconditions. No unified cron is enabled yet.
- Live preparation passed all four books with `transmitted=false`; 41 tests pass. The first external model run was blocked before transmission pending explicit approval to send the privacy-redacted live MNQ snapshot and Sim-only journal/account labels through Hermes to the external GPT service.
- Runtime remains paper/unarmed and all Sim groups remain flat/order-free. The earlier four cron jobs were removed and secondary Glitch gateway login services were uninstalled.

## 2026-07-13 — R06f expanded mining on GL-046 corpus: v1 archetypes invalidated, v2 set holdout-proven (Fable)

- Mined the full expanded corpus (1,410,695 snapshots, 2022-01→2026-03-12; known hole 2023-10..12 — exporter stopped mid-backfill, operator should re-run that slice).
- **Era re-test invalidated the v1 set:** profitable in 2022 bear, losing in 2023 recovery — 2 years of data cannot separate edge from era. All v1 archetypes demoted (retired/candidate) in `glitch_hermes_docs/memory/archetypes.v1.json`.
- **v2 set** (train 2022→2025-H1 across three eras, valid 2025-H2, locked 2026-Q1 holdout touched once): 5 validated — quiet-open breakdown short family (3), **HV-LULL-SHORT workhorse** (1,290 positive dedup trades across 4 eras, 4–10/wk), quiet-midday dip long (thin); 2 retired at holdout; 1 bear-era candidate.
- Seeded `glitch_hermes_docs/memory/archetypes.v2.json` + rewrote `mnq-playbook.md` (v2). Method + full tables: `docs/ai-program/r06-pattern-mining.md` §10; pipeline `Glitch-Collab/Research/r06-mining/` (mine_04/mine_05).
- **Next:** R13 replay proof on v2 set; fill 2023-Q4 corpus; DSR/PBO deflation pass; R11 paper loop should now cite v2 archetype_ids.

## 2026-07-13 — four-profile Hermes paper league

- Implemented one Glitch runtime with four isolated Hermes profiles bound to the existing Glitch group masters: `glitch`→Sim101, `glitch-aggressive`→Sim201, `glitch-conservative`→Sim301, and `glitch-stay-revert`→Sim401. Intent v2 now requires `operator_profile`; Glitch rejects unknown profiles and profile/account mismatches before execution.
- Group resolution remains owned by `AccountGroups.tsv`. Single-master groups are valid; Group 1 currently resolves Sim101/102/103 at 1:1:1 while Groups 2–4 resolve one 100K Sim master each. Cooldown and trade-count gates now scope to the master instead of throttling all profiles globally.
- Parameterized preflight, inference, cycle journals, locks, and one-shot submission by profile/master. A global Sim-submission lock prevents concurrent profiles from entering the temporary arm gate; policy restoration remains mandatory and `executor_left_armed=false` remains part of submission evidence.
- Installed all four profile SOULs and the five canonical Glitch skills. New profiles use `gpt-5.6-luna` with OpenAI Codex auth, stopped gateways, zero enabled cron jobs, and no arm.
- Verification: 35/35 Python/source-safety tests pass; all Hermes PowerShell scripts parse; `git diff --check` has no patch errors. All four live paper preflights passed with fresh MNQ, flat/order-free groups, exact profile/master bindings, and paper-safe policy. Four preparation-only cycles built the expected one-contract contracts with `transmitted=false`.
- Deployed the complete 78-file workspace AddOn folder once to NinjaTrader; source/live SHA-256 parity is 78/78. Runtime fixture `f6989d0f-e2a4-45f2-9be3-e4ef2e50b258` proved the new non-Sim101 binding: `glitch-aggressive`/Sim201 `NOTHING` returned 202 with `executor_mode_paper`; postconditions remained flat, order-free, `mode=paper`, and `executor_enabled=false`. No order was placed.

## 2026-07-12 — deployed paper rail; first entry candidates remained unsubmitted

- With operator approval, deployed the complete 78-file AddOn plus `GlitchMarketSnapshotRawJson.cs` in one combined invocation. AddOn hashes matched 78/78; the indicator matched semantically with CRLF-only normalization. NinjaTrader F5 completed without a compiler-error surface and recreated the Glitch window.
- Fresh live snapshots now emit instrument `current_price`. Tightened preflight/Hermes freshness to the authoritative envelope `created_utc`, rejecting future-dated or stale envelopes; suite passed 22/22.
- Closed GL-043 `NOTHING` endpoint idempotency: first exact fixture POST returned 202, duplicate returned 409, execution journal recorded `executor_mode_paper`, and before/after preflights proved Sim101/102/103 flat with no working orders.
- Hermes then produced two valid `ENTER_SHORT` candidates at approximately $32 and $35 risk per contract, both within the $300 six-contract group cap. Neither was submitted because its exact latest-only snapshot rotated before arm; the executor remained paper/unarmed.
- Fixed the root latency race without rebinding: Glitch now retains four writer-authenticated immutable recent snapshots, resolves only an exact embedded hash, and still enforces original age. Added a fail-closed Sim submitter that arms only after exact validation and restores paper state on any failure. AddOn redeployed and F5-compiled; suite passes 23/23.
- The first cycle through the retained-snapshot path completed in-window and returned `NOTHING`: bearish pressure existed, but price was already near the Asia low and structural invalidation exceeded the $20–50 risk budget. No trade was forced. Final paper preflight is green; all accounts remain flat and order-free; executor remains disabled.

## 2026-07-12 — GL-046 export still active at 1,394,655 rows

- A fresh read-only expanded-corpus audit at 22:46Z observed 1,394,655 indexed `glitch.market.snapshot.v2` rows. Coverage remains 2022-01-04 through 2026-03-12; the append stream has progressed through 2023-10-06.
- The index grew from 337,502,880 to 337,506,510 bytes during the audit, so freeze, full inventory, ETL, split design, mining, and holdout opening remain fail-closed. Exact duplicate IDs and malformed rows remain zero; 141 sampled payloads were clean. Timestamp sorting remains mandatory because the known append-block inversion persists.
- Evidence: `Glitch-Collab/Research/r06-mining/out/expanded_corpus_audit.current.json`. No corpus mutation or archetype/policy promotion occurred.

## 2026-07-12 — Sim101 master-intent / Glitch group contract

- Locked the authority boundary: Hermes emits one logical Sim101 intent only; Glitch resolves and manages the complete enabled account group, including follower quantities, brackets, aggregate risk, recovery, and execution evidence.
- Corrected the AI executor's previous equal-quantity behavior. Sim101 is multiplier 1 and enabled followers use canonical `AccountGroups.tsv` ratios; the current paper group therefore maps one master contract to Sim101:1, Sim102:2, Sim103:3.
- Risk is now computed from actual scaled quantities: each account's daily-loss gate uses its own exposure and the group gate sums all six effective contracts. Duplicate accounts, invalid ratios, non-integral scaled quantities, non-Sim members, and unallowlisted members fail closed.
- Added the permanent operator contract and source-safety coverage. Full Python suite passes 22/22; `git diff --check` has no patch errors. The privacy-redacted cycle prepare-only pass produced exact outbound evidence without transmission, and current paper preflight is fully green with all Sim group accounts flat and no working orders.
- A combined full-AddOn plus snapshot-serializer deployment preview passed. No live files were copied because the indicator boundary still requires explicit operator approval. Policy remains `paper`, executor remains disabled, and no order was placed.
- Strengthened paper preflight and GL-045 so group configuration now proves unique accounts, positive integral ratio-scaled quantities, and reports the exact execution geometry. Live evidence is Sim101:1, Sim102:2, Sim103:3, total multiplier 6. With the current `$300` group-loss cap, deterministic maximum risk is `$50` per contract; an `$80` per-contract trade would require a separately approved `$480` group cap and was not enabled.
- Aligned privacy-redacted Hermes guidance with that deterministic capacity. The cycle now resolves the canonical local group, derives the per-contract exploration ceiling from aggregate capacity, fails closed on malformed group geometry, and transmits only the resulting bounded trading constraint—not private account or policy state. Prepare-only evidence `live-20260712T224443Z` shows `$20–50`, Sim101 quantity 1, no Sim102/103 identifiers, no private policy key, and no transmission; suite remains 22/22.

## 2026-07-12 — active-rail reconciliation

- Reconciled the task-selection rail with runtime evidence: GL-042 implementation is deployed/compiled, GL-044 is complete with a 4,141-snapshot three-day capsule and schema-valid dry/paper `NOTHING` proof, and the full automatic suite is 18/18.
- `operations/pm/status.json` now selects GL-043 as primary. Current closeout is fresh-feed GL-043 POST/idempotency evidence, then GL-045 Sim-group fixtures. No arm or live promotion was authorized.

## 2026-07-12 — market-open prearm green; Hermes dry cycle timed out

- At 22:05Z the paper preflight turned fully green with fresh MNQ data, fresh complete Sim101/102/103 portfolio state, all accounts flat, zero working orders, and `mode=paper` / `executor_enabled=false`. Full GL-045 prearm passed the complete automatic matrix.
- Exactly one unposted Hermes cycle ran under the 120-second hard timeout. It captured `tools/hermes/tests/out/live-20260712T220553Z/cycle.json`, then timed out with empty stdout/stderr and produced no intent or usage artifact. The sandbox also denied the runtime failure-journal append, but source evidence remains in the bounded output directory.
- Fail-closed postconditions passed: `active-cycle.json` was removed, policy remained paper/unarmed, fresh preflight stayed green, and all Sim group accounts remained flat with no working orders. No idempotency run was attempted because no validated `NOTHING` intent existed; no POST, order, policy mutation, or account mutation occurred.
- Follow-up diagnosis isolated the timeout to the execution sandbox: `hermes auth list` failed on denied access to the Glitch profile `auth.lock`. A minimal outside-sandbox diagnostic containing no market/account data returned exactly `HERMES_AUTH_OK` in 15 seconds, proving the OpenAI Codex OAuth/model path is healthy. The corrected live-cycle request was rejected at the data-transmission boundary because it would send bounded portfolio/account and policy state to the external model without fresh explicit operator approval. No workaround or second cycle ran.

## 2026-07-12 — GL-043 fail-closed idempotency harness

- Added `tools/hermes/verify-nothing-idempotency.ps1`, constrained to a schema-valid `NOTHING` intent while policy is `paper` and `executor_enabled=false`. It requires green preflight, proves first POST `202`, exact duplicate `409`, flat/no-working-order postconditions, and a skipped execution-journal record.
- The script is PowerShell-parser clean; the complete Hermes validator/source-safety suite passes `19/19`; diff check is clean. Runtime proof was intentionally not run while MNQ freshness is red. No redeploy, arm, policy change, or order occurred.

## 2026-07-12 — GL-045 full-suite and kill-switch gate

- Corrected `gl045-prearm.ps1` to run the complete Hermes/source-safety suite rather than only the intent-validator subset.
- Added the previously omitted kill-switch matrix case and a source contract proving `ai_kill_switch` is firewall check 1, before AI enablement, instrument, account, risk, or execution checks.
- Full automatic suite is now `18/18`. GL-045 remains fail-closed only on stale Sunday MNQ data plus the intentionally pending operator Sim fixtures; executor remains disabled.

## 2026-07-12 — GL-046 expanded-corpus audit gate

- Added a read-only, memory-bounded expanded-corpus auditor under `Glitch-Collab/Research/r06-mining/`. It streams the index, proves exact duplicate IDs, records chronology transitions, samples payload schema deterministically, and can perform full index/file inventory fingerprints after export freezes.
- Current live audit observed `1,388,653` indexed `glitch.market.snapshot.v2` rows spanning `2022-01-04T07:00Z` through `2026-03-12T20:59Z`; zero malformed rows, duplicate IDs, path/name mismatches, or errors in 140 sampled payloads.
- The index is not globally chronological: line 772,357 transitions from `2026-03-12T20:59Z` back to `2022-01-04T07:00Z`. It also grew by 4,356 bytes during the 22-second audit, proving export is still active. No ETL, split, mining, or holdout selection may run until the export freezes; downstream code must sort by timestamp rather than trust append order.
- A 21:31Z re-audit observed 1,390,411 rows and 141/141 clean payload samples. The index again grew during the pass, confirming the exporter remains active; the freeze/split gate stays closed.
- Corrected a latent post-export deadlock in the audit: the known append-block inversion is now reported as `index_order_safe=false` / `requires_timestamp_sort=true` rather than making an otherwise frozen, inventory-matched set permanently ineligible for split design. Mutation, duplicates, malformed rows, inventory mismatch, path mismatch, or sampled payload failures still fail closed. Three focused gate tests and Python compilation pass; no mining ran.
- Added `freeze_expanded_corpus.py`: it refuses skip-inventory or mutating audits, re-hashes the current stable index, requires exact audited size/hash parity, preserves the timestamp-sort contract, and writes a freeze manifest only after those checks. The audit/freeze suite passes 6/6; an attempted freeze against the current audit failed closed on the missing full inventory match and wrote no manifest.
- Locked the expanded-run temporal doctrine before re-mining: 2025 is explicitly known evidence because Q4 was already opened and influenced the playbook; only purged 2026-Q1 may serve as the one-touch final holdout. The native validation bridge follows NinjaTrader's in-sample then forward unseen-test Walk Forward model, with local data and explicit costs; it cannot substitute for R13 replay parity or promotion approval.
- Added `design_expanded_splits.py`, which accepts only a provenance-bearing freeze manifest, requires the timestamp-sort contract and 2026 coverage, labels 2025 as contaminated known evidence for existing archetypes, and emits the 2026 holdout as `locked=true, opened=false` with explicit opening prerequisites. The combined audit/freeze/split suite passes 9/9; no freeze manifest exists yet, so the live command fails before writing a split artifact.
- Added `freeze_expanded_candidates.py`: it accepts only an unopened locked split, hashes the exact bytes and frozen geometry of each individual `MNQ-*.json`, rejects empty/malformed/duplicate-ID/geometry-less sets, and declares that any post-holdout mutation requires a new future holdout. The four-stage gate suite passes 12/12 and compilation/diff checks are clean; the live command correctly refuses while no split artifact exists.

## 2026-07-12 — GL-043 snapshot-price binding deployed

- Closed the market-price TOCTOU gap: one immutable snapshot read now binds the intent hash, creation-time freshness, instrument presence, and current price used for firewall risk/bracket geometry.
- Group execution independently repeats that exact-hash read immediately before per-account/group risk computation, so a snapshot rotation between firewall approval and order submission fails closed.
- Automatic suite is `17/17`; the complete 78-file AddOn was deployed once for this finished patch, source/live hashes match `78/78`, and NinjaTrader F5 returned without a compiler-error surface. Runtime preflight remains red only for the expected Sunday MNQ freshness gate; executor remains disabled.

## 2026-07-12 — GL-043 bounded cycle failure evidence

- Hardened `invoke-hermes-cycle.ps1` with a configurable hard Hermes timeout, captured stdout/stderr, durable `glitch.hermes.cycle_journal.v1` success/failure records, stage attribution, and guaranteed active-cycle cleanup.
- Functional failure fixtures prove both nonzero model exit and a five-second forced timeout produce no POST, a journaled `hermes_inference` failure, and no residual `active-cycle.json`.
- Automatic suite is now `16/16`; PowerShell parses cleanly and `git diff --check` reports no whitespace errors. Paper execution remains disabled.

## 2026-07-12 — GL-042 NT8 compile and refreshed runtime schema

- Sent F5 to the identified NinjaScript Editor through approved Windows control. NinjaTrader returned to the editor without opening a compiler-error window; the Glitch window was recreated, indicating the AddOn reloaded.
- Fresh runtime preflight now sees the required group fields for Sim101/102/103: portfolio fresh and complete, all accounts flat, zero working orders, policy paper-safe, telemetry and intent servers running.
- GL-045 automatic matrix passes every currently executable safety case. Readiness remains fail-closed solely on `mnq_fresh` because the market snapshot is stale on Sunday. Executor remains disabled; no order or account mutation occurred.

## 2026-07-12 — GL-042 safety closure and full-folder NT8 deployment (F5 pending)

- Three independent safety rereads drove fail-closed completion: exact account/instrument/net-position ownership before any flatten; fresh, complete, single-read portfolio risk state; explicit tracked-order terminal states; recovery remains tracked until account flat plus all orders terminal; malformed position arrays reject.
- Automatic evidence: `15/15` Hermes/validator/source-safety tests pass; `git diff --check` clean; `preflight-open.ps1` parses and correctly rejects the closed-market stale MNQ feed plus the old running portfolio schema.
- Deployment skill previewed and then copied the complete 78-file canonical `GlitchAddOn` folder once into the approved NinjaTrader AddOns target. No strategy/indicator tree, account configuration, policy arming, or order state was changed.
- NinjaTrader F5 was not sent because Windows app-control approval timed out. Required next: operator-approved F5 compile, inspect compile output, restart/reopen Glitch so the new snapshot schema is live, then run GL-045 fixtures. Paper executor remains disabled.

## 2026-07-12 — GL-042 group-safe executor source implementation (compile pending)

- Replaced the single-account AI executor with canonical `AccountGroups.tsv` resolution for the configured Sim101 master and enabled Sim followers only. Direct follower intents and any non-Sim/unallowlisted group member fail closed.
- Each enabled account receives its own market entry and native OCO stop/target pair. Signals use `GLT-AI-*`, so the normal fill-copy engine cannot duplicate AI follower orders.
- Registered AI orders with the existing runtime `OrderUpdate` path. A late rejection starts group recovery: cancel tracked working orders, flatten any exposed member, and append per-account correlation/recovery evidence to `intents/executions.jsonl`.
- Workspace checks: intent validator `6/6` pass; targeted diff check clean. NT8 F5 compile and GL-045 runtime matrix remain mandatory. No live-tree deployment, executor arming, or account mutation performed.

## 2026-07-12 — Market-open readiness and first real Hermes paper POST

- Added `tools/hermes/preflight-open.ps1`: fail-closed checks for telemetry/intent servers, AI/kill-switch state, executor account, MNQ freshness, Sim101 presence/flatness, and paper-safe policy. Closed-market proof failed only `mnq_fresh` (`mnq_age_seconds=121125.7`), as intended.
- Added `tools/hermes/invoke-hermes-cycle.ps1`: reads authoritative market/portfolio through telemetry, builds the bounded capsule input, calls the isolated profile, validates externally, and optionally posts only while `mode=paper` and `executor_enabled=false`.
- First production-shaped dry cycle passed: `NOTHING`, valid hash, no POST. First paper POST was rejected at firewall check 6 because the latest snapshot rotated during model latency. This proved fail-closed behavior and exposed the latest-only hash race.
- Added a pre-POST telemetry hash gate. Rerun passed: intent `cycle-20260712T170806Z-nothing`, hash `-517467854`, firewall checks 1–15 passed, response `accepted` / `mode=paper`, execution journal `skipped` with `executor_mode_paper`.
- Postcondition verified: policy remains `mode=paper`, `executor_enabled=false`, `executor_account=Sim101`; no order was created. At market open rerun preflight, then paper-cycle validation before any separately supervised sim arm.

## 2026-07-12 — GL-043 Hermes contract scenarios, first live pass

- Added `tools/hermes/run-contract-scenarios.ps1`, five bounded fixtures, external JSON Schema/custom invariant validation, and disposable per-run evidence under ignored `tools/hermes/tests/out/`.
- Independent model results on `gpt-5.6-luna`/medium: `fresh_long_breakout → ENTER_LONG`, `fresh_short_breakdown → ENTER_SHORT`, `stale_snapshot_nothing → NOTHING`, `open_long_hold → HOLD`, `open_long_exit → EXIT`. All five passed exact-one-JSON, Intent v2 schema, MNQ/Sim101 scope, snapshot-hash echo, market-only entry, one-contract, tick geometry, and scenario risk-cap checks.
- Fail-closed behavior was exercised during setup: two early runs returned valid `NOTHING` because the Docker fixture was unreadable, but the external validator rejected both for snapshot-hash mismatch. Root cause was malformed JSON serialization in profile `.env` for `TERMINAL_DOCKER_VOLUMES`/`TERMINAL_DOCKER_EXTRA_ARGS`; corrected to valid JSON and reruns passed.
- Live Glitch pre-order check: telemetry `8787` and intent `8788` listen; policy remains `mode=paper`, `executor_enabled=false`, executor account `Sim101`; snapshot sanity reports fresh envelopes but rail feed has `fresh_roots=0/3`. Therefore no Sim101 order test was armed or fired. Restore a fresh MNQ feed and complete GL-042/GL-045 gates first.

## 2026-07-12 — GL-044 isolated Hermes profile and Glitch-native skills

- Inspected `architectonic/skills/dist`: no reusable trading/futures skill pack was present. Kept only the transferable regulated-domain pattern: isolated run folder, reproducible inputs, fingerprints, credential separation, and explicit promotion gates.
- Added five canonical Glitch skills under `hermes-profile/skills`: `glitch-observe-market`, `glitch-assess-risk`, `glitch-form-thesis`, `glitch-build-intent`, and `glitch-review-outcomes`. All pass the skill validator. The first four compose the fast decision loop; review runs separately.
- Added `tools/hermes/build-data-capsule.ps1`. It allowlists current market/portfolio/policy, selected MNQ history, promoted patterns/playbook, Intent v2 schema, and provenance-separated journals; writes SHA-256 evidence to `manifest.json`; never exposes canonical `GlitchData` directly.
- Installed the five skills into Hermes profile `glitch`. Profile truth: `gpt-5.6-luna`, medium reasoning, memory off, curator off, delegation off, terminal-only toolset, Docker backend, no environment forwarding, no automatic cwd mount, no network, read-only capsule at `/opt/glitch-data`.
- Docker isolation proof passed: only capsule files were visible; the Glitch repo was absent; a write under `/opt/glitch-data` failed. Gateway remains stopped and no cron/order execution was armed.
- Attribution rule: Hermes starts at zero trades. Optional prior Sim101 records are labeled `legacy_sim101` and are context only, never Hermes performance.
- Remaining GL-044 acceptance depends on GL-043: strict adapter/schema dry-run from one supplied cycle fixture. GL-042 remains the execution-safety predecessor.

## 2026-07-12 — Hermes Sim101 group plan and profile reset (operator direction)

- **Scope:** Hermes may observe bounded raw/normalized MNQ snapshots, portfolio/group state,
  Apex 250k Legacy Sim policy, historical corpus, mined evidence, and versioned instructions.
  It proposes one strict market-order intent for `MNQ` / **Sim101 only**; Glitch owns price/risk
  revalidation, order creation, Sim102/103 replication, brackets, recovery, and journal.
- **Readiness correction:** existing R12 safely brackets only the direct account. Existing
  event-copy wiring mirrors fills but does not establish follower-native OCO brackets; direct
  follower account intents are not yet blocked by executor-account enforcement.
- **Plan:** GL-042 through GL-046 in `backlog.md` are the canonical Spec Kit task breakdown;
  no standalone planning document was created. GL-042 → GL-043 → GL-044 → GL-045 is the
  execution order; R06g runs after the incoming expanded corpus is complete.
- **Hermes profile:** reset from inherited orchestrator state to a minimal Glitch operator
  profile; model target `gpt-5.6-luna`, medium reasoning. It carries no broker/NT credentials,
  no cron jobs, no retained memory, and no general-purpose skill library. The runtime adapter,
  not prompt text, guarantees valid-or-no-trade intent handling.

## 2026-07-11 — R06 pattern mining v1 complete (Fable, parallel lane)

- **Corpus:** 705,697 MNQ 1-min snapshots (2024-01→2025-12) mined end-to-end; pipeline in `Glitch-Collab/Research/r06-mining/` (ETL → triple-barrier labels net of 1.15 pts friction → regime-cell expectancy scan → frozen archetypes → locked Q4 holdout).
- **Result:** 4 **validated** archetypes (3 downtrend-continuation shorts + opening-exhaustion fade), 1 candidate (London weakness, paper-only), 2 **retired** (US-close dip-buy and quiet-open momentum longs — destroyed in corrective Q4: bull-era beta, not edge).
- **Hermes seed:** `glitch_hermes_docs/memory/archetypes.v1.json` + `mnq-playbook.md`; profile skills/instructions in `glitch_hermes_docs/docs/12_hermes_trading_skills_and_knowledge.md`.
- **Method doc:** `docs/ai-program/r06-pattern-mining.md` (principles, metrics, validation spine, ongoing R06f loop).
- **Next:** R13 replay proof of validated archetypes vs NOTHING baseline; R11 paper loop can now cite archetype_ids in intents.

## 2026-07-10 — R11 Hermes stub + telemetry reads (glitch/ai-rail)

- `tools/hermes/suggest-trade.ps1` — GET market → POST paper NOTHING (cycles.jsonl).
- Telemetry: `GET /snapshot/sanity`, `GET /intent/decisions`, `GET /selfcheck`.
- Policy auto-upgrade adds missing `executor_*` keys on Glitch load.

## 2026-07-10 — snapshot sanity + replay harness scaffold (glitch/ai-rail)

- `GlitchSnapshotSanityWriter` → `selfcheck/snapshot_sanity.json` (5 min).
- `GlitchAiReplayHarnessWriter` → `replay/harness/latest.json` (15 min, MNQ 5m signal tally).
- Telemetry `GET /selfcheck`; script `tools/hermes/snapshot-sanity.ps1`.

## 2026-07-10 — R12 Sim executor scaffold (glitch/ai-rail)

- **R12:** `GlitchAiOrderExecutor` — bracket entry (`GlitchAIEntry`/`GlitchAIStop`/`GlitchAITarget`), EXIT flatten; UI-thread dispatch; journals to `intents/executions.jsonl`.
- **Safety:** default `mode=paper`, `executor_enabled=false`; arm only via `GlitchData/ai/policy.json`.
- **Next:** Hermes R11 paper loop or Sim101 live fire test with armed policy.

## 2026-07-10 — R09 firewall + R10 journal bridge (glitch/ai-rail)

- **R09:** `GlitchAiRiskFirewall` — 15-step chain on POST `/intent`; rejects return 422 with `failed_check_code`; all decisions journaled.
- **R10:** `GlitchAiJournalBridge` → `GlitchData/intents/decisions.jsonl` (intent + snapshot_hash + verdict + check trail).
- **Policy:** `GlitchData/ai/policy.json` (allowlists, M0 caps, kill switch); market snapshots now include `snapshot_hash`.
- **Next:** R11 Hermes paper loop or R12 Sim101 executor.

## 2026-07-10 — R08 paper intent server (glitch/ai-rail)

- **R08:** `GlitchAiIntentServer` on `127.0.0.1:8788` — POST `/intent` (v2 schema validation, paper mode, no executor). Journals to `GlitchData/intents/received.jsonl` + idempotency file `intent_ids.txt`. Shared bearer auth via `GlitchRailBearerAuth`.
- **Logging:** ingest `Log()` lines; intent accept lines in Glitch journal + selfcheck `intent.received_count`.
- **Next:** R09 AI risk firewall.

## 2026-07-10 — R07 telemetry + rail self-check (glitch/ai-rail)

- **R07:** `GlitchExternalTelemetryServer` on `127.0.0.1:8787` (GET `/health`, `/snapshot/*`, `/accounts`, `/positions`, `/risk`, `/journal/recent`; bearer token in `GlitchData/telemetry.token`).
- **Self-check:** `GlitchData/selfcheck/rail.json` every 30s — feed bus roots, snapshot counts, telemetry status (agent-readable).
- **Ingest:** `Log()` added alongside `Print()` so lines land in NT log files.
- **Next:** R08 paper intent endpoint.

## 2026-07-10 — Market snapshot v2 raw-only (glitch/ai-rail)

- **v2:** `glitch.market.snapshot.v2` — `timeframe_bars[]` with OHLCV + indicator numbers only; no scores, labels, or `no_trade_reasons`.
- **Live + export:** `GlitchMarketSnapshotWriter` and corpus exporter both emit v2.
- **v1:** opinionated builder retained as legacy reference; do not use for Hermes ingest or mining.
- **Operator:** clear old v1 corpus files before a fresh bulk export run.

## 2026-07-10 — R05 bulk historical corpus exporter (glitch/ai-rail)

- **Bulk export:** `GlitchMarketSnapshotHistoricalExporter` strategy + bridge `EnableHistoricalSnapshotExport` write `glitch.market.snapshot.v1` at **1-minute cadence only** (`source_mode: historical_replay`).
- **Shared JSON:** `GlitchMarketSnapshotJson` (Indicators) is canonical; `GlitchMarketSnapshotWriter` (AddOn) delegates to it so live and historical cannot drift.
- **Output:** `GlitchData/export/corpus/{INSTRUMENT}/{snapshot_id}.json` + `index.jsonl`.
- **Operator:** Strategy Analyzer on a **1-minute** chart; set date range; `PublishToGlitchUi=false`; export on by default.
- **Not:** `247TelemetryExporter` CSV — reference only.
- **Next:** R06 pattern mining on exported corpus.

## 2026-07-09 — R05 historical snapshot exporter (glitch/ai-rail)

- **R05:** `GlitchHistoricalSnapshotExporter` archives paired market+portfolio snapshots under `GlitchData/snapshots/historical/{market,portfolio}/`.
- Index: `snapshots/historical/index.jsonl` (paired by shared `snapshot_id`).
- Replay bundle: `snapshots/historical/replay/latest.json` (`glitch.historical.replay.v1`, `source_mode: historical_replay`).
- **Next:** R06 pattern mining (parallel) or R07 telemetry server.

## 2026-07-09 — UI bridge vs AI ingest split (glitch/ai-rail)

- **Rollback:** `GlitchAnalyticsBridge` restored to **single-instrument** UI scope (pre-R02 multi-asset on one bridge).
- **Perf:** skip indicator init on non-tracked bips; `EnableOrderFlowLayer` default **off**.
- **New:** `GlitchAiMarketIngest` — lightweight `OnBarClose` ingest on a separate chart; multi-root via Data Series → feed bus → R03 snapshot.
- **Shared:** `GlitchBridgeBusCompat.cs` extracted for both indicators.
- **Operator:** trade chart = bridge (MNQ); ingest chart = `GlitchAiMarketIngest` + Data Series (MES, M2K, …).

## 2026-07-09 — R04 portfolio snapshot writer (glitch/ai-rail)

- **R04:** `GlitchPortfolioSnapshotWriter` → `GlitchData/snapshots/portfolio/latest.json` (`glitch.portfolio.snapshot.v1`, accounts/positions/PnL/locks, prop rules version).
- Shared `GlitchSnapshotJson` helpers for market + portfolio writers.
- **Next:** R05 historical exporter (same schema).

## 2026-07-09 — R03 market snapshot writer (glitch/ai-rail)

- **R03:** `GlitchMarketSnapshotWriter` → `GlitchData/snapshots/market/latest.json` (`glitch.market.snapshot.v1`, 1-min throttle, coverage for TFs 1/5/15/60).
- Bridge logs bar-series + instrument-root count on `DataLoaded` for multi-asset diagnostics.
- **Feed check (NT log):** only **MNQ** publishing (4 readings / 1 root); add secondary roots via chart **Data Series** (1-min each).

## 2026-07-09 — Apex Legacy prop rules patch (glitch/ai-rail)

- `ApexTraderFunding` PA consistency **50% → 30%**; bundled fallback regenerated + deployed.

## 2026-07-09 — R01/R02 Eyes (glitch/ai-rail)

- **GL-025:** `GlitchInstrumentMetadataService` — point value, tick size, session from NT `MasterInstrument`; kills F2 silent `1.0` fallback (unknown instruments excluded from USD aggregates + visible warning).
- **GL-026:** Multi-asset bridge — per-bip instrument root + tick normalization; `AdditionalInstrumentRoots` AddData parameter; Analytics ATR shown in ticks when metadata resolves.
- **Next:** R03 market snapshot writer.

## 2026-07-09 — ABX memory system (ABKB)

- Glitch agent memory routes through ABKB (`knowledge/llm/memory-routing.md`), not Cursor `AGENTS.md` Learned bullets.
- Coding doctrine: `AGENTS.md` § Coding discipline → ponytail + repo reuse rules.
- Removed orphan `.cursor/hooks/state/continual-learning.json`.

## 2026-07-08 — Money path strip + v0.0.1.19 release prep

- **Copy:** Pure fill mirror (`Round(qty×ratio)`, same action); no position reads or exit caps.
- **Flatten:** `account.Flatten()` only (user Flatten All + enabled risk rules).
- **Removed:** GLT-FLAT/SYNC/PROT submit paths, protective OCO, sync machinery.

- **Flatten:** Chart Trader `Flatten All` no longer no-ops when header button ref is null — `RunFlattenAllAsync` runs flatten directly. Each exposed instrument root gets its own `account.Flatten()` call (NT primitive, per-instrument).
- **Drift:** Removed Honest Copy drift banner, Sync now button, and user-sync delta submit path.

## 2026-07-08 — Cursor honest-copy P3 header revert (compile pending)

- **P3-1 (reverted):** Removed Master/Group/Fleet scope selector; first header metric is static **Fleet uPnL** (sum of fleet total PnL: realized + unrealized per account, same basis as PA/Eval header metrics). Localization key `header.metric.fleet_upnl`.

## 2026-07-08 — Cursor honest-copy Phase 4 (compile pending)

- **P4-1:** `GlitchMainWindow.Replication.partial.cs` shrunk to honest-copy shim (~550 LOC): copy engine wiring, drift monitor, user Sync now (`GLT-SYNC`), legacy flag warns once.
- **P4-2:** Deleted polling replication body, protective mirror, emergency stop, strategy trade-source tree, replication freeze/burst/cooldown state.
- **P4-3:** Risk auto-flatten uses `account.Flatten` only (named-flatten chain removed).
- **P4-4:** Money-path empty catches on replication submit, cancel, runtime bridge, no-protection detect → `RecordSubsystemFault`.

## 2026-07-08 — Cursor honest-copy Phase 2–3 (compile pending)

- **P2-1:** `GlitchRiskMitigationEngine` — pure trigger evaluation; `ApplyRiskMitigations` → `ComputeRiskState` + `ApplyEnabledRiskActions`.
- **P2-2:** Max-contracts and no-protection flatten are per-account-type opt-ins (`ENFORCE_MAX_CONTRACTS_FLATTEN_*`, `ENFORCE_NO_PROTECTION_FLATTEN_*`); strategy heuristic gating removed from risk path; no replication freeze on compliance breach.
- **P2-3:** Risk journal schema `rule=...|action=...|observed=...|threshold=...|setting=...|detail=...`.
- **P3-1:** Header PnL scope selector (Master / Group / Fleet); default Master; basis label `realized+unrealized`.

## 2026-07-08 — Cursor honest-copy Phase 1 (compile pending)

- **Alan Phase 0 verify (same day):** sell 1/2/3 + Flatten OK (minor variation); sell 2/4/6 + Flatten OK; sell 2/4/6 + protective SL closed all three. Mid-flight PnL oscillation noted (101≈102, 103 ~2× — inconsistent with 2×/3× ratios).
- **P1-1 (compile pending):** `Services/Trading/GlitchCopyEngine.cs` — master `ExecutionUpdate` fan-out, `GLT-COPY` market orders, ExecutionId LRU, structured `copy|execId|...` journal rows; skips all `GLT-*` master fills.
- **P1-2 (compile pending):** `USE_LEGACY_REPLICATION_ENGINE` default **false** — `ExecuteReplicationCycle` no-ops; no absolute sync / protective mirror / emergency stop on the poll path when off.
- **P1-3 (compile pending):** Drift monitor (read-only banner + **Sync now** → one-shot delta, `user_sync|origin=user_sync_button`). Replication OFF cancels Glitch `GLT-*` working orders on followers.
- **P1-4 (compile pending):** PnL display hardening — `TotalPnlRaw = realized + unrealized` always; removed `TotalProfitLoss` ≈0 fallback flip (fable D-5 partial).

## 2026-07-08 — Cursor honest-copy Phase 0

- **P0-1 (Alan verified):** `TryExecuteFlattenAllAsync` uses `account.Flatten(instruments)` per account only; journals `flatten_all|origin=user_button|result=...`; incomplete flatten raises Critical `FlattenAllIncomplete` without resubmit.
- **P0-2 (Alan verified):** `EnforceStrategyComplianceActions` setting (default false, persisted); max-contracts and no-protection strategy-path flattens/freeze only when enabled.
- **P0-3 (Alan verified):** Replication starts OFF each session (`_isReplicatingUi` forced false on load); user click journals `replication_enabled|origin=user_click`.
- **P0-4 (Alan verified):** `ClampReplicationDelta` returns full delta; burst detection journals `burst_notice` only (no freeze).

## 2026-07-08 — INCIDENT + deep audit → "Honest Copy" rewrite ordered (architect: Fable)
- **Incident (sim, journal-proven):** Flatten All left the user's ATM Stop1 working → filled → Sim101 long 2 unsolicited → 500ms absolute sync bought 4+6 on followers + planted emergency stops → header showed unlabeled fleet PnL (+$72 unrealized → +$2 realized). Full reconstruction: `audits/fable-deep-audit.md` §1.
- Census: 257 fallback occurrences, 80 empty catch blocks, ~30 distinct order-path compensating mechanisms, 2,367-line replication partial on an 8,133-line god window. Verified Cursor's `cursor-deep-audit.md` and corrected it (missed: flatten-leaves-brackets root cause, frozen-account live stops, confirmed_Working bug, runtime/source drift).
- Operator decree recorded: **no Glitch-initiated action without user initiation or explicit granular opt-in; compliance = display math by default; every automatic action journaled with its authorizing setting.**
- Decision: stop patching guards; rewrite order path event-driven (GlitchCopyEngine), one flatten primitive (`account.Flatten`), drift reported never auto-corrected. Backlog Wave 8 (GL-036…041); **Wave 7 AI program frozen until GL-041 verification gate passes.** Handoff: `handoffs/2026-07-08-cursor-honest-copy.md` (Phase 0 same-day).
- Process finding D-10: running NT binary ≠ workspace HEAD (emergency_stop fired live but has no callers in source). All verification must state the binary's commit.

## 2026-07-08 — Replication storm RCA + hotfix (Sim101/102/103)

- **Incident:** Manual 2 MNQ on Sim101 + replicate (2×/3×) + Flatten All produced ~5 follower SL round-trips and ~$520 loss vs expected ~$39–52 scaled loss. Journal + TradeLedger prove buy → missing-master-protective emergency stop → freeze cleared on compliance refresh → rebuy loop.
- **RCA:** `docs/ledger/audits/replication-storm-rca-2026-07-08.md`. Root: `_replicationFrozenKeys` cleared by `ClearComplianceEnforcementRuntimeState` when compliance off; emergency stop on manual copy without master bracket; RP-2 double `ExecuteReplicationCycle`.
- **Fix (deployed to bin\\Custom source):** split `_replicationEngineFrozenKeys`; skip protective breach on manual copy; replication on light ticks only; F2/F4; **group Size/Master live-sync**; burst/protective/in-flight fixes; indicator publish throttle. **Operator:** recompile AddOn + Indicator; run Scenario A–D in `ponytail-audit-2026-07-08.md`.

## 2026-07-08 — Tradovate/Apex instrument universe (operator)

- Captured 148 Tradovate/Apex symbols (108 futures, 40 spreads) for future bridge/normalize/ingest/mining work. Operator intent: export ~2y normalized history via bridge (same units as realtime), mine, then connect live ingest. Catalog: `docs/ai-program/tradovate-apex-instrument-universe.md`.

## 2026-07-08 — Cursor trust-v0019

- **NT8 compile: PASS** (Alan, 2026-07-08) on branch `glitch/trust-v0019` after GL-014 CS1628/CS0019 fix (`6d2d716`). Runtime acceptance per `audits/trust-v0019-changes.md` still open.
- GL-020: `MarshalAccountRefreshResult` dispatcher catch → `RecordSubsystemFault("account_refresh", ex)`; finally/coalesce unchanged.
- GL-021: empty-account header zeroed; final shell publish on no accounts.
- GL-023: gitignore export artifacts.
- GL-024: F1 commission truth (net journal PnL + reconcile notice).
- GL-022: SHA-256 checksum manifest + `npm run checksums`.
- GL-014: per-account-type compliance settings granularity.

## 2026-07-09 — Branching doctrine: main vs glitch/ai-rail

- `main` = v0.0.1.x user line (v19 + non-AI patches); public download zips only from here.
- `glitch/ai-rail` = R01–R23 AI operating-system implementation (v0.0.2.x reserved until promotion).
- Wrote `docs/ledger/branching.md`; pointers in north-star, backlog, rail, roadmap, AGENTS.md.

## 2026-07-09 — v0.0.1.9 baseline closed; ledger pruned to rail

- Operator: v19 = Trust + stable + non-AI operator — Wave 6 + Honest Copy + session work treated as complete.
- Pruned `docs/ledger/backlog.md`: closed v19 items → done; umbrellas GL-008/009 superseded by R01–R23; active work = operating-system rail only.
- Carryover (not v19 blockers): GL-039 → R04; GL-041 → R14 pre-AI; GL-005 F2 → R01; prop-firm GL-017–019 deferred.
- Next: R01 (GL-025/026).

## 2026-07-09 — Operating system rail (first principles, no calendar gates)

- Operator doctrine: fail-fast ($17) beats fail-slow ($600 renewal); pay compute to build/mine/test; stall = bad; target pass eval in 7–15 live days after build+train when evidence green — not self-limited by arbitrary calendars.
- Wrote `docs/ai-program/operating-system-rail.md` — market + portfolio snapshots, Hermes operator bundle, three learning loops, rail R01–R23, parallel R06 mining, Eval Sprint profile for live pass pacing.
- `milestones-v20-v30.md` → pointer to rail doc. Roadmap gates de-calendarized.

## 2026-07-09 — Milestone contracts v20→v30 (operator sprint + 3 eval accounts)

- Operator context: 3× ~$250k funded evals, $15k pass / $6.5k max loss, ~30 days at ~$17 before ~$177 renewal; Hermes + Glitch + NT already running; fail-fast acceptable.
- Wrote `docs/ai-program/milestones-v20-v30.md` — M0-prep (v20–v22) → M0-sim/live (v23–v24) → M1 (v25–v26) → M2 (v27–v28) → M3 self-heal/learn (v29–v30); Eval Sprint Profile vs Survival Profile; account A/B/C roles; ponytail non-goals; week-by-week calendar.
- Roadmap table extended with v0.0.2.5–v0.0.3.0 rows + pointer to contracts doc.
- Next WO: confirm GL-041 → v20 (GL-025/026).

## 2026-07-08 — AI-program architecture pass (architect: Fable → implementer: Cursor)

- Operator decree: Fable architects/documents, Cursor implements exactly what is planned. Goal: improve v0.0.1.9, then v0.0.2.0+ with AI progressively integrated — more assets, better bridge/normalized analytics, then ingest → mine → backtest → learn → 5-min BUY/SELL/HOLD/NOTHING intents with **mandatory SL+TP1 (optional TP2/SL2), NT-held OCO brackets**, Glitch deterministic firewall before any order.
- Wrote `docs/ai-program/roadmap.md` — version ladder v0.0.1.9 "Trust" → v0.0.2.0 "Eyes" → v0.0.2.1 "Voice" → v0.0.2.2 "Ears" (paper) → v0.0.2.3 "Hands-sim" → v0.0.2.4 "Hands-eval", plus Hermes H-0/H-1/H-2 and the 15-step firewall chain.
- Wrote intent contract v2 (bracket mandate): `glitch_hermes_docs/docs/09_intent_contract_v2_brackets.md` + `schemas/intent.v2.schema.json`. AI never manages a loss mid-flight; naked positions impossible by construction.
- Backlog: Wave 6 (GL-020…GL-024, v0.0.1.9 hardening incl. RP-1 catch + F1 commission truth) and Wave 7 (GL-025…GL-035, AI program) seeded with gates; GL-008/GL-009 become umbrellas.
- Handoff for Cursor: `handoffs/2026-07-08-cursor-trust-v0019.md` (Wave A fully specified WO-A1…A6; Wave B preview only). Branch: `glitch/trust-v0019`.
- ABKB glitch project profile updated with AI-program section + pointers.

## 2026-07-08 — v0.0.1.8 post-publish review (lead: Fable)

- Full release review: `audits/v0.0.1.8-release-review.md`. Verdict: sound; one fix recommended.
- **RP-1 (P1):** wave-2 marshaled apply (`RefreshPipeline.partial.cs`) has `try/finally` without catch — exceptions in apply path (incl. `ExecuteReplicationCycle`) escape to WPF dispatcher; old timer-tick catch no longer covers it. Fix: catch → `RecordSubsystemFault("account_refresh", …)`.
- **RP-2:** replication cycle can double-fire (<500ms) and heavy-tick replication timing now rides thread-pool/dispatcher scheduling — LANE-1 must verify idempotency under the new pipeline.
- Security: shipped zip clean (compiled export only); license/HTTP surfaces pass; recommend SHA-256 next to release zips + scoped security audit before wider distribution and before Hermes servers land.
- Hermes (GL-009): read-only prep may start now per phase ladder step 2 (telemetry server + mktintel-style ingestion runtime); intent/execution path stays gated on Waves 1–2, LANE-1, and GL-005 F1 (journal must be commission-true before Hermes learns from it).

## 2026-07-08 — Performance wave 2 (refresh pipeline)

- **Light replication tick:** `heavyTabWork: false` skips `BuildAccountRow` loop — only `ExecuteReplicationCycle` + coalesced shell publish (500ms cadence no longer rebuilds all rows).
- **Background row build:** full refresh snapshots selection overrides, builds rows on thread pool, marshals apply/risk/replication/header to UI at `Background` priority; coalesces overlapping ticks; synchronous path for startup, flatten, grid edit, subsystem degrade.
- **Lock hardening:** `_peakStatesByAccount` and trade-source snapshots → `ConcurrentDictionary`; removed `_peakStateLock` / `_tradeSourceLock`.
- New: `GlitchMainWindow.RefreshPipeline.partial.cs`.
- Deployed to NT bin for F5.

## 2026-07-08 — Performance hardening PA-1…PA-9

- Implemented against current codebase (not blind handoff): HTTP 5s policy, fundamentals snapshot lock shrink, background ledger flush, subsystem fault auto-degrade, flatten submit metric, live-feed virtualization, timer Background priority + reentrancy guard, header metric skip-if-unchanged, collection caps.
- Evidence: `docs/ledger/audits/performance-hardening-pa1-pa9.md`.
- **Alan NT8 compile: PASS** (EarningsEvent type fix on snapshot scratch).
- Operator gates open: PA-5 adversarial replay numbers, PA-9 12h soak, network-pull freeze test, flatten `METRIC|flatten_submit_ms` smoke.

## 2026-07-07 — Runtime performance pass 2

- `AccountGridRow` INotifyPropertyChanged + `ApplyFrom` (stops ObservableCollection row replacement every PnL tick).
- Replication: 500ms light refresh + 3s full; dropped `AccountItemUpdate` subscription; journal batch inserts; analytics 8s throttle; account subscription resync 20s; typed `Position` scan.
- Deployed to NT bin for F5.

## 2026-07-07 — Runtime performance audit + fixes

- Operator: NT slowdown/crash pressure with Glitch open; paused accordion layout iteration.
- Audit: `docs/ledger/audits/runtime-performance-audit.md`.
- Fixes: tab-gated refresh (analytics/journal/settings), hidden-window light tick, 2s replication UI cadence, `AccountItemUpdate` throttle, shell publish coalesce, Chart Trader style once, single page-scroll accordion (removed nested scroll/MaxHeight layout), `CanContentScroll=false` on grids, localization slim-down.
- Deployed to NT bin for F5.

## 2026-07-07 — Accordion page layout (Dashboard + Journal)

- Structural redesign per operator: one **page scroll** per tab, standardized **Expander** sections (primary expanded by default), each section has **inner scroll** with viewport-based `MaxHeight`.
- New `GlitchMainWindow.AccordionLayout.partial.cs`; Dashboard groups + Connected Accounts; Journal Performance + Critical / Notice / Live Feed.
- Removed magic `MaxHeight`/`MinHeight` on accordion-hosted grids; follower per-group cap removed (section scroll owns overflow).
- Localization: `dashboard.replication_groups`.

## 2026-07-07 — Cursor wave 1 (post-compile UX iteration)

- **Alan NT8 compile pass** on `glitch/bulletproof-wave1` after compile-fix `aa251da` (CS0120 static/instance, `GridUnitType`, missing field).
- **Post-compile UX feedback (Alan):** (1) Dashboard two-tier layout still wrong — fixed bottom Connected Accounts steals height from followers when many accounts; wants **one pane**, followers star, accounts **collapse or scroll**, not compete. (2) Journal bottom sections stack tight; Expander headers show **`System.Windows.Controls.TextBlock`** (NT8 Expander renders `Header.ToString()` when Header is a TextBlock — use `BindLocalizedHeader` string headers).
- **Iteration (uncommitted):** Dashboard — single star row; groups fill; Connected Accounts in **collapsed Expander** with capped grid (`DashboardTab.partial.cs`). Journal — `BindLocalizedHeader` on Notice History + Live Feed expanders; bottom `StackPanel` with spacing; critical grid row `Auto` not `Star`; removed forced `MinHeight` on empty critical grid (`JournalTab.partial.cs`).
- **Acceptance still open:** Alan recompile + smoke per `audits/ui-calm-changes.md` before any `done` flip.

## 2026-07-07 — Cursor wave 1

- GL-014 (WO-10): settings granularity design written to `audits/ui-calm-changes.md` (`5ae3c63`); implementation deferred.
- GL-019 (WO-9): `copyTradingPolicy` schema + parser + Settings compliance notice (`d262a31`, JSON/parser in `d83b977`).
- GL-018 (WO-8): Lucid rules rebuilt EOD tiers in `PropFirmRules.json` (`d83b977`).
- GL-017 (WO-7): FundingTicks `Discontinued` + UI suffix (`d83b977`).
- GL-015 (WO-5): ratio `ConverterCulture`, hover affordance, math tooltip (`GlitchMainWindow.cs`, `7ec0ac4`).
- GL-011 (WO-6): followers-first dashboard row swap (`DashboardTab.partial.cs`, `2b5c52b`).
- GL-005/F2 (WO-11): unknown point value quiet notice (`SummaryTab.partial.cs`, `bac3046`).
- **Compile fix (post-Alan F5):** restored `_settingsLicenseKeyUnmaskedValue`; fixed `GridUnitType`→`DataGridLengthUnitType` in notice grid; `L`/`Lf` out of static helpers; `NormalizeTradesToUsd` instance method for F2 notice path.
- GL-012 (WO-4): Critical vs Notice taxonomy; notice history expander; header count critical-only (`GlitchMainWindow.cs`, `Models.partial.cs`, `7ec0ac4`).
- GL-013 (WO-3): Journal Trader Performance primary full-width; Live Feed in collapsed Expander (`JournalTab.partial.cs`, `f61bfd4`).
- GL-010 (WO-2): `MaxHeight = 240` on per-group follower DataGrids (`GlitchMainWindow.cs` `CreateGroupMembersGrid`, `b209ad1`). Pairs with WO-1 cap; groups section ScrollViewer handles many groups.
- GL-010 (WO-1): committed lead-approved `MaxHeight = 240` on connected-accounts DataGrid (`DashboardTab.partial.cs`, `d516250`). Backlog → partial (awaiting NT8 compile).

## 2026-07-07 — pass 7 (lead: Fable) — wave 1 delegated to Cursor/Composer 2.5

- Claude plan hard-rate-limited; operator redirected execution to Cursor ($60 plan, idle). Wrote `handoffs/2026-07-07-cursor-wave1.md`: WO-1…WO-11 covering GL-010/011/012/013/015 (UI calm), GL-017/018/019 (rules truth from LANE-4 findings), GL-014 design, F2 stretch fix. Branch: `glitch/bulletproof-wave1`; main stays clean; done-gate remains Alan's NT8 compile.
- Fable schedulers retooled: lane-relaunch one-shot cancelled; 3-hourly spawner replaced with a 2-hourly monitor-only pass (reviews Cursor's branch commits + ledger entries, writes lead-review-notes.md on violations).
- LANE-1 (replication audit) held for a future Opus window (or Cursor first-pass + Opus verify) — money-path audit deserves the strongest reasoning available.

## 2026-07-07 — pass 6 (lead: Fable) — scheduled pass; LANE-3's surviving edit reviewed

- 13:14 São Paulo, still pre-reset (18:10). Discovered one surviving LANE-3 edit in the working tree: `DashboardTab.partial.cs` — `MaxHeight = 240` on the connected-accounts grid (GL-010). Lead review: APPROVED — premise verified against row layout (Auto row1 grid starving star row2 followers); uses existing `ConfigureDataGridScrolling`; also advances GL-011. **Held uncommitted pending Alan's NT8 compile** per C#-gate. GL-010 → in_progress.
- F1 refined earlier this window (commit `c2d00ff`): dashboard reads NT net via account items; Journal recomputes gross; "commission" absent from entire AddOn — Glitch disagrees with itself; sim masks it, funded exposes it.
- Next event: 18:27 SP one-shot relaunches LANE-1 (opus) + LANE-3 (sonnet), incremental-write instructions.

## 2026-07-07 — pass 5 (lead: Fable) — LANE-2 landed; limit hit again

- **LANE-2 (math-audit, Opus) COMPLETE** → `audits/pnl-math-audit.md` (212 lines, 10 ranked findings). Lead spot-verified F2 (`return 1.0;` pointValue fallback at SummaryTab.partial.cs:940) — grounded. GL-005 → partial (audit done, fixes pending). Headline: arithmetic sound (screenshot reconciles to the cent) but journal PnL is gross of commissions (F1 — why Glitch ≠ NT), unknown-instrument pointValue silently 1.0 (F2 — gates GL-008), fleet-aggregated stats redefine win-rate (F3).
- LANE-1, LANE-3, LANE-4 killed by the next session-limit window (resets 18:10 São Paulo). LANE-1 and LANE-4 died AT report-writing stage; key LANE-1 finding banked: follower-cell WPF bindings lack `ConverterCulture` (en-US parse vs pt-BR display). Progress notes updated in `lane-briefs.md`.
- Relaunch scheduled shortly after 18:10 SP: LANE-1 + LANE-4 first (near-complete, cheap wins), LANE-3 after they land (2+2 stagger per contingency).
- **Correction (same pass): LANE-4's report was found COMPLETE on disk** (`research/nt8-propfirm-refresh-2026-07.md`, 184 lines, fully cited) — the agent died after writing, before reporting. GL-016 → done (research). Red flags spawned **GL-017** (FundingTicks is CLOSED since 2026-01, still shipped as Supported), **GL-018** (Lucid rules block is a byte-identical copy-paste of stale FundingTicks data), **GL-019** (no copy-trading policy encoding; TPT's own policy prohibits cross-account copy services — existential for replication; operator must confirm TPT/TradeDay policy in writing). Relaunch re-scoped: next window spawns LANE-1 + LANE-3 only, writing outputs incrementally.

## 2026-07-07 — pass 4 (lead: Fable) — lane relaunch after limit reset

- ~10:53 São Paulo: session limit confirmed reset. Relaunched all four lanes from `lane-briefs.md`: LANE-1 replication-audit (Opus), LANE-2 math-audit (Opus), LANE-3 ui-calm (Sonnet), LANE-4 external-truth (Sonnet). All running in background; outputs expected in `docs/ledger/audits/` and `docs/ledger/research/`.
- Contingency if the limit trips again mid-flight: partial-progress notes go into lane-briefs.md and lanes restagger 2+2 on the next window.

## 2026-07-07 — pass 3 (lead: Fable) — scheduled pass, honest no-op

- 10:13 São Paulo: still inside the session-limit window (resets 10:40). No lane outputs; no C# changes; nothing to integrate. No new subagents spawned (would fail against the limit and duplicate the scheduled 10:53 relaunch). Next event: one-shot lane relaunch at ~10:53 São Paulo.

## 2026-07-07 — pass 2 (lead: Fable) — session-limit recovery

- All four lanes (2× Opus audit, 2× Sonnet) were killed mid-flight by the subscription session limit (resets 10:40 America/Sao_Paulo). No output files were written; no C# was modified (verified via git status). Partial progress notes captured into `lane-briefs.md`.
- Committed previously-untracked `glitch_hermes_docs/` (AI decision-layer contract referenced by GL-009) — `ba510e7`.
- Wrote `docs/ledger/lane-briefs.md` — relaunch-ready delegation prompts so any lead can respawn lanes without reconstruction.
- Scheduled one-shot relaunch of all four lanes for shortly after the limit reset.
- Backlog status: unchanged (all items todo — honest no-op on findings).
- Blocker: subscription session limit until 10:40 São Paulo.

## 2026-07-07 — pass 1 (lead: Fable)

- Seeded ledger (README, north-star with calm-by-default invariant, backlog GL-001…GL-016 with dependency graph + delegation map) — `e1f0ac7`, pushed.
- Spawned four lanes: replication-audit (Opus), math-audit (Opus), ui-calm (Sonnet), external-truth (Sonnet).
- Created recurring 3-hourly lead operator pass (session-scoped cron).

## 2026-07-20 — isolated trading calls, one-minute recovery, and MOVE_TP

- Root-cause correction: scheduled inference now uses a fresh Hermes session tagged `trading` for each eligible packet. Learning continuity is resent explicitly as five MNQ market frames, the latest portfolio, six recent decisions/executions/outcomes, and native durable memory. Raw transcript accumulation can no longer poison later decisions. The live prompt fell from 90,603 to 57,352 characters while retaining current decision truth.
- Strict JSON output now starts from a literal cycle/book-scoped valid template and accepts no renderer chatter, duplicate object, or repaired malformed JSON. Any model, timeout, compaction, or contract failure records no intent and makes only the next newer minute packet eligible.
- Hermes cron remains on its native one-minute schedule. The enable script now reads `jobs.json` back and fails if schedule, script, workdir, mode, enabled state, or singleton ownership did not persist; this closes the false-success path that claimed an unsupported `30s` edit.
- Added `MOVE_TP` end to end. Hermes may move all remaining master targets with an optional simultaneous tighter stop; validator/firewall enforce strict fields, tick prices, profit-side targets, and no stop loosening. One atomic master `Change` is mirrored by CopyEngine to the corresponding follower target/stop orders.
- Deployment/verification: complete 87-file AddOn installed with 87 source/live hashes matching and zero extras; operator confirmed NinjaTrader F5 compile green. Shared contracts 37/37 and AI/Hermes contracts 90/90 pass (127 total); PowerShell, JSON, and diff checks pass. Corrected Hermes profile installed at `2026-07-20T20:18:32Z`; exactly one job is enabled (`glitch-direct-operator`, `* * * * *`), gateway PID 42852 was live, installed worker hash matched source, and the first post-install scheduler tick completed `ok`.
