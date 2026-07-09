# Glitch — Branching & Release Doctrine

**Audience:** private maintainers and agents only.  
**Effective:** 2026-07-09 · **Baseline:** v0.0.1.9 on `main`

## Intent

Users download **stable non-AI Glitch** from `main` while the operating-system rail (v20→v30) ships on a **long-lived feature branch**. Bug fixes never wait on AI work; AI work never blocks user patches.

```text
main          → v0.0.1.x  Trust · non-AI operator · public download zips
glitch/ai-rail → v0.0.2.x+  R01–R23 implementation until promoted merge
```

---

## Branches

| Branch | Purpose | Version line | Who downloads |
|--------|---------|--------------|---------------|
| **`main`** | Shipped product + non-AI patches | `v0.0.1.9`, `v0.0.1.10`, … | Yes — `apps/download` zips from here |
| **`glitch/ai-rail`** | Operating-system rail (snapshots, Hermes bridge, AI path) | `v0.0.2.0` … `v0.0.3.0` | No — internal until merge |

**Branch name:** `glitch/ai-rail` (create from `main` after v0.0.1.9 ships; all R01+ **code** lands here).

Planning docs (`docs/ai-program/*`, rail spec) may exist on both branches; **product code for R01+ commits only on `glitch/ai-rail`** unless cherry-picking a isolated fix back to `main`.

---

## Rules

### On `main` (allowed)

- v0.0.1.x patch releases (bug fixes, calm UI, replication, journal, analytics **without** AI servers/Hermes execution path)
- Download zip + checksums + `release-dates.json`
- Docs/ledger updates for closed v19 items and user-facing notes
- Hotfixes cherry-picked **from** `glitch/ai-rail` only when the change is strictly non-AI and independently verifiable

### On `main` (forbidden)

- `GlitchExternalTelemetryServer`, `GlitchAiIntentServer`, `GlitchAiOrderExecutor`, snapshot writers for Hermes, or any POST `/intent` path
- Partial AI surfaces that change AddOn behavior for download users
- Merging `glitch/ai-rail` WIP “just to sync docs” with half the rail

### On `glitch/ai-rail` (allowed)

- R01–R23 per `docs/ai-program/operating-system-rail.md`
- Hermes-side contracts and schemas
- Experimental bridge/multi-asset work
- Fail-fast eval experiments (operator machine only)

### On `glitch/ai-rail` (forbidden)

- Bumping `apps/download` public zips (users stay on main)
- Rewriting Honest Copy / replication on main without cherry-pick plan

---

## Version numbering

| Line | Example | Meaning |
|------|---------|---------|
| **main** | `v0.0.1.9`, `v0.0.1.10` | Trust baseline + patches |
| **ai-rail** | `v0.0.2.0` (Eyes), … `v0.0.3.0` (Learn) | AI program rail labels |

**Do not** ship `v0.0.2.x` download zips from `main` for user bug fixes — use **`v0.0.1.10+`** on `main`. The `0.0.2.x` series is reserved for the AI rail branch until a deliberate promotion merge.

---

## Workflows

### User reports bug (production)

```text
branch: main
fix → F5 → export Glitch_v0.0.1.N+1.zip → checksums → commit → push main
```

No AI branch required. If the fix already exists on `glitch/ai-rail`, cherry-pick the **minimal non-AI commit** onto `main`.

### Rail step (R01, R02, …)

```text
branch: glitch/ai-rail
implement → F5 on operator NT → commit → push glitch/ai-rail
```

### Promote AI to users (future)

```text
glitch/ai-rail complete through agreed gate (e.g. R16 + operator sign-off)
→ merge to main
→ first public AI-capable zip uses v0.0.2.x (or agreed version)
→ update download manifest on main only at promotion time
```

Until promotion, Hermes + AI path run from **operator builds** off `glitch/ai-rail`, not from the public download page.

---

## Agent discipline

- **Cursor / implementers:** confirm branch before first edit (`git branch --show-current`).
- New session for rail work: checkout `glitch/ai-rail`, pull, implement next R step.
- Patch session for user bug: checkout `main`, pull, minimal diff.
- Commit messages: `GL-0XX:` or `R01:` on ai-rail; `v0.0.1.10:` on main patches.

---

## References

- `docs/ai-program/operating-system-rail.md`
- `docs/ledger/backlog.md`
- `docs/ledger/north-star.md`
- `.codex/skills/glitch-ninjatrader-packaging` — zips on `main` only unless release owner says otherwise
