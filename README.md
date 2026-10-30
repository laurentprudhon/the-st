# The ST

A faithful emulator of one computer: an Atari 1040STE with TOS 1.62, PAL video and French Péritel
output. Not a family of machines, not a configuration screen — the one machine, modelled as a
time-driven, event-ordered system of virtual chips on a single timeline.

*Not affiliated with, endorsed by, or sponsored by Atari. "Atari" and "ST" are trademarks of their
respective owners; this is an independent, non-commercial emulator project.*

**Status**: design phase. The architecture is decided and recorded in `docs/adr/`; no emulator code
exists yet. The next artefact is a walking skeleton that boots the TOS image to the GEM desktop and
proves the timing model before any device is complete.

- Implementation: **C# on .NET 11**, native multiplatform — see
  [ADR 0002](docs/adr/0002-c-sharp-on-dotnet-native.md).
- Timing: one 32 MHz oscillator, every clock derived by a power of two, one time domain — see
  [ADR 0001](docs/adr/0001-one-oscillator-one-time-domain.md).
- Terms: `GLOSSARY.md` defines the machine, virtual chip, signal crossing, machine time and the rest;
  use those words, not their synonyms.
- Design map: [the wayfinder map](https://github.com/laurentprudhon/the-st/issues/1) on the issue
  tracker, where every open design decision is a ticket.

## Design principle

Simplicity and understandability beat feature count. One teachable rule beats a table of special
cases, and a decision that removes variability — one machine, one TOS, one clock — beats one that adds
a switch.

## Repository layout

| Path | Content |
|---|---|
| `docs/adr/` | Accepted architecture decisions, numbered, with the alternatives that lost |
| `GLOSSARY.md` | The project's domain language |
| `atari-st-hardware/` | Hardware wiki: architecture, components, memory map, bus protocol, pinouts, STe enhancements |
| `atari-st-software/` | TOS, GEMDOS/XBIOS/BIOS APIs, file formats, ROM system, notable games and demos |
| `atari-st-emulation/` | Emulation techniques: 68000 core model, timing, chipset emulation strategies |
| `research/` | Evidence gathered for specific decisions, each cited and dated |
| `design/` | Board inventory and other working design artefacts |
| `steemsse-V4.2.0_R10/` | Source snapshot of Steem SSE v4.2.0 R10 (GPL-3.0), reference implementation |
| `hatari/` | Captured source of the Hatari emulator, used as a reference implementation |
| `neost/` | Captured source of NeoST, used as a reference implementation |

## Captured upstream sources

These trees are **verbatim snapshots** committed as plain files (no upstream `.git`, no submodules),
so they can be read, searched and diffed directly inside this repository. Do not edit them; re-import
from upstream instead. Their own licences apply within each directory.

| Directory | Upstream | Revision | Date |
|---|---|---|---|
| `steemsse-V4.2.0_R10/` | Steem SSE 4.2.0 R10 release | release snapshot | — |
| `hatari/` | https://github.com/hatari/hatari | `23bc3060ebf0da348cc842e358c102ba88002b56` | 2026-10-25 |
| `neost/` | https://github.com/habib256/neost | `da4eb23daf6169f1faf1d3f3229e53e98f0d1777` | 2026-10-28 |

The root `.gitignore` targets this project's own build output; explicit negations at its end keep
upstream source directories whose names collide with build patterns (e.g. `src/debug`, `src/cpu/jit/arm`)
from being excluded.

## TOS

The TOS image is never committed. It is obtained at first run, single version (1.62), alongside the
reverse-engineered disassembly used for debug symbols.
