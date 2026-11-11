# The System Control Unit — the counter, the owner equation, the stores, the clients

> ### Correction — 2026-11-11 (ticket #31, owner's ruling)
>
> This file is `design/memory.md` renamed. The chip it describes is the **System Control Unit**, and
> the DRAM and the TOS ROMs are no longer members with pins: they are plain data structures
> (`Ram`, `Rom`) inside it, defined in §2. Owner's words: *"GSTMCU is too cryptic when you want to
> understand the machine, what about keeping only three letters of the name : SystemControlUnit ?"*
> and, on the store interface, *"the C# interface of the ram and rom data structures should reflect
> as much as possible the real pins (row / columns) but not the timing, all folded in a single
> call."* The decision is [ADR 0014](../docs/adr/0014-the-stores-are-data-structures.md); the
> vocabulary rule is *memory names the resource, never the chip* (`GLOSSARY.md`). Everything below
> about the counter, the owner equation, the clients, the buffer and the leaf is unchanged by the
> split — §1, §2 and §6 are the sections the split rewrote or added.

Implementation-facing companion to tickets [#20](https://github.com/laurentprudhon/the-st/issues/20)
and [#31](https://github.com/laurentprudhon/the-st/issues/31), and to
[ADR 0011](../docs/adr/0011-memory-owns-the-slot.md) and
[ADR 0014](../docs/adr/0014-the-stores-are-data-structures.md). Decisions live in the ADRs; this
file is the shape the code takes, so #10 has nothing left to interpret. Engine rules assumed: ADR
0003 (a chip owns its clock), ADR 0004 (wires are append-only logs), ADR 0005 (the chip is on the
same rules), ADR 0006 (subscriptions), ADR 0007 (no priority table), ADR 0008 (five verbs, anchors
not samples), ADR 0009 (the answering-instant contract).

Catalogue rows and rates are `design/crossings.md`'s. Pitch, slot and window figures are
`research/memory-bus-slots.md`'s; the owner equation is read from the STE reconstruction
(`research/gstmcu-reconstructions.md`, correction of 2026-11-09).

## 1. What the chip is

**Members (inside the System Control Unit):** the DRAM controller and its decode, the refresh
counter, **the video fetch stream** (video address counter, `LINEWIDTH`, hscroll, the **4-word
fetch buffer**), **the sound fetch stream** (sound address counter and frame pointers), sync
generation (`HSYNC`, `VSYNC`, `DE`, `BLANK`, `DCYC`), the chip-select decode, `DTACK`/`BERR`, and —
from #20 and #28 — the `IPL` arbitration.

**Inside it as plain data, not as members:** `Ram` and `Rom`, the two stores of §2. They are not
chips and not members in the sense above: no pin of theirs leaves the chip, they own no clock, they
subscribe to nothing, nothing wakes them, and their access instants are this chip's slot grid
(ADR 0014). That is the distinction the split buys, and a reader needs it anyway — *what has a
timeline* versus *what holds bytes*.

**Clients (served, not members):** the 68000, the Blitter and Disk DMA as bus masters, Sound as a
refill requester.

**Not members, and not clients either:** Sound's Shifter-side FIFO and its `SREQ` generation
(Sound's, by the 2026-11-05 ruling); the disk DMA controller (Disk's); the Blitter (its own chip,
external master, #30). Membership was settled by #5's two-DMA rule and is not re-opened here.

## 2. The stores

Two data structures, sketched in [`design/stores-sketch.cs`](stores-sketch.cs). The interface **is**
the pins, each access is one call, and no timing crosses it: no cycle cost, no latch state, no
strobe sequence, no record, no subscription, no wake.

```csharp
public enum Lane : byte { High = 0, Low = 1 }        // CASnH / CASnL — and U102 / U103

public sealed class Ram                                // 2 banks x 512 rows x 512 cols = 1 MB
{
    public ushort Read (int bank, int row, int column);
    public void   Write(int bank, int row, int column, ushort word);
    public byte   ReadByte (int bank, int row, int column, Lane lane);
    public void   WriteByte(int bank, int row, int column, Lane lane, byte value);
    public void   Refresh(int row);                    // both banks, one row address
    public Span<byte> MachineBytes { get; }            // host only
}

public sealed class Rom                                // 2 x 27C010 = 128K words = 256 KB
{
    public ushort Read(int address);
    public byte   ReadByte(int address, Lane lane);
    public Span<byte> MachineBytes { get; }            // provisioning path (ADR 0010)
}
```

**Why this shape.** `RAS0`/`RAS1` select a bank, `MAD0–MAD9` carry the row and then the column,
`CASnH`/`CASnL` pick the byte lane inside the bank, and `WE` decides the direction — so those are
the parameters and nothing else is. The **controller computes row and column** (it owns `MAD0–MAD9`
and the address latch); the store receives two indices and does no address arithmetic. The ROM has
no multiplexing and no strobes, so its signature is *shorter* than the RAM's — which is what the two
parts look like on the board, and why `Ram` and `Rom` are two types rather than one type with a
read-only flag. Every call is closed: one access in, one value out, no state left behind that a
caller must revisit.

**The population, once** (one machine, no switches — the map's standing preference):

| | Parts | Wiring | Capacity |
|---|---|---|---|
| Bank 0 | 16 × `C0101743-002` (41256-class, 256K×1) | `RAS0`, `CAS0L`/`CAS0H`, 512 rows × 512 columns × 16 bits | 512 KB |
| Bank 1 | 16 × `C0101743-002` | `RAS1`, `CAS1L`/`CAS1H`, same geometry | 512 KB |
| Total | 32 parts in the four SIMM sockets `U600–U603` | | **1 MB** |

`design/board-inventory.md` §2.3 reads "16 × … populate them (1040STE = 2 banks populated)"; the
figure is **per bank**, which is why the inventory's arithmetic (16 × 256K×1 = 512 KB) and the
machine's 1 MB agree. `MAD0–MAD8` carry the nine bits a row or a column needs; this population does
not use `MAD9`.

**`Refresh(row)` is one row, not one row per bank.** The controller keeps a single 512-row counter
and strobes one row address on both `RAS` lines — the only reading under which its 7.8 µs period
(512 rows ÷ 4 ms) and its ≈8.2 slots per PAL line are both true; a per-bank refresh would halve the
period to 3.9 µs and double the slot budget (ADR 0011 §5). `derived` from those two figures, not
read off a schematic.

**`MachineBytes` is the host's, and only the host's.** It is a `Span<byte>` over the store in
**machine byte order** — big-endian pairs, index `2n` the high byte of word `n` — which is why the
backing array is a `byte[]` and the word accessors assemble from two bytes: a raw span over a
`ushort[]` would show every word byte-swapped on a little-endian host, and the debugger would lie.
It serves display inspection, the debugger and side-loading, and it is **not** a second path into
the machine: no virtual chip may hold it (ADR 0005 invariant 1 stands — a chip's only route to
memory is its pins). And because a host write is a change of state with no bus cycle behind it, it
carries no record and no instant: **the host writes only while the machine is stopped** — before
instant 0 for provisioning and fixtures, or between two scheduler steps for a debugger poke. That is
the whole determinism argument, and it needs no new record kind.

**A store is not a chip, and the proof is measurable.** `design/crossings.md` keeps the same rows
and the same rates after the split: 4 000 group deliveries, ≤313 refills, ≤40 000 class-2 bus
entries. Nothing in that table is a DRAM or a ROM pin, because none of those pins ever crossed to
another virtual chip — `MAD0–MAD9`, `RAS0/1`, `CAS0H/L`, `CAS1H/L`, `WE` and `ROM0–ROM6` are
internal to `U400`'s decode and to the sockets beside it.

## 3. One cycle, two slots, one counter

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

## 4. The owner equation (internal slots)

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

## 5. The clients and their answering instants

| Client | Kind | Demand arrives as | Served at |
|---|---|---|---|
| **Video** | computed | nothing — the chip's own counters | one word per cycle while the window is open; a group delivery on the 4th word |
| **Sound** | answered | `SREQ` (`Sound → System Control Unit`, class 1) | the first blanking internal slot after the assertion, up to 4 words back-to-back |
| **CPU** | answered | a bus cycle (`AS`, `R/W`, `FC`, `A1–A23`, class 2) | the next external slot boundary, then `DTACK` |
| **Disk DMA / Blitter** | answered | a granted bus (`BGACK`) | their external slots while the grant holds |
| **Refresh** | computed | nothing — the row counter's expiry | the next unclaimed blanking slot |

**The chip never initiates a sound fetch.** It answers `SREQ`; the FIFO, the cadence and the refill
*semantics* are Sound's (#26), and only the *slot* is the System Control Unit's.

**One timetable, three client kinds** — computed, answered-by-wire, answered-by-bus — all resolved
against the same phase counter. There is no second mechanism and no per-line plan: nothing is precomputed
at the line boundary.

## 6. The video fetch buffer — the chip's, why it exists, and why it is not a store

4 words, one group, inside the System Control Unit (owner's ruling, 2026-11-09):

- **Fill:** one word per cycle while the window is open — the fill rate *is* the phase counter's rate.
- **Drain:** one group delivery when the 4th word lands; the four words go out on `MD0–MD15` and the
  `DCYC`+`LOAD` record is appended.
- **Why it sits here:** it makes the counter legible. 16 px per word at the 8 MHz pixel clock is one word
  per 4 clocks, and one internal slot per 4 clocks is what the phase counter gives — the identity the
  whole machine rests on is now visible in one place instead of being an unexplained property of a
  Shifter-side buffer.
- **Why the split does not move it:** it is a *timing* structure, not storage. Its occupancy is a delivery
  instant — filled one word per internal slot, drained as one group — so putting it in `Ram` would move an
  instant into a store that has none, and its 4 000 deliveries a frame are the catalogue's rows, not a
  store's business.
- **Unchanged:** 4 000 deliveries/frame, 16 000 data records, every instant in `crossings.md` §2.
- **Still approximate:** the buffer's occupancy against the Shifter's own latch/staging. That is where the
  honest hedge now lives, replacing "the group is a stated approximation of the buffering".

## 7. The Level 1 / Level 2 seam — retired

The owner's ruling of 2026-11-09: **the machine is bus-aware throughout; there is no Level-1 step.** A
"4 clocks" answer would have been a second path, not a simpler one, because the chip already computes
every instant from the counter. Consequences for the code:

- **No internal-share constant exists.** With no stretch, the share is structurally 1 per cycle and never
  varies; the constant the ticket expected to keep is not needed.
- **Contention is the external slot's**, and it is already the arbiter's business (ADR 0007): a granted
  master holds the external slots, a pending CPU cycle waits for the release.
- `design/validation.md` §7's "Level 1 models none; nothing can fail for a contention reason" is
  corrected — a run can now fail for a contention reason.

## 8. What crosses out, and what never does

Out (exactly the catalogue's rows, no additions): the group delivery (`DCYC`+`LOAD`, one record per four
words), the refill answer (`SLOAD`), `DE` (two subscribers: Video and Timer B), `BLANK`, `IPL0–IPL2` as
one resolved three-lane record, `FDINT`/`DINT`/`BINT` (frame ends and completions), the CPU's `DTACK`/
`BERR`, the register selects (`CS`, `MFPCS`, `SNDCS`, `FCS`, `JOYWE`, `JOYWL`, `N6850`), and the `RDY` →
`BR` relay for disk DMA. `HSYNC`/`VSYNC` go nowhere in the machine.

Never (invisible by construction): the slot decisions, the owner equation, the 8-phase counter, the
DRAM address latch, the refresh counter and its addresses, the fetch buffer's occupancy, the per-word
fetch addresses, the video and sound counters' progress — and, since the split, every byte the stores
hold.

## 9. Counters, anchors, deadlines

**Anchors** (internal register log, one driver, stamped with the chip's clock; ADR 0008 §6):

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

**`nextDeadline`** (ADR 0008 §5) is the minimum over the counters whose expiry **the chip can observe
as another chip's business** — its wakes are exactly the moments it meets the outside world:

```
nextDeadline = min( next video group boundary,      // 4th word of the current group → push to Video
                    next sound slot,                // a refill owed but not yet served → SLOAD to Sound
                    next external slot owed )       // a pending CPU/DMA exchange's answer → DTACK
```

**Refresh is not a deadline.** It is purely internal: it never preempts video, never delays sound, and
nothing outside the chip can observe it, so there is nothing to be awake for. The row counter is
bookkeeping computed on demand — ADR 0008 §5's own rule ("a counter whose expiry changes nothing
observable needs no wake") applied to the one counter that has no wire. The refresh slots a line
consumed are arithmetic over the same anchors; if a debugger wants them, it computes them. The split
changes none of this: `Ram.Refresh(row)` is called by the controller's own logic, at a slot the
controller already owns, and it appends nothing.

The invariant stands: *if `nextDeadline` is set, a queued `Resume` for the chip exists at or before it.*

**Why the field exists at all.** The engine never polls and has no timer wheel: a chip runs until it
suspends, and the scheduler resumes whoever holds the earliest instant. A chip that owns a counter
therefore has to *declare* the instant it next needs to run — otherwise it sits suspended on a
subscription while its own counter expires, and the machine runs past an event silently, deterministically,
with a green digest. `nextDeadline` is that declaration, and it does no scheduling: it is a debug-only
number whose only reader is the assertion. It is computed where the logic computes it, cleared when
consumed, and it is the **minimum** of the three because being awake at the earliest one is enough — the
others are recomputed when the chip next runs. It costs nothing in release builds.

## 10. The memory leaf — boundary, not permission

ADR 0005's leaf may substitute **exactly one closed exchange**: one CPU bus cycle and the chip's answer,
with both clocks moved to the answering instant, every record appended in full, no subscription but the
CPU's touched, and a digest stream identical apart from declared display decimation. It is T=1 only and
**off by default**; the owner's position — *"it should only be a very last resort, because it would
really cripple the cpu and memory coroutines logic and readability"* — is a constraint on every future
session. This design defines the boundary; it does not turn it on, and the store split does not reach
it: the leaf inlines the **slot wait**, not just the store's read, which is why a `Ram.Read` that is one
call neither is the leaf nor makes it cheaper to justify (ADR 0014, consequences).

## 11. Open points

1. **The refresh period is `derived` and now only arithmetic.** 7.8 µs = 512 rows ÷ 4 ms; the
   reconstruction hard-wires the refresh address to 0 and proves the slot, never the row. With refresh
   off the deadline list (owner's ruling, 2026-11-09, §12), the period only shapes the *budget* — how
   many of a line's blanking slots refresh consumes — and no instant depends on it. A scope on `RAS0`
   with `CAS` suppressed settles the real figure; nothing in the model fails if it is wrong by a factor.
2. **The sound FIFO's depth (4 words) is `inferred`** from Steem with no documentary support; it belongs
   to #26. If it changes, `crossings.md` §2's ≤313 refills/frame gets a dated correction.
3. **Blitter and disk DMA slot occupation** (does a granted burst take every external slot, or one in
   two?) is #29's and #30's, not this chip's.
4. **Absolute timeline fidelity** is unproven by design (`design/validation.md` §7); the overscan cost
   question is the one place where our CPU timing may be optimistic.

## 12. Revision — 2026-11-09 (after acceptance): refresh is not a deadline

Owner's ruling: *"memory only needs to run, ie to synchronize with the system time, when it interacts
with external signals : sample CPU request, sample sound request, push to video. refresh is purely
internal to memory, so nextDeadline should not be concerned by refresh."*

- The deadline list is **three**, not four (§9): a group boundary (push to Video), a refill slot owed
  (`SLOAD` to Sound), an external slot owed (an answer to the CPU or a granted master).
- **Refresh leaves the wake list entirely.** It is bookkeeping computed on demand, not a scheduled
  instant; the row counter is still logged as an anchor, and the slots a line spent on it are arithmetic.
- The general rule this makes explicit: **a chip needs a wake only where it meets another chip.** Its
  wakes are exactly its crossings — 4 000 group deliveries, ≤313 refills, and its bus answers.
- Consequences: ADR 0011 §5 and its Consequences line revised in place; ADR 0008 §5's deadline list
  corrected; open point 1 above softened — the 7.8 µs figure now only shapes the budget, not any instant.
