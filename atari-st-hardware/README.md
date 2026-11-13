# Atari ST / STE Hardware Wiki

> Comprehensive hardware documentation for building a cycle-accurate emulator of the Atari ST, STe, Mega ST, and Mega STe computers.

## Table of Contents

### Architecture Overview
- [System Overview](./architecture/01-system-overview.md) - Physical layout, models, and design philosophy
- [Motherboard and Electronic Components](./architecture/02-motherboard-and-electronic-components.md) - Analysis of ST motherboard board-level components
- [Processor Architecture](./cpu/01-processor-architecture.md) - MC68000/CPU details, pins, registers
- [The 68000, a compact exhaustive tutorial](./cpu/02-mc68000-tutorial.md) - Glossary first, then addressing modes, every instruction by family with what it does, flags, exceptions, interrupts, bus cycles, timing, with diagrams
- [Custom Silicon](./architecture/05-custom-silicon.md) - The four Atari-designed chips: Glue, MMU, DMA, and Shifter

### Components
- [Keyboard Controller: HD6301 (IKBD)](./components/02-ikbd-keyboard.md) - Hitachi HD6301V1 keyboard/mouse controller
- [Glue / MMU / DMA Chips](./components/03-glue-mmu-dma.md) - Atari custom chipset architecture
- [TV RF Modulator](./components/04-tv-rf-modulator.md) - ST/STe TV output variants
- [Video: GTia and Shifter](./components/video/01-gtia-shifter.md) - Shifter (VDC), GTia, and Super Shifter on STe
- [Blitter](./components/blitter/01-blitter.md) - Bit-Block Transfer Processor (BLiTTER)
- [Sound: YM2149 PSG](./components/sound/01-ym2149.md) - Yamaha YM2149 Programmable Sound Generator
- [Floppy Disk Controller: WD1772](./components/fdc/01-wd1772.md) - Western Digital WD1772A/FDC
- [MFP: MC68901](./components/mfp/01-mc68901.md) - Motorola MC68901 Multi-Function Peripheral
- [ACIA: MC6850](./components/iio/01-mc6850-acia.md) - MC6850 Asynchronous Communications Interface Adapter
- [RTC: MC146818A](./components/rtc/01-mc146818a.md) - Motorola MC146818A Real-Time Clock Plus RAM

### Power Supply
- [Power Supply Units](./power-supply/01-power-supply.md) - External bricks, internal SMPS, RF modulator details

### Memory and Bus
- [Memory Map](./memory/01-memory-map.md) - Complete 16MB address map for all ST models
- [Memory Controller](./memory/02-memory-controller.md) - DRAM management, refresh, bank interleaving
- [Bus Protocol](./bus/01-bus-protocol.md) - 68000 bus cycles, DMA arbitration, timing

### I/O and Ports
- [DIN13 Monitor Port](./pins/01-din13-monitor-port.md) - RGB sync, video, audio pinouts
- [DB25 Parallel Port](./pins/02-db25-parallel-port.md) - Centronics printer interface
- [DB25 RS232 Port](./pins/03-db25-rs232-port.md) - Modem serial port pinouts
- [DIN14 Floppy Port](./pins/04-din14-floppy-port.md) - Floppy interface pinouts
- [ACSI (DB19) Port](./pins/05-acsi-interface.md) - Atari hard disk bus (DCB19 connector)
- [DB9 Mouse/Joystick Port](./pins/06-db9-mouse-joystick-port.md) - Mouse and joystick interface
- [MIDI Port](./pins/07-midi-port.md) - DIN5 MIDI In/Out connectors
- [ROM Cartridge Port](./pins/08-rom-cartridge-port.md) - 40-pin expansion port

### STe Enhancements
- [Super Glue / GST MCU / SH2](./ste-enhancements/01-ste-chips.md) - Memory controller and address decode for STe
- [Super Hi-Res Display Mode](./ste-enhancements/02-super-hires.md) - 640x480 resolution mode
- [Stereo Sound DMA](./ste-enhancements/03-stereo-sound.md) - 8-bit stereo sample playback
- [New I/O Ports](./ste-enhancements/04-new-io.md) - Joystick/paddles/pen ports

### External Documents
- [download/](./docs/) - Manual PDFs referenced below

## External Documents (Downloaded)

| Title | Link | Source |
|-------|------|--------|
| Atari ST Internals (3rd Edition) | [PDF](docs/Atari-ST-Internals.pdf) | Abacus Software / DrCoolZic |
| Atari ST 68000 Programmer's Reference Guide | [PDF](docs/st_prog_guide_1.htm) | Atari / Info-Coach |
| Yamaha YM2149 Datasheet | [PDF](docs/ym2149.pdf) | Yamaha Corporation |
| MC68901 MFP Datasheet | [PDF](docs/MC68901.pdf) | Motorola / NFG Games |
| MC6850 ACIA Datasheet | [PDF](docs/MC6850.pdf) | Motorola |
| WD1772 FDC Specification | [PDF](docs/WD1772.pdf) | Western Digital / DrCoolZic |
| MC146818A RTC Datasheet | [PDF](docs/MC146818A.pdf) | Motorola |
| Yamaha BLiTTER User Manual | [PDF](docs/BLiTT_1-25-1990.pdf) | Atari |
| The Little Black Bit Book (Atari Compendium) | [PDF](docs/Bitbook2.pdf) | DrCoolZic |
| Atari Master Memory Map | [PDF](docs/Master-Memory-Map.pdf) | AtariMania |
| ST fd/hd programming guide | [PDF](docs/FD-HD_Programming.pdf) | DrCoolZic |

## ST Model Summary

| Model | CPU | RAM Base | RAM Max | Custom Chips | Notable Features |
|-------|-----|----------|---------|-------------|-----------------|
| 520ST | MC68000 @ 8 MHz | 512 KB | 1 MB | Glue, MMU, DMA, Shifter | Original ST |
| 1040ST | MC68000 @ 8 MHz | 1 MB | 2 MB | Glue, MMU, DMA, Shifter | Most popular ST |
| Mega ST | MC68000 @ 8 MHz | 1 MB | 2 MB | Glue, MMU, DMA, Shifter | Portable (SCSI port) |
| 520STE | MC68000 @ 8 MHz | 1 MB | 4 MB | GST MCU, GST Shifter | Color modes, stereo DAC |
| 1040STE | MC68000 @ 8 MHz | 4 MB | 4 MB | GST MCU, GST Shifter | Full-color ST |
| Mega STe | MC68000 @ 8 MHz | 4 MB | 4 MB | GST MCU, GST Shifter | Portable color ST |
| Mega STE | MC68000 @ 16 MHz | 2 MB | 16 MB | GST MCU, SH2, Shifter | 16 MHz, IDE support |

## Cross-Reference Index

- [All custom silicon](./architecture/05-custom-silicon.md)
- [All I/O pinouts](./pins/01-din13-monitor-port.md)
- [Full 16MB memory map](./memory/01-memory-map.md)
- [68901 MFP registers](./components/mfp/01-mc68901.md)
- [YM2149 registers](./components/sound/01-ym2149.md)
- [WD1772 commands](./components/fdc/01-wd1772.md)
- [Shifter video timing](./components/video/01-gtia-shifter.md)
- [STe memory controller](./ste-enhancements/01-ste-chips.md)
