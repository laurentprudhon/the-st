# The 68000 prefetch phase, its reset sequence, and when an interrupt is sampled

Findings for ticket #9 (the core's structure around the bus interface). Two questions the ticket
could not answer from the vendored cores alone: **what the prefetch is concurrent with and how the
machine starts its first cycle**, and **when exactly the IPL pins are sampled and how exception
entry is ordered**. Every claim below carries one of the project's labels: `documented` (a primary
source says so), `derived` (arithmetic from documented constants), `inferred` (a reference
implementation's code implies it), `unknown`.

Sources

- **UM** — *M68000 8-/16-/32-Bit Microprocessors User's Manual*, Motorola, ninth edition (1993),
  `https://www.nxp.com/docs/en/reference-manual/MC68000UM.pdf`. §5.3 and Figure 5-11 (interrupt
  acknowledge cycle timing), §6.2.4 (stack frames), §6.2.5 (exception processing sequence), §6.3.1
  (reset), §6.3.2 (interrupts), §6.3.4 (spurious interrupt).
- **CWIK** — Jorge Cwik, "Instruction prefetch on the Motorola 68000 processor", v1.3, 2005,
  `https://pasti.fxatari.com/68kdocs/68kPrefetch.html`. The only source found that documents the
  prefetch queue's structure and its per-instruction placement; the UM itself barely mentions
  prefetch (three passing mentions in 400 000 characters).
- **WINUAE/HATARI** — `hatari/src/cpu/newcpu.c:4995-5045` (`ipl_fetch_now`, `ipl_fetch_next`,
  `update_ipl`) and `:5505-5525` (the run loop's `do_specialties` call and the `ipl[0] = ipl_pin`
  shift). The oldest and most-tested model of the sampling delay.
- **MOIRA** — `neost/extern/moira/Moira/Moira.cpp:331-341`, `:502-519`, `:912-937`
  (`setIPL`, the deferred pin change, `CHECK_IRQ`, the level-sensitive STOP poll).

---

## 1. The prefetch queue: three registers, not two

**`documented` (CWIK)** The 68000 has a two-word prefetch queue **plus a one-word instruction decode
register**: `IRC` and `IR` are the queue, `IRD` is what reaches the decoder. Cwik's justification for
the 68000 is an appendix sentence about the 68010 — "a two word prefetch queue in addition to a one
word instruction decode register" — which he asserts is exactly valid for the 68000 as well (the
68010 differs only in loop mode).

**`documented` (CWIK)** "Before **any** instruction starts execution, its opcode word and the next one
were already read. The second word, depending on the instruction, could be an extension word for the
current instruction, or the opcode for the next one."

**`documented` (CWIK)** Every instruction ends with the same three steps, regardless of complexity:

1. `IRC → IR`
2. fetch a new word from the instruction stream `→ IRC`
3. `IR → IRD`

and the reset sequence adds one step in front (load `IRC` from memory before the loop). This is the
whole prefetch mechanism; a NOP is exactly those three steps plus `PC += 2`.

**`documented` (CWIK)** — the rule that makes cost arithmetic possible instead of tabulated:

> "Most instructions perform as many prefetch cycles as the total number of words in the instruction.
> For example NOP … performs one prefetch at the address two words (four bytes) above the location of
> the NOP. `MOVE $10000,$20000`, one possible form of the longest instruction, performs five prefetch
> cycles."

**`documented` (CWIK)** Instructions with extension words do it in two stages: stage 1 performs one
fetch per extension word (most extension words are consumed directly and never enter the queue; only
the **last** fetch of stage 1 goes into `IRC`, because that word is the next opcode, not an extension
word), then stage 2 performs the three steps above.

**`documented` (CWIK)** Fetch cycles are **fixed in microcode**: "Different instructions might perform
the prefetch access differently … but for a specific opcode, prefetches are always executed exactly at
the same cycles." And any wait state stalls the entire CPU, because it is one microcode sequence.

### What the prefetch runs concurrently with

**`documented` (CWIK)** — with the instruction's **internal work**, not with its operand bus cycles.
The 68000 cannot execute two instructions at once, but it reads and decodes the next instruction while
the current one executes. The big win is on instructions with many internal cycles (MUL, DIV, shifts):
prefetch slots into idle bus time with no effect on the current instruction.

**`derived`** — a prefetch can never overlap an operand bus cycle: both are bus cycles, the machine has
one bus, and a bus cycle is 4 CPU clocks before wait states. So on paper the 68000's bus is
single-threaded and the "concurrency" is prefetch-versus-internal-logic only. That is exactly the
distinction our engine needs, because in our design **every** bus cycle suspends the CPU coroutine and
internal work is what happens between them.

### Self-modifying code is an ordering question, not a size question

**`documented` (CWIK)** Where the trailing prefetch sits relative to the instruction's **write** cycles
is what self-modifying code observes, and it sorts instructions into three classes:

| Class | Trailing prefetch relative to the write | Examples (CWIK) |
|---|---|---|
| 0 | **before** the write | TAS, BCHG/BCLR/BSET, shifts/rotates, Scc, ADDX/SUBX, ABCD/SBCD, `MOVE <ea>,-(An)`, `PEA` (non-absolute) |
| 1 | **after** the write | most `MOVE` variants, `JSR`, `PEA` (absolute), `LINK`, `MOVEP`, `MOVEM` |
| 2 | two prefetches after the write | `BSR`; `MOVE memory,(xxx).L`; exceptions |

**`derived`** The class does **not** change an instruction's cycle count — one bus cycle is one bus
cycle — it changes only the order of records at different addresses. So class is a Level-1 *ordering*
datum and needs no cost table: a two-bit field per opcode, or nothing at all if self-modifying code is
accepted as out of the Level-1 baseline.

---

## 2. How the machine starts: the reset sequence

**`documented` (UM §6.3.1)** Reset is the highest-priority exception. The processor is forced into
supervisor state, trace is forced off, the interrupt mask is set to level 7, and the vector number is
0 (the reset vector at location 0, supervisor program space). Because register contents cannot be
trusted, **neither PC nor SR is stacked**:

> "The address in the first two words of the reset exception vector is fetched as the initial SSP, and
> the address in the last two words of the reset exception vector is fetched as the initial program
> counter. Finally, instruction execution is started at the address in the program counter."

**`documented` (CWIK)** The prefetch is then initialised: load `IRC`, `IRC → IR`, reload `IRC`,
`IR → IRD`.

**`derived`** On a 16-bit bus with no long accesses, the first instruction therefore begins after
**six bus cycles**: two long reads (SSP at 0x000000, PC at 0x000004 — each two word cycles) plus the
two prefetch reads at PC and PC+2. In our engine that means the CPU's very first act is a bus cycle at
its own instant, which is exactly the "every iteration suspends" invariant — reset needs no special
case beyond entering a `Reset` phase instead of `Refill`.

**`documented` (UM §6.3.1)** The `RESET` **instruction** is not the reset exception: it asserts the
`RESET` pin for 124 clocks to reset external devices and then continues with the next instruction. The
initial reset is a host-side condition (`RESET` and `HALT` asserted ≥ 100 ms at power-up), not machine
time.

---

## 3. When an interrupt is sampled

Three statements, from three different levels of authority, and they are **not** in conflict:

**`documented` (UM §6.3.2)** — acceptance:

> "Interrupt requests arriving at the processor do not force immediate exception processing, but the
> requests are made pending. **Pending interrupts are detected between instruction executions.** If the
> priority of the pending interrupt is lower than or equal to the current processor priority, execution
> continues with the next instruction …"

**`documented` (UM, Figure 5-11, "Interrupt Acknowledge Cycle Timing Diagram")** — the sampling point
inside the instruction. The figure lays the acknowledge out as four phases and marks `IPL2–IPL0
SAMPLED` **inside the first one**, whose caption is *"LAST BUS CYCLE OF INSTRUCTION (READ OR WRITE)"*:

```
LAST BUS CYCLE OF INSTRUCTION  →  STACK PCL (SSP)  →  IACK CYCLE  →  STACK AND VECTOR FETCH
        ↑ IPL2-IPL0 SAMPLED          (frame word)     (vector no.)    (rest of frame, then vector)
```

So the pins are latched during the instruction's **last bus cycle**, and the decision is taken at the
instruction boundary that follows it. Both are documented, and the second one is why the first bus
cycle of the acknowledge is a *stack write* (PCL) and not the acknowledge itself.

**`inferred` (WINUAE/HATARI)** — the stability window, which the manual never states. `ipl_fetch_next`
(`newcpu.c:5006`) carries the comment:

> "ipl check max 4 cycles before end of instruction. interrupt starts after current instruction if IPL
> was changed earlier. if not early enough: interrupt starts after following instruction."

and `update_ipl` (`newcpu.c:5022`) adds "check if 68000/010 interrupt was detected mid memory access,
2 cycles from start of memory cycle". So a level asserted **≥ 4 CPU clocks** before the end of the
instruction is taken at that boundary; asserted **2–4 clocks** before the end, it is taken after the
*following* instruction; and a change 2 clocks into a memory cycle is latched into the next slot of a
two-deep pin buffer (`regs.ipl[0]`/`regs.ipl[1]`).

**`inferred` (MOIRA)** The same effect, differently expressed: a pin change is deferred by less than
two cycles before it can be observed (`Moira.cpp:331-341`, `NEOST_IPLFETCH`), the check happens at
`sync` points, and `STOP` is level-sensitive and re-polls rather than waiting for an edge.

**`documented` (UM §6.3.2)** Level 7 is the exception: it ignores the mask, and "an interrupt is
generated each time the interrupt request level changes from some lower level to level 7" — an
**edge** on the transition into 7, not a level.

