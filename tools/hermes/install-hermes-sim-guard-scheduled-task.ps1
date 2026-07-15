param(
    [string]$TaskName = 'Glitch Hermes Sim Guard Tick',
    [int]$IntervalMinutes = 1,
    [switch]$SubmitSim,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'

if ($IntervalMinutes -lt 1) {
    throw 'IntervalMinutes must be at least 1.'
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$tickScript = Join-Path $PSScriptRoot 'run-hermes-sim-guard-tick.ps1'
if (-not (Test-Path -LiteralPath $tickScript)) {
    throw "Missing tick script: $tickScript"
}

$arguments = @(
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', ('"' + $tickScript + '"')
)
if ($SubmitSim) { $arguments += '-SubmitSim' }

if ($WhatIfOnly) {
    [ordered]@{
        schema_version = 'glitch.hermes.sim_guard_scheduler_plan.v1'
        task_name = $TaskName
        interval_minutes = $IntervalMinutes
        submit_sim = [bool]$SubmitSim
        command = 'powershell.exe ' + ($arguments -join ' ')
        working_directory = $repo
        registered = $false
    } | ConvertTo-Json -Depth 6
    return
}

$action = New-ScheduledTaskAction `
    -Execute 'powershell.exe' `
    -Argument ($arguments -join ' ') `
    -WorkingDirectory $repo

$trigger = New-ScheduledTaskTrigger `
    -Once `
    -At (Get-Date).AddMinutes(1) `
    -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes) `
    -RepetitionDuration (New-TimeSpan -Days 3650)

$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 4) `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries

$principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME `
    -LogonType Interactive `
    -RunLevel Limited

$definition = New-ScheduledTask `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal `
    -Description 'Runs Glitch/Hermes Sim guard tick. Uses prepare-only opportunity/management guards and spends zero model calls unless a machine trigger or management threshold is present.'

Register-ScheduledTask -TaskName $TaskName -InputObject $definition -Force | Out-Null
[ordered]@{
    schema_version = 'glitch.hermes.sim_guard_scheduler_plan.v1'
    task_name = $TaskName
    interval_minutes = $IntervalMinutes
    submit_sim = [bool]$SubmitSim
    command = 'powershell.exe ' + ($arguments -join ' ')
    working_directory = $repo
    registered = $true
} | ConvertTo-Json -Depth 6
