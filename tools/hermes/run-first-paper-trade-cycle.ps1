$ErrorActionPreference = 'Stop'
$gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
$statePath = Join-Path $gd 'ai\first-paper-trade-state.json'
$lockPath = Join-Path $gd 'ai\first-paper-trade-cycle.lock'
$lock = $null

try {
    try {
        $lock = [IO.File]::Open($lockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    } catch {
        return
    }

    $token = (Get-Content -LiteralPath (Join-Path $gd 'telemetry.token') -Raw).Trim()
    $headers = @{ Authorization = "Bearer $token" }
    $portfolio = Invoke-RestMethod -Uri 'http://127.0.0.1:8787/snapshot/portfolio' -Headers $headers -TimeoutSec 15
    $group = @($portfolio.accounts | Where-Object { $_.account -in @('Sim101','Sim102','Sim103') })
    $allFlat = $group.Count -eq 3 -and @($group | Where-Object { [string]$_.position_display -ne '0' }).Count -eq 0
    $noOrders = $group.Count -eq 3 -and @($group | Where-Object { $null -eq $_.working_orders -or [int]$_.working_orders -ne 0 }).Count -eq 0

    if (Test-Path -LiteralPath $statePath) {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($state.status -eq 'submitted' -and $allFlat -and $noOrders) {
            $state.status = 'closed'
            $state.closed_utc = [datetime]::UtcNow.ToString('o')
            $state | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $statePath -Encoding UTF8
            [ordered]@{
                event = 'first_paper_trade_closed'
                intent_id = $state.intent_id
                action = $state.action
                opened_utc = $state.opened_utc
                closed_utc = $state.closed_utc
                accounts_flat = $true
                no_working_orders = $true
                stop = 'success'
            } | ConvertTo-Json -Compress
        }
        return
    }

    if (-not $allFlat -or -not $noOrders) {
        throw 'Sim group is not flat/order-free and no supervised trade state exists.'
    }

    $preflight = & powershell.exe -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'preflight-open.ps1') -Target paper 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Paper preflight failed: $($preflight -join ' ')" }

    & powershell.exe -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'invoke-hermes-cycle.ps1') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Hermes cycle failed.' }

    $cycleJournalPath = Join-Path $gd 'intents\hermes-cycles.jsonl'
    $cycleRecord = Get-Content -LiteralPath $cycleJournalPath -Tail 1 | ConvertFrom-Json
    if ($cycleRecord.status -ne 'completed' -or -not $cycleRecord.evidence) { throw 'Hermes cycle did not produce completed evidence.' }
    if ($cycleRecord.action -eq 'NOTHING') { return }
    if ($cycleRecord.action -notin @('ENTER_LONG','ENTER_SHORT')) { throw "Unsupported flat-cycle action: $($cycleRecord.action)" }

    $cyclePath = Join-Path $cycleRecord.evidence 'cycle.json'
    $intentPath = Join-Path $cycleRecord.evidence 'intent.json'
    $submitOutput = & powershell.exe -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'submit-validated-sim-intent.ps1') -CyclePath $cyclePath -IntentPath $intentPath 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Validated Sim submission failed: $($submitOutput -join ' ')" }
    $submission = ($submitOutput -join [Environment]::NewLine) | ConvertFrom-Json

    $state = [ordered]@{
        schema_version = 'glitch.first_paper_trade_state.v1'
        status = 'submitted'
        intent_id = $submission.intent_id
        action = $submission.action
        snapshot_hash = $submission.snapshot_hash
        opened_utc = $submission.submitted_utc
        cycle_evidence = $cycleRecord.evidence
        execution = $submission.execution
    }
    $state | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $statePath -Encoding UTF8
    [ordered]@{
        event = 'first_paper_trade_opened'
        intent_id = $state.intent_id
        action = $state.action
        opened_utc = $state.opened_utc
        execution = $state.execution
        executor_left_armed = $false
        stop = 'monitor_until_closed'
    } | ConvertTo-Json -Depth 8 -Compress
} catch {
    [ordered]@{
        event = 'first_paper_trade_cycle_error'
        created_utc = [datetime]::UtcNow.ToString('o')
        error = $_.Exception.Message
        stop = 'blocked'
    } | ConvertTo-Json -Compress
} finally {
    if ($lock) { $lock.Dispose() }
    Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
}
