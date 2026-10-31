# Subsystems specification: virtual chips, what they embed, and what crosses between them

The reference for the partition. Every claim about *which* registers a virtual chip owns is taken
from the vendored Hatari I/O dispatch tables (`hatari/src/ioMemTabSTE.c`, `ioMemTabST.c`) — they
record the handler per address, and the handler's name tells you which block of the machine owns it —
plus the board inventory (`design/board-inventory.md`) for pins. Where the sources disagree or are
silent, the row says so.

Seven virtual chips, three physical ICs split. `U400` GSTMCU goes to Memory, Video and
Timers/Controllers; `U300` DMA controller goes to Memory and Disk; `U202` YM2149 goes to Sound and
Controllers.

---

## 1. The chips in scope

| # | Virtual chip | Role | Composed of |
|---|---|---|---|
| 1 | **CPU** | executes, asks the bus for everything | U100 MC68000 |
| 2 | **Memory** | supplies byte streams; owns RAM, refresh, arbitration | U400 memory half; 16 DRAMs + 4 SIMMs; U101 Blitter; U300 bus-master half |
| 3 | **Video** | interprets the bitplane stream into pixels and colour | U401 GST Shifter |
| 4 | **Sound** | interprets sample/PSG state into audio | U202 YM2149; U508 LMC1992 |
| 5 | **Disk** | FDC and drive interface | U301 WD1772; U300 register half |
| 6 | **Timers & interrupts** | time and interrupt arbitration | U203 MFP 68901 |
| 7 | **Controllers** | input devices and their protocols | U400 pad/pen ADC + port logic; U202 I/O ports; U900 74LS164; keyboard ACIA (U200 or U201) |

Out of the model: `U403` MC1377, `U405` RGB/mono mux, `U502–U507` DACs/filters/op-amps (analog tail);
`U302/U307/U509/U510/U512/U632` buffers (glue); `U208` MIDI opto-coupler, `U206/U207` RS-232 drivers,
MIDI ACIA (out of scope by the map).

---

## 2. What each chip embeds

### 1. CPU — U100

| | |
|---|---|
| **Registers** | none. The CPU has no memory-mapped state; everything below is visible only through it. |
| **Logic** | instruction execution, exception vectoring, bus-cycle sequencing, arbitration participation |
| **Pins** | `A1–A23`, `D0–D15`, `AS`, `LDS`, `UDS`, `R/W`, `FC0–FC2`, `VMA`/`VPA`, `BR`/`BG`/`BGACK`, `IPL0–IPL2`, `DTACK`, `BERR`, `CLK`, `RESET` |

### 2. Memory — U400 memory half + DRAM + U101 + U300 bus-master

| Register | Address | What it is | Source |
|---|---|---|---|
| MMU config | `$FF8001` | memory configuration byte | `STMemory_MMU_Config_*` (ST + STE tables) |
| Video base, high | `$FF8201`, `$FF8203` | frame buffer base (bits 23–16, 15–8) | `Video_ScreenBase_WriteByte` |
| Video base, low | `$FF820D` | low byte of base | `Video_BaseLow_ReadByte` |
| Video counter | `$FF8205`, `$FF8207`, `$FF8209` | live counter: the byte address of the active fetch | `Video_ScreenCounter_*` |
| Line width | `$FF820F` | STE line width, added to the counter each line | `Video_LineWidth_*` |
| Sync / frequency | `$FF820A` | 50/60 Hz, sync mode | `Video_Sync_*` |
| Sound DMA control | `$FF8900` | rate, loop, play | `DmaSnd_SoundControl_*` |
| Sound DMA pointer | `$FF8903/05/07`, `$FF8909/0B/0D`, `$FF890F/11/13` | frame start, live counter, frame end | `DmaSnd_Frame*` |
| Sound mode | `$FF8921` | 8/16-bit, mono/stereo | `DmaSnd_SoundModeCtrl_*` |
| Disk DMA mode | `$FF8606` | DMA mode control (register half in Disk) | `FDC_DmaModeControl_WriteWord` |
| Disk DMA address | `$FF8609/0B/0D` | DMA address counter | `FDC_DmaAddress_*` |
| Blitter | `$FF8A00–$FF8A3E` | halftone RAM (16 words), source/dest increments and addresses, endmasks, words/lines per block, halftone op, log op, control, skew | `Blitter_*` |

