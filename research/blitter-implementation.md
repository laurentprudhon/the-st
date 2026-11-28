# The Blitter (C101643) at the silicon level

### What the recovered design shows, what Atari's manual says, and the one arithmetic every model gets wrong

*Subject: the STE's blit engine, `U101` on the 1040STE board, Atari house number `C101643`, 68-pin PLCC;
the warehouse list calls it "IC ST BLITTER CUSTOM 68 PIN". Studied 2026-11-28, the fourth and last custom of
the per-chip campaign after the GSTMCU, the GST Shifter and the DMA controller.*

*Basis, and it is the best of the campaign: **this is the one part whose drawing survives, and — contrary to
what the record has carried — its drawings are readable.** The recovered archive holds three revisions of one
design under three numbers, and the text of every sheet is machine-readable: `TA-4021` (1 October 1986, 15
sheets) ships as a PDF with a text layer, `ST-4051` (18 March 1988, 15 sheets) and `ST-4082` (15 September
1988, 20 sheets) are image-only PDFs whose `.DWG` sources still carry their sheet text as strings, and the
directory also holds a SILOS simulation deck whose pin file names the chip's external signals.[2] Atari's own
*BLiTTER* manual of 25 January 1990 supplies the register semantics.[31] This study reads **sheets first**,
with the models as a checklist of what must be explained.*

*Labels: `documented`, `derived`, `inferred`, `unknown`; T1 hardware-captured, T2 hardware-corrected, T3
emulator-derived. Companion pages: `research/gstmcu-silicon-implementation.md` (the other side of `BINT` and of
the bus cycle), `research/dma-controller-implementation.md` §4.3, `design/subsystems-spec.md` §3,
`design/board-inventory.md` §3.2 and §4.1. The four sheets drawn from this study are in
`research/blitter-silicon/schematics/` — functional diagrams, and **no floorplan**, since no die photograph of
this part exists.*

---

## 1. Executive summary

| question | answer | § |
|---|---|---|
| Do die photographs or drawings exist? | **No decap — and the drawings survive, three revisions of them.** `TA-4021` is a 15-sheet text-layer PDF; `ST-4051` the same 15 sheets reissued; `ST-4082` "BLT Cell" the same machine redrawn for the combo chip, plus a SILOS deck and a gate list. | 2.1 |
| Is `ST-4051` the blitter? | **Yes — no longer `inferred`.** Its sheet titles are `STB - State machine`, `Source word alignment`, `Register selects`, `Inc and end mask regs`, `X and Y counters`, `Halftone RAM` ×2, `Misc I/O pads`, `Cells For STB`. | 3.2 |
| What is it made of? | 58 cell types, **2,133 instances**, no ROM, PLA or RAM cell: 256 `BIT` cells are the halftone pattern, the counters are `UDC2` cells, and the only adder is the **address** adder. | 3.1 |
| Microcode, PLA or random logic? | **Random logic**, the GSTMCU's cell library, no table anywhere. | 3.5 |
| What counts? | X and Y count (16 bits each, 65536 = `0`), the halftone line number (4 bits), the skew shifter (31 stages), and — in the combo revision only — a `TMOUT` net. | 3.7, 4.3 |
| One word, end to end? | source read → 32-bit skew buffer → HOP → LOP → endmask merge → destination write, at **4 clocks per access**, with 1–3 accesses per word. | 4.1 |
| What does a full-screen blit cost? | **16,000 words = 32,000 accesses = 128,000 clocks = 16 ms** in HOG mode; about twice that non-HOG, so **a full-screen 320×200×4 copy does not fit in one 20 ms frame**. | 4.2, 5.10 |
| Does a "line mode" exist? | **No.** No line bit, no octant, no accumulator: what exists is `SMUDGE` + the halftone line number, driven with `X COUNT = 1`. | 4.6, 5.2 |
| Where does the interrupt go? | U101's `INT` → U400 → **`BINT` (U400.60) → MFP `GPIP3`**; the manual says the line *is* BUSY, so it is a **level**, not a pulse. | 4.5 |
| What is still open? | The 63-or-64 window, the grant-chain order between U400 and U101, and the shipped mask. | 8 |

`derived` The one-sentence machine, assembled from §3 and §4 below: *a bus master with one 16-bit data path and one 23-bit address adder takes
the bus from the CPU, reads a source word, shifts it right by 0–15 bits against a 32-bit remainder, merges it
with a halftone word and the old destination word through a 16-entry truth table, masks the result with one of
three end masks, writes it back, and moves its address by a signed 15-bit increment — counting words per line
and lines per blit, and giving the bus back to the CPU every 64 accesses unless told to hog it.*[31][33]

`derived` And the correction that reorganises the campaign's arithmetic (§4.2): **a "word" is not one bus cycle.**[33] A
copy reads the source and writes the destination — two accesses, eight clocks — and a masked or
destination-dependent LOP adds the destination read, three accesses, twelve clocks; the brief's seed
(`4N + 8`) is the cost of a **fill**, and every throughput figure built on it is half the truth.

---

## 2. The evidence, and what each layer can answer

### 2.1 The drawings survive, and this time they are readable

`documented` The archive's blitter directory holds three designs, not two, and the record's summary of it — "image-only, no
text layer, no netlist", with `4082.LST` as the only machine-readable artefact — is wrong in four ways:[2]

| artefact | what it is | readable how |
|---|---|---|
| `4021.pdf` — `TA-4021`, 15 sheets, Oct 1986 | **the same 15-sheet design as ST-4051**, earlier revision | **a text layer: 53,291 characters** — titles, net names, notes, cell types |
| `4051.pdf` — `ST-4051`, 15 sheets, Mar 1988 | the `STB` chip: state machine, datapath, registers, pads, halftone RAM | the `.DWG` sources carry their sheet text: 3,321 distinct tokens |
| `4082.pdf` — `ST-4082` "BLT Cell", 20 sheets, Sep 1988 | the same machine **redrawn with bus notation** (`SKEW<3:0>`, `aDD<15:0>`, `DHOP<1:0>`) | `.DWG` strings: 2,399 distinct tokens |
| `SIM/4082C.OPF` | the SILOS simulation deck's signal list | **the external pin set in text**: `c-a23…c-a0`, `c-data15…c-data0`, `c-fc2..0`, `c-rw`, `c-as_`, `c-uds_`, `c-lds_`, `c-dtack_`, `c-br_`, `c-bgo_`, `c-bgack_`, `c-int_` |

`derived` So the evidence statement is not "no netlist, only a count": it is **"no netlist, but every sheet's
text."**[2] Connectivity still has to be read from a drawing — the strings are a label stream, not a netlist, and
the record's rule that a vision model invents geometry still holds — but a sheet's *inventory* (which nets,
which cells, which notes) is now machine-readable, and about a third of this study's structural claims come
from it rather than from a reader's eye.[2]

