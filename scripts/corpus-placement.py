#!/usr/bin/env python3
"""How far do CLK and MAME agree, shape by shape, on (a) the total clocks and (b) the skeletons?

Skeletons, not totals, are what the core needs: they say *where* in the instruction the internal
clocks sit. Two independent T3 models agreeing on both is the bar this page is about.
"""
import collections, glob, gzip, json, os, re

CLK = '/opt/data/the-st/tests/680x0/full'
MAME = '/opt/data/cache/scratch/m68000/v1'
PATS = [('Dn', r'D\d'), ('An', r'A\d'), ('(An)', r'\(A\d\)'), ('(An)+', r'\(A\d\)\+'), ('-(An)', r'-\(A\d\)'),
        ('(d16,An)', r'\(d16, A\d\)'), ('(d8,An,Xn)', r'\(d8, A\d, Xn\)'), ('(d16,PC)', r'\(d16, PC\)'),
        ('(d8,PC,Xn)', r'\(d8, PC, Xn\)'), ('(xxx).w', r'\(xxx\)\.w'), ('(xxx).l', r'\(xxx\)\.l'),
        ('#imm', r'#'), ('Q', r'Q')]


def mode(t):
    for l, p in PATS:
        if re.fullmatch(p, t):
            return l
    return t


def split_operands(text):
    out, depth, current = [], 0, ''
    for ch in text:
        if ch == '(':
            depth += 1
        if ch == ')':
            depth -= 1
        if ch == ',' and depth == 0:
            out.append(current.strip()); current = ''
        else:
            current += ch
    if current.strip():
        out.append(current.strip())
    return out


CLK_NAME = re.compile(r'^[0-9a-f]+ \[(.+?)\](?: \d+)?$')
MAME_NAME = re.compile(r'^\d+ (.+?) [0-9a-f]+$')


def shape(name, corpus):
    m = (CLK_NAME if corpus == 'clk' else MAME_NAME).match(name)
    if not m:
        return None
    body = m.group(1)
    mn, _, rest = body.partition(' ')
    return mn, tuple(mode(o) for o in split_operands(rest)) if rest else ()


def skeleton(tr):
    out = []
    for e in tr:
        if e[0] in ('re', 'we'):
            return None
        out.append(f'n{e[1]}' if e[0] == 'n' else (f't{e[1]}' if e[0] == 't' else e[0]))
    return ' '.join(out)


def faults(tr):
    if any(t[0] in ('re', 'we') for t in tr):
        return True
    return any(t[0] == 'r' and len(t) > 3 and t[2] == 5 and t[3] < 0x40 for t in tr)


def view(corpus, name):
    path = os.path.join(CLK if corpus == 'clk' else MAME, name + ('.json.gz' if corpus == 'clk' else '.json'))
    if not os.path.exists(path):
        return None
    records = json.load(gzip.open(path)) if corpus == 'clk' else json.load(open(path))
    out = collections.defaultdict(set)
    for r in records:
        tr = r['transactions']
        if faults(tr):
            continue
        s = shape(r['name'], corpus)
        sk = skeleton(tr)
        if s is None or sk is None:
            continue
        out[s].add((r['length'], sk))
    return out


families = sorted(os.path.basename(p)[:-8] for p in glob.glob(CLK + '/*.json.gz'))
shared = shapes = same = differ_total = differ_skeleton = 0
rows = []
for family in families:
    mame = view('mame', family)
    if not mame:
        continue
    clk = view('clk', family)
    common = set(clk) & set(mame)
    if not common:
        continue
    shared += 1
    ok = bad_total = bad_skel = 0
    for s in common:
        shapes += 1
        ct = {t for t, _ in clk[s]}
        mt = {t for t, _ in mame[s]}
        cs = {k for _, k in clk[s]}
        ms = {k for _, k in mame[s]}
        if ct != mt:
            bad_total += 1
        if cs != ms:
            bad_skel += 1
        if ct == mt and cs == ms:
            ok += 1
    same += ok
    differ_total += bad_total
    differ_skeleton += bad_skel
    rows.append((family, len(common), ok, bad_total, bad_skel))

print(f'families compared {shared}   shapes compared {shapes}')
print(f'  identical (total AND skeleton) {same}  ({100*same/max(1,shapes):.1f} %)')
print(f'  total differs {differ_total}   skeleton differs {differ_skeleton}')
print()
for family, n, ok, bt, bs in rows:
    if ok != n:
        print(f'  {family:<12} {ok}/{n} identical   total≠{bt}  skeleton≠{bs}')
