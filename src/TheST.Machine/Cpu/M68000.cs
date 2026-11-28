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

    // The register file, the pipeline cells and the ALU's latches live in M68000.Registers.cs —
    // storage and nothing else (ADR 0015 §4). What stays here is the prefetch queue's *bookkeeping*:
    // the words are `IRC`/`IR`/`IRD`, and these are the positions the core keeps about them.

    // ---- the prefetch queue's position (ADR 0009 §4) --------------------------------------
    // An instruction's extension words come out of the queue and what it fetches is its own word
    // count, so the queue's head trails the pipeline's position by a word. The position is **derived**
    // (`FetchPosition`/`BoundaryPosition`, M68000.ControlStores.cs) from the word the instruction
    // consumes next and the queue's fill; `_at` (Registers) is the address of the word in IR.

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

    // a control transfer's first extension word came out of the queue with no read behind it (a jump
    // abandons the stream it would refill), so the pipeline's position no longer trails `_au` by a word
    bool _freePop;

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

    /// <summary>
    /// The saved program counter a <b>fetch</b> fault puts in the group-0 frame, when the script has decided
    /// it. MAME's <c>m_pc</c> at that instant is the script's own business: the <c>b</c>/<c>malw3</c> states
    /// put the jump's target there (<c>BSR</c> — 1 271 records, <c>saved − opcode</c> = the displacement), the
    /// <c>bbci3</c> state leaves the boundary's word (<c>JMP</c> — 911 records, +2 on every shape, whatever
    /// the shape read to build its target). #76's rule (<c>_fetchPointer − 2</c>) stays the default because
    /// that is what a fault inside an instruction's own trailing fetch measures, where <c>m_pc</c> has not
    /// moved yet; a script that needs another number says so with <see cref="FrameSavesOnFetch"/>.
    /// </summary>
    uint _frameFetchPc;

    bool _frameFetchPcDeclared;

    /// <summary>
    /// The word a group-0 frame records as "the instruction" (MAME's <c>m_ird</c>). It is the opcode,
    /// except where the store state reloads <c>m_ird</c> from the queue: the word and long
    /// <c>MOVE</c> into <c>-(An)</c> (<c>rmmw1</c> does <c>m_ir = m_irc</c>, then <c>mmmw2</c> does
    /// <c>m_ird = m_ir</c>), so that frame saves the <b>queue head</b> — the word at the instruction's
    /// next address — where every other class saves the opcode (measured: 115 MOVE.w records).
    /// </summary>
    ushort _frameInstructionWord;

    bool _frameInstructionWordDeclared;

    // The counters, the observers and the per-record instruments are the emulator's interface, not
    // the part's: they live in M68000.Emulator.cs behind `Instruments` (ADR 0015 §6, #85).

    public M68000(Engine engine, int id)
        : base(engine, id, "CPU")
    {
        Emulator = new Instruments(this);
        Pads = new Pins(this);
    }

    // ---- registers -----------------------------------------------------------------------

    // ---- the programmer's model (ADR 0015 §1) ---------------------------------------------
    // Every member below is a *view* over the machine underneath and none owns a field of its own, so
    // there is one copy of the state and these are the manual's names for its doors (tutorial §1.1–§1.3).
    // A register is 32 bits (ADR 0015 §3): a word write leaves the high half standing, a byte write the
    // rest of the low half.

    public uint D0 { get => _d[0]; set => _d[0] = value; }
    public uint D1 { get => _d[1]; set => _d[1] = value; }
    public uint D2 { get => _d[2]; set => _d[2] = value; }
    public uint D3 { get => _d[3]; set => _d[3] = value; }
    public uint D4 { get => _d[4]; set => _d[4] = value; }
    public uint D5 { get => _d[5]; set => _d[5] = value; }
    public uint D6 { get => _d[6]; set => _d[6] = value; }
    public uint D7 { get => _d[7]; set => _d[7] = value; }

    public uint A0 { get => _a[0]; set => _a[0] = value; }
    public uint A1 { get => _a[1]; set => _a[1] = value; }
    public uint A2 { get => _a[2]; set => _a[2] = value; }
    public uint A3 { get => _a[3]; set => _a[3] = value; }
    public uint A4 { get => _a[4]; set => _a[4] = value; }
    public uint A5 { get => _a[5]; set => _a[5] = value; }
    public uint A6 { get => _a[6]; set => _a[6] = value; }

    /// <summary>A7 is the live stack pointer — <c>SSP</c> in supervisor mode, <c>USP</c> in user mode (§1.3).</summary>
    public uint A7
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

    /// <summary>
    /// PC is "the next instruction word to be fetched" (§1.4) — the address adder's value at a boundary.
    /// It is written only at one: an instruction's own prefetches move the counter inside the core.
    /// </summary>
    public uint PC { get => _au; set => _au = value; }

    /// <summary>SR, whole (§1.1); the bits the manual leaves undefined read back zero because nothing sets them.</summary>
    public ushort SR { get => _sr; set => _sr = value; }

    /// <summary>CCR — the low five bits of SR (§0.1, §1.2).</summary>
    public byte CCR { get => (byte)(_sr & 0x1F); set => _sr = (ushort)((_sr & ~0x1F) | (value & 0x1F)); }

    public uint USP { get => _usp; set => _usp = value; }
    public uint SSP { get => _ssp; set => _ssp = value; }

    bool Bit(int mask) => (_sr & mask) != 0;
    void SetBit(int mask, bool value) => _sr = (ushort)(value ? _sr | mask : _sr & ~mask);

    /// <summary>Trace (§1.2).</summary>
    public bool T { get => Bit(0x8000); set => SetBit(0x8000, value); }

    /// <summary>Supervisor (§1.2).</summary>
    public bool S { get => Bit(0x2000); set => SetBit(0x2000, value); }

    public bool I2 { get => Bit(0x0400); set => SetBit(0x0400, value); }
    public bool I1 { get => Bit(0x0200); set => SetBit(0x0200, value); }
    public bool I0 { get => Bit(0x0100); set => SetBit(0x0100, value); }

    public bool X { get => Bit(0x0010); set => SetBit(0x0010, value); }
    public bool N { get => Bit(0x0008); set => SetBit(0x0008, value); }
    public bool Z { get => Bit(0x0004); set => SetBit(0x0004, value); }
    public bool V { get => Bit(0x0002); set => SetBit(0x0002, value); }
    public bool C { get => Bit(0x0001); set => SetBit(0x0001, value); }

    /// <summary>The S bit as the core reads it everywhere (the manual's <c>S</c>, §1.2).</summary>
    bool Supervisor => S;

    /// <summary>The interrupt mask as a number — I2:I1:I0 (§1.2), for the level comparison only.</summary>
    int InterruptMask => (I2 ? 4 : 0) | (I1 ? 2 : 0) | (I0 ? 1 : 0);

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

        Emulator.InternalClocks += cpuClocks;
