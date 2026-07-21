# ponytail: inspect and register one immutable NinjaTrader export; never commit or push
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('standard', 'ai')]
    [string]$Edition,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+(?:\.\d+){1,}(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$SourceZip,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$SourceCommit,

    [Parameter(Mandatory = $true)]
    [string]$ReleaseDate,

    [string]$HermesProfileVersion = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$filesDirectory = Join-Path $repoRoot 'apps\download\public\files'
$catalogPath = Join-Path $repoRoot 'apps\download\src\lib\release-catalog.json'
$checksumsPath = Join-Path $filesDirectory 'checksums.json'
$fileName = if ($Edition -eq 'ai') { "Glitch_AI_v$Version.zip" } else { "Glitch_v$Version.zip" }
$destinationPath = Join-Path $filesDirectory $fileName
$status = if ($Edition -eq 'ai') { 'experimental' } else { 'stable' }
$resolvedSource = (Resolve-Path -LiteralPath $SourceZip).Path

if ($Edition -eq 'standard' -and -not [string]::IsNullOrWhiteSpace($HermesProfileVersion)) {
    throw 'A Standard release cannot declare a Hermes profile version.'
}

$parsedReleaseDate = [DateTimeOffset]::MinValue
if (-not [DateTimeOffset]::TryParse(
        $ReleaseDate,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal,
        [ref]$parsedReleaseDate)) {
    throw "Invalid release date: $ReleaseDate"
}
$releaseDateUtc = $parsedReleaseDate.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ss.fffZ', [Globalization.CultureInfo]::InvariantCulture)

if (Test-Path -LiteralPath $destinationPath) {
    throw "Refusing to overwrite existing release: $destinationPath"
}
if ($resolvedSource -eq $destinationPath) {
    throw 'Source archive must be outside the immutable release destination.'
}

$dirty = @(& git -C $repoRoot status --porcelain)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect repository status.'
}
$publisherOwnedPaths = @(
    'apps/download/src/lib/release-catalog.json',
    'apps/download/public/files/checksums.json'
)
$unexpectedDirty = @($dirty | Where-Object {
    $path = if ($_.Length -gt 3) { $_.Substring(3) } else { '' }
    if ($path -match ' -> ') {
        $path = ($path -split ' -> ', 2)[1]
    }
    $path = $path.Trim('"').Replace('\', '/')
    $publisherOwnedPaths -notcontains $path -and
        $path -notmatch '^apps/download/public/files/Glitch(?:_AI)?_v[^/]+\.zip$'
})
if ($unexpectedDirty.Count -gt 0) {
    throw "Release publisher found non-release worktree changes:`n$($unexpectedDirty -join "`n")"
}

& git -C $repoRoot cat-file -e "$SourceCommit`^{commit}"
if ($LASTEXITCODE -ne 0) {
    throw "Source commit is not present locally: $SourceCommit"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedSource)
$temporaryDll = Join-Path ([System.IO.Path]::GetTempPath()) ("glitch-release-" + [Guid]::NewGuid().ToString('N') + '.dll')
try {
    $entries = @($archive.Entries | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) })
    if ($entries.Count -ne 3) {
        throw "Expected exactly three NinjaTrader export entries; found $($entries.Count)."
    }
    if ($entries | Where-Object { $_.FullName -ne $_.Name }) {
        throw 'Release archive must contain only root-level files.'
    }

    $infoEntry = @($entries | Where-Object { $_.Name -eq 'Info.xml' })
    $sourceEntry = @($entries | Where-Object { $_.Name -match '\.cs$' })
    $dllEntry = @($entries | Where-Object { $_.Name -match '\.dll$' })
    if ($infoEntry.Count -ne 1 -or $sourceEntry.Count -ne 1 -or $dllEntry.Count -ne 1) {
        throw 'Expected Info.xml, one generated C# wrapper, and one compiled DLL.'
    }
    if ([IO.Path]::GetFileNameWithoutExtension($sourceEntry[0].Name) -ne [IO.Path]::GetFileNameWithoutExtension($dllEntry[0].Name)) {
        throw 'Generated wrapper and DLL names do not match.'
    }

    $reader = [IO.StreamReader]::new($infoEntry[0].Open())
    try { [xml]$infoXml = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($null -eq $infoXml.NinjaTrader.Export.Version) {
        throw 'Info.xml is not a valid NinjaTrader export manifest.'
    }

    $reader = [IO.StreamReader]::new($sourceEntry[0].Open())
    try { $generatedSource = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($generatedSource -notmatch 'NinjaScript generated code') {
        throw 'The C# entry is not the expected generated NinjaTrader wrapper.'
    }
    foreach ($forbiddenPattern in @('(?i)[A-Z]:\\Users\\', '(?i)D:\\ab\\', '(?i)api[_-]?key\s*[:=]', '(?i)password\s*[:=]', '(?i)bearer\s+[A-Za-z0-9]')) {
        if ($generatedSource -match $forbiddenPattern) {
            throw "Generated wrapper contains forbidden machine or credential material: $forbiddenPattern"
        }
    }

    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($dllEntry[0], $temporaryDll, $false)
    $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($temporaryDll)
    if ($assemblyName.Name -ne 'Glitch') {
        throw "Unexpected assembly name: $($assemblyName.Name)"
    }
    if ($assemblyName.Version.ToString() -ne $Version) {
        throw "Assembly version $($assemblyName.Version) does not match requested release $Version."
    }
}
finally {
    $archive.Dispose()
    if (Test-Path -LiteralPath $temporaryDll) {
        Remove-Item -LiteralPath $temporaryDll -Force
    }
}

$catalogRaw = [IO.File]::ReadAllText($catalogPath)
$checksumsRaw = [IO.File]::ReadAllText($checksumsPath)
$destinationCreated = $false
try {
    $catalog = @($catalogRaw | ConvertFrom-Json)
    if ($catalog | Where-Object { $_.fileName -eq $fileName -or ($_.edition -eq $Edition -and $_.version -eq $Version) }) {
        throw "Release is already registered: $Edition $Version"
    }

    Copy-Item -LiteralPath $resolvedSource -Destination $destinationPath
    $destinationCreated = $true
    $sourceHash = (Get-FileHash -LiteralPath $resolvedSource -Algorithm SHA256).Hash.ToUpperInvariant()
    $destinationHash = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($sourceHash -ne $destinationHash) {
        throw 'Staged archive checksum differs from the inspected source.'
    }

    $entry = [ordered]@{
        fileName = $fileName
        edition = $Edition
        version = $Version
        releaseDate = $releaseDateUtc
        status = $status
        sourceCommit = $SourceCommit.ToLowerInvariant()
    }
    if (-not [string]::IsNullOrWhiteSpace($HermesProfileVersion)) {
        $entry.hermesProfileVersion = $HermesProfileVersion.Trim()
    }
    $catalog += [pscustomobject]$entry

    $checksums = [ordered]@{}
    $existingChecksums = $checksumsRaw | ConvertFrom-Json
    foreach ($property in $existingChecksums.PSObject.Properties | Sort-Object Name) {
        $checksums[$property.Name] = ([string]$property.Value).ToUpperInvariant()
    }
    $checksums[$fileName] = $destinationHash

    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($catalogPath, (($catalog | ConvertTo-Json -Depth 5) + "`n"), $utf8NoBom)
    [IO.File]::WriteAllText($checksumsPath, (($checksums | ConvertTo-Json -Depth 5) + "`n"), $utf8NoBom)

    & npm.cmd run validate:releases --workspace apps/download
    if ($LASTEXITCODE -ne 0) {
        throw 'Release catalog validation failed.'
    }
    & git -C $repoRoot diff --check
    if ($LASTEXITCODE -ne 0) {
        throw 'git diff --check failed.'
    }

    Write-Host "Validated and registered $fileName"
    Write-Host "SHA-256 $destinationHash"
    Write-Host 'No commit or push was performed.'
}
catch {
    [IO.File]::WriteAllText($catalogPath, $catalogRaw, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($checksumsPath, $checksumsRaw, [Text.UTF8Encoding]::new($false))
    if ($destinationCreated -and (Test-Path -LiteralPath $destinationPath)) {
        Remove-Item -LiteralPath $destinationPath -Force
    }
    throw
}
