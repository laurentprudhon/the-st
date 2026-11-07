# 0010 — EmuTOS ships as the boot image; TOS 1.62 is a user-supplied file

- **Status**: accepted (owner, 2026-11-07) — every ruling below is quoted from the ticket.
- **Ticket**: #4 "Settle the TOS image provisioning policy" (`wayfinder:grilling`, HITL).
- **Amends**: the TOS sentences of `README.md`, `GLOSSARY.md` and the map's Notes, in place and with
  dated notes (see §9).
- **Supersedes**: §5.1–§5.6 of `research/tos-rom-sourcing.md` as *policy* — the research note stays
  canonical as evidence, and keeps its `unconfirmed` findings unchanged.

## 1. The rulings, verbatim

> we ship with emutos 1.4, but we must allow all languages variants available for PAL, and we display
> a message at first launch : "The original Atari STE operating system (TOS) is still copyrighted, so
> this emulator ships with a compatible open source operating system (EmuTOS). You can alternatively
> put your own TOS 1.62 image in the emulator directory."

> there is no switch to select the language : the emulator selects the ROM corresponding to the host
> operating system language, or uk if no corresponding ROM is available. we should also bundle the
> source code for EmuTOS 1.4 in the package, so that we can step into the code of the TOS instruction
> by instruction.

> A [all 13 PAL accept-list entries with provenance; reconstructions boot with a warning]

> EmuTOS-only, permanently offline; env-var-gated local TOS tests; no nightly job ever holds a TOS image

Plus, from the round-4 chain: the verification is a **hash accept-list**, refusals are loud and
specific, unknown dumps are refused rather than booted, and symbol tables are produced by
**rebuilding EmuTOS once and proving byte-equality** with the shipped images.

## 2. What ships

| Artefact | Size | Why |
|---|---|---|
| `etos256{ca,cz,de,es,fi,fr,gr,hu,it,nl,no,pl,ro,ru,se,sg,tr,uk}.img` — **18 PAL images** | 4 718 592 B | the boot image, offline, under GPLv2 |
| `emutos-src-1.4.tar.gz` | 1 299 705 B | debugging (step through EmuTOS), and GPLv2 source availability without a link |
| `emutos-1.4/LICENSE.txt` | — | GPLv2 text alongside the images; a `NOTICE` entry names EmuTOS, its version and its source |
| `etos256<lang>.sym` × 18 | — | generated from the rebuild (§6), shipped only once proven byte-identical |

`etos256us.img` (English, NTSC) is **not** shipped and never selected: the machine is PAL.

**No Atari ROM, slice of one, container layer or test fixture is ever committed, bundled, cached or
released.** That is unchanged and not part of this decision.

## 3. Language selection

No switch, no setting, no picker. Chosen at start-up from the host UI language
(`CultureInfo.CurrentUICulture`), falling back to `uk`:

| Host language | Image | Note |
|---|---|---|
| Czech `cs` | `etos256cz.img` | EmuTOS uses `cz`, ISO uses `cs` |
| Greek `el` | `etos256gr.img` | EmuTOS uses `gr`, ISO uses `el` |
| Swedish `sv` | `etos256se.img` | EmuTOS `se` is Swedish; ISO `se` is Northern Sami |
| Swiss German `de-CH` | `etos256sg.img` | no ISO 639-1 code exists |
| English, any region | `etos256uk.img` | EmuTOS `uk` is United Kingdom; ISO `uk` is Ukrainian |

Every other image is selected by its own language subtag (`fr`, `de`, `es`, `it`, `nl`, `pl`, `ro`,
`ru`, `hu`, `fi`, `ca`, `tr`). Two facts the rule must carry, both from the package readme:

- `no` and `se` **do not translate the desktop**: *"Selecting Norwegian/Swedish currently sets the
  language to English, but the keyboard layout to Norwegian/Swedish."*
- `en-US` maps to `uk`, because the NTSC image is unreachable on a PAL machine.

The per-image **keyboard layout** table this rule depends on is its own task ticket (§8): which keys
each national layout remaps, so host keystrokes reach the IKBD as the right scancodes.

## 4. TOS 1.62: a user-supplied file, never fetched

