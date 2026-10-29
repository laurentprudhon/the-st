# TOS 1.62 ROM provisioning for a public open-source Atari STE emulator

Research date: 2026-10-29. Every download below was exercised with `curl` from this
machine; hashes are of bytes actually received and hashed here. No ROM binary is stored
in this repository — only hashes, URLs and verification recipes.

Scope: STeemBrowser needs one fixed TOS (1.62) for its test suite and first-run
experience, without committing copyrighted ROMs to a public repo.

**Bottom line**

- TOS 1.62 is still proprietary. No licence grant covering it was found, and the three
  mature emulators (Hatari, Steem SSE, NeoST) all refuse to ship it.
- There *are* usable download sources, including one CORS-enabled GitHub mirror that a
  browser build can actually `fetch()`. Hashes are pinned below for 5 language variants.
- EmuTOS 1.4's **256 KB** build is explicitly documented as suitable for "STe / Mega
  STe / emulators of the above" and is byte-for-byte the same size as TOS 1.62 (262144).
  It is the right default for CI and for the first-run experience.
- TOS 1.62 should be an *optional, user-triggered* fetch with a warning and a pinned
  SHA-256 check; CI must be green without it.

---

## 1. Legal / practical landscape

### 1.1 Who the code came from, and who holds it now

- TOS was developed by **Atari Corporation**; Wikipedia's infobox lists the licence as
  **proprietary commercial software** (<https://en.wikipedia.org/wiki/Atari_TOS>).
- Corporate chain, as documented in the Atari 8-bit FAQ timeline
  (<https://www.atariage.com/800/faq/>, section 11.2): Atari Corp. → reverse merger with
  JTS (31 Jul 1996) → Atari Division sold to HIACXI Corp., a Hasbro Interactive
  subsidiary (23 Feb 1998) → Hasbro Interactive acquired by Infogrames (29 Jan 2001),
  later renamed Atari Interactive / Atari SA.
- Consequence: the rights holder is identifiable and still trading. This is **not** an
  orphan work, so orphan-works exceptions (e.g. EU Directive 2012/28/EU) do not apply.
- I found **no public statement by Atari SA / Atari Interactive, Inc. authorising
  redistribution of TOS ROM images**, and no licence text in any of the projects below.
  Treat "TOS is freely distributable for emulators" as an unsourced community belief.

### 1.2 The "Atari released TOS for emulators around 1999" story

This is the claim worth pinning down, because it is repeated often. What I could
actually verify:

- The 1999-era Hasbro/Atari Interactive permission that Atari community documentation
  cites covers **hardware documentation**, not TOS: the Atari 8-bit FAQ credits
  "permission from Hasbro's Atari Interactive Division's release of this data" for the
  ANTIC/GTIA/POKEY datasheets (<https://www.atariage.com/800/faq/>, section 1.11).
- The one genuine free relicensing in this software stack is **Digital Research GEM**
  code, which EmuTOS describes as "old open-sourced code from Digital Research"
  (<https://emutos.sourceforge.io/>). That covers GEM components, not Atari's TOS.
- **No source documents a grant covering TOS 1.62** (or 1.06/1.04/1.02). Neither
  Hatari's manual, Hatari's `doc/emutos.txt`, EmuTOS's site/manual, Steem SSE's docs nor
  NeoST's README mentions any such permission — all four treat TOS as copyrighted.

Conclusion for the report: **do not build a policy on a 1999 TOS grant; the evidence
does not support one.** If such a grant exists, it is undocumented in every project that
would have benefited from it.

### 1.3 What comparable projects actually do

| Project | Ships TOS? | What it does instead | Source |
|---|---|---|---|
| **Hatari** | No | "it is not possible to ship an original ROM image with the Hatari package since these images are still copyrighted"; tells users to dump their own with TOSDUMP, and ships EmuTOS as `tos.img` | `hatari/doc/manual.html` §"Installation of a TOS ROM"; <https://www.hatari-emu.org/doc/manual.html> |
| **Steem SSE** (upstream of this project) | No | Tells the user TOS "is the most important file Steem needs", that images are "easily downloadable from the internet, probably from the same place that you downloaded Steem", or dump it from a real ST | `steemsse-V4.2.0_R10/steem/doc/TranslateTemplate.txt:2283`; no ROM in the 4.2.0 R10 tree (`find -iname '*.img'` → only `HD6301V1ST.img`, the IKBD MCU ROM) |
| **NeoST** | No | EmuTOS only; packages built with `NEOST_PACKAGE_NO_ATARI_TOS=ON`; "no proprietary ROM ships with NeoST"; repo history rewritten so it never served an Atari TOS | `neost/README.md` §ROMs, §Licences |
| **MiSTer / MiSTery** | No | "Put the core.rbf and the TOS as tos.img to the SD-Card" — user-supplied | <https://github.com/MiSTer-devel/AtariST_MiSTer> README §Usage |
| **esp32ste** (2025 STE emulator) | No | `roms/README.md`: "Put your own TOS dumps here (they are copyrighted and not part of this repository)" | <https://github.com/rlucencic/esp32ste/blob/main/roms/README.md> |
| **EmuTOS** | n/a | Free replacement, GPLv2, "no code written by Atari in EmuTOS" | <https://emutos.sourceforge.io/> |

Note the split: Hatari and NeoST *bundle a free OS* so the emulator is usable out of the
box; Steem historically assumed the user arrives with a TOS. STeemBrowser needs the
Hatari/NeoST model, because a browser emulator cannot assume anything about the host.

Two sites *do* redistribute TOS images (atariworld.org, Abdess/retrobios). Their
existence does not create a licence for us, and neither states one — atariworld.org
carries no licence or permission notice on its TOS page
(<https://www.atariworld.org/tos-rom/>). RetroBIOS's own footer says its tooling and
metadata are MIT while "BIOS and firmware files are third-party system software,
preserved for personal backup, archival and interoperability" — i.e. it asserts a
preservation rationale, not a permission.

### 1.4 Practical risk framing (not legal advice)

- The realistic enforcement vector is a **DMCA/notice-and-takedown against the repo or
  the fetch URL**, not litigation against a hobby emulator. A repo that contains no ROM
  bytes and only hashes/URLs has very little to take down.
- The related risk is **supply-chain**: third-party mirrors disappear. atariworld.org is
  a one-person fan site; RetroBIOS is a single-maintainer GitHub repo whose `bios/`
  directory can be rewritten at any time (a repo re-organisation would break every
  URL). Pin commit SHAs, not branches.
- Shipping a ROM inside an npm package or a container image would convert a "user
  fetched it" story into "we distributed it". Don't.

---

## 2. Verified download sources for TOS 1.62

All rows below were fetched on 2026-10-29 and hashed locally. `crc32` is the standard
zlib/IEEE CRC-32 of the whole image, which is the value the Atari world quotes.

### 2.1 Image hashes (what to pin)

| Variant | Size | CRC32 | SHA-256 | Notes |
|---|---|---|---|---|
| **1.62 UK** | 262144 | `1C1A4EBA` | `c0c2cf6668f08f0ddfe8558d33a68896363322e4530062c584ea2c64067d5ffb` | atariworld zip **and** tos1x rebuild are byte-identical. Recommended primary. |
| 1.62 UK (variant B) | 262144 | `D1C6F2FA` | `7f8148b382c75aa4877324dac0efa030c213f5e7de07217d3f840abb4c60c8f4` | RetroBIOS copy; differs from variant A in **exactly 2 bytes** (offset 87906: `00`→`10`; offset 158114: `2E`→`3E`). Unidentified patch/revision. |
| **1.62 US** | 262144 | `6372E5BD` | `1b284510424136a8426360f30a801f8c34272f97591d42d1214d110c57c21bb2` | atariworld and tos1x and RetroBIOS all identical. NTSC/60 Hz — wrong default for a 1040STE. |
| **1.62 DE** | 262144 | `2CDEB5E5` | `34f6640a015ab4ea98804e8365cf61155aa9e00b965c0de65b4d0f684d026d40` | identical at atariworld, tos1x, RetroBIOS. |
| **1.62 FR** (as shipped) | 262145 | `D3D32E88` | `fbd2e3edba4b04e56e11c192aae2a26d0590bde7321617bf9902bda11aa25caf` | **has one trailing `0xFF` byte** in every copy I found (atariworld and RetroBIOS are the same file). Strip it. |
| 1.62 FR (truncated to 262144) | 262144 | `0AB003BE` | `689c451df62a7bed0156a60a2c0579f63e3b59ada121e46d5d067942acbdfc7c` | matches the tos1x `tos162fr.img` rebuild exactly. |
| 1.62 SE | 262144 | `90F124B1` | `220fc9b35fd99908db9f9075fb3d850bf196d25741405ac6fa062facbbbd1583` | atariworld copy. |
| 1.62 SE (tos1x rebuild) | 262144 | `09494D80` | `77f9c88cbdf7127a7a6b61b2fa84ddde4d9a02a30f524a6bd87dc7deb250a575` | **does not match** the atariworld dump — a different Swedish build. Do not treat these as interchangeable. |

Sanity check before hashing: every TOS 1.62 image starts with
`60 2E 01 62 00 E0 00 30` (`BRA.S`, version word `0x0162`, load address `0x00E00030`).
A 262144-byte file that doesn't start `602e0162` is not TOS 1.62.

Zip-level hashes (useful for verifying the transfer, but they change if the site
rebuilds archives — verify the *inner* image hash, not the zip):

| Zip | Size | SHA-256 |
|---|---|---|
| `tos162uk.zip` | 109947 | `03ec9850b966b1655bbc649318f9d7c83305b719933e33035f3421b96553db04` |
| `tos162de.zip` | 109985 | `0a320ef3a82443dc2491ac8671e899417ca95b0705a811894db3a038eb823ee9` |
| `tos162fr.zip` | 110175 | `45c56c90f48def88dba505cc3e492e88cc1bd3ba4182ae50fe02fcefc45a5ae3` |
| `tos162se.zip` | 110041 | `7fbc5f550f661ec843464469383b99f2025681e6457b6499e890115fc35c5b6d` |
| `tos162us.zip` | 109939 | `0b36a8ccb9eaa0152b35f5c78e07c94cc77e9b56456fb9a7f39f755c26ebdacf` |

### 2.2 Sources table

| # | Source | URL pattern | Variants | Verdict |
|---|---|---|---|---|
| 1 | **atariworld.org** (zip in / zip out) | `https://www.atariworld.org/download.php?id=105` (UK) / `102` DE / `103` FR / `104` SE / `106` US → 302 to `/files/images/tos162{uk,de,fr,se,us}.zip` | UK, US, DE, FR, SE | **Verified 200**, `application/zip`. Inner names: `tos162uk.rom`, `TOS162US.ROM`, `TOS162DE.IMG`, `tos162fr.img`, `tos162se.img`. **No `Access-Control-Allow-Origin`** → unusable from a browser `fetch()`; needs a server-side fetch or a proxy. No licence statement. |
| 2 | **Abdess/retrobios** (raw GitHub, plain `.img`) | `https://raw.githubusercontent.com/Abdess/retrobios/<sha>/bios/Atari/ST/tos162uk.img` (also `tos162us.img`, `tos162de.img`, `tos162fr.img`) | UK, US, DE, FR | **Verified 200**, 262144 bytes, **`access-control-allow-origin: *`** → the only source usable directly from a web page. Note UK here is *variant B* (`D1C6F2FA`); FR has the 262145 quirk. Pin a commit SHA: `e9ef630b3afde71523b56c902bc79efda3abf4b4` (2026-10-28). Whole-repo download is large; use the raw file URL. |
| 3 | **th-otto/tos1x** (raw GitHub, rebuilt from Atari sources) | `https://raw.githubusercontent.com/th-otto/tos1x/7c6680d96642220710bddca825817d29ba86cdab/roms/tos162{uk,us,de,fr,se,cz,es,it,nl,sf,sg}.img` | **11** variants | **Verified 200**, 262144 bytes each, `ACAO: *`. These are *reconstructions* built from Thorsten Otto's TOS source tree, byte-identical to the retail UK/US/DE/FR dumps above, but not for SE. Not an "archive of dumps" legally — same copyright status, different provenance. Good as the symbol-map companion (§3.1). |
| 4 | **EmuTOS 1.4** (SourceForge) | `https://sourceforge.net/projects/emutos/files/emutos/1.4/emutos-256k-1.4.zip/download` | 19 languages, 256 KB | **Verified 200** (302 to `downloads.sourceforge.net` mirror). GPLv2 — the only entry here that is legal to redistribute. No CORS (SourceForge redirects to rotating mirrors), so bundle it rather than fetching it at runtime. |
| 5 | Internet Archive | search for `tos162`, `atari st tos rom` | — | **No TOS 1.62 item found** (archive.org advancedsearch returned 0 hits for `tos162`; the 32 `atari st tos rom` hits are magazines/newsletters). Do not rely on it. |

### 2.3 Language / region variants of TOS 1.62

- **Attested retail dumps in circulation: 5** — UK, US, DE, FR, SE (atariworld lists
  exactly these; Wikipedia documents 1.62 as a 2-chip 256 KB STE ROM dated 1 Jan 1990,
  <https://en.wikipedia.org/wiki/Atari_TOS>).
- tos1x additionally builds CZ, ES, IT, NL, SF (Swiss French), SG (Swiss German) — 11 in
  total. Those are reconstructions from the same sources, not confirmed retail dumps.
- Practical rule for the emulator: **UK is the default** (PAL 50 Hz, English desktop,
  matches the "1040STE in Europe" mental model); US is 60 Hz NTSC and visibly changes
  demo behaviour (NeoST's README makes the same point: `us` ⇒ 60 Hz, `uk/fr/de/es` ⇒
  50 Hz PAL).

### 2.4 Pitfalls found while verifying

1. **The FR image is 262145 bytes**, not 262144, in both sources — a stray trailing
   `0xFF`. Truncating to 262144 yields `0AB003BE` and matches the tos1x rebuild. A strict
   1 MB/256 KB size check will reject it; either accept `262144..262145` and truncate,
   or skip FR.
2. **"1.62 UK" is two different images** (2-byte delta). Hash-pin one; if a user supplies
   the other, accept it with a warning rather than failing.
3. **Zip hashes are unstable**, image hashes are stable. Verify after extraction.
4. **CORS**: only `raw.githubusercontent.com` sends `ACAO: *`. atariworld.org and
   SourceForge do not, so a browser-only build cannot fetch them — that's an argument
   for putting the fetch behind the project's own small proxy, or for restricting the
   in-browser fetch to the GitHub mirror.
5. GitHub raw has no auth requirement but is rate-limited per IP; a first-run fetch of
   ~256 KB is fine, a CI matrix hammering it is not.

---

## 3. Reverse-engineered TOS disassembly / symbol sources

The format that matters is Hatari's ASCII `.sym`, because that is what the ecosystem's
debuggers already parse (NeoST supports `.sym` too — `neost/README.md` §features).

Hatari's documented format (`hatari/doc/debugger.html:667-678`):

```
e01034 T random
e01076 T kbdvbase
e0107e T supexec
```

> "Where 'T' means text (code), 'D' means data and 'B' means BSS ... The hexadecimal
> address, address type letter and the symbol name are separated by white space. Empty
> lines and lines starting with '#' are ignored."

Addresses are absolute, so TOS symbols (`0x00e0xxxx`) need no relocation; EmuTOS symbols
work the same way ("unless the symbol names are for fixed addresses (like is the case
e.g. with EmuTOS)", `debugger.html:681-689`).

### 3.1 th-otto/tos1x — the only project with **TOS 1.62** symbols

- Repo: <https://github.com/th-otto/tos1x> (pinned: `7c6680d96642220710bddca825817d29ba86cdab`,
  2026-01-10). README: "These are the sources used to build orginal ROM versions of TOS
  1.04/1.06/1.62".
- Contents relevant to us:
  - `roms/tos162{cz,de,es,fr,it,nl,se,sf,sg,uk,us}.img` — built ROMs (262144 B).
  - `glue/tos162*.map` — **`nm`-style symbol maps per language**, e.g.
    `glue/tos162uk.map` (4190 lines). Types present: `990 T`, `1884 t`, `60 D`, `1 d`,
    `425 B`, `267 A`, `563 a`. Sample: `00e00000 T _os_entry`, `00e00002 t os_version`,
    `00e00008 T _os_beg`, `00000840 B gem_env`.
  - `listings/tos162de.s` (~3.5 MB) — full disassembler listings used during the work.
- Licence: **none**. GitHub API reports `license: null`, and there is no LICENSE/COPYING
  file. It is Atari-derived source: all rights reserved by the rights holder. Practical
  reading: the map files are as encumbered as the ROM itself.
- Convert map → Hatari `.sym` (this is the whole converter; ~3360 symbols for UK):

```python
import re
out = []
for line in open("tos162uk.map"):
    p = line.split()
    if len(p) != 3 or not re.fullmatch(r"[0-9a-f]{8}", p[0]):
        continue
    addr, typ, name = p
    m = {"T": "T", "t": "T", "D": "D", "d": "D", "B": "B", "b": "B"}.get(typ)
    if m:                                   # drop 'A'/'a' (absolute constants/enums)
        out.append(f"0x{int(addr,16):08x} {m} {name}")
open("tos162uk.sym", "w").write("\n".join(out) + "\n")
```

  Caveat: `B`/`D` symbols with low addresses (`0x00000840`, `0x0000093a`, …) are TOS's
  own RAM variables, valid only once TOS has booted. `T` symbols are ROM addresses and
  are valid immediately.
- Provenance caveat: the maps correspond to *tos1x's* build. For UK/US/DE/FR that build
  is byte-identical to the retail dump, so the map is correct; for SE it is not.

### 3.2 smonson78/tos-re — TOS 1.04 UK hand disassembly (not 1.62)

- Repo: <https://github.com/smonson78/tos-re> (pinned `d2502b580f6e3db89ec1e936fa5ff71366169279`,
  2021-11-26). README: "I am slowly hand-disassembling parts of TOS 1.4 UK (md5sum
  036c5ae4f885cbf62c9bed651c6c58a8)".
- Format: GNU `as` syntax (`tos-source.s`, `boot-source.s`), heavy symbol definitions in
  `extern.s`, a Python preprocessor to defeat GAS's instruction substitution, linker
  script `tos.ld`, plus `tos-dasm2.txt`, `TOS Memory Map.PDF`, `hardware.txt`.
- Its stated goal is a byte-identical rebuild of 1.4 UK — useful as a *methodology*
  reference and for 1.04, **not** a 1.62 symbol source. Repo also contains
  `tos104uk.img` and `tos206uk.img` (ROM binaries). No LICENSE file.
- Value to us: shows how to emit symbols from a disassembly, and its `extern.s` naming
  conventions are a reasonable vocabulary if we ever build our own 1.62 symbol set.

### 3.3 disastos/tos100fr — TOS 1.00 FR in Ghidra

- Repo: <https://github.com/disastos/tos100fr> (pinned `17d4c66b3987b6eee9ebedc4e304ebf4fef066ac`,
  2025-04-12). Contains `TOS100FR.gar` (Ghidra project archive), `ghidra_scripts/`,
  `internals.md`.
- README opens with: "This software is Copyright © 1985 Atari Corp. & Digital Research,
  Inc. It is now considered as abandonware." That "abandonware" framing is the repo
  author's assertion; it has no legal force (see §1.1 — the rights holder is identifiable
  and trading). Cite it as an example of community practice, not as a legal position.
- Value: a ready-made Ghidra database with annotated functions for one TOS version, and
  a template for producing a 1.62 equivalent.

### 3.4 czietz/ghidraScripts_for_Atari — tooling, not a disassembly

- Repo: <https://github.com/czietz/ghidraScripts_for_Atari> (active 2026-01). No licence
  file.
- `ImportAtariTOSROM.py` imports a TOS ROM into Ghidra and derives the address range from
  the header; it can also load an EmuTOS symbol file produced by EmuTOS's `map2sym.sh`.
  `system_variables.txt` gives the TOS system-variable names (from `tos.hyp`), which is
  the cheapest way to get meaningful names for the `0x4xx` RAM area.
- Value: if we ever need a 1.62 symbol set of our own, this is the pipeline (Ghidra +
  these scripts + tos.hyp names), and the output is *our* file rather than a copied one.

### 3.5 EmuTOS symbols — the only legally clean symbol source

- `emutos-1024k-1.4.zip` contains `etos1024k.sym` (**1850 symbols**, 45106 B, SHA-256
  `a89ec2c894c82000576a8dda52d04de14f181bb53412fbb5f4cb2c0aa2f1d59f`), already in Hatari
  format:
  `0x00000380 D _proc_lives`, `0x00000400 D _etv_timer`, … It is GPLv2 like the ROM.
- The **256 KB** package does *not* ship a `.sym` (verified: 19 `.img` files, zero
  `.sym`), so if the debugger wants symbols for the STE-sized image either take them
  from the 1024k build (they are the same code base but different addresses — do not mix)
  or rebuild EmuTOS and run its `tools/map2sym.sh`.
- This is the right symbols story for CI: free, redistributable, no ambiguity.

### 3.6 How the emulator should consume symbols

1. Look for `<rom-basename>.sym` next to the ROM (Hatari's convention: when a
   `<prgname>.sym` exists the debugger prefers it over the binary), plus an optional
   built-in default for EmuTOS.
2. Parse the 3-field ASCII format, ignore `#` comments and blank lines; keep duplicate
   addresses (aliases are common: `_memvalid` and `memvalid` share `0x420`).
3. Tag symbols by address range: `0x00E00000-0x00EFFFFF` → ROM, else RAM, and grey out
   RAM symbols until the first `GEMDOS`/`TRAP #1` after boot.
4. For a browser build, ship EmuTOS symbols in the bundle (small, GPL) and only load TOS
   symbols if the user's fetch produced them; never fetch tos1x maps by default, since
   they are as encumbered as the ROM (§3.1).

---

## 4. EmuTOS as the stand-in / default

| Property | Value | Source |
|---|---|---|
| Licence | GPLv2 ("no code written by Atari in EmuTOS") | <https://emutos.sourceforge.io/>; `emutos-256k-1.4/doc/license.txt` |
| Latest release | **1.4**, 2025-06-07 (`emutos-512k-1.4.zip` sha256 `1ef7bd25…d1663` per SourceForge `best_release.json`) | <https://sourceforge.net/projects/emutos/files/emutos/1.4/> |
| Project age | Started 2001, actively developed; 1.4 changelog lists Falcon true-colour, LBA48 IDE, new Catalan translation | `doc/changelog.txt` |
| **STE suitability** | 256 KB build readme: "These ROMs are suitable for the following hardware: **STe, Mega STe, emulators of the above**" — 19 single-language images (`etos256uk.img` = English PAL, `etos256us.img` = English NTSC) | `emutos-256k-1.4/readme.txt` |
| ROM size vs TOS 1.62 | **256 KB = 262144 B, exactly TOS 1.62's size** (1.62 is a 2-chip 256 KB STE ROM) | same + Wikipedia |
| Other sizes | 192 KB (ST), 256 KB (STe), 512 KB (**TT/Falcon**, not STe), 1024 KB (emulator-only, multilanguage, "not suitable for original Atari hardware") | readmes in each package |
| Self-test evidence | `doc/status.txt`: STe "yes" (2025-05-18), Mega STe "yes", Hatari v2.5.0 "yes", Steem SSE 4.1.2 R17 "yes" | `doc/status.txt` |
| Symbols | `.sym` in the 1024k package only | §3.5 |
| Known fidelity gaps | `doc/incompatible.txt` (543 lines) documents programs that break, largely due to "undocumented addresses or interfaces … or private TOS address heuristics"; STOS Basic input routines are the canonical example. Hatari's `doc/emutos.txt` keeps its own working/broken STE lists and notes "While 192k and 256k EmuTOS variants work better for some ST and STE programs, only the larger image variants contain support for all the hardware emulated by Hatari." | `doc/incompatible.txt`; `hatari/doc/emutos.txt:87-90` |

Assessment: EmuTOS is mature and is a *faithful enough* boot target for a 1040STE
configuration — the 256 KB images are explicitly built for that machine class, and the
STE hardware paths (DMA sound, blitter, extended palette) exist, which is why Hatari's
STE compatibility notes are meaningful at all. It is **not** a drop-in fidelity oracle
for TOS-specific bugs: anything that depends on undocumented TOS addresses, exact ROM
layout, the TOS 1.62 desktop's behaviour, or GEMDOS internals will diverge. That is
exactly the class of thing a "fixed TOS 1.62" test suite would be checking — so EmuTOS
covers *boot smoke tests and most application tests*, not *TOS conformance*.

Numbers for the decision:

| | TOS 1.62 UK | EmuTOS 1.4 256k UK |
|---|---|---|
| Size | 262144 | 262144 |
| CRC32 | `1C1A4EBA` | `10FFD4BD` |
| SHA-256 | `c0c2cf66…5ffb` | `3bfdc561eb193a8f65aa772169aa2aca241015a2bad9388a2fea3326b1b0aeb0` |
| Licence to redistribute | none | GPLv2 |
| Symbols | tos1x map (encumbered) | none in 256k package; 1024k `.sym` is GPL |

`etos256us.img` (for NTSC runs): CRC32 `4A05C52F`, SHA-256
`f1fe68360db345551231791edc6a7769e825edadf3632f59b10f3b2100ed75b3`.
`etos1024k.img`: 1048576 B, CRC32 `95644512`, SHA-256
`154803e3ff4851f3dc358b91b16d6b6110d187a81d8132ecb665af49878939d5`.

---

## 5. Recommended policy for STeemBrowser

### 5.1 What we ship, what we fetch

1. **Ship EmuTOS 1.4 256 KB (UK, and US for NTSC profiles) in the repo/bundle.** It is
   GPLv2, 256 KB, and gives a bootable STE with zero network access. This is the default
   ROM, the default for `npm test`, and the thing a first-run user sees if they decline
   everything else. Keep `doc/license.txt` alongside it and list it in the NOTICE file
   (GPLv2 requires the licence text and source availability — link to
   <https://sourceforge.net/projects/emutos/files/emutos/1.4/> in the about box).
2. **Never commit, bundle, or containerise TOS 1.62.** Not in the repo, not in release
   artifacts, not in test fixtures, not in Docker layers.
3. **Offer TOS 1.62 as an explicit, user-initiated fetch** — "Use a real TOS 1.62 ROM
   (optional)" — behind a modal that states the legal position (draft in §5.4) and
   requires an affirmative click. Default is "Stay on EmuTOS".
4. **Also accept a user-supplied file** (drag-and-drop / file picker). Many users own a
   1040STE and can dump it (TOSDUMP on real hardware — Hatari's manual documents this
   route). Validate the same way.

### 5.2 Where to fetch from, in order

1. `https://raw.githubusercontent.com/Abdess/retrobios/<pinned-sha>/bios/Atari/ST/tos162uk.img`
   — CORS-enabled (`ACAO: *`), exact 262144 bytes, works from a browser `fetch()`.
   Pin the SHA (`e9ef630b…` as of 2026-10-29); never fetch `main`.
2. Fallback (server-side only, or via the project's proxy): atariworld.org
   `files/images/tos162uk.zip` — verified, but no CORS and no licence statement.
3. Never attempt to scrape tos1x `roms/` for production use. It is fine for a
   developer-mode/symbol workflow, but it is a rebuilt copyrighted image from an
   unlicensed source tree, and its SE build doesn't match retail.

Whichever route is used, **the hash check is the contract**: the URL is just a hint.

### 5.3 Verification (do all four)

```bash
# 1. size
stat -c%s tos162uk.img            # 262144 (accept 262145 for FR, then truncate)
# 2. structural check
xxd -l 8 tos162uk.img            # 602e 0162 00e0 0030
# 3. hash (the gate)
sha256sum tos162uk.img
# 4. crc32 (what the Atari scene quotes, and what humans cross-check)
python3 -c "import zlib,sys;print('%08X'%(zlib.crc32(open(sys.argv[1],'rb').read())&0xffffffff))" tos162uk.img
```

Accept list (pin these in a small `tos-hashes.json`, which is facts, not copyrighted):

```json
{
  "1.62-uk-a": {"size": 262144, "sha256": "c0c2cf6668f08f0ddfe8558d33a68896363322e4530062c584ea2c64067d5ffb", "crc32": "1C1A4EBA", "default": true},
  "1.62-uk-b": {"size": 262144, "sha256": "7f8148b382c75aa4877324dac0efa030c213f5e7de07217d3f840abb4c60c8f4", "crc32": "D1C6F2FA", "warn": "variant differs in 2 bytes from the reference dump"},
  "1.62-us":   {"size": 262144, "sha256": "1b284510424136a8426360f30a801f8c34272f97591d42d1214d110c57c21bb2", "crc32": "6372E5BD"},
  "1.62-de":   {"size": 262144, "sha256": "34f6640a015ab4ea98804e8365cf61155aa9e00b965c0de65b4d0f684d026d40", "crc32": "2CDEB5E5"},
  "1.62-fr":   {"size": 262145, "sha256": "fbd2e3edba4b04e56e11c192aae2a26d0590bde7321617bf9902bda11aa25caf", "crc32": "D3D32E88", "trim_to": 262144}
}
```

On mismatch: **discard the bytes, do not run them**, show the expected vs actual hash and
the "supply your own dump" path. A wrong-language or truncated ROM boots into a subtly
broken machine — worse than no ROM.

Storage: browser → IndexedDB/OPFS, keyed by hash, never re-fetched. Desktop builds →
`<user data>/roms/tos162uk.img`, gitignored. Nothing goes to a server; the project
operates no ROM host.

### 5.4 Draft legal warning (first-run modal, verbatim)

> **About the Atari TOS ROM**
>
> TOS 1.62 is the operating system Atari shipped in the 1040STE. It is proprietary
> software: it was developed by Atari Corporation and is not open source. This project
> has no licence from the current rights holders (Atari Interactive, Inc. / Atari SA) to
> distribute it, and we do not host or ship it.
>
> If you continue, this app will download a 256 KB TOS 1.62 image from a third-party
> public mirror and verify it against a known SHA-256 checksum. The download happens on
> your machine and stays there. Whether you may download and use that image depends on
> the law where you live and on any licence you hold — for example, you may prefer to
> dump the ROM from a 1040STE you own. We can't advise you on that, and by continuing you
> accept responsibility for it.
>
> You do not need TOS at all: this app runs **[EmuTOS](https://emutos.sourceforge.io/)**,
> a free, open-source (GPLv2) TOS-compatible operating system, out of the box on the same
> 1040STE configuration. Some original software behaves differently under EmuTOS than
> under real TOS 1.62.
>
> [ Use EmuTOS (recommended) ]  [ Download TOS 1.62 and continue ]  [ I'll provide my own dump ]

Notes on the wording: it does not assert that downloading is illegal (it may not be,
depending on jurisdiction and on the user's own copy), it does not assert a licence we
don't have, it names the rights holder as the community understands it, and it gives the
free alternative first. Do not use "abandonware" — it is not a legal category and no
source supports it for TOS (§3.3).

Also put a short paragraph in README.md and in the About box: "This project ships no
Atari ROMs. EmuTOS is bundled under GPLv2. TOS 1.62 can optionally be fetched by the user
and is verified by SHA-256."

### 5.5 CI behaviour when the download is unavailable

Principle: **CI is never red because a proprietary ROM could not be obtained, and never
green because a test silently skipped the thing it claimed to test.**

1. **Default jobs (PR + main) run EmuTOS only.** All boot/regression tests take a ROM
   path from `$STEEM_ROM` (default: bundled `etos256uk.img`). No network in tests; add a
   test that asserts the fetch module is not invoked when `$STEEM_ROM` is set.
2. **TOS-dependent tests are a separate, opt-in target**: `STEEM_TOS162=/path/to/tos162uk.img npm run test:tos`.
   Without the env var the target prints
   `SKIP: TOS 1.62 test suite needs STEEM_TOS162 (proprietary ROM, not vendored)` and
   exits **0**. Document this in the test README so a green run is not mistaken for
   "TOS-tested".
3. **A dedicated nightly job** may fetch the ROM via the same code path as production
   (that also exercises the downloader and the hash pin), using a repo secret for the URL
   if one is ever needed. If the fetch fails or the hash mismatches, the job fails loudly
   and opens/updates an issue — it does not block merges.
4. **Never cache or upload the ROM** as a CI artifact, and never put it in the build
   cache keyed by branch. If a runner has it, keep it in an ephemeral path.
5. **Test the downloader without the ROM.** Commit a tiny synthetic fixture (e.g. 512
   bytes starting `60 2E 01 62`, deliberately wrong hash) and unit-test: size rejection,
   header rejection, hash mismatch → discard, duplicate-variant acceptance, FR trim. That
   gives real coverage of the risky code with zero legal exposure.
6. **Hash pinning is a test.** A unit test asserts every entry in `tos-hashes.json` is a
   64-hex SHA-256 and a 8-hex CRC32 with consistent sizes, so a typo in the table fails
   CI before a user hits it.
7. **Order of preference in code**: `$STEEM_TOS162` → user-provided file → download →
   EmuTOS. Log which one was used at the top of every run, including in CI output, so
   nobody has to guess which OS produced a failure.

### 5.6 Takedown readiness (cheap insurance)

- Keep the download URL table and the hash table in **one small module** with no other
  responsibility, so removing a source is a one-file change.
- Keep the EmuTOS path fully independent of it (it already is, by §5.5.1).
- If a mirror disappears: drop the URL, keep the hash (the hash is not infringing), and
  the "provide your own dump" path still works.

---

## Appendix: commands used to verify (reproducible)

```bash
# atariworld UK: follow the 302, hash the zip, then the inner image
curl -sIL -A "Mozilla/5.0" https://www.atariworld.org/download.php?id=105   # -> /files/images/tos162uk.zip
curl -sL -A "Mozilla/5.0" https://www.atariworld.org/download.php?id=105 -o tos162uk.zip
python3 -c "import zipfile,hashlib;d=zipfile.ZipFile('tos162uk.zip').read('tos162uk/tos162uk.rom');print(len(d),hashlib.sha256(d).hexdigest())"
# -> 262144 c0c2cf6668f08f0ddfe8558d33a68896363322e4530062c584ea2c64067d5ffb

# RetroBIOS (CORS-enabled), pinned commit
curl -sL https://raw.githubusercontent.com/Abdess/retrobios/e9ef630b3afde71523b56c902bc79efda3abf4b4/bios/Atari/ST/tos162uk.img | sha256sum

# CORS probe
curl -s -o /dev/null -D - -H "Origin: https://example.com" -r 0-0 \
  https://raw.githubusercontent.com/Abdess/retrobios/main/bios/Atari/ST/tos162uk.img | grep -i access-control
```

All fetches above returned HTTP 200/206 on 2026-10-29. Images were hashed and deleted;
none are retained in this repository.

## Sources

- Hatari manual, TOS installation section — <https://www.hatari-emu.org/doc/manual.html> (local: `hatari/doc/manual.html:459-479`)
- Hatari EmuTOS notes — `hatari/doc/emutos.txt` (sizes, STE notes, compatibility lists)
- Hatari debugger symbol format — `hatari/doc/debugger.html:667-694`
- Wikipedia, Atari TOS (licence field, 1.62 = 2-chip 256 KB, 1 Jan 1990) — <https://en.wikipedia.org/wiki/Atari_TOS>
- Atari 8-bit FAQ (Hasbro 1999 permission scope; corporate timeline) — <https://www.atariage.com/800/faq/>
- EmuTOS — <https://emutos.sourceforge.io/>; manual <https://emutos.github.io/manual/>; releases <https://sourceforge.net/projects/emutos/files/emutos/1.4/>
- NeoST README (ROM policy) — <https://github.com/habib256/neost>
- Steem SSE 4.2.0 R10 docs — `steemsse-V4.2.0_R10/steem/doc/TranslateTemplate.txt:2283`
- th-otto/tos1x — <https://github.com/th-otto/tos1x>
- smonson78/tos-re — <https://github.com/smonson78/tos-re>
- disastos/tos100fr — <https://github.com/disastos/tos100fr>
- czietz/ghidraScripts_for_Atari — <https://github.com/czietz/ghidraScripts_for_Atari>
- Abdess/retrobios — <https://github.com/Abdess/retrobios>
- atariworld.org TOS archive — <https://www.atariworld.org/tos-rom/>
- MiSTer AtariST core — <https://github.com/MiSTer-devel/AtariST_MiSTer>
- esp32ste ROM policy — <https://github.com/rlucencic/esp32ste/blob/main/roms/README.md>
