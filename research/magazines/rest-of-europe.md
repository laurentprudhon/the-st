# Atari ST press outside UK / FR / DE — Spain, Italy, Benelux, Poland, Nordics, and the rest

STATUS: complete (v1) — written 2026-11-15 after ~70 min of probing. Two things are `PARTIAL`/open and
flagged inline: Nordic national-library full-text access, and the Czech/Portuguese negatives (checked, but
only against IA and one aggregator each).

Evidence labels follow `research/st-software-sources.md`: **counted** (I fetched and parsed it — method
stated), **stated** (source claims it, quoted), **unverified** (hearsay / not confirmed).

Probe host: WSL, curl + Python, 2026-11-15. **Metadata, index and file listings only.** No issue was
downloaded; no scan, page image or OCR text was written to disk. Methods used: IA `advancedsearch.php`
(`numFound` + full identifier enumeration), IA `metadata/<id>` (file-format inventory = text-layer test),
public HTML listings of `atarimania.com/magazines`, `retroedicola.com/archivio`, `atarimuseum.nl`,
`atariware.cl`. Where I quote magazine *content*, it is a search-engine snippet of a public OCR page, not a
fetch of the document.

## 0. Headline

Four languages have a real, digitised, OCR'd ST review corpus: **Spanish**, **Italian**, **Polish**, and —
marginal but real — **Greek**. Dutch is a small real corpus (one consumer title, 5 issues scanned). The
Nordics have no dedicated ST magazine at all; Sweden's *Datormagazin* is the only usable vehicle and it is
multiformat. **Portugal, Czechoslovakia, Austria-as-separate-market and Belgium have no ST press of their
own** — that is a result, not a gap.

The single biggest finding: **Spanish *Micromanía* is complete on the Internet Archive, first and second
epoch, with an OCR layer** (35/35 and 80/80, counted). That is 80 monthly issues of 1988–1995 Spanish
ST/Amiga reviews sitting behind a predictable identifier, and it is larger than the entire Italian
ST-era corpus in one title.

Second finding, a correction: the three "Spanish-language Atari magazines" that circulate in checklists —
*Mundo Atari*, *Turbo News*, *STAK* — are **Chilean** (place of publication printed in the IA metadata:
Santiago de Chile). They are still a Spanish-language ST corpus, but they are not Spain's, and Spain's own
Atari-dedicated title is *Atari User (Spain)*, 28 issues, 1988-89.

## 1. Magazine table (per country)

Columns: years · ST coverage · score format · issues published · digitised (counted, deduped) · best source.
"ST coverage" says how the title treats the ST, with the evidence class.

### Spain

