param(
    [switch]$SubmitSim,
    [int]$HermesTimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'hermes-opportunity-gate.ps1')

function Event([hashtable]$Fields) {
    $record = [ordered]@{
        schema_version = 'glitch.hermes.opportunity_guard.v1'
        created_utc = [datetime]::UtcNow.ToString('o')
    }
    foreach ($key in $Fields.Keys) { $record[$key] = $Fields[$key] }
    $record | ConvertTo-Json -Depth 12 -Compress
}

function Read-GuardState([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json } catch { return $null }
}

function Write-GuardState([string]$Path, [object]$State) {
    $dir = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $json = $State | ConvertTo-Json -Depth 8 -Compress
    [IO.File]::WriteAllText($Path, $json, [Text.UTF8Encoding]::new($false))
}

function Get-BookIneligibilitySummary([object[]]$Books) {
    @($Books | Where-Object { -not [bool]$_.eligible_for_new_entry } | ForEach-Object {
        $reasons = @()
        if ($_.PSObject.Properties.Name -contains 'daily_loss_lockout_active' -and [bool]$_.daily_loss_lockout_active) {
            $reasons += 'daily_loss_lockout_active'
        }
        if ([bool]$_.market_ohlc_gate_active) { $reasons += 'market_ohlc_gate_active' }
        if ($_.PSObject.Properties.Name -contains 'instrument_contract_gate_active' -and [bool]$_.instrument_contract_gate_active) {
            $reasons += 'instrument_contract_gate_active'
        }
        [ordered]@{
            route_id = [string]$_.route_id
            account = [string]$_.master_account
            reasons = $reasons
            instrument_full_name = if ($_.PSObject.Properties.Name -contains 'instrument_full_name') { [string]$_.instrument_full_name } else { $null }
            expected_fallback_contract = if ($_.PSObject.Properties.Name -contains 'expected_fallback_contract') { [string]$_.expected_fallback_contract } else { $null }
            compiled_contract_fallback_available = if ($_.PSObject.Properties.Name -contains 'compiled_contract_fallback_available') { [bool]$_.compiled_contract_fallback_available } else { $false }
            daily_loss_remaining_per_master_contract_usd = if ($_.PSObject.Properties.Name -contains 'daily_loss_remaining_per_master_contract_usd') { $_.daily_loss_remaining_per_master_contract_usd } else { $null }
            trades_today = $_.trades_today
        }
    })
}

$invokeScript = Join-Path $PSScriptRoot 'invoke-hermes-portfolio-cycle.ps1'
$prepareOutput = @()
$prepareExitCode = 0
try {
    $prepareOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $invokeScript -PrepareOnly 2>&1
    $prepareExitCode = $LASTEXITCODE
} catch {
    $prepareOutput = @($_.Exception.Message)
    $prepareExitCode = 1
}
if ($prepareExitCode -ne 0) {
    $prepareText = $prepareOutput -join ' '
    if ($prepareText -like '*Another unified Hermes portfolio cycle is already active*' -or
        $prepareText -like '*Unable to create the unified portfolio-cycle lock*') {
        Event @{ event='opportunity_skipped'; reason='portfolio_cycle_already_active'; model_calls=0 }
        return
    }
    throw "Opportunity prepare failed: $prepareText"
}
$preparation = ($prepareOutput -join [Environment]::NewLine) | ConvertFrom-Json
$eligibleBooks = @($preparation.books | Where-Object { [bool]$_.eligible_for_new_entry })
if ($eligibleBooks.Count -eq 0) {
    Event @{
        event='opportunity_skipped'
        reason='no_eligible_books'
        evidence=$preparation.evidence
        model_calls=0
        ineligible_books=(Get-BookIneligibilitySummary @($preparation.books))
    }
    return
}

