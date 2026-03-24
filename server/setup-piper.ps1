# FFXIVoices - Download Piper TTS and voice models (PowerShell)
# Run from the server/ directory

$ErrorActionPreference = "Stop"

$PiperVersion = "2023.11.14-2"
$PiperUrl = "https://github.com/rhasspy/piper/releases/download/$PiperVersion/piper_windows_amd64.zip"
$VoicesBase = "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US"

Write-Host "=== FFXIVoices Piper Setup ===" -ForegroundColor Cyan

# Download Piper binary
if (-not (Test-Path "piper/piper.exe")) {
    Write-Host "[1/3] Downloading Piper..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri $PiperUrl -OutFile "piper.zip"
    New-Item -ItemType Directory -Force -Path "piper" | Out-Null
    Expand-Archive -Path "piper.zip" -DestinationPath "piper" -Force
    Remove-Item "piper.zip"
    Write-Host "      Piper installed to ./piper/" -ForegroundColor Green
} else {
    Write-Host "[1/3] Piper already installed" -ForegroundColor Green
}

# Create voices dir
New-Item -ItemType Directory -Force -Path "voices" | Out-Null

# Download male voice (Ryan)
if (-not (Test-Path "voices/en_US-ryan-medium.onnx")) {
    Write-Host "[2/3] Downloading male voice (Ryan)..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "$VoicesBase/ryan/medium/en_US-ryan-medium.onnx" -OutFile "voices/en_US-ryan-medium.onnx"
    Invoke-WebRequest -Uri "$VoicesBase/ryan/medium/en_US-ryan-medium.onnx.json" -OutFile "voices/en_US-ryan-medium.onnx.json"
} else {
    Write-Host "[2/3] Male voice already downloaded" -ForegroundColor Green
}

# Download female voice (Amy)
if (-not (Test-Path "voices/en_US-amy-medium.onnx")) {
    Write-Host "[3/3] Downloading female voice (Amy)..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "$VoicesBase/amy/medium/en_US-amy-medium.onnx" -OutFile "voices/en_US-amy-medium.onnx"
    Invoke-WebRequest -Uri "$VoicesBase/amy/medium/en_US-amy-medium.onnx.json" -OutFile "voices/en_US-amy-medium.onnx.json"
} else {
    Write-Host "[3/3] Female voice already downloaded" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== Setup complete ===" -ForegroundColor Cyan
Write-Host "Set env var:  `$env:PIPER_PATH = `"$(Resolve-Path 'piper/piper.exe')`"" -ForegroundColor White
Write-Host "Then run:     node index.js" -ForegroundColor White
