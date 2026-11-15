# What the frame budget is made of

Measured on 2026-11-13 against `720f34d`, with `validation-harness boot --frames 800` in a **Release**
build, on the container's host (Intel Xeon @ 2.30 GHz × 4 cores, `nproc` 4, ~3 GB RAM). The question is
row **G8** of `design/validation.md` §3 — mean frame ≤ 5 ms of the 20 ms frame — and the answer is that
the uniform model spends **65.8 ms** a frame, of which the design's own crossing bill already accounts
for most.

The ruling this measurement exists for is [#38](https://github.com/laurentprudhon/the-st/issues/38)
(the leaf, a restated budget, or fewer crossings). This file is the evidence, so that no later session
re-derives it.

## 1. The build is not the problem

`-c Release` sets `Optimize=true` and leaves `DEBUG` off; `scripts/validate.sh` proves the Release
assembly carries no `Digest` symbol, no `ASSERTION` text and writes no digest (ADR 0008 §6). The JIT was
measured rather than assumed, four runs of the same 800-frame boot:

| configuration | mean frame |
|---|---|
| default — tiered compilation **and** tiered PGO | **65.15 ms** |
| `DOTNET_TieredPGO=0` | 76.08 ms |
| `DOTNET_TieredCompilation=0` | 82.88 ms |
| both off | 83.80 ms |

The default is the fastest, so 65 ms is the honest number for this machine and this host. (The mean
includes frame 1 at ~437 ms of JIT and first touch: about 0.5 ms of the 65.)

## 2. Method, and what it can and cannot resolve

`tests/performance/perf-rig.patch` instruments the engine behind `ST_PROF` in the environment:
per-channel volumes (records appended, subscriptions registered, subscribers fired), per-chip
continuation timers, per-operation timers, allocation attributed by region, and `Engine.Log` call
counts. Apply with `git apply`, build Release, run `ST_PROF=1 validation-harness boot --frames 800`,
read `perf.txt` in the run's directory, then `git checkout` the three files.

Two limits, both stated because they cost a claim:

- **A `Stopwatch` reading costs 26.5 ns here.** Any operation cheaper than ~50 ns cannot be timed this
  way. The rig's own pass therefore measured `append` as "0 ns net": that is the instrument's floor, not
  the operation. Those rates come from the micro-benchmark in §5 instead, and everything the rig reports
  is net of the clock.
- **The rig's presence inflates the run** (100.3 ms a frame against 65.8 uninstrumented). The *shares* in
  §3 are the measurement; the absolute figures are scaled from the uninstrumented runs.

## 3. Where a 65.8 ms frame goes

| where | volume, 724 frames | rate | ms/frame | share |
|---|---|---|---|---|
| chip coroutines' own work — async plumbing, CPU decode, log reads | 45 195 083 resumes | 1.0–2.6 µs | ~33 | 51 % |
| firing a wire's subscribers: scan the list, remove, enqueue a resume | 45 194 618 | 176 ns | ~6.0 | 9 % |
| appending a record: look up its log, store it | 91 770 529 | 14–40 ns | ~3.8 | 6 % |
| queue pops, engine side (dequeue + dispatch) | 77 624 118 | ~40 ns | ~4.0 | 6 % |
| enqueues | 77 624 120 | 16–54 ns | ~2.6 | 4 % |
| retention trims | 1 848 a frame | 3.05 µs | ~2.7 | 4 % |
| GC pause (1 177 gen-2 collections) | 19 481 MB allocated | — | 2.4 | 4 % |
| the rig itself and the harness's probes | — | — | the rest | — |

## 4. Per wire, and per chip

Records appended / subscription fires / subscriptions registered / log searches, a frame:

| wire | records | fires | subs | searches |
|---|---|---|---|---|
| `bus` | 71 135 | 38 002 | 38 002 | 1 848 |
| `MD` | 29 102 | — | — | 18 966 |
| `answer` | 14 227 | 14 227 | 14 227 | 30 303 |
| `timer D` (internal) | 6 013 | — | — | 1 830 |
| `group` | 3 995 | 3 995 | 3 995 | 1 848 |
| `IPL` | 632 | 182 | 182 | 8 413 |
| `HSYNC` | 625 | — | — | 1 848 |
| `DE` | 399 | — | — | 5 842 |
| `line` / `pixel counter` / `refresh` | 313 / 200 / 79 | — | — | 1 848 each |
| `MFPCS` | 7 | 6 019 | 6 020 | 1 843 |

`bus` is five records per bus cycle (four assertion lanes and a release) and two wakes per cycle (the
assertion and the release), which is where ADR 0006's falsifier 4 finds 7.54 pops a cycle against the
catalogue's "~4".

| chip | resumes | µs each | share of coroutine time | records driven |
|---|---|---|---|---|
| CPU | 10 432 005 | 2.60 | 52.7 % | 51 501 939 |
| SystemControlUnit | 27 513 211 | 0.75 | 40.1 % | 35 753 608 |
| Video | 2 892 000 | 0.53 | 3.0 % | 144 620 |
| TimersAndEvents | 4 357 867 | 0.50 | 4.3 % | 4 370 362 |

## 5. The rates the rig cannot time (micro-benchmark, same process shape)

| operation | ns |
|---|---|
| store a record in its log's array | 4.4 |
| …with a `Dictionary<Channel, …>` lookup in front | 13.7 |
| …with an array index in front | 3.1 |
| `Dictionary<Channel,int>` lookup alone | 7.0 |
| array index alone | 1.4 |
| fire: look up the channel, scan a one-element subscriber list, reset it | 9.7 |
| queue: enqueue one entry and dequeue it | 15.8 |
| `ValueLog` search: binary over 10 000 records | **99.7** |
| `ValueLog` search: last-record fast path (the newest record is at or before the instant) | **1.9** |
| one `Stopwatch.GetTimestamp` | 26.5 |

**A correction worth keeping.** An earlier measurement in this session reported 72 ns for the
`Dictionary<Channel,…>` lookup. It was wrong — no warmup, and it contradicted the 78 ns total append
cost it was supposed to explain. At 7.0 ns the channel dictionaries are ~1.6 s of a 47 s run, so
rewriting the log tables as arrays is worth ~1.8 ms a frame, not the ~17 s the wrong number implied.

## 6. What this implies

Cheap, design-neutral, measurable (nothing here moves an instant, a record or an ordering):

| change | recovered |
|---|---|
| retention every 256 batches instead of every 16 (`RetentionEvery`) | ~5 ms a frame |
| a last-record fast path in `ValueLog`'s searches (99.7 → 1.9 ns for the newest-record read, most of 68.6 M searches) | ~3 ms → **0.90 ms measured** |
| array-indexed log and subscriber tables in place of `Dictionary<Channel, …>` (1.8 ms) and a one-subscriber inline slot in place of `Dictionary<Channel, List<Subscriber>>` (≈ half of the 6 ms fire path) | ~4 ms → **1.13 ms measured**, slot **reverted** |
| **together** | **65 → ~50 ms a frame** → **34.3 → ~30.2 ms measured** on the laptop host |

The row above is 256-against-16; what shipped is **128** against 16, measured at **2.09 ms a frame** over
four *alternating* adjacent pairs (1.08–3.52, all four positive). Every figure in this section was taken
with alternating pair order: this host drifts ~2 ms across a session, and pairs run A-then-B credit B
with that drift — the same six pairs read 1.15–2.02 that way and 1.08–3.52 alternating.

**The fast path, measured rather than projected** (2026-11-13, laptop host, ten *alternating* adjacent
pairs): **0.90 ms a frame** — 29.32 ms a frame in `validate.sh` against 31.45 before it. Nine of the ten
pairs favoured it and one was −0.41 ms. The ~3 ms above was extrapolated from the 99.7 ns of a search over
**10 000** records; the boot's logs hold a few hundred, so a search is ~8 probes and not ~20, and the
fast path can only remove what the walk actually cost. The projection was a share, not a measurement.

**The array tables, measured**: **1.13 ms a frame**, six alternating pairs, all six positive (0.13–2.79).
That is the dictionary half of the "~4 ms" row above; the inline slot was supposed to be the other half.

**The one-subscriber inline slot was implemented, measured, and reverted** — it is a pessimization on this
engine, both ways it can be written:

| spelling | allocation | pairs | result |
|---|---|---|---|
| single slot, **reclaimed** when a wire drops back to one waiter | 22.78 → **26.77 MB** a frame | 8, all negative | **3.65 ms slower** |
| single slot, list kept once created (no reclaim) | 22.92 MB a frame | 4, all negative | **0.99 ms slower** |

The first spelling allocates a `List` per burst on a wire that oscillates between one and two waiters —
4 MB a frame of garbage to save one dereference. The second spelling removes the garbage and is still
~1 ms slower: `Count` and the indexer become branches (`_many is null ? _one : _many[index]`) on the hot
fire loop, and the three-field set is a worse cache line than the list pointer it replaced. The row's
"≈ half of the 6 ms fire path" assumed the dictionary was still in front of the list; after the array
tables there is no dictionary left to remove, and what remains does not pay for itself.

## 7. The sequence counter split (#39): measured, not projected

`Engine.Enqueue` and `Engine.AppendRecord` drew from one counter, so every queue entry consumed a number
a record could not have. Consequence: **any change that removes queue entries renumbers every later record
and moves the digest**, which made ADR 0008 §7's condition 6 unwitnessable for #40 and for the leaf.

#39's ruling split them: an **arrival** counter for the queue key (read only by `Key.CompareTo`) and a
**record** counter for `Record.Seq` (the subscription discriminator, the logs' sequence searches, the
digest). The measurement is the proof, on this host, in one adjacent pair of 300-frame Debug boots:

| | before the split | after |
|---|---|---|
| `run.digest` — identity included | `ffd761fc…` | **`eaf7ecf1…`** (moved once, re-blessed) |
| `run.stream` — identity excluded | `688ef97b…` | **`688ef97b…`** (byte-identical, 10 801 972 words) |
| `pops_per_frame` | 140 133.6 | 140 133.6 |
| `stale_wakes_per_frame` | 54 830.4 | 54 830.4 |
| gate assertions | silent | silent |

So the records, their instants and their order are untouched; only their names changed. `run.stream` is now
the witness any queue-contents change must satisfy — and it costs four `Mix` calls per record in a Debug
build that is not the measured one, so the frame budget does not see it.

`run.stream` writes a word per instant **that has records**, not per batch: an empty instant carries no
information about the machine, and #40's first measurement proved the difference matters — with empties
folded in, every one of the 9.4 M remaining words moved by a batch-index shift while the records
themselves were untouched. After that refinement it is `e52ed136…` (10 801 251 words), and it is the file
#40 leaves byte-identical.

## 8. The losing arm (#40): measured, and 4.25 ms, not 2–3

The proposal's own projection was "~2–3 ms a frame". **Six adjacent, alternating Release pairs on this
host** say 4.25: the losing date is not merely a pop, it is a `Resume` object allocated per arm that the
GC also has to collect.

| | before | after | the row |
|---|---|---|---|
| frame mean, 6 pairs (A then B, B then A, …) | 29.69 / 28.89 / 29.04 / 28.84 / 28.68 / 28.46 ms | **24.53 / 24.68 / 24.69 / 24.94 / 24.84 / 24.40 ms** | **−4.25 ms mean, −3.84…−5.16, 6/6** |
| `pops_per_frame` | 107 215.6 | **63 193.2** (−41 %) | −44 022 arms a frame |
| `stale_wakes_per_frame` | 44 021.8 | **23 679.7** | exactly the dated half is gone |
| `alloc_mb_per_frame` | 22.78 | **20.77** | −2.0 MB of `Resume` objects |
| `gc_ms_per_frame` | 0.43 | 0.36 | |
| `alloc_mb_per_frame`, Release, 800 frames | 22.78 | 20.77 | no collection-shape regression |

Debug, 300-frame boot, the same pair of runs: `queue_pops` 42 040 073 → 25 589 670, `stale_wakes`
16 449 116 → 8 234 143, `read_suspensions` and `subscriptions` **identical** (16 450 405 / 23 226 950) —
the machine does the same work, it just stops queueing 8.2 M dates it will throw away.

`run.digest` moved again, legitimately and for the last time here: **10 801 972 → 10 801 967 words**
(`eaf7ecf1…` → `93c4dbd4…`). Five instants existed only because a losing date was queued at them; with
the date held in the chip they are never drained, and an empty instant carries no record. `run.stream` —
the identity-free fold — is **byte-identical at `e52ed136…`, 10 801 251 words**, which is the witness that
no instant, record or ordering moved.

And the wall those do not touch: **17.2 GB is allocated inside continuations, 380 B per resume** —
async state-machine boxes plus a `Resume` object for every wake. At the engine's measured 88 ns
suspend/resume floor, 45.2 M continuations is 4.0 s of the run before any work happens, and 77.6 M pops
is 6.8 s. That is why neither a dictionary rewrite nor an array of logs reaches 5 ms, and why the
remaining choice is [#38](https://github.com/laurentprudhon/the-st/issues/38)'s: the leaf, a restated
budget, or fewer crossings.

## 9. The lane-aware subscription (#44): a measured no, and a wake that is not a no-op

FINDINGS §4 said `bus` is five records a bus cycle and two wakes — the assertion group and the release
— and that the release wake finds nothing to do. #44's premise was that a subscriber woken for a record
it never reads is free money in the catalogue, and its whole acceptance test was that such a wake
"must not be able to move a record": `run.stream` byte-identical.

**The premise does not hold, in either half.** A subscription may name the lane mask a record must
carry (`WaitFor(t, wire, afterSeq, lanes)`, matched exactly, `0` = any), the System Control Unit and
the corpus's single-step bus name `0xFF00` (the address-high word — the only record that begins an
exchange), and all of that was built and measured on 2026-11-14, on this host, against today's
baseline (`42d8358`). It is **not landed**, for two reasons that the ticket now records instead of the
win.

**1. It loses.** Six adjacent, alternating Release pairs, 800 frames, `validation-harness boot`:

| | baseline `42d8358` | lane-aware | the row |
|---|---|---|---|
| frame mean, 6 pairs (A→B, B→A, …) | 26.14 / 25.02 / 24.78 / 25.54 / 25.30 / 25.76 ms | **25.97 / 26.04 / 26.05 / 25.83 / 26.05 / 26.33** | **+0.63 ms mean, 5 of 6 pairs against it** |
| `pops_per_frame` | 63 193.2 | **57 080.3** (−9.7 %) | −6 113 pops, 4.44 → **4.01** a bus cycle |
| `subscriptions_per_frame` | 62 424.2 | **56 315.6** | −6 109 |
| `suspensions_per_frame` (dates) | 44 022.4 | **37 913.9** | −6 109 |
| `stale_wakes_per_frame` | 23 679.7 | 23 679.7 | unchanged: the leftover arm still fires, one instant later |
| `alloc_mb_per_frame` | 20.77 | **20.59** | −0.18 MB |
| `gc_ms_per_frame` | 0.36 | 0.38 | |

**The projection was ~1 ms regained** (6 113 pops at the 96 ns a pop #40's own pair implies); the
measurement is −0.63, and the two candidate causes are both in the hot path, which is the answer to
the ADR 0006 §6 question ("does a lane-aware subscription cost anything?"): yes, and not at
registration. A `Subscriber` grows a field and every fire on **every** wire pays

```
subscriber.Lanes != 0 && subscriber.Lanes != mask
```

in the 176 ns fire loop, and the waiter's own "is the wire already answered" test stops being one
comparison on the newest record and becomes a backward scan over the records past its sequence. On
`bus` alone that is ~93 000 records a frame paying the branch, to save 6 113 no-op resumes. A cheaper
spelling (a per-wire "has a lane filter" flag, or a second subscriber list per lane) was **not** built:
what it could recover is bounded by the same 0.63 ms, and reason 2 rules any spelling out.

**2. The release wake is not a no-op — it re-arms the date.** Removing it keeps every counter the
machine has identical (`bus_cycles` 5 564 479, `bus_answers`, `groups`, `de_records`, `hbl`, `vbl`,
`chunks`, `instructions`, `pc`, every assertion silent) and still moves **`run.stream`**:

| | baseline | lane-aware |
|---|---|---|
| `run.stream`, 300-frame Debug boot | `e52ed136…`, 10 801 251 words | **`18c387ea…`, 10 801 251 words** |
| `run.digest` | `93c4dbd4…`, 10 801 967 words | **`8b213e20…`, 10 801 967 words** |
| words that differ | — | **711 of 10 801 251** |

Same word counts, same records per instant, same values — the *order inside* 711 instants moved. The
cause, read from a temporary record/resume dump (not in the tree) at the first differing batch
(7 810, instant 143 480): the System Control Unit arms its next structural date at the top of its loop,
and the release wake resumes it a few arrivals later, so it **arms that date a second time** (the first
arm is dropped by the resume, #40). Per ADR 0007 a same-instant tie is arrival order, so that second
arm decides whether the CPU's bus assertion or the System Control Unit's video group is appended first
when both land on one instant — CPU first in the machine we have, System Control Unit first without the
wake. Both orders are legal under ADR 0007; neither is more physical; the blessed stream is the one
history the machine already has.

**The ruling.** The wake cannot be removed without moving an ordering, and removing it does not pay
anyway: [#44](https://github.com/laurentprudhon/the-st/issues/44) is **answered, not fixed** — the wire
catalogue's bus row stays as it is, `run.stream` stays `e52ed136…`, and the fifth record's wake is
**not** a candidate for #46–#48. What is left as a real observation is the double arm itself: a chip
that re-arms the same held date inside one instant costs one `Resume`, two arms and one subscription a
cycle, and its arrival number is what the ordering hangs on. That is a scheduler shape, not a
subscription one, and it is filed on
[#48](https://github.com/laurentprudhon/the-st/issues/48) with the census below.

**The census of `bus` subscribers (`Wires.Bus`, both, and no other wire in the machine).**

| subscriber | what it reads | the wake it does not need |
|---|---|---|
| `SystemControlUnit` | the four assertion lanes; starts an exchange on `0xFF00` | the release (`0x8000`), and one leftover arm from the previous instant per cycle |
| `Corpus.SingleStepBus` | the same four lanes (a test fixture, not in the boot) | the same release |

No other wire has this shape: `answer` (CPU), `group` (Video), `MFPCS` (Timers and events) and `IPL`
(CPU) each carry one record per event with one mask (`0xFFFF`, `0x0007`, …), so their subscribers read
every record they are woken by. `bus` is the only wire whose subscriber is woken by a record it does not
read, and it is also the widest: 5 records a cycle, 4.44 pops, 23 679.7 stale wakes a frame at
`42d8358`.

## 10. The per-log depth census (#43 rule 5): readers need five records, and nothing sizes the logs but the cadence

#43 asks how deep a ring would have to be, per log, on a workload that is not the boot — and warns that
§4's append rates predate #39/#40. Taken 2026-11-14 on the laptop host (i7-12700H × 20), against
`5506a59`, from a temporary `#if DEBUG` partial (`Engine.Debug.cs`, a `BeforeTrim` hook plus a
channel-aware sample hook) that is **not in the tree**: it samples at each retention pass, so it costs
~17 µs a trim and its `trim_ms` (2 171 against the clean 708) and `frame_mean_ms` (113.90) are
instrument figures, not measurements. Both folds reproduced exactly with the instrument in place —
`run.digest` `93c4dbd4…` / 10 801 967 words, `run.stream` `e52ed136…` / 10 801 251 — so it is inert.

Columns, per log, over the whole run: `peak` = the largest `Count` the log ever held at a retention
pass; `needR` = records from the oldest **subscriber's** `from` to the newest at that pass (what a
reader still needs); `needF` = the same, with today's machine-wide `_sampleFloor` folded in; `lag` =
`MachineTime − from` in ticks; `noDate` = the same lag for subscribers whose chip holds **no date**.

**300-frame Debug boot** (10 801 967 batches, 47 213 110 records appended):

| log | peak | needR | needF | lag (ticks) | no-date lag | records/tick |
|---|---|---|---|---|---|---|
| `bus` | 1 186 | **5** | 605 | 630 192 | 0 | 0.159 |
| `MD` | 893 | **5** | 452 | 0 | 0 | 0.087 |
| `IPL` | 225 | **1** | 128 | 2 015 | 2 015 | 0.003 |
| `HSYNC` | 225 | **2** | 128 | 0 | 0 | 0.002 |
| `answer` | 238 | **1** | 121 | 34 | 34 | 0.032 |
| `timer D` | 235 | **1** | 117 | 0 | 0 | 0.010 |
| `group` | 224 | **1** | 113 | 232 244 | 232 244 | 0.014 |
| `line` | 113 | 1 | 64 | 0 | 0 | 0.001 |
| `MFPCS` | 20 | 1 | 13 | 2 460 240 | 2 460 240 | 0.005 |
| `DE` | 24 | 1 | 12 | 0 | 0 | 0.001 |
| every other log | ≤ 30 | 0–2 | ≤ 16 | — | — | ≤ 0.005 |

**Synthetic trace, 100 frames:** `synth` 429 / **3** / 215, lag 249 (no date); `register` 172 / **1** / 86.

**The reader need is 1–5 records on every log, on both workloads.** `bus` 5 is the widest and it is
structural, not statistical: one exchange is four records and the System Control Unit finds it again by
`IndexAt(_assertionSeq)`, so one exchange plus the base record is the floor a ring can have on `bus`.
`MD` 5 is Video's four words plus the base. Everything else is 1–2.

**What actually sizes the logs is not a reader and not the wire rate.** Two multipliers sit on top of
that need, and both were measured separately:

1. **`_sampleFloor` is reset per retention pass, not per batch.** `TrimLogs` ends with
   `_sampleFloor = long.MaxValue` under the comment "a sample is answered synchronously … so the window
   it opened has been closed by the time the batch ends" — but the reset runs once per **128 batches**,
   so every sample in the window holds a machine-wide floor that applies to *every* log, including the
   ninety per cent nothing sampled. It is the whole of `needF − needR` above (`bus` 605, `MD` 452,
   `IPL` 128) and it reached 129 056 ticks of lag.
2. **The cadence itself.** With the floor reset moved to the end of each batch (one line, experiment
   only, digests still `93c4dbd4…` / `e52ed136…`), `records_retained` falls **770 → 223** and
   `needF == needR` everywhere — but the peaks only fall to `bus` **611**, `MD` 457, `IPL` 130,
   `answer` 123, `group` 115, because between two retention passes a log keeps everything it appends,
   and 128 batches of `bus` is ~600 records. #45's premise was right; the floor was sitting on top of it.

**Four subscriptions have no held date, not two.** §3.1 of #43's handoff names `answer`
(`M68000.cs:264`) and `group` (`Video.cs:75`); the census adds `IPL` when the level is not takeable
(`M68000.cs:458`) and `MFPCS` when neither counter is running (`TimersAndEvents.cs:99`) — both pass
`long.MaxValue` to `WaitFor`, which arms a subscription and no date. Measured lags: `answer` 34 ticks,
`IPL` 2 015, `group` 232 244 (0.36 frame), `MFPCS` 2 460 240 (3.84 frames — §4's own 3.84-frame waiter,
still there under #41's per-log watermark, and it costs 20 records because `MFPCS` is silent while the
CPU idles). Those four are the only arms a timeout has to bound; every other arm already carries an
exact date, and its lag is `DateInstant − from`.

**The capacity arithmetic this gives** — `capacity ≥ Δ × (that log's peak records/tick)` for the
no-date arms, and the exact date-bound need for the rest:

| Δ (ticks) | `answer` | `group` | `MFPCS` | `IPL` | worst | ring 256 |
|---|---|---|---|---|---|---|
| 2 048 (one line) | 66 | 29 | 10 | 6 | **66** | 3.9× margin |
| 8 192 | 262 | 115 | 41 | 25 | 262 | 1.0× — no |

So the ring is **256 records on every log** if the timeout on the four no-date arms is one line (2 048
ticks), and `bus` is then 51× its measured need. A Δ chosen "to be safe" at a frame would need a
20 500-record `answer` ring and a 9 000-record `group` ring — the trap #43 §3.3 describes, and it only
bites the logs whose reader has no date, never `bus`.

**Two things this does not settle.** Whether the per-batch `_sampleFloor` reset should land on its own
(it is stream-identical and four fifths of today's retained set; the ring deletes the floor instead), and
how often a 2 048-tick timeout actually fires on the `IPL` arm — 2 015 ticks is the measured ceiling, so
it fires rarely and must be counted (`timed_wakes`) rather than assumed zero.

## 11. The ring against the cadence (#43 step 5): 3.5 ms a frame, and a timeout that is not free

**The pair.** Six adjacent, alternating Release pairs at 800 frames on the laptop host (i7-12700H × 20),
`validation-harness boot`, load 0.60–1.18, baseline `5506a59` (the append-only log with retention every
128 batches) against `d70169b` (the ring, assertion 8, no cadence):

| pair | retention | ring | delta |
|---|---|---|---|
| 1 (A→B) | 25.17 ms | 21.17 ms | **4.00** |
| 2 (B→A) | 24.40 | 21.21 | **3.19** |
| 3 (A→B) | 24.59 | 21.25 | **3.34** |
| 4 (B→A) | 24.48 | 21.06 | **3.42** |
| 5 (A→B) | 25.38 | 21.13 | **4.25** |
| 6 (B→A) | 24.72 | 21.55 | **3.17** |

**All six positive, mean 3.56 ms a frame** — and `alloc_mb_per_frame` **20.77 → 11.12** on every one of
them, `gc_ms_per_frame` 0.36 → 0.20. The projection was the cadence's own 2.09 ms (#38's 16-v-128 pair);
the measurement is 1.7× that, because the ring also deletes `Grow`/`Compact` — the append-only log
reallocated whenever the head swallowed the array, and that landed in the allocation column, not in
`trim_ms`. `trim_ms` itself was 2.36 ms a frame on the Debug boot (708 ms over 300 frames) and is now
absent from the code.

**The measurement is not the cadence's, and the two are separable:** the one-line `_sampleFloor` reset
(§10) retained 770 → 223 records but changed no frame time measurably, while the ring changes both. What
the ring buys is the pass itself — 84 390 walks over every log per 300-frame boot — plus the array churn.

**Rule 1 of the ticket is answered *no*: a timeout wake is not clock-neutral here.** Armed engine-wide on
the four undated arms at `MaxSilence = 2 048`, a 300-frame boot takes **36 209 timed wakes** (120.7 a
frame) and **moves both folds**: `run.stream` `e52ed136…` → `276eb45d…`, `run.digest` `93c4dbd4…` →
`c601d745…`, with `subscriptions` and `stale_wakes` each +36 209 and `queue_pops` unchanged (a held date
is never a pop). The count is not a coincidence: 113 blank lines of 2 048 ticks a frame is 231 424 ticks
of silence on `group` — `231 424 / 2 048 = 113` wakes a frame, exactly the observation. **Any Δ short
enough to keep a 256-record window fires during a wire's legitimate silence** (0.36 frame on `group`,
3.84 on `MFPCS`), and any Δ long enough not to fire needs 3 240 `group` records and 12 300 `MFPCS`
records — a 16 384-record ring, 640 KB a log. The narrow variant (bounding only `group` and `MFPCS`,
leaving the two "what does it hold now" arms alone) fires the *same* 36 209 times and moves the same
stream, so the CPU's two arms were never the problem.

**What bounds the window instead**: the ring always holds the newest record, so an arm that only asks
"what does it hold now" cannot be starved; the arms that consume a *backlog* are the ones that need a
window, they carry an exact date (`DateInstant − From`), and assertion 8 is the witness that no reader
was ever answered below it. G7 (`check-window`) plus assertion 8 replace the retention row one for one.

**Found, not fixed: `DE`'s passive reader.** Assertion 8 fired on its first 300-frame boot —
`DE: a cursor at seq 380464 is below the window (oldest held 396478)` — because Timers and events scans
`DE` from a cursor advanced only when *it* is awake, and it is **not** a subscriber on `DE` although
`crossings.md` catalogues it as one (`TBI`, class 1, 200 counts a frame). No watermark ever bounded it;
under retention the 128-batch cadence happened to keep what it needed, which is why nothing noticed.
`DE` therefore runs a 4 096-record window (twice its measured need: more than 1 024, no more than 2 048)
and one silent loss on the boot is now impossible rather than unlikely. Making Timers subscribe — the
catalogue's own row — would return `DE` to 256 and is a machine change, so it is filed, not absorbed.

## 12. An abandoned arm is replaced, not appended (#50): 2.93 ms a frame, and both folds identical

A chip re-arms every wake on every resume, and `WaitForEither` appended each re-arm as a fresh pending
subscriber. A chip woken *n* times between two records of a wire therefore left *n* dead entries in that
wire's list, and the next record tore them out one `RemoveAt` and one stale wake at a time. #50's
instrument, on the 800-frame boot, per frame: Timers and events **6 373 resumes** and CPU / System
Control Unit / Video exactly one arm a resume, `DE` emitting 400 records — so **16 dead arms accumulate
per `DE` record**, 581 a frame on `MFPCS` alone. The whole tree paid it: `stale_wakes` **23 680 a
frame** (17 144 113 over 724 frames).

An arm the chip already holds on that wire at the same cursor is now **replaced** instead of appended
(`Engine.AddSubscriber`), so a wire's pending list holds one entry per (chip, wire, cursor). Nothing is
dropped: the replaced entry's token was already superseded, so the record that would have fired it
fires its replacement and resumes the chip exactly as before.

Six adjacent alternating Release pairs, 800 frames, load 0.55–1.05, `3eb1231` (A) against A + the fix
(B):

| pair | A | B | delta |
|---|---|---|---|
| 1 (A→B) | 21.86 ms | 18.99 ms | **2.87** |
| 2 (B→A) | 21.45 | 18.75 | **2.70** |
| 3 (A→B) | 21.66 | 18.86 | **2.80** |
| 4 (B→A) | 21.71 | 18.61 | **3.10** |
| 5 (A→B) | 21.55 | 18.76 | **2.79** |
| 6 (B→A) | 22.53 | 19.20 | **3.33** |

**All six positive, mean 2.93 ms**, `alloc_mb_per_frame` 11.12 unchanged, `gc_ms_per_frame` 0.18 →
0.17, `stale_wakes` 17 144 113 → 823, `drain_ms` 14 344 → 12 551 over 724 frames. **Both folds are
byte-identical**: `run.digest` `93c4dbd4…` / 10 801 967 words, `run.stream` `e52ed136…` / 10 801 251
on the 300-frame Debug boot, with `queue_pops` (25 589 670) and `subscriptions` (23 226 950) unchanged
too — a change that removes no record and no arrival number, exactly the shape #39's split was made to
express.

## 13. Timers subscribes to `DE` (#50): 0.56 ms for the arm, 2.15 ms for the ticket

#50 asked one question — what does it cost to make Timers and events **subscribe** to `DE` instead of
polling it, as `crossings.md` §2 has always said it does — and the pairs answered a second one first:
**the price was never the 400 `DE` records a frame on the fire path; it was the subscriptions the poll
had been hiding** (§12). With §12's fix in place the subscription is cheap, and the ticket nets out a
win:

| variant | what it is | mean frame |
|---|---|---|
| **B** | `3eb1231` + §12's fix — Timers polls `DE`, `DE` holds 4 096 records | 18.6–19.2 ms |
| **C** | B + Timers arms `DE` + `Engine.DeCapacity` deleted (`DE` at 256) | 19.0–19.7 |

| pair | mean delta | all pairs | alloc_mb_per_frame |
|---|---|---|---|
| B → C: the `DE` arm + the window | **0.56 ms** (against) | 0.20 0.57 0.58 0.89 (load 0.67–0.94) | 11.12 → 11.47 |
| A → C: the ticket as it lands | **2.16 ms faster** | 2.31 1.79 2.32 2.13 2.29 2.11 | 11.12 → 11.47 |

The `B → C` figure was taken twice: **0.56 ms** over four quiet pairs (load 0.67–0.94) and 0.83 ms over
six at load 0.85–1.94 — above the 1.5 floor, so quoted only as agreement. **The subscription is honestly
sub-millisecond and against in ten of ten pairs.** It is what a resume costs: 400 `DE` records a frame
become 400 resumes, and §3 puts a chip resume's own work at 1.0–2.6 µs — 0.4–1.0 ms. #44's projection
for a wake that starts no work (96 ns a pop, so ~0.04 ms for 400) is off by more than an order of
magnitude; that arithmetic has now failed to transfer twice (#40 was the first).

**The stream moves, and only in the chip's own anchors**: `run.stream` `e52ed136…` → `e0ca9570…`,
`run.digest` `93c4dbd4…` → `6e657033…`, same word counts, **628 words of 10 801 251 differ** (0.006 %;
#44 moved 711). All 628 are records of Timers and events' own internal logs:

- **601** (`timer D`, value `0x0002`) carry the *same instant* but a different `seq` (+0..+5) — the
  record lands in a different batch of the same instant, which is what a fresher chip clock does to an
  anchor;
- **27** (19 `register`, 8 `acknowledge`) move *forward* by 16…109 680 ticks, all in the same
  direction, because an internal anchor is stamped with the chip's clock (ADR 0008 §6) and that clock
  is now fresh instead of stale by up to 3.84 frames (§10's `MFPCS` lag). The anchor starts telling the
  truth about when the chip was last awake.

Every counter of the machine is identical: `instructions` 4 484 320, `bus_cycles` 10 300 388, groups
1 198 480, `DE` records 119 848 (400/frame), **Timer B counts 59 924 — 200/frame, G5's row, and the
release edge it does not count now wakes the chip 200 times a frame and is still not counted**, `irq`
1 167, `timer_c` 1 167, `frames_to_desktop` unchanged. Assertion 8 silent at `DE` = 256 on the
300-frame boot and on the gate's 800-frame boot — the assertion that failed at 256 before
(`DE: a cursor at seq 380464 is below the window (oldest held 396478)`). `window=default 256` now
describes every one of the 22 logs and `records_retained` fell 8 722 → 4 882: **no log has a window of
its own any more.**

The gate: **31 ok, 1 FAIL (G8 21.20 ms)**, G5 and G6 green, G3 silent from reset to the desktop.

*(The machine-wide counters above are the 300-frame Debug boot's. On the gate's 800-frame boot — 724
frames timed — the same three read `groups` 2 892 000, `queue_pops` 46 041 098 and `subscriptions`
50 131 313, and §14 quotes them that way.)*

**The rule this establishes, for every chip still a stub.** A wire a chip reads for *events* must be an
arm of its wake; a log may be *sampled* only for "what does it hold now" (a level read), never for a
backlog. This chip has four more event inputs: `SINT` (still a cursor, silent only because Sound is a
stub), `FDINT`/`DINT` (Disk), `BINT` (Blitter), and `GPI4` (Controllers — a `GPIP` level a read
answers, not a cursor at all). `WaitFor` takes one date and **two** wires, so `SINT` joins as the same
six lines when Sound drives a frame end. Until then assertion 8 is what says a cursor fell behind,
which is why it was worth keeping.

## 14. The live-wake ledger (#58): 2.66 ms of rent a frame, and the ticket's field that was not needed

`Engine` kept `Dictionary<Chip,int> _freshWakes`, written at **every** arm (`Engine.cs`, five sites) and
every resume (five more) and read only by assertion 2 — which lives inside `#if DEBUG`, so in Release it
was ~96 M dictionary operations a run that nothing read: 50 131 313 arms + 46 041 098 pops over the gate's
724 timed frames. The ticket projected **2.7–5.5 ms** from a 20–40 ns probe.

**The ticket's step 1 was "move the count onto the chip". It was measured first, and then not done.** Both
numbers it would have moved — `_freshWakes[chip]` and `Chip.Armed` — are bumped at arm and cleared when a
wake fires, so the ledger was a second copy of a counter the chip already holds. A temporary probe in
`AfterBatch` compared the two at every batch boundary of a 300-frame Debug boot and found them equal **at
every one**; a `FreshWakes` field on the chip would therefore have compared a number with itself in the
cheap half of assertion 2, and kept the duplication that was the rent. So the ledger was **deleted** and
assertion 2 rewritten around what the chips can still say on their own:

- every batch: the held dates against `_datedChips`, and the rule that a machine with nothing suspended
  holds no queued `Resume`;
- every 256th batch, unchanged: the full rebuild from the queue, the held dates and the subscription
  store, compared **chip by chip with `Chip.Armed`** — still two independent halves, and the one that can
  actually catch an armed-and-lost wake.

Six adjacent alternating Release pairs, 800 frames, load 0.91–1.64 (pair 6 at 1.64 quoted only as
agreement), `ea42288` (A) against the same tree without the ledger (B):

| pair | A | B | delta |
|---|---|---|---|
| 1 (A→B) | 20.16 ms | 17.62 ms | **2.54** |
| 2 (B→A) | 20.29 | 17.55 | **2.74** |
| 3 (A→B) | 20.48 | 17.90 | **2.58** |
| 4 (B→A) | 20.42 | 17.42 | **3.00** |
| 5 (A→B) | 20.08 | 17.66 | **2.42** |
| 6 (B→A) | 20.90 | 17.38 | **3.52** |

**All six positive, mean 2.80 ms — 2.66 ms over the five pairs under the 1.5 load floor.** The measured
number sits inside the ticket's projection and near its upper half; at 30 ns a probe over ~96 M probes the
arithmetic would predict 2.9 ms, which is the first time since #40 that a projection of this shape survived
contact at full size. `alloc_mb_per_frame` **11.47 in both** (a field write, no object), `gc_ms_per_frame`
0.19 both, `stale_wakes` 828 on the 300-frame Debug boot in both.

**Both folds are byte-identical**, and so is every counter the change could have touched:

| | `ea42288` | with the ledger gone |
|---|---|---|
| `run.digest` (300-frame boot) | `6e657033…` / 10 801 967 words | `6e657033…` / 10 801 967 |
| `run.stream` (300-frame boot) | `e0ca9570…` / 10 801 251 words | `e0ca9570…` / 10 801 251 |
| gate boot row (800 frames) | `0b0bf88f…` / `1a6102ff…` | `0b0bf88f…` / `1a6102ff…` |
| `instructions` / `bus_cycles` / `groups` | 4 484 320 / 10 300 388 / 2 892 000 | same |
| `queue_pops` / `subscriptions` | 46 041 098 / 50 131 313 | same |
| `stale_wakes` (gate) | 4 376 | 4 376 |

The gate: **31 ok, 1 FAIL**, G8 **18.07 ms** (was 21.20 ms on the same gate an hour earlier, same host,
same tree but for this change) — a third witness to the pairs, quoted across runs and therefore only as
agreement. G5 and G6 green, G3 silent from reset to the desktop, and the ADR 0008 §6 rows still show no
digest symbol and no `ASSERTION` text in Release.

**The rule this establishes.** A Debug instrument that keeps a *counting* structure pays for it in Release
at every event it counts. Before moving such a counter somewhere cheaper (which is what the ticket
proposed, and what #40 did for a chip's date), check whether one already lives there: a duplicate ledger
costs the write it was supposed to save, and its cheap-side assertion compares a number with itself. What
is genuinely independent — the queue and the subscription store — is worth a sampled walk, not a per-batch
copy. The same test applies to any future instrument written in `Engine.cs` outside `#if DEBUG`: **if
Release can read it, only Release's own rows can show its cost; if it cannot, it is rent.**

## 15. The CPU's coroutine, dissected (#47): 84 % of a bus cycle is the suspension, and the wrappers are worth 1.1 ms

#58's rule priced the instruments; this ticket prices the thing they were measuring. Three builds over the
**boot's own bus cycles** — not a synthetic loop — plus a fourth that is the design-neutral cut the
comparison turned up. Everything below is measured in `tools/cpu-dissection` on the laptop host
(i7-12700H × 20), `main` = `29a849d`, and **nothing in it lands on the machine**: the CPU sources are
linked from `src/` unchanged, `gen.py` derives the two variants from the same two files, and the whole
deliverable is a tool and this page. The gate is untouched and still reads 31 ok / 1 FAIL, G8 18.07 ms at
`runs/20261115T090825Z`.

### The workload, and why it is the boot's

A temporary capture (deleted before the commit, as #58's rule demands) dumped every bus cycle of a
**300-frame Release boot** — address, size, flags, control, data, the asserted instant, the answering
instant, the answer byte and the `MD` word — and every `IPL` record. The bench replays that trace into the
real `M68000` sources and checks, at **every** cycle, that the replay asserted the same four lanes at the
same instant; the pass ends when the trace does. A replay that drifts is not measured, it throws.

| what the trace is | |
|---|---|
| bus cycles | **5 564 478** (18 548 a frame over 300 frames of 640 000 ticks) |
| instructions | **2 655 080** (2.10 bus cycles an instruction) |
| reads / writes / acknowledges | 5 400 783 / 162 234 / 1 461 |
| `IPL` records | 188 979 (630 a frame) |
| latency, asserted → answered | **never 0**: mean 30.1 ticks, so *every* bus cycle suspends |
| sites | fetch 4 220 203 (75.7 %), data read 1 180 580, write 162 234, acknowledge 1 461, stop-wait 13 662 |

**Same work, proven four ways.** All four variants replay the identical trace to the identical end state —
5 564 478 bus cycles, 2 655 080 instructions, and the same `pc`, `sr`, `d0`, `a7` (`e02fe2`, `2320`,
`000005e2`, `00000e96`). No delta below is a workload difference.

### Experiment 1 — the same instructions, with and without continuations

| variant | what it is | ns / bus cycle | ns / instruction | ms a pass | MB a pass |
|---|---|---|---|---|---|
| **A** | the real CPU, real suspension, one queue entry and a `Resume` a cycle | **501.0** | **1 049.9** | 2 787.6 | 3 080.5 |
| **B** | the same code, every await already answered — no suspension, no `Resume` | **82.6** | **173.1** | 459.5 | 4.0 |
| **C** | the same code with no `async` at all (generated: `await` deleted, the two real waits direct calls) | **40.8** | **85.5** | 226.9 | 4.0 |
| **D** | A with the bus helpers folded into one leaf — one suspended frame a cycle instead of three | **421.6** | **883.5** | 2 345.8 | 2 638.7 |

Six alternating passes each, order mirrored every pass (A B C D, then D C B A), each variant a fresh engine
and a fresh CPU; the deltas are per adjacent pass, never across the run:

| delta | ms a pass | ns a bus cycle | unanimity | share of A |
|---|---|---|---|---|
| **A − B** the price of suspension | **2 343.5** | **421.1** | 6/6 | **84.0 %** |
| **B − C** the price of the state machine | **237.8** | **42.7** | 6/6 | 8.5 % |
| **A − C** the whole coroutine | **2 581.2** | **463.9** | 6/6 | 92.6 % |
| **A − D** the price of the wrapper frames | **446.7** | **80.3** | 6/6 | 16.0 % |

**A resume costs ~420 ns**, and the four deltas say what it is: 84 % of the CPU's time on a bus cycle is
*suspending and being resumed*, 8.5 % is the `async` machinery that never suspends, and about 7 % is the
CPU's actual work — decode, execute, log reads. The ticket's "2.60 µs a resume, the other 2.5 µs
unaccounted for" was measured on the Xeon host at a 65.8 ms frame; on this host, at this tree, the answer is
that **there is nothing else in it**: the 2.5 µs is not hiding in decode, it *is* the continuation machinery,
priced by removing it.

### What a suspension allocates: the box is per frame, not per chip

`GC.GetAllocatedBytesForCurrentThread` around each suspension (bracketed from the previous one, because the
box and the continuation `Action` are allocated before the engine is reached), a whole pass:

| | bytes |
|---|---|
| a `Wake` (the machine's `Resume`, same shape) | **48** |
| an `Action` (measured, one per suspended frame) | **56** |
| the state machine boxes `Unsafe.SizeOf` reports, boxed | 40–128 each (33 of them, 3 992 B if every one were boxed once) |
| **A's allocation a pass** | **3 080.5 MB over 5 578 140 resumes = 579.1 B a resume** |
| **D's allocation a pass** | **2 638.7 MB = 496.4 B a resume** (A − D = 83 B a resume) |

The per-suspension histogram is the answer to #46's first question: the modes are **496 B** (3.39 M
suspensions), **376 B** (0.83 M), **504 B** (0.39 M) and **1 176 B** (0.72 M), with a long tail — i.e. the
380 B of §8 is not "a box and a `Resume`", it is **a stack of frames**: the fetch chain is
`Run → FetchAsync → ReadWordAsync → Cycle`, a data read is that plus `ExecuteAsync → LineXAsync →
ReadEaAsync`, and **every frame in the chain boxes its own state machine and allocates its own
continuation**. `Run`'s box really is once per chip (the handoff's hypothesis is true there); every other
box is per *invocation*, and `Cycle`/`ReadWordAsync` are invoked once per bus cycle.

So the pooled-`Resume` idea (#46 question 3) can only reach the 48 B of 579 — 8 % — and the box is not one
object to pool but 4–6 per resume. That is #46's measured ceiling, and it is the reason the ruling below is
about *frames*, not about objects.

### Experiment 2 — the attribution, per site and per opcode line

Suspensions a bus cycle, by opcode line (the opcode the CPU was executing; a fetch belongs to the
instruction it is fetching), `susp/ins` and the mean bytes a suspension:

| line | instructions | suspensions | susp/ins | bytes/suspension |
|---|---|---|---|---|
| `2` MOVE.L | 374 065 | 1 841 651 | **4.92** | 619.9 |
| `3` MOVE.W | 411 362 | 1 546 468 | 3.76 | 647.9 |
| `6` Bcc/BSR | 1 044 028 | 1 050 006 | 1.01 | 497.1 |
| `5` ADDQ/SUBQ/Scc/DBcc | 365 418 | 424 685 | 1.16 | 530.0 |
| `0` ORI/ANDI/… | 14 247 | 32 724 | 2.30 | 706.8 |
| `1`/`b`/`d` byte, CMP and ADD forms | 400 369 | 448 441 | 1.12 | 620/503/526 |
| `4` | 38 843 | 222 065 | 5.72 | 531.7 |
| `9`, `c`, `e` | 4 290 | 9 506 | 2.2 | 496/619/554 |

**75.7 % of every suspension in the machine is an instruction fetch**, and a fetch is the *shortest* chain
the CPU has — four frames. The instruction mix drives the rest: a `MOVE.L` costs ~5 bus cycles and a
branch ~1, which is the whole difference between the lines.

### The ruling

1. **A design-neutral cut exists, and it is measured: fold the bus wrappers (D).** `ReadWordAsync`,
   `FetchAsync`, `ReadByteAsync` and the two write helpers are one-line wrappers around `Cycle`, so a bus
   cycle suspends **three** frames (the wrapper, the wrapper's wrapper for a fetch, and `Cycle`) where the
   design needs **one**. Variant D moves the assertion, the wait, the answer, the fault bookkeeping, the
   release and the fetch's `pc` advance into a single leaf, and the helpers become forwarders — same
   records, same instants, same order, byte-identical work. Measured: **80.3 ns a bus cycle, 446.7 ms a
   pass, 83 B a resume of allocation gone**, 6/6 unanimous, and at the gate's own density
   (14 227 cycles a frame) that is **1.14 ms a frame**; on this trace's denser frames (18 548) **1.49 ms**.
   Filed as **#62**, in the shape of #44, to be priced there over six pairs of the whole engine — this is a
   bench number and the ticket says so.
2. **The remaining 375 ns a cycle is irreducible inside the coroutine model without giving up the model.**
   The frames left are the *instruction layer's* — `Run → ExecuteAsync → LineXAsync → ReadEaAsync` — one
   per dispatch level, and they are the readability ADR 0009 bought. Nothing in this page prices that away:
   folding them is the flat dispatch switch, which is a design change, not a perf change.
3. **Therefore #42 is a real option, and here is its ceiling.** A CPU with no coroutine at all (C) costs
   40.8 ns a bus cycle against 501.0: **460 ns a cycle, 6.55 ms a frame at the gate's density, ~37 % of an
   18 ms frame**. That is the whole of the leaf's prize, and it is worth having — but no smaller step
   reaches it, and #49's re-evaluation now has the number it was waiting for.

### What this does *not* say

- The bench's machine is **one chip**: the CPU's four bus writes append records and fire nothing, so the
  `System Control Unit`'s answer cost and every subscription fire are absent. A's 501 ns a cycle is the CPU's
  own chain over the machine's own `Resume` shape, and the real engine adds the wake-token check,
  `DropDate` and the debug hooks around it. Treat it as a *share*, in the sense §3's 51 % was.
- The trace's 18 548 cycles a frame is the **300-frame** boot's, not the gate's 724-frame row (14 227): the
  early boot fetches far more per frame. Every per-frame figure above names which density it used.
- Variants B and C are **not emulators**: B answers the bus from the trace, so nothing waits on another
  chip; C is not even `async`. Nothing here is compared to a digest, and nothing here may be landed as a
  CPU. That is the ticket's own rule twice over.

## 16. The bus wrappers folded (#62): 1.44 ms a frame measured, against 1.14 ms projected — and both folds unmoved

§15's ruling 1 said the design-neutral cut existed and that the bench's number was a *share*; this is the
engine's own pair. One file moved, `src/TheST.Machine/Cpu/M68000.cs` (+35/−35): `Cycle` now does the fault
bookkeeping and, for a fetch, the `_pc` advance, and the five helpers are forwarders —
`ReadWordAsync`/`FetchAsync`/`ReadByteAsync`/`WriteWordAsync`/`WriteByteAsync` each name one leaf
(`ReadCycleAsync`, `ReadByteCycleAsync`, `WriteCycleAsync`) with no `await` in the wrapper. Nothing else in
the tree changed: no record, no instant, no wire, no arm, no clock.

**The landed code is variant D, not a rewrite of it.** `tools/cpu-dissection/gen.py` owns `FOLDED_BUS`, the
string that generated the fastest and stream-identical variant of §15's table; this commit applied that same
string to the source of record (a script that imports `gen.py` and calls its own `replace_block`, so a
transcription cannot creep in) and restored only the comments — the section divider, ADR 0009 §1's cycle
description, §7's address-error note, the release note, `FetchAsync`'s one-line summary. The bench and the
CPU therefore still cannot drift.

### The pair — six adjacent, alternating, Release, 800 frames (724 frames run)

`ab2.sh`, A = `115da43`'s Release tree, B = the fold, order AB BA AB BA AB BA, load 0.87–1.20:

| pair | A, ms a frame | B, ms a frame | A − B | `alloc_mb_per_frame` B |
|---|---|---|---|---|
| 1 (AB) | 16.87 | 15.70 | **1.17** | 10.42 |
| 2 (BA) | 17.21 | 15.76 | **1.45** | 10.42 |
| 3 (AB) | 17.23 | 15.64 | **1.59** | 10.42 |
| 4 (BA) | 17.37 | 15.59 | **1.78** | 10.42 |
| 5 (AB) | 17.18 | 15.80 | **1.38** | 10.42 |
| 6 (BA) | 17.05 | 15.80 | **1.25** | 10.42 |
| **mean** | **17.15** | **15.72** | **1.44** | **11.47 → 10.42** |

**6/6 unanimous, mean 1.44 ms a frame (−8.4 %)**, `alloc_mb_per_frame` **11.47 → 10.42 (−1.05 MB, −9.2 %)**,
`gc_ms_per_frame` 0.19 → 0.16. The counters that must *not* move did not: `bus_cycles` 10 300 388 in every
run, `pops_per_frame` 63 592.7, `stale_wakes_per_frame` 6.0, `pops_per_bus_cycle` 4.47. This is a shape
change and it reads as one — fewer boxes, the same machine.

### The measured pair beside the projection

| | ns / bus cycle | ms a frame at 14 227 cycles |
|---|---|---|
| **§15's bench, A − D (projection)** | 80.3 | **1.14** |
| **this ticket's six pairs (measured)** | **101.0** | **1.44** |

The projection was the *CPU's own chain* in a one-chip machine; the engine's frames also carry a `Resume`,
its queue entry, the wake-token check and the debug hooks, so removing a frame here takes more than removing
it there. #62's text expected the pair to read **less** than 1.14 ms because the bench's frames are denser
(18 548 against the gate's 14 227); it read **26 % more**, and the reason is the other direction of the same
asymmetry. Recorded as-is: the bench is a share, and this is the number that counts.

### What it costs and what it does not buy

- **The instruction layer is untouched.** `Run → ExecuteAsync → LineXAsync → ReadEaAsync` still suspends a
  frame each; §15 measured folding *those* at ~375 ns a cycle and this ticket's out-of-scope clause says so.
  A fetch chain is now `Run → FetchAsync (forwarder) → ReadCycleAsync → Cycle` — three frames to two.
- **#46's ceiling stands, and its tree has moved.** Pooling the `Resume` still reaches the 48 B of ~500; the
  fold took 83 B of that stack away by removing frames, not objects, so #46 prices what is left.
- **#42's ceiling is unchanged in principle, but smaller in share**: 6.55 ms was 37 % of an 18.07 ms frame;
  against 15.72 ms the same 6.55 ms is 42 %. A CPU with no coroutine at all still has to walk the
  instruction layer as a flat switch — D proves folding *two* of the frames is free, not that all of them are.
- **One declared nuance, on a path the boot never takes**: a *faulting write* now records `_faultAddress`
  (the old wrapper set only `_busFaulted`, leaving the address stale from the previous fault). The trace has
  no write fault, so neither the digest nor the stream can see it; it is the group-0 frame's address under
  ADR 0009 §7, and it is named here rather than hidden.

### The bench still works, and it now witnesses the fold

`python3 gen.py` then `check` on the **folded** sources: all four variants replay the boot's 5 564 478 cycles
to the same end state (`pc` `e02fe2`, `sr` `2320`, `d0` `000005e2`, `a7` `00000e96`) — a per-cycle check of
the landed CPU against the recorded wire traffic, independent of any digest. One fix was needed in the tool:
`gen.py`'s `strip_async` rewrote only `async ValueTask<T>`, so variant C stopped building once the wrappers
became non-`async` forwarders; it now rewrites the bare `ValueTask<T>`/`ValueTask` signatures as well. A and
D are now the same code modulo comments, so `bench`'s A − D row prices nothing but noise — the README says so,
and the pre-#62 contrast reproduces from `115da43`.

### The proof, three parts

1. **Folds byte-identical.** `boot --frames 300` (Debug): `run.digest` **`6e657033…` / 10 801 967 words**,
   `run.stream` **`e0ca9570…` / 10 801 251 words** — unmoved from `115da43`; the same run reads
   `instructions` 2 655 080, `bus_cycles` 5 564 479, `pops_per_frame` 85 698.4, `stale_wakes_per_frame` 2.8.
   The synthetic is untouched (`g1-a` 95 999 words).
2. **The pair above**, `alloc_mb_per_frame` beside it.
3. **`scripts/validate.sh` at `runs/20261115T100948Z`: 31 ok / 1 FAIL**, and the FAIL is G8 at its own
   figure — **16.58 ms** a frame of 20.00 (budget 10.00), n 724, 63 592.7 pops a frame. The row below G8 that
   verifies the fold's own witness stayed ok: `G1` on the boot, 10 801 967 digest words, identical.

## 17. A rented `Resume` (#46): 1.80 MB a frame of allocation removed, the frame unchanged — a measured no

The ticket's premise was stale in its own favour: it was filed against **380 B a resume and 20.77 MB a
frame**, and §15 had already split that stack (48 B the `Resume`/`Wake`, 56 B the continuation `Action`,
4–6 boxes of 40–128 B), while #62 then took the boxes of two frames away. On `af1c28b` a suspension
allocates **246 B** and a frame **10.42 MB**, so this ticket's whole prize is the 48 B of the queue node:
**~2.1 MB of the line, 20 %, and a GC total of 0.17 ms a frame.** It was measured anyway — the ticket says
so, and a no has to be a number, not an estimate.

### The three questions, answered

1. **How much of the 380 B is the box and how much the `Resume`?** §15: the box is *per async frame*, not
   per chip, 4–6 of them a resume at 40–128 B; the `Resume` is 48 B — one object per wake, and the only
   part a queue change can reach.
2. **Can a held date resolve without materialising a `Resume`?** It already does (#40): `SuspendForReadAt`
   and `WaitForEither` hold the date in the chip and `ExecuteDate` calls the continuation directly, so a
   dated wake allocates nothing. What is left is five `Enqueue` sites — the subscription fire
   (`FireSubscriptions`), the awaited write's own resume, and three same-instant resolutions — one
   `Resume` per wake, because the object *is* the queue node.
3. **Can it be a struct in a pooled indexed array?** That shape was not built, deliberately: the pooled
   *class* below is its conservative half, and it converts the same bytes. See the ruling.

### What was built (measured, then reverted)

`Resume` became rentable from an intrusive free list in `Engine` — no extra allocations for the pool
itself, `Rent`/`Free` on four fields, the continuation **cleared on release** so a pooled entry cannot keep
a finished state machine alive, `Entry.Instant` promoted to a member so a rented entry can take a new
instant, and an `Enqueue(Resume)` overload so the hot path reads the instant off the concrete type instead
of through a virtual call. Nothing else changed: same entry, same key, same order, same pop semantics.

| | A, `af1c28b` | B, rented `Resume` |
|---|---|---|
| `alloc_mb_per_frame` | **10.42** (all six runs) | **8.62–8.63** |
| `gc_ms_per_frame` | 0.16–0.18 | **0.13–0.14** |
| `run.digest` / `run.stream`, 300-frame Debug | `6e657033…` / `10 801 967`, `e0ca9570…` / `10 801 251` | **identical** |
| `pops_per_frame` / `stale_wakes_per_frame` | 63 592.7 / 6.0 | identical |
| gate | 31 ok / 1 FAIL, G8 16.58 ms | **31 ok / 1 FAIL, G8 16.36 ms** at `runs/20261115T105032Z` |

The 1.80 MB is ~39 300 objects of 48 B a frame — one resume per suspension, which is exactly §15's shape.

### The pair — six adjacent, alternating, Release, 800 frames, and two earlier attempts

| pair | A, ms | B, ms | A − B |
|---|---|---|---|
| 1 AB | 16.52 | 16.00 | +0.52 |
| 2 BA | 16.04 | 16.63 | −0.59 |
| 3 AB | 16.02 | 15.89 | +0.13 |
| 4 BA | 17.65 | 15.83 | +1.82 |
| 5 AB | 15.92 | 16.40 | −0.48 |
| 6 BA | 16.69 | 16.62 | +0.07 |
| **mean** | **16.47** | **16.23** | **+0.25, 3/6 positive** |

**Unresolved, not a win.** The spread (−0.59…+1.82) is the ±1 ms floor with one host blip, and the earlier
shapes agreed in the other direction: the plain pool (no `Enqueue` overload) read −0.26 ms over six pairs,
4/6 *against*, and that shape with the overload read −0.15 over five before the host rebooted. **Seventeen
pairs, three shapes, a mean of −0.05 ms** — the honest reading is "this change does not move the frame",
not "a small win" and not "a regression".

### The arithmetic that predicted it (write this first next time)

39 300 allocations of 48 B at ~10 ns is ~0.4 ms of *allocation* work a frame, and the rent/release path
gives most of it back (two reads, four writes, plus the continuation's null write) — and the collection
line this change can reach is the 0.17 ms of `gc_ms_per_frame`, of which it recovered **0.03**. A ticket
whose entire prize is a fifth of a 0.17 ms line cannot be resolved by six pairs at a ±1 ms floor.

### The ruling

[#46](https://github.com/laurentprudhon/the-st/issues/46) is **answered, not landed** (closed `not_planned`,
as #44 was): the allocation comes off the line — 10.42 → 8.62 MB, −17.3 %, GC pauses −0.03 ms, both folds
and the gate byte-identical — and the frame does not move. The change was reverted, because it buys a
mutable entry and a free list for a number nobody can see; the tree stays `af1c28b`.

- **The struct-union variant is not worth building.** `PriorityQueue<Entry, Key>` with an inline 48 B
  `Entry` struct would remove the same object *plus* the entry-kind allocation, but it copies 48 B per sift
  step in the queue that runs 63 592 times a frame and it loses the type switch that makes `Execute`
  readable. Its bytes are this ticket's bytes, and those bytes bought nothing.
- **The remaining 8.62 MB a frame is the coroutine itself** — state-machine boxes and continuation
  delegates, 4–6 per suspension at 40–128 B. No queue shape reaches it; that is #42's leaf, or a pooled
  *continuation* in the CPU, which is a different ticket and a different design question.
- **What to price next** from this line of enquiry: nothing in the queue. The queue's share of the
  allocation line is now 17 %, its share of the frame is unmeasurable, and `pops_per_frame` is the cost
  control that already exists.

## 18. The census of wakes (#48): what each chip is woken for, and what a wake is worth

#48 asked three things: a **three-bucket census per chip** (resumed with a record to consume / on a date it
computed / with nothing to do), the **SCU's machinery-versus-work split** by #47's method, and **a ruling
per chip**. All of it was measured on a scratch copy of `b289504` (`/opt/data/cache/scratch/st-census`) with
counters that never entered the tree: an origin tag on `Resume`, one work counter per chip, and a bracket
around the resume split by what the wake found.

**The census build is inert, and that is asserted rather than hoped.** With every counter in place and the
bracket off, the 300-frame Debug boot reproduces **`run.digest` `6e657033…` / 10 801 967 words** and
**`run.stream` `e0ca9570…` / 10 801 251 words** byte-for-byte, and every counter of the machine reads exactly
what the landed tree reads (`pops_per_frame` 63 592.7, `subscriptions_per_frame` 69 242.1,
`suspensions_per_frame` 44 417.5, `stale_wakes_per_frame` 6.0, `bus_cycles` 10 300 388, `groups` 2 892 000,
`chunks` 2 892 000). The *timing* build is not inert: its frame mean is 17.5 ms with the counters alone and
19.2–20.5 ms with the bracket on, against 16.2–16.7 ms for the tree — so the **shares** are the finding and
none of those frame means is quoted as a measurement.

### 18.1 Three families of wake, and why `pops_per_frame` is not the count of them

| family | per frame | allocates | counted by |
|---|---|---|---|
| a wire record fires a subscription — a queued `Resume` | **39 143.2** | a 48-B `Resume` | `queue_pops` |
| a held date fires — the chip's own date, `ExecuteDate` (#40) | **23 680.4** | nothing | nothing |
| a future-dated write — a queued `ApplyWrite` | **24 449.5** | a 48-B entry | `queue_pops` |
| **every resume of a chip** | **62 823.6** | | neither: the census |

`pops_per_frame` (63 592.7) is the **queue's** count: 39 143.2 `Resume` entries plus 24 449.5 `ApplyWrite`
entries. The 23 680.4 held-date wakes are not entries at all (#40 took them out of the queue) and appear in no
pop counter — which is the one thing #46's arithmetic could not see from outside. Its 1.80 MB a frame is
39 143 objects of 48 B, **not** 62 824: the pool reached the wire half of the machine's resumes and, as §17
said, nothing else. Its other half of the split — "~24 300 `ApplyWrite`" — is confirmed here to within 1 %.

### 18.2 The three buckets, per chip

Release, 800 frames asked / 724 run, `b289504` + census. Every figure is a count of `Execute`/`ExecuteDate`
invocations, divided by the 724 frames the run timed; the Debug 300-frame cross-check is in §18.6.

| chip | resumes a frame | by a wire | by a date | consumed a record | **found no work** | the work, per frame |
|---|---|---|---|---|---|---|
| CPU | **14 408.8** | 14 407.1 | 1.6 | — | **181.8** (1.3 %) | 14 227.1 bus cycles |
| SystemControlUnit | **38 001.7** | 20 335.5 | 17 666.9 | 20 340.0 | **6 112.9** (16.1 %) | 14 227.0 bus answers, 14 222.7 exchanges begun, 4 814.8 beam events |
| Video | **3 994.5** | 3 994.5 | 0 | 3 994.5 | **0** | 3 994.5 painted chunks |
| TimersAndEvents | **6 418.6** | 406.0 | 6 012.6 | 406.0 | **206.2** (3.2 %) | 6 012.6 timer expiries (6 008.7 of them Timer D), 199.7 `DE` counts |
| **the machine** | **62 823.6** | 39 143.2 | 23 680.4 | | **6 500.9** (10.3 %) | |

The third bucket is **10.3 % of every resume in the machine, and 94 % of it is one chip's one wake**: the
System Control Unit's 6 112.9. The CPU's 181.8 are wakes that complete no bus cycle (a stale `answer` arm, a
stop-wait's own line); Video's is empty; Timers' 206.2 are `MFPCS` register selects that find nothing to do
and re-arm — 3.2 % of that chip, 0.02 ms a frame, not worth a ticket.

### 18.3 What a wake costs, and how much of it is machinery

Three alternating runs of each direction, Release 800 frames, a `Stopwatch` pair per resume around the
dispatch and per resume around the continuation, all four chips in one process. A timestamp pair costs
**15.5–17.6 ns** measured in the same binary (median 15.7), and every figure below has one pair subtracted.
"Machinery" is the engine's own resume path — `Disarm`, the clock write, the two `#if DEBUG` hooks and the
continuation's invocation; "body" is the continuation itself: the state machine's `MoveNext` *plus* the
chip's own code, which this bracket cannot split.

| chip | resumes a frame | ns a wake | **ms a frame** | share of 16.5 ms | of which machinery |
|---|---|---|---|---|---|
| CPU | 14 408.8 | **620.3** | **8.94** | 54.2 % | 15.5 (2.5 %) |
| SystemControlUnit | 38 001.7 | **92.3** | **3.51** | 21.3 % | 8.2 (8.9 %) |
| Video | 3 994.5 | **133.3** | **0.53** | 3.2 % | 17.4 (13.0 %) |
| TimersAndEvents | 6 418.6 | **89.3** | **0.57** | 3.4 % | 15.7 (17.6 %) |
| **four chips** | 62 823.6 | | **13.55** | **82.1 %** | **~1.0** |

**The engine's resume path is 8–18 ns a wake — 1.0 ms a frame for the whole machine.** It is not where the
time is. What is left (92–620 ns) is the continuation: the coroutine stepping from its await to its next
await, which is the state machine's dispatch *and* the chip's own work in one number.

That number is the honest limit of this method, and it is worth saying why the SCU could not be priced the
way #47 priced the CPU. #47's variant B makes every awaiter complete inline; the CPU survives it because its
device answers on the stack and the chip advances its own `pc`. The System Control Unit **cannot**: its loop
progresses only when its clock moves, and its clock moves only when it is resumed — an inline awaiter spins
at the same instant forever, and a generated "no `async`" variant is the same infinite loop. Making the SCU
synchronous means rewriting `Run` as a stepper that returns its next instant, i.e. it *is* the design change
being priced (ADR 0008 §7's last resort), so it cannot also be its own measurement. What is left is two
bounds, and they agree: the CPU's state machine measured 42.7 ns of a 420 ns chain by #47's A − B (10 %), and
the SCU's cheapest real wake — the release wake that finds nothing — costs **60.3 ns** end to end. A
coroutine-free SCU may therefore save a fraction of 92 ns, not a multiple of it.

### 18.4 The System Control Unit: 2.67 wakes a bus cycle, and one of the three is #44's

14 227 bus cycles a frame, 38 001.7 resumes, and the wake's price depends on what it found:

| what the wake found | per frame | per bus cycle | ns a wake |
|---|---|---|---|
| nothing: a released `Z` at an instant that carries nothing else | **6 112.9** | 0.430 | **60.3** |
| an exchange to begin (the assertion lane, `0xFF00`) | **14 222.7** | 1.000 | **69.3** |
| the answer: its own held date fallen due | **14 227.0** | 1.000 | **113.3** |
| a beam event (line end, group, `DE` edge) | **3 434.7** | 0.241 | **131.3** |
| an answer **and** a beam event | **1 380.1** | 0.097 | **190.3** |

(Beam events are **4 814.8** a frame by work class: 3 434.7 wakes that handled one and 1 380.1 that did so with an
answer — plus 4.4 that only began the next exchange in the same wake, which is why the chip's own `scu_events`
reads 4 819.2.)

Two facts fall out of the census that the profile never showed:

- **The answer is the chip's own date, not the release wire.** `ExecuteDate` fires the bus cycle **10 300 387
  times — exactly `bus_cycles`** — and the `Z` release of that cycle is appended by the CPU *behind* it, in
  the same instant, on the CPU's own stack. Of the 10 300 387 wakes the release record causes, 5 874 660 also
  begin the next exchange (the next assertion shares the instant, so the chip is woken by the release and
  never by the assertion it wanted) and **4 425 727 find nothing at all**. Those are #44's 6 113 a frame, and
  the census counts them independently of that ticket: 4 425 727 / 724 = 6 112.9.
- **#44's pair is this bucket priced.** Removing those 6 113 pops cost **+0.63 ms** (5 of 6 pairs against)
  and moved `run.stream` by 711 same-instant orderings. This page says the same thing from the other end: the
  prize was 6 112.9 × 60.3 ns = **0.37 ms a frame**, and the lane filter that removes them costs more than
  that on every record of the widest wire in the machine.

So the SCU's 21.3 % is 1 exchange begun + 1 answer + 0.24 beam + 0.43 wasted wake per bus cycle, and only the
last is removable — at a measured loss. **Ruling: leave the wake.**

### 18.5 Video: one resume, one group, one chunk — the ticket's premise was a line

2 892 000 resumes over 724 frames = **3 994.5 a frame**, and `groups` = 2 892 000, `chunks` = 2 892 000,
`groups` arriving outside the display window = **0**. Video is **1 : 1 : 1**: one `group` record, one resume,
one painted chunk of 16 pixels (32 columns). The ticket's "2 892 000 resumes against 144 620 records — twenty
resumes a record" compared resumes with *lines*: `de_records` 289 200 and the 144 620 were the per-line
anchors (`Video.LineAnchors`), i.e. 20 groups a line, not 20 wakes a record. There is no per-line or per-`DE`
wake here at all, and no event the chip could compute instead of being woken for: the group *is* its work.

**Ruling: leave it, and correct the premise** — 0.53 ms a frame (3.2 %) for 2.9 M chunks.

### 18.6 Timers: Timer D expires 6 013 times a frame for a log nobody reads

| | per frame | what it is |
|---|---|---|
| `MFPCS` selects that only re-arm | 206.2 | the honest "nothing to do" of this chip: 3.2 % of it, 0.02 ms |
| `DE` counts (Timer B's input) | 199.7 | one per active line, exactly the 200 the video spec asks |
| **timer expiries** | **6 012.6** | of which **6 008.7 are Timer D** and 3.95 Timer C |

The emulated OS programmes **TCDCR `0x51`, TDDR `2`** — Timer D on divider ÷4 with a data register of 2, i.e.
**8 MFP clocks = 104 ticks**, and the census reads `_dExpiry − Clock = 104` at the end of the run. So the
machine expires Timer D **6 008.7 times a frame**, and every expiry

- wakes the chip (**87.3 ns**, the cheapest wake it has),
- appends an anchor to the `timer D` log — **4 353 103 records in 724 frames**, 5.9 % of every record the
  machine appends,
- recomputes the same deadline.

Nothing observes any of it. `timer D` has **no subscriber** and no reader in the machine (§10's census has it
at `needR` 1, the machine-wide floor, like every unwatched log), and `_dCounter` is never decremented — a read
of TDDR returns the value `RestartCounters` wrote, so the 4.35 M anchors carry one repeated number. The USART
this timer clocks is a stub (ADR 0013), which the chip's own comment says: *"Timer D is arithmetic only while
the USART is a stub: it counts, nothing observes it."* It counts 6 009 times a frame to nobody.

**Prize: 6 008.7 × 87.3 ns = 0.52 ms a frame, 3.2 % of it** — the anchor append is already inside that 87.3 ns,
so there is no second number to add. It is 91 % of this chip's own 0.57 ms and 3.8 % of the four chips' 13.55.

**It cannot land as a perf change**: 4.35 M records disappear from the stream, so both folds move and that is
the owner's re-bless (working agreement; #48's own out-of-scope clause says so). It is filed as
[#63](https://github.com/laurentprudhon/the-st/issues/63) in #44's shape, with the number above as its
projection and the re-bless as its first step.

Two things this page found on the way and does **not** file as perf tickets:

- **Timer B's interval mode has no expiry check.** `_bExpiry` is computed in `RestartCounters` and read
  nowhere: a Timer B programmed as an interval timer never fires. The boot uses event-count mode (TBCR bit 3)
  and the emulated OS never notices, so this is a machine gap for a timer-behaviour ticket, not a perf one.
- **The `MFPCS` wake is a re-arm, not a no-op.** The register write itself happens on the System Control Unit's
  stack (`Timers.Register`), and the record only exists so a suspended chip learns its deadline moved. It is
  206 a frame — nothing worth restructuring.

**The same census on the 300-frame boot** (Debug, 300 frames, 18 548 bus cycles a frame against the 800-frame
run's 14 227) — the counts are a property of the machine, not of the run, and the shape survives the density:

| chip | resumes a frame | found no work | the work |
|---|---|---|---|
| CPU | 18 593.8 | 45.5 (0.2 %) | 18 548.3 bus cycles |
| SystemControlUnit | 49 001.6 | **8 831.6** (18.0 %) | 18 548.3 answers, 18 545.9 exchanges begun (8 829.2 of them woken on the assertion lane), 4 817.3 beam events |
| Video | 3 994.9 | 0 | 3 994.9 chunks |
| TimersAndEvents | 6 232.3 | 205.6 (3.3 %) | 5 826.8 Timer D + 3.9 Timer C + 199.7 `DE` |

Every ratio holds to within the phase's different bus density: the wasted release wake is 0.48 a bus cycle here
against 0.43 there, Video is 1:1 at both, and Timer D is 5 827 against 6 009 expiries a frame — its period is
104 ticks, so the count follows the frame's own density and not the run's length. Both folds reproduce from this
run (`6e657033…` / 10 801 967, `e0ca9570…` / 10 801 251), which is what makes the counts in this table
quotable at all.

### 18.7 The rulings

| chip | ruling | the number it rests on |
|---|---|---|
| **SystemControlUnit** | **leave the wake** — 2.67 wakes a bus cycle is the exchange's own structure, and the only removable third (6 112.9 a frame) is #44's, measured at +0.63 ms to remove and ordering-moving | 92.3 ns a wake, 3.51 ms a frame (21.3 %) |
| **Video** | **leave it** — one resume per group, one chunk painted, zero wasted; the ticket's 20:1 was a per-line figure | 133.3 ns a wake, 0.53 ms (3.2 %) |
| **TimersAndEvents** | **cut the wake: Timer D** — a timer nothing observes, at 6 008.7 expiries a frame; filed as #63, re-bless first | 87.3 ns a wake, **0.52 ms (3.2 %)** |
| **CPU** | **nothing to cut** — 181.8 of its 14 408.8 wakes (1.3 %) find nothing, and its 54 % share is one instruction stream's cost, which §15's bench already priced | 620.3 ns a wake, 8.94 ms (54.2 %) |

**What #49 inherits.** The frame, by chip, measured: **CPU 8.94 ms (54.2 %)**, System Control Unit 3.51
(21.3 %), Video 0.53 (3.2 %), Timers 0.57 (3.4 %), and ~2.9 ms (17.9 %) of residual — 24 450 `ApplyWrite`
entries, the batch loop (36 007 instants a frame in the 300-frame boot's own count), GC's 0.17 and the debug
hooks. Chips on separate threads could at
the very best address the 27.9 % in the three device chips; the CPU's 54.2 % is a single instruction stream
under a deterministic `(instant, arrival)` contract and no thread count divides it. And the machinery this
page prices — the engine's resume path — is 1.0 ms of the 16.5. Whatever #42's leaf or #49's threads buy, they
buy it out of the 92–620 ns a wake that belongs to the coroutines, not out of the queue.

### 18.8 What this page does not say, and how it was proved

- **The census is counts, not time.** The counts are identical in Release and Debug (§18.6) and identical
  across runs; the ns figures are Release only, three runs each way, and carry a ±5 ns host drift that moves
  the CPU's row by 8 % between the first and last run of a session (615 → 670 before calibration). Every
  per-chip ms figure is that chip's mean wake times its count, not a difference of frames.
- **Two of the three buckets are counted by witnesses, not by intent.** "Consumed a record" is a chip's own
  cursor (the bus log's `Seq` seen, the group's `Seq`, the MFP select's `Seq`), and "found no work" is its own
  work counter — a bus cycle performed, an exchange begun, a beam event handled, a chunk painted, a timer
  expired, a `DE` counted. A wake that consumed a record and still found no work counts as **nothing**: that
  is exactly the `Z` release, and it is why the third bucket is not empty.
- **Nothing landed, so there is no pair to report.** The tree is `b289504` untouched (`git status --short`
  empty), and the proof is the tree's own: the reference pair `6e657033…` / 10 801 967 and `e0ca9570…` /
  10 801 251 reproduces *from the census build* with every counter in it, the gate's `G1` row reads **10 801 967
  digest words, identical**, the synthetic is `g1-a` 95 999 words `58fcdddf…`, and `scripts/validate.sh` at
  `runs/20261115T111934Z` reads **31 ok / 1 FAIL** with G8 red at its own figure — **16.89 ms** a frame of
  10.00, n 724, 63 592.7 pops a frame, `alloc_mb_per_frame` 10.42, `gc_ms_per_frame` 0.17, i.e. the same
  allocation line §17 left.
- **One gate run was thrown away, and why is worth a line for the next session.** A first re-run this session
  (`runs/20261115T111446Z`) read **18.54 ms** while two local `llama-server` processes were loading — load 1.5,
  which is this skill's own discard threshold — and the second, on the same tree, read 16.89. G8 on this host
  can read 16.6–18.5 without the machine changing; never read a single gate figure as a trend.

## 19. Runtime async (#65 L1): 3.72 ms a frame, 23 % of it — the .NET 11 bet, measured

#65's first rung compiles the machine's `async` onto the runtime's frames instead of compiler-generated state
machines: no box, no continuation `Action`, no `MoveNext`. **The source does not change** — same `await`, same
loops, same readability ADR 0008 §7 protects — and the measurement is **3.72 ms off a 16.01 ms frame, 6 of 6
pairs, with every machine counter and both folds identical.** It is the largest single step the ladder has
taken, and it is the return on the .NET 11 decision rather than a new one: ADR 0002 chose this runtime *for*
runtime-async and the project had never run the machine under it.

### 19.1 What landed: two property lines, and the trap that hid the first attempt

```xml
<Features>runtime-async=on</Features>
```

in `src/TheST.Machine/TheST.Machine.csproj` and in `tools/cpu-dissection/CpuDissection.csproj` (which links
the CPU's sources and would otherwise price a different compilation than the machine ships). It is a Roslyn
**feature** flag, not an SDK property: `Features` reaches `csc` as `/features:`.

**The witness is the emitted IL, and it has to be read on the machine's assembly.** `strings TheST.Machine.dll
| grep AsyncHelpers` hits, and the compiler's async state machines go **45 → 1**; the one left is
`<SubscriberSets>d__87`, an *iterator*, present in both trees. Nothing needed
`[RuntimeAsyncMethodGeneration(false)]`: no `ERR_UnsupportedFeatureInRuntimeAsync`, zero warnings with
`TreatWarningsAsErrors` on.

**The trap this page paid for, and it is a correction to the recipe, not a repeat of it.** The documented
shape — `dotnet build -c Release -p:Features=runtime-async=on -o <clean dir>` — is **not enough**: cleaning the
*output* directory does not clean the *intermediate* `obj`, and `CoreCompile`'s up-to-date check does not hash
`Features`, so the build reports success in 0.34 s and **copies the previous IL**. The first attempt here
produced a `TheST.Machine.dll` byte-identical to the baseline (`md5 5b5185bc…`) while `validation-harness.dll`
*did* differ — the harness project had been rebuilt, the machine had not. Two rules follow:

- put the flag **in the project file** (then it is part of every build's inputs, including the gate's own
  `dotnet build TheST.sln`), and `rm -rf obj bin` once after doing so;
- read the witness on `TheST.Machine.dll`. A harness-only check says "the flag took" when it did not.

### 19.2 The three-part proof

1. **Both folds byte-identical.** 300-frame Debug boot on the landed tree: `run.digest`
   **`6e657033…` / 10 801 967** words, `run.stream` **`e0ca9570…` / 10 801 251** words. The gate's own rows:
   `G1: 10801967 digest words, identical`, `G2 … identical to G1's stream`, the synthetic `g1-a` green.
2. **`tools/cpu-dissection` `check`: `same_work yes`.** All four variants replay the boot's **5 564 478**
   cycles to the same end state — `pc e02fe2`, `sr 2320`, `d0 000005e2`, `a7 00000e96` — with `gen.py`
   regenerated and the bench built under the same flag. (Regenerating `Gen/` produced no diff: the generator
   is still deterministic against the landed sources.)
3. **The pair** below, and `scripts/validate.sh` at `runs/20261115T121801Z`: **31 ok / 1 FAIL**, G8 red at its
   own figure — **12.42 ms** of 10.00, n 724, 63 592.7 pops a frame, `alloc_mb_per_frame` 7.93,
   `gc_ms_per_frame` 0.12.

### 19.3 The pair — six adjacent, alternating, Release, 800 frames, load 0.88–1.00

A = `cdcd9e8` built in this session (`rm -rf obj bin` first, §16's trap), B = the same tree with the two
property lines.

| pair | A, ms | B, ms | A − B |
|---|---|---|---|
| 1 AB | 15.80 | 12.30 | +3.50 |
| 2 BA | 15.98 | 12.19 | +3.79 |
| 3 AB | 16.25 | 12.21 | +4.04 |
| 4 BA | 16.06 | 12.21 | +3.85 |
| 5 AB | 16.01 | 12.50 | +3.51 |
| 6 BA | 15.95 | 12.31 | +3.64 |
| **mean** | **16.01** | **12.29** | **+3.72, 6/6 positive** |

**Every counter of the machine is identical**, which is what makes this a lowering and not a machine:
`pops_per_frame` 63 592.7, `subscriptions_per_frame` 69 242.1, `suspensions_per_frame` 44 417.5,
`stale_wakes_per_frame` 6.0, `bus_cycles` 10 300 388, n 724. What moved is time and bytes:

| | A | B |
|---|---|---|
| mean frame | 16.01 ms | **12.29** (−23.2 %) |
| mean on the desktop (150 frames) | 14.60 | **10.93** |
| frame max (pairs 1/2) | 154.03 / 158.04 | **115.81 / 114.61** |
| `alloc_mb_per_frame` | 10.42 | **7.93** (−2.49 MB, −23.9 %) |
| `gc_ms_per_frame` | 0.17 | **0.12** |

### 19.4 The prize, in the counter it lands in — and the ceiling it was written against

The frame, declared before the build. The handoff's ceiling was *at most 8.94 ms* (the CPU's share of §18's
continuation bill) and 13.55 ms across the four chips; #65 itself said **"0 to ~6 ms"**. Measured: **3.72 ms**,
inside both and 62 % of the ticket's upper bound.

- **59.2 ns off the average wake** (§18's 13.55 ms / 62 823.6 wakes = 215.7 ns a wake), i.e. **27.5 % of the
  continuation bill** §18 attributed.
- **Most of it is the CPU, and the bench says so independently.** `cpu-dissection`'s A row fell **400.2 →
  169.9 ns a bus cycle (−230.3, −57.5 %)**; 230.3 × 14 227 bus cycles = **3.28 ms**, which is 88 % of the
  machine's 3.72. The remaining 0.44 ms spread over the three device chips' 48 415 wakes is 9 ns a wake —
  the order those wakes would move by, and no more.
- **Against #8's prior, not against the ceiling.** #8 put ~165 of a suspension's 213 ns in "execution-context
  capture, state machine box, delegate"; a CPU wake here is **620.3 ns → ~392 ns**, a fall of ~228 ns. The
  prior was in the right place and under-stated the machine's case, because the machine's wake carries
  traffic the one-chip bench never modelled.
- **Allocation: 2.49 MB a frame, exactly 56 B a suspension.** That is one object a suspension — the
  continuation `Action` of §15 — and it confirms the shape: what runtime-async removes from the heap is *one
  object a suspension*, not the 4–6 boxes. The bench's own A row agrees in its own world: **496.0 → 299.8 B
  a resume (−196 B, −39.5 %)**.
- **§17's attribution of the allocation line is now falsified.** §17 said the 8.62 MB left after the `Resume`
  pool "is the coroutine itself — state-machine boxes and continuation delegates". Only **2.49 MB** of it was:
  **7.93 MB remains under a lowering that emits neither a box nor a continuation `Action`** (178 B a
  suspension, the harness's own bookkeeping included). What that line *is* has not been measured, so it is
  written here as an open question rather than a ticket — and the first thing to do with it is price the
  harness's own share before believing any of it is the machine's.

### 19.5 The bench under the new lowering — a new row, labelled, never an edit

Both rows are the same tree, same host, same session, `bench 6` (six alternating passes over the boot's
5 564 478-cycle trace); the only difference is the lowering. The left column is a **re-take in this session**
on the post-#62 tree — not §15's table, whose 501 / 81 / 40.8 ns were measured before the bus wrappers were
folded and must not be compared with either column here.

| variant (one chip, the boot's trace) | compiler state machines | **runtime async** |
|---|---|---|
| **A** — the CPU as it ships | 400.2 ns/cycle, 496.0 B/resume | **169.9 ns/cycle, 299.8 B/resume** |
| **B** — every answer on the stack | 75.5 | **53.5** |
| **C** — no `async` at all | 39.4 | **40.1** |
| **D** — the folded control | 377.5 | **169.3** |
| **A − B** (the suspension) | 324.7 | **115.4** |
| **B − C** (the frames, nothing suspends) | 36.1 | **13.6** |
| **A − C** (all of the coroutine) | 360.8 | **128.9** |

Two things in that table matter beyond this page:

- **`B − C` also fell, 36.1 → 13.6 ns a cycle, and `B` allocates nothing either way.** The runtime lowering is
  cheaper even where nothing suspends — ADR 0002's own "a completed await folds away" — which the machine
  never sees, because its fast-path share is **0.00 %**. That is the part of the lever this machine cannot
  use, and it is the whole of what the bench's B row buys.
- **`A − C` is the ceiling for any CPU coroutine elimination, and it is now 128.9 ns a cycle ≈ 1.83 ms a
  frame** — not the 6.55 ms #42 was filed on.

### 19.6 What this does to the ladder (#65 L2/L3/L4, #42, #49)

- **L2 is subsumed, as the ticket said it would be.** `PoolingAsyncValueTaskMethodBuilder` recycles the
  compiler's state-machine boxes; there are none. Close it as subsumed by L1, not as a no.
- **L3's projection is spent on arithmetic, and this is the sharpest consequence.** It projected **~375 ns a
  bus cycle / ~5.3 ms** for folding the instruction layer, from §15's D-variant shape applied one level
  deeper. The whole cycle now costs **169.9 ns** and the part a fold can reach — the frames themselves,
  `B − C` — is **13.6 ns a cycle, 0.19 ms a frame**. A lever whose projection is larger than the total that
  remains is not "re-derive it later": it cannot be worth what it says. It stays a design question for ADR
  0009 and it is no longer a 5 ms one.
- **#42's leaf has shrunk by two thirds.** Its 6.55 ms was "a coroutine-free CPU" against a then-18.07 ms
  frame; `A − C` says the whole coroutine is **1.83 ms** of a 12.29 ms frame now. The leaf also inherits a
  second reason to re-take its own number: its per-cycle projections were taken under the old lowering.
- **#49's threads lose absolute headroom and keep their share.** At §18's per-chip split and this page's 88 %
  CPU attribution: CPU **8.94 → 5.66 ms**, the three device chips **4.61 → 4.17 ms**, residual 2.9, frame
  16.45 → 12.73. The parallelisable share *rises* (27.9 % → 32.8 %) while the absolute prize falls by 0.44 ms.
  The split is an estimate from the bench's A row, not a per-chip measurement: re-running #48's bracket under
  the landed tree is what would make it one.
- **#63 (Timer D) is untouched** — a timer's expiry is not a coroutine frame, and its 0.52 ms projection stands.

### 19.7 The ruling

**L1 lands.** Two property lines, one commit: 3.72 ms a frame (6/6 pairs, load under 1.0), 2.49 MB of
allocation, both folds and every machine counter byte-identical, `check` `same_work yes`, the gate at
**31 ok / 1 FAIL** with G8 at **12.42 ms** — the first time this gate has read under 13 ms.

**ADR 0002's fallback sentence is now a tested claim, and it reads false.** *"If runtime-async proves
unavailable or unhelpful, we stay on .NET 10"* — unavailable: no, the toolchain carries it and the machine
binds it with no method opted out; unhelpful: no, it is **23 % of the frame and 57 % of a bus cycle's cost**.
The ADR's evidence section now carries the number (§"Revision — 2026-11-15, ticket #65"). Whether the
fallback sentence is struck is the owner's call — it is a language/ADR decision and this page only reports
what the machine measured.

**Two things are left for the next session, and neither is a lever.** The 7.93 MB that remains under a
lowering with no boxes (19.4), and the per-chip split of the 3.72 ms, which #48's bracket would give for the
price of one scratch build.

## 20. The ladder re-priced (#65's study): the CPU took 279 ns a wake, the device chips paid 0.55 ms, and the 7.93 MB is two things

The rung after L1 is not a lever: it is the study that turns two arithmetic estimates into measurements, because
every remaining rung (#42's leaf, L3, #49) was argued against a number that L1 had already deleted. Both questions
are answered here, from scratch copies that never entered the tree — and the second one now closes against the
gate's own allocation line to within 0.01 MB, which is the first time this page can say what the whole line *is*.

**No pairs, because nothing in the tree changed.** `main` stays at **`14b87f2`**, `git status --short` empty. The
proof is the tree's own: the 300-frame Debug boot of the census build reproduces `run.digest`
**`6e657033…` / 10 801 967** and `run.stream` **`e0ca9570…` / 10 801 251** byte-for-byte and **every machine
counter** of the landed tree's own 300-frame Debug boot (`pops_per_frame` 85 698.4, `subscriptions_per_frame`
84 054.9, `suspensions_per_frame` 55 223.8, `bus_cycles` 5 564 479, `frames_run` 300, `groups` = `chunks` =
1 198 480 — the two `run.state` files agree line for line except wall time and the counters' own Debug
allocation); the gate reads **31 ok / 1 FAIL** with G8 red at **12.42 ms**.

### 20.1 How it was measured, and the second lowering that makes it drift-free

#48's instrument, ported onto the landed tree in `/opt/data/cache/scratch/st-census-ra` (the port is three
`Origin*` tags plus one `if (TimingMode == …)` per mode — no machine line changed; the tree's `csproj` files came
along, so the copy builds under the same `runtime-async=on` and the witness is `AsyncHelpers` on
**`TheST.Machine.dll`**, §19.1's trap). Three modes, all from one binary: **0** counters only, **1** a `Stopwatch`
pair around the resume's dispatch, **2** around the continuation, **3** the allocation census of §20.4.

**The study's own addition is the second column: a copy with the flag deliberately off.** §18's per-chip figures
were taken under the compiler state machines, and a single-column re-take under runtime async could not tell a
real 8 ns move from this host's own drift — the same six runs in two thermal episodes read 13.2 and 18.4 ms a frame
for identical code. So the same instrument was built twice (`st-census-csm` with
`<Features>runtime-async=on</Features>` removed — 101 `<…>d__N` names against the RA build's 2, `AsyncHelpers` 0
against 1) and the two binaries were **interleaved run by run**, which is the only comparison in this page that a
hot host cannot fake.

Two things follow, and both are checked rather than hoped:

- **The instrument is inert and reproduces the previous session.** The CSM column reads **616.1 / 93.1 / 134.1 /
  91.1 ns** a wake against §18's **620.3 / 92.3 / 133.3 / 89.3** — the same instrument, a different session,
  within 1–3 %. That is what makes the RA column quotable next to it.
- **The counts are the tree's, and the bucket totals still add up.** 62 823.6 chip resumes a frame
  (`pops_per_frame` 63 592.7 − 24 449.5 `ApplyWrite` + 23 680.4 held-date wakes), and the three-bucket census
  reads the same 10.3 % "found no work" §18.2 published: CPU 181.8, SCU 6 112.9, Video 0, Timers 206.2, 6 500.9 a
  frame. Runtime async changed no count in the machine, which is §19.3's claim restated by a different instrument.

The probe cost was re-measured in each binary and each run (**15.6–18.1 ns** a pair, median 15.9 for the dispatch
and 15.6 for the body bracket) and is subtracted from every figure below.

### 20.2 The four chips, under both lowerings — the machine's split of L1

Release, 800 frames asked / 724 run, three runs each way per column, alternating binary and bracketing the frame
mean either side of the pair. `ns a wake` is the **dispatch** bracket with the probe's own pair subtracted;
`machinery` is dispatch minus body, i.e. `Disarm`, the clock write, the two `#if DEBUG` hooks and the
continuation's invocation, and none of it is where the time is.

| chip | resumes a frame | ns a wake, CSM | **ns a wake, runtime async** | Δ | ms, CSM | **ms, runtime async** | share of 12.29 ms |
|---|---|---|---|---|---|---|---|
| CPU | 14 408.8 | 616.1 | **337.1** | **−279.0** | 8.88 | **4.86** | 39.5 % |
| SystemControlUnit | 38 001.7 | 93.1 | **101.1** | **+8.0** | 3.54 | **3.84** | 31.3 % |
| Video | 3 994.5 | 134.1 | **164.1** | **+30.0** | 0.54 | **0.66** | 5.3 % |
| TimersAndEvents | 6 418.6 | 91.1 | **111.1** | **+20.0** | 0.58 | **0.71** | 5.8 % |
| **four chips** | 62 823.6 | | | | **13.54** (84.5 % of 16.01) | **10.07** (81.9 % of 12.29) | 81.9 % |
| residual (`ApplyWrite` dispatch, batch loop, GC, hooks) | | | | | 2.47 (15.5 %) | 2.22 (18.1 %) | 18.1 % |
| machinery, per chip | | 25.2 / 9.7 / 23.2 / 20.5 | 26.8 / 13.4 / 25.2 / 15.5 | | | | |

**Every one of those four moves is unanimous**, which is what a ±3–5 ns spread allows one to say: CPU 335/332/347
against 616/616/630; SCU 99/98/104 against 93/94/92; Video 165/160/165 against 130/134/134; Timers 109/110/122
against 91/92/89. The continuation bracket says the same thing from the other side (CPU 310.3 against 590.9,
SCU 87.7/83.4, Video 138.9/110.9, Timers 95.6/70.6), and **the machinery column is flat across the lowering —
the whole of the device chips' loss is inside the continuation**, not in the engine's resume path.

### 20.3 What that does to L1's 3.72 ms, and where §19.4 was wrong

The census and the pair agree, and they agree on a correction:

| | the census | the pair |
|---|---|---|
| CPU | **−4.02 ms** (−279.0 ns × 14 408.8) | |
| the three device chips | **+0.55 ms** (+8.0, +30.0, +20.0 ns) | |
| **net, four chips** | **−3.47 ms** | **−3.72 ms** (L1's six pairs) · **−3.77 ms** (bracket on, 16.01 → 19.78) · **−3.85 ms** (allocation mode, 13.66 → 17.52) |

**§19.4's attribution was wrong in both halves, and this is the page that says so.** It read 88 % CPU / 9 ns a
wake for the devices off `cpu-dissection`'s A row (230.3 ns a cycle × 14 227 cycles = 3.28 ms), which is a
**one-chip** world with no record appends and no subscription fires — §15 priced the coroutine, not the machine's
wake. Measured in the machine, the CPU's saving is **279 ns a wake, 4.02 ms — 108 % of the pair's net and 116 % of the
census's**, and the
three device chips did not share the win: they **paid 0.55 ms** for it, +8 to +30 ns a wake, unanimous in both
brackets. The bench's direction was right and its magnitude was 20 % low; the "0.44 ms spread over the device
chips" was not a gain at all.

**Why the device chips lose, stated as a hypothesis and nothing more**: their wakes complete their await on the
stack (the record is already there when the chip is resumed), they allocate nothing (§20.4), and runtime async's
inline path evidently costs a few ns more than a compiler state machine's `MoveNext` on a continuation this short.
The bench cannot see it — its B row is the CPU's own inline shape. It is 0.55 ms and it is a *rent* the ladder
should carry in the open, not a lever.

### 20.4 The 7.93 MB, attributed: 44 % the queue, 56 % the CPU's own suspensions, and no harness

§19.4 left this open with a warning — the number is process-wide, so price the harness first. Done, and the
harness is **zero**:

| | MB a frame | what it is |
|---|---|---|
| `alloc_mb_per_frame`, the gate's figure | **7.93** | 10.42 before L1 |
| of which this harness's own per-frame work | **0.000** | `GC.GetAllocatedBytesForCurrentThread()` around and outside `Engine.Run`, 724 frames, identical to the process total |
| queue entries, `Resume` | **1.79** | 39 147.6 a frame × **48 B** (measured in the binary, not quoted) |
| queue entries, `ApplyWrite` | **1.68** | 24 445.0 a frame × **72 B** |
| **the CPU's continuation, everything else** | **4.45** | 14 408.8 resumes × **324.2 B** |
| SystemControlUnit / Video / Timers, directly | 0.001 each | their wakes suspend nothing and allocate nothing |

**It closes, and the closure is the point**: 4.45 + 3.47 = **7.92** against the gate's 7.93, and the same
arithmetic on the CSM build gives 6.95 + 3.47 = **10.42**, which is exactly what `runs/20261115T111934Z` read
before L1. The census build's own total is 0.30 MB higher than the gate's in both columns because the census's
`Resume` carries an `Origin` tag (56 B against the tree's 48 B × 39 147.6) — so the instrument is priced, not
assumed.

**So L1 removed 181 B a CPU suspension, and that is the only allocation it touched**: 505.5 → 324.2 B a resume,
−2.491 MB a frame against the pair's −2.49. §19.4's "exactly 56 B a suspension — one object a suspension, the
continuation `Action`" divided 2.49 MB by `suspensions_per_frame` (44 417.5), which counts **dated reads and
subscriptions across every chip** — 30 000 of those are the device chips' wakes, which allocate nothing. Per CPU
suspension the compiler was allocating **181 B** (its box *and* its `Action`), not 56. The bench's own A row moved
496.0 → 299.8 B a resume, the same shape and a different world.

### 20.5 The rulings, one line per ticket whose premise moved

- **#65 L2 — pooled boxes: subsumed, not defeated.** There is no compiler box to pool under this lowering. Nothing
  to build, and this page is the ledger entry that says so.
- **#65 L3 — folded dispatch: dead on arithmetic, and further away than §19.5 said.** A fold can reach the frames
  only (`B − C` 13.6 ns a cycle ≈ 0.19 ms) while the whole cycle is 169.9 ns; and the mechanism a fold would
  install — answer inline instead of suspending — is the very path runtime async already made marginally **worse**
  for the short wakes (§20.2's +8 to +30 ns). A lever whose prize is 0.19 ms and whose own mechanism is a measured
  loss stays an ADR 0009 design question with no perf case.
- **#42 — the leaf's ceiling is now 1.64 ms, and it should be quoted as `A − B`, not `A − C`.** The leaf inlines
  *the suspension* (§7's one closed exchange), so its bound is the bench's **`A − B` 115.4 ns a cycle = 1.64 ms**
  of a 12.29 ms frame (13 %), not the whole coroutine's 1.83. Its seven conditions and the readability cost are
  unchanged; the prize is a sixth of the frame, not a third. Its case has to be re-made on that.
- **#49 — the chips' split is now a measurement, and the parallelisable share went UP to 42.4 %.** CPU **4.86 ms
  (39.5 %)**, SystemControlUnit 3.84 (31.3 %), Video 0.66 (5.3 %), Timers 0.71 (5.8 %) — **5.21 ms (42.4 %) in the
  three device chips**, residual 2.22 (18.1 %). §18's 27.9 % and §19.6's estimate of 32.8 % are both superseded:
  L1 made the device chips *more* of the frame by making the CPU cheaper. The ordering contract is still the
  binding constraint and no thread count divides it.
- **#63 — Timer D is now 0.67 ms, not 0.52.** A Timers wake costs 111.1 ns under the landed lowering (91.1
  before), so 6 008.7 expiries × 111.1 ns = **0.67 ms a frame, 5.4 %**, 94 % of that chip's 0.71. The one lever
  on the ladder with no design cost got *bigger* while everything else got smaller, and it still waits on the
  owner's re-bless — a moved `run.stream` is the owner's decision and not this page's.

**What this page does not say**: the ms columns are counts × a bracketed ns, and the runs they were taken in read
15.3–16.3 ms (RA) and 19.0–19.9 ms (CSM) because every wake carries two stopwatch pairs and the counters —
**the rent is ~3 ms and none of those frame means is quotable**. The pair is the number for the frame; the census
is the number for the *shape*, and the CSM column is what proves the shape's instrument against §18. The
reproduction is `/opt/data/cache/scratch/ra20/ab.sh` over `st-census-ra` and `st-census-csm`, with the mode in
`ST_CENSUS_TIMING` (`outer`, `body`, `alloc`).

## 21. Timer D (#63): the ADR's own decision, implemented late — 12.29 → 11.02 ms a frame, and the pair doubled the projection

ADR 0013 §4 has said since it was written that **"the chip's `nextDeadline` is the minimum over A, B and C"**,
because Timer D's only job is the USART baud clock and the USART is a stub. The chip did not do that: it armed
Timer D anyway, expired it **6 008.7 times a frame**, and appended an anchor each time — 4 353 103 records in 724
frames into a log with no subscriber and no reader, its counter never decremented, all of them carrying the same
repeated number. #48 filed the cut, the owner **re-blessed it on 2026-11-15** (a moved `run.stream` is his call),
and this section is the landing.

### 21.1 The change: one arming removed, no register semantics touched

`TimersAndEvents.RestartCounters` no longer arms `_dExpiry`; the counter is still loaded from `TDDR`, so a read
answers what the OS programmed and a write to `TCDCR`/`TDDR` still restarts it in the register sense — it just
produces no instant, no anchor and no wake. `ExpireTimerD` and its sub-tick remainder are gone, and the chip's
deadline is Timer C's alone. Reversal is the one line ADR 0013 §5 now names, for the day a serial ticket gives
the USART a producer.

**Exactly what moved, and what did not** (300-frame Debug boot, before/after, same tree otherwise):

| | before (#20's tree) | after |
|---|---|---|
| `run.digest` | `6e657033…` / 10 801 967 | **`2bfb229b…` / 9 136 634** |
| `run.stream` | `e0ca9570…` / 10 801 251 | **`238faddf…` / 9 135 915** |
| `records_overwritten` | 47 208 998 | 45 461 207 (−1 747 791 = every `timer D` anchor) |
| the logs in the window | 22 | **21** — the `timer D` log is no longer touched at all |
| `bus_cycles` · `timer_c` · `timer_b_counts` · `pops` | 5 564 479 · 1 167 · 59 924 · 85 698.4 | **identical** |
| `read_suspensions` (Debug 300) | 16 567 131 | 14 820 227 (−1 746 904) |

The synthetic is untouched (`g1-a` 95 999 words, `58fcdddf…`). This is #63's declared shape exactly: a dated wake
is not a queue entry, so no pop counter moves; the records and the two folds do, which is why it was the owner's
call and not a perf ticket's.

**The new reference pair is `2bfb229b1211215413613c954262673929dba864a363651e0350ff048d380c61` / 9 136 634 and
`238faddf3c4cd19e262284d5b80fc8dfb89ed477203c382cc1dc3f3a6faedf1c` / 9 135 915**, re-blessed by the owner on
2026-11-15; every older section of this page that quotes `6e657033…` / `e0ca9570…` is a record of what the tree
read *then* and is left as written.

### 21.2 The pair — six adjacent, alternating, Release, 800 frames, load 1.05–1.22

A = `dc1fed9` built in this session, B = the same tree with the arming removed (both built here, §16's trap).

| pair | A, ms | B, ms | A − B |
|---|---|---|---|
| 1 AB | 12.37 | 10.99 | +1.38 |
| 2 BA | 12.34 | 10.95 | +1.39 |
| 3 AB | 12.02 | 11.04 | +0.98 |
| 4 BA | 12.27 | 10.94 | +1.33 |
| 5 AB | 12.28 | 11.10 | +1.18 |
| 6 BA | 12.44 | 11.09 | +1.35 |
| **mean** | **12.29** | **11.02** | **+1.27, 6/6 positive** |

Nothing else in the machine moved: `alloc_mb_per_frame` **7.93** on both sides, `gc_ms_per_frame` 0.12,
`pops_per_frame` 63 592.7 (4.47 a bus cycle), `stale_wakes_per_frame` 6.0, n 724. What fell is the crossings:

| | A | B |
|---|---|---|
| `subscriptions_per_frame` | 69 242.1 | **57 224.8** (−12 017.3 = exactly two per expiry) |
| `suspensions_per_frame` | 44 417.5 | **38 408.9** (−6 008.6 = exactly the expiries) |

### 21.3 The measured number beside the projection, and why the pair doubled it

**Projected 0.67 ms** (§20.5, re-priced from §18.6's 0.52: 6 008.7 expiries × the Timers chip's 111.1 ns mean
wake). **Measured 1.27 ms** — 211 ns per expiry, 1.9× the projection, and 10.3 % of the frame.

The census priced *the wake*; the pair also collected what the arm cost everywhere else, and the Debug counters
say where:

- The arm put a **22nd log in every instant's trim**. `drain_ms` fell 21 282 → 20 404 ms over 300 frames
  (**−2.9 ms a Debug frame**, the same work the Release frame carries cheaper) for one log fewer, and
  `records_overwritten` fell by 1.75 M — 4.35 M anchors per 724 frames is ring traffic the per-wake bracket
  never sees.
- Each expiry also carried **two subscriptions** (the `MfpSelect` and `DE` re-arms) and one dated read, i.e. the
  chip's whole loop re-entered per expiry; when the loop runs 410 times a frame instead of 6 419, the work per
  visit is larger and the per-wake mean measured in the dense regime does not transfer.

So this is #62's and #65's rule again, in the same direction: **the pair beats the projection**, and the census is
the shape of the cost, never its size. It is also the reason #63's own arithmetic was right to say the pops would
not move: they did not.

### 21.4 The three-part proof

1. **Both folds, re-blessed by the owner**: `run.digest` **`2bfb229b…` / 9 136 634**, `run.stream`
   **`238faddf…` / 9 135 915** (300-frame Debug boot, §21.1's table for what moved and what did not).
2. **Six adjacent alternating Release pairs**, above: **12.29 → 11.02 ms, −1.27, 6/6**, `alloc_mb_per_frame` 7.93
   beside them.
3. **`scripts/validate.sh` at `runs/20261115T133640Z`: 31 ok / 1 FAIL**, G8 red at its own figure —
   **11.40 ms** of 10.00, n 724, 63 592.7 pops a frame, `alloc_mb_per_frame` 7.93, `gc_ms_per_frame` 0.12;
   `G1: 9136634 digest words, identical`, `G2` identical to G1's stream, the synthetic green.

**G8 is now 1.02 ms from green** (11.02 in pairs, 11.40 in the gate), where it was 2.29 ms short after L1. The
frame has fallen 65.8 → 12.29 → **11.02 ms** across this page's three landings.

### 21.5 What it does to the ladder

- **#49 — the device chips' share falls with the frame.** §20's table loses the Timers chip: 0.71 ms becomes
  ~0.04, so the three device chips are **~4.5 ms of 11.02 (≈41 %)** rather than 5.21 of 12.29 (42.4 %). The CPU's
  4.86 is unchanged and is now 44 % of the frame. #49's best case is the same order and its case is unchanged.
- **#42 — unchanged in absolute terms** (the CPU is not what moved), and its 1.64 ms is now 15 % of an 11.02 ms
  frame instead of 13 % of 12.29. Nothing in §20.5's ruling is disturbed.
- **#63 — closed.** This section is its Done: policy in ADR 0013, the pair re-blessed and recorded here, six
  pairs, the measured number beside the projection, the gate at 31/1.
- **No new lever is filed.** What is left unmeasured is still §20's residual (~2.2 ms before this cut, and the
  next session's step), and the two design-cost levers the owner has not asked for.


## 22. The residual, bracketed — and the instrument Release was paying for: 11.02 → 9.73 ms a frame, G8 green

§20 left exactly one line of the frame unmeasured: the engine's own per-instant work, ~1.6 ms of 11.02, sitting
between the four chips (measured to the wake) and a queue priced twice. This section brackets it. The bracket's
first result is that **the residual was not dispatch at all**: 1.40 ms of it was the harness-facing clock the
engine was reading four times an instant — in Release — to fill two lines of `run.state`. Removing that clock
from Release is **−1.40 ms a frame over six alternating pairs (6/6)**, and G8 is **green at 9.60 ms** of its
10.00 budget — 10.02 in the gate pass seven minutes before it — where #63 left it 1.02 ms short.

### 22.1 The instrument: one mode per site, and what makes it quotable

#48's shape, one mode per site, chosen by `ST_RESIDUAL_TIMING` and echoed into `run.state` (a mode switch whose
default arm is "off" turns a typo into nine successful-looking runs in the wrong mode — §20.4's rule):

| mode | the one pair it adds | per |
|---|---|---|
| `count` | none — the tallies only | — |
| `next` | around `NextInstant()` | instant |
| `execute` | around the whole `Execute` | queue pop |
| `resume` | around the Resume case's engine half (token test → `Disarm` → `Clock`) | resume |
| `cont` | around the continuation call | resume and dated wake |
| `apply` | around the ApplyWrite case | apply |
| `date` | around `ExecuteDate`'s engine half | dated wake |
| `branch` | all of the inner sites at once | — |
| `null` | every site's pair **with nothing between its two reads** — the rent | — |

**Proved inert before a ratio was quoted**: with every counter in and no pair, the 300-frame **Debug** boot
reproduces `2bfb229b…` / 9 136 634 and `238faddf…` / 9 135 915, `bus_cycles` 5 564 479, `timer_c` 1 167,
`timer_b_counts` 59 924, `queue_pops` 25 709 518, `alloc_mb_per_frame` 22.27 — every machine counter §21 landed.

**The counts reconcile against numbers that existed before the instrument** (Release, 800 frames / 724 run):

| | a frame |
|---|---|
| instants drained | **23 777.3** (Debug 300: 30 455.4, and there `batches` = 9 136 634 = the instant count, exactly) |
| `Resume` entries | 39 147.6 |
| `ApplyWrite` entries | 24 445.0 |
| the two together | **63 592.6** = the gate's `pops_per_frame` 63 592.7 ✓ |
| dated wakes | 17 671.4 — **not** pops and **not** suspensions: §18's 23 680.4 is the pre-#63 figure |

### 22.2 The split, net of the pair's own 16 ns

The rent is measured, not assumed: `null` mode runs the same code shape with the interval emptied, and it agrees
with a tight loop of empty pairs in the same binary (15.4 ns) to 0.6 ns — **16.2 ns a pair, uniform to 0.3 ns
across the six sites**, which is what says it measured the pair and not the work.

| line | ms a frame | an entry | share of 11.13 |
|---|---|---|---|
| chip continuations — 39 147.6 resumes | 6.44 | **164.5 ns** | 57.9 % |
| chip continuations — 17 671.4 dated wakes | 2.11 | **119.2 ns** | 18.9 % |
| `ApplyWrite` dispatch (append + fire + enqueue) — 24 445.0 | 0.45 | **18.4 ns** | 4.0 % |
| `NextInstant` — the head peek and the four-chip date scan, 23 777.3 | 0.17 | **7.2 ns** | 1.5 % |
| `Resume` dispatch, engine half — 39 147.6 | 0.14 | **3.7 ns** | 1.3 % |
| `ExecuteDate` dispatch, engine half — 17 671.4 | 0.02 | **1.2 ns** | 0.2 % |
| **the per-instant clock: 4 reads an instant** | **1.40** | 14.7 ns a read | 12.6 % |
| switch, dequeue, drain prologue, GC 0.12, the harness's own frame | 0.40 | | 3.6 % |
| **the frame** | **11.13** | | |

The column closes: 8.55 (continuations) + 0.79 (the engine's own sites) + 1.40 (the clock) + 0.40 (the rest) =
**11.14 against a pair mean of 11.13**, which is the whole reason this split is believed and a bracket with a
subtracted guess would not have been.

### 22.3 The arithmetic this settles

The handoff's estimate was "~1.6 ms ÷ 63 592.7 pops = **25 ns an entry** if the whole residual were queue
dispatch", to be checked against §18's 8–18 ns a wake. The bracket lands **below** both on the dispatch:
**18.4 ns an `ApplyWrite`, 3.7 ns a `Resume`**, and it says the 25 ns arithmetic had the right queue and the wrong
residual — 1.40 of the residual's 2.58 ms was the clock and 0.40 the loop's own bookkeeping, so queue dispatch was
never more than ~0.6 ms of it.

### 22.4 The lever: a Debug instrument that Release could read is rent

§14 and §58 already have this rule for *counters* (`_freshWakes`: 2.66 ms a frame by its own removal). This is the
same rule in its purest form, because the thing Release was paying for was not even a counter — it was the clock:

```csharp
long phaseStart = System.Diagnostics.Stopwatch.GetTimestamp();   // ×2, every instant, in Release
```

Four reads an instant over 23 777 instants, and the pair of values pinned `phaseStart` live across `DrainInstant`
and wrote two `Engine` fields — so a read in situ costs **14.7 ns**, not the 7.7 ns a bare read costs, and the
whole thing is 1.40 ms of an 11 ms frame.

Six adjacent, alternating Release pairs, A = `9e9dfa2` built here, B = the same tree with the four reads and the
two `run.state` lines behind `#if DEBUG` (load 1.01–1.16):

| pair | A, ms | B, ms | A − B |
|---|---|---|---|
| 1 AB | 11.07 | 10.04 | +1.03 |
| 2 BA | 10.85 | 9.79 | +1.06 |
| 3 AB | 11.11 | 9.50 | +1.61 |
| 4 BA | 11.18 | 9.75 | +1.43 |
| 5 AB | 11.21 | 9.70 | +1.51 |
| 6 BA | 11.37 | 9.60 | +1.77 |
| **mean** | **11.13** | **9.73** | **+1.40, 6/6 positive** |

`alloc_mb_per_frame` **7.93 on both sides**, `gc_ms_per_frame` 0.12, `pops_per_frame` 63 592.7, n 724, and B's
`run.state` carries no `drain_ms` (the guard worked; A's does).

**Why this is not a design question.** `drain_ms` was only ever quoted from a Debug boot (§21.3 reads "−2.9 ms a
Debug frame"), no gate row reads it, and ADR 0008 §6 had already decided that Release carries no digest — a
Release clock whose only output is a Debug report is that same decision applied to a second instrument. Debug is
untouched, so the folds are byte-identical **by construction**, and measured: the Debug 300 boot after the change
still reads `2bfb229b…` / 9 136 634 and `238faddf…` / 9 135 915, with `drain_ms` and `instrument_ms` still in its
`run.state`. **No re-bless is implied and none was asked for.**

### 22.5 The three-part proof

1. **Both folds**: unchanged — `run.digest` `2bfb229b1211215413613c954262673929dba864a363651e0350ff048d380c61` /
   9 136 634, `run.stream` `238faddf3c4cd19e262284d5b80fc8dfb89ed477203c382cc1dc3f3a6faedf1c` / 9 135 915. The
   synthetic `g1-a` is untouched (95 999 words, `58fcdddf…`), because the change cannot be reached from Debug.
2. **Six adjacent alternating Release pairs**, §22.4: **11.13 → 9.73, −1.40, 6/6**, allocation beside the mean.
3. **`tools/cpu-dissection check` — `same_work yes`**: all four variants replay the boot's 5 564 478 cycles to
   `pc e02fe2` / `sr 2320`, an independent witness of the CPU against recorded wire traffic (the CPU is not what
   moved, and this says so without the digest).
4. **`scripts/validate.sh` — PASS, every row ok, G8 green at 9.60 ms** of 10.00 (`runs/20261115T141652Z`), n 724,
   `alloc_mb_per_frame` 7.93, `gc_ms_per_frame` 0.12, `G1: 9136634 digest words, identical`, `G2` identical, the
   synthetic green. **Two passes are quoted, because the first one is the honest one to quote**: the pass 7
   minutes earlier (`runs/20261115T141246Z`) read **10.02 ms** — 31 ok / 1 FAIL, G8 red by 0.02 — with the same
   build and the same counters. The pair mean 9.73 sits between them, so what the budget is now decided by is the
   host's own ±0.3 ms and nothing in the machine.

**This is the first green gate on this page, and it is green by 0.4 ms — not by 1.4.** The reading that survives
both passes: G8 went **11.40 → 10.02 → 9.60** as the clock came out (the gate's own figures), the pairs say
**11.13 → 9.73** (6/6), and the two routes agree on the size of the change to within 0.02 ms.

### 22.6 The ruling

**The residual is closed, and nothing in it is a lever.** After the clock is gone the frame is 9.73 ms, made of
8.55 ms of chip continuations (88 %), 0.79 ms of the engine's own dispatch and 0.40 ms of the loop's bookkeeping,
GC and the harness. The kill-early rule's total test is met (the residual is 1.18 ms ≤ 1.3) and its share test is
met for everything except `ApplyWrite`, which is **4 % of the frame** — and that 4 % is ADR 0008 §1's one record
per write: an append into a 256-record ring plus a subscription scan. There is no shape change there that the
ADRs have not already priced (§20 closed the bytes; #46 and #44 closed the queue).

**The measurement lesson, for the next session.** `ApplyWrite` at 18.4 ns an entry is **1.1× the cost of the
stopwatch pair used to time it**. Per-entry work in this engine is now at the resolution limit of every bracket
this page owns: any further lever here has to be priced by *removal over six pairs*, never by a bracket — and, as
this section found, a bracket's own rent is 1.40 ms of the frame's 11, which is why the removal that found it cost
nothing to run and everything to believe.

**What each open ticket inherits:**

- **#42 — unchanged in absolute terms** (the CPU is not what moved) and now **17 % of a 9.73 ms frame** against
  15 % of 11.02. Its 1.64 ms ceiling and ADR 0008 §7's seven conditions are untouched; its case is still the owner's.
- **#49 — the same ~4.5 ms of device chips, now 47 % of the frame** (CPU 4.86 is 50 % of 9.73 — the two are of the
  same order, which was already #49's premise). Its ordering contract is untouched.
- **#38 — G8 is green, and by 0.4 ms**: 9.60 in the gate's second pass, 10.02 in the pass before it, 9.73 in six
  pairs. That is a *marginal* pass, and what the budget should *be* — and whether frame 1's ~110 ms of JIT belongs
  in a 724-frame mean (it is 0.15 ms of the mean) — is that ticket's decision, not a session's.
- **#65 — the ledger gains one row**: "the per-instant clock in `Engine.Run`" — no design cost, no machine
  counter, **1.40 ms**, and it has been in the tree since #34's retention work.
