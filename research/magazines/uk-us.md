# UK and US Atari ST magazines, 1985–1996 — and where the page scans live

STATUS: **complete (v1)** — written 2026-11-15, ~75 min of probing. All counts are mine unless a
`stated` label quotes someone.

Conventions (from `research/st-software-sources.md`):
- **stated** — the magazine or a named source says so; quoted.
- **counted** — I fetched the source and counted/parsed it myself; method stated.
- **unverified** — hearsay, forum claim, or a figure I could not confirm.

Rules held: metadata, index pages and file listings only. No magazine PDF, page image or OCR text
was downloaded or written to disk. Where I needed to test a PDF for a text layer I piped a byte
range through `strings` and counted `/Font` tokens without saving anything (method noted below).

---

## 0. The headline, before the tables

**The OCR round is not the bottleneck people assume.** The Internet Archive already ships a full
OCR derivative set — `_djvu.txt`, `_djvu.xml`, `_hocr.html`, `_hocr_pageindex.json.gz`,
`_jp2.zip` and a PDF — with **every** magazine item I sampled (six items, six different titles:
ST Format, ST Action, ST Review, ST-Log, ST Applications, ACE). What the IA lacks is *coverage*:
11 ST Format issues, not 86. The two hosts that have the full runs (Atarimania `/magazines`,
stformat.com) have **no text layer at all** — I proved it for Atarimania by byte-range probing two
PDFs (`/Font` and `/BaseFont` token count = **0** in both the first 200 KB and the last 400 KB of
a 46.5 MB and a 48.8 MB file).

So the shape of the problem is: **text exists where coverage is thin, coverage exists where text is
absent.** The interesting question is not "can we OCR" but "which host do we OCR, and do we need to".

---

## 1. Magazine table

Sort: UK ST-dedicated first, then UK multi-format with substantial ST coverage, then US.

