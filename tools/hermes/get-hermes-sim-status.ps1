param(
    [int]$Tail = 12
)

$ErrorActionPreference = 'Stop'
$gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'

function Read-JsonLinesTail {
    param([string]$Path, [int]$Count)
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    @(Get-Content -LiteralPath $Path -Tail $Count | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
        try { $_ | ConvertFrom-Json } catch { $null }
    } | Where-Object { $null -ne $_ })
}

function Convert-ExecutionMessageToMap {
    param([string]$Message)
    $map = @{}
    if ([string]::IsNullOrWhiteSpace($Message)) { return $map }
    foreach ($part in ($Message -split '\|')) {
        $kv = $part -split '=', 2
        if ($kv.Count -eq 2) { $map[[string]$kv[0]] = [string]$kv[1] }
    }
    return $map
}

function Count-FilledEntriesToday {
    param([string]$Path, [datetime]$NowUtc)
    $counts = @{}
    $seen = @{}
    if (-not (Test-Path -LiteralPath $Path)) { return $counts }
    $dayStart = [datetime]::SpecifyKind($NowUtc.Date, [DateTimeKind]::Utc)
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $row = $line | ConvertFrom-Json } catch { continue }
        if ([string]$row.code -ne 'group_entry_filled') { continue }
        $recorded = [datetime]::Parse([string]$row.recorded_utc).ToUniversalTime()
        if ($recorded -lt $dayStart -or $recorded -gt $NowUtc) { continue }
        $intentId = [string]$row.intent_id
        if ([string]::IsNullOrWhiteSpace($intentId) -or $seen.ContainsKey($intentId)) { continue }
        $msg = Convert-ExecutionMessageToMap ([string]$row.message)
        $master = [string]$msg['master']
        if ([string]::IsNullOrWhiteSpace($master)) { continue }
        $seen[$intentId] = $true
        if (-not $counts.ContainsKey($master)) { $counts[$master] = 0 }
        $counts[$master] = [int]$counts[$master] + 1
    }
    return $counts
}