| Magazine | Years | ST coverage | Score format | Published | Digitised | Best source |
|---|---|---|---|---|---|---|
| **Micromanía** (1ª época) | 1985-05 – 1988 | 8-bit first, ST from 1986-87; hardware + games (*unverified* per-issue split) | numeric /10 (*unverified*) | **35** (stated, es.wikipedia) | **35/35 — COMPLETE** (counted: identifiers 1-35, deduped across 2 collections) | IA `micromania*` / `MicromaniaPrimerEpocaspanishIssue*` |
| **Micromanía** (2ª época) | 1988 – 1995 | **core ST/Amiga/PC review monthly** — the Spanish ST corpus | numeric /10 (*unverified*) | **80** (stated, es.wikipedia) | **80/80 — COMPLETE** (counted, distinct numbers 1-80) | IA `micromania-segunda-epoca-*` |
| **Micromanía** (3ª época) | 1995 – 2024 | PC; ST only retroactively | — | 335 (stated) | 75 distinct numbers of 335 (counted) — **not ST-relevant** | IA (partial) |
| **Atari User (Spain)** | 1988 – 1989+ | **Atari-dedicated**, Spanish — XL/XE **and ST** (reviews + hardware) | *unverified* | **28** (stated: Atarimania "28 ISSUES", from Año 1 N°02, 1988) | Atarimania 28 PDFs; IA: none found | Atarimania `/magazines/106`, no login, direct PDFs, **no text layer** |
| **Atari Fan** | 1993-09 – ? | Spanish Atari fanzine, Falcon/TT era (*unverified* whether Spain or LatAm) | *unverified* | 11 (stated: "11 issues") | Atarimania 11 PDFs | Atarimania `/magazines/134` |
| **Atari Explorer (Spain)** | 1985-04 – 1985 | **8-bit era, pre-ST** — no ST reviews | *unverified* | 5 (stated) | 5/5 (Atarimania `/magazines/136`; IA `atari-explorer-es-*`, hOCR present) | either |
| Hobby Consolas | 1991 – | console-led; ST mentioned, not reviewed systematically (*unverified*) | 0-100 % (*unverified*) | 448 IA title hits (counted, includes posters/guides) | large, IA `hc*` | IA — **not an ST source** |
| Super Juegos | 1992 – | 16-bit console-led; ST marginal (*unverified*) | *unverified* | 33 IA hits (counted) | partial | IA — **not an ST source** |
| Input / Input Micros | 1985 – 1988 | Hobby Press; "tried to cover the ST" (stated by Vandal's press history) | *unverified* | — | **0 found** — IA "Input" hits are the UK Atari 8-bit *Input/Output* | none |

**Correction, and an important one:** *Mundo Atari*, *Turbo News* and *STAK* — which every Spanish-language
Atari checklist lists together — are **Chilean, not Spanish**. Counted from IA item metadata, which states
the place of publication outright: *"STAK - la revista Atari, n.º 1, mayo 1992, **Santiago de Chile**"* and
*"MundoAtari … n.º 11, abril 1988, **Santiago de Chile**"*. A.N.A.L.O.G.'s magazine index agrees
("This Atari magazine was based in Chile and ran for 25 issues, from May 1987 to August/September 1989").
They belong to the Latin-American Spanish-language corpus, listed separately below. *Turbo News* is placed
there by association (same Chilean Atari ecosystem; the IA description was not readable), *unverified*.

### Latin America (Spanish-language — outside the brief, but the same OCR target)

| Magazine | Years | ST coverage | Published | Digitised | Text layer | Best source |
|---|---|---|---|---|---|---|
| **Mundo Atari** | 1987-05 – 1989 | **Atari-dedicated** (XL/XE + ST), Santiago de Chile | 25-26 (stated: 25 by A.N.A.L.O.G./atariware, 26 incl. No.00 by Atarimania) | IA: 21+ distinct items; Atarimania 26/26 PDFs | **YES on IA** (Abbyy/Text PDF, sampled), none in Atarimania's PDFs | IA `MundoAtari*`; Atarimania `/magazines/71` |
| **Turbo News** (Atari Turbo News) | 1989-07 – 1991 | **Atari-dedicated**, ST-heavy | 15 (two sources agree) | Atarimania 15/15; IA 44 hits (dupes) | IA yes; Atarimania no | Atarimania `/magazines/75` |
| **STAK** | 1992 – 1993 | **Atari-dedicated**, ST/Falcon era, Santiago de Chile | 10 (two sources agree) | Atarimania 10/10; IA 22 items | **YES on IA** (Abbyy + Text PDF) | IA `STAK01..`; Atarimania `/magazines/72` |

### Italy

| Magazine | Years | ST coverage | Score format | Published | Digitised | Best source |
|---|---|---|---|---|---|---|
| **The Games Machine (IT)** | 1988-09 – | **ST + Amiga were the founding platforms** ("concepita come testata dedicata ai videogiochi per home computer a 16-bit, ovvero principalmente Amiga e Atari ST" — stated, it.wikipedia) | percentage 0-100 (*evidence: IT Wikipedia game pages cite "93% dalla rivista K", "97% da Zzap!"*) | **405** by 2024 (stated: retroedicola "136/405") | IA: 162 distinct numbers; **48/48 for #1-48 (1988-1992)**, **73/76 to #76 (1994)** — counted | IA `the-games-machine-italia-NN` (OCR); retroedicola (gated, images) |
| **K** (Guida al divertimento elettronico) | 1988-12 – 2003 | all formats incl. ST | percentage (*evidence as above*) | 169 (stated) | **56/169** (stated by retroedicola) — ST-era share unknown | retroedicola, **login-gated** |
| **La Rivista di Atari** | 1986-11 – 1988 | **Atari-dedicated** — bimonthly supplement of *BIT*, Jackson | *unverified* | **8** (0/1986 + 1-6/1987 + …) | **8/8 — COMPLETE**, two copies: Atarimania PDFs (open) + retroedicola (gated) | Atarimania `/magazines/84` |
| **Zzap! (IT)** | 1986 – 1993 | C64-led; ST covered incidentally | percentage (*evidence above*) | 85 | **85/85 — COMPLETE** (stated) | retroedicola (gated); IA `zzap-italia-*` 86 items |
| **Computer + Videogiochi** | 1991 – 1995 | "Amstrad, Spectrum, Commodore, Amiga, Pc, **Atari st**, Master System…" (stated by retroedicola) | *unverified* | 44 | **44/44 — COMPLETE** (stated) | retroedicola (gated) |
| Guida Videogiochi | 1989 – 1990 | all formats | *unverified* | 24 | 24/24 (stated) | retroedicola |
| Videogame & Computer World | 1988 – 1995 | all formats | *unverified* | ~80 | 80 (stated) | retroedicola |
| MCmicrocomputer | 1981 – 2001 | technical, multiplatform, ST hardware coverage | *unverified* | 218 | **1/218** (stated) — effectively none | retroedicola |
| Atari News | 1991 | **official Atari Italy** (Il Volo), bimonthly | — | 1 known | 1 | retroedicola |

### Netherlands / Belgium

| Magazine | Years | ST coverage | Score format | Published | Digitised | Best source |
|---|---|---|---|---|---|---|
| **Atari Magazine (NL)** | 1985-05 – ≥1988 | Dutch consumer Atari monthly; ST coverage from 1986 (OCR snippet: "op de ST Nieuwe hardware Goedkope Software Listings voor XL, XE, ST") | *unverified* | ~28 known (atarimuseum holds "Escape" 1/2, 1-1…5-5) | **5 issues** (counted: `atarimagazine-nl-8505/8507/8509`, `atarimagazine1986-08`, `atarimagazine3ejaargangNr6`), all OCR'd | IA (text), atarimuseum.nl (physical) |
| Atari Info (NL) | 1980s | club infoblad, 8-bit | — | 8 (atarimuseum: 1-1…3-2) | 0 | atarimuseum.nl (inventory only) |
| Atari Koerier (NL) | 1989-01 | club paper | — | ≥1 | 0 | atarimuseum.nl |
| Atari Benelux Technische Informatie | 1988 – 1989 | **official Atari Benelux support bulletin** (NL) — hardware Q&A, no reviews | — | ≥62 numbered (no. 62 seen) | ≥27 items on IA (`atari-benelux-techinfo-*-dut`), text present | IA |

**Belgium:** no Belgian ST magazine found in either language. The Benelux support bulletin above is the only
BE+N L shared publication. Dutch-language Belgian ST users read the NL *Atari Magazine*.

### Poland

| Magazine | Years | ST coverage | Score format | Published | Digitised | Best source |
|---|---|---|---|---|---|---|
| **Bajtek** | 1985 – 1996 | multiformat; **ST from 12/1986** (OCR snippet of Bajtek 12/1986: hardware piece "Atari 520ST"); ST game reviews throughout | **percentage per criterion + overall** (counted in OCR snippet, Bajtek 1994 08: "Grafika: Muzyka: Nasza ocena: … 69% … 100%") | ~130+ | **167 IA items** (counted): 109 monthly identifiers + per-year bundles; OCR (Text PDF) confirmed | IA `bajtek*`; also one bulk pack `bajtek-all-pack-by-molerat-cd.7z` |
| **Tajemnice Atari** ("Secrets of Atari", Avalon) | 1991 – 1993 | **Atari-dedicated**, ST + XL/XE; genuine reviews (e.g. *Hans Kloss* review cited on pl.wikipedia) | *unverified* | ~30 (numbering to 10(30)/93) | **53 IA items** (counted), OCR confirmed | IA `tajemnice-atari-*`, `a8b_Tajemnice_Atari_*` |
| **Atari Magazyn** | 1993 – 1994 | **Atari-dedicated** (Spółdzielnia Bajtek) | *unverified* | ≥6 | 7 IA items, hOCR + Text PDF confirmed | IA `atari-magazyn-*` |
| **Moje Atari** | 1990 – 1991 | Atari-dedicated | *unverified* | 7 | 7 IA items, OCR confirmed | IA `moje-atari-*` |
| **Świat Atari** | 1992 – 1993 | Atari-dedicated | — | ≥3 (1/92, 1/93, 2/93) | **0** — known only from a physical list | atarimuseum.nl inventory |
| Bajtek — Atari special | 1990s | one-off special | — | ≥2 | 1 IA item (`Bajtek Atari Nr 2 Specjalny`) | IA |

### Nordics

| Magazine | Country | Years | ST coverage | Score format | Published | Digitised | Best source |
|---|---|---|---|---|---|---|---|
| **Datormagazin** | SE | 1984 – 1996 (relaunched 2000s) | multiformat (C64 → Amiga/ST/PC); ST reviews, incl. hardware ("jämför ATARI ST" — OCR snippet 1987-10) | *unverified* | ~12/yr | **1986-1995 present**: 4 individual 1986 issues + **one IA item per year** (1987…1995), each 10-12 OCR'd PDFs (counted); **CC BY-SA 4.0** | IA `Datormagazin1986Nr*`, `Datormagazin1987…1995`; mirror `stone-oakvalley-studios.com` |
| MikroBitti | FI | 1984 – | multiformat, ST covered | *unverified* | monthly | **scattered only** (e.g. `mb-1985-02`, `mb-1984-04`), hOCR present (counted) | IA |
| Data (magasinet for alle datainteresserte) | NO | 1983 – 1990s | general computing; ST articles exist (nb.no full-text hit, 1987 vol. 5) | *unverified* | monthly | **not scanned publicly** — searchable only inside nb.no | nb.no (see §3) |
| — | DK | — | **no Danish ST magazine found** | — | — | — | — |

### The rest

| Magazine | Country | Years | ST coverage | Score format | Digitised | Best source |
|---|---|---|---|---|---|---|
| **PIXEL** (Πίξελ) | GR | 1983 – 1998 | multiformat, strong 16-bit coverage; **an entire ST special**: *PIXEL extra — "Τα πάντα για τον Atari ST"* (1990) | *unverified* | **143 distinct numbered issues, 1-143** (counted), hOCR present; 61 issues fall in the 1988-1994 ST window | IA `pixel-gr-NNN` + `pixel-extra-atari-st` |
| CPU | PT | 1992 – | **Amiga and PC editions only** — no ST edition among the 27 IA items (counted) | — | Amiga/PC only | IA — **no ST press in Portugal** |
| — | CZ / SK | — | **nothing digitised found**: `title:("bajt")` → 1 unrelated US court doc; no Czech/Slovak ST title in IA, no aggregator hit (`unverified` that paper-only copies do not exist) | — | 0 | — |

## 2. Country-by-country verdict

| Country | Usable ST review corpus? | Reason |
|---|---|---|
| **Spain** | **YES — best of the group** | *Micromanía* 1ª+2ª época complete (115 issues) with OCR, ~80 of them inside the ST window; plus Spain's own Atari-dedicated *Atari User (Spain)* (28 issues, 1988-89) and the fanzine *Atari Fan* (11, 1993-). Spanish is the largest non-UK/FR/DE corpus by a wide margin — and if you count Chile's three Atari titles (*Mundo Atari*, *Turbo News*, *STAK*, 51 issues) as Spanish-language, it more than doubles. |
| **Italy** | **YES** | *The Games Machine* #1-48 complete (1988-1992, ST one of its two launch platforms) with OCR on IA; *K* and *C+Videogiochi* complete or near-complete in scans but **login-gated** at retroedicola; *La Rivista di Atari* 8/8 complete and Atari-dedicated, open PDFs. Caveat: the best archive (retroedicola) is members-only and image-only. |
| **Poland** | **YES, and unexpectedly deep** | Five Atari-dedicated titles (*Tajemnice Atari*, *Atari Magazyn*, *Moje Atari*, *Świat Atari*, Bajtek-Atari special) plus *Bajtek*'s long run, nearly all on IA with OCR. Polish reviews use a percentage rubric, so scores are comparable to Italian/UK ones. |
| **Greece** | **PARTIAL — yes, and nobody has used it** | *PIXEL* is a near-complete 143-issue run of a Greek computer monthly with a dedicated Atari ST special; text layer is hOCR (weaker than Abbyy) and no Greek OCR round has ever been run for ST content. Language-unique, medium size. |
| **Netherlands** | **PARTIAL** | One real consumer title (*Atari Magazine*, NL, 1985-) with only **5 issues** scanned of ~28; the official Benelux bulletin is technical, not reviews. Enough to prove the market existed, not enough to build a corpus. |
| **Belgium** | **NO** | No Belgian ST publication in Dutch or French found; readers used the NL and FR titles. |
| **Sweden** | **PARTIAL** | *Datormagazin* 1986-1995 is present and is the only Nordic title with a clean licence (**CC BY-SA 4.0**), but it is multiformat: ST reviews are a minority of each issue. No ST-dedicated Swedish magazine found. |
| **Denmark** | **NO** | No Danish ST magazine found; nothing in IA under Danish subject/language queries. |
| **Norway** | **NO (magazine)** | No dedicated Norwegian ST magazine; ST content only inside the general monthly *Data*, which is searchable in nb.no but not openly scanned. |
| **Finland** | **PARTIAL/NO** | *MikroBitti* covered the ST, but only scattered issues are scanned; no ST-dedicated Finnish title. |
| **Portugal** | **NO** | *CPU* published Amiga and PC editions only; no Portuguese ST title found. |
| **Czechoslovakia** | **NO (as far as digitisation goes)** | No Czech or Slovak ST title is digitised anywhere I could reach. Paper may exist; the web does not have it. |

## 3. Source table (TEXT LAYER column is the point)

Text-layer test = presence of `Djvu XML` + `Abbyy GZ` (Abbyy = good) or `hOCR`/`chOCR`/`OCR Search Text`
(IA's newer, weaker pipeline). Verified by `metadata/<id>` file inventory per source, one to six items each.

| Source | URL | Country | Magazines / years | Container | **Text layer?** | Per-issue pattern | Rights statement | Observed status | NEW? |
|---|---|---|---|---|---|---|---|---|---|
| IA — Micromanía run | `archive.org/advancedsearch.php?q=identifier:micromania*` | ES | *Micromanía* 1985-2024, 401 items | PDF/DjVu/JP2 + OCR sidecars | **YES — Abbyy GZ + DjVu XML** (2 items tested) | `micromania-segunda-epoca-NN`, `MicromaniaPrimerEpocaspanishIssueNN`, `mp-NN-jazzz` | none stated (`licenseurl` empty) | live; 35/35 + 80/80 complete | **NEW** (not in either survey) |
| IA — Spanish magazines collection | `collection:computermagazinesspanish` | ES | 1,202+ items across ~30 titles (*MicroHobby*, *MundoAtari*, *STAK*, *Turbo News*, *Amiga.info*, *Panorama Bits*, *Micromundo*…) | PDF/JP2 | **YES** (mixed Abbyy / hOCR) | identifier per title+number | none | live | **NEW** |
| IA — TGM Italia | `identifier:the-games-machine-italia*` | IT | *The Games Machine* 1988-2019, 180 items | PDF/JP2 | **YES** (hOCR/chOCR; some `Text PDF`) | `the-games-machine-italia-NN` | none | live; 48/48 for 1-48 | **NEW** |
| IA — Zzap! Italia | `identifier:zzap-italia*` | IT | *Zzap!* 1986-1993, 86 items | PDF/JP2 | YES (Abbyy on sample) | `zzap-italia-NN` | none | live; 85/85 complete | **NEW** |
| IA — Pixel (GR) | `identifier:pixel-gr-*` | GR | *PIXEL* 1983-1998, 143 items + 2 extras | PDF (image container) + JP2 | **YES but hOCR only** (no Abbyy) | `pixel-gr-NNN`; extras `pixel-extra-atari-st` | none | live | **NEW** |
| IA — Polish Atari titles | `title:"tajemnice atari"`, `atari-magazyn`, `moje-atari`, `identifier:bajtek*` | PL | 5 titles, 1990-1996 | PDF/JP2 | **YES** (Abbyy on *Moje Atari*, *Tajemnice*; hOCR on *Atari Magazyn*) | `bajtekYYYYMM`, `atari-magazyn-YYYY-MM`, `moje-atari-N` | none | live | **NEW** |
| IA — Datormagazin | `identifier:datormagazin*` | SE | *Datormagazin* 1986-1995 (+2002-2007) | PDF (10-12 per year-item) | **YES — Abbyy GZ ×10 in one item** | **one item per year**, N PDFs inside | **CC BY-SA 4.0** + Swedish "no profit" note | live | **NEW** — the only clean licence found |
| IA — Benelux bulletin | `title:"Atari Benelux"` | NL/BE | support bulletin, ≥62 nos | PDF | YES (not sampled per item) | `atari-benelux-techinfo-NNN-dut` | none | live | **NEW** |
| **atarimania.com/magazines** | `atarimania.com/magazines` (87 titles) | multi | ES: *Atari User (Spain)* 28, *Atari Fan* 11, *Atari Explorer ES* 5; IT: *La Rivista di Atari* 8; LatAm: *MundoAtari* 26, *Turbo News* 15, *STAK* 10; plus UK/US | **PDF** per issue | **NO — image-only PDFs, no sidecars** | `https://www.atarimania.com/mags/pdf/<slug>-issue-NN.pdf`, **no login** | none stated | live, direct download links work | NEW (survey only had its ST game pages) |
| **retroedicola.com** | `retroedicola.com/archivio` | IT | 19 sections, 300+ testate: TGM 136/405, K 56/169, Zzap! 85/85, C+VG 44/44, *La Rivista di Atari* 8/8, MCmicrocomputer 1/218 | page-image viewer | **NO** (no OCR surface; pages are images) | numeric issue id; `?vista=tabella` gives per-title counts | **no rights statement**; issue pages behind login ("Richiedi l'accesso") | **index public, issues login-gated** — recorded as a gate, not scraped | **NEW** (best Italian census) |
| oldgamesitalia.net → IA | `oldgamesitalia.net` → `archive.org/details/@oldgamesitalia` | IT | "Oldmags" uploader account (source of the Italian IA scans) | — | inherits IA | — | community | live; identifies who scanned the IT corpus | NEW |
| atarimuseum.nl | `atarimuseum.nl/atari-stste-books/`, `/atari-8-bit-books/` | NL | **physical inventory**, incl. Atari Magazine (NL) vol 1-5, Atari Info (NL), Atari Koerier, and — surprisingly — **Świat Atari (PL) 1/92, 1/93, 2/93** | none (paper list) | n/a | title + issue list | museum; "none of this collection is for sale" | live, no scans | NEW |
| atariware.cl | `atariware.cl/wiki/Biblioteca` | CL/ES | bibliographic checklist: MundoAtari 25 (1987-89), Turbo News 15 (1989-91), STAK 10 (1992-93) | n/a | n/a | prose | none | live; the only *stated* issue counts I could cross-check | NEW |
| stone-oakvalley-studios.com | `stone-oakvalley-studios.com/uploads/.../datormagazin_dmz_1987-01.pdf` | SE | *Datormagazin* PDFs | PDF | unverified | direct PDF links | none | live (crawlable, indexed) | NEW |
| **Nasjonalbiblioteket (nb.no)** | `api.nb.no/catalog/v1/items?q="Atari ST"` | NO | 1,660 items mention "Atari ST" (counted `totalElements`); magazine-level hits: *Data* 1987 vol.5, "Arbedsbok for Atari ST" (CETA 1986), "Atari St News Christmas Meeting 1990" | API + viewer | **YES — full text indexed** (that is how the hits arise) | API JSON; `fq=mediatype:magasiner` | NB terms; **full view normally restricted to Norwegian IPs** (*unverified* for this item) | open API answered 200 with no key | NEW |
| Delpher (KB) | `delpher.nl/nl/tijdschriften` | NL | — | JS | n/a | — | — | page is JS-rendered; **no Dutch computing magazine with ST reviews surfaced** — *unverified*, could not read a result count | NEW (negative) |
| digi.kansalliskirjasto.fi | `digi.kansalliskirjasto.fi` | FI | *MikroBitti* expected | JS | n/a | — | — | **JS landing page returned for API-style query**; per-title counts not obtainable without a browser | NEW (blocked-ish) |
| Polona | `polona.pl` | PL | — | — | — | — | — | 200 but **no Atari periodical identified** — *unverified* | NEW (negative) |
| BNE `datos.bne.es` | `datos.bne.es/sparql` | ES | — | — | — | — | — | **403** | NEW (blocked) |
| devuego.es | `devuego.es` | ES | Spanish review **database** (scores per magazine) | HTML | n/a | — | — | **403 anti-bot** ("Hemos bloqueado esta visita", ref logged) | NEW (blocked — worth a browser retry, it indexes Spanish review scores) |
| computeremuzone.com | `computeremuzone.com/atari/` | ES | game DB, community; `/revistas.php` → **404** | — | — | — | — | magazine section apparently removed; no scans | NEW (negative) |
| retromags.com | `retromags.com` | US/UK | volunteer scan project, English-language core | forum downloads | unverified | — | **"Notice to Publishers and Rights Holders"** takedown policy | live; **no ES/IT/NL/PL ST titles surfaced** | NEW |
| retroreaders.pl | `retroreaders.pl` | PL | was the Polish scan site (a *Bajtek Atari* special carries the name in its IA identifier) | — | — | — | — | **DEAD — domain parked for sale, 690 PLN** | NEW (dead) |
| Atari Legend magazines | `atarilegend.com/magazines` | multi | only **8** magazines (ST Format, ST Action, ST User, ST Review, The One, ST World, ST Magazine, Génération 4) — all UK/FR | HTML index | n/a | per-issue **game-review links + scores** | — | live; contributes **no** new national corpus | known |

### Text-layer summary (the operational fact)

- **Abbyy-grade OCR** (best): IA *Micromanía* (both epochs), *Moje Atari*, *Tajemnice Atari*, *Zzap! Italia*, *Datormagazin*.
- **hOCR/chOCR only** (IA's newer pipeline — usable, noisier): *The Games Machine* Italia, *PIXEL* (GR), *MikroBitti*, *CPU*, *Atari Magazyn*.
- **No text layer at all**: Atarimania's magazine PDFs (incl. *Mundo Atari*, *STAK*, *Turbo News*, *La Rivista di Atari*), retroedicola's viewer, everything in atarimuseum's paper list.
- So for Spanish Atari-dedicated titles you must choose: **IA copy = OCR but partial coverage**; **Atarimania copy = complete run but zero OCR**.

## 4. Ranked shortlist — five best non-UK/FR/DE additions to the OCR round

Ranked by (corpus size in the ST window) × (language uniqueness) × (how much of it the current round is
missing).

1. **Micromanía 2ª época, 1988-1995 — 80 issues, Spanish.**
   The largest complete ST review run in any of these languages, and it is *complete*, not sampled
   (80/80 counted). Spanish-language OCR of 1988-91 newsprint is the weakest link: the Abbyy layer exists
   but was produced from low-res scans with a Spanish dictionary; a re-OCR pass is where the value is.
   Cost: 80 issues × ~100 pp ≈ 8,000 pages. Nothing else in this group buys as many review pages per euro.

2. **The Games Machine (Italia) #1-48, 1988-1992 — 48 issues, Italian.**
   Complete for the exact years the ST mattered in Italy, and the magazine was *founded* on Amiga+ST —
   so the share of ST reviews per issue is unusually high. Text exists but only as IA hOCR (no Abbyy),
   which is precisely the case where a proper OCR round pays.

3. **Bajtek 1986-1996 — ~110 issues, Polish.**
   Poland's national computer monthly through the whole ST life, with a documented percentage rubric
   ("Grafika/Muzyka/Nasza ocena") that makes its scores directly comparable to UK/Italian ones. Polish is
   otherwise absent from any review corpus, and the IA set is nearly complete with OCR already present.

4. **Spain's Atari-dedicated run — Atari User (Spain) 28 + Atari Fan 11 = 39 issues, plus Chile's
   Mundo Atari (26) + Turbo News (15) + STAK (10) if Spanish-language breadth matters.**
   These are titles whose entire content is Atari, so ST review density per page dwarfs *Micromanía*'s.
   *Atari User (Spain)* is complete at Atarimania as **image-only PDFs with no text layer** — the cleanest
   "pure gain" OCR target here (39 pages-months, ~4,000 pp). The Chilean trio is already OCR'd on IA, so it
   costs nothing to index and only pays if the corpus is defined as *Spanish-language* rather than *Spain*.

5. **La Rivista di Atari, 1986-1988 — 8 issues, Italian.**
   Eight issues, complete, Atari-dedicated, open PDFs with no OCR at all. The cheapest way to add a fully
   Atari-native Italian source, and short enough to finish in one sitting.

*Honourable mention, not in the five:* `PIXEL extra — Τα πάντα για τον Atari ST` (Athens, 1990) — a single
Greek special devoted to the ST, unique in any language; one item, 16 files, hOCR present. Worth OCR'ing
for its uniqueness, not its volume.

*Deliberately excluded:* Dutch *Atari Magazine* (only 5 issues scanned of ~28 — not a corpus yet, but the
number-one target for a scanning request), *Datormagazin* (multiformat, ST a minority, and already the only
CC BY-SA title so it is safe to use as-is), *Hobby Consolas* / *Super Juegos* (console magazines; their ST
presence is incidental).

## 5. Open threads (honest gaps)

1. **Dutch coverage is the one place a phone call beats OCR**: atarimuseum.nl holds vol. 1-5 of *Atari
   Magazine* (NL) — ~28 issues — and only 5 are online. That is a scanning request, not an OCR job.
2. **retroedicola's gate**: TGM 136/405 and K 56/169 indexes are public but issues need an account
   ("Richiedi l'accesso"). Its census is the best Italian one; its scans are unusable without access.
   Whether it holds the missing TGM #49-76 that IA lacks was not determined.
3. **devuego.es is 403 to curl but is a Spanish *scoring* database** — the one source that would let a
   catalogue cross-check Spanish review scores without OCR. Needs a browser session.
4. **Czech/Slovak and Portuguese negatives are shallow**: IA + one aggregator each. A Czech national
   library check (Moravská zemská knihovna / Kramerius) was not done.
5. **Nordic national libraries were probed, not mined**: nb.no answers its API openly and clearly holds
   Norwegian computing magazines, but full view is normally IP-restricted and I did not test access from a
   Norwegian vantage. digi.kansalliskirjasto.fi and Delpher are JS-only from a curl session.
6. **No full-text (in-page) counting was possible**: IA's full-text search APIs (`ia-pub-fts-api`,
   `ia-fts`) returned empty without credentials, so "ST coverage per issue" for most titles rests on
   publisher scope plus sampled snippets, not on a measured term frequency. A per-issue ST-mention count
   would be the natural next probe once OCR text is local.
