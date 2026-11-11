# The MFP is one encoder on one line

The Timers chip owns sixteen interrupt sources and one output: a single `IRQ` line into the GSTMCU. The
chip's internal rule is a sentence — *the highest-numbered source that is pending, enabled and unmasked,
with no source at or above it in service* — not a table, and everything the machine sees of it is that one
line plus the vector byte it answers an acknowledge with. The `IPL` lanes, the arbitration between levels,
and the `max(6, 4, 2)` record stay with the chip that physically drives those three pins.

**Status**: accepted (2026-11-11), on the owner's word after ticket #28's grilling session.
Tickets: #28 (this design), #10 (the walking skeleton), #16 (the logic-reconstruction pilot), #21
(Controllers), #26 (Sound), #29 (Disk), #30 (Blitter).

## Context

- The partition gave the MFP to one virtual chip — "timers A–D; interrupt priority encoder and vectoring;
  GPIP edge detection" (`design/subsystems-spec.md` §1) — and left the *behaviour* to this ticket. Eight
  sub-questions arrived with it: the four counters and their prescalers, Timer A's input edge, what Timers
  owns versus what Memory owns, the register semantics, the vector and the acknowledge, the USART's fate,
  the four external inputs, and what is logged.
- **The level question was already settled, and in the direction that makes this ADR small.** Ticket #20's
  ruling: the MFP is level 6 only; VBL (4) and HBL (2) are autovectors Memory raises from its own line and
  frame counters; Memory resolves the three into one three-lane record by `max()`. `crossings.md` §4. The
  physical basis is a pin table: `IPL0–IPL2` are `U400.33/.34/.35` and are driven by the GSTMCU alone; the
  MFP has no `IPL` pins and raises `IRQ` (`U203.32`) into it (`board-inventory.md` §4.3, corrected
  2026-11-05).
- **The chip's clock is a third crystal.** `Y200` at 2.4576 MHz on `X1`/`X2`, not `CLK4`: the timers count a
  domain ADR 0001 did not have, and the ratio to the master is exactly 625/48. The OS's own 200 Hz tick is
  12 288 MFP clocks = 160 000 master ticks = four a frame, exactly.
- **Three reference implementations agree on almost everything, and disagree once.** The register map, the
  source numbering, the descending scan, the vector arithmetic and the acknowledge's state changes are
  identical in `hatari/src/mfp.c`, `neost/src/io/Mfp.cpp` and `steemsse-V4.2.0_R10/steem/mfp.cpp`. The one
  divergence is whether a source already in service blocks *itself*.

## Decision

### 1. One encoder rule, stated over bits

**The selected source is the highest-numbered source that is pending, enabled and unmasked, provided no
source numbered *n* or above is in service.** `IRQ` asserts exactly while such a source exists.

- **The number is the priority.** Source *n* is `IPRA` bit *n−8* for *n* ≥ 8, else `IPRB` bit *n*; the scan
  is 15→8 then 7→0, which is the bit order of the two registers. No sixteen-entry priority table exists,
  because the table would be the bit positions written out again.
- **"At or above", not "above", in the in-service test.** A source whose in-service bit is set must not be
  re-selected: a second edge arriving while its handler runs would otherwise loop. Hatari masks the
  candidate's own bit (`ISRA & 0xe0` for Timer A); NeoST accepts only when `highestInService() < n` — the
  same test; Steem SSE tests strictly-higher sources and diverges. Owner's ruling: the Hatari/NeoST reading.
- **No chronological tie-break.** Hatari and NeoST serve the oldest pending request first within one
  instruction (an artefact of instruction-granularity emulation). The machine's ordering key is
  `(instant, seq)` (ADR 0007); at one instant the source number decides.

### 2. The boundary: Timers never knows a level, Memory never knows a source

| Timers owns | Memory owns |
|---|---|
| the sixteen pending bits, enables, masks, in-service bits | the arbitration between the three asserting levels |
| **which source is selected** | the `IPL0–IPL2` lanes and the one three-lane record per level change |
| the `IRQ` line, asserted and released by the rule | `max(6, 4, 2)` — a comparison, and VBL/HBL from its own counters |
| the vector byte it answers with | nothing about sources |

The reason is not taste: `IPL0–IPL2` has exactly one driver, the GSTMCU, which is also the chip that counts
lines and frames and decodes the MFP's selects. A ninth "interrupt controller" chip would need VBL and HBL
as *inputs* — two new crossings, one per line — to reproduce what one chip already holds.

### 3. The acknowledge

- **Vector**: `(VR & 0xF0) + n`, where *n* is the source selected **at the acknowledge instant**. The encoder
  re-runs there (Hatari and NeoST both re-evaluate before `IACK`), so the byte names exactly the handler the
  CPU will run, and a window race shows up as a recorded vector rather than as silence.
- **State moves at the `IACK` cycle, never at `RTE`.** Pending clears when the vector is passed; in-service
  is set only when `VR` bit 3 = 1 (software end-of-interrupt) and stays clear in automatic mode.
- **A stale acknowledge is a bus error**: Timers answers nothing, Memory times the 6800-style cycle out and
  drives `BERR`, the CPU takes the spurious vector 24 ($60). Two of the three references model it; Hatari
  leaves it a `TODO`. A debug assertion reports the case, so a model bug cannot hide inside it.

### 4. The counters, and the third clock domain

