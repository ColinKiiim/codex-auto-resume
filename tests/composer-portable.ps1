param([string]$ExecutablePath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\CodexAutoResume.exe'))
$ErrorActionPreference = 'Stop'
$worker = New-Object Diagnostics.Process
$worker.StartInfo = New-Object Diagnostics.ProcessStartInfo
$worker.StartInfo.FileName = (Resolve-Path -LiteralPath $ExecutablePath).Path
$worker.StartInfo.Arguments = '--composer-worker'
$worker.StartInfo.UseShellExecute = $false
$worker.StartInfo.CreateNoWindow = $true
$worker.StartInfo.RedirectStandardInput = $true
$worker.StartInfo.RedirectStandardOutput = $true
$worker.StartInfo.RedirectStandardError = $true
try {
    $null = $worker.Start()
    # Expired request: reject before querying any user's window or editor.
    $worker.StandardInput.WriteLine('{"Window":0,"ProcessId":0,"Operation":"inspect","ExpiresUtc":"/Date(0)/"}')
    $worker.StandardInput.Close()
    $output = $worker.StandardOutput.ReadToEndAsync()
    $errors = $worker.StandardError.ReadToEndAsync()
    if (-not $worker.WaitForExit(10000)) { throw 'Portable composer worker exceeded its deadline' }
    $reply = $output.GetAwaiter().GetResult() | ConvertFrom-Json
    if ($worker.ExitCode -ne 0 -or $null -eq $reply -or $reply.Result -ne 'Blocked' -or $reply.Stage -notlike 'open:*' -or $errors.GetAwaiter().GetResult().Length -ne 0) { throw 'GUI executable failed its redirected composer protocol' }
    Write-Output 'PASS: production GUI executable returns a bounded blocked response through redirected pipes, without a console or target actions'
} finally {
    if ($worker.Id -and -not $worker.HasExited) { $worker.Kill(); $worker.WaitForExit(2000) | Out-Null }
    $worker.Dispose()
}
