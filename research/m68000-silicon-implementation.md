# The Motorola 68000 at the silicon level

### What the die shots show, what was reverse-engineered from them, and where the public record disagrees

*Subject: the original NMOS MC68000 (1979, HMOS, ~68,000 transistors, 6.24 × 7.14 mm die). Studied November 2026.
Basis: the die-photograph corpus and its derived netlists, the Motorola patents, the 1978–1983 design papers, and the
2012–2026 reverse-engineering work published on SpritesMind, GitHub and siliconpr0n. Claims are labelled as this repo
labels them: `documented` (a primary source or the data says so), `derived` (arithmetic over counted facts),
`inferred` (a reference implementation implies it), `unknown`. Companion page: `research/mame-microcode-core.md`
(what the transcribed microcode would cost to execute here).*

---

## 1. Executive summary

The short answers to the seven questions, each expanded in the section named:

| question | answer | § |
|---|---|---|
| Are there die shots? | Yes — two working sets (one delayered, one metal-only), plus a museum photo and a 1983 magazine photo. The best set is delayered stage by stage and is the one everything else is built on. | 2 |
| Are there overlays showing the subsystem layout? | Yes, but not one canonical file: a 1983 Motorola-authored labelled photo, a six-layer vectorised trace of the whole die, an auto-generated schematic, and a forum-published block-by-block map. | 3 |
| Counters and clock signals? | **Partly.** Clocking, phases, the microcycle, the bus state machine and the E divider are located and modelled. The "counters" are mostly *not* dedicated counters: multiply/divide/`RESET` loops count in the address adder, MOVEM steps a priority encoder, bit ops use a 4→16 decoder. No public gate-level enumeration of every latch exists. | 6 |
| Instructions decoded to microcode scripts? | Yes — three PLAs (A1, A2/A3) on the instruction register, 10-bit outputs, dumped term by term from the die. | 7 |
| How microcode is interpreted? | Yes — 544 × 17-bit words, two formats, explicit next-address plus a 5-bit condition code that supplies two address bits; access class drives the bus. | 8 |
| How nanocode is interpreted? | Yes at the field level, **not** completely: 336 × 68-bit words (66 driven), decoded through PLAs into ~180–200 control points, of which the micro/nanocode itself supplies only ~70. The rest is hard-wired decode keyed on the instruction register. | 9 |
| Internal registers and latches? | Yes — a full inventory exists with two independent naming schemes that map onto each other (patent names and a die-derived A–G naming). | 5 |
| ALU implementation? | Yes — three 16-bit units (two address adders, one ALU), a 15 × 5 control matrix, an extension register for shifts, and a buffer for the second operand. | 10 |

`derived` The single most useful structural fact: the control store is **32,096 bits** — 544 × 17 microcode plus
336 × 68 nanocode — which is exactly the figure Galibert quotes when he reports his transcription error rate, and
exactly the sum of the two arrays counted on the die.[84][78] Any source that quotes a different total is counting
something else.

The one-sentence machine: *an instruction word indexes three PLAs, their 10-bit output addresses a 544-word sequencing
ROM and (many-to-one) a 336-word datapath ROM; the sequencer word picks the next address and the bus action, the
datapath word drives six register-file/bus lanes and the ALU; everything the two ROMs do not say is decided by a bank
of PLAs fed by the instruction register.*

---

## 2. The die photographs

Four independent photographic records exist.[81][83][100] They are not equivalent, and the literature's contradictions mostly come
from people using different ones.

| set | who / when | what it is | limits |
|---|---|---|---|
| **visual6502 set** | Visual6502 project, 2011–2012, chip supplied by the project | Whole die, metal layer; 1600 × 1808 up to 10000 × 11299 PNG (223 MB); Quietust isolated the metal traces as a separate image.[81] | Metal only — "this chip will need further deprocessing and photography for the lower layers."[82] Enough to read the ROM bit patterns and the metal, not enough to trace diffusion.[84] |
| **McMaster / Galibert set** | John McMaster, decapped and delayered *at Olivier Galibert's request*; four stages of de-layering | The working set: six vectorised layers (active, polysilicon, buried contact, metal, via, capacitor) at 16000 × 14000 per aligned image.[74][73] | Not published as a formal paper; lives in a directory tree of TIFF/PNG plus tooling, and the original host is now unreachable — the archived copy is the practical access route.[74] |
| **Rautakorpi photo** | Pauli Rautakorpi, 2014 (MC68000L12), Wikimedia Commons | Single package-off die photograph; the image most secondary sites reproduce.[100] | Presentation quality, not a measurement instrument. |
| **Byte 1983 photo** | Thomas W. Starnes (Motorola), *Byte*, May 1983 | Labelled die photo with the manufacturer's own block names.[83] | 640 × 732 px; labels legible, transistors not. |

`documented` The die is 246 × 281 mils (6.24 × 7.14 mm) with "more than 68,000 transistors".[83] A browsable public
gallery of photographed processor dies (363 dies, 1971–2024) now indexes the 68000 among them.[101]

