param(
    [ValidateSet('ENTER_LONG','ENTER_SHORT')][string]$Action = 'ENTER_LONG',
    [string]$Profile = 'glitch',
    [string]$MasterAccount = 'Sim101',
    [ValidateRange(20,80)][double]$RiskUsd = 40,
    [ValidateRange(1.5,3.0)][double]$RewardRisk = 1.5
)

$ErrorActionPreference = 'Stop'
$gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
$latestPath = Join-Path $gd 'snapshots\market\latest.json'
$market = Get-Content -LiteralPath $latestPath -Raw | ConvertFrom-Json
$mnq = @($market.instruments | Where-Object instrument -eq 'MNQ') | Select-Object -First 1
if (-not $mnq) { throw 'MNQ is absent from the latest market snapshot.' }

$snapshotAge = ([datetime]::UtcNow - [datetime]::Parse($market.created_utc).ToUniversalTime()).TotalSeconds
$policy = Get-Content -LiteralPath (Join-Path $gd 'ai\policy.json') -Raw | ConvertFrom-Json
if ($snapshotAge -lt -5 -or $snapshotAge -gt [double]$policy.snapshot_max_age_seconds) {
    throw "Latest MNQ snapshot is stale ($([math]::Round($snapshotAge,1)) seconds)."
}

$tickSize = 0.25
$pointValue = 2.0
$entry = [math]::Round(([double]$mnq.current_price) / $tickSize) * $tickSize
$riskPoints = $RiskUsd / $pointValue
$rewardPoints = $riskPoints * $RewardRisk
if ($Action -eq 'ENTER_LONG') {
    $stop = $entry - $riskPoints
    $target = $entry + $rewardPoints
} else {
    $stop = $entry + $riskPoints
    $target = $entry - $rewardPoints
}
$stop = [math]::Round($stop / $tickSize) * $tickSize
$target = [math]::Round($target / $tickSize) * $tickSize

$runId = [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$evidence = Join-Path $PSScriptRoot "tests\out\operator-smoke-$runId"
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$cyclePath = Join-Path $evidence 'cycle.json'
$intentPath = Join-Path $evidence 'intent.json'

[ordered]@{
    name = 'operator_approved_sim_smoke'
    expected_actions = @($Action)
    operator = @{ profile=$Profile; master_account=$MasterAccount }
    market = @{ current_price=$entry; snapshot_hash=[string]$market.snapshot_hash }
    policy = @{ max_loss_per_trade_usd=$RiskUsd }
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $cyclePath

$intentId = [guid]::NewGuid().ToString()
[ordered]@{
    schema_version = 'glitch.intent.v2'
    intent_id = $intentId
    created_utc = [datetime]::UtcNow.ToString('o')
    instrument = 'MNQ'
    account = $MasterAccount
    operator_profile = $Profile
    action = $Action
    quantity = 1
    order_type = 'MARKET'
    stop_loss = $stop
    take_profit_1 = $target
    confidence = 0.5
    snapshot_hash = [string]$market.snapshot_hash
    model_version = 'operator-approved-smoke-v1'
    prompt_version = 'operator-approved-smoke-v1'
    reason = 'Operator-approved Sim-only execution and native-bracket workflow proof; not an autonomous market thesis.'
    decision_audit = [ordered]@{
        bull_case = 'The most recent Hermes portfolio cycle produced an aggressive long thesis.'
        bear_case = 'This smoke test does not claim a durable directional edge.'
        flat_case = 'Remaining flat would not exercise the requested end-to-end Sim execution workflow.'
        aggressive_case = 'Use one Sim contract with a native bracket to validate the complete route.'
        conservative_case = 'Hard-limit risk and require all group members to be protected immediately.'
        decisive_evidence = 'The operator explicitly authorized a Sim injection test after the autonomous intent expired.'
        disconfirming_evidence = 'This artifact must not be interpreted as autonomous profitability evidence.'
        change_condition = 'Native stop or target closes the group; no manual interference is required.'
        final_choice = $Action
    }
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $intentPath

$submitOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
    (Join-Path $PSScriptRoot 'submit-validated-sim-intent.ps1') `
    -CyclePath $cyclePath -IntentPath $intentPath 2>&1
if ($LASTEXITCODE -ne 0) { throw ($submitOutput -join ' ') }
$submission = ($submitOutput -join [Environment]::NewLine) | ConvertFrom-Json
if ([bool]$submission.executor_left_armed) { throw 'Smoke submission returned with executor armed.' }

[ordered]@{
    schema_version = 'glitch.hermes.operator_smoke.v1'
    created_utc = [datetime]::UtcNow.ToString('o')
    intent_id = $intentId
    action = $Action
    entry_reference = $entry
    stop_loss = $stop
    take_profit_1 = $target
    master_account = $MasterAccount
    evidence = $evidence
    execution = $submission.execution
    executor_left_armed = $false
} | ConvertTo-Json -Depth 12
