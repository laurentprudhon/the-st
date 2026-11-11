using System.Text;
using TheST.Stores;

namespace TheST.Harness;

/// <summary>
/// The stores' round-trip (ADR 0014 §2/§4): the pins, machine byte order, lane isolation and the two
/// fixed populations. This runs in the host layer, which is the only place allowed to touch a store
/// from outside the System Control Unit — and it holds no record and no instant.
/// </summary>
internal static class StoreChecks
{
    static int _failures;

    internal static int Run()
    {
        _failures = 0;
        Console.WriteLine("check-stores — Ram and Rom, the pins and the host's view (ADR 0014)");
        Console.WriteLine();

        RamPins();
        RamByteOrder();
        RamLaneIsolation();
        RamPopulation();
        RomStore();

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "stores PASS" : $"stores FAIL — {_failures} assertion(s)");
        return _failures == 0 ? 0 : 1;
    }

    static void RamPins()
    {
        var ram = new Ram();
        ram.Write(1, 400, 300, 0xBEEF);
        Check(ram.Read(1, 400, 300) == 0xBEEF, "a word survives a write on the same pins", "bank 1, row 400, column 300 = 0xBEEF");
        Check(ram.Read(0, 400, 300) == 0x0000, "a different bank is a different address (RAS0 vs RAS1)", "bank 0 at the same row and column reads 0");
        Check(ram.Read(1, 401, 300) == 0x0000, "a different row is a different address (MAD0–MAD9)", "row 401 reads 0");
        Check(ram.Read(1, 400, 301) == 0x0000, "a different column is a different address", "column 301 reads 0");

        ram.Refresh(511);
        Check(ram.Read(1, 400, 300) == 0xBEEF, "refresh charges a row and changes nothing observable", "the word is intact after Refresh(511)");
        Console.WriteLine();
    }

    static void RamByteOrder()
    {
        var ram = new Ram();
        ram.Write(0, 0, 0, 0x1234);

        Check(ram.ReadByte(0, 0, 0, Lane.High) == 0x12 && ram.ReadByte(0, 0, 0, Lane.Low) == 0x34,
            "the high lane is the high byte (CAS0H, U102)", "0x1234 -> high 0x12, low 0x34");

        Check(ram.MachineBytes[0] == 0x12 && ram.MachineBytes[1] == 0x34,
            "MachineBytes is machine byte order: index 2n is word n's high byte",
            $"span[0] = 0x{ram.MachineBytes[0]:X2}, span[1] = 0x{ram.MachineBytes[1]:X2}");

        // The host's view is the provisioning path: it writes the span, the pins read it back.
        ram.MachineBytes[2] = 0xAB;
        ram.MachineBytes[3] = 0xCD;
        Check(ram.Read(0, 0, 1) == 0xABCD, "a host write through the span is what the pins read", "span[2..3] = 0xAB,0xCD -> Read = 0xABCD");
        Console.WriteLine();
    }

    static void RamLaneIsolation()
    {
        var ram = new Ram();
        ram.Write(0, 7, 7, 0xFFFF);
        ram.WriteByte(0, 7, 7, Lane.High, 0x00);

        Check(ram.Read(0, 7, 7) == 0x00FF, "a one-byte write leaves the other lane alone (CASnH is one strobe)", "0xFFFF with the high lane zeroed reads 0x00FF");

        ram.WriteByte(1, 7, 7, Lane.Low, 0x5A);
        Check(ram.ReadByte(1, 7, 7, Lane.Low) == 0x5A && ram.ReadByte(1, 7, 7, Lane.High) == 0x00,
            "the lane parameter is a strobe, not a mask applied to a whole word", "low lane 0x5A, high lane untouched");
        Console.WriteLine();
    }

    static void RamPopulation()
    {
        var ram = new Ram();
        long bytes = (long)Ram.Banks * Ram.Rows * Ram.Columns * 2;

        Check(Ram.Banks == 2 && Ram.Rows == 512 && Ram.Columns == 512, "two banks of 512 rows × 512 columns × 16 bits",
            $"{Ram.Banks} banks × {Ram.Rows} rows × {Ram.Columns} columns");
        Check(bytes == 1024 * 1024, "1 MB, the machine's one population (no size switch)", $"{Program.Number(bytes)} bytes");
        Check(ram.MachineBytes.Length == bytes, "the host's span covers the whole store", $"{Program.Number(ram.MachineBytes.Length)} bytes");
        Console.WriteLine();
    }

    static void RomStore()
    {
        var rom = new Rom();
        byte[] image = Encoding.ASCII.GetBytes("TOS");
        image.CopyTo(rom.MachineBytes);

        Check(rom.Read(0) == 0x544F, "Rom reads big-endian pairs through a flat address (A0–A16)", "0x54 0x4F -> 0x544F");
        Check(rom.ReadByte(0, Lane.High) == 0x54 && rom.ReadByte(0, Lane.Low) == 0x4F,
            "U102 is the high byte lane and U103 the low one", "high 0x54, low 0x4F");
        Check(Rom.Words == 128 * 1024 && rom.MachineBytes.Length == 256 * 1024,
            "two 27C010s: 128K words = 256 KB", $"{Program.Number(Rom.Words)} words, {Program.Number(rom.MachineBytes.Length)} bytes");
    }

    static void Check(bool condition, string assertion, string measured)
    {
        Console.WriteLine($"  {(condition ? "ok  " : "FAIL")}  {assertion}");
        Console.WriteLine($"        {measured}");

        if (!condition)
        {
            _failures++;
        }
    }
}
