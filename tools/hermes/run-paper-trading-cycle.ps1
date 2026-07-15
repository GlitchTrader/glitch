param(
    [string]$Profile = 'glitch',
    [string]$MasterAccount,
    [int]$HermesTimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
$gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
$safeProfile = $Profile.ToLowerInvariant() -replace '[^a-z0-9-]', '-'
if (-not $safeProfile) { throw 'Profile is required.' }
$lockPath = Join-Path $gd "ai\paper-trading-cycle.$safeProfile.lock"
$lock = $null

function Write-Event {
    param([hashtable]$Fields)

    $record = [ordered]@{
        schema_version = 'glitch.hermes.paper_cycle.v1'
        created_utc = [datetime]::UtcNow.ToString('o')
        operator_profile = $Profile
    }
    foreach ($key in $Fields.Keys) { $record[$key] = $Fields[$key] }
    $record | ConvertTo-Json -Depth 10 -Compress
}

try {
    try {
        $lock = [IO.File]::Open($lockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    } catch {
        Write-Event @{ event = 'cycle_skipped'; reason = 'cycle_already_running' }
        return
    }

    $preflightArgs = @(
        '-ExecutionPolicy', 'Bypass',
        '-File', (Join-Path $PSScriptRoot 'preflight-open.ps1'),
        '-Target', 'paper',
        '-Profile', $Profile
    )
    if ($MasterAccount) { $preflightArgs += @('-MasterAccount', $MasterAccount) }
    $preflightOutput = & powershell.exe @preflightArgs 2>&1
    $preflight = ($preflightOutput -join [Environment]::NewLine) | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not [bool]$preflight.ready) {
        Write-Event @{
            event = 'cycle_waiting'
            reason = 'paper_preflight_not_ready'
            failed = @($preflight.failed)
        }
        return
    }

    $cycleArgs = @(
        '-ExecutionPolicy', 'Bypass',
        '-File', (Join-Path $PSScriptRoot 'invoke-hermes-cycle.ps1'),
        '-Profile', $Profile,
        '-HermesTimeoutSeconds', $HermesTimeoutSeconds
    )
    if ($MasterAccount) { $cycleArgs += @('-MasterAccount', $MasterAccount) }
    $cycleOutput = & powershell.exe @cycleArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Hermes cycle failed: $($cycleOutput -join ' ')"
    }

    $cycleJournalPath = if ($safeProfile -eq 'glitch') {
        Join-Path $gd 'intents\hermes-cycles.jsonl'
    } else {
        Join-Path $gd "intents\hermes-cycles.$safeProfile.jsonl"
    }
    $cycleRecord = Get-Content -LiteralPath $cycleJournalPath -Tail 1 | ConvertFrom-Json
    if ($cycleRecord.status -ne 'completed' -or -not $cycleRecord.evidence) {
        throw 'Hermes cycle did not produce completed evidence.'
    }

    if ($cycleRecord.action -eq 'NOTHING') {
        Write-Event @{
            event = 'cycle_observed'
            action = 'NOTHING'
            evidence = $cycleRecord.evidence
        }
        return
    }

    if ($cycleRecord.action -notin @('ENTER_LONG','ENTER_SHORT')) {
        Write-Event @{
            event = 'cycle_observed'
            action = [string]$cycleRecord.action
            reason = 'action_not_executable_while_flat'
            evidence = $cycleRecord.evidence
        }
        return
    }

    $cyclePath = Join-Path $cycleRecord.evidence 'cycle.json'
    $intentPath = Join-Path $cycleRecord.evidence 'intent.json'
    $submitOutput = & powershell.exe -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'submit-validated-sim-intent.ps1') -CyclePath $cyclePath -IntentPath $intentPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Validated Sim submission failed: $($submitOutput -join ' ')"
    }
    $submission = ($submitOutput -join [Environment]::NewLine) | ConvertFrom-Json

    Write-Event @{
        event = 'sim_trade_opened'
        action = $submission.action
        intent_id = $submission.intent_id
        master_account = $submission.master_account
        snapshot_hash = $submission.snapshot_hash
        evidence = $cycleRecord.evidence
        execution = $submission.execution
        executor_left_armed = [bool]$submission.executor_left_armed
    }
} catch {
    Write-Event @{
        event = 'cycle_blocked'
        reason = $_.Exception.Message
    }
} finally {
    if ($lock) { $lock.Dispose() }
    Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
}
