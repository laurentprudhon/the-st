// The boot's own bus cycles, read back from the capture: what the CPU asserted and what the machine
// answered, byte for byte — so the replay cannot be a different workload by accident.

using System.Globalization;

namespace TheST.Machine;

public sealed class BusTrace
{
    public readonly uint[] Address;
    public readonly byte[] Size;
    public readonly bool[] IsWrite;
    public readonly bool[] IsCpuSpace;
    public readonly ushort[] Control;
    public readonly ushort[] WriteData;
    public readonly ushort[] Answer;
    public readonly ushort[] Md;
    public readonly long[] Asserted;
    public readonly long[] AnswerInstant;

    public readonly long[] IplInstant;
    public readonly ushort[] IplValue;

    public int IplCount => IplInstant.Length;

    public int Count => Address.Length;

    BusTrace(uint[] address, byte[] size, bool[] isWrite, bool[] isCpuSpace, ushort[] control, ushort[] writeData,
        ushort[] answer, ushort[] md, long[] asserted, long[] answerInstant, long[] iplInstant, ushort[] iplValue)
    {
        Address = address;
        Size = size;
        IsWrite = isWrite;
        IsCpuSpace = isCpuSpace;
        Control = control;
        WriteData = writeData;
        Answer = answer;
        Md = md;
        Asserted = asserted;
        AnswerInstant = answerInstant;
        IplInstant = iplInstant;
        IplValue = iplValue;
    }

    /// <summary>Reads <c>cycles.bin</c> and <c>ipl.txt</c>: 32 bytes a cycle, little-endian, the layout
    /// <c>DissectCapture</c> wrote.</summary>
    public static BusTrace Load(string directory, int skip = 0, int take = int.MaxValue)
    {
        byte[] raw = File.ReadAllBytes(Path.Combine(directory, "cycles.bin"));
        int total = raw.Length / 32;
        int count = Math.Min(take, total - skip);
        var address = new uint[count];
        var size = new byte[count];
        var isWrite = new bool[count];
        var isCpuSpace = new bool[count];
        var control = new ushort[count];
        var writeData = new ushort[count];
        var answer = new ushort[count];
        var md = new ushort[count];
        var asserted = new long[count];
        var answerInstant = new long[count];

        for (int i = 0; i < count; i++)
        {
            int at = (i + skip) * 32;
            address[i] = BitConverter.ToUInt32(raw, at);
            size[i] = raw[at + 4];
            byte flags = raw[at + 5];
            isWrite[i] = (flags & 1) != 0;
            isCpuSpace[i] = (flags & 2) != 0;
            control[i] = BitConverter.ToUInt16(raw, at + 6);
            writeData[i] = BitConverter.ToUInt16(raw, at + 8);
            answer[i] = BitConverter.ToUInt16(raw, at + 10);
            md[i] = BitConverter.ToUInt16(raw, at + 12);
            asserted[i] = BitConverter.ToInt64(raw, at + 16);
            answerInstant[i] = BitConverter.ToInt64(raw, at + 24);
        }

        var iplLines = File.ReadAllLines(Path.Combine(directory, "ipl.txt"));
        var iplInstant = new long[iplLines.Length];
        var iplValue = new ushort[iplLines.Length];
        int used = 0;
        long first = count == 0 ? 0 : asserted[0];
        long last = count == 0 ? 0 : answerInstant[count - 1];
        foreach (string line in iplLines)
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            long instant = long.Parse(parts[0], CultureInfo.InvariantCulture);
            if (instant < first || instant > last)
            {
                continue;   // outside the replayed window
            }

            iplInstant[used] = instant;
            iplValue[used] = ushort.Parse(parts[1], CultureInfo.InvariantCulture);
            used++;
        }

        Array.Resize(ref iplInstant, used);
        Array.Resize(ref iplValue, used);

        return new BusTrace(address, size, isWrite, isCpuSpace, control, writeData, answer, md, asserted, answerInstant,
            iplInstant, iplValue);
    }

    /// <summary>A copy of the first <paramref name="count"/> cycles with their IPL timeline — one
    /// frame's worth, for a pass that has to be short.</summary>
    public BusTrace Slice(int skip, int count)
    {
        var ipl = new List<long>();
        var values = new List<ushort>();
        long from = Asserted[skip];
        long to = AnswerInstant[skip + count - 1];
        for (int i = 0; i < IplInstant.Length; i++)
        {
            if (IplInstant[i] >= from && IplInstant[i] <= to)
            {
                ipl.Add(IplInstant[i]);
                values.Add(IplValue[i]);
            }
        }

        return new BusTrace(
            Address.Skip(skip).Take(count).ToArray(),
            Size.Skip(skip).Take(count).ToArray(),
            IsWrite.Skip(skip).Take(count).ToArray(),
            IsCpuSpace.Skip(skip).Take(count).ToArray(),
            Control.Skip(skip).Take(count).ToArray(),
            WriteData.Skip(skip).Take(count).ToArray(),
            Answer.Skip(skip).Take(count).ToArray(),
            Md.Skip(skip).Take(count).ToArray(),
            Asserted.Skip(skip).Take(count).ToArray(),
            AnswerInstant.Skip(skip).Take(count).ToArray(),
            ipl.ToArray(),
            values.ToArray());
    }
}
