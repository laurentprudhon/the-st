# The machine loop: one queue, three entry kinds, two read shapes

The engine has no "wake" call. There is a queue ordered by instant; firing its head is the only thing
that ever happens. What needs deciding is who fills it: the chip itself, by dating an instant it
computed, or another chip, by writing a wire at an instant the waiter could not know. A *crossing* is
therefore a **subscription** — a pending undated read on a wire, whose firing instant the writing chip
chooses — and the catalogue in `design/crossings.md` is the table of those subscriptions, not a list
of triggers.

**Status**: accepted (2026-11-05), on the owner's question and confirmation. Tickets: #7 (scheduler),
#6 (crossing catalogue), #10 (walking skeleton), #11 (determinism).

> ### Correction — 2026-11-11 (ticket #31, owner's ruling)
>
> The virtual chip this file calls *Memory* is the **System Control Unit**, and the DRAM and the TOS
> ROMs are no longer members with pins: they are plain data structures (`Ram`, `Rom`) inside it —
> stores with no timeline, no record, no subscription and no wake. Every statement below about the
> counter, the owner equation, the clients, the beam, the buffer or the leaf stands unchanged; only
> the name and the store boundary moved. Owner's words: *"GSTMCU is too cryptic when you want to
> understand the machine, what about keeping only three letters of the name : SystemControlUnit ?"*
> See [ADR 0014](0014-the-stores-are-data-structures.md) and `design/system-control-unit.md`.

> ### Revision — 2026-11-12 (ticket #34's build, owner's ruling)
>
> Invariant 3 said *"a suspended coroutine has exactly one pending read, hence at most one
> subscription"*. #34's build showed that no real chip of this machine fits it: the System Control Unit
> owns its beam **and** answers the CPU's bus cycle, and Timers and events owns four counters **and**
> answers the register selects. With one obligation one of the two must lose its own instant — either a
> structural event is stamped at the instant the bus woke the chip (backdating, which invariant 1
> forbids) or a bus answer waits for the next structural event, which invents the wait state this whole
> ADR exists to avoid.
>
> **Owner's words: *"ok with the engine generalization."*** (2026-11-12, on the gap filed as a comment on
> map #32.)
>
> What the engine does now: a chip arms **n** wakes under one continuation — dated resumes and
> subscriptions — and the **first to fire resumes it**, sets its clock, and every other armed wake
> becomes stale and is dropped. Invariant 3 becomes:
>
> > **3.** A suspended coroutine owns at least one pending wake, and each wake it arms is either a dated
> > resume or a subscription on one wire. Wakes are fire-once: the first to fire owns the resume, and
> > the rest are stale.
>
> Invariant 4 gains the discriminator the build needed: a subscription fires on a record at or after its
> instant **and after the sequence number it had already seen** (`WaitFor(t, wire, afterSeq)`). Without
> it, two records at one instant — or a chip re-arming at the instant a record landed — are
> indistinguishable, and a chip resolves on the stale record and spins.
>
> Assertion 2 (`design/scheduler.md` §4) is restated with it: *every suspended chip owns at least one
> live wake, and no live wake belongs to a chip that is not suspended* — counted from the chip's own
> armed counter on one side and the engine's live-wake count on the other, with a sampled full recount
> from the queue and the subscription store so the two sides stay independent.
>
> Cost: a chip may now hold several queue entries for one resume, so the queue carries stale entries
> until they pop. The engine counts them (`stale_wakes`) and reports the number; nothing decides anything
> by it.

## Context

- ADR 0003 fixed the ordering, the queue and the ownership of a chip's clock, and left two things
  underspecified. The owner's question (2026-11-05) exposed both: *"if we always run the chip with the
  oldest timestamp, is a wake still a concept to trigger the execution of the next part of the
  coroutine of a chip?"*
- **Gap 1 — a chip cannot date an instant it does not decide.** ADR 0003 invariant 5 said every `Read`
  names an instant. But a Shifter waiting for a group of words, a CPU waiting for `DTACK`, and a CPU
  waiting for `BG` all wait for *another chip's* decision. With only dated reads, every one of them
  would have to poll, and the tick table would return through a side door.
- **Gap 2 — "only class 1 makes a chip run" is false for the CPU.** The 68000 cannot know how long
  the System Control Unit takes; `DTACK` must reach it through the queue. ADR 0005 already bills ≤ 40 000 bus cycles
  per frame at ~4 wire operations each, which is a queue-insertion count, so the two ADRs were
  describing the same traffic with different words.
