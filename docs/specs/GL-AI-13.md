# GL-AI-13 — Define bounded semantic NT management operations for Hermes

Issue: #18
Priority: P2
Status: Capability matrix reconciled; native acceptance pending

## Intent

Determine whether Hermes should receive bounded semantic tools for NinjaTrader-native trade management and, if so, define the smallest safe contract.

## Current code surface

The current NT gateway supports market entry, native stop/target protection, native OCO linkage when both protection prices are submitted, explicit stop/target changes by leg, cancellation, and account flatten. Hermes currently emits ENTER_LONG, ENTER_SHORT, HOLD, MOVE_STOP, MOVE_TP, EXIT, and NOTHING. No distinct trailing-stop or ATM/template command is present.

## Proposed investigation

Produce a capability matrix for account/position/order inspection, OCO bracket create/verify/replace, trailing-stop management, and ATM/template operations. A trailing operation must specify activation/offset/step, side and leg identity, idempotency, native receipt/reconciliation, and tighten-only/no-widening behavior. Treat OCO as an execution relationship. Treat ATM/template behavior as a composite native policy: expose it only if it is allowlisted, versioned, previewable, and auditable; otherwise document it as unavailable to Hermes.

If approved, define corresponding semantic tool guidance in the Hermes profile and management-method fields for learning. Hermes must call only Glitch-owned validated operations; it must never receive raw NinjaTrader mutators.

## Reconciled capability matrix

| Capability | Current boundary | Hermes exposure | Acceptance gap |
|---|---|---|---|
| Account/position/order inspection | Native read-only facts | Context only | Reconnect/restart fixtures |
| Market entry with stop/target OCO | Glitch-owned validated intent | `ENTER_LONG` / `ENTER_SHORT` | Native receipt and partial-failure fixtures |
| Move stop by leg | Leg-identified semantic update | `MOVE_STOP` | Exact preview, tighten/widen classification, idempotency |
| Move target by leg | Leg-identified semantic update | `MOVE_TP` | Exact preview, idempotency, native acknowledgement |
| Exit / flatten | Glitch-owned validated exit | `EXIT` and explicit Flatten All UI | Partial failure and restart/reconnect evidence |
| Trailing stop | Not exposed | None | No raw or opaque mutator is permitted |
| Breakeven/autobreakeven | Not exposed | None | No deterministic strategy behavior is permitted |
| ATM/template operations | Not exposed | None | Require allowlisted, versioned, previewable, auditable native contract |

The matrix is documentation of the existing bounded surface, not a claim that
native acceptance is complete. No ATM, trailing, breakeven, or raw mutator was
added.

NT reference points for the design review:

- `SetTrailStop()` is a managed-strategy trail, amended according to the parent strategy's calculation/update behavior, and cannot be used concurrently with `SetStopLoss()` for the same position: <https://ninjatrader.com/support/helpguides/nt8/settrailstop.htm>
- ATM is a collection of user-defined stop-loss and profit-target management rules, and can be selected from named templates: <https://ninjatrader.com/support/helpguides/nt8/atm_strategy.htm>
- ATM stop/target orders are OCO by default, while OCO behavior and recovery characteristics depend on the connection/order location: <https://ninjatrader.com/support/helpguides/nt8/faq.htm> and <https://ninjatrader.com/support/helpguides/nt8/where_do_your_orders_reside_.htm>

## Boundaries

- No strategy adoption, strategy selection, or hardcoded formula.
- No arbitrary ATM/template names or opaque native behavior.
- No bypass of Glitch validation, account/leg identity, protection policy, callbacks, reconciliation, or receipts.
- No live activation in this design ticket.

## Acceptance

- NT documentation and current gateway behavior are recorded in the capability matrix.
- Each proposed operation has schema, preconditions, identity/idempotency rules, failure behavior, and receipt expectations.
- Sim fixtures cover duplicate requests, partial brackets, stale state, missing legs, native rejection, reconnect, and restart.
- Unsupported or unsafe ATM behavior is explicitly marked not exposed.
