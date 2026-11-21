# Handoff — U401, the GST Shifter (C300588-001)

**Goal of the next session.** Write `research/gst-shifter-implementation.md`: the STE's video chip studied
to the standard the GSTMCU study reached on 2026-11-21 — what it *is*, how it operates (one line, one
sixteen-pixel group, one palette write, one DMA-sound word), what the record disputes, and what only the
board can settle.

Read first, in this order: the U401 entry in `docs/handoffs/silicon-research-per-chip.md` §4 (the question
set), `research/ste-super-hires.md`, `research/gstmcu-silicon-implementation.md` §3.4 and §5 (the DE and
`DCYC` contract this chip sits on the other side of), `design/video.md`, `design/subsystems-spec.md`'s
Video row, and `research/gstmcu-reconstructions.md` §3 (how the RTL splits the group between the two chips).

---

## 1. What is already established about this part

* **No die photograph, and no Atari drawing.** The archive's 33-sheet set is the GSTMCU; the Shifter has no
  sheet in it, and the decap archives hold no STE custom (§2.1 of the GSTMCU study, still true). So this is
  the *first* chip in the campaign where the primary source is missing and the study must say so up front —
  the honest basis is the service manual's pin list, ijor's reverse-engineering (published schematic and
  forum work), Hatari's model, and the GSTMCU's documented side of every wire they share.
* **The contract with U400 is now documented**, which is the best lever this study has: the Shifter
  asserts `DE`, U400 samples it into `vidb`, opens the internal slot while it is high, and returns each
  word with `DCYC` at phase 1 of that slot (`research/gstmcu-silicon-implementation.md` §5.2). The four-word
  group is the Shifter's own assembly: `shifter_video.v` shifts a 4-deep pipeline on every `LOAD`.
* **What it is not.** `research/ste-super-hires.md` already settles the absent features; do not re-litigate.
* **The STE's sound DMA is inside this chip**, per the reconstruction (`gstshifter.v`, `sndcnt.v`), and the
  2019-2026 consensus is that the DMA controller's sound path is *not* in U300. That is a claim to test,
  not to inherit: the GSTMCU memo specifies the sound channel from the other end and should be read against
  it.

## 2. Acceptance criteria

A reader who has the study must be able to do these without opening another document:

1. **Say what a sixteen-pixel group is**: how 4, 2 and 1-plane modes differ in words fetched, bit order and
   pixel clock; where the group boundary comes from; what happens when a hard-scroll offset is not a
   multiple of 16.
2. **Draw one line as a timetable**: the 320/640/640-mono pixel counts against 512 clocks, the fetch
   schedule U400 imposes, `DE`'s edges, and the blanking that follows.
3. **Follow colour end to end**: 4-bit pixel → palette entry (16 words × 12 bits) → 9-bit DAC value →
   the three analogue outputs, with the STE's 0321 bit order and the ST's 0123 stated side by side.
4. **Follow a palette write**: `$FF8240`-region write → the decode → which register holds it → what a read
   returns, and what `$FF8260`'s duplication means from this side (`research/gstmcu-silicon-implementation.md`
   §6.7).
5. **Say how one DMA-sound word arrives** and where it is buffered before the DAC — with the memo's
   `SLOAD`/`SINT`/`XSRQ` side referenced, not re-derived.
6. **Say where the fine-scroll register lands**: which storage, and whether the offset is added here or in
   U400 (`WLOC*`/`MX21` live in U400's sheet 11; the boundary must be stated, not split).

## 3. Evidence, and what each layer can answer

| layer | what it gives | what it cannot |
|---|---|---|
| service manual (520STE/1040STE field manual, 84-pin PLCC symbol) | pin list, pin numbers, directions | internal structure, anything about timing |
| ijor's published schematic + forum analysis (the Shifter's only real documentation) | block structure, DE/BLANK generation, the palette path | intent, and it is one person's reading — treat as `inferred` unless the manual agrees |
| Hatari `video.c`, `dmaSnd.c`, Steem | behaviour to explain, and a checklist of constants nobody sourced | silicon truth — T3 by definition |
| the GSTMCU study's §5 | the DE/`DCYC` contract, the 500 ns cadence, the four-word group | anything inside this chip |
| board photographs / the machine itself | the DAC path, the LMC1992 wiring, the analogue stages | logic |

**The Matsushita/PLCC pin symbol is on the service manual's sheet 5** — the same scan
`design/board-inventory.md` §3.5 already transcribed. Start there for pin identities, then use ijor's
schematic for structure, and only then read an emulator.

## 4. Method rules that earned their keep this week

* **Sheets first, RTL second, emulators never as evidence.** Every claim labelled `documented` / `derived` /
  `inferred` / `unknown`; `T1`/`T2`/`T3` for anything about our core.
* **Names lie.** `SHR01` was a *select*, not a shift register; `DRAM REFRESH COUNTER` titled the paddle's
  divider; `LATCH` was not an address latch. Read a net's *pins*, never its letters.