- The resolution is that *wake* and *crossing* are different concepts: the queue entry is universal,
  the crossing is a semantic class — the subscriptions that start new work rather than answer an open
  exchange.

## Decision

### Three queue entry kinds, and nothing else

| Kind | Created by | Meaning |
|---|---|---|
| `Resume(cont, chip, t)` | a dated read coming due; a subscription being fired | resume this coroutine at `t` |
| `ApplyWrite(wire, t, driver, mask, value)` | a write dated in the future | append this record at `t` and fire subscriptions |

A write dated "now" is applied inline (log append + subscription drain) rather than queued; only
future-dated writes occupy the queue. The one exception is an **awaited** write, `await WriteAndWait`:
it appends inline and then pushes the writer's own `Resume` at the same instant, so the writer's next
instruction runs behind its record's receivers (ADR 0008 §2).

### Two read shapes

- **`ReadAt(instant, wire)` — dated, a self-wake.** The chip computed the instant: the next DRAM slot,
  a timer expiry, a group boundary, a FIFO deadline. If the instant is already reached, the value is
  resolved on the chip's own stack and it never suspends (ADR 0002's fast path). Otherwise it enqueues
  a `Resume` at that instant and does not care who writes anything.
- **`ReadNext(from, wire)` — undated, a subscription.** The chip asks to be resumed at the instant the
  wire next changes at or after `from`. If a record already exists at or after `from`, the read
  resolves on the stack. Otherwise the continuation is registered in `subs[wire]`.

That is the whole primitive set a chip knows: `Write`, `ReadAt`, `ReadNext`, `advance`.

### The loop

```
while Q not empty:
    (t, prio, _) = Q.peek();  item = Q.pop();  machineTime = t
    match item:
      ApplyWrite: log.append(wire, t, driver, mask, value)
                  for s in subs[wire] where s.from <= t:      # each fires once
                      subs[wire].remove(s)
                      Q.push((t, seq++), Resume(s.cont, s.chip))
      Resume:     chip.clock = t                              # drops the chip's held date, if any
                  run chip until it suspends (one `Read`) or finishes an iteration
```

### Ordering, stated once

The queue is totally ordered by `(instant, emitter sequence)`; `seq` is a monotonic *arrival* counter
assigned at insertion — a second, independent counter from the record's, since #39's ruling (ADR 0007)
— and the chip-priority field is gone: the machine has no priority table. A chip that
writes "now" while running at instant *t* lands after the records it appended before suspending, because
those were applied inline — which is the same-tick rule, no barrier and no participant count.
Subscriptions fired by one write are pushed in registration order, so a double firing is stable too.
The only same-instant ordering chip code can express deliberately is `await WriteAndWait`, which appends
a record and pushes the writer's own `Resume` behind its receivers (ADR 0008 §2).

### What a crossing is, now

- **Class 1 — a crossing.** A subscription whose firing *starts new work* in the receiver: the video
  group delivery, the sound FIFO demand, `DE` into Timer B, `IPL` level changes, the MFP's `IRQ`, the
  arbitration handshake, a register write's chip select. Its rate is set by the writer, and it is
  unbounded.
- **Class 2 — an answer inside an open exchange.** There *is* a subscription (`DTACK` into the CPU), but
  the subscriber is the chip that opened the exchange and the instant is bounded by the answering
  chip's own schedule. It starts no new work. This is why it can be inlined later without changing a
  single instant.
- **Class 3 — phase.** A clock is not a wire: a chip derives it by arithmetic on machine time. It
  produces no records and no subscriptions, however fast it is.
- **Data.** Address and data buses are wires with records and no class-1 subscription. A subscriber
  that needs the value samples the data wire at the instant of the control record it was woken by.

## Invariants

1. Every read names the earliest instant it will accept — `ReadAt` exactly, `ReadNext` as `from` —
   and never earlier than the issuing chip's clock.
