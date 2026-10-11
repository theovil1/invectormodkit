# Builds a version of the mod, ready to publish (this script never publishes):
#   artifacts/InvectorModKitSetup.exe        the installer players download (everything inside)
#   artifacts/InvectorModKit-<version>.zip   the same files for a manual install
#   artifacts/InvectorLevelEditor.zip        the level editor on its own
# The plugins are compiled against the game installed on this computer (its DLLs are not redistributable).
param([Parameter(Mandatory = $true)][string] $Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# 1. the version asked for must be the code's (ModKit.Version, which every plugin reports)
$modKit = Join-Path $root 'src\InvectorModKit\Api\ModKit.cs'
$code = (Select-String -Path $modKit -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
if ($code -ne $Version) { throw "ModKit.Version is $code, not $Version" }

# 2. what the release ships besides our code
& (Join-Path $PSScriptRoot 'get-bepinex.ps1')
& (Join-Path $PSScriptRoot 'get-models.ps1')

$artifacts = Join-Path $root 'artifacts'
$stage = Join-Path $artifacts 'stage'
$payload = Join-Path $stage 'payload'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$plugins = Join-Path $payload 'BepInEx\plugins'
New-Item -ItemType Directory -Force $plugins | Out-Null

function Build([string] $project, [string[]] $extra = @()) {
    dotnet build (Join-Path $root $project) -c Release -nologo -v quiet @extra | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "build failed: $project" }
}

# 3. the plugins, their deploy step pointed at the payload instead of the game
foreach ($project in 'src\InvectorModKit\InvectorModKit.csproj', 'src\InvectorModKit.CustomSongs\InvectorModKit.CustomSongs.csproj',
                     'src\InvectorModKit.Marketplace\InvectorModKit.Marketplace.csproj', 'src\InvectorModKit.ChartGen\InvectorModKit.ChartGen.csproj') {
    Build $project @("-p:PluginsDir=$plugins")
}

# 4. BepInEx and its loader (BepInEx writes its own config at first launch)
Copy-Item (Join-Path $root 'third_party\bepinex\*') $payload -Recurse -Force
Get-ChildItem $payload -Recurse -Force -Include *.pdb, *.xml | Remove-Item -Force
$ours = Join-Path $plugins 'InvectorModKit'
Copy-Item (Join-Path $PSScriptRoot 'release\THIRD-PARTY-NOTICES.txt') $ours
Copy-Item (Join-Path $root 'LICENSE') $ours -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.IO.Compression.FileSystem
$payloadZip = Join-Path $stage 'payload.zip'
[System.IO.Compression.ZipFile]::CreateFromDirectory($payload, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

# 5. the level editor (its own zip, also carried by the installer)
& (Join-Path $root 'tools\ChartEditor\pack.ps1')
Copy-Item (Join-Path $artifacts 'InvectorLevelEditor.zip') (Join-Path $stage 'editor.zip')

# 6. the installer, with both inside
Build 'src\InvectorModKit.Setup\InvectorModKit.Setup.csproj' @("-p:Version=$Version")
Copy-Item (Join-Path $root 'src\InvectorModKit.Setup\bin\Release\net48\InvectorModKitSetup.exe') $artifacts -Force

# 7. the manual zip: the payload and how to install it by hand
Copy-Item (Join-Path $PSScriptRoot 'release\LISEZMOI.txt') (Join-Path $payload 'InvectorModKit-README.txt')
$manual = Join-Path $artifacts "InvectorModKit-$Version.zip"
if (Test-Path $manual) { Remove-Item $manual -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory($payload, $manual, [System.IO.Compression.CompressionLevel]::Optimal, $false)

foreach ($file in 'InvectorModKitSetup.exe', "InvectorModKit-$Version.zip", 'InvectorLevelEditor.zip') {
    $mb = [math]::Round((Get-Item (Join-Path $artifacts $file)).Length / 1MB, 1)
    Write-Host ("{0,-32} {1,6} MB" -f $file, $mb)
}
Write-Host "Version $Version built in $artifacts (not published)."
