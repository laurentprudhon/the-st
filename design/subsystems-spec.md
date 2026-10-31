# Subsystems specification

Eight virtual chips, on the owner's rulings of 2026-10-30:

1. **Bus arbitration belongs to the CPU subsystem** — the negotiation, grant sequencing and the
   4-cycle latencies are CPU-side logic; `BR`/`BG`/`BGACK` are pins between the CPU and whoever asks.
2. **Disk DMA belongs to the Disk subsystem** — the DMA controller (U300) goes to Disk whole.
3. **Video counters and video signals live outside Memory** — Memory does the *address fetch
   calculation* and nothing else, as far as is feasible.
4. **Timers and interrupts are one subsystem** — the MFP is not split.
5. **The Blitter is its own chip**, because it negotiates the bus (§3). This withdraws the earlier
   decision to fold it into Memory.

Register ownership is read from Hatari's I/O dispatch tables (`hatari/src/ioMemTabSTE.c`,
`ioMemTabST.c`), which name the owning block per address; pins are from `design/board-inventory.md`.

---

## 1. The table

| Virtual chip | Original chip and reference | Functions included | Registers included (internal + externally accessible) | Pins exposed |
|---|---|---|---|---|
| **CPU** | U100 MC68000 `C398061-001`, PLCC68 | instruction execution; exception and interrupt vectoring; bus-cycle sequencing; **bus arbitration** — `BR`/`BG`/`BGACK` negotiation, grant order, 4-cycle acquire and release latencies | *externally accessible*: none — the CPU owns no memory-mapped state. *internal*: D0–D7/A0–A7, SR, PC, prefetch queue, arbitration state machine | `D0–D15`, `A1–A23`, `AS`, `LDS`, `UDS`, `R/W`, `FC0–FC2`, `VMA`/`VPA`, `DTACK`, `BERR`, `IPL0–IPL2`, `BR`, `BG`, `BGACK`, `CLK`, `RESET` |
| **Memory** | U400 memory half `C300589-001` (144-pin) + 16 × DRAM `C0101743-002` + SIMM sockets U600–U603 | DRAM row/column multiplexing and strobes; refresh; memory-service scheduling; **address fetch calculation** for the video and sound streams (given an address, produce the access); `DTACK`/`BERR` generation; chip-select decode (`FCS`, `MFPCS`, `SNDCS`, `ROM0–ROM6`) | *externally accessible*: `$FF8001` MMU config. *internal*: refresh counter, row/column address latch, fetch address accumulator, current-bus-master state, decode PLA | `MAD0–MAD9`, `RAS0`, `RAS1`, `CAS0H`, `CAS0L`, `CAS1H`, `CAS1L`, `WE`, `AS`/`LDS`/`UDS`, `R/W`, `FC0–FC2`, `DTACK`, `BERR`, `BR`/`BG`/`BGACK`, `MD0–MD15`, `SREQ`, `SLOAD`, `FCS`, `MFPCS`, `SNDCS`, `ROM0–ROM6` |
| **Video** | U401 GST Shifter `C300588-001`, PLCC84 + U400 video-timing half | **beam counters and sync generation** (`HSYNC`, `VSYNC`, `DE`, `BLANK`, `LATCH`); video address counter and base; line width; fine scroll; palette; resolution; bitplane shift; DE/BLANK gating; Super Hi-Res interpretation | *externally accessible*: `$FF8201`/`$FF8203`/`$FF820D` video base; `$FF8205`/`$FF8207`/`$FF8209` live video counter; `$FF820A` sync/frequency; `$FF820F` line width; `$FF8240–$FF825F` palette (16 words); `$FF8260`/`$FF8261` resolution; `$FF8264`/`$FF8265` fine scroll. *internal*: beam counter, shift registers, palette latch, pixel pipeline | `MD0–MD15` (in), `PD0–PD15`, `R0–R3`/`G0–G3`/`B0–B3` (out), `MONO`, `COLOR`, `HSYNC`, `VSYNC`, `DE`, `BLANK`, `LATCH`, `SCLK` (out), `FCLK`, `CLK`, `SREQ` (out), `SLOAD` (in), `MWE`, `MWD`, `MWK`, `A1–A6`, `CS`, `LD`, `HD`, `R/W` |
| **Sound** | U202 YM2149 `C025983`, DIP40 + U508 LMC1992 `C398058-001`, DIP28 | PSG: 3 tone channels, noise, envelope; LMC1992 volume/bass/treble/mixing; **audio DMA** (placed here by symmetry with the Disk ruling — see §4) | *externally accessible*: `$FF8800` PSG select, `$FF8802` PSG data, `$FF8900` sound DMA control, `$FF8901–$FF8913` frame start/counter/end, `$FF8921` mode, `$FF8922`/`$FF8924` Microwire. *internal*: PSG tone counters and envelope state, DMA address counter, sample-rate divider, Microwire shift register | `SD0–SD7`, `ACC`, `SNDCS`, `SNDIR`; `IOA0–IOA7`, `IOB0–IOB3` (owned by Controllers) |
| **Disk** | U301 WD1772 `C026028-002`, DIP28 + U300 DMA controller `C398739-001`, DIP40 | MFM encode/decode; sector search; stepping and drive control; **disk DMA**; ACSI and its bus-mastership request | *externally accessible*: `$FF8604` FDC command/status, `$FF8606` DMA mode control, `$FF8609`/`$FF860B`/`$FF860D` DMA address. *internal*: MFM shift register, CRC, sector counter, DMA word counter, drive phase | `FDCS`, `FDRQ`, `CD0–CD7`, `CR/W`, `CA1`, `CA2`, `STEP`, `DIRC`, `WG`, `WD`, `MO`, `RD`, `DDEN`, `TR00`, `WPRT`, `IP`, `MR`, `FCS`, `RDY`, `DCYC`, `HDCS`, `HDRQ`, `ACK` |
| **Timers and interrupts** | U203 MFP 68901 `C025984`, DIP48 | timers A–D; interrupt priority encoder and vectoring; GPIP edge detection; USART (RS-232, out of scope but pins occupied) | *externally accessible*: `$FFFA01` GPIP, `$FFFA03` AER, `$FFFA05` DDR, `$FFFA07`–`$FFFA15` IERA/B, IPRA/B, ISRA/B, IMRA/B, `$FFFA17` vector, `$FFFA19`/`1B`/`1D` timer control, `$FFFA1F`/`21`/`23`/`25` timer data, `$FFFA27`–`$FFFA2F` USART. *internal*: four countdown timers with prescalers, pending/in-service latches, USART shift registers | `MFPCS`, `IACK`, `IPL0–IPL2`, `DINT`, `FDINT`, `HDINT`, `BINT`, `SINT`, `KHZ500`, `EINT3`, `EINT5`, `EINT7`, USART pins (out of scope) |
| **Controllers** | U400 pad/pen ADC and port logic + U202 I/O ports + U900 74LS164 `C101611-164` + the keyboard 6850 ACIA `C025985` (U200 or U201 — unresolved) | paddle/pen ADC; joystick, button and joypad ports; keyboard matrix ports; **IKBD serial link** at 7812.5 baud | *externally accessible*: `$FF9200`/`$FF9202` joypad, `$FF9211`/`13`/`15`/`17` analog pads, `$FF9220`/`$FF9222` light pen, `$FFFC00`/`$FFFC02` keyboard ACIA, PSG ports A/B via `$FF8800`/`$FF8802`. *internal*: ADC conversion state, port latches, ACIA transmit/receive registers and framing counters | `JOYAH`, `JOYAL`, `JOYWE`, `JOYWL`, `BUTTON`, `PEN`, `PAD0X`, `PAD0Y`, `PAD1X`, `PAD1Y`, `PADRST`, `IOA0–IOA7`, `IOB0–IOB3`, `N6850`, serial data and clock to the keyboard |
| **Blitter** | U101 ST Blitter `C101643`, PLCC68 | blit engine; halftone; 16 logic operations; skew; **bus-master negotiation** — 4 cycles to acquire the bus, 4 to release, 4 cycles per word read and written on STE | *externally accessible*: `$FF8A00–$FF8A1E` halftone RAM (16 words), `$FF8A20`/`$FF8A22` source increments, `$FF8A24` source address, `$FF8A28`/`$FF8A2A`/`$FF8A2C` endmasks, `$FF8A2E`/`$FF8A30` destination increments, `$FF8A32` destination address, `$FF8A36` words per line, `$FF8A38` lines per block, `$FF8A3A` halftone op, `$FF8A3B` log op, `$FF8A3C` control, `$FF8A3D` skew. *internal*: source and destination pointers, line counter, endmask shifters, busy/ready state | `A1–A23`, `D0–D15`, `AS`/`LDS`/`UDS`, `R/W`, `DTACK`, `BERR`, `FC0–FC2`, `BR`, `BG`, `BGACK`, `VMA`/`VPA`, `CLK` |

