# The silicon reading against the core: what is aligned, what is not

A claim-by-claim comparison of `research/m68000-silicon-implementation.md` §5–§13 with
`src/TheST.Machine/Cpu/`, done 2026-11-27 on the tree after the bottom-up refactor
(`design/cpu-bottom-up.md`, commits `3e30a2d`…`0996b1e`). **Aligned** means the C# has a named
equivalent; **difference** means it does not, and the row says what the code does instead.

Read it the other way round too: an "aligned" row is a claim the code *is* the witness for, and a
"difference" row is a place where a reader of the research file would expect to find something they
will not.

## Summary

| chapter | claims checked | aligned | differences |
|---|---|---|---|
| §5.1 three sections, two buses, six lanes | 4 | 1 | 3 |
| §5.2 the register file (21 × 32, 10 × 16) | 24 | 17 | 7 |
| §5.3 pipeline, PSW-as-several-latches, SSW, INL/TPEND | 6 | 3 | 3 |
| §5.4 the standard cells | 2 | — | n/a (no code implication) |
| §6.1 clocks, microcycle, wait states, E, bus state machine, no incrementer | 7 | 4 | 3 |
| §6.2 the counters are not counters (5 loops) | 5 | 1 | 4 |
| §7 A1/A2/A3, 149 terms, A0/A0S, 10-bit address | 5 | 2 | 3 |
| §8 microcode word: formats, CBC, access label | 5 | 1 | 4 |
| §9 nanocode word, its fields, the IRD PLA bank | 5 | 2 | 3 |
| §10.1 ALU organisation, constants ROM, AU→PC | 5 | 3 | 2 |
| §10.2 the 15 × 5 control matrix | 5 | 1 | 4 |
| §10.3 bit ops via DCR, BCD correction factor | 3 | 1 | 2 |
| §11 conflicts | 8 | n/a | 0 (adjudications, not code) |
| §12 not published | 6 | n/a | 0 (scope notes) |
| §13 three rules for `the-st` | 3 | 3 | 0 |

**The four that matter most**, in the order I would take them:

1. **`SSW` is not a latch** (§5.3, §9) — the frame's status word is composed at the entry from two
   flags (`_faultProgram`, `_faultIsFetch`), where the die latches a word that a faulting instruction
   resumes from. This is the one with a corpus consequence: it is the mechanism behind the frame
   classes.
2. **`DCR`, `PREN`, `FTU` are not latches** (§5.2, §6.2, §10.3) — the bit number's one-hot line, the
   MOVEM mask walk and the field translation are inline arithmetic (`1u << bit`, a `for` loop, local
   `size`/`mode`/`reg`). The *behaviour* is right (both columns green); the *shape* is not the die's.
