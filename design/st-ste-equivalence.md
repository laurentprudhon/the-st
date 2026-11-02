# ST → STE equivalence, and the composite naming scheme

The STE did not invent a machine: it merged the ST's **GLUE** and **MMU** into one **GSTMCU**, and
extended the **Shifter** in place. This document records that continuity so the model can name a pin,
register or state machine by *both* what it is in our partition and where it came from.

Source for the merging: the ST Magazine 44 (1990) STE article, the 520/1040STE Field Service Manual
(§2.5, §2.9), and `design/board-inventory.md` (pin tables, with the 1989 factory drawing's original
`X`-prefixed net names).

## 1. The naming scheme

```
virtual_subsystem . ste_chip . st_chip_when_integrated . pin_or_register
```

- `virtual_subsystem` — one of the eight of `design/subsystems-spec.md`: `cpu`, `memory`, `video`,
  `sound`, `disk`, `timers`, `controllers`, `blitter`.
- `ste_chip` — the physical part as it exists on this board: `gstmcu`, `gstshifter`, `dma`, `wd1772`,
  `mfp`, `ym2149`, `acia`, `blitter`.
- `st_chip_when_integrated` — the ST-era chip whose function this one absorbed, or `none` where the
  part is new: `mmu`, `glue`, `shifter`, `none`.
- `pin_or_register` — the name as printed on the 1989 drawing (pins, `X` = active low) or the address
  (`FF8A00`) for registers.

**Why both halves of the path are needed.** The partition is a *modelling* boundary; the chip path is a
*hardware* fact. They disagree in three places on purpose (§5), and a name that records only one of
them would lose the disagreement.

Examples:

| Name | Reads as |
|---|---|
| `memory.gstmcu.mmu.MAD0` | DRAM address, on the GSTMCU, inherited from the ST's MMU |
| `memory.gstmcu.glue.XROM4` | ROM chip select, GSTMCU, from the ST's GLUE |
| `video.gstshifter.shifter.R0` | red bit 0, GST Shifter, the ST's Shifter line extended |
| `disk.gstmcu.mmu.FF8609` | disk DMA address register — owned by **Disk**, physically in the GSTMCU (§5.1) |
| `sound.gstshifter.shifter.SREQ` | sound DMA request — pin on the **Shifter**, service owned by Memory (§5.2) |
| `timers.mfp.none.IPL2` | interrupt priority 2 — the MFP is unchanged from the ST |

## 2. Chip equivalence

| ST chip (original ST / STF / Mega ST) | STE | Kind of change |
|---|---|---|
| **MMU** — DRAM control, video address counter, refresh, disk-DMA address tracking | **GSTMCU** (`U400`), MMU half | merged; gains sound-DMA counters, line width, verified by the recovered design ST-4081 |
| **GLUE** — decode, chip selects, `BERR`, sync generation, interrupt routing, bus arbitration | **GSTMCU** (`U400`), GLUE half | merged; gains pad/pen ADC and the enhanced joystick ports |
| **Shifter** — bitplane shift, palette, `VIDEOMOD` | **GST Shifter** (`U401`) | extended in place: 4096 colours, `HSCROLL`, sound-DMA shifting, Microwire |
| **DMA controller** (`U300`) | same part | unchanged in role; the STE's sound DMA is *not* here (its counters are in the GSTMCU) |
| **MFP 68901**, **YM2149**, **WD1772**, **6850 ACIA** | same parts | unchanged |
| — | **Blitter** (`U101`) | new on STE, own bus master |
| — | **LMC1992** | new on STE, driven over Microwire from the Shifter |

## 3. Pin equivalence

### MMU → GSTMCU.MMU

| ST GLUE/MMU pin | STE pin | Note |
|---|---|---|
| `MAD0–MAD9` | `MAD0–MAD9` | kept |
| `RAS0`, `RAS1`, `CAS0L/H`, `CAS1L/H`, `WE` | same names | kept |
| `DTACK`, `BERR` | `XDTACK`, `XBERR` | renamed with the `X` active-low prefix |
| `RAM`, `DEV`, `DMA` (MMU/GLUE edge signals) | **internal to the GSTMCU** | the article: *"`*DMA`, `*RAM` and `*DEV` are not used outside of the GST MCU, but are on the STF, sent by the GLUE for the MMU"* |
| `VBASE`/`VCOUNT` state | same, plus `FF820F` line width | STE adds fine line width |

### GLUE → GSTMCU.GLUE

| ST GLUE pin | STE pin | Note |
|---|---|---|
| `ROM0–ROM4` | `XROM0–XROM6` | extended to seven select lines |
| `FCS`, `MFPCS`, `SNDCS`, `6850CS` | `XFCS`, `XMFECS`, `SNDCS`, `N6850` | same fan-out, renamed |
| `IPL1`, `IPL2` (two outputs) | `XIPL0`, `XIPL1`, `XIPL2` (three) | STE routes three levels |
| `DE`, `BLANK`, `HSYNC`, `VSYNC` | `DE`, `BLANK`, `HSYNC`, `VSYNC` | kept — sync stays a GLUE/GSTMCU function, not a Shifter one |
| `BGI`, `BGO`, `BR`, `BGACK` | `XBR`, `XBGK` (+ `BGO`) | BGO kept for Mega ST bus connectors, unused on STE |
| 8 MHz in; 2 MHz and 500 kHz out | `CLK8`, `CLK4`, `CLK2`, `KHZ500` | same divisions, more taps |
| — | `XPAD0X/Y`, `XPAD1X/Y`, `XPEN`, `XPENR` | new: ADC and pen logic |

