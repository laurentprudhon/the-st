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
- [x] **3. Atoms.** (`M68000.Micro.cs` → `M68000.ControlStores.cs`, the two stores' vocabulary, documented as MicroStore/NanoStore blocks) `M68000.ControlStores.cs` with `NanoStore` — the elementary operations (transfers, the AU
  constant select, the ALU mode and its flags, the idle state, the queue helpers) — and the IRD-keyed bank's
  fields (`RegisterFile(rx)`, the size, the vector) as named methods over the atoms.
- [x] **4. Scripts.** (`M68000.Scripts.cs` is already one method per shape, and it calls the ControlStores vocabulary; nothing to move) One script per shape, named by the shape the microcode names (`SingleOperandLongAsync`,
  `AluLongShapeAsync`, …), each calling only `NanoStore` methods — no raw field arithmetic left in a script.
- [x] **5. Decode.** (+ the seam nested as `M68000.SingleStep`, the freeze row live in `validate.sh` and `SURFACE_MODE=freeze`, 0 divergences) (`M68000.Decode.cs`: `Line` + `Family` + 78 predicates; `Instruments.Instruction`/`Family`/`BusState`/`EntryStep`/`Interrupt`; witnessed 303 606/303 606 records) `Line` + `Family` + 78 `public static bool <Family>(ushort, out Decoded)` whose names are
  the manual's; a `Line` classifier above them; `Instruments.Family`, `Instruments.Instruction` (the manual's
  syntax, composed on read) and `Instruments.BusState`/`EntryStep`/`Interrupt`; the freeze test's
  `SURFACE_MODE` flips to `freeze`.

## Rules that must not break

- The corpus's two columns (317 500 state **and** exact-clock), the fold pair and G8 are the verdict at every
  step; a red row means the step is not landed.
- No behaviour change anywhere in this work: it is a re-expression. A moved fold is a bug, not a re-bless.
- A step that cannot be finished is reverted, not committed half-done.

## 8. The owner's rulings (2026-11-27) and where the work stands

**A — all accepted**: `SSW`, `INL`, `TPEND`, `PREN`, `FTU` as latches; `DCR` as the derived decoder; the
access label; the AU's transfers. **B**: `B1` accepted (the AU is the MUL/DIV counter) — with **C3**, the
AU/PC split — so **B2** (AUH/AUL, the two half-adders) comes with it; `B5` accepted *if it costs no
performance* (the answer instant already stretches the cycle, so what is missing is the S0–S7 view, not
the timing); `B3` (sections/lanes) not needed for the two adders — the split is what he wants, the lanes
are a bandwidth feature with no transfer to carry. **C**: all three accepted; **C2** is the *narrow* FTU
(size/mode/register/vector), which is enough for B1.

**B8, answered**: `NMA` is the 10-bit next micro-ROM address (Format I), `TY` selects the *source* of the
next address (NMA, A1, A2, A3), and `CBC` is the 5-bit condition code of a conditional word (Format II)
whose `NMBA` base has bits 7:6 replaced by the condition logic's C1/C0 — the 4-way branch. They are why a
microcoded instruction can branch on `auz`/`nz`/`d4`/`enl`. Our scripts are that control flow as C#
(`if`/call order), so there is nothing to implement.

**Landed so far**

- [x] `#87` **DCR + the AU's transfers** — `Decoder4To16`, `AuPlusTwo`, `AuPlus`, `AuEqualsAt`, `LatchAob`.
- [x] `#87` **`SSW` is a latch** — `_ssw`, written by one composition (`ComposeSsw`) that the frame reads.
  Honest limitation: the write site is the frame, not the microinstruction (moving it is #93's); an
  attempt to latch it per bus cycle cost 50 752 records, because a non-bus-fault exception must keep the
  *data* class of the last operand access, not the fetch's — measured, not guessed.

**Still to take, in this order**

1. [x] `INL`/`TPEND` (`C1`) — landed: `_inl` latched at the boundary sample and read by both sampling
   sites; `_tpend` set from `T` at the boundary and consumed by the trace exception (vector 9), which
   now exists. The folds did not move (the boot never sets `T`), so the trace path is latent and correct.
2. [x] `PREN` — the MOVEM walk. `_pren` holds the list, `StepPren`/`StepPrenReversed` are its `reset
   pren`, and both directions are `while (!PrenEmpty)`: 5 000 records green on both columns, sample
   identical to the parent, G8 median +0.12 ms against `3343c0b` at n 751.
3. the **AU/PC split** (`C3`) then the **narrow FTU** (`C2`) then the **AU as the MUL/DIV counter** (`B1`),
   then `AUH`/`AUL` (`B2`) and the constants ROM.
4. the **access label** (`A7`) — an enum over the script calls.
5. `#88`: the `S0`…`S7` **view** from the cycle's position (the wait-stretch is already the answer
   instant), the byte lane, DTACK/BERR sampling.
6. `#89`: the **65 536-opcode golden** and the term-level decode (the `--families` audit is the entry).
