# The 68000 on silicon — pictures, names, and five schematics

This directory collects the three things that make the 68000's physical machine readable without a microscope and
without a patent-lawyer:

```
die/          four die photographs, and one of them annotated with 19 numbered blocks
nomenclature.md   what every internal part is called, who named it, and which names mislead
schematics/   five educational SVGs: the die, the datapath, the control path, time, the control words
```

The analysis behind all of it — where the parts came from, what was reverse-engineered, and which published claims
conflict — is `../m68000-silicon-implementation.md`.

---

## 30-second version

* **Die photographs.** Two working sets exist: Visual6502's (metal layer only, up to 10000 px) and John McMaster's
  de-layered series made at Olivier Galibert's request (the one every analysis is built on). The only photograph with
  the manufacturer's own labels is the 1983 *Byte* figure — that is the one annotated here.
* **Names.** Architectural names (`D0–D7`, `SR`, `AS`, `DTACK`) are canonical and universal. Microarchitectural names
  (`AOB`, `AT`, `DT`, `AU`, `ALU`, `FTU`, `PREN`) are canonical too — but the canonical source is *Motorola's own
  patents*, not the manual, and the die-first literature invented a second set (registers "A" to "G") that maps onto
  it one-to-one. Signal names are not canonical at all.
* **The schematics** exist because no published diagram shows data, address and control paths at more than one scale.
  These do, with one colour convention across all five.

---

## How to look at them

**1. Start with the annotated die** — `die/byte-1983-annotated.jpg`. Fit to screen: you will see the pad ring, the two
ROM blocks, the PLA strips beside them, and the three execution sections along the bottom. The right-hand panel is the
legend. Then read `die/README.md` §2 for the zoom order and §3 for the URLs of the 10000-pixel originals.

**2. Read the sheets in order** — open `schematics/index.html`, or the SVGs directly:

| sheet | one thing it teaches |
|---|---|
| `00-floorplan.svg` | the die's arrangement, and which block is which |
| `01-datapath.svg` | 3 sections × 2 buses = six 16-bit transfers per cycle |
| `02-control.svg` | opcode → 3 PLAs → 10-bit address → both ROMs → 200 control lines |
| `03-timing.svg` | 2 clocks per microword, 4 per bus cycle, wait states stretch the word |
| `04-word-formats.svg` | the 17-bit and 68-bit words, field by field |

**3. When a name surprises you** — `nomenclature.md`. It has the three-strata argument, the correspondence table
(manual ↔ patent ↔ die analysis ↔ emulator), and the six acronyms that actually cause mistakes (`NROM`, `IRD BUS`,
`A0`/`A0S`, `AU`, `DCR`, `PSW`/`SR`).

---

## Two things this directory deliberately does not do

* **It does not claim pixel-exact geometry.** The numbered boxes follow the outlines printed on the 1983 photograph
  (they were snapped to those lines algorithmically), but no public source maps block boundaries onto the modern
  photographs, and the two sets are different dies.
* **It does not redraw the machine as circuits.** No gate is shown. A reader who wants gates should go to Galibert's
  netlist and schematic — links in `die/SOURCES.md` — and expect 2.8 GB of extracted circuit data.

## Regenerating the artefacts

```bash
cd die && python3 annotate.py             # the numbered overlay
cd ../schematics && python3 build_schematics.py   # the five sheets, plus a layout audit
```

Both scripts are stdlib + Pillow, and both print what they wrote. The schematics script fails loudly if any label
would overflow its box.
