# The core's shape: the die's blocks, as partial files

**Accepted** with [ADR 0015](../docs/adr/0015-the-core-is-the-silicon.md) (2026-11-21). #84–#94 fill the
sections marked *to be filled*; this file is the operational view of the ADR — which block lives in which
file, in which order they may call each other, and what each latch is called.

**The grouping is the owner's.** A file is a block of the die, and the die's own bands order the files. The
floorplan's summary is this map's table of contents:

> · top — clocks, bus control, opcode decode
> · middle — the two control stores
> · bottom — the execution unit

plus the pads at the edge, and one file that is not the part at all: the emulator's interface.

Sources, by sheet (`research/68000-silicon/schematics/`):

| sheet | what it is | what it settles here |
|---|---|---|
| `00-floorplan.svg` | where everything is on the die | **the block list and the file split** — every block named below is that sheet's own label |
| `01-datapath.svg` | three sections, two buses, six lanes | `Registers`, `Address`, `Data` |
| `02-control.svg` | opcode → ~200 control lines | `InstructionRegisters`, `Decode`, `Sequencer`, `Exception`, `Bus`, `ControlStores` |
| `03-timing.svg` | clock, microcycle, bus cycle, wait state | `Clock`, `Bus` |
| `04-word-formats.svg` | a 17-bit microword, a 68-bit nanoword | `Sequencer`, `ControlStores` |

The colour rule is the README's: blue address, green data, amber control, violet clocking and exception
entry, grey storage.

## 1. The partial-file map — the floorplan, block by block

Fourteen files, four of which exist today. Every row is a block `00-floorplan.svg` names (or two the die
draws as one region), and the file *is* that block.

### The edge — pads and buffers (sheet 00, grey)

| file | die block(s) | what it owns | ticket |
|---|---|---|---|
| `M68000.Pins.cs` | `ADDRESS BUFFERS → A1–A23`, `DATA BUFFER → D0–D15`, `BUF INPUTS` | the nested type `Pins` (one `bool` per §8.1 signal), the buffers between the pads and `AOB`/`DOB`/`DBIN` | #85, #88 |
| `M68000.Clock.cs` | `CLOCK GENERATORS`, `DECODE DRIVERS` | the microcycle's names (φ1/φ2; two clocks a microword, four an access) and the phase fan-out | #90, #94 |