function Summarize-Outcomes {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $profitCaptureTriggerUsd = 30.0
    $rows = @(Get-Content -LiteralPath $Path | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
        try { $_ | ConvertFrom-Json } catch { $null }
    } | Where-Object { $null -ne $_ })
    $summaries = @()
    foreach ($group in @($rows | Group-Object route_id, action)) {
        $items = @($group.Group)
        if ($items.Count -eq 0) { continue }
        $accountOutcomes = @($items | ForEach-Object { $_.account_outcomes })
        $wins = @($items | Where-Object { [double]$_.group_realized_pnl_usd -gt 0 })
        $losses = @($items | Where-Object { [double]$_.group_realized_pnl_usd -lt 0 })
        $mfeRows = @($accountOutcomes | Where-Object { $null -ne $_.observed_mfe_usd })
        $maeRows = @($accountOutcomes | Where-Object { $null -ne $_.observed_mae_usd })
        $givebackRows = @($accountOutcomes | Where-Object {
            $null -ne $_.observed_mfe_usd -and [double]$_.observed_mfe_usd -gt 0 -and $null -ne $_.realized_pnl_usd
        })
        $sumPnl = [double](($items | Measure-Object group_realized_pnl_usd -Sum).Sum)
        $avgMfe = if ($mfeRows.Count -gt 0) { [double](($mfeRows | Measure-Object observed_mfe_usd -Average).Average) } else { 0.0 }
        $avgMae = if ($maeRows.Count -gt 0) { [double](($maeRows | Measure-Object observed_mae_usd -Average).Average) } else { 0.0 }
        $avgGiveback = if ($givebackRows.Count -gt 0) {
            [double](($givebackRows | ForEach-Object { [double]$_.observed_mfe_usd - [double]$_.realized_pnl_usd } | Measure-Object -Average).Average)
        } else { 0.0 }
        $stoppedAfterBankableMfe = @($accountOutcomes | Where-Object {
            [string]$_.close_kind -eq 'stop' -and $null -ne $_.observed_mfe_usd -and [double]$_.observed_mfe_usd -ge 35.0
        }).Count
        $captureEligibleLegs = @($accountOutcomes | Where-Object {
            $null -ne $_.observed_mfe_usd -and [double]$_.observed_mfe_usd -ge $profitCaptureTriggerUsd
        })
        $hypotheticalCapturedPnl = 0.0
        foreach ($outcome in $accountOutcomes) {
            $realized = if ($null -ne $outcome.realized_pnl_usd) { [double]$outcome.realized_pnl_usd } else { 0.0 }
            $mfe = if ($null -ne $outcome.observed_mfe_usd) { [double]$outcome.observed_mfe_usd } else { 0.0 }
            if ($mfe -ge $profitCaptureTriggerUsd) {
                $hypotheticalCapturedPnl += $profitCaptureTriggerUsd
            } else {
                $hypotheticalCapturedPnl += $realized
            }
        }
        $hypotheticalImprovement = $hypotheticalCapturedPnl - $sumPnl
        $first = $items[0]
        $summaries += [ordered]@{
            route_id = [string]$first.route_id
            action = [string]$first.action
            trades = $items.Count
            wins = $wins.Count
            losses = $losses.Count
            win_rate = if ($items.Count -gt 0) { [math]::Round($wins.Count / $items.Count, 3) } else { 0.0 }
            group_realized_pnl_usd = [math]::Round($sumPnl, 2)
            avg_group_pnl_usd = [math]::Round($sumPnl / [math]::Max(1, $items.Count), 2)
            avg_account_mfe_usd = [math]::Round($avgMfe, 2)
            avg_account_mae_usd = [math]::Round($avgMae, 2)
            avg_account_giveback_usd = [math]::Round($avgGiveback, 2)
            stopped_after_mfe_35_count = $stoppedAfterBankableMfe
            profit_capture_trigger_usd = $profitCaptureTriggerUsd
            profit_capture_eligible_legs = $captureEligibleLegs.Count
            hypothetical_group_pnl_at_30_capture_usd = [math]::Round($hypotheticalCapturedPnl, 2)
            hypothetical_improvement_at_30_capture_usd = [math]::Round($hypotheticalImprovement, 2)
            lesson = if ($stoppedAfterBankableMfe -gt 0 -and $avgGiveback -gt 35.0) {
                'Bankable MFE was surrendered; deterministic profit exit should improve this route/action.'
            } elseif ($losses.Count -gt $wins.Count) {
                'Negative expectancy so far; require stronger setup filter or management improvement.'
            } else {
                'Insufficient or non-negative evidence.'
            }
        }
    }
    @($summaries | Sort-Object group_realized_pnl_usd)
}

