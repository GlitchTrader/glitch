# Lead review notes (Fable monitor)

Append-only. Cursor/operator writes when wave work needs lead eyes; Fable 2-hourly monitor appends violations.

> Historical wave record. Current status is in `now.md` and `backlog.md`; unchecked items below are not automatically active work.

## 2026-07-07 — Wave 1 post-compile (for Fable)

**Branch:** `glitch/bulletproof-wave1`  
**Operator:** Alan compiled successfully after `aa251da`; UX smoke found layout gaps.

### Status

| Item | Verdict |
|------|---------|
| Compile | **Pass** (Alan F5 after `aa251da`) |
| GL-010…GL-019 code landed | **Partial** — not `done` until acceptance smoke |
| GL-011 first implementation | **Superseded** — row-swap + fixed bottom tier rejected by operator |
| GL-011 iteration 2 | **Pending Alan recompile** — collapsed Connected Accounts expander, single star row |
| Journal expander headers | **Bug fixed in workspace** — was TextBlock-as-Header (NT shows type name) |

### Operator feedback (actioned in workspace, not yet committed)

1. **Dashboard:** Followers must own vertical space; connected accounts must not sit in a fixed bottom tier that grows with account count. Fix: collapsed expander + `MaxHeight` scroll inside expander.
2. **Journal:** `System.Windows.Controls.TextBlock` in expander chrome = unfinished UI; tight stacking. Fix: `BindLocalizedHeader`, bottom stack spacing, drop star row on critical grid.
3. **Performance (2026-07-07):** NT slowdown/crash pressure — runtime audit + tab-gated refresh, throttle account item updates, single page-scroll accordion, shell publish coalesce. See `audits/runtime-performance-audit.md`.
4. **Performance hardening PA-1…PA-9 (2026-07-08):** landed + **Alan F5 pass**. Accordion 40px custom expander template, scrollbar track fix, PA hardening per `performance-hardening-pa1-pa9.md`.

### Monitor checks

- [x] Alan compile on PA hardening batch (2026-07-08).
- [ ] Flatten All journal metric smoke (`METRIC|flatten_submit_ms`).
- [ ] Next commits on branch should reference iteration 2 if committed separately from wave1 batch.
- [ ] Do not flip backlog to `done` without Alan acceptance lines in `ui-calm-changes.md`.
- [ ] GL-014 remains design-only; GL-015 Max DD/L/C read-only gap still honest.
- [ ] LANE-1 replication audit still queued (culture binding noted in pass 6).

### Honest gaps (unchanged)

- Dashboard NetLiq orange warning styling (GL-012 adjacent).
- Lucid per-program consistency split (GL-018 schema limit).
- Replication gating from `copyTradingPolicy` (GL-019 foundation only).
