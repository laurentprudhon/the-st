# The crossing catalogue — a subscription table

Every wire that leaves one virtual chip and can make another chip run, with the class of the signal,
the chip that drives it, the chip that subscribes to it, the resolver that turns its drivers into a
value, and its rate on the machine's timeline. Chip names are the eight from
`design/virtual-chips.md`; pin names and numbers are the inventory's (`design/board-inventory.md`
§3–§4).

Under ADR 0006 the engine has no "wake" call: a queue ordered by instant, filled by dated resumes and
by **subscriptions** — pending undated reads, fired by the writer's record. This table is the set of
subscriptions the machine is allowed to have, so it is also the bound on the queue's insertion rate.

Frame = 640 000 ticks = 20.000 ms = 312.5 lines of 2 048 ticks (512 CPU cycles), 50.000 Hz. Keepers of
counters round to 313 lines. **× 50 converts per-frame to per-second.**

## 1. The rules

**Class 1 — a crossing.** A subscription whose firing *starts new work* in the receiver: a delivery, a
request, a select that changes a chip's state, a level change, an ownership transfer. Rate set by the
writer, unbounded. These are the rows that matter.

**Class 2 — an answer inside an open exchange.** There is a subscription, but the subscriber opened the
exchange and the answering chip bounds the instant (a bus cycle, a DMA cycle, a vector fetch). It
starts no new work and can later be inlined (ADR 0005's leaf) without moving an instant.

**Class 3 — phase.** A clock is not a wire: it is arithmetic on machine time. No records, no
subscriptions, however fast.

**Data.** Address and data buses are wires with records and no class-1 subscription. A subscriber that
needs the value samples the data wire at the instant of the control record that woke it.

**Self-wake.** A chip resumed at an instant it computed itself — next DRAM slot, timer expiry, group
cadence, FIFO deadline — is not a crossing and has no row here. ADR 0003 requires it to be registered
as a wake instant; that obligation is what keeps the catalogue from becoming a tick table.

**One initiator per exchange, and it is the chip whose timeline the exchange serves** — Memory for the
video group (the beam), Sound for its refill (the sample clock), the burst owner for a bus grant, the
CPU for a register write. The subscriber is the other end, always.

**Driver identity and release.** A record names the chip that drove it; releasing is a `Z` record. A
release is a crossing in its own right where the receiver reacts to it (`BGACK` released → the CPU
resumes).

## 2. Video and sound delivery

| Wire | Driver | Subscriber | Resolver | Class | Wake condition | Rate/frame | Basis |
|---|---|---|---|---|---|---|---|
| `LOAD` (U400.106 → `U401.37`) + `DCYC` (U400.138), one record per group | Memory | Video | single-driver | 1 | **four words are available** — one record per group, not per word | 4 000 | 80 words per active line ÷ 4 × 200 lines = 16 000 words. The group is the modelled unit because the Shifter's input buffer is 4 words: a stated approximation of the **buffering**, not of the timing |
| `MD0–MD15` (U401.56–71) | Memory | Video (samples, does not subscribe) | tristate bus | data | — | 16 000 records | 4 records per group; payload only |
| `SREQ` (`U401.36` → U400.107; **driver: Sound**) | Sound | Memory | single-driver, active low | 1 | the sample FIFO is about to underflow | ≤ 313 | one refill per line at 50 kHz (3.2 of 4 words consumed per line); ~125 at 25 kHz, ~63 at 12.5, ~31 at 6.25. A 4-word FIFO holds 1.25 lines at the top rate, so refills cannot exceed one per line |
| `SLOAD` (U400.106 port group: 1 control lane + 16 data lanes) | Memory | Sound | single-driver | 2 | answers the `SREQ` it just served | ≤ 313 | up to 4 words per refill, one internal slot (250 ns) each |
| `DE` (U400.141) | Memory | Video | single-driver | 1 | the display window opens | 200 | one per vertically active line; gates the fetch and the pixel output |
| `DE` — second subscriber: `TBI` (U203.20) | Memory | Timers | single-driver | 1 | the same write, counted by Timer B | 200 | one count per active line. Documented (`board-inventory.md` §6.15), not drawn in the redraw |
| `BLANK` (U400.140) | Memory | Video (level read, no subscription) | single-driver | data | — | 313 records | Video is already running when blanking changes, so it reads the level; nothing starts |
| `HSYNC` 143, `VSYNC` 142 | Memory | **nobody in the machine** | single-driver | data | — | 313 + 1 records | the host display stage is not a virtual chip; it reads the log per frame (ADR 0004) |
| `MWE`, `MWD`, `MWK` | Memory | Video | single-driver | 1 | a Super Hi-Res write | ~0 | never exercised at functional level; kept so the pin set is complete |

> **Correction — 2026-11-09** (ADR 0011, ticket #20). The first row's basis line called the 4-word buffer
> an approximation of the Shifter's. The buffer is now **Memory's own**: filled one word per 250 ns
> internal slot, drained as one delivery when the fourth word lands. The delivery instant, the 4 000/frame
> rate and the four data records per group are unchanged; what retires is the hedge on the buffering. What
> remains approximate is the buffer's occupancy against the Shifter's own latch.

## 3. CPU, memory and the bus

| Wire | Driver | Subscriber | Resolver | Class | Wake condition | Rate/frame | Basis |
|---|---|---|---|---|---|---|---|
| `AS`, `LDS`, `UDS`, `R/W`, `FC0–FC2`, `A1–A23` | CPU | Memory | tristate bus | 2 | the CPU opens a bus cycle | ≤ 40 000 | one per access; the exchange is bounded by Memory's slot schedule, so this is a request-and-answer, not a crossing |
| `D0–D15` | CPU / Memory / Disk / Blitter | the open exchange | tristate bus (**conflict reported**) | data | — | ≤ 40 000 records | lanes travel together; two differing drivers is a modelled conflict |
| `DTACK` (71) | Memory | CPU | single-driver | 2 | the answering instant arrives | ≤ 40 000 | computed from the slot schedule; the CPU's wait state is its clock advancing to this record |
| `BERR` (23) | Memory | CPU | single-driver | 2 | a bus fault | ~0 | should never fire on valid TOS software |
| `VPA` (25), `VMA` (24) | Memory / CPU | Memory / CPU | single-driver | 2 | a 6800-style peripheral cycle | per MFP access | the MFP's cycle shape, not an event |
| `RESET` (66) | reset network | CPU, Memory, Disk, Timers | single-driver | 1 | power-on | 1 per boot | the reset network is not a chip; modelled as a host event |

## 4. Interrupts

| Wire | Driver | Subscriber | Resolver | Class | Wake condition | Rate/frame | Basis |
|---|---|---|---|---|---|---|---|
| `IRQ` (U203.32) | Timers | Memory | single-driver, active low | 1 | the MFP has a pending, unmasked source | one per assertion | the MFP's 16 sources and its priority encoder are internal to Timers, one line carries them all |
| `IPL0–IPL2` (33/34/35) | Memory | CPU | single-driver, 3 lanes | 1 | the **resolved level changes** | 1 + ~309 + one per MFP assertion | Memory arbitrates VBL 4, HBL 2 and the MFP's request 6 and drives the winner. HBL is suppressed during the VSYNC pulse: ~309 lines, to be confirmed against the timing tables |
| `IACK` (83) | CPU | Timers | single-driver | 2 | the CPU acknowledges | one per acknowledge | part of the cycle `IPL` opened |
| `D0–D7` vector | Timers | CPU | tristate bus | data | — | one per acknowledge | the MFP supplies its vector; autovector levels need no data |
| `FDINT` 16, `DINT` 17, `BINT` 60, `SINT` 105 | Memory | Timers | single-driver, active low | 1 | floppy command done, disk DMA done, blit done, sound frame end | per completion | each sets an MFP pending bit; that is the whole behaviour |
| `KHZ500` (6), `CLK8/4/2` (3/4/5), `SCLK`, `FCLK`, `CLK` | Memory / Video | — | **class 3** | — | never | — | phase only; no records, no subscriptions |

## 5. Bus ownership

| Wire | Driver | Subscriber | Resolver | Class | Wake condition | Rate/frame | Basis |
|---|---|---|---|---|---|---|---|
| `BR` (29 in, `BGO` 27 out) | Disk DMA, relayed by Memory; Blitter directly (`U101.13`) | CPU | **wired-OR (open collector)** | 1 | a burst wants the bus | 1 per transfer | two possible drivers — #23 must confirm the resolver |
| `BG` (28 in / `BGI`) | CPU | the requesting burst owner | wired-OR | 1 | the CPU has finished its cycle | 1 per transfer | the CPU stops driving; a real state change |
| `BGACK` (26) | the burst owner | CPU | wired-OR | 1 | the device has taken / released the bus | 2 per transfer (assert + release) | the release is a crossing: the CPU resumes |
| `RDY` (38 → 135) | Disk | Memory | single-driver, active low | 1 | the DMA controller wants the bus | ~60 while streaming | Memory's part is a relay into `BR`; the relay adds no wake |

Six wakes per ownership transfer: three to acquire (`BR`, `BG`, `BGACK`), three on release. The 4-cycle
acquire and release latencies are computed, not scheduled. This is what makes the Blitter's cooperative
mode (a release between words) expressible as a second transfer.

## 6. Register accesses and selects

| Wire | Driver | Subscriber | Resolver | Class | Wake condition | Rate/frame | Basis |
|---|---|---|---|---|---|---|---|
| `CS` on the Shifter (`A1–A6`, `LD`, `HD`) | Memory | Video | single-driver | 1 (write) / 2 (read) | a **write** to a Shifter register | ~10 idle, up to 939 in a raster demo | a write changes the target's future behaviour; a read only answers the cycle |
| `MFPCS` | Memory | Timers | single-driver | 1 / 2 | a write to the MFP; reads answer | per access | the MFP's register file is its state |
| `SNDCS` 19, `SNDIR` 18 | Memory | Sound | single-driver | 1 / 2 | a PSG or LMC write | per access | `SNDIR` is the Microwire direction line |
| `FCS` 136 | Memory | Disk | single-driver | 1 / 2 | a DMA-controller register access | tens | |
| `JOYWE` 101, `JOYWL` 102 | Memory | Controllers | single-driver | 1 | a joystick port read select | per read | the strobe is the crossing, the data is not |
| `N6850` 48 | Memory | Controllers | single-driver | 1 / 2 | a keyboard-ACIA access | per access | MIDI shares the pin and is out of scope |
| `CMPCS` 139 | Memory | — | single-driver | — | cartridge/shadow, out of scope | — | listed for completeness |

**One write costs two wakes**: the CPU's cycle start wakes Memory (class 2, bounded) and Memory's select
wakes the target (class 1). ADR 0004's driver identity is what makes the second hop honest — Memory is
the machine's only address decoder (its `RAM`/`DEV`/`DMA` space selects never leave `U400`), so it is
not a relay that could be collapsed away.

## 7. Disk

| Wire | Driver | Subscriber | Resolver | Class | Wake condition | Rate/frame | Basis |
|---|---|---|---|---|---|---|---|
| `DCYC` (138, DMA-cycle record) | Memory | Disk | single-driver | 2 | the DMA cycle is serviced | 312 while streaming | 625 bytes/frame ÷ 2 |
| `FDCS`, `FDRQ`, `CD0–CD7`, `CR/W`, `CA1`, `CA2` | Disk (internal) | — | — | — | **not crossings** | — | both `U300` and `U301` are inside the Disk chip, so the FDC↔DMA handshake never leaves it |
| `STEP`, `DIRC`, `WG`, `WD`, `MO`, `RD`, `DDEN`, `TR00`, `WPRT`, `IP` | Disk | drive (off-board) | — | 1 | drive control | 0 in v1 | no drive model in the skeleton; listed so the pin set is complete |

## 8. Input

| Wire | Driver | Subscriber | Resolver | Class | Wake condition | Rate/frame | Basis |
|---|---|---|---|---|---|---|---|
| `JOYAH/JOYAL`, `BUTTON`, `PEN`, `PAD0X–PAD1Y` | host (J500/J501) | Controllers | single-driver per lane | 1 | the user moves something | ~0 idle | input only moves when the user does |
| `ACC` | paddle network (LM556) | Controllers | single-driver | 1 | a paddle conversion | per conversion | the GSTMCU measures the pulse width |
| IKBD serial data in | keyboard (off-board) | Controllers | single-driver | 1 | a key or mouse event | ~0 idle | the walking skeleton has to be typed on |

## 9. What is explicitly not a crossing

Kept as a list because the ticket's requirement is that these are **invisible by construction**, not by
convention: a wire that appears here has no class-1 subscriber to register, and where the wire does not
exist at all (class 3, internal state) nothing can subscribe.

- **Data buses**: `D0–D15`, `A1–A23`, `MD0–MD15`, `CD0–CD7`, `SD0–SD7` (internal to Sound), `PD0–PD15`.
- **Completions**: `DTACK`, `BERR`, `VPA`, `VMA`, `IACK`, `*SLOAD`, `DCYC`. Class 2: they answer an
  exchange the subscriber itself opened.
- **Clocks**: `CLK`, `CLK8`, `CLK4`, `CLK2`, `FCLK`, `SCLK`, `KHZ500`. Class 3: not wires.
- **Inside a chip**: the video beam counter, the DRAM refresh counter and its `MAL0–MAL9` outputs, the
  sound address counter and frame pointers, the slot scheduler, the decode PLA, the MFP's four timer
  counters and its 16-source priority encoder, the PSG tone counters, the Blitter's address pointers,
  the FDC's MFM shift register and CRC, the keyboard ACIA's framing counters.
- **Glue absorbed by a chip**: the palette latch `U511`, the pixel pipeline `U500/U501`, the DE/LOAD
  pipeline `U304/U306`, the RAM transceivers. `LATCH` is therefore Video-internal, not a crossing.

## 10. Totals

Class-1 wakes per frame, by workload:

| Workload | Wakes/frame | Dominant terms |
|---|---|---|
| GEM desktop, idle | **≈ 4 730** | video groups 4 000 · `DE` 400 (two subscribers) · HBL 309 · VBL 1 · Shifter/MFP register writes ~20 |
| Floppy load | ≈ 4 810 | + `RDY` ~60, `FDINT` ~20, disk selects |
| Raster demo | ≈ 5 670 | + up to 939 Shifter register writes |

Class-2 queue entries are separate and much larger: **≤ 40 000 bus cycles/frame**, ~4 entries each —
the 40 % bill ADR 0005 records for #10, and the reason the memory leaf exists as a planned option.

The old 10²–10³ band is gone, and the reason is a decision, not an error: the video group delivery is a
crossing at 4 000/frame by the owner's ruling of 2026-11-05 (one event per four words, an approximation
of the Shifter's input buffer). Everything else in the table stays two to three orders below the bus
traffic, which is the property the design actually needs.

## 11. Open points

1. **Resolvers.** Which nets are genuinely open-collector is #23's inventory. Until it lands, every wire
   defaults to tristate, the strictest resolver, so an unlisted multi-driver net shows as a reported
   conflict rather than as a wrong value. `BR`/`BG`/`BGACK` are almost certainly wired-OR (`U101` and
   `U400` both on the net).
2. **`DE` → `TBI` is documented, not drawn** (`board-inventory.md` §6.15): one `Memory → Timers`
   crossing per active line, unverified at the pin level.
3. **`TAI`'s edge** on the STE is the sound-frame-end signal, possibly through an added 74LS164 delay
   (§6.16); it decides whether Timer A counts the `*SINT` edge or a copy of it.
4. **HBL during the VSYNC pulse** — the ~309 figure assumes suppression; the timing tables should
   confirm it.
5. **The sound refill's exact slot — answered by #20 (ADR 0011).** It is a rule, not a slot number: the
   refill takes the **first blanking internal slot after `SREQ` is asserted**, up to four words
   back-to-back, and never enters a display window (`snden = ~hde1 & sreq`, `mcucontrol.v:95`). #26 keeps
   the FIFO depth and the sample cadence; placement is settled here.
6. **Same-tick ordering — answered by #7, and there is no table** (ADR 0007). The three `IPL` sources
   never reach the queue as three events: Memory resolves them into one three-lane record by taking the
   highest asserting level (6 > 4 > 2). Two bus masters on `BR` produce one identical wired-OR record,
   and the grant follows request order (earliest assertion, `seq` breaking a same-instant tie), with a
   simultaneous `BGACK` double drive reported as a conflict. A video group and a CPU cycle never share
   an instant: they occupy an internal and an external slot, 8 ticks apart.

## 12. Revision history

The first version of this document was drawn on the seven-chip partition and on a "wake = any event"
reading. Four corrections produced this version, each with its evidence in the ticket:

1. **The partition is eight chips**, and the Blitter is one of them (two-DMA rule).
2. **`SREQ`/`*SLOAD` are the sound path; `DCYC`/`*LOAD` the video path** —
   `research/bus-arbitration.md`, `research/schematic-evidence.md` §1.
3. **The video delivery is one event per four words, initiated by Memory**, and the sound refill is one
   event per FIFO demand, initiated by Sound (owner's rulings, 2026-11-05).
4. **The catalogue is a subscription table, not a trigger list** (ADR 0006): a class-2 completion costs
   a queue entry and starts nothing.

Rates also changed where the old arithmetic was wrong: the video fetch is **16 000 words/frame** (80 per
active line × 200), not a per-line count multiplied by 313; the sound word rate is **1 001/frame at
50 kHz**, not 500; and the FDC↔DMA handshake is internal to Disk, so `FDRQ` is not a crossing at all.
