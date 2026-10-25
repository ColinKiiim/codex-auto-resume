param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$PolicyPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'version-policy.json')
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Version must be major.minor.patch.' }
$parts = $Version.Split('.')
foreach ($part in $parts) {
    $value = 0
    if (-not [int]::TryParse($part, [ref]$value) -or $value -gt 65535) { throw 'Version components must fit Windows executable metadata (0..65535).' }
}
$policy = Get-Content -LiteralPath $PolicyPath -Raw | ConvertFrom-Json
if ($policy.schemaVersion -ne 1 -or $policy.allowedSeries -notmatch '^\d+\.\d+$' -or $policy.seriesChangeRequiresExplicitUserApproval -ne $true) { throw 'Invalid version policy.' }
$series = $parts[0] + '.' + $parts[1]
if ($series -ne $policy.allowedSeries) {
    throw ('Release series ' + $series + ' is not approved. Default to ' + $policy.allowedSeries + '.x; changing series requires explicit permission from the human user. Do not bypass or self-approve this policy.')
}
