# What the schematics and the service manual settle

Status: research note, 2026-11-01. Sources: the 520/1040STE **Field Service Manual** (Atari, text
layer extracted page by page) and the STE schematic redraw (`STe_Schematic_10-3-1989.pdf`, six sheets —
**scanned images, no text layer**, so its facts are cited from the manual and the inventory instead).
This note records what the machine's own documentation settles, and — more importantly — what it
cannot settle.

## Settled

### 1. `DCYC` goes to the Video Shifter, not only to the DMA controller

> §2.9.4 *Memory Controller*: "In addition to the inputs from GSTMCU mentioned above, there are two
> output control signals associated with video. **DCYC strobes data from the display memory into the
> Video Shifter.** CMPCS (color map select) is active only when changing the color attributes in the
> color palettes."

Consequences: the redraw's `DCYC` (`U400.138`) drawn only towards `U300` is **incomplete** — the net
must fan out to the Shifter. It also confirms that `DCYC` — not `SREQ`/`SLOAD` — is the video
handshake, as the ST Magazine article said. Inventory §6.14 is now half-closed: `DCYC` is video,
`SREQ`/`SLOAD` are sound.

### 2. The memory controller owns the addresses of every stream

> §2.5: "The Memory Controller refreshes the dynamic RAMs, loads the Video Shifter with display data,
> and gives or receives data during direct memory access (DMA). **The Memory Controller produces all of
> the addresses for video, sound, and DMA on the multiplexed address bus. These addresses never appear
> on the system address bus.**"

This is the machine's own statement of the two-bus architecture, and the reason the video and sound
counters belong in the Memory chip.

### 3. Two 6850s: one keyboard, one MIDI — no ACIA on RS-232

> §8 *Glossary*, entry **6850**: "Also ACIA … In the STE, there are **two 6850s, one for keyboard
> communication, and one for MIDI communication**."

Confirmed twice now: by the register map (`$FFFC00`/`$FFFC02` keyboard, `$FFFC04`/`$FFFC06` MIDI) and by
the machine's own manual. The parts list's "MIDI / RS-232" labelling is simply wrong: RS-232 is the MFP
USART.

### 4. `U201` is the keyboard ACIA

> §5.3 *Keyboard Problems*, "Keyboard Not Responding": "A command was sent to the keyboard processor and
> no status was returned within the allowed time. Verify that the keyboard is connected… **Verify that
> Pin 3 of U201 is 500 KHz ±50 KHz.**"

Pin 3 of a 6850 is the receive clock. The keyboard ACIA is therefore **U201**, and `U200` is MIDI.
Single-source, but it is a specific pin-level instruction in the machine's own troubleshooting
procedure — the strongest evidence obtainable without a photographic scan.

### 5. The LM556 times the paddles

> §3, *J7 Paddle*: "The inputs are driven by either 5 V/100 Ω or 5 V/1 MΩ on the test fixture. This
> current charges the RC network on the STE, **varying the output pulse of the LM556. The pulse width is
> measured by the GSTMCU chip.**"

So at least one LM556 half belongs to **Controllers** (paddle/mouse timing), not to Disk. The parts
list shows both halves (`U513A/B`, `U514A/B`) sitting among RC networks, so the other half still most
likely serves floppy/ACSI timing — but that remains **probable, not documented**.

### 6. This machine has no real-time clock

The parts list contains **no clock chip and no battery**, and the board's own documentation never
mentions one. The Mega ST, Mega STE and TT carry an RTC (the Mega STE's is a Tadiran TL-5242/W battery);
the **1040STE does not**. TOS returning a default date at boot is therefore *correct behaviour*, not a
missing part.

This **withdraws finding 4 of `design/partition-audit.md`**, which suspected a missing SmartWatch-type
device. The audit is corrected rather than left standing.

### 7. Sound DMA fetches happen during horizontal blanking

> §2.10.1 *Overview*: "**During horizontal blanking (transparent to the processor)** the DMA sound chip
> fetches samples from memory and provides them to a digital-to-analog converter at one of several
> constant rates…"

Documented placement: sample fetches land in the horizontal blanking interval, i.e. outside the 320-clock
window in which the Shifter consumes every internal slot. That is consistent with the slot arithmetic —
sound at 50 kHz needs only 3.2 fetches per line, and blanking offers 48 internal slots.

### 8. There is exactly one priority scheme, and it is for interrupts

> §2.6 *GSTMCU*: "**INTERRUPT PRIORITY** — interrupts from the MFP and video timing are coded into four
> levels of priority on outputs IPL0 and IPL1 to the 68000. These levels correspond to no interrupts,
> MFP interrupts, VSYNC interrupt, HSYNC interrupt."

> §2.5, *Signal and bus arbitration*: "It arbitrates the bus during DMA transfers to prevent CPU and DMA
> devices from interfering with each other."

So the manual names a 4-level priority for interrupts and an arbitration role for DMA bursts, and says
**nothing** about ranking memory clients. That is a third independent corroboration of Cwik's
"round-robin, no priority of any kind" (`research/memory-bus-slots.md`).

## Not settled — and why the drawings cannot settle it

1. **Whether a video group's four words take four consecutive internal slots, or interleave with
   refresh and sound.** This is a scheduling decision *inside* the GSTMCU. No schematic shows it; no
   manual paragraph describes it; neither Hatari nor Steem models it.
2. **Whether refresh preempts video during DE or is deferred into blanking.** Same reason. §2.10.1 leans
   towards blanking for sound and says nothing about refresh.
3. **The exact refresh period for the fitted 256K×1 parts** (7.8 µs derived, not stated).

What would settle them, in order of cost:

- **A hardware experiment**: run a small program that reads the video counter (`$FF8205`–`$FF8209`) at
  every bus cycle across a line while DMA sound plays, and look for discontinuities where the counter
  jumps by more than one group per fetch window. That distinguishes consecutive-slot bursts from
  interleaving, and shows whether refresh ever stops the counter during DE.
- **A GSTMCU reconstruction**: the Verilog reconstructions referenced in the map's chip-logic decision
  would answer it directly, if one exists for the STE's GSTMCU.
- **A die-level analysis**: out of scope at Level 1/2.

## What this changes in the design

- Memory's register list gains nothing, but `DCYC`'s fan-out and the ACIA identities are now
  documented facts rather than inferences.
- The audit loses one finding (the RTC) and converts two (LM556, ACIA identity) from "unresolved" to
  "resolved by the machine's own manual".
- The scheduling questions that remain are **behavioural, not topological**, which means the partition is
  settled and only #7's internals stay open.
