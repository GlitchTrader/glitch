param(
    [switch]$PostPaper,
    [switch]$PrepareOnly,
    [string]$Profile = 'glitch',
    [string]$MasterAccount,
    [string]$HermesRoot = (Join-Path $env:LOCALAPPDATA 'hermes\hermes-agent'),
    [string]$Capsule = 'D:\ab\projects\glitch\Glitch-Hermes-Data',
    [string]$CycleJournal,
    [ValidateRange(5, 600)]
    [int]$HermesTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
$runId = [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$stage = 'initialize'
$active = $null
$evidence = $null
$safeProfile = $Profile.ToLowerInvariant() -replace '[^a-z0-9-]', '-'
if (-not $safeProfile) { throw 'Profile is required.' }
$cycleJournal = if ($CycleJournal) {
    $CycleJournal
} elseif ($safeProfile -eq 'glitch') {
    Join-Path $gd 'intents\hermes-cycles.jsonl'
} else {
    Join-Path $gd "intents\hermes-cycles.$safeProfile.jsonl"
}

function Write-CycleJournal {
    param(
        [Parameter(Mandatory=$true)][string]$Status,
        [Parameter(Mandatory=$true)][string]$Stage,
        [string]$Reason,
        [object]$Intent,
        [bool]$Posted = $false,
        [bool]$Superseded = $false
    )

    $journalDir = Split-Path -Parent $cycleJournal
    New-Item -ItemType Directory -Force -Path $journalDir | Out-Null
    $record = [ordered]@{
        schema_version = 'glitch.hermes.cycle_journal.v1'
        created_utc = [datetime]::UtcNow.ToString('o')
        run_id = $runId
        status = $Status
        stage = $Stage
        reason = $Reason
        action = if ($Intent) { $Intent.action } else { $null }
        intent_id = if ($Intent) { $Intent.intent_id } else { $null }
        snapshot_hash = if ($Intent) { $Intent.snapshot_hash } else { $null }
        operator_profile = $Profile
        master_account = $MasterAccount
        posted = $Posted
        superseded = $Superseded
        evidence = $evidence
    }
    Add-Content -LiteralPath $cycleJournal -Value ($record | ConvertTo-Json -Depth 6 -Compress)
}

function Invoke-HermesBounded {
    param(
        [Parameter(Mandatory=$true)][string]$Python,
        [Parameter(Mandatory=$true)][string]$Hermes,
        [Parameter(Mandatory=$true)][string]$ProfileName,
        [Parameter(Mandatory=$true)][string]$Usage,
        [Parameter(Mandatory=$true)][string]$Prompt,
        [Parameter(Mandatory=$true)][string]$StdoutPath,
        [Parameter(Mandatory=$true)][string]$StderrPath,
        [Parameter(Mandatory=$true)][int]$TimeoutSeconds
    )

    $arguments = @(
        ('"' + $Hermes + '"'), '-p', $ProfileName,
        '--skills', 'glitch-observe-market,glitch-form-thesis,glitch-build-intent',
        '--usage-file', ('"' + $Usage + '"'), '-z', ('"' + $Prompt.Replace('"', '\"') + '"')
    ) -join ' '
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $Python
    $start.Arguments = $arguments
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    if (-not $process.Start()) { throw 'Hermes process did not start.' }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill() } catch { }
        $process.WaitForExit()
        $stdoutTask.Result | Set-Content -LiteralPath $StdoutPath
        $stderrTask.Result | Set-Content -LiteralPath $StderrPath
        throw "Hermes timed out after $TimeoutSeconds seconds."
    }
    $stdout = $stdoutTask.Result
    $stderr = $stderrTask.Result
    $stdout | Set-Content -LiteralPath $StdoutPath
    $stderr | Set-Content -LiteralPath $StderrPath
    if ($process.ExitCode -ne 0) { throw "Hermes failed with exit code $($process.ExitCode)." }
    return $stdout
}

trap {
    $failure = $_.Exception.Message
    try {
        Write-CycleJournal -Status 'failed' -Stage $stage -Reason $failure
    } catch {
        Write-Warning "Cycle failure journal could not be written: $($_.Exception.Message)"
    }
    if ($active) { Remove-Item -LiteralPath $active -Force -ErrorAction SilentlyContinue }
    throw $failure
}

