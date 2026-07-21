param(
    [string]$Profile = 'glitch',
    [string]$GlitchData = (Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'),
    [string]$Capsule = (Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) 'Glitch-Hermes-Data'),
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
if ($Profile -ne 'glitch') { throw 'Only the Glitch Hermes profile may be reset by this script.' }

$profileRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "hermes\profiles\$Profile"))
$glitchRoot = [IO.Path]::GetFullPath($GlitchData)
$capsuleRoot = [IO.Path]::GetFullPath($Capsule)
$exchangeRoot = [IO.Path]::GetFullPath((Join-Path $glitchRoot 'hermes\exchange'))
$jobsPath = Join-Path $profileRoot 'cron\jobs.json'
$sessionReset = Join-Path $PSScriptRoot 'reset-named-hermes-session.py'
$hermesCommand = Get-Command hermes -ErrorAction Stop
$hermesPython = Join-Path (Split-Path $hermesCommand.Source -Parent) 'python.exe'

foreach ($required in @($profileRoot, $glitchRoot, $exchangeRoot)) {
    if (-not (Test-Path -LiteralPath $required -PathType Container)) {
        throw "Required reset root is missing: $required"
    }
}
foreach ($required in @($jobsPath, $sessionReset)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required reset helper is missing: $required"
    }
}
if (-not (Test-Path -LiteralPath $hermesPython -PathType Leaf)) {
    throw "Hermes Python runtime is missing: $hermesPython"
}

function Assert-UnderRoot([string]$Path, [string]$Root) {
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = $Root.TrimEnd('\') + '\'
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing path outside reset root: $full"
    }
    return $full
}

function Add-ExistingFile([Collections.Generic.List[string]]$List, [string]$Path, [string]$Root) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $List.Add((Assert-UnderRoot $Path $Root))
    }
}

function Add-FilesUnder([Collections.Generic.List[string]]$List, [string]$Path, [string]$Root) {
    if (Test-Path -LiteralPath $Path -PathType Container) {
        foreach ($item in Get-ChildItem -LiteralPath $Path -File -Recurse) {
            $List.Add((Assert-UnderRoot $item.FullName $Root))
        }
    }
}

$jobs = @((Get-Content -LiteralPath $jobsPath -Raw | ConvertFrom-Json).jobs)
$enabledJobs = @($jobs | Where-Object { $_.enabled -or $_.state -eq 'active' })

$files = [Collections.Generic.List[string]]::new()
foreach ($name in @(
    'received.jsonl', 'decisions.jsonl', 'executions.jsonl', 'hermes-trade-outcomes.jsonl',
    'intent_ids.txt', 'hermes-cycles.jsonl', 'hermes-cycles.glitch-aggressive.jsonl',
    'hermes-cycles.glitch-conservative.jsonl', 'hermes-cycles.glitch-stay-revert.jsonl',
    'hermes-portfolio-cycles.jsonl', 'hermes-sim-guard-ticks.jsonl',
    'hermes-opportunity-guard-state.json'
)) {
    Add-ExistingFile $files (Join-Path $glitchRoot "intents\$name") $glitchRoot
}

foreach ($relative in @(
    'glitch\decision-packets', 'glitch\minute-frames', 'glitch\events',
    'hermes\model-attempts', 'hermes\outbox', 'hermes\outbox-context', 'hermes\receipts', 'hermes\events'
)) {
    Add-FilesUnder $files (Join-Path $exchangeRoot $relative) $exchangeRoot
}
foreach ($relative in @(
    'glitch\latest-decision-packet.json', 'glitch\operator-directive.json',
    'hermes\latest-decision-packet.json', 'hermes\operator-directive.json',
    'hermes\operator-directives.jsonl', 'hermes\direct-cycle.lock'
)) {
    Add-ExistingFile $files (Join-Path $exchangeRoot $relative) $exchangeRoot
}
foreach ($relative in @(
    'hermes\supervisor\trade-episodes.jsonl',
    'hermes\supervisor\observations.jsonl',
    'hermes\supervisor\lessons.jsonl',
    'hermes\supervisor\trading-guidance.jsonl',
    'hermes\supervisor\plans.jsonl',
    'hermes\supervisor\daily-journal.jsonl',
    'hermes\supervisor\cognitive-changes.jsonl',
    'hermes\supervisor\current-guidance.json',
    'hermes\supervisor\current-plan.json',
    'hermes\supervisor\active-cognitive-overlay.json',
    'hermes\supervisor\active-trades.json',
    'hermes\supervisor\learning-state.json',
    'hermes\supervisor\learning-worker-status.json',
    'hermes\supervisor\learning-worker.log',
    'hermes\learning-cycle.lock'
)) {
    Add-ExistingFile $files (Join-Path $exchangeRoot $relative) $exchangeRoot
}
foreach ($name in @('control-commands.jsonl', 'cycles.jsonl')) {
    Add-ExistingFile $files (Join-Path $glitchRoot "hermes\$name") $glitchRoot
}

Add-FilesUnder $files (Join-Path $profileRoot 'cron\output') $profileRoot
Add-ExistingFile $files (Join-Path $profileRoot 'memories\USER.md') $profileRoot
Add-FilesUnder $files (Join-Path $capsuleRoot 'journal\nt') $capsuleRoot

