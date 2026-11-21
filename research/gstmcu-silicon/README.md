# `research/gstmcu-silicon/` — the U400 GSTMCU evidence kit

The study is [`../gstmcu-silicon-implementation.md`](../gstmcu-silicon-implementation.md). This directory
holds only the **tooling** needed to work from the recovered Atari design; no Atari CAD content is
committed here (licence: see the study's §9).

## Why there are no images

No die photograph of the GSTMCU (or of any STE custom) exists in public. The primary source is instead
Christian Zietz's recovery of Atari's own CAD: `ASICs-new.zip`, containing three revisions of the chip
(`TA-4081`, `ST-4081`, `TXL`) as schematics, netlists, a gate-count list, simulator files and one design
memo. Those files are Atari's proprietary material, published for research use — download them, never
commit them.

## Rebuilding the evidence set

```bash
python3 tools/fetch_4081.py --work /opt/data/cache/gstmcu-4081     # ~15 MB, extracts 326 files
grep -n "MEMORY ADDR MUX\|SOUND ADDRESS" /opt/data/cache/gstmcu-4081/4081_COMBO/st4081s.txt
```

`fetch_4081.py` also dumps the PDF text layer to `st4081s.txt` (49 pages), which is what makes the sheets
greppable. It needs `pymupdf` for that step only.

## The three tools

| tool | what it does |
|---|---|
| `fetch_4081.py` | downloads, extracts (`4081_COMBO` only, with the backslash-zip fix), dumps the PDF text |
| `analyze_pin.py` | parses a P-CAD `PINLIST` netlist: cell census, net census, every pin on one net, transistor total against the gate list |
| `render_sheet.py` | renders one sheet (optionally a fractional clip) to PNG so a human can read connections the text layer does not carry |

## Two things the tools will not do

- **They do not prove connectivity.** The netlist export stops at sheet 29 of 33 and its data bus stops at
  `D9`; treat every result as a census, and check a connection against the drawing before claiming it.
- **They cannot tell you where a block sits.** Without die geometry there is no floorplan to be had, and a
  diagram invented from part counts is worse than none.
