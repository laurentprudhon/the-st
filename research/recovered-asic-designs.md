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

## Note on the visual tooling (for future sessions)

Rendering the board schematic at >300 dpi is pointless: the PDF embeds a 300 dpi raster per sheet.
`vision_analyze` with a `region` crop timed out repeatedly on **dense, text-heavy crops** (950×1050,
270×900, 950×350) — reproduced three times, so not purely transient — while a **pre-rendered, downscaled
tile sheet** (905×700 per tile, 75 KB total, 2×2 grid) was read instantly and accurately. Working rule:
compose your own small greyscale contact sheets in PIL and pass the file path; do not hand large crops
to the tool and hope.
