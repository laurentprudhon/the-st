using System.Runtime.CompilerServices;

namespace TheST.Machine.Cpu;

/// <summary>
/// The 68000 core (ADR 0009, design/cpu-core.md). One bus cycle is a masked control record on the bus
/// wire, a subscription to the System Control Unit's answer, a data sample that resolves on the stack
/// and a release record — and its cost is the elapsed machine time, never a returned number.
///
/// <para>
/// What this build does and does not carry from the ADR, stated so it is not mistaken for the finished
/// core: the three machines are here as state fields and a driving switch; the bus leaf, the reset
/// sequence and the exception script are ADR 0009's. The prefetch is fetch-on-demand — each
/// instruction word is one bus cycle, fetched when the decoder needs it — rather than the three
/// registers running ahead; that is a fidelity gap recorded in the rulings file, and it is why
/// self-modifying code beyond one instruction boundary would not be exact.
/// </para>
/// </summary>
public sealed partial class M68000 : Chip
{
    // ---- clock ---------------------------------------------------------------------------
    /// <summary>One CPU clock is four ticks of the 32 MHz oscillator: the 8 MHz bus.</summary>
    public const int TicksPerCpuClock = 4;

    readonly uint[] _d = new uint[8];
    readonly uint[] _a = new uint[8];        // A0–A6; A7 is the active stack pointer
    uint _usp;
    uint _ssp;

    uint _pc;
    ushort _sr;

    bool _halted;
    bool _stopped;

    // the fault a bus cycle reports, read by the caller — never an exception
    uint _faultAddress;

    // ---- reported, never gated -----------------------------------------------------------
    public long Instructions { get; private set; }
    public long BusCycles { get; private set; }
    public long InterruptsTaken { get; private set; }
    public long Exceptions { get; private set; }
    public long InternalClocks { get; private set; }

    /// <summary>A debug-build instruction hook: instant, opcode and the address it was read from.</summary>
    public Action<long, ushort, uint>? Observer { get; set; }

    public M68000(Engine engine, int id)
        : base(engine, id, "CPU")
    {
    }

    // ---- registers -----------------------------------------------------------------------

    public bool Supervisor => (_sr & 0x2000) != 0;
    public bool Halted
    {
        get
        {
            if (_halted && HaltReason is null)
            {
                HaltReason = $"halted at pc={_pc:x6} sp={_ssp:x8} t={Clock}";
            }

            return _halted;
        }
    }

    public string? HaltReason { get; private set; }

    /// <summary>A data register, for the harness's probes and the debug instruments.</summary>
    public uint PeekD(int index) => _d[index];

    /// <summary>An address register (A7 is whichever stack pointer is live), for the probes.</summary>
    public uint PeekA(int index) => GetA(index);

    public int InterruptMask => (_sr >> 8) & 0x7;
    public uint PC => _pc;
    public ushort SR => _sr;

    uint A7
    {
        get => Supervisor ? _ssp : _usp;
        set
        {
            if (Supervisor)
            {
                _ssp = value;
            }
            else
            {
                _usp = value;
            }
        }
    }

    uint GetA(int index) => index == 7 ? A7 : _a[index];

    void SetA(int index, uint value)
    {
        if (index == 7)
        {
            A7 = value;
        }
        else
        {
            _a[index] = value;
        }
    }

    /// <summary>Internal work between bus cycles — the only way the core moves its own clock.</summary>
    void Clocks(int cpuClocks)
    {
        InternalClocks += cpuClocks;
        Advance(cpuClocks * TicksPerCpuClock);
    }

    // ---- flags ---------------------------------------------------------------------------
    const ushort FlagC = 0x0001;
    const ushort FlagV = 0x0002;
    const ushort FlagZ = 0x0004;
    const ushort FlagN = 0x0008;
    const ushort FlagX = 0x0010;

    int SizeBits(int size) => size == 0 ? 8 : size == 1 ? 16 : 32;

    uint SizeMask(int size) => size == 0 ? 0xFFu : size == 1 ? 0xFFFFu : 0xFFFFFFFFu;

