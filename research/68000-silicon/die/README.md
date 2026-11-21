# The die images, and how to look at them

Four photographs and one annotation of the same chip. They are not interchangeable — the differences matter, so this
page says what each one is good for before saying how to read it.

| file | what it is | resolution | good for |
|---|---|---|---|
| `byte-1983-annotated.jpg` | **start here.** The 1983 *Byte* die photograph, with Motorola's own block names, upscaled 3× and re-annotated with 19 numbered callouts and a legend. | 3100 × 2196 | finding your way around: which block is which |
| `byte-1983-labelled-x3.jpg` | the same photograph, upscaled, **without** my callouts — the original yellow labels only | 1920 × 2196 | reading Motorola's own label text |
| `mc68000l12-die-commons.jpg` | Pauli Rautakorpi's MC68000L12 (mask 2A72E) die photograph, CC BY 3.0 | 3651 × 3228 | a clean, modern, un-annotated view at usable zoom |
| `visual6502-top-1600.jpg` | Visual6502's metal-layer photograph | 1600 × 1808 | the metal: power rails, the ROM bit lines, the pad ring |
| `visual6502-nometal-1600.jpg` | the same die after de-layering — diffusion and polysilicon exposed | 1600 × 1808 | seeing *cells*: ROM grids, PLA planes, register columns |

Licences and provenance are in [`SOURCES.md`](SOURCES.md). Nothing here is a schematic: these are photographs, and the
annotations are overlays drawn on them.

---

## 1. Open the annotated photo first

Open `byte-1983-annotated.jpg` in any image viewer and **fit it to the window**. At that size you are looking at a
7 mm by 6 mm piece of silicon and you can already see the four things that matter:

* a **ring of bond pads** around the edge (64 of them, one per pin);
* a band of **regular, grid-like blocks** in the upper middle — the two control stores;
* a band of **thin vertical strips** to their right — the PLAs;
* a **row of three large blocks** along the bottom — the execution unit, split into address-high, address-low, data.

Read the numbered legend on the right of the image. Numbers 6 and 7 are the two ROMs; 15, 16, 17 are the three
execution sections; 8, 9 are PLAs; 11 is the PLA bank that everyone mislabels as a bus.

## 2. Then zoom in, in this order

The photograph is only 640 × 732 pixels at source, so zooming is honest only up to about 4×. Beyond that you are
looking at interpolation, not silicon. For real detail switch to the modern photographs (step 3).

1. **1:1 on the annotated image** — the labels become legible, and the yellow outlines Byte drew become visible. Notice
   that the outlines are *not* rectangles everywhere: PLA boundaries are stepped, because a PLA's AND plane and OR
   plane have different widths.
2. **2:1** — the ROM blocks resolve into vertical stripes. Those stripes are the bit lines: seventeen of them, one per
   bit of a microword, running the height of the array.
3. **3:1–4:1** — individual ROM cells appear as a regular lattice. The taller of the two arrays is the nanocode
   (84 address rows of 4 addresses each); the shorter is the microcode (34 rows of 16). Seventeen bit-line columns run
   through both, because a microword is 17 bits and a nanoword is four 17-bit slices
   (see `../m68000-silicon-implementation.md` §8–§9).

## 3. For real magnification, use the modern photographs

`mc68000l12-die-commons.jpg` is 3651 × 3228 and `visual6502-top-1600.jpg` is 1600 × 1808. The visual6502 project also
publishes the same die at **6500 × 7344** and **10000 × 11299** (up to 223 MB), plus the de-layered variants and a
polygon-region map:

```
http://visual6502.org/images/68000/Motorola_68000_die_20x_1a_top_10000w.png
http://visual6502.org/images/68000/Motorola_68000_die_20x_1c_noMetal_10000w.png
http://visual6502.org/images/68000/Motorola_68000_die_20x_1a_top_map_6500w.png
```

Fetch those two 10000-pixel files into this directory if you want to trace metal by hand — at that scale a single
transistor is about 12 pixels across, which is the minimum useful for reading logic.

At those magnifications, on the **no-metal** image, the four texture classes are unmistakable:

| texture | what it is | where |
|---|---|---|
| a dense, perfectly regular lattice of identical cells | a ROM bit array | the two control stores |
| long parallel lines with a ladder of identical transistors between them | a PLA's AND plane (and its OR plane alongside) | A1, A2/A3, branch, trap, IRD decode |
| long straight parallel stripes with periodic taps | the register file and the bus lanes | the three execution sections |
| irregular, varied, densely packed shapes | random logic — the sequencer, the flags, the control distribution | between the blocks |

## 4. What you are actually looking at

Three cautions that save time:

* **The 1983 photo and the modern photos are different dies.** The *Byte* figure is of an early part; the Commons photo
  is an MC68000L12, mask 2A72E; Visual6502 photographed yet another package. The layout is the same to within mask
  revisions, but do not expect the annotations to land pixel-perfectly on the modern images — that is why the numbered
  overlay is only on the 1983 photo.
* **Metal hides everything.** On `visual6502-top-1600.jpg` the top metal layer covers the transistors. You can read the
  ROM *bit pattern* (the presence or absence of a contact) but not the logic. The `nometal` image is the one to reason
  about circuits on.
* **No authoritative overlay exists for the modern photographs.** The only manufacturer-labelled overlay is the 1983
  one, and the only machine-readable one is Olivier Galibert's 6-layer SVG trace (34 MB, needs ~5 GB of RAM in
  Inkscape) — see `../m68000-silicon-implementation.md` §3.

## 5. How the numbered overlay was made

`annotate.py` in this directory. The rectangles were **not** eyeballed: the 1983 figure already draws a yellow outline
around every named block, so the script detects yellow pixels, scores each candidate edge line by the fraction of its
length that lies on a yellow line, and snaps each box edge to the best line within ±16 px of a starting guess. The
boxes therefore follow the printed outlines, and the legend text is mine. Where two adjacent blocks share a printed
edge, that line is drawn once, so a box side can sit a few pixels off its neighbour; the coverage numbers the snapping
pass reports (`1.0` = the whole side lies on a printed line) show which sides are strongly supported.

```bash
python3 annotate.py            # rewrites byte-1983-annotated.jpg
```

If you disagree with a box, edit its coordinates in `BLOCKS` at the top of the script — they are in the magazine
photograph's own 640 × 732 pixel coordinates.