Two practical notes for anyone redoing this: the McMaster images are aligned to `m68000-m-3.png` and the vectorisation
is aligned to it, so that one image must not change;[73] and the whole chain — layer SVG → bitmap per layer → MOSFET and
net extraction → schematic placement — is reproducible from the `dietools` repository and the `readme` in the archive.[74]

---

## 3. Overlays: is there a map of the silicon?

Yes — four of them, at different levels of abstraction.[73][83] What does *not* exist is a single published, authoritative,
machine-readable floorplan with every block named. That is the main reason the secondary literature is vague about
"where things are".

**(a) The manufacturer's own labels.** The 1983 *Byte* photo is annotated by a Motorola engineer and names:
`µROM` and `NROM` (the two control stores), `Trap and Ill. Inst. PLA`, `IRD Reg.`, `ALU Control`, `A0 Mux Control`,
`FC Log.`, `Int. Log.`, `Data Execution Unit`, and two `Address Execution Units`.[83] This is the only overlay with
Motorola's authority behind it, and it fixes the coarse floorplan that every later analysis confirms: control stores
upper right, execution units along the bottom, interrupt/function-code logic at the edges.

**(b) The vectorised layer stack.**[73][74] Galibert and Quietust traced the die by hand into six SVG layers — active (blue),
polysilicon (red), buried contacts (pink), metal (black), vias, capacitors — 42 MB uncompressed, needing ~5 GB of RAM
to open in Inkscape.[73][74] A transistor is "where poly is over active and it's not something else (e.g. not buried or
caps)"; depletion vs enhancement is not visually distinguishable and is resolved by heuristic, "correct at 99.99 % or
so".[86]

**(c) The generated schematic.**[73][74] From the layers, `generate-circuit` emits ~2.8 GB of net/circuit data, and a Lua
placement program turns it into a 23 MB `schem.svg` / 4.9 MB `schem.txt` schematic in which the nets have been moved
into readable positions; `mview` and `sview` display and simulate them.[73][74] This is the artifact the later analysts
actually read. Caveat from its author: the simulator is "essentially digital, with the side effect that relatively
important things like the clock do not work".[73]

**(d) The block-by-block functional map.**[85] Gigasoft's 2019 forum post is, in practice, the missing floorplan document:
it walks the schematic region by region — register file and datapath on the left in three connectable sections with
two lanes each ("up to 6 general purpose data transfers per cycle"), the field-extraction strip up the centre, the ALU
function select and ALU control below it, the instruction decoders at the bottom (word/long on the left, invalid
instruction on the left, second/third stage on the right), the condition-code evaluator above them, the two-section
control store on the right, the "8-bit bus state machine" at the top right, the exception-priority circuit at the
bottom right, and the 16-bit bus logic at the very bottom right.[85]

`documented` Two further annotation efforts exist but are not published as files: Nemesis's "Schematic Explorer", a
C#/WPF tool that loads `schem.svg` and stores human annotations in an external XML alongside it, built precisely
because the SVG is too large and too monochrome to navigate;[86] and a floorplan in which the large PLA between the
control store and the execution unit is labelled `IRD BUS` — a label its users consider misleading, "it is a PLA, not
a bus".[86]

---

## 4. Who reverse-engineered what: the provenance chain

This matters because the layers are routinely conflated. In order:[81][84][91]

1. **2011–2012 — photographs and a first ROM read.** Visual6502 photographs the metal layer.[81][82] The community reads
   bit patterns out of those images; a microcode transcription circulates via `qmtpro.com` and is later mirrored as
   `larsbrinkhoff/m68k-microcode`, together with the microword format and a separate transcription of a Signetics
   68000 by Sean Riddle.[91]
2. **2012 — the first honest error bar.** Galibert reports a "mostly validated dump" of the A1/A2–A3 PLAs but says the
   metal-only image is too dirty to type up the arrays: "about 8 bits wrong out of 136 … almost 2000 bits wrong out of
   the 32096 of the array, which is way more than what I can handle", concluding that "anyone who wants to do the
   microcode-level emulation game is going to need a better delayering".[84]
3. **2013–2019 — the delayered set, the netlist, the dump.** McMaster's staged de-layering, requested by Galibert,
   produces the images that make the arrays readable;[86] Galibert and Quietust vectorise them; the tooling emits the
   netlist, the schematic, and — via `mplay` — the microcode and nanocode dumps plus the address PLAs[73][2]: `microcode.txt`,
   `mic-adr.txt`, `rommap.txt`, `adr-names.txt`, `a1-decode.txt`, `a23-decode.txt`, and `an.cc`, the analysis program
   that decodes both stores.[74][75][78]
4. **2019–2021 — the functional reading.** Gigasoft publishes the layout map and a large body of reconstructed
   microcode in plain-English step form, including an ALU function table read out of the schematic.[85] Nemesis
   transcribes all 92 pages of the patent's microcode appendix and starts a microcode-level core; ijor, who had already
   built a cycle-accurate Verilog core from the decap, contributes the crucial corrections.[86][98]
5. **2022 — the microcode becomes executable.** MAME's 68000 core is regenerated "from the micro/nanocode": the ROM
   contents become data, and the generator emits the interpreter.[`research/mame-microcode-core.md`]. `derived` This is
   the point at which the die transcription stops being a document and becomes a running machine.
