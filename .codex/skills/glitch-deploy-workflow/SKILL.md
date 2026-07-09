---
name: glitch-deploy-workflow
description: Deploy Glitch NinjaTrader workspace files safely. Use when AddOn or Indicator changes in the workspace must be copied into the live NinjaTrader bin/Custom tree.
---

# Glitch Deploy Workflow

## Overview

Use this skill when NinjaTrader workspace edits need to go live. The deployment model is always workspace-first, validate-first, then one approved copy step.
For AddOn deploys, live deployment is full-folder only: copy the entire `GlitchAddOn` workspace folder, never individual AddOn files.

## Guardrails

- Source of truth: `D:\ab\projects\glitch\Glitch-Platform\ninjatrader\Glitch`
- Never edit or patch `C:\Users\alan\Documents\NinjaTrader 8\bin\Custom` directly.
- AddOn live deploys must copy the full `ninjatrader\Glitch\AddOns\GlitchAddOn` tree.
- Do not cherry-pick AddOn files for live deploy.
- Do not deploy Strategies unless the user explicitly requests strategy deployment.
- Finish the patch first. Validate workspace files first. Deploy once at the end.

## Deployment Command

Use the approved deploy script in one invocation with the full recursive `GlitchAddOn` file list:

```powershell
@'
$sourceRoot = 'D:\ab\projects\glitch\Glitch-Platform\ninjatrader\Glitch\AddOns\GlitchAddOn'
$files = Get-ChildItem -Path $sourceRoot -Recurse -File | Select-Object -ExpandProperty FullName
& 'C:\Users\alan\.codex\skills\deploy-glitch-safely\scripts\deploy_glitch_files.ps1' -SourceFiles $files
'@ | powershell -ExecutionPolicy Bypass -Command -
```

## Workflow

1. Validate the workspace patch.
2. Build the full recursive `GlitchAddOn` file list from the workspace source.
3. Run one deploy command.
4. Report the source-to-destination copy map.

## Related Skills

- Pair with `glitch-addon-indicator-workflow` for product code changes.
- Pair with `glitch-localization-workflow` when localization files are involved.
