# IKBD Keyboard Controller (HD6301)

> The Hitachi HD6301V1 is a microcontroller that handles keyboard scanning, mouse scanning, and generating events for the Atari ST. Part of the IKBD (Intelligent Keyboard) subsystem.
>
> **Corrections — 2026-11-28** (`research/ikbd-implementation.md`). This page carried three wrong claims about
> the link and the protocol — a 7-data-bit/even-parity frame, a `$FF` + length + data + checksum packet
> envelope, and a command table that is not the ST's — plus the wrong make/break polarity, a matrix shape of
> 8 × 7 that cannot hold the 94 keys it claimed, and 48 bytes of RAM / 1 KB of ROM for a part that has 128 and
> 4 096. The corrected rows below are replaced outright, not annotated; the study carries the evidence and the
> firmware addresses.

## Overview

| Parameter | Value |
|-----------|-------|
| Manufacturer | Hitachi |
| Part | HD6301V1 (CMOS 6801 family; the ST's part carries no Atari house number of its own) |
| Package | DIP40 |
| Function | Intelligent keyboard/controller microcontroller |
| Crystal | 4 MHz; the internal machine cycle (E) is 1 MHz and the SCI divides that further |
| I/O | 29 parallel I/O lines in four ports (P1 8-bit, P2 5-bit, P3 and P4 8-bit) |
| Timer | 16-bit free-running counter with output compare and input capture, clocked by E |
| RAM | **128 bytes** (`$0080`–`$00FF`) — corrected 2026-11-28, was 48 |
| ROM | **4 KB** mask-programmed (`$F000`–`$FFFF`) — corrected 2026-11-28, was 1 KB |

## Position on Atari ST

The HD6301 is located **inside the keyboard case**, connected to the main ST motherboard by a flat cable that
carries `TXD`, `RXD`, power and ground. It talks to the ST's keyboard MC6850 ACIA (`U201` — the one whose pin 3
carries 500 kHz) over that link. The `U900` 74LS164 is **not** in this path: it is the `SINT`→`TAI` delay
(research/standard-parts-implementation.md §7).

## Keyboard Scanning

### Keyboard Matrix

The matrix is **8 read lines × 15 driven lines = 120 positions**, of which the standard keyboard fits 94 keys.
The 8 × 7 figure this page used to print took `DR3`'s seven bits for the whole drive set, dropped `DR4`'s
eight, and could not hold 94 keys in 56 cells.

| register | role |
|----------|------|
| `DR1` (`$02`) | the eight **read** lines, one per matrix row — input; a pressed key pulls its line low |
| `DR3` (`$06`) | bits 1–7 drive the **first seven** matrix columns |
| `DR4` (`$07`) | bits 0–7 drive the **remaining eight** columns, and double as the joystick/mouse direction input |

- **Scan method**: one column per pass of the firmware's main loop — the 16-bit pattern in `$8C`/`$8D` holds
  exactly one drive line low, is shifted left, and wraps after fifteen — so a full pass is fifteen iterations.
- **Debounce**: none in time. Each port is read twice and the loop repeats until the two reads agree.
- **Scan rate**: not programmed anywhere. It follows the loop's instruction count, so it is hundreds of passes
  over the whole matrix per second; the exact figure is open (design/emulator-limitations.md §7).

### Key Encoding

| Event | Code Format | Description |
|-------|-------------|-------------|
| Key press (make) | `n` — 1…`$72` | the plain scan code (corrected 2026-11-28: make is **not** `$80 + n`) |
| Key release (break) | `n \| $80` — `$81`…`$F2` | Atari: "the break code for each key is obtained by ORing 0x80 with the make code" |
| Mouse, joystick, clock, status | `$F6`–`$FF` | reserved block; any byte ≥ `$F6` starts a packet on the ST's side |

## Mouse Port Scanning

The mouse's axes are quadrature counters read as the pairs `XB`/`XA` and `YB`/`YA` on `DR4` bits 0–3 (or 4–7
when the mouse is in port 1); its two buttons are bits of `DR2`. All of them read low when active.

Relative mouse records are **three bytes, whatever the mode**:

| byte | content |
|---|---|
| header | `%111110xy` — `$F8` with **bit 0 = right button, bit 1 = left button** |
| ΔX | two's complement, −128…+127 |
| ΔY | two's complement; **up the screen is negative** with the default Y origin at the top (`$10`) |

A delta beyond ±127 is split across several records, and motion accumulated while a record is in flight rides
the next one — which is why a record can exceed the threshold by a lot. `$0F` moves the origin to the bottom
and inverts the sign.

## Joystick Port Handling

Event records are **two bytes**: `$FE` (joystick 0) or `$FF` (joystick 1), then `%x000yyyy` — bit 4 the
trigger, bits 0–3 up / down / left / right. `$FD` plus a byte per joystick answers an interrogation. Joystick
monitoring (`$17`) sends headerless two-byte samples at a host-chosen rate, and fire-button monitoring (`$18`)
packs joystick 1's trigger eight samples to a byte.

On the STe the paddle inputs are the GST MCU's own ADC (`$FF9211`–`$FF9217`), not this chip's: the IKBD reads
only switch-type devices.

