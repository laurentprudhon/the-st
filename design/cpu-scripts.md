# The core's instruction scripts — the microcode expanded, not interpreted

Companion to ticket **[#78](https://github.com/laurentprudhon/the-st/issues/78)** (the convention) and
**[#79](https://github.com/laurentprudhon/the-st/issues/79)** (the other two-operand longs, ported with
it). The owner's ruling
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
| `ReadOperandProgramAsync(a)` | `m_base_ssw = SSW_PROGRAM \| SSW_R` | the PC-relative shapes read the operand out of the stream |
| `ReadWordAsync`, `WriteWordAsync` | `m_program.read/write_interruptible` | a bus state: 4 clocks, the memory's |
| `IdleAsync(2)` | `istep` (`m_icount -= 2`) | a state with nothing on the bus |
| `AluLongOp(a, b, AluLong.…)` | `alu_or`, `alu_and`, `alu_eor`, `alu_add` + `alu_addc`, `alu_sub` + `alu_subc` | one 32-bit operation; the chip's two word ALUs are the same result and flags (§1 of the code) |
| `SetLongFlags(…)` | `sr_nzvc` + `sr_nzvc_u`, `sr_xnzvc` + `sr_xnzvc_u` | `extend: false` for a comparison, which leaves X alone |
| `_aluo`, `_alub`, `_dbin`, `_aluWidth` | `m_aluo`, `m_alub`, `m_dbin`, `i=.l/.w/.b` | chip state, so they survive across states |
| `Set16L`, `Set16H`, `High16`, `Low16`, `Ext32`, `Merge16_32` | same names | the 16-bit datapath's helpers |

Three conventions keep it readable:

1. **MAME's state name stays in the comment** (`// 29d morl1`). The comment is the anchor into
   `m68000gen.py`; the code is the expansion.
2. **No clause** — no `if (plainMove)`, no `MoveSourceNeedsNoBusCycle`. Where the old handlers had a
   class rule, a script has the states of *that* shape, which is the whole point (#74's lesson: a
   clause that fits one opcode breaks two hundred).
3. **One method per state block**, named after the states it expands (`ReadLongOperandAsync` =
   `adrl1`/`adrl2`, `AluIntoMemoryLongAsync` = `morl1`/`morl2`/`morw2`), so the next family calls it.

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

`IsScriptedAluLong` in `M68000.Scripts.cs` is the ledger of ported families; the single-operand
families are the exception — their ledger is `IsScriptedSingleOperand(op, size)` with one predicate per
size (`IsScriptedSingleOperandLong`, `IsScriptedSingleOperandWord` and `IsScriptedSingleOperandByte`).

| family | shapes | lines | MAME records | result |
|---|---|---|---|---|
| OR.l (`or.l <ea>,Dn`, `or.l Dn,<ea>`) | 18 | ~330 (incl. the shared blocks) | 2 340 in `OR.l` | **2 340 / 2 340** (was 1 704) |
| AND.l (`and.l` both directions) | 18 | + a dispatch | 2 371 in `AND.l` | **2 371 / 2 371** (was 1 795) |
| EOR.l (`eor.l Dn,<ea>`, one direction only) | 8 | + a dispatch | 2 238 in `EOR.l` | **2 238 / 2 238** (was 1 694) |
| ADD.l (`add.l`, incl. `ADD.l An,Dn`) | 19 | + a dispatch | 1 538 in `ADD.l` | **1 538 / 1 538** (was 1 160) |
| SUB.l (`sub.l`, incl. `SUB.l An,Dn`) | 19 | + a dispatch | 1 527 in `SUB.l` | **1 527 / 1 527** (was 1 150) |
| CMP.l (`cmp.l`, no write) | 12 | + a compare tail | 2 063 in `CMP.l` | **2 063 / 2 063** (was 1 631) |
| ADDA.l / SUBA.l / CMPA.l | 12 each | + a dispatch | 2 500 each | **2 500 / 2 500** each (was 1 982 / 1 975 / 1 955) |
| line 0's long immediates (`ori`/`andi`/`eori`/`addi`/`subi`/`cmpi .l`) | 8 × 6, one method | + `o#l1`/`o#w1` | 90–262 a file | **every record exact-clock**, and the six files 2 500 / 2 500 (except `CMP.l`'s 165) |
| line 5's long quicks (`addq.l`/`subq.l`) | 9 × 2, one method | + `raql1`/`maql1` | 872 (`ADD.l`), 858 (`SUB.l`) | **every record exact-clock** |
| `BSR` (both sizes) | 2 | ~40, one method | 2 500 in `BSR` | **2 500 / 2 500** (was 1 229), all exact-clock |
| `JMP` (seven addressing modes) | 7 | ~90, one method | 2 500 in `JMP` | **2 500 / 2 500** (was 1 589), all exact-clock |
| `UNLK` | 1 | ~20, one method | 2 500 in `UNLINK` | **2 500 / 2 500** (was 1 385), all exact-clock |
| `JSR` (seven addressing modes) | 7 | ~105, one method | 2 500 in `JSR` | **2 500 / 2 500**, **838 → 2 500** exact-clock |
| `RTS` / `RTR` / `RTE` | 1 each | 3 lines each (`ChargeAbortedReadAsync`) | 2 500 each | **2 500 / 2 500** each, 1 263 / 1 285 / 1 886 → **2 500** exact-clock |
| `NOT` / `NEG` / `NEGX` / `CLR` / `TST` `.l` | 8 each, **one** method | ~150 (the RMW block + two helpers) | 2 500 a file | **2 500 / 2 500** each (was 1 867 / 1 918 / 1 891 / 1 921 / 1 886), **every record exact-clock** |
| the same five `.w` | 8 each, **one** method | ~140 (the word block, `ReadWordEaAsync`) | 2 500 a file | **2 500 / 2 500** each (was 2 251 / 2 262 / 2 244 / 2 248 / 2 254), **every record exact-clock** |
| the same five `.b` | 8 each, **the same** method | + `size` 0 in the block and the step (`ReadSmallEaAsync`) | 2 500 a file | **2 500 / 2 500** each, and 12 500 exact-clock — `CLR.b` was the only one short (401 → 2 500) |

