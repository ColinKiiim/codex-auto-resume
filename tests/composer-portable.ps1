param([string]$ExecutablePath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\CodexAutoResume.exe'))
$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $ExecutablePath).Path
$cases = @(
    @{ Mode = '--composer-worker'; Request = '{"Window":0,"ProcessId":0,"Operation":"inspect","ExpiresUtc":"/Date(0)/"}' },
    @{ Mode = '--scope-worker'; Request = '{"Target":{"Window":0,"ProcessId":0,"ExpiresUtc":"/Date(0)/"},"Operation":"read"}' }
)
foreach ($case in $cases) {
    $worker = New-Object Diagnostics.Process
    $worker.StartInfo = New-Object Diagnostics.ProcessStartInfo
    $worker.StartInfo.FileName = $resolved
    $worker.StartInfo.Arguments = $case.Mode
    $worker.StartInfo.UseShellExecute = $false
    $worker.StartInfo.CreateNoWindow = $true
    $worker.StartInfo.RedirectStandardInput = $true
    $worker.StartInfo.RedirectStandardOutput = $true
    $worker.StartInfo.RedirectStandardError = $true
    try {
        $null = $worker.Start()
        # Expired requests reject before querying any user's window or editor.
        $worker.StandardInput.WriteLine($case.Request)
        $worker.StandardInput.Close()
        $output = $worker.StandardOutput.ReadToEndAsync()
        $errors = $worker.StandardError.ReadToEndAsync()
        if (-not $worker.WaitForExit(10000)) { throw ('Portable worker exceeded its deadline: ' + $case.Mode) }
        $reply = $output.GetAwaiter().GetResult() | ConvertFrom-Json
        if ($worker.ExitCode -ne 0 -or $null -eq $reply -or $reply.Result -ne 'Blocked' -or $errors.GetAwaiter().GetResult().Length -ne 0) { throw ('GUI executable failed its redirected worker protocol: ' + $case.Mode) }
        if ($case.Mode -eq '--composer-worker' -and $reply.Stage -notlike 'open:*') { throw 'Composer request did not reject at the open boundary' }
        Write-Output ('PASS: production GUI ' + $case.Mode + ' returns a bounded blocked response through redirected pipes, without a console or target actions')
    } finally {
        if ($worker.Id -and -not $worker.HasExited) { $worker.Kill(); $worker.WaitForExit(2000) | Out-Null }
        $worker.Dispose()
    }
}
