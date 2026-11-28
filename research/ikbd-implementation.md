# The IKBD (HD6301V1): the link, the commands and the two packet families

### What Atari's protocol document says, what the mask ROM actually does, and what the model must own on both sides of a 7 812.5-baud wire

*Subject: the Intelligent Keyboard — a Hitachi HD6301V1 microcontroller on the keyboard's own PCB, wired to
the ST by `TXD`/`RXD` and nothing else. Studied 2026-11-28, closing the per-chip campaign and the only entry
in it with no silicon research in it at all: the part is a 6801-family MCU running a 4 kB mask ROM that
nobody at Atari published, so the study is a protocol study — the interface, not the die.*

*Basis: **Atari's own protocol document is the primary source and it is one text**, reproduced by the Linux
kernel's input documentation and vendored verbatim in this tree.[63] The firmware's behaviour comes from a
published disassembly of the 4 kB ROM with a RAM map beside it, which is a *reading of the shipped program*
and not a model of it.[72] The chip's own arithmetic — 4 MHz crystal, E at 1 MHz, `E/128` for 7 812.5 baud,
the free-running counter on E — is the Hitachi data book.[69] The ST's side is EmuTOS's ACIA set-up and its
packet parser.[65][66] Hatari is the checklist, never the evidence.[64]*

