# Boot Sector, Disk Format, and File System

## 1. Atari ST Executable File Format (MZ Header)

### MZ Header Structure

```asm
Offset    Size    Field                 Description
────────    ────    ──────                ────────────────
$0000       2B      magic                 "MZ" = 0x4D5A
$0002       2B      last_page             Last page of file (in 512B)
$0004       2B      reloc_count           Number of relocation table entries
$0006       2B      hdr_size              Header size in 512B paragraphs
$0008       2B      min_mem               Minimum extra paragraphs needed
$000A       2B      max_mem               Maximum extra paragraphs needed
$000C       2B      checksum              XOR checksum of header
$000E       2B      entry_offset          Entry point offset from segment
$0010       2B      entry_seg             Entry point segment number
$0012       2B      reloc_offset          Relocation table offset
$0014       2B      code_overlay          Code overlay number
$0016       2B                                 Padding
$0018       2B                                 TOS magic = 0x4F52 (RO)
$001A       2B                                 Reserved = 0
$001C       2B      map_len_or_magic      Map length (if >0) or signature
; After MZ header, map table:
$001E       (map_len*2)   disk_checksum   Disk checksum = $1234 @ $001E
$001E+n     (map_len*2)   address_map     Address remapping table
; After map table, segment table:
$001E+m     (seg_count)   segment_table   Segment counts for each segment
; Data follows...
```

### TOS-Specific Header Fields

- **Magic at $0018**: Must be `0x4F52` ("RO") for TOS executable
- **Checksum at $001E**: Must be `0x1234` for TOS to recognize as executable on boot disk
- **Disk checksum**: TOS calculates sum of all words in boot sector + `WORD[$2B4] == $1234`

### Relocation Table

```asm
; The address remap table at offset $001E for map_len words:
; For each word in the table:
;   If high bit (bit 15) is 1:
;       Segment = word & 0x7FFF
;       The segment at base + (Segment * 16) is loaded here
;   If high bit is 0:
;       This word is not a map entry

; The relocation table at $0012 gives offset into the file
; where the relocation entries begin.

; During PEXEC loading:
;   new_base = allocated segment
;   For each address_map_entry:
;       if (entry & 0x8000):
;           seg_number = entry & 0x7FFF
;           relocation_offset = new_base + (seg_number * 16)
;           copy segment data to relocation_offset
```

## 2. Boot Sector (Track 0, Sector 1)

### Complete Boot Sector Layout

```asm
; Track 0, Sector 1 of boot disk (512 bytes)
; Loaded into memory at address $00000200

Offset    Size    Content                   Purpose
────────    ────    ───────                   ────────
$0000       3B      Jump instruction           Bootstrap jump
$0003       8B      OEM name                   "ATARI      "
$000B       2B      bytes_per_sector = 512     $200
$000D       1B      sectors_per_cluster = 1    1
$000E       2B      reserved_sectors = 1       1
$0010       2B      FAT_count = 2              2
$0012       2B      root_dir_entries = 48      48
$0014       2B      total_sectors = 2880       2880 (1.44MB)
$0016       1B      media_descriptor = $F0     $F0
$0017       2B      sectors_per_FAT = 9        9
$0019       2B      sectors_per_track = 9      9
$001B       2B      heads = 2                  2
$001D       2B      hidden_sectors = 0         0
$001F       4B      large_sectors = 0          0 (not used on ST)
$0023       3B      Boot code                  Bootstrap routine
$0026       32B     BIOS parameter block       BPB structure
$0046       3B      Extended bios parameter    EBPB
$0049       1B      drive_number = 0           drive A
$004A       1B      reserved = 0               0
$004B       1B      extended_signature = $29   0x29
$004C       4B      volume_serial_number       Random serial
$0050       11B     volume_label               "ATARI SSD09"
$005B       8B      filesystem_type            "FAT12   "
; Boot code starts here:
$005E       2B      disk_checksum_magic        $1234 (verification magic)
; From $0060 to $01FD: Boot code (routines to load DESKTOP.PRG)
$01FE       2B      boot_signature             $AA55
$01FF       1B      reserved                    0xFF (or padding)
```

## 3. FAT12 File System

### Partition Table on HDD (Boot Sector + Partition)

