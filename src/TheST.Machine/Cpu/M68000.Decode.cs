// ============================================================================================
// M68000.Decode.cs — the decode: `Line` → `Family` (ADR 0015 §1, §10; tickets #85, #89).
//
// The die decodes an instruction word in three levels, and this file is the top two of them:
//
//   · **`Line`** — the opcode's top nibble. Not an architectural concept: it is the A1 PLA's coarsest
//     term, the *address partition* that says which block of the 65 536 encodings this is (tutorial §10
//     calls the same partition "the family selected by the top nibble"; the microcode dumps call it a
//     line, and only `$A` and `$F` are named in the manual, because only those raise exceptions).
//   · **`Family`** — one of the manual's §5 instruction families. One predicate each, named with the
//     manual's own word, because a reader who assembles `ADDX` must find `Addx`.
//
// The predicates are transcribed from the conditions the *core* already executes (`M68000.Instructions.cs`
// and `M68000.Scripts.cs`), not from a reading of the manual: those conditions are 100 % green on both
// corpus columns, so a disagreement between this file and them is a bug in this file by construction.
//
// The third level of the hardware — the PLA bank keyed on `IRD`, which turns a decoded word into ~130
// control lines (which register file RX/RY mean, the size, the vector) — is `Decoded`'s fields plus the
// scripts' own `IsScripted*` ledger; #89 fills it term by term.
// ============================================================================================

namespace TheST.Machine.Cpu;

public sealed partial class M68000
{
    /// <summary>The manual's §5 instruction families — one member per `Decode` predicate.</summary>
    public enum Family
    {
        Move, Movea, Moveq, Movem, Movep, Lea, Pea, Exg, Swap, Link, Unlk,
        MoveFromSr, MoveToSr, MoveToCcr, MoveUsp,
        Andi, Ori, Eori, Add, Adda, Addi, Addq, Addx, Sub, Suba, Subi, Subq, Subx,
        Cmp, Cmpa, Cmpi, Cmpm, Neg, Negx, Clr, Ext, Mulu, Muls, Divu, Divs,
        Abcd, Sbcd, Nbcd, And, Or, Eor, Not, Tst, Tas,
        Asl, Asr, Lsl, Lsr, Rol, Ror, Roxl, Roxr,
        Btst, Bchg, Bclr, Bset,
        Bcc, Bra, Bsr, Dbcc, Scc, Jmp, Jsr, Rts, Rtr, Rte,
        Trap, Trapv, Chk, Nop, Stop, Reset, Illegal,
    }

    /// <summary>
    /// What the decode knows about one instruction: its family, its line, the size (the core's own code —
    /// 0 byte, 1 word, 2 long), the destination's mode and register, the source's, and a branch's
    /// condition. A field a family does not use is 0, which is what the encoding itself says.
    /// </summary>
    public readonly record struct Decoded(Family Family, int Line, int Size, int Mode, int Register,
                                          int SourceMode, int SourceRegister, int Condition);

    /// <summary>
    /// The decode, as the hardware's two levels: the line partition and the family predicates.
    /// </summary>
    public static class Decode
    {
        /// <summary>The A1 PLA's coarsest term: the opcode's top nibble, 0–15 ($A and $F are the exceptions).</summary>
        public static int Line(ushort opcode) => opcode >> 12;

        /// <summary>The three-bit size code: 0 byte, 1 word, 2 long, 3 the line's special.</summary>
        public static int Size(ushort opcode) => (opcode >> 6) & 0x3;

