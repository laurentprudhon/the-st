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

    /// <summary>
    /// The ALU's operation select for the two-operand long families — MAME's <c>r=</c> field
    /// (2 add, 4 and, 5 sub, 13 eor, 14 or).
    /// </summary>
    enum AluLong
    {
        Or,
        And,
        Eor,
        Sub,
        Add
    }

    /// <summary>
    /// <c>alu_or</c> / <c>alu_and</c> / <c>alu_eor</c> / <c>alu_add</c> + <c>alu_addc</c> /
    /// <c>alu_sub</c> + <c>alu_subc</c> — one long operation, thirty-two bits wide, into
    /// <c>m_aluo</c>. <paramref name="a"/> is the source operand and <paramref name="b"/> the
    /// destination, so a subtract is <c>b − a</c>.
    /// </summary>
    /// <remarks>
    /// The chip's ALU is <b>sixteen</b> bits wide, so a long operation is two word ALUs with the first
    /// one's carry feeding the second — which is why MAME's states name <c>alu_add</c> and then
    /// <c>alu_addc</c>. A thirty-two-bit operation gives the same result and the same <em>final</em>
    /// flags, because the carry out of the low word is exactly the carry into the high one; the flags
    /// are therefore stated once (<see cref="SetLongFlags"/>) instead of twice. What the two halves
    /// would have shown is only an intermediate status, which no instruction's own store can observe:
    /// the writes stay where the script puts them (low word, prefetch, high word).
    /// </remarks>
    void AluLongOp(uint a, uint b, AluLong kind)
    {
        _aluWidth = 2;
        _aluo = kind switch
        {
            AluLong.Or => b | a,
            AluLong.And => b & a,
            AluLong.Eor => b ^ a,
            AluLong.Sub => b - a,
            _ => b + a
        };
    }

    /// <summary>
    /// <c>sr_nzvc</c> + <c>sr_nzvc_u</c> for a logic operation, <c>sr_xnzvc</c> + <c>sr_xnzvc_u</c>
    /// for an add or a subtract (which move X with C) — the flags of one long operation.
    /// </summary>
    /// <remarks>
    /// The <c>_u</c> half is Motorola's rule: the high word may <b>clear</b> Z but never set it, so a
    /// long result is zero only when both halves are — which, on a thirty-two-bit result, is just
    /// <c>result == 0</c>. <paramref name="extend"/> is false for a comparison (<c>CMP</c>/<c>CMPA</c>
    /// leave X alone); the arithmetic families set X from the carry.
    /// </remarks>
    void SetLongFlags(AluLong kind, uint a, uint b, uint result, bool extend)
    {
        const uint sign = 0x8000_0000;
        bool arithmetic = kind is AluLong.Add or AluLong.Sub;

        _sr = (ushort)(_sr & ~(FlagN | FlagZ | FlagV | FlagC | (arithmetic && extend ? FlagX : 0)));
        if (result == 0)
        {
            _sr |= FlagZ;
        }

        if ((result & sign) != 0)
        {
            _sr |= FlagN;
        }

        bool carry = false;
        switch (kind)
        {
            case AluLong.Add:
                if (((a ^ result) & (b ^ result) & sign) != 0)
                {
                    _sr |= FlagV;
                }

                carry = result < a || result < b;
                break;

            case AluLong.Sub:
                if (((a ^ b) & (result ^ b) & sign) != 0)
                {
                    _sr |= FlagV;
                }

                carry = b < a;
                break;
        }

        if (carry)
        {
            _sr |= (ushort)(extend ? FlagC | FlagX : FlagC);
        }
    }

    /// <summary>
    /// <c>alu_ext</c> — a word sign-extended to the ALU's width. It is the brief extension word's
    /// displacement, which the indexed scripts compute before they address anything.
    /// </summary>
    void AluExt(ushort word) => _aluo = (uint)(int)(short)word & SizeMask(_aluWidth);

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

    /// <summary><c>low16</c> — the low half of a long value, which is what <c>set_16l</c> takes.</summary>
    static uint Low16(uint value) => value & 0xFFFF;

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
    /// Where MAME's <c>m_pc</c> stands when the fetch that is about to happen faults, for the group-0 frame
    /// (<see cref="_frameFetchPc"/>). The <c>b</c>/<c>malw3</c> states put the jump's own target there and
    /// the <c>bbci3</c> state leaves the boundary's word, so the same odd address saves two different
    /// counters depending on which script read it — that is the corpus's measurement, not a rule to infer.
    /// </summary>
    void FrameSavesOnFetch(uint address)
    {
        _frameFetchPc = address;
        _frameFetchPcDeclared = true;
    }

    /// <summary>
    /// One word of a <b>control transfer</b>'s effective address, out of the decoder's queue. The first is
    /// free: the queue already holds it (the corpus's <c>prefetch[1]</c>), and a branch or a jump does not
    /// refill the slot behind it — the fetch that refill would issue is the one the transfer abandons. That
    /// is why <c>JMP (d16,An)</c> is <c>n2 r r</c> in the corpus's traces (two reads, at the target) where a
    /// fall-through instruction of the same shape reads a third, and why `BSR.w` is 18 clocks like `BSR.b`.
    /// Each word past the first is one read at the fetch pointer (<c>jmal1</c>, <c>jmal2</c>).
    /// </summary>
    async ValueTask<ushort> JumpWordAsync(int index)
    {
        if (index > 0)
        {
            return await ReadQueueWordAsync();
        }

        ushort word = _ir;
        _ir = _irc;
        _ircValid = false;
        _pc += 2;
        return word;
    }

    /// <summary>
    /// MAME's <c>bbci3</c>: a jump's target read, in program space and <b>not</b> in a fetch state — the
    /// address is not committed to the queue's counter until the <c>b</c> state follows. An odd target
    /// therefore faults with the boundary's word still in <c>m_pc</c> (see <see cref="FrameSavesOnFetch"/>)
    /// and costs the aborted read's own 4 (<see cref="ChargeAbortedReadAsync"/>).
    /// </summary>
    async ValueTask<ushort> ReadTargetAsync(uint address)
    {
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
    /// An idle micro-state: 2 clocks with nothing on the bus. Consecutive idle states would be the
    /// same machine as one longer wait, but the scripts keep them apart — the fold is a measurement
    /// (ticket #78's second number), not a convention, so a script that changes its shape has to show
    /// it in the clock column.
    /// </summary>
    ValueTask IdleAsync(int clocks = 2) => ClocksAsync(clocks);

    /// <summary>
    /// The extra 4 clocks an aborted <b>read</b> costs. The microcode's faulting state charges its own
    /// 4 and then 4 more for the cycle it abandons (MAME's <c>if(m_aob &amp; 1) { m_icount -= 4; }</c>
    /// after the state's own charge), so an odd-address read is 8 clocks in total, while an odd-address
    /// write is 4: the abort replaces the write's state. <see cref="Cycle"/> charges the abort and its
    /// record for every family, and the state's 4 is the shape's own business — a scripted family pays
    /// it here, the shape table (<c>M68000.Timing.cs</c>) already carries it in its rows because it was
    /// measured on records that include it. Measured: 7 377 scripted records of the two-operand longs
    /// were exactly 4 short without it, and 6 922 table-charged ones were exactly 4 short <em>with</em>
    /// it, which is the same fact from the other side.
    /// </summary>
    async ValueTask ChargeAbortedReadAsync()
    {
        if (_addressError)
        {
            await ClocksAsync(4);
        }
    }
}
