# Handoff — the same silicon research, one Atari STE chip at a time

**Goal.** Repeat, for every physical chip on the 1040STE board, the work just done for the 68000: find out
what is actually known about the part's *implementation*, separate measurement from claim, and leave the
repository with a cited study plus whatever images and drawings the evidence supports.

**The reference case is complete and is the template.** Read these three before starting a chip:

| what | where |
|---|---|
| the study (and the method it demonstrates) | `research/m68000-silicon-implementation.md` |
| the pictures, the annotation and the licence discipline | `research/68000-silicon/die/` (`README.md`, `SOURCES.md`, `annotate.py`) |
| the naming study, and the five sheets | `research/68000-silicon/nomenclature.md`, `research/68000-silicon/schematics/` |

The method itself is captured as the skill **`research-silicon-chip-implementation`** — load it first, and
`technical-schematics` before drawing anything.

**Non-goals.** No transistor-level simulation of an Atari custom, no gate-level netlist, no claim of a
block boundary that the evidence does not support. If a part has no public decap, the deliverable is an
honest "no die shots exist; here is what the schematic, the datasheet and behavioural probing establish",
which is a perfectly good study.

---

## 1. The board's chips, from our own inventory

From `design/board-inventory.md` §2 (part numbers as printed in the 1991 service manual, assembly
CA400329-101). Designators are the board's; do not invent new ones.

| ref | part | package | role | research value |
|---|---|---|---|---|
| U100 | C398061-001, **68000** @ 8 MHz | 68-pin PLCC | CPU | **done** — reference case |
| U400 | C300589-001, **GSTMCU** | 144-pin PGA/PLCC | glue + MMU + DMA arbitration + video timing + DRAM control + pad/pen ADC + blitter control | **highest** — everything the STE does that an ST cannot |
| U401 | C300588-001, **GST Shifter** | 84-pin PLCC | bitplane shift, palette, DE/BLANK, hardware scrolling, Microwire | **highest** — video is where the STE differs most |
| U300 | C398739-001, **DMA controller** | 40-pin DIP | disk DMA and ACSI: a device port with a FIFO and a request line — **no address pins, no interrupt, no reset, and no sound DMA** (corrected 2026-11-21; the sound path is a two-chip mechanism between U401 and U400) | **done** — `research/dma-controller-implementation.md` |
| U101 | C101643, **Blitter** | 68-pin PLCC | blitter (discrete on this board; merged into GSTMCU later) | high — masks/skew/line mode are fiddly |
| U203 | C025984, **68901 MFP** | 48-pin DIP | timers, USART, interrupt controller, GPIO | medium — datasheet-rich, but timer quirks matter |
| U301 | C026028-002, **WD1772** | 28-pin DIP | floppy controller | medium — command/step timing |
| U200/U201 | C025985, **6850 ACIA** ×2 | 24-pin DIP | IKBD link (in scope), MIDI (out) | low-medium — link timing only |
| U202 | C025983, **YM2149** | 40-pin DIP | PSG + two I/O ports | low — well documented already |
| — | **HD6301V1 "IKBD"** | 40-pin, on the keyboard PCB | keyboard/joystick/mouse MCU + Atari firmware | medium — protocol and scan matrix, not silicon |

Also worth a paragraph each, not a study: the TOS 1.62 ROM pair (U102/U103) and the 74LS164 serialiser
(U900), both already covered by `research/tos-rom-sourcing.md` and `design/board-inventory.md`.

---

## 2. What to find out, per chip (the seven questions, adapted)

Ask the same seven of every part; the answers will be thinner for some, and that is the finding.

1. **Images.** Do die photographs exist? Which mask, which layer, what resolution, what licence? (See §3.)
2. **Clocking and counting.** What clocks does it take, what does it generate, and what actually counts —
   a real counter, a shift register, or a state machine clocked by the bus?
3. **Control structure.** Microcode, PLA, or random logic? Atari's customs are gate arrays/standard
   cells with no microcode, so this question usually resolves to "a PLA or a state machine" — say which,
   with the evidence.
4. **Registers and latches.** The programmer-visible register map is in Atari's hardware docs; the
   *internal* latches are the interesting part and usually unknown.
5. **Datapath.** Width, byte lanes, shifters, FIFOs, prefetch, and where the 16-bit bus is split.
6. **Documented vs real behaviour.** Undocumented registers, write-only mirrors, aliasing, the quirks
   emulators encode but never explain. Every one of these is a candidate for a labelled claim.