6. **2024–2026 — tick-accurate re-derivation.** A third-generation core (`rm68000`) rebuilds the machine from the
   patent's tables plus the die tables, block by block, with a per-block document naming the source of every rule and
   per-block benches over all 65,536 opcodes.[92][93][97]

Attachments: `68000-silicon/die/` holds the photographs and a numbered overlay of the 1983 figure,
`68000-silicon/schematics/` the five educational sheets, `68000-silicon/nomenclature.md` the naming study.

`unknown` No one has published a formal, peer-reviewed account of the 68000 decap. The authoritative record is a
directory of files, two long forum threads and a set of per-block documents in a Git repository; that is the state of
the art, and it is better than most chips of the era ever get.

---

## 5. The machine the silicon shows

### 5.1 Three sections, two buses, six lanes

`documented` The execution unit is "a segmented two-bus structure divided into three sections by bidirectional bus
couplers".[88][93] The **High** section holds the upper 16 bits of every 32-bit register plus a 16-bit arithmetic unit
(AUH); the **Low** section holds the lower halves of the address registers, the PC and the address temporary, plus AUL;
the **Data** section holds the lower halves of the data registers, the ALU proper, and the special units (priority
encoder, bit decoder).[88][94] Each section carries an address bus lane and a data bus lane, and the couplers can join
or split them, giving up to six independent 16-bit transfers in one cycle.[85][88][93]

### 5.2 The register file, counted

`documented` The register file holds **21 32-bit registers**: D0–D7 (high halves in High, low halves in Data), A0–A6
(high in High, low in Low), USP and SSP, PC, the address temporary AT, and the data temporary DT — plus the address
output buffer AOB, which is physically embedded in the unit although drawn outside it.[86][93] On top of those, **10
16-bit registers**: the latched AUH output, the latched AUL output, the latched ALU output, ALUB (ALU's second operand),
ALUE (the ALU extension/shift register), DCR (a 4→16 decoder used for bit manipulation), DOB (data output buffer), DBIN
(data input buffer), FTU (field translation unit) and PREN (priority encoder for MOVEM).[86][94]

Two independent naming schemes exist for the same hardware — one from the patent's datapath drawings, one from the
die schematic — and they must not be mixed:[86][88]

| patent / functional name | Gigasoft's die name | role |
|---|---|---|
| AOB (address output buffer) | memory address register | what goes out on A0–A23 |
| AT (address temporary) | immediate register | prefetch address / saved operand address |
| DT (data temporary) | register B | last accessed memory address / operand |
| AUH, AUL outputs | register A (high, low) | registered adder result, can be copied to PC |
| ALU output latch | register C | ALU result, writeable to memory |
| DBIN | register D | last data read from the bus |
| ALUB | register E | ALU's second operand |
| FTU | register F | SR/IR/IPL/vector/immediate field source; can write SR or CCR |
| ALUE | register G | shift register, chained to the ALU |
| DCR in / out | BIT# / BIT | bit-number decode, selected bit |
| PREN | List / next bit in list | MOVEM mask walk |

`derived` The two schemes differ in *grouping*, not in hardware: Gigasoft names functional latches as he finds them on
the schematic, the patent names the same latches by role in the datapath, and a modern re-implementation collapses
several of them into ports of one register-file array (19 × 32-bit plus separate bus-side registers).[94] Any claim of
the form "the 68000 has N internal registers" is therefore a statement about a particular grouping; there is no
canonical N.[94]

### 5.3 Beyond the register file

`documented` Three instruction registers form a pipeline: `IRC` (next word, from the bus), `IR` (the word being
decoded), `IRD` (the word being executed, tied directly to ALU control, branch control and the field translation
unit).[86][93] The status register is not one register: its bits are latched in several places, with `INL` (3-bit
pending interrupt level) and `TPEND` (trace pending) alongside, and a separate `SSW` — "special word which monitors
status of current microinstruction", saved into the exception frame so a faulting instruction can be resumed or
diagnosed.[86][93]

### 5.4 The standard cells

`documented` The die uses one latch idiom almost everywhere: input drives two inverters, the value loops through a very
weak enhancement transistor with gate tied to its terminal (l/w < 0.2) so the input can overwrite it; both the value and
its complement are available.[73][74] The register bit is the patent's dual-port cell: two inverters holding the value, two
independent vertical buses (address and data), read by letting a bus float and connecting the cell, written by driving
the bus and then connecting.[73][88] `derived` Two cells and one bus discipline explain why the register file is cheap
to replicate per section and why every register is dual-ported.[73]

---

## 6. Clocks, phases and the things that count

This is the weakest-covered area in public, and the honest answer has three parts: what is established, what is
inferred from models, and what is simply not published.

### 6.1 Established

- `documented` One clock pin; the processor acts on **both edges**. A bus cycle is "four clock periods (eight bus cycle
  states)", one state starting on the rising edge and one on the falling edge.[95][103]
