# Downloads the audio tools the marketplace installer ships (into ./tools/audiotools, not committed).
# yt-dlp (Unlicence) gets the video's audio track from YouTube; ffmpeg (LGPL-3.0 build by BtbN)
# turns it into the ogg the game plays. Seen by the mod in BepInEx/plugins/InvectorModKit/audiotools.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'tools\audiotools'
New-Item -ItemType Directory -Force $target | Out-Null
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('audiotools-' + [guid]::NewGuid())
try {
    if (-not (Test-Path (Join-Path $target 'yt-dlp.exe'))) {
        Write-Host 'yt-dlp.exe…'
        Invoke-WebRequest 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' -OutFile (Join-Path $target 'yt-dlp.exe')
    } else { Write-Host 'Already present: yt-dlp.exe' }

    if (-not (Test-Path (Join-Path $target 'ffmpeg.exe'))) {
        Write-Host 'ffmpeg.exe…'
        New-Item -ItemType Directory -Force $tmp | Out-Null
        Invoke-WebRequest 'https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-lgpl.zip' -OutFile (Join-Path $tmp 'ffmpeg.zip')
        Expand-Archive (Join-Path $tmp 'ffmpeg.zip') $tmp
        Copy-Item (Join-Path $tmp 'ffmpeg-master-latest-win64-lgpl\bin\ffmpeg.exe') (Join-Path $target 'ffmpeg.exe')
        Copy-Item (Join-Path $tmp 'ffmpeg-master-latest-win64-lgpl\bin\ffprobe.exe') (Join-Path $target 'ffprobe.exe')
    } else { Write-Host 'Already present: ffmpeg.exe' }

    Write-Host "Saved to $target"
}
finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
