# Chips are independent timelines, ordered by one scheduler

Each virtual chip is an endless `async` loop with a clock of its own, advanced only by its own logic
and never inferred from pin activity. The chip's code carries no scheduler awareness: a crossing is
written `Write(instant, …)` or `await Read(instant)`, with an instant the chip computes itself. One
scheduler thread owns a single queue ordered by `(instant, chip priority, emitter sequence)` and
always fires the earliest. A chip resumed at an instant sets its clock to that instant, so machine
time never decreases — which makes deadlock impossible by construction rather than by review, and
makes "wait until every writer has caught up with this instant" a primitive the engine does not need.

**Status**: accepted (2026-11-04). Tickets: #7 (scheduler), #11 (determinism), #6 (crossing catalogue).

## Context

- ADR 0002 fixed the mechanism — C#, one `async` state machine per virtual chip, a scheduler-owned
  `SynchronizationContext`, and the synchronous fast path (a read of an instant already reached
  returns on the chip's own stack). It deliberately left the *ordering* rules and the ownership of a
  chip's clock open. Those are #7's subject, and they are this ADR.
- The owner's model of the engine (2026-11-04), restated in his words: a chip is a never-ending state
  machine; its logic reads as a continuous function with `await` directives in front of the pin and
  bus samples, each carrying a computed timestamp; the scheduler is completely hidden from it and
  exists to pick the next chip to run, for determinism. This ADR adopts that model whole.
- It replaces exactly one part of it: **the progress timestamp is a property of the chip, not a
  by-product of touching a pin.** A chip that advances its clock monotonically must be able to do so
  while setting no pin at all — an MFP counting down 8 ms, a WD1772 waiting 16.4 ms for a sector, a
  Blitter holding the bus mid-transfer, a 68000 in a delay loop.
- Two shapes were ruled out on the way here. One OS thread per chip, free-running at host speed,
  cannot be deterministic: the order of two crossings at the same instant would come from the host
  scheduler, cache-line ownership and CPU frequency, none of which is part of the model
  (`research/deterministic-threaded-simulation.md` §1.5). A wall-clock wait inside a sample makes
  machine time a function of host speed, which costs fast-forward, headless regression runs and
  trace replay.

## Decision

- **One virtual chip = one endless `async` loop + one clock in ticks.**
- **The clock advances only from the chip's own logic.** It is advanced by explicit cycle accounting,
  never computed from "when did I last touch a pin". `advance(n)` is the only way time moves.
- **Chip code is scheduler-agnostic.** The only operations that cross the chip boundary are
  `Write(instant, wire, mask, value)` and `await Read(instant, wire)`. No yields, no locks, no
  sleeps, no thread primitives, no host clock, no "resume the scheduler" call. A chip's state machine
  must be readable as straight-line code.
- **Every instant a chip names is computed by the chip** — a bus cycle is `now + 16` ticks, a timer
  expiry is `now + count × period`. A read whose instant is already reached returns on the stack
  (ADR 0002's fast path, which is the invariant that keeps this affordable).
- **One scheduler thread, one ordered queue, key `(machine time, chip priority, emitter sequence)`.**
  It fires the earliest pending instant; it never advances an instant with nothing to do.
- **Same-instant coupling is resolved inside the firing of that instant, by the priority key** — never
  by one chip waiting for another. This is the only place the model is allowed to say "these two
  happened at the same moment", and it says it once: `GLOSSARY.md`'s *Same-tick ordering*.
- **A resumed chip sets its clock to the instant it was resumed at.** Nothing may read an instant
  earlier than its own clock, and nothing may write one.
- **Host time appears only in the host adapter** — frame pacing after a completed frame, and the
  audio ring buffer. Machine time never reads it (ADR 0001; hardware overview §10.3 rule 7).
- **Every wire operation is recorded**, because the recording *is* the observability surface: the
  user-interface graphs of a wire's value history are a read of the log, not a second instrumentation
  pass (ADR 0004).

## Invariants

1. One time domain: ticks of the 32 MHz oscillator, 64-bit (a 32-bit counter wraps every 134 s).
2. Ticks are coordinates, not a step size. The engine steps from instant to instant, never tick to
   tick.
3. A chip's clock is its own, advanced by its logic, monotonic, and never derived from host time.
4. Chip code knows only `Read` and `Write`; the scheduler is invisible to it.
5. Every `Read` and every `Write` names an instant ≥ the issuing chip's clock.
6. The scheduler fires the earliest pending instant. Ties break on `(chip priority, emitter
   sequence)`, both assigned by the model.
7. A chip resumed at instant *t* continues with its clock at *t*. Machine time is non-decreasing.
8. Host time is confined to the host adapter; no chip and no wire may consult it.

## Why there is no deadlock (and nothing to reason about)

The owner's item 10 — "reason on the dependency chain to check we can't create deadlocks" — is
retired by invariants 5–7, in three steps:

- A chip only ever *waits* on an instant later than the one it is running at (5), so waiting always
  moves the waiter forward.
- The head of the queue is non-decreasing (6, 7), so no instant can be revisited.
- Two chips that need each other *at the same instant* are not waiting at all: that instant fires
  once and the priority key orders them inside it.

A cycle would require the queue's head to return to an instant it has already passed. It cannot. The
consequence is stronger than "no deadlock": **the engine needs no lookahead, no null messages, no
watermark, no timeout and no deadlock detector** — and no chip ever waits for another chip's
progress. A wire read is answered from the log at the instant asked for, and the ordered queue
guarantees the log is complete for every instant before the head.

The one obligation that replaces all of that machinery: **any time-driven obligation must be
registered as a wake instant.** A chip that owns a counter and forgets to wake when the counter
expires does not deadlock — it silently runs the machine past an event, which is a visible bug and a
unit test, not a protocol.

## Consequences

- **#7 (scheduler)** is a min-heap keyed by the triple, plus the priority table. There is no barrier,
  no participant count, no futex and no `SynchronizationContext` tuning beyond dispatching
  continuations in queue order.
- **#6 (crossing catalogue)** becomes the queue's insertion rate, and the catalogue's class
  distinction survives intact: only class-1 signals are crossings, class-2 completions are answers on
  the same exchange and class-3 clocks are phase. Under ADR 0005 they are all wire records, but only
  class 1 is a reason for another chip to run.
- **#11 (determinism)** is testable as the research prescribes: a state hash per fired batch, twice
  from the same image, byte-identical. Randomised delay injection in debug builds catches any place
  where the model leaks host order.
- **The user interface** gets its data for free, at the granularity the machine actually has: any
  wire, any interval, read out of the log.
- **Parallelism stays available but unearned.** With the scheduler invisible to chip code, a later
  T>1 build is compute-in-parallel / commit-in-order behind the same interface
  (`research/deterministic-threaded-simulation.md` §2.4–2.5). No chip changes.
- **Vocabulary**: this ADR adds *wire*, *wire event*, *driver*, *resolver* to `GLOSSARY.md`, and
  fixes "the scheduler" as the thing that owns the queue and the clock — not a group of threads.

## Considered options

- **One OS thread per chip, free-running at host speed, each wire sample waiting until every writer
  reached the instant.** Rejected: the wait needs per-chip watermarks and a deadlock analysis the
  invariants above make unnecessary; the tie-break would come from the host; and at T=1, which ships
  first (ADR 0002), it buys nothing at all.
- **A barrier on a fixed grid — every tick, or every quantum.** Rejected arithmetically: 640 000
  barriers/frame is 3.2× realtime at 100 ns *in barriers alone*, and even a 64-tick quantum forces
  every chip to be evaluated on the grid, destroying the lazy advance that the fast path depends on
  (`research/pin-event-rate.md` §5, `…threaded-simulation.md` §2.1–2.2).
- **Optimistic synchronisation (Time Warp).** Rejected on four counts — state size, no cheap inverse,
  same-tick contention is a priority decision rather than a causality violation, and irreversible
  observable output — recorded in `research/deterministic-threaded-simulation.md` §1.4. It would be a
  rewrite, not an increment, if it were ever wanted.
- **A host-clock wait inside the VBL or sound sample** (the owner's item 11, as first worded).
  Rejected: it makes machine time a function of host speed and takes fast-forward, headless
  regression and replay with it. Pacing belongs to the host adapter; the VBL stays a model instant at
  exactly 640 000 ticks.
- **Inferring a chip's progress from its last pin activity.** Rejected: every idle chip's clock
  stalls, which is both a wrong timestamp and the classic way a coupled simulator hangs.

## Falsifier

1. Two runs of the same image producing different state at T=1 means invariant 6 or 7 is violated
   (or ADR 0004's retention). Determinism is the test, not a hope.
2. If the walking skeleton cannot hold 50 Hz with headroom, the cost of the model is wrong — but the
   response is ADR 0005's inlined memory path, never a different engine.
3. If a chip's logic ever *needs* to read an instant behind its own clock, there is a latent causality
   bug in the partition: record it as an open question. Do not add a retroactive-read mechanism; at
   that point the machine has two timelines, which is what ADR 0001 exists to prevent.
