# Five sheets, one chip

Standalone SVGs — open any of them in a browser, or open `index.html` to read them in order. They are
**educational schematics**: blocks, groups, paths and budgets. Not circuits, not to scale, and not a
floorplan — this part has no die photograph and no drawing of any kind, so no layout is claimed anywhere.

| sheet | answers | look at it when |
|---|---|---|
| `00-blocks.svg` | what are the chip's parts, and what feeds what? | orientation; before any other sheet |
| `01-group.svg` | how do four words become sixteen, thirty-two or sixty-four pixels? | any question about the group, reload, or the "every other 16 pixels" bug |
| `02-colour.svg` | how does a pixel become three analogue voltages? | palette, bit weights, byte writes, mono |
| `03-line.svg` | how does a line spend its 512 clocks and 128 windows? | video/DMA budgets, `DE`, blanking, the mono line |
| `04-registers.svg` | which registers live here and which in U400? | anything at `$FF82xx`, plus the Microwire frame and the sound word |

## Reading order

`00` assumes nothing. `01` explains what one `LOAD` does and assumes `00`. `02` assumes `01` (the index
comes out of that shift path). `03` is time and can be read beside `01`. `04` is the register map and is
the sheet to open when the study says "here, not in U400".

## The colour rule (shared with the 68000 and GSTMCU sets)

| colour | meaning |
|---|---|
| **blue** | an address: the register decode, `A1–A6`, the `$FF8xxx` window |
| **green** | data: pixel data, the index, the palette's contents, the sound bytes |
| **amber** | control: `CS`, `LOAD`, the Microwire pins, the reload rule |
| **violet** | clocking: the pixel clock mux, `DE`, `SCLK`/`FCLK`, the blanking window |
| **grey / violet fill** | storage, and things deliberately not modelled |

Line weight is bus width. A dashed line means "signals, not a physical wire" or "this is not here" — the
drawings mix a block diagram and a timing chart, so dashes keep the two from being misread as one.

## What is evidence here

Each sheet carries its own provenance line; the study (`../gst-shifter-implementation.md`) carries the
labels and the citations behind them.

- **`documented`** — the 84-pin symbol (`design/board-inventory.md` §3.5), the array's geometry and the
  reload rule (ijor's reverse engineering of the *earlier* ST Shifter), the palette's `1 8 4 2` weights
  (Atari's own register table and its worked fade), the Microwire frame (the board article), `DE`/`*BLANK`
  edges (the GLUE model).
- **`derived`** — the 2 µs group grid, 16/32/64 pixels a group, 80/80/40 words a line, 4 000 groups a
  frame, and the arithmetic behind a colour channel's weights.
- **`unknown`, and drawn as such** — the sound FIFO's depth, the resistor values in each channel's
  ladder, the mono raster's blank release.

Nothing here is copied from a drawing. The sheets are redrawn from the structure the study establishes;
the study's §9 states the licence position and applies here unchanged.

## Regenerating

```bash
python3 build_schematics.py && python3 check_overlaps.py
```

`build_schematics.py` writes the five SVGs, parses each one (a malformed file raises here rather than in
Inkscape), and audits its own labels: width estimated at a conservative 0.60 em per character, reported
against the box each label has to fit. `check_overlaps.py` is the second pass — estimated text bounding
boxes, reported per file. Both print zero on a clean build; a warning is a build failure, not a note.
To make PNGs, `cairosvg.svg2png(url=..., write_to=...)` on each file is enough — the sheets are 1440
units wide by design.
