# Blitter

> **Caution — 2026-11-28.** The body of this page is **not a source**, and its first three claims are wrong for
> the 1040STE this repository models. The blitter here is a **discrete 68-pin PLCC, `U101 C101643`**, addressed
> at `$FF8A00`–`$FF8A3D` — not a block inside a "GST Shifter (C029145)" at `$FFE400`. `C029145` is the TT's
> Shifter, the Mega ST of 1987 had a `C101643` a year before the Mega STE, and on the later `CA4003290` STE
> boards the function moved *into* the GSTMCU (`U400`), which is the only sense in which "combined" is true.
> The register table below is a different part's. For this machine read `design/subsystems-spec.md` §1/§3 and
> `research/blitter-implementation.md`, which is sourced from Atari's own *BLiTTER* manual (25 January 1990)
> and the recovered designs. The page is left as it stands so the correction stays visible; do not cite it.

> The BLiTTER (Bit Block Transfer Processor) is a custom chip in the STe and Mega STE that accelerates bitmap operations. The original ST does not have a dedicated blitter chip.

## Overview

The Atari ST **original** (ST, STe, MST) does not have a dedicated blitter chip - blitting was done in software by the CPU. The **Mega STE** introduced the **GST Shifter (C029145)** which contains a built-in blitter. The **Mega STE** is the only ST family model with hardware blitter support.

## GST Blitter (Mega STE only)

The Mega STE's GST Shifter (C029145) integrates the following:
- Shifter (video controller)
- **Blitter (BITBLT engine)**
- MMU functions
- Sound DMA controller

## Blitter Registers

The blitter has 11 registers mapped at `$FFE400-$FFE41F` (in the MMU address space):

| Register | Offset | Size | RW | Description |
|------|-|------|-|-|--|
| Blitter Control | $FFE400 | 16 bits | RW | Control/status register |
| Source Address | $FFE402 | 16 bits | RW | Source address (16-bit) |
| Source Byte | $FFE404 | 8 bits | RW | Source byte count low |
| Source Word Count | $FFE405 | 8 bits | RW | Source word count high |
| Destination Address | $FFE406 | 16 bits | RW | Destination address (16-bit) |
| Destination Byte | $FFE408 | 8 bits | RW | Dest byte count low |
| Dest Word Count | $FFE409 | 8 bits | RW | Dest word count high |
| Skew Register | $FFE40A | 16 bits | RW | Bit skew for source/dest |

### Blitter Control Register (Bit Map)

| Bit | Name | Description |
|----|--|---|
| 0 | S | Source bitplane (0 = 1 bitplane, 1 = 2 bitplanes) |
| 1 | D | Destination bitplanes (0 = mono, 1 = 2 bitplanes) |
| 2 | W | Word-aligned (0 = byte, 1 = word) |
| 3 | R | Repeat (0 = single, 1 = repeat) |
| 4 | H | Halftone (0 = normal, 1 = halftone mode) |
| 5 | M | Mask (0 = no mask, 1 = use mask) |
| 6 | B | Byte-swapped (0 = big-endian, 1 = little-endian) |
| 7 | L | Line (0 = single line, 1 = multi-line) |
| 8 | I | Input (0 = memory, 1 = I/O) |
| 9 | O | Output (0 = memory, 1 = I/O) |
| 10 | C | Copy (0 = logical, 1 = copy) |
| 11 | K | Invert (0 = normal, 1 = invert result) |
| 12 | T | Two-plane (0 = 1 bitplane, 1 = 2 bitplanes source) |
| 13 | 0 | Reserved |
| 14 | 0 | Reserved |
| 15 | BSY | Blitter busy (read-only, set while blitter is operating, cleared when done) |

### Logic Operations (Halftone mode)

When the Halftone bit (bit 4) is set, the blitter performs halftone/dither patterns:

| Skew value | Operation |
|------|-----|
| 0 | No skew |
| 1 | Skew right by 1 bit |
| 2 | Skew right by pattern bytes per line |
| 3 | Halftone dither with register as pattern |

## How to Use the Blitter

### Step-by-step Blit (Bit Block Transfer):

1. Write the control register (`$FFE400`) to configure the operation
2. Write source and destination addresses (`$FFE402`, `$FFE406`)
3. Write the source byte count (`$FFE404`)
4. Write the destination byte count (`$FFE408`)
5. Write the skew register (`$FFE40A`)
6. Write to the source address register again to **start** the transfer
7. Wait for the Busy bit (bit 15) of the control register to clear
8. Read and clear the interrupt pending register

### Timing

- The blitter operates at system clock speed
- One blitter cycle transfers one bit per clock (1 bit at 16 MHz)
- Block transfer speed depends on the width and number of planes

## References

- [BLiTTER User Manual PDF](docs/BLitter_1-25-1990.pdf) - Atari blitter documentation
- [Atari ST Internals, Ch. 1.1.6 - Blitter (PDF)](https://www.atarimania.com/documents/Atari-ST-Internals.pdf)
- [AtariForumWiki - Blitter manual](https://temlib.org/AtariForumWiki/index.php/Blitter_manual)
- [Atari STE FAQ - Blitter (GitHub)](https://github.com/Number0000009/atari-wiki/blob/master/Atari%20STE%20FAQ%20compiled%20by%20The%20Paranoid%20Paradox.txt)