```asm
; HDD boot sector partition table at $01BE:
Offset    Size    Content
────────    ────    ───────
$01BE       1B      boot_indicator             $00=none, $80=bootable
$01BF       3B      start_chs                    Start CHS address
$01C2       1B      partition_type              e.g., $06=FAT16, $81=Minix
$01C5       3B      end_chs                       End CHS address
$01C8       4B      relative_sectors              LBA of partition start
$01CC       4B      total_sectors                  Total partition size

; Signature at $01FE: $AA55
; On ST floppy, partition table is NOT used (no HDD partitioning on floppy)
```

### FAT12 Directory Entry (32 bytes)

```asm
Offset    Size    Field                         Description
────────    ────    ──────                        ────────────
$0000       8B      filename                     "FILENAME "
$0008       3B      extension                    "EXT  "
$000B       1B      attributes                     File attributes
; Attribute bits:
;   Bit 0:   Read-only    (0x01)
;   Bit 1:   Hidden       (0x02)
;   Bit 2:   System file  (0x04)
;   Bit 3:   Volume label (0x08)
;   Bit 4:   Subdirectory (0x10)
;   Bit 5:   Archive      (0x20)
;   Bit 6:   Device       (0x40) (reserved)
;   Bit 7:   Reserved     (0x80) (reserved)
$000C       1B      reserved                       0 (always 0)
$000D       1B      create_time_seconds             Create time (seconds / 2)
$000E       2B      create_time                    Create time (h:m:s x100)
$0010       2B      create_date                    Create date (y:y12:m:d)
$0012       2B      last_access                      Last access date
$0014       2B      high_cluster                      High 12 bits of cluster
$0016       2B      write_time                       Write time
$0018       2B      write_date                       Write date
$001A       2B      low_cluster                       Low 16 bits of cluster
$001C       4B      file_size                        File size in bytes
```

### FAT12 Cluster Chain

```asm
; FAT12 entries are packed: 1.5 bytes per entry
; Entry for cluster N:
;   offset = N + (N / 2)
;   if N is even:
;       entry = WORD[fat_start + offset] & 0x0FFF
;   else:
;       entry = WORD[fat_start + offset] >> 4

; FAT12 Special Values:
;   $000 = Free cluster
;   $001 = Reserved
;   $FF0-$FFF = Bad cluster
;   $FF8-$FFF = Last cluster in chain (end-of-file marker)

; Chain traversal example (load DESKTOP.PRG):
;   cluster = DIR_ENTRY.cluster (from directory entry)
;   while cluster < $FF8:
;       read sector containing cluster to buffer
;       cluster = next_cluster(cluster)
```

## 4. Standard ST Format Parameters

### Default 720 KB Format (ST/STe)

```
Tracks:          80
Sectors/track    9
Sectors/FAT:     9
Root entries:    48
Clusters:        2920 (80 * 9 * 2 - 2 FATS - 1 root dir)
Cluster size:    1 sector (512 bytes)
Media:           $F0 (floppy)
First data track: 1
First FAT:       1
First root:      10
First cluster:   2
Free clusters:   2907 (after DESKTOP.PRG + AUTO)
```

### Variable Format Parameters

TOS supports custom format parameters via BIOS `GETBVB` function:

```
Sectors/track:   1-33 (depends on disk density and gap)
Track:           40-80 (determined by media)
Sector size:     128, 256, 512 bytes
```

> ### Correction — 2026-11-15 (checked against Hatari's own readers and the Pasti format)
>
> The container descriptions in §5 are **wrong in ways that matter for the disk subsystem**, and this note is
> the one the disk design would cite. Corrected here rather than silently rewritten:
>
> | format | what this note said | what it is |
> |---|---|---|
> | `.ST` | "each track stored as raw MFM-encoded sector data, includes IAM/IDAM/sync bytes" | a **raw sector dump**: 512-byte sectors concatenated, no MFM, no sync bytes, no headers. Size = sectors × tracks × sides × 512 (720 KB at 9 spt, 800 KB at 10, 880 KB at 11). The MFM layer is *not* in the file. |
> | `.MSA` | "Monochrome MFM, 16 sectors/track, 4 MB" | **Magic Shadow Archiver**: a *compressed* sector image — a small header (sectors/track, sides, first track) then per-track RLE-compressed sector data. Typically 9–11 spt, 720–880 KB uncompressed. "Monochrome" and "4 MB" are both wrong. |
> | `.DIM` | "double-image MFM, side-by-side" | a raw sector image with a small geometry header (sides, tracks, sectors/track) — the same sector data as `.ST` plus the geometry that `.ST` lacks. |
> | `.STX` | "XBIOS raw sector image, 2-byte sector count then raw sectors" | **Pasti**: a *track-level* container. Per track it stores a descriptor and per-sector records with their IDs, CRCs, timing and **fuzzy-bit masks** — i.e. what the WD1772 sees, not a flat sector dump. Decoded by Hatari's own `src/floppies/stx.c` (2,134 lines, no external library). |
>
> The three levels that actually matter, and which the disk design must choose between:
> **sector** (`.st`, `.msa`, `.dim` — a byte array, no track model) · **track** (`.stx` — sectors plus their
> quirks) · **flux** (`.ipf`, `.scp`, `.hfe` — bit cells, needing an MFM stream and a bit-cell reader).
> Hatari carries both IPF paths: `ipf_caps.c` delegates to the third-party `capsimage` library, and
> `ipf_dsk.c` (MAME-derived, BSD-3-Clause) decodes IPF natively into **per-track flux transition lists** fed
> through the same MFM stream path as SCP and Kryoflux.

