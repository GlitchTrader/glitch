# Glitch Stability Lock Audit

**Date:** 2026-07-29  
**Scope:** NinjaTrader Standard and NinjaTrader Advanced plus the Advanced Hermes profile  
**Posture:** source-first adversarial review, blue-team hardening, red-team execution/recovery review, white-hat operational wargame  
**Authority:** `docs/ledger/ledger.json` remains the canonical work ledger

## Executive verdict

Glitch is a real operating system rather than a strategy mock-up. Standard has a substantial execution-first replication engine and Advanced has a disciplined cognition/execution boundary. Recent bounded Sim operation produced encouraging short-session PnL and demonstrated that the premise is operationally plausible.

The system is not yet stable enough for evaluation, PA, live-capital, unattended, or profitability claims.

The immediate task is to reduce active work to four foundations:

1. truthful account/risk state;
2. native replication/reload/flatten correctness;
3. one durable AI execution owner;
4. native terminal outcome truth;
5. then release/profile compatibility.

All broad compliance, performance, documentation, and architecture campaigns should be sequenced behind those foundations unless they directly close one of them.

## Product-line boundaries

### Standard

No-AI NinjaTrader operating layer. It owns account truth, risk presentation, manual/strategy master observation, follower replication, native protection, Sync, Flatten All, persistence, journal, and UI.

### Advanced

The same native NinjaTrader authority plus a Hermes cognition lane. Hermes proposes intent. Glitch owns factual executability, native order mutation, brackets, replication, reconciliation, journals, and terminal outcome.

### Topstep

A separate repository and release channel. No NinjaTrader acceptance evidence may be reused as Topstep proof, and no Topstep provider evidence may close NinjaTrader items.

## North star

A dependable, evidence-rich trading operating layer whose distinct products can be installed, operated, recovered, and reasoned about safely. Profitability is an empirical long-run outcome, not a substitute for engineering correctness.

## Current weekly outcome

1. Diagnose and close `GL-STAB-01` unknown-safe risk state.
2. Close the native acceptance of `GL-REP-01`.
3. Reconcile source/ledger/package truth and close durable ownership in `GL-AI-06`.
4. Keep `GL-AI-07` pending until exact native position/protection outcomes are proven.
5. Define and enforce `GL-REL-01` package/profile compatibility.

## Portfolio evidence boundary

The operator-provided runtime shows approximately USD 1,900–2,000 aggregate Sim PnL over a short session with 16 trades, a reported 50% win rate, and a positive profit factor. This is valuable operational evidence that:

- the UI, account observation, journal, AI loop, and replication can run together;
- the system can express and manage a sequence of real Sim positions;
- the product premise warrants continued testing.

It does not prove:

- long-run expectancy;
- profitability after fees/slippage across regimes;
- risk-state correctness;
- evaluation/PA/live readiness;
- native callback/restart safety;
- absence of duplicated or missed orders;
- customer suitability;
- unattended operation.

Preserve the episode with exact account, mode, version, timeframe, trades, fees, and journal. Do not generalize it.

# Standard SWOT

## Strengths

- Replication observes native master executions rather than requiring one producer strategy.
- Follower mutations carry signal/correlation ownership.
- Master native bracket exits are excluded from duplicate fan-out paths.
- PositionUpdate owns final follower protection convergence.
- Exact-instrument state is used for reconciliation.
- Partial reductions preserve protection until authoritative native state is available.
- Excess owned reduction remainders can be cancelled when position truth changes.
- Signal-owned protection remains managed after route disable/removal.
- Sync distinguishes already-synced, tail, reduce, and flatten/re-enter cases.
- Manual unrelated orders and exposure are deliberately outside ordinary replication ownership.
- Standard and Advanced share significant implementation, making parity achievable.

## Weaknesses

- Recent execution deduplication is process-memory bounded and depends on native reconstruction after reload.
- Native repeated-reload acceptance is still pending.
- A single source test cannot prove retained prior-assembly event subscriptions are absent.
- Fractional ratio allocation across separate order identities remains implicit.
- Flatten All completion and unresolved-account reporting need one bounded native fixture.
- Risk state can show 100% from zero/unknown PA values after reinstall/reload.
- Published packages can outrun native acceptance.
- Too many ledger items remain simultaneously in progress.

## Opportunities

- Locking a clean Standard Sim baseline creates a product that can be tested independently of AI/model cost.
- The execution-first engine can become the stable native substrate for Advanced and potentially inform Topstep ownership semantics.
- Exact native journals and screenshots can become a reproducible acceptance harness.
- Explicit fractional allocation and reload ownership can differentiate Glitch from brittle copy-trader tools.
- A stable Standard product can generate legitimate user/tester feedback while Advanced remains experimental.

