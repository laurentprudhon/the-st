# Subsystems specification

Eight virtual chips. Two rules decide every boundary, both from the owner (2026-10-30):

- **Two kinds of DMA, split by how they reach the bus.** A stream with direct access to the memory bus —
  video fetch, sound DMA — belongs to the **Memory** subsystem, because it *is* the memory controller's
  own schedule and never touches the CPU bus. A temporary burst that must ask the CPU for the bus — Disk
  DMA, Blitter — belongs to **the chip that produces the burst**.
- **Only the address counters and the fetch machinery for those streams stay in Memory**; everything that
  interprets the bytes stays outside.

Plus, from the same rulings: **bus arbitration is CPU-side** (grant sequencing), **timers and interrupts
are one subsystem**, and **the Blitter is its own chip** because it negotiates the bus.

Evidence: `research/bus-arbitration.md` (STE hardware article, ST Magazine 44/1990, plus the field
service manual), Hatari's I/O dispatch tables (`ioMemTabSTE.c`) for register ownership, and
`design/board-inventory.md` for pins.

---

## 1. The table

| Virtual chip | Original chip and reference | Functions included | Registers included (internal + externally accessible) | Pins exposed |
|---|---|---|---|---|
| **CPU** | U100 MC68000 `C398061-001`, PLCC68 | instruction execution; exception and interrupt vectoring; bus-cycle sequencing; **bus arbitration** — the grant sequence, 4-cycle acquire and release | *externally accessible*: none — the CPU owns no memory-mapped state. *internal*: D0–D7/A0–A7, SR, PC, prefetch queue, arbitration state machine | `D0–D15`, `A1–A23`, `AS`, `LDS`, `UDS`, `R/W`, `FC0–FC2`, `VMA`/`VPA`, `DTACK`, `BERR`, `IPL0–IPL2`, `BR`, `BG`, `BGACK`, `CLK`, `RESET` |
| **Memory** | U400 memory half `C300589-001` (144-pin) + 16 × DRAM `C0101743-002` + SIMM sockets U600–U603 | DRAM mux and strobes; refresh; **video address counters and fetch machinery** (`VBASE`, `VCOUNT`, `LINEWIDTH`, the 16 000-addresses-per-frame schedule); **sound DMA counters and fetch machinery** (`FBASE`, `FCOUNT`, `FEND`); `DTACK`/`BERR`; chip-select decode; **sync generation** (`HSYNC`, `VSYNC`, `DE`, `*BLANK`, `*DCYC`) — a GLUE function on this machine | *externally accessible*: `$FF8001` MMU config; `$FF8201`/`$FF8203`/`$FF820D` video base; `$FF8205`/`$FF8207`/`$FF8209` video counter; `$FF820A` sync/frequency; `$FF820F` line width; `$FF8900` sound control, `$FF8903`–`$FF8913` frame pointers, `$FF8921` sound mode; **a second copy of `$FF8260`** (GSTMCU side). *internal*: refresh counter, DRAM address latch, video address counter, sound address counter, fetch scheduler, decode PLA | `MAD0–MAD9`, `RAS0`, `RAS1`, `CAS0H`, `CAS0L`, `CAS1H`, `CAS1L`, `WE`, `AS`/`LDS`/`UDS`, `R/W`, `FC0–FC2`, `DTACK`, `BERR`, `BR`, `BG`, `BGACK`, `MD0–MD15`, `SREQ`, `SLOAD`, `DCYC`, `HSYNC`, `VSYNC`, `DE`, `BLANK`, `LATCH`, `FCS`, `MFPCS`, `SNDCS`, `ROM0–ROM6` |
| **Video** | U401 GST Shifter `C300588-001`, PLCC84 | bitplane shift (4 planes → 16 colours, 2 → 4, 1 → mono); palette lookup; DE/BLANK gating; fine scroll; **the Shifter's copy of the resolution register**; Super Hi-Res interpretation; **the Microwire controller driving the LMC1992**. The sound-DMA requester side (`SREQ`, the 4-word FIFO, the byte split, `SD0–SD7`) is **Sound's**, although those pins are physically on this chip (owner's ruling, 2026-11-05) | *externally accessible*: `$FF8240–$FF825F` palette (16 words); `$FF8260` (Shifter copy); `$FF8261`; `$FF8264`/`$FF8265` fine scroll; `$FF8922`/`$FF8924` Microwire data/mask. *internal*: shift registers, palette latch, pixel pipeline, Microwire shift register, sample byte latch | `MD0–MD15` (in), `PD0–PD15`, `R0–R3`/`G0–G3`/`B0–B3` (out), `MONO`, `COLOR`, `DE`, `BLANK`, `LATCH`, `SCLK`, `FCLK`, `CLK`, `SREQ` (out), `SLOAD`, `MWE`, `MWD`, `MWK`, `SD0–SD7` (out), `A1–A6`, `CS`, `LD`, `HD`, `R/W` |
| **Sound** | U202 YM2149 `C025983`, DIP40 + U508 LMC1992 `C398058-001`, DIP28 | PSG: 3 tone channels, noise, envelope; LMC1992 volume/bass/treble/mixing; **the sound-DMA requester side** — the 4-word sample FIFO, `SREQ`, and the split of each fetched word into two bytes on `SD0–SD7` (physically on the Shifter, owned here) | *externally accessible*: `$FF8800` PSG select, `$FF8802` PSG data. *internal*: PSG tone counters, envelope state, noise LFSR | `SD0–SD7` (via the DACs), `ACC`, `SNDCS`, `SNDIR`; `IOA0–IOA7`, `IOB0–IOB3` (owned by Controllers) |
| **Disk** | U301 WD1772 `C026028-002`, DIP28 + U300 DMA controller `C398739-001`, DIP40 | MFM encode/decode; sector search; stepping and drive control; **disk DMA as a bus-mastering burst** — `*RDY` to the GSTMCU, `*BR` relayed to the CPU, `*BGACK` held for the burst; ACSI | *externally accessible*: `$FF8604` FDC command/status, `$FF8606` DMA mode control, `$FF8609`/`$FF860B`/`$FF860D` DMA address. *internal*: MFM shift register, CRC, sector counter, DMA word counter, drive phase | `FDCS`, `FDRQ`, `CD0–CD7`, `CR/W`, `CA1`, `CA2`, `STEP`, `DIRC`, `WG`, `WD`, `MO`, `RD`, `DDEN`, `TR00`, `WPRT`, `IP`, `MR`, `FCS`, `RDY`, `DCYC`, `HDCS`, `HDRQ`, `ACK` |
| **Timers and interrupts** | U203 MFP 68901 `C025984`, DIP48 | timers A–D; interrupt priority encoder and vectoring; GPIP edge detection (HBL/VBL side channels); USART (RS-232, out of scope but pins occupied) | *externally accessible*: `$FFFA01` GPIP, `$FFFA03` AER, `$FFFA05` DDR, `$FFFA07`–`$FFFA15` IERA/B, IPRA/B, ISRA/B, IMRA/B, `$FFFA17` vector, `$FFFA19`/`1B`/`1D` timer control, `$FFFA1F`/`21`/`23`/`25` timer data, `$FFFA27`–`$FFFA2F` USART. *internal*: four countdown timers with prescalers, pending/in-service latches, USART shift registers | `MFPCS`, `IACK`, `IPL0–IPL2`, `DINT`, `FDINT`, `HDINT`, `BINT`, `SINT`, `KHZ500`, `EINT3`, `EINT5`, `EINT7`, USART pins (out of scope) |
| **Controllers** | U400 pad/pen ADC and port logic + U202 I/O ports + U900 74LS164 `C101611-164` + the keyboard 6850 ACIA `C025985` (U200 or U201 — unresolved) | paddle/pen ADC; joystick, button and joypad ports; keyboard matrix ports; **IKBD serial link** at 7812.5 baud | *externally accessible*: `$FF9200`/`$FF9202` joypad, `$FF9211`/`13`/`15`/`17` analog pads, `$FF9220`/`$FF9222` light pen, `$FFFC00`/`$FFFC02` keyboard ACIA, PSG ports A/B via `$FF8800`/`$FF8802`. *internal*: ADC conversion state, port latches, ACIA transmit/receive registers and framing counters | `JOYAH`, `JOYAL`, `JOYWE`, `JOYWL`, `BUTTON`, `PEN`, `PAD0X`, `PAD0Y`, `PAD1X`, `PAD1Y`, `PADRST`, `IOA0–IOA7`, `IOB0–IOB3`, `N6850`, serial data and clock to the keyboard |
| **Blitter** | U101 ST Blitter `C101643`, PLCC68 | blit engine; halftone; 16 logic operations; skew; **bus mastership taken directly from the CPU** — `BR`/`BG`/`BGACK` do not pass through the GSTMCU; 4 cycles to acquire, 4 per word, 4 to release | *externally accessible*: `$FF8A00–$FF8A1E` halftone RAM (16 words), `$FF8A20`/`$FF8A22` source increments, `$FF8A24` source address, `$FF8A28`/`$FF8A2A`/`$FF8A2C` endmasks, `$FF8A2E`/`$FF8A30` destination increments, `$FF8A32` destination address, `$FF8A36` words per line, `$FF8A38` lines per block, `$FF8A3A` halftone op, `$FF8A3B` log op, `$FF8A3C` control, `$FF8A3D` skew. *internal*: source and destination pointers, line counter, endmask shifters, busy/ready state | `A1–A23`, `D0–D15`, `AS`/`LDS`/`UDS`, `R/W`, `DTACK`, `BERR`, `FC0–FC2`, `BR`, `BG`, `BGACK`, `VMA`/`VPA`, `CLK` |

Not virtual chips: analog tail (`U403` MC1377, `U405` RGB/mono mux, `U502–U507` DACs, filters, op-amps)
and glue (`U302`, `U307`, `U509`, `U510`, `U512`, `U632`, `U304`, `U306`, `U511`, `U500`, `U501`).

---

## 2. Signals that cross

**This section is superseded.** The authoritative table is [`design/crossings.md`](crossings.md), which
is the subscription table under ADR 0006 and carries, per row, the wire, the driver, the subscriber, the
resolver, the class and the rate. What remains here is the vocabulary, because the partition table above
uses it:

- **Class 1 — a crossing.** A subscription whose firing starts new work in the receiver. Rate set by the
  writer, unbounded.
- **Class 2 — an answer inside an open exchange.** A subscription that only completes a bus or DMA
  cycle the subscriber itself opened. Bounded, and inlinable later (ADR 0005).
- **Class 3 — phase.** A clock is not a wire: arithmetic on machine time. No records, no subscriptions.
- **Data.** Address and data buses carry records and no class-1 subscription. A subscriber needing a
  value samples the data wire at the instant of the control record that woke it.

Four things the earlier draft of this table got wrong, all corrected in `crossings.md`: `SREQ`/`*SLOAD`
are the sound path and `DCYC`/`*LOAD` the video path; the FDC↔DMA handshake (`FDCS`/`FDRQ`) is internal
to Disk; the video delivery is one crossing per four words, not per word; and the CPU's own bus cycle is
a class-2 exchange whose cost is the queue's, not the catalogue's.

---

## 3. The Blitter: does it negotiate the bus?

**Yes**, and it does so without the GSTMCU in the path. `U101.13`/`.11` are on the `BR`/`BG`/`BGACK` net
with `U400.29/.28/.27/.26` (`design/board-inventory.md` §4.1); `hatari/src/blitter.c` implements
`Blitter_BusArbitration(BUS_MODE_BLITTER)` and `(BUS_MODE_CPU)` with 4 cycles to acquire and 4 to
release, and 4 cycles per word read or written. The STE article states the consequence directly: the
Blitter "does not need the GST MCU to make the request to the 68000", though the GSTMCU still arbitrates
to avoid congestion.

Disk DMA is the opposite case: the DMA controller cannot ask the CPU itself — it asserts `*RDY` to the
GSTMCU, which relays `*BR`. That relay is why disk DMA is a *memory-subsystem-mediated* burst while the
Blitter's is a direct one, and it is the only asymmetry between the two bus masters.

---

## 4. Corrections and open points

1. **`SREQ`/`*SLOAD` are the sound path, not the video path** (STE article). The video handshake is
   `*DCYC` + `*LOAD`. The inventory lists `SLOAD → U401.37 (LOAD)`, so one of the two labels is wrong in
   the redraw.
2. **`DCYC` is drawn only towards U300 in the redraw**, while the article has it telling the Shifter that
   fetched data are ready. Either it fans out to both or the redraw lost a net.
3. **Video and sound counters are physically GSTMCU registers** (`VBASE`, `VCOUNT`, `LINEWIDTH`, sync,
   `FBASE`, `FCOUNT`, `FEND`), and only `COLORS`, `VIDEOMOD` and `HSCROLL` are in the Shifter — which is
   why the counters stay in Memory rather than moving to Video.
4. **`$FF8260` exists twice**, one copy in each chip.
5. **Sync generation is a GLUE/GSTMCU function.** That is the one place where "keep everything but the
   counters outside Memory" cannot be honoured without deviating from the hardware; it is recorded here
   rather than silently split.
6. **Per-line slot placement is still unknown**: which cycles of a 512-cycle line belong to the CPU, which
   to video fetch, and where sound-DMA cycles sit. The 16 000-words-per-frame figure bounds the total,
   not the placement (`research/bus-arbitration.md`).
