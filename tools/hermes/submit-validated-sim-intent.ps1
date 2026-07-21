param(
    [Parameter(Mandatory=$true)][string]$CyclePath,
    [Parameter(Mandatory=$true)][string]$IntentPath
)

$ErrorActionPreference = 'Stop'
$gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
$policyPath = Join-Path $gd 'ai\policy.json'

function Read-WebExceptionBody {
    param([object]$Caught)
    try {
        $response = $Caught.Exception.Response
        if ($null -eq $response) { return $null }
        $stream = $response.GetResponseStream()
        if ($null -eq $stream) { return $null }
        $reader = New-Object IO.StreamReader($stream)
        return $reader.ReadToEnd()
    } catch {
        return $null
    }
}

$intent = Get-Content -LiteralPath (Resolve-Path -LiteralPath $IntentPath) -Raw | ConvertFrom-Json
if ($intent.action -notin @('ENTER_LONG','ENTER_SHORT')) { throw 'Sim submitter accepts only ENTER_LONG or ENTER_SHORT.' }
if (-not $intent.operator_profile -or $intent.instrument -ne 'MNQ' -or [int]$intent.quantity -lt 1) {
    throw 'Sim submitter requires a positive MNQ master quantity and an operator_profile.'
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
    throw "Intent profile/account binding is not authorized by Glitch policy: $profile/$masterAccount."
}

$schema = Join-Path $PSScriptRoot '..\..\glitch_hermes_docs\schemas\intent.v2.schema.json'
$validator = Join-Path $PSScriptRoot 'tests\validate_intent.py'
& python $validator (Resolve-Path -LiteralPath $CyclePath) (Resolve-Path -LiteralPath $IntentPath) $schema | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Intent validation failed; executor remains unarmed.' }

$preflightOutput = & powershell.exe -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'preflight-open.ps1') -Target paper -Profile $profile -MasterAccount $masterAccount 2>&1
if ($LASTEXITCODE -ne 0) { throw "Paper preflight failed; executor remains unarmed: $($preflightOutput -join ' ')" }
$before = ($preflightOutput -join [Environment]::NewLine) | ConvertFrom-Json

$token = (Get-Content -LiteralPath (Join-Path $gd 'telemetry.token') -Raw).Trim()
$headers = @{ Authorization = "Bearer $token" }
$latestPath = Join-Path $gd 'snapshots\market\latest.json'
$recentPath = Join-Path $gd "snapshots\market\recent\$($intent.snapshot_hash).json"
$boundPath = if (Test-Path -LiteralPath $recentPath) { $recentPath } else { $latestPath }
$boundSnapshot = Get-Content -LiteralPath $boundPath -Raw | ConvertFrom-Json
if ([string]$boundSnapshot.snapshot_hash -ne [string]$intent.snapshot_hash) {
    throw 'Validated entry snapshot is not retained by Glitch; executor remains unarmed.'
}
$boundAge = ([DateTimeOffset]::UtcNow - [DateTimeOffset]::Parse([string]$boundSnapshot.created_utc)).TotalSeconds
$policyBefore = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
if ($boundAge -lt -5 -or $boundAge -gt [double]$policyBefore.snapshot_max_age_seconds) {
    throw 'Validated entry snapshot is outside the policy freshness window; executor remains unarmed.'
}

