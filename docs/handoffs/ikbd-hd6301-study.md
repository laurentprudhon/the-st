# Handoff — the IKBD (HD6301V1): the last chip, and the only one that is behaviour rather than silicon

**Goal of the next session.** Write `research/ikbd-implementation.md`: the command set the ST sends, the events
it gets back, the matrix and the ports that produce them, the 7812.5 baud link that carries them, and what the
model must own on both sides of that link. This is the last entry of the per-chip campaign and the only one with
**no silicon research in it at all**: the part is a 6801-family microcontroller running a mask ROM nobody has
published, so the study is a protocol study — the interface, not the die.

Read first, in this order: `docs/handoffs/silicon-research-per-chip.md` §4's IKBD entry and §6's stopping rule;
`research/standard-parts-implementation.md` §5 (the 6850's side of this link — the divisors, the one-byte buffer,
`OVRRUN`, `CTS`); `design/subsystems-spec.md` §1's Controllers row; `design/virtual-chips.md` §7;
`design/board-inventory.md` §3.10 and §4.6; `atari-st-hardware/components/02-ikbd-keyboard.md` (read it, then
distrust it: §1 below lists three of its claims that are wrong); and `hatari/src/ikbd.c`'s header, which is the
densest single page of measured ST-link behaviour in the tree.

---

## 1. What is already established, so the session does not re-derive it

* **The link is the keyboard ACIA's, and its arithmetic is settled.** `U201` is the keyboard ACIA (Atari's own
  diagnostic: "verify that pin 3 of `U201` is 500 KHz ±50 KHz"), clocked from `KHZ500` and divided by 64, so
  **7 812.5 baud, 8 data bits, 1 stop, no parity** — 128 µs a bit, 1.28 ms a ten-bit character.
  `research/standard-parts-implementation.md` §5.2–§5.3 has the datasheet's sentences and Hatari's constants (the 6850's data sheet is the campaign's source 40).
* **The ACIA's other end is a one-byte buffer with an overrun flag.** A byte the ST does not collect within a
  character time is **lost with `OVRN` set** — the link does not stall, and `CTS` affects only the transmitter's
  status bit. That is the answer to "what does the ACIA do when the IKBD is busy" at the chip level; what the
  *IKBD* does when the ST is busy is this session's question.
* **The IKBD is not on the motherboard.** It sits inside the keyboard case, wired through a flat cable; the ST
  side is three wires plus power. `U900` is **not** its serialiser — this week's session established that the one
  74LS164 is the `SINT`→`TAI` delay — so the link is `TXD`/`RXD` between `U201` and the HD6301, nothing else.
* **The interrupt path is `N6850` → `GPIP4` → channel 6**, shared with the MIDI ACIA: the handler reads both
  ACIAs' status registers before it knows whose byte it has. `design/timers-and-events.md` §7's table has the
  bit-to-channel map (GPIP4 is channel 6, not 4).
* **There is no reset line from the ST to the IKBD.** The ST's ACIA is reset by writing `$FFFC00` (the 6850 has
  no reset pin); the IKBD's own reset is the protocol's `$80` command.
* **The emulators are a checklist, and Hatari's is unusually detailed.** Its `ikbd.c` (3 258 lines) opens with a
  ROM analysis rather than a pin table: the serial delays in CPU cycles (`1 bit at 7812.5 baud = 1024 cpu cycles
  at 8 MHz`); an **input buffer of 8 bytes at `$CD`–`$D4`** whose overflow **drops the new bytes**; an **output
  ring of 20 bytes at `$D9`–`$ED`** that drops a whole report it cannot fit; unknown commands treated as **NOPs**;
  and a reset-window quirk where `$08 $14`, `$08 $0B $14`, `$12 $14`, `$12 $1A` received *during* reset put the
  IKBD in a mode reporting mouse and joystick together (Hatari names Barbarian 1 and Hammerfist as users). The
  full command table with parameter counts follows. NeoST and Steem SSE are the second and third models; none is
  a witness — T3 by definition — but this is the densest single page of ST-link behaviour in the tree.
* **The repo's own page is wrong in at least three places**, and this session found them: it gives the link as
  "7 data bits, even parity"; it describes IKBD packets as `$FF` + length + data + checksum; and its command
  table (`$01`/`$10`/`$20`/`$30`/`$50`/`$52`) is not the ST's command set. Hatari's constants contradict the
  first two outright and the third needs the vendor's guide. **Land a dated correction in place**, per the
  campaign's rule that a stale page beside a correction is what the next reader copies.

