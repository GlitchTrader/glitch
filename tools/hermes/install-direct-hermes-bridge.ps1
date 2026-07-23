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
$hermesCommand = Get-Command hermes -ErrorAction Stop
$python = Join-Path (Split-Path $hermesCommand.Source -Parent) 'python.exe'
if (-not (Test-Path -LiteralPath $python -PathType Leaf)) {
    throw "Could not locate the Hermes Python runtime: $python"
}

Copy-Item -LiteralPath (Join-Path $repo 'hermes-profile\profiles\glitch\SOUL.md') `
    -Destination (Join-Path $destination 'SOUL.md') -Force
Copy-Item -LiteralPath (Join-Path $repo 'hermes-profile\operator.json') `
    -Destination (Join-Path $destination 'operator.json') -Force

$skillsDestination = Join-Path $destination 'skills'
$skillSources = @(Get-ChildItem -LiteralPath (Join-Path $repo 'hermes-profile\skills') -Directory)
$sourceSkillNames = @($skillSources | ForEach-Object Name)
foreach ($installedSkill in @(Get-ChildItem -LiteralPath $skillsDestination -Directory -ErrorAction SilentlyContinue | Where-Object Name -Like 'glitch-*')) {
    if ($sourceSkillNames -notcontains $installedSkill.Name) {
        Remove-Item -LiteralPath $installedSkill.FullName -Recurse -Force
    }
}
foreach ($skillSource in $skillSources) {
    $target = Join-Path $skillsDestination $skillSource.Name
    if (Test-Path -LiteralPath $target -PathType Container) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item -Path (Join-Path $skillSource.FullName '*') -Destination $target -Recurse -Force
}

$scriptsDestination = Join-Path $destination 'scripts'
New-Item -ItemType Directory -Force -Path $scriptsDestination | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'tools\hermes\run-direct-glitch-cycle.py') `
    -Destination (Join-Path $scriptsDestination 'run-direct-glitch-cycle.py') -Force
Copy-Item -LiteralPath (Join-Path $repo 'tools\hermes\reconcile-hermes-outcomes.py') `
    -Destination (Join-Path $scriptsDestination 'reconcile-hermes-outcomes.py') -Force
Copy-Item -LiteralPath (Join-Path $repo 'tools\hermes\run-hermes-learning-cycle.py') `
    -Destination (Join-Path $scriptsDestination 'run-hermes-learning-cycle.py') -Force
Copy-Item -LiteralPath (Join-Path $repo 'tools\hermes\launch-hermes-learning-cycle.py') `
    -Destination (Join-Path $scriptsDestination 'launch-hermes-learning-cycle.py') -Force
Copy-Item -LiteralPath (Join-Path $repo 'tools\hermes\ensure-named-sessions.py') `
    -Destination (Join-Path $scriptsDestination 'ensure-named-sessions.py') -Force

$pluginDestination = Join-Path $destination 'plugins\glitch-control'
if (Test-Path -LiteralPath $pluginDestination -PathType Container) {
    Remove-Item -LiteralPath $pluginDestination -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $pluginDestination | Out-Null
Copy-Item -Path (Join-Path $repo 'hermes-profile\plugins\glitch-control\*') `
    -Destination $pluginDestination -Recurse -Force

# The Glitch profile is a host-side operator. Workframe's Hermes remains in its
# separate Docker stack and is not read, restarted, or reconfigured here.
& hermes -p $Profile config set terminal.backend local | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not set the Glitch profile terminal backend to local.' }
& hermes -p $Profile config set model.default gpt-5.6-luna | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not pin the Glitch core model to gpt-5.6-luna.' }
& hermes -p $Profile config set model.provider openai-codex | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not pin the Glitch model provider to OpenAI Codex.' }
& hermes -p $Profile config set agent.reasoning_effort medium | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not pin the Glitch core reasoning effort to medium.' }
# `hermes fallback clear` requires an interactive TTY and returns success when
# stdin is absent and the operation is cancelled. Use Hermes' config API so a
# non-interactive installation cannot falsely report an empty fallback chain.
$previousHermesHome = $env:HERMES_HOME
try {
    $env:HERMES_HOME = $destination
    & $python -c "from hermes_cli.config import load_config, save_config; from hermes_cli.fallback_cmd import _write_chain; c=load_config(); _write_chain(c, []); c.setdefault('agent', {})['reasoning_effort']='medium'; c.setdefault('agent', {}).pop('reasoning_overrides', None); save_config(c)"
    if ($LASTEXITCODE -ne 0) { throw 'Could not clear silent model fallbacks for Glitch.' }
}
finally {
    $env:HERMES_HOME = $previousHermesHome
}

& (Join-Path $repo 'tools\hermes\initialize-supervisor-ledger.ps1') | Out-Null
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
    core_model = 'gpt-5.6-luna'
    core_provider = 'openai-codex'
    core_reasoning_effort = 'medium'
    learning_reasoning_effort = 'high'
    fallback_providers = @()
    terminal_backend = 'local'
    memory_enabled = $true
    sessions_preserved = $true
    named_sessions = @('chat', 'trading')
    control_plugin_enabled = $true
    gateway_supervised = (-not $SkipGatewayInstall)
    cron_enabled = $false
    operator_armed = $false
    next_step = '.\tools\hermes\enable-direct-hermes-cron.ps1; .\tools\hermes\enable-hermes-learning-cron.ps1'
} | ConvertTo-Json -Depth 4
