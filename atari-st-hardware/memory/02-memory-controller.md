# Memory Controller (DRAM and Memory Management)

> How the Atari ST manages DRAM: refresh, page mapping, scrolling, and interleaving.

## Overview

The Atari ST uses **DRAM** (Dynamic RAM) instead of static RAM. This means the memory must be periodically refreshed to retain data. The MMU chip (in ST/STe) and the GST MCU (in STe/Mega STE) handle all DRAM management.

## DRAM Chips

### ST 520ST (512 KB)

| Chip | Type | Package | Quantity | Total |
|------|--|--|----|---|-|
| 41256 | DRAM | DIP28 | 8 | 8 × 256K-bit = 256 KB × 2 banks = 512 KB |

Two banks: odd and even address banks, each with 4 × 41256 chips.

### ST 1040ST and Mega ST (1 MB)

| Chip | Type | Package | Quantity | Total |
|------|--|--|---|----|
| 414616 | DRAM | DIP28 | 16 | 4 × 64K × 8 = 256 KB per bank |

Two banks (odd/even), each with 8 × 414616 chips.

### STe / Mega STe (1 MB / 4 MB)

| Chip | Type | Package | Quantity | Total |
|------|- ----|--|------|--|
| 414616 | 64K × 8 DRAM | DIP28 | 32 | 4 MB total |

### Mega STE (2 MB / up to 16 MB)

| Chip | Type | Package | Quantity | Total |
|------|--|---|--|-----|
| 414616 or 416416 | 64K/256K DRAM | PLCC32 | 16-32 | 2-16 MB |

## DRAM Refresh

> **This section describes a 1 Mbit part, not the part fitted to this machine.** A 1040STE is populated
> with 16 × `C0101743-002`, 256K×1 (41256-class: **512 rows × 512 columns**, per
> `04-dram-chip-41256-414616.md` §3 and `design/board-inventory.md` §2.3). With 512 rows the refresh
> period is **7.8 µs per row**, not 3.9 µs, i.e. ≈8.2 slots per 512-clock PAL line at one slot each.
> The figures below are kept for the 1 Mbit case (Mega STE / TT). See `research/memory-bus-slots.md` §4.

The Glue chip (ST/STe) and GST MCU (STe) autonomously refresh DRAM without CPU involvement:

### Refresh Timing
- Each DRAM row must be refreshed every **4 ms** (standard DRAM timing)
- 414616 has 1024 rows (10-bit row address)
- Refresh period per row: **3.9 us** (4 ms / 1024)

### Refresh Cycle

1. Glue generates a refresh request each 3.9 us (approximately)
2. MMU takes over the bus for one DRAM refresh cycle
3. The refresh cycle reads and rewrites each row
4. After all 1024 rows refreshed, start over
5. Refresh takes ~830 us (1024 rows × 830 ns each)
6. Total cycle: ~4 ms

### Auto-Refresh Integration

On the ST:
- The MMU uses the **RAS cycle** for refresh (RAS-only refresh)
- Each RAS pulse refreshes one row
- The Glue provides RAS0 and RAS1 for the two DRAM banks
- The MMU increments the row address internally

## Memory Bank Interleaving

The ST uses **two interleaved RAM banks** for maximum bandwidth:

### Bank Assignment

| Address bit 23 | Bank |
|----|------|
| 0 | Odd bank (data pins D1-D3, D5-D7, D9-D11, D13-D15) |
| 1 | Even bank (data pins D0, D4, D8, D12) |

Wait, this is wrong. The interleaving is by odd/even data pins, not address bits. Let me correct:

### Odd/Even Banks

| Bank | Data pins | Function |
|------|--| ----|
| Odd | D1, D3, D5, D7, D9, D11, D13, D15 | Odd byte addresses |
| Even | D0, D2, D4, D6, D8, D10, D12, D14 | Even byte addresses |

During a byte access:
- If byte address is even → Even bank active, LDS strobe active
- If byte address is odd → Odd bank active, UDS strobe active
- If word access → Both banks active simultaneously (68000 native word cycle)

## Video RAM Mapping

The ST video RAM is mapped into system memory at **$A0000-$A7FFF** (32 KB):

### Memory Page Mapping

The MMU maps physical RAM pages into logical addresses:

| Page | Physical RAM Address | Logical Address |
|------|-----|--|
| Page 0 | $000000-$07FFFF | $000000-$07FFFF |
| Page 1 | $080000-$0FFFFF | $080000-$0FFFFF |
| Page 2 | $100000-$17FFFF | $100000-$17FFFF |
| ... | ... | ... |
| Page 160 | $A0000-$A7FFF | Video RAM area |

