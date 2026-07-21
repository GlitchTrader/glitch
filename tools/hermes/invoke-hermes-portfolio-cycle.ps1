param(
    [switch]$PrepareOnly,
    [switch]$PromptOnly,
    [switch]$ManagementOnly,
    [string]$NormalizeBatchPath,
    [string]$ExpectedSnapshotHash,
    [string]$Profile = 'glitch',
    [string]$HermesRoot = (Join-Path $env:LOCALAPPDATA 'hermes\hermes-agent'),
    [string]$Capsule = 'D:\ab\projects\glitch\Glitch-Hermes-Data',
    [ValidateRange(30, 600)][int]$HermesTimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
if ($Profile -ne 'glitch') { throw 'The portfolio operator must use the single glitch Hermes profile.' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
    $manifest = Get-Content -LiteralPath (Join-Path $repo 'hermes-profile\operator.json') -Raw | ConvertFrom-Json
$runId = [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$cycleId = "glitch-portfolio-$runId"
$lockPath = Join-Path $gd 'ai\hermes-portfolio-cycle.lock'
$stagePath = Join-Path $gd 'ai\hermes-portfolio-cycle-stage.json'
$lock = $null
$active = $null

function Set-PortfolioCycleStage([string]$Stage, [hashtable]$Fields = @{}) {
    $record = [ordered]@{
        schema_version = 'glitch.hermes.portfolio_stage.v1'
        updated_utc = [datetime]::UtcNow.ToString('o')
        cycle_id = $cycleId
        stage = $Stage
    }
    foreach ($key in $Fields.Keys) { $record[$key] = $Fields[$key] }
    $dir = Split-Path -Parent $stagePath
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [IO.File]::WriteAllText($stagePath, ($record | ConvertTo-Json -Depth 8 -Compress), [Text.UTF8Encoding]::new($false))
}

function Get-NullableNumber {
    param([object]$Value, [string]$Name)
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        throw "Required MNQ feature is unavailable: $Name"
    }
    return [double]$Value
}

function Get-OptionalNumber {
    param([object]$Value)
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }
    return [double]$Value
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

function Test-ArchetypeCondition {
    param([double]$Actual, [string]$Operator, [object]$Expected)
    $target = [double]$Expected
    switch ($Operator) {
        '==' { return [math]::Abs($Actual - $target) -lt 0.000000001 }
        '<=' { return $Actual -le $target }
        '>=' { return $Actual -ge $target }
        '<'  { return $Actual -lt $target }
        '>'  { return $Actual -gt $target }
        default { throw "Unsupported archetype operator: $Operator" }
    }
}

function Get-BarGeometry {
    param([object]$Bar)
    $close = Get-NullableNumber $Bar.close 'bar_close'
    $open = Get-OptionalNumber $Bar.open
    $high = Get-OptionalNumber $Bar.high
    $low = Get-OptionalNumber $Bar.low
    $ohlcComplete = $null -ne $open -and $null -ne $high -and $null -ne $low
    if (-not $ohlcComplete) {
        $open = $close
        $high = $close
        $low = $close
    }
    if ($high -lt $low) { throw 'Bar geometry is invalid: high below low.' }
    $range = $high - $low
    $body = [math]::Abs($close - $open)
    $upperWick = $high - [math]::Max($open, $close)
    $lowerWick = [math]::Min($open, $close) - $low
    $closeLocation = if ($range -gt 0) { ($close - $low) / $range } else { 0.5 }
    [ordered]@{
        open = $open
        high = $high
        low = $low
        close = $close
        ohlc_complete = $ohlcComplete
        direction = if ($close -gt $open) { 'up' } elseif ($close -lt $open) { 'down' } else { 'flat' }
        body_points = [math]::Round($body, 4)
        range_points = [math]::Round($range, 4)
        upper_wick_points = [math]::Round($upperWick, 4)
        lower_wick_points = [math]::Round($lowerWick, 4)
        close_location = [math]::Round($closeLocation, 4)
    }
}

function Invoke-HermesBounded {
    param(
        [string]$Python, [string]$Hermes, [string]$ProfileName,
        [string]$Usage, [string]$Prompt, [string]$StdoutPath,
        [string]$StderrPath, [int]$TimeoutSeconds
    )
    $arguments = @(
        ('"' + $Hermes + '"'), '-p', $ProfileName,
        '--toolsets', 'clarify',
        '--skills', 'glitch-observe-market,glitch-form-thesis,glitch-build-intent',
        '--usage-file', ('"' + $Usage + '"'), '-z', ('"' + $Prompt.Replace('"', '\"') + '"')
    ) -join ' '
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $Python
    $start.Arguments = $arguments
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
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
    $stdoutTask.Result | Set-Content -LiteralPath $StdoutPath
    $stderrTask.Result | Set-Content -LiteralPath $StderrPath
    if ($process.ExitCode -ne 0) { throw "Hermes failed with exit code $($process.ExitCode)." }
}

function Invoke-NativeBounded {
    param(
        [string]$FileName,
        [string[]]$Arguments,
        [int]$TimeoutSeconds,
        [string]$Stage
    )
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $FileName
    $start.Arguments = (($Arguments | ForEach-Object {
        $value = [string]$_
        if ($value -match '[\s"]') { '"' + $value.Replace('"', '\"') + '"' } else { $value }
    }) -join ' ')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    if (-not $process.Start()) { throw "$Stage did not start." }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill($true) } catch { try { $process.Kill() } catch { } }
        $process.WaitForExit()
        throw "$Stage timed out after $TimeoutSeconds seconds."
    }
    $stdout = $stdoutTask.Result
    $stderr = $stderrTask.Result
    if ($process.ExitCode -ne 0) {
        $message = (($stdout, $stderr) -join ' ').Trim()
        throw "$Stage failed with exit code $($process.ExitCode): $message"
    }
    return @($stdout)
}

function Normalize-HermesBatchJson {
    param([string]$Path)
    function Convert-HermesFieldToString([object]$Value, [string[]]$PreferredProperties) {
        if ($null -eq $Value) { return $Value }
        if ($Value -is [string]) { return [string]$Value }
        foreach ($property in $PreferredProperties) {
            if ($Value.PSObject.Properties.Name -contains $property) {
                $candidate = [string]$Value.$property
                if (-not [string]::IsNullOrWhiteSpace($candidate)) { return $candidate }
            }
        }
        return $Value
    }
    function Repair-HermesBatchScalarFields([object]$Batch) {
        $changed = $false
        foreach ($decision in @($Batch.decisions)) {
            if ($null -eq $decision) { continue }
            $operatorProfile = $null
            if ($decision.PSObject.Properties.Name -contains 'operator_profile') {
                $operatorProfile = Convert-HermesFieldToString $decision.operator_profile @('route_id','name','profile','operator_profile')
                if ($operatorProfile -is [string] -and $operatorProfile -ne $decision.operator_profile) {
                    $decision.operator_profile = $operatorProfile
                    $changed = $true
                }
            } elseif ($decision.PSObject.Properties.Name -contains 'route_id') {
                $operatorProfile = Convert-HermesFieldToString $decision.route_id @('route_id','name','profile')
                if ($operatorProfile -is [string] -and -not [string]::IsNullOrWhiteSpace($operatorProfile)) {
                    $decision | Add-Member -NotePropertyName operator_profile -NotePropertyValue $operatorProfile -Force
                    $changed = $true
                }
            }
            if ($decision.PSObject.Properties.Name -contains 'account') {
                $account = Convert-HermesFieldToString $decision.account @('master_account','account','name')
                if ($account -is [string] -and $account -ne $decision.account) {
                    $decision.account = $account
                    $changed = $true
                }
            }
        }
        return $changed
    }

    $raw = Get-Content -LiteralPath $Path -Raw
    try {
        $batch = $raw | ConvertFrom-Json
        if (Repair-HermesBatchScalarFields $batch) {
            $json = $batch | ConvertTo-Json -Depth 30 -Compress
            [IO.File]::WriteAllText($Path, $json, (New-Object Text.UTF8Encoding($false)))
        }
        return
    } catch {
        $trimmed = $raw.TrimEnd()
        if (-not $trimmed.EndsWith('}]}')) { throw }
        $candidate = $trimmed.Substring(0, $trimmed.Length - 2) + '}]}'
        try {
            $batch = $candidate | ConvertFrom-Json
            $null = Repair-HermesBatchScalarFields $batch
            $json = $batch | ConvertTo-Json -Depth 30 -Compress
            [IO.File]::WriteAllText($Path, $json, (New-Object Text.UTF8Encoding($false)))
            return
        } catch {
            throw
        }
    }
}

function Compress-HermesDecisionAudit {
    param([string]$Path)
    $batch = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $auditKeys = @(
        'bull_case',
        'bear_case',
        'flat_case',
        'aggressive_case',
        'conservative_case',
        'decisive_evidence',
        'disconfirming_evidence',
        'change_condition'
    )
    foreach ($decision in @($batch.decisions)) {
        if ($null -eq $decision.decision_audit) { continue }
        foreach ($key in $auditKeys) {
            $value = [string]$decision.decision_audit.$key
            if ([string]::IsNullOrWhiteSpace($value)) { continue }
            $words = @($value -split '\s+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            if ($words.Count -gt 14) {
                $decision.decision_audit.$key = (($words | Select-Object -First 14) -join ' ')
            }
        }
        $decision.decision_audit.final_choice = $decision.action
    }
    $json = $batch | ConvertTo-Json -Depth 30 -Compress
    [IO.File]::WriteAllText($Path, $json, (New-Object Text.UTF8Encoding($false)))
}

