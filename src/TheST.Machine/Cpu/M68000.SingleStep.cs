namespace TheST.Machine.Cpu;

/// <summary>
/// The single-step seam (ticket #34): load an architectural state, execute exactly one instruction,
/// read the state back. It exists for the vendored 680x0 corpus (<c>tests/680x0/</c>) and for nothing
/// else — <c>boot</c> never uses it, and the machine's own loop (<see cref="Launch"/>) resets the chip
/// and samples interrupts instead.
///
/// <para>
/// The seam adds no timing rule and no record: a step fetches its words through the same bus leaf the
/// machine uses, so its cost is still whichever answer instants its accesses land on. What it does not
/// do is the two things the machine's loop does around an instruction — interrupt sampling and the
/// STOP wait — because a corpus test is one instruction in isolation.
/// </para>
/// </summary>
public sealed partial class M68000
{
    /// <summary>
    /// The state a corpus test hands the core, and reads back: exactly the 680x0 corpus's shape, plus
    /// the two words the corpus's <c>prefetch</c> field gives the core already fetched — which is the
    /// whole point of ADR 0009 §4's queue (a test starts with its opcode <em>and</em> the word behind it
    /// in the instruction stream, at <c>pc</c> and <c>pc + 2</c>).
    /// </summary>
    public readonly record struct SingleStepState(
        uint[] D,
        uint[] A,
        uint Usp,
        uint Ssp,
        ushort Sr,
        uint Pc,
        ushort Prefetch0,
        ushort Prefetch1);

    /// <summary>What the core looks like after a step — the same shape, read back.</summary>
    public readonly record struct SingleStepResult(
        uint[] D,
        uint[] A,
        uint Usp,
        uint Ssp,
        ushort Sr,
        uint Pc);

    /// <summary>
    /// Loads a state without a reset sequence: the corpus supplies the supervisor and user stacks, the
    /// status register and the program counter, and a reset would overwrite three of the four.
    /// </summary>
    public void Load(in SingleStepState state)
    {
        for (int index = 0; index < 8; index++)
        {
            _d[index] = state.D[index];
            _a[index] = state.A[index];
        }

        _usp = state.Usp;
        _ssp = state.Ssp;
        _sr = state.Sr;
        _au = state.Pc;

        // the corpus hands over the two words the hardware has already fetched: IRD is the opcode and
        // IR the word behind it, so no read happens for either of them
        _ird = state.Prefetch0;
        _ir = state.Prefetch1;
        _ircValid = false;
        _at = state.Pc;
        _fetchPointer = state.Pc + 4;
        _fetchStateAddress = state.Pc + 2;
        _streamStart = _fetchPointer;
        _flowTransferred = false;
        _trailingFetched = false;
        _queueShort = false;
        _own = 0;
        _eaCharged = false;
        _frameFetchPcDeclared = false;
        _frameInstructionWordDeclared = false;

        _halted = false;
        _stopped = false;
        _busFaulted = false;
        _faultIsFetch = false;
        _addressError = false;
        _faultAddress = 0;
        _faultProgram = false;
    }

    /// <summary>The state after a step, copied out so the corpus can compare it field by field.</summary>
    public SingleStepResult Capture()
    {
        var d = new uint[8];
        var a = new uint[8];
        for (int index = 0; index < 8; index++)
        {
            d[index] = _d[index];
            a[index] = _a[index];
        }

        return new SingleStepResult(d, a, _usp, _ssp, _sr, _au);
    }

// The seven LastFrame* columns are the emulator's instruments now: they live in M68000.Emulator.cs
// behind `Instruments` (ADR 0015 §6, #85), written by the exception entry and read by --clocks.

    // The corpus's two stack pointers are the chip's own USP / SSP now (ADR 0015 §1's retired list):
    // the state the seam loads and captures is the programmer's model, and the chip's views are its
    // doors.

    /// <summary>
    /// One instruction: the decode, the execute, and a fault taken as the exception it would be — no
    /// interrupt sampling, no STOP wait. The instruction counter and the observer see it exactly as the
    /// machine's loop would, and the closing steps are the same ones (one fetch, then the shift).
    /// </summary>
    public async ValueTask StepAsync()
    {
        ushort opcode = _ird;
        Emulator.ExecutingWord = opcode;
        Emulator.InInstruction = true;
        _au += 2;
        _opcodeAddress = _au - 2;
        _streamStart = _fetchPointer;
        _flowTransferred = false;
        _trailingFetched = false;
        _own = 0;
        _eaCharged = false;
        _faultProgram = false;
        _frameFetchPcDeclared = false;
        _frameInstructionWordDeclared = false;

        if (_busFaulted)
        {
            await TakeFaultAsync(_faultAddress, _addressError);
            _busFaulted = false;
            return;
        }

        Emulator.Instructions++;
        _opcode = opcode;
        Emulator.Observer?.Invoke(Clock, opcode, _au - 2);
        await ExecuteAsync(opcode);

        if (!_flowTransferred)
        {
            await CloseInstructionAsync();
        }

        if (_busFaulted)
        {
            await TakeFaultAsync(_faultAddress, _addressError);
            _busFaulted = false;
            return;
        }

        // a stopped CPU's counter is the STOP opcode's own address (MAME's `aaa01` rolls `m_au`
        // back twice); the machine's loop keeps the successor instead, because that is where its
        // `RTE` has to resume
        if (_stopped)
        {
            _au = _opcodeAddress;
        }

        // the prefetch's own address error, exactly as the machine's loop takes it
        if ((_au & 1) != 0)
        {
            _faultIsFetch = true;
            await TakeFaultAsync(_au, true);
        }
    }
}
