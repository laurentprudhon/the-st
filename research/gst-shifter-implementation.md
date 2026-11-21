# The GST Shifter (C300588-001) at the silicon level

### What the record establishes, what the reconstruction infers, and what only the board can settle

*Subject: the STE's video chip, `U401` on the 1040STE board, Atari house number C300588-001, 84-pin PLCC;
the plan called the warehouse part "IC STESHFTR 84P NATIONAL". Studied 2026-11-21, the second part of the
per-chip campaign after the GSTMCU.*

*Basis, and it is the thinnest of the campaign so far: **there is no die photograph and no Atari drawing of
this part.** The archive's 33-sheet set and its 14-sheet addendum are the GSTMCU's; Zietz's catalogue records
no drawing of the STE Shifter at all, and the public decap archives hold no STE custom. The study therefore
rests on the service manual's 84-pin symbol, **ijor's die-level reverse engineering of the earlier ST
Shifter** (the only real documentation of this family), Atari's own register wording, the board article that
describes the STE video circuitry, the Verilog reconstruction of the chip, and the GSTMCU study's documented
side of every wire they share.*

*Labels: `documented` (a primary source or a data file says so), `derived` (arithmetic over counted facts),
`inferred` (a re-implementation or a careful reader implies it), `unknown`; T1 hardware-captured, T2
hardware-corrected, T3 emulator-derived. Companion pages: `research/gstmcu-silicon-implementation.md` (the
other side of `DE`/`DCYC`), `research/ste-super-hires.md` (what this part does *not* have),
`research/gstmcu-reconstructions.md` (the RTL, file:line), `design/video.md` (the shape our model takes).
The five educational sheets drawn from this study are in `research/gstshifter-silicon/schematics/` (blocks,
one group, colour, one line, registers) — functional diagrams, and no floorplan, since no die exists.*

---

## 1. Executive summary

| question | answer | § |
|---|---|---|
| Do die photographs or drawings exist? | **Neither.** No STE custom was ever decapped publicly and Atari's recovered CAD has no Shifter sheet. What exists is ijor's reverse engineering of the **ST** Shifter's die, which shares this chip's architecture and not its feature set. | 2.1 |
| What is it made of? | `documented` Four rows of sixteen two-bit cells: a 16×4 word buffer (clocked by `LOAD`) sitting **inside** a 64-bit shift path (clocked by the pixel clock), plus the palette, the clock mux, the reload control and the sound side. ijor's order-of-magnitude for the family: **≈5,000 transistors**, under a tenth of the 68000. | 3.1–3.3 |
| Microcode, PLA or random logic? | **Random logic, and no table of any kind** — same answer as the GSTMCU and for the same reason: this is a gate array of the same generation, with no memory cell in the library other than the register cells it needs. | 3.2 |
| What counts? | A 4-bit **pixel counter** (16 pixel clocks), a **4-stage LOAD shift register**, the shift array itself, and the sound side's FIFO pointers and sample-rate divider. There is no address counter: the video address is U400's. | 3.3–3.4 |
| Clocking? | One **clock mux** fed by the 32 MHz master, producing 8 MHz (low), 16 MHz (mid) or 32 MHz (mono); mode 3 stops the clock entirely. The pixel clock is the only thing in the machine that changes rate with the display mode. | 3.5 |
| What is a sixteen-pixel group really? | **Four `LOAD`s = 2 µs**, which is 16 low-res pixels, 32 mid pixels or 64 mono pixels. The grouping is this chip's own; the memory controller delivers one word per display window and knows nothing of groups. | 4.1 |
| Where does colour come from? | 4-bit index → 16 words of **12-bit** palette in transparent latches → 12 digital pins → a four-resistor-per-channel network → the MC1377 encoder. The four bits are weighted **1-8-4-2**, the register map's "0321" order. | 4.3 |
| What does the emulator still not know? | The sound FIFO's depth, the exact ladder weights, and the two remaining direction disputes (`SCLK`/`FCLK`, `LD`/`HD`). Nothing load-bearing for the video path is open. | 5, 8 |

`derived` The one-sentence machine: *the memory controller pushes one 16-bit word per display window into a sixteen-word buffer; every fourth word the buffer is copied into a 64-bit shift path that is running at 8, 16 or 32 MHz depending on a two-bit mode register, and each shifted-out 4-bit index addresses sixteen 12-bit latches whose contents leave the package as twelve digital pins — while the same chip, on the same bus, also collects the sound DMA's words into a FIFO and clocks them out two bytes at a time.*

`derived` And the finding that reorganises the campaign's arithmetic: **the "sixteen-pixel group" is a low-res name for a two-microsecond grid.** The transfer is always four words, and a word is always fetched one per display window, so what changes with the mode is only how many pixels four words buy. The GSTMCU study's 80-words-a-line budget and this chip's group are the same fact seen from opposite ends, and a mono line's 40 words are **ten** groups, not forty.[16][18]

---

## 2. The evidence, and what each layer can answer

### 2.1 No decap, no drawing — and a neighbouring chip that has both

`documented` The archive that carried the GSTMCU carries no Shifter: Zietz's recovered CAD set contains `TA-4081`/`ST-4081`/`TXL` for the GSTMCU and separate designs for the Blitter, and nothing for the STE's video chip. The public decap archives were re-checked for this study and are unchanged: five Atari parts, every one of them Lynx or 7800 silicon.[4][5][12]