$uniqueFiles = @($files | Sort-Object -Unique)
$previousHermesHome = $env:HERMES_HOME
try {
    $env:HERMES_HOME = $profileRoot
    $sessionState = (& $hermesPython $sessionReset --title trading --preserve-title chat | Out-String | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the named Hermes sessions.' }
}
finally {
    $env:HERMES_HOME = $previousHermesHome
}

$preview = [ordered]@{
    schema_version = 'glitch.hermes.trading_epoch_reset.v2'
    mode = if ($Apply) { 'apply' } else { 'preview' }
    profile = $Profile
    cron_jobs_paused = ($enabledJobs.Count -eq 0)
    enabled_cron_jobs = @($enabledJobs | ForEach-Object name)
    files_to_clear = $uniqueFiles.Count
    trading_session_id = $sessionState.old_session_id
    trading_session_messages = $sessionState.old_message_count
    chat_session_id_preserved = $sessionState.preserved_session_id
    native_memory_file_cleared = (Test-Path -LiteralPath (Join-Path $profileRoot 'memories\USER.md'))
    preserved = @(
        'SOUL.md', 'skills', 'plugins', 'config', 'chat named session',
        'native memory infrastructure', 'Glitch runtime policy', 'account groups',
        'supervisor build requests and Codex events', 'NinjaTrader accounts',
        'Journal.tsv', 'TradeLedger.tsv'
    )
    nt_reset = 'Use Glitch UI Reset Data, then NinjaTrader Reset for each Sim account.'
}

if (-not $Apply) {
    $preview | ConvertTo-Json -Depth 5
    return
}
if ($enabledJobs.Count -gt 0) {
    throw ('All Glitch Hermes cron jobs must be paused before reset: ' + (($enabledJobs | ForEach-Object name) -join ','))
}

$archiveRoot = Join-Path $glitchRoot 'hermes-archives'
New-Item -ItemType Directory -Force -Path $archiveRoot | Out-Null
$stamp = [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$staging = Join-Path $archiveRoot "trading-epoch-$stamp"
$archive = "$staging.zip"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    $previousHermesHome = $env:HERMES_HOME
    try {
        $env:HERMES_HOME = $profileRoot
        & hermes sessions export (Join-Path $staging 'named-trading-session.jsonl') `
            --session-id $sessionState.old_session_id --redact --yes | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not archive the named trading session.' }

        & hermes sessions export (Join-Path $staging 'one-shot-trading-sessions.jsonl') `
            --source trading --cwd $exchangeRoot --min-messages 2 --redact --yes | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not archive one-shot trading sessions.' }

        & hermes sessions export (Join-Path $staging 'hourly-review-sessions.jsonl') `
            --title glitch-hourly-review --redact --yes | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not archive hourly review sessions.' }
    }
    finally {
        $env:HERMES_HOME = $previousHermesHome
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($path in $uniqueFiles) {
            $entry = if ($path.StartsWith($glitchRoot, [StringComparison]::OrdinalIgnoreCase)) {
                'GlitchData/' + $path.Substring($glitchRoot.Length).TrimStart('\').Replace('\', '/')
            } elseif ($path.StartsWith($profileRoot, [StringComparison]::OrdinalIgnoreCase)) {
                'HermesProfile/' + $path.Substring($profileRoot.Length).TrimStart('\').Replace('\', '/')
            } else {
                'HermesCapsule/' + $path.Substring($capsuleRoot.Length).TrimStart('\').Replace('\', '/')
            }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $path, $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
        foreach ($item in Get-ChildItem -LiteralPath $staging -File) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $item.FullName, ('HermesSessions/' + $item.Name),
                [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $zip.Dispose()
    }

    foreach ($path in $uniqueFiles) {
        if ($path.Equals((Join-Path $profileRoot 'memories\USER.md'), [StringComparison]::OrdinalIgnoreCase)) {
            Set-Content -LiteralPath $path -Value '' -NoNewline
        } else {
            Remove-Item -LiteralPath $path -Force
        }
    }

    $previousHermesHome = $env:HERMES_HOME
    try {
        $env:HERMES_HOME = $profileRoot
        $resetState = (& $hermesPython $sessionReset --title trading --preserve-title chat --apply | Out-String | ConvertFrom-Json)
        if ($LASTEXITCODE -ne 0) { throw 'Could not replace the named trading session.' }

        & hermes sessions prune --source trading --cwd $exchangeRoot `
            --min-messages 2 --include-archived --yes | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not delete one-shot trading sessions.' }

        & hermes sessions prune --title glitch-hourly-review --include-archived --yes | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not delete hourly review sessions.' }
    }
    finally {
        $env:HERMES_HOME = $previousHermesHome
    }
}
finally {
    if (Test-Path -LiteralPath $staging -PathType Container) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}

[ordered]@{
    schema_version = 'glitch.hermes.trading_epoch_reset.v2'
    mode = 'applied'
    reset_utc = [datetime]::UtcNow.ToString('o')
    profile = $Profile
    archived_to = $archive
    cleared_files = $uniqueFiles.Count
    old_trading_session_id = $resetState.old_session_id
    new_trading_session_id = $resetState.new_session_id
    chat_session_id_preserved = $resetState.preserved_session_id
    cron_jobs_remain_paused = $true
    nt_reset = 'manual_required'
} | ConvertTo-Json -Depth 5
