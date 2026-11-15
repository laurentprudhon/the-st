# The corpus schema — what an entry, an article and a review are

STATUS: **proposal (v0)** — 2026-11-15. Written to make the ruled parts concrete before the extraction tool is
built. Each section says whether it is **ruled** (owner, quoted in `/opt/data/handoffs/the-st-52-schema-rulings.md`)
or **proposed** (mine, and open to being overturned).

This is the document the harvester (#53), the artefact store (#60) and the review extractor all read. It is a
design note, not a database migration: no table exists yet.

## 0. The two layers, and why they are separate

| layer | lives in | holds | ruled? |
|---|---|---|---|
| **catalogue** | this repo (`the-st`) | facts, hashes, provenance, our summaries | ruled (metadata-first, no binaries) |
| **artefact store** | a separate private repo, public later | formatted article text, page crops, box/disk/manual/leaflet scans + their OCR | ruled (#60) |

The catalogue never holds a scan; the store never holds a fact the catalogue does not also hold a pointer to.
That asymmetry is what makes the store replaceable and the catalogue durable.

## 1. The file model (ruled in shape, names proposed)

Five levels. **Provenance lives on the file; identity lives on the hashed image.**

```
title ──< release ──< disk ──< rendition >── file (many-to-many: a mirror is a second file)
                    │
                    └──< review >── article ──(page span)──> artefact (store ref)
```

- **title** — the work ("Outlands"). Key: slug. A title may have **zero releases on the ST**: five of the
  twenty sampled titles were in that state (Die Siedler, Ambermoon, Battle Isle, Game Over 1987, Dylan Dog),
  so "exists as a name, never shipped here" is a first-class state, not an absence.
- **release** — an edition: (title, publisher, year, region, kind ∈ original / budget / cracked / compilation
  member / localised). Key: the TOSEC-style tuple → slug. **Disk count is a release property**, because the
  same game is 2 disks in 1988 and 1 disk in the 1992 Hit Squad re-release (Navy Moves), and 2-disk retail vs
  1-disk French repack (Maniac Mansion).
- **disk** — one physical floppy. Key: (release, ordinal). A disk that appears in several releases is one disk
  if its bytes match, joined by hash.
- **rendition** — one byte-exact encoding of one disk in one container (`.stx`, `.scp`, `.ipf`, `.hfe`, `.st`,
  `.msa`). Key: (disk, container) + `image_hash` once fetched. **Protection state is a rendition property**:
  a Pasti `.stx` preserves it, a `[cr …]` `.st` removes it, and a title with no preserving rendition anywhere
  (Giana Sisters) is `unknown`, never `removed`.
- **file** — a fetchable URL: source, fetch date, HTTP status, size, the source's advertised strings, and
  whatever hash the host publishes. A **mirror** is a second file for the same rendition; a **bundle** is one
  file carrying several (Atarimania's 142 MB zips: 4 renditions + 166 `.hxcstream` sidecars).

**Proposed keys, not ruled**: `title:<slug>`, `release:<title-slug>/<publisher-slug>-<year>-<region>-<kind>`,
`disk:<release-key>#<ordinal>`, `rendition:<disk-key>@<container>`, `file:<source>/<path-hash>`. The rule they
obey: **a key is derived from what the source publishes, never from a fetched byte**, so an entry exists before
any file is ever downloaded.

## 2. Article (proposed)

| field | type | note |
|---|---|---|
| `article_id` | key | `article:<magazine-slug>/<issue>/<first-page>-<last-page>` |
| `magazine` | ref | + `issue` (as printed), `issue_date` |
| `page_span` | (first, last) | **printed folios**, not PDF page indices — the leaf→folio map is imperfect (only 25/55 IA items had one) |
| `kind` | enum | `review` · `preview` · `hints` · `cheats` · `feature` · `news` · `advert` · `letter` |
| `title_as_printed` | string | as it appears on the page |
| `summary_en` | text | **ours**, 1–3 sentences |
| `artefact_refs` | list | store ids: page crops, formatted text |
| `provenance` | list | scan source, DPI, provenance class (`own-scan` / `archived-scan-degraded` / `third-party-ocr-fallback`), OCR engine+version+date, confidence |

## 3. Review (ruled as a separate entity)

A review **is not** a field on a title or a release; it is its own record, because reviews are the filter axis:

| field | type | note |
|---|---|---|
| `review_id` | key | `review:<article_id>` — a review always has an article (the page it lives on) |
| `release_reviewed` | ref, nullable | the release the magazine actually reviewed (ST or Amiga — Tilt scored **each machine separately in one entry**) |
| `version_reviewed` | string, nullable | as printed ("v1.04", "european version") |
| `scores` | list of `aspect_score` | **never a single number** |
| `verdict_quote` | ≤ 200 chars | the magazine's own words, short; longer prose stays in the store |
| `summary_en` | text | ours |
| `reviewer` | string, nullable | |
| `is_scored` | bool | ST Magazine printed **no scores at all** — presence must be explicit |

### 3.1 `aspect_score`

```
{ aspect: enum,  value: number,  scale_min: number, scale_max: number, direction: "higher-better"|"lower-better",
  raw: string }
```

Why all four fields and not one `score` column, from the counted corpus:

| magazine | raw | trap if flattened |
|---|---|---|
| ST Format, Joystick, ST Action | `87%` | none |
| **ACE** | `625/1000` | a `%` reader records 625 % |
| **Tilt** (from #113) | `%`; before #39 `1–6`; #39–#112 `x/20` | **the scale changes inside one magazine's run** — the issue date must decide it |
| **Génération 4** | `%` → `/6` → `/20` | same |
| ST-Magazin | `Note: 4 von 6` | `/6` |
| ST-Computer, TOS | `Gesamtwertung 8` (0–10) | sub-scores exist in print only as graphics |
| **68000er** | `GN 1.83` | **lower is better** — inverting this silently is the worst bug available |

Aspect vocabulary, seeded from what the magazines actually print: `graphics`, `sound`, `animation`,
`interest`, `overall`, `gameplay`, `price`, `fun`. Open set, one row per aspect per review.

`raw` is kept so a disagreement between our parse and the printed page is always visible.

## 4. What the emulator and the store read

- The emulator references a **review** by `review_id` and an **artefact** by store id; it never reads the store
  directly for catalogue facts, and when the store is private it degrades to "no image available".
- The store id must be **stable before publication**, so it is content-derived, not path-derived (see #60).

## 6. Engines (2026-11-15) — one runtime, three models, all GGUF

Owner's constraint: **llama.cpp v1** as the only inference runtime. My earlier claim that `d1-2B` had no GGUF
was wrong (I searched three of five models): `LiquidAI/d1-2B-GGUF` is an official Liquid AI repo and llama.cpp
**b11960 / v0.8.0+** supports it. The two-runtime option is withdrawn — one runtime is enough.

| role | model | GGUF | llama.cpp status | licence |
|---|---|---|---|---|
| page → blocks + typed extraction | **OvisOCR3** (0.8B) | community (`mradermacher`, `prithivMLmods`, `Soulfate24`) | community-grade vision path — **to be proven on the golden set** | Apache-2.0 |
| gates: is-this-a-review, has-score-box, which-aspect | **d1-2B** | **official** `LiquidAI/d1-2B-GGUF` | `noul` / `choice` / `score` supported since b11960 | LFM Open License |
| English summary paragraph | **LFM3-VL-2B** | **official** `LiquidAI/LFM3-VL-2B-GGUF` | supported | LFM Open License |
| cross-check transcription (optional) | LightOnOCR-3-0.8B | community (`prithivMLmods`) | unverified | Apache-2.0 |

**The one runtime constraint that shapes the design**: llama.cpp supports d1's `noul`, `choice` and `score`
questions, but **not** its typed extraction (`text`/`number`/`date`), multiple-choice or boxes. That is fine by
construction — typed extraction is OvisOCR3's job (KIE with our JSON template), and d1 only ever answers gates.
If a later step needs d1 extraction, that single step goes to transformers, and the rest stays on llama.cpp.

**Scheduling consequence for the 8 GB box**: the stages are sequential, not concurrent — all pages through the
OCR server, then all blocks through the gate server, then all reviews through the summary server — so one model
is resident at a time. On a 32 GB card the same pipeline runs with all three loaded.

**Surya 2.5 is out**, and the reason is licence not quality: its weights are OpenRAIL-M, and this project is
OSS with a public artefact site later.

## 5. Open, and deliberately not decided here

1. **Q1 of #52** — the file model's identity rule above is still the *pending* question (five levels vs the
   alternatives); this document assumes the five-level shape.
2. Whether `review` links to a **release** or to a **title** when the magazine reviewed a version we cannot
   identify (common: a 1988 review of a game whose dumped release is 1990).
3. How a review of a **compilation** is recorded (one review covering five games, each scored separately).
4. The store's id scheme (#60) and the artefact/rendition boundary.
