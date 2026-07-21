param(
    [string]$Profile = 'glitch',
    [string]$Schedule = '*/15 * * * *',
    [string]$GlitchData = (Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData')
)

$ErrorActionPreference = 'Stop'
if ($Profile -ne 'glitch') { throw 'The learning profile must be glitch.' }
$profileRoot = Join-Path $env:LOCALAPPDATA "hermes\profiles\$Profile"
$env:HERMES_HOME = $profileRoot
$scriptPath = Join-Path $profileRoot 'scripts\run-hermes-learning-cycle.py'
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw 'Install the direct Hermes bridge before enabling learning.'
}

$workdir = Join-Path ([IO.Path]::GetFullPath($GlitchData)) 'hermes\exchange'
New-Item -ItemType Directory -Force -Path $workdir | Out-Null
$jobsPath = Join-Path $profileRoot 'cron\jobs.json'
$document = Get-Content -LiteralPath $jobsPath -Raw | ConvertFrom-Json
$jobs = @($document.jobs)
$core = @($jobs | Where-Object { $_.name -eq 'glitch-direct-operator' -and $_.enabled })
if ($core.Count -ne 1) { throw 'Enable exactly one direct Glitch operator before enabling learning.' }
$matches = @($jobs | Where-Object name -eq 'glitch-learning-supervisor')
if ($matches.Count -gt 1) { throw 'Multiple Glitch learning jobs exist.' }

foreach ($legacy in @($jobs | Where-Object { $_.name -eq 'glitch-hourly-review' -and $_.enabled })) {
    & hermes cron pause ([string]$legacy.id) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not pause the legacy hourly review.' }
}

if ($matches.Count -eq 1) {
    $job = $matches[0]
    & hermes cron edit ([string]$job.id) --schedule $Schedule `
        --script 'run-hermes-learning-cycle.py' --no-agent --workdir $workdir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not reconcile the Glitch learning job.' }
    if (-not $job.enabled) {
        & hermes cron resume ([string]$job.id) | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not resume the Glitch learning job.' }
    }
}
else {
    & hermes cron create $Schedule --name 'glitch-learning-supervisor' `
        --script 'run-hermes-learning-cycle.py' --no-agent --deliver local --workdir $workdir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the Glitch learning job.' }
}

$document = Get-Content -LiteralPath $jobsPath -Raw | ConvertFrom-Json
$installed = @($document.jobs | Where-Object name -eq 'glitch-learning-supervisor')
if ($installed.Count -ne 1) { throw 'Learning cron reconciliation did not leave exactly one job.' }
$job = $installed[0]
$persistedSchedule = if ($job.schedule.display) { [string]$job.schedule.display } else { [string]$job.schedule.expr }
$wrongContract = -not $job.enabled `
    -or -not $job.no_agent `
    -or [string]$job.script -ne 'run-hermes-learning-cycle.py' `
    -or $persistedSchedule -ne $Schedule `
    -or [IO.Path]::GetFullPath([string]$job.workdir) -ne [IO.Path]::GetFullPath($workdir)
if ($wrongContract) {
    throw 'Learning cron job persisted with the wrong contract.'
}

[ordered]@{
    schema_version = 'glitch.hermes.learning_cron_enable.v1'
    enabled_utc = [datetime]::UtcNow.ToString('o')
    profile = $Profile
    job = 'glitch-learning-supervisor'
    job_id = [string]$job.id
    schedule = $persistedSchedule
    nested_session_source = 'trading'
    model = 'gpt-5.6-sol'
    execution_authority = $false
    scheduler_owner = 'Hermes native cron'
    codex_in_runtime_path = $false
} | ConvertTo-Json -Depth 4
