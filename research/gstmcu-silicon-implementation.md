# The GSTMCU (C300589-001) at the silicon level

### What the recovered design says, what the reconstruction infers, and what only the board can settle

*Subject: the STE's combined GLUE + MMU, `U400` on the 1040STE board, Atari house number C300589-001, 144-pin;
the design in Atari's own CAD files is code number **TA-4081 / ST-4081**, "ST MCU / GLUE COMBO", engineer
D. Renn, sheets dated Oct 1987 – Nov 1988. Studied November 2026.*

*Basis, and it is not the one the 68000 study had: **there is no die photograph of this part, of any of its
revisions, anywhere in public.** The study therefore rests on a recovered **gate-level design** — the CAD
netlists, the gate-count list, the 33- and 14-sheet schematic sets, the simulation files and one design memo —
plus a Verilog reconstruction derived from the same drawings, the service manual's nets, and the two vendored
emulators. Every claim carries the label this repo uses — `documented` (a primary source or the data says so),
`derived` (arithmetic over counted facts), `inferred` (a re-implementation implies it), `unknown` — and, where a
claim is about our core, its evidence tier T1 (hardware-captured) / T2 (hardware-corrected) / T3
(emulator-derived). Companion pages: `research/gstmcu-reconstructions.md` (the RTL, file:line),
`research/recovered-asic-designs.md` (the archive, sheet by sheet), `design/system-control-unit.md` (the shape
our model takes).*

---

## 1. Executive summary

| question | answer | § |
|---|---|---|
| Do die photographs exist? | **No.** Not for the GSTMCU, not for any STE custom. The public decap archives hold five Atari parts and every one of them is Lynx or 7800 silicon. What exists instead is the design itself, which is better for structure and worse for proof. | 2.1 |
| What is it made of? | `documented` **15,394 transistors** in a 50-cell standard-cell library, dominated by 553 inverters, 339 NANDs of two to nine inputs and 168 tristate buffers. The netlist of the TA revision lists 1,615 components over 1,508 nets. | 3.1 |
| Microcode, PLA or random logic? | **Random logic.** No ROM, no PLA, no RAM array, no programmable cell of any kind: the largest cell is a 90-transistor register latch and the total storage is 68–73 flip-flops and ~90 latches. | 3.2 |
| What counts? | **Real counters, unlike the 68000.** Five dedicated ones — refresh row, video address, sound address, DMA address, paddle — each with its own clock net. The chip has no datapath that can be made to count. | 3.4 |
| Clocking? | One 8-phase timing generator, drawn as a **single macro cell (`TGEN`)** from `TIME0`–`TIME7`, fed by a 16 MHz clock divided to 8/4/2 MHz and 500 kHz on sheet 7. Every slot decision in the chip is a decode of those phases. | 3.3 |
| How does the address mux arbitrate? | Ten 4:1 muxes (`MX41`), one per `MAD` line, with the designer's own decode table printed on the sheet: refresh → sound → video inside a slot, CPU → DMA outside it. | 3.6 |
| What does the emulator still not know? | The refresh *period* and row mapping, the ADC's conversion law, and the exact enabling of the sound/refresh scheduling. §6 ranks them; §7 gives the six tests that settle them. | 6, 7 |

`derived` The one-sentence machine: *a 16 MHz clock is divided into eight phases by a single macro cell; every 250 ns one phase picks an address from ten 4:1 muxes — video while the display window is open, sound DMA if a request is outstanding outside it, otherwise a RAS-only refresh — and the CPU or the DMA controller gets the other slot of the pair, while 68 flip-flops and ~90 latches hold the register state and no table of any kind is consulted.*

`documented` And the finding that reframes the whole chip for us: **the sound channel was not an STE afterthought bolted onto a GLUE clone — it was specified into the base design in April 1988, in a memo that states its register semantics, its slot policy and its three new pins more precisely than any emulator does.** That memo (§4) is the most valuable single document in the archive, and it contradicts a habit our own design notes inherited from emulator source.

---

## 2. The evidence, and what each layer can answer

### 2.1 There are no die photographs

`documented` The search, recorded so it can be repeated: the siliconpr0n archive's full-text search for *atari* returns five devices — `c025349-31`, `c301719-001`, `c302284-001`, `mikey-rev2`, `suzy-rev2` — of which the first three are Atari house numbers of the Lynx's Mikey (C301719) and Suzy (C302284) and the 7800's Maria-C (C025349), and the last two are the Lynx pair again by code name.[4][5]
`documented` The visual6502 die-shot index lists exactly one Atari part, the 2600's TIA (`10444`), and its "Others…" index sits on the VisualChips wiki, which was answering "Can't contact the database server" when checked on 2026-11-21.[12]
`inferred` An ST-family die photograph does circulate — of the ST's *separate* MMU `C025912`, not the GSTMCU — but as a blog contribution referenced from a forum thread, not as an archive entry; the URL given there no longer answers and has no Wayback snapshot.[10]

