# Glitch NinjaTrader Rebuild Contract

**Status:** FROZEN — operator authorized end-to-end implementation on 2026-08-05
**Branch:** `rebuild/nt-core-v1`
**Scope:** Glitch NinjaTrader Standard and Advanced only
**Out of scope:** Glitch Topstep, live-capital authorization, profitability claims, release promotion

## Purpose

Rebuild the Glitch NinjaTrader runtime from irreducible product requirements,
official NinjaTrader behavior, and the Hermes profile contract. The current UI
is a visual and interaction reference. Legacy trading, recovery, persistence,
and lifecycle code is evidence only; it is not an implementation base.

Glitch is deterministic equipment. It executes supported User or Hermes intent
through NinjaTrader. It does not invent cognition, strategy, permission, policy,
direction, quantity, geometry, timing, or management. It must not induce an
operator error through false facts, incorrect math, duplicate mutation, hidden
state, or a substituted decision.

This document prevents implementation drift. Every production behavior must
have:

1. one requirement ID in this document;
2. one named authority source;
3. one observable acceptance test;
4. one owner of mutable state;
5. explicit operator approval if it changes a frozen requirement.

Code that cannot identify all five does not enter the rebuild.

## Authority

### NinjaTrader

Official NinjaTrader documentation is authoritative for AddOn lifecycle,
threading, accounts, orders, executions, positions, OCO behavior, and native
mutation:

- [OnStateChange](https://ninjatrader.com/support/helpguides/nt8/onstatechange.htm)
- [OnWindowCreated](https://ninjatrader.com/support/helpguides/nt8/onwindowcreated.htm)
- [Account class](https://ninjatrader.com/support/helpGuides/nt8/account_class.htm)
- [Multi-threading](https://ninjatrader.com/support/helpGuides/nt8/multi-threading.htm)
- [ExecutionUpdate](https://ninjatrader.com/support/helpguides/nt8/executionupdate.htm)
- [OnExecutionUpdate](https://ninjatrader.com/support/helpGuides/nt8/onexecutionupdate.htm)
- [Order](https://ninjatrader.com/support/helpguides/nt8/order.htm)
- [OrderUpdate](https://ninjatrader.com/support/helpguides/nt8/orderupdate.htm)
- [PositionUpdate](https://ninjatrader.com/support/helpguides/nt8/positionupdate.htm)
- [AccountItemUpdate](https://ninjatrader.com/support/helpguides/nt8/accountitemupdate.htm)
- [Flatten](https://ninjatrader.com/support/helpguides/nt8/flatten.htm)
- [Native OCO behavior](https://ninjatrader.com/support/helpGuides/nt8/submitting_orders_basic_entry.htm)

If official documentation is silent or ambiguous, implementation stops at the
boundary. Legacy Glitch behavior does not fill the gap automatically.

### Hermes

These distribution-owned files define the Advanced cognition boundary:

- `hermes-profile/profiles/glitch/SOUL.md`
- `hermes-profile/skills/glitch-build-intent/SKILL.md`
- `hermes-profile/skills/glitch-runtime/SKILL.md`
- `hermes-profile/skills/glitch-trade-mnq/SKILL.md`
- `hermes-profile/README.md`

Hermes proposes structured master intent. Hermes does not own NinjaTrader
facts, followers, native mutation, protection, replication, reconciliation, or
receipts.

### Operator

The operator owns account selection, master/follower topology, ratios, manual
account metadata, explicit compliance actions, AI enablement, Sync, Flatten,
and all promotion beyond bounded Sim verification.

The operator is authoritative for user intent. A supported user command is not
subject to a Glitch opinion about whether it is wise. Glitch can report current
native facts and native executability; it cannot replace, suppress, delay, or
counter-trade the command because of inferred strategy, risk preference, firm
policy, market interpretation, or expected outcome.

### Equipment fault contract

Alan and Hermes can make cognitive or operational mistakes. Those mistakes
remain attributable to their decisions. Codex and Glitch perform deterministic
engineering and must not induce, amplify, conceal, or reinterpret them.

- Hermes is authoritative for cognition on the configured master.
- The User is authoritative for direct commands and visible configuration.
- Glitch faithfully translates supported intent into NinjaTrader operations.
- NinjaTrader supplies final native facts and rejection/finality.
- A structurally or natively non-executable command is not a Glitch cognitive
  veto. Glitch preserves the command, reports the exact unresolved fact or
  native result, and never substitutes a different trade.
- When intent is ambiguous, Glitch resolves only what the User or Hermes is
  trying to accomplish from explicit command context. It does not introduce a
  policy objective of its own. If two materially different native actions remain
  possible, it asks instead of guessing.

## Product boundary

```text
Hermes          probabilistic master cognition and intent
Glitch runtime  deterministic contract translation, native commands,
                replication, protection, correlation, receipts, projections
NinjaTrader     authoritative accounts, orders, executions, positions, OCO
UI              configuration, explicit commands, read-only runtime views
```

No component may share or silently assume another component's authority.

## Derived execution state model

This section is the normative implementation model derived from the frozen
requirements. It adds no product behavior. It removes implementation choices
that cannot be proved from NinjaTrader facts.

### One machine

The runtime contains only these mutable authorities:

1. **Native facts** — copied account, order, execution, and position values.
2. **Configuration** — the current User-owned route and opt-in control values.
3. **Operations** — immutable User, Hermes, or master-execution causes and their
   progress through native requests.

Each account/instrument has one FIFO operation queue. Replication, Sync, Hermes
entry/exit/management, protection, and explicit control actions use this same
queue. They are causes of operations, not separate trading engines.

Analytics, UI projections, journals, timers, HTTP handlers, and recovery code
cannot own an operation queue or emit a native request.

### Native facts commute

Order, execution, and position callbacks can arrive in any order. The reducer
therefore stores each immutable fact and evaluates transition predicates over
the complete stored facts. No transition is authorized because one callback
happened to arrive before another.

- An order is terminal only after a native terminal order state.
- A filled order is execution-complete only when its observed execution
  quantity accounts for the native filled quantity.
- A cancellation set is complete only when every exact targeted order is
  terminal and every filled target is execution-complete.
- A trade step is complete only when its own executions equal its requested
  signed quantity and its order is natively terminal.
- Duplicate facts are idempotent. Amend and remove operations revise evidence;
  they never create a second trade.

These predicates must produce the same result for every permutation of the
same native facts.

### Operation lifecycle

Every operation has one stable ID, one content fingerprint, one cause, one
account/instrument scope, and one terminal result:

```text
Accepted
  -> WaitingForProtectionCancellation (only when exact owned protection conflicts)
  -> Ready
  -> NativeRequestStarted
  -> NativePending
  -> Ready                         (only when a sequential next step remains)
  -> WaitingForProtection         (only for confirmed opening fills)
  -> Completed | Failed | Unknown
```

`Failed` requires native rejection or a synchronous structural/native request
failure. `Unknown` means the evidence cannot distinguish outcomes. Neither
state authorizes an automatic retry. A new mutation requires the same
idempotent unfinished operation to be provably resumable or a new User/Hermes
command.

### Signed trade delta

A trade operation owns one signed quantity. Only executions attributable to
that operation reduce its remaining quantity. Independent User executions
update native facts but do not alter, disable, complete, or countermand the
operation.

NinjaTrader reversal uses sequential native orders:

1. Read the latest native signed position.
2. If the remaining delta opposes it, submit only the quantity that closes the
   current side.
3. Wait for the exact close order's fills and terminal state.
4. Re-evaluate native position facts.
5. Submit the remaining opening quantity only after the close is complete.

The coordinator never submits close and reverse-entry orders together. A
stale position plan causes no mutation; the reducer receives the newer native
position fact and plans the same remaining operation again.

Protection is allocated only to the quantity that a native execution actually
opened. Closing fills never receive protection.

### Replication allocation

Each accepted native master execution `e` produces one immutable allocation per
enabled route. For route ratio `r`, batching-independent allocation is:

```text
master_total := master_total + signed_quantity(e)
new_target   := round_away_from_zero(master_total * r)
route_delta  := new_target - prior_target
prior_target := new_target
```

The route delta is enqueued once on the follower. Follower position snapshots
and independent follower executions are absent from this calculation.

Add, enable, ratio change, and explicit Sync are User synchronization
operations. They enqueue `round(master_position * ratio) - follower_position`
from current native facts, then start a new allocation epoch. Disable and
remove stop future allocations without a close.

The visible group topology is one level: an enabled follower cannot also be an
enabled master, and one follower cannot have multiple enabled masters. Invalid
topology is rejected as an unrepresentable net-position configuration instead
of being hidden through execution-origin filters.

### Protection ownership and races

Each confirmed opening fill creates zero or more exact protection bundles. Each
protected leg has a fresh OCO identity, exact native child identities, an entry
fill anchor, quantity, stop offset, and target offset.

- Hermes geometry is offset from the decision price to each actual master and
  follower opening fill.
- Native User master protection is mirrored from observed master geometry to
  Glitch-owned follower lots. Missing reference facts remain explicit and do
  not authorize guessed prices.
- Manual follower executions never resize, cancel, add, or change protection.
- A User close on a Hermes-owned master cancels only the Glitch-owned protection
  attached to the closed master quantity. It does not create another master
  trade. Independent follower actions never trigger this cleanup.
- A follower protective fill settles only the exact Glitch-owned protected lot.
  It creates route settlement credit in the same signed direction as the
  protective exit.
- Settlement credit consumes the next matching copied master delta in FIFO
  order. It can also consume a matching copied delta already waiting for
  protection cancellation. This is the sole duplicate-close prevention rule.
- An opposite copied delta first cancels only conflicting Glitch-owned
  protection. The trade operation proceeds only after the cancellation-set
  predicate is true. A fill during cancellation is processed before that
  predicate can release the trade.

### Correlation and recovery

Every Glitch-created native order uses a signal that contains a versioned,
stable command identity and child role within NinjaTrader's 50-character name
limit. Runtime object identity can accelerate correlation but cannot replace
the signal. Broker `OrderId` is evidence only.

The append-only journal records `Accepted` before work and
`NativeRequestStarted` immediately before a native mutator call. Recovery first
subscribes, snapshots current-session executions, orders, and positions, and
marks all snapshot executions as baseline. Baseline observations never
replicate.

- `Accepted` without `NativeRequestStarted` can resume the original operation.
- A started request with exact current native correlation is reconstructed from
  those facts.
- A started request without sufficient native correlation remains `Unknown`.
- Current-session execution collections cannot be treated as historical broker
  truth, and absence cannot prove an earlier-session request did not execute.

Startup, reconnect, and recompile do not synthesize a master execution, Sync,
entry, exit, protection change, or Flatten.

## Frozen architectural invariants

### Lifecycle

- **LIF-001** `GlitchAddOn` owns exactly one runtime generation from
  `State.Active` through `State.Terminated`.
- **LIF-002** `OnWindowCreated` and `OnWindowDestroyed` only install or remove
  UI elements. They do not create, replace, start, stop, or mutate the trading
  runtime.
- **LIF-003** Window visibility, closure, workspace restore, and NinjaScript
  recompile do not change trading intent or submit native orders.
- **LIF-004** Termination removes every native subscription and stops every
  Glitch-owned listener exactly once.
- **LIF-005** A callback from a retired runtime generation is ignored and can
  never mutate native state.

### Native event model

- **EVT-001** Use direct typed NinjaTrader account subscriptions. Reflection is
  not permitted for documented account events.
- **EVT-002** Native callbacks copy required values into immutable Glitch events
  and enqueue them. They perform no WPF, file, HTTP, waiting, or native order
  mutation.
- **EVT-003** One serialized reducer owns all mutable runtime state.
- **EVT-004** Execution create, amend, and remove operations are distinct native
  facts. Glitch updates its journal and projections from those facts. It does not
  infer a new order unless a User or Hermes command requests one.
- **EVT-005** Order finality comes only from native order events. Submit, Change,
  Cancel, and Flatten calls are requests, not completion evidence.
- **EVT-006** `Submitted`, `Accepted`, `Working`, `PartFilled`, `ChangePending`,
  `CancelPending`, `Rejected`, and `Unknown` remain distinct states.

### Mutation ownership

- **MUT-001** One `OrderCoordinator` is the only Glitch component permitted to
  call `Account.CreateOrder`, `Submit`, `Change`, `Cancel`, or `Flatten`.
- **MUT-002** UI refresh, analytics, journal rendering, risk projection, timers,
  HTTP handlers, and persistence recovery cannot submit native orders.
- **MUT-003** Every Glitch native command has one stable Glitch correlation ID.
  Broker `OrderId` is not used as permanent identity.
- **MUT-004** Duplicate commands with the same ID and content return the same
  receipt. The same ID with different content is rejected.
- **MUT-005** No timeout, missing snapshot, or pending state authorizes a second
  native mutation.

### Replication

- **REP-001** Only an accepted native master execution can create a new follower
  allocation. Configuration changes and position snapshots cannot.
- **REP-002** Each master partial execution is processed independently and
  idempotently.
- **REP-003** Ratios use deterministic cumulative integer allocation with an
  explicit fractional remainder. Rounding never depends on event batching.
- **REP-004** Disabling replication stops future allocations. It does not cancel
  existing protection or create catch-up orders.
- **REP-005** Adding or enabling a follower and changing a ratio are explicit
  User requests. Glitch applies the requested group state and synchronizes that
  follower to the current master exposure at the requested ratio. Removing or
  disabling a follower stops future replication. Glitch closes exposure only
  when the User also requests Flatten or Sync-to-zero.
- **REP-006** The visible account-group model assigns one active master route
  owner to each follower/instrument. This is explicit User topology required by
  NinjaTrader's net position model, not an inferred cognition or risk gate.
- **REP-007** A non-Glitch follower execution is independent User action. Glitch
  takes no action because of that execution. It does not disable replication,
  reconcile, resize, cancel, replace, protect, or counter-trade it.
- **REP-008** Every later accepted master execution remains a new replication
  input. Glitch copies its signed delta at the current ratio without using the
  follower's independent trade as a veto or target-position signal.
- **REP-009** Manual master and Hermes master executions use the same replication
  path. The producer does not change follower routing or allocation math.

### Protection

- **PRO-001** A User master execution is replicated when replication is enabled,
  whether or not the User attached protection. Glitch mirrors attributable
  native master protection when it exists and truthfully reports unprotected
  exposure when it does not. It never invents a stop or target. Hermes entry
  intents remain protected because protection is part of the Hermes contract.
- **PRO-002** Protection prices must preserve the User/Hermes absolute geometry
  and satisfy NinjaTrader tick and market-side requirements. Glitch never moves
  a price silently. A non-executable price returns the exact fact for correction.
- **PRO-003** Each independently protected follower fill or leg gets a fresh OCO
  ID. A terminal OCO ID is never reused.
- **PRO-004** Protection quantity is derived from confirmed follower fills, not
  requested entry quantity or a position estimate.
- **PRO-005** A master exit and a follower protective fill are serialized through
  native events. A pending protection cancellation never authorizes a duplicate
  close.
- **PRO-006** Protection rejection is explicit terminal evidence for that
  protection command. The system does not retry blindly or report protected.

### Execution fidelity

- **EXE-001** Glitch submits a supported Hermes market entry to the configured
  master as soon as it can parse the authenticated intent and resolve the native
  account and instrument. It does not apply market, strategy, risk, session,
  prop-firm, position-conflict, or profitability judgment.
- **EXE-001A** Hermes entry quantity is the literal signed market-order delta
  requested by the intent, not a target-position instruction. Existing native
  exposure changes only how NinjaTrader must sequence a reversal; it does not
  authorize Glitch to resize or reject the requested delta.
- **EXE-002** Hermes entry geometry is defined at the decision snapshot price.
  After the native fill, Glitch offsets every stop and target by the same fill
  drift. This preserves Hermes distances and leg structure.
- **EXE-003** If a future Hermes contract supplies an explicit executable entry
  range, Glitch uses that range as written. It does not invent a range.
- **EXE-004** Follower protection uses the same stop and target offsets from the
  confirmed follower fill. Follower fill drift does not change Hermes cognition.
- **EXE-005** `MOVE_STOP`, `MOVE_TP`, `EXIT`, and User commands execute promptly
  against current native state. Glitch does not delay them for a later packet or
  analytical confirmation.

### Configuration and account facts

- **CFG-001** Manual account status, firm, nominal size, route, ratio, and policy
  are configuration. Native cash value, net liquidation, PnL, position, orders,
  and connection state are observations.
- **CFG-002** Native account events never overwrite manual configuration.
- **CFG-003** Automatic account-size inference is permitted only as a visible
  suggestion or an explicit `Auto` mode. Unknown values remain unknown.
- **CFG-004** A UI edit commits configuration before a refresh can render its
  result.
- **CFG-005** Risk and compliance actions are specific, visible, persisted,
  journaled, and off by default. Names, nominal sizes, and firm labels do not
  imply permission or limits.

### Persistence and recovery

- **PST-001** NinjaTrader remains authoritative for current native orders,
  executions, positions, and OCO state. Glitch does not persist a rival broker
  state as truth.
- **PST-002** Glitch persists one versioned configuration document and one
  append-only operation/receipt journal. Replaceable projections may be rebuilt.
- **PST-002A** The operation/receipt journal is the sole durable authority for
  Hermes intent IDs and content fingerprints. Same-ID/same-content replay returns
  the existing receipt; same-ID/different-content replay is a conflict and never
  creates a second mutation.
- **PST-003** Recovery is observe-only until current native facts are correlated.
  Once correlation proves an unfinished original User/Hermes command, Glitch may
  idempotently finish only that command. Startup and reload never create a new
  thesis, catch-up trade, or unrelated mutation.
- **PST-004** Ambiguous recovery state remains `Unknown` with the exact evidence
  gap. Glitch preserves the original intent, does not claim success/failure, and
  asks for operator evidence only when deterministic native resolution is not
  possible.
- **PST-005** Every persistent field must document why NinjaTrader cannot own it,
  its single writer, and its recovery rule.

### Hermes and controls

- **AI-001** Hermes can address configured masters only. Follower identity is
  absent from the intent contract.
- **AI-002** Glitch performs only the parsing, identity resolution, deduplication,
  native translation, and correlation needed to execute the intent. It does not
  vet, score, approve, suppress, delay, or replace Hermes direction, quantity,
  geometry, timing, or management.
- **AI-003** `ENTER_LONG` and `ENTER_SHORT` are protected market intents. Up to
  three legs retain stable leg IDs and independent native OCO protection.
- **AI-004** `MOVE_STOP`, `MOVE_TP`, `HOLD`, `EXIT`, and `NOTHING` retain their
  Hermes contract meanings. Glitch does not reinterpret them. `HOLD` and
  `NOTHING` are durably receipted no-actions and emit no native request.
- **AI-005** One authenticated localhost gateway owns snapshot, intent, receipt,
  status, pause/resume, and Flatten command transport.
- **CTL-001** Flatten is an explicit operator command. Glitch calls native
  `Account.Flatten` for the selected instruments and waits for native terminal
  evidence.
- **CTL-002** Pause stops new Hermes entries. It does not remove native
  protection or rewrite existing position truth.

### Compliance and risk automation

- **CMP-001** Every compliance or risk automation is disabled by default.
- **CMP-002** The Settings panel exposes each automation as a separate opt-in
  control with its exact prop-firm rule, input facts, action, and journal result.
- **CMP-003** One triggered control performs exactly its documented action. It
  does not enable another control or start an undocumented action chain.
- **CMP-004** A control uses only the rule set that the User assigned to that
  account. Account names and balances do not silently assign rules.
- **CMP-005** Observational risk facts remain visible when automation is off.
  Observation alone never blocks, resizes, closes, or creates an order.

## Prohibited implementation patterns

The rebuild rejects these patterns even if a source test passes:

- trading runtime constructed by a Window or ViewModel;
- more than one native order mutator;
- mutable static order, intent, route, or protection state;
- reflection for documented NinjaTrader account events;
- `Dispatcher.Invoke` in native event or Hermes control paths;
- timer-driven order reconciliation or retry;
- startup/recompile catch-up mutation;
- deterministic strategy, market, account-name, firm, capacity, session, or
  profitability gates that the User did not explicitly configure;
- changing, suppressing, delaying, or counter-trading supported User/Hermes
  intent because Glitch disagrees with the cognition;
- exception swallowing in lifecycle, event, persistence, or mutation code;
- broker `OrderId` as durable identity;
- reusing a terminal OCO ID;
- inferring manual configuration from cash value or net liquidation;
- treating compile, mock tests, package hashes, or UI screenshots as native
  trading acceptance;
- adding a new store, server, worker, manager, or recovery phase without a
  frozen requirement that proves it is irreducible.

## Required acceptance evidence

| ID | Native acceptance fixture | Required result |
|---|---|---|
| ACC-LIFE-01 | 0/1/3/10 NinjaScript recompiles; open, hide, close, reopen UI | One active runtime and one subscription set; zero native orders caused by lifecycle/UI |
| ACC-EVT-01 | Duplicate execution delivery plus partial fills | Each execution operation applied once; exact journal attribution |
| ACC-EVT-02 | Execution amendment/removal after replication | Journal and projections reflect native truth; no new order without a new User/Hermes request |
| ACC-REP-01 | Ratios 0, 0.5, 1, 2 across differently batched partial fills | Same deterministic cumulative follower allocation in every batching |
| ACC-REP-02 | Add, enable, remove, disable, and ratio edit while master is exposed | Add/enable/ratio change synchronizes as the explicit request; remove/disable stops future copies without an unrequested close |
| ACC-MAN-01 | Manual follower add, partial reduction, full close, and reversal between master executions | No immediate Glitch mutation or route-state change; each later master execution still copies independently |
| ACC-PRO-01 | Hermes protected entries plus protected and unprotected User master entries | Hermes geometry is preserved; User protection is mirrored when present; absent User protection stays absent and is labeled truthfully |
| ACC-PRO-02 | Stop or target rejection and delayed cancellation | Truthful rejected/pending state; no duplicate or unprotected-success receipt |
| ACC-PRO-03 | User manually closes a Hermes-owned master while Glitch protection is working | No second master trade; only owned master protection is cancelled; copied follower close still waits for exact follower protection finality |
| ACC-RACE-01 | Master exit concurrent with follower protective fill | Final attributable allocation zero; no follower reversal; no working orphan protection |
| ACC-UI-01 | Manual size/status/firm edits during continuous AccountItem updates | Manual values persist; observed values continue updating independently |
| ACC-AI-01 | 100 same-body and conflicting-body requests for one intent ID | Exactly one mutation owner; identical replays stable; content conflict rejected |
| ACC-AI-02 | Process interruption at every intent persistence/mutation boundary | Observe-only recovery; no duplicate native mutation |
| ACC-EQUIP-01 | Identical supported User/Hermes intents under identical native facts | Identical native command plan; no inferred direction, quantity, geometry, timing, policy, or counter-trade |
| ACC-DRIFT-01 | Hermes decision price differs from master and follower fills | Every leg preserves its decision-price stop/target offsets from each confirmed fill |
| ACC-CMP-01 | Fresh install and reset with all rule observations present | Every automation is off; no observation causes an order or veto |
| ACC-CMP-02 | Enable each compliance control separately and trigger its exact rule | Only the documented action occurs once and is attributed to the User-enabled control |
| ACC-CTL-01 | One-click configured fleet Flatten | Native terminal flat and order-free result or explicit unresolved account/instrument |
| ACC-UNK-01 | Disconnect, missing account items, reset, reinstall, reconnect | Unknown remains unknown; no false safe, lock, success, or mutation |

All order-mutating fixtures are Sim-only until the operator grants separate
authority. Source tests must pass before native fixtures, but source tests do not
replace native fixtures.

## Frozen operator doctrine

The operator froze this doctrine and authorized implementation on 2026-08-05:

1. Alan and Hermes own cognition and can make attributable mistakes.
2. Codex and Glitch own deterministic engineering and must not induce those
   mistakes through defective facts, math, code, normalization, provenance,
   receipts, hidden policy, duplicate mutation, or substituted cognition.
3. The User is authoritative for direct intent and visible configuration.
4. Hermes is authoritative for cognition on the configured master.
5. Glitch executes and equips. It does not police, micromanage, fight, or
   out-think the User or Hermes.
6. Glitch does not reinvent NinjaTrader or create a competing source of native
   truth.
7. When ambiguity remains, implementation starts from the explicit outcome the
   User or Hermes is trying to produce. Glitch asks only when materially
   different native actions remain possible after that context is applied.
8. Reset, deployment, bounded Sim setup, account reset, Journal reset, and
   operator-assisted computer testing are available when required by a named
   acceptance fixture. They do not imply live-capital authority or continuous
   Codex operation.

## Change control

- The operator freezes this contract before production implementation begins.
- A frozen requirement changes only through an explicit decision recorded in
  this document before its code changes.
- Each implementation change names its requirement and acceptance IDs.
- Each vertical slice has one native mutation path and a small reviewable diff.
- Material implementation receives an independent contract review before merge.
- No deployment, release, live-account test, or ongoing monitoring is implied by
  implementation work.

## Definition of done

The rebuild is done only when:

1. every frozen requirement has implementation and test traceability;
2. every required source test passes;
3. every named NinjaTrader Sim acceptance fixture has recorded actual evidence;
4. reload and UI lifecycle cannot create more than one runtime generation;
5. final configured accounts are flat and order-free after the bounded fixture;
6. no unresolved behavior is described as stable, complete, safe, or recovered;
7. the operator approves cutover from the legacy runtime.