Not virtual chips: `U403` MC1377 encoder, `U405` RGB/mono mux, `U502–U507` DACs, filters and op-amps
(analog tail); `U302`, `U307`, `U509`, `U510`, `U512`, `U632`, `U304`, `U306`, `U511`, `U500`, `U501`
(buffers, latches, pipeline registers — no state of their own).

---

## 2. Signals that cross, with acknowledgements

**E** = event (counted), **C** = completion or acknowledgement (not counted), **K** = clock (not
counted). Rates per frame at 50 Hz.

| Between | Signal | Class | Rate |
|---|---|---|---|
| CPU ↔ Memory | `AS`, `LDS`, `UDS`, `R/W`, `FC0–FC2` | C (request) | up to 40 000 |
| | `DTACK` | **C ack** | same |
| | `BERR` | **C ack** | ~0 |
| | `VMA`/`VPA` (MFP cycle) | C | per MFP access |
| | `RESET` | E | 1 per boot |
| | `CLK` | K | 8 MHz |
| CPU ↔ Blitter | `BR` (Blitter asks), `BG` (CPU grants), `BGACK` (Blitter holds) | E | 2 per blit — one acquire, one release |
| CPU ↔ Disk | `BR`/`BG`/`BGACK` through the same negotiation, for DMA cycles | E | 312 while streaming |
| CPU ↔ Timers | `IPL0–IPL2` | E | 1 + 313 + 160 + 61 |
| | `IACK` | **C ack** | one per acknowledge |
| | `KHZ500` | K | 500 kHz |
| CPU ↔ Video | `A1–A6`, `CS`, `LD`, `HD`, `R/W` | E | ~10 idle, up to 939 in a demo |
| Memory ↔ Video | `MD0–MD15` | data | 6 260 words (25 040 overscan) |
| | `SREQ` (Video asks for a slot) | E | 6 260 |
| | `SLOAD` (Memory answers) | **C ack** | 6 260 |
| | `DE`, `BLANK` | E | 313 each |
| | `MWE`, `MWD`, `MWK` | E | ~0 at Level 1 |
| Memory ↔ Disk | `FCS`, `RDY` | E | tens / ~60 |
| | `DCYC` | **C ack** | 312 |
| Memory → Sound | `SD0–SD7` | data | 125 / 250 / 500 / 1 000 |
| | `SNDCS`, `SNDIR` | E | 160–1 000 |
| Memory → Timers | DMA-active → Timer A | E | 160–1 000 |
| Video → host | `HSYNC`, `DE`, `BLANK` | E | 313 each |
| | `VSYNC` | E | 1 |
| | `R0–R3`, `G0–G3`, `B0–B3`, `MONO`, `COLOR` | data | continuous |
| Controllers → CPU | `N6850` decode, ACIA data | E | per input event |

