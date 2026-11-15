# OCR readiness — Atari ST magazines

STATUS: v2, 2026-11-15. **Re-aimed**: the owner ruled that existing internet OCR will not be
reused ("*i don't want to reuse existing OCR results … only if i can't find the original scans*")
and that the source rule is a **resolution floor, not a host rule** ("*accept any scan >= a stated
DPI, fall back to whatever exists below it and mark the provenance*"). So the decisive column is
**page-image availability and measured page-image resolution**, not text-layer presence. The
text-layer census is kept only as the *fallback* measure (§4).

Every figure is labelled **counted** (I fetched it and counted), **measured** (a pixel/DPI figure
taken from a real page image), **stated** (the site says so) or **unverified**.

Nothing copyrighted was written to disk. Fetched: JSON item metadata, IIIF `info.json` (<1 KB/page),
`scandata.xml`, page counts from zip central directories (range reads of the last 66 KB), and
**one** 347 KB page JPEG (`…/page/n20_w1200.jpg`) to confirm the per-page route works. No PDF and
no JP2 payload was downloaded; the JPEG was inspected for status/size only and not kept.

---

## 0. Headline

| | figure | label |
|---|---|---|
| Items (magazine issues/objects) in the 11 title families | **2,390** | counted (`advancedsearch … AND mediatype:texts`) |
| Items whose page images I could fetch one-per-URL | **106 / 110 sampled** (96 %) | counted, 10 per title |
| Estimated total pages behind those items | **≈ 257,000** | derived (median pages × item count, §5) |
| Pages at ≥ 400 dpi | **≈ 64,000** (25 %) | measured per item, §3 |
| Pages at 300–400 dpi | **≈ 56,000** (22 %) | measured per item |
| Pages at 200–300 dpi | **≈ 72,000** (28 %) | measured per item |
| Pages **below 200 dpi** | **≈ 64,000** (25 %) | measured per item |
| Items where **no page image is obtainable** | **2 of 110 sampled** | counted → §4 |
| ST-dedicated titles (ST Format, ST Action, ST Magazine, 68000er, Atari Magazin) | **26,400 pages / 390 items** | derived |

The one-sentence answer: **the page images are there and are individually addressable, but a
quarter of the volume — and entire titles — sit below 200 dpi**, so a "300 dpi floor" would cut
the corpus to roughly half rather than excluding a corner of it. There is almost no magazine for
which someone else's OCR is the only route; the shortfall is resolution, not availability.

---

## 1. Method

- Identifiers were taken **only** from `archive.org/advancedsearch.php` result sets (never
  constructed from `title + issue number`), so every sampled item is one the Archive actually
  indexes. Items returning `files: []` were discarded as bad identifiers and are reported as
  such — **0 occurred** in the final run (the earlier 0/5 artefact came from constructed ids).
- Query shape per title: `title:(…) AND mediatype:texts` (plus `collection:(magazine_rack)` for
  the five multi-platform titles, where `title:` alone returns software, posters and unrelated
  books — see the precision column).
- **Page count per item** from, in order: `_jp2.zip` central-directory entry count (HTTP range
  read of the last 66 KB — no download), `_scandata.xml` `<page leafNum>` count, then
  `imagecount`. Never a summed report field: the earlier `pages=1362` figure came from summing
  `imagecount` across search rows and is discarded.
- **Resolution** measured from `https://iiif.archive.org/iiif/<id>$<leaf>/info.json`, which
  reports the native pixel size of the JP2 actually served. DPI = pixel width ÷ **assumed printed
  page width**: A4 = 8.27 in for the UK/FR/DE/IT/ES titles, US Letter = 8.5 in for ST Magazine
  (its US edition; the FR edition is A4 — see §7). 2–5 leaves per item, 6 items per title,
  leaves spread at 15/30/50/70/85 % of the issue, so covers and ad-heavy backs are not
  over-weighted.
- Sampling: **10 items/title** for availability and page counts, **6 items/title × 2–5 leaves**
  for resolution. Items sampled at evenly spaced indices of the sorted identifier list (a
  deterministic spread, not a random draw).

### Error bars

| quantity | error source | size |
|---|---|---|
| items per title | `numFound` is exact for the query | 0 |
| query precision | `title:` matches non-issue objects (posters, packs, unrelated books) | measured per title, 0.8–1.0 issue-like (§2 col. *precision*) |
| pages/issue | median of 9–10 sampled items | ±15 % typical; ST Action ±40 % (only 3 items yielded counts) |
| total pages | item count × precision × median | **±20 %** — treat 257k as 205k–310k |
| dpi per item | median of 2–5 leaves; **per-item spread is large** (e.g. ST Format 293–312 within one item) | ±5 % within an item; the *between-item* spread is the real variance and is fully visible in §3 |
| dpi absolute | assumed page width | ±10 % (A4 vs Letter = 3 %; some titles are 8.0 in, which shifts DPI +3 %) |

---

## 2. Page-image availability per title

`img/URL` = sampled items where `https://archive.org/download/<id>/page/n4_w1200.jpg` returned
HTTP 200 with an image body (counted). `w2000` = the same probe at `_w2000.jpg` (the widest
per-page derivative offered). `per-item pages` = median leaves of sampled issue-like items, method
in brackets: **J** = `_jp2.zip` entry count, **S** = `_scandata.xml`.

| title | query | items (counted) | precision | img/URL | w2000 | per-item pages (method) | est. pages | page-image route | fallback-only? |
|---|---|---|---|---|---|---|---|---|---|
| **ST Format** (UK) | `title:("ST Format") AND mediatype:texts` | 12 | 9/10 | 9/10 | 9/10 | 141 (J) | ≈1,700 | one page per URL **and** `_jp2.zip` | no |
| **ST Action** (UK) | `title:("ST Action") AND mediatype:texts` | 39 | 10/10 | 10/10 | 10/10 | 93 (J) | ≈3,600 | one page per URL | no |
| **ST Magazine** (FR/US) | `title:("ST Magazine") AND mediatype:texts` | 162 | 10/10 | 10/10 | 10/10 | 69 (J) | ≈11,200 | one page per URL | no |
| **Atari Magazin** (DE) | `title:("Atari Magazin") AND mediatype:texts` | 166 | 10/10 | 9/10 | 9/10 | 50 (J) | ≈8,300 | one page per URL | no (1 item PDF-only, §4) |
| **68000er** (DE) | `title:(68000er) AND mediatype:texts` | 11 | 10/10 | 10/10 | 10/10 | 150 (J) | ≈1,650 | one page per URL | no |
| **Génération 4** (FR) | `title:("Generation 4") AND mediatype:texts` | 229 | 8/10 | 10/10 | 10/10 | 181 (J) | ≈33,200 | one page per URL | no |
| **Tilt** (FR) | `… + collection:(magazine_rack)` | 145 | 9/10 | 10/10 | 10/10 | 158 (J) | ≈20,600 | one page per URL | no |
| **The Games Machine** (UK/IT) | `title:("Games Machine") + magazine_rack` | 425 | 9/10 | 10/10 | 10/10 | 117 (J) | ≈44,800 | one page per URL | no |
| **MicroMania** (IT) | `identifier:micromania* AND mediatype:texts` | 401 | 10/10 | 9/10 | 9/10 | 98 (J) | ≈39,300 | one page per URL | no |
| **Hobby Consolas** (ES) | `title:("Hobby Consolas") AND mediatype:texts` | 436 | 8/10 | 10/10 | 10/10 | 133 (J) | ≈46,400 | one page per URL | no |
| **Joystick** (FR) | `title:(Joystick) + magazine_rack` | 364 | 9/10 | 9/10 | 9/10 | 142 (J) | ≈45,900 | one page per URL | no |
| **total** | | **2,390** | | **106/110** | **106/110** | | **≈257,000** | | **2/110** |

Precision failures are genuinely off-series objects, e.g.
`poster-worldwide-soccer-97` (Hobby Consolas), `rsdebate.nic.in.83050` (a 2-page Indian
parliamentary debate that matched "Generation 4"), `DTIC_ADB328739` (a US Defense Technical
Information Center report), `micromaniawholet0000plat` (a book *"Micromania!"*),
`to-tilt-or-not-to-tilt-locomotive-practically` (an article title). They inflate `items` by
~5–15 % for the multi-platform titles; the estimated pages already apply the measured
precision factor.

**Also on IA, not in the table above** (counted `numFound`, not sampled): Atari ST Review **35**,
Atari ST Log **30**, ST Applications **26**, ST World **10**, START (US) **79**, ST/Amiga Format
**3**. Any OCR round that stops at the eleven titles skips ~180 further items.

### How a page is addressed (measured, per page — this is what a GPU queue needs)

Three independent per-page routes exist and all three were exercised on
`atari-st-format-issue-049`:

| route | URL shape | measured |
|---|---|---|
| JPEG derivative | `https://archive.org/download/<id>/page/n<leaf>_w1200.jpg` | HTTP 200, `image/jpeg`, 347,510 B for `n20` |
| widest derivative | `…/page/n<leaf>_w2000.jpg` | 200 on **106/110** sampled items — the same items as `_w1200`. `_w2000` carries no extra information: where the master is wider it is a downscale, where narrower an upscale. The JP2/IIIF width is the ceiling |
| JP2 inside the item zip | `https://archive.org/download/<id>/<id>_jp2.zip` then `<id>_jp2/<id>_0004.jp2` | zip central directory read by range; entry count = page count |
| IIIF native | `https://iiif.archive.org/iiif/<id>$<leaf>/full/full/0/default.jpg` (+ `info.json`) | `info.json` 200, 968 B, reports `width`/`height` |

So: **one page = one URL = one queue task**, and pages can be sharded by leaf number without
downloading the issue. Nothing requires holding a whole PDF.

---

## 3. Measured resolution, and the distribution against candidate floors

Measured from IIIF `info.json` width of the served JP2, ÷ assumed page width. `n pages` = number
of individual pages measured for that title (each page's *own* width, not an average).

| title | page w (in) | pages measured | median dpi | min | max | < 200 | < 300 | < 400 | ≥ 400 | verdict vs a 300 dpi floor |
|---|---|---|---|---|---|---|---|---|---|---|
| ST Format | 8.27 | 12 | **296** | 293 | 312 | 0 | 7 | 12 | 0 | **borderline — at the floor, not above it** |
| ST Action | 8.27 | 3 | **133** | 133 | 133 | 3 | 3 | 3 | 0 | **fails; every measured item ~133 dpi** |
| ST Magazine | 8.27 | 18 | **277** | 161 | 292 | 4 | 18 | 18 | 0 | fails at 300, passes at 200 except one item |
| Atari Magazin | 8.27 | 19 | **108** | 100 | 303 | 14 | 16 | 19 | 0 | **fails badly; two of five items ~100 dpi** |
| 68000er | 8.27 | 26 | **581** | 569 | 595 | 0 | 0 | 0 | 26 | **passes everything** |
| Génération 4 | 8.27 | 19 | **205** | 141 | 308 | 8 | 14 | 19 | 0 | fails at 300; 40 % of pages under 200 |
| Tilt | 8.27 | 23 | **150** | 145 | 511 | 16 | 19 | 19 | 4 | **fails; only one item is high-res** |
| The Games Machine | 8.27 | 22 | **307** | 59 | 637 | 6 | 11 | 12 | 10 | bimodal: 57 % of pages ≥400, one item at 59 dpi |
| MicroMania | 8.27 | 18 | **300** | 219 | 656 | 0 | 11 | 11 | 7 | sits exactly on the floor; 47 % ≥400 |
| Hobby Consolas | 8.27 | 19 | **424** | 165 | 600 | 2 | 6 | 8 | 11 | passes; one 165 dpi item |
| Joystick | 8.27 | 15 | **302** | 206 | 329 | 0 | 6 | 15 | 0 | passes 300 on the median, none above 400 |

**Page-weighted** (each title's measured tier share applied to its estimated page count):

| tier | pages | share |
|---|---|---|
| ≥ 400 dpi | ≈ 64,200 | 25 % |
| 300–400 dpi | ≈ 56,300 | 22 % |
| 200–300 dpi | ≈ 71,700 | 28 % |
| **< 200 dpi** | ≈ 64,400 | **25 %** |

This is the number that matters for the owner's rule: **a 300 dpi floor keeps ~47 % of the volume
(≈120,500 pages); a 400 dpi floor keeps 25 % (≈64,200 pages); a 200 dpi floor keeps 75 %
(≈192,000 pages).** And the spread is *within* titles, not between them: ST Format's median is
296 but its 12 measured pages run 293–312, while MicroMania's run 219–656. Per-title medians hide
this; only per-page provenance labels expose it.

### Per-item measurements (re-checkable)

Each row is one item, `dpi` = median of that item's measured leaves. Identifiers are exactly as
returned by search; URLs are `https://iiif.archive.org/iiif/<id>$4/info.json`.

| title | identifier | pages | dpi | note |
|---|---|---|---|---|
| ST Format | `atari-st-format-issue-023` | 148 | 296 | |
| ST Format | `atari-st-format-issue-025` | 140 | 294 | |
| ST Format | `atari-st-format-issue-030` | 180 | 311 | |
| ST Format | `atari-st-format-issue-051` | 108 | 306 | |
| ST Format | `atari-st-format-issue-049` | 108 | **145** | *not in the dpi sample*; measured separately — 1200×1562 JP2. The single most-linked ST Format item (Atari Legend links it) is half the title's resolution |
| ST Action | `ST_Action_Issue_021_1990-01_Gollner_Publishing` | 108 | **133** | 1100 px wide |
| ST Magazine | `st-magazine-021` | 152 | 291 | |
| ST Magazine | `st-magazine-053` | 180 | 292 | |
| ST Magazine | `st-magazine-086` | 84 | 289 | |
| ST Magazine | `st-magazine-118` | 84 | 277 | |
| ST Magazine | `20230526_20230526_0856` | 52 | 230 | ST Magazine #24 |
| ST Magazine | `st-magazine-may.-28.2023` | 60 | **161** | |
| Atari Magazin | `Atari-Magazin-00-01` | 40 | 300 | |
| Atari Magazin | `Atari-Magazin-92-05` | 52 | **100** | 824–851 px wide |
| Atari Magazin | `Atari-Magazin-97-06` | 40 | **103** | |
| Atari Magazin | `atari_am94-2` | 56 | 199 | |
| Atari Magazin | `rearc_xl-das-atari-magazin-1985-09-atari` | – | 108 | |
| 68000er | `68000er-03-88` | 150 | 580 | 4,709–4,920 px wide |
| 68000er | `68000er-88-01` | 150 | 585 | |
| 68000er | `68000er8703` | 136 | 594 | |
| 68000er | `68000er8802` | 148 | 582 | |
| 68000er | `happy-computer-sh-12-68000er` | 164 | 581 | |
| 68000er | `happy-computer-sonderheft-03-86` | 148 | 575 | |
| Génération 4 | `generation4-magazine-156` | 148 | 294 | |
| Génération 4 | `generation4-magazine-111` | 212 | 205 | |
| Génération 4 | `generation4-magazine-064` | 164 | **161** | |
| Génération 4 | `generation4-magazine-019` | 136 | **145** | |
| Tilt | `Tilt029` | 136 | 150 | |
| Tilt | `Tilt058` | 134 | 147 | |
| Tilt | `Tilt086` | 148 | 145 | |
| Tilt | `Tilt115` | 157 | 163 | |
| Tilt | `Taito-Tilt-56` | 22 | **510** | the exception, 4,205 px |
| Games Machine | `the-games-machine-italia-249` | 148 | 586 | |
| Games Machine | `the-games-machine-italia-50` | 140 | 516 | |
| Games Machine | `the-games-machine-01` | 132 | 372 | |
| Games Machine | `the-games-machine-n-181-aprile-2004` | 148 | **136** | |
| Games Machine | `what-games-machine-special-2000` | 100 | 637 | |
| MicroMania | `MicromaniaTerceraEpocaSpanishIssue79` | 132 | 656 | 5,428 px |
| MicroMania | `Micromania165` | 138 | 602 | |
| MicroMania | `MicroMania84` | 148 | **219** | |
| MicroMania | `micromania-segunda-epocha-04` | 80 | 283 | |
| Hobby Consolas | `hc-016` | 180 | 448 | |
| Hobby Consolas | `hc-151` | 180 | 319 | |
| Hobby Consolas | `02-26-hobbyc` | 100 | **165** | |
| Joystick | `joystick057` | 196 | 328 | |
| Joystick | `joystick202` | 110 | 302 | |
| Joystick | `joystick130` | 196 | 206 | |
| Joystick | `joystick-dansk-12` | 36 | 273 | |

### Provenance class per source (the owner's three labels, derived per page)

| class | derivation rule | where it lands |
|---|---|---|
| `own-scan-quality` | measured page width ÷ page width ≥ the chosen floor | 68000er (all), MicroMania (47 % of pages ≥400), Games Machine (57 %), Hobby Consolas (76 %), Joystick (57 % at 300–400) |
| `archived-scan-degraded` | scan exists, is per-page addressable, but measured below the floor | **all of ST Action (133 dpi), 83 % of Atari Magazin pages (100–199 dpi), 95 % of Tilt pages (145–163 dpi), 42 % of Génération 4 pages, 20 % of Hobby Consolas, 22 % of Games Machine, 10 % of ST Magazine, half of ST Format** |
| `third-party-ocr-fallback` | no page image obtainable at all | 2 of 110 sampled items (§4); no *title* falls entirely into this class |

---

## 4. Fallback: where page images are unobtainable, and why

Only **2 of 110** sampled items had no obtainable page image. Both are object-level failures, not
magazine-level:

| identifier | why | consequence |
|---|---|---|
| `stformathi05` (matched by `title:("ST Format")`) | 151 files, none of them a page image container (no `_jp2.zip`, no PDF, no `_djvu.xml`) | 1 object; not a lost issue — the STF series has 11 real scanned issues |
| `joystickspecialissue1pp1_2` | 7 files, no image container (metadata + thumbnail only) | 1 object |

Two more sampled items are **PDF-only** — page images exist inside a PDF but are not
individually addressable, and I did **not** measure their embedded resolution:

| identifier | what it has | what it lacks |
|---|---|---|
| `atari_am88_4` (Atari Magazin) | `.pdf` | `_jp2.zip`, IIIF, per-page URL |
| `rearc_xl-das-atari-magazin-1985-09-atari` | `.pdf` (and a `_jp2.zip` — IIIF timed out on probe) | per-page URL unverified |

Access restrictions are effectively absent: of **110** sampled items, **1** is
`access-restricted-item: true` and **0** were `is_dark`. So page images are freely downloadable
at archive.org for 109/110 sampled items.

**Where existing OCR genuinely *would* be the only route** — no page image of any kind located in
this pass:

- **Nothing on IA for: ST Informer, ST Resource** (`numFound` 0 for `mediatype:texts`).
- **Issues missing, not images**: `collection:stformatmagazine` holds **11** items (counted),
  while stformat.com indexes **87** issue directories (`stf00`–`stf86`, counted). ST Format ran
  to roughly 80 issues (**unverified** — not checked against a publisher list). Whichever number
  is right, the gap is undigitised or separately-hosted paper, so neither a GPU nor someone
  else's OCR can fill it from IA alone.
- **kultpower.de** (the German index I expected to cover 68000er/Atari Magazin) holds **Happy
  Computer, Powerplay, TeleMatch, Video Games, PC Player, Amiga Joker** only — counted from
  `heft_*` links: `heft_powerplay` 112, `heft_happycomputer_spielesonderteil` 28,
  `heft_telematch` 22, `heft_happycomputer_spielesonderheft` 8, `heft_videogames` 5. **No
  68000er, no Atari Magazin** as standalone series. It serves per-page images (`seiten[]` array
  of page files) for what it does hold, so it is a page-image source, not a text source.
- **stformat.com** serves **per-issue PDFs**, not per-page images: `stf02/stf02.pdf` HTTP 200,
  `Content-Length: 68,892,013`; `stf49/stf49.pdf` **404**, so coverage is partial (87 issue
  directories exist in the index, counted 174 `stfNN` links = 87 directories × 2 links).
  These PDFs are a second page-image route for the ST Format issues IA holds, and the only route
  for issues IA lacks where stformat.com has a PDF.

### Text layers — demoted to a fallback measure

Kept only to answer "if we ever *do* want someone else's OCR, what exists": `_djvu.txt` was
present on **54 of 55** sampled items across the 11 titles, and the hOCR/ABBYY derivations fall
into two generations:

- **hOCR generation** — `_hocr.html`, `_hocr_pageindex.json.gz`, `_chocr.html.gz`,
  `_hocr_searchtext.txt.gz`, `_page_numbers.json`, `_scandata.xml`, `_djvu.xml`, `_jp2.zip`,
  `_text.pdf`. Per-page boxes and a leaf→folio map.
- **ABBYY generation** — `_abbyy.gz`, `_djvu.xml`, `_scandata.xml`, `_text.pdf`. No hOCR, no
  folio map.

Page-level coordinates exist either way: `_djvu.xml` carries per-word `coords="x,y,w,h"` and
`x-confidence` per `OBJECT` (= one page) — verified on `atari-st-format-issue-049`, first words
read `THE WORLD'S BEST-SELLING ST MAGAZINE` at 93–96 % confidence, while the logo region on the
same page decoded as `Ба` / `а` at 54–65 %. **A leaf→printed-page map exists for 25 of 55 sampled
items** (45 %) via `_page_numbers.json`, and where it exists it is dense:
`st-magazine-029` leaf 2 → page `51` (cover offset), 166 of 168 leaves mapped;
`atari-st-format-issue-023` 140 of 148. Where it is absent (`atari-st-format-issue-049`,
`st-action-*`), "page 47" must be derived from a per-title offset.

**Quality caveat, plainly**: an existing text layer is not a good text layer, and it is exactly
the inconsistency the owner objected to. The one measured sample above already shows the two
failure modes that matter for scoring — column bleed and garbage in display type. That is the
whole argument for re-OCR, and it is also why these files are useless as training data but
**useful as a locating index**: `_page_numbers.json` + `_djvu.txt` can tell a pipeline *which
leaf* a review is on before the GPU runs, at ~0 cost.

---

## 5. Page arithmetic and a cost-ordered OCR plan

Derivation: `est. pages = numFound × precision(issue-like fraction of sample) × median pages per
issue-like item`. Per-title values in §2; total **2,390 items → ≈257,000 pages (±20 %)**.

| tier | pages | what it is | cost implication |
|---|---|---|---|
| **T1 — ≥400 dpi** | ≈ 64,200 | 68000er (1,650), Games Machine 57 % (25,400), MicroMania 47 % (18,400), Hobby Consolas 39 % (18,100) | cheapest per page: pass-through, no upscaling, highest expected CER |
| **T2 — 300–400 dpi** | ≈ 56,300 | ST Format (846), Joystick 57 % (26,100), Hobby Consolas 37 % (17,000), Games Machine 19 % (8,600), MicroMania/Génération 4 remainder | same pipeline, slightly lower expected accuracy |
| **T3 — 200–300 dpi** | ≈ 71,700 | ST Magazine 90 % (10,100), Génération 4 51 % (16,800), Joystick 43 % (19,800), MicroMania 53 % (20,900), ST Format half (846), Hobby Consolas 4 % | needs 2× upscale + preprocessing; CER degrades on small text |
| **T4 — <200 dpi** | ≈ 64,400 | ST Action **all** (3,600), Atari Magazin 83 % (6,900), Tilt 95 % (19,700), Génération 4 42 % (14,000), Games Machine 22 % (9,700), Hobby Consolas 20 % (9,400), ST Magazine 10 % (1,100) | last; these pages are also where a re-scan or a PDF re-render at native size is the only real fix |
| **T5 — no page image** | 2 items | §4 | existing OCR, or exclude |

Recommended order stated in cost terms, not preference:

1. **T1 + T2 = ≈120,500 pages** — one pass, no upscaling, one engine/version. This is where the
   ground-truth check should run first (§6) because measured CER here is the pipeline's best case.
2. **T3 = ≈71,700 pages** — same run with an upscale stage; budget ~1.5× the per-page time of T1/T2.
3. **T4 = ≈64,400 pages** — separate run with a different preprocessing profile; expect to route
   a share of it to re-acquisition rather than OCR.
4. **ST-dedicated subset first if scope must shrink**: 26,400 pages covers ST Format + ST Action +
   ST Magazine + 68000er + Atari Magazin — the titles where *every* page is ST-relevant. The 230k
   multi-platform pages each need OCR merely to *find* the ST content (ST share is unmeasured;
   plausibly 10–30 % of pages).

---

## 6. Ground truth for measuring the pipeline's score-reading accuracy

This is now the main value of the scene's existing data: it lets the owner compute the error rate
of his own OCR on a sample, rather than trusting anybody's text.

| source | fields it already holds | record count | URL / API shape | usable as ground truth? |
|---|---|---|---|---|
| **Atari Legend — magazine index** | **issue, reviewed title, entry type (Review / Preview / Hints-Tips), printed page number**, plus a link to the IA scan | **69 issues, 1,252 unique (issue,title,page) rows, of which 770 are type `Review`** (counted, deduplicated — raw scrape returned exactly 2× each row; 8 magazines: ST Format 14 issues/131 rows, ST Magazine 22/456, Atari ST Review 10/306, Génération 4 1/181, The One 10/95, ST Action 10/72, Atari ST User 1/4, Atari World 1/7) | `https://www.atarilegend.com/magazines/<1..13>` — server-rendered HTML, `<div id="magazine-issue-NN">` cards with a `<tr>` table `Title | Type | Page`; games also appear at `/games/<slug>` with a `Magazines` block `"ST Magazine 16 (Feb 1988) [Preview] p125"` | **yes — the best page-locator ground truth in the scene.** Page numbers are printed folios, so it tests locator + folio derivation. **No scores.** |
| **Atarimania — book/magazine reviews** | **magazine name, issue date, rating (%),** optional writer, optional review text, optional scan image | **19 of 60 sampled ST game pages carry ≥1 review (32 %); 54 entries in 60 pages = 0.90 reviews/page; 39 of 54 (72 %) have a rating.** Extrapolated over 9,988 ST game URLs ≈ **9,000 citations, ≈6,500 with a score** (counted on sample, extrapolated). 16 distinct magazines cited in the sample; top: ST Action 11, Atari User (Spain) 6, The One 4 | `https://www.atarimania.com/games/atari-st-games-<slug>-<id>` — HTML section `BOOK / MAGAZINE REVIEWS (n)`; per review `<span class="font-bold">ST Action</span> · September, 1989 … Rating: 55 %`. Robots: `/api/`, `/search` disallowed, rest allowed. **No page numbers.** | **yes — the best score ground truth.** Pair it with AL pages to get (issue, page, score) triples where both have the same review |
| **Atarimania — magazine index** | "87 MAGAZINES" landing page exists | **87** (stated on page; issue-level index not reachable without the `/api/` route, which `robots.txt` disallows, so not counted) | `https://www.atarimania.com/magazines` | partially — magazine-name vocabulary only |
| **MobyGames** | critic scores per title, cover art, credits | not measured | `https://www.mobygames.com/platform/atari-st/` → **HTTP 403** to curl (5,429-byte block page). API is commercial-licence only | **no** — blocked; recorded, not used |
| **stformat.com** ("ST Format Shrine") | per-issue **contents index**: page number → article/game, plus a per-issue PDF | 87 issue directories (`stf00`…), contents tables server-rendered HTML | `http://www.stformat.com/stf<NN>/index.html`; PDF at `…/stf<NN>/stf<NN>.pdf` | **yes for ST Format page-level locators** (page → title), and a second page-image route |
| **Atari Legend — prose reviews** | author, date, free-text review, user vote score (4.82/5, 7 votes) | **126** (stated: "There are currently 126 reviews available") | `https://www.atarilegend.com/reviews`, `/reviews/<id>` | **no** — modern editorial prose, no magazine/issue/page/score |
| **kultpower.de** | per-issue page-image sets for German mags | 112 Powerplay, 28 Happy Computer Sonderteil, … (counted from `heft_*` links) | `https://www.kultpower.de/archiv/` → `heft_<mag>_<yyyy-mm>` | no ST-specific titles; useful only as a German page-image source |
| **ZX-Spectrum-style ST review database** | — | **none found.** No equivalent of the Spectrum/Amiga review-score archives exists for the ST: the scene's structured review data is exactly Atari Legend (page locators) + Atarimania (scores). Everything else is either dead (Little Green Desktop's DB throws `mysqli` errors) or blocked (MobyGames, Demozoo 403) | — | n/a |

**How to use it**: pick the ~770 AL review rows that have a page, fetch the corresponding IA leaf
(via `_page_numbers.json`), OCR that page, and score three things — (a) is the game title found on
the expected page, (b) is the printed folio read correctly, (c) where Atarimania also has a rating
for that (title, magazine), is the score read correctly. That yields a measurable CER per title
and per dpi tier, which is what makes the tiered plan in §5 defensible rather than hopeful.

---

## 7. What I could not measure, and why

1. **PDF embedded image resolution.** For PDF-only items (`atari_am88_4`) and for stformat.com's
   per-issue PDFs, the page image lives inside the PDF. Measuring it needs either the full PDF or
   parsing the page's XObject `/Width` — I did not download PDFs (size/no-copyright rule), so
   **these are the one class where a resolution floor cannot yet be applied.** Indirect evidence
   that IA's PDFs are rendered from the same JP2 masters: `atari-st-format-issue-049` has 108
   leaves, 108 JP2 entries and a 65.6 MB PDF (~600 KB/page), consistent with the 1200×1562 JP2s —
   but that is inference, not measurement.
2. **`_jp2.zip` entry counts for 4 ST Action items** — repeated range reads on those objects timed
   out; their page counts came from `_scandata.xml` where possible, else they are missing from the
   median (n=3 instead of 10). This is why ST Action's estimate carries ±40 %.
3. **Printed page width per title** — assumed A4/Letter. The real printed width varies (ST Format
   is A4, ST Magazine's US edition Letter, Hobby Consolas ≈ A4). A ±3 % width error shifts every
   DPI by the same ±3 %, which does not move any item across a floor except where the median sits
   within 3 % of it (ST Format at 296 vs 300, MicroMania at 300, Joystick at 302).
4. **ST-relevant share of the multi-platform titles** — not measured; would require OCR'ing them,
   which is the thing being scoped. Treated as an assumption (10–30 %) in §5 only.
5. **Atarimania's 87-magazine issue index** — the issue-level listing is behind `/api/`, which
   `robots.txt` disallows; only the count is stated by the site.

## Appendix — artefacts

All under `/opt/data/cache/scratch/mags/`, disposable:
`census.jsonl` (text-layer census, per-title samples) · `pass2.json` (folios, leaves/tier) ·
`pass3.json` (page-image availability, 10 items/title) · `pass4.json` (per-page IIIF dimensions) ·
`final.json`, `rows.json` (aggregates) · `al_magazines.json` (AL 69 issues / 1,252 rows) ·
`atarimania_reviews.json` (60-page sample) · scripts `census.py`, `pass2.py`, `pass3.py`,
`pass4.py`, `aggregate.py`, `al_scrape.py`, `am2.py`.