`documented` The three revisions are one design, and the third is not for this board: `ST-4082`'s second sheet
carries the note *"It will require minor modifications for use in the combo chip"* — the combo chip is the
GSTMCU, designed two months later.[2] `derived` That fixes the family tree: `TA-4021` (Oct 1986) → `ST-4051`
(Mar 1988) → `BLT` (Jul 1988, 5 surviving sheets) → `ST-4082` (Sep 1988, the cell for the GSTMCU), all three
describing one block structure and one register set (§3.2).

`documented` What is *not* in the archive is any netlist for the blitter: the GSTMCU has two P-CAD `PINLIST`
files, and the blitter's directory has drawings, a gate census and nothing else.[2] `derived` That is the
structural reason §4.3's window question cannot be closed from the design, and it is a better reason than "the
sheets are too dense to read".

### 2.2 Atari's manual is the register semantics, and it is short

`documented` *User Manual for the Atari ST Bit-Block Transfer Processor (BLiTTER)*, 25 January 1990, revision
2.3, source file `blitter.me,v 2.2 87/06/17` — so the prose is 1987 and only the front matter was reissued.[31]
It has 27 pages: an introduction citing Newman & Sproull and the VL16160 RasterOp chip, a functional
description, the register map and semantics, a worked 68000 programming example, and an appendix on the
extended BIOS call.[31]

`documented` Its host is dead and the campaign's note recorded a truncated download; the snapshot at
`web.archive.org` serves **the complete file** for the 2020 timestamp (3.6 MB, 27 pages) while the 2023
timestamp truncates at exactly 1 MB, which is what the note saw.[31] `derived` The manual's most load-bearing
sentences are three, and each contradicted something the campaign had been carrying: the HOG bit's definition
("each will get 64 bus cycles while the other is halted"), the BUSY bit's third sentence ("The interrupt line
is a duplicate of this bit"), and the absence of any line-drawing mode from a document that covers the part's
entire register set.[31]

### 2.3 The models are a checklist, and they disagree on one number

`inferred` Hatari's `blitter.c` is the densest model and it says where its knowledge comes from: "deduced from
studying many cases on a real STE … as well as my own tests on an STE", with a header that lists the four
cases it can predict (the `MOVE`/`BSET`/`MULU`/`DIVU` overlap) and the one it could not.[33] `inferred` It
encodes `BLITTER_NONHOG_BUS_BLITTER 64` with the comment "Can also be 63", and `BLITTER_NONHOG_BUS_CPU 64`,
and its September 2020 update says the `xcount = 1` anomaly "should be correctly emulated … based on reverse
engineering and Verilog implementation made by Jorge Cwik".[33]

`inferred` Cwik's `stBlitter.sv` in the MiSTer core counts 64 from BUSY and calls the discrepancy a
"buglet".[32] `inferred` NeoST's `Blitter.cpp` is a third independent model with pixel etalons.[35] `derived`
Neither is a witness: Cwik's Verilog is the same author as `gyurco/gstmcu`, Hatari's header says it came from
hardware experiments and demo behaviour, and NeoST's etalons are its own output — so Atari wins every
disagreement with a model, and §5 files the models' disagreements with each other.

`documented` What no model implements: the `BERR` input's abort path (the design has a `BERR` pad and the state
sheet consumes it, §3.4), the `DS`-style strobe `XIDS` on the pad ring, and any interrupt output at all —
Hatari's `INTERRUPT_BLITTER` is an internal scheduler slot, not the `BINT` wire.[33][2]

### 2.4 The board, which is the only instrument

`documented` `U101` is a discrete 68-pin PLCC on this revision; on the later `CA4003290` boards the function is
inside U400 and `U101` is not fitted.[2] The nets that matter are in `design/board-inventory.md` §4.1: `D0–D15`
(pins 1–5, 58–68), `A1–A23` (32–51, 53–55), `DTACK` (10), `BR` (13), `BG` (11), `FC0–2` (28–30), and six pins
the transcription never resolved — 6, 7, 8, 9, 18, 31.[2]

---

## 3. The chip the record shows

### 3.1 Size, counted

`documented` `4082.LST`, dated 15 September 1988, is a cell census in the same four-column form as the
GSTMCU's `4081GATE.DOC` — cell, quantity, transistors each, transistors total — over **58 cell types and
2,133 instances**:[2]