---

## 3. The Blitter: does it negotiate the bus?

**Yes.** Two independent lines of evidence, both in the repo:

- **Pins.** `U101.13` and `U101.11` are on the `BR`/`BG`/`BGACK` net with `U400.29/.28/.27/.26`
  (`design/board-inventory.md` §4.1). A device with an arbitration handshake is a bus master.
- **Behaviour.** `hatari/src/blitter.c` implements `Blitter_BusArbitration(BUS_MODE_BLITTER)` and
  `(BUS_MODE_CPU)`, with **4 cycles to acquire the bus and 4 cycles to return it**, and its own
  commentary describes the CPU still running during the acquisition window. Bus reads and writes are
  `BLITTER_CYCLES_PER_BUS_READ/WRITE = 4` cycles per word.

So the Blitter is never inside "the memory direct-access schedule": it takes the bus from the CPU for
the duration of a blit, and Memory serves the accesses while it holds it. That is why it is its own
chip here.

---

## 4. Two consequences to confirm

1. **The video fetch becomes request-driven, not supplied.** With the counters in Video, `SREQ` is a
   real request and `SLOAD` its acknowledgement — 6 260 transactions per frame, each with a computable
   delay. Memory supplies *an access*, not *a stream*. That is the honest reading of "counters outside
   Memory", and it is a change from the previous ruling.
2. **Audio DMA is placed in Sound by symmetry, not by your word.** You said disk DMA belongs to Disk and
   nothing about the sound DMA counters, so they moved from Memory to Sound on the assumption that the
   same rule applies.