## 2. Acceptance criteria

A reader who has the study must be able to do these without opening another document:

1. **Send a command and say what comes back**: the full command set the ST actually writes (`$07`, `$09`,
   `$0B`, `$0C`–`$0F`, `$10`–`$14`, `$18`–`$22`, `$80`, `$81`, `$85`, `$87`, `$88`, the `$8x` reset/status
   family), which are acknowledged, which are silent, and what each one changes in the IKBD's own state.
2. **Draw one key event and one matrix scan**: the scan matrix's row and column counts as the keyboard's own
   cable carries them, the debounce, the make/break encoding (and which way round it is — the repo's page says
   one thing and the emulators another), and the auto-repeat's timing.
3. **Draw one mouse packet and one joystick packet as bytes**, naming the byte that moves, the byte that
   carries the buttons, the sign convention of the deltas (up the screen is negative — check it), and what
   generates them: two quadrature counters and a fire line, versus the joystick's five switches.
4. **State the link's timing budget**: 1.28 ms a character, the ACIA's one-byte buffer, the IKBD's own transmit
   pacing, and therefore how long a burst of mouse packets takes to arrive after the movement that caused it.
5. **Say what happens when either end is busy**, in bytes and in milliseconds, on both sides — the `OVRN` case
   (ST slow) and the IKBD's queue (IKBD slow, which is the case the ST's TOS documents as "lost keyboard").
6. **Say what the model must own** and what it may stub: the Controllers stub's current contract (status
   `0x02`, no byte ever), and the minimum a model needs to type at the desktop — one scancode path, one mouse
   path, and the two commands TOS sends at boot.
7. **Compare the record with the model, claim by claim** — `design/cpu-silicon-todo.md`'s shape, with
   `src/TheST.Machine/Chips/Stubs.cs`'s `Controllers` and `design/virtual-chips.md` §7 named per row.

## 3. The evidence, and what each layer can answer

| layer | what it gives | what it cannot |
|---|---|---|
| Atari's IKBD programmer's guide (dev-docs; the campaign has used that archive already) | the command set, the packet formats, the matrix, the timing — the *only* place all four are written down | the firmware's quirks: what it does on a full queue, on a bad command, on a power glitch |
| the reverse-engineered firmware (dumps and disassemblies circulate) | the actual behaviour, including the quirks the guide omits | licensing: read it for facts, do not vendoring it |
| the Hitachi HD6301V1 datasheet (already in the tree: `atari-st-hardware/docs/downloads/hitachi_hd6301_microcontroller.pdf`) | the SCI's baud arithmetic from the IKBD's own clock, the port and timer resources, the interrupt structure | anything Atari programmed into it |
| the ST side: `U201`, `N6850`, `GPIP4`, TOS's own keyboard code (EmuTOS's `ikbd.c` is in the tree as source) | what the machine sends and expects, and the boot sequence | the IKBD's side |
| Hatari `ikbd.c`, NeoST, Steem SSE | a checklist of quirks with measured numbers (delays, queue depth, packet bytes) | evidence — T3 by definition |
| our model — `Controllers` (a stub, status `0x02`) | every place the study lands as a task rather than as prose | nothing about the part |

## 4. Method rules inherited, plus one

Inherited: every claim labelled `documented`/`derived`/`inferred`/`unknown` with `T1`/`T2`/`T3`; **names lie**;
**check arithmetic against the part**; **the vendor's prose beats every model**; a source in a list is not a
source read; **search before writing `unknown`**; no tickets for what only a board can settle.

