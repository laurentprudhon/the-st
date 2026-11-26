// ============================================================================================
// M68000.Registers.cs — the storage, and nothing else (ADR 0015 §4, §5.1; tickets #85, #86).
//
// This file is the die's register file and the latches beside it, one declaration per latch, each
// carrying the name the patents give it (`research/68000-silicon/nomenclature.md` §3's "recommended"
// column) and the sheet it appears on. Nothing here computes: a method in this file would be a state
// transition, and a state transition belongs to `NanoStore` (the atoms) or to a script.
//
// Three rules, and they are what makes this file checkable:
//
//   · **A register is 32 bits.** The High/Low/Data split the die draws is a *layout* fact with no
//     behavioural consequence here (ADR 0015 §3), so it is expressed as the two section accessors
//     `Set16H`/`Set16L` over one 32-bit cell — the manual's own rule, that a word write leaves the
//     high half standing and a byte write the rest of the low half (tutorial §1.1).
//   · **`AU` is the program counter.** The hardware has one address adder register and it *is* `PC`;
//     `PC` is the manual's view of it, not a second field (ADR 0015 §2). Same for `A7`: `USP`/`SSP`
//     are the storage and `A7` is whichever is live.
//   · **No time here.** A duration is not storage: its name carries its unit and it may live only in
//     `M68000.Emulator.cs` (ADR 0015 §5.1). A script's charge is a local in the script's frame.
//
// What is deliberately *not* here: the prefetch queue's bookkeeping (`_fetchPointer`,
// `_fetchStateAddress`, `_streamStart`, `_opcodeAddress`, `_trailingFetched`, `_queueShort`) — those are
// the queue's software position, not latches the patents name; and `_own`/`_eaCharged`, which are
// durations and "has happened" bits and die with `M68000.Timing.cs` (#90).
// ============================================================================================

namespace TheST.Machine.Cpu;

public sealed partial class M68000
{
    // ---- the register file: D0–D7, A0–A6, USP/SSP -----------------------------------------
    // §5.2 of the die reading: one 32-bit cell a register, drawn across the HIGH, LOW and DATA
    // sections. A7 is not an entry: it is whichever stack pointer the status word selects.

    /// <summary>`D0–D7` — eight 32-bit data registers (tutorial §1.1).</summary>
    readonly uint[] _d = new uint[8];

    /// <summary>`A0–A6` — seven address registers; `A7` is <see cref="_ssp"/> or <see cref="_usp"/>.</summary>
    readonly uint[] _a = new uint[8];

    /// <summary>`USP` — the user stack pointer, hidden while the part is in supervisor state (§1.3).</summary>
    uint _usp;

    /// <summary>`SSP` — the supervisor stack pointer, the one a reset loads (§1.3, §11).</summary>
    uint _ssp;

    /// <summary>
    /// `AU` — the address adder's register, and **the hardware's program counter**: the address of the
    /// next word to be fetched at a boundary (tutorial §1.4). `PC` is this cell's public view.
    /// </summary>
    uint _au;

    /// <summary>`PSW` — the status word (tutorial §1.2). `SR` and `CCR` are views over it.</summary>
    ushort _sr;

    // ---- the instruction pipeline: IRC → IR → IRD -----------------------------------------
    // Three cells, two strobes (`load IR`, `load IRD`), the shape §5.3 describes: the word fetched,
    // the word decoded, the word executed. An extension word comes out of `IR`, which is why the
    // queue runs a word ahead of the decoder.

    /// <summary>`IRD` — the word being executed (the decode PLAs' own input).</summary>
    ushort _ird;

    /// <summary>`IR` — the queue's head: the next word the decoder will see.</summary>
    ushort _ir;

    /// <summary>`IRC` — the word behind `IR`, i.e. what the last fetch brought in.</summary>
    ushort _irc;

    /// <summary>Whether `IRC` holds a real word (the pipeline is three deep only after a fetch).</summary>
    bool _ircValid;

    /// <summary>
    /// `AT` — the address temporary: the address of the word in `IR` (what a group-0/2 frame saves) and,
    /// at a jump, the target the stream is abandoning itself for.
    /// </summary>
    uint _at;

    // ---- the ALU's registers and latches ---------------------------------------------------
    // m_aluo / m_alub / m_alue / m_isr and the nanocode's `i=` width field. They are chip state and
    // survive across microstates, which is why they are fields and not locals: `morl2` reads what
    // `morl1` left.

    /// <summary>`ALUO` — the ALU's output latch; `sr_nzvc` reads its flags.</summary>
    uint _aluo;

    /// <summary>`ALUB` — the ALU's second-operand buffer (the long read's high half, the divisor).</summary>
    uint _alub;

    /// <summary>`ALUE` — the shift pair's other half (`ALUO:ALUE`), sixteen bits of semantics.</summary>
    uint _alue;

    /// <summary>`ISR` — the ALU's flag latch: a *stage* of `PSW`, not the status word itself.</summary>
    ushort _isr;

    /// <summary>The nanocode's `i=` width field, held across microstates (0 byte, 1 word, 2 long).</summary>
    int _aluWidth;

    /// <summary>`DBIN` — the word the last read put on the internal data bus (`m_dbin`).</summary>
    ushort _dbin;

    // ---- the two pad buffers (sheet 00: `ADDRESS BUFFERS`, `DATA BUFFER`) -------------------
    // The pads do not read the core: they read these two, and the bus cycle is the only writer. AOB
    // holds what is on A1–A23 between cycles and DOB what the part drives on D0–D15 during a write;
    // DT is the last operand the part actually transferred, read or written.

    /// <summary>`AOB` — the address output buffer: the level on `A1–A23`, and a fault's address.</summary>
    uint _aob;

    /// <summary>`DOB` — the data output buffer: the word the part drives on `D0–D15` for a write.</summary>
    ushort _dob;

    /// <summary>`DT` — the data temporary: the last operand transferred, in either direction.</summary>
    ushort _dt;

    // ---- the section accessors (ADR 0015 §3, tutorial §1.1) --------------------------------
    // A word write to a 32-bit register changes the low half and leaves the rest; the High section's
    // accessor is the same rule from the other end. These are the *only* legal way to write half of a
    // cell, so the rule is one place instead of a mask repeated at every call site.

    /// <summary>Write a word into a 32-bit cell's low half, leaving the high half standing (§1.1).</summary>
    static uint Set16L(uint cell, ushort value) => (cell & 0xFFFF0000u) | value;

    /// <summary>Write a word into a 32-bit cell's high half, leaving the low half standing (§1.1).</summary>
    static uint Set16H(uint cell, ushort value) => (cell & 0x0000FFFFu) | ((uint)value << 16);
}
