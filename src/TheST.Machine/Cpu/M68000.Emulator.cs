// ============================================================================================
// M68000.Emulator.cs — the emulator's interface, not the part (ADR 0015 §6, ticket #85).
//
// Nothing in the core calls into what lives here; the harness reads it, and it is the one file
// allowed to hold time. Three rules, and they are the whole reason the block is a file:
//
//   · the counters and the per-record instruments are the *emulator's* facts, not the chip's state
//     — the part's own statement of the same thing is Pins.HALT and the sequencer's STOP state;
//   · every member is a view over the machine below (or the counter that reports it), never a second
//     copy of the core's state, and the manual has no word for any of them — which is why they are
//     inside a nested type and not on the surface (ADR 0015 §2);
//   · a duration is not storage: its name carries its unit and it may live only in this file
//     (ADR 0015 §5.1). `grep -nE 'Clocks|Ticks' src/TheST.Machine/Cpu/M68000.*.cs` hits this file,
//     a script's own local, and Pins.CLK — nothing else.
//
// Build split, by consumer (§6, and the trap that cost a session): a counter the **Release** build
// reads stays unconditional, because `boot` prints Instructions / BusCycles and cpu-dissection reaches
// them by reflection — a blanket `#if DEBUG` breaks the Release build and the tool, and neither break
// is visible to the corpus. Only the per-record instruments — the three observers and the seven
// LastFrame* columns — sit behind `#if DEBUG`, because a per-cycle observer in Release costs the very
// thing G8 measures.
// ============================================================================================

namespace TheST.Machine.Cpu;

public sealed partial class M68000
{
    /// <summary>
    /// The emulator's interface (ADR 0015 §1, §6). Each member is documented as *not the part*: the
    /// chip has no such register, no such pin and no such counter, and none of them is graded.
    /// </summary>
    public sealed class Instruments
    {
        readonly M68000 _cpu;

        internal Instruments(M68000 cpu) => _cpu = cpu;

        /// <summary>One CPU clock is four ticks of the 32 MHz oscillator (ADR 0001) — the engine's unit.</summary>
        public const int TicksPerCpuClock = 4;

        // ---- the counters the Release boot reports and cpu-dissection reads by reflection ----------
        /// <summary>Instructions retired.</summary>
        public long Instructions { get; internal set; }

        /// <summary>Bus cycles issued (one per transfer, as the pins see it).</summary>
        public long BusCycles { get; internal set; }

        /// <summary>The internal clocks the core charged between bus cycles.</summary>
        public long InternalClocks { get; internal set; }

        /// <summary>Interrupts taken (the level sampler's verdict, not the pin).</summary>
        public long InterruptsTaken { get; internal set; }

        /// <summary>Exception entries written.</summary>
        public long Exceptions { get; internal set; }

        /// <summary>A debug instruction hook: instant, opcode and the address it was read from.</summary>
        public Action<long, ushort, uint>? Observer { get; set; }

        // ---- the harness's diagnosis of a quiet machine ------------------------------------------
        /// <summary>
        /// Whether the chip stopped of its own accord. The part's own statement of the same fact is
        /// <see cref="Pins.HALT"/> held with the sequencer in its STOP state (ADR 0015 §1's table).
        /// </summary>
        public bool Halted
        {
            get
            {
                if (_cpu._halted && HaltReason is null)
                {
                    HaltReason = $"halted at pc={_cpu._au:x6} sp={_cpu._ssp:x8} t={_cpu.Clock}";
                }

                return _cpu._halted;
            }
        }

        /// <summary>Why it stopped, as the harness reports it — a string, not a latch.</summary>
        public string? HaltReason { get; private set; }

        // ---- the manual's state as text (§1.1) -------------------------------------------------
        // Each is *composed on read* from what the part holds — never built per instruction, because a
        // string per instruction in the Release boot is a G8 cost with no owner. `Family` is null and
        // `Instruction` reads "—" at an instruction boundary, so an instrument never shows the previous
        // instruction's value (the `--clocks` frame-columns bug, refused by name in §1.1).

        /// <summary>The word being executed, and whether the part is inside an instruction at all.</summary>
        internal ushort ExecutingWord;

        /// <summary>True between an instruction's decode and its closing steps.</summary>
        internal bool InInstruction;

        /// <summary>The family of the instruction being executed, or null at a boundary (§1.1).</summary>
        public M68000.Family? Family =>
            InInstruction && M68000.Decode.TryDecode(ExecutingWord, out M68000.Decoded decoded)
                ? decoded.Family
                : null;

