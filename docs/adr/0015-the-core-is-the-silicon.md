# The core is the silicon: the manual's names outside, the parts' names inside

The 68000 core stops being a set of fields that happen to work and becomes the machine the two
authorities describe. **Outside**, its public surface is the programmer's model and the §8.1 signals of
`atari-st-hardware/cpu/02-mc68000-tutorial.md`, exactly and only, frozen by a reflection test — no invented member, no missing
one. **Inside**, every field is a latch one of the two Motorola patents names (`AU`, `AT`, `AOB`,
`DBIN`, `DOB`, `ALUB`, `ALUE`, `ALUO`, `FTU`, `DCR`, `PREN`, `IRC`/`IR`/`IRD`, `PSW`, `SSW`, `INL`,
`TPEND`), every value that is an expression on those latches is computed rather than stored, and today's
bookkeeping — the five overlapping address counters, the "was it charged" flags, the frame's memo
fields — is deleted. The code stays the coroutine it is (ADR 0009, #78's ruling): one named `async`
state method per microword state, one named arm per decode term, never a microcode interpreter.

**Status**: proposed (2026-11-21), ticket [#83](https://github.com/laurentprudhon/the-st/issues/83), the
first ticket of the campaign *CPU: the core is the silicon* ([#82](https://github.com/laurentprudhon/the-st/issues/82)).
Shape: [`design/cpu-silicon.md`](../design/cpu-silicon.md). **Three answers below are recommendations
awaiting the owner's word** — §5, §1's instrument boundary, and §8.4's two aliases; see
[Open questions](#open-questions-for-the-owners-word). Every other statement is a consequence of the two
authorities or of the three rulings recorded in Context, and does not wait on him.

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
  blessed fold pair is `run.digest` `4eac06dc…` / `run.stream` `1693218a…`, desktop at frame 602,
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

`M68000`'s declared public members are **the programmer's model**, **three nested types**, and **one
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

**`Instruments`** — the members the manual has no word for, in one place, each documented as *not the
part*:

| member | build | why it exists |
|---|---|---|
| `TicksPerCpuClock` | both | one CPU clock is four ticks of the 32 MHz oscillator (ADR 0001) — the engine's unit, not the part's |
| `Instructions`, `BusCycles`, `InternalClocks`, `InterruptsTaken`, `Exceptions` | both | the counters the Release `boot` reports and `cpu-dissection` reads by reflection — see §6 |
| `Halted`, `HaltReason` | both | the harness's diagnosis of a machine that went quiet; the part's own statement of the same thing is `Pins.HALT` held and the sequencer's STOP state |
| `Observer` | both | the debug instruction hook the boot's traces use |
| `CycleObserver`, `ClockObserver`, `LastFramePc`, `LastFrameFetchPointer`, `LastFramePcCounter`, `LastFrameIrAddress`, `LastFrameRefetched`, `LastFrameTrailing`, `LastFrameVector` | `#if DEBUG` | the per-record instruments `--trace`/`--clocks` print |

**`SingleStep`** — the corpus seam (ticket #34): `Load(in SingleStepState)`, `Capture()`,
`StepAsync()`. Its vocabulary is the seam's, and the seam *is* the programmer's model plus the two
prefetch words (§7). It is public because `CpuTestsCommand` is a different assembly, and it is nested
because it is not the part.

**`Launch()`** stays on the chip: every virtual chip is started that way (`Machine.cs` calls
`Launch` on the System Control Unit, the Video, the Timers and the CPU alike), so it is the engine's
verb — ADR 0006/0008's surface, not the 68000's.

**Retired:** `PeekD`/`PeekA` (→ `D0`…/`A0`…), `Supervisor` (→ `S`), `InterruptMask` (→ `I2`,`I1`,`I0`),
`Usp`/`Ssp` (→ `USP`/`SSP`).

### 2. A nested type is a place, not a name

`Pins`, `Instruments` and `SingleStep` are words the manual does not have, and that is allowed for one
reason only: they name **where a thing lives**, never **what the part is**. The rule that follows is
mechanical, and it is what makes "exactly and only" enforceable: *every member inside a nested type is
named by its own authority* — `Pins.A1` by §8.1, `Instruments.BusCycles` by nobody (it says so), and no
nested type may ever be used to smuggle an alias in (`Pins.UDStrobe`, `Instruments.Supervisor`).

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
costs four"). The charge is a property of *being in a state*, so it belongs where the state is:
`IdleAsync()` is that charge, and there is no `_own`, no `Charge` on the sequencer, and no per-access-class
table of charges.

- **`M68000.Timing.cs` becomes a bridge with a deadline, not a home.** Its rows charge families whose
  scripts are still MAME's tables; each row dies as the family's script carries its own states, and the
  file is empty at the end of #90. The exact-clock column (317 500 today) is the witness for every
  deletion — a family whose row is removed before its script charges goes short by 2/4/6 and the class is
  visible in `--clocks` grouped by shape.
- **`_own` and `_eaCharged` are therefore deletions with no successor.** The EA's "2 clocks once per
  instruction" (`_eaCharged`) is a fact about the *address-calculator state* the script runs, and once the
  shape's states are transcribed the once-ness is written where the state is, not remembered in a flag.
- The alternatives were refused for the reason this campaign exists: (b) "the access class the script
  declares" puts a clock count on a bus *label*, and the label's job is to say which bus access to start
  (§8), not how long the instruction takes; (c) "a named `Charge` on the sequencer" is this decision with
  an invented object in front of it — a counter the silicon does not have, which is what `_own` already was.

### 6. What an instrument is allowed to be

An instrument is a member that exists for the harness, the boot or a reader, and is documented as not the
part. Two boundaries, and the difference is a *consumer*, not a taste:

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
4. **`ALUO` and `ISR` are admitted as aliases, and the table says so.** The patent names the ALU's result
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
- **The fold pair moves** (`4eac06dc…` / `1693218a…`, desktop 602, 34 122.2 pops) — a bug report, on the
  spot, not a re-bless.
- **G8 above `parent + 0.3 ms`** at equal `frame_n` over 3 interleaved Release pairs, or above 10 ms —
  the re-expression cost more than the machine can pay.
- **A field that cannot be cited** to a patent section, the silicon record, or the instrument boundary of
  §6 — the inventory grew a name back.


## Open questions for the owner's word

Three, and only three. Each is answered above in the form this ADR will keep if he agrees with it; a
different word changes the paragraph named, not the document.

| # | question | this ADR's answer | what changes if it goes the other way |
|---|---|---|---|
| 1 | where the internal clocks live once `_own` is gone | **in the state** (§5): a microword state charges 2, an access 4; `_own`/`_eaCharged` are deleted with no successor, and `M68000.Timing.cs` empties family by family | (c) a named `Charge` on the sequencer keeps a counter-shaped object; (b) the access class carries a clock count, which is not what §8 says the label is for. Either way #90's scope stands, only its shape moves |
| 2 | what an instrument is allowed to be, and where | **two boundaries by consumer** (§6): per-record instruments behind `#if DEBUG`, the five counters and `Halted`/`HaltReason` unconditional because the Release `boot` and `cpu-dissection` read them | keeping all of them behind `#if DEBUG` requires the Release boot to stop printing its counters — a change to the gate's own report, and then #84's verdict loses a column |
| 3 | `ALUO` and `ISR`, which the patents name only by role | **admitted into `nomenclature.md` §3 as aliases**, with their provenance (the microcode listing and the die analysis), because inventing a role-name would break §8's rule 3 | refusing them leaves the ALU's result and flag latches with made-up names, which is the one thing §8 forbids |

The corpus seam (§7), the nested-type rule (§2), the storage decision (§3) and the inventory (§4) are
not open: the first three are the owner's own rulings, and the fourth is the inventory he asked #83 to
freeze.
