# Handoff — U203, U301, U200/U201, U202: the four standard parts

**Executed 2026-11-28** — the study is `research/standard-parts-implementation.md`. Four of the bullets
below were corrected by what the session found; each correction is dated in place.

**Goal of the next session.** Write `research/standard-parts-implementation.md`: the MFP, the floppy controller,
the two ACIAs and the PSG studied to the standard the four customs reached — **but with the method reversed**,
because these parts are the opposite case. The customs had a thin record and a recovered design to mine; these
have *complete datasheets* and a board. So the work is not "what is this part" but three narrower things:
what the **board's wiring** adds, what the **datasheet leaves open**, and what our **model already assumes**
that neither of them says.

Read first, in this order: `docs/handoffs/silicon-research-per-chip.md` §4's last entry and §6 items 5–6 (why
this is the next session, and what closes the campaign); `research/blitter-implementation.md` §2 and §9 (the
shape and the label discipline, and the licence position this study inherits); `design/board-inventory.md`
§3.8 (MFP), §3.9 (YM2149), §3.10 (6850), §6.16 (the 74LS164 story, doubled), §4.3–§4.4 (the nets and the
crystals); `design/timers-and-events.md` (the MFP as the model builds it, and its §10 open points);
`design/disk-path.md` §3 and §7 (the byte clock, and what that file deliberately does not decide);
`design/subsystems-spec.md` §1's Timers / Sound / Controllers / Disk rows **and the correction block under it**
(`GPIP`'s sources on this board); `design/emulator-limitations.md` §§2–4 (§4's D5/D6, §3's S7/S8 and §2's
G4–G6 are this session's rows); and `design/cpu-silicon-todo.md` (the *shape* of the model-comparison
deliverable — it is the one artefact that makes a study actionable).

---

## 1. What is already established, so the session does not re-derive it

* **The campaign's method inverts here.** For a standard part the datasheet *is* the primary source, and it
  outranks every emulator. The board outranks the datasheet only where the board is wired differently: which
  crystal, which chip select, which source a pin carries. That inversion is the whole reason this is one
  short session rather than four long ones.
* **The interrupt chain is settled and must not be re-litigated.** The MFP raises `IRQ` (pin 32) into the
  GSTMCU, which combines it with VBL (level 4) and HBL (level 2) and drives `IPL0–IPL2` itself; the MFP has no
  `IPL` pins and the MFP is level 6 (`design/subsystems-spec.md` §1, corrected 2026-11-05; `board-inventory`
  §4.3).
* **`GPIP`'s sources on this board are settled** (`design/subsystems-spec.md`'s correction block): `FDINT` and
  `DINT` share source 7, `BINT` is source 3, `SINT` is source 15 — the line the STE shares with mono detect and
  which is *also* Timer A's event input — the keyboard ACIA is source 6, and the two RS-232 status lines are
  sources 1 and 2. **`BINT` = the blitter's busy/idle line**, established 2026-11-28
  (`research/blitter-implementation.md` §4.5, `design/emulator-limitations.md` B6): a **level**, so software
  needs the 68901's `AER` edge select to get one interrupt per blit. That is the single most useful new fact
  this session inherits, and the model does not implement it yet.
* **A vendor document already answers two disk questions** and is cited as `[23]` in the DMA study: the status
  word's bit 0 is "1 = no error", and `$FF8606` bit 3 selects `FDCS*`/`HDCS*`. The double strobe is documented
  by Atari and modelled by nobody — a model task, not a mystery (`design/emulator-limitations.md` D7).
* **The disk path's register split is settled**: `$FF8604`/`$FF8606` are U300's, `$FF8609/0B/0D` are the
  GSTMCU's 24-bit counter, and every memory cycle of a burst is the GSTMCU's (`research/
  dma-controller-implementation.md` §4, and `design/disk-path.md` §4).
* **Three of the four parts have unresolved pin rows in our own inventory** *(2026-11-28: **none does now** —
  all four pin families are complete in `design/board-inventory.md` §§3.7–3.10)* — and that is a *reading* task with
  a known technique, not a research question: `board-inventory` §3.8 says the MFP's `IOA`/`IOB` rows could not
  be reconciled with the main table (pins 21 and 46 are illegible), §3.9 has only two YM2149 ports resolved,
  and §3.10's 6850 numbers were "not resolved … the pairing produced duplicates". The technique that settled
  U101 works here: the service manual's schematic pages are **vector**, so `pymupdf`'s text layer prints each
  symbol's pin numbers and names in two parallel blocks — align them **by count**, then have the owner read one
  crop to confirm the block boundaries (`research/blitter-implementation.md` §3.4).
