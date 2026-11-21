using System.Reflection;
using System.Text;

namespace TheST.Harness;

/// <summary>
/// <c>cpu-surface</c> — the freeze test of ADR 0015 §1 (#85): the chip's declared public surface is the
/// manual's programmer's model, four nested types and one verb, and nothing else.
///
/// <para>
/// The allow-list below is ADR 0015 §1 transcribed — the register table, §8.1's signal list (no <c>A0</c>
/// pin), the <c>Instruments</c> table, the manual's §5 family list — and the check is reflection against it
/// in both directions: a member the ADR names and the type lacks is <b>missing</b>; a public member the type
/// declares and the ADR does not name is <b>extra</b>. The second direction is the one that matters: a
/// surface only fails from members nobody asked for.
/// </para>
///
/// <para>
/// <b>Two modes.</b> The default is the freeze: any divergence exits non-zero, and that is how the row of
/// <c>scripts/validate.sh</c> will read once the surface is the manual's. Until then, <c>--audit</c> prints
/// the same report and exits 0, so the campaign can quote the remaining work as a list instead of failing on
/// it (#84's ruling: the gate goes live when the surface is converted, not while it is being converted).
/// </para>
///
/// <para>
/// What it can and cannot see (§5 of `design/cpu-silicon.md`): names, kinds and writability. It cannot see
/// that a member is a *view* rather than a second copy of state, and it cannot see what a member computes —
/// the corpus, the boot's probes and the code's own `grep`s are those witnesses.
/// </para>
/// </summary>
internal static class SurfaceCommand
{
    const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>The programmer's model (ADR 0015 §1) — name, kind, and whether the manual makes it writable.</summary>
    static readonly (string Name, Type Kind, bool Writable)[] Registers =
    {
        ("D0", typeof(uint), true), ("D1", typeof(uint), true), ("D2", typeof(uint), true), ("D3", typeof(uint), true),
        ("D4", typeof(uint), true), ("D5", typeof(uint), true), ("D6", typeof(uint), true), ("D7", typeof(uint), true),
        ("A0", typeof(uint), true), ("A1", typeof(uint), true), ("A2", typeof(uint), true), ("A3", typeof(uint), true),
        ("A4", typeof(uint), true), ("A5", typeof(uint), true), ("A6", typeof(uint), true), ("A7", typeof(uint), true),
        ("PC", typeof(uint), true),
        ("SR", typeof(ushort), true), ("CCR", typeof(byte), true),
        ("USP", typeof(uint), true), ("SSP", typeof(uint), true),
        ("T", typeof(bool), true), ("S", typeof(bool), true),
        ("I2", typeof(bool), true), ("I1", typeof(bool), true), ("I0", typeof(bool), true),
        ("X", typeof(bool), true), ("N", typeof(bool), true), ("Z", typeof(bool), true),
        ("V", typeof(bool), true), ("C", typeof(bool), true),
    };

    /// <summary>§8.1's signals, one <c>bool</c> a level — <b>and no <c>A0</c></b>: an odd byte is <c>A1</c>=1 with <c>LDS</c>.</summary>
    static readonly string[] PinMembers = BuildPins();

    static string[] BuildPins()
    {
        var names = new List<string>();
        for (int bit = 1; bit <= 23; bit++) names.Add($"A{bit}");
        for (int bit = 0; bit <= 15; bit++) names.Add($"D{bit}");
        names.AddRange(new[]
        {
            "AS", "UDS", "LDS",
            "RW", "DTACK", "BERR", "HALT", "RESET", "CLK",
            "FC0", "FC1", "FC2",
            "IPL0", "IPL1", "IPL2",
            "BR", "BG", "BGACK",
            "E", "VPA", "VMA",
        });
        return names.ToArray();
    }