$health = Invoke-RestMethod -Uri 'http://127.0.0.1:8788/health' -Method Get -TimeoutSec 5
if ([string]$health.status -ne 'ok' -or -not [bool]$health.executor_enabled) {
    throw 'Glitch AI is not ON; no POST performed.'
}
$submitLockPath = Join-Path $gd 'ai\sim-submit.lock'
$submitLock = $null
try {
    $submitLock = [IO.File]::Open($submitLockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
} catch {
    throw 'Another acceptance submission is already in flight.'
}
try {
    $boundAgePrePost = ([DateTimeOffset]::UtcNow - [DateTimeOffset]::Parse([string]$boundSnapshot.created_utc)).TotalSeconds
    if ($boundAgePrePost -lt -5 -or $boundAgePrePost -gt [double]$policyBefore.snapshot_max_age_seconds) {
        throw 'Validated entry snapshot expired before POST; no submission performed.'
    }

    $body = $intent | ConvertTo-Json -Depth 10 -Compress
    try {
        $response = Invoke-WebRequest -Uri 'http://127.0.0.1:8788/intent' -Method Post -Headers $headers -ContentType 'application/json' -Body $body -UseBasicParsing -TimeoutSec 15
    } catch {
        $errorBody = Read-WebExceptionBody $_
        if (-not [string]::IsNullOrWhiteSpace($errorBody)) {
            throw "Sim intent rejected by Glitch: $errorBody"
        }
        throw
    }
    if ($response.StatusCode -ne 202) { throw "Sim intent expected 202, got $($response.StatusCode)." }
    $executionJournal = Join-Path $gd 'intents\executions.jsonl'
    $executionRecord = $null
    $executionFailure = $null
    $expectedAccounts = @()
    $protectedAccounts = @()
    $legCount = 1 + [int]($null -ne $intent.take_profit_2) + [int]($null -ne $intent.take_profit_3)
    $executionDeadline = [datetime]::UtcNow.AddSeconds(45)
    do {
        if (Test-Path -LiteralPath $executionJournal) {
            $executionLines = Get-Content -LiteralPath $executionJournal -Tail 200
            foreach ($executionLine in $executionLines) {
                try { $candidate = $executionLine | ConvertFrom-Json } catch { continue }
                if ([string]$candidate.intent_id -ne [string]$intent.intent_id) { continue }
                if ($candidate.code -eq 'master_entry_submitted') {
                    $executionRecord = $candidate
                    $expectedToken = @(([string]$candidate.message -split '\|') | Where-Object { $_ -like 'expected_accounts=*' }) | Select-Object -First 1
                    if ($expectedToken) { $expectedAccounts = @(($expectedToken -replace '^expected_accounts=','') -split ',' | Where-Object { $_ }) }
                }
                if ($candidate.status -eq 'failed') {
                    $executionFailure = $candidate
                }
            }
        }
        $masterProtected = @($executionLines | ForEach-Object {
            try { $candidate = $_ | ConvertFrom-Json } catch { return }
            if ([string]$candidate.intent_id -eq [string]$intent.intent_id -and
                [string]$candidate.code -in @('master_structural_brackets_submitted','group_structural_brackets_submitted')) { $candidate }
        })
        $followersProtected = @($executionLines | ForEach-Object {
            try { $candidate = $_ | ConvertFrom-Json } catch { return }
            if ([string]$candidate.intent_id -eq [string]$intent.intent_id -and [string]$candidate.code -eq 'follower_structural_brackets_submitted') { $candidate }
        })
        $protectedAccounts = @($masterAccount)
        foreach ($record in $followersProtected) {
            $token = @(([string]$record.message -split '\|') | Where-Object { $_ -like 'account=*' }) | Select-Object -First 1
            if ($token) { $protectedAccounts += ($token -replace '^account=','') }
        }
        $portfolio = Get-Content -LiteralPath (Join-Path $gd 'snapshots\portfolio\latest.json') -Raw | ConvertFrom-Json
        $groupRows = @($portfolio.accounts | Where-Object { $_.account -in $expectedAccounts })
        $liveProtected = $expectedAccounts.Count -gt 0 -and $groupRows.Count -eq $expectedAccounts.Count -and @($groupRows | Where-Object {
            @($_.positions).Count -ne 1 -or [int]$_.working_orders -ne ($legCount * 2)
        }).Count -eq 0
        $allEvidence = $masterProtected.Count -gt 0 -and $expectedAccounts.Count -gt 0 -and
            @($expectedAccounts | Where-Object { $_ -notin $protectedAccounts }).Count -eq 0 -and $liveProtected
        if ($executionFailure -or $allEvidence) { break }
        Start-Sleep -Milliseconds 200
    } while ([datetime]::UtcNow -lt $executionDeadline)
    if (-not $allEvidence) {
        $detail = if ($executionFailure) { " Execution evidence: $($executionFailure.code): $($executionFailure.message)" } else { '' }
        throw "Sim intent was accepted but every expected account did not prove exact native bracket coverage within 45 seconds.$detail"
    }
    [ordered]@{
        schema_version = 'glitch.hermes.sim_submit.v1'
        submitted_utc = [datetime]::UtcNow.ToString('o')
        intent_id = $intent.intent_id
        operator_profile = $profile
        master_account = $masterAccount
        action = $intent.action
        snapshot_hash = $intent.snapshot_hash
        status = [int]$response.StatusCode
        response = $response.Content
        expected_accounts = $expectedAccounts
        protected_accounts = @($protectedAccounts | Sort-Object -Unique)
        protection_legs = $legCount
        execution = $executionRecord
        ai_auto_left_on = $true
        preflight_before = $before
    } | ConvertTo-Json -Depth 10
} finally {
    if ($submitLock) { $submitLock.Dispose() }
    Remove-Item -LiteralPath $submitLockPath -Force -ErrorAction SilentlyContinue
}
