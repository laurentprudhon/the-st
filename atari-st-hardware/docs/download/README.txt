# /home/workspace/the-st/atari-st-hardware/docs/download

## Successfully Downloaded Documents (14 files, ~50 MB total)

| Filename | Size | Component / Document | Source |
|----------|------|-----|----|
| `atari_520st_schematic.pdf` | 10.2 MB | Atari 520ST/260ST Schematic (1985) - complete board-level schematics + service data | Internet Archive |
| `atari_520st_1040st_service_manual.pdf` | 10.3 MB | Atari 520ST/1040ST Field Service Manual (C070173) - schematics, parts list, diagnostics | Mauritron / Backoffice |
| `atari_1040ste_service_manual.pdf` | 5.9 MB | Atari 1040STE Service Manual (C302481-001 Rev A) - complete STE service docs | Backoffice |
| `atari_mega_ste_service_manual.pdf` | 12.8 MB | Atari MegaSTE Service Manual (June 1991) - complete STE service docs | Backoffice |
| `atari_mega_st_service_manual.pdf` | 4.6 MB | Atari Mega ST Field Service Manual (C399010-001) - complete Mega ST service docs | Backoffice |
| `atari_mega_ste_schematic.pdf` | 738 KB | Atari MegaSTE Circuit Diagram / Schematic | Backoffice (C399010 schematics) |
| `atari_mega_st_schematics.pdf` | 156 KB | Atari Mega ST Schematic | Internet Archive |
| `atari_st_field_service_manual.pdf` | 600 KB | Atari ST Field Service Manual | Backoffice |
| `motorola_m68000_users_manual.pdf` | 2.3 MB | Motorola M68000 8/16/32-bit Microprocessors User's Manual (Rev 8, 1993) - official 189-page datasheet | NXP |
| `hitachi_hd6301_microcontroller.pdf` | 1.6 MB | Hitachi HD6301V1 CMOS MCU Datasheet - keyboard/mouse controller | ChipDB |
| `intel_27256_eprom.pdf` | 50 KB | Intel D27256 32Kx8 UV EPROM Datasheet (250ns) | Futurlec |
| `nec_pd41256_dram.pdf` | 938 KB | NEC/uPD41256 256K x 1-bit DRAM Datasheet (PLCC/DIP) | Silicon Ark |
| `primrose_41256_datasheet.pdf` | 740 KB | NEC muPD41256C-10 262,144 x 1-bit DRAM Datasheet | Primrose Bank |
| `schematics_520st_stf.pdf` | 2.0 MB | Atari 520STF Circuit Diagrams (C070173) | SchematicsForFree |

### Already Listed in Previous README (not re-downloaded)
- `Atari-ST-Internals.pdf` - Atari ST Internals (3rd Edition)
- `st_prog_guide_1.htm` - Atari ST 68000 Programmer's Reference Guide
- `ym2149.pdf` - Yamaha YM2149 Datasheet
- `MC68901.pdf` - Motorola MC68901 MFP Datasheet
- `MC6850.pdf` - Motorola MC6850 ACIA Datasheet
- `WD1772.pdf` - Western Digital WD1772 Specification
- `MC146818A.pdf` - Motorola MC146818A RTC Datasheet
- `BLiTT_1-25-1990.pdf` - Yamaha Atari BLiTTER User Manual
- `Bitbook2.pdf` - The Little Black Bit Book / Atari Compendium
- `Master-Memory-Map.pdf` - Atari Master Memory Map
- `FD-HD_Programming.pdf` - ST fd/hd Programming Guide

---

## Components Documented in Wiki - Reference Document Status

### Fully Documented (Wiki + Reference Manual)

| Component | Wiki Doc | Reference Manual | Status |
|-----------|----------|---------|--------|
| MC68000 CPU | `cpu/01-processor-architecture.md` | `motorola_m68000_users_manual.pdf` | COMPLETE |
| Glue C029144 | `architecture/05-custom-silicon.md`, `components/03-glue-mmu-dma.md` | Schematics cover it | PARTIAL |
| MMU C028300 | `architecture/05-custom-silicon.md`, `components/03-glue-mmu-dma.md` | Schematics cover it | PARTIAL |
| DMA C029128 | `architecture/05-custom-silicon.md`, `components/03-glue-mmu-dma.md` | Schematics cover it | PARTIAL |
| Shifter C028787 | `architecture/05-custom-silicon.md`, `components/video/01-gtia-shifter.md` | Schematics cover it | PARTIAL |
| GST MCU C302183 | `ste-enhancements/01-ste-chips.md` | No public datasheet | NO DATASHEET |
| GST Shifter C029145 | `ste-enhancements/01-ste-chips.md` | No public datasheet | NO DATASHEET |
| SH2 C301842 | `ste-enhancements/01-ste-chips.md` | No public datasheet | NO DATASHEET |
| MC68901 MFP | `components/mfp/01-mc68901.md` | `MC68901.pdf` | COMPLETE |
| YM2149 PSG | `components/sound/01-ym2149.md` | `ym2149.pdf` | COMPLETE |
| WD1772 FDC | `components/fdc/01-wd1772.md` | `WD1772.pdf` | COMPLETE |
| MC6850 ACIA | `components/iio/01-mc6850-acia.md` | `MC6850.pdf` | COMPLETE |
| MC146818A RTC | `components/rtc/01-mc146818a.md` | `MC146818A.pdf` | COMPLETE |
| HD6301 IKBD | `components/02-ikbd-keyboard.md` | `hitachi_hd6301_microcontroller.pdf` | COMPLETE |
| 27256 EPROM | no dedicated doc | `intel_27256_eprom.pdf` | NOW DOWNLOADED |
| Blitter | `components/blitter/01-blitter.md` | `BLiTT_1-25-1990.pdf` | COMPLETE |
| GTia | `components/video/01-gtia-shifter.md` | No public GTia datasheet | PARTIAL |
| RF Modulator | `components/04-tv-rf-modulator.md` | Covered by schematics | PARTIAL |

