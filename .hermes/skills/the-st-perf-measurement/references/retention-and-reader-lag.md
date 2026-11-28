# Retention, reads, and the bound on how far in the past a reader looks

Read this before proposing or pricing a change to `ValueLog`, the retention cadence, or a fixed-size
ring. It is the structural map plus the rule that decides a capacity; the numbers are from a 300-frame
Debug boot and are host- and revision-specific — retake before quoting.

## Where a wire's and a register's values live

- One storage type for both: `ValueLog`, keyed by `Channel` = `(ChannelKind.Wire, id)` for a net and
  `(ChannelKind.Internal, chip.Id << 8 | slot)` for a chip's register/counter log (`Anchor`). Lazy
  creation, four array-indexed tables in `Engine` (logs and subscriber sets, per kind).
- `Record(long Instant, long Seq, Channel, int Driver, ushort Mask, ushort Value)` ≈ 40 B. `Seq` is
  `_recordSeq`, a counter **separate** from the queue's `_arrival`: records are cursor'd and searched by
  `Seq`, queue order is `(Instant, _arrival)`.
- Reads: `ValueAt` = last record at or before an instant; `IndexAtOrBefore` / `IndexAtOrAfter` /
  `IndexAt(seq)` / `IndexAfter(seq)` = binary search; `HasRecordAfter(seq)` and `ChangesAtOrAfter(t)` are
  one comparison against the newest/oldest record. The newest-record shortcut in `Search` is the hot path
  (the "what does the wire hold now" ask) — keep it cheap in any replacement structure.
- Contract: an ask below the retained window answers **0**, never a wrong value. 0 can be plausible,
  which is why a digest cannot see this class of bug and a dedicated assertion/row is required.

## Sizes, growth, trimming (the thing a ring would delete)

- Array starts at 16, grows on a full append to `max(16, count*2)` (16, 32, 64 …).
- `TrimTo(watermark)` advances `_head` and keeps one **base** record at or before the watermark (min
  retained = 1). Compaction only when `length > 4096 && count*4 < length`, so a long run never copies per
  trim (compacting every trim made a boot quadratic).
- `Engine.Run` calls `TrimLogs()` every `RetentionEvery = 128` batches; `RetentionWatermark(log.Channel)`
  is the minimum `From` over *that log's own* pending subscribers, folded with the machine-wide
  `_sampleFloor` (set by `NoteSampled`, reset after every trim).
- Cost of the cadence, measured: ~84 000 trims over a 300-frame boot, 708 ms total — ~8.4 µs a trim over
  ~26 logs, ~2.36 ms a frame. **That total is a Debug-run stopwatch figure**: it tells you the shape of the
  cost, not the Release frame time. Price the deletion with pairs.
- Aggregate live size is small: ~770 records retained of ~47 M appended at the end of a boot. The ticket is
  the bookkeeping and the array churn, not the bytes.

## Primitives (what a chip can do, and where each wake lives)

| verb | suspends | wake lives in |
|---|---|---|
| `Advance(n)` | never | — |
| `Write(t, …)` | never | inline append+fire if `t <= MachineTime`, else an `ApplyWrite` queue entry |
| `await WriteAndWait(t, …)` | always | same-instant `Resume` at `t`; future: one `ApplyWrite` carrying the waiter |
| `await ReadAt(t, …)` | only if `t > Clock` (else stack + `NoteSampled`) | a **chip-held date** (`HoldDate`), no queue entry |
| `await WaitFor(t, wire, afterSeq)` | yes | **two arms**: chip-held date (unless `t == long.MaxValue`) *and* a subscription with `From = chip.Clock` |

`ReadNext` has **no call sites** in `src` — every real subscription is `WaitFor`. Exactly two arms in the
machine use `long.MaxValue` (CPU on `answer`, Video on `group`); every other arm carries a date
(SCU: `min(NextEvent, _answerInstant)`; Timers: `min(_cExpiry, _dExpiry)`). `WaitForEither` resolves
**inline** when `instant <= MachineTime` (one enqueued `Resume`, no subscription) — a timeout must be ≥ 1
 tick off the chip's clock or the arm degrades to a spin at one instant.

## Where a read actually reaches into the past

A running chip's `Clock == MachineTime` at every resume, and assertion 7 forbids reads before its own
clock. What lags is the **record** instants it consumes:

- A fire dropped as stale (the chip was resumed first) is not data loss: the chip catches up by scanning a
  `Seq` cursor at its next wake — `IndexAfter(_busSeenSeq)`, `IndexAfter(_groupSeq)`, the `DE` scan in
  `CountEventRecords` — and those records predate the batch being drained.
- Samples then name those instants: `ValueAt(De, delivery.Instant)`, `ValueAt(Answer|Md, answerInstant)`,
  `ValueAt(Ipl, Clock)`, plus `NoteSampled` for the video `MD` window.
- The SCU finds its own exchange with `IndexAt(_assertionSeq)` and needs `at + 3` to still exist; if the
  record is gone it **returns silently** (the dead-bus failure). Any bound must cover one full exchange.
- `_sampleFloor` exists only because those asks have no subscription behind them.

## The capacity rule (do not derive it from a chosen timeout)

A timeout that fires is a wake that appends nothing, still resumes the chip, and re-arms its next date with
**a new arrival number** — that reorders same-instant entries elsewhere and moves `run.stream`. So a
timeout is not free, and as a *bound* it is also poor: a one-frame timeout on `bus` (~18 500 cycles a
frame × 5 records, ~0.145 records a tick) means a ~92 700-record ring, orders of magnitude worse than the
per-log watermark it would replace.

The better primitive is already in the engine: **a subscriber whose chip holds a date has an exact
horizon** — `chip.DateInstant`. It will be resumed then whatever happens, so the records it can still ask
for are bounded by `records appended to that log in [From, DateInstant]`. That bound is free, exact, and
arm-neutral (it adds no wake). A timeout is then only insurance on the two `long.MaxValue` arms, where no
date exists and the peer always answers inside a bus cycle or a line — i.e. it should never fire, and a
`timed_wakes` counter in `run.state` is expected to read 0.

State the rule as: **capacity(log) ≥ 4 × the max records appended to that log while one of its readers'
lagged**, lag being exact where the chip holds a date and declared where it does not; capacity is derived
from a measured lag, never from a comfortable Δ. Measure the per-log max `Count` and max reader lag over
three workloads (boot, synthetic, floppy-load) before choosing any number.

## If a ring replaces the log

- Test `instant < OldestInstant` **first** and answer 0 — after wrap a slot holds a newer record, and
  without this test the ring answers a wrong value where the log answered nothing.
- Keep the newest-record shortcut; for older asks, scan back from newest rather than binary-searching a
  wrapped index (a modulo per probe in the hottest read in the machine).
- `Seq` cursors (not indices) are what makes a ring possible at all — the callers already tolerate index 0
  and re-scan from a sequence number.
- `bus` cannot be sized below one exchange: `_assertionSeq` must survive from assertion to answer.
- The witness is not `run.stream` (an empty instant is not a record; a wrong sample folds to nothing). It
  is an assertion that no answer was read from outside the window and no cursor sat below it — plus the new
  gate row, and a decision by the owner on whether the old retention row is subsumed or stays.
