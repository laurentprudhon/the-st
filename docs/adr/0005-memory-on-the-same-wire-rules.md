# The System Control Unit is a virtual chip, on the same wire rules as every other boundary

RAM is a virtual chip with pins like any other: the CPU drives address, data direction and control on
wires, the System Control Unit samples them at the instant they are asserted, and answers by driving data and `DTACK`
at a later instant it computes. No boundary in the machine gets a private calling convention. The
uniformity is chosen deliberately at the *functional* accuracy level, where it costs roughly 40 % of a
frame for CPU traffic alone; the inlined memory fast path is a later optimisation that must leave
every timestamp and every ordering unchanged, so it can be added without touching a single other chip.

**Status**: accepted (2026-11-04), on the owner's instruction. Ticket: #7 (scheduler), #10 (walking skeleton).

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

- The proposal as first put to the owner had RAM as a service call on the fast path —
  `mem.access(addr, kind) -> (value, cycles)` — on the argument that the machine does ~2 × 10⁶ bus
  cycles/s while it does only 10²–10³ crossings/frame, so putting RAM on the wire path would
  dominate the engine by two orders of magnitude.
- **The owner rejected the exception**: "let's just apply the same rules and definitions for the
  memory subsystem at the boundaries we have defined … we will optimise with special behaviour later
  if needed." That is a deliberate choice of one mechanism over one fast mechanism, and it is
  consistent with the project's stated value: simplicity and understandability before feature count.
  This ADR records the decision *and* the bill, because the bill is what the walking skeleton has to
  pay.
- Three facts from the existing design shape the boundary:
  - **Video and sound fetch are memory service, internal to the RAM subsystem** (owner's ruling,
    `research/pin-event-rate.md` correction). The Shifter does not sample a wire per fetched word;
    only the control signals around the stream cross (`DE`, `BLANK`, `DCYC`/`LOAD`, `SREQ`/`SLOAD`).
    The RAM chip computes its own fetch slots from its counters, by the MMU's alternation.
  - **Bus arbitration is CPU-side**, with the GSTMCU relaying a disk request into `BR`/`BG`/`BGACK`
    (`design/crossings.md`, correction of 2026-11-02).
  - **Completions are answers, not crossings** (`design/crossings.md` §"The classification rule"):
    `DTACK`, `BERR`, `RDY`, `DCYC` answer a request the same exchange already made. Under this ADR
    they are ordinary wire records — and although the answering record does resume the waiting chip, it
    starts nothing new there, which is what keeps the crossing rate where the catalogue puts it.

## Decision

- **The System Control Unit is a virtual chip.** The CPU asserts `AS`, `R/W`, `FC0–FC2`, the address lanes and (for a
  write) the data lanes; it samples them at the instant of assertion and drives data and `DTACK`
  at the instant its slot schedule allows. A read is `await Read(t_response, dataWire)`; the CPU's
  clock advance to `t_response` *is* the wait-state model.
- **Every rule of ADR 0004 applies unchanged**: append-only logs, per-driver values, `Z` on release
  (the CPU releases the data bus after a read; the moment matters and is recorded), tristate resolver
  with conflict detection.
- **Uniformity first, at the functional level.** The engine has exactly one way to move data between
  chips, so there is one way to learn, test, trace and draw it. The functional level models no
  contention inside RAM beyond the slot alternation, so uniformity costs performance and no
  correctness.
- **The bill, stated.** ≤ 40 000 CPU bus cycles/frame (`design/crossings.md` §1), each ~4 wire
  operations (one address/control record, one data record or read, one `DTACK` record, one read) →
  ~8 × 10⁶ wire operations per second of emulated time. Under ADR 0006 those four operations are four
  queue entries, which is why this is a scheduler cost and not only a logging cost. At a conservative
  50 ns each, that is ~8 ms of a 20 ms frame: **~40 % of the budget before the CPU executes an
  instruction.** The Blitter, when it is added, roughly doubles it. This figure is the acceptance test
  of #10, and the reason the fast path exists as a planned option rather than as an improvisation.