**`E` and the 6800 group are a level and nothing else** (the owner's ruling): no divider, no ten-state
counter, no "TIMER & E" block. The ST does not use them (tutorial §8.1), so `Pins.E`/`VPA`/`VMA` exist
because §8.1's list does, read false, and nothing drives them.

### The bottom band — the execution unit (sheet 01)

| file | die block(s) | what it owns | ticket |
|---|---|---|---|
| `M68000.Registers.cs` | the register file, drawn across `HIGH` / `LOW` / `DATA` | `D0–D7`, `A0–A6`, `USP`, `SSP`, `PC` (`AU`), `AT`, `DT`, `AOB`, `DBIN`, `DOB`, `ALUO`, `ALUB`, `ALUE`, `ISR`, `ALUWidth`, `IRC`/`IR`/`IRD`, `PSW`, `SSW`, `INL`, `TPEND`, `PREN`, `FTU` — **one file, storage only**, one 32-bit field per register with `Set16H`/`Set16L` as the section accessors (ADR 0015 §3) | #86, #87 |
| `M68000.Address.cs` | `ADDRESS HIGH` (high words + `AUH`), `ADDRESS LOW` (low words + `AUL`), the address bus `AB(h)`/`AB(l)`/`AB(d)` and its couplers | the 32-bit adder as two 16-bit halves, the constants ROM (0, ±1, ±2, +4, +8, size), the lanes of `AT`/`AOB`, the named transfers (`AuEqualsAt`, `AuPlusTwo`, `CopyAuToPc`, `LatchAob`) | #87, #91 |
| `M68000.Data.cs` | `DATA` (low words + the ALU), the data bus `DB(h)`/`DB(l)`/`DB(d)` | the ALU's 15 × 5 matrix (row from the `IRD` PLA, column from three nanocode bits, the initiate/finish bit, `INHCC`), `ALUB`, `ALUE`, `DCR`'s one-hot line, `PREN`'s walk, `FTU`'s fields, `DBIN`/`DOB` | #91, #92 |

### The middle band — the two control stores (sheets 02 and 04)

| file | die block(s) | what it owns | ticket |
|---|---|---|---|
| `M68000.ControlStores.cs` | `µROM 544 × 17`, `NROM 336 × 68` (66 driven) | **two classes, elementary methods only, no data**: `MicroStore` — the 17-bit word's fields as operations (`NextByNma`, `NextByA1/A2/A3/A0/A0S`, `LoadIr`, `LoadIrd`, the access class's `i`/`f`/`n`/`t` × mode × type) and the two word formats (`FormatI`, `FormatII`); `NanoStore` — the 68 bits as operations (the transfers and destinations, the `AU` constant select, the mid-state field, the ALU mode, the flag masks — today's `M68000.Micro.cs`) | #90, #91 |
| `M68000.Scripts.cs` | `µROM` — its words, unrolled | one `async` method per microword state, one method per shape, the dispatcher that turns a microcode address into a state (`IsScripted<Family>` is the ledger), and **the comments**: every microcode line's section prefixed by its ROM address and state with the running clock total (`// 0da morw1   2 clocks`), every group of statements prefixed by the nanoword line it decodes | — |

`MicroStore` and `NanoStore` are the two stores' *vocabularies* — the methods the scripts are written in. The
content of the ROMs is the code itself (ADR 0015 §10): a `ushort[]` of control words, a `switch` over 544 arm
numbers or any data-driven dispatch is the interpreter returning, and #78 refused it.

### The top band — clocks, bus control, opcode decode (sheet 02)

| file | die block(s) | what it owns | ticket |
|---|---|---|---|
| `M68000.InstructionRegisters.cs` | `IRC` / `IR` / `IRD`, with the `load IR` / `load IRD` strobes | the three-deep pipeline: the next word, the word being decoded, the word being executed, and the two strobes that shift it | #87 |
| `M68000.Decode.cs` | `A1 DECODE` (PLA, 149 terms), `A2 / A3 DECODE` (shared AND plane), `ILLEGAL · PRIV`, `IRD REG`, `IRD BUS` (the decode PLA bank) | an entry point per `A1` term, the 20 EA-keyed terms, `IOC`/`PRIV`, the `IRD`-keyed bank (~130 of the ~200 control lines) — **and the public face**: `Decode`'s one function per instruction family and the `Family` enum (ADR 0015 §1) | #89 |
| `M68000.Sequencer.cs` | `MICROCODE ADDRESS SELECTOR` (`NMA · A1 · A2 · A3 · A0 · A0S`), `BRANCH PLA` / `BRANCH UNIT` | the address selection among the sources, the four-way branch (`C1`/`C0` replacing bits 7:6), the 10-bit registered address, and the charge that lives in a state (ADR 0015 §5) | #90 |
| `M68000.Exception.cs` | `A0 MUX CONTROL` / `EXCEPTION LOGIC`, `TRAP & ILL. PLA` | the two override addresses (`A0` at a boundary, `A0S` now), the vector selection, `SSW`'s two lives, the two frame orders, `INL`/`TPEND`'s latching, and `Instruments.EntryStep`/`Interrupt` | #93 |
| `M68000.Bus.cs` | `µP CONTROL LOGIC`, `BUS CONTROL` (bus state machine `S0–S7`, `DTACK · BERR · arbitration`), `INT LOGIC`, `FC LOGIC` | the eight states and the wait rule, the strobes and the byte lane, the access class, `DTACK`/`BERR` sampling, `FC0–FC2` from the class, the `IPL` sampling, and `Instruments.BusState` | #88 |

### The chip, the seam, and the emulator's own face

| file | what it owns | ticket |
|---|---|---|
| `M68000.cs` | the chip: the constructor, `Launch`, the drive loop, reset, and the boundary (interrupt sampling, trace, the decode step) | #85 |
| `M68000.SingleStep.cs` | the corpus seam: `Load`/`Capture`/`StepAsync`, `SingleStepState`/`SingleStepResult` | #34, unchanged |
| `M68000.Emulator.cs` | **the emulator interface — not the part**: the nested `Instruments` (the five counters, `TicksPerCpuClock`, `Halted`/`HaltReason`, `Observer`, and the `#if DEBUG` `CycleObserver`/`ClockObserver`/`LastFrame*` columns) and the chip time the simulation advances. ADR 0015 §6's file, and the **only** place a time field may live (§5.1) | #85 |

**Occupied, then empty** — what the map does not keep, and why:

| file | today | end state |
|---|---|---|
| `M68000.Micro.cs` | the elementary operations, `IdleAsync`, the queue helpers, six latches | becomes `NanoStore` in `M68000.ControlStores.cs` (the atoms); its latches go to `Registers`, its charge to a script's local (#91) |
| `M68000.Timing.cs` | the shape table's `_own` charges | **empty by #90**; its last row (`RESET`'s) goes under #93 |
| `M68000.Instructions.cs` | the line handlers | **dissolved by #89** into `Decode` (the terms) and `Scripts` (the bodies) |
| `M68000.Datapath.cs`, `M68000.Counters.cs` (draft 1) | — | do not exist: the datapath *is* the three sections, and the four things that count are die blocks — `AU` in `Address`, `DCR`/`PREN`/`FTU` in `Data` |

