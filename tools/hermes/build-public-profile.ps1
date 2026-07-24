[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TargetRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sourceRoot = Join-Path $repoRoot 'hermes-profile'
$target = [IO.Path]::GetFullPath($TargetRoot)
if (-not (Test-Path -LiteralPath $target -PathType Container)) {
    New-Item -ItemType Directory -Path $target | Out-Null
}

$allowedTopLevel = @(
    '.git', '.gitattributes', '.gitignore', 'distribution.yaml', 'SOUL.md', 'operator.json',
    'config.yaml', 'skills', 'plugins', 'scripts', 'setup.ps1', 'README.md', 'SHA256SUMS'
)
$unexpected = @(Get-ChildItem -LiteralPath $target -Force | Where-Object { $_.Name -notin $allowedTopLevel })
if ($unexpected.Count -gt 0) {
    throw "Target contains unexpected paths: $($unexpected.Name -join ', ')"
}

foreach ($directoryName in @('skills', 'plugins', 'scripts')) {
    $directoryPath = Join-Path $target $directoryName
    if (Test-Path -LiteralPath $directoryPath) {
        Remove-Item -LiteralPath $directoryPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $directoryPath | Out-Null
}

foreach ($name in @('distribution.yaml', 'operator.json', 'config.yaml', 'setup.ps1', 'README.md', '.gitattributes', '.gitignore')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination (Join-Path $target $name) -Force
}
Copy-Item -LiteralPath (Join-Path $sourceRoot 'profiles\glitch\SOUL.md') -Destination (Join-Path $target 'SOUL.md') -Force
Copy-Item -Path (Join-Path $sourceRoot 'skills\*') -Destination (Join-Path $target 'skills') -Recurse -Force
Copy-Item -Path (Join-Path $sourceRoot 'plugins\glitch-control') -Destination (Join-Path $target 'plugins') -Recurse -Force

$workerNames = @(
    'run-direct-glitch-cycle.py',
    'launch-direct-glitch-cycle.py',
    'reconcile-hermes-outcomes.py',
    'run-hermes-learning-cycle.py',
    'launch-hermes-learning-cycle.py',
    'ensure-named-sessions.py',
    'reset-hermes-trading-epoch.ps1'
)
foreach ($name in $workerNames) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "tools\hermes\$name") -Destination (Join-Path $target "scripts\$name") -Force
}

$skillCount = @(Get-ChildItem -LiteralPath (Join-Path $target 'skills') -Directory).Count
if ($skillCount -ne 11) { throw "Expected 11 Glitch skills; found $skillCount." }
$scriptCount = @(Get-ChildItem -LiteralPath (Join-Path $target 'scripts') -File).Count
if ($scriptCount -ne 7) { throw "Expected seven runtime scripts; found $scriptCount." }

$textFiles = @(Get-ChildItem -LiteralPath $target -Recurse -File -Force | Where-Object {
    $_.Name -ne 'SHA256SUMS' -and $_.FullName -notlike (Join-Path $target '.git\*')
})
$utf8NoBom = [Text.UTF8Encoding]::new($false)
foreach ($file in $textFiles) {
    $content = [IO.File]::ReadAllText($file.FullName)
    $content = $content.Replace("`r`n", "`n").Replace("`r", "`n")
    [IO.File]::WriteAllText($file.FullName, $content, $utf8NoBom)
    foreach ($forbidden in @('(?i)[A-Z]:\\Users\\', '(?i)D:\\ab\\', '(?i)api[_-]?key\s*[:=]\s*\S+', '(?i)bearer\s+[A-Za-z0-9_-]{12,}')) {
        if ($content -match $forbidden) {
            throw "Public profile contains forbidden machine or credential material in $($file.FullName): $forbidden"
        }
    }
}

$hashFiles = @($textFiles | Where-Object { $_.Name -notin @('config.yaml', 'distribution.yaml') })
$manifestLines = @()
foreach ($file in $hashFiles | Sort-Object FullName) {
    $relative = $file.FullName.Substring($target.TrimEnd('\').Length + 1).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $manifestLines += "$hash  $relative"
}
[IO.File]::WriteAllText(
    (Join-Path $target 'SHA256SUMS'),
    (($manifestLines -join "`n") + "`n"),
    $utf8NoBom)

$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $target 'setup.ps1'),
    [ref]$null,
    [ref]$parseErrors) | Out-Null
if ($parseErrors.Count -gt 0) { throw "Public setup.ps1 does not parse: $($parseErrors[0].Message)" }

Get-Content -LiteralPath (Join-Path $target 'operator.json') -Raw | ConvertFrom-Json | Out-Null
foreach ($script in Get-ChildItem -LiteralPath (Join-Path $target 'scripts') -Filter '*.py') {
    & python -c "from pathlib import Path; import sys; p=Path(sys.argv[1]); compile(p.read_text(encoding='utf-8'), str(p), 'exec')" $script.FullName
    if ($LASTEXITCODE -ne 0) { throw "Python compilation failed: $($script.Name)" }
}

$unexpectedAfter = @(Get-ChildItem -LiteralPath $target -Force | Where-Object { $_.Name -notin $allowedTopLevel })
if ($unexpectedAfter.Count -gt 0) { throw "Public profile gained unexpected paths: $($unexpectedAfter.Name -join ', ')" }

[ordered]@{
    schema_version = 'glitch.hermes.public_profile_build.v1'
    target = $target
    version = '0.0.2.11'
    skills = $skillCount
    scripts = $scriptCount
    files = $textFiles.Count + 1
    hashed_files = $hashFiles.Count
    checksum_manifest = 'SHA256SUMS'
} | ConvertTo-Json -Depth 3
