# What the parts of a 68000 are called

### Three naming strata, the names that actually dominate, and the ones that mislead

*Companion to `m68000-silicon-implementation.md` and to the diagrams in `schematics/`. Studied November 2026.
Labels as elsewhere in `research/`: `documented`, `derived`, `inferred`, `unknown`.*

---

## 1. The short answer

There is **one canonical nomenclature for the architectural machine** and **one semi-canonical nomenclature for the
microarchitecture**, and below that **no agreed names at all**.[15][16][23]

| stratum | who fixes the names | how settled it is |
|---|---|---|
| **Architectural** — registers, flags, buses, pins | Motorola's *M68000 User's Manual* and *Programmer's Reference Manual* | Settled. Universal. `D0–D7`, `A0–A7`, `PC`, `SR`, `USP`, `SSP`, `CCR`, `AS`, `UDS`, `LDS`, `DTACK`, `BERR`, `VPA`, `E`.[23][31] |
| **Microarchitectural** — the units and internal registers | the two Motorola patents: US 4,325,121 (control store) and US 4,296,469 (execution unit), whose Appendix B is a glossary of abbreviations[15][16] | Semi-settled. Every serious modern implementation uses this vocabulary — `aob`, `au`, `at`, `dt`, `ftu`, `dbin`, `dob`, `irc`, `ir`, `ird`, `psw`, `ssw` — because the patent is the only *official* source that names them.[14][21][22] |
| **Signal and net level** — individual gates, PLA terms, bit positions | nobody | Not settled. Netlist nodes are machine-generated (`n2388`); the nanocode's 68 bit positions have no official numbering; PLA product terms are unnamed.[14][6] |

So the honest answer to "do these physical elements have canonical names?" is: **the blocks and registers do, in
Motorola's own patent vocabulary; the wires and bits do not.**[15][16][14]

---

## 2. What each Motorola document actually names

`documented` The **user's manual** stops at the programmer's model. It names the sixteen registers, the status
register and its condition codes, the two stack pointers, the pins and their protocols, and it describes the bus in
terms of states S0–S7.[23][31] It does not name a single internal latch.

`documented` **US 4,325,121** ("Two-level control store for microprogrammed data processor") names the control
side: the two stores, the microcode word's fields (function code, next micro address, type, CBC), the A0/A1/A2/A3
address decoders, the execution unit's names in passing, and — crucially — **Appendix B, a glossary of the
abbreviations used in the microcode listings**: `rx`, `ry`, `db`, `dbh`, `dbl`, `dbd`, `edb`, `dbin`, `dob`, `ab`,
`abh`, `abl`, `abd`, `aob`, `at`, `sp`, `pc`, `dcr`, `ftu`, `inl`, `tpend`, `psw`, `ssw`, `corf`, `au`, `alu`.[15][14]

`documented` **US 4,296,469** ("Execution unit for data processor using segmented bus structure") names the
datapath: three sections (high, low, data), two segmented buses with couplers, and the register file's members as
`RF`, `RE`…`R8`, `R7`…`R0`, `AT`, `DT`, `AOB`, `DOB`, `DBIN`, `AUH`, `AUL`, `ALU`, `ALUB`, `ALUE`, `DCR`, `PREN`,
`FTU`, `PSW`, `SSW`.[16][22]

`derived` Those two documents are the closest thing the 68000 has to a **canonical microarchitecture**. They are
Motorola documents, they are contemporaneous with the design, and their vocabulary is what later work had to adopt
in order to be intelligible.

---

## 3. The correspondence table

