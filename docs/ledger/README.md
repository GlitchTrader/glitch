# Glitch Work Ledger

`ledger.json` on `main` is the project's one canonical current-work artifact.

- Rail is the protocol.
- Backlog and queue are item states or generated views, never separate files.
- `north-star.md` and the AI roadmap express durable intent.
- `branching.md` and the release catalog define maintained source and publication truth.
- Git, releases, audits, research, and historical handoffs preserve evidence and history.

Agents select one dependency-clear `ready` item, inspect its named source, act inside its stop line, verify acceptance, and record evidence plus one lifecycle transition. If no eligible item exists, stop cleanly without manufacturing work or status prose.

The maintained code lanes are `standard/20` and `ai/22`, but they do not own separate ledgers. AI-lane agents read the canonical file with `git show origin/main:docs/ledger/ledger.json` and route durable state changes through the default branch.
