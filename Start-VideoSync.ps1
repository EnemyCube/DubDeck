$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot "src\VideoSync.App\VideoSync.App.csproj")
$appPath = Join-Path $PSScriptRoot ("artifacts\VideoSync-" + $project.Project.PropertyGroup.Version + "\VideoSync.exe")
if (!(Test-Path -LiteralPath $appPath)) { & (Join-Path $PSScriptRoot 'scripts\Build.ps1') -Publish }
Start-Process -FilePath $appPath -WorkingDirectory (Split-Path -Parent $appPath) -WindowStyle Normal
