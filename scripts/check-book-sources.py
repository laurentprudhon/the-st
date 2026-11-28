#!/usr/bin/env python3
"""The book's gate: five checks over a chapter file.

    scripts/check-book-sources.py book/part-2/2.2-the-chips.md
    scripts/check-book-sources.py --all
    scripts/check-book-sources.py --root scripts/tests/fixtures --no-manifest FIXTURE...

Checks
  1 structure   the template's shape, in order
  2 provenance  every numbered section carries a `*Sources: …*` line  (owner's rule: per section)
  3 paths       every repository path a chapter cites exists
  4 citations   every [n] resolves to the chapter's own numbered source list, and no entry is uncited
  5 manifest    the file is in book/manifest.json, with the id and the status it claims

Citations are numbered **per chapter**, in `## Where this comes from`: a reader can follow any
chapter without another file open. Paths in the `tos1x` tree and in EmuTOS are external and are
listed, not resolved — the chapter names their pinned commit instead.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Not ours to resolve: the trees the book cites but does not carry.
EXTERNAL = ("bios/", "bdos/", "vdi/", "aes/", "common/", "desk/", "system/", "glue/",
            "listings/", "roms/emutos", "src/", "include/", "lib/", "tools/", "toslib/")
# Ours: a citation of one of these must point at a file.
INTERNAL = ("research/", "design/", "docs/", "book/", "scripts/", "tests/", "atari-st-",
            "roms/", "steemsse", "hatari", "emutos", "vendor/", "figures/")

SOURCES_LINE = re.compile(r"^\*Sources: .+\*$")
NUMBERED = re.compile(r"^## (\d+)\. ")
CITE = re.compile(r"\[(\d+)\]")
PATHISH = re.compile(r"`([A-Za-z0-9_][A-Za-z0-9_./-]*\.[A-Za-z0-9]+(?::\d+(?:-\d+)?)?)`")
IMAGE = re.compile(r"\]\(([^)\s]+\.(?:png|svg|jpe?g))\)")


class Chapter:
    def __init__(self, path: Path):
        self.path = path
        self.lines = path.read_text().splitlines()

    def sections(self):
        """[(line_no, heading, body_lines)] for every `## n. <title>`."""
        out, cur = [], None
        for i, line in enumerate(self.lines, 1):
            if NUMBERED.match(line):
                if cur:
                    out.append(cur)
                cur = [i, line, []]
            elif line.startswith("## "):
                if cur:
                    out.append(cur)
                    cur = None
            elif cur is not None:
                cur[2].append((i, line))
        if cur:
            out.append(cur)
        return out

    def source_list(self):
        """{n: text} from `## Where this comes from`, or None if the heading is missing."""
        try:
            start = next(i for i, l in enumerate(self.lines)
                         if l.strip() == "## Where this comes from")
        except StopIteration:
            return None
        items, cur = {}, None
        for line in self.lines[start + 1:]:
            m = re.match(r"^(\d+)\.\s+(.*)$", line)
            if m:
                cur = int(m.group(1))
                items[cur] = m.group(2)
            elif cur and line.strip():
                items[cur] += " " + line.strip()
        return items


def check_structure(ch: Chapter) -> list[str]:
    out = []
    if not ch.lines or not ch.lines[0].startswith("# "):
        out.append("structure: the file does not open with a `# ` title")
    if "> **In one page.**" not in "\n".join(ch.lines):
        out.append("structure: no `> **In one page.**` box")
    if not ch.sections():
        out.append("structure: no `## n.` numbered section")
    heads = [i for i, l in enumerate(ch.lines) if l.startswith("## ")]
    if not heads:
        out.append("structure: no `## ` heading at all")
    elif not ch.lines[heads[-1]].strip() == "## Where this comes from":
        out.append("structure: the last `## ` heading must be `## Where this comes from`")
    return out


def _has_sources(body: list[tuple[int, str]]) -> bool:
    """A `*Sources:` block, single-line or wrapped: it must open and close with its italics."""
    for idx, (_, line) in enumerate(body):
        if line.strip().startswith("*Sources:"):
            if line.strip().endswith("*") and len(line.strip()) > len("*Sources: *"):
                return True
            for _, nxt in body[idx + 1: idx + 7]:
                if nxt.strip().endswith("*"):
                    return True
    return False


def check_provenance(ch: Chapter) -> list[str]:
    out = []
    for line_no, heading, body in ch.sections():
        if not _has_sources(body):
            out.append(f"{line_no}: provenance: section `{heading.strip('# ')}` has no `*Sources: …*` line")
    return out


def check_paths(ch: Chapter) -> tuple[list[str], int]:
    out, external = [], 0
    for i, line in enumerate(ch.lines, 1):
        for raw in IMAGE.findall(line):
            if not (ch.path.parent / raw).exists() and not (ROOT / raw).exists():
                out.append(f"{i}: paths: figure `{raw}` does not exist")
        for raw in PATHISH.findall(line):
            target = re.sub(r":\d+(?:-\d+)?$", "", raw)
            if target.startswith(EXTERNAL):
                external += 1
                continue
            if not target.startswith(INTERNAL):
                continue
            if "#" in target:
                target = target.split("#", 1)[0]
            if not ((ROOT / target).exists() or (ch.path.parent / target).exists()):
                out.append(f"{i}: paths: `{raw}` does not exist")
    return out, external


def check_citations(ch: Chapter) -> list[str]:
    """A chapter cites either by number or by path.

    Numbered: every [n] resolves to an entry, and every entry is cited — no decoration.
    By path: the numbered list is a bibliography, and the only requirements are that it exists and
    that its paths resolve (check_paths does that). Both forms are allowed by the brief; a chapter
    must not half-use the first.
    """
    out = []
    items = ch.source_list()
    if items is None:
        return out
    body_end = next((i for i, l in enumerate(ch.lines)
                     if l.strip() == "## Where this comes from"), len(ch.lines))
    cited = set()
    for n in CITE.findall("\n".join(ch.lines[:body_end])):
        cited.add(int(n))
    if not cited:
        if not items:
            out.append("citations: `Where this comes from` lists nothing")
        return out
    for n in sorted(cited):
        if n not in items:
            out.append(f"citations: [{n}] is not in `Where this comes from`")
    for n in sorted(items):
        if n not in cited:
            out.append(f"citations: entry [{n}] is never cited in the text")
    return out


def check_manifest(ch: Chapter, root: Path) -> list[str]:
    manifest_path = root / "manifest.json"
    if not manifest_path.exists():
        return []
    manifest = json.loads(manifest_path.read_text())
    rel = ch.path.relative_to(root).as_posix()
    entries = {c["file"]: c for p in manifest["parts"] for c in p["chapters"]}
    entries.update({a["file"]: a for a in manifest["appendices"]})
    if rel not in entries:
        return [f"manifest: `{rel}` is not in book/manifest.json"]
    e = entries[rel]
    if e["status"] != "written":
        return [f"manifest: `{rel}` is marked `{e['status']}` but the file exists"]
    return []


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("paths", nargs="*")
    ap.add_argument("--root", default=str(ROOT / "book"))
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--no-manifest", action="store_true")
    a = ap.parse_args()

    root = Path(a.root).resolve()
    if a.all:
        files = sorted(p for p in root.rglob("*.md")
                       if p.name not in ("README.md", "_template.md")
                       and ".audit" not in p.parts)
    else:
        files = [Path(p).resolve() for p in a.paths]
    if not files:
        print("no files to check")
        return 2

    failed, external = 0, 0
    for f in files:
        ch = Chapter(f)
        problems = check_structure(ch) + check_provenance(ch)
        p, ext = check_paths(ch)
        external += ext
        problems += p + check_citations(ch)
        if not a.no_manifest:
            problems += check_manifest(ch, root)
        name = f.relative_to(ROOT) if ROOT in f.parents else f
        if problems:
            failed += 1
            print(f"FAIL {name}")
            for prob in problems:
                print(f"  {prob}")
        else:
            print(f"ok   {name}")

    note = f" ({external} external citations listed, not resolved)" if external else ""
    print(f"\n{len(files) - failed}/{len(files)} files pass{note}")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