The user drops a TOS 1.62 image in the emulator directory. Nothing is downloaded, so there is no
mirror table, no fetch order, no CORS reasoning and no network code path at all — the largest
simplification in this ticket.

### 4.1 Accept-list (13 PAL entries, each with provenance)

| Entry | Size | SHA-256 | Provenance |
|---|---|---|---|
| 1.62-uk-a | 262144 | `c0c2cf6668f08f0ddfe8558d33a68896363322e4530062c584ea2c64067d5ffb` | retail dump |
| 1.62-uk-b | 262144 | `7f8148b382c75aa4877324dac0efa030c213f5e7de07217d3f840abb4c60c8f4` | retail dump, 2 bytes from A — boots **with a warning** |
| 1.62-de | 262144 | `34f6640a015ab4ea98804e8365cf61155aa9e00b965c0de65b4d0f684d026d40` | retail dump |
| 1.62-fr | 262145→262144 | `fbd2e3edba4b04e56e11c192aae2a26d0590bde7321617bf9902bda11aa25caf` → `689c451df62a7bed0156a60a2c0579f63e3b59ada121e46d5d067942acbdfc7c` | retail dump, **trailing `0xFF` must be stripped** |
| 1.62-se (retail) | 262144 | `220fc9b35fd99908db9f9075fb3d850bf196d25741405ac6fa062facbbbd1583` | retail dump (atariworld) |
| 1.62-se (rebuild) | 262144 | `77f9c88cbdf7127a7a6b61b2fa84ddde4d9a02a30f524a6bd87dc7deb250a575` | reconstruction — a **different build** from the retail dump |
| 1.62-cz | 262144 | `a8f8defb314cb7248f87df63a6c3f2d55614abff99fd04b90cf9ebd3b06bc56f` | reconstruction |
| 1.62-es | 262144 | `7e7ac73d7d5caeae617f641b9151f83f670f7782b070d17bf7c0b2a8bae5971f` | reconstruction |
| 1.62-it | 262144 | `7444102bf237bb39158731826d0c8615ae6de346a61b72e0fc268922fdb6bc94` | reconstruction |
| 1.62-nl | 262144 | `a92f8bf404f918ab22e9fc45601229f824c841f61870312cf2bf2a16749a0618` | reconstruction |
| 1.62-sf | 262144 | `9a5e8338eab196375e8cb0a925d5fa6e4a200ae639bb309a801a2cad18666d56` | reconstruction (Swiss French) |
| 1.62-sg | 262144 | `89863fc589125ec3f6ac52eb720a5358b55d8ff14ec44cda2d6d297ff18c5742` | reconstruction (Swiss German) |
| 1.62-us | 262144 | `1b284510424136a8426360f30a801f8c34272f97591d42d1214d110c57c21bb2` | retail, **NTSC — refused** |

Reconstructions are built from Atari's sources by `th-otto/tos1x`; they are TOS 1.62 in behaviour
but not attested retail dumps, and they boot with a visible one-line warning saying so. The table
lives in the repo as facts (`roms/tos-hashes.json`); the images never do.

### 4.2 Refusals — three rules, three messages

1. **Unknown hash** → refused, never booted, message shows expected vs actual SHA-256 and the file
   path. An unidentified patch boots a machine nobody can vouch for.
2. **Country code 0** (header byte `0x1D`) → refused as NTSC. Verified this session: `0x1D` is the
   only byte separating the US image from every other, and it carries the country code
   (`us`=0, `uk`=7, `de`=3, `fr`=5, `it`=0xB, `se`=0xD, `sf`=0xF, `sg`=0x11, `nl`=0x1D, `cz`=0x1F).
3. **262145 bytes, hash = `fbd2e3ed…`** → strip the trailing `0xFF`, re-hash, then apply 1.

Structural check stays as a cheap pre-filter (every valid image starts `60 2E 01 62`), but the
**hash is the contract**: a structurally valid image with an unknown hash is refused.

### 4.3 Symbols for a user-supplied TOS — derived, not asked

