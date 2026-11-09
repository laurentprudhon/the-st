# Memory owns the slot: one counter, one owner equation, three client kinds

The memory subsystem is one counter and one rule. A 500 ns cycle holds one internal and one external
slot; the internal slot has exactly one owner, decided by a single equation — **video while the
horizontal display window is open, otherwise sound when `SREQ` is pending, otherwise refresh**. Video is
*computed* from Memory's own counters into a 4-word fetch buffer, sound is *answered* on `SREQ` in the
next blanking slot, the CPU and any granted bus master are *answered* at the next external slot. A slot
is arithmetic and never a record; refresh is filler and never a preemptor; the machine is bus-aware
throughout, with no Level-1 step and no internal-share constant.

**Status**: accepted (2026-11-09), on the owner's word during ticket #20's grilling session.
Tickets: #20 (this design), #10 (walking skeleton), #26 (sound path), #27 (video), #28 (timers),
#29 (disk), #30 (Blitter).

## Context

- Memory is the one chip the walking skeleton cannot stub: every bus cycle's answering instant comes
  from its slot schedule, and that instant *is* the CPU's wait state (ADR 0009). A skeleton built on a
  provisional Memory would prove determinism of a timetable nobody decided.
- The slot arithmetic was already established (`research/memory-bus-slots.md`, `documented`/`derived`
  throughout): 250 ns slots, 2 internal and 2 external clocks alternating, 128 internal slots per PAL
  line, video at exactly the internal supply rate during display, sound ≤3.2 slots/line, refresh ≈8.2.
- Two behavioural questions the note left open — intra-line order and whether refresh preempts video —
  were answered by reading the GSTMCU reconstruction, and then **re-answered**: the first reading took
  the address mux's `casez` for a priority encoder. It is a decode table whose arms cannot meet, because
  the select lines are derived from the same signals the clients are gated by
  (`mcucontrol.v:63,95`, `gstmcu.v:142,906,907`; corrections of 2026-11-09 in
  `research/memory-bus-slots.md` §7 and `research/gstmcu-reconstructions.md`).
- The owner's standing preferences shape the answer: one teachable rule over a table of special cases,
  decisions that remove variability, and automation only where it is deterministic.

## Decision

### 1. One cycle, two slots, one counter — and a slot is never a record

```
cycle = 500 ns = 4 CPU clocks = 16 master clocks   (the 8-phase counter's period)
slot  = 250 ns = 2 CPU clocks                      (one DRAM access)
cycle = 1 internal slot + 1 external slot
line  = 512 CPU clocks = 128 cycles = 128 I + 128 E slots
```

- `addrselb` decodes the 8-phase counter (`gstmcu.v:705`): alternating internal and external phase, no
  ranking, nothing able to widen it.
- **The grid is arithmetic, not a record stream.** 256 slots per line × 313 lines ≈ 80 000 decisions a
  frame; a record per slot is the tick table ADR 0006/0007 exist to forbid. Records exist only where a
  client is actually served.
- **An answering instant lies on the external slot grid**: the next external slot boundary at or after
  the assertion. That is the whole of "round up to 4" and the whole of the wait-state model (ADR 0009).

### 2. The owner equation

```
internal slot owner = video    if the horizontal display window is open
                      sound    else if SREQ is pending
                      refresh  otherwise
```

Read from the reconstruction's enables, not from arm order:

| Signal | Source | Meaning |
|---|---|---|
| `hde1` | `gstmcu.v:906`; horizontal only, `DE <= hde & vde` (`:907`) | the window, open on all 313 lines, active window or border |
| `refb = hde1 \| sreq` | `mcucontrol.v:63` | refresh selected only when the window is shut **and** sound is idle |
| `snden = ~hde1 & sreq` | `mcucontrol.v:95` | sound enabled only **outside** the window |
| `REF = addrselb & ~refb` | `gstmcu.v:142` | a refresh cycle is an internal slot with nobody else claiming it |

Three consequences, each of which the model must reproduce:

1. **No client can enter a window it does not own.** The internal phase never serves the CPU (the
   external phase's RAS term requires a registered CPU access, `gstmcu.v:620`), and video never takes
   the external slot.
2. **Refresh is filler, never a preemptor.** Its expiry can fall inside a window and is deferred to the
   next blanking slot. Blanking offers 48 internal slots per line against ≈8.2 refresh + ≤3.2 sound.
3. **A wider display window costs refresh and sound their slots, and costs the CPU nothing.** No stretch
   mechanism exists in the reconstruction, and neither Hatari nor Steem models CPU contention at all —
   both derive the video counter from the line's cycle position (`mmu.cpp:641-668`; Hatari computes
   windows only). The claim that overscan slows the CPU is recorded as a **fidelity gap**, not modelled
   as an invented rule.

