#!/usr/bin/env bash
# ============================================================================================
# scripts/validate.sh — the gate of design/validation.md §3, as far as stage 1 can carry it.
#
# Rows run here:  G4 (check-clocks), G1 and G2 on the synthetic trace, the stage-1 half of G3
# (assertions 1, 2, 6 and 7 must stay silent through every run), G7 (retention under a live reader),
# ADR 0008 §6's "never release" (the digest is absent from a Release build), ADR 0014's falsifier
# (no chip holds a store), the boot to the GEM desktop (G5, G6), G1/G2 on that boot, and G8 — the
# frame budget, in Release, with the host and the crossing count it was measured at.
#
# Rows NOT run here, and who owns them: M1 is the owner's (the pixels have no automatic witness).
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

# ---- G7: retention never truncates under a live reader -----------------------------------
# The one failure a digest structurally cannot see: a log truncated under a live sampler changes an
# answer while the record stream stays identical, so two runs agree and both are wrong (ADR 0004's
# 2026-11-07 revision). check-retention tests the log's algebra and runs a sampler 1 000 ticks into
# the past while a writer keeps appending and retention keeps trimming.
step "G7 — retention never truncates under a live reader (ADR 0004's revision)"
if harness Debug check-retention | tee "$stampdir/check-retention.log"; then
    ok "G7"
else
    bad "G7"
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
    # `diff` is the one-line answer validation.md §4 asks for: the first differing batch, the instant
    # it drained at (from the sparse run.batches index), and each run's tail. `cmp` could only say
    # "byte 12 345 678".
    if harness Debug diff --a "$stampdir/g1-a" --b "$stampdir/g1-b" | tee "$stampdir/g1-diff.log" | grep -q 'identical'; then
        ok "G1: $(wc -l <"$stampdir/g1-a/run.digest") digest words, identical"
    else
        bad "G1: the streams differ — $(head -3 "$stampdir/g1-diff.log" | tr '\n' ' ')"
    fi
else
    bad "G1: no run.digest was produced"
fi

step "G2 — the same stream with host scheduling perturbed (--inject)"
if harness Debug run --frames 100 --inject --out "$stampdir/g2-inject" >"$stampdir/g2.log" 2>&1; then
    if harness Debug diff --a "$stampdir/g1-a" --b "$stampdir/g2-inject" | tee "$stampdir/g2-diff.log" | grep -q 'identical'; then
        ok "G2: identical to G1's stream (yields, allocation noise, collections, frame pacing)"
    else
        bad "G2: injection changed the stream — $(head -3 "$stampdir/g2-diff.log" | tr '\n' ' ')"
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

# ---- the boot: the desktop, and the boot's half of G3, G5 and G6 ---------------------------
# 800 frames is the measured boot with room to spare: EmuTOS reaches the desktop at frame 574 on
# this image, and the harness runs 150 more frames to let the AES event loop settle before it reads
# the OS's own counters. `boot` exits 0 only when the desktop was reached AND G5 and G6 are green,
# so this row is three gate rows in one run. Debug, because G3's assertions exist only there.
step "boot — EmuTOS to the GEM desktop (G5, G6), assertions silent through it (G3)"
if harness Debug boot --frames 800 --out "$stampdir/boot" >"$stampdir/boot.log" 2>&1; then
    desktop=$(grep -oE 'reached the desktop at [0-9]+' "$stampdir/boot/run.report" | head -1)
    ok "boot: ${desktop:-reached the desktop}"
    if grep -q "ASSERTION" "$stampdir/boot.log" "$stampdir/boot/run.state" 2>/dev/null; then
        bad "G3 on the boot: an assertion fired — see $stampdir/boot.log"
    else
        ok "G3 on the boot: no assertion fired from reset to the desktop"
    fi
    g5=$(grep -E '^g5=' "$stampdir/boot/run.state" | cut -d= -f2)
    g6=$(grep -E '^g6=' "$stampdir/boot/run.state" | cut -d= -f2)
    if [ "$g5" = "green" ]; then
        ok "G5: $(grep -cE '^  .* ok$' "$stampdir/boot/run.report") rates match the catalogue"
    else
        bad "G5: $g5 — $(grep -E 'MISMATCH' "$stampdir/boot/run.report" | head -3 | tr '\n' ';')"
    fi
    if [ "$g6" = "green" ]; then
        ok "G6: $(grep -E '_frclock' "$stampdir/boot/run.report" | head -1 | sed 's/^  //')"
    else
        bad "G6: $g6 — see $stampdir/boot/run.report"
    fi
else
    bad "boot: see $stampdir/boot.log and $stampdir/boot/run.state"
fi

