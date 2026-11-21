# Handoff — the GSTMCU, operating

**Goal of the next session.** Complete `research/gstmcu-silicon-implementation.md` with a **detailed
description of how the GSTMCU operates**: one slot, one line, one frame, one bus cycle, one register write —
the chip's operating cycle in time, read from the recovered design. This is the section the 68000 study gets
from its microcode chapters and this one currently does not have.

Read first: the study itself (all of it), `research/gstmcu-silicon/README.md`, the memo
`TXL/DWG/SMOD.TXT`, and `design/system-control-unit.md` (the shape our model already assumes — the new section
must support or correct it, not duplicate it).

---

## 1. Where the study stands

Committed 2026-11-21 (`c8c1178`, `4a469ef`, `0f3f5d0`). What is **done** and must not be rewritten:

| § | content | state |
|---|---|---|
| 1 | executive summary, the one-sentence machine | done |
| 2 | evidence inventory — no decaps exist; the recovered design is the source; RTL is a transcription; emulators are T3 | done |
| 3 | the chip as drawings show it: 15,394 T / 50 cells, no ROM-PLA-RAM, `TGEN`, five counters, `SAB`/`RLC` register bank, ten `MX41` muxes + printed decode table, `INTERLACE`/`OVERLAP`/`UDEN` | done |
| 4 | the sound-DMA memo quoted, its register map and pin timings | done |
| 5 | eight conflicts adjudicated | done |
| 6 | ten open questions, tiered T1/T2/T3 | done |
| 7 | six hardware tests with pass criteria | done |
| 8 | what is still unknown; no figure drawn, and why | done |
| 9 | licence position | done |

What is **missing** is the middle: the study says what the chip *is* and what the record disputes, but never
walks it through a slot, a line or a frame. A reader cannot yet answer "what happens on this machine between
two falling edges of `RAS0`".

## 2. Acceptance criteria for the new section

A reader who has the study must be able to do these five things without opening the archive:

1. **Draw one line as a timetable.** 512 clocks, 128 cycles, 128 internal slots, the display window, blanking,
   which owner gets which slot, where `DCYC`/`SLOAD`/`LATCH` fall, what `RAS`/`CAS`/`WE` do in each slot class.
2. **Say what a frame is.** 313 lines, `VSYNC` assertion, `DE` gating, the video base/counter reload at the VBL,
   line width and fine scroll, and what software writing the mode registers mid-frame does (the Level-16
   double reload is the known case).
3. **Follow a register end to end.** A write on the bus → decode → the latch that holds it → which machinery
   reads it → what a read returns (mirrors, aliasing, `$FF8260` twice, `$FF8000`/`$FF8002-0F` harmless).
4. **Follow one CPU bus cycle.** `AS` to `DTACK`, including a cycle that loses its slot to video, sound or
   refresh, and what that costs in clocks; plus `BERR`, `VMA`/`VPA`, and the `RDY`→`BR`→`BG`→`BGACK` relay
   for a DMA master.
5. **Say what a reset does** and what `TEST` (pin 74, `ITEST` gating dozens of cells) is documented to do —
   the second only as `unknown` if nothing settle it.

Every claim labelled `documented` / `derived` / `inferred` / `unknown` as the rest of the study does, and every
external source cited `[n]` from the ledger.

## 3. Proposed shape, and the renumbering checklist

Insert as **§5 "The chip in operation"** between the sound memo (§4) and the conflicts, because the conflicts
read better once the behaviour has been described. Suggested subsections:

* **5.1 One slot (250 ns).** The eight phases from `TIME0`–`TIME7`, `ADDRSELB` as the internal/external select,
  the four internal owners and the two external ones, the `MX41` select lines, and the DRAM pin choreography
  (`ROW`, `COL/CAS`, `RAS`, `LATCH`, `WE`) per slot class — sheets 20, 21, 22–25, 48–49.
* **5.2 One line (512 clocks).** The timetable: display window length per mode, fetches per line, blanking
  slots, `HBLANK` vs `DE`, `DCYC`+`LOAD` group delivery, `SLOAD`, the sound and refresh placement, the
  `XSRQ` gate — sheets 10, 20, 29, 48, 49 and memo §4.
* **5.3 One frame.** 313 lines, `VSYNC`/`IVSYNC`, `VDE`/`VBLANK`, the video base reload, the sync register's
  50/60 Hz and `OVERLAP` bits, interlace (`unknown` what `INTERLACE` drives) — sheets 26–30, 12, 15, 33.