- `documented` A **microcycle is two clock periods — four phase edges** (φ1, φ2, φ1, φ2); a microword occupies one
  microcycle, so a microinstruction costs two clocks. One that must complete a bus operation costs four, because a bus
  cycle is four clock periods and spans two microcycles.[93][95] The patent requires this explicitly: because the
  next control word is fetched while the current one executes, "a cycle is allowed in programming the microroutines".[93][86]
  `derived` The familiar instruction timings are therefore not tabulated anywhere — they are the lengths of the
  microroutines, which is why the MAME model charges 2 clocks per micro-state and 4 per bus access and reproduces the
  manual's tables.[`research/mame-microcode-core.md`]
- `documented` **Wait states stretch the current microword**, not a separate counter: the sequencer holds at the end of
  the microcycle until the bus cycle reaches its last state, so every wait state lengthens the microinstruction by a
  full period.[95][93]
- `documented` The **E clock** (for 6800-family peripherals) is generated on chip from a 10-state counter: "a single
  period of clock E consists of 10 MC68000 clock periods (six clocks low, four clocks high)".[95] The netlist-derived
  model of that block is a four-stage divider with set/reset decoding — a divide-by-ten, not a free-running prescaler.[79]
- `documented` The **bus state machine** exists as a distinct block, identified on the die at the top right of the
  control-store region and described as an "8-bit bus state machine".[85] Its states are the S0–S7 of the manual, with a
  read-modify-write variant that keeps AS asserted across two handshakes.[95]
- `documented` The **microcode address path** has no incrementer: the address is always explicit or PLA-supplied, held
  in a register, and selected among {next-address field, A1, A2, A3} with two overriding exception addresses A0 and
  A0S.[93][86]

### 6.2 The counters are mostly not counters

This is the finding that contradicts the intuitive picture of a microcoded CPU full of loop counters.

| loop | how it is counted | evidence |
|---|---|---|
| MULU/MULS, DIVU/DIVS | the **address adder AU** is the loop counter, loaded with the constant 15 from the field translation unit; the branch condition `auz` tests AU = 0 on its **six low bits**[93][96] | `documented` [96] |
| `RESET` (132 clocks) | same: AU counts down from −2 by 2, and six bits end the loop after 31 iterations, giving exactly the manual's 132 clocks | `derived` [93] |
| MOVEM | the **priority encoder PREN** walks the register mask; the nanocode keyword `reset pren` advances it | `documented` [86][93] |
| bit ops (BTST/BCLR/BSET/BCHG) | the **DCR** 4→16 decoder turns the bit number into a one-hot line; the branch variable `d4` exposes one of its bits | `documented` [86][93] |
| shifts/rotates | count comes from the instruction field; the datapath shifts one place per microcycle through ALU+ALUE, so the loop is again microcode | `documented` [93][94] |

`inferred` What remains as genuinely dedicated counting hardware is: the microcycle phase pair, the bus state machine,
the E divider, and the small state latches in the exception logic. Everything else that "counts" is data flowing
through the address adder under microcode control.

### 6.3 Not published

`unknown` There is no public gate-level enumeration of the clock generator's internal phases, nor of every latch with
its enable. What exists: the block has been located and simulated from the netlist (a 2019 post shows the clock
generation section "working" in a transistor-level simulator, with transistors colour-coded by state),[86] the E
divider has been extracted as Verilog,[79] and every modern core re-derives the phase discipline from the manual and
validates it against the bus protocol.[95] The internal phase generator itself — the part between the CLK pad and the
two non-overlapping phases — has not been written up.

---

## 7. From instruction word to microcode script

`documented` Three PLAs sit on the instruction register. **A1** is the entry address and exists for every opcode;
**A2** is used after an immediate operand; **A3** after an effective-address calculation. A1 has switched loads, A2/A3
static loads, and A2/A3 share one AND matrix which also produces the invalid-opcode (IOC) and privileged (PRIV)
signals.[84][87] Two more addresses exist: **A0**, which replaces A1 at an instruction boundary when an exception is
pending, and **A0S**, which overrides the next address immediately for reset, bus error and address error.[86][93]

`derived` Counting the dumped A1 table gives **149 terms** — 129 keyed on opcode bits, 20 keyed on the
effective-address field alone (ten for long, ten for byte/word), with inhibit lines between terms.[76] The table is
literally a PLA personality: each row is a two-level AND/OR term over the 16-bit instruction and its complement.[76][84]

`documented` The output is a **10-bit** microcode address, and it addresses *both* ROMs at once.[93] Where the PLAs
produce no address, the next-address field does; where a branch is conditional, two of the ten bits come from the
branch logic instead (below). A modern core builds the same thing as "100 row classes × 13 column classes", writes
1,298 of 1,300 cells, and checks all 65,536 opcodes against the dumped PLA table.[93]

`derived` The physical ROM row order is not the address order: `rommap.txt` maps 118 physical rows to address ranges,
in a deliberately scrambled sequence (row 118 → 000–00f, 117 → 020–02f, … 085 → 3e0–3ef for the microcode; row 84 →
000–003 … row 001 → 3e0–3ef for the nanocode), and `mic-adr.txt` is the PLA that performs the mapping.[77] This is the
one place where reading the die beats reading the patent: the patent's addresses are the prototype's, and "most
addresses were also reshuffled" in production.[86]

