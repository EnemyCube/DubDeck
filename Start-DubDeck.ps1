$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot "src\DubDeck.App\DubDeck.App.csproj")
$appPath = Join-Path $PSScriptRoot ("artifacts\DubDeck-" + $project.Project.PropertyGroup.Version + "\DubDeck.exe")
if (!(Test-Path -LiteralPath $appPath)) { & (Join-Path $PSScriptRoot 'scripts\Build.ps1') -Publish }
Start-Process -FilePath $appPath -WorkingDirectory (Split-Path -Parent $appPath) -WindowStyle Normal