function Convert-ExecutionMessageToObject {
    param([string]$Message)
    $parsed = [ordered]@{}
    if ([string]::IsNullOrWhiteSpace($Message)) { return $parsed }
    foreach ($part in ($Message -split '\|')) {
        $kv = $part -split '=', 2
        if ($kv.Count -ne 2) { continue }
        $key = [string]$kv[0]
        $value = [string]$kv[1]
        $number = 0.0
        if ([double]::TryParse($value, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
            $parsed[$key] = [math]::Round($number, 4)
        } else {
            $parsed[$key] = $value
        }
    }
    return $parsed
}

function Get-FilledAiEntriesToday {
    param([string]$GlitchData, [datetime]$NowUtc)
    $counts = @{}
    $seenIntentIds = @{}
    $path = Join-Path $GlitchData 'intents\executions.jsonl'
    if (-not (Test-Path -LiteralPath $path)) { return $counts }
    $dayStart = [datetime]::SpecifyKind($NowUtc.Date, [DateTimeKind]::Utc)
    foreach ($line in Get-Content -LiteralPath $path) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $row = $line | ConvertFrom-Json } catch { continue }
        if ([string]$row.code -notin @('master_entry_filled','group_entry_filled')) { continue }
        $recorded = [datetime]::Parse([string]$row.recorded_utc).ToUniversalTime()
        if ($recorded -lt $dayStart -or $recorded -gt $NowUtc) { continue }
        $intentId = [string]$row.intent_id
        if ([string]::IsNullOrWhiteSpace($intentId) -or $seenIntentIds.ContainsKey($intentId)) { continue }
        $details = Convert-ExecutionMessageToObject -Message ([string]$row.message)
        $master = [string]$details['master']
        if ([string]::IsNullOrWhiteSpace($master)) { continue }
        $seenIntentIds[$intentId] = $true
        if (-not $counts.ContainsKey($master)) { $counts[$master] = 0 }
        $counts[$master] = [int]$counts[$master] + 1
    }
    return $counts
}

if ($NormalizeBatchPath) {
    Normalize-HermesBatchJson -Path $NormalizeBatchPath
    Compress-HermesDecisionAudit -Path $NormalizeBatchPath
    [ordered]@{
        schema_version = 'glitch.hermes.batch_normalize_result.v1'
        normalized = $true
        transmitted = $false
        path = $NormalizeBatchPath
    } | ConvertTo-Json -Depth 4
    return
}

