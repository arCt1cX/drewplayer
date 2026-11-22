# Builds DrewPlayer (Release, ReadyToRun) and installs it to %LOCALAPPDATA%\Programs\DrewPlayer
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\install.ps1
# Then:   powershell -ExecutionPolicy Bypass -File scripts\register.ps1   (to make it the default player)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\DrewPlayer\DrewPlayer.csproj'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\DrewPlayer'
$dist = Join-Path $root 'dist'

if (-not (Test-Path (Join-Path $root 'lib\libmpv-2.dll'))) {
    throw "lib\libmpv-2.dll is missing. Run scripts\get-libmpv.ps1 first."
}

Get-Process DrewPlayer -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Building..." -ForegroundColor Cyan
dotnet publish $project -c Release -r win-x64 --self-contained false `
    -p:PublishReadyToRun=true -p:DebugType=none -o $dist -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Write-Host "Installing to $dest" -ForegroundColor Cyan
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $dist '*') $dest -Recurse -Force

# Start menu shortcut
$lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'DrewPlayer.lnk'
$sh = New-Object -ComObject WScript.Shell
$s = $sh.CreateShortcut($lnk)
$s.TargetPath = Join-Path $dest 'DrewPlayer.exe'
$s.WorkingDirectory = $dest
$s.IconLocation = (Join-Path $dest 'DrewPlayer.exe') + ',0'
$s.Description = 'DrewPlayer'
$s.Save()

# if already registered: refresh file icons and associations
Start-Process (Join-Path $dest 'DrewPlayer.exe') -ArgumentList '--sync-icons' -Wait

Write-Host "Done. DrewPlayer is in the Start menu." -ForegroundColor Green
Write-Host "To make it the default player: scripts\register.ps1, or Settings > System inside the app." -ForegroundColor Green
