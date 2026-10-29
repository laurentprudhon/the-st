# STeemBrowser

The emulator being designed in this repository: a single, fixed Atari 1040STE emulated with a
time-driven, event-ordered model of its chips, eventually delivered as a web application.

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
value changes are not crossings.
_Avoid_: event, interrupt, edge, bus transaction

### Machine time

The single monotonic timeline the whole emulated machine runs on, derived from the 32 MHz clock.
Every crossing is placed on it at a definite instant.
_Avoid_: emulated time, cycle count, timestamp

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
