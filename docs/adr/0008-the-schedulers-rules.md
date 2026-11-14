# The scheduler's rules: a store, two writes, a horizon, and a digest

The loop ADR 0006 described needs six rules to be implementable without another design question: how
subscriptions are stored and fired, the two write shapes and what separates them, how far a chip may run
ahead of global time, that records are never coalesced, which obligation a chip must register and what
catches a forgotten one, and what proves the whole thing deterministic. Its seventh rule bounds the
deferred memory leaf — the one place the scheduler may be bypassed — in scheduler terms.

**Status**: accepted (2026-11-06), on the owner's answers during ticket #7's grilling session.
Tickets: #7 (scheduler), #10 (walking skeleton), #11 (determinism), #8 (suspension-cost prototype).

> ### Correction — 2026-11-11 (ticket #31, owner's ruling)
>
> The virtual chip this file calls *Memory* is the **System Control Unit**, and the DRAM and the TOS
> ROMs are no longer members with pins: they are plain data structures (`Ram`, `Rom`) inside it —
> stores with no timeline, no record, no subscription and no wake. Every statement below about the
> counter, the owner equation, the clients, the beam, the buffer or the leaf stands unchanged; only
> the name and the store boundary moved. Owner's words: *"GSTMCU is too cryptic when you want to
> understand the machine, what about keeping only three letters of the name : SystemControlUnit ?"*
> See [ADR 0014](0014-the-stores-are-data-structures.md) and `design/system-control-unit.md`.

> ### Correction — 2026-11-11 (owner's ruling, same session)
>
> This chip is the **Timers and events chip** (`TimersAndEvents`). It was *Timers and interrupts* in the
> partition and *Timers* in every operational artefact — a half-renamed vocabulary, which is what made
> the name worth settling. Owner's words: *"maybe we could name it : TimersAndEvents ?"*, on the rule
> that at this stage the chip **selects and names an event**, and presenting it to the CPU as a level is
> the System Control Unit's act. The block that does the selecting is the **event selector**; the file
> `design/timers.md` is now `design/timers-and-events.md`. No decision, rate, record or instant changed.

## Context

- ADR 0006 fixed the queue's two entry kinds and two read shapes. Ticket #7 inherited the rest: the
  priority table (answered by ADR 0007: none), the store's mechanics, batching, the lazy-advance
  obligation, determinism, and the boundary of ADR 0005's leaf.
- The owner added two requirements mid-session that this ADR absorbs: **two write shapes** in the
  chip-facing API — "one that is synchronous — the coroutine has other things to run at the same
  instant, and one asynchronous which will be awaited — the next instruction of the coroutine needs to
  be executed after the write" — and **internal state as logs**: "we also want to be able to observe the
  internal state of the chips … model the internal registers/counters/flags as logs of successive values
  with timestamps, and write values to them only with a timestamp of the internal chip time."
- The digest exists because determinism is the one property that cannot be checked by looking at the
  screen: a model can be deterministic by construction and still read host order through a thread pool,
  a dictionary's iteration order, or an allocation-timing accident. A per-instant digest turns "two runs
  differed" into "this instant, this record".

## Decision

### 1. The subscription store

- **Per wire, sorted by `from`.** `subs[wire]` holds `{from, chip, continuation}` ordered by `from`. It
  stays short by construction: the catalogue is the closed set of subscriptions and one coroutine has
  exactly one pending read (ADR 0006 invariant 3), so a wire's list is bounded by its catalogue rows —
  one subscriber for almost every net, two for `DE`.
- **Fires on append, never after.** Applying a record at instant `t` removes every entry with
  `from <= t` and pushes their `Resume` entries in **registration order** (the order the chips
  suspended in — deterministic, since the instant order is). A subscription registered while a record at
  the same instant is already applied is not fired by it: `ReadNext(from = t)` resolves on the stack
  instead of registering, so a write can never re-enter its own firing pass.
- **Removal only by reset.** Nothing else abandons a read: an interrupt arriving while the CPU waits on
  `DTACK` does not cancel it, and a resumed chip has already consumed its one read. Debug assertion:
  `suspendedChipCount == pendingReadCount + pendingWriteAndWaitCount`, every instant.
- **It publishes two things.** (a) The **retention watermark** ADR 0004 needs: `min(from)` over
  `subs[wire]` — records before it can never be truncated. (b) The **subscriber set** per wire, checked
  against `design/crossings.md`: a subscriber absent from the catalogue is a bug, and a class-1 row
  whose subscriber is missing while its chip is suspended is a liveness bug.

