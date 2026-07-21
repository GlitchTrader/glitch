param(
    [string]$DataRoot = (Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData')
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$exchange = Join-Path $DataRoot 'hermes\exchange\hermes\supervisor'
$streams = @(
    'trade-episodes.jsonl',
    'observations.jsonl',
    'trading-guidance.jsonl',
    'lessons.jsonl',
    'plans.jsonl',
    'daily-journal.jsonl',
    'cognitive-changes.jsonl',
    'build-requests.jsonl',
    'codex-events.jsonl'
)
foreach ($name in $streams) {
    $path = Join-Path $exchange $name
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
    if (-not (Test-Path -LiteralPath $path)) { New-Item -ItemType File -Path $path | Out-Null }
}

[ordered]@{
    schema_version = 'glitch.supervisor.ledger_init.v1'
    initialized_utc = [datetime]::UtcNow.ToString('o')
    owner = 'hermes-supervisor'
    trading_truth_owner = 'glitch'
    builder_owner = 'codex'
    streams = $streams
} | ConvertTo-Json -Depth 4