| cell | qty | T each | total | what it is |
|---|---|---|---|---|
| `BIT` | 364 | 12 | 4,368 | the bit cell (latch + tristate output) — halftone RAM and shift path |
| `I1`/`I3`/`I6`/`I8…` | 277+167+22+… | 2–32 | 2,548 | inverters |
| `NA2`/`NA3`/`NA4`/… | 114+43+21+… | 4–16 | 1,340 | NAND |
| `SAXB`/`SAB` | 191+51 | 6 | 1,452 | half-adder bit — the `UDC2` counter's arithmetic |
| `LL`/`LC`/`LCR`/`LHR` | 89+45+2+1 | 12–14 | 1,632 | transparent latches |
| `TB1`/`TBN`/`TBP` | 150+16+23 | 6–18 | 1,602 | tristate buffers — the bus drivers |
| `DP`/`DPR`/`DPS`/`DN…` | 35+7+1+7 | 22–32 | 1,368 | D flip-flops, with and without reset/set |
| `XO` | 50 | 10 | 500 | XOR — the LOP's conditionally-inverted operands |
| `DAN` | 36 | **9999** | — | variable-width register (the library's "width unknown" placeholder) |
| `MAND`, `AN3333`, `SCR`, `NO2`… | | | 726 | complex gates, scan cells, NORs |
| **fixed cells** | **2,097** | | **15,706** | |

`derived` Two things follow. First, **the blitter and the GSTMCU are the same size class** — 15,706 against
15,394 transistors — which says this is not a small add-on but a quarter of the memory controller's logic in a
package of its own.[2] `derived` Second, **the file's own total row does not reproduce**: it prints
`Total: 375670 93917`, and 375,670 is exactly the naive sum with the 36 `DAN` rows counted at the library's
`9999` placeholder (36 × 9,999 = 359,964 of it).[2] `derived` The true count therefore exceeds 15,706 by 36
variable-width registers, **no transistor total can be quoted for this part**, and the census is comparable to
the GSTMCU's cell for cell rather than in total.

`derived` The cell mix says what the GSTMCU's did: 444 inverting cells, 178 NANDs, 50 XORs and 23 complex gates
against **no memory cell at all**.[2] `derived` The `BIT` count is the storage — 364 bits, of which 256 are the
halftone pattern and the rest the source/remainder registers and the end-mask latches.

### 3.2 Three revisions, one block map

`documented` `ST-4051`'s sheets title themselves, and the 15 titles are the chip's block diagram:[2]

| sheet | title | what it is |
|---|---|---|
| 1 | `STB - State machine` | the sequencer: `BUSY`, `HOG`, `DMARQ`, `DIP`, `STMOD`, `BLTRES` |
| 2 | `STB - Source word alignment` | the skew shifter: `SI0–SI30`, `XS0–XS15`, `SKEW0–3`, `NFSRCLK` |
| 3 | `STB - Register selects` | decode: `SXINCR`, `SYINCR`, `DXINCR`, `DYINCR`, `LEMR`, `REMR`, `CEMR`, `INCR` and their write strobes |
| 4 | `STB - Inc and end mask regs` | `INC1–INC15` + `INCSIGN`, `EM0–EM15` |
| 5 | `STB - ADDRESS REGISTER CELL` | the `BLTADD` adder bit, with a note on the last three passes through it |
| 6 | `STB - ADDRESS LATCHES` | `AO1–AO9`, `AI1–AI9`, `BLTAD1`, `XSEL04/06/12/14` |
| 7 | `BITBLT CELL DEFINITION` | the LOP/HOP cell, with a note listing six changes |
| 8 | `STB - BITBLT CORE/ADD BUFFERS` | `DO0–DO15`, `DI0–DI15`, `EM*`, `HOP0–1`, `OP0–3`, `XCI`/`XCO` |
| 9 | `STB - Address decode and regs` | **the register seam**: `XSEL00…XSEL1E`, `XOPW`, `XHOPW`, `XSKEWW`, `XLINEW`, `XSMUDGE`, `XFXSR`, `XRAMR`/`XRAMW` |
| 10 | `STB - X and Y counters` | `LSDCA–D`, `RSDCA–D`, `NEXTLINE`, `XSEL16`, `XSEL18` |
| 11 | `STB - Misc I/O pads` | the pad ring (§3.4) |
| 12 | `STB - RAM Address decode` | `LINE0–3`, `LM1–9`, `R0R/R0W … R9R/R9W` |
| 13, 14 | `STB - Halftone RAM` | 16 words × 16 bits, read and write ports per word |
| 15 | `Cells For STB` | the macro sheet |

`derived` Read as architecture — fifteen titles, read off the sheets themselves — that is a sequencer, a skew shifter, four increment registers, three end-mask
registers, an address adder and its latches, a 16-LOP/HOP datapath, two counters, a 16-word halftone RAM with
its own address decode, and a decode block that turns the bus into sixteen register strobes — with no FIFO, no
prefetch and no arbitration beyond the grant relay.[2] `derived` The whole machine fits in 15 sheets, which is
why it is much simpler to model than its neighbour the GSTMCU.[2]

`documented` The third revision keeps the block structure and changes the drawing style: `ST-4082` uses buses
(`SKEW<3:0>`, `DHOP<1:0>`, `LINE<3:0>`, `DESTLATCH`, `XCNT`, `YCNT`) instead of one net per bit, and adds the
signals that only make sense inside the combo chip — `WANTDMA`, `-DISK$`, `TMOUT`, `LTMOUT`, `HOG`.[2]
`derived` That is the design's own record of where the arbitration went when the blitter stopped needing pads:
in the standalone part the bus question is one wire pair, and in the combo version it becomes a three-way
negotiation with the disk DMA.[2]

### 3.3 The registers, as the decode draws them

`documented` Atari's map, with the design's own strobe where one survives:[31][2]

| address | register | width | manual's words | design net |
|---|---|---|---|---|
| `$FF8A00–$FF8A1E` | HALFTONE RAM, 16 words | 16 | "a 16×16 halftone pattern mask … repeated every 16 lines" | `XSEL00…XSEL1E`, `XRAMR`/`XRAMW`, `LINE0–3` |
| `$FF8A20`/`$FF8A22` | SOURCE X / Y INCREMENT | signed 15 | "the least significant bit is ignored" | `SXINCR`/`SYINCR`, `XSXINCW`/`XSYINCW` |
| `$FF8A24`/`$FF8A26` | SOURCE ADDRESS (long) | 23 | "only word addresses may be specified" | `AI*`/`AO*`, `BLTADD` |
| `$FF8A28`/`$FF8A2A`/`$FF8A2C` | ENDMASK 1, 2, 3 | 16 | first / any other / last write of a line | `LEMR`/`REMR`/`CEMR`, `XLEMW`… |
| `$FF8A2E`/`$FF8A30` | DESTINATION X / Y INCREMENT | signed 15 | as source | `DXINCR`/`DYINCR`, `XDXINCW`/`XDYINCW` |
| `$FF8A32`/`$FF8A34` | DESTINATION ADDRESS (long) | 23 | as source | `DAW`, `DESTLATCH` |
| `$FF8A36` | X COUNT | 16 | "words in one destination line; minimum one, maximum 65536 designated by zero" | counter bit select `XSEL16` |
| `$FF8A38` | Y COUNT | 16 | "lines in the destination field" | `XSEL18`, `YC1` |
| `$FF8A3A` | HOP | 2 | halftone/source combination | `XHOPW`, `XHOP0–1` |
| `$FF8A3B` | OP (LOP) | 4 | source/destination combination | `XOPW`, `XOP0–3` |
| `$FF8A3C` bit 7 | BUSY (read) / start (write) | 1 | "set after all the other registers have been [written]" | `BUSY`, `STB` |
| `$FF8A3C` bit 6 | HOG | 1 | 64 accesses each, or hog | `HOG` |
| `$FF8A3C` bit 5 | SMUDGE | 1 | low four bits of the skewed source select the halftone line | `XSMUDGE` |
| `$FF8A3C` bits 0–3 | LINE NUMBER | 4 | the current halftone word | `XLINEW`, `LINE0–3` |
| `$FF8A3D` bits 0–3 | SKEW | 4 | right shift before the merge | `XSKEW0–3`, `XSKEWW` |
| `$FF8A3D` bit 7 | FXSR | 1 | force an extra source read per line | `XFXSR`, `FXSR` |
| `$FF8A3D` bit 6 | NFSR | 1 | no final source read per line | `NFSR`, `NFSRCLK` |

`derived` The arithmetic the brief asked for is in the addresses: **`$FF8A00`–`$FF8A3D` is 62 bytes, which is 31
word locations, of which the first sixteen are the halftone RAM and the last is one byte-wide word holding LINE
NUMBER, SKEW, FXSR and NFSR.**[31] `derived` Two independent facts fall out of it: the decode must produce 16
word selects at 2-byte steps — and the design does, `XSEL00, 02, 04 … 1E`, exactly sixteen — and every
register above the halftone block sits at an even address *except* the four byte fields packed into
`$FF8A3A`–`$FF8A3D`, which is why the manual writes `HOP` at `$3A` and `OP` at `$3B` instead of giving each a
word.[2][31]

`documented` Readback is not symmetric with write: X COUNT and Y COUNT "return the number of destination
words/lines **yet** to be written, not necessarily the value initially written", BUSY reads the running state,
unused bits read back as zeros, and the byte registers must be accessed as bytes.[31] `documented` The
write-0-to-BUSY semantics are the manual's — stop with 0, resume with 1, BUSY still reading 1 — and the
hardware case is Hatari's recorded comment that "writing '0' will not end the current blitter transfer,
reading 'busy' bit 7 will still return '1'".[31][33]

### 3.4 The pin set, and the corrections it forces

`documented` The pad ring's own nets, from `TA-4021` sheet 11 and `ST-4051` sheet 11:[2]

| group | nets on the pads |
|---|---|
| data | `XDI0`–`XDI15` (in), `DO0`–`DO15` (out), `DATA0`–`DATA15` |
| address | `AI1`–`AI23`, `AO1`–`AO23`; the simulation deck's list also carries `c-a0` |
| bus control | `XAS`, `XUDS`, `XLDS`, `XIDS`, `XIRW`/`BRW`, `XCS`, `XDTACK`, `XBERR` |
| cycle | `FC0`–`FC2`, `ICLK`/`XICLK` |
| arbitration | `XBGI`/`BGI`, `XBGO`/`XIBGO`, `XBGACK`, `XBGACKI`, `XBR` |
| interrupt/reset | `XINT`, `XRESET`/`XRES` |

`derived` **This is the contrast with the DMA controller, and it is stronger than "it drives addresses".** The
pad ring carries *four* arbitration nets: a bus-grant **in** (`BGI`), a bus-grant **out** (`BGO`), and a
bus-acknowledge **in** (`BGACKI`) and **out** (`BGACK`).[2] `derived` A single 68000 master needs three of the
four; the fourth exists only if the part is a **station in the grant daisy chain** that passes the grant down
to the next device and receives its acknowledge back — and Atari's own sentence is written for exactly this
logic: "In either case the BLiTTER will yield to other DMA devices."[31]

`unknown` Which station is first on this board is settled by no document: `design/board-inventory.md` §4.1
lists the three nets with six endpoints (U400.26–29 and U101.11, .13) but not their order. Both are
electrically possible — CPU → U101 → U400, or CPU → U400 → U101 — and they differ in what "the blitter takes
the bus directly" means; §7.7 is the scope test.

`derived` Three rows of `design/board-inventory.md` §3.2 do not survive this study and are corrected there with
a dated note, as follows.[2] (a) Pins 19/21/23/24/25/26/27 are transcribed as `HALT`, `VMA`, `VPA`, `BERR`,
`IPL2`, `IPL1`, `IPL0`, **none of which exists on the design's pad ring**: the chip has `BERR` (an input — the
state sheet consumes it) and one `INT` pin, and it has no `HALT`, `VMA`, `VPA` or `IPL*` at all, because it does
not halt the CPU, it takes the bus from it, and it has no interrupt-priority encoder to drive.[2] (b) `FC0–2`
are transcribed as inputs; as a bus master the chip must drive them, and the pad nets are written
`FC0`/`FC1`/`FC2` beside output drivers.[2] (c) The six pins the transcription could not read (6, 7, 8, 9, 18,
31) are the natural home of `AS`, `UDS`, `LDS`, `R/W` — which §4.1 lists for U101 without pin numbers — plus
the grant relay and `INT`.[2] `derived` That last assignment is `inferred`, not read; §7.7 and a re-read of the
600 dpi sheet settle it.

