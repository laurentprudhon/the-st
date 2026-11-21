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
| What counts? | **Real counters, unlike the 68000.** Seven counting structures, each with its own clock net: the refresh row chain, the video address counter, the sound address counter, the DMA address counter, the paddle divider, and the pen's horizontal and vertical pair on sheet 33, read since this section was written. The chip has no datapath that can be made to count. | 3.4 |
| Clocking? | One 8-phase timing generator, drawn as a **single macro cell (`TGEN`)** from `TIME0`–`TIME7`, fed by a 16 MHz clock divided to 8/4/2 MHz and 500 kHz on sheet 7. Every slot decision in the chip is a decode of those phases. | 3.3 |
| How does the address mux arbitrate? | Ten 4:1 muxes (`MX41`), one per `MAD` line, with the designer's own decode table printed on the sheet: refresh → sound → video inside a slot, CPU → DMA outside it. | 3.6 |
| What does the emulator still not know? | The refresh *period* and row mapping, the ADC's conversion law, and the exact enabling of the sound/refresh scheduling. §7 ranks them; §8 gives the six tests that settle them. | 6, 7 |

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
The design is already machine-readable and complete enough to give area, cell census and connectivity; a decap would cost about the same as one Falcon chip in the 2025 initiative (≈$1,000 per part at the lab's estimate, $8,021 raised for three)[3] and would answer almost none of §7's open questions, all of which are *timing* questions that a board with a scope answers for free.
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
The RTL remains the most convenient *reading* of the design, and §6 uses it as such[1].

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
| "counters" | the address adder under microcode control | seven counting structures |
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

`documented` The sheet titles are the inventory, with the usual caveat that two of them lie: `DRAM REFRESH COUNTER` (sheets 11, 31, 33), `DMA ADDRESS COUNTER` (16), `SOUND ADDRESS COUNTER` (17), `Paddle Registers` (32), the pen's two position counters (33), and the video base/counter registers (4, 12, 15)[2].
`documented` The lie is worth naming because it misled this study for a day: **sheet 31's block is the paddle scan divider**, and the row counter the title promises is on sheet 11 (§3.4 below).[2]
The netlist supplies the clocks: `REFCLKB` clocks the refresh chain's ten stages (read at sheet 11) while `VIDCLK` is an enable, `M2CLOCK` drives 28 storage cells (the largest single clock domain, 15 × `FF1` + 9 × `FF2` + 3 × `FF4` + 1 × `LT2`), and `ICLK8`/`/ICLK8` drive another 10[2].

| counter | clock / enable | width | evidence |
|---|---|---|---|
| refresh row | ten stages on **sheet 11** carrying `REFCLKB`; that clock is a flop on **sheet 27** clocked by the 500 kHz timebase with `D = NAND(HCTR, LC)`, and `REFCLKB = NAND(SNDEN, REFCLK)` on sheet 20 | ten taps = 1 024 states against 512 rows per bank; **tap 1 is `RA9`**, tap 10 is `RA0` | `documented` (sheets 11 and 6 read; `RA9` read into the `MAD9` mux) |
| video address | advances once per 500 ns cycle while the display window is open | 24 bits across `VIDBH/VIDBM/VIDBL` + `VCOUNT` | `documented` (sheets 4, 12, 15) |
| sound address | `SNDCLK` = the granted slot | 21 bits (`SFTH/SFTM/SFTL`) | `documented` (sheet 17 + memo §4) |
| disk DMA address | `DMADIR`/`RDY` handshake with U300 | 24 bits (`DMAH/DMAM/DMAL`) | `documented` (sheet 16) |
| paddle | `PADC` — the 500 kHz timebase through sheet 31's own divider, whose reset `PADRST` is the chip's power-on reset | four registers (`PAD0X/PAD0Y/PAD1X/PAD1Y`), one byte each at `$FF9211/13/15/17` in the emulator map | `documented` structure and clock source, `inferred` width, `unknown` law (§7.6) |
| pen position | horizontal from `M2CLOCK` through `PX033`/`PX031`/`PX032`, vertical from `IHSYNC`+`VDE` through `PX064`–`PX066`; cleared from `IHSYNC` and `IVSYNC` respectively | two `NCBMS`/`NLCB` chains (`PX021`–`PX026`, `PX052`–`PX059`) captured into `MF2` banks (`PX010`–`PX017`, `PX038`–`PX046`) | `documented` (sheet 33, read cell by cell) |

`documented` The paddle block is worth a sentence because it is the one place where the chip is *analog*: four registers built from `LLR` latches and `TB2` tristate cells, read out through a 32-bit input port on sheet 32, and a `PADC` clock that sheet 31 generates[2].
`documented` Sheet 32 read by its owner is that port and nothing more: **32 bits of input register** feeding the data bus through four selectable 8-bit groups. `PADR` enters at the top left through an inverter (`PW003`), and the inverted form drives four `M03X2` gates (`PW002`, `PW005`, `PW009`, `PW012`) that also take `IA` and `IAB`; their outputs are the nets `SHR01`, `SHR03`, `SHR05` and `SHR12`, each inverted before it reaches the `TB2` selection, so one of the four bytes is enabled onto `TDB` at a time (pads `PW002`–`PW012`).[2]
`derived` Two corrections follow. `SHR01/03/05/12` are **select** signals, not shift registers — this study called them shift registers on the strength of the name alone, and they are gate outputs. And the register select touches the *read* path only: nothing on sheet 32 starts, stops or clears a conversion.
`documented` What sheet 31 actually draws, read off the sheet by its owner: `K500CLK` into the `C` pin of `DPR` `PV009`, then a chain of `NLCB1` and six `NLCBN` carry cells, giving eight taps printed 0–7, reset by `PADRST`, with one tap leaving the sheet as `PADC` towards sheet 32 (pads `PV001`–`PV017`).[2]
`derived` That is the **paddle** divider and nothing else — the pen has its own clocks on sheet 33 and does not see `PADC` at all — so `PADC` is the 500 kHz timebase divided down by these eight taps, which is what the owner reported as "directly derived from `K500CLK`".
`derived` The ten-tap chain that can be a row counter is on **sheet 11** instead: `NCBMS`, seven `NLCBN` and an `NLCB1`, with `CI`/`CO` carried stage to stage and the `Q` outputs printed 1…10, drawn beside the `WLOC*` line-offset registers and their `MX21` muxes (pads `PB001`–`PB010`).[2]
`derived` Ten taps is 1 024 states, and 512 rows per bank is half of that — the first reading in this study that makes ten address bits fit the row count instead of contradicting it.
`documented` The chain read at 600 dpi by its owner: **tap 1 is `RA9`** down to **tap 10 is `RA0`**, so the address register's least significant bit is the *last* stage (`PB010`) and the row count advances from there; every tap leaves the sheet as a bare `RA` wire with no inverter, `NA2` or `MX21` in the way, and no tap is gated differently from its neighbours.[2]
`documented` All ten stages carry `REFCLKB` — the net sheet 6 also brings in — and the only reset is `PORB`: no load, no preset, no per-line clear, so the counter free-runs from its power-on state.[2]
`documented` And **sheet 27** — not the clock sheet — shows where that clock comes from, which retires the "it is a divider off the video clock" assumption: `K500CLK` is buffered locally (`PR026`), a `NAND` (`PR027`) combines two counter bits, `HCTR` and `LC`, into `XY2`, a second `NAND` (`PR028`) mixes `XY2` with `K500CLK`, and the result clocks a `DPR` (`PR032`) whose output carries `REFCLK` (pads `PR025`–`PR030`).[2]
`documented` `HCTR` itself is traced on the same crop: a ÷2 prescaler (`PQ019`, toggling on `M2CLOCK`, its `Q` named `HSCB`) followed by five toggle stages (`PQ020`–`PQ024`), with `HCTR` taken at the last — six stages, so `HCTR = M2CLOCK ÷ 64`.[2]
`documented` And `M2CLOCK` is the **4 MHz** node, buffered out of the divider rather than divided again: sheet 7's chain is a plain `Q̄→D` toggle ladder (`P7001` → `MHZ8`, `P7002` → `MHZ4`, `P7003` → `MHZ2`, two more stages to `KHZ500`), and `M2CLOCK` reads the same 4 MHz node as `MHZ4` through its own buffer, with `M2CLOCKB` its inversion one cell later.[2]
`derived` So `HCTR` is 62.5 kHz with a 16 µs period and no duty skew — the clock tree is one ÷2 ladder, 4 MHz → 2 → 1 → 500 kHz, with `M2CLOCK` and `K500CLK` as two taps of the same oscillator.
`derived` Because `PR027` is a bare two-input `NAND` of two raw counter bits with no decode chain in front of it, the duty is structural rather than a designed pulse width: the decode is high except when both bits are high, so `REFCLK` follows `HCTR`'s own square wave on the lines where `LC` is high and stays static where `LC` is low.
`derived` At 16 µs that is **four decode windows per 64 µs line**, sampled on the 2 µs grid, and 512 rows in 128 lines — **8.19 ms** per full row walk, which is the refresh interval a 512-row DRAM actually needs. The arithmetic closing against the part is the corroboration that the reading is right, and it is the reason to prefer `M2CLOCK = 4 MHz` over the 2 MHz reading, which would double the walk to 16.4 ms and be too slow.
`documented` `LC` is not a raw bit of the line counter either: it is a bit of the register on **sheet 7** (`P7048`–`P7054`, beside `TIME0B`/`1B`/`2B` and `LCYCLEL`), strobed by a `NAND` decode of `TIME0B`/`1B`/`2B`/`4B`/`5B` (`P7064`), so it latches once per timing-cycle event and not once per pixel; which bit of that register (4 or 5) could not be settled.[2]
`derived` A binary counter bit is 50 % duty whatever its position, so `NAND(HCTR, LC)` is true on half the lines and offers **two refresh windows per line on average**, four on the lines where `LC` is high.
`derived` That makes the walk 2 × 512 / 64 µs… i.e. 256 lines for 512 rows, **16.38 ms** — twice the interval a 512-row 8 ms part needs, and exactly right for 256 rows (8.19 ms) or for a 1024-row part at 16 ms.
`documented` And the row count is now sourced from the board rather than from our inventory: a stock 1040STE carries no motherboard DRAM at all — it has four 30-pin SIMM sockets (`U600`–`U603`) holding 4 × 256 KB, each SIMM built from **eight 256K×1 devices of the 41256 family** (`HYB41256-12`, `M5M4256P`, `TMS4256`, `HM50256`…), nine address bits split 9/9, so **512 rows per bank** (`design/board-inventory.md` §3.4b, now sourced).
`derived` That fixes the target: the 41256 family refreshes on 256 cycles per 4 ms, so a single-row RAS-only walk of 512 rows has to complete inside **8 ms**, i.e. one row every 15.6 µs.
`derived` And the gate offers no polarity rescue: `PR027` takes `LC[20]` and `HCTR[26]` **bare** — no inverter, no bubble, twice confirmed on a 900 dpi crop — so the decode really is `NAND(HCTR, LC)`, true except when both inputs are high.
`derived` The walk is therefore **bounded, not computed**: `HCTR` gives four windows per line, and those windows only count on the lines where `LC` is high. `LC` permissive on every line → 8.19 ms, just inside a 41256's 8 ms; `LC` a 50 % bit → 16.4 ms; `LC` asserting once in 16 or 32 lines → 131–262 ms.
`unknown` Only the first of those is legal for the part, and only the last is what a sparse bit of a once-per-cycle register would give — so **one of the two `LC` readings is wrong** and the sheets disagree rather than the arithmetic. The resolving crop is the `D`-pin daisy-chain of `P7048`–`P7054` on sheet 7 (which position `LC` occupies, hence whether it is permissive or sparse); the resolving measurement is §8.1, which counts RAS-only cycles per line and reads the answer off the board in one trace.
`documented` The polarity is closed by the same read further along: on sheet 20 the extractor's final gate is a `NAND` (`PK030`) taking `REFCLK` and `SNDEN` with its output on `REFCLKB`, so `REFCLKB = NAND(SNDEN, REFCLK)` — an active-low, strobe-qualified copy that parks high while `SNDEN` is low.[2]
`derived` That park is the hardware form of the memo's warning: with a sound request pending, the refresh clock is held off, which is the same policy the `REFB`/`SNDEN` gate group expresses one layer up (§5.1).
`derived` That flop has no feedback path into its own `D`, so it is not a toggle divider: `REFCLK` is a **registered decode**, transitioning only on `K500CLK` edges, which puts every refresh step on a 2 µs grid and ties the refresh request to the line structure (`HCTR`, `LC`) rather than to a free-running counter.
`derived` The ceiling is therefore fixed and the rate is not: at most 32 steps per 64 µs line, a full 1 024-step walk in 2.05 ms, and 512 rows in half of that — but how many `REFCLK` edges a line actually carries depends on the duty of `NAND(HCTR, LC)`, which no sheet read so far states.
`unknown` One of the owner's reading passes gave the ascending order instead, and the balance of two careful passes wins only because it agrees with the input end of a carry chain; a second eyeball on the physical sheet, or a scope on `RA0` against the line clock, is what would make the order beyond argument.
`documented` And there is no `MAL` net anywhere in the archive: the chip's symbol carries `MAD0`–`MAD9` (the multiplexed DRAM address, §3.6), and the refresh counter reaches them as `RA0`–`RA9` through the ten `MX41` muxes (read at `RA9`, sheet 25).[2]
`derived` So `design/board-inventory.md`'s "`MAL0–MAL9`, refresh counter" pins are almost certainly the same ten lines under another name — the archive has no refresh pin at all — and the inventory's open item can be re-checked against the service manual's symbol rather than searched for in the design.

`documented` Sheet 33 is the **pen**, and it is the same shape twice: a horizontal section (`NCBMS`/`NLCB` chain `PX021`–`PX026` with `DPR` `PX027`, capture bank `MF2` `PX010`–`PX017`) clocked from `M2CLOCK` through `PX033`/`PX031`/`PX032` and cleared from `IHSYNC` through `PX029`/`PX030`/`PX028`; and a vertical section (chain `PX052`–`PX059`, `DPR` `PX060`, capture bank `MF2` `PX038`–`PX046`) clocked from the `IHSYNC`/`VDE` logic at `PX064`–`PX066` and cleared from `IVSYNC` at `PX061`–`PX063`.[2]
`documented` The `MF2` stages are capture latches, not counters: their `D` inputs take the counters' bits and their clock comes from the pen signal's own conditioning chain — `PAD` … `PEN` → `PX036` → `PX034` → `PX035` → `PX037` → capture.[2]
`derived` So the pen is a device that **latches the beam**: both counters free-run against the beam position, and the pen's own edge freezes them at the instant it arrives — which is why a second read without a second event returns the same value, and why our model must treat the pen as an event that samples video position rather than as a converter.
`documented` Read-out is the same pattern as the paddles: `PENR` and `IAB` into an `NA2X2` (`PX020`) whose output gates `PX019` to produce `SHS09`, selecting the stored group for the bus.[2]
`unknown` Five `DPR` stages sit at the top of sheet 33 (`PX001`–`PX005`) with a `TB2` pair and an output group numbered 8…1 and 0; they belong to neither pen counter and nothing on the sheet says what they hold.
It is a counting ADC — the input charges something, the counter runs, and the caught value is the reading — not a successive-approximation converter, which is why its conversion time depends on the analog value and why emulators approximate it[2].

### 3.5 The register bank, and how it is read

`documented` The state registers are `RLC` cells (60–90 transistors, 21 instances) read through `SAB` cells (87 instances); `inferred` the `SAB` is a sense amplifier bit — the library macro file in the same archive gives it the pins `Q XQ U XU`, two differential bit-line pairs, i.e. a two-port read.[2]
That is the same organising idea as the 68000's register file: bits sit on a pair of vertical lines and a sense amp picks one off[2].
`derived` A two-port register bank is what makes the design's characteristic move possible — the CPU writes a register through one port while the fetch machinery reads it through the other in the same slot[2].

`documented` The register names are the charts and the netlist: `RCONFIG`/`WCONFIG` with `CMAP`/`CPAL`/ `RMAP` (the MMU config and the address mapping), `RVIDBH/BL/BM` + `WVIDBH/BL/BM` (video base, with the read and write nets drawn separately — the readback mirrors that make `$FF8201` readable), `RLOC*`/`WLOC*` (line offset), `RHOFF`/`WHOFF` (fine scroll), `SNDCS`/`SNDIR`/`FF8XXX` (the sound page), `RS2`/`RS3` (reserved), `PADSEL`/`PENSEL`/`BUTSEL`/`JOYSEL` (the controller registers)[2].

### 3.6 The address path: ten muxes and a printed table

`documented` The drawing-level netlist holds exactly **ten `MX41` cells** — 4:1 muxes — each selecting with `SADSEL` and `REF` and driving one `MADn` line (e.g. `PN037 → MAD1`)[2].
`documented` Sheet 25 read by its owner gives one mux in full, and with it the naming the rest of this section uses: the `MAD9` mux takes `RA9` (sheet 11), `VA9` (sheet 11), `MA9` (sheet 17) and `A9` (sheet 19) as its four data inputs, selected by `REF`, `SADSEL`, `IXDMAB` and `ADDRSELB`.[2]
`derived` The four names are the four owners, and there are only four because two of the table's five lanes share an input: `RA` is the refresh address, `VA` the video address, `MA` the sound address (sheet 17 is the `SOUND ADDRESS COUNTER`), and `A` the bus address — which serves both CPU and DMA, because on an external slot the granted master *is* driving the bus. `IXDMAB` therefore selects between lanes that are already distinguishable in time, not between two wires.
`derived` That is also the join the study had been missing: `RA9` comes from **sheet 11**, the same sheet as the ten-tap `NCBMS`/`NLCB` chain, so the refresh row address is that counter and the ten `MAD` lines are its taps (one instance read; the other nine are read as symmetric).
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
§8.3 is the test[2].

---

## 5. The chip in operation

`documented` Five artefacts describe what this chip does with time, and this section uses them in this order: the two April 1988 timing charts (sheets 48–49), the strobe equations in the drawing-level netlist, the sound memo, the clock sheet (7) and the sync sheet (29).[2]
`inferred` The Verilog reconstruction agrees with most of what follows and is quoted wherever it adds a *reading* the drawings do not print; it is never the evidence for a fact.[9]
`derived` The ladder every number below uses, each rung checked against the one above it:

| unit | duration | where it comes from |
|---|---|---|
| master step | 31.25 ns | half a 16 MHz clock; sheet 48's grid carries 16 of them per 500 ns window |
| phase | 62.5 ns | one 16 MHz clock; the `TIME0`–`TIME7` decode's quantum |
| slot | 250 ns = 4 phases = 2 CPU clocks | one DRAM access; sheet 48 draws two `RAS` assertions per window |
| cycle | 500 ns = 2 slots = 8 phases = 4 CPU clocks | one 68000 bus cycle; sheet 48's whole width |
| line | 64 µs = 512 CPU clocks = 128 cycles = 128 internal + 128 external slots | the line arithmetic of `design/system-control-unit.md` §3 |
| frame | 313 lines = 160,256 clocks = 20.03 ms ≈ 49.9 Hz | 313 × 64 µs; PAL's 50 Hz less the line the counter eats |

`derived` Every number in the ladder is forced: 64 µs is 512 clocks at 8 MHz, the window the 68000's four-clock cycle divides exactly twice, and 313 × 512 × 125 ns = 20.032 ms, which is the 49.9 Hz every STE runs at.[2]

### 5.1 One slot (250 ns)

`documented` A window is two DRAM accesses of opposite class, and the class is a *level*, not a decision: `ADDRSELB` is high for the internal slot and `ADDRSEL` for the external one, and the design's own `RAS` equations gate the two halves of the window on exactly those levels.
The netlist gives `RAS0` as the OR of two four-input terms (pads `PM004`/`PM010`, sheet 22), transcribed here as it reads:

```
RAS0 = ~(  TIME0  & ADDRSELB & (~REFB | (RAM1A & VOS))
        | ~TIME0  & ADDRSEL  &  RAM1A & ~RAMCYC )        [2]
```

`documented` The same reading holds for `RAS1` with `RAM2A` in place of `RAM1A`, and it fixes three things at once: the internal access is strobed by `TIME0` **high** and the external one by `TIME0` **low**; the two bank strobes are selected by the DRAM decode (`RAM1A`, `RAM2A`, sheet 18) rather than by the slot; and inside the internal half the address source is chosen by `REFB` and `VOS` before the mux routine of the table below is even consulted.[2]
`derived` So the internal slot precedes the external one in every window — the order is a property of `TIME0`'s polarity, and the chart's `VIDEO/REF` and `CPU` lanes are drawn in that order because the equations are.[2]

`documented` The column strobe trails the row strobe by exactly one phase, and unlike every other strobe on this chip it is not gated by the slot class:

```
CAS0L = ~( TIME2 & LCYCSELB & ... )   (pad PM002, sheet 22)        [2]
```

`documented` `LCYCSEL` is the line-cycle select: a latch in the clock generator (sheet 7) built from the phase chain, with `LCYCSELB` its complement and `CYCSEL` the cycle select beside it.[2]
`inferred` The reconstruction gives `lcycsel` as the `TIME7` pulse and `cycsel_en` as `time6 & ~time7`, i.e. the two selects that bracket a cycle's end.[9]
`derived` `RAS` at `TIME0` and `CAS` at `TIME2` is 62.5 ns of row-address time — one master clock, the DRAM's `tRCD` — and it is the single most load-bearing timing number in the design, because everything a program can measure about STE RAM waits on it.

`documented` The row/column select is `ROWB`: a macro `LLR` latch whose clock term carries `TIME0B` (pads `PL039`/`PL036`, sheet 21), i.e. a register that flips once per DRAM cycle, not a combinational decode of the phase.[2]
`documented` The write strobe is confined to the external half: `WE` is built from `TIME0B`, `ADDRSEL`, `CMPCYC`/`RAMCYCB` and the data-direction term `IRWB` (pads `PL014`/`PL012`, sheet 21), so the CPU's write window is bounded by phase 0 of the external slot at its start and by phase 1 at its end.[2]
`documented` `LATCH` is not a phase decode at all: it is the output of an `LLR` latch clocked by `CLKB` (pads `P7046`–`P7049`, sheet 7), so it is a registered level one 8 MHz clock wide, and the chart draws exactly that.[2]

`derived` A window as sheet 48 draws it, in the sheet's own `MHZ16` state numbers, read off the drawing by its owner. An independent measurement of the same sheet's vector geometry (transition x-positions located on the 16-state grid to ±0.5 %) agrees with every row below except `LATCH`, where a band one point too wide merged its two pulses into one — the reading here is the owner's, and it is the one to trust.

| row (sheet 48) | what the drawing shows |
|---|---|
| `RAS` | low from state 0 to 4 — asserted once per DRAM cycle, so twice in every 16-state window |
| `COL/ROW` | high from state 1 to 5, low from 5 to 9: it switches one state after `RAS` falls and returns before the cycle ends |
| `CAS` | low from state 10 to 14 in the `cpu` cycle, i.e. 2 to 6 relative to each `RAS`, and by period 8 in **every** cycle but the refresh one — the same window as `LATCH` |
| `DCYC` (video lane) | low from state 2 to 5; `SLOAD` on the `sound` lane, likewise 2 to 5 |
| `LATCH` | high from state 10 to 14, and with a period of 8 the same window in **every** cycle: one CPU clock, once per DRAM cycle |
| `TIME0`–`TIME7` | eight levels each a slot wide, rising one state apart, `TIME0` high over the internal slot and `TIME7` over the external one |

`derived` The state numbers are the chart's own `MHZ16` labels, and one state is 31.25 ns — the ladder above, confirmed by the sheet rather than imposed on it.
`derived` Two of these readings settle arguments. `COL/ROW` is **low** while `RAS` falls, and a DRAM cannot accept a column address at that edge, so low is the row address and the high stretch is the column address — the polarity a transitions-only reading cannot fix. And `LATCH`'s period of 8 states puts it *after* `RAS`, inside the access, coincident with that lane's own strobe; an address latch for the cycle would have to close before `RAS` falls, so `LATCH` is a capture at the end of an access, not a latch of the address at its start.

`documented` Sheet 49 is read the same way and answers a different question: it draws **five successive cycles**, and the lane labels are what the equations only imply. In the owner's reading of the sheet: `video` (`RAS` low 0–4, `DCYC` low 2–5), `cpu` (`RAS` low 8–12, `CAS` low 10–14), `sound` (`RAS` low 0–4 of the next window, `SLOAD` low 2–5), `cpu` again, then `refresh` (`RAS` low 0–4, with `DCYC`, `SLOAD` **and** `CAS` all held high).[2]
`derived` Three facts follow from that one line of the chart, and each of them was an assumption before: `CAS` is asserted in **every** cycle but the refresh one — the refresh cycle is the only CAS-less access, which is the memo's "RAS only" drawn rather than argued; `DCYC` and `SLOAD` are *additional* strobes belonging to the video and sound lanes, they do not replace `CAS`; and the internal slot alternates with an external partner cycle for cycle, with video, sound and refresh taking it in turn — the owner equation of `design/system-control-unit.md` §4, drawn out.
`derived` The equations said as much before the chart did: `CAS0L` has no `ADDRSEL` term in it (§5.1), which is exactly why it appears in every lane, while `DCYC` and `SLOAD` each carry `ADDRSELB` and so cannot appear in the external half.

`unknown` The chart draws each `TIME` level a full slot wide and staggers them by a sixteenth of the window, which cannot be a literal one-hot decode: it is the designer drawing *ordering*, not pulse width. Whether the ring's phases are one-hot 62.5 ns pulses (the reconstruction's reading) or slot-wide levels is not settled by any drawing in the archive, and it is the one structural question a scope on `RAS0` against `CLK4` answers in a minute (`§7`, and the closing note of this section).

