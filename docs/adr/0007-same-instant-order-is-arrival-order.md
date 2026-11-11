# Same-instant order is arrival order: the machine has no priority table

The scheduler's ordering key is `(instant, emitter sequence)`. There is **no chip-priority field**,
because the hardware has no table to fill: every precedence this machine actually has is expressed
either as a **resolver** on a wire (an interrupt level taken as a maximum, an open-collector line
taken as a logical OR, a contested tristate bus reported as a conflict), as the **slot phase** the
memory controller decodes for itself, or as the 68000's own mask comparison. What is left — two chips
touching unrelated wires at the same tick — has no hardware rank, so the model must not invent one.

**Status**: accepted (2026-11-06), on the owner's answers during ticket #7's grilling session.
Tickets: #7 (scheduler), #11 (determinism), #6 (crossing catalogue).

> ### Correction — 2026-11-11 (ticket #31, owner's ruling)
>
> The virtual chip this file calls *Memory* is the **System Control Unit**, and the DRAM and the TOS
> ROMs are no longer members with pins: they are plain data structures (`Ram`, `Rom`) inside it —
> stores with no timeline, no record, no subscription and no wake. Every statement below about the
> counter, the owner equation, the clients, the beam, the buffer or the leaf stands unchanged; only
> the name and the store boundary moved. Owner's words: *"GSTMCU is too cryptic when you want to
> understand the machine, what about keeping only three letters of the name : SystemControlUnit ?"*
> See [ADR 0014](0014-the-stores-are-data-structures.md) and `design/system-control-unit.md`.

## Context

Ticket #7 was charted to fill in the priority table ADR 0003 had left deliberately empty, and the
catalogue handed it three concrete ties to decide: the three `IPL` sources inside one instant
(VBL 4, HBL 2, MFP 6), two bus masters asking for the bus together (`BR` from Disk and from the
Blitter), and a video group delivery racing a CPU bus cycle in the same slot. Reading each against
the sources dissolved all three:

