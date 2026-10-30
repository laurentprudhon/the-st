# Signals that cross, and their frequencies

Every net that leaves one virtual chip and arrives at another, with its rate on the machine's
timeline. Chip names are the seven from `design/virtual-chips.md`; pin names are the inventory's
(`design/board-inventory.md` §3–§4).

Frame = 640 000 ticks = 20.000 ms, 313 lines × 512 CPU cycles, 50.000 Hz. So **× 50 converts
per-frame to per-second**, and 1 event per frame = 50/s.

## The classification rule this catalogue needs

Three classes, because "crossing" cannot mean "any net with two ends on it":

1. **Event** — a signal that *starts something new* in the receiving chip: an interrupt, a request, a
   load, an arbitration grant. These are crossings.
2. **Completion** — a signal that only *answers a request the same exchange already made*: `DTACK`,
   `BERR`, `RDY`, `DCYC`, `ACK`. The receiving chip's wait has a *computed* duration, so the answer is
   a latency inside the memory subsystem, taken on the fast path (ADR 0002), not an event in the
   global queue.
3. **Clock / phase** — a signal whose job is to give two chips a common phase: `CLK`, `CLK8/CLK4/CLK2`,
   `FCLK`, `SCLK`, `KHZ500`. `GLOSSARY.md` already says a clock "is not a timeline: it gives its chips
   a common phase, nothing more" — so a clock is never counted, however high its frequency.

**This rule decides the total.** Class 1 alone gives ~10²–10³ per frame. Fold class 2 in and the count
becomes the CPU's bus-cycle rate — ~4 × 10⁴ per frame — which is precisely the 10⁵-per-frame error the
browser research warned about, reproduced one level down. `GLOSSARY.md`'s *Signal crossing* also lists
"bus transaction" among the words to avoid, which supports class 2 being out.

## 1. CPU ↔ Memory

| Signal | Dir | Class | Rate | Basis |
|---|---|---|---|---|
| `AS`, `LDS`, `UDS`, `R/W`, `FC0–FC2` | CPU → Memory | 2 request | up to 40 000/frame (2 × 10⁶/s) | one per bus cycle; 8 MHz ÷ ~4 cycles per access |
| `DTACK` | Memory → CPU | 2 completion | same as above | computed latency, not an event |
| `BERR` | Memory → CPU | 2 completion | ~0 | bus error; should never fire on valid TOS software |
| `BR`, `BG`, `BGACK` | Memory ↔ CPU | 1 event | 312 + up to 1 000/frame | one grant per stolen cycle for FDC and sound DMA (Blitter is now *inside* Memory) |
| `IPL0–IPL2` | Interrupts → CPU | 1 event | 1 + 313 + 160 + 61 per frame (idle→busy) | VBL, HBL/Timer B, MFP Timer A at 8 kHz, FDC sector-complete |
| `VMA` / `VPA` | Memory/CPU | 2 completion | per MFP access | 6800 peripheral cycle for the MFP |
| `RESET` | reset network → CPU | 1 event | 1 per boot | — |

## 2. CPU ↔ Video

| Signal | Dir | Class | Rate | Basis |
|---|---|---|---|---|
| `A1–A6`, `CS`, `LD`, `HD`, `R/W` | CPU → Video | 1 event | ~10/frame idle, up to 939/frame in a demo | Shifter register writes; a raster demo writes ~3 per line (313 × 3) |
| `MWE`, `MWD`, `MWK` | Video ← Memory (Super Hi-Res write path) | 1 event | ~0 at Level 1 | only Super Hi-Res writes |

## 3. Video ↔ Memory — *no crossings at all*

| Signal | Dir | Class | Rate | Basis |
|---|---|---|---|---|
| `MD0–MD15` | Memory → Video | 2 data / memory service | 6 260 words/frame (low-res), up to 25 040 (full overscan) | the fetch stream is Memory's internal schedule (owner's ruling) |
| `SREQ` / `SLOAD` | Shifter ↔ timing generator | **internal to Video** | 20/line = 6 260/frame | dissolved by the partition: both ends are in Video |

That is the partition's main payoff: the busiest "crossing" in the earlier arithmetic is not one.

## 4. Video → host (not a virtual chip)

| Signal | Dir | Class | Rate | Basis |
|---|---|---|---|---|
| `HSYNC` | Video → display stage | 1 event (host) | 313/frame = 15 650/s | once per line |
| `VSYNC` | Video → display stage | 1 event (host) | 1/frame = 50/s | once per frame |
| `DE` | Video → display stage | 1 event (host) | 313/frame | one per active line; gates the fetch slot |
| `BLANK` | Video → display stage | 1 event (host) | 313/frame | per-line blanking |
| `R0–R3`, `G0–G3`, `B0–B3` | Video → analog tail | data | continuous | 12-bit RGB, 4096 colours |
| `LATCH` | Video → palette latch | 1 event | up to 939/frame | palette reloads during raster effects |

