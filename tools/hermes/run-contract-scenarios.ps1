param(
    [string[]]$Scenario,
    [string]$HermesRoot = (Join-Path $env:LOCALAPPDATA 'hermes\hermes-agent'),
    [string]$Capsule = 'D:\ab\projects\glitch\Glitch-Hermes-Data'
)

$ErrorActionPreference = 'Stop'
$testRoot = Join-Path $PSScriptRoot 'tests'
$all = Get-Content -LiteralPath (Join-Path $testRoot 'scenarios.json') -Raw | ConvertFrom-Json
if ($Scenario) { $all = @($all | Where-Object { $_.name -in $Scenario }) }
if (-not $all) { throw 'No matching scenarios.' }

$runId = [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$evidence = Join-Path $testRoot "out\$runId"
$active = Join-Path $Capsule 'tests'
New-Item -ItemType Directory -Force -Path $evidence,$active | Out-Null
$hermes = Join-Path $HermesRoot 'hermes'
$schema = Join-Path $PSScriptRoot '..\..\glitch_hermes_docs\schemas\intent.v2.schema.json'
$validator = Join-Path $testRoot 'validate_intent.py'
$results = [Collections.Generic.List[object]]::new()

foreach ($item in $all) {
    $scenarioPath = Join-Path $active 'active-scenario.json'
    $fixturePath = Join-Path $evidence "$($item.name).scenario.json"
    $outputPath = Join-Path $evidence "$($item.name).intent.json"
    $usagePath = Join-Path $evidence "$($item.name).usage.json"
    $item | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $scenarioPath
    Copy-Item -LiteralPath $scenarioPath -Destination $fixturePath -Force

    $prompt = @"
Run one Glitch decision cycle. Read /opt/glitch-data/tests/active-scenario.json and apply the preloaded Glitch skills in this order: observe market, assess risk, form thesis, build intent. Treat the scenario as the complete authoritative cycle bundle. Return exactly one glitch.intent.v2 JSON object and nothing else. Do not call any order endpoint.
"@
    $output = & python $hermes -p glitch --skills 'glitch-observe-market,glitch-assess-risk,glitch-form-thesis,glitch-build-intent' --usage-file $usagePath -z $prompt
    if ($LASTEXITCODE -ne 0) { throw "Hermes failed for $($item.name) with exit code $LASTEXITCODE" }
    $output | Set-Content -LiteralPath $outputPath

    $validation = & python $validator $fixturePath $outputPath $schema
    if ($LASTEXITCODE -ne 0) { throw "Validation failed for $($item.name)" }
    $results.Add(($validation | ConvertFrom-Json))
}

Remove-Item -LiteralPath (Join-Path $active 'active-scenario.json') -Force -ErrorAction SilentlyContinue
$summary = [ordered]@{ schema_version='glitch.hermes.contract_test.v1'; run_id=$runId; passed=$results.Count; total=@($all).Count; results=$results }
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'summary.json')
$summary | ConvertTo-Json -Depth 6