- **All four counters, the full prescaler field, two modes.** Field 0 = stopped, 1–7 = ÷4, ÷10, ÷16, ÷50,
  ÷64, ÷100, ÷200; *interval* mode counts `data × divider` MFP clocks (data 0 = 256), *event count* mode
  (`TACR = 0x08`) counts input edges. The output shapes (`TAO`/`TBO`/`TCO`/`TDO`) are dropped: those pins
  reach nothing on this board.
- **Timer C is a period, Timer B and Timer A are event counters, Timer D is arithmetic.** A prescaler-driven
  Timer B would disagree with the beam on every line that is not exactly 2 048 ticks. Timer D's only job is
  the USART baud clock, and the USART is a stub — so ADR 0008 §5's own rule retires it from the wake list:
  **Timers' `nextDeadline` is the minimum over A, B and C**.
- **The MFP clock enters the one time domain as an exact rational**: one master tick = 48/625 MFP clocks;
  counters keep subticks, expiries round **up** to a whole tick, the remainder is carried. No float, no
  fractional instant, and the 200 Hz tick lands on 160 000 exactly. ADR 0001 is revised in place: one time
  domain, three clock domains, the third exact (the sound path's −0.133 % stands as the only approximation).
- **Timer A's edge is the delayed copy**: `TAI` sees `SINT` 128 ticks (4 µs) late — a 74LS164, 8 stages at
  `CLK2` — while source 15 sees the record's own instant. `derived`, one constant, one place; reversing it
  moves no record.

### 5. The stubs and the deferrals, declared

- **USART**: a register file that always answers "idle" (`TSR` sets the empty flag, `RSR` reads 0, `UDR`
  reads 0 and discards writes, `UCR`/`SCR` store). **No USART source ever sets a pending bit.** A debug
  assertion reports any write that enables one. Reversal is additive: a serial ticket gives sources 9–12 a
  producer and puts Timer D back on the wake list.
- **`GPIP`**: four live lines (bit 3 `BINT`, 4 keyboard ACIA, 5 `FDINT`/`DINT`, 7 `SINT`), four inputs tied
  inactive-high (bits 0, 1, 2, 6), `DDR = 0x00` at reset. Edge rule: an input line sets its pending bit when
  the new level equals its `AER` bit, so an `AER` or `DDR` write can itself be an edge.
- **`modelled-not`**: `VR`'s software-EOI→automatic flip clearing every in-service bit at once.

## Consequences

- **#10** gets a chip it can boot with: `IRQ` at 200 Hz from Timer C, the enable/mask/pending/in-service
  registers with their clear-on-zero semantics, the vector path, and one declared stub. What a Timers stub
  may **not** fake: raise a VBL or an HBL (Memory's), assert an `IPL` lane (Memory's), or count `DE` twice
  (one record, two subscribers).
- **#16** inherits the encoder as its pilot subject, with this ADR as the answer it must reproduce from the
  RTL — the enable equations, not the `casez` arm order.
- **#21** owns `GPIP` bit 4's producer: the keyboard ACIA's interrupt is source 6 and nothing else sets it.
- **#26** keeps the sound side: `SINT`'s frame end is Sound's fact, the record's driver cell stays as
  catalogued.
- **#29** and **#30** own bits 5 and 3 respectively; each sets one pending bit and does nothing else.
- **ADR 0001** gains a dated revision (the third clock domain); **ADR 0008 §5** gains one (Timers' deadline
  list is three); `design/crossings.md` §11's `TAI` and HBL points are answered.

## Considered options

- **A sixteen-entry priority table.** Rejected: the table *is* the bit positions, and two artefacts that can
  drift apart are one more than the design needs. The rule is also the assertion: a pending, enabled,
  unmasked source that is not selected while nothing at or above it is in service is a bug.
- **The encoder hands Memory a level.** Rejected before it starts: a second `IPL` source inside Timers fires
  twice, and the digest cannot see it (two identical runs).
- **Freeze the source at `IRQ` assertion.** Rejected: it fabricates a handler call when software clears the
  pending bit in the window, which is exactly the case a raster demo provokes.
- **Model the USART and give sources 9–12 producers.** Rejected: RS-232 is out of scope on the map, and a
  stub with a defined return costs one table.
- **Timer D on the wake list.** Rejected by the project's own rule (ADR 0008 §5): its expiry is observable
  only through a USART that does not exist. Reversal is one line when serial returns.
- **Quantise the MFP clock to an integer tick** (13 master ticks, +0.16 %). Rejected: TOS's 12 288-clock pair
  would then mean 4.9915 ms and `_frclock` would drift 34 ticks per 100 frames — the OS's own clock stops
  being the proof G6 uses.

## Falsifier

1. **The rule's own assertion** — a pending, enabled, unmasked source that is not selected while nothing
   numbered at or above it is in service fires in any run: the encoder's rule and its bits have parted, and
   the rule is wrong, not the run.
2. **The 200 Hz reading** — if `_frclock` does not advance exactly four times per frame over 100 frames,
   the sub-tick arithmetic or the 625/48 ratio is wrong; the reading is `design/validation.md` G6's second
   column.
3. **The anchor budget** — if Timers' anchors exceed ~1 000 a frame on the idle desktop (they should be
   ≈420: 200 C expiries, ~200 B expiries, ~20 writes), something has started logging per tick.
4. **The delayed `TAI` edge** — a test that reads Timer A's count at the `SINT` record's instant and finds
   it already counted means the 128-tick constant is not applied; if it is applied twice, the test reads one
   count late. Threshold: one count, one edge.
5. **The stub's honesty** — a spurious acknowledge (Q6's `BERR` path) firing during a normal EmuTOS boot
   means either the encoder re-selection or the in-service rule is wrong, and the boot is not a valid run.
