# Partition audit: is every part and every register assigned exactly once?

Validation of `design/subsystems-spec.md` (eight virtual chips) against the inventory's complete part
list (`design/board-inventory.md` §2) and against every address Hatari's STE dispatch table maps
(`hatari/src/ioMemTabSTE.c`). Method: enumerate, then require each item to be **assigned to exactly one
chip**, **explicitly excluded with a reason**, or **flagged unassigned**. Anything that falls in the third
bucket is a hole in the partition, not a detail.

> ### Correction — 2026-11-11 (ticket #31, owner's ruling)
>
> The virtual chip this file calls *Memory* is the **System Control Unit**, and the DRAM and the TOS
> ROMs are no longer members with pins: they are plain data structures (`Ram`, `Rom`) inside it —
> stores with no timeline, no record, no subscription and no wake. Every statement below about the
> counter, the owner equation, the clients, the beam, the buffer or the leaf stands unchanged; only
> the name and the store boundary moved. Owner's words: *"GSTMCU is too cryptic when you want to
> understand the machine, what about keeping only three letters of the name : SystemControlUnit ?"*
> See [ADR 0014](../docs/adr/0014-the-stores-are-data-structures.md) and `design/system-control-unit.md`.

## 1. Physical parts

| Part | Reference | Status |
|---|---|---|
| 68000 | U100 | **CPU** |
| GSTMCU | U400 | **split**: System Control Unit (memory control, counters, sync, decode, `$FF8260` copy) + Controllers (pad/pen ADC, port logic) |
| GST Shifter | U401 | **Video** |
| DMA controller | U300 | **Disk** (whole) |
| ST Blitter | U101 | **Blitter** |
| TOS ROM high/low | U102, U103 | **UNASSIGNED — finding 1** |
| YM2149 | U202 | **split**: Sound (PSG) + Controllers (I/O ports) |
| MFP 68901 | U203 | **Timers and interrupts** |
| 6850 ACIA ×2 | U200, U201 | **U201 → Controllers** (keyboard, 7812.5 baud); **U200 → out of scope** (MIDI). Resolved from the service manual's keyboard procedure; see `research/schematic-evidence.md` §4 |
| MIDI opto-coupler | U208 | excluded, out of scope |
| WD1772 | U301 | **Disk** |
| LMC1992 | U508 | **Sound** |
| DAC0802 ×2 | U502, U503 | excluded — analog tail |
| LF347 ×2 | U504, U507 | excluded — analog tail |
| MF4-100 ×2 | U505, U506 | excluded — analog tail |
| MC1377 encoder | U403 | excluded — analog tail |
| 74S257 mux | U405 | excluded — analog/glue between shifter and encoder |
| 555 timer | U104 | **UNASSIGNED — finding 3** |
| LM556 ×2 | U513, U514 | paddle half → **Controllers** (documented: the pulse width is measured by the GSTMCU); floppy/ACSI half → Disk **probable, not documented** |
| TL497A regulators | U204, U205 | excluded — power, not modelled |
| 7406 open-collector ×2 | U210, U305 | excluded — drive stage |
| 74LS86, 74LS04 | U209, U211 | excluded — decode/combine glue |
| 74LS74 ×2 | U304, U306 | excluded — DE/LOAD pipeline |
| 74LS164 | U900 | **Controllers** (IKBD serialiser) |
| 74LS244 ×5, 74LS245 ×2 | U307, U509, U510, U512, U632, U302 | excluded — buffers |
| 74LS373, 74F374 ×2 | U511, U500, U501 | excluded — palette latch and pixel pipeline |
| DRAM ×32 (16 per bank, 2 banks), SIMM sockets ×4 | C0101743-002, U600–U603 | the `Ram` **store** inside the System Control Unit — a data structure with no pins leaving the chip, not a member (ADR 0014) |
| Crystals and oscillators | Y200, U303, U402 | excluded — phase/clock sources, no state |
| Resistor packs | P100–P105, P500–P502, P600 | excluded |
| Reset switch | S100 | excluded — host input |

**Result: 27 of 29 active packages assigned or excluded with a reason; two remain unresolved** (the 555,
and the second LM556 half). The suspected missing RTC was **withdrawn** — this machine has none
(`research/schematic-evidence.md` §6).

## 2. Registers

Every address the STE dispatch table maps, by owner. Repeats are marked.

| Range | Owner | Note |
|---|---|---|
| `$FF8001` | System Control Unit | MMU config |
| `$FF8201`, `$FF8203`, `$FF8205`, `$FF8207`, `$FF8209`, `$FF820D`, `$FF820F` | System Control Unit | video base, counter, line width |
| `$FF820A` | System Control Unit | sync/frequency (article: the sync register is in the GSTMCU, not the Shifter) |
| `$FF8200`, `02`, `04`, `06`, `08`, `0B`, `0C`, `0E` | — | void addresses in Hatari; no owner needed |
| `$FF8240–$FF825F` | Video | palette (16 words) |
| `$FF8260` | **Video *and* System Control Unit** | two physical registers, one per chip — see finding 5 |
| `$FF8261` | Video | Shifter-only resolution bits |
| `$FF8263` | Video? | Hatari reads it in the horizontal-scroll path; **uncertain**, one line of evidence only |
| `$FF8264`, `$FF8265` | Video | fine scroll |
| `$FF8604`, `$FF8606`, `$FF8609`, `$FF860B`, `$FF860D` | Disk | FDC and DMA |
| `$FF8800`, `$FF8802` | Sound | PSG |
| `$FF8801`, `$FF8803` | Controllers | PSG port reads (parallel port / ports A-B) |
| `$FF8900`, `$FF8903`–`$FF8913`, `$FF8921` | System Control Unit | sound DMA control, pointers, mode |
| `$FF8922`, `$FF8924` | Video | Microwire data/mask — physically in the Shifter |
| `$FF8A00–$FF8A3E` | Blitter | whole block |
| `$FF9200`, `$FF9202`, `$FF9211`–`$FF9217`, `$FF9220`, `$FF9222` | Controllers | joypad, analog pads, light pen |
| `$FFFA01`–`$FFFA2F` | Timers and interrupts | whole MFP block |
| `$FFFC00`, `$FFFC02` | Controllers | keyboard ACIA |
| `$FFFC04`, `$FFFC06` | out of scope | MIDI ACIA |

