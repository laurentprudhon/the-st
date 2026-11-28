---
name: the-st-perf-measurement
description: "Use when measuring frame time in the-st (G8)."
version: 1.0.0
author: Hermes Agent
license: MIT
metadata:
  hermes:
    tags: [performance, dotnet, measurement, atari-st]
    related_skills: [implement, systematic-debugging, handoff]
---

Measure and land a performance change in `the-st` (Atari STE emulator, `/opt/data/the-st`, C#/.NET 11,
G8 row of `design/validation.md`). The digest is the truth; the frame mean is an estimate that a hostile
host can fake.

## When to Use

- Taking or re-taking the G8 frame measurement, or comparing two engine variants.
- Landing any perf change in `the-st` (retention, `ValueLog`, channel tables, queue contents).
- Resuming a `the-st` handoff whose step list includes "measure", "adjacent pair", "digest pair" or
  `validate.sh`.
- Not for chip/CPU behaviour work: nothing here is about whether the machine is right, only whether it is
  fast, and the digest check exists to prove the perf change did not change the answer. The core's own fidelity —
  decode, effective addresses, the 680x0 corpus — is `the-st-68000-core`.

## Environment (both facts will silently break a run)

```bash
export DOTNET_ROOT=/opt/data/.dotnet PATH=/opt/data/.dotnet:$PATH \
       DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
```

- `dotnet` lives at **/opt/data/.dotnet**, not on `PATH`, and `scripts/validate.sh` only searches
  `/opt/data/dotnet`, `$HOME/dotnet`, `/usr/share/dotnet` — none of which exist. Export `PATH` first or the
  gate greps an empty `runs/<stamp>/g8/`.
- The projects target **net11.0**. If `dotnet --list-sdks` stops at 10, install with
  `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 11.0 --install-dir /opt/data/.dotnet --no-path`.
- **The harness resolves `roms/` against the working directory.** Always `cd /opt/data/the-st` before
  invoking `validation-harness`; from anywhere else every run dies with
  `DirectoryNotFoundException: .../roms/emutos-1.4/etos256uk.img` and `run.state` is simply absent.

## Comparing two variants without rebuilding between runs

Build each variant once and copy the whole output tree aside, then alternate the two binaries. Rebuilding
mid-comparison puts a JIT-cold run next to a warm one.

```bash
cp -r tools/validation-harness/bin/Release/net11.0 /opt/data/cache/scratch/bins/vA   # variant A
cp -r tools/validation-harness/bin/Release/net11.0 /opt/data/cache/scratch/bins/vB   # variant B
# the harness below is this skill's own scripts/ab2.sh — read it with
# skill_view('the-st-perf-measurement', 'scripts/ab2.sh') and write it to /opt/data/cache/scratch/ab2.sh
bash /opt/data/cache/scratch/ab2.sh /opt/data/cache/scratch/bins/vA tagA \
     /opt/data/cache/scratch/bins/vB tagB 6
```

`ab2.sh` runs adjacent pairs in **alternating order** — AB, BA, AB, BA — and prints the signed per-pair
delta plus variant B's allocation. Keep it outside the repo: it is not part of the machine.

## A compilation change is a variant too (runtime async, pooled boxes)

Some of the largest levers are not edits to the machine — they change how the compiler lowers `async`. Price
them exactly like a code variant, with five extra traps:

- **The switch is a Roslyn feature flag, not an SDK property**: `dotnet build -c Release
  -p:Features=runtime-async=on`. **A clean *output* directory is not enough** — the flag is not in
  `CoreCompile`'s up-to-date inputs, so an incremental build reuses the previous IL, reports success in
  0.3 s and hands you a "runtime-async" binary that is the compiler-state-machine one. `rm -rf obj bin` on
  the projects (not just the `-o` dir), or better: put `<Features>runtime-async=on</Features>` in the
  project file, where it is part of every build's inputs — that is what #65 L1 landed, and it makes the
  gate's own `dotnet build TheST.sln` describe the landed tree.
- **Verify the build took from the artifact, not the exit code, and read it on the assembly that holds the
  `async` code**: `strings src/TheST.Machine/bin/.../TheST.Machine.dll | grep AsyncHelpers`. The harness dll
  can carry the flag while the machine did not recompile (the two projects' up-to-date checks are separate),
  so a harness-only check reports a zero that is not one. The state-machine count is the second witness:
  `<Name>d__N` goes 45 → 1, and the survivor is an iterator (`<SubscriberSets>d__87`), not a frame.
- **Every project that compiles the sources of record needs the same flag.** A bench that links `src/` files
  (`<Compile Include="../../src/...">`) builds its own copy of them, so without the flag it prices a
  different compilation than the machine ships; a `ProjectReference` inherits the referenced project's flags
  and needs nothing.
- **A row taken under a different lowering is a NEW row, never an edit of the old one.** Quote the old rows as
  the baseline, label the new numbers with the lowering they were taken under, and never subtract across the
  boundary — the bench's per-cycle figures mean something else afterwards.
- **If the compiler rejects a shape it names the method** (`ERR_UnsupportedFeatureInRuntimeAsync`): opt that
  method out (`[RuntimeAsyncMethodGeneration(false)]`), record which, and do not restructure a chip to please
  the compiler.

`PoolingAsyncValueTaskMethodBuilder` is the shallower version of the same idea: it recycles the state-machine
boxes but keeps every frame and every dispatch, so its witness is `alloc_mb_per_frame`, and the pricing rule
below says what to expect of an allocation-only prize. And before ordering either lever, grep `docs/adr/` for
the bet the project already made — verifying an ADR's own premise on the machine is the cheapest work
available, and it reframes the lever from "one option among many" to "the return on a decision already taken".

**Runtime async is landed (#65 L1), and it is the biggest lever the project has pulled: 3.72 ms of a
16.01 ms frame, 6/6 pairs, all counters and both folds identical, 10.42 → 7.93 MB allocated.** Two rules
follow from it and both are easy to get wrong afterwards:

- **It is not a win on every chip.** Re-measured per chip (FINDINGS §20): the CPU went 616.1 → 337.1 ns a
  wake (−279.0) and the three device chips went **up** — SCU +8.0, Video +30.0, Timers +20.0 ns, unanimous
  3/3 in two bracket modes, +0.55 ms between them, all of it inside the continuation (the machinery column
  is flat). Never quote the folder-wide average as if each chip gained; a chip whose wake completes its
  await inline paid a few ns for a lowering that saved the CPU 279.

- **Every per-cycle price taken before it is a different number, not a stale one.** The CPU's own bench row
  went 400.2 → 169.9 ns a bus cycle and `A − C` (all of the coroutine) 360.8 → 128.9. Any projection in an
  older ticket — #42's 6.55 ms, L3's 375 ns a cycle — is arithmetically dead or needs re-taking before it is
  believed; a lever whose projection exceeds the whole remaining cost cannot be worth what it says.
- **A lever that removes compiler state-machine boxes has nothing left to pool.** `PoolingAsyncValueTaskMethodBuilder`
  is subsumed, not defeated: the boxes do not exist. Read the IL witness before proposing it again.

## Re-running a census across a lowering change, and attributing the allocation line (#65's study, FINDINGS §20)

Re-taking #48's per-chip census after a lowering landed needs three things this project learned the hard way:

- **Build the counterfactual lowering too, and interleave run by run.** A single-column re-take cannot
distinguish a real 8 ns move from this host's thermal episodes — the same six runs read 13.2 ms and 18.4 ms
a frame for identical code. Copy the census tree once, delete `<Features>runtime-async=on</Features>` from the
copy's csproj (this copy keeps the `Origin` tag, so it *is* the counterfactual, not §18's binary), verify with
`AsyncHelpers` 0 on `TheST.Machine.dll` and `d__N` names present, then alternate the two binaries per run.
- **The old column is the instrument's proof.** If the compiler-state-machine column reproduces the previous
  session's per-chip ns within a few percent (616.1/93.1/134.1/91.1 against §18's 620.3/92.3/133.3/89.3), the
  new column is quotable beside it; if it does not, the instrument drifted and nothing in either column travels.