**Logic:** DRAM addressing and multiplexing (`MAD0–MAD9`, `RAS0/1`, `CAS0H/L`, `CAS1H/L`, `WE`),
refresh counter, the per-line fetch schedule for video and sound, bus arbitration, DTACK/BERR
generation.
**Pins:** `MAD0–MAD9`, `RAS0`, `RAS1`, `CAS0H`, `CAS0L`, `CAS1H`, `CAS1L`, `WE`, `HSYNC`, `VSYNC`, `DE`,
`BLANK`, `LATCH`, `DTACK`, `BERR`, `BR`, `BG`, `BGACK`, `AS`/`LDS`/`UDS`, `R/W`, `FC0–FC2`,
`SREQ`, `SLOAD`, `MD0–MD15`; via U101: `A1–A23`, `D0–D15`, `AS/LDS/UDS`, `R/W`, `DTACK`, `BERR`,
`FC0–FC2`, `BR/BG/BGACK`, `VMA/VPA`; via U300: `FCS`, `RDY`, `DCYC`, `HDCS/HDRQ/ACK`.

### 3. Video — U401 Shifter

| Register | Address | What it is | Source |
|---|---|---|---|
| Palette | `$FF8240–$FF825F` | 16 words, 12 bits used (RGB 4:4:4 → 4096 colours) | `Video_Color0..15_*` |
| Resolution | `$FF8260`, `$FF8261` | 320×200 / 640×200 / 640×400 mono | `Video_ResGlueShifter_*`, `Video_ResShifter_*` |
| Fine scroll | `$FF8264`, `$FF8265` | STE horizontal fine scroll | `Video_HorScroll_*` |

**Logic:** bitplane shift (4 planes → 16 colours, 2 planes → 4, 1 plane → mono), the palette lookup,
the DE/BLANK gating of the visible area, Super Hi-Res interpretation (`MWE`/`MWD`/`MWK` write path).
**Pins:** `MD0–MD15` (in), `PD0–PD15`, `R0–R3`/`G0–G3`/`B0–B3` (out), `MONO`, `COLOR`, `SCLK` (out),
`FCLK`, `CLK`, `DE`, `BLANK`, `SREQ` (out), `SLOAD` (in), `MWE`, `MWD`, `MWK`, `A1–A6`, `CS`, `LD`,
`HD`, `R/W`.

### 4. Sound — U202 YM2149 + U508 LMC1992

| Register | Address | What it is | Source |
|---|---|---|---|
| PSG select | `$FF8800` | register number write / data read | `PSG_ff8800_*` |
| PSG data | `$FF8802` | data write | `PSG_ff880x_*` |
| Microwire data | `$FF8922` | LMC1992 serial data | `DmaSnd_MicrowireData_*` |
| Microwire mask | `$FF8924` | LMC1992 serial mask | `DmaSnd_MicrowireMask_*` |

**Logic:** three tone channels + noise + envelope; LMC1992 volume, bass, treble, mixing.
**Pins:** `SD0–SD7` (in), `ACC` (in), `IOA0–IOA7`/`IOB0–IOB3` (owned by Controllers), `SNDCS`,
`SNDIR`.

### 5. Disk — U301 WD1772 + U300 register half

| Register | Address | What it is | Source |
|---|---|---|---|
| FDC command/status | `$FF8604` | sector count / status word | `FDC_DiskController*` |
| DMA mode | `$FF8606` | mode control word | `FDC_DmaModeControl_WriteWord` |

**Logic:** MFM encode/decode, sector search, track stepping, DMA request handshake.
**Pins:** `FDCS`, `FDRQ`, `CD0–CD7`, `CR/W`, `CA1`, `CA2`, `STEP`, `DIRC`, `WG`, `WD`, `MO`, `RD`,
`DDEN`, `TR00`, `WPRT`, `IP`, `MR`, `CS`, `DRQ`.

