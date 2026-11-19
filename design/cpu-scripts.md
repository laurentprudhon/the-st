# The core's instruction scripts — the microcode expanded, not interpreted

Companion to ticket **[#78](https://github.com/laurentprudhon/the-st/issues/78)**. The owner's ruling
(2026-11-19): the 68000 core must stay **human readable**, so it does **not** interpret the microcode
and nanocode tables at run time and carries no microcode ROM. The tables are the *specification*; each
instruction is their **direct expansion** — ordinary C#, factored, in the microcode's own order.

Code: [`M68000.Micro.cs`](../src/TheST.Machine/Cpu/M68000.Micro.cs) (the vocabulary),
[`M68000.Scripts.cs`](../src/TheST.Machine/Cpu/M68000.Scripts.cs) (the expanded scripts).
Specification: `research/vendor/m68000/` (MAME, BSD-3-Clause), read with
`scripts/microcode-walk.py --op …` or generated as C++ (below).

## 1. Why expansion and not a table interpreter

A table-driven core would be MAME's model ported whole: 1 024 micro-words, 1 024 nanocode entries,
1 528 opcode patterns, a decode loop and an ALU table. It would reach 100 % of the gate, and nobody
could read an instruction out of it — the project's own rule (simplicity and understandability beat
feature count) decides against it.

Expansion keeps the two things a reader needs and loses nothing on the gate:

| what the microcode decides | how a script states it |
|---|---|
| the **order** of the bus cycles inside an instruction | the statement order |
| **where the idle clocks sit** (2 per non-bus state) | an explicit `IdleAsync()` at that point |
| **when a register write commits** (`pinl3`, `pdcl2`) | a `SetA(...)` where the script puts it |
| **how deep the frame's saved `pc` is** (`m_pc = m_au`) | `BeginFetchState()` in the states that write `m_pc` |
| **the frame's status word** (`m_base_ssw`) | `ReadOperandProgramAsync` where the read is a program read |
| the clocks | the bus states' 4 (the memory's answer) + the idle states' 2 |

The cost is authorship, and it is paid per *state block*, not per family — see §4.

## 2. The vocabulary

One statement per elementary operation of the nanocode. The right-hand column is what the generator
emits for the same state, so a transcription can be checked line by line.

