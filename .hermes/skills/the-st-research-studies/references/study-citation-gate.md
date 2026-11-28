# The citation gate: what it counts, and how to repair a study's citations

Depth for `the-st-research-studies` §"The gate": only needed when the gate fails for a reason other than too few
citations.

## What the gate actually counts

`verify` splits the draft at the `## Sources` heading, then walks the prose **line by line**:

- a line starting with `|` (a table row) is skipped — table cells never contribute coverage;
- a line starting with `#` is skipped;
- the rest is split on `[.!?]` followed by whitespace, and each fragment of four words or more is one sentence;
- a citation is `\[\d{1,4}\]` **not** followed by `(` or `:` — so a markdown link and a reference-style label do not
  count, and neither does `[1]:`.

Two consequences that cost whole editing passes: a citation written after a sentence's final period lands in its own
one-token fragment and counts for nothing, and a claim that exists only in a table is uncited no matter what the table
says. Cite *inside* the sentence, and keep every load-bearing claim in prose as well.

## Ids

- `sources.py add <url> --title "…"` prints `[n]`; the id is the ledger's, per URL, and a re-added URL returns the
  **existing** id. Take the number from the output — never predict it.
- Every id cited inline must also appear in the `## Sources` block, and the block's URL for that id must match the
  ledger exactly, or the gate fails with "cited but absent" / "does not match the ledger".
- `verify --all` warns about ledger sources this draft does not cite. That is a warning, not a failure, and it is
  expected: the ledger is shared across studies.

## Repairing a study with study-local numbering

Symptom: the gate reports `Sources block URL for [n] does not match the ledger`, or every citation resolves to a URL
that has nothing to do with the sentence. Cause: the study was written before the shared ledger existed, or copied
from a template that numbered its own sources, so its 1..N collide with the ledger's 1..N.

Recipe, scripted, with the assertions that make it safe:

1. Parse the block into `old id → (url, title)`:
   `^\[(\d+)\]\s+(\S+)(?:\s+[—-]\s+(.*))?$` over the lines after the last `## Sources`.
2. Assert the inline id set equals the block's id set. If an inline id has no block line, stop: the study is
   mis-cited in a way a remap would cement.
3. For each old id in order, `add` its URL with its title and capture the returned id. Build `old → new`.
4. Substitute in **two passes**: `[old]` → `[@@new@@]` for every old id, assert the placeholder count equals the
   original citation count, then strip the `@@` markers. A single pass is order-dependent (1 → 10 collides with a
   real 10) and silently corrupts a study.
5. Regenerate the block sorted by new id, and assert exactly one `## Sources` heading survives — the duplicate is how
   a study reaches this state, and `_split_draft` reads the *first* heading, so the block you edited may not be the
   block the gate checked.
6. Re-run the gate: distinct sources cited should equal the block size, and the inline count should be unchanged.

Keep the old→new map in a scratch file until the push lands: it is the only record of what the renumbering meant.

## Running it over a tree

`scripts/verify-research.sh --all` classifies a file with a `## Sources` block and **zero** distinct sources cited as
a sourcing note rather than failing it. Two traps when doing that classification in shell:

- **Anchor the zero.** `grep -q '0 distinct source'` matches the "…**20** distinct source(s) cited" line of every
  healthy study. Use `grep -qE '(^| )0 distinct source'` or match a whole line.
- **A tree another session is editing gives transient zeros.** A study rewritten mid-run can report 0 % for one
  invocation and 47 % on the next; re-run before believing a failure.
