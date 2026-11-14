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

And the wall those do not touch: **17.2 GB is allocated inside continuations, 380 B per resume** —
async state-machine boxes plus a `Resume` object for every wake. At the engine's measured 88 ns
suspend/resume floor, 45.2 M continuations is 4.0 s of the run before any work happens, and 77.6 M pops
is 6.8 s. That is why neither a dictionary rewrite nor an array of logs reaches 5 ms, and why the
remaining choice is [#38](https://github.com/laurentprudhon/the-st/issues/38)'s: the leaf, a restated
budget, or fewer crossings.
