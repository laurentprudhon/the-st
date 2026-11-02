# Recovered Atari ASIC designs: what they are, what they prove

Status: research note, 2026-11-01. Source: Christian Zietz's recovered files from Atari Corp.'s
shutdown, published at <https://www.chzsoft.de/asic-web/> (`ASICs-new.zip`, 15 MB, 1 101 entries).
Zietz recovered the designs from SyQuest/floppy images using Atari's own `print.prg` under Steem and
GhostPCL, and has since been **adding searchable text layers** to the PDFs.

## What is in the archive

| File | Design | Engineer | Date | Text layer? | Relevance |
|---|---|---|---|---|---|
| `ST4081S.PDF` | **ST-4081 "ST MCU / GLUE COMBO"** — the chip that shipped as the GSTMCU | D. Renn | Nov 1988 | **Yes — 49 pages, 73 070 chars** | **This machine's memory controller** |
| `4051.pdf` | ST-4051 "STB — state machine" | D. Renn | Mar 1988 | no | likely the Blitter family (see below) |
| `4082.pdf` | ST-4082 "BLT CELL" | D. Renn | Sep 1988 | no | **Blitter** cells |
| `BLTCELL.pdf` | "BLT CELL" sheets — *archive damage, most sheets missing* | — | — | 5 pages | Blitter, fragmentary |
| `4078.pdf` | ST-4078 "address/data/control lines" | M. Johnson | Jun 1988 | no | I/O front-end of an ASIC |
| `4079.pdf` | ST-4079 "input/output pads" | M. Johnson | Jun 1988 | no | pad ring of an ASIC |
| `4080.pdf` | unknown — *"possibly the never released Unix Workstation"*; corrupted | — | — | 168 pages, no text | **not** ST |
| `4118` (`ST4118.pdf`) | ST-4118 "68K Game Shifter" | D. Renn | Oct 1988 | no | **Atari Panther**, never released — not ST |
| `4138i2`, `PCLAN`, `PCSCSI` | PC cards | — | — | no | not ST |
| `Atari_Sparrow.pdf` | Sparrow (Falcon prototype) PCB, incomplete | — | — | no | not this machine |
| Jaguar PCBs | Jaguar console/cartridge | — | — | no | not ST |

Title-block identities for 4051/4078/4079/4082 were read visually from rendered title blocks (the image
tiles are legible; the underlying PDFs have no text layer). Treat those four names as `inferred` until a
second source agrees — the archive's own README groups them by design number only.

**So: the GSTMCU is recovered and searchable. The Blitter is present but fragmented and image-only. The
STE's Shifter, DMA controller, MFP and YM2149 designs are not in this archive.**

## What the recovered GSTMCU design confirms

The text layer exposes the chip's own net names, and they corroborate the behavioural sources at the
*architectural* level — the things the model depends on exist as real nets:

- **Phase-based slot selection**: `TIME0B`, `TIME1`, `TIME1B`, `TIME2`, `TIME2B`, `TIME4B` feed
  `ADDRSEL` / `ADDRSELB` in the "MCU Control 3 of 3" sheet (p20), exactly the phase counter Cwik
  describes and that `gyurco/gstmcu` encodes as `addrselb = ~time5_s`.
- **The three internal clients are wired to one address-select block**: `REFB` (refresh), `SNDEN`
  (sound DMA) and `VIDEN` (video) all appear in that same control block, and the "RAS/CAS Generator"
  sheet (p21) takes `ADDRSEL`, `ADDRSELB`, `REFB` together with `RAM1A/RAM2A` into `RAS0/RAS1` and
  `CAS0L/CAS0H/CAS1L/CAS1H`.
- **Video and sound handshakes are distinct nets**: `DCYC` and `SLOAD` are separate signals in the
  same block, which is the design-level version of the manual's sentence about `DCYC` strobing display
  data into the Shifter.
- **The refresh counter is a real block**: the sheet titled *"DRAM REFRESH COUNTER"* (p32) sits beside
  the vertical-sync logic, so refresh timing is derived from the video timebase on-chip.
- **`IVSYNC` / `VSYNC`** appear on the vertical-counter sheets (p19, p26, p29, p32).

## What it does **not** confirm — and the trap to avoid

**The design does not, by itself, prove behaviour.** It is a gate-level schematic: signal *ordering*,
*priority* and *edge cases* live in the gate connections, not in the net names. Reading a priority
order out of it means tracing gates by hand or with a tool.

**And this matters:** `gyurco/gstmcu` states in its own header that it is *"based on ST4081S.PDF"*. So
where the RTL and this PDF agree, that is a **transcription agreeing with its source, not two
independent witnesses**. The genuinely independent evidence for behaviour remains:

1. **Cwik's cycle-counting article** (behavioural, from hardware) — round-robin 2-clock slots, no priority
   between internal and external.
