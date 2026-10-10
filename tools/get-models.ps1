# Downloads the vocal separation model used by Custom Songs into ./models (not committed: 20 MB).
# Spleeter 2-stems (Deezer, MIT licence), ONNX export by the sherpa-onnx project (Apache-2.0).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$models = Join-Path $root 'models'
$target = Join-Path $models 'vocals.fp16.onnx'
if (Test-Path $target) { Write-Host "Already present: $target"; return }

$url = 'https://github.com/k2-fsa/sherpa-onnx/releases/download/source-separation-models/sherpa-onnx-spleeter-2stems-fp16.tar.bz2'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('spleeter-' + [guid]::NewGuid())
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    Invoke-WebRequest $url -OutFile (Join-Path $tmp 'model.tar.bz2')
    tar -xjf (Join-Path $tmp 'model.tar.bz2') -C $tmp
    New-Item -ItemType Directory -Force $models | Out-Null
    Copy-Item (Join-Path $tmp 'sherpa-onnx-spleeter-2stems-fp16\vocals.fp16.onnx') $target
    Write-Host "Saved $target"
}
finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
