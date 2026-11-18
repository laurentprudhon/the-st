#!/usr/bin/env bash
# ============================================================================================
# scripts/fetch-conformance.sh — the hardware-based and hardware-checked test sources
#                                (tests/680x0/CONFORMANCE.md).
#
# Two sources, both pinned, both sha256-verified, both git-ignored-free (they are small):
#
#   T1  flamewing/68k-bcd-verifier  @ 39a01be  -> build bcd-gen, run it, keep data/bcd-table.bin
#                                                 (the table the hardware-validated ROM embeds:
#                                                  exhaustive ABCD/SBCD/NBCD, 525 312 entries)
#   T2  tonioni/WinUAE/ cputest/    @ 1977af5  -> readme.txt + cputestgen.ini (the recipe and the
#                                                 preset list of the tester whose 68000 is corrected
#                                                 against real Amigas)
#
# Expected values for T2 are not fetchable: they come from the tester core that `gencpu` emits when
# built with CPU_TESTER, judged on the Amiga side. Building that here is ticket #72.
#
# CI is permanently offline (ADR 0010): this script is run by hand, like validate.sh.
# ============================================================================================
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root"

bcd_pin=39a01be528b0744302bf1dc9b3463fc22a3fc45f
bcd_url=https://github.com/flamewing/68k-bcd-verifier
bcd_sha=8432868c9aa93c92574bae48bebd4efb2834e298340eb09530625365b80147e5

uae_pin=1977af501f6c3389c2eefe119ecb10c82d6582f3
uae_base=https://raw.githubusercontent.com/tonioni/WinUAE/$uae_pin/cputest
uae_dir=tests/680x0/conformance/winuae-cputest
declare -A uae_sha=(
    [cputestgen.ini]=2488e9cd2fc1308bd87749aa8a22aa0e0f54af52229472c7163bf989384b5a34
    [readme.txt]=10dfbbd56a16a2e080c8b2f9f2d5e7089afd7eca89fbfe7c6227baf61f73a514
)

check=0
case "${1:-}" in
    --check) check=1 ;;
    -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
    "") ;;
    *) echo "fetch-conformance.sh: unknown option '$1'" >&2; exit 1 ;;
esac

verify() {  # verify <file> <sha256>
    local got
    got=$(sha256sum "$1" | cut -d' ' -f1)
    if [ "$got" != "$2" ]; then
        echo "fetch-conformance.sh: $1 hashes $got, expected $2" >&2
        exit 1
    fi
}

# ---- T1: flamewing's BCD table -------------------------------------------------------------
bcd_out=tests/680x0/conformance/flamewing-bcd/bcd-table.bin
if [ "$check" = 1 ]; then
    verify "$bcd_out" "$bcd_sha"
    echo "ok  $bcd_out"
else
    cache=${THE_ST_CACHE:-$root/../cache/scratch}/bcd-verifier
    if [ ! -d "$cache/.git" ]; then
        mkdir -p "$(dirname "$cache")"
        git clone -q "$bcd_url" "$cache"
    fi
    if ! git -C "$cache" cat-file -e "$bcd_pin^{commit}" 2>/dev/null; then
        git -C "$cache" fetch -q origin
    fi
    git -C "$cache" checkout -q "$bcd_pin"
    ( cd "$cache" && mkdir -p data && g++ -O2 -o bcd-gen bcd-gen.cc bcd-emul.cc && ./bcd-gen )
    cp "$cache/data/bcd-table.bin" "$bcd_out"
    verify "$bcd_out" "$bcd_sha"
    echo "wrote $bcd_out ($(stat -c%s "$bcd_out") bytes)"
fi

# ---- T2: WinUAE's CPU tester recipe and presets -------------------------------------------
mkdir -p "$uae_dir"
for name in readme.txt cputestgen.ini; do
    dest="$uae_dir/$name"
    if [ "$check" = 1 ]; then
        verify "$dest" "${uae_sha[$name]}"
    else
        curl -sSL -o "$dest" "$uae_base/$name"
        verify "$dest" "${uae_sha[$name]}"
    fi
    echo "ok  $dest"
done

[ "$check" = 1 ] && echo "conformance set: all pins hold"
exit 0