$stage = 'read_runtime_state'
$token = (Get-Content -LiteralPath (Join-Path $gd 'telemetry.token') -Raw).Trim()
$headers = @{ Authorization = "Bearer $token" }
$market = Invoke-RestMethod -Uri 'http://127.0.0.1:8787/snapshot/market' -Headers $headers -TimeoutSec 15
$portfolio = Invoke-RestMethod -Uri 'http://127.0.0.1:8787/snapshot/portfolio' -Headers $headers -TimeoutSec 15
$policy = Get-Content -LiteralPath (Join-Path $gd 'ai\policy.json') -Raw | ConvertFrom-Json
$profileBindings = @{}
foreach ($binding in @($policy.profile_account_bindings)) {
    $parts = [string]$binding -split '=', 2
    if ($parts.Count -eq 2 -and $parts[0].Trim() -and $parts[1].Trim()) {
        $profileBindings[$parts[0].Trim()] = $parts[1].Trim()
    }
}
if (-not $profileBindings.ContainsKey($Profile)) { throw "Hermes profile is not bound by Glitch policy: $Profile" }
$boundAccount = [string]$profileBindings[$Profile]
if ($MasterAccount -and $MasterAccount.Trim() -ne $boundAccount) {
    throw "Requested master does not match Glitch policy binding for $Profile."
}
$MasterAccount = $boundAccount
$journalSync = & powershell.exe -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'sync-nt-journal.ps1') -GlitchData $gd -Capsule $Capsule
if ($LASTEXITCODE -ne 0) { throw 'NinjaTrader journal sync failed; no inference.' }
$mnq = @($market.instruments | Where-Object instrument -eq 'MNQ') | Select-Object -First 1
if (-not $mnq) { throw 'MNQ missing from market snapshot.' }

# Privacy boundary: live account/portfolio/policy state is validated locally and
# is never placed in the capsule that Hermes can read. External inference is
# permitted only for a fully flat, order-free Sim group; position management
# remains native bracket/recovery logic until a separately approved design exists.
$groupPath = Join-Path $gd 'AccountGroups.tsv'
$groupRowsConfig = @(Get-Content -LiteralPath $groupPath | Where-Object { $_ -and -not $_.StartsWith('#') })
$masterGroups = @($groupRowsConfig | ForEach-Object {
    $parts = $_ -split "`t"
    if ($parts.Count -ge 4 -and $parts[0] -eq 'G' -and $parts[2] -eq $MasterAccount) {
        [pscustomobject]@{ group_id=$parts[1]; account=$parts[2] }
    }
} | Where-Object { $_ })
if ($masterGroups.Count -ne 1) { throw 'Local executor group must resolve uniquely; no external inference.' }
$masterGroup = $masterGroups[0]
$configuredFollowers = @($groupRowsConfig | ForEach-Object {
    $parts = $_ -split "`t"
    if ($parts.Count -ge 7 -and $parts[0] -eq 'M' -and $parts[1] -eq $masterGroup.group_id -and $parts[6] -eq '1') {
        $ratio = 0.0
        $parsed = [double]::TryParse(
            $parts[4],
            [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref]$ratio)
        [pscustomobject]@{ account=$parts[2]; ratio=$ratio; parsed=$parsed }
    }
} | Where-Object { $_ })
$expectedGroup = @($MasterAccount) + @($configuredFollowers | ForEach-Object account)
if (@($expectedGroup | Sort-Object -Unique).Count -ne $expectedGroup.Count) {
    throw 'Local executor group contains duplicate accounts; no external inference.'
}
$groupContractMultiplier = 1
foreach ($follower in $configuredFollowers) {
    $rounded = [math]::Round($follower.ratio, 0, [MidpointRounding]::AwayFromZero)
    if (-not $follower.parsed -or $follower.ratio -le 0 -or [math]::Abs($follower.ratio - $rounded) -ge 0.0000001) {
        throw "Local executor group ratio is not a positive integral one-contract multiplier for $($follower.account); no external inference."
    }
    $groupContractMultiplier += [int]$rounded
}
$localRiskCeiling = [math]::Floor((([double]$policy.max_group_loss_per_trade_usd / $groupContractMultiplier) * 100)) / 100
$explorationRiskMinimum = 20.0
$explorationRiskMaximum = [math]::Min(80.0, $localRiskCeiling)
if ($explorationRiskMaximum -lt $explorationRiskMinimum) {
    throw 'Local group risk capacity is below the paper exploration minimum; no external inference.'
}
$groupRows = @($portfolio.accounts | Where-Object { $_.account -in $expectedGroup })
if ($groupRows.Count -ne $expectedGroup.Count) { throw 'Local Sim group state is incomplete; no external inference.' }
if (@($groupRows | Where-Object { [string]$_.position_display -ne '0' }).Count -gt 0) {
    throw 'Local Sim group is not flat; no external inference.'
}
if (@($groupRows | Where-Object { $null -eq $_.working_orders -or [int]$_.working_orders -ne 0 }).Count -gt 0) {
    throw 'Local Sim group has working orders or incomplete order state; no external inference.'
}
if ($policy.mode -ne 'paper' -or [bool]$policy.executor_enabled) {
    throw 'Privacy-bounded external inference requires paper mode with executor disabled.'
}

