# Builds the chart editor and zips it for download: artifacts/InvectorChartEditor.zip, also copied to
# server/public/ where the marketplace Worker serves it as a static file (/InvectorChartEditor.zip).
# Nothing of the game is inside: the editor reads the game's look from the player's own install.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $PSScriptRoot 'ChartEditor.csproj'
$output = Join-Path $PSScriptRoot 'bin\Release\net48'

dotnet build $project -c Release -nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$stage = Join-Path ([System.IO.Path]::GetTempPath()) ('chart-editor-' + [guid]::NewGuid())
$folder = Join-Path $stage 'InvectorChartEditor'
New-Item -ItemType Directory -Force $folder | Out-Null
try {
    # Everything the exe needs, without debug symbols and import libraries.
    robocopy $output $folder /E /XF *.pdb *.lib *.xml /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "copy failed ($LASTEXITCODE)" }
    $readme = @'
Invector Chart Editor (InvectorModKit)

Windows 10 or 11, 64-bit, with Microsoft Edge WebView2 (already on most PCs) and .NET Framework 4.8.
Unzip anywhere, then run InvectorChartEditor.exe. With InvectorModKit installed in the game, the editor
finds your custom songs, sends charts to the game (they reload by themselves) and takes the game's look.

Windows 10 ou 11, 64 bits, avec Microsoft Edge WebView2 (déjà présent sur la plupart des PC) et .NET Framework 4.8.
Décompresse où tu veux, puis lance InvectorChartEditor.exe. Avec InvectorModKit installé dans le jeu, l'éditeur
trouve tes morceaux perso, envoie les charts dans le jeu (ils se rechargent tout seuls) et prend le look du jeu.
'@
    Set-Content -Path (Join-Path $folder 'README.txt') -Value $readme -Encoding UTF8

    $artifacts = Join-Path $root 'artifacts'
    New-Item -ItemType Directory -Force $artifacts | Out-Null
    $zip = Join-Path $artifacts 'InvectorChartEditor.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path $folder -DestinationPath $zip -CompressionLevel Optimal

    $public = Join-Path $root 'server\public'
    New-Item -ItemType Directory -Force $public | Out-Null
    Copy-Item $zip (Join-Path $public 'InvectorChartEditor.zip') -Force
    $mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "Saved $zip ($mb MB), copied to server/public"
    $global:LASTEXITCODE = 0 # robocopy leaves 1 ("files copied")
}
finally { Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue }
