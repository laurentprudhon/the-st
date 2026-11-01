# How the STE allocates access to the RAM bus

Status: research note, 2026-10-30. Sources: the STE hardware article from ST Magazine 44 (September
1990), translated and annotated by DrCoolZic (`info-coach.fr/atari/hardware/STE-HW.php`); the 520/1040
STE Field Service Manual's description of the memory controller; the vendored Hatari sources
(`blitter.c`, `video.c`); and this repo's own `atari-st-emulation/02-atari-ste-hardware-overview-for-emulation.md`
§4.1–4.3. Nothing here was measured on hardware.

## The short answer

There are **two buses**, not one:

- the **CPU bus** — the 68000's address, data and control lines;
- the **memory interface** — the DRAM's multiplexed address bus with `RAS`/`CAS`/`WE`, driven by the
  GSTMCU's memory controller.

The 68000 starts a cycle, asserts `AS`, and waits for `DTACK`; the GSTMCU performs the DRAM transaction
**inside that wait**, and is free to slip a video or sound fetch into the gap. The service manual states
it plainly: *"The Memory Controller produces all of the addresses for video, sound, and DMA on the
multiplexed address bus. These addresses never appear on the system address bus."* That sentence is the
whole architecture: the streams are addressed by the memory controller itself and never touch the CPU
bus.

## The three access paths

| Path | Who asks | How the bus is obtained | Consequence |
|---|---|---|---|
| **Ordinary CPU access** | 68000 | asserts `AS`/`R/W`/`FC`; GSTMCU decodes, performs the DRAM cycle, returns `DTACK` (or `BERR`) | the CPU may be *made to wait* while the controller schedules a fetch; CPU-visible throughput is below 8 MHz |
| **Video and sound fetch** | nobody — the GSTMCU schedules it | **no** `BR`/`BG`/`BGACK` at all: the controller drives the video (or sound-DMA) address counter onto `MAD0–MAD9` and strobes the fetch | a continuous stream, transparent to the CPU, interleaved into the CPU's own cycles |
| **Disk DMA** | DMA controller asserts `*RDY` to the GSTMCU | GSTMCU relays `*BR` to the 68000 → CPU finishes its cycle → `*BG` → GSTMCU → `*BGACK` | a temporary burst; full bus mastership, every 68000 pin tri-stated while held |
| **Blitter** | the Blitter itself | drives `BR`/`BG`/`BGACK` **directly**, bypassing the GSTMCU (which still arbitrates to avoid congestion) | a temporary burst of many accesses: 4 cycles to acquire, 4 per word, 4 to release |

Two consequences worth stating, because they are the ones a model gets wrong:

1. **Video and sound fetches are not bus mastership events.** They are the memory controller serving its
   own timetable. There is no handshake to observe, which is why a per-fetch event in a scheduler is a
   modelling artefact rather than an emulation of anything.
2. **The Blitter does not go through the GSTMCU to take the bus.** Source: the STE article says so
   explicitly, and the inventory agrees — `U101.13`/`.11` are on the `BR`/`BG`/`BGACK` net with
   `U400.29/.28/.27/.26`.

## The video fetch

- The controller holds **`VBASE`, `VCOUNT` and `LINEWID`** and generates one address per fetched word, in
  step with the video counter.
- The article: *"`*DCYC` indicates to the Shifter that the data are available from the RAMs — the GSTMCU
  has just selected the RAM address of the next data to be loaded — this addressing is carried out
  **16 000 times per image**"*. 16 000 words = 320 × 200 × 4 planes ÷ 16 → **4 000 fetch groups of 4
  words** per frame in 320×200, i.e. 20 groups per active line.
- `*LOAD` marks the load into the Shifter.
- **Where the registers live** (article, confirmed against Hatari's dispatch tables): `VBASE`, `VCOUNT`,
  `LINEWID` and the 50/60 Hz sync register are **in the GSTMCU**; only `COLORS`, `VIDEOMOD`
  (`$FF8260`, which exists *twice* — one copy in the Shifter, one in the GSTMCU) and `HSCROLL` are in
  the Shifter. `$FF8260`-shaped state is genuinely duplicated in the hardware.
- **Sync generation** (`HSYNC`, `VSYNC`, `DE`, `*BLANK`) is a **GLUE/GSTMCU** function, not a Shifter one.

## The sound DMA (STE)

- The **counters live in the GSTMCU** — `FBASE`, `FCOUNT`, `FEND`, and probably `PLAYMOD`; the Shifter
  holds `SAMPMOD` (uncertain), `LMCDATA`, `LMCMASK`.
- The handshake is **`SREQ` (Sound REQuest) from the Shifter and `*SLOAD` (Sound LOAD) from the GSTMCU**.
  This corrects an earlier mislabelling in this repo: **`SREQ`/`SLOAD` are the sound path, not the video
  path**; the video path uses `*DCYC` and `*LOAD`.
- The fetch is a 16-bit word; the Shifter then emits it as two bytes on `SD0–SD7`, each latched by a
  74F374 into its DAC.
- The article is explicit that the whole reason `SCLK` exists on the Shifter is this DMA: disconnecting
  `SCLK` silences the sound and changes nothing else.

## Refresh

The memory controller refreshes DRAM; the article and the overview both note that refresh consumes bus
time and must be accounted for rather than assumed free. **The exact refresh period is not stated in any
source examined here** — open question.

## What this means for the model

- **Continuous streams belong to the memory subsystem** because they *are* the memory subsystem's own
  schedule, addressed from counters it owns.
- **Temporary bursts belong to the chip that produces them** (Disk DMA controller, Blitter), because each
  is a bus master that must ask the CPU for the bus — and the CPU's grant is the event.
- **The arbitration algorithm itself is CPU-side**: `BR` → finish current cycle → `BG` → `BGACK` → hold →
  release. The GSTMCU's role for disk DMA is relaying (`RDY` → `BR`), not deciding.

## Evidence gaps

- **No source states the per-line slot allocation** — which cycles within a 512-cycle line belong to the
  CPU, which to video fetch, and where sound DMA cycles are inserted. The 16 000-words-per-frame figure
  bounds the *total*, not the placement.
- **Hatari does not model STE bus contention** in a way that could be cited as the algorithm; its timing
  constants give the Blitter's 4-cycle granularity but not the controller's slot table.
- **Refresh period** unknown.
- The STE article is a magazine analysis with the author's own admissions of uncertainty ("not sure"
  against two sound registers); treat its register placements as strong but not primary.
