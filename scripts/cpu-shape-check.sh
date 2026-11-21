#!/usr/bin/env bash
# ============================================================================================
# scripts/cpu-shape-check.sh — the campaign's one-command verdict (#84).
#
#   scripts/cpu-shape-check.sh <parent-commit>
#
# Every ticket of the silicon-shape campaign (#85–#94) is graded with this script, and nothing
# else: it is the whole of design/cpu-silicon.md §6 in one run. Four verdicts, exit 0 only if all
# four hold:
#
#   1. the corpus, both columns — MAME (the gate) 317 500 / 317 500 state AND exact-clock over
#      127 files; CLK state 863 265, and its red set identical to the parent's **record for
#      record** (a restructure may not trade one class for another behind an unchanged count).
#   2. the folds — a Debug `boot --frames 800` from the repo root, run.digest `4eac06dc…` /
#      run.stream `1693218a…`, with the desktop frame and the pops a frame quoted.
#   3. G8 — two Release builds (the parent's in a `git worktree`), three interleaved pairs at
#      equal frame_n, the rule `parent + 0.3 ms` and never above 10 ms.
#   4. the interface — #85's freeze test (`validation-harness cpu-surface`) if it exists, else a
#      documented skip; and always `tools/cpu-dissection/gen.py` plus a Release build of the two
#      generated variants.
#
# Everything it ran is under runs/<stamp>/shape/, beside validate.sh's own stamps, and the table
# it prints at the end is meant to be quoted into a handoff verbatim.
#
# Traps this script encodes (design/cpu-silicon.md §6, #84's ticket):
#   · a failed Release build leaves the previous binary and the G8 row then lies — the build log
#     is checked first, and a failed build fails G8 rather than reporting a number;
#   · a `validate.sh` stamp writes two run states — this script reads only the states its own
#     runs wrote, and quotes G8 from a Release boot's own run.state;
#   · every boot starts at the tree root (the harness resolves ROM/data paths against the cwd);
#   · a G8 figure is compared only against another G8 figure at the same frame_n, never against
#     a standalone boot or a validate.sh row (those run ~0.9 ms apart on this host);
#   · CLK's pass rate is compared with CLK's, never subtracted from MAME's.
# ============================================================================================
set -uo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root" || exit 1

# ---- the bar, in one place (design/cpu-silicon.md §6) ---------------------------------------
# A re-bless edits these constants and nothing else in this file.
EXPECT_MAME_TOTAL=317500
EXPECT_MAME_EXACT=317500
EXPECT_MAME_FILES=127
EXPECT_CLK_STATE=863265
EXPECT_CLK_EXACT=819372
EXPECT_DIGEST=4eac06dc
EXPECT_STREAM=1693218a
EXPECT_DESKTOP=602
G8_SLACK=0.3
G8_BUDGET=10.0
G8_FRAMES=800
FOLD_FRAMES=800

usage() {
    echo "usage: scripts/cpu-shape-check.sh <parent-commit>" >&2
    echo "       the parent is graded against the working tree; e.g. \`git rev-parse HEAD~1\`" >&2
    exit 2
}

