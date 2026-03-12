---
name: glitch-route-work
description: Route work inside the Glitch monorepo. Use at the start of tasks in this repo to identify the target package, source of truth, safety bar, supporting docs, and validation or deploy workflow before editing.
---

# Glitch Route Work

## Overview

Use this skill when the request does not already map cleanly to one package, when the task spans multiple packages, or when you need to establish the right source of truth and safety bar before editing.

## Routing Decision Tree

1. If the task is in `apps/api`, treat it as production-critical backend work.
   - Read `apps/api/README.md` first.
   - Also use `glitch-api-guardrails`.
2. If the task is in `apps/website`, treat it as public marketing work.
   - Read `apps/website/README.md` first.
   - Preserve env-driven links in `apps/website/src/lib/marketing-links.ts`.
   - If the task includes docs or env-doc updates, also use `glitch-documentation-discipline`.
3. If the task is in `apps/app`, treat it as placeholder work.
   - Keep changes minimal unless the user explicitly wants to productize it.
4. If the task is in `ninjatrader/Glitch/AddOns`, `ninjatrader/Glitch/Indicators/glitch`, or `ninjatrader/Glitch/Docs`, treat it as active NinjaTrader product work.
   - Read `ninjatrader/Glitch/Docs/README.md` first.
   - Also use `glitch-addon-indicator-workflow`.
   - If localization is part of the task, also use `glitch-localization-workflow`.
   - If files need to go live in NinjaTrader, also use `glitch-deploy-workflow` and the global `deploy-glitch-safely` skill.
5. If the task is in `ninjatrader/Glitch/Strategies`, stop and confirm that strategy work is actually intended.
   - Strategies are not part of the default AddOn and Indicator scope.
6. If the task mentions a docs app, note that `apps/docs` does not exist yet.
   - Current docs live in `ninjatrader/Glitch/Docs`.
7. If the task requires direct Vercel env, cron, or deployment control, also use `glitch-vercel-operations`.
   - Default deployment is still GitHub push to trigger Vercel auto deploy.

## Shared Guardrails

- Workspace files are authoritative. Do not treat live NinjaTrader files or `GlitchData` runtime files as source of truth.
- Keep edits package-scoped. If a task crosses packages, identify the contract owner first and only widen scope deliberately.
- API is the highest-risk surface, website is production-facing, and `apps/app` is still placeholder scaffolding.
- If the task updates README files, docs, or env documentation, use `glitch-documentation-discipline`.
- Do not pull Strategies into AddOn or Indicator deployment or documentation unless the user explicitly asks for strategy work.
- If the task is unclear in a way that affects package selection or deployment risk, ask the user before editing.

## Validation Defaults

- Choose the narrowest useful validation for the affected package instead of running the whole monorepo by default.
- If validation cannot be run from the current workspace, say so explicitly in the final handoff.
