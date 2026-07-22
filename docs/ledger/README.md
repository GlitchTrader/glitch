# Glitch Work Ledger

`docs/ledger/ledger.json` on `main` is the project's one canonical current-work artifact. This AI source branch deliberately contains no second ledger, queue, backlog, now file, or status log.

Read current work with:

```powershell
git show origin/main:docs/ledger/ledger.json
```

Route durable lifecycle changes through the Glitch PM/default branch. `north-star.md` and the AI roadmap express intent; `branching.md` and the release catalog define source and publication truth; Git, releases, audits, research, and historical handoffs preserve evidence and history.

Select one dependency-clear `ready` item, act inside its stop line, verify acceptance, and record evidence plus one lifecycle transition. If no eligible item exists, stop cleanly.
