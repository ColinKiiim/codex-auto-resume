param([switch]$BackgroundTests, [switch]$TargetProbe, [switch]$ChromiumTests)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler was not found.' }
$distPath = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $distPath -Force | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& (Join-Path $projectRoot 'tools\build-icon.ps1')
$commonArgs = @('/nologo', '/optimize+', '/platform:x64', '/utf8output', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')))
$commonArgs += '/reference:' + (Join-Path $env:WINDIR 'System32\WinMetadata\Windows.Graphics.winmd')
$commonArgs += '/reference:' + (Join-Path $env:WINDIR 'System32\WinMetadata\Windows.Foundation.winmd')
$commonArgs += '/reference:' + (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll')
$runtimeFacade = Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime') -Filter System.Runtime.dll -Recurse | Select-Object -First 1
$commonArgs += '/reference:' + $runtimeFacade.FullName
foreach ($sampleName in @('resume', 'running', 'send', 'send-current', 'voice')) {
    $resourceName = $sampleName + '-reference.png'
    $commonArgs += '/resource:' + (Join-Path $projectRoot ('assets\' + $resourceName)) + ',' + $resourceName
}
$commonArgs += '/win32icon:' + (Join-Path $projectRoot 'assets\app.ico')
$commonArgs += '/resource:' + (Join-Path $projectRoot 'assets\app.ico') + ',app.ico'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'LICENSE') + ',LICENSE'
& $compiler @commonArgs '/target:winexe' '/main:CodexAutoResume.Program' ('/out:' + (Join-Path $distPath 'CodexAutoResume.exe')) @sources
if ($LASTEXITCODE -ne 0) { throw 'App compilation failed.' }
$testSource = Join-Path $projectRoot 'tests\Tests.cs'
if (Test-Path -LiteralPath $testSource) {
    & $compiler @commonArgs '/target:exe' '/main:CodexAutoResume.Tests' ('/out:' + (Join-Path $distPath 'CodexAutoResume.Tests.exe')) @sources $testSource
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & (Join-Path $distPath 'CodexAutoResume.Tests.exe') $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($ChromiumTests) {
    & $compiler @commonArgs '/target:exe' '/main:CodexAutoResume.ChromiumTests' ('/out:' + (Join-Path $distPath 'CodexAutoResume.ChromiumTests.exe')) @sources (Join-Path $projectRoot 'tests\ChromiumTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Chromium test compilation failed.' }
    & (Join-Path $distPath 'CodexAutoResume.ChromiumTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Chromium tests failed.' }
    return
}
if ($TargetProbe) {
    & $compiler @commonArgs '/target:exe' '/main:CodexAutoResume.TargetCaptureProbe' ('/out:' + (Join-Path $distPath 'CodexAutoResume.TargetCaptureProbe.exe')) @sources (Join-Path $projectRoot 'tests\TargetCaptureProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Target probe compilation failed.' }
    & (Join-Path $distPath 'CodexAutoResume.TargetCaptureProbe.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Target probe failed.' }
    return
}
if ($BackgroundTests) {
    & $compiler @commonArgs '/target:exe' '/main:CodexAutoResume.BackgroundTests' ('/out:' + (Join-Path $distPath 'CodexAutoResume.BackgroundTests.exe')) @sources (Join-Path $projectRoot 'tests\BackgroundTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Background test compilation failed.' }
    & (Join-Path $distPath 'CodexAutoResume.BackgroundTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Background tests failed.' }
    return
}
$releasePath = Join-Path $projectRoot 'release'
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
$releaseExe = Join-Path $releasePath 'CodexAutoResume.exe'
Copy-Item -LiteralPath (Join-Path $distPath 'CodexAutoResume.exe') -Destination $releaseExe -Force
& (Join-Path $projectRoot 'tests\portable-smoke.ps1') -ExecutablePath $releaseExe
if (-not $?) { throw 'Portable release verification failed.' }
& (Join-Path $projectRoot 'tests\migration-audit.ps1') -ExecutablePath $releaseExe
if (-not $?) { throw 'Migration audit failed.' }
& (Join-Path $projectRoot 'tools\package-source.ps1')
if (-not $?) { throw 'Source packaging failed.' }
Write-Output ('Built: ' + (Join-Path $distPath 'CodexAutoResume.exe'))
Write-Output ('Copy this one file to another PC: ' + $releaseExe)