| object | manual | US 4,325,121 (App. B) | US 4,296,469 | die analysis (Galibert) | MAME | Gigasoft (die-first) | recommended |
|---|---|---|---|---|---|---|---|
| address output register | — | `aob` | `AOB` | `aob` | `m_aob` | "memory address register" | **AOB** |
| address temporary | — | `at` | `AT` | `at` | `m_at` | "immediate register" | **AT** |
| data temporary | — | — | `DT` | `dt` | `m_at`/local | "register B" | **DT** |
| 16-bit address adder | — | `au` | `AUH`, `AUL` | `au` | `m_au` | "register A" | **AU** (AUH/AUL for halves) |
| 16-bit data ALU | — | `alu` | `ALU` | `alu` | ALU helpers | "register C" (result latch) | **ALU** |
| ALU second operand | — | `alub` | `ALUB` | `alub` | `m_alub` | "register E" | **ALUB** |
| ALU extend / shift | — | — | `ALUE` | `alue` | — | "register G" | **ALUE** |
| field translation unit | — | `ftu` | `FTU` | `ftu` | `m_ftu` | "register F" | **FTU** |
| bit decoder | — | `dcr` | `DCR` | `dcr` | — | "BIT# / BIT" | **DCR** |
| MOVEM encoder | — | — | `PREN` | `pren` | — | "List / next bit" | **PREN** |
| data input buffer | — | `dbin` | `DBIN` | `dbin` | `m_dbin` | "register D" | **DBIN** |
| data output buffer | — | `dob` | `DOB` | `dob` | `m_dbout` | "output register" | **DOB** |
| instruction queue | "instruction register" | `ir` | — | `irc`/`ir`/`ird` | `m_irc`/`m_ir`/`m_ird` | — | **IRC → IR → IRD** |
| status register | `SR`, `CCR` | `psw` | `PSW` | `psw` | `m_psw` | — | **SR** architecturally, **PSW** internally |
| microinstruction status word | — | `ssw` | `SSW` | `ssw` | `m_ssw` | — | **SSW** |
| pending interrupt level | — | `inl` | — | `iplb`/`inl` | — | — | **INL** |
| trace pending | — | `tpend` | — | `trace` | — | — | **TPEND** |
| decimal correction | — | `corf` | — | — | — | — | **corf** |

`derived` Every entry in the last column is a patent name. Nothing in this table had to be invented, and no modern
implementation invented one either: the two code bases that carry the full machine (MAME's generator, and the
tick-accurate Verilog core) both use this set, with an `m_` prefix in the first case.[14][21][22]

---

## 4. How often each name is actually used

`derived` Counting identifiers in the two most complete machine descriptions we can read confirms it. In Galibert's
`an.cc` the patent names are the only internal names present: `au` 25 occurrences, `alu` 15, `dbin` 9, `irc` 9,
`ird` 8, `aob` 7, `ftu` 4, `alub` 3 — and no A–G letter is used as a register name anywhere in it.[6] The `rm68000`
block documents use the same set (`alu` 109, `ird` 59, `alue` 60, `irc` 32, `ftu` 30, `aob` 21, `alub` 19).[21][22]
Gigasoft's letters appear in exactly one published analysis, and only as a mapping aid.[13]

`documented` The die-first naming exists because the letters were the only handle available when reading the
schematic without the patent: "In addition to the architecturally visible registers, there are seven general purpose
registers for use by the microcode, which I have given the names A through G."[13] That sentence is the whole
provenance of the A–G scheme — one author, one thread, explicitly provisional.

`inferred` The reason the patent names won is mechanical: they are printed next to the transfers in the microcode
listings, which is the artifact everyone ends up reading.[14][18]

---

## 5. Where the names diverge, and why

Five distinct causes, worth separating because they need different fixes:

**(a) Die-first vs document-first.** A–G letters vs AOB/AT/DT/ALU. Fix: keep the letters only as an alias column.

**(b) Prototype vs production.** The patent describes a late prototype; the production part dropped `dcnt` for
DBcc, changed the exception and STOP routines, and "most addresses were also reshuffled".[14] Field *names* survived
that change; some field *values* did not. Do not infer production behaviour from a patent listing without checking
the die dump.[3][8]

**(c) Emulator prefix conventions.** MAME's `m_au`, `m_aob`, `m_ird`, `m_ssw` are the same objects behind a
member-variable prefix, which makes cross-reading code and literature slower than it should be.[14][21]

**(d) Generic CPU literature.** `MAR`, `IR`, `MDR`, `temp`, `accumulator` — none of which the 68000 has in that form.
The 68000's "MAR" is AOB *plus* the address bus; its "MDR" is DBIN for reads and DOB for writes; it has no
accumulator and no single temporary.[16][22]

