# French Atari ST magazine landscape, 1985–1996 — and where the scans live

STATUS: complete v1 — written 2026-11-15 (~75 min of probing). Evidence labels as in
`research/st-software-sources.md`: **counted** = I fetched and parsed it myself (method stated);
**stated** = the source's own sentence, quoted; **unverified** = hearsay I could not confirm.

**Headline.** France is the only country outside the UK where the ST had a dedicated monthly for its
whole commercial life — **ST Magazine**, Sept 1985 → Sept 1998, 127 issues (and, decisively for a
review-score catalogue, it **published no numeric scores**). The three big multi-format monthlies —
**Tilt, Génération 4, Joystick** — index **2,832 ST-specific review entries with a score and a page
number** in abandonware-france's structured issue indexes, which is the single most valuable artefact
found in this whole chapter. On the scan side, **abandonware-magazines.org serves one JPEG per page at
a per-page URL with no text layer**, while **archive.org's French items already carry `_djvu.txt`
(24/24 sampled)** and **amigaland.com serves per-issue OCR text as HTML beside its PDFs**.

## 1. Magazines

Issues-published figures are from the magazine's own run as recorded by abandonware-magazines /
abandonware-france; digitised counts are **counted** from the two archives (method: parse issue-listing
pages for `Numéro NNN` blocks, and parse abandonware-magazines' bulk-download page for one `.zip` per
issue — see §2.1).