[ $# -ge 1 ] || usage
parent_ref=$1
parent_sha=$(git rev-parse --verify "$parent_ref^{commit}" 2>/dev/null) || {
    echo "cpu-shape-check: no such commit '$parent_ref'" >&2
    exit 2
}
parent_short=$(git rev-parse --short "$parent_sha")
child_short=$(git rev-parse --short HEAD)
if [ -n "$(git status --porcelain)" ]; then
    tree_state="working tree dirty — the numbers are not a commit's"
else
    tree_state="working tree clean"
fi

stamp=$(date -u +%Y%m%dT%H%M%SZ)
# absolute: every boot runs from its own tree's root, and an --out relative to the cwd would
# land inside the parent's worktree
out="$root/runs/$stamp/shape"
mkdir -p "$out" || exit 1

fail=0
fail_note=""

step() { printf '\n== %s\n' "$*"; }
ok()   { printf '   ok    %s\n' "$*"; }
bad()  { printf '   FAIL  %s\n' "$*"; fail=1; note "$*"; }
skip() { printf '   skip  %s\n' "$*"; }
note() { fail_note="${fail_note}    · $*
"; }

# ---- environment (the build host has no ICU) ------------------------------------------------
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
if ! command -v dotnet >/dev/null 2>&1; then
    for candidate in /opt/data/.dotnet /opt/data/dotnet "$HOME/dotnet" /usr/share/dotnet; do
        if [ -x "$candidate/dotnet" ]; then PATH="$candidate:$PATH"; break; fi
    done
fi
if ! command -v dotnet >/dev/null 2>&1; then
    echo "cpu-shape-check: no dotnet on PATH" >&2
    exit 1
fi
export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(command -v dotnet)")}"
export PATH="$DOTNET_ROOT:$PATH"

echo "The ST — the core's shape check (#84)"
echo "  child  $child_short  ($tree_state)"
echo "  parent $parent_short ($parent_sha)"
echo "  runs   $out"

# A harness of one tree, run from that tree's root (the traps: ROM/data paths are cwd-relative).
harness_in() {
    local tree=$1 config=$2
    shift 2
    ( cd "$tree" && "./tools/validation-harness/bin/$config/net11.0/validation-harness" "$@" )
}

state_of() {
    local file=$1 key=$2
    [ -f "$file" ] || return 1
    grep -E "^$key=" "$file" | head -1 | cut -d= -f2-
}

# 317,500 → 317500 ; a blank stays blank.
count() { printf '%s' "$1" | tr -d ' ,' ; }
# 317500 → "317 500" for the table (the corpus's own figure, grouping the thousands by threes).
spaced() {
    local v=$1 rest=""
    while [ ${#v} -gt 3 ]; do rest=" ${v: -3}$rest"; v=${v:0:${#v}-3}; done
    printf '%s%s' "$v" "$rest"
}

# ============================================================================================
# build — the child (Debug for the corpus and the folds, Release for G8) and the parent (both,
# in a worktree). A failed Release build is G8's verdict, not a number to explain away.
# ============================================================================================
step "build — child Debug + Release, parent Debug + Release ($parent_short in a worktree)"
build_child_debug=1
build_child_release=1
if dotnet build TheST.sln -c Debug >"$out/build-Debug.log" 2>&1; then
    build_child_debug=0
    ok "child Debug: $(grep -oE '[0-9]+ Warning\(s\)' "$out/build-Debug.log" | tail -1), $(grep -oE '[0-9]+ Error\(s\)' "$out/build-Debug.log" | tail -1)"
else
    bad "child Debug build failed — see $out/build-Debug.log"
fi
if dotnet build TheST.sln -c Release >"$out/build-Release.log" 2>&1; then
    build_child_release=0
    ok "child Release: $(grep -oE '[0-9]+ Warning\(s\)' "$out/build-Release.log" | tail -1), $(grep -oE '[0-9]+ Error\(s\)' "$out/build-Release.log" | tail -1)"
else
    bad "child Release build failed — see $out/build-Release.log (G8 has no binary of its own)"
fi

parent_tree="$out/parent-tree"
rm -rf "$parent_tree"
build_parent_debug=1
build_parent_release=1
if git worktree add --detach --quiet "$parent_tree" "$parent_sha" >"$out/parent-worktree.log" 2>&1; then
    if ( cd "$parent_tree" && dotnet build TheST.sln -c Debug >"$out/parent-build-Debug.log" 2>&1 ); then
        build_parent_debug=0
        ok "parent Debug: built"
    else
        bad "parent Debug build failed — see $out/parent-build-Debug.log"
    fi
    if ( cd "$parent_tree" && dotnet build TheST.sln -c Release >"$out/parent-build-Release.log" 2>&1 ); then
        build_parent_release=0
        ok "parent Release: built"
    else
        bad "parent Release build failed — see $out/parent-build-Release.log (G8 cannot be subtacted from nothing)"
    fi
else
    bad "git worktree add failed — see $out/parent-worktree.log"
fi
cleanup() {
    if [ -d "$parent_tree" ]; then
        git worktree remove --force "$parent_tree" >/dev/null 2>&1 || rm -rf "$parent_tree"
        git worktree prune >/dev/null 2>&1 || true
    fi
}
trap cleanup EXIT

# ============================================================================================
# 1. the corpus, both columns
# ============================================================================================
# The corpus directories are git-ignored, so they are named absolutely: a worktree does not carry
# them, and the parent's run must grade the same bytes the child's does.
mame_dir="$root/tests/680x0/mame"
clk_dir="$root/tests/680x0/full"

corpus_summary() {
    # "cpu-tests: 317,500 / 317,500 passed, 0 failed" and the two column lines → tab-separated
    awk '
        /^cpu-tests: / {
            for (i = 1; i <= NF; i++) if ($i == "/") { passed = $(i-1); total = $(i+1) }
            sub(/,/, "", passed); sub(/,/, "", total)
        }
        /^  exact-clock tests/ { exact = $3; sub(/,/, "", exact) }
        /^  opcode files/      { files = $NF }
        END { printf "%s\t%s\t%s\t%s\n", passed, total, exact, files }
    ' "$1"
}

step "corpus — MAME (the gate), both columns: 317 500 state and exact-clock"
mame_measured="not run"
mame_verdict=FAIL
if [ "$build_child_debug" -eq 0 ] && [ -d "$mame_dir" ]; then
    harness_in "$root" Debug cpu-tests --corpus mame --dir "$mame_dir" --quiet \
        --out "$out/corpus-mame.txt" --failures "$out/corpus-mame.failures" \
        >"$out/corpus-mame.log" 2>&1
    IFS=$'\t' read -r m_passed m_total m_exact m_files <<<"$(corpus_summary "$out/corpus-mame.log")"
    m_passed=$(count "${m_passed:-}"); m_total=$(count "${m_total:-}")
    m_exact=$(count "${m_exact:-}"); m_files=$(count "${m_files:-}")
    mame_measured="$(spaced "$m_passed") / $(spaced "$m_total") state, $(spaced "$m_exact") exact-clock, $m_files files"
    if [ "$m_passed" = "$EXPECT_MAME_TOTAL" ] && [ "$m_total" = "$EXPECT_MAME_TOTAL" ] \
       && [ "$m_exact" = "$EXPECT_MAME_EXACT" ] && [ "$m_files" = "$EXPECT_MAME_FILES" ]; then
        mame_verdict=PASS
        ok "MAME: $mame_measured"
    else
        bad "MAME: $mame_measured — the bar is $(spaced "$EXPECT_MAME_TOTAL") / $(spaced "$EXPECT_MAME_TOTAL") state and exact-clock, $EXPECT_MAME_FILES files"
        echo "     the first failures:"; head -5 "$out/corpus-mame.failures" 2>/dev/null | sed 's/^/       /'
    fi
elif [ "$build_child_debug" -ne 0 ]; then
    bad "MAME: no Debug build to run it"
else
    bad "MAME: no corpus at $mame_dir (scripts/fetch-m68000-tests.sh fetches it)"
fi

step "corpus — CLK (the denser suite), state and the red set record for record against the parent's"
clk_measured="not run"
clk_verdict=FAIL
if [ "$build_child_debug" -eq 0 ] && [ "$build_parent_debug" -eq 0 ] && [ -d "$clk_dir" ]; then
    harness_in "$root" Debug cpu-tests --corpus clk --dir "$clk_dir" --quiet \
        --out "$out/corpus-clk.txt" --failures "$out/corpus-clk.failures" \
        >"$out/corpus-clk.log" 2>&1
    harness_in "$parent_tree" Debug cpu-tests --corpus clk --dir "$clk_dir" --quiet \
        --out "$out/corpus-clk-parent.txt" --failures "$out/corpus-clk-parent.failures" \
        >"$out/corpus-clk-parent.log" 2>&1
    IFS=$'\t' read -r c_passed c_total c_exact c_files <<<"$(corpus_summary "$out/corpus-clk.log")"
    c_passed=$(count "${c_passed:-}"); c_total=$(count "${c_total:-}")
    c_exact=$(count "${c_exact:-}"); c_files=$(count "${c_files:-}")
    # The red set is keyed on (file, record name) — the parent's own record identities, never a
    # pass count. The reason column is compared separately because a record can stay red and
    # change which field it disagrees on (the CLK frame-status dispute, #79).
    cut -f1,2 "$out/corpus-clk-parent.failures" | sort >"$out/clk-red-parent.set"
    cut -f1,2 "$out/corpus-clk.failures"        | sort >"$out/clk-red-child.set"
    cut -f1,3 "$out/corpus-clk-parent.failures" | sort -u >"$out/clk-reason-parent.set"
    cut -f1,3 "$out/corpus-clk.failures"        | sort -u >"$out/clk-reason-child.set"
    red_parent=$(wc -l <"$out/clk-red-parent.set")
    red_child=$(wc -l <"$out/clk-red-child.set")
    new_red=$(comm -13 "$out/clk-red-parent.set" "$out/clk-red-child.set" | wc -l)
    fixed=$(comm -23 "$out/clk-red-parent.set" "$out/clk-red-child.set" | wc -l)
    reason_moves=$(comm -13 "$out/clk-reason-parent.set" "$out/clk-reason-child.set" | wc -l)
    red_files=$(cut -f1 "$out/clk-red-child.set" | sort -u | wc -l)
    clk_measured="$(spaced "$c_passed") state, $(spaced "$c_exact") exact-clock, $red_child red records in $red_files files ($new_red new, $fixed fixed vs parent; $reason_moves field moves)"
    if [ "$c_passed" = "$EXPECT_CLK_STATE" ] && [ "$c_exact" = "$EXPECT_CLK_EXACT" ] \
       && [ "$new_red" -eq 0 ] && [ "$fixed" -eq 0 ]; then
        clk_verdict=PASS
        ok "CLK: $clk_measured"
        [ "$reason_moves" -eq 0 ] || printf '   note  %s records stayed red and changed their first differing field\n' "$reason_moves"
    else
        bad "CLK: $clk_measured — the bar is $(spaced "$EXPECT_CLK_STATE") state, $(spaced "$EXPECT_CLK_EXACT") exact-clock and the parent's red set unchanged"
        [ "$new_red" -eq 0 ] || { echo "     new red records:"; comm -13 "$out/clk-red-parent.set" "$out/clk-red-child.set" | head -8 | sed 's/^/       /'; }
        [ "$fixed" -eq 0 ] || { echo "     records the change fixed (the set must not move at all):"; comm -23 "$out/clk-red-parent.set" "$out/clk-red-child.set" | head -8 | sed 's/^/       /'; }
    fi
else
    if [ "$build_child_debug" -ne 0 ] || [ "$build_parent_debug" -ne 0 ]; then
        bad "CLK: a Debug build is missing"
    else
        bad "CLK: no corpus at $clk_dir (scripts/fetch-680x0-tests.sh --full fetches it)"
    fi
fi

# ============================================================================================
# 2. the folds — a Debug boot of 800 frames from the repo root
# ============================================================================================
step "folds — boot --frames $FOLD_FRAMES (Debug, from the repo root): run.digest / run.stream"
fold_measured="not run"
fold_verdict=FAIL
fold_state="$out/fold/run.state"
if [ "$build_child_debug" -eq 0 ]; then
    harness_in "$root" Debug boot --frames "$FOLD_FRAMES" --out "$out/fold" >"$out/fold.log" 2>&1
    fold_exit=$?
    if [ -f "$fold_state" ]; then
        digest=$(state_of "$fold_state" digest_sha256)
        stream=$(state_of "$fold_state" stream_sha256)
        desktop=$(state_of "$fold_state" frames_to_desktop)
        frclock=$(state_of "$fold_state" _frclock_at_desktop)
        pops=$(state_of "$fold_state" pops_per_frame)
        fold_measured="digest ${digest:0:8}… / stream ${stream:0:8}…, desktop $desktop ($frclock), $pops pops a frame"
        if [ "${digest:0:8}" = "$EXPECT_DIGEST" ] && [ "${stream:0:8}" = "$EXPECT_STREAM" ] \
           && [ "$desktop" = "$EXPECT_DESKTOP" ]; then
            fold_verdict=PASS
            ok "folds: $fold_measured"
        else
            bad "folds: $fold_measured — the bar is $EXPECT_DIGEST… / $EXPECT_STREAM… with the desktop at $EXPECT_DESKTOP"
            echo "     a corrected instruction EmuTOS itself executes moves the pair: that is the owner's"
            echo "     re-bless, never a session's — quote this row and stop."
        fi
    else
        bad "folds: the boot wrote no run.state (exit $fold_exit) — see $out/fold.log"
    fi
else
    bad "folds: no Debug build to boot"
fi

# ============================================================================================
# 3. G8 — three interleaved pairs of Release boots at equal frame_n, parent against child
# ============================================================================================
step "G8 — three interleaved Release pairs (parent, child) at $G8_FRAMES frames, equal frame_n"
g8_measured="not run"
g8_verdict=FAIL
if [ "$build_child_release" -eq 0 ] && [ -f "$parent_tree/tools/validation-harness/bin/Release/net11.0/validation-harness" ]; then
    pairs_tsv="$out/g8-pairs.tsv"
    : >"$pairs_tsv"
    g8_runs_ok=1
    for pair in 1 2 3; do
        # interleaved, parent first, within one pair: the host's drift then hits both sides
        harness_in "$parent_tree" Release boot --frames "$G8_FRAMES" --out "$out/g8/p$pair-parent" >"$out/g8/p$pair-parent.log" 2>&1
        harness_in "$root"            Release boot --frames "$G8_FRAMES" --out "$out/g8/p$pair-child"  >"$out/g8/p$pair-child.log" 2>&1
        p_state="$out/g8/p$pair-parent/run.state"
        c_state="$out/g8/p$pair-child/run.state"
        if [ ! -f "$p_state" ] || [ ! -f "$c_state" ]; then
            g8_runs_ok=0
            bad "G8 pair $pair: a boot produced no run.state — see $out/g8/p$pair-*.log"
            continue
        fi
        printf '%s\t%s\t%s\t%s\t%s\n' "$pair" \
            "$(state_of "$p_state" frame_mean_ms)" "$(state_of "$p_state" frame_n)" \
            "$(state_of "$c_state" frame_mean_ms)" "$(state_of "$c_state" frame_n)" >>"$pairs_tsv"
    done

    if [ "$g8_runs_ok" -eq 1 ]; then
        g8_out=$(python3 - "$pairs_tsv" "$G8_SLACK" "$G8_BUDGET" <<'PY'
import sys
path, slack, budget = sys.argv[1], float(sys.argv[2]), float(sys.argv[3])
rows = []
for line in open(path):
    pair, pm, pn, cm, cn = line.rstrip("\n").split("\t")
    rows.append((int(pair), float(pm), int(pn), float(cm), int(cn)))
problems = []
if not rows:
    problems.append("no pair completed")
for pair, pm, pn, cm, cn in rows:
    if pn != cn:
        problems.append(f"pair {pair}: frame_n differs ({pn} vs {cn}) — not a comparison")
    if cm > pm + slack:
        problems.append(f"pair {pair}: child {cm:.2f} ms > parent {pm:.2f} + {slack:.1f} ms")
    if cm > budget:
        problems.append(f"pair {pair}: child {cm:.2f} ms above the {budget:.1f} ms budget")
means = [r[1] for r in rows]
print("parent " + " / ".join(f"{p:.2f}" for p in means)
      + " vs child " + " / ".join(f"{c:.2f}" for c in [r[3] for r in rows])
      + f" ms at n {rows[0][2]}, worst pair {max(r[3] - r[1] for r in rows):+.2f} ms"
      + ("\tPASS" if not problems else "\tFAIL: " + "; ".join(problems)))
PY
)
        g8_measured=${g8_out%%$'\t'*}
        g8_result=${g8_out##*$'\t'}
        if [ "$g8_result" = "PASS" ]; then
            g8_verdict=PASS
            ok "G8: $g8_measured"
        else
            bad "G8: $g8_measured — ${g8_result#FAIL: }"
        fi
    else
        g8_measured="a boot produced no run.state"
    fi
else
    if [ "$build_child_release" -ne 0 ] || [ "$build_parent_release" -ne 0 ]; then
        g8_measured="no Release binary — the build failed, so there is no number to trust"
        bad "G8: $g8_measured"
    else
        bad "G8: the parent's Release binary is missing"
    fi
fi

# ============================================================================================
# 4. the interface — #85's freeze test if it exists, and the dissection tool's two variants
# ============================================================================================
step "interface — the public surface's freeze test, and gen.py + a build of both variants"
interface_measured="not run"
interface_verdict=FAIL
if [ "$build_child_debug" -ne 0 ]; then
    bad "interface: no Debug build"
else
    harness_in "$root" Debug cpu-surface >"$out/cpu-surface.log" 2>"$out/cpu-surface.err"
    surface_exit=$?
    if [ "$surface_exit" -eq 0 ]; then
        surface_row="cpu-surface PASS"
        surface_ok=1
    elif grep -q "unknown command 'cpu-surface'" "$out/cpu-surface.err"; then
        # #85 has not landed: the freeze test does not exist yet, and that is the documented skip
        # §4 of design/cpu-silicon.md names. It is a skip, not a failure — but it is printed.
        surface_row="cpu-surface absent (#85, documented skip)"
        surface_ok=1
    else
        surface_row="cpu-surface FAIL (exit $surface_exit)"
        surface_ok=0
    fi

    gen_ok=0
    build_ok=0
    python3 tools/cpu-dissection/gen.py >"$out/gen.log" 2>&1 && gen_ok=1
    if [ "$gen_ok" -eq 1 ]; then
        dotnet build tools/cpu-dissection/CpuDissection.csproj -c Release >"$out/build-dissection.log" 2>&1 && build_ok=1
    fi

    if [ "$surface_ok" -eq 1 ] && [ "$gen_ok" -eq 1 ] && [ "$build_ok" -eq 1 ]; then
        interface_verdict=PASS
        interface_measured="$surface_row; gen.py Sync+Folded, Release build ok"
        ok "interface: $interface_measured"
    else
        [ "$gen_ok" -eq 1 ] || bad "interface: gen.py failed — see $out/gen.log"
        [ "$build_ok" -eq 1 ] || bad "interface: the generated variants do not build — see $out/build-dissection.log"
        [ "$surface_ok" -eq 1 ] || bad "interface: $surface_row — see $out/cpu-surface.err"
        interface_measured="$surface_row; gen.py $([ "$gen_ok" -eq 1 ] && echo Sync+Folded || echo FAIL), build $([ "$build_ok" -eq 1 ] && echo ok || echo FAIL)"
    fi
fi

# ============================================================================================
# the table — one block, quotable verbatim into a handoff
# ============================================================================================
echo
echo "== the four verdicts"
echo
echo "  child  $child_short  ($tree_state)"
echo "  parent $parent_short"
echo "  runs   $out"
echo
printf '| %-14s | %-96s | %-58s | %-6s |\n' "verdict" "measured" "bar" "status"
printf '|%s|%s|%s|%s|\n' "$(printf -- '-%.0s' {1..16})" "$(printf -- '-%.0s' {1..98})" "$(printf -- '-%.0s' {1..60})" "$(printf -- '-%.0s' {1..8})"
printf '| %-14s | %-96s | %-58s | %-6s |\n' "corpus mame" "$mame_measured" \
    "$(spaced "$EXPECT_MAME_TOTAL") both columns, $EXPECT_MAME_FILES files" "$mame_verdict"
printf '| %-14s | %-96s | %-58s | %-6s |\n' "corpus clk" "$clk_measured" \
    "$(spaced "$EXPECT_CLK_STATE") state + the parent's red set, record for record" "$clk_verdict"
printf '| %-14s | %-96s | %-58s | %-6s |\n' "folds" "$fold_measured" \
    "${EXPECT_DIGEST}… / ${EXPECT_STREAM}…, desktop $EXPECT_DESKTOP" "$fold_verdict"
printf '| %-14s | %-96s | %-58s | %-6s |\n' "G8" "$g8_measured" \
    "parent + ${G8_SLACK} ms at equal frame_n, never above ${G8_BUDGET} ms" "$g8_verdict"
printf '| %-14s | %-96s | %-58s | %-6s |\n' "interface" "$interface_measured" \
    "the freeze test or a documented skip; gen.py + a build of both variants" "$interface_verdict"
echo

if [ "$fail" -eq 0 ]; then
    echo "PASS — the corpus is closed on both columns and unmoved on CLK's; the folds are the"
    echo "       blessed pair; G8 holds inside the parent's +0.3 ms at the same n; the interface"
    echo "       (the freeze test or its documented absence) and both generated variants stand."
    exit 0
fi

echo "FAIL"
printf '%b' "$fail_note"
echo "  see $out"
exit 1
