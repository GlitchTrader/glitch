param(
    [string]$GlitchData = (Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'),
    [string]$Capsule = 'D:\ab\projects\glitch\Glitch-Hermes-Data'
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $GlitchData).Path
$destination = Join-Path ([IO.Path]::GetFullPath($Capsule)) 'journal\nt'
New-Item -ItemType Directory -Force -Path $destination | Out-Null

$files = [ordered]@{
    'received.jsonl' = 'intents\received.jsonl'
    'decisions.jsonl' = 'intents\decisions.jsonl'
    'executions.jsonl' = 'intents\executions.jsonl'
    'hermes-trade-outcomes.jsonl' = 'intents\hermes-trade-outcomes.jsonl'
    'tradeledger.tsv' = 'tradeledger.tsv'
    'Journal.tsv' = 'Journal.tsv'
}

$synced = @()
foreach ($entry in $files.GetEnumerator()) {
    $from = Join-Path $source $entry.Value
    $to = Join-Path $destination $entry.Key
    if (-not (Test-Path -LiteralPath $from -PathType Leaf)) { continue }
    $temp = "$to.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        Copy-Item -LiteralPath $from -Destination $temp -Force
        Move-Item -LiteralPath $temp -Destination $to -Force
    } finally {
        Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
    }
    $item = Get-Item -LiteralPath $to
    $synced += [ordered]@{
        path = "journal/nt/$($entry.Key)"
        bytes = $item.Length
        sha256 = (Get-FileHash -LiteralPath $to -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

Get-ChildItem -LiteralPath $destination -File -Filter 'hermes-cycles*.jsonl' -ErrorAction SilentlyContinue |
    Remove-Item -Force
$cycleFiles = @(Get-ChildItem -LiteralPath (Join-Path $source 'intents') -File -Filter 'hermes-cycles*.jsonl' -ErrorAction SilentlyContinue)
foreach ($from in $cycleFiles) {
    $to = Join-Path $destination $from.Name
    $temp = "$to.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        Copy-Item -LiteralPath $from.FullName -Destination $temp -Force
        Move-Item -LiteralPath $temp -Destination $to -Force
    } finally {
        Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
    }
    $item = Get-Item -LiteralPath $to
    $synced += [ordered]@{
        path = "journal/nt/$($from.Name)"
        bytes = $item.Length
        sha256 = (Get-FileHash -LiteralPath $to -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

[ordered]@{
    schema_version = 'glitch.hermes.nt_journal_sync.v1'
    synced_utc = [datetime]::UtcNow.ToString('o')
    source = 'NinjaTrader GlitchData journal files copied without transformation'
    files = $synced
} | ConvertTo-Json -Depth 5