| what a GSTMCU decap would give | what it cannot give |
|---|---|
| the shipped mask's cell placement and any revision drift from these drawings | intent, names, or the meaning of any field |
| proof that the silicon is this design (rather than a transcription of it) | the behaviour of a corner the drawings already fix |
| per-block area, to check the layout the sheets imply | anything about the two other part numbers (C302183, Mega STE) unless they too were decapped |

`inferred` For this part the trade is poor.
The design is already machine-readable and complete enough to give area, cell census and connectivity; a decap would cost about the same as one Falcon chip in the 2025 initiative (≈$1,000 per part at the lab's estimate, $8,021 raised for three)[3] and would answer almost none of §6's open questions, all of which are *timing* questions that a board with a scope answers for free.
That is why the owner's ruling for this campaign is die-level work only where a decap exists.

### 2.2 The recovered design is the primary source

`documented` Christian Zietz recovered the drawings from backup floppies in the Atari HQ material and published them; the archive contains, for this chip, **three revisions** — `TA-4081`, `ST-4081` and a `TXL` variant printed as `4081NEW.PDF` — and Zietz describes exactly that: "three different revisions of the STE GSTMCU (code numbers TA-4081, ST-4081)".[1][2] What each artefact is:

| artefact (in the archive) | what it is | what it is good for |
|---|---|---|
| `4081.ORG/DWG/4081GATE.DOC` | Atari's own gate-count list: 46 cell types, quantity, transistors each, `Total: 15394 transistors` | the size and the cell mix — the "how big is it" answer |
| `4081.ORG/PIN/TA4081.PIN` | P-CAD `PINLIST` netlist of the TA revision: 1,615 components, 1,508 nets, 324 named nets | the cell census, the internal net vocabulary, and which clock each storage cell hangs on |
| `4081/PIN/4081.PIN` | a second, drawing-level netlist in the schematic's own symbol library: 1,209 components, 1,174 nets, 505 named nets | what the *drawings* show — e.g. the ten `MX41` address muxes |
| `4081/DWG/pdf/ST4081S.PDF` | the 33-sheet schematic set (Nov 1988) + a 14-sheet addendum (Oct 1988) + two April 1988 timing sheets; searchable text layer | block titles, net names per sheet, the printed decode table, the timing charts |
| `TXL/DWG/SMOD.TXT` | **"Sound Modifications to the ST MCU/GLUE Combo Chip", 4 April 1988, to Doug Renn, cc Shivji and Tittler** | the sound channel's specification, in prose (§4) |
| `4081/SIM/*`, `TXL/SIM/*` | simulator link files, `.NLR/.NOJ/.LTR` binaries, test patterns, fault-coverage output | the fact that this design was *simulated* before tape-out — and, unread, a limit |
| `MACRO/MACRO.SIL`, `MORECELL.SIL` | the cell library's macro definitions: `FF2 Q XQ C D XC XR`, `L1 Q XQ C D XC`, `MF1 = TS1 + FF1` | cell semantics, stated by the designers |

Two cautions that the rest of this document obeys[1].
**First: two netlists, two representations.** `TA4081.PIN` counts logic cells (`NA2`, `NO2`, `FF2`…), `4081.PIN` counts drawing symbols (`MX41`, `AA4`, `LLR`…); they are different revisions in different libraries and their numbers must never be added or compared without saying which is which.
**Second: the netlist is a partial export.** Its sheet references run from 2 to 29 of a 33-sheet set, and the data bus in it stops at `D9`; the last sheets — the ADC and the final refresh-counter drawings — are not in it.
It is a census, not a complete connectivity model, and nothing below claims a connection on its strength alone.

### 2.3 The reconstruction is a transcription, not a second witness

`inferred` `gyurco/gstmcu` states in its header that it is a model "based on the schematics recovered by Christian Zietz", and the MiSTer core's `rtl/gstmcu/` is the same author's code in another packaging.[9] Where the RTL and the PDF agree, that is a transcription agreeing with its source.
The genuinely independent evidence for behaviour is: the memo (intent), our own board (measurement), and the two vendored emulators (models)[1].
The RTL remains the most convenient *reading* of the design, and §5 uses it as such[1].

### 2.4 Emulators answer a different question

`inferred` Hatari, Steem SSE and MAME model this chip well enough to run software, which makes them a checklist of what must be explained — never evidence of silicon[1].
Where an emulator encodes a number nobody can source (Hatari's 63-bus-cycle non-HOG blitter window, its `$FF8001` bank-size table, Steem's 4-word sound FIFO) it is recording behaviour that a test can confirm and a drawing cannot[1].

### 2.5 One quirk is hardware-confirmed

`documented` The STE's VSYNC counter can reload a **second** time when the video mode registers are written in the VBL handler; the RTL author quotes the Level 16 fullscreen demo as the confirmation on a real STE, and deliberately keeps the ST path single-reload[1].
That is the strongest class of evidence available without a decap, and it is the exception in this study, not the rule[1].

---

## 3. The chip the drawings show

### 3.1 Size, counted

`documented` The gate list, transcribed from `4081GATE.DOC` (abridged to the cells that matter to structure; the file has 50 rows and a stated total):[2]

| cell | qty | T each | total | what it is (per `MACRO.SIL`)[2] |
|---|---|---|---|---|
| `I1` | 298 | 2 | 596 | inverter |
| `I2`/`I3`/`I4`/`I5`/`I6`/`I8`/`I9`/`I10`/`I16` | 255 | 4–32 | 1,692 | inverters of increasing drive |
| `NA2`/`NA3`/`NA4`/`NA5`/`NA6`/`NA8`/`NA9` | 339 | 4–18 | 2,132 | NAND, 2 to 9 inputs |
| `NO2`/`NO3`/`NO4` | 34 | 4–8 | 146 | NOR |
| `CB1`/`CBMS`/`CBN`/`NCBMS`/`NLCB1`/`NLCBN` | 87 | 32–44 | 3,364 | complex gates (AND-OR-invert family) |
| `TS1`/`TS3`/`TS4` | 168 | 8–22 | 1,568 | tristate buffers — the bus drivers |
| `SAB` | 87 | 6 | 522 | sense amplifier bit (pins `Q XQ U XU`) |
| `FF1`–`FF5` | 73 | 20–24 | 1,676 | flip-flops (`C`, `D`, `XC`, `XR`) |
| `L1`/`L2`/`L4` | 88 | 10–12 | 956 | transparent latches |
| `MF2` | 17 | 28 | 476 | master flip-flop = `TS1` + `FF1` macro |
| `RLC0`–`RLC4` | 21 | 60–90 | 1,594 | register latch cells (largest cell in the library) |
| `LT2` | 3 | 32 | 96 | latch with tristate output |
| `XO` | 3 | 10 | 30 | XOR |
| `IP1`/`IP2`/`IP5`/`NTR` | 28/3/26/15 | 2–14 | 340 | input pads and bias cells |
| **Total** | **1,549** | | **15,394** | of which pads, gates, latches, flops — no memory of any kind |

`derived` The arithmetic checks against the file's own total: quantity × transistors summed over the 50 rows is exactly 15,394[2].
`derived` Inverters alone are 596 transistors (3.9 %), and 339 NANDs of two to nine inputs plus 87 complex gates means the control of this chip is a two-level random-logic net with no regular structure to exploit[2].
Compare the 68000, where 32,096 bits of control store did the same job for a much larger machine.

### 3.2 No microcode, no PLA, no table of any kind

This is the sharpest structural difference from the reference case, and it is settled by absence rather than by argument: **the cell library contains no ROM cell, no PLA cell, no RAM cell** — `documented`, from all 50 rows of the gate list and from the macro definitions in `MACRO.SIL`/`MORECELL.SIL`.[2]

| | 68000 (`research/m68000-silicon-implementation.md`) | GSTMCU |
|---|---|---|
| control | two control stores, 32,096 bits | none — gates only |
| instruction decode | three PLAs on the instruction register | none — address decode gates |
| "counters" | the address adder under microcode control | five dedicated counters |
| storage | 21 × 32-bit registers + 10 × 16-bit latches | 68–73 FFs + ~90 latches, all single-bit |
| size | ~68,000 transistors | 15,394 |

`derived` The consequence for anyone hoping to emulate this chip from a "control word" is that there is nothing to dump: the chip's behaviour **is** its wiring, which is why the drawings and the netlist are the only route to it, and why every published model of it is somebody's reading of those drawings[2].

### 3.3 Clocking: one macro cell, eight phases

`documented` Sheet 7 is the clock sheet: `ITEST`, `M2CLOCK`, `K500CLK`, `KHZ500`, `ICLK8B`, and the pads that carry `CLK8`/`CLK4`/`CLK2`/`KHZ500` out to the board (our inventory has pins 3–6 for exactly those four)[2].
The division 16 MHz → 8 → 4 → 2 MHz and 500 kHz is drawn there, and the netlist shows `MHZ16`, `MHZ8`, `MHZ4`, `MHZ2`, `K500CLK`, `KHZ500`, `C500`, `VERTCLK`, `REFCLK`, `VIDCLK`, `ICLK8`, `M2CLOCK` as real nets[2].

`documented` The eight-phase generator is **one macro cell**: a single instance named `TGEN`, eighteen pins, taking `CLK`, `CLK4`, `/RES`, `/ITEST` and `LCYCSEL` and producing `/TIME0`, `/TIME1`, `/TIME2`, `/TIME4` plus internal phase nets[2].
That is structurally important: the phase ring is hand-designed and hard-macroed, not a counter built out of library cells, and its internal behaviour is therefore *not* visible in either netlist[2].

`documented` Sheet 48–49, "ST TIMING" by John Horton (April 1988), are the two charts that define the slot grid, and they are the design's own statement of what the chip does with those phases:

| chart row (sheet 49) | meaning |
|---|---|
| `MHZ16` 0–15, `MHZ8` 0–7 | the two clock rings the phases are decoded from |
| `CYCSEL`, `CPUCYC`, `VIDCLK`, `DCYC`, `SLOAD` | the slot strobes |
| `RAS`, `CAS` | the two DRAM strobes, one per slot class |
| `REFRESH` / `CPU` / `SOUND` / `VIDEO` | the four owner lanes the mux table selects between |
| `XSRQ`, `HDE1` | the request and the window that gate sound and video |

`derived` So the "arbitration" is a decode of a phase counter — Cwik's "no priority of any kind between internal and external" is right at this level — and every finer-grained claim in §5 is about which client equation feeds the mux, not about who outranks whom[2].

### 3.4 What counts, and on which clock

`documented` The sheet titles are the inventory: `DRAM REFRESH COUNTER` (sheets 11, 31, 33), `DMA ADDRESS COUNTER` (16), `SOUND ADDRESS COUNTER` (17), `Paddle Registers` (32), and the video base/counter registers (4, 12, 15)[2].
The netlist supplies the clocks: `REFCLK` and `VIDCLK` each drive only a handful of gates and are *enables*, `M2CLOCK` drives 28 storage cells (the largest single clock domain, 15 × `FF1` + 9 × `FF2` + 3 × `FF4` + 1 × `LT2`), and `ICLK8`/`/ICLK8` drive another 10[2].

| counter | clock / enable | width | evidence |
|---|---|---|---|
| refresh row | `K500CLK` (500 kHz) into sheet 31's chain | 10 lines leave the chip as `MAL0`–`MAL9` per our own pin inventory; the STE population is 512 rows per bank | `documented` (design + `board-inventory.md` §3.4b); mapping of 10 bits onto 512 rows `unknown` |
| video address | advances once per 500 ns cycle while the display window is open | 24 bits across `VIDBH/VIDBM/VIDBL` + `VCOUNT` | `documented` (sheets 4, 12, 15) |
| sound address | `SNDCLK` = the granted slot | 21 bits (`SFTH/SFTM/SFTL`) | `documented` (sheet 17 + memo §4) |
| disk DMA address | `DMADIR`/`RDY` handshake with U300 | 24 bits (`DMAH/DMAM/DMAL`) | `documented` (sheet 16) |
| paddle | `PADC`, generated on the refresh sheet and used on the paddle sheet | four registers (`PAD0X/PAD0Y/PAD1X/PAD1Y`), one byte each at `$FF9211/13/15/17` in the emulator map | `documented` structure, `inferred` width, `unknown` law (§6.4) |

`documented` The paddle block is worth a sentence because it is the one place where the chip is *analog*: four registers built from `LLR` latches and `TB2` cells with shift registers `SHR01/03/05/12`, and a `PADC` clock that sheet 31 generates next to the refresh counter[2].
It is a counting ADC — the input charges something, the counter runs, and the caught value is the reading — not a successive-approximation converter, which is why its conversion time depends on the analog value and why emulators approximate it[2].

### 3.5 The register bank, and how it is read

`documented` The state registers are `RLC` cells (60–90 transistors, 21 instances) read through `SAB` sense amplifiers (87 instances, pins `Q XQ U XU` — two differential bit-line pairs, i.e. a **two-port** read)[2].
That is the same organising idea as the 68000's register file: bits sit on a pair of vertical lines and a sense amp picks one off[2].
`derived` A two-port register bank is what makes the design's characteristic move possible — the CPU writes a register through one port while the fetch machinery reads it through the other in the same slot[2].

`documented` The register names are the charts and the netlist: `RCONFIG`/`WCONFIG` with `CMAP`/`CPAL`/ `RMAP` (the MMU config and the address mapping), `RVIDBH/BL/BM` + `WVIDBH/BL/BM` (video base, with the read and write nets drawn separately — the readback mirrors that make `$FF8201` readable), `RLOC*`/`WLOC*` (line offset), `RHOFF`/`WHOFF` (fine scroll), `SNDCS`/`SNDIR`/`FF8XXX` (the sound page), `RS2`/`RS3` (reserved), `PADSEL`/`PENSEL`/`BUTSEL`/`JOYSEL` (the controller registers)[2].

### 3.6 The address path: ten muxes and a printed table

`documented` The drawing-level netlist holds exactly **ten `MX41` cells** — 4:1 muxes — each selecting with `SADSEL` and `REF` and driving one `MADn` line (e.g. `PN037 → MAD1`)[2].
Ten muxes for ten address lines, one per DRAM multiplexed address bit, and the fourth input comes from the phase decode[2].
`derived` That is why the chip can change owner of the address bus in half a clock: there is no address computation to switch, only a select line[2].

`documented` And the designer printed the truth table on the same sheet, "MEMORY ADDR MUX 1 OF 3" (sheet 23):

```
   X  X  1  1   REF          (IXDMAB SADSEL REF ADDRSELB)
   X  1  0  1   SOUND
   X  0  0  1   VIDEO
   1  X  X  0   CPU
   0  X  X  0   DMA
```

Read exactly: `ADDRSELB = 1` is the internal slot, and inside it `REF = 1` wins over `SADSEL`; `ADDRSELB = 0` is the external slot and there `IXDMAB = 1` selects the CPU, `0` the DMA controller.
Nothing in the table ranks internal against external — that is the phase[2].
§5.1 resolves what this table does *not* say[2].

### 3.7 Interrupts, sync, and four nets worth naming

`documented` The interrupt combiner is on-chip: `IPL0`–`IPL2` are outputs, and the inputs seen by the netlist are `MFPINT` (the MFP's single `IRQ`), `VINT`, `HINT`, `DISKINT`, `FDINT`, `HDINT`, `EINT3/5/7`, plus `BINT` — the blitter's interrupt, on pin 60 of the board symbol, whose use in this chip nobody has explained; Info-Coach records the same puzzle.[6] The 74LS148 priority encoder that the ST needed externally is integrated here.[6]

Four names repay care:

- **`INTERLACE`** is a real net, an output of an `XO` (XOR) gate in the vertical-sync block (sheets 26, 28).
  The reconstruction hard-ties `interlace = 0`; that is a model limitation, not a hardware fact. What the net
  drives is `unknown` from the text layer[2].
- **`OVERLAP`** (sheet 2) and **`NOSCROLL`/`SCRLSEL`** (sheets 10, 29) belong to the sync/scroll block, which is
  where the STE's `$FF820A` overlap bit and `$FF8264/65` fine scroll live[2].
- **`UDEN`**, **`VOS`**, **`DEV`/`DMA`/`RAM`** are internal-only nets; `VOS` appears in the RTL's CAS equations and
  `UDEN` does not appear in any published model at all — a candidate for a labelled unknown rather than a guess[2].
- **`TEST`/`ITEST`** are a test-mode pair on the pads (pin 74 `TEST`, and `ITEST` gating many cells in the
  netlist). What the mode does is `unknown`; nothing in the archive documents it[2].

---

## 4. The sound-DMA channel in the designer's own words

`documented` The April 1988 memo is short enough to be decisive.[2] Quoting the parts that settle arguments:

> "Access to memory can occur during the video/refresh time slots falling in the horizontal blanking interval.
> Unused time slots will be RAS only memory refreshes. During the active portion of the horizontal line, video
> refreshes will occur as in the present design. The modified design distinguishes between horizontal blanking
> and composite blanking. Sound and RAS only refreshes take place only during horizontal blanking. In the
> vertical blanking interval when horizontal blanking is not active, memory will be idle."

> "NOTE: Care should be taken that XSRQ is not held low indefinately since normal refresh takes place only when
> XSRQ is high."

> "The frame base and top registers can be written at any time. Their values only have effect at frame
> boundaries. A frame boundary occurs when the sound address counter value equals the value in the frame top
> register. When SCNTL bit 0 is low (sound off), the logic is held in a frame boundary condition so that values
> written to the frame base and top registers before sound is enabled are effective immediately."

And the register map it states, which is the one the STE kept:

| address | register | R/W | semantics (memo) |
|---|---|---|---|
| `$FF8901` | `SCNTL` sound control | R/W | bit 0 enable (0 = disabled, reset state); bit 1 repeat-at-frame-end (1 = repeat); bits 2–3 reserved; 4–7 unused |
| `$FF8903/05/07` | frame base high/mid/low | R/W | first word of the data; takes effect at the next frame boundary |
| `$FF8909/0B/0D` | frame count high/mid/low | **read only** | "the current value of the sound address counter" |
| `$FF890F/11/13` | frame top high/mid/low | R/W | first free word after the data; a boundary fires when the counter equals it |

| pin | name | memo |
|---|---|---|
| 3 new pins | `XSRQ` | sound refresh request, active low; "must be low for sound refresh cycles to occur" |
| | `SLOADB` | "the sound port should latch the memory data on the rising edge" |
| | `SINT` | "go high for 480ns following the detection of a frame boundary" |

`derived` Four consequences for our core, each of which our current notes state less precisely:

1. **Refresh is gated by `XSRQ`, not by a priority.** The memo's parenthetical is a warning to hardware designers; in the chip it means the *only* condition under which a RAS-only refresh happens is "blanking, no sound request".
That is the owner equation, stated by its author eight months before the sheets were drawn[2].
2. **A frame boundary is an equality against the counter, not a counter expiry.** The comparison is `count == top`, so a `top` written mid-frame cannot move the boundary of the frame in progress — the memo says so explicitly.
3. **The counter is readable.** `$FF8909/0B/0D` exposes it, which gives a test-ROM a way to watch the sound address walk without a logic analyser — the single cheapest T1 measurement available on this chip.
4. **`SINT` is 480 ns wide**, i.e. under two 8 MHz clocks, and the memo warns that the first fetch of the next frame can happen while it is still high.
A model that raises `SINT` for a whole slot already costs the difference on the first fetch of every frame[2].

`unknown` What the memo does **not** say, and neither do the sheets in the text layer: the exact slot in which the sound address counter increments relative to `SLOADB`, and whether the counter increments on grant or on load[2].
The RTL picks "clocked when the slot is granted"; the memo's `SLOADB` wording ("latch on the rising edge") is consistent with the fetched word arriving in the same slot[2].
§7.3 is the test[2].

---

## 5. Conflicts in the record, adjudicated

**1. "Refresh preempts video" vs "the arms cannot meet".** Both readings exist in our own notes, ten days apart, and both are defensible from the same sheet.
`documented` The mux table says `REF = 1` selects refresh whatever `SADSEL` and whatever the video state; the memo says refresh uses *unused* blanking slots and that "during the active portion of the horizontal line, video refreshes will occur as in the present design"[2].
The resolution is that the table is a **decode** and the memo is a **scheduling rule**: precedence in a mux is not a priority between clients, because the refresh request itself is only generated in horizontal blanking.
The correction of 2026-11-09 (that `REF`/`SNDEN`/`hde1` are derived from one another so exactly one arm is live) and the correction of 2026-11-01 (that the table encodes precedence) are therefore both right about their own artefact[2].
What would falsify this: a scope trace showing a CAS-less RAS cycle inside the display window.
That is §7.1.

**2. 15,394 or 15,074 transistors?** `derived` Both are from Atari[2].
The gate list totals 15,394 for 1,615 cells; multiplying the *netlist's* quantities by the gate list's per-cell transistor counts gives 15,074, and the difference is not an error but a revision: the netlist has 37 `L1` against the list's 50, 35 `FF2` against 39, 300 `I1` against 298, and it contains cells the list does not (`OB*` output pads, `LHR`, `TGEN`)[2].
Quote the gate list's figure as the chip's size and the netlist's census as the *other* revision's[2].

**3. 68 or 73 flip-flops?** Same cause, and the honest answer is "two revisions, both Atari's": 68 in `TA4081.PIN`, 73 in `4081GATE.DOC`[2].
No emulator should ever quote either as "the chip has N flip-flops" without naming the revision[2].

**4. C300589 or C302183?** `documented` Two part numbers exist: C300589 is the GSTMCU as fitted to this board with a discrete blitter (U101); **C302183** is the same chip with the blitter integrated, fitted from about 1991 and in the Mega STE, and the archive's catalogue notes the combined MMU+GLUE+blitter chip as the Mega STE's.[1][7][8] The recovered design is the *first* kind — nothing in `ST-4081` contains blitter logic, and the blitter exists as its own designs (`TA-4021`, `ST-4051`, `ST-4082`) in the same archive.[1]

**5. `interlace = 0`.** `documented` The RTL hard-ties interlace off with a comment asking whether it is useful; the design has a real `INTERLACE` net from an XOR in the vertical-sync block[2].
The RTL is wrong as a description of the chip, and right as a description of what an emulator needs for software that never enables interlace[2].
`unknown` what the net drives; do not model it from this[2].

**6. The blitter's non-HOG window: 63 or 64 bus cycles?** `documented` Hatari's `blitter.c` says 63 and records that the return to the CPU was verified on a real STE; the MiSTer/Cwik `stBlitter.sv` counts 64 `AS` falling edges from `BUSY` and calls it a "buglet".
These disagree by one cycle, and the difference is probably where `BUSY` is sampled relative to the first `AS` — exactly the kind of off-by-one that only a test settles.
It is the Blitter's study, not this one, but the conflict belongs in the record now that both sources are cited.

**7. `$FF8260` exists twice.** `documented` The design has both write and read nets for the video-base registers and the mode register lives in the sync/video block *and* is decoded in the Shifter; Hatari encodes the pair as `Video_ResGlueShifter` (`$FF8260`) and `Video_ResShifter` (`$FF8261`)[2].
Our own `design/subsystems-spec.md` carries this as correction 4, and the design agrees: two storage sites, not one register mirrored.[2]

**8. "The MMU translates addresses."** `derived` On this machine it mostly does not: `$FF8001` sets two bank sizes (128 KB / 512 KB / 2 MB each), and on the STE bank 1 always takes bank 0's size — so the mapping is a *contiguity* rule, not a page table[2].
The design's sheet 18 ("DRAM SIZE/CONFIGURATION DECODES") and its `K64G`/`K256G`/`M1G` nets are the hardware side of that; Hatari encodes the same three sizes and the same reserved fourth value[2].
Any claim of fine-grained MMU behaviour is an STF-with-non-IMP-MMU story, not this chip[2].

---

## 6. What the emulator needs

The open questions that survive, mapped to our own documents and ranked by what they cost us when wrong.
Tiers per the handoff: T1 hardware-captured, T2 hardware-corrected, T3 emulator-derived.

| # | question in our core | the claim that answers it | tier | home |
|---|---|---|---|---|
| 1 | Is refresh really "filler, never preemptor"? (`design/system-control-unit.md` §4) | yes — memo §4.1 + the mux decode; refresh requires blanking **and** `XSRQ` high | T2 (memo, intent) → T1 by §7.1 | SCU §4 |
| 2 | Does the sound counter increment on grant or on load? | RTL: on grant; memo: `SLOADB` rising edge latches data. Unresolved ordering | T3 | `design/crossings.md` (§2 refills) |
| 3 | Is `SINT` a slot-wide level or a 480 ns pulse? | 480 ns pulse, and the next frame's first fetch may overlap it | T2 (memo) | Sound's frame-end event |
| 4 | Does a mid-frame `top` write move the current boundary? | no — the boundary is `count == top`, evaluated continuously | T2 (memo) | Sound |
| 5 | What is the refresh period and row order? | `derived` 7.8 µs for 512 rows in 4 ms; counter clocked at 500 kHz; 10 `MAL` lines vs 512 rows unexplained | T3 | SCU §11.1 |
| 6 | Is the paddle a counting ADC, and what is its law? | counting, clocked from the same 500 kHz timebase; law unknown | unknown | Controllers |
| 7 | Are 313 lines × 512 clocks right, and where is the `DE` edge? | design has `HDE/LPHDE/VDE/HBLANK`, `SYNCSEL`, `MDE0/MDE1`; the reconstruction gives `ivsync_set` 508/510 | T3 | Video |
| 8 | Does the CPU really get every second slot? | yes — `ADDRSELB` is a phase, and CPU-outranks-DMA only *inside* the external slot | T2 (design table) | SCU §3 |
| 9 | Is `$FF8001` write-only, mirrored, or aliased? | the design carries separate read and write nets for these registers; Hatari models readback, and `$FF8000`/`$FF8002-0F` are harmless | T3 | SCU registers |
| 10 | What is `BINT` for? | `unknown` — a pin, a net name, and no published explanation | unknown | Blitter study |

Two of these (2 and 10) are candidates for a labelled `unknown` in the model rather than a guessed rule; the handoff's stopping rule is explicit that an honest `unknown` plus the experiment beats a plausible constant.

---

## 7. The experiments that would settle the rest

Six tests, in the order that buys the most certainty per evening.
Each is a TOS program (or a bootable `$4E71`-filler ROM) run on the real 1040STE, with a scope on the named pin or a read-back through a named register; each has a pass criterion that decides between the two candidate models in this document.

1. **Refresh during the display window.** Scope `RAS0` and `CAS0L` for one full line while `DE` is high and sound is off, with the video base set so that the fetch never idles.
*Pass:* no CAS-less RAS cycle while `DE` is high → §5.1 resolved in favour of the memo's scheduling rule; any CAS-less RAS inside the window falsifies it and makes the mux table a true priority.
2. **Refresh versus `XSRQ`.** Drive `XSRQ` low indefinitely (a modified board, or a paddle port that holds the line) and scope `RAS0`.
*Pass:* refresh stops — the memo's warning, and the only direct evidence for the owner equation's third arm.
3. **Sound counter ordering.** Play a single frame with a known base/top; poll `$FF8909/0B/0D` in a tight loop and, in a second run, scope `SLOAD`.
*Pass:* the counter value sampled between two refills advances by exactly one fetch, and the increment is visible before or after the `SLOAD` edge — which settles §6.2 either way.
4. **`SINT` width.** Scope pin 105 with a frame of one word.
*Pass:* a pulse under two clocks; measure it.
A model that holds the line for a slot fails this by inspection[2].
5. **Frame boundary with a mid-frame `top` write.** Set `top` = base + N words, start, then rewrite `top` at half the frame.
*Pass:* the frame still ends at the original `top` — the memo's "will not affect the detection of the current frame boundary".
6. **Paddle law.** With a known resistor on `PAD0X`, read `$FF9211` repeatedly at two fixed video modes.
*Pass:* the readings are proportional to the resistance and inversely proportional to the mode's line rate if the counter is free-running on 500 kHz; a fixed count per read means the counter is started by the write instead.
Either answer replaces an `unknown` with a rule[2].

None of the six needs a die, and all six are cheaper than one decap attempt by orders of magnitude.

---

## 8. What is still unknown

- **The shipped mask.** These are *designs*, and the archive holds three revisions of them. Whether the part on
  our board is `ST-4081` or the `TXL` variant is not established; nothing in the design fixes which one shipped
  in C300589-001. A decap answers this and the two revisions differ in cell count (68 vs 73 FFs), so the
  question is not academic[2].
- **The refresh counter's 10 bits against 512 rows**, and whether `MAL0–MAL9` are row addresses at all.
- **The ADC's conversion law and its timing.**
- **`BINT`'s function inside the GSTMCU**, and whether the C302183 variant's blitter logic reuses it.
- **The test mode** (`TEST` pin 74, `ITEST` gating dozens of cells) — undocumented here, unlike the 68000's VPA
  mode which at least has a 1985 paper[2].
- **Whether the sound FIFO is 4 words.** Nothing in the design archive states a depth; the figure we carry is
  `inferred` from Steem, and the reconstruction puts the word pipeline in the *Shifter*, not here — the two
  claims are about different sides of the same wire and both are currently unmeasured[2].

`inferred` What a decap would not settle: any of §6's timing questions except indirectly.
The right next buy for this machine, if anything is ever bought, is not a die but a logic analyser session — and §7 is the shopping list for it. No figure is drawn with this study: the design gives no die geometry, and a floorplan inferred from gate counts would be exactly the outrun-the-evidence failure the 68000 study warns about.

---

## 9. Licence position

The design documents are Atari's proprietary CAD, recovered and published by Christian Zietz; the archive's own terms are research use.[1][2] **Nothing from the archive is vendored into this repository** — the tables above are transcriptions of small excerpts (a gate-count list and a printed decode table), cited to the archive URL.
Following the owner's ruling of this session, these studies are repo/private research and each new study states its position; the 68000 study's 1983 *Byte* figure stays where it is on the same basis.
If any of this publishes, the tables must be redrawn from first principles or replaced with a citation-only reference to Zietz's site.

---

## Sources

[1] https://www.chzsoft.de/asic-web — Christian Zietz — recovered Atari ASIC designs (asic-web)
[2] https://www.chzsoft.de/asic-web/ASICs-new.zip — ASICs-new.zip — recovered Atari CAD archive (Zietz + fpgaarcade)
[3] https://mikrosk.github.io/decap — Miro Kropacek — Atari Falcon custom chips decap initiative
[4] https://siliconpr0n.org/archive/doku.php?do=search&id=atari — siliconpr0n archive — search result for 'atari'
[5] https://siliconpr0n.org/archive/doku.php?id=furrtek:atari:c301719-001 — siliconpr0n — furrtek:atari:c301719-001 (Lynx Mikey)
[6] http://info-coach.fr/atari/hardware/STE-HW.php — Info-Coach — Atari STE hardware (GST MCU chapter)
[7] https://www.atari-forum.com/viewtopic.php?t=42655 — Atari-Forum — GST MCU part numbers C300589 vs C302183
[8] https://www.atari-wiki.com/index.php?title=STE — Atari Wiki — STE (blitter combined with C302183)
[9] https://github.com/gyurco/gstmcu — gyurco/gstmcu — simulation model of the Atari STE GSTMCU
[10] https://www.exxosforum.co.uk/forum/viewtopic.php?t=1436 — exxos forum — microscopic chip analysis thread (ijor: whole ST chipset)
[12] http://visual6502.org/images — visual6502 — photographed-parts index (die shots)
