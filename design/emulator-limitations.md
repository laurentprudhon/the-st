# Documented emulator limitations — the questions a board would settle, and what the record already settles

The owner has no 1040STE and no logic analyser. Every question in this repository whose answer would need
either one is recorded **here**, not in the issue tracker: a ticket is a promise of work, and "nobody can
do this yet" is not work. Each entry says what the record already gives, whether **somebody outside has
already measured or published it**, what our model does instead, and what the cheapest honest route is.

Ruling, 2026-11-28 (owner): *"for every question you determine only a measurement on a real board could
settle: first do a deep search to look if someone already did this test, and if it is really undocumented at
the time, record it in a documented emulator limitations document, but don't create a separate ticket."*
This file is that document, and it supersedes ticket
[#103](https://github.com/laurentprudhon/the-st/issues/103), which asked for four such measurements.

## How to read an entry

| state | what it means | what to do |
|---|---|---|
| `documented` | a vendor document, the recovered design or the board's own prose states it | implement it; no measurement needed |
| `measured-elsewhere` | someone published a hardware result, with enough detail to model | model it and cite them; our own board would only confirm |
| `read` | a drawing settles it — a 600 dpi crop and the owner's eyes, not a scope | ask the owner to read it, with the crop in hand |
| `ours` | we can settle it in-house: our own model, a demo as witness, or a known-good implementation | a test in the tree, not a board |
| `open` | nobody has published it and no drawing decides it | it stays open **here**, with the experiment written down |
| `unmeasurable` | needs a decapped die | record it and move on |

`open` and `unmeasurable` are the only rows that can never be closed by work in this repository. Everything
else is a task with a known route, and the row says which.

## 1. The Blitter (U101)

The study is [`research/blitter-implementation.md`](../research/blitter-implementation.md). **No blitter is
implemented in `src/`** — the machine boots without one, and the stub contract is ticket
[#30](https://github.com/laurentprudhon/the-st/issues/30)'s. So for this chip every row below is a thing our
model does not do yet, whatever its state.

| # | question | state | what settles it, and what is already known |
|---|---|---|---|
| B1 | Is the non-HOG window 64 accesses, and why does hardware sometimes show 63? | **`measured-elsewhere`** | Settled, and the answer is "both, and the arithmetic is now public". **Cyprian** measured on a real STE (atari-forum, June 2020): *"the outcome from my tests on the real STe is that split is isn't stiff. CPU part can takes more bus cycles than 64 and the BLiTTER takes 65 on STE and 66 on Mega STE. And in two last cases, only 63 bus cycles are for data the rest are for bus mastering."* **npomarede** (Hatari): leaving arbitration aside it is 64/64, but *"the blitter will sometimes wrongly count 1 cpu bus access for prefetch as if it was 1 blitter bus access"*, so *"in the end the blitter will only do 63 bus accesses itself, processing one word less than expected."* **ijor** (Cwik): *"the Blitter **tries** to count 64 bus cycles used for itself, but because of the bug, sometimes it would include one bus cycle used by the CPU… At least one bus cycle is always wasted by Blitter when initializing its internal machine state."* Cwik's own RTL is the mechanism: a 7-bit `tmoCntr`, `timeOut = tmoCntr[6]`, incremented on every falling edge of `AS` **from the moment `BUSY` is set** — so a CPU access inside the 4-clock latency window is counted as one of the blitter's own. |
| B2 | The 4-clock latency, the 4-clock acquire and the 4-clock release | **`measured-elsewhere`** | Hatari's header records all three as measured on a real STE (and 8 to acquire on a Mega STE). No board needed: the numbers are published and the mechanism (the CPU finishing its write, then arbitration) is coherent with B1. |
| B3 | Which chip is first in the grant chain — U400 or U101? | **`documented`** (three ways, no board needed) | The design's pad ring carries a grant relay: `BGI` in, `BGO` out, `BGACKI` in, `BGACK` out (`ST-4051` sheet 11). Cwik's model names the two input ports for what they are — `iBGACKn  // GLUE output, or optionally additional bus masters` — and drives `BGOn` onward, i.e. **the CPU grants the blitter first and the blitter passes the grant to the GLUE**, taking `BGACK` back from it. Info-Coach says the same from the board's side: *"the Blitter has also these signals and thus does not need the GST MCU to make the request to the 68000"*. The one nuance a scope would still add is the *share* when both want the bus — and the coder's answer to that is already published: *"if a disk transfer is in progress, the DMA has priority over the blitter"* (atari-forum), which Cwik's RTL implements as politeness (`canRequest` waits while `BR` is already asserted and no `BGACK` is outstanding) rather than as a ranking. |
| B4 | The pin numbers and directions | **`documented`, closed 2026-11-28** | Read off the drawing, not measured. The service manual's sheet 1 is a **vector** page whose text layer carries the symbol's own pin numbers and names; aligning them by count (23 addresses ↔ 23 pins, 16 data ↔ 16, 13 control ↔ 13) gave the table, and the **owner confirmed the arbitration block off a 600 dpi crop**: `BGI` 28 in, `BGO` 37 out, `BGACK` 29 out, `BGKI` 40 in, `BERR` 38 in, `RESET` 39, `INT` 41, `FC0–2` 45/44/43, `AS/UDS/LDS/R/W` 23/24/25/26, `DTACK` 27, `BR` 30, `CLK` 34, `VCC` 1/33, `GND` 5/35, and **eight `N/C`** (22, 31, 32, 36, 42, 55, 60, 68) — 60 named + 8 unnamed = 68. The board's two grant nets, `XCPUBG` and `XBLTBG`, are the relay's two segments. `design/board-inventory.md` §3.2 carries the corrected table. He also read pins 1 and 33 as `VCC` — which falsifies the published DOIT_ST table's `VSS` on 33. A vision read of the same crop came back **shifted by one pin**, which is why the owner's eyes were the instrument. |
| B5 | Does the last pixel of a line get written — Atari's appendix table against its own prose | **`ours`** | Nobody has published anything about it; the widest-read references (The Paranoid's *BLiTTER in brief*, ray's page, the STE FAQ) repeat the manual's prose and copy Atari's `lf_endmask`/`rt_endmask` tables verbatim without discussing the edge. The tables are what every working Atari blitter routine uses, so **the arithmetic must be right and the derived rule in the study's §4.7 is what is suspect**. Route: compare our mask arithmetic against a known-good implementation on the same input — NeoST's etalons, or a screenshot of a TOS fill — with the published tables as the oracle. A one-pixel difference is findable in software. |
| B6 | Is `BINT` a pulse or a level, and where does it go? | **`documented`** | Atari: *"The interrupt line is a duplicate of this bit [BUSY]"*. Info-Coach, from the board: *"The Pin 60 \*BINT is the interrupt sent by the Blitter… based on the configuration of the MFP, it seems that this signal is still not taken into account on **I03** pin"*. Hatari's MFP names the source: *"3 : STF/STE : blitter, active low"*, `MFP_INT_GPIP3`. A homebrew blitter board wires its `BIRQ` to *"pin 25 of 68901"* and calls it *"blitter interrupt (INT3)"*. So: level, from U101's `INT` through U400 to `BINT` (U400.60) to MFP GPIP3/I3, and software must take it as an **edge** (68901 `AER` bit 3) because a level cannot interrupt twice. |
| B7 | `xcount = 1` with `NFSR`: the "strange results" | **`measured-elsewhere`**, coverage `open` | Hatari's header still calls it the case no model covered, then notes the September 2020 fix from Cwik's Verilog; the EmuTOS-devel list has the real-hardware observation that fixed it (Christian Zietz could *not* reproduce the wrong icons in Hatari but *could* in Steem SSE 3.9.1, from a program that uses `X COUNT 1`, `Y COUNT 32`, negative increments and `ENDMASK 0xffff-ffff-0000`). Cwik's RTL has the rule: `skipNextSrc = XC2 & (bltState == ST_WDEST) & Nfsr`, `nfsrCycle = bltActive & writeCycle & Nfsr & XC1`, and the source increment picks Y instead of X when `XC1 \| (XC2 & Nfsr)`. What remains open is *coverage*: an exhaustive sweep of the four sign/FXSR/NFSR combinations at `X COUNT = 1` has not been published, and `hatari/tests/blitter/blitemu.c` is the harness to run it in. |
| B8 | What a bus error does to a blit in flight | **`open`** | Cwik's RTL terminates the cycle (`busEnd = !rBerr \| !rDtack \| !busOe`), and nothing else says anything: no vendor sentence, no forum measurement, and the EmuTOS-devel threads show emulators disagreeing about whether the blitter generates bus errors at all. Recorded, not chased: provoking it needs a cartridge or a hardware fault. |
| B9 | The design's `XCS`/`XIDS` pads against the board's symbol — the symbol has neither | **`open`**, and it is now a documented-vs-design conflict rather than a missing measurement | The board's symbol names 60 pins and there is **no `CS` and no `DS`** among them, while the design's pad ring has `XCS` and `XIDS`. So either those pads are tied off on this board (the decode runs from `FC0–2` + address, which is why `FC` is on the symbol at all) or the symbol omits them. A read of pad sheet 11 at 600 dpi shows whether each is an `IPAD`, an `OPAD` or a `TS` driver — and a continuity beep on a board would settle it outright. |
| B10 | Which of the three revisions is the shipped mask | **`unmeasurable`** | `TA-4021` (Oct 1986), `ST-4051` (Mar 1988), `ST-4082` (Sep 1988); the Mega ST (1987) and the STE (1989) bracket them. A decap, which no STE custom has. It matters only for B1's `TMOUT` location, since the block structure is identical. |

### What B1 means for the model, in one sentence

The window counter is **not** a scheduler concept we can invent: it is a 7-bit counter of `AS` falling edges
that starts when `BUSY` is written and times out at 64, and the CPU's accesses inside the start latency count
towards it. That is reproducible **without a board** — it needs our model to count every `AS` edge the bus
sees, including the CPU's, from the instant `BUSY` is set.

## 2. The GSTMCU (U400)

Study: [`research/gstmcu-silicon-implementation.md`](../research/gstmcu-silicon-implementation.md) §8.

| # | question | state | what settles it |
|---|---|---|---|
| G1 | Does refresh happen inside the display window? | **`measured-elsewhere`** | The atari-forum thread *"MMU control of CPU/BLITTER versus Shifter access cycles"* (already the campaign's source [13]) records the finding: refresh is taken on every MMU phase that is not a video access, i.e. **not** during the display fetch — which matches the design memo's "refresh only when `XSRQ` is high" and the design's own decode table. Model it; the scope would only re-confirm. |
| G2 | Is `SINT` a pulse, and how wide? | **`documented`** | The April 1988 design memo `TXL/DWG/SMOD.TXT` states the sound DMA's pin timings, including `SINT` as a 480 ns pulse. It is in the recovered archive, so no board. |
| G3 | Sound-DMA counter ordering, and the frame boundary | **`documented`** | The same memo: the frame boundary is `count == top`, and `FBASE`/`FCOUNT`/`FEND` are U400's. |
| G4 | The frame boundary when `top` is rewritten mid-frame | **`open`** | Nobody has published it, the memo does not cover it and no emulator models it (Hatari writes `top` and lets the counter run). A 20-line test program would settle it on hardware; recorded here until then. |
| G5 | The paddle/pen ADC's law | **`open`** | Hatari returns a host-joystick-derived constant; the STE FAQ documents the *range* (`$FF9211`, 8 bits) and not the law; Info-Coach documents the pins. A resistive sweep on a known divider would settle it. Nothing in the model depends on it (the ADC is stubbed). |
| G6 | Does refresh ever lose to `XSRQ` held low indefinitely? | **`open`**, and it needs a **modified board** | The memo says a held `XSRQ` stops refresh; nobody has published what the machine does when it is held for a frame. Out of reach without cutting a trace; recorded and not chased. |

## 3. The GST Shifter (U401)

Study: [`research/gst-shifter-implementation.md`](../research/gst-shifter-implementation.md) §7.

| # | question | state | what settles it |
|---|---|---|---|
| S1 | The palette's four-bit weights (`1 8 4 2`) | **`documented`** | Atari's own register table and its worked fade sequence; two independent implementations (Hatari's table, the RTL's rotation) reproduce it. Nothing to measure. |
| S2 | Does a byte write to the palette duplicate into both halves? | **`documented`** | Hatari encodes it with worked examples (`move.b #7,$ff8240` → `$0707`) and the cause is the transparent latch; the mechanism is documented, the behaviour is a model task. |
| S3 | Is a palette write visible inside its own line? | **`measured-elsewhere`** | ijor's trace of the transparent latches, plus the entire Spectrum-512-style demo corpus, which is a hardware witness by construction: those effects do not work unless the write lands mid-line. |
| S4 | Does a non-zero pixel offset make U400 fetch one extra group? | **`documented`** | ijor's GLUE state machine (`PRELOAD`) and the design's own `$FF8265` handling: four extra groups in low res, one in high. Model it. |
| S5 | The ladder's per-bit resistor values | **`open`**, and it is an **analogue** question | A meter on `U405`'s outputs. Four weighted resistors per channel are established; their values are not, and the model stops at the digital pin, so nothing depends on them. |
| S6 | The Microwire frame's length and rate | **`documented`** | The board article and Atari's example: 1 Mbit/s, a mask that decides which bits clock out, 11 clocks for `$07FF`. A model task. |
| S7 | The sound DMA FIFO's depth (carried as four words from Steem) | **`open`** — the row that ticket [#26](https://github.com/laurentprudhon/the-st/issues/26) has been carrying | `SREQ` is the chip's own output, so software cannot starve the buffer to count its depth: it needs a logic analyser on U401 pin 36 under a frame written to overrun, or a franker reading of the reconstruction's own comment (which argues only that two words is too few). Nothing else in the machine depends on the figure — it changes when a sound word is dropped, not whether the path works. |
| S8 | What the ACIA does when the IKBD holds the line busy (ticket [#21](https://github.com/laurentprudhon/the-st/issues/21)) | **`documented`**, closed 2026-11-28 | The 6850's data sheet states all three cases: a one-byte receive register with an overrun flag ("neither the receive buffer nor the `RSR` is overwritten"), `CTS` high inhibiting `TDRE` ("the Transmit Data Register Empty bit is inhibited"), and `DCD` high inhibiting the receiver — so "busy" costs *bytes*, not time, and costs the transmitter only its status bit. The ÷64 clocking to 7812.5 baud is arithmetic on the 500 kHz reference (`design/timers-and-events.md`; `research/standard-parts-implementation.md` §5.2–§5.3). |

## 4. The DMA controller (U300)

Study: [`research/dma-controller-implementation.md`](../research/dma-controller-implementation.md) §7. This is
the subsystem where the search paid off most: two of its seven tests are already answered by a vendor
document that the study did not use.

| # | question | state | what settles it |
|---|---|---|---|
| D1 | The select's width, and the `$FF8600` alias | **`documented`** | Info-Coach's *DMA / WD1772 Programming* document: `$FF8606` bit 3 selects `HDCS*`/`FDCS*`, and the mode register is the only thing that arms a transfer. The alias behaviour follows from the decode arithmetic. |
| D2 | Status bit 0's polarity | **`documented`** — the study's open conflict is closed | Same document: *"Bit 0: DMA error status: 1 = no error; 0 = error"*, and *"Bit 1: Sector count status: 0 if count has reached zero"*. So the Atari-side reading is `1 = no error`, and the "5 sources against 2" conflict in the study's §5.3 is decided by the vendor's own prose. Correct that section rather than measuring it. |
| D3 | The device window's high byte | **`documented`** | Same document's register table plus the 8-bit device port's wiring: the high half floats to ones. |
| D4 | `RDY`'s shape and rate | **`measured-elsewhere`** | The study's own burst arithmetic (8 words, 32 clocks, 32 bursts a sector) is derived from the DMA document's timing and matches the emulators' behaviour; a scope would show the same envelope. |
| D5 | The counter's readback cadence | **`open`** | Nobody has published whether a read of `$FF8609/0B/0D` can disturb a transfer in flight; the study calls it a documented behaviour with no model. Recorded. |
| D6 | The reset state of a chip with no reset pin | **`open`** | Power-on state after a warm boot. A test program and a cold/warm boot; no other route. |
| D7 | The double strobe | **`documented`** — a model task, tracked in [#95](https://github.com/laurentprudhon/the-st/issues/95) | Atari's own DMA documentation and the WD1772's command timing; no emulator models it. |

## 5. Machine-level questions

| # | question | state | what settles it |
|---|---|---|---|
| M1 | Which nets are open-collector / multi-driver ([#23](https://github.com/laurentprudhon/the-st/issues/23)) | **`read`** | A schematic read, not a measurement — and the blitter's contribution is already known: `BR` and `BGACK` are **wired-OR** (open-drain on the 68000 side and on this chip's pads), `BG` is point-to-point, and the blitter's `BGO`→`BGACKI` is a second segment of the same chain. |
| M2 | The real pin-event rate ([#18](https://github.com/laurentprudhon/the-st/issues/18)) | **`ours`** | An *emulator* measurement: instrument a reference emulator. Nothing to do with a board. |
| M3 | The peripherals' `/RESET` receivers ([#101](https://github.com/laurentprudhon/the-st/issues/101)) | **`documented`** in part | The CPU's own hold is captured in the corpus (124 clocks); which chips subscribe is a schematic question (`read`) plus the design's pad rings — the blitter has `XRESET` and one `INT`, and the GSTMCU takes `/RESET` to re-initialise its counters (Info-Coach). |
| M4 | The DMA-chip relay versus the blitter's direct request | **`documented`** | Both studies plus Info-Coach: the DMA asserts `RDY` and the GSTMCU performs the cycles; the blitter drives addresses and takes the grant. This row exists because it is the one contrast every summary of the machine gets wrong. |

## 6. The four standard parts (U203, U301, U200/U201, U202)

Study: [`research/standard-parts-implementation.md`](../research/standard-parts-implementation.md). These
parts are the campaign's opposite case — complete datasheets — so almost nothing here is `open`: a
datasheet answer is a *task*, and a wiring answer is a *reading*. What is left for a board is only the
pin-level reading of two nets and one strap.

| # | question | state | what settles it, and what is already known |
|---|---|---|---|
| P1 | Which of the YM2149's pins 24/25 is `A8` and which `A9` | **closed 2026-11-28** — the board wins and the datasheet is the suspect | The owner read the straps: `24 → VCC`, `25 → VSS`, i.e. `A8` 24 / `A9` 25. The datasheet's own programme wants `A8` = 1, `A9` = 0, and a mismatch drives `DA7–DA0` to high impedance — which would kill both the sound and the floppy select on this machine. So the redraw is right and Yamaha's pin figure (copied by Hatari) prints the pair transposed; a continuity check on a bare part would falsify it (`research/standard-parts-implementation.md` §9.4). |
| P2 | What clocks the WD1772 (`CLK`, pin 18) | **closed 2026-11-28** | `SCLK` — the 8.010613 MHz oscillator, the same clock the Shifter's sound divider and the CPU run on. The owner traced it; the part is 0.133% over the datasheet's 8 MHz ±0.1%, which MFM absorbs because the data separator locks to the data (bit rate becomes 250.3 kbit/s), `research/standard-parts-implementation.md` §4.2. |
| P3 | Which nets reach the MFP's `TAI` (19) and `TBI` (20) | **closed 2026-11-28** | `TAI` is wired to **`U900` pin 13 (`Q7`)** — the shift register's last stage, so the delay is in the path — and `TBI` carries a net labelled **`DE`**. The owner traced both, which is also the board-level confirmation of the 74LS164 story (§7 of the study). |
| P4 | The `XSINT`→`TAI` delay's exact value on *this* build | **`derived`**, one measurement away | 8 stages at `CLK2`'s 2 MHz is 4 µs = 32 CPU clocks, and the STE article and Hatari's comment agree. A frequency counter on `TAI` against a DMA-sound frame end would show it; until then Timer A's pending instant is 128 master ticks late, one constant (`design/timers-and-events.md` §10.1). |

### 6.1 The floppy drive and the medium (2026-11-28, `research/floppy-media-and-images.md` §3, §8)

The drive and the diskette are the disk path's **analogue** half. None of these is in an image, and no imaging
tool measures it per disk; the model fixes the nominal drive and records the rest here.

| # | question | state | what the record gives, and what the model does instead |
|---|---|---|---|
| A1 | Head alignment and azimuth on a worn drive | **`unmeasurable`** | A per-drive mechanical error, not a property of the media. The image carries the track *number*; the model has no head geometry to misalign, and adding one would be a different project. |
| A2 | Write current and the medium's coercivity | **`unmeasurable`** | Set by the mastering drive and the media batch; the copy-protection record uses it as the reason a weak bit cannot be copied, not as a number the model can consume.[51][52] |
| A3 | Media wear, dropout, and the marginal bits age produces | **`unmeasurable`** | An image can record the **instability observed at imaging time** (an STX fuzzy mask, an IPF `FZD`, SCP's raw flux), never the cause. The model reproduces the observed mask; it does not age the disk. |
| A4 | The reference drive's speed error, wow and flutter | **`open`** | The model's spindle is exactly 300 RPM and its index exactly 5 Hz. A tachometer on a real drive would bound the tolerance; the target is the nominal machine, so the row is a bound, not a task. |
| A5 | The preamp/AGC transfer curve and the DPLL's capture range | **`open`** | The WD1772's inspection window is 2 µs nominal; the part's *analogue* capture range and the drive's slicer are not in the datasheet. A bit-error-rate sweep against cell width would settle them; the model stops at the digital cell. |
| A6 | Write splice and erase behaviour when a sector is rewritten | **`open`** | IPF's own document says the analyzer cannot archive modified disks because a sector rewrite leaves undefined bytes in the gaps and a slight drift of the sector against the track (the write splice).[48] The model has no disk-write path yet. |

## 7. The IKBD (HD6301V1)

Study: [`research/ikbd-implementation.md`](../research/ikbd-implementation.md). The part is a 6801-family MCU
running a mask ROM, so the study is a protocol study and what is left for a board is small: one cadence, one
floating line, and the rule behind a firmware quirk whose four known cases are already published. The two
buffer depths that used to be T3 numbers are now settled by the firmware itself and are recorded here as a
correction rather than as a question.

| # | question | state | what settles it, and what is already known |
|---|---|---|---|
| K1 | The exact matrix scan period — one pass over the IKBD's fifteen drive lines | **`open`** | The firmware scans one column per pass of its main loop, so the period is its instruction count; the column block is about 44 machine cycles at E=1 MHz and a full pass is fifteen of them, which bounds a pass at roughly 0.7–5 ms (hundreds of hertz) and does not fix it. A scope on `DR1` during a key press, or a cycle-exact count of every loop path, would. |
| K2 | The output ring's depth, and what is dropped | **settled 2026-11-28** — the row that used to say "8 in, 20 out, T3" | The ROM settles both: the command buffer is `$CD`–`$D4` with its counter `$CC` refusing a byte at 7 (**7 usable**), and the event/reply ring is `$D9`–`$ED` with its index wrapping at 21 (`cmpa #$15`) and a store test of `needed >= free` (**21 cells, 20 usable**) that drops the **whole report** it cannot fit. Hatari's "8 in, 20 out" is right in effect and wrong in size. |
| K3 | What an unplugged keyboard's floating `RXD` reads as | **`open`** | `$FF` (a break condition) and noise are both plausible, and nothing published says which; whether the ST's ACIA reports `OVRN`, `FE` or silence is the same question one level up. Pull the cable mid-session on a real machine. |
| K4 | The reset window: its length, and whether the four known command pairs are the whole rule | **`measured-elsewhere`** for the pairs, **`open`** for the rule | Hatari records `$08 $14`, `$08 $0B $14`, `$12 $14` and `$12 $1A` arriving during the self-test as the trigger for reporting mouse and joystick together, with *Barbarian* and *Hammerfist* named; nothing else in the record generalises it. A real machine with a timer between `$80 $01` and the second command would map the window. |

## 8. What the 2026-11-28 search changed

1. **The 63/64 conflict is closed** — three people (a demo coder with a real STE, Hatari's author, and the
   blitter's own reverse-engineer) published the answer, and the RTL shows the counter. Our study's §5.1 and
   `research/gstmcu-silicon-implementation.md` §6.6 can now say what the mechanism is, not just which number
   is documented.
2. **The grant chain's order is answered** by the pad ring plus Cwik's port comment: the CPU grants the
   blitter, the blitter passes the grant on to the GLUE, and the DMA yields only by politeness on a busy
   `BR` — not by ranking.
3. **The interrupt path is corroborated three times** (Atari's "duplicate of BUSY", Info-Coach's `I03` pin,
   Hatari's `GPIP3`), and a homebrew board wires it to the same pin.
4. **Two DMA questions are answered by a vendor document the study already cited but had not quoted** — a
   reminder that a source in a source list is not a source read (D1, D2, including the
   status bit's polarity, which the study had as a 5-vs-2 conflict).
5. **The pin numbers came from the drawing, not from the published pinout** — the service manual's sheet-1
   symbol is a vector page with a text layer, so aligning its pin blocks by count gave the whole table; the
   owner confirmed the arbitration block on a crop, and a vision read of the same crop shifted it by one pin.
   The published DOIT_ST table turned out to be right about the things that matter (`AS` 23, `BG` twice, `N/C`
   eight times) and wrong about one pin (33 as `VSS`).
6. **Nothing has been published about the endmask's last pixel** — which, given that every working blitter
   routine uses Atari's own tables, is evidence that the tables are right and the derived rule was wrong.

## 9. What is genuinely open, and nothing else

Fourteen rows. Two are on the blitter itself (B7's *coverage* is the third, counted in §1), two on the
disk path's digital side, three on the medium (A4–A6), three on the GSTMCU (two of them on its stub), one
is analogue, one is the Shifter's sound FIFO, and two are on the IKBD — its scan cadence and a floating
`RXD`. Nothing in the list can be closed by work in this repository, and every one of them has the
experiment written down.

| row | question | the one thing that would close it |
|---|---|---|
| B8 | what a bus error does mid-blit | a cartridge-driven fault on hardware, or a vendor sentence nobody has found |
| B9 | the `XIDS` pad | a 600 dpi read of the pad sheet (a `read`, not a scope) |
| D5 | the counter's readback cadence | a poll-loop test against `RDY` on a scope |
| D6 | the disk chip's power-on state (it has no reset pin) | a cold/warm boot pair and a register dump |
| G4 | a mid-frame `top` write | a 20-line test program on a board |
| G5 | the ADC's law | a resistive sweep |
| G6 | `XSRQ` held low | a modified board |
| S5 | the ladder's resistor values | a meter (analogue; nothing depends on it) |
| S7 | the sound FIFO's depth | a logic analyser on `SREQ` under an overrunning frame |
| A4 | the reference drive's speed error and wow | a tachometer on a real drive |
| A5 | the DPLL's analogue capture range | a bit-error-rate sweep against cell width |
| A6 | the write splice of a rewritten sector | a write/rewrite test on hardware, then a flux capture |
| K1 | the IKBD's matrix scan period | a scope on `DR1`, or a cycle count of the firmware's loop |
| K3 | a floating `RXD` on an unplugged keyboard | pull the cable mid-session |

Everything else in this document is a task with a route: implement it (`documented`), model it and cite the
person who measured it (`measured-elsewhere`), read a crop (`read`), or test it in our own tree (`ours`).

## Sources

- Atari — *User Manual for the Atari ST Bit-Block Transfer Processor (BLiTTER)*, 25 Jan 1990:
  <https://web.archive.org/web/2020id_/http://dev-docs.atariforge.org/files/BLiTTER_1-25-1990.pdf>
- atari-forum — *STE Blitter* (Cyprian's hardware measurement, npomarede's and ijor's answers):
  <https://www.atari-forum.com/viewtopic.php?t=38994>
- Jorge Cwik — `stBlitter.sv` (the RTL the MiSTer core copies): <https://github.com/ijor/stBlitter>
  and <https://github.com/MiSTer-devel/AtariST_MiSTer/blob/master/rtl/stBlitter.sv>
- Hatari — `src/blitter.c` (the measured 4/4/4 and the 63 note) and `src/mfp.c` (`GPIP3`):
  <https://github.com/hatari/hatari>
- Info-Coach — *Atari STE Hardware* (the GSTMCU's pins, `BINT`, `I03`): <http://info-coach.fr/atari/hardware/STE-HW.php>
- Info-Coach — *DMA / WD1772 Programming* (status bit 0, `FDCS`/`HDCS`):
  <http://info-coach.fr/atari/documents/_mydoc/FD-HD_Programming.pdf>
- DOIT_ST archive — *8.1.4 Blitter*, `Tabelle 54: 68 pin PLCC Blitter Chip Pinout`:
  <https://mikrosk.github.io/doitarchive/doit_st/080104.htm>
- Atari — *STE Schematic Rev A, C300780-001, Oct 3 1989*, 6 sheets:
  <https://archive.org/details/Atari_STe_Schematic_Rev_A_C300780-001_Oct_3_1989>
- agranlund — `STBlitter_RevA` (the `BIRQ`/68901 wiring and the `1K` pull-up):
  <https://github.com/agranlund/STBlitter_RevA>
- EmuTOS-devel — the blitter threads (`xcount = 1` with `NFSR`, and whether the blitter produces bus errors):
  <https://sourceforge.net/p/emutos/mailman/message/35829134/>,
  <https://sourceforge.net/p/emutos/mailman/message/35883014/>
- The Paranoid / Paradox — *The Atari ST(E) BLiTTER in brief*: <http://beyondbrown.d-bug.me/post/blitter-faq/>
- Motorola — *MC68901 Multi-Function Peripheral*, advance information ADI-984, January 1984:
  <https://www.bitsavers.org/components/motorola/68000/MC68901_Multi-Function_Peripheral_Jan84.pdf>
- Western Digital — *WD1772 Floppy Disk Controller Specification* V1.1 (J.-L. Guérin's edition):
  <https://hxc2001.com/download/datasheet/floppy/thirdparty/FDC/WesternDigital/WD1772-JLG-V11.pdf>
- Motorola — *MC6850/MC68A50/MC68B50* data sheet:
  <http://www.sprow.co.uk/bbc/hardware/extraserial/6850datasheet.pdf>
- Yamaha — *YM2149 Software-Controlled Sound Generator* (LSI-2121492, September 1992):
  <https://www.bitsavers.org/components/yamaha/YM2149_199209.pdf>
- Atari — *STe 520/1040 Computer Field Service Manual*, C302481-001 Rev A, August 1991 (2017 redraw):
  <https://jsobola.atari8.info/DEREATARI/atarisch/1040ste_fsm.pdf>
- Hatari — `src/psg.c` (the YM2149's 2 MHz clock and port A), `src/mfp.c` (the GPIP channel map),
  `src/dmaSnd.c` (the 74LS164 delay): <https://github.com/hatari/hatari>
- Atari — *Intelligent Keyboard (IKBD) Protocol* (the command set, the packet formats and the default modes),
  reproduced in the Linux kernel's input documentation: <https://docs.kernel.org/input/devices/atarikbd.html>
- Hatari — `src/ikbd.c` (the ROM analysis of the IKBD's buffers, the command table, the measured reset and
  reply delays, the reset-window quirk): <https://github.com/hatari/hatari/blob/master/src/ikbd.c>
- EmuTOS — `bios/ikbd.c`, `bios/aciavecs.S`, `bios/mouse.c` (the ACIA set-up, the packet parser and the
  mouse-mode commands): <https://github.com/emutos/emutos>
- Steem SSE — the commented HD6301 ROM disassembly and RAM map of the Atari IKBD, vendored in this tree under
  `steemsse-V4.2.0_R10/steem/doc/SSE/`: <https://sourceforge.net/projects/steemsse/>
- Hitachi — *8-bit single-chip microcomputer data book* (HD6301V1: the SCI's `E/128` table, the E-clocked
  free-running counter): <https://bitsavers.trailing-edge.com/components/hitachi/_dataBooks/1988_U71_8-bit_Single_Chip_Microcomputer_Data_Book.pdf>
