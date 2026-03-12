---
name: glitch-addon-indicator-workflow
description: Handle NinjaTrader AddOn, Indicator, and code-derived docs work safely. Use for ninjatrader/Glitch/AddOns, Indicators/glitch, or Docs tasks; Strategies stay out of default scope unless explicitly requested.
---

# Glitch Addon Indicator Workflow

## Overview

Use this skill when the request targets the active NinjaTrader product surface: the AddOn, the Glitch analytics indicator, or the code-derived docs that describe them.

## Scope

- In scope:
  - `ninjatrader/Glitch/AddOns/GlitchAddOn/**`
  - `ninjatrader/Glitch/Indicators/glitch/**`
  - `ninjatrader/Glitch/Docs/**`
- Out of default scope:
  - `ninjatrader/Glitch/Strategies/**`

If a task is about Strategies, treat that as explicit opt-in work instead of part of normal AddOn and Indicator maintenance.

## Read First

- `ninjatrader/Glitch/Docs/README.md`
- `ninjatrader/Glitch/Docs/architecture.md`
- `ninjatrader/Glitch/Docs/persistence.md`

Read these when relevant:

- Analytics bridge or signal flow: `ninjatrader/Glitch/Docs/data-flow-and-bridge.md`
- AddOn internals: `ninjatrader/Glitch/Docs/addon.md`
- Indicator internals: `ninjatrader/Glitch/Docs/indicator.md`
- API-backed licensing or fundamentals: `ninjatrader/Glitch/Docs/api-reference.md`

## Guardrails

- `D:\click-blue\trading\glitch-platform\ninjatrader\Glitch` is the source of truth.
- Never directly edit `C:\Users\alan\Documents\NinjaTrader 8\bin\Custom`.
- Treat `GlitchData` files as runtime state or overrides, not source templates.
- The AddOn and Indicator communicate across an assembly boundary through reflection. Type names and bridge compatibility matter.
- Docs in `ninjatrader/Glitch/Docs` are code-derived. Do not invent undocumented behavior or silently widen scope to Strategies.
- If the task includes deployment into live NinjaTrader folders, also use `glitch-deploy-workflow` and the global `deploy-glitch-safely` skill.
- If localization is part of the task, also use `glitch-localization-workflow`.

## Workflow

1. Classify the work as AddOn, Indicator, Docs, or explicit Strategy work.
2. Read the narrowest relevant docs and code before editing.
3. Make workspace changes only.
4. If docs change, tie them to code that exists now.
5. If live deployment is required, validate workspace changes first, then deploy once with the approved deploy skill.

## Validation

- Use targeted inspection and diff validation when there is no local build path available from the repo.
- If runtime verification requires launching NinjaTrader, say so explicitly in the handoff.