*Labels: `documented`, `derived`, `inferred`, `unknown`; T1 hardware-captured, T2 hardware-corrected, T3
emulator-derived, with `measured-elsewhere` used where someone published a hardware number and the study only
cites it. Companion pages: `research/standard-parts-implementation.md` §5 (the 6850's half of this link),
`design/subsystems-spec.md` §1's Controllers row, `design/virtual-chips.md` §7, `design/board-inventory.md`
§3.10 and §4.6, `design/timers-and-events.md` §7 (the interrupt path). The rows that stay open are in
`design/emulator-limitations.md` §7.*

---

## 1. Executive summary

| question | answer | § |
|---|---|---|
| What is the link, exactly? | **7 812.5 baud, 8 data bits, one stop, no parity** — the ACIA's ÷64 on the 500 kHz tap, and the IKBD's own `E/128` on its 1 MHz internal clock. Two dividers, one rate: 128 µs a bit, **1.28 ms a ten-bit character**.[40][69] | 7.1 |
| Who may speak first? | **Nobody, and then both.** There is no arbitration, no request line and no flow control: the IKBD transmits whenever it has something (event or reply); the ST writes whenever it likes; each end has its own small buffer and its own overflow rule. | 3.1 |
| How is a command framed? | By **parameter count, not by a terminator**: the receiver is a state machine keyed on (command byte, bytes still expected). Unknown bytes are **NOPs**; `$80` is the only two-byte command and swallows the byte after it.[63][72] | 4.1 |
| What is acknowledged? | **Only the reset.** `$80 $01` answers `$F1` (early parts `$F0`) within 300 ms. Everything else is silent; the inquiries answer with a packet, which is a reply rather than an ack.[63] | 4.2 |
| What does the ST's parser use to find packet boundaries? | **One threshold: every byte ≥ `$F6` is a header.** Ten headers, ten fixed lengths; make codes stop at `$72` and break codes at `$F2`, so a scancode can never be mistaken for a header.[66] | 5.1 |
| One key event? | make `n` (1…`$72`), break `n \| $80`; **make is the plain code and break has bit 7 set** — the inverse of what this repo's page said.[63] | 5.1, 6.4 |
| One matrix scan? | **8 lines read (`DR1`) × 15 driven (`DR3` bits 1–7 and `DR4` bits 0–7) = 120 positions**, 94 populated. Not 8 × 7: the page's shape does not survive the chip's own register widths.[71][72] | 6.1 |
| Debounce? | **None in time.** The firmware reads a port, reads it again, and loops until the two reads agree — a repeat-until-stable sample, not a timer.[72] | 6.3 |
| Auto-repeat? | **Not the IKBD's.** It reports make and break only; TOS synthesises the repeat — 300 ms then every 40 ms.[65][68] The IKBD's one internal repeat is the joystick keycode mode's `Tn`/`Vn`. | 6.5 |
| A mouse record? | **Three bytes: `$F8 \| buttons`, ΔX, ΔY**, two's complement, **up the screen is negative** with the default Y origin at the top; a report is broken into several packets beyond ±127.[63] | 5.2 |
| A joystick record? | **Two bytes: `$FE`/`$FF` then `%x000yyyy`** — bit 4 is the trigger, bits 0–3 are up/down/left/right.[63] | 5.3 |
| What does a burst cost? | 3 bytes × 1.28 ms = **3.84 ms a mouse record**, so the link carries at most **260 records a second**, and a queued burst of six is 23 ms behind the hand that caused it.[40][69] | 7.4 |
| What happens when the ST is slow? | The ACIA's one-byte buffer is overrun: the **new** byte is lost, `OVRN` is set, the link does not stall — and the ST's parser re-synchronises only at the next `$F6` header.[40][66] | 7.5 |
| What happens when the IKBD is slow? | Its **7-byte input buffer** drops new command bytes, and a **21-cell output ring** drops the **whole report it cannot fit** — the 20-byte usable depth is the ring minus the one-byte margin the store test reserves.[72] | 7.5 |
| What must the model own? | A real 6850 at `$FFFC00`/`$FFFC02` (status bits, ÷64, one-byte receive, `IRQ`→`GPIP4`→channel 6), and a second machine behind it that answers `$80 $01` with `$F1` and then emits make/break bytes and 3-byte mouse records. Nothing else is needed to type at the desktop.[40][63] | 8 |

---

## 2. The evidence, and what each layer can answer

| layer | what it gives | what it cannot |
|---|---|---|
| Atari's IKBD protocol document[63] | the command set with parameter counts, the packet formats, the scan-code table, the default modes, the reset timing, the status inquiry layout — **the only place all of it is written down at once** | what the firmware does on a full queue, on an unknown byte mid-command, on an overrun |
| the commented ROM disassembly and RAM map[72] | the actual behaviour: buffer addresses and depths, the drop rules, the SCI-overrun response, the column walk, the read-until-stable sampling | nothing is licensed for vendoring; read it for facts |
| the Hitachi HD6301V1 data book[69] | the SCI's rate arithmetic from the part's own clock, the free-running counter's clock, the port widths, 4 kB ROM / 128 B RAM | anything Atari programmed into it |
| the ST side: EmuTOS's ACIA set-up, packet parser and mouse set-up[65][66][67] | what the machine programs, sends and expects, and the boot order | the IKBD's side |
| Hatari, Steem SSE, NeoST[64][70][35] | a checklist of quirks with measured numbers (delays, depths, packet bytes) | evidence — T3 by definition |
| our model — `Controllers`, a stub answering status `0x02` | every place this study lands as a task rather than as prose[64] | nothing about the part |

`documented` **The archive the campaign used for Atari's documents no longer answers.** `dev-docs.atariforge.org`
failed to resolve on 2026-11-28, and the Wayback Machine holds a snapshot of its file index but no capture of an
`IKBD_Protocol.pdf`. The protocol text therefore has two surviving homes: this tree's vendored copy, taken from
the Steem SSE distribution, and the Linux kernel's reproduction of it.[63][70] `derived` The text is the same
document in both places, down to the per-key table's `{ NOT USED }` rows, so the version this study quotes is
Atari's and not a retyping.

`documented` **The firmware is not a black box, and that is the campaign's one piece of luck here.** The tree
carries a commented disassembly of the 4 kB ROM beside a RAM map naming every buffer, counter and mode byte, and
a note explaining how the scan-code table sits in the ROM at `$F319` in column order.[72] Everything this study
says about queues, drop rules and the scan walk is read off that listing with its addresses quoted, so the next
session can check a line rather than trust a sentence[72].

---

## 3. The turn-taking walk

The brief's one new method rule was to write the *sequence* before the table, and it pays: what follows is one
numbered walk, and every later table is a row of it.

**3.1 Who may speak first — nobody, and then both.**

1. **There is no arbitration, no poll and no request line.** `TXD` and `RXD` cross between `U201` and the
   HD6301, and the keyboard's cable carries three wires plus power.[41] Neither end can hold the other off[63]:
   the ACIA's `CTS` input is not a flow control on this board (the 6850 says it inhibits only the
   transmitter's status bit), and the IKBD's SCI has no equivalent input at all.[40][69]
2. **Each end transmits on its own initiative.** The IKBD writes a byte when it has a scancode, a mouse
   record or a reply to send; the ST writes a byte when it has a command. Both directions can be busy at
   once, and typically are.[72]
3. **Each end therefore owns a queue and an overflow rule**, and they are different rules: one-byte with
   overrun on the ACIA side, N-byte with drop-the-new-bytes on the IKBD's command side, and drop-the-whole-
   report on the IKBD's event side (§7.5).[40][72]

**3.2 One command, byte by byte — the states.**

4. **The ST programs nothing but the divisor.** At reset EmuTOS writes the ACIA's control register twice: once
   with `$03` (master reset), then with receive-interrupt enable, `RTS` low, transmit interrupt disabled,
   clock ÷64 and 8 data bits / 1 stop / no parity.[65] `derived` The master reset is a bus write because the
   6850 has no reset pin, and the IKBD never sees it.[40]
5. **The ST sends `$80 $01`.** The IKBD runs its self-test (RAM pattern, ROM checksum) and answers `$F1`.[63][72]
6. **The IKBD then sends one byte per stuck key**, with **bit 7 set** — a break code without a make code in
   front of it is the documented "key matrix error" flag[63] — *before* the version byte, not after it.[72]
7. **From that instant the IKBD is a report generator**: it scans one matrix column per pass of its main loop
   and appends make/break bytes, mouse records and joystick records to its output ring, and its SCI interrupt
   hands them to the ACIA one character time apart.[72]
8. **The ST writes a command byte and, by its count, further parameter bytes.** The receiver collects them in
   its command buffer and consumes the whole sequence when the expected count is reached; a byte that is not a
   known command is a NOP, and a `$80` waits for exactly one more byte.[63][72][66]
9. **Some commands answer immediately** — `$0D`, `$16`, `$1C`, `$21`, and the `$8x` inquiry family — by
   putting a packet on the same output ring as the events, so a reply queues behind whatever reports are
   already in flight (§7.3).[63][72]
10. **Most commands answer nothing at all**, and the ST relies on that: the mouse set-up of a desktop boot is
    six silent writes in a row.[67]

**3.3 What the IKBD may send unprompted, and when it may not.**

11. **In the default modes** it sends make/break bytes and joystick event records, and mouse records whenever
    motion exceeds the threshold or a button changes.[63][72]
12. **`$13` (PAUSE) stops the transmitter, not the scanner**: the matrix is still read and events are still
    enqueued, and mouse motion is *accumulated* so that resuming costs the minimum number of packets.[63][72]
    `derived` The guide's warning — "because of the limitations of the microcontroller memory this command
    should be used sparingly" — is the output ring's 20 usable bytes seen from the host's side.[63][72]
13. **The monitoring modes invert the traffic**: joystick monitoring (`$17`) and fire-button monitoring
    (`$18`) stop everything except a periodic packet, and the sample rate is the host's choice bounded by the
    line's own capacity.[63][64]

**3.4 The reset window, and why it is a mode and not an instant.**

14. **The reset is a window, not a state.** A command received *during* the self-test can put the firmware in
    a mode that reports mouse and joystick together: Hatari records `$08 $14`, `$08 $0B $14`, `$12 $14` and
    `$12 $1A` arriving in that window as the trigger, and names *Barbarian* and *Hammerfist* as the programs
    that hit it.[64] `inferred` This is a firmware artefact with no sentence in the guide — the guide says only
    that a reset "returns the keyboard to its default (power-up) mode and parameter settings", which a command
    arriving mid-test obviously violates.[63]
15. **The window has a length, and that length is what "wait for the ACK" is really for.** Atari TOS delays
    about 312 ms after the reset and ignores what arrives; EmuTOS instead reads until it sees a `$F0`/`$F1`
    byte and then drains the buffer for 5 ms, and its source explains the hazard: with Ctrl+Alt+Del held the
    real IKBD sends `$9D` (Ctrl released), then `$F1`, then the three held-key makes.[65] `derived` The order
    in step 6 — breaks before the version — is what makes that sequence possible at all.[65]

---

## 4. The command set

**4.1 The table, one row per state.**

`documented` Parameter counts are Atari's own, and Hatari's table agrees with them row for row.[63][64] The
last column is what the command changes in the IKBD's state, in the firmware's own bytes where the RAM map
names them.[72]

| code | parameters | answers with | what it changes |
|---|---|---|---|
| `$80` | one (`$01` only) | `$F1` (or `$F0`) | full reset: modes and parameters to defaults, ROM/RAM self-test, stuck-key scan; the clock survives |
| `$07` | 1 | — | mouse button action (mode byte `$B4`); `%00000mss`: `ss` picks absolute reports on press/release, `m` = buttons act as keys |
| `$08` | 0 | — | relative mouse reporting (the default) |
| `$09` | 4 | — | absolute mode: resets the internal position, sets X/Y maxima |
| `$0A` | 2 | — | mouse keycode mode: X/Y clicks per cursor keystroke |
| `$0B` | 2 | — | relative threshold in ticks per axis (default 1,1) |
| `$0C` | 2 | — | absolute-mode scale: ticks per internal unit, per axis |
| `$0D` | 0 | `$F7` + 5 | interrogate the absolute position (valid only in absolute mode) |
| `$0E` | 5 | — | preset the internal absolute position (one filler byte, then X and Y) |
| `$0F` | 0 | — | Y origin at the bottom (toward the user becomes negative) |
| `$10` | 0 | — | Y origin at the top (the default; toward the user positive) |
| `$11` | 0 | — | RESUME: leave PAUSE; a NOP when not paused |
| `$12` | 0 | — | disable all mouse reporting (and the buttons-as-keys action) |
| `$13` | 0 | — | PAUSE output; the scanner keeps running, motion accumulates |
| `$14` | 0 | — | joystick event reporting (the default) |
| `$15` | 0 | — | joystick interrogation mode: no events, ask with `$16` |
| `$16` | 0 | `$FD` + 2 | joystick interrogation |
| `$17` | 1 | 2-byte packets at the rate | joystick monitoring, rate in hundredths of a second |
| `$18` | 0 | 1-byte packets, 8 samples each | fire-button monitoring of joystick 1, the bit rate's own sampling |
| `$19` | 6 | — | joystick keycode mode: `RX RY TX TY VX VY`, tenths of a second |
| `$1A` | 0 | — | disable all joystick events |
| `$1B` | 6 | — | set the time-of-day clock, packed BCD, invalid digits ignored |
| `$1C` | 0 | `$FC` + 6 | read the clock — the reply is the same seven-byte record as the clock packet |
| `$20` | 3, then `NUM` data bytes | — | memory load into the controller's RAM, ≤128 bytes, <20 ms apart |
| `$21` | 2 | `$F6 $20` + 6 | memory read, always six bytes |
| `$22` | 2 | — | execute a subroutine in controller RAM (the demos' door into the 6301) |
| `$87`–`$9A` | 0 | `$F6` + 7 | status inquiry: `0x80 \| SET` returns that set command's parameters, padded to eight bytes |

`derived` **The table has one row per state because the receiver is keyed on (byte, count), and only one
entry is variable-length.** `$20` takes three parameters and then a *data phase* whose length the third
parameter decides, so it is the one command that cannot be validated by a fixed count; every other command
completes the moment its last parameter lands.[63][72]

**4.2 The reset family, and the only acknowledgement.**

`documented` Atari's document is unusually explicit here: the reset "is the only two byte command understood by
the IKBD. Any byte following an 0x80 command byte other than 0x01 is ignored (and causes the 0x80 to be
ignored)".[63] `derived` That sentence is a state rule with a cost attached: a stray `$80` on the wire consumes
the byte after it, so a desynchronised host loses a whole command for every extra byte it writes[63].

`documented` The reply is `$F0` on the first release and `$F1` afterwards, the byte doubling as the
version/release number,[63] and the ROM's own boot path writes `$F1` from the self-test's tail.[72] `derived`
Within 300 ms of the command, the break or power-up, per the guide;[63] Hatari schedules it 502 000 CPU clocks
after the command (≈63 ms) and adds a 0–3 000-cycle jitter, which is T3 and only tells us the emulator's
authors found the real one early rather than late.[64]

`derived` **A missing ACK does not stop the machine; it stops it for 300 ms.** The stub never answers, and TOS's
`ikbd_readb(300)` returns 0 on its timeout, so the boot continues.[65] This is exactly why the Controllers stub
"works" — and why it must not be read as evidence about the part (§8).[65]

**4.3 The `$8x` status family.**

`documented` Status commands are the SET commands with bit 7 set, and every answer is a `$F6` packet padded to
eight bytes: `$F6`, the mode byte, four parameters, two zero pads.[63] `derived` The padding is a deliberate
design — the reply minus its header is directly re-sendable as the set command — so the pads are NOPs by
construction, and the firmware's NOP rule (§4.4) is what makes the round trip legal[63].

`documented` Two restrictions come with the family: only one inquiry may be outstanding at a time, and status
inquiries are not valid in joystick-monitoring or fire-button-monitoring mode.[63]

**4.4 What is not a command.**

`documented` "Command codes (such as 0x00) which are not specified should perform no operation (NOPs)".[63]
`derived` The ROM implements that literally: the receive routine ignores a `$00` byte when no command is in
progress, and the parameter collector abandons a partial command rather than reporting an error when the count
and the byte disagree.[72] **A protocol with no error message and no retransmission is a protocol whose only
failure mode is silence** — which is the second reason the hosts time out instead of retrying[72].

**4.5 What TOS actually sends at boot — and a correction to the brief.**

`documented` EmuTOS's keyboard initialisation is one command: `$80 $01`, wait for `$F0`/`$F1`, drain.[65] The
mouse set-up is *not* in the BIOS boot; it belongs to the VDI, and it is four to six silent writes: `$08`
(relative), `$0B` `$01` `$01` (threshold 1,1), `$10` (Y origin at the top), `$07` `$00` (buttons are part of
the mouse), with `$12` before and after the desktop's lifetime.[67]

`derived` **The brief said "the two commands TOS sends at boot"; it is one at boot and a family at desktop
start-up.** (Corrected in place in `docs/handoffs/ikbd-hd6301-study.md` §2 as well — what a model needs is the
reset ACK plus the six mouse writes being *accepted silently*, not two named commands.) The suite a model must
swallow to reach a typed-at desktop is therefore: `$80 $01` answered, and `$12`, `$08`, `$0B xx xx`, `$0F` or
`$10`, `$07 xx` accepted without a byte in reply.[65][67]

---

## 5. The two packet families

### 5.1 The keyboard, and the one threshold that frames everything

`documented` Make codes start at 1 and run to `$72`; the break code for a key is "obtained by ORing 0x80 with
the make code"; the codes `$F6`–`$FF` are reserved for the non-keyboard records, with `$F6` the status report,
`$F7` the absolute position, `$F8`–`$FB` the relative position, `$FC` the clock, `$FD` both joysticks and
`$FE`/`$FF` joystick 0 and 1.[63]

`derived` **That reservation is the ST's packet delimiter, and EmuTOS implements it as one comparison.** Its
ACIA handler treats *any* byte ≥ `$F6` as a packet header and then collects a fixed length from a ten-entry
table: 8 bytes for `$F6`, 6 for `$F7`, 3 for each of `$F8`–`$FB`, 7 for `$FC`, 3 for `$FD`, 2 for `$FE` and
`$FF`.[66] `derived` Because make codes stop at `$72` and break codes at `$F2`, no key byte can ever be
mistaken for a header — the reserved block is what buys the ST a resynchronisation point at all (§7.5).[66]

`documented` The table itself is 120 bytes in the ROM at `$F319`, one byte per matrix position, and the
disassembly prints it in column order with the keys' names beside it.[72]

### 5.2 The mouse: three bytes, two of them a delta

`documented` The relative record is "a three byte record of the form (regardless of keyboard mode)":
`%111110xy` — where **`y` is the right button and `x` is the left** — then ΔX, then ΔY as two's complement
integers.[63] `derived` In bit terms the header is `$F8 | (right << 0) | (left << 1)`, which is what Hatari
builds and what EmuTOS's `RIGHT_BUTTON_DOWN 0x01` / `LEFT_BUTTON_DOWN 0x02` decode.[64][65]

`documented` Three consequences the guide states in one paragraph:[63]
- **resolution is never traded for the threshold** — "regardless of the threshold, all bits of resolution are
  returned to the host computer";
- **accumulation is allowed to exceed the threshold** — a report may carry "significantly more than the
  threshold delta", because no event is generated while the keyboard is paused or while a record is being
  transmitted;
- **a delta beyond ±127 is split** — the motion "is broken into multiple packets"[63].

`derived` **The sign convention is a set command, not a wire property.** With `$10` (Y=0 at the top, the
default) "mouse motion toward the user is positive and away from the user negative"; `$0F` inverts it.[63] So
**up the screen is negative** on a default-mode ST, and a model that hard-codes one convention is wrong for
every program that sends `$0F`.

`inferred` `$F9` is the button-only variant of the header the guide folds into `$F8`–`$FB`, and EmuTOS's table
treats all four values as the same three-byte record; Hatari never emits `$F9` at all.[63][66][64]

### 5.3 The joystick: two bytes, one of them a bitmask

`documented` The event record is `%1111111x` (x = joystick 0 or 1) followed by `%x000yyyy` where the high bit
is the trigger and `yyyy` is the stick position.[63] `derived` The four direction bits are the port's own lines
— bit 0 up, 1 down, 2 left, 3 right — which is Hatari's mask and the wiring the RAM map describes when `DR4`
becomes an input through the 74LS244 select.[64][72]

`documented` Interrogation answers with `$FD` and a byte per joystick, and monitoring answers with a two-byte
pair `%000000xy` (the two fire buttons) and `%nnnnmmmm` (joystick 1 in the high nibble, joystick 0 in the
low).[63] `derived` The pair has no header at all: in monitoring mode the *mode itself* is the framing, which
is why the guide warns that the host must not ask for a rate "higher than the serial communications channel
will allow the 2 bytes packets to be transmitted" — at 1.28 ms a byte, 2 bytes is 2.56 ms, so the rate's
ceiling is about 390 pairs a second, i.e. one per 2.56 ms[63].

### 5.4 The clock and the status packets

`documented` The clock packet is `$FC` plus year, month, day, hour, minute, second in packed BCD — seven bytes
— and the status packet is `$F6` plus seven.[63] `derived` The ROM keeps the six BCD cells at `$82`–`$87` and
increments them from a **1 ms output-compare interrupt** (`OCR += 1000` on the E-clocked 16-bit counter, a
thousand-count `$80` byte to the second), so the clock's resolution is one second and its tick is arithmetic,
not a VBL.[69][72]

---

## 6. The matrix and the scan

### 6.1 The shape: 8 read lines, 15 driven — not 8 × 7

`documented` The IKBD drives the matrix from `DR3` and `DR4` and reads it on `DR1`: `DDR1` is all-input, and
"bits 1–7 of `DR3` are mapped to the first seven keyboard's matrix columns ... all bits of `DR4` may be
mapped to the remaining ones", with `DR1` the rows.[72][71] `derived` That is **15 driven lines × 8 read
lines = 120 positions**, and the 120-byte scan-code table at `$F319` is exactly 8 × 15 — the count is
confirmed twice, by the register widths and by the table's length.[72]

`documented` The ST's keyboard has 94 keys and the table has 120 cells, so the mapping is a **table and not
an arithmetic**: the unfitted positions read zero, they cluster in the low drive lines of rows 1–4 and in the
`DR3` columns of rows 5–7, and the ISO key and the function-key block sit where a rectangular fill would have
put something else.[63][72]

`derived` **The repo's page said "8 rows × 7 columns" and 94 keys in the same table, which cannot both be
true** — 56 cells cannot hold 94 keys. The 7 is `DR3`'s seven bits mistaken for the whole drive set, and
`DR4`'s eight were dropped. Corrected in place in `atari-st-hardware/components/02-ikbd-keyboard.md`.

### 6.2 One column, end to end

`documented` The scan of one column, from the listing:[72]

| step | the firmware | why it is in this order |
|---|---|---|
| 1 | `DR2 = $01` | deselects the 74LS244 so the joystick's direction pins are not driving `DR4` |
| 2 | `DR3 = NOT($8C)`, `DDR4 = $FF`, `DR4 = NOT($8D)` | the 16-bit pattern in `$8C`/`$8D` holds **one** line low; the other fourteen are driven high and their cells are isolated by the keyboard's diodes |
| 3 | read `DR1`, read it again, loop until equal | step 3 is the whole of the debounce (§6.3) |
| 4 | `DR3 = DR4 = $FF`, `DR2 = $FE`, `DDR4 = 0` | all lines released, the joystick buffer re-selected, `DR4` back to input |
| 5 | shift `$8C`/`$8D` left one and store | the next column; fifteen shifts per pass, the sixteenth wraps |

`derived` **One column per pass of the main loop, not one column per timer tick.** The 16-bit shift at `$F2D5`
walks the pattern and jumps back to the loop's head, so a full matrix scan is fifteen iterations of a loop that
also reads the mouse, checks the command buffer and tests the SCI — which is why the scan rate is a property of
the loop's instruction count rather than of any programmed period.[72]

`derived` The arithmetic bounds it rather than fixing it: the column block itself is about 44 machine cycles at
the part's 1 MHz E clock, and the key-decode and mouse paths around it are of the same order, so one column
costs some tens to a few hundred microseconds and a full pass of fifteen lands between roughly 0.7 ms and
5 ms — hundreds of hertz.[69][72] `unknown` The exact figure, since it needs either a cycle-exact count of
every loop path or a scope on `DR1` (§10).

### 6.3 Debounce: read until stable, not time out

`documented` Three places in the firmware do the same thing — read the port, read it again, and branch back if
the two reads differ — for the key rows (`DR1`), the buttons (`DR2`) and the joystick/mouse directions
(`DR4`).[72] `derived` **There is no debounce timer**: stability is established by two consecutive equal
samples, so the time constant is the interval between two adjacent reads, tens of microseconds, and a
contact's bounce is filtered only if it is still bouncing *at that instant*. `inferred` That is enough in
practice because a bouncing contact that is still changing is sampled again on the next pass, and the state
machine only emits a code when the *key* state changes[72].

### 6.4 Make and break: the polarity the page had backwards

`documented` "The keyboard always returns key make/break scan codes... The break code for each key is obtained
by ORing 0x80 with the make code."[63] `derived` So a keypress is the plain code and its release is the code
with bit 7 set — `1` then `$81` for Escape, `$3B` then `$BB` for F1. The repo's page had it inverted (make =
`$80 + n`), which would make every keyboard work exactly upside down; that is a bug the desktop would hide,
because TOS would open a key on the release and close it on the press, and a fast tap would look like a
keypress of the wrong duration rather than like an error. Corrected in place.

`documented` The firmware applies the distinction in one place: the byte a stuck key produces at reset is
written *with* bit 7 set, and the guide says that a break code arriving without a make code is "a flag for a
key matrix error".[63][72]

### 6.5 Auto-repeat: not this chip

`documented` TOS owns the repeat. EmuTOS keeps `kb_initial`/`kb_repeat` — 15 and 2 ticks of a 20 ms timer,
i.e. **300 ms then every 40 ms** — and re-emits the last key's record without asking the IKBD for
anything.[65][68] `derived` The IKBD's contribution to a repeat is
exactly zero — it reports make once and break once — which is why any model that synthesises repeats inside
the keyboard is modelling a different machine.

`documented` The one repeat the IKBD does own is the joystick keycode mode's: `RX`/`RY` are the velocity
breakpoints in tenths of a second and `TX`/`TY`, `VX`/`VY` the intervals before and after them.[63]

---

## 7. The link, priced

### 7.1 The two ends' arithmetic, and they agree to the bit

`documented` The ST's ACIA divides the 500 kHz tap by 64: 500 000 ÷ 64 = **7 812.5**, 128 µs a bit.[40][41]
`documented` The IKBD's SCI is programmed with `RMCR = $05`, whose low bits select `E/128`, and with the
part's 4 MHz crystal its E clock is 1 MHz — so 1 000 000 ÷ 128 = **7 812.5**, 128 µs a bit.[69][72] `derived`
Two different crystals, two different dividers, the same rate: the seed's "check the IKBD's side divides its
own clock to the same rate" is answered, and it agrees bit for bit[69].

`derived` The character is ten bits (start + 8 + stop) = **1.28 ms**, which at the CPU's 8 MHz is 1 024 clocks
— Hatari's own constant — or 1 025.4 at the real 8.010613 MHz oscillator.[64][41] The line's capacity is
therefore 781.25 bytes a second.

### 7.2 The ACIA's one byte, and what "busy" costs on that side

`documented` The 6850 has a one-byte receive register with an overrun flag; when a word is due to be
transferred and the buffer is full, "neither the receive buffer nor the `RSR` is overwritten" — the new word is
lost and `OVRN` is set.[40] `derived` So the ST's side of a busy pair costs **bytes, not time**: the old byte
survives, the new one is gone, and nothing in the ACIA slows the IKBD down[40].

`documented` `CTS` and `DCD` are the other two answers: `CTS` high inhibits only `TDRE` (a poll loop spins and
nothing is lost), and `DCD` high inhibits the receiver.[40] `derived` On this board `CTS` and `DCD` are tied
low, so neither ever applies — the keyboard link has no flow control at any level.[41]

### 7.3 The IKBD's transmit pacing, and the reply's latency

`documented` The SCI interrupt sends the next byte as soon as `TDRE` sets, and masks its own transmit interrupt
when the ring is empty, so a queued burst leaves back-to-back at one character each (1.28 ms).[72] `derived`
Hatari's measured interval between bytes handed to the ACIA is 6 920–7 200 CPU clocks (≈0.87 ms), which is
*less* than a character: the ACIA's own shift register absorbs the difference, and the wire still sees 1.28 ms
per character.[64][40]

`documented` Hatari's reply delays are the only measured numbers in the record for command latency: 7 000–7 500
cycles (≈0.9 ms) before the first byte of a `$F6` or `$FC` reply, and 7 500–10 000 (≈0.9–1.25 ms) for `$FD`,
with the comment that the delay is randomised "to simulate the slight variations measured on a real ST".[64]
`derived` That is about one character of processing, so **a round trip of a two-byte command and its six-byte
reply costs roughly 10 ms** — the command's own 2.56 ms plus the reply's 7.7 ms plus the latency — and a host
that sends inquiries faster than that accumulates them in the ring, not on the wire[64].

### 7.4 What a burst costs — the number the brief asked for

`derived` A mouse record is 3 bytes = **3.84 ms**, so the link carries at most **260 records a second**
(781.25 ÷ 3), and every record carries up to ±127 counts per axis[63].

`derived` **The sampling rate therefore cannot be a fixed 40 Hz, and the guide never claims one.** Its
requirement is a *velocity*: ~200 counts per inch, tracking at up to 10 inches a second, i.e. 2 000 counts a
second.[63] Sent one count per record that would need 2 000 × 3.84 ms ≈ 7.7 seconds of line per second — 7.7×
the link — so the guide's own design has to accumulate, and it says so: motion is accumulated while a record is
being transmitted, and a delta beyond ±127 is split into several packets.[63] **The mechanism is the link's
budget expressed as a rule**; the briefly-quoted 40 Hz figure is not in the source and is corrected in place in
the brief.

`derived` A burst of six queued records — the ring's 20 usable bytes ÷ 3 — is 23 ms behind the movement that
caused it, which is about one and a half frames of a 50 Hz display: a spinner's fastest flick can therefore
arrive late enough for the desktop's pointer to lag visibly, which is the observable consequence of the ring's
depth rather than of any timing in the mouse.

### 7.5 What happens when either end is busy — both sides, in bytes and in milliseconds

**The ST is slow.** `documented` The ACIA loses the *new* byte and sets `OVRN`; the old byte is still there to
be read.[40] `derived` The damage is not the lost byte but the lost *alignment*: EmuTOS's parser is a
length-driven state machine, so a byte missing from the middle of a 3-byte mouse record makes the next delta
byte the first data byte of the next record, and the parser only realigns when a byte ≥ `$F6` happens to
appear.[66] `derived` In practice a lost mouse record therefore costs one or two more records, and a lost break
code in a key record leaves a key stuck down until its next break arrives[66].

**The IKBD is slow.** `documented` Two different rules, because the firmware has two buffers:[72]

| buffer | where | depth | what overflows, and what is dropped |
|---|---|---|---|
| commands | `$CD`–`$D4`, counter `$CC`, "full" flag in `$CB` bit 5 | **7 usable** of 8 | the receive routine refuses a byte while the counter is already 7, so **the newest bytes are dropped**; the flag then makes the collector abandon the partial command |
| events and replies | `$D9`–`$ED`, index `$D5`, free counter `$D7` (low 7 bits free, bit 7 = ring empty) | **21 cells, 20 usable** | the store test is `needed >= free`, so a record is only written if it fits **with one byte to spare**; the **whole report** is dropped otherwise |

`derived` **The 21st cell is why Hatari's "20" and the RAM map's `$D9`–`$ED` do not disagree.** The array is
21 bytes and its index wraps at 21 (`cmpa #$15` on both the store and the fetch), but the store test is
`needed >= free`, so the last byte of space can never be claimed: **at most twenty bytes are ever stored, and
the twenty-first cell is a margin rather than a slot.** The margin scales with the record: a three-byte mouse
report needs four bytes free, so at most eighteen bytes of records are ever queued — six reports.[72] `derived` Corrected in place in `design/emulator-limitations.md`, which
carried "8 in, 20 out" as three T3 numbers to verify: the input side is 7 usable rather than 8, and the output
side is right in effect and wrong in size.

`documented` The IKBD's *receive* overflow has a third rule, and here the chip's own data book supplies the
mechanism the firmware only uses: on an SCI overrun the ROM reads `RDR`, sets the wake-up bit `WU`, spins, and
re-arms — and while `WU` is set "RDRF and ORFE flags are not set even if data are received or errors are
detected. Therefore received data are ignored", with the hardware clearing the bit "on receipt of ten
consecutive '1's", i.e. on an idle line.[69][72] `derived` So an IKBD that has just lost a command byte is
**deaf for ten bit times — 1.28 ms of silent line** — and every byte the ST writes inside that window is
discarded rather than queued: the way back is silence, not a retry.[69] `documented` The ROM's flags byte and
`$D4` both move when it happens, and Steem's note records the same shape from the other side ("the ROM sets the
chip in stand-by mode when it detects an overrun").[72]

---

## 8. What the model must own, claim by claim

`documented` `Controllers` is a stub over `$FFFC00`–`$FFFC07`: a status read answers `0x02` (transmitter empty
always, no received byte ever), a data read answers `0x00`, and no byte is ever delivered.[`Stubs.cs`] The
design's own words are the reason it may stay that way for now: delivering one IKBD byte "feeds TOS a
fabricated keyboard version string, and a phantom key changes the desktop state with no input path behind it".

| claim | code | verdict | `design/virtual-chips.md` §7 says |
|---|---|---|---|
| the keyboard ACIA is `$FFFC00`/`$FFFC02` and MIDI is `$FFFC04`/`$FFFC06` | one `Controllers` object over `$FFFC00`–`$FFFC07` | **difference** — one register file for two chips | §7 lists **one** ACIA member, `U201` |
| the status byte reports `RDRF`/`TDRE`/`OVRN` and bit 7 mirrors `IRQ` | `AciaStatus = 0x02` | **by ruling** (stub contract) | §7 promises the status bits; the stub is the ruling |
| `TDRE` is set, so TOS's send loop exits | `0x02` | **aligned** with the stub contract | — |
| `IRQ` → `GPIP4` → channel 6, shared with MIDI | nothing: `N6850` is a declared wire with no producer | **difference** — the interrupt path is dark | §7's pin list and `TimersAndEvents`'s `GPIP` bit 4 |
| ÷64 and 8N1 framing, 128 µs a bit | nothing | **difference** | §7 ("the IKBD serial link at 7 812.5 baud") |
| one-byte receive buffer, `OVRN`, drop-the-new-byte | nothing | **difference** | §7's "ACIA transmit/receive registers and framing counters" |
| a receive timeout is TOS's, not the model's | the stub's silence makes TOS wait its 300 ms | **aligned in effect** | — |
| the reset ACK (`$F1`) | nothing | **difference** | §7's IKBD side, as corrected 2026-11-28 |
| a command state machine with parameter counts and NOPs | nothing: writes are stored and discarded | **difference** | §7's IKBD side, as corrected 2026-11-28 |
| make/break bytes | nothing | **difference** | §7's IKBD side ("the matrix belongs to the IKBD's ports") |
| 3-byte mouse records with the Y origin's sign | nothing | **difference** | §7's IKBD side |
| the IKBD's own buffers and drop rules | nothing | **difference** — and the mechanism a "busy IKBD" claim rests on | §7's IKBD side ("an output ring that drops the whole report") |
| the matrix, debounce and scan cadence | nothing | **by ruling** — a model that owns the packets does not need the matrix | §7 keeps the matrix **inside** the IKBD side, not as a ninth chip |

**The minimum a model needs to type at the desktop, in five parts.**

1. **The ACIA as a chip, not a register file**: `$FFFC00`'s status bits (at least `RDRF`, `TDRE`, `OVRN`),
   `$FFFC02`'s one-byte receive register with the overrun rule, the ÷64 divisor with the control register's
   master-reset state, and `IRQ` wired to `GPIP4` so that channel 6 fires.[40][42]
2. **A reset answer**: `$80 $01` → `$F1` after something on the order of 60 ms, and nothing else answered at
   all.[63][64][72]
3. **A command sink** that consumes parameter counts so that the six mouse writes of a desktop boot do not
   desynchronise the stream (`$12`, `$08`, `$0B xx xx`, `$0F`/`$10`, `$07 xx`).[63][67]
4. **One scancode source**: a make byte and its `|$80` break, in the matrix's own numbering — `$1C` for Return,
   `$39` for Space, `$3B`–`$44` for F1–F10 — which is enough to drive the desktop's menus and a shell.[63][72][66]
5. **One mouse source**: 3-byte relative records with the button bits in the header's low two bits and the
   default Y origin at the top, produced from whatever the host's pointer does.[63][66]

`derived` **And the two things it must not do**: synthesise key repeats (TOS owns them, §6.5), and queue
without limit (the ring drops whole records, §7.5). A model that gets either wrong passes every boot test and
fails the first program that looks at the timing.

---

## 9. Conflicts adjudicated

**9.1 The frame: 8 data bits, one stop, no parity — not "7 data bits, even parity".**
`documented` EmuTOS programs `ACIA_D8N1S` explicitly,[65] the 6850's divisor field gives ÷64 on the 500 kHz
tap,[40] and the IKBD's `RMCR = $05` selects `E/128` on a 1 MHz E clock.[69][72] `derived` A 7-bit even-parity
frame would put one data bit's worth of rate somewhere else and cannot produce 7 812.5 from either divider;
the page's row was simply from some other protocol. **Corrected in place.**

**9.2 The packet format: no envelope — a header byte and a length implied by the header.**
`documented` There is no `$FF` preamble, no length byte and no checksum: the record's shape is the header's
value, and the ST's parser is the ten-entry table.[63][66] `derived` A checksum is impossible to reconcile with
the guide's own sentence that a status report is padded with zeros that are "treated as NOPs" — a checksum
would have to cover them. **Corrected in place.**

**9.3 The command table: Atari's, not `$01`/`$10`/`$20`/`$30`/`$50`/`$52`.**
`documented` `$01` is not a command at all (it is the parameter of `$80`), `$10` is "Y=0 at top", `$20` is
memory load, `$30`/`$31` do not exist, `$50`/`$51`/`$52` do not exist, and the mouse's enable/disable pair is
`$08` and `$12`.[63][64] **Corrected in place**, replaced by §4.1's table.

**9.4 The matrix: 8 × 15, not 8 × 7.** See §6.1 — the register widths and the 120-byte table agree, and the
page's own key count contradicts its own shape.[71][72]

**9.5 Make/break polarity: make is plain, break has bit 7.** See §6.4.[63]

**9.6 The buffer depths: 7 usable in, 21 cells / 20 usable out.** See §7.5 — a 2026-11-28 reading of the ROM's
own counters and comparisons, and the one place where Hatari's numbers are right in effect and wrong in
size.[72][64]

**9.7 The version byte and the order of the stuck-key breaks.** `documented` The guide says the IKBD sends
`$F0`/`$F1` and *then* scans for stuck keys,[63] and the ROM writes the break codes *before* the version
byte.[72] `documented` EmuTOS's source records the resolution from a real machine: with Ctrl+Alt+Del held, the
IKBD sends `$9D`, `$F1`, `$1D`, `$38`, `$53` "within about 100msec of the reset command (info courtesy
Christian Zietz)".[65] `derived` The two accounts reconcile as *only the held keys' breaks precede the
version*, and the guide's ordering describes the empty case; a model that must choose should emit breaks
first, because that is the order a boot that holds a key observes[65].