| Magazine | Country | Years | ST coverage | Review score format | Issues published | Issues digitised (counted) | Best scan source |
|---|---|---|---|---|---|---|---|
| **ST Format** | UK | Aug 1989 – Sep 1996 | Dedicated (ST/STE only) | **percentage**, some reviews add reviewer byline (`stated`: Atarimania review rows read `ST Format · April, 1990 · by Monteiro, Richard · Rating: 77%`) | **86** (`stated`: Wikipedia infobox, "Final issue number 86") | **89** at Atarimania; **87** at stformat.com (`stf00`–`stf86`, counted from index links); **11** on IA with text; 86 records at Retromags | **Atarimania** (full run, PDF) + **stformat.com** (full run, JPEG pages) |
| **ST Action** | UK | May 1988 – Dec 1993 | Dedicated | **percentage** (`counted`: Atarimania rows `ST Action · September, 1990 · Rating: 68%`, `· September, 1994 · Rating: 60%`) | **68** (`stated`: Atarimania "68 ISSUES"; issue 68 = Dec 1993 per Atari Legend) | **68** at Atarimania; **33** on IA with text (issues 1–68, sparse); 67 at Retromags | **Atarimania** (complete) |
| **Atari ST User** | UK | Mar 1986 – Nov 1994 | Dedicated | **percentage** (`counted`: Atarimania `Atari ST User · October, 1990 · Rating: 75%`) | **≈106** (`counted`: IA holds `Atari_ST_User_Issue_105_1994-10`, so numbering ran past 105; Wikipedia `stated` final issue Nov 1994) | **102** at Atarimania; **22** unique on IA with text (of 26 items); 23 in `ataristuser` collection | **Atarimania** (102/≈106) |
| **Atari ST Review** (cover title *ST Review*) | UK | May 1992 – Jan 1995 | Dedicated | **percentage, split into two marks** (`stated`: the magazine's own review box, quoted in the OCR text of Atarimania's issue-07 PDF — "TECHNICAL SCORE: 58% … SILLY SCORE: 74%") | **35** (consistent across three hosts: Atarimania 35, Retromags 35, IA issues 001–035) | **35 / 35 — complete, and every one on IA with a text layer** | **Internet Archive** (best of all: complete *and* OCR'd) |
| **ST/Amiga Format** | UK | Jun 1988 – 1989 | Dual-platform, ST + Amiga, reviewed side by side | **percentage** (`unverified` — not sampled; the magazine's per-format box is the known house style) | **15?** (`unverified`; Atarimania lists 13, dates 1988–1989) | **13** at Atarimania (PDF only); 13 on stformat.com (`staf01`–`staf13`, JPEG); 13 on IA incl. cover disks | **stformat.com** or **Atarimania** |
| **ST Applications** | UK | 1991 – 1993 | Dedicated ("The Magazine for Users of Atari ST, STE and TT Computers", cover price £1.50 — read off the Atarimania PDF search snippet) | **percentage** (`unverified` — index rows not sampled) | **30** (`stated`: Atarimania "30 ISSUES"); ceased with the September 1993 issue (`stated`: STart editorial, "ST Applications, published by Bill Perry, has ceased publication with the September issue") | **30** at Atarimania; **26** on IA with text | **Atarimania** (complete) |
| **Zero** | UK | Nov 1989 – Oct 1992 | Multi-format (Amiga/ST/console), heavy ST games coverage | **percentage** (`counted`: Atarimania row `Zero · May, 1990 · Rating: 55%`; also `Test (89%) F-1` in a Zero #1 contents index) | **37** (`stated`: Sega Retro and Magazines-from-the-Past both give "36 (37)" — issue 00 plus 1–36) | **37 / 37 — complete on IA with text** (identifiers `ZeroIssue00Oct89` … `ZeroIssue36Oct92`, counted) | **Internet Archive** (`zero-magazine`, 74 items) |
| **ACE** (Advanced Computer Entertainment) | UK | Oct 1987 – Apr 1992 | Multi-format, ST covered throughout | **score out of 1000** — the only UK mag in this set that does this (`counted`: Atarimania row `ACE · September, 1990 · Rating: 625/1000`) | **55** (`stated`: Wikipedia infobox, "Final issue number 55"; corroborated by out-of-print archive, "April 1992 was the last issue (55)") | **55 / 55 — complete on IA with text** (`ACE_Issue_01` … `ACE_Issue_55`, counted) | **Internet Archive** (`ace-magazine`, 137 items incl. duplicates/supplements) |
| **The One** (for 16-bit Games / The One Amiga) | UK | Oct 1988 – Jul 1996 | Multi-format; started as ST+Amiga+PC, drifted to Amiga-led | **percentage overall + four sub-scores** (graphics, sound, value, playability) — `stated` via Frame Advance quoting the review: "gave it 76% overall, with 76% for graphics, 51% for sound, 60% for value and 80% for playability" | **88** (`stated`: Sega Retro "88 (89)"; bambi-amiga gives Oct 1988 – Jul 1996) | **66** unique numbered issues on IA (`the-one-magazine-uk` + `magazine_rack`); 10 with indexed contents at Atari Legend | **Internet Archive** (66/88), then Atari Legend for the review index |
| **Computer + Video Games (C+VG)** | UK | Nov 1981 – Oct 2004 | Multi-format; ST reviews from 1985 to ~1993 | **percentage** (`counted`: Atarimania row `Computer + Video Games (C+VG) · September, 1990 · Rating: 96%`) | **~300 over 23 years** (not counted; nothing sane to digitise end-to-end) | **127** unique of issues 1–132 on IA (`cvg-magazine`, 540 items) — i.e. a gap-free 1981–1992 run, which is exactly the ST window | **Internet Archive** (`cvg-magazine`) |
| **The Games Machine** | UK | Oct 1987 – 1990 | Multi-format; ST reviewed throughout | **percentage** (`counted`: Atarimania rows `The Games Machine · December, 1987 · Rating: 65%`, `· August, 1990 · Rating: 50%`) | **30** (`stated`: Frame Advance cites issue 30, May 1990; no later issue found) | not counted at title level; IA query and Atarimania holdings both exist but I did not enumerate them — **`unverified`** | IA (`magazine_rack`); Atarimania has no dedicated entry in its 87-magazine list |
| **New Computer Express** | UK | 1988 – 1991 | Weekly multi-format news mag, ST prominent | **percentage** (`unverified` — not sampled) | **154** (`counted`: IA numbering runs to issue 154) | **152** unique on IA (`newcomputerexpress`, 161 items) | **Internet Archive** — near-complete |
| **ST-Log** | US | 1986 – 1989 | Dedicated (spawned from *ANALOG Computing*) | **unverified** (not sampled; no scored index found) | **36** (`stated`: Atarimania "36 ISSUES"); VGHF notes its collection is "all but two issues" | **36** at Atarimania (PDF only); **29** on IA with text | **Atarimania** (complete) |
| **STart** | US | Summer 1986 – Apr/May 1991 | Dedicated (spawned from *Antic*), cover disk every issue | **unverified** (not sampled) | **42** (`stated`: Wikipedia, "42 issues total") | **42** at Atarimania; **46** unique-numbered items on IA (of 118 items incl. duplicates) with text | **Atarimania** (complete) / **IA** (already OCR'd) |
| **Atari Explorer** | US | 1985 – 1993 | Dedicated-ish (Atari's own house organ, ST + 8-bit) | **unverified** | **36** (`stated`: Atarimania "36 ISSUES") | **36** at Atarimania (PDF only); ~30 distinct issues on IA (57 items), mixed `stx_` scans + duplicates | **Atarimania** |
| **ST World** | US | 1986 – 1991 | Dedicated | **unverified** | **unknown** (`unverified`) | **5** at Atarimania; **7–8** distinct issues on IA (vol I no 4 … vol 6 no 2 iss 55) | **IA** or **Atarimania** — both fragmentary |
| **ST Informer** | US | 1990s | Dedicated | **unverified** | **unknown** (`unverified`) | **1** at Atarimania; **0** on IA under that title | **Atarimania** (single issue) |
| **Current Notes** | US | 1982 – 1996 | 8-bit Atari user-group newsletter with incidental ST content | **none found** (newsletter, no scored review section) | **101** (`stated`: Atarimania "101 ISSUES") | **101** at Atarimania (PDF only) | **Atarimania** |
| **ST Report** | US | 1980s–90s | Dedicated **online** magazine (Cleveland FreeNet) — "The Original 16/32bit Online Magazine" (`stated`, from its own masthead) | **no scores** (news/rumour digest) | not counted | **born-digital text** — issues archived as plain text on atariarchives.org's CFN collection (e.g. `ST Report: 17-Aug-90 #633`) | **atariarchives.org/cfn** — but now **Cloudflare-gated** (Turnstile interstitial on `/cfn/`, returned instead of content) |
| **ANTIC's ST Resource** | US | — | — | — | — | **not found** as a separate title on IA or Atarimania; *Antic* itself (88 issues at Atarimania) carried ST sections | — |

### Notes on the "most popular" column, and which numbers are proxies

Circulation is the number everyone wants and almost nobody published. What actually exists:

- **ST Format — 43,007**, `stated` on stformat.com's own issue table: *"Sales 43,007 (Aug-Dec 1989)"*. This is a
  display figure transcribed from the issue itself, not an audited ABC number.
- **Atari ST User — 33,706 (Jul–Dec 1988)**, `stated` by Wikipedia's infobox (which cites ABC-era figures).
- **ACE — 48,170 (circa April 1991)**, `stated` by Wikipedia's infobox. Note ACE's circulation was
  *higher* than ST Format's but it was a multi-format magazine, so only a fraction of those readers
  were ST owners.
- **Zero, The One, ST Action, ST Review**: I found no circulation figure and did not invent one. `unverified`.

**Proxies, explicitly labelled as proxies** (they measure preservation effort, not sales):
issues digitised at the best host — ST Format 89, Atari ST User 102, ST Action 68, ACE 55, ST Review 35 —
and, where an index exists, the number of scored review rows attributable to the title (see §2,
Atari Legend: ST Format 115 scored rows, The One 92, ST Review 80, ST Action 31). A magazine being
fully scanned tells you someone cared; it does not tell you it sold well. The only popularity claims
I will stand behind are the three circulation figures above.

---

## 2. Source table

`TEXT LAYER?` is the decisive column. "Full OCR derivs" means the item carries `_djvu.txt` **and**
`_djvu.xml` (or `_abbyy.gz`) **and** `_jp2.zip` **and** a PDF; I verified this by reading
`archive.org/metadata/<id>` for six items across six titles.

| Source | What it holds | Container | TEXT LAYER? | Per-issue pattern | Status | NEW? |
|---|---|---|---|---|---|---|
| **Atarimania `/magazines`** — https://www.atarimania.com/magazines | **87 magazines**, incl. every UK/US ST title worth having. Counted holdings: ST Format **89**, ST Action **68**, Atari ST User **102**, ST Review **35**, ST/Amiga Format **13**, ST Applications **30**, ST-Log **36**, STart **42**, Atari Explorer **36**, Current Notes **101**, ACE **55**, ST World **5**, ST Informer **1**, Atari World **5**, ANALOG **79**, Antic **88**, Atari User **43**, Compute!'s Atari ST **10**, Atari Computing (UK) **16** | **PDF** (46.5 MB for one ST Action issue; 48.8 MB for one ST Review issue) + a **JPEG page reader** at `/magazines/<id>/<itemId>`; low-res cover thumbs at `/mags/low_res/<slug>_NN.jpg` | **NO.** Proved by byte-range probe: `/Font` + `/BaseFont` + `/ToUnicode` token count = **0** in the head (0–200 KB) *and* tail (last 400 KB) of two full-size PDFs. These are image-only scans. | Index: `/magazines/<id>`; issue: `/magazines/<id>/<itemId>` (e.g. `/magazines/19/679`); PDF: `https://www.atarimania.com/mags/pdf/<slug>_NN.pdf` (ST Action: `atari_st_action_01.pdf`; ST Review: `atari_st_review-issue_07.pdf`) | HTTP 200 on index and magazine pages. `robots.txt` 200: `Disallow: /login /register /search /api/`, then `Allow: /`. No ToS page found. No licence — scans of in-copyright magazines. | **NEW** — the survey never mentions Atarimania's magazine section at all, and it is the single largest full-run scan host for this corpus |
| **Atarimania game pages — review index** — https://www.atarimania.com/games/atari-st-games-… | Per-title "BOOK / MAGAZINE REVIEWS (n)" blocks: magazine · date · reviewer · **rating** | HTML (Next.js flight payload) | n/a (it is metadata) | `https://www.atarimania.com/games/<slug>` | HTTP 200; robots allows game pages (`Disallow` only login/register/search/api) | **NEW** — this is a ready-made title→magazine→date→score index |
| **stformat.com** ("The ST Format Shrine") — https://www.stformat.com/ | ST Format **87 issues** (`stf00`–`stf86`), ST/Amiga Format **13** (`staf01`–`staf13`), STampede **7** (`stampede01`–`07`). Site claims: *"EVERY page of EVERY issue, now available online"* (`stated`) | **JPEG page images**, one per page: `.../pages_nx1500/stf01_001.jpg` (the `nx1500` path segment implies ~1500 px nominal width — `unverified`, path-derived) plus 160×230 thumbnails; **no PDFs found** (0 `.pdf` hrefs in the issue page) | **NO.** Images only. There is no `.txt`/`.xml`/`hocr` alongside. | Issue index `https://www.stformat.com/`; issue `https://www.stformat.com/stf<NN>/index.html`; page image `https://www.stformat.com/stf<NN>/pages_nx1500/stf<NN>_<PPP>.jpg`; thumb `.../pages_160x230/stf<NN>_<PPP>_s.jpg` | Root 200. `robots.txt` **404** (no policy stated). The linked `page_scans/index.html` is **404** — the site's "Open PDF" tooltip points at a path that no longer resolves. Content still served. | **known-but-unassessed** in the survey (it appears only as a name in the community link list, §"The community's own link list") |
| **Atari Legend `/magazines`** — https://www.atarilegend.com/magazines | **8 magazines, 71 issues** (`stated`: "There are currently 8 magazines in the database"): ST Format 14, ST Action 10, Atari ST Review 10, Atari ST User 1, Atari World 1, The One 10, plus French ST Magazine 22 and Génération 4 1. **Each issue carries a contents table: Title \| Type \| Page, with the score inline** (`Lemmings 95% \| Review \| 68`) and links out to `archive.org/stream/<id>/` | HTML tables (no scans hosted) | n/a (metadata) but **it points at IA items that do have text** | `/magazines`; `/magazines/<id>` (1=The One, 2=ST Action, 3=ST Format, 4=Atari ST User, 9=Atari ST Review, 11=Atari World, 12=ST Magazine, 13=Génération 4); `/magazines/<id>?page=N` | HTTP 200. `robots.txt` 200: `Disallow: /index.php /admin/ /games/search /music`; sitemap published | **partly known** — the survey's table says Atari Legend holds "reviews … magazine scans", but the per-issue scored contents table and its 71-issue coverage are **NEW** |
| **Atari Legend scored-review counts (counted)** | Rows I parsed out of the 8 magazine pages (game-link rows; score = a `NN%` `<small>` in the row) | — | — | — | — | — |
| ↳ *The One* | 94 game rows, **92 scored**, 64 typed "Review" | | | | | |
| ↳ *ST Format* | 126 rows, **115 scored**, 11 typed "Review" (the rest sit in the issue's "games reviewed" summary list) | | | | | |
| ↳ *Atari ST Review* | 294 rows, **80 scored**, 215 typed "Review" | | | | | |
| ↳ *ST Action* | 71 rows, **31 scored**, 38 typed "Review" | | | | | |
| ↳ *Atari ST User* | 4 rows, 4 scored, 0 typed "Review" | | | | | |
| ↳ *ST Magazine* (FR) / *Génération 4* (FR) | 374 and 181 rows, **0 scored** — French mags, score stored elsewhere or on a different scale | | | | | |
| **retromags.com** — https://www.retromags.com/magazines/uk/… | A **magazine database** of issue records with covers and indexed contents. Counted issue records: ST Format **86** (1–86), ST Action **67** (1–67), Zero **36** (1–36), Atari ST Review **35** (1–35) | Covers as gallery JPEGs (e.g. `gallery/?module=images&…&img=8920`); downloadable files sit behind `/files/` and **require sign-in** — container is therefore `unverified` (site convention is CBR/CBZ) | **unknown / gated** — the file listing is not visible to an anonymous client | `/magazines/uk/<slug>/`; `/magazines/uk/<slug>/<slug>-issue-<N>/`; gallery `/gallery/category/212-atari-st-review/` | Category pages **200**; `/search/?q=…` returns **403** (Cloudflare). `robots.txt` 200 with Cloudflare content signals: `search=yes, ai-train=no, use=reference` | **NEW** |
| **Internet Archive — `magazine_rack`** — https://archive.org/details/magazine_rack | The master collection, **680,318** items (counted via `advancedsearch`). Sub-collections that matter here: `atarimagazines` **705**, `computermagazines` **46,900**, `videogamemagazines` **45,523**, `computernewsletters` **29,582** | PDF + full OCR derivative stack | **YES — full OCR derived set.** Verified item-by-item: `_djvu.txt`, `_djvu.xml`, `_hocr.html`, `_hocr_pageindex.json.gz`, `_hocr_searchtext.txt.gz`, `_jp2.zip`, `_chocr.html.gz`, image PDF + `_text.pdf`, `_page_numbers.json`, `_scandata.xml` | `https://archive.org/details/<id>`; text at `/download/<id>/<id>_djvu.txt`; pages at `/download/<id>/<id>_jp2.zip`; API `/metadata/<id>`, `/advancedsearch.php`, `/services/search/v1/scrape` | 200 throughout. IA ToS; **no licence per item** (`licenseurl` absent) — these are in-copyright scans | **NEW detail** — the survey's §20 knew "magazine scans are plentiful" and quoted `title:(ST Format)` → 98; the *collection* structure and the complete OCR derivative set are new |
| **IA per-title collections (counted)** | `stformatmagazine` **11**, `stactionmagazine` **39**, `ataristreview` **35**, `atariexplorer` **42**, `stlog_magazine` **30**, `ataristuser` **23**, `zero-magazine` **74**, `ace-magazine` **137**, `cvg-magazine` **540**, `theone-magazine-uk` **199**, `newcomputerexpress` **5** | as above | as above | `collection:<name>` | 200 | **NEW** |
| **frameadvance.net** — https://frameadvance.net/sources/… | A **citation database**: per-issue source pages ("Source · Secondary · magazine") that pin claims to a magazine's OCR, quoting scores and page numbers. Example: `The One 3 · 1988-12` records that The One gave the ST version of Flying Shark "76% overall, with 76% for graphics, 51% for sound, 60% for value and 80% for playability", citing `p. 94` etc., with the **sha256 of the source item** and the `_djvu.xml` URL | HTML | n/a (it *consumes* IA's text layer) | `/sources/<mag-slug>-<issue>-<YYYY-MM>/`; `/claims/<slug>/`; `/games/<slug>/` | 200; `robots.txt` 200 `Allow: /`; sitemap published | **NEW** — and it is the model to copy for evidence labelling |
| **atariarchives.org/cfn** (ST Report, Z*Magazine, Current Notes reprints) | Born-digital text of US online magazines/newsletters | HTML text (no scans needed) | **YES — natively text** (these were text publications) | `https://www.atariarchives.org/cfn/<path>.php` | **BLOCKED** — `/cfn/` now returns a **Cloudflare Turnstile** interstitial (HTTP 200, 1,108 B, `<title>AtariArchives.org verification</title>`), not content. Recorded as a limitation. | **NEW**, but currently unreadable without a browser session |
| **worldofst / worldofst.net** | — | — | — | — | **DEAD**: `worldofst.com`, `www.worldofst.net`, `www.worldofst.com` all fail DNS (`curl: (6) Could not resolve host`), HTTP 000 | NEW (dead-end, recorded) |
| **atarilegend `/magazines/<id>?page=N` review citations** | see Atari Legend rows above | — | — | — | — | — |

### Method notes (so the counts can be re-run)

- **IA counts** via `https://archive.org/services/search/v1/scrape?q=…&fields=identifier,date,title,collection&count=10000`,
  then de-duplicated in Python by extracting the issue number from identifier/title and collapsing
  duplicates (the same issue is often uploaded two or three times under different naming schemes —
  `ZeroIssue24Oct91`, `Zero_Magazine_Issue_23`, `zero-magazine-24` are three scans of overlapping runs).
  `numFound` alone overstates coverage badly: `title:("Atari ST User")` returns **223** items, but
  most are `manualsbase-id-*` manuals, and only **26** are magazine issues.
- **Atarimania counts** from the `NN<!-- --> <!-- -->ISSUES` label on each `/magazines/<id>` page,
  cross-checked against the number of distinct `mags/pdf/*.pdf` hrefs on the same page — they agree
  for every title I checked.
- **Atari Legend counts** from regexing `<tr>` rows for a game link plus a `<small>NN%</small>`.
- **I did not download a single magazine page.** The only content-adjacent things I fetched were
  index pages and two PDF byte ranges piped through `strings` to count font tokens.

---

## 3. Which UK magazine should the OCR round start with, and why

**Start with ST Action — but do ST Review first as a free calibration run.**

The reasoning falls out of the counts rather than out of reputation:

1. **ST Review needs no OCR at all, and that makes it the right shakedown.** All **35** issues
   (May 1992 – Jan 1995, complete: Atarimania 35, Retromags 35, IA 001–035) are on the Internet
   Archive **with a full OCR derivative set**. Every pipeline step downstream of OCR — page-number
   alignment, review-block detection, score extraction against the "TECHNICAL SCORE / SILLY SCORE"
   box, game-title matching — can be built and validated on it at zero OCR cost, against a known
   complete run. If the extractor cannot find `85%` in `Pacific Islands 85% | Review | 68`
   (Atari Legend's index for issue #1), no amount of OCR will save it.
2. **ST Action is the first magazine that actually needs OCR and is cheap to do completely.**
   Atarimania holds **68 of 68 issues** as image-only PDFs (~46 MB each), and the IA text layer
   covers only **33** of those 68. That is the smallest full-run OCR job with a real scored-review
   corpus: Atari Legend already indexes **31** scored rows across just 10 issues, and Atarimania's
   review index confirms ST Action scored on the same percentage scale as ST Format (`68%`, `89%`,
   `60%`), so one extractor serves both.
3. **ST Format is the prize, and should be second, not first.** It is the biggest ST-dedicated run
   (**89** issues at Atarimania, **87** at stformat.com) and the one with a defensible circulation
   claim (`stated`: 43,007, Aug–Dec 1989), and it has **the worst text coverage of any major UK ST
   title** — 11 issues on IA with text, out of 86. But it is ~2.6× ST Action's page volume and it
   has two rival scan sources of different quality (46 MB image PDFs vs 1500 px JPEGs), so the
   source-choice question should be answered on ST Action first. The one thing to settle before
   committing: **89 vs 86** — Atarimania's extra issues are probably specials/supplements, and that
   needs a page-by-page reconciliation rather than an assumption.

What this ordering buys: a validated extractor on 35 free issues (ST Review), then a complete
scored-review corpus on 68 issues (ST Action), then the flagship run (ST Format) with the sourcing
question already answered — instead of starting on 89 issues of the biggest magazine and discovering
mid-way that the extractor does not generalise.

---

## 4. Gaps, and what I did not do

1. **No OCR quality assessment.** I established *that* a text layer exists on IA for every sampled
   item; I did not read the `_djvu.txt` to judge it. Old-pipeline items (`_abbyy.gz`, no
   `_hocr_pageindex.json.gz`) — ACE, STart — are likely worse than the newer hOCR ones
   (ST Action, ST Review, ST-Log, ST Applications). That split is a real quality axis and is
   testable without downloading a PDF: the `_djvu.txt` is a separate small file.
2. **ST Format 89 vs 86 unresolved** (see §3.3).
3. **Atari ST User's true final issue number** — IA holds `…Issue_105_1994-10`, Atarimania digitises
   102. One of those is a numbering artefact.
4. **No per-title "how many ST reviews did this magazine print" census.** I counted scored *rows*
   in the Atari Legend index (a sample of 10–14 issues per title), not a full-run census. Atarimania's
   per-game review blocks could give the true figure but would need ~10,000 game-page fetches.
5. **US magazines are thin.** ST-Log (36), STart (42), Atari Explorer (36) are all complete at
   Atarimania, but I found **no scored-review index** for any of them — the US titles' review
   conventions (letter grades? stars? prose verdict?) are `unverified` here and need a direct read.
   ST Informer exists as **one** issue; ANTIC's *ST Resource* I could not find as a standalone title
   at all; Current Notes is a user-group newsletter with no scored review section.
6. **Retromags' container format is unverified** because the download listing is behind sign-in.
   Its metadata counts (86/67/36/35) are excellent and free.
7. **No torrent/mirror assessment.** `tosec-main` carries *coverdisks*, not magazines; I found no
   magazine torrent beyond IA's own per-item `.torrent` files, which every item ships anyway.
