param(
    [switch]$SubmitSim,
    [bool]$DeterministicProfitExit = $true,
    [double]$ProfitTriggerUsd = 30.0,
    [double]$AdverseTriggerUsd = -25.0,
    [int]$HermesTimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'hermes-native-bounded.ps1')

function Event([hashtable]$Fields) {
    $record = [ordered]@{
        schema_version = 'glitch.hermes.management_guard.v1'
        created_utc = [datetime]::UtcNow.ToString('o')
    }
    foreach ($key in $Fields.Keys) { $record[$key] = $Fields[$key] }
    $record | ConvertTo-Json -Depth 12 -Compress
}

function Invoke-NativeCapture([scriptblock]$Command) {
    $previous = $ErrorActionPreference
    try {
        $script:ErrorActionPreference = 'Continue'
        return @(& $Command 2>&1)
    } finally {
        $script:ErrorActionPreference = $previous
    }
}

function Clear-HermesPortfolioCycleLock {
    $gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
    $lockPath = Join-Path $gd 'ai\hermes-portfolio-cycle.lock'
    $resolved = Resolve-Path -LiteralPath $lockPath -ErrorAction SilentlyContinue
    if ($resolved -and $resolved.Path -like (Join-Path $gd 'ai\*')) {
        Remove-Item -LiteralPath $resolved.Path -Force -ErrorAction SilentlyContinue
    }
}

$invokeScript = Join-Path $PSScriptRoot 'invoke-hermes-portfolio-cycle.ps1'
$prepareResult = Invoke-HermesNativeBounded -FileName 'powershell.exe' -TimeoutSeconds 30 -Stage 'Management prepare' -Arguments @(
    '-NoProfile','-ExecutionPolicy','Bypass','-File',$invokeScript,'-PrepareOnly','-ManagementOnly'
)
if ([int]$prepareResult.exit_code -ne 0) {
    $prepareText = [string]$prepareResult.message
    if ($prepareText -like '*Another unified Hermes portfolio cycle is already active*') {
        Event @{ event='management_skipped'; reason='portfolio_cycle_already_active'; model_calls=0 }
        return
    }
    if ([bool]$prepareResult.timed_out) {
        Clear-HermesPortfolioCycleLock
        Event @{ event='management_skipped'; reason='management_prepare_timeout'; model_calls=0; timeout_seconds=30; message=$prepareText }
        return
    }
    throw "Management prepare failed: $prepareText"
}
$prepareOutput = @($prepareResult.stdout)
$preparation = ($prepareOutput -join [Environment]::NewLine) | ConvertFrom-Json
$managedBooks = @($preparation.books | Where-Object { [bool]$_.eligible_for_management })
if ($managedBooks.Count -eq 0) {
    Event @{ event='management_skipped'; reason='no_open_protected_books'; evidence=$preparation.evidence; model_calls=0 }
    return
}

$triggered = @()
$profitTriggered = @()
$openBookDiagnostics = @()
foreach ($book in $managedBooks) {
    $memberPnls = @($book.open_position.group_members | ForEach-Object { [double]$_.unrealized_pnl })
    $maxPnl = if ($memberPnls.Count -gt 0) { ($memberPnls | Measure-Object -Maximum).Maximum } else { 0.0 }
    $minPnl = if ($memberPnls.Count -gt 0) { ($memberPnls | Measure-Object -Minimum).Minimum } else { 0.0 }
    $openBookDiagnostics += [ordered]@{
        route_id = $book.route_id
        account = $book.master_account
        action = $book.open_position.action
        quantity = $book.open_position.quantity
        max_unrealized_pnl = [math]::Round([double]$maxPnl, 2)
        min_unrealized_pnl = [math]::Round([double]$minPnl, 2)
        dollars_to_profit_trigger = [math]::Round([math]::Max(0.0, $ProfitTriggerUsd - [double]$maxPnl), 2)
        dollars_to_adverse_trigger = [math]::Round([math]::Max(0.0, [double]$minPnl - $AdverseTriggerUsd), 2)
    }
    if ($maxPnl -ge $ProfitTriggerUsd -or $minPnl -le $AdverseTriggerUsd) {
        $trigger = [ordered]@{
            route_id = $book.route_id
            account = $book.master_account
            max_unrealized_pnl = [math]::Round([double]$maxPnl, 2)
            min_unrealized_pnl = [math]::Round([double]$minPnl, 2)
        }
        $triggered += $trigger
        if ($maxPnl -ge $ProfitTriggerUsd) { $profitTriggered += $trigger }
    }
}

if ($triggered.Count -eq 0) {
    Event @{
        event='management_skipped'
        reason='open_books_below_management_threshold'
        evidence=$preparation.evidence
        model_calls=0
        open_books=$managedBooks.Count
        profit_trigger_usd=$ProfitTriggerUsd
        adverse_trigger_usd=$AdverseTriggerUsd
        open_book_diagnostics=$openBookDiagnostics
    }
    return
}

