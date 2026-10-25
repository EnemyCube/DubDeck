param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$Publish,
    [switch]$Portable,
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
    & $taskDotnet restore DubDeck.slnx --disable-parallel
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & $taskDotnet build DubDeck.slnx --no-restore -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $taskDotnet run --project tests/DubDeck.Tests --no-build -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    if ($MediaTests -or $Soak) {
        & (Join-Path $PSScriptRoot 'Generate-Fixtures.ps1') -IncludeSoak:$Soak
        $testCache = Join-Path $projectRoot ('tests\DubDeck.MediaTests\bin\' + $Configuration + '\net10.0-windows\win-x64\libvlc\win-x64\plugins\plugins.dat')
        if (!(Test-Path -LiteralPath $testCache)) {
            & $taskDotnet run --project tests/DubDeck.MediaTests --no-build -c $Configuration -- --initialize-vlc-cache
            if ($LASTEXITCODE -ne 0) { throw 'Media test cache setup failed.' }
        }
        $mediaArgs = @('run','--project','tests/DubDeck.MediaTests','--no-build','-c',$Configuration,'--','--fixtures',(Join-Path $projectRoot 'artifacts\media-fixtures'))
        if ($Soak) { $mediaArgs += '--soak' }
        & $taskDotnet @mediaArgs
        if ($LASTEXITCODE -ne 0) { throw 'Media tests failed.' }
    }
    if ($Publish -or $Portable) {
        [xml]$applicationProject = Get-Content -LiteralPath (Join-Path $projectRoot "src\DubDeck.App\DubDeck.App.csproj")
        $releaseName = "DubDeck-" + $applicationProject.Project.PropertyGroup.Version
        $applicationFileName = 'DubDeck.exe'
        if ($Portable) {
            $applicationFileName = $releaseName + '-win.x64-portable.exe'
            $releaseName += '-Portable'
            & $taskDotnet publish src/DubDeck.App -c $Configuration -p:PublishProfile=Portable ("-p:PublishDir=../../artifacts/" + $releaseName + "/")
        }
        else {
            & $taskDotnet publish src/DubDeck.App -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=false ("-p:PublishDir=../../artifacts/" + $releaseName + "/")
        }
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        $publishFolder = Join-Path $projectRoot ("artifacts\" + $releaseName)
        if ($Portable) {
            $publishedFiles = @(Get-ChildItem -LiteralPath $publishFolder -Force)
            $rawExecutable = Join-Path $publishFolder 'DubDeck.exe'
            $unexpectedFiles = @($publishedFiles | Where-Object { $_.PSIsContainer -or $_.Name -notin @('DubDeck.exe', $applicationFileName) })
            if ($unexpectedFiles.Count -ne 0 -or !(Test-Path -LiteralPath $rawExecutable -PathType Leaf)) {
                throw 'Unexpected portable output. Use a clean portable output directory.'
            }
            # Label the distributable without changing the application's assembly name.
            Move-Item -LiteralPath $rawExecutable -Destination (Join-Path $publishFolder $applicationFileName) -Force
        }
        else {
            Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $publishFolder 'README.md')
            Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $publishFolder 'LICENSE')
            Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $publishFolder 'THIRD-PARTY-NOTICES.md')
            $imageDestination = Join-Path $publishFolder 'docs\images'
            New-Item -ItemType Directory -Force -Path $imageDestination | Out-Null
            Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs\images') -File | Copy-Item -Destination $imageDestination -Force
            $licenseDestination = Join-Path $publishFolder 'licenses'
            New-Item -ItemType Directory -Force -Path $licenseDestination | Out-Null
            Get-ChildItem -LiteralPath (Join-Path $projectRoot 'licenses') -File | Copy-Item -Destination $licenseDestination -Force
        }
        $applicationPath = Join-Path $publishFolder $applicationFileName
        $cacheProcess = Start-Process -FilePath $applicationPath -ArgumentList '--initialize-vlc-cache' -WindowStyle Hidden -PassThru -Wait
        if ($cacheProcess.ExitCode -ne 0) { throw 'VLC cache initialization failed.' }
        if ($Portable) { Write-Output ('Portable size: ' + [math]::Round((Get-Item -LiteralPath $applicationPath).Length / 1MB, 1) + ' MiB') }
        Write-Output ('Ready: ' + $applicationPath)
    }
}
finally { Pop-Location }
