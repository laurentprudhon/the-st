// ============================================================================================
// M68000.Pins.cs — the pads: the chip's face to the world (ADR 0015 §1, §2; tickets #85, #88).
//
// The pins are **the interface**, not a view of the part's insides: they are the levels the engine's
// wires carry, and a reader of `Pins` is reading the same records the machine's other chips do. That
// is why this file decodes the bus's record group — the contract `SystemControlUnit.PerformBusCycle`
// already relies on — instead of reaching into the core's fields. The core's internal copies of the
// same facts are latches of its own (`AOB`, `DBIN`, `DOB`, #86), and they are *below* this layer.
//
// The bus is one wire carrying one cycle as a positional group (ADR 0004's "a bus is one masked wire",
// used as an ordered tuple): at the assertion instant the chip writes
//
//     [0xFF00] A1–A23's high half   [0xFFFE] the even address   [0xFFFF] the control word
//     [0xFFFF] the data word
//
// and at the answer instant it writes the release, mask 0x8000 — "AS high, Z on the data lanes". The
// control word is `RW`=0x0100, `size`=(word)?0x10:0, `odd`=0x01 in the low nibble, `FC0–FC2`=bits 12–14.
//
// What this file does NOT own, and #88 does: the byte lane's exact drive (UDS/LDS as a *lane*, the
// mid-word cases), the `S0`…`S7` window a handshake belongs to, and `DTACK`/`BERR` sampling as the
// state machine sees them. Those members read the last cycle's records here; #88 turns them into the
// bus state machine's own levels. A member that keeps a previous cycle's value is the `--clocks`
// frame-columns bug again — so a handshake reads false between cycles, never "whatever was last".
//
// One structural note: C# will not let a member and a nested type share a name (CS0102), so the
// accessor is **`Pads`** — the die's own word for the edge block (`pads and buffers`, sheet 00) —
// and the nested type keeps the ADR's name `Pins`: `cpu.Pads.A1`, `cpu.Pads.DTACK`.
// ============================================================================================

namespace TheST.Machine.Cpu;

public sealed partial class M68000
{
    /// <summary>
    /// The signals of tutorial §8.1, one member per signal, each a level (`bool`). **There is no `A0`
    /// pin**: an odd byte access is `A1`=1 with `LDS` asserted (tutorial §2, §8.1).
    /// </summary>
    public sealed class Pins
    {
        readonly M68000 _cpu;

        internal Pins(M68000 cpu) => _cpu = cpu;

        /// <summary>One cycle of the bus wire: the four assertion records and the release.</summary>
        readonly record struct BusCycle(ushort AddressHigh, ushort AddressLow, ushort Control, ushort Data,
                                        bool Asserted, bool Released, bool Faulted);

        /// <summary>
        /// The most recent cycle at or before the chip's clock — the levels a reader sees between
        /// cycles are the ones that cycle left standing, and only the handshakes go back to false.
        /// </summary>
        BusCycle Current()
        {
            ValueLog bus = _cpu.Engine.Log(Wires.Bus);
            int at = bus.IndexAtOrBefore(_cpu.Clock);
            if (at < 0) return default;

            // the release is its own record (mask 0x8000); the group's four sit before it
            bool released = bus[at].Mask == 0x8000;
            int data = released ? at - 1 : at;
            if (data < 3) return default;

            ushort answer = _cpu.Engine.ValueAt(Wires.Answer, _cpu.Clock);
            return new BusCycle(
                bus[data - 3].Value,
                bus[data - 2].Value,
                bus[data - 1].Value,
                bus[data].Value,
                Asserted: !released,
                Released: released,
                Faulted: (answer & 0x0002) != 0);
        }

        bool AddressBit(int bit)
        {
            BusCycle cycle = Current();
            uint address = ((uint)cycle.AddressHigh << 16) | cycle.AddressLow;
            return ((address >> bit) & 1) != 0;
        }

        bool DataBit(int bit) => ((Current().Data >> bit) & 1) != 0;

        bool ControlBit(int mask) => (Current().Control & mask) != 0;

        /// <summary>Word (`size`=1) or byte (`size`=0), from the control word's bit 4.</summary>
        bool Word => ControlBit(0x0010);

        /// <summary>A byte access on an odd address, from the control word's low nibble.</summary>
        bool OddLane => (Current().Control & 0x0F) != 0;