    int SignBit(int size) => size == 0 ? 0x80 : size == 1 ? 0x8000 : unchecked((int)0x80000000u);

    long Signed(uint value, int size) => size switch
    {
        0 => (sbyte)(value & 0xFF),
        1 => (short)(value & 0xFFFF),
        _ => (int)value,
    };

    void SetLogicFlags(uint result, int size)
    {
        uint mask = SizeMask(size);
        result &= mask;
        _sr = (ushort)(_sr & ~(FlagN | FlagZ | FlagV | FlagC));
        if (result == 0) _sr |= FlagZ;
        if ((result & (uint)SignBit(size)) != 0) _sr |= FlagN;
    }

    void SetAddFlags(uint source, uint destination, uint result, int size)
    {
        uint mask = SizeMask(size);
        uint sign = (uint)SignBit(size);
        source &= mask;
        destination &= mask;
        result &= mask;

        _sr = (ushort)(_sr & ~(FlagN | FlagZ | FlagV | FlagC | FlagX));
        if (result == 0) _sr |= FlagZ;
        if ((result & sign) != 0) _sr |= FlagN;
        if (((source ^ result) & (destination ^ result) & sign) != 0) _sr |= FlagV;
        if (result < destination || result < source) _sr |= (ushort)(FlagC | FlagX);
    }

    void SetSubFlags(uint source, uint destination, uint result, int size)
    {
        uint mask = SizeMask(size);
        uint sign = (uint)SignBit(size);
        source &= mask;
        destination &= mask;
        result &= mask;

        _sr = (ushort)(_sr & ~(FlagN | FlagZ | FlagV | FlagC | FlagX));
        if (result == 0) _sr |= FlagZ;
        if ((result & sign) != 0) _sr |= FlagN;
        if (((source ^ destination) & (result ^ destination) & sign) != 0) _sr |= FlagV;
        if (destination < source) _sr |= (ushort)(FlagC | FlagX);
    }

    bool Condition(int condition)
    {
        bool c = (_sr & FlagC) != 0;
        bool v = (_sr & FlagV) != 0;
        bool z = (_sr & FlagZ) != 0;
        bool n = (_sr & FlagN) != 0;

        return condition switch
        {
            0 => true,                 // T
            1 => false,                // F
            2 => !c && !z,             // HI
            3 => c || z,               // LS
            4 => !c,                   // CC
            5 => c,                    // CS
            6 => !z,                   // NE
            7 => z,                    // EQ
            8 => !v,                   // VC
            9 => v,                    // VS
            10 => !n,                  // PL
            11 => n,                   // MI
            12 => n == v,              // GE
            13 => n != v,              // LT
            14 => !z && (n == v),      // GT
            _ => z || (n != v),        // LE
        };
    }

    // ---- the bus -------------------------------------------------------------------------

    readonly record struct BusResult(ushort Data, bool Faulted, bool VectorProvided);

    bool _busFaulted;
    long _answerSeq = long.MinValue;

    /// <summary>
    /// One bus cycle (ADR 0009 §1): the assertion record, the subscription to the answer, the stack
    /// sample, the release. The core advances to the answering instant because the engine sets its
    /// clock there when it resumes it.
    /// </summary>
    async ValueTask<BusResult> Cycle(uint address, int size, bool isWrite, ushort writeData, bool isCpuSpace = false)
    {
        long asserted = Clock;

        ushort control = (ushort)(
            (isWrite ? 0x0100 : 0) |
            ((size == 0 ? 0 : 1) << 4) |
            ((address & 1) != 0 ? 1 : 0) |
            ((isCpuSpace ? 7 : (Supervisor ? 5 : 1)) << 12));

        Write(asserted, Wires.Bus, 0xFF00, (ushort)(address >> 16));
        Write(asserted, Wires.Bus, 0xFFFE, (ushort)(address & 0xFFFE));
        Write(asserted, Wires.Bus, 0xFFFF, control);
        Write(asserted, Wires.Bus, 0xFFFF, isWrite ? writeData : (ushort)0);

        BusCycles++;
        await WaitFor(long.MaxValue, Wires.Answer, _answerSeq);

        long answerInstant = Clock;
        ValueLog answers = Engine.Log(Wires.Answer);
        int consumed = answers.IndexAtOrBefore(answerInstant);
        if (consumed >= 0)
        {
            _answerSeq = answers[consumed].Seq;
        }

        ushort answer = Engine.ValueAt(Wires.Answer, answerInstant);
        bool faulted = (answer & 0x0002) != 0;
        bool vector = (answer & 0x0004) != 0;
        ushort data = isWrite ? (ushort)0 : Engine.ValueAt(Wires.Md, answerInstant);

        // the release: AS high, and Z on the data lanes — a record of its own, and the lane that
        // tells a release from an assertion
        Write(answerInstant, Wires.Bus, 0x8000, 0x8000);

        return new BusResult(data, faulted, vector);
    }

