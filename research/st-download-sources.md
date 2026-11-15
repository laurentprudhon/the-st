# The download side of the ST scene — 20 titles, every language, every host

STATUS: **complete (v1)** — written 2026-11-15 by the #52 session as the second half of the fact base.
Method and roster: the owner's ruling on #52, executed as ticket #57.

> ### Correction — 2026-11-15 (owner's Vroom check): this note's method was title-first and its host list is incomplete
>
> The owner ran one search on one title (*Vroom*, Lankhor 1991) and returned fifteen URLs, of which these
> hosts were **missing from v1 entirely**: `wowroms.com`, `retrosic`/`retro**s**tic.com`, `emu-land.net`,
> `archive.org/details/playtime_atariST` (~1,518 TOSEC-named zips, ~505 MB), `pouet.net`'s download link
> (`ftp.untergrund.net/.../fujiology`), `obligement.free.fr`, `atari-forum.com` attachments.
> Verified live by the lead session on 2026-11-15 (`curl -sI`, HTTP 200).
>
> **Worse, one row of this note is factually wrong**: `myabandonware.com` was recorded as having *no* ST
> binaries. Its Vroom page lists **"Atari ST version — Game file — English — 5 MB"** with a download link
> `/download/lwpc-vroom` (HTTP **303** without a session). The "excluded, no ST files" verdict is withdrawn;
> the correct statement is *ST files exist behind a 303 gate the robots file disallows*.
>
> The root cause is the method, not the effort: searching 20 famous titles samples the web **through those
> titles**, so a host that indexes a title under a local name, or under a different slug, or that simply
> ranks below the publisher's own page, never appears. v2 replaces the method with **site-first
> enumeration** — the aggregator families, the scene tier, the national-language tier, the Internet
> Archive layer enumerated by API, and a format-led discovery pass with a stated stopping criterion —
> and re-runs the twenty titles only as a cross-check. Until v2 lands, treat Part 3's host table as a
> lower bound, not a census.

The first survey (`research/st-software-sources.md`) bounded what a source can **answer** about a title.
This note bounds what a source can **deliver** as bytes: for twenty titles from publishers in six
countries, searched under every name they shipped under, every host that carries a file, opened and
probed.

Evidence labels, unchanged and mandatory: **counted** (fetched and parsed, method stated), **stated**
(the source claims it, quoted), **unverified** (believed, not observed). No file is claimed unless an
HTTP status was observed for it. Figures the lead session re-verified itself carry
`counted (lead, 2026-11-15)` and were reproduced from the primary source, not taken from a subagent.

Nothing was downloaded as a game file: probes were `curl -I`, 1-byte range requests, and zip **central
directories** read by range so that filenames inside an archive are known without fetching disk content.
No copyrighted binary, scan or artwork is in this repo; the cache is disposable.

## What this adds, in five sentences

1. **The spine changed shape.** Atari Mania is no longer "zips of `.stx`, ~835 KB": its dumps are now
   **54–142 MB multi-format bundles** (SCP flux + HFE + Pasti + RAW + per-track `.hxcstream` sidecars),
   its pages moved to `/games/atari-st-games-<slug>-<id>`, and dump filenames are **arbitrary**, so
   slug-guessing silently 404s (`counted`: one 404 body is a 1,259-byte soft-404).
2. **PlanetEmu is the host the first survey missed** — a TOSEC mirror with **one URL category per
   container format**, which makes it the only place where the format census reads off URL paths, and
   the only ST host that publishes **CRC32 per file**.
3. **The Internet Archive layer is four collections, not two**: `softwarelibrary_atari_st_games` (884
   playable `.st` items with SHA1), `atari-st-roms_202405` (420 `.ipf`), `launchbox-atari-st` (2,716
   TOSEC-named zips) and `AtariSTRomsetByGhostware2018` (220 `.stx` + 219 `.st`), beside the known
   `ataridiskimages` and `tosec-main`.
4. **Five of the twenty sample titles have no Atari ST release at all** — the sample itself proved that
   "famous on the ST scene" and "shipped on the ST" are different facts, and that a catalogue needs a
   state for a title with zero releases on this platform.
5. **Protected images are ~15 % of the game corpus** (PlanetEmu, counted): 2,104 of 14,001 game entries
   are `.stx`/`.ipf`/`.scp`/`.hfe`/`.dim`/`.stt`. The emulator's floor is decided against that minority.

## Part 1 — the twenty titles

`ST release` is what the sweep actually established, not what the roster assumed.

