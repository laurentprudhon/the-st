#!/bin/sh
#
# build-emutos-1.4.sh - rebuild EmuTOS 1.4's 256 KB image set and prove it byte-identical
#
# This is the reproduction recipe behind roms/emutos-1.4/*.sym and
# roms/emutos-1.4-symbols.json. It downloads the pinned source and toolchain, rebuilds all 19
# country images, checks every SHA-256 against roms/emutos-1.4-manifest.json, and converts each
# build's linker map into a Hatari .sym file with EmuTOS's own tools/map2sym.sh.
#
# It fails loudly (exit 1) on any hash mismatch: the images in the manifest are the contract, so a
# difference means the toolchain or the build date is wrong, never that the manifest needs editing.
#
# Usage:
#   scripts/build-emutos-1.4.sh                    # rebuild + verify, artefacts in $WORKDIR/out
#   scripts/build-emutos-1.4.sh --write-symbols    # also copy the 18 PAL .sym files into roms/
#   EMUTOS_BUILD_DIR=/big/disk/emutos scripts/build-emutos-1.4.sh
#
# Requirements: curl, sha256sum, dpkg-deb, tar, make, gcc (host, for EmuTOS's mkrom tool),
# mawk/gawk, python3 (manifest + .sym checks), coreutils. Tested on Debian 13 amd64.
#
# Note: the toolchain is unpacked from .deb files rather than apt-installed, so no root is needed.

set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
WORKDIR=${EMUTOS_BUILD_DIR:-${TMPDIR:-/tmp}/emutos-1.4-rebuild}
OUT=$WORKDIR/out
WRITE_SYMBOLS=0
for arg in "$@"; do
    case $arg in
        --write-symbols) WRITE_SYMBOLS=1 ;;
        -h|--help) sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "unknown argument: $arg" >&2; exit 2 ;;
    esac
done

# --- Pinned inputs -----------------------------------------------------------------------------
# Source: the official 1.4 source archive. The binaries it produces are GPLv2, like EmuTOS.
SRC_URL=https://downloads.sourceforge.net/project/emutos/emutos/1.4/emutos-src-1.4.tar.gz
SRC_SHA=98977ab2593d9ab81d6fcf9629f6781369912e3fe05c224a35147d5665749702

# Toolchain: Vincent Riviere's m68k-atari-mint tools, the only supported one (doc/install.txt).
# GCC 4.6.4 is what the 1.4 release readme names as the compiler used for the release; the
# Debian packages are fetched from his apt repository and unpacked locally (no root required).
DEB_BASE=http://vincent.riviere.free.fr/apt/dists/trixie/contrib/binary-amd64
GCC_DEB=gcc-m68k-atari-mint_4.6.4-mint-20200504-6_amd64.deb
GCC_SHA=02171d0c4db8b07d86d69488e874791da739db3b80d05ddf2fbe2f7b7564ea3e
BINUTILS_DEB=binutils-m68k-atari-mint_2.30-mint-20180703-2_amd64.deb
BINUTILS_SHA=8574686f8c48c0174c3f24c381f751f6c0a57b93544498f85e2f796b8c822967
MINTBIN_DEB=mintbin-m68k-atari-mint_0.3-qGit-20230913-1_amd64.deb
MINTBIN_SHA=422cadcfd40c234d7ba375f09174ed5486d896fccb824d6f16d7809f0b12560e

# The build date is part of the ROM: tools/mkheader.awk bakes `date +%Y,%m,%d` into the TOS header
# (os_date at 0x18, os_dosdate at 0x1e) and into the COPYRIGHT_YEAR strings. Without pinning it the
# images differ from the release in exactly those 5 bytes and nothing else. Pin it to the release
# date instead of touching the source.
BUILD_DATE_YMD=2025,06,07

# Every country all256 builds; us is the NTSC image the project does not ship (see the manifest's
# "excluded" entry) but it is still built and checked, because it is part of the same proof.
COUNTRIES="us de fr cz gr es fi sg ru it uk no se nl pl hu tr ro ca"

