# The core's shape: the silicon's blocks, as partial files

**Skeleton.** Ticket #83 fixes the map, the dependency direction and the state table; the tickets that
follow (#84–#94) fill the sections marked *to be filled*. The authority for every name is
[ADR 0015](../docs/adr/0015-the-core-is-the-silicon.md); this file is the *operational* view of it — which
block lives in which file, in which order they may call each other, and what each latch is called.

Sources, by sheet (`research/68000-silicon/schematics/`):

| sheet | what it is | what it settles here |
|---|---|---|
| `00-floorplan.svg` | where everything is on the die | the block list and the file split |
| `01-datapath.svg` | three sections, two buses, six lanes | `Datapath`, `Registers` |
| `02-control.svg` | opcode → ~200 control lines | `Decode`, `Sequencer`, `Exception` |
| `03-timing.svg` | clock, microcycle, bus cycle, wait state | `Bus` |
| `04-word-formats.svg` | a 17-bit microword, a 68-bit nanoword | `Sequencer`, `Scripts` |

The colour rule is the README's: blue address, green data, amber control, violet clocking and exception
entry, grey storage.

## 1. The partial-file map

Nine files, three of which exist today and change character; three today's files disappear. Every file
states its block, its sheet and its sources in its header (#94 makes that a `grep`-able rule).

| level | file | block | sheet | what it owns | ticket |
|---|---|---|---|---|---|
| 0 | `M68000.Registers.cs` | the register file and every latch | 01, grey | `D0–D7`, `A0–A6`, `USP`, `SSP`, `AU` (+`AUH`/`AUL` accessors), `AT`, `DT`, `AOB`, `DBIN`, `DOB`, `ALUO`, `ALUB`, `ALUE`, `ISR`, `ALUWidth`, `IRC`/`IR`/`IRD`, `PSW`, `SSW`, `INL`, `TPEND`, `PREN`, `FTU` — **storage only, no behaviour** | #86, #87 |
| 1 | `M68000.Datapath.cs` | the three sections, two lanes each, the couplers | 01 | the named transfers (`TransferAbhToAth`, `LatchAluOutput`, `Set16H`, `Set16L`, `CopyAuToPc`, …), the ALU's 15 × 5 matrix, the constants ROM | #91 |
| 1 | `M68000.Counters.cs` | the things that count | 01, 02 | `AU`'s six low bits as a loop counter, `PREN`'s mask walk, `DCR`'s one-hot line, `FTU`'s constant 15 | #92 |
| 2 | `M68000.Bus.cs` | the pins and the bus state machine | 03 | `Pins` (§8.1, one member per signal), `S0–S7`, the strobes, `DTACK`/`BERR` sampling, the access class | #88 |
| 3 | `M68000.Sequencer.cs` | the microcode address path and the microcycle | 02, 04 | the word formats I/II, the next-address sources, the four-way conditional branch, `A0`/`A0S`, the 2-clocks-a-state / 4-clocks-an-access charge | #90 |
| 4 | `M68000.Scripts.cs` | the script states | 02, state lists | one `async` method per microword state, one method per shape (`IsScripted<Family>` is the ledger) | — |
| 4 | `M68000.Exception.cs` | the exception scripts and `SSW`'s two lives | 02, violet | the two frame orders, the group-0 word order, the vector keying, the double fault as a documented gap | #93 |
| 5 | `M68000.Decode.cs` | `A1`/`A2`/`A3` and the `IRD` PLA bank | 02, amber | the 149 `A1` terms, the 20 EA-keyed ones, `IOC`/`PRIV`, the `IRD`-keyed bank (~130 of ~200 control lines) | #89 |
| 6 | `M68000.SingleStep.cs` | the corpus seam | — | `Load`/`Capture`/`StepAsync`, `SingleStepState`/`SingleStepResult` | #34, unchanged |
| 7 | `M68000.cs` | the chip | 00, 03 | the members, the constructor, `Launch`, the drive loop, reset, interrupt sampling, `Pins`/`Instruments`/`SingleStep` as nested types | #85 |

**Occupied, then empty** — three of today's files have no place in the map, and saying so now is what
stops a later reader thinking the map is incomplete:

| file | today | end state |
|---|---|---|
| `M68000.Micro.cs` | the elementary operations, `IdleAsync`, the queue helpers, six latches | its latches go to `Registers`, its atoms to `Datapath`/`Counters`, its charge to `Sequencer`; the file survives only if the atoms have no better home (#91 decides) |
| `M68000.Timing.cs` | the shape table's `_own` charges | **empty by #90** — the file's last row is `RESET`'s, kept until #93 |
| `M68000.Instructions.cs` | the line handlers | **dissolved by #89** into `Decode` (the terms) and `Scripts` (the bodies) |

## 2. The dependency direction

**A file may use only files at a *lower* level, never its own level or above. No cycles, ever** — a
cycle in this map means a block that the hardware does not have.

```
7  M68000.cs            the chip: the loop, reset, interrupt sampling
6  M68000.SingleStep.cs the seam (drives the chip; enters at the boundary)
5  M68000.Decode.cs     A1/A2/A3 + the IRD PLA bank -> an entry point
4  M68000.Scripts.cs    the states     M68000.Exception.cs  the exception states
3  M68000.Sequencer.cs  next address, branch, charge
2  M68000.Bus.cs        the pins, S0-S7
1  M68000.Datapath.cs   transfers, ALU matrix     M68000.Counters.cs  the loops
0  M68000.Registers.cs  the latches
```

The two edges worth stating, because they are the ones a reader will expect to be cycles and are not:

- **The sequencer never calls a script.** It *computes an address* — from the next-address field, or `A1`,
  `A2`, `A3`, or the two overrides `A0`/`A0S`. Selecting `trap1` is a value; the state that runs there is
  the dispatcher's business. That is exactly the hardware's shape
  (`research/m68000-silicon-implementation.md` §7) and it is what keeps level 3 below level 4.
- **The bus never calls upward.** A cycle starts, waits, is answered, and returns; the fault it reports is
  `SSW` and `AOB`, both latches at level 0. `BERR`'s double fault and the rerun protocol stay out
  (adr 0015 §"Considered options", #72 §3).

## 3. The state table — latch → patent name → section → field

The inventory is ADR 0015 §4 (adopt / derive / delete / plumbing). This is the same inventory keyed by
**where it lives**, which is what a reader standing in a file needs. Section numbers are
`research/m68000-silicon-implementation.md`; the name column is
`research/68000-silicon/nomenclature.md` §3's "recommended" one.

| field | name | section | file | note |
|---|---|---|---|---|
| `_d[8]` | `D0–D7` (High + Data halves) | §5.2 | Registers | one 32-bit cell; `Set16H`/`Set16L` are the manual's §1.1 rule |
| `_a[7]` | `A0–A6` (High + Low) | §5.2 | Registers | `A7` is the live `USP`/`SSP`, never an entry |
| `_usp`, `_ssp` | `USP`, `SSP` | §5.2 | Registers | the hidden one is reached by `MOVE USP` and by an exception entry |
| `_au` | `AU`, `AUH`, `AUL` | §5.2, §10.1 | Registers | **the hardware's program counter**; `PC` is a view |
| `_at` | `AT` | §5.2 | Registers | the address of the word in `IR`; a jump's target |
| `_dt` | `DT` | §5.2 | Registers | the last accessed address/operand |
| `_aob` | `AOB` | §5.2 | Registers | what goes out on `A1–A23`; a fault's address |
| `_dbin`, `_dob` | `DBIN`, `DOB` | §5.2 | Registers | the read's latch, the write's buffer |
| `_aluo`, `_alub`, `_alue` | `ALUO`, `ALUB`, `ALUE` | §5.2, §10.1 | Registers | 16 bits each; the shift pair is `ALUO`:`ALUE` |
| `_isr` | `ISR` (the ALU's flag latch) | §5.2, §5.3 | Registers | a *stage* of `PSW`'s flags, not `PSW` |
| `_aluWidth` | the nanocode's `i=` field | §9, §10.2 | Registers | the ALU's width select, held across states |
| `_irc`, `_ir`, `_ird` | `IRC` → `IR` → `IRD` | §5.3 | Registers | the three-deep pipeline; `IRD` is the word being executed |
| `_sr` | `PSW` (`SR` architecturally) | §5.3 | Registers | the status word |
| `_ssw` | `SSW` | §5.3 | Registers | the bus-status word a fault frames — **not** `PSW` |
| `_inl`, `_tpend` | `INL`, `TPEND` | §5.2, §5.3 | Registers | pending interrupt level; trace pending |
| `_pren` | `PREN` | §5.2, §6.2 | Registers | the `MOVEM` walk's position |
| `_ftu` | `FTU` | §5.2 | Registers | the instruction field being translated |

**Derived, and therefore not in that table**: the effective address, `CCR` over `PSW`, `A7` over
`USP`/`SSP`, `DCR`'s one-hot line, the frame's saved counter (whichever latch the script left), and every
`_frame*` memo field (ADR 0015 §4's derive column, which lists each one's replacement).

**Instruments, and therefore not in that table**: `Instruments`' seven `LastFrame*` columns are `#if
DEBUG` readings of `AU`/`AT`/`IRD`/`SSW`; they are written where the frame is written and back no latch
of their own. *To be filled by #86*: the exact write sites, one per column.

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
`Instructions`, `BusCycles`, `PC`, `SR`, `PeekD`→`D0`, `PeekA`→`A7`, from all four generated variants)
and `gen.py`'s two variants (which link `src/TheST.Machine/Cpu/*.cs` unchanged). A restructure that breaks
the dissection tool has broken the sources of record.

*To be filled by #85*: the freeze test — `tools/validation-harness/SurfaceCommand.cs`, run as
`validation-harness cpu-surface`, a row of `scripts/validate.sh` beside ADR 0014's falsifier row. Its
allow-list is ADR 0015 §1's list, one comment per entry citing the tutorial's §, and it must fail when a
member is added by hand (the ticket demonstrates both directions).

## 5. What the corpus cannot see

The list grows as the campaign finds things; each entry says what *would* witness it.

| what | why the corpus is blind | the witness |
|---|---|---|
| the public surface's names | it grades registers, status, memory, clocks — never members | #85's freeze test; the boot's probes (a rename breaks them as an exception, not a red record) |
| a pin's level | nothing grades `Pins` | #88's byte-lane test, the boot's screen, and `M1` (the owner's eyes) |
| where inside a state a charge sits | a record's `length` is a total; the transactions carry the bus accesses only | `--trace N` against a record's `transactions`; `--clocks`' grouping by shape |
| whether `AU` or `AT` wrote the frame's saved `pc` | the corpus sees the *value*, not the primitive | #87's sub-steps, one field at a time, corpus after each |
| a decode term's inhibit order | no record is illegal-and-executed | #89's 65 536-opcode golden |

*To be filled by #86–#94*: one row per divergence found — mechanical (a rename) versus a finding (a
bookkeeping field that turned out to be a latch).

## 6. The bar every landing holds

From ADR 0015's falsifier, restated where the person landing a ticket will look for it:

- `--corpus mame`: **317 500 / 317 500** state **and** exact-clock `317 500`.
- `--corpus clk --dir tests/680x0/full`: state **863 265**, the red-file set identical record for record.
- `boot --frames 800` from the repo root: `run.digest` `4eac06dc…` / `run.stream` `1693218a…`, desktop 602,
  34 122.2 pops a frame.
- `scripts/validate.sh` **PASS**; `G8` from 3 interleaved Release pairs against the parent commit,
  `parent + 0.3 ms` and never above 10 ms (`8.94 ms at n 751` is the number to return to, not to beat).
- `python3 tools/cpu-dissection/gen.py` still generates variants that build.
