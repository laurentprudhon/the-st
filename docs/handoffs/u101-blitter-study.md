# Handoff — U101, the Blitter (C101643)

> **Status — executed 2026-11-28.** Study: [`research/blitter-implementation.md`](../../research/blitter-implementation.md).
> Sheets: [`research/blitter-silicon/schematics/`](../../research/blitter-silicon/schematics/) (four, plus a
> README, an index and one generator, `0 layout warnings` and `0 overlaps` on the last build).
>
> - [x] §1 the part's record re-read — and corrected: `TA-4021`/`ST-4051`/`ST-4082` are **three revisions of one
>   design**, `ST-4051` is the blitter by its own sheet titles, and `4021.pdf` **has a text layer** (53,291
>   characters) where the brief recorded none; the `.DWG` sources carry every sheet's labels.
> - [x] §2 all seven acceptance criteria: one word end to end (§4.1), a timetable (§4.2), the control register
>   bit by bit (§3.3, sheet `02`), a mask worked by hand (§4.7), "line mode" **disproved** (§4.6), the
>   interrupt path (§4.5), the operation set (§3.3, §3.6).
> - [x] §5's seeds checked: the `4N + 8` cost is a **fill's**, not a copy's (a copy is `8N + 8`, a masked copy
>   `12N + 8`); `$FF8A00–$FF8A3D` is **62 bytes / 31 words**, not 31 bytes; the 64-access window is 256 clocks
>   = 32 µs and alternates, so a full-screen copy is ~32 ms and does not fit in a frame.
> - [x] §6's `unknown`s kept as `unknown`, with the reason improved: the 63/64 count is not readable from the
>   standalone drawings because they have **no timeout net at all** (`TMOUT` exists only in the combo revision,
>   `ST-4082`), and the grant chain's order is not in the net list.
> - [x] §8's definition of done: study verified (`--min-coverage 0.30`, 31 %), sheets self-auditing, the U400
>   study's §6.6 and `BINT` note closed in place, `design/subsystems-spec.md` §1/§3, `design/board-inventory.md`
>   §3.2 and the in-repo blitter page corrected with dated notes, per-chip §4 U101 marked **done**.
> - [x] **The measurements, after a search (2026-11-28)**: the 63/64 window and the grant-chain order are
>   **already published** — Cyprian measured the split on a real STE, npomarede explained it, ijor confirmed it
>   from the RTL — so they moved from `unknown` to `measured-elsewhere` and the study's §4.3/§5.1 now state the
>   mechanism. Everything still unpublished is in [`design/emulator-limitations.md`](../../design/emulator-limitations.md)
>   §1 (B8 the `BERR` abort path, B9 the `XIDS` pad, B10 the shipped mask, B7 the `xcount=1` coverage sweep);
>   the six unread pins are a **reading** task (B4) with a published pinout that disagrees with our own
>   transcription, not a measurement.
> - [x] **The pin list, read off the drawing and confirmed by the owner (2026-11-28)**: the service manual's
>   sheet 1 is a vector page whose text layer carries the symbol's pin numbers and names, so the table came out
>   by aligning the blocks by count and the owner checked the arbitration block on a crop — `BGI` 28 in, `BGO` 37
>   out, `BGACK` 29 out, `BGKI` 40 in, `BERR` 38 in, `RESET` 39, `INT` 41, `AS/UDS/LDS/R/W` 23/24/25/26,
>   `DTACK` 27, `BR` 30, `FC0–2` 45/44/43, `CLK` 34, eight `N/C`. `design/board-inventory.md` §3.2 now carries
>   it, and the board's two grant nets (`XCPUBG`, `XBLTBG`) make the relay a documented fact rather than an
>   inference. A vision read of the same crop shifted it by one pin — the owner's eyes were the instrument.

**Goal of the next session.** Write `research/blitter-implementation.md`: the STE's blitter studied to the
standard the GSTMCU, Shifter and DMA studies reached — what it *is*, how it operates (one word, one line of
a blit, one bus window, one register write, one interrupt), what the record disputes, and what only the board
can settle.