## 2. The dependency direction

**A file may use only files at a *lower* band, never its own band or above, with one ordered chain inside the
top band. No cycles, ever.** The bands and their order are the die's: control flows down from the decode to
the stores to the execution unit; the pads are the edge.

```
4  M68000.cs            the chip: the loop, reset, the boundary
3  SingleStep.cs        the seam (enters at the boundary)
2  the top band         InstructionRegisters -> Decode -> Sequencer -> Exception
                        Bus    the bus state machine: driven by the class, reports DTACK/BERR
                        Clock  nobody calls it: it names the phases and fans them out
1  the middle band      Scripts (the µROM's words) -> ControlStores (MicroStore / NanoStore)
0  the bottom band      Data / Address -> Registers
-  the edge             Pins (the buffers and the pads; nothing below them)
*  M68000.Emulator.cs   nothing in the core calls it: it reads the core, and it owns time
```

Four edges worth stating, because each looks like a cycle and is not:

- **The sequencer never calls a script.** It *computes an address* — from `NMA`, or `A1`/`A2`/`A3`, or the
  overrides `A0`/`A0S` — and the dispatcher in `Scripts` turns that address into a state. That is the
  hardware's shape (`m68000-silicon-implementation.md` §7), and it is why the top band sits *above* the
  stores: an address is a value that travels down, not a call that travels up.
- **The decode produces an address, not a call.** A term's arm names the microcode address it selects
  (`EnterMoveByte` → `0x0d8`); the body it reaches is a script state, reached through the dispatcher.
- **The bus never calls upward.** A cycle starts, waits, is answered, returns; the fault it reports is `SSW`
  and `AOB`, both latches in `Registers`. `BERR`'s rerun and the double fault stay out of scope.
- **`Clock` is a leaf nobody calls**, exactly as `DECODE DRIVERS` fans phases out without knowing what they
  drive; and `M68000.Emulator.cs` is called by nothing in the core — the harness reads it, and it is the one
  file allowed to hold time and report.

## 3. The state table — latch → patent name → section → field → file

The inventory is ADR 0015 §4 (adopt / derive / delete / time / plumbing); this is the same inventory keyed by
**where it lives**. Section numbers are `m68000-silicon-implementation.md`; the name column is
`research/68000-silicon/nomenclature.md` §3's "recommended" one.