**9.8 "40 Hz sampling".** See §7.4: not in the source, and impossible at 200 counts/inch and 10 in/s unless
records accumulate — which they do.[63] Corrected in place in the brief.

**9.9 The part's own figures.** `documented` The page's table says 48 bytes of RAM and 1 KB of ROM; the data
book says **4 kB ROM and 128 bytes of RAM**, and the die's map agrees — `$0080`–`$00FF` is RAM, `$F000`–`$FFFF`
is the 4 kB ROM the disassembly walks.[69][71][72] `derived` The crystal is 4 MHz and the internal machine
cycle is E at 1 MHz, so "4 MHz (internal)" is right about the crystal and wrong about the machine cycle[69].
**Corrected in place.**

---

## 10. What stays unknown, and where it lives

The brief predicted three rows. Four are here, and only two of them are still open: K2 turned out to be
arithmetic rather than a measurement, and K4's four cases were already published. All four are in
`design/emulator-limitations.md` §7 rather than in tickets:

| # | question | state | the one thing that would close it |
|---|---|---|---|
| K1 | The exact matrix scan period — one pass over fifteen columns | **`unknown`**, bounded | a cycle count of every loop path, or a scope on `DR1`; the bound is 0.7–5 ms a pass (§6.2) |
| K2 | Whether the ring ever holds 21 bytes | **`derived`**: no — the `>=` store test caps it at 20, and at 18 for three-byte reports | nothing; the ROM settles it, and the sentence is the finding |
| K3 | What an unplugged keyboard's floating `RXD` reads as | **`unknown`** | pull the cable mid-session on a real machine: `$FF` (a break) or noise, and whether the ACIA reports `OVRN`, `FE` or silence |
| K4 | The reset window's exact length, and which commands the four known cases generalise to | **`measured-elsewhere`** for the four, `unknown` for the rule | Hatari's four pairs are T3; a real machine with a timer between the `$80 $01` and the second command |