---

## 8. How the microcode is interpreted

`documented` The sequencing store is **544 words × 17 bits**, physically 34 rows × 16 columns, with the 544 address
labels dumped in `adr-names.txt` (`rstp1`, `dvur1`, `mmrw3`, `trap1`, 25 `zzz…` unused).[77][80] Two word formats, per the format note and the patent:[91]

**Format I (bit 1 = 0), direct next address**

| bits | field | meaning |
|---|---|---|
| 16–15 | FC | function code for the bus: n none, u unknown, d data, i instruction, a interrupt acknowledge |
| 14–5 | NMA | next micro-ROM address, 10 bits |
| 4, 0 | C, I | IR/TVN update: `db` neither, `dbi` IRC→IR and update trap-vector number, `dbc` IRC→IR only, `dbl` partial TVN |
| 3–2 | TY | source of next address: db (NMA), a1, a2, a3 |

**Format II (bit 1 = 1), conditional**

| bits | field | meaning |
|---|---|---|
| 6–2 | CBC | condition code, 5 bits |
| 14–7 | NMBA | base address, 8 bits; bits 7 and 6 of the target come from the condition logic's C1/C0 |

`derived` That is why a conditional branch is a **4-way** branch: a base address whose bits 7:6 are replaced by two
condition bits selects one of four words in the same "logical row" of both ROMs (e.g. `mulm4` → `mulm6`, `mulm4`,
`mulm3`, `mulm5`, at 020/0e0/060/0a0 on the die).[93] The 5-bit CBC selects which condition to test; Galibert's decoder
lists 32 codes over 15 variables — `i11` (instruction bit 11), `auz` (AU = 0), `c`, `z`, `n`, `v`, `nz1`, `nz2`,
`ms0`/`m01` (multiply), `ze` (ALU = 0), `nv` (CHK), `d4` (bit decoder bit 4), `enl` (MOVEM end-of-list), `cc`
(Bcc/Scc), plus inverted forms — and the die uses 21 of them.[78][93]

`documented` The other half of the word is the **access label** (four characters: class i/f/n/t × mode p/r/w × type
ak/im/in/ix/op/uk) which tells the machine whether to start a bus cycle, whether it must finish one before the next
microcycle, and which address space to use.[93] This is the mechanism by which "microcode controls the bus timing":
not by specifying cycles, but by declaring an access class and letting the state machine run.[93]

`documented` The two-level split was a deliberate answer to the LSI constraints of the period: the design team's own
account argues that a regular control store costs less area than equivalent random logic, shortens design time, lets
microcode and circuits be written in parallel, and gives "much cleaner" clocking than control dispersed through a
combinatorial maze — while an off-chip store was ruled out by pin count, which would have forced a narrow, slow,
vertical microcode.[89]

`documented` On the die the ten NMA bits are **physically permuted** relative to the patent's bit order, which is why
Galibert's `an.cc` reassembles the next address with a shift/mask scramble rather than a field slice.[78][93] Anyone
reading `microcode.txt` directly must apply that permutation or the branch targets come out wrong — a trap that a 2019
attempt ran into ("the branch addresses were screwy").[86]

---

## 9. How the nanocode is interpreted — and what it does not say

`documented` The datapath store is **336 words × 68 bits**, physically 84 rows × 4 columns.[78][77] It is addressed by
the *same* address as the microcode, many-to-one: several microcode words share one nanoword when the datapath work is
identical, which is why there are 544 sequencing words and only 336 datapath words. `derived` Unifying them would have
cost 544 × 85 bits instead of 32,096 bits of array — the redundancy saved is about 14,000 bits.[86]

`documented` Physical 68 bits, but **66 driven**: "the nanostore allocates physically 68 bits, but two bits are unused.
… on the die you can see two metal traces coming from the nanocode, going to the execution unit but not connected to
anything."[86]

`documented` The word is decoded into "about 180 control points" using "about three gate levels of decoding".[93]
Galibert's decoder splits it 4 / 32 / 32 and names roughly forty fields directly: register/ALU transfers (`regh↔dbh`,
`abh↔ath`, `abl=abh`, `dbl=dbh`, sign extends), the AU input constant select (`db`, `db+1`, `db+2`, `db−1`, `db−2`,
`db−4`, `db+alb`), `au = alu`, destinations (`d`, `a`, `pc`), a four-bit "mid state" field that encodes `sr.t = 0`,
`trace = 0`, `ibuf = ib1`, `mwm_w` (MOVEM write), `disp…`, `sr.int = iplb`, `dl = ibuf`, `ib = ibuf`,
`trace_srt_w`, and the ALU mode field (3 bits + a 2-bit variant flag).[78] A 1985 book chapter describes the same word
being steered out in **three thirds of 23, 23 and 22 bits** to a "parameterisation PLA" of 32 lines (23 used), selected
by IPL1/IPL2 — a test-mode view of the same field structure.[84]

`derived` The 4/32/32 split in Galibert's decoder and the 23/23/22 split in the book are two descriptions of one
68-bit word at different granularities (his first 4 bits are the field that the book counts inside its first third);
neither is a contradiction, and neither is a complete bit map.