| field | name | section | file | note |
|---|---|---|---|---|
| `_d[8]` | `D0–D7` (High + Data halves) | §5.2 | Registers | one 32-bit cell; `Set16H`/`Set16L` are the manual's §1.1 rule |
| `_a[7]` | `A0–A6` (High + Low) | §5.2 | Registers | `A7` is the live `USP`/`SSP`, never an entry |
| `_usp`, `_ssp` | `USP`, `SSP` | §5.2 | Registers | the hidden one is reached by `MOVE USP` and by an exception entry |
| `_au` | `AU`, `AUH`, `AUL` | §5.2, §10.1 | Registers (`AU`) / Address (the halves and the adder) | **the hardware's program counter**; `PC` is a view |
| `_at` | `AT` | §5.2 | Registers (`AT`) / Address (its lane) | the address of the word in `IR`; a jump's target |
| `_dt` | `DT` | §5.2 | Registers | the last accessed address/operand |
| `_aob` | `AOB` | §5.2 | Registers (`AOB`) / Address (its lane) / Pins (`A1–A23`) | what goes out on the pads; a fault's address |
| `_dbin`, `_dob` | `DBIN`, `DOB` | §5.2 | Registers / Data (their lanes) / Pins (`D0–D15`) | the read's latch, the write's buffer |
| `_aluo`, `_alub`, `_alue` | `ALUO`, `ALUB`, `ALUE` | §5.2, §10.1 | Registers (`ALUO`/`ALUB`/`ALUE`) / Data (the ALU and the shift pair) | 16 bits each; the shift pair is `ALUO`:`ALUE` |
| `_isr` | `ISR` (the ALU's flag latch) | §5.2, §5.3 | Registers / Data (the matrix writes it) | a *stage* of `PSW`'s flags, not `PSW` |
| `_aluWidth` | the nanocode's `i=` field | §9, §10.2 | Registers (`ALUWidth`) / Data (the mode field) | the ALU's width select, held across states |
| `_irc`, `_ir`, `_ird` | `IRC` → `IR` → `IRD` | §5.3 | Registers (the cells) / InstructionRegisters (the pipeline and its strobes) | `IRD` is the word being executed |
| `_sr` | `PSW` (`SR` architecturally) | §5.3 | Registers | the status word |
| `_ssw` | `SSW` | §5.3 | Registers / Exception (its composition) | the bus-status word a fault frames — **not** `PSW` |
| `_inl`, `_tpend` | `INL`, `TPEND` | §5.2, §5.3 | Registers / Exception (their latching) | pending interrupt level; trace pending |
| `_pren` | `PREN` | §5.2, §6.2 | Registers / Data (the walk) | the `MOVEM` mask's position |
| `_ftu` | `FTU` | §5.2 | Registers / Data (the fields) | the instruction field being translated |

**Why a latch can appear in two columns**: the *cell* is the register file's (one 32-bit or 16-bit field), and
the *unit that drives it* is its section's (`Address`, `Data`) or its block's (`Pins`, `Exception`). That is
the die's own split — `AOB` is drawn in the register file, in the address lane *and* at the pads — and it is
why "one file per block" does not mean "one file per latch".

**Derived, and therefore not in that table**: the effective address, `CCR` over `PSW`, `A7` over
`USP`/`SSP`, `DCR`'s one-hot line, the frame's saved counter (whichever latch the script left), and every
`_frame*` memo field (ADR 0015 §4's derive column lists each one's replacement).

**Instruments, and therefore not in that table**: the seven `LastFrame*` columns are `#if DEBUG` readings of
`AU`/`AT`/`IRD`/`SSW`, living in `M68000.Emulator.cs`, and they back no latch of their own. *To be filled by
#86*: the exact write sites, one per column.

**Time, and therefore not in that table either**: a duration is not storage, its name carries its unit
(`…Clocks`, `…Ticks`), and it may live **only in `M68000.Emulator.cs`** (ADR 0015 §5.1). A script's charge is
a local in the script's own frame, so the common case has no field at all; if #90 finds one that must cross a
helper it is a `…Clocks` parameter first and a field in the emulator interface only if threading is worse.
`_eaCharged` is not time: it is a "has happened" bit, and the states' own position replaces it.

**The manual's concepts as display** (ADR 0015 §1.1), all reads, none stored, all on `Instruments`:

| member | values | produced by |
|---|---|---|
| `Instruments.Instruction` | the manual's syntax, e.g. `"MOVE.W D0,(A1)+"`; `"—"` at a boundary | composed **on read** from `IRD` and `Decode` — never per instruction (G8) |
| `Instruments.Family` | a `Family` member, or `null` at a boundary | `Decode`'s answer at the boundary |
| `Instruments.BusState` | `"S0"` … `"S7"` | `Bus` |
| `Instruments.EntryStep` | `"1 vector"` … `"5 load PC"`, empty outside an entry | `Exception` |
| `Instruments.Interrupt` | `"masked"`, `"taken"`, `"edge-triggered"`, `"autovector"`, `"spurious"`, `"none"` | `Bus` and `Exception` at the boundary |

## 4. The seam's vocabulary

| the seam says | the silicon says | why they differ |
|---|---|---|
| `SingleStepState.D[8]`, `A[8]` | `D0–D7`, `A0–A6`, `USP`/`SSP` | the corpus's records are the manual's shape |
| `SingleStepState.Pc` | `AU` at the boundary | the corpus's `pc` is where the next word is fetched (§1.4) |
| `SingleStepState.Sr` | `PSW` | the corpus compares the architectural word |
| `SingleStepState.Prefetch0/1` | `IRD`, `IR` | the corpus hands over the two words the hardware has already fetched |
| `Capture()` | `D`, `A`, `USP`, `SSP`, `SR`, `PC` — no latches | a record has no place for `AU`'s halves |
| `Load`/`StepAsync` | reset is *not* run; the boundary is entered with the pipeline full | one instruction in isolation |

**Who may see what.** `CpuTestsCommand.cs` (state, clocks, the frame columns), `BootCommand.cs` (the
programmer's model, the counters, `Halted`), `tools/cpu-dissection/Program.cs` (by reflection:
`Instructions`, `BusCycles`, `PC`, `SR`, `PeekD`→`D0`, `PeekA`→`A7`, from all four generated variants) and
`gen.py`'s two variants (which link `src/TheST.Machine/Cpu/*.cs` unchanged). A restructure that breaks the
dissection tool has broken the sources of record.

*To be filled by #85*: the freeze test — `tools/validation-harness/SurfaceCommand.cs`, run as
`validation-harness cpu-surface`, a row of `scripts/validate.sh` beside ADR 0014's falsifier row, with ADR
0015 §1's list as its allow-list and §1.1's rows as its display set.

## 5. What the corpus cannot see

| what | why the corpus is blind | the witness |
|---|---|---|
| the public surface's names | it grades registers, status, memory, clocks — never members | #85's freeze test; the boot's probes (a rename breaks them as an exception, not a red record) |
| a pin's level | nothing grades `Pins` | #88's byte-lane test, the boot's screen, and `M1` (the owner's eyes) |
| where a charge is flushed | a record's `length` is a total; the transactions carry the bus accesses only | `--trace N` against a record's `transactions`; `--clocks` grouped by shape. The local counter's **flush point** is exactly this: the corpus sees the total, so the flush is witnessed by the order of the reads, not by a number |
| whether `AU` or `AT` wrote the frame's saved `pc` | the corpus sees the *value*, not the primitive | #87's sub-steps, one field at a time, corpus after each |
| a decode term's inhibit order | no record is illegal-and-executed | #89's 65 536-opcode golden |
| the display strings | nothing grades a string | each producer's ticket asserts one worked case (#88 the byte lane's `S0`…`S7`, #89 a `MOVE.W`'s text, #93 a `TRAP`'s five steps); the boot's `--trace` is the second witness |
| a comment matching its ROM line | no instrument reads a comment | #94's comments pass compares each script's comments against `microcode-walk.py`'s output for its opcode |
| which block drives a latch (`AOB`'s three homes) | the corpus sees values, not the wiring | the block a file belongs to, and #94's comments pass |

*To be filled by #86–#94*: one row per divergence found — mechanical (a rename) versus a finding (a
bookkeeping field that turned out to be a latch).

## 6. The bar every landing holds

- `--corpus mame`: **317 500 / 317 500** state **and** exact-clock `317 500`.
- `--corpus clk --dir tests/680x0/full`: state **863 265**, the red-file set identical record for record.
- `boot --frames 800` from the repo root: `run.digest` `4eac06dc…` / `run.stream` `1693218a…`, desktop 602,
  34 122.2 pops a frame.
- `scripts/validate.sh` **PASS**; `G8` from 3 interleaved Release pairs against the parent commit,
  `parent + 0.3 ms` and never above 10 ms (8.94 ms at n 751 is the number to return to, not to beat).
- `python3 tools/cpu-dissection/gen.py` still generates variants that build.
