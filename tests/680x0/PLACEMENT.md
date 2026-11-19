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
choice this page was first written with. MAME's microcoded core is the source of truth for an instruction's
clock count and placement; the vendored CLK corpus stays the oracle for an instruction's *final state*
(1 000 057 / 1 000 060), which is what the harness actually gates. So the table above keeps both columns:
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
