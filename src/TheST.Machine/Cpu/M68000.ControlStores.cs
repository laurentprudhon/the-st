namespace TheST.Machine.Cpu;

/// <summary>
/// The two control stores' vocabulary — the words a script is written in (ADR 0015 §10, `design/cpu-silicon.md`).
///
/// <para>
/// The die has two control stores, read at the same address: the **microcode** (544 × 17 bits), which
/// is a script — what state runs next and which bus access it declares — and the **nanocode**
/// (336 × 68 bits), which is one microstate's datapath work. This file is their vocabulary, in two
/// blocks:
/// </para>
///
/// <list type="bullet">
/// <item><b>MicroStore</b> — the sequencing half: the idle state (<c>IdleAsync</c>, one microword's two
/// clocks), the charge a script spends (<c>ClocksAsync</c>, and the access it declares), and the
/// instruction stream's three reads (<c>FetchAsync</c>, <c>TrailingFetchAsync</c>, <c>RefillAsync</c>).
/// A statement from this block is a microstate; it never computes a value.</item>
/// <item><b>NanoStore</b> — the datapath half: the transfers between the register file and the ALU, the
/// AU's constant select (<c>AuPlusTwo</c> and friends), the ALU mode and its flag update
/// (<c>SetNzvc</c>, <c>SetAddFlags</c>), the size masks, and the sixteen-bit ALU of the variable-time
/// families. A statement from this block is one nanoword's worth of work.</item>
/// </list>
///
/// <para>
/// The third layer is not here: the PLA bank keyed on <c>IRD</c> — which register file <c>RX</c>/<c>RY</c>
/// mean, the operand size, the vector — is `M68000.Decode.cs`, because those are *decode* facts about an
/// instruction, not datapath atoms.
/// </para>
///
/// <para>
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
    // The ALU's latches (ALUO, ALUB, ALUE, ISR, the width field) and the read latch DBIN are storage:
    // they live in M68000.Registers.cs (ADR 0015 §4). Everything below is an *atom* — one nanoword's
    // worth of datapath work — and goes through those latches.

    // ============================================================================================
    // NanoStore — the datapath's atoms: transfers, the AU's constant select, the ALU mode and its
    // flag update, the size masks. Each is one nanoword's field set; a script calls them and computes
    // nothing itself.
    // ============================================================================================

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
        _d[dataRegister] = Set16L(_d[dataRegister], (ushort)((value & 0xFFFF)));

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
        BusResult result = await Cycle(address, 1, false, 0, AccessClass.Program);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
        }

        return result.Data;
    }

    /// <summary>
    /// `m_pc ← m_au` — the die's copy of the address adder into the program counter register: the states
    /// that issue a fetch (`roml1`'s refill, an instruction's closing fetch, a jump's <c>m_pc = m_au</c>)
    /// do it, and nothing else writes `PC`. The adder is the *address being accessed* at that instant,
    /// which is the whole rule: an extension word the queue already holds is not an access, so it does
    /// not move the register.
    /// <para>
    /// The address is handed in because on this tree the adder is the consumption counter rather than the
    /// access's address: `FetchPosition` derives the position from it and the queue's fill, and the value
    /// copied is the die's — the address the access will read (#87).
    /// </para>
    /// </summary>
    void CopyAuToPc(uint position) => _pc = position;

    /// <summary>
    /// A state that begins a prefetch — the microcode's <c>m_pc = m_au</c> (<c>roml1</c>, <c>abww1</c>,
    /// <c>ablw2</c>, <c>e#w1</c>). The address it is about to read becomes the frame's saved program
    /// counter, which is why a group-0 frame's depth is decided per script and not by an expression on
    /// the queue: a state that merely pops a word (<c>adsl1</c>, <c>aixw2</c>) does not move it.
    /// <para>
    /// The position handed over is <see cref="FetchPosition"/> — the derivation — because on this tree
    /// the adder is the consumption counter between states rather than the access's address. The atom is
    /// here so that every script state which begins a prefetch says so in one place, and so that the day
    /// the adder itself holds the access (#87's AU/PC split) this method is the die's bare copy.
    /// </para>
    /// </summary>
    void BeginFetchState() => CopyAuToPc(FetchPosition);

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
    /// The group-0 frame records <paramref name="word"/> as "the instruction" instead of the opcode —
    /// MAME's <c>m_ird = m_ir</c> in the word and long <c>MOVE</c>'s <c>-(An)</c> store state
    /// (<c>mmmw2</c>), where the queue head has already moved to the instruction's next word.
    /// </summary>
    void FrameSavesInstructionWord(ushort word)
    {
        _frameInstructionWord = word;
        _frameInstructionWordDeclared = true;
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
            return await ReadQueueWordAsync(FetchPosition);
        }

        ushort word = _ir;
        _ir = _irc;
        _ircValid = false;
        _au += 2;
        _freePop = true;
        return word;
    }

    /// <summary>
    /// MAME's <c>bbci3</c>: a jump's target read, in program space and <b>not</b> in a fetch state — the
    /// address is not committed to the queue's counter until the <c>b</c> state follows, so the pipeline's
    /// position is untouched and the script sets the counter itself. An odd target therefore faults with
    /// the value the script declared (see <see cref="FrameSavesOnFetch"/>) and costs the aborted read's
    /// own 4 (<see cref="ChargeAbortedReadAsync"/>).
    /// </summary>
    async ValueTask<ushort> ReadTargetAsync(uint address)
    {
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

    // ---- NanoStore: the bit-number decoder and the address adder's transfers ------------------
    // Both are the die's, and both are *derived* in ADR 0015 §4's sense: a decoder has no state, and
    // an adder transfer is a verb, not a latch. They are methods so that the scripts say "the bit
    // number became a one-hot line" and "the adder stepped by two" instead of open-coding a shift and
    // an increment.

    /// <summary>
    /// `DCR` — the bit-number decoder: four bits in, one line of sixteen out (sheet 01's `BIT#`/`BIT`).
    /// The word it drives is the sixteen-bit operand's; a data-register bit operation is 32 bits wide and
    /// shifts its own value, which is why this is not used there.
    /// </summary>
    static ushort Decoder4To16(int bit) => (ushort)(1 << (bit & 15));

    /// <summary>`AU ← AU + 2` — the adder advancing past one word (tutorial §1.4's next fetch).</summary>
    void AuPlusTwo() => _au += 2;

    /// <summary>`AU ← AU + n` — the adder's other constant steps (an extension word, a long operand).</summary>
    void AuPlus(int words) => _au += (uint)(2 * words);

    /// <summary>`AU ← AT` — the adder taking the address temporary (a jump's target, a refill).</summary>
    void AuEqualsAt() => _au = _at;

    /// <summary>
    /// `AT ← AU` — the address temporary latching the adder (`mulr1`'s <c>m_at = m_au</c>), which is how
    /// a loop saves the position it is about to spend on counting: the die protects the program counter by
    /// keeping it in AT and copying it back into `m_pc` inside the loop (<c>mulm4</c>'s
    /// <c>set_16l(m_pc, m_at)</c>); this core keeps it in `_at` and puts it back in the adder at the exit.
    /// </summary>
    void AtEqualsAu() => _at = _au;

    /// <summary>
    /// `AU ← ext32(FTU)` — the adder taking the field register's value, which is how the die loads a
    /// loop's trip count into its counter (`mulr1`'s <c>m_au = ext32(m_ftu)</c>, fed by the constants
    /// ROM). The narrow FTU gives the constant its one home, so a loop's 15 is a transfer and not a
    /// literal in the microcode's text.
    /// </summary>
    void AuEqualsFtu() => _au = (uint)(short)_ftu;

    /// <summary>
    /// `m_au = m_au - 1` — the counter's own step (`mulm4`). Not <see cref="AuPlus"/>: that one steps in
    /// *words*, and a loop counts in units.
    /// </summary>
    void AuMinusOne() => _au--;

    /// <summary>`m_au = m_au + 1` — the unit step's other direction: a divide loads `ftu + 1` (sixteen
    /// iterations for sixteen quotient bits), which is the adder's arithmetic on the field register's
    /// constant and not a second literal.</summary>
    void AuPlusOne() => _au++;

    /// <summary>
    /// The branch variable `auz`: the adder's **six low bits** are zero (`mulm4`'s <c>!(m_au &amp; 0x3f)</c>).
    /// Six, not sixteen, because the loop counter is an address: the die tests the bits a small address
    /// would carry, which is why a loop of sixteen iterations starts at 15 and ends when the low six bits
    /// wrap.
    /// </summary>
    bool AuZeroOnSixBits => (_au & 0x3F) == 0;

    /// <summary>
    /// The adder taking back the **consumed** position after a loop has used it as a counter: the PC
    /// register's copy, less one word. `_pc` is where the instruction stream had fetched up to when the
    /// loop began (a fetch state, measured as the consumed position + 2), and the loop issues no fetch,
    /// so it is intact when the loop ends — which is what makes the adder free for `B1` (see the plan's
    /// §8 item 3 and the two measurements that bound the full split: 4 277 and 604 records).
    /// </summary>
    void AuEqualsConsumed() => _au = _pc - 2;

    /// <summary>`AOB ← address` — the address buffer taking what goes out on `A1–A23`.</summary>
    void LatchAob(uint address) => _aob = address;

    /// <summary>
    /// `m_reset_cb(1)` / `m_reset_cb(0)` — the <c>RESET</c> instruction's drive of the part's `/RESET`
    /// output: <c>rset2</c> asserts it when the countdown starts, <c>rset5</c> releases it in the state
    /// that issues the instruction's one read, 124 clocks later. One record per edge, at the instant the
    /// state makes it, so a peripheral's reset input is a level it can read (`Pins.RESET`) and a change
    /// it can be woken by (owner's ruling on #92). The *input* side of the pin — the reset network
    /// pulling the line at power-on — is still the host's, since that network is not a chip
    /// (`design/crossings.md`).
    /// </summary>
    void DriveReset(bool asserted) => Write(Clock, Wires.Reset, 0x1, (ushort)(asserted ? 1 : 0));

    /// <summary>
    /// The address of the word the instruction has just consumed — **the PC-relative operand's base**,
    /// and the one fact this tree takes from the consumption counter where the die takes it from
    /// `m_pc`. Measured, and it is why the base cannot simply become the register: writing these seven
    /// sites as `_pc` costs **604 records over 13 files** (`ADD.b`, `AND.b`, `BTST`, `CMP.b`, `LEA`,
    /// `MOVE.b`, `MOVEM.l`/`.w`, `MOVEtoCCR`, `MOVEtoSR`, `OR.b`, `PEA`, `SUB.b`) — the shapes that
    /// prefetch before they compute the operand, so `_pc` is already a word ahead of the word in hand.
    /// Our counter advances on **consumption** where the die's advances on **fetch**, and that is the
    /// whole of #87's remaining split: replacing this body is its last step.
    /// </summary>
    uint ConsumedAddress => _au - 2;

    /// <summary>
    /// The pipeline's next read address — the derivation that replaces the old `_fetchPointer` field
    /// (#87). Two cells hold it: `_au`, which names the word the instruction consumes next, and the
    /// queue's fill, which the access itself moves. A read fills the slot <em>behind</em> the head, so
    /// the address is two bytes past `_au` — four once a prefetch has filled that slot (`_ircValid`).
    /// Two states consumed a queue word with no read behind it and leave the position on `_au` itself: a
    /// control transfer's free pop (`_freePop`, the fetch a transfer abandons) and the absolute-long
    /// pair's second word (`_queueShort`, two words taken from one read).
    /// <para>
    /// Measured on MAME's 317 500-record corpus at every site that used to read the field: exact on
    /// 104 527 prefetches, 88 177 pops, 135 864 trailing fetches and 29 075 fetch states.
    /// </para>
    /// </summary>
    uint FetchPosition => _queueShort || _freePop ? _au : _au + (_ircValid ? 4u : 2u);

    /// <summary>
    /// The position **at a boundary**, where the closing shift has moved `_irc` into the queue's head
    /// without moving `_au`: the head is at `_au + 2` and the word past it — the next read — is `_au + 4`.
    /// This is the one state in which `_au` still names the word in `IRD`, and it is what the `--clocks`
    /// frame columns report (4 words past the boundary's counter on 260 664 clean records, 6 on the
    /// 1 230 `STOP` records whose seam rolls the counter back to the opcode's own address — the
    /// machine's loop keeps the successor, so the rollback is two bytes and the position reads
    /// `_au + 6` there).
    /// </summary>
    uint BoundaryPosition => _au + (_stopped ? 6u : 4u);

    // ---- NanoStore: PREN, the encoder that walks MOVEM's list --------------------------------
    // MOVEM is not a loop over a counter: the **list is the loop's state**. PREN holds the mask, its
    // lowest set line is the register this turn transfers, and the nanocode's `reset pren` clears that
    // line — so the exit test is the mask's own zero (MAME's `m_t = !m_movemr`) and there is no counter
    // to get wrong. Measured on the family: both files, 5 000 records, were already green on both
    // columns before the walk was expressed this way, and the expression moved nothing — which is the
    // proof that the mask *was* the whole state (#92).

    /// <summary>`PREN ← list` — the list word off the bus into the encoder (`stmr1`'s `m_movemr = m_dbin`).</summary>
    void LoadPren(ushort list) => _pren = list;

    /// <summary>Whether the walk is over: every line of the list has been taken (`m_t = !m_movemr`).</summary>
    bool PrenEmpty => _pren == 0;

    /// <summary>
    /// `reset pren` — the encoder answers the number of its **lowest set line** and clears that line from
    /// the list: the register this turn transfers. An empty list answers line 0 (MAME's `countr_zero`
    /// clamps the 32 it returns), which the walk's own exit test has already excluded — the clamp is kept
    /// here so the atom is the encoder and not a caller's convention.
    /// </summary>
    int StepPren()
    {
        int line = System.Numerics.BitOperations.TrailingZeroCount(_pren);
        if (line > 15) line = 0;
        _pren = (ushort)(_pren & ~(1 << line));
        return line;
    }

    /// <summary>
    // ---- NanoStore: the FTU, the fields a script reads instead of re-shifting the opcode -----------
    // ADR 0015 §10 lists `FTU` as what replaces "the `size`/`mode`/`reg` locals", and §5.2 gives the die's
    // register F four sources: SR, IR, IPL and a vector. The narrow FTU of the owner's ruling holds the
    // three encoding fields and the vector; the decode writes the fields once per instruction (from
    // `IRD`, which is what the PLAs key on) and a script reads them, so nothing below re-derives
    // `(op >> 6) & 3`.

    /// <summary>The instruction's size field, as decoded (`IRD`'s bits 7–6).</summary>
    int FtuSize => (_ftu >> 14) & 0x3;

    /// <summary>The instruction's effective-address mode field, as decoded (`IRD`'s bits 5–3).</summary>
    int FtuMode => (_ftu >> 11) & 0x7;

    /// <summary>The instruction's register field, as decoded (`IRD`'s bits 2–0).</summary>
    int FtuRegister => (_ftu >> 8) & 0x7;

    /// <summary>`m_ftu` as the exception logic reads it: the vector a `TRAP`, a fault or an interrupt supplied.</summary>
    int FtuVector => _ftu & 0xFF;

    /// <summary>
    /// The decode's write: the instruction's three encoding fields — `A2`/`A3`'s terms over `IRD` — into
    /// the field register. They are the encoding's **low byte in order** (size 7–6, mode 5–3, register
    /// 2–0), so the PLA's output is that byte and the latch is one shift into the high half; the layout
    /// above is exactly this packing, which is why the accessors are masks and not re-derivations. One
    /// instruction, one shift — the cost G8 can see.
    /// </summary>
    void SetFtu(ushort opcode) => _ftu = (ushort)(opcode << 8);

    /// <summary>`vector → FTU` — the exception's vector latched where the entry reads it (`tvn_to_ftu`).</summary>
    void VectorToFtu(int vector) => _ftu = (ushort)((_ftu & 0xFF00) | (vector & 0xFF));

    /// <summary>
    /// `const → FTU` — the constants ROM into the field register (`const_to_ftu`: MAME's `m_ftu = 0x0f`
    /// before a `MUL`'s loop). The die loads a loop's trip count *from here*, which is why `B1`'s counter
    /// is "loaded from the FTU" rather than a literal, and why this atom exists before its first caller.
    /// </summary>
    void ConstToFtu(int value) => _ftu = (ushort)value;

    /// <summary>`reset pren` on the predecrement list, which the encoder reads from its other end:
    /// stored **reversed** (`movem.l d2-d7/a2-a6,-(sp)` is `0x48E7 0x3F3E` where the other modes write
    /// `0x7CFC`), so line 0 is register 15 and the line's complement *is* the register. MAME's
    /// `step_movem_predec` and its `r ^ 0xf` — one encoder, one list, two directions of travel.
    /// </summary>
    int StepPrenReversed() => StepPren() ^ 0xF;

    // ---- NanoStore: the sixteen-bit ALU of the variable-time families --------------------------
    //
    // MULU, MULS, DIVU and DIVS are not operations, they are *algorithms the sequencer iterates*. The
    // 68000 has no multiplier and no divider — a 16x16->32 array multiplier and a 32/16 divider would
    // have been a large, slow block on a chip that already had no cache and 68 000 transistors — so
    // Motorola spent microcode instead, and the instruction's own timing table became a formula for the
    // loop's length (38 + 2n for a multiply, a range for a divide) rather than a number. Every
    // micro-state costs two clocks, four when it touches the bus, so the clocks here are the states
    // actually executed: that is why these four families are scripted as loops (MultiplyAsync,
    // DivideUnsignedAsync, DivideSignedAsync) and why their ledger in M68000.Timing.cs is zero. A wrong
    // value in this block would necessarily be a wrong clock, and a wrong clock a wrong value.
    //
    // The loops need the chip's own registers, kept alive across states the way MAME keeps them
    // (research/vendor/m68000/m68000.h), so they are fields and not locals:
    //   m_aluo   the ALU's output latch — where sr_nzvc reads its flags, and the high half of the pair
    //   m_alub   the second operand: the multiplicand (MUL) and the divisor (DIV)
    //   m_alue   the ALU's other persistent operand: the multiplier's shift register, then the
    //            quotient's — the low half of the pair, in both directions
    //   m_isr    the flags of the operation just run; SrNzvc copies them into SR, and SrNzvcU only ever
    //            clears Z — which is how a clear-only flag carries "the result is zero" across a whole
    //            loop
    //   (in the scripts) m_at, a thirty-two-bit scratch with settable halves, and m_au, the loop's
    //            counter, loaded from the instruction's timing constant
    //
    // Every operation below is transcribed from the C++, where the operands are sixteen bits wide, and
    // the masks are semantics rather than hygiene: `a << 17` on a sixteen-bit operand is how the
    // divide's pair shifts left, and a transcription that widened m_alue to thirty-two bits produced
    // garbage on every record.

    /// <summary><c>alu_and</c> — a bitwise and; V and C fall to the state's flag update, not here.</summary>
    /// <remarks>
    /// The loops use it for two things that are not ands at all. <c>alu_and(Dn, 0)</c> is how a multiply
    /// says "the accumulator starts at zero" before its first add — which is what lets the loop's
    /// clear-only Z end up meaning "the whole product is zero". And <c>alu_and(x, 0xFFFF)</c> is "put x
    /// in the latch and give me its N and Z": a move that sets flags, which the divide uses every time it
    /// has to restore a value's flags without arithmetising it.
    /// </remarks>
    void AluAnd16(uint a, uint b)
    {
        uint r = (b & a) & 0xFFFF;
        _isr = (ushort)(_sr & FlagX);
        if (r == 0)
        {
            _isr |= FlagZ;
        }

        if ((r & 0x8000) != 0)
        {
            _isr |= FlagN;
        }

        _aluo = r;
    }

    /// <summary><c>alu_add</c> — <c>b + a</c>, sixteen bits, with the carry out and the signed overflow.</summary>
    /// <remarks>
    /// The carry out of bit 15 lands in C and in X, and in the multiply it is load-bearing: the
    /// accumulator is only the high half of a thirty-two-bit register, so the bit that falls off its top
    /// is caught in C and put straight back by the following rotate (AluRoxr32mu reads C and shifts it in
    /// at bit 31). That round trip, once per multiplier bit, is the multiply's whole column arithmetic.
    /// </remarks>
    void AluAdd16(uint a, uint b)
    {
        a &= 0xFFFF;
        b &= 0xFFFF;
        uint r = b + a;
        _isr = 0;
        if ((r & 0xFFFF) == 0)
        {
            _isr |= FlagZ;
        }

        if ((r & 0x8000) != 0)
        {
            _isr |= FlagN;
        }

        if ((r & 0x10000) != 0)
        {
            _isr |= (ushort)(FlagX | FlagC);
        }

        if ((((b & a & ~r) | (~b & ~a & r)) & 0x8000) != 0)
        {
            _isr |= FlagV;
        }

        _aluo = r & 0xFFFF;
    }

    /// <summary><c>alu_sub</c> — <c>b − a</c>: the microcode's argument order for every subtract here.</summary>
    /// <remarks>
    /// Read the order carefully: the listing's <c>alu_sub(m_alub, m_aluo)</c> means
    /// <c>m_aluo − m_alub</c> — remainder minus divisor, not the other way round — and the borrow it
    /// leaves in C is the divide's per-bit decision (borrow means restore, quotient bit 0) as well as,
    /// in the prologue, the overflow test (no borrow means the quotient will not fit).
    /// </remarks>
    void AluSub16(uint a, uint b)
    {
        a &= 0xFFFF;
        b &= 0xFFFF;
        uint r = b - a;
        _isr = 0;
        if ((r & 0xFFFF) == 0)
        {
            _isr |= FlagZ;
        }

        if ((r & 0x8000) != 0)
        {
            _isr |= FlagN;
        }

        if ((r & 0x10000) != 0)
        {
            _isr |= (ushort)(FlagX | FlagC);
        }

        if ((((b & ~a & ~r) | (~b & a & r)) & 0x8000) != 0)
        {
            _isr |= FlagV;
        }

        _aluo = r & 0xFFFF;
    }

    /// <summary><c>alu_subc</c> — the same, with the latch's own carry as a borrow in.</summary>
    /// <remarks>
    /// The borrow chain is what the signed divide's prologue needs: complementing a thirty-two-bit
    /// dividend means complementing its low word (setting the carry) and then its high word *with that
    /// carry*, which is one borrow-carrying subtract rather than an add of one.
    /// </remarks>
    void AluSubC16(uint a, uint b)
    {
        a &= 0xFFFF;
        b &= 0xFFFF;
        uint r = b - a - (uint)((_isr & FlagC) != 0 ? 1 : 0);
        _isr = 0;
        if ((r & 0xFFFF) == 0)
        {
            _isr |= FlagZ;
        }

        if ((r & 0x8000) != 0)
        {
            _isr |= FlagN;
        }

        if ((r & 0x10000) != 0)
        {
            _isr |= (ushort)(FlagX | FlagC);
        }

        if ((((b & ~a & ~r) | (~b & a & r)) & 0x8000) != 0)
        {
            _isr |= FlagV;
        }

        _aluo = r & 0xFFFF;
    }

    /// <summary>
    /// <c>alu_over</c> — a divide's overflow exit: <b>N and V set</b>, and Z and C cleared by the
    /// state's flag update. The manuals say V; the corpus's records for both divides carry `0x0A`
    /// (`aN` and `aV`) on all 1 459 overflow records.
    /// </summary>
    void AluOver()
    {
        _isr = (ushort)(FlagV | FlagN);
        _aluo = 0;
    }

    /// <summary>
    /// <c>alu_sla0</c> / <c>alu_sla1</c> — the divide's shift of the <c>(m_aluo:m_alue)</c> pair one
    /// bit left, with <paramref name="bit"/> shifted into the bottom. X is carried through and the
    /// bit shifted out of the top becomes C.
    /// </summary>
    /// <remarks>
    /// This one state is the "bring down the next bit, and double the remainder" step of long division.
    /// The pair holds the remainder in its high half and the dividend's remaining bits in its low half,
    /// so a left shift doubles the remainder in place *and* slides the next dividend bit up into it —
    /// free, because the dividend is already in the register. The bit the shift pushes in at the bottom
    /// is the previous comparison's quotient bit, so the same shift also assembles the quotient in the
    /// low half's tail. <c>a &lt;&lt; 17</c> is the pair's shift (the high half is worth 2^16, so its bits
    /// move up one place), the bit out of the top becomes C, and Z is the pair read whole.
    /// </remarks>
    void AluSla(uint bit)
    {
        uint a = _aluo & 0xFFFF;
        uint r = ((a << 17) | ((_alue & 0xFFFF) << 1) | (bit & 1));
        _isr = (ushort)(_sr & FlagX);
        if (r == 0)
        {
            _isr |= FlagZ;
        }

        if ((r & 0x80000000) != 0)
        {
            _isr |= FlagN;
        }

        if ((a & 0x8000) != 0)
        {
            _isr |= FlagC;
        }

        _alue = r & 0xFFFF;
        _aluo = (r >> 16) & 0xFFFF;
    }

    /// <summary>
    /// <c>alu_roxr32mu</c> — MULU's rotate: the <c>(m_aluo:m_alue)</c> pair right one bit, the carry in
    /// at the top. This is the shift register the unsigned multiply accumulates in: the multiplier
    /// leaves through the bottom while the partial product comes in at the top, which is why the
    /// product's low half ends up in <c>m_alue</c> and its high half in <c>m_aluo</c>.
    /// </summary>
    /// <remarks>
    /// The pair P = (accumulator : remaining multiplier) is the multiply's entire state, and the loop is
    /// the recurrence <c>P = floor((P + b·M·2^16) / 2)</c> from <c>P0 = Q</c>: each rotate halves the
    /// register, so the add's weight shrinks to the bit's own place and after sixteen of them the
    /// accumulator has absorbed exactly <c>sum(b_i·M·2^i) = M·Q</c>. The multiplier's bits are consumed
    /// one per rotate — which is why the caller's branch tests bit 1 and not bit 0, bit 0 having gone by
    /// in the previous rotate — and the low half is left holding what remains of the multiplier, i.e. the
    /// product's low sixteen bits. That is why mulm6 can store the pair whole.
    /// </remarks>
    void AluRoxr32mu()
    {
        uint a = _aluo & 0xFFFF;
        uint r = ((a << 15) | ((_alue & 0xFFFF) >> 1) | ((_sr & FlagC) != 0 ? 0x80000000u : 0u));
        _isr = 0;
        if ((a & 1) != 0)
        {
            _isr |= FlagX;
        }

        if ((r & 0x80000000) != 0)
        {
            _isr |= FlagN;
        }

        if ((r & 0xFFFF0000) == 0)
        {
            _isr |= FlagZ;
        }

        _aluo = (r >> 16) & 0xFFFF;
        _alue = r & 0xFFFF;
    }

    /// <summary>
    /// <c>alu_roxr32ms</c> — MULS's rotate: the same shift register, but the bit shifted in is a copy of
    /// the product's sign (Booth's recoding), read off N and V as the C++ does.
    /// </summary>
    /// <remarks>
    /// The sign shifted in is <c>N xor V</c> — the accumulator's true sign even when the sixteen-bit add
    /// just overflowed, which is why the C++ tests for exactly one of the two. That single change is what
    /// makes the multiplier a *signed* bit string: a negative multiplier's infinite run of leading 1s is
    /// sign-extended into the register instead of being truncated at sixteen bits, so Booth's digits
    /// (<c>d_i = b_i − b_(i−1)</c>) terminate instead of accumulating forever — and it is the only reason
    /// MULS needs a subtract state at all.
    /// </remarks>
    void AluRoxr32ms()
    {
        uint a = _aluo & 0xFFFF;
        uint nv = (uint)(_isr & (FlagN | FlagV));
        uint r = ((a << 15)
                  | ((_alue & 0xFFFF) >> 1)
                  | (nv == FlagN || nv == FlagV ? 0x80000000u : 0u));
        _isr = 0;
        if ((a & 1) != 0)
        {
            _isr |= FlagX;
        }

        if ((r & 0x80000000) != 0)
        {
            _isr |= FlagN;
        }

        if ((r & 0xFFFF0000) == 0)
        {
            _isr |= FlagZ;
        }

        _aluo = (r >> 16) & 0xFFFF;
        _alue = r & 0xFFFF;
    }

    /// <summary>
    /// <c>sr_nzvc</c> — N, Z, V and C from the latch; X untouched. MULU/MULS and DIVU/DIVS set the
    /// flags once per state this way, so a reader can follow the listing's <c>sr_*</c> calls.
    /// </summary>
    void SrNzvc() =>
        _sr = (ushort)((_sr & ~(FlagN | FlagZ | FlagV | FlagC)) | (_isr & (FlagN | FlagZ | FlagV | FlagC)));

    /// <summary>
    /// <c>sr_nzvc_u</c> — the same, except that **Z can only be cleared**: the multiply's loop starts
    /// with Z set and clears it as soon as a partial product is non-zero, and the divide's mantissa
    /// shifts keep the Z of the whole computation. `~FlagZ` has the Z bit *clear*, which is what makes
    /// the C++ expression below a clear-only update (it is easy to read as a no-op — it is not).
    /// </summary>
    void SrNzvcU() =>
        _sr = (ushort)(((_sr & ~(FlagN | FlagV | FlagC) & (_isr | unchecked((ushort)~FlagZ)))
                        | (_isr & (FlagN | FlagV | FlagC))) & 0xFFFF);

    /// <summary>
    /// <c>sr_nz_u</c> — N and Z from the latch and <b>V and C left exactly as they were</b>; Z can only
    /// be cleared. It is the half of a long <c>MOVE</c>'s flag update that the high word owns, and it is
    /// not <see cref="SrNzvcU"/>: that one clears V and C, which a fault between a long store's two
    /// words makes visible (it is the difference between `0x10` and `0x12` on 252 records).
    /// </summary>
    void SrNzU() =>
        _sr = (ushort)(((_sr & ~FlagN & (_isr | unchecked((ushort)~FlagZ))) | (_isr & FlagN)) & 0xFFFF);
}
