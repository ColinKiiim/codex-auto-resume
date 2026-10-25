param([Parameter(Mandatory = $true)][string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$isolatedPath = Join-Path ([System.IO.Path]::GetTempPath()) ('CodexAutoResume-portable-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $isolatedPath | Out-Null
$isolatedExe = Join-Path $isolatedPath 'CodexAutoResume.exe'
$reportPath = Join-Path $isolatedPath 'self-check.txt'
Copy-Item -LiteralPath $ExecutablePath -Destination $isolatedExe
if (@(Get-ChildItem -LiteralPath $isolatedPath -Force).Count -ne 1) { throw 'Isolated test directory must contain only the EXE.' }
$isolatedProcess = Start-Process -FilePath $isolatedExe -ArgumentList @('--self-check', ('"' + $reportPath + '"')) -WorkingDirectory $isolatedPath -WindowStyle Hidden -PassThru
if (-not $isolatedProcess.WaitForExit(15000)) {
    $isolatedProcess.Kill()
    throw 'Portable self-check timed out.'
}
$report = if (Test-Path -LiteralPath $reportPath) { Get-Content -LiteralPath $reportPath -Raw } else { 'No report was produced.' }
if ($isolatedProcess.ExitCode -ne 0 -or $report -notmatch 'PASS:') { throw ('Portable self-check failed: ' + $report) }
Copy-Item -LiteralPath $reportPath -Destination (Join-Path $PSScriptRoot 'portable-result.txt') -Force
Write-Output $report
# Delete only the exact files created above, then the now-empty task-specific directory.
Remove-Item -LiteralPath $isolatedExe, $reportPath
Remove-Item -LiteralPath $isolatedPath
