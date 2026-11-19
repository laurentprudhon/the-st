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
/// sequence and the exception script are ADR 0009's. The prefetch is §4's queue — <c>IRD</c>, <c>IR</c>
/// and <c>IRC</c>, with a jump, a branch, an <c>RTE</c>/<c>RTR</c> or an exception entry refilling it at
/// its target — so an instruction fetches as many words as it has, extension words come out of the
/// queue rather than off the bus, and the program counter a group-0/2 frame saves is the address of the
/// word in <c>IR</c> with no offset arithmetic. What is still deferred is Level 2's per-opcode placement
/// of the trailing fetch inside the instruction (the prefetch class's order against a write), because
/// §4's revision takes the class as a datum and leaves its placement to the micro-op script.
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

    // ---- the prefetch queue (ADR 0009 §4) ------------------------------------------------
    // IRD is the word being executed, IR the head of the two-word queue and IRC the word behind it, so
    // the instruction stream runs a word ahead of the decoder: an instruction's extension words come
    // out of the queue, and what it fetches is its own word count. `_irAddress` is the address of the
    // word in IR — what a group-0/2 frame saves — and it trails the fetch pointer by one word.
    // `_fetchPointer` is the address of the next read, i.e. the hardware's program counter.
    ushort _ird;
    ushort _ir;
    ushort _irc;
    bool _ircValid;
    uint _irAddress;
    uint _fetchPointer;

    /// <summary>
    /// The address of the last read issued in a <em>fetch state</em> — the words that fill the queue
    /// (`FetchAsync`'s refill, the trailing fetch, the absolute-long pair's read, `PrefetchAsync`,
    /// `RefillAsync`). MAME's microcode sets `m_pc` only there (`roml1`), so this is the depth a
    /// group-0 data fault's frame saves; an extension-word read (`ReadProgramAsync`) leaves it alone
    /// (#76: the +2 class, 19 791 records).
    /// </summary>
    uint _fetchStateAddress;

    /// <summary>
    /// The fetch pointer when the current instruction began: `_fetchPointer` still being this value says
    /// the instruction has fetched no word of its own yet. It is #76's instrument, not a rule — the frame
    /// columns of <c>cpu-tests --clocks</c> report it beside the frame's saved program counter, because
    /// where MAME's pipeline puts its prefetch is what a MAME-true frame has to reproduce.
    /// </summary>
    uint _streamStart;

    /// <summary>
    /// The address of the instruction's own opcode word. A group-1/2 frame saves it for the illegal and
    /// privilege exceptions (MAME's corpus, measured over 21 311 frames) and the address past the
    /// instruction for `TRAP`/`TRAPV`/`CHK`/a divide — see `SavesInstructionAddress`.
    /// </summary>
    uint _opcodeAddress;

    // a jump, a branch, a return or an exception entry refilled the queue at its target: the closing
    // steps belong to an instruction that fell through, so the loop skips them for this one
    bool _flowTransferred;

    // the instruction has issued its one trailing fetch (before the write for class 0, at the closing
    // steps for class 1)
    bool _trailingFetched;

    // a MOVE's absolute-long destination consumed the queue whole (its pair is one pop, ADR 0009 §4's
    // per-opcode placement): IRD/IR hold the address's own two words, so the closing steps read the
    // next opcode's pair instead of shifting what is there
    bool _queueShort;

    bool _halted;
    bool _stopped;

    // the fault a bus cycle reports, read by the caller — never an exception
    uint _faultAddress;

    // the fault's own detail, for the group-0 frame (design/cpu-core.md §7): which word was being
    // executed, whether the access was a write, and whether the fault was the fetch of an odd
    // program counter rather than a data access
    ushort _opcode;

    // ---- the internal clock (ADR 0009 §3, choice 3 of LENGTH.md §7) -------------------------
    /// <summary>The instruction's own clocks, charged before its closing fetch. Set from the opcode by
    /// <see cref="InstructionOwnClocks"/> and overridden by the data-dependent families once they have
    /// read their operand.</summary>
    int _own;

    /// <summary>True once the effective address has charged its 2 clocks: an instruction charges them
    /// once however many of its operands predecrement or index.</summary>
    bool _eaCharged;

    bool _faultWrite;
    bool _faultIsFetch;
    bool _addressError;

    // ---- reported, never gated -----------------------------------------------------------
    public long Instructions { get; private set; }
    public long BusCycles { get; private set; }
    public long InterruptsTaken { get; private set; }
    public long Exceptions { get; private set; }
    public long InternalClocks { get; private set; }

    /// <summary>A debug-build instruction hook: instant, opcode and the address it was read from.</summary>
    public Action<long, ushort, uint>? Observer { get; set; }

