#!/usr/bin/env python3
"""Zip-interior census (ticket #53): what containers a host's ZIPs actually hold.

Fujiology serves `.zip` for 99.8 % of its ST tree, so the served extension says nothing about the
container inside. The rules for the one discovery pass forbid downloading an archive to list it, so this
reads the **central directory by HTTP Range**: a HEAD for the size, the last 64 KiB for the end-of-central-
directory record, then a range covering the central directory itself.

One connection per host, >= 1 s between requests, honest User-Agent, honour 429/503 with a long backoff.

    python3 zipdir.py --manifest fujiology21.md5 --base http://ftp.untergrund.net/users/ltk_tscc/fujiology --sample 30
"""

from __future__ import annotations

import argparse
import collections
import http.client
import json
import os
import random
import re
import struct
import sys
import time
import urllib.error
import urllib.request

UA = "TheST-corpus-research/0.1 (+https://github.com/laurentprudhon/the-st)"
LINE = re.compile(r"^(?P<md5>[0-9a-f]{32}) \*(?P<path>.+)$")
SIG_EOCD = b"PK\x05\x06"
SIG_CEN = b"PK\x01\x02"


def request(url: str, headers: dict[str, str] | None = None) -> http.client.HTTPResponse:
    req = urllib.request.Request(url, headers={"User-Agent": UA, **(headers or {})})
    for attempt in range(4):
        try:
            return urllib.request.urlopen(req, timeout=45)
        except urllib.error.HTTPError as exc:
            if exc.code in (429, 503):
                wait = 30 * (attempt + 1)
                print(f"# {exc.code} on {url}; backing off {wait}s", file=sys.stderr)
                time.sleep(wait)
                continue
            raise
    raise RuntimeError(f"gave up on {url}")


def head_size(url: str) -> int:
    with request(url, {"Range": "bytes=0-0"}) as resp:
        content_range = resp.headers.get("Content-Range", "")
        if "/" in content_range:
            return int(content_range.rsplit("/", 1)[1])
        length = resp.headers.get("Content-Length")
        if length is None:
            raise RuntimeError(f"no size for {url}")
        return int(length)


def fetch_range(url: str, start: int, end: int) -> bytes:
    with request(url, {"Range": f"bytes={start}-{end}"}) as resp:
        return resp.read()


def central_directory(url: str, size: int) -> list[str]:
    """File names from the zip central directory, read by range, without the archive body."""
    tail_len = min(size, 96 * 1024)
    tail = fetch_range(url, size - tail_len, size - 1)
    pos = tail.rfind(SIG_EOCD)
    if pos < 0:
        raise ValueError("no end-of-central-directory record in the tail")
    cd_size, cd_offset = struct.unpack("<II", tail[pos + 12 : pos + 20])
    if cd_size == 0:
        return []
    if cd_offset >= size - tail_len:  # already inside the tail we hold
        blob = tail[cd_offset - (size - tail_len) :][:cd_size]
    else:
        blob = fetch_range(url, cd_offset, cd_offset + cd_size - 1)
    names, i = [], 0
    while True:
        i = blob.find(SIG_CEN, i)
        if i < 0:
            break
        name_len, extra_len, comment_len = struct.unpack("<HHH", blob[i + 28 : i + 34])
        name = blob[i + 46 : i + 46 + name_len].decode("cp437", errors="replace")
        names.append(name)
        i += 46 + name_len + extra_len + comment_len
    return names


def ext_of(name: str) -> str:
    return os.path.splitext(name)[1].lower() or "(none)"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--manifest", required=True)
    ap.add_argument("--base", required=True)
    ap.add_argument("--sample", type=int, default=30)
    ap.add_argument("--seed", type=int, default=53)
    ap.add_argument("--out", default="-")
    args = ap.parse_args()

    paths = []
    with open(args.manifest, encoding="utf-8", errors="replace") as fh:
        for raw in fh:
            m = LINE.match(raw.rstrip("\r\n"))
            if m and m.group("path").upper().startswith("ST\\") and m.group("path").lower().endswith(".zip"):
                paths.append(m.group("path"))

    random.seed(args.seed)
    # one per group first, so the sample spans crack groups rather than one prolific uploader
    by_group: dict[str, list[str]] = collections.defaultdict(list)
    for p in paths:
        parts = p.split("\\")
        by_group[parts[1] if len(parts) > 2 else "?"].append(p)
    picked: list[str] = []
    for group in sorted(by_group):
        picked.append(random.choice(sorted(by_group[group])))
    random.shuffle(picked)
    picked = picked[: args.sample]

    interiors: collections.Counter[str] = collections.Counter()
    rows, failures = [], []
    for n, path in enumerate(picked, 1):
        url = f"{args.base}/{path.replace(chr(92), '/')}"
        try:
            size = head_size(url)
            names = central_directory(url, size)
        except Exception as exc:  # noqa: BLE001 — a census records failures, it does not abort
            failures.append({"path": path, "url": url, "error": f"{type(exc).__name__}: {exc}"})
            print(f"# [{n}/{len(picked)}] FAIL {path} — {exc}", file=sys.stderr)
            time.sleep(1)
            continue
        exts = collections.Counter(ext_of(x) for x in names)
        interiors.update(exts)
        rows.append({"path": path, "url": url, "size": size, "entries": len(names), "exts": dict(exts)})
        print(f"# [{n}/{len(picked)}] ok {path} — {len(names)} entries", file=sys.stderr)
        time.sleep(1)

    result = {
        "source": args.base,
        "census": "zip interiors read by central-directory range; archives not downloaded",
        "sample": len(rows),
        "failures": failures,
        "interior_extensions": dict(interiors.most_common()),
        "archives": rows,
    }
    text = json.dumps(result, indent=2)
    if args.out == "-":
        print(text)
    else:
        with open(args.out, "w", encoding="utf-8") as fh:
            fh.write(text + "\n")
        print(f"# wrote {args.out} — {len(rows)} archives, {len(failures)} failures", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