Read first, in this order: the U101 entry in `docs/handoffs/silicon-research-per-chip.md` §4, that file's §6
item 4 (why this is the next chip, and the two questions this campaign hands it), `research/recovered-asic-designs.md`
(the archive inventory, and §"Follow-up 2" on why the non-HOG timeout is not confirmable from the drawn
design), `research/gstmcu-silicon-implementation.md` §6.6 (the 63-or-64 conflict, filed there and deliberately
left to this study), `research/dma-controller-implementation.md` §4.3 (the bus-steal contrast: the DMA
controller is *not* a master and this chip is), `design/subsystems-spec.md` §1's Blitter row and §3 (the
register block and why the blitter bypasses the GSTMCU), and `design/board-inventory.md` §3.x + §4.1 (the
68-pin symbol and the `BR`/`BG`/`BGACK` net).

---

## 1. What is already established about this part

* **This is the last chip in the campaign with a recovered design — and it has one, in pieces.** The archive
  holds `4082.pdf`, **ST-4082 "BLT CELL"**, 21 sheets, Sep 1988, image-only (no text layer, no netlist), and
  `BLTCELL.pdf`, a damaged copy with 5 surviving sheets; `4051.pdf`, **ST-4051 "STB — state machine"**
  (Mar 1988), is read from its title block as *likely* the blitter's controller, and the two together are the
  two halves such a part would have. The only machine-readable artefact in the whole directory is
  `4082.LST`, a **gate-type and count list** — the same kind of file that gave the GSTMCU its 15,394-transistor
  census (`research/recovered-asic-designs.md`).
* **So the method differs from the last two studies in a way worth naming up front**: for the Shifter and the
  DMA controller the primary source was missing and the study rested on pin lists and vendor prose; here there
  are **sheets to read**, and the *owner's eyes are the primary instrument* while the agent's are the
  arithmetic, the gate list and the crops. Deliver crops at 500–700 dpi with the row labels in frame, each with
  a question and two candidate answers.
* **A vendor prose specification exists** — Atari's *BLiTTER* document, dated 25 January 1990, the register
  semantics doc of this part. Its host no longer resolves: take the Wayback snapshot
  `http://web.archive.org/web/20230209133517id_/http://dev-docs.atariforge.org/files/BLiTTER_1-25-1990.pdf`,
  and expect a truncated download (the first attempt returned exactly 1 MB and a malformed PDF; retry with a
  range request or from a second snapshot).
* **This chip really is a bus master, which is now a measured contrast rather than a phrase.** It drives
  `BR`/`BG`/`BGACK` itself — `U101.13`/`.11` on the same net as `U400.29/.28/.27/.26` — and it drives
  `A1–A23`, `D0–D15`, `AS`, `LDS`/`UDS` and `R/W`. The DMA controller next door does none of that: it has no
  address pins and asks the GSTMCU to do every cycle (`research/dma-controller-implementation.md` §4.3, `design/board-inventory.md` §4.1). Two burst owners, two different mechanisms, and
  the asymmetry is the thing a model gets wrong.
* **The costs are documented, and they are the cheapest lever this study has**: 4 cycles to acquire the bus,
  **4 cycles per memory word read or write**, 4 cycles to release; and a **4-cycle latency between the BUSY bit
  being set and the bus request**, during which part of the *next* CPU instruction runs — which is why a
  `MULU` or `DIVU` after the start write partly overlaps the blit (`hatari/src/blitter.c`, its header comment, "as verified on a real STE").
* **The non-HOG window is the campaign's open conflict, and it belongs to this study.** Hatari models 64 bus
  accesses to the blitter and 64 to the CPU, with a documented 63-cycle case and a mechanism for it (a CPU
  access inside the 4-cycle latency being counted as a blitter access); Cwik's `stBlitter.sv` counts 64 from
  `BUSY` and its own comment calls the discrepancy a *"buglet"* (`AtariST_MiSTer/rtl/stBlitter.sv` as quoted in `research/gstmcu-reconstructions.md` §6.6). The GSTMCU study filed it as §6.6 and
  left it here.
