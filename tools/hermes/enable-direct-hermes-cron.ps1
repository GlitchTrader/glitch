param(
    [string]$Profile = 'glitch',
    # Glitch publishes a rolling five-frame packet each minute. The exact-minute
    # tick naturally consumes the previous completed packet.
    [string]$Schedule = '* * * * *',
    [string]$GlitchData = (Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData')
)

$ErrorActionPreference = 'Stop'
if ($Profile -ne 'glitch') { throw 'The direct operator profile must be glitch.' }
$profileRoot = Join-Path $env:LOCALAPPDATA "hermes\profiles\$Profile"
$env:HERMES_HOME = $profileRoot
$scriptPath = Join-Path $profileRoot 'scripts\run-direct-glitch-cycle.py'
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw 'Install the direct Hermes bridge before enabling its cron job.'
}

$hermesCommand = Get-Command hermes -ErrorAction Stop
$python = Join-Path (Split-Path $hermesCommand.Source -Parent) 'python.exe'
if (-not (Test-Path -LiteralPath $python -PathType Leaf)) {
    throw 'Could not locate the Hermes Python runtime used to verify gateway supervision.'
}
& $python -c "from hermes_cli import gateway_windows; from hermes_cli.gateway import find_gateway_pids; import sys; sys.exit(0 if gateway_windows.is_installed() and find_gateway_pids() else 1)"
if ($LASTEXITCODE -ne 0) {
    throw 'The supervised Glitch Hermes gateway is not installed and running. Re-run install-direct-hermes-bridge.ps1 before enabling cron.'
}

$workdir = Join-Path ([IO.Path]::GetFullPath($GlitchData)) 'hermes\exchange'
New-Item -ItemType Directory -Force -Path $workdir | Out-Null
$jobsPath = Join-Path $profileRoot 'cron\jobs.json'
$jobs = @()
if (Test-Path -LiteralPath $jobsPath) {
    $document = Get-Content -LiteralPath $jobsPath -Raw | ConvertFrom-Json
    $jobs = @($document.jobs)
}

$coreJobs = @($jobs | Where-Object name -eq 'glitch-direct-operator')
if ($coreJobs.Count -gt 1) {
    throw 'Multiple glitch-direct-operator jobs exist; refusing to guess which one owns trading.'
}

$disabledJobs = @()
foreach ($job in @($jobs | Where-Object { $_.name -ne 'glitch-direct-operator' -and $_.enabled })) {
    & hermes cron pause ([string]$job.id) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not pause non-core Glitch cron job: $($job.name)" }
    $disabledJobs += [string]$job.name
}

if ($coreJobs.Count -eq 1) {
    $core = $coreJobs[0]
    & hermes cron edit ([string]$core.id) `
        --schedule $Schedule `
        --script 'run-direct-glitch-cycle.py' `
        --no-agent `
        --workdir $workdir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Hermes did not reconcile the direct operator cron job.' }
    if (-not $core.enabled) {
        & hermes cron resume ([string]$core.id) | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Hermes did not resume the direct operator cron job.' }
    }
    $jobId = [string]$core.id
}
else {
    & hermes cron create $Schedule `
        --name 'glitch-direct-operator' `
        --script 'run-direct-glitch-cycle.py' `
        --no-agent `
        --deliver local `
        --workdir $workdir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Hermes did not create the direct operator cron job.' }
    $jobId = $null
}

[ordered]@{
    schema_version = 'glitch.hermes.direct_cron_enable.v1'
    enabled_utc = [datetime]::UtcNow.ToString('o')
    profile = $Profile
    job = 'glitch-direct-operator'
    job_id = $jobId
    schedule = $Schedule
    non_core_jobs_disabled = $disabledJobs
    scheduler_owner = 'Hermes native cron'
    gateway_supervised = $true
    codex_in_runtime_path = $false
} | ConvertTo-Json -Depth 4