* **5.4 The bus and its grants.** CPU cycle, `DTACK` at the slot boundary, `BERR` from the decode, the DMA
  relay and the blitter's direct request, cycle granularity, what a stolen cycle costs — sheets 1–3, 6, 8, 19,
  plus `research/gstmcu-reconstructions.md` as `inferred` only.
* **5.5 The register block.** `$FF8000`–`$FF82FF`, `$FF8600` (FDC/DMA selects), `$FF8900` (sound), `$FF9200`
  (joypad/paddle/pen): address decode, write path, readback mirrors, aliasing, which registers are latched
  where, and what the design does *not* define — sheets 2–5, 10, 12–15, 18, 32.
* **5.6 Interrupts and the controller side.** The ten sources into `IPL0–IPL2`, the integrated 74LS148
  behaviour, `HINT`/`VINT` timing (as far as the sheets go), and the paddle/pen/joypad scanning with the
  `PADC`-clocked counters — sheets 26–30, 31–32.
* **5.7 Reset, test, and the clocks out.** `RESET`/`LRES`/`POR`, `CLK8`/`CLK4`/`CLK2`/`KHZ500` to the board,
  `TEST`/`ITEST` — sheet 7 and the pad sheets.

**Renumbering checklist** (do it in one pass, then re-grep): current §5→§6, §6→§7, §7→§8, §8→§9, §9→§10.
Internal cross-references that will go stale, by current number: `§4.1` (§7 table), `§5.1` (two, in §3.6 and
§5.1 itself), `§6`, `§6.2`, `§6.4`, `§7`, `§7.1` (two), `§7.3`, and the `§6, 7` pair in the §1 summary table.
`grep -o "§[0-9]\+\(\.[0-9]\+\)\?" research/gstmcu-silicon-implementation.md | sort | uniq -c` after the edit
is the cheapest check. No other file in the repo cites this study's section numbers.

## 4. Where the evidence is

Run `research/gstmcu-silicon/tools/fetch_4081.py --work /opt/data/cache/gstmcu-4081` first; then
`st4081s.txt` is greppable and every sheet is one `render_sheet.py` call away.

| sheets | block (titles as printed, cross-checked by net names) |
|---|---|
| 1–3 | address/control bus interface, the `$FFxxxx` decodes, `OVERLAP`/`SYSTEM`/`IRAMA` |
| 4–5 | video base + config registers; the sound control register (`RSCNTL`, `SH4A0x`) |
| 6 | data bus interface |
| 7 | clocks: `ITEST`, `M2CLOCK`, `K500CLK`, `KHZ500`, `ICLK8`, the ÷2 chain |
| 8–9 | chip selects (`CARTSEL`, `JOYSEL`, `REGXACK`, `RAMCYC`, `SNDCS`), cycle selection |
| 10–12 | scroll/`NOSCROLL`/`SCRLSEL`, video base and offset registers, readback mirrors |
| 13–14 | sound registers, frame pointers (`WSFTH/M/L`, `SFRAME`) |
| 15–17 | line offset/counter, `DMA ADDRESS COUNTER`, `SOUND ADDRESS COUNTER` |
| 18–19 | `DRAM SIZE/CONFIGURATION DECODES` (`K64G`/`K256G`/`M1G`), `RAM1A`/`RAM2A`/`RAMSEL` |
| 20–21 | MCU control: `LCYCSEL`, `ADDRSELB`, `REFCLK`, `SADSEL`, `SNDCLK`, `VIDCLK`, `CYCSEL` |
| 22–25 | `MEMORY ADDR MUX 1..3 OF 3` — sheet 23 carries the printed decode table |
| 26–30 | sync: `VERTCLK`, `INTERLACE`, `IHSYNC`/`IIHSYNC`, `HBLANK`, `VBLANK`, `MFPINT`, `DISKINT` |
| 31–33 | `DRAM REFRESH COUNTER` (three sheets, `C500`/`K500CLK`, `PADRST`, `PEN`), `Paddle Registers` |
| 34–47 | the 14-sheet Oct-1988 addendum (register and pad-ring detail); sheet 44 is headed `TA-4081` |
| 48–49 | `ST TIMING` by J. Horton, April 1988 — the two charts that define the slot grid |