function Get-LatestEntryConstraintSummary {
    param([object[]]$LastTicks)
    function Format-InvariantNumber([double]$Value) {
        return ([math]::Round($Value, 2)).ToString('0.##', [System.Globalization.CultureInfo]::InvariantCulture)
    }

    $lastTick = @($LastTicks | Select-Object -Last 1)
    if ($lastTick.Count -eq 0 -or $null -eq $lastTick[0].guard -or [string]::IsNullOrWhiteSpace([string]$lastTick[0].guard.evidence)) {
        return $null
    }

    $evidence = [string]$lastTick[0].guard.evidence
    $modelPath = Join-Path $evidence 'model-cycle.json'
    if (-not (Test-Path -LiteralPath $modelPath)) {
        return [ordered]@{
            evidence = $evidence
            error = 'model_cycle_missing'
        }
    }

    try {
        $model = Get-Content -LiteralPath $modelPath -Raw | ConvertFrom-Json
    } catch {
        return [ordered]@{
            evidence = $evidence
            error = 'model_cycle_unreadable'
            message = $_.Exception.Message
        }
    }

    $books = @($model.books)
    $eligibleBooks = @($books | Where-Object { [bool]$_.eligible_for_new_entry })
    $riskBooks = @($books | ForEach-Object {
        $riskRange = @($_.risk_per_master_contract_usd_range)
        $minRisk = if ($riskRange.Count -gt 0) { [double]$riskRange[0] } else { $null }
        $maxRisk = if ($riskRange.Count -gt 1) { [double]$riskRange[1] } else { $null }
        $blockedReasons = @()
        if (-not [bool]$_.eligible_for_new_entry) {
            if ($_.PSObject.Properties.Name -contains 'daily_loss_lockout_active' -and [bool]$_.daily_loss_lockout_active) { $blockedReasons += 'daily_loss_lockout_active' }
            if ($_.PSObject.Properties.Name -contains 'risk_capacity_below_noise_floor_active' -and [bool]$_.risk_capacity_below_noise_floor_active) { $blockedReasons += 'risk_capacity_below_noise_floor_active' }
            if ($_.PSObject.Properties.Name -contains 'market_ohlc_gate_active' -and [bool]$_.market_ohlc_gate_active) { $blockedReasons += 'market_ohlc_gate_active' }
            if ($_.PSObject.Properties.Name -contains 'instrument_contract_gate_active' -and [bool]$_.instrument_contract_gate_active) { $blockedReasons += 'instrument_contract_gate_active' }
        }
        [ordered]@{
            route_id = [string]$_.route_id
            master_account = [string]$_.master_account
            eligible_for_new_entry = [bool]$_.eligible_for_new_entry
            blocked_reasons = $blockedReasons
            trades_today = $_.trades_today
            min_risk_per_master_contract_usd = if ($null -ne $minRisk) { [math]::Round($minRisk, 2) } else { $null }
            max_risk_per_master_contract_usd = if ($null -ne $maxRisk) { [math]::Round($maxRisk, 2) } else { $null }
            volatility_stop_floor_usd = if ($_.PSObject.Properties.Name -contains 'volatility_stop_floor_usd') { $_.volatility_stop_floor_usd } else { $null }
            noise_floor_stop_points = if ($_.PSObject.Properties.Name -contains 'noise_floor_stop_points') { $_.noise_floor_stop_points } else { $null }
            minimum_planned_reward_risk = if ($_.PSObject.Properties.Name -contains 'minimum_planned_reward_risk') { $_.minimum_planned_reward_risk } else { $null }
        }
    })

    $guard = $lastTick[0].guard
    $waitingFor = @()
    if ($null -ne $guard.points_to_prev_low_reclaim -and [double]$guard.points_to_prev_low_reclaim -gt 0) {
        $waitingFor += ('long_reclaim_prev_low_by_{0}_points' -f (Format-InvariantNumber ([double]$guard.points_to_prev_low_reclaim)))
    }
    if ($null -ne $guard.points_to_near_support_reclaim -and [double]$guard.points_to_near_support_reclaim -gt 0) {
        $waitingFor += ('near_support_reclaim_by_{0}_points' -f (Format-InvariantNumber ([double]$guard.points_to_near_support_reclaim)))
    }
    if ($null -ne $guard.points_to_short_headroom_30 -and [double]$guard.points_to_short_headroom_30 -gt 0) {
        $waitingFor += ('short_headroom_plus_{0}_points' -f (Format-InvariantNumber ([double]$guard.points_to_short_headroom_30)))
    }
    if ($null -ne $guard.points_to_short_headroom_required -and [double]$guard.points_to_short_headroom_required -gt 0) {
        $waitingFor += ('short_reward_headroom_plus_{0}_points' -f (Format-InvariantNumber ([double]$guard.points_to_short_headroom_required)))
    }
    if ($waitingFor.Count -eq 0 -and [string]$guard.reason -eq 'no_actionable_machine_trigger') {
        $waitingFor += 'hard_or_soft_machine_trigger'
    }

    [ordered]@{
        evidence = $evidence
        guard_reason = [string]$guard.reason
        market_location = [ordered]@{
            sess_range_zone = if ($guard.PSObject.Properties.Name -contains 'sess_range_zone') { [string]$guard.sess_range_zone } else { $null }
            points_from_session_low = $guard.points_from_session_low
            points_from_session_high = $guard.points_from_session_high
            points_above_prev_low = $guard.points_above_prev_low
            minimum_required_short_headroom = if ($guard.PSObject.Properties.Name -contains 'minimum_required_short_headroom') { $guard.minimum_required_short_headroom } else { $null }
            points_to_short_headroom_required = if ($guard.PSObject.Properties.Name -contains 'points_to_short_headroom_required') { $guard.points_to_short_headroom_required } else { $null }
            short_headroom_extension_buffer_points = if ($guard.PSObject.Properties.Name -contains 'short_headroom_extension_buffer_points') { $guard.short_headroom_extension_buffer_points } else { $null }
        }
        waiting_for = $waitingFor
        eligible_book_count = $eligibleBooks.Count
        risk_books = $riskBooks
    }
}

