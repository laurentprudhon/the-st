# `research/gstshifter-silicon/` — the U401 GST Shifter drawing set

The study is [`../gst-shifter-implementation.md`](../gst-shifter-implementation.md). This directory holds
the **educational sheets** drawn from it and nothing else: no image of any kind is vendored here, because
none exists to vendor.

## Why there are no images

This is the one chip of the campaign whose primary source is missing **twice**: no public decap of any STE
custom, and no sheet for the STE Shifter in Atari's recovered CAD (that archive carries the GSTMCU and the
Blitter, not the video chip). What the study rests on — the service manual's 84-pin symbol, ijor's reverse
engineering of the *earlier* ST Shifter, Atari's register tables, the board article and the reconstruction —
is the subject of the study, not of this directory.

## What the sheets are, and what they are not

`schematics/` holds five standalone SVGs, an index page, a README explaining how to read them, and the one
generator that produces all of it:

| sheet | level |
|---|---|
| `00-blocks.svg` | the chip as five paths, and what is deliberately not in it |
| `01-group.svg` | one group: the buffer, the 64-bit shift path, the reload control |
| `02-colour.svg` | index → palette → twelve pins → ladder → encoder |
| `03-line.svg` | one line against 512 clocks: `DE`, `*BLANK`, 128 windows |
| `04-registers.svg` | the register map split with U400, and the Microwire frame |

They are **functional diagrams**: topology, widths, strobes and budgets, taken from the study's labelled
claims. They are **not** circuits, not to scale, and **not a floorplan** — with no die there is no geometry
to draw, and a layout inferred from part counts would be the one failure mode this campaign forbids. Every
sheet carries its own provenance line; the study's §9 is the licence position and applies unchanged.

## Regenerating

```bash
cd schematics
python3 build_schematics.py && python3 check_overlaps.py
```

Both must print zero. The build parses each SVG (a malformed file raises there rather than in a downstream
consumer) and audits label widths against the boxes they must fit; the second script estimates text and
block boxes, and reports anything that collides or leaves the canvas. The output is deterministic: two runs
produce byte-identical files, so a rebuild never shows up as a spurious diff.