## Threats

- One retained prior engine can duplicate follower mutations after reload.
- A protection fill racing a copied close remainder can over-close or reverse a follower.
- Unknown risk state displayed as real loss can trigger incorrect operator action or hide a real lock.
- Cross-contract/root-symbol confusion can mutate the wrong expiry.
- Manual follower activity can be commandeered if attribution inference becomes permissive.
- A false successful Flatten All can leave exposure or working orders.
- Package/release drift can make runtime reports impossible to reproduce.

# Advanced SWOT

## Strengths

- Hermes is explicitly advisory/cognitive; NinjaTrader remains native authority.
- The direct worker validates policy and packet state before spending a model call.
- The profile setup verifies a SHA-256 distribution manifest and path containment.
- Fresh installs leave operator and learning jobs paused.
- Supported job definitions are reconciled without silently changing enabled state.
- The profile preserves authentication, sessions, memories, and supported overrides on update.
- Decision prompts require strict JSON and distinguish current native facts from probabilistic judgment.
- Learning influence requires repeated attributable completed master outcomes.
- Follower ratios and limits are visible but do not silently determine master cognition.
- Direct commands remain bounded and do not intentionally turn Codex into the trading loop.

## Weaknesses

- Canonical ledger evidence and inspected `ai/22` source disagree on the atomic execution-owner transition and raw-body byte limit.
- The inspected intent server can save terminal `executed` after synchronous executor result rather than native fill/protection truth.
- A successful `Account.Submit` or `Account.Change` return is too close to terminal semantics.
- Detached minute-worker overlap needs executable cross-process proof.
- AddOn, profile, intent schema, prompt, Hermes, and GlitchData versions are not one enforced compatibility contract.
- Profile README version requirements can drift from the latest published AddOn.
- Native restart and delayed-callback outcomes remain open.
- Current UI feed can overstate queue submission as completed execution if the underlying state is terminal too early.

## Opportunities

- A durable pending/native-terminal state machine can make Advanced materially safer than typical AI-trading adapters.
- Strict source/profile compatibility can allow clean user installation and reproducible testing.
- The current outcome-backed cognition doctrine can generate a credible research corpus without deterministic overfitting.
- A stable native substrate plus bounded Hermes profile can be tested by external collaborators without granting broad authority.

## Threats

- Duplicate requests can produce duplicate native mutation if execution ownership is not atomic.
- Process restart can blindly resubmit an ambiguous order.
- Delayed rejection or bracket failure can be recorded as success and poison learning.
- Stale profile and AddOn combinations can disagree on schema, signal names, or job cadence.
- Model output can be valid JSON but factually stale relative to current native state.
- Human same-instrument actions can be mistaken for AI-owned outcomes.
- Short-session profit can create pressure to relax gates before lifecycle proof.

# P0 findings

## GL-STAB-01 — unknown-safe risk state

Issue: https://github.com/GlitchTrader/glitch/issues/2

### Observed defect class

After reinstall/reload, the UI can show:

- Global Risk 100%;
- PA Risk 100%;
- PA rows with equity/cash read as zero and negative buffer;
- unaffected Eval/Sim rows showing normal values.

This is runtime evidence, not a root-cause conclusion.

### First-principles contract

Risk percentage is a calculation over authoritative account identity and same-epoch inputs. Missing, stale, disconnected, or unclassified fields must remain unknown. Unknown may fail closed operationally if the governing rule requires it, but it must be named `state unavailable`, not presented as a realized 100% loss.

### Trace points

For one affected account, record:

1. native account identity and connection;
2. account type/firm/stage classification;
3. configured size, starting balance, loss model, and thresholds;
4. native cash, net liquidation, realized, unrealized, and margin reads;
5. persisted peak/baseline/lock/epoch;
6. calculated floor, buffer, percentage, and lock;
7. PA/Eval/global aggregation;
8. UI formatting;
9. reinstall, reset, disconnect, reconnect, and stage-change transitions.

Every field must carry provenance: native observed, operator configured, persisted, calculated, unknown, or stale.

### Edge cases

- real authoritative zero versus unavailable zero;
- account exists but provider metric is unsupported;
- disconnected account with last-known values;
- stale peak from a prior account/account-stage epoch;
- duplicate display names with different stable IDs;
- Sim account accidentally classified as PA;
- PA/evaluation transition;
- Reset Data while the account is disconnected;
- reinstall with existing GlitchData versus clean GlitchData;
- old package reading a new schema;
- one unknown account in a mixed fleet;
- actual hard-floor breach;
- currency/locale parsing;
- temporary read order mixing new PnL with old equity;
- account row removed/readded during refresh;
- negative cash/equity that is genuinely authoritative.

