# The core is the silicon: the manual's names outside, the parts' names inside

The 68000 core stops being a set of fields that happen to work and becomes the machine the two
authorities describe. **Outside**, its public surface is the programmer's model, the §8.1 signals and the
state concepts of `atari-st-hardware/cpu/02-mc68000-tutorial.md` — registers, flags, pins, the instruction
families as decoders, and the bus cycle's, the entry's and the interrupt's states as displayable strings —
exactly and only, frozen by a reflection test: no invented member, no missing one. **Inside**, every field is a latch one of the two Motorola patents names (`AU`, `AT`, `AOB`,
`DBIN`, `DOB`, `ALUB`, `ALUE`, `ALUO`, `FTU`, `DCR`, `PREN`, `IRC`/`IR`/`IRD`, `PSW`, `SSW`, `INL`,
`TPEND`), every value that is an expression on those latches is computed rather than stored, and today's
bookkeeping — the five overlapping address counters, the "was it charged" flags, the frame's memo
fields — is deleted. The code stays the coroutine it is (ADR 0009, #78's ruling): one named `async`
state method per microword state, one named arm per decode term, never a microcode interpreter.

**Status**: accepted (2026-11-21), on the owner's answers during ticket #83's grilling session — *"ok for
these names"*, *"one Registers file"*, *"pins as its own file"*, *"you can omit everything related to E"*,
*"one file"*, and the two mechanism rulings quoted in §5 and §10. Section
[The owner's answers](#the-owners-answers) records every one of them with his words. Ticket
[#83](https://github.com/laurentprudhon/the-st/issues/83), the first of the campaign *CPU: the core is the
silicon* ([#82](https://github.com/laurentprudhon/the-st/issues/82)). Shape:
[`design/cpu-silicon.md`](../design/cpu-silicon.md).

## Context

- **Two sources, two authorities, and they do not overlap.** The *manual*
  (`atari-st-hardware/cpu/02-mc68000-tutorial.md`) fixes the architectural names — registers, status
  bits, pins, bus states — and names not one internal latch (§1.1, §1.2, §1.3, §8.1). The *patents* —
  US 4,325,121 App. B and US 4,296,469 — name the microarchitecture: `AOB`, `AT`, `DT`, `AU`, `ALU`,
  `ALUB`, `ALUE`, `FTU`, `DCR`, `PREN`, `DBIN`, `DOB`, `IRC`, `IR`, `IRD`, `PSW`, `SSW`, `INL`,
  `TPEND` (`research/m68000-silicon-implementation.md` §5.2–§5.3,
  `research/68000-silicon/nomenclature.md` §1 and §3's "recommended" column). The die supplies
  *content* — the geometry, the ROM tables, the PLA terms — and the patent supplies *names*
  (`m68000-silicon-implementation.md` §13's first rule). An emulator's identifier is T3 even when it
  descends from the die, so `m_pc`, `m_au`, `m_ird` are evidence, never vocabulary.
- **The campaign is a re-expression under a closed corpus.** The gate is closed on both columns — MAME
  `317 500 / 317 500` state *and* exact-clock, 127 of 127 files — and CLK stands beside it at 863 265
  state / 819 372 exact-clock with its residue classes measured to be conventions, at its ceiling. The
  blessed fold pair is `run.digest` `f8d39bf1…` / `run.stream` `9e362791…` (re-blessed by the `#92`
  ruling), desktop at frame 602,
  `_frclock_at_desktop` 596, 34 122.2 pops a frame; `validate.sh` PASS with **G8 8.94 ms at n 751**.
  Those are the numbers this ADR is bound to: a restructuring that moves any of them is a bug report,
  not a new baseline.
- **Today's surface is the manual's words and invented words, mixed.** `M68000` exposes `PC`, `SR`,
  `Usp`, `Ssp` (the manual's), `PeekD`/`PeekA` (a probe's), `Supervisor`, `InterruptMask` (truths the
  manual states as `S` and `I2–I0`), five counters and three observers (none of them the part's), and
  no pin at all. The corpus seam (`Load`, `Capture`, `StepAsync`, `SingleStepState`) and `Launch` are
  the engine's and the harness's, not the part's.
- **Today's state is 36 fields in `M68000.cs` and 6 in `M68000.Micro.cs`**, of which the four
  address counters, `_fetchStateAddress`, `_streamStart`, `_opcodeAddress`, `_own`, `_eaCharged`,
  `_queueShort`, `_trailingFetched`, `_flowTransferred` and the four `_frame*` fields have no silicon
  counterpart at all: they are *expressions* over the pipeline, the address adder and the sequencer's
  own state, written down because the corpus was being closed by measurement.
- **The corpus cannot see the interface.** It grades registers, status, memory and clocks; a rename that
  breaks the boot's probes or the harness's state load shows up as an exception or a `frames_to_desktop=never`,
  not as a red record. The freeze test of #85 and the boot pair are the only witnesses this ADR can name.
- **Two additions the owner made while this ADR was being written (2026-11-21), and they are
  requirements, not preferences.** (a) *The state rule is about storage, not about time*: "when I said I
  don't want to create additional state compared to what exists on silicon … of course, time is
  simulated, so time constants are allowed as additional state, but they should be named in a specific
  way so that we see them as what they are — not storage on the chip, but an emulator artifact to
  simulate time." §5.1 is that naming rule. (b) *The surface carries the manual's own concepts, not only
  its registers*: the first level of instruction decoding is part of the interface, its functions are
  "named exactly like the instruction families in the programmer manual", the currently executing
  instruction is a string in the public state, and every state-machine concept the manual names is a
  displayable string there too. §1.1 is that inventory.
- **Three rulings are the owner's, made when the map was charted (2026-11-21), and are not re-opened
  here.** (1) *A register is 32 bits of storage*; the High/Low/Data split becomes named accessors, because
  the physical split has no behavioural consequence on this machine and splitting storage costs G8 for
  nothing — the manual's own sentence is the argument: a byte or word write touches "only that byte or
  word … the rest is untouched" (§1.1). (2) *The coroutine style stays* (ADR 0009, #78): "unrolled as C#
  switch and scripts" means one named state method and one named decode arm, never a 544-entry `switch`
  and never a microcode interpreter fed by the ROM dumps. (3) *A moved fold pair is a bug, not a
  re-bless.*

## Decision

### 1. The public surface is the manual's, exactly and only

`M68000`'s declared public members are **the programmer's model**, **four nested types**, and **one
verb** — nothing else. The freeze test of #85 asserts this list by reflection, entry by entry.

**The programmer's model** — the registers of tutorial §1.1, §1.2 and §1.3 and nothing more. Every one
is a *view* over the machine below; none gets a backing field of its own.

| member | type | tutorial | what it is |
|---|---|---|---|
| `D0`…`D7` | `uint` r/w | §1.1 | a data register; a word write is the low half, a byte write the low byte — "the rest is untouched" |
| `A0`…`A7` | `uint` r/w | §1.1, §1.3 | an address register; **`A7` is the live stack pointer** — it reads `SSP` in supervisor mode and `USP` in user mode |
| `PC` | `uint` r/w | §1.1, §1.4 | "the next instruction word to be fetched" — the address adder `AU` at a boundary |
| `SR` | `ushort` r/w | §1.1, §1.2 | the status register, whole; undefined bits read 0 |
| `CCR` | `byte` r/w | §0.1, §1.2 | the low five bits of `SR` — the condition code register |
| `USP`, `SSP` | `uint` r/w | §1.3 | the two stack pointers; the hidden one is reached by `USP` and by an exception entry |
| `T`, `S` | `bool` r/w | §1.2 | trace; supervisor |
| `I2`, `I1`, `I0` | `bool` r/w | §1.2 | the interrupt mask, one member per named bit |
| `X`, `N`, `Z`, `V`, `C` | `bool` r/w | §0.1, §1.2 | the five flags — the manual's letters, no expanded names |

**`Pins`** — the signals of tutorial §8.1, one member per signal, and **there is no `A0` pin**: an odd
byte access is `A1`=1 with `LDS` asserted (§2, §8.1). A pin is a *level*, so every member is a `bool`;
the buses are their bits.

| group | members (`M68000.Pins.…`) |
|---|---|
| address | `A1` … `A23` |
| data | `D0` … `D15` (`D15–D8` = even byte, `D7–D0` = odd byte) |
| strobes | `AS`, `UDS`, `LDS` |
| control | `RW`, `DTACK`, `BERR`, `HALT`, `RESET`, `CLK` |
| space | `FC0`, `FC1`, `FC2` |
| interrupt | `IPL0`, `IPL1`, `IPL2` |
| arbitration | `BR`, `BG`, `BGACK` |
| 6800 | `E`, `VPA`, `VMA` |

**The pins are the interface, not a view** (the owner's correction, 2026-11-21). Unlike the register
table above, a pin is not a mirror of a latch: it **is** the chip's face to the world — the level the
engine's wire carries — so `Pins` reads the wire records (the bus's assertion group and its release),
and the core's own copies of those facts (`AOB`, `DOB`, `DBIN`, #86) sit *below* that layer and serve
it rather than being duplicated by it. C# will not let a member and a nested type share a name
(CS0102), so the accessor is `cpu.Pads.*` and the nested type keeps the name `Pins`.

**`E` is a level and nothing else.** The owner's ruling (2026-11-21): *"you can omit everything related to E
in this emulator."* The ST does not use the 6800 peripheral group (tutorial §8.1), so there is no E divider,
no ten-state counter and no block for it — `Pins.E` exists because §8.1's list does, it reads false, and
nothing drives it. The same holds for `VPA`/`VMA`: a pin in the list, no protocol behind it.

**`Instruments`** — the members the manual has no word for, in one place, each documented as *not the
part*:

| member | build | why it exists |
|---|---|---|
| `TicksPerCpuClock` | both | one CPU clock is four ticks of the 32 MHz oscillator (ADR 0001) — the engine's unit, not the part's |
| `Instructions`, `BusCycles`, `InternalClocks`, `InterruptsTaken`, `Exceptions` | both | the counters the Release `boot` reports and `cpu-dissection` reads by reflection — see §6 |
| `Halted`, `HaltReason` | both | the harness's diagnosis of a machine that went quiet; the part's own statement of the same thing is `Pins.HALT` held and the sequencer's STOP state |
| `Observer` | both | the debug instruction hook the boot's traces use |
| `CycleObserver`, `ClockObserver`, `LastFramePc`, `LastFrameFetchPointer`, `LastFramePcCounter`, `LastFrameIrAddress`, `LastFrameRefetched`, `LastFrameTrailing`, `LastFrameVector` | `#if DEBUG` | the per-record instruments `--trace`/`--clocks` print |

**`Instruments` also carries the manual's own state as text** — §1.1 below lists them, and they are
*display*, not state: each is a string or an enum read off the machine at the instant it is asked for, so
an instrument never makes a copy of the core (the same rule §3's views keep).

**`Decode`** — the first level of the instruction decode, and the one place where a nested type's members
*are* the manual's words: one `public static` function per instruction family, named exactly as the
manual's §5 tables and appendix name it.

```csharp
public static class Decode
{
    public readonly record struct Decoded(Family Family, int Size, int Mode, int Register,
                                          int SourceMode, int SourceRegister, int Condition);

    public static bool Move  (ushort opcode, out Decoded instruction);
    public static bool Movea (ushort opcode, out Decoded instruction);
    …
    public static bool Stop  (ushort opcode, out Decoded instruction);
    public static bool Reset (ushort opcode, out Decoded instruction);
    public static bool Illegal(ushort opcode, out Decoded instruction);
}
```

One member per line of the manual's §5 tables — `Move`, `Movea`, `Moveq`, `Movem`, `Movep`, `Lea`, `Pea`,
`Exg`, `Swap`, `Link`, `Unlk`, `MoveFromSr` (`MOVE SR,<ea>`), `MoveToSr` (`MOVE <ea>,SR`), `MoveToCcr`,
`MoveUsp` (`MOVE USP`), `Andi`, `Ori`, `Eori` (each also carrying the to-`CCR`/to-`SR` targets), `Add`,
`Adda`, `Addi`, `Addq`, `Addx`, `Sub`, `Suba`, `Subi`, `Subq`, `Subx`, `Cmp`, `Cmpa`, `Cmpi`, `Cmpm`,
`Neg`, `Negx`, `Clr`, `Ext`, `Mulu`, `Muls`, `Divu`, `Divs`, `Abcd`, `Sbcd`, `Nbcd`, `And`, `Or`, `Eor`,
`Not`, `Tst`, `Tas`, `Asl`, `Asr`, `Lsl`, `Lsr`, `Rol`, `Ror`, `Roxl`, `Roxr`, `Btst`, `Bchg`, `Bclr`,
`Bset`, `Bcc`, `Bra`, `Bsr`, `Dbcc`, `Scc`, `Jmp`, `Jsr`, `Rts`, `Rtr`, `Rte`, `Trap`, `Trapv`, `Chk`,
`Nop`, `Stop`, `Reset`, and `Illegal` (the encodings §12 says must trap as vector 4). A family function
returns *false* rather than throwing: the first level's question is "does this opcode belong to this
family", and the inhibit order between families is #89's business, not the surface's.

The `Family` enum carries the same names (one member per function, `Decode.Move` ↔ `Family.Move`), and the
freeze test asserts the two lists are equal, so a family that appears in one and not the other fails.

**`SingleStep`** — the corpus seam (ticket #34): `Load(in SingleStepState)`, `Capture()`,
`StepAsync()`. Its vocabulary is the seam's, and the seam *is* the programmer's model plus the two
prefetch words (§7). It is public because `CpuTestsCommand` is a different assembly, and it is nested
because it is not the part.

**`Launch()`** stays on the chip: every virtual chip is started that way (`Machine.cs` calls
`Launch` on the System Control Unit, the Video, the Timers and the CPU alike), so it is the engine's
verb — ADR 0006/0008's surface, not the 68000's.

**Retired:** `PeekD`/`PeekA` (→ `D0`…/`A0`…), `Supervisor` (→ `S`), `InterruptMask` (→ `I2`,`I1`,`I0`),
`Usp`/`Ssp` (→ `USP`/`SSP`).

### 1.1 The manual's state concepts, and which of them are displayable

The owner's addition (b): *scan the manual for every concept it uses as a state and represent them as
displayable strings*. The scan is complete against
`atari-st-hardware/cpu/02-mc68000-tutorial.md` §0.5, §1.4, §5, §6, §7, §8.2–§8.4, §11 — the places the
manual describes execution as a sequence of named states or cases. One member per concept, the values
being the manual's own words, and **a concept the manual only describes gets no member** (the rule's
other half, so the surface cannot grow synonyms).

| concept | manual | its states, in the manual's words | member |
|---|---|---|---|
| the instruction being executed | §1.4, §5 | the mnemonic and its operand syntax; the family it belongs to | `Instruments.Instruction` (string) and `Instruments.Family` (`Family`), fed by `Decode` |
| the bus cycle | §0.5, §8.2 | `S0` "bus idle", `S1` "address, `FC` and `R/W` driven and valid", `S2` "`AS` and the data strobes asserted", `S3–S5` "the CPU waits here", `S6` "the transfer", `S7` "strobes negated, cycle over" | `Instruments.BusState` → `"S0"` … `"S7"` |
| the exception entry | §6 | "1 the vector number n is determined", "2 the old SR is copied …", "3 PUSH PC", "4 PUSH SR", "5 PC ← long at 4 × n" | `Instruments.EntryStep` → `"1 vector"`, `"2 status"`, `"3 push PC"`, `"4 push SR"`, `"5 load PC"` |
| the interrupt | §7 | its table's five cases: "masked", "taken", "edge-triggered", "autovector", "spurious" | `Instruments.Interrupt` → those words, or `"none"` |
| reset | §11 | "RESET held low for at least 10 clocks", "S=1, T=0, I=7", "SSP ← long at $000000", "PC ← long at $000004", "first instruction fetch" | **no member**: reset is two vector reads and `BusState` shows both; the manual names no state of its own beyond the pin |
| bus arbitration | §8.4 | "CPU owns the bus", "BR asserted", "BG granted", "BGACK negated" | **no member**: the campaign keeps arbitration out of scope (named pins, one modelled hand-over point) |
| the prefetch queue | §1.4 | two words held; "`PC` always points at the **next word to be fetched**" | **no member**: it is the pipeline's contents (`IRC`/`IR`/`IRD`), displayed as `PC` and through the seam |
| a read-modify-write pair, a long access | §8.2, §8.3 | "`AS` never released in between", "high word first" | **no member**: the manual names an *access class*, not a state — the class drives `Pins` and `BusState` |

Three constraints on these members, and they are what keeps them instruments rather than state:

- **They are read, never stored.** `Instruction` is composed *when it is asked for*, from `IRD`, `PSW` and
  the decode's answer — never built per instruction, because a string per instruction in the Release boot
  is a G8 cost with no owner. `BusState`, `EntryStep` and `Interrupt` are reads of the sequencer's own
  position (`#89`, `#93`).
- **They are noise-free at the boundary.** `Family` is `Family?`: it names the family of the instruction
  *being executed* and is `null` at an instruction boundary; `Instruction` reads `"—"` there. `BusState`
  reads `"S0"` when no cycle is in progress, and `EntryStep`/`Interrupt` read the empty string outside an
  entry. A member that keeps the previous instruction's value is the `--clocks` frame-columns bug again and
  is refused here by name.
- **A new member here needs a line in this table**, and a concept that this table refuses cannot come
  back as a string instrument under another name.

### 2. A nested type is a place, not a name

`Decode`, `Pins`, `Instruments` and `SingleStep` are words the manual does not have, and that is allowed
for one reason only: they name **where a thing lives**, never **what the part is**. The rule that follows is
mechanical, and it is what makes "exactly and only" enforceable: *every member inside a nested type is
named by its own authority* — `Pins.A1` by §8.1, `Instruments.BusCycles` by nobody (it says so), and no
nested type may ever be used to smuggle an alias in (`Pins.UDStrobe`, `Instruments.Supervisor`). A nested
type's *own* name is a place and may be a word no source has; its *members* never are — which is why
`Decode`'s members are the manual's family names, `Pins`' are §8.1's signals, and `Instruments`' are either
counters that say they are counters or the concept strings of §1.1.

Nesting is also forced, not chosen: `A1`–`A7` and `D0`–`D7` are *both* register names and pin names, so
a flat surface cannot hold the manual's list twice. The register file is the part's face and stays on
the class (every consumer writes `cpu.A7`); the pins are the part's edge and move one level down.

### 3. A register is 32 bits of storage; the sections are accessors

One field per register (`D0`–`D7`, `A0`–`A6`, `USP`, `SSP`, `AU`, `AT`, `DT`). The High/Low/Data split of
`m68000-silicon-implementation.md` §5.1 is a *layout* fact — the High section holds the upper 16 bits of
every 32-bit register, the Low section the lower halves of the address registers, `PC` and `AT`, the Data
section the lower halves of the data registers — with no behavioural consequence on this machine. It
appears as the named accessors the microcode already uses (`Set16H`, `Set16L` over one field), which is
the manual's own rule for a byte or word write (§1.1). Splitting the storage into three arrays would cost
G8 for nothing, and G8 is the reason this is a decision rather than a style note.

`A7` is **not** a ninth array entry: it is a view whose getter and setter pick `SSP` or `USP` by `S`
(§1.3). `PC` is a view on `AU`. `CCR` is a view on `SR`'s low five bits.

### 4. The state is the latches — the inventory, four columns

Not one less, not one more. The ticket named three columns (adopt, derive, delete); the engine's own
plumbing needs a fourth, because ADR 0009 §1 already owns those objects and deleting them would delete
the bus contract.

**Adopt** — a latch, its patent name, its section, its field:

| latch | section | today | field after #86 |
|---|---|---|---|
| `D0`–`D7` | High (upper) + Data (lower) | `_d[8]` | `_d[8]` |
| `A0`–`A6` | High + Low | `_a[8]` | `_a[7]` (A7 is the live `USP`/`SSP`) |
| `USP`, `SSP` | — | `_usp`, `_ssp` | `_usp`, `_ssp` |
| `AU` (+`AUH`/`AUL` halves) | High + Low | `_pc` **and** `_fetchPointer` | `_au` — *the hardware's program counter* |
| `AT` address temporary | Low | implied by `_fetchPointer` arithmetic, and `_irAddress` | `_at` |
| `DT` data temporary | Data | — | `_dt` |
| `AOB` address output buffer | High + Low | the `address` argument of `Cycle` | `_aob` |
| `DBIN` data input buffer | Data | `_dbin` | `_dbin` |
| `DOB` data output buffer | Data | the `writeData` argument of `Cycle` | `_dob` |
| `ALU` output latch (`ALUO`) | Data | `_aluo` | `_aluo` |
| `ALUB` second operand | Data | `_alub` | `_alub` |
| `ALUE` extension/shift | Data | `_alue` | `_alue` |
| `ISR` the ALU's flag latch | Data | `_isr` | `_isr` |
| `ALUWidth` — the nanocode's `i=` field | — | `_aluWidth` | `_aluWidth` |
| `IRC` → `IR` → `IRD` | Data | `_irc`, `_ir`, `_ird` | `_irc`, `_ir`, `_ird` |
| `PSW` the status word | — | `_sr` | `_sr` |
| `SSW` the bus-status word | — | composed at the frame (`BusStatusWord()`) | `_ssw` |
| `INL` pending interrupt level | — | locals in `CheckInterruptAsync` | `_inl` |
| `TPEND` trace pending | — | implied by `PSW.T` | `_tpend` |
| `PREN` the MOVEM mask walk | Data | the `for (bit …)` loop | `_pren` |
| `FTU` field translation | — | the `size`/`mode`/`reg` locals | `_ftu` |

**Derive** — an expression on those latches; if it needs a name, it is a method, not a field:

| today | is |
|---|---|
| `_fetchStateAddress` | `AU` as the last *fetch state* wrote it — the pipeline's position, not a counter (#87) |
| `_streamStart` | `AU` at the boundary — derivable, and an instrument while `--clocks` needs it |
| `_irAddress` | `AT`: the address of the word in `IR` |
| `_opcodeAddress` | `AT` at the decode, or `AU` past the last consumed word |
| `_frameFetchPc` (+`Declared`) | whichever of `AU`/`AT` the script left — `AuDeclared`/`AtDeclared` (#87) |
| `_frameInstructionWord` (+`Declared`) | `IRD` (`_ird`) |
| `_opcode` | `IRD` |
| `_faultAddress`, `_faultWrite`, `_faultIsFetch`, `_faultProgram`, `_addressError` | `SSW` (class + function code + `SSW_N`) and `AOB`'s value at the abort (#93) |
| `_halted`, `_stopped` | the sequencer's own states, and `Pins.HALT` for the bus's hold |
| `_queueShort`, `_trailingFetched`, `_flowTransferred` | pipeline contents and the sequencer's position (#87, #90) |
| `_ircValid` | the sequencer's fill state: a boundary is full, a refill is not |
| `_postIncrementReg`, `_postIncrementFrom` | the commit-state rule — a post-increment commits in the state *after* its access (`pinl3`), which is the script's business |
| `DCR`'s one-hot line | a decoder has no state: `Decoder4To16(bit)` |
| the effective address | a computation over `AT`, `AOB` and the operands — never a field |

**Delete** — no silicon counterpart, no derivation, and nothing left to say: `_own`, `_eaCharged` (§5
below is where their meaning goes).

**Plumbing** — ADR 0009's, and it stays: `_answerSeq` (the subscription's sequence number),
`_busFaulted` (the deferred fault the machine takes at the boundary). These are the engine's objects; the
part has no opinion about them, and ADR 0009 §1 named them before this ticket existed.

### 5. Where the internal clock lives: in the state, not in a counter

**The decision: a microword state charges its own 2 clocks, and a bus access costs its 4** — the faithful
reading of `m68000-silicon-implementation.md` §6.1 ("a microcycle is two clock periods … a microword
occupies one microcycle, so a microinstruction costs two clocks. One that must complete a bus operation
costs four"). The charge is a property of *being in a state*, so there is no `_own`, no `Charge` on the
sequencer and no per-access-class table of charges.

**The mechanism is the owner's (2026-11-21), and it is what a script looks like**: *"when generating the C#
functions from the microcode scripts … insert a comment at each microcode line boundary; add 2 to a local
variable counter as a suffix to each of these sections, then use this local variable counter when calling an
engine async function to sync the chip time."*

```csharp
async ValueTask MoveWordToMemoryAsync(…)          // the µROM's words, unrolled
{
    int clocks = 0;                               // the instruction's own time, and it never leaves this frame
    clocks += 2;  // 0d8 adrw1   (two clocks: an idle microword)
    clocks += 2;  // 0da morw1   the ALU and its flags, no bus
    await ClocksAsync(clocks); clocks = 0;        // the engine's clock, once, before the access it precedes
    await WriteAsync(…);                          // a bus access: its 4 clocks are the bus cycle's own
    clocks += 2;  // 0dc nnml1   the closing prefetch rides inside this idle run
    await ClocksAsync(clocks); clocks = 0;
}
```

- **The charge is a local, never a field.** Three consequences, each mechanical: no `_own`, no `_eaCharged`
  (its once-ness is the position of the states), and nothing to reset at the three boundaries (`Run`,
  `SingleStep.Load`, `SingleStep.StepAsync`) — the flag that had to be reset is gone.
- **The flush points are the bus accesses.** A charge is a wait with nothing on the wire (skill §7), so
  consecutive idle microwords are one longer wait — but the *placement* is load-bearing: the prefetch sits
  inside an idle run, so the counter is handed to the engine at the beginning of every bus access, and once
  at the end of the script. A flush moved one state later changes which access the clocks precede, and
  `--clocks` sees it.
- **The comment is the citation and the arithmetic.** Each microcode line's comment names its ROM address and
  state (`// 0da morw1`) and carries the running total, so a reader — and #94's comments pass — can check
  every script against `--clocks` without opening the dump. This is the campaign's fourth artifact made
  compulsive rather than remembered.
- **`M68000.Timing.cs` becomes a bridge with a deadline, not a home.** Its rows charge families whose
  scripts are still MAME's tables; the value it produces is a *parameter or a local* of the instruction's own
  frame, and each row dies as the family's script carries its own lines, so the file is empty at the end of
  #90. The exact-clock column (317 500 today) is the witness for every deletion — a family whose row is
  removed before its script charges goes short by 2/4/6 and the class is visible in `--clocks` by shape.
- The alternatives were refused for the reason this campaign exists: (b) "the access class the script
  declares" puts a clock count on a bus *label*, and the label's job is to say which bus access to start
  (§8), not how long the instruction takes; (c) "a named `Charge` on the sequencer" is this decision with
  an invented object in front of it — a counter the silicon does not have, which is what `_own` already was.

### 5.1 Time is simulated, and it says so

The owner's rule (a), and it is the general rule the inventory only needed once: *nothing may be state that
the silicon does not store — but time is simulated, so a value that exists to make the emulator's clock
behave is allowed, provided it is named so a reader sees an artifact rather than a latch.*

- **The unit is in the name.** A value that is an interval or a deadline is named for its unit: `…Clocks`
  for a count of CPU clocks (one clock = four oscillator ticks, ADR 0001), `…Ticks` for oscillator ticks.
  `TicksPerCpuClock` and `InternalClocks` already comply; a bare noun may not hold a duration
  (`_own`, `Charge`, `_eaCharged` are the shapes the rule forbids).
- **Time lives in one file, and it is not the part's.** `M68000.Emulator.cs` — the emulator interface of §6 —
  is the only place a time field may exist: the reported counters, the chip time the simulation advances, and
  any surviving duration while #90 empties the table. A `…Clocks` field in `M68000.Registers.cs` or
  `M68000.Sequencer.cs` fails the rule, and so does one in any other block. That is the check "not storage
  on the chip" made mechanical: one file, named for what it is, and nothing about time anywhere else.
- **Time is cited differently.** A latch is cited to a patent section; a time value is cited to
  `m68000-silicon-implementation.md` §6.1 (the microcycle) or to the manual's own timing table (§9), and
  its doc comment opens with *"time, not storage"*.
- **This ADR's §5 answer needs no such value**, and that is a result, not an omission: a script's charge is
  a local in the script's own frame (`int clocks`), so the duration is the state's and there is nothing left
  to remember. If #90 finds a charge that cannot stay local — because it must cross a helper — it becomes a
  `…Clocks` parameter first and a field in `M68000.Emulator.cs` only if threading it is worse. `_eaCharged`
  is not time at all: it is a "has happened" bit, and its replacement is the position of the states.

### 6. What an instrument is allowed to be

An instrument is a member that exists for the harness, the boot or a reader, and is documented as not the
part. **All of them live in one file, `M68000.Emulator.cs`, the emulator's interface** — the owner's
placement (2026-11-21): *"put it in a separate 'emulator interface' file, with the chip time simulation
field."* That file is the only home of `Instruments` (counters, `Halted`/`HaltReason`, the observers, the
frame columns, `TicksPerCpuClock`) and of the chip time the simulation advances; a member that reports or
simulates is in it or does not exist, and no other block may hold a duration (§5.1).

Two boundaries inside it, and the difference is a *consumer*, not a taste:

- **Behind `#if DEBUG`**: everything that is per-record — the three observers and the seven `LastFrame*`
  columns. They are the instruments `--trace`, `--reads` and `--clocks` print, and a per-cycle observer in
  the Release build costs the very thing G8 measures (skill §7's recorded failure: a Release build that
  failed because the harness referenced a `#if DEBUG` member, and the gate then quoted the previous
  binary's row).
- **Unconditional**: the five counters, `TicksPerCpuClock`, `Halted`/`HaltReason` and `Observer`. They
  cannot be `#if DEBUG`, and the reason is a fact about the tree rather than a preference: the **Release**
  `boot` prints them in its own report (its per-frame and closing counters are the G8 row's own text) and
  `tools/cpu-dissection/Program.cs` reads `Instructions` and `BusCycles` **by reflection** from all four
  generated variants. An instrument moved behind `#if DEBUG` breaks the Release build and the dissection
  tool, and neither break is visible to the corpus.

Every consumer migrates with the surface: `CpuTestsCommand.cs` (`PeekD`/`PeekA` → `D0`…/`A0`…, `Usp`/`Ssp` → `USP`/`SSP`, the frame
columns → `Instruments`), `BootCommand.cs` (same), `tools/cpu-dissection/Program.cs` (six reflected names
— `Instructions`, `BusCycles`, `PC`, `SR`, `PeekD`, `PeekA` — of which four change path or name, so
`gen.py`'s generated variants must be regenerated and rebuilt), and `Machine.cs` (nothing, if `Launch`
stays).

### 7. The seam is the programmer's model plus the two prefetch words

`SingleStepState` already is the corpus's shape, and that is what it stays: `D[8]`, `A[8]`, `Pc`, `Sr`,
`Usp`, `Ssp` — the manual's register set — plus `Prefetch0`/`Prefetch1`, the two words the reference has
already fetched (ADR 0009 §4; the corpus's `initial.prefetch`). The two prefetch words are `IRD` and `IR`
on load and a single-step's `Capture` reads back the programmed model, because the corpus compares exactly
those fields.

The seam is the one place where the *harness's* vocabulary is allowed to name things: a corpus record is
one instruction in isolation, and no latch name may leak into it — `SingleStepState` must not grow
`Au`/`At`/`Ssw` fields. Anything #87 needs to observe for a frame rule is an `Instruments` member, not a
seam field.

### 8. The naming authority order

1. **The architectural surface, from the manual** — `D0–D7`, `A0–A7`, `PC`, `SR`, `CCR`, `USP`, `SSP`,
   `T`, `S`, `I2–I0`, `X N Z V C`, and §8.1's signals. The manual's spelling, minus punctuation C# forbids:
   `R/W` is `RW`; numbered signals are indexed ascending (`FC0`, `FC1`, `FC2`, though the manual writes
   `FC2–FC0`). The manual is also what each member's doc comment quotes — §1.1's table for a register,
   §1.2's bit map for a status bit, §8.1 for a pin — because the comment is the citation.
2. **The internals, from the patents** — upper case in prose, upper case in code (`_au`, `_at`, `_aob`,
   `_dbin`, `_dob`, `_alub`, `_alue`, `_aluo`, `_irc`, `_ir`, `_ird`, `_psw`, `_ssw`, `_inl`, `_tpend`,
   `_pren`, `_ftu`). `nomenclature.md` §3's "recommended" column is the table, adopted verbatim;
   `nomenclature.md` §3 is also the **one** alias table, and a name that is not in it is either added
   there or is an error.
3. **Nothing from an emulator, and no net or bit position.** `m_pc`, `m_au`, `m_aluo` are T3 evidence and
   appear in comments only; PLA terms, ROM addresses (`rstp1`, `morw2`) and node numbers may be quoted in
   a state's comment, never used as an identifier.
4. **An instruction family is named by the manual's mnemonic, and a state concept by the manual's own
   words** — `Decode.Move`/`Family.Move` for §5's tables and appendix, `Instruments.BusState`'s `"S0"`…
   `"S7"` for §8.2's states, `Instruments.EntryStep`'s `"3 push PC"` for §6's steps. Where the manual has
   only a phrase (`MOVE SR,<ea>`, "the vector number n is determined"), the phrase minus its punctuation is
   the name (`MoveFromSr`, `"1 vector"`), and the doc comment quotes the phrase.
5. **`ALUO` and `ISR` are admitted as aliases, and the table says so.** The patent names the ALU's result
   latch by role only (§5.2's "the latched ALU output") and the ALU's flag latch not at all; both are
   named `aluo` and `isr` in the microcode listing and in MAME, which is why they are the two entries
   `nomenclature.md` §3 must gain with their provenance — the alternative is to invent a name, and the
   rule forbids that.

### 9. The corpus outranks a derivation, and the coroutine style is fixed

Where a rule read off the silicon disagrees with a rule measured on the corpus, the measured rule wins
for the landing and the difference is a `FINDINGS.md` row — that is how rows 60, 63, 69, 70, 74 and 77
become *derived* rather than re-guessed. Where the two corpora disagree, MAME wins and CLK's number is
quoted beside it, never subtracted.

The code stays named `async` C#: a state is a method named after its microword state, an arm is named
after its decode term, a transfer is a named primitive over the lanes. A state list is a *transcription*,
never a table the machine reads.

### 10. The two control stores are functions, not tables

The owner's rule for what the generated code looks like (2026-11-21), and it is the campaign's shape in one
paragraph: *"the initial PLA instruction decodes are a switch on C# functions names and/or linked to the
programming manual instruction families, then the script/loop/branches inside the C# function is an
unrolling of the Micro store, and each individual statement in the function is a decode of the Nano store
(+ other PLAs) … Micro store and Nano store classes would only contain the elementary methods called by the
microprograms, not the data tables themselves."*

Three layers, three kinds of code, and none of them a table:

| layer | the hardware | the C# | where |
|---|---|---|---|
| first level | `A1`/`A2`/`A3` PLAs, the `IRD` PLA bank | `Decode`'s family functions, one per manual family, returning a microcode entry point (§1) | `M68000.Decode.cs` |
| the µROM | 544 × 17, the sequencing words | the script: one `async` method per microcode line's state, its branches and loops the line's `TY`/`CBC` fields, its `clocks += 2` the line's microcycle | `M68000.Scripts.cs` |
| the NROM | 336 × 68, the datapath atoms | the elementary methods a statement calls — transfers, the `AU` constant select, the ALU mode, the flag masks — **and no data** | `M68000.NanoStore.cs` (the NROM) and the µROM's own field methods |

- **Every microcode line's section carries a comment naming it**, with the running clock total beside it
  (`// 0da morw1   2 clocks`), and every group of statements decoded from one nanoword carries a comment
  naming that nanoword's line. The store classes hold *methods* — `AluAnd16`, `Set16L`, `LatchAob`,
  `NextByNma` — never an array of words to interpret.
- **This is what "unrolled as C# switch and scripts, never an interpreter" means concretely** (§9, #78): the
  ROM's *content* is the code, so the code can be read beside `microcode-walk.py`'s output line for line,
  and the two stores' files are the vocabulary the scripts are written in.
- **A table would be the interpreter returning**: a `ushort[]` of control words, a `switch` over 544 arm
  numbers, or a data-driven dispatch is refused by #78's ruling, and the comment-per-line rule is what makes
  the refusal cheap to check in review.

## Invariants

- **`PSW` and `SSW` are two different words.** The status register and the bus-status word a fault frames;
  a group-2 frame carries both. Collapsing them is the single most likely name-level mistake.
- **`A7` is the live stack pointer**, not a ninth array entry: it reads `SSP` when `S`=1 and `USP` when
  `S`=0. A view that writes the wrong one moves 1 000 060 CLK records at once.
- **There is no `A0` pin.** An odd byte is `A1`=1 with `LDS`. A view that carries an address bit 0 is
  inventing hardware.
- **`ALUO` and `ISR` are 16 bits, and `ISR` is a stage of `PSW`, not `PSW`.** Widening either produces
  garbage on every record; conflating them loses the two-pass flag rule.
- **A register write that happens exactly once must stay exactly once** (`prst`, `popm6`, `pinl3`'s
  commit) — naming the primitive makes the commit point visible, and moving it into a loop is how 249
  records went two bytes out.
- **A member's doc comment quotes its source** (manual §n, or patent §n / sheet), and a second sentence
  says what the code does about it. No paraphrase where a quotation exists.
- **A derived value is a method; a field is a latch or an instrument.** If a field cannot be cited to a
  patent section, a section of the silicon record, or the instrument boundary of §6, it does not exist.

## Consequences

- **Slower, and measured.** The surface becomes views: `PC` is `AU`, `SR` reassembles `PSW`, `A7` branches
  on `S`, the pins read the control record, and the datapath's transfers become calls (they must be
  inlining primitives on the fields — measured, not assumed). The budget is the noise floor, **parent +
  0.3 ms** at equal `frame_n` over 3 interleaved Release pairs, and never above 10 ms; #91 is the ticket
  most exposed to this.
- **The surface grows by the manual's own concepts, and that is a cost as well as a gain.** `Decode` is
  ~78 `public static` functions and `Family` the same number of members, so a reader meets the whole
  instruction set at the type's edge — which is the point (the manual's first level, readable) and also
  the reason the freeze test asserts the two lists are equal. The five display members cost one string
  composition *when read* and nothing per instruction; a `string` built per instruction in the Release boot
  would be a G8 regression with no owner, and §1.1 forbids it.
- **Harder to read in two specific ways.** A frame rule stops being an expression and starts naming a
  latch (`AT` left by state `jsal2`), which is *less* obvious until the reader knows `AU`; and `--clocks`'
  frame columns will print latch values whose meaning lives in a script's state name. #94's comments pass
  is what pays this back.
- **What the corpus cannot see**: the interface itself (a rename breaks the boot's probes as an exception,
  not a red record); the pin views (nothing grades a pin level — the boot's screen and the freeze test are
  the only witnesses); and the timing of a charge *inside* a state, which the corpus reduces to a total.
  That is why #84's one-command verdict exists and why the fold pair is quoted at every step.
- **`M68000.Instructions.cs` and `M68000.Timing.cs` lose their reason to exist** by #90 — the first
  dissolves into the decode (#89) and the scripts, the second into the states. Both are named in
  `design/cpu-silicon.md` as "occupied, then empty", so a later reader does not think the file map is
  incomplete.
- **The record grows by one row per finding** (`FINDINGS.md`, #94): a derived rule that had been an
  expression, a bookkeeping field that turned out to be a latch.

## Considered options

1. **A microcode interpreter fed by the ROM dumps.** Refused by the owner twice (#78, and again when the
   map was charted): it is not the readability the campaign is for, and it would put the 544-word dump in
   the runtime.
2. **Splitting register storage into High/Low/Data cells.** Refused in §3: a layout fact with no
   behavioural consequence, paid for in G8.
3. **A flat surface with prefixes for the colliding names** (`PinA1`, `RegA1`, `PinD0`). Refused: it
   invents two vocabularies to name one part, and the freeze test would then enforce the invention.
4. **A `RegisterFile` nested type holding `D0`–`A7`** for symmetry with `Pins`. Refused: the register file
   *is* the part's face, no consumer needs the extra hop, and the collision that forces `Pins` does not
   exist here.
5. **A separate `M68000Pins` type outside the class.** Refused: the pins are views on the same control
   record the core writes; a second type would need a reference back and would put the pin surface one
   name away from the part it belongs to.
6. **Keeping the counters as "real" public members** (as today, undocumented). Refused: they are the
   clearest example of a name the manual does not have, and leaving them loose is what makes "exactly and
   only" unenforceable.
7. **Re-expressing the seam as the silicon's state** (`SingleStepState` carrying `AU`/`AT`/`SSW`).
   Refused: the corpus's records are the manual's shape, and a seam that speaks latches makes every
   harness change a state migration.
8. **Re-blessing a moved fold pair as a new baseline.** Refused by the owner before this ticket: the
   campaign is a re-expression, so a moved pair means a derivation was wrong.

## Falsifier

- **A public member outside the frozen list** — added, renamed, or missing — fails #85's reflection test.
- **Any corpus record moves**: MAME must hold `317 500 / 317 500` on state *and* exact-clock, CLK must
  hold 863 265 with an identical red-file set, at every landing.
- **The fold pair moves** (`f8d39bf1…` / `9e362791…`, desktop 602, 34 122.2 pops) — a bug report, on the
  spot, not a re-bless (a placement the *die* has differently is the one exception, and it is the owner's
  to bless: `#92`, row 82).
- **G8 above `parent + 0.3 ms`** at equal `frame_n` over 3 interleaved Release pairs, or above 10 ms —
  the re-expression cost more than the machine can pay.
- **A field that cannot be cited** to a patent section, the silicon record, the instrument boundary of §6,
  or the time rule of §5.1 — the inventory grew a name back.
- **A duration under a bare noun, or a `…Clocks` field anywhere but `M68000.Emulator.cs`** — time has
  become storage.
- **A data table in the two stores' classes** — a `ushort[]` of control words, a `switch` over 544 arm
  numbers, or any dispatch the ROM content drives — the interpreter is back (§10, #78).
- **A script section without its line's comment**, or a comment whose address does not match
  `microcode-walk.py` — the unrolled code has stopped being readable beside the dump (§10).
- **A display member that is not a row of §1.1's table**, or a `Decode` family with no `Family` member (or
  the reverse) — the surface has grown a synonym the manual does not have.


## The owner's answers

Every open question was answered in the grilling session (2026-11-21). His words are the decision; the
section named is where the document keeps it.

| # | question | his answer | kept in |
|---|---|---|---|
| 1 | where the internal clocks live | the charge is a **local counter in the script**: "add 2 to a local variable counter as a suffix to each of these sections, then use this local variable counter when calling an engine async function to sync the chip time" | §5 |
| 2 | what an instrument is allowed to be | keep them in the Release build, but "put it in a separate 'emulator interface' file, with the chip time simulation field" | §6, §5.1 |
| 3 | `ALUO` and `ISR`, named by role only in the patents | "ok for these names" | §8.5 |
| 4 | the decode functions' contract | "ok" — one public static function per manual family, `bool <Family>(ushort, out Decoded)` | §1 |
| 5 | the display set of the manual's concepts | "ok" — five members, four concepts refused | §1.1 |
| 6 | the file split | "one Registers file"; "Pins as its own file"; "you can omit everything related to E in this emulator"; "one file" for the exception; the two stores are **functions, not tables**; `Scripts` splits or not by #89's evidence | §10, `design/cpu-silicon.md` §1 |

Two of his answers changed more than a paragraph, and both are now rules rather than placements:

- **§5's local counter** retires the last two bookkeeping fields (`_own`, `_eaCharged`) *and* the reset
  discipline they needed: nothing is carried across a boundary, so nothing has to be cleared at one.
- **§10's two stores** make the ROM content the code: the store files hold the elementary methods, the
  scripts hold the lines with their comments, and a data table anywhere is the interpreter returning.
