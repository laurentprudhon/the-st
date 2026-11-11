// ============================================================================================
// The ST — the stores (Ram, Rom), structural sketch (ticket #31). NOT the implementation, but it
// does compile as written (checked 2026-11-11 against .NET 11.0.100-rc.3: 0 warnings, 0 errors).
// The prose that goes with it is design/system-control-unit.md §2; the ruling is ADR 0014.
//
// The interface is the pins and nothing else. The owner's constraint, verbatim: "the C# interface
// of the ram and rom data structures should reflect as much as possible the real pins (row /
// columns) but not the timing, all folded in a single call." No cycle cost, no latch state, no
// strobe sequence, no record, no subscription, no wake: the instants are the System Control
// Unit's slot grid and never the store's.
//
// Row and column are computed by the controller, which owns MAD0–MAD9 and the address latch. The
// store receives two indices and does no address arithmetic — that is the whole boundary.
// ============================================================================================

namespace TheST.Stores;

// One byte lane of a 16-bit word: CASnH / CASnL on the DRAMs, U102 / U103 on the ROM pair.
public enum Lane : byte { High = 0, Low = 1 }

// ------------------------------------------------------------------------------------------
// Ram — 2 banks x 512 rows x 512 columns x 16 bits = 1 MB, the machine's one population.
//   RAS0 (U400.127) / RAS1 (U400.124)  -> bank
//   MAD0–MAD9                          -> row, latched on RAS; then column, latched on CAS
//   CAS0H/CAS0L, CAS1H/CAS1L           -> the byte lane inside the bank
//   WE (U400.121)                      -> read or write
// This population is fixed (one machine, no switches): 16 x 256Kx1 (41256-class, 512x512) per
// bank, two banks populated.
// ------------------------------------------------------------------------------------------
public sealed class Ram
{
    public const int Banks = 2;
    public const int Rows = 512;
    public const int Columns = 512;

    // Machine byte order — big-endian pairs, so index 2*n is the high byte of word n. That is
    // what makes MachineBytes exact and zero-copy instead of a byte swap on every read.
    private readonly byte[] _bytes = new byte[Banks * Rows * Columns * 2];

    // ---- the pins: one call in, one value out ------------------------------------------
    public ushort Read(int bank, int row, int column)                       // CASnH + CASnL, WE high
        => (ushort)((_bytes[Index(bank, row, column)] << 8) | _bytes[Index(bank, row, column) + 1]);

    public void Write(int bank, int row, int column, ushort word)           // both lanes, WE low
    {
        var i = Index(bank, row, column);
        _bytes[i] = (byte)(word >> 8);
        _bytes[i + 1] = (byte)word;
    }

    public byte ReadByte(int bank, int row, int column, Lane lane)          // one CAS strobe
        => _bytes[Index(bank, row, column) + (int)lane];

    public void WriteByte(int bank, int row, int column, Lane lane, byte value)
        => _bytes[Index(bank, row, column) + (int)lane] = value;

    // The store's half of refresh: the row the controller's counter names. One row address
    // strobed on both RAS lines, so both banks are refreshed by the same call. The 7.8 us
    // period, the counter and the slot it lands in are the controller's (ADR 0011 §5).
    public void Refresh(int row) { /* rows are charged; nothing observable happens here */ }

    // ---- the host's view, and nothing else's -------------------------------------------
    // Display, debugging, side-loading. Machine byte order, no records, no instants: written
    // only while the machine is stopped (ADR 0014 §4). No chip may hold this.
    public Span<byte> MachineBytes => _bytes;

    private int Index(int bank, int row, int column) => ((bank * Rows + row) * Columns + column) * 2;
}

// ------------------------------------------------------------------------------------------
// Rom — U102 (high byte lane) + U103 (low byte lane), 2 x 27C010: A0–A16, no multiplexing, no
// strobes, no WE. 128K words of 16 bits = 256 KB, selected by ROM0–ROM6 out of the decode.
// Its content arrives from the provisioning path (ADR 0010), never from a hard-coded image.
// ------------------------------------------------------------------------------------------
public sealed class Rom
{
    public const int Words = 128 * 1024;

    private readonly byte[] _bytes = new byte[Words * 2];

    public ushort Read(int address) => (ushort)((_bytes[address * 2] << 8) | _bytes[address * 2 + 1]);
    public byte ReadByte(int address, Lane lane) => _bytes[address * 2 + (int)lane];

    // Write path for provisioning and fixtures only; the chip-facing side is read-only.
    public Span<byte> MachineBytes => _bytes;
}
