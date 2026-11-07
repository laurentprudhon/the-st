# The 68000 core — structure around the bus interface

Implementation-facing companion to ticket **[#9](https://github.com/laurentprudhon/the-st/issues/9)**,
in progress. Decisions already taken in this ticket are marked *settled*; sections marked *open* are
still being grilled and must not be implemented as if decided. Evidence for the hardware claims is in
[`research/cpu-prefetch-and-interrupt-sampling.md`](../research/cpu-prefetch-and-interrupt-sampling.md);
the code shape is [`cpu-core-sketch.cs`](cpu-core-sketch.cs) — a sketch, not an implementation
(implementation is #10).

Engine rules this file assumes: ADR 0003 (a chip owns its clock), ADR 0004 (a wire is an append-only
log; a bus is one wire with lane masks), ADR 0005 (Memory is a chip on the same rules; the bus is the
authority on cost), ADR 0007 (order is `(instant, seq)`, no chip rank), ADR 0008 (five verbs, two write
shapes, the run-ahead horizon, anchors-not-samples, the digest).

## 1. The bus cycle — settled

A bus cycle is **two records and one subscription**, written in the CPU's own code:

1. `Write(t_assert, bus, mask, {AS low, R/W, FC0–2, A1–A23, D lanes if a write})` — one masked record
   (ADR 0004: the address bus is one wire with 24 lanes, the data bus one with 16).
2. `await ReadNext(t_assert, Answer)` — Memory chooses the instant from its slot schedule.
3. At the answering instant: the data sample and `BERR`/`VPA` samples **resolve on the stack** (Memory
   appended them before the answer record at the same instant), then the CPU appends its release
   record — `AS` high and `Z` on the data lanes.

**There is no `Result.cycles`.** The cost of a cycle is `t_answer − t_assert`: elapsed machine time,
which is the same time the CPU's clock lands on. `core-comparison.md` §5.3's `Access`/`Result` survives
as a CPU-internal description of the bundle and of `{data, faulted, vectorProvided}` — never as a
calling convention (ADR 0005 invariant 1). Wait states therefore need no mechanism: a slow device is a
chip that answers later. The "round up to 4 clocks" rule is Memory's slot phase and is invisible here.

Consequences: `DTACK` answering at least one bus cycle out makes the per-instruction suspension
structural (a fetch read is never already-satisfied), and `IACK` is the same three steps with
`FC=111` — only the lanes it reads afterwards differ.

*Corrections owed when this ticket closes:* ADR 0005 §Decision's `await Read(t_response, dataWire)`
gets a dated revision note; `research/core-comparison.md` §5.3's `Result.cycles` gets a correction
block (the derivation stays, the field is withdrawn).

## 2. The three nested machines — settled

Three machine *kinds*, **one reused instance each** (no object per opcode, no allocation per
instruction):

| Level | Holds | Emits a bus cycle |
|---|---|---|
| `InstructionMachine` (root, one per CPU) | PC, prefetch pointer, SR, fault record | the **fetch** |
| `OpMachine` (instruction-specific, from the opcode table) | opcode, size, operand descriptors | **never** |
| `AddressingMachine` (one reused instance, run once per operand, in sequence) | mode, register index, step, computed address | the **operand** |

(plus `IACK` in exception entry — the third and last bus-emitting leaf.)

Each level is a `switch` over its own state field in a small driving loop; the only `await` in the core
is at a bus leaf. A suspension therefore suspends the whole chain, and resuming at `t_answer` unwinds
back up **on the same instant** — an unwind is not a crossing and costs no queue entry.

**The return protocol: a nested machine returns a completion tag, never a number.** It advances the
CPU's single clock itself on its internal-cycle transitions (`AdvanceCpuClocks`) and returns
`Done.Ok` / `Done.Faulted`. A fault is a *field* the caller reads (`FaultRecord`), never an exception —
no `longjmp`-equivalent exists in the design.

**Bus requests are emitted at three leaves only**: the fetch, the operand access, and `IACK`. An
instruction *names* operands; the addressing machine *performs* them (a `JSR`'s stack write is a
predecrement operand on A7).

*What it buys and costs.* Dispatch at the root is identical to all three reference cores — one
65536-entry function-pointer table (plus Hatari's separate no-return table for branches, JMP, JSR, RTS,
RTE, TRAP) — so the nested design costs three calls and three state fields per instruction, not a
slower dispatch. What it buys is a nameable state, a completion instant and a loggable transition per
level, which is what makes the core steppable and digestible. **Falsifier:** if the per-instruction
nested-entry cost measured in #8 exceeds the flat dispatch by more than the budget fixed there, keep the
three machines but collapse each level's driving loop to a single `switch` call — do not re-litigate the
nesting.

## 3. The prefetch — the rule, and where it lives

Documented (Cwik 2005; the manual itself barely mentions prefetch):

- three internal registers: `IRD` (decode), `IR` and `IRC` (the two-word queue);
- **before any instruction starts, its opcode and the next word are already fetched**;
- every instruction ends with the same three steps: `IRC → IR`, fetch a new word `→ IRC`, `IR → IRD`;
- **an instruction performs exactly as many fetch cycles as it has words** (NOP: one, at `PC+4`);
- extension words are fetched in a first stage, and only the last of those fetches enters `IRC`;
- where the trailing fetch sits relative to a *write* is the instruction's **prefetch class**
  (0 = before the write, 1 = after, 2 = two fetches after) — the only observable difference between
  instructions, and the thing self-modifying code sees.

So: **cost is arithmetic, not a table.** An instruction's time is `(bus cycles × 4 CPU clocks) +
its declared internal clocks`, where the bus cycles are its operand accesses plus its fetch cycles.
`MOVE.W (An)+,Dn` is 8 clocks = exactly two bus cycles, with no `InstrCost[instr, mode, size]` entry
needed. A fetch placed at instruction *start* would overcharge every instruction by one cycle and is
therefore wrong even at Level 1.

What the prefetch is concurrent with: **the instruction's internal work**, never its operand bus cycles.
The 68000 has one bus, so a fetch and an operand access can only be serialised — which is what our
engine does naturally, since both suspend the CPU.

Level 1 implements: the three registers, the three closing steps, the fetch-per-word count, and the
class of the trailing fetch (a two-bit field per opcode; it costs nothing and it is the only datum the
self-modifying-code effects need). Level 2 adds: the exact per-opcode micro-op placement (`PREFETCH`
steps inside the script), which is also what makes border tricks and I/O read order exact.

**Settled:** the class *is* modelled at Level 1 — one two-bit `PrefetchClass` field per opcode, honoured
at the single trailing-fetch call site. It changes no cycle count, and it is the only Level-1 behaviour
self-modifying code and a fetch past the end of a block can observe. The exact per-opcode micro-op
placement stays Level 2.

## 4. Reset: the first cycle

Documented (UM §6.3.1): supervisor, trace off, mask 7, vector 0, and **nothing stacked** — the first
two words of the reset vector become the SSP, the last two become the PC, then execution starts there.
Cwik adds the four-step prefetch initialisation. On a 16-bit bus that is **six bus cycles** (two long
reads + two fetches) before the first instruction runs, and the first of them is the CPU's first
suspension at its own instant. Reset is therefore a phase, not an initialisation shortcut.

The `RESET` *instruction* is a different thing: it asserts the `RESET` pin for 124 clocks and continues
with the next instruction (UM §6.3.1).

## 5. Interrupt sampling and exception entry

Three documented facts, and one inferred refinement:

- a request is taken when its level is **strictly greater** than `I2–I0`; level 7 ignores the mask and
  is **edge**-sensitive on the transition into 7 (UM §6.3.2);
- pending interrupts are **detected between instruction executions** (UM §6.3.2);
- the pins are **latched during the instruction's last bus cycle** (UM Figure 5-11, whose first phase is
  "LAST BUS CYCLE OF INSTRUCTION" carrying the `IPL2–IPL0 SAMPLED` marker), and the acknowledge runs
  **after the PCL stack write**: `PCL → IACK → SR, PCH → vector fetch`;
- *inferred* (WinUAE `ipl_fetch_next`, Moira's deferred pin change): a level asserted **≥ 4 CPU clocks**
  before the end of the instruction is taken at that boundary; a later change is taken after the
  *following* instruction. This is why an interrupt legitimately appears one instruction late.

Our shape: sample at the answering instant of the last bus cycle, with the level's own change instant
read from the wire log — `(level, changedAt) = SampleWithInstant(Ipl, t)` — and take it iff
`Accepts(level) && t - changedAt >= 4 CPU clocks`. **The CPU cannot subscribe to `IPL`** (it has exactly
one pending read, spent on `Answer`), so the log query replaces WinUAE's two pin-history fields and adds
no scheduler primitive.

Exception entry is a **phase script with the bus work in it**, walked as data: internal clocks, frame
words as real bus cycles on the SSP, the acknowledge between PCL and the rest for interrupts, then the
vector fetch. A fault inside a group-0 script is a **double fault → HALT** — the hardware's rule, not a
modelling choice. A `BERR` during the acknowledge is a **spurious interrupt** (vector 24, short frame).

## 6. What is logged, and what is not

ADR 0008 §6 applies unchanged: internal state is a value log stamped with the chip's own clock, anchors
rather than samples. For the CPU, the **anchors** are:

- `SR` on every write that changes it semantically (exception entry, `MOVE to SR`, `ANDI/ORI/EORI to SR`,
  `RTE`) — not per instruction;
- `PC` at a branch/jump target and at an exception entry (the two places the instruction stream diverges);
- `IRD` at an instruction boundary (it is architectural: it is what a bus-error frame saves);
- a software write to a register (`MOVE` to `USP`, `MOVE to CCR`, `MOVEC`-like cases on later parts).

Everything else — an arithmetic result, a flag update between anchors — is arithmetic on demand.
A full per-instruction trace of PC/SR is a **debug-build observer stream**, not the log: it would be
millions of records a second for no information.

## 7. Breakpoints, stepping, tracing

- A breakpoint is **not** a suspension in the model: the host simply stops popping the queue at a batch
  boundary (one instant, fully drained), so no record and no digest can differ.
- The live events are the `ICpuObserver` hooks: instruction start, register write, bus cycle, bus
  answer, interrupt sampled/taken, exception entry, halt. An address breakpoint is a predicate on the
  bus-cycle hook; a stop on "interrupt received" is the sampled hook plus the level.
- Retrospective tracing needs no hook: every bus cycle is already a record with its instant (ADR 0004),
  which is what the UI draws.
- Single-step = one batch; stepping *inside* an instruction is also one batch, because a bus cycle is an
  exchange between two instants.

## 8. Cycle costs as data, and where the numbers come from

**The primary source exists and is already in hand.** M68000UM ninth edition, **chapter 7
"MC68000 Instruction Execution Times"**: Table 7-1 (effective-address calculation), 7-2…7-4 (MOVE),
7-5 (standard), 7-6 (immediate), 7-7 (single operand), 7-8 (shift/rotate), 7-9 (bit manipulation),
7-10 (conditional), 7-11 (JMP/JSR/LEA/PEA/MOVEM), 7-12 (multiprecision), 7-13 (miscellaneous), 7-14
(MOVEP), 7-15 (exception processing). No research ticket is needed, and the vendored cores are not the
source — they are a cross-check for the one thing the manual does not tabulate (per-opcode prefetch
*placement*).

**Its rows are already in this design's shape.** Every entry is `periods (read cycles / write cycles)`,
and the totals **include instruction fetch, operand reads and operand writes**. So the design's rule
`clocks = 4 × bus cycles + internal clocks` is exactly the manual's own decomposition, with a
per-case residue:

| Table 7-1 row | Manual | 4 × cycles | Internal residue |
|---|---|---|---|
| `(An)` byte | 4 (1/0) | 4 | 0 |
| `-(An)` word | 10 (2/0) | 8 | 2 |
| `(d16,An)` word | 16 (4/0) | 16 | 0 |
| `(xxx).L` long | 32 (8/0) | 32 | 0 |
| `#imm` long | 16 (4/0) | 16 | 0 |

and the same arithmetic on Table 7-15: interrupt entry `72 (9/6)` → 60 clocks of bus work + 12
internal; illegal instruction `62 (8/6)` → 56 + 6; address error `94 (8/14)` → 88 + 6.

**So the three tables of `core-comparison.md` §5.3 collapse to one**, and the design test they were
meant to satisfy survives intact:

- **`InstrCost` is dissolved.** What remains is the *residue*: internal clocks per (opcode family, size,
  EA class), plus the handful of data-dependent formulas the manual states (MULU/MULS `38+2n`, DIVU/DIVS
  `140…158`, shifts/rotates `6+2n`, `MOVEM` per register).
- **`EntryCost` is dissolved.** Exception entry's cost is its script's bus cycles plus its internal
  clocks; Table 7-15 is the oracle for the total (and its "includes the first handler fetch" is our
  trailing prefetch, so the check is complete).
- **`MicroOp` stays, and is the only table** — the Level-2 placement script, with the internal clocks
  carried as steps, so Level 1 sums what Level 2 walks (Moira's one-source-two-consumers principle,
  without the compile-time switch).

**Representation rule.** Store the manual's `(periods, reads, writes)` **verbatim**, with the table
number as its provenance in the comment; compute the residue once at start-up as
`periods − 4 × (reads + writes)`. Then the core charges `4 × its own bus cycles + the residue`, and a
single debug assertion can hold the two accounts together: *if the core's access list and the table's
read/write counts disagree for an instruction, one of them is wrong and the assertion names the row.*
That is the falsifier this table needs, and it is why the table is evidence rather than design: the
teachable rule stays "4 clocks per bus cycle, plus declared internal clocks".

**Settled** (owner's answer): rows verbatim, residue computed, the one assertion, `MicroOp` the only
remaining table. `InstrCost` and `EntryCost` are struck from `core-comparison.md` §5.3's proposal —
the first is dissolved by arithmetic, the second by the entry script.

## 9. Still open in this ticket

- **The dispatch falsifier's threshold**, to be fixed with #8's measurement.
- Whether `SampleWithInstant(wire, t)` belongs on the wire's log API or on the scheduler's.
- Which CPU state is an anchor is fixed in §6 but not yet re-read against the tables of §8 (a residue
  that varies with *data*, e.g. `MULU`, is the one place a logged anchor could lie about a cost).
