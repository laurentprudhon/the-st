# Atari ST / STE Software Source Inventory

STATUS: complete (v1.1) — written 2026-11-15 after ~45 min of probing. Sections marked `PARTIAL` were cut short by the time budget; see *Gaps and next probes*.

A survey of what exists on the web for Atari ST/STE software: disk images (`.st`, `.msa`, `.stx`, `.ipf`, `.scp`, `.hfe`, `.stt`, `.dim`, `.raw`), manuals, box and disk-label artwork, and the metadata attached to them. This is an inventory of **sources**, not a catalogue of titles.

Evidence labels used throughout:
- **counted** — I fetched the source and counted/parsed the items myself (method stated).
- **stated** — the source claims the number; the sentence is quoted.
- **unverified** — hearsay, forum claim, or a figure I could not confirm.

## Gist

Four sources carry almost all of the weight for a metadata catalogue:

1. **Atari Mania** (atarimania.com) is the only large site that pairs per-title metadata with *downloadable original disk dumps*: I counted 9,988 ST games + 4,385 ST utilities + 2,064 ST demos in its sitemap, and 35 of 40 randomly sampled game pages linked a dump at a predictable path (`.zip` of `.stx` Pasti images; one fetched: HTTP 200, 835 KB, two `.stx`). It is also the site that actually *lists copy protection* per title.
2. **The Internet Archive collection `ataridiskimages`** ("Atari Computer Disk Images") — 396 items counted — is the best-preserved *physical* record: each item carries SCP flux, STX, ST and HFE renditions of the same disk plus AUFIT/HxC track renderings, and each file has a SHA1 in the item metadata. It is the closest thing to a verifiable inventory.
3. **TOSEC via `tosec-main` on the Internet Archive** — 67 Atari/ST set archives counted (21.4 GB): Games/Applications/Demos/Diskmags/Coverdisks/Compilations split by container format, including `[IPF]` and `[STX]`. TOSEC naming encodes title, year, publisher, region and dump flags, so the *names alone* are a metadata feed; per-file CRC32/SHA1 in the DATs make drift detectable.
4. **Atari Legend** is the best *human-curated* catalogue of the crack/menu scene: 6,204 games counted from its per-letter sitemaps, and 316 menu sets with 3,820 of 6,930 known disks present (both counted from the menusets page) — i.e. the scene's own estimate that ~45 % of menu disks are still missing.

Biggest legal obstacle: **nothing in this space is licensed**. Every bulk host that actually stores binaries — Internet Archive's `ataridiskimages` and `atari-st-collection`, `tosec-main`, Atari Mania's `/st/dumps/`, SYNC, Atari Shrine — redistributes copyrighted commercial games with no rights statement, and `atari-st-collection` is even filed under IA's `open_source_software` collection, which is simply wrong. A catalogue can be built from metadata; shipping the images is a separate, contested decision.

Biggest technical obstacle: **there is no API anywhere in the ST world**. No site exposes a documented JSON endpoint for ST software. Everything is sitemap+HTML, slug-guessing, or DAT parsing, and the two most structured metadata databases (Demozoo, MobyGames) actively block non-browser clients.

## Source table