        /// <summary>
        /// The instruction being executed, in the manual's own syntax — `"MOVE.W D0,(A1)+"` — or "—" at a
        /// boundary. Operands are composed from the encoding; a displacement or an immediate that lives
        /// in an extension word reads as `d16(An)` or `#…`, because the extension word is the queue's, not
        /// the opcode's.
        /// </summary>
        public string Instruction
        {
            get
            {
                if (!InInstruction || !M68000.Decode.TryDecode(ExecutingWord, out M68000.Decoded decoded))
                {
                    return "—";
                }

                return Format(ExecutingWord, decoded);
            }
        }

        /// <summary>The bus cycle's state (§0.5, §8.2): `S0` idle, `S2` with the strobes asserted.</summary>
        public string BusState => _cpu.Pads.AS ? "S2" : "S0";

        /// <summary>The exception entry's step (§6), empty outside an entry (the sequencer is #93's).</summary>
        public string EntryStep => string.Empty;

        /// <summary>The interrupt's disposition (§7), from the level the pads show against the mask.</summary>
        public string Interrupt
        {
            get
            {
                int level = (_cpu.Pads.IPL2 ? 4 : 0) | (_cpu.Pads.IPL1 ? 2 : 0) | (_cpu.Pads.IPL0 ? 1 : 0);
                if (level == 0) return "none";
                int mask = (_cpu.I2 ? 4 : 0) | (_cpu.I1 ? 2 : 0) | (_cpu.I0 ? 1 : 0);
                return level > mask || level == 7 ? "taken" : "masked";
            }
        }

        // ---- the syntax (the manual's own spelling, tutorial §5) --------------------------------

        static string SizeSuffix(int size) => size switch { 0 => ".B", 1 => ".W", 2 => ".L", _ => string.Empty };

        static string Ea(int mode, int register) => mode switch
        {
            0 => $"D{register}",
            1 => $"A{register}",
            2 => $"(A{register})",
            3 => $"(A{register})+",
            4 => $"-(A{register})",
            5 => $"d16(A{register})",
            6 => $"d8(A{register},Xn)",
            7 => register switch
            {
                0 => "abs.W",
                1 => "abs.L",
                2 => "d16(PC)",
                3 => "d8(PC,Xn)",
                4 => "#…",
                _ => "?",
            },
            _ => "?",
        };

        static string Format(ushort opcode, M68000.Decoded d)
        {
            string name = d.Family.ToString().ToUpperInvariant();
            string size = SizeSuffix(d.Size);
            string one = Ea(d.Mode, d.Register);
            string two = Ea(d.SourceMode, d.SourceRegister);

            switch (d.Family)
            {
                case M68000.Family.Move:
                case M68000.Family.Movea:
                    return $"{name}{size} {Ea((opcode >> 3) & 0x7, opcode & 0x7)},{Ea((opcode >> 6) & 0x7, (opcode >> 9) & 0x7)}";

                case M68000.Family.Moveq:
                    return $"MOVEQ #{opcode & 0xFF},D{(opcode >> 9) & 0x7}";

                case M68000.Family.Add:
                case M68000.Family.Sub:
                case M68000.Family.And:
                case M68000.Family.Or:
                case M68000.Family.Eor:
                case M68000.Family.Cmp:
                    return ((opcode >> 6) & 0x7) == 4
                        ? $"{name}{size} D{(opcode >> 9) & 0x7},{one}"
                        : $"{name}{size} {two},D{(opcode >> 9) & 0x7}";

                case M68000.Family.Adda:
                case M68000.Family.Suba:
                case M68000.Family.Cmpa:
                    return $"{name}{size} {two},A{(opcode >> 9) & 0x7}";

                case M68000.Family.Addq:
                case M68000.Family.Subq:
                {
                    int quick = (opcode >> 9) & 0x7;
                    return $"{name}{size} #{(quick == 0 ? 8 : quick)},{one}";
                }

                case M68000.Family.Addx:
                case M68000.Family.Subx:
                case M68000.Family.Cmpm:
                case M68000.Family.Abcd:
                case M68000.Family.Sbcd:
                    return $"{name}{size} {two},{one}";

                case M68000.Family.Btst:
                case M68000.Family.Bchg:
                case M68000.Family.Bclr:
                case M68000.Family.Bset:
                    return (opcode & 0x0100) != 0
                        ? $"{name} D{(opcode >> 9) & 0x7},{one}"
                        : $"{name} #…,{one}";

                case M68000.Family.Bcc:
                    return $"B{ConditionName((opcode >> 8) & 0xF)} <label>";

                case M68000.Family.Scc:
                    return $"S{ConditionName((opcode >> 8) & 0xF)} {one}";

                case M68000.Family.Dbcc:
                    return $"DB{ConditionName((opcode >> 8) & 0xF)} D{opcode & 0x7},<label>";

                case M68000.Family.Asl:
                case M68000.Family.Asr:
                case M68000.Family.Lsl:
                case M68000.Family.Lsr:
                case M68000.Family.Rol:
                case M68000.Family.Ror:
                case M68000.Family.Roxl:
                case M68000.Family.Roxr:
                    if (d.Size == 3) return $"{name} {one}";
                    if ((opcode & 0x0020) != 0) return $"{name}{size} D{(opcode >> 9) & 0x7},D{opcode & 0x7}";
                    {
                        int count = (opcode >> 9) & 0x7;
                        return $"{name}{size} #{(count == 0 ? 8 : count)},D{opcode & 0x7}";
                    }

                case M68000.Family.Trap:
                    return $"TRAP #{opcode & 0xF}";

                case M68000.Family.Exg:
                    return (opcode & 0xFFF8) == 0xC140
                        ? $"EXG D{(opcode >> 9) & 0x7},D{opcode & 0x7}"
                        : (opcode & 0xFFF8) == 0xC148
                            ? $"EXG A{(opcode >> 9) & 0x7},A{opcode & 0x7}"
                            : $"EXG D{(opcode >> 9) & 0x7},A{opcode & 0x7}";

                case M68000.Family.Ext:
                    return $"EXT{size} D{opcode & 0x7}";

                case M68000.Family.Swap:
                    return $"SWAP D{opcode & 0x7}";

                case M68000.Family.Lea:
                case M68000.Family.Pea:
                case M68000.Family.Jmp:
                case M68000.Family.Jsr:
                case M68000.Family.Neg:
                case M68000.Family.Negx:
                case M68000.Family.Clr:
                case M68000.Family.Not:
                case M68000.Family.Tst:
                case M68000.Family.Tas:
                case M68000.Family.Nbcd:
                case M68000.Family.Movem:
                case M68000.Family.MoveFromSr:
                case M68000.Family.MoveToSr:
                case M68000.Family.MoveToCcr:
                    return $"{name} {one}";

                default:
                    return name;
            }
        }

