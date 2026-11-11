# Subsystems specification

Eight virtual chips. Two rules decide every boundary, both from the owner (2026-10-30):

- **Two kinds of DMA, split by how they reach the bus.** A stream with direct access to the memory bus —
  video fetch, sound DMA — belongs to the **System Control Unit** subsystem, because it *is* the memory controller's
  own schedule and never touches the CPU bus. A temporary burst that must ask the CPU for the bus — Disk
  DMA, Blitter — belongs to **the chip that produces the burst**.
- **Only the address counters and the fetch machinery for those streams stay in the System Control Unit**; everything that
  interprets the bytes stays outside.

Plus, from the same rulings: **bus arbitration is CPU-side** (grant sequencing), **timers and interrupts
are one subsystem**, and **the Blitter is its own chip** because it negotiates the bus.

Evidence: `research/bus-arbitration.md` (STE hardware article, ST Magazine 44/1990, plus the field
service manual), Hatari's I/O dispatch tables (`ioMemTabSTE.c`) for register ownership, and
`design/board-inventory.md` for pins.

---

> ### Correction — 2026-11-11 (ticket #31, owner's ruling)
>
> The virtual chip this file calls *Memory* is the **System Control Unit**, and the DRAM and the TOS
> ROMs are no longer members with pins: they are plain data structures (`Ram`, `Rom`) inside it —
> stores with no timeline, no record, no subscription and no wake. Every statement below about the
> counter, the owner equation, the clients, the beam, the buffer or the leaf stands unchanged; only
> the name and the store boundary moved. Owner's words: *"GSTMCU is too cryptic when you want to
> understand the machine, what about keeping only three letters of the name : SystemControlUnit ?"*
> See [ADR 0014](../docs/adr/0014-the-stores-are-data-structures.md) and `design/system-control-unit.md`.

## 1. The table

