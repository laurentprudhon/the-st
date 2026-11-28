namespace TheST.Machine.Cpu;

/// <summary>
/// The single-step seam (ticket #34): load an architectural state, execute exactly one instruction,
/// read the state back. It exists for the vendored 680x0 corpus (<c>tests/680x0/</c>) and for nothing
/// else — <c>boot</c> never uses it, and the machine's own loop (<see cref="M68000.Launch"/>) resets the
/// chip and samples interrupts instead.
///
/// <para>
/// The seam adds no timing rule and no record: a step fetches its words through the same bus leaf the
/// machine uses, so its cost is still whichever answer instants its accesses land on. What it does not
/// do is the two things the machine's loop does around an instruction — interrupt sampling and the
/// STOP wait — because a corpus test is one instruction in isolation.
/// </para>
///
/// <para>
/// It is a **nested type** because it is not the part (ADR 0015 §2): a real 68000 has no seam, and the
/// three verbs are the harness's, not the chip's. Its two records stay outside, beside the surface,
/// because they are the *shape* a corpus record has, not a place on the die.
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
    /// The seam's three verbs (ADR 0015 §7). `Load`/`StepAsync` write and read the chip's own latches —
    /// the seam is a *door* on the part, never a copy of it — so each takes the chip it works on.
    /// </summary>
    public static class SingleStep
    {
        /// <summary>
        /// Loads a state without a reset sequence: the corpus supplies the supervisor and user stacks, the
        /// status register and the program counter, and a reset would overwrite three of the four.
        /// </summary>
        public static void Load(M68000 cpu, in SingleStepState state)
        {
            for (int index = 0; index < 8; index++)
            {
                cpu._d[index] = state.D[index];
                cpu._a[index] = state.A[index];
            }

            cpu._usp = state.Usp;
            cpu._ssp = state.Ssp;
            cpu._sr = state.Sr;
            cpu._au = state.Pc;

            // the corpus hands over the two words the hardware has already fetched: IRD is the opcode and
            // IR the word behind it, so no read happens for either of them
            cpu._ird = state.Prefetch0;
            cpu._ir = state.Prefetch1;
            cpu._ircValid = false;
            cpu._at = state.Pc;
            cpu._pc = state.Pc + 2;
            cpu._streamStart = cpu.BoundaryPosition;
            cpu._flowTransferred = false;
            cpu._trailingFetched = false;
            cpu._queueShort = false;
            cpu._freePop = false;
            cpu._own = 0;
            cpu._eaCharged = false;
            cpu._frameFetchPcDeclared = false;
            cpu._frameInstructionWordDeclared = false;
            cpu.SetFtu(state.Prefetch0);

            cpu._halted = false;
            cpu._stopped = false;
            cpu._busFaulted = false;
            cpu._faultIsFetch = false;
            cpu._addressError = false;
            cpu._faultAddress = 0;
            cpu._faultProgram = false;
        }

        /// <summary>The state after a step, copied out so the corpus can compare it field by field.</summary>
        public static SingleStepResult Capture(M68000 cpu)
        {
            var d = new uint[8];
            var a = new uint[8];
            for (int index = 0; index < 8; index++)
            {
                d[index] = cpu._d[index];
                a[index] = cpu._a[index];
            }

            return new SingleStepResult(d, a, cpu._usp, cpu._ssp, cpu._sr, cpu._au);
        }

        /// <summary>
        /// One instruction: the decode, the execute, and a fault taken as the exception it would be — no
        /// interrupt sampling, no STOP wait. The instruction counter and the observer see it exactly as the
        /// machine's loop would, and the closing steps are the same ones (one fetch, then the shift).
        /// </summary>
        public static async ValueTask StepAsync(M68000 cpu)
        {
            ushort opcode = cpu._ird;
            cpu.Emulator.ExecutingWord = opcode;
            cpu.Emulator.InInstruction = true;
            cpu._au += 2;
            cpu._opcodeAddress = cpu._au - 2;
            cpu._streamStart = cpu.FetchPosition;
            cpu._flowTransferred = false;
            cpu._trailingFetched = false;
            cpu._freePop = false;
            cpu._own = 0;
            cpu._eaCharged = false;
            cpu._faultProgram = false;
            cpu._frameFetchPcDeclared = false;
            cpu._frameInstructionWordDeclared = false;

            if (cpu._busFaulted)
            {
                await cpu.TakeFaultAsync(cpu._faultAddress, cpu._addressError);
                cpu._busFaulted = false;
                return;
            }

            cpu.Emulator.Instructions++;
            cpu._opcode = opcode;
            cpu.SetFtu(opcode);
            cpu.Emulator.Observer?.Invoke(cpu.Clock, opcode, cpu._au - 2);
            await cpu.ExecuteAsync(opcode);

            if (!cpu._flowTransferred)
            {
                await cpu.CloseInstructionAsync();
            }

            if (cpu._busFaulted)
            {
                await cpu.TakeFaultAsync(cpu._faultAddress, cpu._addressError);
                cpu._busFaulted = false;
                return;
            }

            // a stopped CPU's counter is the STOP opcode's own address (MAME's `aaa01` rolls `m_au`
            // back twice); the machine's loop keeps the successor instead, because that is where its
            // `RTE` has to resume
            if (cpu._stopped)
            {
                cpu._au = cpu._opcodeAddress;
            }

            // the boundary's sample of the queue (#87's derivation): the four columns are otherwise
            // written only inside an exception's entry, so on a clean record the two folds would see
            // nothing but the previous record's leftovers
#if DEBUG
            cpu.Emulator.LastFramePcCounter = cpu._au;
            cpu.Emulator.LastFrameFetchPointer = cpu.BoundaryPosition;
            cpu.Emulator.LastFrameIrAddress = cpu._at;
            cpu.Emulator.LastFrameTrailing = cpu._trailingFetched;
            cpu.Emulator.LastFrameRefetched = cpu.BoundaryPosition != cpu._streamStart;
            cpu.Emulator.LastFrameVector = 0;
#endif

            // the prefetch's own address error, exactly as the machine's loop takes it
            if ((cpu._au & 1) != 0)
            {
                cpu._faultIsFetch = true;
                await cpu.TakeFaultAsync(cpu._au, true);
            }

            cpu.Emulator.InInstruction = false;   // the boundary (§1.1)
        }
    }
}