### The part everyone gets wrong

`documented` The nanocode is **not** the whole control. Galibert, who dumped it: "There are roughly 200 control signals
to the EU, the registers, etc. There are more or less 70 signals coming from the micro or nanocode (68 from nano plus
the two bus access type from micro). The 70→200 expansion is controlled by a ton of PLAs keyed on the IRD register.
Plus other things are fully PLAd, like the exception/interrupt vector addresses."[86]

`documented` ijor, who had to make a cycle-accurate core work, spells out the consequence: "As soon as you start
studying the microcode you realize that there is a lot of stuff that is missing… It doesn't always specify exactly
which type of registers are transferred. I.e., it doesn't specify if RX and RY are address or data registers … or if
RX is actually the temporary register, the USP, or actually from a MOVEM register list."[86]

`derived` So the correct model is three layers, not two: ROM (sequencing) → ROM (datapath atoms) → **PLA bank keyed on
the instruction register** (which register file, which size, which vector, which exception). The largest of those PLAs
sits physically between the control store and the execution unit and is the one a floorplan labels `IRD BUS`.[86] Any
"microcode-level emulation" that stops at the two ROMs is emulating the datapath and guessing the rest.

---

## 10. The arithmetic: three units, one matrix

### 10.1 Organisation

`documented` Two 16-bit arithmetic units (AUH in the High section, AUL in the Low section) and one 16-bit ALU in the
Data section.[88][85] The AUs form a 32-bit address adder split across sections; the ALU handles data, with two
attachments: **ALUE**, a 16-bit register that chains to the ALU for shifts (and holds the other half of a 32-bit pair),
and **ALUB**, a 16-bit buffer for the second operand — "during division the ALU buffer is used to store the 16-bit
divisor".[90][86]

`documented` A small constants ROM feeds the adder directly — the values 0, ±1, ±2, +4, +8 and the instruction size —
so address arithmetic needs no bus traffic.[78][86][90] The AU can write PC directly, without going through either bus,
which is how branch and prefetch targets land.[90][86]

`derived` 32-bit operations are two passes through a 16-bit ALU, with the low half result parked in ALUB/ALUE between
passes; the carry/borrow travels in the carry latch, and for shifts the pair ALU:ALUE is rotated in place.[90][94]

### 10.2 The control matrix

`documented` The ALU and condition-code control block "is arranged as a matrix of 15 rows and 5 columns".[87][93] The row
is selected from the instruction word by a PLA (the patent's FIG. 18 lists the term patterns and the rows each
inhibits); the column comes from three nanocode bits (NIF0–NIF2), and when none is set no column line is driven and the
ALU register is not updated. Each cell holds an operation plus a five-character condition-code assignment in the order
X N Z V C. Column 1 of several rows has **two** entries, selected by the nanocode's initiate/finish bit — the mechanism
by which a 32-bit add updates flags only on the second pass. A sixth column is column 4 with a 1 shifted in (the
quotient bit of a division). One line (INHCC) suppresses condition-code updates entirely, e.g. for address-register
destinations.[93]

`documented` Gigasoft's die-derived table gives the operations as 13 sets of three functions plus AND plus byte
sign-extension, with the flag behaviour per set. Representative cells:[85]

| set (his label) | op 1 | op 2 | op 3 |
|---|---|---|---|
| ADD/ASR/MOVE | A + B | A + B + IC | A & (B LSR 1) |
| SUB/NEG/LSR | A − B | A − B − IC | A & (B LSR 1) |
| ABCD/ASL | A + B + X | A + B + IC | (A & B) SHL 1 |
| SBCD/NBCD/ROL/LINK | A − B − X | A + B + 1 | (A & B) ROL 1 |
| MUL | A − B | A + B | A & (B LSR 1) |
| DIV | A − B | A − B − IC | (A & B) SHL 1 |

with `A | Ext(B)` and `A & B` as the two common pass-through rows, and IC an internal carry that is always written.
`derived` The two counts coexist: 15 rows × 5 columns in the patent's matrix, 13 named function sets in the die dump,
16 rows (row 0 = "no row") in a modern generated table.[93] They are groupings of the same array — the die dump merges
rows that share a function set, and row 0 exists because LEA/PEA/JMP/MOVEM/Bcc use the ALU only as a bus pass.[93]

### 10.3 Bit manipulation and BCD

`documented` Bit operations do not go through the ALU: DCR decodes the bit number to a one-hot 16-bit line, the
selected bit is extracted, and the result is written back by the register file; BCHG/BCLR/BSET recompose the word from
the mask.[86][94] Decimal arithmetic uses the same ALU with a correction factor supplied to it — the field translation
unit's `corf` is listed as "correction factor for decimal arithmetic which can be provided to ALU".[86] `unknown` The
precise ABCD/SBCD flag rules are documented behaviour but were established by black-box testing long before the decap,
and are not derivable from the microcode alone — the microcode is shared between the two instructions, and the
difference lives in the flag logic.[86]

---

## 11. Conflicts in the record, adjudicated