7. **What the emulator needs.** End with a short list: for each open question in our core, the claim that
   answers it and its tier (T1 hardware-captured / T2 hardware-corrected / T3 emulator-derived).

---

## 3. Where to look, in order

| source | what it has | how to use it |
|---|---|---|
| **siliconpr0n.org** (John McMaster's archive) | decaps of many 1980s parts, by part number, with delayered series | search the archive wiki by part number *and* by Atari house number; ask on the visual6502/CPU-Shack forums if absent |
| **visual6502.org/images** | a photographed-parts index with layer sets | check the index page, not just the 65000 pages you know |
| **Sean Riddle's decap pages**, `caps0ff`, **CPU Shack / CPU-MUSEUM**, `qmtpro.com/~nes/chipimages` | hobbyist decaps, often of game/console customs | Atari's ST chips are the kind of part these collections hold — check before concluding "none exist" |
| **bitsavers** | datasheets for the standard parts (68901, 6850, WD1772, YM2149) | primary source for anything the part itself defines |
| **Atari service manual + 1989 schematic set** | pin numbers, net names, block boundaries | ours are pinned in `design/board-inventory.md`; that is the ground truth for *connectivity* |
| **MAME / Hatari / Steem SSE sources** | behavioural models of exactly these chips | use as a checklist of what must be explained, never as evidence of silicon |
| **our own harness** | the only instrument that can settle a timing question on this board | a targeted test ROM beats a forum post |

If a decap exists, everything in `research/68000-silicon/die/README.md` applies unchanged: metal-only vs
delayered, vectorised layers, ROM dumps, and the rule that **a vision model cannot tell you where a block
is** — snap to printed lines or don't draw boxes.

---

## 4. Per-chip plans

Each is a session or two. The bullet list is the *question set*; the deliverable is a study with labelled
claims, not answers in a chat.

### U400 GSTMCU (C300589-001) — **done, 2026-11-21**, `research/gstmcu-silicon-implementation.md`

**Sheets:** `research/gstmcu-silicon/schematics/` — `00-blocks`, `01-cycle`, `02-line`, `03-address`,
`04-counters`, generated by `build_schematics.py` (self-auditing for label overflow) with `README.md` and
`index.html`. Functional only; no floorplan, because the part has no die photograph and the design gives no
geometry. This is the pattern the remaining chips follow.
*(operating section added the same day: §5 "The chip in operation" — one slot, one line, one frame, one bus
cycle, one register write, read off sheets 48–49 and the netlist's strobe equations.)*

What the session established, in the form later chips can reuse:

* **No decap exists and none is needed.** The recovered design (`TA-4081`/`ST-4081`/`TXL`, from Zietz's
  archive) is machine-readable: a gate-count list (15,394 transistors, 50 cell types), two netlists, 33 + 14
  sheets with a text layer, the cell-library macros, and a **design memo** that specifies the sound channel.
  The public decap archives hold five Atari parts and all are Lynx or 7800.
* **Two findings change the later plans.** (a) `TXL/DWG/SMOD.TXT` — the April 1988 memo — states the sound
  DMA's register semantics, slot policy and pin timings (`SINT` is a 480 ns pulse; the frame boundary is
  `count == top`; refresh only when `XSRQ` is high). Read it before the DMA chip's study, because the same
  wire is specified from both ends. (b) The archive contains the Blitter as `TA-4021`/`ST-4051`/`ST-4082`
  (image-only, plus `4082.LST` a gate list) and **no drawing at all of the STE Shifter, the DMA controller,
  MFP or YM2149** — so U401 and U300 are emulator-and-hardware studies, and the Shifter's published
  schematics are ijor's, not the archive's.
* No figure was drawn: the design gives no die geometry and a gate-count floorplan would be invented.
* Six hardware tests, with pass criteria, are specified in §8 of the study; they settle the refresh/sound
  ordering questions that no document can.
* **Operating description: done** ([`gstmcu-operating-description.md`](gstmcu-operating-description.md), the
  follow-up handoff) — the study now walks the chip through a slot (`RAS` at `TIME0`, `CAS` at `TIME2`, `WE`
  in the external half, `LATCH` one clock wide), a line (128 internal slots, 80 to video in low/mid res), a
  frame (313 × 512 clocks = 20.03 ms), a bus cycle (`DTACK` clocked on `ICLK8B`) and a register write (decode
  on sheet 3, separate read/write nets). **Refresh is closed the same day** (§3.4): the address counter
  takes one row per 2 µs off the 500 kHz tap, walks a 512-row bank in 1.02 ms, and the cycles wait for
  blanking — a budget of ≤32 of a line's 48 slots. What is left of §5's unknowns is two: the phases' width
  and `LATCH`'s payload.

### U400 — original question set (kept for reference)
* **What it is:** the STE's combined GLUE + MMU, plus DMA arbitration, video timing, DRAM control, the
  blitter's control side and the pad/pen ADC. Two dies' worth of function in one package.
* **Questions:** the register map and its aliasing (`$FF8000`–`$FF82FF` region); DRAM refresh and RAS/CAS
  generation; the arbitration order between CPU, blitter, DMA and video fetch, and what a stolen cycle
  actually costs; video timing (50/60 Hz, DE/BLANK, line/frame counters); the MMU's mapping granularity
  and how a read-modify-write crosses it; the blitter's control FSM (whose data path is U101 on this
  board); the ADC's bit width and conversion timing.
