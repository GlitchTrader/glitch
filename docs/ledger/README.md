# Glitch Ledger

Workframe-style program ledger for Glitch. Seeded 2026-07-07 from operator dictation (Alan) via Fable session; see `d:/ab/projects/abkb/fable-findings/projects/glitch.md` for the full wargame.

- `north-star.md` — program sequence and invariants
- `now.md` — current execution slice, parallel lane, and stop lines
- `backlog.md` — GL-xxx items with status; human-readable source of truth until a `backlog.json` + verify tooling exists

Every PM or worker session reads `now.md`, then selects one named item from `backlog.md`. `operations/pm/queues.json` may dispatch that item to a worker but may not invent a competing priority.

Discipline (borrowed from workframe `docs/ledger/`): verify-first (evidence before patching), one GL item per commit when possible, no drive-by refactors, statuses only flip with evidence.