`derived` **K2 is the row worth reading twice**, because it is the one the brief expected to be a measurement
and turned out to be arithmetic: "8 in, 20 out" was three T3 numbers, and the ROM's own comparisons make them
7 and 20-with-a-21-byte-array. That is the campaign's rule about verifying a model's constants against the
part, applied to a firmware instead of a die.

---

## 11. The experiments that would settle the rest

| # | experiment | what it settles | cost |
|---|---|---|---|
| 1 | a TOS program that sets the ACIA divisor, reads `$FFFC02` on `RDRF` and stamps every byte[40] | every packet shape and the cadence, directly, including the reset ACK's delay | one afternoon on a real machine |
| 2 | hold both buttons, spin the mouse for five seconds while reading as slowly as possible[63] | the ring's real depth and its drop rule, and whether a lost record costs one record or three (§7.5) | the same program, one more run |
| 3 | `$80 $01` with a timestamp, then `$16`, `$1C`, `$8B`[63] | the ACK's latency and the reply latencies against Hatari's 7 000–10 000 cycles[64] | minutes |
| 4 | pull the cable mid-session[63] | K3, and whether the ST's ACIA reports the floating line | minutes, destructive to nothing |

---

## 12. Licence position

`documented` No ROM image is vendored by this study and none is reproduced: the 4 kB image sits in the tree
only as part of the Steem SSE distribution, and this study cites the *disassembly* by file and address rather
than quoting bytes in bulk.[70][72] Atari's protocol text is quoted in sentences;[63] the Hitachi data book is
quoted in one table's arithmetic;[69] EmuTOS's files are cited by name and Hatari's by file and line.[64][65]