* **Evidence:** service-manual nets (`design/board-inventory.md` §3.4 already has the 144-pin symbol),
  Atari hardware register documentation (ST Internals / Compendium), MAME's `atarist` GSTMCU model,
  Hatari's `video.c` (video and DMA timing) and `mfp.c`, and our own `design/subsystems-spec.md`.
* **Trap:** the same register block is documented differently by every emulator. Where three disagree,
  that is a §11-style conflict table, not a coin toss.

### U401 GST Shifter (C300588-001) — **done, 2026-11-21**, `research/gst-shifter-implementation.md`

What the session established:

* **This is the first part of the campaign with no primary source at all** — no decap of any STE custom,
  and no sheet for this chip in the recovered CAD set. The study says so up front and builds on the service
  manual's 84-pin symbol, **ijor's reverse engineering of the earlier ST Shifter** (the family's only real
  documentation, and a *different* chip), Atari's register wording, the board article and the reconstruction.
* **The sheet is drawn, as a set of five.** `research/gstshifter-silicon/schematics/` — blocks, one group
  (buffer, shift array, reload control), the colour path, one line as a timetable, and the register seam —
  each sheet standalone, with an index page, a README and one generator. No floorplan is claimed anywhere:
  this part has no die, so the drawings are functional diagrams, and the README says so.
* **Three findings change the arithmetic or the model.** (a) The "sixteen-pixel group" is a **two-microsecond
  grid**: the transfer is always four words into a 64-bit shift path, so a group is 16 low-res pixels, 32
  mid, 64 mono — a mono line is **ten** groups, not forty. (b) The palette's four bits are weighted
  **1-8-4-2**, so `$0777` is 14/15 and full red is `$0F00`; the "0321" comment in the RTL and Hatari's table
  are both that arithmetic. (c) There is **no 9-bit DAC** on this board: twelve digital lines leave the chip
  into four weighted resistors per channel and the MC1377 — the 9-bit figure belongs to the ST.
* **The chip in operation** is §4: one group (four `LOAD`s, the reload control's two conditions, what a
  non-multiple-of-16 offset does and does not move), one line as a timetable (320/640/640 against 512 clocks,
  `DE` 56→376 colour and 4→164 mono, 80 words in 128 windows), colour end to end, one palette write (transparent
  latches, so a write lands inside its own cycle), one DMA-sound word (rising edge of `\*SLOAD`, FIFO, byte
  split), one Microwire transfer, one fine-scroll write — with the boundary between the chips stated, not split.
* **Six tests with pass criteria** are specified in §7; the palette's bit weights is the cheapest and settles
  the most.
* **Repository corrections landed with it**: `design/video.md` (a dated block for the mono delivery count and
  the palette weights), `design/board-inventory.md` §3.5 (a dated note for `SCLK`/`FCLK`, `LD`/`HD`, `LOWP`,
  and `COLOR`), and the U400 study's §3.4 and §6.7 confirmed from this side.

### U401 — original question set (kept for reference)
* **Questions:** the shift register and how 1/2/4-bitplane modes differ; palette RAM width and the
  colour path; hardware fine scroll and line offset registers; DE/BLANK generation and where the
  320/640-pixel boundary lives; the Microwire controller's frame timing (LMC1992 volume/bass/treble).
