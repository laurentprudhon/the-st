# Virtual chips and their pins

The machine is decomposed into **eight virtual chips**. Each one is a unit that can make progress on
its own — one timeline — and each keeps the pin names of the physical parts it is regrouped from, in
the form `Uxxx.PINNAME`. Sources: `design/board-inventory.md` (pin tables §3, connections §4,
subsystem grouping §5), `design/subsystems-spec.md` (the five-column partition), and
`research/bus-arbitration.md` (how each access path reaches RAM).

> ### Correction — 2026-11-11 (ticket #31, owner's ruling)
>
> The virtual chip this file calls *Memory* is the **System Control Unit**, and the DRAM and the TOS
> ROMs are no longer members with pins: they are plain data structures (`Ram`, `Rom`) inside it —
> stores with no timeline, no record, no subscription and no wake. Every statement below about the
> counter, the owner equation, the clients, the beam, the buffer or the leaf stands unchanged; only
> the name and the store boundary moved. Owner's words: *"GSTMCU is too cryptic when you want to
> understand the machine, what about keeping only three letters of the name : SystemControlUnit ?"*
> See [ADR 0014](../docs/adr/0014-the-stores-are-data-structures.md) and `design/system-control-unit.md`.

## The boundary rules

1. **A virtual chip is one timeline; the engine goes with the timeline it drives.** A physical IC that
   serves two independent timelines is split, and each half goes where its timeline goes. An analog
   tail is not a chip at all: the emulation ends at the digital boundary.
2. **Two kinds of DMA, split by how the device reaches the bus.** A continuous stream addressed by the
   memory controller itself — video fetch, sound fetch — belongs to the **System Control Unit**, because it *is* the
   controller's own schedule and never touches the CPU bus. A temporary burst that must take the bus —
   Disk DMA, the Blitter — belongs to **the chip that produces it**, because its request and its grant
   are moments the rest of the model can see.
3. **Arbitration is CPU-side.** `BR` → the 68000 finishes its cycle → `BG` → `BGACK` → the burst holds
   the bus → release. The GSTMCU's part in disk DMA is relaying (`*RDY` → `*BR`), not deciding; the
   Blitter drives `BR`/`BG`/`BGACK` itself.

Not chips: `U403` MC1377 encoder, `U405` RGB/mono mux, `U502/U503` DACs, `U504–U507` filters and
op-amps, `U302/U307/U509/U510/U512/U632` buffers and transceivers, `U304/U306` pipeline flip-flops,
`U511/U500/U501` latches and shift registers. They are either analog output stages or glue with no
state of their own; their behaviour is absorbed by the chip on whose pins they sit.

## Applied twice: the splits

| Physical IC | Split into | Because |
|---|---|---|
| U400 GSTMCU | **System Control Unit** (memory control, counters, decode, sync generation) + **Controllers** (pad/pen ADC, port logic) | the memory controller's timetable and the pad/pen logic progress independently. The video timing generator does **not** become a third piece: the beam counter and the fetch cadence are one counter, so they stay with the memory controller (`design/subsystems-spec.md` §1, §4 finding 3) |
| U202 YM2149 | **Sound** (PSG) + **Controllers** (I/O ports A/B) | the PSG has its own 2 MHz clock; the ports are a keyboard/joystick scanner |

`U300` (DMA controller) is **not** split: it is a bus master for disk DMA and the register slave for
the FDC, and both serve the platter's timeline. `U400`'s memory half and `U401` (Shifter) are separate
chips: the counters that decide *when* a word is fetched are the System Control Unit's, the registers that turn it into
pixels are Video's.

## The eight chips

### 1. CPU — U100 MC68000

The MC68000 at 8 MHz, PLCC68 (`C398061-001`). Its pins are the ones it drives: `A1–A23`, `D0–D15`,
`AS`, `LDS`, `UDS`, `R/W`, `FC0–FC2`, `VMA`/`VPA`, `BR`/`BG`/`BGACK`, `IPL0–IPL2`, `DTACK`, `BERR`,
`CLK`, `RESET`. It also owns the **grant half** of arbitration: it is the chip that gives the bus away
and takes it back, 4 cycles each way on this machine.

The CPU never reaches into another chip: it asks, and either gets an answer (`DTACK`), an exception
(`BERR`, `IPL`), or is told to stop driving the bus (`BG`).

### 2. The System Control Unit — U400 memory half `C300589-001`, and the two stores it holds

Owns RAM, **everything needed to supply a stream of bytes to another chip**, and the timetable that
decides when each client is served. Its side of the video and audio divide is the *supply* side — the
Agnus role.