* **Declared out of scope, by ruling:** the USART and RS-232 (a stub), the analog tail (the MC1377, the DACs,
  the LMC1992's analog behaviour), and the IKBD firmware — which is the *next and last* session.
* **The 74LS164's story is doubled in our records** (`board-inventory` §6.16): the 1991 parts list has one at
  `U900` for keyboard/MIDI serialisation, and the STE hardware article describes one added to delay `*SINT`.
  One part serving both, or two parts and one missing from the list. The model takes the delayed copy; a sheet
  read decides it. This session settles it, because it is the only one of these four parts whose *existence*
  is in doubt.

## 2. Acceptance criteria

A reader who has the study must be able to do these without opening another document:

1. **Follow one timer expiry end to end**: the period written to the data register, the prescaler, the count,
   `TAI`/`TBI` and `DE`, the pending bit, the encoder's decision, the vector, `IPL6` — naming which clock each
   step runs on (~one crystal for the MFP) and where the 500 kHz reference enters (if it enters at all, §6.3).
2. **State the `GPIP`/`AER`/`DDR` triplet bit by bit**, including the read-modify-write behaviour of a byte
   write to a port (datasheet vs hardware), which of the sixteen sources is edge- or level-triggered, and the
   exact configuration that turns `BINT`'s level into one interrupt per blit.
3. **Say what the encoder's rule is and what it is not**: sixteen sources, highest pending/enabled/unmasked/
   not-in-service wins, no priority table, and what `IPR`/`ISR`/`IER` do to that rule — with the auto-vector
   corner and the `IACK` chain (`U400.83 · U203.45 · U200/U201`).
4. **Draw one disk sector as a timetable**: command → command latency → the 32-byte prefill → `RDY` bursts of
   eight words → `INTRQ`, with the data rate, the step rate, `DDEN`, `HLD`/`HLT`, and the force-interrupt
   corner named — and the double strobe shown where it happens. *(2026-11-28: three corrections — the prefill
   is the DMA chip's **sixteen**-byte FIFO, so a sector is 32 bursts (`research/dma-controller-implementation.md`
   §4.2); the WD1772 has **no `HLD`/`HLT` pins at all**, only the motor-on wait of 6 revolutions; and the
   double strobe is a documented Atari caution whose own guide says "in practice not following this
   recommendation seems to work fine".)*
5. **State the two serial links' arithmetic**: 500 kHz ÷ 64 = 7812.5 baud (keyboard) and ÷ 16 = 31 250
   (MIDI), the framing, `TDRE`/`RDRF`/overrun, and what the ACIA does when the other end is busy.
6. **State the PSG's arithmetic and its clock**: master clock, tone period → frequency, the noise LFSR, the
   envelope's periods and shapes, and the two ports as a keyboard/joystick scanner — **and what the board's
   nets say the clock is**, because our own records disagree (§5).
7. **Give the 74LS164's role and count**: one part or two, which path it serves, and what its delay does to
   Timer A's pending instant.
8. **Compare the record with the model, claim by claim** — the `design/cpu-silicon-todo.md` shape: for each
   section, one row per claim, "aligned" or "does X instead", with the four model files named
   (`design/timers-and-events.md`, `design/disk-path.md`, `design/virtual-chips.md` §§4–8, `src/TheST.Machine/Chips/*`).

## 3. The evidence, and what each layer can answer

| layer | what it gives | what it cannot |
|---|---|---|
| the four datasheets (68901, WD1772, 6850, YM2149 — bitsavers; the WD1772's and the FD/HD programming guide are already in the ledger as `[28]` and `[23]`) | the register maps, the timing diagrams, the pin descriptions, the errata | anything about *this* board: which crystal, which source a pin carries, how a chip select is decoded |
| the service manual (schematics + error-code section) | the nets, the crystals, the symbols' pin lists, and the field-diagnostic behaviour of each part | timing, and the pin rows our transcription already got wrong once |
| Atari's own prose (ST Internals `[15]`, the FD/HD guide `[23]`) | the board-level semantics, the register addresses as software knows them | silicon truth |
| Hatari / NeoST / Steem SSE (`mfp.c`, `fdc.c`, `MFP`, `psg`) | a checklist of quirks nobody sourced: prescaler edges, `GPIP` RMW, force interrupt, the spurious-interrupt reports | evidence — T3 by definition |
| our own model — `TimersAndEvents` (real), `Disk` (real), `Sound`/`Controllers` (stubs) | every place the study can land as a task rather than as prose | nothing about the parts |
| the board | the only instrument, and the owner has none — so the register in `design/emulator-limitations.md` governs | — |

## 4. Method rules this campaign earned — inherited, plus two new ones

Inherited (each cost a session once): **sheets first, RTL second, emulators never as evidence**; every claim
labelled `documented` / `derived` / `inferred` / `unknown` with `T1`/`T2`/`T3`; **names lie**; **check
arithmetic against the part, not the sheet**; **the vendor's prose beats every model**; keep sheet numbers out
of citation syntax.

New, from the last two sessions:

* **Search before writing `unknown`.** The STE's community measures and publishes; the blitter's 63-access
  window turned out to be measured on hardware, explained by Hatari's author and confirmed by the RTL's author
  in one forum thread (2026-11-28, `design/emulator-limitations.md` §6). A question that only a board can
  settle is *not* a ticket: it goes into `design/emulator-limitations.md` with its one measurement.
* **A source in a list is not a source read.** Two DMA conflicts dissolved when a document already cited was
  actually quoted (`research/dma-controller-implementation.md` §5.3). Read the datasheet's *register tables*
  and *notes* columns, not just its timing diagrams.
* **Our own records can be the stale source.** Three of these parts' pin rows are `unverified`, and the
  `atari-st-hardware/` pages are not sources at all (the blitter page still describes another part). Where the
  study contradicts the repo, land a **dated** correction in place — as `design/board-inventory.md` §3.2 just
  was for U101 — rather than a note in the study only.

## 5. Arithmetic seeds to check, not to copy

* **The MFP's clock is the 2.4576 MHz crystal** (`Y200`, X1/X2 on pins 17/18) — a third crystal, not `CLK4`
  (`design/timers-and-events.md` §3). 2 457 600 ÷ 16 = 153 600; a 20 ms frame is 49 152 MFP clocks; the
  prescale ladder is 1/4/10/16/50/64/100/200, so a Timer A period of 625 at ÷200 is one PAL line.
  *(2026-11-28: **this seed fails its own arithmetic** — 625 × 200 ÷ 2 457 600 = 50.9 ms, not 64 µs; no
  (prescaler, data) pair gives a line exactly, because one line is 157.29 crystal clocks. That is why
  Timer B counts `DE` edges instead of timing lines, and why the only exact relationship in the ladder is
  the 200 Hz tick: 12 288 clocks, or ÷64 with data 192.)*
* **The PSG's clock is 2 MHz in our records and 2.4576 MHz in our inventory** *(2026-11-28: **settled for
  2 MHz** — the board's net on pin 22 is the GSTMCU's `CLK2`, Atari's prose says "the clock input is 2 MHz",
  the diagnostic says "the 2 MHz clock", and the datasheet's maximum with `SEL` high is 2 MHz. The
  inventory's `Y200` row is the stale record; the question below is closed.)* — `design/virtual-chips.md`'s
  chip table and `ADR 0001` say 2 MHz; `board-inventory`'s crystal row (`Y200`, 2.4576 MHz) is tagged
  "MFP / YM2149 clock". **This is the
  first thing to settle**, from the board's net at `U202`'s `CLK` pin: at 2 MHz a tone period of 254 is 492 Hz
  and A4 is 284; at 2.4576 MHz the same period is 604 Hz and every frequency table in every model is shifted
  by 1.23. Do not do the tone arithmetic before the clock is decided.
* **The serial links**: 500 kHz ÷ 64 = 7812.5 (keyboard), ÷ 16 = 31 250 (MIDI) — both from the GSTMCU's 500 kHz
  tap (`KHZ500`), which is the same reference the floppy's `STEP`/`WD` rates are derived from. If one link's
  divisor turns out to be ÷16 and the other ÷64, say which is which rather than "the ACIA's ÷64".
* **The floppy, at 8 MHz**: MFM at 250 kbit/s is one bit per 32 clocks; a sector is 512 bytes plus its envelope;
  the DMA's burst is 32 clocks (eight words) and ~32 bursts fill a sector — the numbers the DMA study derived
  and `design/disk-path.md` §3 reuses. Any derived figure that needs a millisecond the rotation cannot hold is
  wrong, not the drive.
* **The 74LS164**: eight stages at 500 ns = 4 µs (`design/timers-and-events.md` §10.1 says it is `derived`,
  not measured). If that is wrong, only Timer A's pending instant moves — one constant, and worth saying so.
  *(2026-11-28: confirmed — Hatari's `dmaSnd.c` states "8 shifts using a 74LS164 running at 2 MHz ...
  equivalent to 32 CPU cycles", and the part is on the `SINT`→`TAI` path, one part not two.)*

## 6. What must stay `unknown` until measured

Each of these is already a row in `design/emulator-limitations.md`; the session **updates that file** rather
than opening a ticket:

* **The disk chip's power-on state** (D6) — it has no reset pin, and a warm boot is the only witness.
* **The counter's readback cadence** (D5) — whether a read of `$FF8609/0B/0D` can disturb a transfer in flight.
* **The sound FIFO's depth** (S7) — the figure we carry (four words) is Steem's.
* **A mid-frame `top` write** (G4), the **paddle law** (G5), and **`XSRQ` held low** (G6) — the GSTMCU's, but
  they share this session's register and should not be re-litigated here.
* **Whether the 6850's pin rows can be read at all** — the manual's symbol is drawn with the pin-name text at
  zero offset, and the pairing produced duplicates. That is a text-layer problem, not a hardware one: try the
  other sheets of the schematic before declaring it unreadable. *(2026-11-28: **readable**, and the fix is a
  per-edge offset — 1.8 pt along the pin axis, 8.3 pt across it. All 24 pins pair, for both ACIAs.)*

## 7. The experiments that would settle the rest

Short by design, because most of this session's questions are datasheet questions. Each is a TOS program on
the real 1040STE, and each lands in the limitations register with its result, not as a ticket.

1. **The MFP's prescale edge.** Scope `TAO` (pin 13) in output mode with a known period at ÷1 and ÷200; count
   clocks between edges. *Pass:* the datasheet's `period × prescale`, with the expiry at the count's roll-over
   and not one clock early.
2. **The `GPIP` read-modify-write.** Write a byte to `$FFFA01` with `DDR` mixed, read it back, then write a
   single bit. *Pass:* the datasheet's "write the whole port" rule, or the hardware's bit-level behaviour.
3. **`BINT` as an interrupt.** Enable `AER` bit 3 for the falling edge and `IMRA` bit 3, then blit. *Pass:*
   exactly one `IRQ` at completion and none at the start (§4.5 of the blitter study says why this is the test).
4. **The force-interrupt corner.** Issue a `WD1772` command, then `D0` (force interrupt) mid-sector; read the
   status and the `INTRQ` line. *Pass:* the abort's status bits match the datasheet's table.
5. **The serial divisor.** Loop `TXD` back to `RXD` and measure the bit cell at both ACIA clock taps.
   *Pass:* 128 µs (7812.5) or 32 µs (31 250), and the 6850's `÷16`-of-`÷1/÷16/÷64` field matches the wire.
6. **The PSG's clock.** Play a tone at a known period and measure the output frequency. *Pass:* 2 MHz or
   2.4576 MHz — this is the seed check of §5, and a frequency counter settles it in one note.

## 8. Definition of done

1. `research/standard-parts-implementation.md` — **one section per part**, each with its own §"what the board
   says", §"what the datasheet leaves open", §"what the model does", and a shared closing §"what the model
   needs"; `verify --min-coverage 0.30` green.
2. The model-comparison table (§2.8), in `design/cpu-silicon-todo.md`'s shape, with every difference either
   landed as a correction in `design/` or named as a task with its home.
3. `design/emulator-limitations.md` updated: the rows this session owns, and **no new tickets** for anything
   measurement-only (owner's ruling, 2026-11-28).
4. Corrections in place, dated, wherever the datasheets move our records: `design/board-inventory.md` §§3.8–3.10
   (three unresolved pin families), `board-inventory` §6.16 (the 74LS164), `design/timers-and-events.md` §10 and
   `design/disk-path.md` §7 as their open points close, and `design/virtual-chips.md`/`ADR 0001` if the PSG's
   clock is not 2 MHz.
5. No sheet set unless earned. A standard part's drawing is a datasheet page; the one figure worth considering
   is **the interrupt chain** (sixteen sources → one encoder → `IRQ` → GSTMCU → `IPL` → the CPU), and only if
   the prose cannot carry it.
6. `docs/handoffs/silicon-research-per-chip.md` §4's entry for these parts marked **done** with a one-line
   pointer, and §6's ordering updated. Commits pushed to `origin main`.

---

## 9. What closes the campaign after this

Two things, in this order, and neither is a full session:

* **The IKBD (HD6301V1)** — the last entry in the per-chip plan, and behavioural only: no die study, no
  datasheet of Atari's firmware. The questions are the command set and its timing, the scan matrix, the
  mouse/joystick packet formats, and what the ACIA link does when the IKBD is busy — the last of which this
  session will have half-answered from the 6850's side.
* **The closing audit.** Walk `docs/handoffs/silicon-research-per-chip.md` §4 and ask of every chip: does every
  open question in our core have a labelled claim or an explicit `unknown` with its experiment? Then fold the
  measurements register into the design docs wherever a row has already become a task, and leave in it only the
  rows that a board would settle. The campaign's stopping rule is the one written in §6 of that file: stop when
  the open questions are labelled, not when the silicon is exhausted.

---

*Written 2026-11-28, closing the U101 study. The ordering stays as the per-chip plan set it: the four customs
first because the record was thin and the drawings were the gold, then the standard parts because their
datasheets are, and last the IKBD because nothing about it is silicon research at all.*