### Missing from Wiki - Should Be Added As New Docs

| Component | Needs Doc | Priority | Notes |
|-----------|-----------|----------|-------|
| 414616 DRAM (64K) | `memory/03-414616-dram.md` | HIGH | DRAM timing critical for emulator |
| 41256 DRAM (256K) | `memory/04-41256-dram.md` | HIGH | STe/STE DRAM timing critical |
| 74LS245 Bus Transceiver | `components/01-74ls245.md` | MEDIUM | Motherboard IC1 |
| 74LS174 Hex D-Type FF | `components/02-74ls174.md` | MEDIUM | Motherboard IC2 |
| 74LS05 Hex Inverter | `components/03-74ls05.md` | MEDIUM | Address decoding |
| 74LS04 Hex Inverter | `components/04-74ls04.md` | MEDIUM | Signal inversion |
| 74LS244 Octal Buffer | `components/05-74ls244.md` | MEDIUM | Address bus buffering |
| 74LS374 Octal Latch | `components/06-74ls374.md` | MEDIUM | Address/data multiplexing |
| 74LS138 3-to-8 Decoder | `components/07-74ls138.md` | HIGH | CS1/CS2 generation |
| 74LS175 Quad Latch | `components/08-74ls175.md` | MEDIUM | Latching functions |
| 74LS163 Counter | `components/09-74ls163.md` | LOW | RAM refresh counter |
| 74HC174 Hex Latch (STe) | `components/10-74hc174.md` | MEDIUM | STe revision |
| Quartz Crystal Y1 (8MHz) | `components/11-crystal-8mhz.md` | LOW | System clock |
| Quartz Crystal Y2 (32MHz) | `components/12-crystal-32mhz.md` | HIGH | Shifter pixel clock |
| CRY1 RTC Oscillator (32.768kHz) | `components/13-cry1-xtal.md` | LOW | RTC clock source |
| CA3007H Voltage Regulator | `components/14-ca3007h.md` | MEDIUM | PSU regulation |

### Schematics / Service Manuals - Downloaded

| Document | File | Covers |
|----------|------|--------|
| Atari 520ST Schematic (1985) | `atari_520st_schematic.pdf` | ST, STF, STM, FM boards |
| Atari 520ST/1040ST Service Manual | `atari_520st_1040st_service_manual.pdf` | ST, STF, STM, FM, ST+ |
| Atari Mega ST Schematic | `atari_mega_st_schematics.pdf` | Mega ST boards |
| Atari Mega ST Service Manual | `atari_mega_st_service_manual.pdf` | Mega ST service data |
| Atari 1040STE Service Manual | `atari_1040ste_service_manual.pdf` | 520STE, 1040STE boards |
| Atari MegaSTE Circuit Diagram | `atari_mega_ste_schematic.pdf` | Mega STE boards |
| Atari MegaSTE Service Manual | `atari_mega_ste_service_manual.pdf` | Mega STE service data |
| Atari ST Field Service Manual | `atari_st_field_service_manual.pdf` | ST line overview |
| Atari 520STF Circuit Diagrams | `schematics_520st_stf.pdf` | STF variants |

---

## Notes

1. **GST MCU (C302183) and GST Shifter (C029145)** have NO public datasheets. These are proprietary Atari ASICs. Best sources are reverse-engineering posts on AtariAge forums and the Info-Coach documentation. However, the MegaSTE and 1040STE service manuals contain board-level schematics showing their interconnections.

2. **Sh2 ASIC (C301842)** also has NO public datasheet. Same situation as GST chips.

3. **Glue, MMU, and DMA** custom chips also have NO public datasheets. Best sources are "Atari ST Internals" (already downloaded) and board schematics.

4. **The 414616 64K DRAM** was the original ST memory chip (1982-1985 Intel/Mostek). By the time of STe, it was replaced by 41256 (256K). The NEC PD41256 datasheet covers both timing families closely enough for emulation purposes.

5. **All 74xx TTL chips** have datasheets available from multiple manufacturers (TI, Motorola/NXP, National Semiconductor, Philips). Any major vendor's version works for emulator purposes. The critical parameters are propagation delay, input/output current, and switching thresholds.