- `U400.MAD0–MAD9` (110–119), `RAS0` 127, `RAS1` 124, `CAS0H` 126, `CAS0L` 125, `CAS1H` 123,
  `CAS1L` 122, `WE` 121 — the multiplexed DRAM interface;
- the refresh counter (`U400.MAL0–MAL9`), whose period no source states;
- `U400.A1–A23` (41–65), `D0–D15` (75–91), `AS` 67, `LDS` 68, `UDS` 69, `R/W` 70, `FC0–FC2` — the
  CPU-bus side it decodes, answered with `DTACK` 71 or `BERR` 23;
- `U400.BR` 29, `BGI` 28, `BGO` 27, `BGACK` 26 — the arbitration relays;
- **the video beam counter, `LINEWIDTH` and the scroll registers, and the fetch cadence derived from
  them** — one counter decides both where the beam is and when the next group is fetched, so it cannot
  be split across two chips;
- **the sound-DMA counters and the sample-rate divider** (`$FF8900`–`$FF8921`), which decide when the
  next sample word is due;
- `U400.HSYNC` 143, `VSYNC` 142, `DE` 141, `BLANK` 140, `LATCH` 131 — generated here, where the beam is;
- `U400.DCYC` 138 and `SLOAD` 106 out, `SREQ` 107 in — the two handshakes it answers;
- `U400.FCS` 136, `MFPCS`, `SNDCS` 19, `SNDIR` 18, `ROM0–ROM6` (13–7) — the chip selects its decode
  asserts;
- `U400.IPL0–IPL2` (33/34/35) — the level-4 (VBL) and level-2 (HBL) requests it raises from its own
  line and frame counters;
- `U400.KHZ500` 6, `CLK8/CLK4/CLK2` (3/4/5) — the clock outputs it distributes.

### 3. Video — U401 GST Shifter

The *interpretation* side — the Denise role. It receives a stream of bitplane words and turns them
into pixels and colours; it does not decide when a fetch happens.

- `U401`: `MD0–MD15` (fetched words arrive from the System Control Unit), `PD0–PD15` (palette/pixel path),
  `MWE`/`MWD`/`MWK` (the Microwire link to the LMC1992 — the pins are this chip's, the logic is Sound's), `R0–R3`/`G0–G3`/`B0–B3` (12-bit RGB out),
  `MONO`/`COLOR`, `SCLK`, `FCLK`, `CLK`, `A1–A6`/`CS`/`LD`/`HD`/`R/W` (register interface);