#if DEBUG
    /// <summary>
    /// A debug instrument: every bus cycle the core performs, as the corpus's own <c>transactions</c>
    /// records it — write?, function code, address, data. It exists so the core's cycle list can be
    /// printed beside a record's reference trace (ADR 0009 §4's revision: that trace is the bar).
    /// </summary>
    public Action<bool, int, uint, ushort>? CycleObserver { get; set; }

    /// <summary>
    /// A debug instrument: every internal clock the core charges, in order with its bus cycles. The
    /// corpus states the same thing as its <c>["n", cycles]</c> transactions, so this is what makes the
    /// *placement* of the internal work comparable, not only its total.
    /// </summary>
    public Action<int>? ClockObserver { get; set; }
#endif

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

    /// <summary>
    /// Internal work between bus cycles — the only way the core moves its own clock.
    ///
    /// <para>
    /// It is a <b>wait</b>, not an <c>Advance</c>: the machine has one timeline, so a CPU that runs its
    /// own clock forward without suspending takes it past machine time, the beam and the timers fall
    /// behind, and the boot loses its counts (measured: 300 frames never reached the desktop). The
    /// delay is therefore armed as a date on the answer wire, which has nothing pending at that moment,
    /// so the machine advances in step and the CPU resumes at exactly <c>Clock + n × 4</c> ticks.
    /// </para>
    /// </summary>
    /// <param name="prefetch">
    /// Whether an idle clock may be spent on the pipeline's prefetch — the hardware's own use of one,
    /// and the reason a faulting instruction's frame saves a deeper counter than this core's lazy queue
    /// used to (#76: MAME's microcode has an explicit prefetch op between its idle runs). Only the two
    /// body charges ask for it; a leading charge (a branch, a jump) must not, because the word it would
    /// fetch is the fall-through the control transfer is about to abandon, and an exception entry's
    /// charges must not, because the handler's refill has not happened yet.
    /// </param>
    async ValueTask ClocksAsync(int cpuClocks, bool prefetch = false)
    {
        if (cpuClocks <= 0)
        {
            return;
        }

        if (prefetch)
        {
            await PrefetchAsync();
        }

        InternalClocks += cpuClocks;
#if DEBUG
        ClockObserver?.Invoke(cpuClocks);
#endif
        await WaitFor(Clock + cpuClocks * TicksPerCpuClock, Wires.Answer, _answerSeq);
    }

    // ---- flags ---------------------------------------------------------------------------
    const ushort FlagC = 0x0001;
    const ushort FlagV = 0x0002;
    const ushort FlagZ = 0x0004;
    const ushort FlagN = 0x0008;
    const ushort FlagX = 0x0010;

    /// <summary>The bits a 68000 stores in its status register: T, S, I2–I0 and the condition codes.</summary>
    const ushort DefinedSr = 0xA71F;

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

    /// <summary>
    /// <c>ADDX</c>/<c>SUBX</c> flags: N, V, C and X as for a plain add or subtract, and <b>Z only ever
    /// cleared, never set</b> — Motorola's rule, so that a multi-precision chain reports the whole
    /// result rather than its last limb. The corpus pins it: a byte sum that comes out zero leaves Z
    /// as it was (FINDINGS row 41).
    /// </summary>
    void SetExchangeFlags(bool subtract, uint source, uint destination, uint result, int size)
    {
        // The carry has to be seen in a wider width than the operand: `0xFF + 0x4A + X` carries out
        // of a byte, and masking the source first loses exactly that carry.
        uint mask = SizeMask(size);
        uint sign = (uint)SignBit(size);
        uint src = source & mask;
        uint dst = destination & mask;
        uint res = result & mask;
        uint extended = (uint)((_sr & FlagX) != 0 ? 1 : 0);

        bool carry;
        bool overflow;

        if (subtract)
        {
            carry = dst < src + extended;
            overflow = (((src ^ dst) & (res ^ dst) & sign) != 0);
        }
        else
        {
            carry = (ulong)src + dst + extended > mask;

            // the extend bit does not enter the overflow test: V is "the result's sign does not fit
            // the operands' signs", and the corpus pins it on `0x7F + 0x6A + X` (V set) as well as
            // on `0xE9 + 0x7B + X` (V clear)
            overflow = ((src ^ res) & (dst ^ res) & sign) != 0;
        }

        ushort zero = (ushort)(_sr & FlagZ);

        _sr = (ushort)(_sr & ~(FlagN | FlagZ | FlagV | FlagC | FlagX));
        if (carry) _sr |= (ushort)(FlagC | FlagX);
        if ((res & sign) != 0) _sr |= FlagN;
        if (overflow) _sr |= FlagV;

        // Z is only ever cleared: a zero result leaves it as it was, so that a multi-precision chain
        // reports the whole result rather than its last limb
        if (res == 0)
        {
            _sr = (ushort)(_sr | zero);
        }
    }

    /// <summary>
    /// A comparison's flags: N, Z, V and C as a subtraction, and X untouched (CMP, CMPA, CMPM).
    /// </summary>
    void SetCompareFlags(uint source, uint destination, uint result, int size)
    {
        bool extend = (_sr & FlagX) != 0;
        SetSubFlags(source, destination, result, size);
        _sr = (ushort)(extend ? _sr | FlagX : _sr & ~FlagX);
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

    // ---- a post-increment lands with the access, not with the address ---------------------------
    // `(An)+` computes its address, performs the access, and only then moves the register. The core
    // still increments eagerly (one assignment), but remembers where it started, so an access that
    // faults can put the register back: the corpus measures it — `MOVE.w <ea>, (A5)+` onto an odd
    // address leaves A5 where it was, while `-(An)` keeps its decrement (FINDINGS row 42).
    int _postIncrementReg = -1;
    uint _postIncrementFrom;

    void PostIncremented(int reg, uint from)
    {
        _postIncrementReg = reg;
        _postIncrementFrom = from;
    }

    /// <summary>
    /// Puts a post-incremented register back when the access did not land — but only for a plain
    /// <c>MOVE</c>, whose model increments *after* the write. Every other instruction increments
    /// before the access, so a fault keeps the increment (the corpus measures both, FINDINGS row 42).
    /// </summary>
    void SettlePostIncrement(bool faulted)
    {
        bool movesAfterTheWrite = (_opcode >> 12) is 1 or 2 or 3;

        if (faulted && movesAfterTheWrite && _postIncrementReg >= 0)
        {
            SetA(_postIncrementReg, _postIncrementFrom);
        }

        _postIncrementReg = -1;
    }

    /// <summary>
    /// One bus cycle (ADR 0009 §1): the assertion record, the subscription to the answer, the stack
    /// sample, the release. The core advances to the answering instant because the engine sets its
    /// clock there when it resumes it.
    /// <para>
    /// One bus cycle is also <em>one suspended frame</em> (#62): the fault bookkeeping and a fetch's
    /// <c>_pc</c> advance live here, so the helpers below are forwarders and the frame a wrapper would
    /// have boxed does not exist. Same records, same instants, same order.
    /// </para>
    /// </summary>
    async ValueTask<BusResult> Cycle(uint address, int size, bool isWrite, ushort writeData, bool isCpuSpace = false, bool isProgram = false)
    {
        // an address error is not a bus cycle: a word access at an odd address never reaches the
        // wire, and the group-0 frame records what it was trying to reach (ADR 0009 §7)
        if (!isCpuSpace && size == 1 && (address & 1) != 0)
        {
            _faultAddress = address;
            _faultWrite = isWrite;
            _faultIsFetch = false;
            _addressError = true;
            _busFaulted = true;
            SettlePostIncrement(isWrite);
            return new BusResult(0, true, false);
        }

        long asserted = Clock;

        ushort control = (ushort)(
            (isWrite ? 0x0100 : 0) |
            ((size == 0 ? 0 : 1) << 4) |
            ((address & 1) != 0 ? 1 : 0) |
            ((isCpuSpace ? 7 : isProgram ? (Supervisor ? 6 : 2) : (Supervisor ? 5 : 1)) << 12));

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

#if DEBUG
        CycleObserver?.Invoke(isWrite, isCpuSpace ? 7 : isProgram ? (Supervisor ? 6 : 2) : (Supervisor ? 5 : 1), address, isWrite ? writeData : data);
#endif

        // the release: AS high, and Z on the data lanes — a record of its own, and the lane that
        // tells a release from an assertion
        Write(answerInstant, Wires.Bus, 0x8000, 0x8000);

        if (faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
        }

        // the register goes back only for a faulted *write*: a read that faults has already moved it
        // (the corpus measures both directions, and they differ)
        SettlePostIncrement(faulted && isWrite);

        return new BusResult(data, faulted, vector);
    }

    // ---- memory helpers: forwarders, one leaf a shape ----------------------------------------

    async ValueTask<ushort> ReadCycleAsync(uint address, int size, int pcAdvance)
    {
        BusResult result = await Cycle(address, size, false, 0);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
        }
        else if (pcAdvance != 0)
        {
            _pc += (uint)pcAdvance;
        }

        return result.Data;
    }

    async ValueTask<byte> ReadByteCycleAsync(uint address)
    {
        BusResult result = await Cycle(address, 0, false, 0);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
        }

        return (byte)result.Data;
    }

    async ValueTask WriteCycleAsync(uint address, int size, ushort value)
    {
        BusResult result = await Cycle(address, size, true, value);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
        }
    }

    ValueTask<ushort> ReadWordAsync(uint address) => ReadCycleAsync(address, 1, 0);

    ValueTask<byte> ReadByteAsync(uint address) => ReadByteCycleAsync(address);

    async ValueTask<uint> ReadLongAsync(uint address)
    {
        ushort high = await ReadWordAsync(address);
        if (_busFaulted) return 0;
        ushort low = await ReadWordAsync(address + 2);
        return ((uint)high << 16) | low;
    }

    ValueTask WriteWordAsync(uint address, ushort value) => WriteCycleAsync(address, 1, value);

    ValueTask WriteByteAsync(uint address, byte value) => WriteCycleAsync(address, 0, value);

    async ValueTask WriteLongAsync(uint address, uint value)
    {
        await WriteWordAsync(address, (ushort)(value >> 16));
        if (_busFaulted) return;
        await WriteWordAsync(address + 2, (ushort)value);
    }

    // ---- the instruction stream: the prefetch queue of ADR 0009 §4 -------------------------

    /// <summary>
    /// The pipeline's prefetch: the word behind the queue's head, read <em>now</em> — inside an idle
    /// clock of the instruction's own body — instead of when the instruction next consumes a word.
    /// One read, and the queue's slot is then full, so the instruction's later fetch takes the word
    /// without a bus cycle of its own: the count per instruction is unchanged, the <em>placement</em>
    /// is the hardware's, and it is what a MAME-true exception frame saves (the address of the last word
    /// fetched, `_fetchPointer − 2`).
    /// </summary>
    async ValueTask PrefetchAsync()
    {
        if (_busFaulted || _ircValid || _queueShort)
        {
            return;
        }

        uint address = _fetchPointer;
        _fetchStateAddress = address;
        BusResult result = await Cycle(address, 1, false, 0, isProgram: true);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
            _faultIsFetch = true;
            return;
        }

        _fetchPointer = address + 2;
        _irc = result.Data;
        _ircValid = true;
    }

    /// <summary>
    /// One read of the instruction stream: program space, so the bus records FC 6 in supervisor mode
    /// and FC 2 in user mode — never the data codes a fetch-on-demand core used for the same cycle.
    /// A fault here is a <em>fetch</em> fault (<see cref="_faultIsFetch"/>), which is what the frame's
    /// status word records.
    /// </summary>
    async ValueTask<ushort> ReadProgramAsync(uint address)
    {
        // the queue trails the fetch pointer by one word, which is the whole of the frame's rule:
        // a group-0/2 frame saves the address of the word in IR, and it is set by the read, not by a
        // subtraction on the counter
        _irAddress = address - 2;

        BusResult result = await Cycle(address, 1, false, 0, isProgram: true);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
            _faultIsFetch = true;
            return 0;
        }

        _fetchPointer = address + 2;
        return result.Data;
    }

    /// <summary>
    /// The next instruction word: the queue's head, with the word behind it refilled by one read, so an
    /// instruction performs exactly its own word count of fetches (Cwik's rule: one word fetches once
    /// at <c>pc + 4</c>, four words fetch four times).
    /// </summary>
    async ValueTask<ushort> FetchAsync()
    {
        if (_busFaulted)
        {
            return 0;
        }

        // the slot behind the head is filled first, so the head hands back a word the queue already
        // held and the refill is this one word's own fetch. The read is inlined — address bookkeeping
        // then the bus leaf — because this is the hottest path in the core and a forwarder is a frame
        // (#62's rule, which #70 re-paid for once: two hops here cost 0.95 ms of the frame)
        if (!_ircValid)
        {
            uint address = _fetchPointer;
            _irAddress = address - 2;

            BusResult result = await Cycle(address, 1, false, 0, isProgram: true);
            if (result.Faulted)
            {
                _busFaulted = true;
                _faultAddress = address;
                _faultIsFetch = true;
                return 0;
            }

            _fetchPointer = address + 2;
            _irc = result.Data;
            _ircValid = true;
        }

        ushort word = _ir;
        _ir = _irc;
        _ircValid = false;
        _pc += 2;
        return word;
    }

    /// <summary>
    /// The three closing steps (Cwik): <c>IRC → IR</c>, one fetch into <c>IRC</c>, <c>IR → IRD</c>. One
    /// read per instruction, so the queue is a word ahead when the next opcode is decoded, and the
    /// address a frame saves has already moved to the word the instruction has not consumed.
    /// <para>
    /// The trailing fetch is this instruction's one read, and a fault that comes before it cancels it —
    /// which is why a faulting instruction's frame saves a counter two bytes lower than a completed
    /// instruction of the same shape. A class-0 instruction (plain <c>MOVE</c> to <c>-(An)</c>) has
    /// already issued it before its write, so its store fault leaves the fetch in the trace.
    /// </para>
    /// </summary>
    async ValueTask CloseInstructionAsync()
    {
        if (_queueShort)
        {
            await CloseAfterLongDestinationAsync();
            return;
        }

        if (!_busFaulted)
        {
            await ClocksAsync(_own, prefetch: true);   // the instruction's own work, before its closing fetch
            _own = 0;
            await TrailingFetchAsync();
        }

        _ird = _ir;
        _ir = _irc;
        _ircValid = false;
    }

    /// <summary>
    /// The closing steps of a plain <c>MOVE</c> whose destination was an absolute long: that address
    /// took the queue whole, so nothing in <c>IRD</c>/<c>IR</c> belongs to the next opcode and the
    /// shift above would hand the decoder the address's own low word. The reference refills twice
    /// here (<c>Prefetch(); Prefetch();</c> after the store, one of them before it for a source that
    /// took no bus cycle), which is why these two reads are the ones the corpus counts.
    /// </summary>
    async ValueTask CloseAfterLongDestinationAsync()
    {
        if (_busFaulted)
        {
            return;
        }

        // the pair's prefetch-first, when the class ran it, has already paid one of the two
        ushort first = _ircValid ? _irc : await ReadQueueWordAsync();
        _ircValid = false;
        if (_busFaulted)
        {
            return;
        }

        ushort second = await ReadQueueWordAsync();
        if (_busFaulted)
        {
            return;
        }

        _ird = first;
        _ir = second;
    }

    /// <summary>
    /// The instruction's prefetch class (ADR 0009 §4): 0 = the trailing fetch is issued <em>before</em>
    /// the final write, 1 = after it. The corpus pins which is which — a plain <c>MOVE</c> into
    /// <c>-(An)</c> has its trailing fetch before the write, every other instruction after it.
    /// </summary>
    bool PrefetchBeforeWrite => (_opcode >> 12) is 1 or 2 or 3;

    /// <summary>
    /// Whether a <c>MOVE</c>'s <em>source</em> operand cost no bus cycle of its own — a data or address
    /// register direct, or an immediate. The reference calls this class
    /// <c>MOVE_bw_AbsoluteLong_prefetch_first</c> / <c>MOVE_l_AbsoluteLong_prefetch_first</c>: with an
    /// absolute-long destination the bus is free while the store waits, so the CPU prefetches the word
    /// after the address before it writes, where every other source stores first.
    /// </summary>
    bool MoveSourceNeedsNoBusCycle
    {
        get
        {
            int sourceMode = (_opcode >> 3) & 0x7;
            int sourceReg = _opcode & 0x7;
            return sourceMode is 0 or 1 || (sourceMode == 7 && sourceReg == 4);
        }
    }

    /// <summary>
    /// The instruction's trailing fetch: the one read that keeps the queue a word ahead. It is issued
    /// once per instruction, before the write for class 0 and at the closing steps otherwise; a fault
    /// that comes first cancels it, and the corpus's traces count exactly that difference.
    /// </summary>
    async ValueTask TrailingFetchAsync()
    {
        if (_trailingFetched)
        {
            return;
        }

        _trailingFetched = true;

        if (_ircValid)
        {
            return;
        }

        uint address = _fetchPointer;
        _irAddress = address - 2;
        _fetchStateAddress = address;

        BusResult result = await Cycle(address, 1, false, 0, isProgram: true);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
            _faultIsFetch = true;
            return;
        }

        _fetchPointer = address + 2;
        _irc = result.Data;
        _ircValid = true;
    }

    /// <summary>
    /// A jump, a branch, an <c>RTE</c>/<c>RTR</c> or an exception entry does not carry the old words
    /// across the boundary: it refills the queue at its target, two fetches, which is what every
    /// handler entry in the corpus's traces shows (`r fc6 target`, `n 2`, `r fc6 target + 2`). The
    /// closing steps are for an instruction that fell through, so a refilled queue sets
    /// <see cref="_flowTransferred"/> and the loop skips them.
    /// </summary>
    async ValueTask RefillAsync(uint target)
    {
        _flowTransferred = true;
        _ird = await ReadProgramAsync(target);
        if (_busFaulted)
        {
            return;
        }

        _ir = await ReadProgramAsync(target + 2);
        _fetchStateAddress = target + 2;
        _ircValid = false;

        // the boundary convention: `_pc` is the address of the opcode the queue has fetched, and the
        // loop's own decode step is what advances it past that word
        _pc = target;
    }

    async ValueTask<uint> FetchLongAsync()
    {
        uint high = await FetchAsync();
        if (_busFaulted) return 0;
        uint low = await FetchAsync();
        return (high << 16) | low;
    }

    /// <summary>
    /// A plain <c>MOVE</c>'s absolute-long destination address, which the corpus places in a class of
    /// its own: the reference's <c>MOVE_bw_AbsoluteLong</c> / <c>MOVE_l_AbsoluteLong</c> state builds
    /// the pair from one read — the pop that takes the high word fetches the low one, and the low one
    /// is read out of the queue rather than popped — so the address costs one read and leaves the
    /// queue a word short (<see cref="_queueShort"/>, which the closing steps pay).
    /// <para>
    /// That single read is the whole of the 49-record residue: on a memory source the fault lands
    /// before the address's low word's refill, so the read is never issued and the frame saves the
    /// word before the address instead of the address's own high word. A source that took no bus
    /// cycle (a register or an immediate) is the other half of the class and is handled at the store.
    /// </para>
    /// </summary>
    async ValueTask<uint> FetchMoveAbsoluteLongAsync()
    {
        uint high = await FetchAsync();
        if (_busFaulted) return 0;

        uint low = _ir;
        _pc += 2;               // the low word is consumed here, not by a pop of its own
        _queueShort = true;
        return (high << 16) | low;
    }

    /// <summary>
    /// One read of the instruction stream at the fetch pointer, returned rather than queued: the
    /// closing steps use it when the queue holds an instruction's own consumed words (the
    /// absolute-long pair), so there is nothing to shift.
    /// </summary>
    async ValueTask<ushort> ReadQueueWordAsync()
    {
        uint address = _fetchPointer;
        _irAddress = address - 2;

        BusResult result = await Cycle(address, 1, false, 0, isProgram: true);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
            _faultIsFetch = true;
            return 0;
        }

        _fetchPointer = address + 2;
        return result.Data;
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

        // Cwik's prefetch initialisation: the first instruction's opcode and the word behind it, which
        // makes the reset sequence six bus cycles rather than the vector's four (ADR 0009 §5)
        _ird = await ReadProgramAsync(_pc);
        if (!_busFaulted)
        {
            _ir = await ReadProgramAsync(_pc + 2);
        }

        if (_busFaulted)
        {
            _halted = true;
            return;
        }

        _ircValid = false;
        _fetchPointer = _pc + 4;
        _fetchStateAddress = _pc + 2;
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

            // the opcode is in the decode register — the queue fetched it before the instruction
            // started, so decoding costs no bus cycle of its own
            ushort opcode = _ird;
            _pc += 2;
            _opcodeAddress = _pc - 2;
            _streamStart = _fetchPointer;
            _flowTransferred = false;
            _trailingFetched = false;
            _queueShort = false;
            _own = 0;
            _eaCharged = false;

            if (_busFaulted)
            {
                await TakeFaultAsync(_faultAddress, _addressError);
                _busFaulted = false;
                continue;
            }

            Instructions++;
            _opcode = opcode;
            Observer?.Invoke(Clock, opcode, _pc - 2);
            await ExecuteAsync(opcode);

            // the closing steps: an instruction that fell through fetches its last word here. A
            // faulting instruction keeps its trailing fetch only when the class puts the fetch before
            // the faulting write (a plain MOVE / MOVEM store) — every other fault cancels it, which is
            // why the corpus's traces show a differing fetch count per family
            if (!_flowTransferred)
            {
                await CloseInstructionAsync();
            }

            if (_busFaulted)
            {
                await TakeFaultAsync(_faultAddress, _addressError);
                _busFaulted = false;
                continue;
            }

            // the prefetch's own address error: the word the queue fetched after the instruction
            // landed on an odd program counter (a branch or a jump to an odd address)
            if ((_pc & 1) != 0)
            {
                _faultIsFetch = true;
                await TakeFaultAsync(_pc, true);
                continue;
            }

            await CheckInterruptAsync();
        }
    }

    async ValueTask WaitForStop()
    {
        // A stopped CPU leaves STOP only by *taking* an interrupt — never by any wake. Two samples per
        // level: one at the change, and, when the level is takeable but younger than the four-clock
        // window, one four clocks later. Without the second sample a STOP that wakes exactly on the
        // level's own record declines the interrupt for ever (the rule defers it to "one instruction
        // later", and a stopped CPU has no next instruction): the boot's first VBL wait, and later the
        // ACSI wait's `while (hz_200 < next_acsi_time) stop_until_interrupt();`, both froze there.
        while (true)
        {
            long seq = Engine.Log(Wires.Ipl).Count > 0 ? Engine.Log(Wires.Ipl)[^1].Seq : long.MinValue;
            int level = Engine.ValueAt(Wires.Ipl, Clock) & 0x7;
            bool takeable = level != 0 && (level > InterruptMask || level == 7);
            await WaitFor(takeable ? Clock + 4 * TicksPerCpuClock : long.MaxValue, Wires.Ipl, seq);

            _stopped = false;
            if (await CheckInterruptAsync())
            {
                return;
            }

            _stopped = true;
        }
    }

    // ---- interrupts and exceptions -------------------------------------------------------

    /// <summary>
    /// Takes the interrupt the <c>IPL</c> lanes present, if one is takeable. Returns whether an
    /// interrupt was actually entered — the STOP loop must stay stopped when this answers false.
    /// </summary>
    async ValueTask<bool> CheckInterruptAsync()
    {
        int level = Engine.ValueAt(Wires.Ipl, Clock) & 0x7;
        if (level == 0)
        {
            return false;
        }

        if (level <= InterruptMask && level != 7)
        {
            return false;
        }

        long changedAt = LastIplChangeAt(Clock);
        if (Clock - changedAt < 4 * TicksPerCpuClock)
        {
            return false;   // the pin changed too late: taken one instruction later, by the rule
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
        return true;
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

        // the entry's own clocks, before the frame is written: every entry in the corpus's traces is an
        // `n 4` there, then the frame, the vector, `n 2`, the handler's two fetches (M68000.Timing.cs)
        await ClocksAsync(4);

        ushort oldSr = _sr;
        _sr = (ushort)((_sr | 0x2000) & ~0x8000);
        if (level != 0)
        {
            _sr = (ushort)((_sr & ~0x0700) | (level << 8));   // the mask is I2–I0, bits 8–10
        }

        // the saved program counter: the address of the word in the queue for a group-0/2 frame (the
        // prefetch has run ahead, so it is not the instruction's own counter — ADR 0009 §4's revision
        // measured it on the corpus's traces and removed the old `pc − 2` / `pc − 4` arithmetic), and
        // the next instruction for every other frame
        bool group0 = vector == 2 || vector == 3;

        // a data fault saves the address of the word in IR; a fetch's own fault saves the word before
        // the one that failed (the fetch never completed, so the queue did not reach it)
        // a group-0 data fault saves the depth of the pipeline — MAME's `m_pc` (#76)
        uint returnPc = group0
            ? (_faultIsFetch ? _fetchPointer - 2 : _fetchStateAddress)
            : SavesInstructionAddress(vector) ? _opcodeAddress : _pc;

#if DEBUG
        // the frame's program counter, measured rather than assumed (#76): the MAME corpus decides it,
        // and these three quantities are the candidates a session tests it against — see cpu-tests --clocks
        LastFramePc = returnPc;
        LastFrameFetchPointer = _fetchPointer;
        LastFramePcCounter = _pc;
        LastFrameIrAddress = _irAddress;
        LastFrameRefetched = _fetchPointer != _streamStart;
        LastFrameTrailing = _trailingFetched;
        LastFrameVector = vector;
#endif

        if (group0)
        {
            // the seven-word frame, lowest address first: the status word, the access address, the
            // instruction, the status register, the program counter. The writes are in the
            // hardware's own order (Ijor's bus trace), which is why they do not ascend.
            ushort status = BusStatusWord(oldSr);
            uint frame = _ssp - 14;
            _ssp = frame;

            await WriteWordAsync(frame + 12, (ushort)returnPc);

            // the frame's status register carries only the bits a 68000 stores: the undefined ones
            // (7–5) read back as zero, and the corpus compares the frame's bytes (FINDINGS row 45)
            await WriteWordAsync(frame + 8, (ushort)(oldSr & DefinedSr));
            await WriteWordAsync(frame + 10, (ushort)(returnPc >> 16));
            await WriteWordAsync(frame + 6, _opcode);
            await WriteWordAsync(frame + 4, (ushort)(_faultAddress & 0xFFFF));
            await WriteWordAsync(frame, status);
            await WriteWordAsync(frame + 2, (ushort)(_faultAddress >> 16));
        }
        else
        {
            // the six-byte frame, lowest address first: status register, program counter high, low
            uint frame = _ssp - 6;
            _ssp = frame;
            await WriteWordAsync(frame + 4, (ushort)returnPc);
            await WriteWordAsync(frame + 2, (ushort)(returnPc >> 16));
            await WriteWordAsync(frame, (ushort)(oldSr & DefinedSr));
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

        // the handler's first two words: every entry in the corpus's traces is two program fetches,
        // with its own 2 clocks between them
        await ClocksAsync(2);
        await RefillAsync(newPc);
        _busFaulted = false;
    }

    async ValueTask TakeFaultAsync(uint address, bool addressError)
    {
        _busFaulted = false;   // the fault is being taken, not still pending
        _faultAddress = address;
        _addressError = false;
        await EnterExceptionAsync(addressError ? 3 : 2, 0);
        _faultIsFetch = false;
    }

    /// <summary>
    /// Which group-1/2 exceptions save the faulting <em>instruction's</em> address rather than the address
    /// past it: MAME's corpus measures the instruction's own word for the illegal lines (4, 10, 11) and
    /// the privilege violation (8), and the word past the instruction for `TRAP`, `TRAPV`, `CHK` and a
    /// divide by zero. Getting this wrong for `TRAP` is not a corpus failure but a **hang**: an `RTE`
    /// through the frame returns to the `TRAP` itself, and EmuTOS runs on `TRAP` (#76).
    /// </summary>
    static bool SavesInstructionAddress(int vector) => vector is 4 or 8 or 10 or 11;

    /// <summary>
    /// The group-0 frame's first word: the instruction being executed, its low five bits replaced by
    /// the access's own detail — read/write, program or data, supervisor or user (design/cpu-core.md
    /// §7). The corpus is the oracle for the bit layout, which the 68000 manuals leave implicit.
    /// </summary>
    ushort BusStatusWord(ushort statusBefore)
    {
        ushort status = (ushort)(_opcode & ~0x1Fu);

        if (_faultIsFetch)
        {
            // a read, in program space — and *not* 0x08: that bit is MAME's nested-fault marker
            // (`SSW_N`), set only on the frame's own writes, never on a fetch (microcode `bser1`
            // composes the word from `m_base_ssw`). Measured: RTS record 002, corpus 0x4E72 vs
            // 0x4E7A (research/mame-microcode-core.md §2).
            status |= 0x10 | 0x02;
        }
        else
        {
            // The corpus records the read bit for every instruction except a plain MOVE or a MOVEM
            // store writing to memory: its reference implementation classifies the access by the
            // instruction's own operand handling, not by the direction of this one cycle. The corpus
            // is the oracle here, and the difference is recorded in tests/680x0/README.md.
            bool plainMove = (_opcode >> 12) is 1 or 2 or 3;
            bool movemStore = (_opcode & 0xFF80) == 0x4880;
            if (!_faultWrite || !(plainMove || movemStore))
            {
                status |= 0x10;
            }

            status |= 0x01;                 // data space
        }

        if ((statusBefore & 0x2000) != 0)
        {
            status |= 0x04;                 // the access was made in supervisor mode
        }

        return status;
    }
}
