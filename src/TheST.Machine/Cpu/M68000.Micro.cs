namespace TheST.Machine.Cpu;

/// <summary>
/// The chip's elementary operations — the vocabulary an instruction's script is written in.
///
/// <para>
/// The 68000's control section is microcoded: an instruction is a <b>script of micro-states</b>, and
/// MAME's transcribed ROM (vendored under <c>research/vendor/m68000/</c>, read with
/// <c>scripts/microcode-walk.py --op …</c>, or printed as C++ with
/// <c>m68000gen.py sdf m68000.lst out.cpp</c>) is the specification. This core does <b>not</b>
/// interpret that ROM at run time and carries no microcode table: a script here is ordinary C# whose
/// statements are the microcode's own elementary operations, in the microcode's own order, so the code
/// can be read beside the state list it expands.
/// </para>
///
/// <para>
/// Three rules make a script checkable, and they are the whole convention:
/// </para>
///
/// <list type="number">
/// <item><b>One statement per elementary operation.</b> An idle micro-state is <see cref="IdleAsync"/>
/// (2 clocks). A bus-access state is the access itself: its 4 clocks are the memory's answering
/// instant, not a number the core adds. A state the generator charges nothing for (a register
/// transfer, an ALU op) is a plain assignment.</item>
/// <item><b>MAME's state name stays in the comment</b> (<c>morl1</c>, <c>pinl3</c>, <c>adrl2</c>), so a
/// reader can find the same line in the generator's output. The name is the anchor; the C# is the
/// expansion.</item>
/// <item><b>Factor by state block, never by family shape.</b> A block several scripts share
/// (<c>adrl1/adrl2</c>, <c>morl1/morl2/morw2</c>) is one method named after its states, so the second
/// family to use it writes a call and not a copy.</item>
/// </list>
///
/// <para>
/// The instruction stream is <em>not</em> re-modelled here: <c>FetchAsync</c> (an extension word out of
/// the queue), <c>TrailingFetchAsync</c> (the <c>roml1</c>/<c>morl1</c> prefetch state, once per
/// instruction) and <c>RefillAsync</c> (a control transfer's two fetches) are the queue of ADR 0009 §4
/// and stay the only way a script touches it.
/// </para>
/// </summary>
public sealed partial class M68000
{
    // ---- the ALU's three registers ---------------------------------------------------------
    // m_aluo / m_alub / and the nanocode's i=.l/.w/.b width field. They are chip state and survive
    // across states, which is why they are fields and not locals: `morl2` reads what `morl1` left.

    /// <summary>The ALU's output latch (<c>m_aluo</c>) — where <c>sr_nzvc</c> reads its flags.</summary>
    uint _aluo;

    /// <summary>The word the last read put on the internal bus (<c>m_dbin</c>).</summary>
    ushort _dbin;

    /// <summary>The word the long read left behind (<c>m_alub</c>): the high half of a long operand.</summary>
    uint _alub;

    /// <summary>The ALU's width, in the core's own size code (0 = byte, 1 = word, 2 = long).</summary>
    int _aluWidth;

    /// <summary><c>alu_or</c> — the ALU's OR, at the current width.</summary>
    void AluOr(uint a, uint b) => _aluo = (a | b) & SizeMask(_aluWidth);

    /// <summary><c>alu_and</c> — the ALU's AND, at the current width.</summary>
    void AluAnd(uint a, uint b) => _aluo = (a & b) & SizeMask(_aluWidth);

    /// <summary>
    /// <c>alu_ext</c> — a word sign-extended to the ALU's width. It is the brief extension word's
    /// displacement, which the indexed scripts compute before they address anything.
    /// </summary>
    void AluExt(ushort word) => _aluo = (uint)(int)(short)word & SizeMask(_aluWidth);

    /// <summary>
    /// <c>sr_nzvc</c> — N, Z, V and C off the ALU's output at its width; X untouched, V and C cleared.
    /// </summary>
    void SetNzvc() => SetLogicFlags(_aluo, _aluWidth);

    /// <summary>
    /// <c>sr_nzvc_u</c> — the same four, with Z <em>only ever cleared</em>: the second word of a long
    /// operation may add a set flag but never takes one away, so a chain reports the whole result.
    /// </summary>
    void SetNzvcU()
    {
        uint result = _aluo & SizeMask(_aluWidth);
        _sr = (ushort)(_sr & ~(FlagN | FlagZ | FlagV | FlagC));
        if (result != 0)
        {
            _sr = (ushort)(_sr & ~FlagZ);
        }

        if ((result & (uint)SignBit(_aluWidth)) != 0)
        {
            _sr |= FlagN;
        }
    }

    /// <summary><c>set_16l</c> — the low word of a data register, the high one untouched.</summary>
    void Set16L(int dataRegister, uint value) =>
        _d[dataRegister] = (_d[dataRegister] & 0xFFFF0000) | (value & 0xFFFF);

    /// <summary><c>set_16h</c> — the high word of a data register, the low one untouched.</summary>
    void Set16H(int dataRegister, uint value) =>
        _d[dataRegister] = (_d[dataRegister] & 0xFFFF) | ((value & 0xFFFF) << 16);

    /// <summary><c>ext32</c> — a word read as a signed 32-bit value (an extension word's payload).</summary>
    static uint Ext32(ushort word) => (uint)(int)(short)word;

    /// <summary><c>high16</c> — the high half of a long operand, in the low half of the value.</summary>
    static uint High16(uint value) => (value >> 16) & 0xFFFF;

    /// <summary><c>merge_16_32</c> — an address assembled from its high and low words.</summary>
    static uint Merge16_32(uint high, ushort low) => (high << 16) | low;

    /// <summary>
    /// A word read with the instruction stream's bus status (<c>m_base_ssw = SSW_PROGRAM | SSW_R</c>,
    /// <c>m_opcodes.read</c>). The PC-relative shapes read their operand's first word this way — it is
    /// still the instruction stream while the address is being resolved — so a fault records a program
    /// access in the frame's status word. It is not a fetch: the saved counter does not move.
    /// </summary>
    async ValueTask<ushort> ReadOperandProgramAsync(uint address)
    {
        BusResult result = await Cycle(address, 1, false, 0, isProgram: true);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
        }

        return result.Data;
    }

    /// <summary>
    /// A state that begins a prefetch — the microcode's <c>m_pc = m_au</c> (<c>roml1</c>, <c>abww1</c>,
    /// <c>ablw2</c>, <c>e#w1</c>). The address it is about to read becomes the frame's saved program
    /// counter, which is why a group-0 frame's depth is decided per script and not by an expression on
    /// the queue: a state that merely pops a word (<c>adsl1</c>, <c>aixw2</c>) does not move it.
    /// </summary>
    void BeginFetchState() => _fetchStateAddress = _fetchPointer;

    /// <summary>
    /// An idle micro-state: 2 clocks with nothing on the bus. Consecutive idle states would be the
    /// same machine as one longer wait, but the scripts keep them apart — the fold is a measurement
    /// (ticket #78's second number), not a convention, so a script that changes its shape has to show
    /// it in the clock column.
    /// </summary>
    ValueTask IdleAsync(int clocks = 2) => ClocksAsync(clocks);
}
