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

### Virtual chip

The unit of the emulator's decomposition: a subsystem of the machine, regrouped from one or more
physical chips, whose pins and internal logic keep the names of the physical components they come
from. It is the smallest thing that can make progress on its own.
_Avoid_: device, module, component, peripheral

### Signal crossing

An output control signal of one virtual chip that provokes a behaviour in another virtual chip: the
only kind of moment the machine model treats as significant. Internal state evolution and mere pin
value changes are not crossings. In the engine's terms it is a **subscription whose firing starts new
work** in the receiving chip (ADR 0006); an answer inside an exchange the receiver itself opened is
not a crossing.
_Avoid_: event, interrupt, edge, bus transaction

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

### Resolver

The rule that turns the drivers' current values into a wire's value: single-driver, wired-AND/OR for
open-collector nets, or tristate with a reported conflict. It is a property of the wire type, decided
from the schematic, never chosen per access.
_Avoid_: logic level, arbitration, priority

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

The rule that decides which of two crossings placed at the same instant of machine time happens
first. It is derived from hardware priority, never from the order in which the emulator happens to
run its parts.
_Avoid_: tie-break, race, ordering hack

### Bus ownership

The right to drive the machine's system bus, granted and released through the 68000's arbitration
handshake. Held by the CPU, or temporarily by a bus-mastering device.
_Avoid_: bus access, bus lock

### Memory service

The scheduled use of RAM by a client — video fetch, sound fetch, CPU access, Blitter access —
granted by the memory controller inside the machine's timing. Distinct from bus ownership: a client
can be served without ever owning the bus.
_Avoid_: DMA, bus access, memory access

### TOS image

The operating-system ROM set the machine needs to run, which the project never distributes: it is
obtained at first run, is single-version, and is accompanied by the reverse-engineered disassembly
used for debugging symbols.
_Avoid_: ROM, firmware, BIOS

### Oracle

A reference implementation used to settle "what should this do?" questions during development, never
shipped as part of the project.
_Avoid_: reference emulator, baseline

### Walking skeleton

The first end-to-end build of the emulator: it boots the TOS image to GEM and demonstrates the
timing model on real software, before any device is complete.
_Avoid_: prototype, MVP, spike