MMU page registers control this mapping.

### Video Memory Layout

The 32 KB video RAM at $A0000-$A7FFF is used differently depending on the display mode:

| Mode | Bytes used | Memory layout |
|-- |-- |-- |
| LORAM | 32 KB | All 32 KB active (320 × 200 × 2 pixels/byte) |
| HIRE | 16 KB | $A0000-$A3FFF used |

## MMU Scroll Registers

### Scroll Mechanism

The MMU has 8 scroll registers that add to addresses passing through the MMU:

```
Effective Address = Raw Address + Scroll Value
```

Each scroll register holds **12 bits** (0-4095), providing sub-page scrolling capability:

| Register | Address (MMU) | Bits Used | Scroll Range |
|--|----|--|-|
| Scroll 0 | $FFE000 | 12 | 0-4095 |
| Scroll 1 | $FFE002 | 12 | 0-4095 |
| Scroll 2-7 | $FFE004-$FFE016 | 12 each | 0-4095 |

### Scroll Digits

The scroll register is conceptually split into "digits":
- Each digit holds 12 bits (0-4095 in decimal)
- Digits are combined to form a 96-bit scroll value
- The scroll shifts the video memory pointer

### Scroll Use Cases

| Mode | Scroll Purpose | Register Used |
|------|----|------|
| LORAM | Horizontal scroll | Scroll 0-2 (36 bits total) |
| MORAM | Horizontal scroll | Scroll 0-1 (24 bits total) |
| HIRE | Vertical scroll | Scroll 0-2 (36 bits total) |

For LORAM horizontal scroll:
- Scroll 0 provides sub-byte horizontal offset (bits 0-11 of scroll = 2^12 = 4096 pixels of scroll per byte position)
- Scroll 1 provides page-level scroll (adds to page number)

For HIRE vertical scroll:
- Scroll 0 provides row scroll within page (bits 0-7 = vertical scroll in 256-line increments)
- Scroll 1 provides page-level vertical scroll

### Scrolling in HIRE Mode

For 640×400 HIRE mode:
- Each visible line = 80 bytes (640 pixels / 8 pixels per byte)
- 32 KB / 80 bytes per line = 400 lines
- Scroll registers move the start of the visible area through the 32 KB buffer
- Vertical scroll offset: scroll_0 × 80 bytes per line

## STe Memory Controller (GST MCU)

The STe GST MCU adds:

### Additional Memory

| Feature | ST | STe |
|------|--|-- |
| Max RAM | 2 MB | 4 MB (fixed on 1040STE) |
| Memory banking | Fixed page mapping | Programmable page mapping |
| Bank switching | Via firmware | Via GST MCU registers |
| Interleave | Odd/even | Odd/even + page interleaving |

### GST MCU Page Mapping

The GST MCU provides programmable page mapping via special registers:

| Register | Address | Description |
|--|--|---- |
| Page map 0 | GST MCU register | Maps first RAM page |
| Page map 1-31 | GST MCU register | Maps remaining pages |

### Memory Access Timing (68000)

| Access Type | Cycles | Notes |
|---------|--|--|
| RAM read (word) | 1 bus cycle | Native 68000 cycle |
| RAM write (word) | 1 bus cycle | Native 68000 cycle |
| RAM read (byte) | 1 bus cycle | LDS or UDS strobe only |
| DRAM refresh | 1 bus cycle | Merged with MMU read cycle |
| Bus error | - | Access beyond RAM boundary |

## Memory Testing on Boot

When the ST boots, it tests available RAM:

1. Write known pattern to each byte
2. Read back and verify
3. If mismatch → reduce RAM size
4. Store valid RAM size in `memvalid` ($4F2) and `memval2` ($43A), `memval3` ($51A)
5. If all valid → warm boot; if not → cold boot

### RAM Check Algorithm

```
For each memory page:
  Write $55 to every byte
  Read back and verify
  Write $AA to every byte
  Read back and verify
  
  If both patterns pass:
    Page is valid
  Else:
    Page is invalid
    Mark page as unusable
```

## References

- [Atari ST Internals, ch. 1.2 - MMU (PDF)](https://www.atarimania.com/documents/Atari-ST-Internals.pdf)
- [DrCoolZic - Atari ST Bus Doc - Memory](http://info-coach.fr/atari/ressources/doc/Atari%20ST%20bus%20doc.pdf)
- [Information about the MMU](https://raw.githubusercontent.com/sporniket/kicad-symbols-generated/main/reference-materials/atari-16-32/st-glue-mmu-dma-shifter--Atari-ST-Internals.pdf)
