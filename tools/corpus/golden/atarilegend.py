#!/usr/bin/env python3
"""The golden set (#64a): Atari Legend's magazine index, as page-located review rows.

Atari Legend publishes, for every issue it holds, one table of `Title || Type || Page` rows. That is the
ground truth the extraction pipeline is scored against: a real page number per review, and the score the
magazine printed. This walks the eight magazine pages (one request each — the issues are anchors on the
magazine page, not separate URLs), parses every table, and writes JSONL.

The printed page is a **folio**, not a leaf index. Nothing here maps folio → leaf: that is the hard part
the pipeline must do per issue, and the reason this file records the folio verbatim.

    python3 atarilegend.py --out golden/atarilegend-reviews.jsonl
"""

from __future__ import annotations

import argparse
import html
import json
import re
import sys
import time
import urllib.request

UA = "TheST-corpus-research/0.1 (+https://github.com/laurentprudhon/the-st)"
BASE = "https://www.atarilegend.com"

# magazine id -> name, taken from the index page (the ids are not ordered)
MAGAZINES = {9: "Atari ST Review", 4: "Atari ST User", 11: "Atari World", 13: "Génération 4",
             2: "ST Action", 3: "ST Format", 12: "ST Magazine", 1: "The One"}

ROW = re.compile(r"<tr.*?</tr>", re.S)
CELL = re.compile(r"<t[dh].*?</t[dh]>", re.S)
TAG = re.compile(r"<[^>]+>")
ISSUE = re.compile(r'id="magazine-issue-(\d+)"')
# a score as the magazines print it: "85%", "8/10", "8,5/10", "4/5"
SCORE = re.compile(r"(\d{1,3}(?:[.,]\d)?)\s*(%|/\s*\d{1,3})")


def text_of(fragment: str) -> str:
    return re.sub(r"\s+", " ", html.unescape(TAG.sub(" ", fragment))).strip()


def fetch(url: str) -> str:
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    for attempt in range(3):
        try:
            with urllib.request.urlopen(req, timeout=60) as resp:
                return resp.read().decode("utf-8", errors="replace")
        except Exception:
            if attempt == 2:
                raise
            time.sleep(15 * (attempt + 1))
    raise RuntimeError("unreachable")


def parse_issue_tables(page: str, magazine: str, magazine_id: int) -> list[dict]:
    """One table per issue, in document order; the issue id comes from the nearest preceding anchor."""
    rows: list[dict] = []
    # split the page at each issue anchor so every table is attributed to the issue above it
    anchors = [(m.start(), int(m.group(1))) for m in ISSUE.finditer(page)]
    tables = [(m.start(), m.group(0)) for m in re.finditer(r"<table.*?</table>", page, re.S)]
    for pos, table in tables:
        issue = None
        for apos, aid in anchors:
            if apos < pos:
                issue = aid
            else:
                break
        for raw in ROW.findall(table):
            cells = [text_of(c) for c in CELL.findall(raw)]
            if len(cells) < 3:
                continue
            title_cell, kind, folio = cells[0], cells[1], cells[2]
            if not re.match(r"^\d+$", folio or ""):
                continue
            if kind.lower() not in {"review", "preview", "feature", "advert", "hints", "cheats", "news"}:
                continue
            score_raw, scale = None, None
            m = SCORE.search(title_cell)
            title = title_cell
            if m:
                score_raw = m.group(0).replace(" ", "")
                scale = "%" if m.group(2) == "%" else m.group(2).replace(" ", "")
                title = (title_cell[: m.start()] + " " + title_cell[m.end() :]).strip(" -–—%")
                title = re.sub(r"[-\u2013\u2014%\s]+$", "", title)
            rows.append({
                "magazine": magazine,
                "magazine_id": magazine_id,
                "issue_id": issue,
                "title": re.sub(r"\s+", " ", title).strip(),
                "kind": kind.lower(),
                "folio": int(folio),
                "score_raw": score_raw,
                "scale": scale,
            })
    return rows


def fetch_magazine(mid: int, name: str) -> list[dict]:
    """All pages of one magazine. Issues live as anchors on the magazine page, and long runs paginate."""
    rows: list[dict] = []
    seen_issues: set[int] = set()
    for page_no in range(1, 21):
        url = f"{BASE}/magazines/{mid}" if page_no == 1 else f"{BASE}/magazines/{mid}?page={page_no}"
        try:
            page = fetch(url)
        except Exception as exc:  # a 404 past the last page is the normal stop, not a failure
            print(f"#   {name} page {page_no}: {type(exc).__name__} — stopping", file=sys.stderr)
            break
        page_rows = parse_issue_tables(page, name, mid)
        issues = {r["issue_id"] for r in page_rows}
        if not page_rows or issues <= seen_issues:
            print(f"#   {name} page {page_no}: nothing new — stopping", file=sys.stderr)
            break
        seen_issues |= issues
        rows.extend(page_rows)
        time.sleep(1)
    return rows


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--only", type=int, default=None, help="one magazine id, for a smoke test")
    args = ap.parse_args()

    all_rows: list[dict] = []
    for mid, name in sorted(MAGAZINES.items()):
        if args.only and mid != args.only:
            continue
        rows = fetch_magazine(mid, name)
        issues = len({r["issue_id"] for r in rows})
        reviews = sum(1 for r in rows if r["kind"] == "review")
        scored = sum(1 for r in rows if r["kind"] == "review" and r["score_raw"])
        print(f"# {name}: {len(rows)} rows, {issues} issues, {reviews} reviews, {scored} with a printed score",
              file=sys.stderr)
        all_rows.extend(rows)
        time.sleep(1)

    with open(args.out, "w", encoding="utf-8") as fh:
        for row in all_rows:
            fh.write(json.dumps(row, ensure_ascii=False) + "\n")
    reviews = [r for r in all_rows if r["kind"] == "review"]
    print(f"# wrote {args.out}: {len(all_rows)} rows, {len(reviews)} reviews, "
          f"{sum(1 for r in reviews if r['score_raw'])} review scores", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