- **The deferred fast path is a leaf inside the System Control Unit, not an exception in the model.** It may inline
  the wires of the memory bus, serve a bus cycle from a direct call, and decimate its own display log.
  It may not change a timestamp, an ordering or a release instant, and no other chip may know it
  exists. Acceptance: the trace with the leaf on and off is identical except for the decimated
  display records.
- **Same-instant coupling is resolved inside the firing of an instant**, never by one chip waiting for
  the other (ADR 0003, invariant 6). This is the one place in the machine where a genuine circular wait
  could be built, and the design forbids it by construction: a grant is decided inside the firing of
  an instant, not by a suspension. There is no priority key to decide it with (ADR 0007): the resolved
  interrupt level is a `max()`, the slot choice is a phase, and a contested bus is a reported conflict.
- **The System Control Unit's internal schedule is the slot alternation, not a priority table.** Two internal slots and
  two external slots alternate, with no precedence of any kind; the internal ones carry display fetch,
  refresh and audio DMA, the external ones the CPU and any bus master. Modelling it as priorities
  would invent contention the machine never had.

## Invariants

1. No chip has a private calling convention into another; a bus cycle is wire records and samples.
2. The System Control Unit owns its own clock and its own slot schedule; nobody reads its state directly.
3. A completion's delay is a computed instant the answering chip drives at. Its record costs a queue
   entry (ADR 0006) — the entry starts no new work, which is what keeps it inlinable.
4. Video and sound fetch stay inside the System Control Unit; only their control signals cross.
5. A fast path may change the number of instructions, never an instant, an order or a visible value.

## Consequences

- **The CPU's wait states become structural**: a slow device is a chip that answers later, so the
  model needs no separate wait-state mechanism.
- **The Blitter and any bus master take the same path**, and their conflicts show up as tristate
  conflicts (ADR 0004, invariant 8) rather than as silent corruption.
- **#10's walking skeleton is also this ADR's measurement**: holding 50 Hz with headroom on the
  reference host means the uniform model is affordable, and the figure is recorded for the day
  someone proposes the leaf.
- **The temptation to special-case RAM once must be resisted explicitly** — hence invariant 1. A
  second calling convention is how an emulator ends up with two models of the same machine that
  disagree at the edges, which is exactly the failure this project's design discipline exists to
  prevent.

## Considered options

- **RAM as a service call on the fast path from the start.** Rejected by the owner: two mechanisms
  for one job at the functional level, before there is any measurement showing one is needed. Kept as
  the named deferred optimisation, with its acceptance test fixed here.
- **RAM as a chip, with the video and sound fetch also crossing pins.** Rejected: 16 000–25 000 word
  fetches per frame through wire records would put the memory bus at 10⁷ record operations per second
  in the *fetch* path alone, and the owner's earlier ruling already places the fetch inside the System Control Unit.
- **Per-chip private memory pointers (a shared RAM array every chip indexes).** Rejected: it is a
  second, unlogged path for the machine's dominant traffic, invisible to the graphs, and it makes the
  Blitter/CPU contention exactly the thing the model cannot see.
- **Modelling memory ordering as a priority arbiter.** Rejected: the hardware has no priority in the
  memory slots, only an alternation.

## Falsifier

- If the walking skeleton cannot hold 50 Hz with at least 2× headroom on the reference host, the
  uniform model is too expensive as specified and the leaf in this ADR is implemented — inside
  the System Control Unit, with the trace-identity acceptance test. That is a performance change, not a model change.
- If enabling the leaf moves a single timestamp or ordering, the leaf is wrong.
- If a chip ever wants a private handle on RAM (invariant 1 broken "just here"), the model has started
  to fork and this ADR is the place to reopen, not a code review to lose.

## Revision — 2026-11-06

Two sentences above were corrected by later rulings, and the leaf gained the boundary it was missing.