### Stop line

No evaluation/PA/live-readiness claim until unavailable state and real lock are visibly distinct and regression-tested.

## GL-REP-01 — native replication/reload/flatten

Issue: https://github.com/GlitchTrader/glitch/issues/3

### Required native fixture

1. exact preflight: all selected Sim accounts flat and order-free;
2. repeated AddOn reloads: 0, 1, 3, 10;
3. one master fill;
4. one ratio-scaled attributable follower delta per enabled route;
5. native working protection sized to copied exposure;
6. partial master close;
7. protection-fill/reduction race;
8. manual same-instrument follower interleave;
9. route disable/removal with open copied exposure;
10. explicit Sync;
11. one-click Flatten All;
12. final fleet flat/order-free and no retired engine mutation.

### Fractional allocation decision

Choose and implement one visible cumulative basis for examples such as:

- two separate one-contract closes at ratio 0.5;
- partial fills across two order identities;
- restart between fills;
- ratio change followed by Sync;
- master reversal through separate orders.

Per-order rounding reset cannot remain accidental policy.

### Reload ownership edge cases

- old window visually closed but callbacks retained;
- static event handler survives assembly reload;
- new and old engines share the same account object;
- shutdown while an order callback is executing;
- Configure clears routes while a callback holds a snapshot;
- duplicate execution identity evicted from the 1,024-entry memory window;
- execution ID missing and fallback identity collides;
- two masters share signal names;
- copy signal re-enters another group topology;
- contract rollover with same instrument root.

### Stop line

Sim only. Do not close from source-shape or package evidence.

## GL-AI-06 — durable execution owner and source truth

Issue: https://github.com/GlitchTrader/glitch/issues/4

### Current contradiction

The ledger records a durable expected-phase promotion and raw UTF-8 byte enforcement. The inspected maintained `ai/22` server path still shows:

- `TryClaim`;
- resumable approved state;
- unconditional `TrySavePhase(..., "execution_started")` before executor call;
- decoded-character `StreamReader` limit.

The first implementation task is not new code. It is truth reconciliation across:

- canonical source branch/commit;
- installed AddOn;
- downloadable package;
- ledger evidence;
- any unmerged worktree/commit.

### Required ownership semantics

- one UUID plus canonical body hash;
- exactly one durable executor owner;
- same body duplicates observe/replay;
- different body conflicts;
- crash state reconciles before any replay decision;
- no native/UI/network work under the short ownership lock;
- raw byte limit before decoding;
- slow clients cannot monopolize global intent authority.

### Edge cases

- 100 identical requests released by a barrier;
- 50 same-body plus 50 conflicting-body requests;
- process death after claim, approval, promotion, submit, journal, and receipt boundaries;
- legacy journal identity with no state file;
- state file exists but response write is incomplete;
- disk full on phase transition;
- antivirus/backup holding the state file;
- stale `approved` state from an old policy version;
- same UUID with semantically same JSON but different whitespace/property order;
- multibyte body within character limit but above byte limit;
- slowloris body and independent valid request;
- two AddOn instances/listeners competing for the port.

### Stop line

No external intent authority and no native test beyond explicitly approved Sim fixtures.

## GL-AI-07 — pending until native terminal truth

Issue: https://github.com/GlitchTrader/glitch/issues/5

### Required state distinction

Queue admission is not terminal execution.

Entry success requires exact:

- account;
- contract;
- correlation-owned native entry;
- accepted/fill outcome;
- attributable position;
- working native stop and target coverage;
- no ambiguous human ownership.

Amendment success requires exact selected leg and confirmed native working price/state. EXIT success requires attributable exposure absent and owned orders reconciled.

### Fault injection matrix

- before plan persistence;
- after plan persistence before submit;
- submit begins before return;
- return before callback;
- partial fill;
- fill before complete protection;
- delayed rejection;
- protection rejection;
- terminal native state before durable receipt;
- restart/reload in every phase;
- human partial/full close during pending state;
- disconnect during amendment;
- callback order inversion;
- duplicate late callback after terminal state.

### Learning boundary

Only attributable terminal completed episodes may enter outcome-backed learning. Pending, queue-admitted, superseded, or ambiguous events remain evidence but cannot be scored as completed trades.

### Stop line

