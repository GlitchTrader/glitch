# Glitch Platform Agents

This repo is shared between multiple agents. Work like a responsible teammate.

- Read before coding.
- Take ownership of the files you touch.
- Prefer direct, elegant solutions over clever or overengineered ones.
- Avoid arbitrary refactors, hidden scope creep, and cosmetic churn.
- Clean up after yourself: keep docs, env examples, and guardrails aligned with behavior changes.
- Keep secrets out of commits and out of terminal summaries.
- Ask the user when something important is unclear or risky instead of guessing.

## Project Map

- `apps/api`: Critical backend for licensing, Whop webhooks, admin/internal endpoints, and market data. Highest safety bar in the repo.
- `apps/website`: Public marketing site. Production-facing copy, pricing, affiliate, legal, and env-driven Whop links.
- `apps/app`: Placeholder Next.js app. Keep it minimal unless the user explicitly wants product work there.
- `ninjatrader/Glitch/AddOns/GlitchAddOn`: Active NinjaTrader AddOn source.
- `ninjatrader/Glitch/Indicators/glitch`: Active NinjaTrader indicator source.
- `ninjatrader/Glitch/Docs`: Current code-derived docs source. There is no `apps/docs` app yet.
- `ninjatrader/Glitch/Strategies`: Research or legacy area. Ignore by default unless the user explicitly asks for strategy work.

## Operating Rules

- Workspace files are the only source of truth.
- Durable current work lives only in `docs/ledger/ledger.json` on `main`. Backlog and queue are states in that ledger; roadmap, Git history, release records, and audits are not competing work lists.
- Create or update a ledger item only when coordination must survive the session, cross a role or dependency, require review or approval, remain blocked, or carry material risk. Bounded work completed and verified in one interactive session needs no ticket.
- Never directly edit `C:\Users\alan\Documents\NinjaTrader 8\bin\Custom`.
- Treat `GlitchData` runtime files as state or sparse overrides, not source templates.
- Treat customer-facing NinjaTrader export zips as release artifacts. Inspect them before publish and prefer bundled code fallbacks over manual post-export zip mutation for required static data.
- Keep changes package-scoped. If a task crosses packages, identify the contract owner first.
- API changes must preserve auth, licensing, rate limits, nonce validation, and documented env names unless the user explicitly wants a contract change.
- Website changes must preserve env-driven Whop links and public-facing copy intent.
- Docs must be derived from code that exists today. Do not document `apps/app` as a real product, and do not assume an `apps/docs` app exists until it actually does.
- Strategies are not part of normal AddOn or Indicator maintenance and should never be bundled into deploys unless explicitly requested.

## Patch finish

When a bounded patch should be visible: skill `ab-patch-finish` → `abkb/projects/glitch/deploy-routine.md` (full AddOn folder deploy, then NT recompile). Commit in this repo before saying done.

## Branching (NinjaTrader product)

- **`main`:** production web surfaces, explicit release catalog, and the canonical work ledger.
- **`standard/20`:** maintained Standard v0.0.2.0 source.
- **`ai/22`:** maintained Experimental AI v0.0.2.2 source and bounded follow-on verification.
- The old `glitch/ai-rail` branch is historical. Do not put new work there.
- Confirm `git branch --show-current` before editing AddOn/Indicator code.

## Coding discipline

Ponytail (lazy senior dev): `d:/ab/projects/abkb/knowledge/llm/engineering-discipline.md` + `d:/ab/.cursor/rules/ponytail.mdc`. Repo-specific: workspace-only edits; never `bin\Custom`; smallest scoped diff per package map above.

## Vercel

- Default deployment path: push to GitHub and let Vercel auto-deploy.
- Direct Vercel operations are still valid when env vars, cron rollout, or urgent deployment control are needed.
- Use credentials from `.env.codex.local`; never print or commit their values.
- The repo root `.vercel/project.json` is currently linked to the API project.
- On this Windows machine, prefer `cmd /c npx vercel ...` over raw `npx vercel ...` because PowerShell execution policy may block `npx.ps1`.
- Cron definitions live in `apps/api/vercel.json`. To change cron behavior, edit that file and deploy the API project.

## Localization

- `ninjatrader/Glitch/AddOns/GlitchAddOn/Resources/Localization.tsv` is the only localization source of truth.
- Preserve UTF-8. Do not use Excel or any ANSI or Windows-1252 round-trip.
- `C:\Users\alan\Documents\NinjaTrader 8\GlitchData\Localization.tsv` is sparse runtime override only, never a second full catalog.
- After localization edits, verify `zh-CN` still contains CJK and `ru-RU` still contains Cyrillic.

## Shared Agent Set

These are the same operating concepts expressed through different agent mechanisms: Codex skills for task-triggered workflows, Cursor rules for always-on or file-scoped guidance, and `CLAUDE.md` plus this file for Claude Code.

### Codex skills

- `glitch-route-work`: Route repo tasks to the right package and workflow.
- `glitch-api-guardrails`: Guard critical `apps/api` work.
- `glitch-web-workflow`: Handle `apps/website` and `apps/app` correctly.
- `glitch-addon-indicator-workflow`: Handle AddOn, Indicator, and NinjaTrader product code.
- `glitch-documentation-discipline`: Keep docs code-derived and scoped.
- `glitch-localization-workflow`: Protect localization edits and validation.
- `glitch-deploy-workflow`: Deploy NinjaTrader workspace files safely into live paths.
- `glitch-ninjatrader-packaging`: Package compiled NinjaTrader exports, bundled fallbacks, and customer download artifacts safely.
- `glitch-vercel-operations`: Handle direct Vercel env, deploy, and cron rollout work.

### Cursor rules

- `.cursor/rules/glitch-working-style.mdc`
- `.cursor/rules/glitch-monorepo-boundaries.mdc`
- `.cursor/rules/glitch-api-critical.mdc`
- `.cursor/rules/glitch-web-stack-boundaries.mdc`
- `.cursor/rules/glitch-ninjatrader-boundaries.mdc`
- `.cursor/rules/glitch-documentation-discipline.mdc`
- `.cursor/rules/glitch-localization.mdc`
- `.cursor/rules/glitch-deploy-safely.mdc`
- `.cursor/rules/glitch-ninjatrader-packaging.mdc`
- `.cursor/rules/glitch-vercel-operations.mdc`

### Claude

- `CLAUDE.md` mirrors the operating contract for Claude Code.

## Skill Routing

- Start with `glitch-route-work` for any new, ambiguous, or cross-package task.
- Add `glitch-api-guardrails` for anything in `apps/api`.
- Add `glitch-web-workflow` for `apps/website` or `apps/app`.
- Add `glitch-addon-indicator-workflow` for AddOn, Indicator, persistence, localization, or NinjaTrader docs work.
- Add `glitch-documentation-discipline` when updating READMEs or docs.
- Add `glitch-localization-workflow` for `Localization.tsv`, localization services, or language-switcher UI work.
- Add `glitch-deploy-workflow` when NinjaTrader workspace files must go live.
- Add `glitch-ninjatrader-packaging` when exporting NinjaTrader zips, bundling `PropFirmRules.json`, inspecting release archives, or publishing customer-facing download packages.
- Add `glitch-vercel-operations` for direct Vercel operations.

Keep this file, the Codex skills, the Cursor rules, and `CLAUDE.md` aligned as the repo evolves.
