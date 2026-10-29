# STeemBrowser

Web version of the popular Steem SSE Atari ST emulator, with a modern and simple UI and very high quality games store.

## Repository layout

| Path | Content |
|---|---|
| `atari-st-hardware/` | Hardware wiki: architecture, components, memory map, bus protocol, pinouts, STe enhancements |
| `atari-st-software/` | TOS, GEMDOS/XBIOS/BIOS APIs, file formats, ROM system, notable games and demos |
| `atari-st-emulation/` | Emulation techniques: 68000 core model, timing, chipset emulation strategies |
| `steemsse-V4.2.0_R10/` | Source snapshot of Steem SSE v4.2.0 R10 (GPL-3.0), the emulator this project builds on |
| `hatari/` | Captured source of the Hatari emulator, used as a reference implementation |
| `neost/` | Captured source of NeoST, used as a reference implementation |

## Captured upstream sources

These trees are **verbatim snapshots** committed as plain files (no upstream `.git`, no submodules), so they can be read, searched and diffed directly inside this repository. Do not edit them; re-import from upstream instead. Their own licences apply within each directory.

| Directory | Upstream | Revision | Date |
|---|---|---|---|
| `steemsse-V4.2.0_R10/` | Steem SSE 4.2.0 R10 release | release snapshot | — |
| `hatari/` | https://github.com/hatari/hatari | `23bc3060ebf0da348cc842e358c102ba88002b56` | 2026-10-25 |
| `neost/` | https://github.com/habib256/neost | `da4eb23daf6169f1faf1d3f3229e53e98f0d1777` | 2026-10-28 |

The root `.gitignore` targets this project's own build output; explicit negations at its end keep upstream source directories whose names collide with build patterns (e.g. `src/debug`, `src/cpu/jit/arm`) from being excluded.