No unattended/evaluation/PA/live readiness until bounded native callback and restart fixtures pass.

## GL-REL-01 — package/profile compatibility

Issue: https://github.com/GlitchTrader/glitch/issues/6

Create machine-readable Standard and Advanced release manifests that bind:

- source commit;
- package version/hash;
- supported NinjaTrader range;
- persistent-data schema;
- Advanced profile version/hash;
- Hermes version;
- intent/prompt/exchange schema;
- jobs/cadence;
- tests/native evidence;
- rollback boundary;
- unsupported combinations.

The profile setup must fail closed on a known-incompatible AddOn rather than enabling jobs.

### Edge cases

- new profile with old AddOn;
- new AddOn with old profile;
- profile update while jobs enabled;
- AddOn update while positions open;
- partial installation or missing hash file;
- package replaced under the same filename;
- GlitchData schema newer than rollback package;
- user overrides conflict with released routing;
- stale download site;
- AB public index advertising a maturity no release manifest supports.

# P1 findings

## Detached worker exclusion

The minute cron launcher detaches the direct worker so model latency cannot block the next cron tick. This is reasonable only if one cross-process exclusion lock covers:

- packet selection;
- model call;
- intent post;
- final worker cleanup.

Test:

- simultaneous launcher calls;
- previous worker hung beyond four minutes;
- stale lock after crash;
- machine sleep/resume;
- clock change;
- two profiles using the same GlitchData;
- launch during epoch reset/profile update.

## Control-plane completion

`/flatten_all`, replication enable/disable, AI pause/resume, and direct experiments must distinguish:

- request accepted;
- native action started;
- native action complete;
- partial/unresolved;
- denied/failed.

Queue admission is not success. Existing `GL-CTRL-01` should be sequenced after the underlying execution/replication P0s.

## Persistent state ownership

Inventory every file/table under GlitchData and classify:

- product-owned persistent operator state;
- epoch-scoped AI evidence;
- regenerable cache;
- native account truth;
- release metadata;
- secret/config;
- human journal.

Then define reinstall, update, Reset Data, epoch reset, backup, and restore semantics. No command should ambiguously clear several classes.

## PnL and journal truth

Validate:

- account versus aggregate trade count;
- master versus follower allocation;
- commissions/fees/slippage treatment;
- realized versus unrealized;
- partial fills and scale-outs;
- reset epoch boundaries;
- timezone/session day;
- simulated versus provider accounts;
- duplicate execution/journal records after reload.

A screenshot is a useful visual check, not the sole accounting proof.

## Account/group topology

Wargame:

- account is follower in group A and master in group B;
- master and follower ratios change while positioned;
- account removed from group while orders working;
- two groups share a follower;
- master disconnects while follower remains connected;
- route added while master already positioned;
- same display name from two connections;
- unavailable account becomes available after Sync.

The product must preserve the explicit topology policy and prevent feedback loops without silently rejecting supported arrangements.

# P2 findings

- `GL-ARCH-01` source decomposition is valuable but must remain behind P0 behavior.
- Shared Standard/Advanced extraction should occur only in files touched by a closed contract.
- Performance work follows measured callback/UI/model/runtime profiles, not broad optimization.
- Compliance features remain observational or explicit opt-in unless they meet the strict authoritative universal rule contract.
- Documentation campaigns should reconcile source/release truth after behavior closes.

# P3 / deferred

- new strategy/archetype expansion;
- broader instruments beyond the verified contract;
- unattended/live operation;
- automatic evaluation/PA account progression;
- generalized external API;
- performance marketing claims;
- broad AI model experimentation during lifecycle stabilization.

# White-hat threat model

## Assets

- account/order/position authority;
- risk and lock state;
- provider credentials and bearer token;
- intent identity and state;
- native signal/correlation ownership;
- journal and learning evidence;
- package/profile integrity;
- operator controls;
- GlitchData persistence.

## Attack and failure surfaces

- local HTTP listener and bearer token;
- request body parser and JSON validation;
- duplicate/concurrent requests;
- native callbacks and UI dispatcher;
- reload/static event subscriptions;
- Hermes profile scripts/plugins/jobs;
- filesystem state and atomic writes;
- setup/update/reset scripts;
- command/control plugin;
- package/download replacement;
- prompt/packet/journal injection;
- account names and user-provided configuration.

## Abuse cases

