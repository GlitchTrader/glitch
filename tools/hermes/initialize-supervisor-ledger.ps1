param(
    [string]$DataRoot = 'C:\Users\alan\Documents\NinjaTrader 8\GlitchData'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$exchange = Join-Path $DataRoot 'hermes\exchange\hermes\supervisor'
foreach ($name in @('observations.jsonl','trading-guidance.jsonl','lessons.jsonl','build-requests.jsonl','codex-events.jsonl')) {
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
    streams = @('observations.jsonl','trading-guidance.jsonl','lessons.jsonl','build-requests.jsonl','codex-events.jsonl')
} | ConvertTo-Json -Depth 4