if ($SubmitSim -and $DeterministicProfitExit -and $profitTriggered.Count -gt 0) {
    $scenario = Get-Content -LiteralPath (Join-Path ([string]$preparation.evidence) 'scenario.json') -Raw | ConvertFrom-Json
    $exitSubmissions = @()
    foreach ($trigger in $profitTriggered) {
        $book = @($managedBooks | Where-Object { [string]$_.route_id -eq [string]$trigger.route_id }) | Select-Object -First 1
        if (-not $book) { continue }
        $safeRoute = ([string]$book.route_id).ToLowerInvariant() -replace '[^a-z0-9-]', '-'
        $intentId = [guid]::NewGuid().ToString()
        $intentPath = Join-Path ([string]$preparation.evidence) "deterministic-exit-intent-$safeRoute.json"
        $cyclePath = Join-Path ([string]$preparation.evidence) "deterministic-exit-cycle-$safeRoute.json"
        [ordered]@{
            schema_version = 'glitch.intent.v2'
            intent_id = $intentId
            created_utc = [datetime]::UtcNow.ToString('o')
            instrument = 'MNQ'
            account = [string]$book.master_account
            operator_profile = [string]$book.route_id
            action = 'EXIT'
            confidence = 0.99
            snapshot_hash = [string]$scenario.market.snapshot_hash
            model_version = 'deterministic-profit-guard'
            prompt_version = 'glitch-hermes-v2'
            reason = 'Deterministic Sim profit protection: bank favorable open PnL before bracket gives it back.'
            decision_audit = [ordered]@{
                bull_case = 'Open protected position has bankable favorable PnL.'
                bear_case = 'Waiting can surrender one-contract MNQ profit back to stop.'
                flat_case = 'Flat preserves discovered profit and evidence quality.'
                aggressive_case = 'Immediate exit converts MFE into realized paper evidence.'
                conservative_case = 'Profit protection is preferable after threshold breach.'
                decisive_evidence = "max_unrealized_pnl=$($trigger.max_unrealized_pnl)"
                disconfirming_evidence = 'No stronger hold thesis is evaluated in deterministic guard.'
                change_condition = 'Future entries can re-open after fresh validated setup.'
                final_choice = 'EXIT'
            }
        } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $intentPath
        [ordered]@{
            name = "deterministic_profit_exit_$safeRoute"
            expected_actions = @('EXIT')
            operator = @{ profile=$book.route_id; master_account=$book.master_account }
            market = @{ current_price=$scenario.market.current_price; snapshot_hash=$scenario.market.snapshot_hash }
            policy = @{ max_loss_per_trade_usd=$book.max_loss_per_master_contract_usd }
        } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $cyclePath
        try {
            $submitOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
                (Join-Path $PSScriptRoot 'submit-validated-sim-exit.ps1') `
                -CyclePath $cyclePath -IntentPath $intentPath 2>&1
            if ($LASTEXITCODE -ne 0) { throw ($submitOutput -join ' ') }
            $submission = ($submitOutput -join [Environment]::NewLine) | ConvertFrom-Json
            $exitSubmissions += [ordered]@{
                route_id=$book.route_id
                account=$book.master_account
                status='deterministic_profit_exit_submitted'
                trigger=$trigger
                submission=$submission
            }
        } catch {
            $exitSubmissions += [ordered]@{
                route_id=$book.route_id
                account=$book.master_account
                status='deterministic_profit_exit_blocked'
                trigger=$trigger
                reason=$_.Exception.Message
            }
        }
    }
    Event @{
        event='deterministic_profit_exit_complete'
        evidence=$preparation.evidence
        model_calls=0
        profit_trigger_usd=$ProfitTriggerUsd
        open_book_diagnostics=$openBookDiagnostics
        submissions=$exitSubmissions
    }
    return
}

$runnerScript = Join-Path $PSScriptRoot 'run-hermes-portfolio-cycle.ps1'
$runnerArgs = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$runnerScript,'-ManagementOnly','-HermesTimeoutSeconds',$HermesTimeoutSeconds)
if ($SubmitSim) { $runnerArgs += '-SubmitSim' }
$runnerResultNative = Invoke-HermesNativeBounded -FileName 'powershell.exe' -TimeoutSeconds ([math]::Max(45, $HermesTimeoutSeconds + 30)) -Stage 'Management runner' -Arguments $runnerArgs
if ([int]$runnerResultNative.exit_code -ne 0) { throw "Management runner failed: $($runnerResultNative.message)" }
$runnerOutput = @($runnerResultNative.stdout)
$runnerResult = ($runnerOutput -join [Environment]::NewLine) | ConvertFrom-Json
Event @{
    event='management_runner_complete'
    triggered_books=$triggered
    runner=$runnerResult
}