### 2. Two write shapes, one pair of names

| Call | Suspends? | When the coroutine's next line runs |
|---|---|---|
| `Write(t, …)` | no | at the chip's own clock, in the same run |
| `await WriteAndWait(t, …)` | yes | at `t`, behind whatever that record — and that instant — set running |

- **Value resolution and ordering are identical in both forms.** The same record, the same instant, the
  same driver/mask/value, ordered by `(instant, seq)` (ADR 0007). The only difference is the writer's
  resume point, which is a local coroutine detail.
- **`WriteAndWait` is the local expression of hardware phase**: a raster register write that must land
  before the group delivery in its own slot, a bus turnaround where the driver samples the resolved
  value it just drove, a select that must precede the target's reaction. It costs a suspension and a
  queue entry, which is the guard against using it as a general tie-breaker.
- **Two names, not an overload**: the suspension is a cost the call site must show.
- **No `RegisterWake`.** A third timing primitive would re-create the tick table by the side door.

### 3. The run-ahead horizon

- A chip's clock may sit **ahead of global machine time** (global time is the queue head) and may run
  ahead only to its next point of contact with the world: its next `Read` — a mandatory await, since a
  future value cannot be invented — or, in an iteration that contains no read, its next write, which
  must then be a `WriteAndWait`.
- The reason for the second half is progress, not taste: a chip that could write into the future and
  keep running would loop at one instant emitting unbounded future-dated writes while the scheduler
  never regained control.
- So the bound on lazy advance is **the chip's own next read or write**, computed by its own logic —
  not a global quantum, not a lookahead window. Debug builds cap writes per resume; a chip that exceeds
  it has a loop with no suspension in it.
- This is the ticket's answer to "can a chip run ahead until it next touches a shared resource, and how
  is it bounded": yes, and the bound is structural — **every coroutine iteration suspends at least
  once**, visible in the code and assertable at runtime.

### 4. No coalescing

