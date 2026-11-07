// ============================================================================================
// The ST — 68000 core, structural sketch (ticket #9). NOT a decision, not compilable as-is.
// Registered 2026-11-06 so the next session can find it; the prose that goes with it is
// design/cpu-core.md, the evidence research/cpu-prefetch-and-interrupt-sampling.md.
//
// Vocabulary follows GLOSSARY.md: the clock is ticks of the 32 MHz oscillator; a wire is a port
// group; a bus cycle is the exchange; "bus access" as a name for ownership/service is avoided.
// ============================================================================================

namespace TheST.Cpu;

// ---------------------------------------------------------------------------------------------
// The two value types that cross the chip boundary. No `cycles` field anywhere: the answering
// instant IS the cost (ticket #9, Q1).
// ---------------------------------------------------------------------------------------------
public enum FunctionCode { UserData = 1, UserProgram = 2, UserCpuSpace = 3, SupervisorData = 5, SupervisorProgram = 6, SupervisorCpuSpace = 7 }

public readonly record struct BusCycle(
    uint Address, int Size, FunctionCode Fc, bool IsWrite, bool IsFetch, bool IsReadModifyWrite);

public readonly record struct BusAnswer(ushort Data, bool Faulted, bool VectorProvided);

public enum Done { Ok, Faulted }

// ---------------------------------------------------------------------------------------------
// Debug instrumentation. Two different instruments, keep them apart:
//   * ICpuObserver  — per-event callbacks, DEBUG BUILDS ONLY, for breakpoints and tracing.
//   * internal register log — anchors only, always on, part of the digest (ADR 0008 §6).
// An observer may read the machine and never write it: every parameter is a copy.
// ---------------------------------------------------------------------------------------------
public enum Register : byte { D0, D1, D2, D3, D4, D5, D6, D7, A0, A1, A2, A3, A4, A5, A6, A7, PC, SR, USP, SSP, IRA, IRB, IRC, IRD }

public interface ICpuObserver
{
    void OnInstructionStart(long t, uint pc, ushort opcode);
    void OnRegisterWrite(long t, Register register, uint oldValue, uint newValue);
    void OnBusCycle(long t, in BusCycle cycle);
    void OnBusAnswer(long t, in BusAnswer answer);            // also where an address breakpoint lives
    void OnInterruptSampled(long t, int level, long levelChangedAt, ushort sr);
    void OnInterruptTaken(long t, int level, int vector);
    void OnExceptionEntry(long t, int vector);
    void OnHalted(long t, string reason);
}

// =============================================================================================
// The chip. One instance per machine. Its clock is its own; nobody reads its state directly.
// =============================================================================================
public sealed partial class M68000
{
    // ---- clock ------------------------------------------------------------------------------
    // 32 MHz oscillator divided by 4 is the CPU's 8 MHz clock: one CPU clock = 4 ticks.
    const int TicksPerCpuClock = 4;
    public long Clock { get; private set; }                  // this chip's own clock, in ticks

    /// Internal work between bus cycles. The only way the core moves time itself.
    void AdvanceCpuClocks(int cpuClocks) => Clock += cpuClocks * TicksPerCpuClock;

    // ---- registers, as readable and breakpointable properties ---------------------------------
    readonly uint[] _data = new uint[8];
    readonly uint[] _address = new uint[8];
    uint _pc;
    ushort _sr;
    uint _userStack, _supervisorStack;

    public ICpuObserver? Observer { get; set; }              // null in release

    void SetRegister(Register register, ref uint slot, uint value)
    {
        uint old = slot;
        slot = value;
        Observer?.OnRegisterWrite(Clock, register, old, value);   // hook: per-write, debug only
    }

    public uint D0 { get => _data[0]; set => SetRegister(Register.D0, ref _data[0], value); }
    public uint D1 { get => _data[1]; set => SetRegister(Register.D1, ref _data[1], value); }
    public uint D2 { get => _data[2]; set => SetRegister(Register.D2, ref _data[2], value); }
    public uint D3 { get => _data[3]; set => SetRegister(Register.D3, ref _data[3], value); }
    public uint D4 { get => _data[4]; set => SetRegister(Register.D4, ref _data[4], value); }
    public uint D5 { get => _data[5]; set => SetRegister(Register.D5, ref _data[5], value); }
    public uint D6 { get => _data[6]; set => SetRegister(Register.D6, ref _data[6], value); }
    public uint D7 { get => _data[7]; set => SetRegister(Register.D7, ref _data[7], value); }

