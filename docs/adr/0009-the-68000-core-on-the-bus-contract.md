# The 68000 core is three nested machines on one bus contract

The CPU's execution maps onto the engine's five verbs without a private calling convention: a bus
cycle is one masked control record, one subscription to the System Control Unit's answer, one data sample that resolves
on the stack and one release record — and its cost is the elapsed machine time, never a returned
number. Inside the core the machine is three nested state machines, each with its own state field, a
completion tag as its only return, and the clock as the only cycle account; bus requests leave the core
at three leaves only. Instruction cost is arithmetic on the M68000 user's manual's own rows
(`4 clocks × bus cycles + internal residue`), and the prefetch, reset and interrupt-sampling points come
from documented sources rather than from reconstruction.

**Status**: accepted (2026-11-07), on the owner's answers during ticket #9's grilling session.
Tickets: #9 (this core), #10 (walking skeleton), #8 (suspension-cost prototype), #11 (determinism).

> ### Correction — 2026-11-11 (ticket #31, owner's ruling)
>
> The virtual chip this file calls *Memory* is the **System Control Unit**, and the DRAM and the TOS
> ROMs are no longer members with pins: they are plain data structures (`Ram`, `Rom`) inside it —
> stores with no timeline, no record, no subscription and no wake. Every statement below about the
> counter, the owner equation, the clients, the beam, the buffer or the leaf stands unchanged; only
> the name and the store boundary moved. Owner's words: *"GSTMCU is too cryptic when you want to
> understand the machine, what about keeping only three letters of the name : SystemControlUnit ?"*
> See [ADR 0014](0014-the-stores-are-data-structures.md) and `design/system-control-unit.md`.

## Context

- The engine is fixed: ADR 0003 (a chip owns its clock), ADR 0004 (a wire is an append-only log; a bus
  is one wire with lane masks), ADR 0005 (the System Control Unit is a chip on the same rules; the bus is the authority
  on cost), ADR 0006 (two queue entries, two read shapes), ADR 0007 (order is `(instant, seq)`),
  ADR 0008 (five verbs, two write shapes, the run-ahead horizon, anchors-not-samples, the digest).
- The owner fixed the core's structure in round 4, and it is the one place this design departs from all
  three vendored cores, which chose a flat dispatch:
  > "the 68000 is **nested state machines** — instruction/interrupt machine, nested instruction-specific
  > machine after fetch, nested addressing-mode machines for operands."
- The bus was the constraint that shaped everything else: the CPU cannot know when the System Control Unit will answer,
  so the vocabulary of `research/core-comparison.md` §5.3 (`bus.read(Access) -> {data, cycles}`) was
  wrong at the root. ADR 0005's own sentence — "a read is `await Read(t_response, dataWire)`" —
  presumed the same knowledge.
