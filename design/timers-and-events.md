# The Timers and events chip — one encoder, four counters, one line

Implementation-facing companion to ticket [#28](https://github.com/laurentprudhon/the-st/issues/28) and
[ADR 0013](../../docs/adr/0013-the-mfp-is-one-encoder-on-one-line.md). Decisions live in the ADR; this
file is the shape the code takes, so #10 and the later device tickets have nothing left to interpret.
Engine rules assumed: ADR 0001 (one time domain; revised here for the third crystal), ADR 0003 (a chip
owns its clock), ADR 0004 (wires are append-only logs), ADR 0006 (subscriptions), ADR 0007 (no priority
table), ADR 0008 (two write shapes, anchors not samples, a wake only where a chip meets another chip),
ADR 0009 (the CPU's sampling side), ADR 0011 (the System Control Unit owns the slot).

Rates are `design/crossings.md`'s. The register map, the source numbering and the encoder's bit semantics
are read from `hatari/src/mfp.c`, `neost/src/io/Mfp.cpp` and `steemsse-V4.2.0_R10/steem/mfp.cpp`, and
from `design/board-inventory.md` §3.8 for the pins.

> ### Correction — 2026-11-11 (ticket #31, owner's ruling)
>
> The virtual chip this file calls *Memory* is the **System Control Unit**, and the DRAM and the TOS
> ROMs are no longer members with pins: they are plain data structures (`Ram`, `Rom`) inside it —
> stores with no timeline, no record, no subscription and no wake. Every statement below about the
> counter, the owner equation, the clients, the beam, the buffer or the leaf stands unchanged; only
> the name and the store boundary moved. Owner's words: *"GSTMCU is too cryptic when you want to
> understand the machine, what about keeping only three letters of the name : SystemControlUnit ?"*
> See [ADR 0014](../docs/adr/0014-the-stores-are-data-structures.md) and `design/system-control-unit.md`.

> ### Correction — 2026-11-11 (owner's ruling, same session)
>
> This chip is the **Timers and events chip** (`TimersAndEvents`). It was *Timers and interrupts* in the
> partition and *Timers* in every operational artefact — a half-renamed vocabulary, which is what made
> the name worth settling. Owner's words: *"maybe we could name it : TimersAndEvents ?"*, on the rule
> that at this stage the chip **selects and names an event**, and presenting it to the CPU as a level is
> the System Control Unit's act. The block that does the selecting is the **event selector**; the file
> `design/timers.md` is now `design/timers-and-events.md`. No decision, rate, record or instant changed.

## 1. What the chip is

**Members (inside Timers and events):** the four counters and their prescalers, the four control and four
data registers, **the event selector** (the sixteen event inputs — the hardware calls an input a *source*,
because that is what an acknowledge names — and the four bit-files over them: pending, enable, mask,
in-service, with the selected event and the acknowledge path), the vector register, `GPIP`/`AER`/`DDR`
with the eight port lines, and the **USART register file as a declared stub**.

**Not members, and not the chip's:** the `IPL0–IPL2` lanes and the arbitration between levels (**the System Control Unit's** —
it drives those three pins, `board-inventory.md` §4.3); the VBL and HBL autovectors (the System Control Unit's line and
frame counters); the address decode and `MFPCS` (the System Control Unit's); the RS-232 line drivers and the ACIA (out of
scope and Controllers').

**The MFP has no `IPL` pins.** `IPL0–IPL2` belongs to the GSTMCU and the MFP to level 6 only, for all
sixteen of its sources, over the one `IRQ` line (`U203.32`).

## 2. What arrives, and what Timers and events takes

| Arrives as | Wire | Rate | How Timers and events treats it |
|---|---|---|---|
| `DE`, second subscriber | `System Control Unit → Timers and events` (`TBI`, pin 20) | 200/frame | **event count** for Timer B: one count per active line, whatever the CPU is doing |
| sound frame end | `SINT` (`U400.105`) | per frame end | sets source 15's pending bit at the record instant; **Timer A counts the edge 128 ticks later** (`TAI`, through the 74LS164) |
| floppy / disk DMA completion | `FDINT`, `DINT` (one shared line) | per completion | sets source 7's pending bit; nothing else |
| blitter completion | `BINT` | per blit | sets source 3's pending bit; nothing else |
| keyboard ACIA event | `N6850` → `GPI4` | per key or mouse event | sets source 6's pending bit |
| register writes and reads | `MFPCS` | per access | writes are one subscription each; reads answer the cycle |

**Timers and events declares one `nextDeadline`** (ADR 0008 §5): the minimum over the counters that can assert a
pending bit — **A, B and C**. Timer D is arithmetic (Q1): its only purpose is the USART baud clock, the
USART is a stub, so its expiry nothing can observe. The assertion is unchanged: *if `nextDeadline` is
set, a queued `Resume` for Timers and events exists at or before it.*

**Timers and events drives exactly one wire that starts work elsewhere**: `IRQ`, one record per assertion and one per
release. Everything else it does is either an answer inside an open exchange (`IACK`, the vector byte, the
register read) or invisible.

## 3. The four counters

```
MFP clock    = 2.4576 MHz  (Y200, X1/X2 pins 17/18 — a third crystal, not CLK4)
1 MFP clock  = 625/48 master ticks   (32 000 000 / 2 457 600 = 625/48, exact)
1 master tick= 48/625 MFP clocks
```

- **The clock is a third domain, and the ratio is exact.** A counter keeps its position in **subticks**
  (1/48 of a master tick); an expiry is `t + N × 625/48` ticks, the remainder is carried to the next
  period, and the instant handed to the scheduler is that value **rounded up** to a whole tick — an
  expiry may never precede the count it needs. No floating point, no fractional instant in the queue.
- **The 200 Hz tick is the proof, not an assumption.** 200 Hz = 12 288 MFP clocks = **160 000 master
  ticks = exactly four per frame**. Whatever (prescaler, data) pair TOS writes, if the pair multiplies to
  12 288 the OS's `_frclock` advances four times a frame by arithmetic alone — G6's second reading.
- **The prescaler field** (`TACR`/`TBCR`/`TCDCR` bits 0–2, Timer D's in the high half of `TCDCR`):

  | field | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
  |---|---|---|---|---|---|---|---|---|
  | divider | **stopped** | ÷4 | ÷10 | ÷16 | ÷50 | ÷64 | ÷100 | ÷200 |

- **Two modes are modelled.** *Interval* (fields 1–7): the counter counts `data × divider` MFP clocks
  (data 0 means 256) and expires. *Event count* (field 0 with bit 3 set, `TACR = 0x08`): the counter
  decrements once per selected input edge and expires at 1, reloading from the data register.
- **The two output shapes are not modelled.** `TAO`/`TBO`/`TCO`/`TDO` reach nothing on this board; a
  square-wave or one-pulse timeline would be a pin nobody reads.
- **Timer C** is the only clock-driven counter the skeleton uses: 200 Hz, 12 288 MFP clocks, 160 000 ticks.
- **Timer B** is an event counter, never a period: a prescaler-driven B would disagree with the beam on
  every line that is not exactly 2 048 ticks. Its expiries are the only Timer B instants; its *counts* are
  the `DE` records, which already exist (`crossings.md` §2).
- **Timer A** is an event counter on `SINT`, at `TAADR` events.
- **Timer D** counts and can be read back, and sets no pending bit while the USART is a stub.

## 4. The event selector — one rule, no table

**What enters:** sixteen events — eight port lines (`GPIP` bits 0–7, live on 3, 4, 5 and 7; the others
tied inactive-high), four counter expiries (A, B, C, D) and four USART conditions (a stub, so they never
arrive). The hardware calls each input a *source*; the thing that arrives on it is an **event**
(`GLOSSARY.md`). Nothing at this stage knows about priority *levels* — those belong to the System
Control Unit, and this chip never computes one.

**Selected source / selected event** = the highest-numbered source that is (a) pending, (b) enabled,
(c) unmasked, and with (d) **no source numbered *n* or above in service**. `IRQ` asserts exactly while
such a source exists.

- The number *is* the priority: source *n* is `IPRA` bit *n−8* for *n* ≥ 8, else `IPRB` bit *n*. So
  "highest number" is a bit position, never a ranked list, and no sixteen-entry table is written down.
- **(d) is "at or above", not "above".** A source already in service must not be re-selected, or a second
  edge during its handler would loop. Hatari masks the candidate's own bit (`ISRA & 0xe0` for Timer A) and
  NeoST tests `highestInService() < n`; Steem SSE tests only strictly-higher sources and diverges — we take
  the Hatari/NeoST reading.
- **No chronological tie-break.** Hatari and NeoST serve the *oldest* pending request first inside one
  instruction; that is an artefact of instruction-granularity emulation. Our key is `(instant, seq)`
  (ADR 0007) and the source number decides at the same instant.

| Source | Register bit | Atari line | Producer in v1 |
|---|---|---|---|
| 15 | `IPRA` 7 | `SINT` (STE sound-DMA active; mono detect is fixed here) | System Control Unit/Sound |
| 14 | `IPRA` 6 | RS-232 ring indicator | **tied inactive-high** |
| 13 | `IPRA` 5 | Timer A | its own counter |
| 12 | `IPRA` 4 | USART receive buffer full | **stub, never sets** |
| 11 | `IPRA` 3 | USART receive error | **stub, never sets** |
| 10 | `IPRA` 2 | USART transmit buffer empty | **stub, never sets** |
| 9 | `IPRA` 1 | USART transmit error | **stub, never sets** |
| 8 | `IPRA` 0 | Timer B | its own counter |
| 7 | `IPRB` 7 | `FDINT` + `DINT`, one shared line | Disk |
| 6 | `IPRB` 6 | keyboard/MIDI ACIA | Controllers (keyboard) |
| 5 | `IPRB` 5 | Timer C | its own counter |
| 4 | `IPRB` 4 | Timer D | arithmetic only |
| 3 | `IPRB` 3 | `BINT`, blitter done | Blitter |
| 2 | `IPRB` 2 | RS-232 CTS | **tied inactive-high** |
| 1 | `IPRB` 1 | RS-232 DCD | **tied inactive-high** |
| 0 | `IPRB` 0 | Centronics busy | **tied inactive-high** |

## 5. The vector path

- **`VR`** (`$FFFA17`): the high nibble is the vector base, bit 3 is the end-of-interrupt mode, bits 0–2
  are unused. The vector byte is `(VR & 0xF0) + n`, where *n* is the source selected **at the acknowledge
  instant** (Q5 — the encoder re-runs there, so the byte names exactly the handler the CPU will run).
- **`IACK`** (`U203.45`) is class 2 in the catalogue: it answers the cycle `IPL` opened. On the
  acknowledge, at that instant, Timers and events appends three records — the vector byte (data), the pending clear
  (anchor), and the in-service write (anchor) — then re-evaluates `IRQ`, so a still-pending source
  re-asserts inside the same instant.
- **State moves at the IACK cycle, never at `RTE`.** Pending clears when the vector is passed; in-service
  is *set* only when `VR` bit 3 = 1 (software EOI, the handler writes 0 to `ISR` at its end) and stays
  clear in automatic mode.
- **A stale acknowledge is a bus error.** If nothing is selected at the acknowledge instant, Timers and events answers
  nothing; the System Control Unit times the 6800-style cycle out and drives `BERR`; the CPU takes the **spurious vector
  24 ($60)**. Steem and NeoST model this; Hatari leaves it a TODO. A debug assertion on this chip's side
  reports such an acknowledge as a conflict — it must never be a normal path.
- **The `IPL` record is the System Control Unit's**, one three-lane record per level change, resolved by
  `max(MFP 6, VBL 4, HBL 2)`. Timers and events never computes a level and the System Control Unit never learns a source.

## 6. The register file

| Register | Write semantics |
|---|---|
| `GPIP` `$FA01` | outputs write the latch; inputs read the driven level |
| `AER` `$FA03` | stores; a change is re-evaluated as an edge (see §7) |
| `DDR` `$FA05` | stores; a change is re-evaluated as an edge; `0x00` = all inputs at reset |
| `IERA/B` `$FA07/.09` | stores, **and clears the pending bits it disables** (`IPR &= IER`) |
| `IPRA/B` `$FA0B/.0D` | **`value = old & written`** — write 0 clears a pending bit, write 1 leaves it |
| `ISRA/B` `$FA0F/.11` | same rule: write 0 clears an in-service bit, write 1 leaves it |
| `IMRA/B` `$FA13/.15` | stores |
| `VR` `$FA17` | stores; the SEI→automatic flip's "clear every in-service bit" is **`modelled-not`** |
| `TACR/TBCR/TCDCR` `$FA19/.1B/.1D` | a write restarts the timer; stopping it captures the live counter for read-back |
| `TADR…TDDR` `$FA1F/.21/.23/.25` | one byte at the odd address; 8-bit counter, data 0 = 256; **a write while the timer runs takes effect at the next expiry**, not immediately |
| `SCR/UCR/RSR/TSR/UDR` `$FA27–.2F` | the USART stub (§7) |

Every one of these is logged as an anchor on every write (ADR 0008 §6): a register that changes only on a
software write is logged on every write, value and register.

`A0` is not decoded by the MFP — five register-select lines (`RS1–RS5`, pins 2–6) carry `A1–A5`
(`derived`) — so all thirty-two registers sit at odd addresses and the even ones alias. A word access to
an odd address is an address error on the 68000, so none of this is reachable in practice; the model
stores bytes.

## 7. `GPIP`, `AER`, and the USART stub

**The edge rule, one sentence:** a level change on a line configured as input (`DDR` bit = 0) sets that
line's pending bit **when the new level equals its `AER` bit** — i.e. when `GPIP ⊕ AER` falls to 0. That is
why a write to `AER` or `DDR` is itself re-evaluated as a possible edge.

| `GPIP` bit | Source | Line | v1 |
|---|---|---|---|
| 3 | 3 | blitter done `BINT` | live (Blitter, #30) |
| 4 | 6 | keyboard ACIA `N6850` | live (Controllers, #21) |
| 5 | 7 | `FDINT` + `DINT` | live (Disk, #29) |
| 7 | 15 | `SINT`, sound frame end | live (Sound/System Control Unit), and `TAI` |
| 0, 1, 2, 6 | 0, 1, 2, 14 | printer busy, DCD, CTS, RI | **inputs tied inactive-high** — they read 1, never set a bit, and their sources are never selected |

A `GPIP` read is a **sample**, never a wake: the pin level at the reader's instant, no record, no
subscription. Its changes are anchored (§8).

**The USART is a declared stub**, because RS-232 is out of scope on the map:

| Register | The stub returns / does |
|---|---|
| `TSR` `$FA2D` | reads with the transmitter-empty flag set (`0x80`), so a poll loop exits on its first pass |
| `RSR` `$FA2B` | reads `0` — no received byte, no overrun, no framing error |
| `UDR` `$FA2F` | reads `0`; a write is accepted and discarded |
| `UCR` `$FA29`, `SCR` `$FA27` | store what is written, change nothing |

- **No USART source ever sets a pending bit** (9, 10, 11, 12): the same rule as Timer D — an expiry no
  other chip can observe is arithmetic, and a source with no producer is never selected.
- **Declared stub, not an exclusion.** The reversal is additive: a serial ticket re-enables sources 9–12,
  gives `UDR` a producer and puts Timer D back on the wake list.
- **A debug assertion reports any `IERA`/`IERB` write that enables a stubbed source**, so a program that
  programs the USART is reported rather than silently ignored.

## 8. Records, anchors, deadlines

**Anchors** (internal register log, one driver, stamped with the chip's clock):

| Log | Anchor at |
|---|---|
| the whole register file | **every software write** — register, value |
| each counter | **every expiry** — C 200/frame, B one per `TBDR` events, A one per sound frame end at `TAADR`, D while it runs |
| `GPIP` | **every external level change** — the instant a pending bit moves with no write to blame |

Every other instant is arithmetic: a counter's value between two expiries, the selected source (a pure
function of the bits), the `IRQ` level, and the vector byte (recorded once, at the `IACK`). `DE` counts
into Timer B are **not** anchored — they are the `DE` wire's own records.

**`nextDeadline` is the minimum over A, B and C** (§2). D is excluded while the USART is a stub; the ADR's
own rule is "a counter whose expiry changes nothing another chip can observe is arithmetic", and D is that
case exactly.

**Rate test (G5):** Timer B 200 counts a frame, of which one expiry per `TBDR` events; Timer C 200
expiries; Timer A one per sound frame end; register writes ~20 idle; `GPIP` changes per completion. Total
anchors sit in the same order as the System Control Unit's 313 line anchors and two orders below a per-tick log.

## 9. What crosses out, and what never does

**Out (exactly the catalogue's rows, no additions):** `IRQ` (one record per assertion, one per release);
`IACK` as the class-2 answer; the vector byte on `D0–D7`; and, as an input, nothing — `DE`, `SINT`,
`FDINT`/`DINT`, `BINT` and `N6850` arrive as records their drivers already append.

**Never (invisible by construction):** the counters' inner steps, the prescaler arithmetic, the selected
source, the pending/in-service bits between anchors, the encoder's scan, the `GPIP` level between changes,
the USART stub's state.

**The one thing Timers and events does not drive:** an `IPL` lane, a VBL, an HBL, or a refresh. All four exist; none of
them here. A second source would fire twice and the digest would not see it (two identical runs).

## 10. Open points

1. **The 74LS164's 4 µs is `derived`, not measured.** 128 master ticks = 8 stages × 500 ns at `CLK2`. If it
   is wrong, only Timer A's pending instant moves — one constant.
2. **The 74LS164 story is doubled** (`board-inventory.md` §6.16): the 1991 parts list has one at `U900` for
   keyboard/MIDI serialisation, while the STE hardware article describes one added to delay `*SINT`. Either
   one part serves both paths or the delay part is not in this parts list. The model takes the delayed copy
   (Q3); a scan of a mature board settles it.
3. **Which timer input `KHZ500` reaches is illegible in the inventory** (§4.3: "which of `TAI`/`TBI` it
   reaches is not legible"). v1 drives neither: nothing in the skeleton reads a 500 kHz reference, and
   `KHZ500`'s other job — the floppy's — is Disk's.
4. **`SINT`'s driver is the System Control Unit in the catalogue, and the sound-DMA frame end is Sound's fact.** The split
   stays as catalogued (#26 keeps the sound side); if #26 re-homes the record, only the `Driver` cell moves.
5. **the chip's `nextDeadline` gained and lost a counter in one ticket:** §2's list is three (A, B, C) rather
   than ADR 0008 §5's four. That is a correction to that ADR, recorded there, not a silent change.
6. **The exact IACK latency is not modelled beyond the CPU's sampling window.** Hatari and NeoST hold `IRQ`
   invisible to the CPU for 4 cycles after it asserts; ADR 0009 already samples interrupts in the
   instruction's last bus cycle inside a 4-clock window, so the delay is subsumed rather than duplicated.