# --- Helpers -----------------------------------------------------------------------------------
say() { printf '%s\n' "$*"; }
die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

# fetch <url> <sha256> <dest>
fetch() {
    if [ -f "$3" ] && [ "$(sha256sum "$3" | cut -d' ' -f1)" = "$2" ]; then
        say "  cached  $(basename "$3")"
        return 0
    fi
    say "  fetch   $1"
    curl -fsSL --retry 3 --retry-delay 2 -o "$3" "$1" || die "download failed: $1"
    got=$(sha256sum "$3" | cut -d' ' -f1)
    [ "$got" = "$2" ] || die "checksum mismatch for $3
  expected $2
  got      $got"
}

# --- 1. Pinned inputs --------------------------------------------------------------------------
say "== workdir $WORKDIR"
mkdir -p "$WORKDIR/pkgs" "$WORKDIR/toolchain" "$OUT"
say "-- fetching pinned source and toolchain"
fetch "$SRC_URL"    "$SRC_SHA"      "$WORKDIR/pkgs/emutos-src-1.4.tar.gz"
fetch "$DEB_BASE/$GCC_DEB"      "$GCC_SHA"      "$WORKDIR/pkgs/$GCC_DEB"
fetch "$DEB_BASE/$BINUTILS_DEB" "$BINUTILS_SHA" "$WORKDIR/pkgs/$BINUTILS_DEB"
fetch "$DEB_BASE/$MINTBIN_DEB"  "$MINTBIN_SHA"  "$WORKDIR/pkgs/$MINTBIN_DEB"

say "-- unpacking toolchain"
for d in "$GCC_DEB" "$BINUTILS_DEB" "$MINTBIN_DEB"; do
    dpkg-deb -x "$WORKDIR/pkgs/$d" "$WORKDIR/toolchain"
done

say "-- unpacking source"
rm -rf "$WORKDIR/src"
mkdir -p "$WORKDIR/src"
tar -xzf "$WORKDIR/pkgs/emutos-src-1.4.tar.gz" -C "$WORKDIR/src" --strip-components=1

# The date shim: EmuTOS asks `date` only for the header/version fields, so one stub pins the build.
say "-- pinning the build date to $BUILD_DATE_YMD"
mkdir -p "$WORKDIR/date-shim"
cat > "$WORKDIR/date-shim/date" <<EOF
#!/bin/sh
printf '%s\\n' $BUILD_DATE_YMD
EOF
chmod +x "$WORKDIR/date-shim/date"

PATH="$WORKDIR/toolchain/usr/bin:$WORKDIR/date-shim:$PATH"
export PATH

say "-- toolchain"
m68k-atari-mint-gcc --version | head -1 | sed 's/^/  /'
m68k-atari-mint-ld --version | head -1 | sed 's/^/  /'

# --- 2. Build every country --------------------------------------------------------------------
# `make 256 UNIQUE=<cc>` recompiles only the country-dependent objects; the linker map emutos.map is
# overwritten by the next build, so it is copied out immediately, before the next iteration.
#
# -g is added to the documented -Os because map2src.py needs the compiler's line info. It does not
# change code generation, so the images stay byte-identical to the release: the verification below
# would fail loudly if a single byte moved.
cd "$WORKDIR/src"
for cc in $COUNTRIES; do
    say "-- build $cc"
    if ! make 256 UNIQUE=$cc OPTFLAGS='-Os -g' > "$OUT/build-$cc.log" 2>&1; then
        tail -20 "$OUT/build-$cc.log" >&2
        die "build failed for country $cc (log: $OUT/build-$cc.log)"
    fi
    cp emutos.map "$OUT/etos256$cc.map"
    cp "etos256$cc.img" "$OUT/"
    sh tools/map2sym.sh "$OUT/etos256$cc.map" > "$OUT/etos256$cc.sym"
    python3 "$ROOT/scripts/map2src.py" --map "$OUT/etos256$cc.map" --sym "$OUT/etos256$cc.sym" \
        --out "$OUT/etos256$cc.src" --objects "$WORKDIR/src" --source-root "$WORKDIR/src" \
        --check || die "could not build the source map for $cc"
