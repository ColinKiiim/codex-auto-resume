param([switch]$QuestionTests, [string[]]$QuestionImages, [switch]$WorkflowTests, [switch]$UpdateTests, [switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler was not found.' }
$distPath = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $distPath -Force | Out-Null
$version = [IO.File]::ReadAllText((Join-Path $projectRoot 'VERSION')).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must contain major.minor.patch.' }
& (Join-Path $projectRoot 'tools\assert-version-policy.ps1') -Version $version
$generatedVersion = Join-Path $distPath 'Version.g.cs'
$versionSource = '[assembly: System.Reflection.AssemblyVersion("' + $version + '.0")]' + [Environment]::NewLine + '[assembly: System.Reflection.AssemblyFileVersion("' + $version + '.0")]' + [Environment]::NewLine + 'namespace CodexAutoResume { internal static class BuildVersion { public const string Value = "' + $version + '"; } }'
[IO.File]::WriteAllText($generatedVersion, $versionSource, [Text.UTF8Encoding]::new($false))
[xml]$manifest = [IO.File]::ReadAllText((Join-Path $projectRoot 'app.manifest'))
$manifest.assembly.assemblyIdentity.version = $version + '.0'
$generatedManifest = Join-Path $distPath 'app.generated.manifest'; $manifest.Save($generatedManifest)
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
$sources = @($sources) + @($generatedVersion)
& (Join-Path $projectRoot 'tools\build-icon.ps1')
$commonArgs = @('/nologo', '/optimize+', '/platform:x64', '/utf8output', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', ('/win32manifest:' + $generatedManifest))
$commonArgs += '/reference:' + (Join-Path $env:WINDIR 'System32\WinMetadata\Windows.Graphics.winmd')
$commonArgs += @('/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll')
$commonArgs += '/reference:System.Management.dll'
foreach ($frameworkAssembly in @('UIAutomationClient', 'UIAutomationTypes', 'WindowsBase')) {
    $frameworkPath = Get-ChildItem -LiteralPath (Join-Path $env:WINDIR ('Microsoft.NET\assembly\GAC_MSIL\' + $frameworkAssembly)) -Filter ($frameworkAssembly + '.dll') -Recurse | Select-Object -First 1
    $commonArgs += '/reference:' + $frameworkPath.FullName
}
$commonArgs += '/reference:' + (Join-Path $env:WINDIR 'System32\WinMetadata\Windows.Foundation.winmd')
foreach ($metadata in @('Windows.Media', 'Windows.Storage', 'Windows.Globalization')) { $commonArgs += '/reference:' + (Join-Path $env:WINDIR ('System32\WinMetadata\' + $metadata + '.winmd')) }
$commonArgs += '/reference:' + (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll')
$commonArgs += '/reference:' + (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Runtime.InteropServices.WindowsRuntime.dll')
$runtimeFacade = Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime') -Filter System.Runtime.dll -Recurse | Select-Object -First 1
$commonArgs += '/reference:' + $runtimeFacade.FullName
foreach ($sampleName in @('resume', 'running', 'send', 'send-current', 'voice', 'voice-close')) {
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
    & $compiler @commonArgs '/target:exe' '/main:CodexAutoResume.Tests' ('/out:' + (Join-Path $distPath 'CodexAutoResume.Tests.exe')) @sources $testSource (Join-Path $projectRoot 'tests\UpdateTests.cs') (Join-Path $projectRoot 'tests\UsageTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & (Join-Path $distPath 'CodexAutoResume.Tests.exe') $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($UpdateTests) {
    & $compiler @commonArgs '/target:exe' '/main:CodexAutoResume.UpdateIntegrationTests' ('/out:' + (Join-Path $distPath 'CodexAutoResume.UpdateIntegrationTests.exe')) @sources (Join-Path $projectRoot 'tests\UpdateIntegrationTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Update integration test compilation failed.' }
    & (Join-Path $distPath 'CodexAutoResume.UpdateIntegrationTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Update integration tests failed.' }
    return
}
if ($WorkflowTests) {
    & $compiler @commonArgs '/target:exe' '/main:CodexAutoResume.WorkflowTests' ('/out:' + (Join-Path $distPath 'CodexAutoResume.WorkflowTests.exe')) @sources (Join-Path $projectRoot 'tests\WorkflowTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Workflow test compilation failed.' }
    & (Join-Path $distPath 'CodexAutoResume.WorkflowTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Workflow tests failed.' }
    return
}
if ($QuestionTests) {
    & $compiler @commonArgs '/target:exe' '/main:CodexAutoResume.QuestionTests' ('/out:' + (Join-Path $distPath 'CodexAutoResume.QuestionTests.exe')) @sources (Join-Path $projectRoot 'tests\QuestionTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Question test compilation failed.' }
    & (Join-Path $distPath 'CodexAutoResume.QuestionTests.exe') @QuestionImages
    if ($LASTEXITCODE -ne 0) { throw 'Question tests failed.' }
    return
}
if ($BuildOnly) { Write-Output ('Build-only verified: ' + $version + '; release history unchanged.'); return }
$candidateExe = Join-Path $distPath 'CodexAutoResume.exe'
& (Join-Path $projectRoot 'tests\background-portable.ps1') -ExecutablePath $candidateExe
if (-not $?) { throw 'Portable background diagnostic failed.' }
& (Join-Path $projectRoot 'tools\package-source.ps1')
if (-not $?) { throw 'Source packaging failed.' }
& (Join-Path $projectRoot 'tools\publish-release.ps1') -Version $version -ExecutablePath $candidateExe -SourceArchivePath (Join-Path $projectRoot ('packages\CodexAutoResume-' + $version + '-source.zip')) -NotesPath (Join-Path $projectRoot ('docs\releases\v' + $version + '.md')) -PromoteLatest
if (-not $?) { throw 'Versioned release publication failed.' }
Write-Output ('Built: ' + (Join-Path $distPath 'CodexAutoResume.exe'))
Write-Output ('Copy this one file to another PC: ' + (Join-Path $projectRoot 'release\CodexAutoResume.exe'))
