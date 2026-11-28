#!/usr/bin/env bash
# The citation gate for this repository's studies.
#
#   scripts/verify-research.sh research/<study>.md      one study
#   scripts/verify-research.sh --all                    every study (a file with a "## Sources" block)
#
# The ledger is shared and its ids are stable for the whole campaign, so the
# ledger path is fixed rather than per-study. The verifier lives in the agent's
# profile; if it or the ledger moves, this script is the one place to change.
#
# Two things the verifier cannot see and this wrapper does not fix: it skips
# lines beginning with '|', so table rows never count; and it splits sentences on
# [.!?] + whitespace, so a citation written after a sentence's final period falls
# into a dropped fragment. Cite inside the sentence, and keep the sourced claim in
# prose even when it also appears in a table.
set -euo pipefail

HERMES_HOME="${HERMES_HOME:-$HOME/.hermes}"
LEDGER="${HERMES_CITATION_LEDGER:-$HERMES_HOME/cache/citations/ledger.json}"
SOURCES="$HERMES_HOME/skills/research/grounded-citations/scripts/sources.py"
FLOOR="${MIN_COVERAGE:-0.30}"

if [ ! -f "$SOURCES" ]; then
  echo "verify-research: missing $SOURCES (the grounded-citations skill is not installed here)" >&2
  exit 2
fi
if [ ! -f "$LEDGER" ]; then
  echo "verify-research: missing ledger $LEDGER — register a source first (sources.py add <url>)" >&2
  exit 2
fi

run() {
  # Tables and citations written after a sentence's final period do not count:
  # the verifier skips lines starting with '|' and splits on [.!?] + space.
  python3 "$SOURCES" --ledger "$LEDGER" verify --min-coverage "$FLOOR" "$1"
}

case "${1:-}" in
  --all)
    fail=0
    for study in $(grep -l '^## Sources' research/*.md | sort); do
      printf '%-56s' "$study"
      out=$(run "$study" 2>&1) || true
      # A sourcing note has a Sources block and no inline citations; the gate is for studies.
      if echo "$out" | grep -qE '(^| )0 distinct source'; then
        echo "note — no inline citations, not a study"
        continue
      fi
      if echo "$out" | grep -q 'citations OK'; then
        echo "$(echo "$out" | grep -o '[0-9]\+%' | head -1) ok"
      else
        echo "FAIL"
        echo "$out" | grep -E 'FAIL:|error' | head -2 | sed 's/^/    /'
        fail=1
      fi
    done
    exit "$fail"
    ;;
  "")
    echo "usage: $0 <study.md> | --all" >&2
    exit 2
    ;;
  *)
    run "$1" | grep -v 'registered in the ledger but not cited'
    ;;
esac
