$ErrorActionPreference = 'Stop'
$appPath = Join-Path $PSScriptRoot 'artifacts\VideoSync\VideoSync.exe'
if (!(Test-Path -LiteralPath $appPath)) { & (Join-Path $PSScriptRoot 'scripts\Build.ps1') -Publish }
Start-Process -FilePath $appPath -WorkingDirectory (Split-Path -Parent $appPath) -WindowStyle Normal
