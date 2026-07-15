param(
    [string]$GlitchData = (Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'),
    [string]$Output = 'D:\ab\projects\glitch\Glitch-Hermes-Data',
    [ValidateRange(0, 14)][int]$HistoryDays = 3
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$source = (Resolve-Path $GlitchData).Path
$outputFull = [IO.Path]::GetFullPath($Output)
if ($outputFull -eq $source -or $outputFull.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Output must not be inside the canonical GlitchData source.'
}

$dirs = @('current', 'history\market', 'journal', 'journal\nt', 'knowledge', 'contracts')
foreach ($dir in $dirs) { New-Item -ItemType Directory -Force -Path (Join-Path $outputFull $dir) | Out-Null }
@('journal\hermes.jsonl', 'journal\legacy_sim101.jsonl') | ForEach-Object {
    Remove-Item -LiteralPath (Join-Path $outputFull $_) -Force -ErrorAction SilentlyContinue
}

function Copy-Allowlisted([string]$From, [string]$To, [bool]$Required = $false) {
    if (Test-Path -LiteralPath $From -PathType Leaf) {
        Copy-Item -LiteralPath $From -Destination $To -Force
        return $true
    }
    if ($Required) { throw "Required source missing: $From" }
    if (Test-Path -LiteralPath $To) { Remove-Item -LiteralPath $To -Force }
    return $false
}

$copied = [ordered]@{}
$copied.market_latest = Copy-Allowlisted (Join-Path $source 'snapshots\market\latest.json') (Join-Path $outputFull 'current\market.latest.json')
$copied.portfolio_latest = Copy-Allowlisted (Join-Path $source 'snapshots\portfolio\latest.json') (Join-Path $outputFull 'current\portfolio.latest.json')
$copied.policy = Copy-Allowlisted (Join-Path $source 'ai\policy.json') (Join-Path $outputFull 'current\policy.json') $true
$copied.archetypes = Copy-Allowlisted (Join-Path $repo 'glitch_hermes_docs\memory\archetypes.v2.json') (Join-Path $outputFull 'knowledge\archetypes.v2.json') $true
Remove-Item -LiteralPath (Join-Path $outputFull 'knowledge\archetypes.v1.json') -Force -ErrorAction SilentlyContinue
$copied.playbook = Copy-Allowlisted (Join-Path $repo 'glitch_hermes_docs\memory\mnq-playbook.md') (Join-Path $outputFull 'knowledge\mnq-playbook.md') $true
$copied.intent_schema = Copy-Allowlisted (Join-Path $repo 'glitch_hermes_docs\schemas\intent.v2.schema.json') (Join-Path $outputFull 'contracts\intent.v2.schema.json') $true

$historyDir = Join-Path $outputFull 'history\market'
Get-ChildItem -LiteralPath $historyDir -File -Filter '*.json' | Remove-Item -Force
$indexPath = Join-Path $source 'export\corpus\MNQ\index.jsonl'
$selected = [Collections.Generic.List[object]]::new()
if ($HistoryDays -gt 0 -and (Test-Path -LiteralPath $indexPath)) {
    # The index can contain millions of rows. Extract only the fixed snapshot ID;
    # full JSON parsing made refreshes unnecessarily slow.
    $idPattern = [regex]'"snapshot_id":"(?<id>\d{8}T\d{6}Z)"'
    # Freeze the readable boundary because NinjaTrader may still be appending an export.
    $indexLength = (Get-Item -LiteralPath $indexPath).Length
    $maxTime = [datetime]::MinValue
    $reader = [IO.File]::OpenText($indexPath)
    $bytesRead = 0L
    try {
        while ($bytesRead -lt $indexLength -and ($line = $reader.ReadLine()) -ne $null) {
            $bytesRead += [Text.Encoding]::UTF8.GetByteCount($line) + 2
            $match = $idPattern.Match($line)
            if ($match.Success) {
                $t = [datetime]::ParseExact($match.Groups['id'].Value, 'yyyyMMddTHHmmssZ', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal).ToUniversalTime()
                if ($t -gt $maxTime) { $maxTime = $t }
            }
        }
    } finally {
        $reader.Dispose()
    }
    $cutoff = $maxTime.AddDays(-$HistoryDays)
    $reader = [IO.File]::OpenText($indexPath)
    $bytesRead = 0L
    try {
        while ($bytesRead -lt $indexLength -and ($line = $reader.ReadLine()) -ne $null) {
            $bytesRead += [Text.Encoding]::UTF8.GetByteCount($line) + 2
            $match = $idPattern.Match($line)
            if (-not $match.Success) { continue }
            $id = $match.Groups['id'].Value
            $t = [datetime]::ParseExact($id, 'yyyyMMddTHHmmssZ', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal).ToUniversalTime()
            if ($t -ge $cutoff -and $t -le $maxTime) {
                $marketPath = Join-Path (Split-Path $indexPath) "$id.json"
                if (Test-Path -LiteralPath $marketPath) {
                    $selected.Add([pscustomobject]@{ schema_version='glitch.market.snapshot.v2'; snapshot_id=$id; bar_close_utc=$t.ToString('o'); market_path=$marketPath })
                }
            }
        }
    } finally {
        $reader.Dispose()
    }
}

$indexOut = Join-Path $outputFull 'history\index.jsonl'
$indexLines = [Collections.Generic.List[string]]::new()
foreach ($row in ($selected | Sort-Object bar_close_utc -Unique)) {
    $name = [IO.Path]::GetFileName($row.market_path)
    Copy-Item -LiteralPath $row.market_path -Destination (Join-Path $historyDir $name) -Force
    $indexLines.Add(([ordered]@{ schema_version=$row.schema_version; snapshot_id=$row.snapshot_id; bar_close_utc=$row.bar_close_utc; path="history/market/$name" } | ConvertTo-Json -Compress))
}
Set-Content -LiteralPath $indexOut -Value $indexLines

$journalSync = & powershell.exe -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'sync-nt-journal.ps1') -GlitchData $source -Capsule $outputFull
if ($LASTEXITCODE -ne 0) { throw 'NinjaTrader journal sync failed.' }

$files = Get-ChildItem -LiteralPath $outputFull -Recurse -File | Where-Object Name -ne 'manifest.json'
$manifestFiles = foreach ($file in $files) {
    [ordered]@{
        path = $file.FullName.Substring($outputFull.Length + 1).Replace('\','/')
        bytes = $file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$manifest = [ordered]@{
    schema_version = 'glitch.hermes.data_capsule.v1'
    created_utc = [datetime]::UtcNow.ToString('o')
    source = 'GlitchData allowlist; not mounted into Hermes'
    attribution = @{ journal_source = 'NinjaTrader GlitchData'; transformed_journal = $false }
    history_days = $HistoryDays
    history_snapshot_count = $selected.Count
    current = $copied
    journal_sync = ($journalSync | ConvertFrom-Json)
    files = $manifestFiles
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputFull 'manifest.json')
Write-Output ($manifest | ConvertTo-Json -Depth 4 -Compress)