3. **`AUH`/`AUL` (the adders' registered outputs) do not exist** (§5.2, §10.1) — address arithmetic is
   C# `uint` arithmetic on `_au` and `_aob`; there is no 32-bit adder in two 16-bit halves and no
   constants ROM (0, ±1, ±2, +4, +8, size).
4. **No bus state machine and no wait states** (§6.1) — `Cycle` asserts, waits for the answer's
   instant and releases; `Pins.AS` is derived from the release record. The eight S0–S7 states and
   DTACK-driven stretching are the memory's, not the core's.

## §5.1 Three sections, two buses, six lanes

| claim | code | verdict |
|---|---|---|
| the EU is segmented into High / Low / Data sections | `_d[8]`, `_a[7]`, `_usp`, `_ssp`, `_au` are one 32-bit cell each (`M68000.Registers.cs`); there is no section object | **difference** — the split is a comment, not a structure |
| each section carries an address and a data lane | one bus path only: `Cycle(address, …)` at `M68000.cs` | **difference** — no lanes, no couplers |
| the six 16-bit transfers in one cycle | not modelled (one access per cycle) | **difference** — no parallel transfers anywhere |
| register halves are `Set16H`/`Set16L` over one cell | present, and the only legal half-write | **aligned** (ADR 0015 §3's ruling) |

## §5.2 The register file, counted

21 × 32-bit: `D0–D7` ✓, `A0–A6` ✓ (`_a`, with `A7` over `USP`/`SSP`), `USP` ✓, `SSP` ✓, `PC` = `_au` ✓,
`AT` = `_at` ✓, `DT` = `_dt` ✓, `AOB` = `_aob` ✓ — **17 of 21 aligned**, the other four keep the
patent's names the reading gives them.

10 × 16-bit:

| silicon latch | code | verdict |
|---|---|---|
| latched `AUH` output | — | **difference**: no registered adder output |
| latched `AUL` output | — | **difference** (same) |
| latched ALU output | `_aluo` | aligned |
| `ALUB` | `_alub` | aligned |
| `ALUE` | `_alue` | aligned |
| `DCR` (4→16 bit decoder) | `1u << bit` inline (`BitOperationAsync`) | **difference** |
| `DOB` | `_dob` | aligned |
| `DBIN` | `_dbin` | aligned |
| `FTU` (field translation) | none — `size`/`mode`/`reg` are locals; the MUL/DIV trip count is a local loaded with 15 | **difference** |
| `PREN` (MOVEM mask walk) | a `for (bit …)` loop over the mask | **difference** |

The two naming schemes (§5.2's table, patent vs Gigasoft) are not both present: the code uses the
patent's names only, as ADR 0015 §8 requires.

## §5.3 Beyond the register file

| claim | code | verdict |
|---|---|---|
| `IRC` → `IR` → `IRD` pipeline | `_irc`, `_ir`, `_ird`, `_ircValid`, `_at` | **aligned** |
| `PSW` is not one register — bits latched in several places | `_sr` is one `ushort` (+ `_isr` as the ALU's flag stage) | **difference** (deliberate: the corpus compares one word) |
| `INL` (3-bit pending interrupt level) | none — the level is read from the `IPL` wire at the boundary (`CheckInterruptAsync`) | **difference** |
| `TPEND` (trace pending) | none — the `T` bit is in `_sr`; no pending latch | **difference** |
| `SSW` (the microinstruction's status word, framed) | `_faultProgram` + `_faultIsFetch`, composed by `BusStatusWord()` at the entry | **difference** — see §1 of the summary |

## §5.4 The standard cells

No code implication. The dual-port cell and the latch idiom explain *why* the register file is cheap to
replicate; nothing in C# corresponds to a cell.

## §6.1 Clocks, phases, bus cycle

| claim | code | verdict |
|---|---|---|
| one clock pin, both edges; a bus cycle is four clock periods | `Instruments.TicksPerCpuClock = 4` (one CPU clock = four 32 MHz ticks) | **aligned** |
| a microinstruction is two clocks; a bus access four | `IdleAsync` charges 2; an access's 4 are the memory's answering instant | **aligned** |
| wait states stretch the current microword | none — the answer's instant is fixed by the memory; `Cycle` waits for it | **difference** |
| the E clock's 10-state divider | none, by the owner's ruling (`Pins.E` reads false, undriven) | **difference, by ruling** |
| the bus state machine (S0–S7) | none — `Pins.AS` derives from the release record, `Instruments.BusState` prints only `S0`/`S2` | **difference** |
| no incrementer in the microcode address path | n/a — no address path; scripts are calls | n/a |
| five states of a read-modify-write keeping AS asserted | not modelled (a RMW is two accesses; AS re-asserts) | **difference** |

## §6.2 The counters are mostly not counters

| loop | silicon | code | verdict |
|---|---|---|---|
| MULU/MULS, DIVU/DIVS | `AU` loaded with 15, `auz` tests its six low bits | a `while` loop with a local counter (15), inside `MultiplyAsync`/`Divide*Async` | **difference** (behaviour identical) |
| `RESET` = 132 clocks | `AU` counts down from −2 by 2, six bits end it | `M68000.Timing.cs` returns 128, plus the pin's 4 | **difference** (the count is a constant, not a loop) |
| MOVEM | `PREN` walks the mask | `for` loop over the 16 bits | **difference** |
| bit ops | `DCR` one-hot, `d4` branch variable | `1u << bit` | **difference** |
| shifts/rotates | one place per microcycle through `ALU:ALUE` | `Shift()` computes the result in closed form | **difference** |

Only the first of these could change a record if implemented the silicon's way — and it would not,
because the loop counts are already the microcode's.

## §7 From instruction word to microcode script

| claim | code | verdict |
|---|---|---|
| three PLAs on the instruction register: `A1`, `A2`, `A3` | `Decode.Line` + `Decode.TryDecode` are `A1` (the entry: "is this yours"); `A2`/`A3` do not exist — a script that needs the after-immediate or after-EA entry calls the other block directly | **difference** |
| 149 `A1` terms, 129 opcode-keyed + 20 EA-keyed | `Decode`'s predicates: 78 families, the EA-keyed part implicit in the scripts' EA machine | **difference** (functional, not tabular) |
| `A0` (exception at a boundary) and `A0S` (reset/bus error/address error) | none — `EnterExceptionAsync` is called from the boundary and from the fault sites | **difference** |
| a 10-bit address addressing both ROMs | n/a | n/a |
| the physical row order is scrambled | n/a | n/a |

## §8 How the microcode is interpreted

| claim | code | verdict |
|---|---|---|
| 544 × 17 words, two formats | no table: the scripts are the expansion (ADR 0015 §10 refuses the interpreter) | **aligned by construction** |
| Format I: `FC`, `NMA`, `C`/`I`, `TY` | n/a — no next address; a script's sequence is the C# statement order | **difference** (by design) |
| Format II: 5-bit `CBC`, `NMBA`, 4-way branch | the branch logic is `Condition(...)` in C#; a conditional script is an `if` | **difference** (by design) |
| the four-character access label (class × mode × type) | implicit: `ClocksAsync(n, prefetch:)`, `ReadProgramAsync`, `ReadOperandProgramAsync`, `IdleAsync` | **difference** — no named access class |
| wait states and the access label drive bus timing | see §6.1 | **difference** |

## §9 How the nanocode is interpreted

| claim | code | verdict |
|---|---|---|
| 336 × 68 (66 driven) datapath words | the atoms in `M68000.ControlStores.cs` (NanoStore block) are the 66 bits' vocabulary, one method a field set | **aligned in kind** |
| ~40 fields named: transfers, AU constant select, destinations, mid-state, ALU mode | the transfers and ALU mode are methods; the **AU constant select has no member** (`_au + 2` is C# arithmetic); the "mid-state" 4-bit field has no equivalent (its effects are inline: `_sr` writes, `PrefetchAsync`) | **difference** |
| ~70 of ~200 control signals come from the ROMs; the rest from PLAs keyed on `IRD` | `Decode` is that layer for *family, size, mode, register* only; "which register file `RX`/`RY` mean" is decided inside each script | **difference** |
| the nanocode is not the whole control | the ADR and the code agree (the enumerate-the-three-layers rule) | **aligned** |

## §10.1 The arithmetic

| claim | code | verdict |
|---|---|---|
| two 16-bit adders (AUH, AUL) forming a 32-bit address adder, one 16-bit ALU | one ALU (`_aluo`/`_alub`/`_alue`/`_isr`); addressing is C# `uint` arithmetic | **difference** |
| a constants ROM: 0, ±1, ±2, +4, +8, the instruction size | none | **difference** |
| the AU can write `PC` without a bus | `_au` is written directly everywhere (`_au += 2`, `RefillAsync`) | **aligned** |
| `ALUE` chains to the ALU for shifts | `_alue` exists; `Shift()` uses it as a scratch for the 32-bit pair | **aligned** |
| 32-bit operations are two passes with the carry latch | `AluLongOp` = one 32-bit computation + `SetLongFlags` (measured equivalent, FINDINGS row 79) | **difference** (equivalent by measurement) |

## §10.2 The control matrix

| claim | code | verdict |
|---|---|---|
| 15 rows × 5 columns, a row per instruction shape from a PLA | `AluOwnClocks`/`IsScripted*` in `M68000.Timing.cs` are a *timing* ledger, not a row table; the ALU operation is a `switch` in the script | **difference** |
| the column comes from three nanocode bits (NIF0–NIF2); no column line → no register update | no such select: each script names its operation directly | **difference** |
| column 1's two entries, selected by initiate/finish | the two-pass 32-bit flag update is `SetLongFlags`'s `_u` half | **difference** (mechanism not modelled) |
| `INHCC` suppresses flags (address-register destinations) | "no flag call" per script (e.g. `AluIntoAddressRegisterAsync`) | **aligned in effect** |
| 13 function sets in the die dump | `Shift`'s `kind` switch and `Alu*` methods cover the same operations | **aligned in effect** |

## §10.3 Bit manipulation and BCD

| claim | code | verdict |
|---|---|---|
| bit ops bypass the ALU: DCR one-hot, extract, recompose | `BitOperationAsync` builds `1u << bit` and recomposes the word with xor, and-mask or or-mask — same behaviour, no DCR | **difference** |
| decimal arithmetic uses the ALU with FTU's `corf` correction factor | `BcdAsync` computes the packed-decimal result directly | **difference** |
| ABCD/SBCD's flag rules are not derivable from the microcode | the code's flags came from the corpus (T1 in `CONFORMANCE.md`), as the reading says they must | **aligned in method** |

## §11–§12 Adjudications and gaps

Nothing to align: §11 resolves what the literature disagrees about (336 not 366; 68 allocated, 66
driven; the patent's microcode is a prototype; ~70 of ~200 signals; the VPA mode observes rather than
dumps), and §12 lists what nobody has published. Two of them bound this comparison: **the IRD PLA
bank's truth tables are not public**, so `Decode`'s families are the manual's list and not a
transcription of a dumped PLA; and **no complete nanocode bit map exists**, so the atoms' field names
come from the patent and Galibert's decoder, not from a bit assignment.

## §13 What this means for `the-st` — all three rules hold

1. **Die for content, patent for names** — the code's names are the patent's (`AOB`, `AT`, `DT`,
   `ALUB`, `ALUE`, `DBIN`, `DOB`, `PSW`, `SSW`…), and ADR 0015 §8 makes that the rule; content came
   from the corpus and the microcode listing, never from a secondary account.
2. **Anything from an emulator is T3** — the code says so where it matters (`FINDINGS.md`,
   `CONFORMANCE.md`, the MAME gate's provenance), and the frame conventions are quoted with their
   corpus.
3. **Do not model the nanocode as the control** — the file map has `Decode` above `Scripts` above
   `ControlStores`, which is that rule as a structure.

## The todo, in one place

| # | difference | where | what it would buy | blocker |
|---|---|---|---|---|
| 1 | `SSW` is composed, not latched | `M68000.cs` `BusStatusWord()`, `_faultProgram`/`_faultIsFetch` | one fact instead of two flags; the frame classes' mechanism in one place | none — but it must not move a record, so the corpus decides |
| 2 | `DCR` (bit-number decoder) absent | `BitOperationAsync` | the one-hot line the die has, and the `d4` branch variable with it | none |
| 3 | `PREN` (MOVEM mask walk) absent | the MOVEM loops in `M68000.Scripts.cs` | the walk as a latch (and the "read one word ahead" rule reads better) | none |
| 4 | `FTU` absent | `size`/`mode`/`reg` locals; the MUL/DIV trip count | the field-translation unit as a place, which is what makes "the AU is the counter" expressible | #5 |
| 5 | `AU` is not the loop counter; the constants ROM absent | `MultiplyAsync`, `Divide*Async`, `_au + 2` everywhere | the reading's central claim (§6.2) in the code | #4 |
| 6 | `AUH`/`AUL` absent | address arithmetic | the 32-bit adder in two halves, and the six-lane structure behind it | a design decision, not a rename |
| 7 | bus state machine and wait states absent | `Cycle` | `S0`…`S7` and DTACK stretching; `Instruments.BusState` beyond `S0`/`S2` | `design/cpu-silicon.md` §5 says #88 owns it |
| 8 | no access class (i/f/n/t × mode × type) | script calls | the microcode's own vocabulary for "this state starts an access and of which kind" | #89 |
| 9 | `INL`, `TPEND` absent | `CheckInterruptAsync`, `_sr`'s T bit | the pending level and trace as latches | none |
| 10 | `PSW` is one word | `_sr` | the die's several latches | deliberately not modelled (the corpus compares one word) |
| 11 | section/lane structure absent | `Registers`/`Cycle` | the six-transfer cycle the die can do | out of scope for a functional core |
| 12 | `Shift()` is closed-form | `M68000.Instructions.cs` | one place per microcycle, as the ALU:ALUE pair does | none, and no record would move |