#if DEBUG
        Emulator.ClockObserver?.Invoke(cpuClocks);
#endif
        await WaitFor(Clock + cpuClocks * Instruments.TicksPerCpuClock, Wires.Answer, _answerSeq);
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

    /// <summary>
    /// The bus status of the last cycle the instruction attempted (<c>m_base_ssw</c>): program space
    /// for a fetch and for the operand read a PC-relative shape takes out of the instruction stream,
    /// data space for every other operand cycle. The group-0 frame records it, and it is <em>not</em>
    /// the same fact as <see cref="_faultIsFetch"/> — a faulted program-space operand read keeps the
    /// instruction's own saved counter (#78: the `(d16,PC)`/`(d8,PC,Xn)` class).
    /// </summary>
    bool _faultProgram;

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
    /// The access class of one bus cycle (tutorial §8.1's function-code table; MAME's <c>m_base_ssw</c>
    /// values <c>SSW_DATA</c>/<c>SSW_PROGRAM</c>/<c>SSW_CPU</c>). The die chooses it **per state**, and the
    /// bus's <c>FC0–FC2</c> follow the class — never the other way round: the class is why the pins read
    /// 6, 5, 2, 1 or 7, and it is the word a script's access names (#88, ADR 0015 §1).
    /// </summary>
    internal enum AccessClass
    {
        /// <summary><c>SSW_DATA</c> (`i` field clear): an operand, FC 5 in supervisor mode and FC 1 in user mode.</summary>
        Data,

        /// <summary><c>SSW_PROGRAM</c>: the instruction stream, FC 6 / FC 2 — a fetch or an operand read out of it.</summary>
        Program,

        /// <summary><c>SSW_CPU</c>: CPU space, FC 7 — the interrupt acknowledge's cycle, and nothing else here.</summary>
        CpuSpace,
    }

    /// <summary>
    /// The class's function code (tutorial §8.1): `FC2–FC0`. The supervisor bit is the mode the core is
    /// in at the cycle, so the same class yields 6/5 in supervisor mode and 2/1 in user mode.
    /// </summary>
    int FunctionCode(AccessClass access) => access switch
    {
        AccessClass.CpuSpace => 7,
        AccessClass.Program => Supervisor ? 6 : 2,
        _ => Supervisor ? 5 : 1,
    };

    /// <summary>
    /// One bus cycle (ADR 0009 §1): the assertion record, the subscription to the answer, the stack
    /// sample, the release. The core advances to the answering instant because the engine sets its
    /// clock there when it resumes it.
    /// <para>
    /// One bus cycle is also <em>one suspended frame</em> (#62): the fault bookkeeping and a fetch's
    /// <c>_au</c> advance live here, so the helpers below are forwarders and the frame a wrapper would
    /// have boxed does not exist. Same records, same instants, same order.
    /// </para>
    /// </summary>
    async ValueTask<BusResult> Cycle(uint address, int size, bool isWrite, ushort writeData,
                                     AccessClass access = AccessClass.Data)
    {
        bool isCpuSpace = access == AccessClass.CpuSpace;
        bool isProgram = access == AccessClass.Program;

        // an address error is not a bus cycle: a word access at an odd address never reaches the
        // wire, and the group-0 frame records what it was trying to reach (ADR 0009 §7)
        if (!isCpuSpace && size == 1 && (address & 1) != 0)
        {
            _faultAddress = address;
            _faultWrite = isWrite;
            _faultIsFetch = false;
            _addressError = true;
            _busFaulted = true;
            _faultProgram = isProgram;
            SettlePostIncrement(isWrite);

            // the abandoned cycle: the hardware asserts nothing, but an address error's length carries
            // the microcode's charge for it — MAME's `re`/`we` transaction (AS never asserted) is that
            // cycle, and every faulting record of every scripted family was short without it. The 4
            // here is the abort alone. The microcode also charges the aborting <em>state</em> its own 4,
            // so an aborted <b>read</b> costs 8 in total and an aborted write 4 — but the shape table
            // (`M68000.Timing.cs`) was measured on records that include that 4, so an unported family
            // already carries it in its row and would be double-charged. The extra 4 therefore lives in
            // the scripts (`ChargeAbortedReadAsync`, called by the operand blocks), i.e. in the families
            // whose clocks are the microcode's own; it is paid by the port, family by family.
            await ClocksAsync(4);

            return new BusResult(0, true, false);
        }

        long asserted = Clock;

        // the pads' buffers: AOB is the level the address lines take, DOB what the part drives for a
        // write (and nothing for a read, where the slave owns the lanes)
        LatchAob(address);
        _dob = isWrite ? writeData : (ushort)0;

        // the control word: `R/W`, the size, the lane, and the class's function code — §8.1's pins as
        // one record (the pins read it back; see `Pins`)
        ushort control = (ushort)(
            (isWrite ? 0x0100 : 0) |
            ((size == 0 ? 0 : 1) << 4) |
            ((address & 1) != 0 ? 1 : 0) |
            (FunctionCode(access) << 12));

        Write(asserted, Wires.Bus, 0xFF00, (ushort)(address >> 16));
        Write(asserted, Wires.Bus, 0xFFFE, (ushort)(address & 0xFFFE));
        Write(asserted, Wires.Bus, 0xFFFF, control);
        Write(asserted, Wires.Bus, 0xFFFF, isWrite ? writeData : (ushort)0);

        Emulator.BusCycles++;
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
        _dt = isWrite ? writeData : data;   // the last operand transferred, either direction

#if DEBUG
        Emulator.CycleObserver?.Invoke(isWrite, FunctionCode(access), address, isWrite ? writeData : data);
#endif

        // the release: AS high, and Z on the data lanes — a record of its own, and the lane that
        // tells a release from an assertion
        Write(answerInstant, Wires.Bus, 0x8000, 0x8000);

        if (faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
            _faultProgram = isProgram;
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
            _au += (uint)pcAdvance;
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
    /// is the hardware's, and it is what a MAME-true exception frame saves (the position the fetch
    /// moved to, `FetchPosition`).
    /// </summary>
    async ValueTask PrefetchAsync()
    {
        if (_busFaulted || _ircValid || _queueShort)
        {
            return;
        }

        uint address = FetchPosition;
        // `roml1` says `m_pc = m_au`: the die's adder holds the address it is about to read. On this
        // tree the adder is the consumption counter (it names the word the instruction takes next),
        // so the position is the derivation — `FetchPosition` — and the copy is `CopyAuToPc()`'s
        // statement with that value. Leaving the adder itself at the access is what the AU/PC split
        // still owes (#87's last step, bounded by the two measurements in the plan's §8).
        CopyAuToPc(address);
        BusResult result = await Cycle(address, 1, false, 0, AccessClass.Program);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
            _faultIsFetch = true;
            return;
        }

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
        _at = address - 2;

        BusResult result = await Cycle(address, 1, false, 0, AccessClass.Program);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
            _faultIsFetch = true;
            return 0;
        }

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
            uint address = FetchPosition;
            _at = address - 2;

            BusResult result = await Cycle(address, 1, false, 0, AccessClass.Program);
            if (result.Faulted)
            {
                _busFaulted = true;
                _faultAddress = address;
                _faultIsFetch = true;
                return 0;
            }

            _irc = result.Data;
            _ircValid = true;
        }

        ushort word = _ir;
        _ir = _irc;
        _ircValid = false;
        AuPlusTwo();
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

        // The closing pair, which is no script's: every instruction that falls through ends on it, and
        // NOP's whole listing is it (`scripts/microcode-walk.py --op 4e71`). `b` shifts the queue and
        // gives the frame its counter, `mmrw3` makes the instruction's one read. The absolute-long
        // `MOVE` destination above is the other branch, and its two reads are that class's own.
        //
        //   ours: the own work charged first (`ClocksAsync(_own)`) is not a state — it is #94's clock
        //   table's residue, spent before the read. `b`'s `m_au = m_au + 2` is the loop's decode step
        //   (`Run`'s `AuPlusTwo`), so it is not here. And the two queue copies are taken after the
        //   read because the read is what fills `_irc`: a script whose own state *is* the read calls
        //   this helper itself (`nnml1`/`morl1`, and the `MOVE` store that issues it before its
        //   write), and then the pair's read is already spent and only the copies run.
        //
        // b (363) + mmrw3 (34c)  m_aob = m_au; m_ir = m_irc; m_pc = m_au;
        //              m_ird = m_ir; read at m_aob                                                  0+4
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

        // the pair's prefetch-first, when the class ran it, has already paid one of the two. What the
        // closing steps read are the stream's own next two words: the class consumed two words with one
        // read (`_queueShort`), so the queue is a word short and `_au` names the next opcode itself.
        ushort first = _ircValid ? _irc : await ReadQueueWordAsync(_au);
        _ircValid = false;
        if (_busFaulted)
        {
            return;
        }

        ushort second = await ReadQueueWordAsync(_au + 2);
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

        uint address = FetchPosition;
        _at = address - 2;
        CopyAuToPc(address);                      // m_pc = m_au, the adder's position derived — see PrefetchAsync

        BusResult result = await Cycle(address, 1, false, 0, AccessClass.Program);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
            _faultIsFetch = true;
            return;
        }

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

        // `b`/`mmrw3` copy the adder into `m_pc` only after the refill's two reads have landed, so a
        // fault inside one saves the counter as it stood — the register's value, not the position.
        // A script whose own state is `m_pc = m_au` (`dbcc3`) has already said where it stood, and its
        // statement wins (RTS/RTR/RTE's odd return address: 3 066 records; DBcc's 632 the other way).
        if (!_frameFetchPcDeclared)
        {
            FrameSavesOnFetch(_pc);
        }

        _ird = await ReadProgramAsync(target);
        if (_busFaulted)
        {
            return;
        }

        _ir = await ReadProgramAsync(target + 2);
        _pc = target + 2;
        _ircValid = false;

        // the boundary convention: `_au` is the address of the opcode the queue has fetched, and the
        // loop's own decode step is what advances it past that word
        _au = target;
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
        AuPlusTwo();               // the low word is consumed here, not by a pop of its own
        _queueShort = true;
        return (high << 16) | low;
    }

    /// <summary>
    /// One read of the instruction stream at <paramref name="address"/>, returned rather than queued: a
    /// control transfer's later extension words (<c>jmal2</c>, read at the position) and the closing steps
    /// of an instruction whose queue holds its own consumed words (the absolute-long pair, read at the
    /// stream's own next two addresses) use it, so there is nothing to shift.
    /// </summary>
    async ValueTask<ushort> ReadQueueWordAsync(uint address)
    {
        _at = address - 2;

        BusResult result = await Cycle(address, 1, false, 0, AccessClass.Program);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
            _faultIsFetch = true;
            return 0;
        }

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
        _au = await ReadLongAsync(0x00000004);
        if (_busFaulted)
        {
            _halted = true;
            return;
        }

        // Cwik's prefetch initialisation: the first instruction's opcode and the word behind it, which
        // makes the reset sequence six bus cycles rather than the vector's four (ADR 0009 §5)
        _ird = await ReadProgramAsync(_au);
        if (!_busFaulted)
        {
            _ir = await ReadProgramAsync(_au + 2);
        }

        if (_busFaulted)
        {
            _halted = true;
            return;
        }

        _ircValid = false;
        _pc = _au + 2;
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
            AuPlusTwo();
            _opcodeAddress = _au - 2;
            _streamStart = FetchPosition;
            _flowTransferred = false;
            _trailingFetched = false;
            _queueShort = false;
            _freePop = false;
            _frameInstructionWordDeclared = false;
            _own = 0;
            _eaCharged = false;
            _faultProgram = false;
            _frameFetchPcDeclared = false;

            if (_busFaulted)
            {
                await TakeFaultAsync(_faultAddress, _addressError);
                _busFaulted = false;
                continue;
            }

            Emulator.Instructions++;
            _opcode = opcode;

            // the decode's own write: the three encoding fields into the FTU (`A2`/`A3`'s terms over
            // `IRD`), so every script below reads the field instead of re-shifting the opcode
            SetFtu(opcode);
            Emulator.Observer?.Invoke(Clock, opcode, _au - 2);
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

            // the boundary's own sample of the queue: `_au` is the next opcode's address and
            // `BoundaryPosition` the word past the one behind it, so the two name the pipeline's depth
            // for *every* record, not only the faulting ones (#87's derivation reads it)
