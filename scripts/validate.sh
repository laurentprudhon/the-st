#!/usr/bin/env bash
# ============================================================================================
# scripts/validate.sh — the gate of design/validation.md §3, as far as stage 1 can carry it.
#
# Rows run here:  G4 (check-clocks), G1 and G2 on the synthetic trace, the stage-1 half of G3
# (assertions 1, 2, 6 and 7 must stay silent through every run), ADR 0008 §6's "never release" (the
# digest is absent from a Release build), and ADR 0014's falsifier (no chip holds a store).
#
# Rows NOT run here, and who owns them: G3's remaining assertions, G5, G6, G7 and G8 are #34's and
# #35's, and M1 is deferred to #12. Nothing here boots an image.
#
# CI is permanently offline (ADR 0010): this script is the verdict, run by hand. Exit code 0 = pass.
# ============================================================================================
set -uo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root" || exit 1

stamp=$(date -u +%Y%m%dT%H%M%SZ)
stampdir="runs/$stamp"
mkdir -p "$stampdir"

fail=0
step() { printf '\n== %s\n' "$*"; }
ok()   { printf '   ok    %s\n' "$*"; }
bad()  { printf '   FAIL  %s\n' "$*"; fail=1; }

# ---- environment -------------------------------------------------------------------------
# The build host has no ICU, so every dotnet invocation needs the invariant switch, and DOTNET_ROOT
# lets the apphost find a runtime that is not under /usr/share/dotnet. The built executables set
# InvariantGlobalization themselves, which the release run below proves by unsetting the variable.
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

if ! command -v dotnet >/dev/null 2>&1; then
    for candidate in /opt/data/dotnet "$HOME/dotnet" /usr/share/dotnet; do
        if [ -x "$candidate/dotnet" ]; then
            PATH="$candidate:$PATH"
            break
        fi
    done
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo "validate.sh: no dotnet on PATH" >&2
    exit 1
fi

export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(command -v dotnet)")}"

harness() { "$root/tools/validation-harness/bin/$1/net11.0/validation-harness" "${@:2}"; }

echo "The ST — validation (stage 1: the loop, the clock arithmetic, the stores)"
echo "runs in $stampdir"

# ---- build -------------------------------------------------------------------------------
step "build — Debug and Release (warnings are errors in both projects)"
for config in Debug Release; do
    log="$stampdir/build-$config.log"
    if dotnet build TheST.sln -c "$config" >"$log" 2>&1; then
        warnings=$(grep -oE '[0-9]+ Warning\(s\)' "$log" | tail -1)
        errors=$(grep -oE '[0-9]+ Error\(s\)' "$log" | tail -1)
        ok "$config build: ${warnings:-?}, ${errors:-?}"
    else
        bad "$config build failed — see $log"
        tail -n 20 "$log"
    fi
done

# ---- G4 ----------------------------------------------------------------------------------
step "G4 — the clock arithmetic is ADR 0001's (check-clocks, pure arithmetic)"
if harness Debug check-clocks | tee "$stampdir/check-clocks.log"; then
    ok "G4"
else
    bad "G4"
fi

# ---- the stores --------------------------------------------------------------------------
step "stores — Ram / Rom: the pins, machine byte order, lane isolation (ADR 0014)"
if harness Debug check-stores | tee "$stampdir/check-stores.log"; then
    ok "stores"
else
    bad "stores"
fi

# ---- ADR 0014's boundary falsifier -------------------------------------------------------
# The stores are data inside the System Control Unit and the host layer's business, and nowhere
# else: the SCU itself (they are its own decode's memory) and Machine.cs (the host's view) are the
# two files allowed to name them. Every other chip in Chips/ is checked, which is the row's point.
step "ADR 0014 falsifier — no chip holds a store (Ram/Rom/MachineBytes outside the stores, the SCU and the host)"
boundary=$(grep -rnE '\b(Ram|Rom|MachineBytes)\b' --include='*.cs' src tools 2>/dev/null \
    | grep -vE '^(src/TheST\.Machine/Stores/|tools/validation-harness/|src/TheST\.Machine/Chips/SystemControlUnit\.cs|src/TheST\.Machine/Machine\.cs)' || true)
if [ -z "$boundary" ]; then
    ok "0 references outside the stores, the System Control Unit and the host layer"
else
    bad "a chip found a private handle on a store:"
    printf '%s\n' "$boundary"
fi

