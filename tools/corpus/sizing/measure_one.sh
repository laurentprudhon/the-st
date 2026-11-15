#!/bin/bash
# Measure VRAM vs context for one model, to re-derive the KV cost on a new card.
#   ./measure_one.sh <model.gguf> <mmproj.gguf> <ctx> <port>
# Prints one CSV row: model,ctx,vram_mib
set -u
export LD_LIBRARY_PATH="${LD_LIBRARY_PATH:-}:/opt/data/models/llama/cudart-llama-b12409-bin-ubuntu-cuda-12.8-x64"
LL="${LLAMA_SERVER:-/opt/data/models/llama/llama-b12409/llama-server}"
MODELS="${MODELS_DIR:-/opt/data/models}"
m=$1; mm=$2; ctx=$3; port=$4

pkill -x llama-server 2>/dev/null
sleep 2
"$LL" -m "$MODELS/$m" --mmproj "$MODELS/$mm" -ngl 99 -c "$ctx" \
      --host 127.0.0.1 --port "$port" > "/tmp/load_${m}_${ctx}.log" 2>&1 &
pid=$!
for _ in $(seq 1 45); do
  curl -s -m 2 "http://127.0.0.1:$port/health" >/dev/null 2>&1 && break
  sleep 2
done
sleep 3
v=$(nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits | tr -d ' ')
echo "$m,$ctx,$v"
kill $pid 2>/dev/null
wait $pid 2>/dev/null
sleep 2