The point of this section is that a reader who consults three sources will meet three numbers. These are the ones that
matter, with the resolution.

**1. 336 or 366 nanowords?** `derived` 336. The die gives 84 rows × 4 columns, `rommap.txt` confirms 84 nanocode rows
against 34 microcode rows, and 544 × 17 + 336 × 68 = 32,096 — the exact figure Galibert uses for the array size and the
figure the literature repeats.[77][78][84] The "366" appears in a 2016 note accompanying the earliest public dump and is
a transcription-era error.[91]

**2. 68 or 66 nanocode bits?** Both, at different levels: 68 allocated in silicon, 66 driven, two traces left
unconnected.[86] A decoder that reports 68 fields is describing the array; one that reports 66 is describing the
control.[86]

**3. Is the patent's microcode the production microcode?** No. `documented` The patent is a late prototype: it has no
DBcc at all (a simpler `dcnt`), and the exception, STOP and DBcc routines differ; "the actual microcode seems to be
very similar, almost identical … The main differences are, as expected, at the exception microcode, the Stop
instruction, and the DBcc one. … Most addresses were also reshuffled."[86] It is still the only source for the
*semantics* of each microinstruction, because the die gives bits, not names.[90][86]

**4. Does the microcode control everything?** No — see §9: ~70 of ~200 control signals, the rest from IRD-keyed PLAs,
with the exception vector addresses fully PLAd.[86]

**5. The VPA "test mode" — does it read out the ROM?** `documented` A 1985 book chapter describes a mode enabled by
taking VPA to about 8 V, occupying ~4 % of the die and ~250 transistors, in which "the microinstruction contents is
readable … on address pins", with IPL1/IPL2 selecting which third of the 68-bit action word is connected to the 23
lines of a parameterisation PLA, BR/BGACK replacing the branch bits, and DTACK/IPL0 selecting which decoder PLA is
exercised.[84] The description comes from a 1985 collection on VLSI microarchitecture whose 68000 chapter also
evaluates a self-checking variant of the design.[102] A contemporary reading objected that "you can't actually read out the PLA contents directly nor can you
read out the microcode ROM", and that the mode is for testing the decoder PLAs.[84] `derived` Both are right and the
distinction matters: the mode does not dump the array; it *injects* an instruction and *observes* the resulting
control word, third by third, on the address pins. That is enough to exhaustively characterise the PLAs and the
nanoword-to-PLA wiring, and it is why the book's description of the field split (23/23/22) is trustworthy. Nobody in
the public record has reported running the mode on real silicon.[84]

**6. "20 % of the die, 23,000 transistors."** `documented` Wikipedia repeats "about 20 % of the chip's surface area …
is the microcode system" and "approximately 23,000 of the processor's 68,000 transistors" — the second figure carrying
a *citation needed*.[99] `derived` The second is arithmetically impossible as stated: the two arrays alone are 32,096
bits, and a mask ROM bit costs about one transistor, so the arrays alone exceed 23,000 before any decoder, latch or
output driver. Either the figure counts only part of the control logic or it is simply wrong. Treat both numbers as
unverified.[99]

**7. "Microcode-level emulation gives exact timing."** `documented` Partly: it gives the bus access *order* — notably
the exception frame push order — and very accurate timing, "but not necessarily 100 % exact", because of the missing
IRD decode and because the nanocode's order of transfers is sequential-consistent, not literal.[86] `derived` It is
also not independent evidence of silicon behaviour when the reference core and the test corpus were both generated from
the same transcription; that is a closed loop, not a measurement.[`research/mame-microcode-core.md`]

**8. "One 16-bit ALU" vs "two arithmetic units and one ALU."** Both: one ALU for data, two 16-bit adders for addresses,
which is how a 32-bit address computation and a 16-bit data operation proceed in the same cycle.[88][83]

---

## 12. What is still not known, or not published

- **A complete nanocode bit map.** ~40 fields are named in the public decoder; the full 66-bit assignment and the exact
  PLA expansions exist inside at least two working cores (FX68K identifies all control lines,[98] rm68000 decodes 199
  control bits[94]) but not as a published bit-level specification.
