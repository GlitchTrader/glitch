param(
    [Parameter(Mandatory=$true)][string]$CyclePath,
    [Parameter(Mandatory=$true)][string]$IntentPath
)

$ErrorActionPreference = 'Stop'
$gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
$policyPath = Join-Path $gd 'ai\policy.json'
$intent = Get-Content -LiteralPath (Resolve-Path -LiteralPath $IntentPath) -Raw | ConvertFrom-Json
if ($intent.action -ne 'EXIT') { throw 'Sim exit submitter accepts only EXIT.' }
if (-not $intent.operator_profile -or $intent.instrument -ne 'MNQ') {
    throw 'Sim exit submitter requires MNQ and an operator_profile.'
}

$profile = [string]$intent.operator_profile
$masterAccount = [string]$intent.account
$policyForBinding = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
$boundAccount = $null
foreach ($binding in @($policyForBinding.profile_account_bindings)) {
    $parts = [string]$binding -split '=', 2
    if ($parts.Count -eq 2 -and $parts[0].Trim() -eq $profile) { $boundAccount = $parts[1].Trim(); break }
}
if (-not $boundAccount -or $masterAccount -ne $boundAccount) {
    throw "Exit profile/account binding is not authorized by Glitch policy: $profile/$masterAccount."
}

$schema = Join-Path $PSScriptRoot '..\..\glitch_hermes_docs\schemas\intent.v2.schema.json'
$validator = Join-Path $PSScriptRoot 'tests\validate_intent.py'
& python $validator (Resolve-Path -LiteralPath $CyclePath) (Resolve-Path -LiteralPath $IntentPath) $schema | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Exit intent validation failed; executor remains unarmed.' }

function Get-ManagedPreflight([string]$Target) {
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
        (Join-Path $PSScriptRoot 'preflight-open.ps1') -Target $Target -Profile $profile -MasterAccount $masterAccount 2>&1
    $result = ($output -join [Environment]::NewLine) | ConvertFrom-Json
    $allowedPositionFailures = @('master_flat','executor_group_accounts_flat','executor_group_no_working_orders')
    $unexpected = @($result.failed | Where-Object { $_ -notin $allowedPositionFailures })
    if ($unexpected.Count -gt 0) {
        throw "Managed $Target preflight failed: $($unexpected -join ',')."
    }
    if (@($result.failed | Where-Object { $_ -in $allowedPositionFailures }).Count -ne 3) {
        throw "Managed $Target preflight did not prove an open group with working protection."
    }
    return $result
}

function Assert-ExactManagedGroup([object]$Preflight) {
    $portfolio = Get-Content -LiteralPath (Join-Path $gd 'snapshots\portfolio\latest.json') -Raw | ConvertFrom-Json
    $directions = @()
    foreach ($member in @($Preflight.executor_group.members)) {
        $name = [string]$member.account
        if ($name -notmatch '^Sim') { throw "Managed exit refused non-Sim group member: $name" }
        $row = @($portfolio.accounts | Where-Object account -eq $name) | Select-Object -First 1
        if (-not $row) { throw "Managed exit account missing from portfolio: $name" }
        $positions = @($row.positions)
        $mnq = @($positions | Where-Object instrument_root -eq 'MNQ')
        if ($positions.Count -ne 1 -or $mnq.Count -ne 1) {
            throw "Managed exit requires exactly one MNQ position on $name."
        }
        if ([int]$mnq[0].quantity -ne [int]$member.quantity) {
            throw "Managed exit quantity mismatch on $name."
        }
        $workingOrders = [int]$row.working_orders
        if ($workingOrders -lt 2 -or ($workingOrders % 2) -ne 0) {
            throw "Managed exit requires complete stop/target bracket pairs on $name."
        }
        $directions += [string]$mnq[0].market_position
    }
    $uniqueDirections = @($directions | Sort-Object -Unique)
    if ($uniqueDirections.Count -ne 1 -or $uniqueDirections[0] -notin @('Long','Short')) {
        throw 'Managed exit requires one consistent group direction.'
    }
    return [ordered]@{
        group_id = [string]$Preflight.executor_group.group_id
        accounts = @($Preflight.executor_group.members | ForEach-Object { [string]$_.account })
        direction = $uniqueDirections[0]
    }
}