# ---- G1 / G2 on the synthetic trace ------------------------------------------------------
step "G1 — two runs of the synthetic trace, same N, byte-identical run.digest"
harness Debug run --frames 100 --out "$stampdir/g1-a" >"$stampdir/g1-a.log" 2>&1 \
    && harness Debug run --frames 100 --out "$stampdir/g1-b" >"$stampdir/g1-b.log" 2>&1 \
    && ok "two runs completed" \
    || bad "a run failed — see $stampdir/g1-*.log"

if [ -f "$stampdir/g1-a/run.digest" ] && [ -f "$stampdir/g1-b/run.digest" ]; then
    if cmp -s "$stampdir/g1-a/run.digest" "$stampdir/g1-b/run.digest"; then
        ok "G1: $(wc -l <"$stampdir/g1-a/run.digest") digest words, identical"
    else
        bad "G1: the streams differ — $(cmp "$stampdir/g1-a/run.digest" "$stampdir/g1-b/run.digest" 2>&1 | head -1)"
    fi
else
    bad "G1: no run.digest was produced"
fi

step "G2 — the same stream with host scheduling perturbed (--inject)"
if harness Debug run --frames 100 --inject --out "$stampdir/g2-inject" >"$stampdir/g2.log" 2>&1; then
    if cmp -s "$stampdir/g1-a/run.digest" "$stampdir/g2-inject/run.digest"; then
        ok "G2: identical to G1's stream (yields, allocation noise, collections, frame pacing)"
    else
        bad "G2: injection changed the stream — $(cmp "$stampdir/g1-a/run.digest" "$stampdir/g2-inject/run.digest" 2>&1 | head -1)"
    fi
else
    bad "G2: the injected run failed — see $stampdir/g2.log"
fi

# ---- G3 (stage-1 half): the assertions must be silent ------------------------------------
step "G3 (assertions 1, 2, 6 and 7) — silent through a whole run"
if grep -q "ASSERTION" "$stampdir"/g1-a.log "$stampdir"/g1-b.log "$stampdir"/g2.log 2>/dev/null; then
    bad "an assertion fired"
else
    ok "no assertion fired in any run"
fi

# ---- ADR 0008 §6: the digest never reaches a release build --------------------------------
step "ADR 0008 §6 — the digest and the assertions do not exist in Release"
release_dlls="src/TheST.Machine/bin/Release/net11.0/TheST.Machine.dll tools/validation-harness/bin/Release/net11.0/validation-harness.dll"
symbols=0
for dll in $release_dlls; do
    [ -f "$dll" ] || continue
    # Member names live in the metadata as ASCII; literals (the assertion text) are UTF-16.
    ascii=$(grep -a -c -E 'Digest' "$dll" || true)
    utf16=$(strings -e l "$dll" | grep -c -E 'ASSERTION|Digest' || true)
    symbols=$((symbols + ascii + utf16))
done

if [ "$symbols" -eq 0 ]; then
    ok "release assemblies carry no Digest symbol and no ASSERTION text"
else
    bad "release assemblies still carry $symbols digest/assertion strings"
fi

# The release executable must run with no ICU variable at all, and must write no digest.
# (`env` cannot run a shell function, so this row calls the apphost directly.)
release_exe="$root/tools/validation-harness/bin/Release/net11.0/validation-harness"
if env -u DOTNET_SYSTEM_GLOBALIZATION_INVARIANT \
    "$release_exe" run --frames 5 --out "$stampdir/release" >"$stampdir/release.log" 2>&1; then
    if [ -f "$stampdir/release/run.digest" ]; then
        bad "the release build wrote a digest"
    else
        ok "the release build ran with no ICU variable and wrote no run.digest"
    fi
else
    bad "the release build did not run — see $stampdir/release.log"
fi

# ---- the boot (#34's row; not green yet) --------------------------------------------------
step "boot — EmuTOS to the GEM desktop (G3/G5/G6; #34's remaining work)"
if harness Debug boot --frames 200 --out "$stampdir/boot" >"$stampdir/boot.log" 2>&1; then
    ok "boot: reached the desktop"
else
    bad "boot: has not reached the desktop yet — see $stampdir/boot.log and $stampdir/boot/run.state"
fi

# ---- verdict -----------------------------------------------------------------------------
echo
if [ "$fail" -eq 0 ]; then
    echo "PASS — G4, G1 and G2 green on the synthetic trace; assertions 1, 2, 6 and 7 silent;"
    echo "       the stores' boundary intact; no digest in Release."
    echo "       Not proven here: G1/G2 over a TOS boot (the stream is the fixture's, not the machine's),"
    echo "       G3's remaining assertions, G5–G8, and M1. Those are #34's, #35's and #12's."
    exit 0
fi

echo "FAIL — see the rows above and $stampdir"
exit 1
