# The ST-era magazines, their review pages and their scan sources

STATUS: **complete (v1)** — 2026-11-15. Five parallel channels (UK/US, FR, DE, rest-of-Europe, OCR-readiness),
re-aimed twice mid-run by the owner's rulings (page images over text layers; a resolution floor over a source
hierarchy). The unedited channel reports are preserved beside this note in `research/magazines/` — they carry
the per-issue tables this note summarises.

Evidence labels: **counted** (fetched and parsed; method stated), **stated** (the source claims it),
**unverified**. `✓ lead` marks a figure I re-derived myself after the channels reported; `⚠ channel-only`
marks a figure I could **not** reproduce and which therefore rests on the channel's own count — the
re-verification log (§7) names each one.

## 0. What this establishes, in six lines

1. **The review corpus is real and large.** France alone has **2,832 ST-tagged review entries with a score and
   a printed page number** (Tilt 1,371 · Génération 4 1,032 · Joystick 429) in machine-parseable HTML ⚠
   channel-only. Atari Legend's own index holds **1,252 rows / 770 reviews** with printed folios, and
   Atarimania's game pages carry **score-bearing citations** for ~16 magazines ⚠ channel-only.
2. **Score formats disagree across the corpus, and some disagree within one magazine over time** — Tilt went
   `1–6` → `x/20` (from #39, Feb 1987) → `%` (from #113, Apr 1993); Génération 4 did `%` → `/6` → `/20`; ACE
   scored **out of 1000**; 68000er used a German school decimal (`GN 1.83`). A single `score` column cannot
   hold this; §2 is the vocabulary problem the schema must solve.
3. **Page images are obtainable almost everywhere, one page per URL** — 106 of 110 sampled Internet Archive
   items serve `/download/<id>/page/n<leaf>_w1200.jpg` (**✓ lead: HTTP 200, 352,887 B, `image/jpeg` for
   `atari-st-format-issue-049` page 3**), plus IIIF and JP2-in-zip. That is what makes a page-level GPU queue
   possible without splitting PDFs.
4. **Resolution, not availability, is the constraint.** Across ≈257,000 pages (±20 %): **25 % ≥400 dpi, 22 %
   300–400, 28 % 200–300, 25 % <200** ⚠ channel-only. The spread is *within* titles — ST Action measured at
   **133 dpi on every sampled item**, Tilt 145–163, 68000er 569–595, Atari Magazin 100–303 — so a 300 dpi
   floor keeps ~47 % of pages and silently drops one magazine entirely.
5. **Two sources make OCR unnecessary for some titles**: ST Review 35/35, Zero 37/37 and ACE 55/55 are
   complete *and* already carry text layers ⚠ channel-only; and German `stcarchiv.de` is reportedly an
   **article-level transcription** (223 ST-Computer issues and four other titles, HTML with score blocks) —
   the single most valuable claim in the batch, and the one I could not reproduce (§7).
6. **A golden set for measuring our own OCR exists**: ~770 review rows with title, type and printed page from
   Atari Legend, plus Atarimania's citations (no page numbers). That turns "is our OCR any good" into a
   score-reading error rate instead of an opinion.

## 1. The magazines that mattered, by country

`Issues` = published / digitised (or, where publication is unrecorded, digitised only). Every row carries its
channel's label; none is a circulation claim unless it says so.

### UK and USA

| Title | Years | ST coverage | Score format | Issues | Best scan source |
|---|---|---|---|---|---|
| **ST Format** | 1989–1996 | dedicated-ish, ST-first | `%` | **89 / 86** at Atarimania, **only 11** on IA | Atarimania `/magazines` (image-only PDFs, ~46 MB/issue, **no text layer**) |
| **ST Action** | 1988–1993 | dedicated | `%` | **68 at Atarimania**, 33 OCR'd on IA | Atarimania; IA partial |
| **ST Review** | 1991–1995 | dedicated | split "technical / silly" `%` | **35 / 35, complete + text layer** | IA (free calibration run) |
| **The One** | 1988–1995 | Amiga-first, strong ST section | `%` overall + four sub-scores | 92 scored rows indexed (Atari Legend) | Atari Legend index + scan hunt |
| **Atari ST User** | 1986–1994 | dedicated | `%` | 102 published | Atarimania |
| **ACE** | 1987–1992 | multi-format, ST covered | **out of 1000** (`625/1000`) | **55 / 55, complete + text layer** | IA |
| **Zero** | 1989–1992 | multi-format | `%` | **37 / 37, complete + text** | IA |
| **ST-Log**, **STart** | 1986–1990 | US Atari | `%` / letter | 36, 42 published | Atarimania |

Circulation, the only three real figures found: **ST Format 43,007** (Aug–Dec 1989, stated on stformat.com),
**Atari ST User 33,706** (Jul–Dec 1988), **ACE 48,170** (c. Apr 1991). Everything else in the channels is a
digitisation proxy, and is labelled as such rather than presented as sales.

### France

| Title | Years | ST coverage | Score format | Digitised | Source |
|---|---|---|---|---|---|
| **Tilt** | 1982–1994 (ST from #24, Sept 1985; real reviews from #32) | strong, multi-format | **no overall mark**: `Intérêt` numeric + 3 star rows (Graphisme/Animation/Bruitage) + `Prix`; index track `1–6` → `x/20` (#39) → `%` (#113) | 1,371 ST-tagged rows | abandonware-france.org issue indexes (no scans) |
| **Génération 4** | 1987–1998 | strong | `%` (#1–82) → `/6` → `/20` | 1,032 rows | same |
| **Joystick** | 1990–2012 | strong | `%` throughout | 429 rows | same |
| **ST Magazine** | 1989–1997 | dedicated ST | **no scores at all** — prose verdict only | 22 issues indexed at Atari Legend | abandonware-magazines (one **JPEG per page**, per-page URL) |

Tilt and Génération 4 score **each machine separately inside one entry** (`14/20|15/20|15/20`); Joystick gives
one `%` for all versions; ST Magazine has no score. First ST-tagged Tilt item is #24 (Sept 1985) ⚠ channel-only.

### Germany

| Title | Years | Dedicated? | Score format | Digitised | Source |
|---|---|---|---|---|---|
| **ST-Computer** | 1987–1998 | **dedicated** | `Wertung` block → Grafik/Sound/Motivation/Gesamtwertung **0–10** | 223 issues transcribed | `stcarchiv.de` (HTML articles, no images) ⚠ §7 |
| **ST-Magazin / 68000er** | 1986–1996 | dedicated from 4/88; shared before | `Note: X von 6`; 68000er `GN 1.83` (school decimal) | 66 / 12 | Atarimania PDFs (TOS ones carry text), IA |
| **TOS** | 1990–1994 | dedicated | **1–10** | 39 transcribed, 38 PDFs | stcarchiv + Atarimania |
| **Atari Magazin** | 1988–1992 | shared (8-bit + ST → XL/XE-only) | prose + `Fazit:`, no box | 24 transcribed, 66 PDFs, 73 on IA | mixed |
| **Atari Inside**, **Happy Computer**, **Power Play**, **ASM** | 1980s–90s | shared | Power Play `%` (unverified) | 13 / 67 | partial |

`kultpower.de` holds 102 Hefte as page JPEGs with a 10,306-article catalogue — but **no ST title and no ASM**
in that catalogue, so it is a German-scan source without ST review content.

### Rest of Europe

| Country | Verdict | Titles | Source quality |
|---|---|---|---|
| **Spain** | **yes, the best of the group** | **Micromanía** 35/35 (1985–88) + **80/80** (1988–95), all with an Abbyy OCR layer; **Atari User (Spain)** 28 issues from 1988 (Atarimania PDFs, **no text layer**) | complete runs on IA |
| **Italy** | **yes** | **The Games Machine** #1–48 complete (ST was a launch platform), 73/76 to 1994, **hOCR only**; La Rivista di Atari 8/8 (Atarimania, no OCR); **K, C+Videogiochi, Zzap!** exist only in `retroedicola.com`, **login-gated and image-only** | IA + one gated host |
| **Poland** | **yes, unexpectedly deep** | **Bajtek** 167 IA items with OCR; four Atari-dedicated titles (Tajemnice Atari 53, Atari Magazyn, Moje Atari, Świat Atari); scored as `%` per criterion | IA |
| **Greece** | partial, unique | **PIXEL** 143 issues + a 1990 ST special, hOCR only | IA |
| **Netherlands** | partial | Atari Magazine (NL) from May 1985 — **5 of ~28** scanned (the rest are physical at atarimuseum) | IA |
| **No corpus found** | — | Belgium, Denmark, Norway (ST only inside general *Data*), Portugal (Amiga/PC editions only), Czechoslovakia | — |

**Correction recorded**: *Mundo Atari*, *Turbo News* and *STAK* — listed as Spanish in circulating checklists —
are **Chilean** (IA metadata says Santiago de Chile). They are a Spanish-**language** corpus, not Spain's.

## 2. The score vocabulary problem (schema-critical)

Six incompatible scales in the same corpus, one of them with no overall mark, one of them a school grade where
**1 is best**, one of them out of 1000:

| Scale | Where | Trap |
|---|---|---|
| `%` (0–100) | ST Format, ST Action, Joystick, most UK/German | looks universal, is not |
| `x/20` then `%`, and `1–6` before that | Tilt, Génération 4 | **changes within one magazine's run** — the issue date decides the scale |
| `x/6` | Génération 4 mid-run, ST-Magazin (`Note: X von 6`) | `/6` vs `%` are not convertible without a rule |
| `0–10` | ST-Computer (`Gesamtwertung`), TOS | sub-scores exist only as graphics in print |
| decimal school grade, 1 = best | 68000er (`GN 1.83`) | **lower is better** — inverting this silently is the worst possible bug |
| `/1000` | ACE (`625/1000`) | a `%` reader would record 625 % |

So the catalogue cannot store "a score"; it must store **`score_value` + `score_scale` + `score_direction`**,
with the magazine's rule for the issue's date recorded per magazine. That is one rule (scale is data, not
column) instead of six special cases, and it makes "which games scored ≥80 %" answerable only by converting
through the recorded scale.

## 3. Scan sources

| Source | Holds | Container | Page addressing | Resolution | Rights |
|---|---|---|---|---|---|
| **archive.org** magazine items | ~2,390 items, ≈257,000 pages | `_jp2.zip`, PDF, `_djvu.txt`, `_hocr` | **one page per URL** `/download/<id>/page/n<leaf>_w1200.jpg` ✓ lead; IIIF; JP2 entry | 100–595 dpi measured | IA ToS |
| **Atarimania `/magazines`** ✓ lead (200) | **87 magazines**, full runs (ST Format 89, ST Action 68, Atari ST User 102, ST-Log 36, STart 42, ACE 55, ST Review 35) | image-only PDFs, ~46 MB/issue, **no text layer** | PDF per issue → must split | not measured | no licence |
| **abandonware-france.org** `/bibliotheque/magazines/` ✓ lead (200) | issue indexes with scored review rows + printed pages | HTML index (no scans) | per-magazine index | n/a | no licence |
| **abandonware-magazines.org** | French runs (Tilt, Gen4, Joystick, ST Magazine) | **one JPEG per page at a per-page URL** (album viewer) + one ZIP per issue — **no PDFs** | per-page ✓ best case | not measured | no licence |
| **stcarchiv.de** | the German press as **article-level HTML transcription** (223 ST-Computer + 66 ST-Magazin + 39 TOS + 24 Atari Magazin + 13 Atari Inside + 67 Happy Computer) | HTML, **no PDFs, no images** | `/{mag}/{yyyy}/{issue}` → `…/{slug}` | n/a (text) | ⚠ not reproduced (§7) |
| **IA French/German items** | Tilt, Gen4, Joystick, ST Magazine, st-computer, Atari-Magazin, 68000er | `_djvu.txt` on 24/24 French and 54/55 general samples | per page | varies | IA ToS |
| **kultpower.de** | 102 Hefte, German | page JPEGs | per issue | not measured | no ST review catalogue |
| **retroedicola.com** (IT) | K, C+Videogiochi, Zzap! | image-only | login-gated | — | gate |
| **Atari Legend `/magazines`** | per-issue contents with inline scores + page numbers | HTML index | `/magazines/<id>?page=N` | n/a | no scans |

## 4. Page arithmetic for the GPU round

| Bucket | Pages | Note |
|---|---|---|
| All IA magazine pages in the ST-relevant set | **≈257,000** (±20 %) | 2,390 items; derived per item from `_jp2.zip` EOCD → `scandata.xml` → `imagecount` |
| Of which **ST-dedicated titles** | **≈26,400** | the rest are multi-format magazines that must be *searched* for ST content |
| ≥ 400 dpi | ≈64,000 (25 %) | 400 dpi floor keeps a quarter |
| 300–400 dpi | ≈56,000 (22 %) | 300 dpi floor keeps ~47 % |
| 200–300 dpi | ≈72,000 (28 %) | |
| < 200 dpi | ≈64,000 (25 %) | **ST Action sits at 133 dpi on every sampled item** |
| Fallback (no page image at all) | **2 items** (`stformathi05`, `joystickspecialissue1pp1_2`) + 2 PDF-only items whose embedded resolution could not be measured | the fallback list is object-level, not title-level |

**Read this before sizing the 5090 round**: the number that matters is not 257,000 pages but *pages above the
chosen floor, for the titles you actually want*. At a 300 dpi floor that is ≈120,000 pages; the ST-dedicated
titles are only ≈26,400 of them, so a first round aimed at "every ST-dedicated issue" is roughly a fifth of the
work of "every page that mentions the ST".

## 5. Ground truth available for measuring our own OCR

| Source | Fields | Volume | Usable how |
|---|---|---|---|
| **Atari Legend `/magazines/<id>`** | `Title | Type | Page` with printed folios | **69 issues, 1,252 rows, 770 `Review`** | the only index with **page numbers** — a golden set for "did we find the review and on which page" |
| **Atarimania game pages** | `BOOK / MAGAZINE REVIEWS (n)` → magazine, date, rating | ~19/60 game pages carry one; ≈9,000 citations, ≈6,500 scored across ~16 magazines | **no page numbers** — good for score values, useless for locators |
| **abandonware-france issue indexes** | title, note, printed page | 2,832 ST-tagged rows (FR) | best page-located score ground truth for France ⚠ channel-only |
| **IA `_djvu.txt`** | full text, no layout | 54/55 sampled items | a *fallback* and a *quality baseline*: the Tilt #22 text (`450,045` chars, ✓ lead contains `Intérêt` and `/20`) already shows whether a score box survives OCR |

The measurement that matters: with ~770 page-located rows, the pipeline can report **"of N reviews we know
exist, we located M and read the right score on K"** — a real error rate, not a demo.

## 6. What this means for the schema (#52 / #60)

- A **review is not a field on a title**: it is an article with a page span in an issue, carrying a score with
  its own scale and direction, and (for some magazines) a per-machine split. §2 is the constraint.
- **Language and region are different axes** here too: Tilt scored FR and ST/Amiga versions separately in one
  entry, so "the review of X" may be two scores in one row.
- The **artefact store (#60)** inherits per-page requirements from §3–§4: DPI, provenance class, page image
  identity, and the crop rectangle — because the same article may exist as a JPEG page on one host and a JP2
  derived page on another.
- **Hints and cheats are the same shape as reviews** (page span + kind), which is why the article-with-kind
  model is the working proposal.

## 7. Re-verification log (what I reproduced, and what is owed)

| Claim | My probe | Verdict |
|---|---|---|
| IA serves one page per URL | `/download/atari-st-format-issue-049/page/n3_w1200.jpg` | **✓ 200, 352,887 B, `image/jpeg`** |
| Atarimania hosts magazine PDFs | `/magazines` → **200** (note: `/magazines.html` → **410**, so the path has no extension) | **✓ entry point**; the 87-title count and 46 MB/issue are ⚠ channel-only |
| A Tilt scan's text shows the score box | `Tilt022.txt` (IA djvu) → 450,045 chars, contains `Intérêt` and `/20` | **✓** (also confirms the FR channel's reading of the Tilt box) |
| stcarchiv is article-level transcription | `/st-computer/1990/1` → **200 but empty body**, even with `Accept-Encoding: identity`; year indexes 1986–1995 all 200 | **⚠ NOT reproduced** — the biggest prize in the batch and the first thing to re-check, because if true it removes the German OCR entirely |
| abandonware-france scored review index | `/bibliotheque/magazines/` → **200**; the issue-index URL I guessed → 410 | **✓ entry point**, ⚠ the 2,832-row count is channel-only |
| French IA items carry `_djvu.txt` | 24/24 by the channel; my own check on 2 items | **✓** |
| 257,000 pages / DPI distribution | channel-derived per item (49 items listed with identifiers) | ⚠ channel-only; the method is sound but no figure here is mine |

**Re-check owed, in priority order**: (1) `stcarchiv.de` article pages — try a browser session, a different
path shape, or its sitemap; (2) abandonware-france's per-magazine issue index and its 2,832 rows; (3)
Atarimania's 87-title magazine list and one PDF's real DPI; (4) ST Action's 133 dpi, which decides whether that
title is worth OCR at all.

## 8. Where the detail lives

The five unedited channel reports, with their per-issue tables and their own limitation sections:

- `research/magazines/uk-us.md` — UK/USA: 20+ titles, circulation figures, Atarimania's magazine holdings.
- `research/magazines/france.md` — 11 French titles, score-format evolution read off scans and indexes.
- `research/magazines/germany.md` — German titles, `stcarchiv` transcription, kultpower.
- `research/magazines/rest-of-europe.md` — ES/IT/PL/GR/NL, the Chilean correction, the top-5 shortlist.
- `research/magazines/ocr-readiness.md` — the page-image census, DPI distribution, per-item identifiers,
  ground-truth tables.
