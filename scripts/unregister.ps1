# Removes DrewPlayer's file associations (current user only).
$exe = Join-Path $env:LOCALAPPDATA 'Programs\DrewPlayer\DrewPlayer.exe'
if (-not (Test-Path $exe)) { $exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\DrewPlayer.exe' }
Start-Process $exe -ArgumentList '--unregister' -Wait
Write-Host "File associations removed." -ForegroundColor Green
