param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$Publish,
    [switch]$MediaTests,
    [switch]$Soak
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$taskDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $taskDotnet)) {
    New-Item -ItemType Directory -Force -Path (Join-Path $projectRoot '.tools') | Out-Null
    $installScript = Join-Path $projectRoot '.tools\dotnet-install.ps1'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installScript
    & $installScript -Version '10.0.402' -InstallDir (Join-Path $projectRoot '.tools\dotnet') -NoPath
    if ($LASTEXITCODE -ne 0) { throw 'SDK installation failed.' }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Push-Location $projectRoot
try {
    & $taskDotnet restore VideoSync.slnx --disable-parallel
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & $taskDotnet build VideoSync.slnx --no-restore -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $taskDotnet run --project tests/VideoSync.Tests --no-build -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    if ($MediaTests -or $Soak) {
        & (Join-Path $PSScriptRoot 'Generate-Fixtures.ps1') -IncludeSoak:$Soak
        $testCache = Join-Path $projectRoot ('tests\VideoSync.MediaTests\bin\' + $Configuration + '\net10.0-windows\win-x64\libvlc\win-x64\plugins\plugins.dat')
        if (!(Test-Path -LiteralPath $testCache)) {
            & $taskDotnet run --project tests/VideoSync.MediaTests --no-build -c $Configuration -- --initialize-vlc-cache
            if ($LASTEXITCODE -ne 0) { throw 'Media test cache setup failed.' }
        }
        $mediaArgs = @('run','--project','tests/VideoSync.MediaTests','--no-build','-c',$Configuration,'--','--fixtures',(Join-Path $projectRoot 'artifacts\media-fixtures'))
        if ($Soak) { $mediaArgs += '--soak' }
        & $taskDotnet @mediaArgs
        if ($LASTEXITCODE -ne 0) { throw 'Media tests failed.' }
    }
    if ($Publish) {
        [xml]$applicationProject = Get-Content -LiteralPath (Join-Path $projectRoot "src\VideoSync.App\VideoSync.App.csproj")
        $releaseName = "VideoSync-" + $applicationProject.Project.PropertyGroup.Version
        & $taskDotnet publish src/VideoSync.App -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=false ("-p:PublishDir=../../artifacts/" + $releaseName + "/")
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        $publishFolder = Join-Path $projectRoot ("artifacts\" + $releaseName)
        Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $publishFolder 'README.md')
        Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $publishFolder 'THIRD-PARTY-NOTICES.md')
        $licenseDestination = Join-Path $publishFolder 'licenses'
        New-Item -ItemType Directory -Force -Path $licenseDestination | Out-Null
        Get-ChildItem -LiteralPath (Join-Path $projectRoot 'licenses') -File | Copy-Item -Destination $licenseDestination -Force
        $cacheProcess = Start-Process -FilePath (Join-Path $publishFolder 'VideoSync.exe') -ArgumentList '--initialize-vlc-cache' -WindowStyle Hidden -PassThru -Wait
        if ($cacheProcess.ExitCode -ne 0) { throw 'VLC cache initialization failed.' }
        Write-Output ('Ready: ' + (Join-Path $publishFolder 'VideoSync.exe'))
    }
}
finally { Pop-Location }