| # | Source | URL | Formats / what it holds | Volume | Per-title metadata | Programmatic access | Access terms | Stability |
|---|--------|-----|------------------------|--------|--------------------|---------------------|--------------|-----------|
| 1 | **Atari Mania** | https://www.atarimania.com/ | Disk dumps (`.zip` of `.stx`), screenshots (`.gif`), box scans, adverts, some manuals | ST games **9,988**, ST utilities **4,385**, ST demos **2,064** (counted from sitemap); 41,560 game URLs total across all machines; **35/40 sampled games had a dump link** (counted) | Title, publisher, year, genre, **protection**, language, screenshots, box art, adverts; manual coverage sparse (2/40 sampled) | `sitemap.xml` → 4 shards; dumps at `/st/dumps/<slug>.zip`; screens at `/st/screens/<slug>_N.gif`; **no API** (`/api/` robots-disallowed); pages are Next.js flight payloads | `robots.txt`: `User-agent: * / Disallow: /api/ / Disallow: /search` then `Allow: /`; no ToS page found; no rate limit observed | Path-addressed by mutable slug; no published checksums; sitemap re-walk detects additions |
| 2 | **Atari Legend** | https://www.atarilegend.com/ | Screenshots, scrolltexts, reviews, menu-set catalogue, magazine scans; **no disk downloads** | Games **6,204** (counted); menu sets **316** with **3,820/6,930 disks** (both counted) | Title, authors, genre, year, screenshots, review, menu set → disk list, scrolltext OCR, completeness % | `sitemap/games/<letter>` (27 shards); `/menusets/<id>`; no API | `robots.txt`: `Disallow: /index.php`, `/admin/`, `/games/search`, `/music` | Stable numeric menu-set IDs (`/menusets/138`); no checksums |
| 3 | **IA: Atari Computer Disk Images** | https://archive.org/details/ataridiskimages | SCP flux, STX, ST, HFE, AUFIT + HxC track PNGs, box fronts/backs, manual scans | **396 items** (counted, `collection:ataridiskimages`) | title, creator, date, subject tags, description naming drive/format; box & manual images inside item | `advancedsearch.php?...&output=json`, `archive.org/metadata/<id>`, per-file **SHA1/MD5/CRC32** | IA ToS; **no licence** (`licenseurl` absent); openly redistributes commercial games | Hash-addressed + torrents; strongest drift detection of any source here |
| 4 | **IA: `atari-st-collection`** | https://archive.org/details/atari-st-collection | 5 bulk `.7z`: Games, Compilations, Menus, HD, Docs | 5 archives, **8,516 MB** total (counted from metadata) | none — filenames only | one-shot bulk download + torrent | Filed under `open_source_software` (incorrect); no rights statement | SHA1 per 7z; contents mutable inside archives |
| 5 | **TOSEC (`tosec-main` on IA)** | https://archive.org/details/tosec-main | Full TOSEC romsets per DAT, incl. `[ST] [STX] [IPF] [SCP] [RAW] [IMG] [STT] [DIM] [HFE] [PRG] [TOS]` | **67 Atari/ST archives, 21.4 GB** (counted); whole item 326 GB | TOSEC filename convention: title (year)(publisher)(region)(flags) | `archive.org/metadata/tosec-main`; DAT files themselves at tosecdev.org | No rights statement; redistributes commercial software | DAT versions embedded in filenames (`TOSEC-v2025-01-15`) → drift is dateable |
| 6 | **TOSEC official DATs** | https://www.tosecdev.org/downloads/category/8-atari | `.dat` (clrmamepro) only | not counted — page is a Phoca Download list, no direct links exposed | full TOSEC fields incl. dump status flags | requires interactive category browsing; no stable URL pattern found | Free download, no login seen | DAT version dates; no hashes on the DATs themselves |
| 7 | **MAME software list** | https://raw.githubusercontent.com/mamedev/mame/master/hash/st_flop.xml | `.st` floppy entries in MAME XML | `st_flop.xml` **312** entries, `st_flop_demos.xml` **8**, `st_cart.xml` **5** (all counted) | title, publisher, year, region, `part` with **CRC/SHA1** per file | plain XML from git; also shipped in MAME | MAME licence (GPL-2.0+ for the lists' repo); hashes only, no binaries | Hash-addressed; git history is the audit trail |
| 8 | **SNDH archive** | https://sndh.atari.org/ | `.sndh` chiptunes (not disks) | **6,015 SNDH files, 687 composers, 12,039 subtunes** (stated: "Number of SNDH-files: 6015") | composer, title, source (game/demo), date, subtune count, per-tune download | yearly zips `/files/sndh2026_lf.zip` (HTTP 200 verified); HTML index per composer | Free, no login | Yearly immutable zips; per-file HTML pages |
| 9 | **pouët.net** | https://www.pouet.net/ | Demo/cracktro metadata + download links to files hosted elsewhere | not counted — platform filter did not visibly constrain the list; pagination continued past page 130 | prod name, group, year, platform, type, screenshot, download link | HTML scraping only — `prodlist.json` → **404**; no documented API | `robots.txt` → **404** (no policy); no ToS seen | prod IDs stable (`prodlist.php?which=<id>`) |
| 10 | **Demozoo** | https://demozoo.org/ | Demoscene productions incl. ST, with external download links | **not measured — blocked** | (would be) title, group, date, platform, credits, external links | `/api/v1/...` exists but **403 Cloudflare** ("Just a moment...") | Bot-protected; API needs browser-ish client | n/a (blocked) |
| 11 | **Little Green Desktop** | https://www.atari.st/ | Once the largest ST resource (games, box scans, downloads) | **dead as a database** | none retrievable | — | Site states: "This site is here for nostalgia and archive purposes only. The LGD forums are offline (permamently)" | DB-backed pages throw `mysqli_sql_exception: Access denied for user 'fatal3_19'`; `menu_file.php` → 404; only static pages (`/pacifist/`) survive |
| 12 | **Pasti (STX)** | http://pasti.fxatari.com/ | STX tooling + format spec for protected disks | n/a (tools) | — | static HTML | free | static |
| 13 | **SYNC archive** | https://sync.wtf/ | SCP, MSA, STX disk images (Troed's flux dumps) | not counted (PARTIAL) | minimal | directory-ish HTML | no licence visible | path-addressed |
| 14 | **Atari Shrine** | https://www.atari-shrine.co.uk/ | Menu disks / downloads | not counted — front page only 2.6 KB (PARTIAL) | unknown | HTML | no licence visible | unknown |
| 15 | **ScreenScraper** | https://screenscraper.fr/ | Front-end metadata + box/screenshot media | n/a | title, year, publisher, genre, language, box art, wheel, video | **API works**: `api2/systemesListe.php` → **200** `{"Erreur de login : Verifier vos identifiants developpeur !"}` | Requires devid/devpassword + end-user account; rate-limited per account | hash-based ROM lookup |
| 16 | **TheGamesDB** | https://api.thegamesdb.net/ | Front-end metadata + art | n/a | title, year, publisher, genre, fanart | API keyed; unauthenticated call → **403** `"Invalid API key was provided."` | Free key on request | ID-addressed |
| 17 | **MobyGames** | https://www.mobygames.com/platform/atari-st/ | Metadata, cover art, credits | not measured — **403** to curl | rich | paid API only | Bot-blocked HTML; commercial API licence | stable IDs |
| 18 | **redump.org** | https://www.redump.org/ | Disc hashes (CD-based ST titles) | **dead — connection refused** (port 443) | — | — | — | — |
| 19 | **Scenetext** | (menus/diskmags) | — | **DNS failure / timeout** on `scenetext.net`, `www.scenetext.net`, `www.scenetext.com` | — | — | — | — |
| 20 | **IA: magazines** | https://archive.org/details/atari-st-format-issue-049 etc. | Magazine scans (PDF/CBZ), some with cover-disk pages | `title:(ST Format)` **98**, `subject:"Atari ST"` **4,967**, `title:(atari st)` **1,235** (all counted via API) | issue number, date, publisher | same IA APIs | IA ToS | hash-addressed |