done
cd "$ROOT"

# --- 3. Verify against the manifest and sanity-check the symbols --------------------------------
say "-- verifying against roms/emutos-1.4-manifest.json"
python3 - "$ROOT/roms/emutos-1.4-manifest.json" "$OUT" "$ROOT/roms/emutos-1.4" "$WRITE_SYMBOLS" <<'PY' || exit 1
import hashlib, json, os, sys

manifest_path, out, repodir, write = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4] == "1"
man = json.load(open(manifest_path))
entries = list(man["images"].values()) + list(man["excluded"].values())
excluded = {e["file"] for e in man["excluded"].values()}

def sha(p):
    return hashlib.sha256(open(p, "rb").read()).hexdigest()

failures, shipped = [], []
print("  %-16s %-8s %-6s %s" % ("image", "size", "symbols", "sha256"))
for e in sorted(entries, key=lambda e: e["file"]):
    path = os.path.join(out, e["file"])
    cc = e["file"][len("etos256"):-len(".img")]
    if not os.path.exists(path):
        failures.append("%s: not built" % e["file"])
        continue
    got = sha(path)
    size_ok = os.path.getsize(path) == e["size"]
    ok = got == e["sha256"] and size_ok
    sym = os.path.join(out, "etos256%s.sym" % cc)
    tsyms, bad = 0, []
    if os.path.exists(sym):
        for line in open(sym):
            parts = line.split()
            if len(parts) >= 2 and parts[1] == "T":
                a = int(parts[0], 16)
                tsyms += 1
                if not (0x00E00000 <= a <= 0x00EFFFFF):
                    bad.append(hex(a))
    print("  %-16s %-8s %-6s %s %s" % (e["file"], "ok" if size_ok else "BAD", tsyms,
                                       "MATCH" if ok else "MISMATCH", "" if ok else "%s" % got))
    if not ok:
        failures.append("%s: expected %s, got %s" % (e["file"], e["sha256"], got))
    if not os.path.exists(sym):
        failures.append("%s: no .sym produced" % e["file"])
    elif bad:
        failures.append("%s: %d T symbols outside 0x00E00000-0x00EFFFFF (first %s)"
                        % (sym, len(bad), bad[0]))
    if ok and e["file"] not in excluded:
        shipped.append(cc)

# us is reproduced but never shipped; its .sym is not committed (no image of it ever is).
shipped = [c for c in shipped if c != "us"]

if failures:
    print("\nFAILED:")
    for f in failures:
        print("  " + f)
    sys.exit(1)

print("\n  %d/%d images byte-identical to the manifest." % (len(entries) - len(failures), len(entries)))

KINDS = ("sym", "src")
diff_report = [("%s.%s" % (c, k)) for c in shipped for k in KINDS
               if os.path.exists(os.path.join(repodir, "etos256%s.%s" % (c, k)))
               and sha(os.path.join(repodir, "etos256%s.%s" % (c, k))) != sha(os.path.join(out, "etos256%s.%s" % (c, k)))]
if write:
    os.makedirs(repodir, exist_ok=True)
    for c in shipped:
        for k in KINDS:
            src = os.path.join(out, "etos256%s.%s" % (c, k))
            dst = os.path.join(repodir, "etos256%s.%s" % (c, k))
            if os.path.exists(dst) and sha(dst) == sha(src):
                continue
            open(dst, "wb").write(open(src, "rb").read())
            print("  wrote roms/emutos-1.4/etos256%s.%s" % (c, k))
elif diff_report:
    print("  committed files differ from this rebuild: %s" % ", ".join(diff_report))
    sys.exit(1)
else:
    print("  committed .sym and .src files match this rebuild.")
PY

say ""
say "OK: all images reproduced. Artefacts in $OUT"
say "    Rebuild with --write-symbols to refresh roms/emutos-1.4/*.sym and *.src."
