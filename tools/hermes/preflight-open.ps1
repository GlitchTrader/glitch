param(
    [ValidateSet('paper','sim')][string]$Target = 'paper',
    [string]$Profile = 'glitch',
    [string]$MasterAccount
)

$ErrorActionPreference = 'Stop'
$gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
$market = Get-Content -LiteralPath (Join-Path $gd 'snapshots\market\latest.json') -Raw | ConvertFrom-Json
$portfolio = Get-Content -LiteralPath (Join-Path $gd 'snapshots\portfolio\latest.json') -Raw | ConvertFrom-Json
$policy = Get-Content -LiteralPath (Join-Path $gd 'ai\policy.json') -Raw | ConvertFrom-Json
$controlPath = Join-Path $gd 'hermes\control-state.json'
$control = if (Test-Path -LiteralPath $controlPath) {
    Get-Content -LiteralPath $controlPath -Raw | ConvertFrom-Json
} else {
    [pscustomobject]@{ trading_paused = $true }
}
$rail = Get-Content -LiteralPath (Join-Path $gd 'selfcheck\rail.json') -Raw | ConvertFrom-Json
$groupPath = Join-Path $gd 'AccountGroups.tsv'
$mnq = @($market.instruments | Where-Object instrument -eq 'MNQ') | Select-Object -First 1
$profileBindings = @{}
foreach ($binding in @($policy.profile_account_bindings)) {
    $parts = [string]$binding -split '=', 2
    if ($parts.Count -eq 2 -and $parts[0].Trim() -and $parts[1].Trim()) {
        $profileBindings[$parts[0].Trim()] = $parts[1].Trim()
    }
}
$boundAccount = if ($profileBindings.ContainsKey($Profile)) { [string]$profileBindings[$Profile] } else { $null }
$requestedAccount = if ($MasterAccount) { $MasterAccount.Trim() } else { $boundAccount }
$masterPortfolio = @($portfolio.accounts | Where-Object account -eq $requestedAccount) | Select-Object -First 1
$now = [datetime]::UtcNow
$marketCreatedUtc = if ($market.created_utc) { [datetime]::Parse($market.created_utc).ToUniversalTime() } else { $null }
$mnqObservedUtc = if ($mnq -and $mnq.timestamp_utc) { [datetime]::Parse($mnq.timestamp_utc).ToUniversalTime() } else { $null }
$mnqRawAge = if ($mnqObservedUtc) { ($now - $mnqObservedUtc).TotalSeconds } else { [double]::PositiveInfinity }
if ($mnqRawAge -lt -5 -and $marketCreatedUtc) {
    $mnqObservedUtc = $marketCreatedUtc
}
$mnqAge = if ($mnqObservedUtc) { ($now - $mnqObservedUtc).TotalSeconds } else { [double]::PositiveInfinity }
$timeframeAges = @()
if ($mnq) {
    foreach ($bar in @($mnq.timeframe_bars)) {
        $barUtc = if ($bar.utc_time) { [datetime]::Parse($bar.utc_time).ToUniversalTime() } else { $null }
        $timeframeAges += if ($barUtc) { ($now - $barUtc).TotalSeconds } else { [double]::PositiveInfinity }
    }
}
$allTimeframesFresh = $timeframeAges.Count -eq 4 -and @($timeframeAges | Where-Object { $_ -lt -5 -or $_ -gt [double]$policy.snapshot_max_age_seconds }).Count -eq 0
$portfolioCreatedUtc = if ($portfolio.created_utc) { [datetime]::Parse($portfolio.created_utc).ToUniversalTime() } else { $null }
$portfolioAge = if ($portfolioCreatedUtc) { ($now - $portfolioCreatedUtc).TotalSeconds } else { [double]::PositiveInfinity }

