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
