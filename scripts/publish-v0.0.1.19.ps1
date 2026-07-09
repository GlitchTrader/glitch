# ponytail: one-shot v0.0.1.19 publish — run from repo root after NT export to ninjatrader/Glitch/Glitch.zip
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot\..

$zipSrc = 'ninjatrader\Glitch\Glitch.zip'
$zipDest = 'apps\download\public\files\Glitch_v0.0.1.19.zip'

if (-not (Test-Path -LiteralPath $zipSrc)) {
    throw "Missing $zipSrc — export compiled AddOn from NinjaTrader first."
}

New-Item -ItemType Directory -Force -Path (Split-Path $zipDest) | Out-Null
Copy-Item -LiteralPath $zipSrc -Destination $zipDest -Force
Write-Host "Copied $zipSrc -> $zipDest"

cmd /c "npm run checksums --workspace apps/download"
cmd /c "npm run sync:release-dates --workspace apps/download"

git add ninjatrader/Glitch/AddOns/GlitchAddOn/
git add apps/download/public/files/Glitch_v0.0.1.19.zip
git add apps/download/public/files/checksums.json
git add apps/download/src/lib/release-dates.json
git add docs/ledger/log.md

git commit -m @"
release: publish Glitch v0.0.1.19 — event copy + account.Flatten money path

Strip unauthorized money paths: pure fill mirror, NT flatten primitive only.
"@

git push -u origin HEAD
Write-Host 'Done.'