The control transfers are the first family whose **frame's saved counter** the script decides, and they
are also where the queue's lookahead shows: a transfer never refills the slot behind the word it took, so
its extension word costs no read (`JumpWordAsync(0)`), its own clocks lead (`_own` is only spent at the
closing fetch a transfer never runs), and the target's read is either a fetch state (`bsrw1`/`malw3`:
`m_pc` = the target, so an odd target's frame saves the target) or not (`bbci3`: `m_pc` keeps the
boundary's word, so `JMP` saves `opcode + 2` on every shape). That last distinction is
`FrameSavesOnFetch` — a script-declared override, because `_fetchPointer − 2` (the default, #76) is right
only for a fault inside an instruction's own trailing fetch.

`JSR` is the same tail with a push in the middle, and it is the family a **dead predicate** hid: its
shape row in `M68000.Timing.cs` was `(op & 0xFFC0) == 0x4E90`, and `0x4E90 & 0xFFC0` is `0x4E80`, so the
row could never fire and `_own` was 0 on every `JSR` while the state verdict was green. Its two facts a
reader will not guess: the target's read comes **before** the push (`jsra1`/`jsrd3`, so an odd target
pushes nothing and pays the aborting state's 4), and `m_pc` **advances past each consumed extension
word** (`jsrd2`/`jsrd3`), so the pushed return address and an odd target's frame are one number — the
counter past the last extension word (`opcode + 2` / `+4` / `+6`). `jsal2` is the single state where a
**queue read moves that counter** (`ReadQueueWordAsync` does not advance `_pc`, so `(xxx).l` needs an
explicit `_pc += 2`, or the push is a word low). The three returns needed no script at all: they were
green on state and exactly 4 short on only their faulting records, because the aborted cycle's 4 is
charged by `Cycle` and their shape rows carry none — `ChargeAbortedReadAsync` at every fault point.

The five **single-operand** families are the cheapest row in this table and the one a reader should study
first: `NOT`, `NEG`, `NEGX`, `CLR` and `TST` in size 2 are *one* script shape, because the microcode gives
them one (`nnml1`/`nnml2`/`morw2`, or `tsml1`/`tsml2` for `TST`, which writes nothing). Three facts:
`nnml1` is the state that both runs the ALU's low half and issues the closing prefetch, so the operand's
read, the fetch and the two writes are in that order (low word at `address + 2`, then high at `address`);
`CLR` **reads** the operand it zeroes, which is why it is the block and not a store; and the register form
is the same block with `set_16l` before the fetch and `set_16h` after it, ending on `roal4`'s 2 clocks
(`TST` ends on `tsrl2` and charges none). One 32-bit operation gives the result and the final flags
(`SingleOperandResult`/`SetSingleOperandFlags` — `NEG` is `SetSubFlags(operand, 0, …)`, `NEGX` the exchange
form, the other three a plain `nzvc` with `V`/`C` cleared). And the **predicate reads the operation field
as bits 11-8**, four bits wide: read as bits 11-9, `0x4180` looks like `NEGX.l` and `CHK` loses 896
records, because `CHK`'s own "size" field is `10` too.

The same five in size 1 are the subtlest block in this file: the word form is **not** the long
one with a mask. `adrw1` is an address latch with no bus cycle (one read for `(An)`), `pdcw1` begins a
fetch state before it steps `An` down, `pinw2` commits the post-increment **before** its read (so a
faulting `(An)+` word read leaves `An` stepped, where the long form's `pinl3` leaves it alone), and the
register form ends on `roaw2` with no closing idle. `ReadWordEaAsync` is that block; the ALU, the flags and
`SingleOperandResult` are shared, which is why the second size cost ~140 lines and not a second family.

Size 0 is the *same* block and it cost two conditionals, which is the port's one clean demonstration that
these tables are per-shape and not per-size: `adrw1`/`adrw2` … `nnmw1` + `morw2` are one script for the
byte and the word, and only two facts depend on the width — the access (`ReadByteAsync`/`WriteByteAsync`,
i.e. the lane the reference's `m_edb >>= 8` yields) and the EA's **unit step**, one byte except on `A7`
where the byte forms step by two (`pinw1`/`pdcw1`: `m_au = m_da[ry] + (ry < 15 ? 1 : 2)`). So
`SingleOperandWordAsync` became `SingleOperandWordOrByteAsync(op, size)` over
`ReadSmallEaAsync(mode, reg, size)`, and the register form writes `set_8` where the word writes
`set_16l`. The batch was a **clock** batch, not a state one: four of the five byte files were already
2 500 / 2 500 and exact-clock, and the single gap was `CLR` — its old handler only wrote, and the
reference reads the operand it zeroes, so every memory record was one access (4 clocks) short. The
lesson generalises: **an instruction that the microcode's ALU reads `m_dbin` in is a read on the bus**,
and a handler that computes from a constant is invisible until someone counts the cycles.

The 18 OR.l shapes are 18 short methods over **five** shared blocks (the operand pair, the
register-destination tail, the memory-destination tail, the program-status read, and the two idle
charges), so the *next* two-operand long family is a tail and a dispatch, not 330 lines: the blocks are
named for the micro-states and are family-independent. That is the number #78 asked for, and the
follow-up port measured it: AND, EOR, ADD and SUB cost **one line case and one `AluLong` value each**,
and the address-register forms one more tail (the same block, without the flag call).

## 5. The two things that stay open, and why

- **The aborted cycle — closed** (`#79b`, `#79c`). An address error's `length` includes the microcode's
  charge for the cycle the hardware abandons (`re` in MAME's `transactions`, AS never asserted). The core
  now charges it in `Cycle`'s odd-address path, and a *scripted* family pays the aborted read's own state
  as well (`ChargeAbortedReadAsync`, 4 more — `#79c`): the microcode charges the aborting state its 4 and
  then 4 more on the odd address, so an odd read is 8 clocks and an odd write 4. The shape table's rows
  already contain that 4 because they were measured on records that include it, which is why the extra 4
  lives in the scripts and dies with the table.
- **The immediate and quick families — the long forms closed**: `ORI`/`ANDI`/`EORI`/`ADDI`/`SUBI`/`CMPI`
  (line 0, `o#l1`/`o#w1`) and `ADDQ`/`SUBQ` (line 5, `raql1`/`maql1`), one method each
  (`ImmediateLongAsync`, `QuickLongAsync`), every one of their records exact-clock. The `.b`/`.w` sizes of
  both blocks are still measured by shape.

## 6. Verification

The first family (2026-11-19):

| measure | `main` | OR.l scripted |
|---|---|---|
| MAME gate, whole corpus | 291 934 / 317 500 | **292 570 / 317 500** |
| MAME gate, `OR.l` file | 1 799 / 2 500 | **2 435 / 2 500** |
| `OR.l`, line-8 (`or.l`) only | 1 704 / 2 340 | **2 340 / 2 340** |
| exact-clock, whole corpus | 213 891 (67.4 %) | **214 492 (67.6 %)** |
| CLK beside it | 852 059 / 1 000 060 | see `tests/680x0/FINDINGS.md` row 66 |

The two-operand longs that followed (row 67):

| measure | `main` | scripted |
|---|---|---|
| MAME gate, whole corpus | 292 570 / 317 500 | **296 465 / 317 500** (93.4 %) |
| MAME gate, the eight files | 1 871 / 1 860 / 1 909 / 1 924 / 1 828 / 1 982 / 1 975 / 1 955 | **2 447 / 2 404 / 2 287 / 2 301 / 2 260 / 2 500 / 2 500 / 2 500** |
| the families' own records | — | **every one green** (the red left in those files is the immediates' and the quicks') |
| exact-clock, whole corpus | 214 492 (67.6 %) | **215 930 (68.0 %)** |
| CLK beside it | 852 592 / 1 000 060 | **855 744** (+3 152, and no file falls) |

No record that passed on `main` fails here, in either corpus: the failure sets were diffed record by
record (`--failures`, per file on MAME and on CLK's full set). Three CLK records of a PC-relative
operand's *faulting* shape (one each in `AND.l`, `CMP.l`, `SUBA.l`) do cross from pass to fail: the
frame's status word is the one the corpus dispute is about, and the script reads a PC-relative
operand's first word with the instruction stream's bus status (`SSW_PROGRAM`), which is what MAME's
records want — the same three records' MAME counterparts pass.

`validate.sh` **PASS**: desktop at frame 612 (unchanged), G8 **8.98 ms** at n 761, 34 279 pops a frame.
Both folds moved and the owner **blessed the new pair on 2026-11-19**:
`run.digest` `554205a3e5c3b98628e893e6395e1126d20215e711bc2a5f563c3abdc0e9da20`,
`run.stream` `e9f72d4b4c86b444be5d61307ac98c4b7a32ecb2fd672ed19afef4ff9b9c7ed2` (Debug
`boot --frames 800`) — row 66's `afaf347c…` / `81f34a11…` was the pair before it. The machine's own
counters moved by ~0.04 % (pops 34 291 → 34 279 a frame, instructions 4 100 721 → 4 099 631, exceptions
7 654 → 7 644): a script fetches the queue's word inside the idle run instead of at the closing, so a few
refills coalesce.

The control transfers (row 69, 2026-11-20):

| measure | `main` | control transfers scripted |
|---|---|---|
| MAME gate, whole corpus | 297 166 / 317 500 (93.6 %) | **300 463 / 317 500 (94.6 %)** |
| files green | 77 of 127 | **80 of 127** |
| `BSR` / `UNLINK` / `JMP` | 1 229 / 1 385 / 1 589 | **2 500 / 2 500 / 2 500** |
| their exact-clock column | 1 227 / 1 385 / 774 | **2 500 / 2 500 / 2 500** |
| exact-clock, whole corpus | 233 293 (73.5 %) | **237 839 (74.9 %)** |
| CLK beside it | 856 572 / 1 000 060 | **856 572** (unmoved — no file falls) |
| sample (`--dir tests/680x0/v1`) | 2 522 / 2 976 | **2 522 / 2 976** (unmoved) |

No record that passed on `main` fails, in either corpus, and no record that was exact-clock breaks. Boot:
desktop **611** unchanged, pops 34 292.8 → 34 247.2 a frame, `validate.sh` **PASS** (G8 9.20 ms at n 760 on
that run; an interleaved A/B against `main` — 3 pairs, same tree built at both commits — reads 8.28 vs
8.39 ms, so the frame budget is at parity and validate's own number carried the host's spread). **Both
folds moved** and the owner **blessed the new pair on 2026-11-20**, so it is `main`'s: `run.digest`
`7331f4182da425ff85cfe0e56c8faeda923183e7c6ed29cf5218b54ad71a88a5`, `run.stream`
`4b7cf3ac5d7bdc24141f980b9f47bdfc12e169d214ca2b507effac1b34240795` (Debug `boot --frames 800`) — row
68's `9e1b0202…` / `0d972633…` is the pair they replace.

The return half of that family (row 70, 2026-11-20):

| measure | `main` | JSR + the returns |
|---|---|---|
| MAME gate, whole corpus | 300 463 / 317 500 (94.6 %) | **300 463 / 317 500 — unmoved** |
| files green | 80 of 127 | **80 of 127 — unmoved** |
| `JSR` exact-clock | 838 / 2 500 | **2 500 / 2 500** |
| `RTS` / `RTR` / `RTE` exact-clock | 1 263 / 1 285 / 1 886 | **2 500 each** |
| exact-clock, whole corpus | 237 839 (74.9 %) | **242 567 (76.4 %)** |
| CLK beside it | 856 572 / 1 000 060 | **856 572** (unmoved, to the file) |
| sample (`--dir tests/680x0/v1`) | 2 522 / 2 976 | **2 522 / 2 976** (unmoved) |

A clock-column batch: the whole corpus's per-file verdicts and every `--failures` detail line are
**byte-identical** to a `7a487a5` worktree's, and exact-clock rose by exactly the 4 728 records those four
files account for. Boot: desktop **611**, pops 34 247.2 → 34 221.8 a frame, `validate.sh` **PASS** (G8
8.81 ms at n 760). **Both folds moved** and are the owner's blessing of 2026-11-20: `run.digest`
`e48e7e91429ab7c1b3d73a2795414b6ca25e84d35787c63f68ffd42b72386825`, `run.stream`
`c687ef97e155741a213fe286c8c276b0069f97bb4821c11570b8ebc0e17b9dcb` — row 69's `7331f418…` /
`4b7cf3ac…` is the pair they replace.

The single-operand longs, and the proof that "one block" is measurable (row 71, 2026-11-20):

| measure | `main` | single-operand longs scripted |
|---|---|---|
| MAME gate, whole corpus | 300 463 / 317 500 (94.6 %) | **303 480 / 317 500 (95.6 %)** |
| files green | 80 of 127 | **85 of 127** (+5, exactly these files) |
| `NOT.l` / `TST.l` / `NEGX.l` / `NEG.l` / `CLR.l` | 1 867 / 1 886 / 1 891 / 1 918 / 1 921 | **2 500 each** |
| their exact-clock column | 1 645 / 1 292 / 1 733 / 1 720 / 560 | **2 500 each** |
| exact-clock, whole corpus | 242 567 (76.4 %) | **248 117 (78.1 %)** |
| CLK beside it | 856 572 / 1 000 060 | **859 506** (+2 934; the five files rise, no file falls) |
| sample (`--dir tests/680x0/v1`) | 2 522 / 2 976 | **2 529 / 2 976** |

The failure sets were diffed record by record in both corpora: **no record that passed fails**, and the
3 017 records that moved are exactly the five files'. One predicate bug was measured on the way and is the
row's own trap: `(op >> 9) & 7` made `CHK` (`0x4180`) a `NEGX.l` and cost it **896 records** — the
operation field of these five families is **bits 11-8**. Boot: desktop **611**, pops 34 221.8 → 34 217.6 a
frame, `validate.sh` **PASS** (G8 8.65 ms at n 760). **Both folds moved** and are the owner's blessing of
2026-11-20: `run.digest` `8506aeb6687c5b5a0b3cba3b95f80b65bdb650f24a922d4f798b0008c5397766`, `run.stream`
`bf9808c47fbbcd1e7311c9b2666510c5abffd71046b0b12e5192f5fb24d61cb4` — row 70's `e48e7e91…` / `c687ef97…`
is the pair they replace.

The single-operand words, and the four ways a block is not its sibling shrunk (row 72, 2026-11-20):

| measure | `main` | single-operand words scripted |
|---|---|---|
| MAME gate, whole corpus | 303 480 / 317 500 (95.6 %) | **304 721 / 317 500 (96.0 %)** |
| files green | 85 of 127 | **90 of 127** (+5, exactly these files) |
| `NOT.w` / `TST.w` / `NEGX.w` / `NEG.w` / `CLR.w` | 2 251 / 2 254 / 2 244 / 2 262 / 2 248 | **2 500 each** |
| their exact-clock column | 1 696 / 1 702 / 1 710 / 1 711 / 570 | **2 500 each** |
| exact-clock, whole corpus | 248 117 (78.1 %) | **253 228 (79.8 %)** |
| CLK beside it | 859 506 / 1 000 060 | **862 212** (+2 706; the five files rise, no file falls) |
| sample (`--dir tests/680x0/v1`) | 2 529 / 2 976 | **2 540 / 2 976** |

The word forms are a **different block**, not the long one shrunk, and all four differences are in the
microcode rather than in a flag: `adrw1` is an address latch with no bus cycle (so `(An)` is one read, not
two); `pdcw1` writes `m_pc = m_au` **before** it steps `An` down, where the long form's `pdcl1` never
touches `m_pc`; `pinw2` commits the post-increment **before** its read, where `pinl3` commits after both
words (so a faulting `(An)+` word read leaves `An` stepped); and the register form ends on `roaw2` with no
closing idle (`NOT.w Dn` 4 clocks, `NOT.l Dn` 6). Their clocks were measured as expected and then were
right on the first build — the old handler simply never issued the instruction's prefetch (`nnmw1`'s
`m_pc = m_au` read), which is the 4 every record was short of, and `CLR.w`'s row undercharged its 448
clean extension-word shapes by another 4. The failure sets were diffed record by record in both corpora:
**no record that passed fails**, no other file's verdict moves, and the 1 241 records that changed are
exactly the five files'. Boot: desktop **611**, pops 34 217.6 → 34 245.0 a frame, `validate.sh` **PASS**
(G8 8.77 ms at n 760). **Both folds moved** and are the owner's blessing of 2026-11-20: `run.digest`
`a649d65961ba6c10efda5602c9d203f921f66236115f611dc708d071792b6ae7`, `run.stream`
`8785ea1da0fc797432da8fd2595fa8ceff09d9755f5798405d8ace46e7478126` — row 71's `8506aeb6…` / `bf9808c4…`
is the pair they replace.

The single-operand **bytes**, and the width that is the whole of the third batch (row 73, 2026-11-20):

| measure | `main` (after row 72) | the single-operand bytes scripted |
|---|---|---|
| MAME gate, whole corpus | 304 721 / 317 500 (96.0 %) | **304 721 / 317 500 — unchanged, the failure set identical record for record** |
| files green | 90 of 127 | **90 of 127** (no file was red in the batch) |
| `NOT.b` / `TST.b` / `NEGX.b` / `NEG.b` / `CLR.b` | 2 500 each on state | **2 500 each**, all exact-clock |
| their exact-clock column | 2 500 / 2 500 / 2 500 / 2 500 / **401** | **2 500 each** |
| exact-clock, whole corpus | 253 228 (79.8 %) | **255 327 (80.4 %)** (+2 099, exactly `CLR.b`'s memory records) |
| CLK beside it | 862 212 / 1 000 060 | **862 212 — zero record moves** |
| sample (`--dir tests/680x0/v1`) | 2 540 / 2 976 | **2 540 / 2 976** |

Size 0 is the **same** block as size 1, not a third one: `adrw1`/`adrw2` … `nnmw1` + `morw2` are one
script for both widths, and only the access (a byte's lane, the reference's `m_edb >>= 8` on an even
address) and the EA's unit step differ — one byte, except on `A7`, whose byte forms step by two
(`pinw1`/`pdcw1`: `m_au = m_da[ry] + (ry < 15 ? 1 : 2)`). So this batch's cost was `size` 0 in the block
and in the step, and the two places a register write differs (`set_8` in place of `set_16l`). The state
column did not move at all, which is the point worth keeping: **a clock-only gap lives in an access the
instruction does not make**, and here it was `CLR` — the microcode's `clr_bw_*` read the operand they
zero (`nnmw1` runs `alu_and8` on `m_dbin`, never on a constant), while the old handler only wrote, so
every memory record was one read (4 clocks) short. Boot: desktop **611**, pops 34 245.0 → 34 246.7 a
frame, `validate.sh` **PASS** (G8 8.94 ms at n 760). **Both folds moved** and are the owner's blessing of
2026-11-20: `run.digest`
`00a727cf80c237eb00757f3afc61f95601d5bbe9cf7d6a1e5e3c79b5541d02b3`, `run.stream`
`a7209d81575a3620a2bc4dbd19d5e7c707df8d89d6c826d599db48203fd88582` — row 72's `a649d659…` / `8785ea1d…`
is the pair they replace.
