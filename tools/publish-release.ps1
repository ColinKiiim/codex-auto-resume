param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [Parameter(Mandatory = $true)][string]$SourceArchivePath,
    [Parameter(Mandatory = $true)][string]$NotesPath,
    [ValidateSet('original-release', 'rebuilt-from-archived-source')][string]$BinaryOrigin = 'original-release',
    [string]$OutputRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'release'),
    [switch]$PromoteLatest
)
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
$exe = (Resolve-Path -LiteralPath $ExecutablePath).Path
$source = (Resolve-Path -LiteralPath $SourceArchivePath).Path
$notes = (Resolve-Path -LiteralPath $NotesPath).Path
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne ($Version + '.0')) { throw 'EXE file version does not match release version.' }
$notesText = [IO.File]::ReadAllText($notes)
if (-not ($notesText.StartsWith('# CodexAutoResume v' + $Version + "`n") -or $notesText.StartsWith('# CodexAutoResume v' + $Version + "`r`n") -or $notesText.StartsWith('# v' + $Version + ' '))) { throw 'Release notes must begin with the matching version heading.' }
if ($notesText -match '\{\{[^}]+\}\}') { throw 'Replace release-template placeholders before publishing.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceZip = [IO.Compression.ZipFile]::OpenRead($source)
try {
    $versionEntry = $sourceZip.GetEntry('CodexAutoResume/VERSION')
    if ($versionEntry) {
        $reader = [IO.StreamReader]::new($versionEntry.Open())
        try { $sourceVersion = $reader.ReadToEnd().Trim() } finally { $reader.Dispose() }
    } else {
        $legacyVersion = $sourceZip.GetEntry('CodexAutoResume/src/Portable.cs')
        if (-not $legacyVersion) { throw 'Source version metadata missing.' }
        $reader = [IO.StreamReader]::new($legacyVersion.Open())
        try { $sourceText = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($sourceText -notmatch 'AssemblyFileVersion\("(\d+\.\d+\.\d+)\.0"\)') { throw 'Archived source version metadata missing.' }
        $sourceVersion = $Matches[1]
    }
    if ($sourceVersion -ne $Version -or -not $sourceZip.GetEntry('CodexAutoResume/LICENSE') -or -not $sourceZip.GetEntry('CodexAutoResume/build.ps1')) { throw 'Source archive does not match release version or is incomplete.' }
} finally { $sourceZip.Dispose() }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$publicationLock = [IO.File]::Open((Join-Path $OutputRoot '.publish.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
$tag = 'v' + $Version
$versionDirectory = Join-Path $OutputRoot $tag
$latestPath = Join-Path $OutputRoot 'latest.json'
if ($PromoteLatest -and (Test-Path -LiteralPath $latestPath)) {
    $priorLatest = Get-Content -LiteralPath $latestPath -Raw | ConvertFrom-Json
    if ([version]$priorLatest.version -gt [version]$Version) { throw 'Refusing to move latest backwards; archive older versions without -PromoteLatest.' }
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Write-Atomic([string]$Path, [string]$Text) {
    $temporary = $Path + '.tmp-' + [Guid]::NewGuid().ToString('N')
    [IO.File]::WriteAllText($temporary, $Text, $utf8)
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}
function Assert-Release([string]$Directory) {
    $manifest = Get-Content -LiteralPath (Join-Path $Directory 'release.json') -Raw | ConvertFrom-Json
    foreach ($asset in $manifest.assets) {
        if ($asset.name -ne [IO.Path]::GetFileName($asset.name)) { throw 'Invalid asset path in release manifest.' }
        $assetPath = Join-Path $Directory $asset.name
        if (-not (Test-Path -LiteralPath $assetPath) -or (Hash $assetPath) -ne $asset.sha256 -or (Get-Item -LiteralPath $assetPath).Length -ne $asset.bytes) { throw ('Archived release integrity check failed: ' + $asset.name) }
    }
    $sumLines = @(Get-Content -LiteralPath (Join-Path $Directory 'SHA256SUMS.txt'))
    foreach ($sum in $sumLines) {
        if ($sum -notmatch '^([0-9a-f]{64})  ([^\\/]+)$') { throw 'Invalid checksum entry.' }
        if ((Hash (Join-Path $Directory $Matches[2])) -ne $Matches[1]) { throw 'Archived release checksum mismatch.' }
    }
    $expectedNames = @($manifest.assets.name) + @('release.json')
    if ($sumLines.Count -ne $expectedNames.Count) { throw 'Incomplete checksum list.' }
    foreach ($name in $expectedNames) { if (-not ($sumLines | Where-Object { $_.Substring(66) -ceq $name })) { throw ('Missing checksum: ' + $name) } }
    return $manifest
}
$inputs = @(
    @{ name = ('CodexAutoResume-' + $tag + '-windows-x64.exe'); kind = 'portable-executable'; path = $exe },
    @{ name = ('CodexAutoResume-' + $tag + '-source.zip'); kind = 'source'; path = $source },
    @{ name = 'RELEASE_NOTES.md'; kind = 'release-notes'; path = $notes }
)
$assets = @($inputs | ForEach-Object { [ordered]@{ name = $_.name; kind = $_.kind; bytes = (Get-Item -LiteralPath $_.path).Length; sha256 = Hash $_.path } })
if (Test-Path -LiteralPath $versionDirectory) {
    $existing = Assert-Release $versionDirectory
    if ($existing.version -ne $Version -or $existing.binaryOrigin -ne $BinaryOrigin -or $existing.assets.Count -ne $assets.Count) { throw 'Version already exists with different metadata; create a new version.' }
    foreach ($asset in $assets) {
        $old = @($existing.assets | Where-Object { $_.name -ceq $asset.name })
        if ($old.Count -ne 1 -or $old[0].sha256 -ne $asset.sha256) { throw ('Version ' + $Version + ' is immutable; changed asset ' + $asset.name + '. Increment VERSION instead of overwriting history.') }
    }
    Write-Output ('Already archived and verified: ' + $tag)
} else {
    $staging = Join-Path $OutputRoot ($tag + '.pending-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging | Out-Null
    foreach ($inputAsset in $inputs) { Copy-Item -LiteralPath $inputAsset.path -Destination (Join-Path $staging $inputAsset.name) }
    $metadata = [ordered]@{
        schemaVersion = 1; version = $Version; tag = $tag; name = ('CodexAutoResume ' + $tag); platform = 'windows'; architecture = 'x64'; license = 'MIT'
        binaryOrigin = $BinaryOrigin
        archivedAt = [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).ToString('o')
        assets = $assets
    }
    [IO.File]::WriteAllText((Join-Path $staging 'release.json'), ($metadata | ConvertTo-Json -Depth 6), $utf8)
    $sum = @($assets | ForEach-Object { $_.sha256 + '  ' + $_.name })
    $sum += (Hash (Join-Path $staging 'release.json')) + '  release.json'
    [IO.File]::WriteAllText((Join-Path $staging 'SHA256SUMS.txt'), ($sum -join "`n") + "`n", $utf8)
    $null = Assert-Release $staging
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($staging)) -ne $OutputRoot.TrimEnd('\') -or [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($versionDirectory)) -ne $OutputRoot.TrimEnd('\')) { throw 'Release staging paths must be direct children of the release root.' }
    Move-Item -LiteralPath $staging -Destination $versionDirectory
    Write-Output ('Archived without overwriting history: ' + $tag)
}
if ($PromoteLatest) {
    $program = $assets | Where-Object { $_.kind -eq 'portable-executable' }
    $latestExe = Join-Path $OutputRoot 'CodexAutoResume.exe'
    $nextExe = Join-Path $OutputRoot ('latest-exe.tmp-' + [Guid]::NewGuid().ToString('N'))
    Copy-Item -LiteralPath (Join-Path $versionDirectory $program.name) -Destination $nextExe
    try { Move-Item -LiteralPath $nextExe -Destination $latestExe -Force }
    catch { throw 'Version archive is safe. Close the running utility before replacing the latest EXE, then rerun publication.' }
    Write-Atomic $latestPath ([ordered]@{ schemaVersion = 1; version = $Version; tag = $tag; path = ($tag + '/' + $program.name); sha256 = $program.sha256 } | ConvertTo-Json)
}
$versions = @(Get-ChildItem -LiteralPath $OutputRoot -Directory | Where-Object { $_.Name -match '^v\d+\.\d+\.\d+$' } | ForEach-Object { Assert-Release $_.FullName } | Sort-Object { [version]$_.version } -Descending)
# Prepare GitHub-native titles, bilingual bodies and the three download assets.
# These are local exports only; uploading and publishing are separate operations.
$githubRoot = Join-Path $OutputRoot 'github'
New-Item -ItemType Directory -Path $githubRoot -Force | Out-Null
foreach ($item in $versions) {
    $export = Join-Path $githubRoot $item.tag
    $exportAssets = Join-Path $export 'assets'
    New-Item -ItemType Directory -Path $exportAssets -Force | Out-Null
    $archivedNotes = Join-Path (Join-Path $OutputRoot $item.tag) 'RELEASE_NOTES.md'
    $normalizedNotes = [IO.File]::ReadAllText($archivedNotes).Replace("`r`n", "`n").Replace("`r", "`n")
    $body = ($normalizedNotes -replace '\A#[^\r\n]+\n\s*', '').Trim() + "`n"
    Write-Atomic (Join-Path $export 'release-body.md') $body
    Write-Atomic (Join-Path $export 'release-draft.json') ([ordered]@{
        tag_name = $item.tag; name = ('CodexAutoResume ' + $item.tag)
        body = $body; draft = $true; prerelease = $false
    } | ConvertTo-Json -Depth 4)
    $downloadAssets = @($item.assets | Where-Object { $_.kind -in @('portable-executable', 'source') })
    foreach ($asset in $downloadAssets) {
        $destination = Join-Path $exportAssets $asset.name
        if (-not (Test-Path -LiteralPath $destination) -or (Hash $destination) -ne $asset.sha256) {
            Copy-Item -LiteralPath (Join-Path (Join-Path $OutputRoot $item.tag) $asset.name) -Destination $destination -Force
        }
    }
    Write-Atomic (Join-Path $exportAssets 'SHA256SUMS.txt') ((@($downloadAssets | ForEach-Object { $_.sha256 + '  ' + $_.name }) -join "`n") + "`n")
}
$latestVersion = if (Test-Path -LiteralPath $latestPath) { (Get-Content -LiteralPath $latestPath -Raw | ConvertFrom-Json).version } else { $null }
Write-Atomic (Join-Path $OutputRoot 'releases.json') ([ordered]@{ schemaVersion = 1; latest = $latestVersion; releases = $versions } | ConvertTo-Json -Depth 8)
$readme = @('# CodexAutoResume Releases', '', 'Windows x64 便携版：[`CodexAutoResume.exe`](CodexAutoResume.exe)。下载后双击运行。', '', '## 历史版本', '', '| Release | Windows x64 | Source | 更新日志 | SHA256 |', '|---|---|---|---|---|')
foreach ($item in $versions) {
    $label = 'CodexAutoResume v' + $item.version
    if ($item.version -eq $latestVersion) { $label += ' · Latest' }
    $program = $item.assets | Where-Object { $_.kind -eq 'portable-executable' }
    $sources = $item.assets | Where-Object { $_.kind -eq 'source' }
    $readme += ('| ' + $label + ' | [EXE](' + $item.tag + '/' + $program.name + ') | [ZIP](' + $item.tag + '/' + $sources.name + ') | [说明](' + $item.tag + '/RELEASE_NOTES.md) | [SHA256](' + $item.tag + '/SHA256SUMS.txt) |')
}
$readme += @('', 'GitHub Release 标题、正文和下载附件位于 `github/v版本号/`。发布方式见 [发布指南](../docs/releasing.md)。')
Write-Atomic (Join-Path $OutputRoot 'README.md') (($readme -join "`n") + "`n")
} finally { $publicationLock.Dispose() }