    // A registers are 32-bit; a word write sign-extends, and a byte write is an address error.
    public uint A0 { get => _address[0]; set => SetRegister(Register.A0, ref _address[0], value); }
    public uint A1 { get => _address[1]; set => SetRegister(Register.A1, ref _address[1], value); }
    public uint A2 { get => _address[2]; set => SetRegister(Register.A2, ref _address[2], value); }
    public uint A3 { get => _address[3]; set => SetRegister(Register.A3, ref _address[3], value); }
    public uint A4 { get => _address[4]; set => SetRegister(Register.A4, ref _address[4], value); }
    public uint A5 { get => _address[5]; set => SetRegister(Register.A5, ref _address[5], value); }
    public uint A6 { get => _address[6]; set => SetRegister(Register.A6, ref _address[6], value); }
    public uint A7
    {
        get => Supervisor ? _supervisorStack : _userStack;
        set { if (Supervisor) SetRegister(Register.A7, ref _supervisorStack, value); else SetRegister(Register.A7, ref _userStack, value); }
    }

    public uint PC { get => _pc; set => SetRegister(Register.PC, ref _pc, value); }
    public ushort SR { get => _sr; set => SetRegister(Register.SR, ref _sr, value); }

    // Flag and mode helpers. SR is logged at anchors, never per instruction (see LogAnchor).
    public bool Supervisor => (_sr & 0x2000) != 0;
    public int InterruptMask => _sr & 0b111;
    public void LogAnchor(string what) { /* internal register log, ADR 0008 §6 */ }

    // ---- the bus: the only place in the core that suspends -----------------------------------
    // Q1's shape: one record out (the lanes we drive), one subscription to the answer, the data
    // read resolved on the stack, then our release as a record of its own.
    async ValueTask<BusAnswer> RunBusCycleAsync(BusCycle cycle)
    {
        long asserted = Clock;
        Observer?.OnBusCycle(asserted, cycle);

        DriveBus(asserted, cycle);                       // Write(t, bus, mask, value): no suspension
        await WaitForAnswerAsync();                      // ReadNext: Memory picks the instant; Clock := t_answer
        // Running again at the answering instant: data and VPA were appended before the answer
        // record, so both samples below resolve on the stack without a queue entry.
        ushort data = cycle.IsWrite ? (ushort)0 : SampleDataWires();
        bool faulted = SampleBusError();
        bool vectorProvided = cycle.Fc == FunctionCode.SupervisorCpuSpace && !SampleVpa();

        ReleaseBus();                                    // AS high, Z on the data lanes — a record too

        var answer = new BusAnswer(data, faulted, vectorProvided);
        Observer?.OnBusAnswer(Clock, answer);
        return answer;
    }

    extern void DriveBus(long t, in BusCycle cycle);
    extern void ReleaseBus();
    extern ushort SampleDataWires();
    extern bool SampleBusError();
    extern bool SampleVpa();
    extern ValueTask WaitForAnswerAsync();
    extern (int Level, long ChangedAt) SampleInterruptWires(long t);   // a log query, not a new verb

    // ---- prefetch: three registers, and one rule ----------------------------------------------
    // Documented (Cwik 2005, see research/cpu-prefetch-and-interrupt-sampling.md §1):
    //   IRD is the decode register, IR and IRC are the two-word queue.
    //   Every instruction ends with the same three steps: IRC -> IR, fetch a new word -> IRC,
    //   IR -> IRD. The fetch a NOP performs is at PC+4 (two words above the NOP).
    //   An instruction performs exactly as many fetch cycles as it has words.
    // There is NO `pendingFetch` flag: no verb lets the core leave a read outstanding, so the queue
    // only ever holds completed words. The fetch pointer runs ahead of PC by the queued words.
    ushort _ird;                                        // decode register (architectural: saved on a bus error)
    ushort _ir;                                         // queue word 1
    ushort _irc;                                        // queue word 2
    uint _fetchPointer;

    async ValueTask<ushort> FetchInstructionWordAsync(uint address)
    {
        var answer = await RunBusCycleAsync(new BusCycle(address, 2,
            Supervisor ? FunctionCode.SupervisorProgram : FunctionCode.UserProgram,
            IsWrite: false, IsFetch: true, IsReadModifyWrite: false));
        if (answer.Faulted) { _fault = new FaultRecord(address, SR, IsFetch: true); return 0; }
        _fetchPointer = address + 2;
        return answer.Data;
    }

