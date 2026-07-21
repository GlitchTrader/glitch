[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$testRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $testRoot '..\..\..')).Path

Push-Location $repoRoot
try {
    & python -m unittest discover -s tools/hermes/tests -p 'test_*.py'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

Write-Host 'Python direct-rail behavior/contracts: PASS'
Write-Host 'NinjaTrader F5 compile: NOT RUN (separate required gate)'
Write-Host 'NinjaTrader Sim lifecycle acceptance: NOT RUN (separate required gate)'

