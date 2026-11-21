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
                    HaltReason = $"halted at pc={_cpu._pc:x6} sp={_cpu._ssp:x8} t={_cpu.Clock}";
                }

                return _cpu._halted;
            }
        }

        /// <summary>Why it stopped, as the harness reports it — a string, not a latch.</summary>
        public string? HaltReason { get; private set; }

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
