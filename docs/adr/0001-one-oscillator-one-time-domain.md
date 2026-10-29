# One oscillator, one time domain

The machine model has a single 32 MHz oscillator and derives every clock from it by a power of two,
including the 8 MHz sound clock that a real STE takes from a separate 8.010613 MHz crystal. Machine
time is the machine's only time domain, so the scheduler never converts between clock domains. The
accepted divergence is −0.133 % (−2.3 cents) on the audio and PSG paths and the same on floppy bit
windows, because no documented software measures any of it, and because a second oscillator would buy
a permanent second-rate mechanism for a difference below measurement.

**Status**: accepted (2026-10-29)

## Context

- A PAL STE has two crystals: the 32.084988 MHz master (CPU = 8.021247 MHz) and a separate
  8.010613 MHz sound oscillator, `ATARI_STE_EXT_OSC` / SCLK / OSC U303 in Hatari's clock table.
- SCLK is **not** the master divided by four: the ratio is 4.00531. Its clocked subsystems are the
  DMA audio divider, the WD1772 floppy controller, and the YM2149 via SCLK/4 = 2.002653 MHz — so the
  "sound clock" is the clock of the audio, floppy-speed and PSG cluster, not of the audio alone.
- The four DMA sample rates are integer divisors of SCLK: 50 066 = SCLK/160, 25 033 = SCLK/320,
  12 517 ≈ SCLK/640, 6 258 ≈ SCLK/1 280. None divides 32 MHz.
- Because the ratio is not exactly four, the two domains do not hold a fixed phase offset: their
  relative phase slides by one SCLK period every ~94 µs, which is shorter than a scanline. "Two
  oscillators" would therefore mean a continuously sweeping sub-tick phase, not a constant skew.
- Prior art, read from the vendored sources: Hatari keeps one integer cycle counter and declares SCLK
  in its clock table without ever reading it at runtime (audio is generated from the host rate off the
  CPU counter); Steem SSE keeps one cycle counter and emits samples at the real rate through an
  integer accumulator, at CPU-cycle granularity; NeoST keeps one timeline with per-device fractional
  remainders and knowingly carries the −0.13 % error on its FDC. None of the three models a second
  time domain in its scheduler.
- The owner's instruction while charting was "only one 32 MHz clock source". The hardware disagrees;
  this ADR records the instruction, the hardware fact it overrules, and the exact cost of that.

## Decision

- **Timebase**: idealised 32 000 000 ticks/s. One line = 2 048 ticks (64 µs); one frame = 640 000
  ticks (20.000 ms, 50.000 Hz). The master is treated as exactly 32 MHz, not as the real crystal.
- **Every clock is 32 MHz ÷ 2ⁿ**: video 16 MHz, CPU/bus 8 MHz, sound DMA 8 MHz, PSG 2 MHz, floppy bit
  clock 8 MHz.
- **One oscillator, two clock domains, one time domain.** The clock domains are the main tree and the
  sound path; their frequency ratio is exactly 4 in this model (4.00531 on hardware). Machine time is
  the only timeline, so no event is ever converted between domains.
- **The whole SCLK cluster derives**, not just audio: sound DMA, WD1772 bit rate and YM2149 all take
  their clock from the derived 8 MHz.
- **Periodic events have integer tick periods**; no fractional period appears anywhere in the model.

## Divergences accepted

- Sound and PSG: 8.000000 / 2.000000 MHz against 8.010613 / 2.002653 → −0.133 %, i.e. −2.3 cents in
  pitch and −0.133 % in tempo.
- FDC: bit windows 0.133 % short when measured in CPU cycles (≈5 CPU cycles on a 500 µs gap).
- The timebase itself: 50.000 Hz against 50.053 Hz, CPU 8.000 against 8.021 MHz.
- Gained in exchange: the sample rates become 6 250 / 12 500 / 25 000 / 50 000 Hz, which are exactly
  125 / 250 / 500 / 1 000 samples per frame. On hardware none of those counts is integral.

## What software could observe

Three different strengths of "not observable", which the design must not conflate:

- **Unobservable in principle.** Reading the address counter (`$FFFF8909/0B/0D`) returns a buffer
  position, whose meaning is identical here and on hardware; only its rate could differ, which is the
  next group. Comparing playback against a recording of real hardware is a human judgement, and
  2.3 cents is below the ~5-cent discrimination threshold and below the tuning accuracy of any 8-bit
  DMA source.
- **Observable only by a purpose-written measurement.** A program that counts MFP Timer A ticks across
  a known sample count, or the address-counter delta per frame, or a floppy gap length in CPU cycles,
  reads a value 0.133 % off hardware. No documented software or validation target does that, and the
  software we do know about is insensitive to it: players read the DMA state rather than assume a
  ratio, and copy-protection schemes work in ±5–20 % windows. This ADR does not claim the divergence
  is invisible — it claims nothing known looks.
- **A judgement about contention.** Music synchronised to VBL, with palette changes per line while
  samples play, differs only where sound and video fetches compete for the same memory slot. At
  Level 1 no contention is modelled, so there is no observable to differ; at Level 2 there is.

**The one thing genuinely lost**: on hardware the phase between a sound fetch and a video fetch sweeps
through every alignment continuously; here it is constant forever, so contention between them would
resolve identically each frame at a fixed offset. That is a Level 2 fidelity gap, not a wrong result at
Level 1, and it is tracked on the map's audio-fidelity fog rather than only here.

## Consequences

- The scheduler ticket needs no cross-domain rule: instants are machine ticks, ordering is within one
  domain. The SCLK ratio must stay a single local constant so Level 2 can correct it.
- Level 2 may set the ratio to 4.00531. Periods then stop being integers, so the periodic-event
  mechanism must not forbid a rational period; the contention phase rotation comes back with it.
- Host pacing uses 50.000 Hz, 0.1 % slower than a real PAL STE.
- Vocabulary: oscillator, clock, clock domain and time domain are four distinct terms (see
  `GLOSSARY.md`); the map's "one clock" note was a decision, not a description of the hardware.

## Considered options

- **Two oscillators with an exact rational ratio and accumulators.** Rejected: a permanent second-rate
  mechanism for 2.3 cents, and — decisively — the hardware does not offer a constant offset to model,
  since the relative phase slides a whole cycle every ~94 µs.
- **Real crystal timebase (32.084988 MHz, 641 024 ticks per frame, 50.053 Hz).** Rejected: it costs
  the round second and the integral sample counts, and no accuracy level requires it.
- **Derive audio and PSG but keep the WD1772 on the real rate.** Rejected: an exception to the
  invariant, bought for bit-level floppy tests we have no evidence anybody runs.
- **Not model the sound clock at all, as Hatari does.** Rejected: it produces the same 0.133 % while
  hiding the hardware fact, so the divergence becomes invisible to future readers instead of documented.

## Falsifier

A real-hardware test showing a demo or music class depends on the sample-to-line phase rotation, or a
documented program or burn-in test that measures the SCLK-to-MCLK ratio, reopens this ADR and promotes
the second clock domain from a constant to a scheduler concern.
