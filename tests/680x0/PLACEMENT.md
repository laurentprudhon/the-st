# Where the internal clocks sit — CLK against MAME's microcoded core

`LENGTH.md` measured *how many* clocks an instruction spends off the bus. This page is about *where*
inside the instruction they sit, which is the half choice 3 needs: an internal delay is only faithful if
it happens at the right point of the computation, because that is when the next bus cycle starts.

Two independent T3 models answer it, and they agree:

| corpus | what it is | where |
|---|---|---|
| **CLK** (Thomas Harte) | the vendored oracle this core is graded against | `tests/680x0/` (124 files, 1 000 060 records) |
| **MAME microcoded** (`SingleStepTests/m68000`) | a second model, generated from MAME's microcoded 68000 — its microcode has explicit idle cycles | `v1/*.json.bin`, 30 files × 2 500 decoded with its own `decode.py` |

Each record's `transactions` is an ordered cycle list, so a record reduces to a *skeleton*: the sequence
of `r`/`w`/`t` with each run of idle clocks in its place (`r n2 r r w`). Compare the skeletons and the
totals shape by shape (`scripts/corpus-placement.py`):

```
families compared 30   shapes compared 423
  identical (total AND skeleton) 375   (88.7 %)
  total differs 48   skeleton differs 48
  DIVU 0/11 · MOVEM.l 0/14 · MOVEM.w 2/14 · MULU 0/11
```

**The placement agrees on everything except `MUL`/`DIV` and `MOVEM`**, and for those the disagreement is
in the *count*, never in the shape:

- `MULU`/`MULS`: both put a single trailing run after the operand read; MAME charges 2 more whenever the
  multiplier comes from a register or a simple EA, and agrees exactly for `#imm` and `(d16,An)`.
- `DIVU`/`DIVS`: both put the divide's clocks **before the closing fetch** (`r n82 r`), and both have the
  same 6-clock overflow exit; MAME's long path runs 2–8 clocks longer.
- `MOVEM`: MAME's microcode stores a register a cycle at a time with a different count (16–22 words where
  CLK has 8–14); the shape differs too. This is the same family whose exception frame the two corpora
  already contest (`the-st-68000-core` §5, `FINDINGS.md`), so it is named, not averaged.

**Where they disagree the core follows MAME** — the owner's ruling of 2026-11-19, and it reverses the
choice this page was first written with: MAME's microcoded core is the source of truth for an instruction's
clock count and placement **and for its final state**. The state gate moves to it because the coverage
condition the owner set is met: MAME's corpus is **127 files against CLK's 124** (a strict superset — it adds
`ILLEGAL_LINEA`, `ILLEGAL_LINEF`, `STOP`), and by instruction **form** (the opcode with its mode/register
fields masked) the two cover **53 of 53, identically**, over the 30 files both corpora have decoded. What
MAME has less of is *vectors*: 2 500 records a file against CLK's 8 065, and 13 277 distinct opcodes against
CLK's 17 884 on those 30 files (5 078 of CLK's are not in MAME, 471 of MAME's not in CLK, concentrated in
`Bcc` −1 760, `MOVE.w` −1 262, `MOVE.l` −1 235, `ADD`/`SUB` ≈ −200 each). So the *instruction set* is covered
as fully and a little more; the *data samples per instruction* are 3.1 times thinner. CLK stays vendored as
the denser second suite, reported beside the gate rather than gating. The table above keeps both columns:
CLK's numbers are quoted beside MAME's so the disagreement stays visible rather than silently resolved, and
`TAS` and `TRAPV` are excluded from any "100 %" claim because MAME's own README says its TAS read-modify-write
timing and its TRAPV tests are not trustworthy.

## The four charge points (what the traces say, and what the core now does)

