# The M68000 Core: Execution, Timing and Bus Model

> An Atari STe hardware overview for emulator authors, focused on the CPU. Companion to
> [01-emulation-technical-solutions.md](./01-emulation-technical-solutions.md), which covers
> implementation strategies; this document covers *what the hardware actually does* and which
> parts of that behaviour an emulator is obliged to reproduce.

## Contents

1. [Why three separate levels of modelling](#1-why-three-separate-levels-of-modelling)
2. [The instruction set](#2-the-instruction-set)
3. [Addressing modes are execution, not syntax](#3-addressing-modes-are-execution-not-syntax)
4. [The instruction execution model](#4-the-instruction-execution-model)
5. [Bus cycles](#5-bus-cycles)
6. [Is the 68000 microcoded?](#6-is-the-68000-microcoded)
7. [Translating this into an emulator architecture](#7-translating-this-into-an-emulator-architecture)
8. [Worked example: `ADD.W (A0),D0`](#8-worked-example-addw-a0d0)
9. [References and recommended study order](#9-references-and-recommended-study-order)

---

## 1. Why three separate levels of modelling

For an Atari STe emulator, the 68000 has to be understood at three distinct levels:

- **Instruction-set level** — what each instruction does architecturally: register changes,
  memory accesses, condition codes, exceptions.
- **Execution level** — how the processor sequences instruction fetch, effective-address
  calculation, operand processing and completion.
- **Bus and timing level** — when the 68000 requests memory, transfers words, acknowledges
  interrupts, or yields the bus.

These levels are related but not interchangeable. Knowing that `MOVE.W (A0),D0` reads a word
from memory does not, by itself, tell you when the read happens relative to internal execution,
nor how many cycles a given effective address costs.

One distinction matters from the outset: the original MC68000 is **not** a processor for which
Motorola published a complete, programmer-usable, per-internal-clock micro-operation trace of
every instruction. The architecture, instruction encodings, bus protocol and documented timing
are well specified. The precise internal implementation is a different matter — see §6.

## 2. The instruction set

The authoritative starting points are Motorola's *M68000 Family Programmer's Reference Manual*
(instruction set, addressing modes, condition codes) and *M68000 8-/16-/32-Bit Microprocessors
User's Manual* (bus protocol, signals, timing tables).

The Atari STe's 68000 implements the **original MC68000 instruction set**, not the instruction
set of later 680x0 processors. Do not accidentally implement MC68010, 68020 or later
instructions simply because a general-purpose 68k reference lists them.

### 2.1 Instruction families and principal variants

| Family | Instructions and important variants |
|---|---|
| Data movement | `MOVE.B/.W/.L`, `MOVEA.W/.L`, `MOVEQ`, `MOVEM.W/.L`, `MOVEP.W/.L`, `EXG`, `SWAP`, `EXT.W/.L`, `LEA`, `PEA` |
| Integer arithmetic | `ADD`, `ADDA`, `ADDI`, `ADDQ`, `ADDX`; `SUB`, `SUBA`, `SUBI`, `SUBQ`, `SUBX`; `MULS`, `MULU`, `DIVS`, `DIVU` |
| Compare and test | `CMP`, `CMPA`, `CMPI`, `CMPM`, `TST`, `CHK` |
| Logical | `AND`, `ANDI`, `OR`, `ORI`, `EOR`, `EORI`, plus immediate forms targeting CCR/SR |
| Negation and clearing | `NEG`, `NEGX`, `NOT`, `CLR` |
| Bit manipulation | `BTST`, `BCHG`, `BCLR`, `BSET`; bit number immediate or from a data register |
| Shifts and rotates | `ASL`, `ASR`, `LSL`, `LSR`, `ROL`, `ROR`, `ROXL`, `ROXR`; register-count and immediate-count forms, plus memory forms where supported |
| Binary-coded decimal | `ABCD`, `SBCD`, `NBCD` |
| Branches and conditions | `BRA`, `BSR`, `Bcc` (all condition-code conditions), `DBcc`, `Scc` |
| Jumps and subroutines | `JMP`, `JSR`, `RTS`, `RTR`, `RTE` |
| Stack and procedure support | `LINK`, `UNLK`, `PEA`, `MOVEM`, `BSR`, `JSR`, `RTS` |
| Status and system control | `MOVE` to/from SR, CCR and USP where permitted; `ORI`/`ANDI`/`EORI` to CCR or SR; `RESET`, `NOP`, `STOP`, `TRAP`, `TRAPV`, `ILLEGAL` |
| Bus control / atomic | `TAS` on an eligible data-alterable operand — a read-modify-write operation; `TRAP` and exception processing also have system-level effects |
| Special and exceptional behaviour | `RESET`, `STOP`, `RTE`, `ILLEGAL`, `TRAP`, `TRAPV`, `CHK`, address-error and bus-error exception entry, interrupt acknowledge |

Five clarifications that catch out emulator authors:

- **Suffixes matter.** `.B`, `.W`, `.L` select byte, word and longword operations where the
  instruction supports them. Some instructions do not support all sizes.
- **Address registers have special rules.** `MOVEA` does not update condition codes;
  `ADDA`/`SUBA` operate on address registers rather than behaving like ordinary data-register
  arithmetic.
- **Conditions are variants, not separate hardware instructions.** `BNE`, `BEQ`, `BHI`, `BLE`
  share one condition-evaluation scheme and differ only in the condition tested.
- **The effective address changes the work required.** `ADD.W D0,D1`, `ADD.W (A0),D1` and
  `ADD.W D0,(A0)` are the same arithmetic family but not timing-equivalent.
- This is a family-level overview, not an encoding table. A real emulator also needs reserved
  encodings, privilege restrictions, legal effective-address combinations and per-instruction
  size restrictions.

### 2.2 Instructions that are *not* MC68000

Features belonging to later processors — implementing any of these silently would be a
compatibility bug:

- `BKPT`, `MOVEC`, `RTD` — MC68010 or later.
- `LINK.L`, bit-field instructions, `CAS`, `CHK2`, `CMP2`, `PACK`, `UNPK` — later-family
  features, availability varying by instruction.
- `MOVES`, `EXTB.L`, scaled indexing, and the 68020's richer effective-address forms.

For the STe, the CPU's own decoder should enforce the original 68000 legality rules. Do not use
a 68020 decoder and assume programs simply won't execute the extra instructions.

## 3. Addressing modes are execution, not syntax

The instruction set is compact largely because many instructions share the same addressing
modes. Motorola documents 14 addressing modes for the original processor.

| Mode | Typical syntax | What the processor must do |
|---|---|---|
| Data register direct | `D0` | Read or update an internal register |
| Address register direct | `A0` | Read or update an address register, if allowed |
| Address indirect | `(A0)` | Read or write memory at the address in A0 |
| Postincrement | `(A0)+` | Access memory, then increment the address register |
| Predecrement | `-(A0)` | Decrement the address register, then access memory |
| Displacement | `16(A0)` | Fetch an extension word, add signed displacement, access memory |
| Indexed | `8(A0,D1.W)` | Fetch displacement, combine base and index, access memory |
| Absolute short | `($1234).W` | Fetch an extension word and form an address |
| Absolute long | `($00123456).L` | Fetch two extension words and form an address |
| PC-relative displacement | `label(PC)` | Use the instruction's PC-relative base plus an extension word |
| PC-relative indexed | `label(PC,D0.W)` | Combine PC-relative base, index and displacement |
| Immediate | `#$1234` | Fetch operand data from the instruction stream |
| Implied / register-specific | `SR`, `CCR`, `USP`, … | Operate on a register or state named by the instruction |

These are not assembler conveniences: they change the number and order of instruction-stream
fetches, internal calculations and data-bus transactions.

Concretely, a longword read from memory on the original 68000 normally takes two 16-bit bus
transfers; a byte access uses the appropriate byte lane; and an odd-address word or longword
access raises an **address-error exception** rather than being transparently split into byte
operations. That last point is essential for Atari compatibility — the CPU does not make every
unaligned access work merely because the host machine tolerates it.

## 4. The instruction execution model

There is no single small, publicly documented state machine of the form *"state 0 = fetch,
state 1 = decode, state 2 = add"* that fully describes every MC68000 instruction. Instead, think
of execution as an instruction-sequencing process that uses internal operations and invokes bus
cycles as required. Different instructions follow different paths, and exceptions or bus delays
can alter the path.

1. **Instruction prefetch / fetch** — obtain instruction words from memory; maintain the
   instruction stream and PC.
2. **Decode** — interpret opcode fields, operand size, addressing modes and required extensions.
3. **Resolve operands** — calculate effective addresses, fetch extension words or immediate
   data, read source operands as needed.
4. **Execute / update state** — perform arithmetic, logical, shift, comparison or control-flow
   operations; update registers and flags where specified.
5. **Complete writes and control changes** — write results to memory or registers; resolve
   branches, stack changes and instruction-specific completion.

This is a conceptual sequence, not a literal five-state silicon machine. Prefetch can overlap
with execution, and an instruction may interleave internal work with bus transfers. The sequence
is not always linear either: an instruction may need several extension-word fetches, a read
followed by a write, repeated register or memory operations, or exception processing.

### 4.1 The instruction prefetch queue matters

The original 68000 uses a small instruction-prefetch mechanism. In practical terms:

- The CPU can fetch instruction words before it needs to execute them.
- A branch, jump, exception or other control-flow change may invalidate words already fetched
  from the old instruction stream.
- Memory-mapped I/O makes the timing and ordering of instruction fetches observable.
- A write to memory that also contains upcoming instructions can interact with already
  prefetched words.

A single `PC += instruction_length` followed by immediate execution reproduces many programs,
but it does not inherently reproduce these effects. A higher-accuracy core needs an explicit
model of instruction fetches and the relevant prefetch state.

### 4.2 Exception processing is a separate execution path

Interrupts and exceptions are not ordinary instructions, even though their entry sequences
consume processor time and perform memory accesses. The processor may need to:

1. recognise an exception condition or interrupt request;
2. establish the correct supervisor state and stack;
3. save the required return state;
4. fetch the exception vector;
5. transfer control to the handler.

The exact sequence and saved state depend on the exception. Address errors and bus errors are
especially important because the CPU may need to preserve information about the failed access.
The original 68000's exception frame is **not** interchangeable with the MC68010's format.

Model exception entry as a defined CPU operation with real bus accesses — not as
`PC = vector_table[vector]`.

## 5. Bus cycles

The original MC68000 has a 16-bit external data bus, a 24-bit address bus and separate upper and
lower byte strobes. Motorola describes the standard bus protocol as a sequence of eight bus
states, **S0–S7**, for a basic bus cycle.

| States | Function (conceptual grouping) |
|---|---|
| S0–S1 | Select access |
| S2–S3 | Assert strobes |
| S4–S5 | Wait / sample |
| S6–S7 | Finish cycle |

Individual signal edges are defined by the manual's timing diagrams.

At 8 MHz one CPU clock period is 125 ns, so a four-clock bus cycle takes **500 ns** before any
wait states. That is the nominal bus-cycle duration, not the duration of every instruction.

### 5.1 Ordinary read cycle

1. The CPU establishes the function code, identifying the kind of access, and indicates a read.
2. It drives the address onto the address bus.
3. It asserts `AS` and the appropriate data strobe: `UDS`, `LDS`, or both.
4. The external device supplies data.
5. The CPU waits for an accepted cycle-termination response — normally `DTACK`, or a bus error
   through `BERR`.
6. The CPU completes the transfer and releases the bus signals.

If the target is not ready, the CPU inserts wait states. Two identical instructions can therefore
take different amounts of elapsed time if their memory accesses experience different wait
states.

### 5.2 Ordinary write cycle

The basic write sequence is similar, but the CPU drives the data bus instead of sampling it:
address and write direction are established, data becomes valid, the appropriate strobes are
asserted, and the processor waits for the target to terminate the cycle. A memory-mapped device
can therefore observe the timing of writes to its registers.

This is one reason an emulator should not implement every instruction as an instantaneous memory
operation followed by a fixed delay: a write may trigger a video, sound or peripheral state
change at a specific point on the shared machine timeline.

### 5.3 Longword access

A longword is 32 bits wide but the external data bus is only 16 bits wide. For

```asm
MOVE.L  (A0),D0
```

with an aligned `A0`, the memory operand normally requires two word reads: the word at `A0`,
then the word at `A0+2`, assembled into the 32-bit result. A longword write likewise requires
two word transfers. Instruction-extension fetches also consume bus bandwidth — they are not free
just because the emulator can decode them from an array.

An odd-address word or longword access is different: the original 68000 raises an address error
instead of splitting it into byte transfers.

### 5.4 Read-modify-write cycle

Some instructions read a memory operand, change it and write it back. The bus protocol supports
a read-modify-write sequence in which `AS` remains asserted across the whole operation, making it
distinguishable from two unrelated accesses. Examples: eligible memory-destination arithmetic
and logical instructions, bit-manipulation operations, and `TAS`.

Keep three concepts separate:

- **Instruction semantics** — read operand, calculate result, update flags as specified.
- **Bus sequence** — perform the read and the write with the correct ordering and signals.
- **Atomicity / arbitration** — model whether another bus master can intervene in that sequence.

Do not implement a memory-destination operation as a generic read plus a generic write if that
loses the hardware's read-modify-write signalling or arbitration behaviour.

### 5.5 CPU-space and interrupt-acknowledge cycles

The 68000 also performs special bus cycles, notably CPU-space cycles used for operations such as
interrupt acknowledgement. Interrupt handling is not simply a normal memory read of a vector
address: the processor first acknowledges the interrupt through its bus protocol, then performs
the required exception-entry operations and vector fetches.

On an Atari STe this matters because interrupt requests can originate from the MFP and other
hardware, and the CPU's recognition of those requests is affected by the interrupt mask,
instruction timing and exception processing.

### 5.6 Bus arbitration is a different mechanism

The 68000 supports bus arbitration through signals such as `BR`, `BG` and `BGACK`, which govern
a request to relinquish the bus and the transfer of ownership. These are distinct from `DTACK`,
which terminates an individual transaction.

For the STe emulator, keeping the three kinds of state apart is crucial:

| State | Question it answers |
|---|---|
| Bus-cycle state | What transaction is the CPU currently performing? |
| Bus-arbitration state | Who is permitted to use the bus? |
| Wait-state state | Has the current transaction completed yet? |

The Atari's video and DMA hardware make memory-access timing observable, so these must not be
collapsed into one generic "CPU is waiting" flag.

## 6. Is the 68000 microcoded?

The careful answer: **the public MC68000 documentation does not provide a complete, authoritative
internal microcode listing or a full per-clock micro-operation schedule for every instruction.**

It documents the instruction set, bus protocol, exception behaviour and instruction timing —
including the number of reads and writes for many instructions. It does not give emulator
developers a complete internal control-store program that can simply be translated into code.

| Concept | What it means for the 68000 |
|---|---|
| Architectural instruction definition | Documented: operands, results, flags, exceptions, legal forms |
| External bus-cycle sequence and timing | Documented to a useful hardware-engineering level |
| Exact internal micro-operation sequence, cycle by cycle | Not fully documented in the public programmer's/user's manuals |

The internal implementation necessarily has control logic sequencing the datapath and bus
interface. It is not safe, however, to infer the exact internal design — a particular microcode
format, control-store layout or microinstruction count — from programmer-visible documentation
alone.

A software emulator's "read source, calculate, write destination" sequence is a *model* of the
processor's behaviour, not proof that the physical chip executes those exact software-like steps
internally.

### 6.1 Going deeper than the manuals

- **Instruction timing tables** give baseline cycle counts and, often, read/write counts. They
  constrain the possible implementation.
- **Bus traces** reveal external addresses, transfer ordering, strobes, wait states and timing.
- **Hardware measurements and reverse engineering** reveal additional behaviour, especially
  around prefetch, interrupts and unusual instruction cases.
- **Existing CPU cores** encode tested models of instruction behaviour and timing. They are
  valuable references, but different cores model internal timing at different precisions.

None of these should be mistaken for an official, complete silicon microcode specification.

## 7. Translating this into an emulator architecture

Separate the 68000 core into three cooperating parts rather than building one enormous function
per instruction:

| Part | Responsibility |
|---|---|
| Instruction decoder and semantics | Opcode, size, legal addressing modes, register and flag effects |
| Execution sequencer | Prefetch, extension words, effective addresses, operand operations, exceptions, instruction completion |
| Bus interface and shared timeline | Timestamped reads and writes, byte lanes, wait states, bus ownership, interrupt sampling |

The bottom layer should talk to the Atari's memory map and bus scheduler, not to a flat RAM array
for every operation. That is what lets the CPU's accesses interact with the Shifter, GST MCU,
Blitter and DMA devices.

### 7.1 Two useful implementation levels

**Level 1 — instruction-level execution with bus-aware timing**

- Decode and execute one instruction at a time.
- Preserve all architectural effects and exceptions.
- Calculate timing from documented instruction and effective-address rules.
- Timestamp memory accesses and explicitly model important bus-visible effects.
- Account for prefetch and interrupt behaviour to the degree your accuracy target requires.

A good starting point — but a fixed cycle count added *after* an instruction cannot represent
every interaction accurately.

**Level 2 — bus-cycle-aware or micro-operation execution**

- Represent instruction execution as smaller operations.
- Each bus operation has an address, direction, transfer size and completion time.
- Internal operations can consume time between bus transfers.
- The CPU can be interrupted or delayed at the points where the hardware allows it.
- Prefetch, exceptions, read-modify-write sequences and arbitration become explicit state.

This level is much more suitable when you need accurate CPU/video/DMA interaction. It still does
not require simulating every master-clock edge: the shared timeline and scheduler can advance
directly to the next meaningful operation or event.

## 8. Worked example: `ADD.W (A0),D0`

At the architectural level the instruction reads a word from memory, adds it to the low word of
`D0`, writes the result to `D0` and updates the relevant condition codes.

At the emulator level you must account for:

1. instruction-word availability and decoding;
2. a memory read at the address held in `A0`;
3. the internal addition and flag calculations;
4. any instruction-stream prefetch overlapping with execution;
5. completion, and the time at which the next instruction can proceed.

The arithmetic is the easy part. Reproducing the ordering of instruction fetches, bus accesses
and internal execution is the hard part.

## 9. References and recommended study order

- **M68000 8-/16-/32-Bit Microprocessors User's Manual** — primary reference for bus operations,
  signals, exception processing and instruction execution timing.
- **M68000 Family Programmer's Reference Manual** — instruction definitions, opcode families,
  condition codes, addressing modes and legal operand combinations. Check the processor-specific
  tables: this manual also covers later family members.

Recommended progression:

**opcode encoding and legality → effective-address execution → bus cycles → prefetch and
exceptions → interrupt timing → instruction-level timing validation.**

That order builds a reliable foundation before tackling the hardest Atari-specific timing
interactions.

---

*Overview derived from an AI-assisted technical review, then restructured for reference use. It
describes documented MC68000 behaviour; where the public manuals are silent (§6), the text says
so rather than guessing.*
