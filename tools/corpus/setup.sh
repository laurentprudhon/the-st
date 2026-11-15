#!/bin/bash
# Set up the OCR pipeline on a fresh machine: llama.cpp (CUDA nightly) + the four GGUF models + a GPU check.
#
#   ./setup.sh                 # install into $HOME/models and $HOME/llama
#   MODELS_DIR=/data/models LLAMA_DIR=/data/llama ./setup.sh
#   LLAMA_BUILD=b12409 CUDA_TAG=cuda-12.8 ./setup.sh
#
# Idempotent: re-running skips what is already there. Nothing is written outside MODELS_DIR/LLAMA_DIR.
set -euo pipefail

MODELS_DIR="${MODELS_DIR:-$HOME/models}"
LLAMA_DIR="${LLAMA_DIR:-$HOME/llama}"
LLAMA_BUILD="${LLAMA_BUILD:-b12409}"
CUDA_TAG="${CUDA_TAG:-cuda-12.8}"          # match the driver's CUDA UMD version; 12.8 works on 13.5
BASE="https://github.com/ggml-org/llama.cpp/releases/download/${LLAMA_BUILD}"

say(){ printf '\n\033[1m== %s\033[0m\n' "$*"; }

say "GPU check"
if ! command -v nvidia-smi >/dev/null; then
  echo "nvidia-smi not found. In WSL2: install the Windows driver, then nvidia-container-toolkit for docker,"
  echo "and run the container with --gpus all. Nothing below will work without it." >&2
  exit 1
fi
nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv,noheader
nvidia-smi | grep -oE 'CUDA Version: [0-9.]+' || true

say "llama.cpp ${LLAMA_BUILD} (${CUDA_TAG})"
mkdir -p "$LLAMA_DIR" "$MODELS_DIR"
if [ ! -x "$LLAMA_DIR/llama-${LLAMA_BUILD}/llama-server" ]; then
  curl -fL -o "$LLAMA_DIR/llama.tar.gz"  "${BASE}/llama-${LLAMA_BUILD}-bin-ubuntu-${CUDA_TAG}-x64.tar.gz"
  curl -fL -o "$LLAMA_DIR/cudart.tar.gz" "${BASE}/cudart-llama-${LLAMA_BUILD}-bin-ubuntu-${CUDA_TAG}-x64.tar.gz"
  tar xzf "$LLAMA_DIR/llama.tar.gz"  -C "$LLAMA_DIR"
  tar xzf "$LLAMA_DIR/cudart.tar.gz" -C "$LLAMA_DIR"
fi
export LD_LIBRARY_PATH="$LLAMA_DIR/cudart-llama-${LLAMA_BUILD}-bin-ubuntu-${CUDA_TAG}-x64:${LD_LIBRARY_PATH:-}"
"$LLAMA_DIR/llama-${LLAMA_BUILD}/llama-server" --version | head -2
"$LLAMA_DIR/llama-${LLAMA_BUILD}/llama-server" --list-devices

say "models (~3.5 GB)"
dl(){ # repo file
  local url="https://huggingface.co/$1/resolve/main/$2"
  if [ -s "$MODELS_DIR/$2" ]; then echo "  have $2"; else
    echo "  get  $2"; curl -fL -C - -o "$MODELS_DIR/$2" "$url"
  fi
}
dl mradermacher/OvisOCR3-GGUF              OvisOCR3-Q4_K_M.gguf
dl mradermacher/OvisOCR3-GGUF              OvisOCR3.mmproj-Q8_0.gguf
dl LiquidAI/d1-2B-GGUF                     d1-2B-Q4_K_M.gguf
dl LiquidAI/d1-2B-GGUF                     mmproj-d1-2B-Q8_0.gguf
dl LiquidAI/LFM3-VL-2B-GGUF                LFM3-VL-2B-Q4_K_M.gguf
dl LiquidAI/LFM3-VL-2B-GGUF                mmproj-LFM3-VL-2B-Q8_0.gguf
dl prithivMLmods/LightOnOCR-3-0.8B-GGUF    LightOnOCR-3-0.8B.Q4_K_M.gguf
dl prithivMLmods/LightOnOCR-3-0.8B-GGUF    LightOnOCR-3-0.8B.mmproj-q8_0.gguf

say "done"
cat <<EOF
Add to your shell (or export before each run):
  export LD_LIBRARY_PATH=$LLAMA_DIR/cudart-llama-${LLAMA_BUILD}-bin-ubuntu-${CUDA_TAG}-x64:\$LD_LIBRARY_PATH
  export LLAMA_SERVER=$LLAMA_DIR/llama-${LLAMA_BUILD}/llama-server
  export MODELS_DIR=$MODELS_DIR

Then re-derive the sizing constants on this card:
  tools/corpus/sizing/measure_vram.sh
  python3 tools/corpus/sizing/size_pipeline.py --vram 32 --page 2480x3508 --dpi 300 --sweep
EOF
