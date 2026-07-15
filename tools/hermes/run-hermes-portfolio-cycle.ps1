param(
    [switch]$SubmitSim,
    [switch]$ManagementOnly,
    [string]$PreparationPath,
    [int]$HermesTimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'hermes-opportunity-gate.ps1')
$gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
$lockPath = Join-Path $gd 'ai\hermes-portfolio-runner.lock'
$lock = $null
$exitCode = 0

function Event([hashtable]$Fields) {
    $record = [ordered]@{
        schema_version = 'glitch.hermes.portfolio_runner.v1'
        created_utc = [datetime]::UtcNow.ToString('o')
    }
    foreach ($key in $Fields.Keys) { $record[$key] = $Fields[$key] }
    $record | ConvertTo-Json -Depth 12 -Compress
}

function SubmissionBlockReason([object]$Intent, [object]$Caught) {
    $message = [string]$Caught.Exception.Message
    $executionJournal = Join-Path $gd 'intents\executions.jsonl'
    if (Test-Path -LiteralPath $executionJournal) {
        foreach ($line in @(Get-Content -LiteralPath $executionJournal -Tail 300)) {
            try { $candidate = $line | ConvertFrom-Json } catch { continue }
            if ([string]$candidate.intent_id -ne [string]$Intent.intent_id) { continue }
            if ($candidate.status -eq 'failed') {
                return "$($candidate.code): $($candidate.message)"
            }
        }
    }
    return $message
}

