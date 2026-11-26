# The core, bottom up: the refactor's plan and progress

The owner's instruction (2026-11-21): refactor the 68000 so the code is the silicon's own shape, bottom up —
storage and buses, then the nanocode's atoms, then the microcode's scripts, then the decode. At every level a
function combines fields or functions of the level below; at the decode level the manual's instruction names
appear in the function names, and the emulator's view exposes the instruction being executed.

Vocabulary (settled this session, and it is now the code's):

- **`Line`** — the 16 blocks of the opcode's top nibble (manual §10's "family selected by the top nibble";
  the microcode dumps' word). Not architectural: it is the A1 PLA's coarsest term, an *address partition*.
- **`Family`** — one of the manual's 78 instruction families (§5 tables). This is what `Decode` names.
- `Family` never means a line, and `Line` never means a family, in any file or comment.

Levels, bottom → top, and the file each lands in (`design/cpu-silicon.md` §1 is the map):

| level | what it is | file | step |
|---|---|---|---|
| 0 | the register file, the three sections' latches, the pad buffers | `M68000.Registers.cs` (+ `Pins`) | 1–2 |
| 1 | the nanocode's atoms and the IRD-keyed bank's fields | `M68000.ControlStores.cs` (`NanoStore`) | 3 |
| 2 | the microcode's words, one script per shape | `M68000.Scripts.cs` (exists) | 4 |
| 3 | the decode: `Line` → `Family` → script, and `Instruments.Instruction` | `M68000.Decode.cs` | 5 |

Every step is gated by the campaign's instrument and only committed when green:

```sh
bash scripts/cpu-shape-check.sh 82b5580     # corpus (both columns), folds, G8, interface
```

## Progress

- [x] **1. Storage.** (`Registers.cs`, `_pc`→`_au`, `Set16H`/`Set16L` used at 15 write sites) `M68000.Registers.cs`: the register file (`D0–D7`, `A0–A6`, `USP`, `SSP`, `AU`, `PSW`),
  the pipeline cells (`IRC`/`IR`/`IRD`), the ALU latches (`ALUO`, `ALUB`, `ALUE`, `ISR`, the width select) and
  the read latch (`DBIN`), each with its patent name and sheet; `Set16H`/`Set16L` as the 16-bit section
  accessors (tutorial §1.1's rule). `_pc` becomes `_au` — the hardware's program counter, with `PC` the
  manual's view of it.
- [x] **2. Buses and pads.** (`_irAddress`→`_at`; `AOB`/`DOB`/`DT` written by `Cycle`; `Pins` reads the buffers) `_aob`, `_dob`, `_at`, `_dt` become latches written by the bus cycle; `Pins` reads
  them instead of decoding the bus record group (the pins are the interface; the record group is how the
  interface is transported).
- [ ] **3. Atoms.** `M68000.ControlStores.cs` with `NanoStore` — the elementary operations (transfers, the AU
  constant select, the ALU mode and its flags, the idle state, the queue helpers) — and the IRD-keyed bank's
  fields (`RegisterFile(rx)`, the size, the vector) as named methods over the atoms.
- [ ] **4. Scripts.** One script per shape, named by the shape the microcode names (`SingleOperandLongAsync`,
  `AluLongShapeAsync`, …), each calling only `NanoStore` methods — no raw field arithmetic left in a script.
- [ ] **5. Decode.** `Line` + `Family` + 78 `public static bool <Family>(ushort, out Decoded)` whose names are
  the manual's; a `Line` classifier above them; `Instruments.Family`, `Instruments.Instruction` (the manual's
  syntax, composed on read) and `Instruments.BusState`/`EntryStep`/`Interrupt`; the freeze test's
  `SURFACE_MODE` flips to `freeze`.

## Rules that must not break

- The corpus's two columns (317 500 state **and** exact-clock), the fold pair and G8 are the verdict at every
  step; a red row means the step is not landed.
- No behaviour change anywhere in this work: it is a re-expression. A moved fold is a bug, not a re-bless.
- A step that cannot be finished is reverted, not committed half-done.
