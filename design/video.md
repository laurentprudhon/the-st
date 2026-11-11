# The Video chip — the shifter, the screen, and what it never records

Implementation-facing companion to ticket [#27](https://github.com/laurentprudhon/the-st/issues/27) and
[ADR 0012](../../docs/adr/0012-the-shifter-commits-a-screen.md). Decisions live in the ADR; this file is
the shape the code takes, so #10 and #12 have nothing left to interpret. Engine rules assumed: ADR 0003
(a chip owns its clock), ADR 0004 (wires are append-only logs), ADR 0006 (subscriptions), ADR 0007
(no priority table), ADR 0008 (two write shapes, anchors not samples, a wake only where a chip meets
another chip), ADR 0011 (Memory owns the slot and the group delivery).

Rates are `design/crossings.md`'s. Group arithmetic is `design/memory.md`'s. The Microwire ownership is
`research/ste-super-hires.md`'s.

## 1. What the chip is

**Members (inside Video):** the plane shift registers and their reload logic, the **pixel counter**,
the palette latch (absorbed `U511`), the pixel pipeline (absorbed `U500/U501`, a fixed two-pixel
latency), the register file (`$FF8240–$FF825F`, `$FF8260`/`$FF8261`, `$FF8264`/`$FF8265`), and **the
virtual screen**.

**Not members, and not Video's:** the 4-word fetch buffer and every fetch decision (Memory's, ADR 0011);
the Microwire shift and `$FF8922`/`$FF8924` (**Sound's** — the pins are on the Shifter, the timeline is
the audio one; `SREQ` precedent, owner's ruling 2026-11-05 and on #27, 2026-11-09); the `DE → LOAD`
pipeline (`U304/U306`, Memory's); the analog tail (`U403`, `U405`, the DACs, the filters).

**There is no fourth resolution on this board.** Super Hi-Res is the TT's; the `MWE`/`MWD`/`MWK` pins
are the Microwire link (`research/ste-super-hires.md`).

## 2. What arrives, and what Video takes

| Arrives as | Wire | Rate | How Video treats it |
|---|---|---|---|
| **group delivery** | `LOAD`+`DCYC` | 4 000/frame | **subscription** (class 1): the only thing that starts the shifter |
| **payload** | `MD0–MD15` | 16 000 records/frame | sampled at the delivery instant, never subscribed |
| display window | `DE` | 200 records / 400 firings | **subscription** — it gates the write and feeds the pixel counter |
| blanking | `BLANK` | 313 records | **level read at the pixel's instant**: `BLANK` asserted paints nothing |
| register writes | `CS` on the Shifter | ~10 idle → 939 in a raster demo | **subscription**, one per write |
| palette latch strobe | `LATCH` | — | internal: the palette record's instant *is* the load instant |

**Video takes exactly three subscriptions and declares no `nextDeadline`.** Every iteration is ended by
a read or a write (ADR 0008 invariant 3), so there is nothing for the assertion to catch; its output is
phase and its state changes on deliveries, selects and levels.

**Per-line work is `DisplayBytes / 4` deliveries** — 20 on a low-res line, 20 in medium (80 words),
40 in mono. Nothing here is precomputed at the line boundary.

## 3. The shifter, one chunk at a time

```
tick      = 1/32 MHz = 31.25 ns                 (ADR 0001's master clock)
pixel      = 4 ticks low (8 MHz) · 2 medium (16 MHz) · 1 mono (32 MHz)
chunk      = 16 pixels = one iteration = one write into the screen
group      = 4 words = 1 chunk low · 2 medium · 4 mono
line       = 2048 ticks = 512 cycles
visible    = cycles 28…448 = 420 cycles = 1 680 ticks = 52.5 µs
```

- **One iteration consumes one chunk**: it shifts 16 pixels out of the plane registers, resolves each
  through the palette (or the mono inverter), and writes them to the screen at their columns. A group
  delivery drives 1, 2 or 4 iterations by mode — 4 000 / 8 000 / 16 000 chunk writes a frame.
- **Reload is the mode's word grouping**: 4 words in low res, 2 in medium, 1 in mono. That is the RTL's
  `rdelay` (four `LOAD`s ⇒ one reload, `shifter_video.v:83,106`) read through the mode.
- **The pixel counter is state** (owner's ruling, Option B). It counts pixels within the 16-pixel
  window, is enabled at `DE`'s edges, and on the last reload of a mid or mono line it is loaded with 31
  or 63 instead of wrapping (`gstshifter.v:292-295`). It is what makes three hardware behaviours real:
  the **1–2 words left unloaded** at the `DE` edges under hard scroll (`shifter_video.v:110-111`), the
  **overshoot** that decides where the next line's first pixels sit, and the **336-pixel opening**
  (write `$FF8265 > 0`, then `$FF8264 = 0`: the shift registers are already loaded, so the line starts
  16 pixels early).
- **The counter is anchored once per visible line** (§8) and carried across lines by the derivation —
  that is the whole cost of Option B.

## 4. The register file

| Register | Owner | Behaviour |
|---|---|---|
| `$FF8240–$FF825F` | Video | 16 words, **4 bits per channel**, R at 11–8, G at 7–4, B at 3–0. Atari's own wording: *"the least significant bit is added above the old most significant bit"* — the RTL's `0321` rotation (`gstshifter.v:236-237,257-263`) is that serial order. The value applies **from the next emitted pixel** |
| `$FF8260` | **both** | one write, two latches: Memory's timing mode (`mde1`/`mde0`) and Video's plane mode (`shmode`) |
| `$FF8261` | Video | plane mode only — the documented trick that lets software make the two disagree |
| `$FF8264` / `$FF8265` | Video | the pixel offset, 0–15. `$FF8265` also asks for the prefetch group (a **Memory** count rule, §6) |

**Every one of these writes is `Write`, never `WriteAndWait`.** With no per-pixel record the palette has
no reader inside the machine: its only consumer writes pixels at instants strictly later than the
write's, so the instant on the register record *is* the ordering. A CPU read-back is a class-2 exchange
answered from the latch.

**Declared approximate, with reversal facts:** the `$FF8260` data-bus serialisation (the Shifter reads
bits 8–9 of the bus, so a word write takes the high byte), and **value 3** (GLUE reads it as high res,
the Shifter *stops* — Troed's 4-pixel hardscroll). Both are cycle-exact demo territory (D3); modelling
them adds a data-bus-lane dependency and a "stopped" state that nothing in the skeleton exercises.

## 5. The virtual screen

**Two formats, visible parts only — no blanking, borders included, 16-bit 0RGB per pixel:**

| Mode | Screen | Columns | Rows | Size |
|---|---|---|---|---|
| colour (low 320×200, medium 640×200) | the PAL colour raster | **840** | **284** | 477 120 B |
| mono (640×400) | the SM124 raster | **640** | **400** | 512 000 B |

### 5.1 The two physical sizes — public constants of the chip

The host scales the picture, so the chip publishes **the physical display area of the two monitors it was
built for**, from Atari's own specification sheet (Atari Corp (UK), issue 1-302/985, 1985):

| Constant | Monitor | Display area | CRT |
|---|---|---|---|
| `ColourMonitor` | SC1224 | **216 mm × 160 mm** | 12", 305 mm diagonal, dot trio 0.385 mm |
| `MonochromeMonitor` | SM124 | **210 mm × 130 mm** | 12", 305 mm diagonal, paper white |

```csharp
// Video's two reference monitors — the host scales the screen onto one of these areas.
// Source: Atari Corp (UK), "SM124 / SC1224 Monitors", issue 1-302/985, 1985, §PHYSICAL.
public static readonly MonitorSize ColourMonitor     = new(WidthMm: 216, HeightMm: 160); // SC1224
public static readonly MonitorSize MonochromeMonitor = new(WidthMm: 210, HeightMm: 130); // SM124
```

What they mean, and the numbers the host derives from them (`derived`):

| | mm per buffer column | mm per buffer row | Pixel aspect (w × h) | Area aspect |
|---|---|---|---|---|
| colour, low res | 216 ÷ 840 = 0.2571 | 160 ÷ 284 = 0.5634 | 2 cols × 1 row = **0.514 × 0.563 → 0.91** | **1.35 : 1** |
| colour, medium | same | same | 1 col × 1 row = **0.257 × 0.563 → 0.46** | 1.35 : 1 |
| mono | 210 ÷ 640 = 0.3281 | 130 ÷ 400 = 0.3250 | **0.328 × 0.325 → 1.01** | **1.615 : 1** |

- **The buffer maps onto the area, borders included**: the whole visible signal fills the monitor's
  raster, so a host that scales the 840 × 284 buffer to 1.35 : 1 reproduces the original picture's
  proportions — square-pixel output would stretch it by 2.2× in width.
- The mono area is 1.615 : 1 against a 640 × 400 raster, which is why the SM124 is the one monitor whose
  pixels are square, and why mono text looks correct there and squat elsewhere.
- Both monitors are 12-inch, 305 mm diagonal; the alternatives of the era (SC1435 14", SM144/SM147) are
  listed in §10 as not modelled.


```
colour columns = 52.5 µs × 16 MHz = 840      (52.5 µs = cycles 28…448; 16 MHz = the max colour pixel rate)
colour rows    = 39 upper border + 200 display + 45 lower border = 284   (the frame's last 29 lines are blanked)
```

Reproduced by the derivation and true of the tube: the last 29 lines of the 313 are vertical blanking
and are **absent** from the buffer — they are not black rows, they are not rows.

**The write rule, one sentence:** *while the line is visible, Video writes every column of it — fetched
pixels inside `DE`, **palette colour 0** outside it — and writes nothing while `BLANK` is asserted.*

- **The border is written, not cleared.** Atari's *Engineering Hardware Specification*: *"Palette color
  0 is also used to assign a border color while in a multi-plane mode. In monochrome mode the border
  color is always black."* So a mid-frame write to `$FF8240` recolours the surround line by line,
  exactly as it recolours pixels, and there is no clearing pass at frame start.
- **In mono the border is black and the screen has none**: 640×400 of picture, palette bypassed (bit 0
  of colour 0 only selects inverse video).
- **Low res doubles**: a low-res pixel is 2 columns at the 16 MHz reference, so a chunk writes 32
  columns; medium and mono write 16.
- **Column = `(instant − blank release) / ticks per pixel`**, so a chunk's position is arithmetic on
  the instant it is written at — no offset table, and border tricks land where the beam says.
- **A mode change mid-frame re-geometries, it does not reallocate**: the two buffers are allocated once
  at start-up, and the mode selects which one a chunk lands in.

## 6. Scroll

Three things carry the name, and they belong to two chips:

| Register | Chip | Effect on Video | Effect on Memory |
|---|---|---|---|
| `$FF820F` `LINEWIDTH` | Memory | none | the line-to-line address step |
| `$FF8265` (prefetch) | Video (the offset) / Memory (the count) | offsets the shift by 0–15 px | **one extra group per line** while non-zero: 21 deliveries/line, 4 200/frame |
| `$FF8264` (no prefetch) | Video | offsets the shift by 0–15 px | none; the display starts 16 px late and ends 16 px short — the counter's unloaded words produce it |

- Video models **one** `pixel_offset` (0–15) and applies it when shifting out.
- The prefetch is **Memory's count**, not Video's work: `$FF8265 ≠ 0` ⇒ one more group per line. G5's
  **4 000/frame stays exact** because GEM does not scroll.
- The three counter effects are **modelled** (Option B), not approximated: they are pixels in the
  screen, which is exactly what makes the screen worth committing.

## 7. What crosses out, and what never does

**Out:** nothing of Video's own on the hot path — the chip appends no record per pixel, per chunk or per
group. Only its register writes enter the log (palette, mode, scroll: ~10 a frame idle, ≤ 939 in a
raster demo) and its **line-start counter anchor** (§8).

**Never (invisible by construction):** the shift registers' contents, the 16 pixels of a chunk, the
pixel counter's inner steps, the palette lookup, `LATCH`, the mono inverter.

**Host-facing, and not wires:** the screen and its three callbacks (§9).

## 8. Records, anchors, deadlines

**Anchors** (internal register log, one driver, stamped with Video's clock — ADR 0008 §6):

| Log | Anchor at |
|---|---|
| palette | every write (16 words, value + index) |
| plane mode, timing mode, pixel offset, MW state | every write |
| pixel counter | **each visible line start (284/frame)** — the value the next line begins with, which is what carries `pix_cntr` across lines |

Every other instant of the counter is arithmetic. The palette and the mode are logged on every write
because a write is precisely what changes them (ADR 0008 §6's "a register that changes only on a
software write is logged on every write").

**`nextDeadline`: absent**, deliberately. ADR 0008 §5 already excludes Video (driven by deliveries,
selects and levels; its output is phase). Nothing new here.

**Rate test (G5):** 4 000 group deliveries/frame, 4 000 chunk writes in low res (8 000 medium, 16 000
mono), 284 counter anchors, ≤ 939 register writes in the worst raster demo.

## 9. The output-stage seam

- **The screen is Video's state, written by Video, at the instants Video runs.** The host does not
  derive pixels from logs; it reads the screen. What it does with it — scaling, window, blit path,
  refresh cadence — is #12's.
- **Three host callbacks, fired from Video's own run:** *group written* (4 000/frame), *line complete*
  (200/400), *frame complete* (50/s). They are **not wires**: no record, no subscription row, no
  ordering, and a host that ignores all of them changes nothing in the machine.
- **Video's business stops at the digital pixel**: 0RGB in a 16-bit word (1 bit replicated in mono, with
  the inverse-video bit applied). The analog tail and the synchronisation to the host's display are the
  host's.
- **Determinism still holds, as an argument and now also as a check.** The screen is written only from
  record-driven state at instants the machine already decided, so two identical digest streams still
  paint identical screens. The check is a debug-only falsifier (§10.3): re-derive one line per frame
  from the logs and compare it with what the screen holds.

## 10. Open points

1. **The 52.5 µs visible window is `derived`** from the timing tables (blank asserted at cycle 448,
   released at 28 — hardware overview §6.3, Hatari's model), not read off a scope. If it is off, the
   840→832/848 and the picture's centring move; M1 is the check.
2. **The border colour at the extreme edges is `derived` too**: colour 0 is documented, but *where*
   colour 0 takes over from the fetched pixels (the exact `DE` edge within a chunk) is settled by the
   pixel counter, and a demo that hard-scrolls is the only thing that would prove the last pixel.
3. **The screen-versus-derivation falsifier is not in the gate yet.** `design/validation.md` gains no row
   from this ticket; the debug re-derivation is written with #10 and reported, never gated (frames are
   still never compared).
4. **Mono timing under the fixed 50 Hz time base.** The screen format is decided (640×400, no border);
   how the high-res raster's line and frame counts sit in ADR 0001's 640 000-tick frame is not, because
   the skeleton paints low res. A mono-display ticket reopens it, with the SM124's ~71 Hz raster named
   as the reference.
5. **`$FF8920`/`$FF8921` (SAMPMOD)** is decoded inside the Shifter in the reconstruction and listed as
   Memory's in `design/subsystems-spec.md` §1. It is Sound's business (#26), recorded here so the
   Microwire ruling does not look like it left a hole.
6. **`$FF8260`'s second subscriber is a source conflict, not a doubt**: the reconstruction decodes
   `A[15:1]`, so it cannot tell `$FF8260` from `$FF8261`; the hardware overview, Hatari and Atari's own
   "two registers" wording all say they differ. Two registers are modelled; the reconstruction's shared
   decode is the divergence, and a scope on `U401.18` (`CS`) would settle it.
7. **The physical constants are the 1985 pair** — SM124 and SC1224, both 12"/305 mm. Later European
   bundles shipped the **SC1435 (14")** and **SM144/SM147**, whose display areas are not in the
   specification sheet we hold; adding one is a constant plus its source, not a design change, and the
   host picks by machine variant (this machine is fixed, so the pair above is the default).
8. **How much of the display area the border occupies is `derived`**, not measured: the 216 × 160 mm is
   the monitor's usable area, and the mapping assumed here is "the whole visible raster fills it". A
   photograph of a real SC1224 with a calibration grid, or M1 against a screenshot, would prove the
   picture's fraction of the area. If it is wrong, only the mm-per-pixel rows of §5.1 move — the aspect
   of the *buffer* (1.35 : 1) does not.
