#!/usr/bin/env python3
"""Fujiology census (ticket #53): what the host actually serves, counted from its own MD5 manifest.

Input is `fujiology21.md5` (82,244 lines, `MD5 *PATH` with Windows separators). Nothing here touches
the network: the manifest is the host's own statement of what exists, so a count derived from it is
`counted (manifest, <date>)` and re-derivable by anyone who fetches the same file.

    python3 fujiology.py ~/corpus-cache/fujiology21.md5
"""

from __future__ import annotations

import collections
import os
import re
import sys

LINE = re.compile(r"^(?P<md5>[0-9a-f]{32}) \*(?P<path>.+)$")


def parse(path: str) -> list[str]:
    paths, skipped = [], 0
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for raw in fh:
            m = LINE.match(raw.rstrip("\r\n"))
            if m:
                paths.append(m.group("path"))
            elif raw.strip():
                skipped += 1
    if skipped:
        print(f"# {skipped} unparsable lines skipped", file=sys.stderr)
    return paths


def tree_of(path: str) -> str:
    """Top-level tree of a manifest path: 'ST', '8BIT', ..."""
    return path.split("\\", 1)[0]


def ext_of(path: str) -> str:
    base = os.path.basename(path.replace("\\", "/"))
    return os.path.splitext(base)[1].lower() or "(none)"


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    manifest = sys.argv[1]
    paths = parse(manifest)

    print(f"# manifest: {os.path.basename(manifest)} — {len(paths)} files with a path")
    trees = collections.Counter(tree_of(p) for p in paths)
    print("\n## files per top-level tree (all platforms; only `ST` is ours)\n")
    for tree, n in trees.most_common():
        mark = "  <-- ST" if tree.upper() == "ST" else ""
        print(f"{tree or '(root)'}\t{n}{mark}")

    st = [p for p in paths if tree_of(p).upper() == "ST"]
    print(f"\n## ST tree — {len(st)} files, by served extension\n")
    exts = collections.Counter(ext_of(p) for p in st)
    for ext, n in exts.most_common():
        share = 100.0 * n / len(st) if st else 0.0
        print(f"{ext}\t{n}\t{share:.1f}%")

    groups = collections.Counter(p.split("\\")[1] for p in st if p.count("\\") >= 2)
    print(f"\n## ST tree — {len(groups)} groups (second path segment), top 20\n")
    for g, n in groups.most_common(20):
        print(f"{g}\t{n}")
    print(f"\n# distinct groups: {len(groups)}, distinct extensions: {len(exts)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