### 3. Three client kinds, one timetable

| Client | Kind | Served at |
|---|---|---|
| Video | **computed** — Memory's own counters, nothing on a wire | one word per cycle while the window is open; a group delivery on the 4th word |
| Sound | **answered** — `SREQ`, class 1 | first blanking internal slot after the assertion, up to 4 words back-to-back |
| CPU / granted master | **answered** — a bus cycle, class 2 | next external slot boundary, then `DTACK` |
| Refresh | **computed** — the row counter's expiry | next unclaimed blanking slot |

- **Memory never initiates a sound fetch.** It answers one; the FIFO, the cadence and the refill
  semantics are Sound's (#26), the *slot* is Memory's.
- **No per-line plan, no lookahead device.** The ticket's "computed lookahead or per-fetch request"
  question dissolves: it is one timetable, and the client kind says how its demand reaches it.

### 4. The 4-word fetch buffer lives in Memory

Owner's ruling: *"model the 4 words buffer inside memory instead of inside video to explain the 8-phase
counter"*.

- Fill: one word per cycle while the window is open — the fill rate *is* the counter's rate.
- Drain: one group delivery when the 4th word lands, the four words on `MD0–MD15`, one `DCYC`+`LOAD`
  record.
- Why here: 16 px per word at the 8 MHz pixel clock is one word per 4 clocks, and the phase counter hands
  out one internal slot per 4 clocks — the identity the machine rests on becomes visible in one place.
- Unchanged: 4 000 deliveries/frame, 16 000 data records, every instant in `crossings.md` §2. The hedge
  "a stated approximation of the buffering" retires; what remains approximate is the buffer's occupancy
  against the Shifter's own latch.

### 5. Refresh: an anchor, never a record, never a deadline

- No wire, no event: refresh is Memory's own service. It is logged as an anchor on the refresh counter
  (row index at each expiry).
- **It is also not a wake.** Nothing outside Memory can observe it — it never preempts video, never
  delays sound — so there is nothing to be resumed for. The row counter is computed on demand (ADR 0008
  §5: "a counter whose expiry changes nothing observable needs no wake"). Revision of 2026-11-09 below.
- **7.8 µs stays `derived`** (512 rows ÷ 4 ms, the 41256 requirement); the reconstruction hard-wires the
  refresh address to 0 and proves the slot, never the row.
- The STE internal-share question is answered in the negative for this model: sound is gated to blanking,
  so nothing needs widening for it (`inferred` from the STE reconstruction).

### 6. The crossing boundary is the catalogue's rows, unchanged

Out of Memory: the group delivery, `SLOAD`, `DE` (two subscribers), `BLANK`, one resolved `IPL0–IPL2`
record, `FDINT`/`DINT`/`BINT`, `DTACK`/`BERR`, the register selects, and the `RDY` → `BR` relay for disk
DMA. Never out: slot decisions, the owner equation, the phase counter, the DRAM address latch, the
refresh counter, the buffer's occupancy, the per-word fetch addresses.

### 7. What this retires

- **The Level 1 / Level 2 seam.** Owner: *"we should model level 2 right now, and level 2 only, no need
  to introduce a first step witrh level 1 given our current design"*. Memory already computes every
  instant from the counter, so a "4 clocks" Level 1 would be a second path, not a simpler one.
- **The internal-share constant** the ticket intended to keep as a Level-2 hook: with no stretch, the
  share is structurally one slot per cycle and never varies.
- **The overscan stretch** I proposed mid-session: no evidence for it in the reconstruction, Hatari or
  Steem (the owner asked for the emulator precedent before ruling, and it went the other way).

## Invariants

1. A slot is arithmetic; no record exists for an idle slot, a slot decision, or a phase.
2. The internal slot has exactly one owner, by the equation in §2; no client enters a window it does not own.
3. Refresh is deferred, never served inside a display window, and never a wake instant.
4. Video's fill rate equals the phase counter's rate; its buffer never invents an instant.
5. Every answering instant is an external slot boundary; the CPU's wait state *is* that instant.
6. Memory initiates no sound fetch and owns no part of the sample clock.
7. Nothing crosses out of Memory that is not a `design/crossings.md` row.
8. Anchors are logged where a value enters the address arithmetic, plus one per line; addresses themselves are arithmetic.

## Consequences

- **#10** implements this shape: the phase counter, the owner equation, the 4-word buffer, the four
  counters and their anchors, `nextDeadline` as the minimum over the three that meet another chip
  (group boundary, refill owed, bus answer — **never refresh**), the per-line video anchor, and the
  assertion that a queued `Resume` exists at or before `nextDeadline`. Its skeleton must model
  contention, and `design/validation.md` §7's "nothing can fail for a contention reason" is corrected.
