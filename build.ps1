$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler was not found.' }
$distPath = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $distPath -Force | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& (Join-Path $projectRoot 'tools\build-icon.ps1')
$commonArgs = @('/nologo', '/optimize+', '/platform:x64', '/utf8output', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')))
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
