# Glitch PM entry

1. Read `docs/ledger/now.md`.
2. Read the named `GL-*` row in `docs/ledger/backlog.md` and its canonical rail specification.
3. Read `operations/pm/queues.json` for the current worker handoff.
4. Inspect fresh source/runtime evidence before acting.
5. Perform one bounded item, run its acceptance checks, record evidence in the source ledger, and stop or hand off.

The backlog owns priority. This directory is a heartbeat and dispatch surface, not a second backlog. Trading, credentials, live promotion, and risk-policy changes retain their repository and human gates.
