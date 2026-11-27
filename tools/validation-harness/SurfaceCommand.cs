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
        string? familiesDirectory = null;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--audit":
                    audit = true;
                    break;
                case "--families":
                    if (++index >= args.Length) return Program.Unknown(args[index - 1]);
                    familiesDirectory = args[index];
                    break;
                default:
                    return Program.Unknown(args[index]);
            }
        }

        if (familiesDirectory is not null)
        {
            return FamilyAudit(familiesDirectory);
        }


        /// <summary>
        /// <c>--families DIR</c> — the decode's witness. For every corpus record whose own name carries
        /// its file's mnemonic (an <c>ADDQ</c> record inside <c>ADD.b</c> does not), the decoder must
        /// answer that family for the record's opcode word. Legality is not its business: that is the
        /// <c>.lst</c>'s 65 536-opcode golden (#89).
        /// </summary>
        static bool familyIsTwoOperand(string mnemonic) =>
            mnemonic is "ADD" or "SUB" or "AND" or "OR" or "EOR" or "CMP";

        static int FamilyAudit(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Console.Error.WriteLine($"cpu-surface: no corpus at {directory}");
                return 1;
            }

            long total = 0, checkedRecords = 0, mismatched = 0;
            var byFamily = new SortedDictionary<string, (long Checked, long Bad)>(StringComparer.Ordinal);
            var examples = new List<string>();
            var perFamily = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (string path in Directory.GetFiles(directory)
                         .Where(p => p.EndsWith(".json", StringComparison.Ordinal)
                                     || p.EndsWith(".json.gz", StringComparison.Ordinal))
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                string file = Path.GetFileName(path);
                if (file.EndsWith(".gz", StringComparison.Ordinal)) file = file[..^3];
                file = file[..^5];
                int dot = file.IndexOf('.');
                string fileMnemonic = (dot < 0 ? file : file[..dot]).ToUpperInvariant();
                string fileSuffix = dot < 0 ? string.Empty : file[dot..].ToUpperInvariant();

                // a file's own name is not always its family's: `MOVE.q` is MOVEQ, `UNLINK` is UNLK,
                // `ANDItoCCR`/`ANDItoSR` are ANDI, and the two USP directions are MOVEUSP
                string expected = fileMnemonic switch
                {
                    "MOVE" when fileSuffix == ".Q" => "MOVEQ",
                    "UNLINK" => "UNLK",
                    "MOVEFROMUSP" or "MOVETOUSP" => "MOVEUSP",
                    "ANDITOCCR" or "ANDITOSR" => "ANDI",
                    "ORITOCCR" or "ORITOSR" => "ORI",
                    "EORITOCCR" or "EORITOSR" => "EORI",
                    _ => fileMnemonic,
                };

                using Stream stream = File.OpenRead(path);
                using Stream input = path.EndsWith(".gz", StringComparison.Ordinal)
                    ? new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress)
                    : stream;
                using var document = System.Text.Json.JsonDocument.Parse(input);
                if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) continue;

                foreach (System.Text.Json.JsonElement test in document.RootElement.EnumerateArray())
                {
                    total++;
                    string name = test.GetProperty("name").GetString() ?? string.Empty;
                    int space = name.IndexOf(' ');
                    if (space < 0 || name.Length < space + 2) continue;
                    string body = name[(space + 1)..].TrimStart();
                    int end = 0;
                    while (end < body.Length && (char.IsLetter(body[end]) || body[end] == '.')) end++;
                    if (end == 0) continue;
                    string mnemonic = body[..end];
                    int suffix = mnemonic.IndexOf('.');
                    string bare = (suffix < 0 ? mnemonic : mnemonic[..suffix]).ToUpperInvariant();
                    if (bare != fileMnemonic) continue;

                    ushort opcode = (ushort)test.GetProperty("initial").GetProperty("prefetch")[0].GetUInt32();

                    // the additive families share their file with the quick and immediate forms
                    // (`ADD.b` carries ADDQ and ADDI records, which MAME names "ADD.b 2, …"): those are
                    // other families by construction, and this filter is what says so — it is not a
                    // decode rule, and the set is named here rather than derived from the decoder
                    if (familyIsTwoOperand(bare) && (opcode >> 12) is 0x5 or 0x0) continue;
                    if (bare == "CMP" && (opcode & 0xF138) == 0xB108) continue;   // the CMPM records MAME names "CMP"
                    string want = expected;
                    if (want == "BCC")
                    {
                        want = ((opcode >> 8) & 0xF) switch { 0x0 => "BRA", 0x1 => "BSR", _ => "BCC" };
                    }
                    TheST.Machine.Cpu.M68000.Decode.TryDecode(opcode, out TheST.Machine.Cpu.M68000.Decoded decoded);
                    string family = decoded.Family.ToString().ToUpperInvariant();
                    checkedRecords++;
                    var entry = byFamily.TryGetValue(want, out var seen) ? seen : default;
                    if (family != want)
                    {
                        mismatched++;
                        entry.Bad++;
                        int shown = perFamily.TryGetValue(want, out int already) ? already : 0;
                        if (shown < 3)
                        {
                            examples.Add($"    {want,-10} opcode {opcode:x4} line {opcode >> 12:X} -> {family,-10} {name}");
                            perFamily[want] = shown + 1;
                        }
                    }

                    entry.Checked++;
                    byFamily[want] = entry;
                }
            }

            Console.WriteLine($"cpu-surface --families {directory}");
            foreach ((string family, (long checkedCount, long bad)) in byFamily)
            {
                Console.WriteLine($"  {family,-12} {checkedCount - bad,7}/{checkedCount,-7}"
                                  + (bad == 0 ? " ok" : $"  {bad} MISMATCHED"));
            }

            if (examples.Count > 0)
            {
                Console.WriteLine("  the first mismatches:");
                foreach (string line in examples) Console.WriteLine(line);
            }

            Console.WriteLine();
            Console.WriteLine($"decode: {checkedRecords - mismatched} / {checkedRecords} of {total} records "
                              + $"whose name carries their file's mnemonic decode to it, {mismatched} mismatched");
            return mismatched == 0 ? 0 : 1;
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
        // a nested type is a place, reached through an accessor: `Emulator` for Instruments (§6; C#
        // forbids a member and a nested type sharing a name), `Pins` for the pads
        allowed.Add("Emulator");
        allowed.Add("Pads");
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