- **The IRD PLA bank.** Its truth tables are large and no one has published them; the block-level statement ("a ton of
  PLAs") is where the record stops.[86]
- **The internal clock generator**, beyond the E divider.[79][86]
- **Die-revision differences.** R9M and later mask sets, the 68HC000 CMOS conversion, and the 68010's small microcode
  changes are not covered by the die work, which is all on one NMOS part.[73][74]
- **Why particular rows are laid out as they are.** The address scrambling in `rommap.txt` is readable but its
  rationale (layout pitch, decoder fan-out, column sharing) has not been written up.[77]
- **The test mode, on hardware.** Described in 1985, never publicly exercised.

---

## 13. What this means for `the-st`

Three practical rules, consistent with the labelling used elsewhere in `research/`:

1. **Die beats patent for content; patent beats die for names.** The ROM bits and the PLA tables should come from the
   decap dumps;[75][78][80] the meaning of a field, the name of a register and the intended semantics come from
   US 4,325,121 and US 4,296,469,[87][88] with the known errata (DBcc, STOP, exception words, reshuffled addresses).[86]
2. **Anything from an emulator is T3, even when it descends from the die.**[93][97] MAME's tables are a transcription executed
   by a model, and the corpus we gate on was generated by that model — agreement with it is consistency, not
   measurement.[`research/mame-microcode-core.md`] The only independent referees remain hardware captures and the
   manual's own tables.
3. **Do not model the nanocode as the control.**[86][93] A state loop driven by the two ROMs plus the 2-clocks/4-clocks rule
   reproduces bus order and nearly all timing; the remaining state differences (frame bytes, commit points) come from
   the IRD decode, and that is where the money is for the frame class of failures.[`research/mame-microcode-core.md`]

---

## Sources

[73] https://siliconpr0n.org/archive/doku.php?id=motorola:68000 — siliconpr0n archive: Motorola 68000 die shots, vectorized layers, schematics
[74] https://web.archive.org/web/20260728132147/https://og.kervella.org/m68k — og.kervella.org /m68k file index (archived)
[75] https://web.archive.org/web/2026/https://og.kervella.org/m68k/microcode.txt — 68000 microcode ROM dump (Galibert)
[76] https://web.archive.org/web/2026/https://og.kervella.org/m68k/a1-decode.txt — A1 decode PLA terms read from the die
[77] https://web.archive.org/web/2026/https://og.kervella.org/m68k/rommap.txt — micro/nanocode ROM row-to-address map
[78] https://web.archive.org/web/2026/https://og.kervella.org/m68k/an.cc — an.cc: micro/nanocode analysis and nanocode field decode
[79] https://web.archive.org/web/2026/https://og.kervella.org/m68k/eclock.v — eclock.v: E-clock divider extracted from the netlist
[80] https://web.archive.org/web/2026/https://og.kervella.org/m68k/adr-names.txt — 544 microcode address labels
[81] http://visual6502.org/images/pages/Motorola_68000.html — visual6502: Motorola 68000 die photographs
[82] https://github.com/trebonian/visual6502/wiki/Home — visual6502 project status: Motorola 68000
[83] http://www.easy68k.com/paulrsm/doc/dpbm68k2.htm — Starnes, Design Philosophy Behind Motorola's MC68000, Byte May 1983 (labeled die photo)
[84] https://gendev.spritesmind.net/forum/viewtopic.php?t=1091 — SpritesMind: M68000 microcode-level emulation (2012)
[85] https://gendev.spritesmind.net/forum/viewtopic.php?t=2962 — SpritesMind: 68K inner workings (Gigasoft, 2019-2021)
[86] https://gendev.spritesmind.net/forum/viewtopic.php?t=3023 — SpritesMind: building a microcode-level 68000 core (Nemesis et al.)
[87] https://patents.google.com/patent/US4325121A/en — US 4,325,121: Two-level control store for microprogrammed data processor
[88] https://patents.google.com/patent/US4296469A/en — US 4,296,469: Execution unit for data processor using segmented bus structure
[89] https://dl.acm.org/doi/pdf/10.1145/1014198.804299 — Stritter and Tredennick, Microprogrammed implementation of a single chip microprocessor (1978)
[90] https://github.com/RichardBrown384/68000-microcode-division/blob/main/docs/microcode.md — 68000 microcode primer (RichardBrown384)
[91] https://github.com/larsbrinkhoff/m68k-microcode — m68k-microcode: microword format and microcode dump
[92] https://github.com/rmonic79/rm68000 — rm68000: tick-accurate MC68000 in SystemVerilog
[93] https://raw.githubusercontent.com/rmonic79/rm68000/master/docs/microcode-structure.md — rm68000 docs: microcode structure
[94] https://raw.githubusercontent.com/rmonic79/rm68000/master/docs/silicon-datapath.md — rm68000 docs: silicon-structured datapath
[95] https://raw.githubusercontent.com/rmonic79/rm68000/master/docs/clock-dtack.md — rm68000 docs: clock-enable and DTACK generation
[96] https://raw.githubusercontent.com/rmonic79/rm68000/master/docs/mul-div.md — rm68000 docs: multiply and divide
[97] https://raw.githubusercontent.com/rmonic79/rm68000/master/docs/chip-data-sources.md — rm68000 docs: which data come from the chip
[98] https://github.com/ijor/fx68k — FX68K: cycle-accurate 68000 in Verilog from the decap
[99] https://en.wikipedia.org/wiki/Microcode — Wikipedia: Microcode (68000 area and transistor claims)
[100] https://commons.wikimedia.org/wiki/File:Motorola_68000_die.JPG — Wikimedia Commons: Motorola 68000 die photograph
[101] https://sheets.works/data-viz/cpu-dies — sheets.works: gallery of photographed processor dies
[102] https://link.springer.com/chapter/10.1007/978-94-009-5143-3_10 — Microarchitecture of the MC 68000 and Evaluation of a Self Checking Version (Springer)
[103] https://retrocomputing.stackexchange.com/questions/11821/how-does-states-bus-cycles-and-clock-cycles-differ-in-the-m68000 — Retrocomputing SE: states, bus cycles, clock cycles on the M68000
