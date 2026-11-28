---
name: the-st-68000-core
description: "Use when working the-st's 68000 core and its corpus."
version: 1.0.0
author: Hermes Agent
license: MIT
metadata:
  hermes:
    tags: [68000, corpus, emulator, atari-st, fidelity, debugging]
    related_skills: [the-st-perf-measurement, systematic-debugging, hardware-reference-docs]
---

# The 68000 core and its corpus (`the-st`)

**Two oracles, and one of them is the truth.** Thomas Harte's CLK single-step set is vendored under
`tests/680x0/` and is the denser suite — 124 files × 8 065 records = 1 000 060. MAME's microcoded core
(`SingleStepTests/m68000`) is the **source of truth and the gate**, for clocks *and* for state: the owner's
standing ruling. Its corpus is 127 files × 2 500 = 317 500, and it may gate because it covers the
instruction set at least as fully — **127 files against 124** (a strict superset: it adds
`ILLEGAL_LINEA`, `ILLEGAL_LINEF`, `STOP`) and the **same 53 of 53 instruction forms** on the files both
carry — while carrying 3.1× fewer data vectors a file. So grade against MAME, report CLK beside it, and
never subtract one pass rate from the other: a MAME-true core *loses* CLK records on the contested frames
and on the `MUL`/`DIV`/`MOVEM` counts, by construction.

The corpus is the only witness for two thirds of the instruction set — the boot executes maybe a third of
it and does not care why a flag is wrong as long as a branch lands.

## When to Use

- A ticket about the core's decode, effective addresses, flags, exception frames or a failing opcode family.
- Auditing the core against `research/m68000-silicon-implementation.md` ("is this code the silicon?"): start from
  `references/silicon-shape.md` §7, the measured gap list — do not re-run a fourteen-chapter audit.
- Before quoting any "the core passes N %" figure, and before picking a family to fix.
- Not for frame time (`the-st-perf-measurement`) and not for chip behaviour outside the core.

## 1. First: does the oracle run at all

```bash
cd /opt/data/the-st && export DOTNET_ROOT=/opt/data/.dotnet PATH=/opt/data/.dotnet:$PATH \
       DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
tools/validation-harness/bin/Debug/net11.0/validation-harness cpu-tests --file ADDX.b --out /tmp/one.txt
cat /tmp/one.txt          # "cpu-tests: 0/24 passed, 24 failed" and why
```

- **A `harness:` exception is the instrument, not the core.** Any failure line beginning `harness:` — typically
  `OutOfMemoryException`, or an assertion that a write is dated before the batch being drained at
  `9223372036854775807` — means the test rig, not the instruction. When it is back, **bisect the engine, do not
  debug families**: a worktree + `git bisect run` with `cpu-tests --file ADD.b` (0.05 s) as the predicate finds
  such a rig bug in steps, not hours.
- `--quiet` suppresses the summary on stdout, and `--out FILE` gives the per-file verdict line. `--failures
  FILE` writes up to 20 detail lines per opcode file, **and it takes a path** — a bare `--failures` is
  rejected as an unknown option and prints the usage, which reads like the flag does not exist. **Both
  flags take a file path, never a directory**: `--out <dir>` throws `UnauthorizedAccessException` out of
  `File.WriteAllText` after the whole run, and a directory-wide `--out` is not an aggregate report — do
  not expect one; read the one per-file table it writes.
- A file that takes minutes rather than seconds means every test is throwing: fix the rig first, then measure.
- `--dir tests/680x0/full` must skip `68000.official.json` (upstream's decode map is an object, not a test array);
  the scan keeps only files whose JSON opens with `[`.

## 2. Sample versus whole corpus, and the file names

| path | what it is |
|---|---|
| `tests/680x0/v1/` | vendored, 124 files × 24 tests (2 976), the default `--dir` — the loop's fast check |
| `tests/680x0/full/` | the whole **1 000 060**, git-ignored: `scripts/fetch-680x0-tests.sh --full` fetches it from the pinned upstream commit (~194 MB, **25 s to run** — a rig taking 5–6 min is §1's bug) |
| `tests/680x0/FINDINGS.md` | rows *symptom → cause → fix*, in the order they were found (1–55), plus the three conventions |

**Quote the gate, not CLK.** The number that heads a report is MAME's (`--corpus mame`, closed at
317 500 / 317 500); CLK's (below) is the denser suite *beside* it. Do not present either as "the core
passes N %".

**Where the core stands on CLK is NOT a near-100 % figure, and raising it is not work.** CLK read
1 000 057 / 1 000 060 before the gate moved to MAME; since the frame conventions follow MAME it reads
**863 265 / 1 000 060 (58 of 124 files green; exact-clock 819 372)**, because a MAME-true core *loses*
CLK records by construction (`ASR`'s width rule, the group-0/1/2 frames, the `MUL`/`DIV`/`MOVEM`
counts). CLK's red records are not a work queue — read the ceiling table in §5 before proposing
decode work from them. The 3 red records of the pre-gate tree were
upstream's own self-contradictions (`ASL.b` 2 — `ASL.b #2` on `$CDFB7FBE` documented as `$2E5E4304`,
which no byte operation can produce; `DIVU` 1): documented, not chased. Every passing record still
agrees with the reference's own fetch placement — `--reads` is clean, see §4.

- **File names are the corpus's, not the ticket's**: `ADDX.b.json.gz`, `DIVU.json.gz`, `MOVEM.l.json.gz`. A ticket
  that says `DIVU.w` names a file the harness cannot find; `--file` takes the corpus's mnemonic.
- The sample tracks the whole set to within about a point, which is what it is for. **A claim about the core must be
  measured with `--dir tests/680x0/full`.**

## 3. The loop

One family at a time, one cause at a time: run `--file <name> --failures --show 5`, read the first differing field in
`FINDINGS.md`'s vocabulary (`d3`, `sr`, `pc`, `ram`, `cycles`), fix, re-run the same file, then the sample, then the
full set. Rows in `FINDINGS.md` are ordered deliberately: re-triage in that order.

- **`length` (CPU clocks) is a graded column now, not a diagnostic** — and the grading is against MAME.
  Read `tests/680x0/LENGTH.md` and run `scripts/corpus-length-census.py --dir tests/680x0/full --table` (20 s,
  stdlib only) instead of re-deriving it. The residue `length − 4 × bus` is the reference's own `n`/`t`
  clocks on all 1 000 060 records; it is a function of the instruction's shape (family, size, operand EA
  modes) on 73.4 % of the plain class and a counter on a value the core holds for the other nineteen
  families (shifts `base + 2 × count` exactly; `MUL`/`DIV` by operand bits; `Bcc`/`DBcc`/`Scc` by the
  branch). Before tabulating anything, split off the **exception entries** (19.5 %: a fault's `length`
  carries the group-0 frame, the vector and the handler's first fetches — a supervisor read, `fc 5`, below
  `$40` is the detector) and include `t` transactions in the sum (`TAS` has no `n`). Key on the corpus's
  own operand text, never on a hand-rolled opcode decoder — two were written and both were wrong (a shift's
  bit 5, a bit-op's bits 7-6 being the operation, not the size). One datum is open: a data-register
  `BCHG`/`BCLR`/`BSET` pays 6 or 8 clocks on the same shape with nothing in the record separating them —
  settle it from CLK's source (row 58's method), never with a clause (row 57).
- **The residue is now charged to the CPU's clock** (the machine's instruction timing is the hardware's, and a
  timing change is visible to software only as *when the next bus cycle starts*). `Cpu/M68000.Timing.cs` is
  the one table — `InstructionOwnClocks(op)` reads family, size and EA mode off the encoding,
  `LeadsOwnClocks(op)` says whether the charge comes before the instruction's first access or before its
  closing fetch — plus four charge points: the EA's 2 clocks once per instruction for `-(An)`/indexed, the
  family's own work before the closing fetch, a control transfer's *leading* charge, an exception entry's
  4 + 2. `cpu-tests`' **`exact-clock tests`** (`4 × our bus cycles + our internal clocks == length`) is the
  verdict on that table and `--clocks FILE` dumps one line per record for the per-family deltas;
  `tests/680x0/PLACEMENT.md` holds the two-corpus placement, `references/internal-clock-placement.md` the
  charge points, their evidence and what still resists.
