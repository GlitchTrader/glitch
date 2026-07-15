param([ValidateSet('paper','sim')][string]$Target = 'paper')

$ErrorActionPreference = 'Continue'
$null = & python -m unittest discover (Join-Path $PSScriptRoot 'tests') -p 'test_*.py' 2>$null
$validatorExit = $LASTEXITCODE

$preflightOutput = & powershell.exe -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'preflight-open.ps1') -Target $Target 2>&1
$preflightExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$preflight = $null
try { $preflight = ($preflightOutput -join [Environment]::NewLine) | ConvertFrom-Json } catch { }

$matrix = @(
    [ordered]@{ case='intent_schema_and_scope'; mode='automatic'; passed=$validatorExit -eq 0; evidence='full tools/hermes/tests suite' },
    [ordered]@{ case='stale_snapshot'; mode='automatic'; passed=$validatorExit -eq 0; evidence='validator stale-hash rejection plus preflight freshness gate' },
    [ordered]@{ case='wrong_account'; mode='automatic'; passed=$validatorExit -eq 0; evidence='Sim102 intent rejected' },
    [ordered]@{ case='wrong_instrument'; mode='automatic'; passed=$validatorExit -eq 0; evidence='non-MNQ intent rejected' },
    [ordered]@{ case='bad_tick'; mode='automatic'; passed=$validatorExit -eq 0; evidence='off-tick protection rejected' },
    [ordered]@{ case='invalid_or_wide_bracket'; mode='automatic'; passed=$validatorExit -eq 0; evidence='geometry and risk-cap rejection' },
    [ordered]@{ case='kill_switch'; mode='automatic'; passed=$validatorExit -eq 0; evidence='source-safety contract proves firewall check 1 rejects ai_kill_switch before all execution checks' },
    [ordered]@{ case='executor_group_config'; mode='automatic'; passed=$preflight -and $preflight.checks.executor_group_unique -and $preflight.checks.executor_group_accounts_unique -and $preflight.checks.executor_group_ratios_valid -and $preflight.checks.executor_group_quantities_positive -and $preflight.checks.executor_group_sim_only -and $preflight.checks.executor_group_allowlisted -and $preflight.checks.executor_group_accounts_present; evidence='preflight-open.ps1' },
    [ordered]@{ case='market_open_readiness'; mode='automatic'; passed=$preflightExit -eq 0; evidence='preflight-open.ps1' },
    [ordered]@{ case='duplicate_intent'; mode='operator_f5'; passed=$false; evidence='pending runtime POST matrix' },
    [ordered]@{ case='missing_or_disabled_follower'; mode='operator_f5'; passed=$false; evidence='pending runtime group fixture' },
    [ordered]@{ case='follower_rejection_recovery'; mode='operator_f5'; passed=$false; evidence='pending injected reject; all members must end flat or protected' },
    [ordered]@{ case='stop_exit_reconciliation'; mode='operator_f5'; passed=$false; evidence='pending Sim group run' },
    [ordered]@{ case='target_exit_reconciliation'; mode='operator_f5'; passed=$false; evidence='pending Sim group run' }
)

$automatic = @($matrix | Where-Object mode -eq 'automatic')
$result = [ordered]@{
    schema_version = 'glitch.gl045.prearm.v1'
    checked_utc = [datetime]::UtcNow.ToString('o')
    target = $Target
    ready_for_operator_f5 = $automatic.Count -gt 0 -and @($automatic | Where-Object { -not $_.passed }).Count -eq 0
    validator_result = if ($validatorExit -eq 0) { 'passed' } else { 'failed' }
    preflight = $preflight
    matrix = $matrix
}
$result | ConvertTo-Json -Depth 8
if (-not $result.ready_for_operator_f5) { exit 1 }