$gate = Get-HermesOpportunityGate $preparation
$features = $gate.features
$exactMatches = @($gate.exact_matches)
$hardTriggers = @($gate.hard_triggers)
$softTriggers = @($gate.soft_triggers)
$machineTriggers = @($gate.machine_triggers)
$pointsToPrevLowReclaim = $gate.points_to_prev_low_reclaim
$pointsToNearSupportReclaim = $gate.points_to_near_support_reclaim
$shortHeadroomFromSessionLow = $gate.short_headroom_from_session_low
$pointsToShortHeadroom30 = $gate.points_to_short_headroom_30
$minimumRequiredShortHeadroom = $gate.minimum_required_short_headroom
$pointsToShortHeadroomRequired = $gate.points_to_short_headroom_required
$shortHeadroomExtensionBufferPoints = $gate.short_headroom_extension_buffer_points
if (-not [bool]$gate.actionable) {
    Event @{
        event='opportunity_skipped'
        reason=$gate.reason
        evidence=$preparation.evidence
        model_calls=0
        eligible_books=$eligibleBooks.Count
        filtered_soft_trigger=$gate.filtered_soft_trigger
        sess_range_zone=$features.sess_range_zone
        points_from_session_high=$features.points_from_session_high
        points_from_session_low=$features.points_from_session_low
        points_above_prev_low=$features.points_above_prev_low
        points_to_prev_low_reclaim=$pointsToPrevLowReclaim
        points_to_near_support_reclaim=$pointsToNearSupportReclaim
        short_headroom_from_session_low=$shortHeadroomFromSessionLow
        points_to_short_headroom_30=$pointsToShortHeadroom30
        minimum_required_short_headroom=$minimumRequiredShortHeadroom
        points_to_short_headroom_required=$pointsToShortHeadroomRequired
        short_headroom_extension_buffer_points=$shortHeadroomExtensionBufferPoints
        eligible_short_reward_requirements=$gate.eligible_short_reward_requirements
        upper_breakout_attempt_1m=$features.upper_breakout_attempt_1m
        upper_breakout_acceptance_1m=$features.upper_breakout_acceptance_1m
        failed_upper_breakout_1m=$features.failed_upper_breakout_1m
        lower_breakdown_acceptance_1m=$features.lower_breakdown_acceptance_1m
        failed_lower_breakdown_1m=$features.failed_lower_breakdown_1m
        support_reclaim_after_flush=$features.support_reclaim_after_flush
        near_support_reclaim_after_flush=$features.near_support_reclaim_after_flush
        upper_extreme_bear_turn_1m=$features.upper_extreme_bear_turn_1m
        lower_extreme_bull_turn_1m=$features.lower_extreme_bull_turn_1m
        lower_extreme_bear_continuation_pressure_1m=$features.lower_extreme_bear_continuation_pressure_1m
        lower_extreme_bear_continuation_pressure_5m=$features.lower_extreme_bear_continuation_pressure_5m
        lower_extreme_trend_pullback_short_1m=$features.lower_extreme_trend_pullback_short_1m
    }
    return
}

$glitchData = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
$statePath = Join-Path $glitchData 'intents\hermes-opportunity-guard-state.json'
$state = Read-GuardState $statePath
$softOnly = $hardTriggers.Count -eq 0 -and $exactMatches.Count -eq 0 -and $softTriggers.Count -gt 0
$eligibleDiscoveryBooks = @($eligibleBooks | Where-Object { [string]$_.route_id -in @('glitch-aggressive','glitch-stay-revert') })
$softFallbackBooks = @()
if ($softOnly -and $eligibleDiscoveryBooks.Count -eq 0) {
    $softFallbackBooks = @($eligibleBooks | Where-Object {
        $routeId = [string]$_.route_id
        $confirmedSoftTrigger = (
            $softTriggers -contains 'support_reclaim_after_flush' -or
            $softTriggers -contains 'upper_extreme_bear_turn_1m' -or
            $softTriggers -contains 'lower_extreme_bear_continuation_pressure_1m' -or
            $softTriggers -contains 'lower_extreme_bear_continuation_pressure_5m' -or
            $softTriggers -contains 'lower_extreme_trend_pullback_short_1m'
        )
        $conservativeSoftTrigger = (
            $confirmedSoftTrigger -or
            $softTriggers -contains 'near_support_reclaim_after_flush' -or
            $softTriggers -contains 'lower_extreme_bull_turn_1m'
        )
        ($routeId -eq 'glitch-conservative' -and $conservativeSoftTrigger -or
            $routeId -eq 'glitch' -and $confirmedSoftTrigger) -and
        -not [bool]$_.daily_loss_lockout_active
    })
}
if ($softOnly -and $eligibleDiscoveryBooks.Count -eq 0 -and $softFallbackBooks.Count -eq 0) {
    Event @{
        event='opportunity_skipped'
        reason='soft_trigger_no_eligible_discovery_books'
        evidence=$preparation.evidence
        model_calls=0
        eligible_books=$eligibleBooks.Count
        machine_triggers=$machineTriggers
        sess_range_zone=$features.sess_range_zone
        points_to_prev_low_reclaim=$pointsToPrevLowReclaim
        points_to_near_support_reclaim=$pointsToNearSupportReclaim
        short_headroom_from_session_low=$shortHeadroomFromSessionLow
        points_to_short_headroom_30=$pointsToShortHeadroom30
        minimum_required_short_headroom=$minimumRequiredShortHeadroom
        points_to_short_headroom_required=$pointsToShortHeadroomRequired
        short_headroom_extension_buffer_points=$shortHeadroomExtensionBufferPoints
        support_reclaim_after_flush=$features.support_reclaim_after_flush
        near_support_reclaim_after_flush=$features.near_support_reclaim_after_flush
        upper_extreme_bear_turn_1m=$features.upper_extreme_bear_turn_1m
        lower_extreme_bull_turn_1m=$features.lower_extreme_bull_turn_1m
        lower_extreme_bear_continuation_pressure_1m=$features.lower_extreme_bear_continuation_pressure_1m
        lower_extreme_bear_continuation_pressure_5m=$features.lower_extreme_bear_continuation_pressure_5m
        lower_extreme_trend_pullback_short_1m=$features.lower_extreme_trend_pullback_short_1m
    }
    return
}
if ($softOnly -and $state -and $state.last_soft_model_call_utc) {
    $lastSoft = [datetimeoffset]::Parse([string]$state.last_soft_model_call_utc)
    $ageSeconds = ([datetimeoffset]::UtcNow - $lastSoft).TotalSeconds
    if ($ageSeconds -lt 300) {
        Event @{
            event='opportunity_skipped'
            reason='soft_trigger_credit_throttle'
            evidence=$preparation.evidence
            model_calls=0
            eligible_books=$eligibleBooks.Count
            machine_triggers=$machineTriggers
            last_soft_model_call_utc=$state.last_soft_model_call_utc
            throttle_seconds_remaining=[math]::Ceiling(300 - $ageSeconds)
            sess_range_zone=$features.sess_range_zone
            points_to_prev_low_reclaim=$pointsToPrevLowReclaim
            points_to_near_support_reclaim=$pointsToNearSupportReclaim
            short_headroom_from_session_low=$shortHeadroomFromSessionLow
            points_to_short_headroom_30=$pointsToShortHeadroom30
            minimum_required_short_headroom=$minimumRequiredShortHeadroom
            points_to_short_headroom_required=$pointsToShortHeadroomRequired
            short_headroom_extension_buffer_points=$shortHeadroomExtensionBufferPoints
            support_reclaim_after_flush=$features.support_reclaim_after_flush
            near_support_reclaim_after_flush=$features.near_support_reclaim_after_flush
            upper_extreme_bear_turn_1m=$features.upper_extreme_bear_turn_1m
            lower_extreme_bull_turn_1m=$features.lower_extreme_bull_turn_1m
            lower_extreme_bear_continuation_pressure_1m=$features.lower_extreme_bear_continuation_pressure_1m
            lower_extreme_bear_continuation_pressure_5m=$features.lower_extreme_bear_continuation_pressure_5m
            lower_extreme_trend_pullback_short_1m=$features.lower_extreme_trend_pullback_short_1m
            points_above_prev_low=$features.points_above_prev_low
        }
        return
    }
}

