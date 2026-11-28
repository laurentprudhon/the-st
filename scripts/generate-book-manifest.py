#!/usr/bin/env python3
"""Generate book/manifest.json and the README's table of contents from the brief.

The brief (`docs/handoffs/documentation-phase.md` §3) is the single input: a chapter
cannot exist in the brief and not in the manifest, or the reverse. Counts are asserted
against the parsed list, never typed — the "30 chapters" bug was exactly this.

    scripts/generate-book-manifest.py            # write manifest + README TOC
    scripts/generate-book-manifest.py --check     # fail if either is stale
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BRIEF = ROOT / "docs/handoffs/documentation-phase.md"
BOOK = ROOT / "book"
MANIFEST = BOOK / "manifest.json"
README = BOOK / "README.md"

# Tokens whose capitalisation the filename slug cannot carry.
TOKENS = {
    "st": "ST", "ste": "STE", "tos": "TOS", "bios": "BIOS", "xbios": "XBIOS",
    "gemdos": "GEMDOS", "vdi": "VDI", "aes": "AES", "cpu": "CPU", "dma": "DMA",
    "mfp": "MFP", "psg": "PSG", "ikbd": "IKBD", "acia": "ACIA", "rom": "ROM",
    "roms": "ROMs", "ram": "RAM", "fdd": "FDD", "dram": "DRAM", "asic": "ASIC",
    "blitter": "blitter", "gstmcu": "GSTMCU", "1040ste": "1040STE", "io": "I/O",
}


def parse(text: str):
    parts, appendices = [], []

    def rows(block: str):
        out = []
        for line in block.splitlines():
            m = re.match(r"\|\s*`([^`]+\.md)`\s*\|", line)
            if m:
                out.append(m.group(1))
        return out

    # `### Part N — <title> (<k> chapters)` ... table ... until the next `### ` or `---`
    for m in re.finditer(r"^### Part (\d+) — (.+?) \((\d+) chapters?\)\n", text, re.M):
        n = int(m.group(1))
        title = m.group(2).strip()
        declared = int(m.group(3))
        block = text[m.end():]
        nxt = re.search(r"^### ", block, re.M)
        block = block[: nxt.start()] if nxt else block
        files = rows(block)
        assert len(files) == declared, (
            f"part {n}: heading says {declared} chapters, table lists {len(files)}"
        )
        parts.append({"id": n, "title": title, "chapters": files})

    m = re.search(r"^### Appendices \((\d+) files?\)\n", text, re.M)
    assert m, "no appendices heading"
    declared = int(m.group(1))
    block = text[m.end():]
    nxt = re.search(r"^---", block, re.M)
    appendices = rows(block[: nxt.start()] if nxt else block)
    assert len(appendices) == declared, (
        f"appendices: heading says {declared}, table lists {len(appendices)}"
    )
    return parts, appendices


def humanise(fname: str) -> str:
    """`2.2-the-chips.md` -> `the chips`, `1.1-the-1040ste.md` -> `the 1040STE`."""
    slug = re.sub(r"^[0-9A-D]+(\.[0-9]+)?-", "", fname)
    slug = slug[:-3] if slug.endswith(".md") else slug
    words = [TOKENS.get(w, w) for w in slug.split("-")]
    out = " ".join(words)
    return out[0].upper() + out[1:] if out else out


def build():
    text = BRIEF.read_text()
    parts, appendices = parse(text)

    total = sum(len(p["chapters"]) for p in parts)
    per_part = [len(p["chapters"]) for p in parts]
    expected = "4 + 9 + 5 + 8 + 7 + 10"
    typed = re.search(r"\*\*(\d+) chapters in six parts\*\* \(([0-9 +]+)\)", text)
    assert typed, "the brief no longer states the chapter count in the expected form"
    assert int(typed.group(1)) == total, (
        f"brief says {typed.group(1)} chapters, the tables list {total}"
    )
    assert typed.group(2).strip() == expected, (
        f"brief says {typed.group(2).strip()}, generator expects {expected}"
    )
    assert per_part == [4, 9, 5, 8, 7, 10], per_part

    def entry(part_id, fname, kind):
        path = BOOK / kind / fname
        written = path.exists()
        m = re.match(r"^([0-9A-D]+(?:\.[0-9]+)?)", fname)
        assert m, fname
        return {
            "id": m.group(1),
            "file": f"{kind}/{fname}",
            "title": humanise(fname),
            "status": "written" if written else "planned",
        }

    manifest = {
        "generated_from": "docs/handoffs/documentation-phase.md §3",
        "generated_by": "scripts/generate-book-manifest.py",
        "counts": {"parts": len(parts), "chapters": total, "appendices": len(appendices)},
        "parts": [
            {
                "id": p["id"],
                "title": p["title"],
                "chapters": [entry(p["id"], f, f"part-{p['id']}") for f in p["chapters"]],
            }
            for p in parts
        ],
        "appendices": [entry("A", f, "appendix") for f in appendices],
    }
    return manifest, total


def toc(manifest) -> str:
    lines = []
    for p in manifest["parts"]:
        n = len(p["chapters"])
        title = p["title"]
        lines.append(f"| **Part {p['id']}** | {title[0].upper() + title[1:]} | {n} |")
    lines.append(f"| **Appendices** | Pin tables, the unknowns, the glossary, the source index | {len(manifest['appendices'])} |")
    return "\n".join(lines)


def main() -> int:
    check = "--check" in sys.argv
    manifest, total = build()
    body = json.dumps(manifest, indent=2, ensure_ascii=False) + "\n"

    if check:
        stale = []
        if not MANIFEST.exists() or MANIFEST.read_text() != body:
            stale.append("book/manifest.json")
        if README.exists() and toc(manifest) not in README.read_text():
            stale.append("book/README.md's table of contents")
        if stale:
            print("stale: " + ", ".join(stale) + " — run scripts/generate-book-manifest.py")
            return 1
        print(f"manifest current: {manifest['counts']}")
        return 0

    BOOK.mkdir(exist_ok=True)
    MANIFEST.write_text(body)
    print(f"wrote {MANIFEST.relative_to(ROOT)}: {manifest['counts']}")

    if README.exists():
        text = README.read_text()
        start, end = "<!-- toc:begin -->", "<!-- toc:end -->"
        if start in text and end in text:
            pre, rest = text.split(start, 1)
            _, post = rest.split(end, 1)
            README.write_text(f"{pre}{start}\n{toc(manifest)}\n{end}{post}")
            print("wrote the README's table of contents")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
