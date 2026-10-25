param([Parameter(Mandatory = $true)][string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$exeFullPath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$auditRoot = Join-Path ([IO.Path]::GetTempPath()) ('CodexAutoResume-background-' + [Guid]::NewGuid().ToString('N'))
$singleFolder = Join-Path $auditRoot "中文目录 & user's folder"
New-Item -ItemType Directory -Path $singleFolder -Force | Out-Null
$singleExe = Join-Path $singleFolder '后台自测 renamed.exe'
$reportPath = Join-Path $auditRoot 'report.txt'
Copy-Item -LiteralPath $exeFullPath -Destination $singleExe
(Get-Item -LiteralPath $singleExe).IsReadOnly = $true
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $singleExe
$start.Arguments = '--background-self-check "' + $reportPath + '"'
$start.WorkingDirectory = $env:WINDIR
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.EnvironmentVariables['PATH'] = ''
foreach ($name in @('PYTHONPATH', 'NODE_PATH', 'VIRTUAL_ENV', 'OPENAI_API_KEY')) { $start.EnvironmentVariables.Remove($name) }
$child = [Diagnostics.Process]::Start($start)
if (-not $child.WaitForExit(30000)) { $child.Kill(); throw 'Portable background diagnostic timed out; test data retained.' }
$report = if (Test-Path -LiteralPath $reportPath) { Get-Content -LiteralPath $reportPath -Raw } else { 'No report.' }
if ($child.ExitCode -ne 0 -or $report -notmatch 'PASS: fully occluded background diagnostic') { throw ('Portable background diagnostic failed: ' + $report) }
if (@(Get-ChildItem -LiteralPath $singleFolder -Force).Count -ne 1) { throw 'Unexpected sibling dependency or data file.' }
$result = @(('Portable background self-check: ' + (Get-Date -Format o)), 'PASS: one renamed read-only EXE; Chinese/spaces/apostrophe path; empty PATH; Windows working directory.', $report, ('SHA256: ' + (Get-FileHash -LiteralPath $exeFullPath -Algorithm SHA256).Hash))
$result | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'background-portable-result.txt') -Encoding utf8
$result | Write-Output
(Get-Item -LiteralPath $singleExe).IsReadOnly = $false
Remove-Item -LiteralPath $singleExe, $reportPath
Remove-Item -LiteralPath $singleFolder
Remove-Item -LiteralPath $auditRoot
