# The internal clock: how many clocks, and where they sit

An instruction costs its bus cycles (4 clocks each) **plus** the clocks it spends off the bus. A core that
models only bus cycles is right about every register and every byte and wrong about *when* the next cycle
starts — the one thing emulated software can observe. This file is the how-to for charging that residue
faithfully: the numbers, the four places they are charged, and the two corpora that decide where.

## 1. The count: `length − 4 × bus`

Both corpora state the total in `length` (CPU clocks) and the cycle list in `transactions`, and
`length = 4 × (r/w transactions) + Σ n/t` holds on every record — so the residue is the reference's own
account, not a reconstruction. Over the whole CLK corpus:

- **a function of the instruction's shape** (family, size, operand EA modes) for ~three quarters of the plain
  records — ~85 % if the key is the whole opcode word;
- **a counter, not a table**, for the rest; every counter is a value the core holds when it executes: shifts
  `base + 2 × count` (`base` 2 for `.b`/`.w`, 4 for `.l`; count = `Dn & 63` for the register form, the
  immediate 0→8 for the other), `MULU` 34–64 / `MULS` 36–64 (34 + 2 per magnitude bit is the right shape; the
  reference's loop runs one iteration more for a quarter of the values), `DIVU` 76–130 / `DIVS` 122–150 with
  short exits 6 / 12, `CHK` 6, `Scc Dn` 2, `Bcc` 4 not taken / 2 taken, `DBcc` 4 / 2;
- **~20 % of the corpus is not an instruction's cost at all**: a faulting record's `length` carries the
  exception entry (frame + vector + the handler's first fetches). Split those off before tabulating anything
  — a supervisor read (`fc 5`) below `$40` is the detector — and include `t` transactions in the sum (`TAS`
  has no `n`).

## 2. The placement: skeletons, not totals

Reducing a record to a **skeleton** — the cycle list with each idle run in its place, addresses dropped
(`r n2 r r w`) — turns "where does the delay go" into a string comparison per (mnemonic, operand-mode
tuple). Key on the corpus's own operand text (`(d16, A2)`, `(d8, A3, Xn)`, `-(A\d)`, `Q`, `#`): a hand-rolled
opcode decoder disagrees with the corpus silently, and two of them were wrong in different ways (a shift's
bit 5 selects the count form; a bit-op's bits 7-6 are the operation, not the size).

Compare CLK against MAME's microcoded corpus (`SingleStepTests/m68000`, `v1/*.json.bin` decoded with its own
`decode.py`): **375 of 423 shared shapes are identical on both the total and the skeleton**. The
disagreements are the variable-time families and they disagree in the *count*, never in the shape — so the
placement is settled by two independent models, and **the count follows MAME**, which is the owner's standing
ruling for clocks *and* for state. Where a family is set to MAME's number, quote CLK's beside it: `MUL`/`MULS`
run higher by an EA-dependent amount (identical only for `#imm` and `(d16,An)`), `DIV`/`DIVS` by 2–8 on the long
path with the same 6-clock overflow exit, and `MOVEM` differs in count *and* shape. `TAS` and `TRAPV` are
outside any 100 % claim — MAME's README disowns its TAS RMW timing and its TRAPV tests.

The MAME corpus's conventions differ, and each one costs an hour if assumed: `pc` is `m_au`, the **next
prefetch address** (the opcode is at `pc − 4`); `ram` is 16-bit pieces; a byte sits in the **high** lane;
`re`/`we` are the aborted address-error cycles (AS not asserted) and mean the record faulted; `t` is the TAS
cycle; each transaction carries UDS/LDS as well as the data.

## 3. The four charge points

| where | clocks | the evidence (both corpora) |
|---|---|---|
| **the effective address**, once per instruction, before the access it serves | 2 for `-(An)`, 2 for `(d8,An,Xn)` / `(d8,PC,Xn)` | `CLR.w -(A0)`: `n2 r r w`; `CLR.w (d16,A0)`: `r r r w`, no run |
| **the family's own work**, before the closing fetch | §1's counters and constants | `ASL.b Q,D0`: `r n16`; `CHK`: `r n6`; `DIVU`: `r n82 r` |
| **a control transfer**, before its refill fetches | `Bcc` 4 / 2, `DBcc` 4 / 2, `JMP`/`JSR` 2 (indexed 6, 2 of it the EA's), `BSR` 2 | not taken: `n4 r`; taken: `n2 r r`; `JMP (d16,A0)`: `n2 r r` |
| **an exception entry** | 4 before the frame's writes, 2 between the handler's two fetches | every entry: `n4` … frame … `n2` … |

Two facts a manual reader gets wrong, both in both corpora's traces:

- **a not-taken branch's clocks come first** (`n4 r`), before the fetch — the opposite of the "internal work
  trails" intuition that holds for `CHK`, the shifts and a divide;
- **an EA's 2 clocks are charged once per instruction, not once per operand**: `ADDX -(An),-(An)` and
  `MOVE.w <ea>,-(An)` show a single `n2` for two predecrements, and a per-operand charge double-counts
  exactly those.

## 4. The mechanism

- **One table, not scattered constants.** `InstructionOwnClocks(op)` + `LeadsOwnClocks(op)` in a single
  partial file; instruction bodies set nothing except the data-dependent overrides (`MUL`/`DIV` after they
  have read their operand, `Scc` on the condition). Constants sprinkled at the end of each handler are what a
  previous build did, and dozens of them were wrong by margins nobody could see.
- **A charge is a wait** (`WaitFor(Clock + n × 4, Wires.Answer, _answerSeq)`), never an `Advance` — see the
  skill's §7 for the hanging-boot mechanism.
- **Reset the per-instruction state at every boundary** (`Run`, `SingleStep.Load`, `SingleStep.StepAsync`).
- **Grade it with a counter, then pay for it with pairs.** `cpu-tests`' `exact-clock tests` says whether the
  table is right; a per-record dump (`--clocks FILE`) says *which* family is wrong and whether the error is a
  constant or a scatter. Expect the frame mean to *fall* and the guest to be slower per instruction
  (`instructions` and `bus_cycles` per frame both drop, `frame_n` rises) — quote all three or the number will
  be read as a workload regression.

## 5. What resists, and why each is a ticket rather than a tuning knob

- **immediate-to-`CCR`/`SR` forms**: the core performs 3 fewer *bus cycles* than the reference (both words come
  out of the queue where CLK fetches them) — a fetch-model difference, not a clock one.
- **faulting records**: the entry's own bus cycles (the frame) are needed for the total, and the frame's cycle
  count is the corpora's contested convention — **MAME decides it now** (`MOVEM` faulting on the store stacks
  `opcode + 6`, not CLK's `+ 2`), so a MAME-true core loses CLK records there and that is a re-derivation, not
  a regression.
- **`TAS`** is outside the target: MAME's own README says its 5-cycle read-modify-write timing is wrong, so the
  family cannot be graded to 100 % against a corpus that disowns it — say so and exclude it, with the quote.
- **`BCHG`/`BCLR`/`BSET` on a data register**: two records of the same shape pay 6 or 8 clocks with nothing in
  the record to separate them (bit index, old bit, operand value, flags, position in the file — all tested).
  Read the reference's source; do not invent a clause.

## 6. Where the numbers live in the repo

`tests/680x0/LENGTH.md` (the census and the ruling), `tests/680x0/PLACEMENT.md` (the two-corpus placement and
the coverage that lets MAME gate), `scripts/corpus-length-census.py` and `scripts/corpus-placement.py` (each
regenerates its page's table), `src/TheST.Machine/Cpu/M68000.Timing.cs` (the table), `cpu-tests --clocks` (the
per-record audit). MAME's corpus is not vendored yet: fetch `v1/<name>.json.bin` from
`raw.githubusercontent.com/SingleStepTests/m68000/main/v1/`, decode with its `decode.py`, 127 files, 138 MB.
Re-derive nothing else: run the scripts.