- The evidence for the hardware claims is `research/cpu-prefetch-and-interrupt-sampling.md`; the
  structure and its consequences are `design/cpu-core.md`; the code shape is
  `design/cpu-core-sketch.cs` (a sketch — implementation is #10).

## Decision

### 1. A bus cycle is two records and one subscription

1. `Write(t_assert, bus, mask, {AS low, R/W, FC0–2, A1–A23, D lanes if a write})` — one masked record.
2. `await ReadNext(t_assert, Answer)` — the System Control Unit chooses the instant from its own slot schedule, and the
   scheduler sets the CPU's clock to it before resuming.
3. At the answering instant, the data sample and the `BERR`/`VPA` samples **resolve on the stack**
   (the System Control Unit appended them before the answer record at that instant), then the CPU appends its release
   record: `AS` high and `Z` on the data lanes.

**No `Result.cycles`.** The CPU's cost is `t_answer − t_assert`, which is the instant its own clock
lands on. `Access`/`Result` survive only as a CPU-internal description of the bundle and of
`{data, faulted, vectorProvided}` — never as a calling convention (ADR 0005 invariant 1). Wait states
therefore need no mechanism, and the "round up to 4 clocks" rule is the System Control Unit's slot phase: the CPU is
capped at half the bus during display (video takes 100 % of the internal slots), and a bus master can
stretch an answer arbitrarily.

### 2. Three machines, one reused instance each, a tag as the only return

| Level | Holds | Emits a bus cycle |
|---|---|---|
| `InstructionMachine` (one per CPU) | PC, prefetch pointer, SR, fault record | the **fetch** |
| `OpMachine` (instruction-specific, from the opcode table) | opcode, size, operand descriptors | **never** |
| `AddressingMachine` (one reused instance, run once per operand) | mode, register index, step, address | the **operand** |

plus `IACK` in exception entry — the third and last bus-emitting leaf. Each level is a `switch` over its
own state field in a small driving loop; the only `await` is at a bus leaf, so a suspension suspends the
whole chain and resuming unwinds back up on the same instant (an unwind is not a crossing and costs no
queue entry).

**A nested machine returns a completion tag, never a number.** It advances the CPU's single clock itself
on its internal-cycle transitions and returns `Done.Ok`/`Done.Faulted`. A fault is a field the caller
reads, never an exception — no `longjmp`-equivalent exists in the design.

Dispatch at the root is unchanged from the reference cores (one 65536-entry function-pointer table, plus
Hatari's separate no-return table). The nesting costs three calls and three state fields per
instruction; it buys a nameable state, a completion instant and a loggable transition per level.

### 3. Cost is arithmetic on the manual's rows

The M68000UM ninth edition, chapter 7, already reports `periods (read cycles / write cycles)` — and its
totals include instruction fetch, operand reads and operand writes. So the design's rule *is* the
manual's decomposition: **`clocks = 4 × bus cycles + internal residue`**, with
`residue = periods − 4 × (reads + writes)`.

- Store `(periods, reads, writes)` **verbatim** per row, with the table number as its provenance.
- Compute the residue once at start-up; the core charges `4 × its own bus cycles + residue`.
- One debug assertion holds the two accounts together: if the core's access list and the table's
  read/write counts disagree, one of them is wrong and the assertion names the row.
- Data-dependent cases are **formulas, not rows** (`MULU/MULS` `38+2n`, `DIVU/DIVS` `140…158`,
  shifts/rotates `6+2n`, `MOVEM` per register).
- `InstrCost` and `EntryCost` from `core-comparison.md` §5.3 are **retired**: the first is dissolved by
  the arithmetic, the second by the entry script. **The `MicroOp` script is the only table left**, and it
  carries Level-2 placement with its clocks as steps, so Level 1 sums what Level 2 walks.
- The manual's figures assume no wait states, so they are **Level-1 oracles only**: at Level 2 a cycle's
  time is whatever the answer instant says.

### 4. The prefetch is three registers and one rule

Documented (Cwik 2005): `IRD` is the decode register, `IR` and `IRC` the two-word queue; **before any
instruction starts, its opcode and the next word are already fetched**; every instruction ends with the
same three steps (`IRC → IR`, fetch a new word `→ IRC`, `IR → IRD`); and an instruction performs exactly
as many fetch cycles as it has words.

- A fetch placed at instruction *start* overcharges every instruction by one bus cycle and is wrong
  even at Level 1.
- Level 1 implements the three registers, the closing steps, the per-word fetch count, and the
  instruction's **prefetch class** (0 = trailing fetch before the write, 1 = after, 2 = two fetches
  after). The class changes no cycle count — only the order a write and a fetch hit their addresses,
  which is the entire mechanism behind self-modifying code.
- Level 2 adds the exact per-opcode micro-op placement (`PREFETCH` steps in the script).
- `IRD` is architectural: it is the value a bus-error frame saves, so it must exist and be logged.

### 5. Reset is a vector-read sequence

Documented (UM §6.3.1): supervisor, trace off, mask 7, vector 0, nothing stacked; the first two words of
the reset vector become the SSP, the last two the PC. Cwik adds the four-step prefetch initialisation.
That is **six bus cycles** (two long reads + two fetches) before the first instruction runs, and the
first of them is the CPU's first suspension. The `RESET` *instruction* is a different thing: 124 clocks
of pin assertion, then continue.

### 6. Interrupts: sampled in the last bus cycle, taken at the boundary

- A request is taken when its level is **strictly greater** than `I2–I0`; level 7 ignores the mask and is
  edge-sensitive on the transition into 7 (UM §6.3.2).
- Pending interrupts are **detected between instruction executions** (UM §6.3.2); the pins are **latched
  during the instruction's last bus cycle** (UM Figure 5-11).
- *Inferred* (WinUAE `ipl_fetch_next`, Moira's deferred pin change): a level asserted **≥ 4 CPU clocks**
  before the end of the instruction is taken at that boundary; a later change is taken after the
  *following* instruction. This is why an interrupt legitimately appears one instruction late.
- The CPU **cannot subscribe to `IPL`** — it has one pending read, spent on the bus answer. So it samples
  with `SampleWithInstant(Ipl, t) -> (level, changedAt)` **on the wire's log**, and takes the interrupt iff
  `Accepts(level) && t − changedAt ≥ 4 CPU clocks`.

### 7. Exception entry is a script with the bus work in it

Four documented steps (UM §6.2.5): copy SR and set `S`, clear `T`; determine the vector (an acknowledge
cycle for interrupts, internal logic otherwise); stack PC and the saved SR on the SSP; PC ← `vector × 4`.
The acknowledgement sits **between the PCL stack write and the rest of the frame** (Figure 5-11:
`PCL → IACK → SR, PCH → vector fetch`), and the mask is raised before it.

Walked as data: `InternalClocks`, `PushWord`, `Acknowledge`, `ReadVector`. `VPA` is the autovector line
(`$18 + level`); a `BERR` during the acknowledge is a **spurious interrupt** (vector 24, short frame), not
a bus error; and **a fault inside a group-0 script is a double fault → HALT** — the hardware's rule, not
a modelling choice.

### 8. What is logged, and what is only observed

Anchors in the internal register log: `SR` on a semantic write (exception entry, `MOVE to SR`,
`ANDI/ORI/EORI to SR`, `RTE`); `PC` at a branch/jump target and at an exception entry; `IRD` at an
instruction boundary; a software write to a register. Everything else is arithmetic on demand.
A full per-instruction trace is an **observer stream in debug builds**, not the log.

A breakpoint is **not a suspension in the model**: the host stops popping the queue at a batch boundary,
so no record and no digest can differ.

## Invariants

1. Only `Read`/`Write` cross the chip boundary, and a bus cycle is never a call: a record, a
   subscription, a stack sample, a release.
2. No machine returns a cycle count; the clock is the only account, and it moves on a transition's
   internal cost or on a bus answer.
3. Bus requests leave the core at three leaves: fetch, operand, IACK.
4. Nothing in the core knows the memory map, the scheduler, or which device is answering.
5. A fault is a record, never an exception; a fault inside a group-0 script is a HALT.
6. `IRD` is architectural state and must be logged at instruction boundaries.
7. Instruction cost is arithmetic on the manual's rows; a table never asserts a cycle count the bus
   did not produce.

## Consequences

- **#10** implements this core shape: three machines, the bus leaf, the reset and prefetch rules, the
  entry script, and the two debug assertions (the manual-row agreement, the nested-entry budget). Its
  measurement of the whole model against the 40 % bill is unaffected by this ADR.
- **#8** measures one thing this ADR created beyond ADR 0008's two: the **per-instruction cost of the
  three nested entries**, against the 1 CPU clock threshold below.
- **#11** inherits no new determinism surface: the log query (`SampleWithInstant`) reads only records
  the log already holds, and the observer stream is host-side and off in release.
- **The System Control Unit (#20)** inherits the answering-instant contract: it chooses `t_answer` from its slot schedule,
  and the CPU's wait state *is* that choice — no separate wait-state mechanism exists anywhere.
- **ADR 0004** gains one sentence on retention: a wire's log must retain back to the earliest instant a
  live reader may still sample, and the watermark is the minimum over subscribers **and live sampling
  windows**.
- **ADR 0005**'s "a read is `await Read(t_response, dataWire)`" is superseded by §1 (dated revision note).
- **`research/core-comparison.md` §5.3** keeps its derivation and loses `Result.cycles`, `InstrCost`
  and `EntryCost` (dated correction block).
- **Vocabulary**: *bus cycle*, *prefetch class*, *internal residue* and *entry script* enter
  `GLOSSARY.md` where they are hardware facts rather than implementation.

## Considered options

- **`bus.read(Access) -> {data, cycles, faulted}`** (the §5.3 proposal). Rejected: a private calling
  convention ADR 0005 forbids, and a second source of truth for time — the number would be computed by
  the System Control Unit anyway, and at Level 2 it *is* the answer instant.
- **`await WriteAndWait` as the whole cycle.** Rejected: it resumes behind the receivers of its own record
  at the same instant, so it cannot express a wait for an *answer* at an instant the other chip chooses.
- **Async methods as the nesting itself** (the call stack is the machine). Rejected: the compiler's state
  machine has no state that can be named, logged or stepped — invisible to the digest and to the UI, and
  not what the owner specified.
- **One machine object per opcode.** Rejected: an allocation per instruction for no gain.
- **A cycle count returned up the chain of machines.** Rejected: a second account of time, contradicting
  §1 one level down.
- **Keeping `InstrCost` as `(instruction, mode, size)` data.** Rejected: the manual's own decomposition
  makes the external part arithmetic, so the table would duplicate what the bus already says and could
  drift from it.
- **Deferring the prefetch class to Level 2.** Rejected: it changes no cycle count, costs a two-bit field,
  and is the only Level-1 behaviour self-modifying code can observe.
- **Subscribing the CPU to `IPL` to learn when the level changed.** Rejected: a chip has exactly one
  pending read, and it is spent on the answer; the log already knows the instant.

## Falsifier

1. **Mechanism.** If an already-satisfied read costs more than ~20 ns in situ (#8), the `async`/`await`
   shape is replaced by hand-written state machines — the nesting is not reopened.
2. **Choice.** If the three nested entries cost more than **1 CPU clock (4 ticks) per instruction** —
   12.5 % of the cheapest instruction — the three machines stay but each level's driving loop collapses
   to a single `switch` call, with no second state field.
3. **Nothing.** The digest stream, the observer hooks and the anchor log are instrumentation; their cost
   reopens nothing.
4. If a cycle's measured duration disagrees with the manual row for the same instruction *with no wait
   states*, the core's access list is wrong (or the transcription is) — the assertion in §3 names which.
5. If an instruction ever completes without issuing a read, the horizon rule (ADR 0008 §3) is broken by
   this design, not by that instruction.
