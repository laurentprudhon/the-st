# What a suspension costs, natively — measured

Ticket [#8](https://github.com/laurentprudhon/the-st/issues/8). Answers, with numbers taken on this
repository's own toolchain, the question ADR 0002 rests on: *a read of an already-past instant returns
on the caller's own stack, without a task hop — what does it cost, and what does a genuine suspension
cost when it is not avoided?*

Everything here comes from `tools/suspension-bench/` on `main`, which is the artefact: a scheduler with
its own `SynchronizationContext`, eight synthetic virtual chips written as `async` state machines, and
the instruments below. **It is not an emulator** — no 68000, no video, no TOS image.

Every figure is labelled:

- **measured** — printed by the prototype in this container, and quoted with the number of runs.
- **derived** — arithmetic on measured figures or on the model's own constants.

## Where it was measured

| | |
|---|---|
| Container | 4 vCPU, 3.9 GB RAM, Linux (Fly), no public ports |
| CPU | Intel Xeon @ 2.30 GHz (AVX-512 present), shared vCPU |
| Runtime | .NET **11.0.0-rc.3** (`11.0.100-rc.3.26522.148` SDK), x64 |
| GC | workstation, concurrent GC off, default tiering (Tier0 → Tier1 PGO) |
| Build | `dotnet build -c Release` — compiler-generated async state machines unless stated |
| Run count | 3 runs per micro-measurement, median quoted; 3 runs per frame figure after a warm-up frame |

> **Note 2026-11-11** (not a correction — the figures were not re-measured). .NET 11 went GA on
> 2026-11-10: **11.0.0** runtime, **11.0.100** SDK. Every number in this note is an
> **`11.0.0-rc.3` measurement**, and it stays labelled that way. Re-running the whole set is one
> command on the new SDK (`tools/suspension-bench/README.md`, updated), and would be a fresh row, not
> an edit of these. The project's decisions do not depend on the difference: ADR 0002's falsifier is
> about the mechanism (asynchronous state machines, ordering, the fast path), and #10 carries the
> only figure whose threshold is absolute (50 Hz with 2× headroom, in release).

Two configuration facts belong with every number:

- **The state machines are the compiler's.** Runtime-async is opt-in and has no SDK property on this
  SDK: it is the Roslyn feature flag, `dotnet build -c Release -p:Features=runtime-async=on`, which
  emits `System.Runtime.CompilerServices.AsyncHelpers` in the IL (verified). A *default* build is
  therefore compiler state machines, which is the shipping configuration.
- **The anchor does not reproduce the ADR's 0.32 ns — until runtime-async is switched on.** See §1.
  This is the single most important finding in this document.

## 1. The anchor, and the correction it forces to ADR 0002's evidence

The ADR quotes two microbenchmarks: 0.32 ns for awaiting an already-completed `Task` (1e8 awaits in
32 ms) and ~53 ns for a real `Task.Yield`. Both were taken on a desktop. Reproduced here:

| Measurement (measured, median of 3) | compiler state machines | runtime-async on | ADR 0002 quotes |
|---|---|---|---|
| `await Task.CompletedTask` in a bare `async` loop | **2.68 ns/await** | **0.38 ns/await** | 0.32 ns |
| the same loop with the `await` removed | 0.37 ns/iteration | 0.37 ns/iteration | — |
| completed-await cost net of the loop | 2.31 ns | **0.01 ns** | — |
| `await Task.Yield()` to the default thread pool | 289 ns | 362 ns | ~53 ns |

**What this means.** The 0.32 ns figure is *the loop*, not the await: with runtime-async the completed
await folds away entirely (0.01 ns net) and what remains is the loop's 0.37 ns, which is exactly the
quoted number. Under compiler state machines — the default build — the completed await costs 2.31 ns
net, so the honest reading of ADR 0002's anchor is **"at most ~2.3 ns in the shipping configuration,
and ~0 with runtime-async"**, not 0.32 ns. The decision does not move (both are far under 20 ns) but the
evidence line in the ADR should.

The 53 ns yield does not reproduce either: 289 ns here, 362 ns with runtime-async. That figure was a
thread-pool post on a desktop with an idle pool; this container is 4 shared vCPUs with the pool already
busy. It is an upper bound on nothing useful and should stop being quoted as a target.

## 2. The mechanism, in situ

Measured inside the real dispatcher: the chip is running on a stack the scheduler resumed, the queue is
live, and the wire it samples carries records. The instrument is end-to-end wall time over 40 million
reads (one suspension at the start, then the whole loop on resumed stacks), because a per-read
timestamp costs 34 ns on this container — more than the read it would measure.

| Measurement (measured, median of 3) | Value | Notes |
|---|---|---|
| **already-past read, in situ** | **6.79 ns/read** | 6.72 / 6.79 / 7.09; runtime-async build: 6.9–7.2, unchanged |
| **genuine suspension** (dated read, woken by the queue) | **212.9 ns/event** | 210.8 / 212.9 / 218.3; 1 000 004 suspends |
| **awaited write** (`await WriteAndWait`) | **320.3 ns/event** | 274.5 / 320.3 / 324.7; 1 000 008 records |
| **continuation dispatch** (enqueue + pop + `Context.Run`) | **48.0 ns/event** | 45.9 / 48.0 / 50.2 |
| share of reads that take the fast path | **88.9 %** | measured under the stated mix (8 past reads : 1 future read per crossing) |

Reading the four numbers together:

- **The fast path is ~21× the ADR's quoted figure and still 3× under the 20 ns threshold.** That gap is
  the whole point of measuring in situ: 0.32 ns was the *language's* await; 6.8 ns is the *mechanism* —
  the awaiter's `IsCompleted` test, the wire-log probe, the loop-carried arithmetic, and a `Sample`
  call the JIT does not inline. Runtime-async does not move it, because the fast path never enters the
  runtime's async machinery: it is our own struct awaiter returning `IsCompleted == true`.
- **A suspension costs ~31× a fast read** (213 vs 6.8 ns) and allocates: it is the state machine's
  continuation delegate plus the queue entry. At the ADR's 10²–10³ crossings per frame that is
  21–213 µs per frame — 0.1–1 % of the 20 ms budget.
- **The awaited write is one and a half suspensions** (320 ns): the record append and its firing pass
  are cheap; the cost is the suspension itself. It is rare by design (ADR 0008 §2) and must stay rare,
  because at one per crossing it would double the frame cost.
- **Dispatch through the scheduler's own context is 48 ns**, so ~165 ns of a suspension is the async
  machinery's own bookkeeping (execution-context capture, state machine box, delegate), not the queue.

## 3. A frame at three crossing densities

The workload is the shape `design/crossings.md` describes: eight chips, one sync point per crossing,
device-like work between them. Each crossing costs one genuine suspension and eight fast reads; the
mix is a *stated assumption*, not a measurement — the real share belongs to #18.

| crossings/frame | ms/frame (measured, median of 3) | share of 20 ms budget (derived) | bytes allocated/frame (measured) | gen0 / gen1 / gen2 | GC pause ms/frame |
|---|---|---|---|---|---|
| **10²** | **0.119** (0.108–0.140) | 0.6 % | 6 443 | 0 / 0 / 0 | 0.000 |
| **10³** | **1.102** (1.049–1.201) | 5.5 % | 63 999 | 0–1 / 0 / 0 | 0.000–0.017 |
| **10⁴** | **1.575** (1.571–1.617) | 7.9 % | 634 545 | 2–3 / 0 / 0 | 0.009–0.019 |

- **Which figure came from where.** 10⁴ is the stale one: the ticket body says "~10⁴ per frame, per
  #18", which ADR 0002's DMA correction already cut to **10²–10³** (the owner's ruling that video and
  audio DMA are internal to the memory subsystem). #18 still owns the measurement. This document sweeps
  all three and does not choose; the frame cost at the ADR's own range is **0.1–1.1 ms/frame**.