        /// <summary>
        /// Decode one opcode. Returns false only for an encoding the part does not execute at all; every
        /// other answer names a family, `Illegal` included (the A line and the F line raise exceptions
        /// 10 and 11 — a core that answers "unknown" for them loses TOS's trap mechanism).
        /// </summary>
        public static bool TryDecode(ushort opcode, out Decoded instruction)
        {
            int line = Line(opcode);
            int size = Size(opcode);
            int mode = (opcode >> 3) & 0x7;
            int register = opcode & 0x7;
            int destinationRegister = (opcode >> 9) & 0x7;
            int sourceMode = (opcode >> 3) & 0x7;
            int sourceRegister = opcode & 0x7;
            int condition = (opcode >> 8) & 0xF;

            Decoded Make(Family family, int withSize = -1) => new(
                family, line, withSize < 0 ? size : withSize, mode, register,
                sourceMode, sourceRegister, condition);

            switch (line)
            {
                // ---- $1–$3: MOVE and MOVEA, the destination's mode field says which ------------------
                case 0x1:
                case 0x2:
                case 0x3:
                {
                    int moveSize = line == 0x1 ? 0 : line == 0x3 ? 1 : 2;
                    Family family = ((opcode >> 6) & 0x7) == 1 ? Family.Movea : Family.Move;
                    instruction = new Decoded(family, line, moveSize, opcode & 0x7, destinationRegister,
                                              (opcode >> 3) & 0x7, opcode & 0x7, 0);
                    return true;
                }

                case 0x7:
                    instruction = Make(Family.Moveq, 2);
                    return true;

                // ---- $0: the immediate and bit block --------------------------------------------
                case 0x0:
                {
                    bool registerForm = (opcode & 0x0100) != 0;

                    // MOVE to CCR / to SR: mode 7, register 4, a size of 0 or 1, and operation 0/1/5
                    if (!registerForm && mode == 7 && register == 4 && size <= 1
                        && destinationRegister is 0 or 1 or 5)
                    {
                        instruction = Make(destinationRegister switch
                        {
                            0 => Family.Ori,
                            1 => Family.Andi,
                            _ => Family.Eori,
                        });
                        return true;
                    }

                    // MOVEP lives in the bit-operation space: mode 001, bit 8 set, its own bits 7-6
                    if (registerForm && mode == 1)
                    {
                        instruction = Make(Family.Movep);
                        return true;
                    }

                    // the bit operations: dynamic (bit 8) and static ($08xx), the operation in bits 7-6
                    if (registerForm || (opcode & 0x0F00) == 0x0800)
                    {
                        instruction = Make(((opcode >> 6) & 0x3) switch
                        {
                            0 => Family.Btst,
                            1 => Family.Bchg,
                            2 => Family.Bclr,
                            _ => Family.Bset,
                        });
                        return true;
                    }

                    // the six immediates, the operation in bits 11-9
                    instruction = Make(destinationRegister switch
                    {
                        0 => Family.Ori,
                        1 => Family.Andi,
                        2 => Family.Subi,
                        3 => Family.Addi,
                        5 => Family.Eori,
                        6 => Family.Cmpi,
                        _ => Family.Illegal,
                    });
                    return true;
                }

                // ---- $4: the single-purpose block ----------------------------------------------
                case 0x4:
                {
                    // MOVE from CCR is 68010: EmuTOS's own probe relies on this trapping (bios/processor.S)
                    if ((opcode & 0xFF00) == 0x4200 && size == 3)
                    {
                        instruction = Make(Family.Illegal);
                        return true;
                    }

                    if ((opcode & 0x01C0) == 0x01C0) { instruction = Make(Family.Lea); return true; }
                    if ((opcode & 0x01C0) == 0x0180) { instruction = Make(Family.Chk); return true; }

                    if ((opcode & 0xFF00) == 0x4400 && size == 3) { instruction = Make(Family.MoveToCcr); return true; }
                    if ((opcode & 0xFF00) == 0x4600 && size == 3) { instruction = Make(Family.MoveToSr); return true; }

                    switch ((opcode >> 8) & 0xF)
                    {
                        case 0x0:
                            instruction = Make(size == 3 ? Family.MoveFromSr : Family.Negx);
                            return true;
                        case 0x2: instruction = Make(Family.Clr); return true;
                        case 0x4: instruction = Make(Family.Neg); return true;
                        case 0x6: instruction = Make(Family.Not); return true;
                        case 0xA:
                            instruction = Make((opcode & 0x00C0) == 0x00C0 ? Family.Tas : Family.Tst);
                            return true;
                        case 0x8:
                        {
                            if ((opcode & 0xFFF8) == 0x4840) { instruction = Make(Family.Swap); return true; }
                            if ((opcode & 0xFFF8) == 0x4880) { instruction = Make(Family.Ext, 1); return true; }
                            if ((opcode & 0xFFF8) == 0x48C0) { instruction = Make(Family.Ext, 2); return true; }
                            if ((opcode & 0xFF80) == 0x4880 && (opcode & 0x0038) >= 0x10)
                            {
                                instruction = Make(Family.Movem);
                                return true;
                            }

                            if ((opcode & 0xFFC0) == 0x4840) { instruction = Make(Family.Pea); return true; }
                            instruction = Make(Family.Nbcd, 0);
                            return true;
                        }

                        case 0xC:
                            instruction = Make(Family.Movem);
                            return true;

                        case 0xE:
                            instruction = Make(Miscellaneous(opcode));
                            return true;
                    }

                    instruction = Make(Family.Illegal);
                    return true;
                }

                // ---- $5: quick, condition and decrement-and-branch ------------------------------
                case 0x5:
                    instruction = Make(size == 3
                        ? (mode == 1 ? Family.Dbcc : Family.Scc)
                        : ((opcode & 0x0100) != 0 ? Family.Subq : Family.Addq));
                    return true;

                // ---- $6: the branches ----------------------------------------------------------
                case 0x6:
                    instruction = Make(condition switch
                    {
                        0x0 => Family.Bra,
                        0x1 => Family.Bsr,
                        _ => Family.Bcc,
                    });
                    return true;

                // ---- $8–$D: the two-operand ALU block and its odd members -----------------------
                case 0x8:
                {
                    if ((opcode & 0xF1F0) == 0x8100) { instruction = Make(Family.Sbcd, 0); return true; }
                    if (((opcode >> 6) & 0x4) != 0 && size != 3) { instruction = Make(Family.Or); return true; }
                    if (size == 3)
                    {
                        instruction = Make(((opcode >> 6) & 0x7) == 7 ? Family.Divs : Family.Divu, 1);
                        return true;
                    }

                    instruction = Make(Family.Or);
                    return true;
                }

                case 0x9:
                {
                    if ((opcode & 0xF130) == 0x9100 && size != 3) { instruction = Make(Family.Subx); return true; }
                    if (((opcode >> 6) & 0x4) != 0 && size != 3) { instruction = Make(Family.Sub); return true; }
                    if (size == 3) { instruction = Make(Family.Suba, ((opcode >> 6) & 0x7) == 7 ? 2 : 1); return true; }
                    instruction = Make(Family.Sub);
                    return true;
                }

                case 0xB:
                {
                    if ((opcode & 0xF138) == 0xB108 && size != 3) { instruction = Make(Family.Cmpm); return true; }
                    if ((opcode & 0x0100) != 0 && size != 3) { instruction = Make(Family.Eor); return true; }
                    if (size == 3) { instruction = Make(Family.Cmpa, ((opcode >> 6) & 0x7) == 7 ? 2 : 1); return true; }
                    instruction = Make(Family.Cmp);
                    return true;
                }

                case 0xC:
                {
                    if ((opcode & 0xF1F0) == 0xC100) { instruction = Make(Family.Abcd, 0); return true; }
                    if ((opcode & 0xF1F8) is 0xC140 or 0xC148 or 0xC188) { instruction = Make(Family.Exg); return true; }
                    if (size == 3)
                    {
                        instruction = Make(((opcode >> 6) & 0x7) == 3 ? Family.Mulu : Family.Muls, 1);
                        return true;
                    }

                    instruction = Make(Family.And);
                    return true;
                }

                case 0xD:
                {
                    if ((opcode & 0xF130) == 0xD100 && size != 3) { instruction = Make(Family.Addx); return true; }
                    if (((opcode >> 6) & 0x4) != 0 && size != 3) { instruction = Make(Family.Add); return true; }
                    if (size == 3) { instruction = Make(Family.Adda, ((opcode >> 6) & 0x7) == 7 ? 2 : 1); return true; }
                    instruction = Make(Family.Add);
                    return true;
                }

                // ---- $E: the shifts ------------------------------------------------------------
                case 0xE:
                {
                    int kind = size == 3 ? (opcode >> 9) & 0x3 : (opcode >> 3) & 0x3;
                    bool left = (opcode & 0x0100) != 0;
                    instruction = Make((kind, left) switch
                    {
                        (0, false) => Family.Asr,
                        (0, true) => Family.Asl,
                        (1, false) => Family.Lsr,
                        (1, true) => Family.Lsl,
                        (2, false) => Family.Roxr,
                        (2, true) => Family.Roxl,
                        (3, false) => Family.Ror,
                        _ => Family.Rol,
                    });
                    return true;
                }
            }

            // $A and $F are not "unknown": they raise exceptions 10 and 11 (tutorial §10)
            instruction = Make(Family.Illegal);
            return true;
        }