| where | clocks | evidence (both corpora) |
|---|---|---|
| **the effective address**, once per instruction, before the access it serves | 2 for `-(An)`, 2 for `(d8,An,Xn)` / `(d8,PC,Xn)` | `CLR.w -(A0)`: `n2 r r w`; `CLR.w (d16,A0)`: `r r r w` (no run) |
| **the family's own work**, before the instruction's closing fetch | shifts `2 + 2 × count` (`4 + 2 × count` long); `MUL` 34 + 2 per magnitude bit; `DIVU` 76…128, `DIVS` 122…150, short exits 6 / 12; `CHK` 6; `Scc Dn` 2; `.l` ALU 2/4 | `ASL.b Q,D0`: `r n16`; `CHK`: `r n6`; `DIVU`: `r n82 r` |
| **a control transfer**, before its refill fetches | `Bcc` 4 not taken / 2 taken; `DBcc` 4 / 2; `JMP`/`JSR` 2 (indexed 6, 2 of it the EA's); `BSR` 2 | `Bcc` not taken: `n4 r`; taken: `n2 r r`; `JMP (d16,A0)`: `n2 r r` |
| **an exception entry** | 4 before the frame's writes, 2 between the handler's two fetches | every entry: `n4` … frame … `n2` … |

Two facts fall out that a manual reader gets wrong, and both are in the traces of both corpora:

- **a not-taken branch's clocks come first** (`n4 r`), before the fetch — the opposite of the "internal
  work trails" intuition that holds for `CHK`, the shifts and the divide;
- **an EA's 2 clocks are charged once per instruction, not once per operand**: `ADDX -(An),-(An)` and
  `MOVE.w <ea>,-(An)` show a single `n2` for two predecrements.

## The core, as built

`src/TheST.Machine/Cpu/M68000.Timing.cs` is the one table: `InstructionOwnClocks(op)` reads the family,
size and EA mode off the encoding and returns the family's share, `LeadsOwnClocks(op)` says whether it is
charged before the instruction's first access or before its closing fetch, and the effective address
charges its own 2 in `EaAddressAsync`. The 67 ad-hoc `Clocks(n)` calls that shipped with `#34` — guesses,
never measured — are gone.

**It is a wait, not an `Advance`, and that is the load-bearing part.** The first build moved the CPU's own
clock forward with `Advance`, which took it past machine time: the beam and the timers fell behind, the
boot lost its counts, and 300 frames never reached the desktop. A real internal delay suspends the CPU and
lets the machine advance in step, so `ClocksAsync` arms a date on the answer wire (nothing is pending at
that moment) and resumes at exactly `Clock + n × 4` ticks.

**The audit** — `cpu-tests` now prints `exact-clock tests`: records where `4 × our bus cycles + our internal
clocks == the corpus's length`:

| tree | exact-clock tests (whole corpus) |
|---|---|
| `main` before this work (`#34`'s guesses) | 163 / 2 976 on the sample — 5.5 %, and **0 called it the whole corpus** |
| the table, charged with `Advance` | 674 653 / 1 000 060 (67.5 %) |
| the table, charged as a **wait** | **799 562 / 1 000 060 (80.0 %)** |

Corpus state is unchanged throughout: **1 000 057 / 1 000 060**, sample 2 976 / 2 976 — the charges move
the clock, never a register.

**And it is cheaper, not dearer** — six adjacent Release pairs of the 800-frame boot, alternating order:
`main` **9.13 ms** a frame against **8.35** here (**−0.78, 6/6 unanimous**), `alloc_mb_per_frame`
**8.15 → 6.92**. The denominator has to be quoted with it: `frame_n` 747 → 756 and the desktop frame
598 → **607**, because the guest now spends those clocks — `instructions` and `bus_cycles` are each
**−3.9 %** in the same 20 ms frame, and the video and timer counts are unchanged, which is what says the
machine is the same machine. `pops_per_frame` falls 59 397.0 → **35 811.6** (−40 %): the CPU's own dates
coalesce the engine's instants, and that is where the allocation goes.

## What is still outside

The remaining 20 % is four named classes, in descending volume:

1. **the immediate-to-`CCR`/`SR` forms** (48 390 records, −12 clocks): our core performs **3 fewer bus
   cycles** than the reference — a *fetch* difference (the two words come out of the queue where CLK
   fetches them), not an internal-clock one. `#70`/`#74`'s territory.
2. **records whose instruction faults** (194 548, 19.5 %): the entry's own bus cycles and its 4 + 2 are
   charged, but the frame's cycle count is `#72`'s contested ground.
3. **`TAS`** (6 828): CLK's `t` is 10 clocks and 1 cycle where the core performs a read and a write.
4. **the open bit-op datum** (`BCHG`/`BCLR`/`BSET` on a data register, 3 873): 6 or 8 clocks with nothing
   in the record to separate them (`LENGTH.md` §6) — the majority is charged.

So the column is now a **80 % verdict**, not a coincidence, and each remaining class is a ticket of its own
rather than a tuning knob.

## The MAME gate: how it runs, and where the core stands (2026-11-19)

The owner's ruling moved the **state** gate to MAME's microcoded corpus as well (`PLACEMENT.md` §the
coverage condition, `the-st-68000-core` §5): `cpu-tests --corpus mame` grades it, `--corpus clk` reports
the denser suite beside it, and the two numbers are never subtracted from one another.

```
scripts/fetch-m68000-tests.sh                 # 127 files, 138 MB of .json.bin + the decoded JSON (git-ignored)
validation-harness cpu-tests --corpus mame --quiet --out report.txt --failures failures.tsv --clocks clocks.tsv
validation-harness cpu-tests --corpus clk --dir tests/680x0/full
```

Its conventions live in two places in `CpuTestsCommand`: `pc` is `m_au` (the next prefetch address, so
the opcode sits at `pc − 4`) and a byte's lane is `UDS && !LDS ? data >> 8 : data & 0xFF`. Its `ram` map
is already 16-bit-piece-decoded into bytes by upstream's own `decode.py`, `re`/`we` are the aborted
address-error cycles (4 clocks in `length`, no bus cycle performed) and `idle` is the internal run.

| measure | MAME (the gate) | CLK (reported beside it) |
|---|---|---|
| records | 317 500 (127 files) | 1 000 060 (124 files) |
| state, first measurement | **256 897 / 317 500 (80.9 %)** | 983 625 / 1 000 060 (98.36 %) |
| `exact-clock` (`4 × bus + internal == length`) | **205 038 (64.6 %)** | 799 562 (80.0 %) |
| `exact-cycle` (bus alone) | 0 | 0 |

**The CLK figure is 16 432 below the 1 000 057 the branch started with, and that is the ruling's cost, not
a regression**: the two corpora disagree about the frame's saved program counter, and every record the
core loses on one it wins on the other. Report them side by side and never as one number.

### What the 60 603 MAME failures are, class by class (measured)

| class | records | what it needs |
|---|---|---|
| **group-0 frame's saved `pc`** | 30 522 | MAME's *pipeline depth* — see below; this is the one that needs a decision |
| **group-1/2 frame's saved `pc`** | 16 276 | the same question one frame smaller: MAME saves the *instruction's* address for `ANDI`/`ORI`/`EORI`-to-`SR`, `MOVE`-to/from-`SR`, `MOVE`-to/from-`USP`, `RESET` and the illegal lines, but **not** for `TRAP`/`TRAPV`/`CHK`/`RTE` (and the boot depends on `TRAP`'s: see the trap below) |
| `sr` (flags) | 3 026 | real flag differences: shifts (`X`/`C` on `ASR`/`ASL`/`LSR`/`LSL`), `DIVS`/`DIVU` (1 459), `MOVE.l` |
| `a0`–`a6` | 4 842 | a **long** access at an odd address steps the register by **2** in MAME (we step 4, CLK's rule): `ADDX.l`/`SUBX.l` 1 442, `CMP.l`/`MOVE.l`/`CLR.l` the rest |
| `ram`, not in a frame | 2 159 | `LINK`'s and `UNLINK`'s stack writes (`LINK A7,#` differs by 4 bytes) |
| `pc` | 1 230 | `STOP` only: MAME leaves the counter at the **`STOP` opcode's** address, we at the word after its immediate |
| `usp`/`ssp` | 1 115 | `UNLINK`'s popped `A7` |
| `d0`–`d7` | 687 | `DBcc`'s counter (off by one: the wrong side of the condition's true/false split) |
| other frames | 801 | frame sizes the classifier does not recognise (an 18-byte frame: a fault inside a frame write) |

### The finding: a frame's saved `pc` is the pipeline's depth, and MAME prefetches during the idle clocks

Over the 74 201 records that carry a frame, the saved `pc` is **not** a function of anything the core holds
today. Tested per record against our own quantities (`--clocks` prints them: `frame_pc`, `fetch_ptr`,
`pc_counter`, `ir_addr`, plus whether the instruction had fetched a word of its own):

| candidate | records where it is the corpus's value |
|---|---|
| `_fetchPointer − 2` (the last word fetched) | 23 058 |
| `_pc` (past the last consumed word) | 20 173 |
| `_irAddress` (`_pc − 2`, what the core saves) | 17 440 |
| `_pc + 2` | 11 947 |
| `_fetchPointer` | 11 726 |

No candidate wins, and the *same opcode shape* takes different values record to record (`MOVE.w` alone
splits 50/50). The rule the corpus does obey is arithmetic on **how many words the pipeline has fetched**:

```
saved pc = opcode + 2 + 2 × (prefetch words the microcode had issued before the fault)
```

which is `_fetchPointer − 2` **if** the core issued the same reads, at the same points. It does not: MAME
issues its prefetch **inside the instruction's idle clocks** (it has an explicit `prefetch` micro-op
between the idle runs), while this core fills the queue lazily and fetches at the closing. That single
difference explains every value in the table above — e.g. `CLR.w (An)` has no idle before its access and
saves `opcode + 2` (no prefetch), `CLR.w -(An)` has the EA's `n 2` and saves `opcode + 4` (one prefetch),
`MOVEM` saves `opcode + 6`/`+ 8`/`+ 10` (two to four), `MOVE Dn,(An)` one, `MOVE (An),Dn` none.

So 100 % on MAME's state needs one of two things, and it is the owner's call which:

1. **model the prefetch as a bus read inside the idle clocks** — one rule, and it is the hardware's
   ("the CPU spends idle clocks prefetching the next word"), but it moves the read placement `#70`/`#74`
   landed on CLK's schedule and changes the bus's ordering inside an instruction (frames, folds, a
   re-bless); or
2. **a per-form depth table** — measured `saved = opcode + 2 + 2k` per (family, EA shape, fault kind);
   96.0 % of the group-0 frame records sit in shapes where that depth is a single value, so it would take
   the class from 30 522 to about 2 000 — at the cost of a table the core would carry, which is the kind
   of mapping the design prefers not to have.

Until that is decided the core keeps CLK's queue rule for the group-0 frame (so the CLK figure stays
where it is) and MAME's figure is reported as it stands.

### The trap a group-1/2 frame rule set: it greens 11 114 records and hangs the boot

Setting the group-1/2 frame's saved `pc` to the **instruction's own address** — which is what MAME's
illegal-line, privilege, `ANDI`/`ORI`/`EORI`-to-`SR`, `MOVE`-to/from-`USP`, `MOVE`-to-`SR` and `RESET`
records measure, and a single rule rather than a table — raised the state verdict **256 983 → 268 097
(+11 114)** and the boot then **never reached the desktop** (`frames_to_desktop=never`, `_frclock` 796,
`pops_per_frame` 5 223 against the tree's 35 811; the 800-frame boot's 4 108 457 instructions collapse to
an idle loop). Cause: EmuTOS runs on `TRAP`, and `TRAP`'s frame is *not* the instruction's address — an
`RTE` through the changed frame returns to the `TRAP` itself. **So a group-1/2 frame rule must be taken per
family and the boot run before it lands**; on MAME's corpus alone it looks like a pure win, which is
exactly why the gate is not the whole verdict. The change was reverted; the class is 16 276 records of
measured convention, not of arithmetic.

### The clock gap on MAME's numbers (64.6 %)

Same shape as CLK's, different constants, and the four charge points are not in question — the *counts* are.
The largest classes, as `our total − length` summed per file: the immediate-to-`CCR` forms **−12 clocks a
record** (`ANDI`/`ORI`/`EORI` to `CCR`, 7 500 records: our core takes both words from the queue where MAME
fetches them, so three bus cycles are missing — `#70`/`#74`'s ground), `MOVEtoCCR` **−8** (2 500),
`DIVS` **−25 a record** (54 000 clocks over 2 109), `CHK` −6, `CLR`/`Scc` −4…−6, `MULS` −4, `ADDA.w`/
`SUBA.w` −4, and the faulting records' **missing aborted cycle** (`re`/`we` is 4 clocks of `length` and no
bus cycle). `TAS` stays outside any 100 % claim, as the ruling says (MAME disowns its own RMW timing).

