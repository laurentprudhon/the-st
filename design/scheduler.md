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

…and one thing that is deliberately **not** an entry: a chip's own date (#40). A fire is a queue entry
because a writer chooses its instant; a date the chip computed for itself is held in the chip, and the
loop takes its minimum over the chips' dates each batch — at most a handful of compares a batch, against
~27 000 enqueues and pops a frame that were then thrown away as stale.

Key: `(t, seq)` — `seq` monotonic, assigned at insertion: the queue's **arrival** counter, read by
nothing but the same-instant tie-break. Records have their own counter (ADR 0007's 2026-11-14 revision,
#39), so how many entries a frame queues cannot renumber a record or move the digest.

```
while (t = min(Q.peek().t, min over chips of chip.date)) <= endInstant:
    machineTime = t
    assert t >= previousT                    // head monotonic — fails at the instant, not later
    batch = records appended while draining every entry stamped t
    due = the chips whose date is t, in the arrival order their dates drew
    while Q.peek().t == t or due remains:    // one instant, fully drained, born entries included
        item = whichever of Q.peek() and due[0] has the smaller arrival number
        match item:
          ApplyWrite: append(wire, t, driver, mask, value)
                      for s in subs[wire] where s.from <= t, in registration order:
                          subs[wire].remove(s)
                          Q.push(Resume(s.cont, s.chip, t))
          Resume:     chip.clock = t
                      chip.date = none           // a wire fire owns the chip: a losing date is dropped
                      run chip until it suspends
          date:       chip.clock = t
                      run chip until it suspends
    digest(fold over batch)                  // debug/test only
```

A date draws its arrival number when it is **armed**, not when it fires, so its place among same-instant
entries is exactly the one a queue entry would have had. A date whose chip a wire resumed first is dropped
there and then: it is never enqueued, never popped, and never counted as a stale wake. That is #40's whole
change — 107 215.6 → 63 193.2 pops a frame, and 44 021.8 → 23 679.7 stale.

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
- **The table is an array, not a dictionary** (#38). A `Channel` is `(kind, id)` and both ids are assigned
  once, in catalogue order, so `subs` and the logs are two arrays per kind: a lookup is a subscript
  (1.4 ns) rather than a dictionary probe over a struct key (7.0 ns). On the boot's ~92 M record appends
  and ~45 M fire-path lookups that bought **1.13 ms a frame** over six *alternating* adjacent pairs
  (0.13–2.79 ms, all six positive); the `Map` above is the shape, and the array is how the shape is
  spelled. A slot is `null` until its channel is first used, so nothing preallocates a log per wire.
- **Publishes:**
  - nothing about retention: since #43 the log is a fixed ring whose capacity is derived, so the
    engine publishes no watermark (ADR 0004, Revision 2026-11-14).
  - `subscribers(wire)` — compared against `design/crossings.md` in debug builds.

### 3a. How retention is done (#43)

- **A log is a fixed ring.** `ValueLog.Capacity` = 256 records: one array, one head offset, no growth,
  no copy. Appending past the window overwrites the oldest record and counts it, which is the whole of
  the memory story — 8 KiB a log, 22 logs on the boot, `records_retained` 4 882 at the end of a
  300-frame run against 47 213 110 records appended, and no retention pass anywhere in the loop.
- **The capacity is derived, not chosen.** A reader's need is the records its log appends while that
  reader's cursor sits. `tests/performance/FINDINGS.md` §10 measures it at **1–5 records on every log**
  of the 300-frame boot and of the synthetic trace — the boot's `bus` 5 is one four-record exchange plus
  the base, `MD` 5 is Video's four words plus the base — and at **66** in the worst case a timed
  subscription can produce. 256 is that worst case with a 3.9× margin, and `bus` with 51×. A Δ chosen
  "to be safe" at a frame would need a 20 500-record `answer` ring: the trap #43's own §3.3 names.
- **A sample older than the window answers "nothing" (0), never a wrong value.** The ring tests
  `instant < OldestInstant` before it looks at any slot; without that test a wrapped slot would answer a
  *newer* record's value, which is the failure G7 exists for and which no digest can see.
- **Two multipliers used to sit on top of that need, and neither was a reader.** The machine-wide
  `_sampleFloor` was reset per retention pass rather than per batch — 128 batches late, 129 056 ticks of
  lag, the whole of the old `needFloor − needReader` — and then the cadence itself, which kept 128
  batches of a log's own appends (`bus` 611 at peak). Both are gone with the log; FINDINGS §10 has the
  numbers, taken with an instrument that is not in the tree.
- **Cursors are sequence numbers, never indices.** An index saved across a suspension names a different
  record once the ring has overwritten anything; `SystemControlUnit`'s bus exchange and
  `TimersAndEvents`' `DE` scan both stored indices and both broke the moment the window moved (a dead
  bus: three cycles started, no answer, the CPU suspended). A cursor *below* the window now names the
  oldest held record, which is indistinguishable from a cursor that is genuinely there — that is what
  the engine's own assertion is for (#43's rule 3).
- **What proves it.** G7 (`check-window`) tests the ring's algebra and a live sampler against a writer
  that appends eight times faster than the window holds; `run.stream` proves no record, instant or
  ordering moved; the cost sits in FINDINGS beside the projection. The cadence this replaced was
  measured at 128 batches against 16 in #38 (2.09 ms); #45, which would have extended it to 256, is
  closed by this ticket.

## 4. Debug assertions (all off in release)

| # | Assertion | Catches |
|---|---|---|
| 1 | queue head non-decreasing | an ordering bug, at the instant it happens |
| 2 | `suspendedChips == heldDates + queuedResumes + subscriptions + awaitedWrites` | a chip suspended with nothing queued — a lost obligation; and the engine's own count of held dates against the chips that hold one (#40) |
| 3 | `nextDeadline != null ⇒ that chip holds a date at or before it, or a `Resume` for it is queued at or before it` | the forgotten self-wake; on System Control Unit, Timers and events, Sound, Disk, Controllers only |
| 4 | `subscribers(wire)` ⊆ the catalogue's rows for that wire | a subscription nobody documented |
| 5 | every class-1 row's subscriber present while its chip is suspended | a liveness bug with one owner |
| 6 | writes per resume below a cap | a loop with no suspension in it |
| 7 | no read or write earlier than the issuing chip's clock | a backdated instant |
| 8 | no sample, no record-by-seq and no cursor below the ring's `OldestInstant` / oldest `Seq` (`ValueLog.Debug.cs`) | a ring that overwrote what a live reader still meant to ask — the wrong *value* with an unchanged *record stream*, which no digest can see (#43) |

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
- **Two folds, one per question.** `run.digest` mixes each record's instant, its **seq**, its channel and
  its driver/mask/value — the record stream *with* identity, the number a commit quotes. `run.stream`
  mixes the same fields **without** the seq (#39). A change that only alters how many queue entries a
  frame has must leave `run.stream` byte-identical; a change that moves an instant, a record or an
  ordering moves both. `run.stream` is what ADR 0008 §7 condition 6 is witnessed against.
- **The identity digest has moved twice**, both times declared: once by #39's split (boot `ffd761fc…` →
  `eaf7ecf1…`, synthetic `9b92d3ae…` → `58fcdddf…`; `run.stream` byte-identical, `pops_per_frame`
  unchanged) and once by #40 (boot `eaf7ecf1…` → `93c4dbd4…`, **10 801 972 → 10 801 967 batches**: five
  instants existed only because a losing date was queued at them, and an empty instant carries no record;
  `run.stream` byte-identical again, `e52ed136…`). Both folds are host-independent — they matched
  byte-for-byte on two hosts.
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
