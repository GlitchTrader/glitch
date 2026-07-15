---
name: glitch-escalate-to-codex
description: Turn a Hermes supervisor finding into a bounded, approval-gated Codex build request.
---

# Escalate to Codex

Use only from Hermes chat when analysis shows a source-controlled change is
needed.

1. Record the finding in the Hermes supervisor observations stream.
2. Append one `glitch.supervisor.build_request.v1` record with status `proposed`.
3. State the exact files/scope, acceptance criteria, evidence, and rollback.
4. Wait for explicit user approval before changing status to `approved`.

Codex's two-hour builder pass reads approved requests, works in the registered
workspace, validates, and records its result. It does not run trading cycles,
poll market data, or infer approval from a recommendation. Hermes trading keeps
operating independently while a request is pending or being built.
