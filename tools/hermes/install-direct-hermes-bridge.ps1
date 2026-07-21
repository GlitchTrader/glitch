param(
    [string]$Profile = 'glitch',
    [string]$ProfilesRoot = (Join-Path $env:LOCALAPPDATA 'hermes\profiles'),
    [switch]$SkipGatewayInstall
)

$ErrorActionPreference = 'Stop'
if ($Profile -ne 'glitch') { throw 'The direct operator profile must be glitch.' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$destination = Join-Path $ProfilesRoot $Profile
if (-not (Test-Path -LiteralPath $destination -PathType Container)) {
    throw "Hermes profile is missing: $destination"
}

Copy-Item -LiteralPath (Join-Path $repo 'hermes-profile\profiles\glitch\SOUL.md') `
    -Destination (Join-Path $destination 'SOUL.md') -Force

$skillsDestination = Join-Path $destination 'skills'
foreach ($skillSource in @(Get-ChildItem -LiteralPath (Join-Path $repo 'hermes-profile\skills') -Directory)) {
    $target = Join-Path $skillsDestination $skillSource.Name
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item -Path (Join-Path $skillSource.FullName '*') -Destination $target -Recurse -Force
}

$scriptsDestination = Join-Path $destination 'scripts'
New-Item -ItemType Directory -Force -Path $scriptsDestination | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'tools\hermes\run-direct-glitch-cycle.py') `
    -Destination (Join-Path $scriptsDestination 'run-direct-glitch-cycle.py') -Force
Copy-Item -LiteralPath (Join-Path $repo 'tools\hermes\reconcile-hermes-outcomes.py') `
    -Destination (Join-Path $scriptsDestination 'reconcile-hermes-outcomes.py') -Force
Copy-Item -LiteralPath (Join-Path $repo 'tools\hermes\ensure-named-sessions.py') `
    -Destination (Join-Path $scriptsDestination 'ensure-named-sessions.py') -Force
Copy-Item -LiteralPath (Join-Path $repo 'tools\hermes\reset-hermes-trading-epoch.ps1') `
    -Destination (Join-Path $scriptsDestination 'reset-hermes-trading-epoch.ps1') -Force
Copy-Item -LiteralPath (Join-Path $repo 'tools\hermes\reset-named-hermes-session.py') `
    -Destination (Join-Path $scriptsDestination 'reset-named-hermes-session.py') -Force

$pluginDestination = Join-Path $destination 'plugins\glitch-control'
New-Item -ItemType Directory -Force -Path $pluginDestination | Out-Null
Copy-Item -Path (Join-Path $repo 'hermes-profile\plugins\glitch-control\*') `
    -Destination $pluginDestination -Recurse -Force

# The Glitch profile is a host-side operator. Workframe's Hermes remains in its
# separate Docker stack and is not read, restarted, or reconfigured here.
& hermes -p $Profile config set terminal.backend local | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not set the Glitch profile terminal backend to local.' }
& hermes -p $Profile config set memory.memory_enabled true | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not enable native Hermes memory for Glitch.' }
& hermes -p $Profile config set memory.user_profile_enabled true | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not enable the native Hermes user profile for Glitch.' }
& hermes -p $Profile config set sessions.auto_prune false | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not preserve Glitch session transcripts.' }
& hermes -p $Profile plugins enable glitch-control --no-allow-tool-override | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not enable the deterministic Glitch control plugin.' }

if (-not $SkipGatewayInstall) {
    # Hermes native cron is hosted by the gateway. A detached child process is
    # not supervision: install Hermes' profile-scoped Windows Scheduled Task,
    # which is hidden, starts on login, and has restart-on-failure semantics.
    & hermes -p $Profile gateway install --start-now --start-on-login
    if ($LASTEXITCODE -ne 0) { throw 'Could not install the supervised Glitch Hermes gateway.' }
}

$hermesCommand = Get-Command hermes -ErrorAction Stop
$python = Join-Path (Split-Path $hermesCommand.Source -Parent) 'python.exe'
if (-not (Test-Path -LiteralPath $python -PathType Leaf)) {
    throw 'Could not locate the Hermes Python runtime used to seed named sessions.'
}
$previousHermesHome = $env:HERMES_HOME
try {
    $env:HERMES_HOME = $destination
    & $python (Join-Path $scriptsDestination 'ensure-named-sessions.py') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not seed the named chat and trading sessions.' }
}
finally {
    $env:HERMES_HOME = $previousHermesHome
}

[ordered]@{
    schema_version = 'glitch.hermes.direct_bridge_install.v1'
    installed_utc = [datetime]::UtcNow.ToString('o')
    profile = $Profile
    terminal_backend = 'local'
    memory_enabled = $true
    sessions_preserved = $true
    named_sessions = @('chat', 'trading')
    control_plugin_enabled = $true
    gateway_supervised = (-not $SkipGatewayInstall)
    cron_enabled = $false
    operator_armed = $false
    next_step = '.\tools\hermes\enable-direct-hermes-cron.ps1'
} | ConvertTo-Json -Depth 4
