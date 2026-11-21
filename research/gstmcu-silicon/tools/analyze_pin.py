#!/usr/bin/env python3
"""Parse the recovered P-CAD PINLIST netlists of the Atari ST-4081 (GSTMCU).

Format:
    (SYM,<id>
    DATA,2,<pad/designator>
    DATA,3,<cell type>
    PIN,<pin name>,<net>,<sheet>[,<row>,<col>,<dir>]
    )

Two netlists live in the archive and they are DIFFERENT REVISIONS IN DIFFERENT
LIBRARIES — never add or compare their counts:
    4081.ORG/PIN/TA4081.PIN   logic cells  (NA2, NO2, FF2 ...)  ~1615 components
    4081/PIN/4081.PIN         drawing symbols (MX41, AA4, LLR ...) ~1209 components

Usage:
    python3 analyze_pin.py <netlist.PIN> [--nets REF,ADDRSEL,M2CLOCK] [--cells]
    python3 analyze_pin.py <netlist.PIN> --census     # cell counts + transistor mix
    python3 analyze_pin.py <netlist.PIN> --net REF    # every pin on one net

Limits, learned the hard way: the export covers sheet references 2..29 of a 33-sheet
set and the data bus stops at D9, so treat results as a CENSUS, not connectivity.
"""
import argparse, collections, sys


def parse(path):
    txt = open(path, 'rb').read().decode('latin-1')
    insts = []
    for b in txt.split('(SYM,')[1:]:
        pad = cell = None
        pins = []
        for line in (l.strip() for l in b.splitlines()):
            if not line:
                continue
            if line.startswith('DATA,2,'):
                pad = line[7:]
            elif line.startswith('DATA,3,'):
                cell = line[7:]
            elif line.startswith('PIN,'):
                parts = line.split(',')
                name = parts[1] if len(parts) > 1 else ''
                net = parts[2] if len(parts) > 2 else ''
                sheet = parts[3] if len(parts) > 3 else ''
                pins.append((name, net, sheet))
        insts.append(dict(pad=pad, cell=cell, pins=pins))
    return insts


def census(insts, gate_doc=None):
    cells = collections.Counter(i['cell'] for i in insts)
    nets = collections.defaultdict(set)
    for i in insts:
        for _, net, _ in i['pins']:
            if net:
                nets[net].add(i['pad'])
    print('components:', len(insts), ' distinct cell types:', len(cells))
    print('nets:', len(nets), ' named:', sum(1 for k in nets if not k.startswith('***')))
    print('cells:', ', '.join(f'{c}={n}' for c, n in cells.most_common()))
    if gate_doc:
        import re
        per = {}
        for line in open(gate_doc, 'rb').read().decode('latin-1').splitlines():
            m = re.match(r'^(\w+)\s+(\d+)\s+(\d+)\s+(\d+)\s*$', line.strip())
            if m:
                per[m.group(1)] = (int(m.group(2)), int(m.group(3)), int(m.group(4)))
        total = sum(n * per[c][1] for c, n in cells.items() if c in per)
        print('transistors implied by the gate list, for these counts:', total)
        print('cells absent from the gate list:', [c for c in cells if c not in per])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('pin')
    ap.add_argument('--census', action='store_true')
    ap.add_argument('--gate-doc')
    ap.add_argument('--net')
    a = ap.parse_args()

    insts = parse(a.pin)
    if a.net:
        hits = [(i['pad'], i['cell']) for i in insts for _, n, _ in i['pins'] if n == a.net]
        print(f'{a.net}: {len(hits)} pins ->', collections.Counter(c for _, c in hits))
        print('pads:', [p for p, _ in hits])
        return
    if a.census or not a.net:
        census(insts, a.gate_doc)


if __name__ == '__main__':
    main()
