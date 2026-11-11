# Super Hi-Res Display Mode

> **Correction — 2026-11-09** (ticket #27, `research/ste-super-hires.md`). **This page describes a TT
> mode, not an STE one.** The addresses it cites (`$FFC000`/`$FFC200`) are the TT Shifter's register
> space — the STE's Shifter answers at `$FF82xx` — and Atari's own *STE Developer Addendum*
> (5/25/1989) lists the STE's new graphics features with **no fourth resolution**: 4096 colours,
> hardware horizontal/vertical scrolling, genlock. No vendored emulator (Hatari, Steem SSE, NeoST)
> models a Super Hi-Res mode for the STE. Kept as an audit trail; **do not cite it for this machine.**
> `disputed`.

> The STe adds a Super Hi-Res mode: 640x480 resolution with 16 colors.

## Super Hi-Res Mode

The STe GST Shifter (C029145) introduces a new video mode:

| Parameter | Value |
|--|--|
| Resolution | 640 × 480 pixels |
| Colors | 16 of 512 |
| Bits per pixel | 2 (4 pixels per byte) |
| Memory width per line | 80 bytes (640 pixels / 8 pixels/byte) |
| Total lines | 480 visible + 80 vertical blank |
| Total memory | 38.4 KB (40 lines × 80 bytes + 80 lines blank) |
| Memory location | $A0000-$A7FFF (same as LORAM) |

## How Super Hi-Res Works

Super Hi-Res uses the same memory region as LORAM ($A0000-$A7FFF = 32 KB) but with a different display timing:

### LORAM vs Super Hi-Res Timing

| Mode | Visible lines | Line period | Pixel clock | Total lines |
|------|----| ----|--- |---------|
| LORAM (NTSC) | 200 | 63.5 us | 32.0 MHz | 262 |
| Super Hi-Res (NTSC) | 480 | 40.0 us | 32.0 MHz | 525 |

The Super Hi-Res mode works by using a faster horizontal scan rate. The GST Shifter generates different timing from the same 32 MHz pixel clock.

### Pixel Storage in Super Hi-Res

Each byte stores 4 pixels (2 bits per pixel), same as LORAM but with a different line size:

| Address range | Pixels | Rows | Total |
|-----|-|------|--|
| $A0000-$A1FFF | 80 pixels × 256 rows | | 20 KB |
| $A2000-$A7FFF | 80 pixels × 256 rows | | 20 KB |
| **Total** | **640 × 480 pixels** | **480 visible lines** | |

Wait: 32 KB / 80 bytes per line = 400 lines visible. For 480 visible lines, the STe uses:
- 480 visible lines
- 80 lines vertical blanking
- 32 KB total (400 lines × 80 bytes per line = 32 KB)

Actually the exact memory map depends on the mode register setting.

## Register Control for Super Hi-Res

The GST Shifter control registers at $FFC000-$FFC0FF control Super Hi-Res:

| Address | Bits | Function |
|-- |--|-- |
| $FFC200 | bit 7 | Super Hi-Res enable |
| $FFC200 | bit 5 | Vertical timing (0=NTSC, 1=PAL) |
| $FFC200 | bit 4 | Color mode (0=mono, 1=color) |
| $FFC200 | bit 3 | 16/4 color select |
| $FFC200 | bit 2 | Palette bit plane |
| $FFC200 | bit 1 | Horizontal resolution |
| $FFC200 | bit 0 | Display enable |

## Display Mode Summary

### All STe Modes

| Mode | Resolution | Colors | Palette | Memory |
|------|----------|--------|-------|---|
| LORAM | 320×200 | 16 of 512 | 6-bit | 32 KB |
| STe Med | 640×200 | 16 of 512 | 6-bit | 32 KB |
| STe Hi-Color | 640×200 | 64 of 512 | 6-bit | 32 KB |
| STe Hi-Res | 640×400 | 2 of 512 | 6-bit | 32 KB |
| **Super Hi-Res** | **640×480** | **16 of 512** | **6-bit** | **32 KB** |
| STe Mono | 640×400 | 2 of 512 (mono) | 6-bit | 16 KB |

### STe Mode Register Values

| Config | V | H | 16/4 | Mono | Mode |
|-------|--|--|--|--|-- |
| 0 | 0 | 0 | 1 | 0 | LORAM (320×200×16) |
| 1 | 0 | 1 | 0 | 0 | STe Med (640×200×4) |
| 2 | 0 | 1 | 1 | 0 | STe Hi-Res (640×400×1) |
| 4 | 0 | 1 | 1 | 0 | Hi-Color (640×200×64) |
| 6 | 1 | 1 | 1 | 0 | Super Hi-Res (640×480×16) |

### Sync Timing

| Mode | HSync | VSync |
|------|----|----- |
| LORAM | 15,734 Hz (NTSC) | 60 Hz (NTSC) |
| Super Hi-Res | 31,468 Hz (NTSC) | 60 Hz, 480 lines (NTSC) |
| STe Med | Same as LORAM | Same as LORAM |

## Palette Registers (STe)

The STe palette has 32 entries, each 6 bits per color component:

| Register | Address | Range |
|-------|---|---- |--|
| Palette B | $FFC040-$FFC05F | Blue (6 bits) |
| Palette G | $FFC140-$FFC15F | Green (6 bits) |
| Palette R | $FFC240-$FFC25F | Red (6 bits) |

Each color entry:
```
Color n:
  Blue  = $FFC040 + n  (6 bits: bits 0-5)
  Green = $FFC140 + n  (6 bits: bits 0-5)
  Red   = $FFC240 + n  (6 bits: bits 0-5)
```

Example color $n$:
```
Blue = byte n at $FFC040
Green = byte n at $FFC140
Red = byte n at $FFC240
```

## References

- [Info-Coach - STE Hardware](https://info-coach.fr/atari/hardware/STE-HW.php)
- [Atari ST Internals - blitter section (PDF)](https://www.atarimania.com/documents/Atari-ST-Internals.pdf)
- [Atari ST new video modes - troed.se](https://blog.troed.se/projects/atari-st-new-video-modes/)
- [32768 Color Video Shifter - AtariAge](https://forums.atariage.com/topic/248745-32768-color-video-shifter-for-atari-st-computers)
