# DIN14 Floppy Port

> The DIN14 connector for 3.5" floppy disk drives (SF354/SF314).

## Connector

- **DIN14S** (14-pin DIN socket, female)

## Pinout

| Pin | Function | Direction | Notes |
|-----|- --|---|------|
| 1 | Read Data | Input to FDC | TTL active low, 1K pull-up |
| 2 | Select Side 0 | Output from ST | TTL active high (high = side 0) |
| 3 | Logic Ground | - | Ground reference |
| 4 | Index Pulse | Input to FDC | TTL active low, 1K pull-up |
| 5 | Select Drive 0 | Output from ST | TTL active low |
| 6 | Select Drive 1 | Output from ST | TTL active low |
| 7 | Logic Ground | - | Ground reference |
| 8 | Motor On | Output from ST | TTL active low (inverted) |
| 9 | Direction In | Output from ST | TTL (1 = outward, step) |
| 10 | Step | Output from ST | TTL (active pulse) |
| 11 | Write Data | Output to drive | TTL |
| 12 | Write Gate | Output to drive | TTL |
| 13 | Track 00 | Input from drive | TTL active low, 1K pull-up |
| 14 | Write Protect | Input from drive | TTL active low, 1K pull-up |

## Drive Selection

The ST can control up to 2 drives via DIN14:

| Signal | Drive | Active State |
|--------|-------|--------------|
| Drive 0 select (pin 5) | First drive (A:) | Low |
| Drive 1 select (pin 6) | Second drive (B:) | Low |

Both drive select lines are typically controlled by the FDC (WD1772) through the DMA chip.

## Motor Control

- Signal: Motor On (pin 8, active low)
- When the FDC needs a drive, it asserts Motor On low
- The drive spins up and reaches full speed within ~15 ms

## Data Signals to Drive

| Signal | Pin | Direction | Function |
|--------|-----|------|----------|
| Write Data | 11 | ST → Drive | Data being written |
| Write Gate | 12 | ST → Drive | Active during write operations |
| Read Data | 1 | Drive → ST | Data being read |

## Step and Direction

The WD1772 generates step pulses and direction signals to position the drive head:

| Signal | Pin | Function |
|--------|-----|------|
| Step | 10 | Pulse the head stepper motor 1 track |
| Direction | 9 | 1 = step outward (track up), 0 = step inward (track down) |

## Track 00 and Write Protect

| Signal | Pin | Active State | Source |
|--------|-----|----- |---------|
| Track 00 | 13 | Low=at track 0 | Drive (1K pull-up) |
| Write Protect | 14 | Low=writable | Drive (1K pull-up) |

The ST reads these signals to determine if:
1. The head is at track 0 (to prevent stepping inward beyond track 0)
2. The disk is write-protected

## Drive Command Summary

| Pin | Signal | Driver | FDC Source |
|-----|- | --- | --- |
| 1 | Read data | WD1772 pin 1 | |
| 2 | Side select | WD1772 pin 2 | |
| 4 | Index pulse | WD1772 pin 38 | |
| 5 | Drive 0 select | WD1772 pin 6 | |
| 6 | Drive 1 select | WD1772 pin 7 | |
| 8 | Motor on | WD1772 pin 8 (inverted) | |
| 9 | Direction | WD1772 pin 9 | |
| 10 | Step | WD1772 pin 10 | |
| 11 | Write data | WD1772 pin 11 | |
| 12 | Write gate | WD1772 pin 12 | |

> ### Correction — 2026-11-28 (`research/floppy-drive-mechanics.md` §11.2; `research/standard-parts-implementation.md` §4, §9.9)
>
> The **function list** above is usable; the **driver column** and four details are not:
>
> - The **side select** and the two **drive selects** are driven by the **PSG's port A** — bit 0 side, bit 1 drive 0, bit 2 drive 1 — not by WD1772 pins 2/6/7. The WD1772's drive-side pins are `STEP` 16, `DIRC` 17, `RD` 19, `MO` 20, `WG` 21, `WD` 22, `TR00` 23, `IP` 24, `WPRT` 25, `DDEN` 26.
> - **`MOTOR ON`** is the WD1772's `MO` (pin 20); the drive reaches rated speed in **480 ms** and `READY` in **505 ms** — not ~15 ms.
> - **`TRACK 00`** prevents stepping **outward** beyond track 0: the drive ignores a step-out while `TR00` is true, and ignores a step-in from **track 81**.
> - **`WRITE PROTECT`** is the drive's `WPRT` output, true when the write-inhibit hole is **open** (protected). The "low = writable" row is not established by the drive specification; treat the polarity as a board read, not a fact.
>
> The drive model uses logical signals, so these pin errors do not block it; the correction is recorded so a later session does not build a board net from the table. The full evidence is `research/floppy-drive-mechanics.md` §3–§6.

## Cross-References

- `floppy/01-physical-format.md` — Physical format: PM3 encoding, sector layout, track geometry, flux recording
- `floppy/02-logical-format.md` — Logical format: boot sector, BPB, FAT, directory, TOS filesystem
- `floppy/03-copy-protection.md` — Copy protection techniques: weak bits, bit-rate variation, sector skew

## References

- [Atari ST Internals, ch. 1.3 - FDC (PDF)](https://www.atarimania.com/documents/Atari-ST-Internals.pdf)
- [WD1772 Specification (PDF)](https://info-coach.fr/atari/documents/_mydoc/WD1772-JLG.pdf)
- [Atari ST fd/hd Programming (PDF)](https://info-coach.fr/atari/documents/_mydoc/FD-HD_Programming.pdf)
