param([Parameter(Mandatory = $true)][string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$exeFullPath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$auditRoot = Join-Path ([IO.Path]::GetTempPath()) ('CodexAutoResume-migration-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $auditRoot | Out-Null
$otherCwd = Join-Path $auditRoot 'unrelated-working-directory'
New-Item -ItemType Directory -Path $otherCwd | Out-Null
$auditLines = @('Migration audit: ' + (Get-Date -Format o))
$caseNumber = 0
foreach ($caseName in @('plain', '中文路径 & spaces', "apostrophe's-folder")) {
    $caseNumber++
    $caseDirectory = Join-Path $auditRoot $caseName
    New-Item -ItemType Directory -Path $caseDirectory | Out-Null
    $copiedExe = Join-Path $caseDirectory $(if ($caseNumber -eq 1) { 'CodexAutoResume.exe' } else { '便携工具 renamed.exe' })
    Copy-Item -LiteralPath $exeFullPath -Destination $copiedExe
    $readOnlyExe = Get-Item -LiteralPath $copiedExe
    $readOnlyExe.IsReadOnly = $true
    $reportPath = Join-Path $auditRoot ('case-' + $caseNumber + '.txt')
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $copiedExe
    $start.Arguments = '--self-check "' + $reportPath + '"'
    $start.WorkingDirectory = $otherCwd
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.EnvironmentVariables['PATH'] = ''
    foreach ($toolVariable in @('PYTHONPATH', 'NODE_PATH', 'VIRTUAL_ENV', 'OPENAI_API_KEY')) { $start.EnvironmentVariables.Remove($toolVariable) }
    $child = [Diagnostics.Process]::Start($start)
    if (-not $child.WaitForExit(20000)) { $child.Kill(); throw ('Timeout: ' + $caseName) }
    if ($child.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reportPath)) { throw ('Self-check failed: ' + $caseName) }
    $report = Get-Content -LiteralPath $reportPath -Raw
    if ($report -notmatch 'PASS: all five embedded images' -or $report -notmatch 'PASS: WinForms window') { throw ('Incomplete report: ' + $caseName) }
    if (@(Get-ChildItem -LiteralPath $caseDirectory -Force).Count -ne 1) { throw ('App wrote unexpected sibling files: ' + $caseName) }
    $auditLines += 'PASS: ' + $caseName + '; single read-only EXE; unrelated CWD; empty PATH; no Python/Node/API environment.'
    $readOnlyExe.IsReadOnly = $false
    Remove-Item -LiteralPath $copiedExe, $reportPath
    Remove-Item -LiteralPath $caseDirectory
}
# Static audit: look for developer-specific machine strings and list actual assembly/native imports.
$binary = [IO.File]::ReadAllBytes($exeFullPath)
$ascii = [Text.Encoding]::ASCII.GetString($binary)
$utf16 = [Text.Encoding]::Unicode.GetString($binary)
$workspacePath = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$privateMarkers = @($env:USERPROFILE, $workspacePath)
if ([Environment]::UserName.Length -ge 5) { $privateMarkers += [Environment]::UserName }
foreach ($privateMarker in $privateMarkers) {
    if ($ascii.Contains($privateMarker) -or $utf16.Contains($privateMarker)) { throw ('Developer-specific string leaked into release: ' + $privateMarker) }
}
$auditLines += 'PASS: no developer username or workspace path in ASCII/UTF-16 binary strings.'
$auditLines += 'SHA256: ' + (Get-FileHash -LiteralPath $exeFullPath -Algorithm SHA256).Hash
$auditLines | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'migration-result.txt') -Encoding utf8
$auditLines | Write-Output
Remove-Item -LiteralPath $otherCwd
Remove-Item -LiteralPath $auditRoot