### 3.5 No table of any kind

`inferred` There is no ROM, no PLA and no RAM cell in the census, and no description of the part mentions an
array other than the halftone pattern.[2] The `BIT` cells are latches with tristate outputs, the counters are
built from `UDC2` — a one-bit up/down cell of two half-adders, a latch and a tristate driver — and the LOP/HOP
cell is the `BITBLT` macro instantiated once.[2] `derived` This is the same finding the GSTMCU study reached by
counting a cell library, reached the same way, and it means the blitter's behaviour is its wiring: every
published model of it is somebody's reading of these sheets or of a machine.

### 3.6 Storage: 256 bits of halftone and no more

`documented` The halftone RAM is 16 words of 16 bits — 256 `BIT` cells — with a read port and a write port per
word (`R0R`/`R0W` … `R9R`/`R9W` on sheets 13–14) and a 4-bit line decode (`LINE0–3`, `LM1–9`,
`XSEL00…XSEL1E`).[2] `derived` At four bitplanes one word is **one 16-pixel row of the pattern**, sixteen words
make a 16 × 16 block, and since the line number steps once per destination line and wraps, the pattern repeats
every 16 lines at every plane.[31][2] `documented` `SMUDGE` turns the pattern from a screen-space texture into a
per-pixel lookup: "the least significant four bits of the skewed source data [are] used as the **address** of
the current halftone pattern", which is a rename of the line number and is why the halftone operation "is still
valid when SMUDGE is set".[31]

`derived` The end masks are storage too, and they are the reason a masked blit costs a read: three 16-bit
registers (`LEMR`, `REMR`, `CEMR`) hold which bits of the destination will be updated, and the merge
`(result & mask) | (old & ~mask)` needs the old word whenever the mask is not all ones.[2][31] `derived` The
LOP's operand needs and the mask's needs are separate conditions, and Hatari's model is the clearest statement
of it: `need_dst = lop_needs_dst || end_mask != 0xFFFF`.[33]

### 3.7 The datapath

`documented` The skew shifter is drawn on sheet 2 as `SI0…SI30` — **31 stages** — fed 16 bits at a time
(`XSI0…XSI15`), with a 4-bit `SKEW0–3` selecting the tap and `NFSRCLK` timing the second half of the
double-word load.[2] `derived` That is the hardware for the manual's sentence about non-aligned sources: "we
may need to read the first two words into the 32-bit source buffer and use the 16 bits that line up with the
destination, as specified by the SKEW register … When the next source word is read, the lower 16 bits of the
source buffer is transferred to the upper 16 bits and the lower is replaced by the new data."[31] `derived` A
31-stage register is a 32-bit window minus its output tap, and the reversal for a negative X increment is a
direction bit on the same shifter — which is why `SMUDGE` and the `xcount = 1` anomaly of §4.6 are both
properties of this one block.[31]

`derived` The only arithmetic in the chip is the **address** adder: `BLTADD` (23 bits, `BLTAD1` as the low bit)
adds the 15-bit sign-extended increment (`INC1–INC15`, `INCSIGN`) to the running address, and the designer's
note on sheet 5 counts the inverters it took to keep the adder from loading the data bus.[2] `derived` The data
path has no adder at all: halftone, source and destination meet in `BITBLT`, which is a 16-entry truth table
(`XOP0–XOP3`) plus a 4-entry one for the halftone/source merge (`HOP0–HOP1`), with 50 `XO` cells doing the
conditional inversions.[2]

### 3.8 What is not in this chip

- **Any line-drawing machinery** — no octant bits, no accumulator, no line-mode bit (§4.6, §5.2).[31]
- **Any memory timing** — no `RAS`/`CAS`, no refresh, no multi-master priority: the cycles are the GSTMCU's, and
  this chip only asks and, when granted, drives.[2]
