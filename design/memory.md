# The Memory chip — the counter, the owner equation, the clients

Implementation-facing companion to ticket [#20](https://github.com/laurentprudhon/the-st/issues/20) and
[ADR 0011](../docs/adr/0011-memory-owns-the-slot.md). Decisions live in the ADR; this file is the shape
the code takes, so #10 has nothing left to interpret. Engine rules assumed: ADR 0003 (a chip owns its
clock), ADR 0004 (wires are append-only logs), ADR 0005 (Memory is a chip on the same rules), ADR 0006
(subscriptions), ADR 0007 (no priority table), ADR 0008 (five verbs, anchors not samples), ADR 0009
(the answering-instant contract).

Catalogue rows and rates are `design/crossings.md`'s. Pitch, slot and window figures are
`research/memory-bus-slots.md`'s; the owner equation is read from the STE reconstruction
(`research/gstmcu-reconstructions.md`, correction of 2026-11-09).

## 1. What the chip is

**Members (inside Memory):** the 16 DRAM parts and their mux, the DRAM controller and its decode, the
refresh counter, **the video fetch stream** (video address counter, `LINEWIDTH`, hscroll, the **4-word
fetch buffer**), **the sound fetch stream** (sound address counter and frame pointers), sync generation
(`HSYNC`, `VSYNC`, `DE`, `BLANK`, `DCYC`), the chip-select decode, and `DTACK`/`BERR`.

**Clients (served, not members):** the 68000, the Blitter and Disk DMA as bus masters, Sound as a
refill requester.

