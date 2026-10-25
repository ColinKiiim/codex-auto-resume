$ErrorActionPreference = 'Stop'
$guard = Join-Path (Split-Path -Parent $PSScriptRoot) 'tools/assert-version-policy.ps1'
$checks = 0
foreach ($version in @('1.1.0', '1.1.7', '1.1.8', '1.1.65535')) {
    & $guard -Version $version
    $checks++
}
foreach ($version in @('1.2.0', '1.5.3', '2.0.0', '0.1.0', '1.1.65536', '1.1.-1', '1.1.01', '1.1.1-preview')) {
    $rejected = $false
    try { & $guard -Version $version } catch { $rejected = $true }
    if (-not $rejected) { throw ('Unapproved/invalid version accepted: ' + $version) }
    $checks++
}
Write-Output ('Version policy: ' + $checks + ' checks passed; unapproved series rejected.')
