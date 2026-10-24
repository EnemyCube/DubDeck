param([int]$Runs = 3, [string]$ApplicationPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content -LiteralPath (Join-Path $root "src\VideoSync.App\VideoSync.App.csproj")
$app = if ($ApplicationPath) { [IO.Path]::GetFullPath($ApplicationPath) } else { Join-Path $root ("artifacts\VideoSync-" + $project.Project.PropertyGroup.Version + "\VideoSync.exe") }
$fixtures = Join-Path $root 'artifacts\media-fixtures'
if (!(Test-Path -LiteralPath $app)) { throw 'Build the application with scripts/Build.ps1 -Publish first.' }
if (!(Test-Path -LiteralPath (Join-Path $fixtures 'pulse-10min.mp4'))) { & (Join-Path $PSScriptRoot 'Generate-Fixtures.ps1') -IncludeSoak }
$output = Join-Path $root ('artifacts\desktop-tests\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $output | Out-Null
$reports = @()
for ($run = 0; $run -le $Runs; $run++) {
    $mode = if ($run -eq 0) { '--smoke-test' } else { '--performance-test' }
    $name = if ($run -eq 0) { 'workflow' } else { 'performance-' + $run }
    $folder = Join-Path $output $name
    $launchArgs = @($mode, ('"' + $folder + '"'), '--fixtures', ('"' + $fixtures + '"'))
    if ($run -eq 1) { $launchArgs += "--simulate-video-stall" }
    $process = Start-Process -FilePath $app -ArgumentList $launchArgs -WorkingDirectory (Split-Path $app) -WindowStyle Normal -PassThru
    Write-Output ("Running {0}, test PID {1}" -f $name, $process.Id)
    if (!$process.WaitForExit(90000)) {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        throw "$name did not exit within 90 seconds."
    }
    $failure = Join-Path $folder 'failure.txt'
    if (Test-Path -LiteralPath $failure) { throw (Get-Content -LiteralPath $failure -Raw -Encoding UTF8) }
    if ($process.ExitCode -ne 0) { throw "$name exited with $($process.ExitCode)." }
    $result = Get-Content -LiteralPath (Join-Path $folder 'result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $shutdown = Get-Content -LiteralPath (Join-Path $folder 'shutdown.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if (!$result.success -or $shutdown.timedOut -or $shutdown.cleanupFailed) { throw "$name used the shutdown fallback or failed." }
    if ($run -gt 0 -and (!$result.closesDuringPlayback -or $shutdown.closeRequests -lt 2)) { throw "$name did not cover repeated close requests during active playback." }
    $reports += [pscustomobject]@{ Name=$name; Result=$result; Shutdown=$shutdown }
    Write-Output ("PASS {0}: {1} checks; normal shutdown {2:N0} ms" -f $name,$result.checks.Count,$shutdown.elapsedMs)
}
$summary = Join-Path $output 'summary.json'
$reports | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summary -Encoding UTF8
Write-Output ("All desktop regressions passed: " + $summary)