New, from this session: **a protocol is not a datatsheet — read the *sequence*, not the table.** The one thing
that will decide most of this study is not "which byte is which command" but *what order the ST and the IKBD
take turns in*: who may speak first, how long a receiver may wait, and what the sender does when the other end
has said nothing. Write the turn-taking as one numbered walk (the standard-parts study's §3.3 is the shape)
before writing the command table, and the table will then have one row per state rather than one per mnemonic.

## 5. Seeds to check, not to copy

* **7 812.5 baud, 8N1** — from the ACIA's ÷64 on 500 kHz. Check the IKBD's side divides its own clock to the same
  rate; the guide's number and the ACIA's must agree to the bit, or one of the two is mis-set.
* **128 µs a bit, 1.28 ms a character** — and therefore a mouse report at a 40 Hz sampling rate costs ~12 % of
  the link. If a packet is longer than three bytes, the sampling rate cannot be what the guide says.
* **The mouse's header is `$F8` (motion) or `$F9` (buttons)** and the packet is three bytes; the joystick's is
  `$FE` and it is two. Both are Hatari's constants; the guide is the arbiter.
* **Make/break polarity** — the repo says make = `$80 + n`, break = `n`. Verify with the guide *and* with TOS:
  a wrong polarity produces a keyboard that works upside down, which is a bug the desktop would hide.
* **The scan matrix** — 8 rows × 7 columns on the ST's keyboard is the page's claim; the guide and the schematic
  are the arbiters, and the number matters because it decides the scancode space.
* **The command ACK** — the ST's `$80`/`$81` family answers with a byte (`$F1`?), and TOS waits for it at boot.
  If the model never answers, the machine still boots (it times out) — which is why the stub "works" and why the
  study must not read the stub as evidence.
* **The buffer depths are already answered, at T3**: 8 bytes in, 20 out, both dropping on overflow. Treat these as
  the numbers to *verify* (against the guide and a ROM dump) rather than to discover — but keep the drop rule,
  because it is the mechanism a "busy IKBD" claim rests on, and a model that queues instead of dropping is wrong
  in a way no test will notice until the mouse is spun.

## 6. What must stay `unknown` until measured

* Whether 8/20 is the real depth or Hatari's simplification of it, and what exactly is dropped (the newest bytes,
  or the whole report): the ROM analysis says the former for input and the latter for output, which are two
  different rules. This is the one row a flood test settles, and it belongs in `design/emulator-limitations.md`.
* The IKBD's exact sampling and transmit cadence (the "40 Hz" figure) — measurable from the same program.
* Whether an unplugged keyboard's floating `RXD` reads as `$FF` (a break condition) or as noise.

## 7. The experiments that would settle the rest

1. **Raw byte dump.** A TOS program that sets `$FFFC00` to the keyboard divisor, reads `$FFFC02` with `RDRF`,
   and prints every byte with a timestamp: it recovers the packet shapes and the cadence directly, and it costs
   one afternoon on a real machine.
2. **The flood.** Hold two mouse buttons and spin the mouse hard for five seconds while a TOS program reads the
   ACIA as slowly as it can; count what arrives and what is missing. That confirms or refutes 8/20 and the two
   drop rules, and it is the only experiment here that can fail in an interesting way.
3. **The ACK.** Send `$80`, then `$81`, and print the replies; the boot sequence's timing is in TOS, not in the
   guide.
4. **The break.** Pull the keyboard cable mid-session and see whether the link reports `OVRN`, `FE` or silence.

## 8. Definition of done

1. `research/ikbd-implementation.md` — one document, the turn-taking walk first, then the command table, the two
   packet families, the matrix, the timing budget, and the model comparison in `design/cpu-silicon-todo.md`'s
   shape; `verify --min-coverage 0.30` green.
2. The corrections landed in place, dated: `atari-st-hardware/components/02-ikbd-keyboard.md` (the three wrong
   claims of §1), `design/virtual-chips.md` §7, `design/subsystems-spec.md` §1's Controllers row, and
   `design/emulator-limitations.md` for the queue-depth row.
3. `docs/handoffs/silicon-research-per-chip.md` §4's IKBD entry marked **done** with a one-line pointer, and §6's
   ordering updated: the per-chip campaign is then **complete**.
4. No new tickets for anything a board or a firmware dump settles (owner's ruling, 2026-11-28).
5. Commits pushed to `origin main`, from a worktree, and the shared tree fast-forwarded.

## 9. What closes after this, and what comes next

**The campaign closes with this chip**, and the closing audit is the session after it: walk
`docs/handoffs/silicon-research-per-chip.md` §4 and ask of every chip whether each open question in our core has
a labelled claim or an explicit `unknown` with its experiment.

**The thread after the audit** is not silicon at all, and its brief is already written:
`docs/handoffs/disk-media-and-image-formats-study.md` — the drive, the media, the five image readers we support,
and the protection classes the deferred flux formats would have bought. Read its §1 before choosing the audit's
first move: the audit's answer decides whether the model can afford flux, and that brief is where flux is
priced.

---

*Written 2026-11-28, closing the four standard parts (U203, U301, U200/U201, U202). The ordering stays as the
per-chip plan set it: the four customs first because the record was thin and the drawings were the gold, then the
standard parts because their datasheets are, and last the IKBD because nothing about it is silicon research at
all.*