2. Every queue entry is a `Resume` or an `ApplyWrite`. *(2026-11-14, #40: a chip's own date is no
   longer a queue entry at all — a fire is, because a writer chooses its instant; a date the chip
   computed is held in the chip and the loop takes its minimum over the chips' dates each batch.
   See invariant 3's revision below and `design/scheduler.md` §2.)*
3. A suspended coroutine owns at least one pending wake — a **held date** or a subscription on one
   wire — and its wakes are fire-once: the first to fire owns the resume, the rest are stale. *(Revised
   2026-11-12, above; it read "exactly one pending read, hence at most one subscription". Revised
   2026-11-14, #40: the dated wake used to be a queued `Resume`, and about half of the ones the System
   Control Unit armed were then thrown away when a wire fire resumed it first — 8.2 M stale resumes on
   a 300-frame boot. The date now lives in the chip, so a losing date is never enqueued at all.)*
4. A record fires every subscription whose `from <= instant` **and whose sequence number is below the
   record's**, each exactly once. *(Revised 2026-11-12: the sequence discriminator is what makes two
   records at one instant — or a chip re-arming at that instant — unambiguous. Revised 2026-11-14, #39:
   "the record's sequence number" is the record counter's, which no queue entry draws from; the queue's
   key has its own arrival counter — see ADR 0007's revision.)*
5. Order is total: `(instant, seq)`; a chip's newly created same-instant entries follow their creator.
6. A chip's clock advances only to an instant it was resumed at, or by its own `advance`.
7. No chip waits on another chip's *progress*: a subscription is answered by a record, never by a
   handshake, a watermark or a count of participants.
8. A read never returns a value that a later record would change (ADR 0004, invariant 5).
9. Every coroutine iteration suspends at least once — on a read, or on an awaited write when the
   iteration has no read — so a chip's clock runs ahead of machine time only to its next read or write,
   and the scheduler always regains control (ADR 0008 §3).

## Consequences

- **#7 is this loop.** Its measured cost is queue pops per emulated second, and one CPU bus cycle
  costs ~4 (the memory wake, two future writes, the CPU's resume) before the fast path.
- **#6 is the subscription table.** Every class-1 row names the wire, the driver, the subscriber, the
  resolver, the wake condition and the rate; class-2 rows are listed too but marked "answers an open
  exchange"; class-3 clocks are not listed as wires at all.
- **The deferred memory leaf is a substitution, not an exception.** It replaces four queue entries with
  one function call that appends the same records at the same instants — hence the acceptance test
  "identical trace, fewer instructions".
- **Deadlock is still impossible**, and now for a visible reason: a chip suspends only on an instant it
  will accept, so it always moves forward; and a subscription nobody writes is a liveness bug with one
  owner (the writer that never wrote), not a protocol failure.
- **Amends ADR 0003** in two places: invariant 5 (a read names the *earliest* instant it accepts, not
  an exact one) and the Consequences sentence "only class 1 is a reason for another chip to run", which
  becomes the class-1/class-2 distinction above.
- **Vocabulary**: *subscription* enters `GLOSSARY.md`; *wake* is retired as a synonym for it.

## Considered options

- **Keep only dated reads and let receivers poll.** Rejected: the Shifter and the CPU would re-check
  wires on a fixed grid, which is the per-tick barrier with extra steps, and the wire-event budget would
  silently become a poll budget.
- **Let any wire write wake any awaiter (an open set).** Rejected as the spec: the wake rate would then
  depend on how each chip's code happens to be written, and the catalogue would stop being the
  contract. It remains true *mechanically* — the catalogue is what bounds it.
- **Make class 2 completions purely computed latencies with no queue entry.** Rejected as the *model*:
  it forces the receiver to know an instant the answerer decides, which is Gap 1 again. It survives as
  the inlined leaf, where the answerer's inline function returns the instant along with the value.

## Falsifier

1. The queue head moving backwards at any point means invariant 5 or 6 is broken, and with it
   determinism.
2. A run that terminates with a chip still suspended on a subscription, and no chip able to write that
   wire, is a liveness bug — the trace names the wire and the waiter.
3. A trace that differs between the inlined leaf and the queued path, beyond decimated display records,
   means the leaf changed an instant and the substitution is not valid.
4. If the measured pop count per frame is much above the catalogue's class-1 rate plus ~4 per bus
   cycle, some wire is carrying a subscription the catalogue does not know about — fix the catalogue,
   not the loop.

## Revision — 2026-11-06

Ticket #7 completed this loop's contract without changing its mechanism:

1. **The ordering key lost its middle field**: `(instant, seq)`; the machine has no priority table
   (ADR 0007). The pseudocode, the "ordering, stated once" paragraph and invariant 5 are amended in
   place.
2. **A second write shape joins the first**: `Write` (append, continue) and `await WriteAndWait`
   (append, then resume behind the record's receivers). Value resolution and ordering are identical in
   both; only the writer's resume point differs, which makes it a local coroutine detail rather than a
   model change (ADR 0008 §2).
3. **A progress invariant is added** (invariant 9): every iteration suspends at least once, which is
   what bounds a chip's run-ahead horizon and keeps the scheduler in control.
4. **The store, the batching rule, the obligation list and the digest** are specified in ADR 0008; the
   implementable shape is `design/scheduler.md`.
