# Deterministic multithreaded simulation

Prior art on deterministic parallel simulation, and what it implies for a 32 MHz Atari STE emulator whose
virtual chips each run on their own thread and exchange signal crossings through a global scheduler.

Two halves: **Part 1** surveys what has been published and shipped; **Part 2** turns it into recommendations
for this design. **Part 3** covers the browser/WebAssembly mechanics, because several of the Part 2 verdicts
are decided by Emscripten and `SharedArrayBuffer` behaviour rather than by simulation theory.

Claims are sourced. Anything I could not confirm at a primary source is marked **unconfirmed** and collected
in the appendix.

---

## 0. Scope, and one arithmetic correction

The design under discussion: one machine time derived from a 32 MHz clock, one tick = 31.25 ns; virtual chips
as independently progressing state machines; "signal crossings" as the only significant instants; same-tick
ordering decided by hardware priority.

A PAL STE line is 64 µs (15 625 Hz). **At 32 MHz that is 2 048 ticks, not 512.** The 512 figure in the brief
is the 8 MHz CPU-cycle count (`512 × 125 ns = 64 µs`), which is also the range of the `h` counter in
§6.3 of the hardware overview. The distinction matters for every budget below, so all figures here are given
in both units: 1 024 ticks = 256 CPU cycles = 32 µs.

| Quantity | 32 MHz ticks | 8 MHz cycles | Wall time |
|---|---|---|---|
| One tick | 1 | ¼ | 31.25 ns |
| One 68000 bus cycle | 4 | 1 | 125 ns |
| One PAL scanline | 2 048 | 512 | 64 µs |
| One PAL frame (312.5 lines) | 640 000 | 160 000 | 20 ms |
| One emulated second | 32 000 000 | 8 000 000 | 1 s |

Terminology mapping to the parallel-simulation literature, used throughout:

| This project | PDES literature |
|---|---|
| Virtual chip | logical process (LP) |
| Signal crossing | timestamped event / message |
| Machine time | simulation time (virtual time) |
| Same-tick ordering | tie-breaking at equal timestamps |
| Lazy advance (§10.3 rule 3) | lookahead / state-update skipping |
| Memory service (§4.3) | shared resource arbitration inside an LP cluster |

The glossary's insistence that crossings are the only significant moments is, in PDES terms, a statement that
the model is event-driven with a specific event set — which is the strongest structural asset this design has.
It is also what makes the literature applicable at all: a per-tick design would have no such mapping.

---

# Part 1 — Prior art

## 1.1 The two families, and the constraint both must satisfy

Parallel discrete-event simulation (PDES) exists to satisfy the **local causality constraint**: events within
each logical process must be processed in timestamp order, and messages must be delivered in timestamp order.
Fujimoto's formulation is worth quoting exactly, because of its parenthetical:

> Adherence to the local causality constraint is sufficient to ensure that the parallel simulation will produce
> exactly the same results as the corresponding sequential simulation — *provided events with the same time
> stamp are processed in the same order as in the sequential execution*.
> — R. M. Fujimoto, *Time Warp: Local Control* (course slides), <https://sigsim.acm.org/mskr/Courseware/Fujimoto/Slides/FujimotoSlides-11-TimeWarpLocalControl.pdf>

That parenthesis is the whole determinism problem in one sentence. Every synchronisation protocol below is a
way of buying timestamp ordering; **none of them supplies same-timestamp ordering**. That must come from the
model, which is why §10.3 rule 6 of the hardware overview ("same-time ordering is explicit") is not a
refinement — it is load-bearing.

The two families:

- **Conservative** — never process an event until you can prove no earlier event can arrive. Blocking; no
  rollback; needs *lookahead* (a bound on how far ahead a process can safely run).
- **Optimistic** — process immediately, detect causality violations, roll back. No blocking; needs
  checkpointing, anti-messages and a global virtual time (GVT) for fossil collection.

## 1.2 Conservative synchronisation: null messages, lookahead, and the cost curve

The first-generation conservative protocol is Chandy–Misra–Bryant (CMB): a process with nothing to do sends a
*null message* carrying its next-safe timestamp, so neighbours are never blocked forever. Second-generation
variants replace null messages with the timestamp of the next scheduled event, and deadlock-recovery variants
allow deadlock and break it. The lineage and its dependence on lookahead are documented in the conservative
DEVS literature, e.g. <https://dl.acm.org/doi/10.1145/1878537.1878683> (which names Bryant 1977 and Chandy &
Misra 1979 as the origin) and the ESM lookahead paper at
<https://scs-europe.net/services/esm2004/pdf/hpc-08.pdf>.

The economics are simple and unforgiving:

```
useful work per synchronisation  ≈  lookahead / (lookahead + message latency)
```

As lookahead → 0, conservative synchronisation degenerates into a barrier at every timestamp, and speedup goes
to zero (or negative). The null-message traffic itself becomes the bottleneck: each null message is a
synchronisation point that costs the same as a real one.

**This is the single most important fact for the STE.** The STE is a machine in which every significant client
(CPU, video fetch, sound fetch, disk DMA, Blitter) touches the *same* DRAM array, and the hardware resolves the
contention with an arbiter. Two virtual chips that can both write the same address within a few ticks have
lookahead ≈ 0 between them. Splitting them across threads does not create parallelism; it creates the worst
case of the conservative protocol.

The escape is that the STE's contention is **not** peer-to-peer. §4.3 of the hardware overview is explicit:
bus mastership and memory service are two different decisions made by two different pieces of logic, and the
GSTMCU sits in the middle of both. That is architecturally a *central sequencer*, not a message-passing
network. Centralised arbitration means the machine has a single, hardware-defined serialisation order — which
is a gift for determinism and a ceiling on parallelism. Part 2 builds on exactly this.

## 1.3 Barrier-per-tick versus event-driven advance

Four points on the spectrum, all in production or research use:

**(a) Delta-cycle / per-timestamp barriers.** Every process runs one delta, then all synchronise, at every
timestamp. This is the classic VHDL/SystemC scheduler semantics. parti-gem5's survey of the design space puts
it plainly:

> delta-cycle simulators … enforce synchronisation at every single timestamp. However, limited performance
> gains are observed in typical FSS scenarios as the amount of simultaneous activity is often not high enough
> to overcome the synchronisation overhead.
> — *parti-gem5: gem5's Timing Mode Parallelised*, <https://arxiv.org/pdf/2308.09445>

**(b) Quantum / window-based barriers (synchronous).** Time is divided into windows of length `t_qΔ`; threads
advance independently inside a window and meet at a global barrier. Higher speedups, at the cost of a
controlled accuracy deviation. This is the same paper's preferred family, and it is what gem5's design notes
call "a barrier event that can be used to synchronize sets of event queues" plus "point-to-point
synchronization events for controlling the slack between event queues"
(<http://gem5.org/Parallel_M5>).

**(c) Asynchronous with lookahead (Manifold, SCope).** Each thread tracks every other thread's time and may run
ahead by at most `t_la` — the maximum look-ahead distance — past the slowest thread. No global barrier; the
slowest thread sets the pace. Cited in parti-gem5 as the approach of Manifold and SCope.

**(d) Per-eval barrier inside a compiled hardware model.** Verilator's multithreaded mode coarsens the
statement-level DAG into "macro tasks" and schedules them statically onto threads, with a synchronisation at
each `eval()` — i.e. each clock edge. The author's own Gantt analysis on 4 threads reports "longest-thread
efficiency = 30.4%", "all-thread speedup = 2.1" (and his own annotation: "long period of single thread
active; mtask 20 is taking too long")
(<https://veripool.org/papers/Verilator_v4_Multithreaded_OrConf2018.pdf>).

Two lessons from Verilator, both non-obvious:

- **Coarsening is what makes barriers affordable.** "Millions of micro-tasks merge into tens of macro-tasks.
  Runtime synchronization becomes affordable." Granularity is chosen to amortise the barrier, not to match the
  hardware's finest observable behaviour.
- **Dynamic per-cycle scheduling was tried and rejected**: "Every-cycle dynamic runtime assignment was tried
  but slow!" — the shipped design is a *static* assignment computed offline from a runtime estimate. If a
  design needs a load-balancing decision every cycle, it has already lost.

**(e) Cooperative (userland) threading — the outlier worth knowing.** bsnes/higan runs every chip as a
userland coroutine, switching stacks in ~"twelve lines of assembler", so there is exactly one OS thread and no
synchronisation at all. byuu measured the trade-off and states both sides: a fiber switch costs "at worst 50
host CPU cycles, whereas a kernel thread can be thousands" (his phrasing, echoed by yuzu below), but in the
worst case "cooperative threading can be somewhat slower than the state machine approach" because every switch
flushes the pipeline, and — the sentence that matters here — "if you have two CPUs that share all of the
memory address space with one another … you'd always have to synchronize" (hence the "just-in-time
synchronization" optimisation that only synchronises on accesses to genuinely shared regions).
<https://github.com/higan-emu/emulation-articles/blob/master/design/cooperative-threading/README.md>

That last point is the STE in one sentence. Cooperative threading works on the SNES because the CPU and APU
have *disjoint* address spaces and cross over a handful of mailboxes. On the STE, CPU, Blitter, video fetch and
sound fetch all address the same RAM.

## 1.4 Optimistic synchronisation (Time Warp) and why it is a bad fit here

Time Warp (Jefferson) lets a process run ahead speculatively, keeps a **state queue** (checkpoints or
incremental state saving), sends **anti-messages** to annihilate incorrect outputs, and uses **global virtual
time** to reclaim state and to decide when irreversible operations may be performed
(<https://sigsim.acm.org/mskr/Courseware/Fujimoto/Slides/FujimotoSlides-11-TimeWarpLocalControl.pdf>;
original: <http://cs.uiowa.edu/~ghosh/VirtualTime.pdf>).

Against this machine, each of its four requirements fails:

1. **State size.** A checkpoint is not a few registers; it is the virtual chip's whole state *plus whatever it
   has written to shared RAM*. The 68000 has 1–4 MB of directly addressable state that any chip may modify.
   Checkpointing per event is out of the question at ~20 000 instructions per frame, let alone per tick.
2. **No cheap inverse.** Incremental state saving for a CPU register is a two-word undo record. The inverse of
   "Blitter wrote 400 words to screen memory" is an undo log of 400 address/data pairs, and the inverse of a
   *read* (which the emulated program can observe via a register value) has no representation at all in a
   forward-execution model.
3. **Lookahead is not the issue — the issue is that rollback cannot fix the thing that goes wrong.** Time Warp
   repairs *causality* violations (an event arriving in the past). The STE's interesting hazards are
   *same-tick* contention: video fetch vs Blitter vs CPU write, all nominally at the same instant. There is no
   straggler to detect; there is a priority decision to make. Rollback cannot discover a priority.
4. **Irreversible, observable output.** Video and audio are delivered to the user, and the emulated program can
   read back hardware state (the video counter at `$FFFF8205`, a live timer) to observe when things happened.
   A speculative read that is later rolled back has already answered the program. GVT-gated I/O commit (the
   standard fix) means every frame's output is held a full lookahead behind, adding latency for no fidelity
   gain.

There is also a category error: an emulator is not trying to find *a* consistent schedule. It is trying to
reproduce **the** schedule the silicon produced. Optimism explores orderings the hardware never had.

**Verdict: Time Warp is out.** Worth recording, because "we could always add rollback later" is a tempting
escape hatch that would be a rewrite, not an increment.

## 1.5 Why same-timestamp ordering needs an explicit priority, not arrival order

Three independent lines of evidence.

**(a) The theory says so.** Fujimoto's caveat above: local causality is sufficient *only* if equal timestamps
are ordered as in the sequential execution. Conservative protocols that keep every process' event queue sorted
by `(timestamp)` alone are still nondeterministic in the presence of ties; sorted by `(timestamp, priority)` they
are not.

**(b) The shipped simulators say so.** gem5's parallel design notes make determinism a hard requirement, not a
nice-to-have:

> Parallel simulation should be deterministic by default. This is a necessity for maintaining developer sanity.
> Modes that optionally forgo determinism to provide enhanced performance are OK, but they should not be the
> default mode of parallel execution.
> — <http://gem5.org/Parallel_M5>

A tie broken by "whichever thread got there first" is a function of host scheduling, cache-line ownership and
CPU frequency — none of which are part of the model. Under that rule, two runs of the same TOS image can
diverge, and a demo that depends on a mid-line register write will pass on the developer's machine and fail on
a user's.

**(c) Timing analysis of the browser runtime says so.** Cross-core signalling has a floor set by cache
coherency, not by the program: measured signal-to-wakeup latency for a spinning atomic handoff is 70–130 ns on
x86-64 and 170–190 ns on ARM64, versus 3.7–13 µs for anything that sleeps in the kernel — "entering the kernel
costs 30–70×" (<https://elinux.org/images/0/05/Microseconds_Matter_Gautham_Ponnu.pdf>). A 30× spread between
two paths that both mean "this event is due now" is a nondeterminism generator. Arrival order is a *hardware
performance artefact*; priority is a *model* fact. Only one of them can be part of the specification.

The practical consequence for this design: the tie-break key must be
`(machine_time, chip_priority, emitter_sequence_number)`, all three assigned by the model — priority from a
static table derived from the hardware (video fetch before Blitter before CPU write, &c., per §4.3 and §6.3),
sequence number from a monotonic counter incremented at *emit* time in the emitting chip's own deterministic
order. Never from a commit-queue position, a lock acquisition, or a hash iteration.

## 1.6 Deterministic multithreading: the techniques that actually work

The survey literature (<https://arxiv.org/abs/2210.15202>, *A Survey on Parallelism and Determinism*) separates
"languages/models that make races impossible" from "models that make races visible and reproducible". For a
simulator the useful distillation is three techniques, in decreasing order of practicality:

**1. Determinism by construction (fixed partition + ordered commit).** Split the work by a rule that does not
depend on timing; let threads compute in any order; apply results in a canonical order. This is the only
technique that scales without runtime cost, and it is what every deterministic simulator in Part 1.7 does.

**2. Deterministic scheduling (round-robin / lockstep).** One thread runs at a time, in a fixed order, handing
over at defined points. Perfectly deterministic, no data races by design, and *zero* parallel speedup — it is a
serialisation with extra steps. QEMU's original system-mode TCG is exactly this ("dealt with multiple CPUs with
simple round-robin scheduling") and it is still the fallback whenever the parallel mode's preconditions fail
(<https://qemu.readthedocs.io/en/master/devel/multi-thread-tcg.html>). Its virtue is that it is the reference
against which the parallel mode is validated.

**3. Record/replay as a determinism audit.** Do not try to make the parallel run deterministic; instead record
the nondeterministic inputs and replay. QEMU does this — and the telling detail is that it *requires icount*,
and icount is "incompatible with multi-threaded TCG"
(<https://qemu.readthedocs.io/en/v9.2.0/devel/tcg-icount.html>, <https://www.qemu.org/docs/master/system/replay.html>).
QEMU's deterministic mode and QEMU's parallel mode are mutually exclusive, by design, in the most widely
deployed emulator in existence. That is the strongest available signal about how hard it is to have both.

Worth naming what does **not** work as a determinism strategy: relying on the host memory model plus "it looks
atomic enough". QEMU's MTTCG document is essentially a long list of the places where that reasoning fails —
guest memory barriers, load/store-exclusive pairs, and the ABA problem on atomics
(<https://qemu.readthedocs.io/en/master/devel/multi-thread-tcg.html>). A simulator whose correctness depends on
host ordering has moved its specification into the CPU.

## 1.7 What ships, and what it cost them

| System | Parallelism model | Determinism | The lesson |
|---|---|---|---|
| **QEMU MTTCG** | One host thread per vCPU, round-robin fallback | Deterministic mode (icount) is *mutually exclusive* with MTTCG; replay needs icount | Parallel vCPUs and a deterministic virtual clock are different products. Pick per run mode. |
| **gem5** (design notes) | Per-thread event queues + barrier events + point-to-point slack events, later quantum/lookahead | "Deterministic by default" required at design time | Determinism is an architectural requirement, not a debug flag. Also: this was planned in 2008 and is still incomplete, which is itself data. |
| **parti-gem5** (research) | Quantum windows, thread-safe Ruby message passing | Trades accuracy for speed, explicitly | Up to 42.7× speedup, but "error in simulation statistics below 15% in most cases" — a *statistical* simulator's trade, not an emulator's. |
| **Verilator `--threads`** | Static schedule of macro-tasks, barrier per `eval()` | Deterministic (fixed partition) | 2.1× on 4 threads, 30% longest-thread efficiency. Barriers per clock are expensive; coarsening is mandatory. |
| **SystemC (parallel kernels)** | Research kernels, `SC_THREAD`→host thread | Causality/reproducibility listed as the top challenge | Accellera's own slide deck asks "Parallel SystemC Simulation — Does this make sense?" and lists the blockers: negligible compute per process, extremely high activation counts, tight coupling, central kernel. <https://workspace.accellera.org/document/dl/13185> |
| **higan/bsnes** | Cooperative coroutines, one OS thread | Trivially deterministic, fully serial | Chosen for *code clarity* and cycle accuracy, not speed. Fails on shared-address-space machines: "you'd always have to synchronize". |
| **yuzu (Prometheus)** | One host thread per guest core + timer + GPU | **Abandoned** cycle timers for host timing | "Cycle timers … are deterministic" but "it is very hard to keep all the cores advancing at the same pace … For all these reasons, we opted for host timing instead." <https://yuzu-emu.org/entry/yuzu-prometheus/> |

Two cross-cutting observations.

**Observation 1 — nobody ships deterministic *and* parallel *and* cycle-accurate, for a tightly coupled
machine.** Every system above sacrifices one. QEMU sacrifices parallelism in deterministic mode. yuzu sacrifices
determinism to get guest-core parallelism. parti-gem5 sacrifices accuracy. Verilator keeps all three but is
simulating hardware with an explicitly declared, statically analysable clock structure and no dynamic bus
arbitration model — and still only gets 2.1×.

**Observation 2 — the machines that parallelise well are the ones with disjoint state.** SystemC's list
("negligible computation load per process", "tight coupling of processes") and higan's exception ("two CPUs that
share all of the memory address space") describe the same failure mode. The STE is on the wrong side of both.

## 1.8 The cost floor, in numbers

From the synchronisation benchmark (<https://elinux.org/images/0/05/Microseconds_Matter_Gautham_Ponnu.pdf>),
converted into this machine's units:

| Primitive | Latency | In 32 MHz ticks | Share of one scanline (64 µs) |
|---|---|---|---|
| Spinning atomic handoff (x86-64) | 70–130 ns | 2.2–4.2 | 0.11–0.20% |
| Spinning atomic handoff (ARM64) | 170–190 ns | 5.4–6.1 | 0.27–0.30% |
| Kernel sleep (futex/condvar, x86-64) | 3.7–4.1 µs | ~120–130 | ~6% |
| Kernel sleep (futex/condvar, ARM64 non-RT) | 10–13 µs | ~320–420 | ~16–20% |

Read that table twice. A single futex round trip costs **more machine time than an entire 68000 instruction**
(a typical instruction is 4–20 cycles = 16–80 ticks), and in the ARM64 case it approaches a fifth of a
scanline. Any design that synchronises once per emulated *event* using sleeping primitives has already lost
before it computes anything.

---

# Part 2 — Recommendations for this design

## 2.1 The budget, computed

Frame budget: one PAL frame is 20 ms of machine time. To hold 50 fps the emulator must produce a frame in
≤ 20 ms of wall time, and on a 60 Hz display with vsync it has ~16.7 ms. Take a **sync budget of 10% of frame
time** (2 ms) as the design allowance — generous, and it keeps headroom for the host being a browser on a
laptop.

Solving for the maximum number of synchronisation points per frame:

| Barrier cost | Max barriers/frame at 10% | Per scanline | Per 8 MHz cycle |
|---|---|---|---|
| 100 ns (spin, x86-64) | 20 000 | 64 | 1 in 8 |
| 180 ns (spin, ARM64) | 11 100 | 36 | 1 in 14 |
| 4 µs (futex) | 500 | 1.6 | 1 in 320 |
| 13 µs (futex, ARM64) | 154 | 0.5 | 1 in 1000 |

And the two granularities the brief asks about, priced directly:

- **Barrier per 32 MHz tick**: 640 000 per frame. At 100 ns each that is 64 ms per 20 ms frame — **3.2×
  realtime just in barriers**, before any emulation. At futex cost it is 2.6 seconds per frame.
- **Barrier per 8 MHz CPU cycle**: 160 000 per frame. 16 ms at 100 ns — the entire frame budget, every frame,
  for barriers alone.
- **Barrier per 68000 instruction** (~20 000 instructions/frame in a busy frame): 2 ms at 100 ns — exactly the
  10% allowance, spent entirely on synchronisation. This is the *practical ceiling* for a spinning barrier.
- **Barrier per scanline**: 312.5 per frame. 31 µs at 100 ns (0.16%), 1.25 ms at futex cost (6%). Comfortable
  either way.
- **Barrier per 2 µs quantum (64 ticks)**: 10 000 per frame. 1 ms at 100 ns (5%). Affordable.

**Verdict on granularity: the smallest defensible barrier period is ~64 ticks (2 µs), i.e. ~32 per scanline;
the sensible default is a quantum of 128–512 ticks (4–16 µs, 16–4 per scanline) with forced synchronisation at
crossings. Per-tick and per-cycle barriers are arithmetically impossible; per-instruction barriers consume the
entire budget.** Nothing in the hardware requires better than this: the finest *observable* granularity in
§6.3–§6.6 is the video fetch slot and the mid-line register write, and both are much coarser than a tick.

## 2.2 Event-driven advance beats per-tick barriers — decisively

Beyond the arithmetic, three structural reasons:

1. **§10.3 rule 3 (lazy advance) is the same idea as lookahead**, and it is already the design's stated
   principle. A device that can compute its state at a later time without stepping intermediate clocks is
   precisely a device with large lookahead. Per-tick barriers would *destroy* that property by forcing every
   device to be evaluated at every tick.
2. **The event rate is what it is.** Per scanline, the hardware-realistic crossing count is on the order of a
   hundred: ~80 video word loads in low resolution (20 pixel groups × 4 planes) plus the CPU's ~512 bus slots,
   of which only the memory-mapped and contended ones are crossings. That is 3–4 orders of magnitude below the
   tick count. Stepping 2 048 times to handle ~100 events is 95% waste even before threading.
3. **Barrier-per-tick makes the tie-break problem worse, not better.** With one barrier per tick, everything is
   nominally "the same instant", so priority has to decide almost everything, and any residual
   arrival-order dependence becomes visible in almost every tick. Coarse quanta plus event-driven advance
   shrink the set of genuinely simultaneous events to the handful the hardware actually contends over.

So: **advance to the next scheduled instant, not to the next tick.** The scheduler's main loop is
`t = min(next_instant over chips)`, jump there, fire the crossings at `t` in priority order, repeat. Ticks are
the *coordinate system*, not the step size.

## 2.3 How many synchronisation points a PAL line can afford

Direct answer: **1–4 per scanline for anything that involves parking a thread; up to ~30–60 if every handoff is
a spinning atomic. Design for ≤ 4.**

Where they should go, in priority order:

| Instant | Why it is a natural sync point | Cost |
|---|---|---|
| Line boundary (`h = 0` / `h = LINE`) | Hardware event; resets counters, advances the vertical machine, updates `LINEWID` and the video counter | 312.5/frame — trivially affordable |
| `DE` rise/fall, `BLANK`, `HSYNC` | §6.3 gating machine; drives MFP Timer B | ~4/line, all derivable in advance |
| Video fetch word deadlines | Memory-service slots; the Shifter's `LOAD` | 20/line (low res) — the busiest legitimate stream |
| Sound DMA sample deadline | 20–160 µs apart depending on rate | ≤ 1/line at 50 kHz; nothing at 6 kHz |
| Bus-ownership handover (`BR`/`BG`/`BGACK`) | Genuinely serialising, and rare | a few per frame |

The video fetch stream is the only one that approaches the barrier budget, and it does so at 20 events/line.
That is the number to design around: **the busiest real crossing stream in the machine is ~20 per line, which
is 40% of the spinning-barrier allowance and 12× the sleeping-barrier allowance.** Hence the design rule below.

## 2.4 Determinism without serialising everything: compute in parallel, commit in order

This is the core recommendation, and it is the only pattern in Part 1 that survives contact with all four
constraints (tight coupling, tight budget, browser threading, must be debuggable).

**The invariant.** The machine's state is a pure function of `(initial state, ordered sequence of crossings)`.
Threads may compute in any order and at any speed; **the application of effects to shared state happens in one
place, in canonical order.**

Three layers:

**Layer 1 — the sequencer (one thread, always).** Owns machine time, the priority table, and the commit order.
It never emulates a chip. It is small enough to be obviously correct, and it is the thing you unit-test for
determinism.

**Layer 2 — chip workers (N threads, N ≥ 1, same code path).** Each worker executes a quantum for the chips
assigned to it: advance them to their next instant, run their logic, and **append** effects (crossings and
memory-service requests) to a per-worker output buffer. They must not mutate shared chip state or shared RAM
during this phase.

**Layer 3 — the commit.** The sequencer drains all buffers, sorts by `(time, priority, seq)`, and applies. This
is the only code that writes shared state. It is serial, and that is fine: it touches a few hundred words per
line, not a screen.

```
             ┌──────────── sequencer (thread 0) ────────────┐
             │  t = min(next_instant)                        │
   chips ──► │  drain effect buffers → sort → apply          │ ──► memory service grant
             │  publish committed_time (release store)       │      (GSTMCU arbiter)
             └───────────────────────────────────────────────┘
                    ▲ effects (append-only)      │ gate: "you may run to t"
        ┌───────────┴──────────┐        ┌────────┴─────────┐
        │ worker 1: GSTMCU,    │        │ worker 2: Shifter,│
        │  MFP, DMA, YM2149    │        │  Blitter, IKBD    │
        └──────────────────────┘        └───────────────────┘
```

Why this is deterministic without serialising the *work*:

- Ordering comes from the sort key, not from who ran first.
- The partition (chip → worker) is static, so the schedule is a compile-time constant, exactly as Verilator
  found necessary ("Every-cycle dynamic runtime assignment was tried but slow!").
- The only shared mutable state is written by one thread.
- A worker that reads shared state reads the *committed* value it is entitled to see (a version stamped by the
  sequencer), never a value another worker happens to have written.

Trade-off, stated honestly: within a quantum, a worker cannot observe another worker's effects. That is a
*correctness* constraint, not just a performance one, and it is exactly the lookahead problem. It is
tolerable here because the lookahead is not a guess — the video fetch schedule, sample deadlines and line
timing are **known in advance from the hardware's own periodic structure**. Only CPU execution and the Blitter
are dynamically scheduled, and those are the two chips whose crossings are the least frequent
(CPU: memory-mapped accesses only; Blitter: bus requests, not per word).

**Where it breaks and what to do:** a crossing whose deadline falls inside the quantum cannot be delivered in
time. Three remedies, in order of preference: (1) shrink the quantum for that edge — per-edge quantum, not a
global one; (2) force a sync (the sequencer closes the quantum early on request, which is exactly a null
message with a real timestamp); (3) if an edge is genuinely per-word (Blitter ↔ memory), *put both ends on the
same worker* (see §2.5).

## 2.5 Thread topology: a worker pool, not one thread per chip

**Recommendation: a fixed pool of 2–4 workers with chips multiplexed by a static partition. Default the
shipped build to 1 worker (T=1) and enable T>1 behind a flag until the determinism tests are green.**

Reasons, in order of weight:

1. **The chip count exceeds the sensible thread count.** The Virtual chip decomposition yields a dozen or more
   units (GSTMCU, Shifter, 68000, Blitter, DMA, WD1772, MFP, YM2149, two ACIAs, IKBD, LMC1992, ROM/RAM). Nothing
   in this machine has a per-chip parallelism that justifies 12+ OS threads, and the browser imposes a real cap:
   Emscripten documents a Firefox limit of "by default 20" workers per domain
   (<https://emscripten.org/docs/porting/pthreads.html>).
2. **Worker startup is 40 ms, not microseconds.** Mozilla's measurement of worker instantiation is "about
   **40 ms**" with "variations of only a few milliseconds" (<https://hacks.mozilla.org/2015/07/how-fast-are-web-workers/>).
   Forty milliseconds is two whole frames. Workers must be created once, at load, in a pool — which is exactly
   what `PTHREAD_POOL_SIZE` is for. It is also a hard argument against any design that spawns a thread per
   chip when a chip is "activated".
3. **Synchronisation cost scales with the square of the participant count**, not linearly: a global barrier is
   `O(N)` notifications and worse in cache-line traffic (all waiters bounce on one line). Four participants is
   the sweet spot; twelve is a different regime.
4. **Chips are wildly unbalanced.** The 68000 does the most work; the WD1772 does almost nothing for seconds at
   a time. One-thread-per-chip means one thread is a bottleneck and eight are idle, and it makes the buffer
   drain order unpredictable in size. Multiplexing onto a pool lets a static partition balance by *measured*
   load.
5. **The precedent is against it.** No shipped emulator in Part 1.7 uses one thread per chip. higan
   considered exactly this and rejected preemptive threading explicitly; yuzu landed on one thread per *guest
   CPU core* — the one place where there is genuine independent work — and kept everything else on shared
   threads.

The one case where a dedicated thread earns its keep is a chip whose work is *bulk and pure*: the video output
stage (bitplane decode → palette → RGB), the audio mixer, and the audio resampler. Those are functions of
committed state with no back-pressure into the machine, and they can run on their own thread with no
synchronisation beyond "here is the committed frame/block". This is the same split every 3D emulator uses for
its render thread, and it is where the real wall-clock win is at Level 1.

## 2.6 A concrete chip-thread pattern

Three primitives, all needed, all cheap.

**(a) Park until my next instant.** The worker sleeps on a futex until the sequencer's committed time reaches
its next event. Note the ordering: read the time, then compare, then sleep on the *observed* value — the
classic lost-wakeup-safe form.

```c
// shared, in linear memory
_Atomic uint64_t committed_time;     // ticks; release store by sequencer
_Atomic uint32_t time_epoch;         // incremented on every advance (futex word)

static inline void wait_until(uint64_t t) {
    for (;;) {
        uint64_t now  = atomic_load_explicit(&committed_time, memory_order_acquire);
        if (now >= t) return;
        uint32_t e = atomic_load_explicit(&time_epoch, memory_order_acquire);
        // re-check after sampling the epoch: no window between check and wait
        if (atomic_load_explicit(&committed_time, memory_order_acquire) >= t) return;
        futex_wait(&time_epoch, e, /*timeout*/ NEVER);   // Atomics.wait / emscripten_futex_wait
    }
}
```

Two details that are easy to get wrong and fatal if you do: the epoch must be re-read *before* the final
re-check (otherwise a wakeup between the check and the wait is lost), and `committed_time` must be one aligned
64-bit word — a torn read of a 64-bit value across two 32-bit halves would be a nondeterminism source that only
shows up under load.

**(b) Run to the instant, produce effects, never write shared state.**

```c
void worker_step(ChipSet *cs, uint64_t until) {
    for (;;) {
        Chip *c = earliest_chip(cs);              // static order within the worker
        if (!c || c->next_instant > until) break;
        c->now = c->next_instant;
        c->run_to(c->now);                        // pure: only c->state mutated
        emit_effects(c);                          // append crossings + memory requests
    }
}
```

`emit_effects` appends to the worker's buffer with the key already attached:
`{time, priority, seq, target, payload}`. `seq` is a per-emitter counter, so the ordering inside one chip's
output is its own emission order — which is what the hardware does — and never the order the sequencer happened
to drain at.

**(c) Wait for commit of *my* ticket, not for *all* tickets.** A barrier that waits for every worker wastes the
fast workers' time. A ticket-based wait lets a worker proceed as soon as its effects are committed:

```c
uint32_t my_ticket = atomic_fetch_add(&commit_ring.head, 1);
... append effects ...
while (atomic_load_acquire(&commit_ring.committed) < my_ticket)
    futex_wait(&commit_ring.committed, /*observed*/, NEVER);
```

The ring is the communication channel; `committed` is a single monotonic counter, so the wait is on one line
and there is no lock. This is the pattern to prefer over `pthread_barrier_*`, which is all-to-all and pays
`O(N)` notifications when one word would do.

**Anti-patterns to forbid in review:**
- A chip thread calling anything that can take a lock held by another chip thread.
- Any `malloc`/`free` on the emulation hot path (Emscripten's default `dlmalloc` "has a single global lock"
  — see Part 3).
- Reading a shared register without going through the committed-state accessor.
- Sorting by time alone anywhere in the codebase. One sort key, defined once.
- Using `performance.now()` or a frame counter to decide anything inside machine time (§10.3 rule 7).

## 2.7 Testing determinism (this is where the design is won or lost)

Determinism that is not tested rots on the first refactor. Four cheap mechanisms, ordered by payoff:

1. **State hash per crossing-batch.** After each committed batch, hash `(machine_time, Σ chip state hashes)` and
   append to a trace. Run once at T=1, once at T=4; the traces must be byte-identical. This catches every
   ordering bug that matters within minutes of it being introduced, and it is the single highest-value test in
   the project.
2. **Randomised delay injection.** In a debug build, insert a random `spin(n)` before each effect append. If
   the trace changes, the ordering depends on timing. This is the cheapest nondeterminism detector there is,
   and it needs no special tooling.
3. **T=N with N ∈ {1,2,3,4} as a matrix in CI**, not just the shipping configuration. A design that is
   deterministic only at N=1 has not been tested.
4. **Golden-frame comparison against the oracle** (Hatari) on the demo/raster corpus — the existing plan's
   validation ladder, but with the *order* of writes also compared, not just the pixels. Pixels can match while
   the ordering is wrong; that bug shows up three months later as a border effect.

## 2.8 Staging: Level 1 first means threads are not the first problem

Worth stating plainly, because it affects sequencing. At **Level 1**, most virtual chips are lazily evaluable
(§10.3 rule 3) and the machine has very few genuine contention points — a functional emulator can be
single-threaded and still be "correct" at its own fidelity level. The parallel architecture only starts paying
when Level 2 work arrives: per-line video fetch, Blitter/CPU races, mid-line register writes.

So the build order should be: sequencer + ordered commit + T=1 → determinism harness → Level 2 devices →
*then* raise N. Doing it in that order means the T=1 path is a genuine reference implementation, which is what
makes the T=N comparison meaningful. Doing it in the other order means debugging two unknowns at once.

---

# Part 3 — Browser and WebAssembly realities

These are not incidental. Three of the Part 2 recommendations are decided here.

## 3.1 There is no separate "Wasm Workers API"

WebAssembly does not define threads. The threads proposal adds **shared linear memory** and **atomic
instructions**, and says explicitly: "The responsibility of creating and joining threads is deferred to the
embedder." In the browser the embedder is Web Workers + `SharedArrayBuffer`
(<https://github.com/WebAssembly/threads/blob/main/proposals/threads/Overview.md>). Emscripten is what turns
`pthread_create` into "spawn or reuse a Worker, instantiate the module there, call the entry point". So the
mental model is: **each Emscripten pthread is a whole extra WebAssembly instance in its own Worker**, sharing
one linear memory. That has consequences below (per-thread stacks, per-thread TLS, duplicated module
instantiation cost).

Also note the memory ordering available: the proposal states "Currently all atomic memory access instructions
are sequentially consistent. Instructions with other memory orderings may be provided in the future." In
practice this means every atomic operation is a full fence — there is no cheap relaxed load in the MVP
instruction set. **Unconfirmed:** whether the shipped browsers/toolchains now emit acquire/release variants
from the memory-control proposal; if they do, the cost model improves, but the design should not depend on it.

## 3.2 Emscripten pthreads: the five facts that shape the design

From <https://emscripten.org/docs/porting/pthreads.html>:

1. **`-pthread` at compile *and* link.** Plus `-sPTHREAD_POOL_SIZE=<expr>` to pre-create the pool "at page
   `preRun` time before application `main()` is called". This exists because of point 2.
2. **`pthread_create` cannot be followed by synchronous code that expects the thread to be running.** "That
   requires returning the main event loop… it will only run after you return to the event loop. This is a
   violation of POSIX behavior and will break common code which creates a thread and immediately joins it."
   With a pre-sized pool the workers already exist, so this mostly disappears — another reason the pool (not
   spawn-per-chip) is the only viable topology.
3. **The main browser thread cannot block.** "The Web API for atomics does not allow blocking on the main
   thread (specifically, `Atomics.wait` doesn't work there)… we use a busy-wait on the main browser thread,
   which can make the browser tab unresponsive." Therefore: **`-sPROXY_TO_PTHREAD`**, which moves `main()` to a
   worker so that the real main thread only handles proxied events. For this emulator that is not optional —
   the sequencer wants to `futex_wait`, and it must not do so on the UI thread.
4. **No single binary with a threaded fast path and a threaded-less fallback.** "It is not possible to build one
   binary that would be able to leverage multithreading when available and fall back to single threaded when
   not. The best you can do is two separate builds." So the T=1 configuration must be a *runtime* mode of the
   same scheduler (N=1 worker, same code path), not a separate compilation. This is another argument for the
   pool design: the pool of size 1 is the fallback.
5. **No POSIX signals, no `fork`.** Irrelevant to the scheduler, relevant to anything that might want to
   preempt a worker — there is no mechanism to interrupt a running Worker. A worker that spins forever is a
   hung tab. Every wait must have a liveness plan (a watchdog that can abandon a frame).

## 3.3 SharedArrayBuffer, COOP/COEP, and memory growth

**Cross-origin isolation is a deployment requirement, not a code one.** `SharedArrayBuffer` — and therefore
Wasm threads — requires the document to be cross-origin isolated, via
`Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Embedder-Policy: require-corp`, verified with
`self.crossOriginIsolated`. All subresources must carry CORP/CORS headers; workers must be same-origin
(<https://web.dev/articles/coop-coep>). Practical consequences: the hosting configuration is part of the
deliverable; a CDN asset without `Cross-Origin-Resource-Policy` silently breaks threading; and any embedding of
the app in someone else's iframe requires `allow="cross-origin-isolated"` on the iframe.

**Memory growth is the one that bites.** The threads proposal requires a shared memory to declare a maximum
("If linear memory is marked as shared, the maximum memory size must be specified"), and `grow` is
sequentially consistent. MDN documents the JS-facing hazard precisely: for a shared `Memory`, growing does
*not* detach the old buffer — "its length will not be updated… Accesses to the `buffer` property after growing
will yield a larger `SharedArrayBuffer`", while any cached view still covers only the old span
(<https://developer.mozilla.org/docs/Web/JavaScript/Reference/Global_Objects/WebAssembly/Memory/grow>).
Emscripten adds that "Pthreads + memory growth (`ALLOW_MEMORY_GROWTH`) is especially tricky" and links
WebAssembly design issue #1271, and warns that JS-side `HEAP*` views may become stale
(<https://emscripten.org/docs/porting/pthreads.html>,
<https://github.com/WebAssembly/design/issues/1271>).

**Recommendation: never grow.** Allocate the maximum up front — STE RAM (4 MB max, 1 MB for a 1040STE) + TOS
ROM + frame buffers + audio ring + per-worker stacks — set `INITIAL_MEMORY == MAXIMUM_MEMORY`, and disable
`ALLOW_MEMORY_GROWTH`. A fixed 64–128 MB reservation costs nothing on a modern browser and removes an entire
class of bugs: stale views, a `grow` happening while another worker holds a pointer, and the documented JS
slowdown that growth causes. If growth is ever needed, it must be a sequenced operation (all workers parked),
never a lazy one.

## 3.4 Atomics and futex cost inside wasm

`memory.atomic.wait32/64` and `memory.atomic.notify` map onto the browser's own wait/notify machinery; there is
no published per-operation latency figure for any engine that I could find, so:

- **Unconfirmed:** the absolute cost of `Atomics.wait`/`notify` in a Worker inside a browser engine.
- **Confirmed and usable as a bound:** the mechanism is the "sleeping" tier, not the "spinning" tier. The
  Linux futex measurements (3.7–13 µs) are the right order of magnitude for a kernel-assisted wakeup, and the
  browser adds a JS/Worker boundary and its own scheduler on top. **Plan for the sleeping tier: ~5–20 µs per
  park/wake round trip.** Every conclusion in §2.1 that assumed "futex is expensive" therefore holds with
  margin, and any design that assumed "it'll be fast in wasm" is wrong by an order of magnitude.
- The practical corollary: **spin briefly before parking.** A bounded spin (a few thousand `pause`-equivalent
  iterations) followed by `futex_wait` captures the 100 ns case when the sequencer is about to commit anyway,
  and still yields the core when it is not. This is the standard adaptive-mutex shape, and with a fixed
  bounded spin count it stays deterministic (the spin only affects *when* a thread observes a committed value,
  not *what* it observes — provided, as in §2.4, that observation is version-gated).

## 3.5 Worker startup latency, and why it drives the pool decision

40 ms per Worker on 2015 mobile hardware (Mozilla, above). Desktop is certainly faster and I could not find a
current figure — **unconfirmed** — but the order of magnitude is enough to settle the design: workers are a
startup cost amortised over a session, never a per-task cost. Consequences:

- Create the pool before the machine boots (Emscripten `PTHREAD_POOL_SIZE`, or explicit Workers).
- Never spawn a worker to handle a chip becoming active, a frame, or a save-state load.
- Budget the load screen for it: N workers × 40 ms plus module instantiation per worker is a visible stall.
- `postMessage` is also not free (0.5 ms round trip, same source) — another reason all hot-path communication
  goes through shared memory + atomics, and `postMessage` is reserved for control-plane events (frame ready,
  audio buffer, save state).

---

# Appendix A — Sources

Primary, read for this note:

1. Fujimoto, *Time Warp: Local Control* (slides) — <https://sigsim.acm.org/mskr/Courseware/Fujimoto/Slides/FujimotoSlides-11-TimeWarpLocalControl.pdf>
2. Jefferson, *Virtual Time* — <http://cs.uiowa.edu/~ghosh/VirtualTime.pdf>
3. Conservative DEVS / CMB lineage — <https://dl.acm.org/doi/10.1145/1878537.1878683>
4. Lookahead in conservative simulation — <https://scs-europe.net/services/esm2004/pdf/hpc-08.pdf>
5. *parti-gem5: gem5's Timing Mode Parallelised* — <https://arxiv.org/pdf/2308.09445>
6. gem5, *Parallel M5* design notes — <http://gem5.org/Parallel_M5>
7. QEMU, *Multi-threaded TCG* — <https://qemu.readthedocs.io/en/master/devel/multi-thread-tcg.html>
8. QEMU, *TCG Instruction Counting* — <https://qemu.readthedocs.io/en/v9.2.0/devel/tcg-icount.html>
9. QEMU, *Record/replay* — <https://www.qemu.org/docs/master/system/replay.html>
10. Snyder, *Verilator 4.0: Open Simulation Goes Multithreaded* (ORConf 2018) — <https://veripool.org/papers/Verilator_v4_Multithreaded_OrConf2018.pdf>
11. Verilator manual, `--threads` — <https://veripool.org/guide/latest/exe_verilator.html>
12. Accellera, *SystemC Multi-kernel support and thread safety* — <https://workspace.accellera.org/document/dl/13185>
13. byuu, *Cooperative Threading* (higan/bsnes) — <https://github.com/higan-emu/emulation-articles/blob/master/design/cooperative-threading/README.md>
14. yuzu, *Prometheus* — <https://yuzu-emu.org/entry/yuzu-prometheus/>
15. Ponnu, *Microseconds Matter: Benchmarking Thread Synchronization on Linux* (ELC) — <https://elinux.org/images/0/05/Microseconds_Matter_Gautham_Ponnu.pdf>
16. *A Survey on Parallelism and Determinism* — <https://arxiv.org/abs/2210.15202>
17. Emscripten, *Pthreads support* — <https://emscripten.org/docs/porting/pthreads.html>
18. WebAssembly threads proposal — <https://github.com/WebAssembly/threads/blob/main/proposals/threads/Overview.md>
19. web.dev, *COOP and COEP* — <https://web.dev/articles/coop-coep>
20. MDN, `WebAssembly.Memory.prototype.grow()` — <https://developer.mozilla.org/docs/Web/JavaScript/Reference/Global_Objects/WebAssembly/Memory/grow>
21. WebAssembly design issue #1271 (threads + memory growth) — <https://github.com/WebAssembly/design/issues/1271>
22. Mozilla Hacks, *How fast are web workers?* — <https://hacks.mozilla.org/2015/07/how-fast-are-web-workers/>

Further reading, not relied on for any claim above: Fujimoto, "Parallel discrete event simulation", *CACM* 33(10), 1990 — the canonical survey; Nicol & Fujimoto on conservative/optimistic trade-offs; the `par-gem5` and `pdist-gem5` papers.

# Appendix B — Unconfirmed, and where the numbers could be wrong

Marked honestly, in order of how much they could change the recommendations:

- **Browser `Atomics.wait`/`notify` latency in a Worker.** No published per-operation figure found. The
  recommendation assumes the "sleeping tier" (5–20 µs). If it were 500 ns, §2.3's per-scanline allowance would
  rise ~10×, but nothing else in Part 2 changes — the design already avoids hot-path parking.
- **Current desktop Worker startup latency.** The 40 ms figure is 2015 mobile hardware. The *decision* it
  supports (pool, never spawn) is robust to a 10× improvement; the *load-screen budget* is not.
- **Whether shipped engines now emit acquire/release atomics** rather than sequential-consistency-only, per the
  later memory-model proposals. Affects micro-cost, not structure.
- **`SharedArrayBuffer` availability on current Safari/iOS.** Not verified for this note; the project should
  assume COOP/COEP + a feature check, and treat "no SAB" as "T=1 mode", which the design already supports.
- **Synchronisation-cost numbers are Linux/C, not wasm.** The benchmark is a Linux kernel on x86-64 and ARM64;
  a browser's thread is a real OS thread but the wakeup path includes the engine's own scheduling. Directionally
  safe, numerically approximate.
- **Two figures in this note are my own arithmetic**, not quoted: the barrier-budget table in §2.1 and the
  per-instruction/per-line work estimates. The arithmetic is shown so it can be re-derived; the inputs
  (line = 64 µs, tick = 31.25 ns, 100 ns spin, 4 µs futex) are all sourced above.
- **Not investigated, and possibly relevant:** whether any browser exposes a way to pin a Worker to a core
  (it does not, so the 30× spin/sleep spread across CCX/NUMA boundaries in the benchmark is not controllable
  from wasm — this makes the *tail* of the latency distribution worse than the median, which argues for even
  fewer sync points than §2.3 allows).
