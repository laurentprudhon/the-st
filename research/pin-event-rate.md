# Pin event rate: how many cross-chip signals per frame?

Status: complete. **Every figure below is derived (analytic), none measured** — §1 explains why the
measurement path was abandoned at minute ~10 of the budget, and §4 quantifies what that costs the
confidence of the answer. Resolution for wayfinder ticket #18.

> ### Correction — 2026-10-30, owner ruling
>
> The owner has ruled on this document's largest uncertainty (§4 caveat 1): **the DMA parts of the
> video and audio streams live inside the RAM memory subsystem.** They are not crossings between
> virtual chips — they are memory service traffic internal to the memory subsystem, which is exactly
> what `GLOSSARY.md`'s *Memory service* already says ("the scheduled use of RAM by a client — video
> fetch, sound fetch, CPU access, Blitter access — granted by the memory controller").
>
> Consequences:
>
> - The **4 × 10³ – 1.5 × 10⁴ crossings/frame** headline below is **wrong as a crossing count**. It
>   is a *memory-service plus crossing* count; between 60 % and 98 % of it is Shifter `LOAD` traffic.
> - Apply the ruling and the crossing count is the **residual**: VBL, HBL/Timer B, MFP timer expiries,
>   the DMA-active signal that clocks Timer A, interrupt acknowledge, and bus-arbitration control
>   (BR / BG / BGACK). That is **order 10² – 10³ per frame, 10³ – 10⁴ per second** — two to three
>   orders of magnitude below the 10⁵ placeholder the note set out to correct.
> - The precise residual is the job of the crossing catalogue (#6), not of this document; §2's rows
>   are left intact as the record of what was derived under the earlier reading, with the `LOAD` and
>   DMA rows now labelled memory service.
> - **Nothing in §5's scheduler conclusions weakens**: the 16-cycle lookahead granularity comes from
>   the hardware, not from the counting convention, and it is unchanged. What changes is that crossings
>   were never the scheduler's main load — the memory subsystem's fetch deadlines are, and they are
>   computable in advance from counters.

The number is still needed after the move from browser/WASM to native: it sizes the scheduler (#7) and
decides how often a chip must suspend on a cross-chip event versus take the synchronous fast path. It
is no longer a hard 2 ns/event wall-clock budget (the host is now a native process, not a browser
main thread on a laptop), but the *ratio* of crossings to chip work is unchanged, and that ratio is
what the scheduler is shaped by.

**Counting definition used here** (GLOSSARY.md, "Signal crossing"): a crossing is *an output control
signal of one virtual chip that provokes a behaviour in another virtual chip*. It is **not** "any pin
toggle" and **not** "any memory access". Consequently this document keeps two numbers apart everywhere:

- **crossings** — the strict glossary count: events that must reach another chip's behaviour.
- **bus transfers** — word/byte movements on the data bus (video fetches, disk DMA, blitter reads).
  These are *memory-service deadlines*, not suspensions: they are known in advance from a counter and
  are served on the fast path. They are reported, never added to the headline.

---

## 1. Method

### 1.1 What was read, and in what order

All arithmetic is anchored to the project's own accepted constants, not to a general ST datasheet.

| Source | What was taken from it |
|---|---|
| `docs/adr/0001-one-oscillator-one-time-domain.md` | 32 MHz idealised oscillator; **1 PAL line = 2 048 ticks (64 µs)**; **1 frame = 640 000 ticks = 20.000 ms = 50.000 Hz**; CPU/bus = 8 MHz; sound DMA = 8 MHz; PSG = 2 MHz; sample rates 6 250/12 500/25 000/50 000 Hz = exactly 125/250/500/1 000 samples per frame |
| `research/async-runtime-options.md` §0, §3.2–3.4 | the 10^5/frame placeholder, the 2 ns / 20 ns bands, the 160 000 cycles/frame cross-check, the explicit instruction that the rate "must be measured, not assumed" |
| `research/deterministic-threaded-simulation.md` §2.1–2.3 | the 10 % sync budget (2 ms), the ~20 000 instructions/frame busy-frame estimate, and the prior claim that the busiest legitimate stream is **~20 video events per line** |
| `GLOSSARY.md` "Signal crossing" | the definition above; explicitly excludes internal state evolution and mere pin-value changes |
| `atari-st-emulation/02-atari-ste-hardware-overview-for-emulation.md` §6.2–6.5, §7.2–7.3, §8 | 16 pixels ⇒ 4 words (low) / 2 (medium) / 1 (high); the Shifter pipeline stages A–E (`LOAD` is a distinct control event, not a data byte); 512 cycles/line; 313 lines; MFP Timer A fed by the DMA-active signal; four sound-DMA rates; "video and sound are memory-service clients, disk is a bus-mastership client" |
| `hatari/src/video.c` | `int nCyclesPerLine = 512` (line 519), `CYCLES_PER_LINE_50HZ` used throughout for PAL |
| `hatari/src/mfp.c`, `hatari/src/dmaSnd.c` | Timer A is driven from the sound-DMA path (`MFP_InputOnChannel(pMFP, MFP_INT_TIMER_A, ...)`), confirming the DMA-active → Timer A crossing is a real one and not an emulator fiction |

### 1.2 The fixed arithmetic

```
CPU clock            8 000 000 Hz          (ADR 0001: 32 MHz / 4)
Frame                20.000 ms  = 50.000 Hz
CPU cycles / frame   8e6 × 0.020        = 160 000
PAL line             512 CPU cycles     =  64.0 µs = 2 048 ticks
Lines / frame        313                (312.5 rounded up; PAL is 312.5)
Lines / second       312.5 × 50         = 15 625
Video words / line   320 px / 16 px     = 20 groups × 4 planes = 80 words (low res)
Video LOAD events    20 per line (one per 16-pixel plane group)
```

Checked: 313 × 512 = 160 256 ≈ 160 000 CPU cycles per frame. The two derivations agree to 0.16 %,
which is the rounding of 312.5 lines to 313.

### 1.3 Measurement attempt, and why it stopped

Attempted, in this order, within the 12-minute rule:

1. `cmake -S hatari -B /opt/data/cache/scratch/hatari-build -DCMAKE_BUILD_TYPE=Release -DENABLE_SDL2=OFF`
   → **fails**: `CMake Error at CMakeLists.txt:145: SDL library not found!`. `hatari/CMakeLists.txt`
   has no headless/no-SDL option; SDL2 (or SDL3) is a hard `FATAL_ERROR`.
2. `apt-get install -y --no-install-recommends libsdl2-dev` → **exit 100**,
   `Could not open lock file /var/lib/dpkg/lock-frontend … Are you root?` — uid is 10000, no root, and
   no pre-installed SDL2 headers (`/usr/include/SDL2` absent, `pkg-config sdl2` absent).
3. Even with a build, `hatari --trace` (confirmed present: `src/options.c:541 --trace`,
   `:543 --trace-file`) needs a TOS ROM **and** something to run; there is no TOS image in the repo
   (`research/tos-rom-sourcing.md`) and no disk image. EmuTOS is freely distributable and was the
   fallback, but the build was already blocked before that became the binding constraint.

Abandoned at ~minute 10 and the remaining budget spent on the analytic path, as the ticket instructs.
**Nothing in this document is trace output. No Hatari run was performed.** Where the numbers below are
the project's own earlier estimates rather than a fresh derivation, they are marked as such.

---

## 2. Counts per workload

Per-workload tables give the **strict crossing** count, then separately the bus-transfer count that a
naive "count all memory traffic" model would add. Sums are per frame at 50 Hz and per second.

### 2.1 GEM desktop idle (low res, no DMA sound, no disk, no blitter)

| Source | Derivation | Crossings/frame | Bus transfers/frame |
|---|---|---|---|
| Video `LOAD` | 20 groups/line × 200 display lines | **4 000** | — |
| Video word fetches | 80 words/line × 200 lines | — | 16 000 |
| VBL interrupt (level 4) | 1 per frame | 1 | — |
| TOS system timer, MFP Timer C | 200 Hz ÷ 50 | 4 | — |
| IKBD serial (mouse/keyboard) | sparse, ~1–2 packets/frame | 1 | — |
| CPU accesses to chip registers | GEM idle loop touches almost no hardware registers | ~50 | (≈20 000 CPU bus cycles) |
| **Total** | | **4 056 / frame** | 36 000 (incl. CPU bus cycles) |
| | | **≈ 2.0 × 10^5 / s** | |

Idle is *not* quiet, because the video fetch machine never idles: it is 98.6 % of the strict total.
That alone kills the intuition that "idle desktop ≈ no events".

### 2.2 Floppy load (boot, or a level load streaming off disk)

Assumes DD 9-sector tracks at 250 kbit/s MFM = 31 250 bytes/s, the STE's standard read rate.

| Source | Derivation | Crossings/frame | Bus transfers/frame |
|---|---|---|---|
| Video `LOAD` | as §2.1 | **4 000** | — |
| Disk DMA word transfers | 31 250 B/s ÷ 50 = 625 B/frame ÷ 2 | 312 | 312 |
| DMA `DRQ` handshakes per byte | 625 B/frame, one DRQ each | — (byte-level bus protocol) | 625 |
| FDC → MFP IRQ (sector complete 16.4 ms, track complete 147 ms) | 20 ms / 16.4 ms ≈ 1.2 | ~2 | — |
| CPU polling `$FF8604` FDC status | a loader busy-polls; ~1 read per 10 cycles over half the frame | ~2 000 | — |
| **Total** | | **≈ 6 314 / frame** | +937 |
| | | **≈ 3.2 × 10^5 / s** | |

The polling row is the instructive one: it can be tuned from ~200 to ~8 000 crossings/frame purely by
how tight the loader's spin loop is, and *every one of those* is a read whose answer is already known.
That is the fast path, not a suspension.

Also worth noting for the scheduler: a whole track takes 147 ms ≈ 7.4 frames, so the disk stream is a
*steady* 312 crossings/frame, not a burst — but each of the 512-byte sectors arrives as a 16.4 ms
burst of byte-level DRQ handshakes around one sector-complete interrupt.

### 2.3 Raster / demo effect (full overscan, HBL sync, blitter, 50 kHz DMA sound)

| Source | Derivation | Crossings/frame | Bus transfers/frame |
|---|---|---|---|
| Video `LOAD`, full overscan | 20 groups × 313 lines | **6 260** | 25 040 words |
| HBL / MFP Timer B, 1 per line | 313 | 313 | — |
| DMA sound sample deadlines | 50 000 Hz ÷ 50 | 1 000 | 1 000 |
| DMA-active → Timer A | same rate (the signal *is* the timer input) | (counted once, above) | — |
| CPU writes to `$FF8209`/scroll/palette, ~3 per line | 313 × 3 | 939 | — |
| Blitter, 2 full-screen blits | 2 × 16 000 words | 6 (BR/BG/BGACK + done IRQ per blit) | 32 000 |
| **Total** | | **≈ 8 518 / frame** | +58 040 |
| | | **≈ 4.3 × 10^5 / s** | |

Note the shape of the blitter contribution: it is **74 % of the bus traffic and 0.07 % of the
crossings**. A scheduler sized on bus transfers would be sized by the blitter; a scheduler sized on
crossings is sized by the Shifter. The two are different machines and must not share a queue.

An aggressive demo (palette rewritten every line, 8 scroll-register writes per line, Timer B in
addition to HBL) pushes the strict total to ~1.2–1.5 × 10^4/frame; that is the realistic ceiling.

### 2.4 Timer A sound player (YM2149 digi-drums / tracker replayer)

| Source | Derivation | Crossings/frame | Bus transfers/frame |
|---|---|---|---|
| Video `LOAD` | as §2.1 | **4 000** | — |
| MFP Timer A interrupt at 8 kHz | 8 000 ÷ 50 | 160 | — |
| CPU → PSG volume write, one per tick | 160 | 160 | — |
| VBL (tracker row timing) | 1 | 1 | — |
| CPU misc I/O (PSG registers, Microwire) | ~2 per frame | 100 | — |
| **Total** | | **≈ 4 421 / frame** | 36 000 (16 000 video words + 20 000 CPU bus) |
| | | **≈ 2.2 × 10^5 / s** | |

At the more common 200 Hz Timer A (tracker row rate rather than digi rate) the timer and PSG rows
collapse to 4 each and the total is 4 105/frame — indistinguishable from idle. **The sound path is
never the driver of the crossing rate**; even at the machine's maximum 50 kHz DMA rate it is 1 000
crossings/frame, a quarter of the video stream.

### 2.5 Summary

| Workload | Crossings/frame | Crossings/second | Bus transfers/frame | Dominant source |
|---|---|---|---|---|
| GEM desktop idle | 4 056 | 2.03 × 10^5 | 36 000 | Shifter `LOAD` (98.6 %) |
| Floppy load | 6 314 | 3.16 × 10^5 | 37 000 | Shifter `LOAD` (63 %) + CPU polling (32 %) |
| Raster / demo | 8 518 | 4.26 × 10^5 | 78 000 | Shifter `LOAD` (73 %) |
| Timer A player | 4 421 | 2.21 × 10^5 | 36 000 | Shifter `LOAD` (90 %) |
| **Band** | **4 × 10^3 – 1.5 × 10^4** | **2 × 10^5 – 7.5 × 10^5** | 2 × 10^4 – 8 × 10^4 | |

---

## 3. The number

**The real rate is ~10^4 crossings per frame, not 10^5.** Concretely: **4 × 10^3 typical, 8.5 × 10^3
for a busy raster demo, a realistic ceiling of ~1.5 × 10^4** — i.e. **2 × 10^5 to 7.5 × 10^5
crossings/second**. The 10^5/frame figure in `async-runtime-options.md` §0 is a **pessimistic bound
that is wrong by roughly one order of magnitude**, and §3.4 of that document already suspected as
much ("realistic totals are closer to 10^4"); this is that suspicion, made arithmetic.

*(Read this paragraph against the correction at the top: with the DMA video and audio streams inside
the memory subsystem, this 10^4 is a count of memory-service traffic plus crossings. The crossing
count alone is 10²–10³ per frame. The arithmetic below is kept because it is what establishes that
the memory-service stream is a counter, not a wait.)*

Why it cannot be much higher, structurally:

- **It is bounded by the shifter, and the shifter is a counter.** 20 crossings/line is fixed by the
  hardware (16 pixels × 4 planes), and 313 lines is fixed by PAL. 6 260 is the hard ceiling of that
  source, and it is 60–90 % of every workload.
- **Everything else is at most one event per line.** HBL: 313. Timer B: 313. Sound at maximum: 1 000.
  To exceed 10^4 you must add CPU register traffic, and register traffic is ~3 accesses/line in even a
  register-banging demo (939).
- **To reach 10^5 you would need 320 crossings per line** — one every 1.6 CPU cycles, which is the
  observation `async-runtime-options.md` §0 itself makes and calls implausible. My derivation puts the
  real figure at **32 crossings/line**, i.e. one every 16 CPU cycles (2.05 µs).

The one way to get back to 10^5 **is a definitional error**: count *every CPU bus cycle* (40 000/frame)
or every video *word* (16 000–25 000/frame) as a crossing. Doing both gives 8 × 10^4/frame — the
original 10^5 — and it is exactly the model the glossary forbids: those are data movements on a bus,
not control signals provoking another chip. **The 10^5 number is what you get if you count bus cycles;
the 10^4 number is what you get if you count signal crossings.** The project should adopt 10^4 and say
which definition it counted (this document does: strict glossary definition).

Corollary that matters more than the headline: **60–90 % of all crossings come from one source, the
Shifter's `LOAD`, and every one of them is computable in advance** from the video counter, `LINEWID`,
and the scroll registers. They are not waits; they are deadlines. So the scheduler's suspend count is
not 10^4/frame — it is the residual after the fast path takes everything with a known answer, which
for these four workloads is the CPU polling rows plus the timer interrupts: **~200 to ~2 000 per
frame**, i.e. **10^4 to 10^5 suspensions per second**, and even that is over-generous because a poll
of an already-latched register resolves synchronously.

---

## 4. Caveats and uncertainty

Ranked by how much each could move the headline.

1. **Whether video `LOAD` is a crossing at all (±60 %).** This is by far the largest uncertainty and it
   is a *judgement*, not a measurement. I counted GSTMCU → Shifter `LOAD` as a crossing because it is
   an output control signal of one chip that provokes a latch in another, and because
   `deterministic-threaded-simulation.md` §2.3 already counts it ("20/line … the busiest legitimate
   stream"). If the model instead treats the video fetch as a memory-service slot that the Shifter
   pulls silently (i.e. a bus transfer, per §7.3 of the hardware overview), the strict totals collapse
   to **56 / 2 314 / 2 258 / 421 per frame** — a 10^2–10^3 band. **Decide this before sizing anything**;
   the difference is two orders of magnitude and it is not a matter of arithmetic.
2. **CPU polling volume (up to ±2×).** The floppy row's 2 000/frame is my estimate of a spin loop's
   rate, not a count. Real loaders range from interrupt-driven (~50) to tight-poll (~8 000).
3. **Overscan extent in demos (±25 %).** I used the full 313 lines. Many raster effects stop the
   Shifter for part of the frame or use 224/230-byte lines, which changes both the group count and the
   active-line count.
4. **No measurement at all.** Every number here is derived from constants; the 313-vs-312.5 line
   rounding, the 16-pixels-per-group granularity, and the assumption that one `LOAD` serves one group
   are all unverified against a running emulator. A trace of `TRACE_VIDEO_HBL` + `TRACE_MFP` on EmuTOS
   for 100 frames would settle rows 1 and 3 in minutes — that is the follow-up, and it needs an SDL2
   toolchain and a TOS/EmuTOS image, neither present here.
5. **Bus-transfer figures are lower bounds.** They count ideal word transfers; contended cycles,
   refresh, and the blitter's halts add more without adding crossings.

**The single biggest uncertainty: whether the video `LOAD` stream counts as crossings.** It is 60–90 %
of every total, and it is a modelling decision, not a measurable quantity.

---

## 5. What it implies for the scheduler (#7)

1. **Size for 10^4 crossings/frame, not 10^5 — but do not budget for 10^4 either.** The suspend budget
   at 10^4/frame is **20 ns per suspend for 1 % of the frame, 200 ns for 10 %** (`0.01 × 20 ms /
   10^4 = 20 ns`). That is 10× the old 2 ns/20 ns pair. On a native build, 20 ns is enough for a
   function call, an atomic, and a cache-miss-free state-machine resume; 200 ns is enough for a real
   task switch (Rust ~200 ns, C# `Task.Yield` ~53 ns). **The scheduler no longer needs a 2 ns
   mechanism to survive.** This is the answer to "how often must a chip suspend": rarely enough that
   suspension cost is a design taste, not the binding constraint.
2. **The binding constraint moved from cost-per-suspend to lookahead granularity.** At 32 crossings per
   line, the busiest chip is re-evaluated every **512 / 32 = 16 CPU cycles (2.05 µs)**. The scheduler's
   unit is therefore a ~16-cycle quantum with forced synchronisation at crossings — which is exactly
   the "quantum of 128–512 ticks, 16–4 per scanline" already concluded in
   `deterministic-threaded-simulation.md` §2.1. Two independent derivations agreeing is the strongest
   evidence in this document.
3. **One chip, one stream: the Shifter dominates and it is fully predictable.** Give the Shifter its own
   timeline (it can compute all 20 `LOAD` instants for a line at the line boundary) and it never needs
   to suspend: it is a lookahead device, not a waiting device. Same for sound DMA. The chips that
   actually *block* are the CPU (on bus arbitration), the FDC (on rotational latency), and the MFP (on
   timer expiry) — a handful of events per frame, i.e. the "marginal, ≤ 10^4/s" band, which is where
   the expensive path belongs.
4. **Keep two queues, not one.** Crossings (≈ 10^4/frame, control) and bus transfers (≈ 8 × 10^4/frame,
   data) differ by 8× in rate and by 4 orders of magnitude in blitter content. Merging them makes the
   blitter's 32 000 word moves compete with the Shifter's 20 `LOAD`s per line, and the blitter would win
   the scheduler — the wrong chip to optimise for.
5. **Suspension rate to design against: 10^4–10^5/s** (the residual after the fast path), not 5 × 10^6/s.
   At 10^5/s and 200 ns that is 2 % of a frame; at 10^4/s and 1 µs it is 0.05 %. **The synchronous fast
   path is still the design, but now because it is simpler, not because the budget forbids otherwise.**
   If the model later decides video `LOAD` is *not* a crossing (caveat 1), the rate falls to 10^2–10^3
   per frame and item 1 relaxes by another order of magnitude — but item 2, the 16-cycle lookahead
   granularity, is unchanged, because it comes from the hardware, not from the counting convention.