### 6. Timers & interrupts — U203 MFP 68901

| Register | Address | What it is |
|---|---|---|
| GPIP, AER, DDR | `$FFFA01`, `03`, `05` | general-purpose I/O, active edge, data direction |
| IERA/B, IPRA/B, ISRA/B, IMRA/B | `$FFFA07`–`$FFFA15` | interrupt enable/pending/in-service/mask, both banks |
| Vector register | `$FFFA17` | interrupt vector number |
| Timer A/B/C/D control | `$FFFA19`, `1B`, `1D` | prescaler, mode |
| Timer A/B/C/D data | `$FFFA1F`, `21`, `23`, `25` | reload value |
| USART | `$FFFA27`–`$FFFA2F` | SCR, UCR, RSR, TSR, UDR — RS-232 (out of scope, pins occupied) |

**Logic:** four timers, interrupt priority encoder to `IPL0–IPL2`, `IACK` handling, GPIP bit
transitions used by TOS for VBL/HBL side channels.
**Pins:** `MFPCS`, `IACK`, `IPL0–IPL2`, `DINT`, `FDINT`, `HDINT`, `BINT`, `SINT`, `KHZ500`, `EINT3/5/7`.

### 7. Controllers — U400 pad/pen ADC + U202 ports + U900 + keyboard ACIA

| Register | Address | What it is | Source |
|---|---|---|---|
| Joypad buttons / DIP | `$FF9200` | STE joypad button state | `Joy_StePadButtons_DIPSwitches_*` |
| Joypad data | `$FF9202` | joypad shift register | `Joy_StePadMulti_*` |
| Analog pad 0/1 X/Y | `$FF9211`, `13`, `15`, `17` | paddle/analog inputs | `Joy_StePadAnalog*` |
| Light pen X/Y | `$FF9220`, `22` | pen position | `Joy_SteLightpen*` |
| Keyboard ACIA | `$FFFC00`, `$FFFC02` | status/control and data — the IKBD link at 7812.5 baud | `ACIA_IKBD_*` |
| PSG I/O ports A/B | via `$FF8800`/`$FF8802` | keyboard matrix read, joystick/FDD select | `PSG_ff880*` |

**Logic:** paddle/pen ADC, port select logic, the 74LS164 serialiser on the IKBD line.
**Pins:** `JOYAH/JOYAL/JOYWE/JOYWL`, `BUTTON`, `PEN`, `PAD0X/PAD0Y/PAD1X/PAD1Y`, `PADRST`,
`IOA0–IOA7`, `IOB0–IOB3`, `N6850`, plus the ACIA's data/clock pins.

**Open:** which of `U200`/`U201` is the keyboard ACIA is unresolved in the redraw (inventory §6.13).
**Out of scope but embedded here:** MIDI ACIA at `$FFFC04`/`$FFFC06` and the `U208` opto-isolator.

---

## 3. Signals that cross, with acknowledgements

Classes: **E** = event (counted), **C** = completion/acknowledgement (not counted), **K** = clock/phase
(not counted). Frequencies as in `design/crossings.md`.

### CPU ↔ Memory

| Signal(s) | Class | Rate |
|---|---|---|
| `AS`, `LDS`, `UDS`, `R/W`, `FC0–FC2` → Memory | C (request) | up to 40 000/frame |
| `DTACK` → CPU | **C ack** | same |
| `BERR` → CPU | **C ack** | ~0 |
| `BR` → Memory, `BG` ← Memory, `BGACK` → Memory | E | 312–1 000/frame |
| `VMA`/`VPA` (MFP cycle) | C | per MFP access |
| `RESET` | E | 1 per boot |
| `CLK` | K | 8 MHz |

### CPU ↔ Video

| `A1–A6`, `CS`, `LD`, `HD`, `R/W` | E | ~10/frame idle, up to 939 in a demo |

### CPU ↔ Timers