- **Attribute allocation in-process, not by subtraction of guesses.** `alloc_mb_per_frame` is process-wide:
bracket `GC.GetAllocatedBytesForCurrentThread()` around `Engine.Run` per frame (it gives the harness's own
share — **0.000 MB**, measured, so the line is the machine's), around each `continuation()` (per-chip direct
allocation), and price queue entries by a calibration loop in the same binary (200 000 of each, divide). The
check is that the parts close on the gate's own figure: 4.45 MB (CPU continuations, 324.2 B a resume) +
3.47 MB (39 147.6 `Resume` × 48 B + 24 445.0 `ApplyWrite` × 72 B) = 7.92 against the gate's 7.93.
- **An instrument that adds a field changes the object's size** — the census's `Origin` tag makes `Resume`
56 B, not 48, i.e. +0.30 MB a frame at 39 147 entries. Bill it or the closure fails by exactly that much.
- **`suspensions_per_frame` is not the CPU's suspension count.** It counts dated reads *and* subscriptions
across every chip (44 417.5), so 2.49 MB ÷ that gave a false "56 B a suspension"; the CPU's own suspensions
are its 14 408.8 resumes and the object removed was **181 B** of box + continuation. Divide by the counter
that belongs to the wake you are pricing.
- **Reconcile the counts before quoting a ratio**: per-chip resumes must sum to `pops_per_frame` − `ApplyWrite`
+ held-date wakes (14 408.8 + 38 001.7 + 3 994.5 + 6 418.6 = 62 823.6), and the "found no work" bucket must
reproduce the previous census's share (10.3 %).
- **A mode switch driven by an environment variable must echo, and the runner must assert it.** A
`ST_CENSUS_TIMING=outer|body|alloc` switch whose default arm is 0 turns a typo into a full set of successful-
looking runs in the wrong mode: passing `1`/`2` where the switch expects `outer`/`body` produced nine mode-0
runs and a frame mean that looked plausible. Print the resolved mode into `run.state` next to the counts and
have the run script fail when it does not match what it asked for.

