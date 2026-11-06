# Wires are append-only value logs with per-driver state

A wire (or bus) is an ordered, append-only log of `(instant, driver, lane mask, value)` records, plus
the resolver that turns the drivers' current values into the wire's value. Driving high impedance is
a record like any other — a driver releases by writing `Z`, and the past is never rewritten. The value
at an instant is the resolver's answer over each driver's last record at or before that instant, so a
sample taken for the past stays correct forever, and the log doubles as the history the user interface
draws.

**Status**: accepted (2026-11-04). Ticket: #7 (scheduler), and the open-collector inventory under #6.

## Context

- The owner's model (2026-11-04, items 4–6): each wire carries an ordered list of value-set records
  stamped with machine ticks; a chip driving an output pin appends one synchronously; a sample reads
  the last record before the instant asked for; high impedance cancels the last value the chip set;
  the list keeps the last ~1000 records.
- Three of those clauses needed correcting, and the corrections are what this ADR records:
  1. **Retroactive cancellation corrupts history.** Deleting a driver's last record makes a sample
     taken at an earlier instant return a value the wire never carried, and it silently invalidates
     every trace and graph already drawn. Release is therefore an event, `Z`, appended in order.
  2. **One wire can have several drivers at once.** A single-driver resolver is not enough for the
     nets this machine has (an open-collector request line pulled by any of several devices, a bus
     two masters can drive in turn). The value is a function of the *set* of driving values, so the
     resolver is per wire type.
  3. **A count-bounded ring is a correctness rule masquerading as a memory limit.** An evicted record
     is not a missing detail — it is a wrong answer to a legitimate sample. The retention bound must
     be semantic, and ~1000 records is a sizing figure.
- The observability requirement is the owner's and it is explicit: the user interface must be able to
  draw graphs of a wire's value history. That makes the log a deliverable, not a debug facility.
- `GLOSSARY.md` already separates *signal crossing* from a mere pin-value change, and "bus
  transaction" is on its avoid-list. A wire record is therefore **not** a crossing: a hundred records
  may pass with no chip being resumed, and only the class-1 ones in `design/crossings.md` are crossings
  (ADR 0006 separates the two: class 2 resumes a chip that opened the exchange, and starts nothing).

## Decision

- **A wire is a port group, not a pin.** The address bus is one wire with a 24-bit lane mask, the data
  bus one with 16. Per-pin wires are not modelled — they would multiply the record count on the
  address bus twenty-fourfold and add no information. A plain control signal is simply a one-lane
  wire; the representation is uniform.
- **Records are `(instant, driver, mask, value)`, append-only.** Nothing is deleted from the past;
  retention truncates only from the oldest end, under the semantic bound below.
- **`Z` is a value.** A driver releases by writing `Z` at an instant; the wire's value then follows
  from the remaining drivers. Release costs a record, and it is never a rewrite.
- **The value at instant τ = `resolve(per driver: its last record at or before τ)`.** Resolvers:
  - *single-driver* — the one driver's value, `Z` meaning undriven;
  - *wired-AND / wired-OR* — open-collector; any driver pulling the line wins, `Z` is passive;
  - *tristate bus* — exactly one driver expected; two drivers with differing values is a **modelled
    bus conflict**, reported to the debugger rather than silently resolved, because on this machine
    a conflict is a symptom of a broken arbitration model.
- **Drivers are identified in every record.** Driver identity is needed for `Z`, for conflict
  detection, and for drawing one graph line per driver.
- **No coalescing of repeated identical writes in v1.** A repeated write is observable in a graph and
  costs nothing to resolve. If it ever matters, it is an optimisation inside one wire type.
- **Retention is semantic: a record may be discarded only when no reader can still ask for an instant
  at or before it.** In practice that is one publication number from the scheduler — under ADR 0006 it
  is exactly the oldest `from` among the open subscriptions, plus the oldest instant a UI graph still
  references. A count cap may be used as a secondary guard, and when it fires in a debug build it is a
  defect, not a shrink.
- **The display history is a separate, decimated copy.** The engine wants microseconds of records;
  the graph wants seconds. Decimating for the screen is required; decimating for the machine is
  forbidden.
- **The log is the single source for three consumers**: the scheduler's reads, the user interface's
  graphs, and the trace dumped in tests (a VCD export is nearly free, and it is teaching material).

## Invariants

1. One wire per port group; lanes travel together in one record.
2. The log is append-only. No record is ever edited or removed from the middle.
3. `Z` is a value; release is a record; the past is immutable.
4. The value at τ is a pure function of the records at or before τ and the wire's resolver.
5. A sample at τ returns exactly what the resolver would have returned at that instant — the answer
   never changes because of anything that happens later.
6. A record is discarded only when no reader can ask for its instant or earlier.
7. A wire record is not a crossing. It resumes another chip only by firing a subscription, and it is a
   crossing only if that subscription starts new work there (class 1, ADR 0006).
8. Conflicting simultaneous drivers on a tristate wire are reported, never resolved by guess.

## Consequences

- **Cost.** A record is ~16 bytes (instant 8, driver 2, mask 2, value 4). Two hundred wires × 1000
  records is ~3 MB: nothing. A read is a backward step through the log from a per-reader cursor,
  which is O(1) in the common case where the instant is near the front.
- **The UI arrives with the engine.** The logic-analyser view is a query over one wire's log; it
  needs no second instrumentation path, so it cannot disagree with the emulator.
- **Bus conflicts become visible**, which is the cheapest available check on the arbitration model —
  and the arbitration model is exactly what this project has already had to correct twice.
- **The open-collector inventory is an open question for #6**: which nets on this machine are
  genuinely multi-driver must come from the schematic and the field service manual, not from a guess.
  Until that list exists, every wire defaults to *tristate bus* (the strictest resolver), so an
  unlisted multi-driver net shows up as a conflict instead of as a wrong value.
- **Vocabulary**: *wire*, *wire event*, *driver*, *resolver* enter `GLOSSARY.md`.

## Considered options

- **A current-value register per wire, no log.** Rejected: it cannot answer a sample for an instant
  the engine has not reached yet without blocking, it cannot draw history, and it hides `Z`/release
  semantics.
- **Cancelling a driver's last record on release** (the first wording). Rejected: rewrites the past
  and breaks every earlier sample and every graph.
- **A fixed count-bounded ring as the correctness rule (the "1000" as written).** Rejected as *the*
  rule; kept as a sizing figure, with the semantic bound doing the work.
- **Per-pin wires.** Rejected: 24× the records on the address bus for no information.
- **Storing every chip's full output word per record instead of a lane mask.**
  Rejected: a mask is what lets one record carry "these 8 lanes changed" and lets several drivers
  write disjoint lanes.

## Falsifier

- A sample whose answer differs between two runs, or between a run and its replay from the trace,
  means invariant 4 or 6 is violated.
- A graph that shows a wire value the machine never carried means the resolver type is wrong for that
  net — the most likely cause is an open-collector net modelled as tristate.
- If record retention ever needs a hard count bound to stay inside memory, the engine is producing
  far more records than the crossing catalogue predicts: fix the model, not the buffer.