#if DEBUG
            Emulator.LastFramePcCounter = _au;
            Emulator.LastFrameFetchPointer = BoundaryPosition;
            Emulator.LastFrameIrAddress = _at;
            Emulator.LastFrameTrailing = _trailingFetched;
            Emulator.LastFrameRefetched = BoundaryPosition != _streamStart;
            Emulator.LastFrameVector = 0;
#endif

            // the prefetch's own address error: the word the queue fetched after the instruction
            // landed on an odd program counter (a branch or a jump to an odd address)
            if ((_au & 1) != 0)
            {
                _faultIsFetch = true;
                await TakeFaultAsync(_au, true);
                continue;
            }

            // TPEND: the trace bit is read at the boundary, and the trace exception (vector 9) is owed
            // before any interrupt — the manual's order (§7)
            _tpend = T;
            if (_tpend)
            {
                await EnterExceptionAsync(9, 0);
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
            _inl = Engine.ValueAt(Wires.Ipl, Clock) & 0x7;
            int level = _inl;
            bool takeable = level != 0 && (level > InterruptMask || level == 7);
            await WaitFor(takeable ? Clock + 4 * Instruments.TicksPerCpuClock : long.MaxValue, Wires.Ipl, seq);

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
        // INL: the pins, latched at this boundary (the part holds them; the boundary is when we look)
        _inl = Engine.ValueAt(Wires.Ipl, Clock) & 0x7;
        int level = _inl;
        if (level == 0)
        {
            return false;
        }

        if (level <= InterruptMask && level != 7)
        {
            return false;
        }

        long changedAt = LastIplChangeAt(Clock);
        if (Clock - changedAt < 4 * Instruments.TicksPerCpuClock)
        {
            return false;   // the pin changed too late: taken one instruction later, by the rule
        }

        Emulator.InterruptsTaken++;

        // the acknowledge is a CPU-space cycle: FC=111, A3–A1 = level
        int vector = 24 + level;
        BusResult acknowledge = await Cycle(0xFFFFFFF0u | ((uint)level << 1), 1, false, 0, AccessClass.CpuSpace);
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
        Emulator.Exceptions++;
        VectorToFtu(vector);    // `tvn_to_ftu`: the entry's vector lands in the field register first
        _tpend = false;         // the exception owed is taken; the trace bit itself is the handler's to clear

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

        // a data fault saves the depth of the pipeline — MAME's `m_pc` (#76) — and a fetch fault the word
        // before the one that failed, unless the script says where `m_pc` stood at that instant
        // (`FrameSavesOnFetch`: a jump's target, or the boundary's word)
        uint returnPc = group0
            ? _faultIsFetch
                ? _frameFetchPcDeclared ? _frameFetchPc : _au
                : _pc
            : SavesInstructionAddress(vector) ? _opcodeAddress : _au;

#if DEBUG
        // the frame's program counter, measured rather than assumed (#76): the MAME corpus decides it,
        // and these three quantities are the candidates a session tests it against — see cpu-tests --clocks
        Emulator.LastFramePc = returnPc;
        Emulator.LastFrameFetchPointer = FetchPosition;
        Emulator.LastFramePcCounter = _au;
        Emulator.LastFrameIrAddress = _at;
        Emulator.LastFrameRefetched = FetchPosition != _streamStart;
        Emulator.LastFrameTrailing = _trailingFetched;
        Emulator.LastFrameVector = vector;
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
            await WriteWordAsync(frame + 6, _frameInstructionWordDeclared ? _frameInstructionWord : _opcode);
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

        uint newPc = await ReadLongAsync((uint)(FtuVector * 4));
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
    /// `SSW` for one access: the instruction being executed, its low five bits replaced by the access's
    /// own detail — read/write, program or data, supervisor or user (design/cpu-core.md §7). The corpus
    /// is the oracle for the bit layout, which the 68000 manuals leave implicit; the bus cycle is the
    /// only writer, so the latch and this composition cannot disagree.
    /// </summary>
    // SSW's bits, as the die names them (MAME's `SSW_*`; the low five of a frame's first word carry
    // the class the faulting access was in, research/mame-microcode-core.md §2).
    const ushort SswData = 0x01;            // SSW_DATA — an operand access
    const ushort SswProgram = 0x02;         // SSW_PROGRAM — the instruction stream
    const ushort SswSupervisor = 0x04;      // SSW_S — the access was made in supervisor mode
    const ushort SswNested = 0x08;          // SSW_N — the nested-fault marker, never a fetch's
    const ushort SswRead = 0x10;            // SSW_R — the access was a read

    ushort ComposeSsw(ushort statusBefore)
    {
        ushort status = (ushort)((_frameInstructionWordDeclared ? _frameInstructionWord : _opcode) & ~0x1Fu);

        if (_faultIsFetch || _faultProgram)
        {
            // a read, in program space — and *not* `SswNested`: that bit is MAME's nested-fault marker,
            // set only on the frame's own writes, never on a fetch (microcode `bser1` composes the word
            // from `m_base_ssw`). Measured: RTS record 002, corpus 0x4E72 vs 0x4E7A
            // (research/mame-microcode-core.md §2).
            status |= SswRead | SswProgram;
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
                status |= SswRead;
            }

            status |= SswData;              // the operand space
        }

        if ((statusBefore & 0x2000) != 0)
        {
            status |= SswSupervisor;        // the access was made in supervisor mode
        }

        return status;
    }

    /// <summary>
    /// The group-0 frame's first word: the latch, which the last bus cycle composed (see
    /// <see cref="ComposeSsw"/>). `statusBefore` is kept because the caller has it and #93 will read the
    /// mode from the frame's own order; the latch already carries it.
    /// </summary>
    ushort BusStatusWord(ushort statusBefore)
    {
        // the latch is written here today. The die writes SSW per microinstruction (#93 owns moving
        // that write site); what this ticket needed was for the word to *be* a latch the frame reads,
        // and for the rule to have one home instead of being spelled inside the frame's builder.
        _ssw = ComposeSsw(statusBefore);
        return _ssw;
    }
}