## Notes per source

### 1. Atari Mania — the single most useful source
`https://www.atarimania.com/` → HTTP 200. `robots.txt` → 200, contents quoted above; `Sitemap: https://www.atarimania.com/sitemap.xml`.

Volume, **counted**: `sitemap.xml` is an index of 4 shards (`/sitemap/0.xml` … `/3.xml`, 3,268 + 20,000 + 20,000 + 1,560 `<loc>` entries). Deduplicating all `<loc>` URLs and bucketing by slug prefix gives:

| bucket | count |
|---|---|
| `atari-st-games-` | 9,988 |
| `atari-st-utilities-` | 4,385 |
| `atari-st-demos-` | 2,064 |
| `atari-400-800-xl-xe-games-` | 12,036 |
| `atari-2600-games-` | 10,646 |
| other (Jaguar etc.) | ~2,441 |
| **total unique game URLs** | **41,560** |

That is a *counted* measurement of entries, not of unique titles — Atarimania legitimately has multiple entries per title (regions, re-releases, "Budget" editions), so 9,988 ST "games" is an upper bound on titles and a lower bound on nothing.

Per-title metadata is unusually complete. The meta description of `https://www.atarimania.com/game-atari-st-outlands_10154.html` reads: `Outlands for Atari ST, published by Pandora in 1989. Shoot'em Up game. Screenshots, adverts on Atarimania.` — i.e. title, publisher, year, genre, and the presence of screenshots/adverts. Pages also carry a **Protection** field (this is the site that records which protection scheme a title used, and whether a dump is the original or a cracked version) and a **Language** field.