- **A FIFO or a prefetch** — the source buffer is a shift register, not a queue; the read-mix-write loop is
  synchronous with the bus.[2]
- **An interrupt controller** — one output pin, and the manual's own description of it is a copy of BUSY.[31]

---

## 4. The chip in operation

### 4.1 One word, end to end

`derived` The mandatory path, with the register each field comes from and the bus cycle each step costs:[31]

| step | what happens | register / net | bus accesses |
|---|---|---|---|
| 1 | source word fetched at `SOURCE ADDRESS`, unless `HOP` names only the halftone or the LOP names neither operand | `$FF8A24`, stepped by `$FF8A20` | 1 read (4 clocks) |
| 2 | the word loads into the 32-bit remainder and the 16 bits at `SKEW` are taken; on a line's first word `FXSR` forces this read even when it is not needed | `$FF8A3D` bits 0–3, 7; `SI0–SI30` | (already spent) |
| 3 | halftone word selected by LINE NUMBER, or by the skewed source's low nibble when `SMUDGE` is set — a RAM read, not a bus read | `$FF8A3C` bits 0–3, 5; `$FF8A00–1E` | 0 |
| 4 | `HOP` merges source and halftone into one operand: all ones / halftone / source / source AND halftone | `$FF8A3A` | 0 |
| 5 | destination word read — **only** if the LOP names the destination or the end mask is not `$FFFF` | `$FF8A32`, `$FF8A28–2C` | 0 or 1 read |
| 6 | `LOP` combines the operand with the destination through the 16-entry table | `$FF8A3B` | 0 |
| 7 | the end mask merges the result into the old word: ones written, zeros kept | `$FF8A28` first, `$FF8A2A` middle, `$FF8A2C` last | 0 |
| 8 | the word is written back, then the address steps by `$FF8A2E`, or by `$FF8A30` when X COUNT expires | `$FF8A32/2E/30`, `$FF8A36/38` | 1 write (4 clocks) |

`derived` So a word costs **4 clocks when nothing is read (the fill case), 8 when one operand is read, 12 when
both are** — the manual's "a read will only be performed if it is required", counted per operand — — and that table, not a single constant, is the model's unit cost.[33] `derived` The classic
implementation error is to spend one `4` per word and only discover it when the frame budget refuses to close
(§4.2).

`documented` **Fill mode is not a separate mode**: it is `OP = 0` or `OP = F`, and the manual says so — "only the
LOGIC OP block … and the ENDMASK block are in the data path … the BLiTTER will simply write one word after the
other to the destination address **without ever reading the destination**".[31] `derived` With `HOP = 0` (all
ones) as well the source is not read either, and the whole blit is writes — which is the rectangle fill, the
`LINE F` acceleration and every TOS solid-fill path.[31]

### 4.2 One blit as a timetable

`derived` The sequence of a started blit, with each interval's source:

| t | what owns the bus | clocks | source |
|---|---|---|---|
| 0 | the CPU finishes the control-register write; BUSY is set at its end | 0 | the manual: "the BUSY bit is set after all the other registers have been [written]"[31] |
| 0 → 4 | **the CPU**, which can still access the bus — the window in which part of the next instruction runs | 4 | Hatari's measured "4 cycles latency between when the busy bit is set and when the blitter asks for a bus grant"[33] |
| 4 → 8 | **nobody** — arbitration | 4 (8 on a Mega STE) | both models' arbitration constant[33] |
| 8 → | the blitter, 4 clocks per access, `k` accesses per word (§4.1) | 4k | the manual's 64-access windows, Hatari's per-read/per-write constants[31][33] |
| end | the blitter releases; the CPU's next access may start | 4 | Hatari's release constant, symmetric with the acquire[33] |

`derived` A full-screen blit, and this is where the record's arithmetic has to be corrected: a 320 × 200 screen
at four planes is **20 words per line × 200 lines × 4 planes = 16,000 words**.[6]

| operation | accesses per word | accesses | clocks | at 8 MHz | vs a 20 ms frame |
|---|---|---|---|---|---|
| fill (`OP` 0/F, no reads) | 1 | 16,000 | 64,000 | **8 ms** | 40 % |
| copy, LOP names one operand | 2 | 32,000 | 128,000 | **16 ms** | 80 % |
| masked copy (end mask ≠ `$FFFF`) | 3 | 48,000 | 192,000 | **24 ms** | 120 % |

`derived` Then the non-HOG rule applies: "each will get 64 bus cycles while the other is halted", so the
blitter's wall time is `ceil(accesses / 64)` windows of 256 clocks plus the CPU's equal share — **the copy
takes about 32 ms, more than a frame**, and a masked copy nearly two.[31] `documented` The manual says the same
from the other end: "BitBlts in this shared mode may take twice as long as BitBlts executed in hog mode."[31]
`derived` So the brief's seed is right for a fill and wrong for a copy by a factor of two, and the campaign's
sanity check passes in the right direction: with `4N + 8` a 16,000-word copy would be 8 ms, and a model built
on that number would run every full-screen blit at twice the real speed.

### 4.3 One bus window

`documented` Non-HOG mode, in the vendor's words: "the HoG bit, when cleared, causes the processor and the
BLiTTER [to] share the bus equally. In this mode each will get 64 bus cycles while the other is halted. When
the bit [is] set, [it] will cause the processor to be halted until the transfer is complete. In either case the
BLiTTER will yield to other DMA devices. Bus arbitration may allow the processor to execute one or more
instructions even in hog mode."[31]

`derived` 64 accesses is **256 CPU clocks = 32 µs** at 8 MHz, and it alternates with 64 CPU accesses — a duty cycle, not
an average, and the right way to model it is as a phase, exactly as Hatari does with one counter each and the
blitter's state preserved across the hand-over, because the release can fall in the middle of a word's
read-mix-write.[33]

`documented` The 63 case is the campaign's open conflict: Hatari's header records that on a real STE the
blitter sometimes does only 63 accesses, and gives the mechanism — "in non-hog mode the blitter will always
count bus accesses as soon as [the] busy bit is set, even when it has not started to transfer its own data. So,
if the CPU does a bus access during the 4 cycles latency … this CPU bus access will be counted by the blitter
as a blitter bus access".[33] Cwik's RTL counts from BUSY and calls the difference a buglet.[32]

`derived` **The design does not settle it, and this time the reason is structural**: the count would live on the
state-machine sheet, and the standalone revisions (`TA-4021`, `ST-4051`) have **no timeout net at all** — the
string `TMOUT` does not appear in either — while the combo revision has `TMOUT` on sheet 1 beside `WANTDMA`,
`-DISK$`, `DMARQ` and `HOG`.[2] `derived` Two readings are consistent with what is visible: the discrete part
counts nothing and releases the bus on another condition, or the count exists as a `UDC2` chain that carries no
name of its own, as sheet 10's counters carry none.[2] `unknown`, therefore, with §7.1's scope test as the only
route — which is what the archive's own follow-up predicted.[2]

