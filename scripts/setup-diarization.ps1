param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent

# Downloads are pinned by content hash. Existing unexpected files are never overwritten.
function Get-VerifiedModelFile {
    param([string]$Url, [string]$Path, [string]$Sha256)

    if (-not (Test-Path -LiteralPath $Path)) {
        $partialPath = "$Path.partial"
        Invoke-WebRequest $Url -OutFile $partialPath
        if ((Get-FileHash -LiteralPath $partialPath -Algorithm SHA256).Hash -ne $Sha256) {
            throw "SHA256 inattendu : $partialPath"
        }

        Move-Item -LiteralPath $partialPath -Destination $Path
    }

    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Sha256) {
        throw "Le fichier existant ne correspond pas au modèle attendu : $Path"
    }
}

Push-Location $repoRoot
try {
    New-Item -ItemType Directory -Force models/diarization | Out-Null
    Get-VerifiedModelFile `
        -Url 'https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/sherpa-onnx-pyannote-segmentation-3-0.tar.bz2' `
        -Path 'models/diarization/segmentation.tar.bz2' `
        -Sha256 '24615EE884C897D9D2BA09BB4D30DA6BB1B15E685065962DB5B02E76E4996488'

    $segmentationPath = 'models/diarization/sherpa-onnx-pyannote-segmentation-3-0/model.onnx'
    if (-not (Test-Path -LiteralPath $segmentationPath)) {
        tar -xf models/diarization/segmentation.tar.bz2 -C models/diarization
        if ($LASTEXITCODE -ne 0) {
            throw 'Extraction du modèle de segmentation impossible.'
        }
    }

    if ((Get-FileHash -LiteralPath $segmentationPath -Algorithm SHA256).Hash -ne '220AD67CA923BEF2FA91F2390C786097BF305BCEB5E261D4AF67B38E938E1079') {
        throw 'SHA256 inattendu pour le modèle de segmentation.'
    }

    Get-VerifiedModelFile `
        -Url 'https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/nemo_en_titanet_small.onnx' `
        -Path 'models/diarization/nemo_en_titanet_small.onnx' `
        -Sha256 'AD4A1802485D8B34C722D2A9D04249662F2ECE5D28A7A039063CA22F515A789E'

    Write-Host 'Modèles de diarisation prêts dans models/diarization/.'
}
finally {
    Pop-Location
}
