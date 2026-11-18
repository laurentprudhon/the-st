# The `length` column, measured

The corpus publishes two clock numbers per record: `length`, the reference's CPU clocks for the
instruction, and `transactions`, its own cycle list (`["n", cycles]` and `["t", cycles]` for internal
work, `["r"|"w", cycles, fc, address, size, data]` for a bus cycle). This core counts the second kind —
four clocks a bus cycle — so what it is missing is one number per record:

    residue = length - 4 x (the record's r/w transactions)

`cpu-tests` prints `exact-cycle tests` — the records where our bus cycles happen to equal `length`. On the
sample today that number is **0 of 2 976** (it was 18 before `#70`'s queue: the queue made every instruction
fetch exactly its own word count, and no record's bus cycles add up to its `length` any more). This page is
that residue, tabulated over the whole corpus, and the answer to
ADR 0009 §3 / `design/cpu-core.md` §8's internal-clocks question: **is the column a table, and can it be
a verdict?**

Instrument: `scripts/corpus-length-census.py` (standard library only, ~20 s over the whole set — the
numbers below are its output, not a manual count). It reads the corpus's own operand text as the shape
key, so no opcode decoder stands between the corpus and the table.

## 1. The arithmetic holds on every record

`length = 4 x (bus cycles) + sum of the n/t clocks` on **all 1 000 060 records, no exceptions**. So the
residue is not a reconstruction: it is exactly the clocks the reference does not spend on the bus, in its
own account. A record's `length` can therefore be checked from two numbers the reference supplies, and the
only open question is whether *our* core can produce the second one.

## 2. Two splits, before the residue is a table

- **The exception class: 194 548 records (19.5 %).** 186 483 records contain a supervisor-data read inside
  the vector table (`fc 5`, address < `$40`) — the instruction faulted, and `length` covers the
  instruction *and* the group-0 entry (frame writes, the vector read, the handler's first prefetches).
  The whole `TRAP` file (8 065) is the same shape: its `length` is an entry's. A per-opcode residue table
  must not contain these; a 7-word frame's bus cycles land in the same sum.
- **The plain instruction class: 805 512 records (80.5 %)** — the residue a per-opcode table is about.

## 3. The census: the residue is a shape function, not a per-instruction datum

Keyed by (family, size, the operands' effective-address modes) — the shape the manual's tables are keyed
on — the plain class has **1 333 shapes; 1 231 of them (92.3 %) pay one residue each, and those cover
591 191 records (73.4 % of the plain class)**. Keyed by the whole opcode word instead (which also fixes the
destination register) it is **684 685 records (85.0 %)**: the difference is the destination-EA dependence
listed in §4. (The vendored sample reads 89.5 % of shapes / 74.0 % of records — within a point, as
`FINDINGS.md` says it should be.)

Every record whose residue is *not* a function of its shape is in nineteen families, and the count of
records is the whole of the residue:

| family | records in a varying shape | what the residue follows |
|---|---|---|
| 24 shift files (`ASL`/`ASR`/`LSL`/`LSR`/`ROL`/`ROR`/`ROXL`/`ROXR` x `.b`/`.w`/`.l`) | 177 314 | the count (§5) |
| `DBcc` | 6 101 | the branch |
| `Bcc` | 5 865 | the branch |
| `DIVS`, `MULU`, `DIVU`, `MULS` | 4 992 / 4 990 / 4 963 / 4 943 | the operand's value |
| `BSET`, `BCHG`, `Scc`, `BCLR` | 1 315 / 1 301 / 1 280 / 1 257 | the bit op's result, and one open datum (§5) |
| **total** | **214 321 (26.6 % of the plain class)** | |

Nothing outside that list varies: `MOVE`, the arithmetic and logic families, `MOVEM`, the bit ops on
memory, `JMP`/`JSR`, `LEA`/`PEA`, `CLR`/`TST`/`NEG`/`NOT`/`NEGX`, the BCD families and the `SR`/`CCR`
moves all pay one residue per shape.

## 4. The table

One row per (family, size, source EA). Where a second number appears for the same source, it is the
destination-EA (or size) dependence, listed under the row. `-` is a family with no operand (`NOP`, `RTS`,
`RESET` — whose 128 clocks are the 124 the manual's `RESET` asserts plus its fetch).

## The residue per family, by effective address

`residue` = the clocks the reference charges beyond 4 a bus cycle. One row per
(family, source EA); a second number is given where the destination's EA mode
moves it. A blank means the family has no records of that shape.
- **ABCD** — Dn 2, -(An) 2
- **ADD.b** — (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (d16,PC) 0, (d8,PC,Xn) 2, (xxx).w 0, (xxx).l 0
  - destination/size-dependent: {'Dn': [0, 2], '#imm': [0, 2], 'Q': [0, 2]}
- **ADD.l** — An 4, (An) 2, (An)+ 2, -(An) 4, (d16,An) 2, (d8,An,Xn) 4, (d16,PC) 2, (d8,PC,Xn) 4, (xxx).w 2, (xxx).l 2
  - destination/size-dependent: {'Q': [0, 2, 4], 'Dn': [0, 2, 4], '#imm': [0, 2, 4]}
- **ADD.w** — An 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (d16,PC) 0, (d8,PC,Xn) 2, (xxx).w 0, (xxx).l 0
  - destination/size-dependent: {'Q': [0, 2, 4], 'Dn': [0, 2], '#imm': [0, 2]}
- **ADDA.l** — Dn 4, An 4, (An) 2, (An)+ 2, -(An) 4, (d16,An) 2, (d8,An,Xn) 4, (d16,PC) 2, (d8,PC,Xn) 4, (xxx).w 2, (xxx).l 2, #imm 4
- **ADDA.w** — Dn 4, An 4, (An) 4, (An)+ 4, -(An) 6, (d16,An) 4, (d8,An,Xn) 6, (d16,PC) 4, (d8,PC,Xn) 6, (xxx).w 4, (xxx).l 4, #imm 4
- **ADDX.b** — Dn 0, -(An) 2
- **ADDX.l** — Dn 4, -(An) 2
- **ADDX.w** — Dn 0, -(An) 2
- **AND.b** — (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (d16,PC) 0, (d8,PC,Xn) 2, (xxx).w 0, (xxx).l 0
  - destination/size-dependent: {'Dn': [0, 2], '#imm': [0, 2]}
- **AND.l** — (An) 2, (An)+ 2, -(An) 4, (d16,An) 2, (d8,An,Xn) 4, (d16,PC) 2, (d8,PC,Xn) 4, (xxx).w 2, (xxx).l 2
  - destination/size-dependent: {'Dn': [0, 2, 4], '#imm': [0, 2, 4]}
- **AND.w** — (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (d16,PC) 0, (d8,PC,Xn) 2, (xxx).w 0, (xxx).l 0
  - destination/size-dependent: {'Dn': [0, 2], '#imm': [0, 2]}
- **ANDItoCCR** — #imm 8
- **ANDItoSR** — #imm 8
- **ASL.w** — (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **ASR.w** — (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **BSR** — #imm 2, Q 2
- **CHK** — Dn 6, (An) 6, (An)+ 6, -(An) 8, (d16,An) 6, (d8,An,Xn) 8, (d16,PC) 6, (d8,PC,Xn) 8, (xxx).w 6, (xxx).l 6, #imm 6
- **CLR.b** — Dn 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **CLR.l** — Dn 2, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **CLR.w** — Dn 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **CMP.b** — Dn 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (d16,PC) 0, (d8,PC,Xn) 2, (xxx).w 0, (xxx).l 0
  - destination/size-dependent: {'#imm': [0, 2]}
- **CMP.l** — Dn 2, An 2, (An) 2, -(An) 4, (d16,An) 2, (d8,An,Xn) 4, (d16,PC) 2, (d8,PC,Xn) 4, (xxx).w 2, (xxx).l 2
  - destination/size-dependent: {'(An)+': [0, 2], '#imm': [0, 2]}
- **CMP.w** — Dn 0, An 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (d16,PC) 0, (d8,PC,Xn) 2, (xxx).w 0, (xxx).l 0
  - destination/size-dependent: {'#imm': [0, 2]}
- **CMPA.l** — Dn 2, An 2, (An) 2, (An)+ 2, -(An) 4, (d16,An) 2, (d8,An,Xn) 4, (d16,PC) 2, (d8,PC,Xn) 4, (xxx).w 2, (xxx).l 2, #imm 2
- **CMPA.w** — Dn 2, An 2, (An) 2, (An)+ 2, -(An) 4, (d16,An) 2, (d8,An,Xn) 4, (d16,PC) 2, (d8,PC,Xn) 4, (xxx).w 2, (xxx).l 2, #imm 2
- **EORItoCCR** — #imm 8
- **EORItoSR** — #imm 8
- **EXG** — Dn 2, An 2
- **EXT.l** — Dn 0
- **EXT.w** — Dn 0
- **JMP** — (An) 0, (d16,An) 2, (d8,An,Xn) 6, (d16,PC) 2, (d8,PC,Xn) 6, (xxx).w 2, (xxx).l 0
- **JSR** — (An) 0, (d16,An) 2, (d8,An,Xn) 6, (d16,PC) 2, (d8,PC,Xn) 6, (xxx).w 2, (xxx).l 0
- **LEA** — (An) 0, (d16,An) 0, (d8,An,Xn) 4, (d16,PC) 0, (d8,PC,Xn) 4, (xxx).w 0, (xxx).l 0
- **LINK** — An 0
- **LSL.w** — (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **LSR.w** — (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **MOVE.q** — Q 0
- **MOVEA.l** — Dn 0, An 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (d16,PC) 0, (d8,PC,Xn) 2, (xxx).w 0, (xxx).l 0, #imm 0
- **MOVEA.w** — Dn 0, An 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (d16,PC) 0, (d8,PC,Xn) 2, (xxx).w 0, (xxx).l 0, #imm 0
- **MOVEM.l** — (An) 0, (An)+ 0, (d16,An) 0, (d8,An,Xn) 2, (d16,PC) 0, (d8,PC,Xn) 2, (xxx).w 0, (xxx).l 0
  - destination/size-dependent: {'#imm': [0, 2]}
- **MOVEM.w** — (An) 0, (An)+ 0, (d16,An) 0, (d8,An,Xn) 2, (d16,PC) 0, (d8,PC,Xn) 2, (xxx).w 0, (xxx).l 0
  - destination/size-dependent: {'#imm': [0, 2]}
- **MOVEP.l** — Dn 0, (d16,An) 0
- **MOVEP.w** — Dn 0, (d16,An) 0
- **MOVEfromSR** — Dn 2, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **MOVEfromUSP** — An 0
- **MOVEtoCCR** — Dn 4, (An) 4, (An)+ 4, -(An) 6, (d16,An) 4, (d8,An,Xn) 6, (d16,PC) 4, (d8,PC,Xn) 6, (xxx).w 4, (xxx).l 4, #imm 4
- **MOVEtoSR** — Dn 4, (An) 4, (An)+ 4, -(An) 6, (d16,An) 4, (d8,An,Xn) 6, (d16,PC) 4, (d8,PC,Xn) 6, (xxx).w 4, (xxx).l 4, #imm 4
- **MOVEtoUSP** — An 0
- **NBCD** — Dn 2, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **NEG.b** — Dn 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **NEG.l** — Dn 2, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **NEG.w** — Dn 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **NEGX.b** — Dn 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **NEGX.l** — Dn 2, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **NEGX.w** — Dn 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **NOP** — - 0
- **NOT.b** — Dn 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **NOT.l** — Dn 2, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **NOT.w** — Dn 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **OR.b** — (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (d16,PC) 0, (d8,PC,Xn) 2, (xxx).w 0, (xxx).l 0
  - destination/size-dependent: {'#imm': [0, 2], 'Dn': [0, 2]}
- **OR.l** — (An) 2, (An)+ 2, -(An) 4, (d16,An) 2, (d8,An,Xn) 4, (d16,PC) 2, (d8,PC,Xn) 4, (xxx).w 2, (xxx).l 2
  - destination/size-dependent: {'Dn': [0, 2, 4], '#imm': [0, 2, 4]}
- **OR.w** — (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (d16,PC) 0, (d8,PC,Xn) 2, (xxx).w 0, (xxx).l 0
  - destination/size-dependent: {'Dn': [0, 2], '#imm': [0, 2]}
- **ORItoCCR** — #imm 8
- **ORItoSR** — #imm 8
- **PEA** — (An) 0, (d16,An) 0, (d8,An,Xn) 4, (d16,PC) 0, (d8,PC,Xn) 4, (xxx).w 0, (xxx).l 0
- **RESET** — - 128
- **ROL.w** — (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **ROR.w** — (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **ROXL.w** — (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **ROXR.w** — (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **RTE** — - 0
- **RTR** — - 0
- **RTS** — - 0
- **SBCD** — Dn 2, -(An) 2
- **SUB.b** — (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (d16,PC) 0, (d8,PC,Xn) 2, (xxx).w 0, (xxx).l 0
  - destination/size-dependent: {'Q': [0, 2], 'Dn': [0, 2], '#imm': [0, 2]}
- **SUB.l** — An 4, (An) 2, (An)+ 2, -(An) 4, (d16,An) 2, (d8,An,Xn) 4, (d16,PC) 2, (d8,PC,Xn) 4, (xxx).w 2, (xxx).l 2
  - destination/size-dependent: {'Dn': [0, 2, 4], 'Q': [0, 2, 4], '#imm': [0, 2, 4]}
- **SUB.w** — An 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (d16,PC) 0, (d8,PC,Xn) 2, (xxx).w 0, (xxx).l 0
  - destination/size-dependent: {'Q': [0, 2, 4], 'Dn': [0, 2], '#imm': [0, 2]}
- **SUBA.l** — Dn 4, An 4, (An) 2, (An)+ 2, -(An) 4, (d16,An) 2, (d8,An,Xn) 4, (d16,PC) 2, (d8,PC,Xn) 4, (xxx).w 2, (xxx).l 2, #imm 4
- **SUBA.w** — Dn 4, An 4, (An) 4, (An)+ 4, -(An) 6, (d16,An) 4, (d8,An,Xn) 6, (d16,PC) 4, (d8,PC,Xn) 6, (xxx).w 4, (xxx).l 4, #imm 4
- **SUBX.b** — Dn 0, -(An) 2
- **SUBX.l** — Dn 4, -(An) 2
- **SUBX.w** — Dn 0, -(An) 2
- **SWAP** — Dn 0
- **Scc** — (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **TAS** — Dn 0, (An) 10, (An)+ 10, -(An) 12, (d16,An) 10, (d8,An,Xn) 12, (xxx).w 10, (xxx).l 10
- **TRAPV** — - 0
- **TST.b** — Dn 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **TST.l** — Dn 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **TST.w** — Dn 0, (An) 0, (An)+ 0, -(An) 2, (d16,An) 0, (d8,An,Xn) 2, (xxx).w 0, (xxx).l 0
- **UNLINK** — An 0


Read the pattern: **0, 2, 4, 6, 10 — and the two-clock steps are the effective address's own work.** A
`-(An)` costs 2 whatever the family; a `(d8,An,Xn)` costs 2 (4 where the family is already 2); a long
operation costs 2 more than the word one for a register destination (`CLR.l Dn` 2 against `CLR.w Dn` 0);
`TAS` is 10 because its read-modify-write is the reference's `t` transaction, 10 clocks, not a bus cycle;
`RESET`'s 128 are the 124 the manual's `RESET` asserts plus its fetch. That is the same shape `#74` found in the read's placement: **a per-EA script, not a
clause** — and the numbers are small enough to be a small table.

## 5. The five classes that are not a table

Each of these is measured exactly, and each is a *counter*, not a lookup: the value is one the core
already has in its hand at the moment it executes.

- **Shifts: `residue = base + 2 x count`**, `base` = 2 for `.b`/`.w` and 4 for `.l`, `count` = `Dn & 63`
  for the register form or the immediate (0 → 8) for the other. **177 314 records, 0 exceptions** — the
  family that holds 83 % of the varying records is one line of arithmetic.
- **`Bcc`: 8 clocks not taken, 10 taken** (bus 1 against 2) — the manual's numbers, measured; 13 records
  of the `$FF` long form read 12.
- **`DBcc`: 10 taken, 12 not** (`Dn` decremented and the branch retaken).
- **`Scc` on a data register: 4 clocks false, 6 true**; on memory one figure (`0` residue).
- **`MULU` 34–64 and `MULS` 36–64 clocks; `DIVU` 76–130 and `DIVS` 122–150**, in steps of 2, each with a
  short exit when the divide overflows (`DIVU` 6 or 8, `DIVS` 12–16). The counter is the operand's own
  bits: `34 + 2 x popcount(source)` reproduces 4 825 of the 4 990 `MULU` records exactly and lands 2 high on
  the rest — the reference's loop runs one iteration more than the naive popcount for a quarter of the
  values. That is a build's detail, not a study's: the class is data-dependent and no table can hold it.

## 6. The one open datum

**`BCHG`/`BCLR`/`BSET` on a data register pay 6 or 8 clocks on the same shape**, about half each
(`BCHG` 1 301 records: 643 at 6, 658 at 8; `BCLR` and `BSET` the same; `BTST` on a data register, 2 clocks,
does not vary). Nothing in
the record separates the two: not the bit index (register or immediate), not the bit being set or clear,
not the operand's value or its sign, not the flags, not the position in the file. Two records of the same
opcode, the same operand shape and the same operands differ. Until the reference's own state machine says
why, this family cannot be graded — settle it from CLK's source the way row 58 was settled, not by
guessing a rule (row 57 is what a guessed rule costs).

## 7. The ruling: reported now, graded when the table is data

Three shapes, in the order they cost:

1. **Reported, as today** (the column stays a diagnostic, and this page is the record). Cost: nothing.
   What it does not buy: nothing verifies the tick table on any run, so it rots like the corpus itself did.
2. **Graded by `cpu-tests` on the plain class** — the core accrues the residue (`EaAddressAsync`'s mode
   plus the instruction's own work, the `#74` shape), the harness compares `4 x bus + residue` against
   `length` and fails the file when it differs. Cost: the table in §4 as data, the five counters of §5,
   the §6 datum settled; the exception class (§2) excluded with a named reason, because its `length` is an
   entry's and the entry is `#72`'s contested frame. Payoff: 805 512 records become a verdict where 18
   used to agree by accident, and the tick table cannot rot silently. **It cannot be a `validate.sh` gate
   row** — the corpus is 194 MB and 26 s and stays out of the gate by design (`design/validation.md` §3);
   "graded" here means the corpus's own run has a pass/fail number, which it does not today.
3. **The residue on the bus timeline** — the core waits the residue before its next access, so the
   internal clocks become *when the machine's instants happen* rather than a diagnostic. This is the only
   shape that changes emulated behaviour (a cycle-counting demo, a raster-timed effect, `_hz_200`-free
   timing loops), and it costs both folds and the boot's frame numbers: the owner's re-bless, not a
   session's. It is also the shape in which the numbers stop being free: a field written per instruction is
   rent unless it lives behind `#if DEBUG`, and behind `#if DEBUG` it cannot drive Release.

**The recommendation is 2, and not yet** — first `#42`/`#49` (the perf ladder this page does not touch),
because 2 is a build with no behavioural payoff and 3 is the one that has one. What this page settles is
that the table is finite (1 231 shapes, five counters, one datum to read from the source), so 2 or 3 is a
build order rather than a research question — which is what ADR 0009 §3 needed.

## 8. Reproduce

```
scripts/corpus-length-census.py --dir tests/680x0/full --table    # the numbers on this page, 20 s
scripts/corpus-length-census.py --dir tests/680x0/full --shapes   # every varying shape, one line each
scripts/corpus-length-census.py                                   # the vendored sample, 0.5 s
```
