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
| `ADDX.l` / `SUBX.l` (both directions) | 2 scripts, **one** method | ~135 (`ExchangeLongAsync`, the word-at-a-time block) | 2 500 a file | **2 500 / 2 500** each (was 1 671 / 1 673), **every record exact-clock** (was 0) |
| `MULU` / `MULS` (seven EA shapes each) | 3 loop states + the tails, **one** method | ~110 (`MultiplyAsync`) | 2 500 a file | **2 500 / 2 500** each (was 2 203 / 2 223), **every record exact-clock** (was 1 682 / 352) |
| `DIVU` (seven EA shapes) | 17 states, **one** method | ~105 (`DivideUnsignedAsync`) | 2 500 | **2 500 / 2 500** (was 1 648), **every record exact-clock** (was 1 007) |
| `DIVS` (seven EA shapes) | 26 states, **one** method | ~170 (`DivideSignedAsync`) | 2 500 | **2 500 / 2 500** (was 1 348), **every record exact-clock** (was 545) |
| `CHK` (seven EA shapes) | 3 states + the trap | ~50 (`CheckAsync`) | 2 500 | **2 500 / 2 500** (was 2 090), **every record exact-clock** (was 301) |
| the two-operand families' `.w` forms (`or/and/eor/add/sub`, `ADDA`, `SUBA`, `CMPA`) | opmodes 1/3/5, 18 each | ~200 (three tails, two of them shared) | 2 500 a file, nine files | **2 500 / 2 500** each (were 2 186 … 2 241), **every record exact-clock** (20 000; `ADDA.w` was 144) — `CMP.w` 2 338, its 162 left `CMPM.w`'s |
| line 0's word immediates and line 5's word quicks (they live in those nine files) | 8 × 6 and 9 × 2 | + `ImmediateWordAsync`/`QuickWordAsync` over the same tails | ~2 200 records | every one green and **exact-clock** |
| the `.b`/`.w` `ADDX`/`SUBX` memory forms | — | 2 lines (`asxw1`'s idle, in `ExchangeArithmeticAsync`) | 5 004 records | every one exact-clock (was 2 clocks short on all of them) |
| `MOVE` / `MOVEA`, `.w` and `.l` (nine destination classes, four `MoveLongFlags` placements) | 2 × 9 blocks, **one** method | ~430 (`MoveScriptAsync`, `MoveStoreAsync`, `WriteMoveAsync`) | 2 500 a file, four files | **2 500 / 2 500** each (were 1 767 / 1 468 / 2 247 / 1 984), **every record exact-clock** (10 000) |
| `MOVEM` `.l` / `.w`, both directions — **not ported, and it did not need to be** | the two old loops stay | +48 (`MovemToRegistersAsync`: the commit, the class, the read-ahead) | 2 500 a file, two files | **2 500 / 2 500** each (were 2 331 / 2 347), **every record exact-clock** (was 1 836 / 1 802) |

| `CMPM` `.b` / `.w` / `.l` (one source shape: `(Ay)+,(Ax)+`) — a script after all | 1 block, two readings (2 and 4 words) | ~120 (`CompareMemoryAsync`) | 162 in `CMP.w`, 165 in `CMP.l` | **2 500 / 2 500** both files, **every record exact-clock** (5 000) |
| the memory shifts, all eight kinds (`ASR`/`ASL`/`LSR`/`LSL`/`ROXR`/`ROXL`/`ROR`/`ROL .w` on `(An)`, `(An)+`, `-(An)`, `(d16,An)`, `(d8,An,Xn)`, `(xxx).w`, `(xxx).l`) | 4 classes, **not** rewritten | the existing block + the frame class and the abort charge (`MemoryShiftAsync`) | 2 500 a file, ten files | **2 500 / 2 500** each (were 2 415 … 2 435), **every record exact-clock** — the frame class alone greened the eight that were only `ram`-red |
| `ASR`'s C and X past the width (all three sizes) | the `lastOut` rule in `Shift` | 6 lines | 534 (`ASR.b`), 292 (`ASR.l`), 465 (`ASR.w`) | the two small files 2 500 / 2 500 (they were already exact-clock: this was flags alone), `ASR.w` 2 500 |
| `MOVEfromSR` / `MOVEtoCCR` / `MOVEtoSR` — two scripts, one method | 8 EA shapes × 3 blocks | ~150 (`MoveToStatusAsync`, `StatusWriteRefillAsync`, `FrameCounterForExtensionShape`) | 2 500 a file, three files | **2 500 / 2 500** each (were 2 251 / 2 211 / 2 377), **every record exact-clock** (was 163 / 159 / 1 360) |
| `Bcc`'s odd-target frame (one record, a rel16 branch to an odd address) | `bbci3` | 1 line (`FrameSavesOnFetch(_opcodeAddress + 2)`) | 1 | **5 000 / 5 000** (`Bcc` and `DBcc`), and its clock column is untouched: 3 715 exact both sides — the branch shapes are still the table's, not a script's |
`MOVEM` is the one row of this table that is **not a script**, and it is the honest counter-example to the
recipe: the family's loop was already the right shape and every clock of it is a bus read, so what the
corpus caught was three *placements*. (1) The load direction's `(An)+` increment is `popm6`'s
`m_da[ry] = m_at`, reached only when `step_movem` exhausts the mask, so **any** fault leaves `An`
untouched — a per-register pre-commit of `address + width` put 249 records two bytes high. (2) The two
PC-relative classes (`movem_l_dpc_list`, `movem_l_axpc_list`) set `m_base_ssw = SSW_PROGRAM | SSW_R` for
the address's states and never reset it, so the transfer's reads are program reads and a fault frames an
*access class* (`0x4CF2`, not `SSW_DATA`'s `0x4CF1`) — 73 records; `(xxx).w`/`.l` are
`movem_l_adr32_list`, which sets `SSW_DATA` per access, so the predicate is `mode == 7 && reg is 2 or 3`
and widening it to `mode == 7` trades 73 records for 81 of the other kind. (3) `popm5` reads one word
**ahead** (`m_aob = m_au`, `m_at = m_au`), so a clean transfer ends with one discarded word read past the
last register — `fc 5`, 4 clocks — and 1 331 clean records were exactly that short while every faulting
one was already exact. No charge was added to `M68000.Timing.cs`: the table's `MOVEM` row was right all
along, and `movem_*_list` has **zero idle states** (`10 bus accesses, 0 idle`), which is what the class
listing says before any clock is measured.

Row 79 completed the table — MAME's corpus is now 317 500 / 317 500 — and three of its five findings are
about the *frame's saved counter*, which is the one thing a shape table cannot express. `CMPM` moves
`m_pc` before its first read (`cmmw1`), so the frame's depth is the queue's word and not the trailing
fetch's; a memory shift's depth is **per addressing class** (`asr_adr16`/`adr32` move it, `asr_dais`'s
`aixl1` does not, the predecrement block moves it its own way) — one switch, three values, 83 records;
and the status moves' read direction **refills the queue after the status write** (`n 4`, then two
program reads at the instruction's own next word), which is the only place in this file where a script
*pays for* a refill instead of a closing fetch: `RefillAsync(_pc)` is that pair, and marking the flow
transferred is what keeps the closing steps from fetching a third word behind it. The batch's other two
findings are its own kind of lesson: `ASR`'s C and X are **not** zero past the width for the *arithmetic*
shift (MAME's `alu_asr8` in a loop keeps propagating the sign, and CLK's 2 673 records that disagree are
the documented convention split, not a regression), and `MOVE from SR` **reads** its operand before it
stores it — a write-direction instruction the handler had been storing without the read, which is also
why its fault frames say "read".

The control transfers are the first family whose **frame's saved counter** the script decides, and they
are also where the queue's lookahead shows: a transfer never refills the slot behind the word it took, so
its extension word costs no read (`JumpWordAsync(0)`), its own clocks lead (`_own` is only spent at the
closing fetch a transfer never runs), and the target's read is either a fetch state (`bsrw1`/`malw3`:
`m_pc` = the target, so an odd target's frame saves the target) or not (`bbci3`: `m_pc` keeps the
boundary's word, so `JMP` saves `opcode + 2` on every shape). That last distinction is
`FrameSavesOnFetch` — a script-declared override, because the default (#87) is `_au` — the word the
pipeline's position names — and that is right only for a fault at the instruction stream's own read. The
refill and `DBcc` each declare instead: a fault inside `RefillAsync`'s reads saves `_pc` (`b` commits the
target to `m_pc` only after both land), and `dbcc3` saves `_au`.

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

`ADDX.l`/`SUBX.l` are the row that teaches the **ledger's one failure mode**: a predicate that answers 0
is not evidence of a script. Their size field at 2 lands them on the two-operand longs' opmodes 2 and 6,
so `IsScriptedAluLong` had been answering 0 clocks for them while no script existed — the clock column
belonged to a table row that never fired (`(op & 0xF130) == 0x0100` never matches `0xD100`/`0x9100`) and
to a ledger that had claimed the family by accident. `IsScriptedExchangeLong` is the explicit one now, and
the rule for the next family is: **the answer 0 must have a script behind it.** The script itself is two
blocks, both stepping their operands **a word at a time**, and its one surprise is the commit rule —
`asxl3`/`asxl6` write the register only **after** that operand's first word comes back, so an odd `A`
leaves it exactly where it stood, where the *word* form's `asxw2` commits before its read. The two corpora
contest this one: CLK commits `A − 2`, MAME does not, and MAME wins — so CLK's `ADDX.l` records, already
red on the frame's status byte, gain an `aN` disagreement and its state count does not move. That is also
why a **failure-set** diff is not enough here: diff the first differing field per record too.

The 18 OR.l shapes are 18 short methods over **five** shared blocks (the operand pair, the
register-destination tail, the memory-destination tail, the program-status read, and the two idle
charges), so the *next* two-operand long family is a tail and a dispatch, not 330 lines: the blocks are
named for the micro-states and are family-independent. That is the number #78 asked for, and the
follow-up port measured it: AND, EOR, ADD and SUB cost **one line case and one `AluLong` value each**,
and the address-register forms one more tail (the same block, without the flag call).

The **variable-time families** are the row that most needs the ALU this file describes: their clocks are a
**counter on a value the core holds**, so a handler that computes the answer directly has neither the value
nor the count, and no shape table can carry them — which is why `DIVS` sat 110 clocks short on every clean
record and `MULS` scattered ±30 around `34 + 2 × popcount`. The scripts are the microcode's loops: `MULU`
is `mulr1`/`mulm1`, `mulm2` (the instruction's one read), then `mulm3`/`mulm4` and `mulm6`; `MULS` adds
`mulm5` and tests two bits at a time (Booth); `DIVU` is `dvur1`, `dvum2`, `dvum3`, `dvum5`…`dvumc` and
`dvum0`; `DIVS` is that loop inside a sign-fixing prologue and epilogue; `CHK` is `chkm1`/`chkr1`, `chkr2`
and `chkr3`. Six things a reader will not get from the manual:

- **the state's own ALU**: the loops need `m_isr` (the flag latch) and `m_alue` (the multiply's shift
  register, the divide's quotient register), so `M68000.Micro.cs` gains the chip's sixteen-bit operations
  verbatim — including `sr_nzvc_u`, whose `~SR_Z` has the Z bit *clear*, which is what makes it a
  clear-only update rather than the no-op it reads as;
- **the counter is `m_ftu`**, the instruction's timing constant, and it is **15** for all five (the loop
  tests `au & 0x3f`); the number came out of a python transcription validated against the corpus record by
  record before the C# was written — that is the cheapest form of this batch's verification;
- **a divide's overflow is `alu_over`**: N **and** V set, Z and C cleared. The manual says V; both corpora
  carry `0x0A` on every overflow record;
- **`CHK`'s two tests**: `bound − value` (V or N → out of range) and then the value's own sign, and the
  *second* one's flags are what the instruction leaves. `chkr2` branches straight to the trap, so the trap
  path is two states and the clean one three;
- **the frame's saved `pc` is the first extension word** for a displacement or an index shape, and moves
  only where the microcode says `m_pc = m_au` — `abwl1`/`abll2` for the absolute shapes, `pdcw1` for
  `-(An)`; `pinw1` leaves it. That is `FrameSavesFirstExtensionWord()` plus `BeginFetchState()`;
- **a clock row can claim a neighbour**: `(op & 0x00C0) == 0x00C0` charged these families 2 as `CMPA.w`,
  and `(op & 0xF130) == 0x8100` matched `DIVS Dn,<any>` as `ADDX`/`SUBX`. In `AluOwnClocks` a scripted
  family's `_own` must be tested **before** every masked row.

`MOVE` is the one family whose **destination** tail is not the ALU families' tail for the same
effective address, and the difference is where the instruction's one closing read sits: a `MOVE` stores
and *then* fetches, where `morw1`/`morl1` fetch first. The corpus's transactions give the whole table
(`R` a program read, `W` the store, `n2` an idle state), and it is the class row 58 guessed at from one
edge case:

| destination | register / immediate source | memory source |
|---|---|---|
| `Dn`, `An` (MOVEA) | `R` | (the source reads) `R` |
| `(An)`, `(An)+` | `W R` | … `W R` |
| `-(An)` | `R W` | … `R W` |
| `(d16,An)`, `(xxx).w` | `R W R` | … `R W R` |
| `(d8,An,Xn)` | `n2 R W R` | … `n2 R W R` |
| `(xxx).l` | `R R W R` | … `R W R R` |

(`(xxx).l`'s rows are `FetchMoveAbsoluteLongAsync` + the prefetch-first half + `CloseAfterLongDestination`
— #74's class, which turned out to be MAME's own `_queueShort` shape and not a CLK quirk.) The registers
commit where the microcode says: `(An)+` steps **after** the store (`mmiw2`/`rmil3`) and `-(An)` **with**
it (`mmmw2` writes the low word at A − 2, then sets `m_da[rx] = A − 4` and writes the high one), so a
faulting store leaves `An` where it stood — which is what "a plain MOVE is not like the other
instructions" (row 74) really means, stated per class. A long value goes out **high word first**, with
`-(An)` the exception. `MOVEA` calls **no flag update at all** (`i=......f` on every one of its states)
and takes the whole 32-bit value, sign-extending a word source (`m_at = ext32(...)`).

Three facts a reader will not get from the manual, and each one has a measurement:

- **the long form's two flag updates are per class, and only a fault between them shows it.** The pair is
  `sr_nzvc` on the low word and `sr_nz_u` on the high one — and `sr_nz_u` is *not* `sr_nzvc_u`: it sets N
  and Z and leaves **V and C standing** (`M68000.Micro.cs` needed the extra operation). On success the
  pair is a thirty-two-bit logic flag update; on a fault it is a partial one, and where it sits differs
  by class (`rmrl1`…`rmrl3` between the two words, `rmml1`/`rmml2` before both, `rmdl2`/`rmdl3`
  high-then-low, `mall2`/`mall3` low-then-high) — `MoveLongFlags`, four values, 252 records.
- **a `MOVE`'s group-0 frame does not always record the opcode.** The word `-(An)` store state `mmmw2`
  begins `m_ird = m_ir` (`rmmw1` having done `m_ir = m_irc`), so its frame's "instruction" word is the
  **queue head** — the instruction's next word — where every other class keeps the opcode; the long
  form's first write (`rmml2`) reassigns nothing, so *its* frame keeps the opcode. `MOVE.b` never faults
  there at all, because a byte store at an odd address is legal (which is why the family's byte file was
  green and its word file was not). That is `FrameSavesInstructionWord`, 115 records.
- **an odd write is 8 clocks, like an odd read.** `Cycle` charges the abandoned cycle's 4 and the
  aborting state charges its own 4 (`WriteMoveWordAsync`); reading the instruction's `length` as "the
  write's state replaces the abort" is 4 short on 1 042 records. The same shape as row 68's read rule,
  from the other side.

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

The long exchange arithmetic and the ledger that claimed it (row 74, 2026-11-20):

| measure | `main` (after row 73) | `ADDX.l`/`SUBX.l` scripted |
|---|---|---|
| MAME gate, whole corpus | 304 721 / 317 500 (96.0 %) | **306 377 / 317 500 (96.5 %)** |
| files green | 90 of 127 | **92 of 127** (+2, exactly these files) |
| `ADDX.l` / `SUBX.l` | 1 671 / 1 673 | **2 500 each** |
| their exact-clock column | **0 / 0** | **2 500 each** |
| the `.b`/`.w` memory forms' exact-clock | 1 277 / 1 262 / 1 255 / 1 202 of 2 500 | **2 500 each** (5 004 records) |
| exact-clock, whole corpus | 255 327 (80.4 %) | **265 331 (83.6 %)** (+10 004, no file falls) |
| CLK beside it | 862 212 / 1 000 060 | **862 212 — state unmoved** (its clock column rises) |
| sample (`--dir tests/680x0/v1`) | 2 540 / 2 976 | **2 540 / 2 976** |

The failure sets were diffed record by record: **1 656 fixed, 0 new failures, 0 other moves**. CLK's state
count does not move but 2 593 of its `ADDX.l` records gain a differing field — the commit rule is contested
between the corpora, MAME wins, and those records were already red on the frame's status byte (see §4).
Boot: desktop **611**, pops **34 246.7** a frame, `validate.sh` **PASS** (G8 8.75 ms at n 760). **Both folds
moved** and are the owner's blessing of 2026-11-20: `run.digest`
`b5cb195b38e87379a6dd58a6384b63054d43172b9ff4a35ddd02975cab5a295a`, `run.stream`
`d39246cbea7f2bfaa5e6baecb0b99f164fe5eb85a6a7f8abe40b12a4f3da8c5b` — row 73's `00a727cf…` / `a7209d81…`
is the pair they replace. The byte/word half of the commit was measured **not** to move the folds on its
own: a build with its `IdleAsync` removed produces the identical pair, so the movement is the long
script's clocks.

The variable-time families, and the loop that is the clock (row 75, 2026-11-20):

| measure | `main` (after row 74) | the five families scripted |
|---|---|---|
| MAME gate, whole corpus | 306 377 / 317 500 (96.5 %) | **309 365 / 317 500 (97.4 %)** |
| files green | 92 of 127 | **97 of 127** (+5, exactly these files) |
| `DIVU` / `DIVS` / `MULU` / `MULS` / `CHK` | 1 648 / 1 348 / 2 203 / 2 223 / 2 090 | **2 500 each** |
| their exact-clock column | 1 007 / 545 / 1 682 / 352 / 301 | **2 500 each** (12 500 records) |
| exact-clock, whole corpus | 265 331 (83.6 %) | **273 944 (86.3 %)** (+8 613, no file falls) |
| CLK beside it | 862 212 / 1 000 060 | **859 459** — only those five files move |
| sample (`--dir tests/680x0/v1`) | 2 540 / 2 976 | **2 533 / 2 976** |

The failure sets were diffed record by record in both corpora: **2 988 fixed, 0 new failures, 0 other
moves** — the five files' whole red set. CLK falls 2 753 and the fall is the measurement: MULU +420, MULS
+472, CHK +143, DIVU −1 368, DIVS −2 420, and the losses are the two contested conventions (a divide's
overflow: CLK keeps the manual's `V` alone, MAME and ours set `N|V`; and the group-0 frame's status word).
No file's green/red verdict changes (60 of 124 both sides). Boot: desktop **611**, pops **34 246.9** a
frame, `validate.sh` **PASS** (G8 8.84 ms at n 760). **Both folds moved** and are the owner's blessing of
2026-11-20: `run.digest` `ccd91815a56cd67c89041984377fb872b2924dcc6cafba2ed9673cd7fe8a38ae`, `run.stream`
`9a62f0cf95f1e78048eaed7c58b3796fdbb5798a1dbe9512436f3a7911cafd88` — row 74's `b5cb195b…` / `d39246cb…`
is the pair they replace.

The **word forms** of the two-operand families, and the three neighbours that share their opmodes
(row 76, 2026-11-20):

| measure | `main` (after row 75) | the word forms scripted |
|---|---|---|
| MAME gate, whole corpus | 309 365 / 317 500 (97.4 %) | **311 850 / 317 500 (98.2 %)** |
| files green | 97 of 127 | **105 of 127** (+8, exactly these files) |
| `AND.w` / `OR.w` / `EOR.w` / `SUB.w` / `ADD.w` | 2 186 / 2 196 / 2 210 / 2 220 / 2 229 | **2 500 each** |
| `ADDA.w` / `SUBA.w` / `CMPA.w` | 2 254 / 2 234 / 2 241 | **2 500 each** |
| their exact-clock column | 1 587 / 1 517 / 1 660 / 1 729 / 1 727, and **144 / 143 / 1 798** | **2 500 each** (20 000 records) |
| `CMP.w` | 2 083 | **2 338** — its 162 left are `CMPM.w`'s faulting frames |
| exact-clock, whole corpus | 273 944 (86.3 %) | **284 223 (89.5 %)** (+10 279, no file falls) |
| CLK beside it | 859 459 / 1 000 060 | **863 549** — only those nine files move, all up |
| sample (`--dir tests/680x0/v1`) | 2 533 / 2 976 | **2 548 / 2 976** |

Three facts separate the word block from the long one, and none of them is a mask. (1) The destination's
tail is `roaw1`/`roaw2` (`rorw1`/`rrgw2` for `<ea>,Dn`): the ALU and its flags sit in a state with **no bus
cycle and no idle charge**, then `set_16l` and the instruction's one read — and `roaw2` **ends the script**,
so there is no closing idle and `OR.w Dn,Dn` is 4 clocks where `OR.l Dn,Dn` is 6. (2) The memory tail is
`morw1` + `morw2` and the ALU runs **before** the closing prefetch, where the long form's `morl1` fetches
first. (3) `ADDA.w`/`SUBA.w`/`CMPA.w` are the **long** tails: a word source is sign-extended (`romm1`/
`cpmm1`: `m_at = ext32(m_dbin)`) and the ALU runs twice, `alu_add`/`alu_sub` then `alu_addc`/`alu_subc`,
which is what `AluIntoAddressRegisterAsync` already expands — and it is where the timing table's flat 4 was
measured short (`ADDA.w` was 144 exact of 2 500). Their red records were all in a PC-relative or an absolute
source: the line-0 immediates read `o#w1` **before** the destination's extension word, and the old handlers
read them the other way round.

**The block's own trap is its opmodes.** A word form's opmode field carries the size bits — 1 for
`<ea>,Dn`, 3 for `<ea>,An`, 5 for `Dn,<ea>` — so every opmode carries a neighbour, and the first run of the
batch lost 3 500 records to three of them: `DIVU` (line 8 opmode 3), `MULU` (line C opmode 3),
`DIVS`/`MULS` (opmode 7) are already scripted, but `CMPM.w` (line B opmode 5, mode 1), `EXG Dn,Dn` (line C
opmode 5, mode 0) and `ADDX.w`/`SUBX.w` (lines D and 9, opmode 5, mode 0 — 2 500 green each) were not. They
are excluded by mask, and the dispatch is **above the five line handlers**, because each of them would
otherwise repeat the same test. The failure sets were diffed record by record in both corpora: **2 485
fixed, 0 new failures, 0 other moves**; `--reads` reports 0 mismatches on all nine files. Boot: desktop
**611**, pops 34 246.9 → **34 232.3** a frame, `validate.sh` **PASS** (G8 8.85 ms at n 760). **Both folds
moved** and are the owner's blessing of 2026-11-20: `run.digest`
`0512273664adf28c0fdb5351baf9b09f04a3b24faacf26768bdd7857eb30e350`, `run.stream`
`3d2f1b234598f7d97dfc98ab8e7b84e41f55070949ebb89fdfd5d9907d2b2c1d` — row 75's `ccd91815…` / `9a62f0cf…`
is the pair they replace.

The **clock residue**, which is what row 79's 100 % gate left (row 80, 2026-11-21):

| measure | `main` (after row 79) | the residue closed |
|---|---|---|
| MAME gate, whole corpus | 317 500 / 317 500 (100 %) | **317 500 / 317 500 (100 %)** |
| files green | 127 of 127 | **127 of 127** |
| exact-clock, whole corpus | 298 755 (94.1 %) | **315 392 (99.3 %)** (+16 637, no file falls) |
| records with a residue | 18 745 in 24 files | **2 108, all `TAS`** (outside any claim) |
| CLK beside it | 863 265 / 1 000 060 | **863 265 — its per-file table is byte-identical** |
| sample (`--dir tests/680x0/v1`) | 2 552 / 2 976 | **2 552 / 2 976** |

**The rule that organises all of it: a ±2 is a state and a ±4 is a read.** The residue is a *class* problem
and each class is one of those two, so the instrument is `--clocks` grouped by the record's own operand
shape — never a family's pass count, because these files were all green on state already. Nine facts:

1. **A microcode state can sit on either side of the instruction's own read.** The three `to-SR`
   immediates (`andi_i16u_sr` and its two neighbours) test privilege *first* — `S_PRIVILEDGE` before
   `o#w1` — so a user-mode record never reads the immediate word (3 × ~1 240 were 4 over), and the
   supervisor tail is the status moves' own: `stiw1`…`stiw4` (8 clocks) then **two** program reads at the
   instruction's own next word (`malw3` + the branch's `mmrw3`), where `CloseInstructionAsync` issues one.
   `ClocksAsync(_own)` + `RefillAsync(_pc)` is that tail, and it is the same shape `StatusWriteRefillAsync`
   already had — a status write can change which stack the CPU runs on, so the queue is rebuilt.
2. **`Scc` on memory reads the byte it writes** — `adrw2` (operand) + `sccb2`/`sccb3` (program) + `morw2`
   (store), footer `idle states: 0`. 2 096 records. `CLR`'s lesson (a file green on state and short on
   clocks has an access missing) applied to a family that "obviously" only stores.
3. **A branch's displacement word is free when the stream is abandoned** — `dbcc1`/`bbcw1` take it out of
   `m_dbin` — so a taken branch is two reads (the target's pair) and not three. `JumpWordAsync(0)` is that
   pop, and it is the control transfers' rule (`BSR`, row 69) reaching the branches. But `Bcc`'s
   *fall-through* is not a transfer: it reads the word (`bbcw3` + `malw3`) — 9 records, and it is why the
   pop is peeked (`_ir`) and consumed twice, by kind.
4. **A free pop still moves the pipeline depth.** `dbcc3` sets `m_pc = m_au` before it reads the target, so
   an odd target's frame saves the word past the displacement — the shape declares it (`FrameSavesOnFetch
   (_au)`), because the position no longer trails `_au` after a free pop (`_freePop`, #87) and the refill's
   default would save `PC` (632 records). The refill's aborted read is the branch's own state, so it pays
   `ChargeAbortedReadAsync` (another 632, and 669 on `Bcc`).
5. **`LEA` and `PEA` have two idle states in their indexed shape** — `leax0`/`peax0` before the index
   word, `leax4`/`peax4` after the address is built (footers `idle states: 2`) — where `EaAddressAsync`
   charges one. `IndexedBlockIdleAsync` is the second, and it is the only place a *pure address* family
   takes a charge of its own.
6. **`NBCD`'s register form ends on 2 clocks** (`nbcr3`: `set_8` + `m_icount -= 2`); the memory forms have
   no such state and were exact. One `_own`.
7. **The bit operation's own charge is a function of the bit position**, and that is `LENGTH.md` §6's open
   datum, settled from CLK's source: `did_bit_op` sets `dynamic_instruction_length_ = int(bit > 15)` and
   `BCHG_BSET_Dn` spends `IdleBus(1 + …)` (`BCLR_Dn`: `2 + …`). 6 → 8 clocks above bit 15, static forms
   included; `BTST` reads only and its state (`Perform_np_n`) never spends it. Every record of the three
   files (1 212) is explained; the `BTST Dn,#data` form is separate — two reads and `btsr3`'s idle (58).
8. **A line that falls through to `AluOwnClocks` is charged by whatever row matches.** `MOVEQ`'s "size"
   field is the value's top two bits and its "mode" field is part of the value, so the plain long row
   claimed it (1 081 + 123); `MOVE.b` — the one MOVE size `IsScriptedMove` does not cover — has size 3 in
   its own encoding, so the `(op & 0x00C0) == 0x00C0` CMPA/ADDA row claimed every one (1 259 + 124). Both
   lines now answer 0 of their own. `MOVE.b`'s two operands' EA blocks are **per operand** and not once
   per instruction: the predecrement is charged once per instruction (`_eaCharged`), the indexed one twice
   — 65 + 67 records were 2 short behind a charged source and 281 were 2 long behind an uncharged one.
9. **A ledger predicate can claim a family it did not port** — again. `IsScriptedAluLong` excluded `CMPM`
   with the raw mask `(op & 0xF138) == 0xB108`, which cannot tell CMPM's two-bit size field from `CMPA.l`
   with a mode-1 source: so `cmpa_l_as_ad` was charged `CMPA`'s 2 by the table *and* its own `rcal3` by the
   script (315 records 2 long). Using `IsScriptedCmpm(op)` in the ledger is the fix, and it is row 74's
   `ADDX.l` lesson a second time: the ledger must be the one predicate that decides, not a mask.

Boot: desktop **666 → 602**, `_frclock_at_desktop` 659 → **596**, pops 33 699.8 → **34 122.2** a frame,
4 213 332 → **4 110 085** instructions, `validate.sh` **PASS** on every row but G8, which read 15.23 ms at
n 751 on a host whose A/B (3 interleaved Release pairs, same session) is **old 14.60 / 14.64 / 14.49 ms
against new 14.78 / 14.85 / 14.77** — +1.2 %, the +1.25 % pops a frame the batch's reads cost, and a host
60 % slower than the day row 79 was measured on. **Both folds moved and are the owner's blessing of
2026-11-21** (`main`'s since `7181a0a`): `run.digest`
`f8d39bf13da4f7f79e94cde7445eeee6ddc6eadc2feea3b9fec80fba3b9cc7e4`, `run.stream`
`9e362791b3f75aa9576b5111caa374a94474f13e4450931ddfedb7f3d4a1bd4f` (Debug `boot --frames 800`,
reproduced three times on the committed tree) — row 79's `5c7d1312…` / `b452114a…` is the pair they
replace. Row 81 left the pair untouched: EmuTOS executes no memory `TAS` in the first 800 frames.

### Row 81: the last two classes, and why one of them was not a disagreement

1. **`TAS`'s memory form carries one idle state, and that is the whole of the gate's last residue.**
   `tas_ais` is `adrw1` (address latch) → `adrw2` (the byte's read) → `tasm1` (`alu_or8` on it, then
   `m_icount -= 2`) → `tasm2` (the write) → `b`/`mmrw3` (the closing fetch), so `TAS (An)` is 14 clocks:
   `r n2 w r`, which is MAME's record verbatim. CLK times the same 14 as one `['t', 10, …]` read-modify-
   write transaction — its model has the RMW atomic — which is why `LENGTH.md`'s census read the family
   as "10 clocks nobody models": the two accesses were inside the `t`. Two independent corpora agreeing
   on a total the gate's README disowns is a measurement, not a convention split, so the family is
   **fixed rather than quoted**: `await IdleAsync()` between `ReadByteAsync` and the write, never on the
   fault path, and the table row that was meant to carry it deleted (see 2). MAME 2 108 → 0 and CLK
   6 828 → 0, and **both corpora's clock columns are now complete on this family**.
2. **A `(op & 0xFF00) == 0x…` row can never fire — twice now.** `TAS`'s ten-clock row tested
   `(op & 0xFF00) == 0x4AC0`, whose right-hand side has its low byte masked off; `JSR`'s (FINDINGS row 70)
   tested `(op & 0xFFC0) == 0x4E90`, and `0x4E90 & 0xFFC0` is `0x4E80`. Both were invisible because the
   family's *state* column was green and only the clock column could see them. Before trusting a masked
   row, compute the residual.
3. **An encoding with no entry in MAME's decode table is illegal on the part, and the core executed one.**
   `MOVEM <list>,(An)+` (`0x48D8`/`0x48F8`) — the store direction's postincrement, which the manual does
   not have — had no `.lst` entry and ran anyway, with a per-store `An` step this core had invented. It
   now raises vector 4, and the illegal-form test in **both** directions is taken *before* the mask's
   fetch: the part decodes the opcode first, so an illegal form must not read an extension word it never
   sees (the load direction's `-(An)`, `0x4CE0`, was already trapped — with the phantom read). No corpus
   record moves, because neither corpus carries a record for an illegal form; the boot is the witness,
   and it is unchanged.

## The state above the code — the comment convention (`#102`)

A block of C# that expands a micro-state carries **one comment line above it, in the listing's own
syntax**:

```
// <name> (<addr>)  <statements>                                            <charge>
```

`<name>` is the state's label as `scripts/microcode-walk.py --op <opcode>` prints it, `(<addr>)` the
micro-ROM address beside it (three hex digits, no `0x`), `<statements>` the listing's statements for that
state verbatim and in order with the bus boilerplate elided to `read at <expr>` / `write <expr> at
<expr>`, and `<charge>` the state's own `m_icount` decrement — 2 for a state, 4 for one that touches the
bus, 0 for the states the listing leaves uncharged (`rorw1`, `adrw1`, `cpmm1`) — right-aligned so the line
ends at column 104. A wrapped statement block keeps the charge on its **last** line.

What is elided is the engine's, not the state's: `m_base_ssw`, `m_edb = m_opcodes.read_interruptible(…)`,
`m_icount -= 4`, the `if(m_icount <= 0)` suspension block, the odd-address branch (ADR 0009's coroutine
style, `Cycle`'s abort), `set_ftu_const()`, and `m_inst_state = m_decode_table[m_ird]`.

Two things must be **said, never hidden**: a state fused into a helper names both on one line (`// b (363)
+ mmrw3 (34c)`, charge `0+4` — one part per state, in the pair's order: `b` is uncharged bookkeeping, the
read is `mmrw3`'s, and the helper is `TrailingFetchAsync`), and a deliberate difference from the listing
begins a second line `//   ours:` with what the code does and the measurement that made it necessary.
`RESET`'s block (`MiscAsync`'s `0x4E70` case) is the worked example: the count is taken before the clocks
are spent, and the block says so. `UNLK`'s (`UnlinkAsync`, `#94`'s first method) is the model for the two
harder shapes: a read pair the code issues in one helper — named as the two states it is — and a closing
fetch the *loop* issues, which the block says instead of pretending the script does it.

The checker is `scripts/comment-states.py` (stdlib only, needs the pinned microcode):

```sh
python3 scripts/comment-states.py --check FILE...              # every formatted comment in the file
python3 scripts/comment-states.py --complete --op 4e70 FILE    # every state of that listing, in order
python3 scripts/comment-states.py --index | grep rset          # the index it checks against
```

It builds the state index from the generator's whole-ROM dump (`m68000gen.py sdf`) and checks four things:
the `(name, addr)` pair is a state the ROM has, the charge is that state's own, the line ends at column
104, and a state is not commented twice inside one method; `--complete` additionally requires an opcode's
whole script, once each and in listing order. It cannot check that the C# *is* the state — no instrument
reads a comment (`design/cpu-silicon.md` §5) — which is why the comments-only verdict is the identity
(the corpus, the folds, G8) plus a diff with no non-comment line:

```sh
git diff -U0 -- 'src/TheST.Machine/Cpu/*.cs' | grep -E '^[+-]' \
  | grep -vE '^(\+\+\+|---) ' | grep -vE '^[+-][[:space:]]*//'    # expected: empty
```

The checker joins `cpu-shape-check.sh`'s interface row when it reads 0 across the whole core — that is
`#94`'s landing, not this convention's (`#102`); a checker with findings in the gate is a red gate.

Two things a converter pays for once, and neither is the checker's to catch. An `ours:` paragraph belongs
**above** the state lines it qualifies, with a bare `//` line between: a paragraph placed after a state block
is read as that block's continuation, and its last token then becomes the "charge". And the guard above is
line-shaped — it cannot see a trailing comment edited in place — so a conversion adds whole comment lines and
leaves every code line as it stands, trailing comments included.
