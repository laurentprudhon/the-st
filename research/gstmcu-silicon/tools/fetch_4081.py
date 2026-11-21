#!/usr/bin/env python3
"""Re-create the ST-4081 (GSTMCU) evidence set from the recovered Atari CAD archive.

Downloads Zietz's ASICs-new.zip, extracts the 4081_COMBO tree, and dumps the text
layer of ST4081S.PDF so the sheets can be grepped. No Atari CAD content is written
into the repository — this script only fetches, and the work directory is disposable.

    python3 fetch_4081.py --work /opt/data/cache/gstmcu-4081
    grep -n "MEMORY ADDR MUX" $WORK/4081_COMBO/4081/DWG/pdf/st4081s.txt

Requires pymupdf for the text dump (pip install pymupdf, or --no-pdf to skip).
The ZIP's entry names mix '\\' and '/', which makes zipfile raise BadZipFile until
the name is normalised — that is the one trick this script exists to encode.
"""
import argparse, os, sys, urllib.request, zipfile

URL = 'https://www.chzsoft.de/asic-web/ASICs-new.zip'
PREFIX = 'Atari_ASICs/ASIC/4081_COMBO'


def fetch(work, no_pdf):
    os.makedirs(work, exist_ok=True)
    zpath = os.path.join(work, 'ASICs-new.zip')
    if not os.path.exists(zpath):
        print(f'downloading {URL}')
        urllib.request.urlretrieve(URL, zpath)
    print('zip:', os.path.getsize(zpath), 'bytes')

    n = 0
    with zipfile.ZipFile(zpath) as z:
        for info in z.infolist():
            fwd = info.filename.replace('\\', '/')
            info.filename = info.orig_filename = fwd          # the backslash fix
            if PREFIX not in fwd:
                continue
            dest = os.path.join(work, fwd[len('Atari_ASICs/ASIC/'):])
            if fwd.endswith('/'):
                os.makedirs(dest, exist_ok=True)
                continue
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            with z.open(info) as src, open(dest, 'wb') as out:
                out.write(src.read())
            n += 1
    print('extracted', n, 'files under', os.path.join(work, '4081_COMBO'))

    if no_pdf:
        return
    try:
        import pymupdf
    except ImportError:
        print('pymupdf missing: text layer not dumped (pip install pymupdf)', file=sys.stderr)
        return
    pdf = os.path.join(work, '4081_COMBO/4081/DWG/pdf/ST4081S.PDF')
    doc = pymupdf.open(pdf)
    chunks = []
    for i, page in enumerate(doc, 1):
        chunks.append(f'\n===== PAGE {i} =====\n' + page.get_text())
    out = os.path.join(work, '4081_COMBO/st4081s.txt')
    open(out, 'w').write(''.join(chunks))
    print('text layer:', doc.page_count, 'pages ->', out)


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--work', default='/opt/data/cache/gstmcu-4081')
    ap.add_argument('--no-pdf', action='store_true')
    a = ap.parse_args()
    fetch(a.work, a.no_pdf)
