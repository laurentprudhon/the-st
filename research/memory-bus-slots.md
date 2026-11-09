# STE memory controller — DRAM bus slot allocation per scanline

**Status: complete** (research note, 2026-11-01; timeboxed pass over local docs,
vendored Hatari and Steem SSE sources, and the public cycle-counting literature.
Nothing here was measured on hardware.)

Scope: how the GSTMCU/MMU in the Atari STE allocates DRAM cycles between CPU,
video fetch (shifter), sound DMA and refresh, cycle by cycle.

Labelling convention used throughout:

- `documented` — a source states it explicitly
- `derived` — arithmetic from documented constants
- `inferred` — an emulator's code implies it, no hardware doc says it
- `unknown` — not established

Primary new source for this note: Jorge Cwik, *Cycle counting on the Atari ST*
(PASTI, 2009), `https://pasti.fxatari.com/68kdocs/AtariSTCycleCounting.html` —
the only source found that describes the MMU's slot allocation rule directly.

## 1. The bus cycle itself

**The allocation rule (`documented`, Cwik §"Second stage: Round up to a multiple
of four"):**

> "The MMU allocates two clock cycles for each type of RAM access, in a
> round-robin fashion. Two clock cycles for 'internal', and two for 'external'
> access. There is no priority of any kind. Internal and external 'slots'
> alternate constantly."

Cwik's two classes:

| Class | Members |
|---|---|
| **internal** (never reaches the CPU bus) | Shifter video fetch, DRAM refresh, STE sound DMA |
| **external** (comes from the CPU bus) | 68000 bus cycles, DMA-chip cycles, Blitter cycles |

This is the single most important sentence for the whole question, and it
contradicts any "priority order" model: there is no priority, there is a
**fixed time-division multiplex** in which internal and external alternate in
2-clock units. Everything in §5 follows from it.

Derived structure at 8 MHz:

- one **slot** = 2 CPU clocks = 250 ns = 8 master clocks at 32 MHz
- one 68000 bus cycle = 4 CPU clocks = 2 slots (1 internal + 1 external)
- per PAL line: 512 CPU clocks = **128 internal slots + 128 external slots**

`derived`: 512 clocks ÷ 4 = 128 slot pairs per line; at most 128 external
(CPU) accesses, and 128 slots reserved for internal clients.

**Relationship of clocks (`documented`/`derived`):**

- master clock 32.084988 MHz real; treated as 32 MHz here → 31.25 ns per master clock
- CPU clock 8 MHz (master ÷ 4) → 125 ns per CPU clock
- one DRAM access = 2 CPU clocks = 8 master clocks = 250 ns
  (`derived` from the 2-clock slot; the RAS/CAS sequence must fit in 250 ns)
- PAL line = 64 µs = 2 048 master clocks = 512 CPU clocks = 256 slots
  (`documented` — Hatari `src/includes/video.h:47` `CYCLES_PER_LINE_50HZ 512`;
  repo `research/deterministic-threaded-simulation.md:21-24`)

NTSC 60 Hz: 508 CPU clocks per line (`documented`, Hatari `video.h:48`);
mono 71 Hz: 224 clocks per line (`documented`, Hatari `video.h:49`) — 28 µs,
because the mono monitor's line rate is roughly double (`documented` in the
SM124 manual via the Retrocomputing SE thread: 20 µs video-active line).

**The 250 ns DRAM cycle is out of spec by design (`documented`, independent
confirmation).** Retrocomputing Stack Exchange Q11988 establishes that two RAM
accesses per 500 ns require a tRC of 250 ns, while the 41256-15's datasheet tRC
is 260 ns (tRAS 150 + tRP 100 + 10). Atari ran the parts ~4% over spec across
millions of machines; the chips were -10/-12 grade silicon binned as -15.
Useful for an emulator only as a sanity bound: **250 ns per access, no more.**

