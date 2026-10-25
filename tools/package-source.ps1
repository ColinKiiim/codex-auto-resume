param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $projectRoot 'tools/assert-version-policy.ps1') -Version ([IO.File]::ReadAllText((Join-Path $projectRoot 'VERSION')).Trim())
$version = [IO.File]::ReadAllText((Join-Path $projectRoot 'VERSION')).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must contain major.minor.patch.' }
$packageDirectory = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'packages' }
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
$archivePath = Join-Path $packageDirectory ('CodexAutoResume-' + $version + '-source.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$filePaths = @()
foreach ($fileName in @('version-policy.json', 'AGENTS.md', 'LICENSE', 'NOTICE', 'README.md', 'CHANGELOG.md', 'VERSION', '.gitignore', 'build.ps1', 'app.manifest', 'Open-CodexAutoResume.cmd')) { $filePaths += Join-Path $projectRoot $fileName }
foreach ($folder in @('src', 'assets', 'tools', 'docs', 'tests')) {
    foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -Recurse -File)) {
        $include = ($folder -ne 'tests' -or $file.Extension -in @('.cs', '.ps1')) -and $file.Name -ne 'background-progress.md'
        if ($include) { $filePaths += $file.FullName }
    }
}
if (Test-Path -LiteralPath $archivePath) {
    $oldZip = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        if ($oldZip.Entries.Count -ne $filePaths.Count) { throw 'Source snapshot already exists with different contents. Increment VERSION.' }
        foreach ($filePath in $filePaths) {
            $relativeName = $filePath.Substring($projectRoot.Length + 1).Replace('\', '/')
            $entry = $oldZip.GetEntry('CodexAutoResume/' + $relativeName)
            if (-not $entry) { throw 'Source snapshot already exists with different contents. Increment VERSION.' }
            $stream = $entry.Open(); $hasher = [Security.Cryptography.SHA256]::Create()
            try { $entryHash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','') }
            finally { $hasher.Dispose(); $stream.Dispose() }
            if ($entryHash -ne (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash) { throw ('Source snapshot is immutable; changed ' + $relativeName + '. Increment VERSION.') }
        }
    } finally { $oldZip.Dispose() }
    Write-Output ('Unchanged source archive retained: ' + $archivePath)
    return
}
$pendingArchive = Join-Path $packageDirectory ('source.pending-' + [Guid]::NewGuid().ToString('N') + '.zip')
$zip = [IO.Compression.ZipFile]::Open($pendingArchive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($filePath in $filePaths) {
        $relativeName = $filePath.Substring($projectRoot.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $filePath, ('CodexAutoResume/' + $relativeName)) | Out-Null
    }
} finally { $zip.Dispose() }
$verify = [IO.Compression.ZipFile]::OpenRead($pendingArchive)
try {
    foreach ($required in @('version-policy.json', 'LICENSE', 'NOTICE', 'VERSION', 'CHANGELOG.md', 'src/DesktopIdentity.cs', 'assets/app.ico', 'build.ps1')) {
        if (-not $verify.GetEntry('CodexAutoResume/' + $required)) { throw ('Source package missing: ' + $required) }
    }
    foreach ($entry in $verify.Entries) {
        if ($entry.FullName -match '/(logs|dist|release|packages)/|last-result|migration-result|portable-result|gui-migration-case|background-progress\.md') { throw ('Generated or private output in source package: ' + $entry.FullName) }
    }
    Write-Output ('Source package verified: ' + $verify.Entries.Count + ' entries')
} finally { $verify.Dispose() }
Move-Item -LiteralPath $pendingArchive -Destination $archivePath
Write-Output ('MIT source archive: ' + $archivePath)