**Per-chip figures (runtime async, 800 frames / 724 run, pair subtracted, taken before #63's Timer D cut, so
quote them as the per-chip *split* and not as today's total):** CPU **337.1 ns**, 4.86 ms (39.5 %); SCU 101.1,
3.84 (31.3 %); Video 164.1, 0.66 (5.3 %); Timers 111.1, 0.71 (5.8 % — that row is gone now: #63 removed 6 008.7 of
its 6 418.6 wakes a frame). **Today's totals are §22's**: the continuations are 8.55 ms of a 9.73 ms frame
(39 147.6 resumes at 164.5 ns, 17 671.4 dated wakes at 119.2 ns), the engine's own dispatch 0.79, the rest 0.40 —
so the CPU is ~50 % and the three device chips ~47 %, still of the same order (#49's premise). The frame's allocation is **44 % queue, 56 % the CPU's own continuation frames**,
with `gc_ms_per_frame` 0.12 — no lever there, and #46 already priced the queue half.

**An ADR decision the code does not implement is a perf ticket waiting to happen.** ADR 0013 §4 had said since
it was written that the chip's `nextDeadline` is the minimum over A, B and C — Timer D off the wake list —
while `RestartCounters` armed it anyway: 6 008.7 expiries a frame, 4.35 M anchors per 724 frames into a log no
one reads. Landing the ADR's own sentence measured **1.27 ms (12.29 → 11.02, 6/6 pairs)** against a 0.67 ms
projection, because the pair also collects what the wake bracket cannot see: a 22nd log in every instant's trim
(`drain_ms` −2.9 ms a Debug frame) and the ring traffic of 4.35 M anchors. When a ticket's prize seems small,
read the ADRs for a declared decision the code contradicts — the accounting there is already done.

**Read the trim counters, not only the wake, when a cut stops appending to a log.** A log that loses its
records also leaves every instant's trim and the retained-ring total, so the Debug boot should be read for
`drain_ms`, `records_overwritten` and the `window:` log count beside the frame mean; those are where a
sub-millisecond-per-wake projection turns into a doubled measured win.

**When the owner is the decision, present it as numbered shapes with the number attached.** Two or three
options, the cost of each in ms, and what each does to the folds — `bless it (worth N ms, moves both folds)`,
`refuse it (landed as a page)`, `defer it (the gate stays M ms short)` — never an open question, never the
recommendation buried in prose. When the answer is **restate** rather than **strike**, keep the clause's
purpose and delete only the falsified condition (a "a .NET 11 miss must not reopen the language" guard became
"a runtime regression is met by pinning the last working 11.x"), record who decided and when in the ADR's own
revision section, and then write the new wording and the new fold pair into every place that quotes them:
FINDINGS, the ADR, the ticket, this skill, the handoff.

**The instrument that decides a readability question is the diff, so build the minimal one before you argue.** A
scratch tree accretes counters and earlier prototypes, and diffing *it* reports hundreds of lines that would never
land. Copy the clean tree aside (`tar`, excluding `runs/`, `*/bin`, `*/obj`, `.git`), apply **only** the change,
build it, and diff against the shipped source; then report the count split into code and comment (**124 code of 209
changed lines** here, in five files) and the invariants it moves, not an adjective. "Still readable" is
unfalsifiable; a 124-line diff with the reference path left verbatim beside the new branch is a fact the owner can
rule on — and the ground for a refusal is usually the *model* (a chip that owns the clock, a re-entrant drain), not
the line count, so name both or the next session re-argues it.

## A fast path's prize is bounded by *which* suspension it is allowed to remove (#42's leaf)

#42 priced the memory leaf off §47's `A − B` — **115.4 ns a bus cycle = 1.64 ms a frame**, the CPU's own
suspension — and off a denominator of "how many bus cycles are a closed class-2 exchange". Both numbers were real and
the prize was not. Five rules, in the order they were learned:

- **Take the denominator before the prototype, then check the ceiling against the *permission*, not the ticket.**
The count is cheap and can end the question: every CPU bus cycle is one suspension (`Cycle` awaits
`WaitFor(long.MaxValue, Wires.Answer, …)`), **14 227.1 a frame**, and **76.5 %** of them are closed (nothing due at or
before the answer) — 96.9 % during blanking, 64.7 % on an active line, the answer **30.2 ticks** after the assertion.
That is a healthy denominator; the prototype was still worth 0.16 ms, because ADR 0008 §7 **condition 5** ("touches
no subscription but the CPU's own") makes the answering chip's wake mandatory, so the CPU must stay suspended and the
1.64 ms (the bench's variant B, the device answering on the CPU's stack) is outside the permission. What remains
to remove is the chip's **own slot wait**.
- **A held date is not a wake you can price at 119.2 ns.** §22's 119.2 ns is a *continuation* — the chip's own work
plus the machinery — and a leaf moves the work rather than deleting it. The rent is the engine's resume path (§48:
8–18 ns); measured **14.2 ns an exchange** (0.16 ms over 11 243 inlined exchanges, its own gate included). Third time
a wake was priced by its continuation instead of its rent (§50 was the second).
- **A CPU-side fast path cannot even decide at the assertion for ~43 % of cycles.** A chip's `Clocks()` runs its own
clock past the instant the machine has drained (30.0 ticks an assertion here), and at that moment the exchange's own
four records are already in the queue — so "nothing else is due" is unanswerable there. Count it before designing
around it.
- **Appending a record at a future instant moves BOTH folds, and only by batch membership — say which.** The digest
folds one word per *drain batch*, so an early answer lands `MD`/`answer` in the asserting batch's word: batch 0 reads
the empty-batch constant in one run and a hash in the other. The record stream (order, instant, `seq`, channel,
driver, mask, value) is unmoved, and **the per-log record totals (`records_retained`, `overwritten` per log) are the
proof** — quote them, because the folds cannot show it. The fold-neutral shape is the *worse* one: queuing the two
records at the slot instead made the folds byte-identical and the leaf a **measured no** — 3/6 pairs against,
`alloc_mb_per_frame` 7.94 → 9.42 (+19 %), `pops_per_frame` 63 592.7 → 85 204.4 (+34 %).
- **A chip that answers early needs the run's horizon.** The reference path never knows where the run stops (the
scheduler simply never reaches the instant); a leaf answers one exchange **past the end of the run** and appends two
records beyond it — one extra `bus_answers`, and in the append-now shape two records in the last batch's fold. Expose
`Engine.Horizon` (= `Run`'s `endInstant`) and decline past it; falling back is always safe, which is why a frame-edge
decline costs nothing.

**The outcome to expect from this family:** 122 lines in two files (the chip + the engine), `Cpu/M68000.cs` **not
edited at all**, **no `await` removed** — the coroutine fear is half-true, and the half that lands is "an instant the
machine has not reached yet", which is four invariants at once. Price that against 1.6 % of a green frame and refuse
it in the page (FINDINGS §24), keeping the counter: it prices any future fast path in an afternoon.

**The machine's "now" is the queue's head, not any chip's clock — so a synchronous exchange is a scheduler change.**
When the owner asks for "no subscriptions left": each chip has its own clock, `MachineTime` is the queue head (plus a
held date), and only `Run` moves it. While the core executes, the bus cycle is what moves it — **because the cycle
leaves an entry behind** (4 `ApplyWrite` at the assertion when the core's clock is ahead of the drain, 43 % of the
time, then the answer's `Resume`). Measured on a built prototype: a CPU that answers its own exchange (quiet appends,
no queue entry) executes **11 exchanges while `machine_time` stays at 0**, the chip's beam runs past the core, and the
boot parks at instruction 3 — a lost obligation, which fires no assertion. The engine's `Run` is also **resumable but
not re-entrant**: `_dueChips`, `MachineTime`, `_previousInstant`, `_datedChips` and the per-batch digest are per-drain
state written for one batch at a time, so a chip calling `Engine.Run(slot)` from inside `Cycle` either rebuilds the
due list under the outer loop (a chip resumed at the wrong instant, then re-entrant recursion) or, with the list made
per-depth, diverges. **But do not stop there: make the drain re-entrant (per-depth due list + `Horizon` save/restore)
and let the CPU drive it and the shape WINS — 1.41 ms a frame**, the same binary with the mode off and on, three
alternating 800-frame pairs (9.53/9.54/9.32 → 8.19/7.99/7.98), `alloc_mb_per_frame` **7.94 → 3.58** (−55 %), 99 ns a
bus cycle against the bench's 115.4. It tracks bus cycles, not workload: boot window +1.39, desktop +1.49, the ROM's
RAM-test frames +1.85. That is the number §7's condition 5 is worth (a seventh of the frame), and it belongs to a paper
decision (one clock the CPU owns), not to a leaf: a landing still needs the pin sampled at the core's own boundaries
**re-blessed** (−29 instructions, −65 bus cycles over 724 frames), a fold keyed on a record's instant rather than the
batch, and `MachineTime` as a stack.

**…and check your own prints before you believe a sign.** That prototype first measured **41 ms a frame and 61 MB** — a
5× loss — because a scratch print sat on `if (iterations % 20000 == 0)` inside the *drain loop*, and `iterations` is
zero on almost every re-entered call, so it fired on every contact: 36 000 console writes a frame, which made a
winning shape look like a catastrophe. An instrument on a path that is now entered 50× more often is a *new cost*, not
the same cost. Delete the prints, re-run, and only then read the sign.

## A bounded subscription (a timeout): what it costs, and why it loses

#43 needed a derivable bound on how far back a log must reach, and the obvious device is a timeout on the
undated arms (`WaitFor(Clock + Δ, …)` instead of `long.MaxValue`). Measured, it is not clock-neutral:

- **A Δ short enough to keep a small window fires during a wire's legitimate silence.** At Δ = 2 048 the
  300-frame boot took **36 209 timed wakes** (120.7 a frame) and moved both folds. The arithmetic that
  predicts it, before you build: blanking is 113 lines × 2 048 ticks = 231 424 ticks of no `group`, and
  **231 424 / 2 048 = 113 wakes a frame**. Compute `silence / Δ` per wire first; if it is not ~0, the
  arm will fire and the stream will move.
- **A Δ long enough not to fire needs a ring you cannot afford**: `silence × records-per-tick` — 3 240 on
  `group`, 12 300 on `MFPCS`, a 16 384-record ring. The bound and the capacity are one number.
- **The counter to add before deciding** is `timed_wakes` in `run.state` (count in `ExecuteDate` when the
  chip's held date carries a timeout flag). `queue_pops` does **not** move for a held date, so pops are
  the wrong witness: read `subscriptions` and `stale_wakes` (both +1 per firing) and `timed_wakes`.
- **Filed versus absorbed**: a moved `run.stream` from a timeout is the owner's re-bless decision. #43
  answered rule 1 "no" and kept the ring, because the window's real bound is `DateInstant − From` for
  every dated arm plus "the ring always holds the newest record" for the rest.

## An instrument Release can read is rent — and the clock is the worst kind (#66, FINDINGS §22)

The bracket that was supposed to find the last ~1.6 ms of the frame found that 1.40 ms of it was *instrumentation*:
`Engine.Run` read `Stopwatch.GetTimestamp()` **four times per instant in Release** (95 108 reads over 23 777.3
instants) to fill `drain_ms`/`instrument_ms` in `run.state`. `#if DEBUG` around the four reads and the two report
lines is **−1.40 ms over six pairs (6/6, 11.13 → 9.73)** and put G8 green at 9.60. Three rules, in the order they
cost time:

- **Before pricing a chip, grep the engine for its own instruments**: `grep -n "Stopwatch" src/TheST.Machine/*.cs`.
  #14/#58's rule ("a Debug instrument that keeps a counting structure pays for it in Release at every event it
  counts") applies just as hard to a *clock*, and a clock is more expensive than any counter. ADR 0008 §6 had
  already decided Release carries no digest; a Release clock whose only output is a Debug report is the same
  decision, so this is design-neutral and needs no ADR.
- **A read costs what the loop around it costs.** In situ 14.7 ns a read against 7.7 ns for a bare read in a tight
  loop, because each pair also pins a live value across the drained call and writes an engine field. Price the
  removal (six pairs), never the nominal read count.
- **A bracket's rent is the same order as its signal, so measure the rent with the same code shape.** Add a
  `null` mode: every site's pair with **nothing between its two reads**. It returned **16.2 ns a pair, uniform to
  0.3 ns across six sites**, which is what says it measured the pair and not the work; a tight calibration loop
  returned 15.4 and agrees. Never derive rent from the frame delta between modes: adding pairs to the hot loop
  cost 2–4× the sum of their intervals (branch mode: 186 303 pairs, +7.0 ms), because the reads change the loop's
  registers. And never nest an outer pair around inner ones — it reported 2.4 ms of "switch overhead" that was
  spill.

**The split this produced** (Release 800 / 724 frames, per entry net of the 16.2 ns pair): chip continuations
**8.55 ms** (39 147.6 resumes at 164.5 ns, 17 671.4 dated wakes at 119.2 ns), engine dispatch **0.79** (ApplyWrite
0.45 at 18.4 ns, `NextInstant` 0.17 at 7.2, Resume engine 0.14 at 3.7, `ExecuteDate` engine 0.02), the loop's rest
**0.40** (switch, dequeue, drain prologue, GC 0.12). It closed on the pair mean to 0.01 ms — 8.55 + 0.79 + 0.40 =
9.73 — which is the only reason to believe a split assembled from six brackets.

**Per-entry work is now at the bracket's floor**: ApplyWrite at 18.4 ns is 1.1× the pair used to time it, so any
further lever in the engine must be priced by **removal over six pairs**, never by a bracket. The two levers that were
left — the leaf that inlines an exchange and the chips on separate threads — are **both closed `not_planned`**, with
their numbers written down (FINDINGS §24, §24a and §24b for the leaf and its CPU-driven variant; §25 for threads), so
the ladder is shut: do not reopen either without a new measurement, and quote the closing pages rather than the
tickets. What remains in the engine is a model ruling, not a perf ticket.

## Measurement discipline (the part that produces wrong answers)

- **Alternate the order.** A laptop host drifts ~2 ms across a session (turbo/thermal). Pairs run
  always-A-then-B credit B with that drift: the same cadence pairs read 1.15–2.02 ms A-then-B and
  1.08–3.52 ms alternating. Never publish an A-then-B pair.
- **Never compare runs separated in time.** Only adjacent pairs travel; absolute numbers differ per host
  and per hour. `tests/performance/FINDINGS.md` §4 says the same.
- **Know the noise floor.** On the i7-12700H host it is ~±1 ms a frame. A sub-millisecond change needs 8+
  pairs and unanimity before it is a claim; a non-unanimous sub-ms result is "unresolved", not "small win".
- **At the 0.3 ms scale a single pair is not a measurement — take the median of three.** On **byte-identical
  cores** (identical `src/`) one three-pair run read **+0.42 / +0.28 / +0.20 ms** and the next read
  **−0.23 / −0.07 / +0.40**, so a rule of "every pair inside the floor" fails a tree with no change in it at
  all. The datum is the **median** of the three deltas with every delta printed beside it: a real regression
  moves all three, host drift moves one. Say in the report which rule was applied — "worst pair" and
  "median" are different verdicts on the same three runs.
- **Watch `alloc_mb_per_frame` in `run.state`,** not just the mean. A collection-shape regression shows up
  as a step in allocation (a reverted "optimization" read 22.78 → 26.77 MB) before it shows in the mean.
- **Discard any run taken at `loadavg` above ~1.5, and name the consumer before believing a regression.**
  The same tree read **18.54 ms** with load 1.5 and **16.89 ms** on a re-run minutes later, because a local
  inference server the desktop app had started was loading; `ps -eo pcpu,cmd --sort=-pcpu | head` names it in
  one line. One gate figure is never a trend: re-run once the box is quiet, and say in the page which run you
  discarded and why (a single G8 row is quoted as an observation, never as a comparison).
- **The projection is a guess; the pair is the number.** #40 projected 2–3 ms and measured **4.25** over six
  pairs, because the losing arm also allocated a `Resume` per arm (22.78 → 20.77 MB a frame). Any win that
  removes objects must be read in `alloc_mb_per_frame` beside the mean, and a claim needs six unanimous
  pairs, not two.
- **A change that alters the guest's instruction rate moves the frame mean's denominator.** Charging the
  CPU's own clocks makes every instruction cost more, so one 20 ms *emulated* frame contains **less work**:
  `instructions` and `bus_cycles` per frame each fell ~4 %, `frame_n` went 747 → 756, the desktop frame
  598 → 607, and the mean fell 0.78 ms (6/6) with allocation −15 %. All of it is real, and none of it is
  readable without `frame_n` and the per-frame work quoted beside it — price such a change against
  `instructions`/`bus_cycles` per frame, never against the frame count.
- **Never price "one extra suspension per instruction" from a wake cost.** A ledger's 119–165 ns a wake gave
  "5 427 instructions a frame × 0.15 µs ≈ 0.6–0.9 ms against", and the measured move was +1 % suspensions
  (`38 050.8 → 38 415.1`) with `pops_per_frame` *falling* 40 % (`59 397 → 35 812`), because a chip's own dates
  coalesce the engine's instants. When a change moves a chip's dates, count the suspensions **and** the pops
  it actually moves before predicting the frame.
- **A bug in the drain loop fails by hanging, not by failing.** The first #40 build executed the queue's
  head without `Dequeue`-ing it: a 20-frame boot never returned, and a 300-frame one died at the tool's
  timeout having written no files at all. Run a 20-frame boot first — it takes ~3 s — and treat "no output,
  no `run.state`" as a hang, not as a slow host.
- **An empty instant is not a record.** An instant drained with no record appended says nothing about
  the machine. Folding such instants into `run.stream` made five dropped empty batches look like 9.4 M moved
  records; the fold now writes a word only for instants that have records.
- **`drain_ms`/`instrument_ms` are Debug-only since #66** — a Release `run.state` has no such line, so a page
  quoting them is quoting a Debug frame. **A stopwatch counter read in a Debug boot is not frame-time evidence.** `trim_ms`-style totals, resume
  µs and per-phase tick counters describe the *shape* of a cost (how many calls, how long each); a Debug
  frame mean is 4× Release. Caveat the number as "Debug-measured, re-take in Release" or price the change
  with pairs — never quote the counter as the win.
- **`pops_per_frame` and friends are a rate over the frames you asked for, not over the boot.** The same
  machine reads **85 298.9** pops a frame on `boot --frames 300` (Debug) and **63 193.2** on
  `--frames 800` (Release), because the post-desktop frames are nearly idle. Quoting the 800-frame figure
  against a 300-frame run reads as a 26 % regression that does not exist; compare like-for-like or use
  `pops_per_bus_cycle`.

- **A failed Release build does not stop the gate — it re-runs the previous binary, and the numbers read
  perfectly.** `validate.sh` can print a full, plausible `g8/run.state` from a stale `bin/Release` while
  `build-Release.log` says `Build FAILED`; the tell is that the figures reproduce *the other tree's* counters
  (`pops_per_frame`, `frame_n`, `_frclock_at_desktop` all one build behind). Read
  `runs/<stamp>/build-Release.log` **before** quoting any row, and treat "these numbers look like the old
  tree" as the failure it is. A Debug boot in the same run will be the new code while the Release rows are
  not — that mix is the signature.
- **A `#if DEBUG` instrument that a tool reads breaks the Release build.** A harness (or bench) referring to
  a `#if DEBUG` member fails `CS1061` in Release, so the gate builds nothing and reports the previous run.
  Guard the *tool's* use with `#if DEBUG` and answer a message in Release; do not make the member
  unconditional — an instrument Release can read is rent (the #66 rule).
- **Count the async hops you add on a hot path, and price them by removal.** A fidelity change that routed the
  hottest read through two extra `async` forwarders (`FetchAsync → FillAsync → ReadProgramAsync → Cycle`) cost
  **0.95 ms of a 9.x ms frame** — 11.05 ms red, 10.10 after folding the forwarders, 9.84 after inlining the
  read itself, with every counter and both folds identical across the three. So when a machine change moves
  the frame mean but not the counters, count the hops on the per-cycle path before pricing the *work*: #62's
  rule ("a forwarder is a frame") applies to every forwarder you add, not only to the ones a fold removes.
- **An arithmetic op per instruction is a G8 number too — small, but the difference between a comfortable
  landing and one on the edge of the bar.** A decode-time store added to the CPU's per-instruction path was
  first written as three shifts and ors and read **+0.23 ms on the gate's median pair**; the same fields as one
  shift read **+0.08 ms**, with every counter and both folds identical. Write any new per-instruction work in
  its narrowest form before the gate run (pack contiguous fields with one shift, hoist the rest to the decode
  that already runs once), and read the two packings as the measurement rather than counting ops on paper: at a
  0.3 ms bar, +0.23 is a judgement call and +0.08 is a pass.

## A subscription's real cost is the arms it abandons (#50)

Before pricing a *new* wake arm, read `stale_wakes` — it is usually the real regression, and it is often
already there. A chip re-arms every wake on every resume, so a chip woken *n* times between two records
of a wire leaves *n* dead subscribers in that wire's list; the next record tears them out one
`List.RemoveAt` and one stale wake at a time. Measured at `3eb1231`: Timers 6 373 resumes a frame,
581 dead arms a frame on `MFPCS`, **23 680 stale wakes a frame**, 2.93 ms of the frame gone. Replacing
the arm the chip already holds at the same cursor (`AddSubscriber`) fixed it and left both folds
**byte-identical** — a queue change that removes no record and no arrival number.

Two consequences for pricing:

- **Price the delta against the tree that already has the churn fixed**, or the new arm's cost is hidden
  inside a bigger regression. #50's `DE` arm measured 2.15 ms *against* on `3eb1231` and 0.56 ms against
  the fixed tree; the pairs look contradictory until the engine fix is separated.
- **A wake costs 1–2.6 µs, not 96 ns.** 400 wakes a frame is 0.4–1.0 ms, and #44's 96 ns-a-pop
  arithmetic has now failed to transfer twice (#40, then #50). Six pairs, or the number is a guess.
- **A per-chip instrument is five lines — and a measurement hazard, not a free probe (#58).** The recipe
  is a `Dictionary<string,long>` (or `Dictionary<Chip,int>`) in `Engine.cs` incremented in
  `Execute`/`ExecuteDate` and in `AddSubscriber`/`Subscribe`, printed once per `Run(endInstant)`. Ratios
  diagnose: 1 arm a resume is healthy, 2 means a second wire, 17 means abandoned arms. **But price it
  before trusting a number taken with it in the tree**: `Engine._freshWakes`, exactly that shape, cost
  2.66 ms a frame by its own removal (FINDINGS §14, six unanimous pairs) — more than most deltas it is
  used to measure. Put the counting in a scratch build under `tools/`, or use a per-chip `int` field
  (~1 ns a write), and remove it before committing.

**When `run.stream` moves and the owner cannot be reached**, split the work: land the stream-identical
part on `main`, push the moving part as a branch (`issue-number-shape`), and comment the ticket with the
move, the diagnosis and the re-bless request. Do not absorb a re-bless, and do not leave a dirty tree
for the next session to trip over.

## Moving a counter is not always the fix: look for a duplicate one (#58)

When a ticket says "move this Debug counter somewhere cheaper", **measure the counter against what is
already there first**. `_freshWakes` was a `Dictionary<Chip,int>` written at five arm sites and five resume
sites and read only inside `#if DEBUG`; it was a second copy of `Chip.Armed` (both bumped at arm, cleared
when a wake fires), proved so by a temporary probe in `AfterBatch` comparing the two at every batch
boundary of a 300-frame boot. The field the ticket proposed would have kept the duplication and made
assertion 2's cheap half compare a number with itself — so the ledger was **deleted**, and the assertion
kept two real halves (held dates vs `_datedChips` plus the queued-`Resume` rule every batch; the full
queue/subscription rebuild vs `Chip.Armed` chip by chip every 256th batch). 2.66 ms a frame; both folds
byte-identical.

**The general rule:** a Debug instrument that keeps a counting structure pays for it in Release at every
event it counts. If Release can read it, only Release's own rows can show its cost; if it cannot, it is
rent.

## Pricing the continuations themselves (a study): the trace-replay bench (#47)

When a ticket asks "what does a suspension cost, and out of what", do not profile — **remove** the thing and
measure the difference, over the machine's own workload. `tools/cpu-dissection` is that bench and it is
committed; copy its shape rather than inventing one.

- **Capture the workload, then replay it.** A temporary `M68000.Cycle` hook (plus `Engine.AppendRecord` for
  the `IPL` timeline) dumps each bus cycle — address, size, flags, **control word**, data, asserted instant,
  answered instant, answer, `MD` — and a boot supplies it. The bench then replays the trace into the CPU's own
  sources, checking at **every** cycle that the same four lanes were asserted at the same instant, and ends
  when the trace does. That check is what makes "same work" a fact instead of a hope: all four variants
  replay to the same final `pc`/`sr`/`d0`/`a7`.
- **A replay must start at cycle 0.** The state at cycle n depends on 0..n−1; a window of a trace is a
  different machine, and the bench throws on the first mismatch. Cut a run by stopping earlier.
- **Capture the control word.** Without it the cycle's `FC` nibble and size bits cannot be verified and the
  self-check is half blind.
- **Variants: A real, B awaits-inline, C no async, D folded.** A/B are the linked `src/` sources with a
  runtime `Mode` flag in the awaiter (`IsCompleted => true`, and the device answers on that stack); C and D
  are **generated** from the same files by `gen.py` (`await` deleted; the bus helpers folded into one leaf).
  Never hand-edit a generated variant — regenerate, or it drifts from what it prices.
- **Everything in one process.** Host drift across processes is larger than the deltas; with all variants in
  one assembly and alternating passes, the deltas were 6/6 unanimous and the noise was ~2 %.
- **The denominator is bus cycles and instructions, never frames**: C never waits, so it executes a different
  number of frames a second.
- **The allocation bracket is the trap that produces a false 48 B.** `GC.GetAllocatedBytesForCurrentThread`
  read *inside* the awaiter sees only what the engine allocates: the state machine box and the continuation
  `Action` are allocated **before** `UnsafeOnCompleted`. Bracket from the *previous* suspension instead, and
  read the histogram, not the mean.
- **What a suspension really allocates (measured, .NET 11):** the machine's `Resume`/`Wake` **48 B**, a
  continuation `Action` **56 B**, a state machine box 40–128 B, and **one box per suspended frame** — 4–6 a
  resume on the CPU, because `Cycle`/`ReadWordAsync` are invoked once a bus cycle. `Run`'s box is once per
  chip; nothing else is. So "pool the `Resume`" reaches ~8 % of the allocation line.
- **The result this bought** (2026-11-15, laptop host, 300-frame boot trace, 5 564 478 cycles): a resume is
  ~420 ns and **84 % of a bus cycle's cost**; the state machine is 42.7 ns (8.5 %); the CPU's own work ~7 %;
  folding the bus *wrapper* frames is 80.3 ns a cycle (16 %, landed as #62 at 101 ns a cycle measured); a coroutine-free CPU
  would save 460 ns a cycle (~6.55 ms a frame, ~37 % of a then-18.07 ms frame) — the ceiling for the
  memory leaf. **75.7 % of suspensions are instruction fetches**, so the CPU's cost is a fetch chain's cost.
- **A landed variant can break the tool that generated it (#62).** `gen.py`'s `strip_async` rewrote only
  `async ValueTask<T>`; the fold left non-`async` forwarders (`ValueTask<ushort> ReadWordAsync(..) => leaf;`),
  so variant C stopped compiling with 40 errors inside `Gen/Sync/`. Extend `strip_async` to the bare
  signatures, regenerate, `dotnet build` the tool **and** run `check` in the same session as the landing.
- **A stale bench binary lies convincingly.** With the build failed, `dotnet bin/.../cpu-dissection.dll
  check` still printed `same_work yes` — it ran the previous build. Read the build's exit status, not the
  next command's output.
- **The landed fold reads *more* than the bench projected (1.44 vs 1.14 ms a frame).** An engine frame also
  carries a `Resume`, its queue entry, the wake-token check and the debug hooks, so removing a frame there
  saves more than in the bench's one-chip machine. Expect the pair to beat the projection in whichever
  direction the missing engine cost points; never assume the sign.
- **After a fold lands, its A − D row is spent** (A *is* D) and the bench keeps earning its keep only as a
  witness: `check` still replays the boot's 5 564 478 cycles to the same `pc`/`sr`/`d0`/`a7`, a per-cycle
  check of the real CPU against recorded wire traffic, independent of the digest. Run it and quote it.

## A prize is priced against its counter line, not its bytes (#46)

Before building a shape change, convert its prize into the counter it lands in and compare that with the noise
floor. #46 pooled the queue's `Resume`: **1.80 MB a frame** off a 10.42 MB line (−17 %), `gc_ms_per_frame`
0.18 → 0.13, both folds byte-identical, gate unchanged — and the frame untouched (17 pairs, three shapes, mean
−0.05 ms, never unanimous). 39 300 allocations at ~10 ns is ~0.4 ms of allocation work, the rent/release path
gives most of it back, and the collection line it can reach was 0.17 ms of which it recovered 0.03. A prize
that is a fifth of a fifth of a millisecond cannot be resolved by six pairs at ±1 ms, so the ticket is
**answered, not landed**: a page in FINDINGS with the number, the change reverted, the ticket closed
`not_planned` (#44 and #46 are the two precedents), and the commit message says "no" first.

**Closing a measured no is a five-part act, in one session:** the page (numbers, the projection beside the
measurement, which decided it), the ticket comment with the same evidence, `state_reason: not_planned`, a **named
reopening condition** (the measurement or the code change that would make it worth re-taking), and the artifacts
kept where the next session finds them — the counter, the pair runner, the minimal diff. A no without a reopening
condition gets re-derived; a no whose counter was thrown away gets rebuilt. When the owner's ruling is the close,
quote it in the page's own closing block and put the ruled-out shapes in one table, so the ladder reads as closed
rather than forgotten.

Two corollaries:

- **A no is worth building anyway when it is cheap**, because it converts bytes into a fact — and it can pay
  a *later* ticket: #46's pool measured the queue split by entry kind (1.80 MB of 48-B objects = ~39 300
  fired resumes a frame; `pops_per_frame` minus that = ~24 300 `ApplyWrite`), which is #48's census for free.
- **Do not chase the struct-union version of a losing shape.** `PriorityQueue<Entry, Key>` with an inline
  48 B struct entry saves the same bytes, copies 48 B per sift step on a path that runs 63 592 times a frame,
  and loses the type switch that makes `Execute` readable. Say so in the ruling.

## A census of wakes (#48): three buckets, one bracket, and the limit of the method

When a ticket asks "what is each chip woken for, and is a wake worth anything", the answer is counts plus a
bracket, and five rules make it quotable:

- **Build the instrument in a copy of the tree** (`/opt/data/cache/scratch/st-census` is #48's). It touches six
  files — `Chip.cs` for per-chip work counters, the four chips, `Engine.cs` for an origin tag on `Resume` plus
  the tallies, `BootCommand.cs` for the `census=` lines — and a clean `git status` at the end is worth more than
  the convenience of editing in place. Copy with `tar`, excluding the three trees you do not need (there is no
  `rsync` on this host, and `runs/` alone is many GB), then build **inside the copy** — the harness resolves
  `roms/` against the working directory, so `roms/` must come along:
  ```bash
  mkdir -p /opt/data/cache/scratch/st-census && cd /opt/data/the-st \
    && tar -cf - --exclude=./runs --exclude='*/bin' --exclude='*/obj' --exclude=./.git . \
    | tar -xf - -C /opt/data/cache/scratch/st-census
  ```
- **Prove it inert first.** With every counter in place the 300-frame Debug boot must reproduce `run.digest`
  `6e657033…` / 10 801 967 and `run.stream` `e0ca9570…` / 10 801 251 **and** every machine counter. That check,
  not the numbers, is what makes a census quotable; a census build also emits the same counts in Release and
  Debug, so take the counts in Release and cross-check one Debug boot.
- **Reconcile the new instrument against a counter the machine already prints, before quoting any ratio.**
  Sum the per-chip tallies and make them meet a number that existed before you wrote a line: every chip's
  resumes must equal `pops_per_frame` minus the `ApplyWrite` entries it does not explain (and Video's resumes
  must equal `groups` and `chunks`, the SCU's answers must equal `bus_cycles`). This is not bookkeeping — it is
  how a flat indexed census stops lying: two tallies that share a slot alias silently, and the only symptom is
  a bucket that reads 0 or a share that is 8 % off. If the sum does not close, the instrument is wrong, not the
  machine.
- **`pops_per_frame` is not the wake count.** It counts queue entries: `Resume` entries + `ApplyWrite` entries.
  A held date (#40) is not an entry and appears in no pop counter. Today: 39 143.2 + 24 449.5 = 63 592.7 pops, and
  **62 823.6 chip resumes** — the difference is 23 680.4 held-date wakes. Any "objects per frame × 48 B" arithmetic
  (pooling a `Resume`) prices the first family only.
- **The third bucket is "no work unit", not "consumed nothing".** A wake that consumes a record and does nothing
  is the normal case for the SCU's `Z` release, and a cursor-based test calls it work and reports an empty
  bucket. Count a work unit per chip instead — a bus cycle performed, an exchange begun, a beam event, a chunk
  painted, a timer expired, a `DE` counted — and cross-tab origin × work class; that table is the finding.
- **The bracket prices the engine, not the coroutine.** A `Stopwatch` pair around the dispatch and another
  around the continuation, in two runs of one build (a mode switch), with the pair's own cost measured in the
  same binary (~16 ns) and subtracted. The result on this host: the engine's resume path is **8–18 ns a wake**
  (~1.0 ms a frame for 62 824 wakes), and the 92–620 ns that remain is the continuation — the state machine's
  `MoveNext` **plus** the chip's own code, which no bracket can split.
- **So the "synchronous chip" variant cannot be built for the SCU, and saying so is part of the answer.** #47's
  variant B works because the CPU's device answers on the stack and the chip advances its own `pc`; the SCU's
  loop progresses only when its clock moves, so an inline awaiter spins at one instant forever and a generated
  no-`async` variant is the same infinite loop. Making it synchronous means rewriting `Run` as a stepper — the
  design change being priced. Bound it instead: #47's state machine (42.7 of 420 ns) and the chip's cheapest
  real wake.

**#48's numbers, as a baseline** (Release 800 frames / 724 run, `b289504`): CPU 14 408.8 wakes a frame at
620.3 ns (8.94 ms, 54.2 %), SCU 38 001.7 at 92.3 (3.51 ms, 21.3 %), Video 3 994.5 at 133.3 (0.53 ms), Timers
6 418.6 at 89.3 (0.57 ms) — 13.55 ms of a 16.5 ms frame, 82 %. The SCU is 1 exchange begun + 1 answer (fired by
its own held date, `ExecuteDate` runs exactly `bus_cycles` times) + 0.24 beam + **0.43 wake that finds
nothing** (#44's, measured at +0.63 ms to remove). Timer D expires 6 008.7 times a frame for a log nobody reads.

## Choosing what to measure next (the queue, and how to order it)

Perf candidates live in `tests/performance/FINDINGS.md` §3–§8, and the open tickets are only part of
them. Before proposing work:

- **A row in FINDINGS §6 is often a projection that shipped at half its size.** Read the paragraphs under
the table, not just the table: "256 vs 16 ≈ 5 ms" was followed by a shipped and measured **128 vs 16 at
2.09 ms**, so the remaining half was still open, unfiled. Scope from *measured* rows and ask which part of
the row is actually in the tree today.
- **Per-chip and per-wire µs figures are host- and revision-specific.** The tables that put ~51 % of a
  frame in chip coroutines and 2.6 µs in a CPU resume were taken on a different host at a frame time ~2.7×
  today's. Quote them as a *lead*, never as a baseline, and retake before pricing anything.
- **There is no committed per-wire or per-chip census instrument.** The per-wire table came from a scratch
  profile that never entered the tree. What the gate already prints is aggregate and enough for most
  steps: `runs/<stamp>/g8/run.state` carries `pops_per_frame`, `pops_per_bus_cycle`,
  `stale_wakes_per_frame`, `alloc_mb_per_frame`, `gc_ms_per_frame`; `run.report` carries the digest shas,
  retention, record counts and the frame mean. Write counters only when the question needs them, and keep
  them out of the repo.
- **Order the work by design-neutrality, then by risk**, and let the owner's constraint decide the tail:
  that person will not trade the coroutine model's readability for milliseconds while a design-neutral
  option is still unmeasured. The order that survived review: (1) a wire-catalogue change — subscribers
  woken for records they never consume, which the ADR's own falsifier says to fix in the catalogue;
  (2) a constant, priced over six alternating pairs; (3) **studies that attribute the time** — a resume's
  µs split into plumbing (state-machine box, `MoveNext`, continuation capture, engine bookkeeping) versus
  the chip's own work (decode, execute, log reads), plus a census of wakes that find nothing to do;
  (4) shape changes touching allocation; (5) a design question that needs a rule invented before it is
  even a question;  (6) the fast path that inlines an exchange, last, and only with a gate that names what
  must land before it may start.
- **Put the whole ladder in ONE ticket, ordered no-impact-first.** The owner reads the order, not the
  inventory: a single ledger listing every lever with its potential, the counter it lands in, its
  readability/design cost, a **kill-early rule** (the measurement that says stop before a pair is spent) and
  the same three-part proof for all of them. Order rule: anything that keeps the source shape first (a
  compiler/runtime lowering before a source rewrite), cheapest-to-revert first inside each class. A lever may
  end as a measured no, and the ledger is where that no is written down so nobody retries it — which is why the
  ledger is filed even when only the first rung is executable today.
- **A study ticket must end in a ruling, not a table** — either "cut X, worth N ms, filed as its own
  ticket" or "irreducible inside the model, and here is why". Both are useful; a profile page with no
  decision is not, and it will be re-run by the next session.
- **Say when a number is stale.** A ticket or commit that quotes a pre-change pop count (or a projection
  from a superseded revision) invites the next session to re-derive it the hard way. Re-derive, then quote.

## Removing a wake (`#44`): what it costs, and what it moves

A "wake that starts no work" is the most tempting perf candidate in this engine, and it has two properties
that turn it into a measured no:

- **It is not a no-op: it re-arms the chip's held date.** A chip's loop arms its next date at the top; a
  spurious resume a few arrivals later drops that date (#40) and the chip arms the same instant again. Order
  is `(instant, arrival)` (ADR 0007), so **that second arm's arrival number orders the chip's next structural
  event against another chip's** when both land on one instant. Removing the wake kept every counter of the
  machine identical (`bus_cycles`, `groups`, `instructions`, `pc`, all assertions silent) and still moved
  `run.stream` at **711 of 10 801 251 words** — an ordering inside instants, not a record. Expect any
  wake-removal to hit the re-bless question rather than passing byte-identity.
- **The fire path pays for the filter, on every wire.** A lane/filter test inside `FireSubscriptions` costs
  a branch per record per wire (~93 000 bus records a frame alone), and the waiter's "already answered" test
  stops being one comparison on the newest record and becomes a scan. Six alternating Release pairs on a
  change that removed 6 113 pops + 6 109 date arms + 6 109 subscriptions a frame: **+0.63 ms, 5 of 6
  against it**, alloc −0.18 MB. The 96 ns-a-pop figure derived from #40 does **not** transfer to wakes that
  find nothing to do — price these with pairs, never with that arithmetic.
- **Before cutting a wake, name its consumer, and probe the peripheral's programmed state.** A record can feed
  a machine behaviour (a counter, a timer, a rate a gate row publishes, another chip's deadline) or nothing but
  a log. Read the log's depth census: no subscriber and a reader need of 0–1 at the machine-wide floor means the
  log is the only output, which makes the wake a candidate. A wake that feeds a peripheral's counter is **not** a
  candidate even when that peripheral is programmed to zero in the boot you measured — an arm exists so a quiet
  chip's cursor cannot fall behind the wire, and removing it makes the counts arrive late and in batches the day
  the emulated software programmes the counter. Two lines of public accessors in a scratch build (the control
  and data registers, an expiry count) answer "is this peripheral live?" in one boot; the wake such a cut
  removes is worth tens of ns, and the behaviour it can break is worth nothing you can measure back.

**A silent stall is an unobserved exception, not a slow host.** Chips are launched `_ = Run()`, so a
throw inside a coroutine lands in a `ValueTask` nobody awaits: the machine stops doing work, `frames_run`
still reaches its number, and no assertion text reaches `run.state`. Tells: `batches` and `queue_pops` a
quarter of their normal size for the same frame count (2 742 175 against 10 801 967), the folds moved with
no `ASSERTION` line anywhere. Print the message before throwing from the debug `Fail` (or check batches
per frame) — otherwise a new debug assertion reads as a mysterious 4× regression.

**Recipe when the folds move and the counters do not** — find *where*, not just that it moved. Add a temporary dump to the `#if DEBUG` partial methods (never committed): print every record with its instant
and a batch number in `AfterRecordAppended`, and every resume with its chip and clock in `BeforeResume`,
gated on an instant window around the first differing batch (map it with `run.batches`, then `boot --frames
1` takes ~2 s). Diff the two runs' `C…`/`R…` lines: an extra `R` line and a swapped pair of records is the
whole diagnosis. Restore with `cp` of a saved copy plus `git checkout -- <explicit paths>`; keep the feature
and the dump in separate saved copies so each can be put back alone.

**Check the chip's own anchors before hunting an ordering bug.** An internal anchor is stamped with the
chip's clock (ADR 0008 §6), not with machine time, so any change that makes a chip wake more often — a new
arm, a removed poll — makes its `register`/`counter`/`timer` records **arrive earlier and in a different
batch** while the machine does exactly the same work. Filter the dump to that chip's `ChannelKind.Internal`
channels and diff the two runs on **instant and seq separately**: a record that keeps its instant and takes
a new `seq` landed in a different batch of the same instant (a fresher clock, not a moved event), and a
record that moves *forward* in time is the anchor telling the truth about when the chip was last awake. Both
are declared consequences of chip-carried clocks — the owner's re-bless decision — and neither is a bug to
"fix" by matching arrival order.

## Bounding retained history (the ring questions)

Before proposing a fixed-size ring, or a timeout as its capacity derivation, read
`references/retention-and-reader-lag.md` — the storage/primitives map, where a read reaches into the past,
and the rule that decides a capacity. The three rules that cost the most to re-derive:

- **Derive capacity from a measured reader lag, never from a chosen timeout.** Every `WaitFor` arm that
  carries a date already has an exact horizon in `chip.DateInstant`; a timeout on top of it fires wakes
  that renumber arrivals (arm-neutrality, see above) and, as a bound, is orders of magnitude looser. A
  timeout belongs only on the two `long.MaxValue` arms, as insurance that should fire never — count it and
  expect 0.
- **A ring must answer 0 below its oldest instant, tested first.** After wrap a slot holds a newer record;
  without that test the ring lies where the log said nothing, and no digest can see it.
- **The ring's witness is a new assertion and a gate row, not the folds.** Measure the per-log deepest need
  over more than one workload before choosing a capacity — the boot is one sample of one workload.

## The three-part proof every step needs

1. **Digest pair *and* the stream fold** — Debug only (Release carries no digest by design). Two boots
   matter, and they are not the same run:
   - `bash scripts/validate.sh` runs its boot row at **800 frames** into `runs/<stamp>/boot/` — the gate's
     own copy, ~21.4 M words a fold on the current tree;
   - the **reference pair** a ticket quotes is a separate `boot --frames 300` — a *digest* run only: it prints
     `frames_to_desktop=never` on every tree, `main` included, because the desktop lands at emulated frame ~600,
     so the desktop frame is read from the 800-frame gate boot instead. `run.digest` is one word per
     batch, empty or not; `run.stream` is the same records folded **without** their seq, one word per
     instant *that has records*.   **Current (after `#74`'s placement, re-blessed 2026-11-18): `d02f38b5…` / 8 558 021 words
   with `5674792b…` / 8 557 232** — the same word counts as `#70`'s pair, and not one word moved, so a
   **placement** change is told apart from a **queue-contents** change by exactly that: a placement keeps both
   counts and moves the bytes; the queue (`#70`) kept both counts too but had moved 578 616 words out. Before
   `#74` (after the prefetch queue, `#70`, re-blessed 2026-11-18): `2615b600…` / 8 558 021
   with `876c79fb…` / 8 557 232 — **578 616 words fewer** in each fold than `#36`'s pair and not one word moved, because the
   count is one word per drain batch and a queue that fetches a word ahead coalesces the fetch instants into
   fewer, denser batches. The workload is unchanged and that is the proof: 300 frames of both trees carry the
   same records (1 198 480 group deliveries, 119 848 `DE`, 93 750 `HBL`, 299 `VBL`, 4 626 retained). Before it
   (re-blessed 2026-11-18, #36's decode fixes): `d7768cb1…` / 9 136 637 and `96348b12…` / 9 135 918 (before
   it, #63's Timer D cut: `2bfb229b…` / 9 136 634 and `238faddf…` / 9 135 915; before that: `6e657033…` /
   10 801 967 and `e0ca9570…` / 10 801 251; before the `DE` subscription: `93c4dbd4…` / `e52ed136…`).

   A **re-bless** changes that pair. Read the newest FINDINGS section, the closed ticket and the current
   handoff before quoting a sha, and write the new pair into all three when one moves.

   **Pending re-bless, `#74` (the read's placement, PR #75):** `run.digest` `d02f38b5…` / **8 558 021 words**
   and `run.stream` `5674792b…` / **8 557 232 words** — *both word counts unchanged*, the bytes different,
   because a bus cycle now lands at a different instant. That is the shape to expect from a CPU placement
   change: it never adds or removes a record, it moves one. **Re-blessed by the owner 2026-11-18** (merged as
   `351c9ee`, the page is `tests/performance/FINDINGS.md` §28): that pair is `main`'s now, and
   `2615b600…` / `876c79fb…` is `#70`'s — quote the old one only as "before".

   The synthetic is `g1-a/run.digest` (**95 999 words**, `58fcdddf…`) with `run.stream` `cd358809…`. Folds
   are host-independent: they matched byte-for-byte on two hosts.

   **The rule since #39:** a change to the queue's *contents* must leave `run.stream` byte-identical.
   `run.digest` may move when an instant that appended no record stops being drained — that is declared,
   not a bug. If `run.stream` moves, a record, an instant or an ordering moved; `harness diff` names the
   first differing batch and says which fold moved.
2. **Adjacent alternating Release pair** for the frame effect, plus `pops_per_frame` (**59 397.0 / 4.08 a
   bus cycle** on the current tree; 63 592.7 / 4.47 before #70's queue, 63 193.2 / 4.44 before the `DE` arm,
   107 215.6 / 7.54 before the counter split). With the counters split (#39), pops moving on a
   queue-contents change is *expected* — read it as the size of the win, not as a hazard.   Current frame (the gate pass that closed the 680x0 corpus, read from `runs/<stamp>/g8/run.state`):
   **8.94 ms at n 751 — green**, `frame_mean_on_desktop_ms` 8.23, **34 122.2 pops a frame (2.49 a bus
   cycle)**, 4 110 085 instructions, the desktop at **602**. Measured on a quiet host (`loadavg` 0.57 of 20
   cores); the same tree read **15.23 ms** on a loaded one minutes earlier, so a single red G8 row is a
   host observation, not a regression — re-run quiet before opening anything.
   The **9.75 ms / 59 397.0 pops / 38 050.8 suspensions** figures below are `#74`'s and predate the CPU
   fidelity batches: the queue (`#70`) and the control transfers cut `pops_per_frame` by ~40 % and moved
   `frame_n`, so they are history to quote, never a current baseline. Read `g8/run.state` for today's.
   Keep `#74`'s own measured lesson anyway, because it is the shape any *placement* change has — it moves
   which read delivers which word and nothing else: A/B against `main`, three interleaved runs each,
   9.82/9.55/10.02 versus 9.94/9.91/9.61, every counter identical. A placement change that shows a frame
   delta is measuring the host.
   **A control-transfer change moves the frame's own denominator**: the queue makes a branch pay the manual's
   two fetches, so the boot reaches the desktop at frame **591** instead of 568, `frame_n` goes 723 → 747, and
   the mean rises 9.65 → 9.84 without any record count changing. Quote `frame_n` and `_frclock_at_desktop`
   beside the mean, and prove the workload with `records`, never with the frame count.

   **Since 2026-11-15 the mean excludes the warm-up frame** (the owner's ruling, FINDINGS §23, `design/validation.md`
   §3): frame 1 is the JIT and first touch, ~110 ms here = 0.15 ms of a 724-frame mean. The harness skips it
   (`frame_n` 723, `frame_warmup_excluded` 1) and the per-frame counters keep their own denominator, the 724 frames
   the boot ran — **two n's, both printed, deliberately**. Two rules follow: quote `frame_n` beside any G8 figure,
   and **never compare a mean taken before 2026-11-15 with one taken after** (11.40 / 10.02 / 9.60 are 724-frame
   means; 9.53 / 9.62 are 723-frame).

   **A study that changes no machine code has no pair, and must say so.** Write "no pairs: nothing in the tree
   changed" and let the tree's own proof stand (both folds, the gate's `G1` row, G8 at its own figure). Never
   manufacture a pair by comparing two runs of identical code — that reports host drift as a result, and the
   next session inherits it as one. A no-code tree also cannot be settled by comparing the built assemblies
   byte for byte: a comment-only edit moves the MVID and the PDB checksum, which Roslyn derives from the
   compilation's *inputs*. Build both sources in the same tree and diff the two DLLs — only the PE timestamp,
   the MVID (the first entry of the metadata `#GUID` stream) and the debug directory's CodeView/PDB stamps may
   differ, and any other differing byte in `.text` is a real code change.
3. **`bash scripts/validate.sh`** — the steady state is **PASS, every row ok, G8 green** (n 747; 9.75 ms on
   the pass that landed `#74`, 9.84 on the pass that landed `#70`; 9.67 and 9.85 at n 723 on the two passes
   that landed #36). A `1 FAIL` on G8 is a **regression**, not the normal red — and because the green is only
   ~0.2–0.4 ms wide, quote two adjacent passes before calling one: they straddled the budget (10.02 red, then 9.60
   green, same build, same counters). Read the digests from that run, so one gate pass serves as proof 1 and 3.
   **A G8 row taken right after a long working session is a contaminated sample, not a regression**: `#74`'s
   first gate read **11.57 ms** where three interleaved A/B runs of the same two binaries read 9.82/9.55/10.02
   against 9.94/9.91/9.61 — the box had just done a 26 s corpus run and two 800-frame boots. Re-run the gate on
   a quiet box before believing any single row, and never open an optimisation on one reading.
- **The gate's own frame `n` moves with the machine, and that is not drift.** A change that makes a control
  transfer cost what the manual charges (two fetches instead of one) shifts the boot's desktop frame
  (`_frclock_at_desktop`) and `frame_n` together — measure the machine work that must NOT move instead
  (`records`: group deliveries, `DE`, `HBL`/`VBL`, retained) and quote them to prove the workload is the same
  before arguing about the frame.
- Run the gate with `background=true, notify=true`. A foreground call that hits its timeout **kills
  validate.sh mid-gate** and leaves an empty `runs/<stamp>/g8/`. One pass was ~10 min before #65 L1 and is
  **~2.5 min** now (the Debug boots went 3 min → 26 s); the background rule still stands, because a timeout
  is the failure mode that loses the run, not slowness.

## Landing it (this repo's working agreement)

**Stage explicit paths — never `git add -A`.** This clone can be open in a second Hermes/Kilo session at the
same time (the owner works tickets in parallel). `git add -A` on a shared tree commits another agent's
in-flight code under your message, and `git commit` may commit *your* edits under *theirs*. Check
`git status` for files you did not touch and stop if any appear. Confirm the tree is quiet before a gate
run too: a build made at the start of `validate.sh` does not describe source edited while it runs.

**The converse bites just as hard: a live session's `git commit -a` sweeps *your* uncommitted edits into
*its* commit**, message and all — a script, a baseline file and a generator fix all landed inside a commit
whose subject was about something else, and once pushed that attribution is permanent (never rewrite a
shared, pushed history). So **commit each verified slice immediately, before starting the next one**: a
committed slice is also a slice whose gate numbers belong to a commit, which is what a handoff or a ticket
comment is supposed to be able to say.

**Every session, before writing a line, prove the tree is yours** — `git status --short` alone is not
evenough, because a live session's edits look exactly like your own:

```bash
cd /opt/data/the-st && git status --short && git log --oneline -1
ls -la --time-style=full-iso $(git status --short | awk '{print $2}')   # mtimes minutes old = live session
ps -eo pid,etime,cmd | grep -E "MSBuild|VBCSCompiler|validation-harness" | grep -v grep
```

Three tells, any one of which means **stop and ask the owner** rather than working around it: files
stamped within a few minutes of `date`; a live `MSBuild`/`VBCSCompiler`; and diffed code that references a
symbol which does not exist elsewhere in the tree (a half-written edit). Do **not** finish another
session's feature, do not revert it on your own initiative, and do not "just make it compile" — the owner
decides whether to wait, isolate in a separate clone, or revert. Reverting, when asked, is the same
rule as staging: **explicit paths**, `git checkout -- <the files you saw in the diff>`, never `git checkout .`
and never `git clean` (the untracked files may be the other session's). Say in your reply what you
reverted and that the other session will find it gone.

Commit straight to `main`, no PR, no branch dance. `git config user.name/email` must be set
(`Laurent Prud'hon <laurent.prudhon@hotmail.fr>`) or the commit aborts. Commit message: what moved, the
pair numbers, both digests, and the gate's row count with G8's own figure. Put the measured number in
`tests/performance/FINDINGS.md` **beside the projection** and say which is which — twice a projection was
4× the measurement because its micro-benchmark did not describe the boot's own shapes (a 10 000-record log
vs a few hundred; a dictionary that a previous step had already removed).

**`gh` IS installed, at `/opt/data/bin/gh`, and it is git's credential helper** (`credential.https://github.com.helper
= !/opt/data/bin/gh auth git-credential`). Two consequences, each learned by losing a push:

- **Keep `origin` the plain `https://github.com/<owner>/<repo>.git`.** A `git remote set-url` with a token in the
  userinfo *bypasses* the helper, and if that token is the wrong one the push fails with `Invalid username or token`
  while `git ls-remote` still succeeds (public repo, anonymous read) — so the failure diagnosis is misleading. Check
  `gh auth status`, not the remote URL, and `curl -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $GITHUB_TOKEN"
  https://api.github.com/user` for a 401: a token can die **mid-session** (it worked for three pushes and then 401'd
  on the fourth, and on the REST comment that followed). If it is dead, stop and ask the owner to refresh it — never
  accept a token in chat.
- **Evidence to the ticket** can go through `gh` (installed) or the REST API; the token may have Issues write without
  Contents write, so comments and pushes can disagree.

Then append the evidence to the ticket — `gh issue comment <n> --body-file <file>`, or the REST API with
`$GITHUB_TOKEN` from `/opt/data/.env` (`POST /repos/laurentprudhon/the-st/issues/<n>/comments`). Check the comment
landed by reading the list back; a 201 is not a read-back.

**Close the ticket and hand off — every time.** Once the change has landed (and a moved stream has been
re-blessed), fast-forward or merge `main`, delete the parking branch locally **and** on the remote, then
close the ticket with a comment carrying the **new reference pair** (both shas, both word counts), the
gate's row count and G8's figure, and one line per older perf ticket whose premise the change moved — an
allocation figure, a pop count, a "wakes that do nothing" bucket. Then write the next session's handoff
into `<home>/handoffs/` and **post a baseline-refresh comment on the ticket that session will start**:
ticket bodies are written against the tree of their filing day, so their frame, allocation and pop figures
are stale before anyone begins.

Before assuming the queue order, read the next ticket's own text — it may declare that it waits for
another ticket's attribution — and weigh the candidates the last session filed, because a measured
constant is usually cheaper than a study and the owner's order puts constants first.

Two failures of the scripts themselves, which read as product failures and are not:

- **A script that ends on `grep -c` exits 1 when it finds nothing** — a clean run with no `ASSERTION` line
  reports as a failed pass. Read the count in the output, not the exit code.
- **`git push` rejected as `behind 1`** means another session pushed to this `main`. `git fetch`, read what
  landed, `git rebase origin/main`, push again. Never force `main`; rebase a parking branch onto it and
  push with `--force-with-lease`. Two sessions append *different sections* to
  `tests/performance/FINDINGS.md`, so after the rebase re-read your own section (header and last line, not just
  `git status`): a clean rebase says the hunks did not touch, not that the page still says what you wrote.

**Filing the next perf ticket** uses the same house shape as the existing ones: `Part of #32`, the finding
with its numbers and where they were measured, **what it would take** as ordered steps, **Done** as an
acceptance test (the three-part proof, quoted), and **Evidence** pointing at FINDINGS sections, ADRs and
the superseded ticket. Two clauses earn their line: state whether the outcome may be a measured **no**
(a shape change that loses is a legitimate result and must be recorded so nobody retries it), and name what
is **out of scope** — an open perf ticket that does not fence off the coroutine-crippling fast path will be
taken as an invitation to build it.