### 4.4 One register write

`derived` A write to `$FF8A3C` is decoded by `AS` and the address lines against `CS`; the chip takes `D8–D15`
(one byte, on both halves of the 68000 bus) and the write reaches three storage sites at once: BUSY's start
edge, the HOG latch, and the SMUDGE/line-number latch.[31][2] `derived` Three consequences a model must have:

1. **Writing 1 to bit 7 starts a transfer only if the registers are consistent** — BUSY is set *after* the
   others are written, so a start write is the last of a sequence, and a start with `Y COUNT = 0` completes at
   once (Hatari models that by clearing BUSY and HOG immediately, with a `TODO : check on real STE`).[31][33]
2. **Writing 0 stops and writing 1 resumes from the same word** — the state is preserved, which is why Hatari
   keeps its whole word state across a pause and why the manual's restart loop can use `tas` without losing the
   blit.[31][33]
3. **Reading `$FF8A3C` returns BUSY, HOG, SMUDGE and the current line number**, so a poll of BUSY is a poll of
   bit 7 of a live register and has no side effect on the transfer.[31]

`derived` The three end masks are selected by *position in the line*, not by a register: ENDMASK 1 for the
first write, ENDMASK 3 for the last, ENDMASK 2 for everything between — and when X COUNT is 1 the first and
last write are the same write, which is why the manual's example merges them into ENDMASK 1 for a single-word
destination line.[31] `derived` A model that selects by `word_index == 0` and `word_index == xcount` is right;
one that uses `== xcount - 1` writes one word with the wrong mask (§4.7).

### 4.5 One interrupt

`documented` Atari: "The BUSY bit is set after all the other registers have been set … It will remain set until
the transfer is complete. **The interrupt line is a duplicate of this bit.**"[31] `derived` So the chip's `INT`
pin is a **level that follows BUSY** — asserted for the whole transfer, released when Y COUNT expires — not a
completion pulse, and a model that emits one pulse per blit is modelling something else.

`documented` Where it lands, which closes a question the U400 study left open: `design/board-inventory.md` §3.4
has `BINT` as U400 pin 60, an **output**, and §4.1 names the net `BINT / SINT` as "blitter interrupt / sound
interrupt"; Hatari's MFP says "3 : STF/STE : blitter, active low (0=IRQ set, 1=IRQ not set)" with
`MFP_INT_GPIP3`.[2][33] `derived` The chain is therefore **U101 `INT` → U400 → U400.60 `BINT` → MFP `GPIP3`**,
and the GSTMCU does not consume it at all: it is a pass-through, which is why Info-Coach could not find a use
for it inside the GSTMCU and why the U400 study's "not modelled by ruling" is the right ruling rather than an
omission.[6]

`derived` What software sees: because the line is a level, an interrupt needs an **edge**, and the 68901's `AER`
selects rising or falling per GPIP bit — the falling edge being the blit's completion.[33] `documented` Neither
Atari's manual nor the board article says so, and no emulator implements it; the practice in TOS's own code is
to **poll**, and the manual's example does exactly that, `tas` in a loop until BUSY clears.[31] `derived` So the
study's claim is: polling is what the software does, GPIP3-with-`AER` is what the hardware allows, and `BINT`'s
polarity is §7.3's measurement.

### 4.6 One "line" — the case the brief misnamed

`documented` **There is no line mode in this part.** Atari's map has no unused bit that could hold one —
`$FF8A3C`'s bit 4 is marked "not used" and Hatari masks it out — the manual never mentions line drawing,
Bresenham or octants, and neither Hatari nor NeoST has the concept.[31][33][35] `derived` What line drawing *is*
on this chip is `SMUDGE` plus `X COUNT = 1`: with one word per line, one destination line at a time, the
source's low four bits pick the halftone row, so a software Bresenham DDA can write one word per line and let
the pattern follow the pixel.[31] `derived` That is the Mega ST line-drawing trick, and it is why the one-word
line is the configuration with two recorded anomalies:

- **`xcount = 1` with `NFSR` set** — Hatari: "the blitter produces 'strange' results when each line is only 1
  word (xcount=1) and NFSR is set at the same time, depending on whether [the] src X increment is >0 or <0",
  with the September 2020 update that it should now be right, from Cwik's Verilog.[33][32] `inferred` The
  design explains why that corner is fragile: with one word per line, `FXSR`/`NFSR` decide whether the line's
  single source read happens, and the shifter's direction bit comes from the sign of the X increment, so §3.7's
  two mechanisms are both at their extremes on the same word.[2]
- **`X COUNT` and `Y COUNT` both 1** — a single-word blit, where ENDMASK 1 and 3 are the same write and the
  manual's example merges them.[31]

`derived` The two bits that *do* exist per line, and that a model must implement, are **`FXSR`** (force the
extra source read at each line's start, to prime the remainder) and **`NFSR`** (suppress the last source read of
each line).[31] `documented` The manual adds the consequence that is easy to miss: "If the read is suppressed,
the [destination] read is performed on the destination for the last write of each line regardless of the value
of the corresponding ENDMASK register" — `NFSR` *adds* a destination read.[31]

### 4.7 One mask, worked by hand

`derived` The rules, from the manual's semantics: the mask is a bit-per-pixel picture of the word with the most
significant bit at the left; ones are written and zeros keep the old destination; ENDMASK 1 covers the first
word of a line, 3 the last, 2 everything between; `SKEW` is the right shift applied before the merge.[31]

`derived` Take a one-plane, two-line blit: the destination starts at pixel 3 of a word and is 350 pixels wide,
so it ends at pixel 352, and `X COUNT` is 22 (`13 + 21×16 = 349` pixels in 22 words), `Y COUNT` 2, `SKEW` 0:

| word of the line | mask | value | what it does |
|---|---|---|---|
| 1 (first) | ENDMASK 1 | `$1FFF` = `0xFFFF >> 3` | writes pixels 3–15, keeps the three already in the word |
| 2 … 21 (middle) | ENDMASK 2 | `$FFFF` | writes the whole word |
| 22 (last) | ENDMASK 3 | `$F000` = `0xFFFF << 12` | writes pixels 0–3, since the line ends at pixel 3 of word 22 |

`derived` The arithmetic a model can be checked against, as four rules:

1. **`X COUNT` counts the words, first one included.** A line of `n` words has `X COUNT = n`, and the words
   are numbered 1…n; `n = ceil((phase + width) / 16)`. The 350-pixel example is the off-by-one the brief asked
   for, and it is a *pixel* off-by-one: 349 bits fit in 22 words and the 350th needs the 22nd word's fourth
   bit, so a model that computes `ceil(350/16) = 22` gets the right count by the wrong route and one that
   computes `350/16 + 1 = 23` blits a word too many.
