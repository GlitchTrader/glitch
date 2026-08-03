# GL-CTRL-02 — Durable content-bound control commands

## Purpose

A control command represents one authenticated human or Hermes request to change
trading pause state, replication desired state, or request Flatten All. Glitch
must execute exactly that command at most once, never reinterpret it, and never
claim a native outcome before it is proved.

This rail changes no market strategy, risk preference, prop-firm policy,
account selection, order geometry, replication ratio, or trade intent.

## Command identity

Canonicalize only the fixed command contract fields:

```text
schema_version
command_id
action
```

Store a SHA-256 body hash in the durable receipt.

- same ID + same canonical body: observe/replay the authoritative receipt;
- same ID + different body: `command_conflict`, HTTP 409, zero mutation;
- invalid ID/action/schema: reject before receipt creation;
- no native/UI work while the short receipt lock is held.

## Receipt lifecycle

```text
applying → applied
         → rejected
         → failed
         → pending
```

Every receipt records command ID, body hash, normalized action, desired state,
status, nonsecret message/evidence, and timestamp. Existing v2 receipts migrate
or reconcile conservatively; they must never authorize a new mutation merely
because a hash is absent.

## Action reconciliation

### Trading pause/resume

Persist the desired pause state. An interrupted `applying` receipt is `applied`
only when durable/current control state equals the command. Reapplying the same
boolean state is idempotent and must not create another transition callback.

### Replication on/off

Persist the desired replication state and compare both desired and effective
state. The command may only change replication configuration. It must not call
Sync, create catch-up entries, repair positions, or submit an order. If desired
state matches but effective state cannot yet be proved, remain pending with
named evidence.

### Flatten All

Pause trading before requesting flatten. A synchronous launcher result is not
terminal evidence. Terminal `applied` requires an exact snapshot of every
eligible configured account showing:

- net position zero for every exact instrument;
- no working or cancel-pending order;
- no unresolved account snapshot.

If snapshotting fails or an account remains exposed/order-bearing, persist
`pending` with the unresolved account/instrument evidence. On restart or same
command replay, reconcile read-only; never blindly issue a second flatten from
an ambiguous receipt.

## JSON contract

Use the repository's safe JSON value writer/serializer. Delete the incomplete
manual quote helper from response construction. Exception/control messages with
quotes, backslashes, tabs, CR/LF, and control characters must produce valid JSON
without exposing stack traces or secrets.

## Tests

- 100 concurrent same-ID/same-body claims invoke the action once;
- same ID/different action conflicts with zero second mutation;
- crash injection after claim, desired-state write, callback invocation, native
  completion observation, and final receipt write;
- trading and replication reconciliation from every nonterminal state;
- Flatten All flat/order-free, still-exposed, working-order, snapshot-failure,
  restart, and replay fixtures;
- JSON control-character round trip;
- source contract proving replication control never invokes Sync or order APIs.

## Completion

Code-complete requires deterministic concurrency/crash tests and green shared,
AI, release, and package source contracts. Native Sim reload/callback fixtures
remain external; the ledger must record `code_complete_external_acceptance`, not
live/evaluation readiness.