$groupRows = if (Test-Path -LiteralPath $groupPath) {
    @(Get-Content -LiteralPath $groupPath | Where-Object { $_ -and -not $_.StartsWith('#') })
} else { @() }
$masterRows = @($groupRows | ForEach-Object {
    $parts = $_ -split "`t"
    if ($parts.Count -ge 4 -and $parts[0] -eq 'G' -and $parts[2] -eq $requestedAccount) {
        [pscustomobject]@{ group_id=$parts[1]; account=$parts[2] }
    }
} | Where-Object { $_ })
$executorGroup = $masterRows | Select-Object -First 1
$enabledFollowerRows = if ($executorGroup) { @($groupRows | ForEach-Object {
    $parts = $_ -split "`t"
    if ($parts.Count -ge 7 -and $parts[0] -eq 'M' -and $parts[1] -eq $executorGroup.group_id -and $parts[6] -eq '1') {
        $ratio = 0.0
        $ratioParsed = [double]::TryParse(
            $parts[4],
            [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref]$ratio)
        [pscustomobject]@{
            account = $parts[2]
            ratio = $ratio
            ratio_parsed = $ratioParsed
        }
    }
} | Where-Object { $_ }) } else { @() }
$enabledFollowers = @($enabledFollowerRows | ForEach-Object account)
$groupAccounts = @($requestedAccount) + $enabledFollowers
$masterQuantity = 1
$executionMembers = @(
    [pscustomobject]@{ account=$requestedAccount; ratio=1.0; quantity=$masterQuantity; valid=$true }
) + @($enabledFollowerRows | ForEach-Object {
    $scaled = $masterQuantity * $_.ratio
    $rounded = [math]::Round($scaled, 0, [MidpointRounding]::AwayFromZero)
    [pscustomobject]@{
        account = $_.account
        ratio = $_.ratio
        quantity = [int]$rounded
        valid = $_.ratio_parsed -and $_.ratio -gt 0 -and [math]::Abs($scaled - $rounded) -lt 0.0000001
    }
})
$groupContractMultiplier = [int](($executionMembers | Measure-Object -Property quantity -Sum).Sum)
$maxRiskPerContractByGroup = if ($groupContractMultiplier -gt 0) {
    [double]$policy.max_group_loss_per_trade_usd / $groupContractMultiplier
} else { 0.0 }
$portfolioNames = @($portfolio.accounts | ForEach-Object account)
$allowlist = @(@($policy.account_allowlist) + @($profileBindings.Values) | Sort-Object -Unique)
$groupRowsFromPortfolio = @($portfolio.accounts | Where-Object { $_.account -in $groupAccounts })
$requiredPortfolioFields = @('is_risk_locked','is_eval_target_locked','realized_pnl','positions','working_orders')
$groupPortfolioFieldsComplete = $groupRowsFromPortfolio.Count -eq $groupAccounts.Count -and @(
    $groupRowsFromPortfolio | Where-Object {
        $names = @($_.PSObject.Properties.Name)
        $missing = @($requiredPortfolioFields | Where-Object { $_ -notin $names }).Count -gt 0
        $invalid = -not ($_.is_risk_locked -is [bool]) `
            -or -not ($_.is_eval_target_locked -is [bool]) `
            -or $null -eq $_.realized_pnl `
            -or $null -eq $_.positions `
            -or $null -eq $_.working_orders
        $missing -or $invalid
    }
).Count -eq 0

$checks = [ordered]@{
    telemetry_running = [bool]$rail.telemetry.is_running
    intent_running = [bool]$rail.intent.is_running
    trading_enabled = -not [bool]$control.trading_paused
    operator_profile_bound = -not [string]::IsNullOrWhiteSpace($boundAccount)
    requested_master_matches_profile = -not [string]::IsNullOrWhiteSpace($requestedAccount) -and $requestedAccount -eq $boundAccount
    mnq_present = $null -ne $mnq
    mnq_fresh = $mnqAge -ge -5 -and $mnqAge -le [double]$policy.snapshot_max_age_seconds
    mnq_timeframes_fresh = $allTimeframesFresh
    portfolio_created_utc_present = $null -ne $portfolioCreatedUtc
    portfolio_fresh = $portfolioAge -ge -5 -and $portfolioAge -le [double]$policy.snapshot_max_age_seconds
    master_present = $null -ne $masterPortfolio
    master_flat = $null -ne $masterPortfolio -and [string]$masterPortfolio.position_display -eq '0'
    executor_group_unique = $masterRows.Count -eq 1
    executor_group_accounts_unique = @($groupAccounts | Sort-Object -Unique).Count -eq $groupAccounts.Count
    executor_group_ratios_valid = @($executionMembers | Where-Object { -not $_.valid }).Count -eq 0
    executor_group_quantities_positive = @($executionMembers | Where-Object { $_.quantity -lt 1 }).Count -eq 0
    executor_group_sim_only = @($groupAccounts | Where-Object { $_ -notmatch '^Sim' }).Count -eq 0
    executor_group_allowlisted = @($groupAccounts | Where-Object { $_ -notin $allowlist }).Count -eq 0
    executor_group_accounts_present = @($groupAccounts | Where-Object { $_ -notin $portfolioNames }).Count -eq 0
    executor_group_portfolio_fields_complete = $groupPortfolioFieldsComplete
    executor_group_accounts_flat = $groupRowsFromPortfolio.Count -eq $groupAccounts.Count -and @($groupRowsFromPortfolio | Where-Object { [string]$_.position_display -ne '0' }).Count -eq 0
    executor_group_no_working_orders = $groupRowsFromPortfolio.Count -eq $groupAccounts.Count -and @($groupRowsFromPortfolio | Where-Object { $null -eq $_.working_orders -or [int]$_.working_orders -ne 0 }).Count -eq 0
}
if ($Target -eq 'paper') {
    $checks.paper_safe = $policy.mode -eq 'paper'
} else {
    $checks.sim_armed = $false
}

$failed = @($checks.GetEnumerator() | Where-Object { -not $_.Value } | ForEach-Object Key)
$result = [ordered]@{
    schema_version = 'glitch.hermes.open_preflight.v1'
    checked_utc = $now.ToString('o')
    target = $Target
    operator_profile = $Profile
    master_account = $requestedAccount
    ready = $failed.Count -eq 0
    mnq_age_seconds = [math]::Round($mnqAge, 1)
    mnq_timeframe_age_seconds = @($timeframeAges | ForEach-Object { [math]::Round($_, 1) })
    portfolio_age_seconds = [math]::Round($portfolioAge, 1)
    executor_group = @{
        group_id = $executorGroup.group_id
        master_quantity = $masterQuantity
        members = $executionMembers
        total_contract_multiplier = $groupContractMultiplier
        max_risk_per_contract_by_group_usd = [math]::Round($maxRiskPerContractByGroup, 2)
    }
    checks = $checks
    failed = $failed
}
$result | ConvertTo-Json -Depth 5
if ($failed.Count) { exit 1 }