- **A video group cannot race a CPU cycle**, because they do not share an instant. One slot is 2 CPU
  clocks = 250 ns = **8 ticks** of the 32 MHz timebase, and internal and external slots **alternate**
  slot by slot under the `ADDRSELB` phase decode (Cwik's "two internal, two external, no priority of
  any kind", implemented in the GSTMCU reconstruction and printed on sheet 22 of the recovered design).
  A group of four words takes four *internal* slots, interleaved with four external ones
  (`research/memory-bus-slots.md` §6: 80 internal slots per active line, 4 words per 16 clocks), so the
  delivery record lands at an `I` instant and the CPU's cycle at an `E` instant, 8 ticks apart. The tie
  exists only if the model rounds a group to a line boundary.
- **The three `IPL` sources never reach the scheduler as three events.** VBL 4 and HBL 2 come from
  the System Control Unit's own sync generator, the MFP's level 6 arrives over the `IRQ` wire, and the System Control Unit resolves them
  into **one** three-lane record: the highest asserting level wins (6 > 4 > 2, the 68000's own
  precedence, overview §3.1). That is a `max()`, not an ordering — and the CPU's acceptance is then its
  own mask comparison, again not an ordering.
- **Two bus masters on `BR` produce one identical record.** `BR` is wired-OR and both `U101.13` and
  `U400.29` pull it; the record's value is the same whichever driver appends first. The grant is the
  CPU's `BG`, broadcast on the same net, and the machine has **no arbiter between two masters** — only
  "my cycle is done". A genuinely simultaneous grab would double-drive `BGACK` on real hardware.

## Decision

- **The key is `(instant, emitter sequence)`.** `seq` is monotonic and assigned at insertion; a chip's
  newly created same-instant entries follow their creator. Same-instant order is therefore *arrival
  order at that instant*, and the model is total without a rank.
- **No chip-priority field exists** — not in the queue, not in the `SynchronizationContext`, not in a
  table beside it. A rank would have to be invented, and inventing one is the error the memory research
  warns about: a priority arbiter where the hardware has none.
- **Hardware precedence lives in three places, none of them the scheduler**:
  1. **Resolvers.** `IPL` is a maximum over asserting levels; an open-collector net is an OR; a
     multi-driver tristate net is a reported conflict (ADR 0004). Value, not order.
  2. **The crossbar's phase.** Which client is served in which slot is a decode of the 8-phase counter
     inside the System Control Unit (class 3: phase, not a wire). Distinct slots are distinct instants.
  3. **The CPU's mask.** A level is taken when it is strictly greater than `I2–I0`; level 7 ignores the
     mask. Instruction-level behaviour, inside one chip.
- **Bus-grant order is request order.** `BR` is a level and the System Control Unit and the CPU keep the pending requesters in
  assertion order (earliest `BR` instant, `seq` breaking a same-instant tie). The grant goes to the
  head; `BG` released and `BGACK` released return the bus and admit the next. Two masters asserting
  `BGACK` in one instant is a **reported conflict**, named in the trace, not a silently arbitrated win.
- **What a chip may express locally, it must express locally.** Where the hardware really does order a
  write before the same instant's receivers — a raster register write landing before the group delivery
  in that slot, a bus turnaround where the driver samples the value it just drove — the *chip* says so
  with an awaited write (ADR 0008's `WriteAndWait`). The phase belongs where the phase is known.

## Invariants

1. Order is total by `(instant, seq)`; no other field participates.
2. Two entries at one instant from chips that the catalogue says exchange nothing are ordered by
   arrival, and **no behaviour may depend on which came first**.
3. A wire's value at an instant is a function of the records at that instant, never of their order,
   except where the resolver is single-driver (last record wins) or a conflict is reported.
4. The queue head is non-decreasing; ties are resolved inside the firing of the instant.

## Consequences

- **Amends ADR 0003**: its key `(machine time, chip priority, emitter sequence)` becomes
  `(machine time, emitter sequence)`, invariant 6 with it, and the `SynchronizationContext` consequence
  no longer orders by priority (ADR 0002's #7 line follows).
- **Amends ADR 0005**: its "same-instant arbitration is resolved by the priority key" bullet is
  deleted; the three real arbitrations are the `max()` resolver, the slot phase, and the reported
  conflict.
- **Fills the catalogue's open point 6** (`design/crossings.md` §11) with a negative: there is no
  ordering for the three `IPL` sources or the two bus masters to specify.
- **#11 gets a sharper falsifier**: a run whose digest stream changes when two unrelated chips' arrival
  order changes means a missing subscription, not a missing rank.
- **Vocabulary**: *Same-tick ordering* in `GLOSSARY.md` is corrected to say that the order is arrival
  order and the hardware precedence lives in resolvers and phase.

## Considered options

- **A frozen arbitrary rank** (keep the field, declare it arbitrary, "nothing may depend on it"). Kept
  as the fallback if a compatibility test ever needs one, but rejected as the design: it is a field
  every reader would try to fill, and an arbitrary value that *can* be observed *will* be depended on.
- **`video > Blitter > CPU`, "as the hardware suggests"** (the ticket's own hypothesis). Rejected: it
  is a hypothesis, and the phase decode contradicts it for the memory path — video does not outrank the
  CPU in a slot, it has its own slot.
- **Modelling the DRAM bus as a priority arbiter.** Rejected: the hardware alternates, it does not
  rank; a priority table here would invent contention the machine never had.
- **Making an interrupt level's arrival order decide which source is serviced.** Rejected: the level is
  one resolved value; which MFP source is inside is the MFP's own encoder's business.

## Falsifier

1. If a demo or compatibility test needs a specific same-instant order between two chips the catalogue
   pairs with no wire, the fault is a **missing catalogue row** — fix the catalogue, not the key.
2. If two runs of one image reorder same-instant entries differently, that is a determinism failure in
   `seq` assignment (ADR 0008's digest stream localises it), not a missing rank.
3. If the walking skeleton needs a rank to pass a test, reopen this ADR — and bring the test.
