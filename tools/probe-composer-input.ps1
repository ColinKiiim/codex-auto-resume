param([Parameter(Mandatory)][ValidateRange(1,65535)][int]$DebugPort, [switch]$WriteAndClear)
$ErrorActionPreference = 'Stop'
$probeRoot = Split-Path -Parent $PSScriptRoot
$probeOutput = Join-Path $probeRoot 'dist\composer-probe'
New-Item -ItemType Directory -Path $probeOutput -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$probeExe = Join-Path $probeOutput 'ComposerInputProbe.exe'
& (Join-Path $framework 'csc.exe') /nologo /platform:x64 /target:exe /utf8output /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll ('/out:' + $probeExe) (Join-Path $PSScriptRoot 'ComposerInputProbe.cs') (Join-Path $PSScriptRoot 'ComposerDebugInput.cs')
if ($LASTEXITCODE -ne 0) { throw 'Composer input probe compilation failed.' }
# Never enable debugging or restart the app. Reject another process's endpoint.
$probeListeners = @(Get-NetTCPConnection -LocalPort $DebugPort -State Listen -ErrorAction Stop)
$probeOwners = @(Get-Process -Id ($probeListeners.OwningProcess | Select-Object -Unique) -ErrorAction Stop)
if (-not $probeOwners.Count -or @($probeOwners | Where-Object { $_.ProcessName -ne 'ChatGPT' -or $_.Path -notmatch '\\OpenAI\.Codex_[^\\]+\\app\\ChatGPT\.exe$' }).Count) { throw 'Debugging endpoint is not owned by the installed Codex process.' }
$probeArguments = @('--debug-port=' + $DebugPort)
# Default is read-only. The switch permits one temporary draft, readback and
# exact-draft cleanup under a controlled cover window; never chat submission.
if ($WriteAndClear) { $probeArguments += '--write-and-clear' }
& $probeExe @probeArguments
if ($LASTEXITCODE -ne 0) { throw 'Composer input probe did not pass; see the diagnostic above.' }