$marketCreatedUtc = if ($market.created_utc) { [datetime]::Parse($market.created_utc).ToUniversalTime() } else { $null }
if (-not $marketCreatedUtc) { throw 'Market envelope creation time is unavailable; no external inference.' }
$age = ([datetime]::UtcNow - $marketCreatedUtc).TotalSeconds
if ($age -lt -5 -or $age -gt [double]$policy.snapshot_max_age_seconds) {
    throw 'Market envelope is stale or future-dated; no external inference.'
}
$oneMinute = @($mnq.timeframe_bars | Where-Object { [int]$_.minutes -eq 1 }) | Select-Object -First 1
$decisionPrice = if ($mnq.current_price -and [double]$mnq.current_price -gt 0) {
    [double]$mnq.current_price
} elseif ($oneMinute -and $oneMinute.close -and [double]$oneMinute.close -gt 0) {
    [double]$oneMinute.close
} else {
    throw 'MNQ decision price is unavailable; no external inference.'
}
$validationMarket = Get-Content -LiteralPath (Join-Path $gd 'snapshots\market\latest.json') -Raw | ConvertFrom-Json
$validationMarket | Add-Member -NotePropertyName current_price -NotePropertyValue $decisionPrice -Force
$validationCycle = [ordered]@{
    name = 'live_cycle'
    expected_actions = @('ENTER_LONG','ENTER_SHORT','NOTHING')
    operator = @{ profile=$Profile; master_account=$MasterAccount }
    market = $validationMarket
    portfolio = $portfolio
    policy = $policy
    freshness = @{ mnq_age_seconds=[math]::Round($age,1); accepted=$age -le [double]$policy.snapshot_max_age_seconds }
    instruction = 'Decide independently from this complete current cycle. Stale or incomplete critical state requires NOTHING.'
}
$modelCycle = [ordered]@{
    schema_version = 'glitch.hermes.redacted_cycle.v1'
    name = 'live_flat_entry_cycle'
    expected_actions = @('ENTER_LONG','ENTER_SHORT','NOTHING')
    market = [ordered]@{
        schema_version = $market.schema_version
        created_utc = $market.created_utc
        snapshot_id = $market.snapshot_id
        snapshot_hash = $market.snapshot_hash
        source_mode = $market.source_mode
        current_price = $decisionPrice
        instruments = @($mnq)
    }
    local_safety_attestation = [ordered]@{
        paper_simulation = $true
        group_flat = $true
        no_working_orders = $true
        local_risk_firewall_authoritative = $true
        private_account_state_intentionally_omitted = $true
    }
    execution_contract = [ordered]@{
        instrument = 'MNQ'
        account = $MasterAccount
        operator_profile = $Profile
        quantity = 1
        order_type = 'MARKET'
        protective_bracket_required = $true
        decision_scope = 'new_entry_or_nothing'
    }
    paper_exploration = [ordered]@{
        master_quantity = 1
        risk_per_contract_usd_range = @($explorationRiskMinimum,$explorationRiskMaximum)
        risk_budget_source = 'local_aggregate_safety_limit'
        preferred_minimum_reward_risk = 1.5
        preferred_reward_risk = 2.0
        objective = 'Take a clear falsifiable multi-timeframe setup; label non-archetype entries discretionary_candidate:<name>.'
        guardrail = 'Do not force a trade when direction, invalidation, or reward/risk is unclear.'
    }
    learning_context = [ordered]@{
        source = 'verbatim NinjaTrader GlitchData journals'
        directory = '/opt/glitch-data/journal/nt'
        profile_filter = $Profile
        account_filter = $MasterAccount
        instruction = 'Read only recent records attributable to this operator_profile and master account. Treat legacy or unattributed records as context, never as this profile performance. Journal evidence may inform judgment but cannot alter policy or safety limits.'
    }
    freshness = @{ mnq_age_seconds=[math]::Round($age,1); accepted=$age -le [double]$policy.snapshot_max_age_seconds }
    privacy = 'No portfolio, balances, PnL, positions, orders, allowlists, or private policy are supplied. Local validators remain authoritative.'
    instruction = 'Assess only the supplied MNQ market state. Emit a new-entry intent or NOTHING; local risk and portfolio state are enforced after inference.'
}