- **Allocation is one continuation per suspension**, ~64 B: 6.4 KB per frame at 10², 634 KB at 10⁴.
  Nothing is allocated on the fast-path read.
- **GC is not a frame-pacing risk at these densities.** Zero gen2 collections; total pause 0.019 ms per
  frame at 10⁴ crossings, i.e. 0.1 % of a frame. At 10⁴ crossings the run does 2–3 gen0 collections per
  60 frames.
- The cost is **not linear in the crossing count** (10× crossings costs 1.4× at 10³→10⁴): the fast
  reads dominate the mix and their per-read cost in the aggregate loop is lower than the 6.8 ns the
  isolated instrument reports. Both are reported; the aggregate is the one that predicts a frame.
- **Tier0 is 6.5× slower**: the first (cold) 10⁴ run measured 10.3 ms/frame against 1.6 ms warm. Any
  per-frame budget must be a warm budget, and the shipping configuration is default tiering.

## 4. Determinism and topology

ADR 0008 §6's instrument: the digest is an FNV fold over the records of each instant, in append order,
folded into the run digest. Same input twice at T=1 must be byte-identical; T>1 must match T=1 too.

| Topology (measured) | ms/frame, 10³ crossings | digest over 3 runs | records later than machine time | matches T=1 |
|---|---|---|---|---|
| serial loop, T=1, two runs | 1.07 / 1.40 | **identical** (`07C1E672AA862114`) | 0 | — |
| pool, T=1 | 1.22 | stable | 0 | yes |
| pool, T=2 | 0.95 | stable | 0 | yes |
| pool, T=4 | 1.21 | stable | 0 | yes |
| pool, T=8 | 2.91 | stable | 0 | yes |
| pool, T=1/2/4/8, chips colliding on the same instants | 7.33 / 4.88 / 4.91 / 11.21 | stable | 0 | yes |