- local untrusted process reads token and submits intents;
- bearer token written with weak permissions;
- conflicting UUID/body attempts to replace intent;
- large/multibyte/slow request exhausts listener;
- crafted account/signal string breaks journal parsing;
- prompt evidence contains instruction injection;
- stale packet used after native state changes;
- profile update changes job cadence/model unexpectedly;
- reset script clears operator evidence while positions open;
- package version mismatch changes signal identity;
- malicious/accidental external same-instrument order is claimed as AI-owned.

# Blue-team controls

- exact local token ACL and rotation procedure;
- raw-byte/body/time/concurrency limits;
- durable expected-phase intent ownership;
- pending/native terminal state machine;
- exact account/contract/signal/correlation identity;
- native callback reconciliation before replay;
- separate product state, AI epoch, cache, and journal ownership;
- package/profile compatibility manifest and hash validation;
- repeated-reload subscription fixture;
- risk provenance and unknown-safe representation;
- append-only discrepancy/correction evidence;
- bounded Sim acceptance required before release promotion.

# Wargames

## Wargame 1 — reinstall under partial account truth

Reinstall/update while several accounts are disconnected or return missing metrics. Success: no fabricated zero equity/100% loss, real locks remain visible, and state provenance explains every row.

## Wargame 2 — duplicate AI request storm

Release 100 same-body and conflicting-body requests while forcing disk delays. Success: one durable owner, one native mutation, immutable replay/conflict, no global lock held during I/O.

## Wargame 3 — reload during active copied position

Reload repeatedly with working follower protection and a pending master reduction. Success: one owner after reload, protection remains valid, no duplicate close, no reversal.

## Wargame 4 — delayed native rejection

Return from submit successfully, then reject asynchronously or fail a bracket child. Success: pending becomes rejected/recovery, not executed; learning does not score success; attributable recovery is bounded.

## Wargame 5 — human interleave

Human adds, partially closes, fully closes, or places unrelated orders after an AI/copy entry. Success: only provably owned exposure/orders are mutated; ambiguity supersedes rather than guesses.

## Wargame 6 — profile/AddOn mismatch

Install each supported/unsupported version combination. Success: supported pairs pass; unsupported pairs fail before jobs/trading authority are enabled.

## Wargame 7 — crash and stale worker lock

Kill the direct worker during model call and intent post, then let the next cron tick run. Success: no duplicate intent/mutation and stale lock recovery is explicit.

# Ledger consolidation

The next ledger reconciliation should:

1. add `GL-STAB-01` as the first P0 stability item;
2. keep `GL-REP-01`, `GL-AI-06`, and `GL-AI-07` active, with source/current evidence corrected;
3. add `GL-REL-01` as the release-truth contract;
4. reduce simultaneous `in_progress` items by moving broad compliance, performance, docs, and architecture work to ready/backlog/deferred unless directly required;
5. preserve `GL-CTRL-01` P0 backlog behind execution/replication foundations;
6. keep Standard and Advanced acceptance evidence distinct inside the shared ledger;
7. remove or mark historical any evidence not reproducible from the named source revision;
8. never close a native item from package publication or deterministic source tests alone.

GitHub issues are implementation mirrors, not the source of status.

# Recommended execution order

1. `GL-STAB-01` read-only trace, provenance model, deterministic fixtures, then narrow fix.
2. `GL-AI-06` source/ledger/package reconciliation before behavior work.
3. `GL-REP-01` bounded native Sim fixture and fractional allocation decision.
4. `GL-AI-06` durable expected-phase owner and concurrency/crash tests.
5. `GL-AI-07` pending/native terminal callback state machine.
6. `GL-REL-01` compatibility manifest and setup enforcement.
7. Native Advanced restart and worker-overlap acceptance.
8. Only then performance/docs/compliance cleanup and release promotion.

# Questions for the operator

1. Which exact Standard and Advanced AddOn builds are installed in the current runtime screenshots?
2. Was the reinstall an over-install preserving GlitchData, a clean removal, or a package swap?
3. Were the affected PA accounts connected and returning native cash/equity at the time?
4. Which account types may be used for the first bounded native fixtures?
5. What exact fractional ratio behavior does the operator expect across separate master orders?
6. Should unknown risk fail closed for new entries while displaying `unavailable`, or remain observational for Sim?
7. Which package/profile pair should become the first locked Advanced baseline?
8. Is the current goal a user-testable Standard release first, or Standard and Advanced simultaneously?
9. Which GlitchData artifacts must survive reinstall, update, Reset Data, and epoch reset?
10. What independent native reviewer/tester is available for each product line?

# Stop line

Do not add broad features, publish a stable/live/profitable claim, or use evaluation/PA/live accounts until the relevant P0 native and release contracts pass with exact evidence.