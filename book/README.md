# The Atari 1040STE, and the emulator that models it

This is the reference book for the Atari 1040STE and for `the-st`, the emulator in this repository. It has one
job: **a reader who has this book needs nothing else** to understand what the machine is, what it does with
time, how the emulator reproduces it, and where the two disagree. Where the book does not know something it says
so, and points at the register row that records the gap.

It replaces the documents written before it. Those are not deleted: they are the evidence, and they live in
`research/`, with `research/archive/` holding the epoch-1 documents each of which names the chapter that
superseded it.

## Read it along your own path

| your question | your route |
|---|---|
| **I want to use the machine** — plug it in, run a program, write a disk | Part 1, then stop. It is the manual. |
| **I want to know what the board is** — which chip does what, on which pin, at which clock | Part 1.1, then all of Part 2. |
| **I want to know what happens in a frame** — why a border opens where it does, why DMA and video collide | Part 3, after Part 2. |
| **I am implementing an emulator** — how does this project model time and the chips | Part 4, after Part 3; the register of what is not modelled is Part 4.4. |
| **I am programming the 68000** — instruction by instruction, with cycles and bus signals | Part 5, alone. It needs nothing before it. |
| **I am programming TOS** — which call, what it promises, what it does to the hardware | Part 6, then the bridge table in 6.10. |

## The parts

<!-- toc:begin -->
| **Part 1** | The machine as a product | 4 |
| **Part 2** | The machine as hardware | 9 |
| **Part 3** | Time on the real machine | 5 |
| **Part 4** | The emulator | 8 |
| **Part 5** | The 68000 | 7 |
| **Part 6** | The operating system: TOS as code | 10 |
| **Appendices** | Pin tables, the unknowns, the glossary, the source index | 4 |
<!-- toc:end -->

Every chapter is written for progressive disclosure: **In one page** gives you the chapter's gist, the numbered
sections give you the argument with its evidence, and the `Deep dive` blocks give the implementer the register
sequences and code paths that a first read does not need.

## How this book is sourced

Three rules, and they are enforced rather than promised (`scripts/verify-book.sh`):

1. **Every numbered section names its sources**, on its own line, in the repository. A section without one fails
   the gate; a claim whose source is another chapter names the chapter.
2. **The label travels.** Every claim is `measured`, `read`, `inferred`, `ours` or `unknown`, and a paragraph
   built on an `inferred` claim says so. An `unknown` names the register row in
   `design/emulator-limitations.md` that records it, and what experiment would settle it.
3. **Every chapter is read twice.** Once to write it, once against its sources, paragraph by paragraph, with a
   verdict recorded in `book/.audit/`. A `not found` verdict cuts the paragraph or re-sources it.

Part 6 adds a fourth: a claim about an implementation names **which tree** it read — real TOS by `file:line` in
`th-otto/tos1x` (pinned commit, byte-identical to the shipped ROM), or EmuTOS by `src/…:line`. Presenting one as
the other is the one error that would make the part worthless.

## What this book is not

It is not a replacement for the datasheets, the service manuals or the schematics, and it does not reprint them;
it tells you which one to open, at which sheet, and what you will find there. It is not a specification of the
emulator's internals either — `design/` is that record, and the book cites it per claim. And it is not a bug
list: where the model is wrong, Part 4.4 says so and names the register row, because a reader deserves the truth
about the book's own subject more than a tidy story.

## Building and checking

```sh
scripts/verify-book.sh                    # the gate: manifest, five checks per chapter, the fixtures
scripts/verify-book.sh book/part-2/2.2-the-chips.md   # one chapter
python3 scripts/generate-book-manifest.py # regenerate the manifest from the brief's tables
```

The manifest is generated from `docs/handoffs/documentation-phase.md` §3, which is the single source of the
chapter map: a chapter cannot exist in the brief and not in the manifest, or the reverse.