    /// <summary>The emulator's interface (ADR 0015 §1, §6) — the counters unconditional, the per-record instruments <c>#if DEBUG</c>.</summary>
    static readonly string[] InstrumentMembers =
    {
        "TicksPerCpuClock",
        "Instructions", "BusCycles", "InternalClocks", "InterruptsTaken", "Exceptions",
        "Halted", "HaltReason", "Observer",
        "CycleObserver", "ClockObserver",
        "LastFramePc", "LastFrameFetchPointer", "LastFramePcCounter", "LastFrameIrAddress",
        "LastFrameRefetched", "LastFrameTrailing", "LastFrameVector",
        // §1.1: the manual's state as text, composed on read
        "Instruction", "Family", "BusState", "EntryStep", "Interrupt",
    };

    /// <summary>The manual's §5 tables and appendix, one function a line (ADR 0015 §1).</summary>
    static readonly string[] Families =
    {
        "Move", "Movea", "Moveq", "Movem", "Movep", "Lea", "Pea", "Exg", "Swap", "Link", "Unlk",
        "MoveFromSr", "MoveToSr", "MoveToCcr", "MoveUsp",
        "Andi", "Ori", "Eori", "Add", "Adda", "Addi", "Addq", "Addx", "Sub", "Suba", "Subi", "Subq", "Subx",
        "Cmp", "Cmpa", "Cmpi", "Cmpm", "Neg", "Negx", "Clr", "Ext", "Mulu", "Muls", "Divu", "Divs",
        "Abcd", "Sbcd", "Nbcd", "And", "Or", "Eor", "Not", "Tst", "Tas",
        "Asl", "Asr", "Lsl", "Lsr", "Rol", "Ror", "Roxl", "Roxr",
        "Btst", "Bchg", "Bclr", "Bset",
        "Bcc", "Bra", "Bsr", "Dbcc", "Scc", "Jmp", "Jsr", "Rts", "Rtr", "Rte",
        "Trap", "Trapv", "Chk", "Nop", "Stop", "Reset", "Illegal",
    };

    /// <summary>The corpus seam (#34) — the programmer's model plus the two prefetch words.</summary>
    static readonly string[] SeamMembers = { "Load", "Capture", "StepAsync" };