**Not members, and not clients either:** Sound's Shifter-side FIFO and its `SREQ` generation (Sound's,
by the 2026-11-05 ruling); the disk DMA controller (Disk's); the Blitter (its own chip, external master,
#30). Membership was settled by #5's two-DMA rule and is not re-opened here.

## 2. One cycle, two slots, one counter

```
cycle  = 500 ns = 4 CPU clocks = 16 master clocks  (the 8-phase counter's period)
slot   = 250 ns = 2 CPU clocks                     (one DRAM access)
cycle  = 1 internal slot + 1 external slot          (2 of the 8 phases each)
line   = 512 CPU clocks = 128 cycles = 128 I + 128 E slots = 64 µs
```

The counter is **phase, not a schedule**: `addrselb = ~time5` in the reconstruction — a decode of the
8-phase ring. Nothing can widen it, and no client can jump it. Therefore:

- **No record exists for a slot.** `design/scheduler.md` §2's two entry kinds and §6's anchors are the
  whole bookkeeping; a slot is an arithmetic position in the counter, never an event.
- **An answering instant is on the slot grid.** The CPU's answer is the next external slot boundary at or
  after its assertion; that is ADR 0009's "round up to 4" and the whole of the wait-state model.
- **CPU throughput is capped by the phase** — at most one external access per 4 clocks, 4 MB/s.

## 3. The owner equation (internal slots)

**One rule, no ranking:**

```
internal slot owner = video    if the horizontal display window is open
                      sound    else if SREQ is pending
                      refresh  otherwise
```

Read from the STE reconstruction's enables, which make the `casez` arms mutually exclusive
(`research/memory-bus-slots.md` §7, correction of 2026-11-09):

| Signal | Source | Meaning |
|---|---|---|
| `hde1` | `gstmcu.v:906` (`hde1 <= hde`), horizontal only (`DE <= hde & vde`, `gstmcu.v:907`) | the display window, open on all 313 lines |
| `refb = hde1 \| sreq` | `mcucontrol.v:63` | refresh is selected only when the window is shut and sound is idle |
| `snden = ~hde1 & sreq` | `mcucontrol.v:95` | sound is enabled only outside the window |
| `REF = addrselb & ~refb` | `gstmcu.v:142` | a refresh cycle is an internal slot with nobody else claiming it |

Consequences the code must honour:

1. **No client can enter a window it does not own.** Video cannot take the external slot (the external
   phase's RAS term requires a pending CPU access); the CPU cannot take the internal one.
2. **Refresh is filler, never a preemptor.** Its 7.8 µs expiry may fall inside a display window; it is
   *deferred to the next blanking slot*, never served mid-window.
3. **Sound lives in blanking**: 48 internal slots per line against ≤3.2 needed, and refresh's ≈8.2.
4. **A wider window costs refresh and sound their slots, not the CPU.** There is no overscan stretch in
   the reconstruction or in either vendored emulator; recorded as a fidelity gap, not modelled as a rule.
5. **Border lines do not hand their window slots over.** `hde` is horizontal, so the window exists on
   all 313 lines and its slots are reserved-and-idle when `DE` is low.

## 4. The clients and their answering instants

| Client | Kind | Demand arrives as | Served at |
|---|---|---|---|
| **Video** | computed | nothing — Memory's own counters | one word per cycle while the window is open; a group delivery on the 4th word |
| **Sound** | answered | `SREQ` (`Sound → Memory`, class 1) | the first blanking internal slot after the assertion, up to 4 words back-to-back |
| **CPU** | answered | a bus cycle (`AS`, `R/W`, `FC`, `A1–A23`, class 2) | the next external slot boundary, then `DTACK` |
| **Disk DMA / Blitter** | answered | a granted bus (`BGACK`) | their external slots while the grant holds |
| **Refresh** | computed | nothing — the row counter's expiry | the next unclaimed blanking slot |

**Memory never initiates a sound fetch.** It answers `SREQ`; the FIFO, the cadence and the refill
*semantics* are Sound's (#26), and only the *slot* is Memory's.

**One timetable, three client kinds** — computed, answered-by-wire, answered-by-bus — all resolved
against the same phase counter. There is no second mechanism and no per-line plan: nothing is precomputed
at the line boundary.

## 5. The video fetch buffer — Memory's, and why it exists

4 words, one group, inside Memory (owner's ruling, 2026-11-09):

- **Fill:** one word per cycle while the window is open — the fill rate *is* the phase counter's rate.
- **Drain:** one group delivery when the 4th word lands; the four words go out on `MD0–MD15` and the
  `DCYC`+`LOAD` record is appended.
- **Why it sits here:** it makes the counter legible. 16 px per word at the 8 MHz pixel clock is one word
  per 4 clocks, and one internal slot per 4 clocks is what the phase counter gives — the identity the
  whole machine rests on is now visible in one place instead of being an unexplained property of a
  Shifter-side buffer.
- **Unchanged:** 4 000 deliveries/frame, 16 000 data records, every instant in `crossings.md` §2.
- **Still approximate:** the buffer's occupancy against the Shifter's own latch/staging. That is where
  the honest hedge now lives, replacing "the group is a stated approximation of the buffering".

## 6. The Level 1 / Level 2 seam — retired

The owner's ruling of 2026-11-09: **the machine is bus-aware throughout; there is no Level-1 step.** A
"4 clocks" answer would have been a second path, not a simpler one, because Memory already computes
every instant from the counter. Consequences for the code:

- **No internal-share constant exists.** With no stretch, the share is structurally 1 per cycle and never
  varies; the constant the ticket expected to keep is not needed.
- **Contention is the external slot's**, and it is already the arbiter's business (ADR 0007): a granted
  master holds the external slots, a pending CPU cycle waits for the release.
- `design/validation.md` §7's "Level 1 models none; nothing can fail for a contention reason" is
  corrected — a run can now fail for a contention reason.

## 7. What crosses out, and what never does

Out (exactly the catalogue's rows, no additions): the group delivery (`DCYC`+`LOAD`, one record per four
words), the refill answer (`SLOAD`), `DE` (two subscribers: Video and Timer B), `BLANK`, `IPL0–IPL2` as
one resolved three-lane record, `FDINT`/`DINT`/`BINT` (frame ends and completions), the CPU's `DTACK`/
`BERR`, the register selects (`CS`, `MFPCS`, `SNDCS`, `FCS`, `JOYWE`, `JOYWL`, `N6850`), and the `RDY` →
`BR` relay for disk DMA. `HSYNC`/`VSYNC` go nowhere in the machine.

Never (invisible by construction): the slot decisions, the owner equation, the 8-phase counter, the
DRAM address latch, the refresh counter and its addresses, the fetch buffer's occupancy, the per-word
fetch addresses, the video and sound counters' progress.

## 8. Counters, anchors, deadlines

**Anchors** (internal register log, one driver, stamped with Memory's clock; ADR 0008 §6):

| Log | Anchor at |
|---|---|
| line counter | each line start (313/frame) |
| video counter | `VBASE`/`VCOUNT`/`LINEWIDTH`/hscroll software writes, the VBL reload, **and one per line start** |
| sound frame pointers (`FBASE`, `FCOUNT`, `FEND`, mode) | software write; frame load |
| refresh row index | each expiry |

The per-line video anchor is the owner's refinement: **addresses are produced by registers**, so a frame
can be understood only if the values that enter the address arithmetic are logged — but the per-word
fetch (16 000/frame) is arithmetic and never logged. The line anchor costs 313 records and makes each
line reconstructible without replaying the frame.

**`nextDeadline`** (ADR 0008 §5) is the minimum over the chip's own counters:

```
nextDeadline = min( next video group boundary,      // 4th word of the current group
                    next sound slot,                // a refill owed but not yet served
                    refresh expiry,                 // 7.8 µs, derived
                    next external slot owed )       // a pending bus exchange's answer
```

The invariant stands: *if `nextDeadline` is set, a queued `Resume` for Memory exists at or before it.*

**Why the field exists at all.** The engine never polls and has no timer wheel: a chip runs until it
suspends, and the scheduler resumes whoever holds the earliest instant. A chip that owns a counter
therefore has to *declare* the instant it next needs to run — otherwise it sits suspended on a
subscription while its own counter expires, and the machine runs past an event silently, deterministically,
with a green digest. `nextDeadline` is that declaration, and it does no scheduling: it is a debug-only
number whose only reader is the assertion. It is computed where the logic computes it, cleared when
consumed, and it is the **minimum** of the four because being awake at the earliest one is enough — the
others are recomputed when Memory next runs. It costs nothing in release builds.

## 9. The memory leaf — boundary, not permission

ADR 0005's leaf may substitute **exactly one closed exchange**: one CPU bus cycle and Memory's answer,
with both clocks moved to the answering instant, every record appended in full, no subscription but the
CPU's touched, and a digest stream identical apart from declared display decimation. It is T=1 only and
**off by default**; the owner's position — *"it should only be a very last resort, because it would
really cripple the cpu and memory coroutines logic and readability"* — is a constraint on every future
session. This ticket defines the boundary; it does not turn it on.

## 10. Open points

1. **The refresh period is `derived`, not modelled.** 7.8 µs = 512 rows ÷ 4 ms; the reconstruction
   hard-wires the refresh address to 0 and proves the slot, never the row. A scope on `RAS0` with `CAS`
   suppressed settles it.
2. **The sound FIFO's depth (4 words) is `inferred`** from Steem with no documentary support; it belongs
   to #26. If it changes, `crossings.md` §2's ≤313 refills/frame gets a dated correction.
3. **Blitter and disk DMA slot occupation** (does a granted burst take every external slot, or one in
   two?) is #29's and #30's, not this chip's.
4. **Absolute timeline fidelity** is unproven by design (`design/validation.md` §7); the overscan cost
   question is the one place where our CPU timing may be optimistic.