* **`BINT` is this chip's interrupt into the GSTMCU** (pin 60), and nobody has explained what the GSTMCU does
  with it — Info-Coach could not determine its role either, and the MFP's configuration suggests it is inert.
  The U400 study's §9 records it as **not modelled by ruling**; this study inherits the note, not the
  question (`research/gstmcu-silicon-implementation.md` §9, Info-Coach's GST MCU chapter).
* **Our own in-repo page is not a source.** `atari-st-hardware/components/blitter/01-blitter.md` says the
  blitter lives inside a Mega STE "GST Shifter (C029145)", gives its registers at `$FFE400`, and calls the Mega
  STE the only ST with one. This board has a discrete **U101 `C101643`, 68-pin PLCC**, addressed at
  `$FF8A00–$FF8A3D`, and on this board it is a discrete part, not integrated (`design/subsystems-spec.md` §1, `design/board-inventory.md` §3). Correct the page with a dated caution
  of the same shape the Shifter study used for its own wrong page.

## 2. Acceptance criteria

A reader who has the study must be able to do these without opening another document:

1. **Follow one word end to end**: the source read(s) → the halftone read → HOP → LOP → the destination
   write, naming the register each field comes from, the bus cycles each step costs, and where fill mode
   removes the source read altogether.
2. **Draw one blit as a timetable**: the 4-cycle acquire, the 4-cycle latency after BUSY, N word transfers at
   4 cycles each, the release — and where inside that a non-HOG window boundary falls, and what the CPU does
   with its 64 accesses.
3. **State the control register bit by bit, and its readback**: BUSY as a `read` bit versus `start` as a
   `write` bit, the write-0-stops / write-1-resumes semantics (and that busy still reads 1), HOG, smudge, line
   mode, fill, the IRQ enable — from Atari's document, not from an emulator.
4. **Work one mask example by hand**: first word, middle words, last word, with the three endmasks and the
   skew, and the **off-by-one** between "words per line" and the count that ends a line — the arithmetic every
   blitter implementation gets wrong once.
5. **Say what line mode is**: the extra source fetches at each line's start, the Bresenham accumulator, the
   line-number register, and the measured "`xcount = 1` with NFSR set" anomaly whose model Hatari says still
   needs a rule for all cases.
6. **Say where the interrupt goes**: the blit's completion → `BINT` into the GSTMCU's pin 60 → whichever MFP
   source it lands on, and what software actually sees (the TOS/AHDI practice is to poll or to use the MFP's
   GPIP bit 3; say which and prove it).
7. **State the operation set**: the 16 LOPs and the 4 HOPs, what halftone RAM's 16 words are, and how halftone
   and fill interact in the source read.

## 3. Evidence, and what each layer can answer

| layer | what it gives | what it cannot |
|---|---|---|
| `4082.pdf` — ST-4082 "BLT CELL", 21 image-only sheets | the datapath: the mask/skew shifting, the adder, the register file | timing, priority, and nothing without a reader at a crop |
| `BLTCELL.pdf` — 5 surviving sheets, damaged | the same family, fragmentary | coverage — say which sheets are missing |
| `4051.pdf` — ST-4051 "STB — state machine" | *probably* the controller: the word loop, the line loop, the bus request | that it is the blitter's at all — the title block is a visual read |
| `4082.LST` — gate-type and count list | the size and the cell mix, comparable cell-for-cell with the GSTMCU's census | connectivity — it is a count, not a netlist |
| Atari's *BLiTTER* doc (Wayback) | the register map, the bits, the documented behaviour, the errata | the drawn design, and its host is dead |
| Cwik's `stBlitter.sv` (MiSTer) | a complete, buildable reading — and the 63/64 argument in its own comments | independence: same author as `gyurco/gstmcu`, so a transcription, not a second witness |
| Hatari `blitter.c` + `hatari/tests/blitter/blitemu.c` | behaviour to explain, the 4-cycle constants, and **an existing TOS test program** the owner can run on the machine | silicon truth — T3 by definition |
| NeoST `src/core/Blitter.cpp` + `tests/reference/*.png` | a third model and pixel etalons | T3, and its etalons are its own output |
| the board: U101, the `BR`/`BG`/`BGACK` net, `BINT` | what actually happens, with a scope | logic |

**The sheet read is the campaign's first since the GSTMCU, and it is the expensive part.** Budget the session
for two reading passes with the owner; the Shifter study's lesson is that one careful pass plus two artefacts
that agree beats three passes that disagree.

## 4. Method rules this campaign earned

Inherited, and each cost a session once:

* **Sheets first, RTL second, emulators never as evidence.** Every claim labelled `documented` / `derived` /
  `inferred` / `unknown`; `T1`/`T2`/`T3` for anything about our core.
* **Names lie, and this part is full of them.** `HOP` is not a hop, `endmask` does not end anything on its own,
  and "words per line" is not a pixel count. Read a field's consumers, never its letters.
* **A complete pin list settles architecture by counting.** The DMA study's best result came from adding up 40
  pins; here the equivalent is the register block: `$FF8A00–$FF8A3D` is 31 bytes, which is 15 words and one
  byte, and that arithmetic constrains what the decode can be.
* **The vendor's prose beats every model.** Where Atari's document and an emulator disagree, quote Atari and
  file the model's reading as T3 — the DMA study's status-bit conflict (5 sources against 2) is the pattern.
* **A part that cannot drive an address cannot perform a cycle; a part that can, must be asked politely.** The
  blitter drives `A1–A23` and takes `BGACK`, so *its* burst is a real ownership transfer with a real release,
  and the model must not treat it like the DMA's request-and-relay.
* **Check arithmetic against the part, not the sheet.** A full-screen 320×200 blit is 16 000 words behind a
  4-cycle-per-word engine, and a 64-access window is 256 CPU cycles: if a derived figure needs a millisecond
  that the frame cannot hold, the reading is wrong, not the chip.
* **Deliver crops with two candidate answers.** A 500–700 dpi crop with the row labels in frame plus "is it A
  or B, and here is what each would mean" is worth more than any open-ended ask.
* **Never let a vision model's geometry be evidence.** It reads text and levels well; it invents positions.
* **Keep sheet numbers out of citation syntax** (`LC[20]` reads as a citation and fails the verifier).

## 5. Arithmetic seeds to check, not to copy

* **4 cycles per word read or write, 4 to acquire, 4 to release** (`blitter.c`) — so a blit of N words costs `4N + 8`
  clocks plus its extra reads, and a 16 000-word full-screen blit is **64 000 cycles = 8 ms**, 40 % of a
  20 ms frame. If a derived figure puts a full-screen blit past a frame, the word count or the per-word cost is
  misread.
* **The non-HOG window is 64 bus accesses** = 256 CPU cycles = **32 µs**, and it alternates with 64 CPU
  accesses. That is a duty cycle, not a latency: model the alternation, not an average.
* **A 320-pixel line is 20 words**, not 320 — so "words per line" `= 20` and a full-screen blit is `y = 200`.
  A model that feeds pixels into that register blits 16× too much: the classic error, and the §2.4 criterion is
  there to catch it.
* **Line mode adds one source fetch per line** (the next line's first word is fetched twice), so a 200-line
  line-mode blit costs `200 × (4N + 8 + 4)` clocks — check it against Hatari's numbers before quoting it.
* **Halftone:** 16 words of 16 bits = 256 bits = a 16-pixel row at four planes; the counter pairs X with Y, so
  the pattern repeats every 16 lines. 16 LOPs, 4 HOPs — the operation fields are small enough to enumerate
  exhaustively in a test.
* **`$FF8A24` and `$FF8A32` are 32-bit addresses written as two words**, and the part is on a 24-bit bus: say
  what the upper byte does rather than assuming it is dropped.

## 6. What must stay `unknown` until measured

* **What the drawn design says about the window** — `4082.LST` is a count, not a netlist, and the sheets are
  image-only: if the 63/64 case is not readable from a crop, it stays `unknown` with a scope test (§7.3), and
  `research/recovered-asic-designs.md`'s Follow-up 2 already predicted exactly that.
* **Whether ST-4051 is the blitter's controller.** A title block read is `inferred`; a sheet whose pins match
  U101's symbol settles it.
* **The shipped mask.** No decap of any STE custom exists; the design is a revision, not the part.
* **Whether the GSTMCU consumes `BINT`** (U400 pin 60) — not modelled by ruling, so the study records the
  ruling rather than reopening it.
* **The `xcount = 1` + NFSR anomaly**, and whether Cwik's post-2020 model covers every case Hatari lists.

## 7. The experiments that would settle the rest

Each is a TOS program (or `hatari/tests/blitter/blitemu.c`, which is already in the tree) on the real
1040STE, with a scope on a named pin or a read-back through a named register.

1. **The window.** Start a blit in non-HOG mode with a known word count and scope `BGACK`; count blitter-held
   cycles between releases.
   *Pass:* 64 accesses between CPU windows; 63 confirms Hatari's mechanism and falsifies the "64 from BUSY is
   the design" reading.
2. **The 4-cycle latency.** A `MULU`/`DIVU` immediately after the start write, with `BGACK` on the scope and
   the instruction's own timing read from the MFP.
   *Pass:* the documented overlap (part of the instruction runs before the grant) appears; a grant on the
   write's own cycle falsifies §4's constant.
3. **`BINT`.** Scope U400 pin 60 across a blit with the IRQ enabled in the control register's own bit.
   *Pass:* one pulse per blit completion; a pulse with the enable clear means the enable is not the bit the
   document says.
4. **The mask arithmetic.** Blit with `xcount = 1` and then `2`, both endmasks set, to a known pattern and
   read memory back.
   *Pass:* the first and last words masked exactly as §2.4's worked example; an off-by-one moves the whole
   mask model.
5. **Fill mode.** A fill blit (no source) over a known destination with skew 0 and 15.
   *Pass:* the destination holds the halftone/LOP result with no source read on the bus — which a scope on
   `BGACK`'s cycle count can see even without the design.
6. **Stop and resume.** Write 0 to the control register's BUSY bit mid-blit, then 1, and read the busy bit.
   *Pass:* the blit resumes where it stopped, and busy reads 1 throughout — the semantics Hatari records as a
   comment from hardware.

## 8. Definition of done

1. `research/blitter-implementation.md` in the study shape (§1 summary, §2 evidence, §3 the chip as the record
   shows it, §4 the chip in operation, §5 conflicts, §6 what the model needs, §7 tests, §8 what stays unknown,
   §9 licence), every acceptance criterion in §2 demonstrably met, `verify --min-coverage 0.30` green.
2. `docs/handoffs/silicon-research-per-chip.md` §4's U101 entry updated to **done** with a one-line pointer,
   and this file's items ticked.
3. **The sheet set**, `research/blitter-silicon/schematics/`, on the GSTMCU pattern — `build_schematics.py`,
   a `README.md` saying what each sheet is evidence *for*, an `index.html`, and self-audited labels. Four
   sheets are enough: the chip as blocks, one word's datapath (source → HOP → LOP → destination), the register
   seam, and the bus window. **No floorplan** — no die photograph exists. Where a boundary is a guess, a dashed
   outline and a `T3` note on the sheet itself.
4. The neighbouring claims confirmed or corrected in place, dated: the U400 study's `BINT` note and its §6.6
   window, and `design/subsystems-spec.md` §1/§3's blitter rows (the register list is already there — check it
   against Atari's document rather than transcribing an emulator).
5. Commits: one for the study, one for the sheets, one for the index and the corrections; pushed to `origin main`.
6. Anything that changes our model lands as a dated correction in `design/` or as a ticket — never as prose
   inside a ranking table. **And the in-repo blitter page gets its dated caution** (`atari-st-hardware/
   components/blitter/01-blitter.md`).

---

*Written 2026-11-21, closing the U300 study with the next chip's brief. The ordering stays as the per-chip plan
set it: the Blitter is last of the four customs because it is the only one whose drawing survives — and because
it is the one bus master the DMA study proved its neighbour is not.*
