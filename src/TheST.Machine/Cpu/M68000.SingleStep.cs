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
    /// <summary>The state a corpus test hands the core, and reads back: exactly the 680x0 corpus's shape.</summary>
    public readonly record struct SingleStepState(
        uint[] D,
        uint[] A,
        uint Usp,
        uint Ssp,
        ushort Sr,
        uint Pc);

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
        _pc = state.Pc;

        _halted = false;
        _stopped = false;
        _busFaulted = false;
        _faultIsFetch = false;
        _addressError = false;
        _faultAddress = 0;
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

        return new SingleStepResult(d, a, _usp, _ssp, _sr, _pc);
    }

    /// <summary>The user stack pointer, whatever mode the core is in — the corpus keeps both.</summary>
    public uint Usp => _usp;

    /// <summary>The supervisor stack pointer, whatever mode the core is in.</summary>
    public uint Ssp => _ssp;

    /// <summary>
    /// One instruction: the fetch, the execute, and a fault taken as the exception it would be — no
    /// interrupt sampling, no STOP wait. The instruction counter and the observer see it exactly as the
    /// machine's loop would.
    /// </summary>
    public async ValueTask StepAsync()
    {
        ushort opcode = await FetchAsync();
        if (_busFaulted)
        {
            await TakeFaultAsync(_faultAddress, false);
            _busFaulted = false;
            return;
        }

        Instructions++;
        _opcode = opcode;
        Observer?.Invoke(Clock, opcode, _pc - 2);
        await ExecuteAsync(opcode);

        if (_busFaulted)
        {
            await TakeFaultAsync(_faultAddress, _addressError);
            _busFaulted = false;
            return;
        }

        // the prefetch's own address error, exactly as the machine's loop takes it
        if ((_pc & 1) != 0)
        {
            _faultIsFetch = true;
            await TakeFaultAsync(_pc, true);
        }
    }
}