**Misalignment (`documented`, Cwik §"Second stage"):** an external access that
does not start on an external-slot boundary is delayed by wait states until it
does — "MMU doesn't care if the 'internal' slot is actually used or not". The
consequence is the familiar *round up to 4* rule, and Cwik's refinement that it
is really *align each RAM bus cycle to a 4-clock boundary relative to the
previous one on the same bus*. ROM and cartridge accesses are **not** aligned
and cost no wait states (`documented`, Cwik §"State 4") — the STE integrates the
bus buffers/latches into the GSTMCU, so that alignment logic is on-die, and
`documented` that there are no TTL buffers on the STE board.

**Consequence for CPU throughput (`derived`):** the CPU can never use more than
half the slots, so sustained CPU RAM throughput is at most 8 MHz ÷ 2 = one
16-bit access per 500 ns = **4 MB/s**, and only when the CPU issues back-to-back
aligned cycles. This matches the "CPU-visible throughput below 8 MHz" statement
in `atari-st-emulation/02-...:596-598` and gives it a number.

## 2. Video fetch slots

**How much is fetched (`documented`):** 16 000 word fetches per frame in
320×200 (`research/bus-arbitration.md` quoting the STE article: "16 000 times
per image"), i.e. 80 words per displayed line, i.e. **20 groups of 4 words**
(4 bitplanes × 16 pixels/word).

Emulator agreement:
- Steem SSE `glue.cpp:1810`: `CurrentScanline.Bytes = SCANLINE_BYTES_LOMED`,
  with `glue.cpp:53` `BYTE const SCANLINE_BYTES_LOMED = 160; // bytes of video
  memory per scanline, normal` = 80 words per low/medium-res line.
- Hatari `video.c:1460`: "One line uses 80 bytes instead of the standard 160
  bytes in low/med res" (the 80-byte/40-word half-line trick).

**How fast they arrive (`derived`):** active display in low res is 320 pixels
at an 8 MHz pixel clock = 40 µs = 320 CPU clocks. 80 words in 40 µs = **one
word per 500 ns = one word per 4 CPU clocks**. Since internal slots also recur
every 4 CPU clocks, **video consumes 100% of the internal slots during DE** —
not 50%, not a burst with gaps. That is the arithmetic identity which makes the
whole design work, and it is the strongest single conclusion in this note.

Group cadence in low res: 4 words per 16-pixel group → **one group every 16 CPU
clocks (2 µs)**, taking the 4 internal slots inside that window.

**Other modes do not change the rate (`derived`):**

| Mode | Pixels/line | Planes | Words/line | Word period | Internal-slot use during DE |
|---|---|---|---|---|---|
| 320×200 low | 320 | 4 | 80 | 500 ns | 100% |
| 640×200 medium | 640 | 2 | 80 | 500 ns | 100% |
| 640×400 mono | 640 | 1 | 40 | 500 ns | 100% |

Medium doubles the pixel clock (640 px in 40 µs = 16 MHz) so a word covers 16
pixels in 1 µs of display time but two planes are needed → still 2 words/µs.
Mono doubles the pixel clock again *and* halves the planes, but the line is only
28 µs long, so 40 words in 20 µs = 2 words/µs again. **In every standard mode
the Shifter demands exactly the internal-slot supply rate.** Only overscan (more
bytes per line, e.g. 224 or 230) pushes demand above supply, which is precisely
why overscan costs the CPU cycles — Cwik's "round up to 4" rule and the overscan
literature both attribute the cost to video taking internal slots.

**Border lines (`documented` + `inferred`):** no fetch on non-DE lines. Steem
`glue.cpp:1758` `TGlue::FetchingLine()` returns
`scan_y >= video_first_draw_line && scan_y < video_last_draw_line`, and the
`else` branch at `glue.cpp:1808-1813` sets `NextScanline.Bytes=0` when
`de_v_on` is false. Hatari's `SHIFTER_LINE` (`video.c:560-574`) carries
`DisplayStartCycle`/`DisplayEndCycle`/`DisplayBytes` per line and only points the
raster at display data between those cycles; outside DE the Shifter emits the
border colour and no fetch happens.
`derived`: with 200 active lines out of 313, 113 lines per frame carry no video
at all, and each such line offers all 128 of its internal slots to refresh and
sound.

**Timing within the line (`documented`, Hatari `video.c:939-955`):** DE rises at
cycle 56 (`HDE_On_Low_50`) and falls at cycle 376 (`HDE_Off_Low_50`), i.e.
320 clocks of display, matching the 40 µs. Horizontal blanking is cycles
376–512 plus 0–56 = 192 clocks = 96 slots = 48 internal slots per line.

**Where a group is placed inside the line (`unknown`).** Both emulators refuse
to model this. Hatari has no per-fetch schedule at all — its video code computes
*which* words a line needs (`ShifterLines[].DisplayBytes`), not *when* the MMU
fetches them. Steem renders a whole line in one call and fetches sound once per
scanline, with the comment that this is an approximation (`mmu.cpp:495-497`).
No vendored emulator models the per-slot arbitration Cwik describes.

## 3. Sound DMA slots

**One word per fetch, always (`documented`).** The Atari STE developer note
quoted verbatim in Steem SSE `mmu.cpp:473-490`:

> "In stereo mode, each word represents two samples: the upper byte is the
> sample for the left channel, and the lower byte is the sample for the right
> channel. In mono mode each byte is one sample. However, the samples are always
> fetched a word at a time, so only an even number of mono samples can be
> played."

So the stereo split is **a single 16-bit DRAM read, high byte = left, low byte =
right**, latched into two DACs — not two byte reads. This confirms and sharpens
`research/bus-arbitration.md`'s "the Shifter then emits it as two bytes on
`SD0–SD7`, each latched by a 74F374".

**Cost per fetch (`derived`):** one internal slot = 2 CPU clocks = 250 ns, i.e.
**one of the 128 internal slots on that line**. No fetch costs more than one
slot; there is no burst, no read-modify-write.

**Rate per sample-rate setting (`derived` from `documented` rates):**

| `SAMPMOD` | Rate (Hz) | Sample period | Word fetches/s (stereo) | Fetches per 64 µs line (stereo) | mono (half the words) |
|---|---|---|---|---|---|
| `00` | 6 258 | 159.8 µs | 6 258 | 0.40 | 0.20 |
| `01` | 12 517 | 79.9 µs | 12 517 | 0.80 | 0.40 |
| `10` | 25 033 | 39.9 µs | 25 033 | 1.60 | 0.80 |
| `11` | 50 066 | 20.0 µs | 50 066 | 3.20 | 1.60 |

Rates: Hatari `src/dmaSnd.c:215-218` `DmaSndSampleRates_STE[4] =
{6258, 12517, 25033, 50066}`; repo `02-...:882-887` (same figures, rounded).
Stereo = 1 word per sample, mono = 1 word per 2 samples: Steem SSE
`shifter.cpp:950-955` (`SteSoundSamplesCountdown += SteSoundFreq *
CyclesPerScanline` for stereo, `/2` "play half as many words" for mono).

**So sound DMA costs at most 3.2 of 128 internal slots per line — 2.5%.** It is
the cheapest client by an order of magnitude, and at 6 kHz it is invisible. Any
model that treats STE sound DMA as significant bus contention is wrong.

**Where in the line (`documented`, but weakly).** The Atari developer note says
the fetch happens "during horizontal blanking (transparent to the processor)".
Steem quotes that and then adds: *"Approximation: Fetching should happen as
soon as Video Enable stops, we do it at scanline."* (`mmu.cpp:495-497`). Given
§2's result that every internal slot is spoken for during DE, **the note and
the arithmetic agree**: sound fetches cannot happen during active display in
low res, so they land in the 48 internal slots of each line's blanking interval
(or, at worst, in the gap between 16-pixel groups if the MMU does fetch video in
4-word bursts). Which of those two is real is `unknown`.

**Buffer depth (`inferred`):** Steem models a 4-word Shifter FIFO
(`headers/shifter.h:56` `DIGITAL_SOUND_BUFFER_LEN = 4, // 4 words`) and refills
it from up to 4 words when `SREQ` is asserted (`mmu.cpp:473-490`,
`Glue`/`STVL` path `shifter.cpp:1053`). If real, the MMU grants up to 4
consecutive sound slots per refill — 1 µs of DRAM. Nothing in the hardware
documentation found here confirms a 4-word FIFO.

## 4. Refresh

**The two DRAM parts disagree, and that sets the rate (`documented` +
`derived`):**

| Part | Density | Organisation | Rows | tREF |
|---|---|---|---|---|
| 41256 | 256 Kbit | 512 rows × 512 cols | 512 | 4 ms class |
| 414616 | 64 Kbit | 256 rows × 256 cols | 256 | 4.194 ms |

(`documented`, repo `atari-st-hardware/memory/04-dram-chip-41256-414616.md:165`
— "the 41416 has a different internal matrix (64K = 256 rows × 256 columns)
versus the 41256 (256K = 512 rows × 512 columns)" — and `:163` tREF 4.194 ms.)

A controller must refresh the **slower** part's requirement, i.e. all 512 rows of
a 41256 within 4 ms → one RAS-only refresh every **7.8 µs**
(4 ms ÷ 512). `derived`. A 256-row 414616 then gets each row refreshed twice per
4 ms, which is harmless.

**Conflict to record:** `atari-st-hardware/memory/02-memory-controller.md:44-55`
states "414616 has 1024 rows (10-bit row address)" and derives a 3.9 µs refresh
period. That is inconsistent with the repo's own chip note (256 rows) and with
the 414616's 16-pin package (8 address pins cannot carry 10 row bits). **Treat
the 3.9 µs figure as wrong**; 7.8 µs (41256, 512 rows) is the defensible number.
Even if 3.9 µs were right, the bandwidth consequence is small (see below).

**Cost (`derived`):** one RAS-only refresh = one internal slot = 2 CPU clocks =
250 ns. Cwik's classification explicitly puts refresh in the internal class
(`documented`), so it competes with video and sound, not with the CPU directly.

**Per line (`derived`):** 64 µs ÷ 7.8 µs = **8.2 refresh slots per PAL line**
(≈ 16 if the 3.9 µs figure were used). The 48 internal slots of each active
line's blanking interval absorb ~8 of them. On the 320-clock display window
itself, video leaves nothing — per §2 it holds every internal slot. This is the
tension that makes the placement question real rather than academic.

**Refresh during DE (`unknown`).** Something must give: either (a) the MMU
preempts video for refresh (Cwik says "no priority of any kind", so this would
have to be a fixed sub-slot in the round-robin, e.g. one internal slot in N
given to refresh regardless), or (b) video does not in fact consume every
internal slot and the 4-word group is a burst followed by a gap, leaving 4 free
internal slots per 2 µs, or (c) refresh is deferred into blanking and the
controller simply runs a 512-row counter that happens to keep up over a frame
rather than over a line. **No source found resolves this.** Option (b) is the
most likely, and it is testable: see §7.

## 5. Priority and placement

**There is no priority (`documented`, Cwik).** This is worth stating flatly
because the repo's earlier note (`research/bus-arbitration.md:83-87`) records
"no source states the per-line slot allocation" and asks for a priority order.
The answer is that no such order exists at the internal/external level; the
round-robin is unconditional and the CPU is never starved (it always gets its
external slot) and never favoured (it always loses the internal slot).

**The order that does exist is temporal, not priority-based (`derived`):**

1. The internal/external alternation is fixed and free-running; neither side can
   jump it. A misaligned external access waits (Cwik).
2. Within the internal class the MMU decides *what* to put in each internal
   slot. Cwik does not describe this arbiter. From §2–§4 the line budget is:
   80 video + ~8 refresh + ≤3 sound = ~91 of 128 internal slots, leaving ~37
   (29%) genuinely idle. The tight part is not the line, it is the 40 µs display
   window, where video alone claims all 80 of that window's internal slots.
3. Therefore the scheduler is not "who wins" but "which internal slot carries
   which client", and any client that misses its deadline slips to the next
   internal slot — which is why `SREQ`/`*SLOAD` and `*DCYC`/`*LOAD` are
   handshakes rather than fixed-time events.

**CPU cycles surviving per line (`derived`):** 128 external slots × 2 clocks =
**256 CPU clocks of possible bus access per 512-clock line = 50%**. Of those,
the CPU only gets them if it has an aligned cycle pending; idle CPU clocks are
simply not used by anyone (Cwik: "MMU doesn't care if the internal slot is
actually used or not"). So the correct model is *the CPU gets one access per 4
clocks when it asks*, not *the CPU gets 256 of 512 clocks*.

**What this means for the tick-based design (`derived`, correcting
`research/deterministic-threaded-simulation.md:348-350`):** that note counts
"~80 video word loads … plus the CPU's ~512 bus slots" per line. Under the slot
model the real crossing count is **128 external slot boundaries + 80 video word
loads + ≤8 refresh + ≤3 sound ≈ 220 per line** in low res, and the CPU slots are
not independent crossings — they are a fixed 4-clock grid. At 2 µs quanta
(16 clocks) a quantum contains 4 external slots and 4 internal slots, i.e. the
quantum is *coarser than the arbitration*, so a quantum cannot represent a slot
decision. Either the model resolves to slots (4-clock granularity) or it must
treat a whole line's arbitration as atomic.

## 6. Per-line slot maps

Notation: 2-clock slots, `I` = internal (video/refresh/sound), `E` = external
(CPU/DMA/Blitter). `V` = video word, `R` = refresh, `S` = sound word.
A 512-clock line is 256 slots: 128 `I` + 128 `E`, one slot every 2 clocks
(250 ns), 64 slots per 16 µs.

**320×200 low res, PAL, line = 512 clocks**

| Clocks | Slots | Contents |
|---|---|---|
| 0–55 | 28 (14 I) | E/I alternating; 14 internal slots free → refresh (≈2), sound (≤1) |
| 56–375 | 160 (80 I) | DE. 80 internal slots all video (4 words per 16 clocks) + 80 external for CPU |
| 376–447 | 36 (18 I) | blanking; 18 internal slots free |
| 448–511 | 32 (16 I) | blanking + HSYNC; 16 internal slots free |
| **total** | **256 (128 I)** | 80 video, ~8 refresh, ≤3 sound, ~37 I spare; 128 E all available to CPU |

**640×200 medium, PAL, line = 512 clocks**

| Clocks | Slots | Contents |
|---|---|---|
| 0–55 | 28 (14 I) | as above, 14 I free |
| 56–375 | 160 (80 I) | DE. 80 I all video, but in **2-word groups every 8 clocks** (each internal slot still used) |
| 376–511 | 136 (34 I) | 34 I free |
| **total** | **256 (128 I)** | identical budget to low res: 80 video words per line |

**640×400 mono, line = 224 clocks (28 µs, 71 Hz)**

| Clocks | Slots | Contents |
|---|---|---|
| 0–3 | 2 (1 I) | 1 I free |
| 4–163 | 80 (40 I) | DE ≈ 20 µs: 40 I video (1 word per 8 clocks) + 40 E |
| 164–223 | 30 (15 I) | 15 I free for refresh/sound |
| **total** | **112 (56 I)** | 40 video words; refresh gets 28 µs ÷ 7.8 µs = **3.6 slots**, sound ≤1.4 |

Mono is the tight case: only 56 internal slots per line, 40 to video, ~3.6 to
refresh. The *fraction* is the same as colour (3.6/56 = 6.4%, 8.2/128 = 6.4%)
because the line and the frame both shrink together; what changes is that the
absolute slack per line falls from 37 slots to 12.

## 7. What remains unknown

> ### Correction — 2026-11-09, RTL re-read during ticket #20
>
> Items **1, 2 and 5** below are answered, and the answer is not the one an earlier pass gave. Re-reading
> `gyurco/gstmcu`'s control enables (§"Verification pass" of `gstmcu-reconstructions.md` read the
> `casez` as a priority chain) shows the three internal arms are **mutually exclusive by construction**,
> because the two select lines are derived from the same signals:
>
> - `mcucontrol.v:63` `refb = pk016 | pk024;` — `pk016` is `hde1` (the horizontal display window,
>   `gstmcu.v:906`), `pk024` is the latched `SREQ`;
> - `mcucontrol.v:95` `snden = ~pk016 & pk024;` — sound is enabled **only outside** the display window;
> - `gstmcu.v:142` `REF = addrselb & ~refb;` — refresh is selected **only** when the window is closed
>   *and* no sound request is pending.
>
> So the internal slot has exactly one owner, decided by one equation: **video while `hde1` is high,
> otherwise sound if `SREQ` is pending, otherwise refresh.** The `casez` is a decode table, not a
> priority encoder — no arm can meet another. `hde` is horizontal only (`gstmcu.v:907`
> `DE <= hde & vde`), so the window exists on all 313 lines, and the display-window internal slots of a
> border line are *reserved and idle*, not handed to refresh. What remains true from §4's tension is
> only that refresh must fit in blanking: it has 48 internal slots per line against 8.2 needed, so it
> fits with room to spare and never needs to preempt anything.
>
> Consequences: §2's "where a group is placed" is answered (each of the four words is a separate slot
> decision, `gstmcu.v:1078` `vid = vid_reg + 1`, no burst state machine) **and** §4's "refresh during
> DE (unknown)" dissolves — refresh does not occur there. Item 1's second half ("can sound and refresh
> be serviced during DE") answers **no**, by enable, not by priority. The caveat below stands: the
> refresh *address* is hard-wired to 0, so this proves the slot, never the row.

1. **The intra-line order of internal clients.** Nothing found (STE article,
   service manual, Cwik, Hatari, Steem) states whether a video group is fetched
   as 4 consecutive internal slots or interleaved 1-per-slot across 16 clocks.
   This single fact decides whether sound and refresh can be serviced during DE.
   *Measurement:* assert `SREQ` at a known cycle with video active and watch the
   delay to `*SLOAD` on a logic analyser; if the delay is bounded by 4 clocks
   the fetches are interleaved, if it can reach 2 µs they are bursty.
2. **Whether refresh preempts video.** Cwik's "no priority of any kind" implies
   it cannot; arithmetic implies it must sometimes. *Measurement:* count `RAS`-
   only cycles within a DE window.
3. **Exact refresh period.** 7.8 µs is derived from the 41256's 512 rows and a
   4 ms budget; the repo's own 3.9 µs figure contradicts it. *Measurement:*
   scope `RAS0` with `CAS` suppressed and time consecutive refresh pulses.
4. **Sound fetch placement and FIFO depth.** Steem's 4-word FIFO is a modelling
   convenience with no documentary support. *Measurement:* the `*SLOAD` burst
   length at 6.25 kHz, where fetches are 160 µs apart and should be isolated.
5. **What happens on an internal-slot collision** — e.g. a sound deadline and a
   video word landing on the same 250 ns. Cwik says no priority, so one of them
   slips; which one is unstated.
6. **Blitter and disk DMA interaction with the slot grid.** `research/
   bus-arbitration.md` covers the `BR`/`BG`/`BGACK` handshake; whether a granted
   Blitter cycle then occupies every external slot in a burst (Hatari models
   4 cycles per word, `blitter.c:387-396`) or one in two is not established.
7. **STE-specific divergence from the ST.** All of the slot arithmetic above is
   ST-derived (Cwik) and assumed to carry over to the GSTMCU. The STE adds sound
   DMA to the internal class; whether the GSTMCU widened the internal share to
   accommodate it is `unknown` and would show up as a measurable CPU slowdown
   when DMA sound is enabled — a clean experiment on real hardware.