| C# (`M68000.Micro.cs`) | microcode / nanocode | note |
|---|---|---|
| `FetchAsync()` | an extension word out of the queue | ADR 0009 §4's queue, unchanged |
| `TrailingFetchAsync()` | `roml1`, `morl1` | the instruction's one closing read, once |
| `BeginFetchState()` | `m_pc = m_au` | a state that *begins* a prefetch moves the frame's counter |
| `ReadOperandProgramAsync(a)` | `m_base_ssw = SSW_PROGRAM | SSW_R` | the PC-relative shapes read the operand out of the stream |
| `ReadWordAsync`, `WriteWordAsync` | `m_program.read/write_interruptible` | a bus state: 4 clocks, the memory's |
| `IdleAsync(2)` | `istep` (`m_icount -= 2`) | a state with nothing on the bus |
| `AluOr`, `AluAnd`, `AluExt` | `alu_or`, `alu_and`, `alu_ext` | the ALU, at `_aluWidth` |
| `SetNzvc`, `SetNzvcU` | `sr_nzvc`, `sr_nzvc_u` | `_u`: Z only ever *cleared* (the long operation's second word) |
| `_aluo`, `_alub`, `_dbin`, `_aluWidth` | `m_aluo`, `m_alub`, `m_dbin`, `i=.l/.w/.b` | chip state, so they survive across states |
| `Set16L`, `Set16H`, `High16`, `Ext32`, `Merge16_32` | same names | the 16-bit datapath's helpers |

Three conventions keep it readable:

1. **MAME's state name stays in the comment** (`// 29d morl1`). The comment is the anchor into
   `m68000gen.py`; the code is the expansion.
2. **No clause** — no `if (plainMove)`, no `MoveSourceNeedsNoBusCycle`. Where the old handlers had a
   class rule, a script has the states of *that* shape, which is the whole point (#74's lesson: a
   clause that fits one opcode breaks two hundred).
3. **One method per state block**, named after the states it expands (`ReadLongOperandAsync` =
   `adrl1`/`adrl2`, `OrIntoMemoryLongAsync` = `morl1`/`morl2`/`morw2`), so the next family calls it.

## 3. The recipe for one family

```bash
# 1. the script, as C++ — the transcription source
python3 research/vendor/m68000/m68000gen.py sdf research/vendor/m68000/m68000.lst /tmp/gen.cpp
grep -n "or_l_dd_ais_df" /tmp/gen.cpp        # then read the function

# 2. the same opcode, as a listing with the ALU lines (the faster read)
python3 scripts/microcode-walk.py --op 8190

# 3. the corpus's own expectation for the shape: one record's cycle list
#    (tests/680x0/mame/<FILE>.json: `transactions`, `length`)
```

Then, in `M68000.Scripts.cs`: one method per shape, from the generated handler, counting **reads,
writes and idle states**; `IsScripted<Family>` returns true so `M68000.Timing.cs` charges nothing;
the decode routes the family to the script. Verify with

```bash
tools/validation-harness/bin/Debug/net11.0/validation-harness cpu-tests --corpus mame --file OR.l \
    --failures /tmp/f.txt --out /tmp/o.txt
tools/validation-harness/bin/Debug/net11.0/validation-harness cpu-tests --corpus mame --dir <dir-with-one-file> --trace N
```

`--trace N` prints the reference's cycle list beside ours, row by row: the first differing row says
which state is wrong, and `state:` says which field. A whole-file run is ~1 s; the full gate 10 s.

**A family is done** when its file is green on the MAME gate, the sample (`--dir tests/680x0/v1`) did
not move, and the boot still reaches the desktop (§5).

## 4. The ledger, and what a family costs

`IsScripted*` in `M68000.Scripts.cs` is the ledger of ported families.

| family | shapes | lines | MAME records | result |
|---|---|---|---|---|
| OR.l (`or.l <ea>,Dn`, `or.l Dn,<ea>`) | 18 | ~330 (incl. the shared blocks) | 2 340 in `OR.l` | **2 340 / 2 340** (was 1 704) |

The 18 shapes are 18 short methods over **five** shared blocks (the operand pair, the register-destination
tail, the memory-destination tail, the program-status read, and the two idle charges), so the *next*
two-operand long family (ADD, AND, SUB, EOR) is a tail and a dispatch, not 330 lines: the blocks are
named for the micro-states and are family-independent. That is the number #78 asked for.

## 5. The two things that stay open, and why

- **The aborted cycle.** An address error's `length` includes the 4 clocks of the cycle the hardware
  abandons (`re` in the corpus's `transactions`); this core raises the fault *instead of* the cycle, so
  a faulting record is 4 clocks short. It is one row's worth of work and it is not in a script — it is
  in `Cycle`'s odd-address path. (Pre-existing: it fails identically on `main`.)
- **The BCD/`ORI` families not yet ported.** Their faults still go through the old handlers' frame
  conventions; the 65 `ORI` records still red in `OR.l`'s file are theirs, not the script's — the same
  records fail on `main`.

## 6. Verification of the first family (2026-11-19)

| measure | `main` | OR.l scripted |
|---|---|---|
| MAME gate, whole corpus | 291 934 / 317 500 | **292 570 / 317 500** |
| MAME gate, `OR.l` file | 1 799 / 2 500 | **2 435 / 2 500** |
| `OR.l`, line-8 (`or.l`) only | 1 704 / 2 340 | **2 340 / 2 340** |
| exact-clock, whole corpus | 213 891 (67.4 %) | **214 492 (67.6 %)** |
| CLK beside it | 852 059 / 1 000 060 | see `tests/680x0/FINDINGS.md` row 66 |

No record that passed on `main` fails here: the two failure sets were diffed record by record
(`--failures` on the same directory).