$before = Get-ManagedPreflight 'paper'
$managedGroup = Assert-ExactManagedGroup $before
$token = (Get-Content -LiteralPath (Join-Path $gd 'telemetry.token') -Raw).Trim()
$headers = @{ Authorization = "Bearer $token" }
$latestPath = Join-Path $gd 'snapshots\market\latest.json'
$recentPath = Join-Path $gd "snapshots\market\recent\$($intent.snapshot_hash).json"
$boundPath = if (Test-Path -LiteralPath $recentPath) { $recentPath } else { $latestPath }
$boundSnapshot = Get-Content -LiteralPath $boundPath -Raw | ConvertFrom-Json
if ([string]$boundSnapshot.snapshot_hash -ne [string]$intent.snapshot_hash) {
    throw 'Validated exit snapshot is not retained by Glitch; executor remains unarmed.'
}
$policyBefore = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
$boundAge = ([datetime]::UtcNow - [datetime]::Parse($boundSnapshot.created_utc).ToUniversalTime()).TotalSeconds
if ($boundAge -lt -5 -or $boundAge -gt [double]$policyBefore.snapshot_max_age_seconds) {
    throw 'Validated exit snapshot is outside the policy freshness window; executor remains unarmed.'
}

$originalPolicyJson = Get-Content -LiteralPath $policyPath -Raw
if ($policyBefore.mode -ne 'paper') {
    throw 'Bounded Sim exit requires Glitch paper mode.'
}
$submitLockPath = Join-Path $gd 'ai\sim-submit.lock'
$submitLock = $null
if (Test-Path -LiteralPath $submitLockPath) {
    try {
        $staleLock = [IO.File]::Open($submitLockPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $staleLock.Dispose()
        Remove-Item -LiteralPath $submitLockPath -Force
    } catch {
        throw 'Another profile is already inside the one-shot Sim submission gate.'
    }
}
try {
    $submitLock = [IO.File]::Open($submitLockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
} catch {
    throw 'Another profile is already inside the one-shot Sim submission gate.'
}

try {
    $boundAgePrePost = ([datetime]::UtcNow - [datetime]::Parse($boundSnapshot.created_utc).ToUniversalTime()).TotalSeconds
    if ($boundAgePrePost -lt -5 -or $boundAgePrePost -gt [double]$policyBefore.snapshot_max_age_seconds) {
        throw 'Validated exit snapshot expired before POST.'
    }

    $body = $intent | ConvertTo-Json -Depth 10 -Compress
    $response = Invoke-WebRequest -Uri 'http://127.0.0.1:8788/intent' -Method Post -Headers $headers `
        -ContentType 'application/json' -Body $body -UseBasicParsing -TimeoutSec 15
    if ($response.StatusCode -ne 202) { throw "Sim EXIT expected 202, got $($response.StatusCode)." }

    $executionJournal = Join-Path $gd 'intents\executions.jsonl'
    $executionRecord = $null
    $executionFailure = $null
    $deadline = [datetime]::UtcNow.AddSeconds(45)
    do {
        if (Test-Path -LiteralPath $executionJournal) {
            foreach ($line in @(Get-Content -LiteralPath $executionJournal -Tail 250)) {
                try { $candidate = $line | ConvertFrom-Json } catch { continue }
                if ([string]$candidate.intent_id -ne [string]$intent.intent_id) { continue }
                if ($candidate.code -eq 'group_exit_submitted' -and $candidate.status -eq 'submitted') { $executionRecord = $candidate }
                if ($candidate.status -eq 'failed') { $executionFailure = $candidate }
            }
        }
        if ($executionFailure) { break }
        $portfolio = Get-Content -LiteralPath (Join-Path $gd 'snapshots\portfolio\latest.json') -Raw | ConvertFrom-Json
        $groupRows = @($portfolio.accounts | Where-Object { $_.account -in @($managedGroup.accounts) })
        $flat = $groupRows.Count -eq @($managedGroup.accounts).Count -and @($groupRows | Where-Object {
            @($_.positions).Count -ne 0 -or [int]$_.working_orders -ne 0
        }).Count -eq 0
        if ($executionRecord -and $flat) { break }
        Start-Sleep -Milliseconds 200
    } while ([datetime]::UtcNow -lt $deadline)

    if ($executionFailure) {
        throw "Managed exit failed: $($executionFailure.code): $($executionFailure.message)"
    }
    if (-not $executionRecord -or -not $flat) {
        throw 'Managed exit did not prove submitted exit plus flat group postconditions within 45 seconds.'
    }
    [ordered]@{
        schema_version = 'glitch.hermes.sim_exit_submit.v1'
        submitted_utc = [datetime]::UtcNow.ToString('o')
        intent_id = $intent.intent_id
        group_id = $managedGroup.group_id
        accounts = $managedGroup.accounts
        execution = $executionRecord
        executor_left_armed = $false
        temporary_executor_arm_used = $false
        trading_state_unchanged = $true
        postcondition = 'group_flat_no_working_orders'
    } | ConvertTo-Json -Depth 10
} finally {
    if ($submitLock) { $submitLock.Dispose() }
    Remove-Item -LiteralPath $submitLockPath -Force -ErrorAction SilentlyContinue
}