| `MFPCS` (from Memory's decode) | E | 160–1 000/frame |
| `IPL0–IPL2` → CPU | E | 1 + 313 + 160 + 61 per frame |
| `IACK` ↔ MFP | **C ack** | one per acknowledge |
| `KHZ500` | K | 500 kHz |

### Memory ↔ Video

| `MD0–MD15` → Video | data (memory service) | 6 260 words/frame, 25 040 overscan |
| `DE`, `BLANK` → Video | E | 313/frame each |
| `SREQ` → Memory | C (request) | 6 260/frame |
| `SLOAD` → Video | **C ack** | 6 260/frame |
| `MWE`/`MWD`/`MWK` ← Video | E | ~0 at Level 1 |
| `LATCH` → palette latch | E | up to 939/frame |

### Memory ↔ Disk

| `FCS` → DMA | E | tens/frame |
| `RDY` → Memory | E | ~60/frame |
| `DCYC` → DMA | **C ack** | 312/frame |
| `HDRQ`, `HDCS` ↔ ACSI, `ACK` | C | 0 in v1 (no hard disk) |

### Disk ↔ FDC (inside the Disk chip)

| `FDCS` → FDC | E | ~20/frame |
| `FDRQ` → DMA | E | 625/frame while reading |

### Memory → Sound

| `SD0–SD7` (sample bus) | data | 125/250/500/1 000 words/frame |
| `SNDCS`, `SNDIR` | E | 160–1 000/frame |

### Memory → Timers

| DMA-active → Timer A | E | 1 000/frame at 50 kHz, 160/frame at 8 kHz |

### Video → host

| `HSYNC` | E | 313/frame |
| `VSYNC` | E | 1/frame |
| `R0–R3`, `G0–G3`, `B0–B3`, `MONO`, `COLOR` | data | continuous |

### Controllers → CPU (via Memory decode)

| `N6850`, `$FF9200` decode, ACIA data | E | per event |

---

## 4. Where the Memory/Video line is genuinely uncertain

The register map **supports** the split rather than settling it:

- `$FF8200–$FF820F` is the **address/sync block**: video base, live counter, line width, 50/60 Hz.
  In Hatari these live in `video.c` alongside the shifter code, but their content is beam position and
  fetch address — supply-side, and they belong with the DRAM interface that uses them.
- `$FF8240–$FF8265` is the **shifter block**: palette, resolution, fine scroll — interpret-side.
- **Two registers straddle the line.** `$FF8260` is named `Video_ResGlueShifter_WriteByte` — Hatari's
  own name says both halves see it — and there is a second, `$FF8261`
  `Video_ResShifter_WriteByte`, for the Shifter only. And `$FF820F` (line width) is read by the
  address arithmetic while `$FF8264/65` (fine scroll) shifts the pixels: Hatari's own comment notes
  that on STE "LineWidth value in `$FF820F` is added to the shifter counter", i.e. the fetch address
  depends on the shift offset.

So the honest statement is: **the supply/interpret line is clean for the streams and the palette, and
blurred for resolution and scroll.** Three ways to draw it, and this is the decision worth taking
deliberately:

1. **Memory owns `$FF8200–$FF820F`; Video owns `$FF8240–$FF8265`** (the proposal above). Costs: `$FF8260`
   must be visible to both, and fine scroll must reach the fetch address across a chip boundary.
2. **Video owns the whole video register block `$FF8200–$FF8265`, and Memory only owns DRAM, refresh
   and arbitration.** Clean single owner for everything a demo writes; cost: the fetch schedule is then
   driven from Video, which is the "video does make progress on its own" reading — and the stream is no
   longer supplied by the memory subsystem, it is pulled by Video.
3. **Keep the split but make the shared registers part of the *boundary*, not of a chip**: `$FF8260`,
   `$FF8264/65`, `$FF820F` are declared joint state that both chips read at the two points where they
   meet. Cost: a state object owned by nobody, which is the kind of thing this project usually refuses.

Recommendation: **option 1**, because it is the only one where the fetch schedule and the DRAM that
serves it are in the same chip, which is what makes "supply a stream" implementable without a
per-fetch handshake; and because the two shared registers are shared *in the hardware too* — Hatari's
naming is evidence, not a modelling artefact. What it costs is stated above and is not hidden.
