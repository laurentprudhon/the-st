# The corpus workstream — handoff to the GPU machine

STATUS: ready to pick up — 2026-11-15. Written on the 3070 Ti box, for a fresh container on the RTX 5090.
Everything here is landed on `main` of `laurentprudhon/the-st` unless it says otherwise.

## 0. What you are picking up, in one paragraph

Build the **preservation catalogue of Atari ST software** and the machinery that fills it: sweep every source we
have mapped, download what they serve into a local content-addressed cache, populate the catalogue's records from
what the files actually are, and run the **OCR pipeline** over magazine pages and manuals on this machine's GPU.
The catalogue's design is ruled and written (`design/corpus-schema.md`); your job is to populate it, in that shape,
without inventing a second shape.

Read, in this order, before touching anything:

1. `design/corpus-schema.md` — the ruled schema. **This is the contract.**
2. `design/corpus-magazines.md` — the magazine half (articles, reviews, scores, the four models).
3. `research/st-host-registry.md` — every source we mapped, about 330 domains, with what each one *delivers*.
4. `research/st-software-sources.md` and `research/st-download-sources.md` — the two surveys, with dated
   correction blocks (the originals are kept on purpose).
5. `research/st-magazines.md` — per-country titles, six score scales, the DPI distribution.
6. `research/ocr-smoke-test.md` and `research/ocr-batching-and-sizing.md` — measured model behaviour and VRAM.
7. `docs/handoffs/ocr-pipeline.md` — the self-contained OCR handoff, including the six traps.

## 1. The three tiers, and where your bytes go

| tier | where | holds | visibility |
|---|---|---|---|
| **catalogue** | new repo `laurentprudhon/the-st-software` | structured data, our summaries, manual OCR text, the media manifest | **private for now** (ruled 2026-11-15) |
| **media** | a **local static site** on this machine, rooted so paths mirror `media/<sha256[0:2]>/<sha256>.jpg` | crops, boxes, manual page images, leaflets, disk labels, screenshots, article HTML | served on the LAN; R2 later, same keys |
| **cache** | `~/corpus-cache` | **disk images and full magazine pages** | never published |