        /// <summary>The `$4E4x`–`$4E7x` block: a fixed shape per whole opcode, dispatched on the word.</summary>
        static Family Miscellaneous(ushort opcode) => opcode switch
        {
            0x4E70 => Family.Reset,
            0x4E71 => Family.Nop,
            0x4E72 => Family.Stop,
            0x4E73 => Family.Rte,
            0x4E75 => Family.Rts,
            0x4E76 => Family.Trapv,
            0x4E77 => Family.Rtr,
            _ when (opcode & 0xFFF0) == 0x4E40 => Family.Trap,
            _ when (opcode & 0xFFF8) == 0x4E50 => Family.Link,
            _ when (opcode & 0xFFF8) == 0x4E58 => Family.Unlk,
            _ when (opcode & 0xFFF0) == 0x4E60 => Family.MoveUsp,
            _ when (opcode & 0xFFC0) == 0x4E80 => Family.Jsr,
            _ when (opcode & 0xFFC0) == 0x4EC0 => Family.Jmp,
            _ => Family.Illegal,
        };

        static bool Only(ushort opcode, out Decoded instruction, Family family)
        {
            if (TryDecode(opcode, out instruction) && instruction.Family == family)
            {
                return true;
            }

            instruction = default;
            return false;
        }

        // ---- the manual's families, one entry point each (alphabetical, as §5 lists them) -----
        public static bool Abcd(ushort o, out Decoded i) => Only(o, out i, Family.Abcd);
        public static bool Add(ushort o, out Decoded i) => Only(o, out i, Family.Add);
        public static bool Adda(ushort o, out Decoded i) => Only(o, out i, Family.Adda);
        public static bool Addi(ushort o, out Decoded i) => Only(o, out i, Family.Addi);
        public static bool Addq(ushort o, out Decoded i) => Only(o, out i, Family.Addq);
        public static bool Addx(ushort o, out Decoded i) => Only(o, out i, Family.Addx);
        public static bool And(ushort o, out Decoded i) => Only(o, out i, Family.And);
        public static bool Andi(ushort o, out Decoded i) => Only(o, out i, Family.Andi);
        public static bool Asl(ushort o, out Decoded i) => Only(o, out i, Family.Asl);
        public static bool Asr(ushort o, out Decoded i) => Only(o, out i, Family.Asr);
        public static bool Bcc(ushort o, out Decoded i) => Only(o, out i, Family.Bcc);
        public static bool Bchg(ushort o, out Decoded i) => Only(o, out i, Family.Bchg);
        public static bool Bclr(ushort o, out Decoded i) => Only(o, out i, Family.Bclr);
        public static bool Bra(ushort o, out Decoded i) => Only(o, out i, Family.Bra);
        public static bool Bset(ushort o, out Decoded i) => Only(o, out i, Family.Bset);
        public static bool Bsr(ushort o, out Decoded i) => Only(o, out i, Family.Bsr);
        public static bool Btst(ushort o, out Decoded i) => Only(o, out i, Family.Btst);
        public static bool Chk(ushort o, out Decoded i) => Only(o, out i, Family.Chk);
        public static bool Clr(ushort o, out Decoded i) => Only(o, out i, Family.Clr);
        public static bool Cmp(ushort o, out Decoded i) => Only(o, out i, Family.Cmp);
        public static bool Cmpa(ushort o, out Decoded i) => Only(o, out i, Family.Cmpa);
        public static bool Cmpi(ushort o, out Decoded i) => Only(o, out i, Family.Cmpi);
        public static bool Cmpm(ushort o, out Decoded i) => Only(o, out i, Family.Cmpm);
        public static bool Dbcc(ushort o, out Decoded i) => Only(o, out i, Family.Dbcc);
        public static bool Divs(ushort o, out Decoded i) => Only(o, out i, Family.Divs);
        public static bool Divu(ushort o, out Decoded i) => Only(o, out i, Family.Divu);
        public static bool Eor(ushort o, out Decoded i) => Only(o, out i, Family.Eor);
        public static bool Eori(ushort o, out Decoded i) => Only(o, out i, Family.Eori);
        public static bool Exg(ushort o, out Decoded i) => Only(o, out i, Family.Exg);
        public static bool Ext(ushort o, out Decoded i) => Only(o, out i, Family.Ext);
        public static bool Illegal(ushort o, out Decoded i) => Only(o, out i, Family.Illegal);
        public static bool Jmp(ushort o, out Decoded i) => Only(o, out i, Family.Jmp);
        public static bool Jsr(ushort o, out Decoded i) => Only(o, out i, Family.Jsr);
        public static bool Lea(ushort o, out Decoded i) => Only(o, out i, Family.Lea);
        public static bool Link(ushort o, out Decoded i) => Only(o, out i, Family.Link);
        public static bool Lsl(ushort o, out Decoded i) => Only(o, out i, Family.Lsl);
        public static bool Lsr(ushort o, out Decoded i) => Only(o, out i, Family.Lsr);
        public static bool Move(ushort o, out Decoded i) => Only(o, out i, Family.Move);
        public static bool Movea(ushort o, out Decoded i) => Only(o, out i, Family.Movea);
        public static bool MoveFromSr(ushort o, out Decoded i) => Only(o, out i, Family.MoveFromSr);
        public static bool Movem(ushort o, out Decoded i) => Only(o, out i, Family.Movem);
        public static bool Movep(ushort o, out Decoded i) => Only(o, out i, Family.Movep);
        public static bool Moveq(ushort o, out Decoded i) => Only(o, out i, Family.Moveq);
        public static bool MoveToCcr(ushort o, out Decoded i) => Only(o, out i, Family.MoveToCcr);
        public static bool MoveToSr(ushort o, out Decoded i) => Only(o, out i, Family.MoveToSr);
        public static bool MoveUsp(ushort o, out Decoded i) => Only(o, out i, Family.MoveUsp);
        public static bool Muls(ushort o, out Decoded i) => Only(o, out i, Family.Muls);
        public static bool Mulu(ushort o, out Decoded i) => Only(o, out i, Family.Mulu);
        public static bool Nbcd(ushort o, out Decoded i) => Only(o, out i, Family.Nbcd);
        public static bool Neg(ushort o, out Decoded i) => Only(o, out i, Family.Neg);
        public static bool Negx(ushort o, out Decoded i) => Only(o, out i, Family.Negx);
        public static bool Nop(ushort o, out Decoded i) => Only(o, out i, Family.Nop);
        public static bool Not(ushort o, out Decoded i) => Only(o, out i, Family.Not);
        public static bool Or(ushort o, out Decoded i) => Only(o, out i, Family.Or);
        public static bool Ori(ushort o, out Decoded i) => Only(o, out i, Family.Ori);
        public static bool Pea(ushort o, out Decoded i) => Only(o, out i, Family.Pea);
        public static bool Reset(ushort o, out Decoded i) => Only(o, out i, Family.Reset);
        public static bool Rol(ushort o, out Decoded i) => Only(o, out i, Family.Rol);
        public static bool Ror(ushort o, out Decoded i) => Only(o, out i, Family.Ror);
        public static bool Roxl(ushort o, out Decoded i) => Only(o, out i, Family.Roxl);
        public static bool Roxr(ushort o, out Decoded i) => Only(o, out i, Family.Roxr);
        public static bool Rte(ushort o, out Decoded i) => Only(o, out i, Family.Rte);
        public static bool Rtr(ushort o, out Decoded i) => Only(o, out i, Family.Rtr);
        public static bool Rts(ushort o, out Decoded i) => Only(o, out i, Family.Rts);
        public static bool Sbcd(ushort o, out Decoded i) => Only(o, out i, Family.Sbcd);
        public static bool Scc(ushort o, out Decoded i) => Only(o, out i, Family.Scc);
        public static bool Stop(ushort o, out Decoded i) => Only(o, out i, Family.Stop);
        public static bool Sub(ushort o, out Decoded i) => Only(o, out i, Family.Sub);
        public static bool Suba(ushort o, out Decoded i) => Only(o, out i, Family.Suba);
        public static bool Subi(ushort o, out Decoded i) => Only(o, out i, Family.Subi);
        public static bool Subq(ushort o, out Decoded i) => Only(o, out i, Family.Subq);
        public static bool Subx(ushort o, out Decoded i) => Only(o, out i, Family.Subx);
        public static bool Swap(ushort o, out Decoded i) => Only(o, out i, Family.Swap);
        public static bool Tas(ushort o, out Decoded i) => Only(o, out i, Family.Tas);
        public static bool Trap(ushort o, out Decoded i) => Only(o, out i, Family.Trap);
        public static bool Trapv(ushort o, out Decoded i) => Only(o, out i, Family.Trapv);
        public static bool Tst(ushort o, out Decoded i) => Only(o, out i, Family.Tst);
        public static bool Unlk(ushort o, out Decoded i) => Only(o, out i, Family.Unlk);
    }
}
