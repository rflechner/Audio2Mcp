param([switch]$SkipModel)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    git submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) { throw 'Initialisation du sous-module impossible.' }
    cmake -S external/whisper.cpp -B artifacts/whisper-build -DGGML_CUDA=OFF -DWHISPER_BUILD_TESTS=OFF -DWHISPER_BUILD_EXAMPLES=ON -DBUILD_SHARED_LIBS=OFF
    if ($LASTEXITCODE -ne 0) { throw 'Configuration CMake impossible.' }
    cmake --build artifacts/whisper-build --config Release --target whisper-cli -j 4
    if ($LASTEXITCODE -ne 0) { throw 'Compilation whisper.cpp impossible.' }
    New-Item -ItemType Directory -Force tools/whisper, models | Out-Null
    Copy-Item -LiteralPath artifacts/whisper-build/bin/Release/whisper-cli.exe -Destination tools/whisper/whisper-cli.exe
    if (-not $SkipModel -and -not (Test-Path models/ggml-base.bin)) {
        Invoke-WebRequest 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin' -OutFile models/ggml-base.bin.partial
        $expectedHash = '60ED5BC3DD14EEA856493D334349B405782DDCAF0028D4B5DF4088345FBA2EFE'
        if ((Get-FileHash models/ggml-base.bin.partial -Algorithm SHA256).Hash -ne $expectedHash) {
            throw 'SHA256 du modèle inattendu : téléchargement laissé en .partial, ne pas utiliser sans vérification.'
        }
        Move-Item -LiteralPath models/ggml-base.bin.partial -Destination models/ggml-base.bin
    }
    Write-Host 'Moteur : tools/whisper/whisper-cli.exe ; modèle : models/ggml-base.bin'
    if (Test-Path models/ggml-base.bin) { Get-FileHash models/ggml-base.bin -Algorithm SHA256 }
}
finally { Pop-Location }
