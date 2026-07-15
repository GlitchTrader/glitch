param(
    [switch]$SubmitSim,
    [int]$HermesTimeoutSeconds = 180,
    [int]$ManagementPolls = 6,
    [int]$ManagementPollIntervalSeconds = 10
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'hermes-native-bounded.ps1')
$gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
$tickJournal = Join-Path $gd 'intents\hermes-sim-guard-ticks.jsonl'
$guardScript = Join-Path $PSScriptRoot 'run-hermes-management-guard.ps1'

function Event([hashtable]$Fields) {
    $record = [ordered]@{
        schema_version = 'glitch.hermes.sim_guard_tick.v1'
        created_utc = [datetime]::UtcNow.ToString('o')
    }
    foreach ($key in $Fields.Keys) { $record[$key] = $Fields[$key] }
    $json = $record | ConvertTo-Json -Depth 12 -Compress
    $dir = Split-Path -Parent $tickJournal
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    Add-Content -LiteralPath $tickJournal -Value $json
    $json
}

function Clear-HermesPortfolioCycleLock {
    $lockPath = Join-Path $gd 'ai\hermes-portfolio-cycle.lock'
    $resolved = Resolve-Path -LiteralPath $lockPath -ErrorAction SilentlyContinue
    if ($resolved -and $resolved.Path -like (Join-Path $gd 'ai\*')) {
        Remove-Item -LiteralPath $resolved.Path -Force -ErrorAction SilentlyContinue
    }
}

$invokeScript = Join-Path $PSScriptRoot 'invoke-hermes-portfolio-cycle.ps1'
$managementPrepareResult = Invoke-HermesNativeBounded -FileName 'powershell.exe' -TimeoutSeconds 30 -Stage 'Sim guard management prepare' -Arguments @(
    '-NoProfile','-ExecutionPolicy','Bypass','-File',$invokeScript,'-PrepareOnly','-ManagementOnly'
)
if ([int]$managementPrepareResult.exit_code -ne 0) {
    if ([bool]$managementPrepareResult.timed_out) { Clear-HermesPortfolioCycleLock }
    Event @{ event='management_guard_tick_skipped'; reason=if ([bool]$managementPrepareResult.timed_out) { 'management_prepare_timeout' } else { 'management_prepare_failed' }; model_calls=0; message=$managementPrepareResult.message }
    return
}
$managementPrepareOutput = @($managementPrepareResult.stdout)
$managementPreparation = ($managementPrepareOutput -join [Environment]::NewLine) | ConvertFrom-Json
$managedBooks = @($managementPreparation.books | Where-Object { [bool]$_.eligible_for_management })

if ($managedBooks.Count -gt 0) {
    $pollCount = [math]::Max(1, $ManagementPolls)
    $pollInterval = [math]::Max(1, $ManagementPollIntervalSeconds)
    for ($pollIndex = 0; $pollIndex -lt $pollCount; $pollIndex++) {
        if ($pollIndex -gt 0) {
            Start-Sleep -Seconds $pollInterval
            $managementPrepareResult = Invoke-HermesNativeBounded -FileName 'powershell.exe' -TimeoutSeconds 30 -Stage 'Sim guard management poll prepare' -Arguments @(
                '-NoProfile','-ExecutionPolicy','Bypass','-File',$invokeScript,'-PrepareOnly','-ManagementOnly'
            )
            if ([int]$managementPrepareResult.exit_code -ne 0) {
                if ([bool]$managementPrepareResult.timed_out) { Clear-HermesPortfolioCycleLock }
                Event @{ event='management_guard_poll_complete'; poll_index=$pollIndex; reason=if ([bool]$managementPrepareResult.timed_out) { 'management_prepare_timeout' } else { 'management_prepare_failed' }; model_calls=0; message=$managementPrepareResult.message }
                return
            }
            $managementPrepareOutput = @($managementPrepareResult.stdout)
            $managementPreparation = ($managementPrepareOutput -join [Environment]::NewLine) | ConvertFrom-Json
            $managedBooks = @($managementPreparation.books | Where-Object { [bool]$_.eligible_for_management })
            if ($managedBooks.Count -eq 0) {
                Event @{
                    event='management_guard_poll_complete'
                    poll_index=$pollIndex
                    open_books=0
                    guard=[ordered]@{ event='management_skipped'; reason='no_open_protected_books'; model_calls=0 }
                }
                return
            }
        }

        $args = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$guardScript,'-HermesTimeoutSeconds',$HermesTimeoutSeconds)
        if ($pollIndex -gt 0) {
            # Follow-up intra-minute polls are for cheap profit capture only.
            # Avoid repeated adverse-threshold model calls while native brackets
            # continue to enforce the stop.
            $args += @('-AdverseTriggerUsd','-999999')
        }
        if ($SubmitSim) { $args += '-SubmitSim' }
        $guardResult = Invoke-HermesNativeBounded -FileName 'powershell.exe' -TimeoutSeconds ([math]::Max(45, $HermesTimeoutSeconds + 30)) -Stage 'Sim guard management tick' -Arguments $args
        if ([int]$guardResult.exit_code -ne 0) { throw "Sim guard management tick failed: $($guardResult.message)" }
        $output = @($guardResult.stdout)
        $guard = ($output -join [Environment]::NewLine | ConvertFrom-Json)
        Event @{
            event='management_guard_tick_complete'
            poll_index=$pollIndex
            open_books=$managedBooks.Count
            guard=$guard
        }
        if ([string]$guard.event -eq 'deterministic_profit_exit_complete') { return }
    }
    return
}