`documented` And this is where the mux table of §3.6 is finally resolved: the printed order refresh → sound → video is a **decode**, not a priority, because the design derives the two request terms from one gate group — `REFB` is the output of a `NO2` whose inputs are the `ADDRSELB`-gated horizontal-window term and the sound-request term (`PK020`/`PK023`/`PK030`, sheet 20), and `SNDEN` is an `AA2` gated by the same `ADDRSELB` (`PK015`/`PK017`).[2]
`derived` Two complementary terms cannot both be true, so the table's first two arms are mutually exclusive and the third is what remains: an internal slot is video's while the display window is open, sound's while a request is pending outside it, and a RAS-only refresh otherwise. Nothing in the hardware ranks them, exactly as `design/system-control-unit.md` §4 assumes.

### 5.2 One line (512 clocks)

`documented` The line is not generated by this chip: the display window arrives as the `DE` input from the Shifter, the chip registers it (`IDEB` → `vidb`, pad `PK010` on sheet 20) and its own copy of the horizontal window is `HDE1`, a flip-flop (`PK016`) clocked from the sync chain, not from `DE` itself.[2]
`documented` Sheet 29 is the block that names the four window terms — `HDE`, `HBLANK`, `VBLANK`, `VDE`, with `LPHDE` and `HDECB` for the latched and the edge form — and `SYNCSEL` (sheet 3) is the register bit that chooses their polarity.[2]

