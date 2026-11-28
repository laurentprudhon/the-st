#!/usr/bin/env bash
# The book's gate. Exits non-zero on anything that is not ready to be read.
#
#   scripts/verify-book.sh                          # everything
#   scripts/verify-book.sh book/part-2/2.2-*.md     # one or more chapters
set -uo pipefail
cd "$(dirname "$0")/.." || exit 1

fail=0

echo "== manifest =="
python3 scripts/generate-book-manifest.py --check || fail=1

echo
echo "== chapters =="
if [ "$#" -gt 0 ]; then
  python3 scripts/check-book-sources.py "$@" || fail=1
else
  python3 scripts/check-book-sources.py --all || fail=1
fi

echo
echo "== the gate's own fixtures (each one must fail, for its own reason) =="
fixtures="scripts/tests/fixtures"
check_fixture() {  # <fixture> <expected substring>
  local out
  out=$(python3 scripts/check-book-sources.py --root "$fixtures" --no-manifest "$fixtures/$1" 2>&1)
  if printf '%s' "$out" | grep -q "$2"; then
    echo "ok   $1 fails as it should ($2)"
  else
    echo "FAIL $1 did not fail with '$2'"
    printf '%s\n' "$out" | sed 's/^/     /'
    fail=1
  fi
}
check_fixture no-sources.md   "provenance:"
check_fixture dead-path.md    "paths:"
check_fixture bad-structure.md "structure:"
check_fixture bad-citation.md "citations:"

echo
if [ "$fail" -eq 0 ]; then echo "the book is green"; else echo "the book is RED"; fi
exit "$fail"
