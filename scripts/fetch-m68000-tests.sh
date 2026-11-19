#!/usr/bin/env bash
# ============================================================================================
# scripts/fetch-m68000-tests.sh — MAME's microcoded 68000 corpus (ticket #76), vendored whole.
#
# The corpus is SingleStepTests/m68000: MAME's microcoded core, one .json.bin per opcode file,
# 127 files x 2 500 records = 317 500. It is the **gate** for the core — clocks and state — by the
# owner's ruling (2026-11-19), because it covers at least the instruction set the denser CLK suite
# covers (127 files against 124, and the same 53 of 53 instruction forms on the files both carry).
#
# Upstream ships a packed binary format; `decode.py` (in the same upstream repository) converts
# `<name>.json.bin` to `<name>.json`. Both are copied into tests/680x0/mame/ (git-ignored, 138 MB of
# .json.bin plus the decoded JSON), so the harness reads plain JSON and the fetch is reproducible.
#
# CI is permanently offline (ADR 0010): this script is run by hand, like validate.sh.
# ============================================================================================
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root"

# Pinned: a moving HEAD would make a run unreproducible.
commit=64b253116a3de04aaac4346c43680960dc9b67e5
url=https://github.com/SingleStepTests/m68000
cache=${THE_ST_CACHE:-$root/../cache/scratch}/sst-m68000
target=tests/680x0/mame
verbose=0

for arg in "$@"; do
    case "$arg" in
        --verbose) verbose=1 ;;
        -h|--help)
            sed -n '2,16p' "$0"
            exit 0
            ;;
        *)
            echo "fetch-m68000-tests.sh: unknown option '$arg'" >&2
            exit 1
            ;;
    esac
done

# ---- the upstream checkout, at the pinned commit ------------------------------------------
if [ ! -d "$cache/.git" ]; then
    mkdir -p "$(dirname "$cache")"
    git clone -q "$url" "$cache"
fi

if ! git -C "$cache" cat-file -e "$commit^{commit}" 2>/dev/null; then
    git -C "$cache" fetch -q origin
fi

git -C "$cache" checkout -q "$commit"

if [ ! -d "$cache/v1" ]; then
    echo "fetch-m68000-tests.sh: $cache carries no v1 corpus" >&2
    exit 1
fi

# ---- decode .json.bin -> .json (upstream's own tool; it decodes <cwd>/v1 in place) ---------
if [ "$verbose" = 1 ]; then
    (cd "$cache" && python3 decode.py)
else
    (cd "$cache" && python3 decode.py >/dev/null)
fi

# ---- the copy ------------------------------------------------------------------------------
mkdir -p "$target"
cp "$cache"/v1/*.json.bin "$target"/
cp "$cache"/v1/*.json "$target"/
cp "$cache"/decode.py "$target"/

echo "wrote $target — $(ls "$target"/*.json 2>/dev/null | wc -l) decoded files, $(du -sh "$target" | cut -f1)"