Nothing encumbered is shipped or fetched. The emulator loads `<rom-basename>.sym` if the user puts
one beside their image (Hatari's convention: 3-field ASCII, `#` comments, absolute addresses). The
`th-otto/tos1x` maps stay out of the project entirely: they are as encumbered as the ROM and there is
no download path to put them behind. *Flagged as derived from the settled constraints — overrule it
and this paragraph changes, nothing else does.*

## 5. The first-launch message

Wording is the owner's sentence, verbatim, placed in three surfaces: the first-launch modal, a
`README` §TOS paragraph, and the About box:

> The original Atari STE operating system (TOS) is still copyrighted, so this emulator ships with a
> compatible open source operating system (EmuTOS). You can alternatively put your own TOS 1.62 image
> in the emulator directory.

It asserts no licence and no legality either way, names no rights holder, and does not use
"abandonware". The draft in `research/tos-rom-sourcing.md` §5.4 is **superseded by this sentence** and
stays in the note only as the longer variant it was chosen over.

## 6. Symbols for EmuTOS: rebuild once, prove byte-equality

The 256 KB packages ship **no** `.sym`; the 1024k package's `.sym` is at Falcon/TT addresses and is
useless for an STE image. So:

1. Build the 256k set from `emutos-src-1.4.tar.gz` with the documented `m68k-atari-mint` GCC 4.6.4.
2. Assert each built image's SHA-256 equals the value in `roms/emutos-1.4-manifest.json`.
3. Convert that build's `emutos.map` to `.sym` per language and ship it beside the image.
4. "Step instruction by instruction" is then the CPU's single-step (#9/#10) plus these symbols plus
   the bundled source.

**Falsifier**: if a rebuilt image is *not* byte-identical, ship no symbols for it and record why —
never a symbol table whose addresses disagree with the bytes. If the whole set fails to match, the
fallback is to build and distribute our own EmuTOS images (ROM and symbols consistent by
construction), and that is a new decision. Status: `unconfirmed` until the task ticket runs it.

## 7. CI

EmuTOS-only, permanently offline. Every test boots an in-repo `etos256*.img`; a test asserts the
build opens no outbound connection. Tests that need real TOS 1.62 are gated on an environment
variable naming a local file; without it they print one line saying what is missing and exit 0, so a
green run is never mistaken for "TOS-tested". No CI job, cache, artefact or runner secret ever holds
a proprietary ROM.

## 8. Consequences and follow-ups

- **#10 (walking skeleton)**: it boots an in-repo EmuTOS image; its acceptance test is *boots to the
  GEM desktop*, never "runs demo X" — EmuTOS is not the compatibility oracle, and the demo corpus is
  precisely its weak class (`doc/incompatible.txt`: 25 named programs, five systemic failure classes).
  Packaging (§2) and the first-launch modal are #10's work, not this ticket's.
- **New `wayfinder:task` — prove the EmuTOS rebuild is byte-identical and emit the 18 `.sym` files.**
- **New `wayfinder:task` — compile the per-image keyboard layout table** (18 EmuTOS images + the 13
  accepted TOS entries), which §3 depends on and which the input path consumes.
- **`research/tos-rom-sourcing.md`**: §5.1–5.6 become evidence, not policy; §5.2 is superseded
  outright. Amended with a dated note, not rewritten.

## 9. Amendments made in the same commit

| File | Before | After |
|---|---|---|
| `README.md` §TOS | "The TOS image is never committed. It is obtained at first run…" | ships EmuTOS 1.4 (18 PAL images) + source; TOS 1.62 by user file only; dated note |
| `GLOSSARY.md` "TOS image" | "…which the project never distributes…" | the term now covers two artefacts: the shipped EmuTOS image and an optional user-supplied TOS 1.62; dated note |
| map #1 Notes, TOS bullet | "TOS: never committed. Obtained at first run…" | ships EmuTOS; TOS never distributed; dated note |
| `NOTICE` (new) | absent | EmuTOS 1.4, GPLv2, source bundled, no Atari ROMs |
| `roms/tos-hashes.json` (new) | absent | the 13-entry accept-list with provenance |
| `roms/emutos-1.4-manifest.json` (new) | absent | the 18 PAL images, sizes and hashes |