    /// The three closing steps — and the per-instruction suspension point of a register-only opcode.
    async ValueTask EndOfInstructionAsync()
    {
        _ir = _irc;                                         // 1. IRC -> IR
        _irc = await FetchInstructionWordAsync(_fetchPointer);   // 2. new word -> IRC (a bus cycle)
        _ird = _ir;                                         // 3. IR -> IRD
        LogAnchor("IRD");                                   // anchor: the opcode boundary is meaningful
    }

    /// Extension words come out of the queue; the fetch they displace is issued by
    /// EndOfInstructionAsync, which is why the fetch count stays equal to the word count.
    ushort TakeExtensionWord() { ushort word = _ir; _ir = _irc; return word; }

    /// Where the trailing prefetch sits relative to a write is the one observable ordering
    /// difference between instructions (self-modifying code). Documented as three classes.
    public enum PrefetchClass { Zero, One, Two }

    // ---- reset: the machine's first cycle ------------------------------------------------------
    /// Documented (UM §6.3.1): supervisor, trace off, mask 7, no PC/SR stacked; SSP from the first
    /// two words of vector 0, PC from the last two. Then Cwik's four-step prefetch initialisation.
    /// Six bus cycles before the first instruction runs, and not one of them is a shortcut.
    async ValueTask ResetAsync()
    {
        SR = 0x2700;                                        // supervisor, trace cleared, mask 7
        _supervisorStack = await ReadLongAsync(0x0000_0000);
        PC = await ReadLongAsync(0x0000_0004);
        _fetchPointer = PC;
        _irc = await FetchInstructionWordAsync(_fetchPointer);   // 1. load IRC
        _ir = _irc;                                             // 2. IRC -> IR
        _irc = await FetchInstructionWordAsync(_fetchPointer);   // 3. reload IRC
        _ird = _ir;                                             // 4. IR -> IRD
        LogAnchor("SR");
    }

    async ValueTask<uint> ReadLongAsync(uint address)
    {
        var high = await RunBusCycleAsync(new BusCycle(address, 2, FunctionCode.SupervisorData, false, false, false));
        var low = await RunBusCycleAsync(new BusCycle(address + 2, 2, FunctionCode.SupervisorData, false, false, false));
        return ((uint)high.Data << 16) | low.Data;
    }
}

// =============================================================================================
// Interrupt acceptance. Two crossings upstream are not our business: the MFP raises IRQ into
// Memory, Memory arbitrates (VBL 4 / HBL 2 / MFP 6) and drives IPL0-2. We see one level.
// =============================================================================================
public sealed partial class M68000
{
    int _sampledLevel;
    long _levelChangedAt;
    /// Inferred, not documented (WinUAE cpuipldelay4 / cpuipldelay2): a level asserted at least
    /// 4 CPU clocks before the end of the instruction is taken at that boundary; a later change is
    /// taken after the following instruction.
    const int InterruptStabilisationCpuClocks = 4;

    /// Documented: the pins are latched during the instruction's LAST BUS CYCLE (UM Figure 5-11),
    /// and pending interrupts are detected between instruction executions (UM §6.3.2). So this runs
    /// immediately after the last bus cycle of the instruction, at its answering instant.
    void SampleInterruptWiresHere()
    {
        (_sampledLevel, _levelChangedAt) = SampleInterruptWires(Clock);
        Observer?.OnInterruptSampled(Clock, _sampledLevel, _levelChangedAt, SR);
    }

    bool Accepts(int level) => level != 0 && (level == 7 || level > InterruptMask);

    /// The 4-clock window is why an interrupt legitimately lands one instruction late. There is no
    /// pin history register: the wire's log already knows when the level changed (ADR 0004).
    bool TakenAtThisBoundary() =>
        Accepts(_sampledLevel) &&
        Clock - _levelChangedAt >= InterruptStabilisationCpuClocks * TicksPerCpuClock;

    /// The acknowledge is a real CPU-space cycle: FC=111, A3-A1 = level, A23-A4 = ones. The device
    /// answers with a vector on D0-D7 or by asserting VPA (autovector = 24 + level). A BERR here is
    /// a spurious interrupt (vector 24), not a bus error.
    async ValueTask<int> AcknowledgeAsync(int level)
    {
        var answer = await RunBusCycleAsync(new BusCycle(
            Address: 0xFFFF_FFF0u | ((uint)level << 1), Size: 2, Fc: FunctionCode.SupervisorCpuSpace,
            IsWrite: false, IsFetch: false, IsReadModifyWrite: false));

        int vector = answer.VectorProvided ? answer.Data & 0xFF : 24 + level;
        if (answer.Faulted) vector = 24;                    // spurious interrupt vector
        Observer?.OnInterruptTaken(Clock, level, vector);
        return vector;
    }
}

