#!/usr/bin/env bash
# ab2.sh — adjacent A/B of two prebuilt Release binaries, ALTERNATING order so a monotonic host drift
# cancels instead of favouring whichever binary always runs second.
# Usage: ab2.sh <binA> <tagA> <binB> <tagB> <pairs>
# Signed delta = A - B: positive means B is faster.
set -uo pipefail
export DOTNET_ROOT=/opt/data/.dotnet
export PATH=/opt/data/.dotnet:$PATH
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
cd /opt/data/the-st || exit 1   # the harness resolves roms/ against the working directory

binA=$1; tagA=$2; binB=$3; tagB=$4; pairs=${5:-4}
root=/opt/data/cache/scratch/meas
mkdir -p "$root"

run_one() { # bin outname -> frame_mean_ms
    local bin=$1 out=$2
    rm -rf "$out"
    "$bin/validation-harness" boot --frames 800 --out "$out" > "$out.log" 2>&1
    grep -E '^frame_mean_ms=' "$out/run.state" 2>/dev/null | cut -d= -f2
}

for p in $(seq 1 "$pairs"); do
    if [ $((p % 2)) -eq 1 ]; then order="AB"; else order="BA"; fi
    echo "--- pair $p ($order)"
    if [ "$order" = "AB" ]; then
        a=$(run_one "$binA" "$root/$tagA-o$p")
        b=$(run_one "$binB" "$root/$tagB-o$p")
    else
        b=$(run_one "$binB" "$root/$tagB-o$p")
        a=$(run_one "$binA" "$root/$tagA-o$p")
    fi
    alloc=$(grep -E '^alloc_mb_per_frame=' "$root/$tagB-o$p/run.state" 2>/dev/null | cut -d= -f2)
    printf '  %-10s %-7s   %-10s %-7s   delta=%s  allocB=%s  load=%s\n' \
        "$tagA" "$a" "$tagB" "$b" \
        "$(python3 -c "print(round(float('$a')-float('$b'),2))" 2>/dev/null)" \
        "${alloc:-?}" "$(cut -d' ' -f1 /proc/loadavg)"
done