- **#26** inherits the refill *rule* (first blanking internal slot after `SREQ`, up to four words,
  never inside a window) and keeps the FIFO depth (4 words, `inferred`) and the sample cadence.
- **#27** inherits the delivery cadence: one group per four words at the 4th word's instant, 20 groups per
  active line, 4 000/frame — and the buffer now lives on Memory's side of the boundary.
- **#28** inherits `DE` → Timer B (200 counts/frame) and the single resolved `IPL` record; HBL/VBL
  arbitration stays Memory's.
- **#29** inherits the `$FF8609/0B/0D` split: the DMA address *counter* is the Disk chip's (a bus
  master), Memory only muxes `dma_reg` onto the address bus in the external phase.
- **#30** inherits that the Blitter is an external master: it reaches RAM only through granted external
  slots, and its slot occupation is #30's question.
- **`design/memory.md`** is the implementable shape; `research/memory-bus-slots.md` §7 and
  `research/gstmcu-reconstructions.md` carry the dated corrections this ruling forces.
- **Vocabulary**: *slot*, *owner equation*, *fetch buffer* and *answering instant* are used as defined
  here; `GLOSSARY.md` gains *Slot* and amends *Memory service* and *Accuracy level*.

## Considered options

- **A priority chain inside the internal class (refresh → sound → video), refresh preempting video.** The
  handoff's reading, taken from the `casez` arm order. Rejected: the arms cannot meet, and the correction
  of 2026-11-09 shows the enables make them mutually exclusive; a priority table here would invent
  contention the hardware does not have.
- **One record per slot, the grid fully materialised.** Rejected: ≈80 000 records/frame, exactly the tick
  table ADR 0006/0007 forbid.
- **Per-client cadences with no shared counter.** Rejected: it reproduces today's arithmetic by
  coincidence and has no account of why video's rate equals the display rate, nor any place to put the
  external slot the CPU needs.
- **A per-line lookahead plan (video words, refresh, sound reservations resolved at the line boundary).**
  Rejected: a second schedule beside the counter, with nothing in the hardware corresponding to it.
- **Modelling the overscan stretch as a hypothesis about the silicon.** Rejected by the owner after the
  emulator precedent was checked: no emulator models CPU contention, and an invented rule is worse than a
  recorded gap.
- **A Level 1 first step answering "4 clocks".** Rejected by the owner: it is a second path, not a
  simpler one, since Memory already computes every instant from the counter.
- **Video's 4-word buffer staying on Video's side.** Rejected by the owner: putting it in Memory is what
  explains the 8-phase counter.

## Falsifier

1. **Mechanism.** An uncontended RAM read must answer at exactly **4 CPU clocks (16 ticks)** after
   assertion, for every assertion alignment; a measured answer off that figure means the slot phase is
   wrong, not that the phase idea is.
2. **Observation.** A frame's 16 000 video fetch addresses must be reconstructible from the logged
   anchors (one per line plus every register write). If they cannot be, the anchor set is too thin: add
   per-group anchors — never per-word ones.
3. **Choice.** If the walking skeleton cannot hold 50 Hz with headroom once contention is modelled, the
   response is ADR 0005's memory leaf, never a different timetable.
4. **Nothing.** The refresh period (7.8 µs, `derived`), the FIFO depth (4 words, `inferred`) and the
   blitter/disk slot occupation are other tickets' facts; their correction reopens nothing here.

## Revision — 2026-11-09 (owner, after acceptance): refresh is not a deadline

Owner's ruling: *"memory only needs to run, ie to synchronize with the system time, when it interacts
with external signals : sample CPU request, sample sound request, push to video. refresh is purely
internal to memory, so nextDeadline should not be concerned by refresh."*

The ADR as accepted listed refresh among Memory's four deadline counters, which contradicted §5's own
reasoning: a counter whose expiry nothing outside the chip can observe is arithmetic, not an obligation
(ADR 0008 §5). Amended in place:

- **`nextDeadline` is the minimum over three**: the next group boundary (push to Video), the next refill
  slot owed (`SLOAD` to Sound), the next external slot owed (`DTACK` to the CPU or a granted master).
- **Refresh leaves the wake list**: bookkeeping, computed on demand, anchored when logged.
- **The general rule, stated once:** *a chip needs a wake only where it meets another chip.* Memory's
  wakes are exactly its crossings — 4 000 group deliveries, ≤313 refills, and its bus answers.
- Open point 1 of `design/memory.md` softens with it: the 7.8 µs figure now shapes only the per-line slot
  budget, never an instant.

[ADR 0008](0008-the-schedulers-rules.md) §5's Memory line carries the matching correction.