2. **Atari's service manual** (architectural) — one priority scheme, for interrupts only; memory
   controller produces all stream addresses.
3. **Hardware confirmation quoted by the RTL author** — the Level 16 fullscreen demo for the VSYNC
   double-reload.

The RTL's *internal* priority order (refresh → sound → video) is therefore the weakest link in the chain:
it is a transcription, and only a hardware test or a careful gate trace would raise it.

## Next steps this opens

- **Blitter**: `BLTCELL.pdf` is damaged (5 sheets, "most sheets are missing") and `4082.pdf` (21 pages,
  image-only) appears to be the same family. If the surviving sheets cover the bus-request logic, the
  non-HOG 64-cycle timeout could be confirmed from the design rather than from MiSTer's RTL.
- **Identification**: 4051/4078/4079/4082 title blocks were read visually; OCR of the remaining sheets
  would confirm what each design is and whether 4051's "STB state machine" is the Blitter's controller.
- **A gate trace** of the `ADDRSEL` block in `ST4081S` (p20–p24, p47) is the only way to settle whether
  refresh really outranks video in the hardware, as opposed to in the transcription.

## Note on the visual tooling (corrected 2026-11-01)

**Root cause found: it is a 30-second client-side deadline, not the endpoint.** Vision runs as an
*auxiliary* task, and auxiliary tasks default to `_DEFAULT_AUX_TIMEOUT = 30.0 s`
(`/opt/hermes/agent/auxiliary_client.py:6243`); the timeout is read from `auxiliary.<task>.timeout` at
call time (`_get_task_timeout`, line 6346). `/opt/data/config.yaml` has **no `auxiliary:` block**, so
vision inherits 30 s, and any cloud round-trip that exceeds it raises `httpx.ReadTimeout` →
`openai.APITimeoutError` → "Error analyzing image: Request timed out." Image size is irrelevant, which
is why a 49 KB tile failed and a 75 KB sheet succeeded minutes later.

Two upstream issues describe the same failure class: **#2107** (closed; "vision_analyze … will timeout
due to default 30s timeout", with that exact traceback) and **#8120** (still open, P2, `tool/vision`:
"~50% failure rate … the same model works fine in other AI applications"). The documented lever is
`auxiliary.vision.timeout`.

**The fix (user-side, because the agent cannot write `config.yaml`):**

```yaml
auxiliary:
  vision:
    timeout: 120
```

in `$HERMES_HOME/config.yaml`. A fresh session guarantees it takes effect.

**What also holds:**
- The 300 dpi board-schematic PDFs embed a fixed raster, so rendering above 300 dpi only interpolates.
  The **600 dpi** variants (`1040 STFM.pdf`, `1040 STE 600dpi.pdf`, gossuin.be; 6870 × 9774 per sheet)
  are the right source for pin numbers.
- Small pre-composed greyscale contact sheets are the cheapest way to identify sheets or title blocks.
- **Local OCR is available but not sufficient for schematics**: `rapidocr-onnxruntime` is installed, yet
  its detector downsamples to a 736 px min side (configurable only through a JSON config file, not via
  constructor kwargs — passing `det_limit_side_len` raises `KeyError: 'model_path'`), so
  schematic-sized text is lost, and full-page OCR at 300 dpi is **OOM-killed** (exit 137).

**2026-11-02 — vision fabricates, so it is not a locator.** With the endpoint healthy again, three
separate reads invented content on real Atari sheets: a title block "Star's Corporation / CXD870-001",
another "ATARI GAMES CORPORATION / C030322", and, asked for reference designators, a tidy
`U400/U401/U500/U600` sequence that does not exist in the drawing. What it does get right is
transcription of a crop that is already legible. So the division of labour that works: **OCR finds**
(reference designators, `SCHEMATIC DIAGRAM`, `SILK`, sheet structure — 200 dpi full-sheet sweep, then
tight crops), **a human reads** the pin numbers, and vision is only ever a second opinion that must
agree with one of those. A read with no corroboration is not evidence, however plausible it looks.




---

## Follow-up 1 — the `ADDRSEL` priority, settled from the design itself

**Done, and it is decisive.** Page 22 of `ST4081S.PDF` — sheet *"MEMORY ADDR MUX 1 OF 3"* — carries
Atari's own decode table printed on the drawing:

```
                        IXDMAB  SADSEL  REF  ADDRSELB        selects
  (X X 1 1)  REF          X       X      1      1            REFRESH
  (X 1 0 1)  SOUND        X       1      0      1            SOUND DMA
  (X 0 0 1)  VIDEO        X       0      0      1            VIDEO
  (1 X X 0)  CPU          1       X      X      0            CPU
  (0 X X 0)  DMA          0       X      X      0            DMA
```

Read off it directly:

- **`ADDRSELB = 1` is the internal slot.** Within it, **`REF = 1` selects refresh regardless of
  `SADSEL`** — refresh outranks both other clients. Then `SADSEL = 1` selects sound DMA, else video.
  So the internal priority is **refresh → sound → video**, from Atari's sheet, not from anyone's
  transcription.
- **`ADDRSELB = 0` is the external slot**, and `IXDMAB = 1` selects the CPU while `0` selects the DMA
  controller. So within the external half, **CPU outranks the DMA chip** — which is the one ordering
  detail the RTL expressed as `4'b00??`/`4'b01??` and that nothing else documented.
- **Nothing ranks internal against external**: that is `ADDRSELB` alone, i.e. the phase. Cwik's "no
  priority of any kind" is exactly right *between the classes*, and the priority lives *within* them.

Corroborating sheet: page 47, *"ST TIMING / TIMING GENERATOR"*, lists `TIME0`–`TIME7`, `CYCSEL`,
`ADDRSEL`, `SLOAD`, `DCYC`, `LATCH`, `ROW`, `COL/ CAS`, `CPU`, `VIDEO/REF`, `RAS` against `MHZ8`/`MHZ16`
dividers — the 8-phase generator that produces `ADDRSELB`, and a second confirmation that the slot
period is a phase decode rather than a scheduler.

**Consequence:** the RTL's priority order is no longer "transcription agreeing with its source". It is
confirmed by the design. The remaining weak link is only the *refresh period*, whose row counter is
stubbed in the RTL (`ADDR = 0`) and which the design states structurally but not numerically here.

## Follow-up 2 — the Blitter's non-HOG timeout: **not** confirmable from the design

Attempted, and the honest answer is that this archive cannot do it:

- **ST-4082 "BLT CELL"** (21 sheets) is image-only CAD output — no text layer, no netlist. The only
  machine-readable artefact in the whole blitter directory is `4082.LST`, a **gate-type and count list**
  (`BIT 364 × 12`, `DPR 7 × 24`, …) with no connectivity at all.
- **`BLTCELL.pdf`** is archive-damaged — its own first page says *"Archive damage, most sheets are
  missing"* — five sheets, fragmentary.
- Visual reads of the surviving sheets returned only the title block ("BLT CELL") repeated; the gate
  content is too dense at this resolution for reliable net-name reading, and no OCR tool is available in
  this container.

**So the 64-cycle non-HOG timeout rests on one source, but a better one than expected:**
`rtl/stBlitter.sv` in the MiSTer core is **"FX ST Blitter, Copyright (c) 2019,2021 by Jorge Cwik"** — an
independent reconstruction, *not* derived from Zietz's recovered design. Its comment already flags the
behaviour as a buglet, so the claim has an author who reverse-engineered the chip and documented the
quirk. That is decent evidence; it is still not a hardware measurement, and the way to settle it is a
test: run a non-HOG blit whose transfers exceed 64 bus cycles and observe whether `BUSY` clears and the
bus is released early.

## A route for a real gate trace, if ever needed

The archive also contains **CAD netlists** — `4081/PIN/4081.PIN` (138 KB) and
`4081.ORG/PIN/TA4081.PIN` (232 KB) — in P-CAD `PINLIST` format: `(SYM,n … DATA,2,<pad> … PIN,<name>,<net>,…`
records. That is machine-readable connectivity for the GSTMCU, and a far better basis for a gate-level
trace than the PDF text tokens. No equivalent exists for the Blitter.


### OCR recipe that works on these schematics (2026-11-01)

Local OCR is viable after all, but only with two corrections that took several attempts to find:

1. **The sheets are drawn rotated 90°.** OCR without rotation returns almost nothing; with
   `Image.rotate(90, expand=True)` it reads real labels (`6850`, `68901`, `74LS02`, `U56`, `U70`, `SND`,
   `DE`, `TATA`/`TIDATA`).
2. **The detector's resize limit must be raised from the 736 px default.** It cannot be set through
   `RapidOCR(...)` kwargs (that raises `KeyError: 'model_path'`); copy the packaged
   `rapidocr_onnxruntime/config.yaml` to a working file, set `Det.limit_side_len: 2000` and
   `Det.limit_type: max`, then `RapidOCR(config_path='rcfg.yaml')`. With that, a sweep of a 600 dpi sheet
   in 2000 px tiles returns 10–20 tokens per tile instead of zero, and runs at ~26 s per sheet.

What this still does **not** achieve: the words `GLUE`, `MMU` and `SHIFTER` are not detected on any of the
eight sheets (`1040 STFM.pdf`), even with the corrected recipe — so the custom-chip symbols cannot be
located by OCR alone. Sheet 3, which the earlier vision pass called the logic sheet, OCRs as the
**peripheral** section (6850 ACIAs, 68901 MFP, 74LS02 glue). Locating MMU/GLUE still needs a vision pass,
which needs `auxiliary.vision.timeout` raised above the 30 s default.
