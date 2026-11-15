# The Atari ST file-host registry

STATUS: **complete (v1)** — 2026-11-15, site-first sweep, five parallel channels (#57's successor pass).
Supersedes Part 3 of `research/st-download-sources.md`. Every row is a host that was **opened**, not a host
that was mentioned.

Why this file exists: the first pass searched twenty famous *titles* and found the hosts those titles happen
to be indexed under. The owner then ran one search on one title (*Vroom*, Lankhor 1991) and returned six
hosts that pass had missed. The method was the fault, so this pass enumerates by **channel** — aggregator
families, the scene tier, the national-language tier, Google-free discovery by file format and by list, and
the Internet Archive by API — and it reports the **stopping evidence** rather than a claim of completeness.

Evidence labels, unchanged and mandatory: **counted** (fetched and parsed, method stated), **stated** (the
source says so, quoted), **unverified**. `✓ verified by the lead` marks the figures I re-derived myself;
everything else carries the subagent's label and its method. Nothing copyrighted was downloaded: probes were
`HEAD`, 1-byte `Range` reads, and zip **central directories** read by tail range. Hosts that refuse a
scripted client are recorded as limitations and were not worked around.

## 0. Coverage, stated before the tables

| Question | Answer | How |
|---|---|---|
| Hosts probed across the five channels | **~330 distinct domains** | 67 aggregator + 221 community-list hosts + ~40 national + 181 discovery-surfaced + IA items |
| Hosts that actually serve ST bytes | **~45** | table in §1; each with an observed status |
| Hosts mapped with a countable inventory | **22** | §2 arithmetic |
| Hosts that serve **protected** formats (`.stx`/`.ipf`/`.scp`/`.hfe`) | **10** | §2.2 |
| Hosts publishing any checksum | **5** | IA (SHA1), PlanetEmu (CRC32), fujiology (MD5 manifest), atarijookie (SHA-256), MAME |
| Hosts with a **documented permission** from the rights-holder | **1** | `lankhor.net` (quote in §1.4) |
| Dead-but-widely-cited | **6** | §1.7 — `myrient.erista.me` is the dangerous one |
| Community link list: live hosts / serving bytes / dead | 221 probed → **215 live, ~28 serve bytes, 11 dead** | §1.3 |
| Discovery stopping point | last new host at query **#118**; **20 consecutive** queries with no new source | §5 |

The honest summary: **the scene's ST bytes are concentrated in a handful of archives, and the long tail is
gated, dead, PD-only or duplicate.** Five aggregators carry ~24,000 *entries* but share one TOSEC naming
set, so the union is far smaller than the sum; only PlanetEmu adds protected formats.

## 1. The registry

### 1.1 The archives that matter (countable, fetchable)

| Host | Serves | Formats seen | Count | Addressing | Download route + status | Checksums | Rights / robots |
|---|---|---|---|---|---|---|---|
| **ftp.untergrund.net** `/users/ltk_tscc/fujiology/` | menu disks **by crack group**, demos, diskmags, party archive | `.zip` → `.msa`/`.prg`/`.tos` | **14,644 files under `ST\`** ✓ verified by the lead (MD5 manifest `fujiology21.md5`, 6,478,205 B, **82,244 lines**) organised AUTOMATN 640, DBUG 555, ELITE 498 … | Apache autoindex `ST/<letter>/<GROUP>/<PROD>.ZIP` | plain GET, 200 | **MD5 manifest for all 82,244 files** | `robots.txt` = `Disallow: /` (stated); no licence |
| **archive.org `stream-ipf`** | "Atari ST IPF/SCP Flux Stream Images" — flux mirror of the atari-forum.com preservation thread | `.7z` flux bundles | **3,712 files / 100,408 MiB**, 3,643 `.7z` (counted) | `/download/stream-ipf/<name>.7z` | 200 | **SHA1 per file** | **filed under `softwarelibrary_apple`** — right content, wrong collection |
| **archive.org `1st-division-manager-1992-codemastersa`** ("Super Console X: Atari st") | full TOSEC-named ST set, one dir per title | `.st`, `.ipf`, `.stx` | **2,115 files / 1,905 MiB** — 1,500 `.st`, **285 `.ipf`**, 253 `.stx` (counted) | item dirs | 200 (`.st` 737,280 B) | SHA1 | mediakey `audio`/`opensource_audio` — wrong collection |
| **archive.org `playtime_atariST`** | No-Intro-style set, 1 zip = 1 `.st` | `.zip` | **1,518 zips / 507 MiB** (counted) | `/download/playtime_atariST/<TOSEC>.zip` | 200 (581,080 B sampled) | SHA1 | `softwarecapsules_consoles` |
| **archive.org `mean-18-the-ultimate-edition`** | "Atari ST Collection" (GamebaseST-derived) | `.zip` → 1–3 `.st`/`.stx` | **3,394 zips / 1,490 MiB** (counted) | flat | 200 | SHA1 | `open_source_software` |
| **archive.org `launchbox-atari-st`** | LaunchBox set, TOSEC crack zips | `.zip` | **2,716 zips / 986 MiB** ✓ (2,720 files total) | flat | 200 | SHA1 | `opensource_media` |
| **archive.org `atarist-stmenus`**, **`atarist-automation-dbug-cd`**, **`atari-st-collection-1997-cdr-alien-pompey-pirates`** | menu-disk **CD images** | `.iso` | 678.9 MB / 589.1 MB / 665.2 MB (counted) | ISO | 200 | SHA1 | `cdromimages` |
| **archive.org `softwarelibrary_atari_st_games`** | playable per-title `.st`, in-browser emulator | `.st` | **884 items**, exactly 884 `.st` ✓ | `advancedsearch` + `metadata/<id>` | 200 | **SHA1** | IA ToS; no licence |
| **archive.org `alvro-stx`** | STX-only collection | `.stx` | **411** loose (counted) | flat | 200 (382,800 B sampled) | SHA1 | `open_source_software` |
| **archive.org `atari-st-roms_202405`** | SPS/CAPS masters | `.ipf` | **420 `.ipf` + 2 XML dats** ✓ | flat | 206 on range | SHA1 + dat | none |
| **planetemu.net** | per-format TOSEC sets, the only format-faceted host | `.st .stx .ipf .scp .hfe .dim .stt .raw .prg .tos` | **14,001 game entries** ✓ (11,872 / 1,444 / 413 / 178 / 4 / 28 / 37 / 5 / 7 / 5) — **no `.msa` category** | `/roms/atari-st-games-<fmt>` → `/rom/<set>/<slug>` | POST `/download/rom` + quota | **CRC32 per file** ✓ (`e2ac6d05` for Vroom, `48d00935` for 10th Frame) | no robots problem; download needs a session |
| **edgeemu.net** | flat TOSEC zip tree | `.zip` of `.st` | **311** links (counted, 27 letter pages) | `/browse/atari-st/<letter>` → `/download/atari-st/<TOSEC>.zip` | **200, `application/octet-stream`** ✓ (47,529 B sampled) | none | `robots.txt` **404** |
| **sync.wtf/archives/** | Greaseweazle flux dumps of retail disks, per collection | **`.scp` 2,552, `.msa` 1,895, `.stx` 659** | **5,106 files / 14 collections** (counted) | `/archives/collections/<c>.html` → `…/<c>/scp/NNN.scp` | 200 | none per file | hidden from the site root; `/releases/` 403 |
| **atarilegend.com** `/storage/zips/menus/<id>.zip` | menu-set zips, one per set | `.zip` → `.msa` | **316 sets**; 3,822 archives appear in the jookie list ✓ (one 200 / 786,548 B) | `/menusets/<id>` + browser emulator | 200/206 | none | restrictive robots on search paths |
| **exxosforum.co.uk** | Automation, Pompey Pirates, Medway-adjacent, ST Format coverdisks + wonder disks, **Floppyshop PDL** | `.zip` → `.msa`/`.st` | Automation **561** archives; Pompey **131 + 108 fixed**; STF **69 of 86**; wonder disks **32**; Floppyshop **4,072 disks** (all counted) | static indices; Automation dir 403 | files 200 | none | attachment route `dl.php` captcha-gated |
| **ftp.pigwa.net** (`/stuff/collections/`) | "Atari FTP Archive" — the largest plain-HTTP Atari index found | `.7z`, `.zip`, `.st` | directories **`Atari ST Games HD/`**, **`Atari ST game packs/`**, `AtariSTarchive/`, `ROMs - Atari - ST - Compilations - Demos…`, `atarist_axelf/` ✓ (dirs verified by the lead); `Games_HD.7z` **617,712,345 B** (counted) | direct paths | 200 | none | `robots.txt` 404 |
| **ataristdb.sidecartridge.com** | public floppy DB, 2025 | `.st` | **3,409 unique rows / 34 CSV shards** ✓ | `/db/<letter>.csv` → `<GROUP>/<FILE>.st` | 200 `binary/octet-stream` | none (columns: name;0;;;group;path) | `robots.txt` 404 |
| **atarimania.com** `/st/dumps/` | multi-format dumps per title, ST Format coverdisks | `.zip` → `.scp`+`.stx`+`.hfe`+`.raw`+`.hxcstream`+`.png` | 9,988 ST game entries ✓; **STF 86/86** (counted) | `/games/atari-st-games-<slug>-<id>`; dump href **arbitrary** | 200 | none | `/api/`, `/search` disallowed |
| **downloads.atari-home.de** (DE) | PD library + scene demos, real Apache listings | `.st .zip .msa .lzh` | **9,986 files / 70 series dirs** (8,472 `.st`, 1,469 `.zip`, 45 `.msa`) + 740 demos (counted) | `/Public_Domain/<Serie>/` | 200 (755,712 B sampled) | none | no robots |
| **oldgames.sk** (SK/CZ) | retail ST games, per-image metadata | `.st` **and `.stx`** | **281 ST games** (counted, 4 index pages) | `/en/game/<slug>/download/<id>` → `/download?uuid=<u>` | 200 `application/zip` | none; page prints **geometry** (`880 kB, double sided, 11 sectors/track`) | robots "Access Forbidden" |
| **d-bug.mooo.com** | D-Bug/Automation menus + HD patches | `.msa` | **327 menus (1–200)** (counted) | `/page/menus/?s=&e=` | 200 | none | per-menu crack credits |
| **spiny.org** (Medway / M.L.P.) | Medway Boys + M.L.P. menus | `.zip` → `.st`; bare `.ST` for MLP | **117 zips / 112 menus** and **84 `.ST`** (counted) | static pages | 200; `/disks/` 403 | none | — |
| **klapauzius.net** (Klaz) | individually cracked retail games | `.zip` | **85 zips** (counted) | `Old_Games.html` | 200 | none | — |
| **atari.8bitchip.info** (Putnik) | HD/HxC-adapted builds | `.ZIP` | **142 game pages** (counted) | `ASTGA/astgam.php?s=` | 200 | none | — |
| **page6.org** `/st_lib/` | Page 6 PD library + ST NEWS + FASTER diskmags | `.zip` | **1,108 disk pages** (counted) | `standard/stNNNN.php` | 200 | none | ignores `Range` |
| **st-news.com** | ST NEWS diskmag | `.st` | **51 issue pages** (counted) | `/uploads/ST-News/issues/…` | 200 | none | — |
| **ftp.lip6.fr** / **ftp.funet.fi** / **ftp.fi.muni.cz** / **atariftp.czietz.de** | classic FTP mirrors, **frozen** (lip6 index dated **1996**, funet self-declared) | `.zip .lzh .zoo .arc .msa .tos` | **2,996** files (lip6, counted from `0all.txt.gz`) / **2,158** described (funet) / 29 Czech PD (muni) | `0all.txt.gz`, `0index.ALL`, Apache | 200 | none | historical |
| **lankhor.net** | **publisher's own ST builds** | `.zip` → `.ST`/`.PRG` | **15 ST titles** ✓ | `/jeux/jeu/jeu<id>-atari.zip` | **200** ✓ (2,403,918 B for Vroom: `VROOM.ST`, `FORMULA1.PRG`) | none | **the only documented permission** — §1.4 |
| **computeremuzone.com** (ES) | ST zips per ficha | `.zip` | ST subset not enumerable (no filter, JS search); **1 verified** | `/ficha/<id>/<slug>?sec=atari` → `/download.php?ind=` (302) | 302 → 200 (3,784,525 B) | none | **robots disallows `/download.php` and `/*.zip$`** |
| **16bit.pl** (PL) | per-title zips via presigned S3 | `.zip` | **24 ST pages** (counted) | `/gra/<slug>` → `download.php?uuid=` → CDN | page 200; CDN **429** | none | content-signals file |
| **romspedia.com** | TOSEC-named roms, **direct file server** | `.zip` of `.st` | **2,813 entries** (counted, 60 pages) | `/roms/atari-st/<slug>` → `/download` | **200 `application/zip`, 184,588 B** ✓ (page emits the URL; it needs percent-encoding) | none | `Disallow:` (allow-all) |
| **wowroms.com** | TOSEC-named roms, split by format in the slug | `.st` **4,261** + `.stx` **1,167** | **5,430 counted** (6,289 stated) | `/en/roms/atari-st/<slug>/<id>.html` | 200 HTML gate, client-side reveal | none | **no robots policy (soft-404)** |
| **emuparadise.me / .org** | TOSEC-named `.st` set | `.zip` of `.st` | **8,400** (byte-identical on both domains) | `/Atari_ST_ROMs/<TOSEC>/<id>` | 200 HTML + `setTimeout` | none | `Allow: /` |
| **retrostic.com** | TOSEC-named set | `.zip` of `.st` | **8,368** (counted from `sitemaps/roms1.xml`) | `/roms/atari-st/<slug>-<id>` | 200 "Invalid session" | none | allow-all |
| **theoldcomputer.com** | TOSEC-named set | `.zip` | **1,026** (counted) | `getfile.php?file=<base64>` | **paid wall** ("U Unlimited User") | none | blocks GPTBot; `getfile.php` allowed |
| **emu-land.net** (RU) | TOSEC set incl. `[STX]`-labelled files | `.zip` → `.st`, `.stx` | unverified (12 pages for letter V) | `?act=getmfile&id=` AJAX | **Anubis proof-of-work wall** | none | **disallows GPTBot/ClaudeBot/anthropic-ai by name** |
| **atari-st-abandonware.neocities.org** | hand-kept index | Drive IDs | **121** distinct file IDs (counted) | Google Drive links | index 200; files on Drive | none | n/a |

### 1.2 Tier T2 — reachable, terms-restricted (explore during the dev phase, private cache only)

> **Amendment 2026-11-15 (owner).** These hosts are **no longer discarded**. Owner's ruling: *"during the
> emulator development phase, we want to build a private cache of everything that is available online : so
> don't discard the sources behind logins or robots.txt yet - we will explore them now, and then see if they
> are useful or not"*. The rows below keep the **term each host states**, because the term still travels with
> the fetch, and the end-state is unchanged: real users fetch one title at a time, on request.
> Access tiers: **T1 open** (crawl freely) · **T2 terms-restricted** (this table: explore, record the term,
> private cache only) · **T3 account-gated** (owner's own logged-in session; no credential entry, no bot-wall
> circumvention).

| Host | Why | Recorded as |
|---|---|---|
| `myabandonware.com` | real ST "Game file" (5 MB for Vroom) at `/download/lwpc-vroom` — **`robots.txt` disallows `/download/`**, endpoint 303s without a session | T2: explore with a real session, record the term |
| `computeremuzone.com` | files reachable (302 → zip) but robots disallows `/download.php` and `/*.zip$` | T2: counted, fetch only behind the owner's word |
| `exxosforum.co.uk` Floppyshop `dl.php` | captcha/session gate | catalogue countable via `floppyshop_info_dump.txt` (3,460,310 B) |
| `theoldcomputer.com` | paid membership wall | count only |
| `emu-land.net` | Anubis PoW **and** robots naming AI crawlers | T3: needs a real browser session; count unverified |
| `retrostic.com`, `wowroms.com`, `emuparadise.me` | HTML interstitials/session gates, no file URL in the response | T2: catalogue counted, bytes need a browser session |
| `atari-forum.com` | Cloudflare 403 to clients; guests see no attachment links | T3: owner's session only; its binaries also reach IA as `stream-ipf` |

### 1.3 The community link list, harvested in full (the coverage proof for the scene tier)

`sites.google.com/view/ataritosser/links` → **253 external anchors → 221 distinct hosts**, all probed
(counted):

| Result | Hosts | Meaning |
|---|---|---|
| `200` | 96 | live, full body |
| `206` | 100 | live, honours range |
| `416` | 3 | live, rejects ranges |
| `403` | 11 | live but bot-gated (demozoo, atari-forum, atariage, umich, lowresmag …) |
| `404` | 5 | gone at that path |
| `000` | 5 | DNS failure / refused |
| `500` | 1 | server error |

**Only ~28 of 221 (13 %) serve ST bytes.** The rest are hardware shops, emulator pages, museums and
brochures. **11 are dead**, including two the seed list implied were scene sources: `pacidemo.planet-d.net`
and `equinox.planet-d.net` (connection refused).

### 1.4 The one documented permission

`lankhor.net/mentions.php`, quoted (✓ verified by the lead):

> « Les jeux présents sur le site sont la propriété de la société Lankhor. Lankhor.net dispose seulement
> d'une autorisation pour proposer les anciens jeux édités par Lankhor au téléchargement »

An **authorisation**, not an abandonware claim. It is the only ST file source in either sweep where the
rights-holder is the distributor — and it was on nobody's list.

### 1.5 Dead, parked or gutted (recorded so no harvester builds on them)

| Host | Status |
|---|---|
| **`myrient.erista.me`** | **shut down**: every path, including specific TOSEC `[STX]` zips, returns the same 2,334-byte page announcing closure (31 March 2026). Its URLs are widely indexed — **the single most dangerous source to cite** |
| `nicoblog.org`, `romsplanet.com`, `romsmania.com`, `rom-world.com`, `romsdownload.net`, `romsgames.com` | parklogic parking pages |
| `romsmania.cc` | DNS resolves, TLS never completes from this host |
| `owroms.com`, `d-bug.me`, `emutopia.com`, `pacidemo.planet-d.net`, `equinox.planet-d.net`, `retromaniax.gr`, `hispatari.org`, `atari.hu`, `retroportugal.pt`, `planetaretro.pt`, `atarist.it`, `mundoabandon.com`, `retromaniak.eu`, `abandonwaremag.com` | DNS fail / refused / parked |
| `demobase.st` | **not a file server**: a Windows `.exe` downloader |
| `st-computer.net` | domain reused by a French tech blog |
| `atari-portal.de` | WordPress maintenance stub |
| `pgm` claimants: `coolrom`, `vimm`, `romsfun`, `romulation`, `romhustler`, `romsgames`, `freeroms`, `emulator.games`, `romsgo`, `romsbase`, `emurom.net`, `playclassic.games`, `retrogames.cc` | **no Atari ST section at all** (path 404 or zero `atari` strings in sitemap) |

### 1.6 The national tier, per country

| Country | Serves ST bytes | Host | Note |
|---|---|---|---|
| FR | **yes** | `lankhor.net` | 15 titles, rights-holder authorised; **`abandonware-france.org` has no ST axis at all** (platform filter is boot/dos/w3/w95/mac), `retroarchives.fr` and `obligement.free.fr` are metadata only (the latter is Amiga) |
| DE | **yes** | `downloads.atari-home.de` | 9,986 PD files, zero protected formats; `atariuptodate.de`, `stcarchiv.de`, `mogelpower.de` metadata only |
| SK/CZ | **yes** | `oldgames.sk` | 281 games, `.st` **and `.stx`**, prints disk geometry; `ftp.fi.muni.cz` 29 Czech PD |
| ES | yes, disallowed | `computeremuzone.com` | robots conflict |
| PL | yes, throttled | `16bit.pl` | 24 pages, CDN 429 |
| NL/BE | one bulk archive | `atarimuseum.nl` | 129,647,620 B club PD zip |
| RU | exists, walled | `emu-land.net` | TOSEC + STX, Anubis + AI-crawler robots |
| **IT, PT, HU, GR** | **no host** | — | four Italian catalogue/review sites checked; all metadata only |

## 2. The arithmetic

### 2.1 Internet Archive — the only layer with per-file hashes everywhere

| Category | Count | Basis |
|---|---|---|
| Loose ST-family images in Atari-labelled items | **4,671** | `.st` 2,849 · `.stx` 906 · `.ipf` 840 · `.msa` 53 · `.scp` 15 · `.hfe` 8 (counted over 994 items) |
| Images inside zips/7z on the same items | **≈9,000** | 36/36 sampled zips contain `.st`/`.stx`; `playtime` 1,518 + `launchbox` 2,716 + `mean-18` 3,394 = 7,628 zips |
| **Defensible floor for IA total** | **≈13,700 images** | loose + zip count; the opaque 33 GB / 20 GB / 8.5 GB blobs are unopened, so the real figure is plausibly 20–25k |
| Largest single flux mirror | **3,643 `.7z` / 100 GB** | `stream-ipf` |
| **Not anonymously fetchable** | 67 romsets / 21.38 GB | `tosec-main` is in the `loggedin` collection — every download returns **403** |

### 2.2 Protected formats across the whole registry

`.stx` — PlanetEmu 1,444 · wowroms 1,167 · IA 906 loose (+253 in Super-Console item, 411 in `alvro-stx`)
· sync.wtf 659 · oldgames.sk (some) · emu-land (some).
`.ipf` — PlanetEmu 413 · IA 840 loose (+285 in Super-Console) · `atari-st-roms_202405` 420.
`.scp` — sync.wtf 2,552 · IA 15 loose · `stream-ipf` 3,643 bundles · PlanetEmu 178.
`.hfe` — PlanetEmu 4 · Atarimania bundles.
**`.msa` has no first-class host**: no PlanetEmu category, no aggregator section; it exists inside menu
disks, `sync.wtf` (1,895), `downloads.atari-home.de` (45) and PD/menu CDs.

### 2.3 What the joins would be worth (nobody has done them)

- **Menu disks**: `atarijookie`'s list is **4,381 images / 305 sets / SHA-256**, drawn from only **two**
  hosts (Atari Legend 3,822 + exxos Automation 561). Fujiology holds **14,644** ST files (group-organised),
  D-Bug 327 menus, Pompey 239, M.L.P. 84, `sync.wtf` 5,106 retail-disk images, Floppyshop 4,072 PD disks.
  **These six inventories overlap in content and nobody has joined them** — that join is the difference
  between "a list of archives" and a catalogue.
- **Coverdisks**: Atarimania has **86/86** ST Format; exxos has **69/86** + 32 wonder disks. Neither alone is
  complete (Exxos 404s `stf38`, Atarimania serves it) — a real redundancy proof.

## 3. Invariants a harvester must encode (learned the hard way)

1. **The `collection` field on IA is necessary but not sufficient, in both directions.** `stream-ipf`
   (100 GB of ST flux) sits in `softwarelibrary_apple`; the Super-Console ST set sits under
   `opensource_audio`; and `ataridiskimages` *contains* Amiga items. Decide ST-ness by **extension
   allow-list + container size sanity**, never by collection name alone.
2. **TOSEC names are shared infrastructure, not shared ownership.** Six aggregators on six different hosts
   carry byte-identical TOSEC identifiers; their entry counts (8,400 / 8,368 / 5,430 / 2,813 / 1,026) differ
   only by variant de-duplication. Harvesting one ≈ harvesting any.
3. **A file is not an image, and a URL is not a file.** One Atarimania URL is a 142 MB zip of four
   renditions plus 166 sidecars; romspedia's page emits a direct URL that needs percent-encoding; PlanetEmu's
   download is POST + quota; emuparadise's is a `setTimeout`; retrostic's needs a session; theoldcomputer's
   needs money; emu-land's needs a browser and forbids crawlers.
4. **Slug guessing fails at every large host.** Atarimania (`navy_moves.zip` vs
   `f-19_…_uk_1990.zip`), wowroms (`-stx` in the slug), oldgames.sk (rotating uuid), 16bit.pl (presigned
   S3), CEZ (`?ind=`). Enumerate from an index; never construct.
5. **A "dead" host is often a client-side TLS problem.** Six live hosts returned `curl` exit 60 until `-k`;
   their certificates were valid on inspection. Record the client limitation, not an outage.

## 4. What this changes for the catalogue (#52) and the disk path (#29)

- **The corpus is bigger than the title sweep suggested and differently shaped**: the mass is menu disks,
  PD libraries and flux mirrors (fujiology 14,644 · sync.wtf 5,106 · Floppyshop 4,072 · IA ≈13,700), while
  *retail originals* are a small, well-hashed minority (IA `.ipf` 840 · `alvr-stx` 411 · PlanetEmu 413 IPF
  / 1,444 STX).
- **Protected formats are available at scale only on IA, PlanetEmu, sync.wtf and `stream-ipf`** — four
  sources, three of which are cheap to walk (IA API, sitemap-driven PlanetEmu listing, per-collection
  `sync.wtf` pages). #29's floor decision can be answered against those four.
- **Drift detection is possible for maybe a fifth of the corpus**: SHA1 on IA, MD5 on fujiology, CRC32 on
  PlanetEmu, SHA-256 in the atarijookie list. Everything else is size-and-presence (§0).
- **One host is lawfully distributable by its rights-holder** (`lankhor.net`) — the corpus's only clean
  licence position besides PD libraries. Worth modelling as a *kind of source*, not an exception.

## 5. Stopping evidence

The discovery channel ran **141 queries** (138 returning results) across nine families — format-led
(`".stx" download`, `".msa"`, `".ipf"`, `".scp" flux`), object-led in nine languages, list-led, mirror-led
(`<host> mirror|alternative` for 20 hosts), dead-list-led, ccTLD operators, and file-locker/Drive/usenet
angles. **181 distinct domains surfaced; 156 absent from both baselines.** The last query that introduced a
new file-serving host was **#118** (`atari st floppy archive download ftp` → `ftp.pigwa.net`); the following
**20 consecutive queries produced no new source**, and the final 12 were deliberate redundant re-phrasings.
The query log, per-query new-domain counts and resume instructions are in
`/opt/data/cache/scratch/sweep2/discovery.md`.

Structural limits, stated rather than hidden: the search backend **does not support `filetype:`** (three
queries, zero results), so extension-led discovery was done with quoted literals; download gates on
romspedia/wowroms/Floppyshop/CEZ were **not** defeated; `emu-land`, `atari-forum`, `demozoo` and
`devuego.es` refuse scripted clients; and the opaque multi-GB IA blobs were counted but not opened.

**Therefore: this registry is exhaustive for *domain discovery by query and by link list*, and not
exhaustive for *inventory inside the archives already found*.** Further progress is crawling (pigwa's 61
directories, edgeemu's 311 links, Floppyshop's 4,072 disks, fujiology's 14,644 files) and the archive joins
in §2.3 — both are #53's work, not more searching.

## 6. Verification log (what I re-derived myself, 2026-11-15)

| Claim | My probe | Result |
|---|---|---|
| fujiology ST tree = 14,644 files | downloaded `fujiology21.md5`, parsed paths | **82,244 lines, 14,644 under `ST\`** ✓ |
| romspedia serves a direct zip | extracted the URL from the download page, HEAD with percent-encoding | **200, `application/zip`, 184,588 B** ✓ |
| edgeemu's file route | `/download/atari-st/Virus%20%28Europe%29.zip` | **200, `application/octet-stream`, 47,529 B** ✓ |
| pigwa has ST trees | listed `/stuff/collections/` | **`Atari ST Games HD/`, `Atari ST game packs/`, `AtariSTarchive/`** ✓ |
| lankhor ships its own ST build | `jeu32-atari.zip` + `mentions.php` | **200, 2,403,918 B**; permission quoted ✓ |
| PlanetEmu CRC32 | title page HTML | **`rom-detail__files-hash` = `e2ac6d05`** ✓ |
| Atari Legend serves menu zips | `/storage/zips/menus/181.zip` | **200, 786,548 B** ✓ |
| IA counts 884 / 420 / 2,716 | advancedsearch + metadata | ✓ (earlier pass, still current) |

Three of my first probes 404'd because I guessed filenames from the report rather than reading the index —
recorded here because it is the same mistake the registry warns harvesters about (§3.4).