// =============================================================================================
// Exception entry as a real operation: a phase script with the bus work in it (Moira's shape; the
// order is UM Figure 5-11's, which puts the acknowledge BETWEEN the PCL stack write and the rest).
// Level 2 walks this array; Level 1 sums its clocks.
// =============================================================================================
public enum EntryStepKind { InternalClocks, PushWord, Acknowledge, ReadVector }
public enum FrameWord { ProgramCounterLow, ProgramCounterHigh, StatusRegister, SpecialStatusWord, FaultAddress, FunctionCode, DecodeRegister }

public readonly record struct EntryStep(EntryStepKind Kind, int Argument);

public sealed partial class M68000
{
    async ValueTask EnterExceptionAsync(int vector, int level)
    {
        bool group0 = vector < 8;
        Observer?.OnExceptionEntry(Clock, vector);

        SR = (ushort)((SR | 0x2000) & ~0x8000);              // supervisor, trace cleared
        if (level != 0) SR = (ushort)((SR & ~0b111) | level); // mask raised BEFORE the acknowledge
        LogAnchor("SR");

        foreach (var step in EntryScript(vector, level))
        {
            switch (step.Kind)
            {
                case EntryStepKind.InternalClocks:
                    AdvanceCpuClocks(step.Argument);
                    break;

                case EntryStepKind.PushWord:
                    if (await PushFrameWordAsync((FrameWord)step.Argument)) return;
                    break;

                case EntryStepKind.Acknowledge:
                    vector = await AcknowledgeAsync(level);
                    break;

                case EntryStepKind.ReadVector:
                    var entry = await RunBusCycleAsync(new BusCycle((uint)(vector * 4), 4,
                        FunctionCode.SupervisorData, IsWrite: false, IsFetch: false, IsReadModifyWrite: false));
                    if (entry.Faulted) { Halt(group0 ? "double fault" : "bus error fetching vector"); return; }
                    PC = ((uint)entry.Data << 16) | entry.Data;   // placeholder: the vector is a longword read
                    break;
            }
        }

        _ = group0;
    }

    /// One frame word, at the SSP, as a bus cycle a device can observe. A fault here is a double
    /// fault for a group-0 script (the 68000 halts): the rule, not a special case.
    async ValueTask<bool> PushFrameWordAsync(FrameWord word)
    {
        ushort value = word switch
        {
            FrameWord.ProgramCounterLow => (ushort)PC,
            FrameWord.ProgramCounterHigh => (ushort)(PC >> 16),
            FrameWord.StatusRegister => SR,
            FrameWord.DecodeRegister => _ird,
            _ => 0,
        };
        A7 -= 2;
        var answer = await RunBusCycleAsync(new BusCycle(A7, 2, FunctionCode.SupervisorData, IsWrite: true, IsFetch: false, IsReadModifyWrite: false));
        if (!answer.Faulted) return false;
        Halt("double fault");
        return true;
    }

    /// Interrupt entry order, documented in UM Figure 5-11: STACK PCL (SSP) -> IACK CYCLE ->
    /// rest of the frame -> VECTOR FETCH. Group 0 stacks its seven-word frame, no acknowledge.
    static EntryStep[] EntryScript(int vector, int level) =>
        level != 0
            ? [ new(EntryStepKind.InternalClocks, 6),
                new(EntryStepKind.PushWord, (int)FrameWord.ProgramCounterLow),
                new(EntryStepKind.PushWord, (int)FrameWord.ProgramCounterHigh),
                new(EntryStepKind.PushWord, (int)FrameWord.StatusRegister),
                new(EntryStepKind.Acknowledge, level),
                new(EntryStepKind.ReadVector, vector) ]
            : [ new(EntryStepKind.InternalClocks, 4),
                new(EntryStepKind.PushWord, (int)FrameWord.ProgramCounterLow),
                new(EntryStepKind.PushWord, (int)FrameWord.ProgramCounterHigh),
                new(EntryStepKind.PushWord, (int)FrameWord.StatusRegister),
                new(EntryStepKind.ReadVector, vector) ];
}