try {
    try {
        $lock = [IO.File]::Open($lockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        Set-PortfolioCycleStage 'lock_acquired'
    } catch {
        if (Test-Path -LiteralPath $lockPath) {
            throw 'Another unified Hermes portfolio cycle is already active.'
        }
        throw "Unable to create the unified portfolio-cycle lock: $($_.Exception.Message)"
    }

    Set-PortfolioCycleStage 'read_token'
    $token = (Get-Content -LiteralPath (Join-Path $gd 'telemetry.token') -Raw).Trim()
    $headers = @{ Authorization = "Bearer $token" }
    Set-PortfolioCycleStage 'read_market_snapshot'
    $market = Invoke-RestMethod -Uri 'http://127.0.0.1:8787/snapshot/market' -Headers $headers -TimeoutSec 15
    if (-not $ManagementOnly -and -not [string]::IsNullOrWhiteSpace($ExpectedSnapshotHash) -and
        [string]$market.snapshot_hash -ne $ExpectedSnapshotHash) {
        throw "Snapshot hash changed before inference: expected $ExpectedSnapshotHash, got $($market.snapshot_hash)."
    }
    Set-PortfolioCycleStage 'read_portfolio_snapshot'
    $portfolio = Invoke-RestMethod -Uri 'http://127.0.0.1:8787/snapshot/portfolio' -Headers $headers -TimeoutSec 15
    Set-PortfolioCycleStage 'read_policy'
    $policy = Get-Content -LiteralPath (Join-Path $gd 'ai\policy.json') -Raw | ConvertFrom-Json
    if ($policy.mode -ne 'paper' -or [bool]$policy.executor_enabled) {
        throw 'Unified portfolio inference requires paper mode with executor disabled.'
    }
    if (-not [bool]$policy.ai_enabled -or [bool]$policy.ai_kill_switch) {
        throw 'Unified portfolio inference requires AI enabled with kill switch off.'
    }

    $bindings = @{}
    foreach ($binding in @($policy.profile_account_bindings)) {
        $parts = [string]$binding -split '=', 2
        if ($parts.Count -eq 2) { $bindings[$parts[0].Trim()] = $parts[1].Trim() }
    }
    $mnq = @($market.instruments | Where-Object instrument -eq 'MNQ') | Select-Object -First 1
    if (-not $mnq) { throw 'MNQ missing from market snapshot.' }
    $instrumentFullName = if ($mnq.PSObject.Properties.Name -contains 'instrument_full_name') { [string]$mnq.instrument_full_name } else { '' }
    $expectedFallbackContract = 'MNQ ' + (Get-EquityIndexFrontContractSuffix ([datetime]::UtcNow))
    $compiledContractFallbackAvailable = Test-CompiledKnownMicroContractFallback 'MNQ'
    $instrumentContractGateActive = ([string]::IsNullOrWhiteSpace($instrumentFullName) -or $instrumentFullName.Trim().Equals('MNQ', [System.StringComparison]::OrdinalIgnoreCase)) -and -not $compiledContractFallbackAvailable
    $marketCreatedUtc = if ($market.created_utc) { [datetime]::Parse([string]$market.created_utc).ToUniversalTime() } else { $null }
    $observedUtc = if ($mnq.timestamp_utc) { [datetime]::Parse([string]$mnq.timestamp_utc).ToUniversalTime() } else { $null }
    $rawObservedAge = if ($observedUtc) { ([datetime]::UtcNow - $observedUtc).TotalSeconds } else { [double]::PositiveInfinity }
    if ($rawObservedAge -lt -5 -and $marketCreatedUtc) {
        $observedUtc = $marketCreatedUtc
    }
    $observedAge = if ($observedUtc) { ([datetime]::UtcNow - $observedUtc).TotalSeconds } else { [double]::PositiveInfinity }
    if ($observedAge -lt -5 -or $observedAge -gt [double]$policy.snapshot_max_age_seconds) {
        throw "MNQ underlying observation is stale ($([math]::Round($observedAge,1)) seconds); no external inference."
    }
    $oneMinute = @($mnq.timeframe_bars | Where-Object { [int]$_.minutes -eq 1 }) | Select-Object -First 1
    $decisionPrice = if ($mnq.current_price -and [double]$mnq.current_price -gt 0) {
        [double]$mnq.current_price
    } elseif ($oneMinute -and [double]$oneMinute.close -gt 0) {
        [double]$oneMinute.close
    } else { throw 'MNQ decision price unavailable.' }

    $bars = @{}
    foreach ($minutes in @(1,5,15,60)) {
        $bar = @($mnq.timeframe_bars | Where-Object { [int]$_.minutes -eq $minutes }) | Select-Object -First 1
        if (-not $bar) { throw "MNQ ${minutes}m bar missing from market snapshot." }
        $barUtc = if ($bar.utc_time) { [datetime]::Parse([string]$bar.utc_time).ToUniversalTime() } else { $null }
        $barAge = if ($barUtc) { ([datetime]::UtcNow - $barUtc).TotalSeconds } else { [double]::PositiveInfinity }
        if ($barAge -lt -5 -or $barAge -gt [double]$policy.snapshot_max_age_seconds) {
            throw "MNQ ${minutes}m observation is stale ($([math]::Round($barAge,1)) seconds); no external inference."
        }
        $bars[$minutes] = $bar
    }
    Set-PortfolioCycleStage 'market_validated'
    $sessionHigh = Get-NullableNumber $mnq.session.high 'session_high'
    $sessionLow = Get-NullableNumber $mnq.session.low 'session_low'
    $previousHigh = Get-NullableNumber $mnq.session.previous_high 'prev_session_high'
    $previousLow = Get-NullableNumber $mnq.session.previous_low 'prev_session_low'
    if ($sessionHigh -le $sessionLow) { throw 'MNQ session range is invalid.' }
    if ($previousHigh -le $previousLow) { throw 'MNQ previous-session range is invalid.' }

    $i1 = $bars[1].indicators
    $i5 = $bars[5].indicators
    $i15 = $bars[15].indicators
    $i60 = $bars[60].indicators
    $close1 = Get-NullableNumber $bars[1].close 'close_1m'
    $atr1 = Get-NullableNumber $i1.atr 'atr_1m'
    $atr5 = Get-NullableNumber $i5.atr 'atr_5m'
    $atr60 = Get-NullableNumber $i60.atr 'atr_60m'
    $adx60 = Get-NullableNumber $i60.adx 'adx_60m'
    $diPlus60 = Get-NullableNumber $i60.di_plus 'di_plus_60m'
    $diMinus60 = Get-NullableNumber $i60.di_minus 'di_minus_60m'
    $diSpread60 = $diPlus60 - $diMinus60
    $snapshotUtc = if ($observedUtc) { $observedUtc } else { [datetimeoffset]::Parse([string]$market.created_utc).UtcDateTime }
    $eastern = [System.TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time')
    $snapshotEt = [System.TimeZoneInfo]::ConvertTimeFromUtc($snapshotUtc, $eastern)
    $minuteOfDay = ($snapshotEt.Hour * 60) + $snapshotEt.Minute
    $session5 = if ($minuteOfDay -ge 570 -and $minuteOfDay -lt 690) { 'US_open' }
        elseif ($minuteOfDay -ge 690 -and $minuteOfDay -lt 900) { 'US_mid' }
        elseif ($minuteOfDay -ge 900 -and $minuteOfDay -lt 960) { 'US_close' }
        elseif ($minuteOfDay -ge 960 -and $minuteOfDay -lt 1080) { 'post' }
        elseif ([string]$mnq.session.name -eq 'Asia') { 'Asia' }
        elseif ([string]$mnq.session.name -eq 'London') { 'Europe' }
        else { 'overnight' }

    $archetypePath = Join-Path $repo 'glitch_hermes_docs\memory\archetypes.v2.json'
    $archetypeKnowledge = Get-Content -LiteralPath $archetypePath -Raw | ConvertFrom-Json
    $volThresholds = @($archetypeKnowledge.vol_tertile_thresholds_atrnorm60)
    if ($volThresholds.Count -ne 2) { throw 'Archetype v2 volatility thresholds are invalid.' }
    $atrNorm60 = $atr60 / $close1
    $volRegime = if ($atrNorm60 -le [double]$volThresholds[0]) { 'vol_lo' }
        elseif ($atrNorm60 -le [double]$volThresholds[1]) { 'vol_md' }
        else { 'vol_hi' }
    $trendRegime = if ($adx60 -ge 25 -and $diSpread60 -gt 0) { 'trend_up' }
        elseif ($adx60 -ge 25 -and $diSpread60 -le 0) { 'trend_down' }
        else { 'range' }
    $geo1 = Get-BarGeometry $bars[1]
    $geo5 = Get-BarGeometry $bars[5]
    $geo15 = Get-BarGeometry $bars[15]
    $ohlcComplete = [bool]$geo1['ohlc_complete'] -and [bool]$geo5['ohlc_complete'] -and [bool]$geo15['ohlc_complete']
    $upperBreakoutAttempt1m = if ($ohlcComplete -and (($sessionHigh - [double]$geo1['high']) -le 4.0)) { 1.0 } else { 0.0 }
    $upperBreakoutAcceptance1m = if ($ohlcComplete -and $upperBreakoutAttempt1m -eq 1.0 -and $close1 -ge ($sessionHigh - 1.0) -and $close1 -gt $previousHigh -and [double]$geo1['close_location'] -ge 0.70) { 1.0 } else { 0.0 }
    $failedUpperBreakout1m = if ($ohlcComplete -and $upperBreakoutAttempt1m -eq 1.0 -and $close1 -lt ($sessionHigh - 4.0) -and [double]$geo1['close_location'] -lt 0.50) { 1.0 } else { 0.0 }
    $lowerBreakdownAttempt1m = if ($ohlcComplete -and (([double]$geo1['low'] - $sessionLow) -le 4.0)) { 1.0 } else { 0.0 }
    $lowerBreakdownAcceptance1m = if ($ohlcComplete -and $lowerBreakdownAttempt1m -eq 1.0 -and $close1 -le ($sessionLow + 1.0) -and $close1 -lt $previousLow -and [double]$geo1['close_location'] -le 0.30) { 1.0 } else { 0.0 }
    $failedLowerBreakdown1m = if ($ohlcComplete -and $lowerBreakdownAttempt1m -eq 1.0 -and $close1 -gt ($sessionLow + 4.0) -and [double]$geo1['close_location'] -gt 0.50) { 1.0 } else { 0.0 }
    $sessRangePosNow = ($close1 - $sessionLow) / ($sessionHigh - $sessionLow)
    $upperExtremeBearTurn1m = if ($sessRangePosNow -ge 0.80 -and [string]$geo1['direction'] -eq 'down' -and [string]$geo5['direction'] -eq 'down' -and ($sessionHigh - $close1) -ge 20.0 -and [double]$geo1['close_location'] -le 0.45) { 1.0 } else { 0.0 }
    $lowerExtremeBullTurn1m = if ($sessRangePosNow -le 0.20 -and [string]$geo1['direction'] -eq 'up' -and [string]$geo5['direction'] -eq 'up' -and ($close1 - $sessionLow) -ge 20.0 -and [double]$geo1['close_location'] -ge 0.55) { 1.0 } else { 0.0 }
    $nearSupportReclaimAfterFlush = if ($sessionLow -lt $previousLow -and ($close1 - $previousLow) -ge -2.0 -and [string]$geo1['direction'] -eq 'up' -and [double]$geo1['close_location'] -ge 0.70) { 1.0 } else { 0.0 }
    $lowerExtremeBearContinuationPressure1m = if ($sessRangePosNow -le 0.20 -and $close1 -lt $previousLow -and [string]$geo1['direction'] -eq 'down' -and [string]$geo5['direction'] -eq 'down' -and [double]$geo1['close_location'] -le 0.45 -and [double]$geo5['close_location'] -le 0.35 -and (($close1 - $sessionLow) -ge 8.0) -and (((Get-NullableNumber $i5.di_plus 'di_plus_5m') - (Get-NullableNumber $i5.di_minus 'di_minus_5m')) -le -10.0)) { 1.0 } else { 0.0 }
    $lowerExtremeBearContinuationPressure5m = if ($sessRangePosNow -le 0.20 -and $close1 -lt $previousLow -and [string]$geo5['direction'] -eq 'down' -and [string]$geo15['direction'] -eq 'down' -and [double]$geo5['close_location'] -le 0.35 -and [double]$geo15['close_location'] -le 0.45 -and (($close1 - $sessionLow) -ge 30.0) -and (((Get-NullableNumber $i5.di_plus 'di_plus_5m') - (Get-NullableNumber $i5.di_minus 'di_minus_5m')) -le -8.0) -and (Get-NullableNumber $i15.cci 'cci_15m') -le -90.0) { 1.0 } else { 0.0 }
    $lowerExtremeTrendPullbackShort1m = if ($trendRegime -eq 'trend_down' -and $sessRangePosNow -le 0.20 -and $close1 -lt $previousLow -and [string]$geo5['direction'] -eq 'up' -and [string]$geo1['direction'] -eq 'down' -and [double]$geo1['close_location'] -le 0.45 -and (($close1 - $sessionLow) -ge 30.0 -and ((Get-NullableNumber $i5.di_plus 'di_plus_5m') - (Get-NullableNumber $i5.di_minus 'di_minus_5m')) -le -8.0)) { 1.0 } else { 0.0 }
    $machineFeatures = [ordered]@{
        derived_from = 'current completed MNQ bars using r06 mining formulas'
        session5 = $session5
        atr_norm_60m = $atrNorm60
        vol_regime = $volRegime
        trend_regime = $trendRegime
        di_spread_60m = $diSpread60
        di_spread_5m = (Get-NullableNumber $i5.di_plus 'di_plus_5m') - (Get-NullableNumber $i5.di_minus 'di_minus_5m')
        ohlc_complete = $ohlcComplete
        sess_range_pos = $sessRangePosNow
        sess_range_zone = if ($sessRangePosNow -le 0.20) { 'lower_extreme' } elseif ($sessRangePosNow -ge 0.80) { 'upper_extreme' } else { 'middle' }
        points_from_session_low = $close1 - $sessionLow
        points_from_session_high = $sessionHigh - $close1
        prev_range_pos = ($close1 - $previousLow) / ($previousHigh - $previousLow)
        above_prev_high = if ($close1 -gt $previousHigh) { 1.0 } else { 0.0 }
        below_prev_low = if ($close1 -lt $previousLow) { 1.0 } else { 0.0 }
        support_flush_below_prev_low = if ($sessionLow -lt $previousLow) { 1.0 } else { 0.0 }
        support_reclaim_after_flush = if ($sessionLow -lt $previousLow -and $close1 -gt $previousLow) { 1.0 } else { 0.0 }
        near_support_reclaim_after_flush = $nearSupportReclaimAfterFlush
        points_above_prev_low = $close1 - $previousLow
        candle_1m_direction = $geo1['direction']
        candle_1m_close_location = $geo1['close_location']
        candle_1m_body_points = $geo1['body_points']
        candle_1m_upper_wick_points = $geo1['upper_wick_points']
        candle_1m_lower_wick_points = $geo1['lower_wick_points']
        candle_5m_direction = $geo5['direction']
        candle_5m_close_location = $geo5['close_location']
        candle_15m_direction = $geo15['direction']
        candle_15m_close_location = $geo15['close_location']
        upper_breakout_attempt_1m = $upperBreakoutAttempt1m
        upper_breakout_acceptance_1m = $upperBreakoutAcceptance1m
        failed_upper_breakout_1m = $failedUpperBreakout1m
        lower_breakdown_attempt_1m = $lowerBreakdownAttempt1m
        lower_breakdown_acceptance_1m = $lowerBreakdownAcceptance1m
        failed_lower_breakdown_1m = $failedLowerBreakdown1m
        upper_extreme_bear_turn_1m = $upperExtremeBearTurn1m
        lower_extreme_bull_turn_1m = $lowerExtremeBullTurn1m
        lower_extreme_bear_continuation_pressure_1m = $lowerExtremeBearContinuationPressure1m
        lower_extreme_bear_continuation_pressure_5m = $lowerExtremeBearContinuationPressure5m
        lower_extreme_trend_pullback_short_1m = $lowerExtremeTrendPullbackShort1m
        rsi_1m_15m_diff = (Get-NullableNumber $i1.rsi 'rsi_1m') - (Get-NullableNumber $i15.rsi 'rsi_15m')
        macd_h_norm_5m = (Get-NullableNumber $i5.macd_histogram 'macd_histogram_5m') / $atr5
        atr_1m = $atr1
        adx_1m = Get-NullableNumber $i1.adx 'adx_1m'
        adx_15m = Get-NullableNumber $i15.adx 'adx_15m'
        cci_15m = Get-NullableNumber $i15.cci 'cci_15m'
        stoch_k_1m = Get-NullableNumber $i1.stoch_k 'stoch_k_1m'
        z_score_15m = Get-NullableNumber $i15.z_score 'z_score_15m'
    }
    $archetypeEvaluation = @()
    foreach ($archetype in @($archetypeKnowledge.archetypes | Where-Object { $_.status -in @('validated','candidate') })) {
        $preconditions = @()
        $preconditions += [ordered]@{ feature='vol_regime'; actual=$volRegime; allowed=@($archetype.regime_preconditions.vol); matched=($volRegime -in @($archetype.regime_preconditions.vol)) }
        $preconditions += [ordered]@{ feature='trend_regime'; actual=$trendRegime; allowed=@($archetype.regime_preconditions.trend); matched=($trendRegime -in @($archetype.regime_preconditions.trend)) }
        $preconditions += [ordered]@{ feature='session5'; actual=$session5; allowed=@($archetype.regime_preconditions.session); matched=($session5 -in @($archetype.regime_preconditions.session)) }
        $triggerResults = @()
        foreach ($trigger in @($archetype.trigger)) {
            $featureName = [string]$trigger.feature
            $property = $machineFeatures.GetEnumerator() | Where-Object Key -eq $featureName | Select-Object -First 1
            if (-not $property) {
                $triggerResults += [ordered]@{ feature=$featureName; available=$false; op=[string]$trigger.op; expected=$trigger.value; matched=$false }
                continue
            }
            $actual = [double]$property.Value
            $triggerResults += [ordered]@{ feature=$featureName; available=$true; actual=$actual; op=[string]$trigger.op; expected=$trigger.value; matched=(Test-ArchetypeCondition $actual ([string]$trigger.op) $trigger.value) }
        }
        $preconditionsMatched = @($preconditions | Where-Object { -not $_.matched }).Count -eq 0
        $triggersMatched = @($triggerResults | Where-Object { -not $_.matched }).Count -eq 0
        $archetypeEvaluation += [ordered]@{
            archetype_id = [string]$archetype.archetype_id
            status = [string]$archetype.status
            side = [string]$archetype.side
            preconditions = $preconditions
            triggers = $triggerResults
            exact_match = ($preconditionsMatched -and $triggersMatched)
            execution_notes = [string]$archetype.execution_notes_2026_07_13
        }
    }

    $modelBooks = @()
    $validationBooks = @()
    $outcomeRows = @()
    $filledEntriesToday = Get-FilledAiEntriesToday -GlitchData $gd -NowUtc ([datetime]::UtcNow)
    $canonicalOutcomePath = Join-Path $gd 'intents\hermes-trade-outcomes.jsonl'
    if (Test-Path -LiteralPath $canonicalOutcomePath) {
        $outcomeRows = @(Get-Content -LiteralPath $canonicalOutcomePath |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_ | ConvertFrom-Json })
    }
    foreach ($book in @($manifest.books)) {
        $route = [string]$book.route_id
        $account = [string]$book.master_account
        Set-PortfolioCycleStage 'preflight_start' @{ route_id=$route; account=$account }
        if (-not $bindings.ContainsKey($route) -or [string]$bindings[$route] -ne $account) {
            throw "Glitch policy route mismatch: $route/$account"
        }
        $preflightOutput = Invoke-NativeBounded -FileName 'powershell.exe' -TimeoutSeconds 20 -Stage "Portfolio preflight for $route/$account" -Arguments @(
            '-NoProfile',
            '-ExecutionPolicy', 'Bypass',
            '-File', (Join-Path $PSScriptRoot 'preflight-open.ps1'),
            '-Target', 'paper',
            '-Profile', $route,
            '-MasterAccount', $account
        )
        $preflight = ($preflightOutput -join [Environment]::NewLine) | ConvertFrom-Json
        Set-PortfolioCycleStage 'preflight_complete' @{ route_id=$route; account=$account }
        $failed = @($preflight.failed)
        $positionOnlyFailures = @('master_flat','executor_group_accounts_flat','executor_group_no_working_orders')
        $unexpectedFailures = @($failed | Where-Object { $_ -notin $positionOnlyFailures })
        if ($unexpectedFailures.Count -gt 0) {
            throw "Portfolio preflight failed for $route`: $($unexpectedFailures -join ',')"
        }
        $tradesToday = if ($filledEntriesToday.ContainsKey($account)) { [int]$filledEntriesToday[$account] } else { 0 }
        $marketOhlcGateActive = -not $ohlcComplete
        $riskMaximumBase = [math]::Min(80.0, [double]$preflight.executor_group.max_risk_per_contract_by_group_usd)
        $dailyRiskRemaining = [double]::PositiveInfinity
        $memberStates = @()
        foreach ($member in @($preflight.executor_group.members)) {
            $accountRow = @($portfolio.accounts | Where-Object account -eq ([string]$member.account)) | Select-Object -First 1
            $positions = @()
            if ($accountRow) { $positions = @($accountRow.positions) }
            $mnqPositions = @($positions | Where-Object instrument_root -eq 'MNQ')
            $position = $mnqPositions | Select-Object -First 1
            $memberQuantity = [math]::Max(1, [int]$member.quantity)
            $realizedPnl = if ($accountRow -and $null -ne $accountRow.realized_pnl) { [double]$accountRow.realized_pnl } else { 0.0 }
            $lossToday = if ($realizedPnl -lt 0) { -$realizedPnl } else { 0.0 }
            $remainingLossBudget = [double]$policy.max_daily_loss_usd - $lossToday
            $remainingPerMasterContract = $remainingLossBudget / $memberQuantity
            if ($remainingPerMasterContract -lt $dailyRiskRemaining) { $dailyRiskRemaining = $remainingPerMasterContract }
            $memberStates += [ordered]@{
                account = [string]$member.account
                expected_quantity = $memberQuantity
                position_count = $positions.Count
                mnq_position_count = $mnqPositions.Count
                market_position = if ($position) { [string]$position.market_position } else { $null }
                quantity = if ($position) { [int]$position.quantity } else { 0 }
                average_price = if ($position) { [double]$position.average_price } else { $null }
                realized_pnl = $realizedPnl
                unrealized_pnl = if ($accountRow) { [double]$accountRow.unrealized_pnl } else { $null }
                daily_loss_remaining_usd = [math]::Round([double]$remainingLossBudget, 2)
                working_orders = if ($accountRow) { [int]$accountRow.working_orders } else { -1 }
            }
        }
        if ([double]::IsPositiveInfinity($dailyRiskRemaining)) { $dailyRiskRemaining = $riskMaximumBase }
        $dailyRiskRemainingRounded = [math]::Round([math]::Max(0.0, [double]$dailyRiskRemaining), 2)
        $dailyLossLockoutActive = $dailyRiskRemaining -lt 20.0
        $riskMaximum = [math]::Min($riskMaximumBase, $dailyRiskRemainingRounded)
        $tf1 = $null
        $tf5 = $null
        foreach ($tf in @($compactTimeframes)) {
            if ([int]$tf.minutes -eq 1) { $tf1 = $tf }
            if ([int]$tf.minutes -eq 5) { $tf5 = $tf }
        }
        $atr1Points = if ($tf1 -and $null -ne $tf1.indicators.atr) { [double]$tf1.indicators.atr } else { 0.0 }
        $atr5Points = if ($tf5 -and $null -ne $tf5.indicators.atr) { [double]$tf5.indicators.atr } else { 0.0 }
        $range1Points = if ($tf1 -and $null -ne $tf1.candle.range_points) { [double]$tf1.candle.range_points } else { 0.0 }
        $volatilityStopFloorUsd = if ($volRegime -eq 'vol_lo') { 30.0 } elseif ($volRegime -eq 'vol_md') { 40.0 } else { 50.0 }
        $rawNoiseFloorPoints = @(
            ($volatilityStopFloorUsd / 2.0),
            ($atr1Points * 0.75),
            ($atr5Points * 0.35),
            ($range1Points * 0.75)
        ) | Measure-Object -Maximum | ForEach-Object { [double]$_.Maximum }
        $riskCapacityBelowNoiseFloor = $riskMaximum -lt $volatilityStopFloorUsd
        $minimumEntryRiskUsd = [math]::Min($riskMaximum, $volatilityStopFloorUsd)
        $noiseFloorStopPoints = [math]::Round([math]::Min(($riskMaximum / 2.0), [math]::Max(($minimumEntryRiskUsd / 2.0), $rawNoiseFloorPoints)), 2)
        $openDirections = @($memberStates | Where-Object { $_.mnq_position_count -eq 1 } | ForEach-Object market_position | Sort-Object -Unique)
        $openProtected = $memberStates.Count -eq @($preflight.executor_group.members).Count `
            -and $memberStates.Count -gt 0 `
            -and @($memberStates | Where-Object {
                $_.position_count -ne 1 `
                    -or $_.mnq_position_count -ne 1 `
                    -or $_.quantity -ne $_.expected_quantity `
                    -or $_.working_orders -lt 2 `
                    -or ($_.working_orders % 2) -ne 0
            }).Count -eq 0 `
            -and $openDirections.Count -eq 1 `
            -and $openDirections[0] -in @('Long','Short')
        $managementEligible = $unexpectedFailures.Count -eq 0 -and $openProtected
        $eligible = $failed.Count -eq 0 -and -not $dailyLossLockoutActive -and -not $marketOhlcGateActive -and -not $instrumentContractGateActive -and -not $riskCapacityBelowNoiseFloor -and -not $ManagementOnly
        $allowedActions = @(if ($managementEligible) {
            'HOLD'; 'EXIT'
        } elseif ($eligible) {
            'ENTER_LONG'; 'ENTER_SHORT'; 'NOTHING'
        } else {
            'NOTHING'
        })
        if ($eligible -and $riskMaximum -lt 20.0) { throw "Book risk capacity below paper minimum: $route" }
        $adaptiveEntryRules = @()
        foreach ($actionCandidate in @('ENTER_LONG','ENTER_SHORT')) {
            $routeActionRows = @($outcomeRows |
                Where-Object { [string]$_.route_id -eq $route -and [string]$_.action -eq $actionCandidate } |
                Sort-Object { [datetimeoffset]::Parse([string]$_.exit_utc) } -Descending)
            if ($routeActionRows.Count -eq 0) { continue }
            $lossStreak = 0
            foreach ($row in $routeActionRows) {
                if ([double]$row.group_realized_pnl_usd -lt 0) { $lossStreak++ } else { break }
            }
            $lastActionRow = $routeActionRows | Select-Object -First 1
            $lastAccountOutcomes = @($lastActionRow.account_outcomes)
            $lastAvgMfe = if ($lastAccountOutcomes.Count -gt 0) {
                ($lastAccountOutcomes | Measure-Object observed_mfe_usd -Average).Average
            } else { 0.0 }
            $lastCloseKinds = @($lastAccountOutcomes | ForEach-Object close_kind | Sort-Object -Unique)
            $adaptiveEntryRules += [ordered]@{
                action = $actionCandidate
                closed_trades = $routeActionRows.Count
                current_loss_streak = $lossStreak
                last_group_pnl_usd = [math]::Round([double]$lastActionRow.group_realized_pnl_usd, 2)
                last_avg_account_mfe_usd = [math]::Round([double]$lastAvgMfe, 2)
                last_close_kinds = $lastCloseKinds
                rule = if ($lossStreak -ge 1 -and [double]$lastAvgMfe -lt 15.0) {
                    'Do not repeat this action from a weak reclaim/fade thesis unless hard trigger, higher-timeframe alignment, or materially different location is present.'
                } elseif ($lossStreak -ge 1) {
                    'Require materially different evidence from the latest losing thesis before repeating this action.'
                } else {
                    'No loss-streak penalty; still require edge, location, and bracket geometry.'
                }
            }
        }
        $modelBooks += [ordered]@{
            book_id = [string]$book.book_id
            route_id = $route
            master_account = $account
            group_role = [string]$book.group_role
            mandate = [string]$book.mandate
            state = if ($managementEligible) { 'open_protected' } else { 'flat' }
            eligible_for_new_entry = $eligible
            eligible_for_management = $managementEligible
            allowed_actions = $allowedActions
            trades_today = $tradesToday
            daily_loss_lockout_active = $dailyLossLockoutActive
            daily_loss_remaining_per_master_contract_usd = $dailyRiskRemainingRounded
            risk_capacity_below_noise_floor_active = $riskCapacityBelowNoiseFloor
            market_ohlc_gate_active = $marketOhlcGateActive
            instrument_contract_gate_active = $instrumentContractGateActive
            instrument_full_name = $instrumentFullName
            expected_fallback_contract = $expectedFallbackContract
            compiled_contract_fallback_available = $compiledContractFallbackAvailable
            open_position = if ($managementEligible) {
                [ordered]@{
                    side = $openDirections[0]
                    group_members = $memberStates
                }
            } else { $null }
            master_quantity = 1
            risk_per_master_contract_usd_range = @($minimumEntryRiskUsd, $riskMaximum)
            volatility_stop_floor_usd = $volatilityStopFloorUsd
            noise_floor_stop_points = $noiseFloorStopPoints
            bracket_geometry_guidance = 'MNQ noise floor applies: do not use clean-but-tiny stops. Stop must clear noise_floor_stop_points, usually risk at least volatility_stop_floor_usd, fit structure beyond noise, and target must remain reachable. If only a tight stop fits current chop, choose NOTHING.'
            preferred_minimum_reward_risk = 1.55
            minimum_planned_reward_risk = 1.75
            adaptive_entry_rules = $adaptiveEntryRules
            instruction = if ($managementEligible) {
                'Manage the existing protected AI-owned position. Choose HOLD only when thesis and remaining opportunity still justify exposure. Choose EXIT when evidence weakens, invalidates, opportunity cost dominates, or one-contract MNQ has bankable open profit no longer strongly improving. Current unrealized PnL is first-class evidence: after roughly $30-$40 favorable open PnL on small-risk scalps, or $80-$100 on larger/high-vol trades, protect discovery profits unless the hold case is clearly stronger. Do not let large favorable excursion become a stop outcome. Do not emit a new entry or alter brackets.'
            } elseif (-not $eligible) {
                'Emit NOTHING because local Glitch state makes this book ineligible for a new entry.'
            } elseif ([string]$book.book_id -eq 'aggressive') {
                'Paper discovery: choose the highest-relative-expectancy falsifiable long or short thesis when either side has an observable edge and valid structural bracket. This book is allowed to act before perfect acceptance/rejection confirmation; missing acceptance is a risk input, not an automatic veto, when location, momentum, volatility, and reachable target geometry still support a positive expectancy trade. Use the MNQ noise-floor bracket guidance; do not propose a tight stop that ordinary MNQ chop can tag. Do not require an exact archetype match and do not cite its absence as decisive evidence for NOTHING.'
            } elseif ([string]$book.book_id -eq 'conservative') {
                'Paper discovery with controlled risk: prefer confirmation, but do not require perfect acceptance when a machine trigger, location, volatility, and bracket geometry support a falsifiable probe. This book is allowed to take one-contract Sim probes only when the stop is beyond normal MNQ noise and within the supplied risk range. NOTHING beats a probe when the only available stop is clean but too tight for current volatility.'
            } elseif ([string]$book.book_id -eq 'stay_revert') {
                'Paper discovery: while flat, establish the best-supported directional posture when either side has an observable edge and valid structural bracket. This book is explicitly testing stay-or-revert control, so it may seed a position from a probabilistic directional hypothesis before perfect confirmation when invalidation and target geometry are clear. Use stops beyond normal MNQ noise; exact archetypes are priors only, not a reason to remain unseeded.'
            } else {
                'Decide independently for this book. Archetypes are evidence, not permission gates.'
            }
        }
        $validationBooks += [ordered]@{
            book_id = [string]$book.book_id
            route_id = $route
            master_account = $account
            eligible_for_new_entry = $eligible
            eligible_for_management = $managementEligible
            allowed_actions = $allowedActions
            trades_today = $tradesToday
            daily_loss_lockout_active = $dailyLossLockoutActive
            daily_loss_remaining_per_master_contract_usd = $dailyRiskRemainingRounded
            risk_capacity_below_noise_floor_active = $riskCapacityBelowNoiseFloor
            market_ohlc_gate_active = $marketOhlcGateActive
            instrument_contract_gate_active = $instrumentContractGateActive
            instrument_full_name = $instrumentFullName
            expected_fallback_contract = $expectedFallbackContract
            compiled_contract_fallback_available = $compiledContractFallbackAvailable
            max_loss_per_master_contract_usd = $riskMaximum
            min_loss_per_master_contract_usd = $minimumEntryRiskUsd
            volatility_stop_floor_usd = $volatilityStopFloorUsd
            noise_floor_stop_points = $noiseFloorStopPoints
            minimum_reward_risk = 1.55
            minimum_planned_reward_risk = 1.75
            open_position = if ($managementEligible) { [ordered]@{ side=$openDirections[0]; group_members=$memberStates } } else { $null }
        }
    }

    Set-PortfolioCycleStage 'journal_sync_start'
    $journalSync = Invoke-NativeBounded -FileName 'powershell.exe' -TimeoutSeconds 20 -Stage 'NinjaTrader journal sync' -Arguments @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', (Join-Path $PSScriptRoot 'sync-nt-journal.ps1'),
        '-GlitchData', $gd,
        '-Capsule', $Capsule
    )
    Set-PortfolioCycleStage 'journal_sync_complete'

    $compactTimeframes = @(
        foreach ($minutes in @(1,5,15,60)) {
            $bar = $bars[$minutes]
            $geometry = Get-BarGeometry $bar
            [ordered]@{
                minutes = $minutes
                utc_time = $bar.utc_time
                open = $geometry['open']
                high = $geometry['high']
                low = $geometry['low']
                close = $bar.close
                ohlc_complete = $geometry['ohlc_complete']
                candle = [ordered]@{
                    direction = $geometry['direction']
                    body_points = $geometry['body_points']
                    range_points = $geometry['range_points']
                    upper_wick_points = $geometry['upper_wick_points']
                    lower_wick_points = $geometry['lower_wick_points']
                    close_location = $geometry['close_location']
                }
                indicators = [ordered]@{
                    atr = $bar.indicators.atr
                    adx = $bar.indicators.adx
                    rsi = $bar.indicators.rsi
                    stoch_k = $bar.indicators.stoch_k
                    z_score = $bar.indicators.z_score
                    average_price = $bar.indicators.average_price
                    di_plus = $bar.indicators.di_plus
                    di_minus = $bar.indicators.di_minus
                    cci = $bar.indicators.cci
                    macd_histogram = $bar.indicators.macd_histogram
                }
            }
        }
    )
    $compactArchetypes = @(
        foreach ($evaluation in $archetypeEvaluation) {
            [ordered]@{
                archetype_id = $evaluation.archetype_id
                status = $evaluation.status
                side = $evaluation.side
                exact_match = $evaluation.exact_match
                matched_preconditions = @($evaluation.preconditions | Where-Object { [bool]$_.matched } | ForEach-Object { [string]$_.feature })
                failed_preconditions = @($evaluation.preconditions | Where-Object { -not [bool]$_.matched } | ForEach-Object { [string]$_.feature })
                matched_triggers = @($evaluation.triggers | Where-Object { [bool]$_.matched } | ForEach-Object { [string]$_.feature })
                failed_triggers = @($evaluation.triggers | Where-Object { -not [bool]$_.matched } | ForEach-Object { [string]$_.feature })
                execution_notes = $evaluation.execution_notes
            }
        }
    )
    $tradeLedgerPath = Join-Path $Capsule 'journal\nt\tradeledger.tsv'
    $recentCompletedTrades = @()
    if (Test-Path -LiteralPath $tradeLedgerPath) {
        $recentCompletedTrades = @(Get-Content -LiteralPath $tradeLedgerPath |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and -not $_.StartsWith('#') } |
            Select-Object -Last 3)
    }
    $outcomePath = Join-Path $Capsule 'journal\nt\hermes-trade-outcomes.jsonl'
    $recentHermesOutcomes = @()
    if (Test-Path -LiteralPath $outcomePath) {
        $recentHermesOutcomes = @(Get-Content -LiteralPath $outcomePath |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Last 3 |
            ForEach-Object {
                $row = $_ | ConvertFrom-Json
                [ordered]@{
                    route_id = $row.route_id
                    action = $row.action
                    confidence = $row.confidence
                    exit_utc = $row.exit_utc
                    group_realized_pnl_usd = $row.group_realized_pnl_usd
                    account_outcomes = @($row.account_outcomes | ForEach-Object {
                        [ordered]@{
                            account = $_.account
                            realized_pnl_usd = $_.realized_pnl_usd
                            close_kind = $_.close_kind
                            observed_mfe_usd = $_.observed_mfe_usd
                            observed_mae_usd = $_.observed_mae_usd
                        }
                    })
                    reason = $row.reason
                }
            })
    }
    $profitCaptureTriggerUsd = 30.0
    $routeOutcomeSummary = @()
    foreach ($routeGroup in @($outcomeRows | Group-Object route_id)) {
        $groupRows = @($routeGroup.Group)
        $allAccountOutcomes = @($groupRows | ForEach-Object { $_.account_outcomes })
        $avgMfe = if ($allAccountOutcomes.Count -gt 0) { ($allAccountOutcomes | Measure-Object observed_mfe_usd -Average).Average } else { 0.0 }
        $avgMae = if ($allAccountOutcomes.Count -gt 0) { ($allAccountOutcomes | Measure-Object observed_mae_usd -Average).Average } else { 0.0 }
        $stopCount = @($allAccountOutcomes | Where-Object close_kind -eq 'stop').Count
        $stoppedMfeRows = @($allAccountOutcomes | Where-Object { $_.close_kind -eq 'stop' -and $null -ne $_.observed_mfe_usd })
        $maxStoppedMfe = if ($stoppedMfeRows.Count -gt 0) { ($stoppedMfeRows | Measure-Object observed_mfe_usd -Maximum).Maximum } else { 0.0 }
        $sumPnl = [double](($groupRows | Measure-Object group_realized_pnl_usd -Sum).Sum)
        $captureEligibleLegs = @($allAccountOutcomes | Where-Object {
            $null -ne $_.observed_mfe_usd -and [double]$_.observed_mfe_usd -ge $profitCaptureTriggerUsd
        })
        $hypotheticalCapturedPnl = 0.0
        foreach ($outcome in $allAccountOutcomes) {
            $realized = if ($null -ne $outcome.realized_pnl_usd) { [double]$outcome.realized_pnl_usd } else { 0.0 }
            $mfe = if ($null -ne $outcome.observed_mfe_usd) { [double]$outcome.observed_mfe_usd } else { 0.0 }
            if ($mfe -ge $profitCaptureTriggerUsd) {
                $hypotheticalCapturedPnl += $profitCaptureTriggerUsd
            } else {
                $hypotheticalCapturedPnl += $realized
            }
        }
        $routeOutcomeSummary += [ordered]@{
            route_id = [string]$routeGroup.Name
            closed_trades = $groupRows.Count
            group_realized_pnl_usd = [math]::Round($sumPnl, 2)
            avg_account_mfe_usd = [math]::Round([double]$avgMfe, 2)
            avg_account_mae_usd = [math]::Round([double]$avgMae, 2)
            max_stopped_mfe_usd = [math]::Round([double]$maxStoppedMfe, 2)
            stopped_account_legs = $stopCount
            cap30_legs = $captureEligibleLegs.Count
            cap30_pnl = [math]::Round($hypotheticalCapturedPnl, 2)
            cap30_improve = [math]::Round($hypotheticalCapturedPnl - $sumPnl, 2)
            lesson = if ($avgMfe -gt 30 -and $stopCount -gt 0) {
                'Recent trades offered favorable excursion but closed at stops; require better location, quicker target feasibility, or EXIT discipline.'
            } else {
                'Insufficient repeated favorable excursion evidence for a management lesson.'
            }
        }
    }
    $actionOutcomeSummary = @()
    foreach ($actionGroup in @($outcomeRows | Group-Object route_id, action)) {
        $groupRows = @($actionGroup.Group)
        if ($groupRows.Count -eq 0) { continue }
        $allAccountOutcomes = @($groupRows | ForEach-Object { $_.account_outcomes })
        $lossRows = @($groupRows | Where-Object { [double]$_.group_realized_pnl_usd -lt 0 })
        $winRows = @($groupRows | Where-Object { [double]$_.group_realized_pnl_usd -gt 0 })
        $stoppedMfeRows = @($allAccountOutcomes | Where-Object {
            $_.close_kind -eq 'stop' -and $null -ne $_.observed_mfe_usd -and [double]$_.observed_mfe_usd -ge 30
        })
        $sortedRows = @($groupRows | Sort-Object { [datetimeoffset]::Parse([string]$_.exit_utc) } -Descending)
        $lossStreak = 0
        foreach ($row in $sortedRows) {
            if ([double]$row.group_realized_pnl_usd -lt 0) { $lossStreak++ } else { break }
        }
        $lastRow = $sortedRows | Select-Object -First 1
        $routeName = [string]($groupRows[0].route_id)
        $actionName = [string]($groupRows[0].action)
        $avgMfe = if ($allAccountOutcomes.Count -gt 0) { ($allAccountOutcomes | Measure-Object observed_mfe_usd -Average).Average } else { 0.0 }
        $avgMae = if ($allAccountOutcomes.Count -gt 0) { ($allAccountOutcomes | Measure-Object observed_mae_usd -Average).Average } else { 0.0 }
        $sumPnl = [double](($groupRows | Measure-Object group_realized_pnl_usd -Sum).Sum)
        $captureEligibleLegs = @($allAccountOutcomes | Where-Object {
            $null -ne $_.observed_mfe_usd -and [double]$_.observed_mfe_usd -ge $profitCaptureTriggerUsd
        })
        $hypotheticalCapturedPnl = 0.0
        foreach ($outcome in $allAccountOutcomes) {
            $realized = if ($null -ne $outcome.realized_pnl_usd) { [double]$outcome.realized_pnl_usd } else { 0.0 }
            $mfe = if ($null -ne $outcome.observed_mfe_usd) { [double]$outcome.observed_mfe_usd } else { 0.0 }
            if ($mfe -ge $profitCaptureTriggerUsd) {
                $hypotheticalCapturedPnl += $profitCaptureTriggerUsd
            } else {
                $hypotheticalCapturedPnl += $realized
            }
        }
        $actionOutcomeSummary += [ordered]@{
            route_id = $routeName
            action = $actionName
            trades = $groupRows.Count
            losses = $lossRows.Count
            wins = $winRows.Count
            current_loss_streak = $lossStreak
            group_realized_pnl_usd = [math]::Round($sumPnl, 2)
            avg_account_mfe_usd = [math]::Round([double]$avgMfe, 2)
            avg_account_mae_usd = [math]::Round([double]$avgMae, 2)
            stopped_after_mfe_30_count = $stoppedMfeRows.Count
            cap30_legs = $captureEligibleLegs.Count
            cap30_pnl = [math]::Round($hypotheticalCapturedPnl, 2)
            cap30_improve = [math]::Round($hypotheticalCapturedPnl - $sumPnl, 2)
            last_reason = if ($lastRow) { [string]$lastRow.reason } else { $null }
            lesson = if ($lossStreak -ge 2 -and $stoppedMfeRows.Count -gt 0) {
                'Repeated losses with bankable MFE: require stronger entry confirmation or earlier EXIT.'
            } elseif ($lossStreak -ge 2) {
                'Repeated losses: downgrade this route/action unless current evidence is materially different.'
            } else {
                'Insufficient repeated evidence for route/action penalty.'
            }
        }
    }
    $recentExecutionFailures = @()
    $executionJournalPath = Join-Path $gd 'intents\executions.jsonl'
    if (Test-Path -LiteralPath $executionJournalPath) {
        $recentExecutionFailures = @(Get-Content -LiteralPath $executionJournalPath |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Last 80 |
            ForEach-Object {
                try { $_ | ConvertFrom-Json } catch { $null }
            } |
            Where-Object { $_ -and $_.status -eq 'failed' } |
            Select-Object -Last 3 |
            ForEach-Object {
                $failureDetails = Convert-ExecutionMessageToObject -Message ([string]$_.message)
                [ordered]@{
                    intent_id = $_.intent_id
                    code = $_.code
                    details = $failureDetails
                    lesson = if ([string]$_.code -eq 'group_execution_outside_structural_bracket') {
                        'Live price escaped the proposed bracket before safe entry; wait for retest, fresh structure, or faster confirmation.'
                    } elseif ([string]$_.code -eq 'group_execution_reward_risk_degraded') {
                        'Live reward/risk degraded below Glitch minimum; require more execution headroom or skip.'
                    } else {
                        'Blocked by Glitch; preserve safety and adapt timing.'
                    }
                }
            })
    }

    $validationCycle = [ordered]@{
        cycle_id = $cycleId
        market = @{
            current_price=$decisionPrice
            snapshot_hash=$market.snapshot_hash
            sess_range_zone=$machineFeatures.sess_range_zone
            points_from_session_low=$machineFeatures.points_from_session_low
            points_above_prev_low=$machineFeatures.points_above_prev_low
            support_reclaim_after_flush=$machineFeatures.support_reclaim_after_flush
            near_support_reclaim_after_flush=$machineFeatures.near_support_reclaim_after_flush
            lower_breakdown_acceptance_1m=$machineFeatures.lower_breakdown_acceptance_1m
            timeframes=$compactTimeframes
        }
        books = $validationBooks
    }

    if ($PrepareOnly) {
        $evidence = Join-Path $PSScriptRoot "tests\out\portfolio-$runId"
        New-Item -ItemType Directory -Force -Path $evidence | Out-Null
        $scenarioPath = Join-Path $evidence 'scenario.json'
        $modelPath = Join-Path $evidence 'model-cycle.json'
        $validationCycle | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $scenarioPath
        [ordered]@{
            schema_version = 'glitch.hermes.portfolio_cycle.v1'
            cycle_id = $cycleId
            market = [ordered]@{
                current_price = $decisionPrice
                snapshot_hash = $market.snapshot_hash
                instrument = 'MNQ'
                machine_features = $machineFeatures
                archetype_evaluation = $compactArchetypes
            }
            books = $modelBooks
            prepare_only_minimal = $true
        } | ConvertTo-Json -Depth 12 -Compress | Set-Content -LiteralPath $modelPath
        Set-PortfolioCycleStage 'prepare_only_return' @{ evidence=$evidence; management_only=[bool]$ManagementOnly }
        [ordered]@{
            schema_version = 'glitch.hermes.portfolio_preparation.v1'
            cycle_id = $cycleId
            prepared = $true
            transmitted = $false
            prompt_bytes = 0
            prompt_over_limit = $false
            books = $validationBooks
            evidence = $evidence
        } | ConvertTo-Json -Depth 8
        return
    }

    $modelCycle = [ordered]@{
        schema_version = 'glitch.hermes.portfolio_cycle.v1'
        cycle_id = $cycleId
        operator_profile = 'glitch'
        expected_batch_schema = 'glitch.intent.batch.v1'
        market = [ordered]@{
            schema_version = $market.schema_version
            created_utc = $market.created_utc
            snapshot_id = $market.snapshot_id
            snapshot_hash = $market.snapshot_hash
            source_mode = $market.source_mode
            current_price = $decisionPrice
            instrument = 'MNQ'
            session = [ordered]@{
                name = $mnq.session.name
                high = $sessionHigh
                low = $sessionLow
                previous_high = $previousHigh
                previous_low = $previousLow
            }
            timeframes = $compactTimeframes
            machine_features = $machineFeatures
            archetype_evaluation = $compactArchetypes
        }
        local_safety_attestation = [ordered]@{
            paper_simulation = $true
            executor_unarmed = $true
            local_firewall_authoritative = $true
            sim_account_state_bounded_to_configured_books = $true
        }
        books = $modelBooks
        learning_context = [ordered]@{
            source = 'locally bounded NinjaTrader outcome and trade-ledger digest'
            route_outcome_summary = $routeOutcomeSummary
            action_outcome_summary = $actionOutcomeSummary
            recent_execution_failures = $recentExecutionFailures
            recent_hermes_outcomes = $recentHermesOutcomes
            recent_completed_trade_rows = $recentCompletedTrades
            instruction = 'Use outcomes only; attribute route/account/action; never mix Sim/live. Repeated losses need different evidence. Negative PnL with positive MFE means improve entry/target/management, not risk. cap30_improve quantifies bankable profit surrendered to stop. Execution failures are not trades: outside_structural_bracket=live escaped bracket; reward_risk_degraded=R:R below minimum. One outcome is not expectancy proof.'
        }
        instruction = 'Return one decision per book. Obey allowed_actions. Open_protected choose HOLD/EXIT only. machine_features/archetypes are priors, not gates. Flat books need edge plus falsifiable bracket. Location is first-class. adaptive_entry_rules bind route/action loss controls: repeat penalized actions only with decisive different evidence. Balanced requires cleaner confirmation; conservative may take bounded Sim probes when mandate, trigger, and bracket justify discovery. Aggressive/stay_revert may enter early when evidence, mandate, and bracket justify Sim discovery. Soft triggers: upper_extreme_bear_turn_1m, lower_extreme_bull_turn_1m, lower_extreme_bear_continuation_pressure_1m/5m. Lower-extreme bearish continuation is permission only: skip if session-low headroom is too small, snapback dominates, or bracket cannot protect risk with reachable reward. MNQ stop geometry: obey each book risk_per_master_contract_usd_range and volatility_stop_floor_usd; do not use tight clean stops that ordinary MNQ noise can tag. Escaped brackets/degraded R:R require headroom, retest, or skip. NOTHING needs concrete flat-case advantage over bounded probes. Target feasibility: compare ATR5, regime, structure, horizon; prefer reachable 0.75-1.5 ATR5 targets with stop risk beyond noise and quick profit protection available.'
    }
    $promptBooks = @($modelBooks | ForEach-Object {
        $riskRange = @($_.risk_per_master_contract_usd_range)
        [ordered]@{
            book_id = $_.book_id
            route_id = $_.route_id
            master_account = $_.master_account
            style = if ($_.book_id -eq 'aggressive') {
                'early_discovery'
            } elseif ($_.book_id -eq 'conservative') {
                'confirmed_probe'
            } elseif ($_.book_id -eq 'stay_revert') {
                'directional_stay_or_revert'
            } else {
                'balanced'
            }
            state = $_.state
            allowed_actions = $_.allowed_actions
            eligible_for_new_entry = $_.eligible_for_new_entry
            eligible_for_management = $_.eligible_for_management
            trades_today = $_.trades_today
            daily_loss_lockout_active = $_.daily_loss_lockout_active
            daily_loss_remaining_per_master_contract_usd = $_.daily_loss_remaining_per_master_contract_usd
            risk_capacity_below_noise_floor_active = $_.risk_capacity_below_noise_floor_active
            min_loss_per_master_contract_usd = if ($riskRange.Count -gt 0) { $riskRange[0] } else { $null }
            max_loss_per_master_contract_usd = if ($riskRange.Count -gt 1) { $riskRange[1] } else { $null }
            volatility_stop_floor_usd = $_.volatility_stop_floor_usd
            noise_floor_stop_points = $_.noise_floor_stop_points
            minimum_planned_reward_risk = $_.minimum_planned_reward_risk
            adaptive_entry_rules = $_.adaptive_entry_rules
            open_position = $_.open_position
        }
    })
    $promptCycle = [ordered]@{
        schema_version = 'glitch.hermes.portfolio_prompt_cycle.v1'
        compact_prompt_cycle = $true
        cycle_id = $cycleId
        expected_batch_schema = 'glitch.intent.batch.v1'
        market = [ordered]@{
            snapshot_hash = $market.snapshot_hash
            current_price = $decisionPrice
            instrument = 'MNQ'
            session = $modelCycle.market.session
            timeframes = $compactTimeframes
            machine_features = $machineFeatures
            archetype_evaluation = $compactArchetypes
        }
        local_safety_attestation = $modelCycle.local_safety_attestation
        books = $promptBooks
        learning_context = [ordered]@{
            source = 'compact local NinjaTrader outcome digest'
            route_outcome_summary = $routeOutcomeSummary
            action_outcome_summary = $actionOutcomeSummary
            recent_execution_failures = $recentExecutionFailures
            recent_hermes_outcomes = $recentHermesOutcomes
            recent_completed_trade_count = $recentCompletedTrades.Count
            instruction = 'Use outcome summaries, not raw ledger rows. Repeated losses need different evidence. Positive MFE with red close means improve location, target feasibility, and EXIT discipline. cap30_improve is bankable profit surrendered. Execution failures are timing/geometry evidence, not trades.'
        }
        instruction = $modelCycle.instruction
    }
    Set-PortfolioCycleStage 'model_cycle_built'
    Set-PortfolioCycleStage 'model_cycle_serialize_market_start'
    $null = $promptCycle.market | ConvertTo-Json -Depth 12 -Compress
    Set-PortfolioCycleStage 'model_cycle_serialize_books_start'
    $null = $promptCycle.books | ConvertTo-Json -Depth 12 -Compress
    Set-PortfolioCycleStage 'model_cycle_serialize_learning_start'
    $null = $promptCycle.learning_context | ConvertTo-Json -Depth 12 -Compress
    Set-PortfolioCycleStage 'model_cycle_serialize_attestation_start'
    $null = $promptCycle.local_safety_attestation | ConvertTo-Json -Depth 12 -Compress
    Set-PortfolioCycleStage 'model_cycle_serialize_start'
    $modelCycleJson = $promptCycle | ConvertTo-Json -Depth 12 -Compress
    Set-PortfolioCycleStage 'model_cycle_serialize_complete' @{ bytes=[Text.Encoding]::UTF8.GetByteCount($modelCycleJson) }
    $activeDir = Join-Path $Capsule 'tests'
    New-Item -ItemType Directory -Force -Path $activeDir | Out-Null
    $active = Join-Path $activeDir 'active-portfolio-cycle.json'
    [IO.File]::WriteAllText($active, $modelCycleJson, [Text.UTF8Encoding]::new($false))
    $evidence = Join-Path $PSScriptRoot "tests\out\portfolio-$runId"
    New-Item -ItemType Directory -Force -Path $evidence | Out-Null
    $scenarioPath = Join-Path $evidence 'scenario.json'
    $modelPath = Join-Path $evidence 'model-cycle.json'
    $outputPath = Join-Path $evidence 'intent-batch.json'
    $usagePath = Join-Path $evidence 'usage.json'
    $stderrPath = Join-Path $evidence 'hermes.stderr.txt'
    $validationCycle | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $scenarioPath
    Copy-Item -LiteralPath $active -Destination $modelPath -Force
    Set-PortfolioCycleStage 'evidence_written' @{ evidence=$evidence }

    $outputContract = 'OUTPUT_CONTRACT: Outer keys: schema_version="glitch.intent.batch.v1", cycle_id, decisions. Decision keys: schema_version="glitch.intent.v2", intent_id UUID, created_utc UTC, instrument="MNQ", account, operator_profile, action, confidence 0..1, snapshot_hash exact, model_version="gpt-5.6-luna", prompt_version="glitch-hermes-v2", reason, decision_audit. account/operator_profile must be scalar strings copied exactly from book master_account/route_id; never objects. reason<=20 words. decision_audit keys: bull_case,bear_case,flat_case,aggressive_case,conservative_case,decisive_evidence,disconfirming_evidence,change_condition,final_choice. audit values<=14 words except final_choice; final_choice=action. Use allowed_actions only. ENTER_LONG/SHORT require quantity=1, order_type="MARKET", stop_loss, take_profit_1 as action/reason siblings; else NOTHING. Tick-rounded; risk in supplied USD range at $2/point; stop distance must clear noise_floor_stop_points and min_loss_per_master_contract_usd/volatility_stop_floor_usd; R:R meets minimum_planned_reward_risk and keep Glitch live-entry headroom. NOTHING/HOLD/EXIT omit quantity/order/SL/TP. Do not use book_id, side, ENTER.'
    $prompt = 'CURRENT_CYCLE is sole authority. Paper discovery is probabilistic, not whitelist-only. Steelman bull,bear,flat,aggressive,conservative. Return batch/four decisions in order. Obey allowed_actions. Ineligible flat books emit NOTHING; open protected emit HOLD/EXIT. Eligible books never emit cycle_invalid. adaptive_entry_rules bind route/action loss controls; repeating penalized action requires decisive different evidence. Aggressive/stay_revert/conservative-probe may not cite no exact match, ordinary uncertainty, mixed timeframes, range, modest ADX, midpoint, or imperfect acceptance alone as abstention. upper_extreme_bear_turn_1m/lower_extreme_bull_turn_1m/lower_extreme_bear_continuation_pressure_5m/lower_extreme_trend_pullback_short_1m/support_reclaim_after_flush/near_support_reclaim_after_flush are valid soft discovery triggers with noise-aware brackets. A pullback-short trigger means a 1m failed bounce inside a larger down-trend, not permission to chase a fresh low; require valid headroom and bracket. Do not chase lower_extreme shorts or upper_extreme longs without acceptance. Do not propose tight MNQ stops below noise_floor_stop_points or volatility floor. Include location. Emit JSON only. ' + $outputContract + ' CURRENT_CYCLE=' + $modelCycleJson
    $promptBytes = [Text.Encoding]::UTF8.GetByteCount($prompt)

    if ($PrepareOnly) {
        Set-PortfolioCycleStage 'prepare_only_return' @{ evidence=$evidence; prompt_bytes=$promptBytes }
        [ordered]@{
            schema_version = 'glitch.hermes.portfolio_preparation.v1'
            cycle_id = $cycleId
            prepared = $true
            transmitted = $false
            prompt_bytes = $promptBytes
            prompt_over_limit = ($promptBytes -gt 24000)
            books = $validationBooks
            evidence = $evidence
        } | ConvertTo-Json -Depth 8
        return
    }

    if ($PromptOnly) {
        Set-PortfolioCycleStage 'prompt_only_return' @{ evidence=$evidence; prompt_bytes=$promptBytes; prompt_over_limit=($promptBytes -gt 24000) }
        [ordered]@{
            schema_version = 'glitch.hermes.portfolio_prompt_preview.v1'
            cycle_id = $cycleId
            prepared = $true
            transmitted = $false
            prompt_bytes = $promptBytes
            prompt_over_limit = ($promptBytes -gt 24000)
            compact_prompt_cycle = $true
            books = $validationBooks
            evidence = $evidence
        } | ConvertTo-Json -Depth 8
        return
    }

    if ($promptBytes -gt 24000) { throw "Compact Hermes prompt exceeds 24000 bytes ($promptBytes)." }

    $python = (Get-Command python -ErrorAction Stop).Source
    $hermes = Join-Path $HermesRoot 'hermes'
    Invoke-HermesBounded -Python $python -Hermes $hermes -ProfileName $Profile -Usage $usagePath `
        -Prompt $prompt -StdoutPath $outputPath -StderrPath $stderrPath -TimeoutSeconds $HermesTimeoutSeconds

    $usage = Get-Content -LiteralPath $usagePath -Raw | ConvertFrom-Json
    if ([int]$usage.api_calls -ne 1) {
        throw "Hermes portfolio inference used $($usage.api_calls) model calls; expected exactly one. No submission."
    }

    Normalize-HermesBatchJson -Path $outputPath
    Compress-HermesDecisionAudit -Path $outputPath
    & python (Join-Path $PSScriptRoot 'tests\validate_intent_batch.py') $scenarioPath $outputPath `
        (Join-Path $repo 'glitch_hermes_docs\schemas\intent.v2.schema.json') `
        (Join-Path $repo 'glitch_hermes_docs\schemas\intent-batch.v1.schema.json') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Portfolio intent batch validation failed; no submission.' }
    $batch = Get-Content -LiteralPath $outputPath -Raw | ConvertFrom-Json
    $journal = Join-Path $gd 'intents\hermes-portfolio-cycles.jsonl'
    [ordered]@{
        schema_version = 'glitch.hermes.portfolio_journal.v1'
        created_utc = [datetime]::UtcNow.ToString('o')
        cycle_id = $cycleId
        status = 'validated_no_submission'
        decisions = @($batch.decisions | ForEach-Object {
            [ordered]@{ route_id=$_.operator_profile; account=$_.account; action=$_.action; intent_id=$_.intent_id }
        })
        evidence = $evidence
    } | ConvertTo-Json -Depth 8 -Compress | Add-Content -LiteralPath $journal
    [ordered]@{
        schema_version = 'glitch.hermes.portfolio_result.v1'
        cycle_id = $cycleId
        validated = $true
        submitted = $false
        decisions = $batch.decisions
        evidence = $evidence
    } | ConvertTo-Json -Depth 12
} finally {
    if ($active) { Remove-Item -LiteralPath $active -Force -ErrorAction SilentlyContinue }
    if ($lock) { $lock.Dispose() }
    Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
    if (-not $PrepareOnly) { Remove-Item -LiteralPath $stagePath -Force -ErrorAction SilentlyContinue }
}