### Shifter → GST Shifter

| ST Shifter pin | STE pin | Note |
|---|---|---|
| `R0–R2`, `G0–G2`, `B0–B2` | `R0–R3`, `G0–G3`, `B0–B3` | one more bit per channel — 512 → 4096 colours |
| `DE`, `BLANK` in | same | kept |
| `LOAD` | `LOAD` (video) + `SREQ`/`*SLOAD` (sound) | STE adds a second handshake for DMA sound |
| `A1–A5`, `CS`, `R/W` | `A1–A6`, `CS`, `LD`, `HD`, `R/W` | a sixth address bit for `HSCROLL` and the sound registers |
| — | `SD0–SD7` out, `SCLK`, `FCLK`, `MWSI/MWSO/MWCLK` | new: sample bus to the DACs and Microwire to the LMC1992 |

## 4. Register equivalence

| Address | ST | STE | Owner in our model |
|---|---|---|---|
| `FF8001` | MMU configuration | same | `memory.gstmcu.mmu.FF8001` |
| `FF8201/03/05/07/09` | video base / counter | same | `memory.gstmcu.mmu.FF8201` … |
| `FF820A` | GLUE sync register | same | `memory.gstmcu.glue.FF820A` |
| `FF820F` | — | line width | `memory.gstmcu.mmu.FF820F` |
| `FF8240–825F` | palette, 512 colours | palette, 4096 colours | `video.gstshifter.shifter.FF8240` |
| `FF8260` | `VIDEOMOD` in GLUE *and* Shifter | same duplication, plus `FF8261` shifter-only | `video.gstshifter.shifter.FF8260` **and** `memory.gstmcu.glue.FF8260` |
| `FF8264/65` | — | `HSCROLL` | `video.gstshifter.shifter.FF8264` |
| `FF8900–8913`, `FF8921` | — | sound DMA control, frame pointers, mode | `memory.gstmcu.mmu.FF8900` |
| `FF8922/24` | — | Microwire data/mask | `video.gstshifter.shifter.FF8922` |
| `FF8604/06/09/0B/0D` | FDC + DMA address (via MMU counters) | same registers, disk only | `disk.gstmcu.mmu.FF8609` (§5.1) |
| `FF8A00–8A3E` | — | Blitter | `blitter.blitter.none.FF8A00` |
| `FF9200/02`, `FF9211–9217`, `FF9220/22` | — | enhanced ports, analog pads, pen | `controllers.gstmcu.glue.FF9200` |

## 5. Where the partition and the hardware deliberately disagree

1. **Disk DMA registers are physically in the GSTMCU but owned by Disk.** The owner's rule: a temporary
   burst belongs to the chip that produces it. The name `disk.gstmcu.mmu.FF8609` records both facts, and
   is the reason the scheme carries the chip path at all.
2. **Sound DMA is split**: the counters are GSTMCU registers (`memory.gstmcu.mmu.FF8900`), while the
   handshake pins (`SREQ`, `*SLOAD`) and the byte split (`SD0–SD7`) are on the Shifter
   (`sound.gstshifter.shifter.SREQ`). One service, two chips — exactly what `GLOSSARY.md`'s *Memory
   service* means.
3. **The Microwire controller is in the Shifter but drives a Sound chip.** `video.gstshifter.shifter.FF8922`
   and `sound.ym2149.none.*` are on opposite sides of a chip boundary that the hardware draws that way.

## 6. State-machine equivalence

| Function | ST | STE | Note |
|---|---|---|---|
| DRAM cycle generation | MMU | GSTMCU | phase counter `TIME0–TIME7` → `ADDRSEL` (`ST4081S` sheet 47) |
| Refresh | MMU | GSTMCU | its own counter block, "DRAM REFRESH COUNTER" sheet |
| Video address counter | MMU | GSTMCU | same `VBASE`/`VCOUNT`, plus line width |
| Disk DMA address tracking | MMU | GSTMCU | same registers |
| Sound DMA counter | — | GSTMCU | new |
| Sync generation | GLUE | GSTMCU | same signals, one chip fewer |
| Bus arbitration | GLUE | GSTMCU (relay only; Blitter bypasses) | unchanged in principle |
| Bitplane shift | Shifter | GST Shifter | extended: 4 planes at 4096 colours, scrolling |
| Sample fetch and byte split | — | GST Shifter + GSTMCU | new handshake `SREQ`/`*SLOAD` |

## 7. Open

- **Which physical chip is `U200`/`U201`** — resolved as U201 = keyboard (`research/schematic-evidence.md`
  §4); naming uses `acia` for both.
- **Exact ST-era pin names** for the original MMU/GLUE symbols are quoted here from secondary
  descriptions (ST Magazine, service manual), not from an ST schematic — this repo has no ST (non-E)
  drawing. Any pin name marked kept/renamed above should be confirmed against one before being treated
  as authoritative.
- `FF8263` (read in Hatari's scroll path) has no confirmed function; not named here.
