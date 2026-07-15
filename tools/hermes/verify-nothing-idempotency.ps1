param(
    [Parameter(Mandatory=$true)][string]$CyclePath,
    [Parameter(Mandatory=$true)][string]$IntentPath,
    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
$EvidenceRoot = if ($EvidenceRoot) { $EvidenceRoot } else { Join-Path $PSScriptRoot 'tests\out' }
$gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
$cycle = (Resolve-Path -LiteralPath $CyclePath).Path
$intentFile = (Resolve-Path -LiteralPath $IntentPath).Path
$intent = Get-Content -LiteralPath $intentFile -Raw | ConvertFrom-Json

if ($intent.action -ne 'NOTHING') { throw 'Idempotency proof accepts only action=NOTHING.' }
if ($intent.account -ne 'Sim101' -or $intent.instrument -ne 'MNQ') { throw 'Idempotency proof requires MNQ/Sim101.' }

$policy = Get-Content -LiteralPath (Join-Path $gd 'ai\policy.json') -Raw | ConvertFrom-Json
if ($policy.mode -ne 'paper' -or [bool]$policy.executor_enabled) {
    throw 'Idempotency proof requires mode=paper and executor_enabled=false.'
}

$preflightOutput = & powershell.exe -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'preflight-open.ps1') -Target paper 2>&1
if ($LASTEXITCODE -ne 0) { throw "Paper preflight is not ready: $($preflightOutput -join ' ')" }
$before = ($preflightOutput -join [Environment]::NewLine) | ConvertFrom-Json

$schema = Join-Path $PSScriptRoot '..\..\glitch_hermes_docs\schemas\intent.v2.schema.json'
$validator = Join-Path $PSScriptRoot 'tests\validate_intent.py'
& python $validator $cycle $intentFile $schema | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Intent fixture validation failed; no POST performed.' }

$token = (Get-Content -LiteralPath (Join-Path $gd 'telemetry.token') -Raw).Trim()
$headers = @{ Authorization = "Bearer $token" }
$latest = Invoke-RestMethod -Uri 'http://127.0.0.1:8787/snapshot/market' -Headers $headers -TimeoutSec 15
$sourceIntentId = [string]$intent.intent_id
$sourceSnapshotHash = [string]$intent.snapshot_hash
$runId = [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$evidence = Join-Path $EvidenceRoot "idempotency-$runId"
New-Item -ItemType Directory -Force -Path $evidence | Out-Null

# Hermes latency can exceed the lifetime of a rotating live snapshot hash. This
# proof tests endpoint idempotency, not a second model decision: clone only a
# validated NOTHING intent, bind it locally to the latest snapshot, and label
# the new ID/attribution explicitly. No entry action can pass this path.
$proofCycle = Get-Content -LiteralPath $cycle -Raw | ConvertFrom-Json
$proofCycle.name = 'local_nothing_idempotency_fixture'
$proofCycle.expected_actions = @('NOTHING')
$proofCycle.market = $latest
$proofIntent = Get-Content -LiteralPath $intentFile -Raw | ConvertFrom-Json
$proofIntent.intent_id = "idempotency-$runId-$([guid]::NewGuid().ToString('N'))"
$proofIntent.created_utc = [datetime]::UtcNow.ToString('o')
$proofIntent.snapshot_hash = [string]$latest.snapshot_hash
$proofIntent.reason = "local_idempotency_fixture: source_intent=$sourceIntentId; source_reason=$($intent.reason)"
$proofCyclePath = Join-Path $evidence 'cycle.json'
$proofIntentPath = Join-Path $evidence 'intent.json'
$proofCycle | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $proofCyclePath
$proofIntent | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $proofIntentPath
& python $validator $proofCyclePath $proofIntentPath $schema | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Locally rebound NOTHING fixture validation failed; no POST performed.' }

$latestPrePost = Invoke-RestMethod -Uri 'http://127.0.0.1:8787/snapshot/market' -Headers $headers -TimeoutSec 15
if ([string]$latestPrePost.snapshot_hash -ne [string]$proofIntent.snapshot_hash) {
    throw 'Locally rebound NOTHING snapshot was superseded before idempotency POST; no POST performed.'
}
$intent = $proofIntent
$body = $intent | ConvertTo-Json -Depth 10 -Compress
$uri = 'http://127.0.0.1:8788/intent'

$first = Invoke-WebRequest -Uri $uri -Method Post -Headers $headers -ContentType 'application/json' -Body $body -UseBasicParsing -TimeoutSec 15
if ($first.StatusCode -ne 202) { throw "First POST expected 202, got $($first.StatusCode)." }

$duplicateStatus = 0
$duplicateBody = $null
try {
    $second = Invoke-WebRequest -Uri $uri -Method Post -Headers $headers -ContentType 'application/json' -Body $body -UseBasicParsing -TimeoutSec 15
    $duplicateStatus = [int]$second.StatusCode
    $duplicateBody = $second.Content
} catch {
    if (-not $_.Exception.Response) { throw }
    $duplicateStatus = [int]$_.Exception.Response.StatusCode
    $reader = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())
    try { $duplicateBody = $reader.ReadToEnd() } finally { $reader.Dispose() }
}
if ($duplicateStatus -ne 409) {
    throw "Second POST did not prove duplicate rejection: status=$duplicateStatus body=$duplicateBody"
}

$afterOutput = & powershell.exe -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'preflight-open.ps1') -Target paper 2>&1
if ($LASTEXITCODE -ne 0) { throw "Postcondition preflight failed: $($afterOutput -join ' ')" }
$after = ($afterOutput -join [Environment]::NewLine) | ConvertFrom-Json
if (-not $after.checks.executor_group_accounts_flat -or -not $after.checks.executor_group_no_working_orders) {
    throw 'Postcondition violated: Sim group is not flat or has working orders.'
}

$executionJournal = Join-Path $gd 'intents\executions.jsonl'
$executionEvidence = Get-Content -LiteralPath $executionJournal -Tail 100 | Where-Object { $_ -match [regex]::Escape([string]$intent.intent_id) } | Select-Object -Last 1
if (-not $executionEvidence -or $executionEvidence -notmatch '"status":"skipped"') {
    throw 'Expected skipped execution journal record was not found.'
}

$result = [ordered]@{
    schema_version = 'glitch.hermes.idempotency_proof.v1'
    checked_utc = [datetime]::UtcNow.ToString('o')
    intent_id = $intent.intent_id
    attribution = 'local_endpoint_fixture_from_validated_hermes_nothing'
    source_intent_id = $sourceIntentId
    source_snapshot_hash = $sourceSnapshotHash
    action = $intent.action
    first_status = [int]$first.StatusCode
    duplicate_status = $duplicateStatus
    duplicate_body = $duplicateBody
    execution = ($executionEvidence | ConvertFrom-Json)
    preflight_before = $before
    preflight_after = $after
}
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $evidence 'result.json')
$result | ConvertTo-Json -Depth 10