        static string ConditionName(int condition) => condition switch
        {
            0x2 => "HI", 0x3 => "LS", 0x4 => "CC", 0x5 => "CS", 0x6 => "NE", 0x7 => "EQ",
            0x8 => "VC", 0x9 => "VS", 0xA => "PL", 0xB => "MI", 0xC => "GE", 0xD => "LT",
            0xE => "GT", 0xF => "LE", 0x0 => "RA", _ => "SR",
        };

#if DEBUG
        // ---- the per-record instruments (--trace / --clocks print them) --------------------------
        /// <summary>
        /// Every bus cycle the core performs, as the corpus's own <c>transactions</c> record it —
        /// write?, function code, address, data. It exists so the core's cycle list can be printed
        /// beside a record's reference trace (ADR 0009 §4's revision: that trace is the bar).
        /// </summary>
        public Action<bool, int, uint, ushort>? CycleObserver { get; set; }

        /// <summary>
        /// Every internal clock the core charges, in order with its bus cycles. The corpus states the
        /// same thing as its <c>["n", cycles]</c> transactions, so this is what makes the *placement*
        /// of the internal work comparable, not only its total.
        /// </summary>
        public Action<int>? ClockObserver { get; set; }

        /// <summary>The group-0/1 frame's saved program counter candidates, written at an entry.</summary>
        public uint LastFramePc { get; internal set; }

        public uint LastFrameFetchPointer { get; internal set; }

        public uint LastFramePcCounter { get; internal set; }

        public uint LastFrameIrAddress { get; internal set; }

        /// <summary>Whether the instruction had fetched a word of its own, and whether its trailing fetch ran.</summary>
        public bool LastFrameRefetched { get; internal set; }

        public bool LastFrameTrailing { get; internal set; }

        /// <summary>The vector whose frame was written (2/3 are the group-0 frames).</summary>
        public int LastFrameVector { get; internal set; }
#endif
    }

    /// <summary>
    /// The emulator's view of this chip (ADR 0015 §1, §6): `cpu.Emulator.BusCycles`. The nested type is
    /// a *place*, so it is reached through this accessor — named <c>Emulator</c> because C# will not let
    /// a member and a nested type carry one name (CS0102), and because this is the emulator's face, not
    /// the part's. It owns the counters and the per-record instruments, never the part's state.
    /// </summary>
    public Instruments Emulator { get; }
}