try {
    try {
        $lock = [IO.File]::Open($lockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    } catch {
        if (Test-Path -LiteralPath $lockPath) { Event @{ event='cycle_skipped'; reason='cycle_already_running' }; return }
        throw
    }

    $repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $reconcileOutput = & python (Join-Path $PSScriptRoot 'reconcile-hermes-outcomes.py') `
        --glitch-data $gd --evidence-root (Join-Path $PSScriptRoot 'tests\out') 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Hermes outcome reconciliation failed: $($reconcileOutput -join ' ')" }

    $invokeScript = Join-Path $PSScriptRoot 'invoke-hermes-portfolio-cycle.ps1'
    if (-not [string]::IsNullOrWhiteSpace($PreparationPath)) {
        if ($ManagementOnly) { throw 'A supplied preparation is entry-only; management must prepare fresh state.' }
        if (-not (Test-Path -LiteralPath $PreparationPath)) { throw "Supplied Hermes preparation is missing: $PreparationPath" }
        $preparation = Get-Content -LiteralPath $PreparationPath -Raw | ConvertFrom-Json
        if (-not [bool]$preparation.prepared) { throw 'Supplied Hermes preparation is not marked prepared.' }
    } else {
        $prepareArgs = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$invokeScript,'-PrepareOnly')
        if ($ManagementOnly) { $prepareArgs += '-ManagementOnly' }
        $prepareOutput = & powershell.exe @prepareArgs 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Unified Hermes preparation failed: $($prepareOutput -join ' ')" }
        $preparation = ($prepareOutput -join [Environment]::NewLine) | ConvertFrom-Json
    }
    $expectedSnapshotHash = if ($preparation.market -and $preparation.market.snapshot_hash) {
        [string]$preparation.market.snapshot_hash
    } else {
        throw 'Unified Hermes preparation did not include a market snapshot hash.'
    }
    $eligibleBooks = @($preparation.books | Where-Object {
        [bool]$_.eligible_for_new_entry -or [bool]$_.eligible_for_management
    })
    if ($eligibleBooks.Count -eq 0) {
        Event @{ event='cycle_skipped'; reason='no_eligible_books'; evidence=$preparation.evidence; model_calls=0 }
        return
    }
    $managementBooks = @($eligibleBooks | Where-Object { [bool]$_.eligible_for_management })
    $entryBooks = @($eligibleBooks | Where-Object { [bool]$_.eligible_for_new_entry })
    if (-not $ManagementOnly -and $managementBooks.Count -eq 0 -and $entryBooks.Count -gt 0) {
        $entryGate = Get-HermesOpportunityGate $preparation
        if (-not [bool]$entryGate.actionable) {
            Event @{
                event='cycle_skipped'
                reason=$entryGate.reason
                evidence=$preparation.evidence
                model_calls=0
                eligible_books=$entryBooks.Count
                filtered_soft_trigger=$entryGate.filtered_soft_trigger
                hard_triggers=$entryGate.hard_triggers
                soft_triggers=$entryGate.soft_triggers
                exact_match_count=$entryGate.exact_match_count
                sess_range_zone=$entryGate.sess_range_zone
                points_from_session_high=$entryGate.points_from_session_high
                points_from_session_low=$entryGate.points_from_session_low
                points_above_prev_low=$entryGate.points_above_prev_low
                points_to_prev_low_reclaim=$entryGate.points_to_prev_low_reclaim
                points_to_near_support_reclaim=$entryGate.points_to_near_support_reclaim
                short_headroom_from_session_low=$entryGate.short_headroom_from_session_low
                points_to_short_headroom_30=$entryGate.points_to_short_headroom_30
                minimum_required_short_headroom=$entryGate.minimum_required_short_headroom
                points_to_short_headroom_required=$entryGate.points_to_short_headroom_required
                short_headroom_extension_buffer_points=$entryGate.short_headroom_extension_buffer_points
                upper_breakout_acceptance_1m=$entryGate.upper_breakout_acceptance_1m
                failed_upper_breakout_1m=$entryGate.failed_upper_breakout_1m
                lower_breakdown_acceptance_1m=$entryGate.lower_breakdown_acceptance_1m
                failed_lower_breakdown_1m=$entryGate.failed_lower_breakdown_1m
                support_reclaim_after_flush=$entryGate.support_reclaim_after_flush
                near_support_reclaim_after_flush=$entryGate.near_support_reclaim_after_flush
                upper_extreme_bear_turn_1m=$entryGate.upper_extreme_bear_turn_1m
                lower_extreme_bull_turn_1m=$entryGate.lower_extreme_bull_turn_1m
                lower_extreme_bear_continuation_pressure_1m=$entryGate.lower_extreme_bear_continuation_pressure_1m
                lower_extreme_bear_continuation_pressure_5m=$entryGate.lower_extreme_bear_continuation_pressure_5m
                lower_extreme_trend_pullback_short_1m=$entryGate.lower_extreme_trend_pullback_short_1m
            }
            return
        }
    }

    $invokeArgs = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$invokeScript,'-HermesTimeoutSeconds',$HermesTimeoutSeconds)
    if ($ManagementOnly) {
        $invokeArgs += '-ManagementOnly'
    } else {
        $invokeArgs += @('-ExpectedSnapshotHash',$expectedSnapshotHash)
    }
    $invokeOutput = & powershell.exe @invokeArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        $invokeText = $invokeOutput -join ' '
        if (-not $ManagementOnly -and $invokeText -like '*Snapshot hash changed before inference:*') {
            Event @{
                event='cycle_skipped'
                reason='snapshot_changed_before_inference'
                evidence=$preparation.evidence
                model_calls=0
                expected_snapshot_hash=$expectedSnapshotHash
            }
            return
        }
        throw "Unified Hermes inference failed: $invokeText"
    }
    $result = ($invokeOutput -join [Environment]::NewLine) | ConvertFrom-Json
    if (-not [bool]$result.validated -or [bool]$result.submitted) {
        throw 'Unified Hermes result did not satisfy validated/unsubmitted handoff.'
    }
    if (-not $SubmitSim) {
        Event @{ event='portfolio_validated'; cycle_id=$result.cycle_id; decisions=$result.decisions; evidence=$result.evidence; submitted=$false }
        return
    }

    $aggregateScenario = Get-Content -LiteralPath (Join-Path $result.evidence 'scenario.json') -Raw | ConvertFrom-Json
    $submissions = @()
    foreach ($intent in @($result.decisions)) {
        if ($intent.action -eq 'NOTHING') {
            $submissions += [ordered]@{
                route_id=$intent.operator_profile; account=$intent.account; action='NOTHING'; status='observed_no_submission'
            }
            continue
        }
        if ($intent.action -eq 'HOLD') {
            $book = @($aggregateScenario.books | Where-Object route_id -eq $intent.operator_profile) | Select-Object -First 1
            if (-not $book -or -not [bool]$book.eligible_for_management -or $intent.action -notin @($book.allowed_actions)) {
                throw "HOLD decision has no eligible managed book: $($intent.operator_profile)"
            }
            $submissions += [ordered]@{
                route_id=$intent.operator_profile; account=$intent.account; action='HOLD'; status='managed_position_held'
                intent_id=$intent.intent_id
            }
            continue
        }
        if ($intent.action -eq 'EXIT') {
            $book = @($aggregateScenario.books | Where-Object route_id -eq $intent.operator_profile) | Select-Object -First 1
            if (-not $book -or -not [bool]$book.eligible_for_management -or $intent.action -notin @($book.allowed_actions)) {
                throw "EXIT decision has no eligible managed book: $($intent.operator_profile)"
            }
            $safeRoute = ([string]$intent.operator_profile).ToLowerInvariant() -replace '[^a-z0-9-]', '-'
            $intentPath = Join-Path $result.evidence "intent-$safeRoute.json"
            $cyclePath = Join-Path $result.evidence "cycle-$safeRoute.json"
            $intent | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $intentPath
            [ordered]@{
                name = "portfolio_exit_$safeRoute"
                expected_actions = @('EXIT')
                operator = @{ profile=$intent.operator_profile; master_account=$intent.account }
                market = @{ current_price=$aggregateScenario.market.current_price; snapshot_hash=$aggregateScenario.market.snapshot_hash }
                policy = @{ max_loss_per_trade_usd=$book.max_loss_per_master_contract_usd }
            } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $cyclePath
            try {
                $submitOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
                    (Join-Path $PSScriptRoot 'submit-validated-sim-exit.ps1') `
                    -CyclePath $cyclePath -IntentPath $intentPath 2>&1
                if ($LASTEXITCODE -ne 0) { throw ($submitOutput -join ' ') }
                $submission = ($submitOutput -join [Environment]::NewLine) | ConvertFrom-Json
                if ([bool]$submission.executor_left_armed) { throw 'Exit submission returned with executor armed.' }
                $submissions += [ordered]@{
                    route_id=$intent.operator_profile; account=$intent.account; action='EXIT'
                    status='managed_position_exit_submitted'; intent_id=$intent.intent_id; execution=$submission.execution
                    executor_left_armed=$false
                }
            } catch {
                $submissions += [ordered]@{
                    route_id=$intent.operator_profile; account=$intent.account; action='EXIT'
                    status='submission_blocked'; reason=(SubmissionBlockReason $intent $_)
                }
            }
            continue
        }
        if ($intent.action -notin @('ENTER_LONG','ENTER_SHORT')) {
            $submissions += [ordered]@{
                route_id=$intent.operator_profile; account=$intent.account; action=$intent.action; status='unsupported_while_flat'
            }
            continue
        }
        $book = @($aggregateScenario.books | Where-Object route_id -eq $intent.operator_profile) | Select-Object -First 1
        if (-not $book -or -not [bool]$book.eligible_for_new_entry) {
            throw "Entry decision has no eligible validated book: $($intent.operator_profile)"
        }
        $safeRoute = ([string]$intent.operator_profile).ToLowerInvariant() -replace '[^a-z0-9-]', '-'
        $intentPath = Join-Path $result.evidence "intent-$safeRoute.json"
        $cyclePath = Join-Path $result.evidence "cycle-$safeRoute.json"
        $intent | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $intentPath
        [ordered]@{
            name = "portfolio_$safeRoute"
            expected_actions = @('ENTER_LONG','ENTER_SHORT')
            operator = @{ profile=$intent.operator_profile; master_account=$intent.account }
            market = @{ current_price=$aggregateScenario.market.current_price; snapshot_hash=$aggregateScenario.market.snapshot_hash }
            policy = @{ max_loss_per_trade_usd=$book.max_loss_per_master_contract_usd }
        } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $cyclePath

        try {
            $submitOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
                (Join-Path $PSScriptRoot 'submit-validated-sim-intent.ps1') `
                -CyclePath $cyclePath -IntentPath $intentPath 2>&1
            if ($LASTEXITCODE -ne 0) { throw ($submitOutput -join ' ') }
            $submission = ($submitOutput -join [Environment]::NewLine) | ConvertFrom-Json
            if ([bool]$submission.executor_left_armed) { throw 'Submission returned with executor armed.' }
            $submissions += [ordered]@{
                route_id=$intent.operator_profile; account=$intent.account; action=$intent.action
                status='sim_trade_opened'; intent_id=$intent.intent_id; execution=$submission.execution
                executor_left_armed=$false
            }
        } catch {
            $submissions += [ordered]@{
                route_id=$intent.operator_profile; account=$intent.account; action=$intent.action
                status='submission_blocked'; reason=(SubmissionBlockReason $intent $_)
            }
        }
    }
    $policyAfter = Get-Content -LiteralPath (Join-Path $gd 'ai\policy.json') -Raw | ConvertFrom-Json
    if ($policyAfter.mode -ne 'paper' -or [bool]$policyAfter.executor_enabled) {
        throw 'Portfolio runner postcondition failed: policy is not paper/unarmed.'
    }
    $blockedSubmissions = @($submissions | Where-Object status -eq 'submission_blocked')
    if ($blockedSubmissions.Count -gt 0) {
        Event @{
            event='portfolio_cycle_blocked'; cycle_id=$result.cycle_id; submissions=$submissions
            evidence=$result.evidence; executor_left_armed=$false
            reason='one_or_more_book_submissions_blocked'
        }
        $exitCode = 1
    } else {
        Event @{
            event='portfolio_cycle_complete'; cycle_id=$result.cycle_id; submissions=$submissions
            evidence=$result.evidence; executor_left_armed=$false
        }
    }
} catch {
    Event @{ event='portfolio_cycle_blocked'; reason=$_.Exception.Message }
    $exitCode = 1
} finally {
    if ($lock) { $lock.Dispose() }
    Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
}

if ($exitCode -ne 0) { exit $exitCode }