`documented` What does exist is ijor's die-level work on the **ST Shifter** — the earlier part — published as schematics, simulation waveforms and multi-year forum analysis.[16] It is the best documentation this family has, and it is documentation of a *different chip*: the ST part has five address bits and no sound DMA, the GST part has a sixth address bit for `HSCROLL` and the sound registers.[6]
So every statement transferred from it below is marked and justified, and §5.9 tabulates which of its findings transfer and which do not.

| layer | what it gives | what it cannot |
|---|---|---|
| service manual, sheet 4 (84-pin PLCC symbol) | pin list, pin numbers, directions, the package | internal structure, timing, anything about a register |
| the board article (ST Magazine 44, 1990) | what each group of pins is *for*, the two data buses, the sound path, the SCLK story | logic, and it is a magazine article: it gets two directions wrong.[6] |
| ijor's ST-Shifter schematics and analysis | the shift array, the reload control, the clock mux, the palette latches, the reset condition, the "every other 16 pixels" bug | nothing about the STE's additions, and it is one reader's trace.[16] |
| Atari's own register wording (STE FAQ, developer addendum) | the palette's bit weights, the scroll registers and their traps | any internal structure.[18] |
| the reconstruction (`gstshifter.v`, `shifter_video.v`) | a complete, buildable reading; the STE features the ST part lacks | intent; it is a transcription with a known model bug |
| Hatari `video.c`, `conv_st.c`, Steem | behaviour to explain, and a checklist of constants nobody sourced | silicon truth — T3 by definition |

**The pin symbol is the only primary source this study has.** Board-inventory §3.5 transcribed it: 84 pins, with `MD0–MD15` (56–71), the bus side (`A1–A6`, `CS`, `R/W`, `PD0–PD15`), `R0–R3`/`G0–G3`/`B0–B3` (38–49), `SD0–SD7` (21–28), the video handshake (`DE` 54, `LOAD` 37, `SREQ` 36), the sound and Microwire pins, five pins drawn but not legible (35, 52, 55, 72, 76), and pin 73, which the board article names as `LOWP` (§5.5).

### 2.2 The reconstruction is a transcription, and it says so

`inferred` `gstshifter.v` is Till Harbaum's shifter with Gyorgy Szombathelyi's STE additions, and its header states that its video engine is "based on chip decap by Jorge Cwik" — i.e. on ijor's work.[9][16] It is therefore a transcription of a *reading of the ST die*, extended by inference for the STE, and not a second witness.
Where it agrees with ijor's description it is corroboration only of the transcription; where it disagrees with a documented register behaviour it is wrong, and §5 records one such case (its `$FF8264`/`$FF8265` shared decode).

`inferred` It is still the cheapest complete picture of the STE-only additions, which ijor's drawings do not cover: the hard-scroll shift path (`ste_shifted_0…3`, `pix_cntr`), the sound FIFO, the byte split and the Microwire shifter are all in it, with comments naming the demos that needed each behaviour.

### 2.3 Emulators answer a different question

`inferred` Hatari models this chip well enough to run every STE program worth running, which makes it a checklist — never evidence.[9] The checklist items worth naming are the ones no document sources: the two-pixel display latency, the `$FF8260`/`$FF8261` split, the byte-write duplication in the palette, and the `$FF8264`/`$FF8265` prefetch rule.
Where Hatari encodes a number, the number is T3 and the study says so each time.

### 2.4 The board article is the only text about *this* chip's wiring

`documented` The ST Magazine n°44 STE study describes the GST Shifter from the outside and gets the essential structure right: two 16-bit data buses with the isolation logic now **inside** the chip, a third 8-bit bus to the two sound DACs through `74F374`s, the `\*MWE`/`MWD`/`MWK` Microwire link, the 50/60/71 Hz sync and `VIDEOMOD` arriving from U400, and the sixth address bit that carries `HSCROLL` and the sound registers.[6]
It also pins two facts no other source states: the package choice was **pin-limited** (a wider bus to the DACs would have needed a non-standard case), and `SCLK` is an 8 MHz input from a separate oscillator, required only by the sound path.

---

## 3. The chip the record shows

### 3.1 Size, and why the package is 84 pins

`documented` ijor, asked what the chipset's transistor count looked like next to the 68000: *"it would be equivalent to an order of 5,000 transistors. That's less than a tenth the 68K!"*[16]
That figure is for the ST GLUE/Shifter pair rather than this part alone, so the honest statement is an order of magnitude, not a count: **a few thousand transistors**, against 15,394 for the GSTMCU and ~68,000 for the CPU.

`derived` The package is the design constraint, and the article names it: the sound path leaves as **eight** pins and is then split to two DACs by `74F374`s, because sixteen bits of sample data plus a 16-bit memory bus plus twelve RGB pins plus the register bus does not fit a standard 84-pin case.[6]
That is why the "third data bus" is eight bits wide and why a word is sent as two bytes at twice the sample rate's clock.

### 3.2 No table, and the bus logic that came in from the board

`inferred` There is no ROM, no PLA and no RAM in this chip's function: no description of the part mentions an array, the palette's sixteen words of four bits are latches, and the shift array is the only regular structure anyone has traced. It is the same finding the GSTMCU study reached by counting a cell library, reached here by absence.[16]

`documented` The ST needed four TTL packages to isolate the memory bus from the CPU bus; on the STE that logic is **inside the Shifter** — `LATCH`, `\*RDAT` and `\*WDAT` are its inputs, and the isolation is what lets the CPU keep working while the video fetch reads RAM.[6]
`inferred` The reconstruction models exactly this pair of latches: `dout_l` (memory bus → CPU bus, clocked by `LATCH`) and `mdout_l` (CPU data → memory bus, enabled by `WDAT`).[9]
`derived` So the `PD0–PD15` pins on the 84-pin symbol are not a second data port in the usual sense: they are the *memory-side* half of a transceiver that this chip has absorbed, and the palette is written through the same path.

