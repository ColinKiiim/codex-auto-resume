param([string]$ExecutablePath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\CodexAutoResume.exe'))
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -ExecutablePath $ExecutablePath
    if ($LASTEXITCODE -ne 0) { throw 'Framework live usage probe failed.' }
    return
}
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $ExecutablePath).Path)
$staticFlags = [Reflection.BindingFlags]'Static,NonPublic'
$fieldFlags = [Reflection.BindingFlags]'Instance,NonPublic'
$target = Get-Process -Name ChatGPT,Codex -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 -and $_.Path -match '\\(ChatGPT|Codex)\.exe$' } | Select-Object -First 1
if (-not $target) { throw 'No running desktop target; live usage was not tested.' }
$sourceType = $assembly.GetType('CodexAutoResume.UsageSource')
$source = $sourceType.GetMethod('Find', $staticFlags).Invoke($null, @([string]$target.Path, 'live-probe'))
if (-not $source) { throw 'Target installation has no supported bundled CLI.' }
$before = @(Get-Process -Name codex -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
$task = $assembly.GetType('CodexAutoResume.CodexUsage').GetMethod('ReadAsync', $staticFlags).Invoke($null, @($source, [Threading.CancellationToken]::None, 20000))
$result = $task.GetAwaiter().GetResult()
$resultType = $result.GetType()
$errorCode = $resultType.GetField('Error', $fieldFlags).GetValue($result).ToString()
if ($errorCode -ne 'None') { throw ('Live usage failed: ' + $errorCode + ' at ' + $resultType.GetField('Stage', $fieldFlags).GetValue($result)) }
$snapshot = $resultType.GetField('Snapshot', $fieldFlags).GetValue($result)
$windows = $snapshot.GetType().GetField('Windows', $fieldFlags).GetValue($snapshot)
$safeWindows = @($windows | ForEach-Object { @{ remaining = $_.GetType().GetField('Remaining', $fieldFlags).GetValue($_); minutes = $_.GetType().GetField('Minutes', $fieldFlags).GetValue($_) } })
# Do not print account identifiers, email, credential data or raw RPC responses.
Start-Sleep -Milliseconds 250
$newProcesses = @(Get-Process -Name codex -ErrorAction SilentlyContinue | Where-Object { $_.Id -notin $before })
if ($newProcesses.Count) { throw 'A new Codex process remained after the usage query; inspect owned children.' }
[pscustomobject]@{ status = 'passed'; windows = $safeWindows; newCodexProcesses = $newProcesses.Count } | ConvertTo-Json -Depth 5 -Compress