The pool topology tested is the naive one the ticket asks about: chips started on their own threads,
every scheduler touch under one global lock, `seq` assigned at insertion. Two readings:

- **The determinism falsifier did not fire.** Every digest was stable across runs and equal to the
  model's own order, including when all eight chips were made to land on the same instants. **But that
  is not evidence that the pool is safe**: the pool has no ordering discipline — it orders *insertion*,
  not the moment a continuation reaches its write — so a same-instant collision that actually interleaves
  would change the digest. The synthetic workload did not produce one; a real chip set, where two chips
  write unrelated wires in the same tick, is exactly what #11 must run. The honest statement is
  **"not observed to break here", not "proven deterministic"**.
- **Threads buy almost nothing and then cost.** 1.22 → 0.95 ms/frame at T=2 (22 %), no better at T=4,
  and 2.4× worse at T=8 — oversubscription on 4 vCPUs, not a capacity result. This is consistent with
  the prior art in `research/deterministic-threaded-simulation.md` (2–4 workers; commit-in-order needed
  for determinism) and with ADR 0002's "T=1 ships first".

## 5. The three nested machines against a flat `switch` (ADR 0009's falsifier)

Dispatch-shape microbenchmark, 20 M instructions, both variants advancing the CPU clock by the same
8 ticks per instruction, so the difference is the nesting and nothing else.

| Measurement (measured, median of 3) | Value | Threshold (ADR 0009) |
|---|---|---|
| three nested machines (3 switches, 3 state fields, 3 calls) | **10.6 ns/instruction** | — |
| flat `switch` (1 switch, 1 state field) | **15.3 ns/instruction** | — |
| difference | **−4.6 ns/instruction** (nesting is *not* slower here) | 125 ns = 1 CPU clock = 4 ticks |

The nested shape is not measurably slower than the collapse: per instruction it costs 0.34 ticks of the
32 MHz timebase against a threshold of 4. The negative delta is branch prediction, not structure — the
flat variant has four states per instruction where the nested one has three transitions — and it says
what the falsifier needs: **there is no nesting cost to buy back at the cost of the three machines.**

## 6. Falsifier verdicts

Quoted from their ADRs; none of the thresholds is invented here.