- **Every write is a record.** Several records at one instant is normal, not an optimisation target. The
  only places several signals become one record are already decided: the lane mask (a bus is one wire
  with masked lanes, ADR 0004) and the video group (one record per four words, #6).
- A chip may legitimately drive the same wire twice at one instant (two records; the resolver decides —
  last writer wins on a single-driver net). That is a feature, not an artefact to optimise away.
- The gain from coalescing is nil: a write dated *now* already costs no queue entry, so the scheduler
  round-trip is per `Read`, not per write. The cost is the property ADR 0004 exists for — the log is the
  machine's history, and a hidden intermediate record is a wrong graph and a wrong oracle.

### 5. The obligation, and what catches a forgotten one

- **An obligation is a pending read.** "Register your obligation" ≡ `await ReadAt(expiry)`; for a
  deadline with no wire to sample, the chip reads the one it already owns. Nothing to forget to call
  separately from the read that keeps the chip alive.
- **A counter whose expiry changes nothing observable needs no wake at all** — it is computed on demand
  (`advance` to the reader's instant). That is overview §10.3 rule 3, and it is what keeps the queue at
  ~4 700 instead of ~640 000.
- **The forgotten obligation is a chip suspended on a subscription while its own deadline falls due** —
  the case no fired read catches. Three checks, cheapest first:
  1. **Debug-only `nextDeadline`**, one nullable field per chip, set where the logic computes it and
     cleared when consumed. Every instant: *if set, a queued `Resume` for that chip exists at or before
     it.* It is declared on exactly the five chips that own a counter publishing an expiry — **System Control Unit**
     (min over: next group boundary, next refill slot owed, next external slot owed — refresh excluded,
     2026-11-09), **Timers and events** (min over the three counters that can assert a pending bit — A, B, C; D is
     arithmetic while the USART is a stub, 2026-11-11), **Sound** (FIFO underflow deadline, `*SINT` frame end), **Disk** (command durations,
     sector period, DMA byte cadence — unused while the drive is a stub), **Controllers** (ACIA framing,
     paddle `ACC` window). It is **not** declared on **CPU** (every instruction ends in a read, so its
     self-wake already is the obligation), **Video** (driven by deliveries, selects and levels; its
     output is phase and the host reads the log) or **Blitter** (each access is a read; cooperative mode
     is a count, not a deadline).
  2. **A per-chip rate test against the catalogue** — Timer B 200 counts, sound ≤ 313 refills, video
     4 000 groups, `DE` 400 wakes (two subscribers). The digest proves ordering; only a count proves
     something happened.
  3. **The structural invariant** above: a chip suspended with nothing queued and nothing subscribed is
     an immediate assertion failure, not a hang.

### 6. Internal state is logged like a wire, and the digest is a fold over records

- **Internal registers, counters and flags are value logs** — the same append-only `(instant, value)`
  type as a wire, differing in one column: one driver (the chip itself), hence no resolver.
- **Stamped with the chip's own clock, never scheduled.** An internal write happens inside the chip's
  own run; it is not a crossing, has no subscriber, no catalogue row and no queue entry, and cannot be
  backdated — so a per-chip log is strictly ordered and carries no ordering question at all.
- **Anchors, not samples, for counting registers.** A refresh counter or a PSG tone counter changes at
  MHz rates; log the instants at which its value becomes *semantically* distinguishable (reload,
  expiry, a software write) and let every other instant be arithmetic — lazy advance applied to
  observation. A register that changes only on a software write is logged on every write.
- **Declared set, on by default**, because the UI's graphs are a read of the log (ADR 0003) and a
  register never logged cannot be shown retroactively. Retention per log, same watermark rule.
- **The digest is the fold.** Because every state change is a record, the digest of an instant is an
  **incremental fold over the records appended since the last instant** — wire records and internal
  records alike, in append order. No second state walk, no per-chip `HashInto`, no reflection.
- **Batch = one instant, fully drained** (including entries born while draining it). Not per entry:
  that would make the digest depend on how work happened to be grouped.
- **Contract**: same image, twice, byte-identical digest streams at T=1; later, T=1 vs T>1 identical
  too. Debug builds and the test harness only — never release.
- **Randomised delay injection may touch host scheduling only** — thread-pool dispatch, yields, GC
  pressure, frame pacing. It may never touch an instant, a record's contents or the ordering key. That
  is the point of it: perturb everything the runtime chooses, so any difference in the digest proves the
  model read something host-side.
- **A queue-head monotonicity assertion** fires at the exact instant anything reorders, rather than
  4 000 batches later in a digest diff.

### 7. The memory leaf's scheduler boundary

ADR 0005 allowed a fast path that inlines the memory bus and forbade it changing "a timestamp, an
ordering or a release instant". Stated as scheduler conditions, such a leaf:

1. substitutes **exactly one closed exchange** — a CPU bus cycle and the System Control Unit's answer; not a fetch, not a
   grant, not an interrupt;
2. moves **both clocks to the answering instant** — the CPU's as always, and **the System Control Unit's as if it had
   been resumed**, or its next interaction computes from a stale instant;
3. **appends every record in full** — same instants, drivers, masks, values, including the `Z` release
   after a read;
4. fires only where **the answer needs no other chip to run** (a class-2 exchange whose instant is
   computable from the System Control Unit's own slot counters);
5. touches **no subscription but the CPU's own**;
6. leaves the **digest stream identical** apart from its declared display decimation;
7. is **T=1 only and off by default**, so the reference path stays the one that runs until a measurement
   says otherwise.

The owner's position on it, recorded verbatim because it is a constraint on every future session:
*"i hope we will not need it, it should only be a very last resort, because it would really cripple the
cpu and memory coroutines logic and readability"*

**Status, 2026-11-14 (#38).** ADR 0005's falsifier has fired on measurement, so the leaf is **accepted and
deferred**: the conditions above are its acceptance test, not an invitation to start. Two tickets precede
it — #39 (the sequence counter is both the queue key and the record's identity) and #40 (the losing arm) —
because all three change how many queue entries a frame has, and condition 6 ("leaves the digest stream
identical") cannot be witnessed until #39 rules on that counter..

## Invariants

1. Order is `(instant, seq)` (ADR 0007); nothing else participates.
2. A coroutine's next line runs at its own clock after `Write`, at the record's instant after
   `WriteAndWait`.
3. Every coroutine iteration suspends at least once; a chip's clock runs ahead only to its next read or
   write.
4. A record is never coalesced, and a read never returns a value a later record would change.
5. An obligation is a pending read; a suspended chip is always the owner of a **held date** (#40), a
   queued resume, a subscription, or an awaited write — the debug assertion rebuilds the count from all
   four, and the engine's own count of held dates is checked against the chips that hold one.
6. Every observable state change is a logged record; the digest is a fold over records.
7. The leaf substitutes pops, never a clock movement, a record, or another chip's subscription.

## Consequences

- **#10** implements the loop with these assertions, the digest stream, and the 40 % bill of ADR 0005 as
  its measurement; the leaf stays off, and the owner expects it to stay off.
- **#11** owns the digest contract: two runs at T=1, then T=1 vs T>1, with the host-injection allow-list
  as its only permitted perturbation.
- **#8** measures exactly two things this ADR created: the cost of an awaited write (a suspension plus a
  queue entry at the same instant) and the cost of the fast-path read.
- **#20** (memory subsystem) inherits the five-chip deadline list and the anchors-not-samples rule for
  its counters.
- **Vocabulary**: `WriteAndWait`, *run-ahead horizon*, *batch*, *digest* and *memory leaf* enter
  `GLOSSARY.md`.

## Considered options

- **An explicit `RegisterWake(t)` primitive.** Rejected: a second way to say what a read says, and the
  tick table returns through the side door.
- **Coalescing a chip's same-instant writes into one record or one queue entry.** Rejected: no gain (a
  same-instant write already costs no queue entry) and it erases the log's intermediate states.
- **Hashing full state per instant instead of records.** Rejected: costs proportionally to how much
  state exists rather than to what changed, and needs a reflection-style state walk per chip that drifts
  from the state it claims to describe.
- **A per-frame digest instead of per-instant.** Rejected: it says *that* a frame differs, not *where*,
  which is the difference between a one-line diff and a day of bisecting.
- **Dropping the chip-priority field but keeping a per-chip ordering in the store's firing.** Rejected:
  one rule or none; firing follows registration order.

## Falsifier

1. A digest stream differing between two runs of one image at T=1 means the model read host state —
   the instant the streams part names the culprit.
2. A debug `nextDeadline` assertion firing, or a per-chip rate test landing off the catalogue's count,
   means an obligation was not expressed as a read.
3. A digest that changes when host delays are injected means a host read leaked into the model.
4. Enabling the leaf and seeing one different instant, record or digest means the leaf is wrong and it
   is removed, not tuned.
5. A chip needing a third write shape, or a `RegisterWake`, is a sign the two shapes and the horizon are
   insufficient — reopen here rather than adding a primitive.

## Revision — 2026-11-09 (ticket #20): the System Control Unit's refresh is not a deadline

The System Control Unit row above listed refresh among its four deadline counters. Ticket #20's owner ruling corrected
it — *"memory only needs to run … when it interacts with external signals : sample CPU request, sample
sound request, push to video. refresh is purely internal to memory, so nextDeadline should not be
concerned by refresh"* — and the correction is §5's own first principle applied consistently: a counter
whose expiry nothing outside the chip can observe is computed on demand, never registered as a wake.

- **The System Control Unit's list is three**: next group boundary (push to Video), next refill slot owed (`SLOAD` to
  Sound), next external slot owed (`DTACK` to the CPU or a granted master). Refresh is bookkeeping.
- **The rule the case generalises to**, and the wording future chips should be read against: *a chip needs
  a wake only where it meets another chip.* A counter that changes nothing another chip can observe is
  arithmetic, however fast it runs.
- Nothing else in this ADR moves: the five chips keep the field, the assertion keeps its wording, and the
  anchors rule is unchanged — refresh is still anchored when logged, it simply schedules nothing.
- [ADR 0011](0011-memory-owns-the-slot.md) §5 and its revision carry the same correction, and
  `design/system-control-unit.md` §9/§12 is the implementable shape.

## Revision — 2026-11-11 (ticket #28): the chip's deadline list is three

§5's row read "min over the four MFP counters". Ticket #28's owner ruling retired one of them: **Timer D's
only job is the USART baud clock, and the USART is a declared stub** (RS-232 is out of scope on the map), so
D's expiry changes nothing another chip can observe. This ADR's own first principle applies — *a counter
whose expiry changes nothing observable needs no wake* — and the chip's field is now the minimum over **A, B
and C**.

- D still counts, still reads back, and still anchors its expiries; it schedules nothing.
- Reversal is additive: a serial ticket gives the USART a producer and puts D back on the list.
- Nothing else moves: the five chips keep the field, the assertion keeps its wording, and
  `design/timers-and-events.md` §2/§8 is the implementable shape.
