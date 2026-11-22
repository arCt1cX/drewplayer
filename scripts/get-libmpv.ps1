# Downloads libmpv (the mpv engine) into lib\libmpv-2.dll.
# Source: the "mpv-dev-x86_64" builds by shinchiro (https://github.com/shinchiro/mpv-winbuild-cmake).
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\get-libmpv.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$lib = Join-Path $root 'lib'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("drewplayer-libmpv-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $lib, $tmp | Out-Null

try {
    Write-Host "Looking up the latest libmpv build..." -ForegroundColor Cyan
    $release = Invoke-RestMethod 'https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/latest' -Headers @{ 'User-Agent' = 'drewplayer-setup' }
    # plain x86_64 build (the -v3 variant needs a CPU with AVX2)
    $asset = $release.assets | Where-Object { $_.name -match '^mpv-dev-x86_64-\d' } | Select-Object -First 1
    if (-not $asset) { throw "No mpv-dev-x86_64 asset found in release $($release.tag_name)" }

    Write-Host ("Downloading {0} ({1:N0} MB)..." -f $asset.name, ($asset.size / 1MB)) -ForegroundColor Cyan
    $archive = Join-Path $tmp $asset.name
    Invoke-WebRequest $asset.browser_download_url -OutFile $archive -UseBasicParsing

    # Windows 10/11 ship bsdtar, which can extract .7z archives
    & "$env:SystemRoot\System32\tar.exe" -xf $archive -C $tmp
    $dll = Get-ChildItem $tmp -Recurse -Filter 'libmpv-2.dll' | Select-Object -First 1
    if (-not $dll) { throw "libmpv-2.dll not found in the archive" }
    Copy-Item $dll.FullName (Join-Path $lib 'libmpv-2.dll') -Force
    Write-Host "Done: lib\libmpv-2.dll ($($release.tag_name))" -ForegroundColor Green
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
