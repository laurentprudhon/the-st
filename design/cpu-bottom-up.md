# The core, bottom up: the refactor's plan and progress

The owner's instruction (2026-11-21): refactor the 68000 so the code is the silicon's own shape, bottom up —
storage and buses, then the nanocode's atoms, then the microcode's scripts, then the decode. At every level a
function combines fields or functions of the level below; at the decode level the manual's instruction names
appear in the function names, and the emulator's view exposes the instruction being executed.

Vocabulary (settled this session, and it is now the code's):

- **`Line`** — the 16 blocks of the opcode's top nibble (manual §10's "family selected by the top nibble";
  the microcode dumps' word). Not architectural: it is the A1 PLA's coarsest term, an *address partition*.
- **`Family`** — one of the manual's 78 instruction families (§5 tables). This is what `Decode` names.
- `Family` never means a line, and `Line` never means a family, in any file or comment.

Levels, bottom → top, and the file each lands in (`design/cpu-silicon.md` §1 is the map):

| level | what it is | file | step |
|---|---|---|---|
| 0 | the register file, the three sections' latches, the pad buffers | `M68000.Registers.cs` (+ `Pins`) | 1–2 |
| 1 | the nanocode's atoms and the IRD-keyed bank's fields | `M68000.ControlStores.cs` (`NanoStore`) | 3 |
| 2 | the microcode's words, one script per shape | `M68000.Scripts.cs` (exists) | 4 |
| 3 | the decode: `Line` → `Family` → script, and `Instruments.Instruction` | `M68000.Decode.cs` | 5 |

Every step is gated by the campaign's instrument and only committed when green:

```sh
bash scripts/cpu-shape-check.sh 82b5580     # corpus (both columns), folds, G8, interface
```

## Progress

- [x] **1. Storage.** (`Registers.cs`, `_pc`→`_au`, `Set16H`/`Set16L` used at 15 write sites) `M68000.Registers.cs`: the register file (`D0–D7`, `A0–A6`, `USP`, `SSP`, `AU`, `PSW`),
  the pipeline cells (`IRC`/`IR`/`IRD`), the ALU latches (`ALUO`, `ALUB`, `ALUE`, `ISR`, the width select) and
  the read latch (`DBIN`), each with its patent name and sheet; `Set16H`/`Set16L` as the 16-bit section
  accessors (tutorial §1.1's rule). `_pc` becomes `_au` — the hardware's program counter, with `PC` the
  manual's view of it.
- [x] **2. Buses and pads.** (`_irAddress`→`_at`; `AOB`/`DOB`/`DT` written by `Cycle`; `Pins` reads the buffers) `_aob`, `_dob`, `_at`, `_dt` become latches written by the bus cycle; `Pins` reads
  them instead of decoding the bus record group (the pins are the interface; the record group is how the
  interface is transported).
- [x] **3. Atoms.** (`M68000.Micro.cs` → `M68000.ControlStores.cs`, the two stores' vocabulary, documented as MicroStore/NanoStore blocks) `M68000.ControlStores.cs` with `NanoStore` — the elementary operations (transfers, the AU
  constant select, the ALU mode and its flags, the idle state, the queue helpers) — and the IRD-keyed bank's
  fields (`RegisterFile(rx)`, the size, the vector) as named methods over the atoms.
- [x] **4. Scripts.** (`M68000.Scripts.cs` is already one method per shape, and it calls the ControlStores vocabulary; nothing to move) One script per shape, named by the shape the microcode names (`SingleOperandLongAsync`,
  `AluLongShapeAsync`, …), each calling only `NanoStore` methods — no raw field arithmetic left in a script.
- [x] **5. Decode.** (+ the seam nested as `M68000.SingleStep`, the freeze row live in `validate.sh` and `SURFACE_MODE=freeze`, 0 divergences) (`M68000.Decode.cs`: `Line` + `Family` + 78 predicates; `Instruments.Instruction`/`Family`/`BusState`/`EntryStep`/`Interrupt`; witnessed 303 606/303 606 records) `Line` + `Family` + 78 `public static bool <Family>(ushort, out Decoded)` whose names are
  the manual's; a `Line` classifier above them; `Instruments.Family`, `Instruments.Instruction` (the manual's
  syntax, composed on read) and `Instruments.BusState`/`EntryStep`/`Interrupt`; the freeze test's
  `SURFACE_MODE` flips to `freeze`.

## Rules that must not break

- The corpus's two columns (317 500 state **and** exact-clock), the fold pair and G8 are the verdict at every
  step; a red row means the step is not landed.
- No behaviour change anywhere in this work: it is a re-expression. A moved fold is a bug, not a re-bless.
- A step that cannot be finished is reverted, not committed half-done.

## 8. The owner's rulings (2026-11-27) and where the work stands

**A — all accepted**: `SSW`, `INL`, `TPEND`, `PREN`, `FTU` as latches; `DCR` as the derived decoder; the
access label; the AU's transfers. **B**: `B1` accepted (the AU is the MUL/DIV counter) — with **C3**, the
AU/PC split — so **B2** (AUH/AUL, the two half-adders) comes with it; `B5` accepted *if it costs no
performance* (the answer instant already stretches the cycle, so what is missing is the S0–S7 view, not
the timing); `B3` (sections/lanes) not needed for the two adders — the split is what he wants, the lanes
are a bandwidth feature with no transfer to carry. **C**: all three accepted; **C2** is the *narrow* FTU
(size/mode/register/vector), which is enough for B1.

**B8, answered**: `NMA` is the 10-bit next micro-ROM address (Format I), `TY` selects the *source* of the
next address (NMA, A1, A2, A3), and `CBC` is the 5-bit condition code of a conditional word (Format II)
whose `NMBA` base has bits 7:6 replaced by the condition logic's C1/C0 — the 4-way branch. They are why a
microcoded instruction can branch on `auz`/`nz`/`d4`/`enl`. Our scripts are that control flow as C#
(`if`/call order), so there is nothing to implement.

**Landed so far**

- [x] `#87` **DCR + the AU's transfers** — `Decoder4To16`, `AuPlusTwo`, `AuPlus`, `AuEqualsAt`, `LatchAob`.
- [x] `#87` **`SSW` is a latch** — `_ssw`, written by one composition (`ComposeSsw`) that the frame reads.
  Honest limitation: the write site is the frame, not the microinstruction (moving it is #93's); an
  attempt to latch it per bus cycle cost 50 752 records, because a non-bus-fault exception must keep the
  *data* class of the last operand access, not the fetch's — measured, not guessed.

**Still to take, in this order**

1. [x] `INL`/`TPEND` (`C1`) — landed: `_inl` latched at the boundary sample and read by both sampling
   sites; `_tpend` set from `T` at the boundary and consumed by the trace exception (vector 9), which
   now exists. The folds did not move (the boot never sets `T`), so the trace path is latent and correct.
2. [x] `PREN` — the MOVEM walk. `_pren` holds the list, `StepPren`/`StepPrenReversed` are its `reset
   pren`, and both directions are `while (!PrenEmpty)`: 5 000 records green on both columns, sample
   identical to the parent, G8 median +0.12 ms against `3343c0b` at n 751.
3. the **AU/PC split** (`C3`) then the **narrow FTU** (`C2`) then the **AU as the MUL/DIV counter** (`B1`),
   then `AUH`/`AUL` (`B2`) and the constants ROM.
   - [x] `C2`, the **narrow FTU**: `_ftu` holds the instruction's size/mode/register (the encoding's low
     byte, so the decode's write is one shift) and an exception's vector; the decode writes it at the
     boundary and in the seam, and the 69 script locals that re-derived `(op >> 6) & 3` and friends now
     read `FtuSize`/`FtuMode`/`FtuRegister`. `ConstToFtu` is the constants ROM's atom, waiting for `B1`.
     The `IsScripted*` ledger predicates stay op-based by construction: they are static and answer for
     *any* encoding.
   - [x] first half: **`PC` is a register-file entry** (`_pc`), `_fetchStateAddress` is gone, and every
     frame reads the register. `CopyAuToPc()` (`m_pc ← m_au`) written as the bare adder copy costs
     **4 277 records** while the adder is the consumption counter — that measurement is why the copy takes
     the derived position below, and it stays the price of the adder holding an access itself.
   - [x] **the adder's position — `_fetchPointer` is deleted and the position is derived** (`df5dc0b`).
     `FetchPosition` (`M68000.ControlStores.cs`, beside `ConsumedAddress`) is the pipeline's next read
     address from the two cells that hold it: `_au`, which names the word the instruction consumes next,
     and the queue's fill — `_au + 2`, or `_au + 4` once a prefetch has filled the slot behind the head
     (`_ircValid`). Two states consumed a queue word with **no read behind it** and leave the position on
     `_au` itself: a control transfer's free pop (`_freePop`) and the absolute-long pair's second word
     (`_queueShort`). `BoundaryPosition` is the boundary's own state — the closing shift moved `_irc` into
     the head without moving `_au`, so it is `_au + 4` (`_au + 6` on the seam's `STOP` rollback).
     Measured before the swap at every site that read the field: **exact on 104 527 prefetches, 88 177
     pops, 135 864 trailing fetches, 29 075 fetch states**; the trailing/queue residues were exactly the
     two named states. The fetch states now write the die's statement, `CopyAuToPc(FetchPosition)`.
     **One reading that had to move with it**: a fetch fault's saved `pc` is `_au` at the stream's own
     read, but two classes are a different state and say so where the die does — a fault inside a refill
     saves `_pc` (`b` commits the target to `m_pc` only after both reads land: RTS/RTR/RTE's odd return
     address, 3 066 records), and `DBcc`'s taken branch declares `_au` (its `dbcc3` = `m_pc = m_au`, 632).
     Verdict: corpus 317 500 both columns, the blessed fold pair (no re-bless), G8 −0.13 ms median at
     n 751, `cpu-surface` frozen, and the `--clocks` dump identical on **all 261 894 clean records**
     (8 434 faulting records' `fetch_ptr`/`refetched` move by one word: the entry's sample now reports the
     derived position, not the address of the last stream read).
     **What the split still owes** (and it is now only this): the adder itself holding the access's address
     — the bare `CopyAuToPc()` — and with it `ConsumedAddress` (`_au − 2`, the PC-relative base, 604
     records over 13 files when written as `_pc`) and `AUH`/`AUL` (`B2`).
     **The instrument that decided it** (`61f4080`): `--clocks`' `frame_pc`/`fetch_ptr`/`pc_counter`/`ir_addr`
     are now sampled at the boundary as well as at an exception entry, so `fetch_ptr − pc_counter` is the
     pipeline's depth for every record. Measured over MAME's 261 894 clean records: **4** on 260 664 and
     **6** on exactly the 1 230 `STOP` records (the seam's rollback of the counter to the opcode's own
     address) — the boundary's depth is a constant with one named exception, and `ir_addr − pc_counter` is
     0 / −2 / −4 (171 355 / 88 636 / 673), i.e. the address temporary's own placement still varies by the
     last access, which is the part the derivation has to reproduce.
     **What is now known about the two cells, measured and read off the tree** — take it as the design's
     starting point, not as a guess: at a boundary (the seam's `Load`) `_fetchPointer = _au + 4`,
     `_pc = _au + 2`, `_at = _au`; the decode step advances `_au` by 2 and moves neither of the others,
     so mid-instruction `_fetchPointer = _au + 2` until a prefetch adds 2 (`_ircValid`) or an
     extension-word read moves `_fetchPointer` without moving `_au` — that last case is the whole of the
     4 277 measurement. `_pc` is the **live** program counter (the die's `m_pc`: what a fetch state copies
     and what the class pins of `FrameCounterForExtensionShape` state), while `_au` is the *consumption*
     counter with the PC's role bolted on (`_au − 2` as the PC-relative base, `_au = target` at a refill,
     `_au & 1` as the odd-PC test). So the slice is: give every one of those PC readings to `_pc`, derive
     `_au` (the adder) at each access from what the access needs, then delete `_fetchPointer` — and only
     then is `CopyAuToPc()` the die's statement rather than a convenience.
     **And the base is the exception the measurement found**: writing the seven PC-relative bases as
     `_pc` costs **604 records over 13 files** (`ADD.b`, `AND.b`, `BTST`, `CMP.b`, `LEA`, `MOVE.b`,
     `MOVEM.l`/`.w`, `MOVEtoCCR`, `MOVEtoSR`, `OR.b`, `PEA`, `SUB.b` — the shapes that prefetch before
     they compute the operand, where `_pc` is already a word ahead). So on this model the base is the
     **consumption** position and not the **fetch** position: our counter advances per word consumed, the
     die's `m_pc` per fetch. The base now lives in one place (`ConsumedAddress`, `M68000.ControlStores.cs`)
     so the replacement is that one body — and it is the last step, because it needs the queue's fill
     discipline (when the prefetch happens relative to the operand's computation) to be re-derived, not a
     new expression.
   - [~] `B1`, the **adder as the loop counter** — started with the multiplies: `MULU`/`MULS` now
     `ConstToFtu(15)` → `AuEqualsFtu()` → the loop tests `AuZeroOnSixBits` (`auz`, the six low bits) and
     steps with `AuMinusOne()`, and `AuEqualsConsumed()` (`_au = _pc - 2`) puts the instruction's
     position back at the exit. Both files **2 500/2 500 state and exact-clock**; corpus 317 500 on both
     columns, folds the blessed pair, G8 median +0.11 ms. What the restore rests on: `_pc` is a fetch
     state's address and a loop issues no fetch, so the register is intact when the loop ends — the
     PC-register landing (`eb5768f`) is what makes this possible without the full split.
     `DIVU` is landed by the same two atoms plus `AtEqualsAu()`: its fetch comes *after* the loop, so
     `_pc` is not the position — the die latches it in `AT` (`m_at = m_au`) and copies it back, which is
     what `AuEqualsAt()` restores (restoring from `_pc` there costs **649 records**). `DIVS` (`3ed9aca`)
     is the same shape with two exits, which is what its tail asked for: `dvumz` (the loop's overflow
     exit, `dvs08`'s no-borrow) leaves before any decrement and restores from the latch itself, and
     `dvs14` is the first state after the loop's last decrement (`dvs12`'s dead `m_au = m_au - 1`), so
     one `AuEqualsAt()` there covers `dvuma`, `dvum4` and `leaa2`. 2 500/2 500 both columns and a
     `--clocks` dump byte-identical to the parent's.
     `RESET` (`3a02b3b`) is a countdown, not a family: `rset2` starts the adder a word below the
     constants ROM's zero, `rset3`/`rset4` step it down by two and branch on the same six low bits, and
     thirty-one passes are the pin's 128 clocks — now `4 + 4 × passes` derived from the loop instead of
     the table's flat 128.     **Its two placements were the owner's to rule on, and he ruled them** (`#92`, 2026-11-28, verbatim:
     *"1. model the /RESET line and Pin (the rationale is we don't care to emulate the 6800 compatibility
     features, but the reset line could be useful in a machine contemporary with the atari st) 2. await
     each 2 cycle during the loop to give other chips the opportunity to react to the reset signal 3. move
     the read where the silicon does it, and fix the folds if necessary"*). Landed in `78d53cd`: `rset1`…`rset4` each wait 2 clocks (62 waits, one per micro-state),
     `m_reset_cb(1)`/`m_reset_cb(0)` are two records on `Wires.Reset` (asserted at +4 clocks, released at
     +128, **the line low for exactly 124 clocks** — measured), `Pins.RESET` reads the level, and the read
     is issued in `rset5` after the hold. The folds were re-blessed by that answer: `run.digest`
     `f8d39bf1…` / `run.stream` `9e362791…`, desktop 602 and 34 122.2 pops unchanged (FINDINGS row 82).
     The peripherals do not *react* yet — the line, its edges and the per-state waits are what the ruling
     asked for; a subscriber per chip is its own ticket (`#95`). `CHK` has **no loop** — four states (`chkr1`, `chkr2`, then
     `chkr3` or `trap1`) and a net-zero adder transfer pair — so `B1` had nothing to convert there; the
     reading is in `CheckAsync`'s doc comment.     `B1` is therefore **closed**, with `#92`'s ruling landed.
     **An option that may unblock `B1` without the full split** (for the owner to rule on, since it changes
     the ticket's premise): the die counts in `AU` because `AU` is *idle* during a `MUL`/`DIV` loop — no
     address is in play, the stream's position is in `m_pc`. On this tree the cell that holds "the address
     in play" is `_au`, but the stream's position is `_fetchPointer`, and *that* cell is the one that is
     idle during a loop (the loop issues no read). So the faithful mapping may be "count in the adder's
     cell, which on this model is `_fetchPointer`, and restore it at the loop's exit" — two facts, the same
     arithmetic, and `PC` untouched. It is cheaper than the split and it is what `#92` actually asks for
     ("the loops are counted by data flowing through the address adder"): decide it by measurement, with
     the clocks as the witness, before spending a session on the swap.
4. the **access label** (`A7`) — an enum over the script calls.
   - [x] first half: the class is a value — `AccessClass` (`Data`/`Program`/`CpuSpace`, MAME's
     `SSW_DATA`/`SSW_PROGRAM`/`SSW_CPU`), passed by the access instead of the two booleans `isProgram` /
     `isCpuSpace`, with `FunctionCode(access)` as the one place `FC0–FC2` are decided; `ComposeSsw` reads
     the same constants (`SswData`/`SswProgram`/`SswRead`/`SswSupervisor`/`SswNested`) instead of literals.
   - [ ] the rest: the class is *recorded* (a latch for #89's consumer) and the byte lane gets its test
     (`#88`: an odd byte writes `LDS` with `A1`=1, an even one `UDS`).
5. `#88`: the `S0`…`S7` **view** from the cycle's position (the wait-stretch is already the answer
   instant), the byte lane, DTACK/BERR sampling.
6. `#89`: the **65 536-opcode golden** and the term-level decode (the `--families` audit is the entry).