| Name | Publisher | Years | ST coverage | Score format + how established | Issues published | Digitised (counted) | Best source |
|---|---|---|---|---|---|---|---|
| **Tilt** | Éditions Mondiales | 1982-09 → 1993-12 | **shared**, multi-format; ST covered from late 1985, ST-heavy 1986–1992; ran the *Tilt d'Or* awards | **`counted`**: star box + Intérêt score. Index records a single number per game: **1–6 through #38 (Jan 1987)**, **x/20 from #39 (Feb 1987)**, **% from #113 (Apr 1993)**. Verdict box read on a scan (§1.1) | **122** numbered + ~17 HS | **122/122** numbered; 139 zips total | abandonware-magazines (pages) + abandonware-france (score index) |
| **Génération 4 / Gen4** | Pressimage | 1987-Q4 → 2002+ | **shared**; strong ST section 1987–1993, then PC-led | **`counted`** from index: **% from #1 (1987) to #82 (Nov 1995)**, `/6` from #83, `/20` around #139, mixed after | **161** numbered (abandonware-france) | 180 numbered + HS = **301 rows**; **201 zips** | abandonware-magazines + amigaland (PDF+TXT) + IA `generation4-magazine` (193) |
| **Joystick** | Hachette / later | 1990-01 → 2012 | **shared**; ST tested to ~1994, then PC/console | **`counted`** from index: **%** throughout (ranges like `70%-80%` allowed); `/10` only from #140 (Sep 2002) | **260** + Hebdo + HS | 260/260 numbered, **462 rows**, **299 zips** (+49 Joystick Hebdo) | abandonware-magazines; IA `joystick-magazine-french` (328) |
| **ST Magazine** | Pressimage (Godefroy Giudicelli) | 1985-09 → 1998-09 (#127); then NG #129–148 (2003–2010) | **dedicated** (ST/STE/TT/Falcon; "L'univers Atari") | **`counted`: no numeric score at all.** #8 (Mar 1987) verdict is prose only; the OCR text of #57 (Jan 1992) and #97 (Aug 1995) contains no `/20`, `/10` or `%` score strings; abandonware-france's index records **0 notes for every STM issue** | **127** + HS; NG 129–148 | **132/146** numbered zips; 149–151 IA items | abandonware-magazines; IA `computermagazinesfrench` (with cover floppies!) |
| **SVM / Science & Vie Micro** | Excelsior | 1983-12 → 2005 | **shared**, machine-by-machine columns | `unverified` (not read) | **240** | **300 zips** (incl. HS/specials) | abandonware-magazines |
| **Micro News** | — | 1986-10 → 1992-01 | **shared** (MSX-born, then all 16-bit incl. ST) | `unverified` | **54** (site lists from #6) | **50 zips**, 47 PDFs at amigaland | abandonware-magazines / amigaland |
| **Atari 1ST** | — | 1987-07 → 1988-12 | **dedicated** (Atari 8-bit + ST) | `stated`: five criteria — *graphisme, son, intérêt, jouabilité, durée* (academic study, §1.2) | 17 + HS | **18/18** | abandonware-magazines |
| **STart Micro** | — | 1992-11 → 1994 | **dedicated** | `unverified` | 32 + 4 HS | **36/36** | abandonware-magazines |
| **ST Echos** | — | 1994 → 1995 | **dedicated** | `unverified` | 8 + HS | **9/9** | abandonware-magazines |
| **Atari magazine** | — | 1989-05 → | **dedicated** | `unverified` | 37 + "PV" + HS | **53 rows** | abandonware-magazines |

Cross-checks that matter for a catalogue:

- **Tilt's dates contradict the usual assumption that it starts in 1985.** It is a *games* magazine from
  Sept/Oct 1982 (issue 001, "Septembre/Octobre 1982", 33 MB zip — counted). ST coverage begins part-way
  into that run, and I located the onset exactly by crawling Tilt issues #22–42 in abandonware-france's
  index: the **first ST-tagged item is #24 (Septembre 1985)** — "Gem Draw [ST]", a utility; the first
  *game* reviews appear at **#32 (Juin 1986)** (6 entries, e.g. *Nine Princes in Amber [ST]*); volume
  jumps to 15 entries at **#39 (Février 1987)** and stays 6–30 per issue thereafter. So Tilt's usable ST
  review corpus effectively starts in mid-1986, not 1985.
- **ST Magazine starts Sept 1985**, four months after the 520ST launch — the earliest dedicated ST
  monthly anywhere (abandonware-magazines: "premiere parution le 05/85"; issue 001 dated Septembre 1985;
  abandonware-france header "127 numéros recensés 1985 1998").
- **Three separate French Atari-dedicated titles existed besides ST Magazine** (Atari 1ST, STart Micro,
  ST Echos) plus *Atari magazine* — all short-run, all fully digitised at abandonware-magazines.

### 1.1 Tilt's verdict box — read off a scan (counted)

I opened Tilt #51 (Feb 1988), spread pp. 44–45, the *Defender of the Crown* (Atari ST) test. The rating
block is a fixed five-line table:

```
Type ............ action-aventure
Intérêt ......... 18
Graphisme ....... ★ (1–6 stars)
Animation ....... ★ (1–6 stars)
Bruitage ........ ★ (1–6 stars)
Prix ............ C
```

So **Tilt's verdict is not one number**: *Intérêt* is a numeric mark (out of 20 in 1988; the same box
carried /6 before Feb 1987), and three production criteria are **star rows**. There is **no overall
"note globale"** — the star scale replaced per-criterion /6 notes at #5 (May–Jun 1983) with the legend
"★★★★★★ : génial / ★★★★★ : excellent / ★★★★ : bon / ★★★ : acceptable / ★★ : intérêt limité /
★ : faible" (`stated`, quoted from the academic study below, which cites Tilt #5 p. …). The abandonware-france
index preserves **the Intérêt number only**, which is why its Tilt notes run `/6` → `/20` → `%`.

### 1.2 The academic description of French scoring (secondary, `stated`)

*Lire les magazines de jeux vidéo* (Presses universitaires de Lyon, openedition) states:
"*Tilt* substitue dès le numéro 5 (mai–juin 1983) ses notes sur 6 points par un système d'étoiles" and
"*Tilt* (n° 121, décembre 1993) permet à deux autres testeurs de donner leur verdict" — plus the
five-criteria list for *Atari 1st* ("graphisme, son, intérêt, jouabilité, durée"). Same source:
Tilt's criteria are *intérêt, graphisme, bruitage + animation*, and bruitage replaced *fiabilité* at #5.
This is the only scholarly treatment of French game scoring I found, and it independently confirms the
scan reading above.

### 1.3 ST vs Amiga: separate verdicts or one?

- **Tilt — per-machine, in one entry (counted).** The index carries pipe-separated score cells for
  multi-format titles, e.g. Tilt #51: `Bob Morane – Science fiction [ST|CPC|TO8]` → `14/20|15/20|15/20`;
  `Iznogoud [ST|CPC|TO8]` → `15/20|15/20|13/20`; `Test Drive [Amiga|ST|C64]` → `18/20|17/20|17/20`.
  Single-format titles get one number. So Tilt reviewed a title once and scored each machine separately
  — the ST score is recoverable **only if you keep the platform order** of the bracket.
- **Génération 4 — same pattern (counted)**: `75%|78%|75%` on a 3-platform entry (issue #19).
- **Joystick — one score for all versions (counted from the index: 429/429 ST-tagged entries are single
  `%` cells)**; the review prose names the machines. Treat Joystick's ST score as "the game's score",
  not "the ST version's score".
- **ST Magazine — ST-only by definition**; no Amiga comparison exists.
- **Tilt d'Or** (1983/84–1993) is **per category, not per machine** (`stated`, fr.wikipedia "Tilt d'or":
  "récompenses décernées à chaque fin d'année par le magazine Tilt de 1983 à 1993 … par catégorie";
  co-awarded with Canal+ from 1987 per Le Monde). Do **not** encode it as a platform verdict; encode it
  as an award on the title, with the category as a field.

### 1.4 Why ST Magazine cannot supply verdicts

Three independent probes agree: the scan of #8 shows prose-only conclusions; grepping the IA text layers
of #57 and #97 for `%`, `/20`, `/10` returns only OCR noise from ads; and abandonware-france, which does
record notes for Tilt/Gen4/Joystick/SVM, records **zero** notes for all 127 STM issues. ST Magazine is
therefore a **presence + page-citation** source (it tells you a title was reviewed, on which page, in
which issue), not a score source. That is a real limitation for the corpus: the one magazine that
covered the ST exclusively never scored anything.

## 2. Scan sources

| # | Source | Holdings (French, ST-relevant) | Container | **TEXT LAYER?** | Per-issue / per-page pattern | Status / robots | NEW? |
|---|---|---|---|---|---|---|---|
| 1 | **abandonware-magazines.org** | Tilt 139, ST Magazine 132, Génération 4 201, Joystick 299 (+Hebdo 49), SVM 300, Micro News 50, Atari 1ST 18, STart Micro 36, ST Echos 9, Atari magazine 53 — **15,085 issue zips across 539 titles** (counted) | **ZIP of one JPEG per page** (e.g. `Tilt 001 (complet)/TILT 001 (…) - Page 001.jpg`) — *and* loose per-page JPEGs on the download host | **NO** (page images only). Exception: hand-made OCR of *selected articles* for **Tilt** and **Joystick** only (`articles_ocr/<mag>/articles.php`, HTTP 200) | index `affiche_mag.php?mag=<id>&page=<n>`; viewer `affiche_mag.php?mag=<id>&num=<fileid>&album=oui`; file `download.php?num=<fileid>` → 302/HTML stub → **`https://download.abandonware.org/magazines/<Dir>/<prefix>_numero<NNN>.zip`**; page image `…/magazines/<Dir>/<slug>/<Name> - Page NNN.jpg` (thumb under `…/min/`) | 200; `robots.txt` disallows only `/document.docx`, `/document.pdf`; no rate limit observed; `/articles_ocr/` dir listing **403**; `download.abandonware.org/robots.txt` → **302 to error page (no policy)** | NEW |
| 2 | **archive.org** | `tiltmagazine` **141**, `generation4-magazine` **193**, `joystick-magazine-french` **328**, `st-magazine*` **~149–151**, all inside `computermagazinesfrench` **2,293** (all counted via `advancedsearch.php`) | PDF (+ DjVu, JP2 zip, EPUB) | **YES — 24/24 sampled items carry `_djvu.txt`**, many also `_hocr.html` / `_chocr.html.gz` + `_text.pdf`. **ST Magazine items often also carry the cover floppy** (`… disquette.ST` / `.MSA`, 4/6 sampled) | `archive.org/details/<identifier>`; `title:("Génération 4") AND mediatype:texts` → **229**; `title:("ST Magazine") AND mediatype:texts` → **162**; `title:(Tilt) AND mediatype:texts` filtered by `collection:tiltmagazine` → 141 | 200; IA ToS; no licence field | **NEW** (survey had only a passing mention) |
| 3 | **abandonware-france.org** `/bibliotheque/magazines/` | **No scans.** Structured *content index*: Tilt **6,869** items / **1,371 ST-tagged**; Génération 4 **4,976 / 1,032 ST**; Joystick **5,419 / 429 ST**; plus ST Magazine 127 issues (no articles), SVM 199 | HTML only (index + per-game links) | n/a — it is already text | series `…/magazines/<series>/`; issue `…/magazines/<series>/numero-<N>-<id>/`; per item `<span class='mag_art_type'>Test <span class='mag_art_note'>(18/20)</span></span><span class='mag_art_titre'>Title [ST\|CPC]</span> … page NNN` | 200; no robots restriction seen; **zero download links on issue pages** | **NEW** (survey did not know it indexed magazines) |
| 4 | **amigaland.com** (`/dataz/press_magazine/`) | **18 titles**, French among them: `tilt` (**134** PDF_TXT + 134 HQ_PDF + 134 covers), `Generation_4` (**157** Originals_PDF + 157 PDF_TXT), `Micro_News` (47 PDF_TXT). No ST Magazine, no Joystick | PDF (HQ and text variants) **+ per-issue HTML text** | **YES** — `PDF_TXT/` variants **and** `HTML_TXT/<Issue>.htm` per issue (Tilt 061 htm = 719 KB of OCR text). Quality is poor: raw single-blob OCR, entities-escaped, score boxes lost | open Apache autoindex: `/dataz/press_magazine/<Title>/{HQ_PDF,PDF_TXT,HTML_TXT,Originals_PDF,PDF_resized_A4,Covers}/`; file `<Title>_<NNN>_(<date>).pdf` / `.htm` | 200 everywhere; **directory listing open, no robots.txt served, no login** | **NEW** |
| 5 | **atarilegend.com/magazines** | 8 titles, **ST Magazine 22 issues** + **Génération 4 1 issue** (French); rest UK | HTML index of reviews (title / type / page) | n/a (index text) | `/magazines` → `/magazines/<id>` → per-issue review tables | 200 | **NEW** |
| 6 | **lankhor.net** | Cropped **press-review scans per game** (Lankhor catalogue), labelled "Test - Tilt 51 (février 1988) - (512 Ko)", "Test - ST Magazine 11 (aout 1987) - (660 Ko)", "Test - Joystick Hebdo 10" | JPEG/PDF crops, 100 KB–1.5 MB | NO | `jeux.php?jeu=<id>&menu=documents` → `…&ident=<n>` | 200 | **NEW** |
| 7 | **retroarchives.fr** | **No magazine scans** (modern retrogaming blog with `/atari-st/` game pages) | — | — | — | 200 | NEW (checked, negative) |
| 8 | **obligement.free.fr** | **Amiga only.** Its "magazines" downloads are its own diskmag `.lha` on aminet; its `liens.php#mags` is a *link directory* to magazine archives | LHA/PDF | n/a | `obligement.free.fr/telechargements.php#magazines` | 200, latin-1 | NEW (checked, negative for ST) |
| 9 | **gamatomic.com** | Modern review site (no retro scans) | — | — | — | 200 | NEW (negative) |
| 10 | **stmagazine.free.fr / stmagazine.org** | Official ST Magazine site; free PDF of **#142** (30 MB) | PDF | `unverified` | — | 200 via search snippet | NEW |
| 11 | **cotegamers.com** | ST Magazine **NG #129–148** + the 7 **AtTOS** issues, as PDFs (some double-page impositions) | PDF | No | shop content page, per-file | 200 (`stated` by page text) | NEW |
| 12 | **atarimania.com/magazines** | English only (ST Format, ST User, ST Action, Page 6) — **no French titles** | PDF/JPG | No | `/magazines/<titleid>/<issueid>` | 200 | checked, negative for France |

### 2.1 abandonware-magazines.org — the mechanics that decide the OCR plan

- `downloadrapide.php` (HTTP **200, 16.7 MB**) is a bulk index listing **15,085 distinct issue zips**
  in **539** title directories: `https://download.abandonware.org/magazines/<Dir>/<prefix>_numero<NNN>.zip`,
  Hors-Série forms as `<prefix>_numerohs<NN>.zip` (e.g. `stmagazine_numerohs…`, `joystick_numerohsb06`,
  `tilt_numerohsjeux`). Directory names contain spaces and capitals (`ST Magazine`, `Generation 4`,
  `Science et Vie Micro`, `Atari 1ST`) — **do not slugify**, copy them verbatim.
- One zip = one issue = a folder of page JPEGs plus a `readme.txt` (verified by range-reading the first
  4 KB of `tilt_numero001.zip`: entry names `Tilt 001 (complet)/readme.txt`, `… - Page 001.jpg`).
- **The album viewer gives a per-page URL** — `affiche_mag.php?mag=28&num=23&album=oui` embeds one `<img>`
  per page pointing at `https://download.abandonware.org/magazines/Tilt/tilt_numero051/<Name>.jpg`
  (verified: page 050 full size **505,412 B** `image/jpeg`, thumb under `/min/` **11,937 B**). That is a
  page-level OCR job's addressing scheme: no PDF splitting needed.
- **But the page filenames are not regular**: Tilt 001 uses `TILT 001 (Septembre - Octobre 1982) - Page 050.jpg`,
  Tilt 051 uses `TILT - n051 - fevrier 1988 - page044 et 045.jpg` (double-page spreads!), and some issues
  mix `… - Page 058 (1982-09-10).jpg`. So page URLs must be **scraped from the album page per issue**,
  not constructed.
- Existing OCR is **editorial, not systematic**: `articles_ocr/tilt/articles.php` and
  `articles_ocr/joystick/articles.php` (both **200**) serve hand-corrected full-text transcriptions of a
  *curated short list* — 23 Tilt article slugs and 4 Joystick slugs, e.g. `?article=colonel`, `=monkey1/2/3`,
  `=indy3`, `=kq6`, `=kyrandia2`, `=dott`, `=guest`, `=ultima1/3/7/8`, `=xwing`, `=wingc1/c2` — i.e.
  **1990s adventure-game reviews and walkthroughs, almost none of them ST-specific**. No OCR directory
  exists for ST Magazine, Génération 4 or SVM (all **302 → error page**).

## 3. The three special answers

**(a) One page per URL, or one PDF per issue?** **Both, and that is the good news.**
abandonware-magazines.org gives **one JPEG per page at its own URL** through the album viewer
(`…/<slug>/<Name>.jpg`), *and* packages the same pages as one ZIP per issue. There is **no PDF** in that
pipeline at all. So a page-level GPU OCR job is natural there: scrape the per-issue page list, fetch page
JPEGs, OCR page-by-page, key results by (title, issue, printed page).
At **archive.org** it is the opposite: one PDF per item, but the *pages are already separated* inside —
`_djvu.xml` is per-page OCR with coordinates, `_hocr.html` per-page HTML, `_jp2.zip` per-page images.
At **amigaland.com** it is PDF per issue plus a per-issue HTML text blobs.

**(b) Do the French IA items already carry `_djvu.txt`?** **Yes — 24 of 24 sampled, i.e. 100 %.**
I sampled 8 random identifiers from each of `tiltmagazine`, `generation4-magazine`,
`joystick-magazine-french`, `computermagazinesfrench` and checked `metadata/<id>` file lists: every one had
a `*_djvu.txt`; several also had `_hocr.html` / `_chocr.html.gz` / `_text.pdf`. Concretely,
`generation4-magazine-149` carries `generation4_numero_149_djvu.txt` (354,353 B) plus `_djvu.xml` (3.3 MB)
and `_abbyy.gz`; `st-magazine-097` carries `ST magazine 097_djvu.txt` (366,485 B) + `_hocr.html` (9.8 MB);
`joystick029` carries `Joystick 029_djvu.txt` (865,336 B). **A re-OCR round on IA material is optional,
not required** — the text exists and is page-addressable via `_djvu.xml`.

**(c) Is there any existing OCR or transcription of French ST reviews?** **Yes, three kinds, all partial.**
1. **abandonware-magazines' "articles OCRifiés"** — hand-corrected full-text transcriptions of a *curated
   short list*: 23 Tilt articles + 4 Joystick articles, all **1990s adventure games** (*Monkey Island*,
   *Indiana Jones 3*, *Day of the Tentacle*, *Kyrandia 2*, *King's Quest 6*, *Ultima* 1/3/7/8, *X-Wing*,
   *Flashback*, *Fury of the Furries*, *Gobliiins*, *Prehistorik*, *Sram 2*, *Croisière pour un cadavre*,
   *The 7th Guest*), plus solutions. Verified sample: a full Tilt review of *Colonel's Bequest* with the
   by-line "Olivier Hautefeuille". **Not an ST corpus and not a systematic index** — it is a
   reader-service section about adventure games.
2. **amigaland.com `HTML_TXT/`** — complete per-issue OCR text for **Tilt (134), Génération 4 (157),
   Micro News (47)**. Machine OCR, poor: the Tilt #61 blob begins "M   M. m m i moi HBHHI  WlOlj MICRO
   LOISIRS …"; score boxes and star rows do **not** survive.
3. **abandonware-france's structured index** — not OCR but better: **2,832 ST-tagged review entries with
   score + page number** for Tilt/Gen4/Joystick (§2 row 3). This is the closest thing to a ready-made
   French ST review database, and it is what an OCR round should be *validated against*.
   Plus **lankhor.net**'s per-game review crops, which are scans-with-citation, not text.

## 4. Best French OCR starting point (one paragraph)

Start with **archive.org, not with a fresh scan pipeline**. The French items there are PDFs already OCR'd
into `_djvu.txt` + `_djvu.xml` (per-page, with coordinates) + `_hocr.html`, spread across four
collections with clean identifier patterns (`tiltmagazine` 141, `generation4-magazine` 193,
`joystick-magazine-french` 328, and `st-magazine*` ~150 inside `computermagazinesfrench`) and they
frequently ship the **cover floppy** inside the same item — so one fetch yields issue, text, page
geometry and the disk. Use that text to build the review corpus for free, then spend GPU OCR only where
it demonstrably fails: the **score boxes**, which no existing text layer preserves (Tilt's `Intérêt` +
three star rows; Génération 4's `%`). For those, go to **abandonware-magazines.org**, which is the only
host exposing **one JPEG per page at a stable per-page URL** (scraped per issue from
`affiche_mag.php?mag=&num=&album=oui`), and OCR just the ~1,371 Tilt + 1,032 Gen4 + 429 Joystick ST
review pages that **abandonware-france's index already tells you the printed page number of** — a
bounded, page-addressable job of a few thousand images rather than 600,000. Treat **ST Magazine** as
out of scope for scores (it printed none) but in scope for **presence + page citations**, and use
**amigaland.com**'s `HTML_TXT` only as a second opinion, never as the primary text.

## Limitations

- **No magazine file was downloaded.** Everything above rests on index pages, file listings, HTTP HEADs,
  small range reads (first 4 KB of one zip to see its entry names), plus **three page reads**: two
  full-size page JPEGs opened for the Tilt/STM score-box question, and one 366 KB `_djvu.txt` per
  magazine for the score-string greps. No OCR text or scan was written anywhere but the scratch dir, and
  nothing copyrighted was stored beyond transient probe files.
- **SVM and Micro News score formats are `unverified`** — I did not open an issue of either.
- **ST Magazine's "no score"** rests on three issues (1987, 1992, 1995) + a zero-note index; a 1989–91
  issue could in principle have carried a score box. Worth one page check before it is treated as law.
- **Joystick counts are inflated by re-editions**: 462 rows for 260 numbers (collector re-issues and HS).
  Génération 4 likewise (301 rows / 180 numbers).
- **Not probed**: French torrent/mirror distribution; `magazines.abandonware.org`-style siblings;
  `abandonware-magazines`' "mags interdits" list (a page exists — `Mags interdits` in the nav — and was
  not read, so some titles may be deliberately withheld); `stmagazine.org` and `cotegamers.com` were
  characterised from search snippets (`stated`) rather than opened.
- abandonware-magazines' `download.php` returns `text/html` (a stub page holding the real
  `download.abandonware.org` URL), so a naive downloader that trusts `Content-Type` will save HTML.