**Result: no register is orphaned.** One is duplicated by the hardware itself (`$FF8260`), one is
uncertain (`$FF8263`), and the rest sit in exactly one chip.

## 3. Findings

### Finding 1 — the TOS ROMs are assigned to nobody

`U102`/`U103` (2 × 27C010, 256 KB of TOS 1.62) are real devices on the CPU bus, selected by
`ROM0–ROM6` from the GSTMCU's decode. They are the only source of the operating system the walking
skeleton must boot, and they are currently outside the partition.

**Recommendation: a store inside the System Control Unit — `Rom`.** Not because they behave like RAM,
but because they are addressed by the System Control Unit's decode and sit inside the same memory map,
and because they hold no timeline of their own — a read-only store is memory service like DRAM. The
alternative — a ninth chip called "ROM" — would be a chip whose only function is to return the bytes at
an address, which is a data structure, not a subsystem.

> **Correction 2026-11-11** (ticket #31). This finding recommended "ROM → Memory" and stopped one step
> short: `Rom` is not a member of the chip but a **store** it holds, beside `Ram` (ADR 0014,
> `design/system-control-unit.md` §2). The argument above is the one that finished it — kept verbatim
> because it is the reason the split exists.

### Finding 2 — the LM556s are unassigned, and the inventory cannot place them

`U513`/`U514` are dual timers appearing on sheet 3 (disk/ACSI region) and on sheet 5 (audio/controller
region); the inventory (§6.12) could not decide which half serves what.

**Partially resolved (2026-11-01).** The service manual's paddle test settles one half: "This current
charges the RC network on the STE, varying the output pulse of the LM556. The pulse width is measured by
the GSTMCU" — so the paddle/mouse half belongs to **Controllers** (`research/schematic-evidence.md` §5).
The remaining half most likely serves floppy/ACSI timing and would belong to **Disk**; that stays
**probable, not documented**, and it is the only LM556 question still open.

### Finding 3 — the 555 reset timer is unassigned

`U104` generates the power-on reset delay feeding the whole board.

**Recommendation: exclude it from the partition and treat reset as a host event.** A 555 timer with an RC
delay models nothing the emulator needs: machine time starts at zero on reset, and reset distribution is
a single edge. If it ever needs modelling (e.g. for a reset that must race something), it becomes a
System-Control-Unit-adjacent detail, not a chip.

### Finding 4 — WITHDRAWN: this machine has no real-time clock

**Withdrawn 2026-11-01.** The parts list contains no clock chip and no battery, and the machine's own
documentation never mentions one. The RTC belongs to the Mega ST, Mega STE and TT (the Mega STE's is a
Tadiran TL-5242/W). TOS returning a default date on this machine is correct behaviour, not a missing
part. See `research/schematic-evidence.md` §6.

### Finding 5 — `$FF8260` is genuinely duplicated, so one register has two owners

The STE article states there are two resolution registers at that address, one in the Shifter and one in
the GSTMCU, and Hatari's handler names agree (`Video_ResGlueShifter_*` alongside `Video_ResShifter_*` at
`$FF8261`). This is the only place in the machine where a single address has two owners, and it is a
property of the hardware, not of the partition. Both chips hold a copy; a write reaches both.

## 4. Does the split still hold?

Yes, on three tests:

1. **Every chip has one timeline.** CPU executes; the System Control Unit schedules DRAM and keeps the beam; Video
   shifts the words into pixels; Sound counts PSG tones; Disk turns a platter; Timers count down;
   Controllers sample devices; Blitter runs a blit. No chip owns two clocks.
2. **The two-DMA rule is applied without exception.** Continuous streams (video fetch, sound DMA) are in
   the System Control Unit with their counters; bursts (disk DMA, Blitter) are in the chips that produce them and negotiate
   the CPU bus.
3. **No register contradicts its owner's role.** The one oddity — the Microwire controller in the Shifter
   driving the LMC1992 in Sound — is a hardware fact, and it does not give the Shifter a sound timeline:
   it shifts bytes on request, it does not decide when.

**What is not yet validated:** the per-line slot allocation (`research/memory-bus-slots.md`, in progress),
and the pin-level truth of `SREQ`/`SLOAD`/`DCYC` (inventory §6.14).

> ### Correction — 2026-11-09 (ticket #20)
>
> `research/memory-bus-slots.md` is **complete**, not in progress (`research/gstmcu-reconstructions.md`
> re-checked its load-bearing claims against the GSTMCU RTL on 2026-11-01, and ticket #20 re-read the
> internal-owner enables on 2026-11-09). The sentence above is kept for the audit trail; the open item
> it names has moved into that note's §7, where only the refresh period, the sound FIFO depth, the
> blitter/disk slot occupation and the STE internal share remain unknown. The partition itself is
> unaffected — these are behavioural questions, never topological ones. The pin-level question stands.


## Revision — 2026-11-01

Schematic and service-manual evidence (`research/schematic-evidence.md`) resolved the ACIA identity and one LM556 half, withdrew the suspected RTC, and confirmed that `DCYC` fans out to the Shifter. The partition itself did not change: the open items are behavioural (slot placement), not topological.