// =============================================================================================
// LEVEL 1 — the instruction/interrupt machine. The chip's endless coroutine; one phase field.
// =============================================================================================
public enum Phase { Reset, Decode, Execute, EndOfInstruction, CheckInterrupt, Halted }

public sealed partial class M68000
{
    Phase _phase = Phase.Reset;
    OpMachine _op = NoOp.Instance;

    public async ValueTask RunChipAsync()               // the coroutine the scheduler resumes
    {
        while (_phase != Phase.Halted)
        {
            switch (_phase)
            {
                case Phase.Reset:
                    await ResetAsync();                   // six bus cycles, then the first instruction
                    _phase = Phase.Decode;
                    break;

                case Phase.Decode:
                    // IRD already holds the opcode: the fetch that produced it belonged to the
                    // previous instruction, which is the point of the prefetch queue.
                    Observer?.OnInstructionStart(Clock, PC - 4, _ird);
                    _op = OpTable[_ird];
                    _phase = Phase.Execute;
                    break;

                case Phase.Execute:
                    if (await _op.RunAsync(this) == Done.Faulted) { _phase = Phase.Exception; break; }
                    _phase = Phase.EndOfInstruction;
                    break;

                case Phase.EndOfInstruction:
                    await _op.TrailingPrefetchAsync(this);     // class 0 / 1 / 2 placement
                    SampleInterruptWiresHere();                // LAST BUS CYCLE of the instruction
                    _phase = Phase.CheckInterrupt;
                    break;

                case Phase.CheckInterrupt:
                    if (TakenAtThisBoundary())
                    {
                        int vector = await AcknowledgeAsync(_sampledLevel);
                        await EnterExceptionAsync(vector, _sampledLevel);
                    }
                    _phase = Phase.Decode;
                    break;

                case Phase.Exception:
                    await EnterExceptionAsync(_fault!.Vector, 0);
                    _fault = null;
                    _phase = Phase.Decode;
                    break;
            }
        }
    }
}

// =============================================================================================
// LEVEL 2 — the instruction-specific machine. It names operands; it never emits a bus cycle.
// =============================================================================================
public abstract class OpMachine
{
    public abstract ValueTask<Done> RunAsync(M68000 cpu);
    /// The trailing prefetch belongs to the instruction, but its placement is the class's, not
    /// the opcode's: one call site, three behaviours.
    public virtual ValueTask TrailingPrefetchAsync(M68000 cpu) => cpu.EndOfInstructionAsync();
}

public sealed class NoOp : OpMachine
{
    public static readonly NoOp Instance = new();
    public override ValueTask<Done> RunAsync(M68000 cpu) => ValueTask.FromResult(Done.Ok);
}

/// MOVE: two operands, one internal step between them. Note what is absent: no cycle count, no
/// address computation, no bus record. The addressing machine owns all three.
public sealed class MoveOp : OpMachine
{
    public static readonly MoveOp Instance = new();

    public override async ValueTask<Done> RunAsync(M68000 cpu)
    {
        var source = await cpu.Address.ReadAsync(cpu, cpu.SourceOperand);
        if (source.Faulted) return Done.Faulted;

        cpu.AdvanceCpuClocks(2);                          // internal cycles: charged here, returned to nobody

        if (await cpu.Address.WriteAsync(cpu, cpu.DestinationOperand, source.Value) == Done.Faulted)
            return Done.Faulted;

        cpu.SetLogicFlags(source.Value, cpu.Size);        // flags are part of SR; SR is an anchor
        cpu.LogAnchor("SR");
        return Done.Ok;
    }
}

/// JSR: the stack write is a predecrement operand on A7, not instruction-machine code. This is the
/// rule that keeps bus requests out of this level.
public sealed class JsrOp : OpMachine
{
    public static readonly JsrOp Instance = new();

    public override async ValueTask<Done> RunAsync(M68000 cpu)
    {
        cpu.AdvanceCpuClocks(2);
        if (await cpu.Address.WriteAsync(cpu, Operand.PreDecrement(7, 4), cpu.PC) == Done.Faulted)
            return Done.Faulted;
        cpu.PC = await cpu.Address.ResolveTargetAsync(cpu);
        return Done.Ok;
    }
}

// =============================================================================================
// LEVEL 3 — the addressing machine. One state field, one exit, the operand's bus cycle.
// The register number is its OWN field: on the 68000, mode and register are separate 3-bit
// fields of the opcode, so `Dn` is a mode and `n` is a register — never the same value.
// =============================================================================================
public enum AddressingMode : byte
{
    DataRegister, AddressRegister, Indirect, PostIncrement, PreDecrement,
    Displacement, Indexed, AbsoluteShort, AbsoluteLong, PcRelative, Immediate,
}

