# The scheduler — the loop, the store, the assertions

Implementation-facing companion to ADRs [0006](../docs/adr/0006-machine-loop-subscriptions.md),
[0007](../docs/adr/0007-same-instant-order-is-arrival-order.md) and
[0008](../docs/adr/0008-the-schedulers-rules.md). Decisions live in the ADRs; this file is the shape the
code takes, so #10 has nothing left to interpret. Chip names are the eight from
`design/virtual-chips.md`.

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

## 1. The chip-facing API

```
advance(n)                          // the chip's own clock moves by n ticks
Write(t, wire, mask, value)         // append a record at t; continue at the chip's clock
await WriteAndWait(t, wire, mask, value)  // append a record at t; resume at t, behind its receivers
await ReadAt(t, wire)               // dated read: resolves on the stack if t <= clock
await ReadNext(from, wire)          // subscription: resume me when this wire next changes at/after from
```

That is the whole surface. No sleep, no lock, no yield, no host clock, no `RegisterWake`, no scheduler
handle. Chip code reads as straight-line logic with five verbs in it.

**Deciding between `Write` and `WriteAndWait`** — one question: *does my next instruction depend on what
the receivers of this write did at this same instant?* Raster register write before the group delivery
in its slot: yes. Bus turnaround where I sample the value I just drove: yes. Everything else: no.

**A chip's clock may be ahead of machine time** (machine time is the queue head). It may run ahead only
to its next read, or — in an iteration with no read — to a `WriteAndWait`. An iteration with no
suspension at all is a bug, and the debug write cap per resume says so.

## 2. The queue

Two entry kinds only (ADR 0006):

```
Resume(cont, chip, t)
ApplyWrite(wire, t, driver, mask, value)
```

Key: `(t, seq)` — `seq` monotonic, assigned at insertion. No chip rank, no priority (ADR 0007).

```
while Q not empty:
    (t, _) = Q.peek()
    machineTime = t
    assert t >= previousT                    // head monotonic — fails at the instant, not later
    batch = records appended while draining every entry stamped t
    while Q.peek().t == t:                   // one instant, fully drained, born entries included
        item = Q.pop()
        match item:
          ApplyWrite: append(wire, t, driver, mask, value)
                      for s in subs[wire] where s.from <= t, in registration order:
                          subs[wire].remove(s)
                          Q.push(Resume(s.cont, s.chip, t))
          Resume:     chip.clock = t
                      run chip until it suspends
    digest(fold over batch)                  // debug/test only
```

- **Applying a record fires `subs[wire]` and nothing else.** No waking by count, watermark or
  participation.
- **A `Resume` sets the chip's clock to `t` before running it**; the chip cannot read or write an earlier
  instant (assert it).
- **`WriteAndWait` at instant `t`** appends inline and pushes a `Resume` for the writer at `t`, so it
  lands behind the firings of its own record — the only same-instant ordering chip code can express, and
  it is local.

## 3. The store

```
subs: Map<Wire, List<{from, chip, cont}>>   // ordered by from
pendingReads(cont)                          // one per suspended coroutine (invariant 3)
```

- **Fires on append only.** A `ReadNext(from = t)` that finds a record at or after `t` resolves on the
  stack and never registers — so a write cannot re-enter its own firing pass.
- **Registration order is firing order.** No rank.
- **Removal only on reset/shutdown.** Nothing abandons a read mid-run.
- **Publishes:**
  - `retentionWatermark(wire) = min(from)` over `subs[wire]` — the log may never truncate across it
    (ADR 0004); with no subscriber, the UI's trace window decides.
  - `subscribers(wire)` — compared against `design/crossings.md` in debug builds.

### 3a. How retention is done (#34)

- **A log is a window over one array**, with a head offset: discarding from the oldest end is a pointer
  move, and the array compacts when the head has eaten half of it. A long boot appends tens of
  millions of records; without this the tree's memory is unbounded (`ValueLog._records` was
  append-only for ever, and a Debug boot was OOM-killed past ~400 frames at 2.6 GB).
