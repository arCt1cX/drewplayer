# Registers the installed DrewPlayer for video, audio and photos (current user only, no admin)
# and opens Settings > Default apps. Same as the button in the app's Settings > System page.
$exe = Join-Path $env:LOCALAPPDATA 'Programs\DrewPlayer\DrewPlayer.exe'
if (-not (Test-Path $exe)) { throw "DrewPlayer is not installed: run scripts\install.ps1 first" }
Start-Process $exe -ArgumentList '--register' -Wait
Write-Host "Registered. In Windows Settings, pick DrewPlayer as the default for the file types you want." -ForegroundColor Green