**Consequence for the design (`derived`).** The two facts compose into one small rule, and it needs no
pin history register in the model:

- sample the IPL wire at the answering instant of the instruction's **last bus cycle**;
- the wire's own log already carries *when* the value changed, so ask the log, not a field:
  `(level, changedAt) = SampleWithInstant(Ipl, t)`;
- take it now iff `level > mask` (or `level == 7`) **and** `t - changedAt >= 4 CPU clocks`;
  otherwise it is pending and the boundary after the next instruction takes it.

That is WinUAE's arithmetic with its two bookkeeping fields replaced by the log we already keep
(ADR 0004: the log is the history, and a past sample must stay correct).

---

## 4. Exception entry, ordered

**`documented` (UM §6.2.5)** Four steps, in this order:

1. internal copy of SR; `S` set, `T` cleared; for reset and interrupts the interrupt mask is updated to
   the new level;
2. the vector number is determined — for interrupts by a bus cycle "classified as an interrupt
   acknowledge cycle", for everything else by internal logic; the vector address is `vector × 4`;
3. the current PC and the saved SR are stacked on the SSP (skipped for reset). Group 1 and 2 use the
   short four-word frame; bus and address error stack additional information;
4. PC ← the longword at `vector × 4`, and execution resumes there.

**`documented` (UM §6.3.2 + Figure 5-11)** For an interrupt, step 2's acknowledge cycle sits **between
the PCL stack write and the rest of the frame**: PCL → IACK → SR, PCH → vector fetch. The priority mask
is raised to the acknowledged level *before* the IACK cycle.

**`documented` (UM §5.3)** The acknowledge cycle itself: `FC2–FC0 = 111`, `A3–A1` = the acknowledged
level, all other address lines high, `R/W` high, both strobes low. The device answers with a vector
number on `D0–D7` **and** `DTACK`, or with `VPA` for an autovector (`$18 + level`, i.e. 24 + level);
"`DTACK` and `VPA` should never be simultaneously asserted".

**`documented` (UM §6.3.4)** A `BERR` during the acknowledge is a **spurious interrupt**: vector 24,
with a short frame — not a bus error frame. This matters for us because our bus answers one `Answer`
port group; the CPU must distinguish "faulted while acknowledging" from "faulted anywhere else".

**`derived`** The frame's write order and the interleaved acknowledge mean exception entry cannot be
"push three words then read the vector": it is a **script of bus cycles**, which is why Moira models it
as phases with hooks, and why our entry table is data rather than code.

**`inferred` (HATARI `newcpu.c:3092-3108`, `research/core-comparison.md` §1.6)** The 68000's
group-0 frame is **seven words**, carrying the special status word, the faulted address, function
codes, read/write, and `IRD` — the decode register's value, which is why the prefetch state is
architecturally visible on a bus error. Cwik reaches the same conclusion from the other side: "the
`IRD` … content is the one that is stored in the exception frame on a bus or address error."

**`documented` (UM §6.2.5, 6.3.2)** A fault *inside* exception processing terminates the processor:
the 68000 halts rather than taking a second fault. Our rule — "a fault inside a group-0 script is a
double fault → HALT" — is therefore a hardware rule, not a modelling convenience.

---

## 5. What this changes in the design

1. **The prefetch is three registers (`IRC`, `IR`, `IRD`), not a two-slot buffer with a validity flag.**
   The decode register is architecturally visible on a bus error, so it must exist and must be logged.
2. **Cost is arithmetic, not a table**: an instruction performs as many fetch cycles as it has words
   (plus one per extension word, which *is* its words), and internal work is whatever the opcode adds.
   `MOVE.W (An)+,Dn` is 8 clocks = exactly two bus cycles, matching the manual's timing summary with no
   `InstrCost[instr, mode, size]` entry.
3. **A fetch placed at instruction *start* overcharges every instruction by one bus cycle** (4 clocks,
   6 % of an instruction at this end of the set) and hides the fact that the hardware's fetch belongs
   to the *previous* instruction. Placement is therefore not cosmetic even at Level 1.
4. **The prefetch class (0/1/2) is an ordering datum, not a cost datum** — the only thing it changes is
   which of a write and a fetch hits a given address first, i.e. self-modifying code.
5. **Reset is a vector-read sequence, not an initialisation shortcut**: two long reads then two
   fetches, six bus cycles, and the first instruction never runs before they complete.
6. **The interrupt sample point is the last bus cycle of the instruction, and the accept point is the
   boundary after it.** The 4-clock stability window makes an interrupt legitimately land one
   instruction late; ignoring it turns a documented "delayed by one instruction" behaviour into an
   emulator bug report.
7. **The CPU cannot subscribe to `IPL`.** It has exactly one pending read (ADR 0006 invariant 3), which
   it spends on `DTACK`. So the level change must be read from the wire's log with its instant, not
   delivered as a wake — a `SampleWithInstant(wire, t) -> (value, changedAt)` query is all the design
   needs, and it adds no scheduler primitive.