- **The watermark is the minimum over subscribers *and* live sampling windows** (ADR 0004's
  2026-11-07 revision) — **and it is per log** (#41): each log is trimmed against the minimum `from` of
  *its own* subscribers, because only they can ask it anything, with the machine-wide sampling floor
  applied to every log. A subscriber's `from` is a floor, but three readers take no subscription and
  still ask for an instant in the past: the CPU's `IPL` sample for the end of an instruction, the video
  chip's four `MD` words behind a group delivery, and any `ReadAt` at or before the chip's own clock.
  They announce the instant with `Engine.NoteSampled(instant)`; the floor holds until the batch ends,
  by which time the sample has been answered on the reader's own stack.
  <br>One minimum over *every* wire was the first implementation, and it let the slowest waiter in the
  machine size the memory of all logs: on the boot `bus` held 383 792 records while its own readers never
  needed more than 80, because a Timers and events waiter sat 3.84 frames in the past on `MFPCS`.
  Measured, and the reason the watermark is per log.
- **Truncation keeps one base record** — the newest at or before the watermark. That record is what
  keeps invariant 5 true for every instant from the watermark onwards: the answer to a sample in that
  range is either in the retained window or *is* the base. A sample older than the window returns
  "nothing" (0), which is not a wrong value — the past is unreachable, not misreported.
- **Cursors are sequence numbers, never indices.** An index saved across a suspension names a different
  record once anything was trimmed in between; `SystemControlUnit`'s bus exchange and `TimersAndEvents`'
  `DE` scan both stored indices and both broke the moment retention ran (a dead bus: three cycles
  started, no answer, the CPU suspended).
- **Cadence is an implementation detail**: every 16 batches, fine enough that the retained window tracks
  the machine, coarse enough not to walk the logs per instant. G7 (`check-retention`) proves the rule
  rather than the cadence.

## 4. Debug assertions (all off in release)

| # | Assertion | Catches |
|---|---|---|
| 1 | queue head non-decreasing | an ordering bug, at the instant it happens |
| 2 | `suspendedChips == pendingReads + pendingWriteAndWaits` | a chip suspended with nothing queued — a lost obligation |
| 3 | `nextDeadline != null ⇒ a Resume for that chip is queued at or before it` | the forgotten self-wake; on System Control Unit, Timers and events, Sound, Disk, Controllers only |
| 4 | `subscribers(wire)` ⊆ the catalogue's rows for that wire | a subscription nobody documented |
| 5 | every class-1 row's subscriber present while its chip is suspended | a liveness bug with one owner |
| 6 | writes per resume below a cap | a loop with no suspension in it |
| 7 | no read or write earlier than the issuing chip's clock | a backdated instant |

`nextDeadline` is declared on exactly five chips, and it is the min over that chip's own counters:

| Chip | Its counters | Why it alone owns an expiry |
|---|---|---|
| System Control Unit | next group boundary, next refill slot owed, next external slot owed (**never refresh** — 2026-11-09) | the slot schedule is the machine's master timing |
| Timers and events | four MFP counters (A/B/C/D) | expiries assert `IRQ` |
| Sound | FIFO underflow deadline, `*SINT` frame end | the sample clock is its own |
| Disk | command duration, sector period, DMA byte cadence | the platter's clock; unused while the drive is a stub |
| Controllers | ACIA framing, paddle `ACC` window | a byte completes at a serial instant |

Not declared on **CPU** (its instruction end is already a read), **Video** (delivery-driven; output is
phase), **Blitter** (each access is a read; cooperative mode is a count).

## 5. The digest

- **Batch = one instant, fully drained.**
- **Digest = incremental fold over the records appended in the batch**, wire and internal alike, in
  append order — including the internal register logs, which is why no state walk is needed.
- **Contract:** same image, twice, byte-identical streams at T=1; later T=1 vs T>1 identical.
- **Host injection** (thread-pool dispatch, yields, GC pressure, frame pacing) must not change the
  stream. Anything that touches an instant, a record or `seq` is forbidden.
- **On divergence**, re-run with the full record stream enabled for the last N batches; the first
  differing record is the bug.

## 6. Internal state logs

Same append-only log type as a wire, one driver (the chip), no resolver, stamped with the chip's own
clock, never queued, never backdated.

- **Anchors not samples**: log the instants a value becomes semantically distinguishable (reload,
  expiry, software write). A counter's value between anchors is arithmetic.
- **Declared set, on by default** — the UI's graphs read these logs, and an unlogged register cannot be
  shown retroactively. Retention per log, same watermark rule.

## 7. The memory leaf (last resort, off by default)

A function inside the System Control Unit — `Serve(cycle) -> (t_answer, value)` — that the CPU calls instead of the four
queue round trips of a bus cycle, appending the same records at the same instants. Conditions, all seven
(ADR 0008 §7): one closed exchange; both clocks land at the answering instant, the System Control Unit's included;
records and the `Z` release survive in full; only class-2 exchanges whose instant the System Control Unit computes alone;
no other chip's subscription touched; digest stream identical but for declared display decimation; T=1
only, off by default.

The owner's standing position: *"it should only be a very last resort, because it would really cripple
the cpu and memory coroutines logic and readability."* A session that turns it on does so with #10's
measurement in hand, never because it seemed obviously faster.
