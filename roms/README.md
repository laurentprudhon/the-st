# roms/

Data about the operating-system images the machine boots. **No Atari ROM is ever stored here** — not
an image, not a slice of one, not a container layer, not a test fixture.

| File | What it is |
|---|---|
| `emutos-1.4-manifest.json` | The 18 PAL EmuTOS 1.4 images the project ships: file name, size, SHA-256, CRC32, language. `etos256us.img` is listed under `excluded` — it is the NTSC image and the machine is PAL. |
| `tos-hashes.json` | The accept-list for a **user-supplied** Atari TOS 1.62 image: 13 PAL entries each with its provenance, the refused US entry, the structural pre-filter and the header country-code table. Unknown hashes are refused, never booted. |

The images themselves arrive with the packaging work in the walking-skeleton ticket (#10), together
with `emutos-src-1.4.tar.gz` (1 299 705 B) and `LICENSE.txt` (GPLv2). Until then these two files are
the decision's data: they are facts about the images, not the images.

Policy: [`docs/adr/0010-tos-image-provisioning.md`](../docs/adr/0010-tos-image-provisioning.md).
Evidence: [`research/tos-rom-sourcing.md`](../research/tos-rom-sourcing.md).
