# GL-AI-11 — Expose execution drift and protection coverage diagnostics

Issue: #16
Priority: P1
Status: Read-only projection landed; native acceptance pending

## Intent

Provide Hermes and the operator with a read-only projection of whether the native execution and protection state matches the intended state.

## Current gap

Glitch owns native protection and reconciliation, but the current cognition contract lacks a compact diagnostic view of coverage, identity, drift, and lifecycle state. Missing or stale information must remain uncertainty, not be converted into a directional conclusion.

## Proposed change

Expose position quantity, stop/target presence, protected and unprotected quantity, leg/order identity, decision/fill anchors, submission and fill delay, requested-versus-native drift, native lifecycle/protection status, and attribution status. Preserve partial, race, rejection, stale, reconnect, and restart states.

## Landed projection

Completed outcomes now add `execution_diagnostics.intent_fidelity` with the
intent identity, decision/submission/fill prices, signed adverse drift in
native ticks, lifecycle timing, protection coverage, unprotected quantity,
native state, and management history. Missing native acknowledgement remains
`unavailable_native_receipt`; it is never upgraded to success by inference.
The projection is additive and read-only.

## Boundaries

- Read-only diagnostics only.
- No new trading gates, automatic flattening, or strategy logic.
- No claim of protection or execution success without authoritative native evidence.

## Acceptance

- Diagnostics remain explicit and identity-bound through partial fills, races, rejection, reconnect, and restart.
- Serialization is stable for missing and warming states.
- Tests prove the diagnostic projection cannot mutate native state.
