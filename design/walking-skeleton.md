# The walking skeleton — scope, build order, and what it may fake

The operational answer to ticket [#10](https://github.com/laurentprudhon/the-st/issues/10): which of the
eight virtual chips are real when the machine boots, what each stub is allowed to be, which of
[ADR 0008](../docs/adr/0008-the-schedulers-rules.md)'s rules the skeleton obeys, the order it is built
in, and what it is allowed to leave out. The gate itself is `design/validation.md` §3 and is not
restated here.

Rulings: ticket #10's session, 2026-11-11 (the owner's answers, quoted below and in the ticket's
resolution comment). Two rulings predate it and are recorded in the repo already: **the skeleton owns
the TOS packaging** (`roms/README.md`) and **M1 is deferred to
[#12](https://github.com/laurentprudhon/the-st/issues/12)** (`design/validation.md` §5).

## 0. The boot-path facts this scope rests on

Established 2026-11-11 by reading the pinned EmuTOS 1.4 source (`emutos-src-1.4.tar.gz`, SHA-256
`98977ab2…49702`, the recipe's own `SRC_SHA`). They are the reason several answers below are what they
are, so they are part of the decision and not background.

1. **The blitter probe tests for a bus error, not a value.** `bios/machine.c:303` `detect_blitter()` →
   `if (check_read_byte(BLITTER_CONFIG1)) has_blitter = 1;`, and `bios/vectors.S:1389` returns **1
   whenever the read does not fault**. So *any* decode of `$FF8A00–$FF8A3F` makes EmuTOS believe a
   blitter is fitted, and the VDI then draws through blits nothing completes.
2. **The boot probes both floppy drives through the WD1772** (`bios/floppy.c:329-330`): Force
   Interrupt, select, `RESTORE`, each waiting on the FDC interrupt for `MOTOROFF_TIMEOUT = 3 s`. The
   status register is read only if the command did **not** time out (`:423-429`).
3. **The keyboard is survivable**: `bios/ikbd.c:1041` reads with a 300 ms timeout and gives up.
4. **STE detection is register read-back and absent-hardware detection is `BERR`**
   (`bios/machine.c:112` writes 90 to `$FF820D` and reads it back; TT palette, TT MFP, SCC, VIDEL, SCU,
   MONSTER, Magnum and blitter are all `check_read_byte`). The System Control Unit's fault path is what
   makes this machine an STE instead of a TT.
5. **`$FF8800`/`$FF8802` must decode**: the floppy path writes PSG port A for drive select and motor.
6. **G6's addresses are per image**: `_nvbls` (`$454`) and `_gl_apid` (`0x0000e32a`) are common to all
   18, but `_deskstart` is `0x00e2b394` in `etos256uk.sym`, `0x00e2b4b4` in `fr`, `0x00e2b408` in
   `de`. The harness reads its probes from the booted image's own `.sym`, never from a constant —
   `design/validation.md` §3's quoted figure is the uk one, which is also the image §4 picks.

## 1. Real, stub, absent

**Ruling (owner's words):** *"Four real, three stubs, one absent: real = CPU, SystemControlUnit, Video,
TimersAndEvents (timers A/B/C, event selector, GPIP, IRQ); stub = Sound, Disk, Controllers (their
registers decode and swallow writes, and they assert nothing — no FDINT, no SREQ, no IKBD byte);
absent = Blitter (no decode at all, so the read bus-errors and EmuTOS sees no blitter)."*

| Virtual chip | In the skeleton | Its one obligation | What a run fails on if it fakes more |
|---|---|---|---|
| **CPU** | **real** | ADR 0009: one bus cycle = masked control record + subscription + stack-resolved data sample + release; no returned cycle count; three nested machines; reset is a six-cycle phase | An access list that disagrees with its M68000UM chapter-7 row fires #9's assertion (G3); a missing prefetch class shifts every instant after the first instruction |
| **SystemControlUnit** | **real** | the 8-phase counter, the owner equation, the three-deadline `nextDeadline`, the 4-word fetch buffer, `DE`/`BLANK`, the decode with `BERR`, `DTACK`, `IPL` arbitration (`max(6,4,2)`) | A decode that answers an address the machine does not have turns the machine into a TT (fact 4 above) and the boot dies in a `check_read_byte` path; a slot moved off the grid changes 4 000 group deliveries (G5) |
| **Video** | **real** | ADR 0012: two committed screens allocated at start-up, 16 pixels per iteration, one group → 1 chunk low res, palette lookup, `DE`/`BLANK` gating, border written not cleared, 4 000 groups **and** 4 000 chunk writes a frame | A stub that paints a flat colour or a pre-baked frame passes nothing: the desktop must be *drawn* through the chunk path (G5 counts the writes, G1 hashes the records) |
| **TimersAndEvents** | **real** | ADR 0013: sixteen sources, one encoder (highest pending, enabled, unmasked, none above it in service), one `IRQ` into the System Control Unit; A/B/C arithmetic on the 2.4576 MHz crystal (625/48); Timer B counts `DE`; `IPR`/`ISR`/`IER` quirks | A stub that raises a VBL or an HBL fires twice and the digest cannot see it (the System Control Unit's level is the only one); a stub that asserts an `IPL` lane takes the machine's one interrupt record away from its owner |
| **Sound** | **stub** — `$FF8800`/`$FF8802` (PSG select + data), `$FF8922`/`$FF8924` (Microwire) | decode, remember what software wrote, answer idle elsewhere, assert nothing | A stub that raises `SREQ` adds refills to G5, steals internal slots from refresh, and proves nothing about the refill rule it cannot yet implement |
| **Disk** | **stub** — `$FF8604` (FDC command/status), `$FF8606` (DMA mode), `$FF8609`/`B`/`D` (DMA address) | decode, accept commands, answer a status byte with BUSY and TRACK0 clear, assert no `FDINT`, no `DRQ`, no `RDY` | A stub that answers TRACK0 makes TOS register a drive, then wait for a boot sector no one will produce — and a stub that *did* answer a sector would pass G6 while proving nothing about the machine |
| **Controllers** | **stub** — `$FFFC00`/`$FFFC02` (ACIA), `$FF9200–$FF9217`, PSG port reads | decode, answer "transmitter empty, no received byte", assert nothing | Delivering one IKBD byte feeds TOS a fabricated keyboard version string, and a phantom key changes the desktop state with no input path behind it |
| **Blitter** | **absent** — no decode (`$FF8A00–$FF8A3F` faults) | nothing; its `BERR` is the whole of its behaviour here | Decoding its registers is the failure mode: `has_blitter = 1`, and the desktop is drawn by blits the machine cannot run |

**The stub rule (owner's words):** *"a stub owns its register file and nothing else — it decodes its
addresses, remembers what software wrote wherever software reads it back, answers idle everywhere else,
and asserts no wire."* One rule, and the address list above is its consequence, not a second policy.

### The floppy probe, and why Disk answers with silence

The boot **does** touch the WD1772 (fact 2), so "the boot does not probe" was not available and neither
was answering the interrupt: `FDINT` is `U400`'s own output (`design/board-inventory.md` §4, pin 16 —
"FDINT/HDINT → U400 → IPL chain"), `FDCS`/`FDRQ` are internal to Disk (`crossings.md` §7), and the only
wire Disk owns is `RDY`, which means "I want the bus". A stub driving `FDINT` would be an ADR 0004
two-driver conflict or a new crossing that belongs to [#29](https://github.com/laurentprudhon/the-st/issues/29).

**Ruling:** the silent stub, plus a debug witness recording that `$FF8604` was never read during the
boot — the non-timeout branch is *written down as never-taken* rather than quietly assumed to work. The
cost is 2 × 3 s of **emulated** time (≈ 300 frames, ≈ 0.3 s of wall clock), and the desktop comes up
with zero drives. The harness's default `N` is "frames to reach the desktop, then 100 more", so the
probe is absorbed automatically and `run.report` names the 6 s and the zero drives.

> **Measured — 2026-11-13 (#35).** `run.report` now reads these waits off the probe timeline instead of
> asserting them: a run of frames whose program counter stays inside one 16-byte box is the ROM
> waiting, and its length is the ROM's own `_hz_200` advanced over it. Before the desktop it finds
> **4.0 s and 2.0 s in `clock.o`'s delay loop** and **3.0 s stopped in `_stop_until_interrupt`** — the
> FDC interrupt the silent disk stub never answers — and **0.48 s in `ikbd.o`** for the keyboard. So
> the 6 s above is the two delay loops alone, the FDC-interrupt wait is a further 3 s, and fact 3's
> 300 ms timeout reads 480 ms in the model. `_drvbits` is `00` at the desktop: zero drives, measured.

## 2. ADR 0008's seven rules

All seven are obeyed **as written**; the skeleton bends none of them, because each is an invariant the
gate measures rather than a convenience it could trade away.

| # | Rule (ADR 0008 §Invariants) | In the skeleton |
|---|---|---|
| 1 | Order is `(instant, seq)`; nothing else participates | the queue's only key; assertion 1 (head non-decreasing) is on from instant 0 |
| 2 | After `Write` the next line runs at the chip's clock; after `WriteAndWait` at the record's instant | the two shapes exist from the first commit; the CPU's bus cycle is the call site that proves both |
| 3 | Every coroutine iteration suspends at least once; the clock runs ahead only to the next read or write | assertion 6 (writes per resume) — cap **64** records, derived as 2.7× the reset sequence's 24 |
| 4 | A record is never coalesced; a read never returns a value a later record would change | the 16 000 `MD` records stay 16 000; no tick table, no batching |
| 5 | An obligation is a pending read; a suspended chip owns a queued resume, a subscription or an awaited write | assertion 2 (`suspendedChips == pendingReads + pendingWriteAndWaits`) and assertion 3 (`nextDeadline`) |
| 6 | Every observable state change is a logged record; the digest is a fold over records | the digest stream is a deliverable (§4), debug-only, never in release |
| 7 | The leaf substitutes pops, never a clock movement, a record, or another chip's subscription | **not exercised**: the leaf is off and stays off. Its falsifier is this ticket's own measurement — 50 Hz with 2× headroom in release, G8 §3 |

Three things the skeleton sets *around* those rules, stated so a later session does not read them as
bends:

- **Retention is unbounded.** A run is minutes long and G7 needs a live sampler; the watermark is
  computed and reported but no log is truncated.
- **The three stubs log no anchors.** A register file has no counter to anchor; `nextDeadline` is
  declared on them as ADR 0008 §5 requires and stays `null`, which is the field being unused, not the
  rule being bent.
- **The write cap is a constant, not a measurement.** 64; a chip that needs more has a loop with no
  suspension in it, which is what the assertion exists to find.

## 3. Build order

Three commits, ending at the harness contract in `design/validation.md` §2. One solution
`TheST.sln` at the root, two projects: `src/TheST.Machine/` (the machine alone — scheduler, five verbs,
eight chips, stores; no Avalonia, no host clock) and `tools/validation-harness/` (a console exe with
`run`, `--inject` and `check-clocks` subcommands). **No test framework and no NuGet package** — G4 is a
harness subcommand — because CI is offline by ADR 0010. `design/stores-sketch.cs` is lifted verbatim;
`design/cpu-core-sketch.cs` is the shape, not the code.

| Commit | Delivers | Gate rows it turns green |
|---|---|---|
| **1 — the loop** | solution + `src/TheST.Machine`: tick constants and the four dividers (integer ticks), the queue keyed `(instant, seq)`, the five verbs, the digest fold over a batch, assertions 1/2/6/7, `Ram`/`Rom` from the sketch, and one chip driven in a synthetic trace | **G4** (`check-clocks`), **G1/G2** on the synthetic trace |
| **2 — the boot** | the SystemControlUnit (counter, owner equation, fetch buffer, `DE`/`BLANK`, decode + `BERR`, `DTACK`, `IPL`), the CPU core per ADR 0009, Video's screen + chunk write path, TimersAndEvents (A/B/C, event selector, `IRQ`), the three stubs; first boot to the desktop | **G3**, **G5**, **G6**; G1/G2 on the real trace |
| **3 — the contract** | `tools/validation-harness/` subcommands, `runs/<stamp>/run.{digest,state,report}`, `scripts/validate.sh` (Debug for G1–G7, Release for G8), G7's retention test, `run.report`'s reported-never-gated list | **G7**, **G8**; the whole run green |

**Expected green at the end of commit 3: G1–G8, both build configurations.** M1 is not in the skeleton —
it is [#12](https://github.com/laurentprudhon/the-st/issues/12)'s, and until #12 lands a green run means
the model is self-consistent and runs an OS to its desktop, never that a pixel reached a window.

## 4. The harness contract

`design/validation.md` §2 is authoritative and nothing here changes it; the skeleton's additions are the
subcommand shape the rulings require:

- `validate.sh` runs, in order: `check-clocks` (G4), `run --lang uk --frames N` twice and `cmp` (G1),
  `run --inject` (G2), the Debug run's assertion sweep (G3), the rate report (G5), the `.sym` probes
  (G6), the retention test (G7), and the Release run's timing (G8).
- `runs/<stamp>/` holds `run.digest` (one hex word per batch), `run.state` (probe values: `_nvbls`,
  `_gl_apid`, PC, committed registers) and `run.report` (one page).
- `run.report` carries the §4 "reported, never gated" list — fast-path share, suspensions and awaited
  writes per frame, allocation and GC, absolute class-1 wakes against ≈ 4 734, `_frclock`, digest
  length — plus this ticket's two instrumentation facts: **the boot's 6 s of emulated floppy probe** and
  **zero drives**.
- The exit code is the verdict; frames are never compared; sound's refill row stays deferred (D2).

## 5. What the skeleton may leave out

**Ruling (owner's words):** *"the same rule, stated but without per-item dispositions: the design
document names the four items as deferred, and the build session decides each one's treatment as it
writes the code."*

The rule:

> **The skeleton may leave out what only changes the payload — a pixel's value, a sample, a sector's
> bytes — and may never leave out what decides an instant, a record, a wake or a bus answer.**

The four catalogue items that sit in the deferred set, named here so the build session decides each one
rather than assuming:

| Deferred item | Why it is payload, not timing | Reversal |
|---|---|---|
| the System Control Unit's second `$FF8260` copy | it selects the Shifter's mode; the skeleton runs low res, so the value changes no instant | [#12](https://github.com/laurentprudhon/the-st/issues/12)/[#27](https://github.com/laurentprudhon/the-st/issues/27) own the modes; the register still decodes |
| the Microwire link (`$FF8922`/`$FF8924` → LMC1992) | the volume/bass/treble registers change a sample, not an instant | [#26](https://github.com/laurentprudhon/the-st/issues/26) |
| `GPIP`'s tied lines | they are constant levels no source drives; a constant changes no instant | [#21](https://github.com/laurentprudhon/the-st/issues/21) / [#28](https://github.com/laurentprudhon/the-st/issues/28) revisit if an input path lands |
| the USART | ADR 0013 already declares it a stub; RS-232 is out of scope on the map | a serial ticket gives it a producer |

Also payload by the same rule: the mono and medium-res screens, fine scroll and hscroll, border tricks,
PSG tone/noise/envelope synthesis, the drive model and its sector cadence, the paddle ADC, the joystick
and joypad ports.

**Never omitted**, whatever it costs: the phase counter and the owner equation, the decode's `BERR`
path, `DTACK` answering, the three-counter `nextDeadline`, the `IPL` arbitration, VBL 4 and HBL 2 on all
313 lines, Video's chunk write path with palette and `DE`/`BLANK` gating, the MFP's A/B/C and event
selector, the seven scheduler assertions, the two CPU assertions, and the per-instant digest.

## 6. Carrying down

- **#12** inherits the machine-side display: a committed screen (two buffers, `0RGB`, borders written),
  the three host-facing notifications (not wires), and M1.
- **#21** inherits "the skeleton is not typed on": the ACIA decodes and answers idle, no IKBD byte ever
  arrives, and TOS gives up after 300 ms. The catalogue's §8 note ("the walking skeleton has to be typed
  on") is corrected to point here.
- **#26** inherits a Sound register file with no FIFO, no `SREQ` and no `nextDeadline` expiry; the
  refill row (≤ 313/frame) stays D2.
- **#29** inherits a silent FDC: registers decode, commands are accepted, no interrupt, no drive, and
  the non-timeout status branch recorded as never-taken. The `Disk → SystemControlUnit` completion
  crossing, if it is ever needed, is #29's to mint.
- **#30** inherits absence: `$FF8A00–$FF8A3F` faults, and EmuTOS therefore never uses the blitter.

## 7. Open points

1. **The write cap (64) is derived, not measured** — from the reset sequence's 24 records. The build
   session confirms it on the first green boot and records the observed maximum.
2. **The 6 s floppy probe is a boot-time cost the gate does not measure.** It is reported; if a later
   ticket makes it bother a human, the fix is a crossing (#29), not a stub.
3. **G6's `_deskstart` is per image.** Any language switch in a later run must re-read the `.sym`; a
   hardcoded `0x00e2b394` would silently probe the wrong text.
4. **No pixel evidence exists until #12.** The gate is self-consistency plus the OS's own clocks; §1 of
   `design/validation.md` is the standing warning.