2. **The last-word mask depends on the end pixel's phase, not on the width.** For an end at pixel `k` of the
   word, ENDMASK 3 keeps pixels `0…k`, i.e. `(0xFFFF << (15 - k)) & 0xFFFF`.[31]
3. **If `X COUNT = 1`, ENDMASK 1 is the mask for the only write** — the manual's example sets it to the AND of
   the left and right masks.[31]
4. **A mask that is not `$FFFF` costs a destination read**, so the example's middle words are 8-clock words and
   its first and last are 12-clock words; with `SKEW ≠ 0` the source read is doubled at the line's start unless
   `NFSR` suppresses it (§4.6).[31][33]

`unknown` Atari's own appendix disagrees with rule 2 by one pixel: its `rt_endmask` table, indexed by
`DST_XMAX mod 16` and inverted at use, yields `k` ones rather than `k+1`, which would leave the last pixel of
every line unwritten.[31] Two readings are possible — the table is indexed by the pixel *after* the last, or the
published table is wrong — and nothing in the archive decides it; §7.4 measures it in one run.

---

## 5. Conflicts in the record, adjudicated

**1. The non-HOG window: 63 or 64?** `documented` Atari says **64** ("each will get 64 bus cycles while the other
is halted").[31] Hatari models 64 with a documented 63 on real hardware and a mechanism for it.[33] Cwik counts
64 from BUSY and calls the difference a buglet.[32] `derived` The design cannot arbitrate it (§4.3), so the
ruling is: **64 is the documented rule and the model's default; 63 is a measured anomaly with a named
mechanism**, and the study records both rather than choosing.[31][33]

**2. "Line mode".** There is none (§4.6). `derived` The brief's premise came from the Amiga's blitter, which has
`BLTCON1` bit 1 as `LINE` with octant bits; this part has no equivalent and no free register bit for one.[31]
What the brief calls "the extra source fetches at each line's start, the Bresenham accumulator, the line-number
register" is three different things: `FXSR`/`NFSR` (real), a software DDA (not hardware), and the halftone line
number (real, but it indexes the pattern).[31]

**3. `BINT` is an edge.** It is not: the manual says it duplicates BUSY, and the MFP's `GPIP3` comment says the
same ("busy/idle state").[31][33] `derived` The handoff's test 3, "one pulse per blit completion", would fail on
working hardware; the corrected criterion is a level that rises at start and falls at completion.[31]

**4. `BINT` is consumed by the GSTMCU.** It is not: `BINT` is U400 pin 60 **out**, into the MFP's `GPIP3`
(§4.5).[2][33] `derived` The U400 study's "not modelled by ruling" stands and is now explained rather than
merely recorded.

**5. `U101`'s pin list.** `HALT`, `VMA`, `VPA` and `IPL0–2` are not on this chip, `BERR` is an input and
`FC0–2` are outputs (§3.4).[2] `derived` The inventory's rows 19, 21, 23, 25, 26, 27 are therefore not those
signals, and the six unread pins are where the bus strobes must be.

**6. "A bus master" — true, and more.** The pad ring has both ends of the grant and of the acknowledge (§3.4), so
the blitter is a **relay station** in the arbitration chain, not just a requester.[2] `derived` That sharpens
the DMA study's contrast: U300 has no address pins and no arbitration pins and asks the GSTMCU for every cycle,
while U101 has addresses, strobes and a four-wire grant chain and performs its own cycles.

**7. "The archive's blitter is image-only, plus one gate list."** The images are image-only; the *text* is not
lost (§2.1).[2] `derived` The gate list is also richer than a count: it is a full cell census with transistor
counts, of the same form as the GSTMCU's `4081GATE.DOC`.[2]

**8. The gate list's total.** Its own total row (`375670  93917`) is inflated by the 36 variably-wide `DAN`
cells counted at the library's `9999` placeholder, so no total may be quoted; the fixed-cell sum is 15,706.[2]

**9. "`$FF8A00–$FF8A3D` is 31 bytes".** It is 62 bytes, 31 words (§3.3).[31] `derived` The brief's arithmetic is
off by two, and the conclusion it drew from it — that the decode is constrained — is right for the wrong reason:
the first sixteen words are the halftone RAM and the last word holds four byte registers.

**10. The brief's cost seed.** `4N + 8` is a fill's cost, not a copy's (§4.2): a copy is `8N + 8` and a masked
copy `12N + 8`.[33] `derived` Every throughput figure built on the seed is half the truth.

**11. This repository's own blitter page is not a source.** `atari-st-hardware/components/blitter/01-blitter.md`
says the blitter lives inside a Mega STE "GST Shifter (C029145)", puts its registers at `$FFE400`, and calls the
Mega STE the only ST with one — against a discrete `U101 C101643` at `$FF8A00–$FF8A3D` on this board and a Mega
ST that had one a year earlier.[2] A dated caution of the Shifter study's shape has been added to the page.

**12. Which revision shipped.** `TA-4021` (Oct 1986), `ST-4051` (Mar 1988) and `ST-4082` (Sep 1988) all describe
this chip, and the Mega ST (1987) and the STE (1989) bracket them.[2] `unknown` — no decap exists — and it
matters only for §4.3's `TMOUT` question, since the block structure is identical across the three.[2]

---

## 6. What the model needs

`derived` Ranked by what it costs when wrong; tiers per the campaign.

| # | question in our core | the claim that answers it | tier | home |
|---|---|---|---|---|
| 1 | What does a blit cost? | `k` accesses per word, `k` = 1/2/3 from the LOP's operands and the mask, at 4 clocks each | **T2** (manual + two models' constants, arithmetic closed) | `design/subsystems-spec.md` §3 |
| 2 | Does a full-screen blit fit in a frame? | 16 ms HOG, ~32 ms non-HOG: it does not | T2 (derived from 1) | the same |
| 3 | Is the blit's start delayed? | 4 clocks of CPU, then 4 of arbitration (8 on a Mega STE) | T2 (Hatari's measurements, unrebutted) | the same |
| 4 | Is BUSY an edge or a level? | a level that *is* the interrupt line | **T1** (Atari's own sentence) | the same |
| 5 | Does the blitter need a grant from U400? | it is a station in the grant chain; the order is `unknown` | T2 (pad ring) / unknown (order) | §8, test 7 |
| 6 | What is the interrupt path? | `INT` → U400 → `BINT` (U400.60) → MFP `GPIP3`, level, edge-programmable | T2 (board + MFP model) | the same |
| 7 | Is there a line mode? | no; `SMUDGE` + `X COUNT = 1` is the line case | **T1** (the register map) | the same |
| 8 | How many reads does a masked word need? | one more than an unmasked one | T2 (the manual's "a read will only be performed if it is required") | §4.1 |
| 9 | Is the window 64 accesses? | yes, documented; 63 is an anomaly | T2 documented / T3 measured | §5.1 |
| 10 | Does `SMUDGE` still apply HOP? | yes — "the halftone operation is still valid when SMUDGE is set" | T1 | §3.6 |

`derived` Three of these change the model's *shape* rather than a constant: #1 turns one per-word cost into a
table, #2 makes a frame's blit budget a phase schedule rather than a total, and #3's four-then-four makes the
instruction after the start write partly visible — which is the point of Hatari's four worked examples and the
reason a `MULU` after the start write is cheaper than it looks.[33]

---

## 7. The experiments that would settle the rest

Each is a TOS program — `hatari/tests/blitter/blitemu.c` is already in the tree — on the real 1040STE, with a
scope on a named pin or a read-back through a named register.[34] Tests 1, 2, 4 and 7 are filed as **#103**
("U101's four open measurements"), which is where a result is recorded; nothing in this study changes without
one.

1. **The window.** Start a non-HOG blit with a known word count and scope U101's `AS` (one of pins 6–9) or
   `BGACK`; count blitter-held accesses between releases.
   *Pass:* 64; a run of 63 confirms Hatari's mechanism and falsifies "64 from BUSY is the design".[33]
2. **The 4-clock latency.** A `MULU`/`DIVU` immediately after the start write, `BGACK` on the scope and the
   instruction's own timing read from an MFP timer.
   *Pass:* part of the instruction runs before the grant; a grant on the write's own cycle falsifies §4.2.[33]
3. **`BINT`'s polarity and shape.** Scope U101's `INT` (one of pins 12, 20, 22) and U400 pin 60 across a HOG
   blit, then again with the MFP's `AER` bit 3 set for the falling edge and `IMRA` bit 3 enabled.
   *Pass:* a level asserted for the whole transfer on both pins, and exactly one MFP interrupt at completion — a
   level that dies at start, or a pulse at the start, falsifies §4.5.[31][33]
4. **The mask arithmetic.** Blit with `X COUNT = 1` and then `2`, endmasks set from §4.7's rules, to a known
   pattern, and read memory back.
   *Pass:* the first and last words masked exactly as §4.7; a one-pixel difference from Atari's appendix table
   settles §4.7's `unknown` in one run.[31]
5. **Fill mode.** A `LOP = 0` fill over a known destination, `SKEW` 0 and 15.
   *Pass:* the destination written and `AS` active for exactly `X COUNT × Y COUNT` cycles — no reads at all.[31]
6. **Stop and resume.** Write 0 to bit 7 mid-blit, then 1, and read the register.
   *Pass:* the blit resumes at the same word and BUSY reads 1 throughout.[31][33]
7. **The grant chain's order.** Scope U101's grant-in, grant-out and U400's two grant pins during a blit, and
   again during a disk DMA transfer with the blitter idle.
   *Pass:* the order reads off three traces — whichever chip's grant-out precedes the other's grant-in is first,
   and if the blitter is upstream the DMA's grant passes through both.[2]
8. **The window's phase under a busy CPU.** Repeat test 1 with the CPU in a loop of `MOVE.W` bus cycles.
   *Pass:* the CPU's 64 accesses count from the release, so the alternation is a phase and not an average — and
   if the first blitter window is one access short, Hatari's 63 is confirmed as a *counting* error rather than
   a design feature.[33]

`derived` Of the eight, test 4 is the cheapest and settles the most: it turns §4.7's mask rule and Atari's appendix table
from a disputed reading into a measured one, which is the one place where the vendor's own example is suspect.

---

## 8. What is still unknown

- **The shipped mask** — `TA-4021`, `ST-4051` or `ST-4082`; no decap of any STE custom exists (§5.12).[2]
- **The 63-or-64 window's mechanism in the design** — `TMOUT` exists only in the combo revision, and the
  standalone revisions' state sheet carries no named bus counter (§4.3).[2]
- **Which chip is first in the grant chain** — the design proves the relay exists, the net list does not order
  it (§3.4, test 7).[2]
- **The directions of `FC0–2`, `BERR` and the six unread pins** — `inferred` from function, not read (§3.4).[2]
- **Atari's right-end-mask table against its own prose** — one pixel, and both readings are defensible (§4.7).[31]
- **What `XIDS` is** — a `DS`-style strobe on the pad ring that no document mentions; either a 6800-cycle input
  or an internal `DS` formed from `UDS`/`LDS`.[2]
- **Whether Cwik's post-2020 model covers every `xcount = 1` + `NFSR` case** — Hatari says it should, and
  nothing enumerates the cases (§4.6); an exhaustive sweep of the four sign/FXSR/NFSR combinations at
  `X COUNT = 1` against `blitemu.c`'s etalons is the cheap way to find out.[32][33][34]

**Not modelled — by ruling, not by doubt:**

- **The `BERR` abort path.** The chip has the input and the state sheet consumes it; no model responds and no
  documentation describes the timing.[2] What a bus error does to the address registers mid-blit is `unknown`,
  and no test in §7 proposes to find out — the machine's memory map makes it very hard to provoke without a
  cartridge.
- **The Mega ST and Mega STE variants' extra arbitration clocks** (8 instead of 4 to acquire): Hatari's
  constant, and not this board's question.[33]

---

## 9. Licence position

`documented` No image is vendored and no sheet is reproduced: the recovered designs are cited by URL, Atari's
manual is quoted in sentences only, and the models are referenced by file and line.[2][31][32]
`derived` The two kinds of borrowing are the same two the previous studies allowed themselves — transcriptions
of published statements (the register semantics, the 64-access rule, the BUSY-equals-interrupt sentence) and
measurements (the cell census, the arithmetic of §4.2 and §4.7) — each cited, none reproduced as a figure.
`derived` The studies remain repo/private research per the owner's ruling of 2026-11-21; if any of this
publishes, the quotations stay and the design files are described rather than drawn from.

---

## Sources

[2] https://www.chzsoft.de/asic-web/ASICs-new.zip — ASICs-new.zip — recovered Atari CAD archive (Zietz + fpgaarcade); blitter directory = `TA-4021`, `ST-4051`, `ST-4082`, `BLT`, `4082.LST`, `SIM/`
[6] http://info-coach.fr/atari/hardware/STE-HW.php — Info-Coach — Atari STE hardware (GST MCU chapter)
[31] https://web.archive.org/web/2020id_/http://dev-docs.atariforge.org/files/BLiTTER_1-25-1990.pdf — Atari — User Manual for the Atari ST Bit-Block Transfer Processor (BLiTTER), 25 Jan 1990 (archived copy)
[32] https://github.com/MiSTer-devel/AtariST_MiSTer/blob/master/rtl/stBlitter.sv — AtariST_MiSTer — rtl/stBlitter.sv (Jorge Cwik)
[33] https://github.com/hatari/hatari/blob/master/src/blitter.c — Hatari — src/blitter.c, and src/mfp.c for GPIP3
[34] https://github.com/hatari/hatari/tree/master/tests/blitter — Hatari — tests/blitter (blitemu.c)
[35] https://github.com/neost/neost — NeoST — src/core/Blitter.cpp