| Virtual chip | Original chip and reference | Functions included | Registers included (internal + externally accessible) | Pins exposed |
|---|---|---|---|---|
| **CPU** | U100 MC68000 `C398061-001`, PLCC68 | instruction execution; exception and interrupt vectoring; bus-cycle sequencing; **bus arbitration** — the grant sequence, 4-cycle acquire and release | *externally accessible*: none — the CPU owns no memory-mapped state. *internal*: D0–D7/A0–A7, SR, PC, prefetch queue, arbitration state machine | `D0–D15`, `A1–A23`, `AS`, `LDS`, `UDS`, `R/W`, `FC0–FC2`, `VMA`/`VPA`, `DTACK`, `BERR`, `IPL0–IPL2`, `BR`, `BG`, `BGACK`, `CLK`, `RESET` |
| **System Control Unit** | U400 memory half `C300589-001` (144-pin). The DRAMs (32 × `C0101743-002`, 16 per bank, two banks) and `U102`/`U103` (2 × 27C010) are **stores** it holds — `Ram`, `Rom` — not members with pins (ADR 0014) | DRAM controller, mux and strobes (the parts themselves are `Ram`); refresh scheduling; **video address counters and fetch machinery** (`VBASE`, `VCOUNT`, `LINEWIDTH`, the 16 000-addresses-per-frame schedule); **sound DMA counters and fetch machinery** (`FBASE`, `FCOUNT`, `FEND`); `DTACK`/`BERR`; chip-select decode; **sync generation** (`HSYNC`, `VSYNC`, `DE`, `*BLANK`, `*DCYC`) — a GLUE function on this machine | *externally accessible*: `$FF8001` MMU config; `$FF8201`/`$FF8203`/`$FF820D` video base; `$FF8205`/`$FF8207`/`$FF8209` video counter; `$FF820A` sync/frequency; `$FF820F` line width; `$FF8900` sound control, `$FF8903`–`$FF8913` frame pointers, `$FF8921` sound mode; **a second copy of `$FF8260`** (GSTMCU side). *internal*: refresh counter, DRAM address latch, video address counter, **the 4-word video fetch buffer**, sound address counter, fetch scheduler, decode PLA | `MAD0–MAD9`, `RAS0`, `RAS1`, `CAS0H`, `CAS0L`, `CAS1H`, `CAS1L`, `WE`, `AS`/`LDS`/`UDS`, `R/W`, `FC0–FC2`, `DTACK`, `BERR`, `BR`, `BG`, `BGACK`, `MD0–MD15`, `SREQ`, `SLOAD`, `DCYC`, `HSYNC`, `VSYNC`, `DE`, `BLANK`, `LATCH`, `FCS`, `MFPCS`, `SNDCS`, `ROM0–ROM6` |
| **Video** | U401 GST Shifter `C300588-001`, PLCC84 | bitplane shift (4 planes → 16 colours, 2 → 4, 1 → mono); palette lookup; DE/BLANK gating; fine scroll; **the Shifter's copy of the resolution register**. (There is no fourth resolution to interpret on this board — the "Super Hi-Res" label was withdrawn 2026-11-11, `research/ste-super-hires.md`. The Microwire controller left this row on 2026-11-11: its pins are on U401 but its registers and its logic are Sound's — see the Sound row.) The sound-DMA requester side (`SREQ`, the 4-word FIFO, the byte split, `SD0–SD7`) is **Sound's**, although those pins are physically on this chip (owner's ruling, 2026-11-05) | *externally accessible*: `$FF8240–$FF825F` palette (16 words); `$FF8260` (Shifter copy); `$FF8261`; `$FF8264`/`$FF8265` fine scroll. *internal*: shift registers, palette latch, pixel pipeline, pixel counter | `MD0–MD15` (in), `PD0–PD15`, `R0–R3`/`G0–G3`/`B0–B3` (out), `MONO`, `COLOR`, `DE`, `BLANK`, `LATCH`, `SCLK`, `FCLK`, `CLK`, `SREQ` (out), `SLOAD`, `MWE`, `MWD`, `MWK` (out; Sound's logic), `SD0–SD7` (out), `A1–A6`, `CS`, `LD`, `HD`, `R/W` |
| **Sound** | U202 YM2149 `C025983`, DIP40 + U508 LMC1992 `C398058-001`, DIP28 | PSG: 3 tone channels, noise, envelope; LMC1992 volume/bass/treble/mixing; **the Microwire controller that drives the LMC1992** (`*MWE`/`MWD`/`MWK` — pins on the Shifter, logic here, owner's ruling 2026-11-11); **the sound-DMA requester side** — the 4-word sample FIFO, `SREQ`, and the split of each fetched word into two bytes on `SD0–SD7` (physically on the Shifter, owned here) | *externally accessible*: `$FF8800` PSG select, `$FF8802` PSG data, **`$FF8922` Microwire data, `$FF8924` Microwire mask** (2026-11-11; the shift and its mask rotation are internal state). *internal*: PSG tone counters, envelope state, noise LFSR, Microwire shift register and mask | `SD0–SD7` (via the DACs), `ACC`, `SNDCS`, `SNDIR`, `MWE`, `MWD`, `MWK` (out, physically on U401); `IOA0–IOA7`, `IOB0–IOB3` (owned by Controllers) |
| **Disk** | U301 WD1772 `C026028-002`, DIP28 + U300 DMA controller `C398739-001`, DIP40 | MFM encode/decode; sector search; stepping and drive control; **disk DMA as a bus-mastering burst** — `*RDY` to the GSTMCU, `*BR` relayed to the CPU, `*BGACK` held for the burst; ACSI | *externally accessible*: `$FF8604` FDC command/status, `$FF8606` DMA mode control, `$FF8609`/`$FF860B`/`$FF860D` DMA address. *internal*: MFM shift register, CRC, sector counter, DMA word counter, drive phase | `FDCS`, `FDRQ`, `CD0–CD7`, `CR/W`, `CA1`, `CA2`, `STEP`, `DIRC`, `WG`, `WD`, `MO`, `RD`, `DDEN`, `TR00`, `WPRT`, `IP`, `MR`, `FCS`, `RDY`, `DCYC`, `HDCS`, `HDRQ`, `ACK` |
| **Timers and interrupts** | U203 MFP 68901 `C025984`, DIP48 | timers A–D; interrupt priority encoder and vectoring; GPIP edge detection (HBL/VBL side channels); USART (RS-232, out of scope but pins occupied) | *externally accessible*: `$FFFA01` GPIP, `$FFFA03` AER, `$FFFA05` DDR, `$FFFA07`–`$FFFA15` IERA/B, IPRA/B, ISRA/B, IMRA/B, `$FFFA17` vector, `$FFFA19`/`1B`/`1D` timer control, `$FFFA1F`/`21`/`23`/`25` timer data, `$FFFA27`–`$FFFA2F` USART. *internal*: four countdown timers with prescalers, pending/in-service latches, USART shift registers | `MFPCS`, `IACK`, `IPL0–IPL2`, `DINT`, `FDINT`, `HDINT`, `BINT`, `SINT`, `KHZ500`, `EINT3`, `EINT5`, `EINT7`, USART pins (out of scope) |
| **Controllers** | U400 pad/pen ADC and port logic + U202 I/O ports + U900 74LS164 `C101611-164` + the keyboard 6850 ACIA `C025985` (U200 or U201 — unresolved) | paddle/pen ADC; joystick, button and joypad ports; keyboard matrix ports; **IKBD serial link** at 7812.5 baud | *externally accessible*: `$FF9200`/`$FF9202` joypad, `$FF9211`/`13`/`15`/`17` analog pads, `$FF9220`/`$FF9222` light pen, `$FFFC00`/`$FFFC02` keyboard ACIA, PSG ports A/B via `$FF8800`/`$FF8802`. *internal*: ADC conversion state, port latches, ACIA transmit/receive registers and framing counters | `JOYAH`, `JOYAL`, `JOYWE`, `JOYWL`, `BUTTON`, `PEN`, `PAD0X`, `PAD0Y`, `PAD1X`, `PAD1Y`, `PADRST`, `IOA0–IOA7`, `IOB0–IOB3`, `N6850`, serial data and clock to the keyboard |
| **Blitter** | U101 ST Blitter `C101643`, PLCC68 | blit engine; halftone; 16 logic operations; skew; **bus mastership taken directly from the CPU** — `BR`/`BG`/`BGACK` do not pass through the GSTMCU; 4 cycles to acquire, 4 per word, 4 to release | *externally accessible*: `$FF8A00–$FF8A1E` halftone RAM (16 words), `$FF8A20`/`$FF8A22` source increments, `$FF8A24` source address, `$FF8A28`/`$FF8A2A`/`$FF8A2C` endmasks, `$FF8A2E`/`$FF8A30` destination increments, `$FF8A32` destination address, `$FF8A36` words per line, `$FF8A38` lines per block, `$FF8A3A` halftone op, `$FF8A3B` log op, `$FF8A3C` control, `$FF8A3D` skew. *internal*: source and destination pointers, line counter, endmask shifters, busy/ready state | `A1–A23`, `D0–D15`, `AS`/`LDS`/`UDS`, `R/W`, `DTACK`, `BERR`, `FC0–FC2`, `BR`, `BG`, `BGACK`, `VMA`/`VPA`, `CLK` |

Not virtual chips: analog tail (`U403` MC1377, `U405` RGB/mono mux, `U502–U507` DACs, filters, op-amps)
and glue (`U302`, `U307`, `U509`, `U510`, `U512`, `U632`, `U304`, `U306`, `U511`, `U500`, `U501`).

**Correction — 2026-11-11 (ticket #28).** The Timers row's pin list said `IPL0–IPL2`; **the MFP has no
`IPL` pins**. It raises `IRQ` (`U203.32`) into the GSTMCU, which combines it with VBL (4) and HBL (2)
and drives `IPL0–IPL2` itself (`board-inventory.md` §4.3, corrected 2026-11-05). The same row's "GPIP
edge detection (HBL/VBL side channels)" is also wrong — HBL and VBL are the System Control Unit's counters and never
reach the MFP. What `GPIP` carries on this board: the four external completions (`FDINT` and `DINT`
share source 7, `BINT` is source 3, `SINT` is source 15 — the line the STE shares with mono detect, and
also Timer A's event input), the keyboard ACIA (source 6) and the two RS-232 status lines (sources 1
and 2). Sources and bit assignments: `atari-st-hardware/components/mfp/01-mc68901.md`'s dated block.
The USART is a declared stub (RS-232 is out of scope on the map).

**Correction — 2026-11-11 (ticket #31, owner's ruling).** The first column's **Memory** row is the
**System Control Unit**, and the DRAM parts and the two TOS ROMs are **stores** (`Ram`, `Rom`) it
holds rather than members with pins — no pin of theirs leaves the chip, they own no clock, subscribe
to nothing and are woken by nothing (ADR 0014, `design/system-control-unit.md` §2). The eight chips,
the functions, the registers and the pin list of the row are otherwise unchanged; `MAD0–MAD9`,
`RAS0/1`, `CAS0H/L`, `CAS1H/L`, `WE` and `ROM0–ROM6` remain `U400`'s pins and are internal to the
sockets and the ROM pair. A store also gains **two members of its own**: `Ram` is 2 banks × 512 rows ×
512 columns × 16 bits = 1 MB, `Rom` is 2 × 27C010 = 256 KB.

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
   why the counters stay in the System Control Unit rather than moving to Video.
4. **`$FF8260` exists twice**, one copy in each chip.
5. **Sync generation is a GLUE/GSTMCU function.** That is the one place where "keep everything but the
   counters outside the System Control Unit" cannot be honoured without deviating from the hardware; it is recorded here
   rather than silently split.
6. **Per-line slot placement — answered by #20** (ADR 0011, `design/system-control-unit.md`): one 500 ns cycle with one
   internal and one external slot, the internal slot owned by video (display window), sound (`SREQ`,
   blanking) or refresh (filler), and the CPU/DMA answered at the next external slot. The 16 000-words
   figure was the total; the placement is now decided.