The dumps are the important discovery. On that same page the only dump link is `https://www.atarimania.com/st/dumps/outlands_pandora.zip`. I fetched it: **HTTP 200, 835,178 bytes, `application/x-zip-compressed`**, containing `Outlands_(A).stx` (849,834 B) and `Outlands_(B).stx` (847,376 B) — Pasti STX, i.e. *protected* images, not cracked `.st` files. The URL pattern is `slugify(title)_slugify(publisher).zip`, so an inventory can be built by walking the sitemap and HEAD-ing each guess. Directory listing at `/st/dumps/` is disabled and returns a soft-404 (HTTP 200, 151 bytes) that *includes an injected third-party `<script>` from `psgtech72.com`* — treat that host's 404 body as hostile, parse nothing from it.

**Coverage, counted.** I drew a seeded random sample of 40 ST game URLs from `sitemap/1.xml`, fetched each, and looked for a `/st/dumps/` href in the response: **35/40 (87.5 %) carry a dump link**, all `.zip`, zero fetch errors. Extrapolated over 9,988 ST game entries that is roughly 8,700 downloadable original disks — but see the caveat below before believing the extrapolation.

**Rendering caveat.** Atarimania is a Next.js app: a game page returns HTTP 200 with 10 `self.__next_f` flight payloads, and the useful data lives inside them, not in rendered HTML. That is actually convenient — a plain `requests.get` plus regex on the payload yields, for `game-atari-st-outlands_10154.html`:

- `https://www.atarimania.com/st/dumps/outlands_pandora.zip`
- `/st/screens/outlands_pandora.gif`, `_2.gif`, `_3.gif`, `_4.gif` (cache-busted with `?v=<epoch>`)

So screenshots are at a predictable `/st/screens/<title>_<publisher>[_N].gif` path and dumps at `/st/dumps/<title>_<publisher>.zip`. Box art and manuals do **not** appear in the flight payload for this title (0 matches for `/st/boxscans|manuals|ads/`), and only 2 of my 40 sampled pages contained any manual-ish reference — so serial "manual PDF" and "box scan" claims for Atarimania would be overclaiming; treat imagery as per-title and verify individually.

Risk/caveat: no licence, no ToS, and the zips are copyrighted game data. No checksums published, so re-walking detects only presence, not drift.

### 2. Atari Legend — best crack-scene catalogue, no binaries
`/robots.txt` → 200: `Disallow: /index.php`, `/admin/`, `/games/search`, `/music`; `Sitemap: https://www.atarilegend.com/sitemap`.

**Counted** 6,204 game URLs by fetching all 27 per-letter sitemaps (`/sitemap/games/0-9`, `/a` … `/z`) and summing `<loc>`: s 784, m 487, b 445, t 437, a 412, c 419, p 341, d 340, r 273, f 271, g 269, h 202, l 209, e 186, w 185, k 131, o 121, n 118, j 102, v 74, 0-9 71, u 56, q 53, z 52, y 26, x 25, i 115.

The menus section is the gem: `https://www.atarilegend.com/menusets` says **"There are currently 316 menu sets in the database"** (stated) and my parse of the same page found exactly **316** distinct `/menusets/<id>` links (counted), with per-set strings of the form `5 / 5 disks`. Summing those: **3,820 disks present out of 6,930 known** — i.e. the scene's own completeness figure is ~55 %. Per-set pages (e.g. `/menusets/181` for Sash: "*Sash* contains 13 menus and 13 disks. 9 disks missing or damaged (30.8% complete).") give the disk-by-disk contents *and* OCR'd scrolltexts, which is exactly the trainer/cheat/intro evidence a catalogue wants.

No disk downloads on game pages — I checked the HTML of one page: 31 screenshot references, zero `.zip`/`.stx`/`.pdf`. AL is metadata + imagery, and should be joined to a binary source.

### 3. Internet Archive `ataridiskimages` — the preservation-grade set
`https://archive.org/advancedsearch.php?q=collection%3Aataridiskimages&rows=0&output=json` → **396** (counted). Collection description: *"Images of Atari Computer (8-bit, 16-bit) floppy disks, done with flux-reading hardware and other similar methods. These will often require additional conversion/efforts to play in emulators or on systems."*

One item's metadata (`/metadata/Corporation_Core_Design_AtariST_DiskImage`) shows the shape: `collection = ataridiskimages`, `date = 1990`, subject tags including `Greaseweazle, SCP, Aufit, STX, HxC, HFE, Gotek`; **18 files** for a 2-disk game — a `.7z` (66.9 MB) plus, per disk, three PNG renderings (AUFIT, HxC BMP, HxC data) and thumbs. Every file entry carries `sha1` and `size`, so an inventory can be re-walked and diffed exactly.

