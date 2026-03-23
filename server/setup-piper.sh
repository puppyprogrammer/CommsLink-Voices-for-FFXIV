#!/bin/bash
# FFXIVoices - Download Piper TTS and voice models
# Run from the server/ directory

set -e

PIPER_VERSION="2023.11.14-2"
PIPER_URL="https://github.com/rhasspy/piper/releases/download/${PIPER_VERSION}/piper_windows_amd64.zip"
VOICES_BASE="https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US"

echo "=== FFXIVoices Piper Setup ==="

# Download Piper binary
if [ ! -f "piper/piper.exe" ]; then
    echo "[1/3] Downloading Piper..."
    curl -L -o piper.zip "$PIPER_URL"
    mkdir -p piper
    unzip -o piper.zip -d piper/
    rm piper.zip
    echo "      Piper installed to ./piper/"
else
    echo "[1/3] Piper already installed"
fi

# Download male voice (Ryan)
mkdir -p voices
if [ ! -f "voices/en_US-ryan-medium.onnx" ]; then
    echo "[2/3] Downloading male voice (Ryan)..."
    curl -L -o voices/en_US-ryan-medium.onnx \
        "${VOICES_BASE}/ryan/medium/en_US-ryan-medium.onnx"
    curl -L -o voices/en_US-ryan-medium.onnx.json \
        "${VOICES_BASE}/ryan/medium/en_US-ryan-medium.onnx.json"
else
    echo "[2/3] Male voice already downloaded"
fi

# Download female voice (Amy)
if [ ! -f "voices/en_US-amy-medium.onnx" ]; then
    echo "[3/3] Downloading female voice (Amy)..."
    curl -L -o voices/en_US-amy-medium.onnx \
        "${VOICES_BASE}/amy/medium/en_US-amy-medium.onnx"
    curl -L -o voices/en_US-amy-medium.onnx.json \
        "${VOICES_BASE}/amy/medium/en_US-amy-medium.onnx.json"
else
    echo "[3/3] Female voice already downloaded"
fi

echo ""
echo "=== Setup complete ==="
echo "Set PIPER_PATH env var: export PIPER_PATH=$(pwd)/piper/piper.exe"
echo "Then run: node index.js"
