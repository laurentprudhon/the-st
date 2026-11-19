#!/usr/bin/env bash
# Vendors MAME's microcoded 68000 — the decode table (m68000.lst) and the generator that carries
# the transcribed micro/nanocode ROM (m68000gen.py) — into research/vendor/m68000/.
#
# BSD-3-Clause, Olivier Galibert. Not built, not linked: these two files are read (and the
# generator is run to print a walk), so they are vendored as data and git-ignored, the way
# tests/680x0/mame/ is. See research/mame-microcode-core.md.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/research/vendor/m68000"
PIN="${M68000_MICROCODE_PIN:-26f21525e4dada1e9fc07466759378c8bcb878de}"
BASE="https://raw.githubusercontent.com/mamedev/mame/$PIN/src/devices/cpu/m68000"

mkdir -p "$DEST"

for f in m68000.lst m68000gen.py m68000.h; do
    if [ -s "$DEST/$f" ]; then
        echo "have  $f"
        continue
    fi
    echo "fetch $f"
    curl -sfL "$BASE/$f" -o "$DEST/$f"
done

# The transcription must be the one this page measured: a different file is a different table.
cat > "$DEST/SHA256SUMS" <<'EOF'
9b4605ef5e0bd2bb2e17a03146c37c6cf4757cf926ab1b06352d95ab09695631  m68000.lst
87bcba366263ae97daa88fce3a082e85563bc903df89f0692f589d668aced05b  m68000gen.py
EOF

cd "$DEST"
sha256sum -c SHA256SUMS
echo
echo "pin $PIN -> $DEST"
echo "next: scripts/microcode-walk.py --op 80a9     (OR.l (d16,An),D0)"