$activeDir = Join-Path $Capsule 'tests'
New-Item -ItemType Directory -Force -Path $activeDir | Out-Null
$active = Join-Path $activeDir "active-cycle-$safeProfile.json"
$modelCycle | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $active
$evidence = Join-Path $PSScriptRoot "tests\out\live-$safeProfile-$runId"
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$fixture = Join-Path $evidence 'cycle.json'
$modelFixture = Join-Path $evidence 'model-cycle.json'
$outputPath = Join-Path $evidence 'intent.json'
$usage = Join-Path $evidence 'usage.json'
$stderrPath = Join-Path $evidence 'hermes.stderr.txt'
$validationCycle | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $fixture
Copy-Item -LiteralPath $active -Destination $modelFixture

$prompt = "Read /opt/glitch-data/tests/active-cycle-$safeProfile.json as the complete privacy-redacted MNQ entry-decision cycle. You are profile $Profile, permanently bound to master $MasterAccount. Read only recent matching records from /opt/glitch-data/journal/nt as bounded trading-journal evidence. Echo operator_profile=$Profile and account=$MasterAccount exactly. The local_safety_attestation is authoritative: private account details are intentionally absent and must not be treated as invalid input. Apply glitch-observe-market, glitch-form-thesis, then glitch-build-intent. Steelman bull, bear, flat, aggressive, and conservative cases; record only their compact factual summaries in decision_audit, not private chain-of-thought. In paper exploration, take a clear falsifiable entry when multi-timeframe direction, invalidation, and reward/risk support it; otherwise emit NOTHING with a specific market reason. Return exactly one glitch.intent.v2 JSON object and nothing else. Do not call or describe any order endpoint."
if ($PrepareOnly) {
    Remove-Item -LiteralPath $active -Force -ErrorAction SilentlyContinue
    [ordered]@{
        schema_version = 'glitch.hermes.cycle_preparation.v1'
        run_id = $runId
        prepared = $true
        transmitted = $false
        evidence = $evidence
        model_cycle = $modelFixture
        local_validation_cycle = $fixture
    } | ConvertTo-Json -Depth 6
    return
}
$hermes = Join-Path $HermesRoot 'hermes'
$stage = 'hermes_inference'
$pythonCommand = (Get-Command python -ErrorAction Stop).Source
$output = Invoke-HermesBounded -Python $pythonCommand -Hermes $hermes -ProfileName $Profile -Usage $usage -Prompt $prompt -StdoutPath $outputPath -StderrPath $stderrPath -TimeoutSeconds $HermesTimeoutSeconds

$stage = 'validate_intent'
$schema = Join-Path $PSScriptRoot '..\..\glitch_hermes_docs\schemas\intent.v2.schema.json'
$validator = Join-Path $PSScriptRoot 'tests\validate_intent.py'
& python $validator $fixture $outputPath $schema | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Intent validation failed; no POST performed.' }
$intent = Get-Content -LiteralPath $outputPath -Raw | ConvertFrom-Json

$posted = $false
$superseded = $false
$responseBody = $null
if ($PostPaper) {
    $stage = 'paper_prepost_gate'
    if ($policy.mode -ne 'paper' -or [bool]$policy.executor_enabled) { throw 'PostPaper requires mode=paper and executor_enabled=false.' }
    $latest = Invoke-RestMethod -Uri 'http://127.0.0.1:8787/snapshot/market' -Headers $headers -TimeoutSec 15
    if ([string]$latest.snapshot_hash -ne [string]$intent.snapshot_hash) {
        $superseded = $true
        $responseBody = 'snapshot_superseded_before_post'
    } else {
        $stage = 'paper_post'
        try {
            $response = Invoke-WebRequest -Uri 'http://127.0.0.1:8788/intent' -Method Post -Headers $headers -ContentType 'application/json' -Body ($intent | ConvertTo-Json -Depth 10 -Compress) -UseBasicParsing -TimeoutSec 15
            $posted = $true
            $responseBody = $response.Content
        } catch {
            if ($_.Exception.Response) {
                $reader = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())
                try { $responseBody = $reader.ReadToEnd() } finally { $reader.Dispose() }
            }
            throw "Paper POST rejected; no order exists. $responseBody"
        }
    }
}

Remove-Item -LiteralPath $active -Force -ErrorAction SilentlyContinue
$stage = 'complete'
$result = [ordered]@{
    schema_version = 'glitch.hermes.cycle_result.v1'
    run_id = $runId
    validated = $true
    posted = $posted
    superseded = $superseded
    action = $intent.action
    operator_profile = $intent.operator_profile
    master_account = $intent.account
    snapshot_hash = $intent.snapshot_hash
    evidence = $evidence
    response = $responseBody
}
Write-CycleJournal -Status 'completed' -Stage $stage -Intent $intent -Posted $posted -Superseded $superseded -Reason $responseBody
$result | ConvertTo-Json -Depth 6
