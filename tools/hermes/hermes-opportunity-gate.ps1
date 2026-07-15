function Get-HermesOpportunityGate([object]$Preparation) {
    $modelPath = Join-Path ([string]$Preparation.evidence) 'model-cycle.json'
    if (-not (Test-Path -LiteralPath $modelPath)) {
        throw "Prepared model-cycle evidence is missing: $modelPath"
    }

    $model = Get-Content -LiteralPath $modelPath -Raw | ConvertFrom-Json
    $features = $model.market.machine_features
    $exactMatches = @($model.market.archetype_evaluation | Where-Object { [bool]$_.exact_match })
    $eligibleBooks = @($model.books | Where-Object { [bool]$_.eligible_for_new_entry })
    $eligibleShortRewardRequirements = @($eligibleBooks | ForEach-Object {
        $riskRange = @($_.risk_per_master_contract_usd_range)
        $minRiskUsd = if ($riskRange.Count -gt 0 -and $null -ne $riskRange[0]) { [double]$riskRange[0] } else { 0.0 }
        $noiseFloorPoints = if ($_.PSObject.Properties.Name -contains 'noise_floor_stop_points' -and $null -ne $_.noise_floor_stop_points) { [double]$_.noise_floor_stop_points } else { 0.0 }
        $minimumPlannedRewardRisk = if ($_.PSObject.Properties.Name -contains 'minimum_planned_reward_risk' -and $null -ne $_.minimum_planned_reward_risk) { [double]$_.minimum_planned_reward_risk } else { 1.75 }
        $minimumStopPoints = [math]::Max(($minRiskUsd / 2.0), $noiseFloorPoints)
        if ($minimumStopPoints -gt 0.0) {
            [pscustomobject][ordered]@{
                route_id = [string]$_.route_id
                minimum_stop_points = [math]::Round($minimumStopPoints, 2)
                minimum_planned_reward_risk = [math]::Round($minimumPlannedRewardRisk, 2)
                required_short_headroom_points = [math]::Round($minimumStopPoints * $minimumPlannedRewardRisk, 2)
            }
        }
    })
    $minimumRawShortHeadroom = if ($eligibleShortRewardRequirements.Count -gt 0) {
        [double](($eligibleShortRewardRequirements | Measure-Object required_short_headroom_points -Minimum).Minimum)
    } else {
        30.0
    }
    # With no accepted breakdown, the first session low is a target obstacle, not proof of extension.
    # Reserve four MNQ points beyond the minimum R:R distance before spending a model call.
    $shortHeadroomExtensionBufferPoints = 4.0
    $minimumRequiredShortHeadroom = $minimumRawShortHeadroom + $shortHeadroomExtensionBufferPoints

    $hardTriggerFields = @(
        'upper_breakout_acceptance_1m',
        'failed_upper_breakout_1m',
        'lower_breakdown_acceptance_1m',
        'failed_lower_breakdown_1m'
    )
    $softTriggerFields = @(
        'support_reclaim_after_flush',
        'near_support_reclaim_after_flush',
        'upper_extreme_bear_turn_1m',
        'lower_extreme_bull_turn_1m',
        'lower_extreme_bear_continuation_pressure_1m',
        'lower_extreme_bear_continuation_pressure_5m',
        'lower_extreme_trend_pullback_short_1m'
    )

    $hardTriggers = @()
    foreach ($field in $hardTriggerFields) {
        if ([double]$features.$field -ge 1.0) { $hardTriggers += $field }
    }

    $softTriggers = @()
    foreach ($field in $softTriggerFields) {
        if ([double]$features.$field -ge 1.0) { $softTriggers += $field }
    }

    $thinLowerExtremeContinuation = $softTriggers -contains 'lower_extreme_bear_continuation_pressure_1m' `
        -and [double]$features.lower_breakdown_acceptance_1m -lt 1.0 `
        -and [double]$features.points_from_session_low -lt 20.0
    if ($thinLowerExtremeContinuation) {
        $softTriggers = @($softTriggers | Where-Object { $_ -ne 'lower_extreme_bear_continuation_pressure_1m' })
    }

    $insufficientLowerExtremeShortHeadroom = [string]$features.sess_range_zone -eq 'lower_extreme' `
        -and [double]$features.lower_breakdown_acceptance_1m -lt 1.0 `
        -and ([double]$features.points_from_session_low -lt $minimumRequiredShortHeadroom) `
        -and (
            $softTriggers -contains 'lower_extreme_bear_continuation_pressure_1m' -or
            $softTriggers -contains 'lower_extreme_bear_continuation_pressure_5m'
        )
    if ($insufficientLowerExtremeShortHeadroom) {
        $softTriggers = @($softTriggers | Where-Object {
            $_ -notin @('lower_extreme_bear_continuation_pressure_1m','lower_extreme_bear_continuation_pressure_5m')
        })
    }

    $unreclaimedLowerExtremeBullTurn = $softTriggers -contains 'lower_extreme_bull_turn_1m' `
        -and [double]$features.support_reclaim_after_flush -lt 1.0 `
        -and [double]$features.near_support_reclaim_after_flush -lt 1.0 `
        -and [double]$features.points_above_prev_low -lt 0.0
    if ($unreclaimedLowerExtremeBullTurn) {
        $softTriggers = @($softTriggers | Where-Object { $_ -ne 'lower_extreme_bull_turn_1m' })
    }

    $machineTriggers = @($hardTriggers + $softTriggers)
    $actionable = $machineTriggers.Count -gt 0 -or $exactMatches.Count -gt 0
    $pointsAbovePrevLow = [double]$features.points_above_prev_low
    $pointsToPrevLowReclaim = if ($pointsAbovePrevLow -lt 0.0) { [math]::Round(-1.0 * $pointsAbovePrevLow, 2) } else { 0.0 }
    $pointsToNearSupportReclaim = if ($pointsAbovePrevLow -lt -2.0) { [math]::Round((-2.0 - $pointsAbovePrevLow), 2) } else { 0.0 }
    $shortHeadroomFromSessionLow = [math]::Round([double]$features.points_from_session_low, 2)
    $pointsToShortHeadroom30 = [math]::Round([math]::Max(0.0, 30.0 - $shortHeadroomFromSessionLow), 2)
    $pointsToShortHeadroomRequired = [math]::Round([math]::Max(0.0, $minimumRequiredShortHeadroom - $shortHeadroomFromSessionLow), 2)
    $filteredSoftTrigger = $null
    if ($thinLowerExtremeContinuation) { $filteredSoftTrigger = 'lower_extreme_bear_continuation_pressure_1m' }
    if ($insufficientLowerExtremeShortHeadroom) { $filteredSoftTrigger = 'lower_extreme_bear_continuation_pressure_insufficient_headroom' }
    if ($unreclaimedLowerExtremeBullTurn) { $filteredSoftTrigger = 'lower_extreme_bull_turn_1m' }

    [pscustomobject][ordered]@{
        actionable=$actionable
        reason=if ($thinLowerExtremeContinuation) { 'thin_lower_extreme_continuation_no_call' } elseif ($insufficientLowerExtremeShortHeadroom) { 'lower_extreme_continuation_insufficient_reward_headroom_no_call' } elseif ($unreclaimedLowerExtremeBullTurn) { 'unreclaimed_lower_extreme_bull_turn_no_call' } else { 'no_actionable_machine_trigger' }
        filtered_soft_trigger=$filteredSoftTrigger
        hard_triggers=$hardTriggers
        soft_triggers=$softTriggers
        machine_triggers=$machineTriggers
        exact_matches=$exactMatches
        exact_match_count=$exactMatches.Count
        features=$features
        sess_range_zone=$features.sess_range_zone
        points_from_session_high=$features.points_from_session_high
        points_from_session_low=$features.points_from_session_low
        points_above_prev_low=$features.points_above_prev_low
        points_to_prev_low_reclaim=$pointsToPrevLowReclaim
        points_to_near_support_reclaim=$pointsToNearSupportReclaim
        short_headroom_from_session_low=$shortHeadroomFromSessionLow
        points_to_short_headroom_30=$pointsToShortHeadroom30
        minimum_required_short_headroom=$minimumRequiredShortHeadroom
        short_headroom_extension_buffer_points=$shortHeadroomExtensionBufferPoints
        points_to_short_headroom_required=$pointsToShortHeadroomRequired
        eligible_short_reward_requirements=$eligibleShortRewardRequirements
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
}
