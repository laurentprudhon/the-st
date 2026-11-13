#!/usr/bin/env bash
# ============================================================================================
# scripts/fetch-680x0-tests.sh — the 680x0 corpus (ticket #34), vendored or fetched whole.
#
# The corpus is SingleStepTests/680x0: one JSON file per opcode and size, each an array of
# single-instruction tests that carry the registers and the memory map before and after. Upstream it
# is 194 MB of gzip (1 000 060 tests), so the tree carries a deterministic sample of it:
#
#   tests/680x0/v1/<mnemonic>.json.gz    24 tests per opcode file, evenly spaced through the file
#   tests/680x0/68000.official.json      the full official-opcode decoding map
#
# The sample is regenerated from the pinned commit by this script, so it is reproducible rather than
# hand-picked. `--full` writes the whole corpus to tests/680x0/full/ instead (git-ignored), which is
# what a claim of "the core passes the corpus" must be measured against.
#
# CI is permanently offline (ADR 0010): this script is run by hand, like validate.sh.
# ============================================================================================
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root"

# Pinned: the corpus is versioned by upstream, and a moving HEAD would make a run unreproducible.
commit=e0d5ece9670205cc84a0101081837deb446f86a3
url=https://github.com/SingleStepTests/680x0
cache=${THE_ST_CACHE:-$root/../cache/scratch}/sst-680x0
sample_size=24
target=tests/680x0
verbose=0
full=0

for arg in "$@"; do
    case "$arg" in
        --full) full=1 ;;
        --verbose) verbose=1 ;;
        -h|--help)
            sed -n '2,20p' "$0"
            exit 0
            ;;
        *)
            echo "fetch-680x0-tests.sh: unknown option '$arg'" >&2
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

if [ ! -f "$cache/68000/v1/ADD.w.json.gz" ]; then
    echo "fetch-680x0-tests.sh: $cache carries no 68000/v1 corpus" >&2
    exit 1
fi

if [ "$full" = 1 ]; then
    mkdir -p "$target/full"
    cp "$cache"/68000/v1/*.json.gz "$target/full/"
    cp "$cache/map/68000.official.json" "$target/full/"
    echo "wrote tests/680x0/full — $(ls "$target/full" | wc -l) files, $(du -sh "$target/full" | cut -f1)"
    exit 0
fi

# ---- the sample -----------------------------------------------------------------------------
python3 - "$cache" "$target" "$sample_size" "$verbose" <<'PY'
import gzip, json, os, sys

cache, target, sample_size, verbose = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4] == "1"
source = os.path.join(cache, "68000", "v1")
out = os.path.join(target, "v1")
os.makedirs(out, exist_ok=True)

kept = 0
for name in sorted(os.listdir(source)):
    if not name.endswith(".json.gz"):
        continue

    with gzip.open(os.path.join(source, name), "rt") as handle:
        tests = json.load(handle)

    if len(tests) <= sample_size:
        picked = tests
    else:
        # evenly spaced through the file, first and last included: the corpus is randomly ordered,
        # so a stride samples every addressing mode without hand-choosing cases
        step = (len(tests) - 1) / (sample_size - 1)
        indices = sorted({round(index * step) for index in range(sample_size)})
        picked = [tests[index] for index in indices]

    with gzip.GzipFile(os.path.join(out, name), "wb", mtime=0) as handle:
        handle.write(json.dumps(picked, separators=(",", ":")).encode())
    kept += len(picked)
    if verbose:
        print(f"  {name[:-8]:10} {len(picked):3} of {len(tests)}")

with open(os.path.join(cache, "map", "68000.official.json"), "rb") as handle:
    official = handle.read()

with open(os.path.join(target, "68000.official.json"), "wb") as handle:
    handle.write(official)

print(f"wrote {len(os.listdir(out))} opcode files, {kept} tests, into {out}")
PY