| Falsifier | Threshold | Verdict |
|---|---|---|
| **ADR 0002 #1 — mechanism** (fast path in situ) | > ~20 ns | **did not fire** — 6.79 ns measured. The ADR's *quoted* 0.32 ns is not reproduced (2.31 ns net in the default build, ~0 with runtime-async); the ADR's evidence line should be corrected, the decision stands. |
| **ADR 0002 #1 — same-instant order under load** | any two same-instant continuations running out of order | **did not fire** — stable digests at T=1/2/4/8, equal to serial, 0 late records, on both workloads. Not a proof (see §4). |
| **ADR 0002 #2 — language** (sync path, GC) | > ~100 ns, or GC costing frames | **did not fire** — 6.79 ns; GC pause ≤ 0.019 ms/frame at 10⁴ crossings, 0 gen2. Rust does not reopen. |
| **ADR 0002 #4 — .NET 11** | not a falsifier | runtime-async builds and runs; it removes the *anchor's* await cost entirely and leaves this mechanism's cost unchanged. Nothing to reopen. |
| **ADR 0009 #2 — CPU nesting** | > 1 CPU clock (4 ticks) per instruction | **did not fire** — 10.6 ns = 0.34 ticks; nested shape is not slower than flat. The three machines stay as designed. |

## 7. Recommendation, and what it costs

**Ship T=1: one scheduler thread owning the queue, the clock and the context.** The measurement is that
threads buy at most 22 % (1.22 → 0.95 ms/frame at T=2, nothing at T=4, −139 % at T=8 on 4 vCPUs) while
removing the only argument the design has for same-instant order — so the pool is not earned, and the
synchronous path costs 6.8 ns, which leaves the whole frame budget untouched at every density swept.

What would change the recommendation, stated so it can fire rather than be re-argued: a frame at the
real crossing density exceeding ~25 % of the 20 ms budget at T=1 (#10 and #18 measure this), or #11
finding a digest divergence at T=1 that a worker pool somehow cures.

**Open for the owner's ruling** — the ticket is HITL and this is the question it closed on:

> T=1 as the shipping topology, with the pool held as an unearned optimisation — or a fixed 2-worker
> pool added to the roadmap now, on the strength of the 22 % it measured?

**Ruled by the owner, 2026-11-07: T=1 only; the pool is not on the roadmap.** Recorded in
[ADR 0002 §Revision — 2026-11-07](../docs/adr/0002-c-sharp-on-dotnet-native.md) and on ticket #8. The
same ruling corrected the ADR's 0.32 ns anchor in place (it is the loop, not the await: 2.31 ns net in
the default build, 0.01 ns net with runtime-async).

## 8. What this does not measure

- **The real crossing density.** 10²/10³/10⁴ are the stated figures, not a trace; #18 owns the number.
- **The real chips.** Synthetic loads with a device-like read/write mix; a 68000 core's instruction
  stream, the memory subsystem's slot schedule and the video group delivery are all outside this
  artefact.
- **Same-instant contention with a real chip set.** The pool runs did not produce an interleaving that
  broke the digest; #11's job is to try to.
- **Runtime-async's effect on a production chip**, since production chips use this same custom awaiter
  rather than `Task`. Runtime-async's measured effect is confined to the anchor (§1).
- **Anything about frame pacing on the host** — that is #12's, and nothing here touches a display.

## 9. Reproducing

```
dotnet build -c Release                                  # in tools/suspension-bench
dotnet bin/Release/net11.0/SuspensionBench.dll all        # every measurement, ~2 minutes
dotnet bin/Release/net11.0/SuspensionBench.dll sweep      # the three crossing densities
dotnet bin/Release/net11.0/SuspensionBench.dll pool       # T=1/2/4/8, two workloads
dotnet bin/Release/net11.0/SuspensionBench.dll nesting    # ADR 0009's dispatch falsifier
```

Runtime-async variant: build the same sources with `-p:Features=runtime-async=on` in a **clean** output
directory (an incremental build silently keeps the previous IL — that is how the first run of this was
wrong).
