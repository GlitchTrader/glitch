# Glitch Monitor Pass — historical

> Do not execute this retired prompt. Current work lives only in `docs/ledger/ledger.json` on `main`.

Purpose: replaces the in-session cron that re-read a giant transcript every 2h. A fresh session booting from this file + the ledger costs a few thousand tokens instead of a full-history re-read. Works in Claude, Cursor, or Codex.

```text
You are running one Glitch monitor pass as acting lead. Repo: D:\ab\projects\Glitch\Glitch-Platform.
Boot: read docs/ledger/north-star.md, backlog.md, log.md (top 3 entries), and
handoffs/2026-07-07-cursor-wave1.md. Optionally d:/ab/projects/abkb/fable-findings/fable-soul.md
for operating rules. Then:
1. git fetch; git log main..glitch/bulletproof-wave1 --oneline for new commits;
   read new ledger/log entries and audits/ui-calm-changes.md changes on the branch.
2. Spot-review the 1-2 newest commit diffs ONLY for: culture handling
   (ConverterCulture/CultureInfo on user-facing parse/format), localization via
   Localization.tsv pattern, calm-by-default (no new red/orange elements outside
   Critical taxonomy), scope creep outside assigned files.
3. Violations → write numbered file:line notes to docs/ledger/handoffs/lead-review-notes.md,
   commit to main, tell the operator. Clean → one log.md line, no commit needed
   unless other changes are staged.
4. If all WO items are logged complete and the iteration is committed: declare
   readiness for Alan's NT8 compile gate (checklist = audits/ui-calm-changes.md).
5. Do NOT touch the working tree if it has uncommitted changes from another
   agent's live session. Never write under C:\Users\alan\Documents\NinjaTrader 8\.
Report compactly: what landed, review verdict, what's blocking, who's needed.
```
