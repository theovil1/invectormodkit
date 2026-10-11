# Downloads BepInEx 5.4.23.5 (win x64), the mod loader the installer ships, into ./third_party/bepinex (not committed).
# BepInEx is LGPL-2.1: https://github.com/BepInEx/BepInEx
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'third_party\bepinex'
if (Test-Path (Join-Path $target 'BepInEx\core\BepInEx.dll')) { Write-Host "Already present: $target"; return }

$url = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip'
$zip = Join-Path ([System.IO.Path]::GetTempPath()) ('bepinex-' + [guid]::NewGuid() + '.zip')
try {
    Invoke-WebRequest $url -OutFile $zip
    New-Item -ItemType Directory -Force $target | Out-Null
    Expand-Archive $zip -DestinationPath $target -Force
    Write-Host "Saved $target"
}
finally { Remove-Item $zip -Force -ErrorAction SilentlyContinue }