* **Evidence:** our `research/ste-super-hires.md` (settles what this part does *not* have), Hatari's
  shifter model, service-manual pin list (§3.5), and photographs of the board for the DAC path.
* **Deliverable worth having:** a sheet like `schematics/01-datapath.svg` but for the video path —
  bitplanes → shift → palette → DE/BLANK → monitor. **Done**: `research/gstshifter-silicon/schematics/`,
  five sheets, one generator, audited on every build.
* **Handoff: [`u401-gst-shifter-study.md`](u401-gst-shifter-study.md)** — the exact brief, the evidence
  ladder (manual pin list → ijor's schematic → Hatari), the six acceptance criteria, the arithmetic seeds
  (80 words a line, 20 groups, 16-word palette), and the method rules this week's GSTMCU work earned.

### U300 DMA controller (C398739-001) — **done, 2026-11-21**, `research/dma-controller-implementation.md`

What the session established, and what later parts can reuse:

* **The chip is a port with a FIFO, not a bus master.** The 40-pin symbol is complete and decides the
  architecture by counting: 37 signals in four ports (CPU, device, request, memory), three power pins, and no
  address output, no `DTACK`/`BERR`, no interrupt pin and **no reset pin**. It drives data and asks on `RDY`;
  the GSTMCU addresses, times and performs every memory cycle.
* **The register file is split across two packages.** `$FF8604` (device window / sector count) and `$FF8606`
  (mode / status) are U300's — it decodes `A1` and `R/W` only, so it can see exactly two registers — while
  `$FF8609/0B/0D` are the GSTMCU's 24-bit counter (22 bits implemented), written low→high and read high→low.
* **The steal is per sixteen bytes.** `RDY` is a level for one eight-word burst (32 clocks, 4 µs), 32 times a
  sector and ~39 a frame — which corrects `design/disk-path.md` §3's "six wakes a sector" to 192 and
  `design/crossings.md` §5's `RDY` rate to 234 records a frame. 0.78 % of the CPU, not "under 1 %" by luck.
* **The interrupt is not this chip's.** `FDINT` (`U400.16`) ← WD1772 `INTRQ` (`U301.28`, the inventory's
  illegible pin 28) and `HDINT` (`U400.15`) ← ACSI `_IRQ` (`J301.10`), NORed into `*DISKINT` (`U400.17`) for
  the MFP's `I05` — which is what the STE's GSTMCU consumes to stop its own counter, and what the STF lacked.
