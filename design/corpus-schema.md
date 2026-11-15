# The software catalogue — title, release, disk, rendition, file

STATUS: **design (v1)** — 2026-11-15, ticket #52. Every rule below is the owner's, quoted in
`/opt/data/handoffs/the-st-52-schema-rulings.md`; the deductions are marked as deductions. The magazine half of
the corpus lives in `design/corpus-magazines.md`.

This is the document the harvester (#53), the artefact store (#60) and the test pipeline (#61) read. It is a
design note, not a database migration: no table exists yet.

## 0. The two layers

| layer | lives in | holds |
|---|---|---|
| **catalogue** | this repo (`the-st`) | facts, hashes, provenance, our summaries — **no binaries** |
| **artefact store** | a separate private repo, public later (#60) | formatted text, page crops, box/disk/manual/leaflet scans and their OCR |

The catalogue never holds a scan; the store never holds a fact the catalogue does not also point at.

## 1. The five levels

**Ruled** (owner: "*five levels*"). Each level exists because an observed artefact forces it, not for tidiness.

| level | is | key | forced by (counted) |
|---|---|---|---|
| `title` | the work: "Populous" | slug | a compilation disk carries several titles, so the work must exist apart from any disk |
| `release` | a publication: publisher, year, **language**, region, kind, **name of its own**, `disk_count` | slug | Captain Blood's French retail dump is 33.7 MB against 543 KB for the cracked `.st`; *L'Arche du Capitaine Blood* is a different publication name |
| `disk` | one physical floppy | (release, ordinal) | `Outlands_(A).stx` + `Outlands_(B).stx`; Turrican's zip carries three |
| `rendition` | one byte-exact encoding of one disk in one container, with its modifications | (disk, container, sha256) | F-19's single zip carries **four renditions of three disks** |
| `file` | one fetchable URL | (host, path) | Turrican's Atarimania zip holds **171 entries**, of which 166 are per-track `.hxcstream` sidecars |

and one join, **`rendition_titles (rendition_id, title_id, role, part, slot)`** — the content relation, many-to-many.
There is deliberately **no `title_id` on `release`**: the only path from a release to a title goes through the
rendition, so a single-game release and a menu disk take the same route.

```
release ──< disk ──< rendition >── file        (file↔rendition M:N: a mirror, a sidecar, a bundle part)
                        │
                        └──< rendition_titles >── title    (M:N: compilations, coverdisks, multi-disk games)
```

## 2. Identity — three different rules, and they must not be confused

**A title exists only if a file exists.** Ruled: *"there should not be titles without files in the catalog"*. The
catalogue is a **presence** catalogue, not a bibliography: absence is a non-row, never a state. The five sampled
titles with no ST release (Die Siedler, Ambermoon, Battle Isle, Game Over 1987, Dylan Dog) are simply not in it,
and "15.2 % of entries are unopenable" is a fact *about the sources*, not a gap in the catalogue.

**A release is identified by its own name**, never derived from its content — because a compilation cannot be
named after one of its games, and a translated edition has a different name for the same work.

**A rendition is identified by the SHA-256 of its container bytes.** Ruled. Three consequences:

- two hosts serving the byte-identical `.st` are **one rendition with two `file` rows** — the dedupe that matters;
- a `.st` and a `.stx` of one disk are **always two renditions**, and that is honest: they are different dumps;
- therefore **a `disk` is not identified by any hash.** It is a *claim* — "this rendition is of that disk" —
  carrying its own evidence tier: `stated` when the source labels it (PlanetEmu's `Vroom (1991)(Lankhor)`,
  an Atarimania page), `inferred` when a later comparison argues for it. An unmatched rendition is allowed and is
  its own disk until something says otherwise.

The corollary that saves a future mistake: **renditions of one disk are expected to differ in bytes** — that is
what a crack *is*. So sector-level equality cannot decide the disk match either. The disk comes from the
source's labelling or from context, never from comparing data.

## 3. Releases carry the language

Ruled: *"release must carry the language for example"*. Language is a property of the **publication**, never of
the title — so there are never two `title` rows for one work, and "every language version of Populous" is a
join. The translated name lives on `release.name`, with the work's canonical name on `title.name` and the
alternates in `title.name_alt[]` for search.

Refinement for the schema, not yet needed: a release shipping two languages on one disk (some games ask at boot)
needs `languages[]` rather than `language`.

**`release.disk_count` is `stated`, and it is what makes completeness expressible.** The number of `disk` rows is
*counted*; when counted < stated the release is incomplete, and that is a query rather than a flag. Deriving the
count from the files cannot distinguish "a 2-disk release, both held" from "a 3-disk release, one missing". When
nobody states the count it is NULL and completeness is honestly unknowable.

```sql
SELECT name, disk_count, COUNT(disk_id) FROM release
GROUP BY release_id HAVING COUNT(disk_id) < disk_count;   -- "we hold 2 of this release's 3 disks"
```

## 4. Content — compilations, coverdisks, multi-disk games

Ruled: *"we also need to model pompey pirates compilations. a rendition can carry several titles"*. The content
relation is M:N with a `role`:

| `role` | means | `part` / `slot` |
|---|---|---|
| `only` | the whole game is this rendition (a normal single-disk release) | — |
| `part` | the game spans several renditions: `part` = 1..N | the ordinal |
| `menu` | the game is one entry of a compilation or coverdisk | `slot` as the menu shows it |

A compilation is **a release whose `kind` is `compilation` and whose renditions carry several titles**. There is
no compilation table, no `compilation_of` column and no `also_on` field — a game on Pompey Pirates 42 is a second
content row pointing at the *same* `title`, and the menu-disk copy needs no release of its own: the compilation's
release is its publication event. A magazine coverdisk is the same shape again, which matters because the
menu-disk CDs are already a real slice of what the hosts serve.

A multi-disk *compilation* (different games on each disk) is `role=menu` with slots per disk; a game spanning two
menu disks is `role=part` on both. Neither is a new structure.

### 4.1 Two `sides` columns, and why

`disk.sides` is the **physical** disk (1 or 2). `rendition.sides` is what the **image** covers (`both` / `0` /
`1`). So a 360 KB file is a side-0 image of a 2-sided disk — a partial rendition, not a different disk, and "we
have side A only" stays expressible.

## 5. Modifications

Ruled: *"cheats/trainers/copy protection removal"*, with "cheat" fixed as a **built-in cheat menu** (the
published poke is out of scope; if it ever enters, pokes attach to the title, per the rule *in the bytes →
rendition; about the game → title*).

Placement, and the reason it is not on the release:

- `rendition.modifications[]` — changes to the **whole image**: a menu/loader patch, disk-level protection removal;
- `rendition_titles.modifications[]` — changes to **one game on that disk**: its trainer, cheat menu, its
  protection removal.

**The rule: a modification is recorded where the bytes it changed live — whole disk → rendition, one game → the
content entry.** Two ordinary corpus shapes defeat the release level: one release held twice, pristine (an
original Pasti dump) and trained (a cracked `.st`), needs the pristine copy to say "none"; and one menu disk
routinely repackages games cracked by *different* groups, so one author per disk — or per release — is wrong.

`modifications[] = {kind: trainer | cheat-menu | protection-removed | patch, detail, author}` — `author` being
the cracking group, which is why **a crack never needs to become a release of its own**.

## 6. Artefacts — manuals, boxes, disks, screenshots, ads

**Ruled** (owner, 2026-11-15): *"in the catalog, it is important to store also manual, screenshots, disk and box
cover files, ads"*, and *"ocr on the manual in several languages will be very important also"*.

An artefact is a **document about** something in the catalogue: its bytes live in the artefact store (#60), its
facts and its store pointer live here — the same asymmetry as the magazine half.

| field | note |
|---|---|
| `artefact_id` | |
| `kind` | `box-front` · `box-back` · `manual` · `leaflet` (copy-protection insert) · `disk-label` · `disk-scan` · `screenshot` · `ad` · `poster` · `map` |
| `attached_kind` + `attached_id` | `title` · `release` · `disk` — the rule below |
| `language` | on the **artefact**, never on the attachment |
| `pages` | manuals and leaflets |
| `source` | `own-scan` · `archived-scan` · `emulator-capture` · `magazine-page` |
| `store_ref` | the store id (content-derived, stable before publication) |
| `ocr` | `{engine, model_version, date, pages, confidence}` + a store ref for the text |

**The attachment rule, one sentence:** *an artefact attaches to the most specific level whose artefact it is.* A
box front, a manual and a protection leaflet belong to the **release** — they are edition- and language-specific.
A disk label or a disk scan belongs to the **disk** — it is that physical floppy. A **screenshot** belongs to the
**title**, because it shows the game rather than an artefact of one edition, with `source = emulator-capture` and
the rendition it came from recorded in its provenance.

**Multi-language manuals are not a special case**: the language is on the artefact, so a release with an English
and a German manual has two `manual` artefacts and two OCR records. That is the whole of the requirement.

**Screenshots are the one artefact kind the project can generate rather than collect**, so `emulator-capture` is a
first-class `source` and the golden set (#64a) is where they come from — while `archived-scan` means what it
says: a screenshot of unknown provenance, from the internet.

**An ad is the bridge between the two halves of the corpus.** It is at once an `article` of kind `advert` in the
magazine half (with its magazine, issue, page and the scan it was OCR'd from) and an `artefact` of kind `ad`
attached to a **title** here; the artefact carries `article_id`, and it is the only entity that spans both
documents.

**Why manual OCR is the highest-value OCR in the project**, recorded because it changes its priority: a large
share of ST games are unplayable without the manual, because the copy protection asks for a word from a given
line and page, or a code from a printed wheel. A searchable manual text is what lets the emulator **answer that
prompt** — which moves those titles out of `disk-protection-emulation-failure` with no emulator work at all. It
also serves the later website, and it is the same pipeline as the magazines (blocks → gates → summary), so it
costs an adaptation rather than a second system.

## 7. Files, claims and verdicts

A `file` is one fetchable URL: `host`, `path`, `url`, `http_status`, `size`, `sha256`, `fetched_at`, the
`advertised` strings the source publishes, and a provenance tier.

**Both verdicts are properties of the file** (ruled), because what they test is a *claim* — and the claim is
per-listing, not per-bytes: two files of one rendition can be advertised as different titles on different hosts.

| field | scope | values |
|---|---|---|
| `runs` | the **file** | `plays` · `boots` · `disk-image-corrupted` · `disk-protection-emulation-failure` · `machine-emulation-failure` · `unknown` — total, exactly one per file |
| `is-what-it-claims` | the **file** | `agrees` / `disagrees` / `undetermined` on title, version, disk count and language, **with the compared strings recorded** |
| `run-status` | the **run**, never the file | `ok` · `harness-failure` (cache, path, decoder, screenshot) |

`runs` is an enum rather than a boolean because it names **whose** fault: `disk-image-corrupted` is a fact about
the dump (a catalogue note), `disk-protection-emulation-failure` is a gap in our emulator, and
`machine-emulation-failure` is a bug in the machine model — only the last two are emulator work items.

**`is-what-it-claims` is filled in two passes** (ruled):

1. **Inspection of the container content** — cheap, deterministic, runs on every file: the boot sector's
   geometry, the FAT's directory entries, the volume label, the strings the label implies. This is what fills
   the column for the whole corpus.
2. **Escalation for some titles** — run it in the emulator and have a VLM read the title screen. Expensive; used
   to adjudicate what pass 1 flagged, and for trained copies whose filenames are unchanged.

A value is written only with its evidence; `undetermined` beats a guess. `run-status = harness-failure` never
touches the file's verdict, so our own tooling bugs cannot inflate "how much of the corpus fails", and a wrong
title cannot masquerade as an emulator defect.

## 8. Evidence tiers

Every figure and every claim carries one: **`counted`** (fetched and parsed, method stated), **`stated`** (a source
says so, quoted), **`derived`** (inference from counted shapes), **`unverified`** (believed, not observed). This
applies to structure too: `release.disk_count` is stated while the disk rows are counted, and `disk` identity is
a stated or inferred claim while the rendition hash is counted.

## 9. The queries this model must answer

```sql
-- every language version of a work
title → rendition_titles → rendition → disk → release, select release.language

-- how many renditions v1's emulator can actually read (the disk-path floor, #29)
SELECT container, COUNT(*) FROM rendition
WHERE container IN ('st','msa','dim','stx')  -- vs 'ipf','scp','hfe' held but deferred
GROUP BY container;

-- which versions of a game are trained, and by whom
rendition_titles WHERE title_id = ? → modifications[].author

-- what is missing from a release
release.disk_count vs COUNT(disk)

-- which releases ship without a manual, and in which language the manual is missing
release LEFT JOIN artefact ON (attached_kind='release' AND kind='manual') WHERE artefact_id IS NULL

-- the corpus's health, by fault domain
SELECT runs, COUNT(*) FROM file GROUP BY runs;      -- excludes harness failures by construction
```

## 10. What this forbids

`title.language` · per-language title rows · `title_id` on `release` · a `compilation_of` column · an `also_on`
field · a "disk 2 of 3" string inside a filename · a `protection_state` column on a title or release · a
`works: bool` column · deriving `disk` identity from a hash · deriving `release.disk_count` from the files · an artefact attached to more than one level · a `manual` without a `language` · an `ad` without its `article_id` · a screenshot treated as a scan when the emulator made it.

## 11. Open

1. **`release.kind`'s closed set** — `retail` · `budget` · `compilation` · `coverdisk` · `homebrew`? A crack is
   *not* a kind (it is a rendition with modifications, §5), which keeps the set small.
2. **Whether a title's canonical name is English** — the search index needs one name per work, and
   *L'Arche du Capitaine Blood* vs *Captain Blood* is the test case.
3. **What pass 1 of §7 can actually decide** — to be established against a sample of real containers, not
   assumed; a label blank on a cracked copy is the expected failure.
4. **The artefact/rendition boundary and the store's id scheme** (#60).
