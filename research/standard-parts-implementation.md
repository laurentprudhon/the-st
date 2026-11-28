# The four standard parts at the board level

### The MFP, the floppy controller, the two ACIAs and the PSG — what the board adds, what the datasheet leaves open, and what our model already assumes

*Subject: `U203` MC68901 MFP, `U301` WD1772 floppy controller, `U200`/`U201` MC6850 ACIAs, `U202` YM2149 PSG, and the one 74LS164 (`U900`) that serves their clocking, on the 1040STE board CA400329-101.
Studied 2026-11-28, the fifth and last study of the per-chip campaign.*

*Basis, and the method is inverted: **these parts have complete datasheets**, so the datasheet is the primary source and outranks every emulator, and the board outranks the datasheet only where the board is wired differently.
The work is therefore three narrower things — what the **wiring** adds, what the **datasheet leaves open**, and what our **model already assumes** that neither of them says.[38][39]
The inversion is the whole reason this is one short study rather than four long ones.[40][44]*

*Labels: `documented`, `derived`, `inferred`, `measured-elsewhere`, `unknown`; T1 hardware-captured, T2 hardware-corrected, T3 emulator-derived.
Companion pages: `design/timers-and-events.md` (the MFP as built), `design/disk-path.md` (the disk path as built), `design/virtual-chips.md` §§4–8 (the chip boundaries), `design/board-inventory.md` §§3.7–3.10 and §6 (the pins) and `design/emulator-limitations.md` (every question only a board would settle).
Sources are cited by number; the ledger's ids are stable across the campaign's studies.*

---

## 1. Executive summary

| question | answer | § |
|---|---|---|
| Is the MFP's clock 4 MHz or 2.4576 MHz? | **Both, on different pins.** `CLK` (35) is the bus clock, `CLK4` at 4 MHz — the part is rated 4.0 MHz maximum; the timers count the 2.4576 MHz crystal `Y200` on `X1`/`X2` (17/18).[38][41][42] | 3.1 |
| What is the PSG's clock? | **2 MHz**, the GSTMCU's `CLK2` tap on pin 22 — *not* `Y200`'s 2.4576 MHz. Atari says so twice, the datasheet's own limit says so, and the frequency arithmetic says so.[39][41] | 6.1 |
| How many MFP pins does the board use? | **47 named pins plus `N/C` at 12** — the six the inventory left open (`TC`, `SO`, `SI`, `RESET`, `DTACK` and the `N/C`) were resolved by this session's text-layer read, and §3.8's `IOA`/`IOB` puzzle is the YM2149's block read at another symbol's offset.[41] | 3.1 |
| Are the 6850's pin rows readable? | **Yes.** Sheet 2's text layer pairs all 24 pins per ACIA, and the result is the datasheet's own DIP-24 assignment; `U201` is the keyboard (pin 3 = 500 kHz per the field diagnostics), `U200` is MIDI.[40][41] | 5.1 |
| Which GPIP bit is which channel? | Bit *n* is channel *n* only for `n` ≤ 3; then GPIP4→6, GPIP5→7, GPIP6→14, GPIP7→15. The model gets this wrong for four bits.[38] | 3.3 |
| Is the 74LS164 one part or two? | **One**, and it is not a keyboard serialiser: on later STE revisions it is an 8-stage delay of `XSINT` before `TAI`, clocked at 2 MHz — 4 µs, 32 CPU clocks.[43] | 7 |
| Which clock reaches the MFP's `TAI`/`TBI`? | `TAI` counts `SINT` (DMA sound cycles), `TBI` counts `DE` (display lines). `KHZ500` reaches neither — its clients on this sheet are the two ACIAs.[41][42] | 3.1, 5.3 |
| What does the disk chip leave open that matters? | The **force-interrupt** status rule, the 16 µs register re-access, the four-revolution ID search, and the fact that a byte not collected in time is *written as zero* and flagged `LOST` rather than stalling the command.[44] | 4.2 |
| What does the model get most wrong? | Four things: the GPIP-to-channel map, Timer A's absence (it never counts), the timer data register's write-then-read behaviour, and a prescaler table that is right where the datasheet is right and silent about the pulse-width modes.[38] | 8 |
| What is genuinely open? | The FDC's power-on state, the DMA counter's readback cadence, the sound FIFO's depth, and the exact `A8`/`A9` numbering — all in `design/emulator-limitations.md`, none as a ticket. | 10 |

`derived` The one-sentence machine, assembled from §3–§7: *four peripherals that each speak a 6800-style bus to the GSTMCU, clocked from three different references — the 4 MHz bus clock, the 2.4576 MHz timer crystal and the 2 MHz `CLK2` tap — whose interrupts all funnel into sixteen channels of one encoder that presents a single `IRQ` at level 6.[38][39]
The four peripherals share the bus but not a clock.[41][42]

`derived` And the correction that reorganises the campaign's own seed list: **the crystal is not the sound chip's clock**, and a 2.4576 MHz PSG would have shifted every published ST frequency table by 1.23×.[39]
That is an error every musician would have noticed in 1989.[41]

---

## 2. The evidence, and what each layer can answer

### 2.1 The four datasheets, and what a standard part changes about the method

`documented` For a standard part the datasheet is complete in the sense the recovered ASIC drawings never were: it carries the register maps, the timing diagrams, the pin assignments, the electrical limits and, in Motorola's case, a paragraph on the behaviour of every corner that later became an emulator's folklore.[38][40]

| part | document | what it settles outright |
|---|---|---|
| MC68901 | Motorola `ADI-984`, *Advance Information*, January 1984, 52 pages[38] | pin assignment (§8.1), the sixteen-channel priority table (§3.1.1), the vector format (§3.1.2), the four EOI modes (§3.4), `GPIP`/`AER`/`DDR` semantics (§4), the whole timer section including the mode table (§5), the USART (§6), and the part's 4.0 MHz maximum (§8.3) |
| WD1772 | Western Digital, `WD1772` specification V1.1 in J.-L. Guérin's edition, 20 pages[44] | the pin table, the four register selects, eleven commands and their flag fields, the stepping rates, the 8 MHz ±0.1% clock requirement, and the Force Interrupt status rule |
| MC6850 | Motorola, `MC6850/MC68A50/MC68B50`, data sheet, 9 pages[40] | the four registers and their `RS`/`R/W` decode, `CR0`/`CR1`'s ÷1/÷16/÷64/reset field, the status bits, `CTS`'s inhibition of `TDRE`, and the false-start-bit logic |
| YM2149 | Yamaha, catalogue `LSI-2121492`, September 1992, 12 pages[39] | the pin layout, the 16-register array, the tone/noise/envelope formulae, the four bus-control modes, and the 2 MHz maximum with `SEL` high |

`documented` The datasheet is also where the *absences* are stated rather than inferred: the 68901 has no `IPL` pins and one `IRQ` (pin 32), the WD1772 has no head-load pin at all, and the YM2149's `SEL` pin has an internal pull-up so that an unconnected pin means "no divide by two".[38][44][39] `derived` Each of those absences decides something on this board — which is the cheapest architectural finding of the session and the reason the pin tables were worth reading a second time.

### 2.2 The board, read from the redraw's text layer

`derived` This session used that to close three of the four pin families the inventory had marked `unverified`, with the same count-alignment technique that settled `U101` (`research/blitter-implementation.md` §3.4).[41]

`derived` The pairing rule on sheet 2 is a constant offset, not a nearest neighbour: a pin's **number** is one text object, its **name** is another whose origin lies **1.8 pt further along the symbol's pin axis and 8.3 pt across it**, and the rule holds for every pin of every symbol on the sheet.
`derived` A name row is drawn *inside* the symbol and the number row *outside*, so the across-axis sign flips between a symbol's top and bottom edges — which is exactly why the earlier attempt, pairing by block order, "produced duplicates" on the 6850.[41]

| part | pins paired | cross-check | verdict |
|---|---|---|---|
| `U203` MC68901 | 45 of 47 by offset, plus `VCC`/`GND` drawn as power symbols | the datasheet's §8.1 pin figure, read by the same count rule, reproduces 46 of them and prints `N/C` at 12[38] | **complete**, and pin 12 is the package's one unnamed pin: 47 + 1 = 48 |
| `U202` YM2149 | 36 of 40 | the datasheet's pin layout prints the same 36 plus `N/C` 2 and 5, `SEL` 26 and `TEST1` 39[39] | **complete**, with `A8`/`A9` transposed against the datasheet (§9.4) |
| `U200`, `U201` MC6850 | 24 of 24 each | the datasheet's expanded block diagram prints the same numbers[40] | **complete**, and §6.4's "could not be paired" is closed |
| `U301` WD1772 | 28 of 28 | the datasheet's pin table matches on all 27 named pins[44] | **complete**, and pin 28 is `INTRQ`, not "a supply/test pin" |
| `U900` 74LS164 | 14 of 14 | the standard 74164 assignment[43] | **complete** |

