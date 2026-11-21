# Five sheets, one chip

Standalone SVGs — open any of them in a browser, or open `index.html` to read them in order. They are
**educational schematics**: blocks, strobes, budgets and paths. Not circuits, not to scale, and not a
floorplan — this part has no die photograph, so no layout is claimed anywhere.

What is accurate is the *structure*, and each sheet says which part of it is evidence and which is
convention:

| sheet | answers | look at it when |
|---|---|---|
| `00-blocks.svg` | what are the chip's parts, and what feeds what? | orientation; before any other sheet |
| `01-cycle.svg` | what happens in one 500 ns cycle, phase by phase? | any timing question — RAS, CAS, LATCH, DCYC |
| `02-line.svg` | how does a line spend its 128 internal slots? | video/DMA budgets, blanking, refresh, sound |
| `03-address.svg` | where does the DRAM address come from? | any question about `MAD`, banks, or the mux table |
| `04-counters.svg` | what counts, on which clock, and how wide? | refresh cadence, video/sound counters, pen, paddle |

## Reading order

`00` assumes nothing. `01` and `02` are about time and can be read in either order but assume `00`.
`03` assumes `00` and is the sheet to open beside `nets.md`. `04` assumes `01` (it explains where the
seconds that `01` divides come from).

## The colour rule (shared with the 68000 set)

| colour | meaning |
|---|---|
| **blue** | an address: the CPU bus, `VA`, `MA`, `RA`, `MAD0–MAD9` |
| **green** | data: the video fetch buffer, the sound address path, the pad port |
| **amber** | control: decode, selects, `RAS`/`CAS`/`WE`, interrupts |
| **violet** | clocking: the divider ladder, `TGEN`, the counters' clocks |
| **grey / violet fill** | storage, and things deliberately not modelled |

Line weight is bus width, and a dashed line means "signals, not a physical wire" — the drawings mix a
block diagram and a timing chart, so dashes keep the two from being misread as one.

## What is evidence here

Each sheet carries its own provenance line. In summary:

- **`documented`** — net names, pad designators, the printed mux table (sheet 23), the chart's levels
  (sheet 48), the counter widths.
- **`derived`** — the phase width (31.25 ns, §5 of the study), the refresh cadence (2 µs), the line and
  frame arithmetic.
- **ruling** — what is drawn as absent: interlace, the test mode, `BINT`.

Nothing on these sheets is copied from the archive. They are redrawn from the design's connectivity as
transcribed in `../research/gstmcu-silicon-implementation.md` — see that study's §10 for the licence
position, which applies here unchanged.

## Regenerating

```bash
python3 build_schematics.py
```

The script emits the five SVGs and then audits its own output: it estimates every string's width at a
conservative 0.60 em per character and reports any label that would overflow its box. A clean run prints
`0 layout warnings`. To make PNGs (for a slide or a mail), `cairosvg.svg2png(url=..., write_to=...)` on
each file is enough; the sheets are 1440 units wide by design.
