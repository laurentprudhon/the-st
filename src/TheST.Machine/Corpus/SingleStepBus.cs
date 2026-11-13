namespace TheST.Machine.Corpus;

/// <summary>
/// The bus a single-step corpus test runs against: the smallest chip that can answer the CPU, and
/// nothing else. It owns no beam, no counter and no clock of its own — one assertion record begins an
/// exchange, the answer lands a fixed number of ticks later, and a release record ends it, which is the
/// protocol <c>design/cpu-core.md</c> §1 and the System Control Unit's <c>PerformBusCycle</c> define.
///
/// <para>
/// Plain RAM, 24-bit, big-endian, zero-filled: the corpus's <c>ram</c> lists are deltas from zero, so
/// the store's ST bank geometry (ADR 0014) is exactly what this bus must not have. Reads never fault
/// and writes always land, because every address the corpus names is ordinary memory.
/// </para>
/// </summary>
public sealed class SingleStepBus : Chip
{
    /// <summary>One 68000 bus cycle: ADR 0009's "round up to 4 clocks".</summary>
    const long AnswerDelay = 4;

    readonly byte[] _ram = new byte[0x1000000];
    readonly List<uint> _touched = [];

    long _seenSeq = long.MinValue;
    int _assertionIndex = -1;
    long _answerInstant = -1;

    public SingleStepBus(Engine engine, int id)
        : base(engine, id, "test bus")
    {
    }

    public byte Read(uint address) => _ram[address & 0xFFFFFF];

    public void Poke(uint address, byte value)
    {
        address &= 0xFFFFFF;
        if (_ram[address] == value)
        {
            return;
        }

        _ram[address] = value;
        _touched.Add(address);
    }

    public void PokeWord(uint address, ushort value)
    {
        Poke(address, (byte)(value >> 8));
        Poke(address + 1, (byte)value);
    }

    /// <summary>Every byte this test wrote is returned to zero, so the next test starts from a clean base.</summary>
    public void Clear()
    {
        foreach (uint address in _touched)
        {
            _ram[address] = 0;
        }

        _touched.Clear();
    }

    public void Launch() => _ = Run();

    async ValueTask Run()
    {
        while (true)
        {
            ConsumeBusRecords();

            if (_answerInstant >= 0 && Clock >= _answerInstant)
            {
                Answer(_answerInstant);
                _answerInstant = -1;
                _assertionIndex = -1;
            }

            // whichever comes first: the next assertion on the wire, or the instant this bus owes an
            // answer for. With no exchange open the date is the end of time, so an idle machine's
            // queue empties instead of spinning.
            await WaitFor(_answerInstant >= 0 ? _answerInstant : long.MaxValue, Wires.Bus, _seenSeq);
        }
    }

    void ConsumeBusRecords()
    {
        ValueLog bus = Engine.Log(Wires.Bus);

        for (int index = bus.IndexAfter(_seenSeq); index >= 0 && index < bus.Count; index++)
        {
            Record record = bus[index];

            if (record.Mask == 0xFF00 && _answerInstant < 0)
            {
                _assertionIndex = index;
                _answerInstant = record.Instant + AnswerDelay;
            }

            _seenSeq = record.Seq;
        }
    }

    void Answer(long instant)
    {
        ValueLog log = Engine.Log(Wires.Bus);
        int at = _assertionIndex;
        if (at < 0 || at + 3 >= log.Count)
        {
            return;
        }

        ushort addressHigh = log[at].Value;
        ushort addressLow = log[at + 1].Value;
        ushort control = log[at + 2].Value;
        ushort writeData = log[at + 3].Value;

        uint address = (((uint)addressHigh << 16) | addressLow) & 0xFFFFFF;
        bool isWrite = (control & 0x0100) != 0;
        bool isWord = ((control >> 4) & 0x1) == 1;

        // A0 is not on the address bus: the low address record carries A1–A23, and a byte access says
        // which half of the word it wants on the lane bits (as the System Control Unit's RAM does)
        if (!isWord)
        {
            address += (uint)(control & 0x1);
        }

        if (isWrite)
        {
            if (isWord)
            {
                Poke(address, (byte)(writeData >> 8));
                Poke(address + 1, (byte)writeData);
            }
            else
            {
                Poke(address, (byte)writeData);
            }
        }
        else
        {
            ushort data = isWord
                ? (ushort)((_ram[address] << 8) | _ram[(address + 1) & 0xFFFFFF])
                : _ram[address];

            Write(instant, Wires.Md, 0xFFFF, data);
        }

        Write(instant, Wires.Answer, 0x0001, 0x0001);
    }
}
