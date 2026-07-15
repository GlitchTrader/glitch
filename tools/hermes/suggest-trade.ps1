param(
    [string]$Instrument = 'MNQ',
    [string]$Account = 'Sim101',
    [string]$Profile = 'glitch',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
$tokenPath = Join-Path $gd 'telemetry.token'
$base = 'http://127.0.0.1:8787'
$intentBase = 'http://127.0.0.1:8788'

if (-not (Test-Path $tokenPath)) { throw "Missing token: $tokenPath" }
$token = (Get-Content $tokenPath -Raw).Trim()
$headers = @{ Authorization = "Bearer $token" }

$sanityPath = Join-Path $gd 'selfcheck\snapshot_sanity.json'
if (Test-Path $sanityPath) {
    $s = Get-Content $sanityPath -Raw | ConvertFrom-Json
    if ($s.status -ne 'ok') { Write-Warning "snapshot_sanity=$($s.status)" }
}

$market = Invoke-RestMethod -Uri "$base/snapshot/market" -Headers $headers -TimeoutSec 15
$hash = $market.snapshot_hash
if (-not $hash) { throw 'market snapshot_hash missing' }

$action = 'NOTHING'
$confidence = 0.0
$reason = 'hermes_stub_no_trade'

$intentId = [guid]::NewGuid().ToString()
$body = @{
    schema_version = 'glitch.intent.v2'
    intent_id = $intentId
    created_utc = (Get-Date).ToUniversalTime().ToString('o')
    instrument = $Instrument
    account = $Account
    operator_profile = $Profile
    action = $action
    confidence = $confidence
    snapshot_hash = $hash
    model_version = 'hermes-stub-v1'
    reason = $reason
    decision_audit = @{
        bull_case = 'No validated bullish thesis was produced by this stub.'
        bear_case = 'No validated bearish thesis was produced by this stub.'
        flat_case = 'The stub is intentionally non-trading.'
        aggressive_case = 'Unavailable in the non-trading stub.'
        conservative_case = 'Remain flat.'
        decisive_evidence = 'No model decision was requested.'
        disconfirming_evidence = 'A validated model cycle would supersede this stub.'
        change_condition = 'Run the guarded Hermes cycle.'
        final_choice = 'NOTHING'
    }
} | ConvertTo-Json -Compress

$cycleDir = Join-Path $gd 'hermes'
if (-not (Test-Path $cycleDir)) { New-Item -ItemType Directory -Path $cycleDir | Out-Null }
$cycleLine = (@{
    recorded_utc = (Get-Date).ToUniversalTime().ToString('o')
    intent_id = $intentId
    action = $action
    snapshot_hash = $hash
    dry_run = [bool]$DryRun
} | ConvertTo-Json -Compress)
Add-Content -Path (Join-Path $cycleDir 'cycles.jsonl') -Value $cycleLine

if ($DryRun) {
    Write-Output "DRY_RUN action=$action hash=$hash"
    exit 0
}

$response = Invoke-WebRequest -Uri "$intentBase/intent" -Method POST -Headers $headers -ContentType 'application/json' -Body $body -UseBasicParsing -TimeoutSec 15
Write-Output "status=$($response.StatusCode) body=$($response.Content)"
