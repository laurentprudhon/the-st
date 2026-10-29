# Atari STE hardware overview for emulation

A working description of the Atari 520STE / 1040STE for someone building an emulator: what the
machine is made of, how the parts exchange data, which behaviours software can observe, and
which of those a targeted emulator has to reproduce to run ST/STE software correctly.

The document is organised by subsystem, roughly bottom-up: the machine and its board generations,
the CPU and system bus, interrupts, memory and arbitration, the custom chips, video, sound, the
Blitter, storage DMA, and finally the timing architecture of the emulator itself. Each chapter
ends with what it implies for implementation.

Companion material:

- [`sources/ste-study-transcript.md`](./sources/ste-study-transcript.md) — the raw multi-day
  technical discussion this overview was reorganised from.
- [`01-emulation-technical-solutions.md`](./01-emulation-technical-solutions.md) — catalogue of
  implementation approaches used by existing emulators.
- [`../atari-st-hardware/`](../atari-st-hardware/) — component-level hardware wiki (datasheets,
  pinouts, register semantics).

## Contents

- [0. Scope and modelling stance](#0-scope-and-modelling-stance)
- [1. The machine](#1-the-machine)
  - [1.1 ST versus STE](#11-st-versus-ste)
  - [1.2 STE board generations, and which one to target](#12-ste-board-generations-and-which-one-to-target)
  - [1.3 The clock tree](#13-the-clock-tree)
  - [1.4 Functional subsystem map](#14-functional-subsystem-map)
  - [1.5 Connectors and the hardware behind them](#15-connectors-and-the-hardware-behind-them)
- [2. The 68000 and the system bus](#2-the-68000-and-the-system-bus)
  - [2.1 Architecture and registers](#21-architecture-and-registers)
  - [2.2 The instruction set — and what is not 68000](#22-the-instruction-set--and-what-is-not-68000)
  - [2.3 Addressing modes are execution](#23-addressing-modes-are-execution)
  - [2.4 The execution model](#24-the-execution-model)
  - [2.5 Bus cycles](#25-bus-cycles)
  - [2.6 Function codes](#26-function-codes)
  - [2.7 Is the 68000 microcoded?](#27-is-the-68000-microcoded)
- [3. Interrupts](#3-interrupts)
  - [3.1 Levels, masking and acceptance](#31-levels-masking-and-acceptance)
  - [3.2 The interrupt acknowledge cycle, pin by pin](#32-the-interrupt-acknowledge-cycle-pin-by-pin)
  - [3.3 ISR entry and return](#33-isr-entry-and-return)
  - [3.4 How the ST/STE wires it up](#34-how-the-stste-wires-it-up)
  - [3.5 The MFP as interrupt controller](#35-the-mfp-as-interrupt-controller)
- [4. Memory, refresh and bus ownership](#4-memory-refresh-and-bus-ownership)
  - [4.1 The MMU / RAM interface](#41-the-mmu--ram-interface)
  - [4.2 How video reads happen while the CPU "owns" the bus](#42-how-video-reads-happen-while-the-cpu-owns-the-bus)
  - [4.3 Arbitration: the four algorithms](#43-arbitration-the-four-algorithms)
- [5. GLUE and GSTMCU](#5-glue-and-gstmcu)
- [6. Video](#6-video)
  - [6.1 Chips and responsibilities](#61-chips-and-responsibilities)
  - [6.2 Modes and bitplanes](#62-modes-and-bitplanes)
  - [6.3 The timing state machines](#63-the-timing-state-machines)
  - [6.4 The Shifter pipeline](#64-the-shifter-pipeline)
  - [6.5 Register map](#65-register-map)
  - [6.6 Register timing and scrolling](#66-register-timing-and-scrolling)
  - [6.7 Raster tricks and implementation order](#67-raster-tricks-and-implementation-order)
- [7. Sound](#7-sound)
  - [7.1 The YM2149](#71-the-ym2149)
  - [7.2 Stereo DMA sound](#72-stereo-dma-sound)
  - [7.3 Three kinds of RAM access](#73-three-kinds-of-ram-access)
- [8. The Blitter](#8-the-blitter)
- [9. Floppy, ACSI and the disk DMA](#9-floppy-acsi-and-the-disk-dma)
- [10. Emulator architecture](#10-emulator-architecture)
  - [10.1 Three levels of emulation](#101-three-levels-of-emulation)
  - [10.2 What existing emulators do](#102-what-existing-emulators-do)
  - [10.3 A timing architecture](#103-a-timing-architecture)
  - [10.4 Implementation ladder](#104-implementation-ladder)
- [Appendix A. Address and register quick reference](#appendix-a-address-and-register-quick-reference)
- [Appendix B. Document provenance](#appendix-b-document-provenance)

---

## 0. Scope and modelling stance

Three words get used interchangeably in emulator discussions and should not be: *simulation*,
*emulation* and *silicon modelling*. This document assumes the following split.

**Level 1 — functional emulation.** A device produces the right architectural results: registers
read back what software expects, memory contains the right bytes, the screen eventually shows the
right image. Timing is approximated per instruction or per frame. Enough for applications.

**Level 2 — cycle-level, bus-aware emulation.** Individual bus accesses carry timestamps, the
devices that share RAM are scheduled against each other, and the order of accesses is meaningful.
Needed for demos, raster effects, and anything that races the beam or the Blitter.

**Level 3 — electrical/silicon modelling.** Signal-level transitions at sub-cycle granularity.
Almost no software emulator does this; FPGA cores approach it. It is not the target here.

Everything below is written from the point of view of Level 2, with Level 1 as the fallback when
a behaviour is provably unobservable. Where the public documentation does not support a claim,
this document says so instead of inventing a mechanism.

**Reference machine.** Unless stated otherwise, "the STE" means a mature-production
CA4003290 board with a 68000 at 8 MHz, TOS 1.62, 30-pin SIMMs, PAL video at 50 Hz, and the
French Peritel output variant. That is a defensible single target for a first emulator; see §1.2.

---

## 1. The machine

### 1.1 ST versus STE

The STE keeps the ST architecture — 68000, 16-bit data bus, memory-mapped I/O, TOS in ROM — and
changes the chipset around it.

| Feature | Original ST/STF | STE |
|---|---|---|
| CPU | 8 MHz 68000 | 8 MHz 68000 |
| GLUE / MMU | Two separate Atari chips | Combined into **GSTMCU** |
| Disk DMA controller | Dedicated Atari chip | Dedicated chip remains (revised part numbers) |
| Shifter | Original video chip | Enhanced Shifter (4096 colours, scrolling, sound DMA) |
| Palette | 512 possible colours (3 bits/channel) | 4096 possible colours (4 bits/channel) |
| Fine scrolling | Limited, software-assisted | Hardware horizontal and vertical scroll |
| Sound | YM2149 PSG | YM2149 plus 8-bit stereo DMA audio |
| Blitter | Optional, separate IC | Standard; on later boards integrated into a large SMD custom chip |
| RAM | Often soldered DRAM | 30-pin SIMM sockets on standard boards |

Two consequences for an emulator:

1. **GLUE and MMU collapse into one device.** Code structure that treats address decoding and DRAM
   control as one GSTMCU component is closer to the STE than two cooperating chips.
2. **The disk DMA controller does not disappear.** Floppy and ACSI transfers still go through a
   separate DMA chip that requests the bus; it is not part of the GSTMCU and not part of the WD1772.

### 1.2 STE board generations, and which one to target

The STE shipped across at least two motherboard families and several revisions within each.

| Board variant | Main visible differences | Emulator significance |
|---|---|---|
| **CA4003290 — early (1989)** | Factory bodge wires; one chip piggybacked on another; some boards with SIPP memory sockets or black SIMM sockets | Possible bus-timing and DMA-stability differences; production-in-progress design |
| **CA4003290 — second** | The previously piggybacked chip is mounted directly on the board; some examples still retain SIPP sockets | Mostly physical change unless it affects signal timing |
| **CA4003290 — third** | Separate Blitter IC removed, its function integrated into the large surface-mount custom chip; revised DMA chip may appear around this period | Potentially relevant if internal timing or register behaviour differs |
| **CA401177 — later (1991)** | More extensive surface mounting, including CPU, Shifter and DMA; fewer sockets | Distinct later hardware implementation |

Other production variation that gets confused with "revision": RAM population (520STE vs
1040STE), TOS 1.06 versus 1.62 ROMs, and the French Peritel variant, which omits the RF modulator
and drives video out directly. TOS version is not a board revision; the DMA chip revision is not
reliably identifiable without reading the part number, since boards were repaired with
replacements.

**Recommended target: a mature CA4003290, TOS 1.62, 30-pin SIMM memory, French PAL / Peritel.**
Avoid the early bodge-wire boards (their quirks are hard to characterise) and avoid CA401177
(a later, differently-built machine). Do not add a per-revision switch until a test demonstrates a
real observable difference — the table below is the list of candidates worth watching.

What actually matters versus what does not:

| Difference | Likely emulator impact | What to do |
|---|---|---|
| Bodge wires, resistor-pack values | Bus-arbitration stability, edge timing | Model bus ownership properly; investigate only if tests fail |
| SIPP versus SIMM sockets | None visible to software | Model RAM size and memory map, not the socket |
| Discrete versus integrated Blitter | Possibly internal timing or register-behaviour differences | Implement the documented register interface; validate against software |
| DMA chip revision | Floppy DMA compatibility, corner cases | Keep DMA modular; test against several chip variants |
| TOS 1.06 versus 1.62 | Directly changes software behaviour and bug compatibility | Default to TOS 1.62 |
| French Peritel | Video standard and physical output, not the CPU | Target 50 Hz PAL; treat SCART as an output concern |

Development strategy follows from this: implement documented STE behaviour against TOS 1.62, make
bus arbitration / DMA / Blitter / video fetch cycle-aware, run compatibility tests, and only then
add a revision switch if evidence demands it.

### 1.3 The clock tree

Frequency sources are fewer than the number of clock frequencies present.

| Physical source | Typical frequency | Used for |
|---|---|---|
| Main master crystal | ≈ 32 MHz | Video timing and the whole derived system clock tree |
| MFP baud-rate crystal | 2.4576 MHz | Accurate serial baud rates in the MC68901 |
| Colour-subcarrier crystal (RF-modulator models only) | 4.433618 MHz (PAL) / 3.579545 MHz (NTSC) | Colour encoding for the RF/composite path |

Derived clocks distributed to the chips: 16 MHz (video/shifter domain), 8 MHz (CPU and bus),
4 MHz (MFP), 2 MHz (YM2149), 0.5 MHz (ACIAs). The STE adds a separate 8 MHz sound-clock input to
the GST Shifter, which is why DMA sample rates are not simple divisions of the CPU clock.

For the emulator this means three things: keep the frequency relationships as exact integers or
rationals (PAL 32 MHz / N), treat the sound clock as its own domain, and never let host frame
timing drive the internal timeline.

### 1.4 Functional subsystem map

A functional decomposition is more useful than a chip list during implementation, because most
connectors and several chips span subsystems.

- **A — Video**: GST Shifter pixel path, GSTMCU timing, video address control, RAM fetches, monitor output.
- **B — Sound**: YM2149, GST Shifter DMA-audio path, sample latches and DACs, filters, LMC1992 mixer, audio connectors.
- **C — Serial (RS-232)**: MFP UART, line drivers, DB-25 modem port.
- **D — MIDI**: MC6850 ACIA, current-loop in/out circuits, DIN-5 connectors.
- **E — Disk and storage**: DMA controller, WD1772 FDC, GSTMCU bus coordination, YM2149 drive-control bits, MFP interrupt input, floppy and ACSI connectors.
- **F — Controllers**: keyboard ACIA, IKBD microcontroller inside the keyboard, enhanced-controller logic, LM556 paddle timers, DE-9 and DA-15 ports.
- **G — Expansion**: 40-pin cartridge connector, 68000 bus, GSTMCU decoding, cartridge ROM/peripheral.
- **H — Parallel (printer)**: YM2149 parallel ports, handshake logic, DB-25 printer port.
- **I — Power**: mains inlet, PSU, regulators, reset circuitry.
- **J — RAM access, refresh and memory-cycle generation**: GSTMCU DRAM address/timing, SIMM devices, shared data path, and the memory clients (Shifter video, Shifter sound, disk DMA, CPU, Blitter).
- **K — CPU, buses, address mapping and chip selects**: 68000 buses and control, GSTMCU decode and cycle termination, ROM, RAM, peripherals, Blitter register interface.
- **L — System coordination**: clocks, interrupt generation and routing, bus arbitration, reset distribution.

Component-to-subsystem index (useful when deciding what a device model must talk to):

| Component | Subsystems |
|---|---|
| MC68000 | K, L |
| GSTMCU | A, B, E, F, G, H, J, K, L |
| GST Shifter | A, B, J, L |
| DRAM SIMMs | A, B, E, J, K |
| Blitter | A, J, K, L |
| DMA controller | E, J, K, L |
| WD1772 | E, L |
| YM2149 | B, E, H, K |
| MC68901 MFP | C, E, F, L |
| MC6850 keyboard ACIA | F, K, L |
| MC6850 MIDI ACIA | D, K, L |
| IKBD | F |
| LMC1992 | B, K |
| ROMs | G, K |

The design lesson: **the GSTMCU is the hub**. Anything that touches RAM, timing, or interrupts
touches it, which is why it is the first device whose behaviour must be right.

### 1.5 Connectors and the hardware behind them

Grouping external connectors by the chip that actually serves them exposes which device models are
required to "have ports working".

| External connector | Owning hardware |
|---|---|
| Monitor, 13-pin DIN | GST Shifter + GSTMCU + video output stage (RGB, HSYNC, VSYNC, mono, audio) |
| TV/RF out, RCA | Video encoder and RF modulator (absent on Peritel models) |
| Audio left / right RCA | STE DACs, filters, LMC1992 |
| RS-232 modem, DB-25 | MC68901 MFP UART + line drivers |
| Printer, DB-25 | YM2149 parallel ports + handshake logic |
| ACSI hard disk, DB-19 | DMA controller + GSTMCU/system bus |
| External floppy, DIN-14 | WD1772 + DMA controller + drive-control logic |
| MIDI IN / OUT, DIN-5 | MC6850 ACIA + current-loop circuits |
| Mouse/joystick 0 and 1, DE-9 | IKBD and its serial link |
| Enhanced controller A and B, DA-15 | STE enhanced-controller logic (extra buttons, paddles) |
| Cartridge, 40-pin edge | 68000 bus and GSTMCU decoding |
| Mains, IEC C14 | Internal PSU |

Internal connectors worth remembering: 34-pin floppy ribbon, keyboard harness, four 30-pin SIMM
sockets (one bank on a 520STE, two on a 1040STE), two 32-pin TOS ROM sockets, chip sockets for
CPU and Blitter on earlier boards.

Two non-obvious consequences: the **printer port is served by the sound chip's I/O ports**, so an
emulator that stubs the YM2149 breaks printing; and **drive select / side select are also YM2149
port bits**, so floppy emulation depends on PSG register writes, not only on the WD1772.
---

## 2. The 68000 and the system bus

The 68000 has to be understood at three separate levels:

- **Instruction-set level** — register changes, memory accesses, condition codes, exceptions.
- **Execution level** — how the processor sequences fetch, effective-address calculation, operand
  processing and completion.
- **Bus and timing level** — when it requests memory, transfers words, acknowledges interrupts,
  or yields the bus.

Knowing that `MOVE.W (A0),D0` reads a word does not tell you when the read happens relative to
internal execution, nor what the effective address costs in cycles.

### 2.1 Architecture and registers

Sixteen 32-bit general registers: `D0–D7` for data, `A0–A7` for addresses, with `A7` acting as the
stack pointer (and `A7'` the supervisor stack pointer shadow). A 32-bit PC, a 16-bit status
register (condition codes, interrupt mask `I2–I0`, supervisor bit `S`, trace bit `T`), and the
alternate `USP`/`SSP` pair.

Address registers behave differently from data registers in ways software depends on: word-sized
operations on an address register write the low 16 bits and *sign-extend* into the upper half;
byte-sized operations are not allowed on `An` for most instructions.

**Frames and the frame pointer.** `A7` tracks the top of stack; `A6` conventionally holds a fixed
pointer into the current frame so locals can be reached at constant offsets regardless of how much
the stack moves. `LINK A6,#-4` pushes the old `A6`, sets `A6` to the current stack position and
reserves locals; `UNLK A6` restores `A7` from `A6` and pops the previous `A6`; `RTS` returns. This
matters to an emulator only because `LINK`/`UNLK` are ordinary instructions that must produce
byte-identical stack contents — debuggers and crash handlers walk those frames.

### 2.2 The instruction set — and what is not 68000

The STE's 68000 implements the *original* MC68000 set. Later-family instructions are not available.

| Family | Instructions and variants |
|---|---|
| Data movement | `MOVE.B/.W/.L`, `MOVEA.W/.L`, `MOVEQ`, `MOVEM.W/.L`, `MOVEP.W/.L`, `EXG`, `SWAP`, `EXT.W/.L`, `LEA`, `PEA` |
| Integer arithmetic | `ADD`, `ADDA`, `ADDI`, `ADDQ`, `ADDX`; `SUB`, `SUBA`, `SUBI`, `SUBQ`, `SUBX`; `MULS`, `MULU`, `DIVS`, `DIVU` |
| Compare and test | `CMP`, `CMPA`, `CMPI`, `CMPM`, `TST`, `CHK` |
| Logical | `AND`, `ANDI`, `OR`, `ORI`, `EOR`, `EORI`, plus immediate forms to CCR/SR |
| Negation / clearing | `NEG`, `NEGX`, `NOT`, `CLR` |
| Bit manipulation | `BTST`, `BCHG`, `BCLR`, `BSET`; bit number immediate or from a data register |
| Shifts and rotates | `ASL`, `ASR`, `LSL`, `LSR`, `ROL`, `ROR`, `ROXL`, `ROXR`; register and immediate counts, plus memory forms where supported |
| BCD | `ABCD`, `SBCD`, `NBCD` |
| Branches / conditions | `BRA`, `BSR`, `Bcc`, `DBcc`, `Scc` |
| Jumps and subroutines | `JMP`, `JSR`, `RTS`, `RTR`, `RTE` |
| Stack and procedure support | `LINK`, `UNLK`, `PEA`, `MOVEM`, `BSR`, `JSR`, `RTS` |
| Status and system control | `MOVE` to/from `SR`, `CCR`, `USP` where permitted; `ORI`/`ANDI`/`EORI` to `CCR`/`SR`; `RESET`, `NOP`, `STOP`, `TRAP`, `TRAPV`, `ILLEGAL` |
| Bus / atomic | `TAS` — read-modify-write; `TRAP` and exception processing have system-level effects |
| Exceptional behaviour | address-error and bus-error entry, interrupt acknowledge, `RTE` |

Not 68000, and not to be implemented "for convenience":

- `BKPT`, `MOVEC`, `RTD` — MC68010 or later.
- `LINK.L`, bit-field instructions, `CAS`, `CHK2`, `CMP2`, `PACK`, `UNPK` — later family.
- `MOVES`, `EXTB.L`, scaled indexing, 68020 extended effective-address forms.

Four traps for the unwary: suffixes are not universal (`CLR.B` is legal, `MOVEA.B` is not);
`MOVEA` does not touch condition codes and `ADDA`/`SUBA` do not behave like `ADD`/`SUB` on data
registers; `Bcc` variants share one condition-evaluation path; and effective-address choice changes
the work even inside one mnemonic.

The practical rule: **enforce originality in the decoder**. Do not bolt a 68020 decoder onto the
STE and rely on software never executing the extras — illegal-instruction tests and copy
protection rely on getting an exception.

### 2.3 Addressing modes are execution

Motorola documents 14 modes for the original part. They are not assembler sugar: each changes the
number and order of fetch operations.

| Mode | Syntax | Work required |
|---|---|---|
| Data register direct | `D0` | Internal register access |
| Address register direct | `A0` | Internal register access, if allowed |
| Address indirect | `(A0)` | Memory at the address in `A0` |
| Postincrement | `(A0)+` | Access, then increment |
| Predecrement | `-(A0)` | Decrement, then access |
| Displacement | `16(A0)` | Fetch extension word, add signed displacement |
| Indexed | `8(A0,D1.W)` | Fetch displacement, combine base and index |
| Absolute short | `($1234).W` | One extension word forms the address |
| Absolute long | `($00123456).L` | Two extension words form the address |
| PC-relative | `label(PC)` | PC-relative base plus extension word |
| PC-relative indexed | `label(PC,D0.W)` | PC base + index + displacement |
| Immediate | `#$1234` | Operand data comes from the instruction stream |
| Implied / register-specific | `SR`, `CCR`, `USP` | Operate on a named register |

Two hardware facts follow directly: a longword memory operand costs **two** 16-bit bus transfers,
and an **odd-address word or longword access raises an address error** rather than being split into
bytes. The second is the one emulators most often get wrong, because the host happily performs
unaligned accesses that the 68000 traps.

### 2.4 The execution model

There is no publicly documented state machine of the form "state 0 = fetch, state 1 = decode". The
defensible model is a sequencing process that invokes bus cycles as needed:

1. **Prefetch / fetch** — obtain instruction words; maintain PC and the prefetch state.
2. **Decode** — opcode, size, addressing modes, extension words required.
3. **Resolve operands** — compute effective addresses, fetch extensions or immediates, read sources.
4. **Execute** — arithmetic, logic, shifts, comparisons, control flow; update registers and flags.
5. **Complete** — write results, resolve branches and stack updates.

This is conceptual, not silicon. Prefetch overlaps execution; an instruction may interleave
internal work with bus transfers; exceptions divert the sequence entirely.

**Prefetch matters observably.** The CPU can fetch words it does not yet execute; a branch,
jump or exception can invalidate already-fetched words; memory-mapped I/O and self-modifying code
make the fetch order visible. A `PC += length; execute()` core reproduces most software but none of
these effects.

**Exceptions are a separate path**, not instructions: recognise the condition, enter supervisor
state with the right stack, save the return state, fetch the vector, transfer control. Address and
bus errors must preserve information about the failed access, and the 68000's exception frame is
not the 68010's. Model exception entry as an operation with real bus accesses, never as
`PC = vector[vector_number]`.

### 2.5 Bus cycles

16-bit data bus, 24-bit address bus, separate upper/lower byte strobes, and an eight-state
(`S0–S7`) basic cycle. At 8 MHz one clock is 125 ns, so a four-clock bus cycle is **500 ns** before
wait states. That is the cycle duration, not the instruction duration.

| States | Function |
|---|---|
| S0–S1 | Select access |
| S2–S3 | Assert strobes |
| S4–S5 | Wait / sample |
| S6–S7 | Finish cycle |

**Read:** drive function code and address, assert `AS` and `UDS`/`LDS`, wait for `DTACK` (or
`BERR`), sample data, release. **Write:** same shape with the CPU driving data. Wait states stretch
either — so two identical instructions can take different wall-clock time depending on the target.

**Longword:** `MOVE.L (A0),D0` with aligned `A0` is two word reads assembled into 32 bits; a
longword store is two word writes; extension-word fetches consume bus bandwidth too, which matters
when the emulator is cycle-budgeting.

**Read-modify-write:** instructions that read, modify and write back the same memory operand keep
`AS` asserted across the pair, which is distinguishable from two unrelated accesses. `TAS`, bit
operations, and memory-destination arithmetic/logical forms qualify. Keep instruction semantics,
bus sequence, and atomicity/arbitration as three separate concerns in the code.

**CPU space:** interrupt acknowledge is a CPU-space cycle (`FC = 111`), not a vector read. This is
central on the Atari, where several devices share the IPL lines.

**Arbitration is not cycle termination.** `BR`/`BG`/`BGACK` answer "who may use the bus";
`DTACK` answers "this transfer is done". Collapsing them into a single `cpu_waiting` flag destroys
the distinction the Atari hardware depends on.

### 2.6 Function codes

`FC2–FC0` classify every access, and the Atari's decode logic uses them.

| FC2–FC0 | Meaning |
|---|---|
| `001` | User data |
| `010` | User program |
| `011` | User CPU space |
| `101` | Supervisor data |
| `110` | Supervisor program |
| `111` | Supervisor CPU space |

Program/data separation lets a memory system distinguish instruction fetches from data accesses —
relevant for cache-less 68000 only insofar as a device can observe fetch order. Supervisor/user
separation is what makes memory protection and TOS's supervisor calls work. CPU space carries
interrupt acknowledge and other special cycles.

### 2.7 Is the 68000 microcoded?

The public documentation does not include a complete internal microcode listing or a per-clock
micro-operation schedule. What is documented, and what is not:

| Concept | Status for the 68000 |
|---|---|
| Architectural instruction definition (operands, results, flags, exceptions) | Documented |
| External bus-cycle sequence and timing (including read/write counts for many instructions) | Documented to a hardware-engineering level |
| Exact internal micro-operation sequence, cycle by cycle | Not documented publicly |

The transistor-level implementation necessarily has control logic sequencing datapath and bus
interface; inferring a specific microcode format or microinstruction count from the programmer's
manual is not supported. An emulator's "read, compute, write" sequence is a *model* of behaviour,
not a claim about the chip's internals.

Ways to go deeper: instruction timing tables (baseline cycle and access counts), bus traces
(addresses, ordering, strobes, wait states), hardware measurement and reverse engineering
(prefetch, interrupts, odd cases), and existing CPU cores (tested models at differing precision).
None of them is an official microcode specification — see §10.2 for how the three reference
emulators differ here.

---

## 3. Interrupts

### 3.1 Levels, masking and acceptance

Three pins `IPL2–IPL0` encode a level. The levels are **active-low in binary**: all three high
means no request; `110` is level 1 … `000` is level 7.

| Level | `IPL2–IPL0` | Meaning |
|---|---|---|
| 0 | `111` | No interrupt |
| 1 | `110` | Lowest maskable |
| 2 | `101` | Higher priority |
| 3 | `100` | Higher priority |
| 4 | `011` | Higher priority |
| 5 | `010` | Higher priority |
| 6 | `001` | Highest maskable |
| 7 | `000` | Non-maskable, edge-sensitive special case |

The status register's mask `I2–I0` is compared with the requested level: a request is taken when
its level is **strictly greater** than the mask. Level 7 ignores the mask.

| Requested | Mask = 3 | Taken? |
|---|---|---|
| 1, 2, 3 | \(1,2,3 \le 3\) | No |
| 4, 5, 6 | \(> 3\) | Yes |
| 7 | — | Yes (special) |

**Autovectoring** means the CPU derives the vector from the level instead of receiving one from
the device: vector number = `24 + level`, so level 1 → vector 25 (`0x064`) … level 7 → vector 31
(`0x07C`).

### 3.2 The interrupt acknowledge cycle, pin by pin

1. The device asserts the level on `IPL2–IPL0`.
2. The CPU finishes the current instruction, then starts a **CPU-space cycle**.
3. During that cycle: `FC2–FC0 = 111`, `A3–A1` encode the acknowledged level, `A23–A4` are all ones,
   `AS` low, `R/W` high (read), `UDS` and `LDS` both low, and the requesting level stays asserted.
4. The device either drives a vector number on `D7–D0` and asserts `DTACK`, or asserts **`VPA`** to
   request autovectoring.
5. With a supplied vector, the CPU reads the handler address from `vector × 4`. With `VPA`, it uses
   the level-derived autovector.

The distinction between a device-supplied vector and autovectoring is exactly the `VPA` line —
which is why an emulator must model the acknowledge cycle as a bus transaction with a device
answering it, not as a lookup table keyed by level.

### 3.3 ISR entry and return

On acceptance the CPU: enters supervisor mode, switches to the supervisor stack, pushes PC and SR,
updates the mask to the accepted level, fetches the vector, and jumps. Handlers must end with
`RTE` (restores SR and PC), never `RTS`. A higher-priority interrupt can preempt a lower-priority
ISR once the mask has risen to the serviced level; that nesting is a normal, observable behaviour.

### 3.4 How the ST/STE wires it up

| 68000 level | Source | Vector type |
|---|---|---|
| 6 | MFP 68901 (all its 16 sources) | MFP supplies a vector |
| 4 | Vertical blank (VBL) | Autovector |
| 2 | Horizontal blank (HBL) | Autovector |
| 7 | Non-maskable condition | Special exception |

So the CPU arbitrates priority between *classes* of interrupt, while the MFP arbitrates priority
between its own sources. A level-2 request cannot preempt a level-6 handler; a pending level-2
request waits until the mask allows it.

Note the design: **only one MFP interrupt line reaches the CPU**. Everything the MFP handles —
keyboard, MIDI, timers, serial, floppy-completion, Centronics busy, mono detection — arrives as
level 6 and is separated by the MFP's own vector.

### 3.5 The MFP as interrupt controller

The MC68901 provides timers, a UART, GPIO and an interrupt controller with 16 sources, each with
enable/mask/pending/in-service state and a vector base.

| MFP priority | Atari use |
|---|---|
| 15 | Monochrome monitor detection |
| 14 | RS-232 ring indicator |
| 13 | System clock / Timer A (also the sound-DMA event counter input) |
| 12–9 | RS-232 receive/transmit events |
| 8 | Timer B — horizontal-line counter |
| 7 | Floppy controller / DMA completion |
| 6 | Keyboard and MIDI ACIA |
| 5 | Timer C |
| 4 | Timer D / baud-rate timing |
| 3 | Generally unused |
| 2–0 | RS-232 status, Centronics busy |

Key registers (byte-wide, in the `$FFFA01`–`$FFFA2F` block): `IERA` `$FFFA07`, `IERB` `$FFFA09`,
`IPRB` `$FFFA0D`, `ISRB` `$FFFA11`, `IMRB` `$FFFA15`, vector register `$FFFA17`, plus the timer
data/control pairs.

Interrupt flow for a keypress: keyboard → IKBD → keyboard ACIA → MFP source 6 → MFP asserts level 6
→ CPU accepts → IACK cycle → MFP supplies its vector → handler reads the ACIA and clears the
condition.

Implementation implication: **the MFP must be a device with real pending/in-service state**, driven
by Timer B counting display-enable transitions, not a shortcut that raises "VBL interrupt" from the
video code directly. Several demos depend on the MFP's own timing behaviour, and Timer B is how the
machine measures scanlines.
---

## 4. Memory, refresh and bus ownership

### 4.1 The MMU / RAM interface

The MMU (original ST) or the GSTMCU (STE) sits between the shared data bus and the DRAM devices. It
multiplexes addresses, drives the strobes, schedules accesses, and maintains DRAM contents.

| Signal group | Function |
|---|---|
| Multiplexed address outputs | Same pins carry row address, then column address |
| `RAS0`, `RAS1` | Row address strobe per bank |
| `CAS0L`, `CAS0H`, `CAS1L`, `CAS1H` | Column strobe per bank per byte lane |
| `WE` | Write enable |
| `D0–D15` | Shared data path (16 bits, byte lanes selectable) |

The 68000 presents a 16-bit data path; byte accesses select a lane via `UDS`/`LDS`, which the MMU
translates into the corresponding `CAS` strobe. A word access selects both lanes.

Three responsibilities must stay separate in the model:

1. **DRAM protocol** — row/column multiplexing, strobes, read/write timing.
2. **Refresh scheduling** — periodically re-reading rows to preserve contents. Refresh consumes
   bus time and must be accounted for, not silently ignored.
3. **Access arbitration/scheduling** — deciding which client (CPU, video fetch, sound DMA, disk
   DMA, Blitter) is served at a given moment.

Access timing is set by several inputs: the 68000's ≈8 MHz bus, the faster MMU timing derived from
the 32 MHz master, the Shifter's fixed demand rate for the current display mode, and the
MMU's own deferral of CPU completion when a video fetch has priority.

### 4.2 How video reads happen while the CPU "owns" the bus

This is the question that unlocks the whole architecture: the Shifter must fetch display data even
while the 68000 is mid-transaction.

The resolution is that there are **two different buses**. The 68000's address, data and control
lines form the *CPU bus*; the DRAM devices are on the *memory interface* driven by the MMU, with
multiplexed addresses and `RAS`/`CAS` strobes. When the CPU starts a cycle it asserts `AS` and
waits for `DTACK` — but the actual DRAM transaction is performed by the MMU, which is free to
schedule the DRAM accesses it needs during the CPU's wait.

Sequence for one line:

1. The CPU presents address, function codes and `R/W`, and asserts `AS`.
2. The MMU recognises a CPU access and takes control of the DRAM signals.
3. At the scheduled video slot, the MMU drives the video address counter onto the DRAM address
   pins and strobes the fetch.
4. The Shifter captures the fetched word on `LOAD`, buffering it for pixel output.
5. At the CPU's slot, the MMU drives the CPU's address and completes the read.
6. The MMU asserts `DTACK`; the CPU's cycle ends.

The CPU may be made to wait while video work is scheduled — that is arithmetic in the memory
controller, and the reason CPU-visible memory throughput in the ST is lower than 8 MHz would
suggest. It is also why "one memory access per CPU cycle" is a wrong simplification.

### 4.3 Arbitration: the four algorithms

There are four distinct paths through the bus, and they must be modelled separately.

**A. Ordinary CPU access.** CPU drives the bus; GSTMCU decodes the address and asserts the device
select; if the target is RAM, the memory controller performs DRAM addressing and timing, possibly
delaying completion for video; then `DTACK` ends the cycle.

**B. Video fetch.** The memory controller owns the video address counter, schedules the fetch
against DRAM timing, hands the word to the Shifter, and yields remaining slots to the CPU. No CPU
involvement, no `BR`/`BG` handshake.

**C. Disk DMA takes the bus.** The DMA controller asks for bus mastership through the arbitration
signals; the GSTMCU relays the request; the 68000 finishes its current cycle and grants; the DMA
controller and memory controller perform transfers; ownership returns. This is a genuine
bus-master handover, unlike B.

**D. Contention.** Several requesters may want the same resource at once:

| Requester | Wants | Handling |
|---|---|---|
| CPU | A memory transfer | Memory controller schedules and completes it |
| Shifter video | A timed video fetch | Scheduled by the memory controller |
| Shifter sound (STE) | A timed sample fetch | Scheduled by the memory controller |
| Disk DMA | Bus mastership + transfers | Arbitration + memory coordination |
| Blitter (STE) | Many reads/writes | Arbitration, cooperative or HOG mode |
| MFP timer | Interrupt service | Raises level 6 |
| Keyboard ACIA | Interrupt service | Via MFP |
| YM2149 | Register write | Ordinary memory-mapped I/O cycle |

The central rule: **bus mastership** (who may drive the system bus) and **memory service** (whose
DRAM access happens next) are different decisions made by different logic. Video and sound fetches
are memory-service clients; disk DMA and the Blitter are bus-mastering clients.

---

## 5. GLUE and GSTMCU

### 5.1 GLUE (original ST)

GLUE is the system coordinator: address decoding, chip selects, bus arbitration, interrupt-level
routing, and the video sync/blank signals. 68-pin PLCC package.

Signal groups worth knowing (original ST):

- CPU address inputs `A1–A23`, `AS`, `FC0–FC2`, `R/W`, `UDS`, `LDS`, `D0–D1`.
- Chip selects: `ROM0`–`ROM4`, `RAM`, `DMA`, `DEV`, `FCS`, `6850CS`, `MFPCS`, `SNDCS`.
- Arbitration and cycle control: `BGI` (bus-grant in), `BGO` (bus-grant out), `BR` (to CPU),
  `BGACK`, `DTACK`, `BERR`, `VPA`, `RDY`, `IACK`.
- Interrupts: `IPL1`, `IPL2` outputs to the CPU, `MFPINT` input from the MFP.
- Video: `HSYNC`, `VSYNC`, `DE` (display enable), `BLANK`.
- Clocks: 8 MHz master in; 2 MHz and 500 kHz outputs.

Its operating algorithm has four cases: decode a CPU access and select the device; coordinate a DMA
bus request (assert `BR`, relay the grant, release); drive video timing signals; and combine
interrupt sources into the IPL levels presented to the CPU.

### 5.2 GSTMCU (STE)

The STE merges GLUE and MMU into the **GSTMCU**, a large custom chip that also generates the clock
tree, drives DRAM, generates interrupts, and provides the STE-specific features (hardware
scrolling support, enhanced-controller ports, sound-DMA timing, stereo audio coordination).

What this changes in practice:

- Address decode and DRAM control are one component, so "GLUE said yes, MMU did the timing" is
  replaced by a single state machine.
- The GSTMCU still generates `DE`, `BLANK`, `HSYNC`, `VSYNC` and the clock outputs, and still
  relays `BR`/`BG`/`BGACK` for the disk DMA and the Blitter.
- The disk DMA controller remains a separate chip, and the WD1772 remains a separate standard FDC.
- Timer B still counts `DE` transitions, so the MFP's line counter keeps working exactly as on the
  ST — the signal is generated by the GSTMCU instead of GLUE.

The emulator consequence is architectural economy: one `GSTMCU` device owning decode, DRAM
scheduling, video timing, interrupt generation and arbitration is both simpler and more faithful
than modelling two chips for the STE.
---

## 6. Video

### 6.1 Chips and responsibilities

| Chip | Responsibility |
|---|---|
| GSTMCU | Video timing: line/frame counters, `DE`, `BLANK`, `HSYNC`, `VSYNC`, resolution state, video address control, memory-fetch scheduling |
| GST Shifter | Pixel generation: captures fetched words, combines bitplanes, applies palette, emits RGB/mono |
| Memory controller (in GSTMCU) | Actually performs the DRAM fetches when scheduled |

Three concerns are often confused and should be implemented separately: **where pixels live**
(video memory), **when data is fetched** (memory scheduling), and **how it is decoded and gated**
(Shifter pipeline plus sync/blank).

### 6.2 Modes and bitplanes

| Mode | Resolution | Bitplanes | Colours |
|---|---|---|---|
| Low | 320 × 200 | 4 | 16 from a 4096-colour palette |
| Medium | 640 × 200 | 2 | 4 |
| High (mono) | 640 × 400 | 1 | 2 |

Pixels are stored as interleaved **bitplanes**: consecutive 16-bit words on each plane cover 16
pixels. For 16 pixels in low resolution the Shifter fetches 4 words; in medium, 2; in high, 1. The
bit at the same position in each plane forms the index, which is looked up in the palette (except
in mono, where it drives a single-bit video path). Screen size follows: 32000 bytes low,
32000 bytes medium, 32000 bytes high for 200 lines — the interleaving means all three standard
modes use the same amount of memory per frame.

Consequence: **byte order within a plane word is not the pixel order you might expect** (high byte
first in the pixel stream), and any "sprite" logic that assumes chunky pixels will not match
hardware.

### 6.3 The timing state machines

Four machines cooperate.

**Horizontal machine** — a counter `h` per line, a mode (PAL colour / NTSC colour / mono), an
active-display flag, blank and HSYNC flags, and a line length:

| `h` (approx) | Action |
|---|---|
| 0 | Line start; evaluate mode-dependent preload conditions |
| 28 | Release blanking (50 Hz) |
| 40 | Preload trigger (50 Hz) |
| 56 | Normal line-length parameter |
| 376 | End horizontal active display |
| 448 | Assert blank |
| `LINE − 52` | Assert HSYNC, clear horizontal active flag |
| `LINE − 12` | Deassert HSYNC |

**Vertical machine** — line counter `v`, vertical active flag, VBL flag, VSYNC flag, driven by the
horizontal machine's line-boundary events. At VBL, software typically changes the base address or
palette and syncs game logic; the emulator should expose VBL as both an interrupt (level 4) and a
timing event on the shared timeline.

**Video fetch machine** — the memory controller's per-line fetch schedule, tied to the video address
counter and the line offset.

**Output gating machine** — decides whether Shifter output reaches the RGB/mono pins or is
suppressed by blanking; this is what produces the visible border.

PAL reference figures: 50 Hz refresh, 15.625 kHz line rate, 312.5 lines per frame, 200 visible
lines, 320 or 640 visible pixels.

### 6.4 The Shifter pipeline

| Stage | What happens |
|---|---|
| A — capture | `LOAD`/`*LOAD` latches a fetched word into the input buffer |
| B — accumulate | Planes are gathered: 4 words (low), 2 (medium), 1 (high) per 16 pixels |
| C — transfer | The word set moves into shift registers |
| D — shift and decode | Bits are shifted out, bitplane index assembled per pixel |
| E — output gating | Palette lookup and blank gating produce RGB/mono levels |

The relationship between `DE`, `LOAD` and visible pixels is not instantaneous: the GSTMCU raises
`DE` at a horizontal coordinate, the memory controller reacts later and asserts `LOAD`, and the
Shifter needs further time to load and shift before the first corresponding pixel appears.
Modelling `DE → LOAD → pixel` as three delayed events is the minimum for border and raster effects
to land in the right place.

The Shifter's own inputs are: RAM data bus (video words), CPU data bus (palette/resolution writes),
register select and chip select, `R/W`, `LOAD`/`DCYC`, `DE`, `BLANK`, resolution state, scroll
state, and clocks. Its outputs are RGB (analogue, 3 or 4 bits per channel depending on model),
monochrome video, and — on RF variants — a colour-clock-related output to the encoder.

### 6.5 Register map

| Address | Size | Register | Notes |
|---|---|---|---|
| `$FFFF8201` | byte | Video base address, high | Bits 23–16; applied at next VBL normally |
| `$FFFF8203` | byte | Video base address, middle | Bits 15–8 |
| `$FFFF820D` | byte | Video base address, low | STE only; bit 0 ignored (even addresses) |
| `$FFFF8205` | byte | Video counter, high | Read-only on ST, writable on STE |
| `$FFFF8207` | byte | Video counter, middle | |
| `$FFFF8209` | byte | Video counter, low | Bit 0 treated as zero |
| `$FFFF820A` | byte | Sync mode | bit1: 0 = 60 Hz, 1 = 50 Hz; bit0: 0 = internal, 1 = external sync |
| `$FFFF820F` | byte | Line offset (`LINEWID`) | In **words**, not bytes |
| `$FFFF8240–$FFFF825E` | word | Palette 0–15 | STE: 4 bits per channel |
| `$FFFF8260` | byte | Resolution, GSTMCU + Shifter | bits 1–0: 00 low, 01 medium, 10 high, 11 reserved |
| `$FFFF8261` | byte | Resolution, Shifter only | Deliberately separable from `$8260` |
| `$FFFF8264` | byte | Horizontal scroll, no prefetch | |
| `$FFFF8265` | byte | Horizontal scroll, with prefetch | The documented STE scroll register |

Two states must not be merged: **programmed base address** (where the next frame should start) and
**active video counter** (where the hardware is fetching right now). Software reads the counter to
synchronise with the beam, and writes it on the STE to reposition mid-frame.

Palette register numbering is `$8240 + 2n`; STE colour encoding is 4 bits per channel in the low
12 bits of the word. Palette writes take effect for subsequently emitted pixels; pixels already
emitted keep the colour they had. That single rule is what makes split-raster colour bands and
per-line palettes work, and it must not be implemented as "recolour the whole frame".

`$FFFF8260` is unusual because it programs two chips: the GSTMCU's timing configuration and the
Shifter's plane-combining mode. `$FFFF8261` changes only the Shifter's view, which is why software
can make the two disagree — a documented trick that produces specific visual artefacts.

### 6.6 Register timing and scrolling

| Register group | When a change matters | Emulation behaviour |
|---|---|---|
| Base `$8201/$8203/$820D` | Normally next VBL; writes during VBL apply immediately | Keep programmed base separate from active counter |
| Counter `$8205/$8207/$8209` | Subsequent fetches | Move the fetch pointer; never redraw emitted pixels |
| Sync mode `$820A` | Raster synchronisation timing | Apply at the hardware-effective point, not on the write |
| Line offset `$820F` | Line-to-line address progression | Use the value effective at the line-advance event |
| Palette `$8240–$825E` | Subsequent pixel output | Apply from the next emitted pixel |
| Resolution `$8260/$8261` | Pixel decoding and possibly timing | Keep GSTMCU and Shifter modes separate |
| Scroll `$8264/$8265` | Pixel alignment and fetch count | Model prefetch and non-prefetch variants separately |

Horizontal scrolling on the STE uses `$FFFF8264`/`$FFFF8265` (pixel-precise within a word) combined
with `$FFFF820F` (word-granular virtual line width). `$FFFF8265` triggers a prefetch sequence that
changes how many words are fetched per line, so it affects **memory bandwidth**, not just alignment.
`$FFFF8264` does the shift without the prefetch. There is a documented timing quirk around
transitions of the pixel-skip value; demos exploit it, so treat it as behaviour to reproduce, not a
bug to normalise.

Vertical scrolling is normally done by moving the base address (or, mid-frame, the counter) — there
is no dedicated vertical scroll register.

### 6.7 Raster tricks and implementation order

Effects software expects: palette changes mid-line or mid-frame, split screens via base/counter
changes, border opening via timing-sensitive register writes, raster bars, and fine scrolling
without moving bitmap data in RAM. All of them reduce to *register writes taking effect at a
defined point in the beam timeline*.

Implementation order that keeps the work testable:

1. All three modes rendered correctly, static frames.
2. 12-bit STE palette and palette writes.
3. Base-address programming with correct VBL application.
4. Writable video counter (STE) and correct readback.
5. Line offset and `$FFFF8265` prefetch scrolling.
6. `$FFFF8264` no-prefetch behaviour, separately.
7. Raster timing advanced alongside CPU execution; mid-scanline writes.
8. Border effects, sync scrolling, and pathological Shifter states.

Validation order follows the same ladder: base/counter → resolution/palette → line offset/scroll →
sync mode and VBL timing → `$8261` disagreement → raster accuracy. For signal-level reference, the
GSTMCU/GST Shifter Verilog model (`gyurco/gstmcu`) and the ST/STE scanline analyses are the most
directly useful sources.
---

## 7. Sound

The STE has no internal speaker. It has two independent sound generators feeding analogue output
circuitry shared with the monitor port and the RCA outputs.

### 7.1 The YM2149

The PSG provides three square-wave tone channels, a noise generator, per-channel amplitude, and a
hardware envelope. It is accessed through a two-address register interface:

- `$FFFF8800` — write selects a register; read returns the selected register's value.
- `$FFFF8802` — write data to, or read data from, the selected register.

| Function | Registers |
|---|---|
| Tone A period | R0 (low byte), R1 (high 4 bits) |
| Tone B period | R2, R3 |
| Tone C period | R4, R5 |
| Noise period | R6 |
| Mixer (tone/noise enable per channel) | R7 |
| Amplitude A/B/C | R8, R9, R10 |
| Envelope period | R11, R12 |
| Envelope shape | R13 |
| Port A / Port B data and direction | R14, R15 |

The PSG is clocked at 2 MHz and its tone period counts are clock divisions, so pitch depends on the
2 MHz clock rather than the CPU clock — they must not be conflated.

Register 14/15 matter beyond sound: **Port A drives the printer data lines and Port B carries drive
select / side select / motor / LED** (exact bit assignment is board-dependent). Register 14
amplitude behaviour and the DC offset of unipolar output also differ from a naive square wave, which
is audible in sampled recordings but rarely software-visible.

Audio path after the PSG: three channel outputs are mixed, filtered (fixed low-pass), and passed
through the LMC1992 audio processor (volume, balance, bass/treble, source mixing, controlled over
Microwire at `$FFFF8922`/`$FFFF8924`), then to the output connectors.

### 7.2 Stereo DMA sound

The STE adds 8-bit PCM playback directly from RAM, at four rates derived from the 8 MHz sound clock:

| Mode bits | Rate | Interval |
|---|---|---|
| `00` | 6 258 Hz | 159.8 µs |
| `01` | 12 517 Hz | 79.9 µs |
| `10` | 25 033 Hz | 39.9 µs |
| `11` | 50 066 Hz | 20.0 µs |

| Address | Function |
|---|---|
| `$FFFF8901` | DMA control: off / play once / repeat |
| `$FFFF8903`, `$FFFF8905`, `$FFFF8907` | Frame start address (high/middle/low) |
| `$FFFF8909`, `$FFFF890B`, `$FFFF890D` | Current address counter (read-only) |
| `$FFFF890F`, `$FFFF8911`, `$FFFF8913` | Frame end address (high/middle/low) |
| `$FFFF8921` | Mode: mono/stereo, sample rate |
| `$FFFF8922` | Microwire data |
| `$FFFF8924` | Microwire mask/clock |

Per sample tick: the rate divider fires, the sound logic requests the next byte(s) from RAM, the
memory system serves the fetch (competing with CPU and video), the byte is latched into the output
path, the address counter advances, the DAC converts, and the analogue filter smooths the step. In
stereo, left and right bytes are fetched in pairs and drive separate DACs.

The DMA-active signal is routed to **MFP Timer A**, so software can count playback progress from an
interrupt rather than polling the address counter. An emulator that omits this breaks music
players that use it.

What the analogue stage does — and what an emulator should approximate if it wants to sound right:
sample-and-hold stepping, a sample-rate-dependent low-pass, a fixed high-frequency low-pass, and the
LMC1992's volume/tone. Sample-and-hold plus a first-order low-pass near the sample rate is a
defensible minimum; treating the byte as a perfect waveform is audibly wrong at 6 kHz.

### 7.3 Three kinds of RAM access

The most useful clarification in the whole sound chapter: **STE sound DMA is not bus-mastering DMA.**

| Property | Video fetch | STE sound DMA | Floppy disk DMA |
|---|---|---|---|
| Purpose | Pixels | PCM sample bytes | Sector data |
| Hardware | Shifter + GSTMCU/MMU | GST Shifter + GSTMCU | Separate DMA controller + WD1772 |
| CPU copies bytes? | No | No | No |
| Address source | Video address counter | Sound DMA counter | DMA controller registers |
| Timing constraint | Pixel/line deadlines | Sample deadlines | Floppy data rate |
| Interaction with CPU | Shared memory scheduling | Shared memory scheduling | Bus arbitration |

Video and sound are **memory-service clients** scheduled by the memory controller; disk transfers
are **bus-mastership clients**. Modelling sound as a bus-mastering DMA engine produces wrong
contention behaviour, and modelling disk DMA as a scheduled fetch loses the arbitration handshake.

---

## 8. The Blitter

The STE Blitter is a bit-block transfer engine operating on RAM through the memory system. It
copies, combines, masks and shifts rectangular regions, and it is the device that makes ST
software's "fast graphics" possible.

What it can do: rectangle copy, sprite copy with destination preservation, boolean combination of
source and destination (AND/OR/XOR and complements), fills with zeros/ones/pattern, monochrome
pattern and mask application, bit-aligned shifts between source and destination, and sequences of
these for drawing and masking.

Position in the machine: the 68000 programs it; the Blitter computes addresses and requests memory
transfers; the GSTMCU arbitrates and schedules those transfers; DRAM holds the data; the Shifter
keeps fetching display data independently.

Register block at `$FFFF8A00`:

| Address | Register |
|---|---|
| `$FFFF8A00–$FFFF8A1E` | Halftone RAM: sixteen 16-bit pattern words |
| `$FFFF8A20` | Source X increment |
| `$FFFF8A22` | Source Y increment |
| `$FFFF8A24–$FFFF8A26` | Source address (24-bit, in a 32-bit register) |
| `$FFFF8A28` | Endmask 1 — first destination word of a line |
| `$FFFF8A2A` | Endmask 2 — intermediate words |
| `$FFFF8A2C` | Endmask 3 — last word of a line |
| `$FFFF8A2E` | Destination X increment |
| `$FFFF8A30` | Destination Y increment |
| `$FFFF8A32–$FFFF8A34` | Destination address |
| `$FFFF8A36` | X count — words per line |
| `$FFFF8A38` | Y count — lines |
| `$FFFF8A3A` | HOP — halftone/source operation |
| `$FFFF8A3B` | OP — boolean operation |
| `$FFFF8A3C` | Control: start/busy, HOG, smudge, halftone line number |
| `$FFFF8A3D` | Skew: bits 0–3 amount, `NFSR`, `FXSR` |

Execution is a two-stage datapath. `HOP` produces the intermediate word `I` from source `S` and
halftone `H`:

| HOP | `I` |
|---|---|
| 0 | all ones |
| 1 | `H` |
| 2 | `S` |
| 3 | `H ∧ S` |

`OP` then combines `I` with the old destination word `D`:

| OP | Result | Typical use |
|---|---|---|
| 0 | 0 | clear |
| 1 | `I ∧ D` | AND with existing |
| 2 | `I ∧ ¬D` | masked combination |
| 3 | `I` | replace |
| 4 | `¬I ∧ D` | clear where intermediate set |
| 5 | `D` | no change |
| 6 | `I ⊕ D` | XOR |
| 7 | `I ∨ D` | merge |
| 8 | `¬I ∧ ¬D` | complemented combination |
| 9 | `¬I ⊕ D` | complemented XOR |
| A | `¬D` | invert destination |
| B | `I ∨ ¬D` | OR with complemented destination |
| C | `¬I` | write complement |
| D | `¬I ∨ D` | complemented merge |
| E | `¬I ∨ ¬D` | complemented union |
| F | all ones | set |

Endmasks protect neighbouring pixels: the first word of a line uses Endmask 1, interior words
Endmask 2, the last word Endmask 3. They are **hardware masks applied to the write**, not a
post-processing step — which is why a sprite blit can overlap other graphics without erasing them.

Skew shifts the source stream so source and destination need not be word-aligned. `NFSR` (no final
source read) and `FXSR` (force extra source read) control boundary behaviour and affect both the
result and the number of bus accesses.

Control register bits: 7 = start/resume (reads as busy), 6 = HOG (bus-hogging versus cooperative),
5 = smudge (halftone row selected from the low bits of the source word instead of the register),
3–0 = initial halftone row. In cooperative mode the Blitter yields so the CPU and video keep
running; in HOG mode it keeps requesting the bus, slowing everything else. Both must be explicit
state, because software uses the difference to trade speed against interrupt latency.

Timing: a plain copy and a destination-dependent boolean of the same dimensions take different
times, so **rectangle size alone does not determine duration**. Each word costs real memory
accesses, and the Blitter's progress, pending access, and bus ownership are three separate pieces
of state.

What not to do: render sprites directly into the video buffer as a special case; assume every
operation reads a source word then writes a destination word; assume forward-only copying; treat
endmasks as visual clipping; let the Blitter bypass memory mapping and bus scheduling; assume the
CPU is halted for the whole operation; or assume all board revisions have identical Blitter
implementations (later boards integrate it into the large custom chip).

Minimum viable Blitter: correct registers and readback, correct bitwise results and address
progression, correct start/busy semantics, both bus modes, and memory access requests routed
through the shared scheduler rather than direct array writes.

---

## 9. Floppy, ACSI and the disk DMA

Four different chips are involved and the naming confusion is worth clearing up:

| Chip | Type | Role |
|---|---|---|
| GLUE / GSTMCU | Atari custom | Address decode, timing, bus coordination and arbitration |
| MMU / GSTMCU | Atari custom | DRAM control and addressing, including the DMA address counter |
| DMA controller | Dedicated ST chip | Coordinates disk and hard-disk DMA transfers |
| WD1772 | Standard FDC | Floppy protocol, sector read/write, track positioning |

So: the MMU is not "the DMA controller", GLUE is not "the DMA controller", and the DMA controller
is a separate Atari-designed chip — commonly `C025913-38`, with a later revision `C398739-001A`
that matters for storage compatibility.

Register interface:

| Address | Purpose |
|---|---|
| `$FFFF8604` | FDC data/register access or DMA sector-count access, depending on mode |
| `$FFFF8606` | DMA mode control (write) / status (read) |
| `$FFFF8609` | DMA address, high byte |
| `$FFFF860B` | DMA address, middle byte |
| `$FFFF860D` | DMA address, low byte |

Floppy read sequence: CPU programs DMA address/count/mode → CPU commands the WD1772 → the FDC
signals a byte is ready → the DMA controller and memory controller move it to RAM → GSTMCU
arbitrates bus ownership where required → address advances → completion raises the MFP's source 7
interrupt.

The DMA chip's job is to bridge two rates: the peripheral's byte rate and the system bus's timing.
An internal FIFO absorbs the difference; the controller asserts bus requests in bursts rather than
per byte. `ACSI` (the DB-19 hard-disk bus) uses the same DMA controller over the system bus, which
is why ACSI transfers are much faster than floppy transfers but still bounded by arbitration.

Implementation requirements: model the FIFO/rate bridge (not "instant sector copy"), generate the
MFP completion interrupt, respect register readback semantics, and keep the DMA chip's behaviour
modular so a `C398739-001A` variant can be selected later.
---

## 10. Emulator architecture

### 10.1 Three levels of emulation

| Level | Characteristic | Cost | Sufficient for |
|---|---|---|---|
| Functional | Right results, approximate timing | Low | Applications, GEM software |
| Cycle/bus-aware | Timestamped accesses, scheduled devices | Medium–high | Demos, raster effects, Blitter/CPU races |
| Electrical | Signal transitions at sub-cycle granularity | Very high | FPGA cores, verification |

Most ST accuracy problems are Level-2 problems: the machine's interesting behaviour comes from
*ordering* between devices, not from sub-cycle analogue effects.

### 10.2 What existing emulators do

| Aspect | Hatari | NeoST | Steem SSE |
|---|---|---|---|
| Language / build | C, CMake, SDL | C++17, CMake, GLFW/OpenGL, miniaudio | C++, legacy build scripts, platform renderers |
| CPU core | WinUAE-derived, cycle-accurate option | Vendored Moira, cycle-exact mode | Own long-developed 68000 model |
| Time model | Global cycle counter + scheduled events (`cycInt`) | Continuous cycle timeline with dated scheduler | CPU-cycle timing with device-specific machinery |
| Video | Detailed beam/scanline model | Explicit scanline/event model, compared against Hatari | Long history of demo/border fixes |
| Blitter | Integrated in event/timing system | Explicit device, cooperative/HOG documented, some timing open | Supported, iterative timing fixes |
| MFP / IRQ | Mature event-driven | Dated timer and IRQ model, divergences documented | Detailed refinements including interrupt sampling |
| Testing | Compatibility suites, regression | Headless execution, trace-diff tooling, explicit divergence log | Tuning against specific software |

Their roles for this project: **Hatari as the mature reference and regression oracle**, **NeoST as
the architecture to learn from** (explicit devices, documented divergences, headless tests), and
**Steem SSE as the independent source of long-refined corner cases** — especially demos.

The important distinction they all illustrate: CPU accuracy is not system accuracy. A cycle-exact
CPU still needs correct answers to questions like: does the Shifter have priority over a
simultaneous Blitter request; does a video-register write apply immediately or at the next fetch
boundary; does an MFP timer edge land before or after the video event; does a sound-DMA fetch delay
a CPU access; can a Blitter word be preempted halfway.

### 10.3 A timing architecture

One shared timeline, devices as explicit state machines, and synchronisation forced only at
interaction points. Seven rules that produce that:

1. **Every device has explicit state.** A device's progress is never inferred from how often its
   update function happened to be called.
2. **All timed events share one timeline.** Device clocks convert into it with integer or rational
   arithmetic — never floating point accumulation.
3. **Devices advance lazily.** A PSG, timer or DMA engine can compute its state at a later time
   without stepping every intermediate clock, provided nothing observable happened in between.
4. **Interactions force synchronisation.** A register read of a live timer, a sampled signal, or a
   competing bus request must advance the other devices to that instant first.
5. **RAM service is separate from device logic.** The Blitter or sound engine decides what access it
   wants; the bus/memory scheduler decides when it happens.
6. **Same-time ordering is explicit.** Use hardware-derived phase or priority rules, not the
   incidental order of function calls.
7. **Host time is separate.** Rendering, audio delivery and MIDI follow real time but never drive
   the internal hardware timeline.

Clock domains to keep apart: master (32 MHz), CPU/bus (8 MHz), video/Shifter (16 MHz domain),
MFP timer, sound DMA (its own 8 MHz oscillator), and peripheral-local clocks such as the IKBD's.

A worked contention case shows why rule 6 exists:

- the Shifter is due to fetch video data;
- the Blitter requests a RAM read;
- the CPU attempts a memory write;
- MFP Timer B's input changes because `DE` transitions.

All four can carry the same nominal timestamp. Floating-point timestamps cannot express the
hardware's phase relationship; integer ticks plus an explicit priority for same-tick events can.
And a shared clock does not mean a shared phase — devices deriving from the same crystal can still
be offset by a fixed number of clocks.

On event queues specifically: an event queue models *scheduled work*, not independent hardware
execution. Two devices do not "run in parallel" in the emulator; they take turns, and the queue's
ordering rules decide the outcome.

### 10.4 Implementation ladder

1. Cycle-exact CPU bus accesses on a shared timeline; memory map with chip selects.
2. Scheduled HBL/VBL, MFP timers, FDC events, and the interrupt tree (level 6 / 4 / 2).
3. Explicit memory requests and an arbiter: video fetch, sound fetch, disk DMA, Blitter.
4. Video timing with `DE`/`LOAD`/blank gating and timed register writes.
5. Blitter with both bus modes, plus sound DMA with its sample deadlines.
6. Sub-cycle or phase-aware ordering **only** where a test demonstrates that cycle granularity is
   insufficient.

Validation targets, in order of payoff: TOS 1.62 boot and GEM applications; established
compatibility test suites; demos that change palette mid-frame or open the border; sound players
using MFP Timer A; disk images including copy-protected ones.

---

## Appendix A. Address and register quick reference

| Area | Address range |
|---|---|
| ST RAM | `$000000`–`$3FFFFF` (up to 4 MB) |
| TOS ROM | `$E00000`–`$EFFFFF` (2 chips) |
| Cartridge | `$FA0000`–`$FBFFFF` |
| DMA / FDC | `$FF8600`–`$FF860F` |
| YM2149 | `$FF8800`, `$FF8802` |
| MFP 68901 | `$FFFA01`–`$FFFA2F` |
| ACIAs (keyboard, MIDI) | `$FFFC00`–`$FFFC05` |
| Video registers | `$FF8201`–`$FF8265` |
| Blitter | `$FF8A00`–`$FF8A3D` |
| Sound DMA | `$FF8901`–`$FF8924` |
| Microwire (LMC1992) | `$FF8922`, `$FF8924` |
| Interrupt vectors | `$000060`–`$0000FF` (autovectors 25–31 at `$064`–`$07C`) |

## Appendix B. Document provenance

This overview was reorganised from a multi-day technical study session (27–29 October 2026),
preserved verbatim in [`sources/ste-study-transcript.md`](./sources/ste-study-transcript.md). The
material was regrouped by subsystem, condensed where the original repeated itself, and extended
with implementation guidance; the technical claims, register maps and timing figures come from that
discussion and from the hardware references it cites.

Two honesty notes:

- Where the public documentation does not support a mechanism — notably the 68000's internal
  micro-operation sequencing (§2.7) — this document says so rather than describing one.
- The board-revision and DMA-chip variations in §1.2 are documented as *differences of unknown
  emulator significance*. Do not reproduce them speculatively; reproduce them when a test demands
  it.
