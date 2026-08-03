# GL-AI-09 — Preserve nonterminal legacy intent outcomes

**Issue:** #10  
**Priority:** P0 correctness  
**Status:** implemented; repository verification pending

## Intent

A historical execution journal is evidence. Migration may bind it to a durable state file, but it may not strengthen that evidence. In particular, `pending` cannot become terminal `executed` merely because the journal predates the state store.

## Defect

The legacy reconstruction path previously used a two-branch table:

```text
failed → failed
anything else → executed
```

The canonical execution result contract supports `pending`, `failed`, `executed`, and `skipped`. The old migration therefore terminalized pending work and suppressed its reconciliation path.

## Fix

1. Normalize the historical executor status into one `GlitchAiExecutionResult`.
2. Pass that result through `GlitchAiIntentResultContract.GetPhase()`.
3. Build the replay response through `BuildAcceptedJson()`.
4. Fail unknown or empty legacy statuses as `legacy_execution_status_unknown`; never promote them to executed.

Known mapping:

```text
pending  → pending, nonterminal
executed → executed, terminal
failed   → failed, terminal
skipped  → executed terminal outcome with executor=skipped
unknown  → failed, terminal ambiguity
```

`skipped` remains consistent with the existing canonical result contract: it is a completed no-mutation result, not an open native lifecycle.

## Boundaries

This change does not alter:

- Hermes judgment or intent;
- risk admission;
- quantity or bracket geometry;
- account or instrument binding;
- native submit/change/cancel behavior;
- current-process callback reconciliation;
- GL-AI-07 native terminal acceptance.

## Acceptance

- pending reconstruction remains nonterminal;
- executed and failed remain terminal;
- unknown status never becomes executed;
- atomic claim/promotion and raw-byte request limits remain unchanged;
- source-contract tests pass.