- `DCYC` arrives with the fetched word and `LOAD` latches it — **this is the video handshake**;
- `SREQ` leaves the chip and `SLOAD` arrives — **this is the sound-DMA handshake**, and although both
  pins are drawn on this chip, the requester side belongs to **Sound** (the 4-word FIFO and the byte
  split, owner's ruling 2026-11-05). The Shifter is where the wire physically sits:
  `sound.gstshifter.shifter.SREQ`;
- `DE` and `BLANK` arrive as inputs that gate display.

Analog tail excluded: the RGB outputs stop at the chip boundary; `U403`/`U405` are the host's video
encoder, not the machine's.

### 4. Sound — U202 YM2149 `C025983` + U508 LMC1992

- `U202`: `SD0–SD7` (the sample bus to the DACs, 8 lanes), register interface `$FF8800`/`$FF8802`,
  `ACC` (paddle A/D input), and its own 2 MHz clock derived from `SCLK/4`;
- `U508`: volume, bass, treble and mixing, driven over Microwire **from the Shifter**;
- **the sound-DMA requester side: the 4-word sample FIFO, `SREQ`, and the split of each fetched word
  into two bytes on `SD0–SD7`.** The pins are physically on the Shifter, the demand is this chip's —
  the sample clock is Sound's timeline, which is why Sound initiates the refill exchange and the System Control Unit
  answers it (`design/crossings.md` §2);
- the fetched words themselves are the System Control Unit's business; this chip consumes them.

### 5. Disk — U300 DMA controller `C398739-001` + U301 WD1772 + drive buffers

- `U301`: `CS`, `DRQ`, `R/W`, `A0/A1`, and the drive control/status pins `STEP`, `DIRC`, `WG`, `WD`,
  `MO`, `RD`, `DDEN`, `TR00`, `WPRT`, `IP`, `MR`;
- `U300`: `FDCS`, `FDRQ`, `CD0–CD7`, `CR/W`, `CA1`, `CA2`, plus the bus-master side `FCS`, `RDY`,
  `DCYC`, `HDCS/HDRQ/ACK`;
- off-board: `J301` ACSI, `J303`/`J306` floppy.

Disk DMA is the *mediated* burst: `U300.RDY` (38) → `U400` 135, the GSTMCU relays `*BR`, the CPU
grants, and `DCYC` (U400.138) acknowledges each DMA cycle.

### 6. Timers and interrupts — U203 MFP 68901

Timers A–D, the interrupt priority encoder, and the USART that carries the IKBD link: `MFPCS`, `IACK`,
`DINT`, `FDINT`, `HDINT`, `BINT`, `SINT`, `IPL0–IPL2` (drive side), `KHZ500` (timer reference),
`EINT3/EINT5/EINT7` (out of scope, but occupying pins).

Timer B counts `DE` transitions — that is how the machine measures scanlines, and it is a crossing in
its own right (`DE` from the System Control Unit to Timers), not a shortcut inside the video code.

### 7. Controllers — U400 pad/pen/port logic + YM2149 ports + U900 + keyboard ACIA

- `U400`: `JOYAH/JOYAL/JOYWE/JOYWL` (99–102), `BUTTON` 103, `PEN` 137, `PAD0X/PAD0Y` 94/95,
  `PAD1X/PAD1Y` 96/97, `PADRST` 98;
- `U202`: `IOA0–IOA7` (keyboard matrix + joystick read), `IOB0–IOB3` (controller select, FDD side);
- `U900` 74LS164 — keyboard/MIDI serialisation; the keyboard ACIA is `U201` at `$FFFC00`.

### 8. Blitter — U101 ST Blitter

The machine's heaviest bus client and a bus master in its own right: it drives `BR`/`BG`/`BGACK`
**without going through the GSTMCU** (`U101.13`/`.11` are on the same net as `U400.29–.26`), taking 4
cycles to acquire the bus, 4 per word, 4 to release. Pins: `A1–A23`, `D0–D15`, `AS/LDS/UDS`, `R/W`,
`DTACK`, `BERR`, `FC0–FC2`, `BR`/`BG`/`BGACK`, `VMA/VPA`, `CLK`. Registers: the whole `$FF8A00–$FF8A3E`
block.

## Consequences worth stating

- **Eight chips; two physical ICs split.** U400 divides into the System Control Unit and Controllers; U202 into Sound
  and Controllers. The pin count of each virtual chip is the union of its members' external pins; the
  nets that run *between* members (`FDCS`/`FDRQ`, `IOA`/`IOB`) are internal and disappear.
- **The two handshakes cross, and they are different handshakes.** `DCYC`/`LOAD` carry the video word
  (16 000 words per frame, 4 000 groups of 4); `SREQ`/`*SLOAD` carry the sound word (125–1 000 samples
  per frame). An earlier revision of this document had them the other way round and called the
  Shifter↔timing pair intra-chip; both were wrong (`research/bus-arbitration.md`,
  `research/schematic-evidence.md` §1).
- **The System Control Unit is where the machine's hard timing lives** — refresh, the two fetch streams, the beam
  counter, and the decode that answers every CPU cycle.
- **The Blitter is a chip, not a unit inside the System Control Unit.** It takes the bus by itself; folding it into
  the System Control Unit would hide a bus master inside the memory controller, which is what the two-DMA rule exists
  to prevent.
- **Disk DMA is the only mediated burst**; that asymmetry with the Blitter is a hardware property, not
  a modelling choice.
- **U400's CPU-bus pins now carry numbers** (`design/board-inventory.md` §3.4b, tag `D`), read from the
  600 dpi scan. Only `MAL0–MAL9`, `CLK` and the ground pins remain unresolved.

## Revision — 2026-11-02

This document said *seven* chips and put the Blitter inside the System Control Unit, on the 2026-10-30 resolution of
[ticket #5](https://github.com/laurentprudhon/the-st/issues/5). Three later rulings supersede that:

1. **Two kinds of DMA** (`design/subsystems-spec.md`, from `research/bus-arbitration.md`): a burst that
   asks for the bus belongs to the chip that produces it — so the Blitter is a chip again and the System Control Unit no
   longer holds it.
2. **Arbitration is CPU-side**; the GSTMCU only relays `*RDY` → `*BR` for disk DMA.
3. **`SREQ`/`*SLOAD` are the sound path and `DCYC`/`*LOAD` the video path** — the opposite of what this
   file and `design/crossings.md` said, confirmed twice (`research/bus-arbitration.md`,
   `research/schematic-evidence.md` §1).

The seven-chip list also had U400 splitting three ways; it splits two. Nothing else moved: every pin
above is the inventory's, with the number it carries there.
