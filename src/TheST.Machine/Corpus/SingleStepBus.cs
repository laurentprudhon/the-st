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
    long _answerInstant = -1;
    int _groupFill;
    ushort _addressHigh;
    ushort _addressLow;
    ushort _control;
    ushort _writeData;

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
                // The exchange's four records are read here, on this resume, and kept as values: an
                // index into a log is only valid inside one resume (retention discards from the front,
                // so an index held across a suspension lands on another record or past the end).
                // The first record of the group is the address high word, masked 0xFF00.
                _addressHigh = record.Value;
                _groupFill = 1;
                _answerInstant = record.Instant + AnswerDelay;
            }
            else if (_answerInstant >= 0 && _groupFill < 4)
            {
                switch (_groupFill)
                {
                    case 1: _addressLow = record.Value; break;
                    case 2: _control = record.Value; break;
                    default: _writeData = record.Value; break;
                }

                _groupFill++;
            }

            _seenSeq = record.Seq;
        }
    }

    void Answer(long instant)
    {
        if (_groupFill < 4)
        {
            return;
        }

        uint address = (((uint)_addressHigh << 16) | _addressLow) & 0xFFFFFF;
        bool isWrite = (_control & 0x0100) != 0;
        bool isWord = ((_control >> 4) & 0x1) == 1;

        // A0 is not on the address bus: the low address record carries A1–A23, and a byte access says
        // which half of the word it wants on the lane bits (as the System Control Unit's RAM does)
        if (!isWord)
        {
            address += (uint)(_control & 0x1);
        }

        if (isWrite)
        {
            if (isWord)
            {
                Poke(address, (byte)(_writeData >> 8));
                Poke(address + 1, (byte)_writeData);
            }
            else
            {
                Poke(address, (byte)_writeData);
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
        _groupFill = 0;
    }
}