function Get-GuardHealthSummary {
    param([object[]]$LastTicks)
    $ticks = @($LastTicks)
    if ($ticks.Count -eq 0) {
        return [ordered]@{
            inspected_ticks = 0
            model_calls = 0
            runner_blocked_count = 0
            management_skipped_count = 0
            opportunity_skipped_count = 0
            last_blocker = $null
            last_actionable_trigger = $null
        }
    }

    $modelCalls = 0
    $runnerBlocked = @()
    $managementSkipped = @()
    $opportunitySkipped = @()
    $actionable = @()
    $lastBlockerIndex = -1
    for ($index = 0; $index -lt $ticks.Count; $index++) {
        $tick = $ticks[$index]
        if ($null -ne $tick.guard) {
            if ($tick.guard.PSObject.Properties.Name -contains 'model_calls' -and $null -ne $tick.guard.model_calls) {
                $modelCalls += [int]$tick.guard.model_calls
            }
            if ([string]$tick.guard.event -eq 'opportunity_runner_blocked') {
                $runnerBlocked += $tick
                $lastBlockerIndex = $index
            }
            if ([string]$tick.guard.event -eq 'opportunity_skipped') { $opportunitySkipped += $tick }
            $machineTriggers = @($tick.guard.machine_triggers | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
            if ($machineTriggers.Count -gt 0) { $actionable += $tick }
        } else {
            if ($tick.PSObject.Properties.Name -contains 'model_calls' -and $null -ne $tick.model_calls) {
                $modelCalls += [int]$tick.model_calls
            }
        }
        if ([string]$tick.event -eq 'management_guard_tick_skipped') {
            $managementSkipped += $tick
            $lastBlockerIndex = $index
        }
    }

    $blockers = @($runnerBlocked + $managementSkipped)
    $lastBlocker = @($blockers | Select-Object -Last 1)
    $lastActionable = @($actionable | Select-Object -Last 1)
    $recoveredAfterLastBlocker = $false
    if ($lastBlockerIndex -ge 0 -and $lastBlockerIndex -lt ($ticks.Count - 1)) {
        $laterTicks = @($ticks | Select-Object -Skip ($lastBlockerIndex + 1))
        $recoveredAfterLastBlocker = @($laterTicks | Where-Object {
            [string]$_.event -eq 'opportunity_guard_tick_complete' -and
            $null -ne $_.guard -and
            [string]$_.guard.event -eq 'opportunity_skipped'
        }).Count -gt 0
    }
    [ordered]@{
        inspected_ticks = $ticks.Count
        model_calls = $modelCalls
        runner_blocked_count = $runnerBlocked.Count
        management_skipped_count = $managementSkipped.Count
        opportunity_skipped_count = $opportunitySkipped.Count
        recovered_after_last_blocker = $recoveredAfterLastBlocker
        last_blocker = if ($lastBlocker.Count -gt 0) {
            $tick = $lastBlocker[0]
            [ordered]@{
                created_utc = $tick.created_utc
                event = if ($tick.guard) { [string]$tick.guard.event } else { [string]$tick.event }
                reason = if ($tick.guard) {
                    if ($tick.guard.runner) { [string]$tick.guard.runner.reason } else { [string]$tick.guard.reason }
                } else { [string]$tick.reason }
            }
        } else { $null }
        last_actionable_trigger = if ($lastActionable.Count -gt 0) {
            $tick = $lastActionable[0]
            [ordered]@{
                created_utc = $tick.created_utc
                reason = [string]$tick.guard.reason
                machine_triggers = @($tick.guard.machine_triggers | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
                model_calls = if ($tick.guard.PSObject.Properties.Name -contains 'model_calls') { [int]$tick.guard.model_calls } else { 0 }
            }
        } else { $null }
    }
}

function Get-EquityIndexFrontContractSuffix([datetime]$UtcNow) {
    $date = $UtcNow.ToUniversalTime().Date
    foreach ($month in @(3, 6, 9, 12)) {
        $first = [datetime]::new($date.Year, $month, 1)
        $daysUntilFriday = ([int][DayOfWeek]::Friday - [int]$first.DayOfWeek + 7) % 7
        $thirdFriday = $first.AddDays($daysUntilFriday + 14)
        $rollover = $thirdFriday.AddDays(-8)
        if ($date -lt $rollover) {
            return ('{0:00}-{1:00}' -f $month, ($date.Year % 100))
        }
    }

    return ('03-{0:00}' -f (($date.Year + 1) % 100))
}

function Test-CompiledKnownMicroContractFallback([string]$InstrumentRoot) {
    if ([string]::IsNullOrWhiteSpace($InstrumentRoot) -or $InstrumentRoot -notin @('MNQ','MES','M2K')) {
        return $false
    }

    $customRoot = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\bin\Custom'
    $dll = Join-Path $customRoot 'NinjaTrader.Custom.dll'
    $liveSource = Join-Path $customRoot 'AddOns\GlitchAddOn\Services\Trading\GlitchInstrumentMetadataService.cs'
    if (-not (Test-Path -LiteralPath $dll) -or -not (Test-Path -LiteralPath $liveSource)) {
        return $false
    }

    try {
        return (Get-Item -LiteralPath $dll).LastWriteTimeUtc -ge (Get-Item -LiteralPath $liveSource).LastWriteTimeUtc
    } catch {
        return $false
    }
}

$policyPath = Join-Path $gd 'ai\policy.json'
$policy = if (Test-Path -LiteralPath $policyPath) { Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json } else { $null }
$controlStatePath = Join-Path $gd 'hermes\control-state.json'
$controlState = if (Test-Path -LiteralPath $controlStatePath) { Get-Content -LiteralPath $controlStatePath -Raw | ConvertFrom-Json } else { $null }
$executionsPath = Join-Path $gd 'intents\executions.jsonl'
$decisionsPath = Join-Path $gd 'intents\decisions.jsonl'
$ticksPath = Join-Path $gd 'intents\hermes-sim-guard-ticks.jsonl'
$outcomesPath = Join-Path $gd 'intents\hermes-trade-outcomes.jsonl'

$nowUtc = [datetime]::UtcNow
$filledToday = Count-FilledEntriesToday -Path $executionsPath -NowUtc $nowUtc
$bindings = @()
if ($policy) {
    foreach ($binding in @($policy.profile_account_bindings)) {
        $parts = [string]$binding -split '=', 2
        if ($parts.Count -eq 2) {
            $account = $parts[1].Trim()
            $tradesToday = if ($filledToday.ContainsKey($account)) { [int]$filledToday[$account] } else { 0 }
            $bindings += [ordered]@{
                route_id = $parts[0].Trim()
                master_account = $account
                trades_today = $tradesToday
            }
        }
    }
}

$portfolio = $null
try {
    $tokenPath = Join-Path $gd 'telemetry.token'
    $token = (Get-Content -LiteralPath $tokenPath -Raw).Trim()
    $headers = @{ Authorization = "Bearer $token" }
    $portfolio = Invoke-RestMethod -Uri 'http://127.0.0.1:8787/snapshot/portfolio' -Headers $headers -TimeoutSec 15
} catch {
    $portfolio = $null
}

$mnqMarketContract = $null
try {
    $marketPath = Join-Path $gd 'snapshots\market\latest.json'
    if (Test-Path -LiteralPath $marketPath) {
        $market = Get-Content -LiteralPath $marketPath -Raw | ConvertFrom-Json
        $mnq = @($market.instruments | Where-Object { [string]$_.instrument -eq 'MNQ' } | Select-Object -First 1)
        if ($mnq.Count -gt 0) {
            $marketCreatedUtc = if ($market.created_utc) { [datetime]::Parse([string]$market.created_utc).ToUniversalTime() } else { $null }
            $observedUtc = if ($mnq[0].timestamp_utc) { [datetime]::Parse([string]$mnq[0].timestamp_utc).ToUniversalTime() } else { $null }
            $rawAgeSeconds = if ($observedUtc) { [math]::Round(($nowUtc - $observedUtc).TotalSeconds, 1) } else { $null }
            $effectiveObservedUtc = $observedUtc
            if ($null -ne $rawAgeSeconds -and $rawAgeSeconds -lt -5 -and $marketCreatedUtc) {
                $effectiveObservedUtc = $marketCreatedUtc
            }
            $mnqMarketContract = [ordered]@{
                snapshot_id = [string]$market.snapshot_id
                snapshot_hash = [string]$market.snapshot_hash
                timestamp_utc = [string]$mnq[0].timestamp_utc
                raw_age_seconds = $rawAgeSeconds
                effective_timestamp_utc = if ($effectiveObservedUtc) { $effectiveObservedUtc.ToString('o') } else { $null }
                age_seconds = if ($effectiveObservedUtc) { [math]::Round(($nowUtc - $effectiveObservedUtc).TotalSeconds, 1) } else { $null }
                timestamp_source = if ($effectiveObservedUtc -and $observedUtc -and $effectiveObservedUtc -ne $observedUtc) { 'market_created_utc_due_instrument_future_skew' } else { 'instrument_timestamp_utc' }
                instrument_full_name = if ($mnq[0].PSObject.Properties.Name -contains 'instrument_full_name') { [string]$mnq[0].instrument_full_name } else { $null }
                contract_resolved = ($mnq[0].PSObject.Properties.Name -contains 'instrument_full_name') -and -not [string]::IsNullOrWhiteSpace([string]$mnq[0].instrument_full_name) -and -not ([string]$mnq[0].instrument_full_name).Trim().Equals('MNQ', [System.StringComparison]::OrdinalIgnoreCase)
                expected_fallback_contract = 'MNQ ' + (Get-EquityIndexFrontContractSuffix $nowUtc)
                compiled_contract_fallback_available = Test-CompiledKnownMicroContractFallback 'MNQ'
                missing_timeframes_minutes = @($mnq[0].missing_timeframes_minutes)
            }
        }
    }
} catch {
    $mnqMarketContract = [ordered]@{
        error = $_.Exception.Message
    }
}

$simAccounts = @()
if ($portfolio) {
    $simAccounts = @($portfolio.accounts | Where-Object { [string]$_.account -like 'Sim*' } | ForEach-Object {
        [ordered]@{
            account = $_.account
            realized_pnl = $_.realized_pnl
            unrealized_pnl = $_.unrealized_pnl
            total_pnl = $_.total_pnl
            position_display = $_.position_display
            working_orders = $_.working_orders
        }
    })
}

$lastTicks = Read-JsonLinesTail -Path $ticksPath -Count $Tail
$lastDecisions = Read-JsonLinesTail -Path $decisionsPath -Count $Tail
$lastOutcomes = Read-JsonLinesTail -Path $outcomesPath -Count $Tail
$performanceSummary = Summarize-Outcomes -Path $outcomesPath
$entryConstraintSummary = Get-LatestEntryConstraintSummary -LastTicks $lastTicks
$guardHealthSummary = Get-GuardHealthSummary -LastTicks $lastTicks
$lastRejected = @($lastDecisions | Where-Object { [string]$_.status -eq 'rejected' } | Select-Object -Last 4 | ForEach-Object {
    [ordered]@{
        recorded_utc = $_.recorded_utc
        intent_id = $_.intent_id
        account = $_.intent.account
        route_id = $_.intent.operator_profile
        action = $_.intent.action
        failed_check_code = $_.failed_check_code
        failed_check_message = $_.failed_check_message
    }
})

[ordered]@{
    schema_version = 'glitch.hermes.sim_status.v1'
    created_utc = $nowUtc.ToString('o')
    policy = if ($policy) {
        [ordered]@{
            mode = $policy.mode
            executor_enabled = ($null -ne $controlState -and -not [bool]$controlState.trading_paused -and [string]$policy.mode -in @('paper', 'live'))
            trading_paused = if ($null -eq $controlState) { $null } else { [bool]$controlState.trading_paused }
            ai_enabled = [bool]$policy.ai_enabled
            ai_kill_switch = [bool]$policy.ai_kill_switch
            snapshot_max_age_seconds = $policy.snapshot_max_age_seconds
        }
    } else { $null }
    mnq_market_contract = $mnqMarketContract
    route_caps = $bindings
    sim_accounts = $simAccounts
    last_guard_tick = @($lastTicks | Select-Object -Last 1)
    guard_health = $guardHealthSummary
    current_entry_constraints = $entryConstraintSummary
    recent_rejections = $lastRejected
    performance_summary = $performanceSummary
    last_outcome = @($lastOutcomes | Select-Object -Last 1)
} | ConvertTo-Json -Depth 14
