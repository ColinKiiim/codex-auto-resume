$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$publisher = Join-Path $projectRoot 'tools\publish-release.ps1'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('CodexAutoResume-release-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
$outputRoot = Join-Path $temporaryRoot '中文 releases'
$originalRoot = Join-Path $projectRoot 'release\v1.5.0'
$inputs = @{ Version='1.5.0'; ExecutablePath=(Join-Path $originalRoot 'CodexAutoResume-v1.5.0-windows-x64.exe'); SourceArchivePath=(Join-Path $originalRoot 'CodexAutoResume-v1.5.0-source.zip'); NotesPath=(Join-Path $originalRoot 'RELEASE_NOTES.md'); OutputRoot=$outputRoot }
$checks = 0
function Check([bool]$Ok, [string]$Description) { if (-not $Ok) { throw $Description }; $script:checks++; Write-Output ('PASS: ' + $Description) }
function Digest([string]$Directory) { (@(Get-ChildItem -LiteralPath $Directory -File | Sort-Object Name | ForEach-Object { $_.Name + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }) -join '|') }
function Must-Fail([scriptblock]$Action, [string]$Description) { $rejected=$false; try { & $Action | Out-Null } catch { $rejected=$true }; Check $rejected $Description }
& $publisher @inputs -PromoteLatest
$archive = Join-Path $outputRoot 'v1.5.0'
$originalDigest = Digest $archive
$latest = Get-Content -LiteralPath (Join-Path $outputRoot 'latest.json') -Raw | ConvertFrom-Json
Check ($latest.version -eq '1.5.0') 'latest points to published version'
Check ((Get-FileHash -LiteralPath (Join-Path $outputRoot 'CodexAutoResume.exe')).Hash -eq $latest.sha256) 'latest EXE exactly matches versioned asset'
$checksumLines = @(Get-Content -LiteralPath (Join-Path $archive 'SHA256SUMS.txt'))
Check ($checksumLines.Count -eq 4) 'checksums include executable, source, notes and metadata'
$githubExport = Join-Path $outputRoot 'github\v1.5.0'
$draft = Get-Content -LiteralPath (Join-Path $githubExport 'release-draft.json') -Raw | ConvertFrom-Json
$body = [IO.File]::ReadAllText((Join-Path $githubExport 'release-body.md'))
Check ($draft.name -eq 'v1.5.0 · CodexAutoResume' -and $draft.tag_name -eq 'v1.5.0' -and $draft.draft -and -not $draft.prerelease) 'GitHub title starts with the version for readable sidebar navigation and remains a draft'
Check ($draft.body -eq $body -and $body.StartsWith('## 更新日志（中文）') -and $body.Contains('## Changelog (English)')) 'GitHub body preserves both languages without a duplicate release title'
$downloadDirectory = Join-Path $githubExport 'assets'
$downloadSums = @(Get-Content -LiteralPath (Join-Path $downloadDirectory 'SHA256SUMS.txt'))
Check (@(Get-ChildItem -LiteralPath $downloadDirectory -File).Count -eq 3 -and $downloadSums.Count -eq 2) 'GitHub Assets contains only executable, source and download checksums'
foreach ($line in $downloadSums) {
    if ($line -notmatch '^([0-9a-f]{64})  ([^\\/]+)$' -or (Get-FileHash -LiteralPath (Join-Path $downloadDirectory $Matches[2])).Hash.ToLowerInvariant() -ne $Matches[1]) { throw 'GitHub download checksum mismatch' }
}
Check ((Get-FileHash -LiteralPath (Join-Path $downloadDirectory 'CodexAutoResume-v1.5.0-windows-x64.exe')).Hash -eq $latest.sha256) 'GitHub export keeps the exact archived executable bytes'
& $publisher @inputs -PromoteLatest
Check ((Digest $archive) -eq $originalDigest) 'identical publication is idempotent and preserves archive bytes'
$changedNotes = Join-Path $temporaryRoot 'changed-notes.md'
[IO.File]::WriteAllText($changedNotes, [IO.File]::ReadAllText($inputs.NotesPath) + "`nChanged for rejection test.")
$changed = $inputs.Clone(); $changed.NotesPath = $changedNotes
Must-Fail { & $publisher @changed } 'same-version changed asset is rejected'
Check ((Digest $archive) -eq $originalDigest) 'rejected publication leaves old assets unchanged'
$templateNotes = Join-Path $temporaryRoot 'template-notes.md'
[IO.File]::WriteAllText($templateNotes, [IO.File]::ReadAllText($inputs.NotesPath) + "`n{{UNFINISHED}}")
$templateInput = $inputs.Clone(); $templateInput.NotesPath = $templateNotes
Must-Fail { & $publisher @templateInput } 'unfinished release-template placeholders are rejected'
$mismatch = $inputs.Clone(); $mismatch.Version = '1.5.9'
Must-Fail { & $publisher @mismatch } 'mismatched EXE version is rejected'
Check (-not (Test-Path -LiteralPath (Join-Path $outputRoot 'v1.5.9'))) 'version rejection creates no misleading archive'
$wrongSource = $inputs.Clone(); $wrongSource.SourceArchivePath = Join-Path $projectRoot 'packages\CodexAutoResume-1.4.0-source.zip'
Must-Fail { & $publisher @wrongSource } 'source ZIP from another version is rejected'
$oldRoot = Join-Path $projectRoot 'release\v1.4.0'
$oldInputs = @{ Version='1.4.0'; ExecutablePath=(Join-Path $oldRoot 'CodexAutoResume-v1.4.0-windows-x64.exe'); SourceArchivePath=(Join-Path $oldRoot 'CodexAutoResume-v1.4.0-source.zip'); NotesPath=(Join-Path $oldRoot 'RELEASE_NOTES.md'); BinaryOrigin='rebuilt-from-archived-source'; OutputRoot=$outputRoot }
& $publisher @oldInputs
Check ((Get-Content -LiteralPath (Join-Path $outputRoot 'latest.json') -Raw | ConvertFrom-Json).version -eq '1.5.0') 'importing history does not downgrade latest'
Must-Fail { & $publisher @oldInputs -PromoteLatest } 'accidental latest downgrade is rejected'
$index = Get-Content -LiteralPath (Join-Path $outputRoot 'releases.json') -Raw | ConvertFrom-Json
Check ($index.releases.Count -eq 2 -and $index.releases[0].version -eq '1.5.0' -and $index.releases[1].binaryOrigin -eq 'rebuilt-from-archived-source') 'index sorts versions and labels historical rebuild origin'
$lock = [IO.File]::Open((Join-Path $outputRoot '.publish.lock'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try { Must-Fail { & $publisher @inputs } 'concurrent publication cannot mutate a locked release root' } finally { $lock.Dispose() }
$archivedExe = Join-Path $archive 'CodexAutoResume-v1.5.0-windows-x64.exe'
[IO.File]::AppendAllText($archivedExe, 'tampered-fixture')
$tamperedDigest = Digest $archive
Must-Fail { & $publisher @inputs } 'corrupt archived asset is detected rather than silently replaced'
Check ((Digest $archive) -eq $tamperedDigest) 'integrity failure preserves evidence for diagnosis'
$sourceOutput = Join-Path $temporaryRoot 'source packages'
$packager = Join-Path $projectRoot 'tools\package-source.ps1'
& $packager -OutputDirectory $sourceOutput
$currentVersion = [IO.File]::ReadAllText((Join-Path $projectRoot 'VERSION')).Trim()
$newSource = Join-Path $sourceOutput ('CodexAutoResume-' + $currentVersion + '-source.zip')
$sourceHash = (Get-FileHash -LiteralPath $newSource).Hash
& $packager -OutputDirectory $sourceOutput
Check ((Get-FileHash -LiteralPath $newSource).Hash -eq $sourceHash) 'unchanged source packaging preserves original ZIP bytes'
$zip = [IO.Compression.ZipFile]::Open($newSource, [IO.Compression.ZipArchiveMode]::Update)
try {
    Check ($null -ne $zip.GetEntry('CodexAutoResume/VERSION') -and $null -ne $zip.GetEntry('CodexAutoResume/CHANGELOG.md')) 'source snapshot includes version authority and change history'
    Check (@($zip.Entries | Where-Object { $_.FullName -match '/release/|/dist/|background-progress' }).Count -eq 0) 'source snapshot excludes generated release assets and private checkpoint'
    $stream = $zip.GetEntry('CodexAutoResume/VERSION').Open()
    try { $stream.SetLength(0); $bytes=[Text.Encoding]::UTF8.GetBytes('0.0.0'); $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
} finally { $zip.Dispose() }
$changedSourceHash = (Get-FileHash -LiteralPath $newSource).Hash
Must-Fail { & $packager -OutputDirectory $sourceOutput } 'same-version source content changes cannot overwrite snapshot'
Check ((Get-FileHash -LiteralPath $newSource).Hash -eq $changedSourceHash) 'rejected source packaging preserves prior archive'
Write-Output ('Release pipeline checks: ' + $checks + '; all passed. Test data retained in its task-specific temporary folder.')