`derived` And the net labels: the redraw terminates many wires in a **column of net names along the sheet's right edge**, each at the height of the wire it names, so a label's position along that column is the *pin's* position along the symbol — a 1 pt alignment that can be checked by semantics (a label named `XMFPCS` lands on the pin named `CS`).[41] `derived` Four consecutive pairs on the MFP's bus edge agree to within 0.9 pt — `CLK`/`CLK4` (35), `CS`/`XMFPCS` (48), `DS`/`XLDS` (47), `DTACK`/`XDTACK` (46) — which is what makes the same rule usable for the PSG's four control pins (§6.1) and for `SINT` on `IO7` (§3.1).[41]

### 2.3 Atari's own prose, which outranks every model

`documented` The field service manual is not only drawings: §2 describes each part's role in Atari's words and §3 is a **field-diagnostic chapter that names what each part must do**.[41]
That list is the cheapest statement of the behaviours a model has to support, and it names the failure modes the vendor expected.[41]

| the manual's sentence | what it decided |
|---|---|
| "The clock input is 2 MHz; the frequency response range is 30 Hz to 125 kHz" (PSG, §2.9.5) | the PSG's clock, against the inventory's crystal row (§6.1, §9.1) |
| "Verify that... the 2 MHz clock is present" (printer/joystick error code `P0`) | the same, from the diagnostic side |
| "Verify that Pin 3 of `U201` is 500 KHz ±50 KHz" (keyboard error code `K1`) | `U201` is the keyboard ACIA, and its `RxC` is the 500 kHz reference[41] |
| "Timer A... in the STE, its external event input is used to count DMA sound subsystem cycles" | `TAI` counts `SINT`[41] |
| "Timer B... external input can be used to count horizontal display lines" | `TBI` counts `DE`[41] |
| "`T5` PSG Bus Error. The PSG chip is causing a bus error by staying on the data bus too long" | the PSG's bus-holding contract (§6.2) |
| "`T0`... one or more of the four timers in the MFP did not generate an interrupt on counting down" | all four timers interrupt, including D (§3.2) |

### 2.4 The models are a checklist, and they are the record's own words

`inferred` It is a model, so it is T3 by definition, but it is the only place where somebody wrote down what the *pins* carry rather than what the registers do.[42]
Three of its entries are corroborated here by a datasheet or by Atari's prose.[38][41]

`inferred` Hatari's `dmaSnd.c` carries the sentence that settles the 74LS164: "on early STE models the `XSINT` signal was directly connected to MFP `GPIP7` and to Timer A input... on later revisions... the signal to Timer A input is now delayed by 8 shifts using a 74LS164 running at 2 MHz, which is equivalent to 32 CPU cycles".[43] `inferred` Neither Hatari nor NeoST models the delay, so no emulator's behaviour can be evidence about it.[43]

`inferred` The machine's own model — `TimersAndEvents` (real, 611 lines), `Disk`, `Sound` and `Controllers` (stubs) — is a checklist of every place this study can land as a task rather than as prose.

### 2.5 The board is still the only instrument, and it is still absent

`documented` The owner has no 1040STE and no logic analyser, so every question whose only route is a probe goes to `design/emulator-limitations.md` with its experiment and stays there (owner's ruling, 2026-11-28).
`derived` What this session could do instead is the part that changed: the *reading* tasks were done from the redraw's text layer, and the *datasheet* tasks from the four primary documents.[41][38]
What is left over is genuinely one scope and one boot.[39][40][44]

---

## 3. `U203` — the MC68901 multi-function peripheral

### 3.1 What the board says

`documented` The MFP is a 48-pin DIP carrying 47 named pins and one `N/C`, and the board's symbol uses every one of them: the text layer pairs 45 pins by offset and the remaining two (`VCC` 11, `GND` 36) are drawn as power symbols.[38][41]

| pin | signal | on this board | pin | signal | on this board |
|---|---|---|---|---|---|
| 1 | `R/W` | `RXW` | 25 | `IO3` | — |
| 2–6 | `RS1`–`RS5` | `A1`–`A5` (register select) | 26 | `IO4` | keyboard ACIA `N6850` → source 6 |
| 7 | `TC` | tied to `TDO` (16) | 27 | `IO5` | `BINT` (blitter done) → source 7 |
| 8 | `SO` | RS-232 transmit (via `MC1488`) | 28 | `IO6` | tied inactive-high |
| 9 | `SI` | RS-232 receive (via `MC1489`) | 29 | `IO7` | `XSINT` (sound frame end, XOR mono) → source 15 |
| 10 | `RC` | tied to `TDO` (16) | 30 | `TR` | not connected |
| 11 | `VCC` | — | 31 | `RR` | not connected |
| 12 | **`N/C`** | — | 32 | `IRQ` | `XMFPINT` → GSTMCU |
| 13–16 | `TAO`–`TDO` | `TAO`/`TBO`/`TCO` open; `TDO` → `TC`/`RC` | 33 | `IEO` | not connected |
| 17/18 | `XTAL1`/`XTAL2` | `Y200`, 2.4576 MHz | 34 | `IEI` | tied low (no chain) |
| 19/20 | `TAI`/`TBI` | `SINT` (delayed) / `DE` | 35 | `CLK` | `CLK4`, 4 MHz |
| 21 | `RESET` | `XRESET` | 36 | `GND` | — |
| 22–24 | `IO0`–`IO2` | printer busy, DCD, CTS | 37–44 | `D0`–`D7` | peripheral data bus |
| — | — | — | 45 | `IACK` | `XIACK` from the GSTMCU |
| — | — | — | 46 | `DTACK` | `XDTACK` |
| — | — | — | 47 | `DS` | `XLDS` (lower strobe, so odd addresses) |
| — | — | — | 48 | `CS` | `XMFPCS` |

`documented` The 68901's own §8.1 pin figure prints the same table and marks pin 12 `NC`, so the arithmetic that closes a transcription — 47 named plus one unnamed equals 48 pins — is a second, independent witness that the symbol was read whole.[38] `documented` And §2.1.3.2 states why the board ties `DS` to the lower data strobe: the vector number "must be located in the low byte of the data word", which forces every register to an odd address.[38]

`derived` That resolves the "either the symbol is read differently or the ports are elsewhere" question in one sentence: they are elsewhere, and the MFP's `GPIP` port is `IO0`–`IO7` on pins 22–29.[38]

`documented` The clocks are two different references on two different pin groups: the 2.4576 MHz crystal `Y200` with its two 30 pF loads on `XTAL1`/`XTAL2`, and `CLK4` on the bus clock pin.[41]
That split is why a part rated at 4.0 MHz maximum can sit on a machine whose crystals are 8 and 2.4576 MHz.[38]

`derived` The 500 kHz reference does **not** reach this chip's timer inputs.
The datasheet gives four timers and exactly two auxiliary inputs, `TAI` and `TBI`, and this session's reading assigns both: `TAI` to the delayed `SINT`, `TBI` to `DE`.[38][41][42]
`inferred` `KHZ500`'s clients on sheet 2 are the two ACIAs, whose `RxC`/`TxC` pins divide it internally by 16 and 64 (§5.3).[41][40]

`derived` Three of the four timer outputs are open: `TAO`, `TBO` and `TCO` reach no net, and `TDO` is wired back into `TC` and `RC` — which is the circuit Atari's own prose describes ("the 2.4576 MHz clock to the MFP is divided by the timer D (pin 16) output of the MFP to provide the basic clock for receiver and transmitter").[41][42]

`inferred` The daisy-chain is not implemented: `IEI` (34) is tied low and `IEO` (33) left open, which is what the datasheet requires of a single MFP in a system, and the `IACK` net that matters is the GSTMCU's `XIACK` into pin 45, shared with both ACIAs.[38][42]

| GPIP bit | channel | board line | evidence |
|---|---|---|---|
| 0 | 0 | Centronics `BUSY` | Atari lists "parallel port `BUSY`" among the MFP's interrupts[41] |
| 1 | 1 | RS-232 `DCD` | Atari lists `CTS`/`DCD`/`RI`[41] |
| 2 | 2 | RS-232 `CTS` | same |
| 3 | 3 | `BINT`, the blitter's busy/idle level | already settled (`research/blitter-implementation.md` §4.5) |
| 4 | **6** | the two ACIAs' `IRQ`, wired together | "6850 IRQs for keyboard and MIDI data"[41][42] |
| 5 | **7** | `FDINT` + `HDINT` (one shared line) | "disk (`FDINT` and `HDINT`)"[41] |
| 6 | **14** | RS-232 `RI` | same list[41] |
| 7 | **15** | `SINT` XOR `MONOMON` | "monochrome monitor detect" and "display enable"; Hatari prints the XOR[41][42] |

`derived` The channel number is a bit position in `IPRA`/`IPRB` and *not* the GPIP bit number: channels 0–3 are GPIP0–3, and then GPIP4→6, GPIP5→7, GPIP6→14, GPIP7→15, because the four USART channels take 9–12 and the timer channels 4, 5, 8 and 13.[38] `derived` That is the single most useful table in this study, and the model does not implement it (§8).

`derived` `MONOMON` and `SINT` sharing one pin is not a metaphor: sheet 2 carries a 74LS86 (`U209D`) in exactly that corner of the drawing, and Hatari's comment spells the same gate out — "monochrome monitor detect XOR dma sound play".[41][42] `derived` The consequence is a real one for software: a mono monitor holds the shared line in one state, so a sound-DMA completion and a monitor switch can be distinguished only by knowing the monitor.

### 3.2 What the datasheet leaves open, and the corners that matter

`documented` The prescaler ladder is a mode table, not a field: `0000` stops the timer, `0001`–`0111` are delay modes at ÷4, ÷10, ÷16, ÷50, ÷64, ÷100 and ÷200, `1000` is the event-count mode, and `1001`–`1111` are pulse-width modes at the same seven divisors.[38] `documented` The delay-mode period is the data register's value in count pulses — a data value of 100 at ÷10 times out after 1000 timer clocks — and a data value of 00 means 256.[38]

`documented` Four corners follow from the same paragraphs and each of them is a place a model can be silently wrong.[38]

| corner | what the datasheet says | why it matters |
|---|---|---|
| the prescaler edge | a new prescaler value takes effect immediately, and "the first time out pulse will occur at an indeterminate time no less than one nor more than 200 timer clock cycles" | a restart is not a clean `t + data × divider` |
| the data register while running | a write "is not loaded into the timer until the timer counts through 01 (hexadecimal)", and writing *while* it counts through 01 loads "an indeterminate value" | the write is armed, not applied; the model applies it immediately |
| reading the counter | "the value of the counter which was captured on the last low-to-high transition of the `DS` pin" | a read is a latch from the last bus cycle, not a live sample |
| the timer output | the output toggles at every time-out, so a delay-mode timer makes a square wave of **twice** the period; `TAO`/`TBO` can be forced low by bit 4 of their control register | the output is the baud clock's source (§3.1) |

`documented` The event-count mode has its own four: the count pulse comes from the active edge defined by the *AER bit of the associated GPIP channel* (`GPIP4` for `TAI`, `GPIP3` for `TBI`), changing that edge bit "may produce a count pulse", the input may transition at most once every four timer clocks, and the same transition also drives the `I3`/`I4` interrupt channels.[38] `derived` That last clause is a documented cross-talk the model has no room for: with `TAI` in event-count mode, a `SINT` edge can set the *keyboard ACIA's* channel 6 as well as Timer A's channel 13, because channel 6 is `I4` and `I4` is `TAI`'s channel.[38][42]

`documented` The pulse-width modes have no implementation anywhere in our tree: the timer runs while the auxiliary input is active, the interrupt arrives on the transition that *ends* the pulse — "the opposite transition as that normally defined by the edge bit" — and consecutive measurements require a fresh write to the data register.[38] `derived` Nothing on this board uses them, so their absence is a ruling rather than a gap (§10).

`documented` `GPIP`'s read-modify-write behaviour is stated exactly, and it is not the naive one: a write to the data register drives only the pins defined as outputs, "when the `GPIP` is read, data will be passed directly from the bits of the data register for pins which are defined as outputs", and input pins read "from the input buffers".[38] `derived` So a read-modify-write of the port copies *pin levels* into the input bits' latches, which is invisible while they are inputs and becomes visible the moment `DDR` makes them outputs — the corner the brief asked for, and the answer is "the datasheet's whole-port write plus a pin-level read", not the bit-level behaviour some models implement.[38]

`documented` The edge detector is "an exclusive-OR gate whose inputs are the edge bit and the input buffer", so writing `AER` itself "may cause an interrupt-producing transition", and the register "should be configured before enabling interrupts" — with the explicit warning that "changing the edge bit while interrupts are enabled may cause an interrupt on the corresponding channel".[38] `derived` The trigger is therefore the XOR's falling edge (`GPIP ⊕ AER` going to zero), which is what the model implements — but the model fires on *level* equality rather than on that edge, so a write that changes nothing re-arms a pending bit (§8).

`documented` The interrupt registers are minimal and asymmetric: `IER` is a plain enable whose a-zero write "will cause the corresponding bit of interrupt pending register... to be cleared"; `IMR` is an enable too — "when a bit is zero, interrupts are masked" — and `IPR`/`ISR` are cleared by writing ones with a single zero, "writing ones to `IPRA` and `IPRB` has no effect".[38] `derived` This is the datasheet confirming, in two sentences, the polarity that cost `#34` a boot (§4 of `design/timers-and-events.md`).

`documented` Priority is fixed and numeric: "`I7` is the highest priority interrupt channel and `I0` the lowest", and masking "re-prioritizes" the channels rather than reordering them.[38] `documented` Nesting is the `S` bit's job: with `S` = 1 a channel's in-service bit is set at the acknowledge, and "as long as the channel's in-service bit is set, no lower priority channel may request interrupt service nor pass its vector".[38]

`documented` The vector is four bits from the programmer and four from the chip: bits 7–4 are copied from the vector register and bits 3–0 "are the binary channel number of the highest priority channel that is requesting interrupt service".[38] `derived` In our numbering that is `(VR & 0xF0) + channel`, and the register's bit 3 is the `S` bit rather than part of the base — which is exactly how the model computes it.[38]

`documented` The USART's contribution to this study is a stub contract, and the datasheet gives the three sentences it needs: the `CLK` bit of `UCR` selects ÷1 or ÷16; a received word is transferred to the buffer only if the previous one was read; and an overrun "occurs when a received word is due to be transferred to the receive buffer, but the receive buffer is full", with neither the buffer nor the status overwritten.[38]

### 3.3 One timer expiry, end to end

`derived` Take the machine's own 200 Hz tick, because it is the one every TOS wait loop depends on: `Y200` at 2.4576 MHz, prescaler ÷64, data 192, so the counter is reloaded every 64 × 192 = 12 288 timer clocks — four per 20 ms frame at 49 152 clocks a frame.[38][41]

1. **The write.** TOS writes `TCDCR` = `$50` (channel C, ÷64) and `TCDR` = `$C0` (192), both at odd addresses through `XMFPCS`, `XLDS` and `RS1`–`RS5` = `A1`–`A5`.[38][41]
2. **The count.** The prescaler divides the crystal; each 64th clock decrements the 8-bit down counter; the counter is reloaded from the data register and a time-out pulse is produced when it is at `01` and the next pulse arrives.[38]
3. **The pending bit.** The time-out "is coupled to the timer's interrupt channel", which for Timer C is channel 5 — `IPRB` bit 5.[38]
4. **The encoder.** The selected source is the highest-numbered one that is pending, enabled in `IER`, unmasked in `IMR`, and not blocked by an in-service bit at or above it.[38]
5. **`IRQ`.** Pin 32 goes low; the GSTMCU combines it with VBL (4) and HBL (2) and drives `IPL0–IPL2` at level 6, which is the level the field manual's own table prints.[41]
6. **The acknowledge.** The CPU runs an interrupt acknowledge cycle with `FC2–FC0` = 6 and `A3–A1` = 6; the GSTMCU asserts `XIACK` into pin 45; the MFP asserts `DTACK` and places `(VR & 0xF0) | 5` on the low byte of the data bus.[41][38]
7. **The state moves.** The pending bit clears at the acknowledge, not at `RTE`, and the in-service bit sets only in software-EOI mode; the handler writes a zero to that `ISR` bit to re-enable lower channels.[38]

`derived` Every clock in that walk is the crystal's, except two: the bus cycle is `CLK4`'s, and the `IRQ`'s visibility to the CPU is the CPU's own sampling window.[38][42]
`derived` The 500 kHz reference enters nowhere — which is the answer to the brief's question, and the reason `design/timers-and-events.md` §10.3's open point can be closed as "it does not reach the MFP at all".[41][38]

### 3.4 What the model does

`documented` `src/TheST.Machine/Chips/TimersAndEvents.cs` is a real chip: 611 lines, a byte register file indexed by the low address byte, four counters' worth of deadlines, the four bit-files of the encoder, the vector path and the `GPIP` edge evaluator.

| claim | code | verdict |
|---|---|---|
| the clock is the 2.4576 MHz crystal and the ratio to the 32 MHz master is exact (`625/48`) | `MfpClock.TicksFor(...)` with a carried remainder | **aligned** |
| the prescaler ladder ÷4 … ÷200 | `Divider()` maps fields 1–7 to 4/10/16/50/64/100/200, 0 = stopped | **aligned** |
| event count is bit 3 of the control register | `(_r[0x1B] & 0x08) != 0` selects event count for Timer B | **aligned** for B, **missing** for A |
| the pulse-width modes exist (`1001`–`1111`) | no code path: bit 3 is read as "event count" and the divisor is ignored | **difference** — D7's modes are unreachable on this board, so a ruling, not a gap |
| data 0 means 256 | `_r[0x21] == 0 ? 256 : _r[0x21]` at every reload | **aligned** |
| a data write while running is armed until the roll-over | `RestartCounters` is called from the `TADR`…`TDDR` writes and recomputes the expiry immediately | **difference** — the doc says the right thing, the code does not |
| changing the prescaler gives an indeterminate first period | a clean restart from the write's instant | **difference**, and the datasheet's own wording makes the model's choice the *nicer* one |
| a counter read returns the value latched at the last `DS` rising edge | `ReadRegister` returns the live `_cCounter`/`_bCounter`/`_dCounter` | **difference** |
| Timer A counts `TAI` edges at `TAADR` events | **no Timer A at all**: `TADR` reads `_cCounter`, no expiry, source 13 never sets | **difference**, and the largest one |
| Timer B counts `DE` edges | `CountDe` decrements `_bCounter`, reloads and sets source 8 | **aligned** (one count per asserting record) |
| Timer D is arithmetic while the USART is a stub | loaded on writes, `_dExpiry` never armed, no pending bit | **by ruling** (`#63`), documented in the code's own note |
| a `GPIP` write stores the whole byte, inputs stay high-Z | `_r[0x01] = data`, and a read returns the same byte | **difference** — the read is not a window on the pins |
| an edge is `GPIP ⊕ AER` falling to zero | `EvaluateEdges` sets pending whenever the XOR *is* zero, on any `GPIP`/`AER`/`DDR` write | **difference** — level equality, not the falling edge |
| `GPIP` bit *n* is source *n* | `SetPending(bit)` for every bit 0–7 | **difference** for bits 4–7: GPIP4→4 instead of 6, GPIP7→7 instead of 15 |
| the encoder is pending ∧ `IER` ∧ `IMR` ∧ no in-service at or above | `Connected()` and `Selected()` | **aligned**, with the datasheet's own polarity |
| the vector is `(VR & 0xF0) + channel`, in-service only when `VR` bit 3 = 1 | `TryVector` | **aligned** |
| `IER`'s zero-write clears the pending bit | `_pending &= ~((data ^ 0xFF) << 8)` / `& (0xFF00 \| (data ^ 0xFF))` | **aligned** |
| `IPR`/`ISR` are `old & written` | `_r[0x0B] = (byte)(_r[0x0B] & data)` and the same for `ISR` | **aligned** |
| a stale acknowledge is a bus error, not a vector | `StaleAcknowledges++` and vector 24 | **aligned** with the system rule (`design/timers-and-events.md` §5) |
| `RS1`–`RS5` are `A1`–`A5`, every register odd | `index & 0x3F` and the mirrored block at `$FFFA30` | **aligned** for the even-alias rule; the mirror is the model's own choice |
| the USART answers transmitter-empty and never receives | `TSR` returns `0x80`, `RSR` `0x00` | **by ruling** |

---

## 4. `U301` — the WD1772 floppy controller

### 4.1 What the board says

`documented` The board's symbol is the datasheet's DIP-28 exactly, and this session resolved the pin the inventory had left illegible: **pin 28 is `INTRQ`**, an output, and the manual's own disk diagnostics use it ("check for an interrupt on pin 28 of the 1772").[44][41]

| pin | signal | net | pin | signal | net |
|---|---|---|---|---|---|
| 1 | `CS` | `FDCS` from `U300.22` | 15 | `VCC` | — |
| 2 | `R/W` | `CR/W` from `U300.25` | 16 | `STEP` | to the drive connector |
| 3 | `A0` | `CA2` from `U300.27` | 17 | `DIRC` | to the drive connector |
| 4 | `A1` | `CA1` from `U300.26` | 18 | `CLK` | 8 MHz (§4.2) |
| 5–12 | `DAL0`–`DAL7` | `CD0`–`CD7` (the DMA chip's device bus) | 19 | `RD` | from the drive |
| 13 | `MR` | the reset network | 20 | `MO` | to the drive |
| 14 | `GND` | — | 21 | `WG` | to the drive |
| 22 | `WD` | to the drive | 25 | `WPRT` | from the drive |
| 23 | `TR00` | from the drive | 26 | `DDEN` | to the drive |
| 24 | `IP` | from the drive | 27 | `DRQ` | `FDRQ` to `U300.21` |
| — | — | — | 28 | `INTRQ` | to the interrupt chain |

`documented` The register split is the DMA study's and stands: the FDC's own four registers are reached through the DMA controller's device port at `$FF8604`, and `$FF8606` bit 3 chooses `FDCS` or `HDCS` — the FDC never appears on the CPU's bus at all.[23]
`documented` And the two register selects are not the CPU's address lines: `CA1`/`CA2` "are set according to bit 1 & 2 of the mode register", so a write to `$FF8606` decides which of the FDC's four registers the next `$FF8604` access reaches — `$0080` for control/status and `$0180` for the data port in the guide's own worked table.[23]

### 4.2 What the datasheet leaves open, and the part's own numbers

`documented` The clock requirement is a number the board can be checked against: the `CLK` input "requires a free-running 50% duty cycle clock... at 8 MHz ±0.1%".[44] `derived` The board's only 8 MHz-class source is `U303` at 8.010613 MHz — 0.133% high, i.e. just outside the datasheet's window — and the extraction did not pair `U301.18`'s net, so whether the FDC takes that oscillator directly is a **read**, not a measurement (§9.8).[41]

`documented` The command set is eleven commands in four types, and the flags are dense enough that a model should read them from the table rather than from a mnemonic: Type I carries `h` (motor-on), `v` (verify), and a two-bit rate field; Type II carries `m` (multiple), `h`, `e` (15 ms settling) and, for writes, `p` (precompensation) and `a0` (deleted data mark); Type IV is the Force Interrupt with a four-bit condition.[44]

| rate field `r1 r0` | step rate | what the datasheet adds |
|---|---|---|
| `0 0` | 6 ms | a 4 µs (MFM) or 8 µs (FM) step pulse is issued per step |
| `0 1` | 12 ms | `DIRC` is valid 24 µs before the first pulse |
| `1 0` | **2 ms** | 15 ms of head settling follows the last step when `v` is set |
| `1 1` | **3 ms** | and 15 ms before a Type II/III command when `e` is set[44] |

`documented` Four corners decide how a sector behaves, and all four are stated:[44]

| corner | the datasheet's sentence | consequence |
|---|---|---|
| the register re-access | "after any register is written to, the same register can not be read from until 16 µsec in MFM or 32 µsec in FM have elapsed" | a poll loop that reads the command register twice inside 16 µs gets a stale answer |
| the ID search | an ID field "within four revolutions of the disk, or... the Record Not Found status bit is set" | four revolutions is ~800 ms at 300 RPM, not an instant |
| the data-mark window | the DAM "is to be found within 30 bytes in single density and 43 bytes in double density from the last ID field CRC byte" | 43 × 32 µs = 1.4 ms, and a miss restarts the ID search |
| the lost byte | on a read, "if the computer has not read the previous contents... that character is lost and the Lost Data Status Bit is set"; on a write, "a byte of zeroes is written on the diskette and the Lost Data bit is set", and "the command is not terminated" | a write tolerates a stall by corrupting the sector; a read does not tolerate it at all |

`documented` The **Force Interrupt** rule is the one no model in this tree implements, and it is a status rule rather than a timing one: if it arrives "when there is a current command under execution, the Busy Status Bit is reset, and the rest of the status bits are unchanged"; if it arrives when nothing runs, "the rest of the status bits are updated or cleared" and reflect a Type I command.[44]
`documented` Its condition field is `1000` for an immediate interrupt, `0100` for an interrupt on the index pulse and `0000` to "terminate without interrupt".[44]
`documented` And Atari's programming guide adds the two rules a driver needs: `$D8` (the immediate interrupt) is cleared only by a `$D0` load or a status read, so it should always be followed by a `$D0`, and a new command loaded inside 16 µs (MFM) "nullifies the forced interrupt".[23]

`documented` The motor-on behaviour is a protocol, not a flag: with `h` = 0 and `MO` low, the chip "waits 6 revolutions before executing the command" (about one second at 300 RPM), and after a command it lets `MO` fall after 9 idle revolutions.[44] `derived` So a sector's real latency has a floor that has nothing to do with the disk's rotation: a cold access begins with up to 1.2 s of spindle time.

`documented` And the completion instant is 24 µs (MFM) after the last CRC byte is written, while a read's `INTRQ` is set at the command's end and cleared by a status read *or* by loading a new command.[44] `derived` A driver that reads the status before the command's own settle time therefore clears the flag it was waiting for — one of the two ways TOS's floppy code can look stuck.

### 4.3 One sector as a timetable

`derived` At 8 MHz and MFM's 250 kbit/s, a bit cell is 32 clocks and a byte 32 µs; 6 250 bytes a track at 300 RPM is 200 ms a revolution; and a 512-byte sector's payload is 16.4 ms of that revolution.[44][41]

| instant | the FDC | the DMA side | the bus |
|---|---|---|---|
| write to the command register | status `BUSY` set; the register cannot be read for 16 µs (MFM)[44] | `$FF8606` was written before it — "the mode register is the only thing that arms a transfer"[23] | — |
| 15 ms (if `e` = 1) | head settling[44] | — | — |
| ID search | track, sector and CRC compared; up to 4 revolutions[44] | — | — |
| 22 bytes after the ID CRC (MFM) | — | — | — |
| ID match, `DRQ` raised | "if the `DRQ` is serviced", `WG` activates after an 11-byte (FM) / 22-byte (MFM) gap and 6/12 zero bytes[44] | the first byte arrives in the data register | the DMA chip's 16-byte FIFO fills, then asks for one burst of eight words — 32 bursts a sector, ~4 µs of bus each |
| 512 bytes | one `DRQ` per byte; a late read sets `LOST`[44] | one `DCYC` answer per two bytes: 312 answers a frame at a continuous 31 250 B/s | 32 µs per byte, 256 CPU clocks |
| the last CRC byte | `INTRQ` 24 µs later (MFM); status bit 0 clears[44] | `FDINT` → `GPIP5` → channel 7[38][42] | — |

`documented` The double strobe the brief asks about is not in this table because it is not in the chip: Atari's documentation "mentions that it is necessary to write the DMA control register immediately after you write the DMA data register. Otherwise it is possible to get a double strobe from the DMA chip."[23]
`documented` The same guide then says what no summary of it repeats — "in practice not following this recommendation seems to work fine" — and names the suspicion that it is the so-called bad DMA chip's problem.[23]
`derived` So `design/emulator-limitations.md`'s `D7` is a *documented caution with a disputed cause*, not an observed behaviour, and it is a model task only if a board shows it.

`derived` And the sector's clock is the drive's, not the chip's: the FDC's `CLK` sets the *bit* rate (8 MHz ÷ 32), the drive's spindle sets the *byte* rate, and the two agree only because the disk was written by the same class of machine — which is the whole reason a sector read has a 32 µs cadence and not a fixed duration.[44]

### 4.4 What the model does

`documented` `src/TheST.Machine/Chips/Stubs.cs`'s `Disk` is a register file and nothing else: `$FF8604` answers `0x0000` — no `BUSY`, no `TRACK0`, no interrupt — so TOS times out the probe and registers no drive, and `$FF8606`/`$FF8609`/`$FF860B`/`$FF860D` store what is written.

| claim | code | verdict |
|---|---|---|
| the FDC's four registers live behind the DMA chip's device port | `Case 0xFF8604: _machine.Disk.Register(index & 0x1F, …)` | **aligned** in address, not in ownership: one object answers both the FDC's window and the DMA's |
| the status word's bit 0 is `BUSY` and bit 4 is `TRACK0` | `StatusWord = 0x0000` | **by ruling** — the stub's contract, not the part's behaviour |
| a transfer asserts `DRQ`, `FDINT`, `RDY` | nothing asserts any of them | **by ruling** (`walking-skeleton.md` §1) |
| the byte cadence is 32 µs and the sector 16.4 ms | `design/disk-path.md` §3's table | **aligned** as arithmetic; no code path yet |
| the seek cost is `steps × step rate`, rates 6/12/**20**/**30** ms | `design/disk-path.md` §3 | **difference**: the datasheet's rates are 6/12/**2**/**3** ms[44] |
| a register written cannot be read for 16 µs | not modelled | **difference** |
| Force Interrupt's two status cases | not modelled (there is no command register) | **difference**, and it is the corner the brief named |
| the lost-byte semantics of a read and of a write | not modelled | **difference** |

---

## 5. `U200` / `U201` — the two MC6850 ACIAs

### 5.1 What the board says

`documented` The pin rows that the inventory could not pair are readable after all, and the reason they failed is now known: the 6850's two symbol rows sit at a different across-axis offset from the MFP's, so a single global offset produced duplicates while the 1.8 pt / 8.3 pt rule pairs all 24 pins per chip.[41][40]

| pin | signal | pin | signal |
|---|---|---|---|
| 1 | `GND` | 13 | `R/W` |
| 2 | `RXD` | 14 | `E` (enable) |
| 3 | `RXC` | 15–22 | `D7`–`D0` |
| 4 | `TXC` | 23 | `DCD` |
| 5 | `RTS` | 24 | `CTS` |
| 6 | `TXD` | 7 | `IRQ` |
| 8, 10, 9 | `CS0`, `CS1`, `CS2` | 11 | `RS` (register select) |
| 12 | `VCC` | — | — |

`documented` The result is the datasheet's own DIP-24 assignment on every pin, including the three chip selects that differ only in polarity (`CS0`/`CS1` high, `CS2` low) and the `RS` line that picks data from control/status.[40][41]

`documented` Which chip is which is settled from Atari's own diagnostics rather than from the parts list: the keyboard error code says "verify that pin 3 of `U201` is 500 KHz ±50 KHz", and pin 3 is the receive clock — so **`U201` is the keyboard ACIA and `U200` is MIDI**.[41] `derived` The 1991 parts list's "MIDI"/"RS-232" labels are therefore wrong twice over: RS-232 has no ACIA at all on this machine, it has the MFP's USART.[41]

`derived` Both ACIAs' `IRQ` outputs (pin 7) meet at `GPIP4` — Hatari's map says so in one line ("4 : ACIAs 6850 (ikbd and midi)"), and Atari's prose lists "6850 IRQs for keyboard and MIDI data" among the MFP's interrupts.[42][41] `derived` That wired-OR is why the handler has to read both status registers before deciding whose byte it is: channel 6 names the pair, never one of them.

`inferred` The 500 kHz reference reaches both chips' `RXC`/`TXC` pins and is divided inside each — `KHZ500`'s only clients on this sheet are these four clock pins — and the two links choose different divisors, which is the arithmetic of §5.3.[41][40]

### 5.2 What the datasheet leaves open

`documented` The control register's low two bits are a *field with a fourth state*: `00` ÷1, `01` ÷16, `10` ÷64 and `11` master reset, and "after power-on or a power fail/restart, these bits must be set high to reset the ACIA" — the chip has no reset pin, so the reset is a bus write.[40]

`documented` Three behaviours decide whether a link can stall, and none of them is a stall:[40]

| condition | the datasheet's sentence | what it means for the link |
|---|---|---|
| `CTS` high | "in the high state, the Transmit Data Register Empty bit is inhibited and the Clear-to-Send status bit will be high" | the transmitter looks full although it is idle — a poll loop spins, no byte is lost |
| the receiver is full and another arrives | "an overrun error occurs when a received word is due to be transferred to the receive buffer, but the receive buffer is full. Neither the receive buffer nor the `RSR` is overwritten" | the *new* byte is lost, the old one stays, and `OVRN` is set |
| `DCD` goes high | "the `DCD` input inhibits and initializes the receiver section"; a low-to-high transition "initiates an interrupt... when the Receive Interrupt Enable bit is set" | a modem hang-up looks like a received byte |

`documented` The interrupt is a level on an open-drain pin: "the `IRQ` output remains low as long as the cause of the interrupt is present and the appropriate interrupt enable within the ACIA is set", and status bit 7 mirrors it.[40] `derived` On this board that open drain is what lets two chips share `GPIP4` without contention.[42]

`documented` Bit synchronisation in the ÷16 and ÷64 modes is a counter, not a filter: it is "initiated by the detection of 8 or 32 low samples on the receive line", and a false start bit is deleted once "a full half bit of a start bit has been received".[40] `derived` At 500 kHz that is 16 µs or 64 µs of low line before the receiver commits — a quarter and a whole bit time — which is why a glitch on the keyboard line cannot start a character.

`documented` And the enable pin is the bus clock, not a convenience: `E` is "a derivative of the MC6800 Φ2 clock" that latches data and drives the bus buffers.[40] `derived` Its rate on this board is `CLK4`'s 4 MHz, four times the MFP's bus clock — the two peripherals do not share a bus timing even though they share a bus.

### 5.3 The two links' arithmetic

`derived` Both links are one reference and two divisors, and the divisor is the datasheet's `CR0`/`CR1` field:[40][41]

| link | divisor | bit rate | bit cell | a 10-bit character (start + 8 + stop) |
|---|---|---|---|---|
| keyboard (`U201`, 7812.5 baud) | ÷64 | 500 000 ÷ 64 = **7 812.5** | **128 µs** | 1.28 ms |
| MIDI (`U200`, 31 250 baud) | ÷16 | 500 000 ÷ 16 = **31 250** | **32 µs** | 320 µs |

`derived` The MIDI rate is exact and the keyboard's is not: 7 812.5 baud is a quarter of 31 250 and both come from the same 500 kHz tap, which is why the IKBD's firmware and the MIDI standard can share one reference.[40][41] `derived` And the overrun window is the character time: a byte that is not collected within 128 µs on the keyboard link is lost with `OVRN` set, not held.[40]

`documented` What the ACIA does "when the other end is busy" therefore has a precise answer, and it is the one `design/emulator-limitations.md`'s `S8` row was carrying: the chip has a one-byte receive register and an overrun flag, so a busy IKBD costs *bytes*, not time; `CTS` only affects the transmitter's status bit.[40]

### 5.4 What the model does

`documented` `Controllers` is a stub: `$FFFC00`-`$FFFC07` decode to it, a status read answers `0x02` (transmitter empty always, no received byte ever) and a data read answers `0x00`, so TOS gives up after its own 300 ms timeout and no phantom key changes the desktop state.

| claim | code | verdict |
|---|---|---|
| two ACIAs at `$FFFC00`/`$FFFC02` and `$FFFC04`/`$FFFC06` | one `Controllers` object over `$FFFC00`–`$FFFC07` | **difference** — the second chip is the same register file |
| the status byte's bit 7 mirrors `IRQ` | `AciaStatus = 0x02` | **by ruling** |
| `TDRE` (bit 1) is set, so a poll loop exits | `0x02` | **aligned** with the stub contract |
| ÷64 and ÷16 clocking, framing, `RDRF`, `OVRN`, `DCD` | nothing | **by ruling** (the link is the IKBD session's) |
| the keyboard link is in scope because the machine cannot be typed on | one register file shared with MIDI | **difference** documented in `Stubs.cs`'s own comment |

---

## 6. `U202` — the YM2149 programmable sound generator

### 6.1 What the board says, and the clock that was wrong

`documented` The board's net on pin 22 is **`CLK2`**, and `CLK2` is the GSTMCU's 2 MHz tap: the field manual's clock-divider sentence gives 8 MHz, 4 MHz and 500 kHz from 16 MHz, and the recovered design's divider ladder adds the 2 MHz stage that the four pads (`CLK8`, `CLK4`, `CLK2`, `KHZ500`) carry out.[41][42]

`documented` Three independent statements agree with that reading, and each would have been enough:[39][41]

| witness | what it says |
|---|---|
| the datasheet's pin description | `CLOCK` supplies the master clock, and the part's AC table gives "Max. 2 MHz when `SEL` is high" — 2.4576 MHz would be over the limit or, with `SEL` low, a 1.2288 MHz master and an octave-low machine |
| Atari's prose (§2.9.5) | "the clock input is 2 MHz; the frequency response range is 30 Hz to 125 kHz" |
| Atari's diagnostics (`P0`) | "check if the chip selects are being activated and the 2 MHz clock is present" |

`derived` The frequency ceiling closes it arithmetically: 125 kHz is exactly 2 MHz ÷ 16, the noise generator's fastest setting (`NP` = 1); at 2.4576 MHz the same register would produce 153.6 kHz and the manual's own range would be wrong.[39][41]

`derived` So `design/board-inventory.md` §2.3's crystal row — `Y200` "MFP / YM2149 clock" — is the stale record: `Y200` clocks the MFP's timers, and the PSG's clock is a *net* from the GSTMCU, not a crystal.[41][39]

| pin | signal | on this board | pin | signal | on this board |
|---|---|---|---|---|---|
| 40, 1 | `VCC`, `VSS` | power | 13–6 | `IOB0`–`IOB7` | the parallel port's data lines `D0`–`D7` |
| 3, 4, 38 | `ANALOG B`, `A`, `C` | the three channel outputs | 21–14 | `IOA0`–`IOA7` | side select, drive 0, drive 1, `RTS`, `DTR`, `STROBE`, GPO, n/c |
| 22 | `CLOCK` | `CLK2`, **2 MHz** | 24, 25 | `A9`, `A8` | `A9` tied low, `A8` tied high (§9.4) |
| 23 | `RESET` | `XRESET` | 26 | `SEL` | open: pull-up, so no divide by two |
| 27 | `BDIR` | `SNDIR` (GSTMCU pin 18) | 29 | `BC1` | `SNDCS` (GSTMCU pin 19) |
| 28 | `BC2` | tied to `VCC` | 30–37 | `DA7`–`DA0` | `SD0`–`SD7` |
| 39 | `TEST1` | open | 2, 5 | `N/C` | — |

`derived` The control pins are the bus decoder's three inputs, and the board gives it two nets: `BC1` = `SNDCS` and `BDIR` = `SNDIR`, with `BC2` unpaired on the sheet and tied to `VCC` per Hatari's table.[41][45]
`documented` Two lines suffice because the decoder is redundant: the datasheet says "the bus control is redundant, control is possible even when `BC2` is connected to +5 V".[39]

`derived` Hatari's own note says what the machine actually needs from the pair: "as only pins BC1 and BDIR are used in an Atari to address the YM2149, this means only 1 bit is necessary to access select/data registers ... only bit 1 of `$ff88xx` is useful".[45]
`derived` So `SNDCS` and `SNDIR` are the address/data pair, and the two-wire protocol is the whole of the machine's side of the PSG bus.[41][42]
`derived` And `SEL` being open is load-bearing: the datasheet gives it "pullup resistance... when this pin is not connected", so the master clock equals the input clock and the whole tone table is the ST's own.[39]

`documented` The ports are not the sound: Atari's disk diagnostics put drive selection on the PSG's own port — "pin 20 should go low when drive A is selected, and pin 19 when drive B is selected" — which is `IOA1` and `IOA2`.[41]
`derived` Hatari's pin table fills the whole port A in: `IOA0` the floppy side select, `IOA1`/`IOA2` drives 0 and 1, `IOA3`/`IOA4` the RS-232 `RTS`/`DTR` outputs, `IOA5` the parallel `STROBE`, `IOA7` unconnected.[45]
`derived` That is a correction to `design/board-inventory.md` §4.4's row (`IOB0–IOB3` → "controller select / FDD side select"): port B is the printer's data bus and the floppy and handshake lines are port A's — which is also why Atari's prose can say "Request to Send and Data Terminal Ready are output by the PSG chip".[41]

### 6.2 What the datasheet leaves open

`documented` The register array is sixteen bytes and every function is a formula over two of them, which is what makes this part's study short:[39]

| function | register(s) | formula |
|---|---|---|
| channel A/B/C tone | `R0`/`R1`, `R2`/`R3`, `R4`/`R5` | `fT = fMaster ÷ (16 × TP)`, `TP` a 12-bit value (8 fine + 4 coarse) |
| noise | `R6` | `fN = fMaster ÷ (16 × NP)`, `NP` 5 bits |
| mixer and port directions | `R7` | a `0` in a mixer bit *enables* that source; a `0` in `B7`/`B6` makes the port an input |
| channel level | `R8`–`RA` | `M` = 0 fixed 4-bit level, `M` = 1 the envelope generator's 5-bit output |
| envelope period | `RB`/`RC` | `fE = fMaster ÷ (256 × EP)`, `EP` 16 bits; the counter runs at `32 × fE` |
| envelope shape | `RD` | `CONT`, `ATT`, `ALT`, `HOLD` |
| port data | `RE`/`RF` | 8-bit latches, with pull-ups |

`documented` Two sentences in that table are corners rather than arithmetic: the envelope's *actual* clock is 32 × the repetition frequency and the counter "counts the envelope clock 32 times for each envelope pattern cycle", so a shape's step is `EP ÷ 250 000` seconds; and the ports "have a pullup resistance", so an unwritten port reads `$FF` rather than floating.[39]

`documented` The bus is a state machine with four modes and their timing: address mode "fetches a register address from the register array" and holds it "until the next address is fetched, and is not affected by the read, write, or inactive mode"; write mode takes `tDW` of 300 ns minimum; read mode answers in `tDA` = 400 ns; inactive mode leaves `DA7–DA0` high-impedance within 100 ns.[39] `derived` Those numbers are why the STE can put the PSG on a 4 MHz bus at all, and why the diagnostic can call a slow release a bus error (`T5`).[41]

`documented` And the part's own limit is the study's cleanest cross-check: the clock is specified as a maximum of 2 MHz with `SEL` high, 4 MHz otherwise, with a 40–60% duty cycle — a range the board's `CLK2` satisfies and the inventory's 2.4576 MHz does not.[39][41]

### 6.3 The arithmetic, in numbers

`derived` At 2 MHz a tone period and a frequency are one division apart, and the difference between the two candidate clocks is a factor every tune would have carried:[39]

| `TP` | 2 MHz | 2.4576 MHz |
|---|---|---|
| 284 | **440.1 Hz** (A4) | 540.8 Hz |
| 254 | 492.1 Hz | 604.7 Hz |
| 1 | 125 kHz | 153.6 kHz |

`derived` The envelope's step is `EP ÷ 250 000` s, so `EP` = 1 is a 4 µs envelope clock and a 32-step shape lasts 128 µs; at the wrong clock the same register is 23% faster and the note's decay no longer matches the tracker's own tables.[39] `derived` And `NP` = 1 gives the 125 kHz the manual quotes as the part's ceiling — the arithmetic that decided §6.1.[39][41]

### 6.4 What the model does

`documented` `Sound` is a stub with one real behaviour: a write to offset `0x00` records the selected PSG register, so `SelectedRegister` can be read back, and everything else stores.

| claim | code | verdict |
|---|---|---|
| the PSG is at `$FF8800`/`$FF8802`, the DMA sound page at `$FF8900`, the Microwire at `$FF9200` | one `Sound` object over three ranges | **difference** — three interfaces, one register file |
| `$FF8800` selects a register and `$FF8802` reads/writes it | `_selected = value & 0x0F`, and the data byte is stored separately | **difference**: the data port never answers the selected register |
| the clock is 2 MHz | `design/virtual-chips.md` §4 says 2 MHz but *derives* it "from `SCLK/4`" | **value aligned, provenance wrong**: the board's net is the GSTMCU's `CLK2`, not a divider on the Shifter's `SCLK`[41] |
| three tone channels, noise, envelope | nothing — no counters, no LFSR, no level table | **by ruling** (Sound is a stub; the study's arithmetic is the spec) |
| the ports read `$FF` unwritten and drive the drive selects | nothing | **difference**, and it is Controllers' side of the same pins |

---

## 7. `U900` — the one 74LS164

`documented` The parts list has exactly one 74LS164 in both revisions (1991: `U900`; 1989: `U211`), and the board's symbol resolves all fourteen pins to the standard 74164 assignment: `A` 1, `B` 2, `CLK` 8, `CLR` 9, and `Q0`–`Q7` on 3, 4, 5, 6, 10, 11, 12, 13.[41]

`documented` Its role is not the one the parts list's description claims.[43] Hatari's `dmaSnd.c` states it plainly: "on early STE models the `XSINT` signal was directly connected to MFP `GPIP7` and to Timer A input (for event count mode). On later revisions, as well as on TT, the signal to Timer A input is now delayed by 8 shifts using a 74LS164 running at 2 MHz, which is equivalent to 32 CPU cycles when the CPU runs at 8 MHz."[43]

`derived` That settles the count, the path and the delay in one sentence: **one part, eight stages, the `SINT`→`TAI` path, 4 µs** — 8 × 500 ns at `CLK2`, 32 CPU clocks at 8 MHz, and the same figure the STE article gives when it calls it a four-microsecond delay.[43][41]

`derived` So `design/board-inventory.md` §6.16's either/or resolves in favour of the delay, and the parts list's "keyboard/MIDI serialisation" is a description rather than a circuit: the keyboard link needs no shift register (the 6850's `TXD` and `RXD` are the link, §5.1), and no other part on this board is an eight-stage delay.[40][41]

`derived` The consequence is one constant and one edge: `SINT` sets source 15's pending bit at the record's own instant, and the timer's edge — and therefore Timer A's count, its expiry and channel 13 — arrives 4 µs later.[38][43] `derived` An early-revision board has no delay at all, so the model's choice is a *board* choice; ours is a mature 1991 build and takes the delayed copy.

`inferred` Neither Hatari nor NeoST models the delay — Hatari's comment says so explicitly ("at the emulation level, we don't take into account this delay of 32 CPU cycles, as it would add complexity and no program are known so far to require this delay"), and NeoST's own mapping table agrees.[43]

---

## 8. What the model needs

`derived` Twelve differences came out of §§3–7 and they are not of one kind: four are arithmetic (a wrong constant, a missing channel map), four are behaviour the datasheet states and the code does not, three are rulings that are already written down, and one is a stub contract that a later ticket inherits.[38][44]
Only the first four would change a number a program can see.[39][40]

| # | the claim | where it lives | verdict | action |
|---|---|---|---|---|
| 1 | `GPIP` bit *n* is channel *n* only for *n* ≤ 3; GPIP4→6, GPIP5→7, GPIP6→14, GPIP7→15 | `Chips/TimersAndEvents.cs` `EvaluateEdges` | **difference**: four bits set the wrong channel (GPIP4 sets Timer D's 4, GPIP7 sets `FDINT`'s 7) | **land**: a five-entry map in `EvaluateEdges` and in `design/timers-and-events.md` §7's table |
| 2 | Timer A exists: an event counter on `TAI`/`SINT`, channel 13 `IPRA` bit 5 | same file: no Timer A at all; `TADR` reads `_cCounter` | **difference**: the largest | **task** (#28's follow-up), with `TAADR` as the reload and the 4 µs delay as the edge offset |
| 3 | a timer data write while running is armed, not applied | `RestartCounters` on `0x1F`–`0x25` writes | **difference** | **land** if cheap: set the data register and let the roll-over reload it; `design/timers-and-events.md` §6 already says this |
| 4 | a counter read returns the value latched at the last `DS` rising edge | `ReadRegister` returns the live counter | **difference** | **land** as a latch updated on the select record |
| 5 | a `GPIP` read returns the latch for outputs and the pin for inputs | `_r[0x01]` only | **difference** | **task**, gated on a driver for `BINT`/`FDINT`/`SINT` (`#30`, `#29`, `#26`) |
| 6 | an edge is `GPIP ⊕ AER` *falling*, not equality | `EvaluateEdges` | **difference**: re-fires on a no-op write | **land**: compare with the previous level |
| 7 | event-count edges also drive `I3`/`I4` (channels 3 and 6) | nothing | **difference** | **task**, with Timer A |
| 8 | the pulse-width modes (`1001`–`1111`) | nothing | **difference by ruling**: nothing on this board uses them | **ruling** in `design/timers-and-events.md` §10 |
| 9 | the WD1772's stepping rates are 6/12/**2**/**3** ms | `design/disk-path.md` §3's table says 6/12/**20**/**30** | **difference**: a transcription of the field's high-to-low order | **land**: correct the row |
| 10 | Force Interrupt clears `BUSY` and touches no other status bit while a command runs | no command register exists | **difference**, and `D7`'s double strobe is its neighbour | **task** with the Disk chip (`#29`/`#95`) |
| 11 | the FDC's registers cannot be re-read inside 16 µs (MFM) | nothing | **difference** | **task**, one line in the Disk model |
| 12 | the PSG's clock is `CLK2`, 2 MHz, from the GSTMCU | `design/virtual-chips.md` §4 says 2 MHz, derived from `SCLK/4` | **value aligned, provenance wrong** | **land**: a dated correction in `virtual-chips.md` §4 and in `board-inventory.md` §2.3 |

`derived` Four of the twelve are corrections to files rather than to code, and they are landed in place in the sections that carry the claim rather than noted here only: §3.1's pin table in `board-inventory.md`, §6.1's crystal row, `disk-path.md` §3's rate row, and `timers-and-events.md` §10's open points.[38][44]
Each carries the date and the sentence that settles it.[39][41]

`derived` And one row is now closed outright: the brief's own seed — "`KHZ500` reaches one of `TAI`/`TBI`" — is wrong in both directions, because both inputs are taken and the 500 kHz reference's clients are the ACIAs.[41][42]

---

## 9. Conflicts adjudicated

`documented` Nine disagreements came out of the record and the sources, and each is stated with what settles it rather than with a preference.[41][42][44]

1. **The PSG's clock: 2 MHz or 2.4576 MHz.** *The record:* `board-inventory.md` §2.3 tags `Y200` "MFP / YM2149 clock"; `virtual-chips.md` §4 and `ADR 0001` say 2 MHz. *Settled for 2 MHz* on five counts — the board's net on pin 22 is `CLK2`; Atari's prose says "the clock input is 2 MHz"; the diagnostic says "the 2 MHz clock"; Hatari's pin table records `CLOCK : connected to 2 MHz`; and the datasheet's own maximum with `SEL` high is 2 MHz, with the 125 kHz ceiling equal to 2 MHz ÷ 16.[41][39][45]
2. **One crystal tagged for two parts.** *The record:* the `Y200` row names two loads. *It powers one:* the MFP's timers on `X1`/`X2`, and the PSG's clock is a GSTMCU net — the campaign's own warning about a 1.23× error in a seed, made concrete.[41][42]
3. **The MFP's pin rows.** *The record:* pins 21 and 46 "illegible", and an `IOA`/`IOB` block that "could not be reconciled". *Settled:* 21 is `RESET`, 46 is `DTACK`, 47 is `DS`, 12 is `N/C`, and the `IOA`/`IOB` block is the YM2149's, read at another symbol's offset.[38][41]
4. **The YM2149's `A8`/`A9`.** *The board's redraw* prints `A8` 24, `A9` 25; *the datasheet's pin layout* prints `A9` 24, `A8` 25, and Hatari's pin table agrees with the datasheet (`A8` 25 `: connected to VCC`, `A9(INV)` 24 `: connected to VSS/GND`).[39][45]
*Settled in the datasheet's favour*, because the tie-offs are load-bearing and the datasheet states the select programme they implement: "A8 has pullup resistance while A9 has pulldown ... connect A8 and A9 to +5 V and ground respectively when not in use".[39]
What remains is a **read** of the redraw to see which pin the two straps land on; the consequence of getting it wrong is a silent machine, which is why the record's "immaterial" note is withdrawn.[41]
5. **One 74LS164 or two.** *The record:* the parts list has one and describes it as a serialiser; the STE article describes one added to delay `*SINT`. *Settled:* one part, on the delay, 8 shifts at 2 MHz, 4 µs — and the serialiser deserves no circuit, because the ACIA's `TXD`/`RXD` are the keyboard link.[43][40][41]
6. **`KHz500` into the MFP.** *The record:* "MFP timer input; which of `TAI`/`TBI` it reaches is not legible". *Settled:* neither — `TAI` is `SINT`, `TBI` is `DE`, and the reference's clients are the ACIAs' clock pins.[38][41][42]
7. **The FDC's pin 28.** *The record:* "name illegible... a supply/test pin on this package". *Settled:* `INTRQ`, an output, and Atari's own disk diagnostics poll it.[44][41]
8. **The FDC's clock.** *The datasheet:* 8 MHz ±0.1%. *The board:* one 8.010613 MHz oscillator, and the `CLK` pin's net was not paired. *Narrowed:* the part is 0.133% over the datasheet's window if it takes that oscillator directly, which is a **read** of sheet 3 at the oscillator and at pin 18 — not a scope.[44][41]
9. **The PSG's port assignment.** *The record:* `board-inventory.md` §4.4 puts the FDD select on `IOB0–IOB3`. *Atari's diagnostics* put drive A and B selects on pins 20 and 19 — `IOA1`/`IOA2` — and the programming guide refines it to the three low bits of register 14: bit 0 the side, bit 1 drive 0, bit 2 drive 1.[41][23]
*Corrected in place* in §4.4's row, with the diagnostic's sentence beside it.[41][39]

---

## 10. What stays unknown, and where it lives

`documented` Nothing new became a ticket this session (owner's ruling, 2026-11-28): every row that only a board could settle is a row in `design/emulator-limitations.md`, and three of them changed state here.[41][38][40]

| row | was | now | what closed it |
|---|---|---|---|
| `S8` — what the ACIA does when the IKBD is busy | `documented` in part | **`documented`** | the 6850's own overrun paragraph: a one-byte buffer and an `OVRN` flag, so busy costs bytes, not time[40] |
| `G2`/`G4`–`G6` — the GSTMCU's own | unchanged | unchanged | not this session's (§6.1's clock tap is `documented` in the recovered design)[42] |
| `D5`/`D6` — the DMA counter's readback and the DMA chip's power-on state | `open` | **`open`** | `D6` is `U300`'s, and this study does not narrow it; the **FDC is not that case** — its `MR` (pin 13) is on the reset network, so a chip with no reset pin is the DMA controller's distinction alone[44][41] |
| `S7` — the sound FIFO's depth | `open` | **`open`** | unchanged; it is the Shifter's pins, not the PSG's |
| new: the PSG's `A8`/`A9` numbering | — | **`read`** | one 600 dpi crop of the YM2149 symbol with both labels legible (§9.4) |
| new: the FDC's `CLK` source | — | **`read`** | one crop of sheet 3 at `U303` and at `U301.18` (§9.8) |
| new: `TAI`'s and `TBI`'s net labels | — | **`read`** | the same sheet, at pins 19/20 — the alignment succeeded for the other pins, so the labels exist[41] |

`derived` The three `read` rows are readings, not measurements: each is one crop and the owner's eyes, which is why they sit beside the register's `read` state rather than in the `open` list.

---

## 11. The experiments that would settle the rest

`documented` Six experiments, each a TOS program on a real 1040STE, and each written so that its result is one number or one level.[38][44]
`derived` None of them blocks a model change; each *confirms* one that the datasheet already justifies, which is the point of keeping them short.

| # | experiment | pass condition | what it would change |
|---|---|---|---|
| 1 | scope `TAO` (13) in output mode at ÷1 and ÷200 with a known data value | the datasheet's `data × prescale`, with the expiry at the roll-over and not one clock early[38] | the prescaler's edge alignment — one constant |
| 2 | write a byte to `$FFFA01` with a mixed `DDR`, read it back, then flip one bit of `DDR` | whether the input bits read the pin (the datasheet) or the latch (our model)[38] | `GPIP`'s read shape, and with it any `BINT` poll |
| 3 | enable `AER` bit 3 for the falling edge and `IMRA` bit 3, then blit | exactly one `IRQ` at completion and none at the start[38][42] | `BINT`'s level-to-edge conversion, the row `design/emulator-limitations.md` `B6` leaves open |
| 4 | issue a `WD1772` command, then `$D8` (immediate force interrupt) and `$D0` mid-sector; read the status and `INTRQ` | `BUSY` cleared and every other bit unchanged while a command runs, the Type I set when it does not, and no `INTRQ` from a `$D0` at all[44][23] | the Disk model's force-interrupt path |
| 5 | loop `TXD` back to `RXD` and measure the bit cell at both ACIA divisors | 128 µs (÷64) or 32 µs (÷16), and the field matches the wire[40] | the two links' rates, and the IKBD session's timeout budget |
| 6 | play a tone at a known period and measure the output | 2 MHz: `TP` 284 gives 440 Hz[39][41] | nothing — it is the confirmation of §6.1 |

---

## 12. Licence position

`documented` No image is vendored and no sheet is reproduced: the four datasheets, the field manual and the two emulator sources are cited by URL, and their prose is quoted in sentences only.[38][39][44]
The two emulator files are referenced by name and line.[42][43]

`derived` **No sheet set**, and the reason is the method's inversion: for a standard part the drawing *is* a datasheet page, and every figure this study would have drawn — a timer's ladder, a sector's timetable, the encoder — is a table in the source.
The one figure considered and rejected was the interrupt chain (sixteen sources → one encoder → `IRQ` → GSTMCU → `IPL` → the CPU), because §3.3's seven numbered steps carry it in fewer words than a drawing.[38][41]

---

## Sources

[23] http://info-coach.fr/atari/documents/_mydoc/FD-HD_Programming.pdf — Info-Coach — DMA / WD1772 Programming
[38] https://www.bitsavers.org/components/motorola/68000/MC68901_Multi-Function_Peripheral_Jan84.pdf — Motorola — MC68901 Multi-Function Peripheral, advance information ADI-984, January 1984
[39] https://www.bitsavers.org/components/yamaha/YM2149_199209.pdf — Yamaha — YM2149 Software-Controlled Sound Generator (SSG), catalogue LSI-2121492, September 1992
[40] http://www.sprow.co.uk/bbc/hardware/extraserial/6850datasheet.pdf — Motorola — MC6850/MC68A50/MC68B50 Asynchronous Communications Interface Adapter, data sheet
[41] https://jsobola.atari8.info/DEREATARI/atarisch/1040ste_fsm.pdf — Atari — STe 520/1040 Computer Field Service Manual, C302481-001 Rev A, August 1991 (2017 retyped/redrawn copy)
[42] https://github.com/hatari/hatari/blob/master/src/mfp.c — Hatari — src/mfp.c (the per-machine MFP pin tables, the GPIP channel map, the prescaler table)
[43] https://github.com/hatari/hatari/blob/master/src/dmaSnd.c — Hatari — src/dmaSnd.c (the 74LS164 delay of XSINT before TAI, 8 shifts at 2 MHz)
[44] https://hxc2001.com/download/datasheet/floppy/thirdparty/FDC/WesternDigital/WD1772-JLG-V11.pdf — Western Digital — WD1772 Floppy Disk Controller Specification V1.1, J.-L. Guérin edition, November 2008
[45] https://github.com/hatari/hatari/blob/master/src/psg.c — Hatari — src/psg.c (the YM2149 pin table: 2 MHz clock, A8/A9 straps, port A's control lines)
