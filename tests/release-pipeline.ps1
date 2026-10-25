param([string]$ExecutablePath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\CodexAutoResume.exe'))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$publisher = Join-Path $projectRoot 'tools\publish-release.ps1'
$exePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).FileVersion -replace '\.0$', ''
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('CAR-release-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$sourcePath = Join-Path $testRoot 'source.zip'
$notesPath = Join-Path $testRoot 'notes.md'
$outputRoot = Join-Path $testRoot 'output'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($sourcePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in @('VERSION', 'LICENSE', 'build.ps1')) {
        $writer = New-Object IO.StreamWriter($zip.CreateEntry('CodexAutoResume/' + $name).Open())
        try { $writer.Write($(if ($name -eq 'VERSION') { $version } else { 'fixture' })) } finally { $writer.Dispose() }
    }
} finally { $zip.Dispose() }
[IO.File]::WriteAllText($notesPath, ('# CodexAutoResume v' + $version + "`n`nRelease integrity fixture`n"))
$inputs = @{ Version=$version; ExecutablePath=$exePath; SourceArchivePath=$sourcePath; NotesPath=$notesPath; OutputRoot=$outputRoot }
function Require([bool]$Ok, [string]$Message) { if (-not $Ok) { throw $Message }; Write-Output ('PASS: ' + $Message) }
& $publisher @inputs -PromoteLatest | Out-Null
$archivedExe = Join-Path $outputRoot ('v' + $version + '/CodexAutoResume-v' + $version + '-windows-x64.exe')
$hash = (Get-FileHash -LiteralPath $exePath).Hash
Require ((Get-FileHash -LiteralPath $archivedExe).Hash -eq $hash) 'archive preserves exact executable bytes'
$assets = Join-Path $outputRoot ('github/v' + $version + '/assets')
Require (@(Get-ChildItem -LiteralPath $assets -File).Count -eq 3) 'download export contains EXE, source and checksums'
foreach ($sum in Get-Content -LiteralPath (Join-Path $assets 'SHA256SUMS.txt')) {
    if ($sum -notmatch '^([0-9a-f]{64})  ([^\\/]+)$' -or (Get-FileHash -LiteralPath (Join-Path $assets $Matches[2])).Hash.ToLowerInvariant() -ne $Matches[1]) { throw 'Export checksum mismatch' }
}
Write-Output 'PASS: download checksums match actual assets'
& $publisher @inputs -PromoteLatest | Out-Null
Require ((Get-FileHash -LiteralPath $archivedExe).Hash -eq $hash) 'identical publication remains idempotent'
[IO.File]::AppendAllText($notesPath, 'changed')
$rejected = $false
try { & $publisher @inputs -PromoteLatest | Out-Null } catch { $rejected = $true }
Require $rejected 'same-version changed contents cannot overwrite history'
Require ((Get-FileHash -LiteralPath $archivedExe).Hash -eq $hash) 'rejected publication preserves existing executable'
# Keep this small fixture in the exact GUID directory for inspection; it is not a repo artifact.
Write-Output ('Release fixture: ' + $testRoot)