**The key property you must not break:** the media key is host-independent and content-addressed, so the same
directory works as a local site today and as an R2 bucket later. The catalogue stores `media_key`, never a host or
a URL for our own media. (`origin_url` is the *source's* URL, for credit — a different thing.)

## 2. Tickets, and their state

| ticket | state | what it is |
|---|---|---|
| #52 | **closed** | the schema — five levels, identity rules, artefacts, verdicts |
| #57, #59 | **closed** | the title-first sweep and the magazine survey |
| #60 | open | create `the-st-software`: structure, ids, the one-string link, the media manifest |
| #53 | open | the harvest: source adapters, dedupe, what an entry may claim |
| #61 | open, **partly blocked** | the dev-phase test corpus — `runs` needs the emulator's beta, `is-what-it-claims` pass 1 does not |
| #64 | open | the extraction pipeline: stage → extract → verify, with a golden set as its acceptance test |

Claim a ticket before touching it (`gh issue edit N --add-assignee @me`). #60 first: the catalogue needs a home
before it can be filled. Then #53 (harvest) and #64 (OCR) run in parallel — the harvest is I/O-bound and the OCR is
GPU-bound, and they do not touch the same code.

## 3. The ruled design, compressed to what constrains you

Full text in `design/corpus-schema.md`; the rulings verbatim in `/opt/data/handoffs/the-st-52-schema-rulings.md`
on the machine that wrote it. The twelve that will bite you if you improvise:

1. **Five levels**: title / release / disk / rendition / file, plus `rendition_titles` and `artefact`/`article`/`review`.
2. **Presence catalogue** — no title without a file. Absence is a non-row, never a state.
3. **A rendition is identified by the SHA-256 of its container bytes.** Two hosts serving identical bytes are one
   rendition with two `file` rows. `.st` and `.stx` are always two renditions.
4. **A `disk` is a claim, not a hash.** `stated` from the source's labelling, `inferred` later. Renditions of one
   disk are *expected* to differ in bytes — that is what a crack is.
5. **`release` carries its own name and the language**; never a `title_id`.
6. **Content is M:N at the rendition** — `role ∈ {only, part, menu}` — so compilations, coverdisks and multi-disk
   games need no new structure.
7. **Modifications go where the bytes changed**: whole disk → `rendition.modifications`, one game →
   `rendition_titles.modifications`. "Cheat" means a built-in cheat menu.
8. **`release.disk_count` is stated, the disk rows are counted** — the gap is the completeness signal.
9. **`runs`** is a six-value enum naming the fault domain; **`run-status`** is on the run, never on the file.
10. **`is-what-it-claims` is per file**, filled by container inspection first, then emulator + VLM on some titles.
11. **Every figure carries a tier**: `counted` / `stated` / `derived` / `unverified`. A value without its evidence
    is an opinion.
12. **Scope: ST only.** Reviews of ST games (including the ST columns of a multi-machine review), features about
    the ST, hints/cheats pages, ST ads. No Amiga-only, C64-only or console-only content, no general industry news.

## 4. The cache, exactly

```
~/corpus-cache/
  blobs/<sha256>                 # the bytes, once, whatever container or host they came from
  pages/<mag>/<issue>/<n>.jpg    # full magazine pages, as downloaded
  index.sqlite                   # url -> sha256, status, size, fetched_at, host
  media/                         # the local site root (see §5)
```

**Exact deduplication, not clever deduplication**: one blob per SHA-256. A file served by three hosts is stored
once and appears as three `file` rows pointing at the same `rendition`. Do not dedupe on filenames, sizes or
"close enough" — the whole identity rule of §3.3 rests on you using the hash.

`index.sqlite` is a *cache index*, never the catalogue. The catalogue is the `the-st-software` repository; the
index is how you avoid re-fetching. If the index and the catalogue disagree about a hash, the catalogue is right
and the file is re-fetched.

## 5. The local media site

Root it at `~/corpus-cache/media` and serve it with anything static (`python3 -m http.server`, nginx, Caddy):

```
media/ab/abcdef…1234.jpg     # the object, content-addressed, immutable
```

Write `Cache-Control: public, max-age=31536000, immutable` when you move to a real server. Because keys are content
hashes, nothing is ever invalidated, and the same tree uploads to R2 unchanged when the catalogue goes public.

Record for each object in the catalogue's manifest: `key`, `sha256`, `size`, `origin_url`, `credit`, plus the crop
geometry. **The manifest is what makes the bucket replaceable** — and once the full page leaves the cache, the crop
is the only surviving derivative, so the manifest plus a backup is the whole preservation story for media.

## 6. The discovery pass — the one time we ignore `robots.txt`

Ruled (owner, 2026-11-15): *"ignoring the robots.txt instructions just once to build this map of the web, and asking
for logins when needed instead [of] ignoring some websites."*

So: **one** complete discovery pass, and then never again in bulk. The rules that make it defensible:

- **Identify yourself.** One honest User-Agent with a contact URL. Not a browser string.
- **Rate-limit per host** — one connection, ≥1 s between requests, and honour `429`/`503` with a long backoff.
  A directory listing is one request; a hundred product pages are a hundred requests, spaced.
- **`HEAD` and 1-byte `Range` before any body**; read zip **central directories** by range rather than downloading
  a 142 MB archive to list it.
- **Record the decision, not just the fetch**: `robots.txt` allowed/denied/absent, per host, in the index. The next
  pass must be able to honour what this one overrode.
- **Logins: ask the operator.** When a source needs authentication, stop and ask — do not silently skip the site and
  do not guess credentials. (Sites known to be gated: `tosec-main` returns 403 to anonymous; several forums and a
  few national archives.) The point of asking is coverage: a login-gated host that carries files is worth a
  conversation, and the owner would rather be asked than lose the source.

**What the pass must produce**: a row per host — reachable, `robots.txt`, what it serves (bytes / catalogue only /
dead), how many entries, which container formats, whether a file's listing is enumerable, and the URL pattern for an
entry. That table is the answer to "where does the corpus come from", and it is what #53 turns into adapters.

## 7. The sources you already have

Do not start from zero: `research/st-host-registry.md` has ~330 domains probed, ~45 serving bytes, 22 countable.
The spine, counted:

| host | what it holds |
|---|---|
| Atarimania | ~9 988 titles; multi-format zips (533 KB → 142 MB), Pasti + `.ipf` + `.scp` + per-track sidecars |
| PlanetEmu | **14 001** game entries, CRC32 published (11 872 `.st`, 1 444 `.stx`, 413 `.ipf`, 178 `.scp`) |
| `ftp.untergrund.net` (fujiology) | **14 644** ST files + MD5 manifests |
| IA | four collections; `stream-ipf` = 3 643 `.7z` / 100 GB **misfiled under `softwarelibrary_apple`** |
| sync.wtf | 5 106 (2 552 `.scp`, 1 895 `.msa`, 659 `.stx`) |
| edgeemu · romspedia · wowroms · emuparadise | 311 · 2 813 · 4 261 · 8 400 direct files |
| ataristdb · atari-home · exxos Floppyshop | 3 409 CSV rows · 9 986 PD files · 4 072 |
| oldgames.sk · atarilegend | 281 · games + 770 located reviews |

`lankhor.net` is **the only rights-holder permission** in the ST world, which is why the catalogue stores pointers
and hashes and the cache holds the disks privately.

## 8. The OCR pipeline

`docs/handoffs/ocr-pipeline.md` is self-contained (stages, models, VRAM, traps). The essentials:

- **Runtime**: llama.cpp only, GGUF only. `tools/corpus/setup.sh` installs the CUDA nightly and the four models.
- **Models**: OvisOCR3 (page → typed blocks, closed 24-category set, crop rectangles free), d1-2B (gates, 0 output
  tokens), LFM3-VL-2B (English summaries, ~2 500-token budget), LightOnOCR-3 (second engine, output lands in
  `reasoning_content`).
- **VRAM**: 1 181 MiB @ 8k context, 1 493 MiB @ 32k — **re-derive on the 5090** with
  `tools/corpus/sizing/measure_vram.sh` before planning batches; the 13 KiB/token constant is card- and
  driver-specific.
- **Order of work**: the **golden set** first (#64a) — Atari Legend's 770 page-located reviews with folios, hand-
  verified scores. It is the acceptance test for everything downstream, and building it first is what stops you
  from tuning against a moving target.
- **Never reuse internet OCR** (ruled): every page is OCR'd from page images, so one engine/version/confidence
  scale makes the error rate measurable. IA's `_djvu.txt`/`hocr` may be used **only as a cross-check on pages whose
  own image is missing**.

## 9. What "done" looks like for this phase

Numbers, because "populated" is not a finish line:

1. **Every host in the registry has a row** — reachable, robots, what it serves, entry count, URL pattern — and the
   ones needing a login have been *asked about*, not skipped.
2. **`the-st-software` exists**, private, with the schema, the manifest layout, and the first data committed through
   a rebase + fast-forward-only ref update.
3. **Coverage reported, not assumed**: for each of the twenty sample titles, how many distinct renditions were found,
   how many are byte-identical across hosts (the dedupe ratio), and how many hosts served them.
4. **Cache integrity**: every blob has a `sha256` that matches its name; `index.sqlite` reconciles with it; no
   duplicate bytes on disk (report the dedupe ratio achieved).
5. **`is-what-it-claims` pass 1 runs on every fetched container** and its verdict distribution is reported, with the
   `undetermined` share.
6. **The golden set exists** (§8) and the pipeline's score-accuracy on it is measured — not estimated.

## 10. The traps already paid for

Each of these cost a session; none is obvious.

- **Fabricated identifiers never 404 usefully.** A subagent constructed `atari-st-format-issue-001`-style ids, got
  0 files, and reported "no OCR available" for 5 items. Take identifiers **only** from `advancedsearch`/`scrape`
  results; treat a 0-file item as a bad identifier and discard it.
- **Summed fields lie.** "1 362 pages for 70 items" came from summing a metadata field (~19 pages/issue against
  ~100 actual). Count per item, from `imagecount`, the `_jp2.zip` listing, or the PDF's page count.
- **A file's advertised name is not its identity.** PlanetEmu's page says `Vroom (1991)(Lankhor).st` with CRC
  `e2ac6d05`; the *bytes* decide, and half the corpus is misfiled somewhere.
- **Five of the twenty sample titles have no ST release at all** (Die Siedler, Ambermoon, Battle Isle, Game Over
  1987, Dylan Dog). Do not spend a day hunting a file that never existed.
- **The same bytes appear under different names on the same host**, and different bytes under the same name across
  hosts. Hash everything, trust nothing.
- **`myrient.erista.me` is gone**; `tosec-main` 403s anonymously. Check before planning around a host.
- **LFM3-VL-2B narrates its own inspection** and returns nothing at a small `max_tokens`; LightOnOCR-3 returns its
  transcription in `reasoning_content`, not `content`; OvisOCR3 truncates mid-JSON below ~8k output tokens.
- **A "0 results" from a search is not a source being empty** — it is usually a query shape. The owner found six
  hosts on one title that the first sweep missed, which is why the sweep is now site-first, not title-first.

## 11. Environment on this machine

```sh
export GH_TOKEN="$GITHUB_TOKEN"          # from the dashboard's API Keys page, never from chat
git clone https://github.com/laurentprudhon/the-st.git ~/the-st && cd ~/the-st
gh auth setup-git
bash tools/corpus/setup.sh               # llama.cpp CUDA nightly + 4 GGUF models into /opt/data/models
/opt/data/models/llama/llama-b12409/llama-server --list-devices   # must print CUDA0 with ~32 GB
tools/corpus/sizing/measure_vram.sh      # re-derive 1 181 MiB @ 8k / 1 493 MiB @ 32k on this card
```

Container notes: the engine passes the GPU by itself (no `--gpus` flag, no nvidia-container-toolkit); it has no
`--restart` policy and bridge networking only. If the GPU ever fails to appear, run `llama-server` in the WSL distro
and call it over HTTP — the pipeline needs the four endpoints, not the device.

## 12. Boundaries

- **Do not touch** `src/TheST.Machine/**`, `tools/validation-harness/**`, `tools/suspension-bench/**`,
  `scripts/validate.sh`. Another workstream is measuring frame time in `src/` and a third is closing the CPU corpus.
- **Corpus code** lives under `tools/corpus/` until `the-st-software` exists, then moves there.
- **Land docs and data on `main` by rebase + a fast-forward-only ref update**:
  `gh api -X PATCH repos/laurentprudhon/the-st/git/refs/heads/main -f sha=$SHA -F force=false`. Never a merge
  commit.
- **Claim before touching.** Comment the ruling before acting on it.
- **Report counts, never adjectives.** "14 001 entries, 2 129 unopenable (15.2 %), counted by fetching each page"
  is a report; "most of the corpus" is not.

## 13. The one thing not to do

Do not redesign the schema. It took a long grilling to settle and every rule in §3 exists because an observed
artefact forced it. If something genuinely does not fit, write it down as a question with the example that breaks
it, and ask — that is how the last eleven rulings arrived.
