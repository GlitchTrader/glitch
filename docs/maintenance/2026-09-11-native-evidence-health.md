# Native execution evidence and worker-health repair

## Causal findings

- A retained journal may begin after a position opened. A follower's generic replication signal identifies ownership, not whether its fill opened or closed exposure. Inferring entry from signed direction can turn an orphan close into a fictitious opening trade, corrupting later cached round trips.
- Worker-health diagnostics treated only the primary master's MNQ position as positioned. MES, M2K and other bound masters could consequently receive the flat overdue allowance.
- A recent successful market-closed or native-capture admission deferral is worker activity, although it deliberately produces no model attempt. Health should distinguish that from an absent worker.

## Narrow corrections and preserved behavior

Carry the actual native OrderAction through execution lifecycle evidence and its durable serialization. Reporting validates its signed direction. Older unambiguous Hermes/protection roles retain compatibility; ambiguous legacy replication/external fills remain raw evidence but are not guessed into new positions. This does not reconstruct previously corrupted caches automatically.

Health checks every configured master and all its native position quantities. The final cycle-event read is bounded to 16KB, requires a complete valid record, current time/window and an explicitly benign reason. Capture-flat deferrals do not apply when positioned. Failed/stalled attempts and stale/unsafe skips retain alarms.

No order submission, replication, flatten, protection, risk, model, cadence, cognition, account-setting or learning-promotion changes. Old journals remain readable; new action metadata has no engine effect. No live repair or reset is hidden in application startup.

## Verification

- Failing-then-passing orphan follower and all-instrument health reproductions.
- Ledger harness covers partial fills, FIFO ownership, reversal remainder, duplicate execution IDs, orphan exits and ambiguous legacy evidence. Optional `GLITCH_LEDGER_NATIVE_EVIDENCE` mode compares real exported native facts through the production accumulator without writing runtime files.
- Journal round-trip/legacy compatibility and existing-journal host replay.
- Health harness covers bound masters, benign and unsafe skips, stale/future/wrong-window records, bounded-tail parsing, failures and active stalls.
- Full AddOn source compilation and native safety, recovery, configuration, state-machine and runtime-lifecycle harnesses.

Deployment and activation are separate checks. Install the complete AddOn folder only through the supported deployment process. Preserve pause, schedules and runtime evidence. Any cache reconciliation requires independently matched native executions, an original backup and protection against stale resident writers; it is not an epoch reset. Natural market-open execution/protection proof cannot be obtained by offline tests.