## Serial Link to the ACIA

| Parameter | Value |
|-----------|-------|
| Baud rate | 7 812.5 baud, both directions |
| Data format | **8 data bits, 1 stop bit, no parity** (corrected 2026-11-28 — the page said 7 data bits, even parity) |
| Bit cell | 128 µs; 1.28 ms for a ten-bit character |
| ST side | `U201` at `$FFFC00`/`$FFFC02`, ÷64 from the 500 kHz tap |
| IKBD side | SCI at `E/128` — 1 MHz ÷ 128 |
| Flow control | none: `CTS` and `DCD` are tied low on this board, and the IKBD's SCI has no equivalent input |

### Packet Formats (IKBD → ST)

There is **no envelope, no length byte and no checksum** (corrected 2026-11-28): the header byte's value
decides the record and the ST's parser collects a fixed length per header.

| header | packet | length | payload |
|---|---|---|---|
| `$F6` | status reply | 8 | mode byte, four parameters, two zero pads |
| `$F7` | absolute mouse position | 6 | buttons since the last interrogation, X MSB/LSB, Y MSB/LSB |
| `$F8`–`$FB` | relative mouse position | 3 | header (with the buttons), ΔX, ΔY |
| `$FC` | time of day | 7 | year, month, day, hour, minute, second — packed BCD |
| `$FD` | joystick interrogation reply | 3 | joystick 0 byte, joystick 1 byte |
| `$FE` / `$FF` | joystick event | 2 | `%x000yyyy` |

Make and break bytes are single and never reach `$F6`: make codes stop at `$72`, break codes at `$F2`.

### Commands (ST → IKBD)

Commands are framed by their **parameter count**, not by a terminator, and unknown bytes are NOPs. Only the
reset is answered; the inquiries answer with a packet.

| command | parameters | effect |
|---|---|---|
| `$80` | one (`$01`) | reset; answers `$F0`/`$F1` (the version byte) after the self-test |
| `$07` | 1 | mouse button action |
| `$08` / `$09` / `$0A` | 0 / 4 / 2 | relative · absolute · keycode mouse reporting |
| `$0B` / `$0C` | 2 / 2 | mouse threshold · absolute-mode scale |
| `$0D` / `$0E` | 0 / 5 | interrogate · preset the absolute position |
| `$0F` / `$10` | 0 / 0 | Y origin at the bottom · at the top (default) |
| `$11` / `$12` / `$13` | 0 / 0 / 0 | resume · disable mouse · pause output |
| `$14` / `$15` / `$16` | 0 / 0 / 0 | joystick events · interrogation mode · interrogate |
| `$17` / `$18` / `$19` / `$1A` | 1 / 0 / 6 / 0 | joystick monitoring · fire monitoring · keycode mode · disable |
| `$1B` / `$1C` | 6 / 0 | set · read the time-of-day clock (BCD) |
| `$20` / `$21` / `$22` | 3 + `NUM` / 2 / 2 | memory load · memory read · controller execute |
| `$87`–`$9A` | 0 | status inquiry: `$80 \| SET`, answers an 8-byte `$F6` packet |

### Buffers, and what "busy" costs

| buffer | depth | overflow behaviour |
|---|---|---|
| IKBD command buffer (`$CD`–`$D4`, counter `$CC`) | **7 usable** of 8 | the newest bytes are dropped |
| IKBD output ring (`$D9`–`$ED`, index `$D5`, free counter `$D7`) | **21 cells, 20 usable** | the **whole report** it cannot fit is dropped |
| ST side — the 6850's receive register | 1 byte | the new byte is lost and `OVRN` is set |

## Timing

| quantity | value |
|---|---|
| character on the wire | 1.28 ms = 1 024 CPU clocks at 8 MHz |
| link capacity | 781.25 bytes a second |
| a mouse record | 3.84 ms, so at most 260 records a second |
| a queued burst of six records | 23 ms behind the movement that caused it |
| reset ACK | within 300 ms (Atari), ≈63 ms in Hatari's model |
| first reply byte after a command | ≈0.9–1.25 ms (Hatari's measured delays) |

**Key auto-repeat is TOS's**, not the IKBD's: the firmware reports make and break only, and TOS repeats at
300 ms and then every 40 ms. The IKBD's one internal repeat is the joystick keycode mode's `TX`/`VX` timing.

## References

- `research/ikbd-implementation.md` — the study these corrections come from (turn-taking walk, command table,
  packets, matrix, timing budget, model comparison)
- [Atari IKBD protocol, reproduced in the Linux kernel documentation](https://docs.kernel.org/input/devices/atarikbd.html)
- [Hatari `src/ikbd.c`](https://github.com/hatari/hatari/blob/master/src/ikbd.c)
- [Hitachi HD6301V1 data book](https://bitsavers.trailing-edge.com/components/hitachi/_dataBooks/1988_U71_8-bit_Single_Chip_Microcomputer_Data_Book.pdf)
- [Atari ST Internals, ch. 1.1 - IKBD Keyboard](https://www.atarimania.com/documents/Atari-ST-Internals.pdf)
- [Atari ST Programmer's Reference Guide](https://info-coach.fr/atari/hardware/interfaces.php)