`derived` The line's slot budget, which is the whole of the video timing argument:

| mode | words per line | arithmetic | internal slots left for sound and refresh |
|---|---|---|---|
| low res (320 px) | 80 | 320/16 = 20 per plane-set × 4 planes | 48 |
| mid res (640 px) | 80 | 640/16 = 40 × 2 planes | 48 |
| mono (640 px) | 40 | 640/16 = 40 × 1 plane | 88 |

`derived` Against those 48 (or 88) spare slots a line must place ≈8.2 refreshes (512 rows in 4 ms is 7.81 µs apart, and a line is 64 µs) and at most 3.2 sound words (the channel's 50 kHz top rate is one word per 20 µs).[2]
`derived` The budget is comfortable in low res, which is why no emulator has ever had to model refresh starvation and why the memo could call refresh "unused time slots" without qualification — but the refresh term has to be re-derived, because the counter's clock is now known: `REFCLK` is a flop on sheet 27 clocked by the 500 kHz timebase and decoded from the line structure (§3.4), so a line can owe at most 32 blanking slots, not the 8.2 this section has been quoting since its first draft.
`derived` Four windows per line is the ceiling and four is what the part needs, so refresh costs **≤4 of the 48** blanking slots and sound ≤3.2, and the memo's "unused time slots" reads as written.
`derived` The section's original "≈8.2 slots at 7.8 µs" was right in spirit and wrong by a factor: 512 rows against an 8 ms interval is 15.6 µs per row and 4.1 slots per line, not 7.8 µs and 8.2 slots, because the 8 ms target is the 41256 family's and not 4 ms.
`unknown` How many of those four a line actually receives is `LC`'s duty, which §3.4 leaves as the one open conflict — so the budget is `0 < slots ≤ 4` until that crop or a scope settles it.

`derived` The same line as a timetable, which is the form to read it in — one row per block of the 128 internal slots, and the external slots interleaved one for one:

| internal slots | owner | strobe that marks it | DRAM action |
|---|---|---|---|
| the first 80 cycles (40 in mono), one slot each while `DE` is high | video | `DCYC` at phase 1 | normal read, `CAS` at `TIME2`; the four-word group is the Shifter's business |
| the 48 after them (88 in mono), while sound is pending and `XSRQ` is high | sound | `SLOAD` at phase 1 | one word per granted slot, up to 4 back to back |
| the same 48 (or 88), whenever sound is idle | refresh | none — no `CAS` follows | RAS-only, row counter advanced on `K500CLK` |
| one external slot partner per cycle | CPU, or the DMA controller holding the bus | `DTACK` on the next `ICLK8B` edge | normal read or write; `WE` only in this half |

`derived` Two consequences fall out of the table and are worth stating because emulators get them wrong: the vertical blanking interval is **not** a special case (the window closes and its slots become blanket sound and refresh, per the memo), and the window's fetches are consecutive in the *cycle* stream with no slot skipped, so an external access is never more than 250 ns behind a `RAS` assertion.

`documented` The two strobes that mark a word's arrival are both phase-1 latches of the *granted* slot, built identically: `DCYC` from `NA2(RESB, NA3(TIME1, ADDRSELB, VIDEN))` and `SLOAD` from `NA3(ADDRSELB, TIME1, SNDEN)`, each through a macro latch clocked by `CLK` (pads `PL030`/`PL025`/`PL029` and `PL032`/`PL031`/`PL035`, sheet 21).[2]
`derived` So the word is on the bus for the whole slot and its strobe is asserted at phase 1 — two `MHZ16` states, 62.5 ns, after `RAS` falls, in the same window as `CAS` rather than before it — early enough for a DRAM access and late enough that the address has settled.
`documented` `DCYC`'s third input is `VIDEN` taken **directly** (pad `PL030`, sheet 21), while `SNDEN`'s chain takes the inverted form of the same net through an `I2` (pad `PK011`, sheet 20) — so `DCYC` is gated on the video enable and `SNDEN` on its absence, which is the blanking rule expressed in two gates.[2]
`documented` Sheet 49 says the same thing in lane form: the `DCYC` pulse sits in the cycle labelled `video` and the `SLOAD` pulse in the cycle labelled `sound`.[2]
`derived` That closes the seam this section first opened: `DCYC` **is** the video word-load strobe — the net the STE Shifter takes as `LOAD` — and the reconstruction's `viden = ~vidb` reading has `vidb`'s polarity inverted, which is a model bug and not a hardware fact.[9]
`derived` Read with `§5.1`, the two-chip video path is therefore one wire and one gate deep: the Shifter asserts `DE`, the GLUE samples it into `vidb`, opens the internal slot while it is high, and returns each word with `DCYC` at state 2 of that slot.

`documented` Sound and refresh are placed by the memo and confirmed by the enable terms: they run only in horizontal blanking, and the whole vertical blanking interval is memory-idle when horizontal blanking is not active.[2]
`documented` `XSRQ` is drawn on sheet 49 as a level across the three cycles the chart samples, and the memo's warning — refresh happens only while it is high — is the only statement of its use.[2]
`derived` With `SREQ` held low the internal slot falls through to refresh on every blanking slot, which is the memo's "normal refresh takes place only when `XSRQ` is high" read as hardware rather than as advice.

### 5.3 One frame

`derived` 313 lines of 512 clocks is 160,256 × 125 ns = 20.032 ms, 49.92 Hz; the 60 Hz mode differs only in the count the vertical counter is reloaded with, because the counter and the line clock are the same.[2]
`documented` The frame machinery is the vertical-sync block of sheets 26–30: `IVSYNC`/`IIVSYNC` are the latched and inverted forms, `VDE`/`VBLANK` the vertical window terms, and `FRAME` (sheet 20, feeding sheets 11 and 15) is the pulse that tells the video address counter to reload.[2]
`derived` The video address counter is 24 bits across `VIDBH/VIDBM/VIDBL` plus `VCOUNT` (sheets 4, 12, 15); a reload therefore rewrites the top of the frame from software's last write, and every derivation of the visible window starts from that one wire.

`inferred` The reconstruction asserts `VSYNC` at `vsc == 508` in colour and `510` in mono, and reloads the counter twice when the mode registers are written inside the VBL handler — the Level 16 finding, hardware-confirmed and the study's one T1-class behavioural fact (§2.5).[9]
`unknown` Which of those two numbers the drawings intend is not readable: the vertical counter and its compare live in the sync block of sheets 26–30, whose text layer carries the nets but not the constant.

`documented` Line width and fine scroll are registers, not wiring: `WLOCH/WLOCM/WLOCL` and `WHOFF` are write nets with their read twins `RLOCH/RLOCM/RLOCL`/`RHOFF` (sheets 10, 15), and `NOSCROLL` (sheet 29) is the one-bit mode that disables the whole mechanism.[2]
`derived` Because both are *read-back* pairs, the address the counter starts a line from is software-visible, which is what makes the STE's hard-scroll registers cheap to emulate as an offset added at reload rather than as a per-word computation.

### 5.4 The bus and its grants

`documented` The 68000's answer is clocked, not combinational: `DTACK` is the output of a flip-flop (pads `P8012`/`P8006`, sheet 8) clocked on `ICLK8B`, and its set term is an `NA3` gate fed by the cycle comparator and the device `XACK` terms.[2]
`derived` That is why `DTACK` can never arrive mid-slot: the fastest answer is the next 8 MHz edge, so the CPU's cycle quantises to whole clocks at 125 ns and a slot's worth of waiting costs 250 ns = two clocks, which is exactly ADR 0009's "round up to 4".
`documented` The decode itself is a comparison chain, not a table: `BERR` is generated from a chain of `NLCB1`/`NLCBN` cells (pads `PB016`–`PB022`, sheet 11) fed by the address-compare terms, so a bus error is the *decode's* output and not a timeout.[2]
`derived` A cycle that loses its slot is therefore cheap and bounded: the external half recurs every 500 ns, the CPU sees `DTACK` one 8 MHz edge later per slot lost, and the cost of a steal is a multiple of 250 ns, never a stall of unknown length.

`documented` The grant relay is the classic 68000 set and it is all on the bus-interface sheets: `BR` (request in), `BGI`/`BGO` (grant in and out, for a second master), `BGACK` (acknowledge, sheet 9), `RDY`/`READY` (the DMA controller's handshake, sheet 8) and `DMADIRB` (direction, sheet 3).[2]
`inferred` The reconstruction makes the acknowledge a function of the address select — `BGACK = ixdmab` — so a granted master only sees acknowledge when the current cycle has finished, and a steal is cycle-granular by construction rather than by courtesy.[9]
`derived` If that holds, no DMA or blitter access can split a 4-clock 68000 cycle, which is the property every emulator's blitter timing assumes without proof.

`documented` `VMA`/`VPA` are the 6800-peripheral pair, and the design carries a separate `VPAEN` and `PVPAEN` alongside `IACK`, `IIACK`, `FC0–FC2` and the `FFFXXX` decode — the interrupt-acknowledge cycle is decoded on-chip rather than left to the peripherals.[2]
`unknown` Nothing in the archive times the `VMA`/`VPA` cycle against the slot grid; the drawing shows the decode, not the sequence.

### 5.5 The register block

`documented` The address decode is one sheet (3) and it is hierarchical: `FFFXXX` for the interrupt-acknowledge and RTC space, `FF9XXX` for the controller registers, `FF8XXX` for video, sound, MMU and the selects that leave the chip, with `MIXED*` (`MIXD3CD`, `MIXED320`, `MIXED364`…) as the decoded combinations the discrete gates actually match.[2]
`documented` The selects it produces are named for their destinations: `PADSEL`, `PENSEL`, `BUTSEL`, `JOYSEL`, `CARTSEL`, `SCRLSEL`, `SYNCSEL`, `MDESEL`, `SNDCS`/`ISNDCS`, `SNDIR`, `MFPCS`, `FCS`, `RTCCS`/`RTCRD`/`RTCWR`, `DMADIRB` and `REGXACK`/`REGXACKB` — the last the acknowledge a *register* write returns to the bus.[2]
`derived` Read as a net list this is the whole of the "peripheral" side of the chip: every device the STE addresses is a select line out of this sheet, and the chip itself keeps only video, sound, refresh, DMA and the pads.

`documented` The state is held in `RLC` cells and read through `SAB` sense bits (§3.5), and every software-visible register has a *write* net and a *read* net drawn separately — `WVIDBHB/RVIDBHB`, `WCONFIGB/RCONFIGB`, `WSFB*/RSFB*`, `WSFT*/RSFT*` — which is what makes readback a property of the design rather than of an emulator's guess.[2]
`derived` Two storage sites for `$FF8260` (§6.7) is the same pattern taken one step further: the Shifter and the GLUE each decode the address, so the register is not mirrored, it is duplicated, and a write must reach both.

`derived` Follow one write end to end, because the chain is the same for all of them and the design draws every link of it. A store to the video-base middle byte goes:

1. **Bus.** `AS` falls with the address on `A1–A23` and data on `D0–D15`; the decode sheet (3) matches `FF8XXX` and raises the region select, and the combination gates raise `MDESEL` for the video block.
2. **Acknowledge.** `REGXACK` (sheet 3) is the register's own acknowledge and feeds the `DTACK` gate of §5.4, so the cycle ends on the next 8 MHz edge with no peripheral wait — a register write is always a two-clock answer.
3. **Latch.** The data bits are clocked into `WVIDBMB`, the write net of the video-base middle byte, which is an `RLC` cell of the video block (sheets 4, 12). There is no other storage: the write net and the read net `RVIDBMB` are separate wires, which is why the register reads back at all.
4. **Reader.** What consumes it is the video address counter's reload path (§5.3), gated by `FRAME` — so the value written is invisible until the next frame boundary, exactly as the sound memo says of its own base registers.
5. **Readback.** A load from the same address drives `RVIDBMB` through the data-bus `TS` cells (sheet 6) and returns the latched byte, not the counter's running value; the counter is the separate read-only pair beside it.

`inferred` Aliasing is then a property of the decoder's width and nothing else: Hatari's finding that `$FF8000` and `$FF8002–0F` are harmless, while `$FF8001` sets the bank sizes, is what a decode that compares only the bits the `MIXED*` terms carry would produce — but no drawing states which bits those are, so the alias map stays `T3`.[9]

`documented` What the design does **not** define is as important as what it does: there is no register map in the archive — no table of bit fields, no reset values, and no statement of which bits of `$FF8001`'s bank-size field are decoded.[2]
`inferred` The two vendored emulators carry that knowledge as behaviour: Hatari's `$FF8001` bank table, its readback of the video counters, and the observation that `$FF8000` and `$FF8002–0F` are harmless are model facts, T3 by definition, and none of them is confirmed by any drawing.[9]

### 5.6 Interrupts and the controller side

`documented` The priority encoder is integrated, and sheet 28 lists its inputs: `EINT3`, `EINT5`, `EINT7`, `MFPINT`, `FDINT`, `DISKINT`, `HDINT`, `VINTB`, `HINT` and `EXTS`, combining into the three outputs `IPL0`–`IPL2`.[2]
`derived` That is the 74LS148 the ST needed on the board, and its presence on-chip is why the STE's interrupt latency does not include an external encoder's propagation delay — the same argument the service manual makes about the discrete board.

`documented` `HINT` and `VINTB` are latches in the sync block, `HINT` derived from the horizontal compare and `VINTB` from the vertical, with `INTERLACE` an `XO` (XOR) output in the same block driven by `MDE0B` (pads `PS066`/`PS067`, sheet 28).[2]
`unknown` What `INTERLACE` drives is still not readable from the text layer, and the reconstruction hard-ties it off — §6.5 and §3.7 stand.

`documented` The controller side is three different things behind three selects, and the sheets are explicit about which is which: `PADR`/`PADRB` for the paddles, `PENR`/`PENRB` for the pen, `JOYSEL` for the joypads, all read through the 32-bit port of sheet 32 (§3.4).[2]
`documented` The pen is not a converter at all: sheet 33's two position counters free-run against the beam (horizontal from `M2CLOCK`, vertical from `IHSYNC`+`VDE`) and the pen's own edge latches both into the `MF2` banks — a **sampling of video position by an external event**, and the one place on this chip where a signal outside it captures state inside it (§3.4).[2]
`derived` The paddle is the converter: a divider on the 500 kHz timebase whose value is what a read returns, which is why its reading depends on the video mode — the count is a number of `PADC` ticks inside a fixed number of lines, and the line rate changes with the mode.
`documented` `PADC` is the 500 kHz timebase through sheet 31's own eight-tap divider, and `PADRST` clears that divider.[2]
`documented` And `PADRST` is not a start of anything: it is built from `RESB` and `PORB` through an `AA2` (`PV017`) whose output drives the `XR` pin of `DPR` `PV010`, whose `XQ` runs out through an `I3`, an `I4` and an `OB4` (pads `PV011`–`PV013`) and leaves as `PADRST`, active low (the owner's read; the sheet's text layer carries the same cells).[2]
`derived` So the paddle divider is cleared only at reset, never per read and never per line: the `PADRST`-is-the-start hypothesis is dead, and sheet 32 closes the other door as well — its `PADR` reaches select logic and `TB2` enables only, so a read cannot start a count either (§3.4).
`unknown` What is left is capture, not start, and sheet 33 has now been ruled out as its home: that sheet is the pen's pair of position counters and carries no `PADC` net at all. So the paddle's value is frozen either inside sheet 32's own registers (transparent latches, so a read samples the divider live) or by an edge off the pad pin that no sheet in the archive names. That is §7's question 6 and §8's test 6 — and the two candidates give different readings for a fixed resistor, which is what makes the test decisive.

### 5.7 Reset, test, and the clocks out

`documented` Reset is one net fanned out, and the sheets count the fanout: "3 loads on `PORB`" on sheet 20 and "10 loads on `PORB`" on sheet 21 — the power-on reset is a distributed level, with `POR3BO` the clock sheet's own reset form and `LRESB` the local reset from the board.[2]
`derived` A reset that is a level rather than a strobe means every latch in the chip is held transparent while it is low, which is why the design can afford 90 latches and no reset tree.

`documented` The clocks that leave the chip are the pads the board inventory already knows: `CLK8`, `CLK4`, `CLK2` and `KHZ500` on pins 3–6, generated on sheet 7 from `MHZ16` through `ICLK8`/`ICLK8B`, `CLK4`/`CLK4B` and `M2CLOCK`/`M2CLOCKB`, with `K500CLK` and `KHZ500` the two 500 kHz forms.[2]
`derived` `CLK8` is the CPU's clock and `CLK4` the one the 6800 peripherals use, so a board-level user of this chip sees four clocks and no phase information at all — the phases stay inside.

`documented` `TEST` (pin 74) and `ITESTB` are the test pair, and the netlist shows `ITESTB` reaching cells in the clock generator (sheet 7) and the interrupt combiner (sheet 28) as well as the pads.[2]
`unknown` What the mode does is undocumented in the archive — no memo, no note, no test pattern that names it — and it stays `unknown` here; unlike the 68000's `VPA` mode there is not even a 1985 paper to reason from (§9).

---

`derived` What this section changes in our model, kept to the three places where it does:

1. **`DCYC` is the video word-load strobe** (§5.2), drawn in the video lane and gated directly on `VIDEN`; `design/system-control-unit.md` §6's "fill one word per cycle" now has its strobe and its gate, and the reconstruction's inverted-`viden` reading is recorded as a model bug.
2. **The internal slot precedes the external one** (§5.1) is now a documented fact of the `RAS` equations rather than an assumption, and `design/system-control-unit.md` §3 can date it.
3. **`DTACK` is a flip-flop on `ICLK8B`** (§5.4) fixes the CPU's answering instant to a whole 8 MHz edge in *hardware*, not merely as ADR 0009's choice — the same rule, now sourced.

`unknown` Two questions are left, and the owner's reading of the two charts closed the other two — `COL/ROW`'s polarity is settled by where `RAS` falls, `DCYC`'s seam by the lane the pulse is drawn in, and the lane order of sheet 49 is now part of §5.1.

- **The phases' width** (§5.1): the chart draws each `TIME` level a whole slot wide, which cannot be a one-hot decode; whether the ring's phases are 62.5 ns pulses (the reconstruction's reading) or slot-wide levels is a scope question, not a drawing question.
- **`LATCH`'s payload**: the sheet fixes where and how wide the pulse is, and rules out an address latch for its own cycle, but not what it captures — the read-data latch and the latch that arms the *next* cycle's mux remain both open.

---

## 6. Conflicts in the record, adjudicated

**1. "Refresh preempts video" vs "the arms cannot meet".** Both readings exist in our own notes, ten days apart, and both are defensible from the same sheet.
`documented` The mux table says `REF = 1` selects refresh whatever `SADSEL` and whatever the video state; the memo says refresh uses *unused* blanking slots and that "during the active portion of the horizontal line, video refreshes will occur as in the present design"[2].
The resolution is that the table is a **decode** and the memo is a **scheduling rule**: precedence in a mux is not a priority between clients, because the refresh request itself is only generated in horizontal blanking.
The correction of 2026-11-09 (that `REF`/`SNDEN`/`hde1` are derived from one another so exactly one arm is live) and the correction of 2026-11-01 (that the table encodes precedence) are therefore both right about their own artefact[2].
What would falsify this: a scope trace showing a CAS-less RAS cycle inside the display window.
That is §8.1.

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

## 7. What the emulator needs

The open questions that survive, mapped to our own documents and ranked by what they cost us when wrong.
Tiers per the handoff: T1 hardware-captured, T2 hardware-corrected, T3 emulator-derived.

| # | question in our core | the claim that answers it | tier | home |
|---|---|---|---|---|
| 1 | Is refresh really "filler, never preemptor"? (`design/system-control-unit.md` §4) | yes — memo §4.1 + the mux decode; refresh requires blanking **and** `XSRQ` high | T2 (memo, intent) → T1 by §8.1 | SCU §4 |
| 2 | Does the sound counter increment on grant or on load? | RTL: on grant; memo: `SLOADB` rising edge latches data. Unresolved ordering | T3 | `design/crossings.md` (§2 refills) |
| 3 | Is `SINT` a slot-wide level or a 480 ns pulse? | 480 ns pulse, and the next frame's first fetch may overlap it | T2 (memo) | Sound's frame-end event |
| 4 | Does a mid-frame `top` write move the current boundary? | no — the boundary is `count == top`, evaluated continuously | T2 (memo) | Sound |
| 5 | What is the refresh period? | every element is read except `LC`'s duty: ten stages on sheet 11, `REFCLKB` a sheet-27 flop on the 500 kHz timebase with `D = NAND(HCTR, LC)` and both inputs bare, `M2CLOCK` 4 MHz, `HCTR` 16 µs, board 512 rows of 41256 wanting 15.6 µs a row — so the walk is bounded 8.19 ms (LC permissive) to 131 ms (LC sparse), and only the first is legal | T3 (one register bit) | SCU §11.1 |
| 6 | Is the paddle a counting ADC, and what is its law? | counting, clocked from the 500 kHz timebase through sheet 31's divider; cleared only by `PADRST`, not started by the register select, and with no `PADC` path into sheet 33 — so what remains unknown is the *capture* of the count, in sheet 32's registers or at the pad pin | unknown | Controllers |
| 7 | Are 313 lines × 512 clocks right, and where is the `DE` edge? | design has `HDE/LPHDE/VDE/HBLANK`, `SYNCSEL`, `MDE0/MDE1`; the reconstruction gives `ivsync_set` 508/510 | T3 | Video |
| 8 | Does the CPU really get every second slot? | yes — `ADDRSELB` is a phase, and CPU-outranks-DMA only *inside* the external slot | T2 (design table) | SCU §3 |
| 9 | Is `$FF8001` write-only, mirrored, or aliased? | the design carries separate read and write nets for these registers; Hatari models readback, and `$FF8000`/`$FF8002-0F` are harmless | T3 | SCU registers |
| 10 | What is `BINT` for? | `unknown` — a pin, a net name, and no published explanation | unknown | Blitter study |

Two of these (2 and 10) are candidates for a labelled `unknown` in the model rather than a guessed rule; the handoff's stopping rule is explicit that an honest `unknown` plus the experiment beats a plausible constant.

---

## 8. The experiments that would settle the rest

Six tests, in the order that buys the most certainty per evening.
Each is a TOS program (or a bootable `$4E71`-filler ROM) run on the real 1040STE, with a scope on the named pin or a read-back through a named register; each has a pass criterion that decides between the two candidate models in this document.

1. **Refresh during the display window.** Scope `RAS0` and `CAS0L` for one full line while `DE` is high and sound is off, with the video base set so that the fetch never idles.
*Pass:* no CAS-less RAS cycle while `DE` is high → §6.1 resolved in favour of the memo's scheduling rule; any CAS-less RAS inside the window falsifies it and makes the mux table a true priority.
2. **Refresh versus `XSRQ`.** Drive `XSRQ` low indefinitely (a modified board, or a paddle port that holds the line) and scope `RAS0`.
*Pass:* refresh stops — the memo's warning, and the only direct evidence for the owner equation's third arm.
3. **Sound counter ordering.** Play a single frame with a known base/top; poll `$FF8909/0B/0D` in a tight loop and, in a second run, scope `SLOAD`.
*Pass:* the counter value sampled between two refills advances by exactly one fetch, and the increment is visible before or after the `SLOAD` edge — which settles §7.2 either way.
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

## 9. What is still unknown

- **The shipped mask.** These are *designs*, and the archive holds three revisions of them. Whether the part on
  our board is `ST-4081` or the `TXL` variant is not established; nothing in the design fixes which one shipped
  in C300589-001. A decap answers this and the two revisions differ in cell count (68 vs 73 FFs), so the
  question is not academic[2].
- **`LC`'s position in its register.** The gate takes `LC` bare (twice confirmed) and the row count is sourced, so the refresh walk is bounded rather than computed: 8.19 ms if `LC` is permissive on every line, 131–262 ms if it is a sparse bit — and only the first keeps the machine inside its DRAM's interval. Which bit of sheet 7's `P7048`–`P7054` register `LC` is, and whether the sheet-20 reading of it was of a different net, is the last open item in the chain.
- **The paddle's capture edge and conversion law** — the pen's counterpart is now closed: sheet 33 latches two free-running position counters on the pen's own edge, so the pen needs no law, only two counters and one event.
- **`BINT`'s function inside the GSTMCU**, and whether the C302183 variant's blitter logic reuses it.
- **The test mode** (`TEST` pin 74, `ITEST` gating dozens of cells) — undocumented here, unlike the 68000's VPA
  mode which at least has a 1985 paper[2].
- **Whether the sound FIFO is 4 words.** Nothing in the design archive states a depth; the figure we carry is
  `inferred` from Steem, and the reconstruction puts the word pipeline in the *Shifter*, not here — the two
  claims are about different sides of the same wire and both are currently unmeasured[2].

`inferred` What a decap would not settle: any of §7's timing questions except indirectly.
The right next buy for this machine, if anything is ever bought, is not a die but a logic analyser session — and §8 is the shopping list for it. No figure is drawn with this study: the design gives no die geometry, and a floorplan inferred from gate counts would be exactly the outrun-the-evidence failure the 68000 study warns about.

---

## 10. Licence position

The design documents are Atari's proprietary CAD, recovered and published by Christian Zietz; the archive's own terms are research use.[1][2] **Nothing from the archive is vendored into this repository** — what is above is two kinds of excerpt and no more: transcriptions (the gate-count list, the printed decode table, four strobe equations read off the netlist) and measurements (sheet 48's transitions, located on its own printed grid) — each cited to the archive URL rather than reproduced.
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