* **A gate that mixes a decode with a clock is part of the clock path.** The GSTMCU's refresh question died
  because `NAND(decode, K500CLK)` was read as data rather than as the flop's clock. Ask of every gate which
  of its inputs is the edge.
* **Check arithmetic against the part, not the sheet.** The 41256's row count and refresh interval settled a
  contradiction no drawing could; the same move works here (pixel clock, palette depth, DAC bits — all have
  a part behind them).
* **Deliver the crops the owner needs.** A 500-700 dpi crop with the row labels in frame, plus a *question*
  with the two candidate answers and what each would mean, is worth more than any number of open-ended asks.
  The owner is the eye; the agent is the arithmetic and the netlist.
* **Never let a vision model's geometry be evidence.** It reads text and levels well; it invents positions.
* **A qualifier in a strobe's equation often explains a behaviour you had assumed was a special case.** The
  refresh cycle is CAS-less because `VOS` gates `CAS` while `~REFB` gates `RAS` — one gate group, two
  behaviours, no special case anywhere. Before writing "the hardware does X on refresh cycles", read the
  strobe's other inputs.
* **A transparent latch is not a resynchroniser.** Its edge *is* the decode's edge, delayed at most by one
  open window. A one-step offset between a strobe and its decode is therefore the latch's gate, not a
  phase width — reading it the other way put this study on the wrong foot for two sessions.
* **Read the reconstruction as a transcription with comments.** Its author writes latches out longhand
  (`clk ? d : hold`) with the sheet's pad number beside them (`// pl025`), which settled a cell's polarity
  that no sheet in the archive showed. It is still `inferred`, but it is evidence of how a careful reader
  read the same drawing, and it costs one grep.
* **Test every timing reading against a second artefact.** The phase width was settled by `CAS`'s
  combinational path meeting the chart's two-number gap; the latch polarity by the chart's two edges
  meeting `CLK8`'s open half. One artefact can be misread; two that agree cannot both be.
* **Keep sheet numbers out of citation syntax** (`LC[20]` reads as a citation and fails the verifier).

## 5. Arithmetic seeds to check, not to copy

* 320 px at 16 px/word = 20 words per plane-set; ×4 planes = **80** words a line; 640 px mid = 40 × 2 = 80;
  mono = 40 × 1. Matches U400's 80-of-128 slot budget — if the study derives a different count, one of the
  two chips is wrong and that is a finding.
* 16 pixels per group ⇒ 20 groups per lores line, 40 mid, 40 mono; the group reload is every 4th `DCYC`.
* Palette: 16 registers, STE 12-bit (4096 colours), DAC takes 9 bits — state the truncation and where.
* STE bit order 0321 vs ST 0123 (the reconstruction's comment) — check it against the palette path rather
  than repeating it.
* Pixel clock: 8 MHz in lores (16 px per 2 µs), 16 MHz in mid — the Shifter is the only chip in the machine
  whose pixel clock differs by mode.

## 6. What must stay `unknown` until measured

* The fine-scroll boundary between the two chips (§2.6), if the sheets do not settle it.
* The LMC1992's Microwire frame format as this chip drives it — datasheet-led, but the *frame timing* is a
  U401 fact.
* Where the 320-pixel boundary physically lives, if ijor's schematic and Hatari disagree.

## 7. Definition of done

1. `research/gst-shifter-implementation.md` in the study shape (§1 summary, §2 evidence, §3 the chip as the
   record shows it, §4 the chip in operation, §5 conflicts, §6 what the model needs, §7 tests, §8 what stays
   unknown, §9 licence), every acceptance criterion in §2 demonstrably met, `verify --min-coverage 0.30` green.
2. `docs/handoffs/silicon-research-per-chip.md` §4's U401 entry updated to **done** with a one-line pointer,
   and this file's items ticked.
3. The U400 study's Shifter-facing claims either confirmed or corrected in place (`§3.4`'s sound-DMA split,
   `§6.7`'s duplicated `$FF8260`), with a date.
4. **The sheet set**, `research/gst-shifter-silicon/schematics/`, on the GSTMCU pattern
   (`research/gstmcu-silicon/schematics/`): a `build_schematics.py` that emits numbered SVGs and audits its own
   label overflow, a `README.md` stating what each sheet is evidence *for*, and an `index.html` for reading in
   order. Five levels are the series standard — the chip as blocks, its datapath, its control, its timing, and
   one worked example (for U401: a word at 16 colours). **No floorplan** — same ruling as U400: no die
   photograph exists for this part either, so no layout is claimed. Where the evidence is thinner than U400's,
   say so *on the sheet*: a block whose boundary is a guess gets a dashed outline and a `T3`-grade note.
5. Commits: one for the study, one for the sheets, one for the index and the corrections; pushed to `origin main`.
6. Anything that changes our model lands as a dated correction in `design/video.md` or as a ticket — never as
   prose inside a ranking table.
