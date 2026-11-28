# `research/blitter-silicon/` — the U101 Blitter drawing set

The study is [`../blitter-implementation.md`](../blitter-implementation.md). This directory holds the
**educational sheets** drawn from it and nothing else: no image of any kind is vendored here.

## Why there are no images, even though the design survives

This is the one chip of the campaign with a **recovered design**: `TA-4021` (Oct 1986), `ST-4051` (Mar 1988)
and `ST-4082` (Sep 1988, drawn for the combo chip) are three revisions of one 15–20-sheet machine, and every
sheet's text is machine-readable. None of it is redistributed here: the archive is cited by URL, and the sheets
below are redrawn from the structure the study establishes. There is also **no die photograph** of any STE
custom, so — as in the three sets before this one — no layout or geometry is claimed anywhere.

## What the sheets are, and what they are not

`schematics/` holds four standalone SVGs, an index page, a README explaining how to read them, and the one
generator that produces all of it:

| sheet | level |
|---|---|
| `00-blocks.svg` | the chip as blocks, and the five things deliberately absent from it |
| `01-word.svg` | one word: read → skew → HOP → LOP → mask → write, the read-count table, the 32-bit buffer, a masked line |
| `02-registers.svg` | the 62-byte register seam, the control word exploded bit by bit, and what the decode proves |
| `03-window.svg` | the bus window: 4 + 4 clocks, 64-access alternation, the 63 case, the frame budget, `BINT` |

They are **functional diagrams**: topology, widths, strobes and budgets, taken from the study's labelled
claims. They are **not** circuits, not to scale, and **not a floorplan**. Every sheet carries its own
provenance line; the study's §9 is the licence position and applies unchanged.

## Regenerating

```bash
cd schematics
python3 build_schematics.py && python3 check_overlaps.py
```

Both must print zero. The build parses each SVG (a malformed file raises there rather than in a downstream
consumer) and audits label widths against the boxes they must fit; the second script estimates text and block
boxes and reports anything that collides or leaves the canvas.
