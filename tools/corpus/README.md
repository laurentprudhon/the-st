# Corpus tooling — moved

The corpus machinery now lives in [`laurentprudhon/the-st-software`](https://github.com/laurentprudhon/the-st-software/tree/main/tools):

| was here | now |
|---|---|
| `tools/corpus/setup.sh` | `tools/setup.sh` |
| `tools/corpus/sizing/` | `tools/sizing/` |
| — (new, #53) | `tools/census/` |
| — (new, #64a) | `tools/golden/` + `data/golden/` |

Corpus code is no longer developed in this repository. The emulator's map (#51) and its tickets stay
here; the catalogue and its tooling do not.
