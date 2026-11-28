# Writing a chapter of `book/`

A chapter of the book is not a study: a study argues from evidence, a chapter *states* what is known and points at the
evidence. The two failure modes are opposite — a study that hides its disagreement, a chapter that launders it — and
the procedure below exists to make the second impossible.

## 0. Before prose: the claims list

Write the audit table first, one row per claim, with the source each row will carry and no verdict yet:

| # | paragraph (§) | claim, in one line | source to cite | verdict | note |
|---|---|---|---|---|---|

A claim whose source cannot be found is **not written**. It becomes either a question for the session or, if it is a
hardware question, a row in `design/emulator-limitations.md` — never a guess in prose, and never a ticket.

## 1. The provenance line — five forms, one per kind of claim

Every paragraph of prose, and every table, ends with a visible line, in italics:

```markdown
*Sources: `research/gstmcu-silicon-implementation.md` §5.2; ledger [13].*
```

| the claim came from | the line names |
|---|---|
| a chapter of a study or a design doc | the repo path and the section — never "the GSTMCU study" |
| an external source | its ledger id, the same id the studies use |
| a drawing | the sheet, and the crop's path when a crop was read |
| the code | `path:line` |
| the book's own arithmetic or analogy | `arithmetic over §x's table (no external source)` — a source for this purpose, and it tells the reader how much to trust the paragraph |

**The label travels.** A paragraph derived from an `inferred` claim says so ("`inferred` from the RTL's comment"); an
`unknown` names its register row (`B9`); a decision says "by ruling" and cites the ADR, phrased as a decision about the
model, never as a property of the hardware.

## 2. The checker, and what each failure means

```sh
scripts/verify-book.sh book/part-2/2.5-subsystem-video.md
```

| failure | meaning | fix |
|---|---|---|
| unsourced paragraph | a block of prose with no `*Sources:` line | cite it, or fold it into a sourced paragraph — do not delete the line and keep the claim |
| broken reference | a `` `research/...` `` path or `§` that does not exist | open the file and find the right section; a `§` that was renumbered is the usual cause |
| unknown or unlisted id | an id not in the ledger, or cited in the chapter but absent from its `Where this comes from` (or the reverse) | register the URL, or reconcile the list — the list is the chapter's bibliography |
| reserved-word warning | the paragraph says `exactly`/`always`/`never`/`measured` while its sources are all inferred/T3 | re-read that paragraph first in the second pass; either soften it or find a T1 source |
| manifest mismatch | a chapter file with no `book/manifest.json` entry, or an entry with no file | add/remove the entry; the manifest is what the phase's audit walks |

## 3. The second pass — a different reader, a verdict per paragraph

The writer's re-read finds typos, not overstatements. The second pass is a separate read of the finished chapter with
the named source open beside it, by a session that has not committed to the sentences (a delegated reader counts).

Verdicts, and what each one obliges:

- **`found`** — the source says it. Nothing to do.
- **`overstated`** — the source says less (`seldom` became `never`). Soften the sentence to the source's strength.
- **`wrong source`** — true, but not in the place cited. Re-point it; do not drop the citation.
- **`not found`** — the source does not say it. **Cut the paragraph or re-source it.** A `not found` is never softened
  into `overstated` to save the sentence.

Every paragraph gets a row; the table is committed beside the chapter as `<chapter>.audit.md`. A chapter with a
`not found` row is not done, whatever the checker says.

## 4. The chapter file

```markdown
# <chapter title>

> **In one page.** Three to six lines: what this chapter covers and the one thing to remember. A reader who stops
> here is not misled.

## 1. <section>

<prose, one paragraph per block, each ending with *Sources: …*>

> **Deep dive — <topic>.** The pin table, the micro-state listing, the timing arithmetic: what an implementer reads
> and a casual reader skips.

## Where this comes from

- `research/<study>.md` §x — what it settles here
- ledger [n] — title, url

*Audit: `<chapter>.audit.md` — 42 paragraphs, 42 rows, 0 `not found`.*
```

- **A chapter is 150–600 lines.** Longer means it is two chapters; shorter means the deep dives are missing.
- **Cross-links point down the book**: a Part 1/2/3 chapter never links into Part 4's code; a Part 4 chapter always
  links back to the machine chapter it models.
- **Glossary in the same commit**: a chapter that needs a term `GLOSSARY.md` lacks adds it there (and to the
  appendix), or a later reader meets a synonym and thinks it is a second thing.

## 5. Drawings

- **Timing diagrams are generated**, from a table of signals and edges carried in the chapter, by
  `book/tools/draw-timing.py`; the script prints the table it drew, so a diagram cannot drift from its text.
- **Every diagram declares its evidence**: measured (ledger id), derived (the arithmetic), or a datasheet's figure
  (page).
- **A diagram that only repeats a table is not drawn.** The bar is whether the reader sees an *order* or an *overlap*
  that the table hides.

## 6. Definition of done for a chapter

1. `scripts/verify-book.sh <chapter>` green.
2. Every paragraph has a provenance line, and every line's source resolves.
3. `<chapter>.audit.md` has one row per paragraph and zero `not found`.
4. `book/manifest.json` lists it, and the chapter's `In one page` box is true on its own.
5. The glossary is in sync, and any disagreement with a study has been fixed in both, with a date.
