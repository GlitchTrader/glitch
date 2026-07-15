param(
    [string]$ProfilesRoot = (Join-Path $env:LOCALAPPDATA 'hermes\profiles')
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$manifest = Get-Content -LiteralPath (Join-Path $repo 'hermes-profile\operator.json') -Raw | ConvertFrom-Json
$name = [string]$manifest.operator_profile
if ($name -ne 'glitch') { throw 'Canonical Hermes operator profile must be glitch.' }
$destination = Join-Path $ProfilesRoot $name
if (-not (Test-Path -LiteralPath $destination -PathType Container)) { throw 'Base Hermes profile glitch is missing.' }

$soulSource = Join-Path $repo 'hermes-profile\profiles\glitch\SOUL.md'
$soulDestination = Join-Path $destination 'SOUL.md'
Copy-Item -LiteralPath $soulSource -Destination $soulDestination -Force
$skillsSource = Join-Path $repo 'hermes-profile\skills'
$skillsDestination = Join-Path $destination 'skills'
foreach ($skillSource in @(Get-ChildItem -LiteralPath $skillsSource -Directory)) {
    $skillDestination = Join-Path $skillsDestination $skillSource.Name
    New-Item -ItemType Directory -Force -Path $skillDestination | Out-Null
    foreach ($skillFile in @(Get-ChildItem -LiteralPath $skillSource.FullName -Recurse -File)) {
        $relative = $skillFile.FullName.Substring($skillSource.FullName.Length + 1)
        $target = Join-Path $skillDestination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $skillFile.FullName -Destination $target -Force
    }
}
$scriptsDestination = Join-Path $destination 'scripts'
New-Item -ItemType Directory -Force -Path $scriptsDestination | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'tools\hermes\run_hermes_portfolio_cycle.py') `
    -Destination (Join-Path $scriptsDestination 'run_hermes_portfolio_cycle.py') -Force

$jobsPath = Join-Path $destination 'cron\jobs.json'
$enabledJobs = @()
if (Test-Path -LiteralPath $jobsPath) {
    $jobs = Get-Content -LiteralPath $jobsPath -Raw | ConvertFrom-Json
    $enabledJobs = @($jobs.jobs | Where-Object { [bool]$_.enabled })
}
if ($enabledJobs.Count -gt 0) { throw 'Glitch profile unexpectedly contains an enabled cron job.' }

[ordered]@{
    schema_version = 'glitch.hermes.operator_install.v2'
    installed_utc = [datetime]::UtcNow.ToString('o')
    profile = $name
    books = @($manifest.books | ForEach-Object {
        [ordered]@{ book_id=$_.book_id; route_id=$_.route_id; master_account=$_.master_account }
    })
    soul_sha256 = (Get-FileHash -LiteralPath $soulDestination -Algorithm SHA256).Hash.ToLowerInvariant()
    canonical_skills = @(Get-ChildItem -LiteralPath $skillsSource -Directory).Count
    enabled_cron_jobs = 0
    armed = $false
} | ConvertTo-Json -Depth 6