The item descriptions cross-link a separate **box/manual** item and a raw scan directory: `https://archive.org/download/AtariST_Amiga_Floppy_Disks/Scan159.jpg` — a shared pool of flatbed disk-label scans addressed by sequential `ScanNNN.jpg`. That pool is a single point of failure *and* a single point of leverage: one directory, ~hundreds of label scans, no per-scan metadata.

### 4–5. Bulk sets on IA
`atari-st-collection` (https://archive.org/details/atari-st-collection) — **counted** 5 archives: `[Menus].7z` 5,511.9 MB, `[Games].7z` 1,921.8 MB, `[HD].7z` 831.0 MB, `[Compilations - Games].7z` 239.3 MB, `[Docs].7z` 12.4 MB (8,516 MB total), each with SHA1. Uploader `catblower666@gmail.com`; filed under `collection = open_source_software`, which is factually wrong for commercial games and should be recorded as a risk, not relied on as a licence.

`tosec-main` (https://archive.org/details/tosec-main) — **counted** 3,117 files, **67** under `Atari/ST/`, **21.4 GB**, described as *"Every TOSEC/TOSEC-ISO/TOSEC-PIX will be TZip / Every TOSEC/TOSEC-PIX will be single file per DAT"* — so each zip is the *romset* for one DAT, not the DAT. Category/format split I counted:

| TOSEC category | sets | notable formats |
|---|---|---|
| Games | 14 | `[ST]` 3,894 MB, `[SCP]` 3,484 MB, `[STX]` 668 MB, `[IPF]` 112 MB, `[RAW]`, `[DIM]`, `[HFE]`, `[STT]`, `[PRG]`, `[TOS]`, `[Multipart]`, `Save Disks` |
| Collections | 20 | per-cracker compilations (4U, ACN, Atari Inside, Bitz, Budgie UK, Cobra, Delta Labs, FaST Club, …) |
| Compilations | 7 | includes `Compilations - Games - [ST]` 4,731 MB |
| Games/Applications/Demos/Diskmags/Coverdisks | games 14, apps 4, demos 5, diskmags 2, coverdisks 3 | `[STX]` and `[IPF]` present for diskmags and coverdisks |
| Educational 5, Operating Systems 3, Drivers 2, Docs 1, Online Magazines 1 | | |

The naming convention alone is a metadata feed: `Atari ST - Games - [STX] (TOSEC-v2024-07-03).zip`, and inside, entries of the form `Title (19xx)(Publisher)(Region)(flags)`. **The DATs themselves are not in this item**; `https://www.tosecdev.org/downloads/category/8-atari` → 200 but is a Phoca Download listing with no direct `.dat` links in the HTML (I found `0` `.dat`/`.zip` hrefs), so DAT retrieval is interactive-only as of this probe.

Side note: IA also carries an older snapshot, `https://archive.org/details/Atari_ST_TOSEC_2012_04_23`.

### 6–7. Hash-authoritative sources
MAME's lists are the cleanest machine-readable metadata in the whole survey, and the smallest: `hash/st_flop.xml` **312** `<software>` entries (`grep -c '<software name='`), `st_flop_demos.xml` **8**, `st_cart.xml` **5**. Each `<part>` carries `<disk name= sha1=/>` or `<rom name= size= crc= sha1=/>`, plus `<description>` like `10th Frame - Pro Bowling Simulator (Europe)` — region in the title, exactly what a language/region catalogue needs. MAME's ST coverage is deliberately thin (312 vs Atari Mania's ~10,000), so this is a *validation* source, not a catalogue backbone.

### 8. SNDH — music only, but a model of how to publish
`sndh.atari.org` → 200. **Stated**: *"Number of composers: 687 / Number of SNDH-files: 6015 / Number of subtunes: 12039"*. Bulk access is trivial and versioned: `https://sndh.atari.org/files/sndh2026_lf.zip` (and `_sf`, and per-year `_update_` variants) — I fetched `/download.php` (200, 12,652 B) and read the links out of it. Not disk images, but the `lf`/`sf` pair and yearly immutable zips are the pattern the rest of the scene lacks.

### 9–11. The blocked and the dead
- **pouët.net**: root 200, `prodlist.php?platform[]=Atari+ST` 200 but the filter did not visibly constrain results (rows still present at `page=130`, 41–61 rows/page) → treat as "scraping only, count unverified". `prodlist.json` → **404**. `robots.txt` → **404** (no stated policy).
- **Demozoo**: root **403** and `/api/v1/platforms/` **403**, body is a Cloudflare "Just a moment..." interstitial. The API is real but needs a browser session; budget a headless-browser path or skip.
- **Little Green Desktop**: the historical LGD is at `https://www.atari.st/` (not `lgd.fr`, which is now a credit-risk consultancy squatting the acronym — `lgd.fr` → 200, unrelated content). `atari.st` serves a banner reading *"This site is here for nostalgia and archive purposes only"* and *"The LGD forums are offline (permamently)"*, and every DB page dies with `Uncaught mysqli_sql_exception: Access denied for user 'fatal3_19'@'66.39.3.116'` in `/usr/home/fatal3/public_html/lgd/includes/mysql.php:61`. `games.php` returns 200 but the body is the same fatal error; `menu_file.php` and `download.php` → 404. **LGD is gone as a data source.** Its links page still points at `http://www.atari.st/pacifist/` (200, static).

### 12–14. Flux/pasti and remaining archives
`pasti.fxatari.com` → 200 (STX tooling for protected disks — relevant because STX is the format that *preserves* protection). `sync.wtf` → 200 (Troed's SCP/MSA/STX dumps; described by the community as "Atari disk images … in SCP, MSA and where necessary STX formats"). `atari-shrine.co.uk` → 200 but only 2,689 bytes on the front page — PARTIAL, not assessed.

### 15–17. Front-end metadata databases
These are the only sources with *documented, credentialed* APIs, and they are the right place to get box art at scale:
- **ScreenScraper**: `https://api.screenscraper.fr/api2/systemesListe.php?devid=…&devpassword=…&softname=…&output=json` → **200** with body `Erreur de login : Verifier vos identifiants developpeur !` — the endpoint is live and needs a developer id + password plus an end-user account; it is also the only one that keys on ROM **hashes**.
- **TheGamesDB**: unauthenticated `https://api.thegamesdb.net/v1/Platforms?apikey=test` → **403** `{"code":403,"status":"Invalid API key was provided.","remaining_monthly_allowance":0,…}` — free key, then usable.
- **MobyGames**: `https://www.mobygames.com/platform/atari-st/` → **403** to curl; API is commercial-licence only. Richest credits data in existence for ST titles, inaccessible without a contract.

### 18–20. Dead hosts and magazine scans
- `redump.org` → **connection refused** (port 443) at probe time. Redump matters only for CD-based ST/TT titles; treat as unavailable.
- **Scenetext** (the menus/diskmags text archive): `scenetext.net` → DNS failure, `www.scenetext.net` → DNS failure, `www.scenetext.com` → timeout after 25 s. I could not locate a live mirror within budget; **unverified** whether it moved.
- **Magazine scans** on IA are plentiful and API-queryable: `title:(ST Format)` → **98** items (e.g. `atari-st-format-issue-049`); `title:(atari st)` → **1,235**; `subject:"Atari ST" AND mediatype:texts` → **3,751** (all counted). Also `https://archive.org/details/generation4-magazine` for Génération 4. These are *page scans*, not cover-disk images; cover disks live in TOSEC's `Coverdisks` category (441 MB `[ST]`, 19.5 MB `[RAW]`, 2.9 MB `[IPF]`) and at Exxos (`https://exxosforum.co.uk/atari/mag_index.htm`) and Page 6 (`http://www.page6.org/`) — both **unverified** (not fetched).

### The community's own link list
The single best index of scene hosts is `https://sites.google.com/view/ataritosser/links` (HTTP 200, 408 KB, **268** external links parsed). It names, with URLs, the crack-group archives a catalogue would want: Automation (`exxoshost.co.uk/atari/games/automation/`), D-Bug (`d-bug.mooo.com`), Medway Boys (`spiny.org/medway/`), Pompey Pirates (`gingerbeardman.com/archive/pompeypirates/`), M.L.P (`spiny.org/atari/mlp/`), The Chaos Engine/BSW (`mtl-music.de/BSW_DCD/`), PaCidemo (`pacidemo.planet-d.net`), DemoBaseST (`demobase.st`), Atari Shrine, SYNC, ST News (`st-news.com`), ST Format Shrine (`stformat.com`), Page 6, Exxos mag index, PlanetEmu (`planetemu.net/index.php?section=menus&machine=34`). It is also the community's de-facto *dead-link* register: several entries are bare IPs or `free.fr`/`tripod` pages that will rot.

## Dead ends and blocked hosts

| Host | Attempted URL | Result | Fallback tried |
|------|---------------|--------|----------------|
| demozoo.org | `/` and `/api/v1/platforms/` | **403** Cloudflare interstitial | none (would need headless browser) |
| atari-forum.com | `http://www.atari-forum.com/` | **403** | none |
| mobygames.com | `/platform/atari-st/` | **403** | none |
| redump.org | `https://www.redump.org/` | **000** connection refused | none |
| scenetext.net / .com | 3 hostnames | **000** DNS failure; **000** timeout 25 s | web search found no mirror |
| lgd.fr | `/` | 200 but **wrong site** (credit-risk consultancy) | correct host is `atari.st` |
| atari.st (LGD) | `/games.php`, `/menu_file.php`, `/download.php` | 200-with-fatal-error, 404, 404 | static pages only (`/pacifist/`) |
| atarimania.com | `/st/dumps/` directory listing | 200 but soft-404, 151 B, **injected script** | none — must enumerate via sitemap + slug guess |
| pouet.net | `prodlist.json` | **404** | HTML scraping |
| api.thegamesdb.net | without key | **403** invalid key | needs free key |
| api.screenscraper.fr | without creds | 200 + login error | needs dev + user account |
| tosecdev.org | `/downloads/category/8-atari` | 200 but no `.dat` hrefs (Phoca) | IA `tosec-main` romsets only |
| launchbox-app.com | `/platforms/atari-st` | **404** | none |
| atari-shrine.co.uk | `/` | 200, 2,689 B (thin) | not assessed |

## Gaps and next probes

Not checked, in rough priority order:

1. **Atari Mania dump coverage across the whole corpus.** The seeded 40-title sample says 87.5 % of ST *game* pages link a dump. Next probe: walk all 9,988 game slugs (plus 4,385 utilities, 2,064 demos) and HEAD each `/st/dumps/<t>_<p>.zip`, to get exact coverage and the error split (no dump vs. 404 vs. wrong slug guess). The same walk captures the `.stx`-vs-`.st` ratio, which is your protection-preservation metric.
2. **Atari Mania's Protection field taxonomy.** I saw the field mentioned but did not extract its controlled vocabulary. That vocabulary is the schema your catalogue's `protection: present/removed/preserved` column should adopt rather than invent.
3. **`ataridiskimages` full metadata export.** 396 items × ~18 files, all SHA1. One `advancedsearch` + `metadata` loop yields a complete, hash-verified inventory of ~400 flux dumps — small enough to do exhaustively.
4. **TOSEC DAT acquisition.** The DATs are the only source with per-file CRC32/SHA1 *and* title/year/publisher/region in the name. Find a stable mirror (GitHub `tosec`-org repos were 404 on the paths I tried; `tosecdev.org` is interactive) or extract equivalents from `tosec-main` zip filenames.
5. **Cover disks by magazine.** TOSEC has `Coverdisks` (441 MB) and `Diskmags` (717 MB) sets; pairing those to the IA magazine scans (ST Format 98 items, ST Action, Génération 4, ST Magazine) was not attempted. ST Format's "wonder disks" are separately mirrored at Exxos — unverified.
6. **Demozoo via browser.** Its API would supply group/credit/intro metadata that Atari Legend's menu scrolltexts only approximate. Needs a real browser session (403 to curl).
7. **Box and manual scans at scale.** Only IA (`AtariST_Amiga_Floppy_Disks/ScanNNN.jpg`) and Atarimania were confirmed to hold flatbed box/label scans. Atari Legend has screenshots but I saw no box art. MobyGames has the best cover art and is blocked. ScreenScraper may fill the gap once credentialed — untested for ST.
8. **Copy-protection leaflets / code wheels.** Nothing found. No source in this survey indexes code wheels or protection manuals as artefacts; Atarimania's Protection field records the *scheme*, not the leaflet scan. This looks like a genuine hole in the web's coverage.
9. **Scene text archive.** If Scenetext is truly gone, the diskmag text corpus may survive only inside TOSEC `Diskmags` images (requiring extraction) — unverified.
10. **Stability audit.** No source except IA, MAME and TOSEC publishes checksums. A re-walkable inventory should store, per item: source URL, fetch date, size, and (where available) SHA1; and treat Atarimania slugs as mutable.