    internal static int Run(string[] args)
    {
        bool audit = false;
        foreach (string argument in args)
        {
            switch (argument)
            {
                case "--audit":
                    audit = true;
                    break;
                default:
                    return Program.Unknown(argument);
            }
        }

        Type cpu = typeof(TheST.Machine.Cpu.M68000);
        var report = new StringBuilder();
        int divergences = 0;

        report.AppendLine($"cpu-surface — {cpu.FullName} against ADR 0015 §1 ({(audit ? "audit" : "freeze")})");

        // ---- the programmer's model ---------------------------------------------------------
        int present = 0;
        var registerNotes = new List<string>();
        foreach ((string name, Type kind, bool writable) in Registers)
        {
            PropertyInfo? property = cpu.GetProperty(name, Declared);
            if (property is null)
            {
                registerNotes.Add($"missing {name,-4} {kind.Name}{(writable ? " r/w" : string.Empty)}");
                continue;
            }

            present++;
            if (property.PropertyType != kind)
            {
                registerNotes.Add($"kind    {name,-4} is {property.PropertyType.Name}, the manual's is {kind.Name}");
                continue;
            }

            if (writable && !(property.CanRead && property.CanWrite))
            {
                registerNotes.Add($"write   {name,-4} is read-only, the manual writes it");
            }
        }

        report.AppendLine($"  registers        {present} of {Registers.Length} present"
                          + (registerNotes.Count == 0 ? string.Empty : $", {registerNotes.Count} divergence(s)"));
        foreach (string note in registerNotes) report.AppendLine($"    {note}");
        divergences += registerNotes.Count;

        // ---- the four nested types and the verb ---------------------------------------------
        foreach ((string nested, string[] members) in new[]
                 {
                     ("Pins", PinMembers),
                     ("Instruments", InstrumentMembers),
                     ("Decode", Families),
                     ("SingleStep", SeamMembers),
                 })
        {
            Type? type = cpu.GetNestedType(nested, BindingFlags.Public);
            if (type is null)
            {
                report.AppendLine($"  {nested,-16} absent — {members.Length} member(s) the ADR names");
                divergences++;
                continue;
            }

            var missing = new List<string>();
            foreach (string member in members)
            {
                bool found = nested switch
                {
                    "Pins" => type.GetProperty(member, Declared)?.PropertyType == typeof(bool)
                              || type.GetField(member, Declared)?.FieldType == typeof(bool),
                    "Decode" => type.GetMethod(member, Declared) is not null,
                    "SingleStep" => type.GetMethod(member, Declared) is not null,
                    _ => type.GetProperty(member, Declared) is not null || type.GetField(member, Declared) is not null,
                };
                if (!found) missing.Add(member);
            }

            report.AppendLine($"  {nested,-16} {members.Length - missing.Count} of {members.Length} present"
                              + (missing.Count == 0 ? string.Empty : "  missing: " + string.Join(", ", missing)));
            divergences += missing.Count;

            // Decode's enum must carry the same names as its functions (a family in one and not the other fails)
            if (nested == "Decode")
            {
                Type? family = cpu.GetNestedType("Family", BindingFlags.Public);
                if (family is null || !family.IsEnum)
                {
                    report.AppendLine("    Decode.Family  absent — the enum must carry one member per function");
                    divergences++;
                }
                else
                {
                    var declared = Enum.GetNames(family).ToHashSet(StringComparer.Ordinal);
                    var functions = Families.ToHashSet(StringComparer.Ordinal);
                    var onlyEnum = declared.Except(functions).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                    var onlyFunction = functions.Except(declared).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                    if (onlyEnum.Length == 0 && onlyFunction.Length == 0)
                    {
                        report.AppendLine($"    Family         {declared.Count} members, equal to the function list");
                    }
                    else
                    {
                        if (onlyEnum.Length > 0) report.AppendLine("    Family only:   " + string.Join(", ", onlyEnum));
                        if (onlyFunction.Length > 0) report.AppendLine("    functions only:" + string.Join(", ", onlyFunction));
                        divergences += onlyEnum.Length + onlyFunction.Length;
                    }
                }
            }
        }

        bool launch = cpu.GetMethod("Launch", Declared) is not null
                      || cpu.GetMethod("Launch", BindingFlags.Public | BindingFlags.Instance) is not null;
        report.AppendLine($"  Launch           {(launch ? "present" : "missing — the engine's verb, Machine.cs starts every chip that way")}");
        if (!launch) divergences++;

        // ---- nothing else ---------------------------------------------------------------------
        // The other direction, which is the one a surface fails from: a public member nobody asked for.
        var allowed = Registers.Select(register => register.Name).ToHashSet(StringComparer.Ordinal);
        allowed.Add("Launch");
        var extras = new List<string>();
        foreach (MemberInfo member in cpu.GetMembers(Declared).OrderBy(member => member.Name, StringComparer.Ordinal))
        {
            if (member is Type) continue;                      // the nested types are checked above
            if (member is MethodInfo method && method.IsSpecialName) continue;   // property/event accessors
            if (member is ConstructorInfo) continue;
            if (allowed.Contains(member.Name)) continue;
            extras.Add($"{member.MemberType.ToString().ToLowerInvariant()} {member.Name}");
        }

        if (extras.Count == 0)
        {
            report.AppendLine("  extras           0 — every public member is one the ADR names");
        }
        else
        {
            report.AppendLine($"  extras           {extras.Count} public member(s) the ADR does not name:");
            foreach (string extra in extras) report.AppendLine($"    {extra}");
        }

        divergences += extras.Count;

        Console.Write(report);
        Console.WriteLine();
        Console.WriteLine($"cpu-surface: {divergences} divergence(s) from ADR 0015 §1"
                          + (audit ? " (audit: exit 0; the freeze row goes live when the surface is the manual's)" : string.Empty));

        if (audit) return 0;
        return divergences == 0 ? 0 : 1;
    }
}