## 5. .ST File Format (Floppy Image)

### .ST Image Layout

```
Offset    Size      Content
────────    ────      ───────
$0000       512B      Track 0 (IDAM + all sectors + IAM + GAP)
$0200       512B      Track 1
$0400       512B      Track 2
...     (variable)
$0000       512B      Track 79
```

- Each track is stored as raw MFM-encoded sector data
- Includes IAM sector (sync bytes A1A1A1A1)
- Includes IDAM (ID address mark FF FE)
- Includes data address marks (FF F8 for sector data)

### .MSA Format (Monochrome MFM)

```
Offset    Size      Content
────────    ────      ───────
$0000       512B      Track 0 (first side)
$0200       512B      Track 1
...         (512 tracks for 2 sides x 80 tracks x 16 sectors)
$40000      512B      Track 80 (second side)
```

- 16 sectors per track (vs 9 for double-density)
- Used for MFM-formatted drives at 1Mb/s (double-density, MFM encoding)
- 80 tracks x 16 sectors x 512 bytes x 2 sides = 4 MB

### .DIM Format (Double-image MFM)

```
Offset    Size      Content
────────    ────      ───────
$0000       512B      Track 0, side 0
$0200       512B      Track 1, side 0
...         (80 x 2 = 160 tracks)
$0000       512B      Track 0, side 1
$0200       512B      Track 1, side 1
...         (80 x 2 = 160 tracks)
```

- Side-by-side format: both sides stored sequentially
- Used by copying utilities to preserve exact disk layout

### .STX Format (XBIOS Raw Sector Image)

```
Offset    Size      Content
────────    ────      ───────
$0000       2B      Sector count x 512
$0002       ?B      Raw sector data x N (each sector = 512 bytes)
```

- Sector-by-sector raw data
- Used by HxC, SD MFD for precise control

## 6. .PRG File Format (Program Image)

### .PRG Structure

```
Offset    Size      Content
────────    ────      ───────
$0000       2B      MZ magic (0x4D5A)
$0002       2B      Pages in last block
$0004       2B      Relocation entries
$0006       2B      Header size (in 16-bit words)
$0008       2B      Min extra paragraphs
$000A       2B      Max extra paragraphs
$000C       2B      Checksum
$000E       2B      Entry point offset
$0010       2B      Entry point segment
$0012       2B      Relocation table offset
$0014       2B      Overlay number
$0016       2B      Reserved = 0
$0018       2B      TOS magic = 0x4F52 ("RO")
$001A       2B      Disk checksum = $1234
$001C       (N)     Address map (relocation table)
$001E       (N)     Segment map
; Data follows immediately after segment map
```

### File Suffixes

| Suffix | Type | Extension |
|--------|------|-----------|
| .PRG | Program (load & execute) | Standard |
| .TOS | TOS executable (same as .PRG) | Standard |
| .MSA | MFM floppy image | MFM double-density |
| .DIM | Double-density image | Both sides |
| .ST  | Single-track image | MFM double-density |
| .STX | XBIOS raw image | Sector by sector |
| .GEM | GEM PIS (program info sheet) | Icon bitmap |
| .RSC | Resource file | Fonts, icons, dialogs |
| .INF | Configuration file | Desktop / system settings |
| .FNT | Font file | GDOS font definition |
| .DRV | Device driver | Installed at boot |
| .PAC | Packed file | Compression format |
| .ZIP | ZIP archive | Compression (TOS 3+, HiSAT) |
| .SHP | PIS bitmap | Icon shape |
| .DAT | Data file | Generic data |