$opportunityScript = Join-Path $PSScriptRoot 'run-hermes-opportunity-guard.ps1'
$opportunityArgs = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$opportunityScript,'-HermesTimeoutSeconds',$HermesTimeoutSeconds)
if ($SubmitSim) { $opportunityArgs += '-SubmitSim' }
$opportunityResult = Invoke-HermesNativeBounded -FileName 'powershell.exe' -TimeoutSeconds ([math]::Max(45, $HermesTimeoutSeconds + 30)) -Stage 'Sim guard opportunity tick' -Arguments $opportunityArgs
if ([int]$opportunityResult.exit_code -ne 0) {
    if ([bool]$opportunityResult.timed_out) {
        Clear-HermesPortfolioCycleLock
        Event @{ event='opportunity_guard_tick_complete'; open_books=0; guard=[ordered]@{ event='opportunity_skipped'; reason='opportunity_guard_timeout'; model_calls=0; message=$opportunityResult.message } }
        return
    }
    throw "Sim guard opportunity tick failed: $($opportunityResult.message)"
}
$opportunityOutput = @($opportunityResult.stdout)
$opportunityGuard = ($opportunityOutput -join [Environment]::NewLine | ConvertFrom-Json)
Event @{
    event='opportunity_guard_tick_complete'
    open_books=0
    guard=$opportunityGuard
}

$openedSubmissions = @()
if ($SubmitSim -and $opportunityGuard.runner -and $opportunityGuard.runner.submissions) {
    $openedSubmissions = @($opportunityGuard.runner.submissions | Where-Object { [string]$_.status -eq 'sim_trade_opened' })
}
if ($openedSubmissions.Count -eq 0) { return }

# A fresh MNQ entry can reach bankable MFE before the next scheduled tick.
# Run cheap deterministic profit-only management polls immediately after entry.
$postEntryPollCount = [math]::Max(1, $ManagementPolls)
$postEntryPollInterval = [math]::Max(1, $ManagementPollIntervalSeconds)
for ($pollIndex = 0; $pollIndex -lt $postEntryPollCount; $pollIndex++) {
    if ($pollIndex -gt 0) { Start-Sleep -Seconds $postEntryPollInterval }
    $args = @(
        '-NoProfile','-ExecutionPolicy','Bypass','-File',$guardScript,
        '-HermesTimeoutSeconds',$HermesTimeoutSeconds,
        '-AdverseTriggerUsd','-999999'
    )
    if ($SubmitSim) { $args += '-SubmitSim' }
    $guardResult = Invoke-HermesNativeBounded -FileName 'powershell.exe' -TimeoutSeconds ([math]::Max(45, $HermesTimeoutSeconds + 30)) -Stage 'Post-entry management poll' -Arguments $args
    if ([int]$guardResult.exit_code -ne 0) { throw "Post-entry management poll failed: $($guardResult.message)" }
    $output = @($guardResult.stdout)
    $guard = ($output -join [Environment]::NewLine | ConvertFrom-Json)
    Event @{
        event='post_entry_management_guard_tick_complete'
        poll_index=$pollIndex
        opened_submissions=$openedSubmissions
        guard=$guard
    }
    if ([string]$guard.event -eq 'deterministic_profit_exit_complete') { return }
    if ([string]$guard.reason -eq 'no_open_protected_books') { return }
}
