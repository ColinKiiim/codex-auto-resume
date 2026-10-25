$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $projectRoot 'tools/assert-version-policy.ps1') -Version ([IO.File]::ReadAllText((Join-Path $projectRoot 'VERSION')).Trim())
$packageDirectory = Join-Path $projectRoot 'packages'
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
$archivePath = Join-Path $packageDirectory 'CodexAutoResume-1.1.0-source.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$filePaths = @()
foreach ($fileName in @('VERSION', 'version-policy.json', 'AGENTS.md', 'LICENSE', 'NOTICE', 'README.md', '.gitignore', 'build.ps1', 'app.manifest', 'Open-CodexAutoResume.cmd')) { $filePaths += Join-Path $projectRoot $fileName }
foreach ($folder in @('src', 'assets', 'tools', 'docs', 'tests')) {
    foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -Recurse -File)) {
        $include = $folder -ne 'tests' -or $file.Extension -in @('.cs', '.ps1')
        if ($include) { $filePaths += $file.FullName }
    }
}
if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath }
$zip = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($filePath in $filePaths) {
        $relativeName = $filePath.Substring($projectRoot.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $filePath, ('CodexAutoResume/' + $relativeName)) | Out-Null
    }
} finally { $zip.Dispose() }
$verify = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($required in @('version-policy.json', 'LICENSE', 'NOTICE', 'src/DesktopIdentity.cs', 'assets/app.ico', 'build.ps1')) {
        if (-not $verify.GetEntry('CodexAutoResume/' + $required)) { throw ('Source package missing: ' + $required) }
    }
    foreach ($entry in $verify.Entries) {
        if ($entry.FullName -match '/(logs|dist|release|packages)/|last-result|migration-result|portable-result|gui-migration-case') { throw ('Generated or private output in source package: ' + $entry.FullName) }
    }
    Write-Output ('Source package verified: ' + $verify.Entries.Count + ' entries')
} finally { $verify.Dispose() }
Write-Output ('MIT source archive: ' + $archivePath)
