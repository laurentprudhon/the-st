# Die image sources and licences

| file here | original | author / rights | terms |
|---|---|---|---|
| `mc68000l12-die-commons.jpg` | https://commons.wikimedia.org/wiki/File:Motorola_68000_die.JPG (3651 × 3228, re-encoded at quality 88) | Pauli Rautakorpi, own work | **CC BY 3.0** — attribution required; see the Commons page for the licence text |
| `visual6502-top-1600.jpg` | http://visual6502.org/images/68000/Motorola_68000_die_20x_1a_top_1600w.png | Visual6502.org project; metal-trace isolation by Quietust | Visual6502 publishes its die photographs for reuse in computer-history work; credit the project and Quietust's metal isolation. Check the site's own terms before commercial use. |
| `visual6502-nometal-1600.jpg` | http://visual6502.org/images/68000/Motorola_68000_die_20x_1c_noMetal_1600w.png | Visual6502.org project | as above |
| `byte-1983-labelled-x3.jpg` | Thomas W. Starnes, "Design Philosophy Behind Motorola's MC68000", *Byte*, May 1983, p. 353 ff.; scan hosted at http://www.easy68k.com/paulrsm/doc/dpbm68k2.htm | © Byte Publications / McGraw-Hill 1983; photograph by Motorola | **Research and teaching use only.** Not a free licence. If this repository is ever published under an open licence, replace this file with the Commons photograph and keep the annotation geometry — see `annotate.py`. |
| `byte-1983-annotated.jpg` | derived from the file above | overlay by this repository | inherits the restriction of the source photograph |

## Why the 1983 photograph is here at all

It is the only die photograph of the 68000 that carries **the manufacturer's own block labels** — the captions were
written by a Motorola engineer, and the outlines were drawn on the die by the magazine. No equivalent exists for the
Visual6502 or Commons photographs, so every modern analysis ends up referring back to this figure. It is reproduced
small, at low resolution, for identification and commentary.

## What is *not* here, and why

* **The 6500 × 7344 and 10000 × 11299 Visual6502 images** (18–223 MB each). URLs are in `README.md`; they are worth
  fetching into this directory if you intend to trace, but they are too large to keep in git.
* **Olivier Galibert's vectorised layer stack and generated schematic** (`layers.svg` 34 MB, `schem.svg` 23 MB,
  `schem.txt` 4.9 MB, netlist 2.8 GB expanded). Originally at `og.kervella.org/m68k/`; that host no longer answers —
  use the archived copy: https://web.archive.org/web/20260728132147/https://og.kervella.org/m68k/
* **John McMaster's de-layered series**, which is the source Galibert worked from: offered at his request, mirrored
  in the same archive under `original/` and `aligned/`.
