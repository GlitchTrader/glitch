param(
    [Parameter(Mandatory=$true)][string]$CyclePath,
    [Parameter(Mandatory=$true)][string]$IntentPath
)

$ErrorActionPreference = 'Stop'
$gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
$policyPath = Join-Path $gd 'ai\policy.json'

function Read-WebExceptionBody {
    param([object]$Caught)
    try {
        $response = $Caught.Exception.Response
        if ($null -eq $response) { return $null }
        $stream = $response.GetResponseStream()
        if ($null -eq $stream) { return $null }
        $reader = New-Object IO.StreamReader($stream)
        return $reader.ReadToEnd()
    } catch {
        return $null
    }
}

$intent = Get-Content -LiteralPath (Resolve-Path -LiteralPath $IntentPath) -Raw | ConvertFrom-Json
if ($intent.action -notin @('ENTER_LONG','ENTER_SHORT')) { throw 'Sim submitter accepts only ENTER_LONG or ENTER_SHORT.' }
if (-not $intent.operator_profile -or $intent.instrument -ne 'MNQ' -or [int]$intent.quantity -ne 1) {
    throw 'Sim submitter requires one MNQ master contract and an operator_profile.'
}
$profile = [string]$intent.operator_profile
$masterAccount = [string]$intent.account
$policyForBinding = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
$boundAccount = $null
foreach ($binding in @($policyForBinding.profile_account_bindings)) {
    $parts = [string]$binding -split '=', 2
    if ($parts.Count -eq 2 -and $parts[0].Trim() -eq $profile) { $boundAccount = $parts[1].Trim(); break }
}
if (-not $boundAccount -or $masterAccount -ne $boundAccount) {
    throw "Intent profile/account binding is not authorized by Glitch policy: $profile/$masterAccount."
}

$schema = Join-Path $PSScriptRoot '..\..\glitch_hermes_docs\schemas\intent.v2.schema.json'
$validator = Join-Path $PSScriptRoot 'tests\validate_intent.py'
& python $validator (Resolve-Path -LiteralPath $CyclePath) (Resolve-Path -LiteralPath $IntentPath) $schema | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Intent validation failed; executor remains unarmed.' }

$preflightOutput = & powershell.exe -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'preflight-open.ps1') -Target paper -Profile $profile -MasterAccount $masterAccount 2>&1
if ($LASTEXITCODE -ne 0) { throw "Paper preflight failed; executor remains unarmed: $($preflightOutput -join ' ')" }
$before = ($preflightOutput -join [Environment]::NewLine) | ConvertFrom-Json

$token = (Get-Content -LiteralPath (Join-Path $gd 'telemetry.token') -Raw).Trim()
$headers = @{ Authorization = "Bearer $token" }
$latestPath = Join-Path $gd 'snapshots\market\latest.json'
$recentPath = Join-Path $gd "snapshots\market\recent\$($intent.snapshot_hash).json"
$boundPath = if (Test-Path -LiteralPath $recentPath) { $recentPath } else { $latestPath }
$boundSnapshot = Get-Content -LiteralPath $boundPath -Raw | ConvertFrom-Json
if ([string]$boundSnapshot.snapshot_hash -ne [string]$intent.snapshot_hash) {
    throw 'Validated entry snapshot is not retained by Glitch; executor remains unarmed.'
}
$boundAge = ([datetime]::UtcNow - [datetime]::Parse($boundSnapshot.created_utc).ToUniversalTime()).TotalSeconds
$policyBefore = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
if ($boundAge -lt -5 -or $boundAge -gt [double]$policyBefore.snapshot_max_age_seconds) {
    throw 'Validated entry snapshot is outside the policy freshness window; executor remains unarmed.'
}

$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
if ($policy.mode -ne 'paper') { throw 'Bounded Sim submission requires Glitch paper mode.' }
$submitLockPath = Join-Path $gd 'ai\sim-submit.lock'
$submitLock = $null
if (Test-Path -LiteralPath $submitLockPath) {
    try {
        $staleLock = [IO.File]::Open($submitLockPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $staleLock.Dispose()
        Remove-Item -LiteralPath $submitLockPath -Force
    } catch {
        throw 'Another profile is already inside the one-shot Sim submission gate.'
    }
}
try {
    $submitLock = [IO.File]::Open($submitLockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
} catch {
    throw 'Another profile is already inside the one-shot Sim submission gate.'
}
try {
    $boundAgePrePost = ([datetime]::UtcNow - [datetime]::Parse($boundSnapshot.created_utc).ToUniversalTime()).TotalSeconds
    if ($boundAgePrePost -lt -5 -or $boundAgePrePost -gt [double]$policy.snapshot_max_age_seconds) {
        throw 'Validated entry snapshot expired before POST.'
    }

    $body = $intent | ConvertTo-Json -Depth 10 -Compress
    try {
        $response = Invoke-WebRequest -Uri 'http://127.0.0.1:8788/intent' -Method Post -Headers $headers -ContentType 'application/json' -Body $body -UseBasicParsing -TimeoutSec 15
    } catch {
        $errorBody = Read-WebExceptionBody $_
        if (-not [string]::IsNullOrWhiteSpace($errorBody)) {
            throw "Sim intent rejected by Glitch: $errorBody"
        }
        throw
    }
    if ($response.StatusCode -ne 202) { throw "Sim intent expected 202, got $($response.StatusCode)." }
    $executionJournal = Join-Path $gd 'intents\executions.jsonl'
    $executionRecord = $null
    $executionFailure = $null
    $executionDeadline = [datetime]::UtcNow.AddSeconds(45)
    do {
        if (Test-Path -LiteralPath $executionJournal) {
            $executionLines = Get-Content -LiteralPath $executionJournal -Tail 200
            foreach ($executionLine in $executionLines) {
                try { $candidate = $executionLine | ConvertFrom-Json } catch { continue }
                if ([string]$candidate.intent_id -ne [string]$intent.intent_id) { continue }
                if ($candidate.code -eq 'group_entry_open_protected' -and $candidate.status -eq 'submitted') {
                    $executionRecord = $candidate
                }
                if ($candidate.status -eq 'failed') {
                    $executionFailure = $candidate
                }
            }
            if ($executionRecord -or $executionFailure) { break }
        }
        Start-Sleep -Milliseconds 200
    } while ([datetime]::UtcNow -lt $executionDeadline)
    if (-not $executionRecord) {
        $detail = if ($executionFailure) { " Execution evidence: $($executionFailure.code): $($executionFailure.message)" } else { '' }
        throw "Sim intent was accepted but the group did not prove filled positions with working brackets within 45 seconds; executor is disarmed.$detail"
    }
    [ordered]@{
        schema_version = 'glitch.hermes.sim_submit.v1'
        submitted_utc = [datetime]::UtcNow.ToString('o')
        intent_id = $intent.intent_id
        operator_profile = $profile
        master_account = $masterAccount
        action = $intent.action
        snapshot_hash = $intent.snapshot_hash
        status = [int]$response.StatusCode
        response = $response.Content
        execution = $executionRecord
        executor_left_armed = $false
        temporary_executor_arm_used = $false
        trading_state_unchanged = $true
        preflight_before = $before
    } | ConvertTo-Json -Depth 10
} finally {
    if ($submitLock) { $submitLock.Dispose() }
    Remove-Item -LiteralPath $submitLockPath -Force -ErrorAction SilentlyContinue
}
