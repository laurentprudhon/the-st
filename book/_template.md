> **This file is the skeleton every chapter copies.** It is not part of the book. Delete these
> instructions, keep the headings and their order — `scripts/check-book-sources.py` checks the
> order, and the second read checks everything else.

# <chapter number> — <title, the noun the chapter is about>

<One line: the question this chapter answers, in the reader's words.>

> **In one page.** <The whole chapter in five to eight sentences, true if a reader reads nothing
> else. No new claim may survive only here: if it matters, it appears again, sourced, below.>

## 1. <the first thing the reader must believe>

<The claim in the first sentence. Then the evidence for it, in the reader's language, with the
detail the claim needs and no more.>

> **Deep dive — <what this block adds>.** <The register sequence, the micro-state walk, the code
> path, the numbers a table cannot hold. For the implementer, skippable by the reader.>

*Sources: `design/<file>.md` §n; `research/<study>.md` §n; ledger [1], [2].*

## 2. <the next thing>

*Sources: …*

## Where this comes from

1. `path/to/source.md` §n — what it contributed, and what it does not settle.
2. `path/to/other.md` §n — …

---

**How a claim is labelled.** `measured` (a scope or a test measured it), `read` (a document or a
die says it), `inferred` (derived from something else and said so), `ours` (a decision of this
project, not a property of the machine), `unknown` (nobody knows; the register row says what would
settle it). The label travels with the paragraph that carries the claim, never stated once for the
chapter. Part 6 adds one rule: a claim about an implementation names the tree it read — real TOS
`file:line` in `th-otto/tos1x` (pinned `7c6680d`), or EmuTOS `src/…:line`.

**A chapter cites in one of two ways, and does not mix them.** By **path**, which is the house
style: the `*Sources:*` line names the document and its section, and `## Where this comes from` is
the bibliography. Or **by number** in `## Where this comes from`: `[2]` is item 2 of that list, so a
reader needs no other file open — and then every number must be cited and every entry cited, which
the gate enforces. Paths are repository paths unless the chapter says otherwise; the `tos1x` and
EmuTOS trees are cited by path and pinned commit and are not carried here.