`derived` **No sheet set, and no figure**, and the reason is the same as the standard-parts study's: every
figure this study would draw — the turn-taking walk, the command table, the matrix, the timing budget — is a
numbered list or a table in the source, and the one candidate (a diagram of the two rings and the two
overrun rules) is §7.5's table plus six sentences.

---

## Sources

[35] https://github.com/neost/neost — NeoST — the third model (src/io/Ikbd.cpp and its per-behaviour tests)
[40] http://www.sprow.co.uk/bbc/hardware/extraserial/6850datasheet.pdf — Motorola — MC6850/MC68A50/MC68B50 data sheet
[41] https://jsobola.atari8.info/DEREATARI/atarisch/1040ste_fsm.pdf — Atari — STe 520/1040 Computer Field Service Manual, C302481-001 Rev A, August 1991
[42] https://github.com/hatari/hatari/blob/master/src/mfp.c — Hatari — src/mfp.c (the GPIP bit-to-channel map)
[63] https://docs.kernel.org/input/devices/atarikbd.html — Atari Corp — Intelligent Keyboard (IKBD) Protocol (text reproduced by the Linux kernel docs; the tree's verbatim copy is `steemsse-V4.2.0_R10/3rdparty/doc/IKBD Atari Corp.txt`)
[64] https://github.com/hatari/hatari/blob/master/src/ikbd.c — Hatari — src/ikbd.c (the ROM analysis of the buffers, the command table with parameter counts, the measured reset and reply delays, the reset-window quirk)
[65] https://github.com/emutos/emutos/blob/master/bios/ikbd.c — EmuTOS — bios/ikbd.c (the ACIA's ÷64 and 8N1 set-up, the reset handshake and its 300 ms timeout, the key repeat's timing)
[66] https://github.com/emutos/emutos/blob/master/bios/aciavecs.S — EmuTOS — bios/aciavecs.S (the ST's packet parser: the `$F6` threshold, the ten lengths, the resynchronisation rule)
[67] https://github.com/emutos/emutos/blob/master/bios/mouse.c — EmuTOS — bios/mouse.c (the mouse-mode command sequences)
[68] https://github.com/emutos/emutos/blob/master/include/sysconf.h — EmuTOS — include/sysconf.h (`KB_INITIAL` 15, `KB_REPEAT` 2)
[69] https://bitsavers.trailing-edge.com/components/hitachi/_dataBooks/1988_U71_8-bit_Single_Chip_Microcomputer_Data_Book.pdf — Hitachi — 8-bit single-chip microcomputer data book (HD6301V1: 4 kB ROM, 128 B RAM, SCI table 6 giving `E/128` at a 4 MHz crystal, the E-clocked free-running counter); the tree's excerpt is `atari-st-hardware/docs/downloads/hitachi_hd6301_microcontroller.pdf`
[70] https://sourceforge.net/projects/steemsse/ — Steem SSE — the vendored 4.2.0 R10 distribution this tree carries
[71] steemsse-V4.2.0_R10/3rdparty/doc/IKBD%20bible.txt — Stephane Catala (Belzebub/ST Connexion) — *The IKBD bible*, ST Magazine 53–55 (1991), compiled by Tobe/MJJPROD
[72] steemsse-V4.2.0_R10/steem/doc/SSE/Atari%20ST%206301%20Rom%20disassemby%20commented.txt — Steem SSE — the commented HD6301 ROM disassembly of the Atari IKBD, with `Atari ST 6301 RAM map.txt` and `HD6301V1 on ST.txt` beside it in the same directory