* **Directions corrected in `board-inventory` §3.6** (`FDRQ`/`HDRQ` in, `ACK` out — the symbol was drawn from
  the peripheral's side), and the sound finding closed from the third end by a pin census.
* **Four behaviours Atari documents and no emulator models**: the double strobe, the status read that can
  disrupt a transfer, the write-only sector count, and the 32-byte prefill on the sector-count write.

* **Questions the study leaves open** (each with a scope test in its §7): the shipped revision
  (`C025913-38` vs `C398739-001A`), the power-on state of a chip with no reset, the width of `FCS`, how the
  direction bit reaches U400, and the priority between `FDRQ` and `HDRQ`.

### U300 — original question set (kept for reference)
* **Questions:** the arbitration request/grant protocol with the GSTMCU; disk sector DMA (ACSI) address
  and counter registers; **STE sound DMA** — buffer addressing, the loop/reload behaviour, sample-rate
  dividers, and the exact moment the FIFO refills; what happens on a mid-transfer mode change.
* **Evidence:** MAME `atarist` DMA, Hatari `dmaSnd.c` (sound) and the floppy path, and the service
  manual's nets. Sound DMA is the one place where a test ROM against real hardware would be worth the
  effort — it is where our own core is most likely to be subtly wrong.
* **Correction — 2026-11-21** (U401 study, §5.7): the sound-DMA question above is already answered from
  both ends, and the answer is that **U300 is not in that path**. The counters are the GSTMCU's, the
  requester, FIFO and byte split the Shifter's, and the board article says so in as many words. What
  remains for this chip is the disk/ACSI path and the grant relay — read the brief before re-litigating.

### U101 Blitter (C101643) — **done, 2026-11-28**, `research/blitter-implementation.md`

**Sheets:** `research/blitter-silicon/schematics/` — `00-blocks`, `01-word`, `02-registers`, `03-window`,
generated by `build_schematics.py` (self-auditing for label overflow) with `README.md` and `index.html`.
Functional only; no floorplan, because the part has no die photograph. Same pattern as the three before it.

What the session established, in the form the remaining chips can reuse:

* **The archive's blitter is not image-only.** The directory holds *three revisions of one design* — `TA-4021`
  (Oct 1986, 15 sheets, **a 53,291-character text layer**), `ST-4051` (Mar 1988, the same 15 sheets) and
  `ST-4082` "BLT Cell" (Sep 1988, 20 sheets, redrawn with buses, and carrying the note "for use in the combo
  chip") — plus a SILOS simulation deck whose `4082C.OPF` pin file names the chip's external signals and a
  gate census with transistor counts. Since the `.DWG` sources carry their sheet text as strings, **every
  sheet's label inventory is machine-readable**; connectivity still is not.
* **`ST-4051` is the blitter, settled by its own sheet titles** (`STB - State machine`, `Source word
  alignment`, `Register selects`, `Inc and end mask regs`, `ADDRESS REGISTER CELL`, `ADDRESS LATCHES`,
  `BITBLT CELL DEFINITION`, `BITBLT CORE/ADD BUFFERS`, `Address decode and regs`, `X and Y counters`,
  `Misc I/O pads`, `RAM Address decode`, `Halftone RAM` ×2, `Cells For STB`) — no longer a title-block guess.
* **Three findings change the model.** (a) **A word is not a bus cycle**: 1–3 accesses at 4 clocks each
  (fill 4, copy 8, masked 12), so a full-screen 320×200×4 copy is 16 ms in HOG mode and ~32 ms — more than a
  frame — in the documented non-HOG mode; the campaign's `4N + 8` seed was a *fill's* cost. (b) **There is no
  line mode** on this part: no octant bits, no Bresenham accumulator, no register bit — line drawing is
  `SMUDGE` + `X COUNT = 1`, which is also the configuration with the recorded anomalies. (c) **`BINT` is not
  consumed by the GSTMCU**: the blitter's `INT` passes through to `BINT` (U400.60) and lands on **MFP `GPIP3`**,
  a level that duplicates BUSY, so completion is an edge (`AER` bit 3), not a pulse.
* **The bus-master contrast is now structural.** The pad ring carries both ends of the grant (`BGI`, `BGO`)
  and both ends of the acknowledge (`BGACKI`, `BGACK`): the blitter is a station in the chain and passes the
  bus on ("the BLiTTER will yield to other DMA devices"), where U300 has no address and no arbitration pins
  at all. Which chip is *first* (U400 or U101) is `unknown`; the net list gives no order.
* **The 63/64 window** is documented as 64 and measured as 63 sometimes; `TMOUT` exists only in the combo
  revision, so the count cannot be read from the standalone drawings. Eight scope tests with pass criteria are
  in §7, `blitemu.c` being already in the tree.
* **Repository corrections landed with it, dated**: `design/subsystems-spec.md` §1's Blitter row and §3,
  `design/board-inventory.md` §3.2 (seven pin rows that name signals this part does not have),
  `research/gstmcu-silicon-implementation.md` §6.6 and §9, and the dated caution on
  `atari-st-hardware/components/blitter/01-blitter.md`.

### U101 — original question set (kept for reference)
* **Questions:** register semantics (source/dest/X-count/Y-count/hop/op/skew/mask), the three passes and
  how the mask register is consumed, halftone RAM, line mode (Bresenham variant), and the busy/IRQ timing.
* **Evidence:** Atari's blitter documentation, Hatari `blitter.c`, MAME. A blitter is small enough that a
  behavioural model plus a handful of hardware-verified cases is a complete answer.
* **Correction — 2026-11-28:** the "line mode (Bresenham variant)" above does not exist on this part
  (study §4.6); the register question is answered by Atari's own manual, which survives complete at the 2020
  Wayback timestamp, and the "behavioural model plus a handful of cases" is what now exists on both sides.

### U203 MFP 68901, U301 WD1772, U200/U201 6850, U202 YM2149
* Datasheet-led, short studies. Only research what the datasheet does not settle: MFP timer prescaler
  edges and the I/O port's read-modify-write behaviour; WD1772 command timing and the "force interrupt"
  corner; the ACIA's ÷64 clocking to 7812.5 baud and the 74LS164 serialiser's role; the PSG's envelope
  and timer periods against the 2.4576 MHz crystal.
* One combined document is acceptable here — but keep a section per part, so a future session can find the
  answer without reading the whole thing.

### IKBD (HD6301V1, on the keyboard PCB)
* Not a die study: a 6801-family MCU running Atari firmware. Questions: the command set and its timing,
  the scan matrix, mouse/joystick packet formats, and what the ACIA link does when the IKBD is busy.
  MAME has a driver; our `design/board-inventory.md` has the wiring.

---

## 5. The pipeline, concretely

```bash
# 1. does a decap exist? (search by part number AND Atari house number)
#    siliconpr0n archive, visual6502 index, Sean Riddle, CPU Shack, qmtpro
#    If the host is dead, use the archive with the raw suffix:
curl -sS  "http://archive.org/wayback/available?url=host/path/"
curl -sSL "http://web.archive.org/web/2026id_/https://host/path/file"

# 2. vendor images with licences beside them
#    research/<chip>/die/ + SOURCES.md  (see 68000-silicon/die/SOURCES.md for the shape)
#    re-encode photographs to JPEG q90-92; never commit a 10000-px scan (link it)

# 3. annotate only if the figure already prints its own outlines
python3 research/68000-silicon/die/annotate.py     # copy, then edit BLOCKS

# 4. draw only if a reader would otherwise be lost
python3 research/68000-silicon/schematics/build_schematics.py   # copy the kit; one function per sheet

# 5. cite while drafting, then verify
S=~/.hermes/skills/research/grounded-citations/scripts/sources.py
python3 "$S" add <url> --title "..." ; python3 "$S" verify <study>.md --min-coverage 0.30
```

Deliverables land as `research/<chip>/` with the same four parts as the 68000: study, images (+licences),
nomenclature section (only if the part has internal names worth fixing), drawings (only if earned).

---

## 6. Ordering, effort, stopping rule

1. **GSTMCU** — two sessions. It gates video, DMA, blitter and memory mapping.
2. **GST Shifter** — **done, 2026-11-21**; one session for the study and one for the five sheets
   (`research/gstshifter-silicon/schematics/`).
3. **DMA** — **done, 2026-11-21**; one session, and it started from a closed question rather than an open
   one: the sound path is not in this chip, so the work was the disk/ACSI path and the grant relay. It found
   that the chip is a port with a FIFO — no address pins, no interrupt, no reset — and that the counter and
   every memory cycle belong to the GSTMCU.
4. **Blitter** — **done, 2026-11-28**, `research/blitter-implementation.md`. It was the last chip with a
   recovered design, and it turned out to have three revisions of one (and a text layer, which the record
   said it lacked). Both inherited questions are answered: the `BR`/`BG`/`BGACK` set is a full grant relay, so
   the blit's burst is a real ownership transfer through a daisy-chain station; and the non-HOG window is 64
   by Atari's own wording, with the 63 a measured anomaly that the drawings cannot arbitrate because the
   standalone revisions carry no timeout net at all.
5. **MFP / WD1772 / ACIA / PSG** — one combined session.
6. **IKBD** — half a session, behavioural only.

**Stopping rule:** stop when every open question in our core has either a labelled claim or an explicit
`unknown` with the experiment that would settle it. Do not chase transistor-level detail for a part whose
emulation is already T1-verified.

---

## 7. Decisions needed from the owner — **answered 2026-11-21**

* **Do we want die-level research at all for parts with no public decap?** → **Answered: schematic + behaviour
  + test-ROM plan for all parts; die-level only where a decap actually exists.** So U401/U300 are design-and
  hardware studies by construction, and the "no decap" paragraph is the deliverable, not an apology.
* **Is photographing a decap in scope?** → **Answered: no** — we buy and photograph nothing; record what a die
  would settle and move on (the GSTMCU study does this in §2.1 and §8).
* **Do the studies publish?** → **Answered: repo/private research** — the 68000 study's 1983 *Byte* figure stays;
  every new study states its licence position explicitly (the GSTMCU study's §9).

*Original wording of the three questions, kept for context:*

* **Do we want die-level research at all for parts with no public decap?** If not, the GSTMCU and Shifter
  studies become "schematic + behaviour" documents and the effort drops by half.
* **Is photographing a decap in scope?** If yes, which parts are worth a purchased/donated die — the two
  GST chips first, then the DMA.
* **Do the studies publish?** The 68000 one vendors a 1983 *Byte* figure under research use; if any of
  this goes public, that figure must be replaced (the licence note in `die/SOURCES.md` says how).
