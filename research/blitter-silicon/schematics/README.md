# Four sheets, one chip

Standalone SVGs — open any of them in a browser, or open `index.html` to read them in order. They are
**educational schematics**: blocks, a pipeline, a register seam and a bus window. Not circuits, not to scale,
and not a floorplan — this part has no die photograph, so no layout is claimed anywhere.

| sheet | answers | look at it when |
|---|---|---|
| `00-blocks.svg` | what are the chip's parts, and what feeds what? | orientation; before any other sheet |
| `01-word.svg` | how does one word get from source to destination, and what does it cost? | any question about reads, skew, HOP/LOP, masks or fill |
| `02-registers.svg` | which fields live where, and what does the decode prove? | anything at `$FF8A00`–`$FF8A3D`, plus BUSY/HOG/SMUDGE/SKEW |
| `03-window.svg` | how does a blit share the bus, and does it fit in a frame? | timing, the 63/64 conflict, the frame budget, `BINT` |

## Reading order

`00` assumes nothing. `01` explains what one word costs and assumes `00`. `02` is the register map and is the
sheet to open when the study says "here, not in U400". `03` is time, and it is the sheet that corrects the
campaign's arithmetic: a word is 4, 8 or 12 clocks, not 4.

## The colour rule (shared with the 68000, GSTMCU and Shifter sets)

| colour | meaning |
|---|---|
| **blue** | an address: the address latches and adder, the register decode, the `$FF8Axx` window |
| **green** | data: the source word, the skew buffer, the destination word |
| **amber** | control: HOP, LOP, the end masks, the bus request |
| **violet** | clocking and state: the state machine, the grant chain, the interrupt path |
| **grey / violet fill** | storage, and things deliberately not modelled |

Line weight is bus width. A dashed line means "signals, not a physical wire" or "this is not here".

## What is evidence here

Each sheet carries its own provenance line; the study (`../blitter-implementation.md`) carries the labels and
the citations behind them.

- **`documented`** — the register map and every quoted field (Atari's *BLiTTER* manual, 25 Jan 1990); the
  block map (the fifteen sheet titles of `ST-4051`); the strobe names (`XSEL00…XSEL1E`, `XOPW`, `XHOPW`,
  `XSKEWW`, `XLINEW`, `XSMUDGE`, `XFXSR`, `XRAMR`/`XRAMW`); the pad ring's four arbitration nets; the cell
  census (58 types, 2,133 instances); the 64-access rule; "the interrupt line is a duplicate of this bit";
  `BINT`'s direction and the MFP's `GPIP3`.
- **`derived`** — the clocks table of `01` (4/8/12 from the manual's read rules and Hatari's constants), the
  frame budget of `03` (16,000 words, 8/16/24 ms and the non-HOG doubling), the 62-byte decode arithmetic, and
  the mask rules of the study's §4.7.
- **`unknown`, and drawn as such** — whether the 63-access case is a counting error or a design feature, which
  of the three revisions shipped, the grant chain's order between U400 and U101, and the one-pixel disagreement
  between Atari's appendix table and its own prose.

Nothing here is copied from a drawing. The sheets are redrawn from the structure the study establishes; the
study's §9 states the licence position and applies here unchanged. In particular the recovered designs'
*labels* are cited as text (they are the design's own net names), never reproduced as an image.

## Regenerating

```bash
python3 build_schematics.py && python3 check_overlaps.py
```

`build_schematics.py` writes the four SVGs, parses each one (a malformed file raises here rather than in a
browser), and audits its own labels: width estimated at a conservative 0.60 em per character, reported against
the box each label has to fit. `check_overlaps.py` is the second pass — estimated text bounding boxes and the
blocks themselves, plus anything off-canvas. Both print zero on a clean build; a warning is a build failure, not
a note. `cairosvg.svg2png(url=..., write_to=...)` makes PNGs; the sheets are 1440 units wide by design.
