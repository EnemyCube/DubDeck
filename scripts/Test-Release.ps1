param(
    [Parameter(Mandatory)][string]$ApplicationPath,
    [ValidateSet('workflow','performance')][string]$Mode = 'workflow',
    [Parameter(Mandatory)][string]$ReportName,
    [string]$FixtureDirectory,
    [string]$ReportDirectory,
    [int]$TimeoutSeconds = 360
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content -LiteralPath (Join-Path $root 'src/DubDeck.App/DubDeck.App.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$reportRoot = if ($ReportDirectory) { [IO.Path]::GetFullPath($ReportDirectory) } else { Join-Path $root ('artifacts/release-' + $version + '-validation') }
if ([IO.Path]::GetFileName($ReportName) -ne $ReportName -or $ReportName -in '.', '..') { throw 'ReportName must be a single folder name.' }
$report = Join-Path $reportRoot $ReportName
if (Test-Path -LiteralPath $report) { throw 'Use a new report name for each run.' }
New-Item -ItemType Directory -Path $report -Force | Out-Null
$fixtures = if ($FixtureDirectory) { [IO.Path]::GetFullPath($FixtureDirectory) } else { Join-Path $root 'artifacts/media-fixtures' }
if (!(Test-Path -LiteralPath (Join-Path $fixtures 'video.mp4'))) { throw 'Generate fixtures with scripts/Generate-Fixtures.ps1 before running desktop checks.' }
$testFlag = if ($Mode -eq 'performance') { '--performance-test' } else { '--smoke-test' }
$launchArguments = @($testFlag, ('"' + $report + '"'), '--fixtures', ('"' + $fixtures + '"'))
if ($Mode -eq 'performance') { $launchArguments += '--simulate-video-stall' }
$testProcess = Start-Process -FilePath $ApplicationPath -ArgumentList $launchArguments -WorkingDirectory (Split-Path -Parent $ApplicationPath) -WindowStyle Hidden -PassThru
$null = $testProcess.Handle
Write-Output ("Running $ReportName, PID " + $testProcess.Id)
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while (!$testProcess.WaitForExit(10000)) {
    if ([DateTime]::UtcNow -gt $deadline) { Stop-Process -Id $testProcess.Id -ErrorAction SilentlyContinue; throw "$ReportName exceeded $TimeoutSeconds seconds." }
}
if (Test-Path -LiteralPath (Join-Path $report 'failure.txt')) { throw (Get-Content -LiteralPath (Join-Path $report 'failure.txt') -Raw) }
if ($testProcess.ExitCode -ne 0) { throw "$ReportName exited with $($testProcess.ExitCode)." }
$result = Get-Content -LiteralPath (Join-Path $report 'result.json') -Raw | ConvertFrom-Json
$shutdown = Get-Content -LiteralPath (Join-Path $report 'shutdown.json') -Raw | ConvertFrom-Json
if (!$result.success -or $shutdown.timedOut -or $shutdown.cleanupFailed) { throw "$ReportName failed or required shutdown fallback." }
if ($Mode -eq 'performance' -and (!$result.closesDuringPlayback -or $shutdown.closeRequests -lt 2)) { throw 'Performance test missed repeated close requests during playback.' }
[pscustomobject]@{ name=$ReportName; checks=$result.checks.Count; shutdownMs=$shutdown.elapsedMs; report=$report } | ConvertTo-Json
