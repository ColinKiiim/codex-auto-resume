param([string]$ExecutablePath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\CodexAutoResume.exe'), [string]$ElectronPath)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$out=Join-Path ([IO.Path]::GetTempPath()) ('CAR-controls-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$bridge=Join-Path $out 'ControlsFixtureBridge.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /platform:x64 /target:winexe /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll ('/out:'+$bridge) (Join-Path $PSScriptRoot 'ControlsFixtureBridge.cs')
if($LASTEXITCODE -ne 0){throw 'Control fixture compilation failed'}
$chrome='C:\Program Files\Google\Chrome\Application\chrome.exe'
if(-not (Test-Path -LiteralPath $chrome)){throw 'This OS boundary check needs a separately installed Chrome test instance'}
$kind='chrome'
if($ElectronPath){$chrome=(Resolve-Path -LiteralPath $ElectronPath).Path;$kind='electron'}
& node (Join-Path $repo 'tools\test-background-controls.mjs') (Resolve-Path -LiteralPath $ExecutablePath).Path $bridge $chrome $out $kind
if($LASTEXITCODE -ne 0){throw ('Background controls check failed; evidence: '+$out)}
Write-Output ('Background control evidence: '+$out)
