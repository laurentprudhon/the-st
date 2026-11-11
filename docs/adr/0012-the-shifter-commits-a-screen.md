# The Shifter commits a screen, not a pixel stream

Video is the one chip whose output is a picture, and a picture is 3.2 M pixels a second — two orders
above every other record the machine makes. This ADR fixes the shape of that output once: **Video owns
two virtual screens and writes 16 pixels into them per iteration of its state machine; it appends no
record per pixel, per chunk or per group, and nothing derives the picture from the logs.** The screens
are the visible raster only — borders included, blanking excluded — at 840×284 in colour and 640×400 in
mono. The border is palette colour 0 in colour modes and always black in mono, and it is *written*, not
cleared, because colour 0 can change line by line.

**Status**: accepted (2026-11-11), on the owner's word during ticket #27's session — *"ok for ADR
0012"*. Ticket: #27. Depends on ADR 0001 (one timeline), 0004 (wires are logs), 0006 (subscriptions),
0008 (write shapes, anchors, the wake rule) and 0011 (Memory owns the slot and the group delivery).

## Context

- Video receives **one group delivery per four words** (4 000/frame) and four `MD0–MD15` payload
  records; it never sees a slot decision and never asks for a fetch (ADR 0011, owner's ruling on #20).
- A record per pixel is ~3.2 M/s; a record per 16-pixel chunk is 4 000/frame *on top of* the 4 000
  deliveries and 16 000 payload records. ADR 0006/0008 exist to keep the event stream at the crossings,
  not at the sample rate.
- The alternative — commit registers and derive the picture — was argued in the session and rejected by
  the owner: *"the video subsystem should include a virtual screen … and the video subsystem writes 16
  pixels on it each time it runs (when in the visible screen), and the host can subscribe to callbacks …
  so no derivation, but a virtual screen that is written to in sync with the video subsystem state
  machine."*
- The screen's arithmetic is the machine's own: a 64 µs line whose visible part is 420 cycles
  (52.5 µs), 313 lines of which 39 are upper border, 200 display, 45 lower border and 29 vertical
  blanking. Atari's *Engineering Hardware Specification* supplies the border rule; the STE *Developer
  Addendum* confirms there is no fourth resolution to place on this raster.

## Decision

### 1. The screen is state, and Video is the only writer

- Two buffers, allocated once at start-up and never reallocated: **colour 840 × 284** and **mono
  640 × 400**, 16-bit `0RGB` (4 bits per channel) per pixel.
- **Two public constants record the physical display area of the monitors the machine was built for** —
  colour **216 × 160 mm** (SC1224) and monochrome **210 × 130 mm** (SM124), both 12-inch — so the host
  scales the buffer onto the original picture's proportions (1.35 : 1 and 1.615 : 1) instead of guessing
  a square-pixel stretch.
- A **chunk** is 16 pixels — one iteration of the shifter. A **group delivery** drives one chunk in low
  res, two in medium, four in mono, so a frame writes 4 000 / 8 000 / 16 000 chunks.
- Column of a pixel = `(instant − blank release) / ticks per pixel`; a low-res pixel covers two columns
  at the 16 MHz reference, medium and mono one.
- **No blanking rows, no blanking columns.** The buffer holds the visible raster and nothing else.

### 2. The border is written, not cleared

- While a line is visible, every column of it is written: fetched pixels inside `DE`, **palette colour
  0** outside it. While `BLANK` is asserted nothing is written.
- In mono the palette is bypassed and the border is **black**; the mono screen has no border at all.
- Consequence, and the reason to state it as a rule: a mid-frame write to `$FF8240` recolours the
  surround line by line, which is what the hardware does and what demos rely on.

### 3. The pixel counter is committed

- `pix_cntr` (0–15 within a chunk, 31/63 overshoot after a mid/mono line's last reload) is internal
  state, anchored **once per visible line** (284/frame). It is what makes the `DE`-edge unloaded words,
  the overshoot and the 336-pixel opening real instead of approximate.
- Cost: a line's pixels are not a closed form over that line's records — the derivation carries the
  counter across the 200 display lines. Paid knowingly (owner's Option B ruling).

### 4. Three subscriptions, one level read, and no deadline

- Video subscribes to the **group delivery**, to **`DE`**, and to every **`CS` write** to its registers.
- **`BLANK` is a level read at the pixel's instant**: quantising it to chunk boundaries would move the
  border by up to 16 pixels.
- `LATCH` is internal (the absorbed palette latch's strobe); `MW*` is not Video's at all.
- **No `nextDeadline`**: ADR 0008 §5's exclusion of Video stands unchanged.

### 5. Register writes are `Write`, never `WriteAndWait`

With no reader inside the machine, a palette or mode record's **instant** is the whole ordering: pixels
are emitted strictly later than the write, so the value in force is the last record at or before the
pixel's instant. A CPU read-back is a class-2 exchange answered from the latch.

### 6. The Microwire controller is Sound's

`$FF8922`/`$FF8924` and the shift that drives the LMC1992 belong to **Sound**, although the pins are on
the Shifter's silicon — the `SREQ` precedent: physical location does not decide ownership, the timeline
does. Since the LMC1992 is a Sound member, the Microwire wire never leaves that virtual chip and there is
no crossing row for it.

## Invariants

1. Video appends no record on the hot path: not per pixel, not per chunk, not per group.
2. The screen is written only at instants Video already runs, from state the machine already decided —
   so equal digest streams still paint equal screens.
3. The screen is the visible raster: 840×284 colour, 640×400 mono, borders in, blanking out.
4. The border is colour 0 (colour modes) or black (mono) at the pixel's own instant.
5. A palette or mode write takes effect from the next emitted pixel, ordered by its instant alone.
6. The host's callbacks are notifications, not wires: no record, no ordering, no obligation.

## Consequences

- **#10** writes the screen instead of a pixel log; it adds the three callbacks, the 284 counter anchors
  and the debug re-derivation falsifier (§Falsifier 4), and G5's per-chip counts gain the chunk rate
  (4 000 low res) beside the 4 000 deliveries.
- **#12** reads the screen — 840×284 or 640×400, `0RGB` — and owns scaling, the window and the blit path.
  It scales onto the **recorded display area** (216 × 160 mm colour, 210 × 130 mm mono), so the picture
  keeps the monitor's proportions rather than a square-pixel stretch. It no longer needs the derivation
  this ticket's first draft proposed.
- **#26** inherits `$FF8922`/`$FF8924`, the Microwire shift, and the LMC1992 decode; the STE's `MW*`
  link is a Sound-internal wire.
- **#6**'s catalogue loses a row it never should have had (`MW*`) and gains no new one; `crossings.md`
  §2's super-hi-res row is corrected in place with a dated block.
- **#11** changes nothing in the gate: frames are still never compared, and the screen is not a gate
  subject — M1 is what proves the picture.
- **Vocabulary**: *group delivery*, *payload record*, *chunk*, *virtual screen* enter `GLOSSARY.md`.

## Considered options

- **A record per 16-pixel chunk** (4 000/frame, doubling crossings, carrying the colour indices). Rejected:
  it duplicates what the screen already holds, doubles the event stream at the exact place ADR 0006
  exists to protect, and no chip in the machine reads it.
- **A record per line** (200/frame, a line digest). Rejected: a digest of pixels answers "did it change",
  not "what is on screen", and the screen answers both for free.
- **A decimated pixel stream under ADR 0008 §7 condition 6.** Rejected: the condition exists to let a
  *leaf* skip records, not to make the display a record stream.
- **Commit registers and derive the picture in the host** (the session's first recommendation).
  Rejected by the owner: the picture is the chip's output, the screen is in-sync by construction with
  the state machine, and the host is left with scaling only.
- **A per-group sink into the host** (4 000 callbacks carrying pixels as they are produced). Rejected:
  it makes the display a second path that the digest does not cover, and the screen plus a callback
  gives the same liveness without it.
- **The tube at 1024×313 including blanking.** Rejected by the owner: blanking is not visible, so it is
  neither stored nor written.

## Falsifier

1. **A pixel that the screen cannot explain from committed state** — a chunk whose columns do not follow
   from its instant, the register records and the counter anchor — means the write path reads something
   outside the model: fix the write, not the screen.
2. **Two runs of one image whose digest streams are identical but whose screens differ** at any frame
   means the screen was written from something other than record-driven state (a host read, a shared
   buffer, an allocation accident). This is the falsifier that matters, because the screen is outside
   the digest's direct coverage.
3. **A border that does not follow `$FF8240`** — write colour 0 during the VBL and the surround must
   change — means the border is being cleared or sampled at the wrong instant.
4. **The debug re-derivation differing from the screen** at any line of any frame reopens the commit set
   (§Decision 3): an anchor is missing, and the fix is an anchor, never a record per pixel.
5. **A raster demo whose picture is offset by a whole chunk** (16 pixels) at a scroll or border-opening
   edge means the chunk boundary is wrong, not the scroll: reopen the write rule, not the format.

## Evidence labels carried through

- `documented`: the border is palette colour 0 in multi-plane modes and black in mono (*Engineering
  Hardware Specification*); the two monitors' display areas — 216 × 160 mm (SC1224) and 210 × 130 mm
  (SM124), both 12-inch (*Atari Corp (UK)*, "SM124 / SC1224 Monitors", issue 1-302/985, 1985, §PHYSICAL);
  the 11-bit Microwire message and its mask routine (*STE Developer Addendum*); the absence of a fourth
  STE resolution (same document, plus the STE FAQ).
- `derived`: 840 columns and 284 rows (52.5 µs at 16 MHz; 39 + 200 + 45 lines), the 28/448 blanking
  cycles, the 1/2/4 chunks per group.
- `implemented` in the reconstruction: the reload cadence (`shifter_video.v:83,106`), the palette bit
  order (`gstshifter.v:236-237`), the 31/63 overshoot (`gstshifter.v:292-295`), the unloaded words
  (`shifter_video.v:110-111`).
- `disputed`: which chip latches `$FF8261` (the reconstruction decodes `A[15:1]` and cannot tell);
  the border's exact edge within a chunk.