| # | Title | Publisher | Country | Year | ST release | Local names that mattered | Bit-level image | Disks observed |
|---|---|---|---|---|---|---|---|---|
| 1 | Populous | Bullfrog / EA | UK | 1989 | yes | none (editions differ by publisher: Infogrames FR double pack, Hit Squad, Star Performers, The One coverdisk, MCM ES) | `.stx` | 1 (single dump) |
| 2 | Speedball 2: Brutal Deluxe | Bitmap Brothers / Mirrorsoft → Image Works | UK | 1990 | yes | none (MCM ES, Kixx, Arena US editions) | `.stx` | 1–2 (not resolved) |
| 3 | Shadow of the Beast | Psygnosis | UK | 1989/90 | yes | Polish listing "Zemsta besti" only at 16bit.pl | `.stx` | 2 |
| 4 | Robocop | Ocean | UK | 1989 | yes | none (Erbe ES, Data East US) | `.stx` + `.scp` + **`.ipf`** | 2 |
| 5 | Sensible Soccer | Sensible Software / Renegade | UK | 1992 | yes | none on ST (console builds are multi-language; not ST) | `.stx`, `.msa` (coverdisk) | 2 retail; 1 in the 92/93 edition |
| 6 | Xenon 2: Megablast | Bitmap Brothers / Mirrorsoft | UK | 1989 | yes | none (DE release is packaging only) | `.stx` | 2 retail; budget variants single-file |
| 7 | Maniac Mansion | Lucasfilm → US Gold | USA | 1988 | yes | FR (`kixx fr` dump), ES (`maniac_mansion_spanish_lucasfilm.zip`) | `.stx`, `.ST` | **2 retail, 1 in the FR Kixx repack** |
| 8 | Leisure Suit Larry 1 | Sierra | USA | 1987 | yes | none on ST (FR/DE releases not evidenced on ST) | `.scp` + `.hfe` + **`.ipf` ×2** | 2 |
| 9 | F-19 Stealth Fighter | MicroProse | USA | 1990 | yes | none — one English ST master (v1.01, v1.02, Kixx XL) | `.hfe`/Pasti/`.raw`/`.scp` in one zip | **3** |
| 10 | Turrican | Rainbow Arts | DE | 1990 | yes | none (one English build) | `.scp` + `.stx` + `.hfe` + 166 `.hxcstream` | 1 |
| 11 | The Great Giana Sisters | Time Warp / Rainbow Arts | DE | **1988** (not 1987) | yes, **but `Missing dump(s)`** | none (German box exists; disk is English) | **none publicly** — cracks only | 2 (original, unpreserved) |
| 12 | Ambermoon | Thalion | DE | — | **no — `License: Unreleased`** | German game text; the 9-disk release is the **Amiga** one | n/a | n/a |
| 13 | Battle Isle | Blue Byte / Ubi Soft | DE dev / FR publisher | — | **no — `Unreleased`** (reviewed in The One, never shipped) | German manual PDF exists | n/a | n/a |
| 14 | Die Siedler / The Settlers | Blue Byte | DE | 1993 | **no ST release at all** | the language split is real, the ST artefact does not exist | n/a | n/a |
| 15 | Captain Blood | ERE Informatique / Exxos | FR | 1988 | yes | **FR: *L'Arche du Capitaine Blood***; DE: *Die Arche des Captain Blood* (entry, **no dump**) | HFE + Pasti (FR), Pasti (EN) | 2 |
| 16 | Titus the Fox / Les Aventures de Moktar | Titus | FR | 1991/92 | yes | **FR: *Les Aventures de Moktar — Vol. 1, La Zoubida*** (separate product, Lagaf' licence) | HFE + Pasti | 1 |
| 17 | Jim Power in Mutant Planet | Loriciel | FR | 1992 | yes | **none** — one name in every language; "The Lost Dimension" is a console re-title | HFE + Pasti | 2 |
| 18 | Game Over / Game Over II | Dinamic | ES | 1988 | **only Game Over II** (Phantis renamed); the 1987 *Game Over* has no ST entry | ES original is the master; EA/Summit branding abroad | Pasti | 1 |
| 19 | Navy Moves | Dinamic | ES | 1988 | yes | none (Spanish-catalogue title, same binary) | Pasti ×2 (original + Hit Squad) | 2 SS original / 1 DS re-release |
| 20 | Dylan Dog | Simulmondo | IT | 1992 | **no — Amiga/C64/MS-DOS only** | — | n/a | n/a |

**The local-name hypothesis, scored:** it paid off twice and failed twice, and both outcomes are facts a
harvester needs.

- **Paid off — French retail editions are separate dumps, not relabelled English ones.** Captain Blood
  carries `arche_du_captain_blood_(l_)_exxos.zip` **33,707,296 B** beside the English
  `captain_blood.zip` **543,201 B** (both `counted`, HTTP 200); Moktar carries
  `aventures_de_moktar_la_zoubida_titus.zip` **24,718,164 B** beside `titus_the_fox…_titus.zip`
  **1,122,683 B** (both `counted`, 200). Both French pages declare `Dump format HFE , Pasti`. So
  *release* is a real entity, not a spelling variant of *title*.
- **Paid off — the local name finds entries no English query reaches.** The **German** Captain Blood
  entry (*Die Arche des Captain Blood*, Atarimania `19389`) surfaced only by regex over the sitemap; no
  search engine, in either language, returned it. Emuparadise's entry for
  `Arche_du_Captain_Blood,_L'_(1988)(ERE)(fr)` exists **only** under the French TOSEC name.
- **Failed — a French publisher can ship one name everywhere.** Loriciel used *Jim Power in Mutant
  Planet* in all languages; "The Lost Dimension" is a 1993 SNES / 2015 Steam re-title, i.e. a
  *platform* alias, never a localisation.
- **Failed — a localised title can hide a hole.** Die Siedler has a German name and a German fan base
  and **no ST release**; every file the German name reaches is DOS or Amiga.

## Part 2 — every host × title observation

Grouped by title; `status` is the observed HTTP result for that file or page on 2026-11-15.

| Title | Host | Format seen | File / URL observed | Status, size | Checksum published |
|---|---|---|---|---|---|
| Populous | Atari Mania | `.zip`→`.stx` | `/st/dumps/populous_eoa.zip` | 200, 533,481 B | no |
| Populous | Atari Mania | `.zip` | `/st/dumps/populous_the_one.zip`, `sim_city_populous_infogrames.zip` (id 21060) | links present | no |
| Populous | PlanetEmu | `.st`, `.stx` | `/rom/atari-st-games-st(x)/populous-1989-bullfrog` (+`-a`, `-a2`, The Promised Lands) | page 200 | no |
| Populous | IA `softwarelibrary_atari_st_games` | `.st` | `Populous_1989_Bullfrog_cr_Replicants` | metadata 200 | **SHA1** |
| Populous | IA `ataridiskimages` | `.7z` flux | `Populous2_Electronic_Arts_AtariST_DiskImage`, 44,743,778 B | metadata 200 | **SHA1** |
| Populous | Emuparadise / 16bit.pl | `.zip` | `/Atari_ST_ROMs/Populous_(1989)(Bullfrog)/114205`; `/gra/populous` | list 200; download 200 | no |
| Speedball 2 | Atari Mania | `.zip`→`.stx` | `/st/dumps/speedball_2.zip` | 200, 560,517 B | no |
| Speedball 2 | Atari Mania (MCM/Kixx/IW entries 27804, 29987, 45551) | — | pages carry **no** dump link | 200 each | — |
| Speedball 2 | PlanetEmu | `.st`, `.stx` | `…-st/speedball-2-brutal-deluxe-1990-image-works-cr-d-bug` (+4 more), `…-stx/…-image-works` | pages 200 | no |
| Speedball 2 | IA `softwarelibrary` / `ataridiskimages` / oldgamesdownload / 16bit.pl | `.st`, `.7z`, `.zip` | `Speedball_2_…_cr_Replicants_ST_Amigos`; `Speedball2_Kixx_Disk1Of1….7z` 31,059,463 B; `…_Atari-ST_ROM_EN.zip` 552.06 KB (stated) | all 200 | SHA1 on IA only |
| Shadow of the Beast | Atari Mania | `.zip`→`.stx` | `shadow_of_the_beast_psygnosis.zip` (+US `_usa`, Sizzlers, STE homebrew entries) | 200, 1,683,944 B | no |
| Shadow of the Beast | PlanetEmu | `.st`, `.stx`; `.scp`/`.hfe` **for Beast II only** | `/rom/atari-st-games-stx/shadow-of-the-beast-1990-psygnosis-disk-{1,2}-of-2` | pages 200 | no |
| Shadow of the Beast | IA `ataridiskimages` | `.7z` flux + label scans | `Shadow_Of_The_Beast_Psygnosis…` 66,052,322 B (19 files) | metadata 200 | **SHA1** |
| Shadow of the Beast | IA `softwarelibrary` | — | `title:"Shadow of the Beast"` → **0 hits** | — | — |
| Robocop | Atari Mania | `.zip`→`.stx` | `robocop_ocean_software.zip` 306,111 B; `robocop_dataeast.zip` **43,645,604 B**; Hit Squad entry | 200 | no |
| Robocop | PlanetEmu | `.stx`, `.scp`, **`.ipf`** | `…-ipf/robocop-1989-ocean-eu-disk-{1,2}-of-2` | pages 200 | no |
| Robocop | IA `ataridiskimages` | — | Robocop **1**: none; only `Robocop_3_Ocean…` 156,154,585 B | — | SHA1 |
| Robocop | gamesdatabase.org | `.pdf` manual | `/Media/SYSTEM/Atari_ST/manual/Formated/Robocop_-_Ocean.pdf` | 200 | no |
| Sensible Soccer | Atari Mania | `.stx` ×2 | `/st/dumps/sensible_soccer.zip` | 200, 1,401,233 B | no |
| Sensible Soccer | Atari Mania (STF38) | `.msa` | `/st/dumps/stf38.zip` (`stf38a.msa`) | 200, 368,712 B | no |
| Sensible Soccer | Atari Mania (92/93 edition, id 23549) | `.stx` | `sensible_soccer_-_1992-93_season_edition.zip` | 200, 1,017,290 B | no |
| Sensible Soccer | IA `softwarelibrary` / `launchbox` | `.st`, `.zip` | 2 items, 737,280 B; 4 TOSEC zips | 200 | **SHA1** |
| Xenon 2 | Atari Mania | `.stx` ×2 | `xenon_ii_-_megablast_imageworks.zip` | 200, 1,168,918 B | no |
| Xenon 2 | Atari Mania (Kixx 35442 / Mirror Image 24779) | `.stx` | `…_kixx.zip` 1,734,358 B; `…_mirror_image.zip` 1,197,180 B | 200 | no |
| Xenon 2 | Atari Mania (33413) | **HD-install folder** | `xenon_ii_-_megablast_ste_dma_[putnik].zip` — `XENON2A/RUNICDM.TOSPK`, no floppy container | 200, 25,455,616 B | no |
| Xenon 2 | PlanetEmu / `launchbox` | `.st`, `.zip` | 5 crack variants | pages 200 | IA SHA1 only |
| Xenon 2 | IA `softwarelibrary` | — | `title:"Xenon 2"` → **0**, `title:"Xenon II"` → **0** | — | — |
| Maniac Mansion | Atari Mania (9908) | `.stx` ×2 | `maniac_mansion_lucasfilm_usgold_pasti.zip` | 200, 1,060,255 B | no |
| Maniac Mansion | Atari Mania (23569) | `.stx` 1-disk repack | `maniac_mansion_kixx_fr.zip` | 200, 536,009 B | no |
| Maniac Mansion | Atari Mania (43375) | **`.ST`** | `maniac_mansion_spanish_lucasfilm.zip` | 200, 372,602 B | no |
| Maniac Mansion | Atari Mania (22980, 24019, 24187, 25274, 31930) | — | 5 further entries, **no dump href** | 200 each | — |
| Maniac Mansion | IA `softwarelibrary` / `launchbox` / `AtariSTManuals_201812` | `.st`, `.zip`, `.pdf` | 5 items; 4 zips; `Maniac Mansion.pdf` | 200 | **SHA1** |
| Leisure Suit Larry 1 | Atari Mania (9818) | `.hfe` + `.scp` | `leisure_suit_larry_i_-_in_the_land_of_sierra_on-line.zip` | 200, **129,569,066 B** | no |
| Leisure Suit Larry 1 | IA `atari-st-roms_202405` | **`.ipf` ×2** | `…(Europe)(v1.04)(6.18.87)(Disk 1/2).ipf` | 206 on range | **SHA1 + XML dat** |
| Leisure Suit Larry 1 | IA `softwarelibrary` | `.st` | 2 items (`…codes_on_disk` 737,280 B; `…cr_42-Crew` 860,160 B) | metadata 200 | **SHA1** |
| F-19 Stealth Fighter | Atari Mania | zip of **HFE+Pasti+RAW+SCP** | `f-19_stealth_fighter_microprose_software_uk_1990.zip` | **200, 142,194,769 B** (`counted (lead)`) | no |
| F-19 | IA `atari-st-roms_202405` / PlanetEmu | `.ipf` | Disk A/B/C; `F-19 … (Disk 1 of 3)(A)[cp manual]` | listed / page 200 | IA SHA1 |
| Turrican | Atari Mania | **`.scp` 37,815,740 B + `.stx` 1,701,390 B + `.hfe` + 166 `.hxcstream`** | `turrican_rainbow_arts.zip`, 171 entries read from the central directory | **200, 54,488,704 B** (`counted (lead)`) | no |
| Turrican | atari.8bitchip.info | `.ZIP` HD/HxC build | `atari.8bitchip.info/TURRICAN.ZIP`; STX-of-original link **404** | HD zip 200, 718,267 B | no |
| Turrican | PlanetEmu | `.zip`→`.stx` | `…-stx/turrican-1990-rainbow-arts`, **CRC32 `e7af30a4`** | page 200 | **CRC32** |
| Turrican II | Atarimania / `atari-st-roms_202405` / `ataridiskimages` / Ghostware | STX+IPF+SCP (stated); `.ipf`; `.7z` 34,438,128 B; `.stx` 1,971,338 B | `turrican_ii_rainbow_arts.zip` 61,627,307 B | 200 | IA SHA1 |
| The Great Giana Sisters | Atarimania | — | `great_giana_sisters_the_time_warp.zip` → **404** (1,259 B soft-404); page says `Missing dump(s)` | 404 (`counted (lead)`) | — |
| The Great Giana Sisters | `launchbox` / Ghostware | `.zip`, `.st` | 5 crack variants; `[cr Bladerunners].st` 819,200 B | 200 | IA SHA1 |
| Ambermoon / Battle Isle / Die Siedler | Atarimania | — | pages exist for 12/13 with `License: Unreleased` (`counted (lead)`, battle-isle page); `…_thalion.zip`, `…_ubi_soft.zip` → 404 | 404 | — |
| Ambermoon / Battle Isle / Die Siedler | IA / OGDB / heise / SidecarTridge | — | only Amiga/DOS artefacts; `archive.org/details/battle-isle` is the **Amiga** release (cover art) | 200 | — |
| Captain Blood | Atari Mania | HFE+Pasti (FR), Pasti (EN) | `arche_du_captain_blood_(l_)_exxos.zip` **33,707,296 B**; `captain_blood.zip` **543,201 B**; Mindscape 30,359,287 B; Pocketsoft 359,958 B | 200 | no |
| Captain Blood | Atari Mania (19389 DE / 41877 FR Zafiro / 26764) | — | **no dump link on any of the three** | 200 | — |
| Captain Blood | IA / Emuparadise / abandonware-france | `.st`-class; entry only | `Captain_Blood_Smash16_AtariST_DiskImage` (coverdisk); FR TOSEC entry `/111104` | item exists; page 200 | IA SHA1 |
| Titus the Fox / Moktar | Atari Mania | HFE+Pasti | `titus_the_fox_-_to_marrakech_and_back_titus.zip` **1,122,683 B**; `aventures_de_moktar_la_zoubida_titus.zip` **24,718,164 B** | 200 | no |
| Moktar / Titus | IA `softwarelibrary` | `.st` | 3 items incl. `Aventures_de_Moktar_Les_1991_Titus_fr_cr_Vmax` 737,280 B | metadata 200 | **SHA1** |
| Moktar / Titus | SidecarTridge DB | `.st` | `DBUG/DBUG045A.st` 839,680 B (menu-disk rendition) | 200 `binary/octet-stream` | no |
| Jim Power | Atari Mania | HFE+Pasti | `jim_power_mutant_planet_loriciel.zip` **118,217,755 B** | 200 | no |
| Jim Power | IA `softwarelibrary` / SidecarTridge | `.st` | `…_cr_Replicants_t_one_disk` 839,680 B; `SUPERIOR/124(Disk1of2).st` 839,680 B | 200 | IA SHA1 |
| Game Over II | Atari Mania | Pasti | `game_over_ii_dinamic.zip` | 200, 286,214 B | no |
| Game Over II | `atari-st-roms_202405` / `launchbox` / PlanetEmu | `.ipf`, `.zip`, `.stx` | `Game Over II (Europe).ipf`; 2 zips | listed | IA SHA1 |
| Navy Moves | Atari Mania | Pasti ×2 | `navy_moves.zip` **821,960 B**; `navy_moves_the_hit_squad.zip` **701,752 B** (`counted (lead)`) | 200 | no |
| Navy Moves | Computer Emuzone (ES) | `.zip` | `/atari/` → `download.php?ind=` → 302 → `/atari/nmovesA_180.zip` 774,880 B | 302→200 | no |
| Navy Moves | `launchbox` / Atari Legend menuset 245 / Automation DBug CD | `.zip`, menu disk, `.msa` | 1 cracked+trained disk; Medway Boys #12; "Disk 180" | 200 | IA SHA1 |
| Dylan Dog | it.wikipedia / Atarimania sitemap / IA | — | 0 `dylan` and 0 `simulmondo` slugs in the ST sitemap; the IA item is a **`.d64`** | — | — |

## Part 3 — the host inventory (union of the twenty-title sweep)

`New?` compares against the twenty sources of the first survey. "Licence" is what the host asserts, not
what is lawful: **no host that stores ST binaries holds a rights grant.**

| Host | New? | Holds | Formats actually seen | Title addressing | Checksums | Walk strategy | Licence stance |
|---|---|---|---|---|---|---|---|
| **atarimania.com** | known, **materially changed** | ~10k ST games: multi-format dumps, screens, box scans, adverts, some manuals | `.zip` → `.stx`, `.scp`, `.hfe`, `.raw`, `.hxcstream`, `.png`; one HD-folder install | `/games/atari-st-games-<slug>-<id>`; dump href **arbitrary**; page fields `Dump format`, `Number of Disks`, `Dump` status | none | walk `sitemap/{0..3}.xml` (44,832 locs, 9,988 ST games — `counted (lead)`), read each page's dump `href`; `?search=` does not filter | none stated; `/api/`, `/search` robots-disallowed |
| **planetemu.net** | **NEW** | TOSEC romsets, one category per container, ~14,001 game entries | `.st .stx .ipf .scp .hfe .dim .stt .raw .prg .tos` | `/roms/atari-st-games-<fmt>` → `/rom/<set>/<slug>`; download **POST** `/download/rom` `id=<n>` + quota | **CRC32 per file** | `sitemaps/roms/…` shards (66 atari-st categories) or `/machine/atari-st`; download needs a session | none stated; search/popup paths disallowed |
| **IA `softwarelibrary_atari_st_games`** | **NEW** | **884 items** (`counted (lead)`), one **playable** `.st` each, in-browser emulator | `.st` | `advancedsearch.php?q=collection:…`; file `<identifier>.st` | **SHA1 per file** | IA advancedsearch + `metadata/<id>`; `collection` is the only reliable platform signal | IA ToS, no licence |
| **IA `atari-st-roms_202405`** | **NEW** | **420 `.ipf` + 2 XML dats** (`counted (lead)`, 424 files) | `.ipf` | `/download/<item>/<name>.ipf` | **SHA1 + XML dat** | `metadata/<id>` JSON | IA ToS |
| **IA `launchbox-atari-st`** | **NEW** | **2,716 zips** (`counted (lead)`) of TOSEC-named `.st`, "BESTSET", ~1,260 games (stated) | `.zip` | flat `/download/launchbox-atari-st/<name>.zip` | SHA1 in item metadata | `metadata/<id>`; the filename *is* the TOSEC metadata | filed `opensource_media` — wrong |
| **IA `AtariSTRomsetByGhostware2018`** | **NEW** | 505 files: **220 `.stx` + 219 `.st`** + art/mp3 | `.stx`, `.st` | flat | SHA1 | `metadata/<id>` | misfiled as a console set |
| **IA `ataridiskimages`** | known | flux preservation set, per-item `.7z` + label scans | `.7z` (SCP/STX/HFE inside) | `metadata/<id>` | **SHA1** | IA API | no licence |
| **IA `AtariSTManuals_201812`**, **`gamemanuals`** | **NEW** | manual scans, 3,476 files | `.pdf`, `.djvu` | `<Title>.pdf` | IA hashes | IA API | IA ToS |
| **IA `atarist-automation-dbug-cd`**, **`turrican_Collection`**, per-title items | known/new mix | menu-disk CDs, multi-platform rars, single-title flux items | `.msa`, `.rar`, `.7z` | item level | SHA1 | IA API | no licence |
| **ataristpublicfloppydb.sidecartridge.com** | **NEW** | public floppy DB, 2025: **3,409 unique rows / 34 shards** (`counted (lead)`) | `.st` | `ataristdb.sidecartridge.com/db/<letter>.csv` → `<group>/<file>.st` | none (columns: name;0;;;group;path) | 34 CSV shards, then direct GETs — the easiest bulk walk in the ST world | `robots.txt` 404 (none) |
| **exxosforum.co.uk** | known as "unverified" → **verified** | ST Format cover disks (`stf01`–`stf86`, **70 links**, gaps) + Pompey Pirates menus + Automation | `.zip` → `.msa`/`.st` | `/atari/STF/stfNN.zip`; `/atari/games/pompy/` | none | static index pages | none stated |
| **atarilegend.com** | known, **corrected: serves menu binaries** | metadata, scans, reviews, **menu-disk zips** (`/storage/zips/menus/<id>.zip`, one 200 / 786,548 B — `counted (lead)`), browser emulator | `.zip` | `/menusets/<id>`; game pages mark playable | none | menuset walk; no retail originals | restrictive robots on search paths |
| **emuparadise.me** | **NEW** | TOSEC-named set incl. **French** names (`Arche_du_Captain_Blood,_L'…(fr)`) | `.zip` of `.st` | `/Atari_ST_ROMs/<TOSEC name>/<id>` → `…-download` (HTML interstitial; no binary observed) | none | HTML listing | login gate on some downloads |
| **oldgamesdownload.com** | **NEW** | per-platform zips, filename encodes platform+region | `.zip` | `/game/<slug>-<3char>/` → `/file/<uuid>/` | none | parse page per title (UUIDs reassign) | none stated |
| **16bit.pl** (PL) | **NEW** | Polish scene downloads, Polish subtitles ("Zemsta besti") | `.zip` via S3 presigned | `/gra/<slug>` → `/download.php?uuid=…`; bare CDN 401s | none | must go through the uuid page | none stated |
| **computeremuzone.com/atari/** (ES) | **NEW** | Spanish-catalogue ST titles + covers/manuals | `.zip` | `/ficha/<id>/<slug>` → `download.php?ind=<n>` (302) | none | list → ficha → 302 | none seen |
| **gamesdatabase.org** | **NEW** | **manual PDFs** per system at a predictable path | `.pdf` | `/Media/SYSTEM/Atari_ST/manual/Formated/<Title>_-_<Pub>.pdf` | none | path guess works | none stated |
| **page6.org**, **atarimuseum.nl**, **downloads.atari-home.de** | new/probed | PD libraries (page 6 `.ST`; atari-home German PD/Scene/MINT with real Apache listings) | `.ST`, `.ZIP`, `.LZH` | directory listings | none | open listings | **PD only — the cleanest licence position found** |
| **atari.8bitchip.info** (Putnik) | **NEW** | HD/HxC-adapted builds; STX-of-original links 404 | `.ZIP` | `ASTGA/<letter>/<title>.php`; `<TITLE>.ZIP` | none | index walk | none stated |
| **st-news.com** | **NEW** | ST NEWS diskmag issues | `.st`, `.zip` | `/uploads/ST-News/downloads/<name>.st` | none | index | none stated |
| **atariuptodate.de**, **OGDB**, **devuego.es**, **abandonsocios.org**, **abandonware-france.org**, **jeuxvideovintage.com**, **retroarchives.fr**, **storiainformatica.it**, **oldgamesitalia.net** | **NEW** | local-language catalogues (DE/ES/IT/FR) — metadata, screens, in-browser play | none (0 file hrefs observed) | per-site | — | scrape for metadata only | varies; abandonsocios is login/attachment-gated |
| **myabandonware.com** | **NEW, gated** | **does** offer per-platform "Game file" downloads incl. **Atari ST (5 MB for Vroom)**, per-language variants; download 303s to the game page without a session and `robots.txt` **disallows `/download/`** — so: files exist, crawlers are refused | unverified (303 gate not defeated) | `/download/<token>-<slug>` | none | **metadata only for a harvester**; the file exists but the site refuses bots by policy | explicitly disallows crawling `/download/` |
| **gamesnostalgia.net**, **gametronik**, **file-hunter.com**, **retrostic**, **8bitchip listings** | probed | 403 / 301 / **MSX-only** (405 without a browser UA) / paywall-ish | — | — | — | recorded as dead ends | — |
| **atarijookie/atari-st-floppy-image-downloader** | **NEW** | a *harvester itself*: compiled list of ~4,400 menu images (stated) with hash checking | `.msa` | GitHub release + embedded image list | per-image hash check (stated) | read its list; it is a ready-made menu-disk index | GitHub ToS |

## Part 4 — the format census (the number #29 quotes)

**PlanetEmu's per-container census, reproduced by the lead from its own sitemaps
(`counted (lead, 2026-11-15)`).** It is the only source that publishes format as a first-class axis, and
the only one whose numbers a harvester can re-derive exactly.

| Category | Entries | Share of games |
|---|---|---|
| `atari-st-games-st` (plain sector) | **11,872** | 84.8 % |
| `atari-st-games-stx` (Pasti) | **1,444** | 10.3 % |
| `atari-st-games-ipf` (SPS/CAPS) | **413** | 2.9 % |
| `atari-st-games-scp` (flux) | **178** | 1.3 % |
| `atari-st-games-stt` | 37 | 0.3 % |
| `atari-st-games-dim` | 28 | 0.2 % |
| `atari-st-games-prg` | 7 | — |
| `atari-st-games-raw` | 5 | — |
| `atari-st-games-tos` | 5 | — |
| `atari-st-games-hfe` | **4** | 0.03 % |
| `atari-st-games-multipart` / save-disks | 1 each | — |
| **games, all containers** | **14,001** | 100 % |
| `atari-st-applications-st` | 1,211 | — |
| `atari-st-demos-st` | 2,219 | — |
| `atari-st-collections-*` (22 groups: 4U, ACN, Bitz, Budgie UK, Cobra, Delta Labs, FaSt Club, Floppyshop, GFA Club, Heim, Kontrast, Poolware, ST Computer, ST Vision, Xest …) | 88 for `-4u` alone | — |

**There is no `atari-st-games-msa` category at all** — MSA is the menu-disk lingua franca and the format
emulators implement first, yet PlanetEmu does not index it for games. `.msa` appears only inside menu-disk
CDs and the atarijookie list.

**Bit-level availability per title (this sweep):**

| Title | `.stx` | `.ipf` | `.scp` | `.hfe` | `.raw` | best single source |
|---|---|---|---|---|---|---|
| Populous | yes | no | no | no | no | Atarimania 533 KB |
| Speedball 2 | yes | no | no | no | no | Atarimania 560 KB |
| Shadow of the Beast | yes | no | **Beast II only** | Beast II only | no | Atarimania 1.7 MB |
| Robocop | yes | **yes (2 disks)** | yes | no | no | PlanetEmu (STX+SCP+IPF) |
| Sensible Soccer | yes | no | no | no | no | Atarimania 1.4 MB |
| Xenon 2 | yes (3 editions) | no | no | no | no | Atarimania |
| Maniac Mansion | yes (+ FR 1-disk, ES `.ST`) | no | no | no | no | Atarimania |
| Leisure Suit Larry 1 | — | **yes (2 disks)** | yes | yes | no | Atarimania / `atari-st-roms_202405` |
| F-19 Stealth Fighter | yes | yes (3 disks) | yes | yes | **yes** | Atarimania (one 142 MB zip) |
| Turrican | yes | (Turrican II: stated) | yes | yes | (`.hxcstream` ×166) | Atarimania 54.5 MB |
| Giana Sisters | **no original** | no | no | no | no | cracks only |
| Captain Blood (FR) | yes | no | no | yes | no | Atarimania 33.7 MB |
| Moktar (FR) | yes | no | no | yes | no | Atarimania 24.7 MB |
| Jim Power | yes | no | no | yes | no | Atarimania 118 MB |
| Game Over II | yes | yes | no | no | no | Atarimania / IPF set |
| Navy Moves | yes | no | no | no | no | Atarimania (2 releases) |
| Ambermoon / Battle Isle / Die Siedler / Dylan Dog / Game Over (1987) | **no ST release** | — | — | — | — | — |

**What the emulator's floor must cover, stated once:** a v1 that reads only sector-level `.st`/`.msa`
cannot open **2,129 of the 14,001** catalogued game entries (15.2 %), and cannot open the *only*
surviving image of some titles (LSL 1's best copies are `.ipf`/`.scp`; Giana Sisters' original does not
exist as a readable image at all). Conversely SCP flux, the most expensive format to support, covers
**178 game entries** — 1.3 % — though it is the sole master for a handful of titles.

## Part 5 — corrections to `research/st-software-sources.md`

Recorded as a dated block in that note; do not re-read it without them.

1. **Atari Mania's dumps are multi-format bundles, not "a `.zip` of `.stx` at ~835 KB."** Observed sizes:
   533 KB (Populous) → 54,488,704 B (Turrican, 171 entries: `.scp` + `.stx` + `.hfe` + 166
   `.hxcstream` + `.png`) → 142,194,769 B (F-19). Pages carry `Dump format` (`HFE , Pasti`, `Pasti`,
   `HFE, Pasti, RAW, SCP`) and `Number of Disks` fields that did not exist when the survey was written.
2. **Its URL scheme moved**: `game-atari-st-<slug>_<id>.html` → `/games/atari-st-games-<slug>-<id>`
   (0 old-scheme URLs remain in 44,832 sitemap locs — `counted (lead)`). The slug is title-only and not
   unique (7 Populous entries), so a harvester must join on the **numeric id**.
3. **Its dump paths are not reliably derivable.** `slugify(title)_slugify(publisher)` fails on
   `" - "` (→ `_-_`), and several are simply arbitrary (`navy_moves.zip`,
   `f-19_stealth_fighter_microprose_software_uk_1990.zip`). Read the `href`; never guess.
4. **Atari Legend does serve binaries** — menu-disk zips (`/storage/zips/menus/<id>.zip`, verified 200 /
   786,548 B) and a browser emulator. It still serves no retail original.
5. **The STX-only assumption undercounts flux-grade material.** LSL 1's Atarimania zip is `.hfe` + `.scp`
   (129.6 MB) with no `.stx` in the tail we read; F-19's carries four formats; Turrican's carries three.
6. **PlanetEmu was only a link in the survey** and is in fact a first-class host with per-format
   categories, a countable census, and the scene's only per-file CRC32.
7. **The IA layer is four collections, not two** (the three added above); `ataridiskimages` remains the
   only one with flux inside a `.7z`.
8. **"Atarimania's manual coverage is sparse" now has a counterpart**: `AtariSTManuals_201812` (3,476
   files) and gamesdatabase.org's predictable `/Media/SYSTEM/Atari_ST/manual/Formated/` path.

## Part 6 — what this does to the schema (#52) and the disk path (#29)

Labelled `derived`: these are consequences of the counted findings, not new claims about the sources.

- **A title can have zero releases on this platform.** Five of twenty sample titles are in that state, and
  Ambermoon/Battle Isle even *have* an Atarimania page with `License: Unreleased`. So the catalogue needs
  a title-level state that means "exists as a name, never shipped on ST" — otherwise the completeness
  metric either counts phantoms or loses the finding.
- **Release is real, not cosmetic.** FR vs EN Captain Blood and FR Moktar are separate, differently-sized
  dumps at the same host; Maniac Mansion ships as a 2-disk retail set *and* a 1-disk French repack; Navy
  Moves ships 2 single-sided disks (1988) and 1 double-sided (1992 Hit Squad). Disk count is a
  **release** property, never a title property — three sources gave three different counts for one title.
- **A file is not an image.** One Atarimania URL is a 54–142 MB zip carrying 3–4 renditions of the same
  disks plus 166 per-track sidecars; one IA `.7z` carries flux for 2 disks; one `launchbox` zip carries a
  single `.st`. Provenance (URL, fetch date, size, published hash) therefore lives on the **fetchable
  file**, while identity lives on the **hashed image** — and a mirror is a second file for the same image.
- **Protection state is a rendition property, and the evidence tiers are visible in the sources**: Pasti
  `.stx` and SPS `.ipf` *preserve* it; a plain `.st` from `softwarelibrary_atari_st_games` or a `[cr …]`
  TOSEC name *removes* it; Giana Sisters has no preserving rendition anywhere, so its protection state is
  permanently `unknown` rather than `removed`.
- **Hashes exist only where the source publishes them**: IA (SHA1 per file), MAME (CRC/SHA1), TOSEC DATs
  (CRC32/SHA1), and PlanetEmu (CRC32 per file). Atarimania, CEZ, exxos, 16bit.pl, sidecartridge and
  emuparadise publish none — for those, drift detection is size-and-presence only, which is a hard bound
  on #54.
- **For #29:** the floor question is now numeric. Sector-level only loses 15.2 % of the catalogued corpus
  and *all* of the best copies of some titles; adding IPF+STX covers 13.3 % of the corpus and every
  preserved-original case in this sample; SCP buys 1.3 % and is the only master for a few titles.

## Part 7 — gaps and next probes

1. **PlanetEmu's download path is POST + quota-gated**; only its listing side was walked. A harvester
   needs a browser session or the quota endpoint reversed. Its CRC32 values are the prize.
2. **My Abandonware was not defeated and must not be**: `robots.txt` disallows `/download/`, and the
   endpoint 303s to the game page. Its ST container format stays `unverified`.
3. **Emuparadise's download route returns an HTML interstitial**; no binary was observed. Its value so
   far is as a *name authority* (French TOSEC names as entries).
4. **The German-language disk tier is unmined**: `forum.atari-home.de` attachments,
   `atari-forum.com` (403 to curl). No German-language **disk** was found for any German title — the
   German artefacts are boxes, manuals and one unreleased build. If the catalogue wants a `language` axis
   proven by disk contents, that axis is currently only demonstrated for **French** (Moktar, Captain
   Blood) and **Spanish** (Maniac Mansion `.ST`).
5. **Italian and Spanish file hosts remain thin**: CEZ serves Spanish titles; nothing Italian was found
   that serves an ST image. The productive angle is Simulmondo's non-comic catalogue (e.g. *1000 Miglia*,
   *I Play 3D Soccer*) — untested.
6. **Menu-disk corpus not enumerated**: Atari Legend's 316 menu sets, exxos' Pompey Pirates index and the
   atarijookie image list (~4,400 images, stated) are three partial views of the same population. Nobody
   has joined them.
7. **Zip interiors were read by central directory, not downloaded**, except where a page's `Dump format`
   field is the only evidence (Captain Blood DE, LSL disk 2 filenames, Turrican II's IPF). Those three
   claims are `stated`, not `counted`.
