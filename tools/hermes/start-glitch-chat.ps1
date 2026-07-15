param([string]$Profile = 'glitch')

$ErrorActionPreference = 'Stop'
if ($Profile -ne 'glitch') { throw 'The Glitch chat profile must be glitch.' }
& hermes -p $Profile --resume chat --tui
if ($LASTEXITCODE -ne 0) { throw 'The named Glitch chat session did not start.' }