## 5. Memory ↔ Disk (DMA controller and FDC)

| Signal | Dir | Class | Rate | Basis |
|---|---|---|---|---|
| `FCS` | Memory → DMA | 2 completion | tens/frame | DMA register select |
| `DCYC` | Memory → DMA | 2 completion | 312/frame while streaming | DMA cycle acknowledge |
| `RDY` | DMA → Memory | 1 event | ~60/frame | DMA requesting the bus |
| `HDRQ`, `HDCS`, `ACK` | Memory/DMA ↔ ACSI | 2 | 0 in v1 | no hard disk in scope |

## 6. Disk ↔ FDC

| Signal | Dir | Class | Rate | Basis |
|---|---|---|---|---|
| `FDCS` | DMA → FDC | 1 event | ~20/frame during a load | FDC register select (command/status) |
| `FDRQ` | FDC → DMA | 1 event | 625/frame during a read | one per byte at 250 kbit/s MFM, 31 250 B/s ÷ 50 |
| `STEP`, `DIRC`, `WG`, `WD`, `MO`, `RD`, `DDEN`, `TR00`, `WPRT`, `IP` | Disk → drive | 1 event | 0 (off-board, no drive model needed for the skeleton) | — |

## 7. Sound

| Signal | Dir | Class | Rate | Basis |
|---|---|---|---|---|
| `SD0–SD7` | Memory → Sound (sample bus) | 2 data / memory service | 1 000/frame at 50 kHz, 125–1 000 by rate | samples per frame are exact by ADR 0001 (125 / 250 / 500 / 1 000) |
| `SNDCS`, `SNDIR` | Memory/glue → Sound | 1 event | ~160–1 000/frame | PSG select and LMC1992 serial control, once per player tick or volume change |
| `DMA-active → MFP Timer A` | Memory → Timers | 1 event | 1 000/frame at 50 kHz, 160/frame at 8 kHz | the signal *is* Timer A's input; the samples it counts are memory service |

## 8. Timers, interrupts, controllers

| Signal | Dir | Class | Rate | Basis |
|---|---|---|---|---|
| `MFPCS` | Memory → Timers | 1 event | 160–1 000/frame | MFP register access per timer tick |
| `IACK` | CPU ↔ Timers | 1 event | 1 per acknowledged interrupt | follows `IPL` |
| `KHZ500` | Video/glue → Timers | **3 clock** | 500 000/s | not an event, however high the rate |
| `IOA0–IOA7` | Controllers ↔ Controllers (PSG ports) | 1 event | 50–200/s | keyboard/joystick scan; internal to the chip as partitioned |
| `ACC`, `PAD0X/PAD0Y/PAD1X/PAD1Y`, `BUTTON`, `PEN`, `PADRST` | host → Controllers | 1 event | event-driven, ~0 idle | input only moves when the user moves it |

## Totals

| Reading | Events/frame | Events/s |
|---|---|---|
| Class 1 only (events), idle GEM desktop | ≈ 500 | ≈ 25 000 |
| Class 1 only, floppy load | ≈ 1 500 | ≈ 75 000 |
| Class 1 only, raster demo with HBL | ≈ 3 000 | ≈ 150 000 |
| Classes 1 + 2 (completions counted too) | ≈ 40 000 | ≈ 2 × 10⁶ |

The top three lines are the ~10²–10³ band this design has been budgeting against; the last line is the
bus-cycle count, and counting it is the error the whole exercise exists to avoid.

## What stays uncertain

- **Which of `IPL` / `IACK` / `BR`/`BG` are truly separable events.** On hardware, `BGACK` follows
  `BR`/`BG` within a cycle or two; the model may legitimately collapse the three into one grant.
- **The bus-cycle rate is an assumption** (~4 CPU cycles per access). It is a class-2 figure, so it
  does not move the crossing total, but it does size Memory.
- **`KHZ500`'s destination is unresolved** in the redraw (which of `TAI`/`TBI` it reaches), and the MFP
  clocking in general is the least verified part of the inventory.
- **Keyboard path**: the inventory labels `U200` "MIDI" the way the parts list does, while on the ST the
  keyboard ACIA is the 6850 at `$FFFC00`; this needs resolving before Controllers can be specified.