public readonly record struct Operand(AddressingMode Mode, byte RegisterIndex, int Size)
{
    public static Operand PreDecrement(byte register, int size) => new(AddressingMode.PreDecrement, register, size);
}

public readonly record struct OperandValue(uint Value, bool Faulted);

public sealed partial class AddressingMachine
{
    public async ValueTask<OperandValue> ReadAsync(M68000 cpu, Operand operand)
    {
        // register-direct modes produce the operand with no bus cycle and no time at all
        switch (operand.Mode)
        {
            case AddressingMode.DataRegister:
                return new OperandValue(cpu.ReadDataRegister(operand.RegisterIndex, operand.Size), false);
            case AddressingMode.AddressRegister:
                return new OperandValue(cpu.ReadAddressRegister(operand.RegisterIndex), false);
            case AddressingMode.Immediate:
                return new OperandValue(await cpu.TakeImmediateAsync(operand.Size), false);
        }

        uint address = await EffectiveAddressAsync(cpu, operand);
        var answer = await cpu.RunBusCycleAsync(new BusCycle(address, operand.Size, cpu.DataFunctionCode,
            IsWrite: false, IsFetch: false, IsReadModifyWrite: false));
        return new OperandValue(answer.Data, answer.Faulted);
    }

    public async ValueTask<Done> WriteAsync(M68000 cpu, Operand operand, uint value)
    {
        switch (operand.Mode)
        {
            case AddressingMode.DataRegister: cpu.WriteDataRegister(operand.RegisterIndex, operand.Size, value); return Done.Ok;
            case AddressingMode.AddressRegister: cpu.WriteAddressRegister(operand.RegisterIndex, operand.Size, value); return Done.Ok;
        }

        uint address = await EffectiveAddressAsync(cpu, operand);
        var answer = await cpu.RunBusCycleAsync(new BusCycle(address, operand.Size, cpu.DataFunctionCode,
            IsWrite: true, IsFetch: false, IsReadModifyWrite: false));
        return answer.Faulted ? Done.Faulted : Done.Ok;
    }

    async ValueTask<uint> EffectiveAddressAsync(M68000 cpu, Operand operand)
    {
        switch (operand.Mode)
        {
            case AddressingMode.Indirect:
                return cpu.A(operand.RegisterIndex);
            case AddressingMode.PostIncrement:
                uint before = cpu.A(operand.RegisterIndex);
                cpu.A(operand.RegisterIndex, before + (uint)operand.Size);
                return before;
            case AddressingMode.PreDecrement:
                uint after = cpu.A(operand.RegisterIndex) - (uint)operand.Size;
                cpu.A(operand.RegisterIndex, after);
                return after;
            case AddressingMode.Displacement:
                cpu.AdvanceCpuClocks(2);                                    // the extension word is internal work
                return cpu.A(operand.RegisterIndex) + (uint)(short)cpu.TakeExtensionWord();
            case AddressingMode.AbsoluteShort:
                return (uint)(short)cpu.TakeExtensionWord();
            case AddressingMode.AbsoluteLong:
                return ((uint)cpu.TakeExtensionWord() << 16) | cpu.TakeExtensionWord();
            case AddressingMode.PcRelative:
                return cpu.PC + (uint)(short)cpu.TakeExtensionWord();
            default:
                throw new NotSupportedException($"mode {operand.Mode} has no effective address");
        }
    }
}

// =============================================================================================
// Pausing, breakpoints and single-stepping
//   * A breakpoint is NOT a suspension in the model. The chip coroutine is simply not resumed: the
//     host stops popping the queue at a batch boundary (one instant, fully drained). Nothing in the
//     model changes, so no digest can differ.
//   * "Instruction starts", "register changed", "bus cycle", "bus answer", "interrupt taken" are
//     the ICpuObserver hooks above. "Address read/written" is a predicate on OnBusCycle.
//   * Retrospective tracing needs no hook at all: every bus cycle is already a record in the wire
//     log, with its instant (ADR 0004) — that is what the UI draws.
//   * Single-step = advance exactly one batch. Stepping *inside* an instruction means stepping one
//     bus cycle, which is one batch too, because a bus cycle is an exchange between instants.
// =============================================================================================
