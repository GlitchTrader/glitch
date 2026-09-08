# Native protection and flatten incident — 2026-09-08

## Authorized repair

Repair the unprotected M2K entry and stuck flatten/replication failure chain. Keep
cognition, price geometry, risk settings, replication ratios, AI pause state,
learning, journal history, and unrelated working-tree changes intact. No epoch
reset, SIM reset, new trading rule, implicit NinjaTrader restart, or automatic
market-order retry.

## Evidence and cause

At 15:54:22 UTC, intent `1d98ead6-0d79-5107-9115-8c38a3970799` opened the M2K
master short and its followers. The requested target, 2969.45, was not executable
on the native 0.1 tick grid. The gateway checked this only after entry. While
building protection, it created the stop before checking the target. Target
validation then threw before the batch was submitted, stranding an Initialized
stop. This happened on master and followers.

Safety flattens were requested immediately. NinjaTrader left the unsubmitted
stops CancelPending and logged "Close operation failed. Operation timed out."
The reducer never received that asynchronous failure, so its flatten remained
NativePending. Subsequent Flatten All calls were suppressed, and the pending
flatten also blocked ordinary replicated closes. Hermes did issue EXIT requests;
that was not proof that native accounts actually closed.

Evidence: native `operations.v5.jsonl`, NinjaTrader's 2026-09-08 log, Hermes
outbox `20260908T1553Z`, and native portfolio snapshots. Before editing, the
affected installed source matched source HEAD `0b917a1`. The relevant original
paths trace to `5369ccf9`; reverting the latest cognition update would not remove
this native failure chain.

## Smallest change and protected behavior

- Carry the existing, durable intent-derived protection offsets to native entry
  preflight. Reject nonrepresentable stop/target offsets before CreateOrder; do
  not silently round or change Hermes geometry. Record a terminal failed receipt.
- Check every protection price before creating any child order. A bad later
  target must leave zero Initialized children behind.
- Observe a nonterminal flatten after one ten-second deadline as Unknown, never
  as flat. Allow a new explicit flatten request; do not retry automatically or
  reuse an old signed close quantity. Retain native tracking for late completion.
- Keep the account mutation fence until native flat-and-order-clear evidence,
  including safety-generated flattens and recovery. An old failure cannot remove
  a newer pending flatten's reducer barrier. Dispose deadline timers on completion,
  replacement, or gateway retirement.

Manual replication without an AI bracket, valid long/short brackets, OCO behavior,
partial fills, reversal sequencing, manual protection edits, and the replication
toggle retain their existing semantics. Journal format and native command
fingerprints are unchanged; preflight offsets are reconstructed from durable
intent inputs during replay.

## Validation and limits

- New harness executes the actual gateway and reducer against native-boundary
  doubles (no live orders): 26 assertions cover the incident, all-leg preflight,
  valid 0.1/0.25-tick brackets, manual copy/close, timeout/retry, late failure and
  completion, pending cancellation, disposal, and a retiring UI notice subscriber.
- The same incident test fails against original HEAD before this patch.
- Native acceptance suite plus source/control/replication checks: 68 tests pass,
  including six C# harness/compile checks and the new gateway safety harness.
- Full existing journal host replay: 36,339 records, 1,952 emissions, 2,067 command
  identities; zero unjournaled pending commands. Read-only; no replayed orders sent.
- Installed 95-file AddOn and incident logs preserved under the verified local
  checkpoint `D:/ab/checkpoints/glitch-native-safety-20260908-fabbbf08`.

Compilation and doubles do not prove live native behavior. Existing CancelPending
orders are not erased by this source fix. At 16:37 UTC all accounts were flat,
but four M2K orders remained CancelPending across the three SIM accounts. AI was
paused. Resume requires a loaded fixed generation, native order-clear evidence,
and explicit authorization to resume; never call a source copy live verification.

Rollback is the checkpoint/source baseline, not deletion of trading history. The
baseline contains the incident defect: do not restore it and re-enable trading.

Native API references: [CreateOrder](https://ninjatrader.com/support/helpGuides/nt8/createorder.htm),
[Flatten](https://ninjatrader.com/support/helpguides/nt8/flatten.htm), and
[order states](https://ninjatrader.com/support/helpguides/nt8/order.htm).