**(e) Genuinely ambiguous acronyms.** Six of them, and these are the ones that actually cause errors:

| name | what it is | the misreading to avoid |
|---|---|---|
| **NROM** | **N**ano ROM (336 × 68) | "next-address ROM" — the next address is a *field of the microword in the µROM*, not a separate array.[11][6] |
| **IRD BUS** | a bank of PLAs keyed on IRD | a bus. "No sure who labeled it as a bus, but that is misleading, it is a PLA, not a bus."[14] |
| **A0 / A0S** | exception entry addresses | the address register A0, or address bit A0. Three different things share the spelling.[21][14] |
| **AU** | arithmetic unit (the address adder) | "address unit" as if it were a separate address generator; it is the same 16-bit adder pair the datapath uses.[16] |
| **DCR** | 4→16 decoder for bit ops | a register. Its *output* is a one-hot 16-bit line; its input is a 4-bit number.[14][22] |
| **PSW vs SR** | internal vs architectural view of the same bits | the SR is not one register on the die: its bits are latched in several places.[14][21] |

---

## 6. The rule to use

`inferred` For anything written in this repository:

1. **Architectural state** gets the manual's name, because that is what a programmer or a test expects: `D0–D7`,
   `A0–A7`, `PC`, `SR`, `USP`, `SSP`, `CCR`, `AS`, `DTACK`.[23][31]
2. **Internal units and latches** get the patent's name, upper-case in prose, lower-case in code:
   `AOB`, `AT`, `DT`, `AU`/`AUH`/`AUL`, `ALU`, `ALUB`, `ALUE`, `FTU`, `DCR`, `PREN`, `DBIN`, `DOB`, `IRC`, `IR`, `IRD`,
   `PSW`, `SSW`, `INL`, `TPEND`.[15][16][21]
3. **One alias table**, in one file — this one — and nowhere else. If a new name appears, it is either an alias to be
   added here or an error.
4. **Never name a net or a bit position** unless quoting a dump that already names it; machine-generated node numbers
   are not vocabulary, they are coordinates.[14]

The diagrams in `schematics/` follow exactly this: manual names on the pins and registers, patent names on the
internal units, and no invented names anywhere.

---

## Sources

## Sources

[3] https://web.archive.org/web/2026/https://og.kervella.org/m68k/microcode.txt — 68000 microcode ROM dump (Galibert)
[6] https://web.archive.org/web/2026/https://og.kervella.org/m68k/an.cc — an.cc: micro/nanocode analysis and nanocode field decode
[8] https://web.archive.org/web/2026/https://og.kervella.org/m68k/adr-names.txt — 544 microcode address labels
[11] http://www.easy68k.com/paulrsm/doc/dpbm68k2.htm — Starnes, Design Philosophy Behind Motorola's MC68000, Byte May 1983 (labeled die photo)
[13] https://gendev.spritesmind.net/forum/viewtopic.php?t=2962 — SpritesMind: 68K inner workings (Gigasoft, 2019-2021)
[14] https://gendev.spritesmind.net/forum/viewtopic.php?t=3023 — SpritesMind: building a microcode-level 68000 core (Nemesis et al.)
[15] https://patents.google.com/patent/US4325121A/en — US 4,325,121: Two-level control store for microprogrammed data processor
[16] https://patents.google.com/patent/US4296469A/en — US 4,296,469: Execution unit for data processor using segmented bus structure
[18] https://github.com/RichardBrown384/68000-microcode-division/blob/main/docs/microcode.md — 68000 microcode primer (RichardBrown384)
[21] https://raw.githubusercontent.com/rmonic79/rm68000/master/docs/microcode-structure.md — rm68000 docs: microcode structure
[22] https://raw.githubusercontent.com/rmonic79/rm68000/master/docs/silicon-datapath.md — rm68000 docs: silicon-structured datapath
[23] https://raw.githubusercontent.com/rmonic79/rm68000/master/docs/clock-dtack.md — rm68000 docs: clock-enable and DTACK generation
[31] https://retrocomputing.stackexchange.com/questions/11821/how-does-states-bus-cycles-and-clock-cycles-differ-in-the-m68000 — Retrocomputing SE: states, bus cycles, clock cycles on the M68000
