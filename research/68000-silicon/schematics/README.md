# Five sheets, one machine

Standalone SVGs — open any of them in a browser, or open `index.html` to read them in order. They are **educational
schematics**: functional blocks and paths, not circuits, and deliberately not to scale. What is accurate is the
*structure*: which unit feeds which, what is 16 bits and what is 32, where the sequencing loops back, and what is
control and what is data.

| sheet | answers | look at it when |
|---|---|---|
| `00-floorplan.svg` | where is everything on the die? | you are about to open a die photograph |
| `01-datapath.svg` | how do three sections and two buses make a 16-bit machine behave like a 32-bit one? | you want to know why a 32-bit add costs two passes |
| `02-control.svg` | how does an opcode become ~200 control lines? | anything about microcode, PLAs, or "why this clock here" |
| `03-timing.svg` | what is a clock, a microcycle, a bus cycle, a wait state? | instruction timing, or the E clock |
| `04-word-formats.svg` | what is in a 17-bit microword and a 68-bit nanoword? | reading the microcode dumps |

## Reading order, and what each sheet assumes

`00` assumes nothing. `01` assumes you know the programmer's model. `02` assumes `01` (it drives the blocks drawn
there). `03` is independent of `01`–`02` and can be read first if timing is what you care about. `04` assumes `02`.

## The colour rule

The same five colours mean the same thing on every sheet:

| colour | meaning |
|---|---|
| **blue** | an address path: a microcode address, an operand address, the address bus |
| **green** | a data path: an operand, an instruction word, the data bus |
| **amber** | control: decode, sequencing fields, register strobes, bus access class |
| **violet** | clocking and exception entry — the things that are neither data nor address |
| **grey / violet fill** | storage: a latch, a register, or a ROM array |

Line weight is bus width: a hairline is one bit, a thin double line is 16, a thick double line is 32.

## Two conventions worth knowing before you read them

1. **Names follow the patent, not the emulator.** `AOB`, `AT`, `DT`, `AU`, `ALU`, `ALUB`, `ALUE`, `FTU`, `DCR`,
   `PREN`, `DBIN`, `DOB`, `IRC`, `IR`, `IRD`, `PSW`, `SSW`. Why those and not others: `../nomenclature.md`.
2. **The diagrams are drawn in the orientation of the 1983 die photograph**, so `00` doubles as the legend for the
   photographs in `../die/`. If you compare with a schematic drawn by someone else, expect a rotation.

## Regenerating

```bash
python3 build_schematics.py
```

The script emits all five SVGs and then audits its own output: it estimates every text string's width and reports any
label that would overflow its box or run off the canvas. A clean run prints `0 layout warnings` for each sheet. Fonts
are whatever the system has (`Iosevka` → `JetBrains Mono` → `SF Mono` → `Menlo` → `Consolas` → monospace), so text
widths differ slightly between machines; the audit uses a conservative 0.60 em per character.

## Printing

`index.html` is set up for print: A3 landscape at 100 % fits `00`, `01`, `02` and `03`; `04` fits A3 portrait. The
SVGs also scale to A4 legibly if you accept 8-point type on the smallest labels.