$runnerScript = Join-Path $PSScriptRoot 'run-hermes-portfolio-cycle.ps1'
$handoffPath = Join-Path $preparation.evidence 'runner-preparation.json'
[IO.File]::WriteAllText($handoffPath, ($preparation | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
$runnerArgs = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$runnerScript,'-HermesTimeoutSeconds',$HermesTimeoutSeconds,'-PreparationPath',$handoffPath)
if ($SubmitSim) { $runnerArgs += '-SubmitSim' }
$runnerOutput = & powershell.exe @runnerArgs 2>&1
$runnerText = $runnerOutput -join [Environment]::NewLine
if ($LASTEXITCODE -ne 0) {
    try {
        $blockedRunner = $runnerText | ConvertFrom-Json
        $blockedModelCalls = 1
        if ([string]$blockedRunner.reason -like 'Compact Hermes prompt exceeds 24000 bytes*' -or
            [string]$blockedRunner.reason -like 'Another unified Hermes portfolio cycle is already active*' -or
            [string]$blockedRunner.reason -like 'Unified Hermes preparation did not include a market snapshot hash.*' -or
            [string]$blockedRunner.reason -like 'Snapshot hash changed before inference:*') {
            $blockedModelCalls = 0
        }
        Event @{
            event='opportunity_runner_blocked'
            reason='runner_blocked'
            model_calls=$blockedModelCalls
            machine_triggers=$machineTriggers
            exact_archetype_matches=@($exactMatches | ForEach-Object { $_.archetype_id })
            runner=$blockedRunner
        }
        return
    } catch {
        throw "Opportunity runner failed: $runnerText"
    }
}
$runnerResult = $runnerText | ConvertFrom-Json
if ($softOnly) {
    Write-GuardState $statePath ([ordered]@{
        schema_version='glitch.hermes.opportunity_guard_state.v1'
        last_soft_model_call_utc=[datetimeoffset]::UtcNow.ToString('o')
        last_soft_machine_triggers=$softTriggers
        last_evidence=$preparation.evidence
    })
}
Event @{
    event='opportunity_runner_complete'
    reason='actionable_machine_trigger'
    model_calls=1
    machine_triggers=$machineTriggers
    minimum_required_short_headroom=$minimumRequiredShortHeadroom
    points_to_short_headroom_required=$pointsToShortHeadroomRequired
    short_headroom_extension_buffer_points=$shortHeadroomExtensionBufferPoints
    soft_fallback_books=@($softFallbackBooks | ForEach-Object { $_.route_id })
    exact_archetype_matches=@($exactMatches | ForEach-Object { $_.archetype_id })
    runner=$runnerResult
}
