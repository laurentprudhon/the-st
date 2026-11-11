# The ST

The emulator being designed in this repository: a single, fixed Atari 1040STE emulated with a
time-driven, event-ordered model of its chips, delivered as a native multiplatform desktop
application.

## Language

### The machine

The one computer this project emulates: a mature-production Atari 1040STE with TOS 1.62, PAL video
and the French Peritel output. There is no configuration, no machine selection and no variant
support.
_Avoid_: target, profile, configuration, preset, model

### Accuracy level

A grade of fidelity the project commits to, taken from the hardware overview: **functional**
(right architectural results, approximated timing) or **cycle- and bus-aware** (timestamped bus
accesses, scheduled devices, meaningful access order). Electrical, signal-level fidelity is not a
level this project targets.
_Avoid_: precision, realism, fidelity tier

> **Correction 2026-11-09** (ADR 0011, owner's ruling on #20). The project builds **one** level, the
> cycle- and bus-aware machine; there is no functional step before it. The design already computes every
> answering instant from the memory controller's slot counters, so a "four clocks" level would be a
> second path rather than a simpler one. "Accuracy level" survives as the name of the *distinction* in
> the hardware overview, not as a phase of this project.

### Virtual chip

The unit of the emulator's decomposition: a subsystem of the machine, regrouped from one or more
physical chips, whose pins and internal logic keep the names of the physical components they come
from. It is the smallest thing that can make progress on its own.
_Avoid_: device, module, component, peripheral

### System Control Unit

The virtual chip that owns the machine's time base and its bus: the 8-phase slot counter, the DRAM
controller and its decode, the video and sound fetch streams, sync generation (`DE`, `BLANK`,
`HSYNC`, `VSYNC`), `DTACK`/`BERR`, the chip-select decode and the `IPL` arbitration. It is the
`U400` half that is not the pad/pen logic, and it is what "the memory subsystem" used to be called.
It holds two **stores** — `Ram` and `Rom` — as plain data (ADR 0014).
_Avoid_: Memory, GSTMCU, memory controller, MMU

> **Correction 2026-11-11** (ticket #31, owner's ruling). This role was named **Memory** until the
> stores left it. Owner's words: *"GSTMCU is too cryptic when you want to understand the machine,
> what about keeping only three letters of the name : SystemControlUnit ?"*
>
> **The vocabulary rule that comes with the rename: "memory" names the resource — the RAM and the ROM
> as storage — and never the chip.** That is why *Memory service*, *Memory leaf* and *memory
> controller* keep their words: they name the use of storage, the fast path in the memory-access
> path, and the function inside the chip — none of them names the virtual chip.

### Timers and events

The virtual chip that counts time and names peripheral events: the four MFP counters and their
prescalers, the **event selector** (sixteen event inputs over four bit-files), the vector register and
the acknowledge path, `GPIP`/`AER`/`DDR` with its eight port lines, and the USART as a declared stub.
It is `U203`, the MFP 68901; the name states both halves because neither describes the other, and
"interrupts" would be the CPU-side word for what the System Control Unit does with the single `IRQ`
line this chip drives.
_Avoid_: interrupt controller, Timers, interrupts chip (the part is the MFP — that stays in the
partition's second column)

> **Correction 2026-11-11** (owner's ruling). The partition called this chip *Timers and interrupts*
> while every operational artefact called it *Timers*; owner's words: *"maybe we could name it :
> TimersAndEvents ?"* — nothing at this stage presents an interrupt, it selects and names an event, and
> the level belongs to the System Control Unit. The same pass made the word *event* single-sensed: bare
> "event" for a wire record or a crossing now reads **wire event** or **record** in the ADRs, so *Event*
> below means only a peripheral condition.

### Signal crossing

An output control signal of one virtual chip that provokes a behaviour in another virtual chip: the
only kind of moment the machine model treats as significant. Internal state evolution and mere pin
value changes are not crossings. In the engine's terms it is a **subscription whose firing starts new
work** in the receiving chip (ADR 0006); an answer inside an exchange the receiver itself opened is
not a crossing.
_Avoid_: event, interrupt, edge, bus transaction

### MFP source

One of the sixteen **event inputs** of the Timers and events chip — the hardware's own word for the slots (the
number is the priority, the bit position in the pending, enable, mask and in-service register pair), and
the chip needs it because an acknowledge names a source. What *enters* a source is an **event**. A
source is *selected* when its event is pending, it is enabled and unmasked, and no source at or above it
is in service; the selected source is what the acknowledge's vector byte names. Distinct from the CPU's
interrupt **level**, which the Timers and events chip never computes.
_Avoid_: priority level, IRQ number, vector number

### Event

A condition a peripheral or a counter raises without being asked — a port line's edge, a counter's
expiry, a USART state — which the Timers and events chip latches into that source's pending bit and then selects
one of. The chip **names** the event; presenting it to the CPU is not its act: the System Control Unit
drives the level (`IPL0–IPL2`) that the CPU eventually sees. "Interrupt" is the CPU-side word for the
result, not the thing the selector chooses — which is why the Timers and events chip is not an interrupt
controller.
_Avoid_: interrupt, interrupt request, IRQ, crossing, wire event

### Event selector

The block inside the Timers and events chip that takes the sixteen event inputs and selects exactly one: the
highest-numbered source whose event is pending, enabled, unmasked, with none numbered at or above it in
service. The selected number *is* the vector offset (`(VR & 0xF0) + n`), so the block's product is a
named event — never a level. It computes no priority level, holds no chronology and owns no table: the
rule is four conditions on four sixteen-bit files.
_Avoid_: interrupt controller, priority encoder, arbiter, multiplexer

### In-service bit

The timers chip's record that a source's handler is running. Set when the source is acknowledged in
software end-of-interrupt mode, clear in automatic mode, and cleared by writing 0 to it. A source in
service is not selected again, which is what stops a second edge from re-entering its own handler.
_Avoid_: active flag, busy bit, pending bit

### Wire

The modelled connection between virtual chips: a port group — one control signal, or every lane of a
bus — carrying a resolution of the values its drivers impose. A wire is not a timeline and not a
schedule; it is the place values meet.
_Avoid_: net, connection, channel, signal line

### Wire event

One record in a wire's append-only log: an instant, the driver that wrote, the lanes it changed and
the value it imposed, `Z` for a release. A wire event is not a signal crossing — most wire events
provoke nothing in any other chip and wake nothing.
_Avoid_: signal crossing, pin toggle, transition, edge

### Driver

The virtual chip that imposes a value on a wire at an instant. Several drivers may exist on one wire;
at most one drives a tristate bus at a time, and any number may pull an open-collector line.
_Avoid_: writer, source, owner

### Group delivery

The System Control Unit's single record on `LOAD`+`DCYC` when the fourth word of its 4-word fetch buffer lands: the one
crossing that starts the Shifter, 4 000 times a frame in low resolution. It carries no data — the four
words are separate payload records sampled at its instant. Video consumes groups and never sees a slot
decision (ADR 0011).
_Avoid_: burst, DMA request, word event

### Payload record

One `MD0–MD15` wire event: the four words of a group arrive as four of them, 16 000 a frame. Video
samples them at the group delivery's instant; it does not subscribe to the data wire.
_Avoid_: MD record, data packet, fetch

### Chunk

The Shifter's unit of work and of output: **16 pixels**, one iteration of the state machine, one write
into the virtual screen. A group delivery drives one chunk in low resolution, two in medium, four in
mono, so a frame writes 4 000 / 8 000 / 16 000 chunks (ADR 0012).
_Avoid_: tile, block, group (of pixels)

### Virtual screen

Video's output state: the visible raster the Shifter paints into, written 16 pixels at a time in step
with its state machine (ADR 0012). **840 × 284** columns × rows in colour (52.5 µs at the maximum colour
pixel rate; 39 upper border + 200 display + 45 lower border) and **640 × 400** in mono, 16-bit `0RGB`
per pixel, borders included and blanking excluded. The host reads it and scales it; the host derives no
pixels. The chip also publishes the **physical display area** of its two reference monitors — 216 × 160 mm
(SC1224, colour) and 210 × 130 mm (SM124, monochrome) — which is what a realistic scaling is computed
from: 1.35 : 1 and 1.615 : 1.
_Avoid_: framebuffer, raster buffer, video RAM


### Resolver

The rule that turns the drivers' current values into a wire's value: single-driver, wired-AND/OR for
open-collector nets, or tristate with a reported conflict. It is a property of the wire type, decided
from the schematic, never chosen per access.
_Avoid_: logic level, arbitration, priority

### WriteAndWait

The awaited write: the coroutine appends its wire record and then suspends, so its next instruction
runs at that record's instant, behind whatever the record set running. Its counterpart, `Write`,
appends and continues at the chip's own clock. The pair changes nothing about a record's value or its
ordering; the difference is only when the writer's next line runs, and it is how a chip expresses a
hardware phase locally instead of through a global rule.
_Avoid_: async write, deferred write, commit

### Run-ahead horizon

How far a chip's clock may sit ahead of machine time: to its next read, or — in an iteration with no
read — to an awaited write. It is bounded by the chip's own logic, never by a global quantum, and it
is what keeps lazy advance from becoming a tick table.
_Avoid_: lookahead, quantum, budget, slack

### Subscription

A chip's pending read on a wire for an instant it does not decide: "resume me when this wire next
changes, at or after now". It is the only way one chip's output makes another chip run, and the
crossing catalogue is the table of the subscriptions the machine is allowed to have. A chip's own
registered obligation — a timer expiry, a slot boundary, a buffer deadline — is not a subscription.
_Avoid_: wake, trigger, callback, listener, event handler

### Oscillator

The single physical frequency source from which every clock in the modelled machine is derived.
_Avoid_: crystal, master clock, clock source

### Clock

A frequency derived from the oscillator and distributed to the virtual chips. A clock is not a
timeline: it gives its chips a common phase, nothing more.
_Avoid_: clock rate, frequency, timebase

### Clock domain

The virtual chips that share one clock's edges, and so keep a fixed phase relationship with each
other. The machine has two — the main tree and the sound path — whose frequencies hold a constant
ratio; a clock domain never gets a timeline of its own.
_Avoid_: domain, sync group, clock island

### Machine time

The single monotonic timeline the whole emulated machine runs on, measured in ticks of the 32 MHz
oscillator. Every crossing is placed on it at a definite instant.
_Avoid_: emulated time, cycle count, timestamp

### Time domain

A timeline whose events are ordered against one another. The machine has exactly one, machine time;
clock domains are frequencies inside it, never timelines beside it.
_Avoid_: clock domain, timebase, timescale

### Host time

Real wall-clock time, used only to deliver frames and audio to the user. It never advances or
orders machine time.
_Avoid_: real time, wall time

### Same-tick ordering

The rule that decides which of two wire events or continuations placed at the same instant of machine
time happens first: **arrival order at that instant**, i.e. the order in which the engine's ordered
queue holds them (ADR 0007). The machine has no priority table — a hardware precedence is expressed by
the wire's resolver (an interrupt level as a maximum, an open-collector line as an OR, a contested bus
as a reported conflict), by the memory slot's phase, or by a chip asking to be resumed behind the
receivers of its own write.
_Avoid_: tie-break, race, ordering hack, priority

### Bus cycle

One exchange between a bus master and the memory controller: the master's masked control record (AS,
R/W, function codes, address, and data if a write), the controller's answer record at an instant the
controller computes from its own slot schedule, the master's sample of the data, and the master's
release record. Its duration is the difference between those two instants — never a declared number,
which is why the CPU's wait state is simply its clock standing at the answer.
_Avoid_: bus access, bus transaction, memory cycle

### Bus ownership

The right to drive the machine's system bus, granted and released through the 68000's arbitration
handshake. Held by the CPU, or temporarily by a bus-mastering device.
_Avoid_: bus access, bus lock

### Memory service

The scheduled use of RAM by a client — video fetch, sound fetch, CPU access, Blitter access —
granted by the memory controller inside the machine's timing. Distinct from bus ownership: a client
can be served without ever owning the bus.
_Avoid_: DMA, bus access, memory access

> **Correction 2026-11-09** (ADR 0011). A client's *service* is the slot it gets, and the internal slot
> has exactly one owner by one equation — video while the horizontal display window is open, otherwise
> sound when `SREQ` is pending, otherwise refresh. Refresh is the filler of that equation, not a
> preemptor.

> **Note 2026-11-11** (ticket #31, owner's ruling). The word *memory* here names **the resource**, not
> the chip: the service is granted by the **System Control Unit**, and its unit is the *Slot* below.
> The name is kept deliberately — one rule ("memory = the resource") instead of two renames — and the
> leaf below follows the same rule.

### Slot

The unit of memory service: one DRAM access, 250 ns, two CPU clocks. A cycle of the memory controller is
four CPU clocks and holds **one internal slot and one external slot**; internal slots serve video,
sound and refresh, external ones the CPU and any granted bus master. A slot is a position in the
controller's phase counter — arithmetic, never a record and never an event.
_Avoid_: bus cycle, tick, time slot

### Memory leaf

The deferred fast path inside the System Control Unit that serves a CPU bus cycle with a direct call instead of
the queue round trips, appending the same records at the same instants. A last resort, off by default:
it may remove pops and stack frames, never a clock movement, a record, another chip's subscription, or
an instant.
_Avoid_: fast path, shortcut, special case, bypass

> **Note 2026-11-11** (ticket #31). *Memory* here names the memory-access path, not the chip — the chip
> is the System Control Unit. The leaf inlines the **slot wait**, not the store's read, so the store
> split (ADR 0014) neither crosses its boundary nor makes it cheaper to justify.

### Store

A plain data structure that holds bytes for one chip and has no timeline: `Ram` (2 banks × 512 rows ×
512 columns × 16 bits = 1 MB) and `Rom` (2 × 27C010 = 256 KB, two byte lanes). Its interface **is** the
pins — bank, row, column, byte lane, direction — one call per access, and no timing: no cycle cost, no
latch state, no strobe sequence, no record, no subscription, no wake. A store is not a virtual chip; it
is the thing a chip holds. (ADR 0014)
_Avoid_: memory chip, RAM chip, device, address space

### Machine bytes

The host's view of a store: a `Span<byte>` in **machine byte order** (big-endian pairs, index `2n` the
high byte of word `n`), serving display inspection, the debugger and side-loading. Host-only — no
virtual chip may hold it, because a chip's only route to memory is its pins — and written **only while
the machine is stopped**, so a host write carries no record and no instant. (ADR 0014)
_Avoid_: memory dump, buffer, host pointer, RAM array

### Internal register log

A virtual chip's own state — registers, counters, flags — kept as an append-only log of successive
values stamped with the chip's own clock, exactly like a wire's log but with one driver and no resolver.
It never enters the queue, has no subscriber and cannot be backdated, so it can be neither a crossing
nor an ordering question. A counting register logs anchors (reload, expiry, a software write) and its
value between them is arithmetic.
_Avoid_: state dump, snapshot, trace, variable

### Prefetch class

The documented placement of an instruction's last instruction-stream fetch relative to its write: before
it (class 0), after it (class 1), or two fetches after (class 2). It changes no cycle count — only which
of two accesses reaches an address first, which is the whole of what self-modifying code observes.
_Avoid_: pipeline, prefetch depth, fetch order flag

### Internal residue

The clock periods an instruction spends that its own bus cycles do not account for:
`periods − 4 × (read cycles + write cycles)`, taken from the timing tables of the processor's user's
manual. It is the only cycle-cost datum the core keeps as data.
_Avoid_: internal cycles, overhead, penalty, wait states

### Entry script

The ordered steps of exception entry — internal clocks, frame writes, the interrupt acknowledge, the
vector fetch — held as data, so the functional level sums them and the cycle-aware level walks them from
the same array.
_Avoid_: exception sequence, handler prologue, trap path

### Batch

One instant of machine time, fully drained: every queue entry stamped with it, including those born
while draining it. The unit of the determinism digest.
_Avoid_: step, slice, quantum

### Digest

The incrementally folded fingerprint of one batch's records — wire events and internal register writes
alike, in append order. Two runs of one image must produce identical digest streams; the first batch
where they part names the instant and the record where the model read something host-side.
_Avoid_: hash, checksum, snapshot

### TOS image

The 256 KB operating-system ROM set the machine boots. Two artefacts answer to the term: the
**shipped** EmuTOS 1.4 image — GPLv2, one per PAL language, selected from the host UI language — and
the **optional user-supplied** Atari TOS 1.62 image, which the project never distributes and accepts
only against a pinned hash accept-list. Either way it is single-version, and its symbol table comes
from the image's own build.
_Avoid_: ROM, firmware, BIOS

> **Correction 2026-11-07** (ADR 0010). This entry previously said the set *"is obtained at first
> run"* and is *"accompanied by the reverse-engineered disassembly"*. EmuTOS now ships with the
> project, nothing is fetched at run time, and the disassembly is no longer part of the term.

### Oracle

A reference implementation used to settle "what should this do?" questions during development, never
shipped as part of the project.
_Avoid_: reference emulator, baseline

### Walking skeleton

The first end-to-end build of the emulator: it boots the TOS image to GEM and demonstrates the
timing model on real software, before any device is complete.
_Avoid_: prototype, MVP, spike