### 3.3 The shift array: sixteen cells wide, two flip-flops deep

`documented` ijor's read of the array: **four rows of sixteen cells, each cell holding two flip-flops**, one in a vertical buffer path clocked by `LOAD`, one in a horizontal shift path clocked by the pixel clock.[16]
He is explicit that the split is physical rather than logical — "the buffer and shift registers are physically not separated" — and that for behaviour the two can be modelled as separate registers.

| stage | width | clock | what it does |
|---|---|---|---|
| buffer (the community model's "IR") | 4 words × 16 bits (16 columns × 4 rows) | `LOAD` (from `DCYC`) | each pulse pushes a new word in at the top and discards the oldest |
| shift (the model's "RR") | 4 × 16 bits | pixel clock | shifts one bit per pixel clock, or takes the buffer's contents on `Reload` |
| transfer | 64 bits | `Reload` | **always four words, in every mode** — resolution changes the wiring of the chain, not the transfer |

`documented` "The transfer from the FIFO/IR to the RR registers is always 4 words (64 bits), always after every 4 LOAD pulses, regardless of the resolution."[16]
`derived` That one sentence fixes the whole group arithmetic: the group is a property of the buffer's depth, not of the display mode, and the resolution decides only how the four shift registers are chained — in mono the four are combined into a single 64-bit shift, and the bits that no longer line up are masked away by the palette stage.[16]

### 3.4 The reload control, which is the real mode logic

`documented` Two counters decide when a group becomes pixels: a **four-stage shift register** fed by `LOAD` (a `1` enters on each load and stays until cleared, one pixel clock after `Reload`) and a **4-bit pixel counter** that wraps at 16.[16]
`derived` The reload therefore needs **both** events: four words loaded *and* sixteen pixel clocks since the last reload — which is why a line whose word count is not a multiple of four can leave words stranded, and why the mechanism self-stabilises when the pixel counter happens to be far enough from its wrap at the first `LOAD` of a line.[16]
`documented` The failure mode this produces is the famous one: under the wrong conditions the logic misses a reload and sixteen pixels of palette 0 — the border colour — are shifted out. "Every other 16 pixels background" is not a display effect, it is a reload that did not happen.[16][17]

### 3.5 The clock mux

`documented` The Shifter's own clock generator takes the 32 MHz master and selects the pixel clock from the two-bit mode register: 8 MHz low, 16 MHz medium, 32 MHz high. ijor: *"SHIFTER, at high rez at least, works at 32 MHz."* And: the mux **outputs nothing at all** when both mode bits are set, so mode 3 stops the chip's pixel machinery until a valid mode is written.[16]
`derived` That is the hardware behind the demos that use mode 3 as a switch: it is not a documented mode being interpreted, it is an undefined decode that happens to gate the clock off. Our model's `$FF8260` value 3 handling should be described the same way.
`inferred` The reconstruction implements the same ladder as a 2-bit counter and three enable terms rather than a mux, and its mono enable is the 32 MHz tick itself.[9]

`documented` The chip's *other* clock is `SCLK`, an 8 MHz input from a separate oscillator, which the article says feeds only the sound path: disconnecting it leaves the machine working with no sound.[6]
`inferred` The reconstruction derives the sample-rate clock from its 32 MHz tick instead (`a2base_cnt` to 639, then a 3-bit divider giving 6.25/12.5/25/50 kHz), i.e. it models the *result* of the divider without modelling which clock it divides.[9]

### 3.6 The palette

`documented` Sixteen registers, **12 bits each**, four per channel, in transparent latches rather than edge-clocked flip-flops: "The palette registers are indeed asynchronous transparent latches."[16]
`documented` The four bits are not weighted 8-4-2-1: Atari's own register table gives the order as **"0 3 2 1" encoding the values "1 8 4 2"**, with the worked fade sequence `$000, $888, $111, $999, $222, $AAA…` that follows from it.[18]
`derived` So the register's most significant channel bit (bit 11 for red) carries weight **1**, bit 10 carries 8, bit 9 carries 4 and bit 8 carries 2 — a rotation of the ST's three bits with the new bit dropped in at the bottom of the scale while sitting at the top of the field. Two implementations compute exactly that from the stored word: Hatari's conversion table, and the reconstruction's one-bit rotation of the stored channel nibble.[9]
`derived` The consequence is visible to software and is worth stating plainly: `$0777`, the ST's full red, is **14/15** on an STE, because the ST's three bits become the top three weights; `$0800` — the new bit alone — is the *dimmest* red (1/15), not the brightest; and full red is `$0F00`.

`documented` There is a second copy of **bit 0 of colour 0** — the mono/mono-inverse select — written together with the register: "SHIFTER has a second copy of bit 0 of the first palette register, the color selection for monochrome mode. Both registers are updated together."[16]
`derived` That is why a single word write to `$FF8240` both sets the border colour and flips monochrome inversion, and why the reconstruction models `monocolor` as a separate latch written from the same address.[9]

### 3.7 The sound side

`documented` Three things live here and they are separable: the **requester** (`SREQ` out, `\*SLOAD` in, a word FIFO), the **byte splitter** (`SD0–SD7` plus the two latch strobes on pins 19 and 20), and the **Microwire shifter** (`\*MWE`, `MWD`, `MWK`).[6]
`documented` The counters are *not* here: `FBASE`/`FCOUNT`/`FEND` and the address counter are GSTMCU registers, which is why the same wire is specified from both ends and why the U400 study's §4 memo is the authority on the frame boundary.[6][2]
`inferred` The reconstruction puts a depth-8 ring buffer (seven usable words) in the chip and computes `SREQ` as "not full", with a comment arguing the depth from timing: at the top sample rate the buffer must survive the non-blanking part of a line, which is why a two-word buffer would not do.[9]
`unknown` No source states the real depth; four words is the figure the campaign carries from Steem and it is T3.

### 3.8 What is not in this chip

Four things that look like they belong here and do not:

- **`DE`, `\*BLANK`, `HSYNC`, `VSYNC` generation** — U400's sync block. The Shifter *receives* `DE` and `\*BLANK` and treats `DE` asynchronously; it does not make them.[6][16]
- **Every fetch decision** — one word per display window, chosen by U400's internal-slot decode, and the video address counter that walks with it.[2]
- **The fine-scroll address arithmetic** — `LINEWIDTH` and the line-offset registers are U400's; only the *pixel* offset lands here (§4.7).[6]
- **The 320/640-pixel boundary** — there is no such boundary in this chip. The mode register selects a clock and a plane wiring; the picture's width is the `DE` window's, computed in U400 (§4.2).[17]

---

## 4. The chip in operation

### 4.1 One sixteen-pixel group

`derived` The grid is **2 µs = four display windows**, and the mode decides how many pixels that buys:

| mode | pixel clock | pixels per word-plane | pixels per group (4 words) | groups per line |
|---|---|---|---|---|
| low (320 px) | 8 MHz | 16 | 16 | 20 |
| mid (640 px) | 16 MHz | 16 (2 planes used) | 32 | 20 |
| mono (640 px) | 32 MHz | 16 | 64 | 10 |

`derived` The line budget closes on U400's side exactly: 20 × 4 = 80 words for a low or mid line and 10 × 4 = 40 for a mono line, which is the GSTMCU study's own table; 200 colour display lines × 20 groups and 400 mono lines × 10 both give **4,000 groups a frame**, as `design/video.md` §8 requires.
`inferred` A word becomes visible **two pixels late**: the community model's measurement of the STE is "16 cycles plus 2 due to internal delays" from the first `LOAD` to the first RGB value, which is the whole group's latency plus the pipeline design/video.md already carries.[17]

`documented` Where the boundary comes from is therefore not the memory controller: it is the buffer's depth plus the reload control of §3.4. A `LOAD` that arrives when four words are already in the buffer **discards the oldest**, which is what makes a line with an odd number of words leave the last one or two unread.[16]

`derived` A hard-scroll offset that is not a multiple of 16 changes *nothing* about the group: the fetch boundary is still every fourth `LOAD`, and the offset acts in the shift path, after the group. What it does move is the reload's alignment within the line, which is why the demos that change resolution mid-line get an offset that is not a multiple of the step, and why hard-scroll lines leave one or two words unloaded at the `DE` edges.[16][17]

### 4.2 One line as a timetable

`derived` 64 µs is 512 clocks, 256 DRAM slots and **128 windows** of 500 ns, of which the display window supplies one internal slot each. Against that grid:

| mode | pixels | pixel time | words | windows used | `DE` (clocks) | picture area (blank, clocks) |
|---|---|---|---|---|---|---|
| low | 320 | 125 ns | 80 | 80 | 56 → 376 | 28 → 448 |
| mid | 640 | 62.5 ns | 80 | 80 | 56 → 376 | 28 → 448 |
| mono | 640 | 31.25 ns | 40 | 40 | 4 → 164 | (not stated) → 184 |

`derived` The three relations this table states: the *pixel* count doubles mid over low while the *word* count does not (the words buy half as many pixels each), so the fetch schedule is identical in low and mid and halves in mono; `DE`'s window is 320 clocks in colour and 160 in mono, and those are exactly the windows whose internal slots the video client owns.
`documented` The edges are the community model's, read from the GLUE state machine: `H` true at clock 56 and false at 376 for 50 Hz, true at 4 and false at 164 for 71 Hz, `BLANK` released at 28 and asserted at 448 — and asserted at 184 in mono.[17]
`unknown` The mono raster's blank *release* is not in that table; the cell above is left blank rather than guessed, and nothing in our model depends on it (the mono screen is written inside `DE` and has no border).
`derived` The blanking that follows is where the other client lives: the 48 windows outside a colour line's `DE` carry sound DMA when a request is pending and a RAS-only refresh otherwise, which is the memo's rule seen from this side.[2][13]

`inferred` And the STE's `PRELOAD`, which the ST does not have: while `PRELOAD` is true the memory controller issues `LOAD`s *before* `H` is asserted, four in low res and one in high, "which is what makes the STE timings for raised DE match up with ST".[17]
`derived` That is the hardware reason a non-zero pixel offset can work at all: the group that would have been fetched inside `DE` is fetched early, outside the display window, so the first shifted pixels are already in the buffer when the offset asks for them.

### 4.3 Colour, end to end

`derived` The path, with each stage's evidence:

1. **Index.** Four bits, one per plane, taken at the far end of the shift chain; in medium res only the two low bits are used, in mono only bit 0 is looked at as black/white.[16]
2. **Latch.** The 4-bit index addresses sixteen 12-bit transparent latches; the lookup is asynchronous, so a palette write lands on the next pixel clock at the latest.[16]
3. **Weights.** The four bits are weighted 1-8-4-2 (§3.6), which is a rotation of the ST's 4-2-1 and is the reason `$777` is not `$777` here.[18]
4. **Pins.** Twelve digital lines leave the package: `R3–R0` on pins 38–41, `G3–G0` on 42–45, `B3–B0` on 46–49 (active high), per the manual's symbol as transcribed in `design/board-inventory.md` §3.5.
5. **Ladder.** On the board the twelve lines pass the `74S257` colour/mono mux (`U405`) into a resistor network, one per channel, whose summed node is the encoder's analogue input; the scan shows four weighted resistors per channel (values around 1.5 kΩ and 6.2 kΩ).[19]
6. **Encoder.** `U403` (MC1377) turns RGB plus the syncs into composite and feeds the DIN-13 monitor socket.[6]

`derived` So the "9-bit DAC" the round's arithmetic seeds assumed does **not** exist on this board: a 9-bit converter would be three bits per channel, i.e. the ST's 512 colours, and the STE demonstrably displays 4,096. The nine-bit figure belongs to the previous machine, and the interesting truncation here is the other way round: the *fourth* bit is the bit the ST's software never wrote, and Atari put it at the bottom of the scale rather than the top precisely so that ST programs would not turn dark.

`derived` Colour order, stated side by side as the brief asks:

| | ST (3 bits/channel) | STE (4 bits/channel) |
|---|---|---|
| register field | bits 10-8, weights 4-2-1 | bits 11-8, weights 1-8-4-2 |
| colours | 512 | 4,096 |
| `$0777` | full red | 14/15 red |
| how to get full red | `$0700` | `$0F00` |

`unknown` The ladder's exact per-bit resistor weights have not been read at drawing resolution; what is established is that four weighted resistors per channel exist and that the fourth bit reaches the analogue node. A meter on `U405`'s outputs with a known palette settles it in an evening (§7.5).

### 4.4 One palette write

`documented` A store to `$FF8240`–`$FF825E` is decoded by `CS` (pin 18) with `R/W` and the address lines; the Shifter reads the register number from `A1–A4` and takes the data from the bus.[16]
`derived` Because the register is a **transparent latch** gated by `CS`, the write is visible to the pixel pipeline while the cycle is still running — there is no capture edge at the end of the cycle, and no resynchronisation. That is the mechanism behind the Spectrum 512 effect, where the palette changes several times inside one line.[16]

`derived` Readback returns the latch, in the same 0321 arrangement it was written in — the chip does not reorder on the way out. On the ST the unused top bit of each nibble reads back as whatever the bus last left there, which is why Hatari randomises it; on the STE all twelve bits are real.[9]

`documented` Byte writes duplicate: a byte store to either half of a palette word updates **both** halves with the same byte, because the 68000 places a byte on both halves of `D0–D15` and the latch is transparent. Hatari encodes exactly that, with the worked examples `move.b #7,$ff8240` leaving `$0707`.[9]
`derived` The same transparency explains `$FF8260`'s second life: the mode register is decoded in *both* chips — one storage site for the timing mode in U400's sync block, one here for the plane mode and the clock mux — so the register is duplicated, not mirrored, and a single write must reach both. From this side the chip sees only bits 8 and 9 of the bus, which is why a word write carries the value in the high byte.[6][2]

### 4.5 One DMA-sound word

`documented` The sequence, from this end, with the memo's side referenced rather than re-derived:

1. The FIFO drops below full and the chip asserts `SREQ` (pin 36) — the request the memory controller samples; `SREQ` is a level, and the memo warns that holding it low indefinitely stops refresh.[6][2]
2. U400 grants the internal slot (outside `DE`, sound request pending) and drives `\*SLOAD` at phase 1 of that slot, with the fetched word on `MD0–MD15`.[2]
3. The chip latches the word **on the rising edge of `\*SLOAD`** — the memo's own wording, "the sound port should latch the memory data on the rising edge", and the reconstruction's edge detector agree.[2][9]
4. The word waits in the FIFO until the sample clock asks for it; the sample-rate divider runs from `SCLK` (the article's 8 MHz oscillator), giving the four documented rates — 6.25, 12.5, 25 and 50 kHz.[6]
5. On each sample the word leaves as **two bytes**, high byte first, on `SD0–SD7`, each byte captured by its own `74F374` from pins 19 and 20, so that each `DAC0802` (`U502`/`U503`) receives its own channel.[6]
6. The two DACs' outputs go through the op-amp and switched-capacitor filter chain, then the LMC1992 for volume/bass/treble and mixing, then the phono jacks.[6]

`derived` The buffering the model needs is therefore two-stage and asymmetric: a word FIFO that absorbs the burst of up to four granted slots back to back, and a byte register pair that converts 16 bits into two 8-bit lanes one sample apart. Neither exists anywhere else in our model, and both are this chip's.

### 4.6 One Microwire transfer

`documented` Writing `$FF8922` starts a transfer: the chip shifts out a 16-bit field on `MWD` at 1 Mbit/s (one bit per eight `SCLK` ticks), gating `MWK` with a rotating mask register written at `$FF8924`; the documented message is **11 bits** — two to select the circuit, three for the register number, six for the data.[6]
`derived` So the frame's *length* is the mask's business, not the chip's: with the mask Atari's own example uses (`$07FF`), eleven clocks leave the package and the transfer takes about 11 µs; the shift itself always runs sixteen bit-times.[6][9]
`documented` Writing the mask register **during** a transfer aborts it, and the reconstruction carries the reason in a comment: the Systematic Error demo depends on it.[9]

### 4.7 One fine-scroll write

`documented` `$FF8265` is the pixel offset, 0–15, and the article's "sixth address bit" is what gives this chip the decode for it — the ST part had five address bits and no such register.[6][18]
`derived` The boundary the brief asks to be stated and not split:

| register | lives in | what it does |
|---|---|---|
| `$FF8201/03/0D` video base | U400 | the address the counter reloads at the frame boundary |
| `$FF820F` line width | U400 | words skipped after each line |
| `$FF8205/07/09` video counter | U400 | the running fetch address, readable |
| `$FF8265` pixel offset, `$FF8264` prefetch | **U401** | the shift offset applied **here**, and the extra group U400 must fetch |

`derived` The offset is added in the shift path, after the group has been assembled: the chip does not fetch differently, it shifts differently, and the memory controller's only response is the one extra group that `$FF8265 ≠ 0` asks for. Nothing is split between the chips — the address arithmetic is entirely U400's and the pixel arithmetic entirely this chip's.[18][2]

`documented` Atari's own warning about the register pair is worth carrying into our model as a rule: changing the pixel offset from non-zero to zero can make the STE display the wrong address, which is why Atari told programmers to set these registers in an HBL routine rather than in the VBL handler.[18]
`unknown` Whether that is a reload race in this chip or a fetch race in U400 is not established by any source; both are plausible and the two chips share the group boundary, which is exactly where the effect would be.

---

## 5. Conflicts in the record, adjudicated

**1. "There is a die photograph" — no, there is one of the *other* Shifter.** `documented` ijor's schematics and waveforms are the ST Shifter's, traced from a decap of that part; the STE's part has never been decapped publicly.[16]
The transfer rule used throughout: structure that the STE did not change (shift array, reload control, clock mux, palette latches, the reset condition) may be taken from his work as `documented`-for-the-family and `inferred`-for-this-part; anything the STE added (hard scroll, the 12-bit palette, sound DMA, the sixth address bit) may not.

**2. "The DAC takes 9 bits" — rejected.** `derived` The palette is 12 bits, the package has twelve RGB pins, the board has four weighted resistors per channel, and the machine displays 4,096 colours. Nine bits is the ST's 512-colour path.
The seed's truncation question has a different answer here: the truncation is *in the palette write*, not the ladder — the fourth bit exists and Atari placed it at the bottom of the scale.

**3. `SCLK`/`FCLK` direction.** `documented` The article has `SCLK` as an 8 MHz **input** from a separate oscillator which the chip divides to make `FCLK` for the sound filters, and states that pulling `SCLK` leaves the machine running with no sound.[6]
Board-inventory §3.5 has pin 33 `SCLK` as `out` and pin 34 `FCLK` as `in`, and §4 has `U400`'s `CLK8` feeding `FCLK`.
`derived` The article's reading is the one that makes the divider story coherent, and the reconstruction's audio divider can only divide a clock it receives. The inventory rows should be corrected to `SCLK in`, `FCLK out`, with the note that the manual's sheet 4 settles it if anybody re-reads it.

**4. `LD`/`HD` direction.** `documented` The article: "each one is connected through a 74F374 … which transmits its 8 bits at reception of a signal (`\*LD` and `\*RD`) transmitted obviously by Shifter (pins 19 and 20)".[6]
Board-inventory lists pins 19 and 20 as inputs. `derived` They are outputs: the chip drives two latch strobes to split its own 8-bit sound bus into two channels, which is the only way the pin budget of §3.1 makes sense.

**5. Two pins named.** `documented` The article fills two of the inventory's illegible pins: **pin 73 is `LOWP`, not used** on this board, and **pin 51 `COLOR` is a colour-clock output (NTSC), not composite video** — it feeds the modulator stage on the models that have one.[6]
`derived` That reclassifies pin 51 from the inventory's "colour/mono mode select" to "colour clock out", and it means a composite-video mod on an STE is a component addition, not a pin.

**6. `$FF8260` exists twice — confirmed from this side.** `documented` The article is explicit: "at address FF8260 correspond two registers one in the shifter and one in the GLUE", on the same bus bits 8 and 9.[6]
The U400 study's §6.7 stands unchanged, and the reconstruction's shared decode (`A == 6'h30` catching both `$FF8260` and `$FF8261`) is its own simplification, already recorded in `design/video.md` §10.6.

**7. The sound-DMA split — confirmed.** `documented` Counters in the GSTMCU, requester and FIFO and byte split here.[6] The U400 study's §9 note that the word pipeline may live in the Shifter is right, and the two halves are not two claims about one register: they are the two ends of `SREQ`/`\*SLOAD`.
`derived` Nothing about the sound path is in U300 (the DMA controller) on this board: it is a two-chip mechanism, and the DMA controller's own model stays out of it.
`documented` **Confirmed from the third end, 2026-11-21** (`research/dma-controller-implementation.md` §4.7): U300's
40-pin symbol is complete and carries no `SREQ`, no load strobe and no clock but the 8 MHz one it shares with
the FDC, so the finding is now closed by a pin census as well as by the counter side and by the requester.

**8. Five pins still unnamed.** `unknown` Pins 35, 52, 55, 72 and 76 are drawn on the symbol but not legible in the transcription. One of them is almost certainly the internal reset condition's second input (§5.9), since the chip has **no reset pin** at all — the symbol lists none.

**9. Which ijor findings transfer, and which do not.**

| finding | transfers? | why |
|---|---|---|
| 4×16 array of two-flip-flop cells | yes | the STE extended the array's wiring, not its size |
| IR→RR transfer always 4 words | yes | it is the array's depth, not a mode |
| reload needs 4 loads **and** 16 pixel clocks | yes | the STE's hard scroll is built on exactly this |
| palette registers are transparent latches | yes | the article adds no palette register and ijor's copy is the family's |
| mono select is a second copy of colour 0 bit 0 | yes | the reconstruction models the same latch |
| mode 3 stops the pixel clock | yes | the demos that use it are STE-era |
| "SHIFTER resets when both LOAD and CS are asserted" | probably | it is the only reset this chip can have (§5.9) |
| the ST Shifter's offset arithmetic (K) | **no** | the STE's offset is a register, not a resolution switch |
| nothing about hard scroll, sound DMA, 12-bit palette | **no** | STE-only |

**10. The repository's own `atari-st-hardware` GST-Shifter page is not a source.** `documented` `atari-st-hardware/components/video/02-gst-shifter-c029145.md` describes a 64-pin QFP part numbered `C029145` with MCM 16-colour and Super Hi-Res modes, an integrated DAC, and "16 pixels per 16-bit word" in low res. The board's part is an 84-pin PLCC `C300588-001`, `C029145` is the TT's Shifter, no fourth resolution exists (settled in `research/ste-super-hires.md`), and the DAC is on the board, not on the chip.
`derived` The page should carry a dated caution of the same shape as the Super Hi-Res correction; it is not evidence for anything here, and one of its wrong claims — a chip-integrated DAC — is the origin of the "9-bit DAC" seed this study rejects.

**11. `design/video.md`'s mono delivery count.** `derived` The file says "Per-line work is `DisplayBytes / 4` deliveries — 20 on a low-res line, 20 in medium (80 words), 40 in mono". A mono line is 80 bytes, which is 20 deliveries of 4 words, not 40 — and 400 mono display lines × 10 (a mono line's real group count, 40 words) is what keeps the frame at 4,000.
The `40` is a slip in both places it appears; the *rate* (4,000 groups a frame, 16,000 chunk writes in mono) is right, and this study's §4.1 supplies the arithmetic. Corrected in a dated block in `design/video.md`, 2026-11-21.

**12. "16 pixels per group" in mid res.** `derived` The GSTMCU study's line-budget table reaches 80 words for a mid line by "640/16 = 40 × 2 planes", which is a right total by the wrong route: a mid-res word carries **16 pixels from one plane**, so a line needs 40 words *per plane* and 80 in all — twenty groups of four, each covering 32 pixels. The table's number is correct, its label ("40 × 2") is not a group count, and no conclusion in either study changes.

---

## 6. What the model needs

`derived` Ranked by what it costs us when wrong. Tiers per the campaign.

| # | question in our core | the claim that answers it | tier | home |
|---|---|---|---|---|
| 1 | Is a group 16 pixels or 4 words? | four words = 2 µs; 16/32/64 pixels by mode; the frame is 4,000 groups either way | T2 (register wording + array depth) | `design/video.md` §3 |
| 2 | What is the palette's bit order? | weights 1-8-4-2, so `$0777` is 14/15 | T1 (Atari's own table, reproduced by two implementations) | `design/video.md` §4 |
| 3 | Is a palette write instantaneous? | yes — transparent latch, visible during the cycle | T2 (ijor, and Hatari's byte-duplication behaviour needs it) | `design/video.md` §4 |
| 4 | Where does a byte palette write land? | both halves of the word, same byte | T3 (Hatari) → T1 by §7.2 | `design/video.md` §4 |
| 5 | Does `$FF8265` change the fetch? | one extra group per line in U400, the shift here | T2 (register + GSTMCU §5.2) | `design/video.md` §6 |
| 6 | What does mode 3 do? | stops the pixel clock; GLUE reads it as high res | T2 (ijor, the article's `VIDEOMOD` split) | `design/video.md` §4 |
| 7 | How deep is the sound FIFO? | unknown; four words is a carried figure | **T3** | Sound |
| 8 | When is a sound word latched? | rising edge of `\*SLOAD`, memo and RTL agreeing | T2 (memo) | `design/crossings.md` |
| 9 | Is the mono border black by palette or by bypass? | palette bypassed, bit 0 of colour 0 selects inversion | T1 (Atari: "in monochrome the border colour is always black") | `design/video.md` §5 |
| 10 | Does an ST program's `$0777` still look like full red here? | it is 14/15 — a 7 % step down, invisible in a picture and a visible error in a one-step fade | T1 (Atari's fade table) | palette tests |

`derived` Two of these change the model's *shape* rather than a constant: #1 fixes the delivery count in mono to ten a line, and #2 means the palette record's stored value must be written in the register's own field and rotated only at the pixel, exactly as Hatari's table and the reconstruction do — the rotation is not a display nicety, it is the hardware's.

---

## 7. The experiments that would settle the rest

Six tests, each a TOS program or a bootable `$4E71` filler on the real 1040STE, with a scope on a named pin or a read-back through a named register.

1. **The palette's bit weights.** Write `$000`, `$888`, `$111`, `$999` to colour 15 in four successive frames and scope one RGB pin's analogue node.
*Pass:* `$888` gives the **dimmest** level (1/15) and `$111` the second (2/15) — bit 11 is weight 1 and bit 10 is weight 8. The reversed order (`$111` dimmer than `$888`) falsifies §4.3 and moves the rotation in the model.
2. **Byte-write duplication.** `move.b #7,$FF8240`, read back; then `move.b #$55,$FF8241`, read back.
*Pass:* `$0707` then `$5555` — the transparent latch reading both halves of the bus. A single-half result means the palette is edge-clocked and §4.4 is wrong.
3. **Palette latency inside a line.** A Spectrum-512-style routine writing colour 15 several times in one line, with the RGB pins on a scope.
*Pass:* the colour changes at the pixel the write lands on, with no group-boundary quantisation.
4. **The offset's extra group.** With `$FF8265` = 0 and then = 8, scope `\*LOAD` and count pulses inside `DE`.
*Pass:* 80 in both cases for a low-res line, one more group fetched before `DE` in the offset case (the `PRELOAD` of §4.2).
5. **The ladder.** A meter on `U405`'s three outputs while stepping the palette through `$000`, `$200`, `$400`, `$800`, `$F00` per channel.
*Pass:* five distinct levels with the documented weighting; this is the measurement that turns §4.3's `unknown` into a table.
6. **Microwire frame.** Scope `MWK` and `MWD` during the `$07FF`/`$07FF`/command sequence of Atari's own example.
*Pass:* eleven clocks, 1 Mbit/s, and an MWE envelope that ends with the last clock — and a second run writing `$FF8924` mid-transfer showing the abort.

`derived` Test 1 is the cheapest and settles the most: it converts the only load-bearing palette claim in the study from documented-in-a-magazine to measured.

---

## 8. What is still unknown

- **The shipped mask.** No decap exists, so nothing here can be attributed to a revision of this part. Unlike the GSTMCU, there is not even a drawing set to compare against.
- **The sound FIFO's depth.** No source states it; four words is Steem's figure, the reconstruction's own comment argues only that two is too few. Settling it needs a board: `SREQ` is the chip's own output, so software cannot starve the buffer to count its depth — the measurement is a logic analyser on pin 36 under a frame written to overrun.
- **The ladder's per-bit weights.** Four resistors per channel are established; their values, read from a scan rather than a drawing, are not.
- **Which pins 35, 52, 55, 72 and 76 carry**, and therefore where the internal reset condition's second input comes from.
- **Whether the `$FF8265` non-zero-to-zero bug is this chip's or U400's** (§4.7). Both are plausible and the group boundary is shared.
- **`LOWP` (pin 73).** The article says unused on the STE; what it does on the parts that use it is not stated anywhere.

**Not modelled — by ruling, not by doubt:**

- **No fourth resolution.** Settled 2026-11-11 in `research/ste-super-hires.md`; `MWE`/`MWD`/`MWK` are the Microwire link, not a memory-write path. Nothing in this study reopens it.
- **The analog tail.** `U403`, `U405`, the two sound DACs, the filters, the LMC1992's analog behaviour: the model stops at the digital pixel and at the digital sample, and the host renders.
- **Microwire bit-level timing in the machine.** The chip's timeline is the audio one and the register's date is the ordering (owner's ruling on #27); a transfer is one event, and its 1 Mbit/s frame is a property of the chip documented here, not a thing our engine schedules.

`inferred` What a decap would give: the mask revision, the array's exact geometry and the two remaining pin names — not any of §7's timing answers, which a scope gives for free and cheaper.

---

## 9. Licence position

`documented` No image is vendored and no excerpt from a proprietary drawing is reproduced: ijor's schematics are photographs of his own work and are referenced by URL only, Atari's CAD archive contains nothing about this part, and the service manual's symbol is cited through the repository's own transcription.[16]
`derived` The two kinds of borrowing above are the same two the GSTMCU study allowed itself: transcriptions of published statements (the array's organisation, the reload control's rule, the palette's weights) and measurements (the line's edges, the group arithmetic) — each cited, none reproduced as a figure.
The studies remain repo/private research per the owner's ruling of 2026-11-21; if any of this publishes, the quotations stay (they are a forum author's and a magazine's words, not Atari's drawings) and the register tables must be re-sourced from Atari's own documents.

---

## Sources

[2] https://www.chzsoft.de/asic-web/ASICs-new.zip — ASICs-new.zip — recovered Atari CAD archive (Zietz + fpgaarcade)
[4] https://siliconpr0n.org/archive/doku.php?do=search&id=atari — siliconpr0n archive — search result for 'atari'
[5] https://siliconpr0n.org/archive/doku.php?id=furrtek:atari:c301719-001 — siliconpr0n — furrtek:atari:c301719-001 (Lynx Mikey)
[6] http://info-coach.fr/atari/hardware/STE-HW.php — Info-Coach — Atari STE hardware (GST MCU chapter)
[9] https://github.com/gyurco/gstmcu — gyurco/gstmcu — simulation model of the Atari STE GSTMCU
[12] http://visual6502.org/images — visual6502 — photographed-parts index (die shots)
[13] https://www.atari-forum.com/viewtopic.php?t=22673 — Atari-Forum — "MMU control of CPU/BLITTER versus Shifter access cycles" (refresh on every MMU phase that is not a video access)
[16] https://www.atari-forum.com/viewtopic.php?t=29658 — Atari-Forum — "ST Chipset decap" (ijor's reverse engineering thread, 2016-2022)
[17] https://temlib.org/AtariForumWiki/index.php/ST_STE_Scanlines — AtariForumWiki — ST STE Scanlines (GLUE/Shifter state machines, Troed et al.)
[18] https://temlib.org/AtariForumWiki/index.php/Atari_STE_FAQ_compiled_by_The_Paranoid_/_Paradox — Atari STE FAQ (The Paranoid/Paradox) — register tables and STE pitfalls
[19] http://info-coach.fr/atari/hardware/video.php — Info-Coach — Atari Video Presentation (STE video schematic, monitor output)
