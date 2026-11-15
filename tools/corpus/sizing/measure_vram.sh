#!/bin/bash
# Re-derive the sizing constants on a new card: VRAM at two contexts per model.
#   ./measure_vram.sh            # all three models, 8k and 32k
#   ./measure_vram.sh --quick    # one model, two contexts (sanity check)
# Writes CSV rows to stdout; redirect to a file to keep them.
set -u
HERE=$(cd "$(dirname "$0")" && pwd)
PORT="${PORT:-8090}"
echo "model,ctx,vram_mib"

run(){ "$HERE/measure_one.sh" "$1" "$2" "$3" "$PORT"; }

if [ "${1:-}" = "--quick" ]; then
  run OvisOCR3-Q4_K_M.gguf OvisOCR3.mmproj-Q8_0.gguf 8192
  run OvisOCR3-Q4_K_M.gguf OvisOCR3.mmproj-Q8_0.gguf 32768
  exit 0
fi

for spec in "OvisOCR3-Q4_K_M.gguf|OvisOCR3.mmproj-Q8_0.gguf" \
            "d1-2B-Q4_K_M.gguf|mmproj-d1-2B-Q8_0.gguf" \
            "LFM3-VL-2B-Q4_K_M.gguf|mmproj-LFM3-VL-2B-Q8_0.gguf"; do
  m="${spec%%|*}"; mm="${spec##*|}"
  run "$m" "$mm" 8192
  run "$m" "$mm" 32768
done
pkill -x llama-server 2>/dev/null
