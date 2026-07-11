# Hermes snapshot sanity (no NT needed)
# Reads GlitchData selfcheck files written by the AddOn.

$gd = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\GlitchData'
$rail = Join-Path $gd 'selfcheck\rail.json'
$sanity = Join-Path $gd 'selfcheck\snapshot_sanity.json'

if (-not (Test-Path $rail)) { Write-Output 'FAIL rail.json missing'; exit 1 }
if (-not (Test-Path $sanity)) { Write-Output 'WARN snapshot_sanity.json not yet written (wait 5m)'; exit 0 }

$railJson = Get-Content $rail -Raw | ConvertFrom-Json
$sanityJson = Get-Content $sanity -Raw | ConvertFrom-Json

Write-Output ("rail fresh_roots={0}/{1}" -f $railJson.feed_bus.fresh_instrument_count, $railJson.feed_bus.instrument_root_count)
Write-Output ("sanity status={0} market_age={1}s" -f $sanityJson.status, $sanityJson.market.age_seconds)

if ($sanityJson.status -ne 'ok') { exit 1 }
exit 0