    // ---- memory helpers ------------------------------------------------------------------

    async ValueTask<ushort> ReadWordAsync(uint address)
    {
        BusResult result = await Cycle(address, 1, false, 0);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
        }

        return result.Data;
    }

    async ValueTask<byte> ReadByteAsync(uint address)
    {
        BusResult result = await Cycle(address, 0, false, 0);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
        }

        return (byte)result.Data;
    }

    async ValueTask<uint> ReadLongAsync(uint address)
    {
        ushort high = await ReadWordAsync(address);
        if (_busFaulted) return 0;
        ushort low = await ReadWordAsync(address + 2);
        return ((uint)high << 16) | low;
    }

    async ValueTask WriteWordAsync(uint address, ushort value)
    {
        BusResult result = await Cycle(address, 1, true, value);
        if (result.Faulted) _busFaulted = true;
    }

    async ValueTask WriteByteAsync(uint address, byte value)
    {
        BusResult result = await Cycle(address, 0, true, value);
        if (result.Faulted) _busFaulted = true;
    }

    async ValueTask WriteLongAsync(uint address, uint value)
    {
        await WriteWordAsync(address, (ushort)(value >> 16));
        if (_busFaulted) return;
        await WriteWordAsync(address + 2, (ushort)value);
    }

    // ---- the instruction stream ----------------------------------------------------------

    /// <summary>The next instruction word: one bus cycle at the program counter.</summary>
    async ValueTask<ushort> FetchAsync()
    {
        ushort word = await ReadWordAsync(_pc);
        if (!_busFaulted)
        {
            _pc += 2;
        }

        return word;
    }

    async ValueTask<uint> FetchLongAsync()
    {
        uint high = await FetchAsync();
        if (_busFaulted) return 0;
        uint low = await FetchAsync();
        return (high << 16) | low;
    }

    // ---- reset ---------------------------------------------------------------------------

    async ValueTask ResetSequenceAsync()
    {
        _sr = 0x2700;
        _halted = false;
        _stopped = false;

        _busFaulted = false;
        _ssp = await ReadLongAsync(0x00000000);
        _pc = await ReadLongAsync(0x00000004);
        if (_busFaulted)
        {
            _halted = true;
            return;
        }

        _busFaulted = false;
    }

    // ---- the chip's coroutine ------------------------------------------------------------

    public void Launch()
    {
        _ = Run();
    }

    async ValueTask Run()
    {
        await ResetSequenceAsync();
        if (_halted)
        {
            return;
        }

        while (!_halted)
        {
            if (_stopped)
            {
                await WaitForStop();
                continue;
            }

            long before = Clock;
            ushort opcode = await FetchAsync();
            if (_halted)
            {
                return;
            }

            if (_busFaulted)
            {
                await TakeFaultAsync(_faultAddress, false);
                _busFaulted = false;
                continue;
            }

            Instructions++;
            Observer?.Invoke(Clock, opcode, _pc - 2);
            await ExecuteAsync(opcode);

            if (_busFaulted)
            {
                await TakeFaultAsync(_faultAddress, false);
                _busFaulted = false;
                continue;
            }

            _ = before;

            await CheckInterruptAsync();
        }
    }

    async ValueTask WaitForStop()
    {
        // STOP: the loop yields until the level can be taken; the sampling below decides
        await WaitFor(long.MaxValue, Wires.Ipl, Engine.Log(Wires.Ipl).Count > 0 ? Engine.Log(Wires.Ipl)[^1].Seq : long.MinValue);
        _stopped = false;
        await CheckInterruptAsync();
    }

    // ---- interrupts and exceptions -------------------------------------------------------

    async ValueTask CheckInterruptAsync()
    {
        int level = Engine.ValueAt(Wires.Ipl, Clock) & 0x7;
        if (level == 0)
        {
            return;
        }

        if (level <= InterruptMask && level != 7)
        {
            return;
        }

        long changedAt = LastIplChangeAt(Clock);
        if (Clock - changedAt < 4 * TicksPerCpuClock)
        {
            return;   // the pin changed too late: taken one instruction later, by the rule
        }

        InterruptsTaken++;

        // the acknowledge is a CPU-space cycle: FC=111, A3–A1 = level
        int vector = 24 + level;
        BusResult acknowledge = await Cycle(0xFFFFFFF0u | ((uint)level << 1), 1, false, 0, isCpuSpace: true);
        if (!acknowledge.Faulted && acknowledge.VectorProvided)
        {
            vector = acknowledge.Data & 0xFF;
        }
        else if (acknowledge.Faulted)
        {
            vector = 24;   // a spurious interrupt
        }

        await EnterExceptionAsync(vector, level);
    }

    long LastIplChangeAt(long instant)
    {
        ValueLog log = Engine.Log(Wires.Ipl);
        int index = log.IndexAtOrBefore(instant);
        return index < 0 ? 0 : log[index].Instant;
    }

    /// <summary>
    /// Exception entry as a script with the bus work in it (ADR 0009 §7). The acknowledge sits between
    /// the PCL stack write and the rest of the frame; the mask is raised before it.
    /// </summary>
    async ValueTask EnterExceptionAsync(int vector, int level)
    {
        Exceptions++;
        ushort oldSr = _sr;
        _sr = (ushort)((_sr | 0x2000) & ~0x8000);
        if (level != 0)
        {
            _sr = (ushort)((_sr & ~0x0700) | (level << 8));   // the mask is I2–I0, bits 8–10
        }

        uint returnPc = _pc;
        bool group0 = vector == 2 || vector == 3;

        if (group0)
        {
            _ssp -= 2;
            await WriteWordAsync(_ssp, (ushort)(returnPc >> 16));
            _ssp -= 2;
            await WriteWordAsync(_ssp, (ushort)returnPc);
            _ssp -= 2;
            await WriteWordAsync(_ssp, oldSr);
            _ssp -= 2;
            await WriteWordAsync(_ssp, 0);
            _ssp -= 2;
            await WriteWordAsync(_ssp, (ushort)(_faultAddress & 0xFFFF));
            _ssp -= 2;
            await WriteWordAsync(_ssp, (ushort)(_faultAddress >> 16));
            _ssp -= 2;
            await WriteWordAsync(_ssp, 0);
        }
        else
        {
            _ssp -= 2;
            await WriteWordAsync(_ssp, (ushort)(returnPc >> 16));
            _ssp -= 2;
            await WriteWordAsync(_ssp, (ushort)returnPc);
            _ssp -= 2;
            await WriteWordAsync(_ssp, oldSr);
        }

        if (_busFaulted)
        {
            _halted = true;   // a double fault: the hardware's rule
            return;
        }

        uint newPc = await ReadLongAsync((uint)(vector * 4));
        if (_busFaulted)
        {
            _halted = true;
            return;
        }

        _pc = newPc;
        _busFaulted = false;
    }

    async ValueTask TakeFaultAsync(uint address, bool addressError)
    {
        _busFaulted = false;   // the fault is being taken, not still pending
        _faultAddress = address;
        await EnterExceptionAsync(addressError ? 3 : 2, 0);
    }
}
