# Virtual chips and their pins

The machine is decomposed into **seven virtual chips**. Each one is a unit that can make progress on
its own — one timeline — and each keeps the pin names of the physical parts it is regrouped from, in
the form `Uxxx.PINNAME`. Sources: `design/board-inventory.md` (pin tables §3, connections §4,
subsystem grouping §5) and the overview `atari-st-emulation/02-atari-ste-hardware-overview-for-emulation.md`.

## The boundary rule

**A virtual chip is one timeline; the engine goes with the timeline it drives.** A physical IC that
serves two independent timelines is split, and each half goes where its timeline goes. An analog tail
is not a chip at all: the emulation ends at the digital boundary.

Applied three times:

| Physical IC | Split into | Because |
|---|---|---|
| U400 GSTMCU | Memory + Video + Timers/Controllers glue | memory control and arbitration, the video timing generator, and the decode/pad-ADC logic progress independently |
| U300 DMA controller | Memory/Sound fetch engine + Disk register and FDC interface | it is a bus master for two streams and a register slave for the FDC |
| U202 YM2149 | Sound (PSG) + Controllers (I/O ports A/B) | the PSG has its own 2 MHz clock; the ports are a keyboard/joystick scanner |

Not chips: `U403` MC1377 encoder, `U405` RGB/mono mux, `U502/U503` DACs, `U504–U507` filters and
op-amps, `U302/U307/U509/U510/U512/U632` buffers and transceivers, `U304/U306` pipeline flip-flops,
`U511/U500/U501` latches and shift registers. They are either analog output stages or glue with no
state of their own; their behaviour is absorbed by the chip on whose pins they sit.

## The seven chips

### 1. CPU — U100

The MC68000 at 8 MHz. Its pins are the ones it drives: `A1–A23`, `D0–D15`, `AS`, `LDS`, `UDS`,
`R/W`, `FC0–FC2`, `VMA`/`VPA`, `BR`/`BG`/`BGACK`, `IPL0–IPL2`, `DTACK`, `BERR`, `CLK`, `RESET`.
The CPU never reaches into another chip: it asks, and either gets an answer (`DTACK`) or an exception
(`BERR`, `IPL`).

### 2. Memory — U400 memory half + DRAM + SIMMs + U101 Blitter + U300 fetch engine

Owns RAM and everything that decides who gets it:

- `U400.MAD0–MAD9`, `RAS0/RAS1`, `CAS0H/CAS0L/CAS1H/CAS1L`, `WE` — the multiplexed DRAM interface;
- the refresh counter (`U400.MAL0–MAL9`), whose period the redraw leaves unresolved;
- `U400.AS/LDS/UDS/R/W/FC0–2 → DTACK/BERR` — the bus cycle it completes;
- `U400.BR/BG/BGACK` — bus arbitration, granted to the Blitter, the FDC DMA and the sound DMA;
- the **video and sound fetch streams**, scheduled internally from counters (owner's ruling — they
  are memory service, not crossings);
- `U101` Blitter in full: `A1–A23`, `D0–D15`, `AS/LDS/UDS`, `R/W`, `DTACK`, `BERR`, `FC0–2`,
  `BR/BG/BGACK`, `VMA/VPA`;
- `U300`'s bus-master half: `FCS`, `RDY`, `DCYC`, `HDCS/HDRQ/ACK`.

### 3. Video — U400 video-timing half + U401 Shifter

The one chip that generates time for the machine, and the one that renders it:

- timing generator: `HSYNC`, `VSYNC`, `DE`, `BLANK`, `LATCH`, `CLK8/CLK4/CLK2`, and the video counter,
  `LINEWID` and scroll registers that decide when each fetch happens;
- `SREQ` (`U401.36 → U400.107`) and `SLOAD` (`U400.106 → U401.37`) — **internal to this chip**, which
  is the direct consequence of absorbing the timing half: the fetch handshake never leaves Video;
- `U401`: `MD0–MD15` (fetched words arrive from Memory), `PD0–PD15` (palette/pixel path),
  `MWE`/`MWD`/`MWK` (Super Hi-Res write path), `R0–R3`/`G0–G3`/`B0–B3` (12-bit RGB out),
  `MONO`/`COLOR`, `SCLK`, `FCLK`, `CLK`, `A1–A6`/`CS`/`LD`/`HD`/`R/W` (register interface).

Analog tail excluded: the RGB outputs stop at the chip boundary; `U403`/`U405` are the host's
video encoder, not the machine's.

### 4. Sound — U202 PSG + U300 sound-DMA registers

- `U202`: `SD0–SD7` (sample bus), register interface, `ACC` (paddle A/D input), and its own 2 MHz
  clock derived from `SCLK/4`;
- `U300`'s sound side: the DMA address/counter/control registers, `$FF8901–$FF890D`;
- the fetched samples themselves are Memory's business; this chip consumes them.

### 5. Disk — U300 register/FDC half + U301 WD1772 + drive buffers

- `U301`: `CS`, `DRQ`, `R/W`, `A0/A1`, and the drive control/status pins `STEP`, `DIRC`, `WG`, `WD`,
  `MO`, `RD`, `DDEN`, `TR00`, `WPRT`, `IP`, `MR`;
- `U300`: `FDCS`, `FDRQ`, `CD0–CD7`, `CR/W`, `CA1`, `CA2`, plus `HDCS/HDRQ/ACK` register side;
- off-board: `J301` ACSI, `J303`/`J306` floppy.

### 6. Timers and interrupts — U203 MFP 68901

Timers A–D, the interrupt controller, and the USART that carries the IKBD link: `MFPCS`, `IACK`,
`DINT`, `FDINT`, `HDINT`, `BINT`, `SINT`, `IPL0–IPL2` (drive side), `KHZ500` (timer reference),
`EINT3/EINT5/EINT7` (out of scope, but occupying pins).

### 7. Controllers — U400 pad/pen/port logic + YM2149 ports + U900

- `U400`: `JOYAH/JOYAL/JOYWE/JOYWL`, `BUTTON`, `PEN`, `PAD0X/PAD0Y/PAD1X/PAD1Y`, `PADRST`;
- `U202`: `IOA0–IOA7` (keyboard matrix + joystick read), `IOB0–IOB3` (controller select, FDD side);
- `U900` 74LS164 — keyboard/MIDI serialisation.

## Consequences worth stating

- **Seven chips, three physical ICs split.** The pin count of each virtual chip is the union of its
  members' external pins; the nets that run *between* members (`SREQ`/`SLOAD`, `FDCS`/`FDRQ`, `IOA`/`IOB`)
  are internal and disappear.
- **The Shifter↔timing handshake is intra-chip**, so the model has no per-fetch event between Video
  and Memory at all: the fetch is Memory's internal schedule and the fetched word arrives as data.
- **Memory is where the machine's hard timing lives** — refresh, arbitration, and two fetch streams —
  and it holds the Blitter, which is the machine's busiest bus client.
- **`U400`'s CPU-bus pins are unresolved by number** in the inventory (bottom edge, names only). That
  is acceptable here because this document uses names; it is not acceptable for a pin-level netlist.
