function Join-NativeArguments([string[]]$Arguments) {
    (($Arguments | ForEach-Object {
        $value = [string]$_
        if ($value -match '[\s"]') { '"' + $value.Replace('"', '\"') + '"' } else { $value }
    }) -join ' ')
}

function Invoke-HermesNativeBounded {
    param(
        [string]$FileName,
        [string[]]$Arguments,
        [int]$TimeoutSeconds,
        [string]$Stage
    )
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $FileName
    $start.Arguments = Join-NativeArguments $Arguments
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    if (-not $process.Start()) { throw "$Stage did not start." }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timedOut = $false
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $timedOut = $true
        try { $process.Kill($true) } catch { try { $process.Kill() } catch { } }
        $process.WaitForExit()
    }
    [ordered]@{
        stage = $Stage
        timed_out = $timedOut
        exit_code = if ($timedOut) { -1 } else { $process.ExitCode }
        stdout = $stdoutTask.Result
        stderr = $stderrTask.Result
        output = @(($stdoutTask.Result, $stderrTask.Result) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        message = if ($timedOut) { "$Stage timed out after $TimeoutSeconds seconds." } else { (($stdoutTask.Result, $stderrTask.Result) -join ' ').Trim() }
    }
}
