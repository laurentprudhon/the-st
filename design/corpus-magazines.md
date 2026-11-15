# The magazine corpus — article, review and their scales

STATUS: **design (v1)** — 2026-11-15. Split out of `design/corpus-schema.md` when the software catalogue became
its own ruled design (owner: *"now, setting the magazine reviews aside"*). The catalogue half is there; this half
feeds #64 (the extraction pipeline) and #59 (the magazine survey). Rulings are the owner's, quoted in
`/opt/data/handoffs/the-st-52-schema-rulings.md`.

Magazine material is **not** part of the file catalogue's five levels: a review is about a *release*, but it is
its own record, because reviews are the filter axis over games while articles are reading.

## 1. Article

| field | type | note |
|---|---|---|
| `article_id` | key | `article:<magazine-slug>/<issue>/<first-page>-<last-page>` |
| `magazine` | ref | + `issue` (as printed), `issue_date` |
| `page_span` | (first, last) | **printed folios**, not PDF page indices — the leaf→folio map is imperfect (only 25/55 IA items had one) |
| `kind` | enum | `review` · `preview` · `hints` · `cheats` · `feature` · `news` · `advert` · `letter` |
| `title_as_printed` | string | as it appears on the page |
| `summary_en` | text | **ours**, 1–3 sentences |
| `artefact_refs` | list | store ids: page crops, formatted text |
| `provenance` | list | `origin_url` (where the page image came from — for credit and removal), measured `dpi`, OCR engine + version + date, confidence — **degradation is derived from `dpi` against the floor, never stored as a class** |

## 2. Review

**Ruled as a separate entity** (owner: *"i want to model articles and reviews structured data separately,
because reviews will be used to filter games much more often than articles will be read"*).

| field | type | note |
|---|---|---|
| `review_id` | key | `review:<article_id>` — a review always has an article (the page it lives on) |
| `release_reviewed` | ref, nullable | the release the magazine actually reviewed (Tilt scored **each machine separately in one entry**) |
| `version_reviewed` | string, nullable | as printed ("v1.04", "european version") |
| `scores` | list of `aspect_score` | **never a single number** |
| `verdict_quote` | ≤ 200 chars | the magazine's own words, short; longer prose stays in the store |
| `summary_en` | text | ours |
| `reviewer` | string, nullable | |
| `is_scored` | bool | ST Magazine printed **no scores at all** — presence must be explicit |

### 2.1 `aspect_score`

```
{ aspect: enum, value: number, scale_min: number, scale_max: number,
  direction: "higher-better" | "lower-better", raw: string }
```

All four fields, not one `score` column, from the counted corpus:

| magazine | raw | trap if flattened |
|---|---|---|
| ST Format, Joystick, ST Action | `87%` | none |
| **ACE** | `625/1000` | a `%` reader records 625 % |
| **Tilt** | `%`; before #39 `1–6`; #39–#112 `x/20` | **the scale changes inside one magazine's run** — the issue date must decide it |
| **Génération 4** | `%` → `/6` → `/20` | same |
| ST-Magazin | `Note: 4 von 6` | `/6` |
| ST-Computer, TOS | `Gesamtwertung 8` (0–10) | sub-scores exist in print only as graphics |
| **68000er** | `GN 1.83` | **lower is better** — inverting this silently is the worst bug available |

Aspect vocabulary, seeded from what the magazines print: `graphics`, `sound`, `animation`, `interest`, `overall`,
`gameplay`, `price`, `fun`. Open set, one row per aspect per review. `raw` is kept so a disagreement between our
parse and the printed page is always visible.

## 2.5 The link to the software catalogue

`article_titles (article_id, title_id, role)` — many-to-many, `role` = the article's `kind`. **Every article
about a game links to its title**, not only the ads: a review, a preview, a hints page, a cheats page, a feature,
a news item. A `review` *additionally* carries `release_reviewed`, because a review is about a specific edition
while the rest are about the work. One sentence: **article → title always; review → release additionally.**

That makes the two halves one corpus with two indexes: the catalogue answers "which articles exist about this
title, and what did they score it", and the magazine half answers "what was in this issue".

## 3. Where the material lives

Everything in this document lives in **`the-st-software`** (§0 of `design/corpus-schema.md`), together with the
software catalogue — the magazine half and the software half are two indexes of one repository, linked by
`article_titles`. Only the **full page scans** stay in the local cache, and the disk images with them.

| content | location |
|---|---|
| facts, scores, summaries, provenance | `the-st-software` too — the catalogue is one repository, not two |
| formatted text, page crops, box/disk/manual/leaflet scans and their OCR | the catalogue repository `the-st-software` (#60), public later |

Ruled: *"i think it is ok to reproduce magazine articles 30 years later even if it is not public domain, so i
would say : facts and summaries in the git repo, formatted text and pages crops in a private distinct repo for
now, which would be opened as a public website later, and referenced by the-st emulator."*

## 4. Engines — one runtime, four GGUF models

Owner's constraint: **llama.cpp v1**, GGUF only. Licences were checked through the HF API: OvisOCR3 and
LightOnOCR-3 are **Apache-2.0**, d1-2B and LFM3-VL-2B are **LFM Open License**, and **Surya 2.5 is out** — its
weights are OpenRAIL-M, which does not fit an OSS project with a public site later.

| role | model | measured (3070 Ti, one real page) |
|---|---|---|
| page → blocks + typed extraction | **OvisOCR3** 0.8B Q4 | **23.5 s**, 2,172 in / 5,526 out, **58 blocks** of valid JSON, closed 24-category set, crop rectangles free, tables as HTML |
| gates: is-this-a-review, has-score-box | **d1-2B** Q4 | **2.5 s**, 7,619 in / **0 out**, calibrated scores, all gate answers correct |
| English summary paragraph | **LFM3-VL-2B** Q4 | **7.6 s**, 1,126 out at a 2,500-token budget — at 600 it spends the budget narrating its own inspection |
| second engine for cross-check | **LightOnOCR-3** 0.8B Q4 | **11.2 s**, 8,796 chars; the transcription lands in `reasoning_content`, not `content` |

Numbers and traps: `research/ocr-smoke-test.md`. VRAM: **1,181 MiB @ 8k** context, **1,493 MiB @ 32k**, measured
with `tools/corpus/sizing/measure_vram.sh`; ~13 KiB per KV token, to be re-derived on other hardware.

Stages are **sequential on 8 GB** (all pages through OCR, then all blocks through the gate, then all summaries),
overlapped on a 32 GB card with all three resident.

## 5. Open

1. Whether `review` links to a **release** or a **title** when the magazine reviewed a version we cannot identify
   (common: a 1988 review of a game whose dumped release is 1990).
2. How a review of a **compilation** is recorded — one review covering five games, each scored separately.
3. The **resolution floor** value: ruled as a floor rather than a source rule (*"3"*), still unset because it
   should come from the measured DPI distribution, not from intuition. IA's per-page JPEG route measures
   **145 dpi** (1200×1560), below any plausible floor.