Also: `TXL/DWG/SMOD.TXT` (the memo — the sound channel's specification), `4081GATE.DOC` (cell census),
`MACRO/MACRO.SIL` (cell pinouts), and the two netlists via `tools/analyze_pin.py`.

**Limits of that evidence, already established — do not re-litigate:** the netlist export stops at sheet 29
of 33 and its data bus stops at `D9`; the two netlists are different revisions in different libraries and must
never be added; the drawings give no die geometry.

## 5. Method rules

* **Sheets and the memo first, RTL second, emulators never.** A claim from `gyurco/gstmcu` is `inferred` and
  must say so; a claim from Hatari/Steem/MAME is a checklist item, not evidence.
* **Where the text layer runs out, render and ask.** The owner reads schematics himself — batch the questions
  per sheet ("sheet 20, zone C4: does `SADSEL` come from `TIME4` or `TIME2`?"), and mark the answer
  `documented` only once he has read it. A vision model's geometry is not evidence, ever.
* **One sentence per line** in prose, citations while drafting, then
  `python3 ~/.hermes/skills/research/grounded-citations/scripts/sources.py render --replace-in <study>` and
  `verify <study> --min-coverage 0.30`. Cite the archive `[2]` for anything read out of it; the ledger ids in
  the study are stable, so keep adding rather than resetting.
* **Nothing from the archive is committed** (Atari CAD, research use). Tools and transcriptions of small
  excerpts only, cited to the archive URL.
* **No figure** unless a sheet itself prints the outlines to snap to; a timetable table is the honest form.

## 6. Arithmetic seeds to check, not to copy

These follow from the documents but are not yet written down in the study, so treat them as claims to verify
against sheets 20/21/29 and the mode registers:

* line = 512 CPU clocks = 128 cycles = 128 internal slots = 64 µs;
* lores 320 px = 20 words per plane-set × 4 planes = **80 fetches**, mid-res 640 px × 2 planes = 80, mono
  = 40 — so `DE` occupies 80 of the 128 internal slots in lores and mid-res, 40 in mono;
* blanking therefore holds 48 internal slots (lores/mid) or 88 (mono), against sound's ≤3.2 refills per line
  and refresh's ≈8.2 slots at 7.8 µs;
* these are the numbers `design/system-control-unit.md` §4 already assumes — the new section must make them
  explicit and date the derivation.

## 7. What must stay `unknown`

The six tests in §7 of the study (now §8 after renumbering) settle what no document can: refresh inside the
display window, refresh vs `XSRQ`, the sound counter's ordering against `SLOAD`, `SINT`'s width, a mid-frame
`top` write, and the paddle law. Do not resolve any of them by argument — if the new section needs one, state
it as `unknown` and point at the test.

## 8. Pitfalls this session hit

* **The scratch copy of the archive is pruned after 24 h.** Use `fetch_4081.py --work /opt/data/cache/gstmcu-4081`
  (about a minute) rather than looking for the old extraction.
* **Sheet titles lie on reused sheets.** Sheet 11 is headed `DRAM REFRESH COUNTER` but carries the line-offset
  registers (`WLOC*`); sheets 31 and 33 share that title and carry the counter and the paddle clock. Trust the
  nets over the title block.
* **`XO` is an XOR, not a crystal oscillator** (`INTERLACE` is its output on sheets 26/28).
* **Reflowing a document mechanically corrupts lists and abbreviations** (`D. Renn`, `1.` on its own line,
  `correction 4.`). If a reflow pass is needed, re-read the diff before committing.
* **The citation tool counts line fragments.** Hard-wrapped paragraphs under-report coverage and tempt
  mechanical citation appending; that is why the study is one sentence per line.

## 9. Definition of done

1. `research/gstmcu-silicon-implementation.md` has the new operating section, the five acceptance criteria of
   §2 are each demonstrably met, cross-references renumbered, `verify --min-coverage 0.30` green.
2. `docs/handoffs/silicon-research-per-chip.md` §4's U400 entry gains one line pointing at the operating
   section (and the handoff's own pointer to this file is ticked off).
3. Commits: one for the study, one for the handoff/index updates; pushed to `origin main`.
4. If the section changes what our model must do, that lands as a dated correction in
   `design/system-control-unit.md` or as a ticket — not as editing prose inside the study's §6 table, which is
   the ranking, not the decision log.