- **Test state is per test.** If a failure smells like a leak from the previous case, go back to §1.
- **Two documents can disagree about the counts** (a ticket's failing-family table against `FINDINGS.md`'s "what is
  still open") because they were measured on different trees. Re-baseline, and put the new table in the ticket
  comment that starts the work — the ticket body is written against the tree of its filing day.

## 4. Read this before touching frames, `MOVEM`, `CHK`, or anything that faults

**The group-0 frame's saved PC is now the queue's, and the queue is built** (`#70`, 2026-11-18). The core
holds `IRD`/`IR`/`IRC`, `_at` (the address temporary — the field that used to be `_irAddress`) is the address of the word
in `IR`, and `EnterExceptionAsync` writes `returnPc = _at` for vector 2/3 with no arithmetic (a *fetch* fault subtracts 4, the word before the
one that failed). The measured rule that survives the build is: **saved = the address of the last program
read − 2**, i.e. `pc + 2 × (the reads the instruction had already issued)`. So a faulting record's frame
is decided by **how many fetches the instruction got out before the fault**, and the corpus pins that
placement exactly:

- **one read per consumed word** — a completed instruction fetches its own word count, at consecutive
  addresses from `pc + 4` (watch it with `cpu-tests --trace N`, which prints the core's cycle list beside
the record's `transactions`);
- **the closing read is the instruction's trailing fetch**, and the fault cancels it unless the class puts
  it before the faulting write — which the corpus pins as *a plain `MOVE` into `-(An)` only* (a `(An)`,
  `(An)+` or stream-built destination takes it at the closing, or loses it);
- **an address the instruction stream built is validated where it is built**: an odd
  `(d16,An)`/`(d8,An,Xn)`/absolute destination faults *before* the trailing fetch, while a `-(An)` one
  faults at the cycle, after it. That single rule is worth four corpus families;
- a jump, a branch, an `RTE`/`RTR` and an exception entry **refill at their target** — two reads, with the
  `n 2` between them that every handler entry's trace shows — and they cost 8 clocks more than the
  fetch-on-demand core charged, so the machine's frames and both folds move.

**The residue, so nobody re-derives it, and it is now CLOSED** (`#74`, `FINDINGS.md` row 58). The 49
records (`MOVE.w` 23, `MOVE.l` 26) were a `MOVE` into an odd `(xxx).l` destination, and the answer is the
oracle's own per-opcode state, not a rule: CLK gives a plain `MOVE`'s absolute-long destination a state of
its own (`MOVE_bw_AbsoluteLong` / `MOVE_l_AbsoluteLong`, beside the generic
`CalcEffectiveAddress(AbsoluteLong)`), with two halves —

- **the pair for one read**: the pop that takes the address's high word fetches the low one, and the low
  word is read out of the queue rather than popped, so the pair costs **one** read where the generic EA
  machine spends two. That is why `CLR.w`/`ADD.w`/`AND.w` into `(xxx).l` were already green while `MOVE`
  was not;
- **a prefetch-first class**: with the queue now a word short, the word *after* the address is fetched
  **before** the store iff the source cost no bus cycle of its own — a data register, an address register
  or an immediate. An odd destination's fault therefore keeps that read for `33fc [MOVE.w #,(xxx).l]`
  (3 reads) and cancels it for `33ea [MOVE.w (d16,A2),(xxx).l]` (2 reads).

In the core: `FetchMoveAbsoluteLongAsync` (one pop, `_au += 2`, `_queueShort`),
`CloseAfterLongDestinationAsync` (the two words the reference's two closing prefetches read) and
`MoveSourceNeedsNoBusCycle` (the class, read off the opcode's own source field). **9 cases out of 10, the
right shape is the reference source's own state name**: clone CLK sparse (`git clone --depth 1 --filter=blob:none --sparse`, `git sparse-checkout set Processors/68000`) and read
`Implementation/68000Implementation.hpp` — its `BeginStateMode(MOVE_bw, AbsoluteLong)` block is the
answer, and a `--reads` table only *names* the phenomenon it implements.

**And the clause that "fixes" it was measured as a no — do not rebuild it.** "A long destination address
defers its last word's refill until the address is validated" is the natural Level-1 statement and it is
wrong: it **fixes 23 records and breaks 278** (999 753 / 1 000 060, `FINDINGS.md` row 57). Two records of
the same opcode, same word count and same odd destination pay a *different* number of reads, so no uniform
clause can hold. Details, the measured table and the re-derivation recipe are in
`references/prefetch-read-placement.md`.

`FINDINGS.md`'s **three things the corpus fixes that the manuals leave to the implementation** is the reading list:

1. the group-0 frame's saved program counter is one word behind a prefetching core (`pc − 2` here, `pc − 4` for the
   prefetch fault's frame);
2. the frame's first word carries the implementation's access class (the corpus records "read" for `CLR`, a pure
   write);
3. extension words come out of the two-word prefetch queue, not off the bus.

Those are the **design** ticket (the real prefetch queue), not decode debugging. A fix that fights them passes ~90 %
of a file and fails the rest, and looks like a decode bug forever.

## 5. Witnesses, in order of usefulness

0. **each record's own bus trace** — every corpus test carries `transactions`: `["n", cycles]` for internal
   work and `["r"|"w", cycles, functionCode, address, ".w", data]` for a bus cycle. `cpu-tests --trace N`
   prints the core's own cycle list beside record N's (Debug builds only: the observer is `#if DEBUG`).
   **For a whole family use `cpu-tests --reads FILE`**: one tab-separated line per record — name, the
   reference's instruction-stream fetch addresses, ours, the verdict — which is how a placement question is
   answered at scale instead of one record at a time. Diff the two lists in python, grouped by (source EA,
   destination parity, read count), and the family's rule (or the absence of one) falls out of the table.
   Function code 6 is a program fetch, 5 supervisor data — and the core's fetches now assert 6 (2 in user
   mode), which the fetch-on-demand core never did. Five facts the traces settle: a completed instruction
   fetches one word per word at `pc + 4`, `pc + 6`, …; a handler entry is `r fc6 target`, `n 2`,
   `r fc6 target + 2`; an odd absolute destination cancels the trailing fetch; a store into an odd
   `-(An)` keeps it; and the **number** of reads in that instruction differs by its opcode's script, not by
   the EA shape (`#74`: a plain `MOVE` into `(xxx).l` with a memory source reads the pair with one fetch and
   stores, one with a register/immediate source reads one more and *then* stores).
   **And the queue is stated twice per record**: `initial.prefetch` / `final.prefetch` are the model's
   prefetch register pair before and after the instruction (`final.prefetch[0]` = the word at the new PC,
   `final.prefetch[1]` = the one word the instruction read), so a queue implementation can be checked
   directly against both, with the trace explaining how it got from one to the other.
0b. **the second corpus, `SingleStepTests/m68000`** (MAME's microcoded core, MIT, 127 files × 2 500 = 317 500,
   138 MB, `decode.py` converts `.json.bin`). Its conventions differ: `pc` is `m_au` = the **next prefetch
   address**, so the opcode is at `pc − 4`; `ram` is 16-bit pieces; transactions carry 8 fields including
   `t` (TAS) and `re`/`we` (the aborted address-error cycle, AS not asserted) plus UDS/LDS. **It disagrees
   with the CLK corpus in this core's own family**: for `MOVEM.l #, (An)` faulting on the store, CLK stacks
   `opcode + 2`, MAME stacks `opcode + 6`. Neither corpus is hardware-captured — both are T3. The only T2
   source is WinUAE's `cputest` core (corrected on real Amigas; tabulated per family in
   `alewman/m68000-python` `docs/referees.md`), the only T1 flamewing's BCD tables. So for a frame
   convention, say which corpus and which tier decided it — never "the corpus says".
   **How to run it**: `scripts/fetch-m68000-tests.sh` vendors it into `tests/680x0/mame/` (git-ignored,
   127 × 2 500, MIT, `.json.bin` decoded by upstream's own `decode.py`), and
   `cpu-tests --corpus mame [--clocks FILE] [--failures FILE]` grades it (10 s); `--corpus clk --dir
   tests/680x0/full` is the reported second suite (25 s). The conventions are handled there: `pc` is
   `m_au` so the opcode is at `pc − 4`, and `ram` arrives byte-split. **Where it stands (2026-11-19, after
   **Where it stood on the way there is history**: the port climbed 93.4 % → 96.5 % → 98.2 % → 99.02 % over
   the rows FINDINGS records (rows 68–77), each row a family batch with its own fold pair; the standing at any
   of them is that row's, never this tree's. What matters here is that the corpus is closed on both columns
   (`317 500 / 317 500`) and that a red file's *clock* column, not its state column, is where a family's
   missing access hides — see the bullet list below, which is history from 2026-11-19 except where it says
   otherwise.
   The fold pair #79 produced — `run.digest` `554205a3…`, `run.stream` `e9f72d4b…` (Desktop `boot --frames
   800`, desktop at frame 612, G8 8.98–9.06 ms at n 761) — is **`main`'s blessed pair since 2026-11-19**;
   the one before it was #78's `afaf347c…` / `81f34a11…`. Never subtract
   the two — the frame conventions are contested, so a MAME-true core *loses* CLK records by construction.
   `PLACEMENT.md` §"the MAME gate" holds the class-by-class table of what the 60 603 failures are.

   **For *placement*** (where inside the instruction a run of internal clocks sits, which is what a timing
   model needs), reduce both corpora's records to **skeletons** — the cycle list with each idle run in place
   (`r n2 r r w`) — and compare per (mnemonic, operand-mode tuple): the two agree on 375 of 423 shared
   shapes, on both the total and the skeleton, and the only disagreements are the variable-time families
   (`MUL`/`DIV` by 2–8 clocks, `MOVEM` by its register count). **Where they disagree MAME wins** — it is the
   truth for an instruction's clocks *and* its final state, so a family that CLK times differently is set to
   MAME's numbers and CLK's are quoted beside them. `TRAPV` is outside any 100 % claim: MAME's own README disowns its TRAPV tests, so quote the exclusion
   rather than chasing it. **`TAS` was NOT excluded in the end** (row 81): MAME disowns its TAS
timing, but both corpora agree on the memory form's 14 clocks (`r n2 w r` vs CLK's single 10-clock `t`),
so the family is measured and exact on both columns — an oracle disowning a family is a reason to want a
second witness, not a reason to skip one.
   **The prefetch is a bus read inside the instruction's idle clocks — landed 2026-11-19, and it is the
   rule the frames hang on.** MAME's microcode has an explicit `prefetch` op between its idle runs, so its
   group-0 frame saves a *pipeline depth* (`saved = opcode + 2 + 2 × prefetch words issued`) that no
   expression on a lazily filled queue can give (measured: the five candidates split 23 058 / 20 173 /
   17 440 / 11 947 / 11 726 over 74 201 framed records, and one opcode shape takes two values record to
   record). In the core that is `PrefetchAsync` — **idempotent**, so an instruction's read *count* never
   changes, only *when* it happens — issued at the EA's 2 clocks and the family's own clocks
   (`ClocksAsync(n, prefetch: true)`), at `MOVEM`'s transfer loop in both directions and at `ADDX`/`SUBX`'s
   access. **Never at a leading charge** (a branch would fetch the fall-through it is about to abandon) and
   **never at an exception entry** (the handler's refill has not happened). The frame then saves the
   **fetch-state depth** for group-0 (`_fetchStateAddress`, not `_fetchPointer − 2`: an extension-word read
   must not move it, which was 19 791 records), and for group-1/2 `_opcodeAddress` **only for vectors 4, 8,
   10, 11** — `TRAP` keeps `_pc`, and taking that as one rule hangs the boot (see §7).
   Result: MAME 256 897 → **279 297 (88.0 %)** and CLK 983 625 → 821 970 (82.2 %, expected — CLK's records
   encode CLK's placement). The class counts and the model's four charge points are in `PLACEMENT.md`; the
   frame columns of `cpu-tests --clocks` (`frame_pc`, `fetch_ptr`, `pc_counter`, `ir_addr`, `refetched`)
   are how a new tree is read against them.

   The recipe and the per-family conventions are in `references/internal-clock-placement.md`.

   **Reading a class, and four rules that landed (2026-11-19).** `scripts/mame-failure-classes.py
   --failures FILE [--clocks FILE]` joins a `--failures` dump to the records' own `transactions` and
   `final.ssp` and prints the class table — run it before touching a class, the families move.
   `--failures` writes *every* failure (not 20 a file); `--trace N` traces record N of `files[0]`, and
   `--file` matches with `Contains`, so **`--file OR.l` traces `EOR.l`** — for an exact name, symlink
   **both** `X.json` and `X.json.bin` into a temp dir and pass `--dir` (a dir holding only the `.json.bin`
   fails with `no test file matches ''`), or pick a `--file` prefix only one file carries (`MOVEM.l`). **Trace through the corpus flag, never
  `--dir`**: a record read out of a `--dir` is decoded under the CLK conventions (its `pc` is the opcode's own
  address, not MAME's `m_au`), so a MAME record traced that way shows a phantom four-byte offset and a row-0
  mismatch on a record that passes — check the parent tree before believing such a trace, because the mismatch
  is the instrument's; `--corpus mame --file X --trace N` traces the gate's own record. The frames' layouts are measured from the stack pointer's
   movement (`initial.ssp − final.ssp` = 6 or 14) and are not what a manual reader expects: group-0 has
   `+0…+5` undefined (MAME's own residue), the instruction's word at `+6`, SR at `+8`, the saved pc at
   `+10`/`+12`; group-1/2 has SR at `+0`, pc at `+2`/`+4`. `ram` is byte-addressed, so a class's "low
   byte" is the *odd* address of that word (`ssp+13`, `ssp+5`).
   - **`SSW_N` (`0x08`) is not "read"** — it is MAME's nested-fault marker, never set for a fetch
     (5 211 records; RTS/JSR/RTR/RTE went 2 500/2 500 on that one bit).
   - **the frame's saved pc is a *fetch-state* depth** — MAME sets `m_pc` only in a fetch state, so an
     extension-word read must not move it. `_fetchStateAddress` (trailing fetch, `PrefetchAsync`,
     `RefillAsync`, the boundary — never `FetchAsync`, which also serves extension words) took the +2
     class from 19 791 to 11 570, and the rest is a +2/−2/−4 split *inside one opcode shape*: that is
     the measured argument for L2, not a bug to iterate on.
   - **a register write lands after the instruction's last fetch** — `DBcc`'s counter, `LINK`'s old `An`
     (same register when the operand is `A7`, so latch it before A7 moves), `UNLNK`'s stack pointer (an
     odd `An` must enter the frame with `ssp` untouched), `STOP`'s pc. Four files green.
   - **`STOP`'s pc belongs to the single-step seam, not the machine.** A stopped CPU's counter is the
     STOP opcode's own address, but the machine's loop keeps the successor — rolling `_pc` back *in the
     handler* makes EmuTOS re-execute STOP for ever (`frames_to_desktop=never`, 14 464 instructions).
   - **a clock charge can be 0 by a predicate bug**: the to-`CCR` forms are `0x3C`, so the old
     `(op & 0x00C0) == 0x00C0` test (which matches only the `0x7C` to-`SR` forms) charged them nothing;
     their measured residue is **12**, not 8, and 7 500 records become exact on that one line.
- **The core is a hand-expanded script, not a microcode interpreter (the owner's ruling, ticket #78).**
  The tables are the specification; an instruction is their direct expansion in ordinary C#. Where:
  `src/TheST.Machine/Cpu/M68000.Micro.cs` (the elementary operations: `AluOr`/`AluAnd`/`AluExt`,
  `SetNzvc`/`SetNzvcU`, `Set16L`/`Set16H`, `Ext32`/`High16`/`Merge16_32`, `IdleAsync` = one non-bus
  state, `BeginFetchState` = `m_pc = m_au`, `ReadOperandProgramAsync` = a read with the stream's bus
  status), `M68000.Scripts.cs` (the scripts, one method per shape, `IsScripted<Family>` as the ledger),
  and `design/cpu-scripts.md` (the MAME-name→C# table and the recipe). To port a family: generate the
  C++ with `python3 research/vendor/m68000/m68000gen.py sdf research/vendor/m68000/m68000.lst out.cpp`,
  count each handler's `read_interruptible` / `write_interruptible` / `m_icount -= 2`, transcribe in
  state order, make `IsScripted<Family>` true so `M68000.Timing.cs` charges nothing, then diff the
  file's `--failures` against `main`'s record by record — a script that trades one class for another
  looks like progress in the pass count.
- **The single-operand families are ONE script in size 2, and their operation field is bits 11-8.**
  `not_l_ds`/`not_l_ais`/… are the two-operand long's memory tail with a different ALU: the operand's two
  words, then `nnml1` (the ALU's low half **and** the closing prefetch, `m_pc = m_au`), `nnml2` (the low word
  at `address + 2`), `morw2` (the high word at `address`); `TST` is `tsml1`/`tsml2` with no write, and
  **`CLR` reads the operand it zeroes** (it is not a store). The register form is `nnrl1`/`nnrl2`/`roal4` —
  `set_16l` before the fetch, `set_16h` after it, one 2-clock idle; `TST` ends on `tsrl2` with none. One
  32-bit result is enough for the flags: `NEG` = `SetSubFlags(operand, 0, …)`, `NEGX` =
  `SetExchangeFlags(true, …)`, the rest a plain `nzvc` with V/C cleared. **Read the operation field as bits
  11-8** (0x0 NEGX, 0x2 CLR, 0x4 NEG, 0x6 NOT, 0xA TST): as bits 11-9, `0x4180` is `NEGX.l` and **`CHK` loses
  896 records**, because `CHK`'s own "size" field is `10` as well. All five `.l` files are 2 500/2 500 and
  every record exact-clock.
- **The same five in `.w` are a *different* block, not the `.l` one masked.** `adrw1` is an address latch with
  no bus cycle (so `(An)` is one read, `adrw2`); `pdcw1` writes `m_pc = m_au` **before** it steps `An` down
  (the long form's `pdcl1` never touches `m_pc`) — `BeginFetchState()` belongs in the `-(An)` case of the word
  EA block; `pinw2` commits the post-increment **before** its read, so a faulting `(An)+` word read leaves
  `An` stepped (the long form's `pinl3` leaves it alone). The memory tail is `nnmw1` (ALU **and** the
  `m_pc = m_au` prefetch) then `morw2`'s single write at `m_at`; `TST` is `tsmw1` + `mmrw3` and writes
  nothing. The register form is `nnrw1` + `roaw2` with **no closing idle**: `NOT.w Dn` is 4 clocks where
  `NOT.l Dn` is 6. All five `.w` files are 2 500/2 500 and every record exact-clock.
- **The same five in `.b` are the `.w` block, not a third one — and `CLR`'s read is the whole byte clock
  column.** One state list serves size 0 and size 1; only two facts depend on the width: the access (`ReadByteAsync`/
  `WriteByteAsync`, i.e. the lane the reference's `m_edb >>= 8` yields on an even address) and the EA's
  **unit step** — one byte, **except on `A7`, whose byte forms step by two** (`pinw1`/`pdcw1`:
  `m_au = m_da[ry] + (ry < 15 ? 1 : 2)`). So `SingleOperandWordOrByteAsync(op, size)` over
  `ReadSmallEaAsync(mode, reg, size)`, with `set_8` in place of `set_16l` on the register form. The four
  files `NOT.b`/`TST.b`/`NEGX.b`/`NEG.b` were already 2 500/2 500 *and* exact-clock; `CLR.b` was 4 clocks
  short on all 2 099 memory records because its old handler **computed the zero from a constant** while the
  microcode reads the operand (`nnmw1` runs `alu_and8` on `m_dbin`). **A file green on state and short on
  clocks has an access missing, not a table row wrong** — state cannot see a read.
- **`ADDX`/`SUBX` longs are two scripts, and the two predecrement commit rules live in them.**
  `asxl1`…`asxl8` + `morw2` (`addx_l_pais_paid`) and `rorl1`/`rorl2`/`rorl3`: one idle state at
  `asxl1` (the memory form is 30 clocks, not 28), two 2-clock idles in the register form
  (`rorl3`, `roml3` — 8 clocks, not 4), and the operand's two words read **a word at a time** at
  `A − 2` then `A − 4`. The register's commit is `asxl3`/`asxl6`, i.e. **after** the first word has
  come back: an odd `A` leaves the register where it stood (797 MAME records, `aN` 2 low before
  the fix). The byte and word forms' `asxw2` commits **before** its read — same family, different
  script; do not unify the two. Both corpora disagree here: CLK commits `A − 2`, so its already-red
  `ADDX.l` records gain a differing field while its pass count does not move. The byte and word
  memory forms also own that single `asxw1` idle (2 clocks, 5 004 records exact-clock after).
- **The two-operand families in size 1 are a *third* block, and their opmodes are neighbours'.** (row 76)
  `roaw1`/`roaw2` (`rorw1`/`rrgw2` for `<ea>,Dn`) = the ALU and its flags in a state with no bus cycle,
  then `set_16l` and the one read, and **no closing idle** (`roaw2` ends the script) — `OR.w Dn,Dn` 4
  clocks, `OR.l Dn,Dn` 6. `morw1` + `morw2` ALUs **before** the closing prefetch (the long's `morl1`
  fetches first). `ADDA.w`/`SUBA.w`/`CMPA.w` are the **long** tails: sign-extend the word
  (`romm1`/`cpmm1` do `m_at = ext32(m_dbin)`) and reuse `AluIntoAddressRegisterAsync` / the long compare
  tail. Line 0's word immediates read `o#w1` **before** the destination's extension word (the old handlers
  had it the other way round, which is a *wrong value* on every indexed/absolute destination). And the
  opmode field carries the size bits, so 1 = `<ea>,Dn`, 3 = `<ea>,An`, 5 = `Dn,<ea>` — which makes
  `DIVU` (line 8 opmode 3), `MULU` (line C opmode 3), `CMPM.w` (line B opmode 5 mode 1), **`EXG Dn,Dn`
  (line C opmode 5 mode 0)** and **`ADDX.w`/`SUBX.w` (lines D and 9 opmode 5 mode 0)** share an opmode
  with the block; claiming them cost 3 500 records on the first build *while the target files greened*.
  Exclude by mask (`(op & 0xF1F8) is 0xC140 or 0xC148 or 0xC188`, `(op & 0xF130) is 0xD100 or 0x9100`,
  `(op & 0xF138) == 0xB108`) and dispatch **above** the five line handlers.
- **`MOVE` is the family whose store comes *before* its closing read, and that is the whole batch.** (row
  77) Every other family fetches first (`morw1`/`morl1`) and stores after; a MOVE writes and then issues
  its one read, so the read's *position* is per destination class — `Dn`/`An` `R`; `(An)`,`(An)+` `W R`;
  `-(An)` `R W`; `(d16,An)`,`(xxx).w` `R W R`; `(d8,An,Xn)` `n2 R W R`; `(xxx).l` `R R W R` (register or
  immediate source) and `R W R R` (memory source). The source block is the ALU families' own, unchanged;
  `IsScriptedMove` = `(op >> 12) is 0x2 or 0x3` (lines 2 and 3 carry only MOVE/MOVEA; size 0 stays
  table-charged and was already green). Six facts the port paid for:
  1. **`(An)+` steps its register *after* the store, `-(An)` *with* it** — `mmmw2` writes the low word at
     A − 2, then `m_da[rx] = A − 4` before the high one — so a faulting store leaves `An` where it stood;
     the old handlers stepped while they addressed the operand.
  2. **A long value goes out high word first**, `-(An)` excepted (its `rmml1` latch is the *low* word).
  3. **The long form has two flag states and four placements** — `sr_nzvc` on the low word, `sr_nz_u` on
     the high one — and `sr_nz_u` is **not** `sr_nzvc_u`: N and Z only, **V and C left standing**
     (`m68000.h` has both; `M68000.Micro.cs` needed `SrNzU`). Their states differ by class
     (`rmrl1`..`rmrl3` between the words, `rmml1`/`rmml2` before both, `rmdl2`/`rmdl3` high-then-low,
     `mall2`/`mall3` low-then-high); only a fault *between* the two words exposes it — 252 records.
  4. **A group-0 frame's "instruction" word can be the queue head.** The word `-(An)` store state
     `mmmw2` begins `m_ird = m_ir` (`rmmw1` having done `m_ir = m_irc`), so *its* frame saves the
     instruction's **next word**; the long form's first write (`rmml2`) reassigns nothing so *its* frame
     keeps the opcode, and `MOVE.b` never faults there at all (a byte store at an odd address is legal —
     which is why the byte file was green and the word one was not). `FrameSavesInstructionWord`.
  5. **An odd *write* is 8 clocks**, like an odd read: the aborting state's own 4 *and* the abandoned
     cycle's 4 (`Cycle` charges the second). `WriteMoveWordAsync` pays the first; 1 042 records were
     exactly 4 short without it. Reading "the write's state replaces the abort" is the trap.
  6. **`TrailingFetchAsync` does not test `_busFaulted`** — a class that calls it after its store without
     a `return` reads one word too many (224 records read as 4 clocks *long* once the abort was charged).
  `(xxx).l` is #74's class and it is MAME's own: `FetchMoveAbsoluteLongAsync` + `_queueShort` + the
  prefetch-first half + `CloseAfterLongDestinationAsync` reproduce both rows — and the class's *first*
  read sets `m_pc` for a memory source (`BeginFetchState` before it) where a register source sets it at
  the prefetch-first read.
- **`CMPM` is three placements and no charge** (row 79; the last 162/165 records of `CMP.w`/`CMP.l`).
  `cmmw1`/`cmml1` moves `m_pc` **before the first read** (`BeginFetchState()` at the top), so a source
  fault frames the queue's word; the **destination's** increment lands at `cmmw4`/`cmml6`, *after* its
  read, so a destination fault leaves `Ax` alone (the source's is *staged*: `cmml2` commits `A + 2`,
  `cmml3` `A + 4`, so a long first-word fault steps 2, not 4); and the destination's address is
  `m_da[rx]` read at `cmmw3`/`cmml4`, i.e. **after** the source has stepped — only `(A4)+,(A4)+` shows
  it (39 records). `cmpm_*` has **0 idle states**: its long form's opmode is the two-operand long's, so
  the table charged it 4 in `AluOwnClocks` (82 records) — check the ledger covers a family whose
  "size" field is two bits wide.
- **`ASR`'s C and X are NOT zero past the width; the arithmetic shift keeps propagating the sign.**
  MAME's `alu_asr8` is a byte op in a loop, so `0x80` shifted 17 times leaves C and X **set** — row 46's
  "zero past the width" is the *logical* shift's rule. `ASR.b`/`ASR.l` were 2 500 exact-clock while 826
  of their records failed on `sr` alone. CLK's corpus follows the manual here, so the port costs it
  2 673 records: that is a documented convention split, quote it, never "fix" it back.
- **A memory shape's group-0 saved `pc` is per *addressing class*, and one switch covers every family
  whose operand read faults.** `asr_adr16`'s first state and `asr_adr32`'s second are `m_pc = m_au`
  (the adder's own value, `_au`); `aixl1` **never** moves it (`opcode + 2`); the predecrement block moves it its
  own way (`opcode + 4`); `(An)`/`(An)+`/`(d16,An)` already frame what the core leaves. `(d8,PC,Xn)` and
  `(d16,PC)` are the `opcode + 2` half too, and their operand read is a **program** read
  (`ReadOperandProgramAsync` — the frame's status word `0xF6` vs `0xF5`, 74 records). One helper
  (`FrameCounterForExtensionShape(mode, reg)`) served the memory shifts (83 records) and the status
  moves (412) unchanged — write it once, in `Scripts.cs`.
- **`MOVE from SR` READS its operand before storing it, and a status write refills the queue after it.**
  `move_sr_*` is `r w` with the instruction's own program read in between, so an odd address is a
  *read* fault (1 118 records 4 short, and the frame's status word says "read"); `move_*_ccr`/`move_*_sr`
  end `n 4` then **two program reads at the instruction's own next word** (`RefillAsync(_pc)` — the
  measured pair is the extension word read again plus `+2`), because a status write can change which
  stack and bus the CPU runs on: every supervisor record was exactly 8 short and every user-mode one
  (a privilege trap, framed before any EA work) exact. Its `-(An)` idle carries **no** read (the refill
  does it) where the memory shift's does — the same 2 clocks, two different scripts; measure the
  per-record residue rather than deciding from the shape.
- **`Bcc`'s odd-target fault saves `opcode + 2`** (`bbci3` reads the target without moving `m_pc`), which
  is `JMP`'s rule and not `BSR`'s (`b`/`malw3` move it to the target). One record of 5 000, and it
  greened `Bcc` with `DBcc`.
- **The clock residue is a class problem, and its one distinguisher is: a ±2 is a *state*, a ±4 is a *read*.**
  After row 79 the gate was 100 % on state with 18 745 records still off on clocks, all inside green files,
  so `--failures` could not see them — the instrument is `--clocks` grouped by the record's own operand
  shape. Nine facts, in `FINDINGS.md` row 80: the `to-SR` immediates test privilege **before** fetching the
  immediate (a user-mode record never reads it) and their supervisor tail is eight clocks plus a two-read
  refill; **a byte `Scc` reads the byte it writes** (3 accesses, 0 idle); **a branch's displacement word is
  free** when the stream is abandoned (`dbcc1`/`bbcw1` pop it out of `m_dbin` — `JumpWordAsync(0)`) but a
  `Bcc` **fall-through is not a transfer** and does read it; **a free pop still moves the pipeline depth**
  (`dbcc3`'s `m_pc = m_au` is the word an odd target's frame saves — the shape declares `FrameSavesOnFetch(_au)`
  because the position no longer trails `_au` after a free pop, and `RefillAsync`'s default would save `_pc`),
  and the refill's aborted read is the branch's own state (`ChargeAbortedReadAsync`, 632 + 669 records);
  **`LEA`/`PEA`'s indexed shape is two 2-clock states** (`leax0`+`leax4`); **`NBCD`'s register form ends on
  2** (`nbcr3`); and the bit operations' charge grows with the **bit position** (below).
- **A bit operation's own clocks are a function of the bit position, not of the shape**: `BCHG`/`BSET` 2,
  `BCLR` 4 on a data register, **+2 when the bit is above 15** — the 6-or-8 split `LENGTH.md` §6 listed as
  undecidable is one line of CLK: `did_bit_op` sets `dynamic_instruction_length_ = int(bit > 15)` and
  `BCHG_BSET_Dn` spends `IdleBus(1 + …)`. The bit number is a data-register value (`Dn & 31`) or the static
  form's extension word, which is why no operand text separated the groups. `BTST` never pays it
  (`Perform_np_n`), and its undocumented `BTST Dn,#data` form is two reads + `btsr3`'s idle.
- **A line that falls through to `AluOwnClocks` is charged by whatever row matches — check what the masked
  value *is*.** `MOVEQ` (line 7) has its value in the "size" and "mode" fields, so the plain two-operand
  long row charged it 2/4 (1 204 records); `MOVE.b` — the one MOVE size `IsScriptedMove` does not cover —
  encodes byte as size 3, so the `(op & 0x00C0) == 0x00C0` CMPA/ADDA row claimed every one (1 383). Give
  both lines `case 0x1:`/`case 0x7: return 0;`.
- **A `MOVE`'s two operands pay their EA block independently**: the predecrement once per instruction
  (`_eaCharged`), the indexed one **per operand**. 65 + 67 byte records were 2 short behind a charged
  source and 281 were 2 long behind an uncharged `-(An)` destination (whose block is never its own).
- **A ledger predicate must be the one predicate that decides, not a mask.** `IsScriptedAluLong` excluded
  CMPM by `(op & 0xF138) == 0xB108`, which cannot tell CMPM's two-bit size field from `CMPA.l` with a
  mode-1 source: a scripted family was charged the table's `CMPA` 2 *and* its own `rcal3` (315 records).
  Call `IsScriptedCmpm(op)` instead — row 74's `ADDX.l` was the same disease.
- **Where the MAME gate stands (2026-11-21, row 81): `317 500 / 317 500` state (100 %), 127 of 127 files
  green, exact-clock `317 500` (100 %) — the corpus is closed on BOTH columns.** CLK 863 265 / 1 000 060
  beside it (58 of 124 files), its own exact-clock column 819 372 (81.9 %), and its per-file state table
  unmoved by rows 80–81. **CLK's clock column is at its ceiling**: the whole remaining residue is the two
  corpora's conventions, measured per shape — **+8 on 51 050 records is the address error's abort charge**
  (MAME pays the aborting state's 4 *and* 4 for the abandoned cycle; CLK's traces charge neither: `n 4` +
  frame + `n 2` + refill), **+2 on 731** is `ADDQ.l`/`SUBQ.l` into an `An` (CLK spends `maql1`'s idle),
  **−2 on 605** is `CHK`'s trap path (CLK pays `chkr3`, MAME's `chkr2` branches to `trap1`), and **+112…+132
  on 307** is `DIVS` overflow, where CLK short-circuits the division to 16 clocks and MAME's loop runs to
  `m_ftu` regardless. Do not chase any of them; a CLK clock record cannot be greened without leaving MAME.
  **`scripts/clk-residue-by-shape.py`** prints this profile (residue histogram, per file, per shape) from a
  `--clocks` dump and the matching `--failures` dump — run it before quoting any CLK clock percentage, and
  before believing any "the clock column is the next work" claim from a handoff.
- **`TAS`'s memory form is `r n2 w r`, and the `t` column was a decomposition, not a missing model.**
  `tas_ais` runs `tasm1` (`alu_or8` on the byte, then `m_icount -= 2`) between `adrw2`'s read and `tasm2`'s
  write, so `TAS (An)` is 14 clocks; CLK times the same 14 as one `['t', 10, …]` read-modify-write
  transaction, which is why the census read the family as "10 clocks nobody models" (2 missing, 8 inside
  the `t`). The register form needs nothing. **Both corpora agreeing on a total the gate's README disowns
  is a measurement, not a convention split** — fix it and quote 100 %.
- **An encoding absent from MAME's `.lst` is illegal on the part — grep the table, do not assume the
  manual's list.** `MOVEM <list>,(An)+` (`0x48D8`/`0x48F8`) had no entry and this core executed it with a
  per-store `An` step nobody had measured; it now raises vector 4. Neither corpus carries a record for an
  illegal form, so no pass count can move: the witness is the boot plus the decode table, and the datum
  goes in the handler's comment and a ticket. **And the illegal test belongs before the mask's fetch** —
  the part decodes the opcode first, so an illegal form must not read an extension word it never sees.
- **A masked clock row can never fire, and it is invisible while the state column is green.** `TAS`'s row
  tested `(op & 0xFF00) == 0x4AC0` (low byte masked off) and `JSR`'s `(op & 0xFFC0) == 0x4E90` (`& 0xFFC0`
  gives `0x4E80`). Compute `op & mask` before trusting any `==` in `M68000.Timing.cs`, and prefer the
  handler's own charge for a family whose script already knows where its states go.
- **`MOVEM` is a loop, and its three failures are placement, not clocks** (row 78). Both files are 2 500,
  and the clock table was already right: every red record was a *read* or a register, never a charge.
  Facts, each measured:
  1. **The load direction's `(An)+` increment lands once, last.** `popm6` is `m_da[ry] = m_at` and it is
     reached only when the mask is exhausted (`step_movem`/`m_t`), so *any* fault — the first access or
     the fifth — leaves `An` exactly where it stood. Pre-committing `SetA(reg, address + 2)` per register
     put `An` 2 past every faulting record (249 of them), *including* a long read whose first word faults.
  2. **The PC-relative classes read the transfer with `SSW_PROGRAM`.** `movem_l_dpc_list` /
     `movem_l_axpc_list` set `m_base_ssw = SSW_PROGRAM | SSW_R` for the address's states and never reset
     it, so a fault there frames a *program* access (`0x4CF2`, not the data `0x4CF1`) — 73 records, all
     `(d16,PC)`/`(d8,PC,Xn)` loads. `(xxx).w`/`(xxx).l` are the *data* class (`movem_l_adr32_list` sets
     `SSW_DATA` per access), so the predicate is `mode == 7 && reg is 2 or 3`, never `mode == 7`. Use
     `ReadOperandProgramAsync` (a program read that is not a fetch: the saved counter does not move).
  3. **A clean load reads one word *ahead* of the list.** `popm5` sets `m_aob = m_au` and `m_at = m_au`
     while it fills the register it just read, so the transfer ends with one discarded word read past the
     last register — `fc 5`, 4 clocks, in the record. 1 331 clean records of the two files were exactly 4
     short without it, while every *faulting* record was already exact (the fault returns before the
     read). A file that is uniformly 4 short on its clean records is a missing access, not a wrong row.
  The store direction (`0x48xx`) needed no fix: green on both columns, its per-register pre-commit correct
  there, and `-(An)` gets **no** EA idle (its internal clocks are 0) where the indexed shapes get the 2.
  The whole class's arithmetic, which is what a mask-driven loop always looks like: the load side is `n + 1`
  data reads for the word form and `2n + 1` for the long one (the stray one is the read-ahead), plus one
  program read before the loop (`popm1`, the instruction's own prefetch) and one after (`popm6`'s closing
  fetch); the store side is `n`/`2n` **writes** with those same two program reads and **no** read-ahead
  (`stmr1`/`stmr2`/`stmr4`/`stmr6` upward, `push1`/`push2`/`push4`/`push5`/`push3` downward, the predecrement
  picking its register with `step_movem_predec`'s `r ^ 0xf` and writing the long low word first at the
  higher address). So `length` is just `4 ×` that count — `48D0 (A0)` with `n = 11` is 22 writes + 2 reads =
  96 clocks. One invariant explains the register half of every one of them: **`m_at` holds the address of
  the most recently issued transfer access, and `popm6`/`push3` publishes it as `An` exactly once, at the
  the end** — which is why the last access is the read-ahead (one width past the list) and why any fault leaves
  `An` alone.
  **And the mask itself is the loop's state** (the die's `PREN`, one of the ten 16-bit registers): it holds the
  list, `reset pren` answers its lowest set line and clears that line, and the mask's own zero (MAME's
  `m_t = !m_movemr`) is the exit — so both directions read `while (!PrenEmpty)` and no counter can disagree
  with the mask. The predecrement list is stored reversed, so the line's complement *is* the register
  (`step_movem_predec`'s `r ^ 0xf`) and the `15 − bit` arithmetic disappears. Keep the empty-list clamp
  (`countr_zero(0)` = 32 → line 0) inside the encoder atom, not in a caller's convention.
- **`--file` on MAME matches with `Contains`** — `--file OR.w` runs `OR.w`, `EOR.w` and `ROR.w`; for one
  file symlink it into an empty directory and pass `--dir`. And a file cannot green while a *neighbour*
  lives in it (`CMP.w`'s last 162 are `CMPM.w`), so count a family from the record names.
- **The variable-time families are loops, and the loop is the clock** (row 75). `MULU`/`MULS`/`DIVU`/`DIVS`
  cannot be measured by shape at all: their clocks are a counter on a value, so a handler that computes the
  answer directly has neither. Port them as the microcode's own loop (`MultiplyAsync`,
  `DivideUnsignedAsync`, `DivideSignedAsync`, `CheckAsync`) over a **sixteen-bit ALU of their own**
  (`M68000.Micro.cs`: `_isr` the flag latch, `_alue` the second operand, `AluAdd16`/`AluSub16`/
  `AluSubC16`/`AluAnd16`/`AluOver`/`AluSla`/`AluRoxr32mu`/`AluRoxr32ms`). Six facts, each measured:
  (1) a **divide's overflow is `alu_over`** — N *and* V set, Z and C cleared — not the manual's V alone
  (1 459 records); (2) `MULS` is Booth recoding (add/subtract/neither over two-bit digits), so
  `34 + 2 × popcount` scatters ±30; (3) MUL's product comes out of a **32-bit shift register**
  (`m_aluo:m_alue` rotated right, low half = what is left of the multiplier), and its Z starts set and only
  ever clears; (4) `CHK`'s flags are the **second** comparison's, and its trap path is two states — `chkr2`
  branches straight to `trap1`, so charging `chkr3` there is 2 clocks long on 924 records; (5) a group-0
  frame's saved `pc` is the instruction's **first extension word** (`P+2`, `P+4` for `(xxx).w`, `P+6` for
  `(xxx).l`; `pdcw1` moves it, `pinw1` does not), and a PC-relative operand read carries the program bus
  status; (6) the loop counter is MAME's `m_ftu`, an **undocumented constant that is 15 for all five** —
  find it by brute force against the recorded clocks.
- **`m_aluo` and `m_alue` are `u16`, and that is semantics, not a detail.** The multiply's shift register
  is a u16 *pair* (`a << 15` on a u16, the carry in at bit 31), and `alu_sla*`'s `a << 17` is how the
  divide's pair shifts left. Masking operands to 0xFFFF inside the ALU helpers (as the C++ signature does)
  is what makes the result right; a transcription that treats `m_alue` as 32 bits produces garbage on every
  record.
- **A python transcription is the cheap instrument for a loop family.** Before any C#, write the states in
  python and compare record by record on **clocks, destination register and flags** — it needs no build,
  and it catches the transcription slips that otherwise look like corpus disagreements (MULS's first step is
  a *subtract*, `set_16h(at, at)` is a copy not an or).
- **A `+2` on every clean record of a family is a table row, not the script.** Two masked rows in
  `M68000.Timing.cs` claimed these five: `(op & 0x00C0) == 0x00C0` charged them 2 as `CMPA.w`, and
  `(op & 0xF130) == 0x8100` matched `DIVS Dn,<any>` as `ADDX`/`SUBX`. In `AluOwnClocks` a scripted family's
  `_own` must be tested **before every masked row**.
- **`--clocks`' frame columns: the entry's on a faulting record, the boundary's on a clean one** — written
  in `EnterExceptionAsync`, and since `61f4080` at the boundary too; before that they were *stale* on clean
  records, which once reported 2 496 "vector 3" DIVU records where the corpus has 954. Take the fault flag
  from the record's own `transactions` (`re`). `fetch_ptr − pc_counter` is the depth per record (4; 6 on
  `STOP`'s seam rollback). For a group-0 class the cheapest check is
  the record itself: `initial.ssp − final.ssp == 14` with seven `w` transactions is an exception entry
  (and, from CLK's side, the removal is a `t`/`n` run that is not there).
  `scripts/clk-residue-by-shape.py` does the grouping and that check from a `--clocks` + `--failures`
  pair (`--verify-faults tests/680x0/full`).
- **`ADDX.l`/`SUBX.l` were the reason a whole clock column never rose: the ledger lied about them.**
  Their size field at 2 lands them on the two-operand longs' opmodes 2 and 6, so
  `IsScriptedAluLong` answered 0 for them while nothing was scripted — a family can be *claimed* by
  a ledger predicate and still be unported, and the symptom is a clock column that never rises
  while state looks healthy. `IsScriptedExchangeLong` is now the explicit one. The word/byte forms'
  shape row was dead code too (`(op & 0xF130) == 0x0100` never matches `0xD100`/`0x9100`).
- **A failure-set diff is not enough when a record can gain a *field*.** The long-batch set diff read
  "0 new failing, 0 fixed" on CLK while 2 593 of its records had gained an `aN` disagreement — they
  were already red on the frame's status byte, so the *set* was identical. Diff the first differing
  field per record (`file\tname` → reason) as well as the set when the corpora are contested.
- **The predecrement's *three rules, three call sites* are now** `ReadLongEaAsync`'s mode 4 (a
  `MOVE`-class long read steps down 4 and faults at `A − 4`), `PredecrementAsync` (the word form and
  BCD: step, then read), and `ExchangeLongAsync` (step **after** the first word). `PredecrementAsync`
  no longer serves any long.

- **An old handler's missing prefetch is what most "clock column" gaps are.** Both single-operand batches
  were exactly 4 short on every memory record and 8 on the clean multi-extension shapes: the handler never
  issued the instruction's own closing prefetch, and the shape table only partly compensated. Fix it by
  issuing the read in the script (`TrailingFetchAsync`), never by adjusting the table's charge. And read
  `--clocks`' `our_total − length` grouped by operand shape yourself — a handoff or a summary line can have
  the sign the other way round.
- **Diff the corpus per file, not by total.** That `CHK` swallow read as a gain 896 short of the expected
  one; the per-file diff (`--out` before and after, baseline from a `git worktree`) named it in one line.
  A predicate that claims a neighbouring family never shows up as a failure.
- **The two-operand longs are all scripted (2026-11-19, #79), so a new ALU family is a `case`, not a file.**
  `AluLongAsync` keys on the line and passes an `AluLong` value into `AluLongShapeAsync`, which handles the
  three destinations (`<ea>,Dn`, `Dn,<ea>`, `->An`) over one shared operand block and three tails
  (`AluIntoDataRegisterAsync`, `AluIntoMemoryLongAsync`, `AluIntoAddressRegisterAsync`) plus
  `CompareLongAsync`. Four things to know before touching it: (1) **`CMP`'s tail is its own** — both ALU
  halves run *before* the closing prefetch (`cpml1`, `cpml2`, `rcal3`), because it is the destination's
  stores that put a fetch between the halves in the RMW tail, and `extend: false` keeps X; (2)
  **`ADDA`/`SUBA` set no flags at all** (`i=.......` on every state) — same word-by-word ALU into `An`, no
  flag call; (3) the flags are computed **once, 32 bits wide** (`AluLongOp` + `SetLongFlags`), which equals
  MAME's `alu_add`+`alu_addc` pair because the low word's carry out is the high word's carry in, and the
  `_u` half's "Z only cleared" is `result == 0`; (4) the writes stay where the script puts them (low word,
  prefetch, high word), so a fault still leaves the register half updated.
- **An opmode-keyed ledger must exclude the instructions that share its opmode.** `IsScriptedAluLong` reads
  the encoding, and two families hide inside the long opmodes: **`EXG Dn,An`** (`0xC188`, line C opmode 6)
  and **`CMPM.l`** (line B opmode 6, because `CMPM`'s size field is two bits wide so its long form lands on
  `EOR`'s opmode). Missing either does not fail loudly: the unported family keeps its old handler but
  silently loses its clock charge (`AluOwnClocks` returns 0), and the loss shows only as a clock column
  that never rises. Exclude by mask (`(op & 0xF1F8) == 0xC188`, `(op & 0xF138) == 0xB108`).
- **"A family is green" must be counted from the corpus's own opcodes, not the file's verdict.** A file
  carries other instructions (`AND.l` 2 371 `and.l` + 129 `and.i`; `ADD.l` 1 538 + 872 `ADDQ` + 90 `ADDI`).
  Count the class from each record's `name` (the last token is the opcode word) and subtract that class's
  failures from both dumps — the honest statement is "`and.l` 1 795 → 2 371 of 2 371", never "the file rose
  by 576".
- **Four conventions the OR.l port paid for; take them with any family.** (1) `m_pc` moves only in a
  state that *begins* a prefetch (`roml1`, `abww1`, `ablw2`, `e#w1`) — not in one that merely pops an
  extension word (`adsl1`, `aixw2`), so set the frame's counter in the script and nowhere else.
  (2) A PC-relative shape reads its operand's first word with the instruction stream's bus status
  (`SSW_PROGRAM|SSW_R`), so a fault there records a program access while the saved counter stays the
  instruction's own — the status bit and `_faultIsFetch` are two facts, not one. (3) A long store goes
  **low word first** (`morl2` writes `m_at+2`, then `morw2` writes `m_at`). (4) A post-increment commits
  after both words (`pinl3`), so a fault leaves `An` untouched — this is per script, not the `MOVE`
  class rule. And **do not name a micro-op `StepAsync`**: that is the single-step seam's public method
  and the harness then recurses into the core until the stack dies (the idle state is `IdleAsync`).
- **A faulting record is still short until the abort is charged, and the charge is two-part.** The
  microcode charges the aborting state its own 4 **and then** 4 more on the odd address
  (`if(m_aob & 1) { m_icount -= 4; }` *after* the state's charge), and MAME carries the second as an
  `re`/`we` transaction. So `Cycle`'s odd-address branch charges 4 for the abort (no record, no
  prefetch, machine time follows), and a **scripted** family pays the state's 4 too
  (`ChargeAbortedReadAsync`, called where a data read faults): an odd read is 8 clocks, an odd write 4.
  Never charge a global 8 — the shape table's rows already contain that 4 because they were measured on
  records that include it, so 8 breaks 6 922 table-charged records while fixing 7 377 scripted ones.
  The extra charge thus lives in the scripts and dies with the table (row 68).
- **Line 0's immediates and line 5's quicks are one more state block, not a variant of the ALU tails.**
  An immediate is two *program* reads before the destination's EA (`o#l1`, `o#w1`; `o#w1` is the state
  that moves `m_pc`); a quick has no state at all (the value is the opcode's bits 11-9, 0 meaning 8,
  MAME's `m_ftu` and `maql1`/`raql1`); an address register takes the whole 32-bit value with an *empty
  flag mask* (`m=.....` — no flag call), and the register tail ends on `roal3`/`roal4` (2 + 2 idle) rather
  than on the closing fetch. One method each (`ImmediateLongAsync`, `QuickLongAsync`) over the ported
  tails; CMPI needs `CompareLongTailAsync` with `clocks: 2` for a register destination and 0 for a
  memory one (`cpdl2` ends on the prefetch). Every record of those families is exact-clock after it.
- **A control transfer is not a shape, and three of its facts are per-script.** (1) Its extension word comes
  out of the queue **free**: a jump abandons the stream it would refill, so no read is issued behind the word
  it took — `JMP (d16,An)` is `n2 r r` (2 reads at the target) where a fall-through instruction of the same
  shape reads three, `BSR.w` is 18 clocks like `BSR.b`, `JMP (xxx).l` is 3 reads with no idle between them.
  `JumpWordAsync(index)` is that pop (first word free, each later one a read at the fetch pointer). (2) Its own
  clocks **lead** and `_own` is only spent at the closing fetch a transfer never runs, so a jump's `Timing.cs`
  row is charged *nowhere* — `JMP` was 2 short on `(d16,An)`, 6 on `(d8,An,Xn)`, 4 over on `(xxx).l`. (3) The
  frame's saved counter is `m_pc` at the aborted read and the script decides it (`FrameSavesOnFetch`, because
  the default is `_au` — the position's own word at a fetch state — and that is right only for a fault at the
  stream's own read):
  `BSR`'s `b`/`malw3` put the **target** there (`saved − opcode` = the displacement), `JMP`'s `bbci3` leaves the
  boundary's word (`opcode + 2` on every shape, whatever the address cost), `UNLK`'s `unlk1` takes it as it
  stands (`opcode + 4`). Measured: 1 271 / 911 / 1 115 records, and the three files 2 500/2 500 exact-clock.
- **`JSR` is that tail plus a push, and its states are the ones a mask can hide.** The target's read comes
  *before* the push (`jsra1`/`jsrd3`), so an odd target pushes nothing and pays the aborting state's own 4;
  `m_pc` advances past each extension word as it is consumed (`jsrd2`/`jsrd3`: `m_au = m_pc + 2; m_pc = m_au`),
  so the return address *and* an odd target's frame both save the counter past the last extension word —
  `opcode + 2` for `(An)`, `+4` for one extension word, `+6` for `(xxx).l` (measured 2/4/6 over 2 500 records).
  `jsal2` (`(xxx).l`) is the **one** place a queue read does move the counter (`m_pc = m_au` after `jsal1`'s
  increment): `ReadQueueWordAsync` does not advance `_pc`, so that shape needs an explicit `_pc += 2` or both
  the push and the frame are a word low (90 records). Shapes' idle runs are `JMP`'s table exactly (2 / 6 / 0).
- **A return's aborted read is the return's to charge.** `RTS` (1 237), `RTR` (1 215) and `RTE` (614) were green
  on state and exactly 4 clocks short on *only* their faulting records: `Cycle` charges the abandoned cycle's 4
  and a table-charged family's row carries none, so `ChargeAbortedReadAsync` goes at **every** fault point —
  each read and after the `RefillAsync` (an odd return address aborts the refill's first read, not the pop).
- **A clock row can be dead code: compute the mask.** `(op & 0xFFC0) == 0x4E90` never matches — `0x4E90 & 0xFFC0`
  is `0x4E80` — so `JSR`'s 2/4 row in `M68000.Timing.cs` was unreachable and `_own` was 0 on every `JSR` while
  the comment beside it read "JSR still measures by shape". The state was green and the clock column 84 % exact,
  which is exactly what such a row looks like; before trusting a shape row, ask what the masked value *is*.
- **A scripted family charges its own aborted read, wherever the fault lands — including inside `RefillAsync`.**
  `ChargeAbortedReadAsync` lives in the scripts, not in `RefillAsync`/`ReadProgramAsync`, because those are
  shared with table-charged families (`JSR`, `RTE`, the exception entry) whose rows already carry that 4. `BSR`
  needed it *after* its `await RefillAsync(target)` call: the file's state verdict went 2 500/2 500 while its
  clock column stayed at the old 1 229 until that one line was added.
- **`exact-clock` is counted only over records that pass state.** A file can read "1 385 / 1 385, exact-clock
  1 385" while 1 115 of its records are 4 clocks short — read `--clocks`' `our_total` against `length`
  yourself before believing a family's clock column. Same shape of trap in `--failures`: it lists *state*
  failures only, so a clock-only failure never appears in the dump.
- **`--file` on the MAME corpus takes the bare name** (`--file BSR`, not `--file BSR.json`) and matches with
  `Contains`, like the CLK side. **Never `cat` `run.stream`** (a boot writes ~250 MB); `run.state` carries
  `digest_sha256`, `stream_sha256`, `frames_to_desktop` and `pops_per_frame`, which is everything a fold report
  needs.
- **A CLK record that falls while its MAME counterpart passes is the frame-status-word dispute, not a bug.**
  After #79, three CLK records of a PC-relative operand's *faulting* shape crossed pass→fail while every
  MAME record of those shapes stayed green: the script reads the operand's first word with the stream's bus
  status (`SSW_PROGRAM`), which is MAME's convention and CLK's record disagrees on that one frame byte.
  Check the record has a group-0 frame (its `transactions` write 14 bytes below `ssp`) before reading it as
  a placement regression; then quote MAME and move on.
0c. **the microcode itself, to read a rule rather than infer it.** MAME's micro/nanocode is data in
    `m68000gen.py` (1024 locations, 518 control words; BSD-3-Clause, transcribed from a die photo) and
    `scripts/fetch-m68000-microcode.sh` vendors it at a pin into `research/vendor/m68000/`;
    `scripts/microcode-walk.py --op 4e75` (or `--state address_error`) prints an instruction's script in
    order. Every **charged** micro-state costs 2 clocks and a bus-access state 4 (`istep` vs
    `m_icount -= 4`) — but read the charge, because a state with neither costs **0** (`rorw1`, `romw1`,
    `romm1`, `cpmm1`, `adrw1`, `morw1` carry no `m_icount` line at all, which is why `OR.w Dn,Dn` is 4
    clocks and `OR.w (A4),D4` 8). An instruction's clocks are its reads' 4s plus its explicit `istep`s —
    no timing table exists anywhere in MAME. **Before writing any charge for a class, read the listing's
    footer**: `# bus accesses in this listing: N   idle states: M`. `idle states: 0` means there is no
    charge to add — only accesses to place correctly (`movem_*_list` is 10 accesses, 0 idle, and the
    handoff that predicted a per-register idle count for it was wrong). This is the instrument for "where does this frame's saved
    `pc` comes from" and for the `LENGTH.md` §6 open datum. **And do not expect the microcode to be the whole
    control: the two ROMs drive roughly 70 of ~200 control signals; the rest is PLA decode keyed on the
    instruction register.** A behaviour that is missing from a listing (whether `RX`/`RY` is a data or address
    register, the operand size, the exception vector) is decided there, not left undocumented — so "the script
    does not say" ends the microcode search rather than opening a hunt for a missing state.
    `research/mame-microcode-core.md` is the reading, the 38 203-failure class split and the three
    adoption levels (instrument / rules / engine). **`research/m68000-silicon-implementation.md` is the
    silicon-level authority map** — which die set is which, the ROM geometry (544 × 17 microcode in 34 rows ×
    16 columns, 336 × 68 nanocode in 84 rows × 4, 32 096 bits total), the A1/A2/A3 PLAs, the register file's
    two naming schemes, the ALU's 15 × 5 matrix, and which claims in the literature are wrong. Read it before
    trusting any secondary account of how the 68000 is built. `research/68000-silicon/` carries its
    attachments — the die photographs with a numbered overlay (`die/`), five educational sheets
    (`schematics/index.html`), and the naming study (`nomenclature.md`) with the alias table the code
    should follow. **Two vocabularies, two authorities**: the *manual* fixes the architectural names (`D0–D7`,
    `AS`, `DTACK`), the *patents* the microarchitectural ones (`AOB`, `AT`, `AU`, `FTU`, `DCR`, `PREN`, `SSW`),
    and the die supplies content while the patent supplies names. Any ticket about the core's shape, surface,
    internal state, decode or "make the code match the schematics" follows `references/silicon-shape.md` —
    the latch inventory, the missing `A0` pin, **the pins are the interface rather than a view** (they read the
    engine's wire records; `AOB`/`DOB`/`DBIN` sit below them), `AU` as the hardware's `PC`, the 65 536-opcode decode golden,
    the time-versus-storage naming rule (`…Clocks`/`…Ticks`, never in the register file), the manual's state
    concepts as displayable strings composed on read, the slice order for converting the surface with its
    freeze test, the accessor-vs-nested-type naming collision (CS0102), and its sibling: `grep -rn "uint _pc" src/` before adding a register field — the class spans ten partial files and a half-finished rename leaves a second declaration behind, the bar every such landing must hold, and §7's
gap list against §5–§13 of the silicon reading. Three rules it already states: `SSW_N` (0x08) is the
    nested-fault marker, **not** "read", so a fetch fault's frame word must not set it (5 211 records);
    a post-increment commits in the state **after** its access (`pinl3`), not before (the `+4`/`−2` split
    of the `a0`–`a6` class); the `.l` RMW scripts read the operand, prefetch, then write (the depth of
    the 19 791 saved-`pc` records).
0d. **a per-site probe inside the core** — for a rule evaluated at instants the dump never samples (a read, a
    state entry), a `#if DEBUG` tally at the site answers it over the whole corpus in one 10 s run: one
    `Probe("site", actual, candidate)` per site, a static tally, and a printer called from the harness at the
    end of `cpu-tests` (it must be `public` while it lives — a separate assembly cannot see `internal` — so it
    never enters a commit: delete it and the hook before the gate). `templates/per-site-probe.cs` is the
    skeleton; the probe says which expression is exact, the corpus's two columns stay the verdict.
1. **the corpus**, one test at a time, exact;
2. **EmuTOS's own instruction patterns** in the boot (what the machine actually executes, and where a wrong flag costs
   a frame);
3. **Steem SSE's core, vendored in the tree** (`steemsse-V4.2.0_R10/steem/cpu*.cpp`) — a second implementation of the
   same manual, and how the previous rows were settled rather than guessed.

## 6. The proof, and the two traps around it

- **Proof**: the family's files green **file by file** on the sample, the whole set **≥ 99 %**, then
  `bash scripts/validate.sh` **PASS** (G8 green). **The desktop is a gate-boot fact, not a 300-frame one**: the
  boot reaches it at emulated frame ~600, so `boot --frames 300` prints `frames_to_desktop=never` on *every*
  tree, including `main` — that run is the digest pair's, and the desktop frame (`frames_to_desktop`,
  `_frclock_at_desktop`) comes from the 800-frame pass. Reading "never" as a hang is a wasted hour. The corpus is not one of the gate's rows, so its percentage is a separate obligation. Since the
  queue (`#70`) the desktop lands at **frame 591** (`_frclock_at_desktop`; 598 is the Release `frames_to_desktop`
  of the 800-frame gate boot) and G8 reads **9.75–9.84 ms at n 747** — surface those as the baseline, not as a
  regression: a control transfer now pays the manual's two fetches, which is +2.3 % bus cycles a frame and ~4 %
  more emulated frames for the same boot. `#74` left both at parity (A/B against `main`, 3 interleaved runs each:
  9.82/9.55/10.02 versus 9.94/9.91/9.61). The frame margin is thin (0.2–0.4 ms), so quote `frame_n` beside any
  mean and price any further CPU work against it. **Two traps in that number.** `validate.sh`'s own G8 row runs
  higher than a standalone `boot --frames 800` on the same binary (~0.9 ms on this host), so compare validate with
  validate and never across the two instruments. **A `validate.sh` stamp writes two run states**:
  `runs/<stamp>/boot/run.state` is the *Debug* boot (`frame_mean_ms` in the tens of ms) and
  `runs/<stamp>/g8/run.state` is the Release G8 row — quote G8 from `g8/`, and if a frame figure you are
  about to write into a doc did not come from a file you read in this session, read it again. And when a G8 figure looks like a regression, A/B it:
  `git worktree add <scratch> <other-commit>`, build Release in both trees, alternate `boot --frames 800 --out`
  runs between the two binaries (3 pairs) and compare `frame_mean_ms` at the same `frame_n` — that is the decisive
  measurement, a single run is not. **Start every `boot` from the repo root**: the harness resolves its ROM and
  data paths relative to the working directory and aborts with a core dump from anywhere else (six runs lost to
  this in a scratch dir before the cwd was the suspect).
- **The corpus is not a gate row, so it rots silently.** Re-run one file at the *start* of any corpus work; a rig
  that stopped running after some unrelated landing is invisible to `validate.sh`. If you find it broken, that is its
  own ticket (with the mechanism above) and it blocks the corpus ticket — say so on the ticket instead of debugging
  families against a silent oracle.
- **The folds are the risk, and the re-bless has a fixed shape.** A corrected instruction that EmuTOS itself
  executes moves `run.stream` and `run.digest`: that is the **owner's re-bless**, never a session's. Run the boot
  after each family (800 frames if the desktop matters, 300 for the pair alone) and say whether the folds moved
  and why; if they did, stop and ask. **While the blessing is pending, commit the port locally and do not push**
  — the work is preserved, `origin` still states the old pair, and a session never lands an unblessed pair; push
  on the owner's answer, in the same session or the next. When the owner blesses: **re-run `boot --frames 800` on the exact tree you are
  about to record** (a blessing names specific hashes, so re-confirm they reproduce), push the port commit, then
  land a separate `docs:` commit that writes the pair into `FINDINGS.md`'s row and its "gate as it stands" block,
  `design/cpu-scripts.md` and the next handoff — one commit for the change and one for the record keeps the pair
  attributable. **The pair also lives in the gate**: `scripts/cpu-shape-check.sh`'s
  `EXPECT_DIGEST`/`EXPECT_STREAM` fail the folds row until they carry the new prefix, and the design docs, the
  ADR's status line and its falsifier quote it too — search the old hex across `*.md` and `*.sh` and update
  every hit before running the gate. **Say which trace moved**: a *date* change (a read issued elsewhere, a wire
  gaining records) moves `run.digest` **and** `run.stream`; splitting one wait into per-state waits moves
  `run.digest` alone with `run.stream` byte-identical (the fold counts batches, empty or not); a data change
  moves the corpus first, and that one is a bug. The desktop frame and the pops a frame staying put proves the
  tree still behaves.
  Quote the replaced pair as history; never present the previous pair as current.
- **Landing a batch is four artifacts, and the fourth is the one a session forgets.** Beside the port
  commit and the `docs:` commit (FINDINGS row + "gate as it stands" block + `design/cpu-scripts.md`),
  two things go into the tree before the next batch, and the owner asks for both by name:
  (1) **the reading in the handler that implements it** — a doc comment carrying the class's own state
  names, the registers that drive the loop, and the access count as *one worked record* ("n longs =
  `2n + 1` data reads + `popm1` + `popm6` = 13 accesses = 52 clocks on the five-register record"), plus
  an inline comment at each fault return ("`popm6` never runs → the register keeps its value") and at
  each register write that happens exactly once. `git log` and a handoff do not show it to the next
  reader standing in the function; a doc comment does. Keep it comments-only and re-run the family after
  the edit — same verdicts is the proof it was comments-only;
  (2) **the next handoff with the next batch's measured baseline**, never the previous handoff's table:
  one `--failures` + one `--clocks` run per candidate file (8 files ≈ 10 s), then the per-file row
  (state, exact-clock, residue distribution by shape) plus the order to take them in. A prediction
  inherited from the last handoff is a hypothesis — this batch's was wrong about *which* column carries
  the work on two of three families. **Verify its three kinds of claim before acting on any of them**,
  because each is prose a previous session wrote: (a) *the tree state* — `git log` plus a corpus run;
  (b) *the ticket state* — `gh issue view <n> --json state`, because "the next work" is often already
  closed or refused by the owner; (c) *an encoding or an artifact it names* — derive mode and register
  from the opcode (`(op >> 3) & 7`, `op & 7`) instead of trusting a written mnemonic, `grep` the `.lst`
  for the encoding, and `git merge-base --is-ancestor <commit> main` for anything called "already
  landed" (a vendored set can sit on an unmerged branch, and every number cited from it is then
  unreadable on the tree you are measuring). **Report the next-work list with its depth**: one line per
  item is a shrug — say what the item *is*, what it buys, and what blocks it, or the owner will ask
  "explain each of these with more context" and the turn is spent twice.
- **The silicon-shape campaign's verdict is one command: `bash scripts/cpu-shape-check.sh <parent-commit>`** (#84,
  `design/cpu-silicon.md` §7). Four rows — MAME both columns, the blessed fold pair, three interleaved G8 pairs,
  and the interface row — exit 0 only if all hold, ~1 m 40 s, under `runs/<stamp>/shape/`. Quote its table rather
  than hand-running the commands. Two things it settled that a hand run gets wrong: **CLK is no longer a grading
  row** (retired 2026-11-21 — MAME is the truth for state *and* clocks, its 127 files carry every CLK form plus
  ILLEGAL_LINEA/LINEF and STOP, and a gate at 100 % turns any change red; the corpus and its analyses stay), and
  **G8's datum is the median of the three pair deltas**, every delta printed — on byte-identical cores one run
  measured +0.42 / +0.28 / +0.20 ms, so "every pair" fails a tree with no change in it — **and the absolute
  10 ms budget is judged on the median frame for the same reason** (one pair read 10.28 ms against a 9.04 ms
  median). The interface row catches
  the trap that cost a session: `tools/cpu-dissection` links `src/TheST.Machine/Cpu/*.cs` by glob because the CPU
  is six partial files since #78 — a bench that lists files explicitly prices another core and silently stops
  building (its `Folded` variant is now the sources unchanged; A − D was spent by #62).
- **The instruction bodies are grouped by microcode block, not by manual family — so renaming them to family
  names does not work, and `Decode` must be *gathered*, not written from the manual.** The reuse units are: the
  immediate/bit block (line 0: one immediate or bit number, then one EA read-modify-write, operation in bits
  11-8), the single-operand RMW (line 4 → `SingleOperand*Async`), the fixed-shape 4E4x–4E7x block (`MiscAsync`,
  no EA at all), the quick/condition block (line 5), source→ALU→destination (lines 8/9/B/C/D), and the shift
  block (line E). Two families share one body across lines (`ExchangeArithmeticAsync` = ADDX/SUBX, `BcdAsync` =
  ABCD/SBCD); two families in one block share nothing (ABCD and BTST). A 1:1 rename makes three names
  unreadable — `Line4Async` would read `NegxClrNegNotNbcdSwapExtPeaTstTasChkLeaMoveFromSrMoveToSrMoveToCcrMovemAsync`
  (76 chars), `MiscAsync` 56, `Line0Async` 44 — while eleven of twenty stay ≤ 23, i.e. the cost is concentrated
  in three methods. The rule that follows: **a function name carries at most one family** (two is tolerable,
  `MoveMoveaAsync`), past two the function must be split into a dispatcher + one body per family.
  **And the decode table already exists scattered**: ~38 ad-hoc predicates (12 distinct `IsScripted*` across
  Instructions/Scripts/Timing plus ~26 inline `(op & mask) == value` tests). A `Decode` written from the manual
  guesses the boundaries this code settles explicitly — a from-the-manual first pass scored **143 885 / 312 500**
  against the corpus, failing exactly on the lumped cases (`0x0F00` bit-op masks claiming line C's `ABCD`,
  `PEA`'s `(0xFFC0, 0x4840)` swallowing SWAP/EXT/MOVEM, `CHK`/`LEA` as `0xFFC0` not `0xF1FF`, and the shifts /
  `DBcc`/`MOVEP`/line-5 sizes absent). Transcribe the existing conditions — `ExecuteAsync`'s `switch (op >> 12)`
  and each line handler's inner switches are 100 % green on both columns, so any disagreement with them is a bug
  in `Decode` by construction — and witness each family with `cpu-surface --families <dir>`: every record whose
  own name carries its file's mnemonic must decode to that family (`ADDQ` inside `ADD.b` is excluded by that
  filter rather than by a rule). The full encoding map — where the operation field sits per line, the 78
  families by line/operation/group name, the three control layers, and the manual-versus-code vocabulary
  clash (`Family` = 78, `Line` = the 16 blocks) — is `references/decode-encoding.md`.
- **The core's files are the die's blocks, and the surface is frozen** (`design/cpu-bottom-up.md`). Storage
  and nothing else is `M68000.Registers.cs` (register file, `IRC`/`IR`/`IRD`, `ALUO`/`ALUB`/`ALUE`/`ISR`/width,
  `DBIN`, the pad buffers `AOB`/`DOB`/`DT`, and the latches `SSW`/`INL`/`TPEND`); `Set16H`/`Set16L` are the only
  legal half-writes. The two control stores' vocabulary is `M68000.ControlStores.cs`, the decode is
  `M68000.Decode.cs` (`Line` = the opcode's top nibble, the A1 PLA's coarsest term; then `Family` = the
  manual's 78, one `public static bool` each, named with the manual's word). `cpu-surface` is **frozen in
  `validate.sh`** — every public member must be one ADR 0015 §1 names, and the freeze catches a rename nothing
  else sees. **`AU` and `PC` are one cell today (`_pc` was renamed `_au`), and the owner's ruling is to split
  them**: `PC` becomes a register-file entry and `AU` the adder (its `AUH`/`AUL` halves, the constants ROM).
  Take the split **before** making `AU` the `MUL`/`DIV` loop counter — the die's counter is the adder, and on
  today's tree that cell *is* the program counter, so counting in it clobbers `PC` and hangs the core.
  **The split's first half is landed** (`_pc` is the register file's `PC`, `_fetchStateAddress` gone) **and
  so is the stream's position**: `_fetchPointer` is deleted and the position is *derived* —
  `FetchPosition` is `_au + (_ircValid ? 4 : 2)`, and `_au` itself when `_queueShort || _freePop`, because a
  read fills the slot behind the queue's head while a free pop (a control transfer's extension word) and the
  absolute-long pair consume a word with no read behind it; `BoundaryPosition` is the boundary's own state
  (`_au + 4`, `_au + 6` on the seam's `STOP` rollback), where the closing shift moved `_irc` into the head
  without moving `_au`. The fetch states write the die's `m_pc = m_au` as `CopyAuToPc(FetchPosition)` — the
  value handed in, because the adder is the *consumption* counter and the bare copy costs **4 277 records,
  all on `ram`**. What remains of the split is only that: the adder itself holding an access, and with it
  `ConsumedAddress` (`_au − 2`, the PC-relative base — writing it as `_pc` costs **604 records over 13 files**,
  the shapes that prefetch before they compute the operand) and `AUH`/`AUL` (`B2`). `_au` still carries the
  head, the loops' counter/restore (`AuEqualsConsumed`) and the odd-PC test.
  **A frame default is a state, not an expression** — a fetch fault at the stream's own read saves `_au`, a
  fault inside `RefillAsync` saves `_pc` (`b` commits the target to `m_pc` only after both reads land:
  RTS/RTR/RTE's odd return address, 3 066 records), and `DBcc`'s taken branch declares `_au` (its `dbcc3` is
  itself `m_pc = m_au`, 632 records) — and getting one wrong shows on **`ram` alone**, the register and clock
  columns staying green. **And the pop refill is not a fetch state.** `FetchAsync`'s refill is the one of
  `FetchPosition`'s four read sites that does not write `_pc`; writing it there (with the data-fault default
  moved to the consumption head or its address) moves **15 552 framed records, all `ram`, all −2, all group-0
  read faults** — on each the corpus wants the queue head's address (`_at` = `_au − 2`) where `_pc` is a word
  past it. Our lazy queue issues at consumption time what the die issues in an extension-word pop (`adsl1`,
  `aixw2`), a state with no `m_pc = m_au`, so `_pc` keeps the fetch-state discipline — do not re-run that
  inversion. **And measure any state-copy before writing it** with a per-site probe,
  not the `--clocks` dump: the dump samples boundaries and exception entries, a `#if DEBUG` tally in the core
  gives the value at every read and costs 10 s a corpus run (it is `public` while it lives — a separate
  harness assembly cannot see `internal` — so it never gets committed).
- **A multi-slice refactor of this core runs on two documents in the tree, not on prose in a session.** Keep a
  plan with the level map, the slice order and a `- [ ]` progress line per slice in `design/` (plus a handoff
  with the tree state, the measured baseline and the exact next action), and tick a line in the same commit as
  the slice it belongs to. The session will be compacted mid-campaign and the owner asks for the whole campaign
  in one thread; the plan file is what makes the next session resume at the right slice with the right gate
  instead of re-deriving the order. Two further rules for these campaigns, both the owner's: **plan first**, and
  **commit + push each slice only after `scripts/cpu-shape-check.sh <parent>` is green** — a red row means the
  slice is not landed. The gate runs on the **uncommitted** tree with `<parent>` = the commit it sits on: it
  builds the parent from that commit and the child from the working tree, so the A/B is real while `HEAD` has
  not moved, and its table labelling the child "working tree dirty" is expected, not a refusal.
  **A handoff's "one package" is a queue of slices, and only the narrowest one goes in at a time**: take the
  slice whose verdict the corpus can give, land it green, and write the blocking measurement — with its
  record count — into the plan's progress line. A package attempted whole is how a session ends with a
  reverted tree, and the measured blocker is worth more to the next session than a half-rename.
- **A decoder is witnessed by the corpus names, never by reading it**: `cpu-surface --families <dir>` grades
  every record whose name carries its file's mnemonic against `Decode` (303 606/303 606 on the MAME corpus).
  Two traps it encodes: a file carries its neighbours (ADDQ/ADDI inside `ADD.b`, CMPM inside `CMP.b`), so the
  filter names the exclusions rather than deriving them from the decoder; and an opmode of 3/7 reads as
  `size == 3`, which is why `SUBA`/`ADDA`/`CMPA`/`MULU`/`MULS` are tested with `size == 3`, not `size != 3`.
- **A divergence the corpus cannot see is a ticket, not a batch fix.** MAME's decode table is the
  authority on legality: if `research/vendor/m68000/m68000.lst` has no entry for an encoding this core
  executes (or the reverse), neither corpus carries a record, so nothing grades the change and the only
  witness is the boot. Write the datum in the handler's comment so a reader hits it, and file it — do not
  "clean up" a green family on the way past. (`microcode-walk.py` prints nothing for an opcode with no
  entry; `grep` the `.lst` for the opcode to confirm.)

## 7. Pitfalls

- **A failed Release build leaves the previous binary, and the gate row then lies.** `scripts/validate.sh`
  writes `runs/<stamp>/build-Release.log`; when that build fails (this happened when the harness referenced a
  `#if DEBUG` member) the `g8` row still prints a complete, plausible state — from `main`'s binary. **The same
  trap bites a corpus run**: the harness executes whatever binary is on disk, so after a failed `dotnet build`
  `cpu-tests` reports the previous build's percentages just as plausibly. Read `Error(s)` before believing any
  number, not only before a G8 figure. Read the
  build log before believing a G8 figure or a desktop frame. Split the instrument boundary by **consumer**, not by taste: per-record instruments
   (the three observers, the `LastFrame*` columns) go behind `#if DEBUG`, but a counter the **Release** build
   reads — the boot prints `Instructions`/`BusCycles` and `cpu-dissection` reaches them by reflection — stays
   unconditional, because a blanket `#if DEBUG` breaks the Release build and the tool and neither break reaches
   the corpus. A per-cycle observer in Release still costs the very thing G8 measures. **And never edit the tree while `validate.sh` runs** — it builds from the
  working tree, so an edit landing mid-run can fail the Release build (and then the row reports the previous
  binary) or hand the gate a tree that is not the one you committed; land the edits, then run the gate on that
  exact tree and quote that stamp.
- **Create the ticket before you name its number, then wire it into the map's table.** A tracker shared with
  other projects hands out the ids it is ready to hand out, not the ones the prose predicts, so a design document
  that names a to-be-created number sends the next reader to someone else's ticket. Create it, use the number it
  returns, and add the row where the campaign lists its tickets.
- **This domain's own vocabulary trips the shell filter.** Prose about `HALT`, `RESET`, a reboot or a
  shutdown, when it rides *inside* a shell command (a heredoc, a `printf` into `gh issue comment -`), can be
  refused by the runtime's command guard before anything runs — and the refusal never mentions the wording.
  Write the text to a file with `write_file` and hand the path over (`gh issue comment --body-file <path>`,
  `gh issue create --body-file`), or keep the sentence out of the command. Ticket bodies and comments are long
  enough that the file form is the better habit anyway.
- **Commit each verified slice immediately — this working tree can be shared with another agent session.** A
  concurrent session running `git commit -a` sweeps whatever is uncommitted into *its* commit, under a subject
  about something else; by the time `git log -- <path>` shows your file under a stranger's message, the history
  is already pushed and cannot be rewritten. One commit per green gate, `git add` exact paths, verify with
  `git log --oneline -2 -- <path>`, and keep long uncommitted work in a `git worktree`.
- **The comments pass (`#94`) has two traps before you try it.** A prose `ours:` paragraph *below* a state block
  is read as that block's continuation, so the charge becomes a word out of the prose — put it above, with a bare
  `//` between. And a listing is not always one file's: the closing pair `b`/`mmrw3` is the loop's, commented
  once in `M68000.cs`'s `CloseInstructionAsync`/`TrailingFetchAsync` (every fall-through ends on it and NOP's
  whole listing is it), so `--complete` reads the files it is given **in the order given** —
  `--complete --op 4e90 M68000.Scripts.cs M68000.cs` — and its order test reads only states named once, because
  a shared state (`mmrw3` is also a script's own read) is named where that other method needs it.
  The format, the guard and the models are `design/cpu-scripts.md`'s last section; `references/comments-pass.md`
  carries the pass's procedure — convert as many methods per session as the context holds, one commit each,
  and write the handoff when the context is full — not once per instruction.
  (`EXG`/`LINK`/`SWAP`/`NBCD` are handlers, not scripts), the pair-ending listings and the grading order are
  `references/comments-pass.md`.
- **A BCD lane mistake looks like a corpus bug.** `ABCD/SBCD -(An),-(An)` read *words*; the operand is the low
  byte. For the `m68000` corpus a byte's lane is `UDS && !LDS → data>>8`, otherwise `data & 0xFF`; CLK puts a
  byte in the low lane regardless. Mixing them turns every BCD case into a false disagreement — it did, twice,
  in the session that wrote `tests/680x0/CONFORMANCE.md`.
- **T1 says both corpora are right on BCD** (`CONFORMANCE.md` §3: 24 195/24 195 CLK, 5 135/5 135 MAME on every
  judgeable case), so a BCD disagreement is almost always the reading of the case, not the core.
- **A frame rule taken from the corpus alone can hang the boot, and TRAP is why — key it on the vector.** Setting the
  group-1/2 frame's saved `pc` to the instruction's own address — which is what MAME measures for the
  illegal lines, the privilege violations, `ANDI`/`ORI`/`EORI`-to-`SR`, `MOVE`-to/from-`USP` and `RESET` —
  greens 11 114 records (256 983 → 268 097) and makes `boot --frames 800` print
  `frames_to_desktop=never` with 5 223 pops a frame against 35 811: EmuTOS runs on `TRAP`, and `RTE`
  through the changed frame returns to the `TRAP` itself. **Take a frame rule per family and run the boot
  before landing it** — a corpus win is not a machine win.
- **Frames are contested between the corpora, and neither is hardware** (CLK `opcode+2` vs MAME `opcode+6` for
  the same MOVEM fault). MAME is the truth now, so the core's frame code follows `opcode+6` and the CLK figure is
  the one that moves — re-derive the rows written to CLK's convention rather than calling the new red records a
  regression. Never "fix" a frame to whichever corpus was run last: name the source and its tier.

- **A `ValueLog` index is valid only inside one resume.** Keep a sequence number (`IndexAfter(seq)`, `HasRecordAfter`)
  across a suspension: a saved index names a different record once retention has moved the head, and the scan then
  restarts at the window's oldest record and loses records with no assertion on the machine at all.
- **Do not soften a row, and do not edit a test vector.** A test that is "obviously wrong" is the oracle disagreeing
  with the manual — that goes in `FINDINGS.md` as a convention, not in the data.
- **A landed change can break the tool that generated it.** If the corpus' own tooling is generated (a folded or
  no-`async` variant), regenerate and rerun in the same session as the landing; a stale binary reports the previous
  build's verdict.
- **An assertion inside the core is unobserved, so a check must be driven from something that awaits it.**
  `Run()` is launched fire-and-forget, so a throw or a `Debug.Assert` inside a script dies with the coroutine and
  the corpus still prints its numbers. There is **no unit-test project** here: structural tests live in the
  harness (`cpu-surface`, the freeze row), per-record instruments sit behind `#if DEBUG`, and `SingleStep`
  (`Load`/`StepAsync`) is the only path the harness awaits record by record. Decide where a new check is
  observed **before** writing it — a check nobody reads is not a test.
- **The state above the code: one comment line per micro-state, in the listing's own syntax.** A block of a
  script that expands a micro-state carries `// <name> (<addr>)  <statements>  <charge>` above it, pasted
  from `scripts/microcode-walk.py --op <opcode>` (the name, the micro-ROM address, the statements with the
  bus boilerplate elided to `read at <expr>` / `write <expr> at <expr>`, and the state's own 2 or 4 clocks
  right-aligned at column 104). Two states fused into one helper are named together (`b (363) + mmrw3
  (34c)`); a deliberate difference from the listing says `//   ours:` and the measurement that required it.
  `RESET`'s script is the worked example, the closing pair's block (`CloseInstructionAsync`) the model for what
  the code does *not* contain (the `_own` charge, `b`'s `m_au = m_au + 2` — the loop's `AuPlusTwo`), and the
  format's checker is `scripts/comment-states.py`
  (`--check FILE...`, `--complete --op <opcode> FILE...`, `--index`; it builds the state index from
  `m68000gen.py sdf`, and a **charge of 0** is legitimate for a state the listing leaves uncharged —
  `rorw1`, `adrw1`, `cpmm1`). It joins the gate when it reads 0 core-wide (#94). The comments-only
  guard must drop the diff's own metadata first, or it can never read empty:
  `git diff -U0 -- 'src/TheST.Machine/Cpu/*.cs' | grep -E '^[+-]' | grep -vE '^(\+\+\+|---) ' | grep -vE '^[+-][[:space:]]*//'`. A comment transcribed from
  memory lies — paste it, and let the address make it checkable. When *explaining* an instruction to the owner,
  lead with that aligned block and put the C# after it — the same pairing the comments make permanent.
- **A wire the instruction drives is a level, and its edges are the part's instants.** `Wires.Reset` carries
  `RESET`'s `/RESET` output (1 = asserted) as one record per edge — `rset2` asserts, `rset5` releases, **124
  clocks apart** — and `Pins.RESET` reads the level rather than a constant. The states wait 2 clocks each
  (62 waits for the 31 passes) so a peripheral can be woken inside the hold; the *power-on* pull stays the
  host's, because the reset network is not a chip. A line like that moves both folds the first time it exists —
  a record at a new instant is a new batch — so the desktop frame and the pops a frame staying put is the proof
  it is not a behaviour change, and a peripheral reacts only when one is wired to the line.
- **A value two consumers derive gets one home.** The access class's function code was spelled twice — in the
  control word the pins decode and again in the `#if DEBUG` trace observer — so a bus and its instrument could
  drift with nothing to notice. Make the class a value (`AccessClass` = MAME's
  `SSW_DATA`/`SSW_PROGRAM`/`SSW_CPU`, passed by the access rather than two booleans threaded through the leaf)
  and give the derivation one method (`FunctionCode(access)`); the same move turns `ComposeSsw`'s
  `0x10 | 0x02` into the die's bit names (`SswData`/`SswProgram`/`SswRead`/`SswSupervisor`/`SswNested`).
- **A plain `MOVE` is not like the other instructions, and the difference is load-bearing.** Its long store into
  `-(An)` and its `(An)+` post-increment both land **with** the access; every other instruction's land **before** it,
  so a fault keeps their increment and drops its. The gate is `(_opcode >> 12) is 1 or 2 or 3`, in `WriteEaAsync`
  and `SettlePostIncrement`. Widening it to "any store" regresses rows 42 and 44 and looks like two unrelated bugs.
- **A reverted experiment does not restore itself.** A helper script that reverts a hunk, builds and boots exits on
  the build error and leaves the tree reverted — and `if (false) …` is `CS0162 Unreachable code`, an **error** in
  this repo, not a warning. Save a copy, revert by explicit path, and re-count the corpus before believing the tree
  is whole.
- **The queue's boundary convention is `_au`, and a control transfer must set it to the target, not past it.**
  The field is `_au` since the storage rename (`AU` *is* the hardware's program counter; older prose here and in
  doc comments said `_pc`, and MAME's `m_pc` is that same register). During an instruction `_au` is the address
  *past the last consumed word* (the decode step advances it by 2, each extension pop by 2), so `_au − 2` is still
  every PC-relative base and the opcode address. At a boundary it is the address of the *next* opcode, which is why
  a refill sets `_au = target` and lets the decode step advance it. Get
  this wrong and every `(d16,PC)`/branch displacement is off by two while the decode still looks healthy.
- **A single-step seam must carry the queue.** The corpus hands a test the two words the reference has already
  fetched (`initial.prefetch`), so `SingleStepState` carries them and `Load` seeds `IRD`/`IR` from them; a harness
  that forgets them, or a `Load` that leaves the second slot "valid", reports every record off by one word — and the
  failure looks like a decode bug in the first instruction of every file.
- **Two records that look identical can want different read counts.** Before generalising a fetch-placement rule,
  tabulate the family with `--reads` and look for two records with the same (opcode, EA shape, destination parity)
  and different counts. If they exist, the placement is per opcode and the rule is a script, not a clause — and
  the script to copy is the reference's own state machine (CLK `Processors/68000/Implementation`,
  `MOVE_bw_AbsoluteLong_prefetch_first` for the shape `#74` landed).
- **A timing charge is a wait, not an `Advance`.** `Advance` moves only the chip's clock; machine time does not
  follow, the CPU runs ahead of the beam and the timers, the boot loses its counts and never reaches the
  desktop. Shape: `await WaitFor(Clock + n × TicksPerCpuClock, Wires.Answer, _answerSeq)` — nothing is pending
  on the answer wire at the charge, so only the date fires and the machine advances in step.
- **Reset every per-instruction flag at all three boundaries** — `Run`, `SingleStep.Load` **and**
  `SingleStep.StepAsync`. One missed in `StepAsync` passes the sample (24 records a file) and fails the whole
  corpus, because the stale value accumulates record after record.
- **Validate a per-family table keyed on opcode bits per family, with a per-record dump.** A shift's *kind*
  field (RO is kind 3) and `RESET`'s `0x4E70` matching `TRAP`'s `(op & 0xFFC0) == 0x4E40` each silently cost
  a whole family and neither showed on the sample: test specific opcodes before masked ranges, and read the
  dump's delta per file — a constant offset is one wrong constant, a scatter is one wrong expression.
- **A `--clocks` diff is read per column and per record kind, never per line.** One line holds two samples
  (the boundary's on a clean record, the exception entry's on a faulted one), and the closing shift runs even
  when `_busFaulted`, so an entry sample is the *boundary's* state. An intended instrument change shows only on
  faulted records; **one word moved on a clean record is a real regression**.

## 8. Rules the corpus settled that a manual reader gets wrong

Each of them is one line in `references/manual-vs-corpus.md` (`ADDX`'s `Z`, a shift's `C`/`X` past the width,
`CMPI` leaving `X`, a divide's overflow clearing `C`, `MOVE`'s flags set before its store, `BTST Dn,#data` as a
byte test, the saved `SR` mask `0xA71F`, the group-0 frame's `SSW` class and why latching it per cycle costs
50 752 records, `INL`/`TPEND`, `JSR` to an odd target, `(xxx).l`'s full 32 bits, and `-(An)`'s three step
rules). Read it before proposing a flag or a frame rule from the manual: most of these are silent in the boot
and only the corpus reports them.