1. **"Same-instant arbitration is resolved by the priority key"** is deleted as written: the machine has
   no priority table (ADR 0007). What resolves a same-instant coupling is a wire resolver (`IPL` as a
   maximum over asserting levels), the memory slot's phase, or a reported tristate conflict — never a
   rank the engine imposes.
2. **Invariant 3** ("not a queue entry") is amended: ADR 0006 established that a completion's record
   costs a queue entry like any other, and that it starts no new work. That distinction, not the entry
   count, is what keeps the leaf a substitution.
3. **The leaf's scheduler boundary** is now stated as seven conditions (ADR 0008 §7): one closed
   exchange; both clocks land at the answering instant, the System Control Unit's included; every record and the `Z`
   release survive; class-2 exchanges only, where the System Control Unit computes the instant alone; no other chip's
   subscription touched; digest stream identical but for declared display decimation; T=1 only and off
   by default.

## Revision — 2026-11-07 (ticket #9)

**The read sentence in §Decision is superseded.** It said "a read is `await Read(t_response, dataWire)`",
which presumes the CPU knows the instant the System Control Unit will answer. It cannot: only the System Control Unit computes that instant
from its slot schedule. The cycle is instead one masked control record, one subscription to the answer
(`ReadNext`), a data sample that resolves on the stack, and the CPU's own release record; the CPU's clock
advance to the answering instant is still the wait-state model, exactly as written above. See
[ADR 0009](0009-the-68000-core-on-the-bus-contract.md) §1.

The rest of this ADR stands unchanged, including the uniformity argument, the bill, and the memory leaf's
boundary. The 4-clock figure in "wait state" language now has its precise home: the cycle's *minimum*, with
the answer instant (slot phase, overscan, bus grant) deciding the actual value — which is why the CPU's
cost is elapsed time and never a returned number.

The owner's position on ever turning it on, recorded because it binds later sessions: *"i hope we will
not need it, it should only be a very last resort, because it would really cripple the cpu and memory
coroutines logic and readability."*

## Revision — 2026-11-09 (ticket #20)

**One sentence in §Decision is corrected: "the functional level models no contention inside RAM beyond
the slot alternation".** The owner ruled that the machine is built bus-aware, with no functional step
before it — *"we should model level 2 right now, and level 2 only, no need to introduce a first step
with level 1 given our current design"*. The System Control Unit therefore models the slot schedule in full: contention
is the external slot's (a granted bus master holding it), and a contended answer is a later instant.
The uniformity argument, the bill, and the leaf's boundary stand unchanged.

[ADR 0011](0011-memory-owns-the-slot.md) fills in what the slot schedule is: one 500 ns cycle with one
internal and one external slot, one owner equation for the internal slot, three client kinds, and
refresh as filler rather than preemptor. The 40 % bill below is still #10's measurement and still the
reason the leaf exists.

## Revision — 2026-11-11 (ticket #31): the stores are inside the chip, not the boundary

The DRAM and the TOS ROMs became plain data structures (`Ram`, `Rom`) inside this chip, which is now
the **System Control Unit** ([ADR 0014](0014-the-stores-are-data-structures.md),
`design/system-control-unit.md`). What changes here, and what does not:

- **Invariant 1 stands untouched.** A call to `Ram.Read(bank, row, column)` is *inside one chip's own
  execution*, like its phase counter — not a private calling convention between chips. The uniformity
  this ADR bought is about how chips meet each other, and the stores meet nobody.
- **The bill is unchanged.** The ~40 % figure is queue entries for the CPU's *bus cycle* (control
  record, subscription, data sample, release), not for the store call behind it; #10 measures the same
  thing it did before.
- **The leaf's boundary and its status are unchanged**, and the split does not weaken the owner's
  constraint: the leaf inlines the **slot wait** — the suspension and the two clock movements — not
  the store's read. A `Ram.Read` that is one call is therefore neither the leaf nor an argument for
  turning it on.
- **The rejected option "per-chip private memory pointers (a shared RAM array every chip indexes)"
  is still rejected.** `MachineBytes` is the host's view only, in machine byte order, and no virtual
  chip may hold it (ADR 0014 §4).