# ---- G1 / G2 on the machine's own trace ----------------------------------------------------
# The synthetic trace above proves the scheduler's determinism; this proves it on EmuTOS's boot,
# where the workload is real (a ROM's instruction mix, the beam, the MFP, the RAM refresh, the RTC
# detection waits). 300 frames is the digest's cost, not its coverage: a Debug boot is ~0.55 s a
# frame and 364 MB of digest at 800, and the row above already runs the boot to the desktop. Exit
# code 2 here means "the desktop is not reached", which is expected at 300 and not this row's claim.
step "G1 on the machine's own trace — two boots, byte-identical digest"
harness Debug boot --frames 300 --out "$stampdir/boot-g1a" >"$stampdir/boot-g1a.log" 2>&1; g1a=$?
harness Debug boot --frames 300 --out "$stampdir/boot-g1b" >"$stampdir/boot-g1b.log" 2>&1; g1b=$?
if { [ "$g1a" -eq 0 ] || [ "$g1a" -eq 2 ]; } && { [ "$g1b" -eq 0 ] || [ "$g1b" -eq 2 ]; }; then
    if harness Debug diff --a "$stampdir/boot-g1a" --b "$stampdir/boot-g1b" | tee "$stampdir/boot-g1-diff.log" | grep -q 'identical'; then
        ok "G1: $(wc -l <"$stampdir/boot-g1a/run.digest") digest words, identical"
    else
        bad "G1 on the boot: the streams differ — $(head -3 "$stampdir/boot-g1-diff.log" | tr '\n' ' ')"
    fi
else
    bad "G1 on the boot: a run failed — see $stampdir/boot-g1*.log"
fi

step "G2 on the machine's own trace — the same stream with host scheduling perturbed"
harness Debug boot --frames 300 --inject --out "$stampdir/boot-g2" >"$stampdir/boot-g2.log" 2>&1; g2=$?
if [ "$g2" -eq 0 ] || [ "$g2" -eq 2 ]; then
    if harness Debug diff --a "$stampdir/boot-g1a" --b "$stampdir/boot-g2" | tee "$stampdir/boot-g2-diff.log" | grep -q 'identical'; then
        ok "G2 on the boot: identical to G1's stream"
    else
        bad "G2 on the boot: injection changed the stream — $(head -3 "$stampdir/boot-g2-diff.log" | tr '\n' ' ')"
    fi
else
    bad "G2 on the boot: the injected run failed — see $stampdir/boot-g2.log"
fi

# ---- G8: the frame budget -----------------------------------------------------------------
# Release, because a Debug frame carries the per-instant digest and the seven assertions: its wall
# time is a measurement of the instruments (validation.md §2). The row is the mean wall time of one
# frame against the 10 ms of the 20 ms budget (50 Hz with 2x headroom, #38's ruling), and it is reported with n, the host, the core count and
# the crossing count the run actually had — the same mean on another host means nothing (§4), which
# is why the numbers are printed beside the verdict and kept in run.state.
step "G8 — the frame budget (Release: mean frame <= 10 ms of the 20 ms frame)"
harness Release boot --frames 800 --out "$stampdir/g8" >"$stampdir/g8.log" 2>&1
g8state="$stampdir/g8/run.state"
if [ ! -f "$g8state" ]; then
    bad "G8: the Release boot produced no run.state — see $stampdir/g8.log"
else
    mean=$(grep -E '^frame_mean_ms=' "$g8state" | cut -d= -f2)
    n=$(grep -E '^frame_n=' "$g8state" | cut -d= -f2)
    cpu=$(grep -E '^cpu=' "$g8state" | cut -d= -f2)
    cores=$(grep -E '^cores=' "$g8state" | cut -d= -f2)
    pops=$(grep -E '^pops_per_frame=' "$g8state" | cut -d= -f2)
    percycle=$(grep -E '^pops_per_bus_cycle=' "$g8state" | cut -d= -f2)
    g8=$(grep -E '^g8=' "$g8state" | cut -d= -f2)
    if [ "$g8" = "green" ]; then
        ok "G8: ${mean} ms a frame of 20.00 (budget 10.00) — n ${n}, ${cpu} × ${cores}, ${pops} pops a frame (${percycle} a bus cycle)"
    else
        bad "G8: ${mean} ms a frame of 20.00 (budget 10.00) — n ${n}, ${cpu} × ${cores}, ${pops} pops a frame (${percycle} a bus cycle); see $stampdir/g8/run.report"
    fi
fi

# ---- verdict -----------------------------------------------------------------------------
echo
if [ "$fail" -eq 0 ]; then
    echo "PASS — G1 and G2 green on the synthetic trace and on EmuTOS's own boot; G3's assertions"
    echo "       silent through both; G4; G5, G6 and G8 on a boot that reaches the GEM desktop; G7;"
    echo "       the stores' boundary intact; no digest in Release."
    echo "       Not proven here: M1 (the owner's eyes). That row is #12's and is never automated."
    exit 0
fi

echo "FAIL — see the rows above and $stampdir"
exit 1