        // ---- the address bus: A1–A23, and no A0 ------------------------------------------------
        public bool A1 => AddressBit(1);
        public bool A2 => AddressBit(2);
        public bool A3 => AddressBit(3);
        public bool A4 => AddressBit(4);
        public bool A5 => AddressBit(5);
        public bool A6 => AddressBit(6);
        public bool A7 => AddressBit(7);
        public bool A8 => AddressBit(8);
        public bool A9 => AddressBit(9);
        public bool A10 => AddressBit(10);
        public bool A11 => AddressBit(11);
        public bool A12 => AddressBit(12);
        public bool A13 => AddressBit(13);
        public bool A14 => AddressBit(14);
        public bool A15 => AddressBit(15);
        public bool A16 => AddressBit(16);
        public bool A17 => AddressBit(17);
        public bool A18 => AddressBit(18);
        public bool A19 => AddressBit(19);
        public bool A20 => AddressBit(20);
        public bool A21 => AddressBit(21);
        public bool A22 => AddressBit(22);
        public bool A23 => AddressBit(23);

        // ---- the data bus: D15–D8 are the even byte, D7–D0 the odd one -------------------------
        public bool D0 => DataBit(0);
        public bool D1 => DataBit(1);
        public bool D2 => DataBit(2);
        public bool D3 => DataBit(3);
        public bool D4 => DataBit(4);
        public bool D5 => DataBit(5);
        public bool D6 => DataBit(6);
        public bool D7 => DataBit(7);
        public bool D8 => DataBit(8);
        public bool D9 => DataBit(9);
        public bool D10 => DataBit(10);
        public bool D11 => DataBit(11);
        public bool D12 => DataBit(12);
        public bool D13 => DataBit(13);
        public bool D14 => DataBit(14);
        public bool D15 => DataBit(15);

        // ---- the strobes -----------------------------------------------------------------------
        /// <summary>Address strobe: asserted from the cycle's assertion until its release record.</summary>
        public bool AS => Current().Asserted;

        /// <summary>The even byte's data strobe: word cycles and even-address byte cycles.</summary>
        public bool UDS => Current().Asserted && (Word || !OddLane);

        /// <summary>The odd byte's data strobe: word cycles and odd-address byte cycles.</summary>
        public bool LDS => Current().Asserted && (Word || OddLane);

        // ---- control ---------------------------------------------------------------------------
        /// <summary>The manual's `R/W` (tutorial §8.1), true for a write.</summary>
        public bool RW => ControlBit(0x0100);

        /// <summary>
        /// The slave has answered the cycle the pins are in; false once the cycle is released, because
        /// between cycles a handshake is not a level (the `S0`…`S7` window is #88's).
        /// </summary>
        public bool DTACK => Current().Released;

        /// <summary>A bus error on that cycle; false between cycles, like <see cref="DTACK"/>.</summary>
        public bool BERR => Current().Released && Current().Faulted;

        /// <summary>Halt: the part has stopped, which is the chip's own statement of the same fact.</summary>
        public bool HALT => _cpu._halted;

        /// <summary>
        /// Reset: an input the System Control Unit drives, and an output to the peripherals — nothing
        /// on this side models it, so the level reads false (the owner's ruling on undriven pins).
        /// </summary>
        public bool RESET => false;

        /// <summary>The CPU clock's phase: high in the first half of the four-tick window (ADR 0001).</summary>
        public bool CLK => _cpu.Clock % (2 * Instruments.TicksPerCpuClock) < Instruments.TicksPerCpuClock;

        // ---- the space -------------------------------------------------------------------------
        public bool FC0 => ControlBit(0x1000);
        public bool FC1 => ControlBit(0x2000);
        public bool FC2 => ControlBit(0x4000);

        // ---- the interrupt lines, as the System Control Unit drives them -----------------------
        int Level => _cpu.Engine.ValueAt(Wires.Ipl, _cpu.Clock) & 0x7;

        public bool IPL0 => (Level & 0x1) != 0;
        public bool IPL1 => (Level & 0x2) != 0;
        public bool IPL2 => (Level & 0x4) != 0;

        // ---- arbitration and the 6800 group: pins in §8.1's list, no protocol behind them ------
        /// <summary>Bus request: the campaign keeps arbitration out of scope (ADR 0015 §1.1).</summary>
        public bool BR => false;

        /// <summary>Bus grant: as <see cref="BR"/>.</summary>
        public bool BG => false;

        /// <summary>Bus grant acknowledge: as <see cref="BR"/>.</summary>
        public bool BGACK => false;

        /// <summary>The 6800 group's E: a level and nothing else — the owner's ruling.</summary>
        public bool E => false;

        /// <summary>VPA: in §8.1's list, no protocol behind it.</summary>
        public bool VPA => false;

        /// <summary>VMA: as <see cref="VPA"/>.</summary>
        public bool VMA => false;
    }

    /// <summary>
    /// The pads (ADR 0015 §1, §2). A nested type is a *place*, reached through this accessor — named
    /// <c>Pads</c> because C# will not let a member and a nested type carry one name (CS0102), and
    /// because the die calls this edge block the pads: `cpu.Pads.A1`, `cpu.Pads.DTACK`.
    /// </summary>
    public Pins Pads { get; }
}
