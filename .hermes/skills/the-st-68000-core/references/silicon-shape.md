# The silicon shape of the core (`the-st`)

How to restructure or extend the 68000 core so that its *names* and *structure* are the hardware's, without
losing a corpus record. Load this with any ticket about the core's shape, its public surface, its internal
state, its decode, or "make the code match the schematics".

Sources it rests on: `research/m68000-silicon-implementation.md` (what the die shows),
`research/68000-silicon/schematics/` (five sheets: floorplan, datapath, control, timing, word formats),
`research/68000-silicon/nomenclature.md` (§3's "recommended" column is the naming table),
`atari-st-hardware/cpu/02-mc68000-tutorial.md` (the programmer's model and the pins, with § numbers).

## 1. Two vocabularies, two authorities, and never a third

| layer | authority | names |
|---|---|---|
| architectural — registers, flags, pins | the user's manual (tutorial §0–§8) | `D0–D7`, `A0–A7`, `PC`, `SR`, `CCR`, `USP`, `SSP`, `AS`, `UDS`, `LDS`, `R/W`, `DTACK`, `BERR`, `HALT`, `RESET`, `FC0–FC2`, `IPL0–IPL2`, `BR`/`BG`/`BGACK`, `VPA`/`VMA`/`E` |
| microarchitectural — units, latches, buses | the two patents (US 4,325,121 App. B; US 4,296,469) | `AOB`, `AT`, `DT`, `AU` (`AUH`/`AUL`), `ALU`, `ALUB`, `ALUE`, `FTU`, `DCR`, `PREN`, `DBIN`, `DOB`, `IRC`, `IR`, `IRD`, `PSW`, `SSW`, `INL`, `TPEND` |

**Die beats patent for content; patent beats die for names** (silicon doc §13). The die analysis supplies the
tables and the geometry; the patents supply the vocabulary — a die-first name ("register A", "memory address
register") is a finding to map, not a name to adopt. Anything from an emulator is T3, even when it descends
from the die, so agreement with the corpus is consistency and not measurement.

**The public surface is the manual's vocabulary exactly and only**: one member per register, per status bit
and per pin. The two halves are *not* the same kind of thing, and conflating them sends a session down the
wrong order:

- **registers and status bits are *views*** on the machine below (`D7` writes the register file, `SR`
  reassembles the status latch, `A7` is the live stack pointer so its getter returns `SSP` in supervisor mode
  and `USP` in user mode);
- **the pins are the interface, not a view** (the owner's correction — quote it as the rule): a pin is the
  chip's face to the world, the level the engine's wire carries, so `Pins` reads the wire records — the bus's
  assertion group, its release record, the answer and interrupt wires — and is *not* a mirror of a core latch.
  The latches that hold the same facts (`AOB`, `DOB`, `DBIN`) are the layer **below** it and serve it; they do
  not duplicate it, and `Pins` does not wait for them. Reading "every member is a view" as covering the pads
  defers the whole pin type behind the register-file work for no reason — that mistake has been made once.

The corollary a reader needs: a pin level has no meaning at *rest* for a handshake (`DTACK`, `BERR`), so those
read false between cycles rather than "the last cycle's value" — the S0–S7 window that makes them levels is the
bus state machine's, not the pad's. A pin answered with a stale value is the same bug as the `--clocks`
frame-columns staleness. An invented public name — `PeekD`, `Supervisor`, `InterruptMask`, a counter the manual has no word for —
is either retired or moved behind a debug/instrument boundary the design decision names.

**That list cannot be flat, and the reason is a collision rather than a taste**: `A1`–`A7` and `D0`–`D7` name
*both* a register and a pin of tutorial §8.1, so one class cannot hold the manual's list twice. Put the pins in
a nested type (`cpu.Pins.A1`) and say why in the decision — a reader who does not see the collision will
"simplify" it back into a surface that does not compile. **The accessor cannot carry the nested type's name**:
C# rejects a member and a nested type with one identifier (CS0102), so `public Instruments Instruments` and
`public Pins Pins` do not compile — reach the type through a word the design already uses for it (`cpu.Emulator.BusCycles`,
named by the emulator file and by the ADR's §6; `cpu.Pads.A1`, the die's own word for the edge block — *pads and
buffers*), and allow-list each accessor word in the freeze test, or the check reports your own accessor as an
invented member. Decide the accessor in the commit that creates the type and add it to the allow-list there. A nested type's name is a **place,
not a name**: it
may be a word the manual does not have (`Pins`, `Instruments`, `SingleStep`) only because it says *where* a
thing lives; every member inside still takes its own authority (`Pins.A1` from §8.1), and no nested type may
smuggle an alias in. A pin is a **level**, so its members are `bool`; the manual's `R/W` becomes `RW` (drop the
punctuation C# forbids, never rename the signal).

**Freeze the surface by reflection, and freeze it as *declared* members** (`BindingFlags.Public | Instance |
DeclaredOnly`, so inherited members do not count): the programmer's model, the nested types, and the engine's
own start verb (`Launch` — `Machine.cs` starts every chip that way, so it is ADR 0006/0008's surface, not the
part's), then the same check one level down for each nested type. A list that is only documented is a list that
drifts. The tree has no test project: a check of this kind is a `validation-harness` subcommand run as a
`scripts/validate.sh` row, because that script is where this repo's verdicts live.

**The surface is not only registers and pins — it is also what the manual *names as a state*.** Two things
belong to it, and both are mechanical:

- **The first level of the decode, in the manual's family names.** One `public static` function per
  instruction family, taken from the tutorial's §5 tables and its "whole set at a glance" appendix
  (`Move`, `Movea`, `Moveq`, `Movem`, `Movep`, `Lea`, … `Stop`, `Reset`, `Illegal`), plus an enum carrying
  the same members; the freeze test asserts the two sets are **equal**, so a family in one and not the other
  fails. Each function answers "does this opcode belong to me" with `false` rather than an exception — the
  inhibit order between families is the decode's business, not the surface's — and any aggregate
  (`FamilyOf`) is composed from them, never a second table.
- **Every state concept the manual names, as a displayable string.** Scan §0.5, §1.4, §5, §6, §7, §8.2–8.4 and
  §11 — the places the manual describes execution as named states or cases — and give each concept one
  member: the bus cycle's `S0`–`S7`; the exception entry's five numbered steps (§6: "the vector number n is
  determined" … "PC ← long at 4 × n"); §7's interrupt cases (masked, taken, edge-triggered, autovector,
  spurious); and the instruction itself, its text in the manual's syntax plus its family. Values are the
  manual's own words, not paraphrases. **Refuse the concepts the manual only describes** — reset (two vector
  reads the bus state already shows), arbitration (protocol out of scope), the prefetch queue (it is the
  pipeline, shown as `PC`), an RMW or long access (the manual names an *access class*, not a state) — and
  write each refusal with its reason, so the surface cannot quietly grow synonyms.
  Two constraints keep these instruments rather than state: **composed on read, never per instruction** (a
  string in the Release loop is a G8 regression with no owner), and **noise-free at a boundary** (`null` or
  the empty value between instructions, never the previous instruction's value — that is the `--clocks`
  frame-columns staleness in a new costume).

## 2. State: what may be a field

Every field is one of **four** things, and nothing else: **an adopted latch** (cited to its patent name and its
section), **a register-file member**, **an instrument** (a debug/counter member the design decision allows, kept
out of the silicon vocabulary's way), or **the engine's plumbing** (`_answerSeq`, `_busFaulted` — objects ADR
0009 already owns, which the bus contract keeps alive and the part has no opinion about). The "adopt / derive /
delete" table every plan asks for is always short by one column: the plumbing has no silicon counterpart and no
derivation, and deleting it deletes the bus contract. Write the fourth column down rather than forcing those
fields into "instrument". Today's bookkeeping has no silicon counterpart and must go:
`_eaCharged`, `_queueShort`, `_trailingFetched`, `_flowTransferred`, `_streamStart`, `_opcodeAddress`,
`_frameFetchPc`, `_freePop` (and its kind: the pipeline's *position* is no longer a field at all — it is
`FetchPosition`/`BoundaryPosition`, derived from `_au` and the queue's fill, #87). `_own` is the exception of *kind* rather than of necessity: it is a
duration, so it is covered by the time rule below — and in practice it needs no field at all, because the charge
is a local in the script that spends it. Never as a latch, never in the register file.

The five overlapping address/counter fields are the heart of it. The silicon has **one adder register `AU`**
(plus its `AUH`/`AUL` halves), **one address temporary `AT`**, **one output buffer `AOB`**, and a three-deep
instruction register `IRC`→`IR`→`IRD`. MAME's `m_pc` **is** `AU`, which is why every measured frame rule in
`FINDINGS.md` (rows on saved-`pc` depth, the free branch pop, the `STOP` counter) is really a statement about
*which primitive last wrote `AU`* — express it that way and the rule becomes derived instead of guessed.

**A register is 32 bits of storage.** The High/Low/Data section split is a layout fact with no behavioural
consequence on this machine; make it named accessors (`Set16H`/`Set16L`, which is the manual's own "only that
byte or word changes" rule, tutorial §1.1) over one field, not two cells. Splitting storage costs G8 and buys
nothing. `PSW` and `SSW` are *different* words with different lives (a group-2 frame carries both) — do not
collapse them. `ISR` is a third word in that family and is **not** a third status register: it is the ALU's flag
latch, the stage `PSW`'s `X N Z V C` is written from — 16 bits, recomputed per operation, never the
architecturally visible word. `ALUO` and `ISR` are the two names the patents give only by role, so they come
from the microcode listing and belong in the one alias table **with that provenance**; inventing a role-name
instead is what the naming rule forbids.

**Time is simulated, so a duration is allowed — and it must say so in its name.** The rule "no state the
silicon does not store" is about *storage*, not about the emulator's clock: a value that exists to make time
behave is legal, provided a reader sees an artifact rather than a latch.

- **The unit is in the name**: `…Clocks` for a count of CPU clocks (one clock = four oscillator ticks),
  `…Ticks` for oscillator ticks. A bare noun may not hold a duration — `_own`, `Charge`, `_eaCharged` are the
  three shapes the rule forbids.
- **It lives in one file, and never in the part's blocks.** A duration may exist only in the emulator's
  interface file (`M68000.Emulator.cs` — the counters, the chip time, anything the harness reports), never in
  the register file or the sequencer. `grep -nE 'Clocks|Ticks' …/M68000.*.cs` hits there or in a script's own
  locals, and nowhere else; a hit in `M68000.Registers.cs` is the mechanical failure.
- **It is cited differently**: to the microcycle rule (`m68000-silicon-implementation.md` §6.1 — a microword
  occupies one microcycle, 2 clocks, a bus access 4) or to the manual's timing table, never to a patent, and
  its doc comment opens *"time, not storage"*.

**The charge itself is a local, and that is the answer to "where do the internal clocks live".** At each
microcode line boundary the script does `clocks += 2`; the counter is handed to the engine in one
`await ClocksAsync(clocks)` at the beginning of the next bus access (and once at the end), then zeroed. No
`_own`, no `…Clocks` field, and nothing to reset at `Run`/`SingleStep.Load`/`StepAsync` — the flag that had to
be cleared is gone. Two things stay load-bearing: the **flush point** (a prefetch rides inside an idle run, so
flushing one state late changes which access the clocks precede, and only the reads' order in `--trace` sees
it), and the **bridge** (a shape table's per-family charge passes into the instruction's frame as a parameter,
never a field). `_eaCharged` — a "has happened" bit, not a duration — is deleted outright: the states'
position replaces it.

## 3. The bus, and the pin that does not exist

**There is no `A0` pin** (tutorial §8.1): the address is `A1–A23`, and an odd byte access is `A1`=1 with `LDS`
asserted. A pin view that carries an address bit 0 is inventing hardware. A bus cycle is the four-clock,
eight-state S0–S7 sequence of tutorial §8.2 (address valid S1, `AS`/strobes on S2's rising edge, `DTACK`
sampled in S3–S5, transfer S6, negate S7, wait states whole clocks inside S3–S5), with the read-modify-write
variant holding `AS` across two handshakes. The **access class** (`i`/`f`/`n`/`t` × read/write × byte/word)
chosen by the script is what drives the bus — not a cycle count (silicon doc §8).

## 4. The decode, three layers not two

The control path is **three** layers, and stopping at two is the classic failure: the microcode ROM sequences,
the nanocode ROM names datapath atoms, and a **PLA bank keyed on `IRD`** supplies roughly 130 of the ~200
control signals — which register file `RX`/`RY` names (data, address, `AT`, `USP`, or a `MOVEM` list entry),
which size, which vector, which exception. A behaviour missing from a microcode listing is decided there, so
"the script does not say" ends the search rather than opening a hunt for a missing state.

- **A1** has **149 terms — 129 keyed on opcode bits, 20 on the effective-address field alone** (ten long, ten
  byte/word), with inhibit lines between terms. Decode in that shape: one named entry point per term group,
  and preserve the inhibit order (that is what keeps `0x4180` from being read as `NEGX.l` instead of `CHK`).
- **A2/A3** (after an immediate, after an EA) share one AND plane, which is also where `IOC` (invalid opcode)
  and `PRIV` come from — two named signals, not scattered `if`s.
- **A0** replaces A1 at an instruction boundary when an exception is pending; **A0S** overrides the next
  address immediately for reset, bus error and address error. Two named entry points, not "call
  `EnterExceptionAsync` from anywhere".
- A conditional branch is **four-way**: the base address's bits 7:6 are replaced by the branch unit's C1/C0,
  selected by the 5-bit condition field — one named arm per condition (`auz`, `ze`, `nv`, `d4`, `enl`, `cc`,
  the `nz` pair, `ms0`/`m01`, `i11`, the flags), never a numeric table.

**The two control stores are functions, not tables** — the positive form of the no-interpreter rule, and the
shape every generated script must have. The first-level decode is a switch over *named functions* tied to the
manual's families; the body of such a function is the µROM's words **unrolled**, one section per microcode
line, each section prefixed by a comment naming that line (its ROM address and state) and carrying the running
clock total; each group of statements inside a section is one nanoword decoded, prefixed by the nanoword's own
line; and the store classes hold **only the elementary methods the scripts call** — the µROM's fields
(next-address sources, the `load IR`/`load IRD` strobes, the access class) as `MicroStore`, the NROM's 68 bits
(the transfers, the `AU` constant select, the mid-state field, the ALU mode, the flag masks) as `NanoStore`.
A `ushort[]` of control words, a `switch` over 544 arm numbers, or any dispatch the ROM's *content* drives is
the interpreter returning, and the comment-per-line discipline is what makes the refusal cheap to review and
what lets a reader put the C# beside `scripts/microcode-walk.py`'s output for the same opcode.

## 5. The datapath, and what counts

Three sections with an address lane and a data lane each, joined by bidirectional couplers: transfers are
named primitives over those lanes (`regh↔dbh`, `abh↔ath`, `au = alu`, `set_16h`/`set_16l`, `d`/`a`/`pc`
destinations), never direct array writes in a script. The ALU is a **15 × 5 matrix**: row from the instruction
word, column from three nanocode bits, each cell = operation + its `X N Z V C` assignment, column 1 doubled
by the initiate/finish bit (the 32-bit two-pass flag rule), column 4+1 as a divide's quotient bit, and
`INHCC` suppressing flags for address-register destinations.

**The loops are not C# loops.** `MUL`/`DIV` and `RESET` count in `AU` (six low bits, `auz`), `MOVEM` walks the
`PREN` priority encoder, the bit ops use the `DCR` 4→16 decoder (one-hot line, `d4` exposing bit 4), and the
shifts shift one place per microcycle through the `ALU`:`ALUE` pair. Any `for`/`while` counting an emulated
loop is a model that cannot then get the clocks right.

**How a loop counter actually lands** (the shape to copy for every remaining family): the count is a
*transfer*, never a local, and the loop is three named atoms plus one restore —

- `ConstToFtu(15)` then `AuEqualsFtu()` — the trip count comes out of the field register, which is where the
  constants ROM puts it (`const_to_ftu`). It is 15 for all five variable-time families, and a loop of sixteen
  iterations starting at 15 is *why* `auz` is a six-bit test.
- `AuZeroOnSixBits` — the branch variable `auz` as one expression, so the loop's exit reads as the die's
  condition instead of `(counter & 0x3F) == 0`.
- `AuMinusOne()` — a **unit** step. `AuPlus` counts in words (2 × n) and halves the count.
- `AuEqualsConsumed()` at the end — `_au = _pc - 2`: the adder was the counter and the PC register holds where
  the stream had fetched up to. Sound only because the loop issues no fetch, so nothing wrote `_pc` while the
  adder was busy; the files staying exact-clock is the proof that the count did not change.
- The divide's variant loads `ftu + 1` (sixteen quotient bits) and takes its zero test **after** the
  subtract, so the exit lands on the last bit rather than the first.

All five are converted (the multiplies, then `DIVU`, then `DIVS` with its two leaving paths, then `RESET`'s
countdown — `4 + 4 × 31` clocks derived from the loop instead of the table's flat 128); `CHK` has no loop, so
`B1` had nothing to convert there. Each was verified on the clock column alone, because the states and idles
must not move, and `RESET`'s two placements (the read before the hold, one wait instead of 62 states) are the
owner's outstanding ruling on `#92`.

**The field register's own cost is a G8 number, not a taste question.** Its decode write was first packed with
three shifts and measured **+0.23 ms** on the gate's median pair; the same fields as one shift (`_ftu =
(ushort)(opcode << 8)` — size/mode/register *are* the encoding's low byte, in order) measured **+0.08 ms**.
Derive a packing mask from the field ranges (size is bits 7–6, so the mask is `0xFF`; `0x7F` drops the size bit
and answers with 60 000 corpus failures), and give every new per-instruction store its narrowest form before
running the gate. Its reads belong in instance scripts only: the `IsScripted*` ledgers are `static` and answer
for *any* encoding, so the compiler refuses the substitution there (CS0120) — the refusal is the boundary, not
an obstacle.

## 6. Working one of these tickets

**A surface conversion is sliced, and the freeze test is slice one.** The order that works, because each slice
is complete and quotable on its own: (1) the reflection test, in **two modes** — the default is the freeze
(non-zero on any divergence) and `--audit` prints the same report and exits 0, with the mode held as a constant
in whatever script drives it, flipped to freeze **in the commit that converts the surface**, so a later rename
can never pass as "mid-conversion"; (2) the programmer's model, which is purely additive over storage that
already exists; (3) the emulator-interface file and the nested `Instruments`; (4) `Pins` as levels over the bus
record; (5) the decode's families and the display strings. Slice 1 touches no CPU code, so the corpus and the
folds cannot move, and it converts the design document from prose into a numbered work list — a measured
"62 divergences" is a plan, an ADR section is a reminder.

Two mechanical facts about the reflection check, both learned the hard way: **check both directions** (a member
the design names and the type lacks is *missing*; a public member the type declares and the design does not name
is *extra* — the second is how a surface actually fails, and it is the one a one-directional check never sees),
and **filter the member walk**: `BindingFlags.DeclaredOnly` on the class returns its nested types and every
property's accessor methods, so skip `member is Type` and `method.IsSpecialName`, and skip constructors, or the
report fills with `get_D0` and the seam's own record properties.

0. **When the ticket is the decision, not the port** (an ADR / grilling ticket), the deliverable is two
documents and a set of rewritten tickets, and the sequence is:
   - **Survey the tree first, from source, not from the handoff.** `grep -n '^    public '
     src/TheST.Machine/Cpu/M68000*.cs` for the real surface (it is the list the freeze test will enforce) and a
     field-declaration grep for the state; a handoff's counts and names are a previous session's prose and may
     be one branch behind — `git log --oneline` before quoting anything, and expect another session's commits
     on the same branch.
   - **Copy the house shape from the last accepted ADR**: a one-paragraph summary, a `**Status**` line carrying
     the owner's own words and the ticket link, then Context → Decision (numbered) → Invariants →
     Consequences → Considered options → Falsifier. The Falsifier carries the binding numbers, because the next
     session reads the ADR and not the handoff.
   - **Draft the document, then ask as a numbered list in the reply** — one entry per open question, each with
     your recommended answer and what changes if it is refused. The owner answers by number and corrects a
     wrong grouping faster than a vague open question; a blocking form card may simply go unanswered. When he
     asks to "explain the decision with more context", he means the whole chain — what the field is today, what
     the source says, what each option costs, what breaks — not a longer restatement of the recommendation. A
     bare list of options ("do A, or B, or C?") is not something he can rule on: lead with what the part does
     and what the code does today, price each route in corpus records or G8 milliseconds, then recommend, and
     record his answer as the next slice's order.
   - **A multi-session ticket is presented as a numbered slice plan with one decision, not as prose.** Say how
     big it is in measured terms (today's member count against the target's), give each slice and what makes it
     quotable on its own, and put the single question that changes what "green" means at the end with your
     recommendation — then start on the answer instead of re-asking.
   - **When the owner retires a witness, execute and record the cost — do not re-argue.** State in one or two
     sentences what is lost by dropping it (a retired corpus column costs vector density: its 8 065 operands an
     instruction form against the gate's 2 500), do what was asked, and write both the reason and that cost
     into the design document so the next session reads a decision rather than a gap.
   - **Record his answers verbatim in an answers table inside the ADR**, flip the `Status` line to accepted
     with his words, append one line to the campaign map's decisions block, rewrite the downstream ticket
     bodies the decision touches (step 8), and close the ticket.
   - **No corpus run for a design ticket** (no code changed) — but say in the ADR which numbers bind it.
1. **Golden the decode first, and gather its conditions from the code rather than the manual.** Before
touching dispatch, dump today's answer for **all 65 536 opcodes** (entry point, the handler it reaches,
whether it traps) to a table in `tests/680x0/`, and require the new decode to reproduce it exactly — on the
new tree *and* on the parent. Cheap, total, and it catches the family that a rewrite silently re-homes.
   **Know what the job is before starting it**: the manual's families are already *decided* in the core —
   scattered as `IsScripted*` ledgers and inline `(op & mask) == value` tests inside the line handlers (38
   sites, a dozen named predicates across `Instructions.cs`, `Scripts.cs` and `Timing.cs`) — while the 78
   families themselves live inside 13 entry points (`ExecuteAsync`'s line switch plus `MoveAsync`,
   `MoveQAsync`, `MiscAsync`, each `LineXAsync`). So a family decoder is a **gathering and naming** job, not
   a re-derivation, and a decoder written from the manual's tables guesses exactly the boundaries the code
   already settles — it measures around 46 %: `0x48xx` is one block holding `SWAP` / `EXT.w|l` / `PEA` /
   `MOVEM` split by the mode field, `ABCD`/`SBCD` sit inside the bit-operation space, `MOVEP` is mode 001
   with bit 8, `CHK`/`LEA` are `0xFFC0` terms, and `Illegal` has four separate sites. Transcribe the
   conditions the corpus already validates, then order the term table into the PLA's inhibit order.
   **Witness the families against the corpus's own names** — cheaper than the total golden and enough to
   drive the work: take every record **whose name carries its file's mnemonic** (`ADD.b …` in `ADD.b`; an
   `ADDQ` record inside that file is excluded by that filter, not by a rule you would be assuming), decode
   its opcode word (`initial.prefetch[0]`; names read `NNN MNEMONIC.size operands opcode`), and require that
   family. Report `checked/bad` per family plus the first mismatches: a mask that claims a neighbouring line
   shows up as a per-family line rather than as a corpus failure. Legality is *not* in this witness — that is
   what the `.lst`'s total golden is for.
   **When the owner asks how the implementation compares to the target, answer in his shape**: one row per
   target family with where it lives today, then the three classes of mismatch (a line handler that answers
   for several families; one family split across several homes; a method named for the operation rather than
   the family), then the count of ad-hoc predicates that *are* the missing table — and the consequence, which
   is what he is after: which work the target really implies.
2. **One field at a time, corpus after each.** `_irAddress` and `_pc` are already `_at` and `_au` (landed, with
   `Set16H`/`Set16L` used at every half-write); what is left of the address/counter tangle is `_fetchStateAddress`,
   `_streamStart`, `_opcodeAddress` and `_own` — rename one per commit and run both corpora after each. A sub-step that moves a class of records is a rule
   that was measured rather than derived: read the class with `--clocks`' frame columns and fix the *primitive*
   (which latch the state should have written), never the expression that used to compensate for it.
3. **The bar for every landing** is one command: `bash scripts/cpu-shape-check.sh <parent-commit>` — MAME
   `317 500 / 317 500` state **and** exact-clock; the boot pair unmoved (`f8d39bf1…` / `9e362791…` since the
`#92` RESET ruling — a placement the die has differently is the one thing the owner blesses, and FINDINGS
row 82 carries the old pair as history) with the
   desktop frame and pops quoted; `G8` as the **median** of three interleaved pairs at equal `frame_n` within
   `parent + 0.3 ms` (a single pair is not a measurement at that scale); and the interface row, which reports
   the surface's divergence count while the conversion is in progress and the freeze once it lands. A moved
   fold pair is a bug report, not a re-bless request: a re-expression must not change behaviour. **Write that
   bar into the decision document itself**, not only into the handoff: the next session reads the ADR, and
   "held" has to be checkable from the artifact alone. (CLK is not a bar: it was retired as a grading column
   because MAME is the truth for state and clocks and a column that can never be green cannot grade a landing.)
4. **Regenerate the derived trees.** `tools/cpu-dissection` *links* `src/TheST.Machine/Cpu/*.cs` **by glob** —
   the core is six partial files of one class, so an explicit file list in that csproj prices a different core
   and eventually stops compiling — and `gen.py` transforms **every** file into `Gen/Sync` and `Gen/Folded`
   (D is the sources unchanged: the folded shape is the shape of record, so there is no separate bus block to
   substitute). Run `python3 tools/cpu-dissection/gen.py` after any core restructure, then build the tool — a
   restructure that breaks it has broken the sources of record, and the campaign script's interface row is what
   catches that silently.
5. **Read the schematic sheet before writing the file**, and give the design document a *level* order rather
   than arrows: each file may use only files below it, and a cycle means a block the hardware does not have.
   **Group the files by die area and chip function, not by data type** — the owner's rule, and the floorplan
   sheet's own three bands are the order: top (instruction registers, decode, sequencer, exception, bus
   control), middle (the two control stores), bottom (the register file, ADDRESS, DATA), the pads at the edge
   (`Pins`, `Clock`), then the chip, the seam and `M68000.Emulator.cs`. A "counters" or "datapath" file is a
   grouping by function the die does not draw: `AU` is in the address section, `DCR`/`PREN`/`FTU` in the data
   one. Name each partial as *occupied, then empty* when an existing file's reason to exist disappears — a
   later reader otherwise thinks the map is incomplete. Two edges look like cycles and are
   not, and both come from the hardware: the sequencer **computes an address** (`A1`/`A2`/`A3`/`A0`/`A0S`) and
   never calls a script, and the bus never calls upward — the fault it reports is two latches at level 0.
6. **Before retiring or renaming a member, grep every consumer**, including the reflected ones: `grep -rn`
   over `src/` and `tools/` for the name, and read `tools/cpu-dissection/Program.cs`'s reflection block, which
   reaches six names (`Instructions`, `BusCycles`, `PC`, `SR`, `PeekD`, `PeekA`) from four generated variants.
   A rename there compiles fine and dies at run time, and the corpus never sees it.
7. **Split the instrument boundary by consumer, not by taste.** Per-record instruments (the three observers,
   the `LastFrame*` columns) go behind `#if DEBUG`. The counters cannot: the **Release** `boot` prints them in
   its own report and `cpu-dissection` reads `Instructions`/`BusCycles` by reflection, so a blanket `#if DEBUG`
   breaks the Release build *and* the dissection tool, and neither break is visible to the corpus. Group the
   non-manual members in one nested `Instruments` type, and put it — with the chip time the simulation advances
   — in its own **emulator interface file** (`M68000.Emulator.cs`), so the part's files hold no reporter and
   the freeze rule stays one line.
8. **Freezing the interface rewrites the tickets that follow it — in the same session.** An ADR that settles
   a name, a placement or a contract makes some downstream ticket bodies *impossible* (a flat pin list on a
   class whose register names already take `A1`–`A7` and `D0`–`D7` cannot compile), and a body that merely
   predates the decision will be implemented as written by whoever picks it up. Re-read every remaining
   ticket in the campaign and edit the ones the decision touches, then record what changed on the design
   ticket. Where the body already states the right intent, keep it and add only the missing constraint.

## 7. Where the code stands against the die, measured

`design/cpu-silicon-todo.md` walks §5–§13 of the silicon reading claim by claim against
`src/TheST.Machine/Cpu/`, chapter by chapter, with an aligned/difference verdict per claim and the todo ranked
by what it buys. Read it before proposing "make X match the schematics" work, and update the row in the same
commit that closes it — the alternative is re-running a fourteen-chapter audit.

Its shape, which is what a planning turn actually needs: **aligned** — all 21 32-bit registers under the patent's
names (`D0–D7`, `A0–A6`, `USP`, `SSP`, `AU`, `AT`, `DT`, `AOB`), 5 of the 10 16-bit latches (`ALUO`, `ALUB`,
`ALUE`, `DOB`, `DBIN`), the `IRC`→`IR`→`IRD` pipeline, two clocks a microstate and four a bus access, `AU`
writing `PC` without a bus, and the atom vocabulary in `M68000.ControlStores.cs`. **Not aligned, in the order that matters** — and each row carries its state, because this list is the backlog:
`SSW` latched (landed: `_ssw` written by `ComposeSsw` at the frame — writing it per bus cycle, the die's own
site, costs 50 752 records because a non-bus-fault exception keeps the last operand access's data class); `DCR` as the derived decoder `Decoder4To16` (landed: a decoder has no state, so it
is a function and not a field); `PREN` and `FTU` are latches (the `MOVEM` walk and the field register landed: written by the decode, read by the
scripts), `DCR` is a function; **the five variable-time families count in `AU`** (`MULU`/`MULS`/`DIVU`/`DIVS`/
`RESET` — `ConstToFtu` → `AuEqualsFtu` → `AuZeroOnSixBits`, restored by `AuEqualsConsumed` or `AuEqualsAt`;
`CHK` has no loop at all), and **the pipeline's position is derived, not a field** (#87: `FetchPosition` /
`BoundaryPosition`, `_fetchPointer` deleted, the fetch states are `CopyAuToPc(FetchPosition)`); what the split
still owes is the adder holding an access, `ConsumedAddress` and `AUH`/`AUL` (absent as a ROM; the constant has
its transfer, `ConstToFtu`/`AuEqualsFtu`); no bus state machine and
no wait states; no named access class; `INL` and `TPEND` absent; `Shift()` closed-form rather than one place per
microcycle through `ALU`:`ALUE`.

**The rulings that shape the rest of it** (owner's, on the chapter audit — do not re-litigate them, and check the
`design/cpu-bottom-up.md` §8 record before proposing a row as new work): adopt is **in** for `SSW`, `INL`,
`TPEND`, `PREN` and `FTU`; `DCR` is a function, not a field. The `AU`-as-loop-counter and the constants ROM are
**in**, but they need the **AU/PC split** first — `PC` must become a register-file entry and `AU` the adder, which
is also what makes `AUH`/`AUL` real, so the two address adders come with the split and *not* with the
section/lane structure (lanes are a bandwidth feature; no instruction here needs two transfers in one cycle).
The `S0`–`S7` work is **accepted as a view** over the cycle's own position, because wait states are already
correct — `Cycle` waits for whatever instant the device answers at, so the core is not ST-specific; only the
state *names* are missing. The microcode word formats (`NMA` = the next micro-ROM address, `TY` = the source of
the next address, `CBC` = a conditional word's 5-bit condition) stay out: our scripts *are* that control flow.
When a plan turns one of these rows into work, say which of the two kinds it is — a latch the die has, or a
structure the die has that the core deliberately does not — because the second kind is how a "match the
silicon" ticket turns into a rewrite with no record to show for it.

Two traps in the exercise itself. Some rows are differences **by design** — no microcode table, `PSW` as one
word, no `E` divider — and must be marked as rulings, or the list reads as a backlog. And a claim can be aligned
*in effect* while differing in mechanism (the 32-bit flags by one computation rather than two ALU passes): say
which, because the second decides whether a landing could move a record at all.

## 8. Pitfalls

- **Do not build a microcode interpreter.** The ROMs are the specification, not the runtime: a state is an
  `async` C# method named after it, the coroutine style is the engine's (ADR 0009), and the machine's time is
  the engine's clock. "Unrolled as switches and scripts" means named code, not a 544-entry `switch`.
- **`m_aluo`/`m_alue` are 16-bit and that is semantics** — the multiply's shift register is a `u16` pair and
  the divide's pair shifts with `a << 17`; widening them while "naming them after the hardware" produces
  garbage on every record.
- **A register write that happens exactly once must stay exactly once** (`prst`, `popm6`, `pinl3`'s commit):
  naming the primitive makes the commit point visible, and moving it into a per-item loop is how 249 records
  went 2 bytes out.
- **The corpus outranks the derivation.** Where a rule read off the silicon disagrees with a measured rule,
  the measured rule wins for the landing and the difference is a `FINDINGS.md` row.
- **When a view replaces an old member, delete the old member in the same edit.** Adding `public bool S` beside
  an existing `Supervisor` property is a compile error, and the same holds for every bit that already had a
  named accessor — the conversion is a replacement, never an addition.
- **Commit each verified slice before starting the next one.** This tree can be open in a second session that
  commits with `git commit -a`, which sweeps your working tree into its commit; the history is pushed, so the
  attribution stays wrong for good. A slice that is committed is also a slice whose gate numbers are
  attributable to a commit, which is what the campaign's table is supposed to say.
- **Mass renames corrupt each other unless the patterns are whole identifiers.** Moving members behind an
  accessor by substitution matched `Observer?.Invoke(` inside `ClockObserver?.Invoke(` and produced
  `ClockEmulator`, twice, in one edit; anchor on the full member name or do the longest patterns first, then
  build before trusting the diff. And the kind follows the member: a `const` stays on the type
  (`Instruments.TicksPerCpuClock`), only an instance member goes through the accessor — the compiler will say
  `CS0176` and it is a one-line fix, but the substitution loop will keep producing it.
- **A script that redirects into its own output directory must create it first.** `mkdir -p` the run directory
  before any `> "$out/g8/p1.log"`: bash fails the redirect, the command never runs at all, and the symptom is a
  *missing log beside a missing artifact* — which reads as a harness failure and sends the next hour into the
  wrong process.
- **A frame rule taken from one corpus alone can hang the boot** — key it on the vector, and run
  `boot --frames 800` before landing it (EmuTOS runs on `TRAP`).
