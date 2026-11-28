// ============================================================================================
// templates/per-site-probe.cs — the skeleton for a `#if DEBUG` per-site tally in the core.
//
// Copy it to src/TheST.Machine/Cpu/M68000.<Thing>Probe.cs, add one `Probe("<site>", <actual>[, <c1>, <c2>])`
// call at every place that reads or produces the quantity you are replacing, and add the printer call to
// tools/validation-harness/CpuTestsCommand.cs at the end of the corpus run:
//
//     #if DEBUG
//         TheST.Machine.Cpu.M68000.ProbeReport();
//     #endif
//
// Then `cpu-tests --corpus mame --quiet 2>probe.err` (10 s) and read `grep '^tally' probe.err`: one line per
// site, checks / mismatches per candidate. A candidate with 0 mismatches over a whole corpus run is the
// derivation; anything else is bucketed by the bounded sample lines, which carry the state you need to name
// the exception (the fill flags, the fault flag, the opcode).
//
// RULES, and they are the whole value of the instrument:
//  · `ProbeReport` must be `public` while it lives: `tools/validation-harness` is a separate assembly and
//    cannot see an `internal` member. Delete the file and the harness hook in the same session — a public
//    member fails the surface freeze test, so a probe must never reach a commit.
//  · Print a bounded sample per site (a few hundred lines), never every mismatch; the tally is the number,
//    the sample is only for naming the class.
//  · It answers "which expression is exact", not "is the tree right": the corpus's two columns and the folds
//    stay the verdict, and the probe is deleted before the gate runs.
// ============================================================================================
#if DEBUG
namespace TheST.Machine.Cpu;

public sealed partial class M68000
{
    internal static readonly System.Collections.Generic.Dictionary<string, long[]> ProbeTally = new();

    /// <summary>Record <paramref name="actual"/> against the site (a one-candidate probe).</summary>
    internal void Probe(string site, uint actual)
    {
        Probe(site, actual, actual, actual);
    }

    internal void Probe(string site, uint actual, uint first, uint second)
    {
        if (!ProbeTally.TryGetValue(site, out long[]? row))
        {
            ProbeTally[site] = row = new long[3];
        }

        row[0]++;                              // checks at this site
        if (first != actual) row[1]++;         // mismatches of candidate 1
        if (second != actual) row[2]++;        // mismatches of candidate 2

        if ((first != actual || second != actual) && row[0] <= 200)
        {
            System.Console.Error.WriteLine(
                $"probe {site} actual={actual:x6} 1={first:x6} 2={second:x6} " +
                $"au={_au:x6} pc={_pc:x6} at={_at:x6} irc={_ircValid} qs={_queueShort} op={_opcode:x4}");
        }
    }

    /// <summary>The tally, printed by the harness at the end of a run (one line per site).</summary>
    public static void ProbeReport()
    {
        foreach (var pair in ProbeTally)
        {
            long[] row = pair.Value;
            System.Console.Error.WriteLine($"tally {pair.Key} checks={row[0]} bad1={row[1]} bad2={row[2]}");
        }
    }
}
#endif
